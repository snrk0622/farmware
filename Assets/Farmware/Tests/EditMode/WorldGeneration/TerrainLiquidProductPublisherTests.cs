// このファイルは、製品液体カタログ、透明な水Surface、不透明なマグマSurface、生成済み水際Assetを検証します。
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
    /// 液体公開処理が保存した製品Assetを読取専用で検査し、3液体と各7形状の欠落を検出します。
    /// </summary>
    public sealed class TerrainLiquidProductPublisherTests
    {
        /// <summary>液体種ごとの生成済み水際Asset接頭辞です。</summary>
        private static readonly string[] EdgeBaseNames =
        {
            "FreshWaterEdgeWave",
            "SaltWaterEdgeWave",
            "MagmaEdgeGlow"
        };

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
                for (int maskValue = 1;
                     maskValue <= TerrainLiquidEdgeWaveGenerator.MaskCount;
                     maskValue++)
                {
                    Assert.That(
                        definition.GetEdgeWaveTile((TerrainLiquidEdgeMask)maskValue),
                        Is.Not.Null,
                        $"{kinds[kindIndex]} Mask={maskValue}");
                }
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
                        TerrainLiquidEdgeWaveGenerator.SourceFolder + "/"),
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

        /// <summary>
        /// 3つの方向別入力が64×48 pxで、各液体の7 AnimatedTileが4フレームを保持することを確認します。
        /// </summary>
        [Test]
        public void PublishedEdgeAssets_HaveThreeInputsAndSevenFourFrameTilesEach()
        {
            string[] sourcePaths =
            {
                TerrainLiquidProductPublisher.FreshWaterEdgeSourcePath,
                TerrainLiquidProductPublisher.SaltWaterEdgeSourcePath,
                TerrainLiquidProductPublisher.MagmaEdgeSourcePath
            };
            for (int sourceIndex = 0; sourceIndex < sourcePaths.Length; sourceIndex++)
            {
                Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    sourcePaths[sourceIndex]);
                Assert.That(source, Is.Not.Null, sourcePaths[sourceIndex]);
                Assert.That(
                    source.width,
                    Is.EqualTo(TerrainLiquidEdgeWaveGenerator.SourceWidthInPixels));
                Assert.That(
                    source.height,
                    Is.EqualTo(TerrainLiquidEdgeWaveGenerator.SourceHeightInPixels));
                AssertEdgeSourceGeometry(source, sourcePaths[sourceIndex]);
            }

            for (int kindIndex = 0; kindIndex < EdgeBaseNames.Length; kindIndex++)
            {
                for (int maskValue = 1;
                     maskValue <= TerrainLiquidEdgeWaveGenerator.MaskCount;
                     maskValue++)
                {
                    string tilePath =
                        $"{TerrainLiquidEdgeWaveGenerator.OutputFolder}/{EdgeBaseNames[kindIndex]}Mask{maskValue}.asset";
                    TileBase tile = AssetDatabase.LoadAssetAtPath<TileBase>(tilePath);
                    Assert.That(tile, Is.Not.Null, tilePath);
                    SerializedObject serializedTile = new SerializedObject(tile);
                    SerializedProperty animatedSprites = serializedTile.FindProperty(
                        "m_AnimatedSprites");
                    SerializedProperty minimumSpeed = serializedTile.FindProperty("m_MinSpeed");
                    SerializedProperty maximumSpeed = serializedTile.FindProperty("m_MaxSpeed");
                    Assert.That(animatedSprites, Is.Not.Null, tilePath);
                    Assert.That(
                        animatedSprites.arraySize,
                        Is.EqualTo(TerrainLiquidEdgeWaveGenerator.FrameCount),
                        tilePath);
                    for (int frame = 0; frame < animatedSprites.arraySize; frame++)
                    {
                        Assert.That(
                            animatedSprites.GetArrayElementAtIndex(frame).objectReferenceValue,
                            Is.Not.Null,
                            $"{tilePath} frame={frame}");
                    }

                    Assert.That(minimumSpeed, Is.Not.Null, tilePath);
                    Assert.That(maximumSpeed, Is.Not.Null, tilePath);
                    Assert.That(
                        minimumSpeed.floatValue,
                        Is.EqualTo(TerrainLiquidEdgeWaveGenerator.FramesPerSecond));
                    Assert.That(
                        maximumSpeed.floatValue,
                        Is.EqualTo(TerrainLiquidEdgeWaveGenerator.FramesPerSecond));
                }
            }
        }

        /// <summary>
        /// 方向別入力が3px低い水面上端へ接続し、左右線がその上へ突き出さないことを確認します。
        /// </summary>
        /// <param name="source">Read/Write有効でImportされた64×48 px入力です。</param>
        /// <param name="assetPath">失敗時に修正対象を示すAssetパスです。</param>
        private static void AssertEdgeSourceGeometry(Texture2D source, string assetPath)
        {
            int tileSize = TerrainLiquidEdgeWaveGenerator.TileSizeInPixels;
            int visibleTop = tileSize - TerrainLiquidPresentationUtility.SurfaceClipPixels - 1;
            for (int frame = 0; frame < TerrainLiquidEdgeWaveGenerator.FrameCount; frame++)
            {
                int frameStartX = frame * tileSize;
                for (int coordinate = 0; coordinate < tileSize; coordinate++)
                {
                    Assert.That(
                        source.GetPixel(frameStartX + coordinate, (tileSize * 2) + visibleTop).a,
                        Is.EqualTo(1f).Within(0.001f),
                        $"{assetPath} Back frame={frame} x={coordinate}");

                    bool shouldDrawSide = coordinate <= visibleTop;
                    Assert.That(
                        source.GetPixel(frameStartX, tileSize + coordinate).a,
                        Is.EqualTo(shouldDrawSide ? 1f : 0f).Within(0.001f),
                        $"{assetPath} Left frame={frame} y={coordinate}");
                    Assert.That(
                        source.GetPixel(frameStartX + tileSize - 1, coordinate).a,
                        Is.EqualTo(shouldDrawSide ? 1f : 0f).Within(0.001f),
                        $"{assetPath} Right frame={frame} y={coordinate}");
                }
            }
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
