// このファイルは、TERRAIN-01C3の複数Seedについて流向・終端・集水・河川・閉鎖流域・湖の分布を集計します。
// EditorメニューとEditModeテストが同じ純粋集計器を使い、Scene、ECS World、生成アセットを変更せず比較用レポートを作ります。

using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 水文分布監査へ渡す固定Seed、GeneratorVersion、正方形チャンク範囲です。
    /// 大量の有限集水探索を無制限に開始しないよう、範囲は明示的なチャンク半径で指定します。
    /// </summary>
    public sealed class HydrologyDistributionAuditSettings
    {
        /// <summary>順番も比較署名へ影響する、監査対象のSeed文字列一覧です。</summary>
        public IReadOnlyList<string> SeedTexts { get; }

        /// <summary>原点チャンクから正負へ含める半径です。0なら各Seedで原点1チャンクだけを調べます。</summary>
        public int ChunkRadius { get; }

        /// <summary>各Seedへ適用する固定生成版です。</summary>
        public int GeneratorVersion { get; }

        /// <summary>検証済みの複数Seed水文監査条件を作ります。</summary>
        /// <param name="seedTexts">1件以上のSeed文字列です。</param>
        /// <param name="chunkRadius">0以上32以下の正方形チャンク半径です。</param>
        /// <param name="generatorVersion">正の固定生成版です。</param>
        public HydrologyDistributionAuditSettings(
            IReadOnlyList<string> seedTexts,
            int chunkRadius,
            int generatorVersion)
        {
            if (seedTexts == null || seedTexts.Count == 0)
            {
                throw new ArgumentException("水文分布監査には1件以上のSeedが必要です。", nameof(seedTexts));
            }

            if (chunkRadius < 0 || chunkRadius > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkRadius));
            }

            if (generatorVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(generatorVersion));
            }

            string[] copiedSeeds = new string[seedTexts.Count];
            for (int index = 0; index < seedTexts.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(seedTexts[index]))
                {
                    throw new ArgumentException("Seed文字列を空にできません。", nameof(seedTexts));
                }

                copiedSeeds[index] = seedTexts[index];
            }

            SeedTexts = copiedSeeds;
            ChunkRadius = chunkRadius;
            GeneratorVersion = generatorVersion;
        }

        /// <summary>TERRAIN-01C3-D2の手動監査に使う3Seed・5×5チャンク条件を作ります。</summary>
        /// <returns>4,800水文セルを比較する既定条件です。</returns>
        public static HydrologyDistributionAuditSettings CreateDefault()
        {
            return new HydrologyDistributionAuditSettings(
                new[]
                {
                    "hydrology-audit-plain",
                    "hydrology-audit-river",
                    "hydrology-audit-lake"
                },
                2,
                WorldGenerationSettings.CurrentGeneratorVersion);
        }
    }

    /// <summary>
    /// 複数Seed水文監査の分類件数、極値、流域件数、比較署名を不変データとして保持します。
    /// 固定の合否閾値は持たず、Generator調整前後の分布変化と異常な0件を人間が確認するために使用します。
    /// </summary>
    public sealed class HydrologyDistributionAuditResult
    {
        /// <summary>NoneからSouthEastまでの流向値を添字にしたセル件数です。</summary>
        private readonly int[] flowDirectionCounts;

        /// <summary>Marine、ClosedBasin、LongDrainageの順に保持する終端セル件数です。</summary>
        private readonly int[] terminalCounts;

        /// <summary>監査へ使用したSeed数です。</summary>
        public int SeedCount { get; }

        /// <summary>各Seedで原点から正負へ調べたチャンク半径です。</summary>
        public int ChunkRadius { get; }

        /// <summary>全Seed・全チャンクで生成した水文セル総数です。</summary>
        public int TotalCells { get; }

        /// <summary>Stream接続を持つセル数です。Riverセルも含みます。</summary>
        public int StreamCellCount { get; }

        /// <summary>River接続を持つセル数です。</summary>
        public int RiverCellCount { get; }

        /// <summary>上流4,096件上限で集水を打ち切ったセル数です。</summary>
        public int CatchmentTruncatedCellCount { get; }

        /// <summary>不完全な境界のため固定湖面を作らなかった流域セル数です。</summary>
        public int BasinTruncatedCellCount { get; }

        /// <summary>固定湖候補へ属するセル数です。実際の湖面外セルも含みます。</summary>
        public int FixedLakeCellCount { get; }

        /// <summary>固定湖面へ含まれる水文セル数です。</summary>
        public int LakeSurfaceCellCount { get; }

        /// <summary>最大充填へ達し越流辺が有効なセル数です。</summary>
        public int OverflowCellCount { get; }

        /// <summary>終端座標で重複除去した、解決済み閉鎖流域数です。</summary>
        public int ResolvedBasinCount { get; }

        /// <summary>終端座標で重複除去した、打切り閉鎖流域数です。</summary>
        public int TruncatedBasinCount { get; }

        /// <summary>終端座標で重複除去した、固定湖候補を持つ流域数です。</summary>
        public int FixedLakeBasinCount { get; }

        /// <summary>監査範囲で観測した最大集水セル数です。</summary>
        public uint MaximumCatchmentAreaCells { get; }

        /// <summary>監査範囲で観測した最大流出寄与セル相当値です。</summary>
        public ushort MaximumRunoffEquivalentCells { get; }

        /// <summary>監査範囲で観測した最大閉鎖流域面積です。</summary>
        public uint MaximumBasinAreaCells { get; }

        /// <summary>走査順と全固定分類値から作る調整前後比較用64bit署名です。</summary>
        public ulong Signature { get; }

        /// <summary>Unity Consoleへそのまま出力できる日本語の監査レポートです。</summary>
        public string Report { get; }

        /// <summary>Analyzerが完成済み集計値を渡して不変結果を作ります。</summary>
        /// <param name="flowDirectionCounts">Noneを含む各D8流向のセル件数です。</param>
        /// <param name="terminalCounts">海、閉鎖流域、長距離排水の各終端件数です。</param>
        /// <param name="seedCount">監査したSeed数です。</param>
        /// <param name="chunkRadius">各Seedで原点から走査したチャンク半径です。</param>
        /// <param name="totalCells">全Seed・全チャンクの水文セル総数です。</param>
        /// <param name="streamCellCount">Streamフラグを持つセル数です。</param>
        /// <param name="riverCellCount">Riverフラグを持つセル数です。</param>
        /// <param name="catchmentTruncatedCellCount">集水域探索が上限へ達したセル数です。</param>
        /// <param name="basinTruncatedCellCount">閉鎖流域解析が上限へ達したセル数です。</param>
        /// <param name="fixedLakeCellCount">固定湖候補に属するセル数です。</param>
        /// <param name="lakeSurfaceCellCount">基礎湖面より低いセル数です。</param>
        /// <param name="overflowCellCount">最大充填時に越流するセル数です。</param>
        /// <param name="resolvedBasinCount">重複除去した解決済み閉鎖流域数です。</param>
        /// <param name="truncatedBasinCount">重複除去した打切り閉鎖流域数です。</param>
        /// <param name="fixedLakeBasinCount">重複除去した固定湖候補流域数です。</param>
        /// <param name="maximumCatchmentAreaCells">観測した最大集水セル数です。</param>
        /// <param name="maximumRunoffEquivalentCells">観測した最大流出寄与セル相当値です。</param>
        /// <param name="maximumBasinAreaCells">観測した最大閉鎖流域面積です。</param>
        /// <param name="signature">全分類値を走査順に混ぜた比較署名です。</param>
        internal HydrologyDistributionAuditResult(
            int[] flowDirectionCounts,
            int[] terminalCounts,
            int seedCount,
            int chunkRadius,
            int totalCells,
            int streamCellCount,
            int riverCellCount,
            int catchmentTruncatedCellCount,
            int basinTruncatedCellCount,
            int fixedLakeCellCount,
            int lakeSurfaceCellCount,
            int overflowCellCount,
            int resolvedBasinCount,
            int truncatedBasinCount,
            int fixedLakeBasinCount,
            uint maximumCatchmentAreaCells,
            ushort maximumRunoffEquivalentCells,
            uint maximumBasinAreaCells,
            ulong signature)
        {
            this.flowDirectionCounts = flowDirectionCounts;
            this.terminalCounts = terminalCounts;
            SeedCount = seedCount;
            ChunkRadius = chunkRadius;
            TotalCells = totalCells;
            StreamCellCount = streamCellCount;
            RiverCellCount = riverCellCount;
            CatchmentTruncatedCellCount = catchmentTruncatedCellCount;
            BasinTruncatedCellCount = basinTruncatedCellCount;
            FixedLakeCellCount = fixedLakeCellCount;
            LakeSurfaceCellCount = lakeSurfaceCellCount;
            OverflowCellCount = overflowCellCount;
            ResolvedBasinCount = resolvedBasinCount;
            TruncatedBasinCount = truncatedBasinCount;
            FixedLakeBasinCount = fixedLakeBasinCount;
            MaximumCatchmentAreaCells = maximumCatchmentAreaCells;
            MaximumRunoffEquivalentCells = maximumRunoffEquivalentCells;
            MaximumBasinAreaCells = maximumBasinAreaCells;
            Signature = signature;
            Report = BuildReport();
        }

        /// <summary>指定D8方向を持つ水文セル件数を返します。</summary>
        /// <param name="direction">Noneを含む固定流向です。</param>
        /// <returns>全監査Seedの合計セル数です。</returns>
        public int GetFlowDirectionCount(HydrologyFlowDirection direction)
        {
            return flowDirectionCounts[(int)direction];
        }

        /// <summary>指定した相互排他的な下流終端を持つ水文セル件数を返します。</summary>
        /// <param name="terminal">Marine、ClosedBasin、LongDrainageのいずれかです。</param>
        /// <returns>全監査Seedの合計セル数です。</returns>
        public int GetTerminalCount(HydrologyConnectionFlags terminal)
        {
            return terminalCounts[HydrologyDistributionAnalyzer.GetTerminalIndex(terminal)];
        }

        /// <summary>集計結果を分布比較用の複数行レポートへ整形します。</summary>
        /// <returns>流向、終端、集水、河川、湖、打切り件数を含む日本語文字列です。</returns>
        private string BuildReport()
        {
            StringBuilder builder = new StringBuilder(2048);
            builder.AppendLine("[Farmware] TERRAIN-01C3-D2 水文分布監査");
            builder.Append("Seeds=").Append(SeedCount)
                .Append(", ChunkRadius=").Append(ChunkRadius)
                .Append(", Cells=").Append(TotalCells)
                .Append(", Signature=0x").AppendLine(Signature.ToString("X16"));
            builder.AppendLine("※ 分布比較用の診断値であり、面積率や件数の合否閾値は設定していません。");

            builder.Append("Flow:");
            for (HydrologyFlowDirection direction = HydrologyFlowDirection.None;
                 direction <= HydrologyFlowDirection.SouthEast;
                 direction++)
            {
                builder.Append(' ').Append(direction).Append('=')
                    .Append(GetFlowDirectionCount(direction));
            }

            builder.AppendLine();
            builder.Append("Terminal: Marine=").Append(GetTerminalCount(HydrologyConnectionFlags.Marine))
                .Append(", ClosedBasin=").Append(GetTerminalCount(HydrologyConnectionFlags.ClosedBasin))
                .Append(", LongDrainage=").AppendLine(GetTerminalCount(HydrologyConnectionFlags.LongDrainage).ToString());
            builder.Append("Catchment: TruncatedCells=").Append(CatchmentTruncatedCellCount)
                .Append(", MaxArea=").Append(MaximumCatchmentAreaCells)
                .Append(", MaxRunoffEquivalent=").AppendLine(MaximumRunoffEquivalentCells.ToString());
            builder.Append("SurfaceWater: StreamCells=").Append(StreamCellCount)
                .Append(", RiverCells=").AppendLine(RiverCellCount.ToString());
            builder.Append("Basins: Resolved=").Append(ResolvedBasinCount)
                .Append(", Truncated=").Append(TruncatedBasinCount)
                .Append(", FixedLake=").Append(FixedLakeBasinCount)
                .Append(", MaxArea=").AppendLine(MaximumBasinAreaCells.ToString());
            builder.Append("LakeCells: Truncated=").Append(BasinTruncatedCellCount)
                .Append(", FixedLake=").Append(FixedLakeCellCount)
                .Append(", Surface=").Append(LakeSurfaceCellCount)
                .Append(", Overflow=").AppendLine(OverflowCellCount.ToString());
            return builder.ToString();
        }
    }

    /// <summary>
    /// 実ゲームと同じチャンク水文Generatorを複数Seedへ適用し、分布監査結果を作るEditor専用純粋集計器です。
    /// NativeArrayは呼出し中だけ所有して必ず破棄し、ECS BufferやSceneへ結果を書き込みません。
    /// </summary>
    public static class HydrologyDistributionAnalyzer
    {
        /// <summary>FNV-1a比較署名の初期値です。</summary>
        private const ulong SignatureOffsetBasis = 14695981039346656037UL;

        /// <summary>各固定値を比較署名へ順序付きで混ぜるFNV-1a primeです。</summary>
        private const ulong SignaturePrime = 1099511628211UL;

        /// <summary>NoneからSouthEastまでを格納できる流向配列長です。</summary>
        private const int FlowDirectionSlotCount = (int)HydrologyFlowDirection.SouthEast + 1;

        /// <summary>Marine・ClosedBasin・LongDrainageの3分類を格納する終端配列長です。</summary>
        private const int TerminalSlotCount = 3;

        /// <summary>指定した複数Seed・チャンク範囲を生成し、水文分類と流域を集計します。</summary>
        /// <param name="auditSettings">検証済みのSeed、範囲、GeneratorVersionです。</param>
        /// <returns>分布件数、極値、重複除去した流域数、比較署名です。</returns>
        public static HydrologyDistributionAuditResult Analyze(
            HydrologyDistributionAuditSettings auditSettings)
        {
            if (auditSettings == null)
            {
                throw new ArgumentNullException(nameof(auditSettings));
            }

            int[] flowDirectionCounts = new int[FlowDirectionSlotCount];
            int[] terminalCounts = new int[TerminalSlotCount];
            HashSet<BasinKey> resolvedBasins = new HashSet<BasinKey>();
            HashSet<BasinKey> truncatedBasins = new HashSet<BasinKey>();
            HashSet<BasinKey> fixedLakeBasins = new HashSet<BasinKey>();
            int totalCells = 0;
            int streamCellCount = 0;
            int riverCellCount = 0;
            int catchmentTruncatedCellCount = 0;
            int basinTruncatedCellCount = 0;
            int fixedLakeCellCount = 0;
            int lakeSurfaceCellCount = 0;
            int overflowCellCount = 0;
            uint maximumCatchmentAreaCells = 0;
            ushort maximumRunoffEquivalentCells = 0;
            uint maximumBasinAreaCells = 0;
            ulong signature = SignatureOffsetBasis;

            // AnalyzerはThreadPoolでも使い、4フレームを超える場合があるため、Temp系Allocatorの簡易APIは呼びません。
            // 全チャンクで同じPersistent作業領域を再利用し、このメソッドを抜ける際にusingで確実に破棄します。
            using NativeArray<GeneratedHydrologyCell> cells =
                new NativeArray<GeneratedHydrologyCell>(
                    DeterministicHydrologyGenerator.CellsPerChunk,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<HydrologyUpstreamCandidate> frontier =
                new NativeArray<HydrologyUpstreamCandidate>(
                    DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<HydrologyUpstreamCandidate> basinMembers =
                new NativeArray<HydrologyUpstreamCandidate>(
                    DeterministicHydrologyGenerator.ChunkBasinMemberCapacity,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<HydrologyBasinLookupSlot> basinLookup =
                new NativeArray<HydrologyBasinLookupSlot>(
                    DeterministicHydrologyGenerator.ChunkBasinLookupCapacity,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory);
            for (int seedIndex = 0; seedIndex < auditSettings.SeedTexts.Count; seedIndex++)
            {
                WorldGenerationSettings settings = WorldGenerationSettings.CreateDefault(
                    WorldSeedUtility.FromString(auditSettings.SeedTexts[seedIndex]),
                    auditSettings.GeneratorVersion);

                for (int chunkY = -auditSettings.ChunkRadius;
                     chunkY <= auditSettings.ChunkRadius;
                     chunkY++)
                {
                    for (int chunkX = -auditSettings.ChunkRadius;
                         chunkX <= auditSettings.ChunkRadius;
                         chunkX++)
                    {
                        if (!DeterministicHydrologyGenerator.TryGenerateChunkCells(
                                settings,
                                chunkX,
                                chunkY,
                                cells,
                                frontier,
                                basinMembers,
                                basinLookup))
                        {
                            throw new InvalidOperationException(
                                $"水文分布監査でチャンクを生成できません: SeedIndex={seedIndex}, Chunk=({chunkX}, {chunkY})");
                        }

                        for (int cellIndex = 0; cellIndex < cells.Length; cellIndex++)
                        {
                            GeneratedHydrologyCell cell = cells[cellIndex];
                            CountCell(
                                seedIndex,
                                cell,
                                flowDirectionCounts,
                                terminalCounts,
                                resolvedBasins,
                                truncatedBasins,
                                fixedLakeBasins,
                                ref totalCells,
                                ref streamCellCount,
                                ref riverCellCount,
                                ref catchmentTruncatedCellCount,
                                ref basinTruncatedCellCount,
                                ref fixedLakeCellCount,
                                ref lakeSurfaceCellCount,
                                ref overflowCellCount,
                                ref maximumCatchmentAreaCells,
                                ref maximumRunoffEquivalentCells,
                                ref maximumBasinAreaCells,
                                ref signature);
                        }
                    }
                }
            }

            return new HydrologyDistributionAuditResult(
                flowDirectionCounts,
                terminalCounts,
                auditSettings.SeedTexts.Count,
                auditSettings.ChunkRadius,
                totalCells,
                streamCellCount,
                riverCellCount,
                catchmentTruncatedCellCount,
                basinTruncatedCellCount,
                fixedLakeCellCount,
                lakeSurfaceCellCount,
                overflowCellCount,
                resolvedBasins.Count,
                truncatedBasins.Count,
                fixedLakeBasins.Count,
                maximumCatchmentAreaCells,
                maximumRunoffEquivalentCells,
                maximumBasinAreaCells,
                signature);
        }

        /// <summary>1水文セルを各分類件数、流域集合、極値、比較署名へ加算します。</summary>
        /// <param name="seedIndex">同一座標の流域をSeed間で混同しないためのSeed添字です。</param>
        /// <param name="cell">実ゲームGeneratorが生成した1水文セルです。</param>
        /// <param name="flowDirectionCounts">流向別件数へ加算する配列です。</param>
        /// <param name="terminalCounts">終端別件数へ加算する配列です。</param>
        /// <param name="resolvedBasins">解決済み流域を終端座標で重複除去する集合です。</param>
        /// <param name="truncatedBasins">打切り流域を終端座標で重複除去する集合です。</param>
        /// <param name="fixedLakeBasins">固定湖候補流域を終端座標で重複除去する集合です。</param>
        /// <param name="totalCells">走査した総セル数です。</param>
        /// <param name="streamCellCount">Streamセル累計です。</param>
        /// <param name="riverCellCount">Riverセル累計です。</param>
        /// <param name="catchmentTruncatedCellCount">集水域打切りセル累計です。</param>
        /// <param name="basinTruncatedCellCount">流域打切りセル累計です。</param>
        /// <param name="fixedLakeCellCount">固定湖候補セル累計です。</param>
        /// <param name="lakeSurfaceCellCount">湖面セル累計です。</param>
        /// <param name="overflowCellCount">越流セル累計です。</param>
        /// <param name="maximumCatchmentAreaCells">最大集水セル数です。</param>
        /// <param name="maximumRunoffEquivalentCells">最大流出寄与セル相当値です。</param>
        /// <param name="maximumBasinAreaCells">最大閉鎖流域面積です。</param>
        /// <param name="signature">分類値を順次混ぜる比較署名です。</param>
        private static void CountCell(
            int seedIndex,
            in GeneratedHydrologyCell cell,
            int[] flowDirectionCounts,
            int[] terminalCounts,
            HashSet<BasinKey> resolvedBasins,
            HashSet<BasinKey> truncatedBasins,
            HashSet<BasinKey> fixedLakeBasins,
            ref int totalCells,
            ref int streamCellCount,
            ref int riverCellCount,
            ref int catchmentTruncatedCellCount,
            ref int basinTruncatedCellCount,
            ref int fixedLakeCellCount,
            ref int lakeSurfaceCellCount,
            ref int overflowCellCount,
            ref uint maximumCatchmentAreaCells,
            ref ushort maximumRunoffEquivalentCells,
            ref uint maximumBasinAreaCells,
            ref ulong signature)
        {
            totalCells++;
            flowDirectionCounts[(int)cell.FlowDirection]++;
            terminalCounts[GetTerminalIndex(cell.DownstreamTerminal)]++;
            bool isStream = (cell.ConnectionFlags & HydrologyConnectionFlags.Stream) != 0;
            bool isRiver = (cell.ConnectionFlags & HydrologyConnectionFlags.River) != 0;
            bool isBasinTruncated = (cell.LakeFlags & HydrologyLakeFlags.BasinTruncated) != 0;
            bool isFixedLake = (cell.LakeFlags & HydrologyLakeFlags.FixedLake) != 0;
            bool isLakeSurface = (cell.LakeFlags & HydrologyLakeFlags.SurfaceCell) != 0;
            bool isOverflow = (cell.LakeFlags & HydrologyLakeFlags.Overflow) != 0;
            if (isStream) streamCellCount++;
            if (isRiver) riverCellCount++;
            if (cell.CatchmentTruncated) catchmentTruncatedCellCount++;
            if (isBasinTruncated) basinTruncatedCellCount++;
            if (isFixedLake) fixedLakeCellCount++;
            if (isLakeSurface) lakeSurfaceCellCount++;
            if (isOverflow) overflowCellCount++;

            maximumCatchmentAreaCells = Math.Max(maximumCatchmentAreaCells, cell.CatchmentAreaCells);
            if (cell.RunoffEquivalentCells > maximumRunoffEquivalentCells)
            {
                maximumRunoffEquivalentCells = cell.RunoffEquivalentCells;
            }
            maximumBasinAreaCells = Math.Max(maximumBasinAreaCells, cell.BasinAreaCells);

            if (cell.DownstreamTerminal == HydrologyConnectionFlags.ClosedBasin)
            {
                BasinKey basinKey = new BasinKey(
                    seedIndex,
                    cell.DownstreamReachCellX,
                    cell.DownstreamReachCellY);
                if ((cell.LakeFlags & HydrologyLakeFlags.BasinResolved) != 0)
                {
                    resolvedBasins.Add(basinKey);
                }

                if (isBasinTruncated)
                {
                    truncatedBasins.Add(basinKey);
                }

                if (isFixedLake)
                {
                    fixedLakeBasins.Add(basinKey);
                }
            }

            // 件数だけでなく空間配置と湖面値の変化も検出できるよう、走査順に主要固定値を混ぜます。
            MixSignature(ref signature, unchecked((uint)cell.CellX));
            MixSignature(ref signature, unchecked((uint)cell.CellY));
            MixSignature(ref signature, (uint)cell.FlowDirection);
            MixSignature(ref signature, (uint)cell.ConnectionFlags);
            MixSignature(ref signature, cell.CatchmentAreaCells);
            MixSignature(ref signature, cell.RunoffEquivalentCells);
            MixSignature(ref signature, cell.BasinAreaCells);
            MixSignature(ref signature, (uint)cell.LakeFlags);
            MixSignature(ref signature, cell.LakeSurfaceHeight);
        }

        /// <summary>相互排他的な下流終端を固定配列Indexへ変換します。</summary>
        /// <param name="terminal">Marine、ClosedBasin、LongDrainageのいずれかです。</param>
        /// <returns>0〜2の安定Indexです。</returns>
        internal static int GetTerminalIndex(HydrologyConnectionFlags terminal)
        {
            switch (terminal)
            {
                case HydrologyConnectionFlags.Marine: return 0;
                case HydrologyConnectionFlags.ClosedBasin: return 1;
                case HydrologyConnectionFlags.LongDrainage: return 2;
                default: throw new ArgumentOutOfRangeException(nameof(terminal));
            }
        }

        /// <summary>32bit固定値をFNV-1a比較署名へ1 byteずつ順序付きで混ぜます。</summary>
        /// <param name="signature">更新する現在の64bit署名です。</param>
        /// <param name="value">混ぜる符号なし32bit値です。</param>
        private static void MixSignature(ref ulong signature, uint value)
        {
            for (int byteIndex = 0; byteIndex < sizeof(uint); byteIndex++)
            {
                signature ^= (byte)(value >> (byteIndex * 8));
                signature *= SignaturePrime;
            }
        }

        /// <summary>Seedをまたいで同じ終端座標を同一流域と誤認しない、閉鎖流域の安定識別値です。</summary>
        private readonly struct BasinKey : IEquatable<BasinKey>
        {
            /// <summary>監査設定内のSeed順です。</summary>
            private readonly int seedIndex;

            /// <summary>流向なし閉鎖末端の水平水文セル座標です。</summary>
            private readonly int sinkCellX;

            /// <summary>流向なし閉鎖末端の垂直水文セル座標です。</summary>
            private readonly int sinkCellY;

            /// <summary>Seed順と閉鎖末端座標から流域識別値を作ります。</summary>
            /// <param name="seedIndex">監査設定内のSeed順です。</param>
            /// <param name="sinkCellX">閉鎖末端X座標です。</param>
            /// <param name="sinkCellY">閉鎖末端Y座標です。</param>
            public BasinKey(int seedIndex, int sinkCellX, int sinkCellY)
            {
                this.seedIndex = seedIndex;
                this.sinkCellX = sinkCellX;
                this.sinkCellY = sinkCellY;
            }

            /// <summary>Seed順と閉鎖末端X/Yがすべて同じ場合だけ同一流域と判定します。</summary>
            /// <param name="other">比較する流域識別値です。</param>
            /// <returns>3値が一致する場合はtrueです。</returns>
            public bool Equals(BasinKey other)
            {
                return seedIndex == other.seedIndex
                    && sinkCellX == other.sinkCellX
                    && sinkCellY == other.sinkCellY;
            }

            /// <summary>HashSetのobject比較でも型付き比較と同じ結果を返します。</summary>
            /// <param name="obj">比較する任意objectです。</param>
            /// <returns>BasinKeyで3値が一致する場合はtrueです。</returns>
            public override bool Equals(object obj)
            {
                return obj is BasinKey other && Equals(other);
            }

            /// <summary>Seed順と閉鎖末端座標をHashSet用の32bit値へ混ぜます。</summary>
            /// <returns>同じBasinKeyなら必ず同じHash値です。</returns>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = seedIndex;
                    hash = (hash * 397) ^ sinkCellX;
                    return (hash * 397) ^ sinkCellY;
                }
            }
        }
    }
}
