// このファイルは、一体型土地・岩壁のSprite Atlas V2設定と、土地が同じパック済みTextureへ収まることを検証します。
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
        /// 土地Atlasが7種類の元TextureだけをPackableとして持ち、液体や資源を誤って収録していないことを検証します。
        /// </summary>
        [Test]
        public void GroundTerrainAtlas_ContainsAllTerrainSourceTextures()
        {
            AssertAtlasPackables(
                WorldGenerationSpriteAtlasBuilder.GroundTerrainAtlasPath,
                WorldGenerationSpriteAtlasBuilder.GetGroundTerrainSourceTexturePaths());
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
        /// 土地AtlasがPoint・MipMapなし・無圧縮・回転なし・矩形Packing・Built-in配信であることを検証します。
        /// </summary>
        [Test]
        public void GroundTerrainAtlas_UsesPixelArtSettingsAndBuiltInDelivery()
        {
            AssertPixelArtAtlasSettings(WorldGenerationSpriteAtlasBuilder.GroundTerrainAtlasPath);
        }

        /// <summary>
        /// 岩壁Atlasも土地と同じPixel Art向け設定を使用し、将来の差分追加時に設定が分岐しないことを検証します。
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
            SpriteAtlas groundAtlas = LoadRequiredRuntimeAtlas(
                WorldGenerationSpriteAtlasBuilder.GroundTerrainAtlasPath);
            SpriteAtlasUtility.PackAtlases(
                new[] { groundAtlas },
                EditorUserBuildSettings.activeBuildTarget,
                canCancel: false);

            Texture2D expectedPackedTexture = null;
            string[] sourcePaths =
                WorldGenerationSpriteAtlasBuilder.GetGroundTerrainSourceTexturePaths();

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
                            $"土地Spriteが別Textureページへ分割されています: {sprite.name}");
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
