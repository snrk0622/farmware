// このファイルは、動物個体群の状態ハッシュがEntity生成順を除外し、保存対象の論理値を漏れなく区別することを検証します。
// ANIMAL-01Bの地域状態と個体状態を小さなテストWorldへ直接作り、地形生成や表示に依存せずハッシュ契約だけを確認します。

using Farmware.Simulation;
using Farmware.Simulation.Animals;
using NUnit.Framework;
using Unity.Entities;

namespace Farmware.Tests
{
    /// <summary>
    /// AnimalPopulationStateHasherの正規化、変更検出、不正参照の拒否を保証するEditModeテストです。
    /// </summary>
    public sealed class AnimalPopulationStateHashTests
    {
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
        /// 指定順序でSeed、1地域、同じ群れの2個体を作る、各テスト専用のECS Worldを返します。
        /// </summary>
        /// <param name="reverseCreationOrder">trueなら地域と個体、および個体同士のEntity作成順を逆転します。</param>
        /// <returns>呼び出し側がusingで破棄するテストWorldです。</returns>
        private static World CreateWorld(bool reverseCreationOrder)
        {
            World world = new World("Animal population state hash test");
            EntityManager entityManager = world.EntityManager;
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
