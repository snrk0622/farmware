// このファイルは、プレイヤーがF3で現在・初期スポーン座標、F4で各グリッド境界と初期スポーン位置を表示できるようにします。
// ゲーム状態を変更しない診断表示として製品版でも利用でき、開発者専用のノイズ可視化とは入力と責務を分離します。

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// カメラ中心と初期スポーンの座標、各グリッド境界、赤系の初期スポーンタイル・所属チャンク表示を画面へ重ねます。
    /// プレイヤー向け診断表示として製品版でも動作しますが、地形やチャンクのゲーム状態は変更しません。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class WorldGridDebugOverlay : MonoBehaviour
    {
        /// <summary>極端なズームアウト時にIMGUI描画数が増え過ぎないよう制限する可視チャンク数です。</summary>
        private const int MaximumDrawableChunkCount = 256;

        /// <summary>極端なズームアウト時に1タイル間隔の線だけを省略する、縦線と横線の合計上限です。</summary>
        private const int MaximumDrawableTileLineCount = 512;

        /// <summary>画面投影後の誤差を許容し、グリッド線を縦または横と判定する最大ピクセル差です。</summary>
        private const float AxisAlignmentToleranceInPixels = 0.01f;

        // 座標変換とワールド座標の画面投影に使用するカメラです。同じGameObjectのCameraを通常は指定します。
        [Tooltip("座標HUDとチャンク境界を投影する2Dカメラです。未指定なら同じGameObjectのCameraを使用します。")]
        [SerializeField] private Camera targetCamera;

        // タイルセルとワールド位置の変換元です。地形表示用Tilemapを所有するGridを指定します。
        [Tooltip("地形Tilemapを所有するGridです。Gridの原点とセルサイズを座標表示へ反映します。未指定時は1 Unity unitを1タイルとして扱います。")]
        [SerializeField] private GridLayout targetGrid;

        // 初期スポーン表示をTilemapと同じ相対座標へ投影するため、Main Cameraの表示原点を参照します。
        private CameraPanController cameraPanController;

        // Scene開始直後からF3 HUDを表示するかを決めるAuthoring設定です。実行中の切替値とは分けて保持します。
        [Tooltip("Play開始直後から座標HUDを表示する場合に有効にします。実行中はF3キーで切り替えられます。")]
        [SerializeField] private bool showCoordinateHudOnStart;

        // Scene開始直後からF4境界を表示するかを決めるAuthoring設定です。実行中の切替値とは分けて保持します。
        [Tooltip("Play開始直後からタイル・サブチャンク・チャンク境界と座標ラベルを表示する場合に有効にします。実行中はF4キーで切り替えられます。")]
        [SerializeField] private bool showChunkGridOnStart;

        // 両表示が閉じている場合も操作方法を見失わないよう、画面下へショートカットを常時表示します。
        [Tooltip("画面左下にF3・F4のショートカット説明を表示します。デバッグ表示本体が閉じていても操作方法を確認できます。")]
        [SerializeField] private bool showShortcutHint = true;

        // 1タイル境界線へ使用する色です。地形を隠さずセルだけ読めるよう、低い不透明度にします。
        [Tooltip("F4で表示する1×1タイル境界線の色です。アルファ値を下げると地形を透かして確認できます。")]
        [SerializeField] private Color tileBoundaryColor = new Color(1f, 1f, 1f, 0.16f);

        // 1タイル境界線の画面上の太さです。チャンク線より細くして階層を区別します。
        [Tooltip("F4で表示する1×1タイル境界線の太さです。単位は画面ピクセルで、カメラのズームには影響されません。")]
        [Min(1f)]
        [SerializeField] private float tileBoundaryThicknessInPixels = 1f;

        // サブチャンク境界線へ使用する色です。タイル線とチャンク線の中間として水色を使用します。
        [Tooltip("F4で表示する16×16タイルのサブチャンク境界線の色です。チャンク境界との区別に使用します。")]
        [SerializeField] private Color subChunkBoundaryColor = new Color(0.2f, 0.9f, 1f, 0.62f);

        // サブチャンク境界線の画面上の太さです。ズーム率に左右されず読めるピクセル単位で保持します。
        [Tooltip("F4で表示するサブチャンク境界線の太さです。単位は画面ピクセルで、カメラのズームには影響されません。")]
        [Min(1f)]
        [SerializeField] private float subChunkBoundaryThicknessInPixels = 1f;

        // チャンク境界線へ使用する色です。単色地形の上でも見分けやすい半透明の橙色を初期値にします。
        [Tooltip("F4で表示するチャンク境界線の色です。アルファ値を下げると地形を透かして確認できます。")]
        [SerializeField] private Color chunkBoundaryColor = new Color(1f, 0.62f, 0.12f, 0.9f);

        // チャンク境界線の画面上の太さです。ワールドのズーム率には影響されないピクセル単位です。
        [Tooltip("F4で表示するチャンク境界線の太さです。単位は画面ピクセルで、カメラのズームには影響されません。")]
        [Min(1f)]
        [SerializeField] private float chunkBoundaryThicknessInPixels = 2f;

        // 初期スポーンタイルの地形を隠さず位置を面として読めるよう、低い不透明度の明るい赤を重ねます。
        [Tooltip("F4で初期スポーンタイル内へ重ねる色です。アルファ値を上げるほど地形が赤く覆われます。")]
        [SerializeField] private Color initialSpawnTileFillColor = new Color(1f, 0.12f, 0.12f, 0.18f);

        // 初期スポーンタイルの1セル輪郭へ使用し、通常の白いタイル境界より強く区別します。
        [Tooltip("F4で初期スポーンタイルの輪郭へ使用する明るい赤色です。")]
        [SerializeField] private Color initialSpawnTileBoundaryColor = new Color(1f, 0.18f, 0.18f, 1f);

        // 初期スポーンタイル輪郭の画面上の太さです。ズーム率に関係なく見つけられる太さを保ちます。
        [Tooltip("F4で初期スポーンタイルの輪郭へ使用する太さです。単位は画面ピクセルです。")]
        [Min(1f)]
        [SerializeField] private float initialSpawnTileBoundaryThicknessInPixels = 3f;

        // 初期スポーン所属チャンクの輪郭とラベルへ使用し、通常の橙色チャンクから区別する濃い赤です。
        [Tooltip("F4で初期スポーン所属チャンクの輪郭とラベルへ使用する濃い赤色です。")]
        [SerializeField] private Color initialSpawnChunkColor = new Color(0.82f, 0.05f, 0.12f, 0.95f);

        // 初期スポーン所属チャンクの輪郭を通常チャンク線より太くし、重なっても赤線を手前で読めるようにします。
        [Tooltip("F4で初期スポーン所属チャンクの輪郭へ使用する太さです。単位は画面ピクセルです。")]
        [Min(1f)]
        [SerializeField] private float initialSpawnChunkThicknessInPixels = 3f;

        // F3で切り替わる現在のHUD表示状態です。Sceneへは保存せず、Play開始ごとに初期設定から復元します。
        private bool isCoordinateHudVisible;

        // F4で切り替わる現在のチャンク境界表示状態です。ゲーム状態やセーブデータには含めません。
        private bool isChunkGridVisible;

        // Windowboxが作る最終描画領域を取得するためのPixel Perfect Cameraです。AwakeまたはConfigure時にCameraから取得します。
        private PixelPerfectCamera pixelPerfectCamera;

        // 現在のOnGUI呼び出しで地形が表示される矩形です。GUI.BeginGroup内では幅と高さだけをローカル座標変換へ使用します。
        private Rect activeGuiViewportRect;

        // OnGUIごとのmanaged allocationを避けるため、初回描画時に作って以後再利用する座標HUD用Styleです。
        private GUIStyle coordinateHudStyle;

        // 各可視チャンクの中央へ座標を描くため、初回描画時に作って再利用するラベルStyleです。
        private GUIStyle chunkLabelStyle;

        // 初期スポーン所属チャンクだけを赤い文字で示すため、通常チャンクと分けて再利用するラベルStyleです。
        private GUIStyle initialSpawnChunkLabelStyle;

        // F3・F4の操作説明へ使用し、デバッグ表示を閉じた状態でも小さく表示するStyleです。
        private GUIStyle shortcutHintStyle;

        // F3とF4が診断情報を読むDefault Worldです。World再作成時に古いQueryとチャンクEntityキャッシュを捨てるため保持します。
        private World diagnosticWorld;

        // F3またはF4表示中に毎フレームQueryを作り直さず、初期スポーンSingletonだけを読み取るためのQueryです。
        private EntityQuery spawnLocationQuery;

        // F3で探索中と探索失敗を区別するため、初期スポーン探索状態Singletonだけを読み取るQueryです。
        private EntityQuery spawnSearchStatusQuery;

        // F3へGeneratorVersionとワールドシードを表示するため、生成設定Singletonだけを読み取るQueryです。
        private EntityQuery generationSettingsQuery;

        // F3へ現在タイルのBiomeを表示するため、生成済みチャンク座標・状態・地形Bufferを読むQueryです。
        private EntityQuery generatedChunkQuery;

        // 上記4つの診断用Queryを現在のWorld向けに作成済みかを示します。
        private bool diagnosticQueriesAreCreated;

        // 現在タイルを含むGeneratedTile BufferのEntityです。同じチャンク内で毎フレーム全チャンクを走査しないため保持します。
        private Entity cachedGeneratedChunkEntity;

        // cachedGeneratedChunkEntityが表す論理チャンク座標です。カメラが別チャンクへ移動したことを整数座標で判定します。
        private ChunkCoordinate cachedGeneratedChunkCoordinate;

        // cachedGeneratedChunkEntityとcachedGeneratedChunkCoordinateへ有効な組み合わせを記録済みかを示します。
        private bool hasCachedGeneratedChunk;

        /// <summary>現在F3の座標HUDを表示している場合はtrueです。</summary>
        public bool IsCoordinateHudVisible => isCoordinateHudVisible;

        /// <summary>現在F4のタイル・サブチャンク・チャンク境界と座標ラベルを表示している場合はtrueです。</summary>
        public bool IsChunkGridVisible => isChunkGridVisible;

        /// <summary>
        /// Editor構築ツールまたはテストから、投影カメラと座標変換用Gridをまとめて設定します。
        /// </summary>
        /// <param name="camera">デバッグ表示を重ねる2Dカメラです。</param>
        /// <param name="grid">論理タイル座標の原点とセルサイズを提供するGridです。</param>
        public void Configure(Camera camera, GridLayout grid)
        {
            targetCamera = camera;
            targetGrid = grid;
            pixelPerfectCamera = targetCamera == null
                ? null
                : targetCamera.GetComponent<PixelPerfectCamera>();
            cameraPanController = targetCamera == null
                ? null
                : targetCamera.GetComponent<CameraPanController>();
        }

        /// <summary>
        /// Component読み込み直後にUnityから呼ばれ、未設定のCamera参照を同じGameObjectから補います。
        /// </summary>
        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }

            // Pixel Perfect CameraはCameraと同じGameObjectに置かれるため、描画領域計算用に参照を1回だけ保持します。
            pixelPerfectCamera = targetCamera == null
                ? null
                : targetCamera.GetComponent<PixelPerfectCamera>();
            cameraPanController = targetCamera == null
                ? null
                : targetCamera.GetComponent<CameraPanController>();
        }

        /// <summary>
        /// Componentが有効になったときにUnityから呼ばれ、実行中の表示状態をInspectorの開始設定へ戻します。
        /// </summary>
        private void OnEnable()
        {
            isCoordinateHudVisible = showCoordinateHudOnStart;
            isChunkGridVisible = showChunkGridOnStart;
        }

        /// <summary>
        /// Component破棄時にUnityから呼ばれ、F3・F4表示のために作成したEntityQueryを解放します。
        /// </summary>
        private void OnDestroy()
        {
            DisposeDiagnosticQueries();
        }

        /// <summary>
        /// MonoBehaviourの各描画フレームで呼ばれ、F3とF4が押されたフレームだけ表示状態を反転します。
        /// プレイヤーへ提供する診断表示なので、Editorと製品版で同じキー操作を使用します。
        /// </summary>
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.f3Key.wasPressedThisFrame)
            {
                SetCoordinateHudVisible(!isCoordinateHudVisible);
            }

            if (keyboard.f4Key.wasPressedThisFrame)
            {
                SetChunkGridVisible(!isChunkGridVisible);
            }
        }

        /// <summary>
        /// UnityのIMGUI描画イベントごとに呼ばれ、有効なデバッグ情報だけを画面へ重ねます。
        /// 診断表示専用のため、通常のゲームUIへCanvasや永続状態を追加しません。
        /// </summary>
        private void OnGUI()
        {
            if (targetCamera == null)
            {
                return;
            }

            EnsureGuiStyles();
            activeGuiViewportRect = CalculateGuiViewportRect();
            if (activeGuiViewportRect.width <= 0f || activeGuiViewportRect.height <= 0f)
            {
                return;
            }

            // Windowbox外をクリップし、以降のHUDとグリッド座標を地形領域の左上原点へ統一します。
            // try/finallyにすることで、将来描画処理を変更してもGUI.EndGroupの呼び忘れで他のIMGUIを壊しません。
            GUI.BeginGroup(activeGuiViewportRect);
            try
            {
                if (isChunkGridVisible)
                {
                    DrawVisibleChunkGrid();
                }

                if (isCoordinateHudVisible)
                {
                    DrawCoordinateHud();
                }

                if (showShortcutHint)
                {
                    DrawShortcutHint();
                }
            }
            finally
            {
                GUI.EndGroup();
            }
        }

        /// <summary>
        /// 現在のGameビュー内で、地形が最終的に表示される左上原点のIMGUI矩形を求めます。
        /// Windowbox時はPixel Perfect Cameraと同じ整数倍計算を使い、それ以外はCameraの描画矩形へ追従します。
        /// </summary>
        /// <returns>黒帯を除いたIMGUI座標系の描画矩形です。</returns>
        private Rect CalculateGuiViewportRect()
        {
            if (pixelPerfectCamera != null
                && pixelPerfectCamera.isActiveAndEnabled
                && pixelPerfectCamera.cropFrame == PixelPerfectCamera.CropFrame.Windowbox)
            {
                return CalculateWindowboxGuiRect(
                    Screen.width,
                    Screen.height,
                    pixelPerfectCamera.refResolutionX,
                    pixelPerfectCamera.refResolutionY);
            }

            // Camera.pixelRectは左下原点なので、上端までの距離を使ってIMGUIの左上原点へ変換します。
            Rect cameraPixelRect = targetCamera.pixelRect;
            return new Rect(
                cameraPixelRect.xMin,
                Screen.height - cameraPixelRect.yMax,
                cameraPixelRect.width,
                cameraPixelRect.height);
        }

        /// <summary>
        /// Pixel Perfect CameraのWindowboxと同じ規則で、基準解像度を整数倍した中央描画矩形を計算します。
        /// テストからも利用でき、Gameビューの余り領域が左右または上下の黒帯になることを数値で検証できます。
        /// </summary>
        /// <param name="screenWidth">Gameビュー全体の幅です。単位は画面ピクセルです。</param>
        /// <param name="screenHeight">Gameビュー全体の高さです。単位は画面ピクセルです。</param>
        /// <param name="referenceWidth">Pixel Perfect Cameraの基準解像度幅です。単位は基準ピクセルです。</param>
        /// <param name="referenceHeight">Pixel Perfect Cameraの基準解像度高さです。単位は基準ピクセルです。</param>
        /// <returns>Gameビュー左上を原点とした、黒帯を除く最終描画矩形です。</returns>
        public static Rect CalculateWindowboxGuiRect(
            int screenWidth,
            int screenHeight,
            int referenceWidth,
            int referenceHeight)
        {
            // Unity側でも基準解像度は最低1として扱うため、無効値で除算例外が起きないよう同じ前提へ補正します。
            int safeReferenceWidth = Mathf.Max(1, referenceWidth);
            int safeReferenceHeight = Mathf.Max(1, referenceHeight);
            int safeScreenWidth = Mathf.Max(1, screenWidth);
            int safeScreenHeight = Mathf.Max(1, screenHeight);

            // 縦横の両方へ収まる最大整数倍率を選びます。1未満にはしない点もPixel Perfect Cameraの実装と一致させます。
            int horizontalZoom = safeScreenWidth / safeReferenceWidth;
            int verticalZoom = safeScreenHeight / safeReferenceHeight;
            int integerZoom = Mathf.Max(1, Mathf.Min(horizontalZoom, verticalZoom));
            int viewportWidth = integerZoom * safeReferenceWidth;
            int viewportHeight = integerZoom * safeReferenceHeight;

            // 余ったピクセルを左右・上下へ均等に配り、Windowboxの最終Blitと同じ中央配置にします。
            int viewportX = (safeScreenWidth - viewportWidth) / 2;
            int viewportBottom = (safeScreenHeight - viewportHeight) / 2;

            // Unityの最終Blit矩形は左下原点で端数を下側へ切り捨てるため、奇数ピクセルの余りは上側へ1px多く残ります。
            // IMGUIは左上原点なので、単に同じY値を使わず、下端座標から上端までの距離へ変換します。
            int viewportTop = safeScreenHeight - (viewportBottom + viewportHeight);
            return new Rect(viewportX, viewportTop, viewportWidth, viewportHeight);
        }

        /// <summary>
        /// 縦線または横線の2端点から、回転行列を使わずに描画できるIMGUI矩形を計算します。
        /// F4グリッドは軸に平行なので、Windowboxのクリップ座標と衝突しやすいGUI.matrix回転を避けるために使用します。
        /// </summary>
        /// <param name="startGui">Windowbox左上を原点とする線の開始座標です。単位は画面ピクセルです。</param>
        /// <param name="endGui">Windowbox左上を原点とする線の終了座標です。単位は画面ピクセルです。</param>
        /// <param name="lineThicknessInPixels">線の太さです。単位は画面ピクセルです。</param>
        /// <param name="lineRect">成功時に、線を覆う左上原点の描画矩形を返します。</param>
        /// <returns>端点が縦または横にそろい、有効な長さの矩形を計算できた場合はtrueです。</returns>
        public static bool TryCalculateAxisAlignedLineRect(
            Vector2 startGui,
            Vector2 endGui,
            float lineThicknessInPixels,
            out Rect lineRect)
        {
            Vector2 difference = endGui - startGui;
            float safeThickness = Mathf.Max(0f, lineThicknessInPixels);
            if (safeThickness <= 0f)
            {
                lineRect = default;
                return false;
            }

            // 縦線はX座標の小さな投影誤差を平均し、太さの半分だけ左へ広げて中心を境界へ合わせます。
            if (Mathf.Abs(difference.x) <= AxisAlignmentToleranceInPixels
                && Mathf.Abs(difference.y) > AxisAlignmentToleranceInPixels)
            {
                float centerX = (startGui.x + endGui.x) * 0.5f;
                lineRect = new Rect(
                    centerX - (safeThickness * 0.5f),
                    Mathf.Min(startGui.y, endGui.y),
                    safeThickness,
                    Mathf.Abs(difference.y));
                return true;
            }

            // 横線もY座標を平均し、開始・終了方向に依存せず左上から正の幅を持つ矩形へ正規化します。
            if (Mathf.Abs(difference.y) <= AxisAlignmentToleranceInPixels
                && Mathf.Abs(difference.x) > AxisAlignmentToleranceInPixels)
            {
                float centerY = (startGui.y + endGui.y) * 0.5f;
                lineRect = new Rect(
                    Mathf.Min(startGui.x, endGui.x),
                    centerY - (safeThickness * 0.5f),
                    Mathf.Abs(difference.x),
                    safeThickness);
                return true;
            }

            lineRect = default;
            return false;
        }

        /// <summary>
        /// F3座標HUDの表示状態を明示的に設定します。キー入力処理とPlayModeテストが同じ入口を使用します。
        /// </summary>
        /// <param name="isVisible">表示する場合はtrue、隠す場合はfalseです。</param>
        public void SetCoordinateHudVisible(bool isVisible)
        {
            isCoordinateHudVisible = isVisible;
        }

        /// <summary>
        /// F4グリッド境界の表示状態を明示的に設定します。ゲーム状態やTilemapデータは変更しません。
        /// </summary>
        /// <param name="isVisible">表示する場合はtrue、隠す場合はfalseです。</param>
        public void SetChunkGridVisible(bool isVisible)
        {
            isChunkGridVisible = isVisible;
        }

        /// <summary>
        /// カメラ中心をGridセルへ変換し、ワールド・タイル・チャンク・チャンク内座標を左上へ表示します。
        /// </summary>
        private void DrawCoordinateHud()
        {
            Vector3 relativeWorldPosition = targetCamera.transform.position;
            Vector3Int relativeTile = targetGrid == null
                ? new Vector3Int(
                    Mathf.FloorToInt(relativeWorldPosition.x),
                    Mathf.FloorToInt(relativeWorldPosition.y),
                    0)
                : targetGrid.WorldToCell(relativeWorldPosition);
            DisplayOrigin displayOrigin = GetCurrentDisplayOrigin();
            Vector3Int tile = new Vector3Int(
                AddDisplayOriginSaturated(
                    relativeTile.x,
                    displayOrigin.AbsoluteTileCoordinate.x),
                AddDisplayOriginSaturated(
                    relativeTile.y,
                    displayOrigin.AbsoluteTileCoordinate.y),
                0);
            double2 absoluteWorldPosition = cameraPanController == null
                ? new double2(relativeWorldPosition.x, relativeWorldPosition.y)
                : cameraPanController.GetAbsoluteWorldPosition();
            bool hasSpawnLocation = TryGetInitialSpawnLocation(out InitialSpawnLocation spawnLocation);
            bool hasSpawnSearchStatus = TryGetInitialSpawnSearchStatus(
                out InitialSpawnSearchStatus spawnSearchStatus);
            bool hasGenerationSettings = TryGetWorldGenerationSettings(
                out WorldGenerationSettings generationSettings);
            bool hasBiome = TryGetCurrentTileBiome(tile, out BiomeId biome);
            string text = BuildCoordinateHudText(
                absoluteWorldPosition,
                tile,
                hasSpawnLocation,
                spawnLocation,
                hasSpawnSearchStatus,
                spawnSearchStatus,
                hasGenerationSettings,
                generationSettings,
                hasBiome,
                biome);

            const int textLineCount = 10;
            const float panelWidth = 420f;
            const float horizontalPadding = 8f;
            const float verticalPadding = 6f;
            const float glyphBottomSafetyPadding = 4f;

            // 固定高では約1行分の空白が残るため、10行分の高さと小さな文字保護余白だけを確保します。
            // 最後の4pxはg・yなどの下へ伸びる字形がパネル枠へ触れないために必要で、空行ではありません。
            float textHeight = Mathf.Ceil(Mathf.Max(1f, coordinateHudStyle.lineHeight) * textLineCount);
            float panelHeight = textHeight + (verticalPadding * 2f) + glyphBottomSafetyPadding;
            Rect panelRect = new Rect(12f, 12f, panelWidth, panelHeight);
            GUI.Box(panelRect, GUIContent.none);
            GUI.Label(
                new Rect(
                    panelRect.x + horizontalPadding,
                    panelRect.y + verticalPadding,
                    panelRect.width - (horizontalPadding * 2f),
                    textHeight + glyphBottomSafetyPadding),
                text,
                coordinateHudStyle);
        }

        /// <summary>
        /// 現在の座標、生成済みBiome、生成互換情報、記録済み初期スポーンをF3表示用文字列へまとめます。
        /// </summary>
        /// <param name="absoluteWorldPosition">現在のカメラ中心の絶対ワールドXY座標です。</param>
        /// <param name="tile">現在のカメラ中心タイル座標です。</param>
        /// <param name="hasSpawnLocation">初期スポーン探索が完了し、記録済み座標を渡せる場合はtrueです。</param>
        /// <param name="spawnLocation">記録済みの初期スポーン座標です。未完了時は表示に使用しません。</param>
        /// <param name="hasSpawnSearchStatus">初期スポーン探索状態Singletonを読み取れた場合はtrueです。</param>
        /// <param name="spawnSearchStatus">現在の探索段階です。失敗時の代替カメラ位置表示に使用します。</param>
        /// <param name="hasGenerationSettings">生成設定Singletonからバージョンとシードを読めた場合はtrueです。</param>
        /// <param name="generationSettings">現在のWorldが保持する生成設定です。未準備時は表示に使用しません。</param>
        /// <param name="hasBiome">現在タイルの生成済みBiomeを読めた場合はtrueです。</param>
        /// <param name="biome">現在タイルの論理Biomeです。未生成時は表示に使用しません。</param>
        /// <returns>見出しを含む10行のF3座標HUD文字列です。</returns>
        public static string BuildCoordinateHudText(
            double2 absoluteWorldPosition,
            Vector3Int tile,
            bool hasSpawnLocation,
            InitialSpawnLocation spawnLocation,
            bool hasSpawnSearchStatus,
            InitialSpawnSearchStatus spawnSearchStatus,
            bool hasGenerationSettings,
            WorldGenerationSettings generationSettings,
            bool hasBiome,
            BiomeId biome)
        {
            ChunkCoordinate chunk = WorldGrid.ToChunkCoordinate(tile.x, tile.y);
            ChunkLocalTileCoordinate local = WorldGrid.ToLocalTileCoordinate(tile.x, tile.y);
            SubChunkCoordinate subChunk = WorldGrid.ToSubChunkCoordinate(local.X, local.Y);
            string biomeText = hasBiome ? biome.ToString() : "Loading...";
            string generatorVersionText = hasGenerationSettings
                ? generationSettings.GeneratorVersion.ToString()
                : "Loading...";
            string worldSeedText = hasGenerationSettings
                ? $"0x{generationSettings.WorldSeed:X16}"
                : "Loading...";
            string spawnText = "Spawn: Searching...";
            if (hasSpawnLocation)
            {
                ChunkCoordinate spawnChunk = WorldGrid.ToChunkCoordinate(
                    spawnLocation.TileX,
                    spawnLocation.TileY);
                spawnText = $"Spawn: Tile ({spawnLocation.TileX}, {spawnLocation.TileY})"
                    + $" / Chunk ({spawnChunk.X}, {spawnChunk.Y})";
            }
            else if (hasSpawnSearchStatus
                && spawnSearchStatus.Phase == InitialSpawnSearchPhase.Failed)
            {
                // 失敗時もストリーミングはScene保存位置から開始するため、探索中と誤認させない文言へ切り替えます。
                spawnText = "Spawn: Failed / Using Scene camera position";
            }

            return $"F3  Coordinates\n"
                + $"World : ({absoluteWorldPosition.x:0.###}, {absoluteWorldPosition.y:0.###})\n"
                + $"Tile  : ({tile.x}, {tile.y})\n"
                + $"Chunk : ({chunk.X}, {chunk.Y})\n"
                + $"Local : ({local.X}, {local.Y})\n"
                + $"SubChunk: ({subChunk.X}, {subChunk.Y})\n"
                + $"Biome: {biomeText}\n"
                + $"GeneratorVersion: {generatorVersionText}\n"
                + $"WorldSeed: {worldSeedText}\n"
                + spawnText;
        }

        /// <summary>
        /// 初期スポーンタイルを含む64×64論理チャンクのワールドタイル範囲を求めます。
        /// 負座標でも床方向除算を共有することで、F3の座標表示とF4の赤枠を同じチャンクへ一致させます。
        /// </summary>
        /// <param name="spawnLocation">ワールド開始時に記録した初期スポーンタイルです。</param>
        /// <returns>左下タイルを原点とする64×64タイルの整数矩形です。</returns>
        public static RectInt CalculateInitialSpawnChunkTileBounds(InitialSpawnLocation spawnLocation)
        {
            ChunkCoordinate spawnChunk = WorldGrid.ToChunkCoordinate(
                spawnLocation.TileX,
                spawnLocation.TileY);
            return new RectInt(
                spawnChunk.X * WorldGridConstants.LogicalChunkSize,
                spawnChunk.Y * WorldGridConstants.LogicalChunkSize,
                WorldGridConstants.LogicalChunkSize,
                WorldGridConstants.LogicalChunkSize);
        }

        /// <summary>
        /// F4のチャンク座標ラベルを作り、初期スポーン所属チャンクだけを文字でも判別できるようにします。
        /// </summary>
        /// <param name="chunkX">表示する水平論理チャンク座標です。</param>
        /// <param name="chunkY">表示する垂直論理チャンク座標です。</param>
        /// <param name="isInitialSpawnChunk">初期スポーンタイルを含むチャンクの場合はtrueです。</param>
        /// <returns>通常または初期スポーン用のチャンク座標ラベルです。</returns>
        public static string BuildChunkCoordinateLabelText(
            int chunkX,
            int chunkY,
            bool isInitialSpawnChunk)
        {
            return isInitialSpawnChunk
                ? $"Spawn Chunk ({chunkX}, {chunkY})"
                : $"Chunk ({chunkX}, {chunkY})";
        }

        /// <summary>
        /// Default Worldから初期スポーンSingletonを読み、F3描画へ渡します。
        /// World未作成、探索中、またはSingleton数が1件でない場合は未準備として扱います。
        /// </summary>
        /// <param name="spawnLocation">成功時に、ワールド開始時から不変の初期スポーン情報を返します。</param>
        /// <returns>初期スポーンSingletonをちょうど1件読み取れた場合はtrueです。</returns>
        private bool TryGetInitialSpawnLocation(out InitialSpawnLocation spawnLocation)
        {
            spawnLocation = default;
            if (!TryEnsureDiagnosticQueries())
            {
                return false;
            }

            if (spawnLocationQuery.CalculateEntityCount() != 1)
            {
                return false;
            }

            spawnLocation = spawnLocationQuery.GetSingleton<InitialSpawnLocation>();
            return true;
        }

        /// <summary>
        /// Default Worldから初期スポーン探索状態Singletonを読み、探索中と失敗をF3で区別できるようにします。
        /// </summary>
        /// <param name="searchStatus">成功時に、探索Systemが記録した現在または最終段階を返します。</param>
        /// <returns>探索状態Singletonをちょうど1件読み取れた場合はtrueです。</returns>
        private bool TryGetInitialSpawnSearchStatus(out InitialSpawnSearchStatus searchStatus)
        {
            searchStatus = default;
            if (!TryEnsureDiagnosticQueries()
                || spawnSearchStatusQuery.CalculateEntityCount() != 1)
            {
                return false;
            }

            searchStatus = spawnSearchStatusQuery.GetSingleton<InitialSpawnSearchStatus>();
            return true;
        }

        /// <summary>
        /// Default Worldから生成設定Singletonを読み、F3へGeneratorVersionとワールドシードを渡します。
        /// </summary>
        /// <param name="settings">成功時に、現在のWorldが保持する決定論的な生成設定を返します。</param>
        /// <returns>生成設定Singletonをちょうど1件読み取れた場合はtrueです。</returns>
        private bool TryGetWorldGenerationSettings(out WorldGenerationSettings settings)
        {
            settings = default;
            if (!TryEnsureDiagnosticQueries()
                || generationSettingsQuery.CalculateEntityCount() != 1)
            {
                return false;
            }

            settings = generationSettingsQuery.GetSingleton<WorldGenerationSettings>();
            return true;
        }

        /// <summary>
        /// 現在タイルを含む生成済みチャンクのGeneratedTile Bufferから、論理Biomeを読み取ります。
        /// 同じ64×64チャンク内ではEntityを再利用し、F3表示中に全チャンク配列を毎フレーム確保しないようにします。
        /// </summary>
        /// <param name="tile">Biomeを調べる符号付きワールドタイル座標です。</param>
        /// <param name="biome">成功時に、Simulationが生成した現在タイルのBiomeを返します。</param>
        /// <returns>生成完了済みの4096タイルBufferからBiomeを読めた場合はtrueです。</returns>
        private bool TryGetCurrentTileBiome(Vector3Int tile, out BiomeId biome)
        {
            biome = default;
            if (!TryEnsureDiagnosticQueries())
            {
                return false;
            }

            ChunkCoordinate requestedChunk = WorldGrid.ToChunkCoordinate(tile.x, tile.y);
            EntityManager entityManager = diagnosticWorld.EntityManager;
            if (!hasCachedGeneratedChunk
                || !cachedGeneratedChunkCoordinate.Equals(requestedChunk)
                || !entityManager.Exists(cachedGeneratedChunkEntity))
            {
                hasCachedGeneratedChunk = false;
                cachedGeneratedChunkEntity = Entity.Null;

                // Cameraが別チャンクへ移動した場合だけ座標配列を作り、対象Entityを次回以降の描画へキャッシュします。
                using NativeArray<Entity> entities = generatedChunkQuery.ToEntityArray(Allocator.Temp);
                using NativeArray<ChunkCoordinateComponent> coordinates =
                    generatedChunkQuery.ToComponentDataArray<ChunkCoordinateComponent>(Allocator.Temp);
                for (int index = 0; index < entities.Length; index++)
                {
                    ChunkCoordinateComponent coordinate = coordinates[index];
                    if (coordinate.X != requestedChunk.X || coordinate.Y != requestedChunk.Y)
                    {
                        continue;
                    }

                    cachedGeneratedChunkEntity = entities[index];
                    cachedGeneratedChunkCoordinate = requestedChunk;
                    hasCachedGeneratedChunk = true;
                    break;
                }
            }

            if (!hasCachedGeneratedChunk)
            {
                return false;
            }

            ChunkGenerationStatus status =
                entityManager.GetComponentData<ChunkGenerationStatus>(cachedGeneratedChunkEntity);
            if (status.Phase != ChunkGenerationPhase.ReadyToApply
                && status.Phase != ChunkGenerationPhase.Active)
            {
                return false;
            }

            DynamicBuffer<GeneratedTile> tiles =
                entityManager.GetBuffer<GeneratedTile>(cachedGeneratedChunkEntity, true);
            if (tiles.Length != WorldGridConstants.TilesPerChunk)
            {
                return false;
            }

            ChunkLocalTileCoordinate local = WorldGrid.ToLocalTileCoordinate(tile.x, tile.y);
            biome = tiles[WorldGrid.ToTileIndex(local.X, local.Y)].Biome;
            return true;
        }

        /// <summary>
        /// Default World向けの初期スポーン、生成設定、生成済みチャンクQueryをまとめて用意します。
        /// Worldが再作成された場合は古いQueryとEntityキャッシュを先に破棄し、別ワールドの診断値を表示しません。
        /// </summary>
        /// <returns>現在のDefault Worldから診断情報を読み取れる場合はtrueです。</returns>
        private bool TryEnsureDiagnosticQueries()
        {
            World currentWorld = World.DefaultGameObjectInjectionWorld;
            if (currentWorld == null || !currentWorld.IsCreated)
            {
                DisposeDiagnosticQueries();
                return false;
            }

            if (diagnosticQueriesAreCreated && diagnosticWorld == currentWorld)
            {
                return true;
            }

            DisposeDiagnosticQueries();
            diagnosticWorld = currentWorld;
            EntityManager entityManager = currentWorld.EntityManager;
            spawnLocationQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<InitialSpawnLocation>());
            spawnSearchStatusQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<InitialSpawnSearchStatus>());
            generationSettingsQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<WorldGenerationSettings>());
            generatedChunkQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ChunkCoordinateComponent>(),
                ComponentType.ReadOnly<ChunkGenerationStatus>(),
                ComponentType.ReadOnly<GeneratedTile>());
            diagnosticQueriesAreCreated = true;
            return true;
        }

        /// <summary>
        /// 診断表示用Queryと所有Worldが有効な場合だけQueryを破棄し、PlayMode終了後に古い参照を残しません。
        /// UnityはWorldをMonoBehaviourより先に破棄する場合があるため、破棄済みEntityManagerへDisposeを要求しないことが重要です。
        /// </summary>
        private void DisposeDiagnosticQueries()
        {
            if (diagnosticQueriesAreCreated
                && diagnosticWorld != null
                && diagnosticWorld.IsCreated)
            {
                spawnLocationQuery.Dispose();
                spawnSearchStatusQuery.Dispose();
                generationSettingsQuery.Dispose();
                generatedChunkQuery.Dispose();
            }

            diagnosticWorld = null;
            spawnLocationQuery = default;
            spawnSearchStatusQuery = default;
            generationSettingsQuery = default;
            generatedChunkQuery = default;
            diagnosticQueriesAreCreated = false;
            cachedGeneratedChunkEntity = Entity.Null;
            cachedGeneratedChunkCoordinate = default;
            hasCachedGeneratedChunk = false;
        }

        /// <summary>
        /// 現在のViewportへタイル・サブチャンク・チャンク境界を階層順に描き、各チャンク中央へ論理座標を表示します。
        /// </summary>
        private void DrawVisibleChunkGrid()
        {
            VisibleGridBounds bounds = CalculateVisibleGridBounds();
            bool hasSpawnLocation = TryGetInitialSpawnLocation(out InitialSpawnLocation spawnLocation);
            ChunkCoordinate spawnChunk = hasSpawnLocation
                ? WorldGrid.ToChunkCoordinate(spawnLocation.TileX, spawnLocation.TileY)
                : default;
            long chunkCountX = (long)bounds.MaximumChunkX - bounds.MinimumChunkX + 1L;
            long chunkCountY = (long)bounds.MaximumChunkY - bounds.MinimumChunkY + 1L;
            long visibleChunkCount = chunkCountX * chunkCountY;

            // 極端なズームアウト時に大量の線と文字を発行するとデバッグ表示自体が重くなるため、安全上限で打ち切ります。
            if (visibleChunkCount <= 0 || visibleChunkCount > MaximumDrawableChunkCount)
            {
                string warning = $"F4  Chunk Grid hidden: {visibleChunkCount} visible chunks";
                GUI.Label(new Rect(12f, 132f, 420f, 28f), warning, coordinateHudStyle);
                return;
            }

            long visibleTileLineCount = ((long)bounds.MaximumTileX - bounds.MinimumTileX + 2L)
                + ((long)bounds.MaximumTileY - bounds.MinimumTileY + 2L);
            long maximumBoundaryTileX = (long)bounds.MaximumTileX + 1L;
            long maximumBoundaryTileY = (long)bounds.MaximumTileY + 1L;

            // 1タイル線は最も本数が増えるため、通常ズームでは描画し、極端なズームアウト時だけ省略します。
            // サブチャンク線とチャンク線は残すので、広域表示でも空間区分を見失いません。
            if (visibleTileLineCount <= MaximumDrawableTileLineCount)
            {
                DrawGridLines(
                    bounds.MinimumTileX,
                    maximumBoundaryTileX,
                    bounds.MinimumTileY,
                    maximumBoundaryTileY,
                    1,
                    tileBoundaryColor,
                    tileBoundaryThicknessInPixels);
            }
            else
            {
                GUI.Label(
                    new Rect(12f, 132f, 420f, 28f),
                    $"F4  Tile Grid hidden at this zoom: {visibleTileLineCount} lines",
                    coordinateHudStyle);
            }

            DrawGridLines(
                bounds.MinimumTileX,
                maximumBoundaryTileX,
                bounds.MinimumTileY,
                maximumBoundaryTileY,
                WorldGridConstants.SubChunkSize,
                subChunkBoundaryColor,
                subChunkBoundaryThicknessInPixels);

            DrawGridLines(
                bounds.MinimumTileX,
                maximumBoundaryTileX,
                bounds.MinimumTileY,
                maximumBoundaryTileY,
                WorldGridConstants.LogicalChunkSize,
                chunkBoundaryColor,
                chunkBoundaryThicknessInPixels);

            // 通常グリッドの後から赤系マーカーを重ね、同じ境界座標でもスポーン側の線を手前に表示します。
            if (hasSpawnLocation)
            {
                DrawInitialSpawnMarkers(spawnLocation);
            }

            for (int chunkY = bounds.MinimumChunkY; chunkY <= bounds.MaximumChunkY; chunkY++)
            {
                for (int chunkX = bounds.MinimumChunkX; chunkX <= bounds.MaximumChunkX; chunkX++)
                {
                    bool isInitialSpawnChunk = hasSpawnLocation
                        && chunkX == spawnChunk.X
                        && chunkY == spawnChunk.Y;
                    DrawChunkCoordinateLabel(chunkX, chunkY, isInitialSpawnChunk);
                }
            }
        }

        /// <summary>
        /// F4表示へ初期スポーンタイルの赤い面・輪郭と、所属チャンクの濃い赤い輪郭を重ねます。
        /// ECSの記録値を読み取るだけで、生成済み地形や初期スポーン自体は変更しません。
        /// </summary>
        /// <param name="spawnLocation">ワールド開始時から不変の初期スポーン情報です。</param>
        private void DrawInitialSpawnMarkers(InitialSpawnLocation spawnLocation)
        {
            RectInt spawnChunkBounds = CalculateInitialSpawnChunkTileBounds(spawnLocation);

            DrawWorldRectangleBoundary(
                spawnChunkBounds.xMin,
                spawnChunkBounds.yMin,
                spawnChunkBounds.xMax,
                spawnChunkBounds.yMax,
                initialSpawnChunkColor,
                initialSpawnChunkThicknessInPixels);

            DrawWorldArea(
                spawnLocation.TileX,
                spawnLocation.TileY,
                spawnLocation.TileX + 1,
                spawnLocation.TileY + 1,
                initialSpawnTileFillColor);
            DrawWorldRectangleBoundary(
                spawnLocation.TileX,
                spawnLocation.TileY,
                spawnLocation.TileX + 1,
                spawnLocation.TileY + 1,
                initialSpawnTileBoundaryColor,
                initialSpawnTileBoundaryThicknessInPixels);
        }

        /// <summary>
        /// ワールドタイル境界で指定した矩形の4辺を描き、タイルとチャンクの赤枠描画を共通化します。
        /// </summary>
        /// <param name="minimumTileX">矩形左端のワールドタイル境界座標です。</param>
        /// <param name="minimumTileY">矩形下端のワールドタイル境界座標です。</param>
        /// <param name="maximumTileX">矩形右端のワールドタイル境界座標です。</param>
        /// <param name="maximumTileY">矩形上端のワールドタイル境界座標です。</param>
        /// <param name="lineColor">4辺へ使用する色です。</param>
        /// <param name="lineThicknessInPixels">画面上で一定に保つ線の太さです。</param>
        private void DrawWorldRectangleBoundary(
            int minimumTileX,
            int minimumTileY,
            int maximumTileX,
            int maximumTileY,
            Color lineColor,
            float lineThicknessInPixels)
        {
            Vector3 bottomLeft = CellBoundaryToWorld(minimumTileX, minimumTileY);
            Vector3 bottomRight = CellBoundaryToWorld(maximumTileX, minimumTileY);
            Vector3 topLeft = CellBoundaryToWorld(minimumTileX, maximumTileY);
            Vector3 topRight = CellBoundaryToWorld(maximumTileX, maximumTileY);

            DrawWorldLine(bottomLeft, bottomRight, lineColor, lineThicknessInPixels);
            DrawWorldLine(bottomRight, topRight, lineColor, lineThicknessInPixels);
            DrawWorldLine(topRight, topLeft, lineColor, lineThicknessInPixels);
            DrawWorldLine(topLeft, bottomLeft, lineColor, lineThicknessInPixels);
        }

        /// <summary>
        /// ワールドタイル境界で指定した軸平行領域を単色で塗り、初期スポーン1タイルの位置を面として示します。
        /// </summary>
        /// <param name="minimumTileX">塗り領域左端のワールドタイル境界座標です。</param>
        /// <param name="minimumTileY">塗り領域下端のワールドタイル境界座標です。</param>
        /// <param name="maximumTileX">塗り領域右端のワールドタイル境界座標です。</param>
        /// <param name="maximumTileY">塗り領域上端のワールドタイル境界座標です。</param>
        /// <param name="fillColor">地形へ重ねる半透明色です。</param>
        private void DrawWorldArea(
            int minimumTileX,
            int minimumTileY,
            int maximumTileX,
            int maximumTileY,
            Color fillColor)
        {
            if (!TryWorldToGuiPoint(
                    CellBoundaryToWorld(minimumTileX, minimumTileY),
                    out Vector2 firstCorner)
                || !TryWorldToGuiPoint(
                    CellBoundaryToWorld(maximumTileX, maximumTileY),
                    out Vector2 oppositeCorner))
            {
                return;
            }

            // IMGUIは左上原点なので、投影後の大小を取り直して正の幅と高さを持つ矩形へ変換します。
            Rect areaRect = new Rect(
                Mathf.Min(firstCorner.x, oppositeCorner.x),
                Mathf.Min(firstCorner.y, oppositeCorner.y),
                Mathf.Abs(oppositeCorner.x - firstCorner.x),
                Mathf.Abs(oppositeCorner.y - firstCorner.y));
            Color previousColor = GUI.color;
            GUI.color = fillColor;
            GUI.DrawTexture(areaRect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        /// <summary>
        /// 指定タイル間隔で縦線と横線を各1回だけ描き、隣接領域の共有辺が重なって濃くなることを防ぎます。
        /// </summary>
        /// <param name="minimumTileX">描画範囲左端の水平タイル境界座標です。int最小タイルの外周を含めるため64bitで受け取ります。</param>
        /// <param name="maximumTileX">描画範囲右端の水平タイル境界座標です。int最大タイルの右辺はint最大値+1になります。</param>
        /// <param name="minimumTileY">描画範囲下端の垂直タイル境界座標です。int最小タイルの外周を含めるため64bitで受け取ります。</param>
        /// <param name="maximumTileY">描画範囲上端の垂直タイル境界座標です。int最大タイルの上辺はint最大値+1になります。</param>
        /// <param name="intervalInTiles">1、16、64のいずれかとなる境界間隔です。</param>
        /// <param name="lineColor">この階層の境界へ使用する色です。</param>
        /// <param name="lineThicknessInPixels">画面上での境界線の太さです。</param>
        private void DrawGridLines(
            long minimumTileX,
            long maximumTileX,
            long minimumTileY,
            long maximumTileY,
            int intervalInTiles,
            Color lineColor,
            float lineThicknessInPixels)
        {
            long firstBoundaryTileX = CalculateFirstGridBoundary(minimumTileX, intervalInTiles);
            long firstBoundaryTileY = CalculateFirstGridBoundary(minimumTileY, intervalInTiles);
            long verticalLineCount = CalculateGridBoundaryCount(
                minimumTileX,
                maximumTileX,
                intervalInTiles);
            long horizontalLineCount = CalculateGridBoundaryCount(
                minimumTileY,
                maximumTileY,
                intervalInTiles);

            // 回数を先に64bitで確定し、最終境界を描いた後の加算がint反対端へ周回する無限ループを防ぎます。
            for (long lineIndex = 0; lineIndex < verticalLineCount; lineIndex++)
            {
                long boundaryTileX = firstBoundaryTileX + (lineIndex * intervalInTiles);
                DrawWorldLine(
                    CellBoundaryToWorld(boundaryTileX, minimumTileY),
                    CellBoundaryToWorld(boundaryTileX, maximumTileY),
                    lineColor,
                    lineThicknessInPixels);
            }

            for (long lineIndex = 0; lineIndex < horizontalLineCount; lineIndex++)
            {
                long boundaryTileY = firstBoundaryTileY + (lineIndex * intervalInTiles);
                DrawWorldLine(
                    CellBoundaryToWorld(minimumTileX, boundaryTileY),
                    CellBoundaryToWorld(maximumTileX, boundaryTileY),
                    lineColor,
                    lineThicknessInPixels);
            }
        }

        /// <summary>
        /// 指定範囲へ一定間隔で描くグリッド境界線の本数を64bitで計算します。
        /// F4の回帰テストからも使用し、int座標端でも有限回で終了することを保証します。
        /// </summary>
        /// <param name="minimumTileBoundary">描画範囲の最小タイル境界です。</param>
        /// <param name="maximumTileBoundary">描画範囲の最大タイル境界です。包含境界として数えます。</param>
        /// <param name="intervalInTiles">境界線同士の間隔です。1以上を指定します。</param>
        /// <returns>最小値以下の直近境界から最大値までに含まれる線数です。無効範囲または間隔なら0です。</returns>
        public static long CalculateGridBoundaryCount(
            long minimumTileBoundary,
            long maximumTileBoundary,
            int intervalInTiles)
        {
            if (intervalInTiles <= 0 || minimumTileBoundary > maximumTileBoundary)
            {
                return 0L;
            }

            long firstBoundary = CalculateFirstGridBoundary(
                minimumTileBoundary,
                intervalInTiles);
            return ((maximumTileBoundary - firstBoundary) / intervalInTiles) + 1L;
        }

        /// <summary>
        /// 負座標でも0方向へ丸めず、指定位置以下で最も近い正規グリッド境界を求めます。
        /// </summary>
        /// <param name="tileBoundaryCoordinate">境界を探す基準となる64bitタイル境界座標です。</param>
        /// <param name="intervalInTiles">1、16、64などの正の境界間隔です。</param>
        /// <returns>指定位置以下にある、間隔の整数倍となる境界座標です。</returns>
        private static long CalculateFirstGridBoundary(
            long tileBoundaryCoordinate,
            int intervalInTiles)
        {
            long quotient = tileBoundaryCoordinate / intervalInTiles;
            long remainder = tileBoundaryCoordinate % intervalInTiles;
            if (remainder < 0L)
            {
                quotient--;
            }

            return quotient * intervalInTiles;
        }

        /// <summary>
        /// 1チャンクの中央を画面座標へ投影し、F4境界に対応する論理チャンク座標を描きます。
        /// </summary>
        /// <param name="chunkX">ラベルを付ける水平論理チャンク座標です。</param>
        /// <param name="chunkY">ラベルを付ける垂直論理チャンク座標です。</param>
        /// <param name="isInitialSpawnChunk">初期スポーン所属チャンクとして赤い専用ラベルを使う場合はtrueです。</param>
        private void DrawChunkCoordinateLabel(int chunkX, int chunkY, bool isInitialSpawnChunk)
        {
            int centerTileX = (chunkX * WorldGridConstants.LogicalChunkSize)
                + (WorldGridConstants.LogicalChunkSize / 2);
            int centerTileY = (chunkY * WorldGridConstants.LogicalChunkSize)
                + (WorldGridConstants.LogicalChunkSize / 2);
            Vector3 centerWorld = CellBoundaryToWorld(centerTileX, centerTileY);

            if (!TryWorldToGuiPoint(centerWorld, out Vector2 centerGui))
            {
                return;
            }

            // 64タイル幅より描画領域が狭い場合はチャンク中央が画面外になります。
            // ラベルだけをWindowbox内へ寄せ、現在見えている部分がどのチャンクか必ず読めるようにします。
            float halfLabelWidth = Mathf.Min(70f, activeGuiViewportRect.width * 0.5f);
            float halfLabelHeight = Mathf.Min(14f, activeGuiViewportRect.height * 0.5f);
            float minimumGuiX = halfLabelWidth;
            float maximumGuiX = activeGuiViewportRect.width - halfLabelWidth;
            float minimumGuiY = halfLabelHeight;
            float maximumGuiY = activeGuiViewportRect.height - halfLabelHeight;
            centerGui.x = Mathf.Clamp(centerGui.x, minimumGuiX, maximumGuiX);
            centerGui.y = Mathf.Clamp(centerGui.y, minimumGuiY, maximumGuiY);

            GUI.Label(
                new Rect(centerGui.x - 70f, centerGui.y - 14f, 140f, 28f),
                BuildChunkCoordinateLabelText(chunkX, chunkY, isInitialSpawnChunk),
                isInitialSpawnChunk ? initialSpawnChunkLabelStyle : chunkLabelStyle);
        }

        /// <summary>
        /// 2つのワールド座標を画面へ投影し、指定色と一定ピクセル幅の境界線を描きます。
        /// </summary>
        /// <param name="startWorld">線の開始ワールド座標です。</param>
        /// <param name="endWorld">線の終了ワールド座標です。</param>
        /// <param name="lineColor">描画する境界階層の色です。</param>
        /// <param name="lineThicknessInPixels">カメラのズームに影響されない画面ピクセル単位の太さです。</param>
        private void DrawWorldLine(
            Vector3 startWorld,
            Vector3 endWorld,
            Color lineColor,
            float lineThicknessInPixels)
        {
            if (!TryWorldToGuiPoint(startWorld, out Vector2 startGui)
                || !TryWorldToGuiPoint(endWorld, out Vector2 endGui))
            {
                return;
            }

            Vector2 difference = endGui - startGui;
            float length = difference.magnitude;
            if (length <= 0f)
            {
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = lineColor;

            if (TryCalculateAxisAlignedLineRect(
                    startGui,
                    endGui,
                    lineThicknessInPixels,
                    out Rect axisAlignedLineRect))
            {
                // F4の縦横グリッドは回転行列を使わず直接描画し、GUI.BeginGroupのWindowboxクリップ内へ確実に残します。
                GUI.DrawTexture(axisAlignedLineRect, Texture2D.whiteTexture);
            }
            else
            {
                // 将来カメラを回転した場合だけ従来の回転描画へ戻し、斜め線にも対応できる余地を残します。
                Matrix4x4 previousMatrix = GUI.matrix;
                float angleInDegrees = Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg;
                GUIUtility.RotateAroundPivot(angleInDegrees, startGui);
                GUI.DrawTexture(
                    new Rect(
                        startGui.x,
                        startGui.y - (lineThicknessInPixels * 0.5f),
                        length,
                        lineThicknessInPixels),
                    Texture2D.whiteTexture);
                GUI.matrix = previousMatrix;
            }

            GUI.color = previousColor;
        }

        /// <summary>
        /// タイルセル境界の整数座標を、GridのTransformとセル寸法を考慮したワールド座標へ変換します。
        /// </summary>
        /// <param name="tileX">水平タイル境界座標です。int最大タイルの右辺となるint最大値+1も受け取れます。</param>
        /// <param name="tileY">垂直タイル境界座標です。int最大タイルの上辺となるint最大値+1も受け取れます。</param>
        /// <returns>地形平面上のワールド座標です。</returns>
        private Vector3 CellBoundaryToWorld(long tileX, long tileY)
        {
            DisplayOrigin displayOrigin = GetCurrentDisplayOrigin();
            RelativeCellCoordinate relativeCell = new RelativeCellCoordinate(
                tileX - displayOrigin.AbsoluteTileCoordinate.x,
                tileY - displayOrigin.AbsoluteTileCoordinate.y);
            if (relativeCell.X < int.MinValue
                || relativeCell.X > int.MaxValue
                || relativeCell.Y < int.MinValue
                || relativeCell.Y > int.MaxValue)
            {
                return new Vector3(float.PositiveInfinity, float.PositiveInfinity, 0f);
            }

            return targetGrid == null
                ? new Vector3((int)relativeCell.X, (int)relativeCell.Y, 0f)
                : targetGrid.CellToWorld(
                    new Vector3Int((int)relativeCell.X, (int)relativeCell.Y, 0));
        }

        /// <summary>
        /// ワールド座標をカメラViewportへ投影し、Windowbox内を左上原点とするIMGUIローカル座標へ変換します。
        /// </summary>
        /// <param name="worldPoint">投影するワールド座標です。</param>
        /// <param name="guiPoint">成功時にWindowbox左上を原点とするローカルピクセル座標を返します。</param>
        /// <returns>カメラ前方へ投影できた場合はtrueです。</returns>
        private bool TryWorldToGuiPoint(Vector3 worldPoint, out Vector2 guiPoint)
        {
            // 表示原点からint範囲を超えて離れた診断マーカーは画面外なので、InfinityをCamera APIへ渡さず省略します。
            if (!float.IsFinite(worldPoint.x)
                || !float.IsFinite(worldPoint.y)
                || !float.IsFinite(worldPoint.z))
            {
                guiPoint = default;
                return false;
            }

            Vector3 viewportPoint = targetCamera.WorldToViewportPoint(worldPoint);
            if (!float.IsFinite(viewportPoint.x)
                || !float.IsFinite(viewportPoint.y)
                || !float.IsFinite(viewportPoint.z)
                || viewportPoint.z <= 0f)
            {
                guiPoint = default;
                return false;
            }

            // Viewportの0～1を最終描画領域へ拡大し、YだけをIMGUIの上向き原点から下向き原点へ反転します。
            guiPoint = new Vector2(
                viewportPoint.x * activeGuiViewportRect.width,
                (1f - viewportPoint.y) * activeGuiViewportRect.height);
            return true;
        }

        /// <summary>
        /// カメラViewportの左下と右上から、少しでも画面へ含まれるタイルと論理チャンクの包含矩形を求めます。
        /// </summary>
        /// <returns>負座標でも床方向除算された可視タイル・チャンク範囲です。</returns>
        private VisibleGridBounds CalculateVisibleGridBounds()
        {
            float distanceToGridPlane = targetGrid == null
                ? Mathf.Abs(targetCamera.transform.position.z)
                : Mathf.Abs(targetCamera.transform.position.z - targetGrid.transform.position.z);
            Vector3 bottomLeftWorld = targetCamera.ViewportToWorldPoint(
                new Vector3(0f, 0f, distanceToGridPlane));
            Vector3 topRightWorld = targetCamera.ViewportToWorldPoint(
                new Vector3(1f, 1f, distanceToGridPlane));

            int minimumTileX;
            int maximumTileX;
            int minimumTileY;
            int maximumTileY;

            if (targetGrid != null)
            {
                Vector3Int bottomLeftCell = targetGrid.WorldToCell(bottomLeftWorld);
                Vector3Int topRightCell = targetGrid.WorldToCell(topRightWorld);
                minimumTileX = Mathf.Min(bottomLeftCell.x, topRightCell.x);
                maximumTileX = Mathf.Max(bottomLeftCell.x, topRightCell.x);
                minimumTileY = Mathf.Min(bottomLeftCell.y, topRightCell.y);
                maximumTileY = Mathf.Max(bottomLeftCell.y, topRightCell.y);
            }
            else
            {
                minimumTileX = Mathf.FloorToInt(Mathf.Min(bottomLeftWorld.x, topRightWorld.x));
                maximumTileX = Mathf.CeilToInt(Mathf.Max(bottomLeftWorld.x, topRightWorld.x)) - 1;
                minimumTileY = Mathf.FloorToInt(Mathf.Min(bottomLeftWorld.y, topRightWorld.y));
                maximumTileY = Mathf.CeilToInt(Mathf.Max(bottomLeftWorld.y, topRightWorld.y)) - 1;
            }

            DisplayOrigin displayOrigin = GetCurrentDisplayOrigin();
            minimumTileX = AddDisplayOriginSaturated(
                minimumTileX,
                displayOrigin.AbsoluteTileCoordinate.x);
            maximumTileX = AddDisplayOriginSaturated(
                maximumTileX,
                displayOrigin.AbsoluteTileCoordinate.x);
            minimumTileY = AddDisplayOriginSaturated(
                minimumTileY,
                displayOrigin.AbsoluteTileCoordinate.y);
            maximumTileY = AddDisplayOriginSaturated(
                maximumTileY,
                displayOrigin.AbsoluteTileCoordinate.y);

            ChunkCoordinate minimumChunk = WorldGrid.ToChunkCoordinate(minimumTileX, minimumTileY);
            ChunkCoordinate maximumChunk = WorldGrid.ToChunkCoordinate(maximumTileX, maximumTileY);
            return new VisibleGridBounds(
                minimumTileX,
                maximumTileX,
                minimumTileY,
                maximumTileY,
                minimumChunk.X,
                maximumChunk.X,
                minimumChunk.Y,
                maximumChunk.Y);
        }

        /// <summary>
        /// 現在のCameraが共有する表示原点を返します。旧SceneでControllerがない場合だけ絶対原点へフォールバックします。
        /// </summary>
        /// <returns>ローカルセル(0, 0)へ対応する絶対タイル座標です。</returns>
        private DisplayOrigin GetCurrentDisplayOrigin()
        {
            return cameraPanController == null
                ? new DisplayOrigin(int2.zero)
                : cameraPanController.CurrentDisplayOrigin;
        }

        /// <summary>
        /// 表示用の相対タイルへ1軸分の表示原点を加え、int世界端では飽和した絶対座標を返します。
        /// </summary>
        /// <param name="relativeTileCoordinate">表示原点から数えた相対タイル座標です。</param>
        /// <param name="displayOriginCoordinate">同じ軸の絶対表示原点です。</param>
        /// <returns>F3・F4が参照する絶対タイル座標です。</returns>
        private static int AddDisplayOriginSaturated(
            int relativeTileCoordinate,
            int displayOriginCoordinate)
        {
            long absoluteCoordinate = (long)relativeTileCoordinate + displayOriginCoordinate;
            if (absoluteCoordinate < int.MinValue)
            {
                return int.MinValue;
            }

            if (absoluteCoordinate > int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)absoluteCoordinate;
        }

        /// <summary>
        /// F3とF4の操作を画面左下へ表示し、デバッグ表示を閉じても再表示方法が分かるようにします。
        /// </summary>
        private void DrawShortcutHint()
        {
            GUI.Label(
                new Rect(12f, activeGuiViewportRect.height - 30f, 296f, 22f),
                "F3: Coordinates    F4: Tile / SubChunk / Chunk Grid",
                shortcutHintStyle);
        }

        /// <summary>
        /// IMGUI Styleを初回描画時だけ作成し、OnGUIが複数回呼ばれても毎回newしないようにします。
        /// </summary>
        private void EnsureGuiStyles()
        {
            if (coordinateHudStyle != null)
            {
                return;
            }

            coordinateHudStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                alignment = TextAnchor.UpperLeft,
                wordWrap = false
            };
            coordinateHudStyle.normal.textColor = Color.white;

            chunkLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };
            chunkLabelStyle.normal.textColor = chunkBoundaryColor;

            initialSpawnChunkLabelStyle = new GUIStyle(chunkLabelStyle);
            initialSpawnChunkLabelStyle.normal.textColor = initialSpawnChunkColor;

            shortcutHintStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };
            shortcutHintStyle.normal.textColor = Color.white;
        }

        /// <summary>
        /// OnGUIの1回の描画で扱う、タイル座標と論理チャンク座標の包含矩形です。
        /// 表示用の一時値であり、ECSやセーブデータへは保持しません。
        /// </summary>
        private readonly struct VisibleGridBounds
        {
            /// <summary>画面へ少しでも含まれる最小水平タイル座標です。</summary>
            public readonly int MinimumTileX;

            /// <summary>画面へ少しでも含まれる最大水平タイル座標です。</summary>
            public readonly int MaximumTileX;

            /// <summary>画面へ少しでも含まれる最小垂直タイル座標です。</summary>
            public readonly int MinimumTileY;

            /// <summary>画面へ少しでも含まれる最大垂直タイル座標です。</summary>
            public readonly int MaximumTileY;

            /// <summary>表示する最小水平論理チャンク座標です。</summary>
            public readonly int MinimumChunkX;

            /// <summary>表示する最大水平論理チャンク座標です。</summary>
            public readonly int MaximumChunkX;

            /// <summary>表示する最小垂直論理チャンク座標です。</summary>
            public readonly int MinimumChunkY;

            /// <summary>表示する最大垂直論理チャンク座標です。</summary>
            public readonly int MaximumChunkY;

            /// <summary>
            /// タイルとチャンクの包含座標から、1回のIMGUI描画で使用する可視範囲を作ります。
            /// </summary>
            /// <param name="minimumTileX">最小水平タイル座標です。</param>
            /// <param name="maximumTileX">最大水平タイル座標です。</param>
            /// <param name="minimumTileY">最小垂直タイル座標です。</param>
            /// <param name="maximumTileY">最大垂直タイル座標です。</param>
            /// <param name="minimumChunkX">最小水平論理チャンク座標です。</param>
            /// <param name="maximumChunkX">最大水平論理チャンク座標です。</param>
            /// <param name="minimumChunkY">最小垂直論理チャンク座標です。</param>
            /// <param name="maximumChunkY">最大垂直論理チャンク座標です。</param>
            public VisibleGridBounds(
                int minimumTileX,
                int maximumTileX,
                int minimumTileY,
                int maximumTileY,
                int minimumChunkX,
                int maximumChunkX,
                int minimumChunkY,
                int maximumChunkY)
            {
                MinimumTileX = minimumTileX;
                MaximumTileX = maximumTileX;
                MinimumTileY = minimumTileY;
                MaximumTileY = maximumTileY;
                MinimumChunkX = minimumChunkX;
                MaximumChunkX = maximumChunkX;
                MinimumChunkY = minimumChunkY;
                MaximumChunkY = maximumChunkY;
            }
        }
    }
}
