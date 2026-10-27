// このファイルは、41材質の制作入力をPlayerが直接参照しない製品用Sprite、表示カタログ、Sprite Atlasへ公開します。
// TerrainTileArtGenerationSettingsと境界合成Generatorを制作側の入力とし、ChunkTilemapPresenterが参照する完成Assetだけを製品側へ渡します。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Farmware.Simulation;
using Farmware.WorldGeneration.Authoring;
using Farmware.WorldGeneration.Presentation;
using UnityEditor;
using UnityEditor.Sprites;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 41材質の制作Spriteを16×16 pxの公開用Assetへ複製し、製品カタログとAtlasを原子的に更新します。
    /// SceneやRuntimeはSourceフォルダーと生成設定を参照せず、この処理が検証済みの完成Assetだけを参照します。
    /// </summary>
    public static class TerrainMaterialProductPublisher
    {
        /// <summary>41材質の制作入力を保持するEditor専用設定アセットです。</summary>
        public const string GenerationSettingsPath =
            "Assets/Farmware/Art/WorldGeneration/Terrain/Settings/TerrainTileArtGenerationSettings.asset";

        /// <summary>上面と通常断面の公開用単一Spriteを保存する製品フォルダーです。</summary>
        public const string PublishedSpriteRootFolder =
            "Assets/Farmware/Art/WorldGeneration/Terrain/Published/Materials";

        /// <summary>41材質を安定IDで引く製品表示カタログの保存先です。</summary>
        public const string CatalogPath =
            "Assets/Farmware/Art/WorldGeneration/Terrain/Published/TerrainMaterialPresentationCatalog.asset";

        /// <summary>公開済み上面・断面・境界合成結果をまとめる製品Atlasの保存先です。</summary>
        public const string AtlasPath =
            "Assets/Farmware/Art/WorldGeneration/Atlases/TerrainMaterialAtlas.spriteatlasv2";

        /// <summary>境界あり11材質がそれぞれ持つ境界Overlay候補数です。</summary>
        public const int ExpectedBoundaryVariantCount = 4;

        /// <summary>製品Atlasの最大辺長です。全41材質の16×16候補を1ページへ収める余裕を持たせます。</summary>
        public const int AtlasMaxTextureSize = 1024;

        /// <summary>Point Sample時に隣接Spriteの色がにじまないよう確保する余白です。</summary>
        public const int AtlasPaddingInPixels = 4;

        // 今回完成した64×48 px画像を正として確定した境界あり11材質です。
        // HashSetは公開前の集合比較だけに使い、Runtimeの選択順には影響させません。
        private static readonly HashSet<string> ExpectedBoundaryMaterialIds =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "GrassLoam",
                "MeadowSoil",
                "DryGrassSoil",
                "ForestLoam",
                "HumusSoil",
                "MountainLoam",
                "IslandLoam",
                "PodzolSoil",
                "SteppeSoil",
                "DryScrubSoil",
                "TundraSoil"
            };

        /// <summary>
        /// Projectメニューから41材質の境界合成、公開用Sprite、製品カタログ、Atlasをまとめて更新します。
        /// </summary>
        [MenuItem("Farmware/World Generation/Generation/Publish Terrain Material Assets")]
        public static void PublishFromMenu()
        {
            try
            {
                TerrainMaterialPresentationCatalog catalog = PublishAll();
                EditorUtility.DisplayDialog(
                    "地形材質を製品公開しました",
                    $"41材質の公開用Sprite、カタログ、Atlasを更新しました。\n{AssetDatabase.GetAssetPath(catalog)}",
                    "閉じる");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "地形材質を製品公開できません",
                    exception.Message,
                    "閉じる");
            }
        }

        /// <summary>
        /// 制作設定を完全検証し、境界合成結果を更新してから製品用Asset一式を再公開します。
        /// </summary>
        /// <returns>保存して再読込した41材質の製品表示カタログです。</returns>
        public static TerrainMaterialPresentationCatalog PublishAll()
        {
            TerrainTileArtGenerationSettings settings =
                AssetDatabase.LoadAssetAtPath<TerrainTileArtGenerationSettings>(
                    GenerationSettingsPath);
            if (settings == null)
            {
                throw new InvalidOperationException(
                    $"地形材質生成設定を読み込めません: {GenerationSettingsPath}");
            }

            TerrainTileArtValidationReport report = TerrainTileArtGenerator.ValidateAll(settings);
            if (report.HasErrors)
            {
                throw new InvalidOperationException(
                    "地形材質生成設定にエラーがあります。先にInspectorの検証結果を修正してください。\n"
                    + string.Join("\n", report.Errors));
            }

            if (settings.Materials.Length != TerrainBlockMaterialDefinitions.Count)
            {
                throw new InvalidOperationException(
                    $"製品公開には{TerrainBlockMaterialDefinitions.Count}材質が必要ですが、現在は{settings.Materials.Length}件です。");
            }

            ValidateBoundaryMaterialSet(settings.Materials);
            TerrainTileArtGenerator.GenerateAll(settings);
            EnsureFolderHierarchy(PublishedSpriteRootFolder);

            var definitions = new TerrainMaterialPresentationDefinition[
                TerrainBlockMaterialDefinitions.Count];
            var atlasTexturePaths = new List<string>(1024);
            var foundStableIds = new HashSet<TerrainBlockMaterialId>();
            for (int materialIndex = 0;
                 materialIndex < settings.Materials.Length;
                 materialIndex++)
            {
                TerrainMaterialArtSource material = settings.Materials[materialIndex];
                TerrainBlockMaterialId stableId = ParseStableId(material.MaterialId);
                if (!foundStableIds.Add(stableId))
                {
                    throw new InvalidOperationException(
                        $"製品公開対象の安定材質IDが重複しています: {stableId}");
                }

                // カタログの保存順も安定ID順へ固定し、設定Assetの並べ替えで差分やRuntime結果を変えません。
                int destinationIndex = (int)stableId - (int)TerrainBlockMaterialDefinitions.First;
                definitions[destinationIndex] = PublishMaterial(
                    material,
                    stableId,
                    settings.OutputRootFolder,
                    atlasTexturePaths);
            }

            TerrainMaterialPresentationCatalog catalog = CreateOrLoadCatalog();
            catalog.SetDefinitions(definitions);
            if (!catalog.TryInitialize(out string catalogError))
            {
                throw new InvalidOperationException(
                    $"公開直前の地形材質カタログ検証に失敗しました: {catalogError}");
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            GenerateOrUpdateAtlas(atlasTexturePaths);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            PackAndValidateAtlas(atlasTexturePaths);

            TerrainMaterialPresentationCatalog reloaded =
                AssetDatabase.LoadAssetAtPath<TerrainMaterialPresentationCatalog>(CatalogPath);
            if (reloaded == null || !reloaded.TryInitialize(out catalogError))
            {
                throw new InvalidOperationException(
                    $"保存後の地形材質カタログを再読込できません: {catalogError}");
            }

            Debug.Log(
                $"地形材質41件を製品公開しました。境界あり={ExpectedBoundaryMaterialIds.Count}, "
                + $"境界なし={TerrainBlockMaterialDefinitions.Count - ExpectedBoundaryMaterialIds.Count}, "
                + $"Atlas収録Texture={atlasTexturePaths.Count}");
            return reloaded;
        }

        /// <summary>今回の画像で確定した境界あり11材質の安定IDを昇順の複製配列として返します。</summary>
        /// <returns>呼び出し元が変更しても公開規則へ影響しない材質ID配列です。</returns>
        public static string[] GetExpectedBoundaryMaterialIds()
        {
            return ExpectedBoundaryMaterialIds
                .OrderBy(id => (int)ParseStableId(id))
                .ToArray();
        }

        /// <summary>1材質の通常候補を公開用PNGへ書き、境界合成結果を集めてカタログ行を作ります。</summary>
        /// <param name="material">制作設定に登録済みの1材質です。</param>
        /// <param name="stableId">Material IDと一致を確認済みのRuntime安定IDです。</param>
        /// <param name="generatedRootFolder">境界合成Generatorの出力ルートです。</param>
        /// <param name="atlasTexturePaths">Atlasへ追加するTextureパスの収集先です。</param>
        /// <returns>製品カタログへ保存できる完全な1材質定義です。</returns>
        private static TerrainMaterialPresentationDefinition PublishMaterial(
            TerrainMaterialArtSource material,
            TerrainBlockMaterialId stableId,
            string generatedRootFolder,
            List<string> atlasTexturePaths)
        {
            string materialFolder = $"{PublishedSpriteRootFolder}/{material.MaterialId}";
            EnsureFolderHierarchy(materialFolder);

            Sprite[] groundTopSprites = PublishSpriteCandidates(
                material.MaterialId,
                "GroundTop",
                material.GroundTopSprites,
                materialFolder,
                atlasTexturePaths);
            Sprite[] faceSprites = PublishSpriteCandidates(
                material.MaterialId,
                "Face",
                material.FaceSprites,
                materialFolder,
                atlasTexturePaths);

            int boundaryCount = material.FaceBoundaryOverlays.Length;
            Sprite[] groundBoundarySprites = LoadBoundarySprites(
                material,
                generatedRootFolder,
                forWall: false,
                atlasTexturePaths);
            Sprite[] wallBoundarySprites = material.DarkenWallSurface
                ? LoadBoundarySprites(
                    material,
                    generatedRootFolder,
                    forWall: true,
                    atlasTexturePaths)
                : Array.Empty<Sprite>();

            return new TerrainMaterialPresentationDefinition(
                stableId,
                groundTopSprites,
                faceSprites,
                boundaryCount,
                groundBoundarySprites,
                wallBoundarySprites,
                material.DarkenWallSurface,
                material.WallDarkenFactor);
        }

        /// <summary>制作元Sprite配列をSource外の単一Sprite PNGへ同じ順序で公開します。</summary>
        /// <param name="materialId">出力名へ使う安定材質名です。</param>
        /// <param name="partName">GroundTopまたはFaceの用途名です。</param>
        /// <param name="sourceSprites">制作元シート内の16×16候補です。</param>
        /// <param name="materialFolder">材質別の製品出力フォルダーです。</param>
        /// <param name="atlasTexturePaths">Atlasへ追加するパスの収集先です。</param>
        /// <returns>公開用単一Spriteを入力順で格納した配列です。</returns>
        private static Sprite[] PublishSpriteCandidates(
            string materialId,
            string partName,
            Sprite[] sourceSprites,
            string materialFolder,
            List<string> atlasTexturePaths)
        {
            var published = new Sprite[sourceSprites.Length];
            for (int index = 0; index < sourceSprites.Length; index++)
            {
                string path = $"{materialFolder}/{materialId}_{partName}_{index:D2}.png";
                WriteSingleSprite(path, ReadSpritePixels(sourceSprites[index]));
                published[index] = LoadRequiredSingleSprite(path);
                atlasTexturePaths.Add(path);
            }

            return published;
        }

        /// <summary>境界合成Generatorが作った完成SpriteをFace優先・境界優先の固定順で読みます。</summary>
        /// <param name="material">Face数と境界数を持つ材質入力です。</param>
        /// <param name="generatedRootFolder">境界完成PNGの生成ルートです。</param>
        /// <param name="forWall">暗色化済みWall用を読む場合はtrueです。</param>
        /// <param name="atlasTexturePaths">Atlasへ追加するパスの収集先です。</param>
        /// <returns>境界なしなら空、境界ありならFace数×境界数の完成Spriteです。</returns>
        private static Sprite[] LoadBoundarySprites(
            TerrainMaterialArtSource material,
            string generatedRootFolder,
            bool forWall,
            List<string> atlasTexturePaths)
        {
            int boundaryCount = material.FaceBoundaryOverlays.Length;
            if (boundaryCount == 0)
            {
                return Array.Empty<Sprite>();
            }

            var sprites = new Sprite[material.FaceSprites.Length * boundaryCount];
            int destinationIndex = 0;
            string usage = forWall ? "Wall" : "Ground";
            for (int faceIndex = 0; faceIndex < material.FaceSprites.Length; faceIndex++)
            {
                for (int boundaryIndex = 0; boundaryIndex < boundaryCount; boundaryIndex++)
                {
                    string path = $"{generatedRootFolder}/{material.MaterialId}/FaceBoundary/"
                        + $"{material.MaterialId}_{usage}Face_f{faceIndex:D2}_b{boundaryIndex:D2}.png";
                    sprites[destinationIndex++] = LoadRequiredSingleSprite(path);
                    atlasTexturePaths.Add(path);
                }
            }

            return sprites;
        }

        /// <summary>入力Spriteの矩形部分だけを左下原点の16×16画素配列として読み取ります。</summary>
        /// <param name="sprite">Read/Write有効として検証済みの制作元Spriteです。</param>
        /// <returns>Texture2Dへそのまま設定できる256画素です。</returns>
        private static Color32[] ReadSpritePixels(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null || !sprite.texture.isReadable)
            {
                throw new InvalidOperationException("公開元SpriteをCPUから読み取れません。");
            }

            Rect rect = sprite.rect;
            int x = Mathf.RoundToInt(rect.x);
            int y = Mathf.RoundToInt(rect.y);
            int size = TerrainTileArtGenerationSettings.TileSizeInPixels;
            Color32[] texturePixels = sprite.texture.GetPixels32();
            var result = new Color32[size * size];
            for (int row = 0; row < size; row++)
            {
                // 元の64×48シートから対象行だけをコピーし、透明な別区画を製品PNGへ含めません。
                Array.Copy(
                    texturePixels,
                    ((y + row) * sprite.texture.width) + x,
                    result,
                    row * size,
                    size);
            }

            return result;
        }

        /// <summary>16×16画素を同じパスへ上書きし、GUIDを維持した単一SpriteとしてImportします。</summary>
        /// <param name="assetPath">製品フォルダー配下のPNGパスです。</param>
        /// <param name="pixels">左下原点の256画素です。</param>
        private static void WriteSingleSprite(string assetPath, Color32[] pixels)
        {
            int size = TerrainTileArtGenerationSettings.TileSizeInPixels;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                string absolutePath = AssetPathToAbsolutePath(assetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
                File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"公開用SpriteのImporterを取得できません: {assetPath}");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = TerrainTileArtGenerator.PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spritePivot = new Vector2(0.5f, 0.5f);
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        /// <summary>指定PNGが単一の16×16 px・16 PPU Spriteとして読み込めることを確認します。</summary>
        /// <param name="assetPath">確認する製品PNGパスです。</param>
        /// <returns>カタログへ保存する単一Spriteです。</returns>
        private static Sprite LoadRequiredSingleSprite(string assetPath)
        {
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<Sprite>()
                .ToArray();
            if (sprites.Length != 1
                || !Mathf.Approximately(sprites[0].rect.width, 16f)
                || !Mathf.Approximately(sprites[0].rect.height, 16f)
                || !Mathf.Approximately(sprites[0].pixelsPerUnit, 16f))
            {
                throw new InvalidOperationException(
                    $"製品Spriteは単一の16×16 px・16 PPUである必要があります: {assetPath}");
            }

            return sprites[0];
        }

        /// <summary>Material IDを同名の1〜41安定IDへ変換し、別名や予約値を拒否します。</summary>
        /// <param name="materialId">制作設定の英語安定IDです。</param>
        /// <returns>同名の有効な地形材質安定IDです。</returns>
        private static TerrainBlockMaterialId ParseStableId(string materialId)
        {
            if (!Enum.TryParse(materialId, ignoreCase: false, out TerrainBlockMaterialId stableId)
                || stableId < TerrainBlockMaterialDefinitions.First
                || stableId > TerrainBlockMaterialDefinitions.Last
                || !string.Equals(stableId.ToString(), materialId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Material IDを1〜41の同名安定IDへ変換できません: {materialId}");
            }

            return stableId;
        }

        /// <summary>設定の境界あり集合が今回の画像で確定した11材質と完全一致することを検証します。</summary>
        /// <param name="materials">41材質の制作設定です。</param>
        private static void ValidateBoundaryMaterialSet(TerrainMaterialArtSource[] materials)
        {
            var actual = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < materials.Length; index++)
            {
                TerrainMaterialArtSource material = materials[index];
                int boundaryCount = material.FaceBoundaryOverlays?.Length ?? 0;
                if (boundaryCount == 0)
                {
                    continue;
                }

                if (boundaryCount != ExpectedBoundaryVariantCount)
                {
                    throw new InvalidOperationException(
                        $"{material.MaterialId} の境界候補は{ExpectedBoundaryVariantCount}件必要ですが、現在は{boundaryCount}件です。");
                }

                actual.Add(material.MaterialId);
            }

            if (!actual.SetEquals(ExpectedBoundaryMaterialIds))
            {
                string missing = string.Join(", ", ExpectedBoundaryMaterialIds.Except(actual));
                string unexpected = string.Join(", ", actual.Except(ExpectedBoundaryMaterialIds));
                throw new InvalidOperationException(
                    "境界あり材質が今回の画像で確定した11材質と一致しません。"
                    + $" 不足=[{missing}]、余分=[{unexpected}]");
            }
        }

        /// <summary>製品カタログを既存GUIDのまま読み込み、未作成なら新規作成します。</summary>
        /// <returns>更新対象の製品表示カタログです。</returns>
        private static TerrainMaterialPresentationCatalog CreateOrLoadCatalog()
        {
            TerrainMaterialPresentationCatalog catalog =
                AssetDatabase.LoadAssetAtPath<TerrainMaterialPresentationCatalog>(CatalogPath);
            if (catalog != null)
            {
                return catalog;
            }

            EnsureFolderHierarchy(Path.GetDirectoryName(CatalogPath).Replace('\\', '/'));
            catalog = ScriptableObject.CreateInstance<TerrainMaterialPresentationCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            return catalog;
        }

        /// <summary>公開した全TextureだけをPackableにするSprite Atlas V2を作成または更新します。</summary>
        /// <param name="texturePaths">重複のない製品Sprite Textureパス一覧です。</param>
        private static void GenerateOrUpdateAtlas(IReadOnlyList<string> texturePaths)
        {
            EditorSettings.spritePackerMode = SpritePackerMode.SpriteAtlasV2;
            EnsureFolderHierarchy(Path.GetDirectoryName(AtlasPath).Replace('\\', '/'));
            UnityEngine.Object[] textures = texturePaths
                .Distinct(StringComparer.Ordinal)
                .Select(path => AssetDatabase.LoadAssetAtPath<Texture2D>(path))
                .ToArray();
            if (textures.Any(texture => texture == null))
            {
                throw new InvalidOperationException("製品Atlasへ登録するTextureの一部を読み込めません。");
            }

            SpriteAtlasAsset atlasAsset;
            if (AssetDatabase.LoadMainAssetAtPath(AtlasPath) == null)
            {
                atlasAsset = new SpriteAtlasAsset();
            }
            else
            {
                atlasAsset = SpriteAtlasAsset.Load(AtlasPath);
                SerializedObject serializedAtlas = new SerializedObject(atlasAsset);
                SerializedProperty packables = serializedAtlas.FindProperty("m_ImporterData.packables");
                if (packables == null || !packables.isArray)
                {
                    throw new InvalidOperationException($"AtlasのPackable配列を取得できません: {AtlasPath}");
                }

                // 以前の候補数を減らした場合もMissing参照を残さないよう、毎回一覧を明示的に再構築します。
                packables.ClearArray();
                serializedAtlas.ApplyModifiedPropertiesWithoutUndo();
            }

            atlasAsset.Add(textures);
            SpriteAtlasAsset.Save(atlasAsset, AtlasPath);
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceSynchronousImport);

            SpriteAtlasImporter importer = AssetImporter.GetAtPath(AtlasPath) as SpriteAtlasImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"Sprite Atlas V2 Importerを取得できません: {AtlasPath}");
            }

            importer.includeInBuild = true;
            SpriteAtlasTextureSettings textureSettings = importer.textureSettings;
            textureSettings.filterMode = FilterMode.Point;
            textureSettings.generateMipMaps = false;
            textureSettings.readable = false;
            textureSettings.anisoLevel = 0;
            importer.textureSettings = textureSettings;

            SpriteAtlasPackingSettings packingSettings = importer.packingSettings;
            packingSettings.padding = AtlasPaddingInPixels;
            packingSettings.enableRotation = false;
            packingSettings.enableTightPacking = false;
            packingSettings.enableAlphaDilation = false;
            importer.packingSettings = packingSettings;

            TextureImporterPlatformSettings platform =
                importer.GetPlatformSettings("DefaultTexturePlatform");
            platform.name = "DefaultTexturePlatform";
            platform.maxTextureSize = AtlasMaxTextureSize;
            platform.format = TextureImporterFormat.RGBA32;
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformSettings(platform);
            importer.SaveAndReimport();
        }

        /// <summary>Atlasを現在のBuildTarget向けにパックし、全Spriteが1ページを共有することを確認します。</summary>
        /// <param name="texturePaths">Atlasへ登録した製品Textureパス一覧です。</param>
        private static void PackAndValidateAtlas(IReadOnlyList<string> texturePaths)
        {
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            if (atlas == null)
            {
                throw new InvalidOperationException($"Runtime SpriteAtlasを読み込めません: {AtlasPath}");
            }

            SpriteAtlasUtility.PackAtlases(
                new[] { atlas },
                EditorUserBuildSettings.activeBuildTarget,
                canCancel: false);

            Texture2D packedTexture = null;
            foreach (string path in texturePaths.Distinct(StringComparer.Ordinal))
            {
                Sprite sprite = LoadRequiredSingleSprite(path);
                Texture2D current = SpriteUtility.GetSpriteTexture(sprite, getAtlasData: true);
                if (!sprite.packed || current == null)
                {
                    throw new InvalidOperationException($"製品SpriteがAtlasへパックされていません: {path}");
                }

                if (packedTexture == null)
                {
                    packedTexture = current;
                }
                else if (current != packedTexture)
                {
                    throw new InvalidOperationException(
                        $"地形材質Atlasが複数ページへ分割されました。最大サイズまたは候補数を見直してください: {path}");
                }
            }
        }

        /// <summary>Assets配下の相対パスを、現在のUnityプロジェクト内の絶対パスへ変換します。</summary>
        /// <param name="assetPath">Assetsから始まるAssetDatabaseパスです。</param>
        /// <returns>OSでPNGを書き込める絶対パスです。</returns>
        private static string AssetPathToAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        /// <summary>Assetsから始まるフォルダー階層を上から順に作り、Unity管理のmetaを維持します。</summary>
        /// <param name="folderPath">作成または確認するUnityプロジェクト相対フォルダーです。</param>
        private static void EnsureFolderHierarchy(string folderPath)
        {
            string normalized = (folderPath ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(normalized)
                || (!normalized.Equals("Assets", StringComparison.Ordinal)
                    && !normalized.StartsWith("Assets/", StringComparison.Ordinal)))
            {
                throw new ArgumentException($"Assets配下のフォルダーを指定してください: {folderPath}");
            }

            string[] segments = normalized.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = $"{current}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }
        }
    }
}
