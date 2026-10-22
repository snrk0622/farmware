// このファイルは、TERRAIN-01C4-B2の地下水式、自然土壌飽和式、9×9境界入力、ECS用出力を検証します。
// 現在水文やPresentationを作らず、固定地形・固定水文・表流水から完成結果へ至る純粋計算を保証します。

using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 涵養最大、固定9地点の補正後地質平均、地下水接続境界、タイル別自然土壌飽和を検証します。
    /// ChunkGenerationSystemの固定Tick一括公開はWorldGenerationTestsで別に保証します。
    /// </summary>
    public sealed class DeterministicGroundwaterHydrologyGeneratorTests
    {
        /// <summary>
        /// ECSへ4096件格納する要素を準備し、初回実装が1タイル2 bytesだけを保持することを保証します。
        /// </summary>
        [Test]
        public void GeneratedHydrologyTile_SizeMatchesCapacityDocumentation()
        {
            Assert.That(UnsafeUtility.SizeOf<GeneratedHydrologyTile>(), Is.EqualTo(2));
        }

        /// <summary>
        /// 涵養候補なしの人工入力を準備し、湿度と流出寄与の大きい方、透水性と保持力の小さい方を使うことを保証します。
        /// </summary>
        [Test]
        public void GroundwaterFormula_UsesRechargeMaximumAndAquiferMinimum()
        {
            GroundwaterGenerationSettings settings = GroundwaterGenerationSettings.CreateDefault();

            Assert.That(
                DeterministicGroundwaterHydrologyGenerator.TryCalculateGroundwaterPotential(
                    settings,
                    10000,
                    20,
                    false,
                    50000,
                    30000,
                    out ushort actual),
                Is.True);

            long expected = (40960L * 30000L) / ushort.MaxValue;
            Assert.That(actual, Is.EqualTo((ushort)expected));
        }

        /// <summary>
        /// 自然表流水ありと境界直前・境界値の帯水係数を準備し、涵養65,535と接続境界32,768を保証します。
        /// </summary>
        /// <param name="aquiferFactorValue">地下水ポテンシャルと一致させる32bitテスト入力です。</param>
        /// <param name="expectedConnected">接続境界を満たすと期待する場合はtrueです。</param>
        [TestCase(32767, false)]
        [TestCase(32768, true)]
        public void GroundwaterFormula_SurfaceRechargePreservesConnectionBoundary(
            int aquiferFactorValue,
            bool expectedConnected)
        {
            GroundwaterGenerationSettings settings = GroundwaterGenerationSettings.CreateDefault();
            ushort aquiferFactor = (ushort)aquiferFactorValue;

            Assert.That(
                DeterministicGroundwaterHydrologyGenerator.TryCalculateGroundwaterPotential(
                    settings,
                    0,
                    0,
                    true,
                    aquiferFactor,
                    aquiferFactor,
                    out ushort actual),
                Is.True);

            Assert.That(actual, Is.EqualTo(aquiferFactor));
            Assert.That(actual >= settings.GroundwaterConnectionThreshold, Is.EqualTo(expectedConnected));
        }

        /// <summary>
        /// 4入力を同じ値へそろえ、比重2：2：1：1と除数6が39,320／39,321の境界を保つことを保証します。
        /// </summary>
        /// <param name="expectedValue">飽和式の全入力と期待値へ使う32bitテスト入力です。</param>
        [TestCase(39320)]
        [TestCase(39321)]
        public void SoilSaturationFormula_PreservesThresholdBoundary(int expectedValue)
        {
            GroundwaterGenerationSettings settings = GroundwaterGenerationSettings.CreateDefault();
            ushort expected = (ushort)expectedValue;
            ushort complement = (ushort)(ushort.MaxValue - expected);

            Assert.That(
                DeterministicGroundwaterHydrologyGenerator.TryCalculateNaturalSoilSaturation(
                    settings,
                    expected,
                    expected,
                    complement,
                    complement,
                    out ushort actual),
                Is.True);

            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(
                actual >= settings.NaturalSoilSaturationThreshold,
                Is.EqualTo(expected == settings.NaturalSoilSaturationThreshold));
        }

        /// <summary>
        /// 最乾燥・最排水と最湿潤・最停滞の入力を準備し、自然土壌飽和式の0／65,535端点を保証します。
        /// </summary>
        [Test]
        public void SoilSaturationFormula_PreservesZeroAndMaximumEndpoints()
        {
            GroundwaterGenerationSettings settings = GroundwaterGenerationSettings.CreateDefault();

            Assert.That(
                DeterministicGroundwaterHydrologyGenerator.TryCalculateNaturalSoilSaturation(
                    settings,
                    0,
                    0,
                    ushort.MaxValue,
                    ushort.MaxValue,
                    out ushort dry),
                Is.True);
            Assert.That(dry, Is.Zero);

            Assert.That(
                DeterministicGroundwaterHydrologyGenerator.TryCalculateNaturalSoilSaturation(
                    settings,
                    ushort.MaxValue,
                    ushort.MaxValue,
                    0,
                    0,
                    out ushort saturated),
                Is.True);
            Assert.That(saturated, Is.EqualTo(ushort.MaxValue));
        }

        /// <summary>
        /// 負座標セルの実地質を9地点で個別計算し、製品APIが同じ整数平均と地下水式を使うことを保証します。
        /// </summary>
        [Test]
        public void CellGroundwater_UsesFixedNineAdjustedGeologySamples()
        {
            WorldGenerationSettings settings = CreateSettings("groundwater-nine-samples");
            Assert.That(
                DeterministicHydrologyGenerator.TryGenerateCell(
                    settings,
                    -3,
                    5,
                    out GeneratedHydrologyCell cell),
                Is.True);

            Assert.That(
                DeterministicGroundwaterHydrologyGenerator.TryGenerateCellGroundwater(
                    settings,
                    cell,
                    false,
                    out ushort actual),
                Is.True);

            long permeabilityTotal = 0L;
            long storageTotal = 0L;
            int centerX = cell.CellX * settings.Hydrology.CellSizeInTiles;
            int centerY = cell.CellY * settings.Hydrology.CellSizeInTiles;
            int offset = settings.Hydrology.RepresentativeSampleOffsetInTiles;
            for (int sampleY = -1; sampleY <= 1; sampleY++)
            {
                for (int sampleX = -1; sampleX <= 1; sampleX++)
                {
                    int worldTileX = centerX + (sampleX * offset);
                    int worldTileY = centerY + (sampleY * offset);
                    Assert.That(
                        DeterministicGeologyGenerator.TryGenerate(
                            settings,
                            worldTileX,
                            worldTileY,
                            out GeneratedGeologyData geology),
                        Is.True);
                    Assert.That(
                        DeterministicGroundwaterGenerator.TryGenerateAdjustedGeology(
                            settings,
                            worldTileX,
                            worldTileY,
                            geology.Permeability,
                            geology.GroundwaterStorage,
                            out AdjustedGroundwaterGeology adjusted),
                        Is.True);
                    permeabilityTotal += adjusted.Permeability;
                    storageTotal += adjusted.GroundwaterStorage;
                }
            }

            Assert.That(
                DeterministicGroundwaterHydrologyGenerator.TryCalculateGroundwaterPotential(
                    settings.Groundwater,
                    cell.HydrologyMoisture,
                    cell.RunoffEquivalentCells,
                    false,
                    (ushort)(permeabilityTotal / 9L),
                    (ushort)(storageTotal / 9L),
                    out ushort expected),
                Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// 製品と同じチャンク生成用配列を準備し、9×9境界セル、64地下水、4096飽和度が一括生成されることを保証します。
        /// </summary>
        [Test]
        [Timeout(180000)]
        public void ChunkGeneration_ProducesGroundwaterAndTileSaturationAcrossPositiveBoundary()
        {
            WorldGenerationSettings settings = CreateSettings("groundwater-chunk-integration");
            const int ChunkX = -1;
            const int ChunkY = 1;
            using var tiles = new NativeArray<GeneratedTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using var cells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicHydrologyGenerator.CellsPerChunk,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using var frontier = new NativeArray<HydrologyUpstreamCandidate>(
                DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using var basinMembers = new NativeArray<HydrologyUpstreamCandidate>(
                DeterministicHydrologyLakeResolver.MaximumBasinMembers,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using var basinLookup = new NativeArray<HydrologyBasinLookupSlot>(
                DeterministicHydrologyLakeResolver.LookupCapacity,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            using var pathWorkspace = new NativeArray<SurfaceWaterPathSearchNode>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using var pathTiles = new NativeArray<SurfaceWaterPathTile>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using var visited = new NativeArray<byte>(
                DeterministicSurfaceWaterGenerator.MaximumFloodTileCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using var queue = new NativeArray<int>(
                DeterministicSurfaceWaterGenerator.MaximumFloodTileCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using var intersectingCells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using var recharge = new NativeArray<SurfaceRechargeCandidate>(
                DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            using var surfaceWater = new NativeArray<GeneratedSurfaceWaterTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using var hydrologyTiles = new NativeArray<GeneratedHydrologyTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);

            Assert.That(FillTerrain(settings, ChunkX, ChunkY, tiles), Is.True);
            Assert.That(
                DeterministicHydrologyGenerator.TryGenerateChunkCells(
                    settings,
                    ChunkX,
                    ChunkY,
                    cells,
                    frontier,
                    basinMembers,
                    basinLookup),
                Is.True);
            Assert.That(
                DeterministicSurfaceWaterGenerator.TryGenerateChunk(
                    settings,
                    ChunkX,
                    ChunkY,
                    cells,
                    frontier,
                    basinMembers,
                    basinLookup,
                    pathWorkspace,
                    pathTiles,
                    visited,
                    queue,
                    intersectingCells,
                    recharge,
                    surfaceWater),
                Is.True);

            // 表流水Bufferに現れた涵養候補が、同じ絶対タイルを担当する9×9セルの中間フラグへ接続されることを確認します。
            int outputRechargeCandidateCount = 0;
            for (int tileIndex = 0; tileIndex < surfaceWater.Length; tileIndex++)
            {
                if ((surfaceWater[tileIndex].Flags & SurfaceWaterTileFlags.RechargeCandidate) == 0)
                {
                    continue;
                }

                outputRechargeCandidateCount++;
                int localX = tileIndex % WorldGridConstants.LogicalChunkSize;
                int localY = tileIndex / WorldGridConstants.LogicalChunkSize;
                Assert.That(
                    DeterministicHydrologyGenerator.TryGetCellCoordinate(
                        settings.Hydrology,
                        WorldGrid.ToWorldTileX(ChunkX, localX),
                        WorldGrid.ToWorldTileY(ChunkY, localY),
                        out int rechargeCellX,
                        out int rechargeCellY),
                    Is.True);
                int firstCellX = ChunkX * DeterministicHydrologyGenerator.CellsPerChunkAxis;
                int firstCellY = ChunkY * DeterministicHydrologyGenerator.CellsPerChunkAxis;
                int rechargeIndex =
                    ((rechargeCellY - firstCellY)
                        * DeterministicGroundwaterHydrologyGenerator.IntersectingCellsPerAxis)
                    + (rechargeCellX - firstCellX);
                Assert.That(recharge[rechargeIndex].HasRecharge, Is.EqualTo(1));
            }
            Assert.That(outputRechargeCandidateCount, Is.GreaterThan(0));

            int firstRechargeIndex = -1;
            ushort expectedRechargeGroundwater = default;
            for (int cellIndex = 0; cellIndex < recharge.Length; cellIndex++)
            {
                if (recharge[cellIndex].HasRecharge == 0)
                {
                    continue;
                }

                firstRechargeIndex = cellIndex;
                Assert.That(
                    DeterministicGroundwaterHydrologyGenerator.TryGenerateCellGroundwater(
                        settings,
                        intersectingCells[cellIndex],
                        true,
                        out expectedRechargeGroundwater),
                    Is.True);
                break;
            }
            Assert.That(firstRechargeIndex, Is.GreaterThanOrEqualTo(0));

            Assert.That(
                DeterministicGroundwaterHydrologyGenerator.TryGenerateChunk(
                    settings,
                    ChunkX,
                    ChunkY,
                    tiles,
                    cells,
                    intersectingCells,
                    recharge,
                    hydrologyTiles),
                Is.True);
            Assert.That(
                intersectingCells[firstRechargeIndex].GroundwaterPotential,
                Is.EqualTo(expectedRechargeGroundwater));

            for (int cellIndex = 0; cellIndex < cells.Length; cellIndex++)
            {
                GeneratedHydrologyCell cell = cells[cellIndex];
                Assert.That(
                    (cell.ConnectionFlags & HydrologyConnectionFlags.Groundwater) != 0,
                    Is.EqualTo(
                        cell.GroundwaterPotential
                            >= settings.Groundwater.GroundwaterConnectionThreshold));
            }

            // 正側末尾タイルは9個目の交差セルを参照します。8×8だけで丸める回帰をこの一点で検出します。
            const int BoundaryLocalX = 63;
            const int BoundaryLocalY = 63;
            int boundaryIndex = WorldGrid.ToTileIndex(BoundaryLocalX, BoundaryLocalY);
            GeneratedTile boundaryTile = tiles[boundaryIndex];
            GeneratedHydrologyCell positiveBoundaryCell = intersectingCells[
                (8 * DeterministicGroundwaterHydrologyGenerator.IntersectingCellsPerAxis) + 8];
            Assert.That(
                DeterministicGroundwaterGenerator.TryGenerateAdjustedGeology(
                    settings,
                    WorldGrid.ToWorldTileX(ChunkX, BoundaryLocalX),
                    WorldGrid.ToWorldTileY(ChunkY, BoundaryLocalY),
                    boundaryTile.GeologyPermeability,
                    boundaryTile.GeologyGroundwaterStorage,
                    out AdjustedGroundwaterGeology adjustedBoundary),
                Is.True);
            Assert.That(
                DeterministicGroundwaterHydrologyGenerator.TryCalculateNaturalSoilSaturation(
                    settings.Groundwater,
                    positiveBoundaryCell.HydrologyMoisture,
                    positiveBoundaryCell.GroundwaterPotential,
                    adjustedBoundary.Permeability,
                    boundaryTile.TerrainRuggedness,
                    out ushort expectedBoundarySaturation),
                Is.True);
            Assert.That(
                hydrologyTiles[boundaryIndex].NaturalSoilSaturationPotential,
                Is.EqualTo(expectedBoundarySaturation));
        }

        /// <summary>
        /// 製品と同じ同期生成経路を1／4／16チャンクで実行し、表流水までとC4-B2追加後のEditor CPU時間を比較記録します。
        /// 通常のテスト実行時間を増やさないため明示実行とし、絶対的な合否値ではなく同一環境での差分だけを出力します。
        /// </summary>
        [Test]
        [Explicit("C4-B2の性能記録を更新するときだけ実行します。")]
        [Timeout(300000)]
        public void GroundwaterHydrologyGeneration_ReportsSurfaceWaterComparison()
        {
            WorldGenerationSettings settings = CreateSettings("groundwater-performance");
            using var tiles = new NativeArray<GeneratedTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var cells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicHydrologyGenerator.CellsPerChunk,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
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
            using var pathWorkspace = new NativeArray<SurfaceWaterPathSearchNode>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var pathTiles = new NativeArray<SurfaceWaterPathTile>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var visited = new NativeArray<byte>(
                DeterministicSurfaceWaterGenerator.MaximumFloodTileCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var queue = new NativeArray<int>(
                DeterministicSurfaceWaterGenerator.MaximumFloodTileCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var intersectingCells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var recharge = new NativeArray<SurfaceRechargeCandidate>(
                DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            using var surfaceWater = new NativeArray<GeneratedSurfaceWaterTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var hydrologyTiles = new NativeArray<GeneratedHydrologyTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);

            // 初回だけ発生するBurstコンパイルやキャッシュ初期化を計測から除き、各段階の比較条件をそろえます。
            Assert.That(
                TryGenerateMeasuredChunk(
                    settings,
                    0,
                    0,
                    true,
                    tiles,
                    cells,
                    frontier,
                    basinMembers,
                    basinLookup,
                    pathWorkspace,
                    pathTiles,
                    visited,
                    queue,
                    intersectingCells,
                    recharge,
                    surfaceWater,
                    hydrologyTiles),
                Is.True);

            int[] chunkCounts = { 1, 4, 16 };
            for (int countIndex = 0; countIndex < chunkCounts.Length; countIndex++)
            {
                int chunkCount = chunkCounts[countIndex];
                bool surfaceSucceeded = true;
                var surfaceStopwatch = System.Diagnostics.Stopwatch.StartNew();
                for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
                {
                    // 同じ16チャンク座標を各段階へ入力し、地形差ではなくC4-B2処理の追加分を比較します。
                    int chunkX = (chunkIndex % 4) - 2;
                    int chunkY = (chunkIndex / 4) - 2;
                    if (!TryGenerateMeasuredChunk(
                            settings,
                            chunkX,
                            chunkY,
                            false,
                            tiles,
                            cells,
                            frontier,
                            basinMembers,
                            basinLookup,
                            pathWorkspace,
                            pathTiles,
                            visited,
                            queue,
                            intersectingCells,
                            recharge,
                            surfaceWater,
                            hydrologyTiles))
                    {
                        surfaceSucceeded = false;
                        break;
                    }
                }
                surfaceStopwatch.Stop();
                Assert.That(surfaceSucceeded, Is.True);

                bool groundwaterSucceeded = true;
                var groundwaterStopwatch = System.Diagnostics.Stopwatch.StartNew();
                for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
                {
                    int chunkX = (chunkIndex % 4) - 2;
                    int chunkY = (chunkIndex / 4) - 2;
                    if (!TryGenerateMeasuredChunk(
                            settings,
                            chunkX,
                            chunkY,
                            true,
                            tiles,
                            cells,
                            frontier,
                            basinMembers,
                            basinLookup,
                            pathWorkspace,
                            pathTiles,
                            visited,
                            queue,
                            intersectingCells,
                            recharge,
                            surfaceWater,
                            hydrologyTiles))
                    {
                        groundwaterSucceeded = false;
                        break;
                    }
                }
                groundwaterStopwatch.Stop();
                Assert.That(groundwaterSucceeded, Is.True);

                double addedMilliseconds =
                    groundwaterStopwatch.Elapsed.TotalMilliseconds - surfaceStopwatch.Elapsed.TotalMilliseconds;
                UnityEngine.Debug.Log(
                    $"[TERRAIN-01C4-B2] chunks={chunkCount}, "
                    + $"surfaceMs={surfaceStopwatch.Elapsed.TotalMilliseconds:F3}, "
                    + $"withGroundwaterMs={groundwaterStopwatch.Elapsed.TotalMilliseconds:F3}, "
                    + $"addedMs={addedMilliseconds:F3}");
            }
        }

        /// <summary>
        /// 異なる飽和値を同じ基礎ハッシュへ追加し、状態ハッシュが新しいECS Bufferの差を検出することを保証します。
        /// </summary>
        [Test]
        public void HydrologyTileHash_ChangesWhenSaturationChanges()
        {
            const ulong InitialHash = 0xCBF29CE484222325UL;
            var dry = new GeneratedHydrologyTile { NaturalSoilSaturationPotential = 39320 };
            var saturated = new GeneratedHydrologyTile { NaturalSoilSaturationPotential = 39321 };

            ulong dryHash = DeterministicGroundwaterHydrologyGenerator.HashTile(InitialHash, dry);
            ulong saturatedHash = DeterministicGroundwaterHydrologyGenerator.HashTile(InitialHash, saturated);

            Assert.That(dryHash, Is.Not.EqualTo(saturatedHash));
        }

        /// <summary>テスト名から安定Seedを作り、製品既定のGeneratorVersion 1設定を返します。</summary>
        /// <param name="seedText">テストケース間の乱数列を分離する固定文字列です。</param>
        /// <returns>C4-B2までの既定値を持つ固定ワールド生成設定です。</returns>
        private static WorldGenerationSettings CreateSettings(string seedText)
        {
            return WorldGenerationSettings.CreateDefault(WorldSeedUtility.FromString(seedText), 1);
        }

        /// <summary>製品の地質キャッシュ経路で1チャンク4096地形タイルを生成します。</summary>
        /// <param name="settings">Seed、版、環境、地質の固定設定です。</param>
        /// <param name="chunkX">水平論理チャンク座標です。</param>
        /// <param name="chunkY">垂直論理チャンク座標です。</param>
        /// <param name="tiles">呼出し側が所有する4096件の出力配列です。</param>
        /// <returns>キャッシュと全タイルを生成できた場合はtrueです。</returns>
        private static bool FillTerrain(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            NativeArray<GeneratedTile> tiles)
        {
            if (!DeterministicGeologyGenerator.TryCreateChunkCache(
                    settings,
                    chunkX,
                    chunkY,
                    out GeologyChunkCache geologyCache))
            {
                return false;
            }

            for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
            {
                for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
                {
                    int index = WorldGrid.ToTileIndex(localX, localY);
                    if (!DeterministicChunkGenerator.TryGenerateTile(
                            settings,
                            geologyCache,
                            WorldGrid.ToWorldTileX(chunkX, localX),
                            WorldGrid.ToWorldTileY(chunkY, localY),
                            out GeneratedTile tile))
                    {
                        return false;
                    }
                    tiles[index] = tile;
                }
            }
            return true;
        }

        /// <summary>性能計測用の1チャンクを、地形、水文、表流水、任意の地下水・自然土壌飽和の順で同期生成します。</summary>
        /// <param name="settings">全段階で共有する固定ワールド生成設定です。</param>
        /// <param name="chunkX">計測対象の水平論理チャンク座標です。</param>
        /// <param name="chunkY">計測対象の垂直論理チャンク座標です。</param>
        /// <param name="includeGroundwater">C4-B2段階まで実行する場合はtrue、C4-A比較値ではfalseです。</param>
        /// <param name="tiles">再利用する4096件の地形出力です。</param>
        /// <param name="cells">再利用する64件のチャンク内水文セル出力です。</param>
        /// <param name="frontier">集水域探索の再利用ワーク領域です。</param>
        /// <param name="basinMembers">湖沼流域探索の再利用ワーク領域です。</param>
        /// <param name="basinLookup">湖沼流域探索の再利用索引です。</param>
        /// <param name="pathWorkspace">河川経路探索の再利用ワーク領域です。</param>
        /// <param name="pathTiles">河川経路の再利用出力領域です。</param>
        /// <param name="visited">湖沼塗りつぶしの再利用訪問印です。</param>
        /// <param name="queue">湖沼塗りつぶしの再利用待ち行列です。</param>
        /// <param name="intersectingCells">地下水用9×9交差セル出力です。</param>
        /// <param name="recharge">自然表流水涵養候補の9×9フラグ出力です。</param>
        /// <param name="surfaceWater">4096件の表流水出力です。</param>
        /// <param name="hydrologyTiles">4096件の自然土壌飽和出力です。</param>
        /// <returns>要求した全段階が成功した場合はtrueです。</returns>
        private static bool TryGenerateMeasuredChunk(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            bool includeGroundwater,
            NativeArray<GeneratedTile> tiles,
            NativeArray<GeneratedHydrologyCell> cells,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            NativeArray<SurfaceWaterPathSearchNode> pathWorkspace,
            NativeArray<SurfaceWaterPathTile> pathTiles,
            NativeArray<byte> visited,
            NativeArray<int> queue,
            NativeArray<GeneratedHydrologyCell> intersectingCells,
            NativeArray<SurfaceRechargeCandidate> recharge,
            NativeArray<GeneratedSurfaceWaterTile> surfaceWater,
            NativeArray<GeneratedHydrologyTile> hydrologyTiles)
        {
            if (!FillTerrain(settings, chunkX, chunkY, tiles)
                || !DeterministicHydrologyGenerator.TryGenerateChunkCells(
                    settings,
                    chunkX,
                    chunkY,
                    cells,
                    frontier,
                    basinMembers,
                    basinLookup)
                || !DeterministicSurfaceWaterGenerator.TryGenerateChunk(
                    settings,
                    chunkX,
                    chunkY,
                    cells,
                    frontier,
                    basinMembers,
                    basinLookup,
                    pathWorkspace,
                    pathTiles,
                    visited,
                    queue,
                    intersectingCells,
                    recharge,
                    surfaceWater))
            {
                return false;
            }

            // 比較元ではC4-Aまでで止め、比較先だけ地下水と自然土壌飽和を同じ入力から追加生成します。
            return !includeGroundwater
                || DeterministicGroundwaterHydrologyGenerator.TryGenerateChunk(
                    settings,
                    chunkX,
                    chunkY,
                    tiles,
                    cells,
                    intersectingCells,
                    recharge,
                    hydrologyTiles);
        }
    }
}
