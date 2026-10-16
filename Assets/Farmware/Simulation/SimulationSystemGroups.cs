// このファイルは、FarmwareのECS SystemをUnity標準の更新ループへ接続する共通ルートGroupを定義します。
// 個別機能はこのGroupの子として更新順を宣言し、描画フレーム依存処理と今後追加する固定Tick処理の所属を明確にします。

using Unity.Entities;

namespace Farmware.Simulation
{
    /// <summary>
    /// ワールド作成直後のSingleton準備や初期探索をまとめる共通Initialization Groupです。
    /// Unity標準のInitializationSystemGroupから毎フレーム呼ばれ、通常のSimulation更新より前に実行されます。
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class FarmwareInitializationSystemGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// 描画フレームごとに必要なSimulation側の準備、ストリーミング、結果確定処理をまとめる共通Groupです。
    /// 固定TickのゲームルールはSIM-01Bで追加する子Groupへ分離し、このGroup自体の更新回数をゲーム内時間の正にはしません。
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FarmwareSimulationSystemGroup : ComponentSystemGroup
    {
    }
}
