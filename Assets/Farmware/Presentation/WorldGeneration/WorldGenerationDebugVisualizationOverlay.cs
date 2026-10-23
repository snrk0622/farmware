// このファイルは、ECSで生成済みの可視タイルと固定水文・派生水文をPlayMode中だけ色分けして、生成結果を確認できるようにします。
// 操作入口はEditor AssemblyのWorldGenerationDebugVisualizationMenuに置き、Simulationとプレイヤー用F3/F4入力から分離します。

using System.Collections.Generic;
using Farmware.Simulation;
using Farmware.Simulation.Animals;
using Farmware.WorldGeneration.Animals;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 開発者がUnity Editor上で選択できる、ワールド生成データの表示方法です。
    /// None以外はPlayMode中の可視タイルへ色を重ねますが、ECSデータや生成結果は変更しません。
    /// </summary>
    public enum WorldGenerationDebugVisualizationMode : byte
    {
        /// <summary>開発者用の色分けを表示しません。</summary>
        None = 0,

        /// <summary>新18種の論理Biome IDを固定色で表示します。</summary>
        Biome = 1,

        /// <summary>0〜65535の連続標高値を黒から白の濃淡で表示します。</summary>
        Elevation = 2,

        /// <summary>標高補正前の0〜65535の基礎気温を青から赤のグラデーションで表示します。</summary>
        BaseTemperature = 3,

        /// <summary>水域や現在土壌水分と独立した0〜65535の基礎湿度を茶から水色のグラデーションで表示します。</summary>
        BaseMoisture = 4,

        /// <summary>水域を陸地・淡水・海水の固定色で表示します。</summary>
        WaterSalinity = 5,

        /// <summary>水域の生成由来を陸地・川・池・海の固定色で表示します。</summary>
        WaterBody = 6,

        /// <summary>標高寒冷補正後の0〜65535の地形気温を青から赤のグラデーションで表示します。</summary>
        TerrainTemperature = 7,

        /// <summary>最終岩壁を非岩壁・露出面・連続した内部影の濃さとして表示します。</summary>
        RockWall = 8,

        /// <summary>最低間隔と配置競合を適用した後の木・石・砂を固定色で表示します。</summary>
        Resources = 9,

        /// <summary>配置済み植生から独立した0〜65535の木本植生ポテンシャルを茶から緑で表示します。</summary>
        WoodyVegetationPotential = 10,

        /// <summary>マグマ本体、淡水抑制用の火山影響域、それ以外を固定色で表示します。</summary>
        Magma = 11,

        /// <summary>プレイヤー表示へ出さない標準・肥沃・砂質のSoilTypeを固定色で表示します。</summary>
        Soil = 12,

        /// <summary>通常設備・橋の配置可否と、橋の耐熱要件を地形契約ごとの固定色で表示します。</summary>
        TerrainPlacement = 13,

        /// <summary>動物種定義を満たす候補タイルと、初期補充で確定した個体位置を別色で表示します。</summary>
        AnimalPopulation = 14,

        /// <summary>固定9地点の標高差から求めた0〜65535の地形起伏を明色から暗色で表示します。</summary>
        TerrainRuggedness = 15,

        /// <summary>海への近さと開放方向数から求めた0〜65535の海洋影響を陸色から海色で表示します。</summary>
        CoastalInfluence = 16,

        /// <summary>旧Weirdnessと独立した0〜65535の火山活動度を暗灰から橙で表示します。</summary>
        VolcanicActivityPotential = 17,

        /// <summary>卓越基盤地質の8分類を固定色で表示します。</summary>
        GeologyClass = 18,

        /// <summary>地質Definitionの0〜65535の基礎透水性を表示します。</summary>
        GeologyPermeability = 19,

        /// <summary>地質Definitionの0〜65535の基礎地下水保持力を表示します。</summary>
        GeologyGroundwaterStorage = 20,

        /// <summary>地質Definitionの0〜65535の基礎侵食抵抗を表示します。</summary>
        GeologyErosionResistance = 21,

        /// <summary>カルスト・活動的火山の必須条件フラグを固定色で表示します。</summary>
        GeologyFlags = 22,

        /// <summary>各タイルを支配する地質Voronoiサイトを安定した疑似色で表示します。</summary>
        GeologyDominantSite = 23,

        /// <summary>上下左右で支配地質サイトが切り替わるタイルを明色で表示します。</summary>
        GeologySiteBoundary = 24,

        /// <summary>8方向の固定流向と、下流を持たない終端を固定色で表示します。</summary>
        HydrologyFlowDirection = 25,

        /// <summary>下流終端のMarine・ClosedBasin・LongDrainageを固定色で表示します。</summary>
        HydrologyTerminal = 26,

        /// <summary>1〜4,096セルの集水面積を連続色で表示し、上限打切りを別色で示します。</summary>
        HydrologyCatchment = 27,

        /// <summary>固定表面流出を接続なし・Stream・Riverの固定色で表示します。</summary>
        HydrologySurfaceWater = 28,

        /// <summary>閉鎖流域、固定湖候補、湖面、越流、打切り流域を固定色で表示します。</summary>
        HydrologyLake = 29,

        /// <summary>0〜65,535の固定地下水ポテンシャルを低値から高値の連続色で表示します。</summary>
        HydrologyGroundwater = 30,

        /// <summary>0〜65,535の固定自然土壌飽和ポテンシャルを乾燥側から飽和側の連続色で表示します。</summary>
        HydrologyNaturalSoilSaturation = 31,

        /// <summary>固定表流水Bufferで最終確定した泉流を、ほかの表流水と分けて表示します。</summary>
        HydrologySpring = 32,

        /// <summary>0〜65,535の固定自然冠水ポテンシャルを低値から高値の連続色で表示します。</summary>
        HydrologyNaturalFlooding = 33,

        /// <summary>湿原条件外、局所湿原候補、広域湿原規模を3種類の固定色で表示します。</summary>
        HydrologyWetlandCandidate = 34
    }

    /// <summary>
    /// カメラに見えるGeneratedTileだけをIMGUIの色面として重ねる、開発者専用Presentationです。
    /// 通常の地形Tilemapを変更せず、Editorメニューで選択された間だけECSの読み取りを行います。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class WorldGenerationDebugVisualizationOverlay : MonoBehaviour
    {
        /// <summary>極端なズームアウト時にIMGUI描画が増え過ぎないよう制限する最大可視タイル数です。</summary>
        private const int MaximumDrawableTileCount = 8192;

        // タイルをGameビューへ投影するCameraです。通常は同じGameObjectのMain Cameraを設定します。
        [Tooltip("開発者用ノイズ・バイオーム表示を重ねる2Dカメラです。未指定なら同じGameObjectのCameraを使用します。")]
        [SerializeField] private Camera targetCamera;

        // ワールド座標と整数タイル座標を相互変換するGridです。通常の地形表示と同じGridを設定します。
        [Tooltip("地形Tilemapを所有するGridです。セル原点とサイズをデバッグ色面へ反映します。未指定時は1 Unity unitを1タイルとして扱います。")]
        [SerializeField] private GridLayout targetGrid;

        // CameraやTilemapが使用する現在の表示原点です。開発者表示も同じ原点を使い、遠方で相対セルを絶対座標と誤認しないようにします。
        [Tooltip("Main Cameraの表示原点を管理するComponentです。遠方の相対セルをSimulationの絶対タイル座標へ戻すために使用します。")]
        [SerializeField] private CameraPanController cameraPanController;

        // Windowboxの黒帯を除いた描画領域を求めるため、Cameraと同じGameObjectから取得して保持します。
        private PixelPerfectCamera pixelPerfectCamera;

        // Editorメニューが実行中だけ変更する現在の可視化モードです。Sceneやセーブデータへは保存しません。
        private WorldGenerationDebugVisualizationMode currentMode;

        // Editorメニューが選択する診断対象レイヤーです。Transform Zには依存せず、Sceneやセーブへ保存しません。
        private WorldLayer currentLayer = WorldLayer.Surface;

        // PlayMode終了やDefault World再作成を検知してEntityQueryを作り直すために保持するWorldです。
        private World presentationWorld;

        // 座標、生成状態、地形・資源Bufferを持つチャンクだけを読み取る再利用Queryです。
        private EntityQuery chunkQuery;

        // 中央タイルHUDへ現在の閾値を表示するため、WorldGenerationSettings Singletonを読むQueryです。
        private EntityQuery settingsQuery;

        // 中央タイルHUDへ資源の最低間隔と群半径を表示するため、ResourceGenerationSettings Singletonを読むQueryです。
        private EntityQuery resourceSettingsQuery;

        // 動物種の適格条件を読むSingleton Queryです。候補表示を選んだRepaintだけ参照します。
        private EntityQuery animalCatalogQuery;

        // 確定済み野生動物の整数タイル座標を読むQueryです。Simulation状態は変更しません。
        private EntityQuery animalPositionQuery;

        // EntityQueryを現在のDefault World向けに作成済みかを示します。
        private bool queriesAreCreated;

        // 現在のIMGUI描画で使用する、黒帯を除いたGameビュー矩形です。
        private Rect activeGuiViewportRect;

        // モード名、凡例、中央タイル値を描くため、初回だけ作成して再利用するGUI Styleです。
        private GUIStyle informationStyle;

        // Rock Wall表示が水文・Biome確定前の生成へ戻らないよう、完成地形Bufferをチャンク座標で引くRepaint内索引です。
        private readonly Dictionary<ChunkCoordinate, Entity> completedTerrainChunkEntities =
            new Dictionary<ChunkCoordinate, Entity>();

        // 生成済み水文Bufferをセル座標で引くRepaint内キャッシュです。水文セルは中心が隣接チャンクに属する場合があるため、
        // 描画中の地形チャンクだけへ依存せず、ロード済み全チャンクの64件Bufferから一度だけ構築します。
        private readonly Dictionary<long, GeneratedHydrologyCell> hydrologyCellCache =
            new Dictionary<long, GeneratedHydrologyCell>();

        // 可視タイル座標だけを保持し、変更のない画面外ECS Chunkの個体を毎Repaintで読み直さない索引です。
        private readonly AnimalPositionViewportCache animalPositionCache = new AnimalPositionViewportCache();

        /// <summary>Editorメニューが選択中として表示する、現在の開発者用可視化モードです。</summary>
        public WorldGenerationDebugVisualizationMode CurrentMode => currentMode;

        /// <summary>Editorメニューが選択中として表示する、現在の診断対象ワールドレイヤーです。</summary>
        public WorldLayer CurrentLayer => currentLayer;

        /// <summary>
        /// Editor構築ツールまたはテストから、投影Camera、タイル座標用Grid、表示原点の所有者をまとめて設定します。
        /// </summary>
        /// <param name="camera">可視化を重ねる2Dカメラです。</param>
        /// <param name="grid">通常地形と同じセル変換を提供するGridです。</param>
        /// <param name="panController">CameraとTilemapが共有する表示原点の所有者です。</param>
        public void Configure(
            Camera camera,
            GridLayout grid,
            CameraPanController panController)
        {
            targetCamera = camera;
            targetGrid = grid;
            cameraPanController = panController;
            pixelPerfectCamera = targetCamera == null
                ? null
                : targetCamera.GetComponent<PixelPerfectCamera>();
        }

        /// <summary>
        /// Component読み込み直後にUnityから呼ばれ、未設定のCamera、表示原点、Pixel Perfect Camera参照を補います。
        /// </summary>
        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }

            if (cameraPanController == null)
            {
                cameraPanController = GetComponent<CameraPanController>();
            }

            pixelPerfectCamera = targetCamera == null
                ? null
                : targetCamera.GetComponent<PixelPerfectCamera>();
        }

        /// <summary>
        /// 現在のCamera Viewportへ少しでも含まれる範囲を、Simulationと同じ絶対タイル座標で返します。
        /// 開発者表示、PlayModeテスト、将来のGameビュー診断入力が同じ変換境界を確認するために使用します。
        /// </summary>
        /// <param name="minimumTileX">成功時に、画面へ含まれる最小の絶対水平タイル座標を返します。</param>
        /// <param name="maximumTileX">成功時に、画面へ含まれる最大の絶対水平タイル座標を返します。</param>
        /// <param name="minimumTileY">成功時に、画面へ含まれる最小の絶対垂直タイル座標を返します。</param>
        /// <param name="maximumTileY">成功時に、画面へ含まれる最大の絶対垂直タイル座標を返します。</param>
        /// <returns>投影Cameraが設定され、可視範囲を計算できる場合はtrueです。</returns>
        public bool TryGetVisibleAbsoluteTileBounds(
            out int minimumTileX,
            out int maximumTileX,
            out int minimumTileY,
            out int maximumTileY)
        {
            if (targetCamera == null)
            {
                minimumTileX = default;
                maximumTileX = default;
                minimumTileY = default;
                maximumTileY = default;
                return false;
            }

            VisibleTileBounds bounds = CalculateVisibleTileBounds();
            minimumTileX = bounds.MinimumTileX;
            maximumTileX = bounds.MaximumTileX;
            minimumTileY = bounds.MinimumTileY;
            maximumTileY = bounds.MaximumTileY;
            return true;
        }

        /// <summary>
        /// Camera中央を含む相対セルへ現在の表示原点を加え、Simulation上の絶対タイル座標へ戻します。
        /// 中央タイル情報が色面と同じ絶対セルを参照しているか確認する入口としても使用します。
        /// </summary>
        /// <param name="absoluteTileCoordinate">成功時に、Camera中央を含む絶対タイル座標を返します。</param>
        /// <returns>Camera中央が有限なint座標へ変換できる場合はtrueです。</returns>
        public bool TryGetAbsoluteCameraCenterTile(out int2 absoluteTileCoordinate)
        {
            if (targetCamera == null)
            {
                absoluteTileCoordinate = default;
                return false;
            }

            Vector3 centerWorld = targetCamera.transform.position;
            Vector3Int relativeCenterCell = targetGrid == null
                ? new Vector3Int(
                    Mathf.FloorToInt(centerWorld.x),
                    Mathf.FloorToInt(centerWorld.y),
                    0)
                : targetGrid.WorldToCell(centerWorld);
            return WorldCoordinateConversion.TryToAbsoluteTileCoordinate(
                new RelativeCellCoordinate(relativeCenterCell.x, relativeCenterCell.y),
                GetCurrentDisplayOrigin(),
                out absoluteTileCoordinate);
        }

        /// <summary>
        /// Simulation上の絶対タイル座標を、現在の表示原点から見たGridセルへ変換します。
        /// 開発者用色面をTilemapと同じセルへ投影し、遠方でも1セルのずれを作らないために使用します。
        /// </summary>
        /// <param name="absoluteTileCoordinate">投影対象となるSimulation上の絶対タイル座標です。</param>
        /// <param name="displayCell">成功時に、現在のGridへ投影する相対セル座標を返します。</param>
        /// <returns>表示原点との差がUnityのGridセルで扱えるint範囲内ならtrueです。</returns>
        public bool TryGetDisplayCellForAbsoluteTile(
            int2 absoluteTileCoordinate,
            out Vector3Int displayCell)
        {
            RelativeCellCoordinate relativeCell = WorldCoordinateConversion.ToRelativeCellCoordinate(
                absoluteTileCoordinate,
                GetCurrentDisplayOrigin());
            if (relativeCell.X < int.MinValue
                || relativeCell.X > int.MaxValue
                || relativeCell.Y < int.MinValue
                || relativeCell.Y > int.MaxValue)
            {
                displayCell = default;
                return false;
            }

            displayCell = new Vector3Int((int)relativeCell.X, (int)relativeCell.Y, 0);
            return true;
        }

        /// <summary>
        /// Componentが有効になったときにUnityから呼ばれ、Play開始時は必ず通常表示から始めます。
        /// </summary>
        private void OnEnable()
        {
            currentMode = WorldGenerationDebugVisualizationMode.None;
        }

        /// <summary>
        /// Component破棄時にUnityから呼ばれ、このPresentationが明示作成したEntityQueryを解放します。
        /// </summary>
        private void OnDestroy()
        {
            DisposeQueries();
        }

        /// <summary>
        /// UnityのIMGUI Repaint時に呼ばれ、選択中モードの色面と中央タイル情報をWindowbox内へ描きます。
        /// Editor以外では処理本体をコンパイルせず、製品版でECS走査や描画負荷が発生しないようにします。
        /// </summary>
        private void OnGUI()
        {
#if UNITY_EDITOR
            if (currentMode == WorldGenerationDebugVisualizationMode.None
                || Event.current.type != EventType.Repaint
                || targetCamera == null)
            {
                return;
            }

            EnsureGuiStyle();
            activeGuiViewportRect = CalculateGuiViewportRect();
            if (activeGuiViewportRect.width <= 0f || activeGuiViewportRect.height <= 0f)
            {
                return;
            }

            // Windowboxの黒帯へ開発者表示がはみ出さないよう、通常地形と同じ矩形で明示的にクリップします。
            GUI.BeginGroup(activeGuiViewportRect);
            try
            {
                if (!TryEnsureQueries())
                {
                    DrawInformationPanel(
                        false,
                        default,
                        default,
                        default,
                        false,
                        default,
                        false,
                        default,
                        default,
                        default,
                        default,
                        default,
                        default);
                    return;
                }

                DrawVisibleTilesAndInformation();
            }
            finally
            {
                GUI.EndGroup();
            }
#endif
        }

        /// <summary>
        /// Editorメニューから可視化モードを変更します。Simulation、Tilemap、セーブデータには副作用を与えません。
        /// </summary>
        /// <param name="mode">次のIMGUI Repaintから表示する開発者用モードです。</param>
        public void SetMode(WorldGenerationDebugVisualizationMode mode)
        {
#if UNITY_EDITOR
            currentMode = mode;
#else
            // 製品版には操作UIを含めないため、誤って呼ばれても可視化を有効にしません。
            currentMode = WorldGenerationDebugVisualizationMode.None;
#endif
        }

        /// <summary>
        /// Editorメニューから診断対象の固定ワールドレイヤーを変更します。
        /// WORLD-01CではSurfaceだけに生成済みBufferがあるため、別層は「生成待ち」として空表示になります。
        /// </summary>
        /// <param name="layer">次のIMGUI Repaintから検索キーへ含める固定ワールドレイヤーです。</param>
        public void SetLayer(WorldLayer layer)
        {
#if UNITY_EDITOR
            currentLayer = WorldLayerUtility.IsPlayable(layer) ? layer : WorldLayer.Surface;
            animalPositionCache.Clear();
#else
            // 製品版には開発者用レイヤー操作UIを含めず、地上表示だけを維持します。
            currentLayer = WorldLayer.Surface;
#endif
        }

        /// <summary>
        /// 指定モード、GeneratedTile、岩壁影不透明度、資源、マグマ、動物候補・確定状態からGameビューへ重ねる色を純粋計算します。
        /// PlayModeテストも同じ入口を使い、凡例と実描画の対応が変わっていないことを保証します。
        /// </summary>
        /// <param name="mode">色の意味を決める可視化モードです。</param>
        /// <param name="tile">ECS Dynamic Bufferから読み取った1タイルの生成結果です。</param>
        /// <param name="rockWallShadowOpacity">RockWallモードで使う0〜1の連続影不透明度です。他モードでは参照しません。</param>
        /// <param name="resourceType">Resourcesモードで色分けする競合解決後の資源IDです。</param>
        /// <param name="isMagmaCandidate">Magmaモードで最終マグマ候補を明色表示する場合はtrueです。</param>
        /// <param name="isAnimalCandidate">Animal Populationモードで、いずれかの動物種が利用できる地形ならtrueです。</param>
        /// <param name="hasConfirmedAnimal">Animal Populationモードで、確定済み個体がこのタイルに存在するならtrueです。</param>
        /// <param name="geologySite">支配サイト表示へ使用する診断専用サイト情報です。他モードでは参照しません。</param>
        /// <param name="isGeologySiteBoundary">地質サイト境界表示で、支配サイトが上下左右のいずれかと異なる場合はtrueです。</param>
        /// <returns>IMGUIへ設定するRGBA色です。Noneの場合は完全な透明色です。</returns>
        public static Color GetVisualizationColor(
            WorldGenerationDebugVisualizationMode mode,
            GeneratedTile tile,
            float rockWallShadowOpacity = 0f,
            GeneratedResourceType resourceType = GeneratedResourceType.None,
            bool isMagmaCandidate = false,
            bool isAnimalCandidate = false,
            bool hasConfirmedAnimal = false,
            GeologySiteData geologySite = default,
            bool isGeologySiteBoundary = false)
        {
            switch (mode)
            {
                case WorldGenerationDebugVisualizationMode.Biome:
                    return GetBiomeVisualizationColor(tile.Biome);

                case WorldGenerationDebugVisualizationMode.Soil:
                    // SoilTypeは通常の土地Spriteへ反映しないため、生成分布を検証するときだけ専用色で区別します。
                    if (tile.Soil == SoilType.Fertile)
                    {
                        return new Color32(70, 180, 82, 238);
                    }

                    if (tile.Soil == SoilType.Sandy)
                    {
                        return new Color32(232, 190, 80, 238);
                    }

                    return tile.Soil == SoilType.Normal
                        ? new Color32(150, 130, 105, 238)
                        : new Color32(35, 35, 42, 210);

                case WorldGenerationDebugVisualizationMode.TerrainPlacement:
                    TerrainPlacementResult equipmentResult = TerrainPlacementRules.Evaluate(
                        tile,
                        TerrainPlacementKind.NormalEquipment);
                    if (equipmentResult == TerrainPlacementResult.Allowed)
                    {
                        // 探索・建築向けの診断へ絞り、補助物流であるベルト固有の許可状態は色分類へ含めません。
                        return new Color32(72, 194, 92, 242);
                    }

                    TerrainPlacementResult bridgeResult = TerrainPlacementRules.Evaluate(
                        tile,
                        TerrainPlacementKind.Bridge,
                        hasHeatResistance: false);
                    if (bridgeResult == TerrainPlacementResult.Allowed)
                    {
                        return new Color32(48, 146, 226, 242);
                    }

                    TerrainPlacementResult heatResistantBridgeResult = TerrainPlacementRules.Evaluate(
                        tile,
                        TerrainPlacementKind.Bridge,
                        hasHeatResistance: true);
                    if (heatResistantBridgeResult == TerrainPlacementResult.Allowed)
                    {
                        // 非耐熱橋が不足理由付きで拒否され、耐熱橋だけを許可する地形はマグマ用の別色で表示します。
                        return new Color32(238, 82, 38, 245);
                    }

                    return new Color32(42, 42, 50, 220);

                case WorldGenerationDebugVisualizationMode.Elevation:
                    float elevation = tile.Elevation / (float)ushort.MaxValue;
                    return Color.Lerp(
                        new Color32(18, 22, 34, 238),
                        new Color32(242, 246, 255, 238),
                        elevation);

                case WorldGenerationDebugVisualizationMode.BaseTemperature:
                    float temperature = tile.Temperature / (float)ushort.MaxValue;
                    return Color.Lerp(
                        new Color32(45, 95, 210, 238),
                        new Color32(235, 66, 42, 238),
                        temperature);

                case WorldGenerationDebugVisualizationMode.TerrainTemperature:
                    float terrainTemperature = tile.TerrainTemperature / (float)ushort.MaxValue;
                    return Color.Lerp(
                        new Color32(34, 78, 194, 238),
                        new Color32(246, 78, 40, 238),
                        terrainTemperature);

                case WorldGenerationDebugVisualizationMode.BaseMoisture:
                    float moisture = tile.Moisture / (float)ushort.MaxValue;
                    return Color.Lerp(
                        new Color32(176, 116, 52, 238),
                        new Color32(50, 185, 225, 238),
                        moisture);

                case WorldGenerationDebugVisualizationMode.WoodyVegetationPotential:
                    float woodyVegetation = tile.WoodyVegetationPotential / (float)ushort.MaxValue;
                    return Color.Lerp(
                        new Color32(126, 88, 48, 238),
                        new Color32(42, 174, 76, 238),
                        woodyVegetation);

                case WorldGenerationDebugVisualizationMode.TerrainRuggedness:
                    float terrainRuggedness = tile.TerrainRuggedness / (float)ushort.MaxValue;
                    return Color.Lerp(
                        new Color32(232, 226, 198, 238),
                        new Color32(54, 48, 62, 238),
                        terrainRuggedness);

                case WorldGenerationDebugVisualizationMode.CoastalInfluence:
                    float coastalInfluence = tile.CoastalInfluence / (float)ushort.MaxValue;
                    return Color.Lerp(
                        new Color32(192, 150, 76, 238),
                        new Color32(42, 132, 218, 238),
                        coastalInfluence);

                case WorldGenerationDebugVisualizationMode.VolcanicActivityPotential:
                    float volcanicActivity = tile.VolcanicActivityPotential / (float)ushort.MaxValue;
                    return Color.Lerp(
                        new Color32(42, 40, 48, 238),
                        new Color32(248, 112, 30, 238),
                        volcanicActivity);

                case WorldGenerationDebugVisualizationMode.GeologyClass:
                    return GetGeologyClassColor(tile.GeologyClass);

                case WorldGenerationDebugVisualizationMode.GeologyPermeability:
                    float permeability = tile.GeologyPermeability / (float)ushort.MaxValue;
                    return Color.Lerp(
                        new Color32(104, 72, 42, 238),
                        new Color32(54, 188, 230, 238),
                        permeability);

                case WorldGenerationDebugVisualizationMode.GeologyGroundwaterStorage:
                    float groundwaterStorage = tile.GeologyGroundwaterStorage / (float)ushort.MaxValue;
                    return Color.Lerp(
                        new Color32(72, 62, 76, 238),
                        new Color32(50, 118, 232, 238),
                        groundwaterStorage);

                case WorldGenerationDebugVisualizationMode.GeologyErosionResistance:
                    float erosionResistance = tile.GeologyErosionResistance / (float)ushort.MaxValue;
                    return Color.Lerp(
                        new Color32(214, 174, 106, 238),
                        new Color32(58, 54, 66, 238),
                        erosionResistance);

                case WorldGenerationDebugVisualizationMode.GeologyFlags:
                    bool supportsKarst = (tile.GeologyFlags & GeologyDefinitionFlags.SupportsKarst) != 0;
                    bool supportsActiveVolcanism =
                        (tile.GeologyFlags & GeologyDefinitionFlags.SupportsActiveVolcanism) != 0;
                    if (supportsKarst && supportsActiveVolcanism)
                    {
                        return new Color32(210, 82, 224, 242);
                    }

                    if (supportsKarst)
                    {
                        return new Color32(64, 210, 224, 242);
                    }

                    return supportsActiveVolcanism
                        ? new Color32(246, 102, 34, 242)
                        : new Color32(52, 52, 62, 218);

                case WorldGenerationDebugVisualizationMode.GeologyDominantSite:
                    return GetGeologySiteColor(geologySite);

                case WorldGenerationDebugVisualizationMode.GeologySiteBoundary:
                    return isGeologySiteBoundary
                        ? new Color32(250, 250, 250, 248)
                        : new Color32(28, 28, 34, 180);

                case WorldGenerationDebugVisualizationMode.WaterSalinity:
                    // 通常地形でも淡水と海水を見分けられますが、専用表示では地表色を消して境界判定だけを確認しやすくします。
                    if (tile.WaterSalinity == Farmware.WorldGeneration.WaterSalinity.FreshWater)
                    {
                        return new Color32(64, 196, 230, 238);
                    }

                    if (tile.WaterSalinity == Farmware.WorldGeneration.WaterSalinity.SaltWater)
                    {
                        return new Color32(39, 99, 210, 238);
                    }

                    return new Color32(45, 45, 52, 210);

                case WorldGenerationDebugVisualizationMode.WaterBody:
                    // 淡水内の川と池を別色にし、川ノイズと池ノイズの生成結果が意図どおり分離されているか確認します。
                    if (tile.WaterBodyType == Farmware.WorldGeneration.WaterBodyType.River)
                    {
                        return new Color32(60, 210, 230, 238);
                    }

                    if (tile.WaterBodyType == Farmware.WorldGeneration.WaterBodyType.Pond)
                    {
                        return new Color32(58, 178, 136, 238);
                    }

                    if (tile.WaterBodyType == Farmware.WorldGeneration.WaterBodyType.Sea)
                    {
                        return new Color32(35, 89, 204, 238);
                    }

                    return new Color32(45, 45, 52, 210);

                case WorldGenerationDebugVisualizationMode.RockWall:
                    if (tile.Ground != GroundType.RockWall)
                    {
                        return new Color32(24, 24, 30, 150);
                    }

                    // 通常表示と同じ連続値を使い、橙から黒へ滑らかに変化させて距離場の不自然な段差を確認できるようにします。
                    float normalizedShadow = RockWallAppearanceUtility.MaximumShadowOpacity <= 0f
                        ? 0f
                        : Mathf.Clamp01(
                            rockWallShadowOpacity
                            / RockWallAppearanceUtility.MaximumShadowOpacity);
                    return Color.Lerp(
                        new Color32(232, 174, 76, 242),
                        new Color32(5, 5, 8, 248),
                        normalizedShadow);

                case WorldGenerationDebugVisualizationMode.Magma:
                    if (tile.Ground == GroundType.Magma)
                    {
                        return new Color32(255, 92, 18, 245);
                    }

                    return isMagmaCandidate
                        ? new Color32(136, 38, 30, 226)
                        : new Color32(24, 24, 30, 165);

                case WorldGenerationDebugVisualizationMode.Resources:
                    // 最終Bufferと同じ1種類だけを描き、候補ノイズではなく実際の配置競合結果をGameビューで確認します。
                    if (resourceType == GeneratedResourceType.Tree)
                    {
                        return new Color32(38, 178, 72, 242);
                    }

                    if (resourceType == GeneratedResourceType.Stone)
                    {
                        return new Color32(168, 178, 194, 242);
                    }

                    if (resourceType == GeneratedResourceType.Sand)
                    {
                        return new Color32(239, 181, 65, 242);
                    }

                    return new Color32(24, 24, 30, 165);

                case WorldGenerationDebugVisualizationMode.AnimalPopulation:
                    // 確定位置を候補色より優先し、抽選結果を1タイル単位で見失わないようにします。
                    if (hasConfirmedAnimal)
                    {
                        return new Color32(38, 224, 236, 248);
                    }

                    return isAnimalCandidate
                        ? new Color32(246, 186, 52, 218)
                        : new Color32(20, 20, 26, 172);

                default:
                    return Color.clear;
            }
        }

        /// <summary>
        /// 指定モードが固定生成の水文Bufferを読む診断表示かを返します。
        /// Overlay、Map Viewer、凡例が同じ判定を共有し、水文Bufferを不要な表示で走査しないために使用します。
        /// </summary>
        /// <param name="mode">判定する開発者用可視化モードです。</param>
        /// <returns>C3の基礎水文またはC4の派生水文を表示するモードならtrueです。</returns>
        public static bool IsHydrologyVisualizationMode(
            WorldGenerationDebugVisualizationMode mode)
        {
            return mode >= WorldGenerationDebugVisualizationMode.HydrologyFlowDirection
                && mode <= WorldGenerationDebugVisualizationMode.HydrologyWetlandCandidate;
        }

        /// <summary>
        /// 生成済み水文セルを、流向・終端・集水・表流水・湖の診断色へ変換します。
        /// 固定湖面を作れない打切り流域は紫、固定湖候補と湖面は青系に分け、未完成境界を湖と誤認させません。
        /// </summary>
        /// <param name="mode">水文診断の表示内容です。</param>
        /// <param name="cell">ECS Bufferまたは決定論的Generatorから取得した固定水文セルです。</param>
        /// <param name="hasCell">対象座標の水文セルを正常に取得できた場合はtrueです。</param>
        /// <returns>Gameビューへ重ねる半透明RGBA色です。水文以外のモードは透明色です。</returns>
        public static Color GetHydrologyVisualizationColor(
            WorldGenerationDebugVisualizationMode mode,
            in GeneratedHydrologyCell cell,
            bool hasCell = true)
        {
            if (!hasCell)
            {
                // 隣接チャンクが生成待ちの領域を実際の終端や乾燥流域と混同しないよう、専用の暗い紫で示します。
                return new Color32(54, 34, 62, 210);
            }

            switch (mode)
            {
                case WorldGenerationDebugVisualizationMode.HydrologyFlowDirection:
                    switch (cell.FlowDirection)
                    {
                        case HydrologyFlowDirection.East: return new Color32(242, 92, 72, 238);
                        case HydrologyFlowDirection.NorthEast: return new Color32(244, 154, 62, 238);
                        case HydrologyFlowDirection.North: return new Color32(226, 220, 68, 238);
                        case HydrologyFlowDirection.NorthWest: return new Color32(122, 204, 72, 238);
                        case HydrologyFlowDirection.West: return new Color32(54, 190, 142, 238);
                        case HydrologyFlowDirection.SouthWest: return new Color32(52, 172, 224, 238);
                        case HydrologyFlowDirection.South: return new Color32(80, 106, 230, 238);
                        case HydrologyFlowDirection.SouthEast: return new Color32(176, 86, 222, 238);
                        default: return new Color32(238, 238, 242, 244);
                    }

                case WorldGenerationDebugVisualizationMode.HydrologyTerminal:
                    if (cell.DownstreamTerminal == HydrologyConnectionFlags.Marine)
                    {
                        return new Color32(48, 126, 226, 238);
                    }

                    if (cell.DownstreamTerminal == HydrologyConnectionFlags.ClosedBasin)
                    {
                        return new Color32(232, 154, 54, 238);
                    }

                    return cell.DownstreamTerminal == HydrologyConnectionFlags.LongDrainage
                        ? new Color32(210, 72, 202, 242)
                        : new Color32(42, 42, 50, 210);

                case WorldGenerationDebugVisualizationMode.HydrologyCatchment:
                    if (cell.CatchmentTruncated)
                    {
                        return new Color32(220, 70, 210, 244);
                    }

                    // 1〜4,096件は12段階の2倍差を持つため、対数化して小流域と大流域の両方を識別できるようにします。
                    float normalizedCatchment = cell.CatchmentAreaCells <= 1
                        ? 0f
                        : Mathf.Clamp01(Mathf.Log(cell.CatchmentAreaCells, 2f) / 12f);
                    return Color.Lerp(
                        new Color32(214, 188, 96, 238),
                        new Color32(42, 112, 226, 242),
                        normalizedCatchment);

                case WorldGenerationDebugVisualizationMode.HydrologySurfaceWater:
                    bool isRiver = (cell.ConnectionFlags & HydrologyConnectionFlags.River) != 0;
                    bool isStream = (cell.ConnectionFlags & HydrologyConnectionFlags.Stream) != 0;
                    if (isRiver)
                    {
                        return new Color32(36, 104, 236, 244);
                    }

                    return isStream
                        ? new Color32(54, 202, 224, 240)
                        : new Color32(54, 52, 58, 210);

                case WorldGenerationDebugVisualizationMode.HydrologyLake:
                    if ((cell.LakeFlags & HydrologyLakeFlags.BasinTruncated) != 0)
                    {
                        return new Color32(220, 70, 210, 244);
                    }

                    // C4-Cで確定した区分を最優先し、一時湖を固定水面の青で誤表示せず、塩湖と淡水湖も区別します。
                    // 区分はSurfaceCellだけへ設定されるため、流域内の高い乾燥セルを湖面色へ塗りません。
                    switch (cell.LakeClassification)
                    {
                        case LakeWaterBalanceClassification.Temporary:
                            return new Color32(210, 150, 72, 240);
                        case LakeWaterBalanceClassification.PermanentSaltwater:
                            return new Color32(164, 92, 202, 244);
                        case LakeWaterBalanceClassification.ClosedFreshwater:
                            return new Color32(48, 126, 232, 244);
                        case LakeWaterBalanceClassification.OverflowFreshwater:
                            return new Color32(58, 228, 238, 246);
                    }

                    // C3-Dだけを生成した旧診断入力も、SurfaceCellとOverflowの既存色で読み取れるように維持します。
                    if ((cell.LakeFlags
                            & (HydrologyLakeFlags.SurfaceCell | HydrologyLakeFlags.Overflow))
                        == (HydrologyLakeFlags.SurfaceCell | HydrologyLakeFlags.Overflow))
                    {
                        return new Color32(58, 228, 238, 246);
                    }

                    if ((cell.LakeFlags & HydrologyLakeFlags.SurfaceCell) != 0)
                    {
                        return new Color32(48, 126, 232, 244);
                    }

                    if ((cell.LakeFlags & HydrologyLakeFlags.FixedLake) != 0)
                    {
                        return new Color32(72, 88, 190, 240);
                    }

                    return (cell.LakeFlags & HydrologyLakeFlags.BasinResolved) != 0
                        ? new Color32(178, 132, 70, 232)
                        : new Color32(42, 42, 50, 210);

                default:
                    return Color.clear;
            }
        }

        /// <summary>
        /// 生成済みの水文セル、固定表流水、自然水文タイルを、C4派生水文の診断色へ変換します。
        /// 降雨、灌水、排水などの現在状態は引数に取らず、固定生成値と現在水文を混在させません。
        /// </summary>
        /// <param name="mode">地下水、自然飽和、泉、自然冠水、湿原候補のいずれかです。</param>
        /// <param name="cell">地下水ポテンシャルを持つ固定水文セルです。</param>
        /// <param name="hydrologyTile">自然飽和、自然冠水、湿原フラグを持つ固定タイルです。</param>
        /// <param name="surfaceWaterTile">最終確定した泉流を持つ固定表流水タイルです。</param>
        /// <param name="hasCell">対象タイルを担当する生成済み水文セルが存在する場合はtrueです。</param>
        /// <param name="hasTile">対象チャンクの2つのタイルBufferが完成している場合はtrueです。</param>
        /// <returns>Gameビューへ重ねる半透明RGBA色です。対象外モードは既存水文色または透明色を返します。</returns>
        public static Color GetHydrologyVisualizationColor(
            WorldGenerationDebugVisualizationMode mode,
            in GeneratedHydrologyCell cell,
            in GeneratedHydrologyTile hydrologyTile,
            in GeneratedSurfaceWaterTile surfaceWaterTile,
            bool hasCell = true,
            bool hasTile = true)
        {
            if (mode <= WorldGenerationDebugVisualizationMode.HydrologyLake)
            {
                // C3由来の5モードは従来と同じ水文セルだけを正とし、C4 Buffer追加で色を変えません。
                return GetHydrologyVisualizationColor(mode, cell, hasCell);
            }

            if (!hasTile
                || (mode == WorldGenerationDebugVisualizationMode.HydrologyGroundwater && !hasCell))
            {
                // 生成待ちを値0と誤認しないよう、既存水文診断と同じ専用色を使用します。
                return new Color32(54, 34, 62, 210);
            }

            switch (mode)
            {
                case WorldGenerationDebugVisualizationMode.HydrologyGroundwater:
                    bool isGroundwaterConnected = (cell.ConnectionFlags
                        & HydrologyConnectionFlags.Groundwater) != 0;
                    if (!isGroundwaterConnected)
                    {
                        float disconnectedValue = cell.GroundwaterPotential / 32767f;
                        return Color.Lerp(
                            new Color32(72, 50, 38, 220),
                            new Color32(196, 164, 76, 236),
                            Mathf.Clamp01(disconnectedValue));
                    }

                    float connectedValue = (cell.GroundwaterPotential - 32768) / 32767f;
                    return Color.Lerp(
                        new Color32(42, 158, 170, 238),
                        new Color32(78, 238, 246, 246),
                        Mathf.Clamp01(connectedValue));

                case WorldGenerationDebugVisualizationMode.HydrologyNaturalSoilSaturation:
                    return Color.Lerp(
                        new Color32(112, 70, 38, 224),
                        new Color32(72, 206, 132, 244),
                        hydrologyTile.NaturalSoilSaturationPotential / (float)ushort.MaxValue);

                case WorldGenerationDebugVisualizationMode.HydrologySpring:
                    return surfaceWaterTile.Type == SurfaceWaterTileType.SpringStream
                        ? new Color32(62, 236, 246, 248)
                        : new Color32(46, 46, 54, 210);

                case WorldGenerationDebugVisualizationMode.HydrologyNaturalFlooding:
                    return Color.Lerp(
                        new Color32(74, 66, 58, 220),
                        new Color32(52, 118, 238, 246),
                        hydrologyTile.NaturalFloodingPotential / (float)ushort.MaxValue);

                case WorldGenerationDebugVisualizationMode.HydrologyWetlandCandidate:
                    if ((hydrologyTile.Flags & GeneratedHydrologyTileFlags.LargeWetlandExtent) != 0)
                    {
                        return new Color32(62, 212, 104, 246);
                    }

                    return (hydrologyTile.Flags & GeneratedHydrologyTileFlags.WetlandTileEligible) != 0
                        ? new Color32(226, 196, 68, 242)
                        : new Color32(54, 50, 46, 210);

                default:
                    return Color.clear;
            }
        }

        /// <summary>
        /// 地質の安定分類を、GameビューとMap Viewerで共有する識別色へ変換します。
        /// 色は観察専用であり、分類抽選やゲーム判定には使用しません。
        /// </summary>
        /// <param name="geologyClass">色へ変換する卓越基盤地質です。</param>
        /// <returns>8分類を見分ける半透明RGBA色です。未設定値は暗色になります。</returns>
        public static Color GetGeologyClassColor(GeologyClass geologyClass)
        {
            switch (geologyClass)
            {
                case GeologyClass.UnconsolidatedSediment: return new Color32(214, 188, 126, 238);
                case GeologyClass.Sandstone: return new Color32(196, 126, 74, 238);
                case GeologyClass.Limestone: return new Color32(218, 216, 196, 238);
                case GeologyClass.MudstoneSlate: return new Color32(92, 92, 112, 238);
                case GeologyClass.GraniticRock: return new Color32(192, 142, 154, 238);
                case GeologyClass.MetamorphicRock: return new Color32(112, 86, 142, 238);
                case GeologyClass.VolcanicRock: return new Color32(126, 62, 50, 238);
                case GeologyClass.UltramaficRock: return new Color32(62, 112, 78, 238);
                default: return new Color32(30, 30, 36, 210);
            }
        }

        /// <summary>
        /// 支配地質サイトのマクロセル座標を安定した疑似色へ変換します。
        /// 同じサイトは地質分類に関係なく同じ色になり、サイト境界を目視できます。
        /// </summary>
        /// <param name="site">DeterministicGeologyGeneratorが返した支配サイトです。</param>
        /// <returns>サイト座標だけで決まる半透明RGBA色です。</returns>
        public static Color GetGeologySiteColor(in GeologySiteData site)
        {
            unchecked
            {
                // int座標を符号なしへ写してから混ぜ、負座標でも正座標と同じ決定論的な色分布を得ます。
                uint hash = (uint)site.MacroCellX * 0x9E3779B9u;
                hash ^= (uint)site.MacroCellY * 0x85EBCA6Bu;
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;

                // 暗過ぎる色を避けるため各成分を72〜231へ収め、隣接サイトを識別しやすくします。
                byte red = (byte)(72 + (hash & 0x9Fu));
                byte green = (byte)(72 + ((hash >> 8) & 0x9Fu));
                byte blue = (byte)(72 + ((hash >> 16) & 0x9Fu));
                return new Color32(red, green, blue, 238);
            }
        }

        /// <summary>2つの診断用サイトが同じ地質マクロセルに属するかを返します。</summary>
        /// <param name="first">1つ目の支配サイトです。</param>
        /// <param name="second">2つ目の支配サイトです。</param>
        /// <returns>マクロセルX/Yがともに一致する場合はtrueです。</returns>
        public static bool IsSameGeologySite(in GeologySiteData first, in GeologySiteData second)
        {
            return first.MacroCellX == second.MacroCellX
                && first.MacroCellY == second.MacroCellY;
        }

        /// <summary>
        /// Default World向けの読み取りQueryを用意し、World再作成時には古いQueryを破棄して作り直します。
        /// </summary>
        /// <returns>現在のPlayMode Worldからチャンクデータを読める場合はtrueです。</returns>
        private bool TryEnsureQueries()
        {
            World currentWorld = World.DefaultGameObjectInjectionWorld;
            if (currentWorld == null || !currentWorld.IsCreated)
            {
                return false;
            }

            if (queriesAreCreated && presentationWorld == currentWorld)
            {
                return true;
            }

            DisposeQueries();
            presentationWorld = currentWorld;
            EntityManager entityManager = currentWorld.EntityManager;
            chunkQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ChunkCoordinateComponent>(),
                ComponentType.ReadOnly<ChunkGenerationStatus>(),
                ComponentType.ReadOnly<GeneratedTile>(),
                ComponentType.ReadOnly<GeneratedResourceTile>(),
                ComponentType.ReadOnly<GeneratedHydrologyCell>(),
                ComponentType.ReadOnly<GeneratedSurfaceWaterTile>(),
                ComponentType.ReadOnly<GeneratedHydrologyTile>());
            settingsQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldGenerationSettings>());
            resourceSettingsQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ResourceGenerationSettings>());
            animalCatalogQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AnimalSpeciesCatalog>());
            animalPositionQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<WildAnimal>(),
                ComponentType.ReadOnly<AnimalTilePosition>());
            queriesAreCreated = true;
            return true;
        }

        /// <summary>
        /// ECSの可視チャンクを走査し、Gameビューへ含まれるセルだけを着色した後、中央タイルの数値を表示します。
        /// </summary>
        private void DrawVisibleTilesAndInformation()
        {
            VisibleTileBounds bounds = CalculateVisibleTileBounds();
            long tileCount = (long)(bounds.MaximumTileX - bounds.MinimumTileX + 1)
                * (bounds.MaximumTileY - bounds.MinimumTileY + 1);
            if (tileCount <= 0 || tileCount > MaximumDrawableTileCount)
            {
                DrawLimitWarning(tileCount);
                return;
            }

            bool hasCenterCoordinate = TryGetAbsoluteCameraCenterTile(
                out int2 centerAbsoluteTileCoordinate);
            Vector3Int centerTileCoordinate = hasCenterCoordinate
                ? new Vector3Int(
                    centerAbsoluteTileCoordinate.x,
                    centerAbsoluteTileCoordinate.y,
                    0)
                : default;
            bool hasCenterTile = false;
            GeneratedTile centerTile = default;
            GeneratedResourceTile centerResource = default;
            bool hasCenterHydrologyCell = false;
            GeneratedHydrologyCell centerHydrologyCell = default;
            bool hasCenterHydrologyTile = false;
            GeneratedHydrologyTile centerHydrologyTile = default;
            GeneratedSurfaceWaterTile centerSurfaceWaterTile = default;
            RockWallAppearance centerRockWallAppearance = RockWallAppearance.None;
            float centerRockWallShadowOpacity = 0f;
            EntityManager entityManager = presentationWorld.EntityManager;
            bool hasGenerationSettings = settingsQuery.CalculateEntityCount() == 1;
            WorldGenerationSettings settings = hasGenerationSettings
                ? settingsQuery.GetSingleton<WorldGenerationSettings>()
                : default;
            bool hasResourceSettings = resourceSettingsQuery.CalculateEntityCount() == 1;
            ResourceGenerationSettings resourceSettings = hasResourceSettings
                ? resourceSettingsQuery.GetSingleton<ResourceGenerationSettings>()
                : default;
            BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog = default;
            bool hasAnimalCatalog = currentMode
                    == WorldGenerationDebugVisualizationMode.AnimalPopulation
                && animalCatalogQuery.CalculateEntityCount() == 1;
            if (hasAnimalCatalog)
            {
                animalCatalog = animalCatalogQuery.GetSingleton<AnimalSpeciesCatalog>().Value;
                hasAnimalCatalog = animalCatalog.IsCreated;
            }

            CacheAnimalPositionsForCurrentRepaint(bounds);

            using NativeArray<Entity> entities = chunkQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<ChunkCoordinateComponent> coordinates =
                chunkQuery.ToComponentDataArray<ChunkCoordinateComponent>(Allocator.Temp);
            using NativeArray<ChunkGenerationStatus> statuses =
                chunkQuery.ToComponentDataArray<ChunkGenerationStatus>(Allocator.Temp);

            CacheCompletedTerrainChunksForCurrentRepaint(
                entities,
                coordinates,
                statuses,
                entityManager);

            // 水文セルの担当範囲は中心から-4〜+3タイルなので、地形タイルと同じチャンクのBufferだけでは
            // 正端4タイルを引けません。水文モード中だけ全ロード済みBufferを座標索引へまとめます。
            CacheHydrologyCellsForCurrentRepaint(entities, coordinates, statuses, entityManager);

            Color previousGuiColor = GUI.color;
            for (int entityIndex = 0; entityIndex < entities.Length; entityIndex++)
            {
                ChunkGenerationStatus status = statuses[entityIndex];
                if (status.Phase != ChunkGenerationPhase.ReadyToApply
                    && status.Phase != ChunkGenerationPhase.Active)
                {
                    continue;
                }

                ChunkCoordinateComponent coordinate = coordinates[entityIndex];
                if (coordinate.Layer != currentLayer)
                {
                    continue;
                }

                int chunkMinimumX = coordinate.X * WorldGridConstants.LogicalChunkSize;
                int chunkMinimumY = coordinate.Y * WorldGridConstants.LogicalChunkSize;
                int minimumTileX = Mathf.Max(bounds.MinimumTileX, chunkMinimumX);
                int maximumTileX = Mathf.Min(
                    bounds.MaximumTileX,
                    chunkMinimumX + WorldGridConstants.LogicalChunkSize - 1);
                int minimumTileY = Mathf.Max(bounds.MinimumTileY, chunkMinimumY);
                int maximumTileY = Mathf.Min(
                    bounds.MaximumTileY,
                    chunkMinimumY + WorldGridConstants.LogicalChunkSize - 1);
                if (minimumTileX > maximumTileX || minimumTileY > maximumTileY)
                {
                    continue;
                }

                DynamicBuffer<GeneratedTile> tiles =
                    entityManager.GetBuffer<GeneratedTile>(entities[entityIndex], true);
                DynamicBuffer<GeneratedResourceTile> resources =
                    entityManager.GetBuffer<GeneratedResourceTile>(entities[entityIndex], true);
                DynamicBuffer<GeneratedSurfaceWaterTile> surfaceWaterTiles =
                    entityManager.GetBuffer<GeneratedSurfaceWaterTile>(entities[entityIndex], true);
                DynamicBuffer<GeneratedHydrologyTile> hydrologyTiles =
                    entityManager.GetBuffer<GeneratedHydrologyTile>(entities[entityIndex], true);
                if (tiles.Length != WorldGridConstants.TilesPerChunk
                    || resources.Length != WorldGridConstants.TilesPerChunk
                    || surfaceWaterTiles.Length != WorldGridConstants.TilesPerChunk
                    || hydrologyTiles.Length != WorldGridConstants.TilesPerChunk)
                {
                    continue;
                }

                bool hasTerrainNeighborhood =
                    CompletedChunkTerrainNeighborhoodFactory.TryCreate(
                        entityManager,
                        completedTerrainChunkEntities,
                        new ChunkCoordinate(coordinate.Layer, coordinate.X, coordinate.Y),
                        out CompletedChunkTerrainNeighborhood terrainNeighborhood);

                int minimumLocalX = minimumTileX - chunkMinimumX;
                int maximumLocalX = maximumTileX - chunkMinimumX;
                int minimumLocalY = minimumTileY - chunkMinimumY;
                int maximumLocalY = maximumTileY - chunkMinimumY;

                // 0〜63のチャンク内座標をループし、画面外タイルのIMGUI発行とint最大タイルでの加算周回を同時に避けます。
                for (int localY = minimumLocalY; localY <= maximumLocalY; localY++)
                {
                    int tileY = chunkMinimumY + localY;
                    for (int localX = minimumLocalX; localX <= maximumLocalX; localX++)
                    {
                        int tileX = chunkMinimumX + localX;
                        int tileIndex = (localY * WorldGridConstants.LogicalChunkSize) + localX;
                        GeneratedTile tile = tiles[tileIndex];
                        GeneratedResourceTile resource = resources[tileIndex];
                        GeneratedSurfaceWaterTile surfaceWaterTile = surfaceWaterTiles[tileIndex];
                        GeneratedHydrologyTile hydrologyTile = hydrologyTiles[tileIndex];
                        float rockWallShadowOpacity =
                            currentMode == WorldGenerationDebugVisualizationMode.RockWall
                            && hasTerrainNeighborhood
                                ? RockWallAppearanceUtility.CalculateShadowOpacityAt(
                                    terrainNeighborhood,
                                    localX,
                                    localY)
                                : 0f;
                        bool isMagmaCandidate = false;
                        if (currentMode == WorldGenerationDebugVisualizationMode.Magma
                            && hasGenerationSettings)
                        {
                            // C5-C以降は旧岩地Profileや淡水抑制域を使わず、新火山Biomeと連続火山活動度だけを背景候補として表示します。
                            isMagmaCandidate = tile.Biome == BiomeId.VolcanicGeothermal
                                && tile.VolcanicActivityPotential
                                    >= settings.MagmaMinimumVolcanicInfluence;
                        }
                        bool isAnimalCandidate = hasAnimalCatalog
                            && IsEligibleForAnyAnimalSpecies(tile, resource, animalCatalog);
                        bool hasConfirmedAnimal = currentMode
                                == WorldGenerationDebugVisualizationMode.AnimalPopulation
                            && animalPositionCache.Contains(currentLayer, tileX, tileY);
                        GeologySiteData geologySite = default;
                        bool isGeologySiteBoundary = false;
                        if (hasGenerationSettings
                            && (currentMode == WorldGenerationDebugVisualizationMode.GeologyDominantSite
                                || currentMode == WorldGenerationDebugVisualizationMode.GeologySiteBoundary))
                        {
                            // サイト情報はECSへ常駐させず、選択中の診断モードだけで固定生成器から再計算します。
                            // 境界モードは1タイル先の4近傍を比較し、実ゲーム座標でサイトが切り替わるセルを示します。
                            int boundaryProbeDistance = currentMode
                                    == WorldGenerationDebugVisualizationMode.GeologySiteBoundary
                                ? 1
                                : 0;
                            TryGetGeologySiteVisualizationData(
                                settings,
                                tileX,
                                tileY,
                                boundaryProbeDistance,
                                out geologySite,
                                out isGeologySiteBoundary);
                        }
                        GeneratedHydrologyCell hydrologyCell = default;
                        bool hasHydrologyCell = hasGenerationSettings
                            && TryGetCachedHydrologyCellAtTile(
                                settings.Hydrology,
                                tileX,
                                tileY,
                                out hydrologyCell);
                        GUI.color = IsHydrologyVisualizationMode(currentMode)
                            ? GetHydrologyVisualizationColor(
                                currentMode,
                                hydrologyCell,
                                hydrologyTile,
                                surfaceWaterTile,
                                hasHydrologyCell)
                            : GetVisualizationColor(
                                currentMode,
                                tile,
                                rockWallShadowOpacity,
                                resource.Type,
                                isMagmaCandidate,
                                isAnimalCandidate,
                                hasConfirmedAnimal,
                                geologySite,
                                isGeologySiteBoundary);
                        GUI.DrawTexture(CalculateTileGuiRect(tileX, tileY), Texture2D.whiteTexture);

                        if (hasCenterCoordinate
                            && tileX == centerTileCoordinate.x
                            && tileY == centerTileCoordinate.y)
                        {
                            hasCenterTile = true;
                            centerTile = tile;
                            centerResource = resource;
                            hasCenterHydrologyCell = hasHydrologyCell;
                            centerHydrologyCell = hydrologyCell;
                            hasCenterHydrologyTile = true;
                            centerHydrologyTile = hydrologyTile;
                            centerSurfaceWaterTile = surfaceWaterTile;
                            centerRockWallAppearance = hasTerrainNeighborhood
                                ? RockWallAppearanceUtility.ClassifyAt(
                                    terrainNeighborhood,
                                    localX,
                                    localY)
                                : RockWallAppearance.None;
                            // Rock Wall以外のモードでも中央情報欄は表示されるため、中央1セルだけは完成近傍から影を求めます。
                            centerRockWallShadowOpacity = hasTerrainNeighborhood
                                ? currentMode == WorldGenerationDebugVisualizationMode.RockWall
                                    ? rockWallShadowOpacity
                                    : RockWallAppearanceUtility.CalculateShadowOpacityAt(
                                        terrainNeighborhood,
                                        localX,
                                        localY)
                                : 0f;
                        }
                    }
                }
            }

            GUI.color = previousGuiColor;
            DrawInformationPanel(
                hasCenterTile,
                centerTileCoordinate,
                centerTile,
                centerResource,
                hasCenterHydrologyCell,
                centerHydrologyCell,
                hasCenterHydrologyTile,
                centerHydrologyTile,
                centerSurfaceWaterTile,
                centerRockWallAppearance,
                centerRockWallShadowOpacity,
                settings,
                resourceSettings);
        }

        /// <summary>
        /// Rock Wall表示と中央情報欄が完成地形だけを参照できるよう、現在ロード済みの完成チャンクを座標索引へまとめます。
        /// </summary>
        /// <param name="entities">Chunk Queryから同じ順序で取得したEntity配列です。</param>
        /// <param name="coordinates">各Entityと同じ添字のチャンク座標です。</param>
        /// <param name="statuses">各Entityと同じ添字の生成状態です。</param>
        /// <param name="entityManager">完成地形Bufferの長さを検証する現在WorldのEntityManagerです。</param>
        private void CacheCompletedTerrainChunksForCurrentRepaint(
            NativeArray<Entity> entities,
            NativeArray<ChunkCoordinateComponent> coordinates,
            NativeArray<ChunkGenerationStatus> statuses,
            EntityManager entityManager)
        {
            completedTerrainChunkEntities.Clear();
            for (int entityIndex = 0; entityIndex < entities.Length; entityIndex++)
            {
                ChunkGenerationStatus status = statuses[entityIndex];
                bool hasCompleteData = status.Phase == ChunkGenerationPhase.ReadyToApply
                    || status.Phase == ChunkGenerationPhase.Active
                    || status.Phase == ChunkGenerationPhase.Inactive;
                if (!hasCompleteData || coordinates[entityIndex].Layer != currentLayer)
                {
                    continue;
                }

                DynamicBuffer<GeneratedTile> tiles =
                    entityManager.GetBuffer<GeneratedTile>(entities[entityIndex], true);
                if (tiles.Length != WorldGridConstants.TilesPerChunk)
                {
                    continue;
                }

                var coordinate = new ChunkCoordinate(
                    coordinates[entityIndex].Layer,
                    coordinates[entityIndex].X,
                    coordinates[entityIndex].Y);
                completedTerrainChunkEntities[coordinate] = entities[entityIndex];
            }
        }

        /// <summary>
        /// 水文モードのRepaint開始時に、生成済みチャンクの64件Bufferを絶対水文セル座標で引ける索引へまとめます。
        /// 他モードでは空のままにし、通常の地形診断へ水文Buffer走査を追加しません。
        /// </summary>
        /// <param name="entities">Chunk Queryから同じ順序で取得したEntity配列です。</param>
        /// <param name="coordinates">各Entityと同じ添字のチャンク座標とWorldLayerです。</param>
        /// <param name="statuses">各Entityと同じ添字の生成状態です。</param>
        /// <param name="entityManager">読み取り専用Bufferを取得する現在のWorldのEntityManagerです。</param>
        private void CacheHydrologyCellsForCurrentRepaint(
            NativeArray<Entity> entities,
            NativeArray<ChunkCoordinateComponent> coordinates,
            NativeArray<ChunkGenerationStatus> statuses,
            EntityManager entityManager)
        {
            hydrologyCellCache.Clear();
            if (!IsHydrologyVisualizationMode(currentMode))
            {
                return;
            }

            for (int entityIndex = 0; entityIndex < entities.Length; entityIndex++)
            {
                ChunkGenerationStatus status = statuses[entityIndex];
                if (status.Phase != ChunkGenerationPhase.ReadyToApply
                    && status.Phase != ChunkGenerationPhase.Active)
                {
                    continue;
                }

                // 水文セル座標だけではWorldLayerを区別できないため、現在表示中の層だけを索引化します。
                // 将来ほかの層にも水文Bufferを持たせた際、同じXY座標で地上の診断値を上書きしないためです。
                if (coordinates[entityIndex].Layer != currentLayer)
                {
                    continue;
                }

                DynamicBuffer<GeneratedHydrologyCell> cells =
                    entityManager.GetBuffer<GeneratedHydrologyCell>(entities[entityIndex], true);
                if (cells.Length != DeterministicHydrologyGenerator.CellsPerChunk)
                {
                    continue;
                }

                for (int cellIndex = 0; cellIndex < cells.Length; cellIndex++)
                {
                    GeneratedHydrologyCell cell = cells[cellIndex];
                    hydrologyCellCache[CreateHydrologyCellKey(cell.CellX, cell.CellY)] = cell;
                }
            }
        }

        /// <summary>絶対タイル座標を水文セルへ変換し、現在Repaintの生成済みBuffer索引から固定結果を取得します。</summary>
        /// <param name="settings">8×8格子を定義する固定水文設定です。</param>
        /// <param name="tileX">検索する絶対タイルX座標です。</param>
        /// <param name="tileY">検索する絶対タイルY座標です。</param>
        /// <param name="cell">成功時に対応する生成済み水文セルを返します。</param>
        /// <returns>座標変換とBuffer索引の両方が成功した場合はtrueです。</returns>
        private bool TryGetCachedHydrologyCellAtTile(
            in HydrologyGenerationSettings settings,
            int tileX,
            int tileY,
            out GeneratedHydrologyCell cell)
        {
            if (!IsHydrologyVisualizationMode(currentMode)
                || !DeterministicHydrologyGenerator.TryGetCellCoordinate(
                    settings,
                    tileX,
                    tileY,
                    out int cellX,
                    out int cellY))
            {
                cell = default;
                return false;
            }

            return hydrologyCellCache.TryGetValue(
                CreateHydrologyCellKey(cellX, cellY),
                out cell);
        }

        /// <summary>符号付き水文セルX/Yを衝突しない64bit辞書キーへ詰めます。</summary>
        /// <param name="cellX">水平水文セル座標です。</param>
        /// <param name="cellY">垂直水文セル座標です。</param>
        /// <returns>Xを上位32bit、Yを下位32bitへ保持する可逆キーです。</returns>
        private static long CreateHydrologyCellKey(int cellX, int cellY)
        {
            return ((long)cellX << 32) | (uint)cellY;
        }

        /// <summary>
        /// 指定タイルの支配地質サイトを取得し、必要なら上下左右の指定距離でサイト境界を判定します。
        /// GameビューとMap Viewerが同じサイト同一性とint座標端の扱いを共有するための診断専用APIです。
        /// </summary>
        /// <param name="settings">対象ワールドと同じ生成設定です。</param>
        /// <param name="tileX">調べる水平ワールドタイル座標です。</param>
        /// <param name="tileY">調べる垂直ワールドタイル座標です。</param>
        /// <param name="boundaryProbeDistanceInTiles">0なら境界を調べず、正数なら上下左右へ離すタイル数です。</param>
        /// <param name="site">成功時に対象タイルを支配する地質サイトを返します。</param>
        /// <param name="isBoundary">成功時に、生成できた4近傍のいずれかが別サイトならtrueを返します。</param>
        /// <returns>対象タイルの地質サイトを安全に生成できた場合はtrueです。</returns>
        public static bool TryGetGeologySiteVisualizationData(
            in WorldGenerationSettings settings,
            int tileX,
            int tileY,
            int boundaryProbeDistanceInTiles,
            out GeologySiteData site,
            out bool isBoundary)
        {
            site = default;
            isBoundary = false;
            if (boundaryProbeDistanceInTiles < 0
                || !DeterministicGeologyGenerator.TryGenerate(
                    settings,
                    tileX,
                    tileY,
                    out GeneratedGeologyData centerGeology))
            {
                return false;
            }

            site = centerGeology.Site;
            if (boundaryProbeDistanceInTiles == 0)
            {
                return true;
            }

            // 加算前にlongへ広げ、int端で符号反転した座標を別サイトとして誤判定しないようにします。
            long distance = boundaryProbeDistanceInTiles;
            isBoundary = IsDifferentGeneratedGeologySite(
                    settings,
                    (long)tileX - distance,
                    tileY,
                    site)
                || IsDifferentGeneratedGeologySite(
                    settings,
                    (long)tileX + distance,
                    tileY,
                    site)
                || IsDifferentGeneratedGeologySite(
                    settings,
                    tileX,
                    (long)tileY - distance,
                    site)
                || IsDifferentGeneratedGeologySite(
                    settings,
                    tileX,
                    (long)tileY + distance,
                    site);
            return true;
        }

        /// <summary>int範囲内の近傍を生成し、対象サイトと異なる場合だけtrueを返します。</summary>
        /// <param name="settings">対象ワールドと同じ生成設定です。</param>
        /// <param name="tileX">longで安全に計算した近傍X座標です。</param>
        /// <param name="tileY">longで安全に計算した近傍Y座標です。</param>
        /// <param name="centerSite">比較元となる対象タイルの支配サイトです。</param>
        /// <returns>近傍を生成でき、その支配サイトが比較元と異なる場合はtrueです。</returns>
        private static bool IsDifferentGeneratedGeologySite(
            in WorldGenerationSettings settings,
            long tileX,
            long tileY,
            in GeologySiteData centerSite)
        {
            if (tileX < int.MinValue
                || tileX > int.MaxValue
                || tileY < int.MinValue
                || tileY > int.MaxValue
                || !DeterministicGeologyGenerator.TryGenerate(
                    settings,
                    (int)tileX,
                    (int)tileY,
                    out GeneratedGeologyData neighborGeology))
            {
                return false;
            }

            return !IsSameGeologySite(centerSite, neighborGeology.Site);
        }

        /// <summary>
        /// Animal Populationモードの可視位置だけを更新します。画面外Chunkは変更時の範囲更新だけで済ませます。
        /// </summary>
        /// <param name="bounds">8192タイル上限の検査を済ませた絶対座標の包含矩形です。</param>
        private void CacheAnimalPositionsForCurrentRepaint(VisibleTileBounds bounds)
        {
            if (currentMode != WorldGenerationDebugVisualizationMode.AnimalPopulation)
            {
                animalPositionCache.Clear();
                return;
            }
            animalPositionCache.Update(presentationWorld.EntityManager, animalPositionQuery, currentLayer,
                bounds.MinimumTileX, bounds.MinimumTileY, bounds.MaximumTileX, bounds.MaximumTileY);
        }

        /// <summary>
        /// 1タイルが資源に予約されておらず、いずれかの動物種のBiome・Ground条件を満たすか確認します。
        /// </summary>
        /// <param name="tile">生成済みの地形分類です。</param>
        /// <param name="resource">同じタイルの資源予約です。動物より優先するため非空セルを候補表示しません。</param>
        /// <param name="catalog">Bakerが検証した動物種定義Blobです。</param>
        /// <returns>少なくとも1種の配置候補ならtrueです。</returns>
        private static bool IsEligibleForAnyAnimalSpecies(
            GeneratedTile tile,
            GeneratedResourceTile resource,
            BlobAssetReference<AnimalSpeciesCatalogBlob> catalog)
        {
            ref AnimalSpeciesCatalogBlob root = ref catalog.Value;
            for (int speciesIndex = 0; speciesIndex < root.Species.Length; speciesIndex++)
            {
                if (AnimalPopulationGenerator.IsSpawnTileEligible(tile, resource, root.Species[speciesIndex]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 指定タイルの4辺をCamera Viewportへ投影し、Windowbox左上原点のIMGUI矩形へ変換します。
        /// </summary>
        /// <param name="tileX">描画する水平ワールドタイル座標です。</param>
        /// <param name="tileY">描画する垂直ワールドタイル座標です。</param>
        /// <returns>1タイルを覆う正の幅と高さを持つIMGUI矩形です。</returns>
        private Rect CalculateTileGuiRect(int tileX, int tileY)
        {
            if (!TryGetDisplayCellForAbsoluteTile(
                    new int2(tileX, tileY),
                    out Vector3Int displayCell))
            {
                return Rect.zero;
            }

            Vector3 bottomLeftWorld = DisplayCellBoundaryToWorld(displayCell.x, displayCell.y);
            Vector3 topRightWorld = DisplayCellBoundaryToWorld(
                (long)displayCell.x + 1L,
                (long)displayCell.y + 1L);
            Vector3 bottomLeftViewport = targetCamera.WorldToViewportPoint(bottomLeftWorld);
            Vector3 topRightViewport = targetCamera.WorldToViewportPoint(topRightWorld);
            float left = Mathf.Min(bottomLeftViewport.x, topRightViewport.x) * activeGuiViewportRect.width;
            float right = Mathf.Max(bottomLeftViewport.x, topRightViewport.x) * activeGuiViewportRect.width;
            float top = (1f - Mathf.Max(bottomLeftViewport.y, topRightViewport.y))
                * activeGuiViewportRect.height;
            float bottom = (1f - Mathf.Min(bottomLeftViewport.y, topRightViewport.y))
                * activeGuiViewportRect.height;
            return Rect.MinMaxRect(left, top, right, bottom);
        }

        /// <summary>
        /// 現在のCamera Viewportへ少しでも含まれる、整数タイル座標の包含矩形を求めます。
        /// </summary>
        /// <returns>負座標でも床方向へ丸められた可視タイル範囲です。</returns>
        private VisibleTileBounds CalculateVisibleTileBounds()
        {
            float distanceToGridPlane = targetGrid == null
                ? Mathf.Abs(targetCamera.transform.position.z)
                : Mathf.Abs(targetCamera.transform.position.z - targetGrid.transform.position.z);
            Vector3 bottomLeftWorld = targetCamera.ViewportToWorldPoint(
                new Vector3(0f, 0f, distanceToGridPlane));
            Vector3 topRightWorld = targetCamera.ViewportToWorldPoint(
                new Vector3(1f, 1f, distanceToGridPlane));
            DisplayOrigin displayOrigin = GetCurrentDisplayOrigin();

            if (targetGrid != null)
            {
                Vector3Int bottomLeftCell = targetGrid.WorldToCell(bottomLeftWorld);
                Vector3Int topRightCell = targetGrid.WorldToCell(topRightWorld);
                return new VisibleTileBounds(
                    AddDisplayOriginSaturated(
                        Mathf.Min(bottomLeftCell.x, topRightCell.x),
                        displayOrigin.AbsoluteTileCoordinate.x),
                    AddDisplayOriginSaturated(
                        Mathf.Max(bottomLeftCell.x, topRightCell.x),
                        displayOrigin.AbsoluteTileCoordinate.x),
                    AddDisplayOriginSaturated(
                        Mathf.Min(bottomLeftCell.y, topRightCell.y),
                        displayOrigin.AbsoluteTileCoordinate.y),
                    AddDisplayOriginSaturated(
                        Mathf.Max(bottomLeftCell.y, topRightCell.y),
                        displayOrigin.AbsoluteTileCoordinate.y));
            }

            return new VisibleTileBounds(
                AddDisplayOriginSaturated(
                    Mathf.FloorToInt(Mathf.Min(bottomLeftWorld.x, topRightWorld.x)),
                    displayOrigin.AbsoluteTileCoordinate.x),
                AddDisplayOriginSaturated(
                    Mathf.CeilToInt(Mathf.Max(bottomLeftWorld.x, topRightWorld.x)) - 1,
                    displayOrigin.AbsoluteTileCoordinate.x),
                AddDisplayOriginSaturated(
                    Mathf.FloorToInt(Mathf.Min(bottomLeftWorld.y, topRightWorld.y)),
                    displayOrigin.AbsoluteTileCoordinate.y),
                AddDisplayOriginSaturated(
                    Mathf.CeilToInt(Mathf.Max(bottomLeftWorld.y, topRightWorld.y)) - 1,
                    displayOrigin.AbsoluteTileCoordinate.y));
        }

        /// <summary>
        /// 表示原点基準の相対セル境界を、Gridの原点・Transform・セル寸法を反映したワールド座標へ変換します。
        /// </summary>
        /// <param name="displayCellX">現在の表示原点から数えた水平セル境界座標です。</param>
        /// <param name="displayCellY">現在の表示原点から数えた垂直セル境界座標です。</param>
        /// <returns>通常地形と同じ平面上のワールド座標です。</returns>
        private Vector3 DisplayCellBoundaryToWorld(long displayCellX, long displayCellY)
        {
            return targetGrid == null
                ? new Vector3(displayCellX, displayCellY, 0f)
                : targetGrid.CellToWorld(
                    new Vector3Int(
                        SaturateToInt(displayCellX),
                        SaturateToInt(displayCellY),
                        0));
        }

        /// <summary>
        /// CameraやTilemapと共有する現在の表示原点を返します。
        /// 古いSceneを一時的に開いた場合だけ絶対原点へフォールバックし、構築ツールの検証では未接続を検出します。
        /// </summary>
        /// <returns>CameraPanControllerが所有する表示原点、未接続時は絶対座標(0, 0)です。</returns>
        private DisplayOrigin GetCurrentDisplayOrigin()
        {
            return cameraPanController == null
                ? new DisplayOrigin(int2.zero)
                : cameraPanController.CurrentDisplayOrigin;
        }

        /// <summary>
        /// 相対セルへ表示原点を64bitで加算し、ワールド端を越える場合はint端へ収めます。
        /// Camera Viewportがint座標域の外へ少しはみ出しても符号反転させず、存在する側の端だけを描くために使用します。
        /// </summary>
        /// <param name="relativeCoordinate">表示原点から数えた1軸分の相対セル座標です。</param>
        /// <param name="displayOriginCoordinate">同じ軸の絶対表示原点です。</param>
        /// <returns>Simulationのint座標域へ収めた絶対タイル座標です。</returns>
        private static int AddDisplayOriginSaturated(
            int relativeCoordinate,
            int displayOriginCoordinate)
        {
            long absoluteCoordinate = (long)relativeCoordinate + displayOriginCoordinate;
            return SaturateToInt(absoluteCoordinate);
        }

        /// <summary>
        /// 64bit座標をUnityのGridセルで扱えるint範囲へ収めます。
        /// 直接castすると遠方で符号が反転するため、比較してから変換します。
        /// </summary>
        /// <param name="coordinate">int範囲を越える可能性がある1軸分の座標です。</param>
        /// <returns>int最小値から最大値の範囲へ収めた座標です。</returns>
        private static int SaturateToInt(long coordinate)
        {
            if (coordinate < int.MinValue)
            {
                return int.MinValue;
            }

            if (coordinate > int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)coordinate;
        }

        /// <summary>
        /// Windowbox時は基準解像度の整数倍矩形、それ以外はCamera.pixelRectをIMGUI左上原点へ変換して返します。
        /// </summary>
        /// <returns>黒帯を除いたGameビュー上の描画矩形です。</returns>
        private Rect CalculateGuiViewportRect()
        {
            if (pixelPerfectCamera != null
                && pixelPerfectCamera.isActiveAndEnabled
                && pixelPerfectCamera.cropFrame == PixelPerfectCamera.CropFrame.Windowbox)
            {
                return WorldGridDebugOverlay.CalculateWindowboxGuiRect(
                    Screen.width,
                    Screen.height,
                    pixelPerfectCamera.refResolutionX,
                    pixelPerfectCamera.refResolutionY);
            }

            Rect cameraPixelRect = targetCamera.pixelRect;
            return new Rect(
                cameraPixelRect.xMin,
                Screen.height - cameraPixelRect.yMax,
                cameraPixelRect.width,
                cameraPixelRect.height);
        }

        /// <summary>
        /// 選択中モード、凡例、Camera中央タイルの生成値と現在の閾値を右上パネルへ表示します。
        /// </summary>
        /// <param name="hasCenterTile">中央タイルを生成済みBufferから取得できた場合はtrueです。</param>
        /// <param name="centerCoordinate">Camera中央の整数タイル座標です。</param>
        /// <param name="centerTile">取得できた中央GeneratedTileです。</param>
        /// <param name="centerResource">取得できた中央GeneratedResourceTileです。</param>
        /// <param name="hasCenterHydrologyCell">中央タイルに対応する生成済み水文セルを取得できた場合はtrueです。</param>
        /// <param name="centerHydrologyCell">中央タイルを担当する8×8水文セルの固定生成結果です。</param>
        /// <param name="hasCenterHydrologyTile">中央タイルの派生水文と固定表流水を取得できた場合はtrueです。</param>
        /// <param name="centerHydrologyTile">中央タイルの自然飽和、自然冠水、湿原フラグです。</param>
        /// <param name="centerSurfaceWaterTile">中央タイルへ最終確定した固定表流水です。</param>
        /// <param name="centerRockWallAppearance">完成した周囲チャンクを含む中央岩壁の露出分類です。</param>
        /// <param name="centerRockWallShadowOpacity">完成地形から求めた中央岩壁影の不透明度です。</param>
        /// <param name="settings">現在の生成閾値を保持するSingleton値です。</param>
        /// <param name="resourceSettings">現在の資源最低間隔と群半径を保持するSingleton値です。</param>
        private void DrawInformationPanel(
            bool hasCenterTile,
            Vector3Int centerCoordinate,
            GeneratedTile centerTile,
            GeneratedResourceTile centerResource,
            bool hasCenterHydrologyCell,
            GeneratedHydrologyCell centerHydrologyCell,
            bool hasCenterHydrologyTile,
            GeneratedHydrologyTile centerHydrologyTile,
            GeneratedSurfaceWaterTile centerSurfaceWaterTile,
            RockWallAppearance centerRockWallAppearance,
            float centerRockWallShadowOpacity,
            WorldGenerationSettings settings,
            ResourceGenerationSettings resourceSettings)
        {
            const int generatedTileInformationLineCount = 26;
            const int waitingInformationLineCount = 1;
            const float bottomPadding = 6f;
            bool showsTwoLegendRows = currentMode == WorldGenerationDebugVisualizationMode.GeologyClass
                || currentMode == WorldGenerationDebugVisualizationMode.HydrologyFlowDirection
                || currentMode == WorldGenerationDebugVisualizationMode.HydrologyLake;
            // 新18Biomeは2列×9行で示すため、中央情報が凡例へ重ならない専用開始位置を使います。
            float informationStartY = currentMode == WorldGenerationDebugVisualizationMode.Biome
                ? 198f
                : showsTwoLegendRows ? 66f : 48f;

            string centerInformation = "Center tile is waiting for generation.";
            if (hasCenterTile)
            {
                RockWallGenerationDebugInfo rockWallInfo =
                    DeterministicRockWallGenerator.GetDebugInfo(
                    settings,
                    centerCoordinate.x,
                    centerCoordinate.y);
                ResourceGenerationDebugInfo resourceInfo =
                    DeterministicResourceGenerator.GetDebugInfo(
                        settings,
                        resourceSettings,
                        centerTile,
                        centerCoordinate.x,
                        centerCoordinate.y);
                ushort magmaDensity = DeterministicMagmaGenerator.SampleDensity(
                    settings,
                    centerCoordinate.x,
                    centerCoordinate.y);
                bool isMagmaEligible = centerTile.Biome == BiomeId.VolcanicGeothermal
                    && centerTile.VolcanicActivityPotential
                        >= settings.MagmaMinimumVolcanicInfluence;
                bool isMagmaCandidate = isMagmaEligible
                    && magmaDensity >= settings.MagmaNoiseThreshold;
                string finalState = !rockWallInfo.FinalCandidate
                    ? "Outside"
                    : centerTile.Ground == GroundType.Water
                        ? "Water-suppressed"
                        : centerTile.Ground == GroundType.Magma
                            ? "Magma-suppressed"
                        : "Rock";
                string resourceAnchor = resourceInfo.SelectedType == GeneratedResourceType.None
                    ? "None"
                    : $"({resourceInfo.AnchorTileX}, {resourceInfo.AnchorTileY})";
                // 中央HUDも地図の色分けと同じ共通APIを使い、地形属性bitの直接表示と最終判定が食い違わないようにします。
                TerrainPlacementResult equipmentPlacementResult = TerrainPlacementRules.Evaluate(
                    centerTile,
                    TerrainPlacementKind.NormalEquipment);
                TerrainPlacementResult bridgePlacementResult = TerrainPlacementRules.Evaluate(
                    centerTile,
                    TerrainPlacementKind.Bridge,
                    hasHeatResistance: false);
                TerrainPlacementResult heatBridgePlacementResult = TerrainPlacementRules.Evaluate(
                    centerTile,
                    TerrainPlacementKind.Bridge,
                    hasHeatResistance: true);
                bool hasGeologyDebugInfo = DeterministicGeologyGenerator.TryGenerate(
                    settings,
                    centerCoordinate.x,
                    centerCoordinate.y,
                    out GeneratedGeologyData geologyDebugInfo);
                string geologySiteInformation = hasGeologyDebugInfo
                    ? $"Macro=({geologyDebugInfo.Site.MacroCellX}, {geologyDebugInfo.Site.MacroCellY})  "
                        + $"Site=({geologyDebugInfo.Site.SiteX}, {geologyDebugInfo.Site.SiteY})  "
                        + $"DistanceSquared={geologyDebugInfo.Site.DistanceSquared}"
                    : "Unavailable";

                // 生密度・平滑化後・通路削り・連続影値を同時に表示し、生成規則変更時の食い違いをGameビューだけで診断できます。
                centerInformation = $"Layer={currentLayer}  Tile ({centerCoordinate.x}, {centerCoordinate.y})  "
                    + $"Biome={centerTile.Biome}  Ground={centerTile.Ground}  Soil={centerTile.Soil}\n"
                    + $"Water={centerTile.WaterSalinity} / {centerTile.WaterBodyType}\n"
                    + $"Placement: Equipment={GetPlacementResultState(equipmentPlacementResult)}\n"
                    + $"Bridge={GetPlacementResultState(bridgePlacementResult)}  "
                    + $"HeatBridge={GetPlacementResultState(heatBridgePlacementResult)}\n"
                    + $"Elevation={centerTile.Elevation}  Class={centerTile.ElevationClass}\n"
                    + $"BaseTemperature={centerTile.Temperature}  "
                    + $"TerrainTemperature={centerTile.TerrainTemperature}\n"
                    + $"BaseMoisture={centerTile.Moisture}  "
                    + $"WoodyPotential={centerTile.WoodyVegetationPotential}\n"
                    + $"Ruggedness={centerTile.TerrainRuggedness}  "
                    + $"CoastalInfluence={centerTile.CoastalInfluence}\n"
                    + $"VolcanicActivity={centerTile.VolcanicActivityPotential}\n"
                    + $"Geology={centerTile.GeologyClass}  Flags={centerTile.GeologyFlags}\n"
                    + $"GeologyTraits: Permeability={centerTile.GeologyPermeability}  "
                    + $"Groundwater={centerTile.GeologyGroundwaterStorage}  "
                    + $"Erosion={centerTile.GeologyErosionResistance}\n"
                    + $"GeologySite: {geologySiteInformation}\n"
                    + $"RockDensity={rockWallInfo.Density} / {settings.RockWallNoiseThreshold}  "
                    + $"Raw={(rockWallInfo.RawCandidate ? "Yes" : "No")}\n"
                    + $"Corridor={(rockWallInfo.Corridor ? "Yes" : "No")}  "
                    + $"Final={finalState}  Surface={centerRockWallAppearance}  "
                    + $"Shadow={centerRockWallShadowOpacity:F2}\n"
                    + $"SurfaceWater: Type={centerSurfaceWaterTile.Type}  "
                    + $"Flags={centerSurfaceWaterTile.Flags}\n"
                    + $"NaturalHydrology: Flags={centerHydrologyTile.Flags}  "
                    + $"Flooding={centerHydrologyTile.NaturalFloodingPotential}  "
                    + $"Saturation={centerHydrologyTile.NaturalSoilSaturationPotential}\n"
                    + $"Magma: Cell={settings.MagmaCellSizeInTiles}/"
                    + $"{settings.MagmaDetailCellSizeInTiles}  "
                    + $"Warp={settings.MagmaWarpAmplitudeInTiles}  "
                    + $"Density={magmaDensity} / {settings.MagmaNoiseThreshold}\n"
                    + $"MagmaState: Eligible={(isMagmaEligible ? "Yes" : "No")}  "
                    + $"Candidate={(isMagmaCandidate ? "Yes" : "No")}  "
                    + $"Magma={(centerTile.Ground == GroundType.Magma ? "Yes" : "No")}\n"
                    + $"RockSettings: Mass={settings.RockWallMassCellSizeInTiles}  "
                    + $"Detail={settings.RockWallDetailCellSizeInTiles}  "
                    + $"Warp={settings.RockWallWarpAmplitudeInTiles}  "
                    + $"Corridor={settings.RockWallCorridorHalfWidth}\n"
                    + $"Resource={centerResource.Type}  Variant={centerResource.VisualVariant}  "
                    + $"Candidates={resourceInfo.EligibleCandidateCount}\n"
                    + $"ResourceAnchor={resourceAnchor}  TileNoise={resourceInfo.TileNoise}  "
                    + $"AnchorNoise={resourceInfo.AnchorNoise}\n"
                    + $"ResourceSpacing/Radius: Tree={resourceSettings.TreeMinimumSpacingInTiles}/"
                    + $"{resourceSettings.TreeClusterRadiusInTiles}  "
                    + $"Stone={resourceSettings.StoneMinimumSpacingInTiles}/"
                    + $"{resourceSettings.StoneClusterRadiusInTiles}  "
                    + $"Sand={resourceSettings.SandMinimumSpacingInTiles}/"
                    + $"{resourceSettings.SandClusterRadiusInTiles}\n"
                    + $"InterimSoil: Hot={settings.HotTemperatureThreshold}  "
                    + $"Dry={settings.DryMoistureThreshold}  Wet={settings.WetMoistureThreshold}";

                if (IsHydrologyVisualizationMode(currentMode))
                {
                    if (hasCenterHydrologyCell)
                    {
                        // 中央HUDはBufferが持つ確定値をそのまま表示し、表示側で流域や湖判定を再計算しません。
                        centerInformation += "\n"
                            + $"HydrologyCell=({centerHydrologyCell.CellX}, {centerHydrologyCell.CellY})  "
                            + $"Height={centerHydrologyCell.HydrologyHeight}  "
                            + $"CenterElevation={centerHydrologyCell.CenterElevation}\n"
                            + $"Flow={centerHydrologyCell.FlowDirection} -> "
                            + $"({centerHydrologyCell.DownstreamCellX}, {centerHydrologyCell.DownstreamCellY})  "
                            + $"Terminal={centerHydrologyCell.DownstreamTerminal}  "
                            + $"Edges={centerHydrologyCell.DownstreamEdgeCount}\n"
                            + $"Reach=({centerHydrologyCell.DownstreamReachCellX}, "
                            + $"{centerHydrologyCell.DownstreamReachCellY})  "
                            + $"Catchment={centerHydrologyCell.CatchmentAreaCells}  "
                            + $"Truncated={(centerHydrologyCell.CatchmentTruncated ? "Yes" : "No")}\n"
                            + $"HydrologyMoisture={centerHydrologyCell.HydrologyMoisture}  "
                            + $"RunoffSum={centerHydrologyCell.RunoffMoistureSum}  "
                            + $"RunoffEquivalent={centerHydrologyCell.RunoffEquivalentCells}  "
                            + $"MaxDistance={centerHydrologyCell.MaximumContributingDistance}\n"
                            + $"Connections={centerHydrologyCell.ConnectionFlags}  "
                            + $"BasinArea={centerHydrologyCell.BasinAreaCells}  "
                            + $"LakeFlags={centerHydrologyCell.LakeFlags}  "
                            + $"LakeClass={centerHydrologyCell.LakeClassification}\n"
                            + $"LakeHeight: Bottom={centerHydrologyCell.LakeBottomHeight}  "
                            + $"Spill={centerHydrologyCell.SpillHeight}  "
                            + $"Surface={centerHydrologyCell.LakeSurfaceHeight}  "
                            + $"Fill={centerHydrologyCell.LakeFillFraction}  "
                            + $"SpillEdge=({centerHydrologyCell.SpillInsideCellX}, "
                            + $"{centerHydrologyCell.SpillInsideCellY})->"
                            + $"({centerHydrologyCell.SpillOutsideCellX}, {centerHydrologyCell.SpillOutsideCellY})";
                    }
                    else
                    {
                        centerInformation += "\nHydrology cell is waiting for generation.";
                    }

                    if (hasCenterHydrologyTile)
                    {
                        // タイル別の固定生成値は専用Bufferから読み、現在水分や現在冠水へ置き換えません。
                        string groundwaterValue = hasCenterHydrologyCell
                            ? centerHydrologyCell.GroundwaterPotential.ToString()
                            : "Unavailable";
                        bool hasSpring = centerSurfaceWaterTile.Type
                            == SurfaceWaterTileType.SpringStream;
                        bool isFreshwaterConnected = (centerHydrologyTile.Flags
                            & GeneratedHydrologyTileFlags.FreshwaterConnected) != 0;
                        bool isWetlandEligible = (centerHydrologyTile.Flags
                            & GeneratedHydrologyTileFlags.WetlandTileEligible) != 0;
                        bool isLargeWetland = (centerHydrologyTile.Flags
                            & GeneratedHydrologyTileFlags.LargeWetlandExtent) != 0;
                        centerInformation += "\n"
                            + $"Groundwater={groundwaterValue}  "
                            + $"NaturalSaturation={centerHydrologyTile.NaturalSoilSaturationPotential}  "
                            + $"NaturalFlooding={centerHydrologyTile.NaturalFloodingPotential}\n"
                            + $"Spring={(hasSpring ? "Yes" : "No")}  "
                            + $"Freshwater={(isFreshwaterConnected ? "Yes" : "No")}  "
                            + $"WetlandEligible={(isWetlandEligible ? "Yes" : "No")}  "
                            + $"LargeWetland={(isLargeWetland ? "Yes" : "No")}";
                    }
                    else
                    {
                        centerInformation += "\nDerived hydrology tile is waiting for generation.";
                    }
                }
            }
            int hydrologyInformationLineCount = IsHydrologyVisualizationMode(currentMode)
                ? (hasCenterHydrologyCell ? 6 : 1) + (hasCenterHydrologyTile ? 2 : 1)
                : 0;
            int informationLineCount = hasCenterTile
                ? generatedTileInformationLineCount
                    + hydrologyInformationLineCount
                : waitingInformationLineCount;

            // 長い閾値情報を2行へ分け、実際の行高からパネル下端を求めることで、見切れと余分な下余白を同時に防ぎます。
            float informationHeight = Mathf.Ceil(
                Mathf.Max(1f, informationStyle.lineHeight) * informationLineCount);
            float panelWidth = Mathf.Min(430f, activeGuiViewportRect.width - 16f);
            float panelHeight = informationStartY + informationHeight + bottomPadding;
            float panelX = Mathf.Max(8f, activeGuiViewportRect.width - panelWidth - 8f);
            Rect panelRect = new Rect(panelX, 8f, panelWidth, panelHeight);
            GUI.Box(panelRect, GUIContent.none);

            // タイトル・色見本・詳細情報を別々に描くことで、凡例の色を文字名ではなく実際のRGBA値で示します。
            GUI.Label(
                new Rect(panelRect.x + 8f, panelRect.y + 6f, panelRect.width - 16f, 18f),
                $"Developer Visualization: {GetModeDisplayName(currentMode)} / {currentLayer}",
                informationStyle);
            DrawLegend(currentMode, panelRect.x + 8f, panelRect.y + 27f);
            GUI.Label(
                new Rect(panelRect.x + 8f, panelRect.y + informationStartY, panelRect.width - 16f, informationHeight),
                centerInformation,
                informationStyle);
        }

        /// <summary>
        /// 共通地形判定の結果を、中央タイルHUDで読みやすい短い文言へ変換します。
        /// 耐熱性不足を通常の地形不可と区別し、橋定義側で不足している能力をGameビューから確認できるようにします。
        /// </summary>
        /// <param name="result">TerrainPlacementRulesが返した地形配置判定結果です。</param>
        /// <returns>許可、地形不可、耐熱不足、未対応を表す英語の短い表示文言です。</returns>
        private static string GetPlacementResultState(TerrainPlacementResult result)
        {
            switch (result)
            {
                case TerrainPlacementResult.Allowed:
                    return "Allowed";
                case TerrainPlacementResult.HeatResistanceRequired:
                    return "Heat Required";
                case TerrainPlacementResult.TerrainDoesNotAllowPlacement:
                    return "Blocked";
                default:
                    return "Unsupported";
            }
        }

        /// <summary>
        /// 描画上限を超えた場合に、色面を省略した理由と現在の可視タイル数を右上へ表示します。
        /// </summary>
        /// <param name="tileCount">Camera矩形から計算した可視タイル数です。</param>
        private void DrawLimitWarning(long tileCount)
        {
            float panelWidth = Mathf.Min(430f, activeGuiViewportRect.width - 16f);
            float panelX = Mathf.Max(8f, activeGuiViewportRect.width - panelWidth - 8f);
            GUI.Box(new Rect(panelX, 8f, panelWidth, 50f), GUIContent.none);
            GUI.Label(
                new Rect(panelX + 8f, 14f, panelWidth - 16f, 38f),
                $"Developer Visualization hidden: {tileCount} visible tiles exceeds "
                    + $"the {MaximumDrawableTileCount} tile limit.",
                informationStyle);
        }

        /// <summary>
        /// Enum名をGameビュー向けの短い表示名へ変換します。
        /// </summary>
        /// <param name="mode">表示名を求める可視化モードです。</param>
        /// <returns>開発者向けHUDへ表示する英語名です。</returns>
        private static string GetModeDisplayName(WorldGenerationDebugVisualizationMode mode)
        {
            if (mode == WorldGenerationDebugVisualizationMode.WaterSalinity)
            {
                return "Water Salinity";
            }

            if (mode == WorldGenerationDebugVisualizationMode.WaterBody)
            {
                return "Water Body";
            }

            if (mode == WorldGenerationDebugVisualizationMode.RockWall)
            {
                return "Rock Wall";
            }

            if (mode == WorldGenerationDebugVisualizationMode.Magma)
            {
                return "Magma";
            }

            if (mode == WorldGenerationDebugVisualizationMode.Resources)
            {
                return "Resources";
            }

            if (mode == WorldGenerationDebugVisualizationMode.TerrainPlacement)
            {
                return "Terrain Placement";
            }

            if (mode == WorldGenerationDebugVisualizationMode.AnimalPopulation)
            {
                return "Animal Population";
            }

            if (mode == WorldGenerationDebugVisualizationMode.BaseTemperature)
            {
                return "Base Temperature";
            }

            if (mode == WorldGenerationDebugVisualizationMode.TerrainTemperature)
            {
                return "Terrain Temperature";
            }

            if (mode == WorldGenerationDebugVisualizationMode.BaseMoisture)
            {
                return "Base Moisture";
            }

            if (mode == WorldGenerationDebugVisualizationMode.WoodyVegetationPotential)
            {
                return "Woody Vegetation Potential";
            }

            if (mode == WorldGenerationDebugVisualizationMode.TerrainRuggedness)
            {
                return "Terrain Ruggedness";
            }

            if (mode == WorldGenerationDebugVisualizationMode.CoastalInfluence)
            {
                return "Coastal Influence";
            }

            if (mode == WorldGenerationDebugVisualizationMode.GeologyPermeability)
            {
                return "Geology Permeability";
            }

            if (mode == WorldGenerationDebugVisualizationMode.GeologyGroundwaterStorage)
            {
                return "Geology Groundwater Storage";
            }

            if (mode == WorldGenerationDebugVisualizationMode.GeologyErosionResistance)
            {
                return "Geology Erosion Resistance";
            }

            if (mode == WorldGenerationDebugVisualizationMode.GeologyDominantSite)
            {
                return "Geology Dominant Site";
            }

            if (mode == WorldGenerationDebugVisualizationMode.GeologySiteBoundary)
            {
                return "Geology Site Boundary";
            }

            if (mode == WorldGenerationDebugVisualizationMode.HydrologyFlowDirection)
            {
                return "Hydrology Flow Direction";
            }

            if (mode == WorldGenerationDebugVisualizationMode.HydrologyTerminal)
            {
                return "Hydrology Terminal";
            }

            if (mode == WorldGenerationDebugVisualizationMode.HydrologyCatchment)
            {
                return "Hydrology Catchment";
            }

            if (mode == WorldGenerationDebugVisualizationMode.HydrologySurfaceWater)
            {
                return "Hydrology Surface Water";
            }

            if (mode == WorldGenerationDebugVisualizationMode.HydrologyLake)
            {
                return "Hydrology Lake";
            }

            if (mode == WorldGenerationDebugVisualizationMode.HydrologyGroundwater)
            {
                return "Hydrology Groundwater";
            }

            if (mode == WorldGenerationDebugVisualizationMode.HydrologyNaturalSoilSaturation)
            {
                return "Hydrology Natural Soil Saturation";
            }

            if (mode == WorldGenerationDebugVisualizationMode.HydrologySpring)
            {
                return "Hydrology Spring";
            }

            if (mode == WorldGenerationDebugVisualizationMode.HydrologyNaturalFlooding)
            {
                return "Hydrology Natural Flooding";
            }

            if (mode == WorldGenerationDebugVisualizationMode.HydrologyWetlandCandidate)
            {
                return "Hydrology Wetland Candidate";
            }

            return mode == WorldGenerationDebugVisualizationMode.VolcanicActivityPotential
                ? "Volcanic Activity Potential"
                : mode.ToString();
        }

        /// <summary>
        /// 選択中モードの分類または値域を、実描画と同じ色のスウォッチ付きで1行に描きます。
        /// </summary>
        /// <param name="mode">凡例へ表示する可視化モードです。</param>
        /// <param name="startX">凡例行の左端X座標です。</param>
        /// <param name="y">色見本の上端Y座標です。</param>
        private void DrawLegend(WorldGenerationDebugVisualizationMode mode, float startX, float y)
        {
            GUI.Label(new Rect(startX, y - 2f, 48f, 18f), "Legend:", informationStyle);
            float nextX = startX + 48f;

            // 凡例もGetVisualizationColorを通すため、色定義を変更したときに地図と凡例だけが食い違いません。
            switch (mode)
            {
                case WorldGenerationDebugVisualizationMode.Biome:
                    for (byte stableId = BiomeStableIds.First;
                        stableId <= BiomeStableIds.Last;
                        stableId++)
                    {
                        // 横長名を430pxパネルへ収めるため2列とし、偶数番目で次の行へ移ります。
                        int zeroBasedIndex = stableId - BiomeStableIds.First;
                        if ((zeroBasedIndex & 1) == 0)
                        {
                            nextX = startX + 48f;
                        }

                        BiomeId biome = (BiomeId)stableId;
                        DrawLegendEntry(
                            ref nextX,
                            y + ((zeroBasedIndex / 2) * 18f),
                            GetBiomeVisualizationColor(biome),
                            GetBiomeLegendName(biome),
                            185f);
                    }
                    break;
                case WorldGenerationDebugVisualizationMode.Soil:
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(mode, new GeneratedTile { Soil = SoilType.None }),
                        "None",
                        62f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(mode, new GeneratedTile { Soil = SoilType.Normal }),
                        "Normal",
                        76f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(mode, new GeneratedTile { Soil = SoilType.Fertile }),
                        "Fertile",
                        74f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(mode, new GeneratedTile { Soil = SoilType.Sandy }),
                        "Sandy",
                        72f);
                    break;
                case WorldGenerationDebugVisualizationMode.TerrainPlacement:
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            new GeneratedTile
                            {
                                GameplayFlags = TileGameplayFlags.AllowsNormalEquipment
                            }),
                        "Equipment",
                        82f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            new GeneratedTile
                            {
                                GameplayFlags = TileGameplayFlags.AllowsBridge
                            }),
                        "Any Bridge",
                        84f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            new GeneratedTile
                            {
                                GameplayFlags = TileGameplayFlags.AllowsBridge
                                    | TileGameplayFlags.RequiresHeatResistance
                            }),
                        "Heat Only",
                        78f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(mode, new GeneratedTile()),
                        "Blocked",
                        76f);
                    break;
                case WorldGenerationDebugVisualizationMode.Elevation:
                    DrawContinuousValueLegend(mode, ref nextX, y, "0 (Low)", "65535 (High)");
                    break;
                case WorldGenerationDebugVisualizationMode.BaseTemperature:
                    DrawContinuousValueLegend(mode, ref nextX, y, "0 (Cold)", "65535 (Hot)");
                    break;
                case WorldGenerationDebugVisualizationMode.TerrainTemperature:
                    DrawContinuousValueLegend(mode, ref nextX, y, "0 (Cold)", "65535 (Hot)");
                    break;
                case WorldGenerationDebugVisualizationMode.BaseMoisture:
                    DrawContinuousValueLegend(mode, ref nextX, y, "0 (Dry)", "65535 (Wet)");
                    break;
                case WorldGenerationDebugVisualizationMode.WoodyVegetationPotential:
                    DrawContinuousValueLegend(mode, ref nextX, y, "0 (Sparse)", "65535 (Dense)");
                    break;
                case WorldGenerationDebugVisualizationMode.TerrainRuggedness:
                    DrawContinuousValueLegend(mode, ref nextX, y, "0 (Flat)", "65535 (Rugged)");
                    break;
                case WorldGenerationDebugVisualizationMode.CoastalInfluence:
                    DrawContinuousValueLegend(mode, ref nextX, y, "0 (Inland)", "65535 (Coastal)");
                    break;
                case WorldGenerationDebugVisualizationMode.VolcanicActivityPotential:
                    DrawContinuousValueLegend(mode, ref nextX, y, "0 (Inactive)", "65535 (Active)");
                    break;
                case WorldGenerationDebugVisualizationMode.GeologyClass:
                    DrawLegendEntry(
                        ref nextX, y,
                        GetVisualizationColor(mode, new GeneratedTile { GeologyClass = GeologyClass.UnconsolidatedSediment }),
                        "Sediment", 84f);
                    DrawLegendEntry(
                        ref nextX, y,
                        GetVisualizationColor(mode, new GeneratedTile { GeologyClass = GeologyClass.Sandstone }),
                        "Sandstone", 88f);
                    DrawLegendEntry(
                        ref nextX, y,
                        GetVisualizationColor(mode, new GeneratedTile { GeologyClass = GeologyClass.Limestone }),
                        "Limestone", 88f);
                    DrawLegendEntry(
                        ref nextX, y,
                        GetVisualizationColor(mode, new GeneratedTile { GeologyClass = GeologyClass.MudstoneSlate }),
                        "Mud/Slate", 88f);
                    nextX = startX + 48f;
                    float secondGeologyLegendY = y + 18f;
                    DrawLegendEntry(
                        ref nextX, secondGeologyLegendY,
                        GetVisualizationColor(mode, new GeneratedTile { GeologyClass = GeologyClass.GraniticRock }),
                        "Granitic", 84f);
                    DrawLegendEntry(
                        ref nextX, secondGeologyLegendY,
                        GetVisualizationColor(mode, new GeneratedTile { GeologyClass = GeologyClass.MetamorphicRock }),
                        "Metamorphic", 98f);
                    DrawLegendEntry(
                        ref nextX, secondGeologyLegendY,
                        GetVisualizationColor(mode, new GeneratedTile { GeologyClass = GeologyClass.VolcanicRock }),
                        "Volcanic", 86f);
                    DrawLegendEntry(
                        ref nextX, secondGeologyLegendY,
                        GetVisualizationColor(mode, new GeneratedTile { GeologyClass = GeologyClass.UltramaficRock }),
                        "Ultramafic", 94f);
                    break;
                case WorldGenerationDebugVisualizationMode.GeologyPermeability:
                case WorldGenerationDebugVisualizationMode.GeologyGroundwaterStorage:
                case WorldGenerationDebugVisualizationMode.GeologyErosionResistance:
                    DrawContinuousValueLegend(mode, ref nextX, y, "0 (Low)", "65535 (High)");
                    break;
                case WorldGenerationDebugVisualizationMode.GeologyFlags:
                    DrawLegendEntry(
                        ref nextX, y,
                        GetVisualizationColor(mode, new GeneratedTile()),
                        "None", 62f);
                    DrawLegendEntry(
                        ref nextX, y,
                        GetVisualizationColor(
                            mode,
                            new GeneratedTile { GeologyFlags = GeologyDefinitionFlags.SupportsKarst }),
                        "Karst", 66f);
                    DrawLegendEntry(
                        ref nextX, y,
                        GetVisualizationColor(
                            mode,
                            new GeneratedTile { GeologyFlags = GeologyDefinitionFlags.SupportsActiveVolcanism }),
                        "Active Volcanism", 124f);
                    break;
                case WorldGenerationDebugVisualizationMode.GeologyDominantSite:
                    DrawLegendEntry(
                        ref nextX, y,
                        GetVisualizationColor(
                            mode,
                            default,
                            geologySite: new GeologySiteData { MacroCellX = 0, MacroCellY = 0 }),
                        "Coordinate color example A", 174f);
                    DrawLegendEntry(
                        ref nextX, y,
                        GetVisualizationColor(
                            mode,
                            default,
                            geologySite: new GeologySiteData { MacroCellX = 1, MacroCellY = 0 }),
                        "Coordinate color example B", 174f);
                    break;
                case WorldGenerationDebugVisualizationMode.GeologySiteBoundary:
                    DrawLegendEntry(
                        ref nextX, y,
                        GetVisualizationColor(mode, default),
                        "Interior", 76f);
                    DrawLegendEntry(
                        ref nextX, y,
                        GetVisualizationColor(mode, default, isGeologySiteBoundary: true),
                        "Boundary", 82f);
                    break;
                case WorldGenerationDebugVisualizationMode.WaterSalinity:
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(mode, new GeneratedTile()),
                        "Land",
                        64f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            new GeneratedTile
                            {
                                WaterSalinity = Farmware.WorldGeneration.WaterSalinity.FreshWater
                            }),
                        "Fresh Water",
                        104f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            new GeneratedTile
                            {
                                WaterSalinity = Farmware.WorldGeneration.WaterSalinity.SaltWater
                            }),
                        "Salt Water",
                        96f);
                    break;
                case WorldGenerationDebugVisualizationMode.WaterBody:
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(mode, new GeneratedTile()),
                        "Land",
                        64f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            new GeneratedTile
                            {
                                WaterBodyType = Farmware.WorldGeneration.WaterBodyType.River
                            }),
                        "River",
                        68f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            new GeneratedTile
                            {
                                WaterBodyType = Farmware.WorldGeneration.WaterBodyType.Pond
                            }),
                        "Pond",
                        66f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            new GeneratedTile
                            {
                                WaterBodyType = Farmware.WorldGeneration.WaterBodyType.Sea
                            }),
                        "Sea",
                        58f);
                    break;
                case WorldGenerationDebugVisualizationMode.RockWall:
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(mode, new GeneratedTile()),
                        "Non-wall",
                        82f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            new GeneratedTile { Ground = GroundType.RockWall },
                            0f),
                        "Exposed",
                        80f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            new GeneratedTile { Ground = GroundType.RockWall },
                            RockWallAppearanceUtility.MaximumShadowOpacity),
                        "Deep shadow",
                        78f);
                    break;
                case WorldGenerationDebugVisualizationMode.Magma:
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(mode, new GeneratedTile()),
                        "Outside",
                        74f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(mode, new GeneratedTile(), 0f, GeneratedResourceType.None, true),
                        "Freshwater suppressed",
                        142f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            new GeneratedTile { Ground = GroundType.Magma },
                            0f,
                            GeneratedResourceType.None,
                            true),
                        "Magma",
                        72f);
                    break;
                case WorldGenerationDebugVisualizationMode.Resources:
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(mode, default),
                        "None",
                        62f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            default,
                            0f,
                            GeneratedResourceType.Tree),
                        "Tree",
                        62f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            default,
                            0f,
                            GeneratedResourceType.Stone),
                        "Stone",
                        70f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            default,
                            0f,
                            GeneratedResourceType.Sand),
                        "Sand",
                        64f);
                    break;
                case WorldGenerationDebugVisualizationMode.AnimalPopulation:
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(mode, default),
                        "Ineligible",
                        88f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            default,
                            isAnimalCandidate: true),
                        "Candidate",
                        88f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            default,
                            hasConfirmedAnimal: true),
                        "Confirmed animal",
                        126f);
                    break;
                case WorldGenerationDebugVisualizationMode.HydrologyFlowDirection:
                    DrawHydrologyFlowLegend(ref nextX, startX, y);
                    break;
                case WorldGenerationDebugVisualizationMode.HydrologyTerminal:
                    DrawHydrologyLegendEntry(
                        mode,
                        ref nextX,
                        y,
                        new GeneratedHydrologyCell
                        {
                            ConnectionFlags = HydrologyConnectionFlags.Marine
                        },
                        "Marine",
                        76f);
                    DrawHydrologyLegendEntry(
                        mode,
                        ref nextX,
                        y,
                        new GeneratedHydrologyCell
                        {
                            ConnectionFlags = HydrologyConnectionFlags.ClosedBasin
                        },
                        "Closed Basin",
                        104f);
                    DrawHydrologyLegendEntry(
                        mode,
                        ref nextX,
                        y,
                        new GeneratedHydrologyCell
                        {
                            ConnectionFlags = HydrologyConnectionFlags.LongDrainage
                        },
                        "Long Drainage",
                        112f);
                    break;
                case WorldGenerationDebugVisualizationMode.HydrologyCatchment:
                    DrawHydrologyLegendEntry(
                        mode,
                        ref nextX,
                        y,
                        new GeneratedHydrologyCell { CatchmentAreaCells = 1 },
                        "1 Cell",
                        72f);
                    DrawHydrologyLegendEntry(
                        mode,
                        ref nextX,
                        y,
                        new GeneratedHydrologyCell { CatchmentAreaCells = 4096 },
                        "4096 Cells",
                        92f);
                    DrawHydrologyLegendEntry(
                        mode,
                        ref nextX,
                        y,
                        new GeneratedHydrologyCell
                        {
                            CatchmentAreaCells = 4096,
                            CatchmentFlags = HydrologyCatchmentFlags.Truncated
                        },
                        "Truncated",
                        88f);
                    break;
                case WorldGenerationDebugVisualizationMode.HydrologySurfaceWater:
                    DrawHydrologyLegendEntry(mode, ref nextX, y, default, "None", 62f);
                    DrawHydrologyLegendEntry(
                        mode,
                        ref nextX,
                        y,
                        new GeneratedHydrologyCell
                        {
                            ConnectionFlags = HydrologyConnectionFlags.Stream
                        },
                        "Stream",
                        72f);
                    DrawHydrologyLegendEntry(
                        mode,
                        ref nextX,
                        y,
                        new GeneratedHydrologyCell
                        {
                            ConnectionFlags = HydrologyConnectionFlags.Stream
                                | HydrologyConnectionFlags.River
                        },
                        "River",
                        68f);
                    break;
                case WorldGenerationDebugVisualizationMode.HydrologyLake:
                    DrawHydrologyLakeLegend(ref nextX, startX, y);
                    break;
                case WorldGenerationDebugVisualizationMode.HydrologyGroundwater:
                    DrawDerivedHydrologyLegendEntry(
                        mode, ref nextX, y,
                        new GeneratedHydrologyCell { GroundwaterPotential = 0 },
                        default, default, "0 (Dry)", 76f);
                    DrawDerivedHydrologyLegendEntry(
                        mode, ref nextX, y,
                        new GeneratedHydrologyCell { GroundwaterPotential = 32767 },
                        default, default, "32767 (No Link)", 112f);
                    DrawDerivedHydrologyLegendEntry(
                        mode, ref nextX, y,
                        new GeneratedHydrologyCell
                        {
                            GroundwaterPotential = 32768,
                            ConnectionFlags = HydrologyConnectionFlags.Groundwater
                        },
                        default, default, "32768 (Link)", 104f);
                    DrawDerivedHydrologyLegendEntry(
                        mode, ref nextX, y,
                        new GeneratedHydrologyCell
                        {
                            GroundwaterPotential = ushort.MaxValue,
                            ConnectionFlags = HydrologyConnectionFlags.Groundwater
                        },
                        default, default, "65535", 64f);
                    break;
                case WorldGenerationDebugVisualizationMode.HydrologyNaturalSoilSaturation:
                    DrawDerivedHydrologyLegendEntry(
                        mode, ref nextX, y, default, default, default, "0 (Dry)", 88f);
                    DrawDerivedHydrologyLegendEntry(
                        mode, ref nextX, y, default,
                        new GeneratedHydrologyTile
                        {
                            NaturalSoilSaturationPotential = ushort.MaxValue
                        },
                        default, "65535 (Wet)", 108f);
                    break;
                case WorldGenerationDebugVisualizationMode.HydrologySpring:
                    DrawDerivedHydrologyLegendEntry(
                        mode, ref nextX, y, default, default, default, "No Spring", 96f);
                    DrawDerivedHydrologyLegendEntry(
                        mode, ref nextX, y, default, default,
                        new GeneratedSurfaceWaterTile { Type = SurfaceWaterTileType.SpringStream },
                        "Spring Stream", 112f);
                    break;
                case WorldGenerationDebugVisualizationMode.HydrologyNaturalFlooding:
                    DrawDerivedHydrologyLegendEntry(
                        mode, ref nextX, y, default, default, default, "0 (Dry)", 88f);
                    DrawDerivedHydrologyLegendEntry(
                        mode, ref nextX, y, default,
                        new GeneratedHydrologyTile { NaturalFloodingPotential = ushort.MaxValue },
                        default, "65535 (Flooded)", 128f);
                    break;
                case WorldGenerationDebugVisualizationMode.HydrologyWetlandCandidate:
                    DrawDerivedHydrologyLegendEntry(
                        mode, ref nextX, y, default, default, default, "Ineligible", 88f);
                    DrawDerivedHydrologyLegendEntry(
                        mode, ref nextX, y, default,
                        new GeneratedHydrologyTile
                        {
                            Flags = GeneratedHydrologyTileFlags.WetlandTileEligible
                        },
                        default, "Local Candidate", 118f);
                    DrawDerivedHydrologyLegendEntry(
                        mode, ref nextX, y, default,
                        new GeneratedHydrologyTile
                        {
                            Flags = GeneratedHydrologyTileFlags.WetlandTileEligible
                                | GeneratedHydrologyTileFlags.LargeWetlandExtent
                        },
                        default, "Large Extent", 104f);
                    break;
                default:
                    GUI.Label(new Rect(nextX, y - 2f, 40f, 18f), "Off", informationStyle);
                    break;
            }
        }

        /// <summary>新18Biomeの安定IDを、仮地形Tileと対応する開発者表示色へ変換します。</summary>
        /// <param name="biome">色分けする新Biomeです。Noneや未定義値は分類漏れ色になります。</param>
        /// <returns>GameビューOverlayへ重ねるAlpha 238の固定色です。</returns>
        private static Color32 GetBiomeVisualizationColor(BiomeId biome)
        {
            switch (biome)
            {
                case BiomeId.TemperateGrassland: return new Color32(112, 169, 86, 238);
                case BiomeId.TropicalSavannaWoodland: return new Color32(150, 166, 75, 238);
                case BiomeId.TemperateDeciduousForest: return new Color32(79, 139, 70, 238);
                case BiomeId.WarmTemperateEvergreenForest: return new Color32(50, 122, 77, 238);
                case BiomeId.TemperateMontaneBambooForest: return new Color32(91, 151, 88, 238);
                case BiomeId.TropicalRainforest: return new Color32(38, 112, 67, 238);
                case BiomeId.TropicalSeasonalForest: return new Color32(68, 133, 61, 238);
                case BiomeId.SubtropicalOceanicForest: return new Color32(58, 137, 105, 238);
                case BiomeId.BorealConiferForest: return new Color32(66, 112, 82, 238);
                case BiomeId.Steppe: return new Color32(174, 162, 82, 238);
                case BiomeId.DryShrublandWoodland: return new Color32(154, 130, 74, 238);
                case BiomeId.CactusDesertDryPlateau: return new Color32(188, 151, 83, 238);
                case BiomeId.HotDesert: return new Color32(218, 181, 101, 238);
                case BiomeId.FreshwaterMarshFloodplain: return new Color32(77, 142, 119, 238);
                case BiomeId.SwampForest: return new Color32(54, 116, 90, 238);
                case BiomeId.TundraPolarBarrens: return new Color32(190, 214, 218, 238);
                case BiomeId.AlpineRockland: return new Color32(126, 122, 135, 238);
                case BiomeId.VolcanicGeothermal: return new Color32(132, 68, 57, 238);
                default:
                    // Noneを温帯草原へ見せず、C5-C分類失敗を暗色として気付けるようにします。
                    return new Color32(24, 24, 30, 238);
            }
        }

        /// <summary>430pxの診断パネルへ収まる、新18Biomeの短い英語凡例名を返します。</summary>
        /// <param name="biome">表示対象の新Biomeです。</param>
        /// <returns>2列凡例用の省略名です。未定義値はInvalidです。</returns>
        private static string GetBiomeLegendName(BiomeId biome)
        {
            switch (biome)
            {
                case BiomeId.TemperateGrassland: return "Temperate Grassland";
                case BiomeId.TropicalSavannaWoodland: return "Tropical Savanna";
                case BiomeId.TemperateDeciduousForest: return "Deciduous Forest";
                case BiomeId.WarmTemperateEvergreenForest: return "Evergreen Forest";
                case BiomeId.TemperateMontaneBambooForest: return "Montane Bamboo";
                case BiomeId.TropicalRainforest: return "Tropical Rainforest";
                case BiomeId.TropicalSeasonalForest: return "Seasonal Forest";
                case BiomeId.SubtropicalOceanicForest: return "Oceanic Forest";
                case BiomeId.BorealConiferForest: return "Boreal Forest";
                case BiomeId.Steppe: return "Steppe";
                case BiomeId.DryShrublandWoodland: return "Dry Shrubland";
                case BiomeId.CactusDesertDryPlateau: return "Cactus Plateau";
                case BiomeId.HotDesert: return "Hot Desert";
                case BiomeId.FreshwaterMarshFloodplain: return "Freshwater Marsh";
                case BiomeId.SwampForest: return "Swamp Forest";
                case BiomeId.TundraPolarBarrens: return "Tundra";
                case BiomeId.AlpineRockland: return "Alpine Rockland";
                case BiomeId.VolcanicGeothermal: return "Volcanic";
                default: return "Invalid";
            }
        }

        /// <summary>8方向と終端を2行へ分け、狭い情報パネル内で流向色をすべて表示します。</summary>
        /// <param name="nextX">1行目で次に描画するX座標です。</param>
        /// <param name="startX">2行目を揃える凡例左端です。</param>
        /// <param name="y">1行目の上端です。</param>
        private void DrawHydrologyFlowLegend(ref float nextX, float startX, float y)
        {
            DrawHydrologyFlowLegendEntry(ref nextX, y, HydrologyFlowDirection.None, "End", 58f);
            DrawHydrologyFlowLegendEntry(ref nextX, y, HydrologyFlowDirection.East, "E", 42f);
            DrawHydrologyFlowLegendEntry(ref nextX, y, HydrologyFlowDirection.NorthEast, "NE", 46f);
            DrawHydrologyFlowLegendEntry(ref nextX, y, HydrologyFlowDirection.North, "N", 42f);
            DrawHydrologyFlowLegendEntry(ref nextX, y, HydrologyFlowDirection.NorthWest, "NW", 48f);

            nextX = startX + 48f;
            float secondRowY = y + 18f;
            DrawHydrologyFlowLegendEntry(ref nextX, secondRowY, HydrologyFlowDirection.West, "W", 42f);
            DrawHydrologyFlowLegendEntry(ref nextX, secondRowY, HydrologyFlowDirection.SouthWest, "SW", 48f);
            DrawHydrologyFlowLegendEntry(ref nextX, secondRowY, HydrologyFlowDirection.South, "S", 42f);
            DrawHydrologyFlowLegendEntry(ref nextX, secondRowY, HydrologyFlowDirection.SouthEast, "SE", 46f);
        }

        /// <summary>流向1種類を実描画と同じ色関数へ渡し、凡例へ追加します。</summary>
        /// <param name="nextX">次に描画するX座標です。</param>
        /// <param name="y">凡例行の上端です。</param>
        /// <param name="direction">色見本へ設定する固定D8方向です。</param>
        /// <param name="label">方向を表す短い表示名です。</param>
        /// <param name="entryWidth">次の要素まで確保する横幅です。</param>
        private void DrawHydrologyFlowLegendEntry(
            ref float nextX,
            float y,
            HydrologyFlowDirection direction,
            string label,
            float entryWidth)
        {
            GeneratedHydrologyCell cell = new GeneratedHydrologyCell { FlowDirection = direction };
            DrawLegendEntry(
                ref nextX,
                y,
                GetHydrologyVisualizationColor(
                    WorldGenerationDebugVisualizationMode.HydrologyFlowDirection,
                    cell),
                label,
                entryWidth);
        }

        /// <summary>湖状態を2行へ分け、流域状態と最終水収支区分を別色で案内します。</summary>
        /// <param name="nextX">1行目で次に描画するX座標です。</param>
        /// <param name="startX">2行目を揃える凡例左端です。</param>
        /// <param name="y">1行目の上端です。</param>
        private void DrawHydrologyLakeLegend(ref float nextX, float startX, float y)
        {
            WorldGenerationDebugVisualizationMode mode =
                WorldGenerationDebugVisualizationMode.HydrologyLake;
            DrawHydrologyLegendEntry(mode, ref nextX, y, default, "Not Basin", 82f);
            DrawHydrologyLegendEntry(
                mode,
                ref nextX,
                y,
                new GeneratedHydrologyCell { LakeFlags = HydrologyLakeFlags.BasinResolved },
                "Dry Basin",
                80f);
            DrawHydrologyLegendEntry(
                mode,
                ref nextX,
                y,
                new GeneratedHydrologyCell { LakeFlags = HydrologyLakeFlags.BasinTruncated },
                "Truncated",
                84f);
            DrawHydrologyLegendEntry(
                mode,
                ref nextX,
                y,
                new GeneratedHydrologyCell { LakeFlags = HydrologyLakeFlags.FixedLake },
                "Lake Basin",
                88f);

            nextX = startX + 48f;
            float secondRowY = y + 18f;
            DrawHydrologyLegendEntry(
                mode,
                ref nextX,
                secondRowY,
                new GeneratedHydrologyCell
                {
                    LakeFlags = HydrologyLakeFlags.FixedLake | HydrologyLakeFlags.SurfaceCell,
                    LakeClassification = LakeWaterBalanceClassification.Temporary
                },
                "Temporary",
                90f);
            DrawHydrologyLegendEntry(
                mode,
                ref nextX,
                secondRowY,
                new GeneratedHydrologyCell
                {
                    LakeFlags = HydrologyLakeFlags.FixedLake | HydrologyLakeFlags.SurfaceCell,
                    LakeClassification = LakeWaterBalanceClassification.PermanentSaltwater
                },
                "Salt Lake",
                82f);
            DrawHydrologyLegendEntry(
                mode,
                ref nextX,
                secondRowY,
                new GeneratedHydrologyCell
                {
                    LakeFlags = HydrologyLakeFlags.FixedLake | HydrologyLakeFlags.SurfaceCell,
                    LakeClassification = LakeWaterBalanceClassification.ClosedFreshwater
                },
                "Fresh Lake",
                86f);
            DrawHydrologyLegendEntry(
                mode,
                ref nextX,
                secondRowY,
                new GeneratedHydrologyCell
                {
                    LakeFlags = HydrologyLakeFlags.FixedLake
                        | HydrologyLakeFlags.SurfaceCell
                        | HydrologyLakeFlags.Overflow,
                    LakeClassification = LakeWaterBalanceClassification.OverflowFreshwater
                },
                "Overflow",
                82f);
        }

        /// <summary>指定した固定水文セルを実描画と同じ色関数へ渡し、分類凡例へ追加します。</summary>
        /// <param name="mode">色の意味を決める水文表示モードです。</param>
        /// <param name="nextX">次に描画するX座標です。</param>
        /// <param name="y">凡例行の上端です。</param>
        /// <param name="cell">凡例分類を表す固定水文セルです。</param>
        /// <param name="label">分類の表示名です。</param>
        /// <param name="entryWidth">次の要素まで確保する横幅です。</param>
        private void DrawHydrologyLegendEntry(
            WorldGenerationDebugVisualizationMode mode,
            ref float nextX,
            float y,
            GeneratedHydrologyCell cell,
            string label,
            float entryWidth)
        {
            DrawLegendEntry(
                ref nextX,
                y,
                GetHydrologyVisualizationColor(mode, cell),
                label,
                entryWidth);
        }

        /// <summary>C4の固定水文入力を実描画と同じ色関数へ渡し、派生水文の凡例へ追加します。</summary>
        /// <param name="mode">色の意味を決める派生水文表示モードです。</param>
        /// <param name="nextX">次に描画するX座標です。</param>
        /// <param name="y">凡例行の上端です。</param>
        /// <param name="cell">地下水凡例に使う固定水文セルです。</param>
        /// <param name="hydrologyTile">自然飽和・自然冠水・湿原凡例に使う固定タイルです。</param>
        /// <param name="surfaceWaterTile">泉凡例に使う固定表流水タイルです。</param>
        /// <param name="label">分類または値域の表示名です。</param>
        /// <param name="entryWidth">次の要素まで確保する横幅です。</param>
        private void DrawDerivedHydrologyLegendEntry(
            WorldGenerationDebugVisualizationMode mode,
            ref float nextX,
            float y,
            GeneratedHydrologyCell cell,
            GeneratedHydrologyTile hydrologyTile,
            GeneratedSurfaceWaterTile surfaceWaterTile,
            string label,
            float entryWidth)
        {
            DrawLegendEntry(
                ref nextX,
                y,
                GetHydrologyVisualizationColor(mode, cell, hydrologyTile, surfaceWaterTile),
                label,
                entryWidth);
        }

        /// <summary>
        /// 連続値モードの最小値と最大値を、対象フィールドだけ設定したGeneratedTileから描きます。
        /// </summary>
        /// <param name="mode">標高またはTERRAIN-01C1で生成する連続環境軸のいずれかです。</param>
        /// <param name="nextX">次の凡例要素を描くX座標です。描画後は右端へ進めます。</param>
        /// <param name="y">色見本の上端Y座標です。</param>
        /// <param name="minimumLabel">最小値側に表示する説明です。</param>
        /// <param name="maximumLabel">最大値側に表示する説明です。</param>
        private void DrawContinuousValueLegend(
            WorldGenerationDebugVisualizationMode mode,
            ref float nextX,
            float y,
            string minimumLabel,
            string maximumLabel)
        {
            GeneratedTile minimumTile = default;
            GeneratedTile maximumTile = default;

            // 同じ構造体のうち、現在のモードが参照するushort値だけを最小・最大へ設定します。
            switch (mode)
            {
                case WorldGenerationDebugVisualizationMode.Elevation:
                    maximumTile.Elevation = ushort.MaxValue;
                    break;
                case WorldGenerationDebugVisualizationMode.BaseTemperature:
                    maximumTile.Temperature = ushort.MaxValue;
                    break;
                case WorldGenerationDebugVisualizationMode.TerrainTemperature:
                    maximumTile.TerrainTemperature = ushort.MaxValue;
                    break;
                case WorldGenerationDebugVisualizationMode.BaseMoisture:
                    maximumTile.Moisture = ushort.MaxValue;
                    break;
                case WorldGenerationDebugVisualizationMode.WoodyVegetationPotential:
                    maximumTile.WoodyVegetationPotential = ushort.MaxValue;
                    break;
                case WorldGenerationDebugVisualizationMode.TerrainRuggedness:
                    maximumTile.TerrainRuggedness = ushort.MaxValue;
                    break;
                case WorldGenerationDebugVisualizationMode.CoastalInfluence:
                    maximumTile.CoastalInfluence = ushort.MaxValue;
                    break;
                case WorldGenerationDebugVisualizationMode.VolcanicActivityPotential:
                    maximumTile.VolcanicActivityPotential = ushort.MaxValue;
                    break;
                case WorldGenerationDebugVisualizationMode.GeologyPermeability:
                    maximumTile.GeologyPermeability = ushort.MaxValue;
                    break;
                case WorldGenerationDebugVisualizationMode.GeologyGroundwaterStorage:
                    maximumTile.GeologyGroundwaterStorage = ushort.MaxValue;
                    break;
                case WorldGenerationDebugVisualizationMode.GeologyErosionResistance:
                    maximumTile.GeologyErosionResistance = ushort.MaxValue;
                    break;
            }

            DrawLegendEntry(ref nextX, y, GetVisualizationColor(mode, minimumTile), minimumLabel, 102f);
            DrawLegendEntry(ref nextX, y, GetVisualizationColor(mode, maximumTile), maximumLabel, 116f);
        }

        /// <summary>
        /// 1つのRGBA色見本と、その色が表す分類または値を横並びで描きます。
        /// </summary>
        /// <param name="nextX">この要素の左端です。描画後は次の要素の左端へ進めます。</param>
        /// <param name="y">色見本の上端Y座標です。</param>
        /// <param name="color">GetVisualizationColorが返した実際のRGBA色です。</param>
        /// <param name="label">色の意味を説明する文字列です。</param>
        /// <param name="entryWidth">次の要素まで確保する横幅です。</param>
        private void DrawLegendEntry(
            ref float nextX,
            float y,
            Color color,
            string label,
            float entryWidth)
        {
            Color previousGuiColor = GUI.color;

            // 半透明色でも形が判別できるよう黒い1px枠を置き、その内側へ地図と同じRGBA色を重ねます。
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(nextX, y, 14f, 14f), Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(nextX + 1f, y + 1f, 12f, 12f), Texture2D.whiteTexture);
            GUI.color = previousGuiColor;

            GUI.Label(new Rect(nextX + 18f, y - 2f, entryWidth - 18f, 18f), label, informationStyle);
            nextX += entryWidth;
        }

        /// <summary>
        /// Gameビュー用GUI Styleを初回描画時だけ作成し、Repaintごとのmanaged allocationを避けます。
        /// </summary>
        private void EnsureGuiStyle()
        {
            if (informationStyle != null)
            {
                return;
            }

            informationStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                wordWrap = false
            };
            informationStyle.normal.textColor = Color.white;
        }

        /// <summary>
        /// 現在のDefault Worldへ作成したQueryを破棄し、PlayMode終了後の古いWorld参照を残さないようにします。
        /// </summary>
        private void DisposeQueries()
        {
            if (!queriesAreCreated)
            {
                return;
            }

            // PlayMode終了時はECS WorldがMonoBehaviourより先に破棄される場合があり、その場合Queryもすでに解放済みです。
            if (presentationWorld != null && presentationWorld.IsCreated)
            {
                chunkQuery.Dispose();
                settingsQuery.Dispose();
                resourceSettingsQuery.Dispose();
                animalCatalogQuery.Dispose();
                animalPositionQuery.Dispose();
            }

            animalPositionCache.Clear();
            completedTerrainChunkEntities.Clear();
            hydrologyCellCache.Clear();
            queriesAreCreated = false;
            presentationWorld = null;
        }

        /// <summary>
        /// 1回のIMGUI描画で扱う、整数タイル座標の包含矩形です。
        /// </summary>
        private readonly struct VisibleTileBounds
        {
            /// <summary>画面へ少しでも含まれる最小水平タイル座標です。</summary>
            public readonly int MinimumTileX;

            /// <summary>画面へ少しでも含まれる最大水平タイル座標です。</summary>
            public readonly int MaximumTileX;

            /// <summary>画面へ少しでも含まれる最小垂直タイル座標です。</summary>
            public readonly int MinimumTileY;

            /// <summary>画面へ少しでも含まれる最大垂直タイル座標です。</summary>
            public readonly int MaximumTileY;

            /// <summary>
            /// Cameraから求めた4端の包含座標を、描画中だけ使う不変値として保持します。
            /// </summary>
            /// <param name="minimumTileX">最小水平タイル座標です。</param>
            /// <param name="maximumTileX">最大水平タイル座標です。</param>
            /// <param name="minimumTileY">最小垂直タイル座標です。</param>
            /// <param name="maximumTileY">最大垂直タイル座標です。</param>
            public VisibleTileBounds(
                int minimumTileX,
                int maximumTileX,
                int minimumTileY,
                int maximumTileY)
            {
                MinimumTileX = minimumTileX;
                MaximumTileX = maximumTileX;
                MinimumTileY = minimumTileY;
                MaximumTileY = maximumTileY;
            }
        }
    }
}
