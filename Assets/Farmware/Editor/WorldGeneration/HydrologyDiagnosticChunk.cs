// このファイルは、C3とC4-A〜Dの既存Generatorを製品と同じ順序で実行し、Editor監査用の完成水文チャンクを作ります。
// 複数Seed監査と後続Map Viewerが同じ再構築経路を共有し、診断側へ生成規則を複製しないための境界です。

using System;
using Farmware.Simulation;
using Unity.Collections;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 完成水文チャンクの生成が停止した段階です。
    /// 監査エラーで乾燥チャンクと生成失敗を混同しないため、呼出し元へ明示します。
    /// </summary>
    public enum HydrologyDiagnosticGenerationStage : byte
    {
        /// <summary>全段階が成功し、4種類の出力配列を利用できる状態です。</summary>
        Complete = 0,

        /// <summary>Surface以外の未対応WorldLayerが指定され、配列確保前に終了した状態です。</summary>
        UnsupportedLayer = 1,

        /// <summary>C3の64水文セル生成で失敗した状態です。</summary>
        HydrologyCells = 2,

        /// <summary>C4-Aの河道・湖面候補生成で失敗した状態です。</summary>
        SurfaceWater = 3,

        /// <summary>4096地形タイルの固定生成で失敗した状態です。</summary>
        TerrainTiles = 4,

        /// <summary>C4-Bの地下水・自然土壌飽和生成で失敗した状態です。</summary>
        Groundwater = 5,

        /// <summary>C4-Cの湖水収支・泉生成で失敗した状態です。</summary>
        LakeAndSpring = 6,

        /// <summary>C4-Dの自然冠水・湿原規模生成で失敗した状態です。</summary>
        NaturalFloodingAndWetland = 7
    }

    /// <summary>
    /// Editor診断用に再構築した1論理チャンクの完成地形、水文セル、表流水、水文タイルを所有します。
    /// <see cref="Dispose"/>を呼ぶまでPersistent NativeArrayを保持するため、usingで寿命を限定して使用します。
    /// </summary>
    public sealed class HydrologyDiagnosticChunk : IDisposable
    {
        /// <summary>生成対象のレイヤー付き論理チャンク座標です。構築後は変更されません。</summary>
        public ChunkCoordinate Coordinate { get; }

        /// <summary>行優先4096件の完成済み基礎地形です。診断器が所有し、Dispose時に破棄します。</summary>
        public NativeArray<GeneratedTile> TerrainTiles { get; private set; }

        /// <summary>中心が対象チャンク内に属する行優先64件の完成固定水文セルです。診断器が所有します。</summary>
        public NativeArray<GeneratedHydrologyCell> HydrologyCells { get; private set; }

        /// <summary>行優先4096件の湖・泉確定後固定表流水です。診断器が所有します。</summary>
        public NativeArray<GeneratedSurfaceWaterTile> SurfaceWaterTiles { get; private set; }

        /// <summary>行優先4096件の自然土壌飽和・自然冠水・湿原フラグです。診断器が所有します。</summary>
        public NativeArray<GeneratedHydrologyTile> HydrologyTiles { get; private set; }

        /// <summary>
        /// 返却配列をPersistentで確保します。生成途中でもDisposeできるよう、全所有配列を先に作ります。
        /// </summary>
        /// <param name="coordinate">再構築するSurface論理チャンク座標です。</param>
        private HydrologyDiagnosticChunk(ChunkCoordinate coordinate)
        {
            Coordinate = coordinate;
            try
            {
                // 途中の確保で例外が起きても、それ以前に確保したPersistent配列を残さないため、
                // すべての確保を同じtry内にまとめ、catch側で所有済み配列だけを破棄します。
                TerrainTiles = new NativeArray<GeneratedTile>(
                    WorldGridConstants.TilesPerChunk,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                HydrologyCells = new NativeArray<GeneratedHydrologyCell>(
                    DeterministicHydrologyGenerator.CellsPerChunk,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                SurfaceWaterTiles = new NativeArray<GeneratedSurfaceWaterTile>(
                    WorldGridConstants.TilesPerChunk,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                HydrologyTiles = new NativeArray<GeneratedHydrologyTile>(
                    WorldGridConstants.TilesPerChunk,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// C3とC4-A〜Dの既存Generatorを製品と同じ順序で実行し、完成診断チャンクを生成します。
        /// 呼出し元は成功時のchunkをusingで破棄し、失敗時はstageを監査エラーへ記録します。
        /// </summary>
        /// <param name="settings">Seed、GeneratorVersion、全固定生成設定です。</param>
        /// <param name="coordinate">生成するSurface論理チャンク座標です。</param>
        /// <param name="chunk">成功時に完成配列を所有する診断チャンクです。失敗時はnullです。</param>
        /// <param name="stage">成功時はComplete、失敗時は停止した生成段階です。</param>
        /// <returns>全4出力を部分結果なしで完成できた場合はtrueです。</returns>
        public static bool TryGenerate(
            in WorldGenerationSettings settings,
            ChunkCoordinate coordinate,
            out HydrologyDiagnosticChunk chunk,
            out HydrologyDiagnosticGenerationStage stage)
        {
            chunk = null;
            stage = HydrologyDiagnosticGenerationStage.UnsupportedLayer;
            if (coordinate.Layer != WorldLayer.Surface)
            {
                return false;
            }

            HydrologyDiagnosticChunk generated = new HydrologyDiagnosticChunk(coordinate);
            try
            {
                if (!TryPopulate(settings, generated, out stage))
                {
                    generated.Dispose();
                    return false;
                }
            }
            catch
            {
                // 作業配列側のusingだけでは完成出力4配列は解放されないため、
                // GeneratorやAllocatorが例外を返した経路でも所有者をここで破棄してから再送出します。
                generated.Dispose();
                throw;
            }

            chunk = generated;
            return true;
        }

        /// <summary>
        /// 呼出し中だけ必要な大容量作業配列を確実に破棄しながら、所有済み出力へ各段階を順番に書き込みます。
        /// </summary>
        /// <param name="settings">製品Worldと同じ固定生成設定です。</param>
        /// <param name="chunk">成功時だけ公開する4出力配列の所有者です。</param>
        /// <param name="stage">最後に開始した生成段階です。</param>
        /// <returns>C4-Dまでの全処理が成功した場合はtrueです。</returns>
        private static bool TryPopulate(
            in WorldGenerationSettings settings,
            HydrologyDiagnosticChunk chunk,
            out HydrologyDiagnosticGenerationStage stage)
        {
            using var frontier = new NativeArray<HydrologyUpstreamCandidate>(
                DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var basinMembers = new NativeArray<HydrologyUpstreamCandidate>(
                DeterministicHydrologyLakeResolver.MaximumBasinMembers,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var basinLookup = new NativeArray<HydrologyBasinLookupSlot>(
                DeterministicHydrologyLakeResolver.LookupCapacity,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);

            stage = HydrologyDiagnosticGenerationStage.HydrologyCells;
            if (!DeterministicHydrologyGenerator.TryGenerateChunkCells(
                    settings,
                    chunk.Coordinate.X,
                    chunk.Coordinate.Y,
                    chunk.HydrologyCells,
                    frontier,
                    basinMembers,
                    basinLookup))
            {
                return false;
            }

            using var pathWorkspace = new NativeArray<SurfaceWaterPathSearchNode>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var pathTiles = new NativeArray<SurfaceWaterPathTile>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var floodVisited = new NativeArray<byte>(
                DeterministicSurfaceWaterGenerator.MaximumFloodTileCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var floodQueue = new NativeArray<int>(
                DeterministicSurfaceWaterGenerator.MaximumFloodTileCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var intersectingCells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var rechargeCandidates = new NativeArray<SurfaceRechargeCandidate>(
                DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            using var springRechargeCandidates = new NativeArray<SurfaceRechargeCandidate>(
                DeterministicLakeSpringGenerator.SpringRechargeCandidateCount,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);

            stage = HydrologyDiagnosticGenerationStage.SurfaceWater;
            if (!DeterministicSurfaceWaterGenerator.TryGenerateChunk(
                    settings,
                    chunk.Coordinate.X,
                    chunk.Coordinate.Y,
                    chunk.HydrologyCells,
                    frontier,
                    basinMembers,
                    basinLookup,
                    pathWorkspace,
                    pathTiles,
                    floodVisited,
                    floodQueue,
                    intersectingCells,
                    rechargeCandidates,
                    springRechargeCandidates,
                    chunk.SurfaceWaterTiles))
            {
                return false;
            }

            stage = HydrologyDiagnosticGenerationStage.TerrainTiles;
            // NativeArrayはstructなので、プロパティ戻り値へ添字代入せず、同じ所有配列を指すローカル値へ書き込みます。
            NativeArray<GeneratedTile> terrainTiles = chunk.TerrainTiles;
            for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
            {
                for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
                {
                    int tileIndex = WorldGrid.ToTileIndex(localX, localY);
                    int worldTileX = WorldGrid.ToWorldTileX(chunk.Coordinate.X, localX);
                    int worldTileY = WorldGrid.ToWorldTileY(chunk.Coordinate.Y, localY);
                    if (!DeterministicChunkGenerator.TryGenerateTile(
                            settings,
                            worldTileX,
                            worldTileY,
                            out GeneratedTile tile))
                    {
                        return false;
                    }

                    terrainTiles[tileIndex] = tile;
                }
            }

            stage = HydrologyDiagnosticGenerationStage.Groundwater;
            if (!DeterministicGroundwaterHydrologyGenerator.TryGenerateChunk(
                    settings,
                    chunk.Coordinate.X,
                    chunk.Coordinate.Y,
                    chunk.TerrainTiles,
                    chunk.HydrologyCells,
                    intersectingCells,
                    rechargeCandidates,
                    chunk.HydrologyTiles))
            {
                return false;
            }

            using var springHydrologyCells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicLakeSpringGenerator.SpringHydrologyCellCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var springCandidates = new NativeArray<SpringCandidate>(
                DeterministicLakeSpringGenerator.SpringCandidateCellCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);

            stage = HydrologyDiagnosticGenerationStage.LakeAndSpring;
            if (!DeterministicLakeSpringGenerator.TryFinalizeChunk(
                    settings,
                    chunk.Coordinate.X,
                    chunk.Coordinate.Y,
                    chunk.HydrologyCells,
                    intersectingCells,
                    chunk.SurfaceWaterTiles,
                    frontier,
                    basinMembers,
                    basinLookup,
                    pathWorkspace,
                    pathTiles,
                    springRechargeCandidates,
                    springHydrologyCells,
                    springCandidates))
            {
                return false;
            }

            using var scratchHydrologyCells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicHydrologyGenerator.CellsPerChunk,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var scratchIntersectingCells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var scratchRechargeCandidates = new NativeArray<SurfaceRechargeCandidate>(
                DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var scratchSurfaceWater = new NativeArray<GeneratedSurfaceWaterTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var sourceWaterArea = new NativeArray<GeneratedSurfaceWaterTile>(
                DeterministicWetlandExtentGenerator.SourceAreaTileCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var evaluationCells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicWetlandExtentGenerator.EvaluationCellCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var evaluationCellWritten = new NativeArray<byte>(
                DeterministicWetlandExtentGenerator.EvaluationCellCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var wetlandCandidates = new NativeArray<byte>(
                DeterministicWetlandExtentGenerator.EvaluationCellCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var largeWetlandCells = new NativeArray<byte>(
                DeterministicWetlandExtentGenerator.EvaluationCellCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var visitedCells = new NativeArray<byte>(
                DeterministicWetlandExtentGenerator.EvaluationCellCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var cellQueue = new NativeArray<int>(
                DeterministicWetlandExtentGenerator.EvaluationCellCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);

            stage = HydrologyDiagnosticGenerationStage.NaturalFloodingAndWetland;
            if (!DeterministicWetlandExtentGenerator.TryGenerateChunk(
                    settings,
                    chunk.Coordinate.X,
                    chunk.Coordinate.Y,
                    chunk.TerrainTiles,
                    chunk.HydrologyTiles,
                    chunk.SurfaceWaterTiles,
                    springHydrologyCells,
                    frontier,
                    basinMembers,
                    basinLookup,
                    pathWorkspace,
                    pathTiles,
                    floodVisited,
                    floodQueue,
                    scratchHydrologyCells,
                    scratchIntersectingCells,
                    scratchRechargeCandidates,
                    scratchSurfaceWater,
                    sourceWaterArea,
                    evaluationCells,
                    evaluationCellWritten,
                    wetlandCandidates,
                    largeWetlandCells,
                    visitedCells,
                    cellQueue))
            {
                return false;
            }

            stage = HydrologyDiagnosticGenerationStage.Complete;
            return true;
        }

        /// <summary>
        /// この診断チャンクが所有する全NativeArrayを破棄します。
        /// 複数回呼ばれてもIsCreatedを確認し、未確保または破棄済み配列へDisposeしません。
        /// </summary>
        public void Dispose()
        {
            if (TerrainTiles.IsCreated)
            {
                // NativeArrayは値型です。プロパティのコピーだけをDisposeすると、プロパティ側に
                // 解放済みポインターが残るため、解放後にdefaultへ戻して二重破棄を防ぎます。
                NativeArray<GeneratedTile> terrainTiles = TerrainTiles;
                terrainTiles.Dispose();
                TerrainTiles = default;
            }
            if (HydrologyCells.IsCreated)
            {
                NativeArray<GeneratedHydrologyCell> hydrologyCells = HydrologyCells;
                hydrologyCells.Dispose();
                HydrologyCells = default;
            }
            if (SurfaceWaterTiles.IsCreated)
            {
                NativeArray<GeneratedSurfaceWaterTile> surfaceWaterTiles = SurfaceWaterTiles;
                surfaceWaterTiles.Dispose();
                SurfaceWaterTiles = default;
            }
            if (HydrologyTiles.IsCreated)
            {
                NativeArray<GeneratedHydrologyTile> hydrologyTiles = HydrologyTiles;
                hydrologyTiles.Dispose();
                HydrologyTiles = default;
            }
        }
    }
}
