// このファイルは、Ground／Wallで共用する境界ハイライト14基本形のPNGとSprite分割を再現可能に生成します。
// TerrainBoundaryHighlightUtilityの論理マスクを唯一の形状定義として使い、画像順と47接続選択のずれを防ぎます。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Farmware.WorldGeneration.Presentation;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 14基本形を横一列のPNGへ描き、16×16のMultiple SpriteとしてImportします。
    /// マテリアル固有の絵ではなく、すべてのGround／Wallへ重ねる共通の1px輪郭を担当します。
    /// </summary>
    public static class TerrainBoundaryHighlightArtGenerator
    {
        /// <summary>1つの境界Spriteの幅と高さです。単位はピクセルです。</summary>
        public const int SpriteSizeInPixels = 16;

        /// <summary>内角を1画素の点として描くための、角からの画素長です。単位はピクセルです。</summary>
        public const int InnerCornerArmLengthInPixels = 1;

        /// <summary>14基本形を横一列に並べたPNGの幅です。単位はピクセルです。</summary>
        public const int TextureWidthInPixels =
            SpriteSizeInPixels * TerrainBoundaryHighlightUtility.ExpectedSpriteCount;

        /// <summary>境界ハイライトPNGと.metaを格納するUnityプロジェクト相対フォルダです。</summary>
        private const string HighlightAssetFolder =
            "Assets/Farmware/Art/WorldGeneration/Terrain/Highlights";

        /// <summary>生成PNGへ書き込む完全透明画素です。</summary>
        private static readonly Color32 TransparentPixel = new Color32(0, 0, 0, 0);

        /// <summary>表示時にTileのAlphaを掛ける前の、ぼかしを含まない不透明白画素です。</summary>
        private static readonly Color32 HighlightPixel = new Color32(255, 255, 255, 255);

        /// <summary>
        /// メニューからPNG・Sprite分割・Atlasをまとめて更新します。
        /// </summary>
        /// <remarks>Project内の境界ハイライト画像とAtlasを更新し、Runtimeの地形データは変更しません。</remarks>
        [MenuItem("Farmware/World Generation/Generate Boundary Highlight Art")]
        public static void GenerateFromMenu()
        {
            try
            {
                GenerateOrUpdateSpriteSheet();
                WorldGenerationSpriteAtlasBuilder.GenerateOrUpdateAtlases();
                AssetDatabase.SaveAssets();
                Debug.Log("共通境界ハイライト14基本形と専用Sprite Atlasを更新しました。");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "境界ハイライトを生成できません",
                    exception.Message,
                    "閉じる");
            }
        }

        /// <summary>
        /// 論理マスクから14基本形のPNGを再生成し、安定した名前とSprite IDを持つ16×16区画へ分割します。
        /// </summary>
        /// <remarks>PNG、TextureImporter、Sprite Editorメタデータを更新します。</remarks>
        public static void GenerateOrUpdateSpriteSheet()
        {
            EnsureAssetFolder(HighlightAssetFolder);
            WriteSpriteSheetPng();
            ConfigureSpriteSheetImporter();
        }

        /// <summary>
        /// 分割済みSpriteをShape01〜Shape14の順で読み込みます。
        /// </summary>
        /// <returns>14基本形へ対応するSprite配列です。</returns>
        /// <exception cref="InvalidOperationException">Spriteの不足、重複、参照先の不一致がある場合に送出します。</exception>
        public static Sprite[] LoadRequiredSprites()
        {
            string texturePath = WorldGenerationSpriteAtlasBuilder.BoundaryHighlightTexturePath;
            string[] expectedNames =
                WorldGenerationSpriteAtlasBuilder.GetExpectedBoundaryHighlightSpriteNames();
            UnityEngine.Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(texturePath);
            var sprites = new Sprite[expectedNames.Length];
            int loadedSpriteCount = 0;

            for (int assetIndex = 0; assetIndex < allAssets.Length; assetIndex++)
            {
                if (!(allAssets[assetIndex] is Sprite sprite))
                {
                    continue;
                }

                loadedSpriteCount++;
                for (int expectedIndex = 0; expectedIndex < expectedNames.Length; expectedIndex++)
                {
                    if (!string.Equals(sprite.name, expectedNames[expectedIndex], StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (sprites[expectedIndex] != null)
                    {
                        throw new InvalidOperationException(
                            $"境界ハイライトSprite名が重複しています: {sprite.name}");
                    }

                    sprites[expectedIndex] = sprite;
                    break;
                }
            }

            if (loadedSpriteCount != expectedNames.Length)
            {
                throw new InvalidOperationException(
                    $"境界ハイライトSpriteは{expectedNames.Length}枚必要です: "
                    + $"{texturePath}, actual={loadedSpriteCount}");
            }

            for (int index = 0; index < sprites.Length; index++)
            {
                if (sprites[index] == null
                    || !string.Equals(
                        AssetDatabase.GetAssetPath(sprites[index]),
                        texturePath,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"境界ハイライトSpriteを順番どおり読み込めません: "
                        + $"index={index}, expected={expectedNames[index]}");
                }
            }

            return sprites;
        }

        /// <summary>
        /// 指定した境界特徴マスクで、16×16内の画素を白くするか判定します。
        /// 外周は1pxの連続線、内角は欠けた対角側の角に置く1pxの点として描きます。
        /// </summary>
        /// <param name="featureMask">下位4ビットが北・東・南・西の外周、上位4ビットが同順の内角です。</param>
        /// <param name="x">Sprite左端を0とする水平画素座標です。</param>
        /// <param name="y">Sprite下端を0とする垂直画素座標です。</param>
        /// <returns>不透明白で描く画素ならtrueです。</returns>
        public static bool ShouldPixelBeVisible(byte featureMask, int x, int y)
        {
            if (x < 0 || x >= SpriteSizeInPixels || y < 0 || y >= SpriteSizeInPixels)
            {
                throw new ArgumentOutOfRangeException(
                    $"画素座標は0〜{SpriteSizeInPixels - 1}で指定してください: ({x}, {y})");
            }

            const byte northEdge = 1 << 0;
            const byte eastEdge = 1 << 1;
            const byte southEdge = 1 << 2;
            const byte westEdge = 1 << 3;
            const byte northEastInnerCorner = 1 << 4;
            const byte southEastInnerCorner = 1 << 5;
            const byte southWestInnerCorner = 1 << 6;
            const byte northWestInnerCorner = 1 << 7;
            int last = SpriteSizeInPixels - 1;
            int innerLength = InnerCornerArmLengthInPixels;

            // 外周線を辺の端まで連続させ、隣接セルとの間に1pxの欠けを作らないようにします。
            bool isOuterEdge = ((featureMask & northEdge) != 0 && y == last)
                || ((featureMask & eastEdge) != 0 && x == last)
                || ((featureMask & southEdge) != 0 && y == 0)
                || ((featureMask & westEdge) != 0 && x == 0);

            // 内角は外周より強く見せず、欠けた対角方向を示す角の1画素だけで表します。
            bool isInnerCorner = ((featureMask & northEastInnerCorner) != 0
                    && ((y == last && x >= last - innerLength + 1)
                        || (x == last && y >= last - innerLength + 1)))
                || ((featureMask & southEastInnerCorner) != 0
                    && ((y == 0 && x >= last - innerLength + 1)
                        || (x == last && y < innerLength)))
                || ((featureMask & southWestInnerCorner) != 0
                    && ((y == 0 && x < innerLength)
                        || (x == 0 && y < innerLength)))
                || ((featureMask & northWestInnerCorner) != 0
                    && ((y == last && x < innerLength)
                        || (x == 0 && y >= last - innerLength + 1)));
            return isOuterEdge || isInnerCorner;
        }

        /// <summary>14基本形の画素配列を作り、既定パスへPNGとして保存します。</summary>
        private static void WriteSpriteSheetPng()
        {
            var texture = new Texture2D(
                TextureWidthInPixels,
                SpriteSizeInPixels,
                TextureFormat.RGBA32,
                mipChain: false,
                linear: false);
            try
            {
                Color32[] pixels = Enumerable.Repeat(
                        TransparentPixel,
                        TextureWidthInPixels * SpriteSizeInPixels)
                    .ToArray();

                for (int shapeIndex = 0;
                     shapeIndex < TerrainBoundaryHighlightUtility.ExpectedSpriteCount;
                     shapeIndex++)
                {
                    BoundaryHighlightShape shape = (BoundaryHighlightShape)(shapeIndex + 1);
                    byte featureMask = TerrainBoundaryHighlightUtility.GetCanonicalFeatureMask(shape);
                    for (int y = 0; y < SpriteSizeInPixels; y++)
                    {
                        for (int localX = 0; localX < SpriteSizeInPixels; localX++)
                        {
                            if (!ShouldPixelBeVisible(featureMask, localX, y))
                            {
                                continue;
                            }

                            int textureX = (shapeIndex * SpriteSizeInPixels) + localX;
                            pixels[(y * TextureWidthInPixels) + textureX] = HighlightPixel;
                        }
                    }
                }

                texture.SetPixels32(pixels);
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                byte[] pngBytes = texture.EncodeToPNG();
                string absolutePath = ToAbsoluteProjectPath(
                    WorldGenerationSpriteAtlasBuilder.BoundaryHighlightTexturePath);
                File.WriteAllBytes(absolutePath, pngBytes);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>PNGをPixel Art向けのMultiple Spriteとして設定し、14区画のIDを安定して保存します。</summary>
        private static void ConfigureSpriteSheetImporter()
        {
            string texturePath = WorldGenerationSpriteAtlasBuilder.BoundaryHighlightTexturePath;
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null
                || texture.width != TextureWidthInPixels
                || texture.height != SpriteSizeInPixels)
            {
                throw new InvalidOperationException(
                    $"境界ハイライト画像は{TextureWidthInPixels}×{SpriteSizeInPixels}ピクセルである必要があります: {texturePath}");
            }

            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"境界ハイライト画像のTextureImporterを取得できません: {texturePath}");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = SpriteSizeInPixels;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            TextureImporterSettings importerSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(importerSettings);
            importerSettings.spriteMeshType = SpriteMeshType.FullRect;
            importerSettings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(importerSettings);

            // Importer全体の設定を先に確定し、そのImporterに対応するSprite Editor Providerを再取得します。
            importer.SaveAndReimport();
            SpriteDataProviderFactories factories = new SpriteDataProviderFactories();
            factories.Init();
            ISpriteEditorDataProvider dataProvider =
                factories.GetSpriteEditorDataProviderFromObject(importer);
            if (dataProvider == null)
            {
                throw new InvalidOperationException(
                    $"境界ハイライト画像のSprite Editor Data Providerを取得できません: {texturePath}");
            }

            dataProvider.InitSpriteEditorDataProvider();
            ValidateSpriteEditingCapability(dataProvider, texturePath);

            string[] expectedNames =
                WorldGenerationSpriteAtlasBuilder.GetExpectedBoundaryHighlightSpriteNames();
            SpriteRect[] existingRects = dataProvider.GetSpriteRects();
            var configuredRects = new SpriteRect[expectedNames.Length];
            var nameFileIdPairs = new List<SpriteNameFileIdPair>(expectedNames.Length);
            for (int index = 0; index < configuredRects.Length; index++)
            {
                Rect expectedRect = new Rect(
                    index * SpriteSizeInPixels,
                    0f,
                    SpriteSizeInPixels,
                    SpriteSizeInPixels);
                GUID spriteId = FindExistingSpriteId(existingRects, expectedRect);
                if (spriteId.Empty())
                {
                    spriteId = GUID.Generate();
                }

                configuredRects[index] = new SpriteRect
                {
                    name = expectedNames[index],
                    rect = expectedRect,
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    border = Vector4.zero,
                    spriteID = spriteId
                };
                nameFileIdPairs.Add(new SpriteNameFileIdPair(expectedNames[index], spriteId));
            }

            ISpriteNameFileIdDataProvider nameFileIdProvider =
                dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameFileIdProvider == null)
            {
                throw new InvalidOperationException(
                    $"境界ハイライト画像のSprite名とIDを保存できません: {texturePath}");
            }

            dataProvider.SetSpriteRects(configuredRects);
            nameFileIdProvider.SetNameFileIdPairs(nameFileIdPairs);
            dataProvider.Apply();
            importer.SaveAndReimport();
        }

        /// <summary>Spriteの作成、名前、矩形、Pivotを安全に更新できるImporterか確認します。</summary>
        /// <param name="dataProvider">対象TextureのSprite Editor Providerです。</param>
        /// <param name="texturePath">失敗時に表示するTextureパスです。</param>
        private static void ValidateSpriteEditingCapability(
            ISpriteEditorDataProvider dataProvider,
            string texturePath)
        {
            ISpriteFrameEditCapability editCapability =
                dataProvider.GetDataProvider<ISpriteFrameEditCapability>();
            if (editCapability == null)
            {
                throw new InvalidOperationException(
                    $"境界ハイライト画像のSprite編集能力を確認できません: {texturePath}");
            }

            // Unity 6.3では複数の編集可否をまとめたEditCapabilityが返るため、具体型を固定せずAPIの戻り値を使います。
            var capability = editCapability.GetEditCapability();
            bool supportsRequiredEdits =
                capability.HasCapability(EEditCapability.CreateAndDeleteSprite)
                && capability.HasCapability(EEditCapability.EditSpriteName)
                && capability.HasCapability(EEditCapability.EditSpriteRect)
                && capability.HasCapability(EEditCapability.EditPivot);
            if (!supportsRequiredEdits)
            {
                throw new InvalidOperationException(
                    $"境界ハイライト画像の分割に必要なSprite編集能力がありません: {texturePath}");
            }
        }

        /// <summary>既存区画から同じ位置と寸法のSprite IDを探し、再生成でもScene参照を維持します。</summary>
        /// <param name="existingRects">現在Importerへ保存されている区画です。</param>
        /// <param name="expectedRect">今回必要な区画です。</param>
        /// <returns>一致する既存ID、見つからなければ空IDです。</returns>
        private static GUID FindExistingSpriteId(SpriteRect[] existingRects, Rect expectedRect)
        {
            if (existingRects == null)
            {
                return default;
            }

            for (int index = 0; index < existingRects.Length; index++)
            {
                Rect existingRect = existingRects[index].rect;
                bool isSameRect = Mathf.Approximately(existingRect.x, expectedRect.x)
                    && Mathf.Approximately(existingRect.y, expectedRect.y)
                    && Mathf.Approximately(existingRect.width, expectedRect.width)
                    && Mathf.Approximately(existingRect.height, expectedRect.height);
                if (isSameRect)
                {
                    return existingRects[index].spriteID;
                }
            }

            return default;
        }

        /// <summary>Assetsから始まるパスを、現在のUnityプロジェクト内の絶対パスへ変換します。</summary>
        /// <param name="assetPath">Unityプロジェクト相対パスです。</param>
        /// <returns>OS上の絶対パスです。</returns>
        private static string ToAbsoluteProjectPath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        /// <summary>Assets配下のフォルダ階層を浅い順に作り、PNG書き込み先を保証します。</summary>
        /// <param name="folderPath">Assetsから始まるUnityプロジェクト相対フォルダです。</param>
        private static void EnsureAssetFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = $"{current}/{parts[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }

                current = next;
            }
        }
    }
}
