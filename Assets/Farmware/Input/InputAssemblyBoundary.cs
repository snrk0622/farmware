// このファイルは、プレイヤー入力をSimulation Commandへ変換するInput Assemblyの識別点を定義します。
// SIM-01EでCommand Queueを追加するまでは状態を持たず、既存のカメラ操作とは別にゲームルール入力の置き場所だけを確定します。

namespace Farmware.Input
{
    /// <summary>
    /// Farmware.Input Assemblyをコードとテストから識別するための、状態を持たない境界型です。
    /// 将来の入力変換はこのAssemblyからSimulationへCommandを渡し、ECS状態を直接変更しません。
    /// </summary>
    public static class InputAssemblyBoundary
    {
    }
}
