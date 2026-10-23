// このファイルは、C4-E1aの完成水文チャンクを複数Seed・有限チャンク範囲へ適用し、C4の派生水文分布を集計します。
// EditorメニューとEditModeテストが同じ集計器を使い、Scene、ECS World、生成アセットを変更せず比較レポートを作ります。

using System;
using System.Globalization;
using System.Text;
using Farmware.Simulation;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 複数Seedで観測した地下水、湖、泉、自然冠水、湿原の件数・極値・比較署名です。
    /// 固定の合否閾値は持たず、0件を含む分布変化を人間が可視化と合わせて判断するために使用します。
    /// </summary>
    public sealed class DerivedHydrologyDistributionAuditResult
    {
        /// <summary>監査に使用したSeed数です。</summary>
        public int SeedCount { get; }

        /// <summary>各Seedで原点から正負へ調べた論理チャンク半径です。</summary>
        public int ChunkRadius { get; }

        /// <summary>全Seedで完成生成した論理チャンク数です。</summary>
        public long TotalChunks { get; }

        /// <summary>集計した8×8固定水文セルの総数です。</summary>
        public long TotalHydrologyCells { get; }

        /// <summary>集計した1×1自然水文タイルの総数です。</summary>
        public long TotalTiles { get; }

        /// <summary>地下水接続bitを持つ水文セル数です。</summary>
        public long GroundwaterConnectedCellCount { get; }

        /// <summary>自然土壌飽和ポテンシャルが固定閾値以上のタイル数です。</summary>
        public long SaturatedTileCount { get; }

        /// <summary>最終区分が一時湖の水文セル数です。</summary>
        public long TemporaryLakeCellCount { get; }

        /// <summary>最終区分が恒久塩湖の水文セル数です。</summary>
        public long PermanentSaltwaterLakeCellCount { get; }

        /// <summary>最終区分が閉鎖淡水湖の水文セル数です。</summary>
        public long ClosedFreshwaterLakeCellCount { get; }

        /// <summary>最終区分が越流淡水湖の水文セル数です。</summary>
        public long OverflowFreshwaterLakeCellCount { get; }

        /// <summary>泉流として確定した表流水タイル数です。</summary>
        public long SpringStreamTileCount { get; }

        /// <summary>自然冠水ポテンシャルが固定閾値以上のタイル数です。</summary>
        public long FloodedTileCount { get; }

        /// <summary>湿原タイル資格bitを持つタイル数です。</summary>
        public long WetlandEligibleTileCount { get; }

        /// <summary>広域湿原規模bitを持つタイル数です。</summary>
        public long LargeWetlandExtentTileCount { get; }

        /// <summary>監査範囲で観測した最大地下水ポテンシャルです。</summary>
        public ushort MaximumGroundwaterPotential { get; }

        /// <summary>監査範囲で観測した最大自然土壌飽和ポテンシャルです。</summary>
        public ushort MaximumSoilSaturationPotential { get; }

        /// <summary>監査範囲で観測した最大自然冠水ポテンシャルです。</summary>
        public ushort MaximumNaturalFloodingPotential { get; }

        /// <summary>Seed順、チャンク順、監査対象のセル値とタイル全固定値から作る64bit比較署名です。</summary>
        public ulong Signature { get; }

        /// <summary>Unity Consoleへそのまま出力できる日本語の監査レポートです。</summary>
        public string Report { get; }

        /// <summary>集計器が完成済みの件数、極値、署名を不変結果へ変換します。</summary>
        /// <param name="seedCount">監査したSeed数です。</param>
        /// <param name="chunkRadius">各Seedで走査したチャンク半径です。</param>
        /// <param name="counts">完成済みの全累計値です。</param>
        /// <param name="signature">走査順に監査対象のセル値とタイル全固定値を混ぜた比較署名です。</param>
        internal DerivedHydrologyDistributionAuditResult(
            int seedCount,
            int chunkRadius,
            in DerivedHydrologyDistributionCounts counts,
            ulong signature)
        {
            SeedCount = seedCount;
            ChunkRadius = chunkRadius;
            TotalChunks = counts.TotalChunks;
            TotalHydrologyCells = counts.TotalHydrologyCells;
            TotalTiles = counts.TotalTiles;
            GroundwaterConnectedCellCount = counts.GroundwaterConnectedCellCount;
            SaturatedTileCount = counts.SaturatedTileCount;
            TemporaryLakeCellCount = counts.TemporaryLakeCellCount;
            PermanentSaltwaterLakeCellCount = counts.PermanentSaltwaterLakeCellCount;
            ClosedFreshwaterLakeCellCount = counts.ClosedFreshwaterLakeCellCount;
            OverflowFreshwaterLakeCellCount = counts.OverflowFreshwaterLakeCellCount;
            SpringStreamTileCount = counts.SpringStreamTileCount;
            FloodedTileCount = counts.FloodedTileCount;
            WetlandEligibleTileCount = counts.WetlandEligibleTileCount;
            LargeWetlandExtentTileCount = counts.LargeWetlandExtentTileCount;
            MaximumGroundwaterPotential = counts.MaximumGroundwaterPotential;
            MaximumSoilSaturationPotential = counts.MaximumSoilSaturationPotential;
            MaximumNaturalFloodingPotential = counts.MaximumNaturalFloodingPotential;
            Signature = signature;
            Report = BuildReport();
        }

        /// <summary>全分類を0件も省略せず、比較用の複数行レポートへ整形します。</summary>
        /// <returns>母数、件数、割合、極値、比較署名を含む日本語文字列です。</returns>
        private string BuildReport()
        {
            StringBuilder builder = new StringBuilder(1024);
            builder.AppendLine("[Farmware] TERRAIN-01C4-E1b 派生水文分布監査");
            builder.Append("Seeds=").Append(SeedCount)
                .Append(", ChunkRadius=").Append(ChunkRadius)
                .Append(", Chunks=").AppendLine(TotalChunks.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("※ 分布比較用の診断値であり、0件を失敗にする固定の合否閾値は設定していません。");
            builder.Append("HydrologyCells=").Append(TotalHydrologyCells)
                .Append(", GroundwaterConnected=").Append(GroundwaterConnectedCellCount)
                .Append(" (").Append(FormatPercent(GroundwaterConnectedCellCount, TotalHydrologyCells)).Append("%)")
                .Append(", MaxGroundwater=").AppendLine(MaximumGroundwaterPotential.ToString(CultureInfo.InvariantCulture));
            builder.Append("LakeCells: Temporary=").Append(TemporaryLakeCellCount)
                .Append(", PermanentSaltwater=").Append(PermanentSaltwaterLakeCellCount)
                .Append(", ClosedFreshwater=").Append(ClosedFreshwaterLakeCellCount)
                .Append(", OverflowFreshwater=").AppendLine(OverflowFreshwaterLakeCellCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("Tiles=").Append(TotalTiles)
                .Append(", Saturated=").Append(SaturatedTileCount)
                .Append(" (").Append(FormatPercent(SaturatedTileCount, TotalTiles)).Append("%)")
                .Append(", Flooded=").Append(FloodedTileCount)
                .Append(" (").Append(FormatPercent(FloodedTileCount, TotalTiles)).AppendLine("%)");
            builder.Append("WetlandEligible=").Append(WetlandEligibleTileCount)
                .Append(" (").Append(FormatPercent(WetlandEligibleTileCount, TotalTiles)).Append("%)")
                .Append(", LargeWetlandExtent=").Append(LargeWetlandExtentTileCount)
                .Append(" (").Append(FormatPercent(LargeWetlandExtentTileCount, TotalTiles)).AppendLine("%)");
            builder.Append("SpringStreamTiles=").AppendLine(SpringStreamTileCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("MaxSoilSaturation=").Append(MaximumSoilSaturationPotential)
                .Append(", MaxNaturalFlooding=").AppendLine(MaximumNaturalFloodingPotential.ToString(CultureInfo.InvariantCulture));
            builder.Append("Signature=0x").AppendLine(Signature.ToString("X16", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        /// <summary>件数を母数に対する小数第3位までの百分率へ変換します。</summary>
        /// <param name="count">割合の分子となる観測件数です。</param>
        /// <param name="total">割合の分母となる総観測数です。</param>
        /// <returns>母数0では0.000、それ以外はInvariantCultureの百分率文字列です。</returns>
        private static string FormatPercent(long count, long total)
        {
            return total == 0
                ? "0.000"
                : ((count * 100.0) / total).ToString("F3", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>派生水文監査中だけ変更し、完成時に不変結果へコピーする累計値です。</summary>
    internal struct DerivedHydrologyDistributionCounts
    {
        /// <summary>完成生成した論理チャンク累計です。</summary>
        public long TotalChunks;
        /// <summary>走査した固定水文セル累計です。</summary>
        public long TotalHydrologyCells;
        /// <summary>走査した自然水文タイル累計です。</summary>
        public long TotalTiles;
        /// <summary>地下水接続セル累計です。</summary>
        public long GroundwaterConnectedCellCount;
        /// <summary>自然飽和タイル累計です。</summary>
        public long SaturatedTileCount;
        /// <summary>一時湖セル累計です。</summary>
        public long TemporaryLakeCellCount;
        /// <summary>恒久塩湖セル累計です。</summary>
        public long PermanentSaltwaterLakeCellCount;
        /// <summary>閉鎖淡水湖セル累計です。</summary>
        public long ClosedFreshwaterLakeCellCount;
        /// <summary>越流淡水湖セル累計です。</summary>
        public long OverflowFreshwaterLakeCellCount;
        /// <summary>泉流タイル累計です。</summary>
        public long SpringStreamTileCount;
        /// <summary>自然冠水タイル累計です。</summary>
        public long FloodedTileCount;
        /// <summary>湿原資格タイル累計です。</summary>
        public long WetlandEligibleTileCount;
        /// <summary>広域湿原規模タイル累計です。</summary>
        public long LargeWetlandExtentTileCount;
        /// <summary>観測した最大地下水ポテンシャルです。</summary>
        public ushort MaximumGroundwaterPotential;
        /// <summary>観測した最大自然飽和ポテンシャルです。</summary>
        public ushort MaximumSoilSaturationPotential;
        /// <summary>観測した最大自然冠水ポテンシャルです。</summary>
        public ushort MaximumNaturalFloodingPotential;
    }

    /// <summary>
    /// C4-E1aの完成診断チャンクを決定論的な順序で生成し、派生水文分布を数えるEditor専用集計器です。
    /// 各チャンクをusingで直ちに破棄し、製品ECS WorldやSceneへ状態を書き込みません。
    /// </summary>
    public static class DerivedHydrologyDistributionAnalyzer
    {
        /// <summary>空入力と実データを区別する比較署名の初期値です。</summary>
        private const ulong SignatureOffsetBasis = 14695981039346656037UL;

        /// <summary>C4-E1bの手動監査に使う3Seed・各1チャンクの有限条件を作ります。</summary>
        /// <returns>完成チャンク3件を比較する既定設定です。</returns>
        public static HydrologyDistributionAuditSettings CreateDefaultSettings()
        {
            return new HydrologyDistributionAuditSettings(
                new[]
                {
                    "derived-hydrology-audit-a",
                    "derived-hydrology-audit-b",
                    "derived-hydrology-audit-c"
                },
                0,
                WorldGenerationSettings.CurrentGeneratorVersion);
        }

        /// <summary>指定した複数Seed・有限チャンク範囲を完成生成し、件数・極値・比較署名を返します。</summary>
        /// <param name="auditSettings">検証済みのSeed一覧、チャンク半径、GeneratorVersionです。</param>
        /// <returns>0件分類も保持する派生水文分布監査結果です。</returns>
        public static DerivedHydrologyDistributionAuditResult Analyze(
            HydrologyDistributionAuditSettings auditSettings)
        {
            if (auditSettings == null)
            {
                throw new ArgumentNullException(nameof(auditSettings));
            }

            DerivedHydrologyDistributionCounts counts = default;
            ulong signature = SignatureOffsetBasis;
            BiomeClassificationSettings biomeSettings = BiomeDefinitionCatalog.CreateDefault();
            for (int seedIndex = 0; seedIndex < auditSettings.SeedTexts.Count; seedIndex++)
            {
                WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                    WorldSeedUtility.FromString(auditSettings.SeedTexts[seedIndex]),
                    auditSettings.GeneratorVersion);

                // Seed、Y、Xの固定順に走査することで、ロード順やThreadPoolの実行順を署名へ混ぜません。
                for (int chunkY = -auditSettings.ChunkRadius;
                     chunkY <= auditSettings.ChunkRadius;
                     chunkY++)
                {
                    for (int chunkX = -auditSettings.ChunkRadius;
                         chunkX <= auditSettings.ChunkRadius;
                         chunkX++)
                    {
                        ChunkCoordinate coordinate = new ChunkCoordinate(
                            WorldLayer.Surface,
                            chunkX,
                            chunkY);
                        if (!HydrologyDiagnosticChunk.TryGenerate(
                                settings,
                                biomeSettings,
                                coordinate,
                                out HydrologyDiagnosticChunk chunk,
                                out HydrologyDiagnosticGenerationStage stage))
                        {
                            throw new InvalidOperationException(
                                $"派生水文分布監査で完成チャンクを生成できません: SeedIndex={seedIndex}, Chunk=({chunkX}, {chunkY}), Stage={stage}");
                        }

                        using (chunk)
                        {
                            CountChunk(settings, chunk, ref counts, ref signature);
                        }
                    }
                }
            }

            return new DerivedHydrologyDistributionAuditResult(
                auditSettings.SeedTexts.Count,
                auditSettings.ChunkRadius,
                counts,
                signature);
        }

        /// <summary>完成1チャンクの水文セルとタイルを件数・極値・比較署名へ加算します。</summary>
        /// <param name="settings">飽和・冠水を分類する固定閾値を含む生成設定です。</param>
        /// <param name="chunk">C4-Dまで完成し、この呼出し中は有効な診断チャンクです。</param>
        /// <param name="counts">更新する監査累計です。</param>
        /// <param name="signature">セル・タイル値を走査順に混ぜる比較署名です。</param>
        private static void CountChunk(
            in WorldGenerationSettings settings,
            HydrologyDiagnosticChunk chunk,
            ref DerivedHydrologyDistributionCounts counts,
            ref ulong signature)
        {
            counts.TotalChunks++;
            for (int cellIndex = 0; cellIndex < chunk.HydrologyCells.Length; cellIndex++)
            {
                GeneratedHydrologyCell cell = chunk.HydrologyCells[cellIndex];
                counts.TotalHydrologyCells++;
                if ((cell.ConnectionFlags & HydrologyConnectionFlags.Groundwater) != 0)
                {
                    counts.GroundwaterConnectedCellCount++;
                }

                counts.MaximumGroundwaterPotential = Math.Max(
                    counts.MaximumGroundwaterPotential,
                    cell.GroundwaterPotential);
                CountLakeClassification(cell.LakeClassification, ref counts);

                // 座標、値、bit、最終湖区分を含め、同じ件数でも空間配置が変われば署名が変わるようにします。
                signature = WorldSeedUtility.Mix64(signature ^ unchecked((uint)cell.CellX));
                signature = WorldSeedUtility.Mix64(signature ^ unchecked((uint)cell.CellY));
                signature = WorldSeedUtility.Mix64(signature ^ cell.GroundwaterPotential);
                signature = WorldSeedUtility.Mix64(signature ^ (uint)cell.ConnectionFlags);
                signature = WorldSeedUtility.Mix64(signature ^ (uint)cell.LakeClassification);
            }

            for (int tileIndex = 0; tileIndex < chunk.HydrologyTiles.Length; tileIndex++)
            {
                GeneratedSurfaceWaterTile surfaceWater = chunk.SurfaceWaterTiles[tileIndex];
                GeneratedHydrologyTile hydrology = chunk.HydrologyTiles[tileIndex];
                counts.TotalTiles++;
                if (surfaceWater.Type == SurfaceWaterTileType.SpringStream) counts.SpringStreamTileCount++;
                if (hydrology.NaturalSoilSaturationPotential >= settings.Groundwater.NaturalSoilSaturationThreshold) counts.SaturatedTileCount++;
                if (hydrology.NaturalFloodingPotential >= settings.NaturalFlooding.NaturalFloodingThreshold) counts.FloodedTileCount++;
                if ((hydrology.Flags & GeneratedHydrologyTileFlags.WetlandTileEligible) != 0) counts.WetlandEligibleTileCount++;
                if ((hydrology.Flags & GeneratedHydrologyTileFlags.LargeWetlandExtent) != 0) counts.LargeWetlandExtentTileCount++;

                counts.MaximumSoilSaturationPotential = Math.Max(
                    counts.MaximumSoilSaturationPotential,
                    hydrology.NaturalSoilSaturationPotential);
                counts.MaximumNaturalFloodingPotential = Math.Max(
                    counts.MaximumNaturalFloodingPotential,
                    hydrology.NaturalFloodingPotential);

                // 製品状態ハッシュと同じ関数を再利用し、表流水と自然水文の全フィールドを漏れなく比較します。
                signature = DeterministicSurfaceWaterGenerator.HashTile(signature, surfaceWater);
                signature = DeterministicGroundwaterHydrologyGenerator.HashTile(signature, hydrology);
            }
        }

        /// <summary>最終湖区分を4分類の対応する累計へ加算します。</summary>
        /// <param name="classification">C4-Cで確定した湖水収支区分です。</param>
        /// <param name="counts">更新する監査累計です。Noneはどの湖件数にも加えません。</param>
        private static void CountLakeClassification(
            LakeWaterBalanceClassification classification,
            ref DerivedHydrologyDistributionCounts counts)
        {
            switch (classification)
            {
                case LakeWaterBalanceClassification.None:
                    return;
                case LakeWaterBalanceClassification.Temporary:
                    counts.TemporaryLakeCellCount++;
                    return;
                case LakeWaterBalanceClassification.PermanentSaltwater:
                    counts.PermanentSaltwaterLakeCellCount++;
                    return;
                case LakeWaterBalanceClassification.ClosedFreshwater:
                    counts.ClosedFreshwaterLakeCellCount++;
                    return;
                case LakeWaterBalanceClassification.OverflowFreshwater:
                    counts.OverflowFreshwaterLakeCellCount++;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(classification));
            }
        }
    }
}
