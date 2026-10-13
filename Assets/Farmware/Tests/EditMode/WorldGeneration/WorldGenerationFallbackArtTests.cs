// このファイルは、Editor構築ツールが生成したフォールバック断面PNGのピクセル配置を検証します。
// 地形分類のテストとは分け、Base Tileの上面を断面Spriteが1pxも塗り替えないことを保証します。

using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 土、砂、岩、雪のフォールバック断面が、16×16の論理上面より下だけへ描かれることを保証します。
    /// </summary>
    public sealed class WorldGenerationFallbackArtTests
    {
        // フォールバック断面PNGは16px幅で生成し、横方向の1セルへ正確に一致させます。
        private const int ExpectedWidth = 16;

        // 上側16pxの論理上面と下側8pxの断面を合わせた画像全体の高さです。
        private const int ExpectedHeight = 24;

        // Y=8から上が16×16の論理上面領域であり、すべて透明でなければなりません。
        private const int LogicalSurfaceBottomY = 8;

        // 論理上面へ重ならず、その直下を8pxすべて埋める断面の可視高です。
        private const int FrontEdgeVisibleHeight = 8;

        // 構築ツールが生成する4種類の断面PNGをすべて同じ配置規則で検証します。
        private static readonly string[] FrontEdgeTextureNames =
        {
            "SoilFrontEdge.png",
            "SandFrontEdge.png",
            "RockFrontEdge.png",
            "SnowFrontEdge.png"
        };

        /// <summary>
        /// 各断面PNGの上側16pxが完全透明で、その直下8pxがすべて可視であることを検証します。
        /// テスト中はPNGを一時的なTexture2Dへ読み込み、検証後に必ず破棄します。
        /// </summary>
        [Test]
        public void FrontEdgeTextures_ExposeEightPixelsWithoutOverlappingTerrainSurface()
        {
            foreach (string textureName in FrontEdgeTextureNames)
            {
                VerifyFrontEdgeTexture(textureName);
            }
        }

        /// <summary>
        /// 1枚のPNGについて寸法、上面領域の透明性、断面開始行を確認します。
        /// </summary>
        /// <param name="textureName">Fallbackフォルダー内にある拡張子付きPNG名です。</param>
        /// <remarks>ファイルを読み込み、一時Texture2Dを生成・破棄します。プロジェクトアセット自体は変更しません。</remarks>
        private static void VerifyFrontEdgeTexture(string textureName)
        {
            string texturePath = Path.Combine(
                Application.dataPath,
                "Farmware/Art/WorldGeneration/Fallback",
                textureName);
            Assert.That(File.Exists(texturePath), Is.True, $"断面PNGが見つかりません: {texturePath}");

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            try
            {
                byte[] pngBytes = File.ReadAllBytes(texturePath);
                Assert.That(
                    ImageConversion.LoadImage(texture, pngBytes, markNonReadable: false),
                    Is.True,
                    $"断面PNGを読み込めません: {textureName}");
                Assert.That(texture.width, Is.EqualTo(ExpectedWidth), $"横幅が不正です: {textureName}");
                Assert.That(texture.height, Is.EqualTo(ExpectedHeight), $"高さが不正です: {textureName}");

                // Y=8以上が16×16の論理上面領域です。全ピクセル透明ならBase Tileへ重なりません。
                for (int y = LogicalSurfaceBottomY; y < ExpectedHeight; y++)
                {
                    for (int x = 0; x < ExpectedWidth; x++)
                    {
                        Assert.That(
                            texture.GetPixel(x, y).a,
                            Is.EqualTo(0f),
                            $"論理上面へ断面が重なっています: {textureName} ({x}, {y})");
                    }
                }

                // Y=0〜7の全ピクセルを検査し、断面が正確に8px露出して途中で欠けないことを保証します。
                for (int y = 0; y < FrontEdgeVisibleHeight; y++)
                {
                    for (int x = 0; x < ExpectedWidth; x++)
                    {
                        Assert.That(
                            texture.GetPixel(x, y).a,
                            Is.GreaterThan(0f),
                            $"8px断面の途中が欠けています: {textureName} ({x}, {y})");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
