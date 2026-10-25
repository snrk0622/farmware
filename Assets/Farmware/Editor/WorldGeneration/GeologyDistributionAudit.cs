// このファイルは、複数シードの地質面積比、連結領域、境界、孤立領域、標高・火山活動度クロス集計を作ります。
// EditorメニューとEditModeテストから同じ純粋集計器を呼び、ゲームのECS状態や生成アセットは変更しません。

using System;
using System.Text;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// TERRAIN-01C2の地質分布監査結果を不変データとして保持します。
    /// 面積・直径・境界長は一定間隔の代表格子から求める概算値で、調整前後の比較に使用します。
    /// </summary>
    public sealed class GeologyDistributionAuditResult
    {
        /// <summary>GeologyClassの数値を添字にした代表サンプル件数です。</summary>
        private readonly int[] geologyCounts;

        /// <summary>GeologyClassごとの4近傍連結領域数です。</summary>
        private readonly int[] regionCounts;

        /// <summary>GeologyClassごとの1サンプルだけで終わった孤立領域数です。</summary>
        private readonly int[] isolatedRegionCounts;

        /// <summary>GeologyClassごとの最大連結領域に含まれる代表サンプル数です。</summary>
        private readonly int[] maximumRegionSampleCounts;

        /// <summary>GeologyClassごとの最大連結領域の外接矩形対角長をタイル平方で保持します。</summary>
        private readonly long[] maximumRegionDiameterSquaredInTiles;

        /// <summary>GeologyClassごとに異種地質との境界へ参加した代表格子辺数です。</summary>
        private readonly int[] boundaryEdgeCounts;

        /// <summary>GeologyClass×ElevationClassの代表サンプル件数です。</summary>
        private readonly int[] elevationCrossCounts;

        /// <summary>C5-Eで完成Biomeクロス監査へ置換する予約領域です。C5-Cでは全要素0です。</summary>
        private readonly int[] biomeCrossCounts;

        /// <summary>GeologyClass×火山活動度5段階の代表サンプル件数です。</summary>
        private readonly int[] volcanicBandCrossCounts;

        /// <summary>標高区分を配列で表す固定スロット数です。</summary>
        private readonly int elevationSlotCount;

        /// <summary>C5-Eの完成Biomeクロス監査でBiomeIdを配列へ表すための予約スロット数です。</summary>
        private readonly int biomeSlotCount;

        /// <summary>火山活動度5段階を配列で表す固定スロット数です。</summary>
        private readonly int volcanicBandSlotCount;

        /// <summary>概算面積と境界長への換算に使った代表サンプル間隔です。単位はタイルです。</summary>
        private readonly int sampleSpacingInTiles;

        /// <summary>監査へ使用したシード数です。</summary>
        public int SeedCount { get; }

        /// <summary>各シードで生成した正方形代表格子の1辺です。単位はサンプルです。</summary>
        public int GridWidth { get; }

        /// <summary>すべてのシードを合計した代表サンプル数です。</summary>
        public int TotalSamples { get; }

        /// <summary>東・北へ一度ずつ比較した代表格子辺の総数です。</summary>
        public int TotalNeighborEdges { get; }

        /// <summary>異なるGeologyClass同士だった代表格子辺の総数です。</summary>
        public int BoundaryEdgeCount { get; }

        /// <summary>代表格子境界数へサンプル間隔を掛けた概算境界長です。単位はタイルです。</summary>
        public long EstimatedBoundaryLengthInTiles => (long)BoundaryEdgeCount * sampleSpacingInTiles;

        /// <summary>走査順の地質・標高・火山活動度から作った調整前後比較用64bit署名です。</summary>
        public ulong Signature { get; }

        /// <summary>Unity Consoleへそのまま表示できる日本語の監査レポートです。</summary>
        public string Report { get; }

        /// <summary>Analyzerが完成済み配列を渡し、外部から変更できない監査結果を作ります。</summary>
        /// <param name="geologyCounts">地質分類別の代表サンプル件数です。</param>
        /// <param name="regionCounts">地質分類別の連結領域数です。</param>
        /// <param name="isolatedRegionCounts">地質分類別の1サンプル孤立領域数です。</param>
        /// <param name="maximumRegionSampleCounts">地質分類別の最大領域サンプル数です。</param>
        /// <param name="maximumRegionDiameterSquaredInTiles">地質分類別の最大領域外接矩形対角長平方です。</param>
        /// <param name="boundaryEdgeCounts">地質分類別の異種境界参加辺数です。</param>
        /// <param name="elevationCrossCounts">地質×標高区分件数です。</param>
        /// <param name="biomeCrossCounts">C5-Cでは全要素0とする、C5-E向け予約配列です。</param>
        /// <param name="volcanicBandCrossCounts">地質×火山活動度段階件数です。</param>
        /// <param name="seedCount">監査シード数です。</param>
        /// <param name="gridWidth">1シード分の代表格子幅です。</param>
        /// <param name="sampleSpacingInTiles">代表サンプル間隔です。</param>
        /// <param name="totalNeighborEdges">比較した代表格子辺総数です。</param>
        /// <param name="boundaryEdgeCount">異種地質境界辺総数です。</param>
        /// <param name="signature">比較用64bit署名です。</param>
        internal GeologyDistributionAuditResult(
            int[] geologyCounts,
            int[] regionCounts,
            int[] isolatedRegionCounts,
            int[] maximumRegionSampleCounts,
            long[] maximumRegionDiameterSquaredInTiles,
            int[] boundaryEdgeCounts,
            int[] elevationCrossCounts,
            int[] biomeCrossCounts,
            int[] volcanicBandCrossCounts,
            int seedCount,
            int gridWidth,
            int sampleSpacingInTiles,
            int totalNeighborEdges,
            int boundaryEdgeCount,
            ulong signature)
        {
            this.geologyCounts = geologyCounts;
            this.regionCounts = regionCounts;
            this.isolatedRegionCounts = isolatedRegionCounts;
            this.maximumRegionSampleCounts = maximumRegionSampleCounts;
            this.maximumRegionDiameterSquaredInTiles = maximumRegionDiameterSquaredInTiles;
            this.boundaryEdgeCounts = boundaryEdgeCounts;
            this.elevationCrossCounts = elevationCrossCounts;
            this.biomeCrossCounts = biomeCrossCounts;
            this.volcanicBandCrossCounts = volcanicBandCrossCounts;
            elevationSlotCount = (int)ElevationClass.Alpine + 1;
            biomeSlotCount = (int)BiomeId.VolcanicGeothermal + 1;
            volcanicBandSlotCount = (int)ContinuousEnvironmentBand.Level5 + 1;
            this.sampleSpacingInTiles = sampleSpacingInTiles;
            SeedCount = seedCount;
            GridWidth = gridWidth;
            TotalSamples = seedCount * gridWidth * gridWidth;
            TotalNeighborEdges = totalNeighborEdges;
            BoundaryEdgeCount = boundaryEdgeCount;
            Signature = signature;
            Report = BuildReport();
        }

        /// <summary>指定地質の代表サンプル件数を返します。</summary>
        /// <param name="geologyClass">件数を取得する地質分類です。</param>
        /// <returns>全監査シードの合計件数です。</returns>
        public int GetGeologyCount(GeologyClass geologyClass) => geologyCounts[(int)geologyClass];

        /// <summary>指定地質の4近傍連結領域数を返します。</summary>
        /// <param name="geologyClass">領域数を取得する地質分類です。</param>
        /// <returns>シード間を接続しない領域数の合計です。</returns>
        public int GetRegionCount(GeologyClass geologyClass) => regionCounts[(int)geologyClass];

        /// <summary>指定地質の1サンプル孤立領域数を返します。</summary>
        /// <param name="geologyClass">孤立数を取得する地質分類です。</param>
        /// <returns>上下左右に同分類を持たない領域数です。</returns>
        public int GetIsolatedRegionCount(GeologyClass geologyClass) =>
            isolatedRegionCounts[(int)geologyClass];

        /// <summary>指定地質の最大概算領域面積を返します。</summary>
        /// <param name="geologyClass">面積を取得する地質分類です。</param>
        /// <returns>最大領域サンプル数×サンプル間隔の二乗です。単位は平方タイルです。</returns>
        public long GetMaximumRegionAreaInTiles(GeologyClass geologyClass)
        {
            return (long)maximumRegionSampleCounts[(int)geologyClass]
                * sampleSpacingInTiles
                * sampleSpacingInTiles;
        }

        /// <summary>指定地質の最大概算領域直径を返します。</summary>
        /// <param name="geologyClass">直径を取得する地質分類です。</param>
        /// <returns>最大領域の外接矩形対角長です。単位はタイルです。</returns>
        public double GetMaximumRegionDiameterInTiles(GeologyClass geologyClass)
        {
            return Math.Sqrt(maximumRegionDiameterSquaredInTiles[(int)geologyClass]);
        }

        /// <summary>指定地質が参加した概算境界長を返します。</summary>
        /// <param name="geologyClass">境界長を取得する地質分類です。</param>
        /// <returns>異種境界参加辺数×サンプル間隔です。単位はタイルです。</returns>
        public long GetBoundaryLengthInTiles(GeologyClass geologyClass)
        {
            return (long)boundaryEdgeCounts[(int)geologyClass] * sampleSpacingInTiles;
        }

        /// <summary>指定地質と標高区分のクロス件数を返します。</summary>
        /// <param name="geologyClass">行に相当する地質分類です。</param>
        /// <param name="elevationClass">列に相当する標高区分です。</param>
        /// <returns>両条件を満たした代表サンプル件数です。</returns>
        public int GetElevationCrossCount(GeologyClass geologyClass, ElevationClass elevationClass)
        {
            return elevationCrossCounts[((int)geologyClass * elevationSlotCount) + (int)elevationClass];
        }

        /// <summary>C5-E向け予約配列から指定地質とBiomeのクロス件数を返します。C5-Cでは常に0です。</summary>
        /// <param name="geologyClass">行に相当する地質分類です。</param>
        /// <param name="biome">列に相当する論理Biomeです。</param>
        /// <returns>両条件を満たした代表サンプル件数です。</returns>
        public int GetBiomeCrossCount(GeologyClass geologyClass, BiomeId biome)
        {
            return biomeCrossCounts[((int)geologyClass * biomeSlotCount) + (int)biome];
        }

        /// <summary>指定地質と火山活動度5段階のクロス件数を返します。</summary>
        /// <param name="geologyClass">行に相当する地質分類です。</param>
        /// <param name="band">列に相当する火山活動度段階です。</param>
        /// <returns>両条件を満たした代表サンプル件数です。</returns>
        public int GetVolcanicBandCrossCount(
            GeologyClass geologyClass,
            ContinuousEnvironmentBand band)
        {
            return volcanicBandCrossCounts[
                ((int)geologyClass * volcanicBandSlotCount) + (int)band];
        }

        /// <summary>集計値を、調整前後で比較しやすい複数行レポートへ整形します。</summary>
        /// <returns>面積比、領域、境界、3種類のクロス集計を含む日本語文字列です。</returns>
        private string BuildReport()
        {
            StringBuilder builder = new StringBuilder(12288);
            builder.AppendLine("[Farmware] 地質分布監査");
            builder.Append("Seeds=").Append(SeedCount)
                .Append(", Grid=").Append(GridWidth).Append('x').Append(GridWidth)
                .Append(", Spacing=").Append(sampleSpacingInTiles)
                .Append(", Samples=").Append(TotalSamples)
                .Append(", BoundaryLength≈").Append(EstimatedBoundaryLengthInTiles)
                .Append(", Signature=0x").AppendLine(Signature.ToString("X16"));
            builder.AppendLine("※ 面積・直径・境界長は代表格子による比較用概算値です。合否閾値は設定していません。");

            for (GeologyClass geologyClass = GeologyClass.UnconsolidatedSediment;
                 geologyClass <= GeologyClass.UltramaficRock;
                 geologyClass++)
            {
                int geologyIndex = (int)geologyClass;
                int count = geologyCounts[geologyIndex];
                builder.Append(geologyClass).Append(": ")
                    .Append(count).Append(" (").Append(FormatPercent(count, TotalSamples)).Append(')')
                    .Append(", Regions=").Append(regionCounts[geologyIndex])
                    .Append(", Isolated=").Append(isolatedRegionCounts[geologyIndex])
                    .Append(", MaxArea≈").Append(GetMaximumRegionAreaInTiles(geologyClass))
                    .Append(", MaxDiameter≈")
                    .Append(GetMaximumRegionDiameterInTiles(geologyClass).ToString("F1"))
                    .Append(", Boundary≈").Append(GetBoundaryLengthInTiles(geologyClass))
                    .AppendLine(" tiles");
                AppendCrossRow(builder, "  Elevation", geologyClass, elevationSlotCount,
                    value => GetElevationCrossCount(geologyClass, (ElevationClass)value));
                // C5-C以降のBiomeはC4完成チャンクを必要とするため、基礎地質監査へNoneの偽クロス集計を表示しません。
                // 新18Biomeとのクロス監査は候補診断も扱うC5-Eへ集約します。
                AppendCrossRow(builder, "  VolcanicBand", geologyClass, volcanicBandSlotCount,
                    value => GetVolcanicBandCrossCount(geologyClass, (ContinuousEnvironmentBand)value));
            }

            return builder.ToString();
        }

        /// <summary>1つのクロス集計行を0件の予約値も含めてレポートへ追加します。</summary>
        /// <param name="builder">追記先の文字列Builderです。</param>
        /// <param name="label">行頭へ表示する軸名です。</param>
        /// <param name="geologyClass">読取対象の地質分類です。</param>
        /// <param name="slotCount">対象軸の予約値を含むスロット数です。</param>
        /// <param name="countSelector">軸Indexに対応する件数を返す関数です。</param>
        private static void AppendCrossRow(
            StringBuilder builder,
            string label,
            GeologyClass geologyClass,
            int slotCount,
            Func<int, int> countSelector)
        {
            builder.Append(label).Append('[').Append(geologyClass).Append("]:");
            for (int value = 0; value < slotCount; value++)
            {
                builder.Append(' ').Append(value).Append('=').Append(countSelector(value));
            }

            builder.AppendLine();
        }

        /// <summary>分母0を安全に扱い、割合を小数第2位までの文字列へ変換します。</summary>
        /// <param name="count">割合の分子です。</param>
        /// <param name="total">割合の分母です。</param>
        /// <returns>0.00%形式の文字列です。</returns>
        private static string FormatPercent(int count, int total)
        {
            return total > 0 ? ((count * 100d) / total).ToString("F2") + "%" : "0.00%";
        }
    }

    /// <summary>
    /// 実ゲームと同じ決定論的Generatorを代表格子で評価し、地質監査結果を作る純粋集計器です。
    /// UnityEngine.Objectへ触れないためEditorメニューからバックグラウンド実行できます。
    /// </summary>
    public static class GeologyDistributionAnalyzer
    {
        /// <summary>64bit比較署名の初期値として使うFNV-1a offset basisです。</summary>
        private const ulong SignatureOffsetBasis = 14695981039346656037UL;

        /// <summary>分類値を比較署名へ混ぜるFNV-1a primeです。</summary>
        private const ulong SignaturePrime = 1099511628211UL;

        /// <summary>NoneからUltramaficRockまでを格納できる地質配列スロット数です。</summary>
        private const int GeologySlotCount = (int)GeologyClass.UltramaficRock + 1;

        /// <summary>BelowSeaLevelからAlpineまでを格納できる標高配列スロット数です。</summary>
        private const int ElevationSlotCount = (int)ElevationClass.Alpine + 1;

        /// <summary>NoneからVolcanicまでを格納できるBiome配列スロット数です。</summary>
        private const int BiomeSlotCount = (int)BiomeId.VolcanicGeothermal + 1;

        /// <summary>予約値0とLevel1〜5を格納できる火山活動度段階スロット数です。</summary>
        private const int VolcanicBandSlotCount = (int)ContinuousEnvironmentBand.Level5 + 1;

        /// <summary>
        /// 指定シード・範囲・間隔で地質と環境軸を生成し、領域形状を含む監査結果を返します。
        /// 呼び出し中にScene、ECS World、アセットは変更しません。
        /// </summary>
        /// <param name="auditSettings">既存分布監査と共有する固定シード・代表格子設定です。</param>
        /// <returns>複数Seedの地質分布、領域形状、クロス集計、比較署名です。</returns>
        public static GeologyDistributionAuditResult Analyze(
            WorldGenerationDistributionAuditSettings auditSettings)
        {
            if (auditSettings == null)
            {
                throw new ArgumentNullException(nameof(auditSettings));
            }

            int gridWidth = checked((auditSettings.SampleRadiusInSteps * 2) + 1);
            int samplesPerSeed = checked(gridWidth * gridWidth);
            int[] geologyCounts = new int[GeologySlotCount];
            int[] regionCounts = new int[GeologySlotCount];
            int[] isolatedRegionCounts = new int[GeologySlotCount];
            int[] maximumRegionSampleCounts = new int[GeologySlotCount];
            long[] maximumRegionDiameterSquared = new long[GeologySlotCount];
            int[] boundaryEdgeCounts = new int[GeologySlotCount];
            int[] elevationCrossCounts = new int[GeologySlotCount * ElevationSlotCount];
            int[] biomeCrossCounts = new int[GeologySlotCount * BiomeSlotCount];
            int[] volcanicCrossCounts = new int[GeologySlotCount * VolcanicBandSlotCount];
            int totalNeighborEdges = 0;
            int boundaryEdgeCount = 0;
            ulong signature = SignatureOffsetBasis;

            for (int seedIndex = 0; seedIndex < auditSettings.SeedTexts.Count; seedIndex++)
            {
                WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                    WorldSeedUtility.FromString(auditSettings.SeedTexts[seedIndex]),
                    auditSettings.GeneratorVersion);
                GeologyClass[] geologyGrid = new GeologyClass[samplesPerSeed];

                for (int gridY = 0; gridY < gridWidth; gridY++)
                {
                    int worldTileY = checked(
                        (gridY - auditSettings.SampleRadiusInSteps)
                        * auditSettings.SampleSpacingInTiles);
                    for (int gridX = 0; gridX < gridWidth; gridX++)
                    {
                        int worldTileX = checked(
                            (gridX - auditSettings.SampleRadiusInSteps)
                            * auditSettings.SampleSpacingInTiles);
                        if (!DeterministicChunkGenerator.TryGenerateTile(
                                settings,
                                worldTileX,
                                worldTileY,
                                out GeneratedTile tile))
                        {
                            throw new InvalidOperationException(
                                $"地質分布監査の固定サンプル範囲を確保できません: ({worldTileX}, {worldTileY})");
                        }

                        int gridIndex = (gridY * gridWidth) + gridX;
                        int geologyIndex = (int)tile.GeologyClass;
                        ContinuousEnvironmentBand volcanicBand =
                            DeterministicEnvironmentGenerator.ClassifyContinuousEnvironment(
                                tile.VolcanicActivityPotential);
                        geologyGrid[gridIndex] = tile.GeologyClass;
                        geologyCounts[geologyIndex]++;
                        elevationCrossCounts[
                            (geologyIndex * ElevationSlotCount) + (int)tile.ElevationClass]++;
                        volcanicCrossCounts[
                            (geologyIndex * VolcanicBandSlotCount) + (int)volcanicBand]++;

                        // 件数が同じでも空間配置や環境との対応が変われば検出できるよう、走査順に全軸を混ぜます。
                        MixSignature(ref signature, (byte)tile.GeologyClass);
                        MixSignature(ref signature, (byte)tile.ElevationClass);
                        MixSignature(ref signature, (byte)volcanicBand);
                    }
                }

                CountBoundaries(
                    geologyGrid,
                    gridWidth,
                    boundaryEdgeCounts,
                    ref totalNeighborEdges,
                    ref boundaryEdgeCount);
                CountRegions(
                    geologyGrid,
                    gridWidth,
                    auditSettings.SampleSpacingInTiles,
                    regionCounts,
                    isolatedRegionCounts,
                    maximumRegionSampleCounts,
                    maximumRegionDiameterSquared);
            }

            return new GeologyDistributionAuditResult(
                geologyCounts,
                regionCounts,
                isolatedRegionCounts,
                maximumRegionSampleCounts,
                maximumRegionDiameterSquared,
                boundaryEdgeCounts,
                elevationCrossCounts,
                biomeCrossCounts,
                volcanicCrossCounts,
                auditSettings.SeedTexts.Count,
                gridWidth,
                auditSettings.SampleSpacingInTiles,
                totalNeighborEdges,
                boundaryEdgeCount,
                signature);
        }

        /// <summary>代表格子の東・北だけを比較し、異種境界を重複なく集計します。</summary>
        /// <param name="geologyGrid">1シード分の地質代表格子です。</param>
        /// <param name="gridWidth">正方形格子の1辺です。</param>
        /// <param name="boundaryEdgeCounts">地質別の境界参加辺数へ加算します。</param>
        /// <param name="totalNeighborEdges">比較した全辺数へ加算します。</param>
        /// <param name="boundaryEdgeCount">異種地質境界辺数へ加算します。</param>
        private static void CountBoundaries(
            GeologyClass[] geologyGrid,
            int gridWidth,
            int[] boundaryEdgeCounts,
            ref int totalNeighborEdges,
            ref int boundaryEdgeCount)
        {
            for (int gridY = 0; gridY < gridWidth; gridY++)
            {
                for (int gridX = 0; gridX < gridWidth; gridX++)
                {
                    int index = (gridY * gridWidth) + gridX;
                    if (gridX + 1 < gridWidth)
                    {
                        CountBoundaryPair(
                            geologyGrid[index],
                            geologyGrid[index + 1],
                            boundaryEdgeCounts,
                            ref totalNeighborEdges,
                            ref boundaryEdgeCount);
                    }

                    if (gridY + 1 < gridWidth)
                    {
                        CountBoundaryPair(
                            geologyGrid[index],
                            geologyGrid[index + gridWidth],
                            boundaryEdgeCounts,
                            ref totalNeighborEdges,
                            ref boundaryEdgeCount);
                    }
                }
            }
        }

        /// <summary>1つの代表格子辺を数え、異種なら両側の地質へ境界参加を記録します。</summary>
        /// <param name="first">辺の片側の地質です。</param>
        /// <param name="second">辺の反対側の地質です。</param>
        /// <param name="boundaryEdgeCounts">地質別の境界参加辺数です。</param>
        /// <param name="totalNeighborEdges">比較した全辺数です。</param>
        /// <param name="boundaryEdgeCount">異種地質境界辺数です。</param>
        private static void CountBoundaryPair(
            GeologyClass first,
            GeologyClass second,
            int[] boundaryEdgeCounts,
            ref int totalNeighborEdges,
            ref int boundaryEdgeCount)
        {
            totalNeighborEdges++;
            if (first == second)
            {
                return;
            }

            boundaryEdgeCount++;
            boundaryEdgeCounts[(int)first]++;
            boundaryEdgeCounts[(int)second]++;
        }

        /// <summary>4近傍の塗りつぶしで、地質別の領域数・孤立数・最大面積・最大直径を集計します。</summary>
        /// <param name="geologyGrid">1シード分の地質代表格子です。</param>
        /// <param name="gridWidth">正方形格子の1辺です。</param>
        /// <param name="sampleSpacingInTiles">代表点同士のタイル間隔です。</param>
        /// <param name="regionCounts">地質別領域数へ加算します。</param>
        /// <param name="isolatedRegionCounts">地質別孤立領域数へ加算します。</param>
        /// <param name="maximumRegionSampleCounts">地質別最大領域サンプル数を更新します。</param>
        /// <param name="maximumRegionDiameterSquared">地質別最大外接矩形対角長平方を更新します。</param>
        private static void CountRegions(
            GeologyClass[] geologyGrid,
            int gridWidth,
            int sampleSpacingInTiles,
            int[] regionCounts,
            int[] isolatedRegionCounts,
            int[] maximumRegionSampleCounts,
            long[] maximumRegionDiameterSquared)
        {
            bool[] visited = new bool[geologyGrid.Length];
            int[] queue = new int[geologyGrid.Length];
            for (int startIndex = 0; startIndex < geologyGrid.Length; startIndex++)
            {
                if (visited[startIndex])
                {
                    continue;
                }

                GeologyClass geologyClass = geologyGrid[startIndex];
                int readIndex = 0;
                int writeIndex = 1;
                int componentSize = 0;
                int minimumX = startIndex % gridWidth;
                int maximumX = minimumX;
                int minimumY = startIndex / gridWidth;
                int maximumY = minimumY;
                queue[0] = startIndex;
                visited[startIndex] = true;

                // 1つの配列をキューとして再利用し、領域ごとのList確保を行わず同じ地質の4近傍だけを巡回します。
                while (readIndex < writeIndex)
                {
                    int currentIndex = queue[readIndex++];
                    int currentX = currentIndex % gridWidth;
                    int currentY = currentIndex / gridWidth;
                    componentSize++;
                    minimumX = Math.Min(minimumX, currentX);
                    maximumX = Math.Max(maximumX, currentX);
                    minimumY = Math.Min(minimumY, currentY);
                    maximumY = Math.Max(maximumY, currentY);
                    TryEnqueue(currentX - 1, currentY, geologyClass, geologyGrid, gridWidth, visited, queue, ref writeIndex);
                    TryEnqueue(currentX + 1, currentY, geologyClass, geologyGrid, gridWidth, visited, queue, ref writeIndex);
                    TryEnqueue(currentX, currentY - 1, geologyClass, geologyGrid, gridWidth, visited, queue, ref writeIndex);
                    TryEnqueue(currentX, currentY + 1, geologyClass, geologyGrid, gridWidth, visited, queue, ref writeIndex);
                }

                int geologyIndex = (int)geologyClass;
                regionCounts[geologyIndex]++;
                if (componentSize == 1)
                {
                    isolatedRegionCounts[geologyIndex]++;
                }

                maximumRegionSampleCounts[geologyIndex] = Math.Max(
                    maximumRegionSampleCounts[geologyIndex],
                    componentSize);
                long widthInTiles = (long)(maximumX - minimumX) * sampleSpacingInTiles;
                long heightInTiles = (long)(maximumY - minimumY) * sampleSpacingInTiles;
                long diameterSquared = (widthInTiles * widthInTiles) + (heightInTiles * heightInTiles);
                maximumRegionDiameterSquared[geologyIndex] = Math.Max(
                    maximumRegionDiameterSquared[geologyIndex],
                    diameterSquared);
            }
        }

        /// <summary>格子内かつ同じ地質で未訪問の代表点だけを塗りつぶしキューへ追加します。</summary>
        /// <param name="gridX">追加候補の水平格子座標です。</param>
        /// <param name="gridY">追加候補の垂直格子座標です。</param>
        /// <param name="expectedGeology">現在塗りつぶしている地質分類です。</param>
        /// <param name="geologyGrid">1シード分の地質代表格子です。</param>
        /// <param name="gridWidth">正方形格子の1辺です。</param>
        /// <param name="visited">すでにキューへ追加した地点を示します。</param>
        /// <param name="queue">追加地点を保持する再利用キューです。</param>
        /// <param name="writeIndex">次のキュー書込位置です。</param>
        private static void TryEnqueue(
            int gridX,
            int gridY,
            GeologyClass expectedGeology,
            GeologyClass[] geologyGrid,
            int gridWidth,
            bool[] visited,
            int[] queue,
            ref int writeIndex)
        {
            if (gridX < 0 || gridX >= gridWidth || gridY < 0 || gridY >= gridWidth)
            {
                return;
            }

            int candidateIndex = (gridY * gridWidth) + gridX;
            if (visited[candidateIndex] || geologyGrid[candidateIndex] != expectedGeology)
            {
                return;
            }

            visited[candidateIndex] = true;
            queue[writeIndex++] = candidateIndex;
        }

        /// <summary>1byteの分類値を走査順の比較署名へ混ぜます。</summary>
        /// <param name="signature">更新対象の64bit署名です。</param>
        /// <param name="value">今回混ぜる地質・環境分類値です。</param>
        private static void MixSignature(ref ulong signature, byte value)
        {
            signature ^= value;
            signature *= SignaturePrime;
        }
    }
}
