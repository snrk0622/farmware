// このファイルは、Farmware全体で共有する固定Tick設定、現在Tick、一時停止、非同期準備状態のECSデータを定義します。
// 固定Tick Groupと後続の生産・物流・動物SystemはこれらのSingletonを共有し、描画フレーム時間をゲーム結果の正にしません。

using Unity.Entities;

namespace Farmware.Simulation
{
    /// <summary>
    /// 1秒当たりの固定Tick数と、1描画フレームで実行できる最大Tick数を保持する共通設定です。
    /// World作成時にSimulationTimeInitializationSystemが1件だけ作成し、同じWorldの実行途中では変更しません。
    /// </summary>
    public struct FixedTickConfiguration : IComponentData
    {
        /// <summary>性能計測前に使用する暫定の固定Tick数です。単位はTick/秒です。</summary>
        public const uint DefaultTicksPerSecond = 10;

        /// <summary>1描画フレームで許可する暫定の最大追いつきTick数です。単位はTick/フレームです。</summary>
        public const uint DefaultMaximumTicksPerFrame = 4;

        /// <summary>
        /// 1秒当たりに進めるSimulation Tick数です。World開始時に固定Tick Groupが読み取り、そのWorldの寿命中は固定します。
        /// </summary>
        public uint TicksPerSecond;

        /// <summary>
        /// 一時的な描画遅延から追いつくため、1描画フレーム内で実行してよい最大Tick数です。
        /// 上限を超えた整数Tick分の遅延は破棄し、処理落ちが連鎖しないようにします。
        /// </summary>
        public uint MaximumTicksPerFrame;

        /// <summary>
        /// 性能試作前の既定値である10 Tick/秒、最大4 Tick/フレームの設定を作成します。
        /// </summary>
        /// <returns>新しいWorldへ設定する暫定の固定Tick設定です。</returns>
        public static FixedTickConfiguration CreateDefault()
        {
            return new FixedTickConfiguration
            {
                TicksPerSecond = DefaultTicksPerSecond,
                MaximumTicksPerFrame = DefaultMaximumTicksPerFrame
            };
        }

        /// <summary>
        /// 0が指定された項目だけを安全な既定値へ戻します。
        /// 0のまま除算したり、1 Tickも実行できないWorldを作ったりしないために、固定Tick Groupが初回読取時に使用します。
        /// </summary>
        /// <returns>各項目が1以上であることを保証した設定です。</returns>
        public readonly FixedTickConfiguration Normalized()
        {
            return new FixedTickConfiguration
            {
                TicksPerSecond = TicksPerSecond == 0
                    ? DefaultTicksPerSecond
                    : TicksPerSecond,
                MaximumTicksPerFrame = MaximumTicksPerFrame == 0
                    ? DefaultMaximumTicksPerFrame
                    : MaximumTicksPerFrame
            };
        }
    }

    /// <summary>
    /// 固定Tick Groupが開始したSimulation Tick数と、現在実行中のTick番号を保持するゲーム状態です。
    /// Group先頭のSimulationTickAdvanceSystemだけが加算し、同じTickの後続System、セーブ、診断表示が読み取ります。
    /// </summary>
    public struct SimulationTickState : IComponentData
    {
        /// <summary>
        /// World開始時の0から各固定Tickの先頭で1ずつ増える番号です。単位はTickで、Worldのプレイ期間中保持します。
        /// </summary>
        public ulong CurrentTick;
    }

    /// <summary>
    /// 固定Tick Simulationを通常進行させるか一時停止するかを保持する制御状態です。
    /// 将来のInputまたは全画面メニューが変更し、固定Tick Groupは毎描画フレームの開始時に読み取ります。
    /// </summary>
    public struct SimulationPauseState : IComponentData
    {
        /// <summary>
        /// 0なら通常進行、1なら一時停止です。ECS上で大きさが明確なunmanaged値としてWorldの寿命中保持します。
        /// </summary>
        public byte Value;

        /// <summary>現在、固定Tick Simulationが一時停止中かを返します。</summary>
        public readonly bool IsPaused => Value != 0;

        /// <summary>
        /// 一時停止状態を設定します。停止中の描画フレーム時間は固定Tick Groupへ蓄積されません。
        /// </summary>
        /// <param name="isPaused">停止する場合はtrue、通常進行へ戻す場合はfalseです。</param>
        public void SetPaused(bool isPaused)
        {
            Value = isPaused ? (byte)1 : (byte)0;
        }
    }

    /// <summary>
    /// 固定Tickへ入る前に完了すべき非同期準備が残っているかを保持する実行時Singletonです。
    /// 現在はChunkGenerationSystemだけが更新し、Rate ManagerがメインスレッドでJobを待たずTickを延期するために読みます。
    /// </summary>
    public struct FixedTickReadinessState : IComponentData
    {
        /// <summary>
        /// 0なら地形生成バッチが公開可能、1ならWorker上で生成中です。World寿命中保持し、ChunkGenerationSystemだけが変更します。
        /// </summary>
        public byte ChunkGenerationPending;

        /// <summary>地形生成の完了前なので固定Tickを開始してはいけない場合にtrueを返します。</summary>
        public readonly bool IsBlocked => ChunkGenerationPending != 0;

        /// <summary>
        /// 地形生成の待機状態を明示的なbyte値へ変換し、ECS Componentをunmanagedのまま維持します。
        /// </summary>
        /// <param name="isPending">生成Jobが未完了、または投入した同じ描画フレームならtrueです。</param>
        public void SetChunkGenerationPending(bool isPending)
        {
            ChunkGenerationPending = isPending ? (byte)1 : (byte)0;
        }
    }
}
