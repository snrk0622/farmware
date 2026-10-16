// このファイルは、ECSで生成済みの可視タイルをPlayMode中だけ色分けして、ノイズ・バイオーム・土壌・水域・岩壁・資源・地形配置属性を確認できるようにします。
// 操作入口はEditor AssemblyのWorldGenerationDebugVisualizationMenuに置き、Simulationとプレイヤー用F3/F4入力から分離します。

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
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

        /// <summary>論理Biome IDを7種類の固定色で表示します。</summary>
        Biome = 1,

        /// <summary>0〜65535の連続標高値を黒から白の濃淡で表示します。</summary>
        Elevation = 2,

        /// <summary>0〜65535の連続気温値を青から赤のグラデーションで表示します。</summary>
        Temperature = 3,

        /// <summary>0〜65535の連続湿度値を茶から水色のグラデーションで表示します。</summary>
        Moisture = 4,

        /// <summary>水域を陸地・淡水・海水の固定色で表示します。</summary>
        WaterSalinity = 5,

        /// <summary>水域の生成由来を陸地・川・池・海の固定色で表示します。</summary>
        WaterBody = 6,

        /// <summary>論理Biomeと異なる陸上Groundが選ばれた移行タイルだけを強調します。</summary>
        BiomeTransition = 7,

        /// <summary>最終岩壁を非岩壁・露出面・連続した内部影の濃さとして表示します。</summary>
        RockWall = 8,

        /// <summary>最低間隔と配置競合を適用した後の木・石・砂を固定色で表示します。</summary>
        Resources = 9,

        /// <summary>0〜65535の連続Weirdness値を紫から黄のグラデーションで表示します。</summary>
        Weirdness = 10,

        /// <summary>マグマ本体、淡水抑制用の火山影響域、それ以外を固定色で表示します。</summary>
        Magma = 11,

        /// <summary>プレイヤー表示へ出さない標準・肥沃・砂質のSoilTypeを固定色で表示します。</summary>
        Soil = 12,

        /// <summary>通常設備・通常ベルト・橋の配置可否と、橋の耐熱要件を地形契約ごとの固定色で表示します。</summary>
        TerrainPlacement = 13
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

        // Windowboxの黒帯を除いた描画領域を求めるため、Cameraと同じGameObjectから取得して保持します。
        private PixelPerfectCamera pixelPerfectCamera;

        // Editorメニューが実行中だけ変更する現在の可視化モードです。Sceneやセーブデータへは保存しません。
        private WorldGenerationDebugVisualizationMode currentMode;

        // PlayMode終了やDefault World再作成を検知してEntityQueryを作り直すために保持するWorldです。
        private World presentationWorld;

        // 座標、生成状態、地形・資源Bufferを持つチャンクだけを読み取る再利用Queryです。
        private EntityQuery chunkQuery;

        // 中央タイルHUDへ現在の閾値を表示するため、WorldGenerationSettings Singletonを読むQueryです。
        private EntityQuery settingsQuery;

        // 中央タイルHUDへ資源の最低間隔と群半径を表示するため、ResourceGenerationSettings Singletonを読むQueryです。
        private EntityQuery resourceSettingsQuery;

        // EntityQueryを現在のDefault World向けに作成済みかを示します。
        private bool queriesAreCreated;

        // 現在のIMGUI描画で使用する、黒帯を除いたGameビュー矩形です。
        private Rect activeGuiViewportRect;

        // モード名、凡例、中央タイル値を描くため、初回だけ作成して再利用するGUI Styleです。
        private GUIStyle informationStyle;

        // Rock Wall表示の1回のRepaint内で座標ごとの岩壁判定を共有し、同じ重いノイズ生成を近隣タイルごとに繰り返さないキャッシュです。
        private readonly Dictionary<long, bool> rockWallProbeCache =
            new Dictionary<long, bool>();

        /// <summary>Editorメニューが選択中として表示する、現在の開発者用可視化モードです。</summary>
        public WorldGenerationDebugVisualizationMode CurrentMode => currentMode;

        /// <summary>
        /// Editor構築ツールまたはテストから、投影Cameraとタイル座標用Gridをまとめて設定します。
        /// </summary>
        /// <param name="camera">可視化を重ねる2Dカメラです。</param>
        /// <param name="grid">通常地形と同じセル変換を提供するGridです。</param>
        public void Configure(Camera camera, GridLayout grid)
        {
            targetCamera = camera;
            targetGrid = grid;
            pixelPerfectCamera = targetCamera == null
                ? null
                : targetCamera.GetComponent<PixelPerfectCamera>();
        }

        /// <summary>
        /// Component読み込み直後にUnityから呼ばれ、未設定のCamera参照とPixel Perfect Camera参照を補います。
        /// </summary>
        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }

            pixelPerfectCamera = targetCamera == null
                ? null
                : targetCamera.GetComponent<PixelPerfectCamera>();
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
                    DrawInformationPanel(false, default, default, default, default, default);
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
        /// 指定モード、GeneratedTile、岩壁影不透明度、最終資源ID、マグマ影響域からGameビューへ重ねる色を純粋計算します。
        /// PlayModeテストも同じ入口を使い、凡例と実描画の対応が変わっていないことを保証します。
        /// </summary>
        /// <param name="mode">色の意味を決める可視化モードです。</param>
        /// <param name="tile">ECS Dynamic Bufferから読み取った1タイルの生成結果です。</param>
        /// <param name="rockWallShadowOpacity">RockWallモードで使う0〜1の連続影不透明度です。他モードでは参照しません。</param>
        /// <param name="resourceType">Resourcesモードで色分けする競合解決後の資源IDです。</param>
        /// <param name="isMagmaInfluenceArea">Magmaモードで淡水抑制域を暗赤色表示する場合はtrueです。</param>
        /// <returns>IMGUIへ設定するRGBA色です。Noneの場合は完全な透明色です。</returns>
        public static Color GetVisualizationColor(
            WorldGenerationDebugVisualizationMode mode,
            GeneratedTile tile,
            float rockWallShadowOpacity = 0f,
            GeneratedResourceType resourceType = GeneratedResourceType.None,
            bool isMagmaInfluenceArea = false)
        {
            switch (mode)
            {
                case WorldGenerationDebugVisualizationMode.Biome:
                    if (tile.Biome == BiomeId.Forest)
                    {
                        return new Color32(42, 128, 65, 238);
                    }

                    if (tile.Biome == BiomeId.Desert)
                    {
                        return new Color32(224, 174, 70, 238);
                    }

                    if (tile.Biome == BiomeId.Rockland)
                    {
                        return new Color32(130, 126, 145, 238);
                    }

                    if (tile.Biome == BiomeId.Wetland)
                    {
                        return new Color32(45, 135, 112, 238);
                    }

                    if (tile.Biome == BiomeId.Snowfield)
                    {
                        return new Color32(198, 230, 238, 238);
                    }

                    if (tile.Biome == BiomeId.Volcanic)
                    {
                        return new Color32(154, 62, 48, 238);
                    }

                    return new Color32(104, 190, 85, 238);

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
                    TileGameplayFlags normalGroundFlags =
                        TileGameplayFlags.AllowsNormalEquipment
                        | TileGameplayFlags.AllowsNormalBelt;
                    if ((tile.GameplayFlags & normalGroundFlags) == normalGroundFlags)
                    {
                        return new Color32(72, 194, 92, 242);
                    }

                    if ((tile.GameplayFlags & TileGameplayFlags.AllowsBridge) != 0)
                    {
                        // 水上橋とマグマ上の耐熱橋を別色にし、要件bitの付け忘れをGameビューで発見できるようにします。
                        return (tile.GameplayFlags & TileGameplayFlags.RequiresHeatResistance) != 0
                            ? new Color32(238, 82, 38, 245)
                            : new Color32(48, 146, 226, 242);
                    }

                    return new Color32(42, 42, 50, 220);

                case WorldGenerationDebugVisualizationMode.Elevation:
                    float elevation = tile.Elevation / (float)ushort.MaxValue;
                    return Color.Lerp(
                        new Color32(18, 22, 34, 238),
                        new Color32(242, 246, 255, 238),
                        elevation);

                case WorldGenerationDebugVisualizationMode.Temperature:
                    float temperature = tile.Temperature / (float)ushort.MaxValue;
                    return Color.Lerp(
                        new Color32(45, 95, 210, 238),
                        new Color32(235, 66, 42, 238),
                        temperature);

                case WorldGenerationDebugVisualizationMode.Moisture:
                    float moisture = tile.Moisture / (float)ushort.MaxValue;
                    return Color.Lerp(
                        new Color32(176, 116, 52, 238),
                        new Color32(50, 185, 225, 238),
                        moisture);

                case WorldGenerationDebugVisualizationMode.Weirdness:
                    float weirdness = tile.Weirdness / (float)ushort.MaxValue;
                    return Color.Lerp(
                        new Color32(69, 48, 112, 238),
                        new Color32(246, 208, 72, 238),
                        weirdness);

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

                case WorldGenerationDebugVisualizationMode.BiomeTransition:
                    return IsBiomeTransitionTile(tile)
                        ? new Color32(255, 28, 210, 242)
                        : new Color32(0, 0, 0, 110);

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

                    return isMagmaInfluenceArea
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

                default:
                    return Color.clear;
            }
        }

        /// <summary>
        /// 論理Biomeの代表Soilと実際の陸上Soilを比較し、移行帯で別の土壌が選ばれたかを判定します。
        /// 水域と独立生成された岩壁は土壌を持たないため、バイオーム移行としては数えません。
        /// </summary>
        /// <param name="tile">判定する1タイルの生成結果です。</param>
        /// <returns>陸上で論理Biomeと異なるSoilが選択されている場合はtrueです。</returns>
        public static bool IsBiomeTransitionTile(GeneratedTile tile)
        {
            if (tile.Ground == GroundType.Water
                || tile.Ground == GroundType.RockWall
                || tile.Ground == GroundType.Magma)
            {
                return false;
            }

            SoilType representativeSoil = SoilType.Normal;
            if (tile.Biome == BiomeId.Forest || tile.Biome == BiomeId.Wetland)
            {
                representativeSoil = SoilType.Fertile;
            }
            else if (tile.Biome == BiomeId.Desert)
            {
                representativeSoil = SoilType.Sandy;
            }

            return tile.Soil != representativeSoil;
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
                ComponentType.ReadOnly<GeneratedResourceTile>());
            settingsQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldGenerationSettings>());
            resourceSettingsQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ResourceGenerationSettings>());
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

            Vector3 centerWorld = targetCamera.transform.position;
            Vector3Int centerTileCoordinate = targetGrid == null
                ? new Vector3Int(Mathf.FloorToInt(centerWorld.x), Mathf.FloorToInt(centerWorld.y), 0)
                : targetGrid.WorldToCell(centerWorld);
            bool hasCenterTile = false;
            GeneratedTile centerTile = default;
            GeneratedResourceTile centerResource = default;
            EntityManager entityManager = presentationWorld.EntityManager;
            bool hasGenerationSettings = settingsQuery.CalculateEntityCount() == 1;
            WorldGenerationSettings settings = hasGenerationSettings
                ? settingsQuery.GetSingleton<WorldGenerationSettings>()
                : default;
            bool hasResourceSettings = resourceSettingsQuery.CalculateEntityCount() == 1;
            ResourceGenerationSettings resourceSettings = hasResourceSettings
                ? resourceSettingsQuery.GetSingleton<ResourceGenerationSettings>()
                : default;

            // 距離探索範囲は隣接タイル同士で重なるため、Rock Wallモードの1描画中だけ座標判定を共有します。
            rockWallProbeCache.Clear();

            using NativeArray<Entity> entities = chunkQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<ChunkCoordinateComponent> coordinates =
                chunkQuery.ToComponentDataArray<ChunkCoordinateComponent>(Allocator.Temp);
            using NativeArray<ChunkGenerationStatus> statuses =
                chunkQuery.ToComponentDataArray<ChunkGenerationStatus>(Allocator.Temp);

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
                if (tiles.Length != WorldGridConstants.TilesPerChunk
                    || resources.Length != WorldGridConstants.TilesPerChunk)
                {
                    continue;
                }

                // チャンク全体ではなく、カメラ矩形と交差する行・列だけを描き、画面外タイルのIMGUI発行を避けます。
                for (int tileY = minimumTileY; tileY <= maximumTileY; tileY++)
                {
                    int localY = tileY - chunkMinimumY;
                    for (int tileX = minimumTileX; tileX <= maximumTileX; tileX++)
                    {
                        int localX = tileX - chunkMinimumX;
                        int tileIndex = (localY * WorldGridConstants.LogicalChunkSize) + localX;
                        GeneratedTile tile = tiles[tileIndex];
                        GeneratedResourceTile resource = resources[tileIndex];
                        float rockWallShadowOpacity =
                            currentMode == WorldGenerationDebugVisualizationMode.RockWall
                            && hasGenerationSettings
                                ? CalculateCachedRockWallShadowOpacity(
                                    settings,
                                    tileX,
                                    tileY)
                                : 0f;
                        bool isMagmaInfluenceArea = false;
                        if (currentMode == WorldGenerationDebugVisualizationMode.Magma
                            && hasGenerationSettings)
                        {
                            // 専用モードのときだけ追加ノイズを読み、通常表示や他のデバッグ表示へ計算負荷を持ち込みません。
                            MagmaGenerationDebugInfo magmaInfo =
                                DeterministicChunkGenerator.GetMagmaGenerationDebugInfo(
                                    tile.Elevation,
                                    tile.Temperature,
                                    tile.Weirdness,
                                    tile.Biome,
                                    settings,
                                    tileX,
                                    tileY);
                            isMagmaInfluenceArea = magmaInfo.IsInfluenceArea;
                        }
                        GUI.color = GetVisualizationColor(
                            currentMode,
                            tile,
                            rockWallShadowOpacity,
                            resource.Type,
                            isMagmaInfluenceArea);
                        GUI.DrawTexture(CalculateTileGuiRect(tileX, tileY), Texture2D.whiteTexture);

                        if (tileX == centerTileCoordinate.x && tileY == centerTileCoordinate.y)
                        {
                            hasCenterTile = true;
                            centerTile = tile;
                            centerResource = resource;
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
                settings,
                resourceSettings);
        }

        /// <summary>
        /// 開発者用Rock Wall表示について、Repaint内で共有した岩壁判定から最寄り空地距離と連続影値を求めます。
        /// </summary>
        /// <param name="settings">水域優先を含む最終岩壁判定に使う生成設定です。</param>
        /// <param name="worldTileX">中心の符号付き水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">中心の符号付き垂直ワールドタイル座標です。</param>
        /// <returns>0以上MaximumShadowOpacity以下の連続した影不透明度です。</returns>
        private float CalculateCachedRockWallShadowOpacity(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            if (!GetCachedRockWall(settings, worldTileX, worldTileY))
            {
                return 0f;
            }

            int searchRadius = RockWallAppearanceUtility.ShadowSearchRadiusInTiles;
            int maximumDistanceSquared = searchRadius * searchRadius;
            int nearestOpenDistanceSquared = int.MaxValue;

            // 通常表示と同じ円形距離を使い、デバッグ色と実際の影が別の形にならないようにします。
            for (int offsetY = -searchRadius; offsetY <= searchRadius; offsetY++)
            {
                for (int offsetX = -searchRadius; offsetX <= searchRadius; offsetX++)
                {
                    int distanceSquared = (offsetX * offsetX) + (offsetY * offsetY);
                    if (distanceSquared == 0
                        || distanceSquared > maximumDistanceSquared
                        || distanceSquared >= nearestOpenDistanceSquared)
                    {
                        continue;
                    }

                    if (!GetCachedRockWall(
                            settings,
                            worldTileX + offsetX,
                            worldTileY + offsetY))
                    {
                        nearestOpenDistanceSquared = distanceSquared;
                    }
                }
            }

            return RockWallAppearanceUtility
                .CalculateShadowOpacityFromNearestOpenDistanceSquared(
                    nearestOpenDistanceSquared);
        }

        /// <summary>
        /// 符号付きXY座標を64bitキーへ可逆に詰め、同じRepaint中の決定論的岩壁判定を再利用します。
        /// </summary>
        /// <param name="settings">岩壁判定に使う現在の生成設定です。</param>
        /// <param name="worldTileX">調べる水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">調べる垂直ワールドタイル座標です。</param>
        /// <returns>水域優先適用後の最終地形が岩壁ならtrueです。</returns>
        private bool GetCachedRockWall(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            long coordinateKey = ((long)worldTileX << 32) | (uint)worldTileY;
            if (rockWallProbeCache.TryGetValue(coordinateKey, out bool isRockWall))
            {
                return isRockWall;
            }

            isRockWall = DeterministicChunkGenerator.IsRockWall(
                settings,
                worldTileX,
                worldTileY);
            rockWallProbeCache.Add(coordinateKey, isRockWall);
            return isRockWall;
        }

        /// <summary>
        /// 指定タイルの4辺をCamera Viewportへ投影し、Windowbox左上原点のIMGUI矩形へ変換します。
        /// </summary>
        /// <param name="tileX">描画する水平ワールドタイル座標です。</param>
        /// <param name="tileY">描画する垂直ワールドタイル座標です。</param>
        /// <returns>1タイルを覆う正の幅と高さを持つIMGUI矩形です。</returns>
        private Rect CalculateTileGuiRect(int tileX, int tileY)
        {
            Vector3 bottomLeftWorld = CellBoundaryToWorld(tileX, tileY);
            Vector3 topRightWorld = CellBoundaryToWorld(tileX + 1, tileY + 1);
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

            if (targetGrid != null)
            {
                Vector3Int bottomLeftCell = targetGrid.WorldToCell(bottomLeftWorld);
                Vector3Int topRightCell = targetGrid.WorldToCell(topRightWorld);
                return new VisibleTileBounds(
                    Mathf.Min(bottomLeftCell.x, topRightCell.x),
                    Mathf.Max(bottomLeftCell.x, topRightCell.x),
                    Mathf.Min(bottomLeftCell.y, topRightCell.y),
                    Mathf.Max(bottomLeftCell.y, topRightCell.y));
            }

            return new VisibleTileBounds(
                Mathf.FloorToInt(Mathf.Min(bottomLeftWorld.x, topRightWorld.x)),
                Mathf.CeilToInt(Mathf.Max(bottomLeftWorld.x, topRightWorld.x)) - 1,
                Mathf.FloorToInt(Mathf.Min(bottomLeftWorld.y, topRightWorld.y)),
                Mathf.CeilToInt(Mathf.Max(bottomLeftWorld.y, topRightWorld.y)) - 1);
        }

        /// <summary>
        /// タイル境界座標を、Gridの原点・Transform・セル寸法を反映したワールド座標へ変換します。
        /// </summary>
        /// <param name="tileX">水平タイル境界座標です。</param>
        /// <param name="tileY">垂直タイル境界座標です。</param>
        /// <returns>通常地形と同じ平面上のワールド座標です。</returns>
        private Vector3 CellBoundaryToWorld(int tileX, int tileY)
        {
            return targetGrid == null
                ? new Vector3(tileX, tileY, 0f)
                : targetGrid.CellToWorld(new Vector3Int(tileX, tileY, 0));
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
        /// <param name="settings">現在の生成閾値を保持するSingleton値です。</param>
        /// <param name="resourceSettings">現在の資源最低間隔と群半径を保持するSingleton値です。</param>
        private void DrawInformationPanel(
            bool hasCenterTile,
            Vector3Int centerCoordinate,
            GeneratedTile centerTile,
            GeneratedResourceTile centerResource,
            WorldGenerationSettings settings,
            ResourceGenerationSettings resourceSettings)
        {
            const int generatedTileInformationLineCount = 20;
            const int waitingInformationLineCount = 1;
            const float bottomPadding = 6f;
            float informationStartY = currentMode == WorldGenerationDebugVisualizationMode.Biome
                ? 66f
                : 48f;

            string centerInformation = "Center tile is waiting for generation.";
            if (hasCenterTile)
            {
                RockWallGenerationDebugInfo rockWallInfo =
                    DeterministicRockWallGenerator.GetDebugInfo(
                    settings,
                    centerCoordinate.x,
                    centerCoordinate.y);
                RockWallAppearance rockWallAppearance =
                    RockWallAppearanceUtility.ClassifyAt(
                        settings,
                        centerCoordinate.x,
                        centerCoordinate.y);
                float rockWallShadowOpacity =
                    RockWallAppearanceUtility.CalculateShadowOpacityAt(
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
                WaterGenerationDebugInfo waterInfo =
                    DeterministicChunkGenerator.GetWaterGenerationDebugInfo(
                        centerTile.Elevation,
                        centerTile.Temperature,
                        centerTile.Moisture,
                        centerTile.Weirdness,
                        settings,
                        centerCoordinate.x,
                        centerCoordinate.y);
                MagmaGenerationDebugInfo magmaInfo =
                    DeterministicChunkGenerator.GetMagmaGenerationDebugInfo(
                        centerTile.Elevation,
                        centerTile.Temperature,
                        centerTile.Weirdness,
                        centerTile.Biome,
                        settings,
                        centerCoordinate.x,
                        centerCoordinate.y);
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
                ushort displayedMagmaNoiseThreshold = magmaInfo.UsesRocklandProfile
                    ? settings.RocklandMagmaNoiseThreshold
                    : settings.MagmaNoiseThreshold;
                int displayedMagmaCellSize = magmaInfo.UsesRocklandProfile
                    ? settings.RocklandMagmaCellSizeInTiles
                    : settings.MagmaCellSizeInTiles;
                int displayedMagmaDetailCellSize = magmaInfo.UsesRocklandProfile
                    ? 0
                    : settings.MagmaDetailCellSizeInTiles;
                int displayedMagmaWarpAmplitude = magmaInfo.UsesRocklandProfile
                    ? 0
                    : settings.MagmaWarpAmplitudeInTiles;
                ushort displayedMagmaInfluenceThreshold = magmaInfo.UsesRocklandProfile
                    ? settings.RocklandMagmaMinimumVolcanicInfluence
                    : settings.MagmaMinimumVolcanicInfluence;
                string magmaProfile;
                if (!magmaInfo.IsEligibleBiome)
                {
                    // 対象外バイオームを火山用設定と誤認しないよう、診断HUDへ明示します。
                    magmaProfile = "Outside";
                }
                else if (magmaInfo.UsesRocklandProfile)
                {
                    magmaProfile = "Rockland";
                }
                else
                {
                    magmaProfile = "Volcanic";
                }

                // 生密度・平滑化後・通路削り・連続影値を同時に表示し、生成規則変更時の食い違いをGameビューだけで診断できます。
                centerInformation = $"Tile ({centerCoordinate.x}, {centerCoordinate.y})  "
                    + $"Biome={centerTile.Biome}  Ground={centerTile.Ground}  Soil={centerTile.Soil}\n"
                    + $"Water={centerTile.WaterSalinity} / {centerTile.WaterBodyType}\n"
                    + $"Placement: Equipment={GetFlagState(centerTile.GameplayFlags, TileGameplayFlags.AllowsNormalEquipment)}  "
                    + $"Belt={GetFlagState(centerTile.GameplayFlags, TileGameplayFlags.AllowsNormalBelt)}  "
                    + $"Bridge={GetFlagState(centerTile.GameplayFlags, TileGameplayFlags.AllowsBridge)}  "
                    + $"Heat={GetFlagState(centerTile.GameplayFlags, TileGameplayFlags.RequiresHeatResistance)}\n"
                    + $"Elevation={centerTile.Elevation}  Temperature={centerTile.Temperature}\n"
                    + $"Moisture={centerTile.Moisture}  Weirdness={centerTile.Weirdness}\n"
                    + $"Transition={(IsBiomeTransitionTile(centerTile) ? "Yes" : "No")}\n"
                    + $"RockDensity={rockWallInfo.Density} / {settings.RockWallNoiseThreshold}  "
                    + $"Raw={(rockWallInfo.RawCandidate ? "Yes" : "No")}\n"
                    + $"Corridor={(rockWallInfo.Corridor ? "Yes" : "No")}  "
                    + $"Final={finalState}  Surface={rockWallAppearance}  "
                    + $"Shadow={rockWallShadowOpacity:F2}\n"
                    + $"WaterSettings: Sea={settings.SeaLevelThreshold}  "
                    + $"FreshMax={settings.FreshWaterMaximumElevation}\n"
                    + $"Pond: Noise={waterInfo.PondSample} / {waterInfo.EffectivePondThreshold}  "
                    + $"Wet={waterInfo.WetClimateInfluence}  Wetland={waterInfo.WetlandInfluence}\n"
                    + $"PondSuppress: Desert={waterInfo.DesertInfluence}  "
                    + $"Highland={waterInfo.HighlandInfluence}  Volcanic={waterInfo.VolcanicInfluence}\n"
                    + $"River: Distance={waterInfo.RiverDistanceFromCenterLine} / "
                    + $"{waterInfo.EffectiveRiverHalfWidth}\n"
                    + $"Magma[{magmaProfile}]: Cell={displayedMagmaCellSize}/{displayedMagmaDetailCellSize}  "
                    + $"Warp={displayedMagmaWarpAmplitude}  "
                    + $"Noise={magmaInfo.MagmaSample} / {displayedMagmaNoiseThreshold}  "
                    + $"Volcanic={magmaInfo.VolcanicInfluence} / {displayedMagmaInfluenceThreshold}\n"
                    + $"MagmaState: Raw={(magmaInfo.RawMagmaCandidate ? "Yes" : "No")}  "
                    + $"Eligible={(magmaInfo.IsEligibleBiome ? "Yes" : "No")}  "
                    + $"Influence={(magmaInfo.IsInfluenceArea ? "Yes" : "No")}  "
                    + $"Magma={(magmaInfo.IsMagma ? "Yes" : "No")}\n"
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
                    + $"Cold={settings.ColdTemperatureThreshold}  Hot={settings.HotTemperatureThreshold}  "
                    + $"Dry={settings.DryMoistureThreshold}  Wet={settings.WetMoistureThreshold}\n"
                    + $"Wetland={settings.WetlandMoistureThreshold}/"
                    + $"{settings.WetlandMaximumElevation}  "
                    + $"Highland={settings.HighlandElevationThreshold}  "
                    + $"Volcanic={settings.VolcanicWeirdnessThreshold}";
            }
            int informationLineCount = hasCenterTile
                ? generatedTileInformationLineCount
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
                $"Developer Visualization: {GetModeDisplayName(currentMode)}",
                informationStyle);
            DrawLegend(currentMode, panelRect.x + 8f, panelRect.y + 27f);
            GUI.Label(
                new Rect(panelRect.x + 8f, panelRect.y + informationStartY, panelRect.width - 16f, informationHeight),
                centerInformation,
                informationStyle);
        }

        /// <summary>
        /// GameplayFlagsの指定bitを、中央タイルHUDで読みやすいYesまたはNoへ変換します。
        /// 表示専用の変換であり、元のGeneratedTileやECS Bufferは変更しません。
        /// </summary>
        /// <param name="flags">中央タイルから読み取った配置属性の組み合わせです。</param>
        /// <param name="targetFlag">表示したい1つの配置属性です。</param>
        /// <returns>属性を持つ場合はYes、持たない場合はNoです。</returns>
        private static string GetFlagState(
            TileGameplayFlags flags,
            TileGameplayFlags targetFlag)
        {
            return (flags & targetFlag) != 0 ? "Yes" : "No";
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

            return mode == WorldGenerationDebugVisualizationMode.BiomeTransition
                ? "Biome Transition"
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
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(mode, new GeneratedTile { Biome = BiomeId.Grassland }),
                        "Grassland",
                        92f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(mode, new GeneratedTile { Biome = BiomeId.Forest }),
                        "Forest",
                        74f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(mode, new GeneratedTile { Biome = BiomeId.Desert }),
                        "Desert",
                        74f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(mode, new GeneratedTile { Biome = BiomeId.Rockland }),
                        "Rockland",
                        84f);
                    nextX = startX + 48f;
                    float secondBiomeLegendY = y + 18f;
                    DrawLegendEntry(
                        ref nextX,
                        secondBiomeLegendY,
                        GetVisualizationColor(mode, new GeneratedTile { Biome = BiomeId.Wetland }),
                        "Wetland",
                        82f);
                    DrawLegendEntry(
                        ref nextX,
                        secondBiomeLegendY,
                        GetVisualizationColor(mode, new GeneratedTile { Biome = BiomeId.Snowfield }),
                        "Snowfield",
                        90f);
                    DrawLegendEntry(
                        ref nextX,
                        secondBiomeLegendY,
                        GetVisualizationColor(mode, new GeneratedTile { Biome = BiomeId.Volcanic }),
                        "Volcanic",
                        88f);
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
                                    | TileGameplayFlags.AllowsNormalBelt
                            }),
                        "Equipment + Belt",
                        126f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            new GeneratedTile
                            {
                                GameplayFlags = TileGameplayFlags.AllowsBridge
                            }),
                        "Bridge",
                        72f);
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
                        "Heat Bridge",
                        96f);
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
                case WorldGenerationDebugVisualizationMode.Temperature:
                    DrawContinuousValueLegend(mode, ref nextX, y, "0 (Cold)", "65535 (Hot)");
                    break;
                case WorldGenerationDebugVisualizationMode.Moisture:
                    DrawContinuousValueLegend(mode, ref nextX, y, "0 (Dry)", "65535 (Wet)");
                    break;
                case WorldGenerationDebugVisualizationMode.Weirdness:
                    DrawContinuousValueLegend(mode, ref nextX, y, "0 (Common)", "65535 (Weird)");
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
                case WorldGenerationDebugVisualizationMode.BiomeTransition:
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            new GeneratedTile
                            {
                                Biome = BiomeId.Forest,
                                Ground = GroundType.Land,
                                Soil = SoilType.Normal
                            }),
                        "Transition Soil",
                        132f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            new GeneratedTile
                            {
                                Biome = BiomeId.Forest,
                                Ground = GroundType.Land,
                                Soil = SoilType.Fertile
                            }),
                        "Unchanged",
                        96f);
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
                default:
                    GUI.Label(new Rect(nextX, y - 2f, 40f, 18f), "Off", informationStyle);
                    break;
            }
        }

        /// <summary>
        /// 連続値モードの最小値と最大値を、対象フィールドだけ設定したGeneratedTileから描きます。
        /// </summary>
        /// <param name="mode">Elevation、Temperature、Moisture、Weirdnessのいずれかです。</param>
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
                case WorldGenerationDebugVisualizationMode.Temperature:
                    maximumTile.Temperature = ushort.MaxValue;
                    break;
                case WorldGenerationDebugVisualizationMode.Moisture:
                    maximumTile.Moisture = ushort.MaxValue;
                    break;
                case WorldGenerationDebugVisualizationMode.Weirdness:
                    maximumTile.Weirdness = ushort.MaxValue;
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
            }

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
