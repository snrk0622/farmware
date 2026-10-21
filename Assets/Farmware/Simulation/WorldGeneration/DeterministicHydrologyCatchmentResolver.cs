// このファイルは、TERRAIN-01C3-Cの正規化済み上流集合、集水面積、基礎湿度由来の自然流出を決定論的に求めます。
// DeterministicHydrologyGeneratorからD8一辺だけを読み、Chunk生成順・ロード範囲・現在の降雨へ依存しない固定水文結果を返します。

using Unity.Collections;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 上流探索の優先度付きフロンティアへ格納する1水文セルです。
    /// FlowDistance、FlowKey、座標の順で比較し、候補の発見順を集水結果へ持ち込みません。
    /// </summary>
    internal struct HydrologyUpstreamCandidate
    {
        /// <summary>候補水文セルの水平絶対座標です。</summary>
        public int CellX;

        /// <summary>候補水文セルの垂直絶対座標です。</summary>
        public int CellY;

        /// <summary>FlowKeyの第2要素となるSeed・版・座標・専用Salt由来の順位です。</summary>
        public ulong PlateauRank;

        /// <summary>対象セルまで確定D8辺を何本下るかを表す0〜4,095の距離です。</summary>
        public ushort FlowDistanceFromTarget;

        /// <summary>FlowKeyの先頭となる固定9地点の代表標高です。</summary>
        public ushort HydrologyHeight;

        /// <summary>流出寄与へ一度だけ加算する固定9地点の基礎湿度平均です。</summary>
        public ushort HydrologyMoisture;
    }

    /// <summary>
    /// 最大4,096セルの逆向きD8探索を完了した固定集水・自然流出結果です。
    /// GeneratedHydrologyCellへコピーし、後続の湖・地下水・診断処理から再利用します。
    /// </summary>
    public struct HydrologyCatchmentResult
    {
        /// <summary>対象セル自身を含む正規化済み上流集合のセル数です。</summary>
        public uint CatchmentAreaCells;

        /// <summary>同じ上流集合に含むHydrologyMoistureの符号付き64bit合計です。</summary>
        public long RunoffMoistureSum;

        /// <summary>RunoffMoistureSumを65,535で整数除算した流出寄与セル相当値です。</summary>
        public ushort RunoffEquivalentCells;

        /// <summary>対象セル自身の固定9地点基礎湿度平均です。</summary>
        public ushort TargetHydrologyMoisture;

        /// <summary>選択した上流セルのうち対象から最も遠いD8辺数です。</summary>
        public ushort MaximumContributingDistance;

        /// <summary>上流上限で打ち切ったかを表す固定補助状態です。</summary>
        public HydrologyCatchmentFlags CatchmentFlags;

        /// <summary>自然流出閾値から追加するStream・Riverフラグです。下流終端は含めません。</summary>
        public HydrologyConnectionFlags SurfaceWaterConnectionFlags;

        /// <summary>
        /// 上流集合が4,096件上限で打ち切られたかを返します。
        /// </summary>
        public bool CatchmentTruncated => (CatchmentFlags & HydrologyCatchmentFlags.Truncated) != 0;
    }

    /// <summary>
    /// D8辺を逆向きにたどり、最大4,096セルの集水面積と自然流出を同じ選択集合から求める純粋計算です。
    /// Burst Jobから呼べるよう、managed配列、delegate、再帰、例外、共有mutableキャッシュを使用しません。
    /// </summary>
    public static class DeterministicHydrologyCatchmentResolver
    {
        /// <summary>1セルごとに検査するD8逆辺の固定数です。</summary>
        private const int NeighborCount = 8;

        /// <summary>HydrologyMoistureを流出寄与セル相当値へ戻す固定除数です。</summary>
        private const long MaximumNormalizedMoisture = ushort.MaxValue;

        /// <summary>
        /// 4,096選択セルが各8逆辺から発見できる候補数の絶対上限です。
        /// 実際の格子では同じ候補を複数セルから発見しませんが、作業配列は判断回数上限まで確保します。
        /// </summary>
        public const int MaximumFrontierCapacity = 4096 * NeighborCount;

        /// <summary>
        /// 呼出元のNativeArrayが上流探索の最悪時候補数を保持できるか検証します。
        /// </summary>
        /// <param name="frontier">Chunk Jobまたは単一セルAPIが所有する再利用配列です。</param>
        /// <returns>配列が作成済みで固定容量以上ならtrueです。</returns>
        internal static bool HasRequiredFrontierCapacity(NativeArray<HydrologyUpstreamCandidate> frontier)
        {
            return frontier.IsCreated && frontier.Length >= MaximumFrontierCapacity;
        }

        /// <summary>
        /// 対象セルから最大4,096件の上流集合を求め、集水面積、湿度合計、流路区分を返します。
        /// </summary>
        /// <param name="settings">Seed、GeneratorVersion、気候、水文上限を持つ固定設定です。</param>
        /// <param name="targetCellX">集水先となる水平水文セル座標です。</param>
        /// <param name="targetCellY">集水先となる垂直水文セル座標です。</param>
        /// <param name="result">成功時の固定集水・自然流出結果です。</param>
        /// <returns>設定、対象座標、全逆辺を固定上限内で検証できた場合はtrueです。</returns>
        public static bool TryResolve(
            in WorldGenerationSettings settings,
            int targetCellX,
            int targetCellY,
            out HydrologyCatchmentResult result)
        {
            result = default;
            using NativeArray<HydrologyUpstreamCandidate> frontier =
                new NativeArray<HydrologyUpstreamCandidate>(
                    MaximumFrontierCapacity,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            return DeterministicHydrologyGenerator.TryGenerateFlowCell(
                    settings,
                    targetCellX,
                    targetCellY,
                    out GeneratedHydrologyCell startCell)
                && TryResolveWithStartCell(settings, startCell, frontier, out result);
        }

        /// <summary>
        /// 生成済みの対象セルと呼出元所有のHeap配列を使い、セルごとの再確保なしで集水結果を求めます。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="startCell">対象セルの代表値とD8一辺です。</param>
        /// <param name="frontier">上流候補を安定順で取り出すための再利用Heap配列です。</param>
        /// <param name="result">成功時の固定集水・自然流出結果です。</param>
        /// <returns>設定、作業容量、座標が有効で有限探索を完了できた場合はtrueです。</returns>
        internal static bool TryResolveWithStartCell(
            in WorldGenerationSettings settings,
            in GeneratedHydrologyCell startCell,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            out HydrologyCatchmentResult result)
        {
            return TryResolveWithStartCell(settings, startCell, frontier, 0, out result);
        }

        /// <summary>
        /// 呼出元配列の指定位置から始まる専用Heap領域を使い、並列行同士が作業値を上書きせず集水結果を求めます。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="startCell">対象セルの代表値とD8一辺です。</param>
        /// <param name="frontier">複数の独立Heap領域を連続配置できる、呼出元所有の作業配列です。</param>
        /// <param name="frontierStartIndex">この探索だけが読み書きする固定容量領域の先頭添字です。</param>
        /// <param name="result">成功時の固定集水・自然流出結果です。</param>
        /// <returns>設定、作業領域、座標が有効で有限探索を完了できた場合はtrueです。</returns>
        internal static bool TryResolveWithStartCell(
            in WorldGenerationSettings settings,
            in GeneratedHydrologyCell startCell,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            int frontierStartIndex,
            out HydrologyCatchmentResult result)
        {
            return TryResolveWithStartCellCore(
                settings,
                startCell,
                frontier,
                frontierStartIndex,
                default,
                0,
                false,
                out result);
        }

        /// <summary>
        /// 集水結果に加え、同じ安定順で選択した全上流セルを呼出元配列へ保存します。
        /// 閉鎖流域Resolverが境界と連続湖面を求めるときだけ使用し、通常の集水生成では追加配列へ書きません。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="startCell">流域末端となる代表値とD8一辺です。</param>
        /// <param name="frontier">優先度付き候補Heapとして再利用する呼出元配列です。</param>
        /// <param name="frontierStartIndex">この探索へ割り当てたHeap領域の先頭添字です。</param>
        /// <param name="selectedCells">選択した最大4,096件を順に保存する呼出元配列です。</param>
        /// <param name="selectedStartIndex">この探索へ割り当てた選択セル領域の先頭添字です。</param>
        /// <param name="result">成功時の集水面積、流出寄与、打切り状態です。</param>
        /// <returns>全逆辺を検証し、結果と選択セルを同時に確定できた場合はtrueです。</returns>
        internal static bool TryResolveWithStartCellAndMembers(
            in WorldGenerationSettings settings,
            in GeneratedHydrologyCell startCell,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            int frontierStartIndex,
            NativeArray<HydrologyUpstreamCandidate> selectedCells,
            int selectedStartIndex,
            out HydrologyCatchmentResult result)
        {
            return TryResolveWithStartCellCore(
                settings,
                startCell,
                frontier,
                frontierStartIndex,
                selectedCells,
                selectedStartIndex,
                true,
                out result);
        }

        /// <summary>
        /// 通常集水と閉鎖流域列挙で同じ優先度、上限、4,096件目の逆辺検査を共有する内部実装です。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="startCell">集水対象となる代表値とD8一辺です。</param>
        /// <param name="frontier">優先度付き候補Heapとして再利用する呼出元配列です。</param>
        /// <param name="frontierStartIndex">この探索へ割り当てたHeap領域の先頭添字です。</param>
        /// <param name="selectedCells">必要な場合だけ選択済み上流セルを保存する呼出元配列です。</param>
        /// <param name="selectedStartIndex">選択済みセル領域の先頭添字です。</param>
        /// <param name="collectSelectedCells">集水値に加えて全選択セルを保存する場合はtrueです。</param>
        /// <param name="result">成功時の集水面積、流出寄与、打切り状態です。</param>
        /// <returns>設定と作業領域が有効で、固定上限まで逆辺を検査できた場合はtrueです。</returns>
        private static bool TryResolveWithStartCellCore(
            in WorldGenerationSettings settings,
            in GeneratedHydrologyCell startCell,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            int frontierStartIndex,
            NativeArray<HydrologyUpstreamCandidate> selectedCells,
            int selectedStartIndex,
            bool collectSelectedCells,
            out HydrologyCatchmentResult result)
        {
            result = default;
            if (!DeterministicHydrologyGenerator.ValidateSettings(settings.Hydrology)
                || !frontier.IsCreated
                || frontierStartIndex < 0
                || frontierStartIndex > frontier.Length - MaximumFrontierCapacity
                || (collectSelectedCells
                    && (!selectedCells.IsCreated
                        || selectedStartIndex < 0
                        || selectedStartIndex > selectedCells.Length
                            - settings.Hydrology.MaximumCatchmentContributingCells))
                || !TryCreateCandidate(
                    settings,
                    startCell,
                    0,
                    out HydrologyUpstreamCandidate target))
            {
                return false;
            }

            int frontierLength = 0;
            if (!TryPush(frontier, frontierStartIndex, ref frontierLength, target))
            {
                return false;
            }

            uint selectedCount = 0;
            long moistureSum = 0L;
            ushort maximumDistance = 0;
            bool truncated = false;
            int maximumCells = settings.Hydrology.MaximumCatchmentContributingCells;

            while (frontierLength > 0 && selectedCount < maximumCells)
            {
                HydrologyUpstreamCandidate current = Pop(frontier, frontierStartIndex, ref frontierLength);
                if (collectSelectedCells)
                {
                    // selectedCountは追加前なので、0始まりの保存添字としてそのまま使用できます。
                    selectedCells[selectedStartIndex + (int)selectedCount] = current;
                }
                selectedCount++;
                moistureSum += current.HydrologyMoisture;
                if (current.FlowDistanceFromTarget > maximumDistance)
                {
                    maximumDistance = current.FlowDistanceFromTarget;
                }

                bool reachedSelectionLimit = selectedCount == maximumCells;
                bool foundUnselectedDirectUpstream = false;

                // 4,096件目でも8逆辺を必ず検査します。フロンティアが空の一本鎖でも4,097件目を見落とさず、
                // 不完全な閉鎖流域を後続の湖処理へ渡さないためです。
                for (int directionIndex = 1; directionIndex <= NeighborCount; directionIndex++)
                {
                    GetDirectionOffset((HydrologyFlowDirection)directionIndex, out int offsetX, out int offsetY);
                    if (!TryAdd(current.CellX, offsetX, out int neighborX)
                        || !TryAdd(current.CellY, offsetY, out int neighborY)
                        || !DeterministicHydrologyGenerator.TryGenerateFlowCell(
                            settings,
                            neighborX,
                            neighborY,
                            out GeneratedHydrologyCell neighbor))
                    {
                        return false;
                    }

                    if (!neighbor.HasDownstream
                        || neighbor.DownstreamCellX != current.CellX
                        || neighbor.DownstreamCellY != current.CellY)
                    {
                        continue;
                    }

                    if (reachedSelectionLimit)
                    {
                        foundUnselectedDirectUpstream = true;
                        continue;
                    }

                    // D8グラフでは各セルの下流が高々一つなので、同じ上流セルが別の選択済みセルから
                    // 二重発見されることはありません。visited集合を持たず、固定作業メモリで処理できます。
                    ushort upstreamDistance = (ushort)(current.FlowDistanceFromTarget + 1);
                    if (!TryCreateCandidate(
                            settings,
                            neighbor,
                            upstreamDistance,
                            out HydrologyUpstreamCandidate upstream)
                        || !TryPush(frontier, frontierStartIndex, ref frontierLength, upstream))
                    {
                        return false;
                    }
                }

                if (reachedSelectionLimit)
                {
                    truncated = frontierLength > 0 || foundUnselectedDirectUpstream;
                    break;
                }
            }

            if (selectedCount == 0 || selectedCount > maximumCells
                || moistureSum > (long)maximumCells * MaximumNormalizedMoisture)
            {
                return false;
            }

            ushort runoffEquivalentCells = (ushort)(moistureSum / MaximumNormalizedMoisture);
            result = new HydrologyCatchmentResult
            {
                CatchmentAreaCells = selectedCount,
                RunoffMoistureSum = moistureSum,
                RunoffEquivalentCells = runoffEquivalentCells,
                TargetHydrologyMoisture = target.HydrologyMoisture,
                MaximumContributingDistance = maximumDistance,
                CatchmentFlags = truncated
                    ? HydrologyCatchmentFlags.Truncated
                    : HydrologyCatchmentFlags.None,
                SurfaceWaterConnectionFlags = ClassifySurfaceWaterConnection(
                    settings.Hydrology,
                    runoffEquivalentCells)
            };
            return true;
        }

        /// <summary>
        /// 流出寄与セル相当値を、固定のStream・River境界へ分類します。
        /// Riverだけを単独で返さず、河川条件では必ずStreamも設定します。
        /// </summary>
        /// <param name="settings">Stream 16、River 128の固定境界を持つ設定です。</param>
        /// <param name="runoffEquivalentCells">0〜4,096の固定流出寄与セル相当値です。</param>
        /// <returns>None、Stream、またはStreamとRiverの組合せです。</returns>
        public static HydrologyConnectionFlags ClassifySurfaceWaterConnection(
            in HydrologyGenerationSettings settings,
            ushort runoffEquivalentCells)
        {
            if (runoffEquivalentCells >= settings.RiverRunoffEquivalentThreshold)
            {
                return HydrologyConnectionFlags.Stream | HydrologyConnectionFlags.River;
            }

            return runoffEquivalentCells >= settings.StreamRunoffEquivalentThreshold
                ? HydrologyConnectionFlags.Stream
                : HydrologyConnectionFlags.None;
        }

        /// <summary>生成済みD8セルへ固定9地点の基礎湿度を加え、上流Heap用の値へ変換します。</summary>
        internal static bool TryCreateCandidate(
            in WorldGenerationSettings settings,
            in GeneratedHydrologyCell cell,
            ushort flowDistance,
            out HydrologyUpstreamCandidate candidate)
        {
            candidate = default;
            if (!DeterministicHydrologyGenerator.TrySampleHydrologyMoisture(
                settings,
                cell.CellX,
                cell.CellY,
                out ushort moisture))
            {
                return false;
            }

            candidate = new HydrologyUpstreamCandidate
            {
                CellX = cell.CellX,
                CellY = cell.CellY,
                FlowDistanceFromTarget = flowDistance,
                HydrologyHeight = cell.HydrologyHeight,
                PlateauRank = cell.PlateauRank,
                HydrologyMoisture = moisture
            };
            return true;
        }

        /// <summary>優先度付きフロンティアへ候補を追加し、親より小さい間だけ上へ移動します。</summary>
        internal static bool TryPush(
            NativeArray<HydrologyUpstreamCandidate> frontier,
            int startIndex,
            ref int length,
            in HydrologyUpstreamCandidate candidate)
        {
            if (length >= MaximumFrontierCapacity)
            {
                return false;
            }

            int index = length;
            length++;
            frontier[startIndex + index] = candidate;
            while (index > 0)
            {
                int parentIndex = (index - 1) / 2;
                if (Compare(frontier[startIndex + parentIndex], frontier[startIndex + index]) <= 0)
                {
                    break;
                }

                HydrologyUpstreamCandidate parent = frontier[startIndex + parentIndex];
                frontier[startIndex + parentIndex] = frontier[startIndex + index];
                frontier[startIndex + index] = parent;
                index = parentIndex;
            }
            return true;
        }

        /// <summary>優先度が最小の候補を取り出し、末尾要素を下へ移動してHeap順を復元します。</summary>
        internal static HydrologyUpstreamCandidate Pop(
            NativeArray<HydrologyUpstreamCandidate> frontier,
            int startIndex,
            ref int length)
        {
            HydrologyUpstreamCandidate result = frontier[startIndex];
            length--;
            if (length == 0)
            {
                return result;
            }

            frontier[startIndex] = frontier[startIndex + length];
            int index = 0;
            while (true)
            {
                int leftIndex = (index * 2) + 1;
                if (leftIndex >= length)
                {
                    break;
                }

                int rightIndex = leftIndex + 1;
                int smallerChild = rightIndex < length
                    && Compare(frontier[startIndex + rightIndex], frontier[startIndex + leftIndex]) < 0
                    ? rightIndex
                    : leftIndex;
                if (Compare(frontier[startIndex + index], frontier[startIndex + smallerChild]) <= 0)
                {
                    break;
                }

                HydrologyUpstreamCandidate current = frontier[startIndex + index];
                frontier[startIndex + index] = frontier[startIndex + smallerChild];
                frontier[startIndex + smallerChild] = current;
                index = smallerChild;
            }
            return result;
        }

        /// <summary>距離、代表標高、PlateauRank、X、Yの正規化順で二候補を比較します。</summary>
        private static int Compare(
            in HydrologyUpstreamCandidate first,
            in HydrologyUpstreamCandidate second)
        {
            int distanceComparison = first.FlowDistanceFromTarget.CompareTo(second.FlowDistanceFromTarget);
            if (distanceComparison != 0)
            {
                return distanceComparison;
            }

            int heightComparison = first.HydrologyHeight.CompareTo(second.HydrologyHeight);
            if (heightComparison != 0)
            {
                return heightComparison;
            }

            int rankComparison = first.PlateauRank.CompareTo(second.PlateauRank);
            if (rankComparison != 0)
            {
                return rankComparison;
            }

            int xComparison = first.CellX.CompareTo(second.CellX);
            return xComparison != 0 ? xComparison : first.CellY.CompareTo(second.CellY);
        }

        /// <summary>水文セル座標へ-1〜+1をlongで加え、int範囲内の場合だけ返します。</summary>
        internal static bool TryAdd(int coordinate, int offset, out int result)
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

        /// <summary>D8方向IDを、隣接水文セル座標へ加えるX/Y差へ変換します。</summary>
        internal static void GetDirectionOffset(
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
    }
}
