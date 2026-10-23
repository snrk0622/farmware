// このファイルは、Editor構築ツールが生成した新18Biome色だけの一体型土地・岩壁PNGと、廃止アセットの削除を検証します。
// 地形分類とは分け、18種類の土地上面、断面、岩壁側面が指定寸法の一枚画像として欠けずに生成されることを保証します。

using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 土地が単色のバイオーム上面を持つ16×24、岩壁が16×28であり、旧Soil派生が残らないことを保証します。
    /// </summary>
    public sealed class WorldGenerationFallbackArtTests
    {
        // 論理セル1個分に一致させる全ブロック共通の横幅です。
        private const int ExpectedWidth = 16;

        // 上面16pxと土地断面8pxを合わせた土地画像の高さです。
        private const int ExpectedTerrainHeight = 24;

        // 上面16pxと岩壁側面12pxを合わせた岩壁画像の高さです。
        private const int ExpectedRockWallHeight = 28;

        // 構築ツールがBiomeId 1〜18と同じ順で生成する一体型土地PNGです。
        private static readonly string[] TerrainTextureNames =
        {
            "TemperateGrassland.png",
            "TropicalSavannaWoodland.png",
            "TemperateDeciduousForest.png",
            "WarmTemperateEvergreenForest.png",
            "TemperateMontaneBambooForest.png",
            "TropicalRainforest.png",
            "TropicalSeasonalForest.png",
            "SubtropicalOceanicForest.png",
            "BorealConiferForest.png",
            "Steppe.png",
            "DryShrublandWoodland.png",
            "CactusDesertDryPlateau.png",
            "HotDesert.png",
            "FreshwaterMarshFloodplain.png",
            "SwampForest.png",
            "TundraPolarBarrens.png",
            "AlpineRockland.png",
            "VolcanicGeothermal.png"
        };

        // C5-Cで新18IDへ意味が変わるため、旧7Biomeの基準PNGとTileも暗黙流用せず削除します。
        private static readonly string[] ObsoleteLegacyBiomeNames =
        {
            "Grassland", "Forest", "Desert", "Rockland", "Wetland", "Snowfield", "Volcanic"
        };

        // 旧仕様で独立していた断面PNGは、一体化後に残してはいけません。
        private static readonly string[] ObsoleteTextureNames =
        {
            "SoilFrontEdge.png",
            "SandFrontEdge.png",
            "RockFrontEdge.png",
            "SnowFrontEdge.png",
            "RockWallFrontFace.png"
        };

        // プレイヤー表示からSoilTypeを除いたため削除する、旧肥沃土・砂質土派生アセットの拡張子なし名称です。
        private static readonly string[] ObsoleteSoilVariantNames =
        {
            "GrasslandFertile", "GrasslandSandy",
            "ForestFertile", "ForestSandy",
            "DesertFertile", "DesertSandy",
            "RocklandFertile", "RocklandSandy",
            "WetlandFertile", "WetlandSandy",
            "SnowfieldFertile", "SnowfieldSandy",
            "VolcanicFertile", "VolcanicSandy"
        };

        /// <summary>各土地PNGが16×24で、全領域を一枚の不透明画像として持つことを検証します。</summary>
        [Test]
        public void TerrainTextures_ContainSurfaceAndEightPixelFace()
        {
            foreach (string textureName in TerrainTextureNames)
            {
                VerifyOpaqueTexture(textureName, ExpectedTerrainHeight);
            }
        }

        /// <summary>18枚の土地上面に草・葉・砂紋やSoil模様がなく、各Biome色だけで塗られることを検証します。</summary>
        [Test]
        public void TerrainTextures_SurfaceUsesSingleBiomeColor()
        {
            foreach (string textureName in TerrainTextureNames)
            {
                Texture2D texture = LoadTexture(textureName);
                try
                {
                    Color32[] pixels = texture.GetPixels32();
                    int surfaceStartY = ExpectedTerrainHeight - ExpectedWidth;
                    Color32 expectedColor = pixels[surfaceStartY * ExpectedWidth];

                    // 下側8pxの断面は対象外とし、論理セルに対応する上面16pxだけを比較します。
                    for (int y = surfaceStartY; y < ExpectedTerrainHeight; y++)
                    {
                        for (int x = 0; x < ExpectedWidth; x++)
                        {
                            Assert.That(
                                pixels[(y * ExpectedWidth) + x],
                                Is.EqualTo(expectedColor),
                                $"Biome上面へ固有模様またはSoil模様が残っています: {textureName} ({x}, {y})");
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(texture);
                }
            }
        }

        /// <summary>岩壁PNGが16×28で、全領域を一枚の不透明画像として持つことを検証します。</summary>
        [Test]
        public void RockWallTexture_ContainsSurfaceAndTwelvePixelFace()
        {
            VerifyOpaqueTexture("RockWall.png", ExpectedRockWallHeight);
        }

        /// <summary>旧分離断面PNGが削除され、一体型画像との二重管理へ戻らないことを検証します。</summary>
        [Test]
        public void ObsoleteSeparateFaceTextures_DoNotRemain()
        {
            foreach (string textureName in ObsoleteTextureNames)
            {
                string texturePath = GetFallbackAssetPath(textureName);
                Assert.That(File.Exists(texturePath), Is.False, $"旧断面PNGが残っています: {texturePath}");
            }
        }

        /// <summary>廃止した14種類のSoil派生PNGとTileが削除され、プレイヤー表示へ再混入しないことを検証します。</summary>
        [Test]
        public void ObsoleteSoilVariantAssets_DoNotRemain()
        {
            foreach (string assetName in ObsoleteSoilVariantNames)
            {
                string texturePath = GetFallbackAssetPath(assetName + ".png");
                string tilePath = GetFallbackAssetPath(assetName + ".asset");
                Assert.That(File.Exists(texturePath), Is.False, $"旧Soil派生PNGが残っています: {texturePath}");
                Assert.That(File.Exists(tilePath), Is.False, $"旧Soil派生Tileが残っています: {tilePath}");
            }
        }

        /// <summary>旧7Biomeの基準PNGとTileが残らず、新IDへ同名・同色で暗黙流用されないことを検証します。</summary>
        [Test]
        public void ObsoleteLegacyBiomeAssets_DoNotRemain()
        {
            foreach (string assetName in ObsoleteLegacyBiomeNames)
            {
                string texturePath = GetFallbackAssetPath(assetName + ".png");
                string tilePath = GetFallbackAssetPath(assetName + ".asset");
                Assert.That(File.Exists(texturePath), Is.False, $"旧Biome PNGが残っています: {texturePath}");
                Assert.That(File.Exists(tilePath), Is.False, $"旧Biome Tileが残っています: {tilePath}");
            }
        }

        /// <summary>1枚のPNGについて寸法と全ピクセルの可視性を確認します。</summary>
        /// <param name="textureName">Fallbackフォルダー内にある拡張子付きPNG名です。</param>
        /// <param name="expectedHeight">期待する画像高です。単位はピクセルです。</param>
        private static void VerifyOpaqueTexture(string textureName, int expectedHeight)
        {
            Texture2D texture = LoadTexture(textureName);
            try
            {
                Assert.That(texture.width, Is.EqualTo(ExpectedWidth), $"横幅が不正です: {textureName}");
                Assert.That(texture.height, Is.EqualTo(expectedHeight), $"高さが不正です: {textureName}");

                for (int y = 0; y < expectedHeight; y++)
                {
                    for (int x = 0; x < ExpectedWidth; x++)
                    {
                        Assert.That(
                            texture.GetPixel(x, y).a,
                            Is.GreaterThan(0f),
                            $"一体型ブロックの途中が透明です: {textureName} ({x}, {y})");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>Fallback内のPNGを読取可能な一時Texture2Dへ読み込みます。</summary>
        /// <param name="textureName">拡張子付きPNG名です。</param>
        /// <returns>呼び出し元がDestroyImmediateで破棄する一時Texture2Dです。</returns>
        private static Texture2D LoadTexture(string textureName)
        {
            string texturePath = GetFallbackAssetPath(textureName);
            Assert.That(File.Exists(texturePath), Is.True, $"一体型PNGが見つかりません: {texturePath}");

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            byte[] pngBytes = File.ReadAllBytes(texturePath);
            Assert.That(
                ImageConversion.LoadImage(texture, pngBytes, markNonReadable: false),
                Is.True,
                $"一体型PNGを読み込めません: {textureName}");
            return texture;
        }

        /// <summary>Fallbackフォルダー内の対象ファイル絶対パスを組み立てます。</summary>
        /// <param name="fileName">拡張子付きファイル名です。</param>
        /// <returns>現在のUnityプロジェクト内にある対象ファイルの絶対パスです。</returns>
        private static string GetFallbackAssetPath(string fileName)
        {
            return Path.Combine(
                Application.dataPath,
                "Farmware/Art/WorldGeneration/Fallback",
                fileName);
        }
    }
}
