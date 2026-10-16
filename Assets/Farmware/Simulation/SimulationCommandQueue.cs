// このファイルは、InputからSimulationへ渡すゲーム用Commandの共通データと受付APIを定義します。
// Commandの内容は機能固有Componentへ分離し、この共通層は受付順、一時停止、処理結果の寿命だけを管理します。

using Unity.Entities;

namespace Farmware.Simulation
{
    /// <summary>
    /// Commandが受付後にどの処理段階まで進んだかを表します。
    /// PresentationはSucceededまたはRejectedだけを最終結果として読み、それ以外は固定Tick内の一時状態として扱います。
    /// </summary>
    public enum SimulationCommandStatus : byte
    {
        /// <summary>Queueへ追加され、まだ固定Tickで選択されていない状態です。</summary>
        Queued = 0,

        /// <summary>機能固有のValidation Systemが、状態を変更せず実行可否を調べる段階です。</summary>
        Validating = 1,

        /// <summary>検証に成功し、機能固有のCommit Systemが変更を確定できる状態です。</summary>
        ReadyToCommit = 2,

        /// <summary>Commit Systemが処理権を取得し、変更の確定中である状態です。</summary>
        Committing = 3,

        /// <summary>検証とCommitが完了し、ゲーム状態へ変更を反映した最終状態です。</summary>
        Succeeded = 4,

        /// <summary>検証失敗または共通処理の不整合により、変更を確定しなかった最終状態です。</summary>
        Rejected = 5
    }

    /// <summary>
    /// 共通Command基盤自身が検出する失敗理由です。
    /// 機能固有の失敗理由は<see cref="SimulationCommand.FirstFeatureFailureCode"/>以上の値を使用します。
    /// </summary>
    public enum SimulationCommandFailureCode
    {
        /// <summary>失敗していない、またはまだ失敗理由が確定していない状態です。</summary>
        None = 0,

        /// <summary>Commandを担当するValidation Systemが存在しません。</summary>
        ValidationHandlerMissing = 1,

        /// <summary>同じCommandを複数のValidation Systemが担当し、処理主体を一意に決められません。</summary>
        MultipleValidationHandlers = 2,

        /// <summary>検証成功後にCommandを確定するCommit Systemが存在しません。</summary>
        CommitHandlerMissing = 3,

        /// <summary>Commit Systemが処理を開始しましたが、成功状態まで完了しませんでした。</summary>
        CommitDidNotComplete = 4,

        /// <summary>Queue内のEntity、受付番号、または処理状態が共通契約を満たしていません。</summary>
        InvalidCommandState = 5
    }

    /// <summary>
    /// すべてのCommand Entityが持つ受付番号、処理状態、結果をまとめた共通Componentです。
    /// 機能固有SystemだけがTransaction APIを通して状態を進め、InputやPresentationは直接変更しません。
    /// </summary>
    public struct SimulationCommand : IComponentData
    {
        /// <summary>
        /// 機能固有の失敗理由へ割り当てる最初の数値です。
        /// 0から1023までは共通基盤用に予約し、各機能は1024以上のenum値をFailureCodeへ保存します。
        /// </summary>
        public const int FirstFeatureFailureCode = 1024;

        /// <summary>
        /// World内で受付順を一意に表す番号です。0は無効で、1から単調増加し、失敗後も再利用しません。
        /// </summary>
        public ulong ReceiptNumber;

        /// <summary>
        /// Commandの現在段階です。受付時のQueuedから始まり、同じ固定Tick内でSucceededまたはRejectedへ進みます。
        /// </summary>
        public SimulationCommandStatus Status;

        /// <summary>
        /// Rejectedになった理由です。成功時は0、共通失敗はSimulationCommandFailureCode、機能固有失敗は1024以上です。
        /// </summary>
        public int FailureCode;

        /// <summary>
        /// Commandを処理した固定Tick番号です。Queue内で未処理の間は0で、選択時にSimulationTickStateから設定します。
        /// </summary>
        public ulong ProcessedTick;

        /// <summary>
        /// このCommandを担当すると宣言したValidation System数です。
        /// 1件だけを正常とし、0件や複数件による曖昧な処理を共通層で拒否します。
        /// </summary>
        public byte ValidationHandlerCount;

        /// <summary>CommandがPresentationへ公開できる成功または失敗の最終状態かを返します。</summary>
        public readonly bool IsCompleted =>
            Status == SimulationCommandStatus.Succeeded
            || Status == SimulationCommandStatus.Rejected;
    }

    /// <summary>
    /// 現在処理中のCommand Entityだけで有効になるタグです。
    /// Enableable Componentを使い、Commandごとの処理開始と終了でArchetypeを移動させません。
    /// </summary>
    public struct SimulationCommandActive : IComponentData, IEnableableComponent
    {
    }

    /// <summary>
    /// 処理結果を現在の描画フレーム中だけ公開するCommand Entityで有効になるタグです。
    /// 次の描画フレーム先頭でCleanup SystemがEntityを破棄するため、複数固定Tickが同じフレームにあっても結果を失いません。
    /// </summary>
    public struct SimulationCommandCompleted : IComponentData, IEnableableComponent
    {
    }

    /// <summary>
    /// 受付済みCommand Entityを順番どおり保持するSingleton Dynamic Buffer要素です。
    /// 受付番号をEntity側にも重複保持し、壊れた参照や順序不整合を処理前に検出できるようにします。
    /// </summary>
    [InternalBufferCapacity(16)]
    public struct SimulationCommandQueueElement : IBufferElementData
    {
        /// <summary>共通SimulationCommandと機能固有Componentを持つ一時Command Entityです。</summary>
        public Entity CommandEntity;

        /// <summary>追加時の受付番号です。Command EntityのReceiptNumberと一致しなければ処理しません。</summary>
        public ulong ReceiptNumber;
    }

    /// <summary>
    /// 次に発行する受付番号と上限到達状態を保持するSingleton Componentです。
    /// Commandはセーブ対象ではないため、この状態もWorldの実行中だけ保持し、ロード後は1から開始します。
    /// </summary>
    public struct SimulationCommandQueueState : IComponentData
    {
        /// <summary>次に受理するCommandへ付ける1以上の番号です。</summary>
        public ulong NextReceiptNumber;

        /// <summary>0なら受付可能、1ならulong.MaxValueまで発行して受付を停止した状態です。</summary>
        public byte ExhaustedValue;

        /// <summary>受付番号をこれ以上発行できない状態かを返します。</summary>
        public readonly bool IsExhausted => ExhaustedValue != 0;

        /// <summary>
        /// 新しいWorld用に受付番号1から始まる初期状態を作成します。
        /// </summary>
        /// <returns>まだ受付番号を発行していないQueue状態です。</returns>
        public static SimulationCommandQueueState CreateInitial()
        {
            return new SimulationCommandQueueState
            {
                NextReceiptNumber = 1,
                ExhaustedValue = 0
            };
        }
    }

    /// <summary>
    /// Inputまたはテストが機能固有Command Componentを共通Queueへ安全に追加するための受付APIです。
    /// 一時停止、Singleton不整合、受付番号上限ではEntityも受付番号も変更せずfalseを返します。
    /// </summary>
    public static class SimulationCommandQueue
    {
        /// <summary>
        /// 機能固有Componentを持つCommand Entityを作成し、単調増加する受付番号でQueue末尾へ追加します。
        /// 呼び出しはInput境界のメインスレッドで行い、ゲーム状態そのものはこの時点では変更しません。
        /// </summary>
        /// <typeparam name="TCommand">建築、採取などの依頼内容を保持するunmanaged Component型です。</typeparam>
        /// <param name="entityManager">Command Entity、Queue Singleton、一時停止状態を所有するWorldのEntityManagerです。</param>
        /// <param name="commandData">新しいCommand Entityへコピーする機能固有データです。</param>
        /// <param name="commandEntity">成功時に作成したCommand Entity、失敗時にEntity.Nullを返します。</param>
        /// <param name="receiptNumber">成功時に発行した1以上の受付番号、失敗時に0を返します。</param>
        /// <returns>一時停止中ではなく、QueueへCommandを完全に追加できた場合はtrueです。</returns>
        public static bool TryEnqueue<TCommand>(
            EntityManager entityManager,
            TCommand commandData,
            out Entity commandEntity,
            out ulong receiptNumber)
            where TCommand : unmanaged, IComponentData
        {
            commandEntity = Entity.Null;
            receiptNumber = 0;

            EntityQuery queueQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadWrite<SimulationCommandQueueState>(),
                ComponentType.ReadWrite<SimulationCommandQueueElement>());
            EntityQuery pauseQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<SimulationPauseState>());

            try
            {
                // Singletonが欠けている、または複数あるWorldでは、どのQueueを正とするか推測せず受付を拒否します。
                if (queueQuery.CalculateEntityCount() != 1
                    || pauseQuery.CalculateEntityCount() != 1)
                {
                    return false;
                }

                if (pauseQuery.GetSingleton<SimulationPauseState>().IsPaused)
                {
                    // 停止中は予約も行わない仕様なので、受付番号を消費する前に終了します。
                    return false;
                }

                Entity queueEntity = queueQuery.GetSingletonEntity();
                SimulationCommandQueueState queueState =
                    entityManager.GetComponentData<SimulationCommandQueueState>(queueEntity);
                if (!IsValidState(queueState) || queueState.IsExhausted)
                {
                    return false;
                }

                receiptNumber = queueState.NextReceiptNumber;
                commandEntity = entityManager.CreateEntity(
                    ComponentType.ReadWrite<SimulationCommand>(),
                    ComponentType.ReadWrite<SimulationCommandActive>(),
                    ComponentType.ReadWrite<SimulationCommandCompleted>(),
                    ComponentType.ReadWrite<TCommand>());
                entityManager.SetComponentData(
                    commandEntity,
                    new SimulationCommand
                    {
                        ReceiptNumber = receiptNumber,
                        Status = SimulationCommandStatus.Queued,
                        FailureCode = (int)SimulationCommandFailureCode.None,
                        ProcessedTick = 0,
                        ValidationHandlerCount = 0
                    });
                entityManager.SetComponentData(commandEntity, commandData);

                // ActiveとCompletedは状態切替用タグとして最初から持たせ、処理中の構造変更を避けます。
                entityManager.SetComponentEnabled<SimulationCommandActive>(commandEntity, false);
                entityManager.SetComponentEnabled<SimulationCommandCompleted>(commandEntity, false);

                DynamicBuffer<SimulationCommandQueueElement> queue =
                    entityManager.GetBuffer<SimulationCommandQueueElement>(queueEntity);
                queue.Add(new SimulationCommandQueueElement
                {
                    CommandEntity = commandEntity,
                    ReceiptNumber = receiptNumber
                });

                // ulong.MaxValue自体は最後の有効な受付番号として1回使用し、0へ周回させません。
                if (queueState.NextReceiptNumber == ulong.MaxValue)
                {
                    queueState.ExhaustedValue = 1;
                }
                else
                {
                    queueState.NextReceiptNumber++;
                }

                entityManager.SetComponentData(queueEntity, queueState);
                return true;
            }
            finally
            {
                // 一時Queryはこの受付呼び出しだけが所有するため、成功・失敗の両方で必ず解放します。
                queueQuery.Dispose();
                pauseQuery.Dispose();
            }
        }

        /// <summary>
        /// Queue状態が0除外と上限到達表現の共通規則を満たすか確認します。
        /// </summary>
        /// <param name="state">正規形かを確認する現在の受付番号状態です。</param>
        /// <returns>次回番号と上限状態を安全に使用できる場合はtrueです。</returns>
        private static bool IsValidState(SimulationCommandQueueState state)
        {
            if (state.NextReceiptNumber == 0 || state.ExhaustedValue > 1)
            {
                return false;
            }

            return !state.IsExhausted || state.NextReceiptNumber == ulong.MaxValue;
        }
    }

    /// <summary>
    /// 機能固有のValidation／Commit Systemが共通状態を正しい順序で進めるための補助APIです。
    /// Validation中はゲーム状態を変更せず、TryBeginCommitが成功した後だけCommit処理を実行します。
    /// </summary>
    public static class SimulationCommandTransaction
    {
        /// <summary>
        /// Validation SystemがCommandの担当権を1回だけ宣言します。
        /// 複数Systemが同じCommandを担当した場合は曖昧な処理を続けず、共通失敗として拒否します。
        /// </summary>
        /// <param name="command">現在処理中の共通Command状態です。</param>
        /// <returns>このSystemが唯一のValidation担当として検証を続けてよい場合はtrueです。</returns>
        public static bool TryClaimValidation(ref SimulationCommand command)
        {
            bool canClaim = command.Status == SimulationCommandStatus.Validating
                || command.Status == SimulationCommandStatus.ReadyToCommit
                || command.Status == SimulationCommandStatus.Rejected;
            if (!canClaim)
            {
                return false;
            }

            if (command.ValidationHandlerCount < byte.MaxValue)
            {
                command.ValidationHandlerCount++;
            }

            if (command.ValidationHandlerCount != 1)
            {
                command.Status = SimulationCommandStatus.Rejected;
                command.FailureCode =
                    (int)SimulationCommandFailureCode.MultipleValidationHandlers;
                return false;
            }

            return command.Status == SimulationCommandStatus.Validating;
        }

        /// <summary>
        /// 状態を変更しない全検証が成功したことを記録し、Commit可能な段階へ進めます。
        /// </summary>
        /// <param name="command">唯一のValidation担当が検証した共通Command状態です。</param>
        public static void AcceptValidation(ref SimulationCommand command)
        {
            if (command.Status != SimulationCommandStatus.Validating
                || command.ValidationHandlerCount != 1)
            {
                return;
            }

            command.Status = SimulationCommandStatus.ReadyToCommit;
            command.FailureCode = (int)SimulationCommandFailureCode.None;
        }

        /// <summary>
        /// 機能固有の検証失敗を記録し、Commitを実行しない最終状態へ進めます。
        /// </summary>
        /// <param name="command">唯一のValidation担当が検証した共通Command状態です。</param>
        /// <param name="failureCode">1024以上で定義した機能固有の失敗理由です。</param>
        public static void RejectValidation(
            ref SimulationCommand command,
            int failureCode)
        {
            if (command.Status != SimulationCommandStatus.Validating
                || command.ValidationHandlerCount != 1)
            {
                return;
            }

            command.Status = SimulationCommandStatus.Rejected;
            command.FailureCode = failureCode >= SimulationCommand.FirstFeatureFailureCode
                ? failureCode
                : (int)SimulationCommandFailureCode.InvalidCommandState;
        }

        /// <summary>
        /// 検証済みCommandのCommit処理権を取得します。
        /// 戻り値がfalseの場合は、資材消費、Component変更、ECB記録などの副作用を行ってはいけません。
        /// trueの場合もゲーム状態は直接変更せず、Component値更新を含む全変更をCommand専用ECBへ記録します。
        /// CompleteCommitへ到達しなければ共通ECB Systemが記録内容を再生せず破棄します。
        /// </summary>
        /// <param name="command">検証済みであることを確認する共通Command状態です。</param>
        /// <returns>Commit処理を開始してよい場合はtrueです。</returns>
        public static bool TryBeginCommit(ref SimulationCommand command)
        {
            if (command.Status != SimulationCommandStatus.ReadyToCommit)
            {
                return false;
            }

            command.Status = SimulationCommandStatus.Committing;
            return true;
        }

        /// <summary>
        /// Commitのすべての検証済み変更をECBへ記録できたことを記録します。
        /// ECB自体は同じCommand反復内の共通Playback Systemが次のCommandより前に反映します。
        /// </summary>
        /// <param name="command">TryBeginCommitで処理権を取得した共通Command状態です。</param>
        public static void CompleteCommit(ref SimulationCommand command)
        {
            if (command.Status != SimulationCommandStatus.Committing)
            {
                return;
            }

            command.Status = SimulationCommandStatus.Succeeded;
            command.FailureCode = (int)SimulationCommandFailureCode.None;
        }
    }
}
