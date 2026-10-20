// このファイルは、TERRAIN-01C2の複数シード地質分布監査をUnity Editorメニューから開始します。
// 純粋な広域集計はバックグラウンドで実行し、Unity Consoleへの結果出力だけをメインスレッドで行います。

using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 既定条件の地質分布監査を1件ずつ実行するEditor専用入口です。
    /// ゲーム内入力や製品版ビルドへは含めず、生成結果の観察だけを行います。
    /// </summary>
    public static class GeologyDistributionAuditMenu
    {
        /// <summary>地質分布監査を開始するUnity Editorメニューパスです。</summary>
        private const string MenuPath = "Farmware/World Generation/Analyze Geology Distribution";

        /// <summary>同じ重い監査を二重起動しないため、実行中だけtrueにするEditorプロセス内フラグです。</summary>
        private static bool isRunning;

        /// <summary>
        /// 既存分布監査と同じ3シード・129×129代表格子で地質監査を開始します。
        /// async voidはUnityのMenuItemイベントから待機処理を開始する境界としてだけ使用します。
        /// </summary>
        [MenuItem(MenuPath, false, 121)]
        private static async void AnalyzeGeologyDistribution()
        {
            if (isRunning)
            {
                Debug.LogWarning("地質分布監査はすでに実行中です。");
                return;
            }

            isRunning = true;
            Debug.Log("[Farmware] TERRAIN-01C2 地質分布監査を開始しました。Editor操作は継続できます。");
            try
            {
                WorldGenerationDistributionAuditSettings settings =
                    WorldGenerationDistributionAuditSettings.CreateDefault();

                // 集計器はUnityEngine.Objectへ触れないため、広域生成中もEditor操作を止めないようThreadPoolで実行します。
                GeologyDistributionAuditResult result = await Task.Run(
                    () => GeologyDistributionAnalyzer.Analyze(settings));
                Debug.Log(result.Report);
            }
            catch (Exception exception)
            {
                // 設定不正や生成失敗を握りつぶさず、座標を含む例外をConsoleから追跡できるようにします。
                Debug.LogException(exception);
            }
            finally
            {
                isRunning = false;
            }
        }

        /// <summary>監査中はメニューを無効化し、CPU負荷とConsoleレポートの重複を防ぎます。</summary>
        /// <returns>新しい地質監査を開始できる場合はtrueです。</returns>
        [MenuItem(MenuPath, true)]
        private static bool ValidateAnalyzeGeologyDistribution()
        {
            return !isRunning;
        }
    }
}
