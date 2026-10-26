// このファイルは、TERRAIN-01C3-D2の基礎水文監査とC4-E1bの派生水文監査をUnity Editorメニューから開始します。
// 重い固定水文生成はバックグラウンドで実行し、Unity Console操作だけをEditorメインスレッドで行います。

using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>既定3Seedの基礎水文と派生水文を、製品版へ含めずEditorから監査する入口です。</summary>
    public static class HydrologyDistributionAuditMenu
    {
        /// <summary>水文分布監査を開始するUnity Editorメニューパスです。</summary>
        private const string MenuPath =
            "Farmware/World Generation/Analysis/Analyze Hydrology Distribution";

        /// <summary>C4の地下水・湖区分・泉・自然冠水・湿原分布監査を開始するEditorメニューパスです。</summary>
        private const string DerivedMenuPath =
            "Farmware/World Generation/Analysis/Analyze Derived Hydrology Distribution";

        /// <summary>同じ有限探索を二重起動しないため、監査実行中だけtrueにするEditorプロセス内フラグです。</summary>
        private static bool isRunning;

        /// <summary>完成チャンク生成を伴う派生水文監査の二重起動を防ぐEditorプロセス内フラグです。</summary>
        private static bool isDerivedRunning;

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
            Debug.Log("[Farmware] 水文分布監査を開始しました。Editor操作は継続できます。");
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

        /// <summary>
        /// C4-E1bの既定3Seed・各1チャンク監査をThreadPoolで開始し、完了結果をConsoleへ表示します。
        /// async voidはUnityのMenuItemイベントから非同期境界を開始するためだけに使用します。
        /// </summary>
        [MenuItem(DerivedMenuPath, false, 123)]
        private static async void AnalyzeDerivedHydrologyDistribution()
        {
            if (isDerivedRunning)
            {
                Debug.LogWarning("派生水文分布監査はすでに実行中です。");
                return;
            }

            isDerivedRunning = true;
            Debug.Log("[Farmware] 派生水文分布監査を開始しました。Editor操作は継続できます。");
            try
            {
                HydrologyDistributionAuditSettings settings =
                    DerivedHydrologyDistributionAnalyzer.CreateDefaultSettings();

                // 完成チャンク診断器はUnityEngine.ObjectやECS Worldへ触れずPersistent配列を自己破棄するため、
                // 3チャンクの有限生成をThreadPoolへ渡してもEditorメインスレッドの操作を妨げません。
                DerivedHydrologyDistributionAuditResult result = await Task.Run(
                    () => DerivedHydrologyDistributionAnalyzer.Analyze(settings));
                Debug.Log(result.Report);
            }
            catch (Exception exception)
            {
                // 生成段階と座標を含む例外を握りつぶさず、監査条件の不整合をConsoleから追跡可能にします。
                Debug.LogException(exception);
            }
            finally
            {
                isDerivedRunning = false;
            }
        }

        /// <summary>派生水文監査中は同じメニューを無効化し、Persistent作業領域の重複確保を防ぎます。</summary>
        /// <returns>新しい派生水文分布監査を開始できる場合はtrueです。</returns>
        [MenuItem(DerivedMenuPath, true)]
        private static bool ValidateAnalyzeDerivedHydrologyDistribution()
        {
            return !isDerivedRunning;
        }
    }
}
