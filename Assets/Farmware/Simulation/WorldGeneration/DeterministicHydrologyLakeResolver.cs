// このファイルは、閉鎖流域の有限な上流集合から安定した越流点と基礎湖面を求め、D8下流の単調性から連続湖面を判定します。
// DeterministicHydrologyGeneratorのD8流向と集水規則だけを入力とし、ロード済みチャンクや現在水位へ依存しません。

using Unity.Collections;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 閉鎖流域内の座標を固定容量Hash表から引くための1スロットです。
    /// Chunk生成Jobが1流域ずつ再利用し、流域境界判定で内外を区別します。
    /// </summary>
    internal struct HydrologyBasinLookupSlot
    {
        /// <summary>登録した水文セルの水平絶対座標です。</summary>
        public int CellX;

        /// <summary>登録した水文セルの垂直絶対座標です。</summary>
        public int CellY;

        /// <summary>選択セル配列内の0始まり添字です。</summary>
        public int MemberIndex;

        /// <summary>空き、または流域セルのどちらかを表す内部状態です。</summary>
        public HydrologyBasinLookupState State;
    }

    /// <summary>固定容量Hash表の占有状態を表します。</summary>
    internal enum HydrologyBasinLookupState : byte
    {
        /// <summary>このHashスロットには座標が登録されていません。</summary>
        Empty = 0,

        /// <summary>閉鎖流域の上流集合に含まれるセルです。</summary>
        BasinMember = 1
    }

    /// <summary>
    /// 一つの閉鎖末端について確定した流域面積、越流辺、基礎湖面を保持します。
    /// 同じ末端へ流れる複数セルで再利用し、セルごとに流域全体を再探索しないための値です。
    /// </summary>
    public struct HydrologyLakeBasinResult
    {
        /// <summary>流向なしとなる陸上末端の水平水文セル座標です。</summary>
        public int SinkCellX;

        /// <summary>流向なしとなる陸上末端の垂直水文セル座標です。</summary>
        public int SinkCellY;

        /// <summary>正規化済み上流集合に含まれる流域セル数です。</summary>
        public uint BasinAreaCells;

        /// <summary>末端セルの代表標高で、湖面計算の下端です。</summary>
        public ushort LakeBottomHeight;

        /// <summary>流域境界で選んだ最小のmax(内側標高, 外側標高)です。</summary>
        public ushort SpillHeight;

        /// <summary>RunoffEquivalentCellsへ2,048を掛け、65,535へ飽和した充填率です。</summary>
        public ushort LakeFillFraction;

        /// <summary>湖底から越流標高までを充填率で補間した固定湖面標高です。</summary>
        public ushort LakeSurfaceHeight;

        /// <summary>選択した越流辺の流域内側セルXです。</summary>
        public int SpillInsideCellX;

        /// <summary>選択した越流辺の流域内側セルYです。</summary>
        public int SpillInsideCellY;

        /// <summary>選択した越流辺の流域外側セルXです。</summary>
        public int SpillOutsideCellX;

        /// <summary>選択した越流辺の流域外側セルYです。</summary>
        public int SpillOutsideCellY;

        /// <summary>流域解決、除外理由、固定湖、越流の共通状態です。SurfaceCellは含めません。</summary>
        public HydrologyLakeFlags LakeFlags;

        /// <summary>正の水深を持つ固定湖候補を生成できたかを返します。</summary>
        public bool HasFixedLake => (LakeFlags & HydrologyLakeFlags.FixedLake) != 0;
    }

    /// <summary>
    /// 呼出し側が直前に解決した閉鎖流域を保持する小型キャッシュです。
    /// Hash表と選択セル配列の所有権は呼出元に残し、同じ作業領域を使う連続呼出しだけで再利用します。
    /// </summary>
    internal struct HydrologyLakeBasinCache
    {
        /// <summary>Resultと呼出元ワーク領域が同じ流域を表す場合に1です。</summary>
        public byte IsValid;

        /// <summary>同じ末端へ流れる次セルかを比較する共通流域結果です。</summary>
        public HydrologyLakeBasinResult Result;
    }

    /// <summary>
    /// D8閉鎖流域を最大4,096セルで列挙し、安定した越流辺と連続する基礎湖面を求める純粋計算です。
    /// Burst Jobから呼べるよう、管理参照、再帰、可変長コンテナ、ロード済みEntityを使用しません。
    /// </summary>
    public static class DeterministicHydrologyLakeResolver
    {
        /// <summary>選択セルを衝突率50%以下で保持する、2の累乗の固定Hash表容量です。</summary>
        public const int LookupCapacity = 8192;

        /// <summary>閉鎖流域へ保存する正規化済み上流セル数の固定上限です。</summary>
        public const int MaximumBasinMembers = 4096;

        /// <summary>1セルから検査するD8隣接数です。</summary>
        private const int NeighborCount = 8;

        /// <summary>座標Hashから固定Hash表添字を得るためのビットマスクです。</summary>
        private const int LookupMask = LookupCapacity - 1;

        /// <summary>
        /// 単一セル用の一時ワーク領域を確保し、閉鎖流域・湖面を含む固定水文結果を返します。
        /// </summary>
        /// <param name="settings">Seed、GeneratorVersion、固定水文設定です。</param>
        /// <param name="cellX">対象水文セルの水平絶対座標です。</param>
        /// <param name="cellY">対象水文セルの垂直絶対座標です。</param>
        /// <param name="cell">成功時の下流、集水、湖面を含む固定結果です。</param>
        /// <returns>全探索を固定上限内で完了できた場合はtrueです。</returns>
        public static bool TryResolveCell(
            in WorldGenerationSettings settings,
            int cellX,
            int cellY,
            out GeneratedHydrologyCell cell)
        {
            using NativeArray<HydrologyUpstreamCandidate> frontier =
                new NativeArray<HydrologyUpstreamCandidate>(
                    DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            if (!DeterministicHydrologyGenerator.TryGenerateCellWithoutLake(
                    settings,
                    cellX,
                    cellY,
                    frontier,
                    0,
                    out cell))
            {
                // 集水まで完成していない部分値を呼出元が利用しないよう、失敗時はdefaultへ戻します。
                cell = default;
                return false;
            }

            if (cell.DownstreamTerminal != HydrologyConnectionFlags.ClosedBasin)
            {
                return true;
            }

            using NativeArray<HydrologyUpstreamCandidate> basinMembers =
                new NativeArray<HydrologyUpstreamCandidate>(
                    MaximumBasinMembers,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<HydrologyBasinLookupSlot> basinLookup =
                new NativeArray<HydrologyBasinLookupSlot>(
                    LookupCapacity,
                    Allocator.Temp,
                    NativeArrayOptions.ClearMemory);
            HydrologyLakeBasinCache cache = default;
            bool succeeded = TryApplyLakeState(
                    settings,
                    ref cell,
                    frontier,
                    0,
                    basinMembers,
                    0,
                    basinLookup,
                    0,
                    ref cache);
            if (!succeeded)
            {
                // 境界座標端などで湖面解決が完了しなければ、基礎水文だけの部分値も公開しません。
                cell = default;
            }

            return succeeded;
        }

        /// <summary>
        /// 生成済みの下流・集水結果へ、呼出元所有の固定ワーク領域を使って閉鎖流域・湖面状態を追加します。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="cell">入力は下流・集水結果、成功時は閉鎖流域・湖面を追加した同じセルです。</param>
        /// <param name="frontier">流域上流候補Heapを保持する呼出元所有配列です。</param>
        /// <param name="frontierStartIndex">この探索へ割り当てたHeap領域の先頭添字です。</param>
        /// <param name="basinMembers">選択した閉鎖流域セルを保持する呼出元所有配列です。</param>
        /// <param name="memberStartIndex">この探索へ割り当てた流域セル領域の先頭添字です。</param>
        /// <param name="basinLookup">座標が閉鎖流域に属するかを引く固定Hash表です。</param>
        /// <param name="lookupStartIndex">この探索へ割り当てたHash表領域の先頭添字です。</param>
        /// <param name="cache">同じ作業領域で直前に解決した閉鎖末端と流域結果です。</param>
        /// <returns>閉鎖流域以外、または湖面解決を固定上限内で完了できた場合はtrueです。</returns>
        internal static bool TryApplyLakeState(
            in WorldGenerationSettings settings,
            ref GeneratedHydrologyCell cell,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            int frontierStartIndex,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            int memberStartIndex,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            int lookupStartIndex,
            ref HydrologyLakeBasinCache cache)
        {
            if (cell.DownstreamTerminal != HydrologyConnectionFlags.ClosedBasin)
            {
                // 海洋排水と長距離排水は閉鎖流域へ推測しません。defaultの湖状態をそのまま維持します。
                return true;
            }

            if (!HasRequiredWorkspace(
                    settings.Hydrology,
                    frontier,
                    frontierStartIndex,
                    basinMembers,
                    memberStartIndex,
                    basinLookup,
                    lookupStartIndex))
            {
                return false;
            }

            int sinkCellX = cell.DownstreamReachCellX;
            int sinkCellY = cell.DownstreamReachCellY;
            if (cache.IsValid == 0
                || cache.Result.SinkCellX != sinkCellX
                || cache.Result.SinkCellY != sinkCellY)
            {
                if (!TryBuildBasin(
                        settings,
                        sinkCellX,
                        sinkCellY,
                        frontier,
                        frontierStartIndex,
                        basinMembers,
                        memberStartIndex,
                        basinLookup,
                        lookupStartIndex,
                        out HydrologyLakeBasinResult basinResult))
                {
                    cache = default;
                    return false;
                }

                cache.IsValid = 1;
                cache.Result = basinResult;
            }

            ApplyCommonResult(ref cell, cache.Result);
            if (!cache.Result.HasFixedLake)
            {
                return true;
            }

            // 流域内セルはD8下流をたどると必ず末端へ着き、各辺でHydrologyHeightが増加しません。
            // したがって湖面未満のセルから末端までの全経路も湖面未満となり、別のBFSなしで連結を保証できます。
            if (cell.HydrologyHeight < cache.Result.LakeSurfaceHeight)
            {
                cell.LakeFlags |= HydrologyLakeFlags.SurfaceCell;
                cell.ConnectionFlags |= HydrologyConnectionFlags.Lake;
            }

            return true;
        }

        /// <summary>一つの閉鎖末端について上流集合、境界、充填率、連続湖面を順に確定します。</summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="sinkCellX">流向なし陸上末端の水平水文座標です。</param>
        /// <param name="sinkCellY">流向なし陸上末端の垂直水文座標です。</param>
        /// <param name="frontier">上流候補Heapを保持する呼出元所有配列です。</param>
        /// <param name="frontierStartIndex">この探索へ割り当てたHeap領域の先頭添字です。</param>
        /// <param name="basinMembers">正規化済み上流集合を保持する呼出元所有配列です。</param>
        /// <param name="memberStartIndex">この探索へ割り当てた流域セル領域の先頭添字です。</param>
        /// <param name="basinLookup">座標が閉鎖流域に属するかを引く固定Hash表です。</param>
        /// <param name="lookupStartIndex">この探索へ割り当てたHash表領域の先頭添字です。</param>
        /// <param name="result">成功時の流域面積、越流辺、湖面、除外理由です。</param>
        /// <returns>設定と作業領域が有効で、必要な有限探索を完了できた場合はtrueです。</returns>
        internal static bool TryBuildBasin(
            in WorldGenerationSettings settings,
            int sinkCellX,
            int sinkCellY,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            int frontierStartIndex,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            int memberStartIndex,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            int lookupStartIndex,
            out HydrologyLakeBasinResult result)
        {
            result = default;
            if (!DeterministicHydrologyGenerator.TryGenerateFlowCell(
                    settings,
                    sinkCellX,
                    sinkCellY,
                    out GeneratedHydrologyCell sinkCell)
                || sinkCell.IsOceanOutlet
                || sinkCell.HasDownstream
                || !DeterministicHydrologyCatchmentResolver.TryResolveWithStartCellAndMembers(
                    settings,
                    sinkCell,
                    frontier,
                    frontierStartIndex,
                    basinMembers,
                    memberStartIndex,
                    out HydrologyCatchmentResult catchment))
            {
                return false;
            }

            result = new HydrologyLakeBasinResult
            {
                SinkCellX = sinkCellX,
                SinkCellY = sinkCellY,
                BasinAreaCells = catchment.CatchmentAreaCells,
                LakeBottomHeight = sinkCell.HydrologyHeight,
                LakeFillFraction = CalculateFillFraction(settings.Hydrology, catchment.RunoffEquivalentCells),
                LakeFlags = HydrologyLakeFlags.BasinResolved
            };

            if (catchment.CatchmentTruncated)
            {
                // 不完全な4,096件集合から境界を推測せず、ClosedBasinだけを維持します。
                result.LakeFlags = ClassifyBasin(
                    settings.Hydrology,
                    catchment.CatchmentAreaCells,
                    true,
                    catchment.RunoffEquivalentCells,
                    sinkCell.HydrologyHeight,
                    0);
                return true;
            }

            int memberCount = (int)catchment.CatchmentAreaCells;
            ClearLookup(basinLookup, lookupStartIndex);
            for (int memberIndex = 0; memberIndex < memberCount; memberIndex++)
            {
                HydrologyUpstreamCandidate member = basinMembers[memberStartIndex + memberIndex];
                if (!TryInsertMember(
                        basinLookup,
                        lookupStartIndex,
                        member.CellX,
                        member.CellY,
                        memberIndex))
                {
                    return false;
                }
            }

            if (!TryFindSpillPoint(
                    settings,
                    basinMembers,
                    memberStartIndex,
                    memberCount,
                    basinLookup,
                    lookupStartIndex,
                    out HydrologySpillCandidate spill))
            {
                return false;
            }

            result.SpillHeight = spill.SpillHeight;
            result.SpillInsideCellX = spill.InsideCellX;
            result.SpillInsideCellY = spill.InsideCellY;
            result.SpillOutsideCellX = spill.OutsideCellX;
            result.SpillOutsideCellY = spill.OutsideCellY;
            result.LakeFlags = ClassifyBasin(
                settings.Hydrology,
                catchment.CatchmentAreaCells,
                false,
                catchment.RunoffEquivalentCells,
                sinkCell.HydrologyHeight,
                spill.SpillHeight);
            if (!result.HasFixedLake)
            {
                return true;
            }

            long heightRange = spill.SpillHeight - sinkCell.HydrologyHeight;
            result.LakeSurfaceHeight = (ushort)(sinkCell.HydrologyHeight
                + ((heightRange * result.LakeFillFraction) / ushort.MaxValue));
            if (result.LakeSurfaceHeight <= sinkCell.HydrologyHeight)
            {
                // 整数切り捨てで実湖面が湖底と同値になった場合も、固定湖と越流を残しません。
                result.LakeFlags &= ~(HydrologyLakeFlags.FixedLake | HydrologyLakeFlags.Overflow);
                result.LakeFlags |= HydrologyLakeFlags.NoPositiveDepth;
                return true;
            }

            return true;
        }

        /// <summary>流域全境界から仕様の安定全順序で唯一の越流辺を選びます。</summary>
        /// <param name="settings">外側セルの代表標高とFlowKeyを生成する固定設定です。</param>
        /// <param name="basinMembers">正規化済み上流集合を保持する配列です。</param>
        /// <param name="memberStartIndex">対象流域セル領域の先頭添字です。</param>
        /// <param name="memberCount">対象流域へ含まれる有効セル数です。</param>
        /// <param name="basinLookup">隣接座標が流域内かを判定する固定Hash表です。</param>
        /// <param name="lookupStartIndex">対象Hash表領域の先頭添字です。</param>
        /// <param name="best">成功時に安定順で最小となった流域内外の境界辺です。</param>
        /// <returns>全境界を検査し、少なくとも一つの越流候補を確定できた場合はtrueです。</returns>
        private static bool TryFindSpillPoint(
            in WorldGenerationSettings settings,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            int memberStartIndex,
            int memberCount,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            int lookupStartIndex,
            out HydrologySpillCandidate best)
        {
            best = default;
            bool hasBest = false;
            for (int memberIndex = 0; memberIndex < memberCount; memberIndex++)
            {
                HydrologyUpstreamCandidate inside = basinMembers[memberStartIndex + memberIndex];
                for (int directionIndex = 1; directionIndex <= NeighborCount; directionIndex++)
                {
                    GetDirectionOffset((HydrologyFlowDirection)directionIndex, out int offsetX, out int offsetY);
                    if (!TryAdd(inside.CellX, offsetX, out int outsideX)
                        || !TryAdd(inside.CellY, offsetY, out int outsideY))
                    {
                        return false;
                    }

                    if (TryFindMember(
                        basinLookup,
                        lookupStartIndex,
                        outsideX,
                        outsideY,
                        out _,
                        out _))
                    {
                        continue;
                    }

                    if (!DeterministicHydrologyGenerator.TryGenerateCellBasis(
                        settings,
                        outsideX,
                        outsideY,
                        out GeneratedHydrologyCell outside))
                    {
                        return false;
                    }

                    ushort spillHeight = inside.HydrologyHeight >= outside.HydrologyHeight
                        ? inside.HydrologyHeight
                        : outside.HydrologyHeight;
                    HydrologySpillCandidate candidate = new HydrologySpillCandidate
                    {
                        SpillHeight = spillHeight,
                        OutsideHydrologyHeight = outside.HydrologyHeight,
                        OutsidePlateauRank = outside.PlateauRank,
                        InsideCellX = inside.CellX,
                        InsideCellY = inside.CellY,
                        OutsideCellX = outsideX,
                        OutsideCellY = outsideY
                    };
                    if (!hasBest || CompareSpillCandidates(candidate, best) < 0)
                    {
                        best = candidate;
                        hasBest = true;
                    }
                }
            }

            return hasBest;
        }

        /// <summary>共通流域結果を対象セルへコピーし、SurfaceCellだけを対象セルの標高判定へ残します。</summary>
        /// <param name="cell">閉鎖流域情報を追加する生成済み水文セルです。</param>
        /// <param name="result">同じ閉鎖末端を持つセル間で共有する流域結果です。</param>
        private static void ApplyCommonResult(
            ref GeneratedHydrologyCell cell,
            in HydrologyLakeBasinResult result)
        {
            cell.BasinAreaCells = result.BasinAreaCells;
            cell.SpillInsideCellX = result.SpillInsideCellX;
            cell.SpillInsideCellY = result.SpillInsideCellY;
            cell.SpillOutsideCellX = result.SpillOutsideCellX;
            cell.SpillOutsideCellY = result.SpillOutsideCellY;
            cell.LakeBottomHeight = result.LakeBottomHeight;
            cell.SpillHeight = result.SpillHeight;
            cell.LakeFillFraction = result.LakeFillFraction;
            cell.LakeSurfaceHeight = result.LakeSurfaceHeight;
            cell.LakeFlags = result.LakeFlags;
        }

        /// <summary>流出寄与を固定倍率で0〜65,535へ飽和し、浮動小数点を使わず充填率へ変換します。</summary>
        /// <param name="settings">充填率へ使う固定倍率を持つ水文設定です。</param>
        /// <param name="runoffEquivalentCells">末端へ流入する0〜4,096の流出相当セル数です。</param>
        /// <returns>0〜65,535へ飽和した固定湖面充填率です。</returns>
        internal static ushort CalculateFillFraction(
            in HydrologyGenerationSettings settings,
            ushort runoffEquivalentCells)
        {
            long scaled = (long)runoffEquivalentCells * settings.LakeFillFractionMultiplier;
            return scaled >= ushort.MaxValue ? ushort.MaxValue : (ushort)scaled;
        }

        /// <summary>
        /// 閉鎖流域の打切り、面積、流出寄与、正の水深を仕様順に評価し、固定湖または除外理由を返します。
        /// </summary>
        /// <param name="settings">流域4セル、流出8、越流32の固定境界です。</param>
        /// <param name="basinAreaCells">末端自身を含む正規化済み上流セル数です。</param>
        /// <param name="basinTruncated">4,096件上限で流域境界が不完全ならtrueです。</param>
        /// <param name="runoffEquivalentCells">末端へ寄与する正規化済み流出相当値です。</param>
        /// <param name="lakeBottomHeight">末端セルの代表標高です。</param>
        /// <param name="spillHeight">安定した越流辺の内外最大標高です。</param>
        /// <returns>BasinResolvedと、優先される除外理由またはFixedLake・Overflowです。</returns>
        internal static HydrologyLakeFlags ClassifyBasin(
            in HydrologyGenerationSettings settings,
            uint basinAreaCells,
            bool basinTruncated,
            ushort runoffEquivalentCells,
            ushort lakeBottomHeight,
            ushort spillHeight)
        {
            HydrologyLakeFlags flags = HydrologyLakeFlags.BasinResolved;
            if (basinTruncated)
            {
                return flags | HydrologyLakeFlags.BasinTruncated;
            }

            if (basinAreaCells < settings.MinimumClosedBasinAreaCells)
            {
                return flags | HydrologyLakeFlags.BasinTooSmall;
            }

            if (runoffEquivalentCells < settings.LakeRunoffEquivalentThreshold)
            {
                return flags | HydrologyLakeFlags.InsufficientRunoff;
            }

            if (spillHeight <= lakeBottomHeight)
            {
                return flags | HydrologyLakeFlags.NoPositiveDepth;
            }

            flags |= HydrologyLakeFlags.FixedLake;
            if (runoffEquivalentCells >= settings.LakeOverflowRunoffEquivalentThreshold)
            {
                flags |= HydrologyLakeFlags.Overflow;
            }

            return flags;
        }

        /// <summary>再利用する固定Hash表を空へ戻し、前に解決した流域座標が次の判定へ混ざらないようにします。</summary>
        /// <param name="basinLookup">閉鎖流域を1件ずつ保持する、呼出元所有の固定Hash表です。</param>
        /// <param name="lookupStartIndex">今回空にする8,192スロット領域の先頭添字です。</param>
        private static void ClearLookup(
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            int lookupStartIndex)
        {
            for (int index = 0; index < LookupCapacity; index++)
            {
                basinLookup[lookupStartIndex + index] = default;
            }
        }

        /// <summary>座標を固定容量Hash表へ登録し、同一座標または容量不足を明示的に失敗させます。</summary>
        /// <param name="basinLookup">座標と選択セル添字を保持する固定Hash表です。</param>
        /// <param name="lookupStartIndex">対象Hash表領域の先頭添字です。</param>
        /// <param name="cellX">登録する水平水文座標です。</param>
        /// <param name="cellY">登録する垂直水文座標です。</param>
        /// <param name="memberIndex">流域セル配列内の0始まり相対添字です。</param>
        /// <returns>空きスロットへ一意に登録できた場合はtrueです。</returns>
        internal static bool TryInsertMember(
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            int lookupStartIndex,
            int cellX,
            int cellY,
            int memberIndex)
        {
            int initialIndex = GetLookupIndex(cellX, cellY);
            for (int probe = 0; probe < LookupCapacity; probe++)
            {
                int relativeIndex = (initialIndex + probe) & LookupMask;
                int absoluteIndex = lookupStartIndex + relativeIndex;
                HydrologyBasinLookupSlot slot = basinLookup[absoluteIndex];
                if (slot.State == HydrologyBasinLookupState.Empty)
                {
                    basinLookup[absoluteIndex] = new HydrologyBasinLookupSlot
                    {
                        CellX = cellX,
                        CellY = cellY,
                        MemberIndex = memberIndex,
                        State = HydrologyBasinLookupState.BasinMember
                    };
                    return true;
                }

                if (slot.CellX == cellX && slot.CellY == cellY)
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>線形探索Hash表から絶対スロット添字と選択セル添字を返します。</summary>
        /// <param name="basinLookup">検索する固定Hash表です。</param>
        /// <param name="lookupStartIndex">対象Hash表領域の先頭添字です。</param>
        /// <param name="cellX">検索する水平水文座標です。</param>
        /// <param name="cellY">検索する垂直水文座標です。</param>
        /// <param name="slotIndex">成功時の配列全体に対する絶対スロット添字です。</param>
        /// <param name="memberIndex">成功時の流域セル配列内0始まり相対添字です。</param>
        /// <returns>指定座標が対象流域Hash表へ登録済みならtrueです。</returns>
        internal static bool TryFindMember(
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            int lookupStartIndex,
            int cellX,
            int cellY,
            out int slotIndex,
            out int memberIndex)
        {
            int initialIndex = GetLookupIndex(cellX, cellY);
            for (int probe = 0; probe < LookupCapacity; probe++)
            {
                int relativeIndex = (initialIndex + probe) & LookupMask;
                int absoluteIndex = lookupStartIndex + relativeIndex;
                HydrologyBasinLookupSlot slot = basinLookup[absoluteIndex];
                if (slot.State == HydrologyBasinLookupState.Empty)
                {
                    slotIndex = default;
                    memberIndex = default;
                    return false;
                }

                if (slot.CellX == cellX && slot.CellY == cellY)
                {
                    slotIndex = absoluteIndex;
                    memberIndex = slot.MemberIndex;
                    return true;
                }
            }

            slotIndex = default;
            memberIndex = default;
            return false;
        }

        /// <summary>符号付き座標を64bitへ詰め、既存の安定Mix関数からHash表添字を作ります。</summary>
        /// <param name="cellX">Hash化する水平水文座標です。</param>
        /// <param name="cellY">Hash化する垂直水文座標です。</param>
        /// <returns>0以上8,192未満の安定した初期スロット添字です。</returns>
        private static int GetLookupIndex(int cellX, int cellY)
        {
            ulong packed = (uint)cellX | ((ulong)(uint)cellY << 32);
            return (int)(WorldSeedUtility.Mix64(packed ^ 0xC3D1BA51A11CE001UL) & LookupMask);
        }

        /// <summary>越流標高、外側FlowKey、内側座標、外側座標の順で候補を比較します。</summary>
        /// <param name="first">左辺の越流候補です。</param>
        /// <param name="second">右辺の越流候補です。</param>
        /// <returns>左辺が先なら負、同一なら0、右辺が先なら正です。</returns>
        internal static int CompareSpillCandidates(
            in HydrologySpillCandidate first,
            in HydrologySpillCandidate second)
        {
            int comparison = first.SpillHeight.CompareTo(second.SpillHeight);
            if (comparison != 0) return comparison;
            comparison = first.OutsideHydrologyHeight.CompareTo(second.OutsideHydrologyHeight);
            if (comparison != 0) return comparison;
            comparison = first.OutsidePlateauRank.CompareTo(second.OutsidePlateauRank);
            if (comparison != 0) return comparison;
            comparison = first.InsideCellX.CompareTo(second.InsideCellX);
            if (comparison != 0) return comparison;
            comparison = first.InsideCellY.CompareTo(second.InsideCellY);
            if (comparison != 0) return comparison;
            comparison = first.OutsideCellX.CompareTo(second.OutsideCellX);
            if (comparison != 0) return comparison;
            return first.OutsideCellY.CompareTo(second.OutsideCellY);
        }

        /// <summary>呼出元が各固定領域を重ならず確保したか検証し、Job内の範囲外書込を防ぎます。</summary>
        /// <param name="settings">固定容量と一致することを検証する水文設定です。</param>
        /// <param name="frontier">上流候補Heap配列です。</param>
        /// <param name="frontierStartIndex">Heap領域の先頭添字です。</param>
        /// <param name="basinMembers">閉鎖流域セル配列です。</param>
        /// <param name="memberStartIndex">流域セル領域の先頭添字です。</param>
        /// <param name="basinLookup">流域座標Hash表です。</param>
        /// <param name="lookupStartIndex">Hash表領域の先頭添字です。</param>
        /// <returns>全配列が作成済みで、各先頭から必要容量を安全に利用できる場合はtrueです。</returns>
        private static bool HasRequiredWorkspace(
            in HydrologyGenerationSettings settings,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            int frontierStartIndex,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            int memberStartIndex,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            int lookupStartIndex)
        {
            return DeterministicHydrologyGenerator.ValidateSettings(settings)
                && frontier.IsCreated
                && frontierStartIndex >= 0
                && frontierStartIndex <= frontier.Length
                    - DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity
                && basinMembers.IsCreated
                && memberStartIndex >= 0
                && memberStartIndex <= basinMembers.Length - settings.MaximumCatchmentContributingCells
                && basinLookup.IsCreated
                && lookupStartIndex >= 0
                && lookupStartIndex <= basinLookup.Length - LookupCapacity;
        }

        /// <summary>水文セル座標へ-1〜+1を64bitで加え、対応範囲内だけ返します。</summary>
        /// <param name="coordinate">加算前の符号付き水文座標です。</param>
        /// <param name="offset">D8隣接を表す-1〜+1の差分です。</param>
        /// <param name="result">成功時の加算済み座標です。</param>
        /// <returns>加算結果がint範囲内ならtrueです。</returns>
        private static bool TryAdd(int coordinate, int offset, out int result)
        {
            long value = (long)coordinate + offset;
            if (value < int.MinValue || value > int.MaxValue)
            {
                result = default;
                return false;
            }

            result = (int)value;
            return true;
        }

        /// <summary>D8方向IDを隣接座標差へ変換し、流向生成と同じ順で全8方向を列挙します。</summary>
        /// <param name="direction">EastからSouthEastまでのD8方向IDです。</param>
        /// <param name="offsetX">方向に対応する水平差分-1〜+1です。</param>
        /// <param name="offsetY">方向に対応する垂直差分-1〜+1です。</param>
        private static void GetDirectionOffset(
            HydrologyFlowDirection direction,
            out int offsetX,
            out int offsetY)
        {
            switch (direction)
            {
                case HydrologyFlowDirection.East: offsetX = 1; offsetY = 0; return;
                case HydrologyFlowDirection.NorthEast: offsetX = 1; offsetY = 1; return;
                case HydrologyFlowDirection.North: offsetX = 0; offsetY = 1; return;
                case HydrologyFlowDirection.NorthWest: offsetX = -1; offsetY = 1; return;
                case HydrologyFlowDirection.West: offsetX = -1; offsetY = 0; return;
                case HydrologyFlowDirection.SouthWest: offsetX = -1; offsetY = -1; return;
                case HydrologyFlowDirection.South: offsetX = 0; offsetY = -1; return;
                default: offsetX = 1; offsetY = -1; return;
            }
        }

        /// <summary>越流辺の比較だけに必要な固定値をまとめる内部一時値です。</summary>
        internal struct HydrologySpillCandidate
        {
            /// <summary>内外の代表標高の大きい方です。</summary>
            public ushort SpillHeight;
            /// <summary>同点時に外側FlowKeyの先頭として比較する代表標高です。</summary>
            public ushort OutsideHydrologyHeight;
            /// <summary>同点時に外側FlowKeyの第2要素として比較する安定順位です。</summary>
            public ulong OutsidePlateauRank;
            /// <summary>流域内側セルの水平座標です。</summary>
            public int InsideCellX;
            /// <summary>流域内側セルの垂直座標です。</summary>
            public int InsideCellY;
            /// <summary>流域外側セルの水平座標です。</summary>
            public int OutsideCellX;
            /// <summary>流域外側セルの垂直座標です。</summary>
            public int OutsideCellY;
        }
    }
}
