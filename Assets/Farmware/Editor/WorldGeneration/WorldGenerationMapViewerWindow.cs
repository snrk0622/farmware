// このファイルは、MinecraftのBiome Finderに相当するFarmware開発者用の広域マップEditorWindowを提供します。
// UI Toolkitでシード・座標・ズームを操作し、実ゲームと同じAuthoring設定とDeterministicChunkGeneratorの結果を表示します。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 無限ワールドの任意範囲を、ドラッグとホイールで素早く確認する開発者専用EditorWindowです。
    /// 表示用の独自バイオーム判定は持たず、Bakerと共有する設定値で実際のGeneratorを呼びます。
    /// </summary>
    public sealed class WorldGenerationMapViewerWindow : EditorWindow
    {
        /// <summary>UI Toolkitレイアウトを保持するUXMLのプロジェクト相対パスです。</summary>
        private const string LayoutAssetPath =
            "Assets/Farmware/Editor/WorldGeneration/WorldGenerationMapViewerWindow.uxml";

        /// <summary>ズームイン側の最小値で、1画面ピクセルを1ワールドタイルとして表示します。</summary>
        private const int MinimumTilesPerPixel = 1;

        /// <summary>ズームアウト側の上限で、768px幅なら約196,608タイルを一度に確認できます。</summary>
        private const int MaximumTilesPerPixel = 256;

        /// <summary>Window連続リサイズが止まってから再生成するまでの待機時間です。単位はミリ秒です。</summary>
        private const long ResizeRenderDelayMilliseconds = 150;

        /// <summary>Toolbarの表示名とenumを対応させる固定選択肢です。</summary>
        private static readonly List<string> ViewModeChoices = new List<string>
        {
            "Terrain",
            "Biome"
        };

        /// <summary>ドメインリロード後も維持する現在の水平中心タイル座標です。</summary>
        [SerializeField] private int serializedCenterTileX;

        /// <summary>ドメインリロード後も維持する現在の垂直中心タイル座標です。</summary>
        [SerializeField] private int serializedCenterTileY;

        /// <summary>ドメインリロード後も維持する1ピクセルあたりのタイル数です。</summary>
        [SerializeField] private int serializedTilesPerPixel = 4;

        /// <summary>ドメインリロード後も維持する最終地形・論理バイオーム表示モードです。</summary>
        [SerializeField] private WorldGenerationMapViewMode serializedViewMode =
            WorldGenerationMapViewMode.Terrain;

        /// <summary>シード入力欄です。編集されるまでSettings内の実WorldSeedをそのまま使用します。</summary>
        private TextField seedField;

        /// <summary>座標ジャンプと現在中心表示に使う水平タイル入力欄です。</summary>
        private IntegerField centerXField;

        /// <summary>座標ジャンプと現在中心表示に使う垂直タイル入力欄です。</summary>
        private IntegerField centerYField;

        /// <summary>TerrainとBiomeを切り替える選択欄です。</summary>
        private DropdownField viewModeField;

        /// <summary>チャンク・サブチャンク境界を表示するToggleです。</summary>
        private Toggle gridToggle;

        /// <summary>マップTextureと操作イベントを担当するカスタムVisualElementです。</summary>
        private WorldGenerationMapCanvas mapCanvas;

        /// <summary>カーソル下のタイル座標、バイオーム、最終地形を表示するLabelです。</summary>
        private Label hoverLabel;

        /// <summary>設定取得元、GeneratorVersion、実WorldSeedを表示するLabelです。</summary>
        private Label sourceLabel;

        /// <summary>現在の中心、ズーム、概算表示範囲を表示するLabelです。</summary>
        private Label zoomLabel;

        /// <summary>選択中モードの色凡例を保持するContainerです。</summary>
        private VisualElement legendContainer;

        /// <summary>生成中、完了時間、エラーを画面下部へ表示するLabelです。</summary>
        private Label statusLabel;

        /// <summary>Baker入力またはPlayMode ECSから取得した、独自値を含まない基準設定です。</summary>
        private WorldGenerationSettings sourceSettings;

        /// <summary>基準設定を取得でき、地図を生成してよい場合はtrueです。</summary>
        private bool hasSourceSettings;

        /// <summary>Seed欄を開発者が変更し、基準設定のWorldSeedだけを差し替える場合はtrueです。</summary>
        private bool seedWasEdited;

        /// <summary>設定を取得したSceneまたはPlayMode Singletonを説明する文字列です。</summary>
        private string settingsSourceDescription;

        /// <summary>現在Canvasに表示されているTextureの座標・解像度情報です。</summary>
        private WorldGenerationMapSampleResult displayedSample;

        /// <summary>現在Canvasに表示されているTextureを生成した実設定です。</summary>
        private WorldGenerationSettings displayedSettings;

        /// <summary>EditorWindowが所有し、次の生成完了またはWindow終了時に破棄するTextureです。</summary>
        private Texture2D displayedTexture;

        /// <summary>新しい移動・ズーム操作が入ったとき、不要になったバックグラウンド走査を停止します。</summary>
        private CancellationTokenSource renderCancellation;

        /// <summary>非同期結果の到着順が逆転しても古いTextureを適用しないための単調増加IDです。</summary>
        private int renderRequestId;

        /// <summary>最初のGeometryChangedEventでだけ自動生成を始めるためのフラグです。</summary>
        private bool initialRenderRequested;

        /// <summary>Windowリサイズ中の連続生成を避け、最後のサイズだけを反映する遅延実行項目です。</summary>
        private IVisualElementScheduledItem resizeRenderSchedule;

        /// <summary>カーソルが同じタイル内を動くたびに再生成しないため、最後に表示した水平座標を保持します。</summary>
        private int lastHoveredTileX = int.MinValue;

        /// <summary>カーソルが同じタイル内を動くたびに再生成しないため、最後に表示した垂直座標を保持します。</summary>
        private int lastHoveredTileY = int.MinValue;

        /// <summary>
        /// Farmwareメニューから広域マップビューアを開き、既存Windowがあれば再利用します。
        /// </summary>
        [MenuItem("Farmware/World Generation/Map Viewer", false, 110)]
        public static void OpenWindow()
        {
            WorldGenerationMapViewerWindow window =
                GetWindow<WorldGenerationMapViewerWindow>("Farmware Map Viewer");
            window.minSize = new Vector2(720f, 480f);
            window.Show();
        }

        /// <summary>
        /// EditorWindowが有効になったとき、PlayMode切替を監視して設定取得元を更新できるようにします。
        /// </summary>
        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// EditorWindowが閉じられたとき、バックグラウンド処理、イベント購読、生成Textureを確実に破棄します。
        /// </summary>
        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            resizeRenderSchedule?.Pause();
            resizeRenderSchedule = null;
            CancelPendingRender();
            DestroyDisplayedTexture();
        }

        /// <summary>
        /// UI ToolkitがWindowのVisual Treeを作るときに呼ばれ、UXML読込、要素参照、イベント接続を行います。
        /// </summary>
        public void CreateGUI()
        {
            // Unityのスクリプト再コンパイル時はEditorWindowの一部フィールドが一時復元されるため、
            // 破棄済みTextureに対応する「初期生成済み」状態を必ず解除します。
            initialRenderRequested = false;
            resizeRenderSchedule?.Pause();
            resizeRenderSchedule = null;
            rootVisualElement.Clear();
            VisualTreeAsset layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LayoutAssetPath);
            if (layout == null)
            {
                rootVisualElement.Add(new HelpBox(
                    $"Map ViewerのUXMLを読み込めません: {LayoutAssetPath}",
                    HelpBoxMessageType.Error));
                return;
            }

            layout.CloneTree(rootVisualElement);
            CacheVisualElements();
            ConfigureControls();
            ReloadActualSettings(false);

            // CreateGUI直後はまだcontentRectが0の場合があるため、最初のレイアウト完了後にも生成要求を確認します。
            // GeometryChangedEventだけへ依存すると、ドメインリロード後に既存Windowサイズが復元済みの場合へ通知されないことがあります。
            mapCanvas.schedule.Execute(RequestInitialRenderAfterLayout);
        }

        /// <summary>
        /// UXML内の必須要素を型とnameで取得します。
        /// 欠落時は即座に例外とし、操作不能なWindowを静かに表示し続けないようにします。
        /// </summary>
        private void CacheVisualElements()
        {
            seedField = RequireElement<TextField>("seedField");
            centerXField = RequireElement<IntegerField>("centerXField");
            centerYField = RequireElement<IntegerField>("centerYField");
            viewModeField = RequireElement<DropdownField>("viewModeField");
            gridToggle = RequireElement<Toggle>("gridToggle");
            hoverLabel = RequireElement<Label>("hoverLabel");
            sourceLabel = RequireElement<Label>("sourceLabel");
            zoomLabel = RequireElement<Label>("zoomLabel");
            legendContainer = RequireElement<VisualElement>("legendContainer");
            statusLabel = RequireElement<Label>("statusLabel");

            VisualElement mapHost = RequireElement<VisualElement>("mapHost");
            mapCanvas = new WorldGenerationMapCanvas();
            mapHost.Add(mapCanvas);
        }

        /// <summary>
        /// 入力欄の初期値、Tooltip、ButtonとPointerイベントを設定します。
        /// </summary>
        private void ConfigureControls()
        {
            centerXField.SetValueWithoutNotify(serializedCenterTileX);
            centerYField.SetValueWithoutNotify(serializedCenterTileY);
            viewModeField.choices = ViewModeChoices;
            viewModeField.SetValueWithoutNotify(ViewModeChoices[(int)serializedViewMode]);

            seedField.tooltip =
                "編集しなければ実ゲームのWorldSeedをそのまま使用します。編集後は同じAuthoring設定で別シードを比較します。";
            centerXField.tooltip = "移動先の水平ワールドタイル座標です。負座標も入力できます。";
            centerYField.tooltip = "移動先の垂直ワールドタイル座標です。負座標も入力できます。";
            viewModeField.tooltip =
                "Terrainは最終地形、Biomeは水域や障害物の下も含む論理バイオームを表示します。";
            gridToggle.tooltip = "白線は16タイル、青線は64タイル境界です。遠距離では自動的に省略します。";

            seedField.RegisterValueChangedCallback(_ => seedWasEdited = true);
            viewModeField.RegisterValueChangedCallback(OnViewModeChanged);
            gridToggle.RegisterValueChangedCallback(
                evt => mapCanvas.SetGridVisible(evt.newValue));

            RequireElement<Button>("goButton").clicked += RequestRenderFromFields;
            RequireElement<Button>("reloadSettingsButton").clicked +=
                () => ReloadActualSettings(true);
            RequireElement<Button>("originButton").clicked += MoveToOrigin;
            RequireElement<Button>("zoomInButton").clicked += () => ZoomAtCenter(false);
            RequireElement<Button>("zoomOutButton").clicked += () => ZoomAtCenter(true);

            mapCanvas.PanCompleted = OnMapPanCompleted;
            mapCanvas.ZoomRequested = OnMapZoomRequested;
            mapCanvas.PointerMoved = UpdateHoverInformation;
            mapCanvas.SetGridVisible(gridToggle.value);
            mapCanvas.RegisterCallback<GeometryChangedEvent>(OnMapGeometryChanged);
            resizeRenderSchedule = mapCanvas.schedule.Execute(RequestRenderAfterResize);
            resizeRenderSchedule.Pause();
            UpdateLegend();
        }

        /// <summary>
        /// nameに一致する必須UI要素を取得し、UXMLとC#の不一致を明確な例外にします。
        /// </summary>
        /// <typeparam name="T">期待するVisualElement派生型です。</typeparam>
        /// <param name="elementName">UXMLのname属性です。</param>
        /// <returns>見つかった要素です。</returns>
        private T RequireElement<T>(string elementName) where T : VisualElement
        {
            T element = rootVisualElement.Q<T>(elementName);
            if (element == null)
            {
                throw new InvalidOperationException(
                    $"Map Viewer UXMLに必須要素がありません: {elementName} ({typeof(T).Name})");
            }

            return element;
        }

        /// <summary>
        /// Canvasの初回サイズ確定後に自動生成し、以後のWindowリサイズ後は縦横比に合うTextureを再生成します。
        /// </summary>
        /// <param name="evt">変更前後のCanvas矩形を含むイベントです。</param>
        private void OnMapGeometryChanged(GeometryChangedEvent evt)
        {
            if (!initialRenderRequested)
            {
                RequestInitialRenderAfterLayout();
                return;
            }

            if (mapCanvas.contentRect.width < 64f
                || mapCanvas.contentRect.height < 64f
                || (displayedSample != null
                    && WorldGenerationMapSampler.DoesSampleResolutionMatchViewport(
                        displayedSample.Width,
                        displayedSample.Height,
                        mapCanvas.contentRect.width,
                        mapCanvas.contentRect.height)))
            {
                // リサイズを元の比率へ戻した場合は、直前のGeometryChangedEventで予約した不要な再生成を取り消します。
                resizeRenderSchedule?.Pause();
                return;
            }

            // GeometryChangedEventはWindowをドラッグ中に連続して発生するため、
            // 先の予約を停止し、操作が落ち着いた後の最終サイズだけを生成します。
            resizeRenderSchedule?.ExecuteLater(ResizeRenderDelayMilliseconds);
        }

        /// <summary>
        /// Windowリサイズが止まった後に呼ばれ、現在のCanvas縦横比で地図を1回だけ再生成します。
        /// </summary>
        private void RequestRenderAfterResize()
        {
            if (!hasSourceSettings
                || mapCanvas == null
                || mapCanvas.contentRect.width < 64f
                || mapCanvas.contentRect.height < 64f)
            {
                return;
            }

            RequestRenderFromFields();
        }

        /// <summary>
        /// Canvasの実寸が利用可能になった最初のタイミングで、初期地図を1回だけ生成します。
        /// GeometryChangedEventとUI Toolkit Schedulerの両方から呼び、Windowの新規表示とドメインリロード復元を同じ経路で扱います。
        /// </summary>
        private void RequestInitialRenderAfterLayout()
        {
            if (initialRenderRequested
                || mapCanvas.contentRect.width < 64f
                || mapCanvas.contentRect.height < 64f)
            {
                return;
            }

            initialRenderRequested = true;
            RequestRenderFromFields();
        }

        /// <summary>
        /// PlayModeへ入る・出るタイミングで、ECS SingletonまたはAuthoring設定を再取得します。
        /// </summary>
        /// <param name="state">Unity Editorが通知したPlayMode遷移状態です。</param>
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode
                || state == PlayModeStateChange.EnteredEditMode)
            {
                ReloadActualSettings(true);
            }
        }

        /// <summary>
        /// PlayMode ECSまたは設定SubSceneを読み直し、Seed欄と取得元表示を実際の値へ戻します。
        /// </summary>
        /// <param name="requestRender">取得成功後に地図も再生成する場合はtrueです。</param>
        private void ReloadActualSettings(bool requestRender)
        {
            if (!WorldGenerationEditorSettingsProvider.TryLoad(
                out WorldGenerationEditorSettingsSnapshot snapshot,
                out string errorMessage))
            {
                hasSourceSettings = false;
                sourceLabel.text = "生成設定: 取得失敗";
                SetStatus(errorMessage, true);
                return;
            }

            sourceSettings = snapshot.Settings;
            settingsSourceDescription = snapshot.SourceDescription;
            hasSourceSettings = true;
            seedWasEdited = false;

            string seedDisplay = snapshot.SeedText
                ?? $"<runtime WorldSeed 0x{snapshot.Settings.WorldSeed:X16}>";
            seedField.SetValueWithoutNotify(seedDisplay);
            UpdateSourceInformation();
            SetStatus("実ゲームと共有する生成設定を読み込みました。", false);

            if (requestRender && mapCanvas.contentRect.width >= 64f)
            {
                RequestRenderFromFields();
            }
        }

        /// <summary>
        /// Seed欄と座標欄を読み、現在のモードでバックグラウンド生成を開始します。
        /// </summary>
        private void RequestRenderFromFields()
        {
            if (!hasSourceSettings)
            {
                SetStatus("生成設定がないため地図を作れません。実設定を再読込してください。", true);
                return;
            }

            serializedCenterTileX = centerXField.value;
            serializedCenterTileY = centerYField.value;
            serializedViewMode = GetSelectedViewMode();
            _ = RenderMapAsync();
        }

        /// <summary>
        /// 地形走査をThreadPoolで実行し、完了した色配列だけをEditorメインスレッドでTexture2Dへ変換します。
        /// 連続操作では前のTokenをキャンセルし、古い範囲の計算とTexture適用を破棄します。
        /// </summary>
        /// <returns>完了またはキャンセルまでを表すTaskです。例外は内部でEditorへ表示します。</returns>
        private async Task RenderMapAsync()
        {
            CancelPendingRender();
            renderCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = renderCancellation.Token;
            int requestId = ++renderRequestId;

            WorldGenerationMapSampler.CalculateSampleResolution(
                mapCanvas.contentRect.width,
                mapCanvas.contentRect.height,
                out int width,
                out int height);
            int centerTileX = serializedCenterTileX;
            int centerTileY = serializedCenterTileY;
            int tilesPerPixel = serializedTilesPerPixel;
            WorldGenerationMapViewMode viewMode = serializedViewMode;
            WorldGenerationSettings renderSettings = sourceSettings;
            if (seedWasEdited)
            {
                renderSettings.WorldSeed = WorldSeedUtility.FromString(seedField.value);
            }

            int horizontalTileSpan = width * tilesPerPixel;
            int verticalTileSpan = height * tilesPerPixel;
            SetStatus(
                $"生成中… {width}×{height} samples / 約{horizontalTileSpan:N0}×{verticalTileSpan:N0} tiles",
                false);
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                WorldGenerationMapSampleResult result = await Task.Run(
                    () => WorldGenerationMapSampler.Sample(
                        renderSettings,
                        centerTileX,
                        centerTileY,
                        width,
                        height,
                        tilesPerPixel,
                        viewMode,
                        cancellationToken),
                    cancellationToken);

                if (cancellationToken.IsCancellationRequested
                    || requestId != renderRequestId
                    || this == null)
                {
                    return;
                }

                stopwatch.Stop();
                ApplyRenderedMap(result, renderSettings);
                SetStatus(
                    $"完了: {result.Width * result.Height:N0} samples / {stopwatch.Elapsed.TotalSeconds:F2}秒",
                    false);
            }
            catch (OperationCanceledException)
            {
                // 新しいパン・ズーム要求による通常の中断なので、Consoleへ例外を残しません。
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                SetStatus($"地図生成に失敗しました: {exception.Message}", true);
            }
        }

        /// <summary>
        /// バックグラウンドで完成した色配列をTexture2Dへ適用し、古いTextureを破棄します。
        /// UnityEngine.Objectの作成・破棄は必ずEditorメインスレッドのこのメソッドで行います。
        /// </summary>
        /// <param name="result">色と表示座標を含むサンプリング結果です。</param>
        /// <param name="renderSettings">結果生成時に使用した設定値です。</param>
        private void ApplyRenderedMap(
            WorldGenerationMapSampleResult result,
            WorldGenerationSettings renderSettings)
        {
            DestroyDisplayedTexture();
            displayedTexture = new Texture2D(
                result.Width,
                result.Height,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "Farmware Map Viewer Preview",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            displayedTexture.SetPixels32(result.PixelColors);
            displayedTexture.Apply(false, true);

            displayedSample = result;
            displayedSettings = renderSettings;
            lastHoveredTileX = int.MinValue;
            lastHoveredTileY = int.MinValue;
            mapCanvas.SetMapTexture(
                displayedTexture,
                result.CenterTileX,
                result.CenterTileY,
                result.TilesPerPixel);

            // 再生成前のカーソル情報を残すと、地図と異なる座標・地形を表示して誤認させます。
            // 偶数解像度のCanvas中央は2サンプルの境界になるためPointer変換へ通さず、
            // Side Panelの「中心」と同じ生成結果の中心タイルを直接評価します。
            UpdateHoverInformationAtWorldTile(result.CenterTileX, result.CenterTileY);
            UpdateZoomInformation();
            UpdateSourceInformation();
        }

        /// <summary>
        /// ドラッグした画面量をワールド座標へ変換し、地図をつかんで動かす向きに中心を更新します。
        /// </summary>
        /// <param name="screenOffset">ドラッグ開始から終了までの画面ピクセル差です。</param>
        private void OnMapPanCompleted(Vector2 screenOffset)
        {
            if (displayedSample == null)
            {
                return;
            }

            long horizontalTileOffset =
                WorldGenerationMapSampler.CalculateTileOffsetFromViewportDelta(
                    screenOffset.x,
                    mapCanvas.contentRect.width,
                    displayedSample.Width,
                    displayedSample.TilesPerPixel);
            long verticalTileOffset =
                WorldGenerationMapSampler.CalculateTileOffsetFromViewportDelta(
                    screenOffset.y,
                    mapCanvas.contentRect.height,
                    displayedSample.Height,
                    displayedSample.TilesPerPixel);
            serializedCenterTileX = ClampCoordinate(
                (long)displayedSample.CenterTileX - horizontalTileOffset);
            serializedCenterTileY = ClampCoordinate(
                (long)displayedSample.CenterTileY + verticalTileOffset);
            UpdateCoordinateFields();
            RequestRenderFromFields();
        }

        /// <summary>
        /// ホイール方向を2倍刻みのズームへ変換し、カーソル下のワールド座標を固定したまま中心を補正します。
        /// </summary>
        /// <param name="wheelDeltaY">正ならズームアウト、負ならズームインするホイール量です。</param>
        /// <param name="localPointerPosition">Canvas左上を原点とするカーソル位置です。</param>
        private void OnMapZoomRequested(float wheelDeltaY, Vector2 localPointerPosition)
        {
            if (Mathf.Approximately(wheelDeltaY, 0f))
            {
                return;
            }

            ChangeZoom(wheelDeltaY > 0f, localPointerPosition);
        }

        /// <summary>
        /// ＋・－Buttonから、現在の中心タイル座標を変えずにズームを実行します。
        /// Canvas中央は偶数解像度では2つの表示サンプルの境界になるため、Pointer座標として再解釈しません。
        /// </summary>
        /// <param name="zoomOut">広域側へ縮小する場合はtrueです。</param>
        private void ZoomAtCenter(bool zoomOut)
        {
            int currentCenterX = displayedSample?.CenterTileX ?? serializedCenterTileX;
            int currentCenterY = displayedSample?.CenterTileY ?? serializedCenterTileY;
            int currentTilesPerPixel = displayedSample?.TilesPerPixel ?? serializedTilesPerPixel;
            CalculateToolbarZoomState(
                currentCenterX,
                currentCenterY,
                currentTilesPerPixel,
                zoomOut,
                out int nextCenterX,
                out int nextCenterY,
                out int nextTilesPerPixel);
            if (nextTilesPerPixel == currentTilesPerPixel)
            {
                return;
            }

            serializedCenterTileX = nextCenterX;
            serializedCenterTileY = nextCenterY;
            serializedTilesPerPixel = nextTilesPerPixel;
            UpdateCoordinateFields();
            RequestRenderFromFields();
        }

        /// <summary>
        /// ツールバーの中央ズーム後の中心座標と縮尺を、Editor状態へ依存しない純粋計算で求めます。
        /// 偶数解像度のCanvas中央を離散ピクセルへ変換すると片側へ寄るため、中心座標は入力値をそのまま返します。
        /// </summary>
        /// <param name="currentCenterX">変更前の水平中心タイル座標です。</param>
        /// <param name="currentCenterY">変更前の垂直中心タイル座標です。</param>
        /// <param name="currentTilesPerPixel">変更前の1サンプルあたりタイル数です。</param>
        /// <param name="zoomOut">広域側へ1段階変更する場合はtrueです。</param>
        /// <param name="nextCenterX">変更後も維持する水平中心タイル座標です。</param>
        /// <param name="nextCenterY">変更後も維持する垂直中心タイル座標です。</param>
        /// <param name="nextTilesPerPixel">上下限内で2倍または半分にした縮尺です。</param>
        private static void CalculateToolbarZoomState(
            int currentCenterX,
            int currentCenterY,
            int currentTilesPerPixel,
            bool zoomOut,
            out int nextCenterX,
            out int nextCenterY,
            out int nextTilesPerPixel)
        {
            nextCenterX = currentCenterX;
            nextCenterY = currentCenterY;
            nextTilesPerPixel = zoomOut
                ? Math.Min(MaximumTilesPerPixel, currentTilesPerPixel * 2)
                : Math.Max(MinimumTilesPerPixel, currentTilesPerPixel / 2);
        }

        /// <summary>
        /// 指定位置を固定点にしてズーム倍率と中心を更新し、範囲外の倍率変更は無視します。
        /// </summary>
        /// <param name="zoomOut">1ピクセルあたりタイル数を増やす場合はtrueです。</param>
        /// <param name="anchorPosition">ズーム前後で同じワールド座標を保つCanvas位置です。</param>
        private void ChangeZoom(bool zoomOut, Vector2 anchorPosition)
        {
            int oldTilesPerPixel = displayedSample?.TilesPerPixel ?? serializedTilesPerPixel;
            int newTilesPerPixel = zoomOut
                ? Math.Min(MaximumTilesPerPixel, oldTilesPerPixel * 2)
                : Math.Max(MinimumTilesPerPixel, oldTilesPerPixel / 2);
            if (newTilesPerPixel == oldTilesPerPixel)
            {
                return;
            }

            int currentCenterX = displayedSample?.CenterTileX ?? serializedCenterTileX;
            int currentCenterY = displayedSample?.CenterTileY ?? serializedCenterTileY;
            WorldGenerationMapSampler.CalculateSampleResolution(
                mapCanvas.contentRect.width,
                mapCanvas.contentRect.height,
                out int requestedSampleWidth,
                out int requestedSampleHeight);
            int oldSampleWidth = displayedSample?.Width ?? requestedSampleWidth;
            int oldSampleHeight = displayedSample?.Height ?? requestedSampleHeight;
            WorldGenerationMapSampler.CalculateZoomedCenter(
                currentCenterX,
                currentCenterY,
                oldTilesPerPixel,
                newTilesPerPixel,
                oldSampleWidth,
                oldSampleHeight,
                requestedSampleWidth,
                requestedSampleHeight,
                mapCanvas.contentRect.width,
                mapCanvas.contentRect.height,
                anchorPosition.x,
                anchorPosition.y,
                out serializedCenterTileX,
                out serializedCenterTileY);
            serializedTilesPerPixel = newTilesPerPixel;
            UpdateCoordinateFields();
            RequestRenderFromFields();
        }

        /// <summary>
        /// 座標欄をワールド原点へ戻し、現在のSeedと表示モードで再生成します。
        /// </summary>
        private void MoveToOrigin()
        {
            serializedCenterTileX = 0;
            serializedCenterTileY = 0;
            UpdateCoordinateFields();
            RequestRenderFromFields();
        }

        /// <summary>
        /// 表示モード変更をenumへ反映し、色凡例と地図を同じモードで再生成します。
        /// </summary>
        /// <param name="evt">DropdownFieldの変更前後値です。</param>
        private void OnViewModeChanged(ChangeEvent<string> evt)
        {
            serializedViewMode = GetSelectedViewMode();
            UpdateLegend();
            if (hasSourceSettings && mapCanvas.contentRect.width >= 64f)
            {
                RequestRenderFromFields();
            }
        }

        /// <summary>
        /// DropdownFieldの選択文字列を安定した表示モードenumへ変換します。
        /// ChangeEvent通知中はindexが旧値のままになるUnityバージョンがあるため、確定済みのvalueを使用します。
        /// </summary>
        /// <returns>表示文字列がBiomeならBiome、それ以外はTerrainです。</returns>
        private WorldGenerationMapViewMode GetSelectedViewMode()
        {
            return string.Equals(
                viewModeField.value,
                ViewModeChoices[(int)WorldGenerationMapViewMode.Biome],
                StringComparison.Ordinal)
                ? WorldGenerationMapViewMode.Biome
                : WorldGenerationMapViewMode.Terrain;
        }

        /// <summary>
        /// Canvas上のカーソル位置をワールド座標へ変換し、同一タイルへ留まる間はGeneratorを再呼び出しません。
        /// </summary>
        /// <param name="localPointerPosition">Canvas左上原点のカーソル位置です。</param>
        private void UpdateHoverInformation(Vector2 localPointerPosition)
        {
            if (displayedSample == null)
            {
                return;
            }

            WorldGenerationMapSampler.CalculateWorldTileAtPointer(
                displayedSample.CenterTileX,
                displayedSample.CenterTileY,
                displayedSample.TilesPerPixel,
                displayedSample.Width,
                displayedSample.Height,
                mapCanvas.contentRect.width,
                mapCanvas.contentRect.height,
                localPointerPosition.x,
                localPointerPosition.y,
                out int worldTileX,
                out int worldTileY);
            UpdateHoverInformationAtWorldTile(worldTileX, worldTileY);
        }

        /// <summary>
        /// 指定ワールドタイルの生成結果を評価し、Hover欄へ座標・チャンク・地形情報を表示します。
        /// 再生成直後の中心表示と実Pointer移動後の表示で、同じ生成・整形経路を共有するために使用します。
        /// </summary>
        /// <param name="worldTileX">評価する水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">評価する垂直ワールドタイル座標です。</param>
        private void UpdateHoverInformationAtWorldTile(int worldTileX, int worldTileY)
        {
            if (worldTileX == lastHoveredTileX && worldTileY == lastHoveredTileY)
            {
                return;
            }

            lastHoveredTileX = worldTileX;
            lastHoveredTileY = worldTileY;
            GeneratedTile tile = DeterministicChunkGenerator.GenerateTile(
                displayedSettings,
                worldTileX,
                worldTileY);
            ChunkCoordinate chunk = WorldGrid.ToChunkCoordinate(worldTileX, worldTileY);
            hoverLabel.text =
                $"Tile ({worldTileX}, {worldTileY}) / Chunk ({chunk.X}, {chunk.Y}) / "
                + $"Biome {tile.Biome} / Ground {tile.Ground} / Soil {tile.Soil} / Water {tile.WaterBodyType}";
        }

        /// <summary>
        /// 現在の取得元、GeneratorVersion、実際に表示へ使った64bit WorldSeedを更新します。
        /// </summary>
        private void UpdateSourceInformation()
        {
            WorldGenerationSettings settings = displayedSample == null
                ? sourceSettings
                : displayedSettings;
            sourceLabel.text =
                $"設定: {settingsSourceDescription}\n"
                + $"GeneratorVersion: {settings.GeneratorVersion}\n"
                + $"WorldSeed: 0x{settings.WorldSeed:X16}";
        }

        /// <summary>
        /// 現在中心、1ピクセルあたりタイル数、概算表示範囲をSide Panelへ表示します。
        /// </summary>
        private void UpdateZoomInformation()
        {
            if (displayedSample == null)
            {
                zoomLabel.text = "地図未生成";
                return;
            }

            zoomLabel.text =
                $"中心: ({displayedSample.CenterTileX}, {displayedSample.CenterTileY})\n"
                + $"縮尺: 1px = {displayedSample.TilesPerPixel} tiles\n"
                + $"範囲: 約{displayedSample.Width * displayedSample.TilesPerPixel:N0}"
                + $" × {displayedSample.Height * displayedSample.TilesPerPixel:N0} tiles";
        }

        /// <summary>
        /// 選択中モードに対応する固定色の凡例を再構築します。
        /// </summary>
        private void UpdateLegend()
        {
            legendContainer.Clear();
            AddLegendRow("Grassland", "legend-grassland");
            AddLegendRow("Forest", "legend-forest");
            AddLegendRow("Desert", "legend-desert");
            AddLegendRow("Rockland", "legend-rockland");
            AddLegendRow("Wetland", "legend-wetland");
            AddLegendRow("Snowfield", "legend-snowfield");
            AddLegendRow("Volcanic", "legend-volcanic");

            if (GetSelectedViewMode() == WorldGenerationMapViewMode.Terrain)
            {
                // TerrainではBiome以外に優先表示する水域・マグマ・岩壁だけを凡例へ追加します。
                // SoilTypeは通常地形へ反映しないため、Editor専用Debug Visualization側だけで案内します。
                AddLegendRow("Fresh Water", "legend-fresh-water");
                AddLegendRow("Salt Water", "legend-salt-water");
                AddLegendRow("Magma", "legend-magma");
                AddLegendRow("Rock Wall", "legend-rock-wall");
            }
        }

        /// <summary>
        /// 色見本と名前を1行にまとめ、USSクラスから色を適用します。
        /// </summary>
        /// <param name="displayName">開発者へ表示する地形名です。</param>
        /// <param name="colorClass">固定色を定義したUSSクラス名です。</param>
        private void AddLegendRow(string displayName, string colorClass)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("legend-row");

            VisualElement swatch = new VisualElement();
            swatch.AddToClassList("legend-swatch");
            swatch.AddToClassList(colorClass);
            row.Add(swatch);

            Label label = new Label(displayName);
            label.AddToClassList("legend-label");
            row.Add(label);
            legendContainer.Add(row);
        }

        /// <summary>
        /// シリアライズ済み中心座標を入力欄へ反映し、値変更イベントによる余分な再生成を避けます。
        /// </summary>
        private void UpdateCoordinateFields()
        {
            centerXField.SetValueWithoutNotify(serializedCenterTileX);
            centerYField.SetValueWithoutNotify(serializedCenterTileY);
        }

        /// <summary>
        /// 進行状況またはエラーを表示し、エラー時だけ専用USSクラスを付けます。
        /// </summary>
        /// <param name="message">画面下部へ表示する日本語メッセージです。</param>
        /// <param name="isError">エラー色にする場合はtrueです。</param>
        private void SetStatus(string message, bool isError)
        {
            statusLabel.text = message;
            statusLabel.EnableInClassList("error", isError);
        }

        /// <summary>
        /// 実行中のサンプリングへキャンセルを通知し、CancellationTokenSourceを破棄します。
        /// </summary>
        private void CancelPendingRender()
        {
            if (renderCancellation == null)
            {
                return;
            }

            renderCancellation.Cancel();
            renderCancellation.Dispose();
            renderCancellation = null;
        }

        /// <summary>
        /// EditorWindowが所有する一時Textureを即時破棄し、Window再生成時のEditorメモリ漏れを防ぎます。
        /// </summary>
        private void DestroyDisplayedTexture()
        {
            if (displayedTexture == null)
            {
                return;
            }

            DestroyImmediate(displayedTexture);
            displayedTexture = null;
        }

        /// <summary>
        /// longで計算したパン後座標を、Generatorが受け取るint範囲へ飽和変換します。
        /// </summary>
        /// <param name="coordinate">int範囲を越える可能性がある座標です。</param>
        /// <returns>int最小値から最大値へ収めた値です。</returns>
        private static int ClampCoordinate(long coordinate)
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
    }
}
