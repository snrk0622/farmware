// このファイルは、再生成した共通境界ハイライトの画素、14分割、Atlas、47状態検品契約を検証します。
// 論理マスクとPNGの形状がずれたままSampleSceneへ保存される回帰を、EditModeで事前に検出します。

using System;
using System.IO;
using System.Linq;
using Farmware.WorldGeneration.Editor;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Sprites;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 境界ハイライトが16×16の14基本形、1px白線、1px内角、20%表示へ接続できることを保証します。
    /// </summary>
    public sealed class TerrainBoundaryHighlightArtGeneratorTests
    {
        /// <summary>PNGが14枚へ正しい順番で分割され、Pixel Art向けImporter設定を持つことを確認します。</summary>
        [Test]
        public void SpriteSheet_UsesExpectedSlicesAndPixelArtSettings()
        {
            string texturePath = WorldGenerationSpriteAtlasBuilder.BoundaryHighlightTexturePath;
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            Assert.That(importer, Is.Not.Null, $"TextureImporterを取得できません: {texturePath}");
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Multiple));
            Assert.That(
                importer.spritePixelsPerUnit,
                Is.EqualTo(TerrainBoundaryHighlightArtGenerator.SpriteSizeInPixels));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(importer.alphaIsTransparency, Is.True);

            TextureImporterSettings importerSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(importerSettings);
            Assert.That(importerSettings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect));
            Assert.That(importerSettings.spriteGenerateFallbackPhysicsShape, Is.False);

            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(texturePath)
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.rect.x)
                .ToArray();
            string[] expectedNames =
                WorldGenerationSpriteAtlasBuilder.GetExpectedBoundaryHighlightSpriteNames();
            Assert.That(sprites, Has.Length.EqualTo(expectedNames.Length));

            for (int index = 0; index < sprites.Length; index++)
            {
                Sprite sprite = sprites[index];
                Assert.That(sprite.name, Is.EqualTo(expectedNames[index]));
                Assert.That(
                    sprite.rect.x,
                    Is.EqualTo(index * TerrainBoundaryHighlightArtGenerator.SpriteSizeInPixels));
                Assert.That(sprite.rect.y, Is.Zero);
                Assert.That(
                    sprite.rect.width,
                    Is.EqualTo(TerrainBoundaryHighlightArtGenerator.SpriteSizeInPixels));
                Assert.That(
                    sprite.rect.height,
                    Is.EqualTo(TerrainBoundaryHighlightArtGenerator.SpriteSizeInPixels));
                Assert.That(sprite.pivot.x, Is.EqualTo(8f).Within(0.001f));
                Assert.That(sprite.pivot.y, Is.EqualTo(8f).Within(0.001f));
            }
        }

        /// <summary>PNGの全画素が論理マスクから求めた透明または不透明白と一致することを確認します。</summary>
        [Test]
        public void TexturePixels_MatchCanonicalFeatureMasks()
        {
            string assetPath = WorldGenerationSpriteAtlasBuilder.BoundaryHighlightTexturePath;
            string absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            Assert.That(File.Exists(absolutePath), Is.True, $"境界ハイライトPNGが見つかりません: {assetPath}");

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            try
            {
                byte[] pngBytes = File.ReadAllBytes(absolutePath);
                Assert.That(
                    ImageConversion.LoadImage(texture, pngBytes, markNonReadable: false),
                    Is.True,
                    $"境界ハイライトPNGを読み込めません: {assetPath}");
                Assert.That(
                    texture.width,
                    Is.EqualTo(TerrainBoundaryHighlightArtGenerator.TextureWidthInPixels));
                Assert.That(
                    texture.height,
                    Is.EqualTo(TerrainBoundaryHighlightArtGenerator.SpriteSizeInPixels));

                Color32[] pixels = texture.GetPixels32();
                for (int shapeIndex = 0;
                     shapeIndex < TerrainBoundaryHighlightUtility.ExpectedSpriteCount;
                     shapeIndex++)
                {
                    BoundaryHighlightShape shape = (BoundaryHighlightShape)(shapeIndex + 1);
                    byte featureMask = TerrainBoundaryHighlightUtility.GetCanonicalFeatureMask(shape);
                    int visiblePixelCount = 0;

                    for (int y = 0;
                         y < TerrainBoundaryHighlightArtGenerator.SpriteSizeInPixels;
                         y++)
                    {
                        for (int localX = 0;
                             localX < TerrainBoundaryHighlightArtGenerator.SpriteSizeInPixels;
                             localX++)
                        {
                            int textureX =
                                (shapeIndex * TerrainBoundaryHighlightArtGenerator.SpriteSizeInPixels)
                                + localX;
                            Color32 pixel = pixels[(y * texture.width) + textureX];
                            bool shouldBeVisible =
                                TerrainBoundaryHighlightArtGenerator.ShouldPixelBeVisible(
                                    featureMask,
                                    localX,
                                    y);
                            Assert.That(
                                pixel.a != 0,
                                Is.EqualTo(shouldBeVisible),
                                $"境界特徴と画素配置が一致しません: shape={shapeIndex + 1}, "
                                + $"mask=0x{featureMask:X2}, ({localX}, {y})");

                            if (!shouldBeVisible)
                            {
                                Assert.That(pixel.a, Is.Zero);
                                continue;
                            }

                            visiblePixelCount++;
                            Assert.That(pixel, Is.EqualTo(new Color32(255, 255, 255, 255)));
                        }
                    }

                    Assert.That(
                        visiblePixelCount,
                        Is.GreaterThan(0),
                        $"可視画素のない境界形状があります: shape={shapeIndex + 1}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>専用Atlasが境界PNGだけをPackableとして持ち、Pixel Art向け設定を維持することを確認します。</summary>
        [Test]
        public void BoundaryAtlas_ContainsOnlyBoundaryTextureAndUsesPixelArtSettings()
        {
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(
                WorldGenerationSpriteAtlasBuilder.BoundaryHighlightAtlasPath);
            Assert.That(atlas, Is.Not.Null, "境界ハイライトAtlasが見つかりません。");

            string[] actualPaths = atlas.GetPackables()
                .Select(AssetDatabase.GetAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            string[] expectedPaths = WorldGenerationSpriteAtlasBuilder
                .GetBoundaryHighlightSourceTexturePaths()
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.That(actualPaths, Is.EqualTo(expectedPaths));

            SpriteAtlasImporter importer = AssetImporter.GetAtPath(
                WorldGenerationSpriteAtlasBuilder.BoundaryHighlightAtlasPath)
                as SpriteAtlasImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.includeInBuild, Is.True);
            Assert.That(importer.textureSettings.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.textureSettings.generateMipMaps, Is.False);
            Assert.That(importer.packingSettings.padding,
                Is.EqualTo(WorldGenerationSpriteAtlasBuilder.AtlasPaddingInPixels));
            Assert.That(importer.packingSettings.enableRotation, Is.False);
            Assert.That(importer.packingSettings.enableTightPacking, Is.False);
            Assert.That(importer.packingSettings.enableAlphaDilation, Is.False);
        }

        /// <summary>14基本Spriteが専用Atlasの同じTextureページへ実際にパックされることを確認します。</summary>
        [Test]
        public void BoundaryAtlas_PacksEveryShapeIntoOneTexture()
        {
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(
                WorldGenerationSpriteAtlasBuilder.BoundaryHighlightAtlasPath);
            Assert.That(atlas, Is.Not.Null);
            SpriteAtlasUtility.PackAtlases(
                new[] { atlas },
                EditorUserBuildSettings.activeBuildTarget,
                canCancel: false);

            Sprite[] sprites = TerrainBoundaryHighlightArtGenerator.LoadRequiredSprites();
            Texture2D sharedPackedTexture = null;
            for (int index = 0; index < sprites.Length; index++)
            {
                Sprite sprite = sprites[index];
                Texture2D packedTexture = SpriteUtility.GetSpriteTexture(sprite, getAtlasData: true);
                Assert.That(sprite.packed, Is.True, $"Atlasへ未収録の境界Spriteがあります: {sprite.name}");
                Assert.That(packedTexture, Is.Not.Null);

                if (sharedPackedTexture == null)
                {
                    sharedPackedTexture = packedTexture;
                }
                else
                {
                    Assert.That(
                        packedTexture,
                        Is.SameAs(sharedPackedTexture),
                        $"境界Spriteが別Textureページへ分かれています: {sprite.name}");
                }
            }
        }

        /// <summary>固定検品Sceneの入力が14基本形、47状態、完全接続を除く46表示状態を持つことを確認します。</summary>
        [Test]
        public void InspectionMapContract_CoversAllFortySevenStates()
        {
            TerrainBoundaryHighlightInspectionSummary summary =
                TerrainBoundaryHighlightInspectionMapBuilder.ValidateAssetsAndStates();
            Assert.That(
                summary.BasicSpriteCount,
                Is.EqualTo(TerrainBoundaryHighlightUtility.ExpectedSpriteCount));
            Assert.That(summary.NormalizedStateCount, Is.EqualTo(47));
            Assert.That(summary.VisibleHighlightStateCount, Is.EqualTo(46));
        }

        /// <summary>
        /// Wall動的影比較が同一形状の3パネルを持ち、ハイライト、動的影のみ、固定接地影ありを比較できることを確認します。
        /// 真上の主光と左手前の検証光、左右対称の固定接地影の必要数も固定し、比較条件が意図せず変わる回帰を防ぎます。
        /// </summary>
        [Test]
        public void WallShadowComparison_UsesEquivalentHighlightDynamicAndContactShadowPanels()
        {
            TerrainWallShadowComparisonSummary summary =
                TerrainWallShadowComparisonMapBuilder.ValidateAssetsAndLayout();

            Assert.That(summary.PanelCount, Is.EqualTo(3));
            Assert.That(summary.HighlightPanelCount, Is.EqualTo(1));
            Assert.That(summary.ContactShadowPanelCount, Is.EqualTo(1));
            Assert.That(summary.WallCellCountPerPanel, Is.EqualTo(13));
            Assert.That(summary.ExposedFaceColumnCountPerPanel, Is.EqualTo(5));
            Assert.That(summary.FaceDepth, Is.EqualTo(2));
            Assert.That(
                TerrainWallShadowComparisonMapBuilder.DynamicShadowCasterOffsetYInTiles,
                Is.EqualTo(-summary.FaceDepth));
            Assert.That(summary.FaceSpriteCountPerPanel, Is.EqualTo(10));
            Assert.That(summary.ShadowCasterCount, Is.EqualTo(15));
            Assert.That(
                summary.ShadowCasterCount,
                Is.EqualTo(summary.ExposedFaceColumnCountPerPanel * summary.PanelCount),
                "全Wallセルではなく、Faceが露出する前縁だけへCasterを作る必要があります。");
            Assert.That(summary.CompositeShadowCasterCount, Is.EqualTo(3));
            Assert.That(summary.PointLightCount, Is.EqualTo(12));
            Assert.That(summary.ContactShadowSpriteCount, Is.EqualTo(36));
            Assert.That(TerrainWallShadowComparisonMapBuilder.PointLightsPerPanel, Is.EqualTo(2));
            Assert.That(
                TerrainWallShadowComparisonMapBuilder.PointLightComponentsPerPosition,
                Is.EqualTo(2),
                "各光源位置はGround用影あり灯とWall用影なし灯の1組で構成する必要があります。");
            Assert.That(
                SortingLayer.NameToID(TerrainWallShadowUtility.WallSortingLayerName),
                Is.Not.EqualTo(SortingLayer.NameToID(
                    TerrainWallShadowUtility.GroundShadowReceiverSortingLayerName)));
            TerrainWallShadowComparisonMapBuilder.ValidateTerrainWallSortingLayerConfiguration();
            Assert.That(TerrainWallShadowComparisonMapBuilder.PointLightOuterRadius, Is.EqualTo(8f));
            Assert.That(TerrainWallShadowComparisonMapBuilder.FrontLeftLightIntensity, Is.EqualTo(0.65f));
            Assert.That(TerrainWallShadowComparisonMapBuilder.ContactShadowHorizontalSpreadPixels, Is.EqualTo(1));
            Assert.That(TerrainWallShadowComparisonMapBuilder.ContactShadowOffsetYPixels, Is.EqualTo(-3));
            Assert.That(TerrainWallShadowComparisonMapBuilder.ContactShadowLayerOpacity, Is.EqualTo(0.22f));
        }

        /// <summary>
        /// 折れ目影8／16／32pxと境界ハイライトの専用比較が代表5材質×4方式を使い、補助表現以外のFace、接地影、動的影、照明を同数に揃えることを確認します。
        /// </summary>
        [Test]
        public void WallEdgeCueComparison_ChangesOnlyFoldShadowAndBoundaryHighlight()
        {
            // Assetを読み込んで固定件数を算出し、Sceneを開かなくても比較条件の崩れを検出します。
            TerrainWallEdgeCueComparisonSummary summary =
                TerrainWallShadowComparisonMapBuilder.ValidateEdgeCueAssetsAndLayout();

            Assert.That(summary.PanelCount, Is.EqualTo(20));
            Assert.That(summary.FoldShadowPanelCount, Is.EqualTo(15));
            Assert.That(summary.HighlightPanelCount, Is.EqualTo(5));
            Assert.That(summary.WallCellCountPerPanel, Is.EqualTo(13));
            Assert.That(summary.ExposedFaceColumnCountPerPanel, Is.EqualTo(5));
            Assert.That(summary.FaceDepth, Is.EqualTo(2));
            Assert.That(summary.FaceSpriteCountPerPanel, Is.EqualTo(10));
            Assert.That(summary.FoldShadowSpriteCount, Is.EqualTo(100));
            Assert.That(summary.HighlightSpriteCount, Is.EqualTo(60));
            Assert.That(summary.ContactShadowSpriteCount, Is.EqualTo(720));
            Assert.That(summary.ShadowCasterCount, Is.EqualTo(100));
            Assert.That(
                summary.ShadowCasterCount,
                Is.EqualTo(summary.ExposedFaceColumnCountPerPanel * summary.PanelCount),
                "補助表現比較でも奥側Wallへ自己影Casterを残してはいけません。");
            Assert.That(summary.CompositeShadowCasterCount, Is.EqualTo(20));
            Assert.That(summary.PointLightCount, Is.EqualTo(80));
        }

        /// <summary>内角が仕様どおり角の1画素だけになり、隣接画素へ伸びないことを確認します。</summary>
        [Test]
        public void InnerCorner_UsesSinglePixel()
        {
            const byte northEastInnerCorner = 1 << 4;
            Assert.That(
                TerrainBoundaryHighlightArtGenerator.ShouldPixelBeVisible(
                    northEastInnerCorner,
                    15,
                    15),
                Is.True);
            Assert.That(
                TerrainBoundaryHighlightArtGenerator.ShouldPixelBeVisible(
                    northEastInnerCorner,
                    13,
                    15),
                Is.False);
            Assert.That(
                TerrainBoundaryHighlightArtGenerator.ShouldPixelBeVisible(
                    northEastInnerCorner,
                    15,
                    13),
                Is.False);
            Assert.That(
                TerrainBoundaryHighlightArtGenerator.ShouldPixelBeVisible(
                    northEastInnerCorner,
                    12,
                    15),
                Is.False);
            Assert.That(
                TerrainBoundaryHighlightArtGenerator.ShouldPixelBeVisible(
                    northEastInnerCorner,
                    15,
                    12),
                Is.False);
        }
    }
}
