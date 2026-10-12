// このファイルは、仮色の地形Tile・資源アイコンと、マップ生成を確認できるSampleScene構成をUnity Editor APIで再現可能に作ります。
// Runtimeの生成規則には関与せず、フォールバック表示素材、SubScene、Camera、Grid、TilemapのAuthoring接続だけを担当します。

using System;
using System.IO;
using Farmware.WorldGeneration.Authoring;
using Farmware.WorldGeneration.Presentation;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// フォールバック地形Tileと資源アイコンを生成し、SampleSceneと設定用SubSceneを安全に構築するEditor専用ツールです。
    /// メニューまたはバッチモードのexecuteMethodから呼び出し、同じ名前の構成は再利用して重複作成を防ぎます。
    /// </summary>
    public static class WorldGenerationSceneBootstrap
    {
        /// <summary>表示用フォールバック素材をまとめるUnityプロジェクト相対フォルダです。</summary>
        private const string FallbackAssetFolder = "Assets/Farmware/Art/WorldGeneration/Fallback";

        /// <summary>Presentation用GameObjectを配置する既存の確認Sceneです。</summary>
        private const string MainScenePath = "Assets/Scenes/SampleScene.unity";

        /// <summary>BakerがWorldGenerationSettings Entityへ変換するAuthoring専用SubSceneです。</summary>
        private const string SettingsSubScenePath = "Assets/Scenes/WorldGenerationSettingsSubScene.unity";

        /// <summary>フォールバック画像の1辺のピクセル数で、全Tileを偶数寸法へ統一します。</summary>
        private const int FallbackTilePixelSize = 16;

        /// <summary>SpriteのPixels Per Unitです。16px画像を1×1ワールドセルとして表示します。</summary>
        private const int AssetsPixelsPerUnit = 16;

        /// <summary>Pixel Perfect Cameraが基準にする水平方向の解像度です。</summary>
        private const int ReferenceResolutionX = 480;

        /// <summary>Pixel Perfect Cameraが基準にする垂直方向の解像度です。</summary>
        private const int ReferenceResolutionY = 270;

        /// <summary>正式なセーブ互換運用を始めるまで固定する初期生成器バージョンです。</summary>
        private const int PrototypeGeneratorVersion = 1;

        /// <summary>約1.5論理チャンク以内で気温帯が変化する試作格子サイズです。単位はタイルです。</summary>
        private const int PrototypeTemperatureCellSizeInTiles = 96;

        /// <summary>約1.25論理チャンク以内で湿度帯が変化する試作格子サイズです。単位はタイルです。</summary>
        private const int PrototypeMoistureCellSizeInTiles = 80;

        /// <summary>隣接バイオームの地面を段階的に混ぜる試作幅です。単位はタイルです。</summary>
        private const int PrototypeBiomeTransitionWidthInTiles = 6;

        /// <summary>川の大きな蛇行を決める試作ノイズ格子サイズです。単位はタイルです。</summary>
        private const int PrototypeRiverCellSizeInTiles = 96;

        /// <summary>内陸の池をまとめる試作ノイズ格子サイズです。単位はタイルです。</summary>
        private const int PrototypePondCellSizeInTiles = 48;

        /// <summary>川と池の輪郭へ重ねる試作ノイズ解像度数です。</summary>
        private const int PrototypeFreshWaterOctaveCount = 2;

        /// <summary>川の中心線から許容する固定小数点差をInspector表現へ戻した値です。</summary>
        private const float PrototypeRiverCenterLineHalfWidth = 600f / ushort.MaxValue;

        /// <summary>池を生成する固定小数点下限をInspector表現へ戻した値です。</summary>
        private const float PrototypePondThreshold = 57000f / ushort.MaxValue;

        /// <summary>淡水を生成できる固定小数点標高上限をInspector表現へ戻した値です。</summary>
        private const float PrototypeFreshWaterMaximumElevation = 47000f / ushort.MaxValue;

        /// <summary>岩壁の大きな塊を作る試作ノイズ格子サイズです。単位はタイルです。</summary>
        private const int PrototypeRockWallMassCellSizeInTiles = 24;

        /// <summary>岩壁輪郭の凹凸を作る試作ノイズ格子サイズです。単位はタイルです。</summary>
        private const int PrototypeRockWallDetailCellSizeInTiles = 8;

        /// <summary>岩壁密度のサンプリング座標を歪ませる最大幅です。単位はタイルです。</summary>
        private const int PrototypeRockWallWarpAmplitudeInTiles = 6;

        /// <summary>岩壁へ分類する固定小数点密度下限をInspector表現へ戻した値です。</summary>
        private const float PrototypeRockWallNoiseThreshold = 41000f / ushort.MaxValue;

        /// <summary>岩壁から削る通路ノイズ帯の半幅をInspector表現へ戻した値です。</summary>
        private const float PrototypeRockWallCorridorHalfWidth = 1100f / ushort.MaxValue;

        /// <summary>基礎資源を点ではなく群れとして配置する試作ノイズ格子サイズです。単位はタイルです。</summary>
        private const int PrototypeResourceClusterCellSizeInTiles = 24;

        /// <summary>資源群の輪郭へ重ねるノイズ解像度数です。細かな散在を抑えるため地形より少なくします。</summary>
        private const int PrototypeResourceOctaveCount = 2;

        /// <summary>樹木資源を配置する固定小数点下限をInspectorの0〜1表現へ戻した値です。</summary>
        private const float PrototypeTreeResourceThreshold = 50000f / ushort.MaxValue;

        /// <summary>石資源を配置する固定小数点下限をInspectorの0〜1表現へ戻した値です。</summary>
        private const float PrototypeStoneResourceThreshold = 55500f / ushort.MaxValue;

        /// <summary>砂資源を配置する固定小数点下限をInspectorの0〜1表現へ戻した値です。</summary>
        private const float PrototypeSandResourceThreshold = 48000f / ushort.MaxValue;

        /// <summary>樹木群の中心同士へ確保する試作最低間隔です。単位はタイルです。</summary>
        private const int PrototypeTreeResourceMinimumSpacingInTiles = 14;

        /// <summary>石群の中心同士へ確保する試作最低間隔です。単位はタイルです。</summary>
        private const int PrototypeStoneResourceMinimumSpacingInTiles = 20;

        /// <summary>砂資源群の中心同士へ確保する試作最低間隔です。単位はタイルです。</summary>
        private const int PrototypeSandResourceMinimumSpacingInTiles = 18;

        /// <summary>樹木群の試作最大半径です。単位はタイルです。</summary>
        private const int PrototypeTreeResourceClusterRadiusInTiles = 5;

        /// <summary>石群の試作最大半径です。単位はタイルです。</summary>
        private const int PrototypeStoneResourceClusterRadiusInTiles = 4;

        /// <summary>砂資源群の試作最大半径です。単位はタイルです。</summary>
        private const int PrototypeSandResourceClusterRadiusInTiles = 6;

        /// <summary>
        /// Unityメニューからフォールバック表示付きマップ生成Sceneを構築します。
        /// </summary>
        [MenuItem("Farmware/World Generation/Build Fallback Map Prototype")]
        public static void BuildFromMenu()
        {
            BuildFallbackMapPrototype();
        }

        /// <summary>
        /// バッチモードから呼び出す入口です。アセット生成、SubScene作成、SampleScene接続、検証を順番に実行します。
        /// </summary>
        public static void Execute()
        {
            BuildFallbackMapPrototype();
        }

        /// <summary>
        /// 必要な全アセットとScene構成を冪等に作り、保存後の参照が欠けていないことを検証します。
        /// </summary>
        private static void BuildFallbackMapPrototype()
        {
            ValidateUniversalRenderPipeline();
            EnsureFolderHierarchy(FallbackAssetFolder);

            CreateOrUpdateFallbackTile("Grassland", new Color32(112, 169, 86, 255));
            CreateOrUpdateFallbackTile("Forest", new Color32(61, 112, 66, 255));
            CreateOrUpdateFallbackTile("Desert", new Color32(210, 177, 100, 255));
            CreateOrUpdateFallbackTile("FreshWater", new Color32(76, 157, 211, 255));
            CreateOrUpdateFallbackTile("SaltWater", new Color32(42, 101, 176, 255));
            CreateOrUpdateFallbackTile("RockWall", new Color32(101, 101, 109, 255));
            CreateOrUpdateFallbackResourceTile("TreeResource", GeneratedResourceType.Tree);
            CreateOrUpdateFallbackResourceTile("StoneResource", GeneratedResourceType.Stone);
            CreateOrUpdateFallbackResourceTile("SandResource", GeneratedResourceType.Sand);

            // 水は地形そのものを採水源に変更したため、旧水域Tileと水資源アイコンを再生成対象から除外します。
            DeleteObsoleteFallbackTile("Water");
            DeleteObsoleteFallbackTile("WaterResource");
            DeleteObsoleteFallbackTile("RockWallInterior");

            // PNGのSaveAndReimport中に以前読み込んだUnity Object参照が無効化される場合があります。
            // 9種類をすべて生成した後、AssetDatabaseから最新のTileを読み直してSceneへ保存します。
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Tile grasslandTile = LoadRequiredFallbackTile("Grassland");
            Tile forestTile = LoadRequiredFallbackTile("Forest");
            Tile desertTile = LoadRequiredFallbackTile("Desert");
            Tile freshWaterTile = LoadRequiredFallbackTile("FreshWater");
            Tile saltWaterTile = LoadRequiredFallbackTile("SaltWater");
            Tile rockWallTile = LoadRequiredFallbackTile("RockWall");
            Tile treeResourceTile = LoadRequiredFallbackTile("TreeResource");
            Tile stoneResourceTile = LoadRequiredFallbackTile("StoneResource");
            Tile sandResourceTile = LoadRequiredFallbackTile("SandResource");

            SceneAsset settingsSubSceneAsset = CreateOrUpdateSettingsSubScene();
            ConfigureMainScene(
                settingsSubSceneAsset,
                grasslandTile,
                forestTile,
                desertTile,
                freshWaterTile,
                saltWaterTile,
                rockWallTile,
                treeResourceTile,
                stoneResourceTile,
                sandResourceTile);
            ConfigurePixelArtQualitySettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateSavedPrototype();
            Debug.Log("Farmwareのフォールバック地形Tileとマップ生成用SampleSceneを構築しました。");
        }

        /// <summary>
        /// 現在のRender PipelineがURPであることを型名から確認し、誤ったPixel Perfect実装の追加を防ぎます。
        /// </summary>
        private static void ValidateUniversalRenderPipeline()
        {
            RenderPipelineAsset pipelineAsset = GraphicsSettings.currentRenderPipeline;
            string pipelineTypeName = pipelineAsset == null
                ? string.Empty
                : pipelineAsset.GetType().FullName ?? string.Empty;

            // PixelPerfectCameraにはBuilt-in用とURP用があるため、Component追加前に必ずPipelineを判別します。
            if (pipelineAsset == null || !pipelineTypeName.Contains("Universal", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "FarmwareはURP用PixelPerfectCameraを前提としていますが、現在のRender PipelineがURPではありません。");
            }
        }

        /// <summary>
        /// Unityプロジェクト相対パスの各フォルダを、AssetDatabaseへ認識される形で不足分だけ作ります。
        /// </summary>
        /// <param name="assetFolderPath">Assetsから始まる作成対象フォルダです。</param>
        private static void EnsureFolderHierarchy(string assetFolderPath)
        {
            string[] pathParts = assetFolderPath.Split('/');
            string currentPath = pathParts[0];

            // AssetDatabase.CreateFolderは親が存在する必要があるため、浅い階層から1つずつ作ります。
            for (int index = 1; index < pathParts.Length; index++)
            {
                string nextPath = currentPath + "/" + pathParts[index];
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, pathParts[index]);
                }

                currentPath = nextPath;
            }
        }

        /// <summary>
        /// 旧仕様で生成したPNGとTileをAssetDatabase経由で削除します。
        /// GUID参照もUnityに通知し、ファイルだけを消してSceneへ壊れた参照を残さないための移行処理です。
        /// </summary>
        /// <param name="assetName">拡張子を除いた旧フォールバック素材名です。</param>
        private static void DeleteObsoleteFallbackTile(string assetName)
        {
            string tileAssetPath = $"{FallbackAssetFolder}/{assetName}.asset";
            string textureAssetPath = $"{FallbackAssetFolder}/{assetName}.png";

            // DeleteAssetは対象が存在しない場合にfalseを返すだけなので、構築処理を何度実行しても同じ結果になります。
            AssetDatabase.DeleteAsset(tileAssetPath);
            AssetDatabase.DeleteAsset(textureAssetPath);
        }

        /// <summary>
        /// 指定色の16×16 PNGを作成・更新し、Point・MipMapなし・無圧縮・PPU 16のSpriteとTileへ変換します。
        /// </summary>
        /// <param name="assetName">PNGとTileアセットに使用する英語名です。</param>
        /// <param name="color">全ピクセルへ設定する不透明色です。</param>
        /// <returns>作成または更新された表示専用Tileアセットです。</returns>
        private static Tile CreateOrUpdateFallbackTile(string assetName, Color32 color)
        {
            Color32[] pixels = new Color32[FallbackTilePixelSize * FallbackTilePixelSize];

            // 単色の仮素材に限定し、ノイズや模様を入れないことで本番アートとの差し替え箇所を明確にします。
            for (int index = 0; index < pixels.Length; index++)
            {
                pixels[index] = color;
            }

            return CreateOrUpdateFallbackTileAsset(assetName, pixels, hasTransparency: false);
        }

        /// <summary>
        /// 木・石・砂を見分けられる小さな透明背景アイコンを16×16 PNGとTileへ変換します。
        /// 本番Spriteではなく、資源分布とチャンク境界をGameビューで検証するための仮表示です。
        /// </summary>
        /// <param name="assetName">PNGとTileアセットに使用する英語名です。</param>
        /// <param name="resourceType">形と色を決める木・石・砂の基礎資源IDです。</param>
        /// <returns>作成または更新された資源表示専用Tileアセットです。</returns>
        private static Tile CreateOrUpdateFallbackResourceTile(
            string assetName,
            GeneratedResourceType resourceType)
        {
            Color32[] pixels = new Color32[FallbackTilePixelSize * FallbackTilePixelSize];

            // 透明セルを残すことで、下層の地形色を隠さず資源の存在だけを重ねて確認できます。
            for (int y = 0; y < FallbackTilePixelSize; y++)
            {
                for (int x = 0; x < FallbackTilePixelSize; x++)
                {
                    int index = (y * FallbackTilePixelSize) + x;
                    pixels[index] = GetFallbackResourcePixel(resourceType, x, y);
                }
            }

            return CreateOrUpdateFallbackTileAsset(assetName, pixels, hasTransparency: true);
        }

        /// <summary>
        /// 資源種類と16×16内座標から、仮アイコンの不透明色または透明色を返します。
        /// </summary>
        /// <param name="resourceType">描画する木、石、砂の種類です。</param>
        /// <param name="x">画像左端からの0〜15の水平ピクセル座標です。</param>
        /// <param name="y">画像下端からの0〜15の垂直ピクセル座標です。</param>
        /// <returns>単純形状の色です。形の外側は完全透明です。</returns>
        private static Color32 GetFallbackResourcePixel(
            GeneratedResourceType resourceType,
            int x,
            int y)
        {
            Color32 transparent = new Color32(0, 0, 0, 0);
            int offsetX = (x * 2) - 15;
            int offsetY = (y * 2) - 15;

            switch (resourceType)
            {
                case GeneratedResourceType.Tree:
                    // 下側の2px幅の幹と、上側の円形樹冠を組み合わせて遠目でも木と分かる形にします。
                    bool isTrunk = x >= 7 && x <= 8 && y >= 2 && y <= 7;
                    int canopyX = offsetX;
                    int canopyY = (y * 2) - 20;
                    bool isCanopy = (canopyX * canopyX) + (canopyY * canopyY) <= 45;
                    if (isTrunk)
                    {
                        return new Color32(112, 72, 42, 255);
                    }

                    return isCanopy ? new Color32(45, 158, 70, 255) : transparent;

                case GeneratedResourceType.Stone:
                    // 横長の楕円で岩塊を表し、完全な正方形より地形Tileとの差が読みやすくなるようにします。
                    bool isStone = (offsetX * offsetX * 3) + (offsetY * offsetY * 5) <= 245;
                    return isStone ? new Color32(185, 190, 199, 255) : transparent;

                case GeneratedResourceType.Sand:
                    // ひし形の砂山を暖色で描き、砂地の全面色と区別します。
                    bool isSandDeposit = Math.Abs(offsetX) + Math.Abs(offsetY) <= 9;
                    return isSandDeposit ? new Color32(255, 220, 92, 255) : transparent;

                default:
                    return transparent;
            }
        }

        /// <summary>
        /// 16×16ピクセル配列をPNGへ保存し、共通のPoint・MipMapなし・無圧縮設定でSpriteとTileへ変換します。
        /// </summary>
        /// <param name="assetName">PNGとTileアセットに使用する英語名です。</param>
        /// <param name="pixels">行優先で256件のRGBAピクセルです。</param>
        /// <param name="hasTransparency">透明背景としてImporterへ通知する場合はtrueです。</param>
        /// <returns>作成または更新された表示専用Tileアセットです。</returns>
        private static Tile CreateOrUpdateFallbackTileAsset(
            string assetName,
            Color32[] pixels,
            bool hasTransparency)
        {
            string textureAssetPath = $"{FallbackAssetFolder}/{assetName}.png";
            string tileAssetPath = $"{FallbackAssetFolder}/{assetName}.asset";
            string textureAbsolutePath = Path.GetFullPath(textureAssetPath);

            Texture2D texture = new Texture2D(
                FallbackTilePixelSize,
                FallbackTilePixelSize,
                TextureFormat.RGBA32,
                mipChain: false,
                linear: false);

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            File.WriteAllBytes(textureAbsolutePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(textureAssetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(textureAssetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"TextureImporterを取得できませんでした: {textureAssetPath}");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = AssetsPixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = hasTransparency;
            importer.wrapMode = TextureWrapMode.Clamp;

            // Unity 6ではSpriteのAlignmentとPivotをTextureImporterSettings経由で設定します。
            // 16×16の偶数寸法と中央Pivotを組み合わせ、0.5pxずれが起きない配置に固定します。
            TextureImporterSettings textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            textureSettings.spriteAlignment = (int)SpriteAlignment.Center;
            textureSettings.spritePivot = new Vector2(0.5f, 0.5f);
            importer.SetTextureSettings(textureSettings);
            importer.SaveAndReimport();

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(textureAssetPath);
            if (sprite == null)
            {
                throw new InvalidOperationException($"Spriteを読み込めませんでした: {textureAssetPath}");
            }

            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(tileAssetPath);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, tileAssetPath);
            }

            tile.sprite = sprite;
            tile.color = Color.white;
            tile.transform = Matrix4x4.identity;
            tile.colliderType = Tile.ColliderType.None;
            tile.flags = TileFlags.LockTransform;
            EditorUtility.SetDirty(tile);
            return tile;
        }

        /// <summary>
        /// 全Textureの再インポート後に、指定名のフォールバックTileをAssetDatabaseから読み直します。
        /// </summary>
        /// <param name="assetName">読み込むTileアセットの拡張子を除いた名前です。</param>
        /// <returns>有効なSprite参照を持つTileアセットです。</returns>
        private static Tile LoadRequiredFallbackTile(string assetName)
        {
            string tileAssetPath = $"{FallbackAssetFolder}/{assetName}.asset";
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(tileAssetPath);
            if (tile == null || tile.sprite == null)
            {
                throw new InvalidOperationException($"フォールバックTileを再読込できませんでした: {tileAssetPath}");
            }

            return tile;
        }

        /// <summary>
        /// WorldGenerationAuthoringだけを含むSubSceneを作成または更新し、Bakerが設定Singletonを生成できるようにします。
        /// </summary>
        /// <returns>SampleSceneのSubScene Componentへ割り当てるSceneAssetです。</returns>
        private static SceneAsset CreateOrUpdateSettingsSubScene()
        {
            Scene settingsScene;
            SceneAsset existingSceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(SettingsSubScenePath);
            if (existingSceneAsset == null)
            {
                settingsScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            else
            {
                settingsScene = EditorSceneManager.OpenScene(SettingsSubScenePath, OpenSceneMode.Single);
            }

            GameObject settingsObject = FindOrCreateRoot(settingsScene, "World Generation Settings");
            WorldGenerationAuthoring authoring = GetOrAddComponent<WorldGenerationAuthoring>(settingsObject);
            ConfigurePrototypeWorldGenerationSettings(authoring);

            EditorSceneManager.MarkSceneDirty(settingsScene);
            if (!EditorSceneManager.SaveScene(settingsScene, SettingsSubScenePath))
            {
                throw new InvalidOperationException($"設定SubSceneを保存できませんでした: {SettingsSubScenePath}");
            }

            AssetDatabase.ImportAsset(SettingsSubScenePath, ImportAssetOptions.ForceSynchronousImport);
            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(SettingsSubScenePath);
            if (sceneAsset == null)
            {
                throw new InvalidOperationException($"設定SubSceneを読み込めませんでした: {SettingsSubScenePath}");
            }

            return sceneAsset;
        }

        /// <summary>
        /// 既存SubSceneにも現在の試作既定値を反映し、生成器バージョンと気候スケールの食い違いを防ぎます。
        /// </summary>
        /// <param name="authoring">設定SubSceneに1つだけ置くWorldGenerationAuthoringです。</param>
        private static void ConfigurePrototypeWorldGenerationSettings(WorldGenerationAuthoring authoring)
        {
            SerializedObject serializedAuthoring = new SerializedObject(authoring);

            // privateなSerializeFieldは通常の実行時APIとして公開せず、構築ツールだけがSerializedProperty経由で更新します。
            serializedAuthoring.FindProperty("generatorVersion").intValue = PrototypeGeneratorVersion;
            serializedAuthoring.FindProperty("temperatureCellSizeInTiles").intValue =
                PrototypeTemperatureCellSizeInTiles;
            serializedAuthoring.FindProperty("moistureCellSizeInTiles").intValue =
                PrototypeMoistureCellSizeInTiles;
            serializedAuthoring.FindProperty("biomeTransitionWidthInTiles").intValue =
                PrototypeBiomeTransitionWidthInTiles;
            serializedAuthoring.FindProperty("riverCellSizeInTiles").intValue =
                PrototypeRiverCellSizeInTiles;
            serializedAuthoring.FindProperty("pondCellSizeInTiles").intValue =
                PrototypePondCellSizeInTiles;
            serializedAuthoring.FindProperty("freshWaterOctaveCount").intValue =
                PrototypeFreshWaterOctaveCount;
            serializedAuthoring.FindProperty("riverCenterLineHalfWidth").floatValue =
                PrototypeRiverCenterLineHalfWidth;
            serializedAuthoring.FindProperty("pondThreshold").floatValue =
                PrototypePondThreshold;
            serializedAuthoring.FindProperty("freshWaterMaximumElevation").floatValue =
                PrototypeFreshWaterMaximumElevation;
            serializedAuthoring.FindProperty("rockWallMassCellSizeInTiles").intValue =
                PrototypeRockWallMassCellSizeInTiles;
            serializedAuthoring.FindProperty("rockWallDetailCellSizeInTiles").intValue =
                PrototypeRockWallDetailCellSizeInTiles;
            serializedAuthoring.FindProperty("rockWallWarpAmplitudeInTiles").intValue =
                PrototypeRockWallWarpAmplitudeInTiles;
            serializedAuthoring.FindProperty("rockWallNoiseThreshold").floatValue =
                PrototypeRockWallNoiseThreshold;
            serializedAuthoring.FindProperty("rockWallCorridorHalfWidth").floatValue =
                PrototypeRockWallCorridorHalfWidth;
            serializedAuthoring.FindProperty("resourceClusterCellSizeInTiles").intValue =
                PrototypeResourceClusterCellSizeInTiles;
            serializedAuthoring.FindProperty("resourceOctaveCount").intValue =
                PrototypeResourceOctaveCount;
            serializedAuthoring.FindProperty("treeResourceThreshold").floatValue =
                PrototypeTreeResourceThreshold;
            serializedAuthoring.FindProperty("stoneResourceThreshold").floatValue =
                PrototypeStoneResourceThreshold;
            serializedAuthoring.FindProperty("sandResourceThreshold").floatValue =
                PrototypeSandResourceThreshold;
            serializedAuthoring.FindProperty("treeResourceMinimumSpacingInTiles").intValue =
                PrototypeTreeResourceMinimumSpacingInTiles;
            serializedAuthoring.FindProperty("stoneResourceMinimumSpacingInTiles").intValue =
                PrototypeStoneResourceMinimumSpacingInTiles;
            serializedAuthoring.FindProperty("sandResourceMinimumSpacingInTiles").intValue =
                PrototypeSandResourceMinimumSpacingInTiles;
            serializedAuthoring.FindProperty("treeResourceClusterRadiusInTiles").intValue =
                PrototypeTreeResourceClusterRadiusInTiles;
            serializedAuthoring.FindProperty("stoneResourceClusterRadiusInTiles").intValue =
                PrototypeStoneResourceClusterRadiusInTiles;
            serializedAuthoring.FindProperty("sandResourceClusterRadiusInTiles").intValue =
                PrototypeSandResourceClusterRadiusInTiles;
            serializedAuthoring.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(authoring);
        }

        /// <summary>
        /// SampleSceneへSubScene、Grid、Tilemap、Presenter、Camera Bridge、プレイヤー診断表示、開発者可視化、Pixel Perfect Cameraを接続して保存します。
        /// </summary>
        /// <param name="settingsSubSceneAsset">生成設定をBakeするSubSceneアセットです。</param>
        /// <param name="grasslandTile">草原用の単色Tileです。</param>
        /// <param name="forestTile">森林用の単色Tileです。</param>
        /// <param name="desertTile">砂漠用の単色Tileです。</param>
        /// <param name="freshWaterTile">川と池の淡水域用の単色Tileです。</param>
        /// <param name="saltWaterTile">海の海水域用の単色Tileです。</param>
        /// <param name="rockWallTile">全岩壁へ共通で使用し、別の影Meshで内部グラデーションを後付けする単色Tileです。</param>
        /// <param name="treeResourceTile">樹木資源用の透明背景Tileです。</param>
        /// <param name="stoneResourceTile">石資源用の透明背景Tileです。</param>
        /// <param name="sandResourceTile">砂資源用の透明背景Tileです。</param>
        private static void ConfigureMainScene(
            SceneAsset settingsSubSceneAsset,
            TileBase grasslandTile,
            TileBase forestTile,
            TileBase desertTile,
            TileBase freshWaterTile,
            TileBase saltWaterTile,
            TileBase rockWallTile,
            TileBase treeResourceTile,
            TileBase stoneResourceTile,
            TileBase sandResourceTile)
        {
            Scene mainScene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            GameObject subSceneObject = FindOrCreateRoot(mainScene, "World Generation SubScene");
            SubScene subScene = GetOrAddComponent<SubScene>(subSceneObject);
            subScene.SceneAsset = settingsSubSceneAsset;
            subScene.AutoLoadScene = true;

            GameObject gridObject = FindOrCreateRoot(mainScene, "World Grid");
            Grid grid = GetOrAddComponent<Grid>(gridObject);
            grid.cellSize = Vector3.one;
            grid.cellGap = Vector3.zero;
            grid.cellLayout = GridLayout.CellLayout.Rectangle;
            grid.cellSwizzle = GridLayout.CellSwizzle.XYZ;

            GameObject tilemapObject = FindOrCreateDirectChild(gridObject.transform, "Ground Tilemap");
            Tilemap tilemap = GetOrAddComponent<Tilemap>(tilemapObject);
            TilemapRenderer tilemapRenderer = GetOrAddComponent<TilemapRenderer>(tilemapObject);
            tilemapRenderer.mode = TilemapRenderer.Mode.Chunk;
            tilemapRenderer.sortOrder = TilemapRenderer.SortOrder.BottomLeft;
            tilemapRenderer.sortingOrder = 0;
            ChunkTilemapPresenter presenter = GetOrAddComponent<ChunkTilemapPresenter>(tilemapObject);

            GameObject resourceTilemapObject =
                FindOrCreateDirectChild(gridObject.transform, "Resource Tilemap");
            Tilemap resourceTilemap = GetOrAddComponent<Tilemap>(resourceTilemapObject);
            TilemapRenderer resourceTilemapRenderer =
                GetOrAddComponent<TilemapRenderer>(resourceTilemapObject);
            resourceTilemapRenderer.mode = TilemapRenderer.Mode.Chunk;
            resourceTilemapRenderer.sortOrder = TilemapRenderer.SortOrder.BottomLeft;
            resourceTilemapRenderer.sortingOrder = 10;
            ConfigurePresenter(
                presenter,
                tilemap,
                resourceTilemap,
                grasslandTile,
                forestTile,
                desertTile,
                freshWaterTile,
                saltWaterTile,
                rockWallTile,
                treeResourceTile,
                stoneResourceTile,
                sandResourceTile);

            Camera camera = FindMainCamera(mainScene);
            ConfigureCamera(camera, grid);

            EditorSceneManager.MarkSceneDirty(mainScene);
            if (!EditorSceneManager.SaveScene(mainScene, MainScenePath))
            {
                throw new InvalidOperationException($"SampleSceneを保存できませんでした: {MainScenePath}");
            }
        }

        /// <summary>
        /// ChunkTilemapPresenterへ、地形・資源の専用Tilemapと9種類の仮Tileをまとめて設定します。
        /// </summary>
        /// <param name="presenter">設定対象のPresentation Componentです。</param>
        /// <param name="tilemap">生成地形だけを表示する専用Tilemapです。</param>
        /// <param name="resourceTilemap">生成資源だけを表示する専用Tilemapです。</param>
        /// <param name="grasslandTile">草原用の単色Tileです。</param>
        /// <param name="forestTile">森林用の単色Tileです。</param>
        /// <param name="desertTile">砂漠用の単色Tileです。</param>
        /// <param name="freshWaterTile">川と池の淡水域用の単色Tileです。</param>
        /// <param name="saltWaterTile">海の海水域用の単色Tileです。</param>
        /// <param name="rockWallTile">全岩壁へ共通で使用し、別の影Meshで内部グラデーションを後付けする単色Tileです。</param>
        /// <param name="treeResourceTile">樹木資源用の透明背景Tileです。</param>
        /// <param name="stoneResourceTile">石資源用の透明背景Tileです。</param>
        /// <param name="sandResourceTile">砂資源用の透明背景Tileです。</param>
        private static void ConfigurePresenter(
            ChunkTilemapPresenter presenter,
            Tilemap tilemap,
            Tilemap resourceTilemap,
            TileBase grasslandTile,
            TileBase forestTile,
            TileBase desertTile,
            TileBase freshWaterTile,
            TileBase saltWaterTile,
            TileBase rockWallTile,
            TileBase treeResourceTile,
            TileBase stoneResourceTile,
            TileBase sandResourceTile)
        {
            presenter.Configure(
                tilemap,
                resourceTilemap,
                grasslandTile,
                forestTile,
                desertTile,
                freshWaterTile,
                saltWaterTile,
                rockWallTile,
                treeResourceTile,
                stoneResourceTile,
                sandResourceTile);
            EditorUtility.SetDirty(presenter);
        }

        /// <summary>
        /// Main Cameraへ移動、Observer Bridge、Debug Overlay、URP Pixel Perfect Cameraを追加し、16 PPU・480×270の基準へ設定します。
        /// </summary>
        /// <param name="camera">SampleSceneでMainCameraタグを持つOrthographic Cameraです。</param>
        /// <param name="grid">カメラ範囲を整数セルへ変換するWorld Gridです。</param>
        private static void ConfigureCamera(Camera camera, Grid grid)
        {
            camera.orthographic = true;
            camera.orthographicSize = (ReferenceResolutionY / 2f) / AssetsPixelsPerUnit;
            // PlayMode中にPixel Perfect Cameraが一時変更したViewportをSceneへ残さず、毎回Gameビュー全体からWindowboxを再計算させます。
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.allowDynamicResolution = false;

            PixelPerfectCamera pixelPerfectCamera = GetOrAddComponent<PixelPerfectCamera>(camera.gameObject);
            pixelPerfectCamera.assetsPPU = AssetsPixelsPerUnit;
            pixelPerfectCamera.refResolutionX = ReferenceResolutionX;
            pixelPerfectCamera.refResolutionY = ReferenceResolutionY;
            pixelPerfectCamera.gridSnapping = PixelPerfectCamera.GridSnapping.PixelSnapping;
            pixelPerfectCamera.cropFrame = PixelPerfectCamera.CropFrame.Windowbox;

            // 移動Component自身が16 PPUへ座標を丸めるため、入力中もPixel Perfect Cameraと同じ格子を保ちます。
            GetOrAddComponent<CameraPanController>(camera.gameObject);

            CameraChunkStreamingBridge bridge = GetOrAddComponent<CameraChunkStreamingBridge>(camera.gameObject);
            SerializedObject serializedBridge = new SerializedObject(bridge);
            AssignObjectReference(serializedBridge, "targetCamera", camera);
            AssignObjectReference(serializedBridge, "targetGrid", grid);
            serializedBridge.ApplyModifiedPropertiesWithoutUndo();

            WorldGridDebugOverlay debugOverlay = GetOrAddComponent<WorldGridDebugOverlay>(camera.gameObject);
            debugOverlay.Configure(camera, grid);
            EditorUtility.SetDirty(debugOverlay);

            // ノイズ可視化はプレイヤー用F3/F4と分離し、Editorメニューだけが実行時モードを変更します。
            WorldGenerationDebugVisualizationOverlay developerVisualization =
                GetOrAddComponent<WorldGenerationDebugVisualizationOverlay>(camera.gameObject);
            developerVisualization.Configure(camera, grid);
            EditorUtility.SetDirty(developerVisualization);
        }

        /// <summary>
        /// 全Quality LevelでMSAAと異方性フィルタリングを無効化し、単色ドット絵の輪郭が補間されないようにします。
        /// </summary>
        private static void ConfigurePixelArtQualitySettings()
        {
            int originalQualityLevel = QualitySettings.GetQualityLevel();
            string[] qualityLevelNames = QualitySettings.names;

            // Quality Levelごとに値が独立しているため、現在選択中だけでなく全レベルへ同じ安全な初期値を設定します。
            for (int qualityLevel = 0; qualityLevel < qualityLevelNames.Length; qualityLevel++)
            {
                QualitySettings.SetQualityLevel(qualityLevel, applyExpensiveChanges: false);
                QualitySettings.antiAliasing = 0;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
            }

            QualitySettings.SetQualityLevel(originalQualityLevel, applyExpensiveChanges: false);
        }

        /// <summary>
        /// 指定Scene直下から名前でGameObjectを探し、存在しなければ新規作成してそのSceneへ移動します。
        /// </summary>
        /// <param name="scene">検索・作成先のSceneです。</param>
        /// <param name="objectName">重複防止に使用するGameObject名です。</param>
        /// <returns>既存または新規のScene直下GameObjectです。</returns>
        private static GameObject FindOrCreateRoot(Scene scene, string objectName)
        {
            GameObject[] rootObjects = scene.GetRootGameObjects();
            for (int index = 0; index < rootObjects.Length; index++)
            {
                if (rootObjects[index].name == objectName)
                {
                    return rootObjects[index];
                }
            }

            GameObject createdObject = new GameObject(objectName);
            SceneManager.MoveGameObjectToScene(createdObject, scene);
            return createdObject;
        }

        /// <summary>
        /// 指定親Transformの直下だけを名前で探し、存在しなければローカル変換を初期化した子GameObjectを作ります。
        /// </summary>
        /// <param name="parent">検索・作成先となる親Transformです。</param>
        /// <param name="objectName">直下の子を識別するGameObject名です。</param>
        /// <returns>既存または新規の子GameObjectです。</returns>
        private static GameObject FindOrCreateDirectChild(Transform parent, string objectName)
        {
            for (int index = 0; index < parent.childCount; index++)
            {
                Transform child = parent.GetChild(index);
                if (child.name == objectName)
                {
                    return child.gameObject;
                }
            }

            GameObject createdObject = new GameObject(objectName);
            createdObject.transform.SetParent(parent, worldPositionStays: false);
            createdObject.transform.localPosition = Vector3.zero;
            createdObject.transform.localRotation = Quaternion.identity;
            createdObject.transform.localScale = Vector3.one;
            return createdObject;
        }

        /// <summary>
        /// GameObjectから指定Componentを取得し、存在しない場合だけ追加します。
        /// </summary>
        /// <typeparam name="T">取得または追加するUnity Component型です。</typeparam>
        /// <param name="gameObject">Componentを所有するGameObjectです。</param>
        /// <returns>既存または新規のComponentです。</returns>
        private static T GetOrAddComponent<T>(GameObject gameObject)
            where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component == null ? gameObject.AddComponent<T>() : component;
        }

        /// <summary>
        /// Scene内からMainCameraタグを持つCameraを探し、見つからない場合は処理を中断します。
        /// </summary>
        /// <param name="scene">検索対象のSampleSceneです。</param>
        /// <returns>MainCameraタグを持つCamera Componentです。</returns>
        private static Camera FindMainCamera(Scene scene)
        {
            GameObject[] rootObjects = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < rootObjects.Length; rootIndex++)
            {
                Camera[] cameras = rootObjects[rootIndex].GetComponentsInChildren<Camera>(includeInactive: true);
                for (int cameraIndex = 0; cameraIndex < cameras.Length; cameraIndex++)
                {
                    if (cameras[cameraIndex].CompareTag("MainCamera"))
                    {
                        return cameras[cameraIndex];
                    }
                }
            }

            throw new InvalidOperationException("SampleSceneにMainCameraタグを持つCameraがありません。");
        }

        /// <summary>
        /// SerializedObject内のObject参照プロパティへ値を設定し、フィールド名変更時は黙って失敗せず例外にします。
        /// </summary>
        /// <param name="serializedObject">編集対象のMonoBehaviourを包むSerializedObjectです。</param>
        /// <param name="propertyName">SerializeFieldのC#フィールド名です。</param>
        /// <param name="value">Inspectorへ保存するUnity Object参照です。</param>
        private static void AssignObjectReference(
            SerializedObject serializedObject,
            string propertyName,
            UnityEngine.Object value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"{serializedObject.targetObject.GetType().Name}.{propertyName}が見つかりません。");
            }

            property.objectReferenceValue = value;
        }

        /// <summary>
        /// 保存済みアセットとSceneを再読込し、必要なComponent・Sprite・Tile参照がすべて存在することを保証します。
        /// </summary>
        private static void ValidateSavedPrototype()
        {
            string[] tileNames =
            {
                "Grassland",
                "Forest",
                "Desert",
                "FreshWater",
                "SaltWater",
                "RockWall",
                "TreeResource",
                "StoneResource",
                "SandResource"
            };
            for (int index = 0; index < tileNames.Length; index++)
            {
                string texturePath = $"{FallbackAssetFolder}/{tileNames[index]}.png";
                string tilePath = $"{FallbackAssetFolder}/{tileNames[index]}.asset";
                TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);

                // 表示のぼけや隙間へ直結するため、生成直後にImporter設定まで読み戻して検証します。
                if (importer == null
                    || importer.filterMode != FilterMode.Point
                    || importer.mipmapEnabled
                    || importer.textureCompression != TextureImporterCompression.Uncompressed
                    || importer.spritePixelsPerUnit != AssetsPixelsPerUnit
                    || tile == null
                    || tile.sprite == null)
                {
                    throw new InvalidOperationException($"フォールバックTileの検証に失敗しました: {tileNames[index]}");
                }
            }

            Scene mainScene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            Camera camera = FindMainCamera(mainScene);
            GameObject gridObject = FindOrCreateRoot(mainScene, "World Grid");
            Transform tilemapTransform = gridObject.transform.Find("Ground Tilemap");
            Transform resourceTilemapTransform = gridObject.transform.Find("Resource Tilemap");
            GameObject subSceneObject = FindOrCreateRoot(mainScene, "World Generation SubScene");

            ChunkTilemapPresenter presenter = tilemapTransform == null
                ? null
                : tilemapTransform.GetComponent<ChunkTilemapPresenter>();
            bool presenterReferencesAreValid = presenter != null && PresenterHasAllReferences(presenter);
            WorldGridDebugOverlay debugOverlay = camera.GetComponent<WorldGridDebugOverlay>();
            bool debugOverlayReferencesAreValid = debugOverlay != null && DebugOverlayHasAllReferences(debugOverlay);
            WorldGenerationDebugVisualizationOverlay developerVisualization =
                camera.GetComponent<WorldGenerationDebugVisualizationOverlay>();
            bool developerVisualizationReferencesAreValid = developerVisualization != null
                && DeveloperVisualizationHasAllReferences(developerVisualization);

            PixelPerfectCamera pixelPerfectCamera = camera.GetComponent<PixelPerfectCamera>();
            bool pixelPerfectCameraIsValid = pixelPerfectCamera != null
                && pixelPerfectCamera.assetsPPU == AssetsPixelsPerUnit
                && pixelPerfectCamera.refResolutionX == ReferenceResolutionX
                && pixelPerfectCamera.refResolutionY == ReferenceResolutionY
                && pixelPerfectCamera.gridSnapping == PixelPerfectCamera.GridSnapping.PixelSnapping
                && pixelPerfectCamera.cropFrame == PixelPerfectCamera.CropFrame.Windowbox;

            if (camera.GetComponent<CameraPanController>() == null
                || camera.GetComponent<CameraChunkStreamingBridge>() == null
                || !debugOverlayReferencesAreValid
                || !developerVisualizationReferencesAreValid
                || !pixelPerfectCameraIsValid
                || gridObject.GetComponent<Grid>() == null
                || tilemapTransform == null
                || tilemapTransform.GetComponent<Tilemap>() == null
                || resourceTilemapTransform == null
                || resourceTilemapTransform.GetComponent<Tilemap>() == null
                || resourceTilemapTransform.GetComponent<TilemapRenderer>() == null
                || !presenterReferencesAreValid
                || subSceneObject.GetComponent<SubScene>() == null
                || subSceneObject.GetComponent<SubScene>().SceneAsset == null)
            {
                throw new InvalidOperationException("SampleSceneのマップ生成Component接続を検証できませんでした。");
            }
        }

        /// <summary>
        /// Presenterの地形・資源Tilemapと9種類のTile参照が、保存後もすべて設定されているか検証します。
        /// </summary>
        /// <param name="presenter">保存済みSampleSceneから読み込んだPresenterです。</param>
        /// <returns>必要な11参照がすべて非nullならtrueです。</returns>
        private static bool PresenterHasAllReferences(ChunkTilemapPresenter presenter)
        {
            SerializedObject serializedPresenter = new SerializedObject(presenter);
            string[] propertyNames =
            {
                "targetTilemap",
                "resourceTilemap",
                "grasslandTile",
                "forestTile",
                "desertTile",
                "freshWaterTile",
                "saltWaterTile",
                "rockWallTile",
                "treeResourceTile",
                "stoneResourceTile",
                "sandResourceTile"
            };

            for (int index = 0; index < propertyNames.Length; index++)
            {
                SerializedProperty property = serializedPresenter.FindProperty(propertyNames[index]);
                if (property == null || property.objectReferenceValue == null)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Debug OverlayのCameraとGrid参照が、保存後も両方設定されているか検証します。
        /// </summary>
        /// <param name="debugOverlay">保存済みSampleSceneから読み込んだデバッグ表示Componentです。</param>
        /// <returns>投影用Cameraと座標変換用Gridが両方とも非nullならtrueです。</returns>
        private static bool DebugOverlayHasAllReferences(WorldGridDebugOverlay debugOverlay)
        {
            SerializedObject serializedOverlay = new SerializedObject(debugOverlay);
            SerializedProperty cameraProperty = serializedOverlay.FindProperty("targetCamera");
            SerializedProperty gridProperty = serializedOverlay.FindProperty("targetGrid");
            return cameraProperty != null
                && cameraProperty.objectReferenceValue != null
                && gridProperty != null
                && gridProperty.objectReferenceValue != null;
        }

        /// <summary>
        /// 開発者用ノイズ可視化のCameraとGrid参照が、保存後も両方設定されているか検証します。
        /// </summary>
        /// <param name="developerVisualization">保存済みSampleSceneから読み込んだ開発者用可視化Componentです。</param>
        /// <returns>投影用Cameraと座標変換用Gridが両方とも非nullならtrueです。</returns>
        private static bool DeveloperVisualizationHasAllReferences(
            WorldGenerationDebugVisualizationOverlay developerVisualization)
        {
            SerializedObject serializedVisualization = new SerializedObject(developerVisualization);
            SerializedProperty cameraProperty = serializedVisualization.FindProperty("targetCamera");
            SerializedProperty gridProperty = serializedVisualization.FindProperty("targetGrid");
            return cameraProperty != null
                && cameraProperty.objectReferenceValue != null
                && gridProperty != null
                && gridProperty.objectReferenceValue != null;
        }
    }
}
