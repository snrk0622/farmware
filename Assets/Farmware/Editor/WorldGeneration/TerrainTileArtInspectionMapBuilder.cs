// このファイルは、GrassLoamの地形材質Spriteと仮の液体表現を固定配置した検品Sceneを再現可能に構築します。
// 地形Simulationには接続せず、3段で黒へ溶ける崖、4段目からの海中床、連続水深、低い水面、U字の水際波線をGameビューで確認するEditor専用ツールです。

using System;
using System.Collections.Generic;
using System.IO;
using Farmware.WorldGeneration.Authoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 固定検品Sceneへ読み込めたGrassLoam素材の枚数を表します。
    /// EditModeテストとScene構築前検証が同じ素材集合を確認するために使用します。
    /// </summary>
    public readonly struct TerrainTileArtInspectionAssetSummary
    {
        /// <summary>読み込めたGroundTop差分数を返します。</summary>
        public int GroundTopCount { get; }

        /// <summary>読み込めた通常Face差分数を返します。</summary>
        public int FaceCount { get; }

        /// <summary>読み込めたGround用FaceBoundary合成結果数を返します。</summary>
        public int GroundBoundaryCount { get; }

        /// <summary>読み込めたWall用FaceBoundary合成結果数を返します。</summary>
        public int WallBoundaryCount { get; }

        /// <summary>読み込めた仮液体Sprite数を返します。</summary>
        public int LiquidPlaceholderCount { get; }

        /// <summary>構築前検証で確認した各用途のSprite数をまとめます。</summary>
        /// <param name="groundTopCount">GroundTop差分数です。</param>
        /// <param name="faceCount">通常Face差分数です。</param>
        /// <param name="groundBoundaryCount">Ground用境界合成結果数です。</param>
        /// <param name="wallBoundaryCount">Wall用境界合成結果数です。</param>
        /// <param name="liquidPlaceholderCount">淡水、海水、マグマの仮Sprite数です。</param>
        public TerrainTileArtInspectionAssetSummary(
            int groundTopCount,
            int faceCount,
            int groundBoundaryCount,
            int wallBoundaryCount,
            int liquidPlaceholderCount)
        {
            GroundTopCount = groundTopCount;
            FaceCount = faceCount;
            GroundBoundaryCount = groundBoundaryCount;
            WallBoundaryCount = wallBoundaryCount;
            LiquidPlaceholderCount = liquidPlaceholderCount;
        }
    }

    /// <summary>
    /// GrassLoamの反復、Ground上へ重ねたWall、断面段数、Ground／Wall境界、液体被覆を固定配置します。
    /// Wall上面は専用Spriteを使わず、GroundTopと同じSpriteへ材質設定のTintを掛けます。
    /// </summary>
    public static class TerrainTileArtInspectionMapBuilder
    {
        /// <summary>検品対象の地形タイル生成設定アセットです。</summary>
        public const string SettingsAssetPath =
            "Assets/Farmware/Art/WorldGeneration/Terrain/Settings/TerrainTileArtGenerationSettings.asset";

        /// <summary>再構築する固定検品Sceneの保存先です。</summary>
        public const string InspectionScenePath =
            "Assets/Scenes/Development/TerrainTileArtInspection.unity";

        /// <summary>壁暗化係数だけを横並び比較するSceneの保存先です。</summary>
        public const string WallSurfaceDarkenComparisonScenePath =
            "Assets/Scenes/Development/TerrainWallSurfaceDarkenComparison.unity";

        /// <summary>固定検品の対象とする安定材質IDです。</summary>
        public const string InspectedMaterialId = "GrassLoam";

        /// <summary>淡水の仮表示に使う既存Fallback Spriteです。</summary>
        public const string FreshWaterPlaceholderPath =
            "Assets/Farmware/Art/WorldGeneration/Fallback/FreshWater.png";

        /// <summary>海水の仮表示に使う既存Fallback Spriteです。</summary>
        public const string SaltWaterPlaceholderPath =
            "Assets/Farmware/Art/WorldGeneration/Fallback/SaltWater.png";

        /// <summary>マグマの仮表示に使う既存Fallback Spriteです。</summary>
        public const string MagmaPlaceholderPath =
            "Assets/Farmware/Art/WorldGeneration/Fallback/Magma.png";

        /// <summary>海中床に置かれた物体が水越しに見えることを示す仮Spriteです。</summary>
        public const string UnderwaterObjectPlaceholderPath =
            "Assets/Farmware/Art/WorldGeneration/Fallback/StoneResource.png";

        /// <summary>乾燥崖と液体の連続深度勾配を描く検品用Shaderです。</summary>
        public const string ContinuousDepthOverlayShaderPath =
            "Assets/Farmware/Shaders/Development/ContinuousDepthOverlay.shader";

        /// <summary>水際の1px線へ2色の流れを付ける検品用Shaderです。</summary>
        public const string AnimatedWaterEdgeWaveShaderPath =
            "Assets/Farmware/Shaders/Development/AnimatedWaterEdgeWave.shader";

        /// <summary>検品用に自動生成する深度勾配と水際波線のMaterialをまとめるフォルダーです。</summary>
        public const string GeneratedPreviewAssetFolder =
            "Assets/Farmware/Art/WorldGeneration/Generated/Inspection";

        /// <summary>Ground崖面を縦へ反復して確認する段数です。</summary>
        public const int GroundFaceDepth = 3;

        /// <summary>乾燥崖のFace表示が完全に黒へ溶けるまでの画素距離です。</summary>
        public const int DryCliffFadePixels = GroundFaceDepth * 16;

        /// <summary>本番想定のWall前面を縦へ反復して確認する段数です。</summary>
        public const int WallFaceDepth = 2;

        /// <summary>実景検品用の穴を横へ掘るセル数です。</summary>
        public const int InspectionHoleWidth = 3;

        /// <summary>実景検品用の穴を奥から手前へ延ばすセル数です。</summary>
        public const int InspectionHoleDepth = 10;

        /// <summary>地上から見た海中床が始まる0始まりの奥行き位置です。値3は4段目を表します。</summary>
        public const int UnderwaterPreviewStartDepth = GroundFaceDepth;

        /// <summary>淡水・海水の水面をGroundTopより下げる画素数です。</summary>
        public const int WaterSurfaceOffsetPixels = 3;

        /// <summary>直下Underwater床がある水域で、最大Alphaへ到達する実表示距離です。</summary>
        public const int UnderwaterFloorWaterFadePixels = 45;

        /// <summary>直下Underwater床がない水域で、最大Alphaへ到達する実表示距離です。</summary>
        public const int MissingUnderwaterWaterFadePixels = 93;

        /// <summary>直下Underwater床がない水域で、現在材質のFaceを下地へ続ける段数です。</summary>
        public const int MissingUnderwaterVisibleFaceDepth = 6;

        /// <summary>直下Underwater床がある水域の青Overlay最大Alphaです。</summary>
        public const float UnderwaterFloorWaterMaximumAlpha = 0.50f;

        /// <summary>直下Underwater床がない水域の青Overlay最大Alphaです。</summary>
        public const float MissingUnderwaterWaterMaximumAlpha = 1.00f;

        /// <summary>水際の波線を構成する太さです。単位は画素です。</summary>
        public const int WaterEdgeWaveThicknessPixels = 1;

        /// <summary>水際の波線内を流れる色数です。</summary>
        public const int WaterEdgeWaveColorCount = 2;

        // Pixel Perfect Cameraと全Spriteで共有する基準値です。
        private const int AssetsPixelsPerUnit = 16;
        private const int ReferenceResolutionX = 480;
        private const int ReferenceResolutionY = 270;

        // GroundTop反復感を見る矩形と、Wallパッチの寸法です。
        private const int GroundRepeatWidth = 12;
        private const int GroundRepeatHeight = 7;
        private const int WallPatchWidth = 5;
        private const int WallPatchHeight = 3;

        // 検品に読み込む仮液体は淡水、海水、マグマの3種類です。
        private const int LiquidPlaceholderCount = 3;

        // 穴の外周へ1セル幅のGroundTopを残し、「地面の中を掘った穴」と読める輪郭を作ります。
        private const int InspectionHoleBorderWidth = 1;

        // 仮波線は2pxごとの2色帯を、毎秒8pxの整数ステップで流します。
        // 滑らかな補間を行わず、ドット絵の1px境界を維持します。
        private const float WaterEdgeWavePixelsPerSecond = 8f;

        // 3×10セルの1枚絵として描くため、Materialへ渡す距離は全高160pxに対する比率へ変換します。
        private const int InspectionHoleHeightPixels = InspectionHoleDepth * AssetsPixelsPerUnit;

        // 現在値相当、強め、かなり強めを同じ配置で比較します。
        private static readonly float[] WallSurfaceDarkenComparisonFactors =
        {
            0.85f,
            0.70f,
            0.55f
        };

        /// <summary>暗化比較Sceneへ横並びにする比較条件数を返します。</summary>
        public static int WallSurfaceDarkenComparisonCount =>
            WallSurfaceDarkenComparisonFactors.Length;

        /// <summary>穴の4段目以降へ何を表示するかを表します。</summary>
        private enum HoleDepthPreviewKind
        {
            /// <summary>液体のない穴を3段の崖と、4段目からの完全な暗黒で表示します。</summary>
            Dry,

            /// <summary>直下のUnderwaterに存在する地面と物体を水越しに表示します。</summary>
            UnderwaterFloor,

            /// <summary>Underwaterに地面がなくてもDeepSeaを透視せず、6段で不透明になる青Overlayで遮蔽します。</summary>
            HiddenDeepSea,

            /// <summary>不透明なマグマを最初の段から表示します。</summary>
            Magma
        }

        /// <summary>Scene構築中だけ保持するGrassLoam素材一式です。</summary>
        private sealed class InspectionAssets
        {
            /// <summary>GroundとWall上面で共有する差分です。</summary>
            public Sprite[] GroundTopSprites { get; }

            /// <summary>Ground崖面とWall前面で共有する通常Faceです。</summary>
            public Sprite[] FaceSprites { get; }

            /// <summary>Ground用境界合成結果です。</summary>
            public Sprite[] GroundBoundarySprites { get; }

            /// <summary>Wall用境界合成結果です。</summary>
            public Sprite[] WallBoundarySprites { get; }

            /// <summary>淡水の半透明な深度被覆へ一時利用するFallback Spriteです。</summary>
            public Sprite FreshWaterPlaceholder { get; }

            /// <summary>海水の半透明な深度被覆へ一時利用するFallback Spriteです。</summary>
            public Sprite SaltWaterPlaceholder { get; }

            /// <summary>接触段からFaceを隠す不透明マグマへ一時利用するFallback Spriteです。</summary>
            public Sprite MagmaPlaceholder { get; }

            /// <summary>海中床の配置物が地上から見えることを示す仮Spriteです。</summary>
            public Sprite UnderwaterObjectPlaceholder { get; }

            /// <summary>Wall上面へ掛けるRGB乗算係数です。</summary>
            public float WallDarkenFactor { get; }

            /// <summary>検証済み素材と壁暗化設定をScene構築用にまとめます。</summary>
            /// <param name="groundTopSprites">GroundとWall上面で共有する差分です。</param>
            /// <param name="faceSprites">共通Face差分です。</param>
            /// <param name="groundBoundarySprites">Ground用境界合成結果です。</param>
            /// <param name="wallBoundarySprites">Wall用境界合成結果です。</param>
            /// <param name="freshWaterPlaceholder">淡水の仮Spriteです。</param>
            /// <param name="saltWaterPlaceholder">海水の仮Spriteです。</param>
            /// <param name="magmaPlaceholder">マグマの仮Spriteです。</param>
            /// <param name="underwaterObjectPlaceholder">海中床の仮配置物Spriteです。</param>
            /// <param name="wallDarkenFactor">Wall上面のRGB乗算係数です。</param>
            public InspectionAssets(
                Sprite[] groundTopSprites,
                Sprite[] faceSprites,
                Sprite[] groundBoundarySprites,
                Sprite[] wallBoundarySprites,
                Sprite freshWaterPlaceholder,
                Sprite saltWaterPlaceholder,
                Sprite magmaPlaceholder,
                Sprite underwaterObjectPlaceholder,
                float wallDarkenFactor)
            {
                GroundTopSprites = groundTopSprites;
                FaceSprites = faceSprites;
                GroundBoundarySprites = groundBoundarySprites;
                WallBoundarySprites = wallBoundarySprites;
                FreshWaterPlaceholder = freshWaterPlaceholder;
                SaltWaterPlaceholder = saltWaterPlaceholder;
                MagmaPlaceholder = magmaPlaceholder;
                UnderwaterObjectPlaceholder = underwaterObjectPlaceholder;
                WallDarkenFactor = wallDarkenFactor;
            }
        }

        /// <summary>
        /// Scene構築時に自動生成する連続深度Materialと、水際の仮波線Materialをまとめます。
        /// 完成アートとは分離し、検品用素材を本番の材質入力として誤利用しないようにします。
        /// </summary>
        private sealed class GeneratedPreviewAssets
        {
            /// <summary>乾燥崖のFaceを3段で黒へ溶かす連続勾配Materialです。</summary>
            public Material DryCliffFadeMaterial { get; }

            /// <summary>淡水かつ直下Underwater床ありの連続勾配Materialです。</summary>
            public Material FreshWaterWithFloorMaterial { get; }

            /// <summary>海水かつ直下Underwater床ありの連続勾配Materialです。</summary>
            public Material SaltWaterWithFloorMaterial { get; }

            /// <summary>直下Underwater床なしの海水を6段で遮蔽する連続勾配Materialです。</summary>
            public Material SaltWaterWithoutFloorMaterial { get; }

            /// <summary>淡水の奥辺へ使う横方向の2色波線Materialです。</summary>
            public Material FreshWaterHorizontalWaveMaterial { get; }

            /// <summary>淡水の左右辺へ使う縦方向の2色波線Materialです。</summary>
            public Material FreshWaterVerticalWaveMaterial { get; }

            /// <summary>海水の奥辺へ使う横方向の2色波線Materialです。</summary>
            public Material SaltWaterHorizontalWaveMaterial { get; }

            /// <summary>海水の左右辺へ使う縦方向の2色波線Materialです。</summary>
            public Material SaltWaterVerticalWaveMaterial { get; }

            /// <summary>生成済みの検品用表示素材をScene構築へ渡します。</summary>
            /// <param name="dryCliffFadeMaterial">乾燥崖の黒勾配Materialです。</param>
            /// <param name="freshWaterWithFloorMaterial">淡水・海中床あり用Materialです。</param>
            /// <param name="saltWaterWithFloorMaterial">海水・海中床あり用Materialです。</param>
            /// <param name="saltWaterWithoutFloorMaterial">海水・海中床なし用Materialです。</param>
            /// <param name="freshWaterHorizontalWaveMaterial">淡水の奥辺用波線Materialです。</param>
            /// <param name="freshWaterVerticalWaveMaterial">淡水の左右辺用波線Materialです。</param>
            /// <param name="saltWaterHorizontalWaveMaterial">海水の奥辺用波線Materialです。</param>
            /// <param name="saltWaterVerticalWaveMaterial">海水の左右辺用波線Materialです。</param>
            public GeneratedPreviewAssets(
                Material dryCliffFadeMaterial,
                Material freshWaterWithFloorMaterial,
                Material saltWaterWithFloorMaterial,
                Material saltWaterWithoutFloorMaterial,
                Material freshWaterHorizontalWaveMaterial,
                Material freshWaterVerticalWaveMaterial,
                Material saltWaterHorizontalWaveMaterial,
                Material saltWaterVerticalWaveMaterial)
            {
                DryCliffFadeMaterial = dryCliffFadeMaterial;
                FreshWaterWithFloorMaterial = freshWaterWithFloorMaterial;
                SaltWaterWithFloorMaterial = saltWaterWithFloorMaterial;
                SaltWaterWithoutFloorMaterial = saltWaterWithoutFloorMaterial;
                FreshWaterHorizontalWaveMaterial = freshWaterHorizontalWaveMaterial;
                FreshWaterVerticalWaveMaterial = freshWaterVerticalWaveMaterial;
                SaltWaterHorizontalWaveMaterial = saltWaterHorizontalWaveMaterial;
                SaltWaterVerticalWaveMaterial = saltWaterVerticalWaveMaterial;
            }
        }

        /// <summary>未保存Sceneを保護したうえで、GrassLoam固定検品Sceneを再構築して開きます。</summary>
        [MenuItem("Farmware/World Generation/Open Terrain Tile Inspection Map")]
        public static void BuildAndOpenFromMenu()
        {
            BuildFromMenu(false);
        }

        /// <summary>未保存Sceneを保護したうえで、壁暗化係数の比較Sceneを再構築して開きます。</summary>
        [MenuItem("Farmware/World Generation/Open Wall Surface Darken Comparison")]
        public static void BuildWallSurfaceDarkenComparisonFromMenu()
        {
            BuildFromMenu(true);
        }

        /// <summary>CLIや自動処理から固定検品Sceneを構築する入口です。</summary>
        public static void Execute()
        {
            BuildAndOpenScene(LoadAndValidateAssets(), false);
        }

        /// <summary>CLIや自動処理から壁暗化比較Sceneを構築する入口です。</summary>
        public static void ExecuteWallSurfaceDarkenComparison()
        {
            BuildAndOpenScene(LoadAndValidateAssets(), true);
        }

        /// <summary>壁暗化比較Sceneで使用する係数をIndexから返します。</summary>
        /// <param name="index">0始まりの比較条件位置です。</param>
        /// <returns>指定位置のRGB乗算係数です。</returns>
        public static float GetWallSurfaceDarkenComparisonFactor(int index)
        {
            if (index < 0 || index >= WallSurfaceDarkenComparisonFactors.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return WallSurfaceDarkenComparisonFactors[index];
        }

        /// <summary>GrassLoamの入力と生成物を読み、Sceneを変更せず枚数だけ返します。</summary>
        /// <returns>検証済みの用途別Sprite数です。</returns>
        public static TerrainTileArtInspectionAssetSummary ValidateGrassLoamAssets()
        {
            InspectionAssets assets = LoadAndValidateAssets();
            return new TerrainTileArtInspectionAssetSummary(
                assets.GroundTopSprites.Length,
                assets.FaceSprites.Length,
                assets.GroundBoundarySprites.Length,
                assets.WallBoundarySprites.Length,
                LiquidPlaceholderCount);
        }

        /// <summary>水面上端からの画素深度に対応する連続青OverlayのAlphaを返します。</summary>
        /// <param name="depthFromGridTopPixels">穴上端から下方向へ測った画素距離です。</param>
        /// <param name="hasUnderwaterFloor">直下Underwater床がある場合はtrueです。</param>
        /// <returns>上端3pxでは0、指定距離で最大値へ到達する0以上1以下のAlphaです。</returns>
        public static float GetContinuousWaterOverlayAlpha(
            float depthFromGridTopPixels,
            bool hasUnderwaterFloor)
        {
            if (float.IsNaN(depthFromGridTopPixels)
                || float.IsInfinity(depthFromGridTopPixels)
                || depthFromGridTopPixels < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(depthFromGridTopPixels));
            }

            // 水面を3px低く見せる領域では青Overlayを描きません。
            // タイルを物理的にずらさず、この差し引きだけで水面位置を表すため重複Alphaが発生しません。
            float visibleWaterDepth = Mathf.Max(
                0f,
                depthFromGridTopPixels - WaterSurfaceOffsetPixels);
            float fadePixels = hasUnderwaterFloor
                ? UnderwaterFloorWaterFadePixels
                : MissingUnderwaterWaterFadePixels;
            float maximumAlpha = hasUnderwaterFloor
                ? UnderwaterFloorWaterMaximumAlpha
                : MissingUnderwaterWaterMaximumAlpha;
            return Mathf.Clamp01(visibleWaterDepth / fadePixels) * maximumAlpha;
        }

        /// <summary>穴上端からの画素深度に対応する乾燥崖Faceの可視Alphaを返します。</summary>
        /// <param name="depthFromGridTopPixels">穴上端から下方向へ測った画素距離です。</param>
        /// <returns>上端で1、48px地点以降で0になる連続Alphaです。</returns>
        public static float GetContinuousDryCliffFaceAlpha(float depthFromGridTopPixels)
        {
            if (float.IsNaN(depthFromGridTopPixels)
                || float.IsInfinity(depthFromGridTopPixels)
                || depthFromGridTopPixels < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(depthFromGridTopPixels));
            }

            // 黒OverlayのAlphaを0から1へ増やす表示の逆数が、背後にあるFaceの見える割合になります。
            // 3枚のFaceを個別に薄くせず穴全体の1枚絵で計算するため、タイル境界に段差を作りません。
            return 1f - Mathf.Clamp01(depthFromGridTopPixels / DryCliffFadePixels);
        }

        /// <summary>メニュー操作時だけ未保存Sceneを確認してから構築を実行します。</summary>
        /// <param name="comparisonOnly">暗化比較Sceneを作る場合はtrueです。</param>
        private static void BuildFromMenu(bool comparisonOnly)
        {
            InspectionAssets assets;
            try
            {
                assets = LoadAndValidateAssets();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "地形タイル検品Sceneを構築できません",
                    exception.Message,
                    "閉じる");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            SceneSetup[] originalSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                BuildAndOpenScene(assets, comparisonOnly);
            }
            catch (Exception exception)
            {
                EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "地形タイル検品Sceneを構築できません",
                    exception.Message,
                    "閉じる");
            }
        }

        /// <summary>設定アセットから対象材質と生成済み境界Spriteを読み込み、件数と参照を検証します。</summary>
        /// <returns>Scene構築へそのまま使える検証済み素材です。</returns>
        private static InspectionAssets LoadAndValidateAssets()
        {
            TerrainTileArtGenerationSettings settings =
                AssetDatabase.LoadAssetAtPath<TerrainTileArtGenerationSettings>(SettingsAssetPath);
            if (settings == null)
            {
                throw new InvalidOperationException(
                    $"地形タイル生成設定が見つかりません: {SettingsAssetPath}");
            }

            TerrainMaterialArtSource material = FindMaterial(settings, InspectedMaterialId);
            if (material == null)
            {
                throw new InvalidOperationException(
                    $"Materialsに{InspectedMaterialId}がありません。");
            }

            TerrainTileArtValidationReport report =
                TerrainTileArtGenerator.ValidateMaterial(settings, FindMaterialIndex(settings, material));
            if (report.HasErrors)
            {
                throw new InvalidOperationException(
                    "GrassLoamの生成入力が不正です。先に生成設定Inspectorで検証してください。\n- "
                    + string.Join("\n- ", report.Errors));
            }

            List<Sprite> groundBoundary = new List<Sprite>();
            List<Sprite> wallBoundary = new List<Sprite>();
            IReadOnlyList<string> paths = material.LastGeneratedAssetPaths;
            for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++)
            {
                string path = paths[pathIndex];
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                {
                    continue;
                }

                if (path.Contains("_GroundFace_", StringComparison.Ordinal))
                {
                    groundBoundary.Add(sprite);
                }
                else if (path.Contains("_WallFace_", StringComparison.Ordinal))
                {
                    wallBoundary.Add(sprite);
                }
            }

            int expectedBoundaryCount =
                material.FaceSprites.Length * material.FaceBoundaryOverlays.Length;
            if (groundBoundary.Count != expectedBoundaryCount)
            {
                throw new InvalidOperationException(
                    $"Ground境界生成物は{expectedBoundaryCount}枚必要ですが、{groundBoundary.Count}枚でした。対象材質を再生成してください。");
            }

            if (material.DarkenWallSurface && wallBoundary.Count != expectedBoundaryCount)
            {
                throw new InvalidOperationException(
                    $"Wall境界生成物は{expectedBoundaryCount}枚必要ですが、{wallBoundary.Count}枚でした。対象材質を再生成してください。");
            }

            // 暗化なしではWallがGround境界を再利用するため、Scene側も同じ参照を使います。
            Sprite[] wallBoundarySprites = material.DarkenWallSurface
                ? wallBoundary.ToArray()
                : groundBoundary.ToArray();

            // 液体専用の完成アートは後続制作のため、現段階では既存Fallbackを仮Spriteとして使います。
            // 3種類を明示的に検証し、欠けたままSceneを作って比較結果を誤認しないようにします。
            Sprite freshWaterPlaceholder = LoadRequiredSprite(FreshWaterPlaceholderPath, "淡水");
            Sprite saltWaterPlaceholder = LoadRequiredSprite(SaltWaterPlaceholderPath, "海水");
            Sprite magmaPlaceholder = LoadRequiredSprite(MagmaPlaceholderPath, "マグマ");
            Sprite underwaterObjectPlaceholder = LoadRequiredSprite(
                UnderwaterObjectPlaceholderPath,
                "海中床の配置物");
            return new InspectionAssets(
                material.GroundTopSprites,
                material.FaceSprites,
                groundBoundary.ToArray(),
                wallBoundarySprites,
                freshWaterPlaceholder,
                saltWaterPlaceholder,
                magmaPlaceholder,
                underwaterObjectPlaceholder,
                material.DarkenWallSurface ? material.WallDarkenFactor : 1f);
        }

        /// <summary>指定パスから必須Spriteを読み込み、未Importなら理由付きで停止します。</summary>
        /// <param name="assetPath">Assetsから始まるSpriteのパスです。</param>
        /// <param name="displayName">エラーメッセージへ表示する用途名です。</param>
        /// <returns>Sceneへ配置できるImport済みSpriteです。</returns>
        private static Sprite LoadRequiredSprite(string assetPath, string displayName)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null)
            {
                throw new InvalidOperationException(
                    $"{displayName}の仮Spriteを読み込めません: {assetPath}");
            }

            return sprite;
        }

        /// <summary>連続深度Materialと、奥・左右へ使う2色の仮波線Materialを生成または更新します。</summary>
        /// <returns>Sceneへ永続参照できる生成済みAsset一式です。</returns>
        private static GeneratedPreviewAssets EnsureGeneratedPreviewAssets()
        {
            EnsureAssetFolder(GeneratedPreviewAssetFolder);
            AssetDatabase.ImportAsset(
                ContinuousDepthOverlayShaderPath,
                ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(
                AnimatedWaterEdgeWaveShaderPath,
                ImportAssetOptions.ForceUpdate);
            Shader depthShader = AssetDatabase.LoadAssetAtPath<Shader>(
                ContinuousDepthOverlayShaderPath);
            if (depthShader == null)
            {
                throw new InvalidOperationException(
                    $"連続深度Shaderを読み込めません: {ContinuousDepthOverlayShaderPath}");
            }

            Shader waveShader = AssetDatabase.LoadAssetAtPath<Shader>(
                AnimatedWaterEdgeWaveShaderPath);
            if (waveShader == null)
            {
                throw new InvalidOperationException(
                    $"水際波線Shaderを読み込めません: {AnimatedWaterEdgeWaveShaderPath}");
            }

            Material dryCliffFade = EnsureContinuousDepthMaterial(
                depthShader,
                GeneratedPreviewAssetFolder + "/DryCliffFade.mat",
                Color.black,
                0,
                DryCliffFadePixels,
                1f);
            Material freshWaterWithFloor = EnsureContinuousDepthMaterial(
                depthShader,
                GeneratedPreviewAssetFolder + "/FreshWaterWithFloor.mat",
                new Color32(54, 150, 191, 255),
                WaterSurfaceOffsetPixels,
                UnderwaterFloorWaterFadePixels,
                UnderwaterFloorWaterMaximumAlpha);
            Material saltWaterWithFloor = EnsureContinuousDepthMaterial(
                depthShader,
                GeneratedPreviewAssetFolder + "/SaltWaterWithFloor.mat",
                new Color32(46, 112, 178, 255),
                WaterSurfaceOffsetPixels,
                UnderwaterFloorWaterFadePixels,
                UnderwaterFloorWaterMaximumAlpha);
            Material saltWaterWithoutFloor = EnsureContinuousDepthMaterial(
                depthShader,
                GeneratedPreviewAssetFolder + "/SaltWaterWithoutFloor.mat",
                new Color32(38, 85, 148, 255),
                WaterSurfaceOffsetPixels,
                MissingUnderwaterWaterFadePixels,
                MissingUnderwaterWaterMaximumAlpha);
            int horizontalWaveLengthPixels = InspectionHoleWidth * AssetsPixelsPerUnit;
            int verticalWaveLengthPixels = InspectionHoleHeightPixels - WaterSurfaceOffsetPixels;
            Material freshHorizontalWave = EnsureWaterEdgeWaveMaterial(
                waveShader,
                GeneratedPreviewAssetFolder + "/FreshWaterEdgeWaveHorizontal.mat",
                new Color32(86, 180, 215, 255),
                new Color32(171, 229, 238, 255),
                horizontalWaveLengthPixels);
            Material freshVerticalWave = EnsureWaterEdgeWaveMaterial(
                waveShader,
                GeneratedPreviewAssetFolder + "/FreshWaterEdgeWaveVertical.mat",
                new Color32(86, 180, 215, 255),
                new Color32(171, 229, 238, 255),
                verticalWaveLengthPixels);
            Material saltHorizontalWave = EnsureWaterEdgeWaveMaterial(
                waveShader,
                GeneratedPreviewAssetFolder + "/SaltWaterEdgeWaveHorizontal.mat",
                new Color32(78, 135, 205, 255),
                new Color32(157, 201, 237, 255),
                horizontalWaveLengthPixels);
            Material saltVerticalWave = EnsureWaterEdgeWaveMaterial(
                waveShader,
                GeneratedPreviewAssetFolder + "/SaltWaterEdgeWaveVertical.mat",
                new Color32(78, 135, 205, 255),
                new Color32(157, 201, 237, 255),
                verticalWaveLengthPixels);
            AssetDatabase.SaveAssets();

            return new GeneratedPreviewAssets(
                dryCliffFade,
                freshWaterWithFloor,
                saltWaterWithFloor,
                saltWaterWithoutFloor,
                freshHorizontalWave,
                freshVerticalWave,
                saltHorizontalWave,
                saltVerticalWave);
        }

        /// <summary>指定色・開始位置・到達距離・最大Alphaを持つ連続深度Materialを生成または更新します。</summary>
        /// <param name="shader">連続深度を描く検品用Shaderです。</param>
        /// <param name="assetPath">Materialの保存先です。</param>
        /// <param name="overlayColor">重ねる黒または液体種別の青系表示色です。</param>
        /// <param name="clippedTopPixels">描画せず透明に保つ上端の画素数です。</param>
        /// <param name="fadePixels">上端クリップ後に最大Alphaへ到達する画素距離です。</param>
        /// <param name="maximumAlpha">勾配終端の最大Alphaです。</param>
        /// <returns>AssetDatabaseへ保存済みのMaterialです。</returns>
        private static Material EnsureContinuousDepthMaterial(
            Shader shader,
            string assetPath,
            Color overlayColor,
            int clippedTopPixels,
            int fadePixels,
            float maximumAlpha)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = Path.GetFileNameWithoutExtension(assetPath)
                };
                AssetDatabase.CreateAsset(material, assetPath);
            }
            else
            {
                material.shader = shader;
            }

            // Shaderは穴全体のUVを0～1として受け取るため、Pixel距離を160px全高に対する比率へ変換します。
            material.SetColor("_OverlayColor", overlayColor);
            material.SetFloat(
                "_ClippedTopRatio",
                clippedTopPixels / (float)InspectionHoleHeightPixels);
            material.SetFloat("_FadeRatio", fadePixels / (float)InspectionHoleHeightPixels);
            material.SetFloat("_MaximumAlpha", maximumAlpha);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>2色の帯が1px刻みで流れる水際波線Materialを生成または更新します。</summary>
        /// <param name="shader">水際波線を描く検品用Shaderです。</param>
        /// <param name="assetPath">Materialの保存先です。</param>
        /// <param name="firstColor">交互に流す暗い側の色です。</param>
        /// <param name="secondColor">交互に流す明るい側の色です。</param>
        /// <param name="pixelLength">波線の長さです。単位は画素です。</param>
        /// <returns>AssetDatabaseへ保存済みのMaterialです。</returns>
        private static Material EnsureWaterEdgeWaveMaterial(
            Shader shader,
            string assetPath,
            Color firstColor,
            Color secondColor,
            int pixelLength)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = Path.GetFileNameWithoutExtension(assetPath)
                };
                AssetDatabase.CreateAsset(material, assetPath);
            }
            else
            {
                material.shader = shader;
            }

            material.SetColor("_FirstColor", firstColor);
            material.SetColor("_SecondColor", secondColor);
            material.SetFloat("_PixelLength", pixelLength);
            material.SetFloat("_PixelsPerSecond", WaterEdgeWavePixelsPerSecond);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>指定した材質IDをMaterials配列から探します。</summary>
        /// <param name="settings">検索対象の設定アセットです。</param>
        /// <param name="materialId">探す安定材質IDです。</param>
        /// <returns>一致した材質、見つからなければnullです。</returns>
        private static TerrainMaterialArtSource FindMaterial(
            TerrainTileArtGenerationSettings settings,
            string materialId)
        {
            TerrainMaterialArtSource[] materials = settings.Materials;
            for (int index = 0; index < materials.Length; index++)
            {
                TerrainMaterialArtSource material = materials[index];
                if (material != null
                    && string.Equals(material.MaterialId, materialId, StringComparison.Ordinal))
                {
                    return material;
                }
            }

            return null;
        }

        /// <summary>材質参照の配列位置を返します。</summary>
        /// <param name="settings">検索対象の設定アセットです。</param>
        /// <param name="material">位置を探す材質参照です。</param>
        /// <returns>一致した0始まりの位置です。</returns>
        private static int FindMaterialIndex(
            TerrainTileArtGenerationSettings settings,
            TerrainMaterialArtSource material)
        {
            for (int index = 0; index < settings.Materials.Length; index++)
            {
                if (ReferenceEquals(settings.Materials[index], material))
                {
                    return index;
                }
            }

            throw new InvalidOperationException("対象材質の配列位置を解決できませんでした。");
        }

        /// <summary>空SceneへCameraと検品Spriteを配置し、用途別パスへ保存します。</summary>
        /// <param name="assets">検証済みのGrassLoam素材です。</param>
        /// <param name="comparisonOnly">暗化比較だけを配置する場合はtrueです。</param>
        private static void BuildAndOpenScene(InspectionAssets assets, bool comparisonOnly)
        {
            // 検品Sceneだけが利用する深度・波線Materialを、Scene参照を作る前にAssetとして確定します。
            // 一時的なメモリObjectをSceneへ保存すると再起動後に参照が外れるため、AssetDatabase経由で永続化します。
            GeneratedPreviewAssets previewAssets = comparisonOnly
                ? null
                : EnsureGeneratedPreviewAssets();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = comparisonOnly
                ? "TerrainWallSurfaceDarkenComparison"
                : "TerrainTileArtInspection";
            CreateCamera();
            GameObject root = new GameObject("Terrain Tile Art Inspection");
            if (comparisonOnly)
            {
                CreateDarkenComparison(root, assets);
            }
            else
            {
                // 地面の中を掘った横3×手前方向10セルの穴を横1列へ並べ、5条件を同じ深度で比較します。
                // Wallの暗化・接地影・動的影は専用比較Sceneで検証済みなので、このSceneは穴と深度表現へ集中させます。
                CreateHoleInspectionGrid(root, assets, previewAssets);
                CreateBoundaryRows(root, assets);
            }

            string scenePath = comparisonOnly
                ? WallSurfaceDarkenComparisonScenePath
                : InspectionScenePath;
            EnsureAssetFolder("Assets/Scenes/Development");
            if (!EditorSceneManager.SaveScene(scene, scenePath))
            {
                throw new InvalidOperationException($"検品Sceneを保存できませんでした: {scenePath}");
            }

            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        /// <summary>Pixel Perfect CameraをScene原点へ作成します。</summary>
        private static void CreateCamera()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 9f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(22, 25, 30, 255);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            PixelPerfectCamera pixelPerfect = cameraObject.AddComponent<PixelPerfectCamera>();
            // Component.nameへ代入するとGameObject名まで変わるため、EditorのSerializedPropertyでComponent自身の名前を設定します。
            // Unity 6のScene YAMLで空名が末尾空白として保存されることも防ぎ、差分検査を安定させます。
            SerializedObject serializedPixelPerfect = new SerializedObject(pixelPerfect);
            SerializedProperty serializedComponentName =
                serializedPixelPerfect.FindProperty("m_Name");
            serializedComponentName.stringValue = "Terrain Tile Inspection Pixel Perfect Camera";
            serializedPixelPerfect.ApplyModifiedPropertiesWithoutUndo();
            pixelPerfect.assetsPPU = AssetsPixelsPerUnit;
            pixelPerfect.refResolutionX = ReferenceResolutionX;
            pixelPerfect.refResolutionY = ReferenceResolutionY;

            // 基準解像度と異なるGameビューでは上下左右を黒帯で囲み、整数倍率の画素を保ちます。
            // 旧cropFrameX／YはUnity 6で非推奨なので、両方向をまとめる現行APIを使用します。
            pixelPerfect.cropFrame = PixelPerfectCamera.CropFrame.Windowbox;
        }

        /// <summary>Ground反復面の上へ同じSpriteを使ったWallパッチと2段Faceを重ねます。</summary>
        /// <param name="root">検品オブジェクトの親です。</param>
        /// <param name="assets">配置するSprite一式です。</param>
        /// <param name="darkenFactor">Wall上面へ掛けるRGB乗算係数です。</param>
        /// <param name="horizontalOffset">比較Scene内での水平移動量です。</param>
        private static void CreateGroundAndWallInspection(
            GameObject root,
            InspectionAssets assets,
            float darkenFactor,
            float horizontalOffset)
        {
            GameObject group = CreateChild(root, $"Ground and Wall x{darkenFactor:0.00}");
            for (int y = 0; y < GroundRepeatHeight; y++)
            {
                for (int x = 0; x < GroundRepeatWidth; x++)
                {
                    int variant = StableVariantIndex(x, y, assets.GroundTopSprites.Length);
                    CreateSpriteObject(
                        group,
                        $"Ground_{x:D2}_{y:D2}",
                        assets.GroundTopSprites[variant],
                        new Vector3(horizontalOffset + x - 5.5f, y - 3f, 0f),
                        Color.white,
                        0);
                }
            }

            Color wallTint = new Color(darkenFactor, darkenFactor, darkenFactor, 1f);
            for (int y = 0; y < WallPatchHeight; y++)
            {
                for (int x = 0; x < WallPatchWidth; x++)
                {
                    int variant = StableVariantIndex(x + 20, y + 20, assets.GroundTopSprites.Length);
                    float worldX = horizontalOffset + x - 2f;
                    float worldY = y - 0.5f;
                    CreateSpriteObject(
                        group,
                        $"WallTop_{x:D2}_{y:D2}",
                        assets.GroundTopSprites[variant],
                        new Vector3(worldX, worldY, -0.1f),
                        wallTint,
                        10);

                    if (y == 0)
                    {
                        // Wall前面は本番想定の2段にします。境界模様が段ごとに再出現しないよう、
                        // 上面に接する1段目だけWall用境界合成結果、2段目は通常Faceを使います。
                        for (int faceStage = 1; faceStage <= WallFaceDepth; faceStage++)
                        {
                            Sprite face = faceStage == 1
                                ? assets.WallBoundarySprites[x % assets.WallBoundarySprites.Length]
                                : assets.FaceSprites[x % assets.FaceSprites.Length];
                            Vector3 facePosition = new Vector3(
                                worldX,
                                worldY - faceStage,
                                -0.2f);

                            // 固定接地影はWall全体の根元だけに必要です。
                            // 1段目には置かず、2段目の下端から左右対称にはみ出すようにします。
                            if (faceStage == WallFaceDepth)
                            {
                                CreateContactShadow(
                                    group,
                                    $"WallFaceContactShadow_{x:D2}",
                                    face,
                                    facePosition);
                            }

                            CreateSpriteObject(
                                group,
                                $"WallFace_{x:D2}_Stage_{faceStage:D2}",
                                face,
                                facePosition,
                                Color.white,
                                20);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 横3×手前方向10セルの穴を、乾燥、淡水海底、海水海底、海中床なし、マグマの5条件で横1列へ並べます。
        /// 各穴は1セル幅のGroundTopで囲み、単独の縦見本ではなく地面を掘った状況として読めるようにします。
        /// </summary>
        /// <param name="root">検品オブジェクトの親です。</param>
        /// <param name="assets">GrassLoamと仮液体のSprite一式です。</param>
        private static void CreateHoleInspectionGrid(
            GameObject root,
            InspectionAssets assets,
            GeneratedPreviewAssets previewAssets)
        {
            GameObject group = CreateChild(root, "Excavated Holes Width3 Depth10");
            CreateHoleInspectionPanel(
                group,
                assets,
                previewAssets,
                "Dry Hole",
                -12f,
                0.5f,
                null,
                null,
                null,
                null,
                HoleDepthPreviewKind.Dry);
            CreateHoleInspectionPanel(
                group,
                assets,
                previewAssets,
                "FreshWater Underwater Floor",
                -6f,
                0.5f,
                assets.FreshWaterPlaceholder,
                previewAssets.FreshWaterWithFloorMaterial,
                previewAssets.FreshWaterHorizontalWaveMaterial,
                previewAssets.FreshWaterVerticalWaveMaterial,
                HoleDepthPreviewKind.UnderwaterFloor);
            CreateHoleInspectionPanel(
                group,
                assets,
                previewAssets,
                "SaltWater Underwater Floor",
                0f,
                0.5f,
                assets.SaltWaterPlaceholder,
                previewAssets.SaltWaterWithFloorMaterial,
                previewAssets.SaltWaterHorizontalWaveMaterial,
                previewAssets.SaltWaterVerticalWaveMaterial,
                HoleDepthPreviewKind.UnderwaterFloor);
            CreateHoleInspectionPanel(
                group,
                assets,
                previewAssets,
                "Open Underwater Hides DeepSea",
                6f,
                0.5f,
                assets.SaltWaterPlaceholder,
                previewAssets.SaltWaterWithoutFloorMaterial,
                previewAssets.SaltWaterHorizontalWaveMaterial,
                previewAssets.SaltWaterVerticalWaveMaterial,
                HoleDepthPreviewKind.HiddenDeepSea);
            CreateHoleInspectionPanel(
                group,
                assets,
                previewAssets,
                "Magma Hole",
                12f,
                0.5f,
                assets.MagmaPlaceholder,
                null,
                null,
                null,
                HoleDepthPreviewKind.Magma);
        }

        /// <summary>1セル幅の地面で囲まれた横3×手前方向10セルの穴を1区画へ作ります。</summary>
        /// <param name="parent">5つの穴をまとめる親です。</param>
        /// <param name="assets">GroundTop、Face、境界合成結果を含む素材です。</param>
        /// <param name="previewAssets">連続勾配Materialと仮波線Materialです。</param>
        /// <param name="panelName">Hierarchyへ表示する穴の種類です。</param>
        /// <param name="centerX">穴と外周を合わせた区画中心のX座標です。</param>
        /// <param name="centerY">穴と外周を合わせた区画中心のY座標です。</param>
        /// <param name="liquidSprite">乾燥穴ではnull、液体穴では仮Spriteです。</param>
        /// <param name="liquidOverlayMaterial">淡水・海水では連続勾配Material、乾燥穴とマグマではnullです。</param>
        /// <param name="horizontalWaveMaterial">淡水・海水の奥辺へ使う横方向波線Materialです。</param>
        /// <param name="verticalWaveMaterial">淡水・海水の左右辺へ使う縦方向波線Materialです。</param>
        /// <param name="previewKind">4段目以降へ表示する床または遮蔽の種類です。</param>
        private static void CreateHoleInspectionPanel(
            GameObject parent,
            InspectionAssets assets,
            GeneratedPreviewAssets previewAssets,
            string panelName,
            float centerX,
            float centerY,
            Sprite liquidSprite,
            Material liquidOverlayMaterial,
            Material horizontalWaveMaterial,
            Material verticalWaveMaterial,
            HoleDepthPreviewKind previewKind)
        {
            GameObject panel = CreateChild(parent, panelName);
            int panelWidth = InspectionHoleWidth + (InspectionHoleBorderWidth * 2);
            int panelHeight = InspectionHoleDepth + (InspectionHoleBorderWidth * 2);

            // まず穴の外周だけへGroundTopを置きます。
            // 内側の横3×手前方向10セルを空け、液体色の矩形ではなく地面を掘った輪郭として見せます。
            for (int localY = 0; localY < panelHeight; localY++)
            {
                for (int localX = 0; localX < panelWidth; localX++)
                {
                    bool isInsideHole = localX >= InspectionHoleBorderWidth
                        && localX < panelWidth - InspectionHoleBorderWidth
                        && localY >= InspectionHoleBorderWidth
                        && localY < panelHeight - InspectionHoleBorderWidth;
                    if (isInsideHole)
                    {
                        continue;
                    }

                    int variant = StableVariantIndex(
                        localX + Mathf.RoundToInt(centerX * 10f),
                        localY + Mathf.RoundToInt(centerY * 10f),
                        assets.GroundTopSprites.Length);
                    Vector3 groundPosition = CalculatePanelCellPosition(
                        centerX,
                        centerY,
                        panelWidth,
                        panelHeight,
                        localX,
                        localY,
                        0f);
                    CreateSpriteObject(
                        panel,
                        $"{panelName}_Ground_{localX:D2}_{localY:D2}",
                        assets.GroundTopSprites[variant],
                        groundPosition,
                        Color.white,
                        70);
                }
            }

            // 穴の内側は最初の3段を崖面にし、4段目以降は直下レイヤー、継続Face、完全な暗黒のいずれかへ切り替えます。
            // WorldLayer自体の論理距離は変えず、SurfaceからUnderwaterを見せるPresentation上の投影だけを検証します。
            for (int depthStage = 0; depthStage < InspectionHoleDepth; depthStage++)
            {
                for (int holeX = 0; holeX < InspectionHoleWidth; holeX++)
                {
                    int variant = StableVariantIndex(
                        holeX + Mathf.RoundToInt(centerX * 10f) + 100,
                        depthStage + Mathf.RoundToInt(centerY * 10f) + 100,
                        assets.FaceSprites.Length);
                    int panelX = holeX + InspectionHoleBorderWidth;
                    int panelY = panelHeight - InspectionHoleBorderWidth - depthStage - 1;
                    Vector3 cellPosition = CalculatePanelCellPosition(
                        centerX,
                        centerY,
                        panelWidth,
                        panelHeight,
                        panelX,
                        panelY,
                        -0.2f);

                    if (previewKind == HoleDepthPreviewKind.Dry)
                    {
                        // Faceが透明になる途中にも必ず暗黒が見えるよう、乾燥穴の全段へ黒い下地を敷きます。
                        // 黒勾配自体は穴全体を覆う1枚で作り、セル境界ごとのAlpha差を発生させません。
                        CreateSpriteObject(
                            panel,
                            $"{panelName}_BlackBacking_{holeX:D2}_Depth_{depthStage + 1:D2}",
                            assets.FreshWaterPlaceholder,
                            cellPosition,
                            Color.black,
                            30);
                    }

                    if (depthStage < GroundFaceDepth)
                    {
                        CreateFacePreviewCell(
                            panel,
                            assets,
                            panelName,
                            holeX,
                            depthStage,
                            variant,
                            cellPosition);
                    }
                    else if (previewKind == HoleDepthPreviewKind.UnderwaterFloor)
                    {
                        CreateUnderwaterFloorPreviewCell(
                            panel,
                            assets,
                            panelName,
                            holeX,
                            depthStage,
                            variant,
                            cellPosition);
                    }
                    else if (previewKind == HoleDepthPreviewKind.Dry)
                    {
                        // 乾燥崖は3段目の下端でFace Alphaが0になるため、4段目以降は黒い下地だけを見せます。
                        // 水域ではこの黒を使わず、通常Faceまたは直下床へ青Overlayだけを重ねます。
                    }
                    else if (previewKind == HoleDepthPreviewKind.HiddenDeepSea
                        && depthStage < MissingUnderwaterVisibleFaceDepth)
                    {
                        // Underwater床がない水域だけ、6段目まで現在材質のFaceを続けます。
                        // 7段目以降は青Overlayが完全不透明なので、見えない下地を生成しません。
                        CreateSpriteObject(
                            panel,
                            $"{panelName}_ContinuedFace_{holeX:D2}_Depth_{depthStage + 1:D2}",
                            assets.FaceSprites[variant],
                            cellPosition,
                            Color.white,
                            40);
                    }

                    // マグマだけは接触する1段目から完全不透明な既存仮Spriteを置き、背後のFaceを隠します。
                    // 淡水・海水はセル単位で置かず、ループ後に穴全体を覆う1枚の連続勾配として作ります。
                    if (previewKind == HoleDepthPreviewKind.Magma)
                    {
                        CreateSpriteObject(
                            panel,
                            $"{panelName}_Magma_{holeX:D2}_Depth_{depthStage + 1:D2}",
                            liquidSprite,
                            new Vector3(cellPosition.x, cellPosition.y, -0.3f),
                            Color.white,
                            50);
                    }
                }
            }

            if (previewKind == HoleDepthPreviewKind.Dry)
            {
                CreateContinuousDepthOverlay(
                    panel,
                    panelName,
                    centerX,
                    centerY,
                    assets.FreshWaterPlaceholder,
                    previewAssets.DryCliffFadeMaterial,
                    "ContinuousDryCliffFade");
            }
            else if (liquidOverlayMaterial != null)
            {
                CreateContinuousDepthOverlay(
                    panel,
                    panelName,
                    centerX,
                    centerY,
                    liquidSprite,
                    liquidOverlayMaterial,
                    "ContinuousWaterOverlay");
                CreateWaterEdgeWaveUShape(
                    panel,
                    panelName,
                    centerX,
                    centerY,
                    liquidSprite,
                    horizontalWaveMaterial,
                    verticalWaveMaterial);
            }
        }

        /// <summary>穴の1～3段目へ、境界を含むGrassLoam崖面を配置します。</summary>
        /// <param name="parent">穴1区画の親です。</param>
        /// <param name="assets">Faceと境界合成結果を含む素材です。</param>
        /// <param name="panelName">Hierarchy上の穴名です。</param>
        /// <param name="holeX">穴内の0始まり水平位置です。</param>
        /// <param name="depthStage">0始まりの崖面段です。</param>
        /// <param name="variant">座標から決めた通常Face差分位置です。</param>
        /// <param name="position">Spriteを置くワールド座標です。</param>
        private static void CreateFacePreviewCell(
            GameObject parent,
            InspectionAssets assets,
            string panelName,
            int holeX,
            int depthStage,
            int variant,
            Vector3 position)
        {
            Sprite faceSprite = depthStage == 0
                ? assets.GroundBoundarySprites[
                    variant % assets.GroundBoundarySprites.Length]
                : assets.FaceSprites[variant];
            CreateSpriteObject(
                parent,
                $"{panelName}_Face_{holeX:D2}_Depth_{depthStage + 1:D2}",
                faceSprite,
                position,
                Color.white,
                40);
        }

        /// <summary>4段目以降へUnderwaterの地面を投影し、1個の仮配置物を水越しに置きます。</summary>
        /// <param name="parent">穴1区画の親です。</param>
        /// <param name="assets">GroundTopと仮配置物を含む素材です。</param>
        /// <param name="panelName">Hierarchy上の穴名です。</param>
        /// <param name="holeX">穴内の0始まり水平位置です。</param>
        /// <param name="depthStage">0始まりの穴奥行き位置です。</param>
        /// <param name="variant">座標から決めた差分位置です。</param>
        /// <param name="position">Spriteを置くワールド座標です。</param>
        private static void CreateUnderwaterFloorPreviewCell(
            GameObject parent,
            InspectionAssets assets,
            string panelName,
            int holeX,
            int depthStage,
            int variant,
            Vector3 position)
        {
            int groundVariant = variant % assets.GroundTopSprites.Length;
            CreateSpriteObject(
                parent,
                $"{panelName}_UnderwaterGround_{holeX:D2}_Depth_{depthStage + 1:D2}",
                assets.GroundTopSprites[groundVariant],
                position,
                Color.white,
                30);

            // 4段目より少し手前の中央セルへ代表物を1個だけ置き、地上からも存在が読めることを示します。
            // これは図鑑発見や選択対象ではなく、直下レイヤーのPresentationプレビューだけです。
            if (holeX == InspectionHoleWidth / 2
                && depthStage == UnderwaterPreviewStartDepth + 2)
            {
                CreateSpriteObject(
                    parent,
                    $"{panelName}_UnderwaterObjectPreview",
                    assets.UnderwaterObjectPlaceholder,
                    new Vector3(position.x, position.y, -0.25f),
                    Color.white,
                    45);
            }
        }

        /// <summary>穴全体を覆う1枚のSpriteRendererへ連続深度Materialを設定します。</summary>
        /// <param name="parent">穴1区画の親です。</param>
        /// <param name="panelName">Hierarchy上の穴名です。</param>
        /// <param name="centerX">穴の水平中心です。</param>
        /// <param name="centerY">穴の垂直中心です。</param>
        /// <param name="geometrySprite">16×16pxの矩形Geometryとして使う仮Spriteです。</param>
        /// <param name="overlayMaterial">上端クリップと連続Alpha勾配を持つMaterialです。</param>
        /// <param name="objectSuffix">Hierarchy上で用途を識別する末尾名です。</param>
        private static void CreateContinuousDepthOverlay(
            GameObject parent,
            string panelName,
            float centerX,
            float centerY,
            Sprite geometrySprite,
            Material overlayMaterial,
            string objectSuffix)
        {
            GameObject overlay = new GameObject($"{panelName}_{objectSuffix}");
            overlay.transform.SetParent(parent.transform, false);
            overlay.transform.position = new Vector3(centerX, centerY, -0.3f);

            // 元Spriteは1×1 Unity unitなので、穴の3×10セルへ1枚だけ引き伸ばします。
            // 1枚のUVをShaderで評価することで、セル境界にAlphaの継ぎ目を作りません。
            overlay.transform.localScale = new Vector3(
                InspectionHoleWidth,
                InspectionHoleDepth,
                1f);
            SpriteRenderer renderer = overlay.AddComponent<SpriteRenderer>();
            renderer.sprite = geometrySprite;
            renderer.color = Color.white;
            renderer.sharedMaterial = overlayMaterial;
            renderer.sortingOrder = 50;
        }

        /// <summary>水面の奥・左・右辺へ、2色が流れる1px波線をU字状に配置します。</summary>
        /// <param name="parent">穴1区画の親です。</param>
        /// <param name="panelName">Hierarchy上の穴名です。</param>
        /// <param name="centerX">穴の水平中心です。</param>
        /// <param name="centerY">穴の垂直中心です。</param>
        /// <param name="geometrySprite">矩形Geometryとして使う仮Spriteです。</param>
        /// <param name="horizontalMaterial">奥辺の48px線へ使うMaterialです。</param>
        /// <param name="verticalMaterial">左右辺の157px線へ使うMaterialです。</param>
        private static void CreateWaterEdgeWaveUShape(
            GameObject parent,
            string panelName,
            float centerX,
            float centerY,
            Sprite geometrySprite,
            Material horizontalMaterial,
            Material verticalMaterial)
        {
            float topEdgeY = centerY + (InspectionHoleDepth * 0.5f);
            float bottomEdgeY = centerY - (InspectionHoleDepth * 0.5f);
            float loweredWaterSurfaceY = topEdgeY
                - (WaterSurfaceOffsetPixels / (float)AssetsPixelsPerUnit);
            float halfPixel = 0.5f / AssetsPixelsPerUnit;
            float lineThickness = WaterEdgeWaveThicknessPixels / (float)AssetsPixelsPerUnit;
            float verticalLength = loweredWaterSurfaceY - bottomEdgeY;
            float verticalCenterY = (loweredWaterSurfaceY + bottomEdgeY) * 0.5f;
            float leftEdgeX = centerX - (InspectionHoleWidth * 0.5f);
            float rightEdgeX = centerX + (InspectionHoleWidth * 0.5f);

            // 奥辺は3px低い水面の直内側へ置きます。手前辺は斜め見下ろしでFaceを隠すため、意図的に作りません。
            CreateWaterEdgeWaveLine(
                parent,
                $"{panelName}_WaterEdgeWave_Back",
                geometrySprite,
                new Vector3(centerX, loweredWaterSurfaceY - halfPixel, -0.4f),
                new Vector3(InspectionHoleWidth, lineThickness, 1f),
                0f,
                horizontalMaterial);

            // 同じ横長Geometryを90度回転し、左辺は上向き、右辺は下向きに色帯が流れるU字の循環方向を作ります。
            CreateWaterEdgeWaveLine(
                parent,
                $"{panelName}_WaterEdgeWave_Left",
                geometrySprite,
                new Vector3(leftEdgeX + halfPixel, verticalCenterY, -0.4f),
                new Vector3(verticalLength, lineThickness, 1f),
                90f,
                verticalMaterial);
            CreateWaterEdgeWaveLine(
                parent,
                $"{panelName}_WaterEdgeWave_Right",
                geometrySprite,
                new Vector3(rightEdgeX - halfPixel, verticalCenterY, -0.4f),
                new Vector3(verticalLength, lineThickness, 1f),
                -90f,
                verticalMaterial);
        }

        /// <summary>指定位置へ1本のShaderアニメーション波線を作ります。</summary>
        /// <param name="parent">穴1区画の親です。</param>
        /// <param name="name">Hierarchyへ表示する波線名です。</param>
        /// <param name="geometrySprite">矩形Geometryとして使う仮Spriteです。</param>
        /// <param name="position">線の中心を置くワールド座標です。</param>
        /// <param name="scale">線の長さと1pxの太さを表すScaleです。</param>
        /// <param name="rotationZ">横線を左右辺へ回すZ回転角度です。</param>
        /// <param name="material">2色を整数Pixel単位で流すMaterialです。</param>
        private static void CreateWaterEdgeWaveLine(
            GameObject parent,
            string name,
            Sprite geometrySprite,
            Vector3 position,
            Vector3 scale,
            float rotationZ,
            Material material)
        {
            GameObject wave = new GameObject(name);
            wave.transform.SetParent(parent.transform, false);
            wave.transform.position = position;
            wave.transform.rotation = Quaternion.Euler(0f, 0f, rotationZ);
            wave.transform.localScale = scale;
            SpriteRenderer renderer = wave.AddComponent<SpriteRenderer>();
            renderer.sprite = geometrySprite;
            renderer.color = Color.white;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 80;
        }

        /// <summary>区画内の整数セルを、区画中心基準のワールド座標へ変換します。</summary>
        /// <param name="centerX">区画中心のX座標です。</param>
        /// <param name="centerY">区画中心のY座標です。</param>
        /// <param name="panelWidth">外周を含む区画幅です。単位はセルです。</param>
        /// <param name="panelHeight">外周を含む区画高です。単位はセルです。</param>
        /// <param name="localX">区画左端を0とする水平セル位置です。</param>
        /// <param name="localY">区画下端を0とする垂直セル位置です。</param>
        /// <param name="z">Sprite同士の前後関係に使うZ座標です。</param>
        /// <returns>Sprite中心を置くワールド座標です。</returns>
        private static Vector3 CalculatePanelCellPosition(
            float centerX,
            float centerY,
            int panelWidth,
            int panelHeight,
            int localX,
            int localY,
            float z)
        {
            float left = centerX - ((panelWidth - 1) * 0.5f);
            float bottom = centerY - ((panelHeight - 1) * 0.5f);
            return new Vector3(left + localX, bottom + localY, z);
        }

        /// <summary>採用済みC案と同じ左右対称の固定接地影を、Wall最下段Faceの背後へ作ります。</summary>
        /// <param name="parent">Wall検品Groupです。</param>
        /// <param name="name">Hierarchyへ表示する影の基底名です。</param>
        /// <param name="sourceSprite">不透明形状を影へ転用する最下段Faceです。</param>
        /// <param name="sourcePosition">最下段Faceのワールド座標です。</param>
        private static void CreateContactShadow(
            GameObject parent,
            string name,
            Sprite sourceSprite,
            Vector3 sourcePosition)
        {
            // 左右に1pxずらした同一形状を2枚重ね、真上基準の光でも片側へ偏らない接地点を作ります。
            // 比較専用Sceneと同じ公開定数を参照し、採用値が2つの検品Sceneで食い違わないようにします。
            for (int horizontalSign = -1; horizontalSign <= 1; horizontalSign += 2)
            {
                Vector3 pixelOffset = new Vector3(
                    horizontalSign
                        * TerrainWallShadowComparisonMapBuilder.ContactShadowHorizontalSpreadPixels
                        / (float)AssetsPixelsPerUnit,
                    TerrainWallShadowComparisonMapBuilder.ContactShadowOffsetYPixels
                        / (float)AssetsPixelsPerUnit,
                    0.05f);
                string sideName = horizontalSign < 0 ? "Left" : "Right";
                CreateSpriteObject(
                    parent,
                    $"{name}_{sideName}",
                    sourceSprite,
                    sourcePosition + pixelOffset,
                    new Color(
                        0f,
                        0f,
                        0f,
                        TerrainWallShadowComparisonMapBuilder.ContactShadowLayerOpacity),
                    15);
            }
        }

        /// <summary>Ground／Wall用FaceBoundary合成結果を上下2列へ並べます。</summary>
        /// <param name="root">検品オブジェクトの親です。</param>
        /// <param name="assets">配置する境界Sprite一式です。</param>
        private static void CreateBoundaryRows(GameObject root, InspectionAssets assets)
        {
            GameObject group = CreateChild(root, "Face Boundary Rows");
            for (int index = 0; index < assets.GroundBoundarySprites.Length; index++)
            {
                float x = index - (assets.GroundBoundarySprites.Length - 1) * 0.5f;
                CreateSpriteObject(
                    group,
                    $"GroundBoundary_{index:D2}",
                    assets.GroundBoundarySprites[index],
                    new Vector3(x, -7f, -0.1f),
                    Color.white,
                    30);
                CreateSpriteObject(
                    group,
                    $"WallBoundary_{index:D2}",
                    assets.WallBoundarySprites[index],
                    new Vector3(x, -8.2f, -0.1f),
                    Color.white,
                    30);
            }
        }

        /// <summary>3種類の壁暗化係数を同じ小型配置で横並びにします。</summary>
        /// <param name="root">検品オブジェクトの親です。</param>
        /// <param name="assets">配置するGroundTopとFaceです。</param>
        private static void CreateDarkenComparison(GameObject root, InspectionAssets assets)
        {
            for (int index = 0; index < WallSurfaceDarkenComparisonFactors.Length; index++)
            {
                float offset = (index - 1) * 13f;
                CreateGroundAndWallInspection(
                    root,
                    assets,
                    WallSurfaceDarkenComparisonFactors[index],
                    offset);
            }
        }

        /// <summary>座標から安定した差分Indexを算出し、Scene再生成時にも配置を変えません。</summary>
        /// <param name="x">整数の水平セル座標です。</param>
        /// <param name="y">整数の垂直セル座標です。</param>
        /// <param name="variantCount">選択候補数です。</param>
        /// <returns>0以上variantCount未満の安定Indexです。</returns>
        private static int StableVariantIndex(int x, int y, int variantCount)
        {
            if (variantCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(variantCount));
            }

            // 大きさの異なる素数を使って二次元座標を混ぜ、単純な縦横周期を見えにくくします。
            uint hash = unchecked((uint)(x * 73856093) ^ (uint)(y * 19349663));
            return (int)(hash % (uint)variantCount);
        }

        /// <summary>親GameObjectの直下へ空の子Groupを作ります。</summary>
        /// <param name="parent">親GameObjectです。</param>
        /// <param name="name">Hierarchyへ表示する名前です。</param>
        /// <returns>作成した子GameObjectです。</returns>
        private static GameObject CreateChild(GameObject parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        /// <summary>指定Sprite、Tint、描画順を持つSpriteRendererを作ります。</summary>
        /// <param name="parent">親Groupです。</param>
        /// <param name="name">Hierarchyへ表示する名前です。</param>
        /// <param name="sprite">描画するSpriteです。</param>
        /// <param name="position">ワールド座標です。</param>
        /// <param name="color">SpriteRendererへ設定する乗算色です。</param>
        /// <param name="sortingOrder">同じSorting Layer内の描画順です。</param>
        private static void CreateSpriteObject(
            GameObject parent,
            string name,
            Sprite sprite,
            Vector3 position,
            Color color,
            int sortingOrder)
        {
            GameObject gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent.transform, false);
            gameObject.transform.position = position;
            SpriteRenderer renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
        }

        /// <summary>Assets配下のフォルダー階層を不足分だけ作ります。</summary>
        /// <param name="folderPath">作成するAssets相対フォルダーです。</param>
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
