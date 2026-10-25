// このファイルは、C5-Eの完成チャンク監査に使う設定、集計軸、結果、Consoleレポートを定義します。
// 解析処理はBiomeDistributionAnalyzerへ分離し、テストとEditorメニューは不変な結果だけを読み取ります。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 通常Biome候補の8評価軸を表します。
    /// 候補資格を得たタイルの適性寄与を、Definitionの重みと同じ順序で集計するときに使用します。
    /// </summary>
    public enum BiomeAuditAxis : byte
    {
        /// <summary>地形気温です。</summary>
        TerrainTemperature = 0,
        /// <summary>基礎湿度です。</summary>
        BaseMoisture = 1,
        /// <summary>標高です。</summary>
        Elevation = 2,
        /// <summary>木本植生ポテンシャルです。</summary>
        WoodyVegetation = 3,
        /// <summary>地形起伏です。</summary>
        TerrainRuggedness = 4,
        /// <summary>固定地質適合度です。</summary>
        Geology = 5,
        /// <summary>火山活動度です。</summary>
        VolcanicActivity = 6,
        /// <summary>海洋影響です。</summary>
        CoastalInfluence = 7
    }

    /// <summary>
    /// 隣接する異種Biome間で記録する7つの連続環境差を表します。
    /// 地質は連続値ではないため含めず、別の境界理由フラグとして記録します。
    /// </summary>
    public enum BiomeBoundaryAxis : byte
    {
        /// <summary>地形気温差です。</summary>
        TerrainTemperature = 0,
        /// <summary>基礎湿度差です。</summary>
        BaseMoisture = 1,
        /// <summary>標高差です。</summary>
        Elevation = 2,
        /// <summary>木本植生ポテンシャル差です。</summary>
        WoodyVegetation = 3,
        /// <summary>地形起伏差です。</summary>
        TerrainRuggedness = 4,
        /// <summary>火山活動度差です。</summary>
        VolcanicActivity = 5,
        /// <summary>海洋影響差です。</summary>
        CoastalInfluence = 6
    }

    /// <summary>
    /// 異種Biome境界で、分類結果または離散環境区分が変わった理由を重複可能なbitで表します。
    /// 1境界に複数理由が成立するため、各理由の件数合計は境界総数と一致する必要がありません。
    /// </summary>
    [Flags]
    public enum BiomeBoundaryReasonFlags : ushort
    {
        /// <summary>理由をまだ記録していません。</summary>
        None = 0,
        /// <summary>特殊優先前の通常首位Biomeが異なります。</summary>
        NormalWinner = 1 << 0,
        /// <summary>地形気温の固定5段階が異なります。</summary>
        TemperatureBand = 1 << 1,
        /// <summary>基礎湿度の固定5段階が異なります。</summary>
        MoistureBand = 1 << 2,
        /// <summary>標高区分が異なります。</summary>
        ElevationBand = 1 << 3,
        /// <summary>木本植生ポテンシャルの固定5段階が異なります。</summary>
        WoodyBand = 1 << 4,
        /// <summary>地形起伏の固定5段階が異なります。</summary>
        RuggednessBand = 1 << 5,
        /// <summary>固定地質分類が異なります。</summary>
        Geology = 1 << 6,
        /// <summary>火山活動度の固定5段階が異なります。</summary>
        VolcanicBand = 1 << 7,
        /// <summary>海洋影響の固定5段階が異なります。</summary>
        CoastalBand = 1 << 8,
        /// <summary>通常・火山中核・水文昇格の最終選択理由が異なります。</summary>
        SelectionReason = 1 << 9,
        /// <summary>固定水文フラグが異なります。</summary>
        Hydrology = 1 << 10
    }

    /// <summary>
    /// C5-E監査で完成させる連続チャンク範囲と生成互換境界を保持します。
    /// 代表点監査ではなく全タイルを調べるため、範囲はタイル間隔ではなくチャンク半径で指定します。
    /// </summary>
    public sealed class BiomeDistributionAuditSettings
    {
        /// <summary>監査する1件以上のSeed文字列です。構築時に複製し、監査中は変更しません。</summary>
        public IReadOnlyList<string> SeedTexts { get; }

        /// <summary>原点チャンクから各軸へ完成させる半径です。1なら3×3チャンクを表します。</summary>
        public int ChunkRadius { get; }

        /// <summary>保存互換境界を含む生成器バージョンです。</summary>
        public int GeneratorVersion { get; }

        /// <summary>
        /// 同じ条件を再実行できる監査設定を作ります。
        /// </summary>
        /// <param name="seedTexts">1件以上の空でないSeed文字列です。</param>
        /// <param name="chunkRadius">0〜4の連続チャンク半径です。</param>
        /// <param name="generatorVersion">監査対象のGeneratorVersionです。</param>
        public BiomeDistributionAuditSettings(string[] seedTexts, int chunkRadius, int generatorVersion)
        {
            if (seedTexts == null || seedTexts.Length == 0)
            {
                throw new ArgumentException("Biome監査には1件以上のSeedが必要です。", nameof(seedTexts));
            }

            if (chunkRadius < 0 || chunkRadius > 4)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkRadius),
                    "全タイル監査のチャンク半径は0〜4で指定してください。");
            }

            string[] ownedSeeds = (string[])seedTexts.Clone();
            for (int index = 0; index < ownedSeeds.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(ownedSeeds[index]))
                {
                    throw new ArgumentException($"Biome監査のSeedが空です。index={index}", nameof(seedTexts));
                }
            }

            // 呼び出し元の配列変更で途中結果が変わらないよう、監査が所有する複製だけを公開します。
            SeedTexts = ownedSeeds;
            ChunkRadius = chunkRadius;
            GeneratorVersion = generatorVersion;
        }

        /// <summary>
        /// 温帯、水文、火山を意識した3 Seedの各3×3完成チャンクを調べる既定設定を返します。
        /// </summary>
        /// <returns>同じ入力で比較可能なC5-E標準設定です。</returns>
        public static BiomeDistributionAuditSettings CreateDefault()
        {
            return new BiomeDistributionAuditSettings(
                new[]
                {
                    "biome-audit-temperate",
                    "biome-audit-hydrology",
                    "biome-audit-volcanic"
                },
                1,
                WorldGenerationSettings.CurrentGeneratorVersion);
        }
    }

    /// <summary>
    /// C5-E監査の全集計と決定論署名を保持します。
    /// 配列を外へ渡さず取得メソッドを介すことで、Console出力後も結果が書き換わることを防ぎます。
    /// </summary>
    public sealed class BiomeDistributionAuditResult
    {
        /// <summary>Biome安定ID別の最終面積です。</summary>
        private readonly int[] areaCounts;
        /// <summary>Biome安定ID別の通常候補資格件数です。</summary>
        private readonly int[] candidateCounts;
        /// <summary>Biome安定ID別の通常首位件数です。</summary>
        private readonly int[] normalWinnerCounts;
        /// <summary>Biome・評価軸別の候補適性合計です。</summary>
        private readonly long[] axisFitSums;
        /// <summary>Biome・評価軸別の候補適性へDefinition重みを掛けた寄与合計です。</summary>
        private readonly long[] axisContributionSums;
        /// <summary>順不同Biomeペア別の陸地隣接辺数です。</summary>
        private readonly int[] adjacencyCounts;
        /// <summary>順不同Biomeペア・理由別の境界理由件数です。</summary>
        private readonly int[] boundaryReasonCounts;
        /// <summary>順不同Biomeペア・環境軸別の絶対差合計です。</summary>
        private readonly long[] boundaryDifferenceSums;
        /// <summary>順不同Biomeペア・環境軸別の最大絶対差です。</summary>
        private readonly ushort[] boundaryDifferenceMaxima;
        /// <summary>Biome安定ID別の4近傍連結領域数です。</summary>
        private readonly int[] regionCounts;
        /// <summary>Biome安定ID別の最大4近傍連結領域面積です。</summary>
        private readonly int[] maximumRegionAreas;
        /// <summary>Biome安定ID別の面積1〜3タイル領域数です。</summary>
        private readonly int[] smallRegionCounts;
        /// <summary>Biome安定ID別の監査窓端へ接した領域数です。</summary>
        private readonly int[] clippedRegionCounts;

        /// <summary>監査したSeed数です。</summary>
        public int SeedCount { get; }
        /// <summary>1 Seedで完成させたチャンク数です。</summary>
        public int ChunksPerSeed { get; }
        /// <summary>完成させた全チャンク数です。</summary>
        public int TotalChunks { get; }
        /// <summary>海域を含む全監査タイル数です。</summary>
        public int TotalTiles { get; }
        /// <summary>新18Biome分類対象になった陸地タイル数です。</summary>
        public int LandTiles { get; }
        /// <summary>BiomeId.Noneとして正しく除外した海域タイル数です。</summary>
        public int OceanTiles { get; }
        /// <summary>候補なし、不一致、完成チャンク失敗を検出した件数です。成功結果では必ず0です。</summary>
        public int ClassificationFailureCount { get; }
        /// <summary>陸地同士を東・北の一度だけ数えた隣接辺総数です。</summary>
        public int TotalLandAdjacencyEdges { get; }
        /// <summary>陸地隣接辺のうち最終Biomeが異なる境界総数です。</summary>
        public int DifferentBiomeEdges { get; }
        /// <summary>通常首位から活動的火山中核へ上書きされたタイル数です。</summary>
        public int VolcanicCoreSelections { get; }
        /// <summary>火山Biomeだが中核優先ではなく通常候補で選ばれた周縁タイル数です。</summary>
        public int VolcanicFringeSelections { get; }
        /// <summary>淡水湿原・氾濫原へHostから昇格したタイル数です。</summary>
        public int FreshwaterMarshPromotions { get; }
        /// <summary>湿地林・沼沢林へHostから昇格したタイル数です。</summary>
        public int SwampForestPromotions { get; }
        /// <summary>走査順、分類、候補、境界を固定順で混ぜた比較用64bit署名です。</summary>
        public ulong Signature { get; }
        /// <summary>Unity Consoleへ表示する日本語監査レポートです。</summary>
        public string Report { get; }

        /// <summary>
        /// Analyzerが完成した配列を所有させ、以後変更しない監査結果を構築します。
        /// </summary>
        internal BiomeDistributionAuditResult(
            int[] areaCounts,
            int[] candidateCounts,
            int[] normalWinnerCounts,
            long[] axisFitSums,
            long[] axisContributionSums,
            int[] adjacencyCounts,
            int[] boundaryReasonCounts,
            long[] boundaryDifferenceSums,
            ushort[] boundaryDifferenceMaxima,
            int[] regionCounts,
            int[] maximumRegionAreas,
            int[] smallRegionCounts,
            int[] clippedRegionCounts,
            int seedCount,
            int chunksPerSeed,
            int totalTiles,
            int landTiles,
            int oceanTiles,
            int totalLandAdjacencyEdges,
            int differentBiomeEdges,
            int volcanicCoreSelections,
            int volcanicFringeSelections,
            int freshwaterMarshPromotions,
            int swampForestPromotions,
            ulong signature)
        {
            this.areaCounts = areaCounts;
            this.candidateCounts = candidateCounts;
            this.normalWinnerCounts = normalWinnerCounts;
            this.axisFitSums = axisFitSums;
            this.axisContributionSums = axisContributionSums;
            this.adjacencyCounts = adjacencyCounts;
            this.boundaryReasonCounts = boundaryReasonCounts;
            this.boundaryDifferenceSums = boundaryDifferenceSums;
            this.boundaryDifferenceMaxima = boundaryDifferenceMaxima;
            this.regionCounts = regionCounts;
            this.maximumRegionAreas = maximumRegionAreas;
            this.smallRegionCounts = smallRegionCounts;
            this.clippedRegionCounts = clippedRegionCounts;
            SeedCount = seedCount;
            ChunksPerSeed = chunksPerSeed;
            TotalChunks = checked(seedCount * chunksPerSeed);
            TotalTiles = totalTiles;
            LandTiles = landTiles;
            OceanTiles = oceanTiles;
            ClassificationFailureCount = 0;
            TotalLandAdjacencyEdges = totalLandAdjacencyEdges;
            DifferentBiomeEdges = differentBiomeEdges;
            VolcanicCoreSelections = volcanicCoreSelections;
            VolcanicFringeSelections = volcanicFringeSelections;
            FreshwaterMarshPromotions = freshwaterMarshPromotions;
            SwampForestPromotions = swampForestPromotions;
            Signature = signature;
            Report = BuildReport();
        }

        /// <summary>指定Biomeの最終面積を返します。</summary>
        public int GetAreaCount(BiomeId biome) => areaCounts[(int)biome];

        /// <summary>指定Biomeが通常候補資格を得たタイル数を返します。</summary>
        public int GetCandidateCount(BiomeId biome) => candidateCounts[(int)biome];

        /// <summary>指定Biomeが特殊優先前の通常首位になったタイル数を返します。</summary>
        public int GetNormalWinnerCount(BiomeId biome) => normalWinnerCounts[(int)biome];

        /// <summary>指定Biome候補の指定軸における平均適性を0〜65,535で返します。候補なしは0です。</summary>
        public double GetAverageAxisFit(BiomeId biome, BiomeAuditAxis axis)
        {
            int count = GetCandidateCount(biome);
            return count == 0 ? 0d : (double)axisFitSums[GetAxisIndex(biome, axis)] / count;
        }

        /// <summary>指定Biome候補の指定軸について、適性とDefinition重みを掛けた平均Score寄与を返します。</summary>
        public double GetAverageAxisContribution(BiomeId biome, BiomeAuditAxis axis)
        {
            int count = GetCandidateCount(biome);
            return count == 0 ? 0d : (double)axisContributionSums[GetAxisIndex(biome, axis)] / count;
        }

        /// <summary>順序に依存せず、指定Biomeペアの陸地隣接辺数を返します。</summary>
        public int GetAdjacencyCount(BiomeId first, BiomeId second) => adjacencyCounts[GetPairIndex(first, second)];

        /// <summary>指定した異種Biomeペアで、該当理由が成立した境界数を返します。</summary>
        public int GetBoundaryReasonCount(BiomeId first, BiomeId second, BiomeBoundaryReasonFlags reason)
        {
            int reasonIndex = GetSingleReasonIndex(reason);
            return boundaryReasonCounts[(GetPairIndex(first, second) * BiomeDistributionAnalyzer.BoundaryReasonCount) + reasonIndex];
        }

        /// <summary>指定した異種Biomeペアの、指定連続環境軸における平均絶対差を返します。</summary>
        public double GetAverageBoundaryDifference(BiomeId first, BiomeId second, BiomeBoundaryAxis axis)
        {
            int pairIndex = GetPairIndex(first, second);
            int count = adjacencyCounts[pairIndex];
            return count == 0 ? 0d : (double)boundaryDifferenceSums[(pairIndex * BiomeDistributionAnalyzer.BoundaryAxisCount) + (int)axis] / count;
        }

        /// <summary>指定した異種Biomeペアの、指定連続環境軸における最大絶対差を返します。</summary>
        public ushort GetMaximumBoundaryDifference(BiomeId first, BiomeId second, BiomeBoundaryAxis axis)
            => boundaryDifferenceMaxima[(GetPairIndex(first, second) * BiomeDistributionAnalyzer.BoundaryAxisCount) + (int)axis];

        /// <summary>指定Biomeの4近傍連結領域数を返します。</summary>
        public int GetRegionCount(BiomeId biome) => regionCounts[(int)biome];

        /// <summary>指定Biomeの最大4近傍連結領域面積をタイル数で返します。</summary>
        public int GetMaximumRegionArea(BiomeId biome) => maximumRegionAreas[(int)biome];

        /// <summary>指定Biomeの面積1〜3タイル領域数を返します。</summary>
        public int GetSmallRegionCount(BiomeId biome) => smallRegionCounts[(int)biome];

        /// <summary>指定Biomeのうち監査窓端へ接した領域数を返します。</summary>
        public int GetClippedRegionCount(BiomeId biome) => clippedRegionCounts[(int)biome];

        /// <summary>Biomeと軸を一次元配列へ格納する固定添字を返します。</summary>
        private static int GetAxisIndex(BiomeId biome, BiomeAuditAxis axis)
            => ((int)biome * BiomeDistributionAnalyzer.CandidateAxisCount) + (int)axis;

        /// <summary>順不同Biomeペアを一次元配列へ格納する固定添字を返します。</summary>
        private static int GetPairIndex(BiomeId first, BiomeId second)
        {
            int low = Math.Min((int)first, (int)second);
            int high = Math.Max((int)first, (int)second);
            return (low * BiomeDistributionAnalyzer.BiomeSlotCount) + high;
        }

        /// <summary>1bitだけ指定された理由フラグを固定配列の添字へ変換します。</summary>
        private static int GetSingleReasonIndex(BiomeBoundaryReasonFlags reason)
        {
            for (int index = 0; index < BiomeDistributionAnalyzer.BoundaryReasonCount; index++)
            {
                if (reason == (BiomeBoundaryReasonFlags)(1 << index))
                {
                    return index;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(reason), "境界理由は1種類だけ指定してください。");
        }

        /// <summary>全18Biomeと全153異種ペアを省略せず記載した再現可能なレポートを作ります。</summary>
        private string BuildReport()
        {
            var builder = new StringBuilder(32768);
            builder.AppendLine("Biome分布・境界監査");
            builder.AppendLine($"Seed={SeedCount}, Chunks={TotalChunks}, Tiles={TotalTiles}, Land={LandTiles}, Ocean={OceanTiles}");
            builder.AppendLine($"LandEdges={TotalLandAdjacencyEdges}, DifferentEdges={DifferentBiomeEdges}, Signature=0x{Signature:X16}");
            builder.AppendLine($"VolcanicCore={VolcanicCoreSelections}, VolcanicFringe={VolcanicFringeSelections}, MarshPromotion={FreshwaterMarshPromotions}, SwampPromotion={SwampForestPromotions}");
            builder.AppendLine("判定注記: この監査では景観の合否閾値を新設せず、後続の景観調整で比較できる実測値を固定します。");
            builder.AppendLine("Biome別: Area(率) / Candidate(陸地率) / NormalWin(陸地率) / Regions / MaxArea / Small(1-3) / Clipped / 平均Fit(T,M,E,W,R,G,V,C) / 平均Score寄与(T,M,E,W,R,G,V,C)");
            for (int stableId = BiomeStableIds.First; stableId <= BiomeStableIds.Last; stableId++)
            {
                BiomeId biome = (BiomeId)stableId;
                double areaRatio = LandTiles == 0 ? 0d : (double)GetAreaCount(biome) / LandTiles;
                double candidateRatio = LandTiles == 0 ? 0d : (double)GetCandidateCount(biome) / LandTiles;
                double winnerRatio = LandTiles == 0 ? 0d : (double)GetNormalWinnerCount(biome) / LandTiles;
                builder.Append("  ").Append(stableId.ToString("D2", CultureInfo.InvariantCulture))
                    .Append(' ').Append(biome).Append(": ")
                    .Append(GetAreaCount(biome)).Append('(').Append(areaRatio.ToString("P4", CultureInfo.InvariantCulture)).Append(") / ")
                    .Append(GetCandidateCount(biome)).Append('(').Append(candidateRatio.ToString("P4", CultureInfo.InvariantCulture)).Append(") / ")
                    .Append(GetNormalWinnerCount(biome)).Append('(').Append(winnerRatio.ToString("P4", CultureInfo.InvariantCulture)).Append(") / ")
                    .Append(GetRegionCount(biome)).Append(" / ")
                    .Append(GetMaximumRegionArea(biome)).Append(" / ")
                    .Append(GetSmallRegionCount(biome)).Append(" / ")
                    .Append(GetClippedRegionCount(biome)).Append(" / ");
                for (int axis = 0; axis < BiomeDistributionAnalyzer.CandidateAxisCount; axis++)
                {
                    if (axis > 0)
                    {
                        builder.Append(',');
                    }

                    builder.Append(GetAverageAxisFit(biome, (BiomeAuditAxis)axis).ToString("F1", CultureInfo.InvariantCulture));
                }

                builder.Append(" / ");
                for (int axis = 0; axis < BiomeDistributionAnalyzer.CandidateAxisCount; axis++)
                {
                    if (axis > 0)
                    {
                        builder.Append(',');
                    }

                    builder.Append(GetAverageAxisContribution(biome, (BiomeAuditAxis)axis).ToString("F1", CultureInfo.InvariantCulture));
                }

                builder.AppendLine();
            }

            builder.AppendLine("全153異種Biomeペア: Edges / 全陸地辺比 / 異種辺比 / 平均Δ(T,M,E,W,R,V,C) / 最大Δ(T,M,E,W,R,V,C) / 理由件数");
            for (int firstId = BiomeStableIds.First; firstId <= BiomeStableIds.Last; firstId++)
            {
                for (int secondId = firstId + 1; secondId <= BiomeStableIds.Last; secondId++)
                {
                    BiomeId first = (BiomeId)firstId;
                    BiomeId second = (BiomeId)secondId;
                    int edges = GetAdjacencyCount(first, second);
                    double allRatio = TotalLandAdjacencyEdges == 0 ? 0d : (double)edges / TotalLandAdjacencyEdges;
                    double differentRatio = DifferentBiomeEdges == 0 ? 0d : (double)edges / DifferentBiomeEdges;
                    builder.Append("  ").Append(firstId.ToString("D2", CultureInfo.InvariantCulture))
                        .Append('-').Append(secondId.ToString("D2", CultureInfo.InvariantCulture))
                        .Append(": ").Append(edges)
                        .Append(" / ").Append(allRatio.ToString("P4", CultureInfo.InvariantCulture))
                        .Append(" / ").Append(differentRatio.ToString("P4", CultureInfo.InvariantCulture))
                        .Append(" / ");
                    AppendBoundaryDifferences(builder, first, second, false);
                    builder.Append(" / ");
                    AppendBoundaryDifferences(builder, first, second, true);
                    builder.Append(" / ");
                    AppendBoundaryReasons(builder, first, second);
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }

        /// <summary>指定ペアの7軸平均差または最大差を短い固定順表現で追記します。</summary>
        private void AppendBoundaryDifferences(StringBuilder builder, BiomeId first, BiomeId second, bool maximum)
        {
            for (int axis = 0; axis < BiomeDistributionAnalyzer.BoundaryAxisCount; axis++)
            {
                if (axis > 0)
                {
                    builder.Append(',');
                }

                if (maximum)
                {
                    builder.Append(GetMaximumBoundaryDifference(first, second, (BiomeBoundaryAxis)axis));
                }
                else
                {
                    builder.Append(GetAverageBoundaryDifference(first, second, (BiomeBoundaryAxis)axis).ToString("F1", CultureInfo.InvariantCulture));
                }
            }
        }

        /// <summary>指定ペアで成立した11理由の件数を固定順で追記します。</summary>
        private void AppendBoundaryReasons(StringBuilder builder, BiomeId first, BiomeId second)
        {
            for (int reasonIndex = 0; reasonIndex < BiomeDistributionAnalyzer.BoundaryReasonCount; reasonIndex++)
            {
                if (reasonIndex > 0)
                {
                    builder.Append(',');
                }

                BiomeBoundaryReasonFlags reason = (BiomeBoundaryReasonFlags)(1 << reasonIndex);
                builder.Append(reason).Append('=').Append(GetBoundaryReasonCount(first, second, reason));
            }
        }
    }
}
