// このファイルは、比較用のチャンク内ローカルTilemapについて、原点変更で表示と構築進捗を再利用できることを検証します。
// ECSの地形生成規則ではなく、Presenterが所有する表示キャッシュ・空きプール・World切替時の寿命を対象とします。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Farmware.Simulation;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace Farmware.WorldGeneration.PlayModeTests
{
    /// <summary>
    /// 最小限のGridとPresenterを用意し、チャンク表示の再配置・消去・返却をPlayMode Test Runnerで検証します。
    /// 非公開キャッシュは反射で確認し、製品APIへテスト専用の状態公開を追加しません。
    /// </summary>
    public sealed class ChunkLocalTilemapPresentationTests
    {
        // Fixtureが各テスト前に作り、テスト後に確実に破棄する固定基準Gridです。
        private GameObject gridObject;

        // 表示原点を所有するCameraです。各テストの相対移動によってだけ原点を変更します。
        private GameObject cameraObject;

        // Scene上のGround見本です。新方式ではこのセルへ直接書かれないことも検証します。
        private Tilemap template;

        // 表示単位とプールを所有する、テスト寿命だけ有効なPresenterです。
        private ChunkTilemapPresenter presenter;

        // 絶対Camera位置を保つ自動原点変更を実行する、CameraObject所有のControllerです。
        private CameraPanController controller;

        // セルの消失や混入を識別するテスト専用Tileです。Fixtureが所有し、終了時に破棄します。
        private Tile tile;

        /// <summary>
        /// 各テスト前に空の表示とCameraを作り、比較方式を有効にします。ECS WorldやSceneアセットは変更しません。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            gridObject = new GameObject("Chunk Local Test Grid", typeof(Grid));
            GameObject layer = new GameObject("Chunk Local Test Ground", typeof(Tilemap), typeof(TilemapRenderer));
            layer.transform.SetParent(gridObject.transform, false);
            template = layer.GetComponent<Tilemap>();
            template.GetComponent<TilemapRenderer>().mode = TilemapRenderer.Mode.Individual;
            template.GetComponent<TilemapRenderer>().sortOrder = TilemapRenderer.SortOrder.TopLeft;
            presenter = layer.AddComponent<ChunkTilemapPresenter>();
            Field("targetTilemap").SetValue(presenter, template);
            presenter.ConfigureChunkLocalPresentation(true);

            cameraObject = new GameObject("Chunk Local Test Camera", typeof(Camera));
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            controller = cameraObject.AddComponent<CameraPanController>();
            presenter.ConfigureDisplayOrigin(controller);
            tile = ScriptableObject.CreateInstance<Tile>();
        }

        /// <summary>
        /// 各テスト後に表示ルート・影・Camera・Tileを破棄し、失敗時も後続テストへ所有Objectを残しません。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(gridObject);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(tile);
        }

        /// <summary>新規Componentの保存前の既定値がONで、生成ツールからの新規追加も採用方式になることを保証します。</summary>
        [Test]
        public void NewPresenter_DefaultsToChunkLocalTilemaps()
        {
            GameObject temporary = new GameObject("Default Presenter Test");
            try
            {
                var component = temporary.AddComponent<ChunkTilemapPresenter>();
                Assert.That(Field("useChunkLocalTilemaps").GetValue(component), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(temporary);
            }
        }

        /// <summary>
        /// 正・負・斜め・遠方の自動原点変更で、同じTilemapとセル内容、完成済み集合、部分構築の進捗を保持することを保証します。
        /// </summary>
        /// <param name="originX">最初の絶対表示原点Xです。単位はタイルです。</param>
        /// <param name="originY">最初の絶対表示原点Yです。単位はタイルです。</param>
        /// <param name="nextX">自動変更を起こす旧原点からの相対Camera Xです。</param>
        /// <param name="nextY">自動変更を起こす旧原点からの相対Camera Yです。</param>
        /// <param name="chunkX">移動先で継続表示する絶対チャンクXです。</param>
        /// <param name="chunkY">移動先で継続表示する絶対チャンクYです。</param>
        [TestCase(0, 0, 544.5f, 0.5f, 8, 0)]
        [TestCase(0, 0, -544.5f, -544.5f, -9, -9)]
        [TestCase(1000000000, -1000000000, 544.5f, 0.5f, 15625008, -15625000)]
        public void AutomaticRebase_PreservesTilemapsAndApplyProgress(
            int originX, int originY, float nextX, float nextY, int chunkX, int chunkY)
        {
            controller.SetAbsoluteWorldPosition(originX + 0.5d, originY + 0.5d);
            Invoke("SynchronizeDisplayOrigin");
            ChunkCoordinate coordinate = new ChunkCoordinate(chunkX, chunkY);
            Tilemap original = GetTilemap(coordinate, true);
            Vector3Int cell = new Vector3Int(4, 5, 0);
            original.SetTile(cell, tile);
            HashSet<ChunkCoordinate> displayed = (HashSet<ChunkCoordinate>)Field("displayedCoordinates").GetValue(presenter);
            displayed.Add(coordinate);

            // 別チャンクを3サブチャンク反映済みとして用意し、原点変更で先頭へ戻されないことを確認します。
            IDictionary progress = (IDictionary)Field("applyProgressByCoordinate").GetValue(presenter);
            Type progressType = typeof(ChunkTilemapPresenter).GetNestedType("ChunkApplyProgress", BindingFlags.NonPublic);
            object partial = Activator.CreateInstance(progressType, new object[] { Entity.Null });
            progressType.GetField("NextSubChunkIndex").SetValue(partial, 3);
            ChunkCoordinate partialCoordinate = new ChunkCoordinate(chunkX + 1, chunkY);
            progress.Add(partialCoordinate, partial);
            Tilemap partialTilemap = GetTilemap(partialCoordinate, true);

            controller.SetWorldPosition(new Vector2(nextX, nextY));
            Invoke("SynchronizeDisplayOrigin");

            Assert.That(GetTilemap(coordinate, false), Is.SameAs(original));
            Assert.That(original.GetTile(cell), Is.SameAs(tile));
            Assert.That(template.GetTile(cell), Is.Null, "見本のTilemapへチャンク表示を書き込んではいけません。");
            Assert.That(displayed.Contains(coordinate), Is.True);
            Assert.That(progress[partialCoordinate], Is.SameAs(partial));
            Assert.That(progressType.GetField("NextSubChunkIndex").GetValue(partial), Is.EqualTo(3));
            Assert.That(GetTilemap(partialCoordinate, false), Is.SameAs(partialTilemap));
            Assert.That(((ICollection)Field("staleOriginClearQueue").GetValue(presenter)).Count, Is.Zero);
            Assert.That(gridObject.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(original.GetComponent<TilemapRenderer>().mode, Is.EqualTo(TilemapRenderer.Mode.Individual));
            Assert.That(original.GetComponent<TilemapRenderer>().sortOrder, Is.EqualTo(TilemapRenderer.SortOrder.TopLeft));

            // 新原点は対象チャンクの左下です。ローカルセルの世界位置が原点変更後も正しい小さい値になります。
            Assert.That(original.CellToWorld(Vector3Int.zero), Is.EqualTo(Vector3.zero));
        }

        /// <summary>
        /// 消去途中では表示を貸し出さず、16サブチャンク消去後に同じ空のTilemapを別チャンクへ再利用できることを保証します。
        /// </summary>
        [Test]
        public void ClearQueue_ReturnsOnlyFullyClearedPresentationToPool()
        {
            Invoke("SynchronizeDisplayOrigin");
            ChunkCoordinate coordinate = new ChunkCoordinate(0, 0);
            Tilemap original = GetTilemap(coordinate, true);
            original.SetTile(new Vector3Int(63, 63, 0), tile);
            ((HashSet<ChunkCoordinate>)Field("displayedCoordinates").GetValue(presenter)).Add(coordinate);
            Invoke("CancelApplyAndScheduleClearIfNeeded", coordinate);

            Invoke("ProcessClearQueue", 8);
            Assert.That(((ICollection)Field("pooledChunkLocalPresentations").GetValue(presenter)).Count, Is.Zero);
            Assert.That(original.gameObject.activeInHierarchy, Is.False);
            Assert.That(original.GetTile(new Vector3Int(63, 63, 0)), Is.SameAs(tile));

            Invoke("ProcessClearQueue", 8);
            Assert.That(((ICollection)Field("pooledChunkLocalPresentations").GetValue(presenter)).Count, Is.EqualTo(1));
            Tilemap reused = GetTilemap(new ChunkCoordinate(-1, 0), true);
            Assert.That(reused, Is.SameAs(original));
            Assert.That(reused.GetTile(new Vector3Int(63, 63, 0)), Is.Null);
            Assert.That(reused.CellToWorld(Vector3Int.zero).x, Is.EqualTo(-64f));
            Assert.That(reused.gameObject.activeInHierarchy, Is.True);
        }

        /// <summary>
        /// 上下の別Tilemapへ同じSorting Orderを保ったまま小さいZ差を付け、奥の断面が手前の土地へ重ならない描画順を保証します。
        /// XYのセル位置と固定World Gridを変更しないことも確認します。
        /// </summary>
        [Test]
        public void AdjacentChunkRows_UseOrderedRelativeDepthWithoutChangingLayers()
        {
            Invoke("SynchronizeDisplayOrigin");
            Tilemap lower = GetTilemap(new ChunkCoordinate(0, -1), true);
            Tilemap center = GetTilemap(new ChunkCoordinate(0, 0), true);
            Tilemap upper = GetTilemap(new ChunkCoordinate(0, 1), true);
            Assert.That(lower.transform.position.y, Is.EqualTo(-64f));
            Assert.That(upper.transform.position.y, Is.EqualTo(64f));
            Assert.That(lower.transform.position.z, Is.LessThan(center.transform.position.z));
            Assert.That(upper.transform.position.z, Is.GreaterThan(center.transform.position.z));
            Assert.That(Mathf.Abs(lower.transform.position.z), Is.LessThan(0.01f));
            Assert.That(lower.GetComponent<TilemapRenderer>().sortingOrder, Is.EqualTo(template.GetComponent<TilemapRenderer>().sortingOrder));
            Assert.That(upper.GetComponent<TilemapRenderer>().sortingOrder, Is.EqualTo(template.GetComponent<TilemapRenderer>().sortingOrder));
            Assert.That(gridObject.transform.position, Is.EqualTo(Vector3.zero));
        }

        /// <summary>
        /// 空きプール上限0では返却物を保持せず、方式切替とWorld切替用の即時掃除でも全表示を解放することを保証します。
        /// </summary>
        [Test]
        public void ImmediateCleanupAndPoolLimit_DoNotRetainOldPresentations()
        {
            Field("maximumPooledChunkPresentations").SetValue(presenter, 0);
            Invoke("SynchronizeDisplayOrigin");
            ChunkCoordinate coordinate = new ChunkCoordinate(0, 0);
            GetTilemap(coordinate, true).SetTile(Vector3Int.zero, tile);
            ((HashSet<ChunkCoordinate>)Field("displayedCoordinates").GetValue(presenter)).Add(coordinate);
            Invoke("CancelApplyAndScheduleClearIfNeeded", coordinate);
            Invoke("ProcessClearQueue", 16);
            Assert.That(((ICollection)Field("pooledChunkLocalPresentations").GetValue(presenter)).Count, Is.Zero);
            Assert.That(((IDictionary)Field("chunkLocalPresentations").GetValue(presenter)).Count, Is.Zero);

            GetTilemap(new ChunkCoordinate(1, 0), true).SetTile(Vector3Int.zero, tile);
            presenter.ConfigureChunkLocalPresentation(false);
            Assert.That(((IDictionary)Field("chunkLocalPresentations").GetValue(presenter)).Count, Is.Zero);
            Assert.That(((ICollection)Field("pooledChunkLocalPresentations").GetValue(presenter)).Count, Is.Zero);
            Assert.That(template.GetTile(Vector3Int.zero), Is.Null);
        }

        /// <summary>
        /// Surfaceのチャンク表示と反映途中キャッシュを準備してUnderwaterへ切り替え、通常の4サブチャンク予算を待たず、
        /// 同じ呼び出し内ですべての旧表示・Queue・貸出Tilemapを破棄することを保証します。
        /// </summary>
        [Test]
        public void LayerChanged_ClearsPreviousLayerPresentationImmediately()
        {
            Invoke("SynchronizeDisplayOrigin");
            Assert.That(Invoke("SynchronizePresentedLayer", WorldLayer.Surface), Is.False);

            ChunkCoordinate coordinate = new ChunkCoordinate(WorldLayer.Surface, 0, 0);
            Tilemap oldTilemap = GetTilemap(coordinate, true);
            oldTilemap.SetTile(Vector3Int.zero, tile);
            ((HashSet<ChunkCoordinate>)Field("displayedCoordinates").GetValue(presenter)).Add(coordinate);

            Assert.That(Invoke("SynchronizePresentedLayer", WorldLayer.Underwater), Is.True);
            Assert.That(oldTilemap.gameObject.activeInHierarchy, Is.False,
                "レイヤー変更フレーム内に旧Surface表示を無効化する必要があります。");
            Assert.That(((IDictionary)Field("chunkLocalPresentations").GetValue(presenter)).Count, Is.Zero);
            Assert.That(
                ((HashSet<ChunkCoordinate>)Field("displayedCoordinates").GetValue(presenter)).Count,
                Is.Zero);
            Assert.That(((ICollection)Field("applyQueue").GetValue(presenter)).Count, Is.Zero);
            Assert.That(((ICollection)Field("clearQueue").GetValue(presenter)).Count, Is.Zero);
        }

        /// <summary>
        /// 異なるECS Worldへ切り替えたとき、表示中と空きプールを両方破棄し、同座標の古い地形を再利用しないことを保証します。
        /// </summary>
        [Test]
        public void WorldChanged_DiscardsAssignedAndPooledTilemaps()
        {
            World previousWorld = World.DefaultGameObjectInjectionWorld;
            // Queryは各Worldが所有します。テスト終了時にはWorldをDisposeし、Default参照を必ず元へ戻します。
            using World firstWorld = new World("Chunk Local First Test World");
            using World secondWorld = new World("Chunk Local Second Test World");
            try
            {
                World.DefaultGameObjectInjectionWorld = firstWorld;
                Invoke("TryEnsureQueries");
                Invoke("SynchronizeDisplayOrigin");
                ChunkCoordinate pooledCoordinate = new ChunkCoordinate(0, 0);
                GetTilemap(pooledCoordinate, true).SetTile(Vector3Int.zero, tile);
                HashSet<ChunkCoordinate> displayed = (HashSet<ChunkCoordinate>)Field("displayedCoordinates").GetValue(presenter);
                displayed.Add(pooledCoordinate);
                Invoke("CancelApplyAndScheduleClearIfNeeded", pooledCoordinate);
                Invoke("ProcessClearQueue", 16);
                Assert.That(((ICollection)Field("pooledChunkLocalPresentations").GetValue(presenter)).Count, Is.EqualTo(1));

                // 2つの表示単位を借り、一方だけ返して、表示中と空きプールの両方が存在する状態を作ります。
                Invoke("GetTilemapForChunk", new ChunkCoordinate(1, 0), template, true);
                Tilemap assigned = GetTilemap(new ChunkCoordinate(2, 0), true);
                assigned.SetTile(Vector3Int.zero, tile);
                displayed.Add(new ChunkCoordinate(1, 0));
                displayed.Add(new ChunkCoordinate(2, 0));
                Invoke("CancelApplyAndScheduleClearIfNeeded", new ChunkCoordinate(1, 0));
                Invoke("ProcessClearQueue", 16);
                Assert.That(((ICollection)Field("pooledChunkLocalPresentations").GetValue(presenter)).Count, Is.EqualTo(1));

                World.DefaultGameObjectInjectionWorld = secondWorld;
                Invoke("TryEnsureQueries");
                Assert.That(((IDictionary)Field("chunkLocalPresentations").GetValue(presenter)).Count, Is.Zero);
                Assert.That(((ICollection)Field("pooledChunkLocalPresentations").GetValue(presenter)).Count, Is.Zero);
                Assert.That(assigned.gameObject.activeInHierarchy, Is.False);
            }
            finally
            {
                World.DefaultGameObjectInjectionWorld = previousWorld;
            }
        }

        /// <summary>空→空だけを省略し、末尾に1セルだけある配列も書き、非空→空では実際の消去を行うことを保証します。</summary>
        [Test]
        public void SparseWrite_SkipsOnlyEmptyToEmpty()
        {
            Invoke("SynchronizeDisplayOrigin");
            ChunkCoordinate coordinate = new ChunkCoordinate(0, 0);
            TileBase[] empty = new TileBase[256];
            TileBase[] oneTile = new TileBase[256];
            oneTile[255] = tile;
            Assert.That(WriteLayer(coordinate, template, 0, empty), Is.False);
            Assert.That(WriteLayer(coordinate, template, 0, oneTile), Is.True);
            Tilemap map = GetTilemap(coordinate, false);
            Assert.That(map.GetTile(new Vector3Int(15, 15, 0)), Is.SameAs(tile));
            Assert.That(WriteLayer(coordinate, template, 0, empty), Is.True, "既存タイルを消すnull書込は省略できません。");
            Assert.That(map.GetUsedTilesCount(), Is.Zero);
            Assert.That(WriteLayer(coordinate, template, 0, empty), Is.False);
        }

        /// <summary>2レイヤーと先頭・末尾サブチャンクを別々に反映し、空状態の履歴が混ざらないことを保証します。</summary>
        [Test]
        public void SparseWrite_KeepsLayerAndSubChunkHistorySeparate()
        {
            GameObject layer = new GameObject("Sparse Resource Layer", typeof(Tilemap), typeof(TilemapRenderer));
            layer.transform.SetParent(gridObject.transform, false);
            Tilemap resource = layer.GetComponent<Tilemap>();
            Field("resourceTilemap").SetValue(presenter, resource);
            Invoke("SynchronizeDisplayOrigin");
            ChunkCoordinate coordinate = new ChunkCoordinate(-1, -1);
            TileBase[] empty = new TileBase[256];
            TileBase[] oneTile = new TileBase[256];
            oneTile[0] = tile;
            Assert.That(WriteLayer(coordinate, template, 0, oneTile), Is.True);
            Assert.That(WriteLayer(coordinate, resource, 15, oneTile), Is.True);
            Assert.That(WriteLayer(coordinate, template, 15, empty), Is.False);
            Assert.That(WriteLayer(coordinate, resource, 0, empty), Is.False);
            Assert.That(WriteLayer(coordinate, template, 0, empty), Is.True);
            Tilemap resourceMap = (Tilemap)Invoke("GetTilemapForChunk", coordinate, resource, false);
            Assert.That(resourceMap.GetTile(new Vector3Int(48, 48, 0)), Is.SameAs(tile));
            Assert.That(WriteLayer(coordinate, resource, 15, empty), Is.True);
            Assert.That(resourceMap.GetUsedTilesCount(), Is.Zero);
        }

        /// <summary>部分反映した領域を全消去してプールへ返し、再貸出時には空履歴から安全に反映できることを保証します。</summary>
        [Test]
        public void SparseWrite_PoolReuseStartsWithClearedHistory()
        {
            Invoke("SynchronizeDisplayOrigin");
            ChunkCoordinate coordinate = new ChunkCoordinate(0, 0);
            TileBase[] oneTile = new TileBase[256];
            oneTile[0] = tile;
            WriteLayer(coordinate, template, 15, oneTile);
            Tilemap original = GetTilemap(coordinate, false);
            ((HashSet<ChunkCoordinate>)Field("displayedCoordinates").GetValue(presenter)).Add(coordinate);
            Invoke("CancelApplyAndScheduleClearIfNeeded", coordinate);
            Invoke("ProcessClearQueue", 8);
            Assert.That(original.GetTile(new Vector3Int(48, 48, 0)), Is.SameAs(tile));
            Assert.That(((ICollection)Field("pooledChunkLocalPresentations").GetValue(presenter)).Count, Is.Zero);
            Invoke("ProcessClearQueue", 8);
            ChunkCoordinate nextCoordinate = new ChunkCoordinate(1, 0);
            Assert.That(WriteLayer(nextCoordinate, template, 15, new TileBase[256]), Is.False);
            Assert.That(GetTilemap(nextCoordinate, false), Is.SameAs(original));
            Assert.That(original.GetUsedTilesCount(), Is.Zero);
            Assert.That(WriteLayer(nextCoordinate, template, 15, oneTile), Is.True);
        }

        /// <summary>共有方式では空配列も従来どおり書き、比較方式への切替時には新しい空履歴を使うことを保証します。</summary>
        [Test]
        public void SparseWrite_SharedModeKeepsUnconditionalWrite()
        {
            presenter.ConfigureChunkLocalPresentation(false);
            Invoke("SynchronizeDisplayOrigin");
            ChunkCoordinate coordinate = new ChunkCoordinate(0, 0);
            TileBase[] empty = new TileBase[256];
            template.SetTile(Vector3Int.zero, tile);
            Assert.That(WriteLayer(coordinate, template, 0, empty), Is.True);
            Assert.That(template.GetTile(Vector3Int.zero), Is.Null);
            Assert.That(WriteLayer(coordinate, template, 0, empty), Is.True);
            presenter.ConfigureChunkLocalPresentation(true);
            Assert.That(WriteLayer(coordinate, template, 0, empty), Is.False);
        }

        /// <summary>正負の自動原点変更を挟んでも非空履歴を維持し、その後のnull書込で旧セルを消せることを保証します。</summary>
        /// <param name="relativeX">自動原点変更を起こす相対Xです。</param>
        /// <param name="chunkX">移動後も同じ表示を使うチャンクXです。</param>
        [TestCase(544.5f, 8)]
        [TestCase(-544.5f, -9)]
        public void SparseWrite_AutomaticRebasePreservesHistory(float relativeX, int chunkX)
        {
            controller.SetAbsoluteWorldPosition(0.5d, 0.5d);
            Invoke("SynchronizeDisplayOrigin");
            ChunkCoordinate coordinate = new ChunkCoordinate(chunkX, 0);
            TileBase[] oneTile = new TileBase[256];
            oneTile[0] = tile;
            WriteLayer(coordinate, template, 0, oneTile);
            Tilemap original = GetTilemap(coordinate, false);
            ((HashSet<ChunkCoordinate>)Field("displayedCoordinates").GetValue(presenter)).Add(coordinate);
            controller.SetWorldPosition(new Vector2(relativeX, 0.5f));
            Invoke("SynchronizeDisplayOrigin");
            Assert.That(GetTilemap(coordinate, false), Is.SameAs(original));
            Assert.That(WriteLayer(coordinate, template, 0, new TileBase[256]), Is.True);
            Assert.That(original.GetTile(Vector3Int.zero), Is.Null);
        }

        /// <summary>
        /// 完成した画面外データを先に作り、可視優先・完成近傍が揃う範囲だけの予約・Inactive再利用・範囲外消去を保証します。
        /// 共有方式とローカル方式で同じ有限範囲を使い、Observer自身は変更しません。
        /// </summary>
        /// <param name="local">チャンク別Tilemapを使う場合はtrueです。</param>
        /// <param name="center">可視中心の絶対チャンク座標です。正負・遠方を確認します。</param>
        [TestCase(true, 0)]
        [TestCase(false, 0)]
        [TestCase(true, -7)]
        [TestCase(false, 15625000)]
        public void PresentationPreload_SchedulesVisibleFirstAndRetainsOnlyOneChunkMargin(bool local, int center)
        {
            World previous = World.DefaultGameObjectInjectionWorld;
            using World world = new World("Presentation Preload Test");
            try
            {
                World.DefaultGameObjectInjectionWorld = world;
                presenter.ConfigureChunkLocalPresentation(local);
                EntityManager manager = world.EntityManager;
                // Queryの順序が画面外を先に返しても、可視1件を先頭に予約できるよう外側から作ります。
                for (int y = -2; y <= 2; y++)
                {
                    for (int x = -2; x <= 2; x++)
                    {
                        CreateCompletedChunk(manager, center + x, center + y, ChunkGenerationPhase.Inactive);
                    }
                }

                Assert.That(Invoke("TryEnsureQueries"), Is.True);
                ChunkStreamingObserver observer = new ChunkStreamingObserver
                {
                    Layer = WorldLayer.Surface,
                    MinimumVisibleChunkX = center, MaximumVisibleChunkX = center,
                    MinimumVisibleChunkY = center, MaximumVisibleChunkY = center
                };
                Invoke("CollectPresentationWork", observer, false);
                var queue = (Queue<ChunkCoordinate>)Field("applyQueue").GetValue(presenter);
                var progress = (IDictionary)Field("applyProgressByCoordinate").GetValue(presenter);
                Assert.That(queue.Count, Is.EqualTo(9), "5×5の保持データ全体ではなく、可視1＋余白8件だけを準備します。");
                Assert.That(queue.Peek(), Is.EqualTo(new ChunkCoordinate(center, center)));

                // 完成済みの1件が次に可視へ入っても、消去・先頭からの反映を予約し直してはいけません。
                ChunkCoordinate entering = new ChunkCoordinate(center + 1, center);
                progress.Remove(entering);
                var displayed = (HashSet<ChunkCoordinate>)Field("displayedCoordinates").GetValue(presenter);
                displayed.Add(entering);
                observer.MinimumVisibleChunkX = center + 1;
                observer.MaximumVisibleChunkX = center + 1;
                Invoke("CollectPresentationWork", observer, false);
                Assert.That(displayed.Contains(entering), Is.True);
                Assert.That(progress.Contains(entering), Is.False);
                var pendingClear = (HashSet<ChunkCoordinate>)Field("pendingClearCoordinates").GetValue(presenter);
                Assert.That(pendingClear.Contains(entering), Is.False);
                Assert.That(pendingClear.Contains(new ChunkCoordinate(center - 1, center)), Is.True);
                Assert.That(
                    progress.Contains(new ChunkCoordinate(center + 2, center)),
                    Is.False,
                    "右外周のさらに右隣が未ロードなら、確定前地形で補完せず先行表示を待つ必要があります。");
                Assert.That(progress.Contains(new ChunkCoordinate(center + 3, center)), Is.False);
            }
            finally
            {
                // Queryの所有Worldを破棄する前にPresenter側の参照を解放し、元のDefault Worldを復元します。
                presenter.enabled = false;
                World.DefaultGameObjectInjectionWorld = previous;
            }
        }

        /// <summary>
        /// 同期再構築は可視だけを予約し、通常時は生成中を読まず、完成した先行反映も1回の予算4を越えないことを保証します。
        /// </summary>
        [Test]
        public void PresentationPreload_VisibleOnlyRebuildAndApplyRespectBudget()
        {
            World previous = World.DefaultGameObjectInjectionWorld;
            using World world = new World("Presentation Preload Budget Test");
            try
            {
                World.DefaultGameObjectInjectionWorld = world;
                EntityManager manager = world.EntityManager;
                // 可視中心と外側1チャンクの各表示が完成3×3近傍を借用できるよう、保持範囲相当の5×5を用意します。
                for (int y = -2; y <= 2; y++)
                {
                    for (int x = -2; x <= 2; x++)
                    {
                        CreateCompletedChunk(
                            manager,
                            x,
                            y,
                            x == 0 && y == 0
                                ? ChunkGenerationPhase.ReadyToApply
                                : ChunkGenerationPhase.Inactive);
                    }
                }
                Assert.That(Invoke("TryEnsureQueries"), Is.True);
                Invoke("SynchronizeDisplayOrigin");
                ChunkStreamingObserver observer = new ChunkStreamingObserver
                {
                    Layer = WorldLayer.Surface
                };
                Invoke("CollectPresentationWork", observer, true);
                var progress = (IDictionary)Field("applyProgressByCoordinate").GetValue(presenter);
                Assert.That(progress.Count, Is.EqualTo(1), "原点変更の同期経路へ余白を含めません。");
                Invoke("CollectPresentationWork", observer, false);
                Assert.That(progress.Count, Is.EqualTo(9), "通常経路は完成近傍を持つ外側1チャンクまで予約します。");
                Assert.That(Invoke("ProcessApplyQueue", 4), Is.EqualTo(0));
                object partial = progress[new ChunkCoordinate(0, 0)];
                Assert.That(partial.GetType().GetField("NextSubChunkIndex").GetValue(partial), Is.EqualTo(4));
                object waiting = progress[new ChunkCoordinate(1, 0)];
                Assert.That(waiting.GetType().GetField("NextSubChunkIndex").GetValue(waiting), Is.EqualTo(0));
            }
            finally
            {
                presenter.enabled = false;
                World.DefaultGameObjectInjectionWorld = previous;
            }
        }

        /// <summary>表示予約に必要なComponentと確定BufferをテストWorldへ作り、指定状態を設定します。</summary>
        /// <param name="manager">Fixtureの一時WorldのEntityManagerです。</param>
        /// <param name="x">絶対チャンクXです。</param>
        /// <param name="y">絶対チャンクYです。</param>
        /// <param name="phase">完成・画面外・生成中を識別する状態です。</param>
        private static void CreateCompletedChunk(EntityManager manager, int x, int y, ChunkGenerationPhase phase)
        {
            Entity entity = manager.CreateEntity(typeof(ChunkCoordinateComponent), typeof(ChunkGenerationStatus));
            manager.SetComponentData(
                entity,
                new ChunkCoordinateComponent
                {
                    Layer = WorldLayer.Surface,
                    X = x,
                    Y = y
                });
            manager.SetComponentData(entity, new ChunkGenerationStatus { Phase = phase });
            manager.AddBuffer<GeneratedTerrainCell>(entity).ResizeUninitialized(WorldGridConstants.TilesPerChunk);
            manager.AddBuffer<GeneratedResourceTile>(entity).ResizeUninitialized(WorldGridConstants.TilesPerChunk);
            // 現行Presenterが読むBlock・Liquid地形Bufferをゼロ初期化し、分類結果を未初期化メモリへ依存させません。
            DynamicBuffer<GeneratedTerrainCell> terrain = manager.GetBuffer<GeneratedTerrainCell>(entity);
            DynamicBuffer<GeneratedResourceTile> resources = manager.GetBuffer<GeneratedResourceTile>(entity);
            for (int index = 0; index < WorldGridConstants.TilesPerChunk; index++)
            {
                terrain[index] = default;
                resources[index] = default;
            }
        }

        /// <summary>実際のPresenter反映経路へ、指定レイヤーの256セル配列を渡して書込実行の有無を返します。</summary>
        /// <param name="coordinate">対象チャンク座標です。</param>
        /// <param name="layer">反映するScene見本です。</param>
        /// <param name="index">行優先のサブチャンク番号です。</param>
        /// <param name="tiles">テストが用意した256要素配列です。</param>
        /// <returns>SetTilesBlockを実行した場合はtrueです。</returns>
        private bool WriteLayer(ChunkCoordinate coordinate, Tilemap layer, int index, TileBase[] tiles)
        {
            BoundsInt bounds = new BoundsInt(index % 4 * 16, index / 4 * 16, 0, 16, 16, 1);
            return (bool)Invoke("WriteSubChunkLayer", coordinate, layer, bounds, tiles);
        }

        /// <summary>
        /// Presenterが有効地形近傍を直接参照し、旧260セル外周コピーを常駐させないことを保証します。
        /// 外周の差分解決と影分類を二重実行して、表示側のCPU時間とNativeメモリを増やす退行を防ぎます。
        /// </summary>
        [Test]
        public void EffectiveTerrainNeighborhood_PresenterDoesNotKeepLegacyHaloBuffer()
        {
            FieldInfo field = typeof(ChunkTilemapPresenter).GetField(
                "terrainNeighborHaloBuffer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(
                field,
                Is.Null,
                "Presenterは3×3有効地形近傍を直接共有し、旧外周コピーを常駐させてはいけません。");
        }

        /// <summary>
        /// テスト対象の非公開Fieldを取得し、名前の変更で検証が抜け落ちる場合は明示的に失敗させます。
        /// </summary>
        /// <param name="name">PresenterのField名です。</param>
        /// <returns>存在を検証済みの反射情報です。</returns>
        private static FieldInfo Field(string name)
        {
            FieldInfo field = typeof(ChunkTilemapPresenter).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return field;
        }

        /// <summary>
        /// テスト対象の非公開メソッドを呼び、実際のPresenterと同じ予約・移動・掃除の経路を通します。
        /// </summary>
        /// <param name="name">呼び出すメソッド名です。</param>
        /// <param name="arguments">対象メソッドへ渡す引数です。</param>
        /// <returns>対象メソッドの戻り値です。</returns>
        private object Invoke(string name, params object[] arguments)
        {
            MethodInfo method = typeof(ChunkTilemapPresenter).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            return method.Invoke(presenter, arguments);
        }

        /// <summary>
        /// Presenterの実際の取得経路を使い、指定論理チャンクのGround表示を取得します。
        /// </summary>
        /// <param name="coordinate">対象の絶対チャンク座標です。</param>
        /// <param name="create">未作成なら貸し出してよい場合はtrueです。</param>
        /// <returns>チャンク内ローカルTilemap、または未割当時のnullです。</returns>
        private Tilemap GetTilemap(ChunkCoordinate coordinate, bool create)
        {
            return (Tilemap)Invoke("GetTilemapForChunk", coordinate, template, create);
        }
    }
}
