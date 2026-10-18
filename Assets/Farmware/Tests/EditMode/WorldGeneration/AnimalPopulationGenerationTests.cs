// このファイルは、ANIMAL-01Bの地域初期個体群、群れ形成、永続ID発行の決定論をEditModeで検証します。
// 64×64の生成済み地形を準備し、純粋生成パイプラインと固定Tick Systemの両方を実行します。

using System.Collections.Generic;
using Farmware.Simulation;
using Farmware.Simulation.Animals;
using Farmware.WorldGeneration.Animals;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 地形Seedと個体群Seedの分離、地域境界、群れ、ID安定順、再初期化防止を保証するテスト群です。
    /// </summary>
    public sealed class AnimalPopulationGenerationTests
    {
        /// <summary>テスト用共通定義で鶏を識別する変更禁止GUIDです。</summary>
        private static readonly DefinitionGuid ChickenGuid = new DefinitionGuid(0, 0xC1);

        /// <summary>
        /// 全面が草原陸地の地域へ、定義どおり8羽を3〜6羽の群れとして重複なしで配置することを保証します。
        /// </summary>
        [Test]
        public void InitialPopulation_CreatesTargetCountInValidHerdsWithoutDuplicateTiles()
        {
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog = default;
            BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog = default;
            using NativeArray<GeneratedTile> tiles = CreateUniformTiles(
                BiomeId.Grassland,
                GroundType.Land);
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
            using NativeArray<GeneratedTile> tiles = CreateUniformTiles(
                BiomeId.Grassland,
                GroundType.Land);
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
                        ref firstState,
                        first),
                    Is.True);
                Assert.That(
                    AnimalPopulationGenerator.TryAppendInitialPopulation(
                        animalCatalog,
                        tiles,
                        resources,
                        ref repeatedState,
                        repeated),
                    Is.True);
                Assert.That(
                    AnimalPopulationGenerator.TryAppendInitialPopulation(
                        animalCatalog,
                        tiles,
                        resources,
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

            Assert.That(
                AnimalPopulationGenerator.IsTileEligible(
                    new GeneratedTile { Biome = BiomeId.Grassland, Ground = GroundType.Land },
                    chicken),
                Is.True);
            Assert.That(
                AnimalPopulationGenerator.IsTileEligible(
                    new GeneratedTile { Biome = BiomeId.Forest, Ground = GroundType.Land },
                    chicken),
                Is.False);
            Assert.That(
                AnimalPopulationGenerator.IsTileEligible(
                    new GeneratedTile { Biome = BiomeId.Grassland, Ground = GroundType.Water },
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

        /// <summary>全面を同じBiomeとGroundで満たした4096タイル配列を準備します。</summary>
        private static NativeArray<GeneratedTile> CreateUniformTiles(
            BiomeId biome,
            GroundType ground)
        {
            NativeArray<GeneratedTile> tiles = new NativeArray<GeneratedTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Temp);
            for (int index = 0; index < tiles.Length; index++)
            {
                tiles[index] = new GeneratedTile
                {
                    Biome = biome,
                    Ground = ground
                };
            }

            return tiles;
        }

        /// <summary>純粋な適格判定テストへ渡す、鶏の検証済み相当Blob値を作ります。</summary>
        private static AnimalSpeciesDefinitionBlob CreateChickenBlob(
            DefinitionRuntimeId runtimeId)
        {
            return new AnimalSpeciesDefinitionBlob
            {
                SpeciesRuntimeId = runtimeId,
                AllowedBiomes = AnimalBiomeMask.Grassland,
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

        /// <summary>Systemが必要とする4つのSingletonをテストWorldへ1件ずつ準備します。</summary>
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
        }

        /// <summary>全面草原陸地で生成完了済みのストリーミング用チャンクEntityを作ります。</summary>
        private static Entity CreateReadyChunk(EntityManager entityManager, int chunkX, int chunkY)
        {
            Entity chunkEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(
                chunkEntity,
                new ChunkCoordinateComponent { X = chunkX, Y = chunkY });
            entityManager.AddComponentData(
                chunkEntity,
                new ChunkGenerationStatus
                {
                    Phase = ChunkGenerationPhase.ReadyToApply,
                    GeneratedWithVersion = 1
                });
            entityManager.AddBuffer<GeneratedTile>(chunkEntity);
            entityManager.AddBuffer<GeneratedResourceTile>(chunkEntity);
            // Buffer追加は構造変更なので、両方を追加した後に参照を取り直してから初期値を書き込みます。
            DynamicBuffer<GeneratedTile> tiles = entityManager.GetBuffer<GeneratedTile>(chunkEntity);
            DynamicBuffer<GeneratedResourceTile> resources =
                entityManager.GetBuffer<GeneratedResourceTile>(chunkEntity);
            tiles.ResizeUninitialized(WorldGridConstants.TilesPerChunk);
            resources.ResizeUninitialized(WorldGridConstants.TilesPerChunk);
            for (int index = 0; index < tiles.Length; index++)
            {
                tiles[index] = new GeneratedTile
                {
                    Biome = BiomeId.Grassland,
                    Ground = GroundType.Land
                };
                resources[index] = default;
            }

            return chunkEntity;
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
                        AllowedBiomes = AnimalBiomeMask.Grassland,
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
