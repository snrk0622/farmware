// このファイルは、代表5材質の地形SpriteとGrassLoamを使った仮の液体表現を固定配置した検品Sceneを再現可能に構築します。
// 地形Simulationには接続せず、材質差分、Wall暗化、Wall折れ目影、固定接地影、動的影、3段で黒へ溶ける崖、4段目からの海中床、連続水深、低い水面、U字の水際波線をGameビューで確認するEditor専用ツールです。

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
    /// 代表材質1種類について、固定検品Sceneへ読み込めた用途別Sprite数を表します。
    /// 材質ごとの可変長配列を固定枚数へ丸めず、設定に登録された候補をそのまま検証するために使用します。
    /// </summary>
    public readonly struct TerrainTileArtInspectionMaterialSummary
    {
        /// <summary>検品対象を識別する安定材質IDを返します。</summary>
        public string MaterialId { get; }

        /// <summary>読み込めたGroundTop差分数を返します。</summary>
        public int GroundTopCount { get; }

        /// <summary>読み込めた通常Face差分数を返します。</summary>
        public int FaceCount { get; }

        /// <summary>読み込めたGround用FaceBoundary合成結果数を返します。</summary>
        public int GroundBoundaryCount { get; }

        /// <summary>読み込めたWall用FaceBoundary合成結果数を返します。</summary>
        public int WallBoundaryCount { get; }

        /// <summary>代表材質1種類の検品結果を、安定IDと用途別Sprite数で保持します。</summary>
        /// <param name="materialId">設定に登録された安定材質IDです。</param>
        /// <param name="groundTopCount">GroundTop差分数です。</param>
        /// <param name="faceCount">通常Face差分数です。</param>
        /// <param name="groundBoundaryCount">Ground用境界合成結果数です。</param>
        /// <param name="wallBoundaryCount">Wall用境界合成結果数です。</param>
        public TerrainTileArtInspectionMaterialSummary(
            string materialId,
            int groundTopCount,
            int faceCount,
            int groundBoundaryCount,
            int wallBoundaryCount)
        {
            MaterialId = materialId;
            GroundTopCount = groundTopCount;
            FaceCount = faceCount;
            GroundBoundaryCount = groundBoundaryCount;
            WallBoundaryCount = wallBoundaryCount;
        }
    }

    /// <summary>
    /// 代表5材質のGroundTop・Wall上面・Faceと、GrassLoamの断面段数、Ground／Wall境界、液体被覆を固定配置します。
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

        // 材質構造の違いを比較する代表5材質です。
        // 配列順はSceneの左から右へ対応し、テストと人間確認で同じ配置を再現します。
        private static readonly string[] RepresentativeMaterialIds =
        {
            "GrassLoam",
            "DesertSand",
            "Sandstone",
            "Granite",
            "Basalt"
        };

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

        /// <summary>Wall上面と前面の折れ目へ8pxの共通陰影を描く検品用Shaderです。</summary>
        public const string WallFoldShadowShaderPath =
            "Assets/Farmware/Shaders/Development/WallFoldShadow.shader";

        /// <summary>Light 2DとShadow Caster 2Dの影響を受けるURP 2D Lit Materialです。</summary>
        private const string SpriteLitMaterialPath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat";

        /// <summary>照明で濃度を変えない固定接地影へ使うURP 2D Unlit Materialです。</summary>
        private const string SpriteUnlitMaterialPath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";

        /// <summary>検品用に自動生成する深度勾配、水際波線、Wall折れ目影のMaterialをまとめるフォルダーです。</summary>
        public const string GeneratedPreviewAssetFolder =
            "Assets/Farmware/Art/WorldGeneration/Generated/Inspection";

        /// <summary>Ground崖面を縦へ反復して確認する段数です。</summary>
        public const int GroundFaceDepth = 3;

        /// <summary>乾燥崖のFace表示が完全に黒へ溶けるまでの画素距離です。</summary>
        public const int DryCliffFadePixels = GroundFaceDepth * 16;

        /// <summary>本番想定のWall前面を縦へ反復して確認する段数です。</summary>
        public const int WallFaceDepth = 2;

        /// <summary>Wall前面1段目の上端から折れ目影が完全透過になるまでの画素数です。</summary>
        public const int WallFoldShadowFadePixels = 8;

        /// <summary>Wall折れ目影の最上段へ掛ける黒のAlpha初期値です。</summary>
        public const float WallFoldShadowTopAlpha = 0.35f;

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
        private const int ReferenceResolutionX = 720;
        private const int ReferenceResolutionY = 360;

        // GroundTop反復感を見る矩形と、Wallパッチの寸法です。
        private const int GroundRepeatWidth = 12;
        private const int GroundRepeatHeight = 7;
        private const int WallPatchWidth = 5;
        private const int WallPatchHeight = 3;

        // 代表5材質は、同じ材質のGroundの上へ暗化Wall上面とFace 2段を重ねた小型検品パネルで表示します。
        // 切り離したSprite行ではなく、実際の地面とWallの重なり方で色と立体感を確認します。
        private const float RepresentativeMaterialSpacing = 9f;

        /// <summary>Wallの左右へ露出させる同一材質Groundの幅です。単位は16px論理セルです。</summary>
        public const int RepresentativeGroundSideMargin = 2;

        /// <summary>Wall前面より手前へ露出させる同一材質Groundの奥行きです。単位は16px論理セルです。</summary>
        public const int RepresentativeGroundFrontMargin = 2;

        /// <summary>Wall上面より奥へ露出させる同一材質Groundの奥行きです。単位は16px論理セルです。</summary>
        public const int RepresentativeGroundBackMargin = 2;

        /// <summary>代表材質パネルの最小横幅です。単位は16px論理セルです。</summary>
        public const int RepresentativeGroundPatchMinimumWidth =
            4 + (RepresentativeGroundSideMargin * 2);

        /// <summary>代表材質パネルのGroundを奥から手前へ並べる行数です。</summary>
        public const int RepresentativeGroundPatchHeight =
            RepresentativeGroundFrontMargin
            + WallFaceDepth
            + 1
            + RepresentativeGroundBackMargin;

        /// <summary>Groundパッチ下端を0としたWall上面の行位置です。</summary>
        public const int RepresentativeWallTopLocalY =
            RepresentativeGroundFrontMargin + WallFaceDepth;

        /// <summary>代表パネル1つへ置く上方主光と左手前検証光の合計数です。</summary>
        public const int RepresentativePointLightsPerPanel = 2;

        /// <summary>代表材質パネルの中心Y座標です。単位はUnity Unitです。</summary>
        private const float RepresentativePanelCenterY = 7.75f;

        // 代表材質パネルは上へ拡張し、穴検品との間へ0.5セルの隙間を残します。
        private const float HoleInspectionCenterY = -1.75f;

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

        /// <summary>固定検品Sceneへ左から右の順で配置する代表材質数を返します。</summary>
        public static int RepresentativeMaterialCount => RepresentativeMaterialIds.Length;

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

        /// <summary>Scene構築中だけ保持する、代表材質1種類の検証済み素材です。</summary>
        private sealed class InspectionMaterialAssets
        {
            /// <summary>設定とHierarchy名に使う安定材質IDです。</summary>
            public string MaterialId { get; }

            /// <summary>Inspectorで確認しやすい日本語名です。</summary>
            public string JapaneseName { get; }

            /// <summary>GroundとWall上面で共有する差分です。</summary>
            public Sprite[] GroundTopSprites { get; }

            /// <summary>Ground崖面とWall前面で共有する通常Faceです。</summary>
            public Sprite[] FaceSprites { get; }

            /// <summary>Ground用境界合成結果です。</summary>
            public Sprite[] GroundBoundarySprites { get; }

            /// <summary>Wall用境界合成結果です。</summary>
            public Sprite[] WallBoundarySprites { get; }

            /// <summary>Wall上面へ掛けるRGB乗算係数です。</summary>
            public float WallDarkenFactor { get; }

            /// <summary>代表材質1種類の入力・生成SpriteとWall暗化値をまとめます。</summary>
            /// <param name="materialId">安定材質IDです。</param>
            /// <param name="japaneseName">Inspector表示用の日本語名です。</param>
            /// <param name="groundTopSprites">GroundとWall上面で共有する差分です。</param>
            /// <param name="faceSprites">Ground崖面とWall前面で共有する通常Faceです。</param>
            /// <param name="groundBoundarySprites">Ground用境界合成結果です。</param>
            /// <param name="wallBoundarySprites">Wall用境界合成結果です。</param>
            /// <param name="wallDarkenFactor">Wall上面のRGB乗算係数です。</param>
            public InspectionMaterialAssets(
                string materialId,
                string japaneseName,
                Sprite[] groundTopSprites,
                Sprite[] faceSprites,
                Sprite[] groundBoundarySprites,
                Sprite[] wallBoundarySprites,
                float wallDarkenFactor)
            {
                MaterialId = materialId;
                JapaneseName = japaneseName;
                GroundTopSprites = groundTopSprites;
                FaceSprites = faceSprites;
                GroundBoundarySprites = groundBoundarySprites;
                WallBoundarySprites = wallBoundarySprites;
                WallDarkenFactor = wallDarkenFactor;
            }
        }

        /// <summary>Scene構築中だけ保持する代表5材質とGrassLoam液体検品素材一式です。</summary>
        private sealed class InspectionAssets
        {
            /// <summary>Scene上部へ左から右の順で配置する代表5材質です。</summary>
            public InspectionMaterialAssets[] RepresentativeMaterials { get; }

            /// <summary>GroundとWall上面で共有する差分です。</summary>
            public Sprite[] GroundTopSprites => RepresentativeMaterials[0].GroundTopSprites;

            /// <summary>Ground崖面とWall前面で共有する通常Faceです。</summary>
            public Sprite[] FaceSprites => RepresentativeMaterials[0].FaceSprites;

            /// <summary>Ground用境界合成結果です。</summary>
            public Sprite[] GroundBoundarySprites =>
                RepresentativeMaterials[0].GroundBoundarySprites;

            /// <summary>Wall用境界合成結果です。</summary>
            public Sprite[] WallBoundarySprites =>
                RepresentativeMaterials[0].WallBoundarySprites;

            /// <summary>淡水の半透明な深度被覆へ一時利用するFallback Spriteです。</summary>
            public Sprite FreshWaterPlaceholder { get; }

            /// <summary>海水の半透明な深度被覆へ一時利用するFallback Spriteです。</summary>
            public Sprite SaltWaterPlaceholder { get; }

            /// <summary>接触段からFaceを隠す不透明マグマへ一時利用するFallback Spriteです。</summary>
            public Sprite MagmaPlaceholder { get; }

            /// <summary>海中床の配置物が地上から見えることを示す仮Spriteです。</summary>
            public Sprite UnderwaterObjectPlaceholder { get; }

            /// <summary>代表材質パネルでLight 2Dと動的影を受けるURP 2D Lit Materialです。</summary>
            public Material SpriteLitMaterial { get; }

            /// <summary>代表材質パネルの固定接地影を照明から独立させるURP 2D Unlit Materialです。</summary>
            public Material SpriteUnlitMaterial { get; }

            /// <summary>Wall上面へ掛けるRGB乗算係数です。</summary>
            public float WallDarkenFactor => RepresentativeMaterials[0].WallDarkenFactor;

            /// <summary>検証済み素材と壁暗化設定をScene構築用にまとめます。</summary>
            /// <param name="representativeMaterials">検証済みの代表5材質です。</param>
            /// <param name="freshWaterPlaceholder">淡水の仮Spriteです。</param>
            /// <param name="saltWaterPlaceholder">海水の仮Spriteです。</param>
            /// <param name="magmaPlaceholder">マグマの仮Spriteです。</param>
            /// <param name="underwaterObjectPlaceholder">海中床の仮配置物Spriteです。</param>
            /// <param name="spriteLitMaterial">代表材質のGround、Wall、Faceへ使うLit Materialです。</param>
            /// <param name="spriteUnlitMaterial">固定接地影へ使うUnlit Materialです。</param>
            public InspectionAssets(
                InspectionMaterialAssets[] representativeMaterials,
                Sprite freshWaterPlaceholder,
                Sprite saltWaterPlaceholder,
                Sprite magmaPlaceholder,
                Sprite underwaterObjectPlaceholder,
                Material spriteLitMaterial,
                Material spriteUnlitMaterial)
            {
                RepresentativeMaterials = representativeMaterials;
                FreshWaterPlaceholder = freshWaterPlaceholder;
                SaltWaterPlaceholder = saltWaterPlaceholder;
                MagmaPlaceholder = magmaPlaceholder;
                UnderwaterObjectPlaceholder = underwaterObjectPlaceholder;
                SpriteLitMaterial = spriteLitMaterial;
                SpriteUnlitMaterial = spriteUnlitMaterial;
            }
        }

        /// <summary>
        /// Scene構築時に自動生成する連続深度、水際の仮波線、Wall折れ目影のMaterialをまとめます。
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

            /// <summary>Wall前面1段目の上端8pxへ共通して重ねる折れ目影Materialです。</summary>
            public Material WallFoldShadowMaterial { get; }

            /// <summary>生成済みの検品用表示素材をScene構築へ渡します。</summary>
            /// <param name="dryCliffFadeMaterial">乾燥崖の黒勾配Materialです。</param>
            /// <param name="freshWaterWithFloorMaterial">淡水・海中床あり用Materialです。</param>
            /// <param name="saltWaterWithFloorMaterial">海水・海中床あり用Materialです。</param>
            /// <param name="saltWaterWithoutFloorMaterial">海水・海中床なし用Materialです。</param>
            /// <param name="freshWaterHorizontalWaveMaterial">淡水の奥辺用波線Materialです。</param>
            /// <param name="freshWaterVerticalWaveMaterial">淡水の左右辺用波線Materialです。</param>
            /// <param name="saltWaterHorizontalWaveMaterial">海水の奥辺用波線Materialです。</param>
            /// <param name="saltWaterVerticalWaveMaterial">海水の左右辺用波線Materialです。</param>
            /// <param name="wallFoldShadowMaterial">Wall前面1段目へ重ねる折れ目影Materialです。</param>
            public GeneratedPreviewAssets(
                Material dryCliffFadeMaterial,
                Material freshWaterWithFloorMaterial,
                Material saltWaterWithFloorMaterial,
                Material saltWaterWithoutFloorMaterial,
                Material freshWaterHorizontalWaveMaterial,
                Material freshWaterVerticalWaveMaterial,
                Material saltWaterHorizontalWaveMaterial,
                Material saltWaterVerticalWaveMaterial,
                Material wallFoldShadowMaterial)
            {
                DryCliffFadeMaterial = dryCliffFadeMaterial;
                FreshWaterWithFloorMaterial = freshWaterWithFloorMaterial;
                SaltWaterWithFloorMaterial = saltWaterWithFloorMaterial;
                SaltWaterWithoutFloorMaterial = saltWaterWithoutFloorMaterial;
                FreshWaterHorizontalWaveMaterial = freshWaterHorizontalWaveMaterial;
                FreshWaterVerticalWaveMaterial = freshWaterVerticalWaveMaterial;
                SaltWaterHorizontalWaveMaterial = saltWaterHorizontalWaveMaterial;
                SaltWaterVerticalWaveMaterial = saltWaterVerticalWaveMaterial;
                WallFoldShadowMaterial = wallFoldShadowMaterial;
            }
        }

        /// <summary>未保存Sceneを保護したうえで、代表5材質の固定検品Sceneを再構築して開きます。</summary>
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

        /// <summary>固定検品Sceneの指定位置へ配置する代表材質IDを返します。</summary>
        /// <param name="index">左から数える0始まりの配置位置です。</param>
        /// <returns>設定アセット内の材質を特定する安定IDです。</returns>
        public static string GetRepresentativeMaterialId(int index)
        {
            if (index < 0 || index >= RepresentativeMaterialIds.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return RepresentativeMaterialIds[index];
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

        /// <summary>代表5材質の入力と生成物を読み、Sceneを変更せず用途別枚数を返します。</summary>
        /// <returns>左から右の固定配置順に並んだ代表材質の検証結果です。</returns>
        public static TerrainTileArtInspectionMaterialSummary[]
            ValidateRepresentativeMaterialAssets()
        {
            InspectionAssets assets = LoadAndValidateAssets();
            TerrainTileArtInspectionMaterialSummary[] summaries =
                new TerrainTileArtInspectionMaterialSummary[
                    assets.RepresentativeMaterials.Length];
            for (int index = 0; index < assets.RepresentativeMaterials.Length; index++)
            {
                InspectionMaterialAssets material = assets.RepresentativeMaterials[index];
                summaries[index] = new TerrainTileArtInspectionMaterialSummary(
                    material.MaterialId,
                    material.GroundTopSprites.Length,
                    material.FaceSprites.Length,
                    material.GroundBoundarySprites.Length,
                    material.WallBoundarySprites.Length);
            }

            return summaries;
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

        /// <summary>Wall前面1段目の上端から数えた画素行に対応する折れ目影Alphaを返します。</summary>
        /// <param name="pixelRowFromTop">上端を0とする0以上の画素行です。</param>
        /// <returns>上端で初期Alpha、8px目で0になり、それより下も0となるAlphaです。</returns>
        public static float GetWallFoldShadowAlpha(int pixelRowFromTop)
        {
            if (pixelRowFromTop < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pixelRowFromTop));
            }

            // 8pxの範囲内で最後の行を完全透過にするため、0～7行を「行数-1」で割ります。
            // Shaderと同じ式を公開しておくことで、検品用見た目の調整値をEditModeテストで固定できます。
            if (pixelRowFromTop >= WallFoldShadowFadePixels)
            {
                return 0f;
            }

            float fadeDenominator = Mathf.Max(WallFoldShadowFadePixels - 1, 1);
            float fade = 1f - Mathf.Clamp01(pixelRowFromTop / fadeDenominator);
            return WallFoldShadowTopAlpha * fade;
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

        /// <summary>設定アセットから代表5材質と生成済み境界Spriteを読み込み、件数と参照を検証します。</summary>
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

            InspectionMaterialAssets[] representativeMaterials =
                new InspectionMaterialAssets[RepresentativeMaterialIds.Length];
            for (int materialIndex = 0;
                 materialIndex < RepresentativeMaterialIds.Length;
                 materialIndex++)
            {
                // 一部の材質だけを黙って省略すると比較結果を誤認するため、5材質をすべて必須入力として検証します。
                representativeMaterials[materialIndex] = LoadAndValidateMaterialAssets(
                    settings,
                    RepresentativeMaterialIds[materialIndex]);
            }

            // 液体専用の完成アートは後続制作のため、現段階では既存Fallbackを仮Spriteとして使います。
            // 3種類を明示的に検証し、欠けたままSceneを作って比較結果を誤認しないようにします。
            Sprite freshWaterPlaceholder = LoadRequiredSprite(FreshWaterPlaceholderPath, "淡水");
            Sprite saltWaterPlaceholder = LoadRequiredSprite(SaltWaterPlaceholderPath, "海水");
            Sprite magmaPlaceholder = LoadRequiredSprite(MagmaPlaceholderPath, "マグマ");
            Sprite underwaterObjectPlaceholder = LoadRequiredSprite(
                UnderwaterObjectPlaceholderPath,
                "海中床の配置物");
            Material spriteLitMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                SpriteLitMaterialPath);
            if (spriteLitMaterial == null)
            {
                throw new InvalidOperationException(
                    $"URP 2D Lit Materialを読み込めません: {SpriteLitMaterialPath}");
            }

            Material spriteUnlitMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                SpriteUnlitMaterialPath);
            if (spriteUnlitMaterial == null)
            {
                throw new InvalidOperationException(
                    $"URP 2D Unlit Materialを読み込めません: {SpriteUnlitMaterialPath}");
            }

            return new InspectionAssets(
                representativeMaterials,
                freshWaterPlaceholder,
                saltWaterPlaceholder,
                magmaPlaceholder,
                underwaterObjectPlaceholder,
                spriteLitMaterial,
                spriteUnlitMaterial);
        }

        /// <summary>代表材質1種類の設定、入力Sprite、境界合成結果を検証して読み込みます。</summary>
        /// <param name="settings">材質配列を保持する生成設定です。</param>
        /// <param name="materialId">読み込む代表材質の安定IDです。</param>
        /// <returns>Sceneへ配置できる検証済み材質素材です。</returns>
        private static InspectionMaterialAssets LoadAndValidateMaterialAssets(
            TerrainTileArtGenerationSettings settings,
            string materialId)
        {
            TerrainMaterialArtSource material = FindMaterial(settings, materialId);
            if (material == null)
            {
                throw new InvalidOperationException($"Materialsに{materialId}がありません。");
            }

            TerrainTileArtValidationReport report = TerrainTileArtGenerator.ValidateMaterial(
                settings,
                FindMaterialIndex(settings, material));
            if (report.HasErrors)
            {
                throw new InvalidOperationException(
                    $"{materialId}の生成入力が不正です。先に生成設定Inspectorで検証してください。\n- "
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
                    $"{materialId}のGround境界生成物は{expectedBoundaryCount}枚必要ですが、{groundBoundary.Count}枚でした。対象材質を再生成してください。");
            }

            if (material.DarkenWallSurface && wallBoundary.Count != expectedBoundaryCount)
            {
                throw new InvalidOperationException(
                    $"{materialId}のWall境界生成物は{expectedBoundaryCount}枚必要ですが、{wallBoundary.Count}枚でした。対象材質を再生成してください。");
            }

            // 暗化なしではWallがGround境界を再利用するため、Scene側も同じ参照を使います。
            Sprite[] wallBoundarySprites = material.DarkenWallSurface
                ? wallBoundary.ToArray()
                : groundBoundary.ToArray();
            return new InspectionMaterialAssets(
                material.MaterialId,
                material.JapaneseName,
                material.GroundTopSprites,
                material.FaceSprites,
                groundBoundary.ToArray(),
                wallBoundarySprites,
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

        /// <summary>連続深度、奥・左右へ使う2色の仮波線、Wall折れ目影のMaterialを生成または更新します。</summary>
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
            AssetDatabase.ImportAsset(
                WallFoldShadowShaderPath,
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

            Shader wallFoldShadowShader = AssetDatabase.LoadAssetAtPath<Shader>(
                WallFoldShadowShaderPath);
            if (wallFoldShadowShader == null)
            {
                throw new InvalidOperationException(
                    $"Wall折れ目影Shaderを読み込めません: {WallFoldShadowShaderPath}");
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
            Material wallFoldShadow = EnsureWallFoldShadowMaterial(
                wallFoldShadowShader,
                GeneratedPreviewAssetFolder + "/WallFoldShadow.mat");
            AssetDatabase.SaveAssets();

            return new GeneratedPreviewAssets(
                dryCliffFade,
                freshWaterWithFloor,
                saltWaterWithFloor,
                saltWaterWithoutFloor,
                freshHorizontalWave,
                freshVerticalWave,
                saltHorizontalWave,
                saltVerticalWave,
                wallFoldShadow);
        }

        /// <summary>Wall暗化比較Sceneだけで使う折れ目影Materialを、液体用Assetへ依存せず生成または更新します。</summary>
        /// <returns>Sceneへ永続参照できる折れ目影Materialです。</returns>
        public static Material EnsureWallFoldShadowPreviewMaterial()
        {
            EnsureAssetFolder(GeneratedPreviewAssetFolder);
            AssetDatabase.ImportAsset(
                WallFoldShadowShaderPath,
                ImportAssetOptions.ForceUpdate);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(WallFoldShadowShaderPath);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Wall折れ目影Shaderを読み込めません: {WallFoldShadowShaderPath}");
            }

            // 暗化比較Sceneは水・崖を表示しないため、液体用ShaderやMaterialを要求しません。
            // 必要な共通影だけを確定し、比較ツールの入力範囲をWall素材へ限定します。
            Material material = EnsureWallFoldShadowMaterial(
                shader,
                GeneratedPreviewAssetFolder + "/WallFoldShadow.mat");
            AssetDatabase.SaveAssets();
            return material;
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

        /// <summary>Wall前面1段目の上端8pxへ掛ける共通折れ目影Materialを生成または更新します。</summary>
        /// <param name="shader">画素行単位のAlpha勾配を描く検品用Shaderです。</param>
        /// <param name="assetPath">Materialの保存先です。</param>
        /// <returns>AssetDatabaseへ保存済みのMaterialです。</returns>
        private static Material EnsureWallFoldShadowMaterial(
            Shader shader,
            string assetPath)
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

            // 折れ目影は材質色を持たず、黒のAlphaだけを8画素で減衰させます。
            // Sprite寸法は生成設定の16pxを参照し、Shader側のマジックナンバーと食い違わないようにします。
            material.SetColor("_ShadowColor", Color.black);
            material.SetFloat("_TopAlpha", WallFoldShadowTopAlpha);
            material.SetFloat("_FadePixelCount", WallFoldShadowFadePixels);
            material.SetFloat(
                "_SpritePixelHeight",
                TerrainTileArtGenerationSettings.TileSizeInPixels);
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
        /// <param name="assets">検証済みの代表5材質とGrassLoam液体検品素材です。</param>
        /// <param name="comparisonOnly">暗化比較だけを配置する場合はtrueです。</param>
        private static void BuildAndOpenScene(InspectionAssets assets, bool comparisonOnly)
        {
            // 検品Sceneだけが利用する深度・波線・折れ目影Materialを、Scene参照を作る前にAssetとして確定します。
            // 一時的なメモリObjectをSceneへ保存すると再起動後に参照が外れるため、AssetDatabase経由で永続化します。
            // Wall暗化比較では折れ目影だけを生成し、固定検品では深度・波線を含む一式を生成します。
            // これにより、Wallだけの比較が無関係な液体Shaderの欠落で実行不能になることを避けます。
            GeneratedPreviewAssets previewAssets = comparisonOnly
                ? null
                : EnsureGeneratedPreviewAssets();
            Material wallFoldShadowMaterial = comparisonOnly
                ? EnsureWallFoldShadowPreviewMaterial()
                : previewAssets.WallFoldShadowMaterial;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = comparisonOnly
                ? "TerrainWallSurfaceDarkenComparison"
                : "TerrainTileArtInspection";
            CreateCamera();
            GameObject root = new GameObject("Terrain Tile Art Inspection");
            if (comparisonOnly)
            {
                CreateDarkenComparison(root, assets, wallFoldShadowMaterial);
            }
            else
            {
                // C案と同じ低い環境光を代表材質パネルへ与え、Point Lightの外側も完全な黒にならないようにします。
                // 液体検品はUnlitまたは専用Shaderのままなので、代表パネル用照明から表示契約を分離できます。
                CreateRepresentativeGlobalLight(root);

                // 各代表材質について、同じGroundの上へ暗化Wall上面とFace 2段を重ねます。
                // 実景に近い重なり方にすることで、Wall上面、折れ目影、Face、固定接地影、動的影の関係をひとつのパネルで読めるようにします。
                CreateRepresentativeMaterialWallPanels(
                    root,
                    assets.RepresentativeMaterials,
                    wallFoldShadowMaterial,
                    assets.SpriteLitMaterial,
                    assets.SpriteUnlitMaterial);

                // 地面の中を掘った横3×手前方向10セルの穴を横1列へ並べ、5条件を同じ深度で比較します。
                // 穴は代表材質パネルと独立したUnlit表示とし、深度表現の色が検証用Lightで変化しないようにします。
                CreateHoleInspectionGrid(root, assets, previewAssets);
                CreateBoundaryRows(root, assets);
                ValidateRepresentativePanelLighting(
                    root,
                    assets.RepresentativeMaterials);
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
        /// <param name="wallFoldShadowMaterial">Wall前面1段目へ重ねる共通折れ目影です。</param>
        private static void CreateGroundAndWallInspection(
            GameObject root,
            InspectionAssets assets,
            float darkenFactor,
            float horizontalOffset,
            Material wallFoldShadowMaterial)
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
                                    facePosition,
                                    assets.SpriteUnlitMaterial);
                            }

                            CreateSpriteObject(
                                group,
                                $"WallFace_{x:D2}_Stage_{faceStage:D2}",
                                face,
                                facePosition,
                                Color.white,
                                20);

                            if (faceStage == 1)
                            {
                                // 上面と前面の明度が近い材質でも立ち上がりを読めるよう、1段目の上端だけへ共通影を重ねます。
                                // Face自体や2段目以降は暗化せず、材質本来の断面色と模様を維持します。
                                CreateSpriteObject(
                                    group,
                                    $"WallFoldShadow_{x:D2}",
                                    face,
                                    facePosition,
                                    Color.white,
                                    21,
                                    wallFoldShadowMaterial);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>代表材質パネルの外側も読める最低限の環境光を作ります。</summary>
        /// <param name="root">固定検品Sceneの親GameObjectです。</param>
        private static void CreateRepresentativeGlobalLight(GameObject root)
        {
            GameObject lightObject = CreateChild(root, "Representative Global Light 2D");
            Light2D light = lightObject.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.intensity = TerrainWallShadowComparisonMapBuilder.GlobalLightIntensity;
            light.color = Color.white;
            light.shadowsEnabled = false;
        }

        /// <summary>
        /// 代表5材質のそれぞれに、周囲へ同じ材質のGroundを広げ、中央へWall上面とFace 2段を重ねた検品パネルを作ります。
        /// 切り離したSprite行では分かりにくい、GroundとWallの明度差、折れ目影、Faceの縦接続、固定接地影、動的影を同時に確認できます。
        /// </summary>
        /// <param name="root">固定検品Scene内の全表示をまとめる親です。</param>
        /// <param name="materials">左から右の固定順で検証済みの代表材質です。</param>
        /// <param name="wallFoldShadowMaterial">Wall前面1段目の上端へ重ねる共通折れ目影です。</param>
        /// <param name="spriteLitMaterial">Ground、Wall、Faceへ使うURP 2D Lit Materialです。</param>
        /// <param name="spriteUnlitMaterial">固定接地影へ使うURP 2D Unlit Materialです。</param>
        private static void CreateRepresentativeMaterialWallPanels(
            GameObject root,
            InspectionMaterialAssets[] materials,
            Material wallFoldShadowMaterial,
            Material spriteLitMaterial,
            Material spriteUnlitMaterial)
        {
            GameObject group = CreateChild(root, "Representative Material Wall Panels");
            GameObject lightsGroup = CreateChild(group, "Representative Panel Lights");
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                InspectionMaterialAssets material = materials[materialIndex];
                float centerX =
                    (materialIndex - ((materials.Length - 1) * 0.5f))
                    * RepresentativeMaterialSpacing;
                string displayName = string.IsNullOrWhiteSpace(material.JapaneseName)
                    ? material.MaterialId
                    : $"{material.MaterialId} {material.JapaneseName}";
                GameObject materialGroup = CreateChild(group, displayName);
                Color wallTint = new Color(
                    material.WallDarkenFactor,
                    material.WallDarkenFactor,
                    material.WallDarkenFactor,
                    1f);

                // Wall幅はGroundTopとFaceの長い方に合わせ、現在登録されている差分を横一列で省略しません。
                // その左右へ必ず2セルずつGroundを足し、「Sprite見本」ではなく地面の中央にWallが立つ配置にします。
                int wallWidth = Mathf.Max(
                    material.GroundTopSprites.Length,
                    material.FaceSprites.Length);
                int panelWidth = Mathf.Max(
                    RepresentativeGroundPatchMinimumWidth,
                    wallWidth + (RepresentativeGroundSideMargin * 2));
                int wallStartLocalX = (panelWidth - wallWidth) / 2;

                for (int localY = 0;
                     localY < RepresentativeGroundPatchHeight;
                     localY++)
                {
                    for (int localX = 0; localX < panelWidth; localX++)
                    {
                        int groundVariant = StableVariantIndex(
                            localX + (materialIndex * 31),
                            localY + (materialIndex * 17),
                            material.GroundTopSprites.Length);
                        Vector3 groundPosition = CalculatePanelCellPosition(
                            centerX,
                            RepresentativePanelCenterY,
                            panelWidth,
                            RepresentativeGroundPatchHeight,
                            localX,
                            localY,
                            0f);
                        CreateSpriteObject(
                            materialGroup,
                            $"{material.MaterialId}_Ground_{localX:D2}_{localY:D2}",
                            material.GroundTopSprites[groundVariant],
                            groundPosition,
                            Color.white,
                            0,
                            spriteLitMaterial);
                    }
                }

                GameObject shadowCasterRoot = CreateChild(
                    materialGroup,
                    "Composite Ground Contact Shadow Casters");
                shadowCasterRoot.AddComponent<CompositeShadowCaster2D>();
                GameObject contactShadowRoot = CreateChild(
                    materialGroup,
                    "Symmetric Contact Shadows");

                for (int wallX = 0; wallX < wallWidth; wallX++)
                {
                    int localX = wallStartLocalX + wallX;
                    int groundVariant = wallX % material.GroundTopSprites.Length;
                    Vector3 wallTopPosition = CalculatePanelCellPosition(
                        centerX,
                        RepresentativePanelCenterY,
                        panelWidth,
                        RepresentativeGroundPatchHeight,
                        localX,
                        RepresentativeWallTopLocalY,
                        -0.1f);

                    // Wall上面は下地と同じGroundTop差分を使い、表示色だけを材質設定の係数で暗くします。
                    CreateSpriteObject(
                        materialGroup,
                        $"{material.MaterialId}_WallTop_{localX:D2}",
                        material.GroundTopSprites[groundVariant],
                        wallTopPosition,
                        wallTint,
                        80,
                        spriteLitMaterial);

                    // 表示Spriteへ直接Casterを付けると上面から影が伸びるため、Face 2段分だけ手前へ移した透明な代理輪郭を作ります。
                    // 材質パネル単位のCompositeへまとめ、横に連続するWallセルの共有辺から不要な影を出しません。
                    CreateRepresentativeDynamicShadowCaster(
                        shadowCasterRoot,
                        $"{material.MaterialId}_GroundContactCaster_{wallX:D2}",
                        material.GroundTopSprites[groundVariant],
                        wallTopPosition,
                        spriteLitMaterial);

                    for (int faceStage = 1; faceStage <= WallFaceDepth; faceStage++)
                    {
                        // 1段目は実装予定と同じWall用境界合成結果を優先し、境界のない材質では通常Faceを使います。
                        // 2段目は境界を繰り返さず、必ず通常Faceを縦接続します。
                        Sprite faceSprite = faceStage == 1
                            && material.WallBoundarySprites.Length > 0
                                ? material.WallBoundarySprites[
                                    wallX % material.WallBoundarySprites.Length]
                                : material.FaceSprites[
                                    wallX % material.FaceSprites.Length];
                        Vector3 facePosition = new Vector3(
                            wallTopPosition.x,
                            wallTopPosition.y - faceStage,
                            -0.2f);

                        // 根元の位置を同じGround上で読めるよう、採用済みC案の固定接地影を最下段だけに作ります。
                        if (faceStage == WallFaceDepth)
                        {
                            CreateContactShadow(
                                contactShadowRoot,
                                $"{material.MaterialId}_ContactShadow_{wallX:D2}",
                                faceSprite,
                                facePosition,
                                spriteUnlitMaterial);
                        }

                        CreateSpriteObject(
                            materialGroup,
                            $"{material.MaterialId}_WallFace_{wallX:D2}_Stage_{faceStage:D2}",
                            faceSprite,
                            facePosition,
                            Color.white,
                            80,
                            spriteLitMaterial);

                        if (faceStage == 1)
                        {
                            // 折れ目影はWall上面に接する1段目だけへ重ね、2段目のFace本体は元色を保ちます。
                            CreateSpriteObject(
                                materialGroup,
                                $"{material.MaterialId}_WallFoldShadow_{wallX:D2}",
                                faceSprite,
                                new Vector3(
                                    facePosition.x,
                                    facePosition.y,
                                    -0.21f),
                                Color.white,
                                81,
                                wallFoldShadowMaterial);
                        }
                    }
                }

                // C案と同じ上方主光と左手前検証光を材質ごとに置き、固定接地影と光源追従する投影影を同時に確認します。
                CreateRepresentativePanelLight(
                    lightsGroup,
                    $"{material.MaterialId}_Top Light 2D",
                    centerX,
                    RepresentativePanelCenterY + 3f,
                    1f);
                CreateRepresentativePanelLight(
                    lightsGroup,
                    $"{material.MaterialId}_Front Left Light 2D",
                    centerX - 2.5f,
                    RepresentativePanelCenterY - 3.5f,
                    TerrainWallShadowComparisonMapBuilder.FrontLeftLightIntensity);
            }
        }

        /// <summary>Wall最下段Faceの根元を起点にする透明な動的影用Shadow Caster 2Dを作ります。</summary>
        /// <param name="parent">材質パネル内でCasterを結合するCompositeの親です。</param>
        /// <param name="name">Hierarchyへ表示する代理Caster名です。</param>
        /// <param name="sourceSprite">Wall上面と同じ不透明輪郭を持つSpriteです。</param>
        /// <param name="wallTopPosition">対応するWall上面のワールド座標です。</param>
        /// <param name="spriteLitMaterial">透明な代理Spriteへ使うURP 2D Lit Materialです。</param>
        private static void CreateRepresentativeDynamicShadowCaster(
            GameObject parent,
            string name,
            Sprite sourceSprite,
            Vector3 wallTopPosition,
            Material spriteLitMaterial)
        {
            Vector3 contactPosition = new Vector3(
                wallTopPosition.x,
                wallTopPosition.y
                    + TerrainWallShadowComparisonMapBuilder.DynamicShadowCasterOffsetYInTiles,
                wallTopPosition.z);

            // 完全透明のRendererは輪郭だけをShadow Casterへ提供します。
            // Collider2Dを追加しないため、検品用の動的影が地形判定へ混ざることはありません。
            GameObject casterObject = new GameObject(name);
            casterObject.transform.SetParent(parent.transform, false);
            casterObject.transform.position = contactPosition;
            SpriteRenderer renderer = casterObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sourceSprite;
            renderer.color = Color.clear;
            renderer.sortingOrder = -100;
            renderer.sharedMaterial = spriteLitMaterial;
            ShadowCaster2D caster = casterObject.AddComponent<ShadowCaster2D>();
            caster.castingOption = ShadowCaster2D.ShadowCastingOptions.CastShadow;
            caster.trimEdge = 0f;
        }

        /// <summary>C案と同じ設定で、代表材質パネル1つを照らすPoint Light 2Dを作ります。</summary>
        /// <param name="parent">5材質のLightをまとめる親です。</param>
        /// <param name="name">Hierarchyへ表示するLight名です。</param>
        /// <param name="x">Light中心のX座標です。単位はUnity Unitです。</param>
        /// <param name="y">Light中心のY座標です。単位はUnity Unitです。</param>
        /// <param name="intensity">主光または左手前検証光の強度です。</param>
        private static void CreateRepresentativePanelLight(
            GameObject parent,
            string name,
            float x,
            float y,
            float intensity)
        {
            GameObject lightObject = CreateChild(parent, name);
            lightObject.transform.position = new Vector3(x, y, -1f);
            Light2D light = lightObject.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.intensity = intensity;
            light.color = new Color(1f, 0.96f, 0.88f, 1f);
            light.pointLightInnerRadius = 0.75f;
            light.pointLightOuterRadius =
                TerrainWallShadowComparisonMapBuilder.PointLightOuterRadius;
            light.falloffIntensity = 0.5f;
            light.shadowsEnabled = true;
            light.shadowIntensity =
                TerrainWallShadowComparisonMapBuilder.PointLightShadowIntensity;
            light.shadowSoftness =
                TerrainWallShadowComparisonMapBuilder.PointLightShadowSoftness;
        }

        /// <summary>代表材質パネルがC案と同じ動的影Component構成を持ち、物理Colliderを追加していないことを検証します。</summary>
        /// <param name="root">構築直後の固定検品Sceneルートです。</param>
        /// <param name="materials">各パネルのWall幅を計算する代表材質です。</param>
        private static void ValidateRepresentativePanelLighting(
            GameObject root,
            InspectionMaterialAssets[] materials)
        {
            int expectedCasterCount = 0;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                // Wall幅は生成処理と同じくGroundTopとFaceの長い方です。
                // 検証側でも同じ契約から算出し、可変長配列を固定4枚へ暗黙に丸めません。
                expectedCasterCount += Mathf.Max(
                    materials[materialIndex].GroundTopSprites.Length,
                    materials[materialIndex].FaceSprites.Length);
            }

            ShadowCaster2D[] casters = root.GetComponentsInChildren<ShadowCaster2D>(true);
            CompositeShadowCaster2D[] composites =
                root.GetComponentsInChildren<CompositeShadowCaster2D>(true);
            Light2D[] lights = root.GetComponentsInChildren<Light2D>(true);
            Collider2D[] colliders = root.GetComponentsInChildren<Collider2D>(true);
            int expectedLightCount = 1
                + (materials.Length * RepresentativePointLightsPerPanel);

            if (casters.Length != expectedCasterCount)
            {
                throw new InvalidOperationException(
                    $"代表材質パネルのShadow Caster 2D数が不正です: expected={expectedCasterCount}, actual={casters.Length}");
            }

            if (composites.Length != materials.Length)
            {
                throw new InvalidOperationException(
                    $"代表材質パネルのComposite Shadow Caster 2D数が不正です: expected={materials.Length}, actual={composites.Length}");
            }

            if (lights.Length != expectedLightCount)
            {
                throw new InvalidOperationException(
                    $"代表材質パネルのLight 2D数が不正です: expected={expectedLightCount}, actual={lights.Length}");
            }

            if (colliders.Length != 0)
            {
                throw new InvalidOperationException(
                    "代表材質パネルの影検品へCollider2Dを追加してはいけません。");
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
                HoleInspectionCenterY,
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
                HoleInspectionCenterY,
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
                HoleInspectionCenterY,
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
                HoleInspectionCenterY,
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
                HoleInspectionCenterY,
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
        /// <param name="spriteUnlitMaterial">照明の影響を受けないURP 2D Unlit Materialです。</param>
        private static void CreateContactShadow(
            GameObject parent,
            string name,
            Sprite sourceSprite,
            Vector3 sourcePosition,
            Material spriteUnlitMaterial)
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
                    15,
                    spriteUnlitMaterial);
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
                    new Vector3(x, -7.3f, -0.1f),
                    Color.white,
                    30);
                CreateSpriteObject(
                    group,
                    $"WallBoundary_{index:D2}",
                    assets.WallBoundarySprites[index],
                    new Vector3(x, -8.3f, -0.1f),
                    Color.white,
                    30);
            }
        }

        /// <summary>3種類の壁暗化係数を同じ小型配置で横並びにします。</summary>
        /// <param name="root">検品オブジェクトの親です。</param>
        /// <param name="assets">配置するGroundTopとFaceです。</param>
        /// <param name="wallFoldShadowMaterial">Wall前面1段目へ重ねる共通折れ目影です。</param>
        private static void CreateDarkenComparison(
            GameObject root,
            InspectionAssets assets,
            Material wallFoldShadowMaterial)
        {
            for (int index = 0; index < WallSurfaceDarkenComparisonFactors.Length; index++)
            {
                float offset = (index - 1) * 13f;
                CreateGroundAndWallInspection(
                    root,
                    assets,
                    WallSurfaceDarkenComparisonFactors[index],
                    offset,
                    wallFoldShadowMaterial);
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
            CreateSpriteObject(
                parent,
                name,
                sprite,
                position,
                color,
                sortingOrder,
                null);
        }

        /// <summary>指定Sprite、Tint、描画順、任意Materialを持つSpriteRendererを作ります。</summary>
        /// <param name="parent">親Groupです。</param>
        /// <param name="name">Hierarchyへ表示する名前です。</param>
        /// <param name="sprite">描画するSpriteです。</param>
        /// <param name="position">ワールド座標です。</param>
        /// <param name="color">SpriteRendererへ設定する乗算色です。</param>
        /// <param name="sortingOrder">同じSorting Layer内の描画順です。</param>
        /// <param name="material">専用Shaderを使う場合の共有Materialです。nullなら既定Sprite Materialを使います。</param>
        private static void CreateSpriteObject(
            GameObject parent,
            string name,
            Sprite sprite,
            Vector3 position,
            Color color,
            int sortingOrder,
            Material material)
        {
            GameObject gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent.transform, false);
            gameObject.transform.position = position;
            SpriteRenderer renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }
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
