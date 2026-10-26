// このファイルは、動物個体群の複数Seed・複数地域監査をUnity Editorメニューから開始します。
// 重い地形・候補生成はバックグラウンドで行い、Unity APIのログ出力だけをEditorメインスレッドへ戻します。

using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>Farmwareメニューから動物個体群監査を重複なく開始するEditor専用入口です。</summary>
    public static class AnimalPopulationDistributionAuditMenu
    {
        /// <summary>動物個体群分布監査を開始するUnity Editorメニューパスです。</summary>
        private const string MenuPath =
            "Farmware/World Generation/Analysis/Analyze Animal Population";

        /// <summary>同じ約100万タイル監査を二重起動しないための実行中フラグです。</summary>
        private static bool isRunning;

        /// <summary>既定の3組のSeed監査をバックグラウンドで開始し、完了結果をConsoleへ表示します。</summary>
        [MenuItem(MenuPath, false, 121)]
        private static async void AnalyzeAnimalPopulation()
        {
            if (isRunning)
            {
                Debug.LogWarning("動物個体群分布監査はすでに実行中です。");
                return;
            }

            isRunning = true;
            Debug.Log("[Farmware] 動物個体群分布監査を開始しました。Editor操作は継続できます。");
            try
            {
                AnimalPopulationDistributionAuditSettings settings =
                    AnimalPopulationDistributionAuditSettings.CreateDefault();
                AnimalPopulationDistributionAuditResult result = await Task.Run(
                    () => AnimalPopulationDistributionAnalyzer.Analyze(settings));
                Debug.Log(result.Report);
            }
            catch (Exception exception)
            {
                // 設定不正や候補生成失敗を握りつぶさず、Consoleから原因を追跡できるようにします。
                Debug.LogException(exception);
            }
            finally
            {
                isRunning = false;
            }
        }

        /// <summary>監査中は同じメニューを無効化し、CPU負荷と結果ログの重複を防ぎます。</summary>
        /// <returns>新しい監査を開始できる場合はtrueです。</returns>
        [MenuItem(MenuPath, true)]
        private static bool ValidateAnalyzeAnimalPopulation()
        {
            return !isRunning;
        }
    }
}
