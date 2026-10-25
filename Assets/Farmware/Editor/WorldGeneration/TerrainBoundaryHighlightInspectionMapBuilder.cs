// このファイルは、共通境界ハイライトの47接続状態をGround／Wallへ固定配置した検品Sceneを構築します。
// Runtimeの地形や接続判定を変更せず、14基本Sprite・回転・20%表示・Wall暗化の組合せを人間が確認するためのEditor専用ツールです。

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
    /// 境界検品Sceneを作る前に確認したSprite数と47接続状態数をまとめます。
    /// EditModeテストからSceneを変更せず前提だけを検証するために使用します。
    /// </summary>
    public readonly struct TerrainBoundaryHighlightInspectionSummary
    {
        /// <summary>読み込めた14基本Sprite数です。</summary>
        public int BasicSpriteCount { get; }

        /// <summary>256通りの入力を正規化して得た有効接続状態数です。</summary>
        public int NormalizedStateCount { get; }

        /// <summary>境界を表示する接続状態数です。完全接続だけは含みません。</summary>
        public int VisibleHighlightStateCount { get; }

        /// <summary>検証済みの件数を保存します。</summary>
        /// <param name="basicSpriteCount">14基本Sprite数です。</param>
        /// <param name="normalizedStateCount">正規化済み接続状態数です。</param>
        /// <param name="visibleHighlightStateCount">境界を表示する状態数です。</param>
        public TerrainBoundaryHighlightInspectionSummary(
            int basicSpriteCount,
            int normalizedStateCount,
            int visibleHighlightStateCount)
        {
            BasicSpriteCount = basicSpriteCount;
            NormalizedStateCount = normalizedStateCount;
            VisibleHighlightStateCount = visibleHighlightStateCount;
        }
    }

    /// <summary>
    /// GrassLoamのGroundTopを背景に、47接続状態をGround色と0.85暗化Wall色で左右へ一覧表示します。
    /// 各オブジェクト名へ接続Mask、基本形、回転数を含め、Hierarchyから個別状態を追跡できます。
    /// </summary>
    public static class TerrainBoundaryHighlightInspectionMapBuilder
    {
        /// <summary>境界ハイライト固定検品Sceneの保存先です。</summary>
        public const string InspectionScenePath =
            "Assets/Scenes/Development/TerrainBoundaryHighlightInspection.unity";

        /// <summary>背景へ使うGrassLoamを含む地形タイル生成設定です。</summary>
        private const string SettingsAssetPath =
            "Assets/Farmware/Art/WorldGeneration/Terrain/Settings/TerrainTileArtGenerationSettings.asset";

        /// <summary>背景材質として検索する安定材質IDです。</summary>
        private const string InspectedMaterialId = "GrassLoam";

        /// <summary>Pixel Perfect CameraとSprite Importで共有する1Unit当たりの画素数です。</summary>
        private const int AssetsPixelsPerUnit = 16;

        /// <summary>検品SceneのPixel Perfect基準解像度です。単位はピクセルです。</summary>
        private const int ReferenceResolutionX = 480;

        /// <summary>検品SceneのPixel Perfect基準解像度です。単位はピクセルです。</summary>
        private const int ReferenceResolutionY = 270;

        /// <summary>1領域につき横へ並べる接続状態数です。</summary>
        private const int ColumnCount = 8;

        /// <summary>隣り合う検品セルの中心間隔です。単位はUnity Unitです。</summary>
        private const float CellSpacing = 1.2f;

        /// <summary>Ground一覧とWall一覧の中心を分ける水平距離です。単位はUnity Unitです。</summary>
        private const float RegionCenterOffset = 5.5f;

        /// <summary>Scene構築で使う検証済みSpriteとWall色です。</summary>
        private sealed class InspectionAssets
        {
            /// <summary>47状態の背景へ使うGrassLoam上面Spriteです。</summary>
            public Sprite GroundTopSprite { get; }

            /// <summary>Shape01〜Shape14の順に並ぶ共通境界Spriteです。</summary>
            public Sprite[] BoundarySprites { get; }

            /// <summary>Wall上面へ掛けるRGB乗算色です。</summary>
            public Color WallTint { get; }

            /// <summary>正規化済み接続Maskと選択結果の安定順一覧です。</summary>
            public IReadOnlyList<StateEntry> States { get; }

            /// <summary>Scene構築に必要な検証済み値をまとめます。</summary>
            /// <param name="groundTopSprite">GrassLoam上面Spriteです。</param>
            /// <param name="boundarySprites">14基本境界Spriteです。</param>
            /// <param name="wallTint">Wall上面の乗算色です。</param>
            /// <param name="states">47接続状態です。</param>
            public InspectionAssets(
                Sprite groundTopSprite,
                Sprite[] boundarySprites,
                Color wallTint,
                IReadOnlyList<StateEntry> states)
            {
                GroundTopSprite = groundTopSprite;
                BoundarySprites = boundarySprites;
                WallTint = wallTint;
                States = states;
            }
        }

        /// <summary>1つの正規化済み接続Maskと、そのMaskが選ぶ基本形・回転を保持します。</summary>
        private readonly struct StateEntry
        {
            /// <summary>8方向を1bitずつ表す正規化済み接続Maskです。</summary>
            public byte Connections { get; }

            /// <summary>14基本形と時計回り回転数です。</summary>
            public BoundaryHighlightSelection Selection { get; }

            /// <summary>接続Maskと選択結果を1件として保存します。</summary>
            /// <param name="connections">正規化済み接続Maskです。</param>
            /// <param name="selection">対応する境界選択です。</param>
            public StateEntry(byte connections, BoundaryHighlightSelection selection)
            {
                Connections = connections;
                Selection = selection;
            }
        }

        /// <summary>未保存Sceneを保護してから、47状態の固定検品Sceneを再構築して開きます。</summary>
        [MenuItem("Farmware/World Generation/Open Boundary Highlight Inspection Map")]
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
                    "境界ハイライト検品Sceneを構築できません",
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
                    "境界ハイライト検品Sceneを構築できません",
                    exception.Message,
                    "閉じる");
            }
        }

        /// <summary>CLIや自動処理から、47状態の固定検品Sceneを再構築する入口です。</summary>
        public static void Execute()
        {
            BuildAndOpenScene(LoadAndValidateAssets());
        }

        /// <summary>Sceneを変更せず、14基本Spriteと47接続状態の件数を検証します。</summary>
        /// <returns>検証済み件数をまとめた値です。</returns>
        public static TerrainBoundaryHighlightInspectionSummary ValidateAssetsAndStates()
        {
            InspectionAssets assets = LoadAndValidateAssets();
            int visibleCount = 0;
            for (int index = 0; index < assets.States.Count; index++)
            {
                if (assets.States[index].Selection.HasHighlight)
                {
                    visibleCount++;
                }
            }

            return new TerrainBoundaryHighlightInspectionSummary(
                assets.BoundarySprites.Length,
                assets.States.Count,
                visibleCount);
        }

        /// <summary>GrassLoam上面、14基本境界Sprite、47接続状態を読み込み、Scene構築前に検証します。</summary>
        /// <returns>Sceneへそのまま配置できる検証済みデータです。</returns>
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
            if (material == null
                || material.GroundTopSprites == null
                || material.GroundTopSprites.Length == 0
                || material.GroundTopSprites[0] == null)
            {
                throw new InvalidOperationException(
                    $"{InspectedMaterialId}のGroundTop Spriteを読み込めません。");
            }

            Sprite[] boundarySprites = TerrainBoundaryHighlightArtGenerator.LoadRequiredSprites();
            IReadOnlyList<StateEntry> states = BuildNormalizedStates();
            if (states.Count != 47)
            {
                throw new InvalidOperationException(
                    $"境界ハイライトの正規化済み接続状態は47件必要です: actual={states.Count}");
            }

            float darkenFactor = material.DarkenWallSurface
                ? material.WallDarkenFactor
                : 1f;
            return new InspectionAssets(
                material.GroundTopSprites[0],
                boundarySprites,
                new Color(darkenFactor, darkenFactor, darkenFactor, 1f),
                states);
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

        /// <summary>256通りの入力を正規化して重複を除き、Maskの昇順で47状態を返します。</summary>
        /// <returns>再構築のたびに同じ順序になる47状態です。</returns>
        private static IReadOnlyList<StateEntry> BuildNormalizedStates()
        {
            var uniqueMasks = new SortedSet<byte>();
            for (int raw = byte.MinValue; raw <= byte.MaxValue; raw++)
            {
                byte normalized = (byte)TerrainBoundaryHighlightUtility.NormalizeConnections(
                    (BoundaryNeighborConnection)(byte)raw);
                uniqueMasks.Add(normalized);
            }

            var states = new List<StateEntry>(uniqueMasks.Count);
            foreach (byte connections in uniqueMasks)
            {
                states.Add(new StateEntry(
                    connections,
                    TerrainBoundaryHighlightUtility.SelectForConnections(
                        (BoundaryNeighborConnection)connections)));
            }

            return states;
        }

        /// <summary>空SceneへCameraとGround／Wallの47状態一覧を配置し、固定パスへ保存します。</summary>
        /// <param name="assets">検証済みのSpriteと接続状態です。</param>
        private static void BuildAndOpenScene(InspectionAssets assets)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "TerrainBoundaryHighlightInspection";
            CreateCamera();

            GameObject root = new GameObject("Boundary Highlight Inspection");
            CreateRegion(
                root,
                "Ground 47 States",
                assets,
                -RegionCenterOffset,
                Color.white);
            CreateRegion(
                root,
                "Wall 47 States",
                assets,
                RegionCenterOffset,
                assets.WallTint);

            EnsureAssetFolder("Assets/Scenes/Development");
            if (!EditorSceneManager.SaveScene(scene, InspectionScenePath))
            {
                throw new InvalidOperationException(
                    $"境界ハイライト検品Sceneを保存できませんでした: {InspectionScenePath}");
            }

            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        /// <summary>Pixel Perfect Cameraを原点へ配置し、左右の47状態一覧を同時に表示します。</summary>
        private static void CreateCamera()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 6f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(22, 25, 30, 255);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            PixelPerfectCamera pixelPerfect = cameraObject.AddComponent<PixelPerfectCamera>();
            pixelPerfect.assetsPPU = AssetsPixelsPerUnit;
            pixelPerfect.refResolutionX = ReferenceResolutionX;
            pixelPerfect.refResolutionY = ReferenceResolutionY;
            pixelPerfect.cropFrame = PixelPerfectCamera.CropFrame.Windowbox;
        }

        /// <summary>47状態を8列へ並べ、同じ背景色で基本形と回転を比較できる一覧を作ります。</summary>
        /// <param name="root">Ground／Wall一覧をまとめる親です。</param>
        /// <param name="regionName">Hierarchyへ表示する領域名です。</param>
        /// <param name="assets">配置するSpriteと47状態です。</param>
        /// <param name="centerX">一覧の水平中心です。単位はUnity Unitです。</param>
        /// <param name="baseTint">GroundまたはWall上面へ掛ける乗算色です。</param>
        private static void CreateRegion(
            GameObject root,
            string regionName,
            InspectionAssets assets,
            float centerX,
            Color baseTint)
        {
            GameObject region = new GameObject(regionName);
            region.transform.SetParent(root.transform, false);
            for (int index = 0; index < assets.States.Count; index++)
            {
                StateEntry state = assets.States[index];
                int column = index % ColumnCount;
                int row = index / ColumnCount;
                float x = centerX + (column - (ColumnCount - 1) * 0.5f) * CellSpacing;
                float y = (2.5f - row) * CellSpacing;
                string stateName = state.Selection.HasHighlight
                    ? $"Mask_{state.Connections:X2}_Shape_{(int)state.Selection.Shape:00}_R{state.Selection.QuarterTurnsClockwise}"
                    : $"Mask_{state.Connections:X2}_NoHighlight";

                GameObject stateObject = new GameObject(stateName);
                stateObject.transform.SetParent(region.transform, false);
                CreateSpriteRenderer(
                    stateObject,
                    "Base",
                    assets.GroundTopSprite,
                    new Vector3(x, y, 0f),
                    baseTint,
                    0,
                    0f);

                if (state.Selection.HasHighlight)
                {
                    int shapeIndex = (int)state.Selection.Shape - 1;
                    CreateSpriteRenderer(
                        stateObject,
                        "Highlight",
                        assets.BoundarySprites[shapeIndex],
                        new Vector3(x, y, -0.1f),
                        // Wall側ではBaseと同じ暗化係数をRGBへ掛けます。描画順は手前のままなので、
                        // 「通常色へハイライトを合成してから全体を暗化する」見た目を追加Shaderなしで再現できます。
                        new Color(
                            baseTint.r,
                            baseTint.g,
                            baseTint.b,
                            TerrainBoundaryHighlightUtility.DefaultOpacity),
                        1,
                        -90f * state.Selection.QuarterTurnsClockwise);
                }
            }
        }

        /// <summary>指定Sprite、Tint、描画順、回転を持つSpriteRendererを作ります。</summary>
        /// <param name="parent">状態をまとめる親GameObjectです。</param>
        /// <param name="name">子GameObject名です。</param>
        /// <param name="sprite">描画するSpriteです。</param>
        /// <param name="position">ワールド座標です。</param>
        /// <param name="color">SpriteRendererへ設定する乗算色です。</param>
        /// <param name="sortingOrder">同じSorting Layer内の描画順です。</param>
        /// <param name="rotationDegrees">Z軸へ適用する角度です。単位は度です。</param>
        private static void CreateSpriteRenderer(
            GameObject parent,
            string name,
            Sprite sprite,
            Vector3 position,
            Color color,
            int sortingOrder,
            float rotationDegrees)
        {
            GameObject gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent.transform, false);
            gameObject.transform.position = position;
            gameObject.transform.rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
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
