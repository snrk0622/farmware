// このファイルは、SampleSceneを実際に再生し、初期動物種の静的定義、初期カメラ位置、ECS地形・資源生成、Tilemap表示までの縦切りを検証します。
// F3/F4と開発者用表示を含め、純粋計算では確認できないGameObject・SubScene・Presentation間の参照切れを検出します。
// 遠距離の往復ではFloating Originとチャンク再生成の継続性を確認し、既存Profiler Markerの比較値も記録します。

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Farmware.Simulation;
using Farmware.Simulation.Animals;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.PlayModeTests
{
    /// <summary>
    /// SampleSceneの起動後に動物種定義、デバッグ表示、生成設定Singleton、チャンクEntity、地形・資源Tileが正しく連携することを保証します。
    /// Unity Test RunnerのPlayModeテストから実行されます。
    /// </summary>
    public sealed class WorldGenerationScenePlayModeTests
    {
        /// <summary>SubSceneの非同期Bake・ロードと初回チャンク生成を待つ最大描画フレーム数です。</summary>
        private const int MaximumWaitFrames = 300;

        /// <summary>
        /// BatchModeの高速空フレーム間で、Burst Workerへ処理時間を渡す待機時間です。単位はミリ秒です。
        /// 60fps実プレイの1フレームより短い10ミリ秒に留め、300フレーム上限を変えずにWorker飢餓だけを防ぎます。
        /// </summary>
        private const int BatchModeWorkerYieldMilliseconds = 10;

        /// <summary>初期スポーンチャンクが保持範囲外へ確実に出る、テスト用カメラ水平移動量です。</summary>
        private const int FarCameraOffsetInTiles = WorldGridConstants.LogicalChunkSize * 8;

        /// <summary>MAP-02Eの継続移動中に、各Profiler Markerのフレーム値を保持する最大件数です。</summary>
        private const int LongDistanceProfilerSampleCapacity = 4096;

        /// <summary>
        /// Windowboxの基準解像度を整数倍して中央配置し、余ったGameビュー領域が黒帯として除外されることを検証します。
        /// F4グリッドとHUDが参照する純粋な矩形計算なので、Sceneロード前に複数アスペクト比をまとめて確認します。
        /// </summary>
        /// <param name="screenWidth">テストするGameビュー全体の幅です。単位は画面ピクセルです。</param>
        /// <param name="screenHeight">テストするGameビュー全体の高さです。単位は画面ピクセルです。</param>
        /// <param name="expectedX">期待する地形描画矩形の左端です。単位は画面ピクセルです。</param>
        /// <param name="expectedY">期待する地形描画矩形の上端です。単位は画面ピクセルです。</param>
        /// <param name="expectedWidth">期待する地形描画矩形の幅です。単位は画面ピクセルです。</param>
        /// <param name="expectedHeight">期待する地形描画矩形の高さです。単位は画面ピクセルです。</param>
        [TestCase(1920, 1080, 0, 0, 1920, 1080)]
        [TestCase(1760, 916, 160, 53, 1440, 810)]
        [TestCase(1761, 917, 160, 54, 1440, 810)]
        [TestCase(1200, 900, 120, 180, 960, 540)]
        public void CalculateWindowboxGuiRect_CentersIntegerScaledViewport(
            int screenWidth,
            int screenHeight,
            int expectedX,
            int expectedY,
            int expectedWidth,
            int expectedHeight)
        {
            Rect actual = WorldGridDebugOverlay.CalculateWindowboxGuiRect(
                screenWidth,
                screenHeight,
                480,
                270);

            Assert.That(actual.x, Is.EqualTo(expectedX));
            Assert.That(actual.y, Is.EqualTo(expectedY));
            Assert.That(actual.width, Is.EqualTo(expectedWidth));
            Assert.That(actual.height, Is.EqualTo(expectedHeight));
        }

        /// <summary>
        /// 画面内タイルがちょうど1チャンクに収まる境界条件を準備し、上下へはみ出す土地・岩壁Spriteの接地チャンクも含むことを保証します。
        /// 水平方向には現在はみ出しがないため、不要な左右チャンクを追加しないことも同時に確認します。
        /// </summary>
        [Test]
        public void CalculateChunkBoundsFromVisibleTileBounds_IncludesVerticalSpriteOverhang()
        {
            ChunkStreamingObserver bounds =
                CameraChunkStreamingBridge.CalculateChunkBoundsFromVisibleTileBounds(
                    0,
                    WorldGridConstants.LogicalChunkSize - 1,
                    0,
                    WorldGridConstants.LogicalChunkSize - 1);

            Assert.That(bounds.MinimumVisibleChunkX, Is.EqualTo(0));
            Assert.That(bounds.MaximumVisibleChunkX, Is.EqualTo(0));
            Assert.That(
                bounds.MinimumVisibleChunkY,
                Is.EqualTo(-1),
                "画面下端外の岩壁が上へはみ出すため、直下チャンクも表示対象に含める必要があります。");
            Assert.That(
                bounds.MaximumVisibleChunkY,
                Is.EqualTo(1),
                "画面上端外の土地断面が下へはみ出すため、直上チャンクも表示対象に含める必要があります。");
        }

        /// <summary>
        /// CameraのViewportをGridの64タイル境界へ正確に揃え、面積を持たない右上側セルと隣接チャンクを要求しないことを保証します。
        /// 正負両側で、上下方向はSpriteのはみ出し用チャンクだけが加わり、水平方向へ余分なチャンクが増えないことを確認します。
        /// </summary>
        /// <param name="cameraCenterX">64タイル幅Viewportの中心X座標です。</param>
        /// <param name="expectedMinimumChunkX">期待する最小可視チャンクXです。</param>
        /// <param name="expectedMaximumChunkX">期待する最大可視チャンクXです。</param>
        [TestCase(32f, 0, 0)]
        [TestCase(-32f, -1, -1)]
        public void CalculateVisibleChunkBounds_GridBoundaryTreatsUpperRightAsExclusive(
            float cameraCenterX,
            int expectedMinimumChunkX,
            int expectedMaximumChunkX)
        {
            GameObject gridObject = null;
            GameObject cameraObject = null;

            try
            {
                gridObject = new GameObject("Upper Exclusive Test Grid");
                Grid grid = gridObject.AddComponent<Grid>();

                cameraObject = new GameObject("Upper Exclusive Test Camera");
                cameraObject.transform.position = new Vector3(cameraCenterX, 32f, -10f);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 32f;
                camera.aspect = 1f;
                CameraChunkStreamingBridge bridge =
                    cameraObject.AddComponent<CameraChunkStreamingBridge>();

                // Sceneを変更せずprivateなInspector参照を設定し、通常処理と同じCamera/Grid座標変換を直接検証します。
                FieldInfo targetGridField = typeof(CameraChunkStreamingBridge).GetField(
                    "targetGrid",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo calculateVisibleChunkBounds =
                    typeof(CameraChunkStreamingBridge).GetMethod(
                        "CalculateVisibleChunkBounds",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(targetGridField, Is.Not.Null);
                Assert.That(calculateVisibleChunkBounds, Is.Not.Null);
                targetGridField.SetValue(bridge, grid);

                ChunkStreamingObserver bounds = (ChunkStreamingObserver)
                    calculateVisibleChunkBounds.Invoke(bridge, null);

                Assert.That(bounds.MinimumVisibleChunkX, Is.EqualTo(expectedMinimumChunkX));
                Assert.That(
                    bounds.MaximumVisibleChunkX,
                    Is.EqualTo(expectedMaximumChunkX),
                    "Viewport右端は上限排他なので、正負どちらでも右隣チャンクを可視扱いにしてはいけません。");
                Assert.That(bounds.MinimumVisibleChunkY, Is.EqualTo(-1));
                Assert.That(bounds.MaximumVisibleChunkY, Is.EqualTo(1));
            }
            finally
            {
                if (cameraObject != null)
                {
                    Object.DestroyImmediate(cameraObject);
                }

                if (gridObject != null)
                {
                    Object.DestroyImmediate(gridObject);
                }
            }
        }

        /// <summary>
        /// 同じ可視タイル矩形を地上と海中へ変換し、包含するX/Yは同じでもObserverのLayerが分離されることを保証します。
        /// </summary>
        [Test]
        public void CalculateChunkBounds_SameXyDifferentLayers_PreservesRequestedLayer()
        {
            ChunkStreamingObserver surface =
                CameraChunkStreamingBridge.CalculateChunkBoundsFromVisibleTileBounds(
                    WorldLayer.Surface,
                    -65,
                    -2,
                    64,
                    127);
            ChunkStreamingObserver underwater =
                CameraChunkStreamingBridge.CalculateChunkBoundsFromVisibleTileBounds(
                    WorldLayer.Underwater,
                    -65,
                    -2,
                    64,
                    127);

            Assert.That(surface.Layer, Is.EqualTo(WorldLayer.Surface));
            Assert.That(underwater.Layer, Is.EqualTo(WorldLayer.Underwater));
            Assert.That(underwater.MinimumVisibleChunkX, Is.EqualTo(surface.MinimumVisibleChunkX));
            Assert.That(underwater.MaximumVisibleChunkX, Is.EqualTo(surface.MaximumVisibleChunkX));
            Assert.That(underwater.MinimumVisibleChunkY, Is.EqualTo(surface.MinimumVisibleChunkY));
            Assert.That(underwater.MaximumVisibleChunkY, Is.EqualTo(surface.MaximumVisibleChunkY));
        }

        /// <summary>
        /// Bridgeが所有するObserverを海中へ切り替えてからカメラを移動し、可視矩形の再計算後もUnderwaterを保持することを保証します。
        /// カメラ更新が互換用4引数APIのSurfaceを上書きする退行を防ぐPlayModeテストです。
        /// </summary>
        [Test]
        public void CameraChunkStreamingBridge_CameraUpdatePreservesSelectedObserverLayer()
        {
            World previousWorld = World.DefaultGameObjectInjectionWorld;
            using World world = new World("Camera layer preservation test");
            GameObject cameraObject = null;

            try
            {
                World.DefaultGameObjectInjectionWorld = world;
                cameraObject = new GameObject("Layer-aware streaming camera");
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 16f;
                CameraChunkStreamingBridge bridge =
                    cameraObject.AddComponent<CameraChunkStreamingBridge>();
                MethodInfo streamingUpdate = typeof(CameraChunkStreamingBridge).GetMethod(
                    "Update",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(streamingUpdate, Is.Not.Null);

                streamingUpdate.Invoke(bridge, null);
                using EntityQuery observerQuery = world.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<ChunkStreamingObserver>());
                Assert.That(observerQuery.CalculateEntityCount(), Is.EqualTo(1));
                Entity observerEntity = observerQuery.GetSingletonEntity();
                ChunkStreamingObserver selectedObserver =
                    world.EntityManager.GetComponentData<ChunkStreamingObserver>(observerEntity);
                selectedObserver.Layer = WorldLayer.Underwater;
                world.EntityManager.SetComponentData(observerEntity, selectedObserver);

                // 別チャンクへ動かしてX/Y再計算を必ず発生させ、Layerだけが以前のSurfaceへ戻らないことを確認します。
                cameraObject.transform.position = new Vector3(
                    WorldGridConstants.LogicalChunkSize * 2,
                    0f,
                    -10f);
                streamingUpdate.Invoke(bridge, null);

                ChunkStreamingObserver actual =
                    world.EntityManager.GetComponentData<ChunkStreamingObserver>(observerEntity);
                Assert.That(actual.Layer, Is.EqualTo(WorldLayer.Underwater));
                Assert.That(actual.MinimumVisibleChunkX, Is.GreaterThanOrEqualTo(1));
            }
            finally
            {
                if (cameraObject != null)
                {
                    Object.DestroyImmediate(cameraObject);
                }

                World.DefaultGameObjectInjectionWorld = previousWorld;
            }
        }

        /// <summary>
        /// 通常移動で512タイル閾値を越え、表示原点をチャンク境界へ自動更新しても絶対Camera位置とObserver範囲が変わらないことを保証します。
        /// 正負の両方向を通し、C#の0方向丸めではなく床方向のチャンク境界が選ばれることも確認します。
        /// </summary>
        [Test]
        public void NormalCameraMovement_AutomaticallyRebasesAndPreservesAbsoluteChunkBounds()
        {
            GameObject gridObject = null;
            GameObject cameraObject = null;

            try
            {
                gridObject = new GameObject("Display Origin Test Grid");
                Grid grid = gridObject.AddComponent<Grid>();

                cameraObject = new GameObject("Display Origin Test Camera");
                cameraObject.SetActive(false);
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 16f;
                camera.aspect = 1f;
                CameraPanController panController =
                    cameraObject.AddComponent<CameraPanController>();
                CameraChunkStreamingBridge bridge =
                    cameraObject.AddComponent<CameraChunkStreamingBridge>();
                cameraObject.SetActive(true);

                FieldInfo targetGridField = typeof(CameraChunkStreamingBridge).GetField(
                    "targetGrid",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo calculateVisibleChunkBounds =
                    typeof(CameraChunkStreamingBridge).GetMethod(
                        "CalculateVisibleChunkBounds",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(targetGridField, Is.Not.Null);
                Assert.That(calculateVisibleChunkBounds, Is.Not.Null);
                targetGridField.SetValue(bridge, grid);

                // 閾値の直前までは不要な原点変更を行わず、ピクセル整列可能な相対座標を維持します。
                panController.SetWorldPosition(new Vector2(511.9375f, 32.5f));
                Assert.That(
                    panController.CurrentDisplayOrigin.AbsoluteTileCoordinate,
                    Is.EqualTo(int2.zero));
                Assert.That(
                    panController.GetAbsoluteWorldPosition(),
                    Is.EqualTo(new double2(511.9375d, 32.5d)));

                // 8チャンク閾値を越える位置へ通常移動し、Cameraだけを小さな相対座標へ戻します。
                panController.SetWorldPosition(new Vector2(544.5f, 32.5f));
                ChunkStreamingObserver bounds = (ChunkStreamingObserver)
                    calculateVisibleChunkBounds.Invoke(bridge, null);

                Assert.That(
                    panController.CurrentDisplayOrigin.AbsoluteTileCoordinate,
                    Is.EqualTo(new int2(512, 0)),
                    "正方向の自動原点はCamera所属チャンク境界へ移動する必要があります。");
                Assert.That(
                    panController.LastDisplayOriginChangeWasAutomatic,
                    Is.True,
                    "通常移動の閾値到達は明示テレポートと区別できる必要があります。");
                Assert.That(
                    grid.transform.localPosition,
                    Is.EqualTo(Vector3.zero),
                    "Floating OriginでWorld Grid自体を移動してはいけません。");
                Assert.That(cameraObject.transform.position.x, Is.EqualTo(32.5f));
                Assert.That(cameraObject.transform.position.y, Is.EqualTo(32.5f));
                Assert.That(
                    bounds.MinimumVisibleChunkX,
                    Is.EqualTo(8));
                Assert.That(
                    bounds.MaximumVisibleChunkX,
                    Is.EqualTo(8));
                Assert.That(
                    bounds.MinimumVisibleChunkY,
                    Is.EqualTo(0));
                Assert.That(
                    bounds.MaximumVisibleChunkY,
                    Is.EqualTo(0));
                Assert.That(
                    panController.GetAbsoluteWorldPosition(),
                    Is.EqualTo(new double2(544.5d, 32.5d)));

                // 現在原点から負方向へ大きく移動し、絶対座標-32.5が所属する-64境界へ床方向に更新します。
                panController.SetWorldPosition(new Vector2(-544.5f, 32.5f));
                ChunkStreamingObserver negativeBounds = (ChunkStreamingObserver)
                    calculateVisibleChunkBounds.Invoke(bridge, null);
                Assert.That(
                    panController.CurrentDisplayOrigin.AbsoluteTileCoordinate,
                    Is.EqualTo(new int2(-64, 0)),
                    "負方向の自動原点は0方向ではなく床方向のチャンク境界を使用してください。");
                Assert.That(cameraObject.transform.position.x, Is.EqualTo(31.5f));
                Assert.That(cameraObject.transform.position.y, Is.EqualTo(32.5f));
                Assert.That(negativeBounds.MinimumVisibleChunkX, Is.EqualTo(-1));
                Assert.That(negativeBounds.MaximumVisibleChunkX, Is.EqualTo(-1));
                Assert.That(
                    panController.GetAbsoluteWorldPosition(),
                    Is.EqualTo(new double2(-32.5d, 32.5d)));
            }
            finally
            {
                if (cameraObject != null)
                {
                    Object.DestroyImmediate(cameraObject);
                }

                if (gridObject != null)
                {
                    Object.DestroyImmediate(gridObject);
                }
            }
        }

        /// <summary>
        /// Pixel Perfect Cameraの前回描画行列を保持したまま自動原点変更し、Observerが8チャンク先へ飛ばないことを保証します。
        /// 実際にちらついたX=448と960、および負方向を、描画フレームのタイミングに依存せず再現します。
        /// </summary>
        /// <param name="initialOriginX">原点更新前の絶対表示原点Xです。単位はタイルです。</param>
        /// <param name="previousRelativeX">前フレームの描画行列へ保存する相対Camera Xです。</param>
        /// <param name="nextRelativeX">通常移動APIへ渡す、原点更新閾値上の相対Camera Xです。</param>
        /// <param name="expectedOriginX">更新後に期待する絶対表示原点Xです。</param>
        /// <param name="expectedMinimumChunkX">期待する最小可視絶対チャンクXです。</param>
        /// <param name="expectedMaximumChunkX">期待する最大可視絶対チャンクXです。</param>
        [TestCase(-64, 511.75f, 512f, 448, 6, 7)]
        [TestCase(448, 511.75f, 512f, 960, 14, 15)]
        [TestCase(-64, -511.75f, -512f, -576, -10, -9)]
        public void AutomaticRebase_PreviousPixelPerfectCameraMatrixDoesNotShiftObserver(
            int initialOriginX,
            float previousRelativeX,
            float nextRelativeX,
            int expectedOriginX,
            int expectedMinimumChunkX,
            int expectedMaximumChunkX)
        {
            GameObject gridObject = null;
            GameObject cameraObject = null;

            try
            {
                gridObject = new GameObject("Previous Render Matrix Test Grid");
                Grid grid = gridObject.AddComponent<Grid>();
                cameraObject = new GameObject("Previous Render Matrix Test Camera");
                cameraObject.SetActive(false);
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                CameraPanController panController = cameraObject.AddComponent<CameraPanController>();
                CameraChunkStreamingBridge bridge = cameraObject.AddComponent<CameraChunkStreamingBridge>();
                PixelPerfectCamera pixelPerfectCamera = cameraObject.AddComponent<PixelPerfectCamera>();
                pixelPerfectCamera.assetsPPU = 16;
                pixelPerfectCamera.gridSnapping = PixelPerfectCamera.GridSnapping.PixelSnapping;
                cameraObject.SetActive(true);
                camera.orthographicSize = 8f;
                camera.aspect = 1f;

                // Sceneを変えず、実際のBridgeのCamera/Grid変換を直接呼んで比較します。
                FieldInfo targetGridField = typeof(CameraChunkStreamingBridge).GetField(
                    "targetGrid", BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo calculateVisibleChunkBounds = typeof(CameraChunkStreamingBridge).GetMethod(
                    "CalculateVisibleChunkBounds", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(targetGridField, Is.Not.Null);
                Assert.That(calculateVisibleChunkBounds, Is.Not.Null);
                targetGridField.SetValue(bridge, grid);
                Assert.That(
                    panController.SetAbsoluteWorldPosition(initialOriginX + 0.5d, -18.5d),
                    Is.True);
                panController.SetWorldPosition(new Vector2(previousRelativeX, 45.5f));

                // 行列への明示代入で、URPが前回描画時の位置を保持する状態を再現します。
                // Transformだけを動かしても行列が追従しない点が、不具合に必要な前提です。
                camera.ResetWorldToCameraMatrix();
                Matrix4x4 previousRenderMatrix = camera.worldToCameraMatrix;
                camera.worldToCameraMatrix = previousRenderMatrix;
                panController.SetWorldPosition(new Vector2(nextRelativeX, 45.5f));
                Assert.That(
                    camera.cameraToWorldMatrix.MultiplyPoint3x4(Vector3.zero).x,
                    Is.EqualTo(previousRelativeX).Within(0.0001f),
                    "可視範囲の計算直前まで前フレームの行列を保持している必要があります。");

                ChunkStreamingObserver actual = (ChunkStreamingObserver)
                    calculateVisibleChunkBounds.Invoke(bridge, null);
                Assert.That(panController.CurrentDisplayOrigin.AbsoluteTileCoordinate.x, Is.EqualTo(expectedOriginX));
                Assert.That(panController.GetAbsoluteWorldPosition().x, Is.EqualTo((double)initialOriginX + nextRelativeX));
                Assert.That(actual.MinimumVisibleChunkX, Is.EqualTo(expectedMinimumChunkX));
                Assert.That(actual.MaximumVisibleChunkX, Is.EqualTo(expectedMaximumChunkX));
                Assert.That(actual.MinimumVisibleChunkY, Is.EqualTo(-1));
                Assert.That(actual.MaximumVisibleChunkY, Is.EqualTo(-1));
            }
            finally
            {
                // テストが失敗しても、Cameraの描画イベント購読と表示用オブジェクトを残しません。
                if (cameraObject != null)
                {
                    Object.DestroyImmediate(cameraObject);
                }

                if (gridObject != null)
                {
                    Object.DestroyImmediate(gridObject);
                }
            }
        }

        /// <summary>
        /// 自動原点変更前のチャンク表示を準備し、新原点側の同じ論理チャンクを残したまま旧相対セルだけを分割消去できることを保証します。
        /// 旧表示の掃除が新しい可視セルを消すと原点変更直後に空白が出るため、2つの表示原点を明示して回帰確認します。
        /// </summary>
        [Test]
        public void AutomaticRebase_DeferredCleanupClearsOnlyStaleOriginCells()
        {
            GameObject cameraObject = null;
            GameObject gridObject = null;
            GameObject tilemapObject = null;
            GameObject presenterObject = null;
            Tile testTile = null;

            try
            {
                cameraObject = new GameObject("Automatic Rebase Test Camera");
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);
                cameraObject.AddComponent<Camera>();
                CameraPanController panController =
                    cameraObject.AddComponent<CameraPanController>();

                gridObject = new GameObject("Automatic Rebase Test Grid");
                gridObject.AddComponent<Grid>();
                tilemapObject = new GameObject("Automatic Rebase Test Tilemap");
                tilemapObject.transform.SetParent(gridObject.transform, false);
                Tilemap tilemap = tilemapObject.AddComponent<Tilemap>();
                tilemapObject.AddComponent<TilemapRenderer>();

                presenterObject = new GameObject("Automatic Rebase Test Presenter");
                ChunkTilemapPresenter presenter =
                    presenterObject.AddComponent<ChunkTilemapPresenter>();
                presenter.ConfigureDisplayOrigin(panController);

                FieldInfo targetTilemapField = typeof(ChunkTilemapPresenter).GetField(
                    "targetTilemap",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo displayedCoordinatesField = typeof(ChunkTilemapPresenter).GetField(
                    "displayedCoordinates",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo staleClearQueueField = typeof(ChunkTilemapPresenter).GetField(
                    "staleOriginClearQueue",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo synchronizeDisplayOrigin = typeof(ChunkTilemapPresenter).GetMethod(
                    "SynchronizeDisplayOrigin",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo processStaleClearQueue = typeof(ChunkTilemapPresenter).GetMethod(
                    "ProcessStaleOriginClearQueue",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(targetTilemapField, Is.Not.Null);
                Assert.That(displayedCoordinatesField, Is.Not.Null);
                Assert.That(staleClearQueueField, Is.Not.Null);
                Assert.That(synchronizeDisplayOrigin, Is.Not.Null);
                Assert.That(processStaleClearQueue, Is.Not.Null);
                targetTilemapField.SetValue(presenter, tilemap);

                // Presenterへ旧原点0を記録し、絶対チャンク(8, 0)を相対セルX=512へ表示済みとして準備します。
                synchronizeDisplayOrigin.Invoke(presenter, null);
                HashSet<ChunkCoordinate> displayedCoordinates =
                    (HashSet<ChunkCoordinate>)displayedCoordinatesField.GetValue(presenter);
                displayedCoordinates.Add(new ChunkCoordinate(8, 0));
                testTile = ScriptableObject.CreateInstance<Tile>();
                Vector3Int staleCell = new Vector3Int(512, 0, 0);
                tilemap.SetTile(staleCell, testTile);

                // Cameraの絶対位置544.5を変えず、表示原点だけ512へ進めて旧チャンクを分割消去Queueへ移します。
                panController.SetWorldPosition(new Vector2(544.5f, 0.5f));
                synchronizeDisplayOrigin.Invoke(presenter, null);
                object staleClearQueue = staleClearQueueField.GetValue(presenter);
                Assert.That(staleClearQueue, Is.InstanceOf<System.Collections.ICollection>());
                int queuedChunkCount = ((System.Collections.ICollection)staleClearQueue).Count;
                Assert.That(queuedChunkCount, Is.EqualTo(1));
                Assert.That(tilemap.GetTile(staleCell), Is.SameAs(testTile));

                // 新原点512では同じ絶対チャンクの先頭が相対セル0になります。旧セル掃除後もこの新表示を残します。
                Vector3Int currentCell = Vector3Int.zero;
                tilemap.SetTile(currentCell, testTile);
                processStaleClearQueue.Invoke(
                    presenter,
                    new object[] { WorldGridConstants.SubChunksPerChunk });
                Assert.That(
                    tilemap.GetTile(staleCell),
                    Is.Null,
                    "旧表示原点の相対セルはフレーム予算内で消去される必要があります。");
                Assert.That(
                    tilemap.GetTile(currentCell),
                    Is.SameAs(testTile),
                    "旧原点セルの消去で新原点側の同じ論理チャンクを消してはいけません。");
            }
            finally
            {
                if (presenterObject != null)
                {
                    Object.DestroyImmediate(presenterObject);
                }

                if (tilemapObject != null)
                {
                    Object.DestroyImmediate(tilemapObject);
                }

                if (gridObject != null)
                {
                    Object.DestroyImmediate(gridObject);
                }

                if (cameraObject != null)
                {
                    Object.DestroyImmediate(cameraObject);
                }

                if (testTile != null)
                {
                    Object.DestroyImmediate(testTile);
                }
            }
        }

        /// <summary>
        /// 自動原点変更前に作成した岩壁影Meshを準備し、同じ論理チャンクの新表示でGameObjectとMeshを再利用することを保証します。
        /// 毎回の影マスク計算とMesh再生成が戻ると原点変更スパイクが再発するため、位置変更と再利用候補の消費まで確認します。
        /// </summary>
        [Test]
        public void AutomaticRebase_ReusesRockWallShadowForSameLogicalChunk()
        {
            GameObject cameraObject = null;
            GameObject gridObject = null;
            GameObject tilemapObject = null;
            GameObject presenterObject = null;

            try
            {
                cameraObject = new GameObject("Rock Wall Shadow Rebase Test Camera");
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);
                cameraObject.AddComponent<Camera>();
                CameraPanController panController =
                    cameraObject.AddComponent<CameraPanController>();

                gridObject = new GameObject("Rock Wall Shadow Rebase Test Grid");
                gridObject.AddComponent<Grid>();
                tilemapObject = new GameObject("Rock Wall Shadow Rebase Test Tilemap");
                tilemapObject.transform.SetParent(gridObject.transform, false);
                Tilemap tilemap = tilemapObject.AddComponent<Tilemap>();
                tilemapObject.AddComponent<TilemapRenderer>();

                presenterObject = new GameObject("Rock Wall Shadow Rebase Test Presenter");
                ChunkTilemapPresenter presenter =
                    presenterObject.AddComponent<ChunkTilemapPresenter>();
                presenter.ConfigureDisplayOrigin(panController);

                FieldInfo targetTilemapField = typeof(ChunkTilemapPresenter).GetField(
                    "targetTilemap",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo displayedCoordinatesField = typeof(ChunkTilemapPresenter).GetField(
                    "displayedCoordinates",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo reusableShadowsField = typeof(ChunkTilemapPresenter).GetField(
                    "reusableRockWallShadowCoordinates",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo synchronizeDisplayOrigin = typeof(ChunkTilemapPresenter).GetMethod(
                    "SynchronizeDisplayOrigin",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo getOrCreateShadow = typeof(ChunkTilemapPresenter).GetMethod(
                    "GetOrCreateRockWallShadowPresentation",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo tryReuseShadow = typeof(ChunkTilemapPresenter).GetMethod(
                    "TryReuseRockWallShadowAfterAutomaticRebase",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo destroyUnusedShadows = typeof(ChunkTilemapPresenter).GetMethod(
                    "DestroyUnusedRebasedRockWallShadows",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(targetTilemapField, Is.Not.Null);
                Assert.That(displayedCoordinatesField, Is.Not.Null);
                Assert.That(reusableShadowsField, Is.Not.Null);
                Assert.That(synchronizeDisplayOrigin, Is.Not.Null);
                Assert.That(getOrCreateShadow, Is.Not.Null);
                Assert.That(tryReuseShadow, Is.Not.Null);
                Assert.That(destroyUnusedShadows, Is.Not.Null);
                targetTilemapField.SetValue(presenter, tilemap);

                // 旧原点0で絶対チャンク(8, 0)を表示済みにし、実際のPresenter経路で影GameObjectとMeshを1組作ります。
                synchronizeDisplayOrigin.Invoke(presenter, null);
                ChunkCoordinate coordinate = new ChunkCoordinate(8, 0);
                HashSet<ChunkCoordinate> displayedCoordinates =
                    (HashSet<ChunkCoordinate>)displayedCoordinatesField.GetValue(presenter);
                displayedCoordinates.Add(coordinate);
                object originalPresentation = getOrCreateShadow.Invoke(
                    presenter,
                    new object[] { coordinate });
                Assert.That(originalPresentation, Is.Not.Null);
                ChunkCoordinate unusedCoordinate = new ChunkCoordinate(7, 0);
                object unusedPresentation = getOrCreateShadow.Invoke(
                    presenter,
                    new object[] { unusedCoordinate });
                Assert.That(unusedPresentation, Is.Not.Null);
                FieldInfo shadowGameObjectField = originalPresentation.GetType().GetField(
                    "GameObject",
                    BindingFlags.Public | BindingFlags.Instance);
                FieldInfo shadowMeshField = originalPresentation.GetType().GetField(
                    "Mesh",
                    BindingFlags.Public | BindingFlags.Instance);
                Assert.That(shadowGameObjectField, Is.Not.Null);
                Assert.That(shadowMeshField, Is.Not.Null);
                GameObject originalShadowObject =
                    (GameObject)shadowGameObjectField.GetValue(originalPresentation);
                Mesh originalShadowMesh = (Mesh)shadowMeshField.GetValue(originalPresentation);
                originalShadowObject.transform.localPosition = new Vector3(
                    WorldGridConstants.LogicalChunkSize * 8,
                    TerrainLayerPresentationUtility.RockWallElevationInCells,
                    0f);

                // 絶対Camera位置は保ったまま表示原点を512へ進め、影の形状を作り直さず相対セル0へ移します。
                panController.SetWorldPosition(new Vector2(544.5f, 0.5f));
                synchronizeDisplayOrigin.Invoke(presenter, null);
                object retainedPresentation = getOrCreateShadow.Invoke(
                    presenter,
                    new object[] { coordinate });
                Assert.That(retainedPresentation, Is.SameAs(originalPresentation));
                Assert.That(
                    shadowGameObjectField.GetValue(retainedPresentation),
                    Is.SameAs(originalShadowObject));
                Assert.That(
                    shadowMeshField.GetValue(retainedPresentation),
                    Is.SameAs(originalShadowMesh));
                Assert.That(originalShadowObject.transform.localPosition.x, Is.EqualTo(0f));

                // 新Tilemap反映完了を模擬して再利用候補を消費し、未使用影の掃除から採用済み影が除外されることを確認します。
                HashSet<ChunkCoordinate> reusableShadows =
                    (HashSet<ChunkCoordinate>)reusableShadowsField.GetValue(presenter);
                Assert.That(reusableShadows.Count, Is.EqualTo(2));
                Assert.That(
                    tryReuseShadow.Invoke(presenter, new object[] { coordinate }),
                    Is.EqualTo(true));
                Assert.That(reusableShadows.Count, Is.EqualTo(1));
                destroyUnusedShadows.Invoke(presenter, null);
                Assert.That(reusableShadows.Count, Is.EqualTo(0));
                Assert.That(
                    getOrCreateShadow.Invoke(presenter, new object[] { coordinate }),
                    Is.SameAs(originalPresentation),
                    "新表示で採用した影は原点変更フレーム末の未使用掃除で破棄してはいけません。");
                Assert.That(
                    getOrCreateShadow.Invoke(presenter, new object[] { unusedCoordinate }),
                    Is.Not.SameAs(unusedPresentation),
                    "新Viewportで採用しなかった旧影は辞書から除き、表示・メモリへ残してはいけません。");
            }
            finally
            {
                if (presenterObject != null)
                {
                    Object.DestroyImmediate(presenterObject);
                }

                if (tilemapObject != null)
                {
                    Object.DestroyImmediate(tilemapObject);
                }

                if (gridObject != null)
                {
                    Object.DestroyImmediate(gridObject);
                }

                if (cameraObject != null)
                {
                    Object.DestroyImmediate(cameraObject);
                }
            }
        }

        /// <summary>
        /// 現在の負座標と記録済み初期スポーンを準備し、F3文字列が両者を混同せずタイル・チャンク座標として表示することを保証します。
        /// </summary>
        [Test]
        public void BuildCoordinateHudText_IncludesRecordedInitialSpawnLocation()
        {
            InitialSpawnLocation spawnLocation = new InitialSpawnLocation
            {
                Layer = WorldLayer.Surface,
                TileX = -65,
                TileY = 64
            };

            string text = WorldGridDebugOverlay.BuildCoordinateHudText(
                new double2(5.5d, -2.5d),
                new Vector3Int(5, -3, 0),
                true,
                spawnLocation,
                true,
                new InitialSpawnSearchStatus
                {
                    Phase = InitialSpawnSearchPhase.Succeeded
                },
                true,
                new WorldGenerationSettings
                {
                    GeneratorVersion = 7,
                    WorldSeed = 0x1234UL
                },
                true,
                BiomeId.Snowfield);

            Assert.That(text, Does.Contain("Tile  : (5, -3)"));
            Assert.That(text, Does.Contain("Layer : Surface"));
            Assert.That(text, Does.Contain("SubChunk: (0, 3)"));
            Assert.That(text, Does.Contain("Biome: Snowfield"));
            Assert.That(text, Does.Contain("GeneratorVersion: 7"));
            Assert.That(text, Does.Contain("WorldSeed: 0x0000000000001234"));
            Assert.That(text, Does.Contain("Spawn: Surface Tile (-65, 64) / Chunk (-2, 1)"));
        }

        /// <summary>
        /// 初期スポーン探索が失敗した状態を準備し、F3が探索中ではなくScene保存位置の使用を表示することを保証します。
        /// </summary>
        [Test]
        public void BuildCoordinateHudText_SearchFailedShowsSceneCameraFallback()
        {
            string text = WorldGridDebugOverlay.BuildCoordinateHudText(
                new double2(123.5d, -45.25d),
                new Vector3Int(123, -46, 0),
                false,
                default,
                true,
                new InitialSpawnSearchStatus
                {
                    Phase = InitialSpawnSearchPhase.Failed
                },
                false,
                default,
                false,
                default);

            Assert.That(text, Does.Contain("Spawn: Failed / Using Scene camera position"));
            Assert.That(text, Does.Not.Contain("Spawn: Searching..."));
        }

        /// <summary>
        /// 生成設定Singletonと負座標チャンクのGeneratedTile Bufferを一時Worldへ準備し、F3が表示用の推測値ではなくECSの正から値を読むことを保証します。
        /// </summary>
        [Test]
        public void CoordinateHud_ReadsGenerationSettingsAndCurrentBiomeFromEcs()
        {
            World previousWorld = World.DefaultGameObjectInjectionWorld;
            World testWorld = new World("CoordinateHudEcsReadWorld");
            GameObject overlayObject = null;

            try
            {
                World.DefaultGameObjectInjectionWorld = testWorld;
                Entity settingsEntity = testWorld.EntityManager.CreateEntity();
                testWorld.EntityManager.AddComponentData(
                    settingsEntity,
                    new WorldGenerationSettings
                    {
                        GeneratorVersion = 9,
                        WorldSeed = 0xFEDCBA9876543210UL
                    });

                Entity searchStatusEntity = testWorld.EntityManager.CreateEntity();
                testWorld.EntityManager.AddComponentData(
                    searchStatusEntity,
                    new InitialSpawnSearchStatus
                    {
                        Phase = InitialSpawnSearchPhase.Failed
                    });

                Entity chunkEntity = testWorld.EntityManager.CreateEntity();
                testWorld.EntityManager.AddComponentData(
                    chunkEntity,
                    new ChunkCoordinateComponent
                    {
                        Layer = WorldLayer.Surface,
                        X = 0,
                        Y = -1
                    });
                testWorld.EntityManager.AddComponentData(
                    chunkEntity,
                    new ChunkGenerationStatus
                    {
                        Phase = ChunkGenerationPhase.Active,
                        GeneratedWithVersion = 9
                    });
                DynamicBuffer<GeneratedTile> tiles =
                    testWorld.EntityManager.AddBuffer<GeneratedTile>(chunkEntity);
                tiles.ResizeUninitialized(WorldGridConstants.TilesPerChunk);
                ChunkLocalTileCoordinate local = WorldGrid.ToLocalTileCoordinate(5, -3);
                int tileIndex = WorldGrid.ToTileIndex(local.X, local.Y);
                tiles[tileIndex] = new GeneratedTile { Biome = BiomeId.Volcanic };

                overlayObject = new GameObject("Coordinate HUD ECS Reader");
                overlayObject.AddComponent<Camera>();
                WorldGridDebugOverlay overlay = overlayObject.AddComponent<WorldGridDebugOverlay>();
                MethodInfo readSettings = typeof(WorldGridDebugOverlay).GetMethod(
                    "TryGetWorldGenerationSettings",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo readSearchStatus = typeof(WorldGridDebugOverlay).GetMethod(
                    "TryGetInitialSpawnSearchStatus",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo readBiome = typeof(WorldGridDebugOverlay).GetMethod(
                    "TryGetCurrentTileBiome",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(readSettings, Is.Not.Null);
                Assert.That(readSearchStatus, Is.Not.Null);
                Assert.That(readBiome, Is.Not.Null);

                object[] settingsArguments = { default(WorldGenerationSettings) };
                object[] searchStatusArguments = { default(InitialSpawnSearchStatus) };
                object[] biomeArguments =
                {
                    WorldLayer.Surface,
                    new Vector3Int(5, -3, 0),
                    default(BiomeId)
                };
                Assert.That(readSettings.Invoke(overlay, settingsArguments), Is.EqualTo(true));
                Assert.That(readSearchStatus.Invoke(overlay, searchStatusArguments), Is.EqualTo(true));
                Assert.That(readBiome.Invoke(overlay, biomeArguments), Is.EqualTo(true));

                WorldGenerationSettings actualSettings =
                    (WorldGenerationSettings)settingsArguments[0];
                Assert.That(actualSettings.GeneratorVersion, Is.EqualTo(9));
                Assert.That(actualSettings.WorldSeed, Is.EqualTo(0xFEDCBA9876543210UL));
                Assert.That(
                    ((InitialSpawnSearchStatus)searchStatusArguments[0]).Phase,
                    Is.EqualTo(InitialSpawnSearchPhase.Failed));
                Assert.That((BiomeId)biomeArguments[2], Is.EqualTo(BiomeId.Volcanic));
            }
            finally
            {
                if (overlayObject != null)
                {
                    Object.DestroyImmediate(overlayObject);
                }

                World.DefaultGameObjectInjectionWorld = previousWorld;
                testWorld.Dispose();
            }
        }

        /// <summary>
        /// 負座標の初期スポーンを準備し、F4の赤い所属チャンク枠と専用ラベルがF3と同じ床方向チャンクを指すことを保証します。
        /// </summary>
        [Test]
        public void InitialSpawnF4Marker_UsesContainingChunkBoundsAndDistinctLabel()
        {
            InitialSpawnLocation spawnLocation = new InitialSpawnLocation
            {
                Layer = WorldLayer.Surface,
                TileX = -65,
                TileY = 64
            };

            RectInt chunkBounds = WorldGridDebugOverlay.CalculateInitialSpawnChunkTileBounds(
                spawnLocation);
            string spawnLabel = WorldGridDebugOverlay.BuildChunkCoordinateLabelText(-2, 1, true);
            string normalLabel = WorldGridDebugOverlay.BuildChunkCoordinateLabelText(-1, 1, false);

            Assert.That(chunkBounds, Is.EqualTo(new RectInt(-128, 64, 64, 64)));
            Assert.That(spawnLabel, Is.EqualTo("Spawn Chunk (-2, 1)"));
            Assert.That(normalLabel, Is.EqualTo("Chunk (-1, 1)"));
        }

        /// <summary>
        /// 初期スポーン探索失敗を持つ一時Worldとカメラを準備し、Scene保存位置を維持したままストリーミングObserverを作れることを保証します。
        /// </summary>
        [Test]
        public void InitialSpawnFailure_KeepsSceneCameraPositionAndStartsStreaming()
        {
            World previousWorld = World.DefaultGameObjectInjectionWorld;
            World testWorld = new World("InitialSpawnFailurePresentationTests");
            GameObject cameraObject = null;

            try
            {
                World.DefaultGameObjectInjectionWorld = testWorld;
                Entity searchStatusEntity = testWorld.EntityManager.CreateEntity();
                testWorld.EntityManager.AddComponentData(
                    searchStatusEntity,
                    new InitialSpawnSearchStatus
                    {
                        Phase = InitialSpawnSearchPhase.Failed
                    });

                cameraObject = new GameObject("Initial Spawn Failure Camera");
                cameraObject.SetActive(false);
                Vector3 savedScenePosition = new Vector3(123.5f, -45.25f, -10f);
                cameraObject.transform.position = savedScenePosition;
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                cameraObject.AddComponent<CameraPanController>();
                InitialCameraPositionController positionController =
                    cameraObject.AddComponent<InitialCameraPositionController>();
                CameraChunkStreamingBridge streamingBridge =
                    cameraObject.AddComponent<CameraChunkStreamingBridge>();
                cameraObject.SetActive(true);

                // UnityがprivateなUpdateを呼ぶ通常フレームを待つと既存Sceneまで一時Worldへ接続されるため、
                // Reflectionで対象2Componentだけを通常の実行順どおり同期実行し、テストを一時World内へ隔離します。
                MethodInfo positionUpdate = typeof(InitialCameraPositionController).GetMethod(
                    "Update",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo streamingUpdate = typeof(CameraChunkStreamingBridge).GetMethod(
                    "Update",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(positionUpdate, Is.Not.Null);
                Assert.That(streamingUpdate, Is.Not.Null);
                positionUpdate.Invoke(positionController, null);
                streamingUpdate.Invoke(streamingBridge, null);

                Assert.That(positionController.HasCompletedInitialPositioning, Is.True);
                Assert.That(positionController.HasAppliedInitialPosition, Is.False);
                Assert.That(cameraObject.transform.position, Is.EqualTo(savedScenePosition));

                using EntityQuery observerQuery = testWorld.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<ChunkStreamingObserver>());
                Assert.That(
                    observerQuery.CalculateEntityCount(),
                    Is.EqualTo(1),
                    "探索失敗後もScene保存位置を基準にチャンク読み込みを開始する必要があります。");
            }
            finally
            {
                if (cameraObject != null)
                {
                    cameraObject.SetActive(false);
                    Object.DestroyImmediate(cameraObject);
                }

                World.DefaultGameObjectInjectionWorld = previousWorld;
                testWorld.Dispose();
            }
        }

        /// <summary>
        /// 2つの有効なWorldを維持したままDefault Worldを初期スポーン探索中のWorldへ切り替え、Camera Bridgeが旧Observerだけを直ちに破棄することを保証します。
        /// 新Worldでは初期位置の確定までObserverを作らず、確定後に1件だけ作成する寿命全体も検証します。
        /// </summary>
        [Test]
        public void CameraChunkStreamingBridge_WorldChangedWhileSpawnPending_DestroysOnlyPreviousObserver()
        {
            World previousWorld = World.DefaultGameObjectInjectionWorld;
            World firstWorld = new World("CameraBridgeFirstWorld");
            World secondWorld = new World("CameraBridgeSecondWorld");
            GameObject cameraObject = null;

            try
            {
                World.DefaultGameObjectInjectionWorld = firstWorld;
                Entity firstSearchStatusEntity = firstWorld.EntityManager.CreateEntity();
                firstWorld.EntityManager.AddComponentData(
                    firstSearchStatusEntity,
                    new InitialSpawnSearchStatus
                    {
                        Phase = InitialSpawnSearchPhase.Failed
                    });

                cameraObject = new GameObject("World Switching Streaming Camera");
                cameraObject.SetActive(false);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                cameraObject.AddComponent<CameraPanController>();
                InitialCameraPositionController positionController =
                    cameraObject.AddComponent<InitialCameraPositionController>();
                CameraChunkStreamingBridge streamingBridge =
                    cameraObject.AddComponent<CameraChunkStreamingBridge>();
                cameraObject.SetActive(true);

                MethodInfo positionUpdate = typeof(InitialCameraPositionController).GetMethod(
                    "Update",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo streamingUpdate = typeof(CameraChunkStreamingBridge).GetMethod(
                    "Update",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(positionUpdate, Is.Not.Null);
                Assert.That(streamingUpdate, Is.Not.Null);
                positionUpdate.Invoke(positionController, null);
                streamingUpdate.Invoke(streamingBridge, null);

                using EntityQuery firstObserverQuery = firstWorld.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<ChunkStreamingObserver>());
                using EntityQuery secondObserverQuery = secondWorld.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<ChunkStreamingObserver>());
                Assert.That(firstObserverQuery.CalculateEntityCount(), Is.EqualTo(1));
                Assert.That(secondObserverQuery.CalculateEntityCount(), Is.EqualTo(0));

                // 旧Worldを破棄せずDefaultだけを切り替え、新World側の初期位置Controllerを探索待機状態へ戻します。
                World.DefaultGameObjectInjectionWorld = secondWorld;
                positionUpdate.Invoke(positionController, null);
                streamingUpdate.Invoke(streamingBridge, null);

                Assert.That(
                    firstObserverQuery.CalculateEntityCount(),
                    Is.EqualTo(0),
                    "新Worldの初期スポーン探索中でも、旧WorldのObserverは直ちに破棄する必要があります。");
                Assert.That(
                    positionController.HasCompletedInitialPositioning,
                    Is.False,
                    "探索結果がない新Worldでは、初期カメラ位置の確定を待つ必要があります。");
                Assert.That(
                    secondObserverQuery.CalculateEntityCount(),
                    Is.EqualTo(0),
                    "初期位置が未確定の新WorldへObserverを先行作成してはいけません。");

                Entity secondSearchStatusEntity = secondWorld.EntityManager.CreateEntity();
                secondWorld.EntityManager.AddComponentData(
                    secondSearchStatusEntity,
                    new InitialSpawnSearchStatus
                    {
                        Phase = InitialSpawnSearchPhase.Failed
                    });
                positionUpdate.Invoke(positionController, null);
                streamingUpdate.Invoke(streamingBridge, null);

                Assert.That(positionController.HasCompletedInitialPositioning, Is.True);
                Assert.That(secondObserverQuery.CalculateEntityCount(), Is.EqualTo(1));
            }
            finally
            {
                if (cameraObject != null)
                {
                    cameraObject.SetActive(false);
                    Object.DestroyImmediate(cameraObject);
                }

                World.DefaultGameObjectInjectionWorld = previousWorld;
                firstWorld.Dispose();
                secondWorld.Dispose();
            }
        }

        /// <summary>
        /// Camera GameObjectを維持したままDefault Worldを作り直し、新しいWorldの初期スポーンをもう一度適用することを保証します。
        /// </summary>
        [Test]
        public void InitialCameraPosition_WorldRecreated_ReappliesNewSpawnLocation()
        {
            World previousWorld = World.DefaultGameObjectInjectionWorld;
            World firstWorld = new World("InitialCameraFirstWorld");
            World secondWorld = new World("InitialCameraSecondWorld");
            GameObject cameraObject = null;

            try
            {
                Entity firstSpawnEntity = firstWorld.EntityManager.CreateEntity();
                firstWorld.EntityManager.AddComponentData(
                    firstSpawnEntity,
                    new InitialSpawnLocation
                    {
                        Layer = WorldLayer.Surface,
                        TileX = 2,
                        TileY = 3
                    });
                Entity secondSpawnEntity = secondWorld.EntityManager.CreateEntity();
                secondWorld.EntityManager.AddComponentData(
                    secondSpawnEntity,
                    new InitialSpawnLocation
                    {
                        Layer = WorldLayer.Surface,
                        TileX = -5,
                        TileY = 7
                    });

                World.DefaultGameObjectInjectionWorld = firstWorld;
                cameraObject = new GameObject("World Recreation Initial Camera");
                cameraObject.SetActive(false);
                cameraObject.AddComponent<Camera>();
                CameraPanController panController =
                    cameraObject.AddComponent<CameraPanController>();
                InitialCameraPositionController positionController =
                    cameraObject.AddComponent<InitialCameraPositionController>();
                cameraObject.SetActive(true);

                MethodInfo positionUpdate = typeof(InitialCameraPositionController).GetMethod(
                    "Update",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(positionUpdate, Is.Not.Null);

                positionUpdate.Invoke(positionController, null);
                Assert.That(
                    panController.GetAbsoluteWorldPosition(),
                    Is.EqualTo(new double2(2.5d, 3.5d)));

                // Componentを無効化せずWorldだけを切り替え、以前の完了フラグが新Worldの適用を妨げないことを確認します。
                World.DefaultGameObjectInjectionWorld = secondWorld;
                positionUpdate.Invoke(positionController, null);

                Assert.That(positionController.HasCompletedInitialPositioning, Is.True);
                Assert.That(positionController.HasAppliedInitialPosition, Is.True);
                Assert.That(
                    panController.GetAbsoluteWorldPosition(),
                    Is.EqualTo(new double2(-4.5d, 7.5d)));
                Assert.That(
                    panController.CurrentDisplayOrigin.AbsoluteTileCoordinate,
                    Is.EqualTo(new int2(-64, 0)),
                    "負座標の初期スポーンでも0方向ではなく床方向のチャンク境界を表示原点にしてください。");
            }
            finally
            {
                if (cameraObject != null)
                {
                    cameraObject.SetActive(false);
                    Object.DestroyImmediate(cameraObject);
                }

                World.DefaultGameObjectInjectionWorld = previousWorld;
                firstWorld.Dispose();
                secondWorld.Dispose();
            }
        }

        /// <summary>
        /// 初期スポーン適用後にプレイヤーが移動したカメラを準備し、同じWorldでComponentを再有効化しても位置を保つことを保証します。
        /// OnEnableが初期化済み状態を消し、カメラを開始地点へ戻す退行を防ぎます。
        /// </summary>
        [Test]
        public void InitialCameraPosition_ReenabledInSameWorld_KeepsPlayerPosition()
        {
            World previousWorld = World.DefaultGameObjectInjectionWorld;
            World testWorld = new World("InitialCameraReenableWorld");
            GameObject cameraObject = null;

            try
            {
                Entity spawnEntity = testWorld.EntityManager.CreateEntity();
                testWorld.EntityManager.AddComponentData(
                    spawnEntity,
                    new InitialSpawnLocation
                    {
                        Layer = WorldLayer.Surface,
                        TileX = 2,
                        TileY = 3
                    });

                World.DefaultGameObjectInjectionWorld = testWorld;
                cameraObject = new GameObject("Reenabled Initial Camera");
                cameraObject.SetActive(false);
                cameraObject.AddComponent<Camera>();
                CameraPanController panController =
                    cameraObject.AddComponent<CameraPanController>();
                InitialCameraPositionController positionController =
                    cameraObject.AddComponent<InitialCameraPositionController>();
                cameraObject.SetActive(true);

                MethodInfo positionUpdate = typeof(InitialCameraPositionController).GetMethod(
                    "Update",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(positionUpdate, Is.Not.Null);
                positionUpdate.Invoke(positionController, null);
                Assert.That(cameraObject.transform.position.x, Is.EqualTo(2.5f));
                Assert.That(cameraObject.transform.position.y, Is.EqualTo(3.5f));

                Vector2 playerPosition = new Vector2(30.5f, -12.5f);
                panController.SetWorldPosition(playerPosition);
                positionController.enabled = false;
                positionController.enabled = true;
                positionUpdate.Invoke(positionController, null);

                Assert.That(positionController.HasCompletedInitialPositioning, Is.True);
                Assert.That(positionController.HasAppliedInitialPosition, Is.True);
                Assert.That(cameraObject.transform.position.x, Is.EqualTo(playerPosition.x));
                Assert.That(cameraObject.transform.position.y, Is.EqualTo(playerPosition.y));
            }
            finally
            {
                if (cameraObject != null)
                {
                    cameraObject.SetActive(false);
                    Object.DestroyImmediate(cameraObject);
                }

                World.DefaultGameObjectInjectionWorld = previousWorld;
                testWorld.Dispose();
            }
        }

        /// <summary>
        /// Presenterが参照するDefault Worldを切り替え、旧WorldのTileと表示済み座標キャッシュを即時破棄することを保証します。
        /// </summary>
        [Test]
        public void ChunkTilemapPresenter_WorldChanged_ClearsPreviousWorldPresentation()
        {
            World previousWorld = World.DefaultGameObjectInjectionWorld;
            World firstWorld = new World("TilemapPresenterFirstWorld");
            World secondWorld = new World("TilemapPresenterSecondWorld");
            GameObject presenterRoot = null;
            Tile testTile = null;

            try
            {
                World.DefaultGameObjectInjectionWorld = firstWorld;
                ChunkTilemapPresenter presenter = CreateLifecycleTestPresenter(
                    out presenterRoot,
                    out Tilemap terrainTilemap,
                    out Tilemap landBoundaryHighlightTilemap,
                    out Tilemap rockWallBoundaryHighlightTilemap);
                MethodInfo ensureQueries = typeof(ChunkTilemapPresenter).GetMethod(
                    "TryEnsureQueries",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(ensureQueries, Is.Not.Null);
                Assert.That(ensureQueries.Invoke(presenter, null), Is.EqualTo(true));

                testTile = ScriptableObject.CreateInstance<Tile>();
                terrainTilemap.SetTile(Vector3Int.zero, testTile);
                landBoundaryHighlightTilemap.SetTile(Vector3Int.zero, testTile);
                rockWallBoundaryHighlightTilemap.SetTile(Vector3Int.zero, testTile);
                FieldInfo displayedCoordinatesField = typeof(ChunkTilemapPresenter).GetField(
                    "displayedCoordinates",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(displayedCoordinatesField, Is.Not.Null);
                HashSet<ChunkCoordinate> displayedCoordinates =
                    (HashSet<ChunkCoordinate>)displayedCoordinatesField.GetValue(presenter);
                displayedCoordinates.Add(new ChunkCoordinate(0, 0));

                // 同じ座標を持ち得る別Worldへ切り替え、旧キャッシュによる再反映省略を防げることを確認します。
                World.DefaultGameObjectInjectionWorld = secondWorld;
                Assert.That(ensureQueries.Invoke(presenter, null), Is.EqualTo(true));

                Assert.That(terrainTilemap.GetTile(Vector3Int.zero), Is.Null);
                Assert.That(landBoundaryHighlightTilemap.GetTile(Vector3Int.zero), Is.Null);
                Assert.That(rockWallBoundaryHighlightTilemap.GetTile(Vector3Int.zero), Is.Null);
                Assert.That(displayedCoordinates, Is.Empty);
            }
            finally
            {
                if (presenterRoot != null)
                {
                    Object.DestroyImmediate(presenterRoot);
                }

                if (testTile != null)
                {
                    Object.DestroyImmediate(testTile);
                }

                World.DefaultGameObjectInjectionWorld = previousWorld;
                firstWorld.Dispose();
                secondWorld.Dispose();
            }
        }

        /// <summary>
        /// 部分反映後に消去待ちだけとなったチャンクを準備し、Presenter無効化時に残存Tileと待機キャッシュを消去することを保証します。
        /// </summary>
        [Test]
        public void ChunkTilemapPresenter_Disabled_ClearsPendingPartialChunk()
        {
            GameObject presenterRoot = null;
            Tile testTile = null;

            try
            {
                ChunkTilemapPresenter presenter = CreateLifecycleTestPresenter(
                    out presenterRoot,
                    out Tilemap terrainTilemap,
                    out Tilemap landBoundaryHighlightTilemap,
                    out Tilemap rockWallBoundaryHighlightTilemap);
                testTile = ScriptableObject.CreateInstance<Tile>();
                terrainTilemap.SetTile(Vector3Int.zero, testTile);
                landBoundaryHighlightTilemap.SetTile(Vector3Int.zero, testTile);
                rockWallBoundaryHighlightTilemap.SetTile(Vector3Int.zero, testTile);

                FieldInfo pendingCoordinatesField = typeof(ChunkTilemapPresenter).GetField(
                    "pendingClearCoordinates",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(pendingCoordinatesField, Is.Not.Null);
                HashSet<ChunkCoordinate> pendingCoordinates =
                    (HashSet<ChunkCoordinate>)pendingCoordinatesField.GetValue(presenter);
                pendingCoordinates.Add(new ChunkCoordinate(0, 0));

                // enabledの変更はUnityからOnDisableを同期的に呼び、消去キューを待たずに全セルを片付けます。
                presenter.enabled = false;

                Assert.That(terrainTilemap.GetTile(Vector3Int.zero), Is.Null);
                Assert.That(landBoundaryHighlightTilemap.GetTile(Vector3Int.zero), Is.Null);
                Assert.That(rockWallBoundaryHighlightTilemap.GetTile(Vector3Int.zero), Is.Null);
                Assert.That(pendingCoordinates, Is.Empty);
            }
            finally
            {
                if (presenterRoot != null)
                {
                    Object.DestroyImmediate(presenterRoot);
                }

                if (testTile != null)
                {
                    Object.DestroyImmediate(testTile);
                }
            }
        }

        /// <summary>
        /// 同じBiomeでSoilTypeだけを変えても、Presenterが同じプレイヤー向け土地Tileを選ぶことを保証します。
        /// </summary>
        [Test]
        public void ChunkTilemapPresenter_IgnoresSoilForPlayerTerrainTile()
        {
            GameObject presenterRoot = null;
            Tile grasslandBase = null;
            Tile forestBase = null;

            try
            {
                ChunkTilemapPresenter presenter = CreateLifecycleTestPresenter(
                    out presenterRoot,
                    out Tilemap terrainTilemap,
                    out _,
                    out _);
                grasslandBase = ScriptableObject.CreateInstance<Tile>();
                forestBase = ScriptableObject.CreateInstance<Tile>();

                presenter.Configure(
                    terrainTilemap,
                    null,
                    grasslandBase,
                    forestBase,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
                MethodInfo selectLandTile = typeof(ChunkTilemapPresenter).GetMethod(
                    "SelectLandTile",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(selectLandTile, Is.Not.Null);

                GeneratedTile forestNormal = new GeneratedTile
                {
                    Ground = GroundType.Land,
                    Biome = BiomeId.Forest,
                    Soil = SoilType.Normal
                };
                GeneratedTile forestFertile = forestNormal;
                forestFertile.Soil = SoilType.Fertile;
                GeneratedTile forestSandy = forestNormal;
                forestSandy.Soil = SoilType.Sandy;

                // SoilTypeは資源生成の内部情報なので、3種類とも森林の同じTileへ到達する必要があります。
                Assert.That(
                    selectLandTile.Invoke(presenter, new object[] { forestNormal }),
                    Is.SameAs(forestBase));
                Assert.That(
                    selectLandTile.Invoke(presenter, new object[] { forestFertile }),
                    Is.SameAs(forestBase));
                Assert.That(
                    selectLandTile.Invoke(presenter, new object[] { forestSandy }),
                    Is.SameAs(forestBase));
            }
            finally
            {
                if (presenterRoot != null)
                {
                    Object.DestroyImmediate(presenterRoot);
                }

                if (grasslandBase != null)
                {
                    Object.DestroyImmediate(grasslandBase);
                }

                if (forestBase != null)
                {
                    Object.DestroyImmediate(forestBase);
                }
            }
        }

        /// <summary>
        /// F4グリッドの縦線と横線が、Windowbox内でGUI.matrixを回転せず描画できる正の矩形へ変換されることを検証します。
        /// 開始・終了の向きを逆にしたケースも含め、縦線だけ消える不具合の再発を防ぎます。
        /// </summary>
        /// <param name="startX">線の開始X座標です。単位は画面ピクセルです。</param>
        /// <param name="startY">線の開始Y座標です。単位は画面ピクセルです。</param>
        /// <param name="endX">線の終了X座標です。単位は画面ピクセルです。</param>
        /// <param name="endY">線の終了Y座標です。単位は画面ピクセルです。</param>
        /// <param name="expectedX">期待する描画矩形の左端です。単位は画面ピクセルです。</param>
        /// <param name="expectedY">期待する描画矩形の上端です。単位は画面ピクセルです。</param>
        /// <param name="expectedWidth">期待する描画矩形の幅です。単位は画面ピクセルです。</param>
        /// <param name="expectedHeight">期待する描画矩形の高さです。単位は画面ピクセルです。</param>
        [TestCase(25f, 10f, 25f, 90f, 24f, 10f, 2f, 80f)]
        [TestCase(25f, 90f, 25f, 10f, 24f, 10f, 2f, 80f)]
        [TestCase(10f, 25f, 90f, 25f, 10f, 24f, 80f, 2f)]
        [TestCase(90f, 25f, 10f, 25f, 10f, 24f, 80f, 2f)]
        public void TryCalculateAxisAlignedLineRect_ReturnsVisibleRectangle(
            float startX,
            float startY,
            float endX,
            float endY,
            float expectedX,
            float expectedY,
            float expectedWidth,
            float expectedHeight)
        {
            bool succeeded = WorldGridDebugOverlay.TryCalculateAxisAlignedLineRect(
                new Vector2(startX, startY),
                new Vector2(endX, endY),
                2f,
                out Rect actual);

            Assert.That(succeeded, Is.True);
            Assert.That(actual.x, Is.EqualTo(expectedX));
            Assert.That(actual.y, Is.EqualTo(expectedY));
            Assert.That(actual.width, Is.EqualTo(expectedWidth));
            Assert.That(actual.height, Is.EqualTo(expectedHeight));
        }

        /// <summary>
        /// 開発者用可視化がBiome、Soil、連続環境軸、水域、資源、地形配置属性を異なる色へ写像することを検証します。
        /// SceneやECS Worldを作らない純粋計算として、TERRAIN-01C1各軸の表示色、水域分類、配置契約、最終資源分類と入力値の不変性をまとめて保証します。
        /// </summary>
        [Test]
        public void DebugVisualization_MapsGeneratedTileValuesWithoutChangingSimulationData()
        {
            GeneratedTile forestTransitionTile = new GeneratedTile
            {
                Biome = BiomeId.Forest,
                Ground = GroundType.Land,
                Soil = SoilType.Normal,
                // Terrain Placement表示は補助物流のベルト可否に依存せず、通常設備可否だけで陸上色を選びます。
                GameplayFlags = TileGameplayFlags.AllowsNormalEquipment,
                Elevation = ushort.MaxValue,
                Temperature = 0,
                Moisture = ushort.MaxValue,
                Weirdness = ushort.MaxValue,
                TerrainTemperature = ushort.MaxValue,
                WoodyVegetationPotential = ushort.MaxValue,
                TerrainRuggedness = ushort.MaxValue,
                CoastalInfluence = ushort.MaxValue,
                VolcanicActivityPotential = ushort.MaxValue,
                GeologyClass = GeologyClass.Limestone,
                GeologyFlags = GeologyDefinitionFlags.SupportsKarst,
                GeologyPermeability = ushort.MaxValue,
                GeologyGroundwaterStorage = ushort.MaxValue,
                GeologyErosionResistance = ushort.MaxValue
            };
            GeneratedTile originalForestTransitionTile = forestTransitionTile;

            Color coldColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.BaseTemperature,
                forestTransitionTile);
            Assert.That(coldColor.b, Is.GreaterThan(coldColor.r), "最低基礎気温は赤より青が強い必要があります。");

            Color highElevationColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Elevation,
                forestTransitionTile);
            Assert.That(
                highElevationColor.r,
                Is.GreaterThan(0.9f),
                "最高標高は凡例どおり白に近い必要があります。");

            Color highTerrainTemperatureColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.TerrainTemperature,
                forestTransitionTile);
            Assert.That(
                highTerrainTemperatureColor.r,
                Is.GreaterThan(highTerrainTemperatureColor.b),
                "最高地形気温は凡例どおり青より赤が強い必要があります。");

            Color highMoistureColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.BaseMoisture,
                forestTransitionTile);
            Color highWoodyColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.WoodyVegetationPotential,
                forestTransitionTile);
            Color highRuggednessColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.TerrainRuggedness,
                forestTransitionTile);
            Color highCoastalColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.CoastalInfluence,
                forestTransitionTile);
            Color highVolcanicColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.VolcanicActivityPotential,
                forestTransitionTile);
            Assert.That(highMoistureColor.b, Is.GreaterThan(highMoistureColor.r));
            Assert.That(highWoodyColor.g, Is.GreaterThan(highWoodyColor.r));
            Assert.That(highRuggednessColor.r, Is.LessThan(0.3f));
            Assert.That(highCoastalColor.b, Is.GreaterThan(highCoastalColor.r));
            Assert.That(highVolcanicColor.r, Is.GreaterThan(highVolcanicColor.b));

            Color geologyClassColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.GeologyClass,
                forestTransitionTile);
            Color otherGeologyClassColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.GeologyClass,
                new GeneratedTile { GeologyClass = GeologyClass.VolcanicRock });
            Color highPermeabilityColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.GeologyPermeability,
                forestTransitionTile);
            Color highGroundwaterColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.GeologyGroundwaterStorage,
                forestTransitionTile);
            Color highErosionColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.GeologyErosionResistance,
                forestTransitionTile);
            Color karstFlagColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.GeologyFlags,
                forestTransitionTile);
            Color activeVolcanismFlagColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.GeologyFlags,
                new GeneratedTile
                {
                    GeologyFlags = GeologyDefinitionFlags.SupportsActiveVolcanism
                });
            Color firstSiteColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.GeologyDominantSite,
                forestTransitionTile,
                geologySite: new GeologySiteData { MacroCellX = -1, MacroCellY = 2 });
            Color secondSiteColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.GeologyDominantSite,
                forestTransitionTile,
                geologySite: new GeologySiteData { MacroCellX = 0, MacroCellY = 2 });
            Color siteInteriorColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.GeologySiteBoundary,
                forestTransitionTile);
            Color siteBoundaryColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.GeologySiteBoundary,
                forestTransitionTile,
                isGeologySiteBoundary: true);
            Assert.That(geologyClassColor, Is.Not.EqualTo(otherGeologyClassColor));
            Assert.That(highPermeabilityColor.b, Is.GreaterThan(highPermeabilityColor.r));
            Assert.That(highGroundwaterColor.b, Is.GreaterThan(highGroundwaterColor.r));
            Assert.That(highErosionColor.r, Is.LessThan(0.3f));
            Assert.That(karstFlagColor, Is.Not.EqualTo(activeVolcanismFlagColor));
            Assert.That(firstSiteColor, Is.Not.EqualTo(secondSiteColor));
            Assert.That(siteInteriorColor, Is.Not.EqualTo(siteBoundaryColor));

            Color wetlandColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Biome,
                new GeneratedTile { Biome = BiomeId.Wetland });
            Color volcanicColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Biome,
                new GeneratedTile { Biome = BiomeId.Volcanic });
            Assert.That(
                wetlandColor,
                Is.Not.EqualTo(volcanicColor),
                "7バイオーム表示では湿地と火山を別色で判別できる必要があります。");

            Color normalSoilColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Soil,
                new GeneratedTile { Soil = SoilType.Normal });
            Color fertileSoilColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Soil,
                new GeneratedTile { Soil = SoilType.Fertile });
            Color sandySoilColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Soil,
                new GeneratedTile { Soil = SoilType.Sandy });
            Assert.That(normalSoilColor, Is.Not.EqualTo(fertileSoilColor));
            Assert.That(fertileSoilColor, Is.Not.EqualTo(sandySoilColor));
            Assert.That(sandySoilColor, Is.Not.EqualTo(normalSoilColor));

            GeneratedTile waterTile = forestTransitionTile;
            waterTile.Ground = GroundType.Water;
            waterTile.WaterSalinity = WaterSalinity.FreshWater;
            waterTile.WaterBodyType = WaterBodyType.River;

            Color freshWaterColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.WaterSalinity,
                waterTile);
            GeneratedTile saltWaterTile = waterTile;
            saltWaterTile.WaterSalinity = WaterSalinity.SaltWater;
            saltWaterTile.WaterBodyType = WaterBodyType.Sea;
            Color saltWaterColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.WaterSalinity,
                saltWaterTile);
            Assert.That(
                freshWaterColor,
                Is.Not.EqualTo(saltWaterColor),
                "淡水と海水はWater Salinity表示で別の色になる必要があります。");

            Color riverColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.WaterBody,
                waterTile);
            GeneratedTile pondTile = waterTile;
            pondTile.WaterBodyType = WaterBodyType.Pond;
            Color pondColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.WaterBody,
                pondTile);
            Color seaColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.WaterBody,
                saltWaterTile);
            Assert.That(riverColor, Is.Not.EqualTo(pondColor), "川と池は別の色で判別できる必要があります。");
            Assert.That(pondColor, Is.Not.EqualTo(seaColor), "池と海は別の色で判別できる必要があります。");
            Assert.That(riverColor, Is.Not.EqualTo(seaColor), "川と海は別の色で判別できる必要があります。");

            GeneratedTile magmaTile = forestTransitionTile;
            magmaTile.Biome = BiomeId.Volcanic;
            magmaTile.Ground = GroundType.Magma;
            Color magmaColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Magma,
                magmaTile,
                0f,
                GeneratedResourceType.None,
                true);
            Color magmaInfluenceColor =
                WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                    WorldGenerationDebugVisualizationMode.Magma,
                    forestTransitionTile,
                    0f,
                    GeneratedResourceType.None,
                    true);
            Color magmaOutsideColor =
                WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                    WorldGenerationDebugVisualizationMode.Magma,
                    forestTransitionTile);
            Assert.That(magmaColor, Is.Not.EqualTo(magmaInfluenceColor));
            Assert.That(magmaInfluenceColor, Is.Not.EqualTo(magmaOutsideColor));

            Color treeResourceColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Resources,
                forestTransitionTile,
                0f,
                GeneratedResourceType.Tree);
            Color stoneResourceColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Resources,
                forestTransitionTile,
                0f,
                GeneratedResourceType.Stone);
            Color emptyResourceColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Resources,
                forestTransitionTile);
            Assert.That(
                treeResourceColor,
                Is.Not.EqualTo(stoneResourceColor),
                "Resources表示では木と石を別色で判別できる必要があります。");
            Assert.That(
                emptyResourceColor,
                Is.Not.EqualTo(treeResourceColor),
                "Resources表示では配置なしと樹木を別色で判別できる必要があります。");

            Color normalGroundPlacementColor =
                WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                    WorldGenerationDebugVisualizationMode.TerrainPlacement,
                    forestTransitionTile);
            waterTile.GameplayFlags = TileGameplayFlags.AllowsBridge;
            Color waterBridgePlacementColor =
                WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                    WorldGenerationDebugVisualizationMode.TerrainPlacement,
                    waterTile);
            magmaTile.GameplayFlags = TileGameplayFlags.AllowsBridge
                | TileGameplayFlags.RequiresHeatResistance;
            Color magmaBridgePlacementColor =
                WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                    WorldGenerationDebugVisualizationMode.TerrainPlacement,
                    magmaTile);
            Color blockedPlacementColor =
                WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                    WorldGenerationDebugVisualizationMode.TerrainPlacement,
                    new GeneratedTile
                    {
                        Ground = GroundType.RockWall,
                        GameplayFlags = TileGameplayFlags.None
                    });

            // 設備と橋について4つの結果パターンを別色にし、水とマグマで橋の耐熱要件が異なることを目視でも判別できるようにします。
            Assert.That(normalGroundPlacementColor, Is.Not.EqualTo(waterBridgePlacementColor));
            Assert.That(waterBridgePlacementColor, Is.Not.EqualTo(magmaBridgePlacementColor));
            Assert.That(magmaBridgePlacementColor, Is.Not.EqualTo(blockedPlacementColor));
            Assert.That(blockedPlacementColor, Is.Not.EqualTo(normalGroundPlacementColor));

            Color animalIneligibleColor =
                WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                    WorldGenerationDebugVisualizationMode.AnimalPopulation,
                    forestTransitionTile);
            Color animalCandidateColor =
                WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                    WorldGenerationDebugVisualizationMode.AnimalPopulation,
                    forestTransitionTile,
                    isAnimalCandidate: true);
            Color confirmedAnimalColor =
                WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                    WorldGenerationDebugVisualizationMode.AnimalPopulation,
                    forestTransitionTile,
                    isAnimalCandidate: true,
                    hasConfirmedAnimal: true);

            // 候補地形と確定個体を別色にし、地形条件の欠落と抽選結果の欠落をGameビューで切り分けられるようにします。
            Assert.That(animalCandidateColor, Is.Not.EqualTo(animalIneligibleColor));
            Assert.That(confirmedAnimalColor, Is.Not.EqualTo(animalCandidateColor));
            Assert.That(confirmedAnimalColor, Is.Not.EqualTo(animalIneligibleColor));

            Color hiddenColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.None,
                forestTransitionTile);
            Assert.That(hiddenColor.a, Is.EqualTo(0f), "Offモードは地形を一切着色してはいけません。");

            // 可視化関数へ値型を渡しただけで、Simulationから読み取った元タイルの属性を変更してはいけません。
            AssertTileEquals(originalForestTransitionTile, forestTransitionTile, 0);
        }

        /// <summary>
        /// 岩壁の露出面から内部へ連続増加する影不透明度と、共有頂点へ平均する補間値を確認します。
        /// 64×64チャンク境界の両側もワールド座標から同じ規則で分類され、内部だけ資源表示を隠すことを保証します。
        /// </summary>
        [Test]
        public void RockWallAppearance_UsesContinuousGradientAcrossChunkBoundary()
        {
            float exposedOpacity =
                RockWallAppearanceUtility.CalculateShadowOpacityFromNearestOpenDistanceSquared(1);
            float shallowOpacity =
                RockWallAppearanceUtility.CalculateShadowOpacityFromNearestOpenDistanceSquared(4);
            float middleOpacity =
                RockWallAppearanceUtility.CalculateShadowOpacityFromNearestOpenDistanceSquared(16);
            float deepOpacity =
                RockWallAppearanceUtility.CalculateShadowOpacityFromNearestOpenDistanceSquared(
                    int.MaxValue);
            Assert.That(exposedOpacity, Is.EqualTo(0f));
            Assert.That(shallowOpacity, Is.GreaterThan(exposedOpacity));
            Assert.That(
                shallowOpacity,
                Is.GreaterThan(0.5f),
                "露出面から1タイル内側の影が薄すぎる状態へ戻ってはいけません。");
            Assert.That(middleOpacity, Is.GreaterThan(shallowOpacity));
            Assert.That(
                deepOpacity,
                Is.EqualTo(RockWallAppearanceUtility.MaximumShadowOpacity));

            // 共有頂点は周囲4タイルの中間値になり、1タイル全面を同じ暗色にする旧方式へ戻っていないことを保証します。
            float interpolatedVertexOpacity = RockWallAppearanceUtility.CalculateVertexOpacity(
                0f,
                shallowOpacity,
                middleOpacity,
                deepOpacity);
            Assert.That(interpolatedVertexOpacity, Is.GreaterThan(shallowOpacity));
            Assert.That(interpolatedVertexOpacity, Is.LessThan(deepOpacity));
            Assert.That(
                RockWallAppearanceUtility.ShouldDisplayResource(RockWallAppearance.Interior),
                Is.False,
                "岩壁内部の資源アイコンは影の上へ表示してはいけません。");
            Assert.That(
                RockWallAppearanceUtility.ShouldDisplayResource(RockWallAppearance.Exposed),
                Is.True,
                "空間へ露出した岩壁の資源表示は内部影の非表示規則へ巻き込んではいけません。");
            Assert.That(
                RockWallAppearanceUtility.ShouldDisplayResource(RockWallAppearance.None),
                Is.True,
                "通常地面の資源アイコンは岩壁用の非表示規則へ巻き込んではいけません。");

            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("rock-wall-chunk-edge-test"),
                1);
            // 地上18Biomeの固定標高範囲と両立する最小海面を使い、無効設定へ依存せず岩壁境界だけを検証します。
            settings.SeaLevelThreshold =
                WorldGenerationSettings.MinimumSupportedSeaLevelThreshold;
            settings.FreshWaterMaximumElevation =
                WorldGenerationSettings.MinimumSupportedSeaLevelThreshold;
            settings.RiverCenterLineHalfWidth = 0;
            settings.PondThreshold = ushort.MaxValue;
            bool foundShadowAcrossChunkBoundary = false;

            // X=63とX=64は別チャンクです。境界をまたぐ岩壁を探し、両側が座標再生成で影分類できることを確認します。
            for (int tileY = -256; tileY <= 256; tileY++)
            {
                RockWallAppearance left =
                    RockWallAppearanceUtility.ClassifyAt(settings, 63, tileY);
                RockWallAppearance right =
                    RockWallAppearanceUtility.ClassifyAt(settings, 64, tileY);
                if (left != RockWallAppearance.None && right != RockWallAppearance.None)
                {
                    foundShadowAcrossChunkBoundary = true;
                    break;
                }
            }

            Assert.That(
                foundShadowAcrossChunkBoundary,
                Is.True,
                "64×64チャンク境界をまたぐ岩壁を試験範囲で確認できませんでした。");

            // 境界上の座標版も0〜最大値へ収まり、未ロード隣接チャンクを含む距離探索で例外や範囲外値を出さないことを確認します。
            float boundaryOpacity =
                RockWallAppearanceUtility.CalculateShadowOpacityAt(settings, 63, 0);
            Assert.That(
                boundaryOpacity,
                Is.InRange(0f, RockWallAppearanceUtility.MaximumShadowOpacity));

            GeneratedTile rockWallTile = new GeneratedTile { Ground = GroundType.RockWall };
            Color exposedColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.RockWall,
                rockWallTile,
                0f);
            Color deepShadowColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.RockWall,
                rockWallTile,
                RockWallAppearanceUtility.MaximumShadowOpacity);
            Assert.That(
                exposedColor,
                Is.Not.EqualTo(deepShadowColor),
                "岩壁デバッグ表示では露出面と深い内部影を別色で確認できる必要があります。");
        }

        /// <summary>
        /// SampleSceneを読み込み、初回表示、遠方移動時のアンロード、原点へ戻った再生成の決定論を検証します。
        /// </summary>
        /// <returns>非同期Sceneロードと複数フレームの生成・表示完了を待つIEnumeratorです。</returns>
        [UnityTest]
        public IEnumerator SampleScene_StreamsAndDeterministicallyRegeneratesTerrainTiles()
        {
            return VerifySampleSceneStreaming(true);
        }

        /// <summary>旧共有方式でも同じScene連携・決定論・表示契約を維持することを確認します。</summary>
        /// <returns>共有方式に切り替えて共通の縦切り検証を進めるIEnumeratorです。</returns>
        [UnityTest]
        public IEnumerator SampleScene_SharedModeStreamsAndRegeneratesTerrainTiles()
        {
            return VerifySampleSceneStreaming(false);
        }

        /// <summary>
        /// 採用済みのチャンク別Tilemap方式で、自動Floating Originを正負へ繰り返し、再訪したチャンクが重複せず同じ内容になることを保証します。
        /// MAP-02Eの通常回帰として、複数回の表示原点変更、東西往復、0境界横断を一つのPlayModeセッション内で実行します。
        /// </summary>
        /// <returns>各移動先の非同期生成・表示完了を、フレーム上限付きで待つIEnumeratorです。</returns>
        [UnityTest]
        public IEnumerator SampleScene_RepeatedAutomaticRebasesPreserveStreamingAcrossRoundTripAndNegativeCrossing()
        {
            AsyncOperation loadOperation = SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            Assert.That(loadOperation, Is.Not.Null, "Build SettingsからSampleSceneを読み込めませんでした。");

            while (!loadOperation.isDone)
            {
                yield return null;
            }

            CameraPanController cameraPanController = Object.FindFirstObjectByType<CameraPanController>();
            InitialCameraPositionController initialPositionController =
                Object.FindFirstObjectByType<InitialCameraPositionController>();
            ChunkTilemapPresenter terrainPresenter = Object.FindFirstObjectByType<ChunkTilemapPresenter>();
            Tilemap groundTilemap = GameObject.Find("World Grid/Ground Tilemap")?.GetComponent<Tilemap>();
            Tilemap liquidBaseTilemap = GameObject.Find("World Grid/Liquid Base Tilemap")?.GetComponent<Tilemap>();
            Assert.That(cameraPanController, Is.Not.Null, "Main CameraにCameraPanControllerがありません。");
            Assert.That(initialPositionController, Is.Not.Null, "Main CameraにInitialCameraPositionControllerがありません。");
            Assert.That(terrainPresenter, Is.Not.Null, "SampleSceneにChunkTilemapPresenterがありません。");
            Assert.That(groundTilemap, Is.Not.Null, "SampleSceneにGround Tilemapがありません。");
            Assert.That(liquidBaseTilemap, Is.Not.Null, "SampleSceneにLiquid Base Tilemapがありません。");

            // MAP-02Dで採用した方式だけを継続回帰の対象にし、診断用の旧共有方式との二重実行で通常テストを重くしません。
            terrainPresenter.ConfigureChunkLocalPresentation(true);

            World world = null;
            EntityQuery settingsQuery = default;
            EntityQuery locationQuery = default;
            EntityQuery observerQuery = default;
            EntityQuery chunkQuery = default;
            bool queriesCreated = false;
            bool sceneReady = false;

            for (int frame = 0; frame < MaximumWaitFrames; frame++)
            {
                world = World.DefaultGameObjectInjectionWorld;
                if (world != null && world.IsCreated && !queriesCreated)
                {
                    settingsQuery = world.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<WorldGenerationSettings>());
                    locationQuery = world.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<InitialSpawnLocation>());
                    observerQuery = world.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<ChunkStreamingObserver>());
                    chunkQuery = world.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<ChunkCoordinateComponent>(),
                        ComponentType.ReadOnly<ChunkGenerationStatus>(),
                        ComponentType.ReadOnly<GeneratedTile>(),
                        ComponentType.ReadOnly<GeneratedResourceTile>());
                    queriesCreated = true;
                }

                // 初期スポーン適用が完了する前にテスト座標へ動かすと、初期化側が次フレームにCameraを上書きするため待機します。
                if (queriesCreated
                    && settingsQuery.CalculateEntityCount() == 1
                    && locationQuery.CalculateEntityCount() == 1
                    && initialPositionController.HasCompletedInitialPositioning)
                {
                    sceneReady = true;
                    break;
                }

                yield return null;
            }

            Assert.That(sceneReady, Is.True, $"{MaximumWaitFrames}フレーム以内にScene初期化が完了しませんでした。");
            WorldGenerationSettings initialSettings = settingsQuery.GetSingleton<WorldGenerationSettings>();
            InitialSpawnLocation initialSpawnLocation = locationQuery.GetSingleton<InitialSpawnLocation>();

            // 開始地点をチャンク(0, 0)へ固定し、以降は実際の通常移動と同じ相対座標APIだけで原点変更を起こします。
            Assert.That(cameraPanController.SetAbsoluteWorldPosition(32.5d, 32.5d), Is.True);

            // 各入力は「現在の表示原点からの相対座標」です。同じ値でも直前の原点が違えば別の絶対座標へ進みます。
            // 東へ2回、西へ0境界を越えて2回、東へ戻り、既訪問の東端・負座標・開始地点を順に再訪します。
            Vector2[] relativeMoves =
            {
                new Vector2(544.5f, 32.5f),
                new Vector2(544.5f, 32.5f),
                new Vector2(-544.5f, 32.5f),
                new Vector2(-544.5f, 32.5f),
                new Vector2(544.5f, 32.5f),
                new Vector2(672.5f, 32.5f),
                new Vector2(-1120.5f, 32.5f),
                new Vector2(160.5f, 32.5f)
            };
            double2[] expectedAbsolutePositions =
            {
                new double2(32.5d, 32.5d),
                new double2(544.5d, 32.5d),
                new double2(1056.5d, 32.5d),
                new double2(479.5d, 32.5d),
                new double2(-96.5d, 32.5d),
                new double2(416.5d, 32.5d),
                new double2(1056.5d, 32.5d),
                new double2(-96.5d, 32.5d),
                new double2(32.5d, 32.5d)
            };
            int2[] expectedDisplayOrigins =
            {
                new int2(0, 0),
                new int2(512, 0),
                new int2(1024, 0),
                new int2(448, 0),
                new int2(-128, 0),
                new int2(384, 0),
                new int2(1024, 0),
                new int2(-128, 0),
                new int2(-128, 0)
            };

            // 再訪前のEntityと代表セルを退避し、アンロード後の別Entityでも同じ決定論的内容になることを検証します。
            var lastEntityByCoordinate = new Dictionary<ChunkCoordinate, Entity>();
            var terrainSamplesByCoordinate = new Dictionary<ChunkCoordinate, GeneratedTile[]>();
            var resourceSamplesByCoordinate = new Dictionary<ChunkCoordinate, GeneratedResourceTile[]>();
            var visitedDisplayOrigins = new HashSet<int2>();
            int revisitedChunkCount = 0;

            // 合否閾値は未決定なので時間の上限は設けず、回帰時に比較できる実測値だけをNUnitログへ残します。
            using ProfilerRecorder streamingRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Scripts,
                "Farmware.WorldGeneration.ChunkStreaming",
                LongDistanceProfilerSampleCapacity);
            using ProfilerRecorder presentationRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Scripts,
                "Farmware.WorldGeneration.TilemapPresentation",
                LongDistanceProfilerSampleCapacity);
            using ProfilerRecorder rebaseRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Scripts,
                "Farmware.WorldGeneration.FloatingOriginRebase",
                LongDistanceProfilerSampleCapacity);
            // C3-D1では未完了Jobを固定Tick入口で待たず、完成確認後のHandle確定だけを行います。
            // StreamingやTilemapだけでは回帰を見落とすため、境界処理の残存時間も同じ移動区間で記録します。
            using ProfilerRecorder boundaryWaitRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Scripts,
                "Farmware.WorldGeneration.ChunkGenerationBoundaryWait",
                LongDistanceProfilerSampleCapacity);
            // 完成Handle確定の内訳を基礎水文・湖・地形へ分け、同期待ちが再混入した場合に系列を特定します。
            using ProfilerRecorder hydrologyBaseWaitRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Scripts,
                "Farmware.WorldGeneration.ChunkGenerationHydrologyBaseWait",
                LongDistanceProfilerSampleCapacity);
            using ProfilerRecorder lakeWaitRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Scripts,
                "Farmware.WorldGeneration.ChunkGenerationLakeWait",
                LongDistanceProfilerSampleCapacity);
            using ProfilerRecorder terrainWaitRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Scripts,
                "Farmware.WorldGeneration.ChunkGenerationTerrainWait",
                LongDistanceProfilerSampleCapacity);

            try
            {
                for (int checkpointIndex = 0;
                     checkpointIndex < expectedAbsolutePositions.Length;
                     checkpointIndex++)
                {
                    int2 previousDisplayOrigin =
                        cameraPanController.CurrentDisplayOrigin.AbsoluteTileCoordinate;
                    if (checkpointIndex > 0)
                    {
                        cameraPanController.SetWorldPosition(relativeMoves[checkpointIndex - 1]);
                    }

                    int2 actualDisplayOrigin =
                        cameraPanController.CurrentDisplayOrigin.AbsoluteTileCoordinate;
                    double2 actualAbsolutePosition = cameraPanController.GetAbsoluteWorldPosition();
                    Assert.That(
                        actualDisplayOrigin,
                        Is.EqualTo(expectedDisplayOrigins[checkpointIndex]),
                        $"checkpoint={checkpointIndex}: 表示原点が期待する64タイル境界と一致しません。");
                    Assert.That(
                        actualAbsolutePosition.x,
                        Is.EqualTo(expectedAbsolutePositions[checkpointIndex].x).Within(0.00001d),
                        $"checkpoint={checkpointIndex}: 原点変更で絶対Camera Xが変化しました。");
                    Assert.That(
                        actualAbsolutePosition.y,
                        Is.EqualTo(expectedAbsolutePositions[checkpointIndex].y).Within(0.00001d),
                        $"checkpoint={checkpointIndex}: 原点変更で絶対Camera Yが変化しました。");

                    if (!actualDisplayOrigin.Equals(previousDisplayOrigin))
                    {
                        Assert.That(
                            cameraPanController.LastDisplayOriginChangeWasAutomatic,
                            Is.True,
                            $"checkpoint={checkpointIndex}: 通常移動による原点変更が明示移動として扱われました。");
                    }

                    visitedDisplayOrigins.Add(actualDisplayOrigin);
                    int targetTileX = (int)System.Math.Floor(actualAbsolutePosition.x);
                    int targetTileY = (int)System.Math.Floor(actualAbsolutePosition.y);
                    ChunkCoordinate targetChunk = WorldGrid.ToChunkCoordinate(targetTileX, targetTileY);
                    bool checkpointCompleted = false;
                    Entity targetChunkEntity = Entity.Null;

                    for (int frame = 0; frame < MaximumWaitFrames; frame++)
                    {
                        bool hasSingleObserver = observerQuery.CalculateEntityCount() == 1;
                        ChunkStreamingObserver observer = hasSingleObserver
                            ? observerQuery.GetSingleton<ChunkStreamingObserver>()
                            : default;
                        bool observerContainsTarget = hasSingleObserver
                            && observer.MinimumVisibleChunkX <= targetChunk.X
                            && observer.MaximumVisibleChunkX >= targetChunk.X
                            && observer.MinimumVisibleChunkY <= targetChunk.Y
                            && observer.MaximumVisibleChunkY >= targetChunk.Y;
                        bool targetIsActive = TryGetActiveCompleteChunk(
                            world.EntityManager,
                            chunkQuery,
                            targetChunk.X,
                            targetChunk.Y,
                            out targetChunkEntity);
                        Vector3Int displayCell = ToDisplayCell(
                            targetTileX,
                            targetTileY,
                            cameraPanController.CurrentDisplayOrigin);
                        bool targetIsDisplayed = GetPresentedTile(
                                terrainPresenter,
                                groundTilemap,
                                displayCell,
                                cameraPanController.CurrentDisplayOrigin) != null
                            || GetPresentedTile(
                                terrainPresenter,
                                liquidBaseTilemap,
                                displayCell,
                                cameraPanController.CurrentDisplayOrigin) != null;

                        if (observerContainsTarget && targetIsActive && targetIsDisplayed)
                        {
                            checkpointCompleted = true;
                            break;
                        }

                        // BatchModeは描画フレームだけが実時間より速く進むため、Workerと10 Tick/秒の境界へ最低限の実時間を渡します。
                        System.Threading.Thread.Sleep(BatchModeWorkerYieldMilliseconds);
                        yield return null;
                    }

                    Assert.That(
                        checkpointCompleted,
                        Is.True,
                        $"checkpoint={checkpointIndex}, chunk={targetChunk}: {MaximumWaitFrames}フレーム以内に生成・表示が完了しませんでした。");
                    Assert.That(
                        CountChunksAt(chunkQuery, targetChunk.X, targetChunk.Y),
                        Is.EqualTo(1),
                        $"checkpoint={checkpointIndex}, chunk={targetChunk}: 同じ絶対チャンクが重複しています。");
                    CaptureOrAssertChunkSamples(
                        world.EntityManager,
                        targetChunkEntity,
                        targetChunk,
                        lastEntityByCoordinate,
                        terrainSamplesByCoordinate,
                        resourceSamplesByCoordinate,
                        ref revisitedChunkCount);

                    // Floating OriginはPresentationだけの変更なので、生成設定と記録済み初期スポーンを一切書き換えてはいけません。
                    Assert.That(settingsQuery.GetSingleton<WorldGenerationSettings>(), Is.EqualTo(initialSettings));
                    Assert.That(locationQuery.GetSingleton<InitialSpawnLocation>(), Is.EqualTo(initialSpawnLocation));
                }

                Assert.That(visitedDisplayOrigins.Count, Is.GreaterThanOrEqualTo(6));
                Assert.That(revisitedChunkCount, Is.GreaterThanOrEqualTo(3));
                Assert.That(streamingRecorder.Valid, Is.True, "ChunkStreaming Markerを記録できませんでした。");
                Assert.That(presentationRecorder.Valid, Is.True, "TilemapPresentation Markerを記録できませんでした。");
                Assert.That(rebaseRecorder.Valid, Is.True, "FloatingOriginRebase Markerを記録できませんでした。");
                Assert.That(boundaryWaitRecorder.Valid, Is.True, "ChunkGenerationBoundaryWait Markerを記録できませんでした。");
                Assert.That(hydrologyBaseWaitRecorder.Valid, Is.True, "ChunkGenerationHydrologyBaseWait Markerを記録できませんでした。");
                Assert.That(lakeWaitRecorder.Valid, Is.True, "ChunkGenerationLakeWait Markerを記録できませんでした。");
                Assert.That(terrainWaitRecorder.Valid, Is.True, "ChunkGenerationTerrainWait Markerを記録できませんでした。");

                long streamingMaximumNanoseconds = GetMaximumProfilerValue(streamingRecorder);
                long presentationMaximumNanoseconds = GetMaximumProfilerValue(presentationRecorder);
                long rebaseMaximumNanoseconds = GetMaximumProfilerValue(rebaseRecorder);
                long boundaryWaitMaximumNanoseconds = GetMaximumProfilerValue(boundaryWaitRecorder);
                long hydrologyBaseWaitMaximumNanoseconds = GetMaximumProfilerValue(hydrologyBaseWaitRecorder);
                long lakeWaitMaximumNanoseconds = GetMaximumProfilerValue(lakeWaitRecorder);
                long terrainWaitMaximumNanoseconds = GetMaximumProfilerValue(terrainWaitRecorder);
                Assert.That(streamingMaximumNanoseconds, Is.GreaterThan(0L));
                Assert.That(presentationMaximumNanoseconds, Is.GreaterThan(0L));
                Assert.That(rebaseMaximumNanoseconds, Is.GreaterThan(0L));
                Assert.That(boundaryWaitMaximumNanoseconds, Is.GreaterThan(0L));
                Assert.That(hydrologyBaseWaitMaximumNanoseconds, Is.GreaterThan(0L));
                Assert.That(lakeWaitMaximumNanoseconds, Is.GreaterThan(0L));
                Assert.That(terrainWaitMaximumNanoseconds, Is.GreaterThan(0L));
                Debug.Log(
                    "MAP-02E Profiler記録: "
                    + $"ChunkStreaming最大={streamingMaximumNanoseconds / 1000000d:F3}ms, "
                    + $"TilemapPresentation最大={presentationMaximumNanoseconds / 1000000d:F3}ms, "
                    + $"FloatingOriginRebase最大={rebaseMaximumNanoseconds / 1000000d:F3}ms, "
                    + $"ChunkGenerationBoundaryWait最大={boundaryWaitMaximumNanoseconds / 1000000d:F3}ms, "
                    + $"HydrologyBaseWait最大={hydrologyBaseWaitMaximumNanoseconds / 1000000d:F3}ms, "
                    + $"LakeWait最大={lakeWaitMaximumNanoseconds / 1000000d:F3}ms, "
                    + $"TerrainWait最大={terrainWaitMaximumNanoseconds / 1000000d:F3}ms, "
                    + $"再訪チャンク数={revisitedChunkCount}, 表示原点数={visitedDisplayOrigins.Count}");
            }
            finally
            {
                if (queriesCreated && world != null && world.IsCreated)
                {
                    settingsQuery.Dispose();
                    locationQuery.Dispose();
                    observerQuery.Dispose();
                    chunkQuery.Dispose();
                }
            }
        }

        /// <summary>Sceneを読み込み、指定表示方式で地形・液面・資源・診断表示と再生成を実際に検証します。</summary>
        /// <param name="local">既定のチャンク別方式ならtrue、旧共有方式を検証するときはfalseです。</param>
        /// <returns>SceneロードとECS・表示の完了をフレーム上限付きで待つIEnumeratorです。</returns>
        private IEnumerator VerifySampleSceneStreaming(bool local)
        {
            AsyncOperation loadOperation = SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            Assert.That(loadOperation, Is.Not.Null, "Build SettingsからSampleSceneを読み込めませんでした。");

            while (!loadOperation.isDone)
            {
                yield return null;
            }

            GameObject groundTilemapObject = GameObject.Find("World Grid/Ground Tilemap");
            Tilemap tilemap = groundTilemapObject == null
                ? null
                : groundTilemapObject.GetComponent<Tilemap>();
            Assert.That(tilemap, Is.Not.Null, "SampleSceneに地形表示用Ground Tilemapがありません。");

            ChunkTilemapPresenter terrainPresenter =
                Object.FindFirstObjectByType<ChunkTilemapPresenter>();
            Assert.That(
                terrainPresenter,
                Is.Not.Null,
                "SampleSceneに地形Tileを割り当てるChunkTilemapPresenterがありません。");

            string[] biomeTileFieldNames =
            {
                "grasslandTile",
                "forestTile",
                "desertTile",
                "rocklandTile",
                "wetlandTile",
                "snowfieldTile",
                "volcanicTile"
            };
            string[] expectedBiomeSpriteNames =
            {
                "terrain_grassland",
                "terrain_forest",
                "terrain_desert",
                "terrain_rockland",
                "terrain_wetland",
                "terrain_snowfield",
                "terrain_volcanic"
            };

            // Sceneの参照先まで検証し、Tileアセットだけ作成されて表示には旧Fallbackが残る回帰を防ぎます。
            for (int biomeIndex = 0; biomeIndex < biomeTileFieldNames.Length; biomeIndex++)
            {
                FieldInfo tileField = typeof(ChunkTilemapPresenter).GetField(
                    biomeTileFieldNames[biomeIndex],
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(
                    tileField,
                    Is.Not.Null,
                    $"Biome Tileフィールドが見つかりません: {biomeTileFieldNames[biomeIndex]}");

                Tile biomeTile = tileField.GetValue(terrainPresenter) as Tile;
                Assert.That(
                    biomeTile,
                    Is.Not.Null,
                    $"SampleSceneのBiome Tile参照がありません: {biomeTileFieldNames[biomeIndex]}");
                Assert.That(
                    biomeTile.sprite,
                    Is.Not.Null,
                    $"SampleSceneのBiome TileにSpriteがありません: {biomeTileFieldNames[biomeIndex]}");
                Assert.That(
                    biomeTile.sprite.name,
                    Is.EqualTo(expectedBiomeSpriteNames[biomeIndex]),
                    $"SampleSceneが本番用Biome Spriteを参照していません: {biomeTileFieldNames[biomeIndex]}");
            }

            // 専用TileアセットとSceneの参照を同時に確認し、構築ツールの再実行でFallback岩壁へ戻る回帰を検出します。
            FieldInfo rockWallTileField = typeof(ChunkTilemapPresenter).GetField(
                "rockWallTile",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(rockWallTileField, Is.Not.Null, "岩壁Tileフィールドが見つかりません。");
            Tile productionRockWallTile = rockWallTileField.GetValue(terrainPresenter) as Tile;
            Assert.That(productionRockWallTile, Is.Not.Null, "SampleSceneの岩壁Tile参照がありません。");
            Assert.That(
                productionRockWallTile.sprite,
                Is.Not.Null,
                "SampleSceneの岩壁TileにSpriteがありません。");
            Assert.That(
                productionRockWallTile.sprite.name,
                Is.EqualTo("terrain_rock_wall_01"),
                "SampleSceneが本番用岩壁Spriteを参照していません。");

            GameObject resourceTilemapObject = GameObject.Find("World Grid/Resource Tilemap");
            Tilemap resourceTilemap = resourceTilemapObject == null
                ? null
                : resourceTilemapObject.GetComponent<Tilemap>();
            Assert.That(resourceTilemap, Is.Not.Null, "SampleSceneに資源表示用Resource Tilemapがありません。");
            GameObject resourceBehindRockWallTilemapObject =
                GameObject.Find("World Grid/Resource Behind Rock Wall Tilemap");
            Tilemap resourceBehindRockWallTilemap = resourceBehindRockWallTilemapObject == null
                ? null
                : resourceBehindRockWallTilemapObject.GetComponent<Tilemap>();
            Assert.That(
                resourceBehindRockWallTilemap,
                Is.Not.Null,
                "SampleSceneに手前岩壁で隠す資源専用Tilemapがありません。");
            GameObject liquidBaseTilemapObject =
                GameObject.Find("World Grid/Liquid Base Tilemap");
            Tilemap liquidBaseTilemap = liquidBaseTilemapObject == null
                ? null
                : liquidBaseTilemapObject.GetComponent<Tilemap>();
            Assert.That(
                liquidBaseTilemap,
                Is.Not.Null,
                "SampleSceneに8px下げた静止液面専用Tilemapがありません。");
            Assert.That(
                liquidBaseTilemap.transform.localPosition.y,
                Is.EqualTo(TerrainLayerPresentationUtility.LiquidSurfaceOffsetInCells)
                    .Within(0.0001f),
                "静止した水・マグマ面は論理セルを変えず、表示だけ8px下へ移動する必要があります。");
            GameObject liquidAnimationTilemapObject =
                GameObject.Find("World Grid/Liquid Surface Animation Tilemap");
            Tilemap liquidAnimationTilemap = liquidAnimationTilemapObject == null
                ? null
                : liquidAnimationTilemapObject.GetComponent<Tilemap>();
            Assert.That(
                liquidAnimationTilemap,
                Is.Not.Null,
                "SampleSceneに液面アニメーション専用Tilemapがありません。");
            Assert.That(
                liquidAnimationTilemap.animationFrameRate,
                Is.EqualTo(4f),
                "液面アニメーションは仮素材の4fps設定を保持する必要があります。");
            Assert.That(
                liquidAnimationTilemap.transform.localPosition.y,
                Is.EqualTo(TerrainLayerPresentationUtility.LiquidSurfaceOffsetInCells)
                    .Within(0.0001f),
                "波・気泡は静止液面と同じ8px下へ移動し、上下へ分離してはいけません。");
            Assert.That(
                GameObject.Find("World Grid/Terrain Front Edge Tilemap"),
                Is.Null,
                "土地断面を一体化したため、旧Terrain Front Edge Tilemapを残してはいけません。");
            Assert.That(
                GameObject.Find("World Grid/Rock Wall Front Face Tilemap"),
                Is.Null,
                "岩壁側面を一体化したため、旧Rock Wall Front Face Tilemapを残してはいけません。");
            Assert.That(
                GameObject.Find("World Grid/Rock Wall Top Tilemap"),
                Is.Null,
                "一体型岩壁へ改名したため、旧Rock Wall Top Tilemapを残してはいけません。");
            GameObject rockWallTilemapObject =
                GameObject.Find("World Grid/Rock Wall Tilemap");
            Tilemap rockWallTilemap = rockWallTilemapObject == null
                ? null
                : rockWallTilemapObject.GetComponent<Tilemap>();
            Assert.That(
                rockWallTilemap,
                Is.Not.Null,
                "SampleSceneに16×28の一体型岩壁専用Tilemapがありません。");
            Assert.That(
                rockWallTilemap.transform.localPosition.y,
                Is.EqualTo(0f).Within(0.0001f),
                "岩壁の高さはSprite Pivotで表すため、Tilemap Transformを移動してはいけません。");
            GameObject rockWallResourceTilemapObject =
                GameObject.Find("World Grid/Rock Wall Resource Tilemap");
            Tilemap rockWallResourceTilemap = rockWallResourceTilemapObject == null
                ? null
                : rockWallResourceTilemapObject.GetComponent<Tilemap>();
            Assert.That(
                rockWallResourceTilemap,
                Is.Not.Null,
                "SampleSceneに岩壁上資源専用Tilemapがありません。");
            Assert.That(
                rockWallResourceTilemap.transform.localPosition.y,
                Is.EqualTo(TerrainLayerPresentationUtility.RockWallElevationInCells).Within(0.0001f),
                "岩壁上資源は岩壁上面と同じ12px上へ移動している必要があります。");
            GameObject landBoundaryHighlightTilemapObject =
                GameObject.Find("World Grid/Land Boundary Highlight Tilemap");
            Tilemap landBoundaryHighlightTilemap = landBoundaryHighlightTilemapObject == null
                ? null
                : landBoundaryHighlightTilemapObject.GetComponent<Tilemap>();
            Assert.That(
                landBoundaryHighlightTilemap,
                Is.Not.Null,
                "SampleSceneに通常土地の境界ハイライト専用Tilemapがありません。");
            Assert.That(
                landBoundaryHighlightTilemap.transform.localPosition.y,
                Is.EqualTo(0f).Within(0.0001f),
                "通常土地の境界ハイライトは地表と同じ高さへ配置する必要があります。");
            GameObject rockWallBoundaryHighlightTilemapObject =
                GameObject.Find("World Grid/Rock Wall Boundary Highlight Tilemap");
            Tilemap rockWallBoundaryHighlightTilemap =
                rockWallBoundaryHighlightTilemapObject == null
                    ? null
                    : rockWallBoundaryHighlightTilemapObject.GetComponent<Tilemap>();
            Assert.That(
                rockWallBoundaryHighlightTilemap,
                Is.Not.Null,
                "SampleSceneに岩壁上面の境界ハイライト専用Tilemapがありません。");
            Assert.That(
                rockWallBoundaryHighlightTilemap.transform.localPosition.y,
                Is.EqualTo(TerrainLayerPresentationUtility.RockWallElevationInCells)
                    .Within(0.0001f),
                "岩壁境界ハイライトは岩壁上面と同じ12px上へ移動する必要があります。");

            FieldInfo boundarySpritesField = typeof(ChunkTilemapPresenter).GetField(
                "boundaryHighlightSprites",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(boundarySpritesField, Is.Not.Null, "境界ハイライトSprite配列が見つかりません。");
            Sprite[] boundarySprites =
                boundarySpritesField.GetValue(terrainPresenter) as Sprite[];
            Assert.That(
                boundarySprites,
                Has.Length.EqualTo(TerrainBoundaryHighlightUtility.ExpectedSpriteCount),
                "SampleSceneには左から右への14基本Spriteを保存する必要があります。");
            for (int index = 0; index < boundarySprites.Length; index++)
            {
                Assert.That(boundarySprites[index], Is.Not.Null, $"境界Sprite参照がありません: index={index}");
                Assert.That(
                    boundarySprites[index].name,
                    Is.EqualTo($"terrain_boundary_highlight_{index + 1:00}"),
                    $"境界Spriteの順序が画像の左から右と一致しません: index={index}");
            }

            FieldInfo boundaryOpacityField = typeof(ChunkTilemapPresenter).GetField(
                "boundaryHighlightOpacity",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(boundaryOpacityField, Is.Not.Null, "境界ハイライトの不透明度設定が見つかりません。");
            Assert.That(
                (float)boundaryOpacityField.GetValue(terrainPresenter),
                Is.EqualTo(TerrainBoundaryHighlightUtility.DefaultOpacity).Within(0.0001f),
                "境界ハイライトは既定の20%不透明度でBase Tileへ馴染ませる必要があります。");

            FieldInfo generatedBoundaryTilesField = typeof(ChunkTilemapPresenter).GetField(
                "generatedBoundaryHighlightTiles",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(generatedBoundaryTilesField, Is.Not.Null, "実行時境界Tile配列が見つかりません。");
            Tile[,] generatedBoundaryTiles =
                generatedBoundaryTilesField.GetValue(terrainPresenter) as Tile[,];
            Assert.That(generatedBoundaryTiles, Is.Not.Null, "境界Spriteから実行時Tileが生成されていません。");
            Assert.That(
                generatedBoundaryTiles[0, 0],
                Is.Not.Null,
                "不透明度を検証する境界Tileが生成されていません。");
            Assert.That(
                generatedBoundaryTiles[0, 0].color.a,
                Is.EqualTo(TerrainBoundaryHighlightUtility.DefaultOpacity).Within(0.0001f),
                "実行時Tileへ20%の不透明度が反映されていません。");

            Assert.That(
                tilemap.transform.parent.GetComponentsInChildren<Tilemap>(includeInactive: true),
                Has.Length.EqualTo(9),
                "World Gridには既存7層と境界ハイライト2層の合計9 Tilemapが必要です。");

            // 液面の下半分を手前土地で隠す前後関係はTransform位置だけでは決まらないため、Rendererの描画順もScene実体から確認します。
            TilemapRenderer groundRenderer = tilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer rockWallRenderer = rockWallTilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer resourceRenderer = resourceTilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer resourceBehindRockWallRenderer =
                resourceBehindRockWallTilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer liquidBaseRenderer = liquidBaseTilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer liquidAnimationRenderer =
                liquidAnimationTilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer landBoundaryHighlightRenderer =
                landBoundaryHighlightTilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer rockWallBoundaryHighlightRenderer =
                rockWallBoundaryHighlightTilemap.GetComponent<TilemapRenderer>();
            Assert.That(groundRenderer, Is.Not.Null, "Ground TilemapにTilemapRendererがありません。");
            Assert.That(rockWallRenderer, Is.Not.Null, "Rock Wall TilemapにTilemapRendererがありません。");
            Assert.That(resourceRenderer, Is.Not.Null, "Resource TilemapにTilemapRendererがありません。");
            Assert.That(
                resourceBehindRockWallRenderer,
                Is.Not.Null,
                "Resource Behind Rock Wall TilemapにTilemapRendererがありません。");
            Assert.That(
                groundRenderer.mode,
                Is.EqualTo(TilemapRenderer.Mode.Individual),
                "16×24土地はセルごとの行ソートが必要なためIndividual Modeで描画してください。");
            Assert.That(
                rockWallRenderer.mode,
                Is.EqualTo(TilemapRenderer.Mode.Individual),
                "16×28岩壁はセルごとの行ソートが必要なためIndividual Modeで描画してください。");
            Assert.That(
                groundRenderer.sortOrder,
                Is.EqualTo(TilemapRenderer.SortOrder.TopLeft),
                "16×24土地は奥の高いYから先に描き、手前の低いYを後から重ねてください。");
            Assert.That(
                rockWallRenderer.sortOrder,
                Is.EqualTo(TilemapRenderer.SortOrder.TopLeft),
                "16×28岩壁は奥の高いYから先に描き、手前の低いYを後から重ねてください。");
            Assert.That(
                resourceBehindRockWallRenderer.sortingOrder,
                Is.LessThan(rockWallRenderer.sortingOrder),
                "直前方が岩壁の通常資源は、岩壁より先に描いて重なる下部を隠してください。");
            Assert.That(
                resourceRenderer.sortingOrder,
                Is.GreaterThan(rockWallRenderer.sortingOrder),
                "岩壁に隠れない通常資源は、岩壁より後から描いて前面へ表示してください。");
            Assert.That(
                landBoundaryHighlightRenderer.sortingOrder,
                Is.EqualTo(1),
                "通常土地の境界はGroundより前、岩壁に隠す資源より後ろへ描画してください。");
            Assert.That(
                rockWallBoundaryHighlightRenderer.sortingOrder,
                Is.EqualTo(4),
                "岩壁上面の境界は岩壁より前、通常資源より後ろへ描画してください。");
            Assert.That(
                liquidBaseRenderer,
                Is.Not.Null,
                "Liquid Base TilemapにTilemapRendererがありません。");
            Assert.That(
                liquidAnimationRenderer,
                Is.Not.Null,
                "Liquid Surface Animation TilemapにTilemapRendererがありません。");
            Assert.That(
                liquidBaseRenderer.sortingOrder,
                Is.LessThan(liquidAnimationRenderer.sortingOrder),
                "静止液面は透明な波アニメーションより奥へ描画する必要があります。");
            Assert.That(
                liquidAnimationRenderer.sortingOrder,
                Is.LessThan(groundRenderer.sortingOrder),
                "8px下げた液面の下辺は、手前にある土地上面より奥へ描画する必要があります。");
            CameraPanController cameraPanController = Object.FindFirstObjectByType<CameraPanController>();
            Assert.That(cameraPanController, Is.Not.Null, "Main CameraにCameraPanControllerがありません。");
            Assert.That(typeof(ChunkTilemapPresenter).GetField("useChunkLocalTilemaps",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(terrainPresenter), Is.True,
                "SampleSceneの保存値も既定ONへそろえる必要があります。");
            terrainPresenter.ConfigureChunkLocalPresentation(local);
            // ローカル方式のScene見本は空です。表示原点基準セルから、実際に描くチャンク内セルへ読取だけを変換します。
            // 方式にかかわらず以降の検証内容を共通にし、表示されていない見本を成功条件へ使わないようにします。
            System.Func<Tilemap, Vector3Int, TileBase> readPresentedTile = (layer, cell) =>
                GetPresentedTile(terrainPresenter, layer, cell, cameraPanController.CurrentDisplayOrigin);

            // SampleSceneへ保存した操作速度がコードの既定値とずれると、構築メニューを再実行した後だけ体感が変わります。
            // privateなInspector値を読み取り、現在のSceneと新規構築時の両方が通常12タイル/秒・高速36タイル/秒になることを保証します。
            FieldInfo movementSpeedField = typeof(CameraPanController).GetField(
                "movementSpeedInTilesPerSecond",
                BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo fastMovementMultiplierField = typeof(CameraPanController).GetField(
                "fastMovementMultiplier",
                BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo automaticRebaseThresholdField = typeof(CameraPanController).GetField(
                "automaticRebaseThresholdInTiles",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(
                movementSpeedField,
                Is.Not.Null,
                "CameraPanControllerの通常移動速度フィールドが見つかりません。");
            Assert.That(
                fastMovementMultiplierField,
                Is.Not.Null,
                "CameraPanControllerの高速移動倍率フィールドが見つかりません。");
            Assert.That(
                automaticRebaseThresholdField,
                Is.Not.Null,
                "CameraPanControllerの自動Floating Origin距離フィールドが見つかりません。");
            float movementSpeedInTilesPerSecond =
                (float)movementSpeedField.GetValue(cameraPanController);
            float fastMovementMultiplier =
                (float)fastMovementMultiplierField.GetValue(cameraPanController);
            int automaticRebaseThresholdInTiles =
                (int)automaticRebaseThresholdField.GetValue(cameraPanController);
            Assert.That(
                movementSpeedInTilesPerSecond,
                Is.EqualTo(CameraPanController.DefaultMovementSpeedInTilesPerSecond),
                "SampleSceneの通常移動速度はコード既定値の12タイル/秒と一致する必要があります。");
            Assert.That(
                fastMovementMultiplier,
                Is.EqualTo(CameraPanController.DefaultFastMovementMultiplier),
                "SampleSceneの高速移動倍率はコード既定値の3倍と一致する必要があります。");
            Assert.That(
                movementSpeedInTilesPerSecond * fastMovementMultiplier,
                Is.EqualTo(36f),
                "Shift併用時の移動速度は36タイル/秒になる必要があります。");
            Assert.That(
                automaticRebaseThresholdInTiles,
                Is.EqualTo(CameraPanController.DefaultAutomaticRebaseThresholdInTiles),
                "SampleSceneの自動Floating Origin距離はコード既定値の512タイルと一致する必要があります。");
            InitialCameraPositionController initialPositionController =
                Object.FindFirstObjectByType<InitialCameraPositionController>();
            Assert.That(
                initialPositionController,
                Is.Not.Null,
                "Main CameraにInitialCameraPositionControllerがありません。");
            WorldGridDebugOverlay debugOverlay = Object.FindFirstObjectByType<WorldGridDebugOverlay>();
            Assert.That(debugOverlay, Is.Not.Null, "Main CameraにWorldGridDebugOverlayがありません。");
            WorldGenerationDebugVisualizationOverlay developerVisualization =
                Object.FindFirstObjectByType<WorldGenerationDebugVisualizationOverlay>();
            Assert.That(
                developerVisualization,
                Is.Not.Null,
                "Main CameraにWorldGenerationDebugVisualizationOverlayがありません。");
            developerVisualization.SetLayer(WorldLayer.Underwater);
            Assert.That(
                developerVisualization.CurrentLayer,
                Is.EqualTo(WorldLayer.Underwater),
                "開発者可視化は同じX/Yを別層として診断できるよう、選択レイヤーを保持する必要があります。");
            developerVisualization.SetLayer(WorldLayer.Surface);
            FieldInfo developerVisualizationPanControllerField =
                typeof(WorldGenerationDebugVisualizationOverlay).GetField(
                    "cameraPanController",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(
                developerVisualizationPanControllerField,
                Is.Not.Null,
                "開発者可視化の表示原点参照フィールドが見つかりません。");
            Assert.That(
                developerVisualizationPanControllerField.GetValue(developerVisualization),
                Is.SameAs(cameraPanController),
                "開発者可視化はMain Cameraと同じCameraPanControllerの表示原点を参照する必要があります。");
            PixelPerfectCamera pixelPerfectCamera = Object.FindFirstObjectByType<PixelPerfectCamera>();
            Assert.That(pixelPerfectCamera, Is.Not.Null, "Main CameraにURP PixelPerfectCameraがありません。");

            // 制作開始前に決めた16 PPU・480×270・WindowboxがSceneへ保存され、表示範囲が意図せず戻らないことを保証します。
            Assert.That(pixelPerfectCamera.assetsPPU, Is.EqualTo(16));
            Assert.That(pixelPerfectCamera.refResolutionX, Is.EqualTo(480));
            Assert.That(pixelPerfectCamera.refResolutionY, Is.EqualTo(270));
            Assert.That(
                pixelPerfectCamera.gridSnapping,
                Is.EqualTo(PixelPerfectCamera.GridSnapping.PixelSnapping));
            Assert.That(
                pixelPerfectCamera.cropFrame,
                Is.EqualTo(PixelPerfectCamera.CropFrame.Windowbox));

            // F3・F4と同じ公開入口で表示状態を切り替え、両オーバーレイを独立して操作できることを確認します。
            debugOverlay.SetCoordinateHudVisible(true);
            Assert.That(debugOverlay.IsCoordinateHudVisible, Is.True);
            Assert.That(debugOverlay.IsChunkGridVisible, Is.False);
            debugOverlay.SetChunkGridVisible(true);
            Assert.That(debugOverlay.IsChunkGridVisible, Is.True);

            // 開発者用表示はキーではなくEditorメニューと同じ公開入口から切り替え、F3/F4の状態へ影響しないことを確認します。
            Assert.That(
                developerVisualization.CurrentMode,
                Is.EqualTo(WorldGenerationDebugVisualizationMode.None));
            developerVisualization.SetMode(WorldGenerationDebugVisualizationMode.Biome);
            Assert.That(
                developerVisualization.CurrentMode,
                Is.EqualTo(WorldGenerationDebugVisualizationMode.Biome));
            Assert.That(debugOverlay.IsCoordinateHudVisible, Is.True);
            Assert.That(debugOverlay.IsChunkGridVisible, Is.True);

            developerVisualization.SetMode(WorldGenerationDebugVisualizationMode.TerrainPlacement);
            Assert.That(
                developerVisualization.CurrentMode,
                Is.EqualTo(WorldGenerationDebugVisualizationMode.TerrainPlacement));

            developerVisualization.SetMode(WorldGenerationDebugVisualizationMode.AnimalPopulation);
            Assert.That(
                developerVisualization.CurrentMode,
                Is.EqualTo(WorldGenerationDebugVisualizationMode.AnimalPopulation));

            // 1描画フレームだけ動物候補・確定位置とF3/F4を有効にし、Query、座標変換、凡例、タイル・グリッド描画まで例外なく通します。
            yield return null;
            developerVisualization.SetMode(WorldGenerationDebugVisualizationMode.None);
            debugOverlay.SetCoordinateHudVisible(false);
            debugOverlay.SetChunkGridVisible(false);

            World world = null;
            EntityQuery settingsQuery = default;
            EntityQuery animalCatalogQuery = default;
            EntityQuery animalPopulationSeedQuery = default;
            EntityQuery animalRegionQuery = default;
            EntityQuery wildAnimalQuery = default;
            EntityQuery locationQuery = default;
            EntityQuery chunkQuery = default;
            bool queriesCreated = false;
            bool initialLoadCompleted = false;
            Entity originalChunkEntity = Entity.Null;
            Vector3Int originalResourceCell = default;
            InitialSpawnLocation initialSpawnLocation = default;
            ChunkCoordinate initialSpawnChunk = default;
            Vector2 initialCameraWorldPosition = default;

            for (int frame = 0; frame < MaximumWaitFrames; frame++)
            {
                world = World.DefaultGameObjectInjectionWorld;
                if (world != null && world.IsCreated && !queriesCreated)
                {
                    settingsQuery = world.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<WorldGenerationSettings>(),
                        ComponentType.ReadOnly<ResourceGenerationSettings>(),
                        ComponentType.ReadOnly<ChunkStreamingSettings>());
                    animalCatalogQuery = world.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<DefinitionCatalog>(),
                        ComponentType.ReadOnly<AnimalSpeciesCatalog>(),
                        ComponentType.ReadOnly<TerrainBlockCatalog>());
                    animalPopulationSeedQuery = world.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<AnimalPopulationSeed>());
                    animalRegionQuery = world.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<AnimalPopulationRegionState>());
                    wildAnimalQuery = world.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<WildAnimal>(),
                        ComponentType.ReadOnly<PersistentObjectId>(),
                        ComponentType.ReadOnly<AnimalSpecies>(),
                        ComponentType.ReadOnly<AnimalTilePosition>(),
                        ComponentType.ReadOnly<AnimalHerdMembership>(),
                        ComponentType.ReadOnly<AnimalAppearanceVariant>());
                    locationQuery = world.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<InitialSpawnLocation>());
                    chunkQuery = world.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<ChunkCoordinateComponent>(),
                        ComponentType.ReadOnly<ChunkGenerationStatus>(),
                        ComponentType.ReadOnly<GeneratedTile>(),
                        ComponentType.ReadOnly<GeneratedResourceTile>());
                    queriesCreated = true;
                }

                // 設定、初期スポーン記録、カメラ移動、スポーン先チャンク、地形・資源Tileがそろうまで固定秒数に依存せず待ちます。
                if (queriesCreated
                    && settingsQuery.CalculateEntityCount() == 1
                    && animalCatalogQuery.CalculateEntityCount() == 1
                    && animalPopulationSeedQuery.CalculateEntityCount() == 1
                    && animalRegionQuery.CalculateEntityCount() > 0
                    && wildAnimalQuery.CalculateEntityCount() > 0
                    && locationQuery.CalculateEntityCount() == 1)
                {
                    initialSpawnLocation = locationQuery.GetSingleton<InitialSpawnLocation>();
                    initialSpawnChunk = WorldGrid.ToChunkCoordinate(
                        initialSpawnLocation.TileX,
                        initialSpawnLocation.TileY);
                    initialCameraWorldPosition = new Vector2(
                        initialSpawnLocation.TileX + 0.5f,
                        initialSpawnLocation.TileY + 0.5f);
                    Vector3Int spawnCell = ToDisplayCell(
                        initialSpawnLocation.TileX,
                        initialSpawnLocation.TileY,
                        cameraPanController.CurrentDisplayOrigin);

                    if (initialPositionController.HasAppliedInitialPosition
                        && TryGetActiveCompleteChunk(
                            world.EntityManager,
                            chunkQuery,
                            initialSpawnChunk.X,
                            initialSpawnChunk.Y,
                            out originalChunkEntity)
                        && readPresentedTile(tilemap, spawnCell) != null
                        && TryGetDisplayedResourceCell(
                            world.EntityManager,
                            originalChunkEntity,
                            initialSpawnChunk,
                            cameraPanController.CurrentDisplayOrigin,
                            resourceTilemap,
                            resourceBehindRockWallTilemap,
                            readPresentedTile,
                            out originalResourceCell))
                    {
                        initialLoadCompleted = true;
                        break;
                    }
                }

                // 初回生成も固定Tickで投入するため、BatchModeの高速空フレームだけで待機上限を使い切らないようにします。
                System.Threading.Thread.Sleep(BatchModeWorkerYieldMilliseconds);
                yield return null;
            }

            Assert.That(
                initialLoadCompleted,
                Is.True,
                $"{MaximumWaitFrames}フレーム以内に初回生成と表示が完了しませんでした。");

            // 通常の左移動で最初に画面へ入る隣接チャンクは、中心と同じ初回生成バッチで完成させます。
            // C4-D2の重い生成中に角チャンクを先に待ち、左境界越え時に空白が見える回帰を検出します。
            Assert.That(
                TryGetCompleteChunk(
                    world.EntityManager,
                    chunkQuery,
                    initialSpawnChunk.X - 1,
                    initialSpawnChunk.Y,
                    out _),
                Is.True,
                "初期チャンクの描画完了時に左隣チャンクの5 Bufferが先読み完了していません。");

            // 実際のSubScene Baker結果を読み、Sceneの文字列だけ正しくてもRuntime Blobが欠ける回帰を検出します。
            DefinitionCatalog runtimeDefinitionCatalog =
                animalCatalogQuery.GetSingleton<DefinitionCatalog>();
            AnimalSpeciesCatalog runtimeAnimalCatalog =
                animalCatalogQuery.GetSingleton<AnimalSpeciesCatalog>();
            TerrainBlockCatalog runtimeTerrainCatalog =
                animalCatalogQuery.GetSingleton<TerrainBlockCatalog>();
            Assert.That(
                DefinitionGuid.TryParse(
                    "1728e39573b24efa996020e9493cc742",
                    out DefinitionGuid chickenGuid),
                Is.True);
            Assert.That(
                DefinitionCatalogLookup.TryResolve(
                    runtimeDefinitionCatalog.Value,
                    chickenGuid,
                    out DefinitionRuntimeId chickenRuntimeId),
                Is.True,
                "設定SubSceneの鶏GUIDが共通DefinitionCatalogへBakeされていません。");
            Assert.That(
                AnimalSpeciesCatalogLookup.TryGetDefinition(
                    runtimeAnimalCatalog.Value,
                    chickenRuntimeId,
                    out AnimalSpeciesDefinitionBlob chickenDefinition),
                Is.True,
                "共通実行時IDに対応する鶏の固有設定がBakeされていません。");
            Assert.That(chickenDefinition.AllowedBiomes, Is.EqualTo(AnimalBiomeMask.Grassland));
            Assert.That(chickenDefinition.AllowedGrounds, Is.EqualTo(AnimalGroundMask.Land));
            Assert.That(chickenDefinition.MinimumHerdSize, Is.EqualTo(3));
            Assert.That(chickenDefinition.MaximumHerdSize, Is.EqualTo(6));
            Assert.That(chickenDefinition.TargetPopulationPer4096EligibleTiles, Is.EqualTo(8));
            Assert.That(chickenDefinition.MinimumIdleTicks, Is.EqualTo(20));
            Assert.That(chickenDefinition.MaximumIdleTicks, Is.EqualTo(80));
            Assert.That(chickenDefinition.MovementStepIntervalTicks, Is.EqualTo(5));
            Assert.That(chickenDefinition.MaximumWanderDistanceInTiles, Is.EqualTo(8));
            Assert.That(chickenDefinition.AppearanceVariantCount, Is.EqualTo(3));

            // TERRAIN-01Aの組み込み5定義も同じSubSceneからBakeし、旧GroundTypeを有効セルへ変換できることを確認します。
            Assert.That(runtimeTerrainCatalog.Value.IsCreated, Is.True);
            Assert.That(runtimeTerrainCatalog.BuiltInRuntimeIds.IsValid, Is.True);
            Assert.That(
                TerrainBlockCatalogLookup.TryGetDefinition(
                    runtimeTerrainCatalog.Value,
                    runtimeTerrainCatalog.BuiltInRuntimeIds.NaturalRockWall,
                    out TerrainBlockDefinitionBlob runtimeRockWall),
                Is.True,
                "自然岩壁の組み込みBlock定義がBakeされていません。");
            Assert.That(runtimeRockWall.Kind, Is.EqualTo(DefinitionKind.WallBlock));
            Assert.That(
                runtimeRockWall.Flags & TerrainBlockDefinitionFlags.BlocksMovement,
                Is.Not.EqualTo(TerrainBlockDefinitionFlags.None));

            // SampleSceneの実際の固定Tickで独立Seed、地域状態、個体Entityまで作られ、静的定義だけで止まっていないことを確認します。
            AnimalPopulationSeed runtimePopulationSeed =
                animalPopulationSeedQuery.GetSingleton<AnimalPopulationSeed>();
            Assert.That(runtimePopulationSeed.IsValid, Is.True);
            Assert.That(animalRegionQuery.CalculateEntityCount(), Is.GreaterThan(0));
            Assert.That(wildAnimalQuery.CalculateEntityCount(), Is.GreaterThan(0));
            using (NativeArray<AnimalSpecies> runtimeSpecies =
                   wildAnimalQuery.ToComponentDataArray<AnimalSpecies>(Allocator.Temp))
            using (NativeArray<AnimalHerdMembership> runtimeHerds =
                   wildAnimalQuery.ToComponentDataArray<AnimalHerdMembership>(Allocator.Temp))
            using (NativeArray<AnimalTilePosition> runtimePositions =
                   wildAnimalQuery.ToComponentDataArray<AnimalTilePosition>(Allocator.Temp))
            using (EntityQuery resourceConfigurationQuery = world.EntityManager.CreateEntityQuery(
                   ComponentType.ReadOnly<ResourceGenerationSettings>()))
            {
                WorldGenerationSettings animalWorldSettings = settingsQuery.GetSingleton<WorldGenerationSettings>();
                ResourceGenerationSettings animalResourceSettings = resourceConfigurationQuery.GetSingleton<ResourceGenerationSettings>();
                for (int animalIndex = 0; animalIndex < runtimeSpecies.Length; animalIndex++)
                {
                    Assert.That(runtimeSpecies[animalIndex].RuntimeId, Is.EqualTo(chickenRuntimeId));
                    Assert.That(runtimeHerds[animalIndex].HerdId, Is.GreaterThan(0));

                    // 実際に使用中の設定と絶対座標から優先レイヤーの資源予約を再確認し、Scene上の生成個体との重複を検出します。
                    AnimalTilePosition animalPosition = runtimePositions[animalIndex];
                    GeneratedTile animalTerrain = DeterministicChunkGenerator.GenerateTile(
                        animalWorldSettings, animalPosition.TileX, animalPosition.TileY);
                    GeneratedResourceTile animalResource = DeterministicResourceGenerator.GenerateResource(
                        animalWorldSettings, animalResourceSettings, animalTerrain,
                        animalPosition.TileX, animalPosition.TileY);
                    Assert.That(animalResource.Type, Is.EqualTo(GeneratedResourceType.None),
                        "野生動物を地形生成時の資源予約と同じセルへ生成してはいけません。");
                }
            }

            // 候補表示もRuntimeと同じ資源予約条件を使用することを、実際にBakeされたカタログで確認します。
            MethodInfo animalCandidateMethod = typeof(WorldGenerationDebugVisualizationOverlay).GetMethod(
                "IsEligibleForAnyAnimalSpecies", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(animalCandidateMethod, Is.Not.Null);
            GeneratedTile candidateTerrain = new GeneratedTile { Biome = BiomeId.Grassland, Ground = GroundType.Land };
            Assert.That(animalCandidateMethod.Invoke(null, new object[]
                { candidateTerrain, new GeneratedResourceTile { Type = GeneratedResourceType.Tree }, runtimeAnimalCatalog.Value }), Is.False);
            Assert.That(animalCandidateMethod.Invoke(null, new object[]
                { candidateTerrain, default(GeneratedResourceTile), runtimeAnimalCatalog.Value }), Is.True);
            int2 initialDisplayOrigin =
                cameraPanController.CurrentDisplayOrigin.AbsoluteTileCoordinate;
            int originalResourceAbsoluteTileX = initialDisplayOrigin.x + originalResourceCell.x;
            int originalResourceAbsoluteTileY = initialDisplayOrigin.y + originalResourceCell.y;

            // 一体型岩壁が存在するセルを実際の表示結果から探し、同じ論理セルに土地ブロックも残る二段構成を保証します。
            // TransformとPivotだけを検査すると、Presenterが岩壁を書き忘れてもテストが通るため、Tilemapの内容まで確認します。
            bool foundDisplayedRockWall = false;
            Tilemap initialRockWallMap = GetPresentedTilemap(terrainPresenter, rockWallTilemap, initialSpawnChunk);
            Tilemap initialGroundMap = GetPresentedTilemap(terrainPresenter, tilemap, initialSpawnChunk);
            Assert.That(initialRockWallMap, Is.Not.Null);
            Assert.That(initialGroundMap, Is.Not.Null);
            foreach (Vector3Int cellPosition in initialRockWallMap.cellBounds.allPositionsWithin)
            {
                if (!initialRockWallMap.HasTile(cellPosition))
                {
                    continue;
                }

                foundDisplayedRockWall = true;
                Assert.That(
                    initialRockWallMap.GetTile(cellPosition),
                    Is.SameAs(productionRockWallTile),
                    $"岩壁セル {cellPosition} がSampleSceneの本番用岩壁Tileを参照していません。");
                Assert.That(
                    initialGroundMap.HasTile(cellPosition),
                    Is.True,
                    $"岩壁セル {cellPosition} の下に、Biome由来の一体型土地ブロックが残っていません。");
            }

            Assert.That(
                foundDisplayedRockWall,
                Is.True,
                "初回表示範囲に一体型岩壁が1枚もなく、二段構成を実表示で検証できませんでした。");
            double2 initialAbsoluteCameraPosition =
                cameraPanController.GetAbsoluteWorldPosition();
            Assert.That(
                initialAbsoluteCameraPosition.x,
                Is.EqualTo(initialCameraWorldPosition.x).Within(0.00001d),
                "表示原点を加えた絶対Camera X座標が記録済み初期スポーン中央と一致しません。");
            Assert.That(
                initialAbsoluteCameraPosition.y,
                Is.EqualTo(initialCameraWorldPosition.y).Within(0.00001d),
                "表示原点を加えた絶対Camera Y座標が記録済み初期スポーン中央と一致しません。");

            // 初期移動後も通常カメラ操作は16 PPUへ丸まり、テスト後は記録済みスポーンへ正確に戻せることを確認します。
            cameraPanController.SetAbsoluteWorldPosition(
                initialCameraWorldPosition.x + 0.1d,
                initialCameraWorldPosition.y - 0.1d);
            double2 movedAbsoluteCameraPosition =
                cameraPanController.GetAbsoluteWorldPosition();
            Assert.That(
                movedAbsoluteCameraPosition.x,
                Is.EqualTo(initialCameraWorldPosition.x + 0.125d).Within(0.00001d));
            Assert.That(
                movedAbsoluteCameraPosition.y,
                Is.EqualTo(initialCameraWorldPosition.y - 0.125d).Within(0.00001d));
            cameraPanController.SetAbsoluteWorldPosition(
                initialCameraWorldPosition.x,
                initialCameraWorldPosition.y);

            // 正式なセーブ互換運用前は生成調整のたびに番号を増やさず、Sceneの初期バージョンを1へ固定します。
            WorldGenerationSettings runtimeGenerationSettings =
                settingsQuery.GetSingleton<WorldGenerationSettings>();
            Assert.That(
                runtimeGenerationSettings.GeneratorVersion,
                Is.EqualTo(1),
                "初期開発中のGeneratorVersionは1へ固定してください。");
            Assert.That(
                runtimeGenerationSettings.SeaLevelThreshold,
                Is.EqualTo(WorldGenerationSettings.DefaultSeaLevelThreshold));
            Assert.That(
                runtimeGenerationSettings.HotTemperatureThreshold,
                Is.EqualTo(WorldGenerationSettings.DefaultHotTemperatureThreshold));
            Assert.That(
                runtimeGenerationSettings.DryMoistureThreshold,
                Is.EqualTo(WorldGenerationSettings.DefaultDryMoistureThreshold));
            Assert.That(
                runtimeGenerationSettings.WetMoistureThreshold,
                Is.EqualTo(WorldGenerationSettings.DefaultWetMoistureThreshold));

            // 生成済みタイルを使う地形気温表示を1フレーム描画し、ECS Buffer読取、Windowboxクリップ、情報HUDまで例外なく通します。
            developerVisualization.SetMode(WorldGenerationDebugVisualizationMode.TerrainTemperature);
            yield return null;
            developerVisualization.SetMode(WorldGenerationDebugVisualizationMode.None);

            DynamicBuffer<GeneratedTile> originalTiles =
                world.EntityManager.GetBuffer<GeneratedTile>(originalChunkEntity, true);
            DynamicBuffer<GeneratedResourceTile> originalResources =
                world.EntityManager.GetBuffer<GeneratedResourceTile>(originalChunkEntity, true);
            GeneratedTile originalFirstTile = originalTiles[0];
            GeneratedTile originalCenterTile = originalTiles[WorldGridConstants.TilesPerChunk / 2];
            GeneratedTile originalLastTile = originalTiles[WorldGridConstants.TilesPerChunk - 1];
            GeneratedResourceTile originalFirstResource = originalResources[0];
            GeneratedResourceTile originalCenterResource =
                originalResources[WorldGridConstants.TilesPerChunk / 2];
            GeneratedResourceTile originalLastResource =
                originalResources[WorldGridConstants.TilesPerChunk - 1];

            // 初期スポーンから8チャンク東へ即時移動し、開始チャンクがRetention範囲外になってデータと表示から消えるのを待ちます。
            Vector2 farCameraWorldPosition = initialCameraWorldPosition
                + new Vector2(FarCameraOffsetInTiles, 0f);
            ChunkCoordinate farCameraChunk = WorldGrid.ToChunkCoordinate(
                Mathf.FloorToInt(farCameraWorldPosition.x),
                Mathf.FloorToInt(farCameraWorldPosition.y));
            cameraPanController.SetAbsoluteWorldPosition(
                farCameraWorldPosition.x,
                farCameraWorldPosition.y);
            bool farLoadCompleted = false;
            for (int frame = 0; frame < MaximumWaitFrames; frame++)
            {
                bool originalChunkWasUnloaded = !HasChunkAt(
                    chunkQuery,
                    initialSpawnChunk.X,
                    initialSpawnChunk.Y);
                bool farChunkIsActive = TryGetActiveCompleteChunk(
                    world.EntityManager,
                    chunkQuery,
                    farCameraChunk.X,
                    farCameraChunk.Y,
                    out _);
                Vector3Int farCameraDisplayCell = ToDisplayCell(
                    Mathf.FloorToInt(farCameraWorldPosition.x),
                    Mathf.FloorToInt(farCameraWorldPosition.y),
                    cameraPanController.CurrentDisplayOrigin);
                bool farCameraCellIsDisplayed = readPresentedTile(tilemap, farCameraDisplayCell) != null
                    || readPresentedTile(liquidBaseTilemap, farCameraDisplayCell) != null;
                Vector3Int originalResourceDisplayCell = ToDisplayCell(
                    originalResourceAbsoluteTileX,
                    originalResourceAbsoluteTileY,
                    cameraPanController.CurrentDisplayOrigin);
                bool originalResourceWasCleared =
                    readPresentedTile(resourceTilemap, originalResourceDisplayCell) == null
                    && readPresentedTile(resourceBehindRockWallTilemap, originalResourceDisplayCell) == null;
                bool displayOriginMoved = !cameraPanController.CurrentDisplayOrigin
                    .AbsoluteTileCoordinate.Equals(initialDisplayOrigin);

                if (originalChunkWasUnloaded
                    && displayOriginMoved
                    && farChunkIsActive
                    && farCameraCellIsDisplayed
                    && originalResourceWasCleared)
                {
                    farLoadCompleted = true;
                    break;
                }

                // 遠方移動後の生成Jobと固定Tick公開へ処理時間を渡し、EditorとBatchModeで同じ待機契約にします。
                System.Threading.Thread.Sleep(BatchModeWorkerYieldMilliseconds);
                yield return null;
            }

            Assert.That(
                farLoadCompleted,
                Is.True,
                $"{MaximumWaitFrames}フレーム以内に遠方チャンクへの切替と原点のアンロードが完了しませんでした。");

            // 申し送り事項の正座標側を確認し、可視範囲、中央情報、色面投影が同じ絶対セルを参照することを保証します。
            AssertDeveloperVisualizationUsesAbsoluteCoordinates(
                developerVisualization,
                cameraPanController,
                tilemap,
                farCameraChunk,
                farCameraWorldPosition,
                "正座標の遠方");
            developerVisualization.SetMode(WorldGenerationDebugVisualizationMode.Biome);
            yield return null;
            developerVisualization.SetMode(WorldGenerationDebugVisualizationMode.None);

            // 負座標ではC#の0方向丸めを使うと別チャンクへずれるため、初期位置から十分離れた負の絶対座標でも同じ境界を確認します。
            Vector2 negativeFarCameraWorldPosition = new Vector2(
                -Mathf.Abs(initialCameraWorldPosition.x) - FarCameraOffsetInTiles,
                -Mathf.Abs(initialCameraWorldPosition.y) - FarCameraOffsetInTiles);
            ChunkCoordinate negativeFarCameraChunk = WorldGrid.ToChunkCoordinate(
                Mathf.FloorToInt(negativeFarCameraWorldPosition.x),
                Mathf.FloorToInt(negativeFarCameraWorldPosition.y));
            cameraPanController.SetAbsoluteWorldPosition(
                negativeFarCameraWorldPosition.x,
                negativeFarCameraWorldPosition.y);
            bool negativeFarLoadCompleted = false;
            for (int frame = 0; frame < MaximumWaitFrames; frame++)
            {
                bool negativeFarChunkIsActive = TryGetActiveCompleteChunk(
                    world.EntityManager,
                    chunkQuery,
                    negativeFarCameraChunk.X,
                    negativeFarCameraChunk.Y,
                    out _);
                Vector3Int negativeFarDisplayCell = ToDisplayCell(
                    Mathf.FloorToInt(negativeFarCameraWorldPosition.x),
                    Mathf.FloorToInt(negativeFarCameraWorldPosition.y),
                    cameraPanController.CurrentDisplayOrigin);
                bool negativeFarCellIsDisplayed = readPresentedTile(tilemap, negativeFarDisplayCell) != null
                    || readPresentedTile(liquidBaseTilemap, negativeFarDisplayCell) != null;
                if (negativeFarChunkIsActive && negativeFarCellIsDisplayed)
                {
                    negativeFarLoadCompleted = true;
                    break;
                }

                // 負座標への即時移動後も、BatchModeの高速空フレームだけで待機上限を使い切らないようにします。
                System.Threading.Thread.Sleep(BatchModeWorkerYieldMilliseconds);
                yield return null;
            }

            EntityQuery negativeFinalObserverQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ChunkStreamingObserver>());
            bool hasNegativeFinalObserver = negativeFinalObserverQuery.CalculateEntityCount() == 1;
            ChunkStreamingObserver negativeFinalObserver = hasNegativeFinalObserver
                ? negativeFinalObserverQuery.GetSingleton<ChunkStreamingObserver>()
                : default;
            negativeFinalObserverQuery.Dispose();
            int negativeTargetChunkCount = CountChunksAt(
                chunkQuery,
                negativeFarCameraChunk.X,
                negativeFarCameraChunk.Y);
            string negativeTargetChunkState = DescribeSurfaceChunkState(
                world.EntityManager,
                chunkQuery,
                negativeFarCameraChunk.X,
                negativeFarCameraChunk.Y);
            Assert.That(
                negativeFarLoadCompleted,
                Is.True,
                $"{MaximumWaitFrames}フレーム以内に負座標の遠方チャンクが生成・表示されませんでした。"
                + $" Observer={hasNegativeFinalObserver}/{negativeFinalObserver.Layer}:"
                + $"({negativeFinalObserver.MinimumVisibleChunkX}..{negativeFinalObserver.MaximumVisibleChunkX}, "
                + $"{negativeFinalObserver.MinimumVisibleChunkY}..{negativeFinalObserver.MaximumVisibleChunkY}), "
                + $"Target={negativeFarCameraChunk}, TargetEntityCount={negativeTargetChunkCount}, "
                + $"TargetState={negativeTargetChunkState}");
            AssertDeveloperVisualizationUsesAbsoluteCoordinates(
                developerVisualization,
                cameraPanController,
                tilemap,
                negativeFarCameraChunk,
                negativeFarCameraWorldPosition,
                "負座標の遠方");
            developerVisualization.SetMode(WorldGenerationDebugVisualizationMode.BaseMoisture);
            yield return null;
            developerVisualization.SetMode(WorldGenerationDebugVisualizationMode.None);

            // 記録済みスポーンへ戻り、破棄済みEntityに依存せず同じシードから同一タイルデータを再生成できることを確認します。
            cameraPanController.SetAbsoluteWorldPosition(
                initialCameraWorldPosition.x,
                initialCameraWorldPosition.y);
            bool returnLoadCompleted = false;
            Entity regeneratedChunkEntity = Entity.Null;
            for (int frame = 0; frame < MaximumWaitFrames; frame++)
            {
                if (TryGetActiveCompleteChunk(
                        world.EntityManager,
                        chunkQuery,
                        initialSpawnChunk.X,
                        initialSpawnChunk.Y,
                        out regeneratedChunkEntity)
                    && readPresentedTile(tilemap,
                        ToDisplayCell(
                            initialSpawnLocation.TileX,
                            initialSpawnLocation.TileY,
                            cameraPanController.CurrentDisplayOrigin)) != null)
                {
                    returnLoadCompleted = true;
                    break;
                }

                // 再生成Jobと固定Tick公開へ処理時間を渡し、EditorとBatchModeで同じ待機契約にします。
                System.Threading.Thread.Sleep(BatchModeWorkerYieldMilliseconds);
                yield return null;
            }

            Assert.That(
                returnLoadCompleted,
                Is.True,
                $"{MaximumWaitFrames}フレーム以内に原点チャンクの再生成と再表示が完了しませんでした。");
            Assert.That(
                regeneratedChunkEntity,
                Is.Not.EqualTo(originalChunkEntity),
                "初期スポーンチャンクは遠方移動時に破棄され、別Entityとして再生成される必要があります。");

            DynamicBuffer<GeneratedTile> regeneratedTiles =
                world.EntityManager.GetBuffer<GeneratedTile>(regeneratedChunkEntity, true);
            DynamicBuffer<GeneratedResourceTile> regeneratedResources =
                world.EntityManager.GetBuffer<GeneratedResourceTile>(regeneratedChunkEntity, true);
            AssertTileEquals(originalFirstTile, regeneratedTiles[0], 0);
            AssertTileEquals(
                originalCenterTile,
                regeneratedTiles[WorldGridConstants.TilesPerChunk / 2],
                WorldGridConstants.TilesPerChunk / 2);
            AssertTileEquals(
                originalLastTile,
                regeneratedTiles[WorldGridConstants.TilesPerChunk - 1],
                WorldGridConstants.TilesPerChunk - 1);
            AssertResourceEquals(originalFirstResource, regeneratedResources[0], 0);
            AssertResourceEquals(
                originalCenterResource,
                regeneratedResources[WorldGridConstants.TilesPerChunk / 2],
                WorldGridConstants.TilesPerChunk / 2);
            AssertResourceEquals(
                originalLastResource,
                regeneratedResources[WorldGridConstants.TilesPerChunk - 1],
                WorldGridConstants.TilesPerChunk - 1);
            Assert.That(
                readPresentedTile(resourceTilemap, originalResourceCell) != null
                    || readPresentedTile(resourceBehindRockWallTilemap, originalResourceCell) != null,
                Is.True,
                "原点へ戻った後、決定論的に再生成された資源アイコンが再表示される必要があります。");

            // 初期スポーン周辺は安全な陸地として選ばれるため、水が偶然あることへ依存せず、生成規則から液体セルを探して表示先へ移動します。
            bool foundLiquidCell = TryFindLiquidCell(
                runtimeGenerationSettings,
                initialSpawnLocation.TileX,
                initialSpawnLocation.TileY,
                out Vector3Int liquidCell);
            Assert.That(
                foundLiquidCell,
                Is.True,
                "初期スポーンから2048タイル以内に、液面レイヤーを実表示検証できる水・マグマが見つかりませんでした。");

            cameraPanController.SetAbsoluteWorldPosition(
                liquidCell.x + 0.5d,
                liquidCell.y + 0.5d);
            bool liquidDisplayCompleted = false;
            Vector3Int liquidDisplayCell = default;
            for (int frame = 0; frame < MaximumWaitFrames; frame++)
            {
                liquidDisplayCell = ToDisplayCell(
                    liquidCell.x,
                    liquidCell.y,
                    cameraPanController.CurrentDisplayOrigin);
                // 静止面とアニメーションが同じ論理セルへそろって初めて、実際の液面表示が完成したと判断します。
                if (readPresentedTile(liquidBaseTilemap, liquidDisplayCell) != null
                    && readPresentedTile(liquidAnimationTilemap, liquidDisplayCell) != null)
                {
                    liquidDisplayCompleted = true;
                    break;
                }

                // 遠方の液体チャンクも固定Tickで生成を投入するため、BatchModeではWorkerとTickへ最低限の実時間を渡します。
                System.Threading.Thread.Sleep(BatchModeWorkerYieldMilliseconds);
                yield return null;
            }

            Assert.That(
                liquidDisplayCompleted,
                Is.True,
                $"液体セル {liquidCell} の静止面とアニメーションが{MaximumWaitFrames}フレーム以内に表示されませんでした。");
            Assert.That(
                readPresentedTile(tilemap, liquidDisplayCell) != null,
                Is.False,
                $"液体セル {liquidCell} がGround Tilemapにも重複し、元の高さの液面が残っています。");

            if (queriesCreated && world != null && world.IsCreated)
            {
                settingsQuery.Dispose();
                animalCatalogQuery.Dispose();
                animalPopulationSeedQuery.Dispose();
                animalRegionQuery.Dispose();
                wildAnimalQuery.Dispose();
                locationQuery.Dispose();
                chunkQuery.Dispose();
            }
        }

        /// <summary>
        /// 指定絶対チャンク座標を持つEntity数を数え、ストリーミング往復で同じ座標が二重生成されていないか確認できるようにします。
        /// </summary>
        /// <param name="chunkQuery">座標、状態、地形・資源Bufferを持つチャンクQueryです。</param>
        /// <param name="chunkX">数える水平論理チャンク座標です。</param>
        /// <param name="chunkY">数える垂直論理チャンク座標です。</param>
        /// <returns>指定した絶対チャンク座標と一致するEntity数です。</returns>
        private static int CountChunksAt(EntityQuery chunkQuery, int chunkX, int chunkY)
        {
            using NativeArray<ChunkCoordinateComponent> coordinates =
                chunkQuery.ToComponentDataArray<ChunkCoordinateComponent>(Allocator.Temp);
            int matchingCount = 0;
            for (int index = 0; index < coordinates.Length; index++)
            {
                if (coordinates[index].Layer == WorldLayer.Surface
                    && coordinates[index].X == chunkX
                    && coordinates[index].Y == chunkY)
                {
                    matchingCount++;
                }
            }

            return matchingCount;
        }

        /// <summary>
        /// 初回訪問ではチャンクEntityと代表3セルを保存し、再訪では別Entityへ再生成されても地形・資源が一致することを確認します。
        /// 全4096セルの決定論はEditModeで保証済みのため、この継続PlayMode回帰では先頭・中央・末尾を比較して実行時間を抑えます。
        /// </summary>
        /// <param name="entityManager">生成済みBufferを所有するDefault WorldのEntityManagerです。</param>
        /// <param name="chunkEntity">今回の訪問でActiveかつ生成完了したチャンクEntityです。</param>
        /// <param name="coordinate">比較履歴を識別する絶対論理チャンク座標です。</param>
        /// <param name="lastEntityByCoordinate">座標ごとに直前の訪問で使われたEntityを保持します。</param>
        /// <param name="terrainSamplesByCoordinate">座標ごとの代表地形3セルを保持します。</param>
        /// <param name="resourceSamplesByCoordinate">座標ごとの代表資源3セルを保持します。</param>
        /// <param name="revisitedChunkCount">別Entityで同じ座標を再確認できた回数を加算します。</param>
        private static void CaptureOrAssertChunkSamples(
            EntityManager entityManager,
            Entity chunkEntity,
            ChunkCoordinate coordinate,
            Dictionary<ChunkCoordinate, Entity> lastEntityByCoordinate,
            Dictionary<ChunkCoordinate, GeneratedTile[]> terrainSamplesByCoordinate,
            Dictionary<ChunkCoordinate, GeneratedResourceTile[]> resourceSamplesByCoordinate,
            ref int revisitedChunkCount)
        {
            DynamicBuffer<GeneratedTile> terrain = entityManager.GetBuffer<GeneratedTile>(chunkEntity, true);
            DynamicBuffer<GeneratedResourceTile> resources =
                entityManager.GetBuffer<GeneratedResourceTile>(chunkEntity, true);
            int[] sampleIndices =
            {
                0,
                WorldGridConstants.TilesPerChunk / 2,
                WorldGridConstants.TilesPerChunk - 1
            };

            if (!lastEntityByCoordinate.TryGetValue(coordinate, out Entity previousEntity))
            {
                var terrainSamples = new GeneratedTile[sampleIndices.Length];
                var resourceSamples = new GeneratedResourceTile[sampleIndices.Length];
                for (int sampleIndex = 0; sampleIndex < sampleIndices.Length; sampleIndex++)
                {
                    int tileIndex = sampleIndices[sampleIndex];
                    terrainSamples[sampleIndex] = terrain[tileIndex];
                    resourceSamples[sampleIndex] = resources[tileIndex];
                }

                lastEntityByCoordinate.Add(coordinate, chunkEntity);
                terrainSamplesByCoordinate.Add(coordinate, terrainSamples);
                resourceSamplesByCoordinate.Add(coordinate, resourceSamples);
                return;
            }

            Assert.That(
                chunkEntity,
                Is.Not.EqualTo(previousEntity),
                $"chunk={coordinate}: 保持範囲外へ移動した後も以前のEntityが残っています。");
            GeneratedTile[] expectedTerrain = terrainSamplesByCoordinate[coordinate];
            GeneratedResourceTile[] expectedResources = resourceSamplesByCoordinate[coordinate];
            for (int sampleIndex = 0; sampleIndex < sampleIndices.Length; sampleIndex++)
            {
                int tileIndex = sampleIndices[sampleIndex];
                AssertTileEquals(expectedTerrain[sampleIndex], terrain[tileIndex], tileIndex);
                AssertResourceEquals(expectedResources[sampleIndex], resources[tileIndex], tileIndex);
            }

            lastEntityByCoordinate[coordinate] = chunkEntity;
            revisitedChunkCount++;
        }

        /// <summary>
        /// ProfilerRecorderが保持した各フレームの値を走査し、比較記録へ使う最大値を取得します。
        /// 合格閾値は未決定なので、このメソッドは値を加工せずナノ秒単位の最大値だけを返します。
        /// </summary>
        /// <param name="recorder">継続移動中に有効化した時間MarkerのRecorderです。</param>
        /// <returns>記録された最大値です。時間Markerでは単位はナノ秒です。</returns>
        private static long GetMaximumProfilerValue(ProfilerRecorder recorder)
        {
            var samples = new List<ProfilerRecorderSample>(recorder.Count);
            recorder.CopyTo(samples);
            long maximumValue = 0L;
            for (int index = 0; index < samples.Count; index++)
            {
                if (samples[index].Value > maximumValue)
                {
                    maximumValue = samples[index].Value;
                }
            }

            return maximumValue;
        }

        /// <summary>
        /// 指定座標のチャンクがQuery内に存在するかを、生成段階に関係なく確認します。
        /// </summary>
        /// <param name="chunkQuery">座標、状態、地形・資源Bufferを持つチャンクQueryです。</param>
        /// <param name="chunkX">検索する水平論理チャンク座標です。</param>
        /// <param name="chunkY">検索する垂直論理チャンク座標です。</param>
        /// <returns>指定座標を持つEntityが1つ以上あればtrueです。</returns>
        private static bool HasChunkAt(EntityQuery chunkQuery, int chunkX, int chunkY)
        {
            using NativeArray<ChunkCoordinateComponent> coordinates =
                chunkQuery.ToComponentDataArray<ChunkCoordinateComponent>(Allocator.Temp);

            for (int index = 0; index < coordinates.Length; index++)
            {
                if (coordinates[index].Layer == WorldLayer.Surface
                    && coordinates[index].X == chunkX
                    && coordinates[index].Y == chunkY)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 初期スポーンを中心とする一定範囲を粗く走査し、実表示テストに使う水またはマグマの論理セルを決定論的に探します。
        /// </summary>
        /// <param name="settings">実行中Worldが使用している地形生成設定です。</param>
        /// <param name="centerTileX">探索中心となる水平ワールドタイル座標です。</param>
        /// <param name="centerTileY">探索中心となる垂直ワールドタイル座標です。</param>
        /// <param name="liquidCell">成功時に、見つかった液体セルのワールド座標を返します。</param>
        /// <returns>探索範囲内に水またはマグマが見つかった場合はtrueです。</returns>
        private static bool TryFindLiquidCell(
            in WorldGenerationSettings settings,
            int centerTileX,
            int centerTileY,
            out Vector3Int liquidCell)
        {
            // 無限ワールド全体を走査せず、32タイル刻みで最大2048タイル四方を調べれば、テスト時間を抑えつつ低地を確実に探せます。
            const int searchRadiusInTiles = 2048;
            const int searchStrideInTiles = 32;
            for (int offsetY = -searchRadiusInTiles;
                 offsetY <= searchRadiusInTiles;
                 offsetY += searchStrideInTiles)
            {
                for (int offsetX = -searchRadiusInTiles;
                     offsetX <= searchRadiusInTiles;
                     offsetX += searchStrideInTiles)
                {
                    int tileX = centerTileX + offsetX;
                    int tileY = centerTileY + offsetY;
                    GeneratedTile generatedTile =
                        DeterministicChunkGenerator.GenerateTile(settings, tileX, tileY);
                    if (generatedTile.Ground != GroundType.Water
                        && generatedTile.Ground != GroundType.Magma)
                    {
                        continue;
                    }

                    liquidCell = new Vector3Int(tileX, tileY, 0);
                    return true;
                }
            }

            liquidCell = default;
            return false;
        }

        /// <summary>
        /// 指定座標のチャンクを調べ、Active状態かつ地形・資源を各4096件保持するEntityを取得します。
        /// </summary>
        /// <param name="entityManager">Default WorldのEntityManagerです。</param>
        /// <param name="chunkQuery">座標、状態、地形・資源Bufferを持つチャンクQueryです。</param>
        /// <param name="chunkX">検索する水平論理チャンク座標です。</param>
        /// <param name="chunkY">検索する垂直論理チャンク座標です。</param>
        /// <param name="chunkEntity">成功時に、完全生成されたチャンクEntityを返します。</param>
        /// <returns>完全生成され表示対象になった指定座標のチャンクが見つかればtrueです。</returns>
        private static bool TryGetActiveCompleteChunk(
            EntityManager entityManager,
            EntityQuery chunkQuery,
            int chunkX,
            int chunkY,
            out Entity chunkEntity)
        {
            using NativeArray<Entity> entities = chunkQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<ChunkCoordinateComponent> coordinates =
                chunkQuery.ToComponentDataArray<ChunkCoordinateComponent>(Allocator.Temp);
            using NativeArray<ChunkGenerationStatus> statuses =
                chunkQuery.ToComponentDataArray<ChunkGenerationStatus>(Allocator.Temp);

            for (int index = 0; index < entities.Length; index++)
            {
                if (coordinates[index].Layer != WorldLayer.Surface
                    || coordinates[index].X != chunkX
                    || coordinates[index].Y != chunkY
                    || statuses[index].Phase != ChunkGenerationPhase.Active)
                {
                    continue;
                }

                DynamicBuffer<GeneratedTile> tiles = entityManager.GetBuffer<GeneratedTile>(entities[index], true);
                DynamicBuffer<GeneratedResourceTile> resources =
                    entityManager.GetBuffer<GeneratedResourceTile>(entities[index], true);
                if (tiles.Length == WorldGridConstants.TilesPerChunk
                    && resources.Length == WorldGridConstants.TilesPerChunk)
                {
                    chunkEntity = entities[index];
                    return true;
                }
            }

            chunkEntity = Entity.Null;
            return false;
        }

        /// <summary>
        /// 指定座標でReadyToApply、Active、Inactiveのいずれかとなり、5種類の固定生成Bufferがそろったチャンクを取得します。
        /// 画面外の先読みはTilemap反映前のReadyToApplyまたは反映後のInactiveなので、Active限定の検索と分けます。
        /// </summary>
        /// <param name="entityManager">5種類のBufferを所有するDefault WorldのEntityManagerです。</param>
        /// <param name="chunkQuery">座標、状態、地形・資源Bufferを持つチャンクQueryです。</param>
        /// <param name="chunkX">検索する水平論理チャンク座標です。</param>
        /// <param name="chunkY">検索する垂直論理チャンク座標です。</param>
        /// <param name="chunkEntity">成功時に、完全生成済みのチャンクEntityを返します。</param>
        /// <returns>Simulationの5 Buffer公開と表示対象判定が完了していればtrueです。</returns>
        private static bool TryGetCompleteChunk(
            EntityManager entityManager,
            EntityQuery chunkQuery,
            int chunkX,
            int chunkY,
            out Entity chunkEntity)
        {
            using NativeArray<Entity> entities = chunkQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<ChunkCoordinateComponent> coordinates =
                chunkQuery.ToComponentDataArray<ChunkCoordinateComponent>(Allocator.Temp);
            using NativeArray<ChunkGenerationStatus> statuses =
                chunkQuery.ToComponentDataArray<ChunkGenerationStatus>(Allocator.Temp);

            for (int index = 0; index < entities.Length; index++)
            {
                ChunkGenerationPhase phase = statuses[index].Phase;
                if (coordinates[index].Layer != WorldLayer.Surface
                    || coordinates[index].X != chunkX
                    || coordinates[index].Y != chunkY
                    || (phase != ChunkGenerationPhase.ReadyToApply
                        && phase != ChunkGenerationPhase.Active
                        && phase != ChunkGenerationPhase.Inactive))
                {
                    continue;
                }

                Entity entity = entities[index];
                // 5 Bufferのいずれかが欠ける部分公開も、先読み完了とはみなしません。
                if (entityManager.GetBuffer<GeneratedTile>(entity, true).Length
                        == WorldGridConstants.TilesPerChunk
                    && entityManager.GetBuffer<GeneratedResourceTile>(entity, true).Length
                        == WorldGridConstants.TilesPerChunk
                    && entityManager.GetBuffer<GeneratedHydrologyCell>(entity, true).Length
                        == DeterministicHydrologyGenerator.CellsPerChunk
                    && entityManager.GetBuffer<GeneratedSurfaceWaterTile>(entity, true).Length
                        == WorldGridConstants.TilesPerChunk
                    && entityManager.GetBuffer<GeneratedHydrologyTile>(entity, true).Length
                        == WorldGridConstants.TilesPerChunk)
                {
                    chunkEntity = entity;
                    return true;
                }
            }

            chunkEntity = Entity.Null;
            return false;
        }

        /// <summary>
        /// 失敗メッセージ用に、指定した地上チャンクの生成段階とBuffer件数を文字列へまとめます。
        /// 通常の成功経路ではAssertion本文に表示されず、Simulation状態は変更しません。
        /// </summary>
        /// <param name="entityManager">チャンクEntityとBufferを所有するDefault Worldです。</param>
        /// <param name="chunkQuery">座標、状態、地形・資源Bufferを持つチャンクQueryです。</param>
        /// <param name="chunkX">調べる水平論理チャンク座標です。</param>
        /// <param name="chunkY">調べる垂直論理チャンク座標です。</param>
        /// <returns>該当Entityの生成段階とBuffer長、またはmissingです。</returns>
        private static string DescribeSurfaceChunkState(
            EntityManager entityManager,
            EntityQuery chunkQuery,
            int chunkX,
            int chunkY)
        {
            using NativeArray<Entity> entities = chunkQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<ChunkCoordinateComponent> coordinates =
                chunkQuery.ToComponentDataArray<ChunkCoordinateComponent>(Allocator.Temp);
            using NativeArray<ChunkGenerationStatus> statuses =
                chunkQuery.ToComponentDataArray<ChunkGenerationStatus>(Allocator.Temp);
            int queuedCount = 0;
            int generatingCount = 0;
            int readyOrActiveCount = 0;
            string targetState = "missing";
            for (int index = 0; index < entities.Length; index++)
            {
                if (statuses[index].Phase == ChunkGenerationPhase.Queued)
                {
                    queuedCount++;
                }
                else if (statuses[index].Phase == ChunkGenerationPhase.GeneratingData)
                {
                    generatingCount++;
                }
                else if (statuses[index].Phase == ChunkGenerationPhase.ReadyToApply
                    || statuses[index].Phase == ChunkGenerationPhase.Active)
                {
                    readyOrActiveCount++;
                }

                if (coordinates[index].Layer != WorldLayer.Surface
                    || coordinates[index].X != chunkX
                    || coordinates[index].Y != chunkY)
                {
                    continue;
                }

                int terrainCount = entityManager.GetBuffer<GeneratedTile>(entities[index], true).Length;
                int resourceCount =
                    entityManager.GetBuffer<GeneratedResourceTile>(entities[index], true).Length;
                targetState =
                    $"{statuses[index].Phase}/terrain={terrainCount}/resources={resourceCount}";
            }

            return $"{targetState}; totals=queued:{queuedCount},generating:{generatingCount},readyOrActive:{readyOrActiveCount}";
        }

        /// <summary>
        /// 指定チャンクの資源Bufferから最初の配置済みセルを探し、前面または岩壁背面のResource Tilemapへ表示されたか確認します。
        /// </summary>
        /// <param name="entityManager">Default WorldのEntityManagerです。</param>
        /// <param name="chunkEntity">検査対象チャンクを表す完全生成済みEntityです。</param>
        /// <param name="chunkCoordinate">ローカルBuffer Indexをワールドセルへ戻す論理チャンク座標です。</param>
        /// <param name="displayOrigin">絶対ワールドセルをTilemapの相対セルへ変換する現在の表示原点です。</param>
        /// <param name="resourceTilemap">岩壁に隠れない資源を重ね描画するTilemapです。</param>
        /// <param name="resourceBehindRockWallTilemap">直前方の岩壁に隠す資源を描画するTilemapです。</param>
        /// <param name="readPresentedTile">見本レイヤーと表示原点基準セルから実際の表示Tileを読む、テスト用の変換です。</param>
        /// <param name="resourceCell">成功時に、表示済み資源の表示原点基準セル座標を返します。</param>
        /// <returns>配置済み資源が1件以上あり、そのTileが表示済みならtrueです。</returns>
        private static bool TryGetDisplayedResourceCell(
            EntityManager entityManager,
            Entity chunkEntity,
            ChunkCoordinate chunkCoordinate,
            DisplayOrigin displayOrigin,
            Tilemap resourceTilemap,
            Tilemap resourceBehindRockWallTilemap,
            System.Func<Tilemap, Vector3Int, TileBase> readPresentedTile,
            out Vector3Int resourceCell)
        {
            DynamicBuffer<GeneratedResourceTile> resources =
                entityManager.GetBuffer<GeneratedResourceTile>(chunkEntity, true);

            // Bufferは行優先なので、Indexを64で割った商と余りから原点チャンク内座標へ戻します。
            for (int tileIndex = 0; tileIndex < resources.Length; tileIndex++)
            {
                if (resources[tileIndex].Type == GeneratedResourceType.None)
                {
                    continue;
                }

                int localX = tileIndex % WorldGridConstants.LogicalChunkSize;
                int localY = tileIndex / WorldGridConstants.LogicalChunkSize;
                resourceCell = ToDisplayCell(
                    WorldGrid.ToWorldTileX(chunkCoordinate.X, localX),
                    WorldGrid.ToWorldTileY(chunkCoordinate.Y, localY),
                    displayOrigin);
                return readPresentedTile(resourceTilemap, resourceCell) != null
                    || readPresentedTile(resourceBehindRockWallTilemap, resourceCell) != null;
            }

            resourceCell = default;
            return false;
        }

        /// <summary>
        /// 表示方式に対応するTilemapを、作成せずに取得します。非公開キャッシュの確認はテスト内だけに閉じます。
        /// </summary>
        /// <param name="presenter">Sceneの表示キャッシュ所有者です。</param>
        /// <param name="template">Sceneの対応レイヤー見本です。</param>
        /// <param name="coordinate">確認する絶対チャンク座標です。</param>
        /// <returns>描画先のTilemap、まだ未割当ならnullです。共有方式では見本自身です。</returns>
        private static Tilemap GetPresentedTilemap(ChunkTilemapPresenter presenter, Tilemap template, ChunkCoordinate coordinate)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            return (Tilemap)typeof(ChunkTilemapPresenter).GetMethod("GetTilemapForChunk", flags)
                .Invoke(presenter, new object[] { coordinate, template, false });
        }

        /// <summary>表示原点基準セルを実際の描画先へ変換し、Tileを読んで返します。表示状態は変更しません。</summary>
        /// <param name="presenter">Sceneの表示キャッシュ所有者です。</param>
        /// <param name="template">対応するScene見本レイヤーです。</param>
        /// <param name="relativeCell">Cameraと共通の表示原点基準セルです。</param>
        /// <param name="origin">現在の表示原点です。</param>
        /// <returns>実際の表示Tile、未割当または空セルならnullです。</returns>
        private static TileBase GetPresentedTile(ChunkTilemapPresenter presenter, Tilemap template, Vector3Int relativeCell, DisplayOrigin origin)
        {
            int absoluteX = relativeCell.x + origin.AbsoluteTileCoordinate.x;
            int absoluteY = relativeCell.y + origin.AbsoluteTileCoordinate.y;
            ChunkCoordinate coordinate = WorldGrid.ToChunkCoordinate(absoluteX, absoluteY);
            Tilemap map = GetPresentedTilemap(presenter, template, coordinate);
            if (map == null) return null;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            bool local = (bool)typeof(ChunkTilemapPresenter).GetField("chunkLocalTilemapsAreActive", flags).GetValue(presenter);
            // 両方式とも論理セルは同じです。ローカル方式だけ、負座標にも対応する床除算の余りで0〜63へ戻します。
            ChunkLocalTileCoordinate cell = WorldGrid.ToLocalTileCoordinate(absoluteX, absoluteY);
            return map.GetTile(local ? new Vector3Int(cell.X, cell.Y, 0) : relativeCell);
        }

        /// <summary>
        /// Simulationの絶対タイル座標を、現在の表示原点を差し引いたTilemapセルへ変換します。
        /// </summary>
        /// <param name="absoluteTileX">Simulationが保持する絶対水平タイル座標です。</param>
        /// <param name="absoluteTileY">Simulationが保持する絶対垂直タイル座標です。</param>
        /// <param name="displayOrigin">Tilemapローカルセル(0, 0)へ対応する絶対タイル座標です。</param>
        /// <returns>Cameraと見本Gridが共有する相対セル座標です。チャンク別方式の読取時はGetPresentedTileで0〜63へ変換します。</returns>
        private static Vector3Int ToDisplayCell(
            int absoluteTileX,
            int absoluteTileY,
            DisplayOrigin displayOrigin)
        {
            RelativeCellCoordinate relativeCell =
                WorldCoordinateConversion.ToRelativeCellCoordinate(
                    new int2(absoluteTileX, absoluteTileY),
                    displayOrigin);
            Assert.That(relativeCell.X, Is.InRange((long)int.MinValue, int.MaxValue));
            Assert.That(relativeCell.Y, Is.InRange((long)int.MinValue, int.MaxValue));
            return new Vector3Int((int)relativeCell.X, (int)relativeCell.Y, 0);
        }

        /// <summary>
        /// 遠方の表示原点で、開発者可視化の可視範囲、中央情報、色面投影が同じ絶対タイルを参照することを保証します。
        /// 正負の各移動先がActiveになった後に呼び、ECSチャンクとGameビューセルの両方へ接続されていることを確認します。
        /// </summary>
        /// <param name="developerVisualization">絶対・相対座標の境界を検査する開発者用可視化です。</param>
        /// <param name="cameraPanController">現在の表示原点とCamera Transformを所有するControllerです。</param>
        /// <param name="terrainTilemap">通常地形と同じGridセルへの投影結果を比較するTilemapです。</param>
        /// <param name="activeChunk">Camera中央を含み、生成完了を確認済みの絶対論理チャンクです。</param>
        /// <param name="absoluteCameraPosition">移動時に指定したSimulation上の絶対Camera座標です。</param>
        /// <param name="caseName">失敗メッセージで正座標・負座標を区別する表示名です。</param>
        private static void AssertDeveloperVisualizationUsesAbsoluteCoordinates(
            WorldGenerationDebugVisualizationOverlay developerVisualization,
            CameraPanController cameraPanController,
            Tilemap terrainTilemap,
            ChunkCoordinate activeChunk,
            Vector2 absoluteCameraPosition,
            string caseName)
        {
            bool hasVisibleBounds = developerVisualization.TryGetVisibleAbsoluteTileBounds(
                out int minimumTileX,
                out int maximumTileX,
                out int minimumTileY,
                out int maximumTileY);
            Assert.That(hasVisibleBounds, Is.True, $"{caseName}: 絶対可視範囲を取得できません。");

            int chunkMinimumX = activeChunk.X * WorldGridConstants.LogicalChunkSize;
            int chunkMaximumX = chunkMinimumX + WorldGridConstants.LogicalChunkSize - 1;
            int chunkMinimumY = activeChunk.Y * WorldGridConstants.LogicalChunkSize;
            int chunkMaximumY = chunkMinimumY + WorldGridConstants.LogicalChunkSize - 1;
            bool intersectsActiveChunk = minimumTileX <= chunkMaximumX
                && maximumTileX >= chunkMinimumX
                && minimumTileY <= chunkMaximumY
                && maximumTileY >= chunkMinimumY;
            Assert.That(
                intersectsActiveChunk,
                Is.True,
                $"{caseName}: 開発者可視化の絶対可視範囲がActiveチャンクと交差していません。");

            bool hasCenterTile = developerVisualization.TryGetAbsoluteCameraCenterTile(
                out int2 centerAbsoluteTile);
            Assert.That(hasCenterTile, Is.True, $"{caseName}: Camera中央の絶対タイルを取得できません。");
            Assert.That(
                centerAbsoluteTile,
                Is.EqualTo(
                    new int2(
                        Mathf.FloorToInt(absoluteCameraPosition.x),
                        Mathf.FloorToInt(absoluteCameraPosition.y))),
                $"{caseName}: 中央タイル情報がCameraの絶対セルと一致していません。");
            Assert.That(centerAbsoluteTile.x, Is.InRange(minimumTileX, maximumTileX));
            Assert.That(centerAbsoluteTile.y, Is.InRange(minimumTileY, maximumTileY));

            bool hasDisplayCell = developerVisualization.TryGetDisplayCellForAbsoluteTile(
                centerAbsoluteTile,
                out Vector3Int developerDisplayCell);
            Assert.That(hasDisplayCell, Is.True, $"{caseName}: 中央の絶対セルを表示セルへ戻せません。");
            Vector3Int expectedDisplayCell = ToDisplayCell(
                centerAbsoluteTile.x,
                centerAbsoluteTile.y,
                cameraPanController.CurrentDisplayOrigin);
            Assert.That(
                developerDisplayCell,
                Is.EqualTo(expectedDisplayCell),
                $"{caseName}: 色面の投影セルがTilemapの表示セルと一致していません。");
            Vector3Int cameraGridCell = terrainTilemap.layoutGrid.WorldToCell(
                cameraPanController.transform.position);
            Assert.That(
                new Vector3Int(cameraGridCell.x, cameraGridCell.y, 0),
                Is.EqualTo(developerDisplayCell),
                $"{caseName}: 中央情報と色面がGameビュー上の別セルを参照しています。");
        }

        /// <summary>
        /// 再生成前後の1タイルについて、表示分類だけでなくゲームルールが参照する全フィールドの一致を保証します。
        /// </summary>
        /// <param name="expected">遠方移動前に原点チャンクから退避したタイル値です。</param>
        /// <param name="actual">原点へ戻った後に再生成されたタイル値です。</param>
        /// <param name="tileIndex">失敗メッセージへ表示するチャンク内の行優先Indexです。</param>
        private static void AssertTileEquals(GeneratedTile expected, GeneratedTile actual, int tileIndex)
        {
            Assert.That(actual.Biome, Is.EqualTo(expected.Biome), $"Biomeが一致しません。Index={tileIndex}");
            Assert.That(actual.Ground, Is.EqualTo(expected.Ground), $"Groundが一致しません。Index={tileIndex}");
            Assert.That(actual.Soil, Is.EqualTo(expected.Soil), $"Soilが一致しません。Index={tileIndex}");
            Assert.That(
                actual.WaterSalinity,
                Is.EqualTo(expected.WaterSalinity),
                $"WaterSalinityが一致しません。Index={tileIndex}");
            Assert.That(
                actual.WaterBodyType,
                Is.EqualTo(expected.WaterBodyType),
                $"WaterBodyTypeが一致しません。Index={tileIndex}");
            Assert.That(
                actual.ElevationClass,
                Is.EqualTo(expected.ElevationClass),
                $"ElevationClassが一致しません。Index={tileIndex}");
            Assert.That(
                actual.GameplayFlags,
                Is.EqualTo(expected.GameplayFlags),
                $"GameplayFlagsが一致しません。Index={tileIndex}");
            Assert.That(
                actual.ResourceSuitability,
                Is.EqualTo(expected.ResourceSuitability),
                $"ResourceSuitabilityが一致しません。Index={tileIndex}");
            Assert.That(
                actual.MovementCost,
                Is.EqualTo(expected.MovementCost),
                $"MovementCostが一致しません。Index={tileIndex}");
            Assert.That(actual.Elevation, Is.EqualTo(expected.Elevation), $"Elevationが一致しません。Index={tileIndex}");
            Assert.That(actual.Temperature, Is.EqualTo(expected.Temperature), $"Temperatureが一致しません。Index={tileIndex}");
            Assert.That(actual.Moisture, Is.EqualTo(expected.Moisture), $"Moistureが一致しません。Index={tileIndex}");
            Assert.That(actual.Weirdness, Is.EqualTo(expected.Weirdness), $"Weirdnessが一致しません。Index={tileIndex}");
            Assert.That(actual.TerrainTemperature, Is.EqualTo(expected.TerrainTemperature), $"TerrainTemperatureが一致しません。Index={tileIndex}");
            Assert.That(actual.WoodyVegetationPotential, Is.EqualTo(expected.WoodyVegetationPotential), $"WoodyVegetationPotentialが一致しません。Index={tileIndex}");
            Assert.That(actual.TerrainRuggedness, Is.EqualTo(expected.TerrainRuggedness), $"TerrainRuggednessが一致しません。Index={tileIndex}");
            Assert.That(actual.CoastalInfluence, Is.EqualTo(expected.CoastalInfluence), $"CoastalInfluenceが一致しません。Index={tileIndex}");
            Assert.That(actual.VolcanicActivityPotential, Is.EqualTo(expected.VolcanicActivityPotential), $"VolcanicActivityPotentialが一致しません。Index={tileIndex}");
            Assert.That(actual.GeologyClass, Is.EqualTo(expected.GeologyClass), $"GeologyClassが一致しません。Index={tileIndex}");
            Assert.That(actual.GeologyFlags, Is.EqualTo(expected.GeologyFlags), $"GeologyFlagsが一致しません。Index={tileIndex}");
            Assert.That(actual.GeologyPermeability, Is.EqualTo(expected.GeologyPermeability), $"GeologyPermeabilityが一致しません。Index={tileIndex}");
            Assert.That(actual.GeologyGroundwaterStorage, Is.EqualTo(expected.GeologyGroundwaterStorage), $"GeologyGroundwaterStorageが一致しません。Index={tileIndex}");
            Assert.That(actual.GeologyErosionResistance, Is.EqualTo(expected.GeologyErosionResistance), $"GeologyErosionResistanceが一致しません。Index={tileIndex}");
        }

        /// <summary>
        /// 再生成前後の資源IDと表示差分を比較し、地形と同じ決定論が維持されていることを保証します。
        /// </summary>
        /// <param name="expected">遠方移動前に退避した資源データです。</param>
        /// <param name="actual">原点へ戻った後に再生成された資源データです。</param>
        /// <param name="tileIndex">失敗メッセージへ表示するチャンク内の行優先Indexです。</param>
        private static void AssertResourceEquals(
            GeneratedResourceTile expected,
            GeneratedResourceTile actual,
            int tileIndex)
        {
            Assert.That(actual.Type, Is.EqualTo(expected.Type), $"資源IDが一致しません。Index={tileIndex}");
            Assert.That(
                actual.VisualVariant,
                Is.EqualTo(expected.VisualVariant),
                $"資源表示差分が一致しません。Index={tileIndex}");
        }

        /// <summary>
        /// PresenterのWorld切替・無効化テスト用に、最小のGrid、地形Tilemap、境界Tilemap、Presenterを作成します。
        /// </summary>
        /// <param name="rootObject">テスト終了時に破棄するGridとPresenterの親Objectです。</param>
        /// <param name="terrainTilemap">残存Tileの有無を検証する地形Tilemapです。</param>
        /// <param name="landBoundaryHighlightTilemap">土地境界の表示残りを検証するTilemapです。</param>
        /// <param name="rockWallBoundaryHighlightTilemap">岩壁境界の表示残りを検証するTilemapです。</param>
        /// <returns>地形Tilemapと境界Tilemapを設定したテスト対象Presenterです。</returns>
        private static ChunkTilemapPresenter CreateLifecycleTestPresenter(
            out GameObject rootObject,
            out Tilemap terrainTilemap,
            out Tilemap landBoundaryHighlightTilemap,
            out Tilemap rockWallBoundaryHighlightTilemap)
        {
            rootObject = new GameObject("Tilemap Presenter Lifecycle Root");
            rootObject.SetActive(false);
            rootObject.AddComponent<Grid>();

            GameObject tilemapObject = new GameObject("Lifecycle Ground Tilemap");
            tilemapObject.transform.SetParent(rootObject.transform, false);
            terrainTilemap = tilemapObject.AddComponent<Tilemap>();
            tilemapObject.AddComponent<TilemapRenderer>();

            // 境界表示もPresenterが所有するため、地形と同じライフサイクルで確実に消去されるかを確認します。
            GameObject landBoundaryObject = new GameObject("Lifecycle Land Boundary Highlight Tilemap");
            landBoundaryObject.transform.SetParent(rootObject.transform, false);
            landBoundaryHighlightTilemap = landBoundaryObject.AddComponent<Tilemap>();
            landBoundaryObject.AddComponent<TilemapRenderer>();

            GameObject rockWallBoundaryObject = new GameObject("Lifecycle Rock Wall Boundary Highlight Tilemap");
            rockWallBoundaryObject.transform.SetParent(rootObject.transform, false);
            rockWallBoundaryHighlightTilemap = rockWallBoundaryObject.AddComponent<Tilemap>();
            rockWallBoundaryObject.AddComponent<TilemapRenderer>();

            ChunkTilemapPresenter presenter = rootObject.AddComponent<ChunkTilemapPresenter>();
            presenter.Configure(
                terrainTilemap,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
            presenter.ConfigureBoundaryHighlights(
                landBoundaryHighlightTilemap,
                rockWallBoundaryHighlightTilemap,
                null);
            rootObject.SetActive(true);
            return presenter;
        }
    }
}
