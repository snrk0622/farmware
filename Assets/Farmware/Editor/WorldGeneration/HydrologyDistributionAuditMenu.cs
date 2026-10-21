// このファイルは、TERRAIN-01C3-D2の複数Seed水文分布監査をUnity Editorメニューから開始します。
// 重い固定水文生成はバックグラウンドで実行し、Unity Console操作だけをEditorメインスレッドで行います。

using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>既定3Seedの流向・流域・河川・湖分布を、製品版へ含めずEditorから監査する入口です。</summary>
    public static class HydrologyDistributionAuditMenu
    {
        /// <summary>水文分布監査を開始するUnity Editorメニューパスです。</summary>
        private const string MenuPath = "Farmware/World Generation/Analyze Hydrology Distribution";

        /// <summary>同じ有限探索を二重起動しないため、監査実行中だけtrueにするEditorプロセス内フラグです。</summary>
        private static bool isRunning;

        /// <summary>
        /// 3Seed・5×5チャンクの既定監査をThreadPoolで開始し、完了結果をConsoleへ表示します。
        /// async voidはUnityのMenuItemイベントから非同期境界を開始するためだけに使用します。
        /// </summary>
        [MenuItem(MenuPath, false, 122)]
        private static async void AnalyzeHydrologyDistribution()
        {
            if (isRunning)
            {
                Debug.LogWarning("水文分布監査はすでに実行中です。");
                return;
            }

            isRunning = true;
            Debug.Log("[Farmware] TERRAIN-01C3-D2 水文分布監査を開始しました。Editor操作は継続できます。");
            try
            {
                HydrologyDistributionAuditSettings settings =
                    HydrologyDistributionAuditSettings.CreateDefault();

                // AnalyzerはUnityEngine.ObjectやECS Worldへ触れないため、有限探索中もEditor操作を止めないようThreadPoolへ渡します。
                HydrologyDistributionAuditResult result = await Task.Run(
                    () => HydrologyDistributionAnalyzer.Analyze(settings));
                Debug.Log(result.Report);
            }
            catch (Exception exception)
            {
                // 生成失敗や設定不正を握りつぶさず、対象座標を含む例外としてConsoleへ残します。
                Debug.LogException(exception);
            }
            finally
            {
                isRunning = false;
            }
        }

        /// <summary>監査中はメニューを無効化し、重い水文生成の二重起動を防ぎます。</summary>
        /// <returns>新しい水文分布監査を開始できる場合はtrueです。</returns>
        [MenuItem(MenuPath, true)]
        private static bool ValidateAnalyzeHydrologyDistribution()
        {
            return !isRunning;
        }
    }
}
