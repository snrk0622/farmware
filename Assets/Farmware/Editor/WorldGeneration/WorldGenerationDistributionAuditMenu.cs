// このファイルは、C5-Eの複数Seed・完成チャンクBiome監査をUnity Editorメニューから開始します。
// 重い純粋計算だけをバックグラウンドへ移し、Unity APIのログ出力はEditorのメインスレッドへ戻ってから行います。

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// Farmwareメニューから複数Seedの全タイル監査を1件ずつ実行するEditor専用入口です。
    /// ゲーム内キーを消費せず、製品版ビルドへ含まれません。
    /// </summary>
    public static class WorldGenerationDistributionAuditMenu
    {
        /// <summary>分布監査を開始するUnity Editorメニューパスです。</summary>
        private const string MenuPath = "Farmware/World Generation/Analyze Distribution";

        /// <summary>同時に複数監査を開始してCPU負荷とログを重複させないための実行中フラグです。</summary>
        private static bool isRunning;

        /// <summary>Unity Consoleで長大な1メッセージが省略されないようにする、1ログ当たりの目安文字数です。</summary>
        private const int MaximumCharactersPerLog = 12000;

        /// <summary>
        /// 既定の3 Seed×3×3完成チャンク監査をバックグラウンドで開始し、完了後に比較用レポートを表示します。
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
            Debug.Log("[Farmware] C5-E Biome分布・境界監査を開始しました。Editor操作は継続できます。");

            try
            {
                BiomeDistributionAuditSettings settings =
                    BiomeDistributionAuditSettings.CreateDefault();
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                // 完成チャンク再構築はUnityEngine.Objectへ触れない純粋計算なので、Editorを固めないようThreadPoolで実行できます。
                BiomeDistributionAuditResult result = await Task.Run(
                    () => BiomeDistributionAnalyzer.Analyze(settings));
                stopwatch.Stop();
                LogReport(result.Report);
                Debug.Log($"[Farmware] C5-E Biome分布・境界監査を完了しました。実行時間={stopwatch.Elapsed.TotalSeconds:F1}秒");
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

        /// <summary>監査の多重実行だけを防ぎ、待機中はメニューを利用可能にします。</summary>
        /// <returns>別のBiome分布監査が動作していなければtrueです。</returns>
        [MenuItem(MenuPath, true)]
        private static bool ValidateAnalyzeDistribution()
        {
            return !isRunning;
        }

        /// <summary>
        /// 全153ペアを含むレポートを改行境界で分割し、Unity Consoleへ順番どおり表示します。
        /// </summary>
        /// <param name="report">Analyzerが固定順で作った複数行レポートです。</param>
        private static void LogReport(string report)
        {
            using var reader = new StringReader(report);
            var chunk = new StringBuilder(MaximumCharactersPerLog);
            int partNumber = 1;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                // 既存チャンクへ次の1行を足すと目安を超える場合、行を途中で切らず現在分を先に表示します。
                if (chunk.Length > 0
                    && chunk.Length + line.Length + Environment.NewLine.Length > MaximumCharactersPerLog)
                {
                    Debug.Log($"[Farmware] C5-E監査レポート part {partNumber}\n{chunk}");
                    chunk.Clear();
                    partNumber++;
                }

                chunk.AppendLine(line);
            }

            if (chunk.Length > 0)
            {
                Debug.Log($"[Farmware] C5-E監査レポート part {partNumber}\n{chunk}");
            }
        }
    }
}
