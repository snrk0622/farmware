// このファイルは、TERRAIN-01C2の地質分布監査が全代表点とC5-C後も有効な環境クロス軸を漏れなく集計することを検証します。
// 通常テストでは小さな固定格子を使い、既定3シードの広域レポートはEditorメニューから必要時に実行します。

using Farmware.WorldGeneration.Editor;
using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 地質監査の面積比母数、領域・境界集計、環境クロス集計、決定論的署名への退行をEditModeで検出します。
    /// </summary>
    public sealed class GeologyDistributionAuditTests
    {
        /// <summary>
        /// 2シードの13×13代表格子を2回監査し、全件数、辺数、クロス集計、署名が一致することを保証します。
        /// </summary>
        [Test]
        public void Analyze_SmallFixedSampleCountsAllAxesAndIsDeterministic()
        {
            WorldGenerationDistributionAuditSettings settings =
                new WorldGenerationDistributionAuditSettings(
                    new[] { "geology-audit-test-a", "geology-audit-test-b" },
                    6,
                    64,
                    WorldGenerationSettings.CurrentGeneratorVersion);

            GeologyDistributionAuditResult first = GeologyDistributionAnalyzer.Analyze(settings);
            GeologyDistributionAuditResult second = GeologyDistributionAnalyzer.Analyze(settings);

            const int expectedGridWidth = 13;
            const int expectedSeedCount = 2;
            const int expectedTotalSamples = expectedGridWidth * expectedGridWidth * expectedSeedCount;
            const int expectedNeighborEdges =
                expectedSeedCount * 2 * expectedGridWidth * (expectedGridWidth - 1);
            Assert.That(first.SeedCount, Is.EqualTo(expectedSeedCount));
            Assert.That(first.GridWidth, Is.EqualTo(expectedGridWidth));
            Assert.That(first.TotalSamples, Is.EqualTo(expectedTotalSamples));
            Assert.That(first.TotalNeighborEdges, Is.EqualTo(expectedNeighborEdges));
            Assert.That(SumGeologyCounts(first), Is.EqualTo(expectedTotalSamples));
            Assert.That(SumBoundaryParticipation(first), Is.EqualTo(first.BoundaryEdgeCount * 2));
            Assert.That(first.EstimatedBoundaryLengthInTiles, Is.EqualTo(first.BoundaryEdgeCount * 64L));
            Assert.That(second.Signature, Is.EqualTo(first.Signature));
            Assert.That(second.Report, Is.EqualTo(first.Report));
            Assert.That(first.Report, Does.Contain("TERRAIN-01C2 地質分布監査"));
            Assert.That(first.Report, Does.Contain("合否閾値は設定していません"));

            for (GeologyClass geologyClass = GeologyClass.UnconsolidatedSediment;
                 geologyClass <= GeologyClass.UltramaficRock;
                 geologyClass++)
            {
                int geologyCount = first.GetGeologyCount(geologyClass);
                Assert.That(SumElevationCrossCounts(first, geologyClass), Is.EqualTo(geologyCount));
                Assert.That(SumVolcanicCrossCounts(first, geologyClass), Is.EqualTo(geologyCount));
                Assert.That(first.GetIsolatedRegionCount(geologyClass),
                    Is.LessThanOrEqualTo(first.GetRegionCount(geologyClass)));
                Assert.That(first.GetMaximumRegionAreaInTiles(geologyClass), Is.GreaterThanOrEqualTo(0L));
                Assert.That(first.GetMaximumRegionDiameterInTiles(geologyClass), Is.GreaterThanOrEqualTo(0d));
            }
        }

        /// <summary>8地質分類の件数を合計し、代表サンプルの集計漏れがないか確認します。</summary>
        /// <param name="result">合計対象の地質監査結果です。</param>
        /// <returns>Noneを除いた8地質分類の件数合計です。</returns>
        private static int SumGeologyCounts(GeologyDistributionAuditResult result)
        {
            int total = 0;
            for (GeologyClass geologyClass = GeologyClass.UnconsolidatedSediment;
                 geologyClass <= GeologyClass.UltramaficRock;
                 geologyClass++)
            {
                total += result.GetGeologyCount(geologyClass);
            }

            return total;
        }

        /// <summary>地質別境界参加長を辺数へ戻して合計し、1境界が両側へ1回ずつ記録されたことを確認します。</summary>
        /// <param name="result">合計対象の地質監査結果です。</param>
        /// <returns>全地質の境界参加辺数です。</returns>
        private static long SumBoundaryParticipation(GeologyDistributionAuditResult result)
        {
            long totalLength = 0;
            for (GeologyClass geologyClass = GeologyClass.UnconsolidatedSediment;
                 geologyClass <= GeologyClass.UltramaficRock;
                 geologyClass++)
            {
                totalLength += result.GetBoundaryLengthInTiles(geologyClass);
            }

            return totalLength / 64L;
        }

        /// <summary>指定地質の5標高区分を合計します。</summary>
        /// <param name="result">読取対象の監査結果です。</param>
        /// <param name="geologyClass">合計対象の地質分類です。</param>
        /// <returns>全標高区分の件数合計です。</returns>
        private static int SumElevationCrossCounts(
            GeologyDistributionAuditResult result,
            GeologyClass geologyClass)
        {
            int total = 0;
            for (ElevationClass elevation = ElevationClass.BelowSeaLevel;
                 elevation <= ElevationClass.Alpine;
                 elevation++)
            {
                total += result.GetElevationCrossCount(geologyClass, elevation);
            }

            return total;
        }

        /// <summary>指定地質の火山活動度5段階を合計します。</summary>
        /// <param name="result">読取対象の監査結果です。</param>
        /// <param name="geologyClass">合計対象の地質分類です。</param>
        /// <returns>Level1〜5の件数合計です。</returns>
        private static int SumVolcanicCrossCounts(
            GeologyDistributionAuditResult result,
            GeologyClass geologyClass)
        {
            int total = 0;
            for (ContinuousEnvironmentBand band = ContinuousEnvironmentBand.Level1;
                 band <= ContinuousEnvironmentBand.Level5;
                 band++)
            {
                total += result.GetVolcanicBandCrossCount(geologyClass, band);
            }

            return total;
        }
    }
}
