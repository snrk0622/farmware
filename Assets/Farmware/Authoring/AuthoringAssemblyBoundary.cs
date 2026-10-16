// このファイルは、Inspector用MonoBehaviour、Baker、設定アセットを置くAuthoring Assemblyの識別点を定義します。
// 機能別Authoring Assemblyと境界テストが共通Assemblyを明示参照できるようにし、実行中のゲーム状態は保持しません。

namespace Farmware.Authoring
{
    /// <summary>
    /// Farmware.Authoring Assemblyをコードとテストから識別するための、状態を持たない境界型です。
    /// Authoring機能はこのAssemblyからSimulationの公開データへ変換し、逆方向の参照を作りません。
    /// </summary>
    public static class AuthoringAssemblyBoundary
    {
    }
}
