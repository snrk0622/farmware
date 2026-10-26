// このファイルは、水文、Biome、Blockセルの既存Generatorを製品と同じ順序で実行し、Editor監査用の完成地形チャンクを作ります。
// 複数Seed監査と後続Map Viewerが同じ再構築経路を共有し、診断側へ生成規則を複製しないための境界です。

using System;
using Farmware.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 完成水文チャンクの生成が停止した段階です。
    /// 監査エラーで乾燥チャンクと生成失敗を混同しないため、呼出し元へ明示します。
    /// </summary>
    public enum HydrologyDiagnosticGenerationStage : byte
    {
        /// <summary>全段階が成功し、環境、Block、水文の5種類の出力配列を利用できる状態です。</summary>
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
        NaturalFloodingAndWetland = 7,

        /// <summary>既定地形DefinitionとBlockパレットの構築で失敗した状態です。</summary>
        TerrainCatalog = 8,

        /// <summary>新18Biome分類と自然生成Blockセル確定で失敗した状態です。</summary>
        BiomeAndTerrainFinalization = 9
    }

    /// <summary>
    /// 完成診断チャンクが共有する地形Definition、材質、Paletteの読み取り専用Blob一式です。
    /// Unity EntitiesのBlob完成処理は内部でMain Thread限定の一時領域を使うため、Editorメインスレッドで作成し、
    /// 完成後のBlobだけをMap Viewerや分布監査のThreadPool処理へ渡します。
    /// </summary>
    public sealed class HydrologyDiagnosticTerrainCatalogs : IDisposable
    {
        /// <summary>Runtime ID解決の基準となる共通Definition Blobです。この所有者だけが破棄します。</summary>
        private BlobAssetReference<DefinitionCatalogBlob> definitionCatalog;

        /// <summary>Ground、Wall、Liquidの分類と属性を保持する地形Definition Blobです。</summary>
        private BlobAssetReference<TerrainBlockCatalogBlob> terrainCatalog;

        /// <summary>41材質をItem、Ground、Wallへ対応付ける材質Blobです。</summary>
        private BlobAssetReference<TerrainBlockMaterialCatalogBlob> materialCatalog;

        /// <summary>18Biomeと8地質の自然生成候補を保持し、この所有者だけが破棄するPalette Blobです。</summary>
        private BlobAssetReference<TerrainBlockPaletteCatalogBlob> paletteCatalog;

        /// <summary>完成セル確定へ読み取り専用で渡す、構築済みPalette Blobです。</summary>
        internal BlobAssetReference<TerrainBlockPaletteCatalogBlob> PaletteCatalog => paletteCatalog;

        /// <summary>淡水、海水、マグマ等の組み込みBlockを解決したRuntime ID一式です。</summary>
        public BuiltInTerrainBlockRuntimeIds RuntimeIds { get; private set; }

        /// <summary>必要な4つのBlobと組み込みRuntime IDがすべて利用可能な場合はtrueです。</summary>
        public bool IsCreated => definitionCatalog.IsCreated
            && terrainCatalog.IsCreated
            && materialCatalog.IsCreated
            && paletteCatalog.IsCreated
            && RuntimeIds.IsValid;

        /// <summary>静的な<see cref="TryCreate"/>だけに生成を限定し、部分構築された所有者を公開しません。</summary>
        private HydrologyDiagnosticTerrainCatalogs()
        {
        }

        /// <summary>
        /// 製品Bakerと同じ既定入力から診断用カタログを構築します。
        /// Blob完成処理がMain Thread限定の一時領域を使うため、Task.Runへ入る前のEditorメインスレッドで呼び出します。
        /// </summary>
        /// <param name="catalogs">成功時に4つのPersistent Blobを所有するカタログ一式です。失敗時はnullです。</param>
        /// <returns>共通Definition、属性、材質、Palette、組み込みIDをすべて作成できた場合はtrueです。</returns>
        public static bool TryCreate(out HydrologyDiagnosticTerrainCatalogs catalogs)
        {
            var created = new HydrologyDiagnosticTerrainCatalogs();
            try
            {
                DefinitionBuildEntry[] definitions =
                    BuiltInTerrainBlockDefinitions.CreateCommonDefinitions(
                        0,
                        out DefinitionBuildReference[] references);
                if (!DefinitionCatalogBuilder.TryBuild(
                        definitions,
                        references,
                        out created.definitionCatalog,
                        out _)
                    || !BuiltInTerrainBlockDefinitions.TryCreateTerrainDefinitions(
                        created.definitionCatalog,
                        out TerrainBlockDefinitionBuildEntry[] terrainDefinitions)
                    || !TerrainBlockCatalogBuilder.TryBuild(
                        created.definitionCatalog,
                        terrainDefinitions,
                        out created.terrainCatalog)
                    || !TerrainBlockMaterialCatalogBuilder.TryBuild(
                        created.definitionCatalog,
                        TerrainBlockMaterialDefinitions.CreateDefault(),
                        out created.materialCatalog)
                    || !TerrainBlockPaletteCatalogBuilder.TryBuild(
                        created.terrainCatalog,
                        created.materialCatalog,
                        TerrainBlockPaletteDefinitions.CreateBiomeSurfaceDefaults(),
                        TerrainBlockPaletteDefinitions.CreateGeologyDefaults(),
                        out created.paletteCatalog)
                    || !BuiltInTerrainBlockDefinitions.TryResolveRuntimeIds(
                        created.definitionCatalog,
                        out BuiltInTerrainBlockRuntimeIds runtimeIds))
                {
                    created.Dispose();
                    catalogs = null;
                    return false;
                }

                created.RuntimeIds = runtimeIds;
                catalogs = created;
                return true;
            }
            catch
            {
                // 途中のBuilderが例外を返した場合も、すでに完成したPersistent Blobを残しません。
                created.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 依存するPaletteから逆順に、所有するPersistent Blobをすべて破棄します。
        /// ThreadPool処理の完了を待った呼出し元が一度だけ実行し、破棄後のBlobを再利用しません。
        /// </summary>
        public void Dispose()
        {
            if (paletteCatalog.IsCreated)
            {
                paletteCatalog.Dispose();
                paletteCatalog = default;
            }
            if (materialCatalog.IsCreated)
            {
                materialCatalog.Dispose();
                materialCatalog = default;
            }
            if (terrainCatalog.IsCreated)
            {
                terrainCatalog.Dispose();
                terrainCatalog = default;
            }
            if (definitionCatalog.IsCreated)
            {
                definitionCatalog.Dispose();
                definitionCatalog = default;
            }

            RuntimeIds = default;
        }
    }

    /// <summary>
    /// Editor診断用に再構築した1論理チャンクの完成地形、水文セル、表流水、水文タイルを所有します。
    /// <see cref="Dispose"/>を呼ぶまでPersistent NativeArrayを保持するため、usingで寿命を限定して使用します。
    /// </summary>
    public sealed class HydrologyDiagnosticChunk : IDisposable
    {
        /// <summary>生成対象のレイヤー付き論理チャンク座標です。構築後は変更されません。</summary>
        public ChunkCoordinate Coordinate { get; }

        /// <summary>行優先4096件のC5-C確定地形です。診断器が所有し、Dispose時に破棄します。</summary>
        public NativeArray<GeneratedTile> TerrainTiles { get; private set; }

        /// <summary>行優先4096件の完成済み自然生成Blockセルです。診断器が所有し、Dispose時に破棄します。</summary>
        public NativeArray<GeneratedTerrainCell> TerrainCells { get; private set; }

        /// <summary>完成セルが参照する淡水、海水、マグマ等の組み込みRuntime IDです。</summary>
        public BuiltInTerrainBlockRuntimeIds TerrainRuntimeIds { get; private set; }

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
                TerrainCells = new NativeArray<GeneratedTerrainCell>(
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
        /// C3、C4-A〜D、C5-Cの既存Generatorを製品と同じ順序で実行し、完成診断チャンクを生成します。
        /// 呼出し元は成功時のchunkをusingで破棄し、失敗時はstageを監査エラーへ記録します。
        /// </summary>
        /// <param name="settings">Seed、GeneratorVersion、全固定生成設定です。</param>
        /// <param name="biomeSettings">C5-Cで使用する新18Biome Definitionです。</param>
        /// <param name="coordinate">生成するSurface論理チャンク座標です。</param>
        /// <param name="terrainCatalogs">Main Threadで完成し、呼出し中は破棄されない読み取り専用地形カタログです。</param>
        /// <param name="chunk">成功時に完成配列を所有する診断チャンクです。失敗時はnullです。</param>
        /// <param name="stage">成功時はComplete、失敗時は停止した生成段階です。</param>
        /// <returns>全5出力を部分結果なしで完成できた場合はtrueです。</returns>
        public static bool TryGenerate(
            in WorldGenerationSettings settings,
            in BiomeClassificationSettings biomeSettings,
            ChunkCoordinate coordinate,
            HydrologyDiagnosticTerrainCatalogs terrainCatalogs,
            out HydrologyDiagnosticChunk chunk,
            out HydrologyDiagnosticGenerationStage stage)
        {
            chunk = null;
            stage = HydrologyDiagnosticGenerationStage.UnsupportedLayer;
            if (coordinate.Layer != WorldLayer.Surface)
            {
                return false;
            }

            stage = HydrologyDiagnosticGenerationStage.TerrainCatalog;
            if (terrainCatalogs == null || !terrainCatalogs.IsCreated)
            {
                return false;
            }

            HydrologyDiagnosticChunk generated = new HydrologyDiagnosticChunk(coordinate);
            try
            {
                generated.TerrainRuntimeIds = terrainCatalogs.RuntimeIds;
                if (!TryPopulate(
                        settings,
                        biomeSettings,
                        terrainCatalogs.PaletteCatalog,
                        generated,
                        out stage))
                {
                    generated.Dispose();
                    return false;
                }
            }
            catch
            {
                // 作業配列側のusingだけでは完成出力5配列は解放されないため、
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
        /// <param name="biomeSettings">製品Worldと同じ新18Biome Definitionです。</param>
        /// <param name="paletteCatalog">Main Threadで完成済みの自然生成BlockパレットBlobです。</param>
        /// <param name="chunk">成功時だけ公開する5出力配列の所有者です。</param>
        /// <param name="stage">最後に開始した生成段階です。</param>
        /// <returns>C5-Cまでの全処理が成功した場合はtrueです。</returns>
        private static bool TryPopulate(
            in WorldGenerationSettings settings,
            in BiomeClassificationSettings biomeSettings,
            in BlobAssetReference<TerrainBlockPaletteCatalogBlob> paletteCatalog,
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

            stage = HydrologyDiagnosticGenerationStage.BiomeAndTerrainFinalization;
            // 基礎地形配列をその場で確定結果へ置き換えます。NativeArrayは値型なので、
            // プロパティの添字へ直接代入せず、同じ領域を指すローカル値を通して書き戻します。
            NativeArray<GeneratedTile> finalizedTerrainTiles = chunk.TerrainTiles;
            NativeArray<GeneratedTerrainCell> finalizedTerrainCells = chunk.TerrainCells;
            for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
            {
                for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
                {
                    int tileIndex = WorldGrid.ToTileIndex(localX, localY);
                    int worldTileX = WorldGrid.ToWorldTileX(chunk.Coordinate.X, localX);
                    int worldTileY = WorldGrid.ToWorldTileY(chunk.Coordinate.Y, localY);
                    if (!DeterministicBiomeChunkFinalizer.TryFinalizeTile(
                            settings,
                            biomeSettings,
                            worldTileX,
                            worldTileY,
                            finalizedTerrainTiles[tileIndex],
                            chunk.HydrologyTiles[tileIndex],
                            chunk.SurfaceWaterTiles[tileIndex],
                            out GeneratedTile finalizedTile))
                    {
                        // 未定義IDや候補なしを旧Biomeへ読み替えず、診断チャンク全体を失敗させます。
                        return false;
                    }

                    if (!DeterministicTerrainCellFinalizer.TryFinalizeSurfaceCell(
                            settings,
                            paletteCatalog,
                            chunk.TerrainRuntimeIds,
                            worldTileX,
                            worldTileY,
                            finalizedTile,
                            chunk.HydrologyTiles[tileIndex],
                            chunk.SurfaceWaterTiles[tileIndex],
                            out GeneratedTerrainCell terrainCell))
                    {
                        return false;
                    }

                    // 製品Jobと同じBlockセル由来の暫定資源適性をHover用環境タイルへ反映します。
                    finalizedTile.ResourceSuitability =
                        DeterministicTerrainCellFinalizer.ClassifyInterimResourceSuitability(
                            terrainCell);
                    finalizedTerrainTiles[tileIndex] = finalizedTile;
                    finalizedTerrainCells[tileIndex] = terrainCell;
                }
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
            if (TerrainCells.IsCreated)
            {
                NativeArray<GeneratedTerrainCell> terrainCells = TerrainCells;
                terrainCells.Dispose();
                TerrainCells = default;
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
