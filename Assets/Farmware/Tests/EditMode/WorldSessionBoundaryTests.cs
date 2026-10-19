// このファイルは、WORLD-01DのWorldId、PlayerId、ローカル操作主体、停止窓口をEditModeで検証します。
// SceneやInput機器を使わず最小ECS Worldを作り、別ワールド入力や不正な所有者が状態を変更しないことを保証します。

using Farmware.Simulation;
using NUnit.Framework;
using Unity.Entities;

namespace Farmware.Tests.EditMode
{
    /// <summary>
    /// 単独プレイ用の初期化と、後続の睡眠・関心範囲も再利用する共通主体検証の契約を保証するテスト群です。
    /// </summary>
    public sealed class WorldSessionBoundaryTests
    {
        /// <summary>
        /// 一般的なGUID表記を同じWorldIdへ正規化し、全0や不正文字列を未設定として拒否することを保証します。
        /// </summary>
        [Test]
        public void WorldId_TryParseNormalizesGuidAndRejectsInvalidValues()
        {
            const string compact = "1234567890abcdef1234567890abcdef";
            const string dashed = "12345678-90ab-cdef-1234-567890abcdef";

            Assert.That(WorldId.TryParse(compact, out WorldId compactId), Is.True);
            Assert.That(WorldId.TryParse(dashed, out WorldId dashedId), Is.True);
            Assert.That(compactId.Equals(dashedId), Is.True);
            Assert.That(compactId.ToString(), Is.EqualTo(compact));
            Assert.That(WorldId.TryParse("not-a-guid", out _), Is.False);
            Assert.That(
                WorldId.TryParse("00000000-0000-0000-0000-000000000000", out _),
                Is.False);
        }

        /// <summary>
        /// 保存読込がまだない開発用Worldへ、固定WorldId、Player 1、ローカルContextを各1件だけ作ることを保証します。
        /// </summary>
        [Test]
        public void InitializationSystem_CreatesDevelopmentWorldAndSingleLocalPlayer()
        {
            using World world = new World("World Session Initialization Test");
            SystemHandle initializationSystem =
                world.GetOrCreateSystem<WorldSessionInitializationSystem>();

            initializationSystem.Update(world.Unmanaged);

            WorldSessionIdentity worldIdentity =
                GetSingleton<WorldSessionIdentity>(world);
            PlayerIdentity playerIdentity = GetSingleton<PlayerIdentity>(world);
            LocalPlayerContext localPlayerContext =
                GetSingleton<LocalPlayerContext>(world);
            Assert.That(worldIdentity.Id.Equals(WorldId.DevelopmentDefault), Is.True);
            Assert.That(playerIdentity.Id.Value, Is.EqualTo(1ul));
            Assert.That(localPlayerContext.PlayerId.Equals(playerIdentity.Id), Is.True);
            Assert.That(
                WorldSessionAuthority.TryGetLocalActor(
                    world.EntityManager,
                    out WorldCommandActor actor,
                    out WorldSessionAccessFailure failure),
                Is.True);
            Assert.That(failure, Is.EqualTo(WorldSessionAccessFailure.None));
            Assert.That(actor.WorldId.Equals(WorldId.DevelopmentDefault), Is.True);
            Assert.That(actor.PlayerId.Value, Is.EqualTo(1ul));
        }

        /// <summary>
        /// SAVE-01等が一部だけ準備した状態を固定開発値で補修せず、後続検証が不整合を検出できることを保証します。
        /// </summary>
        [Test]
        public void InitializationSystem_DoesNotOverwritePartialExternalSessionState()
        {
            using World world = new World("World Session Partial Load Test");
            WorldId loadedWorldId = new WorldId(10, 20);
            Entity identityEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(
                identityEntity,
                new WorldSessionIdentity
                {
                    Id = loadedWorldId
                });
            SystemHandle initializationSystem =
                world.GetOrCreateSystem<WorldSessionInitializationSystem>();

            initializationSystem.Update(world.Unmanaged);

            Assert.That(CountComponents<WorldSessionIdentity>(world), Is.EqualTo(1));
            Assert.That(CountComponents<PlayerIdentity>(world), Is.Zero);
            Assert.That(CountComponents<LocalPlayerContext>(world), Is.Zero);
            Assert.That(
                GetSingleton<WorldSessionIdentity>(world).Id.Equals(loadedWorldId),
                Is.True);
        }

        /// <summary>
        /// 現在Worldと登録済みPlayerだけを受理し、古いWorld入力と未登録Playerを状態変更前に区別して拒否します。
        /// </summary>
        [Test]
        public void Authority_RejectsWrongWorldAndUnknownPlayer()
        {
            using World world = CreateValidSessionWorld(includePauseState: false);
            WorldCommandActor validActor = CreateActor();
            WorldCommandActor wrongWorldActor = new WorldCommandActor(
                new WorldId(validActor.WorldId.High, validActor.WorldId.Low + 1),
                validActor.PlayerId);
            WorldCommandActor unknownPlayerActor = new WorldCommandActor(
                validActor.WorldId,
                new PlayerId(2));

            Assert.That(
                WorldSessionAuthority.TryValidateActor(
                    world.EntityManager,
                    validActor,
                    out WorldSessionAccessFailure validFailure),
                Is.True);
            Assert.That(validFailure, Is.EqualTo(WorldSessionAccessFailure.None));
            Assert.That(
                WorldSessionAuthority.TryValidateActor(
                    world.EntityManager,
                    wrongWorldActor,
                    out WorldSessionAccessFailure worldFailure),
                Is.False);
            Assert.That(worldFailure, Is.EqualTo(WorldSessionAccessFailure.WorldMismatch));
            Assert.That(
                WorldSessionAuthority.TryValidateActor(
                    world.EntityManager,
                    unknownPlayerActor,
                    out WorldSessionAccessFailure playerFailure),
                Is.False);
            Assert.That(playerFailure, Is.EqualTo(WorldSessionAccessFailure.PlayerNotFound));
        }

        /// <summary>
        /// 現在World側のIDが全0なら、正規な操作主体の誤りと混同せず、読込状態の破損として拒否することを保証します。
        /// </summary>
        [Test]
        public void Authority_RejectsInvalidStoredWorldIdentity()
        {
            using World world = CreateValidSessionWorld(includePauseState: false);
            EntityQuery worldIdentityQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<WorldSessionIdentity>());
            try
            {
                Entity worldIdentityEntity = worldIdentityQuery.GetSingletonEntity();
                world.EntityManager.SetComponentData(
                    worldIdentityEntity,
                    new WorldSessionIdentity
                    {
                        Id = WorldId.Invalid
                    });
            }
            finally
            {
                worldIdentityQuery.Dispose();
            }

            Assert.That(
                WorldSessionAuthority.TryValidateActor(
                    world.EntityManager,
                    CreateActor(),
                    out WorldSessionAccessFailure failure),
                Is.False);
            Assert.That(
                failure,
                Is.EqualTo(WorldSessionAccessFailure.WorldIdentityInvalid));
        }

        /// <summary>
        /// 同じPlayerIdを複数Entityへ付けた破損状態では、任意の1件を操作主体として選ばないことを保証します。
        /// </summary>
        [Test]
        public void Authority_RejectsDuplicatedPlayerIdentity()
        {
            using World world = CreateValidSessionWorld(includePauseState: false);
            Entity duplicatePlayerEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(
                duplicatePlayerEntity,
                new PlayerIdentity
                {
                    Id = CreateActor().PlayerId
                });

            Assert.That(
                WorldSessionAuthority.TryValidateActor(
                    world.EntityManager,
                    CreateActor(),
                    out WorldSessionAccessFailure failure),
                Is.False);
            Assert.That(
                failure,
                Is.EqualTo(WorldSessionAccessFailure.PlayerIdentityDuplicated));
        }

        /// <summary>
        /// 停止と再開をQueue外の同じ主体検証から行い、停止中でも正規プレイヤーが再開できることを保証します。
        /// </summary>
        [Test]
        public void PauseControl_ValidActorCanPauseAndResume()
        {
            using World world = CreateValidSessionWorld(includePauseState: true);
            WorldCommandActor actor = CreateActor();

            bool paused = SimulationPauseControl.TrySetPaused(
                world.EntityManager,
                actor,
                true,
                out WorldSessionAccessFailure pauseFailure);
            bool resumed = SimulationPauseControl.TrySetPaused(
                world.EntityManager,
                actor,
                false,
                out WorldSessionAccessFailure resumeFailure);

            Assert.That(paused, Is.True);
            Assert.That(pauseFailure, Is.EqualTo(WorldSessionAccessFailure.None));
            Assert.That(resumed, Is.True);
            Assert.That(resumeFailure, Is.EqualTo(WorldSessionAccessFailure.None));
            Assert.That(GetSingleton<SimulationPauseState>(world).IsPaused, Is.False);
        }

        /// <summary>
        /// 別ワールドの主体による停止要求を拒否し、現在のPause値を変更しないことを保証します。
        /// </summary>
        [Test]
        public void PauseControl_WrongWorldLeavesStateUnchanged()
        {
            using World world = CreateValidSessionWorld(includePauseState: true);
            WorldCommandActor actor = CreateActor();
            WorldCommandActor wrongWorldActor = new WorldCommandActor(
                new WorldId(actor.WorldId.High + 1, actor.WorldId.Low),
                actor.PlayerId);

            bool changed = SimulationPauseControl.TrySetPaused(
                world.EntityManager,
                wrongWorldActor,
                true,
                out WorldSessionAccessFailure failure);

            Assert.That(changed, Is.False);
            Assert.That(failure, Is.EqualTo(WorldSessionAccessFailure.WorldMismatch));
            Assert.That(GetSingleton<SimulationPauseState>(world).IsPaused, Is.False);
        }

        /// <summary>
        /// テストで共通利用する固定WorldIdとPlayer 1の操作主体を作成します。
        /// </summary>
        /// <returns>有効な単独プレイヤー主体です。</returns>
        private static WorldCommandActor CreateActor()
        {
            return new WorldCommandActor(
                new WorldId(100, 200),
                new PlayerId(1));
        }

        /// <summary>
        /// WorldId、PlayerId、Local Contextと、必要ならPause Singletonを持つ最小Worldを作成します。
        /// </summary>
        /// <param name="includePauseState">停止窓口の検証用Singletonを追加する場合はtrueです。</param>
        /// <returns>呼び出し側がusingで破棄するテストWorldです。</returns>
        private static World CreateValidSessionWorld(bool includePauseState)
        {
            World world = new World("World Session Boundary Test");
            WorldCommandActor actor = CreateActor();

            Entity worldEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(
                worldEntity,
                new WorldSessionIdentity
                {
                    Id = actor.WorldId
                });

            Entity playerEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(
                playerEntity,
                new PlayerIdentity
                {
                    Id = actor.PlayerId
                });

            Entity localContextEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(
                localContextEntity,
                new LocalPlayerContext
                {
                    PlayerId = actor.PlayerId
                });

            if (includePauseState)
            {
                Entity pauseEntity = world.EntityManager.CreateEntity();
                world.EntityManager.AddComponentData(
                    pauseEntity,
                    new SimulationPauseState
                    {
                        Value = 0
                    });
            }

            return world;
        }

        /// <summary>
        /// 指定Componentの唯一の値を取得し、一時Queryを同じ呼び出し内で解放します。
        /// </summary>
        /// <typeparam name="T">取得するunmanaged Component型です。</typeparam>
        /// <param name="world">検索対象のテストWorldです。</param>
        /// <returns>Worldに1件だけ存在するComponent値です。</returns>
        private static T GetSingleton<T>(World world)
            where T : unmanaged, IComponentData
        {
            EntityQuery query = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<T>());
            T value = query.GetSingleton<T>();
            query.Dispose();
            return value;
        }

        /// <summary>
        /// 指定Componentを持つEntity数を数え、一時Queryを解放します。
        /// </summary>
        /// <typeparam name="T">件数を確認するunmanaged Component型です。</typeparam>
        /// <param name="world">検索対象のテストWorldです。</param>
        /// <returns>指定Componentを持つEntity数です。</returns>
        private static int CountComponents<T>(World world)
            where T : unmanaged, IComponentData
        {
            EntityQuery query = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<T>());
            int count = query.CalculateEntityCount();
            query.Dispose();
            return count;
        }
    }
}
