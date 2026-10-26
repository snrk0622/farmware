// このファイルは、水面反射の比較Sceneが垂直面だけを反射するProxy方式、反射Material、固定検証値を満たすことを確認します。
// Sceneを再構築する処理自体は実行せず、生成後のAssetと座標計算を検査してUnity Editorの作業Sceneを変更しません。

using System.IO;
using Farmware.WorldGeneration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// Wall前面などの垂直面だけを反転する水面反射案が、WaterSurfaceへ描かれる前提を保証します。
    /// </summary>
    public sealed class TerrainWaterReflectionComparisonMapBuilderTests
    {
        /// <summary>32px、縦圧縮0.75、最大Alpha 0.22という初期比較値を保証します。</summary>
        [Test]
        public void ReflectionPrototype_UsesDocumentedInitialValues()
        {
            Assert.That(
                TerrainWaterReflectionComparisonMapBuilder.MaximumReflectionDepthPixels,
                Is.EqualTo(32));
            Assert.That(
                TerrainWaterReflectionComparisonMapBuilder.VerticalCompression,
                Is.EqualTo(0.75f));
            Assert.That(
                TerrainWaterReflectionComparisonMapBuilder.MaximumReflectionAlpha,
                Is.EqualTo(0.22f));
            Assert.That(
                TerrainWaterReflectionComparisonMapBuilder.WavePixelsPerSecond,
                Is.EqualTo(4f));
        }

        /// <summary>反射軸より奥の垂直面を、軸の手前へ上下反転して0.75倍に圧縮することを保証します。</summary>
        [Test]
        public void CalculateReflectedWorldY_MirrorsAndCompressesVerticalSurface()
        {
            Assert.That(
                TerrainWaterReflectionComparisonMapBuilder.CalculateReflectedWorldY(
                    0.5f,
                    0.5f),
                Is.EqualTo(0.5f));
            Assert.That(
                TerrainWaterReflectionComparisonMapBuilder.CalculateReflectedWorldY(
                    1.5f,
                    0.5f),
                Is.EqualTo(-0.25f).Within(0.0001f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                TerrainWaterReflectionComparisonMapBuilder.CalculateReflectedWorldY(
                    0.25f,
                    0.5f));
        }

        /// <summary>反射Proxyと波線を描くWaterSurfaceが通常地形のDefaultより後にあることを保証します。</summary>
        [Test]
        public void WaterSurfaceSortingLayer_FollowsDefault()
        {
            SortingLayer[] sortingLayers = SortingLayer.layers;
            int defaultIndex = -1;
            int waterSurfaceIndex = -1;
            for (int index = 0; index < sortingLayers.Length; index++)
            {
                if (sortingLayers[index].name == "Default")
                {
                    defaultIndex = index;
                }
                else if (sortingLayers[index].name
                    == TerrainWaterReflectionComparisonMapBuilder.WaterSurfaceSortingLayerName)
                {
                    waterSurfaceIndex = index;
                }
            }

            Assert.That(defaultIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(waterSurfaceIndex, Is.GreaterThan(defaultIndex));
        }

        /// <summary>再構築済みScene、Shader、MaterialをAssetDatabaseから読み直せることを保証します。</summary>
        [Test]
        public void GeneratedReflectionAssets_ArePersistentAndConfigured()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                TerrainWaterReflectionComparisonMapBuilder.ReflectionShaderPath);
            Assert.That(shader, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);

            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                TerrainWaterReflectionComparisonMapBuilder.ReflectionMaterialPath);
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader, Is.EqualTo(shader));
            Assert.That(
                material.GetFloat("_MaximumAlpha"),
                Is.EqualTo(
                    TerrainWaterReflectionComparisonMapBuilder.MaximumReflectionAlpha));
            Assert.That(
                material.GetFloat("_MaximumDepthWorldUnits"),
                Is.EqualTo(
                    TerrainWaterReflectionComparisonMapBuilder.MaximumReflectionDepthPixels
                    / 16f));
            Assert.That(
                material.GetFloat("_WavePixelsPerSecond"),
                Is.EqualTo(TerrainWaterReflectionComparisonMapBuilder.WavePixelsPerSecond));
            Assert.That(
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    TerrainWaterReflectionComparisonMapBuilder.ScenePath),
                Is.Not.Null);
        }

        /// <summary>
        /// 反射ShaderがSpriteRendererの上下反転値を頂点へ適用し、非対称な模様も鏡像になることを保証します。
        /// </summary>
        [Test]
        public void ReflectionShader_AppliesSpriteRendererFlipBeforeWorldTransform()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.That(projectRoot, Is.Not.Null);
            string shaderFilePath = Path.Combine(
                projectRoot,
                TerrainWaterReflectionComparisonMapBuilder.ReflectionShaderPath);
            string shaderSource = File.ReadAllText(shaderFilePath);

            // Shaderコンパイル成功だけではflipYの適用漏れを検出できないため、公式2D Sprite Shaderと同じ反転関数の使用を確認します。
            Assert.That(
                shaderSource,
                Does.Contain("SetUpSpriteInstanceProperties();"));
            Assert.That(
                shaderSource,
                Does.Contain("UnityFlipSprite("));
            Assert.That(
                shaderSource.IndexOf("UnityFlipSprite(", System.StringComparison.Ordinal),
                Is.LessThan(shaderSource.IndexOf(
                    "TransformObjectToWorld(",
                    System.StringComparison.Ordinal)));
        }
    }
}
