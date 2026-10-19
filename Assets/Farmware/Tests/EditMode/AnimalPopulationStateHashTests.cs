// このファイルは、動物個体群の状態ハッシュがEntity生成順を除外し、保存対象の論理値を漏れなく区別することを検証します。
// ANIMAL-01Bの地域状態と個体状態を小さなテストWorldへ直接作り、地形生成や表示に依存せずハッシュ契約だけを確認します。

using Farmware.Simulation;
using Farmware.Simulation.Animals;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.Tests
{
    /// <summary>
    /// AnimalPopulationStateHasherの正規化、変更検出、不正参照の拒否を保証するEditModeテストです。
    /// </summary>
    public sealed class AnimalPopulationStateHashTests
    {
        /// <summary>各テストWorldが参照し、TearDownで破棄する、鶏と別種の2種を含む静的カタログです。</summary>
        private BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog;

        /// <summary>各テスト前に、有効な種ID1・2と外見差分3種類を持つカタログを準備します。</summary>
        [SetUp]
        public void SetUp()
        {
            animalCatalog = CreateAnimalCatalog();
        }

        /// <summary>各テスト後に、永続Allocatorで確保したBlob Assetを必ず破棄します。</summary>
        [TearDown]
        public void TearDown()
        {
            if (animalCatalog.IsCreated)
            {
                animalCatalog.Dispose();
            }
        }

        /// <summary>
        /// 同じ地域と個体を逆順で作り、Entity格納順が違っても同じ状態ハッシュになることを保証します。
        /// </summary>
        [Test]
        public void TryAppendTo_IgnoresEntityCreationOrder()
        {
            using World firstWorld = CreateWorld(reverseCreationOrder: false);
            using World secondWorld = CreateWorld(reverseCreationOrder: true);

            Assert.That(TryCompute(firstWorld, out SimulationStateHash firstHash), Is.True);
            Assert.That(TryCompute(secondWorld, out SimulationStateHash secondHash), Is.True);
            Assert.That(secondHash, Is.EqualTo(firstHash));
        }

        /// <summary>
        /// 地域の次回抽選状態だけを変更し、画面上の個体が同じでも将来の生成結果が違う状態を区別できることを保証します。
        /// </summary>
        [Test]
        public void TryAppendTo_DetectsRegionRandomStateChange()
        {
            using World firstWorld = CreateWorld(reverseCreationOrder: false);
            using World secondWorld = CreateWorld(reverseCreationOrder: false);
            EntityQuery regionQuery = secondWorld.EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<AnimalPopulationRegionState>());
            try
            {
                AnimalPopulationRegionState changed =
                    regionQuery.GetSingleton<AnimalPopulationRegionState>();
                changed.RandomState++;
                regionQuery.SetSingleton(changed);
            }
            finally
            {
                regionQuery.Dispose();
            }

            Assert.That(TryCompute(firstWorld, out SimulationStateHash firstHash), Is.True);
            Assert.That(TryCompute(secondWorld, out SimulationStateHash secondHash), Is.True);
            Assert.That(secondHash, Is.Not.EqualTo(firstHash));
        }

        /// <summary>
        /// 群れ先頭IDが存在しない壊れた個体を準備し、復元後の不正参照を正式な状態ハッシュとして受理しないことを保証します。
        /// </summary>
        [Test]
        public void TryAppendTo_RejectsMissingHerdLeader()
        {
            using World world = CreateWorld(reverseCreationOrder: false);
            EntityQuery animalQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<WildAnimal>(),
                ComponentType.ReadWrite<AnimalHerdMembership>());
            try
            {
                using Unity.Collections.NativeArray<Entity> animals =
                    animalQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                AnimalHerdMembership invalidMembership =
                    world.EntityManager.GetComponentData<AnimalHerdMembership>(animals[0]);
                invalidMembership.HerdId = 9999;
                world.EntityManager.SetComponentData(animals[0], invalidMembership);
            }
            finally
            {
                animalQuery.Dispose();
            }

            SimulationStateHashBuilder builder = CreateBuilderAfterSpeciesSection();
            bool succeeded = AnimalPopulationStateHasher.TryAppendTo(
                world.EntityManager,
                ref builder,
                out AnimalPopulationStateHashError error);

            Assert.That(succeeded, Is.False);
            Assert.That(error.Code, Is.EqualTo(AnimalPopulationStateHashErrorCode.AnimalStateIsInvalid));
        }

        /// <summary>
        /// WildAnimalタグだけを持つ不完全なEntityを追加し、必須Component欠落をハッシュ対象外として黙認しないことを保証します。
        /// </summary>
        [Test]
        public void TryAppendTo_RejectsWildAnimalWithMissingRequiredComponent()
        {
            using World world = CreateWorld(reverseCreationOrder: false);
            world.EntityManager.CreateEntity(typeof(WildAnimal));

            SimulationStateHashBuilder builder = CreateBuilderAfterSpeciesSection();
            bool succeeded = AnimalPopulationStateHasher.TryAppendTo(
                world.EntityManager,
                ref builder,
                out AnimalPopulationStateHashError error);

            Assert.That(succeeded, Is.False);
            Assert.That(error.Code, Is.EqualTo(AnimalPopulationStateHashErrorCode.AnimalStateIsInvalid));
        }

        /// <summary>
        /// 正数でも動物種カタログに存在しないRuntime IDを持つ個体を、正式な状態として受理しないことを保証します。
        /// </summary>
        [Test]
        public void TryAppendTo_RejectsUnknownSpeciesRuntimeId()
        {
            using World world = CreateWorld(reverseCreationOrder: false);
            Entity animal = GetFirstAnimal(world.EntityManager);
            world.EntityManager.SetComponentData(
                animal,
                new AnimalSpecies { RuntimeId = new DefinitionRuntimeId(99) });

            SimulationStateHashBuilder builder = CreateBuilderAfterSpeciesSection();
            bool succeeded = AnimalPopulationStateHasher.TryAppendTo(
                world.EntityManager,
                ref builder,
                out AnimalPopulationStateHashError error);

            Assert.That(succeeded, Is.False);
            Assert.That(error.Code, Is.EqualTo(AnimalPopulationStateHashErrorCode.AnimalStateIsInvalid));
        }

        /// <summary>
        /// 種定義の外見差分数と同じ番号は0始まりの範囲外となるため、状態ハッシュが拒否することを保証します。
        /// </summary>
        [Test]
        public void TryAppendTo_RejectsAppearanceVariantOutsideSpeciesRange()
        {
            using World world = CreateWorld(reverseCreationOrder: false);
            Entity animal = GetFirstAnimal(world.EntityManager);
            world.EntityManager.SetComponentData(
                animal,
                new AnimalAppearanceVariant { Value = 3 });

            SimulationStateHashBuilder builder = CreateBuilderAfterSpeciesSection();
            bool succeeded = AnimalPopulationStateHasher.TryAppendTo(
                world.EntityManager,
                ref builder,
                out AnimalPopulationStateHashError error);

            Assert.That(succeeded, Is.False);
            Assert.That(error.Code, Is.EqualTo(AnimalPopulationStateHashErrorCode.AnimalStateIsInvalid));
        }

        /// <summary>
        /// 存在するIDでも、循環参照、別群れのメンバー、別種の先頭への所属を正式な状態として受理しないことを保証します。
        /// </summary>
        /// <param name="invalidCase">0は循環、1はメンバーへの間接参照、2は異種の群れ先頭への参照です。</param>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void TryAppendTo_RejectsInvalidHerdLeaderRelationship(int invalidCase)
        {
            using World world = CreateWorld(reverseCreationOrder: false);
            EntityManager entityManager = world.EntityManager;
            if (invalidCase == 0)
            {
                // 既存の11→10に対して10→11を設定し、IDの存在だけでは判別できない循環を作ります。
                entityManager.SetComponentData(FindAnimal(entityManager, 10), new AnimalHerdMembership { HerdId = 11 });
            }
            else if (invalidCase == 1)
            {
                CreateAnimal(entityManager, 12, 12, 4, -4, 0);
                CreateAnimal(entityManager, 13, 12, 5, -4, 0);
                entityManager.SetComponentData(FindAnimal(entityManager, 11), new AnimalHerdMembership { HerdId = 13 });
            }
            else
            {
                CreateAnimal(entityManager, 12, 12, 4, -4, 0);
                entityManager.SetComponentData(FindAnimal(entityManager, 12),
                    new AnimalSpecies { RuntimeId = new DefinitionRuntimeId(2) });
                entityManager.SetComponentData(FindAnimal(entityManager, 11), new AnimalHerdMembership { HerdId = 12 });
            }

            SimulationStateHashBuilder builder = CreateBuilderAfterSpeciesSection();
            Assert.That(AnimalPopulationStateHasher.TryAppendTo(entityManager, ref builder,
                out AnimalPopulationStateHashError error), Is.False);
            Assert.That(error.Code, Is.EqualTo(AnimalPopulationStateHashErrorCode.AnimalStateIsInvalid));
        }

        /// <summary>不正所属を作るテスト準備時に、生成順に依存せず指定永続IDの個体Entityを探します。</summary>
        /// <param name="entityManager">テストWorldのEntityManagerです。</param>
        /// <param name="persistentId">取得する個体の永続IDです。</param>
        /// <returns>指定IDの個体Entityです。見つからない場合はテスト準備失敗として例外にします。</returns>
        private static Entity FindAnimal(EntityManager entityManager, ulong persistentId)
        {
            using EntityQuery query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<WildAnimal>(), ComponentType.ReadOnly<PersistentObjectId>());
            using NativeArray<Entity> animals = query.ToEntityArray(Allocator.Temp);
            foreach (Entity animal in animals)
            {
                if (entityManager.GetComponentData<PersistentObjectId>(animal).Value == persistentId)
                {
                    return animal;
                }
            }

            throw new System.InvalidOperationException("テスト対象の動物IDがありません。");
        }

        /// <summary>
        /// 指定順序でSeed、1地域、同じ群れの2個体を作る、各テスト専用のECS Worldを返します。
        /// </summary>
        /// <param name="reverseCreationOrder">trueなら地域と個体、および個体同士のEntity作成順を逆転します。</param>
        /// <returns>呼び出し側がusingで破棄するテストWorldです。</returns>
        private World CreateWorld(bool reverseCreationOrder)
        {
            World world = new World("Animal population state hash test");
            EntityManager entityManager = world.EntityManager;
            Entity catalogEntity = entityManager.CreateEntity(typeof(AnimalSpeciesCatalog));
            entityManager.SetComponentData(
                catalogEntity,
                new AnimalSpeciesCatalog { Value = animalCatalog });
            Entity seedEntity = entityManager.CreateEntity(typeof(AnimalPopulationSeed));
            entityManager.SetComponentData(
                seedEntity,
                new AnimalPopulationSeed { Value = 0x123456789ABCDEF0UL });

            if (reverseCreationOrder)
            {
                CreateAnimal(entityManager, 11, 10, 3, -4, 2);
                CreateAnimal(entityManager, 10, 10, 2, -4, 1);
                CreateRegion(entityManager);
            }
            else
            {
                CreateRegion(entityManager);
                CreateAnimal(entityManager, 10, 10, 2, -4, 1);
                CreateAnimal(entityManager, 11, 10, 3, -4, 2);
            }

            return world;
        }

        /// <summary>野生動物Queryの先頭Entityを取得し、不正値を設定する回帰テストへ返します。</summary>
        /// <param name="entityManager">対象のテストWorldを所有するEntityManagerです。</param>
        /// <returns>状態ハッシュ対象となる野生動物Entityの1体です。</returns>
        private static Entity GetFirstAnimal(EntityManager entityManager)
        {
            EntityQuery animalQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<WildAnimal>());
            try
            {
                using NativeArray<Entity> animals =
                    animalQuery.ToEntityArray(Allocator.Temp);
                return animals[0];
            }
            finally
            {
                animalQuery.Dispose();
            }
        }

        /// <summary>鶏のRuntime ID 1と別種ID 2を含み、各種が外見差分3種類を持つカタログを作ります。</summary>
        /// <returns>TearDownで破棄する永続AllocatorのBlob Assetです。</returns>
        private static BlobAssetReference<AnimalSpeciesCatalogBlob> CreateAnimalCatalog()
        {
            using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref AnimalSpeciesCatalogBlob root =
                ref builder.ConstructRoot<AnimalSpeciesCatalogBlob>();
            BlobBuilderArray<AnimalSpeciesDefinitionBlob> species =
                builder.Allocate(ref root.Species, 2);
            species[0] = new AnimalSpeciesDefinitionBlob
            {
                SpeciesRuntimeId = new DefinitionRuntimeId(1),
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
            species[1] = species[0];
            species[1].SpeciesRuntimeId = new DefinitionRuntimeId(2);
            return builder.CreateBlobAssetReference<AnimalSpeciesCatalogBlob>(
                Allocator.Persistent);
        }

        /// <summary>保存対象となる1地域の抽選状態をテストWorldへ作ります。</summary>
        /// <param name="entityManager">地域Entityを所有するテストWorldのEntityManagerです。</param>
        private static void CreateRegion(EntityManager entityManager)
        {
            Entity regionEntity = entityManager.CreateEntity(typeof(AnimalPopulationRegionState));
            entityManager.SetComponentData(
                regionEntity,
                new AnimalPopulationRegionState
                {
                    RegionX = -1,
                    RegionY = 2,
                    RandomState = 12345,
                    NextSpawnSequence = 3,
                    InitializedAtTick = 20
                });
        }

        /// <summary>状態ハッシュに必要な全Componentを持つ野生動物Entityを1体作ります。</summary>
        /// <param name="entityManager">個体Entityを所有するテストWorldのEntityManagerです。</param>
        /// <param name="persistentId">個体の一意な永続IDです。</param>
        /// <param name="herdId">群れ先頭個体の永続IDです。</param>
        /// <param name="tileX">絶対水平タイル座標です。</param>
        /// <param name="tileY">絶対垂直タイル座標です。</param>
        /// <param name="variant">動物種内の外見差分番号です。</param>
        private static void CreateAnimal(
            EntityManager entityManager,
            ulong persistentId,
            ulong herdId,
            int tileX,
            int tileY,
            byte variant)
        {
            Entity animal = entityManager.CreateEntity(
                typeof(WildAnimal),
                typeof(PersistentObjectId),
                typeof(AnimalSpecies),
                typeof(AnimalTilePosition),
                typeof(AnimalHerdMembership),
                typeof(AnimalAppearanceVariant));
            entityManager.SetComponentData(
                animal,
                new PersistentObjectId { Value = persistentId });
            entityManager.SetComponentData(
                animal,
                new AnimalSpecies { RuntimeId = new DefinitionRuntimeId(1) });
            entityManager.SetComponentData(
                animal,
                new AnimalTilePosition { TileX = tileX, TileY = tileY });
            entityManager.SetComponentData(
                animal,
                new AnimalHerdMembership { HerdId = herdId });
            entityManager.SetComponentData(
                animal,
                new AnimalAppearanceVariant { Value = variant });
        }

        /// <summary>動物種定義セクションまで追加済みという呼び出し順を模したBuilderを作ります。</summary>
        /// <returns>ANIMAL-01Bの個体群セクションを次に追加できるBuilderです。</returns>
        private static SimulationStateHashBuilder CreateBuilderAfterSpeciesSection()
        {
            SimulationStateHashBuilder builder =
                new SimulationStateHashBuilder(SimulationStateHashSchema.CurrentVersion);
            builder.BeginSection(AnimalSpeciesStateHasher.SpeciesDefinitionSectionId);
            builder.WriteInt32(0);
            return builder;
        }

        /// <summary>テストWorldの個体群セクションを追加し、完成した128bit状態ハッシュを返します。</summary>
        /// <param name="world">比較対象のテストWorldです。</param>
        /// <param name="hash">成功時に完成した状態ハッシュを受け取ります。</param>
        /// <returns>個体群状態が有効で、Builderへ追加できた場合はtrueです。</returns>
        private static bool TryCompute(World world, out SimulationStateHash hash)
        {
            SimulationStateHashBuilder builder = CreateBuilderAfterSpeciesSection();
            if (!AnimalPopulationStateHasher.TryAppendTo(
                    world.EntityManager,
                    ref builder,
                    out _))
            {
                hash = default;
                return false;
            }

            hash = builder.Complete();
            return true;
        }
    }
}
