// このファイルは、本番用バイオーム土地・フォールバック岩壁・境界ハイライトのSprite Atlas V2設定と、各Atlasが1ページへ収まることを検証します。
// Tilemapの見た目を画像比較する代わりに、誤描画の原因だったTexture分割をEditModeで直接保証します。

using System;
using System.IO;
using System.Linq;
using Farmware.WorldGeneration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Sprites;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 土地・岩壁AtlasがBuilt-in配信とPixel Art向け設定を持ち、全土地Spriteが1枚のTextureへパックされることを保証します。
    /// </summary>
    public sealed class WorldGenerationSpriteAtlasTests
    {
        /// <summary>
        /// Sprite PackerがAtlas V2で有効になり、Atlasアセットだけ存在して実際にはパックされない状態を防ぎます。
        /// </summary>
        [Test]
        public void SpritePackerMode_IsSpriteAtlasV2()
        {
            Assert.That(
                EditorSettings.spritePackerMode,
                Is.EqualTo(SpritePackerMode.SpriteAtlasV2),
                "Sprite Packer ModeはSprite Atlas V2である必要があります。");
        }

        /// <summary>
        /// 土地Atlasが7バイオームを収録した本番用TextureだけをPackableとして持ち、Fallback、液体、資源を誤って収録していないことを検証します。
        /// </summary>
        [Test]
        public void GroundTerrainAtlas_ContainsAllTerrainSourceTextures()
        {
            AssertAtlasPackables(
                WorldGenerationSpriteAtlasBuilder.GroundTerrainAtlasPath,
                WorldGenerationSpriteAtlasBuilder.GetGroundTerrainSourceTexturePaths());
        }

        /// <summary>
        /// 本番用土地シートが左からBiome ID順の7枚へSliceされ、16×24土地向けのPixel Art設定を持つことを検証します。
        /// </summary>
        [Test]
        public void ProductionBiomeSpriteSheet_UsesExpectedSlicesAndPixelArtSettings()
        {
            string texturePath = WorldGenerationSpriteAtlasBuilder.ProductionBiomeTerrainTexturePath;
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            Assert.That(importer, Is.Not.Null, $"TextureImporterを取得できません: {texturePath}");
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Multiple));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(16f));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));

            // Unity 6.3ではMesh TypeとFallback Physics ShapeがTextureImporter本体から設定構造体へ移動しています。
            // 実際のImporter設定を読み戻し、矩形Tileへ不要な輪郭処理やCollider形状が混ざらないことを保証します。
            TextureImporterSettings importerSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(importerSettings);
            Assert.That(importerSettings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect));
            Assert.That(importerSettings.spriteGenerateFallbackPhysicsShape, Is.False);

            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(texturePath)
                .OfType<Sprite>()
                .OrderBy(sprite => sprite.rect.x)
                .ToArray();
            string[] expectedNames = WorldGenerationSpriteAtlasBuilder.GetExpectedBiomeSpriteNames();
            Assert.That(sprites, Has.Length.EqualTo(expectedNames.Length));

            for (int index = 0; index < sprites.Length; index++)
            {
                Sprite sprite = sprites[index];

                // 112×24画像を左から16pxずつ読むことで、Biome IDとSprite名の対応を固定します。
                Assert.That(sprite.name, Is.EqualTo(expectedNames[index]));
                Assert.That(sprite.rect.x, Is.EqualTo(index * 16f));
                Assert.That(sprite.rect.y, Is.Zero);
                Assert.That(sprite.rect.width, Is.EqualTo(16f));
                Assert.That(sprite.rect.height, Is.EqualTo(24f));
                Assert.That(sprite.pivot.x, Is.EqualTo(8f).Within(0.001f));
                Assert.That(sprite.pivot.y, Is.EqualTo(16f).Within(0.001f));
            }
        }

        /// <summary>
        /// 7個の本番用Tileが同名のSlice Spriteを参照し、Colliderを持たない表示専用アセットであることを検証します。
        /// </summary>
        [Test]
        public void ProductionBiomeTiles_ReferenceMatchingSprites()
        {
            string[] expectedNames = WorldGenerationSpriteAtlasBuilder.GetExpectedBiomeSpriteNames();

            for (int index = 0; index < expectedNames.Length; index++)
            {
                string expectedName = expectedNames[index];
                string tilePath =
                    $"Assets/Farmware/Art/WorldGeneration/Terrain/Base/{expectedName}.asset";
                Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);

                Assert.That(tile, Is.Not.Null, $"本番用Biome Tileが見つかりません: {tilePath}");
                Assert.That(tile.sprite, Is.Not.Null, $"TileのSprite参照がありません: {tilePath}");
                Assert.That(tile.sprite.name, Is.EqualTo(expectedName));
                Assert.That(
                    AssetDatabase.GetAssetPath(tile.sprite),
                    Is.EqualTo(WorldGenerationSpriteAtlasBuilder.ProductionBiomeTerrainTexturePath));
                Assert.That(tile.colliderType, Is.EqualTo(Tile.ColliderType.None));
            }
        }

        /// <summary>
        /// 岩壁Atlasが土地Atlasから独立し、岩壁TextureだけをPackableとして持つことを検証します。
        /// </summary>
        [Test]
        public void RockWallAtlas_ContainsOnlyRockWallSourceTexture()
        {
            AssertAtlasPackables(
                WorldGenerationSpriteAtlasBuilder.RockWallAtlasPath,
                WorldGenerationSpriteAtlasBuilder.GetRockWallSourceTexturePaths());
        }

        /// <summary>
        /// 境界ハイライトAtlasが224×16の元TextureだけをPackableとして持つことを検証します。
        /// </summary>
        [Test]
        public void BoundaryHighlightAtlas_ContainsOnlyBoundarySourceTexture()
        {
            AssertAtlasPackables(
                WorldGenerationSpriteAtlasBuilder.BoundaryHighlightAtlasPath,
                WorldGenerationSpriteAtlasBuilder.GetBoundaryHighlightSourceTexturePaths());
        }

        /// <summary>
        /// 境界ハイライト画像が16×16の14枚へ左から順に分割され、中心PivotとPixel Art設定を持つことを検証します。
        /// </summary>
        [Test]
        public void BoundaryHighlightSpriteSheet_UsesExpectedSlicesAndPixelArtSettings()
        {
            string texturePath = WorldGenerationSpriteAtlasBuilder.BoundaryHighlightTexturePath;
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            Assert.That(importer, Is.Not.Null, $"TextureImporterを取得できません: {texturePath}");
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Multiple));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(16f));
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
                Assert.That(sprite.rect.x, Is.EqualTo(index * 16f));
                Assert.That(sprite.rect.y, Is.Zero);
                Assert.That(sprite.rect.width, Is.EqualTo(16f));
                Assert.That(sprite.rect.height, Is.EqualTo(16f));
                Assert.That(sprite.pivot.x, Is.EqualTo(8f).Within(0.001f));
                Assert.That(sprite.pivot.y, Is.EqualTo(8f).Within(0.001f));
            }
        }

        /// <summary>
        /// 14区画すべてに白い可視画素があり、半透明のぼかしや別色が混入していないことをPNG原稿から検証します。
        /// </summary>
        [Test]
        public void BoundaryHighlightTexture_EveryShapeUsesOnlyTransparentOrOpaqueWhitePixels()
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
                Assert.That(texture.width, Is.EqualTo(224));
                Assert.That(texture.height, Is.EqualTo(16));

                Color32[] pixels = texture.GetPixels32();
                for (int shapeIndex = 0;
                     shapeIndex < WorldGenerationSpriteAtlasBuilder.ExpectedBoundaryHighlightSpriteCount;
                     shapeIndex++)
                {
                    int visiblePixelCount = 0;
                    for (int y = 0; y < 16; y++)
                    {
                        for (int localX = 0; localX < 16; localX++)
                        {
                            int x = (shapeIndex * 16) + localX;
                            Color32 pixel = pixels[(y * texture.width) + x];
                            if (pixel.a == 0)
                            {
                                continue;
                            }

                            // 半透明や色付き画素は回転時に継ぎ目を作るため、可視画素を完全な不透明白へ限定します。
                            visiblePixelCount++;
                            Assert.That(pixel.a, Is.EqualTo(byte.MaxValue),
                                $"半透明画素があります: shape={shapeIndex + 1}, ({localX}, {y})");
                            Assert.That(pixel.r, Is.EqualTo(byte.MaxValue),
                                $"白以外の画素があります: shape={shapeIndex + 1}, ({localX}, {y})");
                            Assert.That(pixel.g, Is.EqualTo(byte.MaxValue),
                                $"白以外の画素があります: shape={shapeIndex + 1}, ({localX}, {y})");
                            Assert.That(pixel.b, Is.EqualTo(byte.MaxValue),
                                $"白以外の画素があります: shape={shapeIndex + 1}, ({localX}, {y})");
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

        /// <summary>
        /// 土地AtlasがPoint・MipMapなし・無圧縮・回転なし・矩形Packing・Built-in配信であることを検証します。
        /// </summary>
        [Test]
        public void GroundTerrainAtlas_UsesPixelArtSettingsAndBuiltInDelivery()
        {
            AssertPixelArtAtlasSettings(WorldGenerationSpriteAtlasBuilder.GroundTerrainAtlasPath);
        }

        /// <summary>
        /// 岩壁Atlasも土地と同じPixel Art向け設定を使用し、本番岩壁へ差し替える際に設定が分岐しないことを検証します。
        /// </summary>
        [Test]
        public void RockWallAtlas_UsesPixelArtSettingsAndBuiltInDelivery()
        {
            AssertPixelArtAtlasSettings(WorldGenerationSpriteAtlasBuilder.RockWallAtlasPath);
        }

        /// <summary>
        /// 境界ハイライトAtlasもPoint・MipMapなし・無圧縮・回転なし・Built-in配信であることを検証します。
        /// </summary>
        [Test]
        public void BoundaryHighlightAtlas_UsesPixelArtSettingsAndBuiltInDelivery()
        {
            AssertPixelArtAtlasSettings(
                WorldGenerationSpriteAtlasBuilder.BoundaryHighlightAtlasPath);
        }

        /// <summary>
        /// 実際のパック結果で全土地Spriteが同じTextureを参照し、バイオーム境界の描画順がTexture単位に分断されないことを検証します。
        /// </summary>
        [Test]
        public void GroundTerrainAtlas_PacksEveryTerrainSpriteIntoOneTexture()
        {
            AssertAllSpritesPackedIntoOneTexture(
                WorldGenerationSpriteAtlasBuilder.GroundTerrainAtlasPath,
                WorldGenerationSpriteAtlasBuilder.GetGroundTerrainSourceTexturePaths());
        }

        /// <summary>
        /// 14枚の境界ハイライトが専用Atlasの同じTextureページへ収まり、描画中のTexture切替を抑えられることを検証します。
        /// </summary>
        [Test]
        public void BoundaryHighlightAtlas_PacksEveryShapeIntoOneTexture()
        {
            AssertAllSpritesPackedIntoOneTexture(
                WorldGenerationSpriteAtlasBuilder.BoundaryHighlightAtlasPath,
                WorldGenerationSpriteAtlasBuilder.GetBoundaryHighlightSourceTexturePaths());
        }

        /// <summary>
        /// 指定Atlasを実際にパックし、全元Spriteが同じ1枚のパック済みTextureを参照することを確認します。
        /// </summary>
        /// <param name="atlasPath">確認対象の`.spriteatlasv2`パスです。</param>
        /// <param name="sourcePaths">Atlasへ収録した元Textureパスです。</param>
        private static void AssertAllSpritesPackedIntoOneTexture(
            string atlasPath,
            string[] sourcePaths)
        {
            SpriteAtlas atlas = LoadRequiredRuntimeAtlas(atlasPath);
            SpriteAtlasUtility.PackAtlases(
                new[] { atlas },
                EditorUserBuildSettings.activeBuildTarget,
                canCancel: false);

            Texture2D expectedPackedTexture = null;

            for (int textureIndex = 0; textureIndex < sourcePaths.Length; textureIndex++)
            {
                Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(sourcePaths[textureIndex])
                    .OfType<Sprite>()
                    .OrderBy(sprite => sprite.name, StringComparer.Ordinal)
                    .ToArray();
                Assert.That(sprites, Is.Not.Empty, $"Spriteがありません: {sourcePaths[textureIndex]}");

                for (int spriteIndex = 0; spriteIndex < sprites.Length; spriteIndex++)
                {
                    Sprite sprite = sprites[spriteIndex];
                    Texture2D packedTexture = SpriteUtility.GetSpriteTexture(sprite, getAtlasData: true);
                    Assert.That(sprite.packed, Is.True, $"SpriteがAtlasへパックされていません: {sprite.name}");
                    Assert.That(packedTexture, Is.Not.Null, $"パック済みTextureを取得できません: {sprite.name}");

                    if (expectedPackedTexture == null)
                    {
                        expectedPackedTexture = packedTexture;
                    }
                    else
                    {
                        Assert.That(
                            packedTexture,
                            Is.SameAs(expectedPackedTexture),
                            $"Spriteが別Textureページへ分割されています: {atlasPath}, {sprite.name}");
                    }
                }
            }
        }

        /// <summary>
        /// Atlasが期待した元TextureだけをPackableとして保持していることを確認します。
        /// </summary>
        /// <param name="atlasPath">確認対象の`.spriteatlasv2`パスです。</param>
        /// <param name="expectedSourcePaths">期待する元Textureパスです。</param>
        private static void AssertAtlasPackables(string atlasPath, string[] expectedSourcePaths)
        {
            SpriteAtlas atlas = LoadRequiredRuntimeAtlas(atlasPath);
            string[] actualPaths = atlas.GetPackables()
                .Select(AssetDatabase.GetAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            string[] orderedExpectedPaths = expectedSourcePaths
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.That(actualPaths, Is.EqualTo(orderedExpectedPaths));
        }

        /// <summary>
        /// Atlas ImporterのBuilt-in配信、Texture設定、Packing設定、既定Platform設定を確認します。
        /// </summary>
        /// <param name="atlasPath">確認対象の`.spriteatlasv2`パスです。</param>
        private static void AssertPixelArtAtlasSettings(string atlasPath)
        {
            SpriteAtlasImporter importer = AssetImporter.GetAtPath(atlasPath) as SpriteAtlasImporter;
            Assert.That(importer, Is.Not.Null, $"SpriteAtlasImporterを取得できません: {atlasPath}");
            Assert.That(importer.includeInBuild, Is.True, "常時使用する地形AtlasはBuilt-inへ含めてください。");

            SpriteAtlasTextureSettings textureSettings = importer.textureSettings;
            Assert.That(textureSettings.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(textureSettings.generateMipMaps, Is.False);
            Assert.That(textureSettings.readable, Is.False);
            Assert.That(textureSettings.anisoLevel, Is.Zero);

            SpriteAtlasPackingSettings packingSettings = importer.packingSettings;
            Assert.That(packingSettings.padding, Is.EqualTo(WorldGenerationSpriteAtlasBuilder.AtlasPaddingInPixels));
            Assert.That(packingSettings.enableRotation, Is.False);
            Assert.That(packingSettings.enableTightPacking, Is.False);
            Assert.That(packingSettings.enableAlphaDilation, Is.False);

            TextureImporterPlatformSettings defaultPlatformSettings =
                importer.GetPlatformSettings("DefaultTexturePlatform");
            Assert.That(defaultPlatformSettings.maxTextureSize, Is.EqualTo(WorldGenerationSpriteAtlasBuilder.AtlasMaxTextureSize));
            Assert.That(defaultPlatformSettings.format, Is.EqualTo(TextureImporterFormat.RGBA32));
            Assert.That(
                defaultPlatformSettings.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
        }

        /// <summary>
        /// 指定パスからRuntime問い合わせ用SpriteAtlasを読み込み、欠落時には対象パスを含む失敗へ変換します。
        /// </summary>
        /// <param name="atlasPath">読み込む`.spriteatlasv2`パスです。</param>
        /// <returns>Packableとパック結果を問い合わせるRuntime SpriteAtlasです。</returns>
        private static SpriteAtlas LoadRequiredRuntimeAtlas(string atlasPath)
        {
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            Assert.That(atlas, Is.Not.Null, $"Sprite Atlasが見つかりません: {atlasPath}");
            return atlas;
        }
    }
}
