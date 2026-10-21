// このファイルは、8×8水文セルの河道と閉鎖流域の湖面を、1論理チャンク4096タイルの固定表流水へ細分化します。
// DeterministicHydrologyGeneratorの流向・流出・湖面を正として読み、現在水位や旧GeneratedTile.WaterBodyTypeへは書き戻しません。

using System;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 1タイルに描き下ろした固定表流水の種類です。
    /// 数値は将来のセーブ互換に使える安定値として扱い、既存値を別用途へ再利用しません。
    /// </summary>
    public enum SurfaceWaterTileType : byte
    {
        /// <summary>河道、湖面候補、確定水面のいずれも存在しないタイルです。</summary>
        None = 0,

        /// <summary>Stream水文辺の中心線だけを占める固定の渓流です。</summary>
        Stream = 1,

        /// <summary>River水文辺の中心線と上下左右1タイルを占める固定の河川です。</summary>
        River = 2,

        /// <summary>閉鎖流域の湖底から連続する長期的な湖面候補です。恒久性と塩分はC4-Cで確定します。</summary>
        LakeCandidate = 3,

        /// <summary>C4-Cで確定する恒久淡水湖用の予約済み安定値です。</summary>
        PermanentFreshwaterLake = 4,

        /// <summary>C4-Cで確定する恒久塩湖用の予約済み安定値です。</summary>
        PermanentSaltwaterLake = 5,

        /// <summary>C4-Cで地下水の泉から追加する固定淡水流用の予約済み安定値です。</summary>
        SpringStream = 6
    }

    /// <summary>
    /// 固定表流水が後続水文へ提供する意味を、種類とは独立した重複可能なビットで保持します。
    /// C4-A2では河道を確定自然水面・淡水接続、湖面候補を涵養候補として区別します。
    /// </summary>
    [Flags]
    public enum SurfaceWaterTileFlags : byte
    {
        /// <summary>表流水由来の固定属性がない状態です。</summary>
        None = 0,

        /// <summary>後続の地下水計算へ長期的な涵養入力を提供できる候補です。</summary>
        RechargeCandidate = 1 << 0,

        /// <summary>候補ではなく固定生成で水面として成立している河道または後続の恒久湖です。</summary>
        NaturalSurfaceWater = 1 << 1,

        /// <summary>後続の水域接続が淡水としてたどれる表流水です。塩湖では設定しません。</summary>
        FreshwaterConnected = 1 << 2,

        /// <summary>河道幅の周辺タイルではなく、C4-A1が選んだ中心線そのものです。</summary>
        ChannelCenterLine = 1 << 3
    }

    /// <summary>
    /// 1論理チャンクで行優先4096件を保持する固定表流水Buffer要素です。
    /// Buffer添字から絶対座標を復元できるため座標は重複保持せず、代表供給元の水文セルだけを記録します。
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct GeneratedSurfaceWaterTile : IBufferElementData
    {
        /// <summary>このタイルへ描き下ろした河道、湖面候補、または後続の確定水面の種類です。</summary>
        public SurfaceWaterTileType Type;

        /// <summary>涵養候補、確定自然水面、淡水接続、中心線を区別する固定状態です。</summary>
        public SurfaceWaterTileFlags Flags;

        /// <summary>河道では元地形の連続標高、湖面候補では固定湖面標高を0〜65,535で保持します。</summary>
        public ushort SurfaceHeight;

        /// <summary>河道では供給元RunoffEquivalentCells、湖ではLakeFillFractionを保持する0〜65,535の固定量です。</summary>
        public ushort SourceAmount;

        /// <summary>代表供給元となる河道上流セル、または閉鎖流域末端の水平水文セル座標です。</summary>
        public int SourceCellX;

        /// <summary>代表供給元となる河道上流セル、または閉鎖流域末端の垂直水文セル座標です。</summary>
        public int SourceCellY;
    }

    /// <summary>
    /// 完成済み固定水文から河道幅と連続湖面候補を4096タイルへ描き下ろす純粋計算です。
    /// 呼出し側所有の固定長NativeArrayだけを使い、Job内で管理メモリを確保しません。
    /// </summary>
    public static class DeterministicSurfaceWaterGenerator
    {
        /// <summary>最大4,096水文セルそれぞれの8×8タイルを表せるFlood Fill上限です。</summary>
        public const int MaximumFloodTileCount =
            DeterministicHydrologyLakeResolver.MaximumBasinMembers * 8 * 8;

        /// <summary>一つの水文セルが担当する各軸のタイル数です。</summary>
        private const int TilesPerHydrologyCellAxis = 8;

        /// <summary>水文セル中心から担当8×8の最小タイルまでのオフセットです。</summary>
        private const int ResponsibilityMinimumOffset = -4;

        /// <summary>河道幅が中心線の上下左右へ広がる固定半径です。</summary>
        private const int RiverOrthogonalRadius = 1;

        /// <summary>
        /// 生成済み64水文セルから、指定Surfaceチャンクの4096固定表流水要素を生成します。
        /// </summary>
        /// <param name="settings">Seed、GeneratorVersion、海面、標高、水文設定を持つ固定生成設定です。</param>
        /// <param name="chunkX">出力する水平論理チャンク座標です。</param>
        /// <param name="chunkY">出力する垂直論理チャンク座標です。</param>
        /// <param name="hydrologyCells">湖面解決まで完成した、対象チャンクの行優先64水文セルです。</param>
        /// <param name="frontier">外周河道と流域再構築で再利用する最大4,096件以上の上流候補Heapです。</param>
        /// <param name="basinMembers">一つの閉鎖流域を保持する4,096件以上の作業配列です。</param>
        /// <param name="basinLookup">流域座標を検索する8,192件以上の固定Hash表です。</param>
        /// <param name="pathWorkspace">河道中心線探索へ貸す256件以上の作業配列です。</param>
        /// <param name="pathTiles">河道中心線を一時保持する256件以上の配列です。</param>
        /// <param name="floodVisited">流域全タイルの訪問状態を保持する262,144件以上の配列です。</param>
        /// <param name="floodQueue">同じ流域全タイルを一度ずつ保持できる262,144件以上の待ち行列です。</param>
        /// <param name="surfaceWater">成功時に行優先4096件をすべて上書きする出力配列です。</param>
        /// <returns>全河道と対象湖面候補を固定上限内で最後まで生成できた場合はtrueです。</returns>
        internal static bool TryGenerateChunk(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            NativeArray<GeneratedHydrologyCell> hydrologyCells,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            NativeArray<SurfaceWaterPathSearchNode> pathWorkspace,
            NativeArray<SurfaceWaterPathTile> pathTiles,
            NativeArray<byte> floodVisited,
            NativeArray<int> floodQueue,
            NativeArray<GeneratedSurfaceWaterTile> surfaceWater)
        {
            if (!DeterministicEnvironmentGenerator.ValidateSettings(settings)
                || !DeterministicHydrologyGenerator.ValidateSettings(settings.Hydrology)
                || !hydrologyCells.IsCreated
                || hydrologyCells.Length < DeterministicHydrologyGenerator.CellsPerChunk
                || !frontier.IsCreated
                || frontier.Length < DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity
                || !basinMembers.IsCreated
                || basinMembers.Length < DeterministicHydrologyLakeResolver.MaximumBasinMembers
                || !basinLookup.IsCreated
                || basinLookup.Length < DeterministicHydrologyLakeResolver.LookupCapacity
                || !pathWorkspace.IsCreated
                || pathWorkspace.Length < DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount
                || !pathTiles.IsCreated
                || pathTiles.Length < DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount
                || !floodVisited.IsCreated || floodVisited.Length < MaximumFloodTileCount
                || !floodQueue.IsCreated || floodQueue.Length < MaximumFloodTileCount
                || !surfaceWater.IsCreated || surfaceWater.Length < WorldGridConstants.TilesPerChunk
                || !TryGetChunkBounds(chunkX, chunkY, out int minimumTileX, out int minimumTileY,
                    out int maximumTileX, out int maximumTileY)
                || !TryGetHydrologyBufferOrigin(chunkX, chunkY, out int firstCellX, out int firstCellY)
                || !TryGetHydrologyScanBounds(
                    settings.Hydrology,
                    minimumTileX,
                    minimumTileY,
                    maximumTileX,
                    maximumTileY,
                    out int minimumIntersectingCellX,
                    out int minimumIntersectingCellY,
                    out int maximumIntersectingCellX,
                    out int maximumIntersectingCellY,
                    out int minimumChannelSourceCellX,
                    out int minimumChannelSourceCellY,
                    out int maximumChannelSourceCellX,
                    out int maximumChannelSourceCellY))
            {
                return false;
            }

            // 枠を再利用した前回チャンクの値を残さず、成功時は必ず4096件すべてを今回の結果にします。
            for (int tileIndex = 0; tileIndex < WorldGridConstants.TilesPerChunk; tileIndex++)
            {
                surfaceWater[tileIndex] = default;
            }

            // 河道中心線は隣接2セルの担当範囲を通ります。
            // 正側末尾4タイルを所有する9個目のセルへ流入する辺も必要なため、担当セルのさらに外側まで再生成します。
            // 64件Buffer外かつチャンクと交差する正側17セルは、後続の湖判定でも使うため固定長リストへ保持します。
            FixedList4096Bytes<GeneratedHydrologyCell> haloIntersectingCells = default;
            for (long sourceCellLongY = minimumChannelSourceCellY;
                sourceCellLongY <= maximumChannelSourceCellY;
                sourceCellLongY++)
            {
                for (long sourceCellLongX = minimumChannelSourceCellX;
                    sourceCellLongX <= maximumChannelSourceCellX;
                    sourceCellLongX++)
                {
                    long sourceOffsetLongX = sourceCellLongX - firstCellX;
                    long sourceOffsetLongY = sourceCellLongY - firstCellY;
                    GeneratedHydrologyCell sourceCell;
                    bool isLocal = sourceOffsetLongX >= 0
                        && sourceOffsetLongX < DeterministicHydrologyGenerator.CellsPerChunkAxis
                        && sourceOffsetLongY >= 0
                        && sourceOffsetLongY < DeterministicHydrologyGenerator.CellsPerChunkAxis;
                    if (isLocal)
                    {
                        int sourceOffsetX = (int)sourceOffsetLongX;
                        int sourceOffsetY = (int)sourceOffsetLongY;
                        int cellIndex = (sourceOffsetY * DeterministicHydrologyGenerator.CellsPerChunkAxis)
                            + sourceOffsetX;
                        sourceCell = hydrologyCells[cellIndex];
                    }
                    else
                    {
                        if (!DeterministicHydrologyGenerator.TryGenerateCellWithoutLake(
                                settings,
                                (int)sourceCellLongX,
                                (int)sourceCellLongY,
                                frontier,
                                0,
                                out sourceCell))
                        {
                            return false;
                        }

                        bool intersectsChunk = sourceCellLongX >= minimumIntersectingCellX
                            && sourceCellLongX <= maximumIntersectingCellX
                            && sourceCellLongY >= minimumIntersectingCellY
                            && sourceCellLongY <= maximumIntersectingCellY;
                        if (intersectsChunk)
                        {
                            if (haloIntersectingCells.Length >= haloIntersectingCells.Capacity)
                            {
                                return false;
                            }

                            haloIntersectingCells.Add(sourceCell);
                        }
                    }

                    HydrologyConnectionFlags channel = sourceCell.ConnectionFlags
                        & (HydrologyConnectionFlags.Stream | HydrologyConnectionFlags.River);
                    if (channel == HydrologyConnectionFlags.None || !sourceCell.HasDownstream)
                    {
                        continue;
                    }

                    if (!TryRasterizeChannelWithinBounds(
                            settings,
                            sourceCell,
                            minimumTileX,
                            minimumTileY,
                            maximumTileX,
                            maximumTileY,
                            pathWorkspace,
                            pathTiles,
                            surfaceWater))
                    {
                        return false;
                    }
                }
            }

            // 1チャンクと交差する水文セルは8×8ではなく9×9です。
            // 正側外周は64件Bufferに含まれないため、保持した基礎水文へ湖状態を追加し、末端座標で同じ湖を一度だけ処理します。
            FixedList4096Bytes<long> processedLakeTerminalKeys = default;
            HydrologyLakeBasinCache haloLakeCache = default;
            for (long representativeCellLongY = minimumIntersectingCellY;
                representativeCellLongY <= maximumIntersectingCellY;
                representativeCellLongY++)
            {
                for (long representativeCellLongX = minimumIntersectingCellX;
                    representativeCellLongX <= maximumIntersectingCellX;
                    representativeCellLongX++)
                {
                    long representativeOffsetLongX = representativeCellLongX - firstCellX;
                    long representativeOffsetLongY = representativeCellLongY - firstCellY;
                    GeneratedHydrologyCell representative;
                    bool isLocal = representativeOffsetLongX >= 0
                        && representativeOffsetLongX < DeterministicHydrologyGenerator.CellsPerChunkAxis
                        && representativeOffsetLongY >= 0
                        && representativeOffsetLongY < DeterministicHydrologyGenerator.CellsPerChunkAxis;
                    if (isLocal)
                    {
                        int representativeOffsetX = (int)representativeOffsetLongX;
                        int representativeOffsetY = (int)representativeOffsetLongY;
                        int representativeIndex =
                            (representativeOffsetY * DeterministicHydrologyGenerator.CellsPerChunkAxis)
                            + representativeOffsetX;
                        representative = hydrologyCells[representativeIndex];
                    }
                    else
                    {
                        // 河道走査で生成済みの基礎水文を再利用し、湖状態だけを追加します。
                        // これにより正側17セルの高コストな下流・集水探索を同一チャンク内で二重実行しません。
                        if (!TryFindHydrologyCell(
                                haloIntersectingCells,
                                (int)representativeCellLongX,
                                (int)representativeCellLongY,
                                out representative)
                            || !DeterministicHydrologyLakeResolver.TryApplyLakeState(
                                settings,
                                ref representative,
                                frontier,
                                0,
                                basinMembers,
                                0,
                                basinLookup,
                                0,
                                ref haloLakeCache))
                        {
                            return false;
                        }
                    }

                    if ((representative.LakeFlags & HydrologyLakeFlags.FixedLake) == 0
                        || ContainsLakeTerminal(processedLakeTerminalKeys, representative))
                    {
                        continue;
                    }

                    if (processedLakeTerminalKeys.Length >= processedLakeTerminalKeys.Capacity)
                    {
                        return false;
                    }
                    processedLakeTerminalKeys.Add(PackCellCoordinate(
                        representative.DownstreamReachCellX,
                        representative.DownstreamReachCellY));

                    if (!DeterministicHydrologyLakeResolver.TryBuildBasin(
                            settings,
                            representative.DownstreamReachCellX,
                            representative.DownstreamReachCellY,
                            frontier,
                            0,
                            basinMembers,
                            0,
                            basinLookup,
                            0,
                            out HydrologyLakeBasinResult basin)
                        || !basin.HasFixedLake
                        || basin.LakeSurfaceHeight != representative.LakeSurfaceHeight
                        || basin.LakeFillFraction != representative.LakeFillFraction
                        || !TryFillLakeCandidate(
                            settings,
                            basin,
                            basinMembers,
                            basinLookup,
                            floodVisited,
                            floodQueue,
                            minimumTileX,
                            minimumTileY,
                            maximumTileX,
                            maximumTileY,
                            surfaceWater))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 1本のStreamまたはRiver中心線と固定幅だけを、指定チャンクの既存出力へ重ねます。
        /// C4-A2の幅規則をChunk Jobと純粋計算テストで共有し、湖面候補や他の水文セルは生成しません。
        /// </summary>
        /// <param name="settings">Anchor、経路、海面、連続標高を固定する生成設定です。</param>
        /// <param name="sourceCell">D8下流辺とStreamまたはRiverを持つ上流水文セルです。</param>
        /// <param name="chunkX">出力先の水平論理チャンク座標です。</param>
        /// <param name="chunkY">出力先の垂直論理チャンク座標です。</param>
        /// <param name="pathWorkspace">最大16×16経路探索へ貸す256件以上の作業配列です。</param>
        /// <param name="pathTiles">中心線を一時保持する256件以上の配列です。</param>
        /// <param name="surfaceWater">候補を重ねる行優先4096件以上の出力配列です。呼出し前の値は維持されます。</param>
        /// <returns>入力と経路が有効で、対象チャンク内の全候補を評価できた場合はtrueです。</returns>
        internal static bool TryRasterizeChannel(
            in WorldGenerationSettings settings,
            in GeneratedHydrologyCell sourceCell,
            int chunkX,
            int chunkY,
            NativeArray<SurfaceWaterPathSearchNode> pathWorkspace,
            NativeArray<SurfaceWaterPathTile> pathTiles,
            NativeArray<GeneratedSurfaceWaterTile> surfaceWater)
        {
            if (!surfaceWater.IsCreated || surfaceWater.Length < WorldGridConstants.TilesPerChunk
                || !pathWorkspace.IsCreated
                || pathWorkspace.Length < DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount
                || !pathTiles.IsCreated
                || pathTiles.Length < DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount
                || !TryGetChunkBounds(chunkX, chunkY, out int minimumTileX, out int minimumTileY,
                    out int maximumTileX, out int maximumTileY))
            {
                return false;
            }

            return TryRasterizeChannelWithinBounds(
                settings,
                sourceCell,
                minimumTileX,
                minimumTileY,
                maximumTileX,
                maximumTileY,
                pathWorkspace,
                pathTiles,
                surfaceWater);
        }

        /// <summary>検証済みチャンク境界内へ1本の河道中心線と上下左右のRiver幅を適用します。</summary>
        /// <param name="settings">経路探索と標高判定へ使う固定設定です。</param>
        /// <param name="sourceCell">D8辺、流出量、Stream・River分類を持つ上流水文セルです。</param>
        /// <param name="minimumTileX">対象チャンクの左端タイルXです。</param>
        /// <param name="minimumTileY">対象チャンクの下端タイルYです。</param>
        /// <param name="maximumTileX">対象チャンクの右端タイルXです。</param>
        /// <param name="maximumTileY">対象チャンクの上端タイルYです。</param>
        /// <param name="pathWorkspace">経路費用と直前位置を保持する256件以上の作業配列です。</param>
        /// <param name="pathTiles">開始から終了までの中心線を保持する256件以上の配列です。</param>
        /// <param name="surfaceWater">優先順位を適用して河道候補を書き込む4096件以上の配列です。</param>
        /// <returns>中心線生成と全幅候補の標高評価を完了できた場合はtrueです。</returns>
        private static bool TryRasterizeChannelWithinBounds(
            in WorldGenerationSettings settings,
            in GeneratedHydrologyCell sourceCell,
            int minimumTileX,
            int minimumTileY,
            int maximumTileX,
            int maximumTileY,
            NativeArray<SurfaceWaterPathSearchNode> pathWorkspace,
            NativeArray<SurfaceWaterPathTile> pathTiles,
            NativeArray<GeneratedSurfaceWaterTile> surfaceWater)
        {
            if (!DeterministicSurfaceWaterPathGenerator.TryBuildChannelCenterLine(
                    settings,
                    sourceCell,
                    pathWorkspace,
                    pathTiles,
                    out int pathLength))
            {
                return false;
            }

            bool isRiver = (sourceCell.ConnectionFlags & HydrologyConnectionFlags.River) != 0;
            for (int pathIndex = 0; pathIndex < pathLength; pathIndex++)
            {
                SurfaceWaterPathTile pathTile = pathTiles[pathIndex];
                if (!TryApplyChannelTile(
                        settings,
                        pathTile.WorldTileX,
                        pathTile.WorldTileY,
                        true,
                        isRiver,
                        sourceCell,
                        minimumTileX,
                        minimumTileY,
                        maximumTileX,
                        maximumTileY,
                        surfaceWater))
                {
                    return false;
                }

                if (!isRiver)
                {
                    continue;
                }

                // 河川幅は上下左右だけへ1タイル広げます。斜め角は別の中心線が通らない限り埋めません。
                for (int direction = 0; direction < 4; direction++)
                {
                    int offsetX = direction == 0 ? -RiverOrthogonalRadius
                        : direction == 1 ? RiverOrthogonalRadius : 0;
                    int offsetY = direction == 2 ? -RiverOrthogonalRadius
                        : direction == 3 ? RiverOrthogonalRadius : 0;
                    if (!TryAdd(pathTile.WorldTileX, offsetX, out int widenedX)
                        || !TryAdd(pathTile.WorldTileY, offsetY, out int widenedY)
                        || !TryApplyChannelTile(
                            settings,
                            widenedX,
                            widenedY,
                            false,
                            true,
                            sourceCell,
                            minimumTileX,
                            minimumTileY,
                            maximumTileX,
                            maximumTileY,
                            surfaceWater))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>河道候補1タイルを海面・チャンク範囲・重複優先順位で評価して出力へ反映します。</summary>
        /// <param name="settings">連続標高と海面を再現する固定設定です。</param>
        /// <param name="worldTileX">候補の水平絶対タイル座標です。</param>
        /// <param name="worldTileY">候補の垂直絶対タイル座標です。</param>
        /// <param name="isCenterLine">C4-A1の中心線そのものならtrueです。</param>
        /// <param name="isRiver">River幅として扱う場合はtrue、Stream中心線ならfalseです。</param>
        /// <param name="sourceCell">種類、流出量、代表供給元座標を持つ上流水文セルです。</param>
        /// <param name="minimumTileX">対象チャンクの左端タイルXです。</param>
        /// <param name="minimumTileY">対象チャンクの下端タイルYです。</param>
        /// <param name="maximumTileX">対象チャンクの右端タイルXです。</param>
        /// <param name="maximumTileY">対象チャンクの上端タイルYです。</param>
        /// <param name="surfaceWater">対象チャンクの行優先出力配列です。</param>
        /// <returns>候補が範囲外で無関係、または標高生成と優先適用を完了できた場合はtrueです。</returns>
        internal static bool TryApplyChannelTile(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            bool isCenterLine,
            bool isRiver,
            in GeneratedHydrologyCell sourceCell,
            int minimumTileX,
            int minimumTileY,
            int maximumTileX,
            int maximumTileY,
            NativeArray<GeneratedSurfaceWaterTile> surfaceWater)
        {
            if (worldTileX < minimumTileX || worldTileX > maximumTileX
                || worldTileY < minimumTileY || worldTileY > maximumTileY)
            {
                return true;
            }

            if (!DeterministicEnvironmentGenerator.TryGenerateBaseClimate(
                    settings,
                    worldTileX,
                    worldTileY,
                    out GeneratedEnvironmentData environment))
            {
                return false;
            }

            // 海面未満は既存の海水域を維持し、河道の淡水候補で上書きしません。
            if (environment.Elevation < settings.SeaLevelThreshold)
            {
                return true;
            }

            int localX = worldTileX - minimumTileX;
            int localY = worldTileY - minimumTileY;
            int tileIndex = WorldGrid.ToTileIndex(localX, localY);
            GeneratedSurfaceWaterTile existing = surfaceWater[tileIndex];
            SurfaceWaterTileType candidateType = isRiver
                ? SurfaceWaterTileType.River
                : SurfaceWaterTileType.Stream;
            bool existingIsChannel = existing.Type == SurfaceWaterTileType.Stream
                || existing.Type == SurfaceWaterTileType.River;
            if (!ShouldReplaceChannel(existing, candidateType, sourceCell))
            {
                // 中心線は代表河道の種類・供給元とは独立した属性です。
                // 弱い別河道の中心線が既存の強い河道幅と交差しても、代表値を変えず中心線情報だけを残します。
                if (isCenterLine && existingIsChannel)
                {
                    existing.Flags |= SurfaceWaterTileFlags.ChannelCenterLine;
                    surfaceWater[tileIndex] = existing;
                }
                return true;
            }

            surfaceWater[tileIndex] = new GeneratedSurfaceWaterTile
            {
                Type = candidateType,
                Flags = SurfaceWaterTileFlags.RechargeCandidate
                    | SurfaceWaterTileFlags.NaturalSurfaceWater
                    | SurfaceWaterTileFlags.FreshwaterConnected
                    // 既存中心線を別河道の幅が代表として置き換えても、交差した事実は失わせません。
                    | (existingIsChannel
                        ? existing.Flags & SurfaceWaterTileFlags.ChannelCenterLine
                        : SurfaceWaterTileFlags.None)
                    | (isCenterLine
                        ? SurfaceWaterTileFlags.ChannelCenterLine
                        : SurfaceWaterTileFlags.None),
                SurfaceHeight = environment.Elevation,
                SourceAmount = sourceCell.RunoffEquivalentCells,
                SourceCellX = sourceCell.CellX,
                SourceCellY = sourceCell.CellY
            };
            return true;
        }

        /// <summary>複数河道の重なりをRiver、流出量、安定した水文セルY・Xの順で解決します。</summary>
        /// <param name="existing">現在このタイルを代表している表流水です。</param>
        /// <param name="candidateType">新しいStreamまたはRiver候補です。</param>
        /// <param name="candidateSource">新候補の流出量と絶対水文座標です。</param>
        /// <returns>新候補を代表として上書きする場合はtrueです。</returns>
        private static bool ShouldReplaceChannel(
            in GeneratedSurfaceWaterTile existing,
            SurfaceWaterTileType candidateType,
            in GeneratedHydrologyCell candidateSource)
        {
            if (existing.Type == SurfaceWaterTileType.None)
            {
                return true;
            }
            if (existing.Type == SurfaceWaterTileType.LakeCandidate)
            {
                return false;
            }
            if (candidateType != existing.Type)
            {
                return candidateType == SurfaceWaterTileType.River;
            }
            if (candidateSource.RunoffEquivalentCells != existing.SourceAmount)
            {
                return candidateSource.RunoffEquivalentCells > existing.SourceAmount;
            }
            if (candidateSource.CellY != existing.SourceCellY)
            {
                return candidateSource.CellY < existing.SourceCellY;
            }
            return candidateSource.CellX < existing.SourceCellX;
        }

        /// <summary>同じ閉鎖末端を持つ固定湖が今回の9×9走査ですでに処理済みかを調べます。</summary>
        /// <param name="processedTerminalKeys">処理済み閉鎖末端のX・Yを詰めた固定長リストです。</param>
        /// <param name="representative">現在調べている固定湖セルです。</param>
        /// <returns>同じ閉鎖末端が前方の交差セルで処理済みならtrueです。</returns>
        private static bool ContainsLakeTerminal(
            in FixedList4096Bytes<long> processedTerminalKeys,
            in GeneratedHydrologyCell representative)
        {
            long terminalKey = PackCellCoordinate(
                representative.DownstreamReachCellX,
                representative.DownstreamReachCellY);
            for (int index = 0; index < processedTerminalKeys.Length; index++)
            {
                if (processedTerminalKeys[index] == terminalKey)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>河道走査時に保持した正側外周セルから、指定した絶対水文セルを取得します。</summary>
        /// <param name="cells">チャンクと交差するが64件Buffer外にある、最大17件の基礎水文セルです。</param>
        /// <param name="cellX">取得する水平絶対水文セル座標です。</param>
        /// <param name="cellY">取得する垂直絶対水文セル座標です。</param>
        /// <param name="cell">成功時に、湖状態をまだ適用していない基礎水文セルを受け取ります。</param>
        /// <returns>同じX・Yのセルを固定長リスト内で発見できた場合はtrueです。</returns>
        private static bool TryFindHydrologyCell(
            in FixedList4096Bytes<GeneratedHydrologyCell> cells,
            int cellX,
            int cellY,
            out GeneratedHydrologyCell cell)
        {
            for (int index = 0; index < cells.Length; index++)
            {
                GeneratedHydrologyCell candidate = cells[index];
                if (candidate.CellX == cellX && candidate.CellY == cellY)
                {
                    cell = candidate;
                    return true;
                }
            }

            cell = default;
            return false;
        }

        /// <summary>符号付き水文セルX・Yを、衝突しない単一の64bit比較値へ詰めます。</summary>
        /// <param name="cellX">水平水文セル座標です。</param>
        /// <param name="cellY">垂直水文セル座標です。</param>
        /// <returns>Xを下位32bit、Yを上位32bitへ元のビット列のまま格納した値です。</returns>
        private static long PackCellCoordinate(int cellX, int cellY)
        {
            ulong packed = (uint)cellX | ((ulong)(uint)cellY << 32);
            return (long)packed;
        }

        /// <summary>流域末端の最低標高タイルから8近傍で到達できる湖面未満タイルだけを湖面候補へします。</summary>
        /// <param name="settings">標高、海面、水文格子を固定した生成設定です。</param>
        /// <param name="basin">流域面積、末端、湖面標高、充填率を持つ固定湖結果です。</param>
        /// <param name="basinMembers">先頭からBasinAreaCells件に流域セルを保持する作業配列です。</param>
        /// <param name="basinLookup">各流域座標からbasinMembers添字を検索する固定Hash表です。</param>
        /// <param name="visited">流域セル添字×64ローカルタイルの状態です。0は未確認、1は湖面条件外、2は到達済みを表します。</param>
        /// <param name="queue">訪問済みタイルを一度ずつ処理する固定長FIFOです。</param>
        /// <param name="minimumTileX">出力チャンクの左端タイルXです。</param>
        /// <param name="minimumTileY">出力チャンクの下端タイルYです。</param>
        /// <param name="maximumTileX">出力チャンクの右端タイルXです。</param>
        /// <param name="maximumTileY">出力チャンクの上端タイルYです。</param>
        /// <param name="surfaceWater">湖面候補を河道より優先して書く4096件の出力です。</param>
        /// <returns>開始点と全到達タイルを固定上限内で処理できた場合はtrueです。</returns>
        internal static bool TryFillLakeCandidate(
            in WorldGenerationSettings settings,
            in HydrologyLakeBasinResult basin,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            NativeArray<byte> visited,
            NativeArray<int> queue,
            int minimumTileX,
            int minimumTileY,
            int maximumTileX,
            int maximumTileY,
            NativeArray<GeneratedSurfaceWaterTile> surfaceWater)
        {
            int memberCount = (int)basin.BasinAreaCells;
            int tileCapacity = memberCount * TilesPerHydrologyCellAxis * TilesPerHydrologyCellAxis;
            if (memberCount <= 0 || memberCount > DeterministicHydrologyLakeResolver.MaximumBasinMembers
                || tileCapacity > MaximumFloodTileCount
                || !DeterministicSurfaceWaterPathGenerator.TryFindChannelAnchor(
                    settings,
                    basin.SinkCellX,
                    basin.SinkCellY,
                    out SurfaceWaterPathTile start,
                    out _)
                || !TryGetBasinTileIndex(
                    settings.Hydrology,
                    start.WorldTileX,
                    start.WorldTileY,
                    basinLookup,
                    out int startIndex)
                || startIndex < 0 || startIndex >= tileCapacity
                || !TryIsEligibleLakeTile(
                    settings,
                    start.WorldTileX,
                    start.WorldTileY,
                    basin.LakeSurfaceHeight,
                    out bool isStartEligible)
                || !isStartEligible)
            {
                return false;
            }

            int remainingLocalCandidates = 0;
            for (int localTileY = 0; localTileY < WorldGridConstants.LogicalChunkSize; localTileY++)
            {
                // 絶対座標を終了条件にすると、右端がint.MaxValueのチャンクで++が折り返します。
                // 必ず0〜63のローカル添字を走査し、検証済みのチャンク左下へ足して端点でも有限にします。
                int worldTileY = (int)((long)minimumTileY + localTileY);
                for (int localTileX = 0; localTileX < WorldGridConstants.LogicalChunkSize; localTileX++)
                {
                    int worldTileX = (int)((long)minimumTileX + localTileX);
                    if (!TryGetBasinTileIndex(
                            settings.Hydrology,
                            worldTileX,
                            worldTileY,
                            basinLookup,
                            out _))
                    {
                        continue;
                    }
                    if (!TryIsEligibleLakeTile(
                            settings,
                            worldTileX,
                            worldTileY,
                            basin.LakeSurfaceHeight,
                            out bool isEligible))
                    {
                        return false;
                    }
                    if (isEligible)
                    {
                        remainingLocalCandidates++;
                    }
                }
            }

            // 対象チャンクに湖面未満タイルがなければ、開始点の有効性だけを確認して全流域走査を省略できます。
            if (remainingLocalCandidates == 0)
            {
                return true;
            }

            for (int index = 0; index < tileCapacity; index++)
            {
                visited[index] = 0;
            }

            int readIndex = 0;
            int writeIndex = 0;
            // 1は湖面条件外、2は到達済みとして使い、条件外タイルの高価な連続標高生成も一度に限定します。
            visited[startIndex] = 2;
            queue[writeIndex++] = startIndex;
            while (readIndex < writeIndex)
            {
                int basinTileIndex = queue[readIndex++];
                if (!TryDecodeBasinTile(
                        basinMembers,
                        basinTileIndex,
                        out int worldTileX,
                        out int worldTileY))
                {
                    return false;
                }

                if (worldTileX >= minimumTileX && worldTileX <= maximumTileX
                    && worldTileY >= minimumTileY && worldTileY <= maximumTileY)
                {
                    int outputIndex = WorldGrid.ToTileIndex(
                        worldTileX - minimumTileX,
                        worldTileY - minimumTileY);
                    surfaceWater[outputIndex] = new GeneratedSurfaceWaterTile
                    {
                        Type = SurfaceWaterTileType.LakeCandidate,
                        Flags = SurfaceWaterTileFlags.RechargeCandidate,
                        SurfaceHeight = basin.LakeSurfaceHeight,
                        SourceAmount = basin.LakeFillFraction,
                        SourceCellX = basin.SinkCellX,
                        SourceCellY = basin.SinkCellY
                    };
                    remainingLocalCandidates--;
                    // 出力対象の全候補へ到達済みなら、別チャンクだけに属する残りの流域を走査する必要はありません。
                    if (remainingLocalCandidates == 0)
                    {
                        return true;
                    }
                }

                for (int offsetY = -1; offsetY <= 1; offsetY++)
                {
                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        if ((offsetX == 0 && offsetY == 0)
                            || !TryAdd(worldTileX, offsetX, out int neighborX)
                            || !TryAdd(worldTileY, offsetY, out int neighborY)
                            || !TryGetBasinTileIndex(
                                settings.Hydrology,
                                neighborX,
                                neighborY,
                                basinLookup,
                                out int neighborIndex))
                        {
                            continue;
                        }

                        if (neighborIndex < 0 || neighborIndex >= tileCapacity)
                        {
                            return false;
                        }
                        if (visited[neighborIndex] != 0)
                        {
                            continue;
                        }
                        if (!TryIsEligibleLakeTile(
                                settings,
                                neighborX,
                                neighborY,
                                basin.LakeSurfaceHeight,
                                out bool isEligible))
                        {
                            return false;
                        }
                        if (!isEligible)
                        {
                            visited[neighborIndex] = 1;
                            continue;
                        }
                        if (writeIndex >= tileCapacity)
                        {
                            return false;
                        }

                        visited[neighborIndex] = 2;
                        queue[writeIndex++] = neighborIndex;
                    }
                }
            }

            return true;
        }

        /// <summary>絶対タイル座標を流域メンバー添字×64のFlood Fill添字へ変換します。</summary>
        /// <param name="settings">担当セル変換に使う8タイル水文設定です。</param>
        /// <param name="worldTileX">水平絶対タイル座標です。</param>
        /// <param name="worldTileY">垂直絶対タイル座標です。</param>
        /// <param name="lookup">流域座標とメンバー添字を保持する固定Hash表です。</param>
        /// <param name="basinTileIndex">成功時の0〜262,143の作業添字です。</param>
        /// <returns>タイルの担当水文セルが同じ閉鎖流域へ含まれる場合はtrueです。</returns>
        private static bool TryGetBasinTileIndex(
            in HydrologyGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            NativeArray<HydrologyBasinLookupSlot> lookup,
            out int basinTileIndex)
        {
            basinTileIndex = default;
            if (!DeterministicHydrologyGenerator.TryGetCellCoordinate(
                    settings,
                    worldTileX,
                    worldTileY,
                    out int cellX,
                    out int cellY)
                || !DeterministicHydrologyLakeResolver.TryFindMember(
                    lookup,
                    0,
                    cellX,
                    cellY,
                    out _,
                    out int memberIndex))
            {
                return false;
            }

            long centerX = (long)cellX * TilesPerHydrologyCellAxis;
            long centerY = (long)cellY * TilesPerHydrologyCellAxis;
            long localX = (long)worldTileX - centerX - ResponsibilityMinimumOffset;
            long localY = (long)worldTileY - centerY - ResponsibilityMinimumOffset;
            if (localX < 0 || localX >= TilesPerHydrologyCellAxis
                || localY < 0 || localY >= TilesPerHydrologyCellAxis)
            {
                return false;
            }

            basinTileIndex = (memberIndex * TilesPerHydrologyCellAxis * TilesPerHydrologyCellAxis)
                + ((int)localY * TilesPerHydrologyCellAxis)
                + (int)localX;
            return true;
        }

        /// <summary>Flood Fill添字を流域セルの中心-4〜+3に対応する絶対タイル座標へ戻します。</summary>
        /// <param name="members">先頭から流域セルを保持する作業配列です。</param>
        /// <param name="basinTileIndex">メンバー添字×64とセル内添字を合成した値です。</param>
        /// <param name="worldTileX">成功時の水平絶対タイル座標です。</param>
        /// <param name="worldTileY">成功時の垂直絶対タイル座標です。</param>
        /// <returns>乗算と加算がint範囲内ならtrueです。</returns>
        private static bool TryDecodeBasinTile(
            NativeArray<HydrologyUpstreamCandidate> members,
            int basinTileIndex,
            out int worldTileX,
            out int worldTileY)
        {
            int memberIndex = basinTileIndex / 64;
            int localIndex = basinTileIndex % 64;
            HydrologyUpstreamCandidate member = members[memberIndex];
            long tileX = ((long)member.CellX * TilesPerHydrologyCellAxis)
                + ResponsibilityMinimumOffset
                + (localIndex % TilesPerHydrologyCellAxis);
            long tileY = ((long)member.CellY * TilesPerHydrologyCellAxis)
                + ResponsibilityMinimumOffset
                + (localIndex / TilesPerHydrologyCellAxis);
            if (tileX < int.MinValue || tileX > int.MaxValue
                || tileY < int.MinValue || tileY > int.MaxValue)
            {
                worldTileX = default;
                worldTileY = default;
                return false;
            }

            worldTileX = (int)tileX;
            worldTileY = (int)tileY;
            return true;
        }

        /// <summary>元地形を生成し、湖面未満かつ海面以上ならFlood Fillの通過対象として返します。</summary>
        /// <param name="settings">連続標高と海面閾値を生成する固定設定です。</param>
        /// <param name="worldTileX">水平絶対タイル座標です。</param>
        /// <param name="worldTileY">垂直絶対タイル座標です。</param>
        /// <param name="lakeSurfaceHeight">未満比較する固定湖面標高です。</param>
        /// <param name="isEligible">生成成功時に、海面以上かつ湖面未満ならtrueです。</param>
        /// <returns>元地形の生成に成功した場合はtrueです。湖面条件外だけでは失敗にしません。</returns>
        private static bool TryIsEligibleLakeTile(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            ushort lakeSurfaceHeight,
            out bool isEligible)
        {
            if (!DeterministicEnvironmentGenerator.TryGenerateBaseClimate(
                    settings,
                    worldTileX,
                    worldTileY,
                    out GeneratedEnvironmentData environment))
            {
                isEligible = false;
                return false;
            }

            isEligible = environment.Elevation >= settings.SeaLevelThreshold
                && environment.Elevation < lakeSurfaceHeight;
            return true;
        }

        /// <summary>論理チャンクの64×64絶対タイル境界を64bit中間計算から安全に求めます。</summary>
        /// <param name="chunkX">水平論理チャンク座標です。</param>
        /// <param name="chunkY">垂直論理チャンク座標です。</param>
        /// <param name="minimumX">成功時の左端タイルXです。</param>
        /// <param name="minimumY">成功時の下端タイルYです。</param>
        /// <param name="maximumX">成功時の右端タイルXです。</param>
        /// <param name="maximumY">成功時の上端タイルYです。</param>
        /// <returns>四端がint範囲内ならtrueです。</returns>
        private static bool TryGetChunkBounds(
            int chunkX,
            int chunkY,
            out int minimumX,
            out int minimumY,
            out int maximumX,
            out int maximumY)
        {
            long minimumLongX = (long)chunkX * WorldGridConstants.LogicalChunkSize;
            long minimumLongY = (long)chunkY * WorldGridConstants.LogicalChunkSize;
            long maximumLongX = minimumLongX + WorldGridConstants.LogicalChunkSize - 1L;
            long maximumLongY = minimumLongY + WorldGridConstants.LogicalChunkSize - 1L;
            if (minimumLongX < int.MinValue || minimumLongY < int.MinValue
                || maximumLongX > int.MaxValue || maximumLongY > int.MaxValue)
            {
                minimumX = default;
                minimumY = default;
                maximumX = default;
                maximumY = default;
                return false;
            }

            minimumX = (int)minimumLongX;
            minimumY = (int)minimumLongY;
            maximumX = (int)maximumLongX;
            maximumY = (int)maximumLongY;
            return true;
        }

        /// <summary>64件の入力Buffer先頭に対応する水文セル座標を64bit中間計算から求めます。</summary>
        /// <param name="chunkX">水平論理チャンク座標です。</param>
        /// <param name="chunkY">垂直論理チャンク座標です。</param>
        /// <param name="firstCellX">成功時の左端水文セルXです。</param>
        /// <param name="firstCellY">成功時の下端水文セルYです。</param>
        /// <returns>8倍した先頭座標とBuffer末尾座標がint範囲内ならtrueです。</returns>
        private static bool TryGetHydrologyBufferOrigin(
            int chunkX,
            int chunkY,
            out int firstCellX,
            out int firstCellY)
        {
            long firstLongX = (long)chunkX * DeterministicHydrologyGenerator.CellsPerChunkAxis;
            long firstLongY = (long)chunkY * DeterministicHydrologyGenerator.CellsPerChunkAxis;
            long bufferLastX = firstLongX + DeterministicHydrologyGenerator.CellsPerChunkAxis - 1L;
            long bufferLastY = firstLongY + DeterministicHydrologyGenerator.CellsPerChunkAxis - 1L;
            if (firstLongX < int.MinValue || bufferLastX > int.MaxValue
                || firstLongY < int.MinValue || bufferLastY > int.MaxValue)
            {
                firstCellX = default;
                firstCellY = default;
                return false;
            }

            firstCellX = (int)firstLongX;
            firstCellY = (int)firstLongY;
            return true;
        }

        /// <summary>
        /// タイル境界を正として、交差する担当水文セルと、その担当範囲へ流入できる河道供給元の範囲を求めます。
        /// </summary>
        /// <param name="settings">中心-4〜+3の担当変換に使う水文設定です。</param>
        /// <param name="minimumTileX">対象チャンクの左端タイルXです。</param>
        /// <param name="minimumTileY">対象チャンクの下端タイルYです。</param>
        /// <param name="maximumTileX">対象チャンクの右端タイルXです。</param>
        /// <param name="maximumTileY">対象チャンクの上端タイルYです。</param>
        /// <param name="minimumIntersectingCellX">成功時に左端タイルを担当する水文セルXです。</param>
        /// <param name="minimumIntersectingCellY">成功時に下端タイルを担当する水文セルYです。</param>
        /// <param name="maximumIntersectingCellX">成功時に右端タイルを担当する水文セルXです。</param>
        /// <param name="maximumIntersectingCellY">成功時に上端タイルを担当する水文セルYです。</param>
        /// <param name="minimumChannelSourceCellX">成功時に河道供給元として調べる最小セルXです。</param>
        /// <param name="minimumChannelSourceCellY">成功時に河道供給元として調べる最小セルYです。</param>
        /// <param name="maximumChannelSourceCellX">成功時に河道供給元として調べる最大セルXです。</param>
        /// <param name="maximumChannelSourceCellY">成功時に河道供給元として調べる最大セルYです。</param>
        /// <returns>担当セル変換と、その外側1セルの座標がint範囲内ならtrueです。</returns>
        internal static bool TryGetHydrologyScanBounds(
            in HydrologyGenerationSettings settings,
            int minimumTileX,
            int minimumTileY,
            int maximumTileX,
            int maximumTileY,
            out int minimumIntersectingCellX,
            out int minimumIntersectingCellY,
            out int maximumIntersectingCellX,
            out int maximumIntersectingCellY,
            out int minimumChannelSourceCellX,
            out int minimumChannelSourceCellY,
            out int maximumChannelSourceCellX,
            out int maximumChannelSourceCellY)
        {
            if (!DeterministicHydrologyGenerator.TryGetCellCoordinate(
                    settings,
                    minimumTileX,
                    minimumTileY,
                    out minimumIntersectingCellX,
                    out minimumIntersectingCellY)
                || !DeterministicHydrologyGenerator.TryGetCellCoordinate(
                    settings,
                    maximumTileX,
                    maximumTileY,
                    out maximumIntersectingCellX,
                    out maximumIntersectingCellY)
                || !TryAdd(minimumIntersectingCellX, -1, out minimumChannelSourceCellX)
                || !TryAdd(minimumIntersectingCellY, -1, out minimumChannelSourceCellY)
                || !TryAdd(maximumIntersectingCellX, 1, out maximumChannelSourceCellX)
                || !TryAdd(maximumIntersectingCellY, 1, out maximumChannelSourceCellY))
            {
                minimumIntersectingCellX = default;
                minimumIntersectingCellY = default;
                maximumIntersectingCellX = default;
                maximumIntersectingCellY = default;
                minimumChannelSourceCellX = default;
                minimumChannelSourceCellY = default;
                maximumChannelSourceCellX = default;
                maximumChannelSourceCellY = default;
                return false;
            }

            return true;
        }

        /// <summary>小さい固定オフセットをint座標へ加える前に64bitで範囲を検証します。</summary>
        /// <param name="value">基準となる絶対座標です。</param>
        /// <param name="offset">-1、0、+1の近傍オフセットです。</param>
        /// <param name="result">成功時の加算結果です。</param>
        /// <returns>加算結果がint範囲内ならtrueです。</returns>
        private static bool TryAdd(int value, int offset, out int result)
        {
            long sum = (long)value + offset;
            if (sum < int.MinValue || sum > int.MaxValue)
            {
                result = default;
                return false;
            }
            result = (int)sum;
            return true;
        }

        /// <summary>固定表流水1要素を順序付き64bit状態ハッシュへ追加します。</summary>
        /// <param name="currentHash">直前要素までを反映したハッシュです。</param>
        /// <param name="tile">種類、フラグ、高さ、供給量、供給元座標を持つ表流水です。</param>
        /// <returns>全固定フィールドを追加したハッシュです。</returns>
        internal static ulong HashTile(ulong currentHash, in GeneratedSurfaceWaterTile tile)
        {
            currentHash = WorldSeedUtility.Mix64(currentHash ^ (byte)tile.Type);
            currentHash = WorldSeedUtility.Mix64(currentHash ^ (byte)tile.Flags);
            currentHash = WorldSeedUtility.Mix64(currentHash ^ tile.SurfaceHeight);
            currentHash = WorldSeedUtility.Mix64(currentHash ^ tile.SourceAmount);
            currentHash = WorldSeedUtility.Mix64(currentHash ^ (uint)tile.SourceCellX);
            return WorldSeedUtility.Mix64(currentHash ^ (uint)tile.SourceCellY);
        }
    }
}
