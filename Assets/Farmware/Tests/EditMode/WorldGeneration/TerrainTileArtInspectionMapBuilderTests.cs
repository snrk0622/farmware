// このファイルは、GrassLoam固定検品マップの素材契約とWall上面暗化の比較条件を検証します。
// Scene自体は開閉せず、現在のUnity Editor作業状態を変えないAsset検証と比較値確認だけを実行します。

using Farmware.WorldGeneration.Authoring;
using Farmware.WorldGeneration.Editor;
using NUnit.Framework;
using UnityEditor;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 固定検品マップがGroundTop共有Wallと境界合成Spriteだけを要求することを保証します。
    /// </summary>
    public sealed class TerrainTileArtInspectionMapBuilderTests
    {
        /// <summary>暗化比較が0.85から段階的に強くなる3条件であることを保証します。</summary>
        [Test]
        public void WallSurfaceDarkenComparison_UsesThreeDescendingFactors()
        {
            Assert.That(
                TerrainTileArtInspectionMapBuilder.WallSurfaceDarkenComparisonCount,
                Is.EqualTo(3));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetWallSurfaceDarkenComparisonFactor(0),
                Is.EqualTo(0.85f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetWallSurfaceDarkenComparisonFactor(1),
                Is.EqualTo(0.70f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetWallSurfaceDarkenComparisonFactor(2),
                Is.EqualTo(0.55f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                TerrainTileArtInspectionMapBuilder.GetWallSurfaceDarkenComparisonFactor(3));
        }

        /// <summary>
        /// 現在のGrassLoam設定からGroundTop、Face、全Face×Boundary組合せを読めることを確認します。
        /// </summary>
        [Test]
        public void ValidateGrassLoamAssets_ReturnsSharedTopAndBoundarySprites()
        {
            TerrainTileArtGenerationSettings settings =
                AssetDatabase.LoadAssetAtPath<TerrainTileArtGenerationSettings>(
                    TerrainTileArtInspectionMapBuilder.SettingsAssetPath);
            Assert.That(settings, Is.Not.Null);

            TerrainMaterialArtSource grassLoam = null;
            for (int materialIndex = 0;
                 materialIndex < settings.Materials.Length;
                 materialIndex++)
            {
                TerrainMaterialArtSource material = settings.Materials[materialIndex];
                if (material != null
                    && material.MaterialId
                    == TerrainTileArtInspectionMapBuilder.InspectedMaterialId)
                {
                    grassLoam = material;
                    break;
                }
            }

            Assert.That(grassLoam, Is.Not.Null);
            TerrainTileArtInspectionAssetSummary summary =
                TerrainTileArtInspectionMapBuilder.ValidateGrassLoamAssets();
            int expectedBoundaryCount =
                grassLoam.FaceSprites.Length * grassLoam.FaceBoundaryOverlays.Length;

            Assert.That(
                summary.GroundTopCount,
                Is.EqualTo(grassLoam.GroundTopSprites.Length));
            Assert.That(summary.FaceCount, Is.EqualTo(grassLoam.FaceSprites.Length));
            Assert.That(summary.GroundBoundaryCount, Is.EqualTo(expectedBoundaryCount));
            Assert.That(summary.WallBoundaryCount, Is.EqualTo(expectedBoundaryCount));
        }
    }
}
