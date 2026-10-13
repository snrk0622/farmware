// このファイルは、一体型の土地・岩壁Spriteと液面アニメーションに共通する表示寸法をSceneなしのEditModeで検証します。
// ChunkTilemapPresenterとEditor構築ツールが同じ定数を使い、Pivotや表示高がずれないことを素早く保証します。

using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 16×24土地、16×28岩壁、8px低い液面の寸法規則と液面分類を保証します。
    /// </summary>
    public sealed class TerrainLayerPresentationUtilityTests
    {
        /// <summary>
        /// 土地Spriteが上面16pxと断面8pxを持ち、論理セル下端へ合わせるPivotが2/3になることを検証します。
        /// </summary>
        [Test]
        public void CombinedTerrainSprite_UsesSixteenPixelSurfaceAndEightPixelFace()
        {
            Assert.That(TerrainLayerPresentationUtility.TerrainSurfaceHeightInPixels, Is.EqualTo(16));
            Assert.That(TerrainLayerPresentationUtility.TerrainFrontFaceHeightInPixels, Is.EqualTo(8));
            Assert.That(TerrainLayerPresentationUtility.CombinedTerrainSpriteHeightInPixels, Is.EqualTo(24));
            Assert.That(
                TerrainLayerPresentationUtility.CombinedTerrainPivotY,
                Is.EqualTo(2f / 3f).Within(0.0001f));
        }

        /// <summary>
        /// 岩壁Spriteが上面16pxと側面12pxを持ち、基準セルへ合わせるPivotが8/28になることを検証します。
        /// </summary>
        [Test]
        public void CombinedRockWallSprite_UsesSixteenPixelSurfaceAndTwelvePixelFace()
        {
            Assert.That(TerrainLayerPresentationUtility.RockWallElevationInPixels, Is.EqualTo(12));
            Assert.That(TerrainLayerPresentationUtility.CombinedRockWallSpriteHeightInPixels, Is.EqualTo(28));
            Assert.That(
                TerrainLayerPresentationUtility.CombinedRockWallPivotY,
                Is.EqualTo(2f / 7f).Within(0.0001f));
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
