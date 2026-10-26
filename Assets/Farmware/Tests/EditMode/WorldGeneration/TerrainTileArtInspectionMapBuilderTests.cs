// このファイルは、代表5材質の固定順・可変長素材契約・同一地面上の2段Wall・固定接地影・動的影検品レイアウトと、GrassLoamを使う3段の崖勾配、連続水深、U字の仮波線、Wall上面暗化、8pxのWall折れ目影を検証します。
// Scene自体は開閉せず、現在のUnity Editor作業状態を変えないAsset検証と固定値確認だけを実行します。

using Farmware.WorldGeneration.Authoring;
using Farmware.WorldGeneration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 固定検品マップが代表5材質の全差分と、GrassLoamの境界合成Sprite、3種類の仮液体を要求することを保証します。
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
        /// 代表材質の周囲Ground付きパッチ、Wall前面2段、C案と同じ影条件、横3×手前方向10セルの穴、3段の崖、45px／93pxの連続水深勾配、3px低い水面を保証します。
        /// </summary>
        [Test]
        public void InspectionDepths_UseConfiguredHoleWallAndLiquidValues()
        {
            Assert.That(TerrainTileArtInspectionMapBuilder.GroundFaceDepth, Is.EqualTo(3));
            Assert.That(TerrainTileArtInspectionMapBuilder.WallFaceDepth, Is.EqualTo(2));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.RepresentativeGroundPatchMinimumWidth,
                Is.EqualTo(8));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.RepresentativeGroundPatchHeight,
                Is.EqualTo(7));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.RepresentativeGroundSideMargin,
                Is.EqualTo(2));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.RepresentativeGroundFrontMargin,
                Is.EqualTo(2));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.RepresentativeGroundBackMargin,
                Is.EqualTo(2));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.RepresentativeWallTopLocalY,
                Is.EqualTo(4));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.RepresentativePointLightsPerPanel,
                Is.EqualTo(TerrainWallShadowComparisonMapBuilder.PointLightsPerPanel));
            Assert.That(
                TerrainWallShadowComparisonMapBuilder.DynamicShadowCasterOffsetYInTiles,
                Is.EqualTo(-TerrainTileArtInspectionMapBuilder.WallFaceDepth));
            Assert.That(TerrainTileArtInspectionMapBuilder.InspectionHoleWidth, Is.EqualTo(3));
            Assert.That(TerrainTileArtInspectionMapBuilder.InspectionHoleDepth, Is.EqualTo(10));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.UnderwaterPreviewStartDepth,
                Is.EqualTo(3));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.WaterSurfaceOffsetPixels,
                Is.EqualTo(3));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.UnderwaterFloorWaterFadePixels,
                Is.EqualTo(45));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.MissingUnderwaterWaterFadePixels,
                Is.EqualTo(93));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.MissingUnderwaterVisibleFaceDepth,
                Is.EqualTo(6));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.UnderwaterFloorWaterMaximumAlpha,
                Is.EqualTo(0.50f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.MissingUnderwaterWaterMaximumAlpha,
                Is.EqualTo(1.00f));
            Assert.That(TerrainTileArtInspectionMapBuilder.DryCliffFadePixels, Is.EqualTo(48));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.WaterEdgeWaveThicknessPixels,
                Is.EqualTo(1));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.WaterEdgeWaveColorCount,
                Is.EqualTo(2));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.WallFoldShadowFadePixels,
                Is.EqualTo(8));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.WallFoldShadowTopAlpha,
                Is.EqualTo(0.35f));

            // 折れ目影は上端を最も濃くし、8px目で完全透過へ到達して、それより下のFaceを暗くしません。
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetWallFoldShadowAlpha(0),
                Is.EqualTo(0.35f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetWallFoldShadowAlpha(1),
                Is.EqualTo(0.30f).Within(0.0001f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetWallFoldShadowAlpha(2),
                Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetWallFoldShadowAlpha(3),
                Is.EqualTo(0.20f).Within(0.0001f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetWallFoldShadowAlpha(4),
                Is.EqualTo(0.15f).Within(0.0001f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetWallFoldShadowAlpha(5),
                Is.EqualTo(0.10f).Within(0.0001f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetWallFoldShadowAlpha(6),
                Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetWallFoldShadowAlpha(7),
                Is.EqualTo(0f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetWallFoldShadowAlpha(8),
                Is.EqualTo(0f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                TerrainTileArtInspectionMapBuilder.GetWallFoldShadowAlpha(-1));

            // 乾燥崖はFaceが上端で完全表示され、3タイル下の48px地点で完全に黒へ溶けます。
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetContinuousDryCliffFaceAlpha(0f),
                Is.EqualTo(1f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetContinuousDryCliffFaceAlpha(24f),
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetContinuousDryCliffFaceAlpha(48f),
                Is.EqualTo(0f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetContinuousDryCliffFaceAlpha(160f),
                Is.EqualTo(0f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                TerrainTileArtInspectionMapBuilder.GetContinuousDryCliffFaceAlpha(-1f));

            // 穴上端3pxまでは描かず、直下床ありは合計48px位置、床なしは合計96px位置で終端へ達します。
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetContinuousWaterOverlayAlpha(2f, true),
                Is.EqualTo(0f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetContinuousWaterOverlayAlpha(3f, true),
                Is.EqualTo(0f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetContinuousWaterOverlayAlpha(48f, true),
                Is.EqualTo(0.50f).Within(0.0001f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetContinuousWaterOverlayAlpha(48f, false),
                Is.EqualTo(45f / 93f).Within(0.0001f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetContinuousWaterOverlayAlpha(96f, false),
                Is.EqualTo(1.00f).Within(0.0001f));
            Assert.That(
                TerrainTileArtInspectionMapBuilder.GetContinuousWaterOverlayAlpha(160f, false),
                Is.EqualTo(1.00f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                TerrainTileArtInspectionMapBuilder.GetContinuousWaterOverlayAlpha(-1f, true));
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
            Assert.That(summary.LiquidPlaceholderCount, Is.EqualTo(3));
            Assert.That(
                AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(
                    TerrainTileArtInspectionMapBuilder.FreshWaterPlaceholderPath),
                Is.Not.Null);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(
                    TerrainTileArtInspectionMapBuilder.SaltWaterPlaceholderPath),
                Is.Not.Null);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(
                    TerrainTileArtInspectionMapBuilder.MagmaPlaceholderPath),
                Is.Not.Null);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(
                    TerrainTileArtInspectionMapBuilder.UnderwaterObjectPlaceholderPath),
                Is.Not.Null);
        }

        /// <summary>
        /// 代表5材質が固定順で読み込まれ、各材質の可変長GroundTop・Face・境界枚数を省略せず返すことを保証します。
        /// </summary>
        [Test]
        public void ValidateRepresentativeMaterials_ReturnsAllConfiguredVariantsInStableOrder()
        {
            TerrainTileArtGenerationSettings settings =
                AssetDatabase.LoadAssetAtPath<TerrainTileArtGenerationSettings>(
                    TerrainTileArtInspectionMapBuilder.SettingsAssetPath);
            Assert.That(settings, Is.Not.Null);

            string[] expectedMaterialIds =
            {
                "GrassLoam",
                "DesertSand",
                "Sandstone",
                "Granite",
                "Basalt"
            };
            Assert.That(
                TerrainTileArtInspectionMapBuilder.RepresentativeMaterialCount,
                Is.EqualTo(expectedMaterialIds.Length));

            TerrainTileArtInspectionMaterialSummary[] summaries =
                TerrainTileArtInspectionMapBuilder.ValidateRepresentativeMaterialAssets();
            Assert.That(summaries, Has.Length.EqualTo(expectedMaterialIds.Length));

            for (int expectedIndex = 0;
                 expectedIndex < expectedMaterialIds.Length;
                 expectedIndex++)
            {
                string expectedMaterialId = expectedMaterialIds[expectedIndex];
                Assert.That(
                    TerrainTileArtInspectionMapBuilder.GetRepresentativeMaterialId(
                        expectedIndex),
                    Is.EqualTo(expectedMaterialId));
                Assert.That(summaries[expectedIndex].MaterialId, Is.EqualTo(expectedMaterialId));

                // 設定側の可変長配列と比較し、Scene用検証が4枚などの固定数へ丸めていないことを確認します。
                TerrainMaterialArtSource material = null;
                for (int materialIndex = 0;
                     materialIndex < settings.Materials.Length;
                     materialIndex++)
                {
                    TerrainMaterialArtSource candidate = settings.Materials[materialIndex];
                    if (candidate != null && candidate.MaterialId == expectedMaterialId)
                    {
                        material = candidate;
                        break;
                    }
                }

                Assert.That(material, Is.Not.Null);
                int expectedBoundaryCount =
                    material.FaceSprites.Length * material.FaceBoundaryOverlays.Length;
                Assert.That(
                    summaries[expectedIndex].GroundTopCount,
                    Is.EqualTo(material.GroundTopSprites.Length));
                Assert.That(
                    summaries[expectedIndex].FaceCount,
                    Is.EqualTo(material.FaceSprites.Length));
                Assert.That(
                    summaries[expectedIndex].GroundBoundaryCount,
                    Is.EqualTo(expectedBoundaryCount));
                Assert.That(
                    summaries[expectedIndex].WallBoundaryCount,
                    Is.EqualTo(expectedBoundaryCount));
            }

            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                TerrainTileArtInspectionMapBuilder.GetRepresentativeMaterialId(-1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                TerrainTileArtInspectionMapBuilder.GetRepresentativeMaterialId(
                    expectedMaterialIds.Length));
        }

        /// <summary>
        /// Scene再構築で生成した連続深度Shader、波線Shader、折れ目影Shader、9種類のMaterialを再読込できることを保証します。
        /// </summary>
        [Test]
        public void GeneratedLiquidPreviewAssets_ArePersistentAndComplete()
        {
            Shader depthShader = AssetDatabase.LoadAssetAtPath<Shader>(
                TerrainTileArtInspectionMapBuilder.ContinuousDepthOverlayShaderPath);
            Assert.That(depthShader, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(depthShader), Is.False);
            Shader waveShader = AssetDatabase.LoadAssetAtPath<Shader>(
                TerrainTileArtInspectionMapBuilder.AnimatedWaterEdgeWaveShaderPath);
            Assert.That(waveShader, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(waveShader), Is.False);
            Shader wallFoldShadowShader = AssetDatabase.LoadAssetAtPath<Shader>(
                TerrainTileArtInspectionMapBuilder.WallFoldShadowShaderPath);
            Assert.That(wallFoldShadowShader, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(wallFoldShadowShader), Is.False);

            string folder = TerrainTileArtInspectionMapBuilder.GeneratedPreviewAssetFolder;
            Material dryCliffFade = AssetDatabase.LoadAssetAtPath<Material>(
                $"{folder}/DryCliffFade.mat");
            Assert.That(dryCliffFade, Is.Not.Null);
            Assert.That(dryCliffFade.GetFloat("_ClippedTopRatio"), Is.EqualTo(0f));
            Assert.That(dryCliffFade.GetFloat("_FadeRatio"), Is.EqualTo(0.3f));
            Assert.That(dryCliffFade.GetFloat("_MaximumAlpha"), Is.EqualTo(1f));
            Assert.That(
                AssetDatabase.LoadAssetAtPath<Material>($"{folder}/FreshWaterWithFloor.mat"),
                Is.Not.Null);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<Material>($"{folder}/SaltWaterWithFloor.mat"),
                Is.Not.Null);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<Material>($"{folder}/SaltWaterWithoutFloor.mat"),
                Is.Not.Null);
            Material freshHorizontalWave = AssetDatabase.LoadAssetAtPath<Material>(
                $"{folder}/FreshWaterEdgeWaveHorizontal.mat");
            Material freshVerticalWave = AssetDatabase.LoadAssetAtPath<Material>(
                $"{folder}/FreshWaterEdgeWaveVertical.mat");
            Assert.That(freshHorizontalWave, Is.Not.Null);
            Assert.That(freshVerticalWave, Is.Not.Null);
            Assert.That(freshHorizontalWave.GetFloat("_PixelLength"), Is.EqualTo(48f));
            Assert.That(freshVerticalWave.GetFloat("_PixelLength"), Is.EqualTo(157f));
            Assert.That(
                AssetDatabase.LoadAssetAtPath<Material>(
                    $"{folder}/SaltWaterEdgeWaveHorizontal.mat"),
                Is.Not.Null);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<Material>(
                    $"{folder}/SaltWaterEdgeWaveVertical.mat"),
                Is.Not.Null);
            Material wallFoldShadow = AssetDatabase.LoadAssetAtPath<Material>(
                $"{folder}/WallFoldShadow.mat");
            Assert.That(wallFoldShadow, Is.Not.Null);
            Assert.That(wallFoldShadow.shader, Is.EqualTo(wallFoldShadowShader));
            Assert.That(wallFoldShadow.GetFloat("_TopAlpha"), Is.EqualTo(0.35f));
            Assert.That(wallFoldShadow.GetFloat("_FadePixelCount"), Is.EqualTo(8f));
            Assert.That(wallFoldShadow.GetFloat("_SpritePixelHeight"), Is.EqualTo(16f));

            // 旧飛沫方式を同時に残すと二重表示になるため、置換後は旧Animation資産を受理しません。
            Assert.That(
                AssetDatabase.LoadAssetAtPath<Object>(
                    $"{folder}/WaterContactSplashPlaceholder.anim"),
                Is.Null);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<Object>(
                    $"{folder}/WaterContactSplashPlaceholder.controller"),
                Is.Null);

            // 旧4フレーム画像も残さず、Shader波線と旧飛沫が同時に描画されないことを保証します。
            for (int frameIndex = 0; frameIndex < 4; frameIndex++)
            {
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<Sprite>(
                        $"{folder}/WaterContactSplash_{frameIndex:D2}.png"),
                    Is.Null);
            }
        }
    }
}
