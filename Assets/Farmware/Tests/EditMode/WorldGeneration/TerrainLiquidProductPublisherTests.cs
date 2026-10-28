// このファイルは、製品液体カタログ、透明な水Surface、不透明なマグマSurface、静止水際色と共有Materialを検証します。
// SampleSceneへ接続する前提AssetがSourceやFallbackへ依存せず、淡水・海水の通常Faceを隠さないことを保証します。

using System.IO;
using Farmware.WorldGeneration.Editor;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 液体公開処理が保存した製品Assetを読取専用で検査し、3液体の参照・色の欠落を検出します。
    /// </summary>
    public sealed class TerrainLiquidProductPublisherTests
    {
        /// <summary>
        /// 製品カタログが3液体を完全登録し、制作入力またはFallbackを再帰依存へ含めないことを確認します。
        /// </summary>
        [Test]
        public void PublishedCatalog_ContainsThreeCompleteLiquidsWithoutSourceDependencies()
        {
            TerrainLiquidPresentationCatalog catalog =
                AssetDatabase.LoadAssetAtPath<TerrainLiquidPresentationCatalog>(
                    TerrainLiquidProductPublisher.CatalogPath);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.TryInitialize(out string error), Is.True, error);
            Assert.That(catalog.DefinitionCount, Is.EqualTo(3));

            TerrainLiquidVisualKind[] kinds =
            {
                TerrainLiquidVisualKind.FreshWater,
                TerrainLiquidVisualKind.SaltWater,
                TerrainLiquidVisualKind.Magma
            };
            for (int kindIndex = 0; kindIndex < kinds.Length; kindIndex++)
            {
                Assert.That(
                    catalog.TryGetDefinition(
                        kinds[kindIndex],
                        out TerrainLiquidPresentationDefinition definition),
                    Is.True,
                    kinds[kindIndex].ToString());
                Assert.That(definition.SurfaceTile, Is.Not.Null);
                Assert.That(definition.ShorelineColor.a, Is.EqualTo(1f));
            }

            string[] dependencies = AssetDatabase.GetDependencies(
                TerrainLiquidProductPublisher.CatalogPath,
                recursive: true);
            for (int dependencyIndex = 0;
                 dependencyIndex < dependencies.Length;
                 dependencyIndex++)
            {
                string dependency = dependencies[dependencyIndex].Replace('\\', '/');
                Assert.That(
                    dependency,
                    Does.Not.StartWith(
                        "Assets/Farmware/Art/WorldGeneration/Liquids/Source/"),
                    "製品Sceneが制作入力PNGを間接参照してはいけません。");
                Assert.That(
                    dependency,
                    Does.Not.StartWith(
                        "Assets/Farmware/Art/WorldGeneration/Fallback/"),
                    "製品液体カタログが旧Fallbackを参照してはいけません。");
            }
        }

        /// <summary>
        /// 淡水・海水の深化Fadeが明るいシアンへ近づく旧配色へ戻らず、96px地点で暗い深水色へ収束することを保証します。
        /// </summary>
        [Test]
        public void PublishedCatalog_DepthColorsConvergeToDarkWater()
        {
            TerrainLiquidPresentationCatalog catalog =
                AssetDatabase.LoadAssetAtPath<TerrainLiquidPresentationCatalog>(
                    TerrainLiquidProductPublisher.CatalogPath);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.TryInitialize(out string error), Is.True, error);
            Assert.That(
                catalog.TryGetDefinition(
                    TerrainLiquidVisualKind.FreshWater,
                    out TerrainLiquidPresentationDefinition freshWater),
                Is.True);
            Assert.That(
                catalog.TryGetDefinition(
                    TerrainLiquidVisualKind.SaltWater,
                    out TerrainLiquidPresentationDefinition saltWater),
                Is.True);

            // ShaderのAlphaは深いほど増えるため、収束色そのものを暗く固定して見た目の深化方向を保証します。
            Assert.That(
                (Color32)freshWater.DepthOverlayColor,
                Is.EqualTo(new Color32(24, 72, 96, 255)));
            Assert.That(
                (Color32)saltWater.DepthOverlayColor,
                Is.EqualTo(new Color32(16, 42, 72, 255)));
        }

        /// <summary>
        /// 水Surfaceは全画素透明で通常Faceを隠さず、マグマSurfaceだけが全画素不透明で背後を遮蔽することを確認します。
        /// </summary>
        [Test]
        public void PublishedSurfacePixels_KeepWaterTransparentAndMagmaOpaque()
        {
            AssertUniformAlpha(
                TerrainLiquidProductPublisher.FreshWaterSurfaceTexturePath,
                expectedAlpha: 0);
            AssertUniformAlpha(
                TerrainLiquidProductPublisher.SaltWaterSurfaceTexturePath,
                expectedAlpha: 0);
            AssertUniformAlpha(
                TerrainLiquidProductPublisher.MagmaSurfaceTexturePath,
                expectedAlpha: 255);
        }

        /// <summary>製品水際Materialが専用Shaderを使い、旧4フレームAnimatedTileへ依存しないことを確認します。</summary>
        [Test]
        public void PublishedShoreline_UsesSharedShaderWithoutAnimatedTiles()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                TerrainLiquidProductPublisher.ShorelineMaterialPath);
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo("Farmware/Terrain/Liquid Shoreline"));
            Assert.That(ShaderUtil.ShaderHasError(material.shader), Is.False);
            // 最低濃度0による透明部分も正しい調整値です。常時着色する旧契約へ戻さないようにします。
            Assert.That(material.GetFloat("_BaseOpacity"), Is.InRange(0f, 1f));
            Assert.That(material.GetFloat("_HighlightOpacity"), Is.GreaterThan(material.GetFloat("_BaseOpacity")));
            // 一方向の移動設定を残さず、階調数・往復周期・コマ数を調整できる製品契約を検査します。
            Assert.That(material.HasProperty("_FlowSpeedPixelsPerSecond"), Is.False);
            Assert.That(material.GetFloat("_BrightnessLevels"), Is.InRange(2f, 8f));
            Assert.That(material.GetFloat("_OscillationPeriodSeconds"), Is.InRange(1f, 12f));
            Assert.That(material.GetFloat("_PeriodVariation"), Is.InRange(0f, 0.5f));
            Assert.That(material.GetFloat("_AnimationStepsPerSecond"), Is.InRange(1f, 12f));
            Assert.That(AssetDatabase.FindAssets("t:AnimatedTile",
                new[] { "Assets/Farmware/Art/WorldGeneration/Liquids" }), Is.Empty);
        }

        /// <summary>PNGを一時Textureへ読み、全画素のAlphaが期待値と一致することを確認します。</summary>
        /// <param name="assetPath">Assetsから始まる製品Surface PNGパスです。</param>
        /// <param name="expectedAlpha">水なら0、マグマなら255です。</param>
        private static void AssertUniformAlpha(string assetPath, byte expectedAlpha)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.That(projectRoot, Is.Not.Null);
            string fullPath = Path.Combine(projectRoot, assetPath);
            Assert.That(File.Exists(fullPath), Is.True, assetPath);

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(
                    ImageConversion.LoadImage(
                        texture,
                        File.ReadAllBytes(fullPath),
                        markNonReadable: false),
                    Is.True,
                    assetPath);
                Assert.That(
                    texture.width,
                    Is.EqualTo(TerrainLiquidPresentationUtility.PixelsPerCell));
                Assert.That(
                    texture.height,
                    Is.EqualTo(TerrainLiquidPresentationUtility.PixelsPerCell));
                Color32[] pixels = texture.GetPixels32();
                for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
                {
                    Assert.That(
                        pixels[pixelIndex].a,
                        Is.EqualTo(expectedAlpha),
                        $"{assetPath} pixel={pixelIndex}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
