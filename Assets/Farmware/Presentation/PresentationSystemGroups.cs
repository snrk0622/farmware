// このファイルは、ECS Simulationの結果を画面表示へ反映するSystemの共通ルートGroupを定義します。
// PresentationはSimulation Assemblyへ一方向に依存し、表示処理からゲーム状態の更新順を逆向きに支配しない境界を作ります。

using Unity.Entities;

namespace Farmware.Presentation
{
    /// <summary>
    /// Simulation更新後に、ECSの結果を描画・アニメーション・音声へ反映する共通Presentation Groupです。
    /// Unity標準のPresentationSystemGroupから呼ばれ、ここへ属するSystemはゲームルールの正を保持しません。
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class FarmwarePresentationSystemGroup : ComponentSystemGroup
    {
    }
}
