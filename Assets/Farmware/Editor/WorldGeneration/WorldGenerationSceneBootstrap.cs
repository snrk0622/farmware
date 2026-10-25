// このファイルは、本番用バイオーム土地・岩壁、資源・液面アニメーションの仮素材、初期動物種の静的定義を組み合わせ、マップ生成を確認できるSampleScene構成を再現可能に作ります。
// Runtimeの生成規則には関与せず、製品・フォールバック表示素材、Sprite Atlas、SubScene、Camera、Grid、Tilemap、静的定義のAuthoring接続だけを担当します。

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
    /// 本番用バイオーム土地・岩壁、フォールバック地形、共通Sprite Atlas、資源アイコン、初期動物種の静的定義を接続し、SampleSceneと設定用SubSceneを安全に構築するEditor専用ツールです。
    /// メニューまたはバッチモードのexecuteMethodから呼び出し、同じ名前の構成は再利用して重複作成を防ぎます。
    /// </summary>
    public static class WorldGenerationSceneBootstrap
    {
        /// <summary>表示用フォールバック素材をまとめるUnityプロジェクト相対フォルダです。</summary>
        private const string FallbackAssetFolder = "Assets/Farmware/Art/WorldGeneration/Fallback";

        /// <summary>本番用土地・岩壁のTileアセットをまとめるUnityプロジェクト相対フォルダです。</summary>
        private const string ProductionTerrainTileFolder =
            "Assets/Farmware/Art/WorldGeneration/Terrain/Base";

        /// <summary>本番用岩壁Spriteを参照する専用TileのUnityプロジェクト相対パスです。</summary>
        private const string ProductionRockWallTilePath =
            ProductionTerrainTileFolder + "/terrain_rock_wall_01.asset";

        // BiomeId 1〜18と同じ順序で仮土地Tileを作成・読み込みするための安定英語名です。
        private static readonly string[] FallbackBiomeAssetNames =
        {
            "TemperateGrassland",
            "TropicalSavannaWoodland",
            "TemperateDeciduousForest",
            "WarmTemperateEvergreenForest",
            "TemperateMontaneBambooForest",
            "TropicalRainforest",
            "TropicalSeasonalForest",
            "SubtropicalOceanicForest",
            "BorealConiferForest",
            "Steppe",
            "DryShrublandWoodland",
            "CactusDesertDryPlateau",
            "HotDesert",
            "FreshwaterMarshFloodplain",
            "SwampForest",
            "TundraPolarBarrens",
            "AlpineRockland",
            "VolcanicGeothermal"
        };

        // C5より前に生成していた7土地Tileを削除し、旧IDの見た目を新IDへ暗黙流用しないための名前一覧です。
        private static readonly string[] LegacyFallbackBiomeAssetNames =
        {
            "Grassland", "Forest", "Desert", "Rockland", "Wetland", "Snowfield", "Volcanic"
        };

        // 18種を仮素材の段階でも目視で区別する上面色です。配列位置はFallbackBiomeAssetNamesと一致します。
        private static readonly Color32[] FallbackBiomeTopColors =
        {
            new Color32(112, 169, 86, 255), new Color32(150, 166, 75, 255),
            new Color32(79, 139, 70, 255), new Color32(50, 122, 77, 255),
            new Color32(91, 151, 88, 255), new Color32(38, 112, 67, 255),
            new Color32(68, 133, 61, 255), new Color32(58, 137, 105, 255),
            new Color32(66, 112, 82, 255), new Color32(174, 162, 82, 255),
            new Color32(154, 130, 74, 255), new Color32(188, 151, 83, 255),
            new Color32(218, 181, 101, 255), new Color32(77, 142, 119, 255),
            new Color32(54, 116, 90, 255), new Color32(190, 214, 218, 255),
            new Color32(126, 122, 135, 255), new Color32(132, 68, 57, 255)
        };

        /// <summary>Presentation用GameObjectを配置する既存の確認Sceneです。</summary>
        private const string MainScenePath = "Assets/Scenes/SampleScene.unity";

        /// <summary>BakerがWorldGenerationSettings Entityへ変換するAuthoring専用SubSceneです。</summary>
        private const string SettingsSubScenePath = "Assets/Scenes/WorldGenerationSettingsSubScene.unity";

        /// <summary>フォールバック画像の1辺のピクセル数で、全Tileを偶数寸法へ統一します。</summary>
        private const int FallbackTilePixelSize = 16;

        /// <summary>淡水・海水・マグマの仮アニメーションに使用するフレーム数です。</summary>
        private const int LiquidAnimationFrameCount = 4;

        /// <summary>液面を速く点滅させず穏やかに見せる、1秒あたりのアニメーションフレーム数です。</summary>
        private const float LiquidAnimationFramesPerSecond = 4f;

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
        /// Unityメニューから、現在のScene作業状態を保護しながらフォールバック表示付きマップ生成Sceneを構築します。
        /// </summary>
        [MenuItem("Farmware/World Generation/Build Fallback Map Prototype")]
        public static void BuildFromMenu()
        {
            RunInteractiveBuild(
                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo,
                EditorSceneManager.GetSceneManagerSetup,
                BuildFallbackMapPrototype,
                EditorSceneManager.RestoreSceneManagerSetup);
        }

        /// <summary>
        /// バッチモードから呼び出す入口です。アセット生成、SubScene作成、SampleScene接続、検証を順番に実行します。
        /// </summary>
        public static void Execute()
        {
            BuildFallbackMapPrototype();
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

        /// <summary>
        /// 必要な全アセットとScene構成を冪等に作り、保存後の参照が欠けていないことを検証します。
        /// </summary>
        private static void BuildFallbackMapPrototype()
        {
            ValidateUniversalRenderPipeline();
            EnsureFolderHierarchy(FallbackAssetFolder);

            // 各土地は上面16pxと断面8pxを1枚へまとめます。正式アートが揃うまでは18種を異なる上面色で明示します。
            for (int index = 0; index < FallbackBiomeAssetNames.Length; index++)
            {
                bool isDryBiome = index >= 9 && index <= 12;
                bool isRockBiome = index >= 16;
                bool isPolarBiome = index == 15;
                Color32 faceColor = isRockBiome
                    ? new Color32(75, 72, 81, 255)
                    : isPolarBiome
                        ? new Color32(159, 190, 201, 255)
                        : isDryBiome
                            ? new Color32(174, 130, 66, 255)
                            : new Color32(92, 63, 40, 255);
                Color32 lowerFaceColor = isRockBiome
                    ? new Color32(113, 108, 120, 255)
                    : isDryBiome
                        ? new Color32(213, 170, 91, 255)
                        : new Color32(139, 92, 53, 255);
                CreateOrUpdateFallbackTerrainTile(
                    FallbackBiomeAssetNames[index],
                    FallbackBiomeTopColors[index],
                    faceColor,
                    lowerFaceColor);
            }
            CreateOrUpdateFallbackTile("Magma", new Color32(238, 82, 24, 255));
            CreateOrUpdateFallbackTile("FreshWater", new Color32(76, 157, 211, 255));
            CreateOrUpdateFallbackTile("SaltWater", new Color32(42, 101, 176, 255));
            CreateOrUpdateFallbackRockWallTile(
                "RockWall",
                new Color32(101, 101, 109, 255),
                new Color32(69, 68, 76, 255),
                new Color32(91, 88, 99, 255));
            CreateOrUpdateFallbackResourceTile("TreeResource", GeneratedResourceType.Tree);
            CreateOrUpdateFallbackResourceTile("StoneResource", GeneratedResourceType.Stone);
            CreateOrUpdateFallbackResourceTile("SandResource", GeneratedResourceType.Sand);
            CreateOrUpdateFallbackLiquidAnimationTile(
                "FreshWaterAnimation",
                new Color32(186, 231, 255, 190),
                isMagma: false);
            CreateOrUpdateFallbackLiquidAnimationTile(
                "SaltWaterAnimation",
                new Color32(116, 193, 255, 190),
                isMagma: false);
            CreateOrUpdateFallbackLiquidAnimationTile(
                "MagmaAnimation",
                new Color32(255, 214, 67, 220),
                isMagma: true);

            // 水は地形そのものを採水源に変更したため、旧水域Tileと水資源アイコンを再生成対象から除外します。
            DeleteObsoleteFallbackTile("Water");
            DeleteObsoleteFallbackTile("WaterResource");
            DeleteObsoleteFallbackTile("RockWallInterior");
            DeleteObsoleteFallbackTile("SoilFrontEdge");
            DeleteObsoleteFallbackTile("SandFrontEdge");
            DeleteObsoleteFallbackTile("RockFrontEdge");
            DeleteObsoleteFallbackTile("SnowFrontEdge");
            DeleteObsoleteFallbackTile("RockWallFrontFace");

            // 旧7Biomeの基準TileとSoil派生Tileは新IDへ流用せず、C5の版境界でまとめて削除します。
            for (int index = 0; index < LegacyFallbackBiomeAssetNames.Length; index++)
            {
                string legacyName = LegacyFallbackBiomeAssetNames[index];
                DeleteObsoleteFallbackTile(legacyName);
                DeleteObsoleteFallbackTile(legacyName + "Fertile");
                DeleteObsoleteFallbackTile(legacyName + "Sandy");
            }

            // 14基本形は論理マスクから再現可能に生成し、画像順と47接続選択の対応を常に一致させます。
            TerrainBoundaryHighlightArtGenerator.GenerateOrUpdateSpriteSheet();

            // 本番PNG自体は書き換えず、Importerと専用Tileだけを冪等に設定します。
            // これを毎回行うことで、構築ツールの再実行後もFallback岩壁へ参照が戻りません。
            CreateOrUpdateProductionRockWallTile();

            // PNGのSaveAndReimport中に以前読み込んだUnity Object参照が無効化される場合があります。
            // 液体・岩壁・資源を生成した後、本番用土地を含む最新のTileを読み直してSceneへ保存します。
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // 一体型土地はセル同士を8px重ねるため、元PNGが異なっても行ソートを維持できる共通Atlasへまとめます。
            // 岩壁も同じ制作・ビルド手順で管理し、本番素材へ差し替えた後のTexture分割を防ぎます。
            WorldGenerationSpriteAtlasBuilder.GenerateOrUpdateAtlases();

            TileBase[] biomeTiles = new TileBase[BiomeStableIds.Count];
            for (int index = 0; index < biomeTiles.Length; index++)
            {
                // 配列位置を安定IDへ直結し、旧7種の近似Tileを選ぶ分岐を作りません。
                biomeTiles[index] = LoadRequiredFallbackTile(FallbackBiomeAssetNames[index]);
            }
            Tile magmaTile = LoadRequiredFallbackTile("Magma");
            Tile freshWaterTile = LoadRequiredFallbackTile("FreshWater");
            Tile saltWaterTile = LoadRequiredFallbackTile("SaltWater");
            Tile rockWallTile = LoadRequiredProductionRockWallTile();
            Tile treeResourceTile = LoadRequiredFallbackTile("TreeResource");
            Tile stoneResourceTile = LoadRequiredFallbackTile("StoneResource");
            Tile sandResourceTile = LoadRequiredFallbackTile("SandResource");
            AnimatedTile freshWaterAnimationTile =
                LoadRequiredFallbackAnimatedTile("FreshWaterAnimation");
            AnimatedTile saltWaterAnimationTile =
                LoadRequiredFallbackAnimatedTile("SaltWaterAnimation");
            AnimatedTile magmaAnimationTile =
                LoadRequiredFallbackAnimatedTile("MagmaAnimation");
            Sprite[] boundaryHighlightSprites =
                TerrainBoundaryHighlightArtGenerator.LoadRequiredSprites();

            SceneAsset settingsSubSceneAsset = CreateOrUpdateSettingsSubScene();
            ConfigureMainScene(
                settingsSubSceneAsset,
                biomeTiles,
                magmaTile,
                freshWaterTile,
                saltWaterTile,
                rockWallTile,
                treeResourceTile,
                stoneResourceTile,
                sandResourceTile,
                freshWaterAnimationTile,
                saltWaterAnimationTile,
                magmaAnimationTile,
                boundaryHighlightSprites);
            ConfigurePixelArtQualitySettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateSavedPrototype();
            Debug.Log("Farmwareの本番用バイオーム土地、フォールバック地形、マップ生成用SampleSceneを構築しました。");
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
        /// 上面16pxと前面断面8pxを一枚へ結合した16×24土地Tileを作成または更新します。
        /// </summary>
        /// <param name="assetName">PNGとTileアセットに使用する英語名です。</param>
        /// <param name="surfaceColor">上側16pxを一色で塗るバイオームの地形上面色です。</param>
        /// <param name="rimColor">断面上端の1pxへ使う影色です。</param>
        /// <param name="faceColor">断面の地盤色です。</param>
        /// <returns>作成または更新された一体型土地Tileアセットです。</returns>
        private static Tile CreateOrUpdateFallbackTerrainTile(
            string assetName,
            Color32 surfaceColor,
            Color32 rimColor,
            Color32 faceColor)
        {
            int pixelHeight = TerrainLayerPresentationUtility.CombinedTerrainSpriteHeightInPixels;
            Color32[] pixels = new Color32[FallbackTilePixelSize * pixelHeight];

            for (int y = 0; y < pixelHeight; y++)
            {
                for (int x = 0; x < FallbackTilePixelSize; x++)
                {
                    Color32 selectedColor;
                    if (y >= TerrainLayerPresentationUtility.TerrainFrontFaceHeightInPixels)
                    {
                        // フォールバック上面はBiome色だけを示し、草・葉・砂紋やSoilTypeの模様を重ねません。
                        selectedColor = surfaceColor;
                    }
                    else if (y == TerrainLayerPresentationUtility.TerrainFrontFaceHeightInPixels - 1)
                    {
                        selectedColor = rimColor;
                    }
                    else
                    {
                        selectedColor = ((x + y) % 5 == 0)
                            ? DarkenFallbackColor(faceColor, 18)
                            : faceColor;
                    }

                    pixels[(y * FallbackTilePixelSize) + x] = selectedColor;
                }
            }

            return CreateOrUpdateFallbackTileAsset(
                assetName,
                pixels,
                FallbackTilePixelSize,
                pixelHeight,
                new Vector2(0.5f, TerrainLayerPresentationUtility.CombinedTerrainPivotY),
                hasTransparency: false);
        }

        /// <summary>
        /// 上面16pxと側面12pxを一枚へ結合した16×28岩壁Tileを作成または更新します。
        /// </summary>
        /// <param name="assetName">PNGとTileアセットに使用する英語名です。</param>
        /// <param name="surfaceColor">上側16pxの岩壁上面色です。</param>
        /// <param name="rimColor">側面上端の1pxへ使う影色です。</param>
        /// <param name="faceColor">岩壁側面の地盤色です。</param>
        /// <returns>作成または更新された一体型岩壁Tileアセットです。</returns>
        private static Tile CreateOrUpdateFallbackRockWallTile(
            string assetName,
            Color32 surfaceColor,
            Color32 rimColor,
            Color32 faceColor)
        {
            int pixelHeight = TerrainLayerPresentationUtility.CombinedRockWallSpriteHeightInPixels;
            Color32[] pixels = new Color32[FallbackTilePixelSize * pixelHeight];

            for (int y = 0; y < pixelHeight; y++)
            {
                for (int x = 0; x < FallbackTilePixelSize; x++)
                {
                    Color32 selectedColor;
                    if (y >= TerrainLayerPresentationUtility.RockWallElevationInPixels)
                    {
                        selectedColor = surfaceColor;
                    }
                    else if (y == TerrainLayerPresentationUtility.RockWallElevationInPixels - 1)
                    {
                        selectedColor = rimColor;
                    }
                    else
                    {
                        selectedColor = ((x + y) % 5 == 0)
                            ? DarkenFallbackColor(faceColor, 18)
                            : faceColor;
                    }

                    pixels[(y * FallbackTilePixelSize) + x] = selectedColor;
                }
            }

            return CreateOrUpdateFallbackTileAsset(
                assetName,
                pixels,
                FallbackTilePixelSize,
                pixelHeight,
                new Vector2(0.5f, TerrainLayerPresentationUtility.CombinedRockWallPivotY),
                hasTransparency: false);
        }

        /// <summary>
        /// 4枚の透明PNGとAnimatedTileを作成または更新し、液体本体を隠さない波・気泡を用意します。
        /// </summary>
        /// <param name="assetName">AnimatedTileとフレームPNGの共通英語名です。</param>
        /// <param name="effectColor">波または気泡へ使用する半透明色です。</param>
        /// <param name="isMagma">波ではなくマグマ気泡を描く場合はtrueです。</param>
        /// <returns>作成または更新された液面表示専用AnimatedTileです。</returns>
        private static AnimatedTile CreateOrUpdateFallbackLiquidAnimationTile(
            string assetName,
            Color32 effectColor,
            bool isMagma)
        {
            Sprite[] frames = new Sprite[LiquidAnimationFrameCount];
            for (int frameIndex = 0; frameIndex < LiquidAnimationFrameCount; frameIndex++)
            {
                Color32[] pixels = new Color32[FallbackTilePixelSize * FallbackTilePixelSize];
                if (isMagma)
                {
                    DrawFallbackMagmaBubbles(pixels, effectColor, frameIndex);
                }
                else
                {
                    DrawFallbackWaterWave(pixels, effectColor, frameIndex);
                }

                string frameAssetName = $"{assetName}Frame{frameIndex:00}";
                frames[frameIndex] = CreateOrUpdateFallbackSpriteAsset(
                    frameAssetName,
                    pixels,
                    FallbackTilePixelSize,
                    FallbackTilePixelSize,
                    new Vector2(0.5f, 0.5f),
                    hasTransparency: true);
            }

            string animatedTileAssetPath = $"{FallbackAssetFolder}/{assetName}.asset";
            AnimatedTile animatedTile =
                AssetDatabase.LoadAssetAtPath<AnimatedTile>(animatedTileAssetPath);
            if (animatedTile == null)
            {
                animatedTile = ScriptableObject.CreateInstance<AnimatedTile>();
                AssetDatabase.CreateAsset(animatedTile, animatedTileAssetPath);
            }

            animatedTile.m_AnimatedSprites = frames;
            animatedTile.m_MinSpeed = 1f;
            animatedTile.m_MaxSpeed = 1f;
            animatedTile.m_AnimationStartTime = 0f;
            animatedTile.m_AnimationStartFrame = 0;
            animatedTile.m_TileColliderType = Tile.ColliderType.None;
            EditorUtility.SetDirty(animatedTile);
            return animatedTile;
        }

        /// <summary>
        /// 水面の左右へ流れる短い水平線を、フレームごとに1px上下・左右へ移動して描きます。
        /// </summary>
        /// <param name="pixels">書き込む16×16の透明ピクセル配列です。</param>
        /// <param name="waveColor">波へ使用する半透明色です。</param>
        /// <param name="frameIndex">0〜3のアニメーションフレーム番号です。</param>
        private static void DrawFallbackWaterWave(
            Color32[] pixels,
            Color32 waveColor,
            int frameIndex)
        {
            int firstWaveY = 10 + (frameIndex == 1 || frameIndex == 2 ? 1 : 0);
            int secondWaveY = 4 + (frameIndex >= 2 ? 1 : 0);
            int horizontalShift = frameIndex % 2;
            DrawFallbackHorizontalLine(pixels, 2 + horizontalShift, firstWaveY, 5, waveColor);
            DrawFallbackHorizontalLine(pixels, 9 - horizontalShift, firstWaveY - 3, 4, waveColor);
            DrawFallbackHorizontalLine(pixels, 4 - horizontalShift, secondWaveY, 5, waveColor);
        }

        /// <summary>
        /// マグマ面の気泡が膨らんで消える様子を、フレーム番号に応じた小さな円で描きます。
        /// </summary>
        /// <param name="pixels">書き込む16×16の透明ピクセル配列です。</param>
        /// <param name="bubbleColor">気泡へ使用する半透明色です。</param>
        /// <param name="frameIndex">0〜3のアニメーションフレーム番号です。</param>
        private static void DrawFallbackMagmaBubbles(
            Color32[] pixels,
            Color32 bubbleColor,
            int frameIndex)
        {
            int radius = frameIndex == 1 || frameIndex == 2 ? 2 : 1;
            DrawFallbackCircleOutline(pixels, 5, 6 + (frameIndex / 2), radius, bubbleColor);
            if (frameIndex >= 2)
            {
                DrawFallbackCircleOutline(pixels, 11, 10, 1, bubbleColor);
            }
        }

        /// <summary>
        /// 16×16配列へ範囲内の水平線を描き、仮アニメーション形状を組み立てます。
        /// </summary>
        /// <param name="pixels">書き込む16×16ピクセル配列です。</param>
        /// <param name="startX">線の左端X座標です。</param>
        /// <param name="y">線のY座標です。</param>
        /// <param name="length">線の長さです。単位はピクセルです。</param>
        /// <param name="color">書き込む色です。</param>
        private static void DrawFallbackHorizontalLine(
            Color32[] pixels,
            int startX,
            int y,
            int length,
            Color32 color)
        {
            for (int offset = 0; offset < length; offset++)
            {
                int x = startX + offset;
                if (x >= 0 && x < FallbackTilePixelSize && y >= 0 && y < FallbackTilePixelSize)
                {
                    pixels[(y * FallbackTilePixelSize) + x] = color;
                }
            }
        }

        /// <summary>
        /// 16×16配列へ小さな円周を描き、マグマ気泡の膨張を表します。
        /// </summary>
        /// <param name="pixels">書き込む16×16ピクセル配列です。</param>
        /// <param name="centerX">気泡中心のX座標です。</param>
        /// <param name="centerY">気泡中心のY座標です。</param>
        /// <param name="radius">気泡半径です。単位はピクセルです。</param>
        /// <param name="color">書き込む色です。</param>
        private static void DrawFallbackCircleOutline(
            Color32[] pixels,
            int centerX,
            int centerY,
            int radius,
            Color32 color)
        {
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    int distanceSquared = (offsetX * offsetX) + (offsetY * offsetY);
                    if (distanceSquared < radius * radius - radius
                        || distanceSquared > radius * radius + radius)
                    {
                        continue;
                    }

                    int x = centerX + offsetX;
                    int y = centerY + offsetY;
                    if (x >= 0 && x < FallbackTilePixelSize && y >= 0 && y < FallbackTilePixelSize)
                    {
                        pixels[(y * FallbackTilePixelSize) + x] = color;
                    }
                }
            }
        }

        /// <summary>
        /// RGB各成分を同じ量だけ暗くし、側面へ最低限の縦筋を付けます。
        /// </summary>
        /// <param name="source">元となる不透明色です。</param>
        /// <param name="amount">0〜255で差し引く明度です。</param>
        /// <returns>Alphaを維持して暗くした色です。</returns>
        private static Color32 DarkenFallbackColor(Color32 source, byte amount)
        {
            return new Color32(
                (byte)Math.Max(0, source.r - amount),
                (byte)Math.Max(0, source.g - amount),
                (byte)Math.Max(0, source.b - amount),
                source.a);
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
        /// 配置済みの16×28本番岩壁PNGへPixel Art用Importer設定を適用し、専用Tileを作成または更新します。
        /// </summary>
        /// <returns>本番岩壁Spriteを参照し、Colliderを持たない表示専用Tileです。</returns>
        /// <remarks>PNGの画素は変更せず、Importer、Pivot、Tile参照だけを更新します。</remarks>
        private static Tile CreateOrUpdateProductionRockWallTile()
        {
            string texturePath = WorldGenerationSpriteAtlasBuilder.ProductionRockWallTexturePath;
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null
                || texture.width != FallbackTilePixelSize
                || texture.height
                    != TerrainLayerPresentationUtility.CombinedRockWallSpriteHeightInPixels)
            {
                throw new InvalidOperationException(
                    $"本番用岩壁画像は16×28ピクセルで配置してください: {texturePath}");
            }

            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"本番用岩壁画像のTextureImporterを取得できません: {texturePath}");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = AssetsPixelsPerUnit;
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
            importer.SaveAndReimport();

            // 下側12pxを基準面より上へ積むため、Pivotは左下基準でY=8/28へ固定します。
            ApplySpritePivot(
                texturePath,
                new Vector2(0.5f, TerrainLayerPresentationUtility.CombinedRockWallPivotY));

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
            if (sprite == null)
            {
                throw new InvalidOperationException(
                    $"本番用岩壁Spriteを読み込めませんでした: {texturePath}");
            }

            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(ProductionRockWallTilePath);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, ProductionRockWallTilePath);
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
        /// 本番用岩壁Tileを読み直し、Fallbackではなく配置済みPNGのSpriteを参照することを確認します。
        /// </summary>
        /// <returns>本番用岩壁Spriteを参照する専用Tileです。</returns>
        private static Tile LoadRequiredProductionRockWallTile()
        {
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(ProductionRockWallTilePath);
            if (tile == null || tile.sprite == null)
            {
                throw new InvalidOperationException(
                    $"本番用岩壁Tileを再読込できませんでした: {ProductionRockWallTilePath}");
            }

            string spriteAssetPath = AssetDatabase.GetAssetPath(tile.sprite);
            if (!string.Equals(tile.sprite.name, "terrain_rock_wall_01", StringComparison.Ordinal)
                || !string.Equals(
                    spriteAssetPath,
                    WorldGenerationSpriteAtlasBuilder.ProductionRockWallTexturePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"本番用岩壁Tileが別のSpriteを参照しています: "
                    + $"{ProductionRockWallTilePath}, sprite={tile.sprite.name}, "
                    + $"texture={spriteAssetPath}");
            }

            return tile;
        }

        /// <summary>
        /// 全Textureの再インポート後に、指定名のフォールバックAnimatedTileをAssetDatabaseから読み直します。
        /// </summary>
        /// <param name="assetName">読み込むAnimatedTileアセットの拡張子を除いた名前です。</param>
        /// <returns>4枚の有効なSprite参照を持つAnimatedTileアセットです。</returns>
        private static AnimatedTile LoadRequiredFallbackAnimatedTile(string assetName)
        {
            string tileAssetPath = $"{FallbackAssetFolder}/{assetName}.asset";
            AnimatedTile tile = AssetDatabase.LoadAssetAtPath<AnimatedTile>(tileAssetPath);
            if (tile == null
                || tile.m_AnimatedSprites == null
                || tile.m_AnimatedSprites.Length != LiquidAnimationFrameCount)
            {
                throw new InvalidOperationException(
                    $"フォールバックAnimatedTileを再読込できませんでした: {tileAssetPath}");
            }

            for (int frameIndex = 0; frameIndex < tile.m_AnimatedSprites.Length; frameIndex++)
            {
                if (tile.m_AnimatedSprites[frameIndex] == null)
                {
                    throw new InvalidOperationException(
                        $"AnimatedTileのフレーム参照がありません: {tileAssetPath} frame={frameIndex}");
                }
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
        /// <param name="biomeTiles">BiomeId 1〜18の順で並ぶ、識別用の仮土地Tileです。</param>
        /// <param name="magmaTile">マグマ地形用の単色Tileです。</param>
        /// <param name="freshWaterTile">川と池の淡水域用の単色Tileです。</param>
        /// <param name="saltWaterTile">海の海水域用の単色Tileです。</param>
        /// <param name="rockWallTile">全岩壁へ共通で使用し、別の影Meshで内部グラデーションを後付けする単色Tileです。</param>
        /// <param name="treeResourceTile">樹木資源用の透明背景Tileです。</param>
        /// <param name="stoneResourceTile">石資源用の透明背景Tileです。</param>
        /// <param name="sandResourceTile">砂資源用の透明背景Tileです。</param>
        /// <param name="freshWaterAnimationTile">淡水へ重ねる透明AnimatedTileです。</param>
        /// <param name="saltWaterAnimationTile">海水へ重ねる透明AnimatedTileです。</param>
        /// <param name="magmaAnimationTile">マグマへ重ねる透明AnimatedTileです。</param>
        /// <param name="boundaryHighlightSprites">左からShape01〜Shape14の順に並ぶ境界ハイライトSpriteです。</param>
        private static void ConfigureMainScene(
            SceneAsset settingsSubSceneAsset,
            TileBase[] biomeTiles,
            TileBase magmaTile,
            TileBase freshWaterTile,
            TileBase saltWaterTile,
            TileBase rockWallTile,
            TileBase treeResourceTile,
            TileBase stoneResourceTile,
            TileBase sandResourceTile,
            TileBase freshWaterAnimationTile,
            TileBase saltWaterAnimationTile,
            TileBase magmaAnimationTile,
            Sprite[] boundaryHighlightSprites)
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

            GameObject landBoundaryHighlightTilemapObject =
                FindOrCreateDirectChild(gridObject.transform, "Land Boundary Highlight Tilemap");
            Tilemap landBoundaryHighlightTilemap =
                GetOrAddComponent<Tilemap>(landBoundaryHighlightTilemapObject);
            landBoundaryHighlightTilemapObject.transform.localPosition = Vector3.zero;
            ConfigureTilemapRenderer(landBoundaryHighlightTilemapObject, sortingOrder: 1);

            GameObject liquidBaseTilemapObject =
                FindOrCreateDirectChild(gridObject.transform, "Liquid Base Tilemap");
            Tilemap liquidBaseTilemap = GetOrAddComponent<Tilemap>(liquidBaseTilemapObject);
            liquidBaseTilemapObject.transform.localPosition = new Vector3(
                0f,
                TerrainLayerPresentationUtility.LiquidSurfaceOffsetInCells,
                0f);
            ConfigureTilemapRenderer(liquidBaseTilemapObject, sortingOrder: -2);

            GameObject liquidAnimationTilemapObject =
                FindOrCreateDirectChild(gridObject.transform, "Liquid Surface Animation Tilemap");
            Tilemap liquidAnimationTilemap = GetOrAddComponent<Tilemap>(liquidAnimationTilemapObject);
            liquidAnimationTilemap.animationFrameRate = LiquidAnimationFramesPerSecond;
            liquidAnimationTilemapObject.transform.localPosition = new Vector3(
                0f,
                TerrainLayerPresentationUtility.LiquidSurfaceOffsetInCells,
                0f);
            ConfigureTilemapRenderer(liquidAnimationTilemapObject, sortingOrder: -1);

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

            GameObject rockWallBoundaryHighlightTilemapObject =
                FindOrCreateDirectChild(
                    gridObject.transform,
                    "Rock Wall Boundary Highlight Tilemap");
            Tilemap rockWallBoundaryHighlightTilemap =
                GetOrAddComponent<Tilemap>(rockWallBoundaryHighlightTilemapObject);
            rockWallBoundaryHighlightTilemapObject.transform.localPosition = new Vector3(
                0f,
                TerrainLayerPresentationUtility.RockWallElevationInCells,
                0f);
            // ハイライトを白のまま暗いWallへ重ねると輪郭だけが浮くため、Wall上面と同じ既定係数をRGBへ掛けます。
            // Tile自身の20% Alphaは維持するので、合成済みのWall全体を0.85倍した場合と同じ色関係になります。
            float wallBoundaryHighlightDarkenFactor =
                TerrainMaterialArtSource.DefaultWallDarkenFactor;
            rockWallBoundaryHighlightTilemap.color = new Color(
                wallBoundaryHighlightDarkenFactor,
                wallBoundaryHighlightDarkenFactor,
                wallBoundaryHighlightDarkenFactor,
                1f);
            ConfigureTilemapRenderer(
                rockWallBoundaryHighlightTilemapObject,
                sortingOrder: 4);

            // 直前方が岩壁の資源だけを壁より先に描き、岩壁Spriteと重なる下部を確実に隠します。
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
                TerrainLayerPresentationUtility.RockWallElevationInCells,
                0f);
            ConfigureTilemapRenderer(rockWallResourceTilemapObject, sortingOrder: 11);
            ConfigurePresenter(
                presenter,
                tilemap,
                resourceTilemap,
                resourceBehindRockWallTilemap,
                biomeTiles,
                magmaTile,
                freshWaterTile,
                saltWaterTile,
                rockWallTile,
                treeResourceTile,
                stoneResourceTile,
                sandResourceTile,
                liquidBaseTilemap,
                liquidAnimationTilemap,
                rockWallTilemap,
                rockWallResourceTilemap,
                landBoundaryHighlightTilemap,
                rockWallBoundaryHighlightTilemap,
                freshWaterAnimationTile,
                saltWaterAnimationTile,
                magmaAnimationTile,
                boundaryHighlightSprites);

            Camera camera = FindMainCamera(mainScene);
            CameraPanController panController = ConfigureCamera(camera, grid);
            presenter.ConfigureDisplayOrigin(panController);
            // 構築し直したSceneも採用済みの既定方式へそろえ、自動原点変更で全タイルを書き直さないようにします。
            presenter.ConfigureChunkLocalPresentation(true);
            EditorUtility.SetDirty(presenter);

            EditorSceneManager.MarkSceneDirty(mainScene);
            if (!EditorSceneManager.SaveScene(mainScene, MainScenePath))
            {
                throw new InvalidOperationException($"SampleSceneを保存できませんでした: {MainScenePath}");
            }
        }

        /// <summary>
        /// ChunkTilemapPresenterへ、地形・資源の専用Tilemapと一体型の仮Tileをまとめて設定します。
        /// </summary>
        /// <param name="presenter">設定対象のPresentation Componentです。</param>
        /// <param name="tilemap">生成地形だけを表示する専用Tilemapです。</param>
        /// <param name="resourceTilemap">岩壁に隠れない生成資源を、岩壁より前へ表示する専用Tilemapです。</param>
        /// <param name="resourceBehindRockWallTilemap">直前方の岩壁に隠す生成資源を、岩壁より後ろへ表示する専用Tilemapです。</param>
        /// <param name="biomeTiles">BiomeId 1〜18の順で並ぶ、識別用の仮土地Tileです。</param>
        /// <param name="magmaTile">マグマ地形用の単色Tileです。</param>
        /// <param name="freshWaterTile">川と池の淡水域用の単色Tileです。</param>
        /// <param name="saltWaterTile">海の海水域用の単色Tileです。</param>
        /// <param name="rockWallTile">全岩壁へ共通で使用し、別の影Meshで内部グラデーションを後付けする単色Tileです。</param>
        /// <param name="treeResourceTile">樹木資源用の透明背景Tileです。</param>
        /// <param name="stoneResourceTile">石資源用の透明背景Tileです。</param>
        /// <param name="sandResourceTile">砂資源用の透明背景Tileです。</param>
        /// <param name="liquidBaseTilemap">静止した液面を土地より8px下へ描画するTilemapです。</param>
        /// <param name="liquidAnimationTilemap">液面アニメーションを静止液面と同じ8px下へ描画するTilemapです。</param>
        /// <param name="rockWallTilemap">16×28の岩壁ブロックを基準セルへ描画するTilemapです。</param>
        /// <param name="rockWallResourceTilemap">露出した岩壁上の資源を12px上へ描画するTilemapです。</param>
        /// <param name="landBoundaryHighlightTilemap">通常土地の水・マグマ側へ境界を重ねるTilemapです。</param>
        /// <param name="rockWallBoundaryHighlightTilemap">岩壁上面の境界を12px高い位置へ重ねるTilemapです。</param>
        /// <param name="freshWaterAnimationTile">淡水へ重ねる透明AnimatedTileです。</param>
        /// <param name="saltWaterAnimationTile">海水へ重ねる透明AnimatedTileです。</param>
        /// <param name="magmaAnimationTile">マグマへ重ねる透明AnimatedTileです。</param>
        /// <param name="boundaryHighlightSprites">左からShape01〜Shape14の順に並ぶ境界ハイライトSpriteです。</param>
        private static void ConfigurePresenter(
            ChunkTilemapPresenter presenter,
            Tilemap tilemap,
            Tilemap resourceTilemap,
            Tilemap resourceBehindRockWallTilemap,
            TileBase[] biomeTiles,
            TileBase magmaTile,
            TileBase freshWaterTile,
            TileBase saltWaterTile,
            TileBase rockWallTile,
            TileBase treeResourceTile,
            TileBase stoneResourceTile,
            TileBase sandResourceTile,
            Tilemap liquidBaseTilemap,
            Tilemap liquidAnimationTilemap,
            Tilemap rockWallTilemap,
            Tilemap rockWallResourceTilemap,
            Tilemap landBoundaryHighlightTilemap,
            Tilemap rockWallBoundaryHighlightTilemap,
            TileBase freshWaterAnimationTile,
            TileBase saltWaterAnimationTile,
            TileBase magmaAnimationTile,
            Sprite[] boundaryHighlightSprites)
        {
            presenter.Configure(
                tilemap,
                resourceTilemap,
                biomeTiles,
                magmaTile,
                freshWaterTile,
                saltWaterTile,
                rockWallTile,
                treeResourceTile,
                stoneResourceTile,
                sandResourceTile);
            presenter.ConfigureResourceOcclusion(resourceBehindRockWallTilemap);
            presenter.ConfigureTerrainEffects(
                liquidBaseTilemap,
                liquidAnimationTilemap,
                rockWallTilemap,
                rockWallResourceTilemap,
                freshWaterAnimationTile,
                saltWaterAnimationTile,
                magmaAnimationTile);
            presenter.ConfigureBoundaryHighlights(
                landBoundaryHighlightTilemap,
                rockWallBoundaryHighlightTilemap,
                boundaryHighlightSprites,
                TerrainBoundaryHighlightUtility.DefaultOpacity);
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
            string[] tileNames =
            {
                "TemperateGrassland",
                "TropicalSavannaWoodland",
                "TemperateDeciduousForest",
                "WarmTemperateEvergreenForest",
                "TemperateMontaneBambooForest",
                "TropicalRainforest",
                "TropicalSeasonalForest",
                "SubtropicalOceanicForest",
                "BorealConiferForest",
                "Steppe",
                "DryShrublandWoodland",
                "CactusDesertDryPlateau",
                "HotDesert",
                "FreshwaterMarshFloodplain",
                "SwampForest",
                "TundraPolarBarrens",
                "AlpineRockland",
                "VolcanicGeothermal",
                "Magma",
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

            for (int index = 0; index < FallbackBiomeAssetNames.Length; index++)
            {
                ValidateFallbackSpriteGeometry(
                    FallbackBiomeAssetNames[index],
                    FallbackTilePixelSize,
                    TerrainLayerPresentationUtility.CombinedTerrainSpriteHeightInPixels,
                    new Vector2(0.5f, TerrainLayerPresentationUtility.CombinedTerrainPivotY));
            }

            ValidateFallbackSpriteGeometry(
                "RockWall",
                FallbackTilePixelSize,
                TerrainLayerPresentationUtility.CombinedRockWallSpriteHeightInPixels,
                new Vector2(0.5f, TerrainLayerPresentationUtility.CombinedRockWallPivotY));

            string[] animatedTileNames =
            {
                "FreshWaterAnimation",
                "SaltWaterAnimation",
                "MagmaAnimation"
            };
            for (int index = 0; index < animatedTileNames.Length; index++)
            {
                // LoadRequired側で4フレームすべてのSprite参照まで検証し、空のAnimatedTileをSceneへ保存しません。
                LoadRequiredFallbackAnimatedTile(animatedTileNames[index]);
            }

            Scene mainScene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            Camera camera = FindMainCamera(mainScene);
            GameObject gridObject = FindOrCreateRoot(mainScene, "World Grid");
            Transform tilemapTransform = gridObject.transform.Find("Ground Tilemap");
            Transform resourceTilemapTransform = gridObject.transform.Find("Resource Tilemap");
            Transform resourceBehindRockWallTilemapTransform =
                gridObject.transform.Find("Resource Behind Rock Wall Tilemap");
            Transform liquidBaseTilemapTransform =
                gridObject.transform.Find("Liquid Base Tilemap");
            Transform liquidAnimationTilemapTransform =
                gridObject.transform.Find("Liquid Surface Animation Tilemap");
            Transform rockWallTilemapTransform =
                gridObject.transform.Find("Rock Wall Tilemap");
            Transform rockWallResourceTilemapTransform =
                gridObject.transform.Find("Rock Wall Resource Tilemap");
            Transform landBoundaryHighlightTilemapTransform =
                gridObject.transform.Find("Land Boundary Highlight Tilemap");
            Transform rockWallBoundaryHighlightTilemapTransform =
                gridObject.transform.Find("Rock Wall Boundary Highlight Tilemap");
            GameObject subSceneObject = FindOrCreateRoot(mainScene, "World Generation SubScene");

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
                || landBoundaryHighlightTilemapTransform == null
                || landBoundaryHighlightTilemapTransform.GetComponent<Tilemap>() == null
                || landBoundaryHighlightTilemapTransform.GetComponent<TilemapRenderer>() == null
                || landBoundaryHighlightTilemapTransform.GetComponent<TilemapRenderer>().sortingOrder
                    != 1
                || !Mathf.Approximately(
                    landBoundaryHighlightTilemapTransform.localPosition.y,
                    0f)
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
                    TerrainLayerPresentationUtility.LiquidSurfaceOffsetInCells)
                || liquidAnimationTilemapTransform == null
                || liquidAnimationTilemapTransform.GetComponent<Tilemap>() == null
                || liquidAnimationTilemapTransform.GetComponent<TilemapRenderer>() == null
                || !Mathf.Approximately(
                    liquidAnimationTilemapTransform.localPosition.y,
                    TerrainLayerPresentationUtility.LiquidSurfaceOffsetInCells)
                || gridObject.transform.Find("Terrain Front Edge Tilemap") != null
                || gridObject.transform.Find("Rock Wall Front Face Tilemap") != null
                || gridObject.transform.Find("Rock Wall Top Tilemap") != null
                || rockWallTilemapTransform == null
                || rockWallTilemapTransform.GetComponent<Tilemap>() == null
                || rockWallTilemapTransform.GetComponent<TilemapRenderer>() == null
                || rockWallTilemapTransform.GetComponent<TilemapRenderer>().mode
                    != TilemapRenderer.Mode.Individual
                || rockWallTilemapTransform.GetComponent<TilemapRenderer>().sortOrder
                    != TilemapRenderer.SortOrder.TopLeft
                || resourceBehindRockWallTilemapTransform.GetComponent<TilemapRenderer>().sortingOrder
                    >= rockWallTilemapTransform.GetComponent<TilemapRenderer>().sortingOrder
                || resourceTilemapTransform.GetComponent<TilemapRenderer>().sortingOrder
                    <= rockWallTilemapTransform.GetComponent<TilemapRenderer>().sortingOrder
                || !Mathf.Approximately(
                    rockWallTilemapTransform.localPosition.y,
                    0f)
                || rockWallBoundaryHighlightTilemapTransform == null
                || rockWallBoundaryHighlightTilemapTransform.GetComponent<Tilemap>() == null
                || rockWallBoundaryHighlightTilemapTransform.GetComponent<TilemapRenderer>() == null
                || rockWallBoundaryHighlightTilemapTransform.GetComponent<TilemapRenderer>().sortingOrder
                    != 4
                || !Mathf.Approximately(
                    rockWallBoundaryHighlightTilemapTransform.localPosition.y,
                    TerrainLayerPresentationUtility.RockWallElevationInCells)
                || !Mathf.Approximately(
                    rockWallBoundaryHighlightTilemapTransform.GetComponent<Tilemap>().color.r,
                    TerrainMaterialArtSource.DefaultWallDarkenFactor)
                || !Mathf.Approximately(
                    rockWallBoundaryHighlightTilemapTransform.GetComponent<Tilemap>().color.g,
                    TerrainMaterialArtSource.DefaultWallDarkenFactor)
                || !Mathf.Approximately(
                    rockWallBoundaryHighlightTilemapTransform.GetComponent<Tilemap>().color.b,
                    TerrainMaterialArtSource.DefaultWallDarkenFactor)
                || !Mathf.Approximately(
                    rockWallBoundaryHighlightTilemapTransform.GetComponent<Tilemap>().color.a,
                    1f)
                || rockWallResourceTilemapTransform == null
                || rockWallResourceTilemapTransform.GetComponent<Tilemap>() == null
                || rockWallResourceTilemapTransform.GetComponent<TilemapRenderer>() == null
                || !Mathf.Approximately(
                    rockWallResourceTilemapTransform.localPosition.y,
                    TerrainLayerPresentationUtility.RockWallElevationInCells)
                || !presenterReferencesAreValid
                || subSceneObject.GetComponent<SubScene>() == null
                || subSceneObject.GetComponent<SubScene>().SceneAsset == null)
            {
                throw new InvalidOperationException("SampleSceneのマップ生成Component接続を検証できませんでした。");
            }
        }

        /// <summary>
        /// 保存済みフォールバックSpriteの画像寸法とPivotを読み戻して検証します。
        /// </summary>
        /// <param name="assetName">拡張子を除いたSprite名です。</param>
        /// <param name="expectedWidth">期待する画像幅です。単位はピクセルです。</param>
        /// <param name="expectedHeight">期待する画像高です。単位はピクセルです。</param>
        /// <param name="expectedNormalizedPivot">画像左下を0、右上を1とする期待Pivotです。</param>
        private static void ValidateFallbackSpriteGeometry(
            string assetName,
            int expectedWidth,
            int expectedHeight,
            Vector2 expectedNormalizedPivot)
        {
            string texturePath = $"{FallbackAssetFolder}/{assetName}.png";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
            if (sprite == null
                || !Mathf.Approximately(sprite.rect.width, expectedWidth)
                || !Mathf.Approximately(sprite.rect.height, expectedHeight))
            {
                throw new InvalidOperationException(
                    $"フォールバックSpriteの寸法が不正です: {texturePath}");
            }

            Vector2 expectedPixelPivot = new Vector2(
                expectedWidth * expectedNormalizedPivot.x,
                expectedHeight * expectedNormalizedPivot.y);
            if (!Mathf.Approximately(sprite.pivot.x, expectedPixelPivot.x)
                || !Mathf.Approximately(sprite.pivot.y, expectedPixelPivot.y))
            {
                throw new InvalidOperationException(
                    $"フォールバックSpriteのPivotが不正です: {texturePath}");
            }
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
        /// <returns>必要な地形・断面・アニメーション参照がすべて非nullならtrueです。</returns>
        private static bool PresenterHasAllReferences(ChunkTilemapPresenter presenter)
        {
            SerializedObject serializedPresenter = new SerializedObject(presenter);
            string[] propertyNames =
            {
                "targetTilemap",
                "resourceTilemap",
                "resourceBehindRockWallTilemap",
                "liquidBaseTilemap",
                "liquidAnimationTilemap",
                "rockWallTilemap",
                "rockWallResourceTilemap",
                "landBoundaryHighlightTilemap",
                "rockWallBoundaryHighlightTilemap",
                "cameraPanController",
                "magmaTile",
                "freshWaterTile",
                "saltWaterTile",
                "rockWallTile",
                "treeResourceTile",
                "stoneResourceTile",
                "sandResourceTile",
                "freshWaterAnimationTile",
                "saltWaterAnimationTile",
                "magmaAnimationTile"
            };

            for (int index = 0; index < propertyNames.Length; index++)
            {
                SerializedProperty property = serializedPresenter.FindProperty(propertyNames[index]);
                if (property == null || property.objectReferenceValue == null)
                {
                    return false;
                }
            }

            SerializedProperty biomeTilesProperty = serializedPresenter.FindProperty("biomeTiles");
            if (biomeTilesProperty == null
                || !biomeTilesProperty.isArray
                || biomeTilesProperty.arraySize != BiomeStableIds.Count)
            {
                return false;
            }

            // 新18種のどれか1枚でも欠けた場合は旧Tileへフォールバックせず、Scene構築自体を失敗させます。
            for (int index = 0; index < biomeTilesProperty.arraySize; index++)
            {
                SerializedProperty biomeTileProperty = biomeTilesProperty.GetArrayElementAtIndex(index);
                if (biomeTileProperty.objectReferenceValue == null)
                {
                    return false;
                }
            }

            SerializedProperty boundarySpritesProperty =
                serializedPresenter.FindProperty("boundaryHighlightSprites");
            if (boundarySpritesProperty == null
                || !boundarySpritesProperty.isArray
                || boundarySpritesProperty.arraySize
                    != TerrainBoundaryHighlightUtility.ExpectedSpriteCount)
            {
                return false;
            }

            for (int index = 0; index < boundarySpritesProperty.arraySize; index++)
            {
                SerializedProperty spriteProperty =
                    boundarySpritesProperty.GetArrayElementAtIndex(index);
                if (spriteProperty.objectReferenceValue == null)
                {
                    return false;
                }
            }

            SerializedProperty boundaryOpacityProperty =
                serializedPresenter.FindProperty("boundaryHighlightOpacity");
            if (boundaryOpacityProperty == null
                || !Mathf.Approximately(
                    boundaryOpacityProperty.floatValue,
                    TerrainBoundaryHighlightUtility.DefaultOpacity))
            {
                return false;
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
