// このファイルは、ECSで生成済みの可視タイルをPlayMode中だけ色分けして、ノイズとバイオーム判定を確認できるようにします。
// 操作入口はEditor AssemblyのWorldGenerationDebugVisualizationMenuに置き、Simulationとプレイヤー用F3/F4入力から分離します。

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

        /// <summary>論理Biome IDを草原・森林・砂漠の固定色で表示します。</summary>
        Biome = 1,

        /// <summary>0〜65535の連続標高値を黒から白の濃淡で表示します。</summary>
        Elevation = 2,

        /// <summary>0〜65535の連続気温値を青から赤のグラデーションで表示します。</summary>
        Temperature = 3,

        /// <summary>0〜65535の連続湿度値を茶から水色のグラデーションで表示します。</summary>
        Moisture = 4,

        /// <summary>論理Biomeと異なる陸上Groundが選ばれた移行タイルだけを強調します。</summary>
        BiomeTransition = 5
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

        // 座標、生成状態、GeneratedTile Bufferを持つチャンクだけを読み取る再利用Queryです。
        private EntityQuery chunkQuery;

        // 中央タイルHUDへ現在の閾値を表示するため、WorldGenerationSettings Singletonを読むQueryです。
        private EntityQuery settingsQuery;

        // EntityQueryを現在のDefault World向けに作成済みかを示します。
        private bool queriesAreCreated;

        // 現在のIMGUI描画で使用する、黒帯を除いたGameビュー矩形です。
        private Rect activeGuiViewportRect;

        // モード名、凡例、中央タイル値を描くため、初回だけ作成して再利用するGUI Styleです。
        private GUIStyle informationStyle;

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
                    DrawInformationPanel(false, default, default, default);
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
        /// 指定モードとGeneratedTileから、Gameビューへ重ねる色を純粋計算します。
        /// PlayModeテストも同じ入口を使い、凡例と実描画の対応が変わっていないことを保証します。
        /// </summary>
        /// <param name="mode">色の意味を決める可視化モードです。</param>
        /// <param name="tile">ECS Dynamic Bufferから読み取った1タイルの生成結果です。</param>
        /// <returns>IMGUIへ設定するRGBA色です。Noneの場合は完全な透明色です。</returns>
        public static Color GetVisualizationColor(
            WorldGenerationDebugVisualizationMode mode,
            GeneratedTile tile)
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

                    return new Color32(104, 190, 85, 238);

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

                case WorldGenerationDebugVisualizationMode.BiomeTransition:
                    return IsBiomeTransitionTile(tile)
                        ? new Color32(255, 28, 210, 242)
                        : new Color32(0, 0, 0, 110);

                default:
                    return Color.clear;
            }
        }

        /// <summary>
        /// 論理Biomeの代表Groundと実際の陸上Groundを比較し、移行帯で別の地面が選ばれたかを判定します。
        /// 水域と岩壁は標高分類による上書きなので、バイオーム移行としては数えません。
        /// </summary>
        /// <param name="tile">判定する1タイルの生成結果です。</param>
        /// <returns>陸上で論理Biomeと異なるGroundが選択されている場合はtrueです。</returns>
        public static bool IsBiomeTransitionTile(GeneratedTile tile)
        {
            if (tile.Ground == GroundType.Water || tile.Ground == GroundType.RockWall)
            {
                return false;
            }

            GroundType representativeGround = GroundType.Normal;
            if (tile.Biome == BiomeId.Forest)
            {
                representativeGround = GroundType.Vegetation;
            }
            else if (tile.Biome == BiomeId.Desert)
            {
                representativeGround = GroundType.Sand;
            }

            return tile.Ground != representativeGround;
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
                ComponentType.ReadOnly<GeneratedTile>());
            settingsQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldGenerationSettings>());
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
            EntityManager entityManager = presentationWorld.EntityManager;

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
                if (tiles.Length != WorldGridConstants.TilesPerChunk)
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
                        GUI.color = GetVisualizationColor(currentMode, tile);
                        GUI.DrawTexture(CalculateTileGuiRect(tileX, tileY), Texture2D.whiteTexture);

                        if (tileX == centerTileCoordinate.x && tileY == centerTileCoordinate.y)
                        {
                            hasCenterTile = true;
                            centerTile = tile;
                        }
                    }
                }
            }

            GUI.color = previousGuiColor;
            WorldGenerationSettings settings = settingsQuery.CalculateEntityCount() == 1
                ? settingsQuery.GetSingleton<WorldGenerationSettings>()
                : default;
            DrawInformationPanel(hasCenterTile, centerTileCoordinate, centerTile, settings);
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
        /// <param name="settings">現在の生成閾値を保持するSingleton値です。</param>
        private void DrawInformationPanel(
            bool hasCenterTile,
            Vector3Int centerCoordinate,
            GeneratedTile centerTile,
            WorldGenerationSettings settings)
        {
            const int generatedTileInformationLineCount = 5;
            const int waitingInformationLineCount = 1;
            const float informationStartY = 48f;
            const float bottomPadding = 6f;

            string centerInformation = hasCenterTile
                ? $"Tile ({centerCoordinate.x}, {centerCoordinate.y})  "
                    + $"Biome={centerTile.Biome}  Ground={centerTile.Ground}\n"
                    + $"Elevation={centerTile.Elevation}  Temperature={centerTile.Temperature}  "
                    + $"Moisture={centerTile.Moisture}\n"
                    + $"Transition={(IsBiomeTransitionTile(centerTile) ? "Yes" : "No")}\n"
                    + $"Thresholds: Water={settings.WaterThreshold}  Rock={settings.RockWallThreshold}  "
                    + $"Hot={settings.HotTemperatureThreshold}\n"
                    + $"Dry={settings.DryMoistureThreshold}  Wet={settings.WetMoistureThreshold}"
                : "Center tile is waiting for generation.";
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
                case WorldGenerationDebugVisualizationMode.BiomeTransition:
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            new GeneratedTile { Biome = BiomeId.Forest, Ground = GroundType.Normal }),
                        "Transition Ground",
                        132f);
                    DrawLegendEntry(
                        ref nextX,
                        y,
                        GetVisualizationColor(
                            mode,
                            new GeneratedTile { Biome = BiomeId.Forest, Ground = GroundType.Vegetation }),
                        "Unchanged",
                        96f);
                    break;
                default:
                    GUI.Label(new Rect(nextX, y - 2f, 40f, 18f), "Off", informationStyle);
                    break;
            }
        }

        /// <summary>
        /// 連続値モードの最小値と最大値を、対象フィールドだけ設定したGeneratedTileから描きます。
        /// </summary>
        /// <param name="mode">Elevation、Temperature、Moistureのいずれかです。</param>
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
