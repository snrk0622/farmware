// このファイルは、Editor専用の複数シード分布監査が全サンプルを数え、同じ入力から同じ署名を返すことを軽量に検証します。
// 通常テストでは小さな格子だけを使い、広域の3シード監査はUnity Editorメニューから必要なときだけ実行します。

using Farmware.WorldGeneration.Editor;
using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 分布監査の集計漏れ、隣接ペア漏れ、非決定的な比較署名への退行をEditModeで検出します。
    /// </summary>
    public sealed class WorldGenerationDistributionAuditTests
    {
        /// <summary>
        /// 2シードの9×9代表格子を2回監査し、全件集計、直接隣接数、分類件数、署名が一致することを保証します。
        /// </summary>
        [Test]
        public void Analyze_SmallFixedSampleCountsEveryTileAndIsDeterministic()
        {
            WorldGenerationDistributionAuditSettings settings =
                new WorldGenerationDistributionAuditSettings(
                    new[] { "audit-test-a", "audit-test-b" },
                    4,
                    32,
                    1);

            WorldGenerationDistributionAuditResult first =
                WorldGenerationDistributionAnalyzer.Analyze(settings);
            WorldGenerationDistributionAuditResult second =
                WorldGenerationDistributionAnalyzer.Analyze(settings);

            const int expectedSamplesPerSeed = 9 * 9;
            const int expectedTotalSamples = expectedSamplesPerSeed * 2;
            Assert.That(first.SeedCount, Is.EqualTo(2));
            Assert.That(first.SamplesPerSeed, Is.EqualTo(expectedSamplesPerSeed));
            Assert.That(first.TotalAnchorSamples, Is.EqualTo(expectedTotalSamples));
            Assert.That(first.TotalDirectAdjacencySamples, Is.EqualTo(expectedTotalSamples * 2));
            Assert.That(first.TotalChunkBoundaryAdjacencySamples, Is.EqualTo(180));
            Assert.That(
                first.ChunkBoundaryBiomeChangeCount,
                Is.InRange(0, first.TotalChunkBoundaryAdjacencySamples));
            Assert.That(SumBiomeCounts(first), Is.EqualTo(expectedTotalSamples));
            Assert.That(SumGroundCounts(first), Is.EqualTo(expectedTotalSamples));
            Assert.That(SumWaterBodyCounts(first), Is.EqualTo(expectedTotalSamples));
            Assert.That(second.Signature, Is.EqualTo(first.Signature));
            Assert.That(second.Report, Is.EqualTo(first.Report));
            Assert.That(first.Report, Does.Contain("ワールド生成分布監査"));
        }

        /// <summary>7種類の実バイオーム件数を合計し、代表サンプルの集計漏れがないか確認します。</summary>
        /// <param name="result">合計対象の監査結果です。</param>
        /// <returns>Noneを除いた7バイオームの件数合計です。</returns>
        private static int SumBiomeCounts(WorldGenerationDistributionAuditResult result)
        {
            int total = 0;
            for (BiomeId biome = BiomeId.Grassland; biome <= BiomeId.Volcanic; biome++)
            {
                total += result.GetBiomeCount(biome);
            }

            return total;
        }

        /// <summary>全地面種別の件数を合計し、代表サンプルの集計漏れがないか確認します。</summary>
        /// <param name="result">合計対象の監査結果です。</param>
        /// <returns>通常地面からマグマまでの件数合計です。</returns>
        private static int SumGroundCounts(WorldGenerationDistributionAuditResult result)
        {
            int total = 0;
            for (GroundType ground = GroundType.Normal; ground <= GroundType.Magma; ground++)
            {
                total += result.GetGroundCount(ground);
            }

            return total;
        }

        /// <summary>水域なしを含む全水域種別の件数を合計し、代表サンプルの集計漏れがないか確認します。</summary>
        /// <param name="result">合計対象の監査結果です。</param>
        /// <returns>水域なし・川・池・海の件数合計です。</returns>
        private static int SumWaterBodyCounts(WorldGenerationDistributionAuditResult result)
        {
            int total = 0;
            for (WaterBodyType waterBody = WaterBodyType.None;
                 waterBody <= WaterBodyType.Sea;
                 waterBody++)
            {
                total += result.GetWaterBodyCount(waterBody);
            }

            return total;
        }
    }
}
