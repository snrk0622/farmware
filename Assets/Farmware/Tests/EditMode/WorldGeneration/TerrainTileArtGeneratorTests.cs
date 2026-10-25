// このファイルは、地形タイルEditor生成の暗色化、境界合成、Sprite数、入力契約を検証します。
// 実ワールドやSceneを生成せず、16×16 pxの小さな入力だけで新しいGroundTop共有仕様の退行を検出します。

using System;
using System.Collections.Generic;
using System.Reflection;
using Farmware.WorldGeneration.Authoring;
using Farmware.WorldGeneration.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 地形材質Sprite生成の純粋画素処理と、Authoring設定から導く枚数・検証結果を保証します。
    /// </summary>
    public sealed class TerrainTileArtGeneratorTests
    {
        // 各テストで作ったUnityEngine.Objectを保持し、終了時にEditorメモリへ残さないよう破棄します。
        private readonly List<UnityEngine.Object> createdObjects =
            new List<UnityEngine.Object>();

        /// <summary>各テスト後にTexture、Sprite、ScriptableObjectを破棄します。</summary>
        [TearDown]
        public void TearDown()
        {
            for (int objectIndex = 0; objectIndex < createdObjects.Count; objectIndex++)
            {
                UnityEngine.Object createdObject = createdObjects[objectIndex];
                if (createdObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdObject);
                }
            }

            createdObjects.Clear();
        }

        /// <summary>RGBが0.85倍後に丸められ、Alphaは維持されることを確認します。</summary>
        [Test]
        public void Darken_MultipliesRgbWithNearestRoundingAndKeepsAlpha()
        {
            Color32 result = TerrainTileArtPixelComposer.Darken(
                new Color32(101, 100, 1, 127),
                0.85f);

            Assert.That(result, Is.EqualTo(new Color32(86, 85, 1, 127)));
        }

        /// <summary>新規材質が壁暗化ON、係数0.85で始まることを確認します。</summary>
        [Test]
        public void MaterialArtSource_UsesDocumentedWallDarkenDefaults()
        {
            TerrainMaterialArtSource material = new TerrainMaterialArtSource();

            Assert.That(
                material.DarkenWallSurface,
                Is.EqualTo(TerrainMaterialArtSource.DefaultDarkenWallSurface));
            Assert.That(
                material.WallDarkenFactor,
                Is.EqualTo(TerrainMaterialArtSource.DefaultWallDarkenFactor));
        }

        /// <summary>
        /// Authoring型に専用WallTop部品、外周幅、共通黒塗り、Weightが残っていないことを確認します。
        /// </summary>
        [Test]
        public void AuthoringTypes_DoNotContainRemovedWallTopOrWeightFields()
        {
            const BindingFlags flags = BindingFlags.Instance
                | BindingFlags.NonPublic
                | BindingFlags.Public;
            Type materialType = typeof(TerrainMaterialArtSource);
            Type settingsType = typeof(TerrainTileArtGenerationSettings);

            Assert.That(materialType.GetField("groundTopSprites", flags)?.FieldType,
                Is.EqualTo(typeof(Sprite[])));
            Assert.That(materialType.GetField("faceSprites", flags)?.FieldType,
                Is.EqualTo(typeof(Sprite[])));
            Assert.That(materialType.GetField("faceBoundaryOverlays", flags)?.FieldType,
                Is.EqualTo(typeof(Sprite[])));
            Assert.That(materialType.GetField("wallTopRims", flags), Is.Null);
            Assert.That(materialType.GetField("topRimWidth", flags), Is.Null);
            Assert.That(materialType.GetField("sideRimWidth", flags), Is.Null);
            Assert.That(materialType.GetField("bottomRimWidth", flags), Is.Null);
            Assert.That(materialType.GetField("weight", flags), Is.Null);
            Assert.That(settingsType.GetField("wallInteriorDark", flags), Is.Null);
            Assert.That(
                typeof(TerrainMaterialArtSource).Assembly.GetType(
                    "Farmware.WorldGeneration.Authoring.TerrainWallTopRimInput"),
                Is.Null);
        }

        /// <summary>Wall用断面1段目ではOverlayだけが暗く、Faceは変わらないことを確認します。</summary>
        [Test]
        public void ComposeFaceBoundary_DarkensOnlyOpaqueOverlayPixels()
        {
            Color32 faceColor = new Color32(120, 80, 40, 255);
            Color32[] face = CreateSolidPixels(faceColor);
            Color32[] overlay = CreateSolidPixels(new Color32(0, 0, 0, 0));
            overlay[0] = new Color32(100, 200, 50, 255);

            Color32[] result = TerrainTileArtPixelComposer.ComposeFaceBoundary(
                face,
                overlay,
                true,
                0.5f);

            Assert.That(result[0], Is.EqualTo(new Color32(50, 100, 25, 255)));
            Assert.That(result[1], Is.EqualTo(faceColor));
        }

        /// <summary>FaceBoundaryOverlayの半透明画素を明示的に拒否することを確認します。</summary>
        [Test]
        public void ComposeFaceBoundary_RejectsPartiallyTransparentOverlay()
        {
            Color32[] face = CreateSolidPixels(new Color32(100, 100, 100, 255));
            Color32[] overlay = CreateSolidPixels(new Color32(0, 0, 0, 0));
            overlay[5] = new Color32(10, 20, 30, 128);

            Assert.Throws<ArgumentException>(() =>
                TerrainTileArtPixelComposer.ComposeFaceBoundary(
                    face,
                    overlay,
                    false,
                    1f));
        }

        /// <summary>
        /// GroundTop 4、Face 4、境界4、壁暗化ONなら、手描き12枚、生成32枚になることを確認します。
        /// </summary>
        [Test]
        public void CalculateSpriteCounts_DoesNotGenerateDedicatedWallTopSprites()
        {
            TerrainMaterialArtSource material = CreateMaterial(
                groundCount: 4,
                faceCount: 4,
                boundaryCount: 4,
                darkenWall: true);

            TerrainTileArtSpriteCounts counts =
                TerrainTileArtGenerator.CalculateSpriteCounts(material);

            Assert.That(counts.HandDrawnInputCount, Is.EqualTo(12));
            Assert.That(counts.GroundBoundaryOutputCount, Is.EqualTo(16));
            Assert.That(counts.WallBoundaryOutputCount, Is.EqualTo(16));
            Assert.That(counts.GeneratedOutputCount, Is.EqualTo(32));
            Assert.That(counts.RuntimeCandidateCount, Is.EqualTo(40));
        }

        /// <summary>境界なしでは生成0枚となり、GroundTopとFaceだけが候補になることを確認します。</summary>
        [Test]
        public void CalculateSpriteCounts_WithoutBoundaryGeneratesNothing()
        {
            TerrainMaterialArtSource material = CreateMaterial(
                groundCount: 4,
                faceCount: 4,
                boundaryCount: 0,
                darkenWall: true);

            TerrainTileArtSpriteCounts counts =
                TerrainTileArtGenerator.CalculateSpriteCounts(material);

            Assert.That(counts.HandDrawnInputCount, Is.EqualTo(8));
            Assert.That(counts.GeneratedOutputCount, Is.Zero);
            Assert.That(counts.RuntimeCandidateCount, Is.EqualTo(8));
        }

        /// <summary>GroundTopとFaceだけで有効になり、専用WallTop入力を要求しないことを確認します。</summary>
        [Test]
        public void ValidateMaterial_DoesNotRequireDedicatedWallTopInput()
        {
            TerrainMaterialArtSource material = CreateMaterial(1, 1, 0, true);
            SetPrivateField(material, "materialId", "GrassLoam");
            TerrainTileArtGenerationSettings settings =
                ScriptableObject.CreateInstance<TerrainTileArtGenerationSettings>();
            createdObjects.Add(settings);
            SetPrivateField(settings, "materials", new[] { material });

            TerrainTileArtValidationReport report =
                TerrainTileArtGenerator.ValidateMaterial(settings, 0);

            Assert.That(report.HasErrors, Is.False, string.Join("\n", report.Errors));
        }

        /// <summary>指定数の透明／不透明16×16 Spriteを持つ材質設定を組み立てます。</summary>
        /// <param name="groundCount">GroundTop数です。</param>
        /// <param name="faceCount">Face数です。</param>
        /// <param name="boundaryCount">FaceBoundaryOverlay数です。</param>
        /// <param name="darkenWall">壁暗化設定です。</param>
        /// <returns>Privateフィールドまでテスト用に設定した材質です。</returns>
        private TerrainMaterialArtSource CreateMaterial(
            int groundCount,
            int faceCount,
            int boundaryCount,
            bool darkenWall)
        {
            TerrainMaterialArtSource material = new TerrainMaterialArtSource();
            SetPrivateField(material, "groundTopSprites", CreateSprites(groundCount, false));
            SetPrivateField(material, "faceSprites", CreateSprites(faceCount, false));
            SetPrivateField(
                material,
                "faceBoundaryOverlays",
                CreateSprites(boundaryCount, true));
            SetPrivateField(material, "darkenWallSurface", darkenWall);
            return material;
        }

        /// <summary>指定数の16×16 Spriteを作り、TearDown対象へ登録します。</summary>
        /// <param name="count">作成数です。</param>
        /// <param name="transparent">透明Spriteを作る場合はtrueです。</param>
        /// <returns>作成したSprite配列です。</returns>
        private Sprite[] CreateSprites(int count, bool transparent)
        {
            Sprite[] sprites = new Sprite[count];
            Color32 color = transparent
                ? new Color32(0, 0, 0, 0)
                : new Color32(100, 120, 80, 255);
            for (int index = 0; index < count; index++)
            {
                Texture2D texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                texture.SetPixels32(CreateSolidPixels(color));
                texture.Apply(false, false);
                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, 16f, 16f),
                    new Vector2(0.5f, 0.5f),
                    16f);
                createdObjects.Add(sprite);
                createdObjects.Add(texture);
                sprites[index] = sprite;
            }

            return sprites;
        }

        /// <summary>16×16 pxを指定色で満たす画素配列を作ります。</summary>
        /// <param name="color">全画素へ設定する色です。</param>
        /// <returns>256画素の配列です。</returns>
        private static Color32[] CreateSolidPixels(Color32 color)
        {
            Color32[] pixels = new Color32[TerrainTileArtPixelComposer.PixelCount];
            for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
            {
                pixels[pixelIndex] = color;
            }

            return pixels;
        }

        /// <summary>UnityのSerializeFieldをInspectorと同じ値へテスト内で設定します。</summary>
        /// <param name="target">設定対象の通常Objectです。</param>
        /// <param name="fieldName">Privateフィールド名です。</param>
        /// <param name="value">設定する値です。</param>
        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Privateフィールドが見つかりません: {fieldName}");
            field.SetValue(target, value);
        }
    }
}
