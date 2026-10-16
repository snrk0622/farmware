// このファイルは、Farmware共通の固定Tick、一時停止、最大追いつき回数、フレーム分割に依存しない進行をEditModeで検証します。
// テストごとに最小のECS Worldを作り、描画フレーム相当のDeltaTimeを直接与えて現在Tickだけを比較します。

using Farmware.Simulation;
using NUnit.Framework;
using Unity.Core;
using Unity.Entities;

namespace Farmware.Tests.EditMode
{
    /// <summary>
    /// SIM-01Bの固定Tick基盤が、通常進行、処理落ち、一時停止、異なるフレーム分割で同じ規則を守ることを保証します。
    /// Unity Test RunnerのEditModeから実行し、SceneやDefault Worldは使用しません。
    /// </summary>
    public sealed class FixedTickSimulationTests
    {
        /// <summary>
        /// 初期化Systemを1回実行し、固定Tick設定、現在Tick、一時停止状態が各1件作られることを保証します。
        /// </summary>
        [Test]
        public void InitializationSystem_CreatesOneSetOfSimulationTimeSingletons()
        {
            using World world = new World("Simulation Time Initialization Test");
            SystemHandle initializationSystem =
                world.GetOrCreateSystem<SimulationTimeInitializationSystem>();

            initializationSystem.Update(world.Unmanaged);

            Assert.That(
                CountComponents<FixedTickConfiguration>(world),
                Is.EqualTo(1));
            Assert.That(
                CountComponents<SimulationTickState>(world),
                Is.EqualTo(1));
            Assert.That(
                CountComponents<SimulationPauseState>(world),
                Is.EqualTo(1));

            FixedTickConfiguration configuration =
                GetSingleton<FixedTickConfiguration>(world);
            Assert.That(
                configuration.TicksPerSecond,
                Is.EqualTo(FixedTickConfiguration.DefaultTicksPerSecond));
            Assert.That(
                configuration.MaximumTicksPerFrame,
                Is.EqualTo(FixedTickConfiguration.DefaultMaximumTicksPerFrame));
            Assert.That(GetCurrentTick(world), Is.Zero);
            Assert.That(GetSingleton<SimulationPauseState>(world).IsPaused, Is.False);
        }

        /// <summary>
        /// 10 Tick/秒では0.1秒未満のフレーム時間を端数として保持し、合計が境界へ達した時だけTickを進めることを保証します。
        /// </summary>
        [Test]
        public void FixedTickGroup_AccumulatesSmallFrameTimesUntilTickBoundary()
        {
            using World world = CreateFixedTickTestWorld(
                FixedTickConfiguration.CreateDefault(),
                out FarmwareFixedTickSimulationSystemGroup fixedTickGroup);

            UpdateFixedTickGroup(world, fixedTickGroup, 0.04f);
            Assert.That(GetCurrentTick(world), Is.Zero);

            UpdateFixedTickGroup(world, fixedTickGroup, 0.06f);
            Assert.That(GetCurrentTick(world), Is.EqualTo(1ul));

            UpdateFixedTickGroup(world, fixedTickGroup, 0.20f);
            Assert.That(GetCurrentTick(world), Is.EqualTo(3ul));
        }

        /// <summary>
        /// 1秒超の遅延があっても1フレームでは最大4 Tickだけ進み、破棄した遅延を後続フレームへ持ち越さないことを保証します。
        /// </summary>
        [Test]
        public void FixedTickGroup_DropsWholeTicksBeyondMaximumCatchUpCount()
        {
            using World world = CreateFixedTickTestWorld(
                FixedTickConfiguration.CreateDefault(),
                out FarmwareFixedTickSimulationSystemGroup fixedTickGroup);

            UpdateFixedTickGroup(world, fixedTickGroup, 1.05f);
            Assert.That(
                GetCurrentTick(world),
                Is.EqualTo(4ul),
                "1描画フレームでは最大追いつき回数より多く進めてはいけません。");

            UpdateFixedTickGroup(world, fixedTickGroup, 0.05f);
            Assert.That(
                GetCurrentTick(world),
                Is.EqualTo(5ul),
                "上限超過分を持ち越さず、Tick未満の0.05秒だけを次フレームへ残してください。");
        }

        /// <summary>
        /// 一時停止中は大きなDeltaTimeを与えてもTickを進めず、再開時に停止時間分を追いつかないことを保証します。
        /// </summary>
        [Test]
        public void FixedTickGroup_PauseDoesNotAccumulateStoppedTime()
        {
            using World world = CreateFixedTickTestWorld(
                FixedTickConfiguration.CreateDefault(),
                out FarmwareFixedTickSimulationSystemGroup fixedTickGroup);

            UpdateFixedTickGroup(world, fixedTickGroup, 0.05f);
            SetPaused(world, true);

            UpdateFixedTickGroup(world, fixedTickGroup, 5f);
            Assert.That(GetCurrentTick(world), Is.Zero);

            SetPaused(world, false);
            UpdateFixedTickGroup(world, fixedTickGroup, 0.05f);
            Assert.That(
                GetCurrentTick(world),
                Is.EqualTo(1ul),
                "停止前の端数は保持しても、停止中の5秒は再開後へ加えてはいけません。");
        }

        /// <summary>
        /// 同じ合計時間を異なる描画フレーム幅で渡しても、追いつき上限内なら同じCurrentTickになることを保証します。
        /// </summary>
        [Test]
        public void FixedTickGroup_SameElapsedTimeProducesSameTickAcrossFramePartitions()
        {
            FixedTickConfiguration exactQuarterSecondConfiguration =
                new FixedTickConfiguration
                {
                    TicksPerSecond = 4,
                    MaximumTicksPerFrame = 4
                };

            using World firstWorld = CreateFixedTickTestWorld(
                exactQuarterSecondConfiguration,
                out FarmwareFixedTickSimulationSystemGroup firstGroup);
            using World secondWorld = CreateFixedTickTestWorld(
                exactQuarterSecondConfiguration,
                out FarmwareFixedTickSimulationSystemGroup secondGroup);

            // 0.125秒を8回渡し、細かい描画フレームで合計1秒を作ります。
            for (int frameIndex = 0; frameIndex < 8; frameIndex++)
            {
                UpdateFixedTickGroup(firstWorld, firstGroup, 0.125f);
            }

            // 同じ1秒を0.5、0.25、0.25秒へ分け、粗い描画フレームでも結果が変わらないことを比べます。
            UpdateFixedTickGroup(secondWorld, secondGroup, 0.5f);
            UpdateFixedTickGroup(secondWorld, secondGroup, 0.25f);
            UpdateFixedTickGroup(secondWorld, secondGroup, 0.25f);

            Assert.That(GetCurrentTick(firstWorld), Is.EqualTo(4ul));
            Assert.That(
                GetCurrentTick(secondWorld),
                Is.EqualTo(GetCurrentTick(firstWorld)));
        }

        /// <summary>
        /// Tick境界の許容誤差で少し先行した不足時間を負債として保持し、同じfloat合計を異なるフレーム幅で渡してもTick数が一致することを保証します。
        /// </summary>
        [Test]
        public void FixedTickGroup_PreservesToleranceDebtAcrossFramePartitions()
        {
            const float FineFrameDeltaTime = 0.09999995f;
            const int FineFrameCount = 1000;
            const float CoarseFrameDeltaTime = 0.1999999f;
            const int CoarseFrameCount = 500;

            using World fineFrameWorld = CreateFixedTickTestWorld(
                FixedTickConfiguration.CreateDefault(),
                out FarmwareFixedTickSimulationSystemGroup fineFrameGroup);
            using World coarseFrameWorld = CreateFixedTickTestWorld(
                FixedTickConfiguration.CreateDefault(),
                out FarmwareFixedTickSimulationSystemGroup coarseFrameGroup);

            // 2種類のDeltaTimeはfloatへ丸めた後も合計が同一です。分割方法以外の条件をそろえて比較します。
            Assert.That(
                (double)FineFrameDeltaTime * FineFrameCount,
                Is.EqualTo((double)CoarseFrameDeltaTime * CoarseFrameCount));

            // 0.1秒の直前にある細かいフレームを繰り返し、許容誤差による先行Tickと時間負債を発生させます。
            for (int frameIndex = 0; frameIndex < FineFrameCount; frameIndex++)
            {
                UpdateFixedTickGroup(fineFrameWorld, fineFrameGroup, FineFrameDeltaTime);
            }

            // 同じ合計時間を2倍幅のフレームで渡し、時間の分割だけを変更します。
            for (int frameIndex = 0; frameIndex < CoarseFrameCount; frameIndex++)
            {
                UpdateFixedTickGroup(coarseFrameWorld, coarseFrameGroup, CoarseFrameDeltaTime);
            }

            // 合計は100秒よりわずかに短いため、完了できる0.1秒Tickは両方999回です。
            Assert.That(GetCurrentTick(fineFrameWorld), Is.EqualTo(999ul));
            Assert.That(
                GetCurrentTick(coarseFrameWorld),
                Is.EqualTo(GetCurrentTick(fineFrameWorld)));
        }

        /// <summary>
        /// 0の設定値を正規化し、除算不能なTickレートや更新不能な追いつき上限を既定値へ戻すことを保証します。
        /// </summary>
        [Test]
        public void FixedTickConfiguration_NormalizedReplacesZeroWithDefaults()
        {
            FixedTickConfiguration normalized = default(FixedTickConfiguration).Normalized();

            Assert.That(
                normalized.TicksPerSecond,
                Is.EqualTo(FixedTickConfiguration.DefaultTicksPerSecond));
            Assert.That(
                normalized.MaximumTicksPerFrame,
                Is.EqualTo(FixedTickConfiguration.DefaultMaximumTicksPerFrame));
        }

        /// <summary>
        /// テスト専用Worldへ固定Tick用Singleton、Group、Tick加算Systemを作成します。
        /// </summary>
        /// <param name="configuration">World開始前に固定するTickレートと追いつき上限です。</param>
        /// <param name="fixedTickGroup">描画フレーム相当の更新を直接呼び出す固定Tick Groupです。</param>
        /// <returns>各テストがusingで破棄責任を持つ最小ECS Worldです。</returns>
        private static World CreateFixedTickTestWorld(
            FixedTickConfiguration configuration,
            out FarmwareFixedTickSimulationSystemGroup fixedTickGroup)
        {
            World world = new World("Fixed Tick Simulation Test");
            Entity singletonEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(singletonEntity, configuration);
            world.EntityManager.AddComponentData(
                singletonEntity,
                new SimulationTickState
                {
                    CurrentTick = 0
                });
            world.EntityManager.AddComponentData(
                singletonEntity,
                new SimulationPauseState
                {
                    Value = 0
                });

            fixedTickGroup =
                world.GetOrCreateSystemManaged<FarmwareFixedTickSimulationSystemGroup>();
            SystemHandle tickAdvanceSystem =
                world.GetOrCreateSystem<SimulationTickAdvanceSystem>();
            fixedTickGroup.AddSystemToUpdateList(tickAdvanceSystem);
            fixedTickGroup.SortSystems();
            return world;
        }

        /// <summary>
        /// 外側の描画フレームに相当するDeltaTimeをWorldへ一時設定し、固定Tick Groupを1回呼び出します。
        /// </summary>
        /// <param name="world">時間を一時設定するテストWorldです。</param>
        /// <param name="fixedTickGroup">経過時間から0回以上の固定Tickを実行するGroupです。</param>
        /// <param name="frameDeltaTime">今回の描画フレームで経過した秒数です。</param>
        private static void UpdateFixedTickGroup(
            World world,
            FarmwareFixedTickSimulationSystemGroup fixedTickGroup,
            float frameDeltaTime)
        {
            TimeData currentTime = world.Time;
            world.PushTime(new TimeData(
                currentTime.ElapsedTime + frameDeltaTime,
                frameDeltaTime));
            fixedTickGroup.Update();
            world.PopTime();
        }

        /// <summary>
        /// SimulationPauseState Singletonを更新し、次の描画フレームから停止または再開させます。
        /// </summary>
        /// <param name="world">一時停止状態を所有するテストWorldです。</param>
        /// <param name="isPaused">停止する場合はtrue、再開する場合はfalseです。</param>
        private static void SetPaused(World world, bool isPaused)
        {
            EntityQuery pauseQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<SimulationPauseState>());
            Entity pauseEntity = pauseQuery.GetSingletonEntity();
            SimulationPauseState pauseState =
                world.EntityManager.GetComponentData<SimulationPauseState>(pauseEntity);
            pauseState.SetPaused(isPaused);
            world.EntityManager.SetComponentData(pauseEntity, pauseState);
            pauseQuery.Dispose();
        }

        /// <summary>
        /// SimulationTickState Singletonから、固定Tick Groupが開始した現在Tick番号を読み取ります。
        /// </summary>
        /// <param name="world">現在Tickを所有するテストWorldです。</param>
        /// <returns>CurrentTickのulong値です。</returns>
        private static ulong GetCurrentTick(World world)
        {
            return GetSingleton<SimulationTickState>(world).CurrentTick;
        }

        /// <summary>
        /// 指定Componentを持つEntity数を数え、Singletonが1件だけ作られたかを検証できるようにします。
        /// </summary>
        /// <typeparam name="T">件数を数えるunmanagedなECS Component型です。</typeparam>
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

        /// <summary>
        /// 指定ComponentのSingleton値を読み取り、テスト終了前に一時Queryを破棄します。
        /// </summary>
        /// <typeparam name="T">取得するunmanagedなECS Component型です。</typeparam>
        /// <param name="world">Singletonを所有するテストWorldです。</param>
        /// <returns>指定型のSingleton Component値です。</returns>
        private static T GetSingleton<T>(World world)
            where T : unmanaged, IComponentData
        {
            EntityQuery query = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<T>());
            T singleton = query.GetSingleton<T>();
            query.Dispose();
            return singleton;
        }
    }
}
