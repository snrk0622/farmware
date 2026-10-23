// このファイルは、C4-A2の渓流・河川幅、湖面候補、4096件の固定表流水Buffer生成を検証します。
// C4-A1の中心線とC3の固定水文を入力にし、海面除外、負座標、チャンク境界、再現性、有限作業上限を保証します。

using System.Diagnostics;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 河道幅の形、湖面候補の属性、ECSへ渡す固定長結果、状態ハッシュ、性能比較を検証するEditModeテストです。
    /// 現在水位や旧GeneratedTile.WaterBodyTypeは入力にせず、固定生成結果だけを対象にします。
    /// </summary>
    public sealed class DeterministicSurfaceWaterGeneratorTests
    {
        /// <summary>
        /// ECS表流水と固定経路ノードの要素サイズを固定し、Persistent作業容量とロード済みChunk容量の文書ずれを検出します。
        /// </summary>
        [Test]
        public void SurfaceWaterStructSizes_MatchDocumentedCapacity()
        {
            Assert.That(UnsafeUtility.SizeOf<GeneratedSurfaceWaterTile>(), Is.EqualTo(16));
            Assert.That(UnsafeUtility.SizeOf<SurfaceWaterPathTile>(), Is.EqualTo(8));
            Assert.That(UnsafeUtility.SizeOf<SurfaceWaterPathSearchNode>(), Is.EqualTo(48));
        }

        /// <summary>
        /// 64×64チャンクの正側末尾4タイルを9個目の水文セルが担当し、河道供給元はさらに1セル外側まで走査することを保証します。
        /// </summary>
        [Test]
        public void HydrologyScanBounds_IncludePositiveEdgeLakeOwnersAndOuterChannelSources()
        {
            WorldGenerationSettings settings = CreateSettings("surface-water-scan-bounds");
            Assert.That(
                DeterministicSurfaceWaterGenerator.TryGetHydrologyScanBounds(
                    settings.Hydrology,
                    false,
                    0,
                    0,
                    WorldGridConstants.LogicalChunkSize - 1,
                    WorldGridConstants.LogicalChunkSize - 1,
                    out int minimumIntersectingCellX,
                    out int minimumIntersectingCellY,
                    out int maximumIntersectingCellX,
                    out int maximumIntersectingCellY,
                    out int minimumChannelSourceCellX,
                    out int minimumChannelSourceCellY,
                    out int maximumChannelSourceCellX,
                    out int maximumChannelSourceCellY),
                Is.True);

            Assert.That(minimumIntersectingCellX, Is.EqualTo(0));
            Assert.That(minimumIntersectingCellY, Is.EqualTo(0));
            Assert.That(maximumIntersectingCellX, Is.EqualTo(8));
            Assert.That(maximumIntersectingCellY, Is.EqualTo(8));
            Assert.That(minimumChannelSourceCellX, Is.EqualTo(-1));
            Assert.That(minimumChannelSourceCellY, Is.EqualTo(-1));
            Assert.That(maximumChannelSourceCellX, Is.EqualTo(9));
            Assert.That(maximumChannelSourceCellY, Is.EqualTo(9));
        }

        /// <summary>
        /// 地下水涵養を生成するときは河川幅が2セル先の担当範囲へ入る供給元まで走査し、
        /// 同じ境界セルの涵養結果が生成元チャンクによって変わらないことを保証します。
        /// </summary>
        [Test]
        public void HydrologyScanBounds_GroundwaterRechargeIncludesSecondOuterChannelSource()
        {
            WorldGenerationSettings settings = CreateSettings("groundwater-recharge-scan-bounds");
            Assert.That(
                DeterministicSurfaceWaterGenerator.TryGetHydrologyScanBounds(
                    settings.Hydrology,
                    true,
                    0,
                    0,
                    WorldGridConstants.LogicalChunkSize - 1,
                    WorldGridConstants.LogicalChunkSize - 1,
                    out int minimumIntersectingCellX,
                    out int minimumIntersectingCellY,
                    out int maximumIntersectingCellX,
                    out int maximumIntersectingCellY,
                    out int minimumChannelSourceCellX,
                    out int minimumChannelSourceCellY,
                    out int maximumChannelSourceCellX,
                    out int maximumChannelSourceCellY),
                Is.True);

            // 交差セル0〜8は変えず、涵養の供給元だけを2セル外側の-2〜10まで広げます。
            // 例えばセル6→7の河川がタイル59で終わると、幅のタイル60はセル8を担当するため、セル6も必要です。
            Assert.That(minimumIntersectingCellX, Is.EqualTo(0));
            Assert.That(minimumIntersectingCellY, Is.EqualTo(0));
            Assert.That(maximumIntersectingCellX, Is.EqualTo(8));
            Assert.That(maximumIntersectingCellY, Is.EqualTo(8));
            Assert.That(minimumChannelSourceCellX, Is.EqualTo(-2));
            Assert.That(minimumChannelSourceCellY, Is.EqualTo(-2));
            Assert.That(maximumChannelSourceCellX, Is.EqualTo(10));
            Assert.That(maximumChannelSourceCellY, Is.EqualTo(10));
        }

        /// <summary>
        /// 同じ1本のD8辺をStreamとして描き、海面以上の中心線だけが渓流になり、幅を持たないことを保証します。
        /// </summary>
        [Test]
        public void StreamRasterization_UsesCenterLineOnly()
        {
            WorldGenerationSettings settings = CreateSettings("surface-water-stream-width");
            using var pathWorkspace = CreatePathWorkspace();
            using var pathTiles = CreatePathTiles();
            Assert.That(
                TryFindRasterizableSource(
                    settings,
                    false,
                    pathWorkspace,
                    pathTiles,
                    out GeneratedHydrologyCell source,
                    out int chunkX,
                    out int chunkY,
                    out int pathLength),
                Is.True);
            source.ConnectionFlags |= HydrologyConnectionFlags.Stream;
            source.ConnectionFlags &= ~HydrologyConnectionFlags.River;
            source.RunoffEquivalentCells = 7;

            using var surfaceWater = new NativeArray<GeneratedSurfaceWaterTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            Assert.That(
                DeterministicSurfaceWaterGenerator.TryRasterizeChannel(
                    settings,
                    source,
                    chunkX,
                    chunkY,
                    pathWorkspace,
                    pathTiles,
                    surfaceWater),
                Is.True);

            int expectedCount = 0;
            for (int index = 0; index < pathLength; index++)
            {
                SurfaceWaterPathTile pathTile = pathTiles[index];
                if (!TryGetEligibleOutputIndex(settings, chunkX, chunkY, pathTile.WorldTileX,
                        pathTile.WorldTileY, out int outputIndex))
                {
                    continue;
                }

                expectedCount++;
                GeneratedSurfaceWaterTile actual = surfaceWater[outputIndex];
                Assert.That(actual.Type, Is.EqualTo(SurfaceWaterTileType.Stream));
                Assert.That(actual.Flags & SurfaceWaterTileFlags.ChannelCenterLine, Is.Not.Zero);
                Assert.That(actual.SourceAmount, Is.EqualTo(7));
            }

            Assert.That(expectedCount, Is.GreaterThan(0));
            Assert.That(CountType(surfaceWater, SurfaceWaterTileType.Stream), Is.EqualTo(expectedCount));
        }

        /// <summary>
        /// 同じ中心線をRiverとして描き、中心と上下左右1タイルだけが候補となり、斜め角を追加しないことを保証します。
        /// </summary>
        [Test]
        public void RiverRasterization_AddsOnlyOrthogonalWidth()
        {
            WorldGenerationSettings settings = CreateSettings("surface-water-river-width");
            using var pathWorkspace = CreatePathWorkspace();
            using var pathTiles = CreatePathTiles();
            Assert.That(
                TryFindRasterizableSource(
                    settings,
                    false,
                    pathWorkspace,
                    pathTiles,
                    out GeneratedHydrologyCell source,
                    out int chunkX,
                    out int chunkY,
                    out int pathLength),
                Is.True);
            source.ConnectionFlags |= HydrologyConnectionFlags.Stream | HydrologyConnectionFlags.River;
            source.RunoffEquivalentCells = 64;

            using var surfaceWater = new NativeArray<GeneratedSurfaceWaterTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            Assert.That(
                DeterministicSurfaceWaterGenerator.TryRasterizeChannel(
                    settings,
                    source,
                    chunkX,
                    chunkY,
                    pathWorkspace,
                    pathTiles,
                    surfaceWater),
                Is.True);

            var expected = new bool[WorldGridConstants.TilesPerChunk];
            for (int pathIndex = 0; pathIndex < pathLength; pathIndex++)
            {
                SurfaceWaterPathTile pathTile = pathTiles[pathIndex];
                MarkEligible(settings, chunkX, chunkY, pathTile.WorldTileX, pathTile.WorldTileY, expected);
                MarkEligible(settings, chunkX, chunkY, pathTile.WorldTileX - 1, pathTile.WorldTileY, expected);
                MarkEligible(settings, chunkX, chunkY, pathTile.WorldTileX + 1, pathTile.WorldTileY, expected);
                MarkEligible(settings, chunkX, chunkY, pathTile.WorldTileX, pathTile.WorldTileY - 1, expected);
                MarkEligible(settings, chunkX, chunkY, pathTile.WorldTileX, pathTile.WorldTileY + 1, expected);
            }

            int expectedCount = 0;
            for (int tileIndex = 0; tileIndex < expected.Length; tileIndex++)
            {
                if (expected[tileIndex])
                {
                    expectedCount++;
                    Assert.That(surfaceWater[tileIndex].Type, Is.EqualTo(SurfaceWaterTileType.River));
                }
                else
                {
                    Assert.That(surfaceWater[tileIndex].Type, Is.EqualTo(SurfaceWaterTileType.None));
                }
            }

            Assert.That(expectedCount, Is.GreaterThan(pathLength));
            Assert.That(CountType(surfaceWater, SurfaceWaterTileType.River), Is.EqualTo(expectedCount));
        }

        /// <summary>
        /// 別供給元の河川幅と渓流中心線を両方の適用順で重ね、代表河道の優先順位と中心線属性を独立して保持することを保証します。
        /// </summary>
        [Test]
        public void ChannelOverlap_PreservesAnyCenterLineWithoutChangingRepresentative()
        {
            WorldGenerationSettings settings = CreateSettings("surface-water-center-line-overlap");
            Assert.That(
                TryFindEligibleTile(settings, out int worldTileX, out int worldTileY),
                Is.True);
            ChunkCoordinate owner = WorldGrid.ToChunkCoordinate(worldTileX, worldTileY);
            int minimumTileX = WorldGrid.ToWorldTileX(owner.X, 0);
            int minimumTileY = WorldGrid.ToWorldTileY(owner.Y, 0);
            int maximumTileX = WorldGrid.ToWorldTileX(
                owner.X,
                WorldGridConstants.LogicalChunkSize - 1);
            int maximumTileY = WorldGrid.ToWorldTileY(
                owner.Y,
                WorldGridConstants.LogicalChunkSize - 1);
            var strongerRiver = new GeneratedHydrologyCell
            {
                CellX = 10,
                CellY = 20,
                RunoffEquivalentCells = 128
            };
            var weakerStream = new GeneratedHydrologyCell
            {
                CellX = -10,
                CellY = -20,
                RunoffEquivalentCells = 16
            };

            using var riverThenStream = new NativeArray<GeneratedSurfaceWaterTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            Assert.That(
                DeterministicSurfaceWaterGenerator.TryApplyChannelTile(
                    settings,
                    worldTileX,
                    worldTileY,
                    false,
                    true,
                    strongerRiver,
                    minimumTileX,
                    minimumTileY,
                    maximumTileX,
                    maximumTileY,
                    riverThenStream),
                Is.True);
            Assert.That(
                DeterministicSurfaceWaterGenerator.TryApplyChannelTile(
                    settings,
                    worldTileX,
                    worldTileY,
                    true,
                    false,
                    weakerStream,
                    minimumTileX,
                    minimumTileY,
                    maximumTileX,
                    maximumTileY,
                    riverThenStream),
                Is.True);
            AssertRepresentativeRiverWithCenterLine(
                riverThenStream,
                worldTileX - minimumTileX,
                worldTileY - minimumTileY,
                strongerRiver);

            using var streamThenRiver = new NativeArray<GeneratedSurfaceWaterTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            Assert.That(
                DeterministicSurfaceWaterGenerator.TryApplyChannelTile(
                    settings,
                    worldTileX,
                    worldTileY,
                    true,
                    false,
                    weakerStream,
                    minimumTileX,
                    minimumTileY,
                    maximumTileX,
                    maximumTileY,
                    streamThenRiver),
                Is.True);
            Assert.That(
                DeterministicSurfaceWaterGenerator.TryApplyChannelTile(
                    settings,
                    worldTileX,
                    worldTileY,
                    false,
                    true,
                    strongerRiver,
                    minimumTileX,
                    minimumTileY,
                    maximumTileX,
                    maximumTileY,
                    streamThenRiver),
                Is.True);
            AssertRepresentativeRiverWithCenterLine(
                streamThenRiver,
                worldTileX - minimumTileX,
                worldTileY - minimumTileY,
                strongerRiver);
        }

        /// <summary>
        /// 負座標チャンクを2回生成し、4096件の全固定値が一致し、表流水が海面未満へ置かれないことを保証します。
        /// </summary>
        [Test]
        public void ChunkGeneration_NegativeCoordinate_IsDeterministicAndExcludesSea()
        {
            WorldGenerationSettings settings = CreateSettings("ecs-system-test");
            using var first = new NativeArray<GeneratedSurfaceWaterTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using var second = new NativeArray<GeneratedSurfaceWaterTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);

            Assert.That(TryGenerateSurfaceWater(settings, 1, -1, first), Is.True);
            Assert.That(TryGenerateSurfaceWater(settings, 1, -1, second), Is.True);
            for (int index = 0; index < first.Length; index++)
            {
                AssertSurfaceWaterEqual(first[index], second[index], index);
                if (first[index].Type == SurfaceWaterTileType.None)
                {
                    continue;
                }

                int localX = index % WorldGridConstants.LogicalChunkSize;
                int localY = index / WorldGridConstants.LogicalChunkSize;
                int worldTileX = WorldGrid.ToWorldTileX(1, localX);
                int worldTileY = WorldGrid.ToWorldTileY(-1, localY);
                Assert.That(
                    DeterministicEnvironmentGenerator.TryGenerateBaseClimate(
                        settings,
                        worldTileX,
                        worldTileY,
                        out GeneratedEnvironmentData environment),
                    Is.True);
                Assert.That(environment.Elevation, Is.GreaterThanOrEqualTo(settings.SeaLevelThreshold));
            }
        }

        /// <summary>
        /// 1水文セルの固定流域と湖面を準備し、末端最低点から到達する湖面未満タイルだけが連続候補になることを保証します。
        /// </summary>
        [Test]
        public void LakeCandidate_UsesConnectedBasinSurfaceValues()
        {
            WorldGenerationSettings settings = CreateSettings("surface-water-lake-candidate");
            int sinkCellX = 0;
            int sinkCellY = 0;
            SurfaceWaterPathTile anchor = default;
            ushort anchorElevation = default;
            bool foundSink = false;
            for (int cellY = -16; cellY <= 16 && !foundSink; cellY++)
            {
                for (int cellX = -16; cellX <= 16 && !foundSink; cellX++)
                {
                    if (DeterministicSurfaceWaterPathGenerator.TryFindChannelAnchor(
                            settings,
                            cellX,
                            cellY,
                            out SurfaceWaterPathTile candidateAnchor,
                            out ushort candidateElevation)
                        && candidateElevation >= settings.SeaLevelThreshold
                        && candidateElevation <= ushort.MaxValue - 1024)
                    {
                        sinkCellX = cellX;
                        sinkCellY = cellY;
                        anchor = candidateAnchor;
                        anchorElevation = candidateElevation;
                        foundSink = true;
                    }
                }
            }
            Assert.That(foundSink, Is.True);

            ChunkCoordinate owner = WorldGrid.ToChunkCoordinate(anchor.WorldTileX, anchor.WorldTileY);
            int minimumTileX = WorldGrid.ToWorldTileX(owner.X, 0);
            int minimumTileY = WorldGrid.ToWorldTileY(owner.Y, 0);
            int maximumTileX = WorldGrid.ToWorldTileX(owner.X, WorldGridConstants.LogicalChunkSize - 1);
            int maximumTileY = WorldGrid.ToWorldTileY(owner.Y, WorldGridConstants.LogicalChunkSize - 1);
            using var surfaceWater = new NativeArray<GeneratedSurfaceWaterTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            using var basinMembers = new NativeArray<HydrologyUpstreamCandidate>(
                DeterministicHydrologyLakeResolver.MaximumBasinMembers,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using var basinLookup = new NativeArray<HydrologyBasinLookupSlot>(
                DeterministicHydrologyLakeResolver.LookupCapacity,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            using var visited = new NativeArray<byte>(
                DeterministicSurfaceWaterGenerator.MaximumFloodTileCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using var queue = new NativeArray<int>(
                DeterministicSurfaceWaterGenerator.MaximumFloodTileCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            SetSingleBasinMember(basinMembers, sinkCellX, sinkCellY);
            Assert.That(
                DeterministicHydrologyLakeResolver.TryInsertMember(
                    basinLookup,
                    0,
                    sinkCellX,
                    sinkCellY,
                    0),
                Is.True);
            var basin = new HydrologyLakeBasinResult
            {
                SinkCellX = sinkCellX,
                SinkCellY = sinkCellY,
                BasinAreaCells = 1,
                LakeFillFraction = 32768,
                LakeSurfaceHeight = (ushort)(anchorElevation + 1024),
                LakeFlags = HydrologyLakeFlags.BasinResolved | HydrologyLakeFlags.FixedLake
            };

            Assert.That(
                DeterministicSurfaceWaterGenerator.TryFillLakeCandidate(
                    settings,
                    basin,
                    basinMembers,
                    basinLookup,
                    visited,
                    queue,
                    minimumTileX,
                    minimumTileY,
                    maximumTileX,
                    maximumTileY,
                    surfaceWater),
                Is.True);

            int candidateCount = 0;
            for (int tileIndex = 0; tileIndex < surfaceWater.Length; tileIndex++)
            {
                GeneratedSurfaceWaterTile tile = surfaceWater[tileIndex];
                if (tile.Type != SurfaceWaterTileType.LakeCandidate)
                {
                    continue;
                }

                candidateCount++;
                Assert.That(tile.Flags, Is.EqualTo(SurfaceWaterTileFlags.RechargeCandidate));
                Assert.That(tile.SurfaceHeight, Is.EqualTo(basin.LakeSurfaceHeight));
                Assert.That(tile.SourceAmount, Is.EqualTo(basin.LakeFillFraction));
                Assert.That(tile.SourceCellX, Is.EqualTo(sinkCellX));
                Assert.That(tile.SourceCellY, Is.EqualTo(sinkCellY));
                int worldTileX = minimumTileX + (tileIndex % WorldGridConstants.LogicalChunkSize);
                int worldTileY = minimumTileY + (tileIndex / WorldGridConstants.LogicalChunkSize);
                Assert.That(
                    DeterministicEnvironmentGenerator.TryGenerateBaseClimate(
                        settings,
                        worldTileX,
                        worldTileY,
                        out GeneratedEnvironmentData environment),
                    Is.True);
                Assert.That(environment.Elevation, Is.GreaterThanOrEqualTo(settings.SeaLevelThreshold));
                Assert.That(environment.Elevation, Is.LessThan(tile.SurfaceHeight));
            }

            int anchorIndex = WorldGrid.ToTileIndex(
                anchor.WorldTileX - minimumTileX,
                anchor.WorldTileY - minimumTileY);
            Assert.That(surfaceWater[anchorIndex].Type, Is.EqualTo(SurfaceWaterTileType.LakeCandidate));
            Assert.That(candidateCount, Is.GreaterThan(0));
            Assert.That(
                CountConnectedLakeCandidates(surfaceWater, anchorIndex),
                Is.EqualTo(candidateCount));
        }

        /// <summary>
        /// 水文セル中心は陸上でも最低Anchorだけが海面下になる海岸条件を作り、チャンク生成失敗や不連続な淡水湖にしないことを保証します。
        /// </summary>
        [Test]
        public void LakeCandidate_UnderSeaAnchor_SkipsBasinWithoutFailure()
        {
            WorldGenerationSettings settings = CreateSettings("surface-water-coastal-lake-anchor");
            int sinkCellX = default;
            int sinkCellY = default;
            SurfaceWaterPathTile anchor = default;
            ushort anchorElevation = default;
            ushort centerElevation = default;
            bool foundCoastalShape = false;
            for (int cellY = -32; cellY <= 32 && !foundCoastalShape; cellY++)
            {
                for (int cellX = -32; cellX <= 32 && !foundCoastalShape; cellX++)
                {
                    // 海面を最低点と中心点の間へ置けるセルを探し、平均水文とタイル地形の解像度差を決定論的に再現します。
                    if (!DeterministicSurfaceWaterPathGenerator.TryFindChannelAnchor(
                            settings,
                            cellX,
                            cellY,
                            out SurfaceWaterPathTile candidateAnchor,
                            out ushort candidateAnchorElevation)
                        || !DeterministicEnvironmentGenerator.TryGenerateBaseClimate(
                            settings,
                            cellX * 8,
                            cellY * 8,
                            out GeneratedEnvironmentData centerEnvironment)
                        || candidateAnchorElevation >= centerEnvironment.Elevation
                        || candidateAnchorElevation
                            < WorldGenerationSettings.MinimumSupportedSeaLevelThreshold - 1
                        || candidateAnchorElevation >= settings.WetlandMaximumElevation
                        || centerEnvironment.Elevation == ushort.MaxValue)
                    {
                        continue;
                    }

                    sinkCellX = cellX;
                    sinkCellY = cellY;
                    anchor = candidateAnchor;
                    anchorElevation = candidateAnchorElevation;
                    centerElevation = centerEnvironment.Elevation;
                    foundCoastalShape = true;
                }
            }
            Assert.That(foundCoastalShape, Is.True);

            // 最低Anchorより1高く、かつ地上Biomeの固定標高下限以上へ海面を置くと、
            // 有効な生成設定を維持したままAnchorだけが海面下で中心タイルは陸上になります。
            settings.SeaLevelThreshold = (ushort)(anchorElevation + 1);
            Assert.That(DeterministicEnvironmentGenerator.ValidateSettings(settings), Is.True);
            Assert.That(anchorElevation, Is.LessThan(settings.SeaLevelThreshold));
            Assert.That(centerElevation, Is.GreaterThanOrEqualTo(settings.SeaLevelThreshold));

            ChunkCoordinate owner = WorldGrid.ToChunkCoordinate(anchor.WorldTileX, anchor.WorldTileY);
            int minimumTileX = WorldGrid.ToWorldTileX(owner.X, 0);
            int minimumTileY = WorldGrid.ToWorldTileY(owner.Y, 0);
            int maximumTileX = WorldGrid.ToWorldTileX(owner.X, WorldGridConstants.LogicalChunkSize - 1);
            int maximumTileY = WorldGrid.ToWorldTileY(owner.Y, WorldGridConstants.LogicalChunkSize - 1);
            using var surfaceWater = new NativeArray<GeneratedSurfaceWaterTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            using var basinMembers = new NativeArray<HydrologyUpstreamCandidate>(
                DeterministicHydrologyLakeResolver.MaximumBasinMembers,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using var basinLookup = new NativeArray<HydrologyBasinLookupSlot>(
                DeterministicHydrologyLakeResolver.LookupCapacity,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            using var visited = new NativeArray<byte>(
                DeterministicSurfaceWaterGenerator.MaximumFloodTileCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using var queue = new NativeArray<int>(
                DeterministicSurfaceWaterGenerator.MaximumFloodTileCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            SetSingleBasinMember(basinMembers, sinkCellX, sinkCellY);
            Assert.That(
                DeterministicHydrologyLakeResolver.TryInsertMember(
                    basinLookup,
                    0,
                    sinkCellX,
                    sinkCellY,
                    0),
                Is.True);
            var basin = new HydrologyLakeBasinResult
            {
                SinkCellX = sinkCellX,
                SinkCellY = sinkCellY,
                BasinAreaCells = 1,
                LakeFillFraction = 32768,
                LakeSurfaceHeight = (ushort)(centerElevation + 1),
                LakeFlags = HydrologyLakeFlags.BasinResolved | HydrologyLakeFlags.FixedLake
            };

            Assert.That(
                DeterministicSurfaceWaterGenerator.TryFillLakeCandidate(
                    settings,
                    basin,
                    basinMembers,
                    basinLookup,
                    visited,
                    queue,
                    minimumTileX,
                    minimumTileY,
                    maximumTileX,
                    maximumTileY,
                    surfaceWater),
                Is.True);
            Assert.That(CountType(surfaceWater, SurfaceWaterTileType.LakeCandidate), Is.Zero);
        }

        /// <summary>
        /// 右隣チャンクへ渡る1本の中心線を両チャンクへ別順で描き、各絶対タイルが所有Bufferで欠けないことを保証します。
        /// </summary>
        [Test]
        public void ChannelAcrossChunkBoundary_IsPublishedByAbsoluteTileOwner()
        {
            WorldGenerationSettings settings = CreateSettings("surface-water-boundary");
            using var pathWorkspace = CreatePathWorkspace();
            using var pathTiles = CreatePathTiles();
            Assert.That(
                TryFindRasterizableSource(
                    settings,
                    true,
                    pathWorkspace,
                    pathTiles,
                    out GeneratedHydrologyCell source,
                    out int leftChunkX,
                    out int chunkY,
                    out int pathLength),
                Is.True);
            source.ConnectionFlags |= HydrologyConnectionFlags.Stream;
            source.ConnectionFlags &= ~HydrologyConnectionFlags.River;

            using var left = new NativeArray<GeneratedSurfaceWaterTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            using var right = new NativeArray<GeneratedSurfaceWaterTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            Assert.That(
                DeterministicSurfaceWaterGenerator.TryRasterizeChannel(
                    settings, source, leftChunkX, chunkY, pathWorkspace, pathTiles, left),
                Is.True);
            Assert.That(
                DeterministicSurfaceWaterGenerator.TryRasterizeChannel(
                    settings, source, leftChunkX + 1, chunkY, pathWorkspace, pathTiles, right),
                Is.True);

            bool foundLeft = false;
            bool foundRight = false;
            for (int pathIndex = 0; pathIndex < pathLength; pathIndex++)
            {
                SurfaceWaterPathTile pathTile = pathTiles[pathIndex];
                if (TryGetEligibleOutputIndex(settings, leftChunkX, chunkY, pathTile.WorldTileX,
                        pathTile.WorldTileY, out int leftIndex))
                {
                    foundLeft = true;
                    Assert.That(left[leftIndex].Type, Is.EqualTo(SurfaceWaterTileType.Stream));
                }
                if (TryGetEligibleOutputIndex(settings, leftChunkX + 1, chunkY, pathTile.WorldTileX,
                        pathTile.WorldTileY, out int rightIndex))
                {
                    foundRight = true;
                    Assert.That(right[rightIndex].Type, Is.EqualTo(SurfaceWaterTileType.Stream));
                }
            }

            Assert.That(foundLeft, Is.True);
            Assert.That(foundRight, Is.True);
        }

        /// <summary>
        /// 水文だけの生成と、同じ水文から4096表流水まで細分化する生成を1・4・16チャンクで比較記録します。
        /// 合格閾値は置かず、C4-A2の追加CPU時間を同一Editor・同一Seedで確認するための明示実行テストです。
        /// </summary>
        [Test]
        [Explicit("C4-A2の表流水細分化追加コストを1・4・16チャンクで比較します。製品性能の合格判定には使いません。")]
        [Timeout(300000)]
        public void SurfaceWaterGeneration_ReportsHydrologyComparison()
        {
            WorldGenerationSettings settings = CreateSettings("surface-water-performance");
            using var cells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicHydrologyGenerator.CellsPerChunk,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            using var surfaceWater = new NativeArray<GeneratedSurfaceWaterTile>(
                WorldGridConstants.TilesPerChunk,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);

            // 初回JITとNativeArray安全性初期化を比較対象から外すため、両経路を一度ずつ温めます。
            Assert.That(DeterministicHydrologyGenerator.TryGenerateChunkCells(settings, 0, 0, cells), Is.True);
            Assert.That(TryGenerateSurfaceWater(settings, 0, 0, surfaceWater), Is.True);
            foreach (int chunkCount in new[] { 1, 4, 16 })
            {
                var stopwatch = Stopwatch.StartNew();
                for (int index = 0; index < chunkCount; index++)
                {
                    GetBenchmarkChunk(index, out int chunkX, out int chunkY);
                    Assert.That(
                        DeterministicHydrologyGenerator.TryGenerateChunkCells(
                            settings,
                            chunkX,
                            chunkY,
                            cells),
                        Is.True);
                }
                stopwatch.Stop();
                double hydrologyMilliseconds = stopwatch.Elapsed.TotalMilliseconds;

                stopwatch.Restart();
                for (int index = 0; index < chunkCount; index++)
                {
                    GetBenchmarkChunk(index, out int chunkX, out int chunkY);
                    Assert.That(TryGenerateSurfaceWater(settings, chunkX, chunkY, surfaceWater), Is.True);
                }
                stopwatch.Stop();

                UnityEngine.Debug.Log(
                    $"SURFACE_WATER chunks={chunkCount}, hydrologyMs={hydrologyMilliseconds:F6}, "
                    + $"hydrologyAndSurfaceMs={stopwatch.Elapsed.TotalMilliseconds:F6}");
            }
        }

        /// <summary>表流水生成で使う256ノードの未初期化一時配列を作ります。</summary>
        /// <returns>呼出し側がusingで破棄する経路探索作業配列です。</returns>
        private static NativeArray<SurfaceWaterPathSearchNode> CreatePathWorkspace()
        {
            return new NativeArray<SurfaceWaterPathSearchNode>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
        }

        /// <summary>表流水生成で使う256タイルの未初期化一時配列を作ります。</summary>
        /// <returns>呼出し側がusingで破棄する中心線結果配列です。</returns>
        private static NativeArray<SurfaceWaterPathTile> CreatePathTiles()
        {
            return new NativeArray<SurfaceWaterPathTile>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
        }

        /// <summary>有限範囲から海面以上の1タイルを探し、重複河道テストの固定入力として返します。</summary>
        /// <param name="settings">連続標高と海面を生成する固定設定です。</param>
        /// <param name="worldTileX">成功時の水平絶対タイル座標です。</param>
        /// <param name="worldTileY">成功時の垂直絶対タイル座標です。</param>
        /// <returns>海面以上かつ標高生成可能なタイルを発見できた場合はtrueです。</returns>
        private static bool TryFindEligibleTile(
            in WorldGenerationSettings settings,
            out int worldTileX,
            out int worldTileY)
        {
            for (int candidateY = -64; candidateY <= 64; candidateY++)
            {
                for (int candidateX = -64; candidateX <= 64; candidateX++)
                {
                    if (!DeterministicEnvironmentGenerator.TryGenerateBaseClimate(
                            settings,
                            candidateX,
                            candidateY,
                            out GeneratedEnvironmentData environment)
                        || environment.Elevation < settings.SeaLevelThreshold)
                    {
                        continue;
                    }

                    worldTileX = candidateX;
                    worldTileY = candidateY;
                    return true;
                }
            }

            worldTileX = default;
            worldTileY = default;
            return false;
        }

        /// <summary>重複後の代表値が強い河川を維持し、中心線フラグも残ったことを検証します。</summary>
        /// <param name="tiles">重複候補を適用済みの4096件表流水です。</param>
        /// <param name="localTileX">検証するチャンク内水平タイル座標です。</param>
        /// <param name="localTileY">検証するチャンク内垂直タイル座標です。</param>
        /// <param name="expectedSource">代表として残る強い河川の水文セルです。</param>
        private static void AssertRepresentativeRiverWithCenterLine(
            NativeArray<GeneratedSurfaceWaterTile> tiles,
            int localTileX,
            int localTileY,
            in GeneratedHydrologyCell expectedSource)
        {
            GeneratedSurfaceWaterTile actual = tiles[WorldGrid.ToTileIndex(localTileX, localTileY)];
            Assert.That(actual.Type, Is.EqualTo(SurfaceWaterTileType.River));
            Assert.That(actual.SourceAmount, Is.EqualTo(expectedSource.RunoffEquivalentCells));
            Assert.That(actual.SourceCellX, Is.EqualTo(expectedSource.CellX));
            Assert.That(actual.SourceCellY, Is.EqualTo(expectedSource.CellY));
            Assert.That(actual.Flags & SurfaceWaterTileFlags.ChannelCenterLine, Is.Not.Zero);
        }

        /// <summary>単一チャンクの水文と全固定作業配列を準備し、製品と同じ表流水純粋生成を実行します。</summary>
        /// <param name="settings">固定SeedとGeneratorVersionを持つ生成設定です。</param>
        /// <param name="chunkX">水平論理チャンク座標です。</param>
        /// <param name="chunkY">垂直論理チャンク座標です。</param>
        /// <param name="surfaceWater">成功時に4096件を受け取る呼出し側所有配列です。</param>
        /// <returns>水文、河道、湖面候補をすべて生成できた場合はtrueです。</returns>
        private static bool TryGenerateSurfaceWater(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            NativeArray<GeneratedSurfaceWaterTile> surfaceWater)
        {
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
            using var pathWorkspace = CreatePathWorkspace();
            using var pathTiles = CreatePathTiles();
            using var visited = new NativeArray<byte>(
                DeterministicSurfaceWaterGenerator.MaximumFloodTileCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            using var queue = new NativeArray<int>(
                DeterministicSurfaceWaterGenerator.MaximumFloodTileCount,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            return DeterministicHydrologyGenerator.TryGenerateChunkCells(
                    settings,
                    chunkX,
                    chunkY,
                    cells,
                    frontier,
                    basinMembers,
                    basinLookup)
                && DeterministicSurfaceWaterGenerator.TryGenerateChunk(
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
                    surfaceWater);
        }

        /// <summary>有限範囲から経路生成可能な陸上D8辺を探し、必要なら右チャンクへ実際に渡る辺だけを返します。</summary>
        /// <param name="settings">D8辺と中心線を固定する生成設定です。</param>
        /// <param name="mustCrossRightBoundary">右隣チャンクにも海面以上の中心線が必要ならtrueです。</param>
        /// <param name="workspace">候補検査ごとに再利用する256ノード配列です。</param>
        /// <param name="pathTiles">成功した中心線を保持する256タイル配列です。</param>
        /// <param name="source">成功時のD8上流水文セルです。</param>
        /// <param name="chunkX">成功時の中心セルを所有する水平論理チャンク座標です。</param>
        /// <param name="chunkY">成功時の中心セルを所有する垂直論理チャンク座標です。</param>
        /// <param name="pathLength">成功時の中心線タイル数です。</param>
        /// <returns>条件を満たす辺を有限範囲内で発見できた場合はtrueです。</returns>
        private static bool TryFindRasterizableSource(
            in WorldGenerationSettings settings,
            bool mustCrossRightBoundary,
            NativeArray<SurfaceWaterPathSearchNode> workspace,
            NativeArray<SurfaceWaterPathTile> pathTiles,
            out GeneratedHydrologyCell source,
            out int chunkX,
            out int chunkY,
            out int pathLength)
        {
            for (int candidateY = -32; candidateY <= 32; candidateY++)
            {
                for (int candidateX = -32; candidateX <= 32; candidateX++)
                {
                    int candidateChunkX = WorldGrid.FloorDivide(
                        candidateX,
                        DeterministicHydrologyGenerator.CellsPerChunkAxis);
                    int candidateChunkY = WorldGrid.FloorDivide(
                        candidateY,
                        DeterministicHydrologyGenerator.CellsPerChunkAxis);
                    int localCellX = candidateX
                        - (candidateChunkX * DeterministicHydrologyGenerator.CellsPerChunkAxis);
                    if (mustCrossRightBoundary && localCellX != 7)
                    {
                        continue;
                    }

                    if (!DeterministicHydrologyGenerator.TryGenerateFlowCell(
                            settings,
                            candidateX,
                            candidateY,
                            out GeneratedHydrologyCell candidate)
                        || candidate.IsOceanOutlet
                        || !candidate.HasDownstream)
                    {
                        continue;
                    }

                    candidate.ConnectionFlags |= HydrologyConnectionFlags.Stream;
                    if (!DeterministicSurfaceWaterPathGenerator.TryBuildChannelCenterLine(
                            settings,
                            candidate,
                            workspace,
                            pathTiles,
                            out int candidateLength))
                    {
                        continue;
                    }

                    bool hasLocal = false;
                    bool hasRight = false;
                    for (int pathIndex = 0; pathIndex < candidateLength; pathIndex++)
                    {
                        SurfaceWaterPathTile pathTile = pathTiles[pathIndex];
                        hasLocal |= TryGetEligibleOutputIndex(
                            settings,
                            candidateChunkX,
                            candidateChunkY,
                            pathTile.WorldTileX,
                            pathTile.WorldTileY,
                            out _);
                        hasRight |= TryGetEligibleOutputIndex(
                            settings,
                            candidateChunkX + 1,
                            candidateChunkY,
                            pathTile.WorldTileX,
                            pathTile.WorldTileY,
                            out _);
                    }

                    if (!hasLocal || (mustCrossRightBoundary && !hasRight))
                    {
                        continue;
                    }

                    source = candidate;
                    chunkX = candidateChunkX;
                    chunkY = candidateChunkY;
                    pathLength = candidateLength;
                    return true;
                }
            }

            source = default;
            chunkX = default;
            chunkY = default;
            pathLength = default;
            return false;
        }

        /// <summary>絶対タイルが対象チャンク内かつ海面以上なら、行優先の出力添字を返します。</summary>
        /// <param name="settings">海面と連続標高を持つ固定設定です。</param>
        /// <param name="chunkX">対象の水平論理チャンク座標です。</param>
        /// <param name="chunkY">対象の垂直論理チャンク座標です。</param>
        /// <param name="worldTileX">候補の水平絶対タイル座標です。</param>
        /// <param name="worldTileY">候補の垂直絶対タイル座標です。</param>
        /// <param name="outputIndex">成功時の0〜4095行優先添字です。</param>
        /// <returns>候補が対象チャンク内かつ海面以上ならtrueです。</returns>
        private static bool TryGetEligibleOutputIndex(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            int worldTileX,
            int worldTileY,
            out int outputIndex)
        {
            int minimumX = WorldGrid.ToWorldTileX(chunkX, 0);
            int minimumY = WorldGrid.ToWorldTileY(chunkY, 0);
            int localX = worldTileX - minimumX;
            int localY = worldTileY - minimumY;
            outputIndex = default;
            if (localX < 0 || localX >= WorldGridConstants.LogicalChunkSize
                || localY < 0 || localY >= WorldGridConstants.LogicalChunkSize
                || !DeterministicEnvironmentGenerator.TryGenerateBaseClimate(
                    settings,
                    worldTileX,
                    worldTileY,
                    out GeneratedEnvironmentData environment)
                || environment.Elevation < settings.SeaLevelThreshold)
            {
                return false;
            }

            outputIndex = WorldGrid.ToTileIndex(localX, localY);
            return true;
        }

        /// <summary>河川の期待中心線または上下左右候補が有効なら、4096件の期待Maskへ記録します。</summary>
        /// <param name="settings">海面と連続標高を生成する固定設定です。</param>
        /// <param name="chunkX">対象の水平論理チャンク座標です。</param>
        /// <param name="chunkY">対象の垂直論理チャンク座標です。</param>
        /// <param name="worldTileX">候補の水平絶対タイル座標です。</param>
        /// <param name="worldTileY">候補の垂直絶対タイル座標です。</param>
        /// <param name="expected">有効候補の行優先添字をtrueにする期待配列です。</param>
        private static void MarkEligible(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            int worldTileX,
            int worldTileY,
            bool[] expected)
        {
            if (TryGetEligibleOutputIndex(
                    settings,
                    chunkX,
                    chunkY,
                    worldTileX,
                    worldTileY,
                    out int outputIndex))
            {
                expected[outputIndex] = true;
            }
        }

        /// <summary>4096件の表流水から指定種類の件数を数えます。</summary>
        /// <param name="tiles">固定長の表流水結果です。</param>
        /// <param name="type">数える安定種類です。</param>
        /// <returns>指定種類と一致した要素数です。</returns>
        private static int CountType(
            NativeArray<GeneratedSurfaceWaterTile> tiles,
            SurfaceWaterTileType type)
        {
            int count = 0;
            for (int index = 0; index < tiles.Length; index++)
            {
                if (tiles[index].Type == type)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>指定開始タイルから8近傍で到達できる湖面候補数を数え、Flood Fill結果の連結性を検証します。</summary>
        /// <param name="tiles">行優先4096件の固定表流水結果です。</param>
        /// <param name="startIndex">湖底Anchorに対応する行優先添字です。</param>
        /// <returns>開始点と8近傍でつながるLakeCandidateの件数です。</returns>
        private static int CountConnectedLakeCandidates(
            NativeArray<GeneratedSurfaceWaterTile> tiles,
            int startIndex)
        {
            var visited = new bool[WorldGridConstants.TilesPerChunk];
            var queue = new int[WorldGridConstants.TilesPerChunk];
            int readIndex = 0;
            int writeIndex = 0;
            int connectedCount = 0;
            visited[startIndex] = true;
            queue[writeIndex++] = startIndex;
            while (readIndex < writeIndex)
            {
                int currentIndex = queue[readIndex++];
                connectedCount++;
                int currentX = currentIndex % WorldGridConstants.LogicalChunkSize;
                int currentY = currentIndex / WorldGridConstants.LogicalChunkSize;
                for (int offsetY = -1; offsetY <= 1; offsetY++)
                {
                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        int neighborX = currentX + offsetX;
                        int neighborY = currentY + offsetY;
                        if ((offsetX == 0 && offsetY == 0)
                            || neighborX < 0 || neighborX >= WorldGridConstants.LogicalChunkSize
                            || neighborY < 0 || neighborY >= WorldGridConstants.LogicalChunkSize)
                        {
                            continue;
                        }

                        int neighborIndex = WorldGrid.ToTileIndex(neighborX, neighborY);
                        if (visited[neighborIndex]
                            || tiles[neighborIndex].Type != SurfaceWaterTileType.LakeCandidate)
                        {
                            continue;
                        }

                        visited[neighborIndex] = true;
                        queue[writeIndex++] = neighborIndex;
                    }
                }
            }

            return connectedCount;
        }

        /// <summary>using変数として所有するNativeArrayの先頭へ、テスト用の単一流域セルを書き込みます。</summary>
        /// <param name="members">呼出し側が所有・破棄する流域メンバー配列です。</param>
        /// <param name="cellX">登録する水平水文セル座標です。</param>
        /// <param name="cellY">登録する垂直水文セル座標です。</param>
        private static void SetSingleBasinMember(
            NativeArray<HydrologyUpstreamCandidate> members,
            int cellX,
            int cellY)
        {
            members[0] = new HydrologyUpstreamCandidate
            {
                CellX = cellX,
                CellY = cellY
            };
        }

        /// <summary>2回生成した表流水要素の全固定フィールドが同じであることを保証します。</summary>
        /// <param name="expected">1回目の要素です。</param>
        /// <param name="actual">2回目の要素です。</param>
        /// <param name="index">失敗メッセージへ含める行優先添字です。</param>
        private static void AssertSurfaceWaterEqual(
            in GeneratedSurfaceWaterTile expected,
            in GeneratedSurfaceWaterTile actual,
            int index)
        {
            Assert.That(actual.Type, Is.EqualTo(expected.Type), $"Type index={index}");
            Assert.That(actual.Flags, Is.EqualTo(expected.Flags), $"Flags index={index}");
            Assert.That(actual.SurfaceHeight, Is.EqualTo(expected.SurfaceHeight), $"SurfaceHeight index={index}");
            Assert.That(actual.SourceAmount, Is.EqualTo(expected.SourceAmount), $"SourceAmount index={index}");
            Assert.That(actual.SourceCellX, Is.EqualTo(expected.SourceCellX), $"SourceCellX index={index}");
            Assert.That(actual.SourceCellY, Is.EqualTo(expected.SourceCellY), $"SourceCellY index={index}");
        }

        /// <summary>性能比較で原点から外側へ広がる4×4の決定論的チャンク座標を返します。</summary>
        /// <param name="index">0〜15の測定添字です。</param>
        /// <param name="chunkX">成功時の水平チャンク座標です。</param>
        /// <param name="chunkY">成功時の垂直チャンク座標です。</param>
        private static void GetBenchmarkChunk(int index, out int chunkX, out int chunkY)
        {
            chunkX = (index % 4) - 2;
            chunkY = (index / 4) - 2;
        }

        /// <summary>文字列Seedと現行既定値から、各テスト専用の固定ワールド設定を作ります。</summary>
        /// <param name="seedText">別テストの乱数系列と共有しないSeed文字列です。</param>
        /// <returns>現在GeneratorVersionの既定生成設定です。</returns>
        private static WorldGenerationSettings CreateSettings(string seedText)
        {
            return WorldGenerationSettings.CreateDefault(WorldSeedUtility.FromString(seedText), WorldGenerationSettings.CurrentGeneratorVersion);
        }
    }
}
