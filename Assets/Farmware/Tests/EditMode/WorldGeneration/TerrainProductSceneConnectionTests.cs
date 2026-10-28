// このファイルは、製品SampleSceneが41材質・液体カタログと採用済み断面・Wall影Tilemapへ切り替わったことを検証します。
// SceneのSerialized参照、Transform、Sorting Layer、Materialを確認し、旧境界や全面液体アニメーションの再混入を防ぎます。

using Farmware.WorldGeneration.Editor;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// SampleSceneをAdditiveで開き、製品材質・液体表示の必須参照と採用済みWall影が接続されていることを保証します。
    /// </summary>
    public sealed class TerrainProductSceneConnectionTests
    {
        /// <summary>テストが追加で開いた場合だけ閉じる製品Sceneです。</summary>
        private Scene scene;

        /// <summary>既に開いていたSceneをテスト終了時に閉じないための所有フラグです。</summary>
        private bool openedByTest;

        /// <summary>各テスト前にSampleSceneを既存作業を閉じないAdditive方式で読み込みます。</summary>
        [SetUp]
        public void SetUp()
        {
            scene = SceneManager.GetSceneByPath(TerrainProductSceneConnector.ProductScenePath);
            openedByTest = !scene.IsValid() || !scene.isLoaded;
            if (openedByTest)
            {
                scene = EditorSceneManager.OpenScene(
                    TerrainProductSceneConnector.ProductScenePath,
                    OpenSceneMode.Additive);
            }
        }

        /// <summary>テストが追加で開いた製品Sceneだけを閉じ、ユーザーが開いていたScene状態を維持します。</summary>
        [TearDown]
        public void TearDown()
        {
            if (openedByTest && scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        /// <summary>
        /// Presenterが材質・液体カタログ、断面、深部黒、固定影、折れ目影、水面・マグマ模様をすべて参照することを確認します。
        /// </summary>
        [Test]
        public void SampleScene_PresenterReferencesCompleteProductTerrainLayers()
        {
            Transform grid = FindRoot(TerrainProductSceneConnector.WorldGridName).transform;
            Transform ground = grid.Find(TerrainProductSceneConnector.GroundTilemapName);
            Assert.That(ground, Is.Not.Null);
            ChunkTilemapPresenter presenter = ground.GetComponent<ChunkTilemapPresenter>();
            Assert.That(presenter, Is.Not.Null);

            SerializedObject serialized = new SerializedObject(presenter);
            AssertObjectReferencePath(
                serialized,
                "materialPresentationCatalog",
                TerrainMaterialProductPublisher.CatalogPath);
            AssertObjectReferencePath(
                serialized,
                "liquidPresentationCatalog",
                TerrainLiquidProductPublisher.CatalogPath);
            AssertTilemapReference(
                serialized,
                "groundFaceTilemap",
                TerrainProductSceneConnector.GroundFaceTilemapName);
            AssertTilemapReference(
                serialized,
                "wallFaceTilemap",
                TerrainProductSceneConnector.WallFaceTilemapName);
            AssertTilemapReference(
                serialized,
                "dryCliffDeepBlackTilemap",
                TerrainProductSceneConnector.DryCliffDeepBlackTilemapName);
            AssertTilemapReference(
                serialized,
                "magmaSurfacePatternTilemap",
                TerrainProductSceneConnector.MagmaSurfacePatternTilemapName);
            AssertTilemapReference(
                serialized,
                "wallContactShadowLeftTilemap",
                TerrainProductSceneConnector.WallContactShadowLeftTilemapName);
            AssertTilemapReference(
                serialized,
                "wallContactShadowRightTilemap",
                TerrainProductSceneConnector.WallContactShadowRightTilemapName);
            AssertTilemapReference(
                serialized,
                "wallFoldShadowTopTilemap",
                TerrainProductSceneConnector.WallFoldShadowTopTilemapName);
            AssertTilemapReference(
                serialized,
                "wallFoldShadowBottomTilemap",
                TerrainProductSceneConnector.WallFoldShadowBottomTilemapName);
            AssertTilemapReference(
                serialized,
                "liquidDepthWithFloorTilemap",
                TerrainProductSceneConnector.LiquidDepthWithFloorTilemapName);
            AssertTilemapReference(
                serialized,
                "liquidDepthWithoutFloorTilemap",
                TerrainProductSceneConnector.LiquidDepthWithoutFloorTilemapName);
            AssertTilemapReference(
                serialized,
                "underwaterFloorPreviewTilemap",
                TerrainProductSceneConnector.UnderwaterFloorPreviewTilemapName);
            AssertTilemapReference(serialized, "submergedGroundFaceTilemap",
                TerrainProductSceneConnector.SubmergedGroundFaceTilemapName);
            AssertTilemapReference(serialized, "waterSurfacePatternTilemap",
                TerrainProductSceneConnector.WaterSurfacePatternTilemapName);
            Assert.That(serialized.FindProperty("waterReflectionAtlas").objectReferenceValue,
                Is.Not.Null, "水面反射は製品Atlasを明示参照し、元画像ごとの描画バッチを作りません。");
            AssertTilemapReference(
                serialized,
                "waterReflectionTilemap",
                TerrainProductSceneConnector.WaterReflectionTilemapName);
            AssertTilemapReference(
                serialized,
                "liquidEdgeWaveTilemap",
                TerrainProductSceneConnector.LiquidEdgeWaveTilemapName);
        }

        /// <summary>
        /// 製品液体が論理セル位置を維持し、水面・マグマ模様、反射、水際を採用順で重ねることを確認します。
        /// </summary>
        [Test]
        public void SampleScene_LiquidLayersUseProductMaterialsAndAdoptedOrder()
        {
            Transform grid = FindRoot(TerrainProductSceneConnector.WorldGridName).transform;
            AssertLayer(
                grid,
                TerrainProductSceneConnector.LiquidBaseTilemapName,
                Vector3.zero,
                TerrainProductSceneConnector.WaterSortingLayerName,
                TerrainProductSceneConnector.LiquidSurfaceMaterialPath);
            AssertLayer(
                grid,
                TerrainProductSceneConnector.LiquidDepthWithFloorTilemapName,
                Vector3.zero,
                TerrainProductSceneConnector.WaterSortingLayerName,
                TerrainProductSceneConnector.LiquidDepthWithFloorMaterialPath);
            AssertLayer(
                grid,
                TerrainProductSceneConnector.LiquidDepthWithoutFloorTilemapName,
                Vector3.zero,
                TerrainProductSceneConnector.WaterSortingLayerName,
                TerrainProductSceneConnector.LiquidDepthWithoutFloorMaterialPath);
            AssertLayer(
                grid,
                TerrainProductSceneConnector.UnderwaterFloorPreviewTilemapName,
                Vector3.zero,
                TerrainProductSceneConnector.WaterSortingLayerName,
                TerrainProductSceneConnector.SpriteLitMaterialPath);
            AssertLayer(grid, TerrainProductSceneConnector.SubmergedGroundFaceTilemapName,
                Vector3.zero, TerrainProductSceneConnector.WaterSortingLayerName,
                TerrainProductSceneConnector.SubmergedGroundFaceMaterialPath);
            AssertLayer(grid, TerrainProductSceneConnector.WaterSurfacePatternTilemapName,
                Vector3.zero, TerrainProductSceneConnector.WaterSortingLayerName,
                TerrainProductSceneConnector.WaterSurfacePatternMaterialPath);
            AssertLayer(grid, TerrainProductSceneConnector.MagmaSurfacePatternTilemapName,
                Vector3.zero, TerrainProductSceneConnector.WaterSortingLayerName,
                TerrainProductSceneConnector.MagmaSurfacePatternMaterialPath);
            AssertLayer(
                grid,
                TerrainProductSceneConnector.WaterReflectionTilemapName,
                Vector3.zero,
                TerrainProductSceneConnector.WaterSortingLayerName,
                TerrainProductSceneConnector.WaterReflectionMaterialPath);
            AssertLayer(
                grid,
                TerrainProductSceneConnector.LiquidEdgeWaveTilemapName,
                Vector3.zero,
                TerrainProductSceneConnector.WaterSortingLayerName,
                TerrainProductSceneConnector.ShorelineMaterialPath);

            TilemapRenderer surface = GetRenderer(
                grid,
                TerrainProductSceneConnector.LiquidBaseTilemapName);
            TilemapRenderer depth = GetRenderer(
                grid,
                TerrainProductSceneConnector.LiquidDepthWithFloorTilemapName);
            TilemapRenderer reflection = GetRenderer(
                grid,
                TerrainProductSceneConnector.WaterReflectionTilemapName);
            TilemapRenderer edgeWave = GetRenderer(
                grid,
                TerrainProductSceneConnector.LiquidEdgeWaveTilemapName);
            Assert.That(surface.sortingOrder, Is.EqualTo(depth.sortingOrder));
            TilemapRenderer submerged = GetRenderer(grid, TerrainProductSceneConnector.SubmergedGroundFaceTilemapName);
            Assert.That(depth.sortingOrder, Is.LessThan(submerged.sortingOrder));
            Assert.That(submerged.sortingOrder, Is.LessThan(reflection.sortingOrder));
            TilemapRenderer pattern = GetRenderer(grid, TerrainProductSceneConnector.WaterSurfacePatternTilemapName);
            Assert.That(submerged.sortingOrder, Is.LessThan(pattern.sortingOrder));
            Assert.That(pattern.sortingOrder, Is.LessThan(reflection.sortingOrder));
            Assert.That(grid.Find(TerrainProductSceneConnector.UnderwaterFloorPreviewTilemapName).GetComponent<Tilemap>().color.a,
                Is.EqualTo(0.5f));
            Assert.That(depth.sortingOrder, Is.LessThan(reflection.sortingOrder));
            Assert.That(reflection.sortingOrder, Is.LessThan(edgeWave.sortingOrder));

            TilemapRenderer magmaPattern = GetRenderer(
                grid,
                TerrainProductSceneConnector.MagmaSurfacePatternTilemapName);
            Assert.That(
                magmaPattern.enabled,
                Is.True,
                "製品経路ではマグマ模様専用Rendererを有効にします。");
            Assert.That(magmaPattern.sortingOrder, Is.LessThan(reflection.sortingOrder));
        }

        /// <summary>
        /// 論理Wall根元から上面・Face・資源・影を2タイル持ち上げ、
        /// 固定接地影がそこから左右各1px・手前3pxになることを確認します。
        /// </summary>
        [Test]
        public void SampleScene_WallShadowLayersUseAdoptedTransformsAndMaterials()
        {
            Transform grid = FindRoot(TerrainProductSceneConnector.WorldGridName).transform;
            float pixel = 1f / 16f;
            float wallOffset = TerrainMaterialVerticalProjectionUtility.WallTopOffsetInTiles;
            AssertLayer(
                grid,
                TerrainProductSceneConnector.WallContactShadowLeftTilemapName,
                new Vector3(-pixel, wallOffset - (3f * pixel), 0f),
                "Default",
                TerrainProductSceneConnector.ContactShadowMaterialPath);
            AssertLayer(
                grid,
                TerrainProductSceneConnector.WallContactShadowRightTilemapName,
                new Vector3(pixel, wallOffset - (3f * pixel), 0f),
                "Default",
                TerrainProductSceneConnector.ContactShadowMaterialPath);
            AssertLayer(
                grid,
                TerrainProductSceneConnector.WallFoldShadowTopTilemapName,
                new Vector3(0f, wallOffset, 0f),
                "TerrainWall",
                TerrainProductSceneConnector.FoldShadowTopMaterialPath);
            AssertLayer(
                grid,
                TerrainProductSceneConnector.WallFoldShadowBottomTilemapName,
                new Vector3(0f, wallOffset, 0f),
                "TerrainWall",
                TerrainProductSceneConnector.FoldShadowBottomMaterialPath);

            AssertLocalY(grid, "Rock Wall Top Tilemap", wallOffset);
            AssertLocalY(
                grid,
                TerrainProductSceneConnector.WallFaceTilemapName,
                wallOffset);
            AssertLocalY(grid, "Rock Wall Resource Tilemap", wallOffset);

            Transform groundFace = grid.Find(TerrainProductSceneConnector.GroundFaceTilemapName);
            Assert.That(
                AssetDatabase.GetAssetPath(groundFace.GetComponent<TilemapRenderer>().sharedMaterial),
                Is.EqualTo(TerrainProductSceneConnector.DryCliffMaterialPath));
        }

        /// <summary>
        /// Wall専用Sorting LayerがGlobal Lightの照明対象であり、遮蔽対象資源だけがWallの奥へ回ることを確認します。
        /// </summary>
        [Test]
        public void SampleScene_WallLayerIsLitAndResourcesKeepExpectedDepth()
        {
            Transform grid = FindRoot(TerrainProductSceneConnector.WorldGridName).transform;
            TilemapRenderer resourceBehindWall = GetRenderer(
                grid,
                "Resource Behind Rock Wall Tilemap");
            TilemapRenderer wallTop = GetRenderer(grid, "Rock Wall Top Tilemap");
            TilemapRenderer resource = GetRenderer(grid, "Resource Tilemap");
            TilemapRenderer wallResource = GetRenderer(grid, "Rock Wall Resource Tilemap");

            Assert.That(
                resourceBehindWall.sortingLayerName,
                Is.EqualTo(TerrainProductSceneConnector.GroundSortingLayerName));
            Assert.That(
                wallTop.sortingLayerName,
                Is.EqualTo(TerrainProductSceneConnector.WallSortingLayerName));
            Assert.That(
                resource.sortingLayerName,
                Is.EqualTo(TerrainProductSceneConnector.WallSortingLayerName));
            Assert.That(
                wallResource.sortingLayerName,
                Is.EqualTo(TerrainProductSceneConnector.WallSortingLayerName));
            Assert.That(resource.sortingOrder, Is.GreaterThan(wallTop.sortingOrder));
            Assert.That(wallResource.sortingOrder, Is.GreaterThan(resource.sortingOrder));

            int wallSortingLayerId = SortingLayer.NameToID(
                TerrainProductSceneConnector.WallSortingLayerName);
            Assert.That(
                SceneHasGlobalLightTargeting(wallSortingLayerId),
                Is.True,
                "Lit MaterialのTerrainWallを対象にするGlobal Light 2Dが必要です。");
            Assert.That(SceneHasGlobalLightTargeting(SortingLayer.NameToID(TerrainProductSceneConnector.WaterSortingLayerName)),
                Is.True, "基底色の前へ移したLit水中床もGlobal Lightの対象に含めます。");
        }

        /// <summary>製品CameraがHDRとBloom 0.4を受け取り、岸照明がGround Presenterへ接続されていることを保証します。</summary>
        [Test]
        public void SampleScene_UsesAdoptedMagmaGlowAndShoreLighting()
        {
            Camera camera = FindRoot("Main Camera").GetComponent<Camera>();
            Assert.That(camera.allowHDR, Is.True);
            var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            Assert.That(cameraData.renderPostProcessing, Is.True);
            Volume volume = FindRoot(TerrainProductSceneConnector.MagmaBloomVolumeName).GetComponent<Volume>();
            Assert.That(volume.isGlobal && volume.enabled, Is.True);
            Assert.That(cameraData.volumeLayerMask.value & (1 << volume.gameObject.layer), Is.Not.Zero);
            Assert.That(AssetDatabase.GetAssetPath(volume.sharedProfile), Is.EqualTo(TerrainProductSceneConnector.MagmaBloomProfilePath));
            Assert.That(volume.sharedProfile.TryGet(out Bloom bloom), Is.True);
            Assert.That(bloom.intensity.overrideState && bloom.threshold.overrideState, Is.True);
            Assert.That(bloom.intensity.value, Is.EqualTo(0.4f));
            Assert.That(bloom.threshold.value, Is.EqualTo(1.15f));
            Assert.That(bloom.scatter.value, Is.EqualTo(0.25f));
            Assert.That(bloom.maxIterations.value, Is.EqualTo(3));
            GameObject ground = FindRoot(TerrainProductSceneConnector.WorldGridName).transform.Find(
                TerrainProductSceneConnector.GroundTilemapName).gameObject;
            var lighting = ground.GetComponent<TerrainMagmaShoreLighting>();
            Assert.That(lighting, Is.Not.Null);
            var serialized = new SerializedObject(ground.GetComponent<ChunkTilemapPresenter>());
            Assert.That(serialized.FindProperty("magmaShoreLighting").objectReferenceValue, Is.EqualTo(lighting));
            Assert.That(new SerializedObject(lighting).FindProperty("cameraController").objectReferenceValue,
                Is.EqualTo(camera.GetComponent<CameraPanController>()));
            Material material = AssetDatabase.LoadAssetAtPath<Material>(TerrainProductSceneConnector.MagmaSurfacePatternMaterialPath);
            Assert.That(material.GetColor("_CoreColor").maxColorComponent, Is.GreaterThan(1f));
        }

        /// <summary>再接続で製品Volumeを重複生成せず、同じProfileを使うことを保証します。</summary>
        [Test]
        public void ConfigureMagmaBloom_ReusesExistingVolumeAndProfile()
        {
            // 製品Sceneを変更しないよう、専用Preview Sceneへ最小の接続対象だけを準備します。
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var cameraObject = new GameObject("Magma Connection Test Camera", typeof(Camera), typeof(UniversalAdditionalCameraData), typeof(CameraPanController));
                cameraObject.tag = "MainCamera";
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                var grid = new GameObject(TerrainProductSceneConnector.WorldGridName);
                SceneManager.MoveGameObjectToScene(grid, preview);
                var ground = new GameObject(TerrainProductSceneConnector.GroundTilemapName, typeof(ChunkTilemapPresenter));
                SceneManager.MoveGameObjectToScene(ground, preview);
                ground.transform.SetParent(grid.transform);
                TerrainProductSceneConnector.ConfigureMagmaBloom(preview);
                TerrainProductSceneConnector.ConfigureMagmaBloom(preview);
                int count = 0;
                foreach (GameObject root in preview.GetRootGameObjects())
                    if (root.name == TerrainProductSceneConnector.MagmaBloomVolumeName) count++;
                Assert.That(count, Is.EqualTo(1));
                Assert.That(ground.GetComponents<TerrainMagmaShoreLighting>().Length, Is.EqualTo(1));
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        /// <summary>Scene直下から指定名のGameObjectを探します。</summary>
        /// <param name="name">検索するRoot名です。</param>
        /// <returns>一意に見つかったRootです。</returns>
        private GameObject FindRoot(string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                if (roots[index].name == name)
                {
                    return roots[index];
                }
            }

            Assert.Fail($"製品Sceneに{name}がありません。");
            return null;
        }

        /// <summary>
        /// 製品Scene内のGlobal Light 2Dが指定Sorting Layer IDを照明対象に含むか確認します。
        /// </summary>
        /// <param name="sortingLayerId">照明対象として必要なSorting Layer IDです。</param>
        /// <returns>該当するGlobal Light 2Dが1件以上あればtrueです。</returns>
        private bool SceneHasGlobalLightTargeting(int sortingLayerId)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                Component[] components = roots[rootIndex].GetComponentsInChildren<Component>(
                    includeInactive: true);
                for (int componentIndex = 0;
                     componentIndex < components.Length;
                     componentIndex++)
                {
                    Component component = components[componentIndex];
                    if (component == null
                        || component.GetType().FullName
                            != "UnityEngine.Rendering.Universal.Light2D")
                    {
                        continue;
                    }

                    // Sorting Layer一覧はUnityが保存する実フィールドを検査し、Scene接続の欠落を検出します。
                    SerializedObject serializedLight = new SerializedObject(component);
                    SerializedProperty lightType = serializedLight.FindProperty("m_LightType");
                    SerializedProperty targetLayers = serializedLight.FindProperty(
                        "m_ApplyToSortingLayers");
                    if (lightType == null
                        || lightType.intValue != 4
                        || targetLayers == null)
                    {
                        continue;
                    }

                    for (int layerIndex = 0;
                         layerIndex < targetLayers.arraySize;
                         layerIndex++)
                    {
                        if (targetLayers.GetArrayElementAtIndex(layerIndex).intValue
                            == sortingLayerId)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>Grid直下の必須TilemapRendererを取得します。</summary>
        /// <param name="grid">製品World Gridです。</param>
        /// <param name="name">必須Tilemap GameObject名です。</param>
        /// <returns>見つかったTilemapRendererです。</returns>
        private static TilemapRenderer GetRenderer(Transform grid, string name)
        {
            Transform child = grid.Find(name);
            Assert.That(child, Is.Not.Null, name);
            TilemapRenderer renderer = child.GetComponent<TilemapRenderer>();
            Assert.That(renderer, Is.Not.Null, name);
            return renderer;
        }

        /// <summary>Serialized Object参照が期待Assetパスと一致することを確認します。</summary>
        /// <param name="serialized">PresenterのSerializedObjectです。</param>
        /// <param name="propertyName">確認するprivate field名です。</param>
        /// <param name="expectedPath">期待するAssetDatabaseパスです。</param>
        private static void AssertObjectReferencePath(
            SerializedObject serialized,
            string propertyName,
            string expectedPath)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            Assert.That(property, Is.Not.Null, propertyName);
            Assert.That(property.objectReferenceValue, Is.Not.Null, propertyName);
            Assert.That(
                AssetDatabase.GetAssetPath(property.objectReferenceValue),
                Is.EqualTo(expectedPath));
        }

        /// <summary>PresenterのTilemap参照が期待したScene GameObjectを指すことを確認します。</summary>
        /// <param name="serialized">PresenterのSerializedObjectです。</param>
        /// <param name="propertyName">確認するprivate Tilemap field名です。</param>
        /// <param name="expectedObjectName">期待するGameObject名です。</param>
        private static void AssertTilemapReference(
            SerializedObject serialized,
            string propertyName,
            string expectedObjectName)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            Assert.That(property, Is.Not.Null, propertyName);
            Tilemap tilemap = property.objectReferenceValue as Tilemap;
            Assert.That(tilemap, Is.Not.Null, propertyName);
            Assert.That(tilemap.gameObject.name, Is.EqualTo(expectedObjectName));
        }

        /// <summary>1つの影レイヤーの位置、Sorting Layer、Materialを確認します。</summary>
        /// <param name="grid">製品World Gridです。</param>
        /// <param name="name">対象Tilemap名です。</param>
        /// <param name="expectedPosition">セル単位の期待ローカル位置です。</param>
        /// <param name="expectedSortingLayer">期待する受影面Sorting Layerです。</param>
        /// <param name="expectedMaterialPath">期待する製品Materialパスです。</param>
        private static void AssertLayer(
            Transform grid,
            string name,
            Vector3 expectedPosition,
            string expectedSortingLayer,
            string expectedMaterialPath)
        {
            Transform layer = grid.Find(name);
            Assert.That(layer, Is.Not.Null, name);
            Assert.That(layer.localPosition.x, Is.EqualTo(expectedPosition.x).Within(0.0001f));
            Assert.That(layer.localPosition.y, Is.EqualTo(expectedPosition.y).Within(0.0001f));
            TilemapRenderer renderer = layer.GetComponent<TilemapRenderer>();
            Assert.That(renderer, Is.Not.Null, name);
            Assert.That(renderer.sortingLayerName, Is.EqualTo(expectedSortingLayer));
            Assert.That(
                AssetDatabase.GetAssetPath(renderer.sharedMaterial),
                Is.EqualTo(expectedMaterialPath));
        }

        /// <summary>Wall関連Tilemapの論理根元からのY表示補正を確認します。</summary>
        /// <param name="grid">製品World Gridです。</param>
        /// <param name="name">対象Tilemap名です。</param>
        /// <param name="expectedY">タイル単位の期待Yです。</param>
        private static void AssertLocalY(Transform grid, string name, float expectedY)
        {
            Transform layer = grid.Find(name);
            Assert.That(layer, Is.Not.Null, name);
            Assert.That(layer.localPosition.y, Is.EqualTo(expectedY).Within(0.0001f), name);
        }
    }
}
