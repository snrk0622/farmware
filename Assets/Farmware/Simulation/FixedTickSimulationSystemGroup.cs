// このファイルは、描画フレーム時間を固定間隔へ変換し、Farmwareのゲームルールを一定回数だけ更新するECS System Groupを定義します。
// 固定Tick設定、一時停止、現在Tick、非同期チャンク生成の準備状態を読み、重いJobを描画スレッドで待たず論理Tickだけを延期します。

using System;
using Unity.Core;
using Unity.Entities;

namespace Farmware.Simulation
{
    /// <summary>
    /// 生産、物流、動物など、ゲーム内時間で進むSystemをまとめる固定Tick Groupです。
    /// 描画フレームごとの準備が終わった後に、蓄積時間と追いつき上限から0回以上更新します。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareSimulationSystemGroup), OrderLast = true)]
    public partial class FarmwareFixedTickSimulationSystemGroup : ComponentSystemGroup
    {
        /// <summary>
        /// Group作成時にFarmware専用の時間管理を接続します。
        /// Unity標準の無制限な追いつきではなく、一時停止と最大追いつき回数を共通規則として適用します。
        /// </summary>
        public FarmwareFixedTickSimulationSystemGroup()
        {
            RateManager = new FarmwareFixedTickRateManager();
        }

        /// <summary>
        /// World破棄時に、Rate Managerが保持するEntityQueryを先に解放します。
        /// QueryはこのGroupのWorldだけを参照するため、別Worldへ引き継ぎません。
        /// </summary>
        protected override void OnDestroy()
        {
            if (RateManager is FarmwareFixedTickRateManager fixedTickRateManager)
            {
                fixedTickRateManager.Dispose();
            }

            RateManager = null;
            base.OnDestroy();
        }
    }

    /// <summary>
    /// 描画フレームの経過時間を固定Tickへ変換する、FarmwareFixedTickSimulationSystemGroup専用のRate Managerです。
    /// Worldごとに所有され、一時停止中、非同期準備中、追いつき上限を超えた整数Tickを無制限に蓄積しません。
    /// </summary>
    internal sealed class FarmwareFixedTickRateManager : IRateManager, IDisposable
    {
        // 浮動小数点の0.1秒などを加算した際、Tick境界のごく小さな誤差で1回不足しないようにする比較余白です。
        // Unityの描画DeltaTimeはfloatなので、doubleへ変換しても元の丸め誤差が残ります。
        // 0.1秒に対して100万分の1という十分小さい余白だけを認め、先行した不足分は負の時間として次フレームへ残します。
        private const double TickBoundaryTolerance = 0.0000001d;

        // 固定Tick設定を1件だけ取得するQueryです。初回更新時に作成し、Group破棄時にDisposeします。
        private EntityQuery configurationQuery;

        // 現在Tickを1件だけ取得し、固定Tick内のElapsedTime初期値を合わせるためのQueryです。
        private EntityQuery tickStateQuery;

        // 一時停止状態を毎描画フレーム読み取るQueryです。停止中は経過時間をaccumulatedSecondsへ加えません。
        private EntityQuery pauseStateQuery;

        // Chunk生成が未完了かを読む任意Singleton Queryです。存在しない最小テストWorldは準備完了として扱います。
        private EntityQuery readinessStateQuery;

        // Queryを同じWorldに対して一度だけ作成したかを表します。Worldをまたいだ再利用は行いません。
        private bool queriesCreated;

        // World開始時の設定を固定済みかを表します。同じWorldの途中でTickレートを変えないために使用します。
        private bool configurationLocked;

        // 1 Tickの秒数です。既定値は10 Tick/秒に対応する0.1秒で、初回設定読取後はWorld寿命中固定です。
        private double fixedDeltaTimeSeconds =
            1d / FixedTickConfiguration.DefaultTicksPerSecond;

        // 1描画フレームで実行する最大Tick数です。初回設定読取後はWorld寿命中固定です。
        private uint maximumTicksPerFrame =
            FixedTickConfiguration.DefaultMaximumTicksPerFrame;

        // まだ固定Tickへ変換されていない通常進行中の秒数です。
        // 許容誤差でTickを少し先行した場合だけ負になり、その不足時間を次フレームで返すための負債として保持します。
        private double accumulatedSeconds;

        // 現在の描画フレームで、この後に実行する予定の固定Tick数です。
        private uint remainingTicksThisFrame;

        // 生成待ちに入った時点で到来済みだったTickを1件だけ保持し、準備完了後に同じ論理順で再開します。
        // 2件目以降の壁時計遅延は捨て、生成完了直後に大量追いつきが起きないようにします。
        private bool deferredTickPending;

        // 固定Tick Groupへ一時的なWorld.TimeをPushしているかを表し、次の判定時に必ずPopするために使います。
        private bool timePushed;

        // 次に開始するTickに対応するSimulation上の経過秒です。描画側のElapsedTimeではなく、固定Tickだけで増加します。
        private double simulationElapsedSeconds;

        /// <summary>
        /// 子Systemへ渡す固定DeltaTimeです。秒単位で、初回設定読取後はWorldの実行途中で変更しません。
        /// </summary>
        public float Timestep
        {
            get => (float)fixedDeltaTimeSeconds;
            set
            {
                // IRateManager契約上setterが必要です。0、NaN、無限大は採用せず、現在値を維持します。
                if (value > 0f && !float.IsNaN(value) && !float.IsInfinity(value))
                {
                    fixedDeltaTimeSeconds = value;
                }
            }
        }

        /// <summary>
        /// 今回の描画フレームで固定Tick Groupをもう1回更新する必要があるかを判定します。
        /// 最初の呼び出しで経過時間をTick数へ変換し、以後は上限回数まで固定DeltaTimeをPushします。
        /// </summary>
        /// <param name="group">更新回数と一時的なWorld.Timeを制御する固定Tick Groupです。</param>
        /// <returns>子Systemを1回更新する場合はtrue、今回の描画フレームを終了する場合はfalseです。</returns>
        public bool ShouldGroupUpdate(ComponentSystemGroup group)
        {
            if (timePushed)
            {
                // 直前の固定Tick用時間を戻してから、同じ描画フレーム内の次Tickを判断します。
                group.World.PopTime();
                timePushed = false;

                // 直前Tickの公開Systemが次の生成バッチを投入した場合、残りの追いつきTickを同じフレームで
                // 実行すると再び同期点へ到達します。到来済みTickを1件だけ延期し、描画へ制御を返します。
                if (IsFixedTickBlocked())
                {
                    CaptureOneDeferredTickAndDropAdditionalBacklog();
                    remainingTicksThisFrame = 0;
                    return false;
                }

                if (remainingTicksThisFrame > 0)
                {
                    return PushNextTickTime(group);
                }

                return false;
            }

            EnsureQueries(group);
            if (!TryLockConfigurationAndReadPause(out bool isPaused))
            {
                // 初期化Singletonがまだそろっていないフレームでは、不完全な設定でSimulationを進めません。
                return false;
            }

            if (isPaused)
            {
                // 停止中のDeltaTimeを加えないことで、再開時に停止時間分を追いつく挙動を防ぎます。
                remainingTicksThisFrame = 0;
                return false;
            }

            float frameDeltaTime = group.World.Time.DeltaTime;
            if (frameDeltaTime > 0f
                && !float.IsNaN(frameDeltaTime)
                && !float.IsInfinity(frameDeltaTime))
            {
                accumulatedSeconds += frameDeltaTime;
            }

            if (IsFixedTickBlocked())
            {
                // Workerの完了をMain Threadで待たず、この描画フレームはGame ViewとPresentationへ返します。
                // 到来済みTickは1件だけ保持し、生成中の長い壁時計時間を後で一括追いつきしません。
                CaptureOneDeferredTickAndDropAdditionalBacklog();
                remainingTicksThisFrame = 0;
                return false;
            }

            if (deferredTickPending)
            {
                // 延期Tick分の時間はaccumulatedSecondsへ1 Tick残してあるため、通常と同じPush処理で消費します。
                deferredTickPending = false;
                remainingTicksThisFrame = 1;
                return PushNextTickTime(group);
            }

            PlanTicksForCurrentFrame();
            if (remainingTicksThisFrame == 0)
            {
                return false;
            }

            return PushNextTickTime(group);
        }

        /// <summary>
        /// Rate Managerが作成したEntityQueryを解放します。
        /// FarmwareFixedTickSimulationSystemGroupのOnDestroyからWorldが有効な間に1回呼び出します。
        /// </summary>
        public void Dispose()
        {
            if (!queriesCreated)
            {
                return;
            }

            configurationQuery.Dispose();
            tickStateQuery.Dispose();
            pauseStateQuery.Dispose();
            readinessStateQuery.Dispose();
            queriesCreated = false;
        }

        /// <summary>
        /// 固定Tick用Singletonを取得するQueryを、対象Groupと同じWorldに対して一度だけ作成します。
        /// </summary>
        /// <param name="group">Queryの所有先となるWorldを持つ固定Tick Groupです。</param>
        private void EnsureQueries(ComponentSystemGroup group)
        {
            if (queriesCreated)
            {
                return;
            }

            configurationQuery = group.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<FixedTickConfiguration>());
            tickStateQuery = group.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<SimulationTickState>());
            pauseStateQuery = group.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<SimulationPauseState>());
            readinessStateQuery = group.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<FixedTickReadinessState>());
            queriesCreated = true;
        }

        /// <summary>
        /// 任意の準備状態Singletonを読み、Chunk生成中なら固定Tickを開始しないようにします。
        /// </summary>
        /// <returns>Singletonが1件あり、Chunk生成Jobの完了待ちを示している場合はtrueです。</returns>
        private bool IsFixedTickBlocked()
        {
            // 古いテストWorldや機能単体WorldにSingletonが無い場合は、後方互換のため準備完了として扱います。
            return readinessStateQuery.CalculateEntityCount() == 1
                && readinessStateQuery.GetSingleton<FixedTickReadinessState>().IsBlocked;
        }

        /// <summary>
        /// 現在までに到来した整数Tickを最大1件だけ延期し、それを超える生成待ち時間を破棄します。
        /// </summary>
        private void CaptureOneDeferredTickAndDropAdditionalBacklog()
        {
            double availableTickCount = Math.Floor(
                (accumulatedSeconds + TickBoundaryTolerance) / fixedDeltaTimeSeconds);
            if (availableTickCount < 1d)
            {
                return;
            }

            deferredTickPending = true;
            // 延期した1 Tick分は残します。許容誤差でわずかに先行した負債も、通常経路と同じく端数へ保持します。
            accumulatedSeconds -= (availableTickCount - 1d) * fixedDeltaTimeSeconds;
        }

        /// <summary>
        /// 必要なSingletonが各1件あることを確認し、初回だけ設定と現在TickをRate Managerへ固定します。
        /// 一時停止状態はプレイヤー操作で変化するため、描画フレームごとに読み直します。
        /// </summary>
        /// <param name="isPaused">読取成功時に、現在一時停止中かを返します。</param>
        /// <returns>設定、現在Tick、一時停止状態が各1件存在する場合はtrueです。</returns>
        private bool TryLockConfigurationAndReadPause(out bool isPaused)
        {
            isPaused = false;
            if (configurationQuery.CalculateEntityCount() != 1
                || tickStateQuery.CalculateEntityCount() != 1
                || pauseStateQuery.CalculateEntityCount() != 1)
            {
                return false;
            }

            if (!configurationLocked)
            {
                FixedTickConfiguration configuration =
                    configurationQuery.GetSingleton<FixedTickConfiguration>().Normalized();
                SimulationTickState tickState =
                    tickStateQuery.GetSingleton<SimulationTickState>();

                fixedDeltaTimeSeconds = 1d / configuration.TicksPerSecond;
                maximumTicksPerFrame = configuration.MaximumTicksPerFrame;
                simulationElapsedSeconds = tickState.CurrentTick * fixedDeltaTimeSeconds;
                configurationLocked = true;
            }

            isPaused = pauseStateQuery.GetSingleton<SimulationPauseState>().IsPaused;
            return true;
        }

        /// <summary>
        /// 蓄積秒数から今回実行するTick数を求め、最大追いつき回数を超えた整数Tick分だけを破棄します。
        /// Tick未満の端数は次フレームへ残し、許容誤差で先行した不足時間も負の端数として失わないようにします。
        /// </summary>
        private void PlanTicksForCurrentFrame()
        {
            double availableTickCount = Math.Floor(
                (accumulatedSeconds + TickBoundaryTolerance) / fixedDeltaTimeSeconds);
            if (availableTickCount < 1d)
            {
                remainingTicksThisFrame = 0;
                return;
            }

            double ticksToRunAsDouble = Math.Min(availableTickCount, maximumTicksPerFrame);
            remainingTicksThisFrame = (uint)ticksToRunAsDouble;

            if (availableTickCount <= maximumTicksPerFrame)
            {
                return;
            }

            // 上限超過分はゲーム内時間を遅らせる方針で破棄します。
            // 余りがわずかに負なら、許容誤差で先行した時間の負債なので、0へ丸めず次フレームへ引き継ぎます。
            double fractionalRemainder =
                accumulatedSeconds - (availableTickCount * fixedDeltaTimeSeconds);
            accumulatedSeconds =
                (remainingTicksThisFrame * fixedDeltaTimeSeconds)
                + fractionalRemainder;
        }

        /// <summary>
        /// 1 Tick分の蓄積時間を消費し、子Systemが見るWorld.Timeを固定DeltaTimeへ一時的に差し替えます。
        /// </summary>
        /// <param name="group">時間をPushし、直後に子Systemを更新する固定Tick Groupです。</param>
        /// <returns>常にtrueを返し、ComponentSystemGroupへ1回の更新を指示します。</returns>
        private bool PushNextTickTime(ComponentSystemGroup group)
        {
            // 許容誤差で境界直前のTickを実行した場合は負数になります。
            // この不足分を消すとフレーム分割によってTick数が変わるため、次フレームへ負債として残します。
            accumulatedSeconds -= fixedDeltaTimeSeconds;
            remainingTicksThisFrame--;
            simulationElapsedSeconds += fixedDeltaTimeSeconds;

            group.World.PushTime(new TimeData(
                simulationElapsedSeconds,
                (float)fixedDeltaTimeSeconds));
            timePushed = true;
            return true;
        }
    }
}
