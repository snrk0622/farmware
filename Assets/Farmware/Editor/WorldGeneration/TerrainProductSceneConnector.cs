// このファイルは、公開済み41材質・液体カタログと採用済み断面・液面模様・Wall影レイヤーを製品SampleSceneへ接続します。
// Sprite生成やSimulation状態は変更せず、Tilemap、Material、描画順とHDR発光用のCamera・Bloomを構成します。

using System;
using Farmware.WorldGeneration.Authoring;
using Farmware.WorldGeneration.Presentation;
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
    /// SampleSceneの既存Gridへ製品用地形・液体レイヤーを冪等に追加します。
    /// C6-D2〜D4で実装したRuntime経路を、C6-D6で完成材質と液体Assetへ接続するEditor専用処理です。
    /// </summary>
    public static class TerrainProductSceneConnector
    {
        /// <summary>Build Settingsで有効な製品Sceneの固定パスです。</summary>
        public const string ProductScenePath = "Assets/Scenes/SampleScene.unity";

        /// <summary>製品材質表示を所有するGridルート名です。</summary>
        public const string WorldGridName = "World Grid";

        /// <summary>ChunkTilemapPresenterを持つGround Tilemap名です。</summary>
        public const string GroundTilemapName = "Ground Tilemap";

        /// <summary>Ground、液体、Wall背後の影や資源を描く既定Sorting Layer名です。</summary>
        public const string GroundSortingLayerName = "Default";

        /// <summary>Wall本体とWallより手前の資源を描くSorting Layer名です。</summary>
        public const string WallSortingLayerName = "TerrainWall";

        /// <summary>水面Surface、深度、反射、水際を順に重ねるSorting Layer名です。</summary>
        public const string WaterSortingLayerName = "WaterSurface";

        /// <summary>製品Surface Tileを書き込む既存液体基礎レイヤー名です。</summary>
        public const string LiquidBaseTilemapName = "Liquid Base Tilemap";

        /// <summary>製品経路でマグマだけへ手続き型の網状模様を重ねるレイヤー名です。</summary>
        public const string MagmaSurfacePatternTilemapName = "Magma Surface Pattern Tilemap";

        /// <summary>網目を絶対座標から生成するマグマ模様Materialの保存先です。</summary>
        public const string MagmaSurfacePatternMaterialPath =
            "Assets/Farmware/Art/WorldGeneration/Materials/TerrainMagmaSurfacePattern.mat";

        /// <summary>マグマのHDR色へにじみを加える製品Bloom Profileの保存先です。</summary>
        public const string MagmaBloomProfilePath = "Assets/Settings/TerrainMagmaBloom.asset";

        /// <summary>製品SceneでBloomを提供するGlobal Volumeのルート名です。</summary>
        public const string MagmaBloomVolumeName = "Terrain Magma Bloom";

        /// <summary>直下床あり水域の基底色レイヤー名です。45pxの光学減衰は断面側で評価します。</summary>
        public const string LiquidDepthWithFloorTilemapName =
            "Liquid Depth With Floor Tilemap";

        /// <summary>直下床なし水域の基底色レイヤー名です。93pxの光学減衰は断面側で評価します。</summary>
        public const string LiquidDepthWithoutFloorTilemapName =
            "Liquid Depth Without Floor Tilemap";

        /// <summary>直下WorldLayerのGround上面を簡易投影するレイヤー名です。</summary>
        public const string UnderwaterFloorPreviewTilemapName =
            "Underwater Floor Preview Tilemap";

        /// <summary>実際の岸の垂直投影距離で減衰する水中Ground断面の専用レイヤー名です。</summary>
        public const string SubmergedGroundFaceTilemapName = "Submerged Ground Face Tilemap";

        /// <summary>乾燥崖から分離した水中断面Materialの保存先です。</summary>
        public const string SubmergedGroundFaceMaterialPath =
            "Assets/Farmware/Art/WorldGeneration/Materials/TerrainSubmergedGroundFace.mat";

        /// <summary>Wall前面の水面反射Proxyレイヤー名です。</summary>
        public const string WaterReflectionTilemapName = "Water Reflection Tilemap";

        /// <summary>断面と反射の間へ重ねる短い水面模様のレイヤー名です。</summary>
        public const string WaterSurfacePatternTilemapName = "Water Surface Pattern Tilemap";

        /// <summary>水際の調整値と分離して保持する水面模様Materialの保存先です。</summary>
        public const string WaterSurfacePatternMaterialPath =
            "Assets/Farmware/Art/WorldGeneration/Materials/TerrainWaterSurfacePattern.mat";

        /// <summary>奥・左・右の3bit接触線アニメーションレイヤー名です。</summary>
        public const string LiquidEdgeWaveTilemapName = "Liquid Edge Wave Tilemap";

        /// <summary>乾燥穴へ投影するGround断面レイヤー名です。</summary>
        public const string GroundFaceTilemapName = "Ground Face Tilemap";

        /// <summary>Wall前面2段を描くレイヤー名です。</summary>
        public const string WallFaceTilemapName = "Wall Face Tilemap";

        /// <summary>乾燥穴4段目以降を完全な黒で埋めるレイヤー名です。</summary>
        public const string DryCliffDeepBlackTilemapName = "Dry Cliff Deep Black Tilemap";

        /// <summary>Wall結合シルエットを左へ1pxずらす固定接地影レイヤー名です。</summary>
        public const string WallContactShadowLeftTilemapName = "Wall Contact Shadow Left Tilemap";

        /// <summary>Wall結合シルエットを右へ1pxずらす固定接地影レイヤー名です。</summary>
        public const string WallContactShadowRightTilemapName = "Wall Contact Shadow Right Tilemap";

        /// <summary>32px折れ目影の0〜15pxを描くレイヤー名です。</summary>
        public const string WallFoldShadowTopTilemapName = "Wall Fold Shadow Top Tilemap";

        /// <summary>32px折れ目影の16〜31pxを描くレイヤー名です。</summary>
        public const string WallFoldShadowBottomTilemapName = "Wall Fold Shadow Bottom Tilemap";

        /// <summary>製品用乾燥崖連続減衰Materialのパスです。</summary>
        public const string DryCliffMaterialPath =
            "Assets/Farmware/Art/WorldGeneration/Materials/TerrainDryCliffFace.mat";

        /// <summary>製品用固定接地影Materialのパスです。</summary>
        public const string ContactShadowMaterialPath =
            "Assets/Farmware/Art/WorldGeneration/Materials/TerrainWallContactShadow.mat";

        /// <summary>製品用32px折れ目影上段Materialのパスです。</summary>
        public const string FoldShadowTopMaterialPath =
            "Assets/Farmware/Art/WorldGeneration/Materials/TerrainWallFoldShadowTop.mat";

        /// <summary>製品用32px折れ目影下段Materialのパスです。</summary>
        public const string FoldShadowBottomMaterialPath =
            "Assets/Farmware/Art/WorldGeneration/Materials/TerrainWallFoldShadowBottom.mat";

        /// <summary>岸側セルだけ上端3pxを隠す製品液面Materialのパスです。</summary>
        public const string LiquidSurfaceMaterialPath =
            "Assets/Farmware/Art/WorldGeneration/Materials/TerrainLiquidSurface.mat";

        /// <summary>直下床あり水域の一定の基底色Materialのパスです。</summary>
        public const string LiquidDepthWithFloorMaterialPath =
            "Assets/Farmware/Art/WorldGeneration/Materials/TerrainLiquidDepthWithFloor.mat";

        /// <summary>直下床なし水域の一定の基底色Materialのパスです。</summary>
        public const string LiquidDepthWithoutFloorMaterialPath =
            "Assets/Farmware/Art/WorldGeneration/Materials/TerrainLiquidDepthWithoutFloor.mat";

        /// <summary>深度Shaderへ淡水色を渡すMaterialプロパティ名です。</summary>
        public const string FreshWaterDepthColorProperty = "_FreshWaterColor";

        /// <summary>深度Shaderへ海水色を渡すMaterialプロパティ名です。</summary>
        public const string SaltWaterDepthColorProperty = "_SaltWaterColor";

        /// <summary>垂直面だけを映す製品水面反射Materialのパスです。</summary>
        public const string WaterReflectionMaterialPath =
            "Assets/Farmware/Art/WorldGeneration/Materials/TerrainWaterReflection.mat";

        /// <summary>直下床プレビューへ使うURP 2D Lit既定Materialのパスです。</summary>
        public const string SpriteLitMaterialPath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat";

        /// <summary>水際形状と明暗を分離する製品用共有Materialのパスです。</summary>
        public const string ShorelineMaterialPath = TerrainLiquidProductPublisher.ShorelineMaterialPath;

        /// <summary>メニューから公開済み材質をSampleSceneへ接続し、Sceneを保存します。</summary>
        [MenuItem("Farmware/World Generation/Setup/Connect Product Terrain Presentation")]
        public static void ConnectFromMenu()
        {
            try
            {
                ConnectAndSave();
                EditorUtility.DisplayDialog(
                    "製品地形表示を接続しました",
                    "SampleSceneへ41材質、液体深度・反射・水際・マグマ模様、採用済みWall影を接続しました。",
                    "閉じる");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "製品地形表示を接続できません",
                    exception.Message,
                    "閉じる");
            }
        }

        /// <summary>
        /// 公開済み材質・液体カタログとMaterialを検証し、SampleSceneへ製品Tilemapを接続して保存します。
        /// </summary>
        public static void ConnectAndSave()
        {
            TerrainMaterialPresentationCatalog catalog =
                AssetDatabase.LoadAssetAtPath<TerrainMaterialPresentationCatalog>(
                    TerrainMaterialProductPublisher.CatalogPath);
            string catalogError = catalog == null
                ? "製品カタログAssetがありません。"
                : string.Empty;
            if (catalog == null || !catalog.TryInitialize(out catalogError))
            {
                throw new InvalidOperationException(
                    $"41材質の製品カタログを先に公開してください: {catalogError}");
            }

            TerrainLiquidPresentationCatalog liquidCatalog =
                AssetDatabase.LoadAssetAtPath<TerrainLiquidPresentationCatalog>(
                    TerrainLiquidProductPublisher.CatalogPath);
            string liquidCatalogError = liquidCatalog == null
                ? "製品液体カタログAssetがありません。"
                : string.Empty;
            if (liquidCatalog == null || !liquidCatalog.TryInitialize(out liquidCatalogError))
            {
                throw new InvalidOperationException(
                    $"液体3種の製品カタログを先に公開してください: {liquidCatalogError}");
            }

            Material dryCliff = LoadRequiredMaterial(DryCliffMaterialPath);
            Material submergedGroundFace = AssetDatabase.LoadAssetAtPath<Material>(SubmergedGroundFaceMaterialPath);
            if (submergedGroundFace == null)
            {
                // 既存の水際Materialを変更せず、新責務の水中断面Materialだけを新規作成します。
                Shader submergedShader = Shader.Find("Farmware/Terrain/Submerged Ground Face");
                if (submergedShader == null) throw new InvalidOperationException("水中断面Shaderがありません。");
                submergedGroundFace = new Material(submergedShader);
                AssetDatabase.CreateAsset(submergedGroundFace, SubmergedGroundFaceMaterialPath);
            }
            Material contactShadow = LoadRequiredMaterial(ContactShadowMaterialPath);
            Material foldTop = LoadRequiredMaterial(FoldShadowTopMaterialPath);
            Material foldBottom = LoadRequiredMaterial(FoldShadowBottomMaterialPath);
            Material liquidSurface = LoadRequiredMaterial(LiquidSurfaceMaterialPath);
            Material liquidDepthWithFloor = LoadRequiredMaterial(
                LiquidDepthWithFloorMaterialPath);
            Material liquidDepthWithoutFloor = LoadRequiredMaterial(
                LiquidDepthWithoutFloorMaterialPath);
            Material waterReflection = LoadRequiredMaterial(WaterReflectionMaterialPath);
            Material magmaSurfacePattern = LoadRequiredMaterial(MagmaSurfacePatternMaterialPath);
            Material surfacePattern = AssetDatabase.LoadAssetAtPath<Material>(WaterSurfacePatternMaterialPath);
            if (surfacePattern == null)
            {
                // 初回だけ既定値で作成し、再接続時にInspectorでの調整値を上書きしません。
                Shader patternShader = Shader.Find("Farmware/Terrain/Water Surface Pattern");
                if (patternShader == null) throw new InvalidOperationException("水面模様Shaderがありません。");
                surfacePattern = new Material(patternShader);
                AssetDatabase.CreateAsset(surfacePattern, WaterSurfacePatternMaterialPath);
            }
            Material spriteLit = LoadRequiredMaterial(SpriteLitMaterialPath);
            Material shoreline = LoadRequiredMaterial(ShorelineMaterialPath);
            ConfigureLiquidDepthMaterialColors(
                liquidCatalog,
                liquidDepthWithFloor,
                liquidDepthWithoutFloor);

            Scene scene = SceneManager.GetSceneByPath(ProductScenePath);
            bool openedForThisOperation = !scene.IsValid() || !scene.isLoaded;
            if (openedForThisOperation)
            {
                // 現在の検品Sceneを閉じずにAdditiveで編集し、ユーザーのScene作業状態を壊さないようにします。
                scene = EditorSceneManager.OpenScene(ProductScenePath, OpenSceneMode.Additive);
            }

            try
            {
                GameObject gridObject = FindRoot(scene, WorldGridName);
                float wallTopOffset =
                    TerrainMaterialVerticalProjectionUtility.WallTopOffsetInTiles;
                Vector3 wallLayerOffset = new Vector3(0f, wallTopOffset, 0f);
                Transform groundTransform = gridObject.transform.Find(GroundTilemapName);
                if (groundTransform == null)
                {
                    throw new InvalidOperationException(
                        $"製品Sceneに{GroundTilemapName}がありません: {ProductScenePath}");
                }

                ChunkTilemapPresenter presenter =
                    groundTransform.GetComponent<ChunkTilemapPresenter>();
                if (presenter == null)
                {
                    throw new InvalidOperationException(
                        $"{GroundTilemapName}にChunkTilemapPresenterがありません。");
                }

                Tilemap groundFace = EnsureTilemap(
                    gridObject.transform,
                    GroundFaceTilemapName,
                    Vector3.zero,
                    sortingLayerName: GroundSortingLayerName,
                    sortingOrder: 1,
                    dryCliff);
                Tilemap wallFace = EnsureTilemap(
                    gridObject.transform,
                    WallFaceTilemapName,
                    wallLayerOffset,
                    sortingLayerName: WallSortingLayerName,
                    sortingOrder: 1,
                    material: null);
                Tilemap deepBlack = EnsureTilemap(
                    gridObject.transform,
                    DryCliffDeepBlackTilemapName,
                    Vector3.zero,
                    sortingLayerName: GroundSortingLayerName,
                    sortingOrder: -1,
                    material: null);

                // 製品液面はTilemap全体を8px下げず、論理セル位置を維持したまま岸側だけShaderで上端3pxを隠します。
                Transform liquidBaseTransform = gridObject.transform.Find(LiquidBaseTilemapName);
                Tilemap liquidBase = liquidBaseTransform == null
                    ? null
                    : liquidBaseTransform.GetComponent<Tilemap>();
                TilemapRenderer liquidBaseRenderer = liquidBaseTransform == null
                    ? null
                    : liquidBaseTransform.GetComponent<TilemapRenderer>();
                if (liquidBase == null || liquidBaseRenderer == null)
                {
                    throw new InvalidOperationException(
                        $"製品Sceneに必須Tilemapがありません: {LiquidBaseTilemapName}");
                }

                liquidBaseTransform.localPosition = Vector3.zero;
                liquidBaseRenderer.enabled = true;
                liquidBaseRenderer.mode = TilemapRenderer.Mode.Chunk;
                liquidBaseRenderer.sortOrder = TilemapRenderer.SortOrder.BottomLeft;
                liquidBaseRenderer.sortingLayerName = WaterSortingLayerName;
                liquidBaseRenderer.sortingOrder = 0;
                liquidBaseRenderer.sharedMaterial = liquidSurface;
                EditorUtility.SetDirty(liquidBaseTransform);
                EditorUtility.SetDirty(liquidBaseRenderer);

                Tilemap floorPreview = EnsureTilemap(
                    gridObject.transform,
                    UnderwaterFloorPreviewTilemapName,
                    Vector3.zero,
                    sortingLayerName: WaterSortingLayerName,
                    sortingOrder: 1,
                    spriteLit);
                // 床は48px以深だけへ置くため、既存の最大被覆50%に対応する可視Alpha 50%を使います。
                // Litの照明は維持し、水色の上へ透過合成します。隣の岸の距離は参照しません。
                floorPreview.color = new Color(1f, 1f, 1f, 0.5f);
                Tilemap submergedFaces = EnsureTilemap(
                    gridObject.transform,
                    SubmergedGroundFaceTilemapName,
                    Vector3.zero,
                    sortingLayerName: WaterSortingLayerName,
                    sortingOrder: 1,
                    submergedGroundFace);
                Tilemap depthWithFloor = EnsureTilemap(
                    gridObject.transform,
                    LiquidDepthWithFloorTilemapName,
                    Vector3.zero,
                    sortingLayerName: WaterSortingLayerName,
                    sortingOrder: 0,
                    liquidDepthWithFloor);
                Tilemap depthWithoutFloor = EnsureTilemap(
                    gridObject.transform,
                    LiquidDepthWithoutFloorTilemapName,
                    Vector3.zero,
                    sortingLayerName: WaterSortingLayerName,
                    sortingOrder: 0,
                    liquidDepthWithoutFloor);
                Tilemap pattern = EnsureTilemap(
                    gridObject.transform,
                    WaterSurfacePatternTilemapName,
                    Vector3.zero,
                    sortingLayerName: WaterSortingLayerName,
                    sortingOrder: 2,
                    surfacePattern);
                Tilemap reflection = EnsureTilemap(
                    gridObject.transform,
                    WaterReflectionTilemapName,
                    Vector3.zero,
                    sortingLayerName: WaterSortingLayerName,
                    sortingOrder: 3,
                    waterReflection);
                Tilemap edgeWave = EnsureTilemap(
                    gridObject.transform,
                    LiquidEdgeWaveTilemapName,
                    Vector3.zero,
                    sortingLayerName: WaterSortingLayerName,
                    sortingOrder: 4,
                    shoreline);

                // マグマ専用層は再接続時も同じGameObjectを再利用し、製品名から直接構成します。
                Tilemap magmaPattern = EnsureTilemap(
                    gridObject.transform,
                    MagmaSurfacePatternTilemapName,
                    Vector3.zero,
                    sortingLayerName: WaterSortingLayerName,
                    sortingOrder: 2,
                    magmaSurfacePattern);
                magmaPattern.animationFrameRate = 0f;
                EditorUtility.SetDirty(magmaPattern);

                float pixelsPerUnit = TerrainTileArtGenerationSettings.TileSizeInPixels;
                Vector3 contactBaseOffset = new Vector3(
                    0f,
                    wallTopOffset
                        + (TerrainWallShadowUtility.ContactShadowOffsetYPixels / pixelsPerUnit),
                    0f);
                Tilemap contactLeft = EnsureTilemap(
                    gridObject.transform,
                    WallContactShadowLeftTilemapName,
                    contactBaseOffset + new Vector3(
                        -TerrainWallShadowUtility.ContactShadowHorizontalSpreadPixels / pixelsPerUnit,
                        0f,
                        0f),
                    sortingLayerName: GroundSortingLayerName,
                    sortingOrder: 1,
                    contactShadow);
                Tilemap contactRight = EnsureTilemap(
                    gridObject.transform,
                    WallContactShadowRightTilemapName,
                    contactBaseOffset + new Vector3(
                        TerrainWallShadowUtility.ContactShadowHorizontalSpreadPixels / pixelsPerUnit,
                        0f,
                        0f),
                    sortingLayerName: GroundSortingLayerName,
                    sortingOrder: 1,
                    contactShadow);
                Tilemap foldTopTilemap = EnsureTilemap(
                    gridObject.transform,
                    WallFoldShadowTopTilemapName,
                    wallLayerOffset,
                    sortingLayerName: WallSortingLayerName,
                    sortingOrder: 2,
                    foldTop);
                Tilemap foldBottomTilemap = EnsureTilemap(
                    gridObject.transform,
                    WallFoldShadowBottomTilemapName,
                    wallLayerOffset,
                    sortingLayerName: WallSortingLayerName,
                    sortingOrder: 2,
                    foldBottom);

                // Wall上面は製品経路でも既存Rock Wall Tilemapを再利用しますが、一体型Spriteではなくカタログ選択Tileだけを書き込みます。
                Transform wallTopTransform = gridObject.transform.Find("Rock Wall Top Tilemap");
                if (wallTopTransform == null)
                {
                    // 旧Sceneをまだ再構築していない場合は一体型時代の名前を引き継ぎ、製品名へその場で更新します。
                    wallTopTransform = gridObject.transform.Find("Rock Wall Tilemap");
                    if (wallTopTransform != null)
                    {
                        wallTopTransform.name = "Rock Wall Top Tilemap";
                    }
                }

                if (wallTopTransform == null)
                {
                    throw new InvalidOperationException("製品SceneにRock Wall Top Tilemapがありません。");
                }

                // Tileは論理根元セルへ書き込み、表示レイヤ全体を2タイル奥へ持ち上げます。
                // 同じ補正を上面・Face・折れ目影・固定影へ適用することで、最下段Faceが論理根元に戻ります。
                wallTopTransform.localPosition = wallLayerOffset;
                TilemapRenderer wallTopRenderer = wallTopTransform.GetComponent<TilemapRenderer>();
                wallTopRenderer.sortingLayerName = WallSortingLayerName;
                wallTopRenderer.sortingOrder = 0;

                // Sorting LayerはsortingOrderより優先されます。Wallを専用Layerへ移した後も、
                // 通常資源はWallより手前、手前側2セル以内のWallに遮蔽される資源だけはWallより奥になるよう明示します。
                ConfigureExistingRenderer(
                    gridObject.transform,
                    "Resource Behind Rock Wall Tilemap",
                    GroundSortingLayerName,
                    sortingOrder: 2);
                ConfigureExistingRenderer(
                    gridObject.transform,
                    "Resource Tilemap",
                    WallSortingLayerName,
                    sortingOrder: 10);
                ConfigureExistingRenderer(
                    gridObject.transform,
                    "Rock Wall Resource Tilemap",
                    WallSortingLayerName,
                    sortingOrder: 11);
                SetExistingLocalPosition(
                    gridObject.transform,
                    "Rock Wall Resource Tilemap",
                    wallLayerOffset);
                EnsureGlobalLightIlluminatesTerrain(scene);
                ConfigureMagmaBloom(scene);

                presenter.ConfigureTerrainMaterialPresentation(
                    catalog,
                    groundFace,
                    wallFace,
                    deepBlack);
                presenter.ConfigureTerrainWallShadowPresentation(
                    contactLeft,
                    contactRight,
                    foldTopTilemap,
                    foldBottomTilemap);
                presenter.ConfigureTerrainLiquidPresentation(
                    liquidCatalog,
                    depthWithFloor,
                    depthWithoutFloor,
                    floorPreview,
                    submergedFaces,
                    pattern,
                    magmaPattern,
                    reflection,
                    AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>(TerrainMaterialProductPublisher.AtlasPath),
                    edgeWave);
                EditorUtility.SetDirty(presenter);

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ProductScenePath))
                {
                    throw new InvalidOperationException(
                        $"製品Sceneを保存できませんでした: {ProductScenePath}");
                }
            }
            finally
            {
                if (openedForThisOperation && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, removeScene: true);
                }
            }
        }

        /// <summary>製品Sceneへ採用済みBloomとCamera設定を接続します。既存ProfileのInspector調整値は保持します。</summary>
        /// <param name="scene">未保存変更を確認済みの製品Sceneです。変更を保存する責任は呼び出し元が持ちます。</param>
        public static void ConfigureMagmaBloom(Scene scene)
        {
            if (UniversalRenderPipeline.asset == null || !UniversalRenderPipeline.asset.supportsHDR)
                throw new InvalidOperationException("マグマ発光にはHDRを有効にしたURP設定が必要です。");

            Camera camera = null;
            GameObject volumeObject = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == MagmaBloomVolumeName) volumeObject = root;
                foreach (Camera candidate in root.GetComponentsInChildren<Camera>(true))
                    if (candidate.CompareTag("MainCamera")) camera = candidate;
            }
            if (camera == null || !camera.TryGetComponent(out UniversalAdditionalCameraData cameraData)
                || cameraData.renderType != CameraRenderType.Base)
                throw new InvalidOperationException("製品SceneにURPのBase MainCameraが必要です。");

            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(MagmaBloomProfilePath);
            if (profile == null)
            {
                // 初回だけ採用値で作成します。再接続で調整値やGUIDを上書きしないため、既存Profileは再生成しません。
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "TerrainMagmaBloom";
                AssetDatabase.CreateAsset(profile, MagmaBloomProfilePath);
                Bloom bloom = profile.Add<Bloom>(true);
                bloom.threshold.value = 1.15f;
                bloom.intensity.value = 0.4f;
                bloom.scatter.value = 0.25f;
                bloom.maxIterations.value = 3;
                bloom.tint.value = new Color(1f, 0.72f, 0.35f);
                bloom.downscale.value = BloomDownscaleMode.Half;
                bloom.highQualityFiltering.value = true;
                // VolumeComponentは別のUnity Objectなので、ProfileのSubAssetとして保存して参照切れを防ぎます。
                AssetDatabase.AddObjectToAsset(bloom, profile);
                EditorUtility.SetDirty(bloom);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
            }
            if (!profile.TryGet(out Bloom _))
                throw new InvalidOperationException("製品マグマBloom ProfileにBloom overrideがありません。");

            if (volumeObject == null)
            {
                volumeObject = new GameObject(MagmaBloomVolumeName);
                SceneManager.MoveGameObjectToScene(volumeObject, scene);
            }
            if (!volumeObject.TryGetComponent(out Volume volume)) volume = volumeObject.AddComponent<Volume>();
            volumeObject.layer = 0;
            volume.enabled = true;
            volume.isGlobal = true;
            volume.weight = 1f;
            volume.priority = 0f;
            volume.sharedProfile = profile;
            // HDR色を1より大きいままBloomへ渡し、Default Layerの製品VolumeをCameraが受け取るようにします。
            camera.allowHDR = true;
            cameraData.renderPostProcessing = true;
            cameraData.volumeLayerMask |= 1;
            GameObject ground = FindRoot(scene, WorldGridName).transform.Find(GroundTilemapName).gameObject;
            if (!ground.TryGetComponent(out TerrainMagmaShoreLighting lighting)) lighting = ground.AddComponent<TerrainMagmaShoreLighting>();
            lighting.Configure(camera.GetComponent<CameraPanController>());
            ChunkTilemapPresenter presenter = ground.GetComponent<ChunkTilemapPresenter>();
            presenter.ConfigureMagmaShoreLighting(lighting);
            EditorUtility.SetDirty(lighting);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(cameraData);
            EditorUtility.SetDirty(volume);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>指定Scene直下から固定名のルートGameObjectを探します。</summary>
        /// <param name="scene">製品SampleSceneです。</param>
        /// <param name="name">検索するルート名です。</param>
        /// <returns>一意に見つかったルートGameObjectです。</returns>
        private static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                if (string.Equals(roots[index].name, name, StringComparison.Ordinal))
                {
                    return roots[index];
                }
            }

            throw new InvalidOperationException($"製品Sceneに{name}ルートがありません。");
        }

        /// <summary>Grid直下のTilemapを再利用または作成し、Transform、描画順、Materialを製品値へ揃えます。</summary>
        /// <param name="grid">全地形Tilemapを所有するGrid Transformです。</param>
        /// <param name="name">安定したTilemap GameObject名です。</param>
        /// <param name="localPosition">1セルを1 unitとしたローカル位置です。</param>
        /// <param name="sortingLayerName">受影面を分けるSorting Layer名です。</param>
        /// <param name="sortingOrder">同じSorting Layer内の描画順です。</param>
        /// <param name="material">専用Shaderを使う場合の共有Material、既定Sprite Materialならnullです。</param>
        /// <returns>Presenterへ接続するTilemapです。</returns>
        private static Tilemap EnsureTilemap(
            Transform grid,
            string name,
            Vector3 localPosition,
            string sortingLayerName,
            int sortingOrder,
            Material material)
        {
            Transform existing = grid.Find(name);
            GameObject gameObject;
            if (existing == null)
            {
                gameObject = new GameObject(name);
                gameObject.transform.SetParent(grid, worldPositionStays: false);
            }
            else
            {
                gameObject = existing.gameObject;
            }

            gameObject.transform.localPosition = localPosition;
            gameObject.transform.localRotation = Quaternion.identity;
            gameObject.transform.localScale = Vector3.one;
            Tilemap tilemap = gameObject.GetComponent<Tilemap>();
            if (tilemap == null)
            {
                tilemap = gameObject.AddComponent<Tilemap>();
            }

            TilemapRenderer renderer = gameObject.GetComponent<TilemapRenderer>();
            if (renderer == null)
            {
                renderer = gameObject.AddComponent<TilemapRenderer>();
            }

            renderer.mode = TilemapRenderer.Mode.Chunk;
            renderer.sortOrder = TilemapRenderer.SortOrder.BottomLeft;
            renderer.enabled = true;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }

            return tilemap;
        }

        /// <summary>製品MaterialをAssetDatabaseから読み込み、不足時はSceneを変更する前に停止します。</summary>
        /// <param name="assetPath">読み込むMaterialのAssetパスです。</param>
        /// <returns>製品Sceneへ割り当てる共有Materialです。</returns>
        private static Material LoadRequiredMaterial(string assetPath)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                throw new InvalidOperationException($"製品Materialを読み込めません: {assetPath}");
            }

            return material;
        }

        /// <summary>
        /// Tile Tintを水種・段数のデータへ使えるよう、淡水・海水の一定の製品色をMaterial側へ同期します。
        /// </summary>
        /// <param name="catalog">淡水・海水の深度色を正として保持する製品カタログです。</param>
        /// <param name="withFloor">直下床あり水域の基底色Materialです。</param>
        /// <param name="withoutFloor">直下床なし水域の基底色Materialです。</param>
        private static void ConfigureLiquidDepthMaterialColors(
            TerrainLiquidPresentationCatalog catalog,
            Material withFloor,
            Material withoutFloor)
        {
            if (!catalog.TryGetDefinition(
                    TerrainLiquidVisualKind.FreshWater,
                    out TerrainLiquidPresentationDefinition freshWater)
                || !catalog.TryGetDefinition(
                    TerrainLiquidVisualKind.SaltWater,
                    out TerrainLiquidPresentationDefinition saltWater))
            {
                throw new InvalidOperationException(
                    "製品液体カタログから淡水・海水の深度色を取得できません。");
            }

            Material[] materials = { withFloor, withoutFloor };
            for (int index = 0; index < materials.Length; index++)
            {
                Material material = materials[index];
                if (!material.HasProperty(FreshWaterDepthColorProperty)
                    || !material.HasProperty(SaltWaterDepthColorProperty))
                {
                    throw new InvalidOperationException(
                        $"製品深度Materialに液体色プロパティがありません: {material.name}");
                }

                material.SetColor(FreshWaterDepthColorProperty, freshWater.DepthOverlayColor);
                material.SetColor(SaltWaterDepthColorProperty, saltWater.DepthOverlayColor);
                EditorUtility.SetDirty(material);
            }

            // Sceneだけ保存して色変更を失わないよう、共有Material Assetをここで永続化します。
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 既存TilemapRendererを指定Sorting Layerと順序へ揃えます。
        /// </summary>
        /// <param name="grid">対象Tilemapを直接所有する製品Gridです。</param>
        /// <param name="name">必須Tilemap GameObject名です。</param>
        /// <param name="sortingLayerName">設定するSorting Layer名です。</param>
        /// <param name="sortingOrder">同じSorting Layer内の描画順です。</param>
        private static void ConfigureExistingRenderer(
            Transform grid,
            string name,
            string sortingLayerName,
            int sortingOrder)
        {
            Transform child = grid.Find(name);
            TilemapRenderer renderer = child == null
                ? null
                : child.GetComponent<TilemapRenderer>();
            if (renderer == null)
            {
                throw new InvalidOperationException(
                    $"製品Sceneに必須TilemapRendererがありません: {name}");
            }

            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;
            EditorUtility.SetDirty(renderer);
        }

        /// <summary>
        /// 必須の既存Tilemapを、論理根元から求めた製品表示位置へ移動します。
        /// </summary>
        /// <param name="grid">対象Tilemapを直接所有する製品Gridです。</param>
        /// <param name="name">必須Tilemap GameObject名です。</param>
        /// <param name="localPosition">1セルを1 unitとしたローカル位置です。</param>
        private static void SetExistingLocalPosition(
            Transform grid,
            string name,
            Vector3 localPosition)
        {
            Transform child = grid.Find(name);
            if (child == null || child.GetComponent<Tilemap>() == null)
            {
                throw new InvalidOperationException(
                    $"製品Sceneに必須Tilemapがありません: {name}");
            }

            child.localPosition = localPosition;
            EditorUtility.SetDirty(child);
        }

        /// <summary>
        /// 製品SceneのGlobal Light 2DへWallと水中床のSorting Layerを追加し、Lit表示が無照明で黒くなることを防ぎます。
        /// </summary>
        /// <param name="scene">製品地形を表示するSampleSceneです。</param>
        private static void EnsureGlobalLightIlluminatesTerrain(Scene scene)
        {
            int[] requiredLayers = { SortingLayer.NameToID(WallSortingLayerName), SortingLayer.NameToID(WaterSortingLayerName) };
            Light2D[] lights = UnityEngine.Object.FindObjectsByType<Light2D>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            bool foundSceneGlobalLight = false;

            for (int lightIndex = 0; lightIndex < lights.Length; lightIndex++)
            {
                Light2D light = lights[lightIndex];
                if (light.gameObject.scene != scene
                    || light.lightType != Light2D.LightType.Global)
                {
                    continue;
                }

                foundSceneGlobalLight = true;
                foreach (int required in requiredLayers)
                {
                    int[] currentLayers = light.targetSortingLayers;
                    bool alreadyIncluded = false;
                    for (int layerIndex = 0; layerIndex < currentLayers.Length; layerIndex++)
                    {
                        if (currentLayers[layerIndex] == required)
                        {
                            alreadyIncluded = true;
                            break;
                        }
                    }
                    if (alreadyIncluded) continue;
                    // 既存対象は維持します。水色ShaderはUnlitのため、WaterSurface追加で変わるのはLitの水中床だけです。
                    var updatedLayers = new int[currentLayers.Length + 1];
                    Array.Copy(currentLayers, updatedLayers, currentLayers.Length);
                    updatedLayers[updatedLayers.Length - 1] = required;
                    light.targetSortingLayers = updatedLayers;
                    EditorUtility.SetDirty(light);
                }
            }

            if (!foundSceneGlobalLight)
            {
                throw new InvalidOperationException(
                    "製品SceneにGlobal Light 2Dがないため、壁と水中床を照明対象へ追加できません。");
            }
        }
    }
}
