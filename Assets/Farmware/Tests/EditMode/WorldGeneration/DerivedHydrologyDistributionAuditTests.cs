// このファイルは、C4-E1bの派生水文監査が完成チャンクを漏れなく集計し、同じ入力で同じ署名とレポートを返すことを検証します。
// 通常テストは1Seed・1チャンクに限定し、既定3Seedの実レポートはEditorメニューから必要時に記録します。

using System.Threading.Tasks;
using Farmware.WorldGeneration.Editor;
using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>地下水、湖、泉、自然冠水、湿原の母数・包含関係・ThreadPool再現性を検証します。</summary>
    public sealed class DerivedHydrologyDistributionAuditTests
    {
        /// <summary>
        /// 固定Seedの原点1チャンクをMain ThreadとThreadPoolで監査し、全件数と比較出力が一致することを保証します。
        /// </summary>
        [Test]
        [Timeout(120000)]
        public void Analyze_OneChunkCountsDerivedHydrologyAndIsDeterministic()
        {
            HydrologyDistributionAuditSettings settings = new HydrologyDistributionAuditSettings(
                new[] { "derived-hydrology-distribution-test" },
                0,
                WorldGenerationSettings.CurrentGeneratorVersion);

            DerivedHydrologyDistributionAuditResult first =
                DerivedHydrologyDistributionAnalyzer.Analyze(settings);
            // 実メニューと同じThreadPool境界で再実行し、Main Thread限定APIや短命Allocatorへの依存も検出します。
            DerivedHydrologyDistributionAuditResult second = Task.Run(
                    () => DerivedHydrologyDistributionAnalyzer.Analyze(settings))
                .GetAwaiter()
                .GetResult();

            Assert.That(first.SeedCount, Is.EqualTo(1));
            Assert.That(first.ChunkRadius, Is.Zero);
            Assert.That(first.TotalChunks, Is.EqualTo(1));
            Assert.That(first.TotalHydrologyCells, Is.EqualTo(DeterministicHydrologyGenerator.CellsPerChunk));
            Assert.That(first.TotalTiles, Is.EqualTo(WorldGridConstants.TilesPerChunk));
            Assert.That(first.GroundwaterConnectedCellCount, Is.InRange(0, first.TotalHydrologyCells));
            Assert.That(first.SaturatedTileCount, Is.InRange(0, first.TotalTiles));
            Assert.That(first.SpringStreamTileCount, Is.InRange(0, first.TotalTiles));
            Assert.That(first.FloodedTileCount, Is.InRange(0, first.TotalTiles));
            Assert.That(first.WetlandEligibleTileCount, Is.InRange(0, first.TotalTiles));
            Assert.That(first.LargeWetlandExtentTileCount, Is.InRange(0, first.TotalTiles));
            Assert.That(SumLakeClassifications(first), Is.InRange(0, first.TotalHydrologyCells));

            Assert.That(second.Signature, Is.EqualTo(first.Signature));
            Assert.That(second.Report, Is.EqualTo(first.Report));
            Assert.That(first.Report, Does.Contain("TERRAIN-01C4-E1b 派生水文分布監査"));
            Assert.That(first.Report, Does.Contain("SpringStreamTiles="));
            Assert.That(first.Report, Does.Contain("0件を失敗にする固定の合否閾値は設定していません"));
        }

        /// <summary>相互排他的な最終湖4区分のセル件数を合計します。</summary>
        /// <param name="result">合計対象の派生水文監査結果です。</param>
        /// <returns>None以外の最終湖区分を持つセル総数です。</returns>
        private static long SumLakeClassifications(DerivedHydrologyDistributionAuditResult result)
        {
            return result.TemporaryLakeCellCount
                + result.PermanentSaltwaterLakeCellCount
                + result.ClosedFreshwaterLakeCellCount
                + result.OverflowFreshwaterLakeCellCount;
        }
    }
}
