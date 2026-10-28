// このファイルは、製品41材質の地形、資源の仮素材、初期動物種の静的定義を組み合わせ、マップ生成を確認できるSampleScene構成を再現可能に作ります。
// Runtimeの生成規則には関与せず、製品表示素材、Sprite Atlas、SubScene、Camera、Grid、Tilemap、静的定義のAuthoring接続だけを担当します。

using System;
using System.Collections.Generic;
using System.IO;
using Farmware.Authoring;
using Farmware.Simulation;
using Farmware.Simulation.Animals;
using Farmware.WorldGeneration.Authoring;
using Farmware.WorldGeneration.Presentation;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 製品41材質の地形、共通Sprite Atlas、資源アイコン、初期動物種の静的定義を接続し、SampleSceneと設定用SubSceneを安全に構築するEditor専用ツールです。
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

        /// <summary>C5の新18Biome形式を識別し、旧7Biome生成結果の再利用を拒否する生成器バージョンです。</summary>
        private const int PrototypeGeneratorVersion =
            WorldGenerationSettings.CurrentGeneratorVersion;

        /// <summary>最初の動物種である鶏を名称変更や配列順から独立して識別する変更禁止GUIDです。</summary>
        private const string PrototypeChickenDefinitionGuid =
            "1728e39573b24efa996020e9493cc742";

        /// <summary>鶏の1群れを作る開発用最小個体数です。単位は個体です。</summary>
        private const int PrototypeChickenMinimumHerdSize = 3;

        /// <summary>鶏の1群れを作る開発用最大個体数です。単位は個体です。</summary>
        private const int PrototypeChickenMaximumHerdSize = 6;

        /// <summary>草原の有効地形4096タイル当たりに配置する鶏の開発用目標個体数です。</summary>
        private const int PrototypeChickenPopulationPer4096EligibleTiles = 8;

        /// <summary>鶏が次の行動候補を選ぶまでの開発用最小待機時間です。単位は固定Tickです。</summary>
        private const long PrototypeChickenMinimumIdleTicks = 20;

        /// <summary>鶏が次の行動候補を選ぶまでの開発用最大待機時間です。単位は固定Tickです。</summary>
        private const long PrototypeChickenMaximumIdleTicks = 80;

        /// <summary>鶏が隣接タイルへ1歩進む開発用時間です。単位は固定Tickです。</summary>
        private const long PrototypeChickenMovementStepIntervalTicks = 5;

        /// <summary>鶏が群れの基準位置から通常の徘徊で離れてよい開発用距離です。単位はタイルです。</summary>
        private const int PrototypeChickenMaximumWanderDistanceInTiles = 8;

        /// <summary>鶏の生成時に選択できる開発用外見差分数です。</summary>
        private const int PrototypeChickenAppearanceVariantCount = 3;

        /// <summary>約1.5論理チャンク以内で気温帯が変化する試作格子サイズです。単位はタイルです。</summary>
        private const int PrototypeTemperatureCellSizeInTiles = 96;

        /// <summary>約1.25論理チャンク以内で湿度帯が変化する試作格子サイズです。単位はタイルです。</summary>
        private const int PrototypeMoistureCellSizeInTiles = 80;

        /// <summary>バイオーム境界を曲げる試作ノイズ格子サイズです。単位はタイルです。</summary>
        private const int PrototypeBiomeWarpCellSizeInTiles = 192;

        /// <summary>気候ノイズのサンプリング座標を歪ませる最大幅です。単位はタイルです。</summary>
        private const int PrototypeBiomeWarpAmplitudeInTiles = 12;

        /// <summary>海面の固定小数点既定値をInspector表現へ戻した値です。</summary>
        private const float PrototypeSeaLevelThreshold =
            WorldGenerationSettings.DefaultSeaLevelThreshold / (float)ushort.MaxValue;

        /// <summary>高温域の固定小数点既定値をInspector表現へ戻した値です。</summary>
        private const float PrototypeHotTemperatureThreshold =
            WorldGenerationSettings.DefaultHotTemperatureThreshold / (float)ushort.MaxValue;

        /// <summary>乾燥域の固定小数点既定値をInspector表現へ戻した値です。</summary>
        private const float PrototypeDryMoistureThreshold =
            WorldGenerationSettings.DefaultDryMoistureThreshold / (float)ushort.MaxValue;

        /// <summary>湿潤域の固定小数点既定値をInspector表現へ戻した値です。</summary>
        private const float PrototypeWetMoistureThreshold =
            WorldGenerationSettings.DefaultWetMoistureThreshold / (float)ushort.MaxValue;

        /// <summary>低温とみなす固定小数点閾値をInspector表現へ戻した値です。</summary>
        private const float PrototypeColdTemperatureThreshold = 23000f / ushort.MaxValue;

        /// <summary>湿地候補とみなす固定小数点湿度閾値をInspector表現へ戻した値です。</summary>
        private const float PrototypeWetlandMoistureThreshold = 50000f / ushort.MaxValue;

        /// <summary>湿地候補にできる固定小数点標高上限をInspector表現へ戻した値です。</summary>
        private const float PrototypeWetlandMaximumElevation = 36000f / ushort.MaxValue;

        /// <summary>岩地・火山候補とみなす固定小数点高地閾値をInspector表現へ戻した値です。</summary>
        private const float PrototypeHighlandElevationThreshold = 43000f / ushort.MaxValue;

        /// <summary>局所的な火山にも陸地を残しつつ、マグマの湾・流路をまとめる試作ノイズ格子サイズです。単位はタイルです。</summary>
        private const int PrototypeMagmaCellSizeInTiles = 24;

        /// <summary>マグマの入り江、半島、島を作る試作細部ノイズ格子サイズです。単位はタイルです。</summary>
        private const int PrototypeMagmaDetailCellSizeInTiles = 8;

        /// <summary>マグマ輪郭の規則性を崩す試作座標ワープ振幅です。単位はタイルです。</summary>
        private const int PrototypeMagmaWarpAmplitudeInTiles = 6;

        /// <summary>マグマ輪郭へ重ねる試作ノイズ解像度数です。</summary>
        private const int PrototypeMagmaOctaveCount = 3;

        /// <summary>活動度段階5の火山中核のおよそ半分をマグマへ変換する固定小数点ノイズ下限です。</summary>
        private const float PrototypeMagmaNoiseThreshold = 34000f / ushort.MaxValue;

        /// <summary>段階5の火山中核全域をマグマ面積比の母集団にする、追加火山活動度下限の試作値です。</summary>
        private const float PrototypeMagmaMinimumVolcanicInfluence = 0f;

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
        /// Unityメニューから、現在のScene作業状態を保護しながら製品表示付きマップ生成Sceneを構築します。
        /// </summary>
        [MenuItem("Farmware/World Generation/Generation/Build Map Prototype")]
        public static void BuildFromMenu()
        {
            RunInteractiveBuild(
                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo,
                EditorSceneManager.GetSceneManagerSetup,
                BuildMapPrototype,
                EditorSceneManager.RestoreSceneManagerSetup);
        }

        /// <summary>
        /// バッチモードから呼び出す入口です。アセット生成、SubScene作成、SampleScene接続、検証を順番に実行します。
        /// </summary>
        public static void Execute()
        {
            BuildMapPrototype();
        }

        /// <summary>
        /// 対話実行前に未保存Sceneの扱いを確認し、構築の成否にかかわらず元のScene構成へ戻します。
        /// </summary>
        /// <param name="confirmSceneSave">
        /// 未保存Sceneを保存するか確認する処理です。ユーザーがキャンセルした場合はfalseを返します。
        /// </param>
        /// <param name="captureSceneSetup">現在開いているScene、読込状態、Active Sceneを退避する処理です。</param>
        /// <param name="build">アセット、設定SubScene、SampleSceneを構築・保存する処理です。</param>
        /// <param name="restoreSceneSetup">退避したScene構成をUnity Editorへ復元する処理です。</param>
        private static void RunInteractiveBuild(
            Func<bool> confirmSceneSave,
            Func<SceneSetup[]> captureSceneSetup,
            Action build,
            Action<SceneSetup[]> restoreSceneSetup)
        {
            // 保存確認をアセット生成より先に行い、キャンセルされた場合はプロジェクトを一切変更せず終了します。
            if (!confirmSceneSave())
            {
                return;
            }

            SceneSetup[] originalSceneSetup = captureSceneSetup();
            try
            {
                // 構築中は設定SubSceneとSampleSceneをSingleで開くため、元の複数Scene構成は一時的に閉じられます。
                build();
            }
            finally
            {
                // 途中で例外が発生しても、ユーザーが作業していたScene構成とActive Sceneを必ず復元します。
                restoreSceneSetup(originalSceneSetup);
            }
        }

        /// <summary>製品地形と仮資源を使うSceneを再構築します。生成ルールは従来のAuthoring値を維持します。</summary>
        private static void BuildMapPrototype()
        {
            ValidateUniversalRenderPipeline();
            EnsureFolderHierarchy(FallbackAssetFolder);
            // 資源の仮アイコンは現行機能です。置換済みの土地・岩壁・液体素材は再生成しません。
            CreateOrUpdateFallbackResourceTile("TreeResource", GeneratedResourceType.Tree);
            CreateOrUpdateFallbackResourceTile("StoneResource", GeneratedResourceType.Stone);
            CreateOrUpdateFallbackResourceTile("SandResource", GeneratedResourceType.Sand);
            SceneAsset settingsSubSceneAsset = CreateOrUpdateSettingsSubScene();
            ConfigureMainScene(settingsSubSceneAsset,
                LoadRequiredFallbackTile("TreeResource"), LoadRequiredFallbackTile("StoneResource"), LoadRequiredFallbackTile("SandResource"));
            TerrainMaterialProductPublisher.PublishAll();
            TerrainLiquidProductPublisher.PublishAll();
            TerrainProductSceneConnector.ConnectAndSave();
            ConfigurePixelArtQualitySettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateSavedPrototype();
            Debug.Log("Farmwareの製品地形・仮資源・マップ生成用SampleSceneを構築しました。");
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
                    // ひし形の砂山を暖色で描き、砂質土の全面色と区別します。
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
            return CreateOrUpdateFallbackTileAsset(
                assetName,
                pixels,
                FallbackTilePixelSize,
                FallbackTilePixelSize,
                new Vector2(0.5f, 0.5f),
                hasTransparency);
        }

        /// <summary>
        /// 任意高さのピクセル配列をPNGへ保存し、指定PivotのSpriteと表示専用Tileへ変換します。
        /// </summary>
        /// <param name="assetName">PNGとTileアセットに使用する英語名です。</param>
        /// <param name="pixels">画像寸法と同数の行優先RGBAピクセルです。</param>
        /// <param name="pixelWidth">画像の幅です。単位はピクセルです。</param>
        /// <param name="pixelHeight">画像の高さです。単位はピクセルです。</param>
        /// <param name="pivot">画像左下を0、右上を1とするSprite Pivotです。</param>
        /// <param name="hasTransparency">透明背景としてImporterへ通知する場合はtrueです。</param>
        /// <returns>作成または更新された表示専用Tileアセットです。</returns>
        private static Tile CreateOrUpdateFallbackTileAsset(
            string assetName,
            Color32[] pixels,
            int pixelWidth,
            int pixelHeight,
            Vector2 pivot,
            bool hasTransparency)
        {
            string tileAssetPath = $"{FallbackAssetFolder}/{assetName}.asset";
            Sprite sprite = CreateOrUpdateFallbackSpriteAsset(
                assetName,
                pixels,
                pixelWidth,
                pixelHeight,
                pivot,
                hasTransparency);

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
        /// ピクセル配列をPNGへ保存し、Point・MipMapなし・無圧縮・PPU 16の単一Spriteへ変換します。
        /// </summary>
        /// <param name="assetName">PNGアセットに使用する英語名です。</param>
        /// <param name="pixels">画像寸法と同数の行優先RGBAピクセルです。</param>
        /// <param name="pixelWidth">画像の幅です。単位はピクセルです。</param>
        /// <param name="pixelHeight">画像の高さです。単位はピクセルです。</param>
        /// <param name="pivot">画像左下を0、右上を1とするSprite Pivotです。</param>
        /// <param name="hasTransparency">透明背景としてImporterへ通知する場合はtrueです。</param>
        /// <returns>作成または更新されたSpriteです。</returns>
        private static Sprite CreateOrUpdateFallbackSpriteAsset(
            string assetName,
            Color32[] pixels,
            int pixelWidth,
            int pixelHeight,
            Vector2 pivot,
            bool hasTransparency)
        {
            if (pixels == null || pixels.Length != pixelWidth * pixelHeight)
            {
                throw new ArgumentException("画像寸法とピクセル配列の要素数が一致しません。", nameof(pixels));
            }

            string textureAssetPath = $"{FallbackAssetFolder}/{assetName}.png";
            string textureAbsolutePath = Path.GetFullPath(textureAssetPath);
            Texture2D texture = new Texture2D(
                pixelWidth,
                pixelHeight,
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
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;

            // FilterやPPUなどTexture全体の設定を先に確定し、その後Sprite固有のPivotを公式Data Providerで変更します。
            // Importer固有フィールドへ直接書くと将来のSprite Editor実装と食い違うため、Pivotは必ず能力確認後に設定します。
            importer.SaveAndReimport();
            ApplySpritePivot(textureAssetPath, pivot);

            // 新規Single SpriteではData Providerの初回ApplyがMesh Typeを既定値へ戻すため、Pivot確定後に再設定します。
            // Full RectとFallback Physics Shapeなしを最後に保存し、Tilemapの矩形バッチとColliderなし契約を維持します。
            importer = AssetImporter.GetAtPath(textureAssetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Pivot設定後のTextureImporterを再取得できませんでした: {textureAssetPath}");
            }

            TextureImporterSettings importerSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(importerSettings);
            importerSettings.spriteMeshType = SpriteMeshType.FullRect;
            importerSettings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(importerSettings);
            importer.SaveAndReimport();

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(textureAssetPath);
            if (sprite == null)
            {
                throw new InvalidOperationException($"Spriteを読み込めませんでした: {textureAssetPath}");
            }

            return sprite;
        }

        /// <summary>
        /// 単一SpriteのPivotをSprite Editor Data Provider経由で設定します。
        /// </summary>
        /// <param name="textureAssetPath">Assetsから始まる対象PNGのプロジェクト相対パスです。</param>
        /// <param name="pivot">画像左下を0、右上を1とする正規化Pivotです。</param>
        /// <exception cref="InvalidOperationException">ImporterがPivot編集に対応しない場合、または単一Spriteでない場合に送出します。</exception>
        private static void ApplySpritePivot(string textureAssetPath, Vector2 pivot)
        {
            AssetImporter importer = AssetImporter.GetAtPath(textureAssetPath);
            SpriteDataProviderFactories factories = new SpriteDataProviderFactories();
            factories.Init();
            ISpriteEditorDataProvider dataProvider =
                factories.GetSpriteEditorDataProviderFromObject(importer);
            if (dataProvider == null)
            {
                throw new InvalidOperationException(
                    $"Sprite Editor Data Providerを取得できませんでした: {textureAssetPath}");
            }

            dataProvider.InitSpriteEditorDataProvider();
            ISpriteFrameEditCapability editCapability =
                dataProvider.GetDataProvider<ISpriteFrameEditCapability>();
            if (editCapability == null
                || !editCapability.GetEditCapability().HasCapability(EEditCapability.EditPivot))
            {
                // 対応能力を確認せずにSpriteRectを変更すると、Importerごとのデータ契約を壊す可能性があるため中断します。
                throw new InvalidOperationException(
                    $"Sprite Pivotの編集に対応していないImporterです: {textureAssetPath}");
            }

            SpriteRect[] spriteRects = dataProvider.GetSpriteRects();
            if (spriteRects == null || spriteRects.Length != 1)
            {
                throw new InvalidOperationException(
                    $"単一SpriteとしてPivotを設定できませんでした: {textureAssetPath}");
            }

            bool usesCenterPivot = Mathf.Approximately(pivot.x, 0.5f)
                && Mathf.Approximately(pivot.y, 0.5f);
            spriteRects[0].alignment = usesCenterPivot
                ? SpriteAlignment.Center
                : SpriteAlignment.Custom;
            spriteRects[0].pivot = pivot;
            dataProvider.SetSpriteRects(spriteRects);
            dataProvider.Apply();
            importer.SaveAndReimport();
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
        /// WorldGenerationAuthoringと静的定義Authoringを含むSubSceneを作成または更新し、Bakerが設定Singletonを生成できるようにします。
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
            DefinitionCatalogAuthoring definitionAuthoring =
                GetOrAddComponent<DefinitionCatalogAuthoring>(settingsObject);
            ConfigurePrototypeAnimalDefinitions(definitionAuthoring);

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
        /// 最初の動物種である鶏を共通定義と動物固有設定へ一対一で登録します。
        /// 構築ツールを再実行してもGUID、配列件数、開発用初期値を同じ状態へ戻します。
        /// </summary>
        /// <param name="authoring">設定SubSceneに1つだけ置く共通定義Authoringです。</param>
        private static void ConfigurePrototypeAnimalDefinitions(
            DefinitionCatalogAuthoring authoring)
        {
            SerializedObject serializedAuthoring = new SerializedObject(authoring);
            SerializedProperty definitions = serializedAuthoring.FindProperty("definitions");
            SerializedProperty animalDefinitions =
                serializedAuthoring.FindProperty("animalSpeciesDefinitions");
            if (definitions == null || animalDefinitions == null)
            {
                throw new InvalidOperationException(
                    "DefinitionCatalogAuthoringの動物種設定を取得できませんでした。");
            }

            // ANIMAL-01Aでは鶏1種だけを正式な入力とし、後続種を追加するまでは配列件数も1へ固定します。
            definitions.arraySize = 1;
            SerializedProperty chickenDefinition = definitions.GetArrayElementAtIndex(0);
            chickenDefinition.FindPropertyRelative("displayName").stringValue = "Chicken";
            chickenDefinition.FindPropertyRelative("kind").intValue =
                (int)DefinitionKind.AnimalSpecies;
            chickenDefinition.FindPropertyRelative("guid").stringValue =
                PrototypeChickenDefinitionGuid;
            chickenDefinition.FindPropertyRelative("references").arraySize = 0;

            animalDefinitions.arraySize = 1;
            SerializedProperty chickenSettings = animalDefinitions.GetArrayElementAtIndex(0);
            chickenSettings.FindPropertyRelative("definitionGuid").stringValue =
                PrototypeChickenDefinitionGuid;
            chickenSettings.FindPropertyRelative("allowedBiomeBits").intValue =
                (int)AnimalBiomeMask.TemperateGrassland;
            chickenSettings.FindPropertyRelative("allowedGrounds").intValue =
                (int)AnimalGroundMask.Land;
            chickenSettings.FindPropertyRelative("minimumHerdSize").intValue =
                PrototypeChickenMinimumHerdSize;
            chickenSettings.FindPropertyRelative("maximumHerdSize").intValue =
                PrototypeChickenMaximumHerdSize;
            chickenSettings.FindPropertyRelative("targetPopulationPer4096EligibleTiles").intValue =
                PrototypeChickenPopulationPer4096EligibleTiles;
            chickenSettings.FindPropertyRelative("minimumIdleTicks").longValue =
                PrototypeChickenMinimumIdleTicks;
            chickenSettings.FindPropertyRelative("maximumIdleTicks").longValue =
                PrototypeChickenMaximumIdleTicks;
            chickenSettings.FindPropertyRelative("movementStepIntervalTicks").longValue =
                PrototypeChickenMovementStepIntervalTicks;
            chickenSettings.FindPropertyRelative("maximumWanderDistanceInTiles").intValue =
                PrototypeChickenMaximumWanderDistanceInTiles;
            chickenSettings.FindPropertyRelative("appearanceVariantCount").intValue =
                PrototypeChickenAppearanceVariantCount;

            // privateなSerializeFieldを正規のUnityシリアライズ経路で更新し、Scene保存対象としてDirtyにします。
            serializedAuthoring.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(authoring);
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
            serializedAuthoring.FindProperty("biomeWarpCellSizeInTiles").intValue =
                PrototypeBiomeWarpCellSizeInTiles;
            serializedAuthoring.FindProperty("biomeWarpAmplitudeInTiles").intValue =
                PrototypeBiomeWarpAmplitudeInTiles;
            serializedAuthoring.FindProperty("waterThreshold").floatValue =
                PrototypeSeaLevelThreshold;
            serializedAuthoring.FindProperty("hotTemperatureThreshold").floatValue =
                PrototypeHotTemperatureThreshold;
            serializedAuthoring.FindProperty("dryMoistureThreshold").floatValue =
                PrototypeDryMoistureThreshold;
            serializedAuthoring.FindProperty("wetMoistureThreshold").floatValue =
                PrototypeWetMoistureThreshold;
            serializedAuthoring.FindProperty("coldTemperatureThreshold").floatValue =
                PrototypeColdTemperatureThreshold;
            serializedAuthoring.FindProperty("wetlandMoistureThreshold").floatValue =
                PrototypeWetlandMoistureThreshold;
            serializedAuthoring.FindProperty("wetlandMaximumElevation").floatValue =
                PrototypeWetlandMaximumElevation;
            serializedAuthoring.FindProperty("highlandElevationThreshold").floatValue =
                PrototypeHighlandElevationThreshold;
            serializedAuthoring.FindProperty("magmaCellSizeInTiles").intValue =
                PrototypeMagmaCellSizeInTiles;
            serializedAuthoring.FindProperty("magmaDetailCellSizeInTiles").intValue =
                PrototypeMagmaDetailCellSizeInTiles;
            serializedAuthoring.FindProperty("magmaWarpAmplitudeInTiles").intValue =
                PrototypeMagmaWarpAmplitudeInTiles;
            serializedAuthoring.FindProperty("magmaOctaveCount").intValue =
                PrototypeMagmaOctaveCount;
            serializedAuthoring.FindProperty("magmaNoiseThreshold").floatValue =
                PrototypeMagmaNoiseThreshold;
            serializedAuthoring.FindProperty("magmaMinimumVolcanicInfluence").floatValue =
                PrototypeMagmaMinimumVolcanicInfluence;
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
        /// <param name="treeResourceTile">樹木資源用の透明背景Tileです。</param>
        /// <param name="stoneResourceTile">石資源用の透明背景Tileです。</param>
        /// <param name="sandResourceTile">砂資源用の透明背景Tileです。</param>
        private static void ConfigureMainScene(
            SceneAsset settingsSubSceneAsset,
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
            // Floating OriginではGridのローカルセル(0, 0)を表示原点として扱うため、Transform自体は固定の基準位置へ戻します。
            gridObject.transform.position = Vector3.zero;
            gridObject.transform.rotation = Quaternion.identity;
            gridObject.transform.localScale = Vector3.one;
            Grid grid = GetOrAddComponent<Grid>(gridObject);
            grid.cellSize = Vector3.one;
            grid.cellGap = Vector3.zero;
            grid.cellLayout = GridLayout.CellLayout.Rectangle;
            grid.cellSwizzle = GridLayout.CellSwizzle.XYZ;

            GameObject tilemapObject = FindOrCreateDirectChild(gridObject.transform, "Ground Tilemap");
            Tilemap tilemap = GetOrAddComponent<Tilemap>(tilemapObject);
            tilemapObject.transform.localPosition = Vector3.zero;
            ConfigureTilemapRenderer(
                tilemapObject,
                sortingOrder: 0,
                mode: TilemapRenderer.Mode.Individual,
                sortOrder: TilemapRenderer.SortOrder.TopLeft);
            ChunkTilemapPresenter presenter = GetOrAddComponent<ChunkTilemapPresenter>(tilemapObject);

            GameObject liquidBaseTilemapObject =
                FindOrCreateDirectChild(gridObject.transform, "Liquid Base Tilemap");
            Tilemap liquidBaseTilemap = GetOrAddComponent<Tilemap>(liquidBaseTilemapObject);
            liquidBaseTilemapObject.transform.localPosition = Vector3.zero;
            ConfigureTilemapRenderer(liquidBaseTilemapObject, sortingOrder: -2);

            GameObject magmaSurfacePatternTilemapObject =
                FindOrCreateDirectChild(gridObject.transform, TerrainProductSceneConnector.MagmaSurfacePatternTilemapName);
            Tilemap magmaSurfacePatternTilemap = GetOrAddComponent<Tilemap>(magmaSurfacePatternTilemapObject);
            magmaSurfacePatternTilemap.animationFrameRate = 0f;
            // マグマ模様は論理セル原点へ置き、後段の製品接続で専用Shaderと描画順を割り当てます。
            magmaSurfacePatternTilemapObject.transform.localPosition = Vector3.zero;
            ConfigureTilemapRenderer(magmaSurfacePatternTilemapObject, sortingOrder: 2);

            // 旧分離レイヤーは参照が切れた状態で残さず、Sceneから明示的に取り除きます。
            DeleteDirectChildIfPresent(gridObject.transform, "Terrain Front Edge Tilemap");
            DeleteDirectChildIfPresent(gridObject.transform, "Rock Wall Front Face Tilemap");

            GameObject rockWallTilemapObject = FindOrCreateRenamedDirectChild(
                gridObject.transform,
                "Rock Wall Tilemap",
                "Rock Wall Top Tilemap");
            Tilemap rockWallTilemap = GetOrAddComponent<Tilemap>(rockWallTilemapObject);
            rockWallTilemapObject.transform.localPosition = Vector3.zero;
            ConfigureTilemapRenderer(
                rockWallTilemapObject,
                sortingOrder: 3,
                mode: TilemapRenderer.Mode.Individual,
                sortOrder: TilemapRenderer.SortOrder.TopLeft);

            // ここでは製品用の背面レイヤーを作り、後段の製品接続で手前2セルのWall遮蔽判定を使います。
            // 固定レイヤーへ振り分けるため、無限ワールドの大きな絶対Y座標を透明ソート値に使いません。
            GameObject resourceBehindRockWallTilemapObject =
                FindOrCreateDirectChild(gridObject.transform, "Resource Behind Rock Wall Tilemap");
            Tilemap resourceBehindRockWallTilemap =
                GetOrAddComponent<Tilemap>(resourceBehindRockWallTilemapObject);
            ConfigureTilemapRenderer(resourceBehindRockWallTilemapObject, sortingOrder: 2);

            GameObject resourceTilemapObject =
                FindOrCreateDirectChild(gridObject.transform, "Resource Tilemap");
            Tilemap resourceTilemap = GetOrAddComponent<Tilemap>(resourceTilemapObject);
            ConfigureTilemapRenderer(resourceTilemapObject, sortingOrder: 10);

            GameObject rockWallResourceTilemapObject =
                FindOrCreateDirectChild(gridObject.transform, "Rock Wall Resource Tilemap");
            Tilemap rockWallResourceTilemap =
                GetOrAddComponent<Tilemap>(rockWallResourceTilemapObject);
            rockWallResourceTilemapObject.transform.localPosition = new Vector3(
                0f,
                TerrainMaterialVerticalProjectionUtility.WallTopOffsetInTiles,
                0f);
            ConfigureTilemapRenderer(rockWallResourceTilemapObject, sortingOrder: 11);
            ConfigurePresenter(
                presenter,
                tilemap,
                resourceTilemap,
                resourceBehindRockWallTilemap,
                treeResourceTile,
                stoneResourceTile,
                sandResourceTile,
                liquidBaseTilemap,
                magmaSurfacePatternTilemap,
                rockWallTilemap,
                rockWallResourceTilemap);

            Camera camera = FindMainCamera(mainScene);
            CameraPanController panController = ConfigureCamera(camera, grid);
            presenter.ConfigureDisplayOrigin(panController);
            EditorUtility.SetDirty(presenter);

            EditorSceneManager.MarkSceneDirty(mainScene);
            if (!EditorSceneManager.SaveScene(mainScene, MainScenePath))
            {
                throw new InvalidOperationException($"SampleSceneを保存できませんでした: {MainScenePath}");
            }
        }

        /// <summary>
        /// ChunkTilemapPresenterへ、地形・資源の専用Tilemapと資源の仮Tileをまとめて設定します。
        /// </summary>
        /// <param name="presenter">設定対象のPresentation Componentです。</param>
        /// <param name="tilemap">生成地形だけを表示する専用Tilemapです。</param>
        /// <param name="resourceTilemap">岩壁に隠れない生成資源を、岩壁より前へ表示する専用Tilemapです。</param>
        /// <param name="resourceBehindRockWallTilemap">直前方の岩壁に隠す生成資源を、岩壁より後ろへ表示する専用Tilemapです。</param>
        /// <param name="treeResourceTile">樹木資源用の透明背景Tileです。</param>
        /// <param name="stoneResourceTile">石資源用の透明背景Tileです。</param>
        /// <param name="sandResourceTile">砂資源用の透明背景Tileです。</param>
        /// <param name="liquidBaseTilemap">論理セル原点へ基礎液面を描画するTilemapです。</param>
        /// <param name="magmaSurfacePatternTilemap">論理セル原点にマグマの網状模様を描画するTilemapです。</param>
        /// <param name="rockWallTilemap">2段Wall上面を描画するTilemapです。</param>
        /// <param name="rockWallResourceTilemap">製品Wall上面と同じく論理Wall根元から2タイル上へ資源を描画するTilemapです。</param>
        private static void ConfigurePresenter(
            ChunkTilemapPresenter presenter,
            Tilemap tilemap,
            Tilemap resourceTilemap,
            Tilemap resourceBehindRockWallTilemap,
            TileBase treeResourceTile,
            TileBase stoneResourceTile,
            TileBase sandResourceTile,
            Tilemap liquidBaseTilemap,
            Tilemap magmaSurfacePatternTilemap,
            Tilemap rockWallTilemap,
            Tilemap rockWallResourceTilemap)
        {
            presenter.Configure(
                tilemap,
                resourceTilemap,
                treeResourceTile,
                stoneResourceTile,
                sandResourceTile);
            presenter.ConfigureResourceOcclusion(resourceBehindRockWallTilemap);
            presenter.ConfigureTerrainEffects(
                liquidBaseTilemap,
                magmaSurfacePatternTilemap,
                rockWallTilemap,
                rockWallResourceTilemap);
            EditorUtility.SetDirty(presenter);
        }

        /// <summary>
        /// 追加Tilemapへセルの走査方向、描画モード、レイヤー間の描画順を設定します。
        /// </summary>
        /// <param name="tilemapObject">TilemapRendererを持たせる追加レイヤーのGameObjectです。</param>
        /// <param name="sortingOrder">Ground Tilemapを0とした描画順序です。</param>
        /// <param name="mode">Spriteをチャンク一括またはセル単位でソートする描画モードです。</param>
        /// <param name="sortOrder">Tilemap内のセルを描き始める方向です。背のあるSpriteは画面奥から描くためTopLeftを指定します。</param>
        private static void ConfigureTilemapRenderer(
            GameObject tilemapObject,
            int sortingOrder,
            TilemapRenderer.Mode mode = TilemapRenderer.Mode.Chunk,
            TilemapRenderer.SortOrder sortOrder = TilemapRenderer.SortOrder.BottomLeft)
        {
            TilemapRenderer renderer = GetOrAddComponent<TilemapRenderer>(tilemapObject);
            renderer.mode = mode;
            renderer.sortOrder = sortOrder;
            renderer.sortingOrder = sortingOrder;
        }

        /// <summary>
        /// Main Cameraへ初期位置、表示原点、Observer Bridge、Debug Overlay、URP Pixel Perfect Cameraを追加し、16 PPU・480×270の基準へ設定します。
        /// </summary>
        /// <param name="camera">SampleSceneでMainCameraタグを持つOrthographic Cameraです。</param>
        /// <param name="grid">カメラ範囲を整数セルへ変換するWorld Gridです。</param>
        /// <returns>Cameraと全Tilemapが共有する表示原点を所有する移動Controllerです。</returns>
        private static CameraPanController ConfigureCamera(Camera camera, Grid grid)
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
            CameraPanController panController = GetOrAddComponent<CameraPanController>(camera.gameObject);

            // Simulationが記録した初期スポーンタイルを一度だけ読み、通常の移動Controller経由でカメラへ反映します。
            InitialCameraPositionController initialPositionController =
                GetOrAddComponent<InitialCameraPositionController>(camera.gameObject);
            initialPositionController.Configure(panController);
            EditorUtility.SetDirty(initialPositionController);

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
            developerVisualization.Configure(camera, grid, panController);
            EditorUtility.SetDirty(developerVisualization);
            return panController;
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
        /// 現在名の子を優先して取得し、存在しなければ旧名の子を改名して再利用します。
        /// </summary>
        /// <param name="parent">検索・作成先となる親Transformです。</param>
        /// <param name="objectName">移行後に使用するGameObject名です。</param>
        /// <param name="legacyObjectName">旧Sceneに保存されている可能性があるGameObject名です。</param>
        /// <returns>現在名へ統一された既存または新規の子GameObjectです。</returns>
        private static GameObject FindOrCreateRenamedDirectChild(
            Transform parent,
            string objectName,
            string legacyObjectName)
        {
            Transform currentTransform = parent.Find(objectName);
            if (currentTransform != null && currentTransform.parent == parent)
            {
                return currentTransform.gameObject;
            }

            Transform legacyTransform = parent.Find(legacyObjectName);
            if (legacyTransform != null && legacyTransform.parent == parent)
            {
                legacyTransform.name = objectName;
                return legacyTransform.gameObject;
            }

            return FindOrCreateDirectChild(parent, objectName);
        }

        /// <summary>
        /// 旧仕様だけで使用した直下GameObjectをSceneから削除します。
        /// </summary>
        /// <param name="parent">削除候補を探す親Transformです。</param>
        /// <param name="objectName">削除する直下GameObject名です。</param>
        private static void DeleteDirectChildIfPresent(Transform parent, string objectName)
        {
            Transform child = parent.Find(objectName);
            if (child != null && child.parent == parent)
            {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
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
            // 製品カタログの完全性は接続時に検証済みです。ここでは現行の仮資源3枚だけを再読込します。
            string[] tileNames = { "TreeResource", "StoneResource", "SandResource" };
            foreach (string name in tileNames)
            {
                Tile tile = LoadRequiredFallbackTile(name);
                if (tile.sprite == null) throw new InvalidOperationException($"仮資源Spriteがありません: {name}");
            }

            Scene mainScene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            ValidateProductSceneReferences(mainScene);
        }

        /// <summary>読み込み済み製品Sceneの参照を検査します。検査中にSceneやAssetを作成・保存しません。</summary>
        /// <param name="mainScene">構築直後、または回帰テストがAdditiveで読み込んだ製品Sceneです。</param>
        private static void ValidateProductSceneReferences(Scene mainScene)
        {
            Camera camera = FindMainCamera(mainScene);
            GameObject gridObject = Array.Find(mainScene.GetRootGameObjects(), root => root.name == "World Grid");
            if (gridObject == null) throw new InvalidOperationException("製品SceneにWorld Gridがありません。");
            Transform tilemapTransform = gridObject.transform.Find("Ground Tilemap");
            Transform resourceTilemapTransform = gridObject.transform.Find("Resource Tilemap");
            Transform resourceBehindRockWallTilemapTransform =
                gridObject.transform.Find("Resource Behind Rock Wall Tilemap");
            Transform liquidBaseTilemapTransform =
                gridObject.transform.Find("Liquid Base Tilemap");
            Transform magmaSurfacePatternTilemapTransform =
                gridObject.transform.Find(TerrainProductSceneConnector.MagmaSurfacePatternTilemapName);
            Transform liquidDepthWithFloorTransform = gridObject.transform.Find(
                TerrainProductSceneConnector.LiquidDepthWithFloorTilemapName);
            Transform liquidDepthWithoutFloorTransform = gridObject.transform.Find(
                TerrainProductSceneConnector.LiquidDepthWithoutFloorTilemapName);
            Transform underwaterFloorPreviewTransform = gridObject.transform.Find(
                TerrainProductSceneConnector.UnderwaterFloorPreviewTilemapName);
            Transform submergedGroundFaceTransform = gridObject.transform.Find(
                TerrainProductSceneConnector.SubmergedGroundFaceTilemapName);
            Transform waterReflectionTransform = gridObject.transform.Find(
                TerrainProductSceneConnector.WaterReflectionTilemapName);
            Transform liquidEdgeWaveTransform = gridObject.transform.Find(
                TerrainProductSceneConnector.LiquidEdgeWaveTilemapName);
            Transform rockWallTilemapTransform =
                gridObject.transform.Find("Rock Wall Top Tilemap");
            Transform rockWallResourceTilemapTransform =
                gridObject.transform.Find("Rock Wall Resource Tilemap");
            GameObject subSceneObject = Array.Find(mainScene.GetRootGameObjects(), root => root.name == "World Generation SubScene");
            if (subSceneObject == null) throw new InvalidOperationException("製品Sceneに設定SubScene参照がありません。");

            ChunkTilemapPresenter presenter = tilemapTransform == null
                ? null
                : tilemapTransform.GetComponent<ChunkTilemapPresenter>();
            bool presenterReferencesAreValid = presenter != null && PresenterHasAllReferences(presenter);
            WorldGridDebugOverlay debugOverlay = camera.GetComponent<WorldGridDebugOverlay>();
            bool debugOverlayReferencesAreValid = debugOverlay != null && DebugOverlayHasAllReferences(debugOverlay);
            InitialCameraPositionController initialPositionController =
                camera.GetComponent<InitialCameraPositionController>();
            bool initialPositionReferencesAreValid = initialPositionController != null
                && InitialPositionControllerHasAllReferences(initialPositionController);
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
                || !initialPositionReferencesAreValid
                || !debugOverlayReferencesAreValid
                || !developerVisualizationReferencesAreValid
                || !pixelPerfectCameraIsValid
                || gridObject.GetComponent<Grid>() == null
                || tilemapTransform == null
                || tilemapTransform.GetComponent<Tilemap>() == null
                || tilemapTransform.GetComponent<TilemapRenderer>() == null
                || tilemapTransform.GetComponent<TilemapRenderer>().mode
                    != TilemapRenderer.Mode.Individual
                || tilemapTransform.GetComponent<TilemapRenderer>().sortOrder
                    != TilemapRenderer.SortOrder.TopLeft
                || resourceTilemapTransform == null
                || resourceTilemapTransform.GetComponent<Tilemap>() == null
                || resourceTilemapTransform.GetComponent<TilemapRenderer>() == null
                || resourceBehindRockWallTilemapTransform == null
                || resourceBehindRockWallTilemapTransform.GetComponent<Tilemap>() == null
                || resourceBehindRockWallTilemapTransform.GetComponent<TilemapRenderer>() == null
                || liquidBaseTilemapTransform == null
                || liquidBaseTilemapTransform.GetComponent<Tilemap>() == null
                || liquidBaseTilemapTransform.GetComponent<TilemapRenderer>() == null
                || !Mathf.Approximately(
                    liquidBaseTilemapTransform.localPosition.y,
                    0f)
                || liquidBaseTilemapTransform.GetComponent<TilemapRenderer>().sortingLayerName
                    != TerrainProductSceneConnector.WaterSortingLayerName
                || AssetDatabase.GetAssetPath(
                    liquidBaseTilemapTransform.GetComponent<TilemapRenderer>().sharedMaterial)
                    != TerrainProductSceneConnector.LiquidSurfaceMaterialPath
                || magmaSurfacePatternTilemapTransform == null
                || magmaSurfacePatternTilemapTransform.GetComponent<Tilemap>() == null
                || magmaSurfacePatternTilemapTransform.GetComponent<TilemapRenderer>() == null
                || !magmaSurfacePatternTilemapTransform.GetComponent<TilemapRenderer>().enabled
                || !ValidateProductLiquidTilemap(
                    magmaSurfacePatternTilemapTransform,
                    TerrainProductSceneConnector.WaterSortingLayerName,
                    TerrainProductSceneConnector.MagmaSurfacePatternMaterialPath)
                || !ValidateProductLiquidTilemap(
                    liquidDepthWithFloorTransform,
                    TerrainProductSceneConnector.WaterSortingLayerName,
                    TerrainProductSceneConnector.LiquidDepthWithFloorMaterialPath)
                || !ValidateProductLiquidTilemap(
                    liquidDepthWithoutFloorTransform,
                    TerrainProductSceneConnector.WaterSortingLayerName,
                    TerrainProductSceneConnector.LiquidDepthWithoutFloorMaterialPath)
                || !ValidateProductLiquidTilemap(
                    underwaterFloorPreviewTransform,
                    TerrainProductSceneConnector.WaterSortingLayerName,
                    TerrainProductSceneConnector.SpriteLitMaterialPath)
                || !ValidateProductLiquidTilemap(
                    submergedGroundFaceTransform,
                    TerrainProductSceneConnector.WaterSortingLayerName,
                    TerrainProductSceneConnector.SubmergedGroundFaceMaterialPath)
                || !ValidateProductLiquidTilemap(
                    waterReflectionTransform,
                    TerrainProductSceneConnector.WaterSortingLayerName,
                    TerrainProductSceneConnector.WaterReflectionMaterialPath)
                || !ValidateProductLiquidTilemap(
                    liquidEdgeWaveTransform,
                    TerrainProductSceneConnector.WaterSortingLayerName,
                    TerrainProductSceneConnector.ShorelineMaterialPath)
                || gridObject.transform.Find("Terrain Front Edge Tilemap") != null
                || gridObject.transform.Find("Rock Wall Front Face Tilemap") != null
                || gridObject.transform.Find("Rock Wall Tilemap") != null
                || rockWallTilemapTransform == null
                || rockWallTilemapTransform.GetComponent<Tilemap>() == null
                || rockWallTilemapTransform.GetComponent<TilemapRenderer>() == null
                || rockWallTilemapTransform.GetComponent<TilemapRenderer>().mode
                    != TilemapRenderer.Mode.Individual
                || rockWallTilemapTransform.GetComponent<TilemapRenderer>().sortOrder
                    != TilemapRenderer.SortOrder.TopLeft
                || resourceBehindRockWallTilemapTransform.GetComponent<TilemapRenderer>().sortingLayerName
                    != TerrainProductSceneConnector.GroundSortingLayerName
                || rockWallTilemapTransform.GetComponent<TilemapRenderer>().sortingLayerName
                    != TerrainProductSceneConnector.WallSortingLayerName
                || resourceTilemapTransform.GetComponent<TilemapRenderer>().sortingLayerName
                    != TerrainProductSceneConnector.WallSortingLayerName
                || resourceTilemapTransform.GetComponent<TilemapRenderer>().sortingOrder
                    <= rockWallTilemapTransform.GetComponent<TilemapRenderer>().sortingOrder
                || !Mathf.Approximately(
                    rockWallTilemapTransform.localPosition.y,
                    TerrainMaterialVerticalProjectionUtility.WallTopOffsetInTiles)
                || rockWallResourceTilemapTransform == null
                || rockWallResourceTilemapTransform.GetComponent<Tilemap>() == null
                || rockWallResourceTilemapTransform.GetComponent<TilemapRenderer>() == null
                || rockWallResourceTilemapTransform.GetComponent<TilemapRenderer>().sortingLayerName
                    != TerrainProductSceneConnector.WallSortingLayerName
                || rockWallResourceTilemapTransform.GetComponent<TilemapRenderer>().sortingOrder
                    <= resourceTilemapTransform.GetComponent<TilemapRenderer>().sortingOrder
                || !Mathf.Approximately(
                    rockWallResourceTilemapTransform.localPosition.y,
                    TerrainMaterialVerticalProjectionUtility.WallTopOffsetInTiles)
                || !presenterReferencesAreValid
                || subSceneObject.GetComponent<SubScene>() == null
                || subSceneObject.GetComponent<SubScene>().SceneAsset == null)
            {
                throw new InvalidOperationException("SampleSceneのマップ生成Component接続を検証できませんでした。");
            }
        }

        /// <summary>
        /// 保存済み製品液体Tilemapが論理セル原点、期待Sorting Layer、期待Materialを保持するか確認します。
        /// </summary>
        /// <param name="tilemapTransform">検証するGrid直下のTransformです。</param>
        /// <param name="expectedSortingLayer">液体またはGroundの期待Sorting Layer名です。</param>
        /// <param name="expectedMaterialPath">共有Materialの期待Assetパスです。</param>
        /// <returns>TilemapとRendererが揃い、製品接続値が一致すればtrueです。</returns>
        private static bool ValidateProductLiquidTilemap(
            Transform tilemapTransform,
            string expectedSortingLayer,
            string expectedMaterialPath)
        {
            if (tilemapTransform == null
                || tilemapTransform.GetComponent<Tilemap>() == null
                || tilemapTransform.GetComponent<TilemapRenderer>() == null
                || tilemapTransform.localPosition != Vector3.zero)
            {
                return false;
            }

            TilemapRenderer renderer = tilemapTransform.GetComponent<TilemapRenderer>();
            return renderer.enabled
                && renderer.sortingLayerName == expectedSortingLayer
                && AssetDatabase.GetAssetPath(renderer.sharedMaterial) == expectedMaterialPath;
        }

        /// <summary>
        /// 初期カメラ位置Controllerが、絶対初期位置を相対表示へ変換する通常移動Controllerを参照しているか確認します。
        /// </summary>
        /// <param name="controller">保存済みSampleSceneから読み込んだ初期位置Controllerです。</param>
        /// <returns>CameraPanController参照が非nullならtrueです。</returns>
        private static bool InitialPositionControllerHasAllReferences(
            InitialCameraPositionController controller)
        {
            SerializedObject serializedController = new SerializedObject(controller);
            SerializedProperty panControllerProperty =
                serializedController.FindProperty("cameraPanController");
            return panControllerProperty != null
                && panControllerProperty.objectReferenceValue != null;
        }

        /// <summary>
        /// Presenterの地形・資源・追加表示Tilemapと必要なTile参照が、保存後もすべて設定されているか検証します。
        /// </summary>
        /// <param name="presenter">保存済みSampleSceneから読み込んだPresenterです。</param>
        /// <returns>必要な地形・断面・液面模様の参照がすべて非nullならtrueです。</returns>
        private static bool PresenterHasAllReferences(ChunkTilemapPresenter presenter)
        {
            SerializedObject serializedPresenter = new SerializedObject(presenter);
            string[] propertyNames =
            {
                "targetTilemap",
                "resourceTilemap",
                "resourceBehindRockWallTilemap",
                "liquidBaseTilemap",
                "magmaSurfacePatternTilemap",
                "liquidDepthWithFloorTilemap",
                "liquidDepthWithoutFloorTilemap",
                "underwaterFloorPreviewTilemap",
                "submergedGroundFaceTilemap",
                "waterReflectionTilemap",
                "waterReflectionAtlas",
                "waterSurfacePatternTilemap",
                "liquidEdgeWaveTilemap",
                "rockWallTilemap",
                "rockWallResourceTilemap",
                "groundFaceTilemap",
                "wallFaceTilemap",
                "dryCliffDeepBlackTilemap",
                "wallContactShadowLeftTilemap",
                "wallContactShadowRightTilemap",
                "wallFoldShadowTopTilemap",
                "wallFoldShadowBottomTilemap",
                "materialPresentationCatalog",
                "liquidPresentationCatalog",
                "cameraPanController",
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
        /// 開発者用ノイズ可視化のCamera、Grid、表示原点参照が、保存後もすべて設定されているか検証します。
        /// </summary>
        /// <param name="developerVisualization">保存済みSampleSceneから読み込んだ開発者用可視化Componentです。</param>
        /// <returns>投影用Camera、座標変換用Grid、CameraPanControllerがすべて非nullならtrueです。</returns>
        private static bool DeveloperVisualizationHasAllReferences(
            WorldGenerationDebugVisualizationOverlay developerVisualization)
        {
            SerializedObject serializedVisualization = new SerializedObject(developerVisualization);
            SerializedProperty cameraProperty = serializedVisualization.FindProperty("targetCamera");
            SerializedProperty gridProperty = serializedVisualization.FindProperty("targetGrid");
            SerializedProperty panControllerProperty =
                serializedVisualization.FindProperty("cameraPanController");
            return cameraProperty != null
                && cameraProperty.objectReferenceValue != null
                && gridProperty != null
                && gridProperty.objectReferenceValue != null
                && panControllerProperty != null
                && panControllerProperty.objectReferenceValue != null;
        }
    }
}
