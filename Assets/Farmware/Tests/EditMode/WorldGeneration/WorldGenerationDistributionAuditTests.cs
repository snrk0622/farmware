// このファイルは、C5-Eの完成チャンクBiome監査が候補・面積・境界・領域を欠落なく決定論的に集計することを検証します。
// 製品と同じC3〜C5-C生成経路を1チャンクで実行し、旧単一タイル監査へ戻る退行も検出します。

using System;
using Farmware.WorldGeneration.Editor;
using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// C5-E監査の入力検証、全タイル保存則、全153ペア台帳、同条件再実行の一致を保証します。
    /// </summary>
    public sealed class WorldGenerationDistributionAuditTests
    {
        /// <summary>
        /// 空Seedや過大範囲を準備し、監査開始前に分かりやすい引数例外で拒否することを保証します。
        /// </summary>
        [Test]
        public void Settings_InvalidSeedOrRadius_ThrowsBeforeGeneration()
        {
            Assert.That(
                () => new BiomeDistributionAuditSettings(Array.Empty<string>(), 0, WorldGenerationSettings.CurrentGeneratorVersion),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new BiomeDistributionAuditSettings(new[] { "audit" }, 5, WorldGenerationSettings.CurrentGeneratorVersion),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// 同じSeedの完成1チャンク監査を2回実行し、分類失敗なし、保存則、候補包含、隣接保存則、署名とレポートの一致を保証します。
        /// </summary>
        [Test]
        [Timeout(300000)]
        public void Analyze_CompletedChunk_IsCompleteAndDeterministic()
        {
            var settings = new BiomeDistributionAuditSettings(
                new[] { "c5-e-editmode-audit" },
                0,
                WorldGenerationSettings.CurrentGeneratorVersion);

            BiomeDistributionAuditResult first = BiomeDistributionAnalyzer.Analyze(settings);
            BiomeDistributionAuditResult second = BiomeDistributionAnalyzer.Analyze(settings);

            Assert.That(first.SeedCount, Is.EqualTo(1));
            Assert.That(first.TotalChunks, Is.EqualTo(1));
            Assert.That(first.TotalTiles, Is.EqualTo(WorldGridConstants.TilesPerChunk));
            Assert.That(first.LandTiles + first.OceanTiles, Is.EqualTo(first.TotalTiles));
            Assert.That(first.ClassificationFailureCount, Is.Zero);

            int areaSum = 0;
            int normalWinnerSum = 0;
            int adjacencySum = 0;
            int differentAdjacencySum = 0;
            for (int firstId = BiomeStableIds.First; firstId <= BiomeStableIds.Last; firstId++)
            {
                BiomeId firstBiome = (BiomeId)firstId;
                areaSum += first.GetAreaCount(firstBiome);
                normalWinnerSum += first.GetNormalWinnerCount(firstBiome);
                Assert.That(
                    first.GetCandidateCount(firstBiome),
                    Is.GreaterThanOrEqualTo(first.GetNormalWinnerCount(firstBiome)),
                    $"通常首位は候補資格件数を超えません。Biome={firstBiome}");

                for (int secondId = firstId; secondId <= BiomeStableIds.Last; secondId++)
                {
                    int edgeCount = first.GetAdjacencyCount(firstBiome, (BiomeId)secondId);
                    adjacencySum += edgeCount;
                    if (secondId != firstId)
                    {
                        differentAdjacencySum += edgeCount;
                    }
                }
            }

            Assert.That(areaSum, Is.EqualTo(first.LandTiles));
            Assert.That(normalWinnerSum, Is.EqualTo(first.LandTiles));
            Assert.That(adjacencySum, Is.EqualTo(first.TotalLandAdjacencyEdges));
            Assert.That(differentAdjacencySum, Is.EqualTo(first.DifferentBiomeEdges));
            Assert.That(first.Report, Does.Contain("全153異種Biomeペア"));
            Assert.That(first.Report, Does.Contain("01-18:"));
            Assert.That(first.Report, Does.Contain("景観の合否閾値を新設せず"));
            Assert.That(second.Signature, Is.EqualTo(first.Signature));
            Assert.That(second.Report, Is.EqualTo(first.Report));
        }

        /// <summary>
        /// 既定の3 Seed×3×3完成チャンクを準備し、実運用監査が火山中核／周縁、水文昇格を含めて完走することを保証します。
        /// 通常の短い回帰テストから分離するためExplicitとし、C5-E完了確認や分類調整時に明示実行します。
        /// </summary>
        [Test]
        [Explicit("110,592タイルの完成水文・Biome監査を行う長時間テストです。")]
        [Timeout(600000)]
        public void Analyze_DefaultMultiSeedAudit_CompletesSpecialCoverage()
        {
            BiomeDistributionAuditResult result =
                BiomeDistributionAnalyzer.Analyze(BiomeDistributionAuditSettings.CreateDefault());

            Assert.That(result.SeedCount, Is.EqualTo(3));
            Assert.That(result.TotalChunks, Is.EqualTo(27));
            Assert.That(result.TotalTiles, Is.EqualTo(110592));
            Assert.That(result.LandTiles + result.OceanTiles, Is.EqualTo(result.TotalTiles));
            Assert.That(result.ClassificationFailureCount, Is.Zero);
            Assert.That(result.TotalLandAdjacencyEdges, Is.GreaterThan(0));
            Assert.That(result.DifferentBiomeEdges, Is.GreaterThan(0));
            Assert.That(
                result.VolcanicCoreSelections + result.VolcanicFringeSelections,
                Is.GreaterThan(0),
                "火山中核または周縁を少なくとも1件監査できるSeed構成が必要です。");
            Assert.That(
                result.FreshwaterMarshPromotions + result.SwampForestPromotions,
                Is.GreaterThan(0),
                "水文特殊Hostからの昇格を少なくとも1件監査できるSeed構成が必要です。");

            // CLI結果へ実測値を残すため、全保証を通過した後に成功メッセージとして固定条件の集計を返します。
            Assert.Pass(
                $"C5-E既定監査: Chunks={result.TotalChunks}, Tiles={result.TotalTiles}, "
                + $"Land={result.LandTiles}, Ocean={result.OceanTiles}, Edges={result.TotalLandAdjacencyEdges}, "
                + $"Different={result.DifferentBiomeEdges}, Core={result.VolcanicCoreSelections}, "
                + $"Fringe={result.VolcanicFringeSelections}, Marsh={result.FreshwaterMarshPromotions}, "
                + $"Swamp={result.SwampForestPromotions}, Signature=0x{result.Signature:X16}");
        }
    }
}
