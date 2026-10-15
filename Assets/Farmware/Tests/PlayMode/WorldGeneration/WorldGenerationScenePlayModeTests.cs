// このファイルは、SampleSceneを実際に再生し、初期カメラ位置、ECS地形・資源生成、Tilemap表示までの縦切りを検証します。
// F3/F4と開発者用表示を含め、純粋計算では確認できないGameObject・SubScene・Presentation間の参照切れを検出します。

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.PlayModeTests
{
    /// <summary>
    /// SampleSceneの起動後にデバッグ表示、生成設定Singleton、チャンクEntity、地形・資源Tileが正しく連携することを保証します。
    /// Unity Test RunnerのPlayModeテストから実行されます。
    /// </summary>
    public sealed class WorldGenerationScenePlayModeTests
    {
        /// <summary>SubSceneの非同期Bake・ロードと初回チャンク生成を待つ最大描画フレーム数です。</summary>
        private const int MaximumWaitFrames = 300;

        /// <summary>初期スポーンチャンクが保持範囲外へ確実に出る、テスト用カメラ水平移動量です。</summary>
        private const int FarCameraOffsetInTiles = WorldGridConstants.LogicalChunkSize * 8;


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
        /// 現在の負座標と記録済み初期スポーンを準備し、F3文字列が両者を混同せずタイル・チャンク座標として表示することを保証します。
        /// </summary>
        [Test]
        public void BuildCoordinateHudText_IncludesRecordedInitialSpawnLocation()
        {
            InitialSpawnLocation spawnLocation = new InitialSpawnLocation
            {
                TileX = -65,
                TileY = 64
            };

            string text = WorldGridDebugOverlay.BuildCoordinateHudText(
                new Vector3(5.5f, -2.5f, -10f),
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
            Assert.That(text, Does.Contain("SubChunk: (0, 3)"));
            Assert.That(text, Does.Contain("Biome: Snowfield"));
            Assert.That(text, Does.Contain("GeneratorVersion: 7"));
            Assert.That(text, Does.Contain("WorldSeed: 0x0000000000001234"));
            Assert.That(text, Does.Contain("Spawn: Tile (-65, 64) / Chunk (-2, 1)"));
        }

        /// <summary>
        /// 初期スポーン探索が失敗した状態を準備し、F3が探索中ではなくScene保存位置の使用を表示することを保証します。
        /// </summary>
        [Test]
        public void BuildCoordinateHudText_SearchFailedShowsSceneCameraFallback()
        {
            string text = WorldGridDebugOverlay.BuildCoordinateHudText(
                new Vector3(123.5f, -45.25f, -10f),
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
                object[] biomeArguments = { new Vector3Int(5, -3, 0), default(BiomeId) };
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
                Assert.That((BiomeId)biomeArguments[1], Is.EqualTo(BiomeId.Volcanic));
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
                        TileX = 2,
                        TileY = 3
                    });
                Entity secondSpawnEntity = secondWorld.EntityManager.CreateEntity();
                secondWorld.EntityManager.AddComponentData(
                    secondSpawnEntity,
                    new InitialSpawnLocation
                    {
                        TileX = -5,
                        TileY = 7
                    });

                World.DefaultGameObjectInjectionWorld = firstWorld;
                cameraObject = new GameObject("World Recreation Initial Camera");
                cameraObject.SetActive(false);
                cameraObject.AddComponent<Camera>();
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

                // Componentを無効化せずWorldだけを切り替え、以前の完了フラグが新Worldの適用を妨げないことを確認します。
                World.DefaultGameObjectInjectionWorld = secondWorld;
                positionUpdate.Invoke(positionController, null);

                Assert.That(positionController.HasCompletedInitialPositioning, Is.True);
                Assert.That(positionController.HasAppliedInitialPosition, Is.True);
                Assert.That(cameraObject.transform.position.x, Is.EqualTo(-4.5f));
                Assert.That(cameraObject.transform.position.y, Is.EqualTo(7.5f));
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
                    out Tilemap terrainTilemap);
                MethodInfo ensureQueries = typeof(ChunkTilemapPresenter).GetMethod(
                    "TryEnsureQueries",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(ensureQueries, Is.Not.Null);
                Assert.That(ensureQueries.Invoke(presenter, null), Is.EqualTo(true));

                testTile = ScriptableObject.CreateInstance<Tile>();
                terrainTilemap.SetTile(Vector3Int.zero, testTile);
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
                    out Tilemap terrainTilemap);
                testTile = ScriptableObject.CreateInstance<Tile>();
                terrainTilemap.SetTile(Vector3Int.zero, testTile);

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
                    out Tilemap terrainTilemap);
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
        /// 開発者用可視化がBiome、Soil、連続ノイズ、水域、移行帯、資源を異なる色へ写像し、論理BiomeとSoilの差を正しく判定することを検証します。
        /// SceneやECS Worldを作らない純粋計算として、表示色の両端、水域分類、水域除外、最終資源分類をまとめて保証します。
        /// </summary>
        [Test]
        public void DebugVisualization_MapsGeneratedTileValuesWithoutChangingSimulationData()
        {
            GeneratedTile forestTransitionTile = new GeneratedTile
            {
                Biome = BiomeId.Forest,
                Ground = GroundType.Land,
                Soil = SoilType.Normal,
                Elevation = ushort.MaxValue,
                Temperature = 0,
                Moisture = ushort.MaxValue,
                Weirdness = ushort.MaxValue
            };

            Assert.That(
                WorldGenerationDebugVisualizationOverlay.IsBiomeTransitionTile(forestTransitionTile),
                Is.True,
                "森林Biomeで標準土壌が選ばれた陸上タイルは移行帯として強調される必要があります。");

            Color coldColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Temperature,
                forestTransitionTile);
            Assert.That(coldColor.b, Is.GreaterThan(coldColor.r), "最低気温は赤より青が強い必要があります。");

            Color highElevationColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Elevation,
                forestTransitionTile);
            Assert.That(
                highElevationColor.r,
                Is.GreaterThan(0.9f),
                "最高標高は凡例どおり白に近い必要があります。");

            Color highWeirdnessColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.Weirdness,
                forestTransitionTile);
            Assert.That(
                highWeirdnessColor.r,
                Is.GreaterThan(highWeirdnessColor.b),
                "最高Weirdnessは凡例どおり紫より黄に近い必要があります。");

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

            Color transitionColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.BiomeTransition,
                forestTransitionTile);
            Assert.That(
                transitionColor.r,
                Is.GreaterThan(transitionColor.g),
                "移行タイルは暗色ではなくマゼンタ系で強調される必要があります。");

            GeneratedTile waterTile = forestTransitionTile;
            waterTile.Ground = GroundType.Water;
            waterTile.WaterSalinity = WaterSalinity.FreshWater;
            waterTile.WaterBodyType = WaterBodyType.River;
            Assert.That(
                WorldGenerationDebugVisualizationOverlay.IsBiomeTransitionTile(waterTile),
                Is.False,
                "標高分類で生じる水域はバイオーム移行帯へ数えません。");

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
            Assert.That(
                WorldGenerationDebugVisualizationOverlay.IsBiomeTransitionTile(magmaTile),
                Is.False,
                "独立生成されるマグマはバイオーム移行帯へ数えません。");
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

            Color hiddenColor = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                WorldGenerationDebugVisualizationMode.None,
                forestTransitionTile);
            Assert.That(hiddenColor.a, Is.EqualTo(0f), "Offモードは地形を一切着色してはいけません。");
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
            settings.SeaLevelThreshold = 0;
            settings.FreshWaterMaximumElevation = 0;
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

            // 液面の下半分を手前土地で隠す前後関係はTransform位置だけでは決まらないため、Rendererの描画順もScene実体から確認します。
            TilemapRenderer groundRenderer = tilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer rockWallRenderer = rockWallTilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer resourceRenderer = resourceTilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer resourceBehindRockWallRenderer =
                resourceBehindRockWallTilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer liquidBaseRenderer = liquidBaseTilemap.GetComponent<TilemapRenderer>();
            TilemapRenderer liquidAnimationRenderer =
                liquidAnimationTilemap.GetComponent<TilemapRenderer>();
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

            // 1描画フレームだけ両方を有効にし、GUI Style作成、座標変換、タイル・サブチャンク・チャンク線とラベル描画まで例外なく通します。
            yield return null;
            developerVisualization.SetMode(WorldGenerationDebugVisualizationMode.None);
            debugOverlay.SetCoordinateHudVisible(false);
            debugOverlay.SetChunkGridVisible(false);

            World world = null;
            EntityQuery settingsQuery = default;
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
                    && locationQuery.CalculateEntityCount() == 1)
                {
                    initialSpawnLocation = locationQuery.GetSingleton<InitialSpawnLocation>();
                    initialSpawnChunk = WorldGrid.ToChunkCoordinate(
                        initialSpawnLocation.TileX,
                        initialSpawnLocation.TileY);
                    initialCameraWorldPosition = new Vector2(
                        initialSpawnLocation.TileX + 0.5f,
                        initialSpawnLocation.TileY + 0.5f);
                    Vector3Int spawnCell = new Vector3Int(
                        initialSpawnLocation.TileX,
                        initialSpawnLocation.TileY,
                        0);

                    if (initialPositionController.HasAppliedInitialPosition
                        && TryGetActiveCompleteChunk(
                            world.EntityManager,
                            chunkQuery,
                            initialSpawnChunk.X,
                            initialSpawnChunk.Y,
                            out originalChunkEntity)
                        && tilemap.GetTile(spawnCell) != null
                        && TryGetDisplayedResourceCell(
                            world.EntityManager,
                            originalChunkEntity,
                            initialSpawnChunk,
                            resourceTilemap,
                            resourceBehindRockWallTilemap,
                            out originalResourceCell))
                    {
                        initialLoadCompleted = true;
                        break;
                    }
                }

                yield return null;
            }

            Assert.That(
                initialLoadCompleted,
                Is.True,
                $"{MaximumWaitFrames}フレーム以内に初回生成と表示が完了しませんでした。");

            // 一体型岩壁が存在するセルを実際の表示結果から探し、同じ論理セルに土地ブロックも残る二段構成を保証します。
            // TransformとPivotだけを検査すると、Presenterが岩壁を書き忘れてもテストが通るため、Tilemapの内容まで確認します。
            bool foundDisplayedRockWall = false;
            foreach (Vector3Int cellPosition in rockWallTilemap.cellBounds.allPositionsWithin)
            {
                if (!rockWallTilemap.HasTile(cellPosition))
                {
                    continue;
                }

                foundDisplayedRockWall = true;
                Assert.That(
                    tilemap.HasTile(cellPosition),
                    Is.True,
                    $"岩壁セル {cellPosition} の下に、Biome由来の一体型土地ブロックが残っていません。");
            }

            Assert.That(
                foundDisplayedRockWall,
                Is.True,
                "初回表示範囲に一体型岩壁が1枚もなく、二段構成を実表示で検証できませんでした。");
            Assert.That(
                cameraPanController.transform.position.x,
                Is.EqualTo(initialCameraWorldPosition.x).Within(0.00001f),
                "カメラX座標が記録済み初期スポーンタイル中央と一致しません。");
            Assert.That(
                cameraPanController.transform.position.y,
                Is.EqualTo(initialCameraWorldPosition.y).Within(0.00001f),
                "カメラY座標が記録済み初期スポーンタイル中央と一致しません。");

            // 初期移動後も通常カメラ操作は16 PPUへ丸まり、テスト後は記録済みスポーンへ正確に戻せることを確認します。
            cameraPanController.SetWorldPosition(
                initialCameraWorldPosition + new Vector2(0.1f, -0.1f));
            Assert.That(
                cameraPanController.transform.position.x,
                Is.EqualTo(initialCameraWorldPosition.x + 0.125f).Within(0.00001f));
            Assert.That(
                cameraPanController.transform.position.y,
                Is.EqualTo(initialCameraWorldPosition.y - 0.125f).Within(0.00001f));
            cameraPanController.SetWorldPosition(initialCameraWorldPosition);

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

            // 生成済みタイルを使う移行帯表示を1フレーム描画し、ECS Buffer読取、Windowboxクリップ、情報HUDまで例外なく通します。
            developerVisualization.SetMode(WorldGenerationDebugVisualizationMode.BiomeTransition);
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
            cameraPanController.SetWorldPosition(farCameraWorldPosition);
            bool farLoadCompleted = false;
            for (int frame = 0; frame < MaximumWaitFrames; frame++)
            {
                bool originalChunkWasUnloaded = !HasChunkAt(
                    chunkQuery,
                    initialSpawnChunk.X,
                    initialSpawnChunk.Y);
                bool originalTileWasCleared = tilemap.GetTile(
                    new Vector3Int(
                        initialSpawnLocation.TileX,
                        initialSpawnLocation.TileY,
                        0)) == null;
                bool originalResourceWasCleared =
                    resourceTilemap.GetTile(originalResourceCell) == null
                    && resourceBehindRockWallTilemap.GetTile(originalResourceCell) == null;
                bool farChunkIsActive = TryGetActiveCompleteChunk(
                    world.EntityManager,
                    chunkQuery,
                    farCameraChunk.X,
                    farCameraChunk.Y,
                    out _);

                if (originalChunkWasUnloaded
                    && originalTileWasCleared
                    && originalResourceWasCleared
                    && farChunkIsActive)
                {
                    farLoadCompleted = true;
                    break;
                }

                yield return null;
            }

            Assert.That(
                farLoadCompleted,
                Is.True,
                $"{MaximumWaitFrames}フレーム以内に遠方チャンクへの切替と原点のアンロードが完了しませんでした。");

            // 記録済みスポーンへ戻り、破棄済みEntityに依存せず同じシードから同一タイルデータを再生成できることを確認します。
            cameraPanController.SetWorldPosition(initialCameraWorldPosition);
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
                    && tilemap.GetTile(
                        new Vector3Int(
                            initialSpawnLocation.TileX,
                            initialSpawnLocation.TileY,
                            0)) != null)
                {
                    returnLoadCompleted = true;
                    break;
                }

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
                resourceTilemap.GetTile(originalResourceCell) != null
                    || resourceBehindRockWallTilemap.GetTile(originalResourceCell) != null,
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

            cameraPanController.SetWorldPosition(
                new Vector2(liquidCell.x + 0.5f, liquidCell.y + 0.5f));
            bool liquidDisplayCompleted = false;
            for (int frame = 0; frame < MaximumWaitFrames; frame++)
            {
                // 静止面とアニメーションが同じ論理セルへそろって初めて、実際の液面表示が完成したと判断します。
                if (liquidBaseTilemap.HasTile(liquidCell)
                    && liquidAnimationTilemap.HasTile(liquidCell))
                {
                    liquidDisplayCompleted = true;
                    break;
                }

                yield return null;
            }

            Assert.That(
                liquidDisplayCompleted,
                Is.True,
                $"液体セル {liquidCell} の静止面とアニメーションが{MaximumWaitFrames}フレーム以内に表示されませんでした。");
            Assert.That(
                tilemap.HasTile(liquidCell),
                Is.False,
                $"液体セル {liquidCell} がGround Tilemapにも重複し、元の高さの液面が残っています。");

            if (queriesCreated && world != null && world.IsCreated)
            {
                settingsQuery.Dispose();
                locationQuery.Dispose();
                chunkQuery.Dispose();
            }
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
                if (coordinates[index].X == chunkX && coordinates[index].Y == chunkY)
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
                if (coordinates[index].X != chunkX
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
        /// 指定チャンクの資源Bufferから最初の配置済みセルを探し、前面または岩壁背面のResource Tilemapへ表示されたか確認します。
        /// </summary>
        /// <param name="entityManager">Default WorldのEntityManagerです。</param>
        /// <param name="chunkEntity">検査対象チャンクを表す完全生成済みEntityです。</param>
        /// <param name="chunkCoordinate">ローカルBuffer Indexをワールドセルへ戻す論理チャンク座標です。</param>
        /// <param name="resourceTilemap">岩壁に隠れない資源を重ね描画するTilemapです。</param>
        /// <param name="resourceBehindRockWallTilemap">直前方の岩壁に隠す資源を描画するTilemapです。</param>
        /// <param name="resourceCell">成功時に、表示済み資源のワールドセル座標を返します。</param>
        /// <returns>配置済み資源が1件以上あり、そのTileが表示済みならtrueです。</returns>
        private static bool TryGetDisplayedResourceCell(
            EntityManager entityManager,
            Entity chunkEntity,
            ChunkCoordinate chunkCoordinate,
            Tilemap resourceTilemap,
            Tilemap resourceBehindRockWallTilemap,
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
                resourceCell = new Vector3Int(
                    WorldGrid.ToWorldTileX(chunkCoordinate.X, localX),
                    WorldGrid.ToWorldTileY(chunkCoordinate.Y, localY),
                    0);
                return resourceTilemap.GetTile(resourceCell) != null
                    || resourceBehindRockWallTilemap.GetTile(resourceCell) != null;
            }

            resourceCell = default;
            return false;
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
        /// PresenterのWorld切替・無効化テスト用に、最小のGrid、地形Tilemap、Presenterを作成します。
        /// </summary>
        /// <param name="rootObject">テスト終了時に破棄するGridとPresenterの親Objectです。</param>
        /// <param name="terrainTilemap">残存Tileの有無を検証する地形Tilemapです。</param>
        /// <returns>地形Tilemapだけを設定したテスト対象Presenterです。</returns>
        private static ChunkTilemapPresenter CreateLifecycleTestPresenter(
            out GameObject rootObject,
            out Tilemap terrainTilemap)
        {
            rootObject = new GameObject("Tilemap Presenter Lifecycle Root");
            rootObject.SetActive(false);
            rootObject.AddComponent<Grid>();

            GameObject tilemapObject = new GameObject("Lifecycle Ground Tilemap");
            tilemapObject.transform.SetParent(rootObject.transform, false);
            terrainTilemap = tilemapObject.AddComponent<Tilemap>();
            tilemapObject.AddComponent<TilemapRenderer>();

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
            rootObject.SetActive(true);
            return presenter;
        }
    }
}
