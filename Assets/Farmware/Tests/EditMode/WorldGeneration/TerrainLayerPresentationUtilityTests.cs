// このファイルは、通常地形の手前断面、岩壁側面、液面アニメーションの分類規則をSceneなしのEditModeで検証します。
// ChunkTilemapPresenterが参照する純粋計算だけを対象とし、TilemapやECS Worldを作らず表示条件の回帰を素早く検出します。

using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// Forager風の直線的な手前断面と液面オーバーレイが、GeneratedTileを変更せず正しく分類されることを保証します。
    /// </summary>
    public sealed class TerrainLayerPresentationUtilityTests
    {
        /// <summary>
        /// 土地または岩壁下の土地の手前が水・マグマの場合だけ、Biomeに対応する4種類の断面が選ばれることを検証します。
        /// </summary>
        [Test]
        public void ClassifyTerrainFrontEdge_SelectsMaterialOnlyAboveLowland()
        {
            GeneratedTile water = new GeneratedTile { Ground = GroundType.Water };
            GeneratedTile magma = new GeneratedTile { Ground = GroundType.Magma };
            GeneratedTile normalLand = new GeneratedTile
            {
                Biome = BiomeId.Grassland,
                Ground = GroundType.Normal
            };
            GeneratedTile rockWallAboveGrassland = new GeneratedTile
            {
                Biome = BiomeId.Grassland,
                Ground = GroundType.RockWall
            };

            Assert.That(
                TerrainLayerPresentationUtility.ClassifyTerrainFrontEdge(normalLand, water),
                Is.EqualTo(TerrainFrontEdgeKind.Soil));
            Assert.That(
                TerrainLayerPresentationUtility.ClassifyTerrainFrontEdge(
                    new GeneratedTile { Biome = BiomeId.Desert, Ground = GroundType.Sand },
                    water),
                Is.EqualTo(TerrainFrontEdgeKind.Sand));
            Assert.That(
                TerrainLayerPresentationUtility.ClassifyTerrainFrontEdge(
                    new GeneratedTile { Biome = BiomeId.Volcanic, Ground = GroundType.Normal },
                    magma),
                Is.EqualTo(TerrainFrontEdgeKind.Rock));
            Assert.That(
                TerrainLayerPresentationUtility.ClassifyTerrainFrontEdge(
                    new GeneratedTile { Biome = BiomeId.Snowfield, Ground = GroundType.Normal },
                    water),
                Is.EqualTo(TerrainFrontEdgeKind.Snow));
            Assert.That(
                TerrainLayerPresentationUtility.ClassifyTerrainFrontEdge(normalLand, normalLand),
                Is.EqualTo(TerrainFrontEdgeKind.None),
                "基本地形同士には断面を付けず、オートタイル化しない必要があります。");
            Assert.That(
                TerrainLayerPresentationUtility.ClassifyTerrainFrontEdge(
                    rockWallAboveGrassland,
                    water),
                Is.EqualTo(TerrainFrontEdgeKind.Soil),
                "岩壁が低地へ面する場所にも、岩壁下の土地ブロック断面を残す必要があります。");
            Assert.That(
                TerrainLayerPresentationUtility.ShouldDisplayRockWallFrontFace(
                    rockWallAboveGrassland,
                    water),
                Is.True,
                "低地へ面する岩壁では、土地断面と同じセルへ岩壁側面も重ねる必要があります。");
            Assert.That(
                TerrainLayerPresentationUtility.ClassifyTerrainFrontEdge(water, magma),
                Is.EqualTo(TerrainFrontEdgeKind.None),
                "液体セル自身へ通常地形の断面を重ねてはいけません。");
        }

        /// <summary>
        /// 岩壁の上面16pxと側面12pxが1:0.75になり、Tilemap用セル換算も同じ値になることを検証します。
        /// </summary>
        [Test]
        public void RockWallElevation_UsesThreeQuarterSideRatio()
        {
            Assert.That(TerrainLayerPresentationUtility.RockWallElevationInPixels, Is.EqualTo(12));
            Assert.That(TerrainLayerPresentationUtility.RockWallElevationInCells, Is.EqualTo(0.75f));
        }

        /// <summary>
        /// 液面が16pxセルの半分に当たる8pxだけ画面下へ移動し、整数ピクセル境界を維持することを検証します。
        /// </summary>
        [Test]
        public void LiquidSurfaceOffset_MovesDownByHalfTile()
        {
            Assert.That(TerrainLayerPresentationUtility.LiquidSurfaceDepthInPixels, Is.EqualTo(8));
            Assert.That(
                TerrainLayerPresentationUtility.LiquidSurfaceOffsetInCells,
                Is.EqualTo(-0.5f));
        }

        /// <summary>
        /// 岩壁側面が最前列の岩壁だけに表示され、岩壁が縦に連続する内部では表示されないことを検証します。
        /// </summary>
        [Test]
        public void ShouldDisplayRockWallFrontFace_ReturnsTrueOnlyForFrontRow()
        {
            GeneratedTile rockWall = new GeneratedTile { Ground = GroundType.RockWall };
            GeneratedTile openLand = new GeneratedTile { Ground = GroundType.Normal };

            Assert.That(
                TerrainLayerPresentationUtility.ShouldDisplayRockWallFrontFace(rockWall, openLand),
                Is.True);
            Assert.That(
                TerrainLayerPresentationUtility.ShouldDisplayRockWallFrontFace(rockWall, rockWall),
                Is.False);
            Assert.That(
                TerrainLayerPresentationUtility.ShouldDisplayRockWallFrontFace(openLand, openLand),
                Is.False);
        }

        /// <summary>
        /// 淡水・海水・マグマが別のアニメーション種類へ分類され、通常地形には何も重ならないことを検証します。
        /// </summary>
        [Test]
        public void ClassifyLiquidAnimation_DistinguishesLiquidTypes()
        {
            GeneratedTile freshWater = new GeneratedTile
            {
                Ground = GroundType.Water,
                WaterSalinity = WaterSalinity.FreshWater
            };
            GeneratedTile saltWater = new GeneratedTile
            {
                Ground = GroundType.Water,
                WaterSalinity = WaterSalinity.SaltWater
            };
            GeneratedTile magma = new GeneratedTile { Ground = GroundType.Magma };
            GeneratedTile land = new GeneratedTile { Ground = GroundType.Normal };

            Assert.That(
                TerrainLayerPresentationUtility.ClassifyLiquidAnimation(freshWater),
                Is.EqualTo(LiquidAnimationKind.FreshWater));
            Assert.That(
                TerrainLayerPresentationUtility.ClassifyLiquidAnimation(saltWater),
                Is.EqualTo(LiquidAnimationKind.SaltWater));
            Assert.That(
                TerrainLayerPresentationUtility.ClassifyLiquidAnimation(magma),
                Is.EqualTo(LiquidAnimationKind.Magma));
            Assert.That(
                TerrainLayerPresentationUtility.ClassifyLiquidAnimation(land),
                Is.EqualTo(LiquidAnimationKind.None));
        }
    }
}
