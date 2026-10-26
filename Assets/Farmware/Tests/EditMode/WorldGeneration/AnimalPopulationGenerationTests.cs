// このファイルは、ANIMAL-01Bの地域初期個体群、群れ形成、永続ID発行の決定論をEditModeで検証します。
// 64×64の生成済み地形を準備し、純粋生成パイプラインと固定Tick Systemの両方を実行します。

using System.Collections.Generic;
using System.Reflection;
using Farmware.Simulation;
using Farmware.Simulation.Animals;
using Farmware.WorldGeneration.Animals;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Jobs;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 地形Seedと個体群Seedの分離、地域境界、群れ、ID安定順、再初期化防止を保証するテスト群です。
    /// </summary>
    public sealed class AnimalPopulationGenerationTests
    {
        /// <summary>テスト用共通定義で鶏を識別する変更禁止GUIDです。</summary>
        private static readonly DefinitionGuid ChickenGuid = new DefinitionGuid(0, 0xC1);

        /// <summary>純粋な動物適格判定で通常Groundを表す仮Runtime IDです。</summary>
        private static readonly DefinitionRuntimeId TestGroundRuntimeId =
            new DefinitionRuntimeId(1);

        /// <summary>
        /// 同じSeed・地域X/Yを異なるWorldLayerで初期化し、地域キーと乱数系列が互いに衝突しないことを保証します。
        /// </summary>
        [Test]
        public void InitialRegionState_SameXyDifferentLayers_UsesIndependentRandomState()
        {
            AnimalPopulationSeed seed = new AnimalPopulationSeed { Value = 0x123456789ABCDEF0UL };
            AnimalPopulationRegionState surface =
                AnimalPopulationGenerator.CreateInitialRegionState(
                    seed,
                    WorldLayer.Surface,
                    -3,
                    7);
            AnimalPopulationRegionState underwater =
                AnimalPopulationGenerator.CreateInitialRegionState(
                    seed,
                    WorldLayer.Underwater,
                    -3,
                    7);

            Assert.That(surface.Layer, Is.EqualTo(WorldLayer.Surface));
            Assert.That(underwater.Layer, Is.EqualTo(WorldLayer.Underwater));
            Assert.That(surface.RegionX, Is.EqualTo(underwater.RegionX));
            Assert.That(surface.RegionY, Is.EqualTo(underwater.RegionY));
            Assert.That(surface.RandomState, Is.Not.EqualTo(underwater.RandomState));
        }

        /// <summary>
        /// 全面が草原陸地の地域へ、定義どおり8羽を3〜6羽の群れとして重複なしで配置することを保証します。
        /// </summary>
        [Test]
        public void InitialPopulation_CreatesTargetCountInValidHerdsWithoutDuplicateTiles()
        {
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog = default;
            BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog = default;
            BuiltInTerrainBlockRuntimeIds terrainRuntimeIds = CreateTerrainRuntimeIds();
            using NativeArray<GeneratedTerrainCell> tiles = CreateUniformTerrainCells(
                BiomeId.TemperateGrassland,
                TestGroundRuntimeId);
            using NativeArray<GeneratedResourceTile> resources =
                new NativeArray<GeneratedResourceTile>(tiles.Length, Allocator.Temp);
            using NativeList<AnimalSpawnCandidate> candidates =
                new NativeList<AnimalSpawnCandidate>(16, Allocator.Temp);
            try
            {
                CreateCatalogs(out definitionCatalog, out animalCatalog);
                AnimalPopulationRegionState regionState =
                    AnimalPopulationGenerator.CreateInitialRegionState(
                        new AnimalPopulationSeed { Value = 0x123456789ABCDEF0UL },
                        0,
                        0);

                bool succeeded = AnimalPopulationGenerator.TryAppendInitialPopulation(
                    animalCatalog,
                    tiles,
                    resources,
                    terrainRuntimeIds,
                    ref regionState,
                    candidates);

                Assert.That(succeeded, Is.True);
                Assert.That(candidates.Length, Is.EqualTo(8));
                Assert.That(regionState.NextSpawnSequence, Is.EqualTo(9ul));

                HashSet<(int X, int Y)> uniquePositions = new HashSet<(int X, int Y)>();
                Dictionary<int, int> herdSizes = new Dictionary<int, int>();
                for (int index = 0; index < candidates.Length; index++)
                {
                    AnimalSpawnCandidate candidate = candidates[index];
                    Assert.That(candidate.Layer, Is.EqualTo(WorldLayer.Surface));
                    Assert.That(uniquePositions.Add((candidate.TileX, candidate.TileY)), Is.True);
                    Assert.That(candidate.AppearanceVariant, Is.InRange((byte)0, (byte)2));
                    Assert.That(candidate.SpawnSequence, Is.EqualTo((ulong)index + 1ul));

                    herdSizes.TryGetValue(candidate.HerdLeaderCandidateIndex, out int herdSize);
                    herdSizes[candidate.HerdLeaderCandidateIndex] = herdSize + 1;
                }

                foreach (KeyValuePair<int, int> herd in herdSizes)
                {
                    Assert.That(herd.Key, Is.InRange(0, candidates.Length - 1));
                    Assert.That(herd.Value, Is.InRange(3, 6));
                }
            }
            finally
            {
                DisposeCatalogs(definitionCatalog, animalCatalog);
            }
        }

        /// <summary>
        /// 6羽を一群で置けない3タイルずつの分断地形でも、3羽の群れへ縮小して目標6羽を配置することを保証します。
        /// </summary>
        [Test]
        public void SpeciesPopulation_RetriesSmallerHerdsAcrossFragmentedTiles()
        {
            AnimalSpeciesDefinitionBlob chicken = CreateChickenBlob(new DefinitionRuntimeId(1));
            using NativeArray<int> eligibleLocalTileIndices = new NativeArray<int>(
                new[]
                {
                    0,
                    1,
                    WorldGridConstants.LogicalChunkSize,
                    32 + (32 * WorldGridConstants.LogicalChunkSize),
                    33 + (32 * WorldGridConstants.LogicalChunkSize),
                    32 + (33 * WorldGridConstants.LogicalChunkSize)
                },
                Allocator.Temp);
            using NativeList<int> occupiedLocalTileIndices =
                new NativeList<int>(6, Allocator.Temp);
            using NativeList<AnimalSpawnCandidate> candidates =
                new NativeList<AnimalSpawnCandidate>(6, Allocator.Temp);
            AnimalPopulationRegionState regionState = CreateRegionState(
                0x123456789ABCDEF0UL,
                0,
                0);

            bool succeeded = AnimalPopulationGenerator.TryAppendSpeciesPopulation(
                chicken,
                6,
                eligibleLocalTileIndices,
                occupiedLocalTileIndices,
                ref regionState,
                candidates);

            Assert.That(succeeded, Is.True);
            Assert.That(candidates.Length, Is.EqualTo(6));
            Assert.That(occupiedLocalTileIndices.Length, Is.EqualTo(6));
            Assert.That(regionState.NextSpawnSequence, Is.EqualTo(7ul));

            Dictionary<int, int> herdSizes = new Dictionary<int, int>();
            for (int index = 0; index < candidates.Length; index++)
            {
                int herdLeaderCandidateIndex = candidates[index].HerdLeaderCandidateIndex;
                herdSizes.TryGetValue(herdLeaderCandidateIndex, out int herdSize);
                herdSizes[herdLeaderCandidateIndex] = herdSize + 1;
            }

            Assert.That(herdSizes.Count, Is.EqualTo(2));
            foreach (KeyValuePair<int, int> herd in herdSizes)
            {
                Assert.That(herd.Value, Is.EqualTo(3));
            }
        }

        /// <summary>
        /// 4羽を先に置くと1羽不足する5タイル＋3タイルの分断地形で、
        /// 後続配置まで確認して5羽＋3羽へ戻り、目標8羽をすべて配置することを保証します。
        /// </summary>
        [Test]
        public void SpeciesPopulation_BacktracksToHerdSplitThatFillsTarget()
        {
            AnimalSpeciesDefinitionBlob chicken = CreateChickenBlob(new DefinitionRuntimeId(1));
            using NativeArray<int> eligibleLocalTileIndices = new NativeArray<int>(
                new[]
                {
                    0,
                    1,
                    2,
                    WorldGridConstants.LogicalChunkSize,
                    1 + WorldGridConstants.LogicalChunkSize,
                    32 + (32 * WorldGridConstants.LogicalChunkSize),
                    33 + (32 * WorldGridConstants.LogicalChunkSize),
                    32 + (33 * WorldGridConstants.LogicalChunkSize)
                },
                Allocator.Temp);
            using NativeList<int> occupiedLocalTileIndices =
                new NativeList<int>(8, Allocator.Temp);
            using NativeList<AnimalSpawnCandidate> candidates =
                new NativeList<AnimalSpawnCandidate>(8, Allocator.Temp);

            // Seed 1は最初に4羽を抽選します。4羽＋3羽では1羽不足するため、5羽＋3羽への戻り探索が必要です。
            AnimalPopulationRegionState regionState = CreateRegionState(1UL, 0, 0);
            bool succeeded = AnimalPopulationGenerator.TryAppendSpeciesPopulation(
                chicken,
                8,
                eligibleLocalTileIndices,
                occupiedLocalTileIndices,
                ref regionState,
                candidates);

            Assert.That(succeeded, Is.True);
            Assert.That(candidates.Length, Is.EqualTo(8));
            Assert.That(occupiedLocalTileIndices.Length, Is.EqualTo(8));
            Assert.That(regionState.NextSpawnSequence, Is.EqualTo(9ul));

            Dictionary<int, int> herdSizes = new Dictionary<int, int>();
            for (int index = 0; index < candidates.Length; index++)
            {
                int herdLeaderCandidateIndex = candidates[index].HerdLeaderCandidateIndex;
                herdSizes.TryGetValue(herdLeaderCandidateIndex, out int herdSize);
                herdSizes[herdLeaderCandidateIndex] = herdSize + 1;
            }

            Assert.That(herdSizes.Count, Is.EqualTo(2));
            CollectionAssert.AreEquivalent(new[] { 5, 3 }, herdSizes.Values);
        }

        /// <summary>
        /// 先行群れのメンバー選択が後続群れの必須タイルを塞ぐ地形を準備し、
        /// Seed 1でもメンバーの組合せへ戻って全8羽を配置し、再実行結果も一致することを保証します。
        /// </summary>
        [Test]
        public void SpeciesPopulation_BacktracksMembersWithoutBlockingLaterHerds()
        {
            AnimalSpeciesDefinitionBlob chicken = CreateChickenBlob(new DefinitionRuntimeId(1));
            using NativeArray<int> eligibleTiles = new NativeArray<int>(
                new[]
                {
                    4 + (4 * WorldGridConstants.LogicalChunkSize),
                    8 + (10 * WorldGridConstants.LogicalChunkSize),
                    18 + (14 * WorldGridConstants.LogicalChunkSize),
                    22 + (14 * WorldGridConstants.LogicalChunkSize),
                    18 + (16 * WorldGridConstants.LogicalChunkSize),
                    14 + (18 * WorldGridConstants.LogicalChunkSize),
                    16 + (18 * WorldGridConstants.LogicalChunkSize),
                    6 + (22 * WorldGridConstants.LogicalChunkSize)
                },
                Allocator.Temp);
            using NativeList<int> occupiedTiles = new NativeList<int>(8, Allocator.Temp);
            using NativeList<int> repeatedOccupiedTiles = new NativeList<int>(8, Allocator.Temp);
            using NativeList<AnimalSpawnCandidate> candidates =
                new NativeList<AnimalSpawnCandidate>(8, Allocator.Temp);
            using NativeList<AnimalSpawnCandidate> repeatedCandidates =
                new NativeList<AnimalSpawnCandidate>(8, Allocator.Temp);
            AnimalPopulationRegionState regionState = CreateRegionState(1UL, 0, 0);
            AnimalPopulationRegionState repeatedState = regionState;

            // 中心(8,10)の3羽と中心(14,18)の5羽が成立しますが、先行群れが後続の中心を使うと全数を配置できません。
            Assert.That(AnimalPopulationGenerator.TryAppendSpeciesPopulation(
                chicken, 8, eligibleTiles, occupiedTiles, ref regionState, candidates), Is.True);
            Assert.That(AnimalPopulationGenerator.TryAppendSpeciesPopulation(
                chicken, 8, eligibleTiles, repeatedOccupiedTiles, ref repeatedState, repeatedCandidates), Is.True);

            Assert.That(candidates.Length, Is.EqualTo(8),
                "中心とサイズだけでなくメンバーを選び直し、配置可能な8羽をすべて補充する必要があります。");
            Assert.That(occupiedTiles.Length, Is.EqualTo(8));
            Assert.That(regionState.NextSpawnSequence, Is.EqualTo(9ul),
                "失敗した探索分岐が受付順を消費してはいけません。");
            Assert.That(repeatedState.RandomState, Is.EqualTo(regionState.RandomState));
            Assert.That(repeatedState.NextSpawnSequence, Is.EqualTo(regionState.NextSpawnSequence));
            AssertCandidatesEqual(candidates, repeatedCandidates);

            HashSet<(int X, int Y)> uniquePositions = new HashSet<(int X, int Y)>();
            Dictionary<int, int> herdSizes = new Dictionary<int, int>();
            for (int index = 0; index < candidates.Length; index++)
            {
                AnimalSpawnCandidate candidate = candidates[index];
                Assert.That(uniquePositions.Add((candidate.TileX, candidate.TileY)), Is.True);
                Assert.That(candidate.SpawnSequence, Is.EqualTo((ulong)index + 1ul));
                Assert.That(candidate.AppearanceVariant, Is.InRange((byte)0, (byte)2));
                Assert.That(candidate.HerdLeaderCandidateIndex, Is.InRange(0, index));
                AnimalSpawnCandidate leader = candidates[candidate.HerdLeaderCandidateIndex];
                int distance = System.Math.Max(
                    System.Math.Abs(candidate.TileX - leader.TileX),
                    System.Math.Abs(candidate.TileY - leader.TileY));
                Assert.That(distance, Is.LessThanOrEqualTo(8));
                herdSizes.TryGetValue(candidate.HerdLeaderCandidateIndex, out int herdSize);
                herdSizes[candidate.HerdLeaderCandidateIndex] = herdSize + 1;
            }

            foreach (KeyValuePair<int, int> herd in herdSizes)
            {
                Assert.That(herd.Value, Is.InRange(3, 6));
            }
        }

        /// <summary>
        /// 最小群れを作れない2タイルを含む地域で完全配置探索を打ち切り、部分補充へ戻っても、
        /// 呼び出し前の候補と占有タイルを保持し、不成立分岐の個体や受付順が漏れないことを保証します。
        /// </summary>
        [Test]
        public void SpeciesPopulation_PartialFallbackPreservesExistingCandidatesAndOccupancy()
        {
            AnimalSpeciesDefinitionBlob chicken = CreateChickenBlob(new DefinitionRuntimeId(1));
            using NativeArray<int> eligibleTiles = new NativeArray<int>(
                new[]
                {
                    0, 1, 2, 3, 4, 5, 6, 7,
                    40 + (40 * WorldGridConstants.LogicalChunkSize),
                    41 + (40 * WorldGridConstants.LogicalChunkSize)
                },
                Allocator.Temp);
            using NativeList<int> occupiedTiles = new NativeList<int>(16, Allocator.Temp);
            using NativeList<AnimalSpawnCandidate> candidates =
                new NativeList<AnimalSpawnCandidate>(16, Allocator.Temp);

            // 前地域の候補と、別種が使用済みの適格範囲外タイルを残したまま共通関数を呼び出します。
            AnimalSpawnCandidate existingCandidate = new AnimalSpawnCandidate
            {
                Layer = WorldLayer.Surface,
                TileX = -1,
                TileY = -2,
                SpawnSequence = 42
            };
            candidates.Add(existingCandidate);
            int existingOccupiedTile = WorldGridConstants.TilesPerChunk - 1;
            occupiedTiles.Add(existingOccupiedTile);
            AnimalPopulationRegionState regionState = CreateRegionState(1UL, 0, 0);
            regionState.NextSpawnSequence = 43;

            Assert.That(AnimalPopulationGenerator.TryAppendSpeciesPopulation(
                chicken, 10, eligibleTiles, occupiedTiles, ref regionState, candidates), Is.True);

            int createdCount = candidates.Length - 1;
            Assert.That(createdCount, Is.InRange(3, 8),
                "離れた2タイルには最小群れを置けないため、作れる群れだけを確定する必要があります。");
            Assert.That(candidates[0], Is.EqualTo(existingCandidate));
            Assert.That(occupiedTiles[0], Is.EqualTo(existingOccupiedTile));
            Assert.That(occupiedTiles.Length, Is.EqualTo(1 + createdCount));
            Assert.That(regionState.NextSpawnSequence, Is.EqualTo(43ul + (ulong)createdCount));

            HashSet<int> uniqueTiles = new HashSet<int>();
            Dictionary<int, int> herdSizes = new Dictionary<int, int>();
            for (int index = 1; index < candidates.Length; index++)
            {
                AnimalSpawnCandidate candidate = candidates[index];
                Assert.That(candidate.TileX, Is.InRange(0, 7));
                Assert.That(candidate.TileY, Is.Zero);
                Assert.That(uniqueTiles.Add(candidate.TileX), Is.True);
                Assert.That(candidate.SpawnSequence, Is.EqualTo(42ul + (ulong)index));
                Assert.That(candidate.HerdLeaderCandidateIndex, Is.InRange(1, index));
                herdSizes.TryGetValue(candidate.HerdLeaderCandidateIndex, out int herdSize);
                herdSizes[candidate.HerdLeaderCandidateIndex] = herdSize + 1;
            }

            foreach (KeyValuePair<int, int> herd in herdSizes)
            {
                Assert.That(herd.Value, Is.InRange(3, 6));
            }
        }

        /// <summary>
        /// 同じ地形と個体群Seedは同じ候補を返し、個体群Seedだけを変えると初期配置が変わることを保証します。
        /// </summary>
        [Test]
        public void InitialPopulation_IsRepeatableButNotFixedByTerrainAlone()
        {
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog = default;
            BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog = default;
            BuiltInTerrainBlockRuntimeIds terrainRuntimeIds = CreateTerrainRuntimeIds();
            using NativeArray<GeneratedTerrainCell> tiles = CreateUniformTerrainCells(
                BiomeId.TemperateGrassland,
                TestGroundRuntimeId);
            using NativeArray<GeneratedResourceTile> resources =
                new NativeArray<GeneratedResourceTile>(tiles.Length, Allocator.Temp);
            using NativeList<AnimalSpawnCandidate> first =
                new NativeList<AnimalSpawnCandidate>(16, Allocator.Temp);
            using NativeList<AnimalSpawnCandidate> repeated =
                new NativeList<AnimalSpawnCandidate>(16, Allocator.Temp);
            using NativeList<AnimalSpawnCandidate> differentWorld =
                new NativeList<AnimalSpawnCandidate>(16, Allocator.Temp);
            try
            {
                CreateCatalogs(out definitionCatalog, out animalCatalog);
                AnimalPopulationRegionState firstState = CreateRegionState(0x1111111111111111UL, -2, 5);
                AnimalPopulationRegionState repeatedState = CreateRegionState(0x1111111111111111UL, -2, 5);
                AnimalPopulationRegionState differentState = CreateRegionState(0x2222222222222222UL, -2, 5);

                Assert.That(
                    AnimalPopulationGenerator.TryAppendInitialPopulation(
                        animalCatalog,
                        tiles,
                        resources,
                        terrainRuntimeIds,
                        ref firstState,
                        first),
                    Is.True);
                Assert.That(
                    AnimalPopulationGenerator.TryAppendInitialPopulation(
                        animalCatalog,
                        tiles,
                        resources,
                        terrainRuntimeIds,
                        ref repeatedState,
                        repeated),
                    Is.True);
                Assert.That(
                    AnimalPopulationGenerator.TryAppendInitialPopulation(
                        animalCatalog,
                        tiles,
                        resources,
                        terrainRuntimeIds,
                        ref differentState,
                        differentWorld),
                    Is.True);

                AssertCandidatesEqual(first, repeated);
                Assert.That(HasDifferentPosition(first, differentWorld), Is.True,
                    "同じ地形でも別のAnimalPopulationSeedなら配置を固定してはいけません。");

                // 負の地域座標では左下が(-128, 320)になり、C#の0方向丸めへ依存しない範囲へ収まります。
                for (int index = 0; index < first.Length; index++)
                {
                    Assert.That(first[index].TileX, Is.InRange(-128, -65));
                    Assert.That(first[index].TileY, Is.InRange(320, 383));
                }
            }
            finally
            {
                DisposeCatalogs(definitionCatalog, animalCatalog);
            }
        }

        /// <summary>
        /// バイオームと基礎地形を別々に判定し、草原陸地だけが鶏の候補になることを保証します。
        /// </summary>
        [Test]
        public void Eligibility_RequiresBothAllowedBiomeAndGround()
        {
            AnimalSpeciesDefinitionBlob chicken = CreateChickenBlob(new DefinitionRuntimeId(1));
            BuiltInTerrainBlockRuntimeIds terrainRuntimeIds = CreateTerrainRuntimeIds();

            Assert.That(
                AnimalPopulationGenerator.IsTileEligible(
                    new GeneratedTerrainCell
                    {
                        Biome = BiomeId.TemperateGrassland,
                        GroundBlockRuntimeId = TestGroundRuntimeId
                    },
                    terrainRuntimeIds,
                    chicken),
                Is.True);
            Assert.That(
                AnimalPopulationGenerator.IsTileEligible(
                    new GeneratedTerrainCell
                    {
                        Biome = BiomeId.TemperateDeciduousForest,
                        GroundBlockRuntimeId = TestGroundRuntimeId
                    },
                    terrainRuntimeIds,
                    chicken),
                Is.False);
            Assert.That(
                AnimalPopulationGenerator.IsTileEligible(
                    new GeneratedTerrainCell
                    {
                        Biome = BiomeId.TemperateGrassland,
                        LiquidRuntimeId = terrainRuntimeIds.FreshWater
                    },
                    terrainRuntimeIds,
                    chicken),
                Is.False);
            Assert.That(
                AnimalPopulationGenerator.CalculateTargetPopulation(2048, 8),
                Is.EqualTo(4));
        }

        /// <summary>
        /// 新規Worldには地形と無関係な非0 Seedを1件だけ作り、ロード側が先に復元したSeedは上書きしないことを保証します。
        /// </summary>
        [Test]
        public void PopulationSeedInitializationSystem_CreatesNewSeedAndKeepsRestoredSeed()
        {
            using World newWorld = new World("Animal Population New Seed Test");
            SystemHandle newWorldSystem =
                newWorld.GetOrCreateSystem<AnimalPopulationSeedInitializationSystem>();
            newWorldSystem.Update(newWorld.Unmanaged);
            EntityQuery newSeedQuery = newWorld.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AnimalPopulationSeed>());
            try
            {
                Assert.That(newSeedQuery.CalculateEntityCount(), Is.EqualTo(1));
                Assert.That(newSeedQuery.GetSingleton<AnimalPopulationSeed>().IsValid, Is.True);
            }
            finally
            {
                newSeedQuery.Dispose();
            }

            using World restoredWorld = new World("Animal Population Restored Seed Test");
            Entity restoredSeedEntity = restoredWorld.EntityManager.CreateEntity(
                typeof(AnimalPopulationSeed));
            restoredWorld.EntityManager.SetComponentData(
                restoredSeedEntity,
                new AnimalPopulationSeed { Value = 0x0102030405060708UL });
            SystemHandle restoredWorldSystem =
                restoredWorld.GetOrCreateSystem<AnimalPopulationSeedInitializationSystem>();
            restoredWorldSystem.Update(restoredWorld.Unmanaged);
            EntityQuery restoredSeedQuery = restoredWorld.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AnimalPopulationSeed>());
            try
            {
                Assert.That(restoredSeedQuery.CalculateEntityCount(), Is.EqualTo(1));
                Assert.That(
                    restoredSeedQuery.GetSingleton<AnimalPopulationSeed>().Value,
                    Is.EqualTo(0x0102030405060708UL));
            }
            finally
            {
                restoredSeedQuery.Dispose();
            }
        }

        /// <summary>
        /// 2地域のチャンクEntity作成順を逆転しても、地域座標順の永続IDと個体状態が同じになることを保証します。
        /// </summary>
        [Test]
        public void InitializationSystem_AssignsIdsByRegionCoordinateNotEntityOrder()
        {
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog = default;
            BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog = default;
            try
            {
                CreateCatalogs(out definitionCatalog, out animalCatalog);
                List<AnimalRecord> forward = RunInitializationSystem(
                    animalCatalog,
                    new[] { (-1, 0), (2, -3) });
                List<AnimalRecord> reverse = RunInitializationSystem(
                    animalCatalog,
                    new[] { (2, -3), (-1, 0) });

                Assert.That(reverse.Count, Is.EqualTo(forward.Count));
                for (int index = 0; index < forward.Count; index++)
                {
                    Assert.That(reverse[index], Is.EqualTo(forward[index]));
                }
            }
            finally
            {
                DisposeCatalogs(definitionCatalog, animalCatalog);
            }
        }

        /// <summary>
        /// 同時生成Jobの一方だけを先に完了させても、公開バッチ、ID、群れID、初期化Tick、ハッシュが変わらないことを保証します。
        /// </summary>
        /// <param name="isPaused">停止中フレームの一括公開と再開後の初期化も確認する場合はtrueです。</param>
        [TestCase(false)]
        [TestCase(true)]
        public void GenerationBoundary_KeepsPopulationIndependentOfWorkerCompletion(bool isPaused)
        {
            BlobAssetReference<DefinitionCatalogBlob> definitions = default;
            BlobAssetReference<AnimalSpeciesCatalogBlob> animals = default;
            try
            {
                CreateCatalogs(out definitions, out animals);
                SimulationStateHash firstEarly = RunGenerationBoundary(animals, 0, isPaused);
                SimulationStateHash secondEarly = RunGenerationBoundary(animals, 1, isPaused);
                SimulationStateHash neitherForced = RunGenerationBoundary(animals, -1, isPaused);
                Assert.That(secondEarly, Is.EqualTo(firstEarly));
                Assert.That(neitherForced, Is.EqualTo(firstEarly));
            }
            finally
            {
                DisposeCatalogs(definitions, animals);
            }
        }

        /// <summary>
        /// 同じ4要求を1・2・4描画フレームへ分け、非同期延期を含む実物のRate Managerで2 Tick後の保存状態が一致することを保証します。
        /// </summary>
        [Test]
        public void GenerationBoundary_MatchesAcrossFramePartitionsAndRequestCreationOrder()
        {
            CreateCatalogs(out BlobAssetReference<DefinitionCatalogBlob> definitions,
                out BlobAssetReference<AnimalSpeciesCatalogBlob> animals);
            try
            {
                SimulationStateHash singleFrame = RunFramePartitionedGeneration(animals, new[] { 0.2f }, false);
                Assert.That(RunFramePartitionedGeneration(animals, new[] { 0.1f, 0.1f }, false), Is.EqualTo(singleFrame));
                Assert.That(RunFramePartitionedGeneration(animals, new[] { 0.05f, 0.05f, 0.05f, 0.05f }, true),
                    Is.EqualTo(singleFrame));
            }
            finally
            {
                DisposeCatalogs(definitions, animals);
            }
        }

        /// <summary>
        /// 個体群Worldの通常フレームでは生成Jobを先行投入する一方、固定Tick入口までは結果を公開しないことを保証します。
        /// </summary>
        [Test]
        public void GenerationSystem_PreSchedulesRunningWorldAndPublishesOnlyAtFixedTickBoundary()
        {
            BlobAssetReference<DefinitionCatalogBlob> definitions = default;
            BlobAssetReference<AnimalSpeciesCatalogBlob> animals = default;
            try
            {
                CreateCatalogs(out definitions, out animals);
                using TerrainGenerationTestCatalogSet terrainCatalogs =
                    TerrainGenerationTestCatalogSet.Create();
                using World world = new World("Chunk generation pre-schedule boundary test");
                EntityManager manager = world.EntityManager;
                PrepareWorldSingletons(world, animals);
                Entity configuration = manager.CreateEntity();
                manager.AddComponentData(configuration, WorldGenerationSettings.CreateDefault(123456789UL, WorldGenerationSettings.CurrentGeneratorVersion));
                manager.AddComponentData(configuration, BiomeDefinitionCatalog.CreateDefault());
                manager.AddComponentData(configuration, ResourceGenerationSettings.CreateDefault());
                manager.AddComponentData(configuration, ChunkStreamingSettings.CreateDefault());
                manager.AddComponentData(configuration, new SimulationPauseState());
                AddTerrainCatalogComponents(manager, configuration, terrainCatalogs);

                Entity chunk = CreateReadyChunk(manager, -4, -4);
                manager.SetComponentData(chunk, new ChunkGenerationStatus { Phase = ChunkGenerationPhase.Queued });
                manager.AddComponent<ChunkGenerationRequest>(chunk);
                manager.GetBuffer<GeneratedTile>(chunk).Clear();
                manager.GetBuffer<GeneratedResourceTile>(chunk).Clear();

                SystemHandle generation = world.GetOrCreateSystem<ChunkGenerationSystem>();
                SystemHandle boundary = world.GetOrCreateSystem<ChunkGenerationFixedTickPublicationSystem>();

                // 通常フレームでは重い計算をWorkerへ渡しますが、Simulationが参照するBufferはまだ空のままにします。
                generation.Update(world.Unmanaged);
                Assert.That(
                    manager.GetComponentData<ChunkGenerationStatus>(chunk).Phase,
                    Is.EqualTo(ChunkGenerationPhase.GeneratingData));
                Assert.That(manager.IsComponentEnabled<ChunkGenerationRequest>(chunk), Is.False);
                Assert.That(manager.GetBuffer<GeneratedTile>(chunk).Length, Is.Zero);
                Assert.That(manager.GetBuffer<GeneratedTerrainCell>(chunk).Length, Is.Zero);
                Assert.That(manager.GetBuffer<GeneratedResourceTile>(chunk).Length, Is.Zero);
                Assert.That(manager.GetBuffer<GeneratedHydrologyCell>(chunk).Length, Is.Zero);
                Assert.That(manager.GetBuffer<GeneratedSurfaceWaterTile>(chunk).Length, Is.Zero);
                Assert.That(manager.GetBuffer<GeneratedHydrologyTile>(chunk).Length, Is.Zero);

                // Worker完了をテスト側で確定し、固定Tick入口が待機せず一括公開できる状態を作ります。
                CompleteAllGenerationSlots(world, generation);

                // 固定Tick入口は完成済みHandleだけを確定し、地形・資源・水文・表流水を同じ境界で可視化可能にします。
                boundary.Update(world.Unmanaged);
                Assert.That(
                    manager.GetComponentData<ChunkGenerationStatus>(chunk).Phase,
                    Is.EqualTo(ChunkGenerationPhase.ReadyToApply));
                Assert.That(manager.GetBuffer<GeneratedTile>(chunk).Length,
                    Is.EqualTo(WorldGridConstants.TilesPerChunk));
                Assert.That(manager.GetBuffer<GeneratedTerrainCell>(chunk).Length,
                    Is.EqualTo(WorldGridConstants.TilesPerChunk));
                Assert.That(manager.GetBuffer<GeneratedResourceTile>(chunk).Length,
                    Is.EqualTo(WorldGridConstants.TilesPerChunk));
                Assert.That(manager.GetBuffer<GeneratedHydrologyCell>(chunk).Length,
                    Is.EqualTo(DeterministicHydrologyGenerator.CellsPerChunk));
                Assert.That(manager.GetBuffer<GeneratedSurfaceWaterTile>(chunk).Length,
                    Is.EqualTo(WorldGridConstants.TilesPerChunk));
                Assert.That(manager.GetBuffer<GeneratedHydrologyTile>(chunk).Length,
                    Is.EqualTo(WorldGridConstants.TilesPerChunk));
            }
            finally
            {
                DisposeCatalogs(definitions, animals);
            }
        }

        /// <summary>最初から同じ4要求を作り、描画フレーム更新と実物の固定Tick Groupを通したハッシュを返します。</summary>
        /// <param name="catalog">各比較Worldで共通に使う動物種Blobです。</param>
        /// <param name="frameDeltas">最初に与える合計0.2秒の描画フレーム幅です。延期時は2 Tick到達まで通常フレームを追加します。</param>
        /// <param name="reverseCreation">Entity作成順を逆転し、Query順ではなく座標順でバッチを選ぶか確認します。</param>
        /// <returns>初期化Tick・個体ID・群れID・抽選状態・次回IDを含む保存状態ハッシュです。</returns>
        private static SimulationStateHash RunFramePartitionedGeneration(
            BlobAssetReference<AnimalSpeciesCatalogBlob> catalog, float[] frameDeltas, bool reverseCreation)
        {
            using TerrainGenerationTestCatalogSet terrainCatalogs =
                TerrainGenerationTestCatalogSet.Create();
            using World world = new World("Animal frame partition regression");
            EntityManager manager = world.EntityManager;
            PrepareWorldSingletons(world, catalog);
            using (EntityQuery tick = manager.CreateEntityQuery(typeof(SimulationTickState)))
                tick.SetSingleton(new SimulationTickState { CurrentTick = 0 });
            Entity configuration = manager.CreateEntity();
            manager.AddComponentData(configuration, WorldGenerationSettings.CreateDefault(123456789UL, WorldGenerationSettings.CurrentGeneratorVersion));
            manager.AddComponentData(configuration, BiomeDefinitionCatalog.CreateDefault());
            manager.AddComponentData(configuration, ResourceGenerationSettings.CreateDefault());
            manager.AddComponentData(configuration, ChunkStreamingSettings.CreateDefault());
            manager.AddComponentData(configuration, FixedTickConfiguration.CreateDefault());
            manager.AddComponentData(configuration, new SimulationPauseState());
            AddTerrainCatalogComponents(manager, configuration, terrainCatalogs);
            int[] xs = { -4, -3, 1, 2 };
            for (int i = 0; i < xs.Length; i++)
            {
                Entity chunk = CreateReadyChunk(manager, xs[reverseCreation ? xs.Length - 1 - i : i], -4);
                manager.SetComponentData(chunk, new ChunkGenerationStatus { Phase = ChunkGenerationPhase.Queued });
                manager.AddComponent<ChunkGenerationRequest>(chunk);
                manager.GetBuffer<GeneratedTile>(chunk).Clear();
                manager.GetBuffer<GeneratedResourceTile>(chunk).Clear();
            }

            // Rate Managerを含む本番Group構成で実行し、公開Systemだけを直接呼ぶテストの見落としを防ぎます。
            var root = world.GetOrCreateSystemManaged<FarmwareSimulationSystemGroup>();
            var fixedTicks = world.GetOrCreateSystemManaged<FarmwareFixedTickSimulationSystemGroup>();
            var gameplay = world.GetOrCreateSystemManaged<FarmwareFixedTickGameplaySystemGroup>();
            SystemHandle generation = world.GetOrCreateSystem<ChunkGenerationSystem>();
            root.AddSystemToUpdateList(generation);
            root.AddSystemToUpdateList(fixedTicks);
            fixedTicks.AddSystemToUpdateList(world.GetOrCreateSystem<SimulationTickAdvanceSystem>());
            fixedTicks.AddSystemToUpdateList(world.GetOrCreateSystem<ChunkGenerationFixedTickPublicationSystem>());
            fixedTicks.AddSystemToUpdateList(gameplay);
            gameplay.AddSystemToUpdateList(world.GetOrCreateSystem<AnimalPopulationInitializationSystem>());
            root.SortSystems(); fixedTicks.SortSystems(); gameplay.SortSystems();
            foreach (float delta in frameDeltas)
            {
                UpdateRootGroup(world, root, delta);

                // 実時間sleepに依存せずWorker完了だけを確定し、次の描画フレームで延期Tickを再開させます。
                CompleteAllGenerationSlots(world, generation);
                UpdateRootGroup(world, root, 0f);
                CompleteAllGenerationSlots(world, generation);
            }

            // 0.2秒を単一フレームで渡した場合、生成中に2件目以降の追いつきTickを意図的に捨てます。
            // 比較対象を同じ論理Tickへそろえるため、通常の0.1秒フレームを追加してTick 2まで進めます。
            int safetyFrameCount = 0;
            while (GetSimulationTick(manager) < 2ul && safetyFrameCount < 8)
            {
                UpdateRootGroup(world, root, 0.1f);
                CompleteAllGenerationSlots(world, generation);
                UpdateRootGroup(world, root, 0f);
                CompleteAllGenerationSlots(world, generation);
                safetyFrameCount++;
            }

            Assert.That(GetSimulationTick(manager), Is.EqualTo(2ul), "非同期生成を完了させれば有限フレーム内にTick 2へ到達します。");
            Assert.That(CountRegionStates(manager), Is.EqualTo(4), "追いつき2 Tickでも各2地域を処理します。");
            using EntityQuery regions = manager.CreateEntityQuery(typeof(AnimalPopulationRegionState));
            using NativeArray<AnimalPopulationRegionState> states = regions.ToComponentDataArray<AnimalPopulationRegionState>(Allocator.Temp);
            foreach (AnimalPopulationRegionState region in states)
                Assert.That(region.InitializedAtTick, Is.EqualTo(region.RegionX < 0 ? 1ul : 2ul));
            var builder = new SimulationStateHashBuilder(SimulationStateHashSchema.CurrentVersion);
            builder.BeginSection(AnimalSpeciesStateHasher.SpeciesDefinitionSectionId);
            builder.WriteUInt64(GetAllocatorState(manager).NextId);
            Assert.That(AnimalPopulationStateHasher.TryAppendTo(manager, ref builder, out _), Is.True);
            return builder.Complete();
        }

        /// <summary>公開境界がTick加算後・Command前に接続され、同TickのGameplayより先に動くことを保証します。</summary>
        [Test]
        public void GenerationBoundary_RunsAtFixedTickEntry()
        {
            System.Type boundary = typeof(ChunkGenerationFixedTickPublicationSystem);
            UpdateInGroupAttribute parent = boundary.GetCustomAttribute<UpdateInGroupAttribute>();
            Assert.That(parent.GroupType, Is.EqualTo(typeof(FarmwareFixedTickSimulationSystemGroup)));
            Assert.That(parent.OrderFirst, Is.True);
            Assert.That(boundary.GetCustomAttribute<UpdateAfterAttribute>().SystemType,
                Is.EqualTo(typeof(SimulationTickAdvanceSystem)));
            Assert.That(boundary.GetCustomAttribute<UpdateBeforeAttribute>().SystemType,
                Is.EqualTo(typeof(FarmwareCommandProcessingSystemGroup)));
        }

        /// <summary>
        /// 同じ2バッチをTick 12・13で生成し、Worker完了順だけを変えたWorldの保存対象ハッシュを返します。
        /// 地形Jobは実物を使い、壁時計のsleepではなく指定HandleのCompleteで早期完了条件を作ります。
        /// </summary>
        /// <param name="catalog">各Worldで共通の動物種定義です。</param>
        /// <param name="earlySlot">フレーム公開判定前に完了させる枠です。-1は強制完了しません。</param>
        /// <param name="isPaused">最初のバッチを停止中の表示更新で公開するかどうかです。</param>
        /// <returns>地域状態、全個体、次回永続IDを含む比較ハッシュです。</returns>
        private static SimulationStateHash RunGenerationBoundary(
            BlobAssetReference<AnimalSpeciesCatalogBlob> catalog, int earlySlot, bool isPaused)
        {
            using TerrainGenerationTestCatalogSet terrainCatalogs =
                TerrainGenerationTestCatalogSet.Create();
            using World world = new World("Animal deterministic generation boundary test");
            EntityManager manager = world.EntityManager;
            PrepareWorldSingletons(world, catalog);
            Entity configuration = manager.CreateEntity();
            manager.AddComponentData(configuration, WorldGenerationSettings.CreateDefault(123456789UL, WorldGenerationSettings.CurrentGeneratorVersion));
            manager.AddComponentData(configuration, BiomeDefinitionCatalog.CreateDefault());
            manager.AddComponentData(configuration, ResourceGenerationSettings.CreateDefault());
            manager.AddComponentData(configuration, ChunkStreamingSettings.CreateDefault());
            manager.AddComponentData(configuration, new SimulationPauseState { Value = isPaused ? (byte)1 : (byte)0 });
            AddTerrainCatalogComponents(manager, configuration, terrainCatalogs);
            SystemHandle generation = world.GetOrCreateSystem<ChunkGenerationSystem>();
            SystemHandle boundary = world.GetOrCreateSystem<ChunkGenerationFixedTickPublicationSystem>();
            SystemHandle population = world.GetOrCreateSystem<AnimalPopulationInitializationSystem>();
            // この固定Seedで目標が3羽以上になる草原を含む4地域です。2回のTickへ分けて地域間の順序も確認します。
            (int X, int Y)[] coordinates = { (-4, -4), (-3, -4), (1, -4), (2, -4) };
            for (int batchIndex = 0; batchIndex < 2; batchIndex++)
            {
                (int X, int Y) firstCoordinate = coordinates[batchIndex * 2];
                (int X, int Y) secondCoordinate = coordinates[batchIndex * 2 + 1];
                Entity first = CreateReadyChunk(manager, firstCoordinate.X, firstCoordinate.Y);
                Entity second = CreateReadyChunk(manager, secondCoordinate.X, secondCoordinate.Y);
                foreach (Entity chunk in new[] { first, second })
                {
                    manager.SetComponentData(chunk, new ChunkGenerationStatus { Phase = ChunkGenerationPhase.Queued });
                    manager.AddComponent<ChunkGenerationRequest>(chunk);
                    manager.GetBuffer<GeneratedTile>(chunk).Clear();
                    manager.GetBuffer<GeneratedResourceTile>(chunk).Clear();
                }

                generation.Update(world.Unmanaged);
                if (!isPaused)
                {
                    // 本番Tick境界と同じ投入APIを使い、待ち合わせ前の実物Job完了順だけを操作します。
                    ref ChunkGenerationSystem generator = ref world.Unmanaged.GetUnsafeSystemRef<ChunkGenerationSystem>(generation);
                    generator.ScheduleBatch(manager, WorldGenerationSettings.CreateDefault(123456789UL, WorldGenerationSettings.CurrentGeneratorVersion),
                        BiomeDefinitionCatalog.CreateDefault(),
                        ResourceGenerationSettings.CreateDefault(),
                        terrainCatalogs.PaletteCatalog,
                        terrainCatalogs.RuntimeIds,
                        2);
                }
                ulong nextIdBeforePublication = GetAllocatorState(manager).NextId;
                if (earlySlot >= 0)
                {
                    CompleteGenerationSlot(world, generation, earlySlot);
                }

                // 固定Tickへ入る前に通常の描画フレーム更新があっても、一方だけが公開されてはいけません。
                generation.Update(world.Unmanaged);
                ChunkGenerationPhase firstPhase = manager.GetComponentData<ChunkGenerationStatus>(first).Phase;
                ChunkGenerationPhase secondPhase = manager.GetComponentData<ChunkGenerationStatus>(second).Phase;
                Assert.That(secondPhase, Is.EqualTo(firstPhase), "同じ有限バッチの一部だけを先に公開してはいけません。");
                Assert.That(CountRegionStates(manager), Is.EqualTo(batchIndex * 2));
                Assert.That(GetAllocatorState(manager).NextId, Is.EqualTo(nextIdBeforePublication));

                // 固定Tick境界は未完了Jobを待たないため、残りのWorker処理をテスト側で完了させます。
                CompleteAllGenerationSlots(world, generation);
                if (isPaused)
                {
                    // 停止中はTickが来ないので、完成済みバッチを次の描画更新で公開します。
                    generation.Update(world.Unmanaged);
                    Assert.That(manager.GetComponentData<ChunkGenerationStatus>(first).Phase,
                        Is.EqualTo(ChunkGenerationPhase.ReadyToApply));
                    Assert.That(manager.GetComponentData<ChunkGenerationStatus>(second).Phase,
                        Is.EqualTo(ChunkGenerationPhase.ReadyToApply));
                }

                // 停止中の公開自体はTickもIDも進めず、再開した固定Tickから初めて個体群を確定します。
                manager.SetComponentData(configuration, new SimulationPauseState { Value = 0 });
                using EntityQuery ticks = manager.CreateEntityQuery(ComponentType.ReadWrite<SimulationTickState>());
                ticks.SetSingleton(new SimulationTickState { CurrentTick = (ulong)(12 + batchIndex) });
                boundary.Update(world.Unmanaged);
                population.Update(world.Unmanaged);
                using EntityQuery regions = manager.CreateEntityQuery(ComponentType.ReadOnly<AnimalPopulationRegionState>());
                using NativeArray<AnimalPopulationRegionState> states =
                    regions.ToComponentDataArray<AnimalPopulationRegionState>(Allocator.Temp);
                foreach (AnimalPopulationRegionState region in states)
                {
                    bool isNew = (region.RegionX == firstCoordinate.X && region.RegionY == firstCoordinate.Y)
                        || (region.RegionX == secondCoordinate.X && region.RegionY == secondCoordinate.Y);
                    if (isNew)
                    {
                        Assert.That(region.InitializedAtTick, Is.EqualTo((ulong)(12 + batchIndex)));
                    }
                }

                manager.SetComponentData(configuration, new SimulationPauseState { Value = isPaused ? (byte)1 : (byte)0 });
            }

            Assert.That(CountRegionStates(manager), Is.EqualTo(4));
            Assert.That(CountAnimals(manager), Is.GreaterThan(0), "IDと群れIDも比較できる実際の個体群が必要です。");
            SimulationStateHashBuilder builder = new SimulationStateHashBuilder(SimulationStateHashSchema.CurrentVersion);
            builder.BeginSection(AnimalSpeciesStateHasher.SpeciesDefinitionSectionId);
            builder.WriteUInt64(GetAllocatorState(manager).NextId);
            Assert.That(AnimalPopulationStateHasher.TryAppendTo(manager, ref builder, out _), Is.True);
            return builder.Complete();
        }

        /// <summary>
        /// テストで指定した実物Jobを先に完了させます。公開や配列内容は変更せず、完了タイミングだけを変えます。
        /// </summary>
        /// <param name="world">生成Jobを所有するテストWorldです。</param>
        /// <param name="generation">このWorldの生成Systemです。</param>
        /// <param name="slotIndex">先に完了させる同時生成枠の添字です。</param>
        private static void CompleteGenerationSlot(World world, SystemHandle generation, int slotIndex)
        {
            // 本番APIへテスト用待機設定を足さないため、所有情報の読取だけに反射を限定します。
            object generator = world.Unmanaged.GetUnsafeSystemRef<ChunkGenerationSystem>(generation);
            object slots = typeof(ChunkGenerationSystem).GetField("slots", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(generator);
            object slot = slots.GetType().GetProperty("Item").GetValue(slots, new object[] { slotIndex });
            JobHandle handle = (JobHandle)slot.GetType().GetField("Handle").GetValue(slot);
            handle.Complete();
        }

        /// <summary>
        /// 生成Systemが所有する全作業枠のJobを完了させ、次の更新がMain Thread待機なしで公開できる状態を作ります。
        /// </summary>
        /// <param name="world">生成Jobを所有するテストWorldです。</param>
        /// <param name="generation">このWorldの生成Systemです。</param>
        private static void CompleteAllGenerationSlots(World world, SystemHandle generation)
        {
            // 本番コードへテスト専用同期APIを追加しないため、既存の作業枠数だけを反射で読み取ります。
            object generator = world.Unmanaged.GetUnsafeSystemRef<ChunkGenerationSystem>(generation);
            object slots = typeof(ChunkGenerationSystem).GetField("slots", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(generator);
            int slotCount = (int)slots.GetType().GetProperty("Length").GetValue(slots);
            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                CompleteGenerationSlot(world, generation, slotIndex);
            }
        }

        /// <summary>描画フレーム相当の時間を一時設定し、本番Root Groupを1回更新します。</summary>
        /// <param name="world">時間を所有するテストWorldです。</param>
        /// <param name="root">通常フレームと固定Tickを順に更新するRoot Groupです。</param>
        /// <param name="frameDeltaTime">今回だけ加える描画時間です。秒単位です。</param>
        private static void UpdateRootGroup(
            World world,
            FarmwareSimulationSystemGroup root,
            float frameDeltaTime)
        {
            world.PushTime(new TimeData(world.Time.ElapsedTime + frameDeltaTime, frameDeltaTime));
            try
            {
                root.Update();
            }
            finally
            {
                world.PopTime();
            }
        }

        /// <summary>現在のSimulationTickState Singletonから論理Tick番号を読み取ります。</summary>
        /// <param name="manager">現在Tickを所有するテストWorldのEntityManagerです。</param>
        /// <returns>固定Tick Groupが開始済みのTick番号です。</returns>
        private static ulong GetSimulationTick(EntityManager manager)
        {
            using EntityQuery tickQuery = manager.CreateEntityQuery(
                ComponentType.ReadOnly<SimulationTickState>());
            return tickQuery.GetSingleton<SimulationTickState>().CurrentTick;
        }

        /// <summary>
        /// 初期化済み地域の同じ地形チャンクを再度Systemへ見せても、個体とIDを追加発行しないことを保証します。
        /// </summary>
        [Test]
        public void InitializationSystem_DoesNotPopulateInitializedRegionAgain()
        {
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog = default;
            BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog = default;
            using World world = new World("Animal Population Repeat Test");
            try
            {
                CreateCatalogs(out definitionCatalog, out animalCatalog);
                PrepareWorldSingletons(world, animalCatalog);
                Entity firstChunk = CreateReadyChunk(world.EntityManager, 0, 0);
                SystemHandle system = world.GetOrCreateSystem<AnimalPopulationInitializationSystem>();

                system.Update(world.Unmanaged);
                int firstAnimalCount = CountAnimals(world.EntityManager);
                PersistentIdAllocatorState firstAllocator = GetAllocatorState(world.EntityManager);
                world.EntityManager.DestroyEntity(firstChunk);
                CreateReadyChunk(world.EntityManager, 0, 0);

                system.Update(world.Unmanaged);

                Assert.That(CountAnimals(world.EntityManager), Is.EqualTo(firstAnimalCount));
                Assert.That(
                    GetAllocatorState(world.EntityManager).NextId,
                    Is.EqualTo(firstAllocator.NextId));
                Assert.That(CountRegionStates(world.EntityManager), Is.EqualTo(1));
            }
            finally
            {
                DisposeCatalogs(definitionCatalog, animalCatalog);
            }
        }

        /// <summary>
        /// 固定Tick停止中にPresentationが完成チャンクをActiveまたはInactiveへ進めても、
        /// 再開後の初回Tickで地域状態と動物を作り、表示状態をSimulationの生存条件にしないことを保証します。
        /// </summary>
        /// <param name="completedPhase">Presentationによる反映後を再現する完成チャンク状態です。</param>
        [TestCase(ChunkGenerationPhase.Active)]
        [TestCase(ChunkGenerationPhase.Inactive)]
        public void InitializationSystem_PopulatesCompletedChunkAfterPresentationTransition(
            ChunkGenerationPhase completedPhase)
        {
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog = default;
            BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog = default;
            using World world = new World("Animal Population Presentation Transition Test");
            try
            {
                CreateCatalogs(out definitionCatalog, out animalCatalog);
                PrepareWorldSingletons(world, animalCatalog);
                Entity chunkEntity = CreateReadyChunk(world.EntityManager, 0, 0);

                // 固定Tickが止まっている間に表示処理だけが完了した状況を、System実行前の状態変更で再現します。
                ChunkGenerationStatus status =
                    world.EntityManager.GetComponentData<ChunkGenerationStatus>(chunkEntity);
                status.Phase = completedPhase;
                world.EntityManager.SetComponentData(chunkEntity, status);

                SystemHandle system =
                    world.GetOrCreateSystem<AnimalPopulationInitializationSystem>();
                system.Update(world.Unmanaged);

                Assert.That(CountRegionStates(world.EntityManager), Is.EqualTo(1));
                Assert.That(CountAnimals(world.EntityManager), Is.EqualTo(8));
                Assert.That(
                    world.EntityManager.HasComponent<AnimalPopulationRegionProcessed>(chunkEntity),
                    Is.True);
            }
            finally
            {
                DisposeCatalogs(definitionCatalog, animalCatalog);
            }
        }

        /// <summary>
        /// 全面が有効地形でも8セル以外を資源で予約し、目標密度を保ちながら空きセルだけへ8羽を生成することを保証します。
        /// </summary>
        /// <param name="resourceType">動物より先にセルを予約する基礎資源の種類です。</param>
        [TestCase(GeneratedResourceType.Tree)]
        [TestCase(GeneratedResourceType.Stone)]
        [TestCase(GeneratedResourceType.Sand)]
        public void InitializationSystem_RespectsResourceReservations(GeneratedResourceType resourceType)
        {
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog = default;
            BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog = default;
            using World world = new World("Animal resource reservation test");
            try
            {
                CreateCatalogs(out definitionCatalog, out animalCatalog);
                PrepareWorldSingletons(world, animalCatalog);
                Entity chunk = CreateReadyChunk(world.EntityManager, 0, 0);
                DynamicBuffer<GeneratedResourceTile> resources =
                    world.EntityManager.GetBuffer<GeneratedResourceTile>(chunk);
                for (int index = 8; index < resources.Length; index++)
                {
                    resources[index] = new GeneratedResourceTile { Type = resourceType };
                }

                SystemHandle system = world.GetOrCreateSystem<AnimalPopulationInitializationSystem>();
                system.Update(world.Unmanaged);

                Assert.That(CountRegionStates(world.EntityManager), Is.EqualTo(1));
                Assert.That(CountAnimals(world.EntityManager), Is.EqualTo(8),
                    "密度の基準は4096セルの地形面積であり、資源を除いた8セルへ全8羽を置く必要があります。");
                using EntityQuery animalQuery = world.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<WildAnimal>(), ComponentType.ReadOnly<AnimalTilePosition>());
                using NativeArray<AnimalTilePosition> positions =
                    animalQuery.ToComponentDataArray<AnimalTilePosition>(Allocator.Temp);
                foreach (AnimalTilePosition position in positions)
                {
                    Assert.That(position.TileX, Is.InRange(0, 7));
                    Assert.That(position.TileY, Is.Zero);
                }

                // Entity作成後はBuffer参照を取り直し、動物生成が優先レイヤーの資源を書き換えていないことも確認します。
                resources = world.EntityManager.GetBuffer<GeneratedResourceTile>(chunk);
                Assert.That(resources[8].Type, Is.EqualTo(resourceType));
            }
            finally
            {
                DisposeCatalogs(definitionCatalog, animalCatalog);
            }
        }

        /// <summary>
        /// 資源Bufferが欠落・未完成なら初期化とID発行を保留し、完成後のTickで初めて生成することを保証します。
        /// </summary>
        /// <param name="resourceCount">-1はBufferなし、4095は最後のセルが未完成の状態です。</param>
        [TestCase(-1)]
        [TestCase(4095)]
        public void InitializationSystem_WaitsForCompleteResources(int resourceCount)
        {
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog = default;
            BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog = default;
            using World world = new World("Animal incomplete resources test");
            try
            {
                CreateCatalogs(out definitionCatalog, out animalCatalog);
                PrepareWorldSingletons(world, animalCatalog);
                Entity chunk = CreateReadyChunk(world.EntityManager, 0, 0);
                world.EntityManager.RemoveComponent<GeneratedResourceTile>(chunk);
                if (resourceCount >= 0)
                {
                    world.EntityManager.AddBuffer<GeneratedResourceTile>(chunk).ResizeUninitialized(resourceCount);
                }

                PersistentIdAllocatorState allocatorBefore = GetAllocatorState(world.EntityManager);
                SystemHandle system = world.GetOrCreateSystem<AnimalPopulationInitializationSystem>();
                system.Update(world.Unmanaged);
                Assert.That(CountRegionStates(world.EntityManager), Is.Zero);
                Assert.That(CountAnimals(world.EntityManager), Is.Zero);
                Assert.That(GetAllocatorState(world.EntityManager).NextId, Is.EqualTo(allocatorBefore.NextId));
                Assert.That(world.EntityManager.HasComponent<AnimalPopulationRegionProcessed>(chunk), Is.False);

                // 未完成データを全件資源なしへ完成させ、前のTickが初期化済み状態を残していないことを確認します。
                if (!world.EntityManager.HasBuffer<GeneratedResourceTile>(chunk))
                {
                    world.EntityManager.AddBuffer<GeneratedResourceTile>(chunk);
                }

                DynamicBuffer<GeneratedResourceTile> resources = world.EntityManager.GetBuffer<GeneratedResourceTile>(chunk);
                resources.ResizeUninitialized(WorldGridConstants.TilesPerChunk);
                for (int index = 0; index < resources.Length; index++)
                {
                    resources[index] = default;
                }

                system.Update(world.Unmanaged);
                Assert.That(CountRegionStates(world.EntityManager), Is.EqualTo(1));
                Assert.That(CountAnimals(world.EntityManager), Is.EqualTo(8));
            }
            finally
            {
                DisposeCatalogs(definitionCatalog, animalCatalog);
            }
        }

        /// <summary>指定Seedと地域座標からテスト用初期地域状態を作ります。</summary>
        private static AnimalPopulationRegionState CreateRegionState(
            ulong seed,
            int regionX,
            int regionY)
        {
            return AnimalPopulationGenerator.CreateInitialRegionState(
                new AnimalPopulationSeed { Value = seed },
                regionX,
                regionY);
        }

        /// <summary>全面を同じBiomeとGroundBlockで満たした4096セル配列を準備します。</summary>
        /// <param name="biome">全セルへ設定するBiomeです。</param>
        /// <param name="groundRuntimeId">全セルへ設定するGroundBlock Runtime IDです。</param>
        /// <returns>呼び出し側が破棄する自然生成Blockセル配列です。</returns>
        private static NativeArray<GeneratedTerrainCell> CreateUniformTerrainCells(
            BiomeId biome,
            DefinitionRuntimeId groundRuntimeId)
        {
            NativeArray<GeneratedTerrainCell> tiles = new NativeArray<GeneratedTerrainCell>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Temp);
            for (int index = 0; index < tiles.Length; index++)
            {
                tiles[index] = new GeneratedTerrainCell
                {
                    Biome = biome,
                    GroundBlockRuntimeId = groundRuntimeId
                };
            }

            return tiles;
        }

        /// <summary>純粋な動物候補テストで使う、有効な仮Liquid Runtime ID一式を作ります。</summary>
        /// <returns>淡水、海水、マグマを一意に識別できるIDです。</returns>
        private static BuiltInTerrainBlockRuntimeIds CreateTerrainRuntimeIds()
        {
            return new BuiltInTerrainBlockRuntimeIds
            {
                FreshWater = new DefinitionRuntimeId(3),
                SaltWater = new DefinitionRuntimeId(4),
                Magma = new DefinitionRuntimeId(5)
            };
        }

        /// <summary>純粋な適格判定テストへ渡す、鶏の検証済み相当Blob値を作ります。</summary>
        private static AnimalSpeciesDefinitionBlob CreateChickenBlob(
            DefinitionRuntimeId runtimeId)
        {
            return new AnimalSpeciesDefinitionBlob
            {
                SpeciesRuntimeId = runtimeId,
                AllowedBiomes = AnimalBiomeMask.TemperateGrassland,
                AllowedGrounds = AnimalGroundMask.Land,
                MinimumHerdSize = 3,
                MaximumHerdSize = 6,
                TargetPopulationPer4096EligibleTiles = 8,
                MinimumIdleTicks = 20,
                MaximumIdleTicks = 80,
                MovementStepIntervalTicks = 5,
                MaximumWanderDistanceInTiles = 8,
                AppearanceVariantCount = 3
            };
        }

        /// <summary>2つの候補配列が全論理フィールドで一致することを確認します。</summary>
        private static void AssertCandidatesEqual(
            NativeList<AnimalSpawnCandidate> expected,
            NativeList<AnimalSpawnCandidate> actual)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (int index = 0; index < expected.Length; index++)
            {
                AnimalSpawnCandidate expectedCandidate = expected[index];
                AnimalSpawnCandidate actualCandidate = actual[index];
                Assert.That(actualCandidate.Layer, Is.EqualTo(expectedCandidate.Layer));
                Assert.That(actualCandidate.RegionX, Is.EqualTo(expectedCandidate.RegionX));
                Assert.That(actualCandidate.RegionY, Is.EqualTo(expectedCandidate.RegionY));
                Assert.That(actualCandidate.SpeciesRuntimeId, Is.EqualTo(expectedCandidate.SpeciesRuntimeId));
                Assert.That(actualCandidate.TileX, Is.EqualTo(expectedCandidate.TileX));
                Assert.That(actualCandidate.TileY, Is.EqualTo(expectedCandidate.TileY));
                Assert.That(actualCandidate.AppearanceVariant, Is.EqualTo(expectedCandidate.AppearanceVariant));
                Assert.That(actualCandidate.SpawnSequence, Is.EqualTo(expectedCandidate.SpawnSequence));
                Assert.That(
                    actualCandidate.HerdLeaderCandidateIndex,
                    Is.EqualTo(expectedCandidate.HerdLeaderCandidateIndex));
            }
        }

        /// <summary>2つの候補配列で1件以上の絶対位置が異なるかを返します。</summary>
        private static bool HasDifferentPosition(
            NativeList<AnimalSpawnCandidate> first,
            NativeList<AnimalSpawnCandidate> second)
        {
            if (first.Length != second.Length)
            {
                return true;
            }

            for (int index = 0; index < first.Length; index++)
            {
                if (first[index].TileX != second[index].TileX
                    || first[index].TileY != second[index].TileY)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>指定順で2チャンクを作り、System確定後の個体をID順で返します。</summary>
        private static List<AnimalRecord> RunInitializationSystem(
            BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog,
            (int X, int Y)[] chunkCoordinates)
        {
            using World world = new World("Animal Population Stable Id Test");
            PrepareWorldSingletons(world, animalCatalog);
            for (int index = 0; index < chunkCoordinates.Length; index++)
            {
                CreateReadyChunk(
                    world.EntityManager,
                    chunkCoordinates[index].X,
                    chunkCoordinates[index].Y);
            }

            SystemHandle system = world.GetOrCreateSystem<AnimalPopulationInitializationSystem>();
            system.Update(world.Unmanaged);

            EntityQuery query = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PersistentObjectId>(),
                ComponentType.ReadOnly<AnimalSpecies>(),
                ComponentType.ReadOnly<AnimalTilePosition>(),
                ComponentType.ReadOnly<AnimalHerdMembership>(),
                ComponentType.ReadOnly<AnimalAppearanceVariant>(),
                ComponentType.ReadOnly<WildAnimal>());
            using NativeArray<PersistentObjectId> ids =
                query.ToComponentDataArray<PersistentObjectId>(Allocator.Temp);
            using NativeArray<AnimalSpecies> species =
                query.ToComponentDataArray<AnimalSpecies>(Allocator.Temp);
            using NativeArray<AnimalTilePosition> positions =
                query.ToComponentDataArray<AnimalTilePosition>(Allocator.Temp);
            using NativeArray<AnimalHerdMembership> herds =
                query.ToComponentDataArray<AnimalHerdMembership>(Allocator.Temp);
            using NativeArray<AnimalAppearanceVariant> variants =
                query.ToComponentDataArray<AnimalAppearanceVariant>(Allocator.Temp);
            query.Dispose();

            List<AnimalRecord> records = new List<AnimalRecord>(ids.Length);
            for (int index = 0; index < ids.Length; index++)
            {
                records.Add(new AnimalRecord(
                    ids[index].Value,
                    species[index].RuntimeId.Value,
                    positions[index].TileX,
                    positions[index].TileY,
                    herds[index].HerdId,
                    variants[index].Value));
            }

            records.Sort((first, second) => first.PersistentId.CompareTo(second.PersistentId));
            return records;
        }

        /// <summary>Systemが必要とするSingletonと、既定で準備完了の固定Tick状態をテストWorldへ1件ずつ準備します。</summary>
        private static void PrepareWorldSingletons(
            World world,
            BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog)
        {
            Entity singletonEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(
                singletonEntity,
                new AnimalPopulationSeed { Value = 0xCAFEBABE12345678UL });
            world.EntityManager.AddComponentData(
                singletonEntity,
                new AnimalSpeciesCatalog { Value = animalCatalog });
            world.EntityManager.AddComponentData(
                singletonEntity,
                PersistentIdAllocatorState.CreateInitial());
            world.EntityManager.AddComponentData(
                singletonEntity,
                new SimulationTickState { CurrentTick = 12 });
            world.EntityManager.AddComponentData(
                singletonEntity,
                new FixedTickReadinessState { ChunkGenerationPending = 0 });
            world.EntityManager.AddComponentData(
                singletonEntity,
                new TerrainBlockCatalog
                {
                    BuiltInRuntimeIds = CreateTerrainRuntimeIds()
                });
        }

        /// <summary>全面草原陸地で生成完了済みのストリーミング用チャンクEntityを作ります。</summary>
        private static Entity CreateReadyChunk(EntityManager entityManager, int chunkX, int chunkY)
        {
            Entity chunkEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(
                chunkEntity,
                new ChunkCoordinateComponent
                {
                    Layer = WorldLayer.Surface,
                    X = chunkX,
                    Y = chunkY
                });
            entityManager.AddComponentData(
                chunkEntity,
                new ChunkGenerationStatus
                {
                    Phase = ChunkGenerationPhase.ReadyToApply,
                    GeneratedWithVersion = WorldGenerationSettings.CurrentGeneratorVersion
                });
            entityManager.AddBuffer<GeneratedTile>(chunkEntity);
            entityManager.AddBuffer<GeneratedTerrainCell>(chunkEntity);
            entityManager.AddBuffer<GeneratedResourceTile>(chunkEntity);
            entityManager.AddBuffer<GeneratedHydrologyCell>(chunkEntity);
            entityManager.AddBuffer<GeneratedSurfaceWaterTile>(chunkEntity);
            entityManager.AddBuffer<GeneratedHydrologyTile>(chunkEntity);
            // Buffer追加は構造変更なので、すべて追加した後に参照を取り直してから既存の地形・資源初期値を書き込みます。
            DynamicBuffer<GeneratedTerrainCell> terrainCells =
                entityManager.GetBuffer<GeneratedTerrainCell>(chunkEntity);
            DynamicBuffer<GeneratedResourceTile> resources =
                entityManager.GetBuffer<GeneratedResourceTile>(chunkEntity);
            terrainCells.ResizeUninitialized(WorldGridConstants.TilesPerChunk);
            resources.ResizeUninitialized(WorldGridConstants.TilesPerChunk);
            for (int index = 0; index < terrainCells.Length; index++)
            {
                terrainCells[index] = new GeneratedTerrainCell
                {
                    Biome = BiomeId.TemperateGrassland,
                    GroundBlockRuntimeId = TestGroundRuntimeId
                };
                resources[index] = default;
            }

            return chunkEntity;
        }

        /// <summary>個体群テストの設定Entityへ、チャンク生成が必要とする地形定義と固定Blockパレットを追加します。</summary>
        /// <param name="entityManager">設定Entityを保持するテストWorldです。</param>
        /// <param name="configuration">生成設定と同じ寿命でカタログを保持するEntityです。</param>
        /// <param name="catalogs">呼び出し側がWorld終了後まで所有するテスト用Blob一式です。</param>
        private static void AddTerrainCatalogComponents(
            EntityManager entityManager,
            Entity configuration,
            TerrainGenerationTestCatalogSet catalogs)
        {
            EntityQuery terrainCatalogQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadWrite<TerrainBlockCatalog>());
            Entity terrainCatalogEntity = terrainCatalogQuery.GetSingletonEntity();
            terrainCatalogQuery.Dispose();
            entityManager.SetComponentData(terrainCatalogEntity, new TerrainBlockCatalog
            {
                Value = catalogs.TerrainCatalog,
                BuiltInRuntimeIds = catalogs.RuntimeIds
            });
            entityManager.AddComponentData(configuration, new TerrainBlockPaletteCatalog
            {
                Value = catalogs.PaletteCatalog
            });
        }

        /// <summary>AnimalSpeciesCatalogBuilderと共通Builderで鶏1種のBlobを作ります。</summary>
        private static void CreateCatalogs(
            out BlobAssetReference<DefinitionCatalogBlob> definitionCatalog,
            out BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog)
        {
            bool definitionSucceeded = DefinitionCatalogBuilder.TryBuild(
                new[]
                {
                    new DefinitionBuildEntry
                    {
                        Guid = ChickenGuid,
                        Kind = DefinitionKind.AnimalSpecies,
                        ReferenceStartIndex = 0,
                        ReferenceCount = 0
                    }
                },
                System.Array.Empty<DefinitionBuildReference>(),
                out definitionCatalog,
                out DefinitionCatalogValidationError definitionError);
            Assert.That(
                definitionSucceeded,
                Is.True,
                $"共通定義のテスト準備に失敗しました: {definitionError.Code}");

            bool animalSucceeded = AnimalSpeciesCatalogBuilder.TryBuild(
                definitionCatalog,
                new[]
                {
                    new AnimalSpeciesDefinitionBuildEntry
                    {
                        DefinitionGuid = ChickenGuid,
                        // 固定Tick・Job順のテストではBiome分布を対象にしないため、18種すべてを許可して必ず実個体を作ります。
                        // 鶏の製品用TemperateGrassland制限はScene構築テストと適性判定テストで別に保証します。
                        AllowedBiomes = AnimalBiomeMask.All,
                        AllowedGrounds = AnimalGroundMask.Land,
                        MinimumHerdSize = 3,
                        MaximumHerdSize = 6,
                        TargetPopulationPer4096EligibleTiles = 8,
                        MinimumIdleTicks = 20,
                        MaximumIdleTicks = 80,
                        MovementStepIntervalTicks = 5,
                        MaximumWanderDistanceInTiles = 8,
                        AppearanceVariantCount = 3
                    }
                },
                out animalCatalog,
                out AnimalSpeciesCatalogValidationError animalError);
            Assert.That(
                animalSucceeded,
                Is.True,
                $"動物種定義のテスト準備に失敗しました: {animalError.Code}");
        }

        /// <summary>個体群テストで作成した2つのBlob Assetを安全な順序で破棄します。</summary>
        private static void DisposeCatalogs(
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog,
            BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog)
        {
            if (animalCatalog.IsCreated)
            {
                animalCatalog.Dispose();
            }

            if (definitionCatalog.IsCreated)
            {
                definitionCatalog.Dispose();
            }
        }

        /// <summary>World内の野生動物Entity数を返します。</summary>
        private static int CountAnimals(EntityManager entityManager)
        {
            EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<WildAnimal>());
            int count = query.CalculateEntityCount();
            query.Dispose();
            return count;
        }

        /// <summary>World内の動物地域状態Entity数を返します。</summary>
        private static int CountRegionStates(EntityManager entityManager)
        {
            EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AnimalPopulationRegionState>());
            int count = query.CalculateEntityCount();
            query.Dispose();
            return count;
        }

        /// <summary>Worldで一意な共通永続IDアロケーター状態を読み取ります。</summary>
        private static PersistentIdAllocatorState GetAllocatorState(EntityManager entityManager)
        {
            EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PersistentIdAllocatorState>());
            PersistentIdAllocatorState state = query.GetSingleton<PersistentIdAllocatorState>();
            query.Dispose();
            return state;
        }

        /// <summary>個体Entityの論理値を、World間で比較できる不変レコードへまとめます。</summary>
        private readonly struct AnimalRecord
        {
            /// <summary>共通アロケーターが発行したワールド内永続IDです。</summary>
            public readonly ulong PersistentId;

            /// <summary>鶏を表す共通定義実行時IDです。</summary>
            private readonly int speciesRuntimeId;

            /// <summary>絶対水平タイル座標です。</summary>
            private readonly int tileX;

            /// <summary>絶対垂直タイル座標です。</summary>
            private readonly int tileY;

            /// <summary>群れ先頭個体の永続IDです。</summary>
            private readonly ulong herdId;

            /// <summary>0始まりの外見差分番号です。</summary>
            private readonly byte appearanceVariant;

            /// <summary>比較対象となる全論理値を受け取って不変レコードを作ります。</summary>
            public AnimalRecord(
                ulong persistentId,
                int speciesRuntimeId,
                int tileX,
                int tileY,
                ulong herdId,
                byte appearanceVariant)
            {
                PersistentId = persistentId;
                this.speciesRuntimeId = speciesRuntimeId;
                this.tileX = tileX;
                this.tileY = tileY;
                this.herdId = herdId;
                this.appearanceVariant = appearanceVariant;
            }

            /// <summary>全論理値が一致するかをNUnitのEqualToから利用できる形で返します。</summary>
            public override bool Equals(object obj)
            {
                return obj is AnimalRecord other
                    && PersistentId == other.PersistentId
                    && speciesRuntimeId == other.speciesRuntimeId
                    && tileX == other.tileX
                    && tileY == other.tileY
                    && herdId == other.herdId
                    && appearanceVariant == other.appearanceVariant;
            }

            /// <summary>テスト用Collectionで論理値を扱うための32bitハッシュを返します。</summary>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = PersistentId.GetHashCode();
                    hash = (hash * 397) ^ speciesRuntimeId;
                    hash = (hash * 397) ^ tileX;
                    hash = (hash * 397) ^ tileY;
                    hash = (hash * 397) ^ herdId.GetHashCode();
                    return (hash * 397) ^ appearanceVariant;
                }
            }
        }
    }
}
