// このファイルは、複数シードの分布監査をUnity Editorメニューから開始し、完了結果をConsoleへ表示します。
// 重い純粋計算だけをバックグラウンドへ移し、Unity APIのログ出力はEditorのメインスレッドへ戻ってから行います。

using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// Farmwareメニューから軽量な複数シード監査を1件ずつ実行するEditor専用入口です。
    /// ゲーム内キーを消費せず、製品版ビルドへ含まれません。
    /// </summary>
    public static class WorldGenerationDistributionAuditMenu
    {
        /// <summary>分布監査を開始するUnity Editorメニューパスです。</summary>
        private const string MenuPath = "Farmware/World Generation/Analyze Distribution";

        /// <summary>同時に複数監査を開始してCPU負荷とログを重複させないための実行中フラグです。</summary>
        private static bool isRunning;

        /// <summary>
        /// 既定の3シード監査をバックグラウンドで開始し、完了後にConsoleへ比較用レポートを表示します。
        /// async voidはUnityのMenuItemイベントから待機処理を開始する境界としてだけ使用します。
        /// </summary>
        [MenuItem(MenuPath, false, 120)]
        private static async void AnalyzeDistribution()
        {
            if (isRunning)
            {
                Debug.LogWarning("ワールド生成分布監査はすでに実行中です。");
                return;
            }

            isRunning = true;
            Debug.Log("[Farmware] ワールド生成分布監査を開始しました。Editor操作は継続できます。");

            try
            {
                WorldGenerationDistributionAuditSettings settings =
                    WorldGenerationDistributionAuditSettings.CreateDefault();

                // DeterministicChunkGeneratorはUnityEngine.Objectへ触れない純粋計算なので、Editorを固めないようThreadPoolで実行できます。
                WorldGenerationDistributionAuditResult result = await Task.Run(
                    () => WorldGenerationDistributionAnalyzer.Analyze(settings));
                Debug.Log(result.Report);
            }
            catch (Exception exception)
            {
                // 例外を握りつぶさず、設定不正や生成退行をUnity Consoleから追跡できる形で表示します。
                Debug.LogException(exception);
            }
            finally
            {
                isRunning = false;
            }
        }

        /// <summary>C5-Eの新18Biome監査へ置換するまで、旧基礎生成を使うメニューを無効化します。</summary>
        /// <returns>C5-Cでは常にfalseです。</returns>
        [MenuItem(MenuPath, true)]
        private static bool ValidateAnalyzeDistribution()
        {
            return false;
        }
    }
}
