// このファイルは、負座標変換、安定シード、ノイズ境界、チャンク決定性、ECS生成SystemをEditModeで検証します。
// 失敗時にどの仕様が崩れたか分かるよう、純粋計算とEntityデータフローを分けてテストします。

using System;
using NUnit.Framework;
using Unity.Entities;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// Phase 1〜2の決定論的チャンク基盤が、境界値とECS実行の両方で同じ結果を作ることを保証します。
    /// Unity Test RunnerのEditModeテストから実行されます。
    /// </summary>
    public sealed class WorldGenerationTests
    {
        /// <summary>
        /// 正負の境界タイルを準備し、床方向除算で期待するチャンク座標とローカル座標になることを検証します。
        /// </summary>
        /// <param name="tile">XとYの両方に使うワールドタイル座標です。</param>
        /// <param name="expectedChunk">期待するXとY共通の論理チャンク座標です。</param>
        /// <param name="expectedLocal">期待するXとY共通のチャンク内座標です。</param>
        [TestCase(0, 0, 0)]
        [TestCase(63, 0, 63)]
        [TestCase(64, 1, 0)]
        [TestCase(-1, -1, 63)]
        [TestCase(-64, -1, 0)]
        [TestCase(-65, -2, 63)]
        public void CoordinateConversion_UsesFloorDivisionForNegativeTiles(
            int tile,
            int expectedChunk,
            int expectedLocal)
        {
            ChunkCoordinate chunk = WorldGrid.ToChunkCoordinate(tile, tile);
            ChunkLocalTileCoordinate local = WorldGrid.ToLocalTileCoordinate(tile, tile);

            Assert.That(chunk.X, Is.EqualTo(expectedChunk));
            Assert.That(chunk.Y, Is.EqualTo(expectedChunk));
            Assert.That(local.X, Is.EqualTo(expectedLocal));
            Assert.That(local.Y, Is.EqualTo(expectedLocal));
        }

        /// <summary>
        /// 既知の文字列を入力し、FNV-1aの固定値と一致することで.NETの可変HashCodeへ戻っていないことを保証します。
        /// </summary>
        [Test]
        public void SeedConversion_KnownTextProducesStableValue()
        {
            ulong actual = WorldSeedUtility.FromString("Farmware");

            Assert.That(actual, Is.EqualTo(0x3EE49B3100677454UL));
        }

        /// <summary>
        /// 格子境界の直前と直後を準備し、SmoothStep補間が正負どちらの境界でも急な段差を作らないことを検証します。
        /// </summary>
        [Test]
        public void NoiseSampling_AcrossGridBoundaryRemainsContinuous()
        {
            const ulong seed = 0x123456789ABCDEF0UL;
            const int cellSize = 64;
            const int maximumOneTileDifference = 128;

            ushort positiveBefore = DeterministicNoise.SampleValueNoise(
                seed, 1, 63, 17, cellSize, WorldGenerationPurposeSalts.Elevation);
            ushort positiveAfter = DeterministicNoise.SampleValueNoise(
                seed, 1, 64, 17, cellSize, WorldGenerationPurposeSalts.Elevation);
            ushort negativeBefore = DeterministicNoise.SampleValueNoise(
                seed, 1, -1, 17, cellSize, WorldGenerationPurposeSalts.Elevation);
            ushort negativeAfter = DeterministicNoise.SampleValueNoise(
                seed, 1, 0, 17, cellSize, WorldGenerationPurposeSalts.Elevation);

            // 128/65535は約0.2%です。整数丸めは許容しつつ、視認できる格子境界の段差は検出します。
            Assert.That(Math.Abs(positiveAfter - positiveBefore), Is.LessThanOrEqualTo(maximumOneTileDifference));
            Assert.That(Math.Abs(negativeAfter - negativeBefore), Is.LessThanOrEqualTo(maximumOneTileDifference));
        }

        /// <summary>
        /// 同じ設定で複数チャンクを異なる順序に計算し、各座標のハッシュが呼び出し順に依存しないことを保証します。
        /// </summary>
        [Test]
        public void ChunkHash_IsIndependentFromGenerationOrder()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("order-test"),
                1);
            ChunkCoordinate firstCoordinate = new ChunkCoordinate(-2, 3);
            ChunkCoordinate secondCoordinate = new ChunkCoordinate(4, -1);

            ulong firstThenSecondFirst = DeterministicChunkGenerator.ComputeChunkHash(settings, firstCoordinate);
            ulong firstThenSecondSecond = DeterministicChunkGenerator.ComputeChunkHash(settings, secondCoordinate);
            ulong secondThenFirstSecond = DeterministicChunkGenerator.ComputeChunkHash(settings, secondCoordinate);
            ulong secondThenFirstFirst = DeterministicChunkGenerator.ComputeChunkHash(settings, firstCoordinate);

            Assert.That(firstThenSecondFirst, Is.EqualTo(secondThenFirstFirst));
            Assert.That(firstThenSecondSecond, Is.EqualTo(secondThenFirstSecond));
            Assert.That(firstThenSecondFirst, Is.Not.EqualTo(firstThenSecondSecond));
        }

        /// <summary>
        /// 隣接する正負チャンクの端を準備し、チャンク生成値が同じワールド座標の直接生成値と一致することを保証します。
        /// </summary>
        [Test]
        public void TileGeneration_ChunkEdgesUseWorldCoordinates()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("edge-test"),
                1);

            AssertTileEqual(
                DeterministicChunkGenerator.GenerateTile(settings, 63, -1),
                GenerateTileThroughChunkCoordinates(settings, 0, -1, 63, 63));
            AssertTileEqual(
                DeterministicChunkGenerator.GenerateTile(settings, 64, 0),
                GenerateTileThroughChunkCoordinates(settings, 1, 0, 0, 0));
            AssertTileEqual(
                DeterministicChunkGenerator.GenerateTile(settings, -65, -64),
                GenerateTileThroughChunkCoordinates(settings, -2, -1, 63, 0));
        }

        /// <summary>
        /// 現在の試作既定値を作り、64×64チャンクとは独立したバイオーム規模と移行帯幅が設定されることを保証します。
        /// </summary>
        [Test]
        public void DefaultSettings_UseTunedBiomeScaleAndTransitionWidth()
        {
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("default-settings-test"),
                2);

            Assert.That(settings.TemperatureCellSizeInTiles, Is.EqualTo(192));
            Assert.That(settings.MoistureCellSizeInTiles, Is.EqualTo(160));
            Assert.That(settings.BiomeTransitionWidthInTiles, Is.EqualTo(6));
        }

        /// <summary>
        /// 同じシードを移行帯あり・なしで生成し、論理Biomeと気候値を維持したまま境界付近の陸上地面だけが混ざることを保証します。
        /// </summary>
        [Test]
        public void BiomeTransition_ChangesGroundWithoutChangingLogicalBiome()
        {
            WorldGenerationSettings transitionSettings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("biome-transition-test"),
                2);
            WorldGenerationSettings discreteSettings = transitionSettings;
            discreteSettings.BiomeTransitionWidthInTiles = 0;
            int changedGroundTileCount = 0;

            // 正負座標と複数チャンク境界を含む範囲を調べ、移行判定が特定チャンク内だけに閉じていないことも確認します。
            for (int tileY = -96; tileY < 96; tileY++)
            {
                for (int tileX = -96; tileX < 96; tileX++)
                {
                    GeneratedTile transitioned = DeterministicChunkGenerator.GenerateTile(
                        transitionSettings,
                        tileX,
                        tileY);
                    GeneratedTile discrete = DeterministicChunkGenerator.GenerateTile(
                        discreteSettings,
                        tileX,
                        tileY);

                    Assert.That(transitioned.Biome, Is.EqualTo(discrete.Biome));
                    Assert.That(transitioned.Elevation, Is.EqualTo(discrete.Elevation));
                    Assert.That(transitioned.Temperature, Is.EqualTo(discrete.Temperature));
                    Assert.That(transitioned.Moisture, Is.EqualTo(discrete.Moisture));

                    if (transitioned.Ground != discrete.Ground)
                    {
                        // 水域と岩壁はバイオーム移行の対象外であり、地形ルールを変えてはならないため明示的に除外を保証します。
                        Assert.That(discrete.Ground, Is.Not.EqualTo(GroundType.Water));
                        Assert.That(discrete.Ground, Is.Not.EqualTo(GroundType.RockWall));
                        changedGroundTileCount++;
                    }
                }
            }

            Assert.That(
                changedGroundTileCount,
                Is.GreaterThan(0),
                "試験範囲にバイオーム移行地面が1件も生成されませんでした。");
        }

        /// <summary>
        /// テスト用Worldへ設定Entityと要求チャンクを作り、System更新後に4096タイルと完了状態が得られることを保証します。
        /// </summary>
        [Test]
        public void ChunkGenerationSystem_ProducesBufferAndDisablesRequest()
        {
            using World world = new World("WorldGenerationTests");
            EntityManager entityManager = world.EntityManager;
            WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                WorldSeedUtility.FromString("ecs-system-test"),
                1);

            Entity settingsEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(settingsEntity, settings);

            Entity chunkEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(chunkEntity, new ChunkCoordinateComponent { X = 1, Y = -1 });
            entityManager.AddComponentData(chunkEntity, new ChunkGenerationStatus
            {
                Phase = ChunkGenerationPhase.Queued,
                GeneratedWithVersion = 0
            });
            entityManager.AddComponent<ChunkGenerationRequest>(chunkEntity);
            entityManager.AddBuffer<GeneratedTile>(chunkEntity);

            // ISystemを明示的に1回更新し、テスト終了前にJobを完了してからEntityデータを読み取ります。
            SystemHandle system = world.GetOrCreateSystem<ChunkGenerationSystem>();
            system.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();

            ChunkGenerationStatus status = entityManager.GetComponentData<ChunkGenerationStatus>(chunkEntity);
            DynamicBuffer<GeneratedTile> tiles = entityManager.GetBuffer<GeneratedTile>(chunkEntity);

            Assert.That(status.Phase, Is.EqualTo(ChunkGenerationPhase.ReadyToApply));
            Assert.That(status.GeneratedWithVersion, Is.EqualTo(settings.GeneratorVersion));
            Assert.That(tiles.Length, Is.EqualTo(WorldGridConstants.TilesPerChunk));
            Assert.That(entityManager.IsComponentEnabled<ChunkGenerationRequest>(chunkEntity), Is.False);

            GeneratedTile expectedFirstTile = DeterministicChunkGenerator.GenerateTile(settings, 64, -64);
            AssertTileEqual(expectedFirstTile, tiles[0]);
        }

        /// <summary>
        /// 原点だけが見えるObserverと同時生成上限3を準備し、中心優先かつ上限以内で要求が作られることを保証します。
        /// </summary>
        [Test]
        public void ChunkStreamingSystem_QueuesNearChunksWithinConcurrentLimit()
        {
            using World world = new World("ChunkStreamingQueueTests");
            EntityManager entityManager = world.EntityManager;
            CreateStreamingSingletons(
                entityManager,
                new ChunkStreamingObserver
                {
                    MinimumVisibleChunkX = 0,
                    MaximumVisibleChunkX = 0,
                    MinimumVisibleChunkY = 0,
                    MaximumVisibleChunkY = 0
                },
                new ChunkStreamingSettings
                {
                    PreloadMarginInChunks = 1,
                    RetentionMarginInChunks = 1,
                    MaximumConcurrentGenerations = 3,
                    MaximumSubChunkApplicationsPerFrame = 1
                });

            SystemHandle streamingSystem = world.GetOrCreateSystem<ChunkStreamingSystem>();
            streamingSystem.Update(world.Unmanaged);

            EntityQuery chunkQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ChunkCoordinateComponent>(),
                ComponentType.ReadOnly<ChunkGenerationStatus>());
            Assert.That(chunkQuery.CalculateEntityCount(), Is.EqualTo(3));
            Assert.That(ContainsChunkCoordinate(chunkQuery, 0, 0), Is.True);

            // 生成Jobを進めない2回目の更新では3要求が上限を満たしたままなので、重複Entityを追加しません。
            streamingSystem.Update(world.Unmanaged);
            Assert.That(chunkQuery.CalculateEntityCount(), Is.EqualTo(3));
            chunkQuery.Dispose();
        }

        /// <summary>
        /// プリロード外かつ保持範囲内の未開始要求を準備し、要求だけが取り消されEntityは再利用可能に残ることを保証します。
        /// </summary>
        [Test]
        public void ChunkStreamingSystem_CancelsQueuedChunkOutsidePreloadRange()
        {
            using World world = new World("ChunkStreamingCancellationTests");
            EntityManager entityManager = world.EntityManager;
            CreateStreamingSingletons(
                entityManager,
                new ChunkStreamingObserver
                {
                    MinimumVisibleChunkX = 0,
                    MaximumVisibleChunkX = 0,
                    MinimumVisibleChunkY = 0,
                    MaximumVisibleChunkY = 0
                },
                new ChunkStreamingSettings
                {
                    PreloadMarginInChunks = 1,
                    RetentionMarginInChunks = 1,
                    MaximumConcurrentGenerations = 1,
                    MaximumSubChunkApplicationsPerFrame = 1
                });
            Entity queuedEntity = CreateChunkEntity(
                entityManager,
                2,
                0,
                ChunkGenerationPhase.Queued,
                requestEnabled: true);

            SystemHandle streamingSystem = world.GetOrCreateSystem<ChunkStreamingSystem>();
            streamingSystem.Update(world.Unmanaged);

            Assert.That(entityManager.Exists(queuedEntity), Is.True);
            Assert.That(
                entityManager.GetComponentData<ChunkGenerationStatus>(queuedEntity).Phase,
                Is.EqualTo(ChunkGenerationPhase.Unrequested));
            Assert.That(entityManager.IsComponentEnabled<ChunkGenerationRequest>(queuedEntity), Is.False);
        }

        /// <summary>
        /// 保持範囲外の古いチャンクと離れた新しいObserverを準備し、古いEntityを破棄して新中心を要求することを保証します。
        /// </summary>
        [Test]
        public void ChunkStreamingSystem_UnloadsOutsideRetentionAndRequestsNewCenter()
        {
            using World world = new World("ChunkStreamingUnloadTests");
            EntityManager entityManager = world.EntityManager;
            CreateStreamingSingletons(
                entityManager,
                new ChunkStreamingObserver
                {
                    MinimumVisibleChunkX = 10,
                    MaximumVisibleChunkX = 10,
                    MinimumVisibleChunkY = -5,
                    MaximumVisibleChunkY = -5
                },
                new ChunkStreamingSettings
                {
                    PreloadMarginInChunks = 1,
                    RetentionMarginInChunks = 0,
                    MaximumConcurrentGenerations = 1,
                    MaximumSubChunkApplicationsPerFrame = 1
                });
            Entity oldEntity = CreateChunkEntity(
                entityManager,
                0,
                0,
                ChunkGenerationPhase.Active,
                requestEnabled: false);

            SystemHandle streamingSystem = world.GetOrCreateSystem<ChunkStreamingSystem>();
            streamingSystem.Update(world.Unmanaged);

            Assert.That(entityManager.Exists(oldEntity), Is.False);
            EntityQuery chunkQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ChunkCoordinateComponent>());
            Assert.That(chunkQuery.CalculateEntityCount(), Is.EqualTo(1));
            Assert.That(ContainsChunkCoordinate(chunkQuery, 10, -5), Is.True);
            chunkQuery.Dispose();
        }

        /// <summary>
        /// 表示済みチャンクを可視範囲外へ移動させ、保持データを破棄せずInactiveへ遷移することを保証します。
        /// </summary>
        [Test]
        public void ChunkStreamingSystem_MarksRetainedVisibleDataInactive()
        {
            using World world = new World("ChunkStreamingInactiveTests");
            EntityManager entityManager = world.EntityManager;
            CreateStreamingSingletons(
                entityManager,
                new ChunkStreamingObserver
                {
                    MinimumVisibleChunkX = 2,
                    MaximumVisibleChunkX = 2,
                    MinimumVisibleChunkY = 0,
                    MaximumVisibleChunkY = 0
                },
                new ChunkStreamingSettings
                {
                    PreloadMarginInChunks = 1,
                    RetentionMarginInChunks = 1,
                    MaximumConcurrentGenerations = 1,
                    MaximumSubChunkApplicationsPerFrame = 1
                });
            Entity retainedEntity = CreateChunkEntity(
                entityManager,
                0,
                0,
                ChunkGenerationPhase.Active,
                requestEnabled: false);

            SystemHandle streamingSystem = world.GetOrCreateSystem<ChunkStreamingSystem>();
            streamingSystem.Update(world.Unmanaged);

            Assert.That(entityManager.Exists(retainedEntity), Is.True);
            Assert.That(
                entityManager.GetComponentData<ChunkGenerationStatus>(retainedEntity).Phase,
                Is.EqualTo(ChunkGenerationPhase.Inactive));
        }

        /// <summary>
        /// 複数のキャンセル済みEntityを可視範囲内へ戻し、再有効化も同時生成上限を超えないことを保証します。
        /// </summary>
        [Test]
        public void ChunkStreamingSystem_ReactivationRespectsConcurrentLimit()
        {
            using World world = new World("ChunkStreamingReactivationTests");
            EntityManager entityManager = world.EntityManager;
            CreateStreamingSingletons(
                entityManager,
                new ChunkStreamingObserver
                {
                    MinimumVisibleChunkX = 0,
                    MaximumVisibleChunkX = 0,
                    MinimumVisibleChunkY = 0,
                    MaximumVisibleChunkY = 0
                },
                new ChunkStreamingSettings
                {
                    PreloadMarginInChunks = 1,
                    RetentionMarginInChunks = 1,
                    MaximumConcurrentGenerations = 1,
                    MaximumSubChunkApplicationsPerFrame = 1
                });
            Entity centerEntity = CreateChunkEntity(
                entityManager,
                0,
                0,
                ChunkGenerationPhase.Unrequested,
                requestEnabled: false);
            Entity neighboringEntity = CreateChunkEntity(
                entityManager,
                1,
                0,
                ChunkGenerationPhase.Unrequested,
                requestEnabled: false);

            SystemHandle streamingSystem = world.GetOrCreateSystem<ChunkStreamingSystem>();
            streamingSystem.Update(world.Unmanaged);

            Assert.That(entityManager.IsComponentEnabled<ChunkGenerationRequest>(centerEntity), Is.True);
            Assert.That(
                entityManager.GetComponentData<ChunkGenerationStatus>(centerEntity).Phase,
                Is.EqualTo(ChunkGenerationPhase.Queued));
            Assert.That(entityManager.IsComponentEnabled<ChunkGenerationRequest>(neighboringEntity), Is.False);
            Assert.That(
                entityManager.GetComponentData<ChunkGenerationStatus>(neighboringEntity).Phase,
                Is.EqualTo(ChunkGenerationPhase.Unrequested));
        }

        /// <summary>
        /// テスト用Worldへ生成設定、ストリーミング設定、Observerの3つのSingleton Entityを準備します。
        /// </summary>
        /// <param name="entityManager">テスト用WorldのEntityManagerです。</param>
        /// <param name="observer">カメラ表示範囲として入力する論理チャンク矩形です。</param>
        /// <param name="streamingSettings">生成上限と保持余白を含むテスト条件です。</param>
        private static void CreateStreamingSingletons(
            EntityManager entityManager,
            ChunkStreamingObserver observer,
            ChunkStreamingSettings streamingSettings)
        {
            Entity settingsEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(
                settingsEntity,
                WorldGenerationSettings.CreateDefault(WorldSeedUtility.FromString("streaming-test"), 1));
            entityManager.AddComponentData(settingsEntity, streamingSettings);

            Entity observerEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(observerEntity, observer);
        }

        /// <summary>
        /// ストリーミングテスト用のチャンクEntityを指定状態で作り、空のGeneratedTile Bufferも追加します。
        /// </summary>
        /// <param name="entityManager">テスト用WorldのEntityManagerです。</param>
        /// <param name="x">論理チャンクの水平座標です。</param>
        /// <param name="y">論理チャンクの垂直座標です。</param>
        /// <param name="phase">準備する生成段階です。</param>
        /// <param name="requestEnabled">ChunkGenerationRequestを有効にする場合はtrueです。</param>
        /// <returns>作成したテスト用チャンクEntityです。</returns>
        private static Entity CreateChunkEntity(
            EntityManager entityManager,
            int x,
            int y,
            ChunkGenerationPhase phase,
            bool requestEnabled)
        {
            Entity entity = entityManager.CreateEntity();
            entityManager.AddComponentData(entity, new ChunkCoordinateComponent { X = x, Y = y });
            entityManager.AddComponentData(entity, new ChunkGenerationStatus
            {
                Phase = phase,
                GeneratedWithVersion = phase == ChunkGenerationPhase.Queued ? 0 : 1
            });
            entityManager.AddComponent<ChunkGenerationRequest>(entity);
            entityManager.SetComponentEnabled<ChunkGenerationRequest>(entity, requestEnabled);
            entityManager.AddBuffer<GeneratedTile>(entity);
            return entity;
        }

        /// <summary>
        /// Query結果を走査し、指定した論理チャンク座標がちょうど1つ以上存在するか確認します。
        /// </summary>
        /// <param name="chunkQuery">ChunkCoordinateComponentを含むテスト用Queryです。</param>
        /// <param name="x">検索する水平チャンク座標です。</param>
        /// <param name="y">検索する垂直チャンク座標です。</param>
        /// <returns>一致する座標が見つかった場合はtrueです。</returns>
        private static bool ContainsChunkCoordinate(EntityQuery chunkQuery, int x, int y)
        {
            using Unity.Collections.NativeArray<ChunkCoordinateComponent> coordinates =
                chunkQuery.ToComponentDataArray<ChunkCoordinateComponent>(Unity.Collections.Allocator.Temp);
            for (int index = 0; index < coordinates.Length; index++)
            {
                if (coordinates[index].X == x && coordinates[index].Y == y)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// チャンク座標とローカル座標をワールド座標へ戻して1タイルを生成します。
        /// </summary>
        /// <param name="settings">テスト対象の生成設定です。</param>
        /// <param name="chunkX">論理チャンクの水平座標です。</param>
        /// <param name="chunkY">論理チャンクの垂直座標です。</param>
        /// <param name="localX">チャンク内の水平座標です。</param>
        /// <param name="localY">チャンク内の垂直座標です。</param>
        /// <returns>復元したワールド座標から生成したタイルです。</returns>
        private static GeneratedTile GenerateTileThroughChunkCoordinates(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            int localX,
            int localY)
        {
            int worldTileX = WorldGrid.ToWorldTileX(chunkX, localX);
            int worldTileY = WorldGrid.ToWorldTileY(chunkY, localY);
            return DeterministicChunkGenerator.GenerateTile(settings, worldTileX, worldTileY);
        }

        /// <summary>
        /// GeneratedTileの全フィールドを個別に比較し、失敗した分類または気候値をTest Runnerへ明示します。
        /// </summary>
        /// <param name="expected">期待するタイル値です。</param>
        /// <param name="actual">実際に生成されたタイル値です。</param>
        private static void AssertTileEqual(GeneratedTile expected, GeneratedTile actual)
        {
            Assert.That(actual.Biome, Is.EqualTo(expected.Biome));
            Assert.That(actual.Ground, Is.EqualTo(expected.Ground));
            Assert.That(actual.ElevationClass, Is.EqualTo(expected.ElevationClass));
            Assert.That(actual.GameplayFlags, Is.EqualTo(expected.GameplayFlags));
            Assert.That(actual.ResourceSuitability, Is.EqualTo(expected.ResourceSuitability));
            Assert.That(actual.MovementCost, Is.EqualTo(expected.MovementCost));
            Assert.That(actual.Elevation, Is.EqualTo(expected.Elevation));
            Assert.That(actual.Temperature, Is.EqualTo(expected.Temperature));
            Assert.That(actual.Moisture, Is.EqualTo(expected.Moisture));
        }
    }
}
