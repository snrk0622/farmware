// このファイルは、複数シードのバイオーム・地形・水域・隣接関係を一定条件で集計し、調整前後を比較できる診断結果を作ります。
// Editorメニューから呼ばれる開発専用処理であり、ランタイムのECS状態や実際のワールド生成結果は変更しません。

using System;
using System.Collections.Generic;
using System.Text;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 分布監査で使用するシード、サンプル範囲、間隔、生成器バージョンをまとめます。
    /// WorldGenerationDistributionAuditMenuとEditModeテストが同じ入力形式を共有します。
    /// </summary>
    public sealed class WorldGenerationDistributionAuditSettings
    {
        /// <summary>監査対象にするワールドシード文字列です。監査開始後は変更されません。</summary>
        public IReadOnlyList<string> SeedTexts { get; }

        /// <summary>原点から縦横へ何サンプル調べるかを保持します。実タイル距離ではありません。</summary>
        public int SampleRadiusInSteps { get; }

        /// <summary>隣り合う代表サンプル間の距離です。単位はワールドタイルです。</summary>
        public int SampleSpacingInTiles { get; }

        /// <summary>監査対象のGeneratorVersionです。通常のワールド再生成と同じ値を渡します。</summary>
        public int GeneratorVersion { get; }

        /// <summary>
        /// 検証条件を固定し、同じ条件の監査結果を調整前後で比較できる設定を作ります。
        /// </summary>
        /// <param name="seedTexts">1件以上のワールドシード文字列です。</param>
        /// <param name="sampleRadiusInSteps">原点から各軸へ調べるサンプル数です。0以上を指定します。</param>
        /// <param name="sampleSpacingInTiles">代表サンプル同士の間隔です。1タイル以上を指定します。</param>
        /// <param name="generatorVersion">生成結果へ渡すGeneratorVersionです。</param>
        public WorldGenerationDistributionAuditSettings(
            string[] seedTexts,
            int sampleRadiusInSteps,
            int sampleSpacingInTiles,
            int generatorVersion)
        {
            if (seedTexts == null || seedTexts.Length == 0)
            {
                throw new ArgumentException("分布監査には1件以上のシードが必要です。", nameof(seedTexts));
            }

            if (sampleRadiusInSteps < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampleRadiusInSteps),
                    "サンプル半径は0以上である必要があります。");
            }

            if (sampleSpacingInTiles <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampleSpacingInTiles),
                    "サンプル間隔は1タイル以上である必要があります。");
            }

            string[] ownedSeedTexts = (string[])seedTexts.Clone();
            for (int seedIndex = 0; seedIndex < ownedSeedTexts.Length; seedIndex++)
            {
                if (string.IsNullOrWhiteSpace(ownedSeedTexts[seedIndex]))
                {
                    throw new ArgumentException(
                        $"分布監査のシード文字列が空です。index={seedIndex}",
                        nameof(seedTexts));
                }
            }

            // 呼び出し元が監査中に配列を書き換えて結果を変えないよう、複製を読み取り専用の型で公開します。
            SeedTexts = ownedSeedTexts;
            SampleRadiusInSteps = sampleRadiusInSteps;
            SampleSpacingInTiles = sampleSpacingInTiles;
            GeneratorVersion = generatorVersion;
        }

        /// <summary>
        /// 通常の開発サイクルでEditorを長時間停止させず、広域傾向を比較できる既定設定を返します。
        /// </summary>
        /// <returns>3シードを32タイル間隔の129×129地点で調べる設定です。</returns>
        public static WorldGenerationDistributionAuditSettings CreateDefault()
        {
            return new WorldGenerationDistributionAuditSettings(
                new[]
                {
                    "distribution-audit-plain",
                    "distribution-audit-river",
                    "distribution-audit-volcanic"
                },
                64,
                32,
                1);
        }
    }

    /// <summary>
    /// 分布監査で集計した件数、直接隣接、概算領域数と、人間が読むConsoleレポートを保持します。
    /// 配列自体は外へ公開せず、列挙値を指定する取得メソッドから読み取ります。
    /// </summary>
    public sealed class WorldGenerationDistributionAuditResult
    {
        /// <summary>BiomeIdの数値を添字にした代表サンプル件数です。</summary>
        private readonly int[] biomeCounts;

        /// <summary>GroundTypeの数値を添字にした代表サンプル件数です。</summary>
        private readonly int[] groundCounts;

        /// <summary>WaterBodyTypeの数値を添字にした代表サンプル件数です。</summary>
        private readonly int[] waterBodyCounts;

        /// <summary>順不同のBiomeIdペアを二次元配列相当で保持する直接隣接件数です。</summary>
        private readonly int[] biomeAdjacencyCounts;

        /// <summary>BiomeIdごとの4近傍連結領域数です。32タイル間隔の概算比較値です。</summary>
        private readonly int[] biomeRegionCounts;

        /// <summary>BiomeIdごとの連結領域へ含まれた代表サンプル総数です。</summary>
        private readonly int[] biomeRegionSampleCounts;

        /// <summary>BiomeIdごとの1サンプルだけで終わった孤立領域数です。</summary>
        private readonly int[] isolatedBiomeRegionCounts;

        /// <summary>1つのBiomeIdを配列で表すときに使用する固定スロット数です。</summary>
        private readonly int biomeSlotCount;

        /// <summary>監査へ使用したシード数です。</summary>
        public int SeedCount { get; }

        /// <summary>各シードで生成した代表サンプル数です。</summary>
        public int SamplesPerSeed { get; }

        /// <summary>すべてのシードを合計した代表サンプル数です。</summary>
        public int TotalAnchorSamples { get; }

        /// <summary>東と北の実際の隣接1タイルを調べたペア総数です。</summary>
        public int TotalDirectAdjacencySamples { get; }

        /// <summary>直接隣接ペアのうち、異なるBiomeId同士だった境界数です。</summary>
        public int DirectBiomeBoundaryCount { get; }

        /// <summary>仕様上強く抑制する組み合わせが直接接続していた境界数です。</summary>
        public int StronglySuppressedBoundaryCount { get; }

        /// <summary>64×64論理チャンク境界をまたいで調べた東西・南北ペア総数です。</summary>
        public int TotalChunkBoundaryAdjacencySamples { get; }

        /// <summary>論理チャンク境界をまたぐ1タイル間でBiomeIdが変化したペア数です。</summary>
        public int ChunkBoundaryBiomeChangeCount { get; }

        /// <summary>代表サンプルの並びと分類結果から作った、調整前後を比較する64bit署名です。</summary>
        public ulong Signature { get; }

        /// <summary>Unity Consoleへそのまま表示できる日本語の集計レポートです。</summary>
        public string Report { get; }

        /// <summary>
        /// Analyzerが完成済みの各集計値を渡し、不変な監査結果を作ります。
        /// </summary>
        /// <param name="biomeCounts">BiomeId別の代表サンプル件数です。</param>
        /// <param name="groundCounts">GroundType別の代表サンプル件数です。</param>
        /// <param name="waterBodyCounts">WaterBodyType別の代表サンプル件数です。</param>
        /// <param name="biomeAdjacencyCounts">順不同のBiomeIdペア別直接隣接件数です。</param>
        /// <param name="biomeRegionCounts">BiomeId別の概算連結領域数です。</param>
        /// <param name="biomeRegionSampleCounts">BiomeId別の領域内代表サンプル総数です。</param>
        /// <param name="isolatedBiomeRegionCounts">BiomeId別の1サンプル孤立領域数です。</param>
        /// <param name="biomeSlotCount">BiomeIdを配列へ格納する固定スロット数です。</param>
        /// <param name="seedCount">監査へ使用したシード数です。</param>
        /// <param name="samplesPerSeed">各シードの代表サンプル数です。</param>
        /// <param name="totalDirectAdjacencySamples">東・北へ調べた直接隣接ペアの総数です。</param>
        /// <param name="directBiomeBoundaryCount">異なるBiomeId同士だった直接境界数です。</param>
        /// <param name="stronglySuppressedBoundaryCount">強い隣接抑制対象だった直接境界数です。</param>
        /// <param name="totalChunkBoundaryAdjacencySamples">論理チャンク境界をまたいで調べたペア総数です。</param>
        /// <param name="chunkBoundaryBiomeChangeCount">論理チャンク境界上でBiomeIdが変化したペア数です。</param>
        /// <param name="signature">走査順の分類結果から作った比較用64bit署名です。</param>
        /// <param name="sampleSpacingInTiles">概算領域面積への換算に使うサンプル間隔です。</param>
        internal WorldGenerationDistributionAuditResult(
            int[] biomeCounts,
            int[] groundCounts,
            int[] waterBodyCounts,
            int[] biomeAdjacencyCounts,
            int[] biomeRegionCounts,
            int[] biomeRegionSampleCounts,
            int[] isolatedBiomeRegionCounts,
            int biomeSlotCount,
            int seedCount,
            int samplesPerSeed,
            int totalDirectAdjacencySamples,
            int directBiomeBoundaryCount,
            int stronglySuppressedBoundaryCount,
            int totalChunkBoundaryAdjacencySamples,
            int chunkBoundaryBiomeChangeCount,
            ulong signature,
            int sampleSpacingInTiles)
        {
            this.biomeCounts = biomeCounts;
            this.groundCounts = groundCounts;
            this.waterBodyCounts = waterBodyCounts;
            this.biomeAdjacencyCounts = biomeAdjacencyCounts;
            this.biomeRegionCounts = biomeRegionCounts;
            this.biomeRegionSampleCounts = biomeRegionSampleCounts;
            this.isolatedBiomeRegionCounts = isolatedBiomeRegionCounts;
            this.biomeSlotCount = biomeSlotCount;
            SeedCount = seedCount;
            SamplesPerSeed = samplesPerSeed;
            TotalAnchorSamples = seedCount * samplesPerSeed;
            TotalDirectAdjacencySamples = totalDirectAdjacencySamples;
            DirectBiomeBoundaryCount = directBiomeBoundaryCount;
            StronglySuppressedBoundaryCount = stronglySuppressedBoundaryCount;
            TotalChunkBoundaryAdjacencySamples = totalChunkBoundaryAdjacencySamples;
            ChunkBoundaryBiomeChangeCount = chunkBoundaryBiomeChangeCount;
            Signature = signature;
            Report = BuildReport(sampleSpacingInTiles);
        }

        /// <summary>指定バイオームの代表サンプル件数を返します。</summary>
        /// <param name="biome">件数を取得するバイオームです。</param>
        /// <returns>すべての監査シードを合計した件数です。</returns>
        public int GetBiomeCount(BiomeId biome)
        {
            return biomeCounts[(int)biome];
        }

        /// <summary>指定地面種別の代表サンプル件数を返します。</summary>
        /// <param name="ground">件数を取得する地面種別です。</param>
        /// <returns>すべての監査シードを合計した件数です。</returns>
        public int GetGroundCount(GroundType ground)
        {
            return groundCounts[(int)ground];
        }

        /// <summary>指定水域種別の代表サンプル件数を返します。</summary>
        /// <param name="waterBody">件数を取得する水域種別です。</param>
        /// <returns>すべての監査シードを合計した件数です。</returns>
        public int GetWaterBodyCount(WaterBodyType waterBody)
        {
            return waterBodyCounts[(int)waterBody];
        }

        /// <summary>指定した2バイオームが直接隣接していた件数を順序に依存せず返します。</summary>
        /// <param name="first">1つ目のバイオームです。</param>
        /// <param name="second">2つ目のバイオームです。</param>
        /// <returns>東または北に1タイル離れた直接隣接ペアの件数です。</returns>
        public int GetBiomeAdjacencyCount(BiomeId first, BiomeId second)
        {
            int firstIndex = Math.Min((int)first, (int)second);
            int secondIndex = Math.Max((int)first, (int)second);
            return biomeAdjacencyCounts[(firstIndex * biomeSlotCount) + secondIndex];
        }

        /// <summary>指定バイオームの概算連結領域数を返します。</summary>
        /// <param name="biome">領域数を取得するバイオームです。</param>
        /// <returns>代表サンプル格子を4近傍で連結した領域数です。</returns>
        public int GetBiomeRegionCount(BiomeId biome)
        {
            return biomeRegionCounts[(int)biome];
        }

        /// <summary>指定バイオームの孤立した1サンプル領域数を返します。</summary>
        /// <param name="biome">孤立領域数を取得するバイオームです。</param>
        /// <returns>代表格子で同種の上下左右を持たない領域数です。</returns>
        public int GetIsolatedBiomeRegionCount(BiomeId biome)
        {
            return isolatedBiomeRegionCounts[(int)biome];
        }

        /// <summary>
        /// 件数を割合とともに整形し、調整前後で人間が比較しやすいConsoleレポートを作ります。
        /// </summary>
        /// <param name="sampleSpacingInTiles">概算領域面積へ換算する代表サンプル間隔です。</param>
        /// <returns>複数行の日本語レポートです。</returns>
        private string BuildReport(int sampleSpacingInTiles)
        {
            StringBuilder builder = new StringBuilder(4096);
            builder.AppendLine("[Farmware] ワールド生成分布監査");
            builder.Append("Seeds=").Append(SeedCount)
                .Append(", Anchors=").Append(TotalAnchorSamples)
                .Append(", DirectPairs=").Append(TotalDirectAdjacencySamples)
                .Append(", Signature=0x").AppendLine(Signature.ToString("X16"));
            builder.AppendLine("Biome distribution:");

            for (BiomeId biome = BiomeId.Grassland; biome <= BiomeId.Volcanic; biome++)
            {
                int biomeIndex = (int)biome;
                int count = biomeCounts[biomeIndex];
                int regionCount = biomeRegionCounts[biomeIndex];
                float averageRegionSamples = regionCount > 0
                    ? biomeRegionSampleCounts[biomeIndex] / (float)regionCount
                    : 0f;
                float estimatedArea = averageRegionSamples
                    * sampleSpacingInTiles
                    * sampleSpacingInTiles;
                builder.Append("  ").Append(biome)
                    .Append(": ").Append(count)
                    .Append(" (").Append(FormatPercent(count, TotalAnchorSamples)).Append(")")
                    .Append(", Regions=").Append(regionCount)
                    .Append(", Isolated=").Append(isolatedBiomeRegionCounts[biomeIndex])
                    .Append(", AvgArea≈").Append(estimatedArea.ToString("F0"))
                    .AppendLine(" tiles");
            }

            builder.AppendLine("Ground distribution:");
            for (GroundType ground = GroundType.Normal; ground <= GroundType.Magma; ground++)
            {
                int count = groundCounts[(int)ground];
                builder.Append("  ").Append(ground)
                    .Append(": ").Append(count)
                    .Append(" (").Append(FormatPercent(count, TotalAnchorSamples)).AppendLine(")");
            }

            builder.AppendLine("Water body distribution:");
            for (WaterBodyType waterBody = WaterBodyType.River;
                 waterBody <= WaterBodyType.Sea;
                 waterBody++)
            {
                int count = waterBodyCounts[(int)waterBody];
                builder.Append("  ").Append(waterBody)
                    .Append(": ").Append(count)
                    .Append(" (").Append(FormatPercent(count, TotalAnchorSamples)).AppendLine(")");
            }

            builder.Append("Direct biome boundaries: ")
                .Append(DirectBiomeBoundaryCount)
                .Append(", Strongly suppressed: ")
                .Append(StronglySuppressedBoundaryCount)
                .Append(" (")
                .Append(FormatPercent(StronglySuppressedBoundaryCount, DirectBiomeBoundaryCount))
                .AppendLine(")");
            builder.Append("Chunk-edge biome changes: ")
                .Append(ChunkBoundaryBiomeChangeCount)
                .Append(" / ").Append(TotalChunkBoundaryAdjacencySamples)
                .Append(" (")
                .Append(FormatPercent(
                    ChunkBoundaryBiomeChangeCount,
                    TotalChunkBoundaryAdjacencySamples))
                .AppendLine(")");

            for (BiomeId first = BiomeId.Grassland; first <= BiomeId.Volcanic; first++)
            {
                for (BiomeId second = (BiomeId)((int)first + 1);
                     second <= BiomeId.Volcanic;
                     second++)
                {
                    int count = GetBiomeAdjacencyCount(first, second);
                    if (count == 0)
                    {
                        continue;
                    }

                    // 強い抑制対象へ印を付け、許可隣接と見分けられるようにします。件数自体には合否を設けず調整材料として扱います。
                    string marker = DeterministicChunkGenerator.IsStronglySuppressedBiomePair(first, second)
                        ? " [suppressed]"
                        : string.Empty;
                    builder.Append("  ").Append(first).Append(" <-> ").Append(second)
                        .Append(": ").Append(count).AppendLine(marker);
                }
            }

            return builder.ToString();
        }

        /// <summary>分母0を安全に扱い、割合を小数第2位までの文字列へ変換します。</summary>
        /// <param name="count">割合の分子です。</param>
        /// <param name="total">割合の分母です。</param>
        /// <returns>0.00%形式の文字列です。</returns>
        private static string FormatPercent(int count, int total)
        {
            return total > 0 ? ((count * 100f) / total).ToString("F2") + "%" : "0.00%";
        }
    }

    /// <summary>
    /// 決定論的ワールド生成器を読み取り、複数シードの分布監査結果を作る純粋なEditor用集計器です。
    /// UnityEngine.Objectへ触れないため、Editorメニューからバックグラウンドスレッドで実行できます。
    /// </summary>
    public static class WorldGenerationDistributionAnalyzer
    {
        /// <summary>64bit比較署名の初期値として使うFNV-1a offset basisです。</summary>
        private const ulong SignatureOffsetBasis = 14695981039346656037UL;

        /// <summary>各分類値を比較署名へ混ぜるFNV-1a primeです。</summary>
        private const ulong SignaturePrime = 1099511628211UL;

        /// <summary>BiomeId.NoneからVolcanicまでを格納できる配列スロット数です。</summary>
        private const int BiomeSlotCount = (int)BiomeId.Volcanic + 1;

        /// <summary>GroundType.NormalからMagmaまでを格納できる配列スロット数です。</summary>
        private const int GroundSlotCount = (int)GroundType.Magma + 1;

        /// <summary>WaterBodyType.NoneからSeaまでを格納できる配列スロット数です。</summary>
        private const int WaterBodySlotCount = (int)WaterBodyType.Sea + 1;

        /// <summary>
        /// 指定条件で代表タイルと東・北の直接隣接を生成し、分布と概算領域を集計します。
        /// 呼び出し中にアセット、Scene、ECS Worldは変更しません。
        /// </summary>
        /// <param name="auditSettings">固定シード、範囲、サンプル間隔、GeneratorVersionです。</param>
        /// <returns>件数、署名、Console表示用レポートを含む監査結果です。</returns>
        public static WorldGenerationDistributionAuditResult Analyze(
            WorldGenerationDistributionAuditSettings auditSettings)
        {
            if (auditSettings == null)
            {
                throw new ArgumentNullException(nameof(auditSettings));
            }

            int gridWidth = (auditSettings.SampleRadiusInSteps * 2) + 1;
            int samplesPerSeed = gridWidth * gridWidth;
            int[] biomeCounts = new int[BiomeSlotCount];
            int[] groundCounts = new int[GroundSlotCount];
            int[] waterBodyCounts = new int[WaterBodySlotCount];
            int[] adjacencyCounts = new int[BiomeSlotCount * BiomeSlotCount];
            int[] regionCounts = new int[BiomeSlotCount];
            int[] regionSampleCounts = new int[BiomeSlotCount];
            int[] isolatedRegionCounts = new int[BiomeSlotCount];
            int totalAdjacencySamples = 0;
            int directBoundaryCount = 0;
            int suppressedBoundaryCount = 0;
            int totalChunkBoundarySamples = 0;
            int chunkBoundaryBiomeChangeCount = 0;
            ulong signature = SignatureOffsetBasis;

            for (int seedIndex = 0; seedIndex < auditSettings.SeedTexts.Count; seedIndex++)
            {
                WorldGenerationSettings generationSettings = WorldGenerationSettings.CreateDefault(
                    WorldSeedUtility.FromString(auditSettings.SeedTexts[seedIndex]),
                    auditSettings.GeneratorVersion);
                BiomeId[] biomeGrid = new BiomeId[samplesPerSeed];

                // 広域分布は一定間隔の代表タイルで数え、隣接抑制は各代表点から実際に1タイル離れた東・北を別途調べます。
                for (int gridY = 0; gridY < gridWidth; gridY++)
                {
                    int worldTileY = (gridY - auditSettings.SampleRadiusInSteps)
                        * auditSettings.SampleSpacingInTiles;
                    for (int gridX = 0; gridX < gridWidth; gridX++)
                    {
                        int worldTileX = (gridX - auditSettings.SampleRadiusInSteps)
                            * auditSettings.SampleSpacingInTiles;
                        GeneratedTile anchor = DeterministicChunkGenerator.GenerateTile(
                            generationSettings,
                            worldTileX,
                            worldTileY);
                        GeneratedTile east = DeterministicChunkGenerator.GenerateTile(
                            generationSettings,
                            worldTileX + 1,
                            worldTileY);
                        GeneratedTile north = DeterministicChunkGenerator.GenerateTile(
                            generationSettings,
                            worldTileX,
                            worldTileY + 1);

                        int gridIndex = (gridY * gridWidth) + gridX;
                        biomeGrid[gridIndex] = anchor.Biome;
                        biomeCounts[(int)anchor.Biome]++;
                        groundCounts[(int)anchor.Ground]++;
                        waterBodyCounts[(int)anchor.WaterBodyType]++;
                        CountAdjacency(
                            anchor.Biome,
                            east.Biome,
                            adjacencyCounts,
                            ref totalAdjacencySamples,
                            ref directBoundaryCount,
                            ref suppressedBoundaryCount);
                        CountAdjacency(
                            anchor.Biome,
                            north.Biome,
                            adjacencyCounts,
                            ref totalAdjacencySamples,
                            ref directBoundaryCount,
                            ref suppressedBoundaryCount);

                        // 件数が同じでも配置が変わった場合を検出できるよう、走査順の分類値を署名へ順番に混ぜます。
                        MixSignature(ref signature, (byte)anchor.Biome);
                        MixSignature(ref signature, (byte)anchor.Ground);
                        MixSignature(ref signature, (byte)anchor.WaterBodyType);
                        MixSignature(ref signature, (byte)east.Biome);
                        MixSignature(ref signature, (byte)north.Biome);
                    }
                }

                CountApproximateRegions(
                    biomeGrid,
                    gridWidth,
                    regionCounts,
                    regionSampleCounts,
                    isolatedRegionCounts);
                CountChunkBoundaryChanges(
                    generationSettings,
                    auditSettings.SampleRadiusInSteps,
                    auditSettings.SampleSpacingInTiles,
                    ref totalChunkBoundarySamples,
                    ref chunkBoundaryBiomeChangeCount);
            }

            return new WorldGenerationDistributionAuditResult(
                biomeCounts,
                groundCounts,
                waterBodyCounts,
                adjacencyCounts,
                regionCounts,
                regionSampleCounts,
                isolatedRegionCounts,
                BiomeSlotCount,
                auditSettings.SeedTexts.Count,
                samplesPerSeed,
                totalAdjacencySamples,
                directBoundaryCount,
                suppressedBoundaryCount,
                totalChunkBoundarySamples,
                chunkBoundaryBiomeChangeCount,
                signature,
                auditSettings.SampleSpacingInTiles);
        }

        /// <summary>
        /// 1組の直接隣接を順不同の行列へ加算し、異種境界と強い抑制対象を同時に数えます。
        /// </summary>
        /// <param name="first">代表地点側のバイオームです。</param>
        /// <param name="second">東または北へ1タイル離れた地点のバイオームです。</param>
        /// <param name="adjacencyCounts">順不同ペアの件数を蓄積する二次元配列相当です。</param>
        /// <param name="totalAdjacencySamples">調査した直接隣接ペア総数へ1を加えます。</param>
        /// <param name="directBoundaryCount">異なるバイオーム同士の境界総数へ必要なら1を加えます。</param>
        /// <param name="suppressedBoundaryCount">強い隣接抑制対象の境界総数へ必要なら1を加えます。</param>
        private static void CountAdjacency(
            BiomeId first,
            BiomeId second,
            int[] adjacencyCounts,
            ref int totalAdjacencySamples,
            ref int directBoundaryCount,
            ref int suppressedBoundaryCount)
        {
            int firstIndex = Math.Min((int)first, (int)second);
            int secondIndex = Math.Max((int)first, (int)second);
            adjacencyCounts[(firstIndex * BiomeSlotCount) + secondIndex]++;
            totalAdjacencySamples++;

            if (first == second)
            {
                return;
            }

            directBoundaryCount++;
            if (DeterministicChunkGenerator.IsStronglySuppressedBiomePair(first, second))
            {
                suppressedBoundaryCount++;
            }
        }

        /// <summary>
        /// 一定間隔の代表格子を4近傍で塗りつぶし、調整前後比較用の概算領域数と孤立数を数えます。
        /// </summary>
        /// <param name="biomeGrid">1シード分の代表格子に記録したバイオームです。</param>
        /// <param name="gridWidth">正方形の代表格子の1辺です。単位はサンプルです。</param>
        /// <param name="regionCounts">バイオーム別の連結領域数を蓄積します。</param>
        /// <param name="regionSampleCounts">バイオーム別の領域内代表サンプル総数を蓄積します。</param>
        /// <param name="isolatedRegionCounts">バイオーム別の1サンプル孤立領域数を蓄積します。</param>
        private static void CountApproximateRegions(
            BiomeId[] biomeGrid,
            int gridWidth,
            int[] regionCounts,
            int[] regionSampleCounts,
            int[] isolatedRegionCounts)
        {
            bool[] visited = new bool[biomeGrid.Length];
            int[] queue = new int[biomeGrid.Length];

            for (int startIndex = 0; startIndex < biomeGrid.Length; startIndex++)
            {
                if (visited[startIndex])
                {
                    continue;
                }

                BiomeId biome = biomeGrid[startIndex];
                int queueReadIndex = 0;
                int queueWriteIndex = 1;
                int componentSize = 0;
                queue[0] = startIndex;
                visited[startIndex] = true;

                // 配列をキューとして再利用し、領域ごとのList生成とGC Allocを避けながら同種4近傍だけを巡回します。
                while (queueReadIndex < queueWriteIndex)
                {
                    int currentIndex = queue[queueReadIndex++];
                    int currentX = currentIndex % gridWidth;
                    int currentY = currentIndex / gridWidth;
                    componentSize++;
                    TryEnqueueSameBiome(
                        currentX - 1,
                        currentY,
                        biome,
                        biomeGrid,
                        gridWidth,
                        visited,
                        queue,
                        ref queueWriteIndex);
                    TryEnqueueSameBiome(
                        currentX + 1,
                        currentY,
                        biome,
                        biomeGrid,
                        gridWidth,
                        visited,
                        queue,
                        ref queueWriteIndex);
                    TryEnqueueSameBiome(
                        currentX,
                        currentY - 1,
                        biome,
                        biomeGrid,
                        gridWidth,
                        visited,
                        queue,
                        ref queueWriteIndex);
                    TryEnqueueSameBiome(
                        currentX,
                        currentY + 1,
                        biome,
                        biomeGrid,
                        gridWidth,
                        visited,
                        queue,
                        ref queueWriteIndex);
                }

                int biomeIndex = (int)biome;
                regionCounts[biomeIndex]++;
                regionSampleCounts[biomeIndex] += componentSize;
                if (componentSize == 1)
                {
                    isolatedRegionCounts[biomeIndex]++;
                }
            }
        }

        /// <summary>
        /// 監査範囲内の64×64論理チャンク境界をまたぐ1タイルペアを調べ、境界線だけに分類変化が集中していないか比較できる件数を作ります。
        /// </summary>
        /// <param name="generationSettings">対象シードのワールド生成設定です。</param>
        /// <param name="sampleRadiusInSteps">原点から各軸へ調べる代表サンプル数です。</param>
        /// <param name="sampleSpacingInTiles">境界線に沿って調べる座標間隔です。単位はタイルです。</param>
        /// <param name="totalChunkBoundarySamples">調べた東西・南北ペア総数を蓄積します。</param>
        /// <param name="chunkBoundaryBiomeChangeCount">境界をまたいでBiomeIdが変わったペア数を蓄積します。</param>
        private static void CountChunkBoundaryChanges(
            in WorldGenerationSettings generationSettings,
            int sampleRadiusInSteps,
            int sampleSpacingInTiles,
            ref int totalChunkBoundarySamples,
            ref int chunkBoundaryBiomeChangeCount)
        {
            int minimumWorldTile = -sampleRadiusInSteps * sampleSpacingInTiles;
            int maximumWorldTile = sampleRadiusInSteps * sampleSpacingInTiles;
            int minimumChunkIndex = CeilingDivide(
                minimumWorldTile,
                WorldGridConstants.LogicalChunkSize);
            int maximumChunkIndex = WorldGrid.FloorDivide(
                maximumWorldTile,
                WorldGridConstants.LogicalChunkSize);

            // 各チャンク開始座標の直前と開始セルを組にし、正座標と負座標で同じ規則を検査します。
            for (int chunkIndex = minimumChunkIndex; chunkIndex <= maximumChunkIndex; chunkIndex++)
            {
                int boundaryTile = chunkIndex * WorldGridConstants.LogicalChunkSize;
                for (int sampleStep = -sampleRadiusInSteps;
                     sampleStep <= sampleRadiusInSteps;
                     sampleStep++)
                {
                    int alongBoundaryTile = sampleStep * sampleSpacingInTiles;
                    GeneratedTile west = DeterministicChunkGenerator.GenerateTile(
                        generationSettings,
                        boundaryTile - 1,
                        alongBoundaryTile);
                    GeneratedTile east = DeterministicChunkGenerator.GenerateTile(
                        generationSettings,
                        boundaryTile,
                        alongBoundaryTile);
                    GeneratedTile south = DeterministicChunkGenerator.GenerateTile(
                        generationSettings,
                        alongBoundaryTile,
                        boundaryTile - 1);
                    GeneratedTile north = DeterministicChunkGenerator.GenerateTile(
                        generationSettings,
                        alongBoundaryTile,
                        boundaryTile);

                    totalChunkBoundarySamples += 2;
                    chunkBoundaryBiomeChangeCount += west.Biome != east.Biome ? 1 : 0;
                    chunkBoundaryBiomeChangeCount += south.Biome != north.Biome ? 1 : 0;
                }
            }
        }

        /// <summary>負座標でも数学的な上方向へ丸める整数除算を行い、範囲内最初のチャンク境界を求めます。</summary>
        /// <param name="value">割られる符号付きワールドタイル座標です。</param>
        /// <param name="divisor">正の論理チャンクサイズです。</param>
        /// <returns>実数除算の結果を正の無限大方向へ丸めた整数です。</returns>
        private static int CeilingDivide(int value, int divisor)
        {
            return -WorldGrid.FloorDivide(-value, divisor);
        }

        /// <summary>格子内かつ同じバイオームで未訪問の代表点だけを塗りつぶしキューへ追加します。</summary>
        /// <param name="gridX">追加候補の水平サンプル座標です。</param>
        /// <param name="gridY">追加候補の垂直サンプル座標です。</param>
        /// <param name="expectedBiome">現在塗りつぶしている領域のバイオームです。</param>
        /// <param name="biomeGrid">1シード分の代表格子です。</param>
        /// <param name="gridWidth">正方形の代表格子の1辺です。</param>
        /// <param name="visited">すでにキューへ追加した地点を示す配列です。</param>
        /// <param name="queue">追加済み地点を保持する再利用キューです。</param>
        /// <param name="queueWriteIndex">次の書き込み位置で、追加時に1増やします。</param>
        private static void TryEnqueueSameBiome(
            int gridX,
            int gridY,
            BiomeId expectedBiome,
            BiomeId[] biomeGrid,
            int gridWidth,
            bool[] visited,
            int[] queue,
            ref int queueWriteIndex)
        {
            if (gridX < 0 || gridX >= gridWidth || gridY < 0 || gridY >= gridWidth)
            {
                return;
            }

            int candidateIndex = (gridY * gridWidth) + gridX;
            if (visited[candidateIndex] || biomeGrid[candidateIndex] != expectedBiome)
            {
                return;
            }

            visited[candidateIndex] = true;
            queue[queueWriteIndex++] = candidateIndex;
        }

        /// <summary>1byteの分類値を走査順の比較署名へ混ぜます。</summary>
        /// <param name="signature">更新対象の64bit署名です。</param>
        /// <param name="value">今回混ぜるバイオーム・地面・水域の分類値です。</param>
        private static void MixSignature(ref ulong signature, byte value)
        {
            signature ^= value;
            signature *= SignaturePrime;
        }
    }
}
