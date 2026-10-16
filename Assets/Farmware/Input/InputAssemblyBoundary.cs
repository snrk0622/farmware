// このファイルは、プレイヤー入力をSimulation Commandへ変換するInput Assemblyの識別点を定義します。
// 機能固有の入力処理はSimulationCommandQueueへ依頼を渡し、既存のカメラ操作とは別にゲーム状態の直接変更を防ぎます。

namespace Farmware.Input
{
    /// <summary>
    /// Farmware.Input Assemblyをコードとテストから識別するための、状態を持たない境界型です。
    /// 機能固有の入力変換はこのAssemblyからSimulationCommandQueueへCommandを渡し、ECS状態を直接変更しません。
    /// </summary>
    public static class InputAssemblyBoundary
    {
    }
}
