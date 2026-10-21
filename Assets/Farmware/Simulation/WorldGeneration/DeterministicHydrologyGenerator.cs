// このファイルは、TERRAIN-01C3-A/Bの8×8水文セル、D8流向、最大4,096辺の下流終端を決定論的に生成します。
// ChunkGenerationSystem、状態ハッシュ、後続の集水・湖処理が同じ純粋計算を共有し、ロード順や表示状態による差を防ぎます。

using System;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 固定水文の基礎格子、代表標高、下流探索上限を再計算するため、ワールド寿命中に固定する設定です。
    /// WorldGenerationSettingsへ値として含め、GeneratorVersionと一緒に保存対象とします。
    /// </summary>
    public struct HydrologyGenerationSettings
    {
        /// <summary>1水文セルの1辺に含まれるタイル数です。TERRAIN-01C3では8タイルに固定します。</summary>
        public int CellSizeInTiles;

        /// <summary>代表標高の中心から正負へ離す固定サンプル距離です。単位はタイルです。</summary>
        public int RepresentativeSampleOffsetInTiles;

        /// <summary>1セルの下流終端を確定するために進める最大辺数です。C3-Bでは4,096辺に固定します。</summary>
        public int MaximumDownstreamEdges;

        /// <summary>Downstream Summaryの最大Levelです。Level 12が4,096辺を表します。</summary>
        public int MaximumDownstreamSummaryLevel;

        /// <summary>
        /// TERRAIN-01C3-A/Bの確定値を持つ既定設定を作ります。
        /// </summary>
        /// <returns>8×8セル、9点サンプル、最大4,096辺・Level 12を持つ設定です。</returns>
        public static HydrologyGenerationSettings CreateDefault()
        {
            return new HydrologyGenerationSettings
            {
                CellSizeInTiles = 8,
                RepresentativeSampleOffsetInTiles = 3,
                MaximumDownstreamEdges = 4096,
                MaximumDownstreamSummaryLevel = 12
            };
        }
    }

    /// <summary>
    /// 水文セルから隣接する8セルのどちらへ流れるかを表す安定値です。
    /// Noneは海洋流出口または陸上の閉鎖末端を表し、両者はHydrologyCellFlagsで区別します。
    /// </summary>
    public enum HydrologyFlowDirection : byte
    {
        /// <summary>下流セルを持たない状態です。</summary>
        None = 0,

        /// <summary>水平座標が1増える東隣へ流れます。</summary>
        East = 1,

        /// <summary>水平・垂直座標がともに1増える北東隣へ流れます。</summary>
        NorthEast = 2,

        /// <summary>垂直座標が1増える北隣へ流れます。</summary>
        North = 3,

        /// <summary>水平座標が1減り、垂直座標が1増える北西隣へ流れます。</summary>
        NorthWest = 4,

        /// <summary>水平座標が1減る西隣へ流れます。</summary>
        West = 5,

        /// <summary>水平・垂直座標がともに1減る南西隣へ流れます。</summary>
        SouthWest = 6,

        /// <summary>垂直座標が1減る南隣へ流れます。</summary>
        South = 7,

        /// <summary>水平座標が1増え、垂直座標が1減る南東隣へ流れます。</summary>
        SouthEast = 8
    }

    /// <summary>
    /// 水文セルの終端特性をビット単位で保持します。
    /// 後続の終端分類や接続フラグとは分離し、C3-Aでは中心タイルが海面未満かだけを記録します。
    /// </summary>
    [Flags]
    public enum HydrologyCellFlags : byte
    {
        /// <summary>海洋流出口ではない通常の陸上水文セルです。</summary>
        None = 0,

        /// <summary>中心タイルが海面未満で、下流を持たない海洋流出口です。</summary>
        OceanOutlet = 1 << 0
    }

    /// <summary>
    /// 固定水文が持つ自然接続を重複可能なビットとして表します。
    /// C3-Bでは下流終端3種だけを書き込み、河川・湖・地下水は後続PRで同じ値へ追加します。
    /// </summary>
    [Flags]
    public enum HydrologyConnectionFlags : ushort
    {
        /// <summary>確定した自然接続がまだない状態です。</summary>
        None = 0,

        /// <summary>固定表面流出が渓流・小川の条件を満たす接続です。C3-Cで使用します。</summary>
        Stream = 1 << 0,

        /// <summary>固定表面流出が河川の条件を満たす接続です。C3-Cで使用します。</summary>
        River = 1 << 1,

        /// <summary>固定湖面へ属する接続です。C3-Dで使用します。</summary>
        Lake = 1 << 2,

        /// <summary>固定地下水へ接続する状態です。C4で使用します。</summary>
        Groundwater = 1 << 3,

        /// <summary>4,096辺以内で陸上の流向なしセルへ到達する下流終端です。</summary>
        ClosedBasin = 1 << 4,

        /// <summary>4,096辺以内で海洋流出口へ到達する下流終端です。</summary>
        Marine = 1 << 5,

        /// <summary>4,096辺を進んでも海洋・閉鎖流域の終端へ到達しなかった固定分類です。</summary>
        LongDrainage = 1 << 6
    }

    /// <summary>
    /// 1つの8×8水文セルについて生成した固定の代表値、D8流向、下流終端です。
    /// 1論理チャンクEntityへ中心座標が属する8×8件を行優先で格納し、タイルBufferや現在水位とは分離します。
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct GeneratedHydrologyCell : IBufferElementData
    {
        /// <summary>水文セルの水平絶対座標です。中心タイルXはこの値の8倍です。</summary>
        public int CellX;

        /// <summary>水文セルの垂直絶対座標です。中心タイルYはこの値の8倍です。</summary>
        public int CellY;

        /// <summary>下流水文セルの水平絶対座標です。下流なしの場合は自身のCellXを保持します。</summary>
        public int DownstreamCellX;

        /// <summary>下流水文セルの垂直絶対座標です。下流なしの場合は自身のCellYを保持します。</summary>
        public int DownstreamCellY;

        /// <summary>終端到達時は終端セル、LongDrainage時は4,096辺進んだセルの水平絶対座標です。</summary>
        public int DownstreamReachCellX;

        /// <summary>終端到達時は終端セル、LongDrainage時は4,096辺進んだセルの垂直絶対座標です。</summary>
        public int DownstreamReachCellY;

        /// <summary>中心からX・Yとも-3、0、+3にある固定9標高の整数平均です。</summary>
        public ushort HydrologyHeight;

        /// <summary>中心タイルだけの標高です。海面閾値との比較に使用し、9点平均で置き換えません。</summary>
        public ushort CenterElevation;

        /// <summary>同じ代表標高の平坦面を循環なく並べる、Seed・版・座標・専用Salt由来の64bit順位です。</summary>
        public ulong PlateauRank;

        /// <summary>自身から終端または4,096辺上限までに実際に進んだD8辺数です。</summary>
        public ushort DownstreamEdgeCount;

        /// <summary>C3-Bの相互排他的な下流終端と、後続PRで追加する自然接続を保持します。</summary>
        public HydrologyConnectionFlags ConnectionFlags;

        /// <summary>隣接8セルのどちらへ流れるか、または下流なしを表す安定値です。</summary>
        public HydrologyFlowDirection FlowDirection;

        /// <summary>中心タイル由来の海洋流出口など、C3-Aで確定した特性です。</summary>
        public HydrologyCellFlags Flags;

        /// <summary>
        /// このセルが中心タイルの陸海判定による海洋流出口かを返します。
        /// </summary>
        public bool IsOceanOutlet => (Flags & HydrologyCellFlags.OceanOutlet) != 0;

        /// <summary>
        /// このセルがD8の下流セルを持つかを返します。
        /// </summary>
        public bool HasDownstream => FlowDirection != HydrologyFlowDirection.None;

        /// <summary>
        /// C3-Bで確定するMarine、ClosedBasin、LongDrainageのいずれか一つを返します。
        /// </summary>
        public HydrologyConnectionFlags DownstreamTerminal => ConnectionFlags
            & (HydrologyConnectionFlags.Marine
                | HydrologyConnectionFlags.ClosedBasin
                | HydrologyConnectionFlags.LongDrainage);
    }

    /// <summary>
    /// Seed、GeneratorVersion、固定設定、絶対整数座標だけから水文セルとD8流向を生成する純粋計算です。
    /// Burst Jobから呼べるよう、管理参照、例外、UnityEngine API、生成済みチャンク参照を使用しません。
    /// </summary>
    public static class DeterministicHydrologyGenerator
    {
        /// <summary>1論理チャンクの各軸に中心が属する水文セル数です。</summary>
        public const int CellsPerChunkAxis = 8;

        /// <summary>1論理チャンクへ格納する水文セル総数です。</summary>
        public const int CellsPerChunk = CellsPerChunkAxis * CellsPerChunkAxis;

        /// <summary>
        /// 水文設定が確定した8×8格子と9点サンプルを表すか検証します。
        /// </summary>
        /// <param name="settings">検証する固定水文設定です。</param>
        /// <returns>暗黙補正せずC3-A/Bの生成へ使用できる場合はtrueです。</returns>
        public static bool ValidateSettings(in HydrologyGenerationSettings settings)
        {
            return settings.CellSizeInTiles == 8
                && settings.RepresentativeSampleOffsetInTiles == 3
                && settings.MaximumDownstreamEdges == 4096
                && settings.MaximumDownstreamSummaryLevel == 12
                && (1 << settings.MaximumDownstreamSummaryLevel) == settings.MaximumDownstreamEdges
                && WorldGridConstants.LogicalChunkSize % settings.CellSizeInTiles == 0
                && WorldGridConstants.LogicalChunkSize / settings.CellSizeInTiles == CellsPerChunkAxis;
        }

        /// <summary>
        /// 絶対タイル座標を、担当範囲が中心から-4〜+3になる水文セル座標へ変換します。
        /// </summary>
        /// <param name="settings">8タイルのセル寸法を持つ固定設定です。</param>
        /// <param name="worldTileX">水平の絶対タイル座標です。</param>
        /// <param name="worldTileY">垂直の絶対タイル座標です。</param>
        /// <param name="cellX">成功時の水平水文セル座標です。</param>
        /// <param name="cellY">成功時の垂直水文セル座標です。</param>
        /// <returns>設定が有効で変換結果がint範囲内ならtrueです。</returns>
        public static bool TryGetCellCoordinate(
            in HydrologyGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            out int cellX,
            out int cellY)
        {
            cellX = default;
            cellY = default;
            if (!ValidateSettings(settings))
            {
                return false;
            }

            // +4をintで先に足すと最大座標付近で周回するため、longのまま床方向除算します。
            long horizontal = FloorDivide((long)worldTileX + 4L, settings.CellSizeInTiles);
            long vertical = FloorDivide((long)worldTileY + 4L, settings.CellSizeInTiles);
            if (horizontal < int.MinValue || horizontal > int.MaxValue
                || vertical < int.MinValue || vertical > int.MaxValue)
            {
                return false;
            }

            cellX = (int)horizontal;
            cellY = (int)vertical;
            return true;
        }

        /// <summary>
        /// 1水文セルの代表標高、中心標高、平坦面順位、D8下流、最大4,096辺の終端を生成します。
        /// </summary>
        /// <param name="settings">Seed、GeneratorVersion、標高、水文設定を持つワールド設定です。</param>
        /// <param name="cellX">水平の絶対水文セル座標です。</param>
        /// <param name="cellY">垂直の絶対水文セル座標です。</param>
        /// <param name="cell">成功時の固定水文セル結果です。</param>
        /// <returns>設定、中心、9サンプル、全D8隣接座標、下流経路が有効ならtrueです。</returns>
        public static bool TryGenerateCell(
            in WorldGenerationSettings settings,
            int cellX,
            int cellY,
            out GeneratedHydrologyCell cell)
        {
            cell = default;
            if (!DeterministicHydrologyDownstreamResolver.TryResolveWithStartCell(
                    settings,
                    cellX,
                    cellY,
                    out cell,
                    out HydrologyDownstreamResult downstreamResult))
            {
                return false;
            }

            // 一辺の流向と終端分類を同じBuffer要素へ揃え、後続Systemが部分的な水文状態を読まないようにします。
            cell.DownstreamReachCellX = downstreamResult.ReachedCellX;
            cell.DownstreamReachCellY = downstreamResult.ReachedCellY;
            cell.DownstreamEdgeCount = downstreamResult.EdgeCount;
            cell.ConnectionFlags = downstreamResult.Terminal;
            return true;
        }

        /// <summary>
        /// 1セルの代表値とD8一辺だけを生成し、下流終端の再帰計算を行わない内部APIです。
        /// Downstream Resolverが経路上のセルを有限回たどるために使用します。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="cellX">水平の絶対水文セル座標です。</param>
        /// <param name="cellY">垂直の絶対水文セル座標です。</param>
        /// <param name="cell">成功時の代表値とD8一辺です。終端集約フィールドは未設定です。</param>
        /// <returns>設定、中心、9サンプル、全D8隣接座標が有効ならtrueです。</returns>
        internal static bool TryGenerateFlowCell(
            in WorldGenerationSettings settings,
            int cellX,
            int cellY,
            out GeneratedHydrologyCell cell)
        {
            cell = default;
            if (!DeterministicEnvironmentGenerator.ValidateSettings(settings)
                || !ValidateSettings(settings.Hydrology)
                || !TryGenerateCellBasis(settings, cellX, cellY, out cell))
            {
                return false;
            }

            if (cell.IsOceanOutlet)
            {
                // 海洋流出口は必ず終端です。下流座標は未使用ですが、自身を入れてdefault座標との誤認を避けます。
                cell.DownstreamCellX = cell.CellX;
                cell.DownstreamCellY = cell.CellY;
                cell.FlowDirection = HydrologyFlowDirection.None;
                return true;
            }

            FixedList512Bytes<GeneratedHydrologyCell> neighbors = default;
            for (int directionIndex = 1; directionIndex <= 8; directionIndex++)
            {
                GetDirectionOffset((HydrologyFlowDirection)directionIndex, out int offsetX, out int offsetY);
                if (!TryAdd(cellX, offsetX, out int neighborX)
                    || !TryAdd(cellY, offsetY, out int neighborY)
                    || !TryGenerateCellBasis(settings, neighborX, neighborY, out GeneratedHydrologyCell neighbor))
                {
                    return false;
                }

                neighbors.Add(neighbor);
            }

            if (!TrySelectDownstream(cell, neighbors, out GeneratedHydrologyCell selected))
            {
                // 海でもなく、自身より小さい安定キーもないセルは閉鎖流域候補として下流なしにします。
                cell.DownstreamCellX = cell.CellX;
                cell.DownstreamCellY = cell.CellY;
                cell.FlowDirection = HydrologyFlowDirection.None;
                return true;
            }

            cell.DownstreamCellX = selected.CellX;
            cell.DownstreamCellY = selected.CellY;
            cell.FlowDirection = GetDirection(
                selected.CellX - cell.CellX,
                selected.CellY - cell.CellY);
            return cell.FlowDirection != HydrologyFlowDirection.None;
        }

        /// <summary>
        /// 8つのD8隣接候補から、海洋優先、次に安定Flow順で下流を一意に選びます。
        /// テストと将来のSummary構築が同じ候補選択を使い、候補配列の並び替えで結果が変わらないようにします。
        /// </summary>
        /// <param name="current">流向を決める陸上の基準セルです。</param>
        /// <param name="neighbors">同じ基準セルに隣接する8セルです。列挙順は結果へ使用しません。</param>
        /// <param name="selected">成功時に選ばれた下流セルです。</param>
        /// <returns>海洋流出口または自身より小さい陸上Flow順が見つかった場合はtrueです。</returns>
        public static bool TrySelectDownstream(
            in GeneratedHydrologyCell current,
            in FixedList512Bytes<GeneratedHydrologyCell> neighbors,
            out GeneratedHydrologyCell selected)
        {
            selected = default;
            if (current.IsOceanOutlet || neighbors.Length != 8)
            {
                return false;
            }

            bool foundDownstream = false;

            // 海岸の9点平均が高くても排水を妨げないよう、海洋流出口だけを標高比較より先に選びます。
            for (int index = 0; index < neighbors.Length; index++)
            {
                GeneratedHydrologyCell candidate = neighbors[index];
                if (candidate.IsOceanOutlet
                    && (!foundDownstream || CompareFlowOrder(candidate, selected) < 0))
                {
                    foundDownstream = true;
                    selected = candidate;
                }
            }

            if (foundDownstream)
            {
                return true;
            }

            for (int index = 0; index < neighbors.Length; index++)
            {
                GeneratedHydrologyCell candidate = neighbors[index];
                if (candidate.IsOceanOutlet || CompareFlowOrder(candidate, current) >= 0)
                {
                    // 陸上同士は代表標高、平坦面順位、衝突時座標が必ず小さくなる辺だけを許可します。
                    continue;
                }

                if (!foundDownstream || CompareFlowOrder(candidate, selected) < 0)
                {
                    foundDownstream = true;
                    selected = candidate;
                }
            }

            return foundDownstream;
        }

        /// <summary>
        /// 2水文セルを代表標高、平坦面順位、座標X、座標Yの安定順で比較します。
        /// ハッシュ衝突時も全順序を作り、候補列挙順を結果へ持ち込まないために使用します。
        /// </summary>
        /// <param name="first">1つ目の水文セルです。</param>
        /// <param name="second">2つ目の水文セルです。</param>
        /// <returns>firstが先なら負、同じキーなら0、後なら正です。</returns>
        public static int CompareFlowOrder(
            in GeneratedHydrologyCell first,
            in GeneratedHydrologyCell second)
        {
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

        /// <summary>
        /// 1論理チャンクの中心所有8×8水文セルを、行優先のNativeArrayへ生成します。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="chunkX">水平の論理チャンク座標です。</param>
        /// <param name="chunkY">垂直の論理チャンク座標です。</param>
        /// <param name="cells">呼び出し側が所有する64件以上の出力配列です。</param>
        /// <returns>全64件を生成できた場合はtrueです。失敗時は部分値を公開しないでください。</returns>
        public static bool TryGenerateChunkCells(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            NativeArray<GeneratedHydrologyCell> cells)
        {
            if (!cells.IsCreated || cells.Length < CellsPerChunk)
            {
                return false;
            }

            long firstCellX = (long)chunkX * CellsPerChunkAxis;
            long firstCellY = (long)chunkY * CellsPerChunkAxis;
            long lastCellX = firstCellX + CellsPerChunkAxis - 1L;
            long lastCellY = firstCellY + CellsPerChunkAxis - 1L;
            if (firstCellX < int.MinValue || firstCellY < int.MinValue
                || lastCellX > int.MaxValue || lastCellY > int.MaxValue)
            {
                return false;
            }

            for (int localY = 0; localY < CellsPerChunkAxis; localY++)
            {
                for (int localX = 0; localX < CellsPerChunkAxis; localX++)
                {
                    int index = (localY * CellsPerChunkAxis) + localX;
                    if (!TryGenerateCell(
                        settings,
                        (int)firstCellX + localX,
                        (int)firstCellY + localY,
                        out GeneratedHydrologyCell cell))
                    {
                        return false;
                    }

                    cells[index] = cell;
                }
            }

            return true;
        }

        /// <summary>
        /// 指定チャンクの64水文セルを、既存の地形チャンクハッシュへ順序付きで追加します。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="chunkCoordinate">Surface上の論理チャンク座標です。</param>
        /// <param name="currentHash">地形タイルまでを反映した現在の64bitハッシュです。</param>
        /// <returns>全水文セル、または明示的な生成失敗印を追加した64bitハッシュです。</returns>
        public static ulong AppendChunkHash(
            in WorldGenerationSettings settings,
            ChunkCoordinate chunkCoordinate,
            ulong currentHash)
        {
            long firstCellX = (long)chunkCoordinate.X * CellsPerChunkAxis;
            long firstCellY = (long)chunkCoordinate.Y * CellsPerChunkAxis;
            for (int localY = 0; localY < CellsPerChunkAxis; localY++)
            {
                for (int localX = 0; localX < CellsPerChunkAxis; localX++)
                {
                    long candidateX = firstCellX + localX;
                    long candidateY = firstCellY + localY;
                    if (candidateX < int.MinValue || candidateX > int.MaxValue
                        || candidateY < int.MinValue || candidateY > int.MaxValue
                        || !TryGenerateCell(
                            settings,
                            (int)candidateX,
                            (int)candidateY,
                            out GeneratedHydrologyCell cell))
                    {
                        // 不正設定や座標端を通常セルと同じdefault値へ潰さず、失敗自体を比較可能にします。
                        return WorldSeedUtility.Mix64(currentHash ^ 0xC3A0FA11ED000001UL);
                    }

                    currentHash = HashCell(currentHash, cell);
                }
            }

            return currentHash;
        }

        /// <summary>代表値と海洋判定だけを生成し、D8隣接から再帰的に流向を求めない内部処理です。</summary>
        private static bool TryGenerateCellBasis(
            in WorldGenerationSettings settings,
            int cellX,
            int cellY,
            out GeneratedHydrologyCell cell)
        {
            cell = default;
            if (!TryGetCellCenter(
                settings.Hydrology,
                cellX,
                cellY,
                out int centerX,
                out int centerY))
            {
                return false;
            }

            int sampleOffset = settings.Hydrology.RepresentativeSampleOffsetInTiles;
            long elevationTotal = 0L;
            for (int sampleYIndex = -1; sampleYIndex <= 1; sampleYIndex++)
            {
                for (int sampleXIndex = -1; sampleXIndex <= 1; sampleXIndex++)
                {
                    long sampleX = (long)centerX + (sampleXIndex * sampleOffset);
                    long sampleY = (long)centerY + (sampleYIndex * sampleOffset);
                    if (sampleX < int.MinValue || sampleX > int.MaxValue
                        || sampleY < int.MinValue || sampleY > int.MaxValue)
                    {
                        return false;
                    }

                    elevationTotal += DeterministicEnvironmentGenerator.SampleElevation(
                        settings,
                        (int)sampleX,
                        (int)sampleY);
                }
            }

            ushort centerElevation = DeterministicEnvironmentGenerator.SampleElevation(
                settings,
                centerX,
                centerY);
            bool isOceanOutlet = centerElevation < settings.SeaLevelThreshold;
            cell = new GeneratedHydrologyCell
            {
                CellX = cellX,
                CellY = cellY,
                DownstreamCellX = cellX,
                DownstreamCellY = cellY,
                HydrologyHeight = (ushort)(elevationTotal / 9L),
                CenterElevation = centerElevation,
                PlateauRank = WorldSeedUtility.HashCoordinate(
                    settings.WorldSeed,
                    settings.GeneratorVersion,
                    cellX,
                    cellY,
                    WorldGenerationPurposeSalts.HydrologyFlowTie),
                FlowDirection = HydrologyFlowDirection.None,
                Flags = isOceanOutlet
                    ? HydrologyCellFlags.OceanOutlet
                    : HydrologyCellFlags.None
            };
            return true;
        }

        /// <summary>水文セル座標を中心タイル座標へ64bitで変換し、9点サンプルを置ける場合だけ成功します。</summary>
        private static bool TryGetCellCenter(
            in HydrologyGenerationSettings settings,
            int cellX,
            int cellY,
            out int centerX,
            out int centerY)
        {
            long horizontal = (long)cellX * settings.CellSizeInTiles;
            long vertical = (long)cellY * settings.CellSizeInTiles;
            int offset = settings.RepresentativeSampleOffsetInTiles;
            if (horizontal - offset < int.MinValue || horizontal + offset > int.MaxValue
                || vertical - offset < int.MinValue || vertical + offset > int.MaxValue)
            {
                centerX = default;
                centerY = default;
                return false;
            }

            centerX = (int)horizontal;
            centerY = (int)vertical;
            return true;
        }

        /// <summary>正の除数に対してlong値を負の無限大方向へ丸めて除算します。</summary>
        private static long FloorDivide(long value, int positiveDivisor)
        {
            long quotient = value / positiveDivisor;
            long remainder = value % positiveDivisor;
            return remainder < 0L ? quotient - 1L : quotient;
        }

        /// <summary>水文セル座標へ-1〜+1をlongで加え、int範囲内の場合だけ返します。</summary>
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

        /// <summary>安定方向IDを、隣接水文セル座標へ加えるX/Y差へ変換します。</summary>
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

        /// <summary>隣接セルとの差を、保存互換性を持つD8方向IDへ変換します。</summary>
        private static HydrologyFlowDirection GetDirection(int offsetX, int offsetY)
        {
            if (offsetX == 1 && offsetY == 0) return HydrologyFlowDirection.East;
            if (offsetX == 1 && offsetY == 1) return HydrologyFlowDirection.NorthEast;
            if (offsetX == 0 && offsetY == 1) return HydrologyFlowDirection.North;
            if (offsetX == -1 && offsetY == 1) return HydrologyFlowDirection.NorthWest;
            if (offsetX == -1 && offsetY == 0) return HydrologyFlowDirection.West;
            if (offsetX == -1 && offsetY == -1) return HydrologyFlowDirection.SouthWest;
            if (offsetX == 0 && offsetY == -1) return HydrologyFlowDirection.South;
            if (offsetX == 1 && offsetY == -1) return HydrologyFlowDirection.SouthEast;
            return HydrologyFlowDirection.None;
        }

        /// <summary>1水文セルの全固定値を、それまでのチャンクハッシュへ順序付きで混合します。</summary>
        private static ulong HashCell(ulong currentHash, in GeneratedHydrologyCell cell)
        {
            unchecked
            {
                ulong coordinate = (uint)cell.CellX;
                coordinate |= (ulong)(uint)cell.CellY << 32;
                ulong downstream = (uint)cell.DownstreamCellX;
                downstream |= (ulong)(uint)cell.DownstreamCellY << 32;
                ulong downstreamReach = (uint)cell.DownstreamReachCellX;
                downstreamReach |= (ulong)(uint)cell.DownstreamReachCellY << 32;
                ulong values = cell.HydrologyHeight;
                values |= (ulong)cell.CenterElevation << 16;
                values |= (ulong)(byte)cell.FlowDirection << 32;
                values |= (ulong)(byte)cell.Flags << 40;
                ulong downstreamState = cell.DownstreamEdgeCount;
                downstreamState |= (ulong)(ushort)cell.ConnectionFlags << 16;

                ulong coordinateHash = WorldSeedUtility.Mix64(currentHash ^ coordinate);
                ulong downstreamHash = WorldSeedUtility.Mix64(coordinateHash ^ downstream);
                ulong reachHash = WorldSeedUtility.Mix64(downstreamHash ^ downstreamReach);
                ulong valuesHash = WorldSeedUtility.Mix64(reachHash ^ values);
                valuesHash = WorldSeedUtility.Mix64(valuesHash ^ downstreamState);
                return WorldSeedUtility.Mix64(valuesHash ^ cell.PlateauRank);
            }
        }
    }
}
