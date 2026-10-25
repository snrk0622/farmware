// このファイルは、GrassLoamの地形材質Spriteを固定配置した検品Sceneを再現可能に構築します。
// 地形Simulationには接続せず、GroundTop共有Wall、暗色Tint、Face、境界合成をGameビューで確認するEditor専用ツールです。

using System;
using System.Collections.Generic;
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

        /// <summary>構築前検証で確認した各用途のSprite数をまとめます。</summary>
        /// <param name="groundTopCount">GroundTop差分数です。</param>
        /// <param name="faceCount">通常Face差分数です。</param>
        /// <param name="groundBoundaryCount">Ground用境界合成結果数です。</param>
        /// <param name="wallBoundaryCount">Wall用境界合成結果数です。</param>
        public TerrainTileArtInspectionAssetSummary(
            int groundTopCount,
            int faceCount,
            int groundBoundaryCount,
            int wallBoundaryCount)
        {
            GroundTopCount = groundTopCount;
            FaceCount = faceCount;
            GroundBoundaryCount = groundBoundaryCount;
            WallBoundaryCount = wallBoundaryCount;
        }
    }

    /// <summary>
    /// GrassLoamの反復、Ground上へ重ねたWall、断面段数、Ground／Wall境界を固定配置します。
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

        // Pixel Perfect Cameraと全Spriteで共有する基準値です。
        private const int AssetsPixelsPerUnit = 16;
        private const int ReferenceResolutionX = 480;
        private const int ReferenceResolutionY = 270;

        // GroundTop反復感を見る矩形と、Wallパッチの寸法です。
        private const int GroundRepeatWidth = 12;
        private const int GroundRepeatHeight = 7;
        private const int WallPatchWidth = 5;
        private const int WallPatchHeight = 3;

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

            /// <summary>Wall上面へ掛けるRGB乗算係数です。</summary>
            public float WallDarkenFactor { get; }

            /// <summary>検証済み素材と壁暗化設定をScene構築用にまとめます。</summary>
            /// <param name="groundTopSprites">GroundとWall上面で共有する差分です。</param>
            /// <param name="faceSprites">共通Face差分です。</param>
            /// <param name="groundBoundarySprites">Ground用境界合成結果です。</param>
            /// <param name="wallBoundarySprites">Wall用境界合成結果です。</param>
            /// <param name="wallDarkenFactor">Wall上面のRGB乗算係数です。</param>
            public InspectionAssets(
                Sprite[] groundTopSprites,
                Sprite[] faceSprites,
                Sprite[] groundBoundarySprites,
                Sprite[] wallBoundarySprites,
                float wallDarkenFactor)
            {
                GroundTopSprites = groundTopSprites;
                FaceSprites = faceSprites;
                GroundBoundarySprites = groundBoundarySprites;
                WallBoundarySprites = wallBoundarySprites;
                WallDarkenFactor = wallDarkenFactor;
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
                assets.WallBoundarySprites.Length);
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
            return new InspectionAssets(
                material.GroundTopSprites,
                material.FaceSprites,
                groundBoundary.ToArray(),
                wallBoundarySprites,
                material.DarkenWallSurface ? material.WallDarkenFactor : 1f);
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
                CreateGroundAndWallInspection(root, assets, assets.WallDarkenFactor, 0f);
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
            pixelPerfect.assetsPPU = AssetsPixelsPerUnit;
            pixelPerfect.refResolutionX = ReferenceResolutionX;
            pixelPerfect.refResolutionY = ReferenceResolutionY;

            // 基準解像度と異なるGameビューでは上下左右を黒帯で囲み、整数倍率の画素を保ちます。
            // 旧cropFrameX／YはUnity 6で非推奨なので、両方向をまとめる現行APIを使用します。
            pixelPerfect.cropFrame = PixelPerfectCamera.CropFrame.Windowbox;
        }

        /// <summary>Ground反復面の上へ同じSpriteを使ったWallパッチとFaceを重ねます。</summary>
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
                        Sprite face = assets.FaceSprites[x % assets.FaceSprites.Length];
                        CreateSpriteObject(
                            group,
                            $"WallFace_{x:D2}",
                            face,
                            new Vector3(worldX, worldY - 1f, -0.2f),
                            Color.white,
                            20);
                    }
                }
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
                    new Vector3(x, -5f, -0.1f),
                    Color.white,
                    30);
                CreateSpriteObject(
                    group,
                    $"WallBoundary_{index:D2}",
                    assets.WallBoundarySprites[index],
                    new Vector3(x, -6.2f, -0.1f),
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
