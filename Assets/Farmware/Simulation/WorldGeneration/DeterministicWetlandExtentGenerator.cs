// このファイルは、C4-Cまでの固定地形・表流水・地下水から自然冠水度と広域湿原の最低規模を決定論的に生成します。
// ChunkGenerationSystemはロード済み隣接Entityを参照せず、3×3チャンクの恒久淡水源と21×21水文キャッシュを使って4096件を一括公開します。

using Unity.Collections;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 半径32の冠水源、タイル資格16件、上下左右4セルの広域湿原規模を固定長配列だけで評価します。
    /// C4-D2の製品Jobと状態ハッシュが同じAPIを使い、ロード順やJob完了順による差を作りません。
    /// </summary>
    public static class DeterministicWetlandExtentGenerator
    {
        /// <summary>対象チャンクの周囲で再構築するチャンク半径です。固定設定として生成中は変更しません。</summary>
        public const int SourceChunkRadius = 1;

        /// <summary>冠水源を再構築する各軸のチャンク数です。3×3を固定し、生成中は変更しません。</summary>
        public const int SourceChunksPerAxis = (SourceChunkRadius * 2) + 1;

        /// <summary>中心を除く周囲チャンクの固定件数です。製品Jobではこの8件を有限並列化します。</summary>
        public const int SourceChunkLaneCount = (SourceChunksPerAxis * SourceChunksPerAxis) - 1;

        /// <summary>
        /// 周囲8チャンクを4件ずつ2波で処理するときの同時作業領域数です。
        /// 8件分の巨大Flood Fill配列を常駐させず、4 Workerまで利用できる折衷値です。
        /// </summary>
        public const int ParallelSourceWorkspaceLaneCount = 4;

        /// <summary>3チャンク分の冠水源矩形幅です。単位はタイルで、生成中は変更しません。</summary>
        public const int SourceAreaTilesPerAxis =
            SourceChunksPerAxis * WorldGridConstants.LogicalChunkSize;

        /// <summary>3×3チャンクの全表流水を保持する固定要素数です。生成枠の寿命中再利用します。</summary>
        public const int SourceAreaTileCount = SourceAreaTilesPerAxis * SourceAreaTilesPerAxis;

        /// <summary>対象64×64タイルが参照し得る各軸の水文セル数です。生成中は変更しません。</summary>
        public const int TargetOwnerCellsPerAxis =
            DeterministicGroundwaterHydrologyGenerator.IntersectingCellsPerAxis;

        /// <summary>4セル成分を証明するため対象外へ探索する最大距離です。単位は水文セルです。</summary>
        public const int ExtentSearchRadiusInCells = 3;

        /// <summary>湿原候補を評価する各軸の水文セル数です。対象9セルと両側3セルからなります。</summary>
        public const int EvaluationCellsPerAxis =
            TargetOwnerCellsPerAxis + (ExtentSearchRadiusInCells * 2);

        /// <summary>湿原候補と連結判定を保持する固定要素数です。生成枠の寿命中再利用します。</summary>
        public const int EvaluationCellCount = EvaluationCellsPerAxis * EvaluationCellsPerAxis;

        /// <summary>水文セル中心から担当8×8タイルの左下までの固定オフセットです。</summary>
        private const int ResponsibilityMinimumOffset = -4;

        /// <summary>一つの水文セルが担当する各軸のタイル数です。</summary>
        private const int TilesPerHydrologyCellAxis = 8;

        /// <summary>
        /// 完成済み中心チャンクと再構築した周囲8チャンクから、4096件の自然冠水・湿原結果を生成します。
        /// </summary>
        /// <param name="settings">自然冠水、地形、水文、地下水、湖区分を再現する固定設定です。</param>
        /// <param name="chunkX">対象の水平論理チャンク座標です。</param>
        /// <param name="chunkY">対象の垂直論理チャンク座標です。</param>
        /// <param name="terrainTiles">完成済み中心チャンクの4096地形タイルです。</param>
        /// <param name="hydrologyTiles">自然飽和を保持し、成功時に自然冠水と湿原bitを追加する4096件です。</param>
        /// <param name="centerSurfaceWater">湖・泉まで確定した中心チャンクの4096表流水です。</param>
        /// <param name="springHydrologyCells">地下水と湖区分が確定した中心周囲21×21水文セルです。</param>
        /// <param name="frontier">周辺チャンクの集水探索へ再利用する固定長Heapです。</param>
        /// <param name="basinMembers">周辺チャンクの閉鎖流域を保持する固定長配列です。</param>
        /// <param name="basinLookup">周辺チャンクの流域所属を検索する固定Hash表です。</param>
        /// <param name="pathWorkspace">周辺河道の経路探索へ再利用する256件配列です。</param>
        /// <param name="pathTiles">一つの周辺河道を保持する256件配列です。</param>
        /// <param name="floodVisited">周辺湖Flood Fillの訪問状態を保持する固定長配列です。</param>
        /// <param name="floodQueue">周辺湖Flood Fillの待ち行列として使う固定長配列です。</param>
        /// <param name="scratchHydrologyCells">周辺1チャンクの64水文セルを順次上書きする作業配列です。</param>
        /// <param name="scratchIntersectingHydrologyCells">周辺1チャンクと交差する9×9水文を保持する作業配列です。</param>
        /// <param name="scratchSurfaceRechargeCandidates">周辺9×9水文セルの自然表流水涵養有無を保持します。</param>
        /// <param name="scratchSurfaceWater">周辺1チャンクの4096表流水を順次上書きする作業配列です。</param>
        /// <param name="sourceWaterArea">成功時に3×3チャンクの表流水を行優先で保持する作業配列です。</param>
        /// <param name="evaluationHydrologyCells">15×15湿原候補に必要な地下水・湖区分完成セルです。</param>
        /// <param name="evaluationHydrologyCellWritten">15×15全セルが再構築済みかを0／1で保持します。</param>
        /// <param name="candidateCells">15×15水文セルの資格有無を保持する作業配列です。</param>
        /// <param name="largeExtentCells">対象セルが4セル以上の成分へ属するかを保持する作業配列です。</param>
        /// <param name="visitedCells">各対象セルの有限幅優先探索で再利用する訪問配列です。</param>
        /// <param name="cellQueue">有限幅優先探索で再利用する水文セル添字Queueです。</param>
        /// <returns>周辺再構築と4096件の計算を完了できた場合はtrueです。false時の作業配列は公開しません。</returns>
        internal static bool TryGenerateChunk(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            NativeArray<GeneratedTile> terrainTiles,
            NativeArray<GeneratedHydrologyTile> hydrologyTiles,
            NativeArray<GeneratedSurfaceWaterTile> centerSurfaceWater,
            NativeArray<GeneratedHydrologyCell> springHydrologyCells,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            NativeArray<SurfaceWaterPathSearchNode> pathWorkspace,
            NativeArray<SurfaceWaterPathTile> pathTiles,
            NativeArray<byte> floodVisited,
            NativeArray<int> floodQueue,
            NativeArray<GeneratedHydrologyCell> scratchHydrologyCells,
            NativeArray<GeneratedHydrologyCell> scratchIntersectingHydrologyCells,
            NativeArray<SurfaceRechargeCandidate> scratchSurfaceRechargeCandidates,
            NativeArray<GeneratedSurfaceWaterTile> scratchSurfaceWater,
            NativeArray<GeneratedSurfaceWaterTile> sourceWaterArea,
            NativeArray<GeneratedHydrologyCell> evaluationHydrologyCells,
            NativeArray<byte> evaluationHydrologyCellWritten,
            NativeArray<byte> candidateCells,
            NativeArray<byte> largeExtentCells,
            NativeArray<byte> visitedCells,
            NativeArray<int> cellQueue)
        {
            if (!DeterministicNaturalFloodingGenerator.ValidateSettings(settings.NaturalFlooding)
                || !DeterministicEnvironmentGenerator.ValidateSettings(settings)
                || !DeterministicHydrologyGenerator.ValidateSettings(settings.Hydrology)
                || !DeterministicGroundwaterGenerator.ValidateSettings(settings.Groundwater)
                || !DeterministicLakeSpringGenerator.ValidateSettings(settings.LakeSpring)
                || !terrainTiles.IsCreated
                || terrainTiles.Length < WorldGridConstants.TilesPerChunk
                || !hydrologyTiles.IsCreated
                || hydrologyTiles.Length < WorldGridConstants.TilesPerChunk
                || !centerSurfaceWater.IsCreated
                || centerSurfaceWater.Length < WorldGridConstants.TilesPerChunk
                || !springHydrologyCells.IsCreated
                || springHydrologyCells.Length < DeterministicLakeSpringGenerator.SpringHydrologyCellCount
                || !scratchHydrologyCells.IsCreated
                || scratchHydrologyCells.Length < DeterministicHydrologyGenerator.CellsPerChunk
                || !scratchIntersectingHydrologyCells.IsCreated
                || scratchIntersectingHydrologyCells.Length
                    < DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount
                || !scratchSurfaceRechargeCandidates.IsCreated
                || scratchSurfaceRechargeCandidates.Length
                    < DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount
                || !scratchSurfaceWater.IsCreated
                || scratchSurfaceWater.Length < WorldGridConstants.TilesPerChunk
                || !sourceWaterArea.IsCreated
                || sourceWaterArea.Length < SourceAreaTileCount
                || !evaluationHydrologyCells.IsCreated
                || evaluationHydrologyCells.Length < EvaluationCellCount
                || !evaluationHydrologyCellWritten.IsCreated
                || evaluationHydrologyCellWritten.Length < EvaluationCellCount
                || !candidateCells.IsCreated
                || candidateCells.Length < EvaluationCellCount
                || !largeExtentCells.IsCreated
                || largeExtentCells.Length < EvaluationCellCount
                || !visitedCells.IsCreated
                || visitedCells.Length < EvaluationCellCount
                || !cellQueue.IsCreated
                || cellQueue.Length < EvaluationCellCount
                || !TryGetOrigins(
                    chunkX,
                    chunkY,
                    out int targetMinimumTileX,
                    out int targetMinimumTileY,
                    out int sourceMinimumTileX,
                    out int sourceMinimumTileY,
                    out int firstCellX,
                    out int firstCellY))
            {
                return false;
            }

            int evaluationFirstCellX = firstCellX - ExtentSearchRadiusInCells;
            int evaluationFirstCellY = firstCellY - ExtentSearchRadiusInCells;

            if (!TryBuildSourceWaterArea(
                    settings,
                    chunkX,
                    chunkY,
                    centerSurfaceWater,
                    springHydrologyCells,
                    evaluationFirstCellX,
                    evaluationFirstCellY,
                    frontier,
                    basinMembers,
                    basinLookup,
                    pathWorkspace,
                    pathTiles,
                    floodVisited,
                    floodQueue,
                    scratchHydrologyCells,
                    scratchIntersectingHydrologyCells,
                    scratchSurfaceRechargeCandidates,
                    scratchSurfaceWater,
                    sourceWaterArea,
                    evaluationHydrologyCells,
                    evaluationHydrologyCellWritten))
            {
                return false;
            }

            return TryEvaluateChunk(
                settings,
                targetMinimumTileX,
                targetMinimumTileY,
                sourceMinimumTileX,
                sourceMinimumTileY,
                evaluationFirstCellX,
                evaluationFirstCellY,
                terrainTiles,
                hydrologyTiles,
                sourceWaterArea,
                evaluationHydrologyCells,
                evaluationHydrologyCellWritten,
                candidateCells,
                largeExtentCells,
                visitedCells,
                cellQueue);
        }

        /// <summary>
        /// 製品Jobの一担当として、中心を除く周囲8チャンクのうち1件を再構築します。
        /// 担当ごとに独立した作業配列を受け取り、共有配列では重ならない64×64領域だけへ書きます。
        /// </summary>
        /// <param name="settings">表流水、地下水、湖区分を再現する固定設定です。</param>
        /// <param name="chunkX">公開対象の水平論理チャンク座標です。</param>
        /// <param name="chunkY">公開対象の垂直論理チャンク座標です。</param>
        /// <param name="sourceLaneIndex">左下から行優先で中心を飛ばした0〜7の周辺担当番号です。</param>
        /// <param name="frontier">この担当だけが使う集水Heapです。</param>
        /// <param name="basinMembers">この担当だけが使う閉鎖流域配列です。</param>
        /// <param name="basinLookup">この担当だけが使う流域Hash表です。</param>
        /// <param name="pathWorkspace">この担当だけが使う河道探索配列です。</param>
        /// <param name="pathTiles">この担当だけが使う河道経路配列です。</param>
        /// <param name="floodVisited">この担当だけが使う湖Flood Fill訪問配列です。</param>
        /// <param name="floodQueue">この担当だけが使う湖Flood Fill待ち行列です。</param>
        /// <param name="scratchHydrologyCells">担当チャンクの64水文セル出力です。</param>
        /// <param name="scratchIntersectingHydrologyCells">後続最終Jobへ渡す交差9×9水文出力です。</param>
        /// <param name="scratchSurfaceRechargeCandidates">後続最終Jobへ渡す交差9×9涵養出力です。</param>
        /// <param name="scratchSurfaceWater">担当チャンクの4096表流水出力です。</param>
        /// <param name="sourceWaterArea">担当の64×64部分だけを書き込む共有192×192水源配列です。</param>
        /// <returns>担当チャンクを固定上限内で再構築し、共有水源へ配置できた場合はtrueです。</returns>
        internal static bool TryGenerateSourceChunkLane(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            int sourceLaneIndex,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            NativeArray<SurfaceWaterPathSearchNode> pathWorkspace,
            NativeArray<SurfaceWaterPathTile> pathTiles,
            NativeArray<byte> floodVisited,
            NativeArray<int> floodQueue,
            NativeArray<GeneratedHydrologyCell> scratchHydrologyCells,
            NativeArray<GeneratedHydrologyCell> scratchIntersectingHydrologyCells,
            NativeArray<SurfaceRechargeCandidate> scratchSurfaceRechargeCandidates,
            NativeArray<GeneratedSurfaceWaterTile> scratchSurfaceWater,
            NativeArray<GeneratedSurfaceWaterTile> sourceWaterArea)
        {
            if (sourceLaneIndex < 0 || sourceLaneIndex >= SourceChunkLaneCount
                || !sourceWaterArea.IsCreated || sourceWaterArea.Length < SourceAreaTileCount)
            {
                return false;
            }

            // 3×3の中央添字4を飛ばし、0〜7を周囲8座標へ一意に写像します。
            int sourceGridIndex = sourceLaneIndex < 4 ? sourceLaneIndex : sourceLaneIndex + 1;
            int chunkOffsetX = (sourceGridIndex % SourceChunksPerAxis) - SourceChunkRadius;
            int chunkOffsetY = (sourceGridIndex / SourceChunksPerAxis) - SourceChunkRadius;
            long sourceChunkX = (long)chunkX + chunkOffsetX;
            long sourceChunkY = (long)chunkY + chunkOffsetY;
            if (sourceChunkX < int.MinValue || sourceChunkX > int.MaxValue
                || sourceChunkY < int.MinValue || sourceChunkY > int.MaxValue
                || !DeterministicHydrologyGenerator.TryGenerateChunkCells(
                    settings,
                    (int)sourceChunkX,
                    (int)sourceChunkY,
                    scratchHydrologyCells,
                    frontier,
                    basinMembers,
                    basinLookup)
                || !DeterministicSurfaceWaterGenerator.TryGenerateChunk(
                    settings,
                    (int)sourceChunkX,
                    (int)sourceChunkY,
                    scratchHydrologyCells,
                    frontier,
                    basinMembers,
                    basinLookup,
                    pathWorkspace,
                    pathTiles,
                    floodVisited,
                    floodQueue,
                    scratchIntersectingHydrologyCells,
                    scratchSurfaceRechargeCandidates,
                    scratchSurfaceWater)
                || !DeterministicLakeSpringGenerator.TryFinalizeSurfaceWaterLakes(
                    settings,
                    scratchSurfaceWater,
                    frontier,
                    basinMembers,
                    basinLookup))
            {
                return false;
            }

            CopySourceChunkToArea(
                chunkOffsetX,
                chunkOffsetY,
                scratchSurfaceWater,
                sourceWaterArea);
            return true;
        }

        /// <summary>
        /// 8担当が再構築した周辺結果と中心の完成結果を結合し、4096件の自然冠水・湿原bitを確定します。
        /// 重なる交差9×9水文は固定担当順で最初の同一絶対座標だけを採用するため、Worker完了順に依存しません。
        /// </summary>
        /// <param name="settings">自然冠水、地形、水文、地下水、湖区分を再現する固定設定です。</param>
        /// <param name="chunkX">対象の水平論理チャンク座標です。</param>
        /// <param name="chunkY">対象の垂直論理チャンク座標です。</param>
        /// <param name="terrainTiles">完成済み中心チャンクの4096地形です。</param>
        /// <param name="hydrologyTiles">自然飽和を読み、自然冠水と湿原bitを追記する4096件です。</param>
        /// <param name="centerSurfaceWater">湖・泉まで確定した中心4096表流水です。</param>
        /// <param name="springHydrologyCells">中央9×9を再利用するC4-C完成水文です。</param>
        /// <param name="frontier">外周セルの最終湖区分へ順次再利用する集水Heapです。</param>
        /// <param name="basinMembers">外周セルの最終湖区分へ順次再利用する流域配列です。</param>
        /// <param name="basinLookup">外周セルの最終湖区分へ順次再利用する流域Hash表です。</param>
        /// <param name="sourceIntersectingHydrologyCells">8担当分を連結した交差9×9水文配列です。</param>
        /// <param name="sourceRechargeCandidates">8担当分を連結した交差9×9涵養配列です。</param>
        /// <param name="sourceWaterArea">周囲8担当が配置済みで、ここで中心を補う192×192水源配列です。</param>
        /// <param name="evaluationHydrologyCells">15×15の地下水・最終湖区分完成水文です。</param>
        /// <param name="evaluationHydrologyCellWritten">15×15各セルの書込完了を示す0／1配列です。</param>
        /// <param name="candidateCells">15×15の湿原候補セルです。</param>
        /// <param name="largeExtentCells">中央9×9の広域湿原成分結果です。</param>
        /// <param name="visitedCells">有限連結探索へ再利用する訪問配列です。</param>
        /// <param name="cellQueue">有限連結探索へ再利用するFIFOです。</param>
        /// <returns>全周辺結果を結合し、4096件を確定できた場合はtrueです。</returns>
        internal static bool TryFinalizeParallelChunk(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            NativeArray<GeneratedTile> terrainTiles,
            NativeArray<GeneratedHydrologyTile> hydrologyTiles,
            NativeArray<GeneratedSurfaceWaterTile> centerSurfaceWater,
            NativeArray<GeneratedHydrologyCell> springHydrologyCells,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            NativeArray<GeneratedHydrologyCell> sourceIntersectingHydrologyCells,
            NativeArray<SurfaceRechargeCandidate> sourceRechargeCandidates,
            NativeArray<GeneratedSurfaceWaterTile> sourceWaterArea,
            NativeArray<GeneratedHydrologyCell> evaluationHydrologyCells,
            NativeArray<byte> evaluationHydrologyCellWritten,
            NativeArray<byte> candidateCells,
            NativeArray<byte> largeExtentCells,
            NativeArray<byte> visitedCells,
            NativeArray<int> cellQueue)
        {
            int intersectingCellCount =
                DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount;
            if (!DeterministicNaturalFloodingGenerator.ValidateSettings(settings.NaturalFlooding)
                || !terrainTiles.IsCreated || terrainTiles.Length < WorldGridConstants.TilesPerChunk
                || !hydrologyTiles.IsCreated || hydrologyTiles.Length < WorldGridConstants.TilesPerChunk
                || !centerSurfaceWater.IsCreated
                || centerSurfaceWater.Length < WorldGridConstants.TilesPerChunk
                || !springHydrologyCells.IsCreated
                || springHydrologyCells.Length < DeterministicLakeSpringGenerator.SpringHydrologyCellCount
                || !sourceIntersectingHydrologyCells.IsCreated
                || sourceIntersectingHydrologyCells.Length
                    < SourceChunkLaneCount * intersectingCellCount
                || !sourceRechargeCandidates.IsCreated
                || sourceRechargeCandidates.Length < SourceChunkLaneCount * intersectingCellCount
                || !sourceWaterArea.IsCreated || sourceWaterArea.Length < SourceAreaTileCount
                || !evaluationHydrologyCells.IsCreated
                || evaluationHydrologyCells.Length < EvaluationCellCount
                || !evaluationHydrologyCellWritten.IsCreated
                || evaluationHydrologyCellWritten.Length < EvaluationCellCount
                || !TryGetOrigins(
                    chunkX,
                    chunkY,
                    out int targetMinimumTileX,
                    out int targetMinimumTileY,
                    out int sourceMinimumTileX,
                    out int sourceMinimumTileY,
                    out int firstCellX,
                    out int firstCellY))
            {
                return false;
            }

            int evaluationFirstCellX = firstCellX - ExtentSearchRadiusInCells;
            int evaluationFirstCellY = firstCellY - ExtentSearchRadiusInCells;
            for (int index = 0; index < EvaluationCellCount; index++)
            {
                evaluationHydrologyCellWritten[index] = 0;
            }

            // 中央9×9はすでに正確な地下水・最終湖区分を持つため、周辺再構築より先に固定します。
            for (int centerY = 0; centerY < TargetOwnerCellsPerAxis; centerY++)
            {
                for (int centerX = 0; centerX < TargetOwnerCellsPerAxis; centerX++)
                {
                    int cacheX = centerX
                        + DeterministicLakeSpringGenerator.SpringHydrologyOriginOffsetFromIntersecting;
                    int cacheY = centerY
                        + DeterministicLakeSpringGenerator.SpringHydrologyOriginOffsetFromIntersecting;
                    int evaluationX = centerX + ExtentSearchRadiusInCells;
                    int evaluationY = centerY + ExtentSearchRadiusInCells;
                    int evaluationIndex = (evaluationY * EvaluationCellsPerAxis) + evaluationX;
                    evaluationHydrologyCells[evaluationIndex] = springHydrologyCells[
                        (cacheY * DeterministicLakeSpringGenerator.SpringHydrologyCellsPerAxis) + cacheX];
                    evaluationHydrologyCellWritten[evaluationIndex] = 1;
                }
            }
            CopySourceChunkToArea(0, 0, centerSurfaceWater, sourceWaterArea);

            // 担当完了順ではなく0〜7の固定順で重複セルを解決し、実行スケジュールを結果へ混ぜません。
            for (int sourceLaneIndex = 0; sourceLaneIndex < SourceChunkLaneCount; sourceLaneIndex++)
            {
                int startIndex = sourceLaneIndex * intersectingCellCount;
                if (!TryFinalizeEvaluationHydrologyFromSourceChunk(
                        settings,
                        evaluationFirstCellX,
                        evaluationFirstCellY,
                        sourceIntersectingHydrologyCells.GetSubArray(startIndex, intersectingCellCount),
                        sourceRechargeCandidates.GetSubArray(startIndex, intersectingCellCount),
                        frontier,
                        basinMembers,
                        basinLookup,
                        evaluationHydrologyCells,
                        evaluationHydrologyCellWritten))
                {
                    return false;
                }
            }

            return TryEvaluateChunk(
                settings,
                targetMinimumTileX,
                targetMinimumTileY,
                sourceMinimumTileX,
                sourceMinimumTileY,
                evaluationFirstCellX,
                evaluationFirstCellY,
                terrainTiles,
                hydrologyTiles,
                sourceWaterArea,
                evaluationHydrologyCells,
                evaluationHydrologyCellWritten,
                candidateCells,
                largeExtentCells,
                visitedCells,
                cellQueue);
        }

        /// <summary>
        /// 低地、淡水接続、自然飽和または自然冠水の3条件を一つのタイルについて評価します。
        /// </summary>
        /// <param name="settings">低地範囲と二つのポテンシャル境界を持つ固定設定です。</param>
        /// <param name="elevation">対象タイルの0〜65,535連続標高です。</param>
        /// <param name="freshwaterConnected">固定淡水系へ接続する場合はtrueです。</param>
        /// <param name="soilSaturationPotential">対象タイルの自然土壌飽和度です。</param>
        /// <param name="floodingPotential">対象タイルの自然冠水度です。</param>
        /// <returns>確定した3条件をすべて満たす場合はtrueです。</returns>
        public static bool IsWetlandTileEligible(
            in NaturalFloodingGenerationSettings settings,
            ushort elevation,
            bool freshwaterConnected,
            ushort soilSaturationPotential,
            ushort floodingPotential)
        {
            return DeterministicNaturalFloodingGenerator.ValidateSettings(settings)
                && elevation >= settings.WetlandMinimumElevation
                && elevation <= settings.WetlandMaximumElevation
                && freshwaterConnected
                && (soilSaturationPotential >= settings.NaturalFloodingThreshold
                    || floodingPotential >= settings.NaturalFloodingThreshold);
        }

        /// <summary>
        /// 完成水文セルの接続フラグと最終湖区分から、湿原資格に使える淡水接続かを判定します。
        /// </summary>
        /// <param name="hydrologyCell">河川・地下水接続とC4-Cの最終湖区分を持つ完成水文セルです。</param>
        /// <returns>河川・地下水、または恒久淡水湖へ接続する場合はtrueです。</returns>
        internal static bool IsHydrologyCellFreshwaterConnected(
            in GeneratedHydrologyCell hydrologyCell)
        {
            HydrologyConnectionFlags directFreshwaterFlags = HydrologyConnectionFlags.Stream
                | HydrologyConnectionFlags.River
                | HydrologyConnectionFlags.Groundwater;
            if ((hydrologyCell.ConnectionFlags & directFreshwaterFlags) != 0)
            {
                // 湖の水質とは独立した河川・地下水接続があるセルは、その接続自体を淡水として扱います。
                return true;
            }

            if ((hydrologyCell.ConnectionFlags & HydrologyConnectionFlags.Lake) == 0)
            {
                // 最終湖区分だけが残る不整合な入力を淡水へ昇格させず、明示された接続だけを使用します。
                return false;
            }

            // Lake bitは固定湖面の存在だけを表すため、一時湖と恒久塩湖を水質区分で除外します。
            return hydrologyCell.LakeClassification
                    == LakeWaterBalanceClassification.OverflowFreshwater
                || hydrologyCell.LakeClassification
                    == LakeWaterBalanceClassification.ClosedFreshwater;
        }

        /// <summary>
        /// 一つの8×8水文セルが広域湿原候補になる最低資格タイル数を満たすか判定します。
        /// </summary>
        /// <param name="settings">最低資格タイル数16を持つ固定自然冠水設定です。</param>
        /// <param name="eligibleTileCount">セル内64タイルのうち共通資格を満たした件数です。</param>
        /// <returns>設定と件数が有効で、資格タイル数が16以上ならtrueです。</returns>
        public static bool HasMinimumEligibleTiles(
            in NaturalFloodingGenerationSettings settings,
            int eligibleTileCount)
        {
            return DeterministicNaturalFloodingGenerator.ValidateSettings(settings)
                && eligibleTileCount >= settings.MinimumEligibleTilesPerHydrologyCell
                && eligibleTileCount <= TilesPerHydrologyCellAxis * TilesPerHydrologyCellAxis;
        }

        /// <summary>
        /// 指定候補セルが上下左右だけで4セル以上の成分へ属するか、4件発見時点で打ち切って判定します。
        /// </summary>
        /// <param name="settings">必要連結数4を持つ固定自然冠水設定です。</param>
        /// <param name="startX">15×15候補配列内の開始Xです。</param>
        /// <param name="startY">15×15候補配列内の開始Yです。</param>
        /// <param name="candidateCells">資格セルを1で保持する行優先15×15配列です。</param>
        /// <param name="visitedCells">呼出しごとに全要素を初期化して再利用する訪問配列です。</param>
        /// <param name="cellQueue">最大225件のセル添字を保持するFIFOです。</param>
        /// <returns>開始セルを含む上下左右成分から必要数を発見した場合はtrueです。</returns>
        public static bool HasMinimumConnectedComponent(
            in NaturalFloodingGenerationSettings settings,
            int startX,
            int startY,
            NativeArray<byte> candidateCells,
            NativeArray<byte> visitedCells,
            NativeArray<int> cellQueue)
        {
            if (!DeterministicNaturalFloodingGenerator.ValidateSettings(settings)
                || startX < 0 || startX >= EvaluationCellsPerAxis
                || startY < 0 || startY >= EvaluationCellsPerAxis
                || !candidateCells.IsCreated || candidateCells.Length < EvaluationCellCount
                || !visitedCells.IsCreated || visitedCells.Length < EvaluationCellCount
                || !cellQueue.IsCreated || cellQueue.Length < EvaluationCellCount)
            {
                return false;
            }

            int startIndex = (startY * EvaluationCellsPerAxis) + startX;
            if (candidateCells[startIndex] == 0)
            {
                return false;
            }

            for (int index = 0; index < EvaluationCellCount; index++)
            {
                visitedCells[index] = 0;
            }

            int readIndex = 0;
            int writeIndex = 1;
            int connectedCount = 0;
            cellQueue[0] = startIndex;
            visitedCells[startIndex] = 1;
            while (readIndex < writeIndex)
            {
                int currentIndex = cellQueue[readIndex++];
                connectedCount++;
                if (connectedCount >= settings.MinimumConnectedHydrologyCells)
                {
                    return true;
                }

                int currentX = currentIndex % EvaluationCellsPerAxis;
                int currentY = currentIndex / EvaluationCellsPerAxis;
                // 上下左右だけを固定順で追加し、斜め接触や列挙順を結果へ混ぜません。
                for (int direction = 0; direction < 4; direction++)
                {
                    int neighborX = currentX + (direction == 0 ? -1 : direction == 1 ? 1 : 0);
                    int neighborY = currentY + (direction == 2 ? -1 : direction == 3 ? 1 : 0);
                    if (neighborX < 0 || neighborX >= EvaluationCellsPerAxis
                        || neighborY < 0 || neighborY >= EvaluationCellsPerAxis)
                    {
                        continue;
                    }

                    int neighborIndex = (neighborY * EvaluationCellsPerAxis) + neighborX;
                    if (candidateCells[neighborIndex] == 0 || visitedCells[neighborIndex] != 0)
                    {
                        continue;
                    }

                    visitedCells[neighborIndex] = 1;
                    cellQueue[writeIndex++] = neighborIndex;
                }
            }

            return false;
        }

        /// <summary>一つの64×64表流水を、3×3共有水源内の重ならない担当位置へコピーします。</summary>
        /// <param name="chunkOffsetX">中心から見た-1〜1の水平チャンク差です。</param>
        /// <param name="chunkOffsetY">中心から見た-1〜1の垂直チャンク差です。</param>
        /// <param name="source">コピー元となる行優先4096表流水です。</param>
        /// <param name="sourceWaterArea">コピー先となる行優先192×192表流水です。</param>
        private static void CopySourceChunkToArea(
            int chunkOffsetX,
            int chunkOffsetY,
            NativeArray<GeneratedSurfaceWaterTile> source,
            NativeArray<GeneratedSurfaceWaterTile> sourceWaterArea)
        {
            int destinationStartX = (chunkOffsetX + SourceChunkRadius)
                * WorldGridConstants.LogicalChunkSize;
            int destinationStartY = (chunkOffsetY + SourceChunkRadius)
                * WorldGridConstants.LogicalChunkSize;
            for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
            {
                int sourceRow = localY * WorldGridConstants.LogicalChunkSize;
                int destinationRow = (destinationStartY + localY) * SourceAreaTilesPerAxis;
                for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
                {
                    sourceWaterArea[destinationRow + destinationStartX + localX] =
                        source[sourceRow + localX];
                }
            }
        }

        /// <summary>
        /// 完成した192×192水源と15×15水文から、湿原候補、連結規模、4096件の最終bitを求めます。
        /// 同期純粋経路と有限並列の製品経路でこの処理を共有し、結果差を作りません。
        /// </summary>
        /// <param name="settings">自然冠水と湿原資格を含む固定生成設定です。</param>
        /// <param name="targetMinimumTileX">対象チャンク左端の絶対タイルXです。</param>
        /// <param name="targetMinimumTileY">対象チャンク下端の絶対タイルYです。</param>
        /// <param name="sourceMinimumTileX">192×192水源左端の絶対タイルXです。</param>
        /// <param name="sourceMinimumTileY">192×192水源下端の絶対タイルYです。</param>
        /// <param name="evaluationFirstCellX">15×15評価領域左端の絶対水文セルXです。</param>
        /// <param name="evaluationFirstCellY">15×15評価領域下端の絶対水文セルYです。</param>
        /// <param name="terrainTiles">対象チャンクの完成済み4096地形です。</param>
        /// <param name="hydrologyTiles">自然飽和を読み、自然冠水と湿原bitを追記する4096件です。</param>
        /// <param name="sourceWaterArea">半径32の水源検索に使う192×192表流水です。</param>
        /// <param name="evaluationHydrologyCells">地下水・最終湖区分まで完成した15×15水文です。</param>
        /// <param name="evaluationHydrologyCellWritten">15×15全要素の完成状態です。</param>
        /// <param name="candidateCells">16資格タイル以上を持つセルを記録する作業配列です。</param>
        /// <param name="largeExtentCells">4セル以上の成分に属する中央セルを記録する作業配列です。</param>
        /// <param name="visitedCells">連結探索ごとに初期化して再利用する訪問配列です。</param>
        /// <param name="cellQueue">連結探索へ再利用する最大225件のFIFOです。</param>
        /// <returns>15×15評価と4096件への反映を完了できた場合はtrueです。</returns>
        private static bool TryEvaluateChunk(
            in WorldGenerationSettings settings,
            int targetMinimumTileX,
            int targetMinimumTileY,
            int sourceMinimumTileX,
            int sourceMinimumTileY,
            int evaluationFirstCellX,
            int evaluationFirstCellY,
            NativeArray<GeneratedTile> terrainTiles,
            NativeArray<GeneratedHydrologyTile> hydrologyTiles,
            NativeArray<GeneratedSurfaceWaterTile> sourceWaterArea,
            NativeArray<GeneratedHydrologyCell> evaluationHydrologyCells,
            NativeArray<byte> evaluationHydrologyCellWritten,
            NativeArray<byte> candidateCells,
            NativeArray<byte> largeExtentCells,
            NativeArray<byte> visitedCells,
            NativeArray<int> cellQueue)
        {
            if (!candidateCells.IsCreated || candidateCells.Length < EvaluationCellCount
                || !largeExtentCells.IsCreated || largeExtentCells.Length < EvaluationCellCount
                || !visitedCells.IsCreated || visitedCells.Length < EvaluationCellCount
                || !cellQueue.IsCreated || cellQueue.Length < EvaluationCellCount)
            {
                return false;
            }

            // 枠再利用時の前回成分判定を残さず、今回の15×15セルだけを正にします。
            for (int index = 0; index < EvaluationCellCount; index++)
            {
                candidateCells[index] = 0;
                largeExtentCells[index] = 0;
            }

            for (int evaluationY = 0; evaluationY < EvaluationCellsPerAxis; evaluationY++)
            {
                for (int evaluationX = 0; evaluationX < EvaluationCellsPerAxis; evaluationX++)
                {
                    int cellX = evaluationFirstCellX + evaluationX;
                    int cellY = evaluationFirstCellY + evaluationY;
                    int evaluationIndex = (evaluationY * EvaluationCellsPerAxis) + evaluationX;
                    GeneratedHydrologyCell hydrologyCell = evaluationHydrologyCells[evaluationIndex];
                    if (evaluationHydrologyCellWritten[evaluationIndex] == 0
                        || hydrologyCell.CellX != cellX
                        || hydrologyCell.CellY != cellY
                        || !TryCountEligibleTiles(
                            settings,
                            cellX,
                            cellY,
                            hydrologyCell,
                            targetMinimumTileX,
                            targetMinimumTileY,
                            sourceMinimumTileX,
                            sourceMinimumTileY,
                            terrainTiles,
                            hydrologyTiles,
                            sourceWaterArea,
                            out int eligibleTileCount))
                    {
                        return false;
                    }

                    candidateCells[evaluationIndex] = (byte)(HasMinimumEligibleTiles(
                        settings.NaturalFlooding,
                        eligibleTileCount) ? 1 : 0);
                }
            }

            // 対象タイルの所有セルは15×15評価領域の中央9×9です。各開始点は4セル発見時点で打ち切ります。
            for (int ownerY = 0; ownerY < TargetOwnerCellsPerAxis; ownerY++)
            {
                for (int ownerX = 0; ownerX < TargetOwnerCellsPerAxis; ownerX++)
                {
                    int evaluationX = ownerX + ExtentSearchRadiusInCells;
                    int evaluationY = ownerY + ExtentSearchRadiusInCells;
                    int evaluationIndex = (evaluationY * EvaluationCellsPerAxis) + evaluationX;
                    if (candidateCells[evaluationIndex] != 0
                        && HasMinimumConnectedComponent(
                            settings.NaturalFlooding,
                            evaluationX,
                            evaluationY,
                            candidateCells,
                            visitedCells,
                            cellQueue))
                    {
                        largeExtentCells[evaluationIndex] = 1;
                    }
                }
            }

            for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
            {
                for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
                {
                    int worldTileX = targetMinimumTileX + localX;
                    int worldTileY = targetMinimumTileY + localY;
                    if (!DeterministicHydrologyGenerator.TryGetCellCoordinate(
                            settings.Hydrology,
                            worldTileX,
                            worldTileY,
                            out int ownerCellX,
                            out int ownerCellY))
                    {
                        return false;
                    }

                    int evaluationX = ownerCellX - evaluationFirstCellX;
                    int evaluationY = ownerCellY - evaluationFirstCellY;
                    int evaluationIndex = (evaluationY * EvaluationCellsPerAxis) + evaluationX;
                    int tileIndex = WorldGrid.ToTileIndex(localX, localY);
                    GeneratedHydrologyTile result = hydrologyTiles[tileIndex];
                    if (largeExtentCells[evaluationIndex] != 0)
                    {
                        result.Flags |= GeneratedHydrologyTileFlags.LargeWetlandExtent;
                    }
                    hydrologyTiles[tileIndex] = result;
                }
            }

            return true;
        }

        /// <summary>中心の完成表流水と再生成した周囲8チャンクを192×192の固定矩形へ並べます。</summary>
        /// <param name="settings">表流水、地下水、湖区分を再現する固定生成設定です。</param>
        /// <param name="chunkX">中心の水平論理チャンク座標です。</param>
        /// <param name="chunkY">中心の垂直論理チャンク座標です。</param>
        /// <param name="centerSurfaceWater">湖・泉まで完成済みの中心4096表流水です。</param>
        /// <param name="springHydrologyCells">中央9×9の地下水・湖区分を再利用するC4-C完成キャッシュです。</param>
        /// <param name="evaluationFirstCellX">15×15評価領域の左端絶対水文セルXです。</param>
        /// <param name="evaluationFirstCellY">15×15評価領域の下端絶対水文セルYです。</param>
        /// <param name="frontier">周辺の集水・湖区分へ再利用する固定長Heapです。</param>
        /// <param name="basinMembers">周辺の閉鎖流域セルを保持する固定長配列です。</param>
        /// <param name="basinLookup">周辺の流域所属を検索する固定Hash表です。</param>
        /// <param name="pathWorkspace">周辺河道の有限経路探索へ再利用する配列です。</param>
        /// <param name="pathTiles">周辺河道の一経路を保持する配列です。</param>
        /// <param name="floodVisited">周辺湖Flood Fillの訪問状態です。</param>
        /// <param name="floodQueue">周辺湖Flood Fillの待ち行列です。</param>
        /// <param name="scratchHydrologyCells">周辺1チャンクの64基礎水文です。</param>
        /// <param name="scratchIntersectingHydrologyCells">周辺1チャンクと交差する9×9水文です。</param>
        /// <param name="scratchSurfaceRechargeCandidates">周辺9×9水文の表流水涵養有無です。</param>
        /// <param name="scratchSurfaceWater">周辺1チャンクの4096表流水です。</param>
        /// <param name="sourceWaterArea">中心を含む3×3チャンクの192×192出力です。</param>
        /// <param name="evaluationHydrologyCells">地下水・最終湖区分まで完成する15×15出力です。</param>
        /// <param name="evaluationHydrologyCellWritten">15×15各要素の書込完了を示す0／1配列です。</param>
        /// <returns>3×3チャンクを生成順へ依存せずすべて配置できた場合はtrueです。</returns>
        private static bool TryBuildSourceWaterArea(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            NativeArray<GeneratedSurfaceWaterTile> centerSurfaceWater,
            NativeArray<GeneratedHydrologyCell> springHydrologyCells,
            int evaluationFirstCellX,
            int evaluationFirstCellY,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            NativeArray<SurfaceWaterPathSearchNode> pathWorkspace,
            NativeArray<SurfaceWaterPathTile> pathTiles,
            NativeArray<byte> floodVisited,
            NativeArray<int> floodQueue,
            NativeArray<GeneratedHydrologyCell> scratchHydrologyCells,
            NativeArray<GeneratedHydrologyCell> scratchIntersectingHydrologyCells,
            NativeArray<SurfaceRechargeCandidate> scratchSurfaceRechargeCandidates,
            NativeArray<GeneratedSurfaceWaterTile> scratchSurfaceWater,
            NativeArray<GeneratedSurfaceWaterTile> sourceWaterArea,
            NativeArray<GeneratedHydrologyCell> evaluationHydrologyCells,
            NativeArray<byte> evaluationHydrologyCellWritten)
        {
            // 再利用枠の前回座標を残さず、今回の15×15セルがすべて埋まったことを末尾で検証します。
            for (int index = 0; index < EvaluationCellCount; index++)
            {
                evaluationHydrologyCellWritten[index] = 0;
            }

            // 中央9×9はC4-Cが地下水と最終湖区分まで完成済みです。泉用21×21の中央位置から再利用します。
            for (int centerY = 0; centerY < TargetOwnerCellsPerAxis; centerY++)
            {
                for (int centerX = 0; centerX < TargetOwnerCellsPerAxis; centerX++)
                {
                    int cacheX = centerX
                        + DeterministicLakeSpringGenerator.SpringHydrologyOriginOffsetFromIntersecting;
                    int cacheY = centerY
                        + DeterministicLakeSpringGenerator.SpringHydrologyOriginOffsetFromIntersecting;
                    int evaluationX = centerX + ExtentSearchRadiusInCells;
                    int evaluationY = centerY + ExtentSearchRadiusInCells;
                    int evaluationIndex = (evaluationY * EvaluationCellsPerAxis) + evaluationX;
                    evaluationHydrologyCells[evaluationIndex] = springHydrologyCells[
                        (cacheY * DeterministicLakeSpringGenerator.SpringHydrologyCellsPerAxis) + cacheX];
                    evaluationHydrologyCellWritten[evaluationIndex] = 1;
                }
            }

            for (int chunkOffsetY = -SourceChunkRadius;
                chunkOffsetY <= SourceChunkRadius;
                chunkOffsetY++)
            {
                for (int chunkOffsetX = -SourceChunkRadius;
                    chunkOffsetX <= SourceChunkRadius;
                    chunkOffsetX++)
                {
                    NativeArray<GeneratedSurfaceWaterTile> source = centerSurfaceWater;
                    if (chunkOffsetX != 0 || chunkOffsetY != 0)
                    {
                        long sourceChunkX = (long)chunkX + chunkOffsetX;
                        long sourceChunkY = (long)chunkY + chunkOffsetY;
                        if (sourceChunkX < int.MinValue || sourceChunkX > int.MaxValue
                            || sourceChunkY < int.MinValue || sourceChunkY > int.MaxValue
                            || !DeterministicHydrologyGenerator.TryGenerateChunkCells(
                                settings,
                                (int)sourceChunkX,
                                (int)sourceChunkY,
                                scratchHydrologyCells,
                                frontier,
                                basinMembers,
                                basinLookup)
                            || !DeterministicSurfaceWaterGenerator.TryGenerateChunk(
                                settings,
                                (int)sourceChunkX,
                                (int)sourceChunkY,
                                scratchHydrologyCells,
                                frontier,
                                basinMembers,
                                basinLookup,
                                pathWorkspace,
                                pathTiles,
                                floodVisited,
                                floodQueue,
                                scratchIntersectingHydrologyCells,
                                scratchSurfaceRechargeCandidates,
                                scratchSurfaceWater)
                            || !TryFinalizeEvaluationHydrologyFromSourceChunk(
                                settings,
                                evaluationFirstCellX,
                                evaluationFirstCellY,
                                scratchIntersectingHydrologyCells,
                                scratchSurfaceRechargeCandidates,
                                frontier,
                                basinMembers,
                                basinLookup,
                                evaluationHydrologyCells,
                                evaluationHydrologyCellWritten)
                            || !DeterministicLakeSpringGenerator.TryFinalizeSurfaceWaterLakes(
                                settings,
                                scratchSurfaceWater,
                                frontier,
                                basinMembers,
                                basinLookup))
                        {
                            return false;
                        }

                        source = scratchSurfaceWater;
                    }

                    int destinationStartX = (chunkOffsetX + SourceChunkRadius)
                        * WorldGridConstants.LogicalChunkSize;
                    int destinationStartY = (chunkOffsetY + SourceChunkRadius)
                        * WorldGridConstants.LogicalChunkSize;
                    for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
                    {
                        int sourceRow = localY * WorldGridConstants.LogicalChunkSize;
                        int destinationRow = (destinationStartY + localY) * SourceAreaTilesPerAxis;
                        for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
                        {
                            sourceWaterArea[destinationRow + destinationStartX + localX] =
                                source[sourceRow + localX];
                        }
                    }
                }
            }

            for (int index = 0; index < EvaluationCellCount; index++)
            {
                if (evaluationHydrologyCellWritten[index] == 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 周辺チャンクの交差9×9水文へ正確な涵養地下水と最終湖区分を適用し、必要な15×15範囲へコピーします。
        /// </summary>
        /// <param name="settings">地下水と湖水収支を再現する固定生成設定です。</param>
        /// <param name="evaluationFirstCellX">15×15評価領域の左端絶対水文セルXです。</param>
        /// <param name="evaluationFirstCellY">15×15評価領域の下端絶対水文セルYです。</param>
        /// <param name="intersectingCells">表流水生成が完成させた周辺チャンクの交差9×9水文です。</param>
        /// <param name="surfaceRechargeCandidates">河道幅と湖面候補を含む9×9涵養有無です。</param>
        /// <param name="frontier">湖区分の集水探索へ再利用する固定長Heapです。</param>
        /// <param name="basinMembers">湖区分の閉鎖流域セルを保持する固定長配列です。</param>
        /// <param name="basinLookup">湖区分の流域所属を検索する固定Hash表です。</param>
        /// <param name="evaluationHydrologyCells">成功セルを絶対座標対応位置へ書く15×15配列です。</param>
        /// <param name="evaluationHydrologyCellWritten">書込済み位置へ1を設定する15×15配列です。</param>
        /// <returns>交差9×9全件の地下水と湖区分を確定し、評価範囲との交差をコピーできた場合はtrueです。</returns>
        private static bool TryFinalizeEvaluationHydrologyFromSourceChunk(
            in WorldGenerationSettings settings,
            int evaluationFirstCellX,
            int evaluationFirstCellY,
            NativeArray<GeneratedHydrologyCell> intersectingCells,
            NativeArray<SurfaceRechargeCandidate> surfaceRechargeCandidates,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            NativeArray<GeneratedHydrologyCell> evaluationHydrologyCells,
            NativeArray<byte> evaluationHydrologyCellWritten)
        {
            HydrologyLakeBasinCache lakeCache = default;
            for (int index = 0;
                index < DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount;
                index++)
            {
                GeneratedHydrologyCell cell = intersectingCells[index];
                long evaluationLongX = (long)cell.CellX - evaluationFirstCellX;
                long evaluationLongY = (long)cell.CellY - evaluationFirstCellY;
                if (evaluationLongX < 0 || evaluationLongX >= EvaluationCellsPerAxis
                    || evaluationLongY < 0 || evaluationLongY >= EvaluationCellsPerAxis)
                {
                    // 9×9のうち15×15評価領域へ入らないセルは、表流水生成だけで役割を終えています。
                    continue;
                }
                int evaluationIndex = ((int)evaluationLongY * EvaluationCellsPerAxis)
                    + (int)evaluationLongX;
                if (evaluationHydrologyCellWritten[evaluationIndex] != 0)
                {
                    // 中央9×9または先に処理した隣接チャンクと重なるセルは、同じ絶対座標なので再計算しません。
                    continue;
                }

                if (!DeterministicGroundwaterHydrologyGenerator.TryGenerateCellGroundwater(
                        settings,
                        cell,
                        surfaceRechargeCandidates[index].HasRecharge != 0,
                        out ushort groundwaterPotential))
                {
                    return false;
                }

                cell.GroundwaterPotential = groundwaterPotential;
                cell.ConnectionFlags &= ~HydrologyConnectionFlags.Groundwater;
                if (groundwaterPotential >= settings.Groundwater.GroundwaterConnectionThreshold)
                {
                    cell.ConnectionFlags |= HydrologyConnectionFlags.Groundwater;
                }
                if (!DeterministicLakeSpringGenerator.TryFinalizeCellLakeForVisualization(
                        settings,
                        ref cell,
                        frontier,
                        basinMembers,
                        basinLookup,
                        ref lakeCache))
                {
                    return false;
                }
                intersectingCells[index] = cell;

                evaluationHydrologyCells[evaluationIndex] = cell;
                evaluationHydrologyCellWritten[evaluationIndex] = 1;
            }

            return true;
        }

        /// <summary>一つの8×8水文セルを全タイル評価し、広域湿原ゲートを満たす件数を返します。</summary>
        /// <param name="settings">地形、地下水、自然冠水の固定生成設定です。</param>
        /// <param name="cellX">評価する絶対水文セルXです。</param>
        /// <param name="cellY">評価する絶対水文セルYです。</param>
        /// <param name="hydrologyCell">地下水と最終淡水接続を含む完成水文セルです。</param>
        /// <param name="targetMinimumTileX">公開対象チャンクの左端絶対タイルXです。</param>
        /// <param name="targetMinimumTileY">公開対象チャンクの下端絶対タイルYです。</param>
        /// <param name="sourceMinimumTileX">192×192淡水源矩形の左端絶対タイルXです。</param>
        /// <param name="sourceMinimumTileY">192×192淡水源矩形の下端絶対タイルYです。</param>
        /// <param name="terrainTiles">対象内で再利用する完成済み4096地形です。</param>
        /// <param name="hydrologyTiles">対象内の自然飽和を読み、自然冠水と資格bitを追記する配列です。</param>
        /// <param name="sourceWaterArea">半径32の水源を検索する192×192表流水です。</param>
        /// <param name="eligibleTileCount">成功時に共通湿原資格を満たした0〜64件です。</param>
        /// <returns>64タイルすべての固定値を生成できた場合はtrueです。</returns>
        private static bool TryCountEligibleTiles(
            in WorldGenerationSettings settings,
            int cellX,
            int cellY,
            in GeneratedHydrologyCell hydrologyCell,
            int targetMinimumTileX,
            int targetMinimumTileY,
            int sourceMinimumTileX,
            int sourceMinimumTileY,
            NativeArray<GeneratedTile> terrainTiles,
            NativeArray<GeneratedHydrologyTile> hydrologyTiles,
            NativeArray<GeneratedSurfaceWaterTile> sourceWaterArea,
            out int eligibleTileCount)
        {
            eligibleTileCount = 0;
            bool cellFreshwaterConnected = IsHydrologyCellFreshwaterConnected(hydrologyCell);
            long centerTileX = (long)cellX * TilesPerHydrologyCellAxis;
            long centerTileY = (long)cellY * TilesPerHydrologyCellAxis;
            for (int tileOffsetY = ResponsibilityMinimumOffset;
                tileOffsetY < ResponsibilityMinimumOffset + TilesPerHydrologyCellAxis;
                tileOffsetY++)
            {
                for (int tileOffsetX = ResponsibilityMinimumOffset;
                    tileOffsetX < ResponsibilityMinimumOffset + TilesPerHydrologyCellAxis;
                    tileOffsetX++)
                {
                    long worldTileLongX = centerTileX + tileOffsetX;
                    long worldTileLongY = centerTileY + tileOffsetY;
                    if (worldTileLongX < int.MinValue || worldTileLongX > int.MaxValue
                        || worldTileLongY < int.MinValue || worldTileLongY > int.MaxValue)
                    {
                        return false;
                    }

                    int worldTileX = (int)worldTileLongX;
                    int worldTileY = (int)worldTileLongY;
                    // int同士の減算は端座標で符号反転し得るため、対象内判定が済むまで64bitの差を保ちます。
                    long localLongX = (long)worldTileX - targetMinimumTileX;
                    long localLongY = (long)worldTileY - targetMinimumTileY;
                    bool isTargetTile = localLongX >= 0
                        && localLongX < WorldGridConstants.LogicalChunkSize
                        && localLongY >= 0
                        && localLongY < WorldGridConstants.LogicalChunkSize;
                    GeneratedTile terrainTile;
                    ushort saturationPotential;
                    int targetTileIndex = -1;
                    if (isTargetTile)
                    {
                        int localX = (int)localLongX;
                        int localY = (int)localLongY;
                        targetTileIndex = WorldGrid.ToTileIndex(localX, localY);
                        terrainTile = terrainTiles[targetTileIndex];
                        saturationPotential = hydrologyTiles[targetTileIndex]
                            .NaturalSoilSaturationPotential;
                    }
                    else
                    {
                        if (!DeterministicChunkGenerator.TryGenerateTile(
                                settings,
                                worldTileX,
                                worldTileY,
                                out terrainTile)
                            || !DeterministicGroundwaterGenerator.TryGenerateAdjustedGeology(
                                settings,
                                worldTileX,
                                worldTileY,
                                terrainTile.GeologyPermeability,
                                terrainTile.GeologyGroundwaterStorage,
                                out AdjustedGroundwaterGeology adjusted)
                            || !DeterministicGroundwaterHydrologyGenerator
                                .TryCalculateNaturalSoilSaturation(
                                    settings.Groundwater,
                                    hydrologyCell.HydrologyMoisture,
                                    hydrologyCell.GroundwaterPotential,
                                    adjusted.Permeability,
                                    terrainTile.TerrainRuggedness,
                                    out saturationPotential))
                        {
                            return false;
                        }
                    }

                    if (!DeterministicNaturalFloodingGenerator.TryFindPreferredSource(
                            settings.NaturalFlooding,
                            worldTileX,
                            worldTileY,
                            sourceMinimumTileX,
                            sourceMinimumTileY,
                            SourceAreaTilesPerAxis,
                            SourceAreaTilesPerAxis,
                            sourceWaterArea,
                            out NaturalFloodingSource source)
                        || !DeterministicNaturalFloodingGenerator
                            .TryCalculateNaturalFloodingPotential(
                                settings.NaturalFlooding,
                                terrainTile.Elevation,
                                terrainTile.TerrainRuggedness,
                                source,
                                out NaturalFloodingResult flooding))
                    {
                        return false;
                    }

                    bool freshwaterConnected = cellFreshwaterConnected || source.IsValid != 0;
                    bool eligible = IsWetlandTileEligible(
                        settings.NaturalFlooding,
                        terrainTile.Elevation,
                        freshwaterConnected,
                        saturationPotential,
                        flooding.Potential);
                    if (eligible)
                    {
                        eligibleTileCount++;
                    }

                    if (!isTargetTile)
                    {
                        continue;
                    }

                    GeneratedHydrologyTile result = hydrologyTiles[targetTileIndex];
                    result.NaturalFloodingPotential = flooding.Potential;
                    result.Flags = GeneratedHydrologyTileFlags.None;
                    if (freshwaterConnected)
                    {
                        result.Flags |= GeneratedHydrologyTileFlags.FreshwaterConnected;
                    }
                    if (eligible)
                    {
                        result.Flags |= GeneratedHydrologyTileFlags.WetlandTileEligible;
                    }
                    hydrologyTiles[targetTileIndex] = result;
                }
            }

            return true;
        }

        /// <summary>対象・冠水源矩形・先頭水文セルの座標を64bitで検証してからintへ変換します。</summary>
        /// <param name="chunkX">対象の水平論理チャンク座標です。</param>
        /// <param name="chunkY">対象の垂直論理チャンク座標です。</param>
        /// <param name="targetMinimumTileX">成功時の対象チャンク左端絶対タイルXです。</param>
        /// <param name="targetMinimumTileY">成功時の対象チャンク下端絶対タイルYです。</param>
        /// <param name="sourceMinimumTileX">成功時の192×192水源矩形左端Xです。</param>
        /// <param name="sourceMinimumTileY">成功時の192×192水源矩形下端Yです。</param>
        /// <param name="firstCellX">成功時の対象9×9水文先頭セルXです。</param>
        /// <param name="firstCellY">成功時の対象9×9水文先頭セルYです。</param>
        /// <returns>3×3チャンクと15×15評価セルがint座標域へ収まる場合はtrueです。</returns>
        private static bool TryGetOrigins(
            int chunkX,
            int chunkY,
            out int targetMinimumTileX,
            out int targetMinimumTileY,
            out int sourceMinimumTileX,
            out int sourceMinimumTileY,
            out int firstCellX,
            out int firstCellY)
        {
            long targetX = (long)chunkX * WorldGridConstants.LogicalChunkSize;
            long targetY = (long)chunkY * WorldGridConstants.LogicalChunkSize;
            long sourceX = targetX - WorldGridConstants.LogicalChunkSize;
            long sourceY = targetY - WorldGridConstants.LogicalChunkSize;
            long sourceMaximumX = sourceX + SourceAreaTilesPerAxis - 1L;
            long sourceMaximumY = sourceY + SourceAreaTilesPerAxis - 1L;
            long cellX = (long)chunkX * DeterministicHydrologyGenerator.CellsPerChunkAxis;
            long cellY = (long)chunkY * DeterministicHydrologyGenerator.CellsPerChunkAxis;
            if (targetX < int.MinValue || targetX + WorldGridConstants.LogicalChunkSize - 1L > int.MaxValue
                || targetY < int.MinValue || targetY + WorldGridConstants.LogicalChunkSize - 1L > int.MaxValue
                || sourceX < int.MinValue || sourceMaximumX > int.MaxValue
                || sourceY < int.MinValue || sourceMaximumY > int.MaxValue
                // 21×21キャッシュの先頭は対象先頭より6セル前なので、15×15評価範囲より広い側も検査します。
                || cellX - DeterministicLakeSpringGenerator.SpringHydrologyOriginOffsetFromIntersecting
                    < int.MinValue
                || cellX + TargetOwnerCellsPerAxis + ExtentSearchRadiusInCells - 1L > int.MaxValue
                || cellY - DeterministicLakeSpringGenerator.SpringHydrologyOriginOffsetFromIntersecting
                    < int.MinValue
                || cellY + TargetOwnerCellsPerAxis + ExtentSearchRadiusInCells - 1L > int.MaxValue)
            {
                targetMinimumTileX = default;
                targetMinimumTileY = default;
                sourceMinimumTileX = default;
                sourceMinimumTileY = default;
                firstCellX = default;
                firstCellY = default;
                return false;
            }

            targetMinimumTileX = (int)targetX;
            targetMinimumTileY = (int)targetY;
            sourceMinimumTileX = (int)sourceX;
            sourceMinimumTileY = (int)sourceY;
            firstCellX = (int)cellX;
            firstCellY = (int)cellY;
            return true;
        }
    }
}
