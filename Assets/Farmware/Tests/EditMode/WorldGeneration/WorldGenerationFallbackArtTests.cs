// このファイルは、Editor構築ツールが生成した一体型土地・岩壁PNGのピクセル配置を検証します。
// 地形分類のテストとは分け、上面と断面が指定寸法の一枚画像として欠けずに生成されることを保証します。

using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 7種類の土地が16×24、岩壁が16×28で、上面と断面の全ピクセルが可視であることを保証します。
    /// </summary>
    public sealed class WorldGenerationFallbackArtTests
    {
        // 論理セル1個分に一致させる全ブロック共通の横幅です。
        private const int ExpectedWidth = 16;

        // 上面16pxと土地断面8pxを合わせた土地画像の高さです。
        private const int ExpectedTerrainHeight = 24;

        // 上面16pxと岩壁側面12pxを合わせた岩壁画像の高さです。
        private const int ExpectedRockWallHeight = 28;

        // 構築ツールが生成する7種類の一体型土地PNGです。
        private static readonly string[] TerrainTextureNames =
        {
            "Grassland.png",
            "Forest.png",
            "Desert.png",
            "Rockland.png",
            "Wetland.png",
            "Snowfield.png",
            "Volcanic.png"
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

        /// <summary>
        /// 各土地PNGが16×24で、上面16pxと断面8pxの全領域を一枚の不透明画像として持つことを検証します。
        /// </summary>
        [Test]
        public void TerrainTextures_ContainSurfaceAndEightPixelFace()
        {
            foreach (string textureName in TerrainTextureNames)
            {
                VerifyOpaqueTexture(textureName, ExpectedTerrainHeight);
            }
        }

        /// <summary>
        /// 岩壁PNGが16×28で、上面16pxと側面12pxの全領域を一枚の不透明画像として持つことを検証します。
        /// </summary>
        [Test]
        public void RockWallTexture_ContainsSurfaceAndTwelvePixelFace()
        {
            VerifyOpaqueTexture("RockWall.png", ExpectedRockWallHeight);
        }

        /// <summary>
        /// 旧分離断面PNGが削除され、一体型画像との二重管理へ戻らないことを検証します。
        /// </summary>
        [Test]
        public void ObsoleteSeparateFaceTextures_DoNotRemain()
        {
            foreach (string textureName in ObsoleteTextureNames)
            {
                string texturePath = GetFallbackTexturePath(textureName);
                Assert.That(File.Exists(texturePath), Is.False, $"旧断面PNGが残っています: {texturePath}");
            }
        }

        /// <summary>
        /// 1枚のPNGについて寸法と全ピクセルの可視性を確認します。
        /// </summary>
        /// <param name="textureName">Fallbackフォルダー内にある拡張子付きPNG名です。</param>
        /// <param name="expectedHeight">期待する画像高です。単位はピクセルです。</param>
        /// <remarks>ファイルを読み込み、一時Texture2Dを生成・破棄します。プロジェクトアセット自体は変更しません。</remarks>
        private static void VerifyOpaqueTexture(string textureName, int expectedHeight)
        {
            string texturePath = GetFallbackTexturePath(textureName);
            Assert.That(File.Exists(texturePath), Is.True, $"一体型PNGが見つかりません: {texturePath}");

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            try
            {
                byte[] pngBytes = File.ReadAllBytes(texturePath);
                Assert.That(
                    ImageConversion.LoadImage(texture, pngBytes, markNonReadable: false),
                    Is.True,
                    $"一体型PNGを読み込めません: {textureName}");
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

        /// <summary>
        /// Fallbackフォルダー内のPNG絶対パスを組み立てます。
        /// </summary>
        /// <param name="textureName">拡張子付きPNG名です。</param>
        /// <returns>現在のUnityプロジェクト内にある対象PNGの絶対パスです。</returns>
        private static string GetFallbackTexturePath(string textureName)
        {
            return Path.Combine(
                Application.dataPath,
                "Farmware/Art/WorldGeneration/Fallback",
                textureName);
        }
    }
}
