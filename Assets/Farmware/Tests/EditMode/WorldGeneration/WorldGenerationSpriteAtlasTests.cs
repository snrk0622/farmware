// このファイルは、新18Biome仮土地・本番用岩壁のSprite Atlas V2設定と、各Atlasが1ページへ収まることを検証します。
// Tilemapの見た目を画像比較する代わりに、誤描画の原因だったTexture分割をEditModeで直接保証します。

using System;
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
        /// 土地Atlasが新18Biomeの仮TextureだけをPackableとして持ち、旧7土地、液体、資源を誤って収録していないことを検証します。
        /// </summary>
        [Test]
        public void GroundTerrainAtlas_ContainsAllTerrainSourceTextures()
        {
            AssertAtlasPackables(
                WorldGenerationSpriteAtlasBuilder.GroundTerrainAtlasPath,
                WorldGenerationSpriteAtlasBuilder.GetGroundTerrainSourceTexturePaths());
        }

        /// <summary>
        /// 新18Biomeの仮土地がID順に各1枚のSpriteとして揃い、16×24土地向けのPixel Art設定を持つことを検証します。
        /// </summary>
        [Test]
        public void FallbackBiomeSprites_UseExpectedNamesAndPixelArtSettings()
        {
            string[] texturePaths = WorldGenerationSpriteAtlasBuilder.GetGroundTerrainSourceTexturePaths();
            string[] expectedNames = WorldGenerationSpriteAtlasBuilder.GetExpectedBiomeSpriteNames();
            Assert.That(texturePaths, Has.Length.EqualTo(BiomeStableIds.Count));
            Assert.That(expectedNames, Has.Length.EqualTo(BiomeStableIds.Count));

            for (int index = 0; index < texturePaths.Length; index++)
            {
                string texturePath = texturePaths[index];
                TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                Assert.That(importer, Is.Not.Null, $"TextureImporterを取得できません: {texturePath}");
                Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
                Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
                Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(16f));
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(importer.mipmapEnabled, Is.False);
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
                Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));

                // Unity 6.3ではMesh TypeとFallback Physics Shapeを設定構造体から読み戻します。
                TextureImporterSettings importerSettings = new TextureImporterSettings();
                importer.ReadTextureSettings(importerSettings);
                Assert.That(importerSettings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect));
                Assert.That(importerSettings.spriteGenerateFallbackPhysicsShape, Is.False);

                Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(texturePath).OfType<Sprite>().Single();

                Assert.That(sprite.name, Is.EqualTo(expectedNames[index]));
                Assert.That(sprite.rect.x, Is.Zero);
                Assert.That(sprite.rect.y, Is.Zero);
                Assert.That(sprite.rect.width, Is.EqualTo(16f));
                Assert.That(sprite.rect.height, Is.EqualTo(24f));
                Assert.That(sprite.pivot.x, Is.EqualTo(8f).Within(0.001f));
                Assert.That(sprite.pivot.y, Is.EqualTo(16f).Within(0.001f));
            }
        }

        /// <summary>
        /// 18個の仮土地Tileが同名Spriteを参照し、Colliderを持たない表示専用アセットであることを検証します。
        /// </summary>
        [Test]
        public void FallbackBiomeTiles_ReferenceMatchingSprites()
        {
            string[] expectedNames = WorldGenerationSpriteAtlasBuilder.GetExpectedBiomeSpriteNames();
            string[] texturePaths = WorldGenerationSpriteAtlasBuilder.GetGroundTerrainSourceTexturePaths();

            for (int index = 0; index < expectedNames.Length; index++)
            {
                string expectedName = expectedNames[index];
                string tilePath =
                    $"Assets/Farmware/Art/WorldGeneration/Fallback/{expectedName}.asset";
                Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);

                Assert.That(tile, Is.Not.Null, $"新Biome仮Tileが見つかりません: {tilePath}");
                Assert.That(tile.sprite, Is.Not.Null, $"TileのSprite参照がありません: {tilePath}");
                Assert.That(tile.sprite.name, Is.EqualTo(expectedName));
                Assert.That(
                    AssetDatabase.GetAssetPath(tile.sprite),
                    Is.EqualTo(texturePaths[index]));
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
        /// 本番用岩壁が16×28の単一Spriteとして、PPU 16、Point、MipMapなし、無圧縮、Full Rect、Pivot Y 8/28で取り込まれることを検証します。
        /// </summary>
        [Test]
        public void ProductionRockWallSprite_UsesExpectedGeometryAndPixelArtSettings()
        {
            string texturePath = WorldGenerationSpriteAtlasBuilder.ProductionRockWallTexturePath;
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            Assert.That(importer, Is.Not.Null, $"TextureImporterを取得できません: {texturePath}");
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(16f));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));

            // Tilemapの矩形全体を常に描画し、透明輪郭から自動生成されたMeshやColliderへ依存しないことを保証します。
            TextureImporterSettings importerSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(importerSettings);
            Assert.That(importerSettings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect));
            Assert.That(importerSettings.spriteGenerateFallbackPhysicsShape, Is.False);

            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(texturePath)
                .OfType<Sprite>()
                .ToArray();
            Assert.That(sprites, Has.Length.EqualTo(1));
            Sprite sprite = sprites[0];
            Assert.That(sprite.name, Is.EqualTo("terrain_rock_wall_01"));
            Assert.That(sprite.rect.x, Is.Zero);
            Assert.That(sprite.rect.y, Is.Zero);
            Assert.That(sprite.rect.width, Is.EqualTo(16f));
            Assert.That(sprite.rect.height, Is.EqualTo(28f));
            Assert.That(sprite.pivot.x, Is.EqualTo(8f).Within(0.001f));
            Assert.That(sprite.pivot.y, Is.EqualTo(8f).Within(0.001f));
        }

        /// <summary>
        /// 専用TileがFallbackではなく本番岩壁Spriteを参照し、物理衝突をPresentationへ混入させないことを検証します。
        /// </summary>
        [Test]
        public void ProductionRockWallTile_ReferencesProductionSprite()
        {
            const string tilePath =
                "Assets/Farmware/Art/WorldGeneration/Terrain/Base/terrain_rock_wall_01.asset";
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);

            Assert.That(tile, Is.Not.Null, $"本番用岩壁Tileが見つかりません: {tilePath}");
            Assert.That(tile.sprite, Is.Not.Null, $"TileのSprite参照がありません: {tilePath}");
            Assert.That(tile.sprite.name, Is.EqualTo("terrain_rock_wall_01"));
            Assert.That(
                AssetDatabase.GetAssetPath(tile.sprite),
                Is.EqualTo(WorldGenerationSpriteAtlasBuilder.ProductionRockWallTexturePath));
            Assert.That(tile.colliderType, Is.EqualTo(Tile.ColliderType.None));
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
        /// 本番用岩壁Spriteが専用Atlasへ実際にパックされ、Fallback画像を必要としないことを検証します。
        /// </summary>
        [Test]
        public void RockWallAtlas_PacksProductionSpriteIntoOneTexture()
        {
            AssertAllSpritesPackedIntoOneTexture(
                WorldGenerationSpriteAtlasBuilder.RockWallAtlasPath,
                WorldGenerationSpriteAtlasBuilder.GetRockWallSourceTexturePaths());
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
