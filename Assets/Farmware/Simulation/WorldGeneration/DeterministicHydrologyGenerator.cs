// このファイルは、TERRAIN-01C3-A〜Dの8×8水文セル、D8流向、下流終端、集水・自然流出、基礎湖面と、C4-Cの最終湖区分を保持します。
// ChunkGenerationSystem、状態ハッシュ、湖Resolver、診断表示が同じ固定水文値を共有し、ロード順や表示状態による差を防ぎます。

using System;
using Farmware.Simulation;
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

        /// <summary>1対象セルへ寄与させる上流水文セル数の固定上限です。対象セル自身を含みます。</summary>
        public int MaximumCatchmentContributingCells;

        /// <summary>空間集約Summaryの最大Levelです。Level 6が64×64水文セルを表します。</summary>
        public int MaximumAggregationSummaryLevel;

        /// <summary>固定の渓流・小川と判定する最小RunoffEquivalentCellsです。</summary>
        public ushort StreamRunoffEquivalentThreshold;

        /// <summary>固定の河川と判定する最小RunoffEquivalentCellsです。RiverはStreamも必ず伴います。</summary>
        public ushort RiverRunoffEquivalentThreshold;

        /// <summary>同じ閉鎖末端へ流れるセル群を固定湖候補として扱う最小セル数です。</summary>
        public ushort MinimumClosedBasinAreaCells;

        /// <summary>正の水深を持つ閉鎖流域へLake候補を設定する最小RunoffEquivalentCellsです。</summary>
        public ushort LakeRunoffEquivalentThreshold;

        /// <summary>最大湖面へ到達し、安定した越流辺を有効にする最小RunoffEquivalentCellsです。</summary>
        public ushort LakeOverflowRunoffEquivalentThreshold;

        /// <summary>RunoffEquivalentCellsを0〜65,535の湖面充填率へ変換する固定倍率です。</summary>
        public ushort LakeFillFractionMultiplier;

        /// <summary>
        /// TERRAIN-01C3-A〜Dの確定値を持つ既定設定を作ります。
        /// </summary>
        /// <returns>8×8セル、9点サンプル、下流・上流各4,096件と固定流出閾値を持つ設定です。</returns>
        public static HydrologyGenerationSettings CreateDefault()
        {
            return new HydrologyGenerationSettings
            {
                CellSizeInTiles = 8,
                RepresentativeSampleOffsetInTiles = 3,
                MaximumDownstreamEdges = 4096,
                MaximumDownstreamSummaryLevel = 12,
                MaximumCatchmentContributingCells = 4096,
                MaximumAggregationSummaryLevel = 6,
                StreamRunoffEquivalentThreshold = 16,
                RiverRunoffEquivalentThreshold = 128,
                MinimumClosedBasinAreaCells = 4,
                LakeRunoffEquivalentThreshold = 8,
                LakeOverflowRunoffEquivalentThreshold = 32,
                LakeFillFractionMultiplier = 2048
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
    /// D8下流候補の比較に必要な値だけを保持する小型の一時値です。
    /// 集水・終端結果を含むGeneratedHydrologyCell全体を8件複製せず、固定512 bytes内で比較するために使用します。
    /// </summary>
    public struct HydrologyFlowCandidate
    {
        /// <summary>候補水文セルの水平絶対座標です。</summary>
        public int CellX;

        /// <summary>候補水文セルの垂直絶対座標です。</summary>
        public int CellY;

        /// <summary>同標高面の安定全順序を作る64bit順位です。</summary>
        public ulong PlateauRank;

        /// <summary>FlowKeyの先頭となる固定9地点の代表標高です。</summary>
        public ushort HydrologyHeight;

        /// <summary>海洋流出口かを比較時に判定する固定セル特性です。</summary>
        public HydrologyCellFlags Flags;

        /// <summary>
        /// この候補が中心タイルの陸海判定による海洋流出口かを返します。
        /// </summary>
        public bool IsOceanOutlet => (Flags & HydrologyCellFlags.OceanOutlet) != 0;
    }

    /// <summary>
    /// 集水域の有限探索で確定した補助状態をビット単位で保持します。
    /// 現在の降雨やロード済み範囲ではなく、固定生成結果だけを表します。
    /// </summary>
    [Flags]
    public enum HydrologyCatchmentFlags : byte
    {
        /// <summary>上流集合をすべて列挙でき、件数上限による打切りがない状態です。</summary>
        None = 0,

        /// <summary>4,096件の選択後も未選択の上流候補があり、固定上限で集計を打ち切った状態です。</summary>
        Truncated = 1 << 0
    }

    /// <summary>
    /// 閉鎖流域と基礎湖面の判定過程をビット単位で保持します。
    /// 湖にならない理由も固定結果へ残し、診断表示が生成処理を再解釈しないようにします。
    /// </summary>
    [Flags]
    public enum HydrologyLakeFlags : byte
    {
        /// <summary>海洋終端または長距離排水で、閉鎖流域の湖判定対象ではない状態です。</summary>
        None = 0,

        /// <summary>流向なしの陸上末端と、その末端へ流れる閉鎖流域を有限探索で解決した状態です。</summary>
        BasinResolved = 1 << 0,

        /// <summary>上流4,096件上限で流域境界が不完全なため、固定湖面を作らない状態です。</summary>
        BasinTruncated = 1 << 1,

        /// <summary>流域が4水文セル未満で、固定湖ではなく小池・一時水面候補に残した状態です。</summary>
        BasinTooSmall = 1 << 2,

        /// <summary>流出寄与が8未満で、固定湖ではなく乾燥窪地・一時湖候補に残した状態です。</summary>
        InsufficientRunoff = 1 << 3,

        /// <summary>越流標高が湖底以下で、正の水深を作れない状態です。</summary>
        NoPositiveDepth = 1 << 4,

        /// <summary>正の水深と流出条件を満たす固定湖候補が存在する閉鎖流域です。</summary>
        FixedLake = 1 << 5,

        /// <summary>この水文セルが湖底から連続する固定湖面セルへ含まれる状態です。</summary>
        SurfaceCell = 1 << 6,

        /// <summary>充填率が最大へ達し、SpillPointからの越流辺を持つ閉鎖流域です。</summary>
        Overflow = 1 << 7
    }

    /// <summary>
    /// 固定水文が持つ自然接続を重複可能なビットとして表します。
    /// C4-B2では下流終端3種、Stream・River、固定湖面のLake、固定地下水のGroundwaterを使用します。
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

        /// <summary>同じ代表標高の平坦面を循環なく並べる、Seed・版・座標・専用Salt由来の64bit順位です。</summary>
        public ulong PlateauRank;

        /// <summary>
        /// 同じ上流集合に含む各HydrologyMoistureの符号付き64bit合計です。最大268,431,360です。
        /// 仕様上の集約演算型を保ち、将来のSummary合成でも32bit型へ暗黙に狭めません。
        /// </summary>
        public long RunoffMoistureSum;

        /// <summary>正規化済み上流集合に含む水文セル数です。対象セル自身を含み、最大4,096です。</summary>
        public uint CatchmentAreaCells;

        /// <summary>中心からX・Yとも-3、0、+3にある固定9標高の整数平均です。</summary>
        public ushort HydrologyHeight;

        /// <summary>中心タイルだけの標高です。海面閾値との比較に使用し、9点平均で置き換えません。</summary>
        public ushort CenterElevation;

        /// <summary>代表標高と同じ固定9地点で求めた基礎湿度の整数平均です。</summary>
        public ushort HydrologyMoisture;

        /// <summary>涵養候補と固定9地点の補正後地質から求めた0〜65,535の固定地下水ポテンシャルです。</summary>
        public ushort GroundwaterPotential;

        /// <summary>RunoffMoistureSumを65,535で整数除算した流出寄与セル相当値です。</summary>
        public ushort RunoffEquivalentCells;

        /// <summary>選択した上流セルのうち、対象セルから最も遠いD8辺数です。</summary>
        public ushort MaximumContributingDistance;

        /// <summary>自身から終端または4,096辺上限までに実際に進んだD8辺数です。</summary>
        public ushort DownstreamEdgeCount;

        /// <summary>C3-Bの相互排他的な下流終端と、後続PRで追加する自然接続を保持します。</summary>
        public HydrologyConnectionFlags ConnectionFlags;

        /// <summary>閉鎖末端の正規化済み上流集合に含まれるセル数です。閉鎖流域以外では0です。</summary>
        public uint BasinAreaCells;

        /// <summary>安定した越流辺の流域内側セルXです。有効性はLakeFlagsのFixedLakeで判断します。</summary>
        public int SpillInsideCellX;

        /// <summary>安定した越流辺の流域内側セルYです。有効性はLakeFlagsのFixedLakeで判断します。</summary>
        public int SpillInsideCellY;

        /// <summary>安定した越流辺の流域外側セルXです。有効性はLakeFlagsのFixedLakeで判断します。</summary>
        public int SpillOutsideCellX;

        /// <summary>安定した越流辺の流域外側セルYです。有効性はLakeFlagsのFixedLakeで判断します。</summary>
        public int SpillOutsideCellY;

        /// <summary>流向なし末端の代表標高です。固定湖候補では湖底標高として使用します。</summary>
        public ushort LakeBottomHeight;

        /// <summary>流域境界で選んだ最小の越流標高です。境界未確定時は0です。</summary>
        public ushort SpillHeight;

        /// <summary>流出寄与へ2,048を掛けて0〜65,535へ飽和した固定湖面充填率です。</summary>
        public ushort LakeFillFraction;

        /// <summary>湖底から越流標高までを充填率で補間した固定湖面標高です。</summary>
        public ushort LakeSurfaceHeight;

        /// <summary>隣接8セルのどちらへ流れるか、または下流なしを表す安定値です。</summary>
        public HydrologyFlowDirection FlowDirection;

        /// <summary>中心タイル由来の海洋流出口など、C3-Aで確定した特性です。</summary>
        public HydrologyCellFlags Flags;

        /// <summary>上流集合が4,096件上限で打ち切られたかなど、C3-Cの集水補助状態です。</summary>
        public HydrologyCatchmentFlags CatchmentFlags;

        /// <summary>閉鎖流域の解決状態、湖にならない理由、湖面・越流の有無です。</summary>
        public HydrologyLakeFlags LakeFlags;

        /// <summary>
        /// C4-Cの水収支で確定した湖面セルの最終区分です。
        /// 湖面候補以外はNoneとし、Simulation、Gameビュー、Map Viewerが同じ分類を参照します。
        /// </summary>
        public LakeWaterBalanceClassification LakeClassification;

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

        /// <summary>
        /// 上流集合に未選択候補が残り、固定上限で打ち切られたかを返します。
        /// </summary>
        public bool CatchmentTruncated => (CatchmentFlags & HydrologyCatchmentFlags.Truncated) != 0;
    }

    /// <summary>
    /// 1論理チャンク内で同じ閉鎖末端へ到達する水文セル群を、並列湖Jobへ渡す作業項目です。
    /// 基礎水文完成後に一度だけ作り、各セルを互いに重ならない一つの流域作業へ割り当てます。
    /// </summary>
    internal struct HydrologyLakeChunkWorkItem
    {
        /// <summary>流向なしとなる陸上末端の水平水文セル座標です。</summary>
        public int SinkCellX;

        /// <summary>流向なしとなる陸上末端の垂直水文セル座標です。</summary>
        public int SinkCellY;

        /// <summary>同じ末端を持つチャンク内64セルを、配列添字に対応するビットで保持します。</summary>
        public ulong CellMask;
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
        /// 8行を並列生成するときに各行へ独立した上流Heapを貸すための、1チャンク分の固定作業要素数です。
        /// </summary>
        internal const int ParallelChunkFrontierCapacity =
            CellsPerChunkAxis * DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity;

        /// <summary>
        /// チャンク全体で同じ閉鎖末端を一度だけ解決するときに使う、閉鎖流域セルの固定作業要素数です。
        /// </summary>
        internal const int ChunkBasinMemberCapacity = DeterministicHydrologyLakeResolver.MaximumBasinMembers;

        /// <summary>チャンク全体の湖面解決で再利用する、流域座標Hash表の固定作業要素数です。</summary>
        internal const int ChunkBasinLookupCapacity = DeterministicHydrologyLakeResolver.LookupCapacity;

        /// <summary>最大8流域を並列解決するとき、各作業へ流域セル領域を一つずつ貸す総要素数です。</summary>
        internal const int ParallelChunkBasinMemberCapacity = CellsPerChunkAxis * ChunkBasinMemberCapacity;

        /// <summary>最大8流域を並列解決するとき、各作業へHash表領域を一つずつ貸す総要素数です。</summary>
        internal const int ParallelChunkBasinLookupCapacity = CellsPerChunkAxis * ChunkBasinLookupCapacity;

        /// <summary>
        /// 水文設定が確定した8×8格子、9点サンプル、有限の下流・上流上限を表すか検証します。
        /// </summary>
        /// <param name="settings">検証する固定水文設定です。</param>
        /// <returns>暗黙補正せずC3-A〜D1の生成へ使用できる場合はtrueです。</returns>
        public static bool ValidateSettings(in HydrologyGenerationSettings settings)
        {
            return settings.CellSizeInTiles == 8
                && settings.RepresentativeSampleOffsetInTiles == 3
                && settings.MaximumDownstreamEdges == 4096
                && settings.MaximumDownstreamSummaryLevel == 12
                && (1 << settings.MaximumDownstreamSummaryLevel) == settings.MaximumDownstreamEdges
                && settings.MaximumCatchmentContributingCells == 4096
                && settings.MaximumAggregationSummaryLevel == 6
                && (1 << (settings.MaximumAggregationSummaryLevel * 2))
                    == settings.MaximumCatchmentContributingCells
                && settings.StreamRunoffEquivalentThreshold == 16
                && settings.RiverRunoffEquivalentThreshold == 128
                && settings.StreamRunoffEquivalentThreshold < settings.RiverRunoffEquivalentThreshold
                && settings.MinimumClosedBasinAreaCells == 4
                && settings.LakeRunoffEquivalentThreshold == 8
                && settings.LakeOverflowRunoffEquivalentThreshold == 32
                && settings.LakeRunoffEquivalentThreshold < settings.LakeOverflowRunoffEquivalentThreshold
                && settings.LakeFillFractionMultiplier == 2048
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
        /// 1水文セルの代表値、D8下流、固定終端、最大4,096セルの集水・自然流出、閉鎖流域・基礎湖面を生成します。
        /// </summary>
        /// <param name="settings">Seed、GeneratorVersion、標高、水文設定を持つワールド設定です。</param>
        /// <param name="cellX">水平の絶対水文セル座標です。</param>
        /// <param name="cellY">垂直の絶対水文セル座標です。</param>
        /// <param name="cell">成功時の固定水文セル結果です。</param>
        /// <returns>基礎水文と、必要な場合は閉鎖流域・湖面を固定上限内で完全に生成できた場合はtrueです。</returns>
        public static bool TryGenerateCell(
            in WorldGenerationSettings settings,
            int cellX,
            int cellY,
            out GeneratedHydrologyCell cell)
        {
            return DeterministicHydrologyLakeResolver.TryResolveCell(
                settings,
                cellX,
                cellY,
                out cell);
        }

        /// <summary>
        /// 呼出元が再利用する上流フロンティアと呼出し単位の湖作業配列を使い、1水文セルの全固定値を生成します。
        /// 基礎集水のHeapだけは呼出元配列を再利用し、閉鎖流域の場合だけ追加の一時配列を確保します。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="cellX">水平の絶対水文セル座標です。</param>
        /// <param name="cellY">垂直の絶対水文セル座標です。</param>
        /// <param name="frontier">呼出元が所有する上流候補Heap用配列です。</param>
        /// <param name="cell">成功時の下流、集水、閉鎖流域、湖面を含む固定水文結果です。</param>
        /// <returns>基礎水文と、必要な場合は閉鎖流域・湖面を固定上限内で完全に生成できた場合はtrueです。</returns>
        internal static bool TryGenerateCell(
            in WorldGenerationSettings settings,
            int cellX,
            int cellY,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            out GeneratedHydrologyCell cell)
        {
            if (!TryGenerateCellWithoutLake(settings, cellX, cellY, frontier, 0, out cell))
            {
                // 下流だけが組み立てられた部分値を成功として返さないよう、基礎生成の失敗をそのまま伝えます。
                cell = default;
                return false;
            }

            if (cell.DownstreamTerminal != HydrologyConnectionFlags.ClosedBasin)
            {
                return true;
            }

            using NativeArray<HydrologyUpstreamCandidate> basinMembers =
                new NativeArray<HydrologyUpstreamCandidate>(
                    DeterministicHydrologyLakeResolver.MaximumBasinMembers,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<HydrologyBasinLookupSlot> basinLookup =
                new NativeArray<HydrologyBasinLookupSlot>(
                    DeterministicHydrologyLakeResolver.LookupCapacity,
                    Allocator.Temp,
                    NativeArrayOptions.ClearMemory);
            HydrologyLakeBasinCache cache = default;
            bool succeeded = DeterministicHydrologyLakeResolver.TryApplyLakeState(
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
                // 湖境界まで完成しなかったセルを、単一セルAPIの有効結果として誤利用させません。
                cell = default;
            }

            return succeeded;
        }

        /// <summary>
        /// 呼出元配列内の専用Heap領域を使い、並列の別行と作業メモリを共有せず1水文セルを生成します。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="cellX">水平の絶対水文セル座標です。</param>
        /// <param name="cellY">垂直の絶対水文セル座標です。</param>
        /// <param name="frontier">一つ以上の上流候補Heap領域を持つ作業配列です。</param>
        /// <param name="frontierStartIndex">この呼出しだけが使用する固定容量領域の先頭添字です。</param>
        /// <param name="cell">成功時の下流・上流を含む固定水文結果です。</param>
        /// <returns>設定、座標、下流辺、逆辺を固定上限内で検証できた場合はtrueです。</returns>
        internal static bool TryGenerateCellWithoutLake(
            in WorldGenerationSettings settings,
            int cellX,
            int cellY,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            int frontierStartIndex,
            out GeneratedHydrologyCell cell)
        {
            cell = default;
            if (!DeterministicHydrologyDownstreamResolver.TryResolveWithStartCell(
                    settings,
                    cellX,
                    cellY,
                    out cell,
                    out HydrologyDownstreamResult downstreamResult)
                || !DeterministicHydrologyCatchmentResolver.TryResolveWithStartCell(
                    settings,
                    cell,
                    frontier,
                    frontierStartIndex,
                    out HydrologyCatchmentResult catchmentResult))
            {
                return false;
            }

            // 下流と上流の両計算が成功してから同じBuffer要素へ揃え、部分的なHydrologyStateを公開しません。
            cell.DownstreamReachCellX = downstreamResult.ReachedCellX;
            cell.DownstreamReachCellY = downstreamResult.ReachedCellY;
            cell.DownstreamEdgeCount = downstreamResult.EdgeCount;
            cell.HydrologyMoisture = catchmentResult.TargetHydrologyMoisture;
            cell.CatchmentAreaCells = catchmentResult.CatchmentAreaCells;
            cell.RunoffMoistureSum = catchmentResult.RunoffMoistureSum;
            cell.RunoffEquivalentCells = catchmentResult.RunoffEquivalentCells;
            cell.MaximumContributingDistance = catchmentResult.MaximumContributingDistance;
            cell.ConnectionFlags = downstreamResult.Terminal | catchmentResult.SurfaceWaterConnectionFlags;
            cell.CatchmentFlags = catchmentResult.CatchmentFlags;
            return true;
        }

        /// <summary>
        /// 下流・集水の基礎結果へ閉鎖流域と湖面を追加し、同じ行の直前流域キャッシュを再利用します。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="cellX">対象水文セルの水平絶対座標です。</param>
        /// <param name="cellY">対象水文セルの垂直絶対座標です。</param>
        /// <param name="frontier">上流候補Heapを保持する呼出元所有配列です。</param>
        /// <param name="frontierStartIndex">この呼出しへ割り当てたHeap領域の先頭添字です。</param>
        /// <param name="basinMembers">閉鎖流域の選択セルを保持する呼出元所有配列です。</param>
        /// <param name="memberStartIndex">この呼出しへ割り当てた流域セル領域の先頭添字です。</param>
        /// <param name="basinLookup">座標が閉鎖流域に属するかを引く固定Hash表です。</param>
        /// <param name="lookupStartIndex">この呼出しへ割り当てたHash表領域の先頭添字です。</param>
        /// <param name="cache">同じ作業領域で直前に解決した閉鎖末端と流域結果です。</param>
        /// <param name="cell">成功時の下流、集水、閉鎖流域、湖面を含む固定結果です。</param>
        /// <returns>基礎水文と、必要な場合は閉鎖流域を固定上限内で解決できた場合はtrueです。</returns>
        internal static bool TryGenerateCell(
            in WorldGenerationSettings settings,
            int cellX,
            int cellY,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            int frontierStartIndex,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            int memberStartIndex,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            int lookupStartIndex,
            ref HydrologyLakeBasinCache cache,
            out GeneratedHydrologyCell cell)
        {
            return TryGenerateCellWithoutLake(
                    settings,
                    cellX,
                    cellY,
                    frontier,
                    frontierStartIndex,
                    out cell)
                && DeterministicHydrologyLakeResolver.TryApplyLakeState(
                    settings,
                    ref cell,
                    frontier,
                    frontierStartIndex,
                    basinMembers,
                    memberStartIndex,
                    basinLookup,
                    lookupStartIndex,
                    ref cache);
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

            // 下流選択に不要な集水・終端結果を複製せず、小型候補8件を固定512 bytes内へ収めます。
            FixedList512Bytes<HydrologyFlowCandidate> neighbors = default;
            for (int directionIndex = 1; directionIndex <= 8; directionIndex++)
            {
                GetDirectionOffset((HydrologyFlowDirection)directionIndex, out int offsetX, out int offsetY);
                if (!TryAdd(cellX, offsetX, out int neighborX)
                    || !TryAdd(cellY, offsetY, out int neighborY)
                    || !TryGenerateCellBasis(settings, neighborX, neighborY, out GeneratedHydrologyCell neighbor))
                {
                    return false;
                }

                neighbors.Add(CreateFlowCandidate(neighbor));
            }

            if (!TrySelectDownstream(cell, neighbors, out HydrologyFlowCandidate selected))
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
            in FixedList512Bytes<HydrologyFlowCandidate> neighbors,
            out HydrologyFlowCandidate selected)
        {
            selected = default;
            if (current.IsOceanOutlet || neighbors.Length != 8)
            {
                return false;
            }

            bool foundDownstream = false;
            HydrologyFlowCandidate currentCandidate = CreateFlowCandidate(current);

            // 海岸の9点平均が高くても排水を妨げないよう、海洋流出口だけを標高比較より先に選びます。
            for (int index = 0; index < neighbors.Length; index++)
            {
                HydrologyFlowCandidate candidate = neighbors[index];
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
                HydrologyFlowCandidate candidate = neighbors[index];
                if (candidate.IsOceanOutlet || CompareFlowOrder(candidate, currentCandidate) >= 0)
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

        /// <summary>D8選択用の小型候補二つを、代表標高、順位、座標の安定順で比較します。</summary>
        private static int CompareFlowOrder(
            in HydrologyFlowCandidate first,
            in HydrologyFlowCandidate second)
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

        /// <summary>完全な水文セルから、D8比較に必要な固定値だけを小型候補へコピーします。</summary>
        private static HydrologyFlowCandidate CreateFlowCandidate(in GeneratedHydrologyCell cell)
        {
            return new HydrologyFlowCandidate
            {
                CellX = cell.CellX,
                CellY = cell.CellY,
                PlateauRank = cell.PlateauRank,
                HydrologyHeight = cell.HydrologyHeight,
                Flags = cell.Flags
            };
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

            using NativeArray<HydrologyUpstreamCandidate> frontier =
                new NativeArray<HydrologyUpstreamCandidate>(
                    DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            return TryGenerateChunkCells(settings, chunkX, chunkY, cells, frontier);
        }

        /// <summary>
        /// 1論理チャンクの64水文セルを、呼出元が所有する上流探索配列を再利用して生成します。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="chunkX">水平の論理チャンク座標です。</param>
        /// <param name="chunkY">垂直の論理チャンク座標です。</param>
        /// <param name="cells">呼出元が所有する64件以上の出力配列です。</param>
        /// <param name="frontier">64セルで順次再利用する上流候補Heap用配列です。</param>
        /// <returns>全64件を生成できた場合はtrueです。失敗時は部分値を公開しないでください。</returns>
        internal static bool TryGenerateChunkCells(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            NativeArray<GeneratedHydrologyCell> cells,
            NativeArray<HydrologyUpstreamCandidate> frontier)
        {
            if (!cells.IsCreated || cells.Length < CellsPerChunk
                || !DeterministicHydrologyCatchmentResolver.HasRequiredFrontierCapacity(frontier))
            {
                return false;
            }

            using NativeArray<HydrologyUpstreamCandidate> basinMembers =
                new NativeArray<HydrologyUpstreamCandidate>(
                    DeterministicHydrologyLakeResolver.MaximumBasinMembers,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<HydrologyBasinLookupSlot> basinLookup =
                new NativeArray<HydrologyBasinLookupSlot>(
                    DeterministicHydrologyLakeResolver.LookupCapacity,
                    Allocator.Temp,
                    NativeArrayOptions.ClearMemory);
            return TryGenerateChunkCells(
                settings,
                chunkX,
                chunkY,
                cells,
                frontier,
                basinMembers,
                basinLookup);
        }

        /// <summary>
        /// 1論理チャンクの基礎水文を先に揃え、同じ閉鎖末端を共有するセル群へ湖面結果を一括適用します。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="chunkX">水平の論理チャンク座標です。</param>
        /// <param name="chunkY">垂直の論理チャンク座標です。</param>
        /// <param name="cells">呼出元が所有する64件以上の出力配列です。</param>
        /// <param name="frontier">上流探索で再利用する固定Heapです。</param>
        /// <param name="basinMembers">1閉鎖流域の選択セルを保持する作業配列です。</param>
        /// <param name="basinLookup">1閉鎖流域の座標と湖面状態を保持する固定Hash表です。</param>
        /// <returns>64件の基礎水文と全閉鎖流域を固定上限内で解決できた場合はtrueです。</returns>
        internal static bool TryGenerateChunkCells(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            NativeArray<GeneratedHydrologyCell> cells,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup)
        {
            if (!cells.IsCreated || cells.Length < CellsPerChunk
                || !DeterministicHydrologyCatchmentResolver.HasRequiredFrontierCapacity(frontier)
                || !basinMembers.IsCreated || basinMembers.Length < ChunkBasinMemberCapacity
                || !basinLookup.IsCreated || basinLookup.Length < ChunkBasinLookupCapacity)
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
                    if (!TryGenerateCellWithoutLake(
                            settings,
                            (int)firstCellX + localX,
                            (int)firstCellY + localY,
                            frontier,
                            0,
                            out GeneratedHydrologyCell cell))
                    {
                        return false;
                    }

                    cells[index] = cell;
                }
            }

            // 64セルの下流終端が揃ってからまとめることで、行をまたぐ同じ流域を一度だけ再構築します。
            return TryApplyChunkLakeStates(
                settings,
                cells,
                frontier,
                basinMembers,
                basinLookup);
        }

        /// <summary>
        /// 1論理チャンクの指定した水文1行について、湖面を除く基礎結果を生成します。
        /// 8行を独立Jobで並列化し、全行完了後にTryApplyChunkLakeStatesで湖面をまとめて解決します。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="chunkX">水平の論理チャンク座標です。</param>
        /// <param name="chunkY">垂直の論理チャンク座標です。</param>
        /// <param name="localY">チャンク内の水文行です。0以上8未満に限定します。</param>
        /// <param name="cells">全8行が互いに異なる範囲へ書き込む64件以上の出力配列です。</param>
        /// <param name="frontier">各行へ固定容量ずつ割り当てた、8領域分の上流Heap配列です。</param>
        /// <returns>指定行の8セルについて下流終端と集水結果まで生成できた場合はtrueです。</returns>
        internal static bool TryGenerateChunkCellRowWithoutLake(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            int localY,
            NativeArray<GeneratedHydrologyCell> cells,
            NativeArray<HydrologyUpstreamCandidate> frontier)
        {
            if (localY < 0 || localY >= CellsPerChunkAxis
                || !cells.IsCreated || cells.Length < CellsPerChunk
                || !frontier.IsCreated || frontier.Length < ParallelChunkFrontierCapacity)
            {
                return false;
            }

            long firstCellX = (long)chunkX * CellsPerChunkAxis;
            long firstCellY = (long)chunkY * CellsPerChunkAxis;
            long rowCellY = firstCellY + localY;
            long lastCellX = firstCellX + CellsPerChunkAxis - 1L;
            if (firstCellX < int.MinValue || lastCellX > int.MaxValue
                || rowCellY < int.MinValue || rowCellY > int.MaxValue)
            {
                return false;
            }

            // 行ごとに別Heap領域を使うため、Workerが同時に別行を処理しても候補値が混ざりません。
            int frontierStartIndex =
                localY * DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity;
            for (int localX = 0; localX < CellsPerChunkAxis; localX++)
            {
                int index = (localY * CellsPerChunkAxis) + localX;
                if (!TryGenerateCellWithoutLake(
                        settings,
                        (int)firstCellX + localX,
                        (int)rowCellY,
                        frontier,
                        frontierStartIndex,
                        out GeneratedHydrologyCell cell))
                {
                    return false;
                }

                cells[index] = cell;
            }

            return true;
        }

        /// <summary>
        /// 生成済み64セルを閉鎖末端ごとにまとめ、各流域を一度だけ構築して湖面状態を全対象セルへ適用します。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="cells">基礎水文が生成済みの64件以上の配列です。成功時は湖面結果を上書きします。</param>
        /// <param name="frontier">流域上流探索で再利用する固定Heapです。</param>
        /// <param name="basinMembers">1閉鎖流域の選択セルを保持する作業配列です。</param>
        /// <param name="basinLookup">1閉鎖流域の座標と湖面状態を保持する固定Hash表です。</param>
        /// <returns>全閉鎖流域の湖面状態を固定上限内で解決できた場合はtrueです。</returns>
        internal static bool TryApplyChunkLakeStates(
            in WorldGenerationSettings settings,
            NativeArray<GeneratedHydrologyCell> cells,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup)
        {
            if (!cells.IsCreated || cells.Length < CellsPerChunk
                || !DeterministicHydrologyCatchmentResolver.HasRequiredFrontierCapacity(frontier)
                || !basinMembers.IsCreated || basinMembers.Length < ChunkBasinMemberCapacity
                || !basinLookup.IsCreated || basinLookup.Length < ChunkBasinLookupCapacity)
            {
                return false;
            }

            for (int representativeIndex = 0; representativeIndex < CellsPerChunk; representativeIndex++)
            {
                GeneratedHydrologyCell representative = cells[representativeIndex];
                if (representative.DownstreamTerminal != HydrologyConnectionFlags.ClosedBasin
                    || (representative.LakeFlags & HydrologyLakeFlags.BasinResolved) != 0)
                {
                    continue;
                }

                // キャッシュが参照するHash表は次の流域で上書きされるため、同じ終端のセルを続けて全件処理します。
                HydrologyLakeBasinCache cache = default;
                if (!DeterministicHydrologyLakeResolver.TryApplyLakeState(
                        settings,
                        ref representative,
                        frontier,
                        0,
                        basinMembers,
                        0,
                        basinLookup,
                        0,
                        ref cache))
                {
                    return false;
                }

                cells[representativeIndex] = representative;
                for (int candidateIndex = representativeIndex + 1;
                    candidateIndex < CellsPerChunk;
                    candidateIndex++)
                {
                    GeneratedHydrologyCell candidate = cells[candidateIndex];
                    if (candidate.DownstreamTerminal != HydrologyConnectionFlags.ClosedBasin
                        || candidate.DownstreamReachCellX != representative.DownstreamReachCellX
                        || candidate.DownstreamReachCellY != representative.DownstreamReachCellY)
                    {
                        continue;
                    }

                    if (!DeterministicHydrologyLakeResolver.TryApplyLakeState(
                            settings,
                            ref candidate,
                            frontier,
                            0,
                            basinMembers,
                            0,
                            basinLookup,
                            0,
                            ref cache))
                    {
                        return false;
                    }

                    cells[candidateIndex] = candidate;
                }
            }

            return true;
        }

        /// <summary>
        /// 基礎水文64件を閉鎖末端ごとにまとめ、並列湖Jobが互いに異なるセルだけを書く作業一覧を作ります。
        /// </summary>
        /// <param name="cells">湖面適用前の基礎水文64件以上です。</param>
        /// <param name="workItems">最大64個の一意な閉鎖末端と対象セルMaskを受け取る配列です。</param>
        /// <param name="workItemCount">成功時に作成した一意な閉鎖末端数です。</param>
        /// <returns>配列容量が有効で、全閉鎖流域セルを重複しない作業へ割り当てた場合はtrueです。</returns>
        internal static bool TryBuildChunkLakeWorkItems(
            NativeArray<GeneratedHydrologyCell> cells,
            NativeArray<HydrologyLakeChunkWorkItem> workItems,
            out int workItemCount)
        {
            workItemCount = 0;
            if (!cells.IsCreated || cells.Length < CellsPerChunk
                || !workItems.IsCreated || workItems.Length < CellsPerChunk)
            {
                return false;
            }

            for (int cellIndex = 0; cellIndex < CellsPerChunk; cellIndex++)
            {
                GeneratedHydrologyCell cell = cells[cellIndex];
                if (cell.DownstreamTerminal != HydrologyConnectionFlags.ClosedBasin)
                {
                    continue;
                }

                int matchingWorkItemIndex = -1;
                for (int workItemIndex = 0; workItemIndex < workItemCount; workItemIndex++)
                {
                    HydrologyLakeChunkWorkItem existing = workItems[workItemIndex];
                    if (existing.SinkCellX == cell.DownstreamReachCellX
                        && existing.SinkCellY == cell.DownstreamReachCellY)
                    {
                        matchingWorkItemIndex = workItemIndex;
                        break;
                    }
                }

                ulong cellBit = 1UL << cellIndex;
                if (matchingWorkItemIndex >= 0)
                {
                    // 値型を取り出してから書き戻し、同じ末端のMaskだけへ現在セルを追加します。
                    HydrologyLakeChunkWorkItem existing = workItems[matchingWorkItemIndex];
                    existing.CellMask |= cellBit;
                    workItems[matchingWorkItemIndex] = existing;
                    continue;
                }

                workItems[workItemCount] = new HydrologyLakeChunkWorkItem
                {
                    SinkCellX = cell.DownstreamReachCellX,
                    SinkCellY = cell.DownstreamReachCellY,
                    CellMask = cellBit
                };
                workItemCount++;
            }

            return true;
        }

        /// <summary>
        /// 並列湖Jobの1担当が作業一覧を飛び石状に受け持ち、専用ワーク領域で各閉鎖流域を解決します。
        /// 作業Maskは重ならないため、別担当と同じ水文セルへ書きません。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="cells">基礎水文64件です。担当Maskのセルだけへ湖面結果を書き戻します。</param>
        /// <param name="workItems">閉鎖末端ごとの重複しない対象セルMaskです。</param>
        /// <param name="workItemCount">作業一覧内の有効要素数です。</param>
        /// <param name="laneIndex">0以上laneCount未満の担当番号です。</param>
        /// <param name="laneCount">同時に作業領域を所有する担当総数です。</param>
        /// <param name="frontier">担当ごとに独立した上流候補Heap領域を持つ配列です。</param>
        /// <param name="basinMembers">担当ごとに独立した流域セル領域を持つ配列です。</param>
        /// <param name="basinLookup">担当ごとに独立した流域所属Hash表領域を持つ配列です。</param>
        /// <returns>担当する全閉鎖流域を固定上限内で解決できた場合はtrueです。</returns>
        internal static bool TryApplyChunkLakeWorkItemsLane(
            in WorldGenerationSettings settings,
            NativeArray<GeneratedHydrologyCell> cells,
            NativeArray<HydrologyLakeChunkWorkItem> workItems,
            int workItemCount,
            int laneIndex,
            int laneCount,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup)
        {
            if (!cells.IsCreated || cells.Length < CellsPerChunk
                || !workItems.IsCreated || workItemCount < 0 || workItemCount > workItems.Length
                || laneCount != CellsPerChunkAxis || laneIndex < 0 || laneIndex >= laneCount
                || !frontier.IsCreated || frontier.Length < ParallelChunkFrontierCapacity
                || !basinMembers.IsCreated || basinMembers.Length < ParallelChunkBasinMemberCapacity
                || !basinLookup.IsCreated || basinLookup.Length < ParallelChunkBasinLookupCapacity)
            {
                return false;
            }

            int frontierStartIndex = laneIndex
                * DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity;
            int memberStartIndex = laneIndex * ChunkBasinMemberCapacity;
            int lookupStartIndex = laneIndex * ChunkBasinLookupCapacity;
            for (int workItemIndex = laneIndex;
                workItemIndex < workItemCount;
                workItemIndex += laneCount)
            {
                HydrologyLakeChunkWorkItem workItem = workItems[workItemIndex];
                HydrologyLakeBasinCache cache = default;
                bool appliedAnyCell = false;
                for (int cellIndex = 0; cellIndex < CellsPerChunk; cellIndex++)
                {
                    if ((workItem.CellMask & (1UL << cellIndex)) == 0UL)
                    {
                        continue;
                    }

                    GeneratedHydrologyCell cell = cells[cellIndex];
                    if (cell.DownstreamTerminal != HydrologyConnectionFlags.ClosedBasin
                        || cell.DownstreamReachCellX != workItem.SinkCellX
                        || cell.DownstreamReachCellY != workItem.SinkCellY
                        || !DeterministicHydrologyLakeResolver.TryApplyLakeState(
                            settings,
                            ref cell,
                            frontier,
                            frontierStartIndex,
                            basinMembers,
                            memberStartIndex,
                            basinLookup,
                            lookupStartIndex,
                            ref cache))
                    {
                        return false;
                    }

                    cells[cellIndex] = cell;
                    appliedAnyCell = true;
                }

                if (!appliedAnyCell)
                {
                    // 空Maskは構築処理の不整合なので、部分的な湖面を公開せず明示的に失敗させます。
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 指定チャンクの完成地形・Blockセル、地下水反映済み64水文セル、4096固定表流水、4096自然水文タイルを地形ハッシュへ追加します。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="biomeSettings">C5の新18Biome Definitionです。</param>
        /// <param name="paletteCatalog">自然Ground・Wallを選ぶ固定Blockパレットです。</param>
        /// <param name="terrainRuntimeIds">海水・淡水・マグマを直接設定する解決済みIDです。</param>
        /// <param name="chunkCoordinate">Surface上の論理チャンク座標です。</param>
        /// <param name="currentHash">呼び出し元が用意した64bitハッシュ初期値です。</param>
        /// <returns>全固定水文結果、または明示的な生成失敗印を追加した64bitハッシュです。</returns>
        public static ulong AppendChunkHash(
            in WorldGenerationSettings settings,
            in BiomeClassificationSettings biomeSettings,
            BlobAssetReference<TerrainBlockPaletteCatalogBlob> paletteCatalog,
            in BuiltInTerrainBlockRuntimeIds terrainRuntimeIds,
            ChunkCoordinate chunkCoordinate,
            ulong currentHash)
        {
            using NativeArray<HydrologyUpstreamCandidate> frontier =
                new NativeArray<HydrologyUpstreamCandidate>(
                    DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<HydrologyUpstreamCandidate> basinMembers =
                new NativeArray<HydrologyUpstreamCandidate>(
                    DeterministicHydrologyLakeResolver.MaximumBasinMembers,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<HydrologyBasinLookupSlot> basinLookup =
                new NativeArray<HydrologyBasinLookupSlot>(
                    DeterministicHydrologyLakeResolver.LookupCapacity,
                    Allocator.Temp,
                    NativeArrayOptions.ClearMemory);
            using NativeArray<GeneratedHydrologyCell> cells =
                new NativeArray<GeneratedHydrologyCell>(
                    CellsPerChunk,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            if (!TryGenerateChunkCells(
                    settings,
                    chunkCoordinate.X,
                    chunkCoordinate.Y,
                    cells,
                    frontier,
                    basinMembers,
                    basinLookup))
            {
                // 不正設定や座標端を通常セルと同じdefault値へ潰さず、失敗自体を比較可能にします。
                return WorldSeedUtility.Mix64(currentHash ^ 0xC3A0FA11ED000001UL);
            }

            using NativeArray<SurfaceWaterPathSearchNode> pathWorkspace =
                new NativeArray<SurfaceWaterPathSearchNode>(
                    DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<SurfaceWaterPathTile> pathTiles =
                new NativeArray<SurfaceWaterPathTile>(
                    DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<byte> floodVisited = new NativeArray<byte>(
                DeterministicSurfaceWaterGenerator.MaximumFloodTileCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using NativeArray<int> floodQueue = new NativeArray<int>(
                DeterministicSurfaceWaterGenerator.MaximumFloodTileCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using NativeArray<GeneratedSurfaceWaterTile> surfaceWater =
                new NativeArray<GeneratedSurfaceWaterTile>(
                    WorldGridConstants.TilesPerChunk,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<GeneratedHydrologyCell> intersectingCells =
                new NativeArray<GeneratedHydrologyCell>(
                    DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<SurfaceRechargeCandidate> surfaceRechargeCandidates =
                new NativeArray<SurfaceRechargeCandidate>(
                    DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount,
                    Allocator.Temp,
                    NativeArrayOptions.ClearMemory);
            using NativeArray<SurfaceRechargeCandidate> springSurfaceRechargeCandidates =
                new NativeArray<SurfaceRechargeCandidate>(
                    DeterministicLakeSpringGenerator.SpringRechargeCandidateCount,
                    Allocator.Temp,
                    NativeArrayOptions.ClearMemory);
            if (!DeterministicSurfaceWaterGenerator.TryGenerateChunk(
                    settings,
                    chunkCoordinate.X,
                    chunkCoordinate.Y,
                    cells,
                    frontier,
                    basinMembers,
                    basinLookup,
                    pathWorkspace,
                    pathTiles,
                    floodVisited,
                    floodQueue,
                    intersectingCells,
                    surfaceRechargeCandidates,
                    springSurfaceRechargeCandidates,
                    surfaceWater))
            {
                // 表流水だけが失敗した場合も通常の空Bufferと区別し、決定性監査が欠落を見逃さないようにします。
                return WorldSeedUtility.Mix64(currentHash ^ 0xC4A2FA11ED000001UL);
            }

            using NativeArray<GeneratedTile> terrainTiles = new NativeArray<GeneratedTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
            {
                for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
                {
                    int worldTileX = WorldGrid.ToWorldTileX(chunkCoordinate.X, localX);
                    int worldTileY = WorldGrid.ToWorldTileY(chunkCoordinate.Y, localY);
                    int tileIndex = WorldGrid.ToTileIndex(localX, localY);
                    if (!DeterministicChunkGenerator.TryGenerateTile(
                            settings,
                            worldTileX,
                            worldTileY,
                            out GeneratedTile terrainTile))
                    {
                        return WorldSeedUtility.Mix64(currentHash ^ 0xC4B2FA11ED000001UL);
                    }
                    SetTerrainTile(terrainTiles, tileIndex, terrainTile);
                }
            }

            using NativeArray<GeneratedHydrologyTile> hydrologyTiles =
                new NativeArray<GeneratedHydrologyTile>(
                    WorldGridConstants.TilesPerChunk,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            if (!DeterministicGroundwaterHydrologyGenerator.TryGenerateChunk(
                    settings,
                    chunkCoordinate.X,
                    chunkCoordinate.Y,
                    terrainTiles,
                    cells,
                    intersectingCells,
                    surfaceRechargeCandidates,
                    hydrologyTiles))
            {
                return WorldSeedUtility.Mix64(currentHash ^ 0xC4B2FA11ED000002UL);
            }

            using NativeArray<GeneratedHydrologyCell> springHydrologyCells =
                new NativeArray<GeneratedHydrologyCell>(
                    DeterministicLakeSpringGenerator.SpringHydrologyCellCount,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<SpringCandidate> springCandidates =
                new NativeArray<SpringCandidate>(
                    DeterministicLakeSpringGenerator.SpringCandidateCellCount,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            if (!DeterministicLakeSpringGenerator.TryFinalizeChunk(
                    settings,
                    chunkCoordinate.X,
                    chunkCoordinate.Y,
                    cells,
                    intersectingCells,
                    surfaceWater,
                    frontier,
                    basinMembers,
                    basinLookup,
                    pathWorkspace,
                    pathTiles,
                    springSurfaceRechargeCandidates,
                    springHydrologyCells,
                    springCandidates))
            {
                // C4-Cの湖最終区分または泉生成に失敗した状態を、通常の水面なしチャンクと区別します。
                return WorldSeedUtility.Mix64(currentHash ^ 0xC4C0FA11ED000001UL);
            }

            using NativeArray<GeneratedHydrologyCell> naturalFloodingScratchHydrologyCells =
                new NativeArray<GeneratedHydrologyCell>(
                    CellsPerChunk,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<GeneratedSurfaceWaterTile> naturalFloodingScratchSurfaceWater =
                new NativeArray<GeneratedSurfaceWaterTile>(
                    WorldGridConstants.TilesPerChunk,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<GeneratedHydrologyCell>
                naturalFloodingScratchIntersectingHydrologyCells =
                    new NativeArray<GeneratedHydrologyCell>(
                        DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount,
                        Allocator.Temp,
                        NativeArrayOptions.UninitializedMemory);
            using NativeArray<SurfaceRechargeCandidate>
                naturalFloodingScratchSurfaceRechargeCandidates =
                    new NativeArray<SurfaceRechargeCandidate>(
                        DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount,
                        Allocator.Temp,
                        NativeArrayOptions.UninitializedMemory);
            using NativeArray<GeneratedSurfaceWaterTile> naturalFloodingSourceWater =
                new NativeArray<GeneratedSurfaceWaterTile>(
                    DeterministicWetlandExtentGenerator.SourceAreaTileCount,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<byte> wetlandCandidateCells = new NativeArray<byte>(
                DeterministicWetlandExtentGenerator.EvaluationCellCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using NativeArray<GeneratedHydrologyCell> wetlandEvaluationHydrologyCells =
                new NativeArray<GeneratedHydrologyCell>(
                    DeterministicWetlandExtentGenerator.EvaluationCellCount,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<byte> wetlandEvaluationHydrologyCellWritten =
                new NativeArray<byte>(
                    DeterministicWetlandExtentGenerator.EvaluationCellCount,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<byte> largeWetlandExtentCells = new NativeArray<byte>(
                DeterministicWetlandExtentGenerator.EvaluationCellCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using NativeArray<byte> wetlandVisitedCells = new NativeArray<byte>(
                DeterministicWetlandExtentGenerator.EvaluationCellCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using NativeArray<int> wetlandCellQueue = new NativeArray<int>(
                DeterministicWetlandExtentGenerator.EvaluationCellCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            if (!DeterministicWetlandExtentGenerator.TryGenerateChunk(
                    settings,
                    chunkCoordinate.X,
                    chunkCoordinate.Y,
                    terrainTiles,
                    hydrologyTiles,
                    surfaceWater,
                    springHydrologyCells,
                    frontier,
                    basinMembers,
                    basinLookup,
                    pathWorkspace,
                    pathTiles,
                    floodVisited,
                    floodQueue,
                    naturalFloodingScratchHydrologyCells,
                    naturalFloodingScratchIntersectingHydrologyCells,
                    naturalFloodingScratchSurfaceRechargeCandidates,
                    naturalFloodingScratchSurfaceWater,
                    naturalFloodingSourceWater,
                    wetlandEvaluationHydrologyCells,
                    wetlandEvaluationHydrologyCellWritten,
                    wetlandCandidateCells,
                    largeWetlandExtentCells,
                    wetlandVisitedCells,
                    wetlandCellQueue))
            {
                // C4-D2の周辺淡水源再構築または湿原連結判定失敗を、通常の低冠水チャンクと区別します。
                return WorldSeedUtility.Mix64(currentHash ^ 0xC4D0FA11ED000001UL);
            }

            // C4の湖・泉・自然冠水が完成してから全4096タイルを新18Biomeへ分類し、公開時と同じ最終地形を混合します。
            for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
            {
                for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
                {
                    int tileIndex = WorldGrid.ToTileIndex(localX, localY);
                    int worldTileX = WorldGrid.ToWorldTileX(chunkCoordinate.X, localX);
                    int worldTileY = WorldGrid.ToWorldTileY(chunkCoordinate.Y, localY);
                    if (!DeterministicBiomeChunkFinalizer.TryFinalizeTile(
                            settings,
                            biomeSettings,
                            worldTileX,
                            worldTileY,
                            terrainTiles[tileIndex],
                            hydrologyTiles[tileIndex],
                            surfaceWater[tileIndex],
                            out GeneratedTile finalizedTile))
                    {
                        return WorldSeedUtility.Mix64(currentHash ^ 0xC5C0FA11ED000001UL);
                    }

                    if (!DeterministicTerrainCellFinalizer.TryFinalizeSurfaceCell(
                            settings,
                            paletteCatalog,
                            terrainRuntimeIds,
                            worldTileX,
                            worldTileY,
                            finalizedTile,
                            hydrologyTiles[tileIndex],
                            surfaceWater[tileIndex],
                            out GeneratedTerrainCell terrainCell))
                    {
                        // ECS公開と同じ確定器が失敗した状態を、Blockセル未混合の通常チャンクと区別します。
                        return WorldSeedUtility.Mix64(currentHash ^ 0xC6C2FA11ED000001UL);
                    }

                    // ECS公開経路と同じBlockセル由来の暫定資源適性を混合し、署名だけが旧分類へ依存しないようにします。
                    finalizedTile.ResourceSuitability =
                        DeterministicTerrainCellFinalizer.ClassifyInterimResourceSuitability(
                            terrainCell);
                    // using変数のNativeArrayはreadonly扱いになるため、既存の値コピー用ヘルパー経由で書き戻します。
                    SetTerrainTile(terrainTiles, tileIndex, finalizedTile);
                    currentHash = DeterministicChunkGenerator.HashTile(currentHash, finalizedTile);
                    currentHash = DeterministicTerrainCellFinalizer.HashCell(
                        currentHash,
                        terrainCell);
                }
            }

            // 湖水収支・泉JobがLake・Stream接続まで確定し、自然冠水も成功した後の64セルを固定順で混合します。
            for (int index = 0; index < CellsPerChunk; index++)
            {
                currentHash = HashCell(currentHash, cells[index]);
            }

            for (int index = 0; index < WorldGridConstants.TilesPerChunk; index++)
            {
                currentHash = DeterministicSurfaceWaterGenerator.HashTile(currentHash, surfaceWater[index]);
                currentHash = DeterministicGroundwaterHydrologyGenerator.HashTile(
                    currentHash,
                    hydrologyTiles[index]);
            }

            return currentHash;
        }

        /// <summary>
        /// using宣言で破棄責任を固定したNativeArrayへ、値コピーした配列ハンドルから1タイルを書き込みます。
        /// </summary>
        /// <param name="tiles">呼出し元が所有し、このメソッドでは破棄しない地形配列です。</param>
        /// <param name="index">0〜4095の行優先タイル添字です。</param>
        /// <param name="tile">書き込む完成済み基礎地形です。</param>
        private static void SetTerrainTile(
            NativeArray<GeneratedTile> tiles,
            int index,
            in GeneratedTile tile)
        {
            tiles[index] = tile;
        }

        /// <summary>
        /// 代表値と海洋判定だけを生成し、D8隣接から再帰的に流向を求めない内部処理です。
        /// 流向候補と湖の越流辺比較は標高・順位だけが必要なため、同じ基礎値を共有して不要な隣接生成を避けます。
        /// </summary>
        /// <param name="settings">標高、海面、順位Saltを含む固定ワールド生成設定です。</param>
        /// <param name="cellX">水平の絶対水文セル座標です。</param>
        /// <param name="cellY">垂直の絶対水文セル座標です。</param>
        /// <param name="cell">成功時の代表標高、中心標高、順位、海洋フラグを持つ基礎セルです。</param>
        /// <returns>中心と固定9サンプル座標が有効で、基礎セルを生成できた場合はtrueです。</returns>
        internal static bool TryGenerateCellBasis(
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

        /// <summary>
        /// 代表標高と同じ固定9地点の基礎湿度を64bit合計し、整数平均を返します。
        /// 現在の降雨や土壌水分を混ぜず、上流集合の固定流出寄与だけに使用します。
        /// </summary>
        /// <param name="settings">固定ワールド生成設定です。</param>
        /// <param name="cellX">水平の絶対水文セル座標です。</param>
        /// <param name="cellY">垂直の絶対水文セル座標です。</param>
        /// <param name="moisture">成功時の0〜65,535の固定9地点平均です。</param>
        /// <returns>中心と全9サンプル座標が有効で、基礎気候を生成できた場合はtrueです。</returns>
        internal static bool TrySampleHydrologyMoisture(
            in WorldGenerationSettings settings,
            int cellX,
            int cellY,
            out ushort moisture)
        {
            moisture = default;
            if (!TryGetCellCenter(settings.Hydrology, cellX, cellY, out int centerX, out int centerY))
            {
                return false;
            }

            int sampleOffset = settings.Hydrology.RepresentativeSampleOffsetInTiles;
            long moistureTotal = 0L;
            for (int sampleYIndex = -1; sampleYIndex <= 1; sampleYIndex++)
            {
                for (int sampleXIndex = -1; sampleXIndex <= 1; sampleXIndex++)
                {
                    long sampleX = (long)centerX + (sampleXIndex * sampleOffset);
                    long sampleY = (long)centerY + (sampleYIndex * sampleOffset);
                    if (sampleX < int.MinValue || sampleX > int.MaxValue
                        || sampleY < int.MinValue || sampleY > int.MaxValue
                        || !DeterministicEnvironmentGenerator.TryGenerateBaseClimate(
                            settings,
                            (int)sampleX,
                            (int)sampleY,
                            out GeneratedEnvironmentData environment))
                    {
                        return false;
                    }

                    moistureTotal += environment.BaseMoisture;
                }
            }

            moisture = (ushort)(moistureTotal / 9L);
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
                downstreamState |= (ulong)(byte)cell.CatchmentFlags << 32;
                ulong catchmentCount = cell.CatchmentAreaCells;
                ulong runoffMoistureSum = (ulong)cell.RunoffMoistureSum;
                ulong catchmentValues = cell.HydrologyMoisture;
                catchmentValues |= (ulong)cell.GroundwaterPotential << 16;
                catchmentValues |= (ulong)cell.RunoffEquivalentCells << 32;
                catchmentValues |= (ulong)cell.MaximumContributingDistance << 48;
                ulong basinArea = cell.BasinAreaCells;
                basinArea |= (ulong)(byte)cell.LakeFlags << 32;
                basinArea |= (ulong)(byte)cell.LakeClassification << 40;
                ulong spillInside = (uint)cell.SpillInsideCellX;
                spillInside |= (ulong)(uint)cell.SpillInsideCellY << 32;
                ulong spillOutside = (uint)cell.SpillOutsideCellX;
                spillOutside |= (ulong)(uint)cell.SpillOutsideCellY << 32;
                ulong lakeValues = cell.LakeBottomHeight;
                lakeValues |= (ulong)cell.SpillHeight << 16;
                lakeValues |= (ulong)cell.LakeFillFraction << 32;
                lakeValues |= (ulong)cell.LakeSurfaceHeight << 48;

                ulong coordinateHash = WorldSeedUtility.Mix64(currentHash ^ coordinate);
                ulong downstreamHash = WorldSeedUtility.Mix64(coordinateHash ^ downstream);
                ulong reachHash = WorldSeedUtility.Mix64(downstreamHash ^ downstreamReach);
                ulong valuesHash = WorldSeedUtility.Mix64(reachHash ^ values);
                valuesHash = WorldSeedUtility.Mix64(valuesHash ^ downstreamState);
                ulong catchmentHash = WorldSeedUtility.Mix64(valuesHash ^ catchmentCount);
                catchmentHash = WorldSeedUtility.Mix64(catchmentHash ^ runoffMoistureSum);
                catchmentHash = WorldSeedUtility.Mix64(catchmentHash ^ catchmentValues);
                ulong basinHash = WorldSeedUtility.Mix64(catchmentHash ^ basinArea);
                basinHash = WorldSeedUtility.Mix64(basinHash ^ spillInside);
                basinHash = WorldSeedUtility.Mix64(basinHash ^ spillOutside);
                basinHash = WorldSeedUtility.Mix64(basinHash ^ lakeValues);
                return WorldSeedUtility.Mix64(basinHash ^ cell.PlateauRank);
            }
        }
    }
}
