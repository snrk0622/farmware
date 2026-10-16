// このファイルは、SIM-01EのCommand受付番号、順序、検証失敗時の無変更、ECB反映、結果寿命をEditModeで検証します。
// テスト専用Commandを最小ECS Worldへ追加し、Sceneや製品用の建築Commandが未実装でも共通基盤の契約を保証します。

using Farmware.Simulation;
using NUnit.Framework;
using Unity.Core;
using Unity.Entities;

namespace Farmware.Tests.EditMode
{
    /// <summary>
    /// 検証時に期待するMarker数と、テスト都合で拒否するかを保持する機能固有Commandです。
    /// 製品コードへテスト用Command種別を混ぜず、共通Queueが任意のunmanaged Componentを運べることも確認します。
    /// </summary>
    public struct CommandQueueTestPayload : IComponentData
    {
        /// <summary>このCommandの検証開始時に存在すると期待するMarker Entity数です。</summary>
        public int ExpectedMarkerCount;

        /// <summary>1なら期待数に関係なく検証を失敗させ、0なら期待数の一致で判定します。</summary>
        public byte ForceRejection;

        /// <summary>1ならCommitがMarker作成を記録した後に完了せず、共通基盤の破棄処理を検証します。</summary>
        public byte LeaveCommitIncomplete;
    }

    /// <summary>
    /// 成功したテストCommandが1件ずつECB経由で作る状態変更です。
    /// 次のCommandのValidationが直前のECB再生結果を観測できるかを数で確認します。
    /// </summary>
    public struct CommandQueueTestMarker : IComponentData
    {
    }

    /// <summary>
    /// Gameplay Groupが同じ固定Tick内でCommand確定後の状態を観測した記録です。
    /// テストがSystem順序を実際の更新結果から確認するため、World内に1件だけ作成します。
    /// </summary>
    public struct CommandQueueGameplayObservation : IComponentData
    {
        /// <summary>Gameplay Systemが更新された時点で存在したMarker Entity数です。</summary>
        public int MarkerCount;

        /// <summary>Gameplay Systemが結果を読み取った固定Tick番号です。</summary>
        public ulong ObservedTick;
    }

    /// <summary>
    /// テストCommandを担当し、Marker数を変更せずに全条件を検証するValidation Systemです。
    /// テストWorldへ明示追加した場合だけ動かし、通常のEditor Worldへ自動作成しません。
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(FarmwareCommandValidationSystemGroup))]
    public partial struct CommandQueueTestValidationSystem : ISystem
    {
        /// <summary>現在確定済みのMarker数を検証前に数えるQueryです。</summary>
        private EntityQuery markerQuery;

        /// <summary>
        /// System作成時にMarker用Queryを準備します。
        /// </summary>
        /// <param name="state">Queryを作成するテストWorldのSystem状態です。</param>
        public void OnCreate(ref SystemState state)
        {
            markerQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<CommandQueueTestMarker>());
        }

        /// <summary>
        /// ActiveなテストCommandを1件検証し、成功ならReadyToCommit、失敗ならRejectedへ進めます。
        /// </summary>
        /// <param name="state">テストCommandを所有するWorldのSystem状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            int currentMarkerCount = markerQuery.CalculateEntityCount();
            foreach ((RefRW<SimulationCommand> commandReference,
                     RefRO<CommandQueueTestPayload> payloadReference) in
                     SystemAPI.Query<RefRW<SimulationCommand>,
                         RefRO<CommandQueueTestPayload>>()
                         .WithAll<SimulationCommandActive>())
            {
                SimulationCommand command = commandReference.ValueRO;
                if (!SimulationCommandTransaction.TryClaimValidation(ref command))
                {
                    commandReference.ValueRW = command;
                    continue;
                }

                CommandQueueTestPayload payload = payloadReference.ValueRO;
                bool shouldReject = payload.ForceRejection != 0
                    || payload.ExpectedMarkerCount != currentMarkerCount;
                if (shouldReject)
                {
                    SimulationCommandTransaction.RejectValidation(
                        ref command,
                        SimulationCommand.FirstFeatureFailureCode);
                }
                else
                {
                    SimulationCommandTransaction.AcceptValidation(ref command);
                }

                commandReference.ValueRW = command;
            }
        }

        /// <summary>World破棄時に呼ばれます。QueryはWorldが所有するため個別解放はありません。</summary>
        /// <param name="state">破棄されるSystem状態です。</param>
        public void OnDestroy(ref SystemState state)
        {
        }
    }

    /// <summary>
    /// 同じテストCommandを誤って担当する2件目のValidation Systemです。
    /// 共通基盤が複数担当を検出し、先に成功した検証結果をそのままCommitしないことを確認するためだけに使用します。
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(FarmwareCommandValidationSystemGroup))]
    [UpdateAfter(typeof(CommandQueueTestValidationSystem))]
    public partial struct CommandQueueDuplicateValidationSystem : ISystem
    {
        /// <summary>System作成時に追加の永続状態は準備しません。</summary>
        /// <param name="state">テストWorldのSystem状態です。</param>
        public void OnCreate(ref SystemState state)
        {
        }

        /// <summary>
        /// ActiveなテストCommandへ2回目の担当宣言を行い、共通の複数担当拒否へ進めます。
        /// </summary>
        /// <param name="state">テストCommandの共通状態を更新するSystem状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            foreach (RefRW<SimulationCommand> commandReference in
                     SystemAPI.Query<RefRW<SimulationCommand>>()
                         .WithAll<SimulationCommandActive, CommandQueueTestPayload>())
            {
                SimulationCommand command = commandReference.ValueRO;
                SimulationCommandTransaction.TryClaimValidation(ref command);
                commandReference.ValueRW = command;
            }
        }

        /// <summary>World破棄時に呼ばれます。永続メモリを所有しないため解放処理はありません。</summary>
        /// <param name="state">破棄されるSystem状態です。</param>
        public void OnDestroy(ref SystemState state)
        {
        }
    }

    /// <summary>
    /// 検証済みテストCommandごとにMarker EntityをECBへ記録するCommit Systemです。
    /// ECBは同じCommand反復内で再生されるため、次のValidationから新しいMarkerを確認できます。
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(FarmwareCommandCommitSystemGroup))]
    public partial struct CommandQueueTestCommitSystem : ISystem
    {
        /// <summary>
        /// System作成時にCommand用ECB Singletonを必須条件へ登録します。
        /// </summary>
        /// <param name="state">更新条件を登録するテストWorldのSystem状態です。</param>
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SimulationCommandEntityCommandBufferSystem.Singleton>();
        }

        /// <summary>
        /// ReadyToCommitなCommandだけを開始し、Marker作成を記録して成功状態へ進めます。
        /// </summary>
        /// <param name="state">ECB作成とCommand状態更新に使用するSystem状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            SimulationCommandEntityCommandBufferSystem.Singleton commandBufferSingleton =
                SystemAPI.GetSingleton<SimulationCommandEntityCommandBufferSystem.Singleton>();
            EntityCommandBuffer commandBuffer =
                commandBufferSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            foreach ((RefRW<SimulationCommand> commandReference,
                     RefRO<CommandQueueTestPayload> payloadReference) in
                     SystemAPI.Query<RefRW<SimulationCommand>,
                         RefRO<CommandQueueTestPayload>>()
                         .WithAll<SimulationCommandActive, CommandQueueTestPayload>())
            {
                SimulationCommand command = commandReference.ValueRO;
                if (!SimulationCommandTransaction.TryBeginCommit(ref command))
                {
                    commandReference.ValueRW = command;
                    continue;
                }

                Entity markerEntity = commandBuffer.CreateEntity();
                commandBuffer.AddComponent(markerEntity, new CommandQueueTestMarker());

                // 未完了テストでは成功へ進めず、共通ECB Systemが記録済みMarker作成を破棄することを確認します。
                if (payloadReference.ValueRO.LeaveCommitIncomplete != 0)
                {
                    commandReference.ValueRW = command;
                    continue;
                }

                SimulationCommandTransaction.CompleteCommit(ref command);
                commandReference.ValueRW = command;
            }
        }

        /// <summary>World破棄時に呼ばれます。ECBの破棄責任は共通ECB Systemが持ちます。</summary>
        /// <param name="state">破棄されるSystem状態です。</param>
        public void OnDestroy(ref SystemState state)
        {
        }
    }

    /// <summary>
    /// Command Groupの後にGameplay Groupが更新され、確定済み状態だけを読むことを確認するテストSystemです。
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(FarmwareFixedTickGameplaySystemGroup))]
    public partial struct CommandQueueGameplayObservationSystem : ISystem
    {
        /// <summary>Command Commit後のMarker数を数えるQueryです。</summary>
        private EntityQuery markerQuery;

        /// <summary>
        /// System作成時にMarker Queryと必要なSingleton条件を準備します。
        /// </summary>
        /// <param name="state">Queryと更新条件を登録するテストWorldのSystem状態です。</param>
        public void OnCreate(ref SystemState state)
        {
            markerQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<CommandQueueTestMarker>());
            state.RequireForUpdate<CommandQueueGameplayObservation>();
            state.RequireForUpdate<SimulationTickState>();
        }

        /// <summary>
        /// 現在のMarker数とTick番号を記録し、Command処理より後に実行されたことを結果へ残します。
        /// </summary>
        /// <param name="state">観測結果Singletonを更新するSystem状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            RefRW<CommandQueueGameplayObservation> observation =
                SystemAPI.GetSingletonRW<CommandQueueGameplayObservation>();
            observation.ValueRW.MarkerCount = markerQuery.CalculateEntityCount();
            observation.ValueRW.ObservedTick =
                SystemAPI.GetSingleton<SimulationTickState>().CurrentTick;
        }

        /// <summary>World破棄時に呼ばれます。QueryはWorldが所有するため個別解放はありません。</summary>
        /// <param name="state">破棄されるSystem状態です。</param>
        public void OnDestroy(ref SystemState state)
        {
        }
    }

    /// <summary>
    /// Command Queueの初期化、受付、固定Tick処理、結果公開、次フレーム破棄を保証するテスト群です。
    /// </summary>
    public sealed class SimulationCommandQueueTests
    {
        /// <summary>テスト用Validation失敗理由です。共通予約範囲と衝突しない1024を使用します。</summary>
        private const int TestValidationFailureCode =
            SimulationCommand.FirstFeatureFailureCode;

        /// <summary>
        /// 初期化Systemが受付番号1のQueue状態と空Bufferを1件だけ作成することを保証します。
        /// </summary>
        [Test]
        public void InitializationSystem_CreatesOneEmptyQueueStartingAtOne()
        {
            using World world = new World("Command Queue Initialization Test");
            SystemHandle initializationSystem =
                world.GetOrCreateSystem<SimulationCommandQueueInitializationSystem>();

            initializationSystem.Update(world.Unmanaged);

            Entity queueEntity = GetQueueEntity(world);
            SimulationCommandQueueState queueState =
                world.EntityManager.GetComponentData<SimulationCommandQueueState>(queueEntity);
            DynamicBuffer<SimulationCommandQueueElement> queue =
                world.EntityManager.GetBuffer<SimulationCommandQueueElement>(queueEntity);
            Assert.That(queueState.NextReceiptNumber, Is.EqualTo(1ul));
            Assert.That(queueState.IsExhausted, Is.False);
            Assert.That(queue.Length, Is.Zero);
        }

        /// <summary>
        /// 通常時は受付番号を単調増加させ、一時停止中はEntityも番号も消費せず受付を拒否することを保証します。
        /// </summary>
        [Test]
        public void TryEnqueue_AssignsMonotonicReceiptsAndRejectsPausedInputWithoutConsumption()
        {
            using World world = CreateQueueOnlyWorld();

            bool firstAccepted = SimulationCommandQueue.TryEnqueue(
                world.EntityManager,
                CreatePayload(expectedMarkerCount: 0),
                out Entity firstCommand,
                out ulong firstReceipt);
            SetPaused(world, true);
            bool pausedAccepted = SimulationCommandQueue.TryEnqueue(
                world.EntityManager,
                CreatePayload(expectedMarkerCount: 0),
                out Entity pausedCommand,
                out ulong pausedReceipt);
            SetPaused(world, false);
            bool secondAccepted = SimulationCommandQueue.TryEnqueue(
                world.EntityManager,
                CreatePayload(expectedMarkerCount: 0),
                out Entity secondCommand,
                out ulong secondReceipt);

            Assert.That(firstAccepted, Is.True);
            Assert.That(firstReceipt, Is.EqualTo(1ul));
            Assert.That(world.EntityManager.Exists(firstCommand), Is.True);
            Assert.That(pausedAccepted, Is.False);
            Assert.That(pausedCommand, Is.EqualTo(Entity.Null));
            Assert.That(pausedReceipt, Is.Zero);
            Assert.That(secondAccepted, Is.True);
            Assert.That(secondReceipt, Is.EqualTo(2ul));
            Assert.That(world.EntityManager.Exists(secondCommand), Is.True);
            Assert.That(GetQueue(world).Length, Is.EqualTo(2));
        }

        /// <summary>
        /// ulong最大値を最後の有効な受付番号として1回使い、その後はQueueやEntityを変更せず拒否することを保証します。
        /// </summary>
        [Test]
        public void TryEnqueue_UsesMaximumReceiptOnceThenStopsWithoutWrapping()
        {
            using World world = CreateQueueOnlyWorld();
            Entity queueEntity = GetQueueEntity(world);
            world.EntityManager.SetComponentData(
                queueEntity,
                new SimulationCommandQueueState
                {
                    NextReceiptNumber = ulong.MaxValue,
                    ExhaustedValue = 0
                });

            bool lastAccepted = SimulationCommandQueue.TryEnqueue(
                world.EntityManager,
                CreatePayload(expectedMarkerCount: 0),
                out Entity lastCommand,
                out ulong lastReceipt);
            bool overflowAccepted = SimulationCommandQueue.TryEnqueue(
                world.EntityManager,
                CreatePayload(expectedMarkerCount: 0),
                out Entity overflowCommand,
                out ulong overflowReceipt);

            SimulationCommandQueueState finalState =
                world.EntityManager.GetComponentData<SimulationCommandQueueState>(queueEntity);
            Assert.That(lastAccepted, Is.True);
            Assert.That(lastReceipt, Is.EqualTo(ulong.MaxValue));
            Assert.That(world.EntityManager.Exists(lastCommand), Is.True);
            Assert.That(overflowAccepted, Is.False);
            Assert.That(overflowCommand, Is.EqualTo(Entity.Null));
            Assert.That(overflowReceipt, Is.Zero);
            Assert.That(finalState.NextReceiptNumber, Is.EqualTo(ulong.MaxValue));
            Assert.That(finalState.IsExhausted, Is.True);
            Assert.That(GetQueue(world).Length, Is.EqualTo(1));
        }

        /// <summary>
        /// 同一TickのCommandを受付順に検証・Commitし、失敗CommandがMarkerを一部変更せず、Gameplayが最終状態だけを見ることを保証します。
        /// </summary>
        [Test]
        public void ProcessingGroup_ProcessesSequentiallyAndLeavesRejectedCommandUnchanged()
        {
            using World world = CreateProcessingWorld(
                out FarmwareFixedTickSimulationSystemGroup fixedTickGroup);

            SimulationCommandQueue.TryEnqueue(
                world.EntityManager,
                CreatePayload(expectedMarkerCount: 0),
                out Entity firstCommand,
                out ulong firstReceipt);
            SimulationCommandQueue.TryEnqueue(
                world.EntityManager,
                CreatePayload(expectedMarkerCount: 0),
                out Entity conflictingCommand,
                out ulong conflictingReceipt);
            SimulationCommandQueue.TryEnqueue(
                world.EntityManager,
                CreatePayload(expectedMarkerCount: 1),
                out Entity thirdCommand,
                out ulong thirdReceipt);

            UpdateFixedTickGroup(world, fixedTickGroup, 0.1f);

            Assert.That(CountComponents<CommandQueueTestMarker>(world), Is.EqualTo(2));
            AssertCommandResult(
                world,
                firstCommand,
                firstReceipt,
                SimulationCommandStatus.Succeeded,
                (int)SimulationCommandFailureCode.None);
            AssertCommandResult(
                world,
                conflictingCommand,
                conflictingReceipt,
                SimulationCommandStatus.Rejected,
                TestValidationFailureCode);
            AssertCommandResult(
                world,
                thirdCommand,
                thirdReceipt,
                SimulationCommandStatus.Succeeded,
                (int)SimulationCommandFailureCode.None);
            Assert.That(GetQueue(world).Length, Is.Zero);

            CommandQueueGameplayObservation observation =
                GetSingleton<CommandQueueGameplayObservation>(world);
            Assert.That(observation.MarkerCount, Is.EqualTo(2));
            Assert.That(observation.ObservedTick, Is.EqualTo(1ul));
        }

        /// <summary>
        /// 担当Validation SystemがないCommandを共通基盤が拒否し、Commitや状態変更へ進めないことを保証します。
        /// </summary>
        [Test]
        public void ProcessingGroup_RejectsCommandWithoutValidationHandler()
        {
            using World world = CreateProcessingWorld(
                out FarmwareFixedTickSimulationSystemGroup fixedTickGroup,
                includeValidationHandler: false);
            SimulationCommandQueue.TryEnqueue(
                world.EntityManager,
                CreatePayload(expectedMarkerCount: 0),
                out Entity commandEntity,
                out ulong receiptNumber);

            UpdateFixedTickGroup(world, fixedTickGroup, 0.1f);

            Assert.That(CountComponents<CommandQueueTestMarker>(world), Is.Zero);
            AssertCommandResult(
                world,
                commandEntity,
                receiptNumber,
                SimulationCommandStatus.Rejected,
                (int)SimulationCommandFailureCode.ValidationHandlerMissing);
        }

        /// <summary>
        /// Validationに成功しても担当Commit SystemがないCommandを拒否し、状態変更を残さないことを保証します。
        /// </summary>
        [Test]
        public void ProcessingGroup_RejectsCommandWithoutCommitHandler()
        {
            using World world = CreateProcessingWorld(
                out FarmwareFixedTickSimulationSystemGroup fixedTickGroup,
                includeCommitHandler: false);
            SimulationCommandQueue.TryEnqueue(
                world.EntityManager,
                CreatePayload(expectedMarkerCount: 0),
                out Entity commandEntity,
                out ulong receiptNumber);

            UpdateFixedTickGroup(world, fixedTickGroup, 0.1f);

            Assert.That(CountComponents<CommandQueueTestMarker>(world), Is.Zero);
            AssertCommandResult(
                world,
                commandEntity,
                receiptNumber,
                SimulationCommandStatus.Rejected,
                (int)SimulationCommandFailureCode.CommitHandlerMissing);
        }

        /// <summary>
        /// 同じCommandを複数のValidation Systemが担当した場合は曖昧なCommitへ進めず拒否することを保証します。
        /// </summary>
        [Test]
        public void ProcessingGroup_RejectsMultipleValidationHandlers()
        {
            using World world = CreateProcessingWorld(
                out FarmwareFixedTickSimulationSystemGroup fixedTickGroup,
                includeDuplicateValidationHandler: true);
            SimulationCommandQueue.TryEnqueue(
                world.EntityManager,
                CreatePayload(expectedMarkerCount: 0),
                out Entity commandEntity,
                out ulong receiptNumber);

            UpdateFixedTickGroup(world, fixedTickGroup, 0.1f);

            Assert.That(CountComponents<CommandQueueTestMarker>(world), Is.Zero);
            AssertCommandResult(
                world,
                commandEntity,
                receiptNumber,
                SimulationCommandStatus.Rejected,
                (int)SimulationCommandFailureCode.MultipleValidationHandlers);
        }

        /// <summary>
        /// Commitが成功状態へ到達しない場合は記録済みECBを再生せず、部分的なMarker作成を残さないことを保証します。
        /// </summary>
        [Test]
        public void ProcessingGroup_DiscardsRecordedChangesWhenCommitDoesNotComplete()
        {
            using World world = CreateProcessingWorld(
                out FarmwareFixedTickSimulationSystemGroup fixedTickGroup);
            SimulationCommandQueue.TryEnqueue(
                world.EntityManager,
                CreatePayload(expectedMarkerCount: 0, leaveCommitIncomplete: true),
                out Entity commandEntity,
                out ulong receiptNumber);

            UpdateFixedTickGroup(world, fixedTickGroup, 0.1f);

            Assert.That(CountComponents<CommandQueueTestMarker>(world), Is.Zero);
            AssertCommandResult(
                world,
                commandEntity,
                receiptNumber,
                SimulationCommandStatus.Rejected,
                (int)SimulationCommandFailureCode.CommitDidNotComplete);
        }

        /// <summary>
        /// 未完了Commitが連続しても各ECBを再生せず、その後の成功Commandだけを正常に反映できることを保証します。
        /// 失敗経路もUnity標準ECB処理へ渡し、次のCommandへ未再生BufferやAllocator状態を持ち越さない契約を確認します。
        /// </summary>
        [Test]
        public void ProcessingGroup_DiscardsRepeatedIncompleteCommitsBeforeFollowingSuccess()
        {
            using World world = CreateProcessingWorld(
                out FarmwareFixedTickSimulationSystemGroup fixedTickGroup);
            const int incompleteCommandCount = 32;
            Entity[] incompleteCommands = new Entity[incompleteCommandCount];
            ulong[] incompleteReceipts = new ulong[incompleteCommandCount];

            // Queueの内部容量を超える件数を同Tickへ投入し、失敗が連続する現実的な経路を再現します。
            for (int index = 0; index < incompleteCommandCount; index++)
            {
                SimulationCommandQueue.TryEnqueue(
                    world.EntityManager,
                    CreatePayload(expectedMarkerCount: 0, leaveCommitIncomplete: true),
                    out incompleteCommands[index],
                    out incompleteReceipts[index]);
            }

            SimulationCommandQueue.TryEnqueue(
                world.EntityManager,
                CreatePayload(expectedMarkerCount: 0),
                out Entity successfulCommand,
                out ulong successfulReceipt);

            UpdateFixedTickGroup(world, fixedTickGroup, 0.1f);

            // 32件分の記録はすべて破棄され、最後に成功した1件だけがMarkerを作成します。
            Assert.That(CountComponents<CommandQueueTestMarker>(world), Is.EqualTo(1));
            for (int index = 0; index < incompleteCommandCount; index++)
            {
                AssertCommandResult(
                    world,
                    incompleteCommands[index],
                    incompleteReceipts[index],
                    SimulationCommandStatus.Rejected,
                    (int)SimulationCommandFailureCode.CommitDidNotComplete);
            }

            AssertCommandResult(
                world,
                successfulCommand,
                successfulReceipt,
                SimulationCommandStatus.Succeeded,
                (int)SimulationCommandFailureCode.None);
        }

        /// <summary>
        /// 完了結果が処理フレーム中は存在し、次の描画フレーム先頭のCleanupで破棄されることを保証します。
        /// </summary>
        [Test]
        public void ResultCleanupSystem_DestroysCompletedCommandsOnFollowingFrame()
        {
            using World world = CreateProcessingWorld(
                out FarmwareFixedTickSimulationSystemGroup fixedTickGroup);
            SimulationCommandQueue.TryEnqueue(
                world.EntityManager,
                CreatePayload(expectedMarkerCount: 0),
                out Entity commandEntity,
                out ulong receiptNumber);
            UpdateFixedTickGroup(world, fixedTickGroup, 0.1f);

            Assert.That(world.EntityManager.Exists(commandEntity), Is.True);
            AssertCommandResult(
                world,
                commandEntity,
                receiptNumber,
                SimulationCommandStatus.Succeeded,
                (int)SimulationCommandFailureCode.None);

            SystemHandle cleanupSystem =
                world.GetOrCreateSystem<SimulationCommandResultCleanupSystem>();
            cleanupSystem.Update(world.Unmanaged);

            Assert.That(world.EntityManager.Exists(commandEntity), Is.False);
        }

        /// <summary>
        /// Queue受付と一時停止判定だけに必要なSingletonを持つ最小Worldを作成します。
        /// </summary>
        /// <returns>各テストがusingで破棄責任を持つECS Worldです。</returns>
        private static World CreateQueueOnlyWorld()
        {
            World world = new World("Command Queue Test");
            Entity controlEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(
                controlEntity,
                new SimulationPauseState
                {
                    Value = 0
                });

            SystemHandle initializationSystem =
                world.GetOrCreateSystem<SimulationCommandQueueInitializationSystem>();
            initializationSystem.Update(world.Unmanaged);
            return world;
        }

        /// <summary>
        /// 固定Tick、Command Group、テスト用Validation／Commit、Gameplay観測を接続した最小Worldを作成します。
        /// </summary>
        /// <param name="fixedTickGroup">描画フレーム相当の更新を直接呼ぶ親固定Tick Groupです。</param>
        /// <param name="includeValidationHandler">falseならValidation担当を追加せず、担当不在の拒否を検証します。</param>
        /// <param name="includeCommitHandler">falseならCommit担当を追加せず、検証成功後の担当不在を検証します。</param>
        /// <param name="includeDuplicateValidationHandler">trueなら2件目のValidation担当を追加し、複数担当の拒否を検証します。</param>
        /// <returns>各テストがusingで破棄責任を持つECS Worldです。</returns>
        private static World CreateProcessingWorld(
            out FarmwareFixedTickSimulationSystemGroup fixedTickGroup,
            bool includeValidationHandler = true,
            bool includeCommitHandler = true,
            bool includeDuplicateValidationHandler = false)
        {
            World world = CreateQueueOnlyWorld();
            Entity timeEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(
                timeEntity,
                FixedTickConfiguration.CreateDefault());
            world.EntityManager.AddComponentData(
                timeEntity,
                new SimulationTickState
                {
                    CurrentTick = 0
                });
            Entity observationEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(
                observationEntity,
                new CommandQueueGameplayObservation
                {
                    MarkerCount = -1,
                    ObservedTick = 0
                });

            fixedTickGroup =
                world.GetOrCreateSystemManaged<FarmwareFixedTickSimulationSystemGroup>();
            SystemHandle tickAdvanceSystem =
                world.GetOrCreateSystem<SimulationTickAdvanceSystem>();
            FarmwareCommandProcessingSystemGroup commandGroup =
                world.GetOrCreateSystemManaged<FarmwareCommandProcessingSystemGroup>();
            FarmwareCommandValidationSystemGroup validationGroup =
                world.GetOrCreateSystemManaged<FarmwareCommandValidationSystemGroup>();
            SystemHandle validationEndSystem =
                world.GetOrCreateSystem<SimulationCommandValidationEndSystem>();
            FarmwareCommandCommitSystemGroup commitGroup =
                world.GetOrCreateSystemManaged<FarmwareCommandCommitSystemGroup>();
            SimulationCommandEntityCommandBufferSystem commandBufferSystem =
                world.GetOrCreateSystemManaged<SimulationCommandEntityCommandBufferSystem>();
            SystemHandle completionSystem =
                world.GetOrCreateSystem<SimulationCommandCompletionSystem>();
            FarmwareFixedTickGameplaySystemGroup gameplayGroup =
                world.GetOrCreateSystemManaged<FarmwareFixedTickGameplaySystemGroup>();
            SystemHandle gameplayObservationSystem =
                world.GetOrCreateSystem<CommandQueueGameplayObservationSystem>();

            if (includeValidationHandler)
            {
                SystemHandle validationSystem =
                    world.GetOrCreateSystem<CommandQueueTestValidationSystem>();
                validationGroup.AddSystemToUpdateList(validationSystem);
            }

            if (includeDuplicateValidationHandler)
            {
                SystemHandle duplicateValidationSystem =
                    world.GetOrCreateSystem<CommandQueueDuplicateValidationSystem>();
                validationGroup.AddSystemToUpdateList(duplicateValidationSystem);
            }

            if (includeCommitHandler)
            {
                SystemHandle commitSystem =
                    world.GetOrCreateSystem<CommandQueueTestCommitSystem>();
                commitGroup.AddSystemToUpdateList(commitSystem);
            }

            validationGroup.SortSystems();
            commitGroup.SortSystems();
            commandGroup.AddSystemToUpdateList(validationGroup);
            commandGroup.AddSystemToUpdateList(validationEndSystem);
            commandGroup.AddSystemToUpdateList(commitGroup);
            commandGroup.AddSystemToUpdateList(commandBufferSystem);
            commandGroup.AddSystemToUpdateList(completionSystem);
            commandGroup.SortSystems();

            gameplayGroup.AddSystemToUpdateList(gameplayObservationSystem);
            gameplayGroup.SortSystems();
            fixedTickGroup.AddSystemToUpdateList(tickAdvanceSystem);
            fixedTickGroup.AddSystemToUpdateList(commandGroup);
            fixedTickGroup.AddSystemToUpdateList(gameplayGroup);
            fixedTickGroup.SortSystems();
            return world;
        }

        /// <summary>
        /// テストCommandのPayloadを、期待Marker数と任意の強制拒否設定から作成します。
        /// </summary>
        /// <param name="expectedMarkerCount">Validation開始時に期待する確定済みMarker数です。</param>
        /// <param name="forceRejection">期待数に関係なく拒否させる場合はtrueです。</param>
        /// <param name="leaveCommitIncomplete">Marker作成を記録した後にCommitを未完了のまま残す場合はtrueです。</param>
        /// <returns>Queueへ追加できるテスト用Command Componentです。</returns>
        private static CommandQueueTestPayload CreatePayload(
            int expectedMarkerCount,
            bool forceRejection = false,
            bool leaveCommitIncomplete = false)
        {
            return new CommandQueueTestPayload
            {
                ExpectedMarkerCount = expectedMarkerCount,
                ForceRejection = forceRejection ? (byte)1 : (byte)0,
                LeaveCommitIncomplete = leaveCommitIncomplete ? (byte)1 : (byte)0
            };
        }

        /// <summary>
        /// 外側の描画フレーム時間を一時設定し、親固定Tick Groupを1回更新します。
        /// </summary>
        /// <param name="world">時間を一時設定するテストWorldです。</param>
        /// <param name="fixedTickGroup">経過時間から固定Tickを実行する親Groupです。</param>
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
        /// Command Entityの最終状態、受付番号、処理Tick、Enableableタグ状態をまとめて検証します。
        /// </summary>
        /// <param name="world">Command Entityを所有するテストWorldです。</param>
        /// <param name="commandEntity">結果を確認するCommand Entityです。</param>
        /// <param name="expectedReceipt">受付時に返された番号です。</param>
        /// <param name="expectedStatus">期待する成功または拒否状態です。</param>
        /// <param name="expectedFailureCode">期待する共通またはテスト固有失敗理由です。</param>
        private static void AssertCommandResult(
            World world,
            Entity commandEntity,
            ulong expectedReceipt,
            SimulationCommandStatus expectedStatus,
            int expectedFailureCode)
        {
            SimulationCommand command =
                world.EntityManager.GetComponentData<SimulationCommand>(commandEntity);
            Assert.That(command.ReceiptNumber, Is.EqualTo(expectedReceipt));
            Assert.That(command.Status, Is.EqualTo(expectedStatus));
            Assert.That(command.FailureCode, Is.EqualTo(expectedFailureCode));
            Assert.That(command.ProcessedTick, Is.EqualTo(1ul));
            Assert.That(
                world.EntityManager.IsComponentEnabled<SimulationCommandActive>(commandEntity),
                Is.False);
            Assert.That(
                world.EntityManager.IsComponentEnabled<SimulationCommandCompleted>(commandEntity),
                Is.True);
        }

        /// <summary>
        /// SimulationPauseState Singletonを更新し、次の受付を停止または再開します。
        /// </summary>
        /// <param name="world">一時停止状態を所有するテストWorldです。</param>
        /// <param name="isPaused">停止する場合はtrue、通常受付へ戻す場合はfalseです。</param>
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
        /// World内のCommand Queue Singleton Entityを取得します。
        /// </summary>
        /// <param name="world">Queueを所有するテストWorldです。</param>
        /// <returns>Queue状態とDynamic Bufferを持つ唯一のEntityです。</returns>
        private static Entity GetQueueEntity(World world)
        {
            EntityQuery queueQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<SimulationCommandQueueState>(),
                ComponentType.ReadOnly<SimulationCommandQueueElement>());
            Entity queueEntity = queueQuery.GetSingletonEntity();
            queueQuery.Dispose();
            return queueEntity;
        }

        /// <summary>
        /// World内のCommand Queue Dynamic Bufferを取得します。
        /// 呼び出し側は同じ式の間だけ参照し、構造変更をまたいで保持しません。
        /// </summary>
        /// <param name="world">Queueを所有するテストWorldです。</param>
        /// <returns>受付順のCommand参照を持つDynamic Bufferです。</returns>
        private static DynamicBuffer<SimulationCommandQueueElement> GetQueue(World world)
        {
            return world.EntityManager.GetBuffer<SimulationCommandQueueElement>(
                GetQueueEntity(world));
        }

        /// <summary>
        /// 指定Componentを持つEntity数を数え、Command成功・失敗後の状態変更件数を比較します。
        /// </summary>
        /// <typeparam name="T">件数を数えるunmanaged Component型です。</typeparam>
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
        /// 指定ComponentのSingleton値を読み取り、一時Queryを同じ呼び出し内で解放します。
        /// </summary>
        /// <typeparam name="T">取得するunmanaged Component型です。</typeparam>
        /// <param name="world">Singletonを所有するテストWorldです。</param>
        /// <returns>指定型のSingleton Component値です。</returns>
        private static T GetSingleton<T>(World world)
            where T : unmanaged, IComponentData
        {
            EntityQuery query = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<T>());
            T value = query.GetSingleton<T>();
            query.Dispose();
            return value;
        }
    }
}
