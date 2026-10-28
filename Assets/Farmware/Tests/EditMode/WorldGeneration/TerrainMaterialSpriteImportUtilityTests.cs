// このファイルは、地形材質PNG専用の自動Import設定と4×3分割を検証します。
// 一時PNGをAssets配下へImportし、既存Sprite IDを維持したまま再検査できることまで保証します。

using System;
using System.Collections.Generic;
using System.IO;
using Farmware.WorldGeneration.Authoring;
using Farmware.WorldGeneration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 材質入力専用フォルダーの対象判定、Importer設定、Sprite分割、再実行時のID維持を検証します。
    /// テストPNGは各テスト後にAssetDatabaseから削除し、Projectへ一時Assetを残しません。
    /// </summary>
    public sealed class TerrainMaterialSpriteImportUtilityTests
    {
        // 実製品の材質と衝突しない、テスト専用PNGのAssets相対パスです。
        private const string TestAssetPath =
            TerrainMaterialSpriteImportUtility.SourceFolder
            + "/__TerrainMaterialSpriteImportTest.png";

        /// <summary>各テスト前に、前回の異常終了で残った一時PNGとmetaをAssetDatabase経由で削除します。</summary>
        [SetUp]
        public void SetUp()
        {
            DeleteTestAsset();
        }

        /// <summary>各テスト後に一時PNGを削除し、Project変更通知を実材質だけの状態へ戻します。</summary>
        [TearDown]
        public void TearDown()
        {
            DeleteTestAsset();
        }

        /// <summary>材質Source配下のPNGだけが対象となり、別フォルダーや別拡張子を変更しないことを確認します。</summary>
        [Test]
        public void IsMaterialSourceTexture_AcceptsOnlyPngBelowDedicatedFolder()
        {
            Assert.That(
                TerrainMaterialSpriteImportUtility.IsMaterialSourceTexture(
                    TerrainMaterialSpriteImportUtility.SourceFolder + "/NewMaterial.png"),
                Is.True);
            Assert.That(
                TerrainMaterialSpriteImportUtility.IsMaterialSourceTexture(
                    TerrainMaterialSpriteImportUtility.SourceFolder + "/Sub/NewMaterial.PNG"),
                Is.True);
            Assert.That(
                TerrainMaterialSpriteImportUtility.IsMaterialSourceTexture(
                    "Assets/Farmware/Art/Animals/NewMaterial.png"),
                Is.False);
            Assert.That(
                TerrainMaterialSpriteImportUtility.IsMaterialSourceTexture(
                    TerrainMaterialSpriteImportUtility.SourceFolder + "/Readme.txt"),
                Is.False);
        }

        /// <summary>
        /// 新規64×48 px PNGへPixel Art設定と上段左から始まる12区画を作り、
        /// 同じパスの画像内容を修正して再ImportしてもSprite IDを維持することを確認します。
        /// </summary>
        [Test]
        public void EnsureImported_NewMaterialTexture_ConfiguresAndKeepsSpriteIds()
        {
            CreateAndImportTestPng(
                TerrainMaterialSpriteImportUtility.ExpectedWidthInPixels,
                TerrainMaterialSpriteImportUtility.ExpectedHeightInPixels);

            TerrainMaterialSpriteImportResult firstResult =
                TerrainMaterialSpriteImportUtility.EnsureImported(TestAssetPath);

            Assert.That(firstResult.Changed, Is.True);
            Assert.That(firstResult.CreatedSpriteRects, Is.True);
            AssertImporterSettings(TestAssetPath);
            SpriteRect[] firstRects = ReadSpriteRects(TestAssetPath);
            AssertExpectedRects(firstRects);

            GUID[] firstIds = new GUID[firstRects.Length];
            for (int index = 0; index < firstRects.Length; index++)
            {
                SpriteRect spriteRect = FindSpriteRectByName(
                    firstRects,
                    $"__TerrainMaterialSpriteImportTest_{index}");
                Assert.That(spriteRect, Is.Not.Null);
                firstIds[index] = spriteRect.spriteID;
            }

            // 制作者がPNGを書き出し直す通常フローを再現し、画像内容だけの変更で既存参照が切れないことを確認します。
            CreateAndImportTestPng(
                TerrainMaterialSpriteImportUtility.ExpectedWidthInPixels,
                TerrainMaterialSpriteImportUtility.ExpectedHeightInPixels,
                new Color32(140, 95, 60, 255));
            TerrainMaterialSpriteImportResult secondResult =
                TerrainMaterialSpriteImportUtility.EnsureImported(TestAssetPath);
            SpriteRect[] secondRects = ReadSpriteRects(TestAssetPath);

            Assert.That(secondResult.Changed, Is.False);
            Assert.That(secondResult.CreatedSpriteRects, Is.False);
            Assert.That(secondRects.Length, Is.EqualTo(firstIds.Length));
            for (int index = 0; index < secondRects.Length; index++)
            {
                SpriteRect spriteRect = FindSpriteRectByName(
                    secondRects,
                    $"__TerrainMaterialSpriteImportTest_{index}");
                Assert.That(spriteRect, Is.Not.Null);
                Assert.That(
                    spriteRect.spriteID,
                    Is.EqualTo(firstIds[index]),
                    $"再検査でSprite IDが変わりました: index={index}");
            }
        }

        /// <summary>規約外寸法を部分的に設定せず拒否し、制作側が64×48 pxへ直せる理由を返すことを確認します。</summary>
        [Test]
        public void EnsureImported_InvalidDimensions_RejectsBeforeSlicing()
        {
            CreateAndImportTestPng(32, 32);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                TerrainMaterialSpriteImportUtility.EnsureImported(TestAssetPath));

            Assert.That(exception.Message, Does.Contain("64×48 px"));
            Assert.That(ReadSpriteRects(TestAssetPath).Length, Is.LessThanOrEqualTo(1));
        }

        /// <summary>
        /// 初回Import中にUnityが透明領域基準の規約外区画を作った場合は、参照が作られる前なので12区画へ置換できることを確認します。
        /// </summary>
        [Test]
        public void EnsureImported_InitialImportWithUnexpectedRects_ReplacesWithGrid()
        {
            CreateAndImportTestPng(
                TerrainMaterialSpriteImportUtility.ExpectedWidthInPixels,
                TerrainMaterialSpriteImportUtility.ExpectedHeightInPixels);
            ConfigureUnexpectedTwoSpriteRects(TestAssetPath);

            TerrainMaterialSpriteImportResult result =
                TerrainMaterialSpriteImportUtility.EnsureImported(TestAssetPath, true);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.CreatedSpriteRects, Is.True);
            AssertExpectedRects(ReadSpriteRects(TestAssetPath));
        }

        /// <summary>
        /// 同じ規約外区画でも通常の再Importでは置換せず、既存Sprite IDと参照を保護することを確認します。
        /// </summary>
        [Test]
        public void EnsureImported_ExistingUnexpectedRects_RejectsWithoutChangingIds()
        {
            CreateAndImportTestPng(
                TerrainMaterialSpriteImportUtility.ExpectedWidthInPixels,
                TerrainMaterialSpriteImportUtility.ExpectedHeightInPixels);
            ConfigureUnexpectedTwoSpriteRects(TestAssetPath);
            SpriteRect[] beforeRects = ReadSpriteRects(TestAssetPath);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                TerrainMaterialSpriteImportUtility.EnsureImported(TestAssetPath));
            SpriteRect[] afterRects = ReadSpriteRects(TestAssetPath);

            Assert.That(exception.Message, Does.Contain("既存Sprite分割は自動修復しません"));
            Assert.That(afterRects.Length, Is.EqualTo(beforeRects.Length));
            // 区画数だけでなくIDと座標も比較し、例外を返す前に部分変更していないことを保証します。
            for (int index = 0; index < beforeRects.Length; index++)
            {
                Assert.That(afterRects[index].spriteID, Is.EqualTo(beforeRects[index].spriteID));
                Assert.That(afterRects[index].rect, Is.EqualTo(beforeRects[index].rect));
            }
        }

        /// <summary>既存の境界なし8区画と境界あり12区画を、変更やID再作成なしで受理することを確認します。</summary>
        /// <param name="fileName">Sourceフォルダーに存在する代表材質PNG名です。</param>
        /// <param name="expectedSpriteCount">境界有無に応じた既存区画数です。</param>
        [TestCase("Basalt.png", TerrainMaterialSpriteImportUtility.BoundarylessSpriteCount)]
        [TestCase("GrassLoam.png", TerrainMaterialSpriteImportUtility.ExpectedSpriteCount)]
        public void EnsureImported_ExistingValidLayout_DoesNotRewrite(
            string fileName,
            int expectedSpriteCount)
        {
            string assetPath = TerrainMaterialSpriteImportUtility.SourceFolder + "/" + fileName;

            TerrainMaterialSpriteImportResult result =
                TerrainMaterialSpriteImportUtility.EnsureImported(assetPath);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.CreatedSpriteRects, Is.False);
            Assert.That(ReadSpriteRects(assetPath).Length, Is.EqualTo(expectedSpriteCount));
        }

        /// <summary>一時Textureを指定寸法で作り、Unityの通常初回Importまで同期的に完了します。</summary>
        /// <param name="width">作成する元PNGの横幅です。</param>
        /// <param name="height">作成する元PNGの高さです。</param>
        /// <param name="fillColor">画像修正を再現するときの塗り色です。未指定時は初回用の緑色を使います。</param>
        private static void CreateAndImportTestPng(
            int width,
            int height,
            Color32? fillColor = null)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            try
            {
                Color32 color = fillColor ?? new Color32(90, 120, 70, 255);
                Color32[] pixels = new Color32[checked(width * height)];
                for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
                {
                    pixels[pixelIndex] = color;
                }

                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                string absolutePath = AssetPathToAbsolutePath(TestAssetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
                File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(TestAssetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>自動設定後のTextureImporterが材質入力用Pixel Art契約を満たすことを確認します。</summary>
        /// <param name="assetPath">検査する一時PNGのAssets相対パスです。</param>
        private static void AssertImporterSettings(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Multiple));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(16f));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.alphaSource, Is.EqualTo(TextureImporterAlphaSource.FromInput));
            Assert.That(importer.alphaIsTransparency, Is.True);
            Assert.That(importer.sRGBTexture, Is.True);
            Assert.That(importer.isReadable, Is.True);
            Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None));

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            Assert.That(settings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect));
        }

        /// <summary>Data Provider経由で現在のSprite区画を読み、Importer固有の内部形式へ依存しないようにします。</summary>
        /// <param name="assetPath">区画を読むPNGのAssets相対パスです。</param>
        /// <returns>Importerへ保存されているSprite区画です。</returns>
        private static SpriteRect[] ReadSpriteRects(string assetPath)
        {
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            Assert.That(importer, Is.Not.Null);
            SpriteDataProviderFactories factories = new SpriteDataProviderFactories();
            factories.Init();
            ISpriteEditorDataProvider dataProvider =
                factories.GetSpriteEditorDataProviderFromObject(importer);
            Assert.That(dataProvider, Is.Not.Null);
            dataProvider.InitSpriteEditorDataProvider();
            return dataProvider.GetSpriteRects();
        }

        /// <summary>
        /// 透明領域基準の自動Sliceを模した横長2区画を保存し、Asepriteからの初回Exportで発生した問題を再現します。
        /// </summary>
        /// <param name="assetPath">規約外区画を設定するテストPNGのAssets相対パスです。</param>
        private static void ConfigureUnexpectedTwoSpriteRects(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.SaveAndReimport();

            ISpriteEditorDataProvider dataProvider = CreateSpriteDataProvider(importer);
            ISpriteNameFileIdDataProvider nameFileIdProvider =
                dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            Assert.That(nameFileIdProvider, Is.Not.Null);

            GUID upperSpriteId = GUID.Generate();
            GUID lowerSpriteId = GUID.Generate();
            // 実際のMeadowSoilで発生した上下2領域の座標を使い、透明な中央帯で分離された自動Sliceを再現します。
            SpriteRect[] unexpectedRects =
            {
                new SpriteRect
                {
                    name = "__TerrainMaterialSpriteImportTest_0",
                    rect = new Rect(0f, 25f, 64f, 23f),
                    alignment = SpriteAlignment.Center,
                    pivot = Vector2.zero,
                    spriteID = upperSpriteId
                },
                new SpriteRect
                {
                    name = "__TerrainMaterialSpriteImportTest_1",
                    rect = new Rect(0f, 0f, 64f, 17f),
                    alignment = SpriteAlignment.Center,
                    pivot = Vector2.zero,
                    spriteID = lowerSpriteId
                }
            };
            List<SpriteNameFileIdPair> nameFileIdPairs = new List<SpriteNameFileIdPair>(2)
            {
                new SpriteNameFileIdPair(unexpectedRects[0].name, upperSpriteId),
                new SpriteNameFileIdPair(unexpectedRects[1].name, lowerSpriteId)
            };

            dataProvider.SetSpriteRects(unexpectedRects);
            nameFileIdProvider.SetNameFileIdPairs(nameFileIdPairs);
            dataProvider.Apply();
            importer.SaveAndReimport();
        }

        /// <summary>Importerから初期化済みのSprite Editor Data Providerを取得します。</summary>
        /// <param name="importer">Sprite Multipleとして設定済みのテスト用Importerです。</param>
        /// <returns>区画と名前・File IDを読み書きできるData Providerです。</returns>
        private static ISpriteEditorDataProvider CreateSpriteDataProvider(AssetImporter importer)
        {
            SpriteDataProviderFactories factories = new SpriteDataProviderFactories();
            factories.Init();
            ISpriteEditorDataProvider dataProvider =
                factories.GetSpriteEditorDataProviderFromObject(importer);
            Assert.That(dataProvider, Is.Not.Null);
            dataProvider.InitSpriteEditorDataProvider();
            return dataProvider;
        }

        /// <summary>12区画の名前、上段左からの順序、寸法、中央Pivotが規約どおりか確認します。</summary>
        /// <param name="spriteRects">自動分割後に再読込した区画です。</param>
        private static void AssertExpectedRects(SpriteRect[] spriteRects)
        {
            Assert.That(
                spriteRects.Length,
                Is.EqualTo(TerrainMaterialSpriteImportUtility.ExpectedSpriteCount));
            int size = TerrainTileArtGenerationSettings.TileSizeInPixels;
            for (int row = 0; row < TerrainMaterialSpriteImportUtility.RowCount; row++)
            {
                for (int column = 0;
                    column < TerrainMaterialSpriteImportUtility.ColumnCount;
                    column++)
                {
                    int index = row * TerrainMaterialSpriteImportUtility.ColumnCount + column;
                    SpriteRect spriteRect = FindSpriteRectByName(
                        spriteRects,
                        $"__TerrainMaterialSpriteImportTest_{index}");
                    Assert.That(spriteRect, Is.Not.Null);
                    Assert.That(
                        spriteRect.name,
                        Is.EqualTo($"__TerrainMaterialSpriteImportTest_{index}"));
                    Assert.That(spriteRect.rect.x, Is.EqualTo(column * size));
                    Assert.That(
                        spriteRect.rect.y,
                        Is.EqualTo((TerrainMaterialSpriteImportUtility.RowCount - 1 - row) * size));
                    Assert.That(spriteRect.rect.width, Is.EqualTo(size));
                    Assert.That(spriteRect.rect.height, Is.EqualTo(size));
                    Assert.That(spriteRect.alignment, Is.EqualTo(SpriteAlignment.Center));
                    Assert.That(spriteRect.pivot, Is.EqualTo(new Vector2(0.5f, 0.5f)));
                    Assert.That(spriteRect.spriteID.Empty(), Is.False);
                }
            }
        }

        /// <summary>Data Providerの返却順に依存せず、指定名のテストSprite区画を探します。</summary>
        /// <param name="spriteRects">Importerから読み取った全区画です。</param>
        /// <param name="spriteName">上段左からの番号を含む安定名です。</param>
        /// <returns>一致した区画、存在しない場合はnullです。</returns>
        private static SpriteRect FindSpriteRectByName(
            SpriteRect[] spriteRects,
            string spriteName)
        {
            for (int index = 0; index < spriteRects.Length; index++)
            {
                if (string.Equals(spriteRects[index].name, spriteName, StringComparison.Ordinal))
                {
                    return spriteRects[index];
                }
            }

            return null;
        }

        /// <summary>一時PNGが存在する場合だけAssetDatabase経由で削除し、対応metaも同時に片付けます。</summary>
        private static void DeleteTestAsset()
        {
            if (AssetDatabase.LoadMainAssetAtPath(TestAssetPath) != null)
            {
                Assert.That(
                    AssetDatabase.DeleteAsset(TestAssetPath),
                    Is.True,
                    "テスト用材質PNGを削除できませんでした。");
                return;
            }

            // 初回Import前にテストが停止した場合だけ、AssetDatabaseに未登録のPNGを直接片付けます。
            string absolutePath = AssetPathToAbsolutePath(TestAssetPath);
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }

            string metaPath = absolutePath + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }

        /// <summary>Assets相対パスを、現在のUnity Project内にある絶対ファイルパスへ変換します。</summary>
        /// <param name="assetPath">Assetsから始まるUnity Assetパスです。</param>
        /// <returns>OSの区切りを使う絶対パスです。</returns>
        private static string AssetPathToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.That(projectRoot, Is.Not.Null.And.Not.Empty);
            return Path.Combine(
                projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
