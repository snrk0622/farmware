// このファイルは、固定Tick用Singletonの初期化と、各固定Tick開始時のTick番号更新を担当します。
// 初期化はFarmwareInitializationSystemGroup、Tick加算はFarmwareFixedTickSimulationSystemGroupへ分離します。

using Unity.Entities;

namespace Farmware.Simulation
{
    /// <summary>
    /// 新しいWorldへ固定Tick設定、現在Tick、一時停止状態を1件ずつ準備する初期化Systemです。
    /// Farmware Initializationの先頭で1回だけ実行し、作成後は自身を無効化します。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareInitializationSystemGroup), OrderFirst = true)]
    public partial struct SimulationTimeInitializationSystem : ISystem
    {
        /// <summary>
        /// System作成時に呼ばれます。Singletonは最初の更新でまとめて作るため、ここでは状態を変更しません。
        /// </summary>
        /// <param name="state">WorldのEntityManagerとSystemの有効状態へアクセスするための状態です。</param>
        public void OnCreate(ref SystemState state)
        {
        }

        /// <summary>
        /// World開始時に固定Tick用Singletonを同じEntityへ作成し、二重作成を防ぐため自身を停止します。
        /// </summary>
        /// <param name="state">Singleton Entityの作成とSystem停止に使用する状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            Entity simulationTimeEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(
                simulationTimeEntity,
                FixedTickConfiguration.CreateDefault());
            state.EntityManager.AddComponentData(
                simulationTimeEntity,
                new SimulationTickState
                {
                    CurrentTick = 0
                });
            state.EntityManager.AddComponentData(
                simulationTimeEntity,
                new SimulationPauseState
                {
                    Value = 0
                });

            // これらのSingletonはWorld寿命中に1組だけ存在させるため、初期化後は毎フレーム更新しません。
            state.Enabled = false;
        }

        /// <summary>
        /// World破棄時に呼ばれます。SingletonのメモリはWorldが所有するため、個別の解放処理はありません。
        /// </summary>
        /// <param name="state">破棄されるSystem状態です。Entityの変更には使用しません。</param>
        public void OnDestroy(ref SystemState state)
        {
        }
    }

    /// <summary>
    /// 固定Tick Groupが実際に更新されるたび、現在Tickを1だけ進めるSystemです。
    /// OrderFirstにより、同じTickの生産・物流・動物Systemは更新後の共通Tick番号を読み取れます。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareFixedTickSimulationSystemGroup), OrderFirst = true)]
    public partial struct SimulationTickAdvanceSystem : ISystem
    {
        /// <summary>
        /// System作成時に現在Tick Singletonを必須条件へ登録し、初期化前の更新を防ぎます。
        /// </summary>
        /// <param name="state">更新条件を登録するSystem状態です。</param>
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SimulationTickState>();
        }

        /// <summary>
        /// 固定Tick Groupから呼ばれたとき、これから実行するTick番号としてCurrentTickを1増やします。
        /// </summary>
        /// <param name="state">現在Tick Singletonへ書き込むSystem状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            RefRW<SimulationTickState> tickState =
                SystemAPI.GetSingletonRW<SimulationTickState>();

            // ulongの上限には10 Tick/秒でも約584億年かかるため、通常プレイでの巻き戻り対策は不要です。
            tickState.ValueRW.CurrentTick++;
        }

        /// <summary>
        /// World破棄時に呼ばれます。永続メモリを所有しないため、解放処理はありません。
        /// </summary>
        /// <param name="state">破棄されるSystem状態です。Entityの変更には使用しません。</param>
        public void OnDestroy(ref SystemState state)
        {
        }
    }
}
