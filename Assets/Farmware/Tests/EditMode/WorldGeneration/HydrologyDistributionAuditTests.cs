// このファイルは、TERRAIN-01C3-D2の複数Seed水文監査が全セルを漏れなく分類し、同じ入力で再現することを検証します。
// 通常テストは1Seed・1チャンクへ限定し、既定3Seed・5×5チャンクの広域監査はEditorメニューから必要時に実行します。

using System.Threading.Tasks;
using Farmware.WorldGeneration.Editor;
using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>水文監査の流向・終端母数、河川包含、湖状態、署名の退行をEditModeで検出します。</summary>
    public sealed class HydrologyDistributionAuditTests
    {
        /// <summary>
        /// 固定Seedの原点1チャンクを2回監査し、64セルの全分類と署名が一致することを保証します。
        /// </summary>
        [Test]
        public void Analyze_OneChunkCountsAllCellsAndIsDeterministic()
        {
            HydrologyDistributionAuditSettings settings =
                new HydrologyDistributionAuditSettings(
                    new[] { "hydrology-audit-test" },
                    0,
                    WorldGenerationSettings.CurrentGeneratorVersion);

            HydrologyDistributionAuditResult first =
                HydrologyDistributionAnalyzer.Analyze(settings);
            // 実際のEditorメニューと同じThreadPool境界で再実行し、Allocator.Temp依存の再発も検出します。
            HydrologyDistributionAuditResult second =
                Task.Run(() => HydrologyDistributionAnalyzer.Analyze(settings))
                    .GetAwaiter()
                    .GetResult();

            Assert.That(first.SeedCount, Is.EqualTo(1));
            Assert.That(first.ChunkRadius, Is.Zero);
            Assert.That(first.TotalCells, Is.EqualTo(DeterministicHydrologyGenerator.CellsPerChunk));
            Assert.That(SumFlowDirections(first), Is.EqualTo(first.TotalCells));
            Assert.That(SumTerminals(first), Is.EqualTo(first.TotalCells));
            Assert.That(first.RiverCellCount, Is.LessThanOrEqualTo(first.StreamCellCount));
            Assert.That(first.LakeSurfaceCellCount, Is.LessThanOrEqualTo(first.FixedLakeCellCount));
            Assert.That(first.FixedLakeBasinCount, Is.LessThanOrEqualTo(first.ResolvedBasinCount));
            Assert.That(first.MaximumCatchmentAreaCells, Is.InRange(1u, 4096u));
            Assert.That(second.Signature, Is.EqualTo(first.Signature));
            Assert.That(second.Report, Is.EqualTo(first.Report));
            Assert.That(first.Report, Does.Contain("水文分布監査"));
            Assert.That(first.Report, Does.Contain("合否閾値は設定していません"));
        }

        /// <summary>Noneを含む9流向の件数を合計します。</summary>
        /// <param name="result">合計対象の水文監査結果です。</param>
        /// <returns>全流向分類のセル件数です。</returns>
        private static int SumFlowDirections(HydrologyDistributionAuditResult result)
        {
            int total = 0;
            for (HydrologyFlowDirection direction = HydrologyFlowDirection.None;
                 direction <= HydrologyFlowDirection.SouthEast;
                 direction++)
            {
                total += result.GetFlowDirectionCount(direction);
            }

            return total;
        }

        /// <summary>Marine・ClosedBasin・LongDrainageの件数を合計します。</summary>
        /// <param name="result">合計対象の水文監査結果です。</param>
        /// <returns>相互排他的な3終端分類のセル件数です。</returns>
        private static int SumTerminals(HydrologyDistributionAuditResult result)
        {
            return result.GetTerminalCount(HydrologyConnectionFlags.Marine)
                + result.GetTerminalCount(HydrologyConnectionFlags.ClosedBasin)
                + result.GetTerminalCount(HydrologyConnectionFlags.LongDrainage);
        }
    }
}
