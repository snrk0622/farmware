// このファイルは、比較用のチャンク内ローカルTilemapについて、原点変更で表示と構築進捗を再利用できることを検証します。
// ECSの地形生成規則ではなく、Presenterが所有する表示キャッシュ・空きプール・World切替時の寿命を対象とします。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
