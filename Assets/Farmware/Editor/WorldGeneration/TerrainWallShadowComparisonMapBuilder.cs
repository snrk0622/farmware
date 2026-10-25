// このファイルは、旧境界ハイライト、2D動的影、採用した左右対称の固定接地影を同じWall形状で比較する固定検品Sceneを構築します。
// 地形Simulationや製品Sceneは変更せず、採用したCと旧案の差を再確認できるようにするEditor専用ツールです。

using System;
using System.Collections.Generic;
using Farmware.WorldGeneration.Authoring;
using Farmware.WorldGeneration.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// Wall動的影比較Sceneの固定レイアウト件数を保持します。
    /// EditModeテストとScene構築処理が同じ比較条件を使っていることを確認するために使用します。
    /// </summary>
    public readonly struct TerrainWallShadowComparisonSummary
    {
        /// <summary>横並びにする比較パネル数です。</summary>
        public int PanelCount { get; }

        /// <summary>境界ハイライトを表示するパネル数です。</summary>
        public int HighlightPanelCount { get; }

        /// <summary>固定接地影を表示するパネル数です。</summary>
        public int ContactShadowPanelCount { get; }

        /// <summary>1パネルへ配置するWallセル数です。</summary>
        public int WallCellCountPerPanel { get; }

        /// <summary>1パネルで手前向きに露出するFace列数です。</summary>
        public int ExposedFaceColumnCountPerPanel { get; }

        /// <summary>Wall前面を縦へ反復する段数です。</summary>
        public int FaceDepth { get; }

        /// <summary>1パネルへ配置するFace Spriteの合計数です。</summary>
        public int FaceSpriteCountPerPanel { get; }

        /// <summary>全パネルへ配置するShadow Caster 2D数です。</summary>
        public int ShadowCasterCount { get; }

        /// <summary>全パネルへ配置するComposite Shadow Caster 2D数です。</summary>
        public int CompositeShadowCasterCount { get; }

        /// <summary>同じ相対位置へ配置するPoint Light 2D数です。</summary>
        public int PointLightCount { get; }

        /// <summary>固定接地影として配置するSpriteRenderer数です。</summary>
        public int ContactShadowSpriteCount { get; }

        /// <summary>比較条件の固定件数を保存します。</summary>
        /// <param name="panelCount">比較パネル数です。</param>
        /// <param name="highlightPanelCount">境界ハイライトを表示するパネル数です。</param>
        /// <param name="contactShadowPanelCount">固定接地影を表示するパネル数です。</param>
        /// <param name="wallCellCountPerPanel">1パネルのWallセル数です。</param>
        /// <param name="exposedFaceColumnCountPerPanel">1パネルの露出Face列数です。</param>
        /// <param name="faceDepth">Wall前面の反復段数です。</param>
        /// <param name="faceSpriteCountPerPanel">1パネルのFace Sprite合計数です。</param>
        /// <param name="shadowCasterCount">全Shadow Caster 2D数です。</param>
        /// <param name="compositeShadowCasterCount">全Composite Shadow Caster 2D数です。</param>
        /// <param name="pointLightCount">Point Light 2D数です。</param>
        /// <param name="contactShadowSpriteCount">固定接地影Sprite数です。</param>
        public TerrainWallShadowComparisonSummary(
            int panelCount,
            int highlightPanelCount,
            int contactShadowPanelCount,
            int wallCellCountPerPanel,
            int exposedFaceColumnCountPerPanel,
            int faceDepth,
            int faceSpriteCountPerPanel,
            int shadowCasterCount,
            int compositeShadowCasterCount,
            int pointLightCount,
            int contactShadowSpriteCount)
        {
            PanelCount = panelCount;
            HighlightPanelCount = highlightPanelCount;
            ContactShadowPanelCount = contactShadowPanelCount;
            WallCellCountPerPanel = wallCellCountPerPanel;
            ExposedFaceColumnCountPerPanel = exposedFaceColumnCountPerPanel;
            FaceDepth = faceDepth;
            FaceSpriteCountPerPanel = faceSpriteCountPerPanel;
            ShadowCasterCount = shadowCasterCount;
            CompositeShadowCasterCount = compositeShadowCasterCount;
            PointLightCount = pointLightCount;
            ContactShadowSpriteCount = contactShadowSpriteCount;
        }
    }

    /// <summary>
    /// GrassLoamの同一Wall形状を、ハイライトあり、動的影のみ、固定接地影ありの3条件で横並びにします。
    /// 各WallセルのShadow Caster 2Dをパネル単位のComposite Shadow Caster 2Dへまとめ、共有辺へ影を出さない構成を検品します。
    /// </summary>
    public static class TerrainWallShadowComparisonMapBuilder
    {
        /// <summary>比較Sceneの保存先です。</summary>
        public const string InspectionScenePath =
            "Assets/Scenes/Development/TerrainWallShadowComparison.unity";

        /// <summary>Point Light 2Dの影の濃さです。0は影なし、1は最大です。</summary>
        public const float PointLightShadowIntensity = 0.9f;

        /// <summary>Point Light 2Dの影の柔らかさです。ドット絵の輪郭を確認できる弱い値にします。</summary>
        public const float PointLightShadowSoftness = 0.05f;

        /// <summary>
        /// 比較用Point Light 2Dの外径です。単位はUnity Unitです。
        /// Wall上面から2段手前の設置面まで照らし、代理Casterの投影影を比較できる広さにします。
        /// </summary>
        public const float PointLightOuterRadius = 8f;

        /// <summary>3つの比較パネルへ共通して使う低い環境光の強さです。</summary>
        public const float GlobalLightIntensity = 0.35f;

        /// <summary>URP 2D Rendererが提供する既定Lit Sprite Materialのパスです。</summary>
        private const string SpriteLitMaterialPath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat";

        /// <summary>照明の明暗で濃度が変わらない固定接地影へ使うURP 2D Unlit Materialのパスです。</summary>
        private const string SpriteUnlitMaterialPath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";

        /// <summary>FaceBoundary合成済みのWall前面で優先して使う生成物名です。</summary>
        private const string PreferredWallFaceFileName = "GrassLoam_WallFace_f00_b00.png";

        /// <summary>Pixel Perfect CameraとSprite Importで共有する1Unit当たりの画素数です。</summary>
        private const int AssetsPixelsPerUnit = 16;

        /// <summary>比較SceneのPixel Perfect基準解像度です。単位はピクセルです。</summary>
        private const int ReferenceResolutionX = 480;

        /// <summary>比較SceneのPixel Perfect基準解像度です。単位はピクセルです。</summary>
        private const int ReferenceResolutionY = 270;

        /// <summary>隣り合う比較パネル中心の間隔です。単位はUnity Unitです。</summary>
        private const float PanelHorizontalSpacing = 8f;

        /// <summary>
        /// 固定接地影を左右へ同じ幅だけ広げる量です。
        /// 上方からの基準光に合わせ、単位はピクセルです。
        /// </summary>
        public const int ContactShadowHorizontalSpreadPixels = 1;

        /// <summary>
        /// 固定接地影を手前へずらす量です。
        /// 2段Wallでも根元を読める広さにするため、単位はピクセルです。
        /// </summary>
        public const int ContactShadowOffsetYPixels = -3;

        /// <summary>
        /// 左右の各接地影レイヤーの不透明度です。
        /// 中央の重なりは約39%、左右外縁は22%となり、ドット輪郭を保った弱いぼかしを作ります。
        /// </summary>
        public const float ContactShadowLayerOpacity = 0.22f;

        /// <summary>1パネルに置く上方光と左手前検証光の合計数です。</summary>
        public const int PointLightsPerPanel = 2;

        /// <summary>左手前の検証光の強さです。上方の主光より弱くします。</summary>
        public const float FrontLeftLightIntensity = 0.65f;

        /// <summary>
        /// 本番のWall表示を想定し、手前の露出面へ縦に並べるFaceの段数です。
        /// 各段は16px高なので、2段で表示高32pxになります。
        /// </summary>
        public const int WallFaceDepth = 2;

        /// <summary>
        /// 動的影用の遮蔽輪郭をWall上面から設置面へ移すY方向のタイル数です。
        /// Wall前面の段数と同じだけ手前へ移し、影が上面ではなく最下段Faceの根元から伸びるようにします。
        /// </summary>
        public const int DynamicShadowCasterOffsetYInTiles = -WallFaceDepth;

        /// <summary>1パネルのGround背景を横へ並べるセル数です。</summary>
        private const int GroundWidth = 7;

        /// <summary>1パネルのGround背景を縦へ並べるセル数です。</summary>
        private const int GroundHeight = 10;

        /// <summary>比較パネル数です。ハイライトあり、動的影のみ、固定接地影ありの3条件です。</summary>
        private const int PanelCount = 3;

        /// <summary>境界ハイライトを表示する比較パネル数です。</summary>
        private const int HighlightPanelCount = 1;

        /// <summary>固定接地影を表示する比較パネル数です。</summary>
        private const int ContactShadowPanelCount = 1;

        /// <summary>
        /// 外角、内角、細い先端、広い連続面を同時に確認するWallセル配置です。
        /// 同じ配列を3つのパネルへ使い、ハイライトや接地影以外の形状差が比較へ混ざらないようにします。
        /// </summary>
        private static readonly Vector2Int[] WallCells =
        {
            new Vector2Int(-2, 1),
            new Vector2Int(-1, 1),
            new Vector2Int(0, 1),
            new Vector2Int(1, 1),
            new Vector2Int(-2, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 0),
            new Vector2Int(1, 0),
            new Vector2Int(2, 0),
            new Vector2Int(-1, -1),
            new Vector2Int(0, -1),
            new Vector2Int(1, -1),
            new Vector2Int(0, -2)
        };

        /// <summary>Scene構築で使う検証済みSprite、Material、暗化色です。</summary>
        private sealed class InspectionAssets
        {
            /// <summary>GroundとWall上面で共有するGroundTop差分です。</summary>
            public Sprite[] GroundTopSprites { get; }

            /// <summary>Wall手前1段目に使う、境界Overlay合成済みまたは通常Faceです。</summary>
            public Sprite WallFirstFaceSprite { get; }

            /// <summary>Wall手前2段目以降へ反復する、境界Overlayを持たない通常Faceです。</summary>
            public Sprite WallRepeatingFaceSprite { get; }

            /// <summary>Shape01〜Shape14の順に並ぶ共通境界Spriteです。</summary>
            public Sprite[] BoundarySprites { get; }

            /// <summary>Light 2DとShadow Caster 2Dの影響を受けるURP 2D Lit Materialです。</summary>
            public Material SpriteLitMaterial { get; }

            /// <summary>環境光で薄まらない固定接地影へ使うURP 2D Unlit Materialです。</summary>
            public Material SpriteUnlitMaterial { get; }

            /// <summary>Wall上面とWall境界ハイライトへ掛けるRGB乗算色です。</summary>
            public Color WallTint { get; }

            /// <summary>Sceneへ配置できる検証済みAssetをまとめます。</summary>
            /// <param name="groundTopSprites">GroundTop差分です。</param>
            /// <param name="wallFirstFaceSprite">Wall前面1段目Spriteです。</param>
            /// <param name="wallRepeatingFaceSprite">Wall前面2段目以降の通常Face Spriteです。</param>
            /// <param name="boundarySprites">14基本境界Spriteです。</param>
            /// <param name="spriteLitMaterial">URP 2D Lit Materialです。</param>
            /// <param name="spriteUnlitMaterial">固定接地影用URP 2D Unlit Materialです。</param>
            /// <param name="wallTint">Wall表示用RGB乗算色です。</param>
            public InspectionAssets(
                Sprite[] groundTopSprites,
                Sprite wallFirstFaceSprite,
                Sprite wallRepeatingFaceSprite,
                Sprite[] boundarySprites,
                Material spriteLitMaterial,
                Material spriteUnlitMaterial,
                Color wallTint)
            {
                GroundTopSprites = groundTopSprites;
                WallFirstFaceSprite = wallFirstFaceSprite;
                WallRepeatingFaceSprite = wallRepeatingFaceSprite;
                BoundarySprites = boundarySprites;
                SpriteLitMaterial = spriteLitMaterial;
                SpriteUnlitMaterial = spriteUnlitMaterial;
                WallTint = wallTint;
            }
        }

        /// <summary>未保存Sceneを保護してから、Wall動的影比較Sceneを再構築して開きます。</summary>
        [MenuItem("Farmware/World Generation/Open Wall Shadow Comparison")]
        public static void BuildAndOpenFromMenu()
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
                    "Wall動的影比較Sceneを構築できません",
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
                BuildAndOpenScene(assets);
            }
            catch (Exception exception)
            {
                EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Wall動的影比較Sceneを構築できません",
                    exception.Message,
                    "閉じる");
            }
        }

        /// <summary>CLIや自動処理から、Wall動的影比較Sceneを再構築する入口です。</summary>
        public static void Execute()
        {
            BuildAndOpenScene(LoadAndValidateAssets());
        }

        /// <summary>Sceneを変更せず、固定比較レイアウトと必要Assetを検証します。</summary>
        /// <returns>3つのパネル、Face、Shadow Caster、Light、固定接地影の固定件数です。</returns>
        public static TerrainWallShadowComparisonSummary ValidateAssetsAndLayout()
        {
            LoadAndValidateAssets();
            HashSet<Vector2Int> wallCells = BuildWallCellSet();
            int exposedFaceColumnCount = CountExposedSouthFaces(wallCells);
            int faceSpriteCount = exposedFaceColumnCount * WallFaceDepth;
            return new TerrainWallShadowComparisonSummary(
                PanelCount,
                HighlightPanelCount,
                ContactShadowPanelCount,
                WallCells.Length,
                exposedFaceColumnCount,
                WallFaceDepth,
                faceSpriteCount,
                WallCells.Length * PanelCount,
                PanelCount,
                PanelCount * PointLightsPerPanel,
                (WallCells.Length + exposedFaceColumnCount)
                    * 2
                    * ContactShadowPanelCount);
        }

        /// <summary>GrassLoam、共通境界Sprite、URP 2D Lit Materialを読み込み、Scene構築前に検証します。</summary>
        /// <returns>Sceneへそのまま配置できる検証済みAssetです。</returns>
        private static InspectionAssets LoadAndValidateAssets()
        {
            TerrainTileArtGenerationSettings settings =
                AssetDatabase.LoadAssetAtPath<TerrainTileArtGenerationSettings>(
                    TerrainTileArtInspectionMapBuilder.SettingsAssetPath);
            if (settings == null)
            {
                throw new InvalidOperationException(
                    $"地形タイル生成設定が見つかりません: {TerrainTileArtInspectionMapBuilder.SettingsAssetPath}");
            }

            TerrainMaterialArtSource material = FindMaterial(
                settings,
                TerrainTileArtInspectionMapBuilder.InspectedMaterialId);
            if (material == null
                || material.GroundTopSprites == null
                || material.GroundTopSprites.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{TerrainTileArtInspectionMapBuilder.InspectedMaterialId}のGroundTop Spriteを読み込めません。");
            }

            for (int index = 0; index < material.GroundTopSprites.Length; index++)
            {
                if (material.GroundTopSprites[index] == null)
                {
                    throw new InvalidOperationException(
                        $"GroundTop配列に未設定のSpriteがあります: index={index}");
                }
            }

            Sprite wallFirstFaceSprite = LoadPreferredWallFace(material);
            Sprite wallRepeatingFaceSprite = LoadRequiredRepeatingFace(material);
            Sprite[] boundarySprites = TerrainBoundaryHighlightArtGenerator.LoadRequiredSprites();
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

            HashSet<Vector2Int> wallCellSet = BuildWallCellSet();
            if (wallCellSet.Count != WallCells.Length)
            {
                throw new InvalidOperationException("Wall比較レイアウトに重複セルがあります。");
            }

            float darkenFactor = material.DarkenWallSurface
                ? material.WallDarkenFactor
                : 1f;
            return new InspectionAssets(
                material.GroundTopSprites,
                wallFirstFaceSprite,
                wallRepeatingFaceSprite,
                boundarySprites,
                spriteLitMaterial,
                spriteUnlitMaterial,
                new Color(darkenFactor, darkenFactor, darkenFactor, 1f));
        }

        /// <summary>GrassLoamの生成済みWall用1段目Faceを読み、なければ通常Faceへ戻します。</summary>
        /// <param name="material">GrassLoamの手描き入力と生成記録です。</param>
        /// <returns>比較SceneでWallの手前へ表示するSpriteです。</returns>
        private static Sprite LoadPreferredWallFace(TerrainMaterialArtSource material)
        {
            IReadOnlyList<string> generatedPaths = material.LastGeneratedAssetPaths;
            for (int index = 0; index < generatedPaths.Count; index++)
            {
                string path = generatedPaths[index];
                if (!path.EndsWith(PreferredWallFaceFileName, StringComparison.Ordinal))
                {
                    continue;
                }

                Sprite generated = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (generated != null)
                {
                    return generated;
                }
            }

            if (material.FaceSprites != null
                && material.FaceSprites.Length > 0
                && material.FaceSprites[0] != null)
            {
                return material.FaceSprites[0];
            }

            throw new InvalidOperationException("GrassLoamのWall前面へ使えるFace Spriteがありません。");
        }

        /// <summary>
        /// Wall前面2段目以降へ繰り返す通常Faceを読みます。
        /// 境界Overlayは1段目にだけ必要なため、生成済みの1段目用Spriteはここで使いません。
        /// </summary>
        /// <param name="material">GrassLoamの手描き入力です。</param>
        /// <returns>2段目以降に反復する通常Face Spriteです。</returns>
        private static Sprite LoadRequiredRepeatingFace(TerrainMaterialArtSource material)
        {
            if (material.FaceSprites != null
                && material.FaceSprites.Length > 0
                && material.FaceSprites[0] != null)
            {
                return material.FaceSprites[0];
            }

            throw new InvalidOperationException("GrassLoamの2段目以降へ使うFace Spriteがありません。");
        }

        /// <summary>設定のMaterials配列から安定材質IDが一致する要素を探します。</summary>
        /// <param name="settings">検索対象の地形タイル生成設定です。</param>
        /// <param name="materialId">検索する安定材質IDです。</param>
        /// <returns>一致する材質、見つからなければnullです。</returns>
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

        /// <summary>空Sceneへ3条件の比較パネル、同期移動用Light、Cameraを作り、固定パスへ保存します。</summary>
        /// <param name="assets">検証済みのSprite、Material、Wall暗化色です。</param>
        private static void BuildAndOpenScene(InspectionAssets assets)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "TerrainWallShadowComparison";
            CreateCamera();

            GameObject root = new GameObject("Wall Shadow Comparison");
            CreateGlobalLight(root);
            CreateComparisonPanel(
                root,
                assets,
                "A Highlight",
                -PanelHorizontalSpacing,
                showHighlight: true,
                showContactShadow: false);
            CreateComparisonPanel(
                root,
                assets,
                "B Dynamic Only",
                0f,
                showHighlight: false,
                showContactShadow: false);
            CreateComparisonPanel(
                root,
                assets,
                "C Adopted: Contact + Dynamic",
                PanelHorizontalSpacing,
                showHighlight: false,
                showContactShadow: true);
            CreateSynchronizedPointLights(root);
            CreateInstructionLabel(root);
            ValidateBuiltScene(root);

            EnsureAssetFolder("Assets/Scenes/Development");
            if (!EditorSceneManager.SaveScene(scene, InspectionScenePath))
            {
                throw new InvalidOperationException(
                    $"Wall動的影比較Sceneを保存できませんでした: {InspectionScenePath}");
            }

            Selection.activeGameObject = root.transform
                .Find("Synchronized Lights (Move This)")
                ?.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        /// <summary>Pixel Perfect Cameraを原点へ配置し、3つの比較パネルを同時に表示します。</summary>
        private static void CreateCamera()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.2f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(22, 25, 30, 255);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            PixelPerfectCamera pixelPerfect = cameraObject.AddComponent<PixelPerfectCamera>();
            pixelPerfect.assetsPPU = AssetsPixelsPerUnit;
            pixelPerfect.refResolutionX = ReferenceResolutionX;
            pixelPerfect.refResolutionY = ReferenceResolutionY;
            pixelPerfect.cropFrame = PixelPerfectCamera.CropFrame.Windowbox;
        }

        /// <summary>影の外側も読める最低限の明るさを、3つの比較パネルへ共通して与えます。</summary>
        /// <param name="root">比較Sceneの親GameObjectです。</param>
        private static void CreateGlobalLight(GameObject root)
        {
            GameObject lightObject = CreateChild(root, "Global Light 2D");
            Light2D light = lightObject.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.intensity = GlobalLightIntensity;
            light.color = Color.white;
            light.shadowsEnabled = false;
        }

        /// <summary>Ground、暗化Wall、Face、任意の境界ハイライト、結合Shadow Casterを1パネルへ作ります。</summary>
        /// <param name="root">3つの比較パネルをまとめる親です。</param>
        /// <param name="assets">配置するSpriteとMaterialです。</param>
        /// <param name="panelName">HierarchyとGameビューへ表示する比較条件名です。</param>
        /// <param name="centerX">パネル中心のX座標です。単位はUnity Unitです。</param>
        /// <param name="showHighlight">境界ハイライトを表示する場合はtrueです。</param>
        /// <param name="showContactShadow">2段Wall向けに左右対称へ広げた固定接地影を表示する場合はtrueです。</param>
        private static void CreateComparisonPanel(
            GameObject root,
            InspectionAssets assets,
            string panelName,
            float centerX,
            bool showHighlight,
            bool showContactShadow)
        {
            GameObject panel = CreateChild(root, panelName);
            CreateLabel(panel, panelName, new Vector3(centerX, 5.3f, 0f), 100);

            for (int y = 0; y < GroundHeight; y++)
            {
                for (int x = 0; x < GroundWidth; x++)
                {
                    int localX = x - (GroundWidth / 2);
                    int localY = y - (GroundHeight / 2);
                    int variant = StableVariantIndex(localX, localY, assets.GroundTopSprites.Length);
                    CreateSpriteObject(
                        panel,
                        $"Ground_{localX:+00;-00;00}_{localY:+00;-00;00}",
                        assets.GroundTopSprites[variant],
                        new Vector3(centerX + localX, localY, 0f),
                        Color.white,
                        0,
                        assets.SpriteLitMaterial);
                }
            }

            HashSet<Vector2Int> wallCells = BuildWallCellSet();
            GameObject shadowCasterRoot = CreateChild(panel, "Composite Ground Contact Shadow Casters");
            shadowCasterRoot.AddComponent<CompositeShadowCaster2D>();
            GameObject contactShadowRoot = showContactShadow
                ? CreateChild(panel, "Symmetric Contact Shadows")
                : null;

            for (int index = 0; index < WallCells.Length; index++)
            {
                Vector2Int cell = WallCells[index];
                int variant = StableVariantIndex(cell.x + 20, cell.y + 20, assets.GroundTopSprites.Length);
                Vector3 position = new Vector3(centerX + cell.x, cell.y, -0.1f);
                if (showContactShadow)
                {
                    CreateContactShadow(
                        contactShadowRoot,
                        $"Wall Contact Shadow_{cell.x:+00;-00;00}_{cell.y:+00;-00;00}",
                        assets.GroundTopSprites[variant],
                        position,
                        assets.SpriteUnlitMaterial);
                }

                GameObject wallObject = CreateSpriteObject(
                    panel,
                    $"Wall_{cell.x:+00;-00;00}_{cell.y:+00;-00;00}",
                    assets.GroundTopSprites[variant],
                    position,
                    assets.WallTint,
                    10,
                    assets.SpriteLitMaterial);

                CreateGroundContactShadowCaster(
                    shadowCasterRoot,
                    $"Ground Contact Shadow Caster_{cell.x:+00;-00;00}_{cell.y:+00;-00;00}",
                    assets.GroundTopSprites[variant],
                    position,
                    assets.SpriteLitMaterial);

                if (showHighlight)
                {
                    CreateBoundaryHighlight(wallObject, assets, wallCells, cell, position);
                }

                if (!wallCells.Contains(cell + Vector2Int.down))
                {
                    // 本番想定の2段を1枚ずつ縦に反復し、同じFace Spriteが上下接続したときの見え方も検品します。
                    for (int faceStage = 1; faceStage <= WallFaceDepth; faceStage++)
                    {
                        // 境界OverlayはWall上面と前面の切り替わりを示す1段目だけに必要です。
                        // 2段目以降は通常Faceを使い、境界模様が段ごとに再出現するのを防ぎます。
                        Sprite faceSprite = faceStage == 1
                            ? assets.WallFirstFaceSprite
                            : assets.WallRepeatingFaceSprite;
                        Vector3 facePosition = new Vector3(
                            position.x,
                            position.y - faceStage,
                            -0.3f);

                        // 固定接地影はWall全体の根元を示す表現です。
                        // 1段目と2段目の継ぎ目に偽の影を作らないよう、最下段のFaceにだけ配置します。
                        if (showContactShadow && faceStage == WallFaceDepth)
                        {
                            CreateContactShadow(
                                contactShadowRoot,
                                $"Face Contact Shadow_{cell.x:+00;-00;00}_{cell.y:+00;-00;00}",
                                faceSprite,
                                facePosition,
                                assets.SpriteUnlitMaterial);
                        }

                        CreateSpriteObject(
                            panel,
                            $"Face_{cell.x:+00;-00;00}_{cell.y:+00;-00;00}_Stage_{faceStage:00}",
                            faceSprite,
                            facePosition,
                            Color.white,
                            30,
                            assets.SpriteLitMaterial);
                    }
                }
            }
        }

        /// <summary>
        /// Wall上面とは別の透明Spriteから、設置面を起点とする動的影用Shadow Caster 2Dを作ります。
        /// 表示Spriteへ直接Casterを付けると上面から影が伸びるため、Wall前面の段数だけ手前へ移した代理輪郭を使います。
        /// </summary>
        /// <param name="parent">1区画分のCasterを結合するComposite Shadow Caster 2Dの親です。</param>
        /// <param name="name">Hierarchyへ表示する代理Caster名です。</param>
        /// <param name="sourceSprite">Wall上面と同じ16×16 pxの不透明輪郭を持つSpriteです。</param>
        /// <param name="wallTopPosition">対応するWall上面のワールド座標です。</param>
        /// <param name="material">透明な代理Spriteへ設定するURP 2D Lit Materialです。</param>
        private static void CreateGroundContactShadowCaster(
            GameObject parent,
            string name,
            Sprite sourceSprite,
            Vector3 wallTopPosition,
            Material material)
        {
            Vector3 groundContactPosition = new Vector3(
                wallTopPosition.x,
                wallTopPosition.y + DynamicShadowCasterOffsetYInTiles,
                wallTopPosition.z);

            // Shadow Caster 2DがSprite輪郭を取得できるよう、透明なSpriteRendererを先に作ります。
            // 色を完全透明にして表示から隠し、Collider2Dを追加しないことで物理判定と描画用の遮蔽輪郭を分離します。
            GameObject casterObject = CreateSpriteObject(
                parent,
                name,
                sourceSprite,
                groundContactPosition,
                Color.clear,
                -100,
                material);
            ShadowCaster2D caster = casterObject.AddComponent<ShadowCaster2D>();
            caster.castingOption = ShadowCaster2D.ShadowCastingOptions.CastShadow;
            caster.trimEdge = 0f;
        }

        /// <summary>
        /// 元Spriteの不透明形状を左右へ1pxずつ、手前へ3pxずらし、実Spriteの背後からはみ出す部分を固定接地影として見せます。
        /// 動的な投影影を置き換えるものではなく、環境光が強い場合にもブロックの接地点を読むための比較条件です。
        /// </summary>
        /// <param name="parent">接地影だけをまとめる親GameObjectです。</param>
        /// <param name="name">Hierarchyへ表示する名前です。</param>
        /// <param name="sourceSprite">不透明形状を接地影へ転用するWall上面またはFace Spriteです。</param>
        /// <param name="sourcePosition">元Spriteのワールド座標です。</param>
        /// <param name="material">照明の影響を受けないURP 2D Unlit Materialです。</param>
        private static void CreateContactShadow(
            GameObject parent,
            string name,
            Sprite sourceSprite,
            Vector3 sourcePosition,
            Material material)
        {
            // 元形状を左右対称に2枚重ね、上方からの基準光で左右どちらかに偏らない接地影を作ります。
            // 重なる中央と単独の外縁で濃さが変わるため、別のぼかしTextureを追加せず弱いソフトさを比較できます。
            for (int horizontalSign = -1; horizontalSign <= 1; horizontalSign += 2)
            {
                Vector3 pixelOffset = new Vector3(
                    horizontalSign * ContactShadowHorizontalSpreadPixels
                        / (float)AssetsPixelsPerUnit,
                    ContactShadowOffsetYPixels / (float)AssetsPixelsPerUnit,
                    0.05f);
                string sideName = horizontalSign < 0 ? "Left" : "Right";
                CreateSpriteObject(
                    parent,
                    $"{name}_{sideName}",
                    sourceSprite,
                    sourcePosition + pixelOffset,
                    new Color(0f, 0f, 0f, ContactShadowLayerOpacity),
                    5,
                    material);
            }
        }

        /// <summary>1つのWallセルについて、隣接状態が要求する境界Spriteと回転を追加します。</summary>
        /// <param name="wallObject">Wall上面とShadow Caster 2Dを持つ親です。</param>
        /// <param name="assets">14基本境界Spriteと暗化色です。</param>
        /// <param name="wallCells">同じWall領域に属するセル集合です。</param>
        /// <param name="cell">境界を求めるWallセル座標です。</param>
        /// <param name="position">Wallセルのワールド座標です。</param>
        private static void CreateBoundaryHighlight(
            GameObject wallObject,
            InspectionAssets assets,
            HashSet<Vector2Int> wallCells,
            Vector2Int cell,
            Vector3 position)
        {
            BoundaryNeighborConnection connections = BuildConnections(wallCells, cell);
            BoundaryHighlightSelection selection =
                TerrainBoundaryHighlightUtility.SelectForConnections(connections);
            if (!selection.HasHighlight)
            {
                return;
            }

            int spriteIndex = (int)selection.Shape - 1;
            GameObject highlight = CreateSpriteObject(
                wallObject,
                "Boundary Highlight",
                assets.BoundarySprites[spriteIndex],
                new Vector3(position.x, position.y, -0.2f),
                new Color(
                    assets.WallTint.r,
                    assets.WallTint.g,
                    assets.WallTint.b,
                    TerrainBoundaryHighlightUtility.DefaultOpacity),
                20,
                assets.SpriteLitMaterial);
            highlight.transform.rotation = Quaternion.Euler(
                0f,
                0f,
                -90f * selection.QuarterTurnsClockwise);
        }

        /// <summary>
        /// 3つのパネルへ上方の主光と左手前の検証光を作り、同じ親の移動量で同期させます。
        /// グループを個別に有効・無効化すると、上方光だけと左手前光を追加した状態を比較できます。
        /// </summary>
        /// <param name="root">比較Sceneの親GameObjectです。</param>
        private static void CreateSynchronizedPointLights(GameObject root)
        {
            GameObject lightsRoot = CreateChild(root, "Synchronized Lights (Move This)");
            GameObject topLights = CreateChild(lightsRoot, "Top Lights");
            GameObject frontLeftLights = CreateChild(lightsRoot, "Front Left Lights (Toggle This)");

            CreatePointLight(topLights, "A Top Light 2D", -PanelHorizontalSpacing, 3f, 1f);
            CreatePointLight(topLights, "B Top Light 2D", 0f, 3f, 1f);
            CreatePointLight(topLights, "C Top Light 2D", PanelHorizontalSpacing, 3f, 1f);

            CreatePointLight(
                frontLeftLights,
                "A Front Left Light 2D",
                -PanelHorizontalSpacing - 2.5f,
                -3.5f,
                FrontLeftLightIntensity);
            CreatePointLight(
                frontLeftLights,
                "B Front Left Light 2D",
                -2.5f,
                -3.5f,
                FrontLeftLightIntensity);
            CreatePointLight(
                frontLeftLights,
                "C Front Left Light 2D",
                PanelHorizontalSpacing - 2.5f,
                -3.5f,
                FrontLeftLightIntensity);
        }

        /// <summary>1パネルを照らし、WallのComposite輪郭から動的影を投影するPoint Light 2Dを作ります。</summary>
        /// <param name="parent">同期移動させるLightの共通親です。</param>
        /// <param name="name">Hierarchyへ表示するLight名です。</param>
        /// <param name="x">Lightの初期X座標です。単位はUnity Unitです。</param>
        /// <param name="y">Lightの初期Y座標です。単位はUnity Unitです。</param>
        /// <param name="intensity">Lightの強さです。主光と1、検証光でそれより弱い値を使います。</param>
        private static void CreatePointLight(
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
            light.pointLightOuterRadius = PointLightOuterRadius;
            light.falloffIntensity = 0.5f;
            light.shadowsEnabled = true;
            light.shadowIntensity = PointLightShadowIntensity;
            light.shadowSoftness = PointLightShadowSoftness;
        }

        /// <summary>Sceneビューで動かすべきLight親と、比較条件をGameビュー下部へ表示します。</summary>
        /// <param name="root">比較Sceneの親GameObjectです。</param>
        private static void CreateInstructionLabel(GameObject root)
        {
            CreateLabel(
                root,
                "Move or Toggle Synchronized Lights in Scene View",
                new Vector3(0f, -6.1f, 0f),
                100);
        }

        /// <summary>構築したSceneが比較条件どおりのComponent数と設定を持ち、Collider2Dを含まないことを確認します。</summary>
        /// <param name="root">構築直後の比較Sceneルートです。</param>
        private static void ValidateBuiltScene(GameObject root)
        {
            TerrainWallShadowComparisonSummary summary = ValidateAssetsAndLayout();
            ShadowCaster2D[] casters = root.GetComponentsInChildren<ShadowCaster2D>(true);
            CompositeShadowCaster2D[] composites =
                root.GetComponentsInChildren<CompositeShadowCaster2D>(true);
            Light2D[] lights = root.GetComponentsInChildren<Light2D>(true);
            Collider2D[] colliders = root.GetComponentsInChildren<Collider2D>(true);
            Transform contactShadowRoot = root.transform.Find(
                "C Adopted: Contact + Dynamic/Symmetric Contact Shadows");
            int contactShadowSpriteCount = contactShadowRoot == null
                ? 0
                : contactShadowRoot.GetComponentsInChildren<SpriteRenderer>(true).Length;

            if (casters.Length != summary.ShadowCasterCount)
            {
                throw new InvalidOperationException(
                    $"Shadow Caster 2D数が不正です: expected={summary.ShadowCasterCount}, actual={casters.Length}");
            }

            if (composites.Length != summary.CompositeShadowCasterCount)
            {
                throw new InvalidOperationException(
                    $"Composite Shadow Caster 2D数が不正です: expected={summary.CompositeShadowCasterCount}, actual={composites.Length}");
            }

            // Global 1個と、3区画それぞれの上方光・左手前検証光を合わせたPoint 6個を要求します。
            if (lights.Length != summary.PointLightCount + 1)
            {
                throw new InvalidOperationException(
                    $"Light 2D数が不正です: expected={summary.PointLightCount + 1}, actual={lights.Length}");
            }

            if (colliders.Length != 0)
            {
                throw new InvalidOperationException(
                    "Wall動的影比較SceneへCollider2Dを追加してはいけません。");
            }

            if (contactShadowSpriteCount != summary.ContactShadowSpriteCount)
            {
                throw new InvalidOperationException(
                    $"固定接地影Sprite数が不正です: expected={summary.ContactShadowSpriteCount}, actual={contactShadowSpriteCount}");
            }

            for (int index = 0; index < casters.Length; index++)
            {
                if (!casters[index].castsShadows || casters[index].selfShadows)
                {
                    throw new InvalidOperationException(
                        $"Shadow Caster 2DはCastShadow専用である必要があります: index={index}");
                }
            }
        }

        /// <summary>固定配列から重複のないWallセル集合を作ります。</summary>
        /// <returns>8近傍判定と露出Face判定へ使うWallセル集合です。</returns>
        private static HashSet<Vector2Int> BuildWallCellSet()
        {
            return new HashSet<Vector2Int>(WallCells);
        }

        /// <summary>南側にWallがなく、手前Faceを表示するセル数を数えます。</summary>
        /// <param name="wallCells">Wall領域に属するセル集合です。</param>
        /// <returns>1パネルで表示するFace数です。</returns>
        private static int CountExposedSouthFaces(HashSet<Vector2Int> wallCells)
        {
            int count = 0;
            for (int index = 0; index < WallCells.Length; index++)
            {
                if (!wallCells.Contains(WallCells[index] + Vector2Int.down))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>指定Wallセルと同じ領域に属する8近傍を接続bitへ変換します。</summary>
        /// <param name="wallCells">Wall領域に属するセル集合です。</param>
        /// <param name="cell">接続状態を求める中心セルです。</param>
        /// <returns>境界ハイライト選択へ渡す8方向接続bitです。</returns>
        private static BoundaryNeighborConnection BuildConnections(
            HashSet<Vector2Int> wallCells,
            Vector2Int cell)
        {
            BoundaryNeighborConnection connections = BoundaryNeighborConnection.None;
            AddConnectionIfPresent(
                wallCells,
                cell + Vector2Int.up,
                BoundaryNeighborConnection.North,
                ref connections);
            AddConnectionIfPresent(
                wallCells,
                cell + Vector2Int.right,
                BoundaryNeighborConnection.East,
                ref connections);
            AddConnectionIfPresent(
                wallCells,
                cell + Vector2Int.down,
                BoundaryNeighborConnection.South,
                ref connections);
            AddConnectionIfPresent(
                wallCells,
                cell + Vector2Int.left,
                BoundaryNeighborConnection.West,
                ref connections);
            AddConnectionIfPresent(
                wallCells,
                cell + new Vector2Int(1, 1),
                BoundaryNeighborConnection.NorthEast,
                ref connections);
            AddConnectionIfPresent(
                wallCells,
                cell + new Vector2Int(1, -1),
                BoundaryNeighborConnection.SouthEast,
                ref connections);
            AddConnectionIfPresent(
                wallCells,
                cell + new Vector2Int(-1, -1),
                BoundaryNeighborConnection.SouthWest,
                ref connections);
            AddConnectionIfPresent(
                wallCells,
                cell + new Vector2Int(-1, 1),
                BoundaryNeighborConnection.NorthWest,
                ref connections);
            return connections;
        }

        /// <summary>隣接座標がWall集合にあれば、対応する接続bitを追加します。</summary>
        /// <param name="wallCells">Wall領域に属するセル集合です。</param>
        /// <param name="neighbor">調べる隣接セル座標です。</param>
        /// <param name="connection">存在時に追加する方向bitです。</param>
        /// <param name="connections">現在までに見つかった接続bitです。</param>
        private static void AddConnectionIfPresent(
            HashSet<Vector2Int> wallCells,
            Vector2Int neighbor,
            BoundaryNeighborConnection connection,
            ref BoundaryNeighborConnection connections)
        {
            if (wallCells.Contains(neighbor))
            {
                connections |= connection;
            }
        }

        /// <summary>座標から安定した差分Indexを算出し、Scene再構築時にも配置を変えません。</summary>
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

            // 大きさの異なる素数で二次元座標を混ぜ、単純な縦横周期を避けます。
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

        /// <summary>指定Sprite、Tint、Lit Material、描画順を持つSpriteRendererを作ります。</summary>
        /// <param name="parent">親Groupです。</param>
        /// <param name="name">Hierarchyへ表示する名前です。</param>
        /// <param name="sprite">描画するSpriteです。</param>
        /// <param name="position">ワールド座標です。</param>
        /// <param name="color">SpriteRendererへ設定する乗算色です。</param>
        /// <param name="sortingOrder">同じSorting Layer内の描画順です。</param>
        /// <param name="material">Light 2Dの影響を受ける共有Materialです。</param>
        /// <returns>追加Componentや子オブジェクトを設定できる作成済みGameObjectです。</returns>
        private static GameObject CreateSpriteObject(
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
            renderer.sharedMaterial = material;
            return gameObject;
        }

        /// <summary>比較条件や操作方法を、Gameビューで読めるTextMeshとして作ります。</summary>
        /// <param name="parent">ラベルをまとめる親GameObjectです。</param>
        /// <param name="text">表示する短い英語ラベルです。</param>
        /// <param name="position">ワールド座標です。</param>
        /// <param name="sortingOrder">Spriteより前へ表示する描画順です。</param>
        private static void CreateLabel(
            GameObject parent,
            string text,
            Vector3 position,
            int sortingOrder)
        {
            GameObject labelObject = new GameObject($"Label {text}");
            labelObject.transform.SetParent(parent.transform, false);
            labelObject.transform.position = position;
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 32;
            label.characterSize = 0.25f;
            label.color = Color.white;
            MeshRenderer renderer = labelObject.GetComponent<MeshRenderer>();
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
