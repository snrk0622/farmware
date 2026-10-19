// このファイルは、Command Queueの初期化、結果破棄、固定Tick先頭での受付順処理を担当します。
// Validation、Commit、ECB反映、結果確定をCommandごとに完了し、後続ゲームルールへ途中状態を公開しません。

using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Farmware.Simulation
{
    /// <summary>
    /// Commandの検証から結果確定までを固定Tick先頭で実行する共通Groupです。
    /// 専用Rate ManagerがQueue要素数だけGroupを反復し、受付順の1件ごとに全処理段階を完了させます。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareFixedTickSimulationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(SimulationTickAdvanceSystem))]
    public partial class FarmwareCommandProcessingSystemGroup : ComponentSystemGroup
    {
        /// <summary>
        /// Group作成時にCommand Queue専用Rate Managerを接続します。
        /// World.Timeは変更せず、同じ固定Tick番号の中で受付済みCommandだけを順番に処理します。
        /// </summary>
        public FarmwareCommandProcessingSystemGroup()
        {
            RateManager = new SimulationCommandRateManager();
        }

        /// <summary>
        /// World破棄時にRate Managerが所有するEntityQueryを先に解放します。
        /// </summary>
        protected override void OnDestroy()
        {
            if (RateManager is SimulationCommandRateManager commandRateManager)
            {
                commandRateManager.Dispose();
            }

            RateManager = null;
            base.OnDestroy();
        }
    }

    /// <summary>
    /// 機能固有のCommand Validation SystemをまとめるGroupです。
    /// 子Systemはゲーム状態を読み取るだけに留め、検証中に資材、Entity、占有情報などを変更しません。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareCommandProcessingSystemGroup), OrderFirst = true)]
    public partial class FarmwareCommandValidationSystemGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// 検証に成功したCommandだけをゲーム状態へ反映する機能固有Commit SystemのGroupです。
    /// Component値の更新を含むゲーム状態変更はSimulationCommandEntityCommandBufferSystemのECBへ記録し、直接実行しません。
    /// 共通Command状態だけを成功へ進め、未完了なら記録済みECBを破棄して部分変更を防ぎます。
    /// Commandごとに直後の再生または破棄を確定するため、子Systemはメインスレッドで完結し、ECBへ書くJobをスケジュールしません。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareCommandProcessingSystemGroup))]
    [UpdateAfter(typeof(SimulationCommandValidationEndSystem))]
    public partial class FarmwareCommandCommitSystemGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// Command確定後に動く生産、物流、動物などの固定TickゲームルールをまとめるGroupです。
    /// 後続機能は固定Tickの直下ではなくこのGroupへ所属し、同TickのCommand結果を必ず読み取ります。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareFixedTickSimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(FarmwareCommandProcessingSystemGroup))]
    public partial class FarmwareFixedTickGameplaySystemGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// 新しいWorldへCommand Queue状態とDynamic Bufferを1件だけ準備する初期化Systemです。
    /// 受付番号はWorldごとに1から始まり、Commandと一緒にセーブしません。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareInitializationSystemGroup))]
    public partial struct SimulationCommandQueueInitializationSystem : ISystem
    {
        /// <summary>
        /// 既存Queue Singletonを確認するQueryです。Worldが所有し、System破棄時にUnityが解放します。
        /// </summary>
        private EntityQuery queueQuery;

        /// <summary>
        /// System作成時にQueue状態とBufferを同時に持つEntity用Queryを準備します。
        /// </summary>
        /// <param name="state">Queryを作成するWorldのSystem状態です。</param>
        public void OnCreate(ref SystemState state)
        {
            queueQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<SimulationCommandQueueState>(),
                ComponentType.ReadOnly<SimulationCommandQueueElement>());
        }

        /// <summary>
        /// Queueが未作成ならSingleton EntityとBufferを追加し、以後の毎フレーム更新を停止します。
        /// </summary>
        /// <param name="state">Queue Entityの作成とSystem停止に使用する状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            if (queueQuery.IsEmptyIgnoreFilter)
            {
                Entity queueEntity = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(
                    queueEntity,
                    SimulationCommandQueueState.CreateInitial());
                state.EntityManager.AddBuffer<SimulationCommandQueueElement>(queueEntity);
            }

            // Queueの作成または復元済み状態の確認はWorld開始時だけ必要なので、以後は更新しません。
            state.Enabled = false;
        }

        /// <summary>
        /// World破棄時に呼ばれます。Query、Entity、BufferはWorldが所有するため個別解放はありません。
        /// </summary>
        /// <param name="state">破棄されるSystem状態です。Entity変更には使用しません。</param>
        public void OnDestroy(ref SystemState state)
        {
        }
    }

    /// <summary>
    /// 前の描画フレームで完了したCommand Entityを、次の固定Tick処理より前に破棄するSystemです。
    /// 結果を1固定Tickではなく1描画フレーム保持し、同一フレームに複数Tickが進んでもPresentationの読取機会を残します。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareSimulationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(FarmwareFixedTickSimulationSystemGroup))]
    public partial struct SimulationCommandResultCleanupSystem : ISystem
    {
        /// <summary>有効なCompletedタグを持つ、前フレームのCommand結果だけを検索するQueryです。</summary>
        private EntityQuery completedCommandQuery;

        /// <summary>
        /// System作成時に完了Command用Queryを準備します。無効なCompletedタグは対象に含めません。
        /// </summary>
        /// <param name="state">Queryを作成するWorldのSystem状態です。</param>
        public void OnCreate(ref SystemState state)
        {
            completedCommandQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<SimulationCommand>(),
                ComponentType.ReadOnly<SimulationCommandCompleted>());
            state.RequireForUpdate(completedCommandQuery);
        }

        /// <summary>
        /// Presentationが前フレームに読み取った完了Commandを一括破棄します。
        /// </summary>
        /// <param name="state">完了Command Entityを所有するWorldのSystem状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            // Queryは有効なCompletedだけに一致するため、未処理または処理中Commandを誤って破棄しません。
            state.EntityManager.DestroyEntity(completedCommandQuery);
        }

        /// <summary>
        /// World破棄時に呼ばれます。QueryはWorldが所有するため個別解放はありません。
        /// </summary>
        /// <param name="state">破棄されるSystem状態です。Entity変更には使用しません。</param>
        public void OnDestroy(ref SystemState state)
        {
        }
    }

    /// <summary>
    /// Validation Group終了時に、担当SystemがないCommandや複数担当のCommandを安全に拒否します。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareCommandProcessingSystemGroup))]
    [UpdateAfter(typeof(FarmwareCommandValidationSystemGroup))]
    [UpdateBefore(typeof(FarmwareCommandCommitSystemGroup))]
    public partial struct SimulationCommandValidationEndSystem : ISystem
    {
        /// <summary>System作成時に追加の永続状態は準備しません。</summary>
        /// <param name="state">WorldのSystem状態です。</param>
        public void OnCreate(ref SystemState state)
        {
        }

        /// <summary>
        /// 現在処理中の1件を確認し、Validation担当数が0または複数ならCommitへ進めず拒否します。
        /// </summary>
        /// <param name="state">現在のCommand Componentへ書き込むSystem状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            foreach (RefRW<SimulationCommand> commandReference in
                     SystemAPI.Query<RefRW<SimulationCommand>>()
                         .WithAll<SimulationCommandActive>())
            {
                SimulationCommand command = commandReference.ValueRO;
                if (command.ValidationHandlerCount == 0
                    && command.Status == SimulationCommandStatus.Validating)
                {
                    command.Status = SimulationCommandStatus.Rejected;
                    command.FailureCode =
                        (int)SimulationCommandFailureCode.ValidationHandlerMissing;
                }
                else if (command.ValidationHandlerCount != 1)
                {
                    command.Status = SimulationCommandStatus.Rejected;
                    command.FailureCode =
                        (int)SimulationCommandFailureCode.MultipleValidationHandlers;
                }

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
    /// 各CommandのCommit Systemが記録した構造変更を、次のCommand検証より前に再生するECB Systemです。
    /// これにより同じセルへの連続Commandなどが、直前の確定済み状態を見て正しく競合判定できます。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareCommandProcessingSystemGroup))]
    [UpdateAfter(typeof(FarmwareCommandCommitSystemGroup))]
    [UpdateBefore(typeof(SimulationCommandCompletionSystem))]
    public partial class SimulationCommandEntityCommandBufferSystem : EntityCommandBufferSystem
    {
        /// <summary>
        /// 現在処理中のCommandを1件だけ取得し、ECBを再生してよい成功状態か確認するQueryです。
        /// </summary>
        private EntityQuery activeCommandQuery;

        /// <summary>
        /// unmanagedなISystemから、このECB Systemが同じCommand反復内で再生するBufferを作るためのSingletonです。
        /// </summary>
        public unsafe struct Singleton : IComponentData, IECBSingleton
        {
            /// <summary>ECB Systemが所有する未再生Buffer一覧へのポインターです。</summary>
            internal UnsafeList<EntityCommandBuffer>* PendingBuffers;

            /// <summary>作成するCommand Bufferが一時メモリを確保するAllocatorです。</summary>
            internal AllocatorManager.AllocatorHandle Allocator;

            /// <summary>
            /// 現在のCommand Commit用ECBを作り、このSystemの次回更新で自動再生される一覧へ追加します。
            /// このECBはメインスレッドのCommit Systemだけが使用し、Jobへ渡しません。
            /// </summary>
            /// <param name="world">ECBへ記録するEntityを所有するWorldです。</param>
            /// <returns>Commit Systemが構造変更を記録するEntityCommandBufferです。</returns>
            public EntityCommandBuffer CreateCommandBuffer(WorldUnmanaged world)
            {
                return EntityCommandBufferSystem.CreateCommandBuffer(
                    ref *PendingBuffers,
                    Allocator,
                    world);
            }

            /// <summary>
            /// EntityCommandBufferSystemが所有する未再生Buffer一覧をSingletonへ接続します。
            /// UnityのIECBSingleton契約から呼ばれる設定用APIで、ゲーム側から直接呼びません。
            /// </summary>
            /// <param name="buffers">このSystemが次回更新で再生するBuffer一覧です。</param>
            public void SetPendingBufferList(ref UnsafeList<EntityCommandBuffer> buffers)
            {
                PendingBuffers = (UnsafeList<EntityCommandBuffer>*)UnsafeUtility.AddressOf(
                    ref buffers);
            }

            /// <summary>
            /// ECB作成時に使用するAllocatorを設定します。
            /// UnityのIECBSingleton契約から呼ばれ、Bufferの寿命はECB Systemが管理します。
            /// </summary>
            /// <param name="allocator">ECBへ使用する標準Allocatorです。</param>
            public void SetAllocator(Allocator allocator)
            {
                Allocator = allocator;
            }

            /// <summary>
            /// ECB作成時に使用するAllocator Handleを設定します。
            /// UnityのIECBSingleton契約から呼ばれ、ゲーム側でDisposeしません。
            /// </summary>
            /// <param name="allocator">ECBへ使用するAllocator Handleです。</param>
            public void SetAllocator(AllocatorManager.AllocatorHandle allocator)
            {
                Allocator = allocator;
            }
        }

        /// <summary>
        /// ECB System作成時にSingletonを登録し、Commit SystemからBufferを取得できるようにします。
        /// </summary>
        protected override void OnCreate()
        {
            base.OnCreate();
            activeCommandQuery = GetEntityQuery(
                ComponentType.ReadOnly<SimulationCommand>(),
                ComponentType.ReadOnly<SimulationCommandActive>());
            this.RegisterSingleton<Singleton>(ref PendingBuffers, World.Unmanaged);
        }

        /// <summary>
        /// Commitが成功状態まで到達した場合だけ記録済みECBを再生します。
        /// Commit途中や拒否状態ではShouldPlaybackを無効にし、次のCommandへ部分変更を公開しません。
        /// 成否にかかわらず基底処理へ渡し、Job依存の完了、Buffer破棄、専用Allocatorの巻き戻しをUnity標準経路で行います。
        /// </summary>
        protected override void OnUpdate()
        {
            bool shouldPlayback = activeCommandQuery.CalculateEntityCount() == 1
                && activeCommandQuery.GetSingleton<SimulationCommand>().Status
                    == SimulationCommandStatus.Succeeded;
            if (!shouldPlayback)
            {
                // ShouldPlaybackは、ECB取得後に処理中断が決まった場合のためにUnityが公開している機能です。
                // Buffer自体は基底処理へ残し、再生だけを抑止することでAllocatorの巻き戻しを省略しません。
                for (int index = 0; index < PendingBuffers.Length; index++)
                {
                    EntityCommandBuffer commandBuffer = PendingBuffers[index];
                    if (commandBuffer.IsCreated)
                    {
                        commandBuffer.ShouldPlayback = false;
                        PendingBuffers[index] = commandBuffer;
                    }
                }
            }

            // EntityCommandBufferSystemの標準処理が、再生可否に関係なく依存完了、Dispose、Allocator.Rewindを行います。
            base.OnUpdate();
        }
    }

    /// <summary>
    /// CommitとECB再生後にCommand結果を最終化し、Presentationへ公開するSystemです。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareCommandProcessingSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(SimulationCommandEntityCommandBufferSystem))]
    public partial struct SimulationCommandCompletionSystem : ISystem
    {
        /// <summary>System作成時に追加の永続状態は準備しません。</summary>
        /// <param name="state">WorldのSystem状態です。</param>
        public void OnCreate(ref SystemState state)
        {
        }

        /// <summary>
        /// Commit担当不在や未完了を拒否へ変換し、結果を現在フレームのPresentationへ公開します。
        /// </summary>
        /// <param name="state">現在のCommandとCompletedタグへ書き込むSystem状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            foreach ((RefRW<SimulationCommand> commandReference, Entity commandEntity) in
                     SystemAPI.Query<RefRW<SimulationCommand>>()
                         .WithAll<SimulationCommandActive>()
                         .WithPresent<SimulationCommandCompleted>()
                         .WithEntityAccess())
            {
                SimulationCommand command = commandReference.ValueRO;
                if (command.Status == SimulationCommandStatus.ReadyToCommit)
                {
                    command.Status = SimulationCommandStatus.Rejected;
                    command.FailureCode =
                        (int)SimulationCommandFailureCode.CommitHandlerMissing;
                }
                else if (command.Status == SimulationCommandStatus.Committing)
                {
                    command.Status = SimulationCommandStatus.Rejected;
                    command.FailureCode =
                        (int)SimulationCommandFailureCode.CommitDidNotComplete;
                }
                else if (!command.IsCompleted)
                {
                    command.Status = SimulationCommandStatus.Rejected;
                    command.FailureCode =
                        (int)SimulationCommandFailureCode.InvalidCommandState;
                }

                commandReference.ValueRW = command;
                state.EntityManager.SetComponentEnabled<SimulationCommandCompleted>(
                    commandEntity,
                    true);
            }
        }

        /// <summary>World破棄時に呼ばれます。永続メモリを所有しないため解放処理はありません。</summary>
        /// <param name="state">破棄されるSystem状態です。</param>
        public void OnDestroy(ref SystemState state)
        {
        }
    }

    /// <summary>
    /// Command Processing GroupをQueue内の受付済み件数だけ反復させるRate Managerです。
    /// 各反復の開始前に1件だけActiveにし、前回反復の完了後に無効化して次の受付番号へ進みます。
    /// </summary>
    internal sealed class SimulationCommandRateManager : IRateManager, IDisposable
    {
        /// <summary>Queue Singletonを1件だけ取得するQueryです。</summary>
        private EntityQuery queueQuery;

        /// <summary>処理Tick番号を1件だけ取得するQueryです。</summary>
        private EntityQuery tickStateQuery;

        /// <summary>対象WorldへQueryを一度作成したかを表します。</summary>
        private bool queriesCreated;

        /// <summary>現在のQueue Bufferで次に確認する要素位置です。</summary>
        private int nextQueueIndex;

        /// <summary>現在の固定Tickですでに選択した最後の受付番号です。順序の破損検出に使います。</summary>
        private ulong lastReceiptNumber;

        /// <summary>直前のGroup反復でActiveにしたCommand Entityです。</summary>
        private Entity activeCommandEntity;

        /// <summary>
        /// IRateManager契約上の時間間隔です。このGroupは親固定Tickと同じWorld.Timeを使い、値をPushしません。
        /// </summary>
        public float Timestep { get; set; }

        /// <summary>
        /// 前回Commandを終了してからQueueの次要素を選び、処理対象があればGroupをもう1回更新します。
        /// </summary>
        /// <param name="group">Command処理Systemを子に持つGroupです。</param>
        /// <returns>受付済みCommandを1件処理する場合はtrue、Queueを処理し終えた場合はfalseです。</returns>
        public bool ShouldGroupUpdate(ComponentSystemGroup group)
        {
            EnsureQueries(group);
            EntityManager entityManager = group.EntityManager;
            FinishPreviousCommand(entityManager);

            if (queueQuery.CalculateEntityCount() != 1
                || tickStateQuery.CalculateEntityCount() != 1)
            {
                return false;
            }

            Entity queueEntity = queueQuery.GetSingletonEntity();
            DynamicBuffer<SimulationCommandQueueElement> queue =
                entityManager.GetBuffer<SimulationCommandQueueElement>(queueEntity);
            ulong currentTick = tickStateQuery
                .GetSingleton<SimulationTickState>()
                .CurrentTick;

            // 壊れた要素は他Entityを誤って変更せず拒否または読み飛ばし、後続の正常Commandは処理を継続します。
            while (nextQueueIndex < queue.Length)
            {
                SimulationCommandQueueElement element = queue[nextQueueIndex];
                nextQueueIndex++;

                if (!entityManager.Exists(element.CommandEntity)
                    || !entityManager.HasComponent<SimulationCommand>(element.CommandEntity)
                    || !entityManager.HasComponent<SimulationCommandActive>(element.CommandEntity)
                    || !entityManager.HasComponent<SimulationCommandCompleted>(element.CommandEntity))
                {
                    lastReceiptNumber = element.ReceiptNumber;
                    continue;
                }

                SimulationCommand command =
                    entityManager.GetComponentData<SimulationCommand>(element.CommandEntity);
                bool receiptOrderIsValid = element.ReceiptNumber > lastReceiptNumber;
                bool commandStateIsValid = command.Status == SimulationCommandStatus.Queued
                    && command.ReceiptNumber == element.ReceiptNumber;
                lastReceiptNumber = element.ReceiptNumber;

                command.ProcessedTick = currentTick;
                command.ValidationHandlerCount = 0;
                command.FailureCode = (int)SimulationCommandFailureCode.None;
                if (!receiptOrderIsValid || !commandStateIsValid)
                {
                    command.Status = SimulationCommandStatus.Rejected;
                    command.FailureCode =
                        (int)SimulationCommandFailureCode.InvalidCommandState;
                    entityManager.SetComponentData(element.CommandEntity, command);
                    entityManager.SetComponentEnabled<SimulationCommandCompleted>(
                        element.CommandEntity,
                        true);
                    continue;
                }

                // 受付後にWorld読込やPlayer削除が行われた可能性があるため、機能固有Validationへ渡す直前にも主体を確認します。
                // 古いCommandを新しいセーブへ適用しないことが目的であり、不一致時はActiveにせず結果だけを公開します。
                if (!WorldSessionAuthority.TryValidateActor(
                        entityManager,
                        command.Actor,
                        out _))
                {
                    command.Status = SimulationCommandStatus.Rejected;
                    command.FailureCode =
                        (int)SimulationCommandFailureCode.ActorIsNoLongerAuthorized;
                    entityManager.SetComponentData(element.CommandEntity, command);
                    entityManager.SetComponentEnabled<SimulationCommandCompleted>(
                        element.CommandEntity,
                        true);
                    continue;
                }

                command.Status = SimulationCommandStatus.Validating;
                entityManager.SetComponentData(element.CommandEntity, command);
                entityManager.SetComponentEnabled<SimulationCommandActive>(
                    element.CommandEntity,
                    true);
                activeCommandEntity = element.CommandEntity;
                return true;
            }

            // 全要素の処理後に一度だけBufferを空にし、先頭削除による要素シフトをCommandごとに発生させません。
            if (nextQueueIndex > 0)
            {
                queue.Clear();
                nextQueueIndex = 0;
                lastReceiptNumber = 0;
            }

            return false;
        }

        /// <summary>
        /// Rate Managerが所有するQueryをWorld破棄前に解放します。
        /// </summary>
        public void Dispose()
        {
            if (!queriesCreated)
            {
                return;
            }

            queueQuery.Dispose();
            tickStateQuery.Dispose();
            queriesCreated = false;
            activeCommandEntity = Entity.Null;
        }

        /// <summary>
        /// Queueと現在TickのQueryを、対象Groupと同じWorldへ一度だけ作成します。
        /// </summary>
        /// <param name="group">Queryの所有先となるWorldを持つCommand Groupです。</param>
        private void EnsureQueries(ComponentSystemGroup group)
        {
            if (queriesCreated)
            {
                return;
            }

            queueQuery = group.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<SimulationCommandQueueState>(),
                ComponentType.ReadWrite<SimulationCommandQueueElement>());
            tickStateQuery = group.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<SimulationTickState>());
            queriesCreated = true;
        }

        /// <summary>
        /// 直前の反復で処理したCommandをActive対象から外します。
        /// 子Systemが結果確定に到達しなかった場合も、次のCommandへ不完全なActive状態を持ち越しません。
        /// </summary>
        /// <param name="entityManager">直前Commandを所有するWorldのEntityManagerです。</param>
        private void FinishPreviousCommand(EntityManager entityManager)
        {
            if (activeCommandEntity == Entity.Null)
            {
                return;
            }

            if (entityManager.Exists(activeCommandEntity)
                && entityManager.HasComponent<SimulationCommand>(activeCommandEntity))
            {
                SimulationCommand command =
                    entityManager.GetComponentData<SimulationCommand>(activeCommandEntity);
                if (!command.IsCompleted)
                {
                    command.Status = SimulationCommandStatus.Rejected;
                    command.FailureCode =
                        (int)SimulationCommandFailureCode.InvalidCommandState;
                    entityManager.SetComponentData(activeCommandEntity, command);
                }

                if (entityManager.HasComponent<SimulationCommandActive>(activeCommandEntity))
                {
                    entityManager.SetComponentEnabled<SimulationCommandActive>(
                        activeCommandEntity,
                        false);
                }

                if (entityManager.HasComponent<SimulationCommandCompleted>(activeCommandEntity))
                {
                    entityManager.SetComponentEnabled<SimulationCommandCompleted>(
                        activeCommandEntity,
                        true);
                }
            }

            activeCommandEntity = Entity.Null;
        }
    }
}
