// このファイルは、TERRAIN-01C3-C2の集水域を1〜64水文セル四方へまとめる空間Summaryを構築します。
// DeterministicHydrologyCatchmentResolverの基準探索を置き換えず、単一出口・外部入口・分解理由を検証してから再利用可能な固定値を返します。

using System;
using Unity.Collections;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 空間Summaryを一括利用できない理由をビット単位で保持します。
    /// 診断とテストが分解理由を区別できるようにし、無効なSummaryを暗黙に集水結果へ加えないために使用します。
    /// </summary>
    [Flags]
    public enum HydrologyAggregationSummaryFlags : byte
    {
        /// <summary>領域内の全セルが一つの出口へ流れ、一括集約できる状態です。</summary>
        None = 0,

        /// <summary>領域内に海洋流出口または陸上の流向なし終端があり、親Summaryとして一括利用できません。</summary>
        ContainsTerminal = 1 << 0,

        /// <summary>領域外へ出るD8辺が複数あり、単一出口として扱えません。</summary>
        MultipleExits = 1 << 1,

        /// <summary>領域を出た下流経路が同じ領域へ戻るため、内部だけの距離として閉じられません。</summary>
        DownstreamReentersRegion = 1 << 2,

        /// <summary>領域内のセルから単一出口までの経路または距離を完全に確定できません。</summary>
        IncompleteInternalPath = 1 << 3
    }

    /// <summary>
    /// Summary領域の外側から内部へ直接流れ込む1水文セルです。
    /// 上位の集水探索はこの入口を次の候補として保持し、Summary境界で上流探索を打ち切りません。
    /// </summary>
    public struct HydrologyCatchmentExternalEntrance
    {
        /// <summary>領域外にある入口セルの水平絶対水文座標です。</summary>
        public int CellX;

        /// <summary>領域外にある入口セルの垂直絶対水文座標です。</summary>
        public int CellY;

        /// <summary>入口が直接流れ込む領域内セルの水平絶対水文座標です。</summary>
        public int InternalCellX;

        /// <summary>入口が直接流れ込む領域内セルの垂直絶対水文座標です。</summary>
        public int InternalCellY;

        /// <summary>入口セルからSummary出口セルまでに下るD8辺数です。</summary>
        public ushort DistanceToOutlet;

        /// <summary>FlowKeyの先頭となる入口セルの固定9地点代表標高です。</summary>
        public ushort HydrologyHeight;

        /// <summary>入口セルを個別展開するときに流出合計へ一度だけ加える固定9地点基礎湿度です。</summary>
        public ushort HydrologyMoisture;

        /// <summary>FlowKeyの第2要素となる入口セルの安定順位です。</summary>
        public ulong PlateauRank;
    }

    /// <summary>
    /// 2のLevel乗四方の水文セルを、単一出口へ向かう固定集水単位としてまとめた値です。
    /// managed参照を持たず、キャッシュの構築順やチャンクのロード状態に依存しない再生成可能データです。
    /// </summary>
    public struct HydrologyCatchmentAggregationSummary
    {
        /// <summary>Summary領域の最小水平絶対水文座標です。</summary>
        public int MinimumCellX;

        /// <summary>Summary領域の最小垂直絶対水文座標です。</summary>
        public int MinimumCellY;

        /// <summary>領域外へ流れる唯一の内部セルの水平絶対水文座標です。</summary>
        public int OutletCellX;

        /// <summary>領域外へ流れる唯一の内部セルの垂直絶対水文座標です。</summary>
        public int OutletCellY;

        /// <summary>出口セルの直後にある領域外セルの水平絶対水文座標です。</summary>
        public int DownstreamCellX;

        /// <summary>出口セルの直後にある領域外セルの垂直絶対水文座標です。</summary>
        public int DownstreamCellY;

        /// <summary>領域に含まれる水文セル数です。Level 0〜6で1〜4,096になります。</summary>
        public uint CellCount;

        /// <summary>領域内全セルのHydrologyMoistureを符号付き64bitで合計した値です。</summary>
        public long RunoffMoistureSum;

        /// <summary>領域内で出口から最も遠いセルまでのD8辺数です。</summary>
        public ushort MaximumDistanceToOutlet;

        /// <summary>領域外から内部へ直接入るセル数です。実体は呼出元の入口配列先頭から格納されます。</summary>
        public ushort ExternalEntranceCount;

        /// <summary>0は1×1、6は64×64水文セルを表す空間階層です。</summary>
        public byte Level;

        /// <summary>単一出口として一括利用できない理由です。Noneだけが集約可能です。</summary>
        public HydrologyAggregationSummaryFlags Flags;

        /// <summary>
        /// 領域内の全セルを一つの固定集水単位として利用できるかを返します。
        /// </summary>
        public bool CanAggregate => Flags == HydrologyAggregationSummaryFlags.None;
    }

    /// <summary>
    /// 固定D8辺からLevel 0〜6の空間Summaryを構築する純粋計算です。
    /// 呼出元所有のNativeArrayだけを作業領域に使い、Hot Pathでmanaged allocationや共有mutable状態を作りません。
    /// </summary>
    public static class DeterministicHydrologyAggregationSummary
    {
        /// <summary>Level 6の64×64領域が持つ固定最大セル数です。</summary>
        public const int MaximumCellCapacity = 4096;

        /// <summary>64×64領域の外周1セル帯に存在し得る固定最大セル数です。</summary>
        public const int MaximumExternalEntranceCapacity = 260;

        /// <summary>未計算の内部距離を、正しい0辺距離と区別する番兵値です。</summary>
        private const ushort UnknownDistance = ushort.MaxValue;

        /// <summary>
        /// 指定セルを含むLevel整列領域を生成し、単一出口、内部距離、外部入口を固定します。
        /// </summary>
        /// <param name="settings">Seed、GeneratorVersion、固定水文設定を持つワールド設定です。</param>
        /// <param name="cellX">整列領域を選ぶ水平絶対水文セル座標です。</param>
        /// <param name="cellY">整列領域を選ぶ垂直絶対水文セル座標です。</param>
        /// <param name="level">0〜6の空間Summary Levelです。</param>
        /// <param name="cells">最大4,096件の領域セルを保持する呼出元所有配列です。</param>
        /// <param name="distances">各領域セルから出口までの距離を保持する呼出元所有配列です。</param>
        /// <param name="externalEntrances">最大260件の外部入口を行優先で受け取る呼出元所有配列です。</param>
        /// <param name="summary">成功時の集約値または明示的な分解理由です。</param>
        /// <returns>設定、座標、作業容量が有効で、必要な固定セルをすべて生成できた場合はtrueです。</returns>
        public static bool TryBuildAligned(
            in WorldGenerationSettings settings,
            int cellX,
            int cellY,
            int level,
            NativeArray<GeneratedHydrologyCell> cells,
            NativeArray<ushort> distances,
            NativeArray<HydrologyCatchmentExternalEntrance> externalEntrances,
            out HydrologyCatchmentAggregationSummary summary)
        {
            summary = default;
            if (!DeterministicHydrologyGenerator.ValidateSettings(settings.Hydrology)
                || level < 0
                || level > settings.Hydrology.MaximumAggregationSummaryLevel)
            {
                return false;
            }

            int sideLength = 1 << level;
            int cellCount = sideLength * sideLength;
            int maximumExternalCells = (4 * sideLength) + 4;
            if (!cells.IsCreated || cells.Length < cellCount
                || !distances.IsCreated || distances.Length < cellCount
                || !externalEntrances.IsCreated || externalEntrances.Length < maximumExternalCells
                || !TryGetAlignedOrigin(cellX, sideLength, out int minimumCellX)
                || !TryGetAlignedOrigin(cellY, sideLength, out int minimumCellY)
                || !TryGetMaximumCoordinate(minimumCellX, sideLength, out int maximumCellX)
                || !TryGetMaximumCoordinate(minimumCellY, sideLength, out int maximumCellY))
            {
                return false;
            }

            HydrologyAggregationSummaryFlags flags = HydrologyAggregationSummaryFlags.None;
            int outletIndex = -1;
            int outletCount = 0;
            long moistureSum = 0L;

            // 同じ固定入力から領域内セルを行優先で作ります。生成済みチャンクを読まないため、キャッシュ構築順が結果へ入りません。
            for (int localY = 0; localY < sideLength; localY++)
            {
                for (int localX = 0; localX < sideLength; localX++)
                {
                    int index = (localY * sideLength) + localX;
                    int currentCellX = minimumCellX + localX;
                    int currentCellY = minimumCellY + localY;
                    if (!DeterministicHydrologyGenerator.TryGenerateFlowCell(
                            settings,
                            currentCellX,
                            currentCellY,
                            out GeneratedHydrologyCell current)
                        || !DeterministicHydrologyGenerator.TrySampleHydrologyMoisture(
                            settings,
                            currentCellX,
                            currentCellY,
                            out ushort moisture))
                    {
                        return false;
                    }

                    current.HydrologyMoisture = moisture;
                    cells[index] = current;
                    distances[index] = UnknownDistance;
                    moistureSum += moisture;

                    if (!current.HasDownstream)
                    {
                        flags |= HydrologyAggregationSummaryFlags.ContainsTerminal;
                        continue;
                    }

                    if (Contains(
                        minimumCellX,
                        minimumCellY,
                        maximumCellX,
                        maximumCellY,
                        current.DownstreamCellX,
                        current.DownstreamCellY))
                    {
                        continue;
                    }

                    outletCount++;
                    if (outletCount == 1)
                    {
                        outletIndex = index;
                        summary.OutletCellX = current.CellX;
                        summary.OutletCellY = current.CellY;
                        summary.DownstreamCellX = current.DownstreamCellX;
                        summary.DownstreamCellY = current.DownstreamCellY;
                    }
                    else
                    {
                        flags |= HydrologyAggregationSummaryFlags.MultipleExits;
                    }
                }
            }

            summary.MinimumCellX = minimumCellX;
            summary.MinimumCellY = minimumCellY;
            summary.CellCount = (uint)cellCount;
            summary.RunoffMoistureSum = moistureSum;
            summary.Level = (byte)level;

            if (outletCount != 1)
            {
                flags |= outletCount == 0
                    ? HydrologyAggregationSummaryFlags.IncompleteInternalPath
                    : HydrologyAggregationSummaryFlags.MultipleExits;
            }

            if (outletCount == 1
                && TryClassifyDownstreamOutsideRegion(
                    settings,
                    summary.DownstreamCellX,
                    summary.DownstreamCellY,
                    minimumCellX,
                    minimumCellY,
                    maximumCellX,
                    maximumCellY,
                    out HydrologyAggregationSummaryFlags downstreamFlags))
            {
                flags |= downstreamFlags;
            }
            else if (outletCount == 1)
            {
                return false;
            }

            // 終端、複数出口、再進入を持つ親Summaryは一括利用しません。子Levelへ分解すれば同じD8辺を個別に評価できます。
            if (flags != HydrologyAggregationSummaryFlags.None)
            {
                summary.Flags = flags;
                return true;
            }

            distances[outletIndex] = 0;
            ushort maximumDistance = 0;
            for (int index = 0; index < cellCount; index++)
            {
                if (!TryResolveInternalDistance(
                    cells,
                    distances,
                    sideLength,
                    minimumCellX,
                    minimumCellY,
                    maximumCellX,
                    maximumCellY,
                    index,
                    out ushort distance))
                {
                    summary.Flags = HydrologyAggregationSummaryFlags.IncompleteInternalPath;
                    return true;
                }

                if (distance > maximumDistance)
                {
                    maximumDistance = distance;
                }
            }

            if (!TryCollectExternalEntrances(
                settings,
                distances,
                sideLength,
                minimumCellX,
                minimumCellY,
                maximumCellX,
                maximumCellY,
                externalEntrances,
                out ushort externalEntranceCount))
            {
                return false;
            }

            summary.MaximumDistanceToOutlet = maximumDistance;
            summary.ExternalEntranceCount = externalEntranceCount;
            summary.Flags = HydrologyAggregationSummaryFlags.None;
            return true;
        }

        /// <summary>
        /// Summary全体を現在の選択数へ加えても4,096件上限を越えないかを検証します。
        /// 上限をまたぐ場合はfalseを返し、呼出元が一段下のSummaryまたは個別セルへ分解できるようにします。
        /// </summary>
        /// <param name="summary">単一出口と内部距離を検証済みのSummaryです。</param>
        /// <param name="selectedCellCount">すでに確定した上流セル数です。</param>
        /// <param name="maximumCellCount">設定で固定した最大寄与セル数です。</param>
        /// <returns>Summaryが集約可能で、加算後も上限以下ならtrueです。</returns>
        public static bool CanAccumulateWithoutCrossingLimit(
            in HydrologyCatchmentAggregationSummary summary,
            uint selectedCellCount,
            int maximumCellCount)
        {
            return summary.CanAggregate
                && maximumCellCount > 0
                && selectedCellCount <= (uint)maximumCellCount
                && summary.CellCount <= (uint)maximumCellCount - selectedCellCount;
        }

        /// <summary>指定座標を含む2の累乗幅領域の最小座標を床方向除算で求めます。</summary>
        /// <param name="coordinate">整列対象の水平または垂直絶対水文座標です。</param>
        /// <param name="sideLength">Levelから導出した1〜64の領域幅です。</param>
        /// <param name="origin">成功時の床方向へ整列した最小座標です。</param>
        /// <returns>整列結果がint範囲内ならtrueです。</returns>
        internal static bool TryGetAlignedOrigin(int coordinate, int sideLength, out int origin)
        {
            long quotient = coordinate / sideLength;
            long remainder = coordinate % sideLength;
            if (remainder < 0L)
            {
                quotient--;
            }

            long value = quotient * sideLength;
            if (value < int.MinValue || value > int.MaxValue)
            {
                origin = default;
                return false;
            }

            origin = (int)value;
            return true;
        }

        /// <summary>領域最小座標へ幅-1を64bitで加え、int範囲内の最大座標を返します。</summary>
        /// <param name="minimum">領域の最小絶対水文座標です。</param>
        /// <param name="sideLength">Levelから導出した1〜64の領域幅です。</param>
        /// <param name="maximum">成功時の領域最大絶対水文座標です。</param>
        /// <returns>加算結果がint範囲内ならtrueです。</returns>
        private static bool TryGetMaximumCoordinate(int minimum, int sideLength, out int maximum)
        {
            long value = (long)minimum + sideLength - 1L;
            if (value < int.MinValue || value > int.MaxValue)
            {
                maximum = default;
                return false;
            }

            maximum = (int)value;
            return true;
        }

        /// <summary>指定セルが閉区間のSummary領域内にあるかを判定します。</summary>
        /// <param name="minimumCellX">領域の最小水平絶対水文座標です。</param>
        /// <param name="minimumCellY">領域の最小垂直絶対水文座標です。</param>
        /// <param name="maximumCellX">領域の最大水平絶対水文座標です。</param>
        /// <param name="maximumCellY">領域の最大垂直絶対水文座標です。</param>
        /// <param name="cellX">判定対象の水平絶対水文座標です。</param>
        /// <param name="cellY">判定対象の垂直絶対水文座標です。</param>
        /// <returns>対象セルが領域内ならtrueです。</returns>
        private static bool Contains(
            int minimumCellX,
            int minimumCellY,
            int maximumCellX,
            int maximumCellY,
            int cellX,
            int cellY)
        {
            return cellX >= minimumCellX && cellX <= maximumCellX
                && cellY >= minimumCellY && cellY <= maximumCellY;
        }

        /// <summary>
        /// Summary出口の下流を有限に追い、同じ領域への再進入または4,096辺打切りを分類します。
        /// 再進入や未確定の長距離経路を許すと内部セルを二重集計し得るため、親Summaryを分解する明示的な理由にします。
        /// </summary>
        /// <param name="settings">固定下流上限とD8生成設定を持つワールド設定です。</param>
        /// <param name="startCellX">出口直後の水平絶対水文座標です。</param>
        /// <param name="startCellY">出口直後の垂直絶対水文座標です。</param>
        /// <param name="minimumCellX">Summary領域の最小水平座標です。</param>
        /// <param name="minimumCellY">Summary領域の最小垂直座標です。</param>
        /// <param name="maximumCellX">Summary領域の最大水平座標です。</param>
        /// <param name="maximumCellY">Summary領域の最大垂直座標です。</param>
        /// <param name="flags">再進入、打切り、または問題なしを表す分解理由です。</param>
        /// <returns>経路上の全座標を固定上限まで生成できた場合はtrueです。</returns>
        private static bool TryClassifyDownstreamOutsideRegion(
            in WorldGenerationSettings settings,
            int startCellX,
            int startCellY,
            int minimumCellX,
            int minimumCellY,
            int maximumCellX,
            int maximumCellY,
            out HydrologyAggregationSummaryFlags flags)
        {
            flags = HydrologyAggregationSummaryFlags.None;
            int currentCellX = startCellX;
            int currentCellY = startCellY;
            for (int edgeIndex = 0; edgeIndex < settings.Hydrology.MaximumDownstreamEdges; edgeIndex++)
            {
                if (Contains(
                    minimumCellX,
                    minimumCellY,
                    maximumCellX,
                    maximumCellY,
                    currentCellX,
                    currentCellY))
                {
                    flags = HydrologyAggregationSummaryFlags.DownstreamReentersRegion;
                    return true;
                }

                if (!DeterministicHydrologyGenerator.TryGenerateFlowCell(
                    settings,
                    currentCellX,
                    currentCellY,
                    out GeneratedHydrologyCell current))
                {
                    return false;
                }

                if (!current.HasDownstream)
                {
                    return true;
                }

                currentCellX = current.DownstreamCellX;
                currentCellY = current.DownstreamCellY;
            }

            // 4,096辺より先は固定LongDrainageの外側であり、再進入しないと証明できないため不完全経路として分解します。
            flags = HydrologyAggregationSummaryFlags.IncompleteInternalPath;
            return true;
        }

        /// <summary>1内部セルから既知距離または出口までをたどり、経路上の距離を逆向きに埋めます。</summary>
        /// <param name="cells">行優先で生成済みの領域内D8セルです。</param>
        /// <param name="distances">未計算番兵値または出口までの確定距離を保持する作業配列です。</param>
        /// <param name="sideLength">Summary領域の1辺セル数です。</param>
        /// <param name="minimumCellX">Summary領域の最小水平座標です。</param>
        /// <param name="minimumCellY">Summary領域の最小垂直座標です。</param>
        /// <param name="maximumCellX">Summary領域の最大水平座標です。</param>
        /// <param name="maximumCellY">Summary領域の最大垂直座標です。</param>
        /// <param name="startIndex">距離を求める行優先セル添字です。</param>
        /// <param name="distance">成功時の開始セルから出口セルまでの辺数です。</param>
        /// <returns>領域外脱出、循環、距離上限超過なしに出口へ到達できた場合はtrueです。</returns>
        private static bool TryResolveInternalDistance(
            NativeArray<GeneratedHydrologyCell> cells,
            NativeArray<ushort> distances,
            int sideLength,
            int minimumCellX,
            int minimumCellY,
            int maximumCellX,
            int maximumCellY,
            int startIndex,
            out ushort distance)
        {
            distance = default;
            if (distances[startIndex] != UnknownDistance)
            {
                distance = distances[startIndex];
                return true;
            }

            int currentIndex = startIndex;
            int unresolvedCount = 0;
            int cellCount = sideLength * sideLength;
            while (distances[currentIndex] == UnknownDistance)
            {
                if (unresolvedCount >= cellCount)
                {
                    return false;
                }

                GeneratedHydrologyCell current = cells[currentIndex];
                if (!Contains(
                    minimumCellX,
                    minimumCellY,
                    maximumCellX,
                    maximumCellY,
                    current.DownstreamCellX,
                    current.DownstreamCellY))
                {
                    return false;
                }

                currentIndex = GetInternalIndex(
                    current.DownstreamCellX,
                    current.DownstreamCellY,
                    minimumCellX,
                    minimumCellY,
                    sideLength);
                unresolvedCount++;
            }

            int resolvedDistance = distances[currentIndex] + unresolvedCount;
            if (resolvedDistance >= UnknownDistance)
            {
                return false;
            }

            distance = (ushort)resolvedDistance;
            currentIndex = startIndex;
            int remainingDistance = resolvedDistance;
            while (distances[currentIndex] == UnknownDistance)
            {
                distances[currentIndex] = (ushort)remainingDistance;
                GeneratedHydrologyCell current = cells[currentIndex];
                currentIndex = GetInternalIndex(
                    current.DownstreamCellX,
                    current.DownstreamCellY,
                    minimumCellX,
                    minimumCellY,
                    sideLength);
                remainingDistance--;
            }

            return true;
        }

        /// <summary>領域外周の1セル帯を行優先で調べ、内部へ直接流れる入口と出口までの距離を保持します。</summary>
        /// <param name="settings">固定D8辺と基礎湿度を再生成するワールド設定です。</param>
        /// <param name="distances">領域内各セルから出口までの確定距離です。</param>
        /// <param name="sideLength">Summary領域の1辺セル数です。</param>
        /// <param name="minimumCellX">Summary領域の最小水平座標です。</param>
        /// <param name="minimumCellY">Summary領域の最小垂直座標です。</param>
        /// <param name="maximumCellX">Summary領域の最大水平座標です。</param>
        /// <param name="maximumCellY">Summary領域の最大垂直座標です。</param>
        /// <param name="externalEntrances">行優先で見つけた外部入口を受け取る配列です。</param>
        /// <param name="externalEntranceCount">成功時に配列先頭へ書いた入口数です。</param>
        /// <returns>外周座標、入口距離、湿度をすべて固定できた場合はtrueです。</returns>
        private static bool TryCollectExternalEntrances(
            in WorldGenerationSettings settings,
            NativeArray<ushort> distances,
            int sideLength,
            int minimumCellX,
            int minimumCellY,
            int maximumCellX,
            int maximumCellY,
            NativeArray<HydrologyCatchmentExternalEntrance> externalEntrances,
            out ushort externalEntranceCount)
        {
            externalEntranceCount = 0;
            long outerMinimumX = (long)minimumCellX - 1L;
            long outerMinimumY = (long)minimumCellY - 1L;
            long outerMaximumX = (long)maximumCellX + 1L;
            long outerMaximumY = (long)maximumCellY + 1L;
            if (outerMinimumX < int.MinValue || outerMinimumY < int.MinValue
                || outerMaximumX > int.MaxValue || outerMaximumY > int.MaxValue)
            {
                return false;
            }

            for (long candidateY = outerMinimumY; candidateY <= outerMaximumY; candidateY++)
            {
                for (long candidateX = outerMinimumX; candidateX <= outerMaximumX; candidateX++)
                {
                    if (Contains(
                        minimumCellX,
                        minimumCellY,
                        maximumCellX,
                        maximumCellY,
                        (int)candidateX,
                        (int)candidateY))
                    {
                        continue;
                    }

                    if (!DeterministicHydrologyGenerator.TryGenerateFlowCell(
                            settings,
                            (int)candidateX,
                            (int)candidateY,
                            out GeneratedHydrologyCell candidate))
                    {
                        return false;
                    }

                    if (!candidate.HasDownstream
                        || !Contains(
                            minimumCellX,
                            minimumCellY,
                            maximumCellX,
                            maximumCellY,
                            candidate.DownstreamCellX,
                            candidate.DownstreamCellY))
                    {
                        continue;
                    }

                    int internalIndex = GetInternalIndex(
                        candidate.DownstreamCellX,
                        candidate.DownstreamCellY,
                        minimumCellX,
                        minimumCellY,
                        sideLength);
                    ushort internalDistance = distances[internalIndex];
                    if (internalDistance == UnknownDistance
                        || internalDistance >= UnknownDistance - 1
                        || externalEntranceCount >= externalEntrances.Length
                        || !DeterministicHydrologyGenerator.TrySampleHydrologyMoisture(
                            settings,
                            candidate.CellX,
                            candidate.CellY,
                            out ushort moisture))
                    {
                        return false;
                    }

                    externalEntrances[externalEntranceCount] = new HydrologyCatchmentExternalEntrance
                    {
                        CellX = candidate.CellX,
                        CellY = candidate.CellY,
                        InternalCellX = candidate.DownstreamCellX,
                        InternalCellY = candidate.DownstreamCellY,
                        DistanceToOutlet = (ushort)(internalDistance + 1),
                        HydrologyHeight = candidate.HydrologyHeight,
                        HydrologyMoisture = moisture,
                        PlateauRank = candidate.PlateauRank
                    };
                    externalEntranceCount++;
                }
            }

            return true;
        }

        /// <summary>領域内の絶対座標を、行優先の作業配列添字へ変換します。</summary>
        /// <param name="cellX">領域内セルの水平絶対水文座標です。</param>
        /// <param name="cellY">領域内セルの垂直絶対水文座標です。</param>
        /// <param name="minimumCellX">Summary領域の最小水平座標です。</param>
        /// <param name="minimumCellY">Summary領域の最小垂直座標です。</param>
        /// <param name="sideLength">Summary領域の1辺セル数です。</param>
        /// <returns>呼出元が領域内を確認済みの座標に対応する0始まり添字です。</returns>
        private static int GetInternalIndex(
            int cellX,
            int cellY,
            int minimumCellX,
            int minimumCellY,
            int sideLength)
        {
            int localX = cellX - minimumCellX;
            int localY = cellY - minimumCellY;
            return (localY * sideLength) + localX;
        }
    }
}
