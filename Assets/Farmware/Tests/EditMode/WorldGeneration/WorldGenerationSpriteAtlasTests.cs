// このファイルは、本番用バイオーム土地・フォールバック岩壁・境界ハイライトのSprite Atlas V2設定と、各Atlasが1ページへ収まることを検証します。
// Tilemapの見た目を画像比較する代わりに、誤描画の原因だったTexture分割をEditModeで直接保証します。

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
        /// 14形状の実画素が、選択処理で宣言した外周辺・内角の向きと完全に一致することを確認します。
        /// PNGの上下反転や形状順の入れ替わりを、論理マスクだけのテストでは見逃さないための検証です。
        /// </summary>
        [Test]
        public void BoundaryHighlightTexture_PixelsMatchCanonicalFeatureMasks()
        {
            const int spriteSize = 16;
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
                    Is.EqualTo(
                        WorldGenerationSpriteAtlasBuilder.ExpectedBoundaryHighlightSpriteCount
                        * spriteSize));
                Assert.That(texture.height, Is.EqualTo(spriteSize));

                Color32[] pixels = texture.GetPixels32();
                for (int shapeIndex = 0;
                     shapeIndex < WorldGenerationSpriteAtlasBuilder.ExpectedBoundaryHighlightSpriteCount;
                     shapeIndex++)
                {
                    BoundaryHighlightShape shape = (BoundaryHighlightShape)(shapeIndex + 1);
                    byte featureMask = TerrainBoundaryHighlightUtility.GetCanonicalFeatureMask(shape);

                    // Texture2Dの画素座標は左下が原点です。北をy=15として照合し、PNG上下の取り違えを検出します。
                    for (int y = 0; y < spriteSize; y++)
                    {
                        for (int localX = 0; localX < spriteSize; localX++)
                        {
                            int x = (shapeIndex * spriteSize) + localX;
                            bool isVisible = pixels[(y * texture.width) + x].a != 0;
                            bool shouldBeVisible = ShouldBoundaryHighlightPixelBeVisible(
                                featureMask,
                                localX,
                                y);

                            Assert.That(
                                isVisible,
                                Is.EqualTo(shouldBeVisible),
                                $"境界特徴と画素配置が一致しません: shape={shapeIndex + 1}, "
                                + $"mask=0x{featureMask:X2}, ({localX}, {y})");
                        }
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// 境界特徴マスクから、16×16 Sprite内の指定画素を白く描くべきか判定します。
        /// 外周は対応する辺全体、内角は対応する角の1画素として、制作仕様を直接表現します。
        /// </summary>
        /// <param name="featureMask">外周4方向と内角4方向を保持する8ビットの特徴マスクです。</param>
        /// <param name="x">Sprite内の左端を0とする水平画素座標です。</param>
        /// <param name="y">Sprite内の下端を0とする垂直画素座標です。</param>
        /// <returns>指定画素が境界線または内角として可視であるべきならtrueです。</returns>
        private static bool ShouldBoundaryHighlightPixelBeVisible(byte featureMask, int x, int y)
        {
            const int lastPixelCoordinate = 15;
            // TerrainBoundaryHighlightUtilityと同じく、下位4ビットを北から時計回りの外周へ割り当てます。
            const byte northEdge = 1 << 0;
            const byte eastEdge = 1 << 1;
            const byte southEdge = 1 << 2;
            const byte westEdge = 1 << 3;
            const byte northEastInnerCorner = 1 << 4;
            const byte southEastInnerCorner = 1 << 5;
            const byte southWestInnerCorner = 1 << 6;
            const byte northWestInnerCorner = 1 << 7;

            bool isOuterEdge = ((featureMask & northEdge) != 0 && y == lastPixelCoordinate)
                || ((featureMask & eastEdge) != 0 && x == lastPixelCoordinate)
                || ((featureMask & southEdge) != 0 && y == 0)
                || ((featureMask & westEdge) != 0 && x == 0);
            bool isInnerCorner = ((featureMask & northEastInnerCorner) != 0
                    && x == lastPixelCoordinate
                    && y == lastPixelCoordinate)
                || ((featureMask & southEastInnerCorner) != 0
                    && x == lastPixelCoordinate
                    && y == 0)
                || ((featureMask & southWestInnerCorner) != 0 && x == 0 && y == 0)
                || ((featureMask & northWestInnerCorner) != 0
                    && x == 0
                    && y == lastPixelCoordinate);
            return isOuterEdge || isInnerCorner;
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
