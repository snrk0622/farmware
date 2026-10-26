// このファイルは、8近傍の地形から14基本形と回転を選ぶ境界ハイライト規則をEditModeで検証します。
// SpriteやSceneを必要としない純粋計算として、47状態の網羅性、土地・岩壁の接続条件、回転方向を保証します。

using System.Collections.Generic;
using Farmware.Simulation;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// TerrainBoundaryHighlightUtilityの正規化と基本形選択が、画像制作仕様の47状態を満たすことを確認します。
    /// </summary>
    public sealed class TerrainBoundaryHighlightUtilityTests
    {
        /// <summary>
        /// 256通りの生入力をすべて正規化し、斜め接続制約によって47状態へ縮約されることを確認します。
        /// </summary>
        [Test]
        public void NormalizeConnections_AllRawMasks_ProducesExactlyFortySevenStates()
        {
            var normalizedStates = new HashSet<byte>();

            // byteの全範囲をintで走査し、255の次でオーバーフローして無限ループになることを避けます。
            for (int rawMask = byte.MinValue; rawMask <= byte.MaxValue; rawMask++)
            {
                BoundaryNeighborConnection normalized =
                    TerrainBoundaryHighlightUtility.NormalizeConnections(
                        (BoundaryNeighborConnection)rawMask);
                normalizedStates.Add((byte)normalized);
            }

            Assert.That(normalizedStates.Count, Is.EqualTo(47));
        }

        /// <summary>
        /// 47接続状態のうち完全接続だけが非表示となり、残る46状態が14基本形のいずれかへ割り当てられることを確認します。
        /// </summary>
        [Test]
        public void SelectForConnections_AllNormalizedStates_CoversFourteenShapesAndOneEmptyState()
        {
            var normalizedStates = new HashSet<byte>();
            var selectedShapes = new HashSet<BoundaryHighlightShape>();
            int emptySelectionCount = 0;

            for (int rawMask = byte.MinValue; rawMask <= byte.MaxValue; rawMask++)
            {
                byte normalized = (byte)TerrainBoundaryHighlightUtility.NormalizeConnections(
                    (BoundaryNeighborConnection)rawMask);
                if (!normalizedStates.Add(normalized))
                {
                    continue;
                }

                BoundaryHighlightSelection selection =
                    TerrainBoundaryHighlightUtility.SelectForConnections(
                        (BoundaryNeighborConnection)normalized);
                if (!selection.HasHighlight)
                {
                    emptySelectionCount++;
                    Assert.That(normalized, Is.EqualTo(byte.MaxValue));
                    continue;
                }

                selectedShapes.Add(selection.Shape);
                Assert.That(selection.QuarterTurnsClockwise, Is.InRange(0, 3));
            }

            Assert.That(normalizedStates.Count, Is.EqualTo(47));
            Assert.That(emptySelectionCount, Is.EqualTo(1));
            Assert.That(selectedShapes.Count, Is.EqualTo(14));
        }

        /// <summary>
        /// 横長画像の左から右への14枚が、仕様どおりの外周・内角ビットへ対応することを確認します。
        /// </summary>
        [Test]
        public void GetCanonicalFeatureMask_AllShapes_MatchesSpriteSheetOrder()
        {
            byte[] expectedMasks =
            {
                0x0F,
                0x0E,
                0x0A,
                0x0C,
                0x1C,
                0x01,
                0x21,
                0x41,
                0x61,
                0x10,
                0x30,
                0x50,
                0x70,
                0xF0
            };

            for (int shapeIndex = 1; shapeIndex <= expectedMasks.Length; shapeIndex++)
            {
                byte actualMask = TerrainBoundaryHighlightUtility.GetCanonicalFeatureMask(
                    (BoundaryHighlightShape)shapeIndex);
                Assert.That(actualMask, Is.EqualTo(expectedMasks[shapeIndex - 1]));
            }
        }

        /// <summary>
        /// 北の外周線が東・南・西へ順に移り、4回転で元へ戻るため、反転ではなく純粋な回転であることを確認します。
        /// </summary>
        [Test]
        public void RotateHighlightFeatureMask_NorthEdge_RotatesClockwiseWithoutReflection()
        {
            const byte northEdge = 0x01;

            Assert.That(
                TerrainBoundaryHighlightUtility.RotateHighlightFeatureMask(northEdge, 1),
                Is.EqualTo(0x02));
            Assert.That(
                TerrainBoundaryHighlightUtility.RotateHighlightFeatureMask(northEdge, 2),
                Is.EqualTo(0x04));
            Assert.That(
                TerrainBoundaryHighlightUtility.RotateHighlightFeatureMask(northEdge, 3),
                Is.EqualTo(0x08));
            Assert.That(
                TerrainBoundaryHighlightUtility.RotateHighlightFeatureMask(northEdge, 4),
                Is.EqualTo(northEdge));
        }

        /// <summary>
        /// 異なるBiomeと岩壁は陸続きとして扱い、水またはマグマだけが通常土地の外周を作ることを確認します。
        /// </summary>
        [Test]
        public void SelectLand_DifferentBiomesAndRockWall_DoNotCreateBoundary()
        {
            GeneratedTerrainCell current = CreateLand(BiomeId.TemperateGrassland);
            TerrainNeighborTiles connectedNeighbors = CreateNeighbors(
                CreateLand(BiomeId.TemperateDeciduousForest),
                CreateWall(BiomeId.AlpineRockland),
                CreateLand(BiomeId.HotDesert),
                CreateLand(BiomeId.VolcanicGeothermal));

            BoundaryHighlightSelection selection =
                TerrainBoundaryHighlightUtility.SelectLand(current, connectedNeighbors);

            Assert.That(selection.HasHighlight, Is.False);
        }

        /// <summary>
        /// 東側が水なら東外周、東側がマグマでも同じ外周を選び、液体種別によって形が変わらないことを確認します。
        /// </summary>
        [Test]
        public void SelectLand_WaterOrMagmaNeighbor_SelectsLiquidBoundary()
        {
            GeneratedTerrainCell current = CreateLand(BiomeId.TemperateGrassland);
            TerrainNeighborTiles waterNeighbors = CreateNeighbors(
                CreateLand(),
                CreateLiquid(3),
                CreateLand(),
                CreateLand());
            TerrainNeighborTiles magmaNeighbors = CreateNeighbors(
                CreateLand(),
                CreateLiquid(5),
                CreateLand(),
                CreateLand());

            BoundaryHighlightSelection waterSelection =
                TerrainBoundaryHighlightUtility.SelectLand(current, waterNeighbors);
            BoundaryHighlightSelection magmaSelection =
                TerrainBoundaryHighlightUtility.SelectLand(current, magmaNeighbors);

            Assert.That(waterSelection.HasHighlight, Is.True);
            Assert.That(waterSelection.Shape, Is.EqualTo(BoundaryHighlightShape.Shape06));
            Assert.That(waterSelection.QuarterTurnsClockwise, Is.EqualTo(1));
            Assert.That(magmaSelection.Shape, Is.EqualTo(waterSelection.Shape));
            Assert.That(
                magmaSelection.QuarterTurnsClockwise,
                Is.EqualTo(waterSelection.QuarterTurnsClockwise));
        }

        /// <summary>
        /// 上下左右が接続し北東だけ欠ける場合に、外周ではなく北東の内角基本形を選ぶことを確認します。
        /// </summary>
        [Test]
        public void SelectForConnections_MissingNorthEastDiagonal_SelectsInnerCornerOnly()
        {
            BoundaryNeighborConnection connections = BoundaryNeighborConnection.North
                | BoundaryNeighborConnection.East
                | BoundaryNeighborConnection.South
                | BoundaryNeighborConnection.West
                | BoundaryNeighborConnection.SouthEast
                | BoundaryNeighborConnection.SouthWest
                | BoundaryNeighborConnection.NorthWest;

            BoundaryHighlightSelection selection =
                TerrainBoundaryHighlightUtility.SelectForConnections(connections);

            Assert.That(selection.Shape, Is.EqualTo(BoundaryHighlightShape.Shape10));
            Assert.That(selection.QuarterTurnsClockwise, Is.EqualTo(0));
        }

        /// <summary>
        /// 上下左右なしの斜め接続は無効化され、孤立セルの四辺外周だけになって不要な内角を作らないことを確認します。
        /// </summary>
        [Test]
        public void NormalizeConnections_DiagonalsWithoutCardinals_RemovesAllDiagonals()
        {
            BoundaryNeighborConnection diagonalOnly = BoundaryNeighborConnection.NorthEast
                | BoundaryNeighborConnection.SouthEast
                | BoundaryNeighborConnection.SouthWest
                | BoundaryNeighborConnection.NorthWest;

            BoundaryNeighborConnection normalized =
                TerrainBoundaryHighlightUtility.NormalizeConnections(diagonalOnly);
            BoundaryHighlightSelection selection =
                TerrainBoundaryHighlightUtility.SelectForConnections(diagonalOnly);

            Assert.That(normalized, Is.EqualTo(BoundaryNeighborConnection.None));
            Assert.That(selection.Shape, Is.EqualTo(BoundaryHighlightShape.Shape01));
            Assert.That(selection.QuarterTurnsClockwise, Is.EqualTo(0));
        }

        /// <summary>
        /// 岩壁の連続面は岩壁だけを接続扱いとし、隣接する通常土地との境界を選ぶことを確認します。
        /// </summary>
        [Test]
        public void SelectRockWall_LandNeighbor_SelectsRockWallBoundary()
        {
            GeneratedTerrainCell current = CreateWall();
            TerrainNeighborTiles neighbors = CreateNeighbors(
                CreateWall(),
                CreateLand(),
                CreateWall(),
                CreateWall(),
                diagonal: CreateWall());

            BoundaryHighlightSelection selection =
                TerrainBoundaryHighlightUtility.SelectRockWall(current, neighbors);

            Assert.That(selection.Shape, Is.EqualTo(BoundaryHighlightShape.Shape06));
            Assert.That(selection.QuarterTurnsClockwise, Is.EqualTo(1));
        }

        /// <summary>
        /// GroundBlockとBiomeだけを持つ、境界選択に必要最小限の陸地セルを作ります。
        /// </summary>
        /// <param name="biome">設定するBiomeです。</param>
        /// <returns>テスト用の自然生成Blockセルです。</returns>
        private static GeneratedTerrainCell CreateLand(
            BiomeId biome = BiomeId.TemperateGrassland)
        {
            return new GeneratedTerrainCell
            {
                GroundBlockRuntimeId = new DefinitionRuntimeId(1),
                Biome = biome
            };
        }

        /// <summary>地面の上へWallBlockを持つテスト用岩壁セルを作ります。</summary>
        /// <param name="biome">設定するBiomeです。</param>
        /// <returns>テスト用の自然岩壁セルです。</returns>
        private static GeneratedTerrainCell CreateWall(
            BiomeId biome = BiomeId.TemperateGrassland)
        {
            GeneratedTerrainCell cell = CreateLand(biome);
            cell.WallBlockRuntimeId = new DefinitionRuntimeId(2);
            return cell;
        }

        /// <summary>地面を持たず指定Liquid Runtime IDだけを持つテスト用セルを作ります。</summary>
        /// <param name="runtimeId">水またはマグマを表す有効な仮Runtime IDです。</param>
        /// <returns>境界上は非接続となる液体セルです。</returns>
        private static GeneratedTerrainCell CreateLiquid(int runtimeId)
        {
            return new GeneratedTerrainCell
            {
                Biome = BiomeId.TemperateGrassland,
                LiquidRuntimeId = new DefinitionRuntimeId(runtimeId)
            };
        }

        /// <summary>
        /// 上下左右を個別指定し、斜め4方向には共通の地形を設定した8近傍を作ります。
        /// </summary>
        /// <param name="north">北側の地形です。</param>
        /// <param name="east">東側の地形です。</param>
        /// <param name="south">南側の地形です。</param>
        /// <param name="west">西側の地形です。</param>
        /// <param name="diagonal">斜め4方向へ共通で設定する地形です。省略時は通常土地です。</param>
        /// <returns>境界選択へ渡す8近傍です。</returns>
        private static TerrainNeighborTiles CreateNeighbors(
            GeneratedTerrainCell north,
            GeneratedTerrainCell east,
            GeneratedTerrainCell south,
            GeneratedTerrainCell west,
            GeneratedTerrainCell? diagonal = null)
        {
            GeneratedTerrainCell diagonalTile = diagonal ?? CreateLand();
            return new TerrainNeighborTiles(
                north,
                east,
                south,
                west,
                diagonalTile,
                diagonalTile,
                diagonalTile,
                diagonalTile);
        }
    }
}
