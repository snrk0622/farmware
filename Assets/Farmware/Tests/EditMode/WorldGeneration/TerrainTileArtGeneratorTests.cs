// このファイルは、C6-A地形タイルEditor生成の画素規則、Sprite数、入力拒否条件を検証します。
// 実ワールドやSceneを生成せず、16×16 pxの小さな入力だけで反転・接続・暗色化の退行を検出します。

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Farmware.WorldGeneration.Authoring;
using Farmware.WorldGeneration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 地形材質Sprite生成の純粋画素処理と、Authoring設定から導く枚数・検証結果を保証します。
    /// </summary>
    public sealed class TerrainTileArtGeneratorTests
    {
        // 各テストで作ったUnityEngine.Objectを保持し、TearDownで確実に破棄してEditorメモリへ残しません。
        private readonly List<UnityEngine.Object> createdObjects =
            new List<UnityEngine.Object>();

        /// <summary>
        /// 各テストの後にTexture、Sprite、ScriptableObjectを破棄し、ほかのテストへ状態を持ち越しません。
        /// </summary>
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

        /// <summary>
        /// sRGB各成分が0.85倍後に最も近い整数へ丸められ、Alphaだけは元値を維持することを確認します。
        /// </summary>
        [Test]
        public void Darken_MultipliesRgbWithNearestRoundingAndKeepsAlpha()
        {
            Color32 result = TerrainTileArtPixelComposer.Darken(
                new Color32(101, 100, 1, 127),
                0.85f);

            Assert.That(result.r, Is.EqualTo(86));
            Assert.That(result.g, Is.EqualTo(85));
            Assert.That(result.b, Is.EqualTo(1));
            Assert.That(result.a, Is.EqualTo(127));
        }

        /// <summary>
        /// 新規材質の外周幅、Wall暗色化の有効状態、暗色化係数が仕様の初期値で始まることを確認します。
        /// </summary>
        [Test]
        public void MaterialArtSource_UsesDocumentedRimAndDarkenDefaults()
        {
            TerrainMaterialArtSource material = new TerrainMaterialArtSource();

            Assert.That(
                material.TopRimWidth,
                Is.EqualTo(TerrainMaterialArtSource.DefaultTopRimWidth));
            Assert.That(
                material.SideRimWidth,
                Is.EqualTo(TerrainMaterialArtSource.DefaultSideRimWidth));
            Assert.That(
                material.BottomRimWidth,
                Is.EqualTo(TerrainMaterialArtSource.DefaultBottomRimWidth));
            Assert.That(
                material.DarkenWallSurface,
                Is.EqualTo(TerrainMaterialArtSource.DefaultDarkenWallSurface));
            Assert.That(
                material.WallDarkenFactor,
                Is.EqualTo(TerrainMaterialArtSource.DefaultWallDarkenFactor));
        }

        /// <summary>
        /// 材質差分が追加設定を持たないSprite配列であり、WallTop外周セットにもWeight調整用フィールドが残っていないことを確認します。
        /// </summary>
        [Test]
        public void MaterialInputs_DoNotContainWeightAdjustmentFields()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type materialType = typeof(TerrainMaterialArtSource);
            Type rimType = typeof(TerrainWallTopRimInput);

            Assert.That(
                materialType.GetField("groundTopSprites", flags)?.FieldType,
                Is.EqualTo(typeof(Sprite[])));
            Assert.That(
                materialType.GetField("faceSprites", flags)?.FieldType,
                Is.EqualTo(typeof(Sprite[])));
            Assert.That(
                materialType.GetField("faceBoundaryOverlays", flags)?.FieldType,
                Is.EqualTo(typeof(Sprite[])));
            Assert.That(rimType.GetField("weight", flags), Is.Null);
            Assert.That(rimType.GetField("overrideWeight", flags), Is.Null);
        }

        /// <summary>
        /// Wall用断面1段目ではOverlay画素だけが暗くなり、透明部分のFaceと不透明Face画素が変わらないことを確認します。
        /// </summary>
        [Test]
        public void ComposeFaceBoundary_DarkensOnlyOpaqueOverlayPixels()
        {
            Color32 faceColor = new Color32(120, 80, 40, 255);
            Color32 overlayColor = new Color32(100, 200, 50, 255);
            Color32[] face = CreateSolidPixels(faceColor);
            Color32[] overlay = CreateSolidPixels(new Color32(0, 0, 0, 0));
            overlay[0] = overlayColor;

            Color32[] result = TerrainTileArtPixelComposer.ComposeFaceBoundary(
                face,
                overlay,
                true,
                0.5f);

            Assert.That(result[0], Is.EqualTo(new Color32(50, 100, 25, 255)));
            Assert.That(result[1], Is.EqualTo(faceColor));
        }

        /// <summary>
        /// FaceBoundaryOverlayに半透明画素がある場合、曖昧な合成結果を作らず明示的に拒否することを確認します。
        /// </summary>
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
        /// 孤立Wallでは四辺・四隅が各部品から作られ、中央だけが共通暗色になることを確認します。
        /// </summary>
        [Test]
        public void ComposeWallTop_IsolatedWallUsesEdgesCornersAndInterior()
        {
            Color32 top = new Color32(200, 10, 10, 255);
            Color32 bottom = new Color32(10, 200, 10, 255);
            Color32 left = new Color32(10, 10, 200, 255);
            Color32 topCorner = new Color32(220, 220, 10, 255);
            Color32 bottomCorner = new Color32(220, 10, 220, 255);
            Color32 interior = new Color32(13, 16, 22, 255);

            Color32[] result = TerrainTileArtPixelComposer.ComposeWallTop(
                CreateSolidPixels(top),
                CreateSolidPixels(bottom),
                CreateSolidPixels(left),
                CreateSolidPixels(topCorner),
                CreateSolidPixels(bottomCorner),
                TerrainWallConnectionMask.None,
                3,
                4,
                5,
                interior,
                false,
                1f);

            Assert.That(result[ToIndex(0, 15)], Is.EqualTo(topCorner));
            Assert.That(result[ToIndex(15, 15)], Is.EqualTo(topCorner));
            Assert.That(result[ToIndex(8, 15)], Is.EqualTo(top));
            Assert.That(result[ToIndex(0, 0)], Is.EqualTo(bottomCorner));
            Assert.That(result[ToIndex(15, 0)], Is.EqualTo(bottomCorner));
            Assert.That(result[ToIndex(8, 0)], Is.EqualTo(bottom));
            Assert.That(result[ToIndex(0, 8)], Is.EqualTo(left));
            Assert.That(result[ToIndex(15, 8)], Is.EqualTo(left));
            Assert.That(result[ToIndex(8, 8)], Is.EqualTo(interior));
        }

        /// <summary>
        /// 全方向が同材質Wallへ接続する内部セルは、外周を一切出さず共通暗色だけになることを確認します。
        /// </summary>
        [Test]
        public void ComposeWallTop_FullyConnectedWallUsesOnlyInteriorDark()
        {
            Color32 rim = new Color32(200, 120, 80, 255);
            Color32 interior = new Color32(13, 16, 22, 255);

            Color32[] result = TerrainTileArtPixelComposer.ComposeWallTop(
                CreateSolidPixels(rim),
                CreateSolidPixels(rim),
                CreateSolidPixels(rim),
                CreateSolidPixels(rim),
                CreateSolidPixels(rim),
                TerrainWallConnectionMask.All,
                3,
                4,
                5,
                interior,
                true,
                0.5f);

            for (int pixelIndex = 0; pixelIndex < result.Length; pixelIndex++)
            {
                Assert.That(result[pixelIndex], Is.EqualTo(interior));
            }
        }

        /// <summary>
        /// 上1・右2・下4・左8の各bitが、対応する辺だけを共通暗色へ置き換えることを確認します。
        /// </summary>
        /// <param name="maskValue">接続扱いにして隠す1方向のbit値です。</param>
        /// <param name="sampleX">対応辺の中央付近を読む水平座標です。</param>
        /// <param name="sampleY">対応辺の中央付近を読む垂直座標です。</param>
        [TestCase(1, 8, 15)]
        [TestCase(2, 15, 8)]
        [TestCase(4, 8, 0)]
        [TestCase(8, 0, 8)]
        public void ComposeWallTop_ConnectionBitHidesMatchingEdge(
            int maskValue,
            int sampleX,
            int sampleY)
        {
            Color32 rim = new Color32(180, 120, 80, 255);
            Color32 interior = new Color32(13, 16, 22, 255);

            Color32[] result = TerrainTileArtPixelComposer.ComposeWallTop(
                CreateSolidPixels(rim),
                CreateSolidPixels(rim),
                CreateSolidPixels(rim),
                CreateSolidPixels(rim),
                CreateSolidPixels(rim),
                (TerrainWallConnectionMask)maskValue,
                3,
                4,
                5,
                interior,
                false,
                1f);

            Assert.That(result[ToIndex(sampleX, sampleY)], Is.EqualTo(interior));
        }

        /// <summary>
        /// 右辺は左辺入力の水平反転から作られ、別の右辺Spriteを必要としないことを確認します。
        /// </summary>
        [Test]
        public void ComposeWallTop_RightEdgeMirrorsLeftEdgePixels()
        {
            Color32 transparent = new Color32(0, 0, 0, 0);
            Color32[] left = CreateSolidPixels(transparent);
            Color32 expectedMirroredColor = new Color32(31, 62, 93, 255);
            left[ToIndex(0, 8)] = expectedMirroredColor;

            Color32[] result = TerrainTileArtPixelComposer.ComposeWallTop(
                CreateSolidPixels(transparent),
                CreateSolidPixels(transparent),
                left,
                CreateSolidPixels(transparent),
                CreateSolidPixels(transparent),
                TerrainWallConnectionMask.Up
                    | TerrainWallConnectionMask.Down
                    | TerrainWallConnectionMask.Left,
                3,
                4,
                5,
                new Color32(13, 16, 22, 255),
                false,
                1f);

            Assert.That(result[ToIndex(15, 8)], Is.EqualTo(expectedMirroredColor));
        }

        /// <summary>
        /// 既定のG=4、F=4、B=2、W=1かつWall暗色化有効から、仕様式どおりの枚数を算出することを確認します。
        /// </summary>
        [Test]
        public void CalculateSpriteCounts_UsesVariableArrayFormula()
        {
            TerrainMaterialArtSource material = new TerrainMaterialArtSource();
            SetPrivateField(
                material,
                "groundTopSprites",
                new Sprite[4]);
            SetPrivateField(
                material,
                "faceSprites",
                new Sprite[4]);
            SetPrivateField(
                material,
                "faceBoundaryOverlays",
                new Sprite[2]);
            SetPrivateField(
                material,
                "wallTopRims",
                new[] { new TerrainWallTopRimInput() });
            SetPrivateField(material, "darkenWallSurface", true);

            TerrainTileArtSpriteCounts counts =
                TerrainTileArtGenerator.CalculateSpriteCounts(material);

            Assert.That(counts.HandDrawnInputCount, Is.EqualTo(15));
            Assert.That(counts.WallTopOutputCount, Is.EqualTo(16));
            Assert.That(counts.GroundBoundaryOutputCount, Is.EqualTo(8));
            Assert.That(counts.WallBoundaryOutputCount, Is.EqualTo(8));
            Assert.That(counts.GeneratedOutputCount, Is.EqualTo(32));
            Assert.That(counts.RuntimeCandidateCount, Is.EqualTo(40));
        }

        /// <summary>
        /// 左右幅8pxや上下合計16pxのように中央が消える設定を、生成前に明示的に拒否することを確認します。
        /// </summary>
        [TestCase(3, 8, 5)]
        [TestCase(8, 4, 8)]
        [TestCase(0, 4, 5)]
        public void ValidateWidths_RejectsMissingInterior(
            int topWidth,
            int sideWidth,
            int bottomWidth)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                TerrainTileArtPixelComposer.ValidateWidths(
                    topWidth,
                    sideWidth,
                    bottomWidth));
        }

        /// <summary>
        /// 境界OverlayのAlphaが0/255以外なら、設定検証の段階で生成を拒否することを確認します。
        /// </summary>
        [Test]
        public void ValidateMaterial_RejectsSemiTransparentBoundarySprite()
        {
            Sprite opaqueSprite = CreateReadableSprite(
                CreateSolidPixels(new Color32(100, 100, 100, 255)),
                "Opaque");
            Color32[] boundaryPixels =
                CreateSolidPixels(new Color32(0, 0, 0, 0));
            boundaryPixels[0] = new Color32(100, 100, 100, 128);
            Sprite boundarySprite = CreateReadableSprite(boundaryPixels, "Boundary");
            TerrainTileArtGenerationSettings settings =
                Track(ScriptableObject.CreateInstance<TerrainTileArtGenerationSettings>());
            TerrainMaterialArtSource material = CreateCompleteMaterial(
                opaqueSprite,
                boundarySprite);
            SetPrivateField(
                settings,
                "materials",
                new[] { material });

            TerrainTileArtValidationReport report =
                TerrainTileArtGenerator.ValidateMaterial(settings, 0);

            Assert.That(report.HasErrors, Is.True);
            Assert.That(
                ContainsMessage(report.Errors, "半透明画素"),
                Is.True,
                "FaceBoundaryOverlayのAlpha違反を日本語理由付きで報告する必要があります。");
        }

        /// <summary>
        /// 実AssetDatabase上で最小の境界あり材質を生成し、18枚のPNG、Importer、GUID維持、旧境界出力整理を確認します。
        /// </summary>
        [Test]
        public void GenerateMaterial_WritesImportsAndCleansOwnedObsoleteOutputs()
        {
            string temporaryRoot =
                $"Assets/Farmware/Tests/EditMode/TerrainTileArtTemp_{Guid.NewGuid():N}";
            try
            {
                EnsureAssetFolder(temporaryRoot);
                Sprite opaqueSprite = CreateImportedSpriteAsset(
                    $"{temporaryRoot}/Opaque.png",
                    CreateSolidPixels(new Color32(120, 100, 80, 255)));
                Color32[] overlayPixels =
                    CreateSolidPixels(new Color32(0, 0, 0, 0));
                overlayPixels[ToIndex(0, 15)] = new Color32(100, 80, 60, 255);
                Sprite overlaySprite = CreateImportedSpriteAsset(
                    $"{temporaryRoot}/Overlay.png",
                    overlayPixels);
                TerrainTileArtGenerationSettings settings =
                    Track(ScriptableObject.CreateInstance<TerrainTileArtGenerationSettings>());
                TerrainMaterialArtSource material = CreateCompleteMaterial(
                    opaqueSprite,
                    overlaySprite);
                SetPrivateField(settings, "outputRootFolder", $"{temporaryRoot}/Generated");
                SetPrivateField(settings, "materials", new[] { material });

                TerrainTileArtGenerationResult firstResult =
                    TerrainTileArtGenerator.GenerateMaterial(settings, 0);

                Assert.That(firstResult.GeneratedSpriteCount, Is.EqualTo(18));
                string wallPath =
                    $"{temporaryRoot}/Generated/GrassLoam/WallTop/GrassLoam_WallTop_w00_m0.png";
                string groundBoundaryPath =
                    $"{temporaryRoot}/Generated/GrassLoam/FaceBoundary/GrassLoam_GroundFace_f00_b00.png";
                string wallBoundaryPath =
                    $"{temporaryRoot}/Generated/GrassLoam/FaceBoundary/GrassLoam_WallFace_f00_b00.png";
                Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>(wallPath), Is.Not.Null);
                Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>(groundBoundaryPath), Is.Not.Null);
                Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>(wallBoundaryPath), Is.Not.Null);
                AssertGeneratedImporter(wallPath);

                string firstGuid = AssetDatabase.AssetPathToGUID(wallPath);
                TerrainTileArtGenerationResult secondResult =
                    TerrainTileArtGenerator.GenerateMaterial(settings, 0);
                Assert.That(secondResult.GeneratedSpriteCount, Is.EqualTo(18));
                Assert.That(AssetDatabase.AssetPathToGUID(wallPath), Is.EqualTo(firstGuid));
                Assert.That(
                    TerrainTileArtGenerator.RequiresRegeneration(settings, material),
                    Is.False);

                // 境界差分を0件へ減らした再生成では、WallTop 16枚を維持し、前回所有した境界PNGだけを削除します。
                SetPrivateField(
                    material,
                    "faceBoundaryOverlays",
                    Array.Empty<Sprite>());
                TerrainTileArtGenerationResult withoutBoundaryResult =
                    TerrainTileArtGenerator.GenerateMaterial(settings, 0);
                Assert.That(withoutBoundaryResult.GeneratedSpriteCount, Is.EqualTo(16));
                Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>(wallPath), Is.Not.Null);
                Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>(groundBoundaryPath), Is.Null);
                Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>(wallBoundaryPath), Is.Null);
            }
            finally
            {
                // テスト専用の一意なフォルダーだけを削除し、製品Assetや別テストの出力へ触れません。
                if (AssetDatabase.IsValidFolder(temporaryRoot))
                {
                    AssetDatabase.DeleteAsset(temporaryRoot);
                }
            }
        }

        /// <summary>
        /// 指定色で埋めた16×16 px配列を作り、テストごとの意図した入力を短く表現します。
        /// </summary>
        /// <param name="color">全256画素へ入れる8bit RGBAです。</param>
        /// <returns>新しく確保した256画素配列です。</returns>
        private static Color32[] CreateSolidPixels(Color32 color)
        {
            Color32[] pixels =
                new Color32[TerrainTileArtPixelComposer.PixelCount];
            for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
            {
                pixels[pixelIndex] = color;
            }

            return pixels;
        }

        /// <summary>
        /// 左下原点の16×16 px座標を一次元画素添字へ変換します。
        /// </summary>
        /// <param name="x">0〜15の水平座標です。</param>
        /// <param name="y">0〜15の垂直座標です。</param>
        /// <returns>0〜255の画素添字です。</returns>
        private static int ToIndex(int x, int y)
        {
            return y * TerrainTileArtGenerationSettings.TileSizeInPixels + x;
        }

        /// <summary>
        /// 検証テスト用に、必須Spriteと任意境界をすべて設定した1材質を作ります。
        /// </summary>
        /// <param name="opaqueSprite">GroundTop、Face、全Wall外周部品へ共用する不透明Spriteです。</param>
        /// <param name="boundarySprite">FaceBoundaryOverlayへ登録する検証対象Spriteです。</param>
        /// <returns>Alpha違反以外の必須条件を満たす材質設定です。</returns>
        private static TerrainMaterialArtSource CreateCompleteMaterial(
            Sprite opaqueSprite,
            Sprite boundarySprite)
        {
            TerrainWallTopRimInput rim = new TerrainWallTopRimInput();
            SetPrivateField(rim, "topEdge", opaqueSprite);
            SetPrivateField(rim, "bottomEdge", opaqueSprite);
            SetPrivateField(rim, "leftEdge", opaqueSprite);
            SetPrivateField(rim, "topLeftCorner", opaqueSprite);
            SetPrivateField(rim, "bottomLeftCorner", opaqueSprite);

            TerrainMaterialArtSource material = new TerrainMaterialArtSource();
            SetPrivateField(material, "materialId", "GrassLoam");
            SetPrivateField(material, "japaneseName", "草地ローム");
            SetPrivateField(material, "groundTopSprites", new[] { opaqueSprite });
            SetPrivateField(material, "faceSprites", new[] { opaqueSprite });
            SetPrivateField(
                material,
                "faceBoundaryOverlays",
                new[] { boundarySprite });
            SetPrivateField(material, "wallTopRims", new[] { rim });
            return material;
        }

        /// <summary>
        /// 16×16 pxの読み取り可能TextureとSpriteを作り、TearDown対象へ登録します。
        /// </summary>
        /// <param name="pixels">Spriteへ設定する256画素です。</param>
        /// <param name="name">検証メッセージで識別するSprite名です。</param>
        /// <returns>中央Pivot、16 PPUの読み取り可能Spriteです。</returns>
        private Sprite CreateReadableSprite(Color32[] pixels, string name)
        {
            const int size = TerrainTileArtGenerationSettings.TileSizeInPixels;
            Texture2D texture = Track(new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false,
                false));
            texture.name = $"{name}Texture";
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            Sprite sprite = Track(Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size));
            sprite.name = name;
            return sprite;
        }

        /// <summary>
        /// テスト用PNGをAssets配下へ書き、生成ツールが要求する読取可能なPixel Art SpriteとしてImportします。
        /// </summary>
        /// <param name="assetPath">一意なテスト専用フォルダー内のPNGパスです。</param>
        /// <param name="pixels">書き込む16×16 pxの画素です。</param>
        /// <returns>AssetDatabaseから読み直した単一Spriteです。</returns>
        private static Sprite CreateImportedSpriteAsset(
            string assetPath,
            Color32[] pixels)
        {
            const int size = TerrainTileArtGenerationSettings.TileSizeInPixels;
            Texture2D texture = new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false,
                false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(AssetPathToAbsolutePath(assetPath), texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 16f;
            importer.isReadable = true;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            TextureImporterSettings importerSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(importerSettings);
            importerSettings.spriteMeshType = SpriteMeshType.FullRect;
            importerSettings.spriteAlignment = (int)SpriteAlignment.Center;
            importerSettings.spritePivot = new Vector2(0.5f, 0.5f);
            importer.SetTextureSettings(importerSettings);
            importer.SaveAndReimport();
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            Assert.That(sprite, Is.Not.Null);
            return sprite;
        }

        /// <summary>
        /// 生成PNGがC6-Aの完成Sprite向けImport設定になっていることを確認します。
        /// </summary>
        /// <param name="assetPath">生成済みPNGのプロジェクト相対パスです。</param>
        private static void AssertGeneratedImporter(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(16f));
            Assert.That(importer.isReadable, Is.False);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.sRGBTexture, Is.True);

            TextureImporterSettings importerSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(importerSettings);
            Assert.That(importerSettings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect));
            Assert.That(importerSettings.spriteAlignment, Is.EqualTo((int)SpriteAlignment.Center));
        }

        /// <summary>
        /// Assets配下のテスト用フォルダーを、親から順にAssetDatabaseへ作成します。
        /// </summary>
        /// <param name="assetFolder">Assetsから始まる作成先フォルダーです。</param>
        private static void EnsureAssetFolder(string assetFolder)
        {
            string[] parts = assetFolder.Split('/');
            string current = parts[0];
            for (int partIndex = 1; partIndex < parts.Length; partIndex++)
            {
                string next = $"{current}/{parts[partIndex]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[partIndex]);
                }

                current = next;
            }
        }

        /// <summary>
        /// Assetsから始まるプロジェクト相対パスを、テストPNG書込用の絶対パスへ変換します。
        /// </summary>
        /// <param name="assetPath">Assets配下のPNGパスです。</param>
        /// <returns>File.WriteAllBytesへ渡す絶対パスです。</returns>
        private static string AssetPathToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.That(projectRoot, Is.Not.Null.And.Not.Empty);
            return Path.Combine(
                projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// テストで作ったUnityEngine.Objectを破棄対象へ登録し、型を保ったまま返します。
        /// </summary>
        /// <typeparam name="T">登録するUnityEngine.Objectの型です。</typeparam>
        /// <param name="createdObject">このテストが所有するObjectです。</param>
        /// <returns>引数と同じObject参照です。</returns>
        private T Track<T>(T createdObject) where T : UnityEngine.Object
        {
            createdObjects.Add(createdObject);
            return createdObject;
        }

        /// <summary>
        /// UnityがInspectorから書き込む非公開SerializeFieldを、Assetを作らない純粋テストで設定します。
        /// </summary>
        /// <param name="targetObject">フィールドを持つAuthoringデータです。</param>
        /// <param name="fieldName">設定する非公開インスタンスフィールド名です。</param>
        /// <param name="value">Inspector入力の代わりに設定する値です。</param>
        private static void SetPrivateField(
            object targetObject,
            string fieldName,
            object value)
        {
            FieldInfo field = targetObject.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"テスト対象フィールドが見つかりません: {fieldName}");
            field.SetValue(targetObject, value);
        }

        /// <summary>
        /// 検証エラー一覧に指定断片を含む日本語メッセージがあるか確認します。
        /// </summary>
        /// <param name="messages">ValidationReportのエラー一覧です。</param>
        /// <param name="expectedPart">含まれるべき理由の断片です。</param>
        /// <returns>少なくとも1件に断片が含まれる場合はtrueです。</returns>
        private static bool ContainsMessage(
            IReadOnlyList<string> messages,
            string expectedPart)
        {
            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
            {
                if (messages[messageIndex].Contains(expectedPart, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
