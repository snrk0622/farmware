// このファイルは、水面反射なし／ありを同じGrassLoam地形と淡水で比較する検品Sceneを構築します。
// 地形Simulationや本番の液体描画には接続せず、垂直面だけを上下反転する反射Proxy方式を検証します。

using System;
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
    /// GrassLoamの岸とWallを映す、水面反射あり／なし比較Sceneを再現可能に構築します。
    /// EditorのOpenメニューは保存済みSceneを変更せずに開き、明示的な再構築とCLIだけが検証用SceneとMaterialを更新します。
    /// </summary>
    public static class TerrainWaterReflectionComparisonMapBuilder
    {
        /// <summary>比較Sceneの保存先です。</summary>
        public const string ScenePath =
            "Assets/Scenes/Development/TerrainWaterReflectionComparison.unity";

        /// <summary>反射対象Spriteだけを水際で反転・着色する検証用Shaderです。</summary>
        public const string ReflectionShaderPath =
            "Assets/Farmware/Shaders/Development/WaterSurfaceReflection.shader";

        /// <summary>比較Sceneが参照する生成Materialの保存先です。</summary>
        public const string ReflectionMaterialPath =
            "Assets/Farmware/Art/WorldGeneration/Generated/Inspection/WaterSurfaceReflection.mat";

        /// <summary>反射と波線を、通常地形のDefaultより後に描くSorting Layer名です。</summary>
        public const string WaterSurfaceSortingLayerName = "WaterSurface";

        /// <summary>背の高い対象でも反射を表示する奥岸からの上限深さです。単位は画素です。</summary>
        public const int MaximumReflectionDepthPixels = 32;

        /// <summary>反射像を斜め見下ろしへ合わせて縦方向へ縮める倍率です。</summary>
        public const float VerticalCompression = 0.75f;

        /// <summary>反射が水面色や海底を隠しすぎないための最大Alphaです。</summary>
        public const float MaximumReflectionAlpha = 0.22f;

        /// <summary>反射像を左右へ動かす整数Pixelアニメーションの速度です。</summary>
        public const float WavePixelsPerSecond = 4f;

        // SpriteとWorld座標の変換で使う、1 unit当たりの画素数です。
        private const int AssetsPixelsPerUnit = 16;

        // Pixel Perfect Cameraへ設定する基準解像度の横幅です。単位は画素です。
        private const int ReferenceResolutionX = 480;

        // Pixel Perfect Cameraへ設定する基準解像度の高さです。単位は画素です。
        private const int ReferenceResolutionY = 270;

        // 左右各区画で並べる水面の横セル数です。
        private const int WaterWidthTiles = 5;

        // 左右各区画で並べる水面の手前方向セル数です。
        private const int WaterDepthTiles = 4;

        // Wall前面を水面へ反転する基準となる奥岸のWorld Y座標です。
        private const float ReflectionAxisWorldY = 0.5f;

        // 反射なし区画の中心World X座標です。
        private const float WithoutReflectionCenterX = -4f;

        // 反射あり区画の中心World X座標です。
        private const float WithReflectionCenterX = 4f;

        /// <summary>未保存Sceneを保護したうえで、保存済みの水面反射比較Sceneを開きます。</summary>
        [MenuItem("Farmware/World Generation/Inspection Scenes/Open Water Reflection Comparison")]
        public static void BuildAndOpenFromMenu()
        {
            OpenOrRebuildFromMenu(forceRebuild: false);
        }

        /// <summary>素材または配置規則を変更したときだけ、水面反射比較Sceneを明示的に再構築します。</summary>
        [MenuItem("Farmware/World Generation/Generation/Rebuild Water Reflection Comparison")]
        public static void RebuildFromMenu()
        {
            OpenOrRebuildFromMenu(forceRebuild: true);
        }

        /// <summary>入力を検証し、通常は保存済みSceneを開き、明示指定時だけ再構築します。</summary>
        /// <param name="forceRebuild">既存SceneがあってもMaterialとSceneを生成し直す場合はtrueです。</param>
        private static void OpenOrRebuildFromMenu(bool forceRebuild)
        {
            try
            {
                ValidateSortingLayerConfiguration();
                LoadAndValidateAssets();
                bool sceneExists = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null;
                if (!forceRebuild && sceneExists)
                {
                    LoadAndValidateReflectionMaterial();
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "水面反射の比較Sceneを開けません",
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
                if (!forceRebuild
                    && TerrainTileArtInspectionMapBuilder.TryOpenExistingInspectionScene(
                        ScenePath,
                        "Water Reflection Comparison"))
                {
                    return;
                }

                Execute();
            }
            catch (Exception exception)
            {
                // 構築失敗で作業中Sceneを失わないよう、メニュー実行前のScene構成へ戻します。
                EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "水面反射の比較Sceneを構築できません",
                    exception.Message,
                    "閉じる");
            }
        }

        /// <summary>CLIや自動処理から水面反射の比較Sceneを構築する入口です。</summary>
        public static void Execute()
        {
            ValidateSortingLayerConfiguration();
            ReflectionAssets assets = LoadAndValidateAssets();
            Material reflectionMaterial = EnsureReflectionMaterial();
            BuildAndOpenScene(assets, reflectionMaterial);
        }

        /// <summary>水面位置を軸に、垂直面Spriteの中心を上下反転・縦圧縮したWorld Y座標を返します。</summary>
        /// <param name="sourceWorldY">反射対象となる垂直面Sprite中心のWorld Y座標です。</param>
        /// <param name="reflectionAxisWorldY">奥岸に置く反射軸のWorld Y座標です。</param>
        /// <returns>水面内へ投影した反射Sprite中心のWorld Y座標です。</returns>
        public static float CalculateReflectedWorldY(
            float sourceWorldY,
            float reflectionAxisWorldY)
        {
            if (float.IsNaN(sourceWorldY)
                || float.IsInfinity(sourceWorldY)
                || float.IsNaN(reflectionAxisWorldY)
                || float.IsInfinity(reflectionAxisWorldY)
                || sourceWorldY < reflectionAxisWorldY)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceWorldY));
            }

            // 水際より奥にある垂直面の距離を手前へ反転し、斜め見下ろし用に0.75倍へ縮めます。
            // 水平なGroundTopはこの計算へ渡さず、反射対象そのものから除外します。
            float distanceFromAxis = sourceWorldY - reflectionAxisWorldY;
            return reflectionAxisWorldY - (distanceFromAxis * VerticalCompression);
        }

        /// <summary>反射Proxyと波線を描くWaterSurface Sorting LayerがDefaultより後にあることを確認します。</summary>
        private static void ValidateSortingLayerConfiguration()
        {
            SortingLayer[] sortingLayers = SortingLayer.layers;
            int defaultIndex = -1;
            int waterSurfaceIndex = -1;
            for (int index = 0; index < sortingLayers.Length; index++)
            {
                if (sortingLayers[index].name == "Default")
                {
                    defaultIndex = index;
                }

                if (sortingLayers[index].name == WaterSurfaceSortingLayerName)
                {
                    waterSurfaceIndex = index;
                }
            }

            if (defaultIndex < 0 || waterSurfaceIndex <= defaultIndex)
            {
                throw new InvalidOperationException(
                    $"Sorting Layer '{WaterSurfaceSortingLayerName}' をDefaultより後へ配置してください。");
            }
        }

        /// <summary>GrassLoamの上面・断面、淡水、波線MaterialをAssetDatabaseから読み込みます。</summary>
        /// <returns>比較Sceneへ配置できる検証済み素材一式です。</returns>
        private static ReflectionAssets LoadAndValidateAssets()
        {
            TerrainTileArtGenerationSettings settings =
                AssetDatabase.LoadAssetAtPath<TerrainTileArtGenerationSettings>(
                    TerrainTileArtInspectionMapBuilder.SettingsAssetPath);
            if (settings == null)
            {
                throw new InvalidOperationException(
                    $"地形タイル生成設定が見つかりません: {TerrainTileArtInspectionMapBuilder.SettingsAssetPath}");
            }

            TerrainMaterialArtSource grassLoam = null;
            for (int index = 0; index < settings.Materials.Length; index++)
            {
                TerrainMaterialArtSource candidate = settings.Materials[index];
                if (candidate != null
                    && candidate.MaterialId == TerrainTileArtInspectionMapBuilder.InspectedMaterialId)
                {
                    grassLoam = candidate;
                    break;
                }
            }

            if (grassLoam == null
                || grassLoam.GroundTopSprites.Length == 0
                || grassLoam.FaceSprites.Length == 0)
            {
                throw new InvalidOperationException(
                    "GrassLoamのGroundTopまたはFaceを読み込めません。");
            }

            Sprite freshWater = AssetDatabase.LoadAssetAtPath<Sprite>(
                TerrainTileArtInspectionMapBuilder.FreshWaterPlaceholderPath);
            Material waveMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                TerrainTileArtInspectionMapBuilder.GeneratedPreviewAssetFolder
                + "/FreshWaterEdgeWaveHorizontal.mat");
            if (freshWater == null || waveMaterial == null)
            {
                throw new InvalidOperationException(
                    "淡水の仮Spriteまたは水際波線Materialがありません。先に地形タイル固定検品Sceneを再生成してください。");
            }

            return new ReflectionAssets(
                grassLoam.GroundTopSprites,
                grassLoam.FaceSprites,
                freshWater,
                waveMaterial,
                grassLoam.DarkenWallSurface ? grassLoam.WallDarkenFactor : 1f);
        }

        /// <summary>反射ShaderをImportし、比較Sceneが永続参照するMaterialを生成または更新します。</summary>
        /// <returns>検証値を反映した保存済みMaterialです。</returns>
        private static Material EnsureReflectionMaterial()
        {
            AssetDatabase.ImportAsset(ReflectionShaderPath, ImportAssetOptions.ForceUpdate);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ReflectionShaderPath);
            if (shader == null || ShaderUtil.ShaderHasError(shader))
            {
                throw new InvalidOperationException(
                    $"水面反射Shaderを正常に読み込めません: {ReflectionShaderPath}");
            }

            EnsureAssetFolder(Path.GetDirectoryName(ReflectionMaterialPath));
            Material material = AssetDatabase.LoadAssetAtPath<Material>(ReflectionMaterialPath);
            bool changed = false;
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = Path.GetFileNameWithoutExtension(ReflectionMaterialPath)
                };
                AssetDatabase.CreateAsset(material, ReflectionMaterialPath);
                changed = true;
            }
            else if (material.shader != shader)
            {
                // Shaderが変わった場合だけ参照を更新します。同じShaderのMaterialを丸ごと初期化すると、
                // 値が同一でもUnityが再シリアライズしてGit差分を作るため、通常実行では行いません。
                material.shader = shader;
                changed = true;
            }

            // 初期値は反射の存在を確認できつつ、水中床と青Overlayを隠しすぎない弱い反射として設定します。
            changed |= TerrainTileArtInspectionMapBuilder.SetMaterialColorIfDifferent(
                material,
                "_ReflectionTint",
                new Color32(143, 209, 232, 255));
            changed |= TerrainTileArtInspectionMapBuilder.SetMaterialFloatIfDifferent(
                material,
                "_MaximumAlpha",
                MaximumReflectionAlpha);
            changed |= TerrainTileArtInspectionMapBuilder.SetMaterialFloatIfDifferent(
                material,
                "_ReflectionAxisWorldY",
                ReflectionAxisWorldY);
            changed |= TerrainTileArtInspectionMapBuilder.SetMaterialFloatIfDifferent(
                material,
                "_MaximumDepthWorldUnits",
                MaximumReflectionDepthPixels / (float)AssetsPixelsPerUnit);
            changed |= TerrainTileArtInspectionMapBuilder.SetMaterialFloatIfDifferent(
                material,
                "_PixelsPerUnit",
                AssetsPixelsPerUnit);
            changed |= TerrainTileArtInspectionMapBuilder.SetMaterialFloatIfDifferent(
                material,
                "_WavePixelsPerSecond",
                WavePixelsPerSecond);
            if (changed)
            {
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
            }

            return material;
        }

        /// <summary>保存済みの反射MaterialとShader参照を読み取り専用で検証します。</summary>
        /// <returns>比較Sceneが参照できる既存Materialです。</returns>
        private static Material LoadAndValidateReflectionMaterial()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ReflectionShaderPath);
            if (shader == null || ShaderUtil.ShaderHasError(shader))
            {
                throw new InvalidOperationException(
                    $"水面反射Shaderを正常に読み込めません: {ReflectionShaderPath}");
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(ReflectionMaterialPath);
            if (material == null || material.shader != shader)
            {
                throw new InvalidOperationException(
                    "水面反射Materialが未生成または現在のShaderと一致しません。"
                    + "Generationメニューから比較Sceneを再構築してください。");
            }

            return material;
        }

        /// <summary>空SceneへCameraと反射なし／ありの2区画を作り、比較Sceneとして保存します。</summary>
        /// <param name="assets">配置するGrassLoam、淡水、波線素材です。</param>
        /// <param name="reflectionMaterial">右区画だけへ設定する反射Materialです。</param>
        private static void BuildAndOpenScene(
            ReflectionAssets assets,
            Material reflectionMaterial)
        {
            // 現在Sceneの選択を先に外し、破棄済みObjectをInspectorが参照して内部例外を出すことを防ぎます。
            Selection.activeObject = null;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "TerrainWaterReflectionComparison";
            CreateCamera();

            GameObject root = new GameObject("Water Reflection Comparison");
            CreateComparisonPanel(
                root,
                "A_Reflection_Off",
                WithoutReflectionCenterX,
                false,
                assets,
                reflectionMaterial);
            CreateComparisonPanel(
                root,
                "B_Reflection_On",
                WithReflectionCenterX,
                true,
                assets,
                reflectionMaterial);

            EnsureAssetFolder("Assets/Scenes/Development");
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"比較Sceneを保存できませんでした: {ScenePath}");
            }

            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        /// <summary>反射比較をPixel崩れなく表示するOrthographic Cameraを作ります。</summary>
        private static void CreateCamera()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0.5f, -10f);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(22, 25, 30, 255);

            PixelPerfectCamera pixelPerfect = cameraObject.AddComponent<PixelPerfectCamera>();
            pixelPerfect.assetsPPU = AssetsPixelsPerUnit;
            pixelPerfect.refResolutionX = ReferenceResolutionX;
            pixelPerfect.refResolutionY = ReferenceResolutionY;
            pixelPerfect.cropFrame = PixelPerfectCamera.CropFrame.Windowbox;
        }

        /// <summary>GrassLoamの岸、2段Wall、淡水と波線を配置し、右区画だけへ垂直面反射Proxyを加えます。</summary>
        /// <param name="root">比較Scene全体の親です。</param>
        /// <param name="panelName">Hierarchy上で条件を示す区画名です。</param>
        /// <param name="centerX">区画中央のWorld X座標です。</param>
        /// <param name="usesReflection">Wall前面の反射Proxyを追加する場合はtrueです。</param>
        /// <param name="assets">配置するSpriteとMaterialです。</param>
        /// <param name="reflectionMaterial">各反射Proxyへ使うMaterialです。</param>
        private static void CreateComparisonPanel(
            GameObject root,
            string panelName,
            float centerX,
            bool usesReflection,
            ReflectionAssets assets,
            Material reflectionMaterial)
        {
            GameObject panel = CreateChild(root, panelName);

            // 水中にもFaceを敷き、反射の有無以外の条件を左右で同じにします。
            // 淡水Spriteは半透明にし、下地と反射を同時に読み取れる仮水面として使います。
            for (int depth = 0; depth < WaterDepthTiles; depth++)
            {
                for (int x = 0; x < WaterWidthTiles; x++)
                {
                    float worldX = centerX + x - ((WaterWidthTiles - 1) * 0.5f);
                    float worldY = -depth;
                    int variant = StableVariantIndex(x, depth, assets.FaceSprites.Length);
                    CreateSpriteObject(
                        panel,
                        $"UnderwaterFace_{x:D2}_{depth:D2}",
                        assets.FaceSprites[variant],
                        new Vector3(worldX, worldY, 0f),
                        Color.white,
                        10,
                        "Default");
                    CreateSpriteObject(
                        panel,
                        $"FreshWater_{x:D2}_{depth:D2}",
                        assets.FreshWaterSprite,
                        new Vector3(worldX, worldY, -0.1f),
                        new Color(1f, 1f, 1f, 0.45f),
                        20,
                        "Default");
                }
            }

            // 奥岸のGroundとWallを同じ構図へ置きます。水平なGroundTopとWallTopは見えていても反射対象には含めません。
            for (int x = -3; x <= 3; x++)
            {
                int groundVariant = StableVariantIndex(x, 20, assets.GroundTopSprites.Length);
                CreateSpriteObject(
                    panel,
                    $"RearGround_{x + 3:D2}",
                    assets.GroundTopSprites[groundVariant],
                    new Vector3(centerX + x, 1f, 0f),
                    Color.white,
                    30,
                    "Default");
            }

            Color wallTint = new Color(
                assets.WallDarkenFactor,
                assets.WallDarkenFactor,
                assets.WallDarkenFactor,
                1f);
            for (int x = -1; x <= 1; x++)
            {
                int topVariant = StableVariantIndex(x, 30, assets.GroundTopSprites.Length);
                int faceVariant = StableVariantIndex(x, 31, assets.FaceSprites.Length);
                CreateSpriteObject(
                    panel,
                    $"WallTop_{x + 1:D2}",
                    assets.GroundTopSprites[topVariant],
                    new Vector3(centerX + x, 3f, -0.2f),
                    wallTint,
                    50,
                    "Default");

                for (int faceStage = 0; faceStage < 2; faceStage++)
                {
                    Vector3 facePosition = new Vector3(
                        centerX + x,
                        2f - faceStage,
                        -0.2f);
                    CreateSpriteObject(
                        panel,
                        $"WallFace_{x + 1:D2}_{faceStage + 1:D2}",
                        assets.FaceSprites[faceVariant],
                        facePosition,
                        Color.white,
                        45,
                        "Default");

                    if (usesReflection)
                    {
                        // 斜め見下ろしで水面へ映るのはWallの垂直な前面です。
                        // 同じBlockの水平なWallTopはProxyを作らず、誤った上面反射を防ぎます。
                        CreateReflectionProxy(
                            panel,
                            $"WallFaceReflection_{x + 1:D2}_{faceStage + 1:D2}",
                            assets.FaceSprites[faceVariant],
                            facePosition,
                            reflectionMaterial);
                    }
                }
            }

            CreateRearWaveLine(panel, centerX, assets.FreshWaterSprite, assets.WaveMaterial);
        }

        /// <summary>垂直面Spriteだけを水際で上下反転し、縦圧縮した反射Proxyとして水面へ置きます。</summary>
        /// <param name="parent">反射あり区画の親です。</param>
        /// <param name="name">Hierarchy上で反射元を識別できる名前です。</param>
        /// <param name="sourceSprite">反射元と同じ垂直面Spriteです。</param>
        /// <param name="sourcePosition">反射元Sprite中心のWorld座標です。</param>
        /// <param name="material">青Tint、深度Clip、整数Pixel変位を適用する反射Materialです。</param>
        private static void CreateReflectionProxy(
            GameObject parent,
            string name,
            Sprite sourceSprite,
            Vector3 sourcePosition,
            Material material)
        {
            GameObject reflection = new GameObject(name);
            reflection.transform.SetParent(parent.transform, false);
            reflection.transform.position = new Vector3(
                sourcePosition.x,
                CalculateReflectedWorldY(sourcePosition.y, ReflectionAxisWorldY),
                -0.3f);
            reflection.transform.localScale = new Vector3(1f, VerticalCompression, 1f);

            SpriteRenderer renderer = reflection.AddComponent<SpriteRenderer>();
            renderer.sprite = sourceSprite;
            renderer.color = Color.white;
            renderer.sharedMaterial = material;
            renderer.flipY = true;
            renderer.sortingLayerName = WaterSurfaceSortingLayerName;
            renderer.sortingOrder = 60;
        }

        /// <summary>反射より手前へ、既存の2色1px波線を奥岸にだけ配置します。</summary>
        /// <param name="parent">比較区画の親です。</param>
        /// <param name="centerX">区画中央のWorld X座標です。</param>
        /// <param name="geometrySprite">1px線の矩形Geometryとして使うSpriteです。</param>
        /// <param name="waveMaterial">2色を整数Pixelで流す既存Materialです。</param>
        private static void CreateRearWaveLine(
            GameObject parent,
            float centerX,
            Sprite geometrySprite,
            Material waveMaterial)
        {
            float halfPixel = 0.5f / AssetsPixelsPerUnit;
            GameObject wave = new GameObject("RearWaterEdgeWave");
            wave.transform.SetParent(parent.transform, false);
            wave.transform.position = new Vector3(
                centerX,
                ReflectionAxisWorldY - halfPixel,
                -0.4f);
            wave.transform.localScale = new Vector3(
                WaterWidthTiles,
                1f / AssetsPixelsPerUnit,
                1f);

            SpriteRenderer renderer = wave.AddComponent<SpriteRenderer>();
            renderer.sprite = geometrySprite;
            renderer.color = Color.white;
            renderer.sharedMaterial = waveMaterial;
            renderer.sortingLayerName = WaterSurfaceSortingLayerName;
            renderer.sortingOrder = 80;
        }

        /// <summary>親GameObjectの直下へ、用途名を持つ空の子Groupを作ります。</summary>
        /// <param name="parent">親GameObjectです。</param>
        /// <param name="name">Hierarchyへ表示する名前です。</param>
        /// <returns>作成した子GameObjectです。</returns>
        private static GameObject CreateChild(GameObject parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        /// <summary>指定Spriteを、描画順とSorting Layerを明示したSpriteRendererとして配置します。</summary>
        /// <param name="parent">配置先の親Groupです。</param>
        /// <param name="name">Hierarchyへ表示する名前です。</param>
        /// <param name="sprite">描画するSpriteです。</param>
        /// <param name="position">Sprite中心のWorld座標です。</param>
        /// <param name="color">SpriteRendererへ掛ける乗算色です。</param>
        /// <param name="sortingOrder">同じSorting Layer内の描画順です。</param>
        /// <param name="sortingLayerName">描画先のSorting Layer名です。</param>
        private static void CreateSpriteObject(
            GameObject parent,
            string name,
            Sprite sprite,
            Vector3 position,
            Color color,
            int sortingOrder,
            string sortingLayerName)
        {
            GameObject gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent.transform, false);
            gameObject.transform.position = position;
            SpriteRenderer renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;
        }

        /// <summary>整数座標から決定論的な差分Indexを選び、Scene再生成時の見た目を固定します。</summary>
        /// <param name="x">水平セル座標です。</param>
        /// <param name="y">垂直セル座標です。</param>
        /// <param name="variantCount">利用可能なSprite差分数です。</param>
        /// <returns>0以上variantCount未満の差分Indexです。</returns>
        private static int StableVariantIndex(int x, int y, int variantCount)
        {
            if (variantCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(variantCount));
            }

            // 二方向へ異なる素数を掛け、単純な縦縞や横縞を避けつつ同じ座標では常に同じ結果にします。
            uint hash = unchecked((uint)(x * 73856093) ^ (uint)(y * 19349663));
            return (int)(hash % (uint)variantCount);
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

        /// <summary>比較Sceneの構築中だけ保持するSpriteとMaterial一式です。</summary>
        private sealed class ReflectionAssets
        {
            /// <summary>岸とWall上面へ使うGrassLoam差分です。</summary>
            public Sprite[] GroundTopSprites { get; }

            /// <summary>水中下地とWall前面へ使うGrassLoam差分です。</summary>
            public Sprite[] FaceSprites { get; }

            /// <summary>仮の半透明水面と矩形Geometryへ使う淡水Spriteです。</summary>
            public Sprite FreshWaterSprite { get; }

            /// <summary>反射の上へ重ねる既存の2色1px波線Materialです。</summary>
            public Material WaveMaterial { get; }

            /// <summary>Wall上面へ掛けるRGB乗算係数です。</summary>
            public float WallDarkenFactor { get; }

            /// <summary>読み込み済み素材を比較Scene構築へまとめて渡します。</summary>
            /// <param name="groundTopSprites">GrassLoamの上面差分です。</param>
            /// <param name="faceSprites">GrassLoamの断面差分です。</param>
            /// <param name="freshWaterSprite">淡水の仮Spriteです。</param>
            /// <param name="waveMaterial">既存の水際波線Materialです。</param>
            /// <param name="wallDarkenFactor">Wall上面の暗化係数です。</param>
            public ReflectionAssets(
                Sprite[] groundTopSprites,
                Sprite[] faceSprites,
                Sprite freshWaterSprite,
                Material waveMaterial,
                float wallDarkenFactor)
            {
                GroundTopSprites = groundTopSprites;
                FaceSprites = faceSprites;
                FreshWaterSprite = freshWaterSprite;
                WaveMaterial = waveMaterial;
                WallDarkenFactor = wallDarkenFactor;
            }
        }
    }
}
