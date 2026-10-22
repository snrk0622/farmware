// このファイルは、C4-A1の水文セルAnchorと河道中心線が決定論的かつ有限であることを検証します。
// 固定D8辺を準備し、負座標、端点、隣接性、無効入力を確認して、後続のECS表流水Bufferが同じ純粋計算を安全に利用できることを保証します。

using NUnit.Framework;
using Unity.Collections;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 最低標高Anchor、最大16×16の経路探索、同一入力の再現性を検証するEditModeテストです。
    /// C4-A1では表示や湖面Flood Fillを対象にせず、河道中心線の純粋計算だけを保証します。
    /// </summary>
    public sealed class DeterministicSurfaceWaterPathGeneratorTests
    {
        /// <summary>
        /// 負座標の水文セルを準備し、選択Anchorが中心-4〜+3の範囲内にあり、64タイルの最低標高と一致することを保証します。
        /// </summary>
        [Test]
        public void ChannelAnchor_NegativeCell_SelectsLowestTileInsideResponsibilityArea()
        {
            WorldGenerationSettings settings = CreateSettings("surface-water-anchor");
            const int cellX = -3;
            const int cellY = -2;

            bool generated = DeterministicSurfaceWaterPathGenerator.TryFindChannelAnchor(
                settings,
                cellX,
                cellY,
                out SurfaceWaterPathTile anchor,
                out ushort anchorElevation);

            Assert.That(generated, Is.True);
            Assert.That(anchor.WorldTileX, Is.InRange((cellX * 8) - 4, (cellX * 8) + 3));
            Assert.That(anchor.WorldTileY, Is.InRange((cellY * 8) - 4, (cellY * 8) + 3));

            ushort minimumElevation = ushort.MaxValue;
            for (int worldTileY = (cellY * 8) - 4; worldTileY <= (cellY * 8) + 3; worldTileY++)
            {
                for (int worldTileX = (cellX * 8) - 4; worldTileX <= (cellX * 8) + 3; worldTileX++)
                {
                    Assert.That(
                        DeterministicEnvironmentGenerator.TryGenerateBaseClimate(
                            settings,
                            worldTileX,
                            worldTileY,
                            out GeneratedEnvironmentData environment),
                        Is.True);
                    if (environment.Elevation < minimumElevation)
                    {
                        minimumElevation = environment.Elevation;
                    }
                }
            }

            Assert.That(anchorElevation, Is.EqualTo(minimumElevation));
        }

        /// <summary>
        /// int端へ寄り過ぎた水文セルを準備し、中心の8倍がタイル座標範囲を越える場合に既定値だけを返すことを保証します。
        /// </summary>
        [Test]
        public void ChannelAnchor_CellOutsideTileCoordinateRange_ReturnsFalse()
        {
            WorldGenerationSettings settings = CreateSettings("surface-water-coordinate-limit");

            bool generated = DeterministicSurfaceWaterPathGenerator.TryFindChannelAnchor(
                settings,
                int.MaxValue,
                int.MinValue,
                out SurfaceWaterPathTile anchor,
                out ushort anchorElevation);

            Assert.That(generated, Is.False);
            Assert.That(anchor.WorldTileX, Is.Zero);
            Assert.That(anchor.WorldTileY, Is.Zero);
            Assert.That(anchorElevation, Is.Zero);
        }

        /// <summary>
        /// 8方向それぞれの固定Stream辺を2回生成し、両端、8近傍連結、固定上限、全座標の一致を保証します。
        /// </summary>
        /// <param name="downstreamOffsetX">下流セルまでの水平差です。D8隣接なので-1〜1です。</param>
        /// <param name="downstreamOffsetY">下流セルまでの垂直差です。D8隣接なので-1〜1です。</param>
        /// <param name="direction">座標差と一致させる固定流向です。</param>
        [TestCase(1, 0, HydrologyFlowDirection.East)]
        [TestCase(1, 1, HydrologyFlowDirection.NorthEast)]
        [TestCase(0, 1, HydrologyFlowDirection.North)]
        [TestCase(-1, 1, HydrologyFlowDirection.NorthWest)]
        [TestCase(-1, 0, HydrologyFlowDirection.West)]
        [TestCase(-1, -1, HydrologyFlowDirection.SouthWest)]
        [TestCase(0, -1, HydrologyFlowDirection.South)]
        [TestCase(1, -1, HydrologyFlowDirection.SouthEast)]
        public void ChannelCenterLine_D8Edge_IsConnectedBoundedAndDeterministic(
            int downstreamOffsetX,
            int downstreamOffsetY,
            HydrologyFlowDirection direction)
        {
            WorldGenerationSettings settings = CreateSettings("surface-water-path");
            var source = new GeneratedHydrologyCell
            {
                CellX = -1,
                CellY = 2,
                DownstreamCellX = -1 + downstreamOffsetX,
                DownstreamCellY = 2 + downstreamOffsetY,
                FlowDirection = direction,
                ConnectionFlags = HydrologyConnectionFlags.Stream
            };
            using var firstWorkspace = new NativeArray<SurfaceWaterPathSearchNode>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                Allocator.Temp);
            using var secondWorkspace = new NativeArray<SurfaceWaterPathSearchNode>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                Allocator.Temp);
            using var firstPath = new NativeArray<SurfaceWaterPathTile>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                Allocator.Temp);
            using var secondPath = new NativeArray<SurfaceWaterPathTile>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                Allocator.Temp);

            Assert.That(DeterministicSurfaceWaterPathGenerator.TryBuildChannelCenterLine(
                settings, source, firstWorkspace, firstPath, out int firstLength), Is.True);
            Assert.That(DeterministicSurfaceWaterPathGenerator.TryBuildChannelCenterLine(
                settings, source, secondWorkspace, secondPath, out int secondLength), Is.True);
            Assert.That(firstLength, Is.InRange(2, DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount));
            Assert.That(secondLength, Is.EqualTo(firstLength));

            Assert.That(DeterministicSurfaceWaterPathGenerator.TryFindChannelAnchor(
                settings, source.CellX, source.CellY, out SurfaceWaterPathTile start, out _), Is.True);
            Assert.That(DeterministicSurfaceWaterPathGenerator.TryFindChannelAnchor(
                settings, source.DownstreamCellX, source.DownstreamCellY,
                out SurfaceWaterPathTile destination, out _), Is.True);
            AssertTileEqual(start, firstPath[0]);
            AssertTileEqual(destination, firstPath[firstLength - 1]);

            for (int index = 0; index < firstLength; index++)
            {
                AssertTileEqual(firstPath[index], secondPath[index]);
                if (index == 0)
                {
                    continue;
                }

                int deltaX = firstPath[index].WorldTileX - firstPath[index - 1].WorldTileX;
                int deltaY = firstPath[index].WorldTileY - firstPath[index - 1].WorldTileY;
                Assert.That(deltaX, Is.InRange(-1, 1));
                Assert.That(deltaY, Is.InRange(-1, 1));
                Assert.That(deltaX == 0 && deltaY == 0, Is.False);
            }
        }

        /// <summary>
        /// 終点の高地によって両経路の最大標高が同じになる標高格子を準備し、
        /// 早い段階の最大標高ではなく、最終的な上り合計が小さい経路を選ぶことを保証します。
        /// </summary>
        [Test]
        public void PreparedGrid_RequiredHighDestination_ChoosesLowerTotalUphillAfterMaximumEqualizes()
        {
            WorldGenerationSettings settings = CreateSettings("surface-water-equalized-maximum");
            const int width = 8;
            const int height = 5;
            const int nodeCount = width * height;
            const int startIndex = (2 * width) + 0;
            const int destinationIndex = (2 * width) + 7;
            using var workspace = new NativeArray<SurfaceWaterPathSearchNode>(nodeCount, Allocator.Temp);
            using var path = new NativeArray<SurfaceWaterPathTile>(nodeCount, Allocator.Temp);

            // 標高20は最適な最大標高10を超える壁です。上下2本の細い通路だけを通れる反例格子にします。
            for (int localY = 0; localY < height; localY++)
            {
                for (int localX = 0; localX < width; localX++)
                {
                    SetPreparedElevation(workspace, width, localX, localY, 20);
                }
            }

            SetPreparedElevation(workspace, width, 0, 2, 0);
            SetPreparedElevation(workspace, width, 1, 1, 1);
            SetPreparedElevation(workspace, width, 2, 1, 0);
            SetPreparedElevation(workspace, width, 3, 1, 1);
            SetPreparedElevation(workspace, width, 4, 1, 0);
            SetPreparedElevation(workspace, width, 5, 1, 1);
            for (int worldTileX = 1; worldTileX <= 5; worldTileX++)
            {
                SetPreparedElevation(workspace, width, worldTileX, 3, 2);
            }
            SetPreparedElevation(workspace, width, 6, 2, 0);
            SetPreparedElevation(workspace, width, 7, 2, 10);

            Assert.That(DeterministicSurfaceWaterPathGenerator.TryBuildPreparedCenterLine(
                settings,
                0,
                0,
                width,
                height,
                startIndex,
                destinationIndex,
                workspace,
                path,
                out int pathLength), Is.True);
            Assert.That(pathLength, Is.EqualTo(8));
            Assert.That(path[1].WorldTileY, Is.EqualTo(3),
                "上側は途中の最大標高が1と低い一方で上り合計13です。最終最大標高10が同じなら、上り合計12の下側を選びます。");

            ulong maximumElevation = 0;
            ulong totalUphill = 0;
            ushort previousElevation = 0;
            for (int pathIndex = 0; pathIndex < pathLength; pathIndex++)
            {
                int nodeIndex = (path[pathIndex].WorldTileY * width) + path[pathIndex].WorldTileX;
                ushort elevation = workspace[nodeIndex].Elevation;
                if (elevation > maximumElevation)
                {
                    maximumElevation = elevation;
                }
                if (pathIndex > 0 && elevation > previousElevation)
                {
                    totalUphill += (ulong)(elevation - previousElevation);
                }
                previousElevation = elevation;
            }

            Assert.That(maximumElevation, Is.EqualTo(10UL));
            Assert.That(totalUphill, Is.EqualTo(12UL));
        }

        /// <summary>
        /// 表流水フラグがない辺、隣接していない下流座標、流向と座標差が不一致の辺を準備し、部分経路を成功として返さないことを保証します。
        /// </summary>
        [Test]
        public void ChannelCenterLine_InvalidSource_ReturnsFalseWithoutPartialPath()
        {
            WorldGenerationSettings settings = CreateSettings("surface-water-invalid");
            using var workspace = new NativeArray<SurfaceWaterPathSearchNode>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                Allocator.Temp);
            using var path = new NativeArray<SurfaceWaterPathTile>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                Allocator.Temp);
            var noWater = new GeneratedHydrologyCell
            {
                CellX = 0,
                CellY = 0,
                DownstreamCellX = 1,
                DownstreamCellY = 0,
                FlowDirection = HydrologyFlowDirection.East,
                ConnectionFlags = HydrologyConnectionFlags.Marine
            };
            var nonAdjacent = noWater;
            nonAdjacent.DownstreamCellX = 2;
            nonAdjacent.ConnectionFlags = HydrologyConnectionFlags.Stream;
            var mismatchedDirection = noWater;
            mismatchedDirection.FlowDirection = HydrologyFlowDirection.North;
            mismatchedDirection.ConnectionFlags = HydrologyConnectionFlags.Stream;

            Assert.That(DeterministicSurfaceWaterPathGenerator.TryBuildChannelCenterLine(
                settings, noWater, workspace, path, out int noWaterLength), Is.False);
            Assert.That(noWaterLength, Is.Zero);
            Assert.That(DeterministicSurfaceWaterPathGenerator.TryBuildChannelCenterLine(
                settings, nonAdjacent, workspace, path, out int nonAdjacentLength), Is.False);
            Assert.That(nonAdjacentLength, Is.Zero);
            Assert.That(DeterministicSurfaceWaterPathGenerator.TryBuildChannelCenterLine(
                settings, mismatchedDirection, workspace, path, out int mismatchedLength), Is.False);
            Assert.That(mismatchedLength, Is.Zero);
        }

        /// <summary>
        /// 固定上限より1件少ない作業配列を準備し、探索を始めず部分経路も公開しないことを保証します。
        /// </summary>
        [Test]
        public void ChannelCenterLine_InsufficientWorkspace_ReturnsFalseWithoutPartialPath()
        {
            WorldGenerationSettings settings = CreateSettings("surface-water-small-workspace");
            var source = new GeneratedHydrologyCell
            {
                CellX = 0,
                CellY = 0,
                DownstreamCellX = 1,
                DownstreamCellY = 0,
                FlowDirection = HydrologyFlowDirection.East,
                ConnectionFlags = HydrologyConnectionFlags.Stream
            };
            using var shortWorkspace = new NativeArray<SurfaceWaterPathSearchNode>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount - 1,
                Allocator.Temp);
            using var path = new NativeArray<SurfaceWaterPathTile>(
                DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount,
                Allocator.Temp);

            Assert.That(DeterministicSurfaceWaterPathGenerator.TryBuildChannelCenterLine(
                settings, source, shortWorkspace, path, out int pathLength), Is.False);
            Assert.That(pathLength, Is.Zero);
        }

        /// <summary>テスト名から安定Seedを作り、製品既定のGeneratorVersion 1設定を返します。</summary>
        /// <param name="seedText">テストケースを分離するSeed文字列です。</param>
        /// <returns>C4-A1の純粋計算へ渡す既定ワールド生成設定です。</returns>
        private static WorldGenerationSettings CreateSettings(string seedText)
        {
            return WorldGenerationSettings.CreateDefault(WorldSeedUtility.FromString(seedText), 1);
        }

        /// <summary>合成標高格子の指定位置へ標高を設定し、反例用の通路を読みやすく組み立てます。</summary>
        /// <param name="workspace">行優先で標高を保持する作業配列です。</param>
        /// <param name="width">合成格子の幅です。</param>
        /// <param name="localX">設定する格子内Xです。</param>
        /// <param name="localY">設定する格子内Yです。</param>
        /// <param name="elevation">設定する固定連続標高です。</param>
        private static void SetPreparedElevation(
            NativeArray<SurfaceWaterPathSearchNode> workspace,
            int width,
            int localX,
            int localY,
            ushort elevation)
        {
            int nodeIndex = (localY * width) + localX;
            SurfaceWaterPathSearchNode node = workspace[nodeIndex];
            node.Elevation = elevation;
            workspace[nodeIndex] = node;
        }

        /// <summary>期待座標と実座標をフィールド別に比較し、失敗時の差を読みやすくします。</summary>
        /// <param name="expected">期待する絶対タイル座標です。</param>
        /// <param name="actual">純粋Generatorが返した絶対タイル座標です。</param>
        private static void AssertTileEqual(
            SurfaceWaterPathTile expected,
            SurfaceWaterPathTile actual)
        {
            Assert.That(actual.WorldTileX, Is.EqualTo(expected.WorldTileX));
            Assert.That(actual.WorldTileY, Is.EqualTo(expected.WorldTileY));
        }
    }
}
