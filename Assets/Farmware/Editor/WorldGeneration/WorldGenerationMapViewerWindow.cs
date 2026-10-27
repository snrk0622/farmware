// このファイルは、MinecraftのBiome Finderに相当するFarmware開発者用の広域マップEditorWindowを提供します。
// UI Toolkitでシード・座標・ズームを操作し、実ゲームと同じAuthoring設定とDeterministicChunkGeneratorの結果を表示します。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Farmware.Simulation;
using Farmware.WorldGeneration.Presentation;
using Unity.Collections;
using Unity.Entities;
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

        /// <summary>未レイアウトのCanvasを生成対象にしないための最小表示寸法です。単位はEditorポイントです。</summary>
        private const float MinimumViewportSize = 64f;

        /// <summary>
        /// Map Viewerへ許可する最小Window寸法です。単位はEditorポイントです。
        /// 横幅は操作欄とSide Panelを保ち、高さは地図240px、Hover診断280px、ヘッダー・操作欄・状態欄を同時表示できる値です。
        /// </summary>
        public static Vector2 MinimumWindowSize => new Vector2(720f, 700f);

        /// <summary>
        /// Toolbarへ渡す表示名です。順序と生成契約はCatalogを正とし、UI Toolkitが要求するListへ一度だけコピーします。
        /// </summary>
        private static readonly List<string> ViewModeChoices =
            new List<string>(WorldGenerationMapViewModeCatalog.OrderedDisplayNames);

        /// <summary>Toolbarの表示名と固定ワールドレイヤーを対応させる選択肢です。</summary>
        private static readonly List<string> LayerChoices = new List<string>
        {
            "Surface",
            "Underwater",
            "Deep Sea"
        };

        /// <summary>ドメインリロード後も維持する現在の水平中心タイル座標です。</summary>
        [SerializeField] private int serializedCenterTileX;

        /// <summary>ドメインリロード後も維持する現在の垂直中心タイル座標です。</summary>
        [SerializeField] private int serializedCenterTileY;

        /// <summary>ドメインリロード後も維持する1ピクセルあたりのタイル数です。</summary>
        [SerializeField] private int serializedTilesPerPixel = 4;

        /// <summary>ドメインリロード後も維持する地形・環境・地質・水文の表示モードです。</summary>
        [SerializeField] private WorldGenerationMapViewMode serializedViewMode =
            WorldGenerationMapViewMode.Terrain;

        /// <summary>ドメインリロード後も維持する診断対象の固定ワールドレイヤーです。</summary>
        [SerializeField] private WorldLayer serializedLayer = WorldLayer.Surface;

        /// <summary>シード入力欄です。編集されるまでSettings内の実WorldSeedをそのまま使用します。</summary>
        private TextField seedField;

        /// <summary>座標ジャンプと現在中心表示に使う水平タイル入力欄です。</summary>
        private IntegerField centerXField;

        /// <summary>座標ジャンプと現在中心表示に使う垂直タイル入力欄です。</summary>
        private IntegerField centerYField;

        /// <summary>Terrain、Biome、連続環境軸、地質、基礎水文、派生水文を切り替える選択欄です。</summary>
        private DropdownField viewModeField;

        /// <summary>Surface、Underwater、DeepSeaの検索キーを切り替える選択欄です。</summary>
        private DropdownField layerField;

        /// <summary>チャンク・サブチャンク境界を表示するToggleです。</summary>
        private Toggle gridToggle;

        /// <summary>マップTextureと操作イベントを担当するカスタムVisualElementです。</summary>
        private WorldGenerationMapCanvas mapCanvas;

        /// <summary>カーソル下の座標、生成タイル、地質、選択中の水文診断値を表示するLabelです。</summary>
        private Label hoverLabel;

        /// <summary>設定取得元、GeneratorVersion、実WorldSeedを表示するLabelです。</summary>
        private Label sourceLabel;

        /// <summary>現在の中心、ズーム、概算表示範囲を表示するLabelです。</summary>
        private Label zoomLabel;

        /// <summary>選択中モードの色凡例を保持するContainerです。</summary>
        private VisualElement legendContainer;

        /// <summary>生成中、完了時間、エラーを画面下部へ表示するLabelです。</summary>
        private Label statusLabel;

        /// <summary>初回画像がまだない間、地図領域の中央へ生成進捗または失敗理由を表示するLabelです。</summary>
        private Label generationLabel;

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

        // Hover中央情報で同じ8×8水文セルをタイルごとに再生成しないため、現在の表示設定に限って固定結果を保持します。
        // Map Viewerの地図Textureと同じ決定論的Generatorの結果だけをキャッシュし、Simulation状態には書き戻しません。
        private readonly Dictionary<long, GeneratedHydrologyCell> hoverHydrologyCellCache =
            new Dictionary<long, GeneratedHydrologyCell>();

        /// <summary>
        /// Farmwareメニューから広域マップビューアを開き、既存Windowがあれば再利用します。
        /// </summary>
        [MenuItem("Farmware/World Generation/Map Viewer", false, 110)]
        public static void OpenWindow()
        {
            WorldGenerationMapViewerWindow window =
                GetWindow<WorldGenerationMapViewerWindow>("Farmware Map Viewer");
            // Hover診断を17行へ拡張した後も地図と上下の操作欄を切らず、最小サイズだけで全情報を確認できるようにします。
            window.minSize = MinimumWindowSize;
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
            CancelPendingRender();
            DestroyDisplayedTexture();
            displayedSample = null;
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
            layerField = RequireElement<DropdownField>("layerField");
            gridToggle = RequireElement<Toggle>("gridToggle");
            hoverLabel = RequireElement<Label>("hoverLabel");
            sourceLabel = RequireElement<Label>("sourceLabel");
            zoomLabel = RequireElement<Label>("zoomLabel");
            ScrollView sidePanel = RequireElement<ScrollView>("sidePanel");
            // 材質名が長くても横方向へスクロールさせず、残り幅で折り返して色見本の幅を守ります。
            sidePanel.mode = ScrollViewMode.Vertical;
            sidePanel.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            legendContainer = RequireElement<VisualElement>("legendContainer");
            statusLabel = RequireElement<Label>("statusLabel");
            generationLabel = RequireElement<Label>("generationLabel");

            VisualElement mapHost = RequireElement<VisualElement>("mapHost");
            mapCanvas = new WorldGenerationMapCanvas();
            mapHost.Add(mapCanvas);
            // 地図がまだないときの案内をCanvasより前面へ置き、生成途中を空表示と取り違えないようにします。
            generationLabel.BringToFront();
        }

        /// <summary>
        /// 入力欄の初期値、Tooltip、ButtonとPointerイベントを設定します。
        /// </summary>
        private void ConfigureControls()
        {
            centerXField.SetValueWithoutNotify(serializedCenterTileX);
            centerYField.SetValueWithoutNotify(serializedCenterTileY);
            // ドメインリロード前の未知値をそのままIndexへ使うとUI構築で例外になるため、現在定義へ含まれない値は通常地形へ戻します。
            serializedViewMode = Enum.IsDefined(typeof(WorldGenerationMapViewMode), serializedViewMode)
                ? serializedViewMode
                : WorldGenerationMapViewMode.Terrain;
            viewModeField.choices = ViewModeChoices;
            viewModeField.SetValueWithoutNotify(ViewModeChoices[(int)serializedViewMode]);
            serializedLayer = WorldLayerUtility.IsPlayable(serializedLayer)
                ? serializedLayer
                : WorldLayer.Surface;
            layerField.choices = LayerChoices;
            layerField.SetValueWithoutNotify(GetLayerDisplayName(serializedLayer));

            seedField.tooltip =
                "編集しなければ実ゲームのWorldSeedをそのまま使用します。編集後は同じAuthoring設定で別シードを比較します。";
            centerXField.tooltip = "移動先の水平ワールドタイル座標です。負座標も入力できます。";
            centerYField.tooltip = "移動先の垂直ワールドタイル座標です。負座標も入力できます。";
            viewModeField.tooltip =
                "Terrainは最終地形、Biomeは論理バイオーム、Environmentは連続軸、Geologyは固定地質、Hydrologyは固定水文の結果を表示します。派生水文は完成チャンク生成のため低解像度です。";
            layerField.tooltip =
                "診断する固定ワールドレイヤーです。現在はSurfaceだけに自然生成が接続され、別層は未接続表示になります。";
            gridToggle.tooltip = "白線は16タイル、青線は64タイル境界です。遠距離では自動的に省略します。";

            seedField.RegisterValueChangedCallback(_ => seedWasEdited = true);
            viewModeField.RegisterValueChangedCallback(OnViewModeChanged);
            layerField.RegisterValueChangedCallback(OnLayerChanged);
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

            if (!CanRenderViewport(mapCanvas.contentRect.width, mapCanvas.contentRect.height)
                || (displayedSample != null
                    && WorldGenerationMapSampler.DoesSampleResolutionMatchViewport(
                        displayedSample.Width,
                        displayedSample.Height,
                        mapCanvas.contentRect.width,
                        mapCanvas.contentRect.height,
                        WorldGenerationMapViewModeCatalog.GetMaximumSampleResolution(
                            serializedViewMode,
                            displayedSample.TilesPerPixel))))
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
                || !CanRenderViewport(mapCanvas.contentRect.width, mapCanvas.contentRect.height))
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
                || !CanRenderViewport(mapCanvas.contentRect.width, mapCanvas.contentRect.height))
            {
                return;
            }

            initialRenderRequested = true;
            RequestRenderFromFields();
        }

        /// <summary>
        /// Canvasの実寸が確定し、広域画像を生成できるかを判定します。
        /// 初回レイアウト前のNaNは「64未満」の比較を通過するため、最小値以上という肯定条件と有限値検査を使います。
        /// </summary>
        /// <param name="width">Canvasの横幅です。単位はEditorポイントです。</param>
        /// <param name="height">Canvasの縦幅です。単位はEditorポイントです。</param>
        /// <returns>両辺が有限値かつ最小表示寸法以上ならtrueです。状態は変更しません。</returns>
        public static bool CanRenderViewport(float width, float height)
        {
            return width >= MinimumViewportSize
                && height >= MinimumViewportSize
                && !float.IsInfinity(width)
                && !float.IsInfinity(height);
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

            if (requestRender)
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

            if (!CanRenderViewport(mapCanvas.contentRect.width, mapCanvas.contentRect.height))
            {
                SetStatus("地図の表示サイズが確定するまで待っています…", false);
                return;
            }

            serializedCenterTileX = centerXField.value;
            serializedCenterTileY = centerYField.value;
            serializedViewMode = GetSelectedViewMode();
            serializedLayer = GetSelectedLayer();
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

            WorldGenerationMapViewMode viewMode = serializedViewMode;
            int tilesPerPixel = serializedTilesPerPixel;
            WorldGenerationMapSampler.CalculateSampleResolution(
                mapCanvas.contentRect.width,
                mapCanvas.contentRect.height,
                WorldGenerationMapViewModeCatalog.GetMaximumSampleResolution(
                    viewMode,
                    tilesPerPixel),
                out int width,
                out int height);
            int centerTileX = serializedCenterTileX;
            int centerTileY = serializedCenterTileY;
            WorldLayer layer = serializedLayer;
            WorldGenerationSettings renderSettings = sourceSettings;
            if (seedWasEdited)
            {
                renderSettings.WorldSeed = WorldSeedUtility.FromString(seedField.value);
            }

            int horizontalTileSpan = width * tilesPerPixel;
            int verticalTileSpan = height * tilesPerPixel;
            SetStatus(
                $"生成中… 0% / {width}×{height} samples / 約{horizontalTileSpan:N0}×{verticalTileSpan:N0} tiles",
                false);
            Stopwatch stopwatch = Stopwatch.StartNew();

            // Progressは作成時のUnityメインスレッドへ通知を戻すため、ThreadPoolからUIを直接操作しません。
            // 中止・完了済みの通知は破棄し、連続操作後の新しいステータスを古い進捗で上書きさせません。
            IProgress<float> progress = new Progress<float>(fraction =>
            {
                if (cancellationToken.IsCancellationRequested
                    || requestId != renderRequestId
                    || this == null
                    || !stopwatch.IsRunning)
                {
                    return;
                }

                SetStatus(
                    $"生成中… {fraction:P0} / {width}×{height} samples / 約{horizontalTileSpan:N0}×{verticalTileSpan:N0} tiles",
                    false);
            });

            HydrologyDiagnosticTerrainCatalogs terrainCatalogs = null;
            WorldGenerationMapTerrainDeltaSnapshot terrainDeltaSnapshot = null;
            try
            {
                WorldGenerationMapSamplingPath samplingPath =
                    WorldGenerationMapViewModeCatalog.Get(viewMode).SamplingPath;
                bool requiresCompletedTerrain = samplingPath
                    == WorldGenerationMapSamplingPath.CompletedTerrainChunk
                    || samplingPath == WorldGenerationMapSamplingPath.CompletedHydrologyChunk;
                if (requiresCompletedTerrain
                    && layer == WorldLayer.Surface
                    && !HydrologyDiagnosticTerrainCatalogs.TryCreate(out terrainCatalogs))
                {
                    throw new InvalidOperationException(
                        "Map Viewer用の地形Definitionカタログを構築できませんでした。");
                }

                if (requiresCompletedTerrain && layer == WorldLayer.Surface)
                {
                    // ECSのDynamic BufferはThreadPoolから読めないため、同じ生成設定のWorldだけをメインスレッドで値コピーします。
                    terrainDeltaSnapshot = CaptureTerrainDeltaSnapshot(renderSettings, layer);
                }

                // BlobBuilderの完成処理はMain Thread限定なので上で先に完了させ、
                // ThreadPool側には読み取り専用Blobだけを渡して重いチャンク生成を続けます。
                WorldGenerationMapSampleResult result = await Task.Run(
                    () => WorldGenerationMapSampler.Sample(
                        renderSettings,
                        terrainCatalogs,
                        layer,
                        centerTileX,
                        centerTileY,
                        width,
                        height,
                        tilesPerPixel,
                        viewMode,
                        cancellationToken,
                        progress,
                        terrainDeltaSnapshot),
                    cancellationToken);

                if (cancellationToken.IsCancellationRequested
                    || requestId != renderRequestId
                    || this == null)
                {
                    return;
                }

                stopwatch.Stop();
                ApplyRenderedMap(result, renderSettings);
                string completionMessage;
                if (!result.HasGeneratedData)
                {
                    completionMessage =
                        $"{result.Layer}: 自然生成は未接続です。座標キーだけを表示しています。";
                }
                else if (result.DiagnosticChunkCount > 0)
                {
                    completionMessage =
                        $"完了: {result.Width * result.Height:N0} samples / 完成診断{result.DiagnosticChunkCount:N0} chunks / {stopwatch.Elapsed.TotalSeconds:F2}秒";
                    if (result.UsesTerrainDeltaSnapshot)
                    {
                        completionMessage +=
                            $" / 有効地形差分 {result.AppliedTerrainDeltaSampleCount:N0} samples";
                    }
                }
                else
                {
                    completionMessage =
                        $"完了: {result.Width * result.Height:N0} samples / {stopwatch.Elapsed.TotalSeconds:F2}秒";
                }
                SetStatus(completionMessage, false);
            }
            catch (OperationCanceledException)
            {
                // 新しいパン・ズーム要求による通常の中断なので、Consoleへ例外を残しません。
            }
            catch (Exception exception)
            {
                if (cancellationToken.IsCancellationRequested || requestId != renderRequestId || this == null)
                {
                    return;
                }

                UnityEngine.Debug.LogException(exception);
                SetStatus($"地図生成に失敗しました: {exception.Message}", true);
            }
            finally
            {
                // awaitによりThreadPool処理が終了した後なので、読み取り中のBlobを破棄しません。
                terrainCatalogs?.Dispose();
                // 完了後にキューから届く進捗通知が、完了表示やエラー表示を上書きしないようにします。
                stopwatch.Stop();
            }
        }

        /// <summary>
        /// 実行中Worldの永続TerrainCellDeltaをManaged配列へコピーし、Map Viewer用の不変Snapshotを作ります。
        /// </summary>
        /// <param name="renderSettings">Map Viewerが今回使うSeedと全生成設定です。</param>
        /// <param name="layer">コピー対象の固定ワールドレイヤーです。</param>
        /// <returns>
        /// 同じ設定を持つDefault Worldが存在する場合は空を含むSnapshot、それ以外は自然生成だけを示すnullです。
        /// </returns>
        private static WorldGenerationMapTerrainDeltaSnapshot CaptureTerrainDeltaSnapshot(
            in WorldGenerationSettings renderSettings,
            WorldLayer layer)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return null;
            }

            EntityManager entityManager = world.EntityManager;
            EntityQuery settingsQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<WorldGenerationSettings>());
            try
            {
                // 別Seedや別調整値の差分を適用すると、同じ保存差分を異なる自然地形へ重ねることになります。
                // 全設定が一致する実行中Worldだけを現在有効地形の取得元として採用します。
                if (settingsQuery.CalculateEntityCount() != 1
                    || !settingsQuery.GetSingleton<WorldGenerationSettings>().Equals(renderSettings))
                {
                    return null;
                }
            }
            finally
            {
                settingsQuery.Dispose();
            }

            var copiedDeltas =
                new Dictionary<ChunkCoordinate, TerrainCellDelta[]>();
            EntityQuery deltaQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<TerrainChunkDeltaOwner>(),
                ComponentType.ReadOnly<TerrainCellDelta>());
            try
            {
                using NativeArray<Entity> entities = deltaQuery.ToEntityArray(Allocator.Temp);
                using NativeArray<TerrainChunkDeltaOwner> owners =
                    deltaQuery.ToComponentDataArray<TerrainChunkDeltaOwner>(Allocator.Temp);
                for (int index = 0; index < entities.Length; index++)
                {
                    TerrainChunkDeltaOwner owner = owners[index];
                    if (owner.Layer != layer)
                    {
                        continue;
                    }

                    var coordinate = new ChunkCoordinate(owner.Layer, owner.ChunkX, owner.ChunkY);
                    if (copiedDeltas.ContainsKey(coordinate))
                    {
                        throw new InvalidOperationException(
                            $"Map Viewer用の地形差分所有者が重複しています: {coordinate}");
                    }

                    DynamicBuffer<TerrainCellDelta> source =
                        entityManager.GetBuffer<TerrainCellDelta>(entities[index], true);
                    var copy = new TerrainCellDelta[source.Length];
                    for (int deltaIndex = 0; deltaIndex < source.Length; deltaIndex++)
                    {
                        copy[deltaIndex] = source[deltaIndex];
                    }

                    copiedDeltas.Add(coordinate, copy);
                }
            }
            finally
            {
                deltaQuery.Dispose();
            }

            return new WorldGenerationMapTerrainDeltaSnapshot(copiedDeltas);
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
            hoverHydrologyCellCache.Clear();
            generationLabel?.AddToClassList("hidden");
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
            // 派生水文は縮尺で解像度上限が変わるため、変更前後を別々に計算し、Pointer固定点を実Textureへ一致させます。
            WorldGenerationMapSampler.CalculateSampleResolution(
                mapCanvas.contentRect.width,
                mapCanvas.contentRect.height,
                WorldGenerationMapViewModeCatalog.GetMaximumSampleResolution(
                    serializedViewMode,
                    oldTilesPerPixel),
                out int requestedOldSampleWidth,
                out int requestedOldSampleHeight);
            WorldGenerationMapSampler.CalculateSampleResolution(
                mapCanvas.contentRect.width,
                mapCanvas.contentRect.height,
                WorldGenerationMapViewModeCatalog.GetMaximumSampleResolution(
                    serializedViewMode,
                    newTilesPerPixel),
                out int requestedNewSampleWidth,
                out int requestedNewSampleHeight);
            int oldSampleWidth = displayedSample?.Width ?? requestedOldSampleWidth;
            int oldSampleHeight = displayedSample?.Height ?? requestedOldSampleHeight;
            WorldGenerationMapSampler.CalculateZoomedCenter(
                currentCenterX,
                currentCenterY,
                oldTilesPerPixel,
                newTilesPerPixel,
                oldSampleWidth,
                oldSampleHeight,
                requestedNewSampleWidth,
                requestedNewSampleHeight,
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
            if (hasSourceSettings)
            {
                RequestRenderFromFields();
            }
        }

        /// <summary>
        /// レイヤー選択を固定IDへ反映し、同じX/Yの別レイヤーを独立した検索キーとして再生成します。
        /// </summary>
        /// <param name="evt">DropdownFieldの変更前後値です。</param>
        private void OnLayerChanged(ChangeEvent<string> evt)
        {
            serializedLayer = GetSelectedLayer();
            if (hasSourceSettings)
            {
                RequestRenderFromFields();
            }
        }

        /// <summary>
        /// DropdownFieldの選択文字列を安定した表示モードenumへ変換します。
        /// ChangeEvent通知中はindexが旧値のままになるUnityバージョンがあるため、確定済みのvalueを使用します。
        /// </summary>
        /// <returns>表示文字列と同じIndexの表示モードです。未知の文字列はTerrainへ戻します。</returns>
        private WorldGenerationMapViewMode GetSelectedViewMode()
        {
            int selectedIndex = ViewModeChoices.IndexOf(viewModeField.value);
            return ResolveViewModeChoiceIndex(selectedIndex);
        }

        /// <summary>
        /// DropdownFieldの選択Indexを表示モードへ変換し、範囲外または未定義の値はTerrainへ戻します。
        /// </summary>
        /// <param name="selectedIndex">ViewModeChoices内の0始まりIndexです。未選択時は-1です。</param>
        /// <returns>定義済みの表示モード、または安全な既定値のTerrainです。</returns>
        public static WorldGenerationMapViewMode ResolveViewModeChoiceIndex(int selectedIndex)
        {
            return WorldGenerationMapViewModeCatalog.ResolveChoiceIndex(selectedIndex);
        }

        /// <summary>レイヤー選択文字列を保存IDへ変換し、未知の表示値はSurfaceへ戻します。</summary>
        /// <returns>Surface、Underwater、DeepSeaのいずれかです。</returns>
        private WorldLayer GetSelectedLayer()
        {
            if (string.Equals(layerField.value, LayerChoices[1], StringComparison.Ordinal))
            {
                return WorldLayer.Underwater;
            }

            if (string.Equals(layerField.value, LayerChoices[2], StringComparison.Ordinal))
            {
                return WorldLayer.DeepSea;
            }

            return WorldLayer.Surface;
        }

        /// <summary>固定レイヤーIDをDropdownFieldの表示名へ変換します。</summary>
        /// <param name="layer">表示する固定ワールドレイヤーです。</param>
        /// <returns>LayerChoicesに含まれる表示名です。</returns>
        private static string GetLayerDisplayName(WorldLayer layer)
        {
            switch (layer)
            {
                case WorldLayer.Underwater: return LayerChoices[1];
                case WorldLayer.DeepSea: return LayerChoices[2];
                default: return LayerChoices[0];
            }
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
            ChunkCoordinate chunk = WorldGrid.ToChunkCoordinate(
                displayedSample.Layer,
                worldTileX,
                worldTileY);
            if (!displayedSample.HasGeneratedData)
            {
                hoverLabel.text =
                    $"{displayedSample.Layer} / Tile ({worldTileX}, {worldTileY}) / "
                    + $"Chunk ({chunk.X}, {chunk.Y}) / 自然生成未接続";
                return;
            }

            // 完成チャンクを使った画像では、その場で基礎地形を再生成するとBiome=Noneへ戻ってしまいます。
            // 画像作成時のC5-C確定値を優先し、環境・地質など基礎生成だけのモードでのみ純粋Generatorを呼びます。
            bool hasFinalizedTerrain = displayedSample.TryGetFinalizedTerrainSample(
                worldTileX,
                worldTileY,
                out FinalizedTerrainMapSample finalizedTerrainSample);
            GeneratedTile tile = hasFinalizedTerrain
                ? finalizedTerrainSample.Tile
                : default;
            GeneratedTerrainCell terrainCell = hasFinalizedTerrain
                ? finalizedTerrainSample.TerrainCell
                : default;
            TerrainBlockMaterialId groundMaterialId = hasFinalizedTerrain
                ? finalizedTerrainSample.GroundMaterialId
                : TerrainBlockMaterialId.None;
            TerrainBlockMaterialId wallMaterialId = hasFinalizedTerrain
                ? finalizedTerrainSample.WallMaterialId
                : TerrainBlockMaterialId.None;
            bool hasTile = hasFinalizedTerrain;
            if (!hasTile)
            {
                hasTile = DeterministicChunkGenerator.TryGenerateTile(
                    displayedSettings,
                    worldTileX,
                    worldTileY,
                    out tile);
            }

            if (!hasTile)
            {
                hoverLabel.text =
                    $"{displayedSample.Layer} / Tile ({worldTileX}, {worldTileY}) / "
                    + $"Chunk ({chunk.X}, {chunk.Y}) / 固定サンプル範囲外のため生成失敗";
                return;
            }

            bool hasGeologyInfo = DeterministicGeologyGenerator.TryGenerate(
                displayedSettings,
                worldTileX,
                worldTileY,
                out GeneratedGeologyData geologyInfo);
            string geologySiteText = hasGeologyInfo
                ? $"Macro ({geologyInfo.Site.MacroCellX}, {geologyInfo.Site.MacroCellY}) / "
                    + $"Site ({geologyInfo.Site.SiteX}, {geologyInfo.Site.SiteY}) / "
                    + $"DistanceSquared {geologyInfo.Site.DistanceSquared}"
                : "サイト情報生成失敗";
            string hydrologyText = string.Empty;
            string biomeSelectionText = string.Empty;
            WorldGenerationMapHoverDataContract hoverDataContract =
                WorldGenerationMapViewModeCatalog.Get(displayedSample.ViewMode).HoverDataContract;
            if (hoverDataContract
                == WorldGenerationMapHoverDataContract.FinalizedTerrainAndBiomeSelection)
            {
                if (tile.Biome == BiomeId.None)
                {
                    biomeSelectionText = "\nBiomeSelection: not applicable (ocean)";
                }
                else if (displayedSample.TryGetBiomeClassificationSample(
                        worldTileX,
                        worldTileY,
                        out BiomeClassificationMapSample biomeSample))
                {
                    // 画像生成時の完成チャンクから保存した値を使い、Pointer移動ごとに重い水文と分類を再実行しません。
                    biomeSelectionText = "\n"
                        + WorldGenerationDebugVisualizationOverlay
                            .FormatBiomeSelectionDiagnostics(biomeSample.Selection);
                }
                else
                {
                    biomeSelectionText = "\nBiomeSelection: completed sample unavailable";
                }
            }

            if (hoverDataContract == WorldGenerationMapHoverDataContract.DerivedHydrologyTile)
            {
                if (displayedSample.TryGetDerivedHydrologySample(
                        worldTileX,
                        worldTileY,
                        out DerivedHydrologyMapSample derivedSample))
                {
                    // 地図生成時に完成チャンクから値コピー済みなので、Pointer移動のたびに重いC4生成を同期実行しません。
                    bool isGroundwaterConnected = (derivedSample.Cell.ConnectionFlags
                        & HydrologyConnectionFlags.Groundwater) != 0;
                    bool hasSpring = derivedSample.SurfaceWaterTile.Type
                        == SurfaceWaterTileType.SpringStream;
                    bool isFreshwaterConnected = (derivedSample.HydrologyTile.Flags
                        & GeneratedHydrologyTileFlags.FreshwaterConnected) != 0;
                    bool isWetlandEligible = (derivedSample.HydrologyTile.Flags
                        & GeneratedHydrologyTileFlags.WetlandTileEligible) != 0;
                    bool isLargeWetland = (derivedSample.HydrologyTile.Flags
                        & GeneratedHydrologyTileFlags.LargeWetlandExtent) != 0;
                    hydrologyText = "\n"
                        + $"Derived Hydrology Cell ({derivedSample.Cell.CellX}, {derivedSample.Cell.CellY}) / "
                        + $"Groundwater {derivedSample.Cell.GroundwaterPotential} / "
                        + $"GroundwaterConnected {(isGroundwaterConnected ? "Yes" : "No")}\n"
                        + $"NaturalSaturation {derivedSample.HydrologyTile.NaturalSoilSaturationPotential} / "
                        + $"NaturalFlooding {derivedSample.HydrologyTile.NaturalFloodingPotential} / "
                        + $"Spring {(hasSpring ? "Yes" : "No")}\n"
                        + $"Freshwater {(isFreshwaterConnected ? "Yes" : "No")} / "
                        + $"WetlandEligible {(isWetlandEligible ? "Yes" : "No")} / "
                        + $"LargeWetland {(isLargeWetland ? "Yes" : "No")}";
                }
                else
                {
                    hydrologyText = "\n完成派生水文サンプルを取得できません。";
                }
            }
            else if (hoverDataContract == WorldGenerationMapHoverDataContract.BaseHydrologyCell)
            {
                if (TryGetHoverHydrologyCell(
                        worldTileX,
                        worldTileY,
                        out GeneratedHydrologyCell hydrologyCell))
                {
                    hydrologyText = "\n"
                        + $"Hydrology Cell ({hydrologyCell.CellX}, {hydrologyCell.CellY}) / "
                        + $"Height {hydrologyCell.HydrologyHeight} / Flow {hydrologyCell.FlowDirection} -> "
                        + $"({hydrologyCell.DownstreamCellX}, {hydrologyCell.DownstreamCellY}) / "
                        + $"Terminal {hydrologyCell.DownstreamTerminal} / Edges {hydrologyCell.DownstreamEdgeCount}\n"
                        + $"Catchment {hydrologyCell.CatchmentAreaCells} / "
                        + $"Truncated {(hydrologyCell.CatchmentTruncated ? "Yes" : "No")} / "
                        + $"Runoff {hydrologyCell.RunoffEquivalentCells} / "
                        + $"Connections {hydrologyCell.ConnectionFlags}\n"
                        + $"BasinArea {hydrologyCell.BasinAreaCells} / LakeFlags {hydrologyCell.LakeFlags} / "
                        + $"LakeClassification {hydrologyCell.LakeClassification} / "
                        + $"Bottom {hydrologyCell.LakeBottomHeight} / Spill {hydrologyCell.SpillHeight} / "
                        + $"Surface {hydrologyCell.LakeSurfaceHeight} / Fill {hydrologyCell.LakeFillFraction} / "
                        + $"SpillEdge ({hydrologyCell.SpillInsideCellX}, {hydrologyCell.SpillInsideCellY})->"
                        + $"({hydrologyCell.SpillOutsideCellX}, {hydrologyCell.SpillOutsideCellY})";
                }
                else
                {
                    hydrologyText = "\n固定水文セルを生成できません。";
                }
            }

            hoverLabel.text =
                $"{displayedSample.Layer} / Tile ({worldTileX}, {worldTileY}) / Chunk ({chunk.X}, {chunk.Y}) / "
                + $"Biome {tile.Biome} / GroundBlock {terrainCell.GroundBlockRuntimeId.Value} / "
                + $"GroundMaterial {groundMaterialId} / WallBlock {terrainCell.WallBlockRuntimeId.Value} / "
                + $"WallMaterial {wallMaterialId} / Liquid {terrainCell.LiquidRuntimeId.Value} / "
                + $"Surface {terrainCell.TerrainSurfaceRuntimeId.Value}\n"
                + $"Elevation {tile.Elevation} ({tile.ElevationClass}) / BaseTemp {tile.Temperature} / "
                + $"TerrainTemp {tile.TerrainTemperature} / BaseMoisture {tile.Moisture}\n"
                + $"Woody {tile.WoodyVegetationPotential} / Ruggedness {tile.TerrainRuggedness} / "
                + $"Coastal {tile.CoastalInfluence} / Volcanic {tile.VolcanicActivityPotential}\n"
                + $"Geology {tile.GeologyClass} / Flags {tile.GeologyFlags} / "
                + $"Permeability {tile.GeologyPermeability} / Groundwater {tile.GeologyGroundwaterStorage} / "
                + $"Erosion {tile.GeologyErosionResistance}\n"
                + $"Geology Site: {geologySiteText}"
                + biomeSelectionText
                + hydrologyText;
        }

        /// <summary>
        /// Hoverタイルを担当する8×8水文セルを生成し、同じセルの再利用時はWindow内キャッシュから返します。
        /// 地図サンプリングと同じ設定・単一セルAPIを使い、中央情報だけ別の近似値にならないようにします。
        /// </summary>
        /// <param name="worldTileX">Hover中の絶対タイルX座標です。</param>
        /// <param name="worldTileY">Hover中の絶対タイルY座標です。</param>
        /// <param name="cell">成功時に集水・湖まで確定した固定水文セルを返します。</param>
        /// <returns>座標変換と決定論的水文生成に成功した場合はtrueです。</returns>
        private bool TryGetHoverHydrologyCell(
            int worldTileX,
            int worldTileY,
            out GeneratedHydrologyCell cell)
        {
            if (!DeterministicHydrologyGenerator.TryGetCellCoordinate(
                    displayedSettings.Hydrology,
                    worldTileX,
                    worldTileY,
                    out int cellX,
                    out int cellY))
            {
                cell = default;
                return false;
            }

            long key = ((long)cellX << 32) | (uint)cellY;
            if (hoverHydrologyCellCache.TryGetValue(key, out cell))
            {
                return true;
            }

            if (!WorldGenerationMapSampler.TryGenerateHydrologyCellForDisplay(
                    displayedSettings,
                    cellX,
                    cellY,
                    displayedSample.ViewMode == WorldGenerationMapViewMode.HydrologyLake,
                    out cell))
            {
                return false;
            }

            hoverHydrologyCellCache.Add(key, cell);
            return true;
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
                + $"Layer: {(displayedSample == null ? serializedLayer : displayedSample.Layer)}\n"
                + $"GeneratorVersion: {settings.GeneratorVersion}\n"
                + $"WorldSeed: 0x{settings.WorldSeed:X16}"
                + (displayedSample == null
                    ? string.Empty
                    : displayedSample.UsesTerrainDeltaSnapshot
                        ? $"\n地形: 実行中Worldの有効地形（差分適用 {displayedSample.AppliedTerrainDeltaSampleCount:N0} samples）"
                        : "\n地形: 自然生成（同じ設定の実行中Worldなし）");
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
                $"中心: {displayedSample.Layer} ({displayedSample.CenterTileX}, {displayedSample.CenterTileY})\n"
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
            WorldGenerationMapViewMode selectedMode = GetSelectedViewMode();
            if (IsContinuousViewMode(selectedMode))
            {
                GeneratedTile minimumTile = default;
                GeneratedTile maximumTile = CreateMaximumEnvironmentTile(selectedMode);
                GetContinuousLegendLabels(
                    selectedMode,
                    out string minimumLabel,
                    out string maximumLabel);
                AddLegendRow(
                    minimumLabel,
                    WorldGenerationMapSampler.GetDisplayColor(selectedMode, minimumTile));
                AddLegendRow(
                    maximumLabel,
                    WorldGenerationMapSampler.GetDisplayColor(selectedMode, maximumTile));
                return;
            }

            if (selectedMode == WorldGenerationMapViewMode.GeologyClass)
            {
                for (GeologyClass geologyClass = GeologyClass.UnconsolidatedSediment;
                     geologyClass <= GeologyClass.UltramaficRock;
                     geologyClass++)
                {
                    GeneratedTile geologyTile = new GeneratedTile { GeologyClass = geologyClass };
                    AddLegendRow(
                        geologyClass.ToString(),
                        WorldGenerationMapSampler.GetDisplayColor(selectedMode, geologyTile));
                }

                return;
            }

            if (selectedMode == WorldGenerationMapViewMode.GeologyFlags)
            {
                AddGeologyFlagsLegendRow("None", GeologyDefinitionFlags.None);
                AddGeologyFlagsLegendRow("Supports Karst", GeologyDefinitionFlags.SupportsKarst);
                AddGeologyFlagsLegendRow(
                    "Supports Active Volcanism",
                    GeologyDefinitionFlags.SupportsActiveVolcanism);
                return;
            }

            if (selectedMode == WorldGenerationMapViewMode.GeologyDominantSite)
            {
                AddLegendRow(
                    "Examples only: colors vary by site coordinates",
                    WorldGenerationMapSampler.GetDisplayColor(
                        selectedMode,
                        default,
                        new GeologySiteData { MacroCellX = 0, MacroCellY = 0 },
                        false));
                AddLegendRow(
                    "Second coordinate-derived color example",
                    WorldGenerationMapSampler.GetDisplayColor(
                        selectedMode,
                        default,
                        new GeologySiteData { MacroCellX = 1, MacroCellY = 0 },
                        false));
                return;
            }

            if (selectedMode == WorldGenerationMapViewMode.GeologySiteBoundary)
            {
                AddLegendRow(
                    "Site interior",
                    WorldGenerationMapSampler.GetDisplayColor(selectedMode, default, default, false));
                AddLegendRow(
                    "Sampled site boundary",
                    WorldGenerationMapSampler.GetDisplayColor(selectedMode, default, default, true));
                return;
            }

            if (selectedMode == WorldGenerationMapViewMode.HydrologyFlowDirection)
            {
                for (HydrologyFlowDirection direction = HydrologyFlowDirection.None;
                     direction <= HydrologyFlowDirection.SouthEast;
                     direction++)
                {
                    AddHydrologyLegendRow(
                        selectedMode,
                        direction == HydrologyFlowDirection.None ? "End" : direction.ToString(),
                        new GeneratedHydrologyCell { FlowDirection = direction });
                }

                return;
            }

            if (selectedMode == WorldGenerationMapViewMode.HydrologyTerminal)
            {
                AddHydrologyLegendRow(
                    selectedMode,
                    "Marine",
                    new GeneratedHydrologyCell
                    {
                        ConnectionFlags = HydrologyConnectionFlags.Marine
                    });
                AddHydrologyLegendRow(
                    selectedMode,
                    "Closed Basin",
                    new GeneratedHydrologyCell
                    {
                        ConnectionFlags = HydrologyConnectionFlags.ClosedBasin
                    });
                AddHydrologyLegendRow(
                    selectedMode,
                    "Long Drainage",
                    new GeneratedHydrologyCell
                    {
                        ConnectionFlags = HydrologyConnectionFlags.LongDrainage
                    });
                return;
            }

            if (selectedMode == WorldGenerationMapViewMode.HydrologyCatchment)
            {
                AddHydrologyLegendRow(
                    selectedMode,
                    "1 contributing cell",
                    new GeneratedHydrologyCell { CatchmentAreaCells = 1 });
                AddHydrologyLegendRow(
                    selectedMode,
                    "4,096 contributing cells",
                    new GeneratedHydrologyCell { CatchmentAreaCells = 4096 });
                AddHydrologyLegendRow(
                    selectedMode,
                    "Truncated at 4,096",
                    new GeneratedHydrologyCell
                    {
                        CatchmentAreaCells = 4096,
                        CatchmentFlags = HydrologyCatchmentFlags.Truncated
                    });
                return;
            }

            if (selectedMode == WorldGenerationMapViewMode.HydrologySurfaceWater)
            {
                AddHydrologyLegendRow(selectedMode, "No fixed surface flow", default);
                AddHydrologyLegendRow(
                    selectedMode,
                    "Stream",
                    new GeneratedHydrologyCell
                    {
                        ConnectionFlags = HydrologyConnectionFlags.Stream
                    });
                AddHydrologyLegendRow(
                    selectedMode,
                    "River",
                    new GeneratedHydrologyCell
                    {
                        ConnectionFlags = HydrologyConnectionFlags.Stream
                            | HydrologyConnectionFlags.River
                    });
                return;
            }

            if (selectedMode == WorldGenerationMapViewMode.HydrologyLake)
            {
                AddHydrologyLegendRow(selectedMode, "Not a closed basin", default);
                AddHydrologyLegendRow(
                    selectedMode,
                    "Resolved dry basin",
                    new GeneratedHydrologyCell { LakeFlags = HydrologyLakeFlags.BasinResolved });
                AddHydrologyLegendRow(
                    selectedMode,
                    "Truncated basin (not a fixed lake)",
                    new GeneratedHydrologyCell { LakeFlags = HydrologyLakeFlags.BasinTruncated });
                AddHydrologyLegendRow(
                    selectedMode,
                    "Fixed lake basin",
                    new GeneratedHydrologyCell { LakeFlags = HydrologyLakeFlags.FixedLake });
                AddHydrologyLegendRow(
                    selectedMode,
                    "Temporary lake / dry lakebed",
                    new GeneratedHydrologyCell
                    {
                        LakeFlags = HydrologyLakeFlags.FixedLake | HydrologyLakeFlags.SurfaceCell,
                        LakeClassification = LakeWaterBalanceClassification.Temporary
                    });
                AddHydrologyLegendRow(
                    selectedMode,
                    "Permanent saltwater lake",
                    new GeneratedHydrologyCell
                    {
                        LakeFlags = HydrologyLakeFlags.FixedLake | HydrologyLakeFlags.SurfaceCell,
                        LakeClassification = LakeWaterBalanceClassification.PermanentSaltwater
                    });
                AddHydrologyLegendRow(
                    selectedMode,
                    "Permanent freshwater lake",
                    new GeneratedHydrologyCell
                    {
                        LakeFlags = HydrologyLakeFlags.FixedLake | HydrologyLakeFlags.SurfaceCell,
                        LakeClassification = LakeWaterBalanceClassification.ClosedFreshwater
                    });
                AddHydrologyLegendRow(
                    selectedMode,
                    "Overflowing freshwater lake",
                    new GeneratedHydrologyCell
                    {
                        LakeFlags = HydrologyLakeFlags.FixedLake
                            | HydrologyLakeFlags.SurfaceCell
                            | HydrologyLakeFlags.Overflow,
                        LakeClassification = LakeWaterBalanceClassification.OverflowFreshwater
                    });
                return;
            }

            if (selectedMode == WorldGenerationMapViewMode.HydrologyGroundwater)
            {
                AddDerivedHydrologyLegendRow(
                    selectedMode,
                    "0 (Dry)",
                    new GeneratedHydrologyCell { GroundwaterPotential = 0 },
                    default,
                    default);
                AddDerivedHydrologyLegendRow(
                    selectedMode,
                    "32767 (No Link)",
                    new GeneratedHydrologyCell { GroundwaterPotential = 32767 },
                    default,
                    default);
                AddDerivedHydrologyLegendRow(
                    selectedMode,
                    "32768 (Link)",
                    new GeneratedHydrologyCell
                    {
                        GroundwaterPotential = 32768,
                        ConnectionFlags = HydrologyConnectionFlags.Groundwater
                    },
                    default,
                    default);
                AddDerivedHydrologyLegendRow(
                    selectedMode,
                    "65535",
                    new GeneratedHydrologyCell
                    {
                        GroundwaterPotential = ushort.MaxValue,
                        ConnectionFlags = HydrologyConnectionFlags.Groundwater
                    },
                    default,
                    default);
                return;
            }

            if (selectedMode == WorldGenerationMapViewMode.HydrologyNaturalSoilSaturation)
            {
                AddDerivedHydrologyLegendRow(
                    selectedMode, "0 (Dry)", default, default, default);
                AddDerivedHydrologyLegendRow(
                    selectedMode,
                    "65535 (Wet)",
                    default,
                    new GeneratedHydrologyTile
                    {
                        NaturalSoilSaturationPotential = ushort.MaxValue
                    },
                    default);
                return;
            }

            if (selectedMode == WorldGenerationMapViewMode.HydrologySpring)
            {
                AddDerivedHydrologyLegendRow(
                    selectedMode, "No Spring", default, default, default);
                AddDerivedHydrologyLegendRow(
                    selectedMode,
                    "Spring Stream",
                    default,
                    default,
                    new GeneratedSurfaceWaterTile
                    {
                        Type = SurfaceWaterTileType.SpringStream
                    });
                return;
            }

            if (selectedMode == WorldGenerationMapViewMode.HydrologyNaturalFlooding)
            {
                AddDerivedHydrologyLegendRow(
                    selectedMode, "0 (Dry)", default, default, default);
                AddDerivedHydrologyLegendRow(
                    selectedMode,
                    "65535 (Flooded)",
                    default,
                    new GeneratedHydrologyTile
                    {
                        NaturalFloodingPotential = ushort.MaxValue
                    },
                    default);
                return;
            }

            if (selectedMode == WorldGenerationMapViewMode.HydrologyWetlandCandidate)
            {
                AddDerivedHydrologyLegendRow(
                    selectedMode, "Ineligible", default, default, default);
                AddDerivedHydrologyLegendRow(
                    selectedMode,
                    "Local Candidate",
                    default,
                    new GeneratedHydrologyTile
                    {
                        Flags = GeneratedHydrologyTileFlags.WetlandTileEligible
                    },
                    default);
                AddDerivedHydrologyLegendRow(
                    selectedMode,
                    "Large Extent",
                    default,
                    new GeneratedHydrologyTile
                    {
                        Flags = GeneratedHydrologyTileFlags.WetlandTileEligible
                            | GeneratedHydrologyTileFlags.LargeWetlandExtent
                    },
                    default);
                return;
            }

            if (selectedMode == WorldGenerationMapViewMode.Terrain)
            {
                // TerrainはBiome色ではなく41材質色を使います。同じ色相の暗色がWallであることを行名で明示します。
                AddLegendRow("Fresh Water", "legend-fresh-water");
                AddLegendRow("Salt Water", "legend-salt-water");
                AddLegendRow("Magma", "legend-magma");
                for (TerrainBlockMaterialId materialId = TerrainBlockMaterialDefinitions.First;
                    materialId <= TerrainBlockMaterialDefinitions.Last;
                    materialId++)
                {
                    AddLegendRow(
                        $"{materialId} Ground (Wall = darker)",
                        WorldGenerationMapSampler.GetTerrainMaterialDisplayColor(materialId, false));
                }

                return;
            }

            for (byte stableId = BiomeStableIds.First;
                stableId <= BiomeStableIds.Last;
                stableId++)
            {
                // Biome凡例もMap本体の色関数を通し、18種追加時にUSSの旧7色だけが残らないようにします。
                BiomeId biome = (BiomeId)stableId;
                AddLegendRow(
                    biome.ToString(),
                    WorldGenerationMapSampler.GetBiomeColor(biome));
            }
        }

        /// <summary>選択モードが0〜65,535の連続値を最小・最大色で示すものかを返します。</summary>
        /// <param name="viewMode">判定するMap Viewer表示モードです。</param>
        /// <returns>2端点の連続凡例を表示するモードならtrueです。</returns>
        private static bool IsContinuousViewMode(WorldGenerationMapViewMode viewMode)
        {
            return (viewMode >= WorldGenerationMapViewMode.Elevation
                    && viewMode <= WorldGenerationMapViewMode.VolcanicActivityPotential)
                || (viewMode >= WorldGenerationMapViewMode.GeologyPermeability
                    && viewMode <= WorldGenerationMapViewMode.GeologyErosionResistance);
        }

        /// <summary>地質フラグ1種類について、実地図色と同じ色見本を凡例へ追加します。</summary>
        /// <param name="displayName">フラグの意味を示す表示名です。</param>
        /// <param name="flags">色へ変換する地質Definitionフラグです。</param>
        private void AddGeologyFlagsLegendRow(string displayName, GeologyDefinitionFlags flags)
        {
            GeneratedTile tile = new GeneratedTile { GeologyFlags = flags };
            AddLegendRow(
                displayName,
                WorldGenerationMapSampler.GetDisplayColor(WorldGenerationMapViewMode.GeologyFlags, tile));
        }

        /// <summary>固定水文分類1種類を実地図と同じ不透明色へ変換し、Map Viewer凡例へ追加します。</summary>
        /// <param name="viewMode">現在選択中の水文表示モードです。</param>
        /// <param name="displayName">分類の意味を示す表示名です。</param>
        /// <param name="cell">凡例分類を表す固定水文セルです。</param>
        private void AddHydrologyLegendRow(
            WorldGenerationMapViewMode viewMode,
            string displayName,
            GeneratedHydrologyCell cell)
        {
            AddLegendRow(
                displayName,
                WorldGenerationMapSampler.GetHydrologyDisplayColor(viewMode, cell));
        }

        /// <summary>完成派生水文1分類をGameビュー共有色へ変換し、Map Viewer凡例へ追加します。</summary>
        /// <param name="viewMode">現在選択中の派生水文表示モードです。</param>
        /// <param name="displayName">分類または連続値端点の表示名です。</param>
        /// <param name="cell">地下水凡例に使う固定水文セルです。</param>
        /// <param name="hydrologyTile">自然飽和・自然冠水・湿原凡例に使う固定タイルです。</param>
        /// <param name="surfaceWaterTile">泉凡例に使う固定表流水タイルです。</param>
        private void AddDerivedHydrologyLegendRow(
            WorldGenerationMapViewMode viewMode,
            string displayName,
            GeneratedHydrologyCell cell,
            GeneratedHydrologyTile hydrologyTile,
            GeneratedSurfaceWaterTile surfaceWaterTile)
        {
            AddLegendRow(
                displayName,
                WorldGenerationMapSampler.GetHydrologyDisplayColor(
                    viewMode,
                    cell,
                    hydrologyTile,
                    surfaceWaterTile));
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
        /// 連続環境軸の最大側だけを65,535へ設定した凡例用タイルを作ります。
        /// 地図の実色関数へ同じGeneratedTileを渡し、凡例だけ別の配色になることを防ぎます。
        /// </summary>
        /// <param name="viewMode">最大値を設定する連続環境軸です。</param>
        /// <returns>対象フィールドだけが最大値で、他フィールドが0のタイルです。</returns>
        private static GeneratedTile CreateMaximumEnvironmentTile(
            WorldGenerationMapViewMode viewMode)
        {
            GeneratedTile tile = default;
            switch (viewMode)
            {
                case WorldGenerationMapViewMode.Elevation:
                    tile.Elevation = ushort.MaxValue;
                    break;
                case WorldGenerationMapViewMode.BaseTemperature:
                    tile.Temperature = ushort.MaxValue;
                    break;
                case WorldGenerationMapViewMode.TerrainTemperature:
                    tile.TerrainTemperature = ushort.MaxValue;
                    break;
                case WorldGenerationMapViewMode.BaseMoisture:
                    tile.Moisture = ushort.MaxValue;
                    break;
                case WorldGenerationMapViewMode.WoodyVegetationPotential:
                    tile.WoodyVegetationPotential = ushort.MaxValue;
                    break;
                case WorldGenerationMapViewMode.TerrainRuggedness:
                    tile.TerrainRuggedness = ushort.MaxValue;
                    break;
                case WorldGenerationMapViewMode.CoastalInfluence:
                    tile.CoastalInfluence = ushort.MaxValue;
                    break;
                case WorldGenerationMapViewMode.VolcanicActivityPotential:
                    tile.VolcanicActivityPotential = ushort.MaxValue;
                    break;
                case WorldGenerationMapViewMode.GeologyPermeability:
                    tile.GeologyPermeability = ushort.MaxValue;
                    break;
                case WorldGenerationMapViewMode.GeologyGroundwaterStorage:
                    tile.GeologyGroundwaterStorage = ushort.MaxValue;
                    break;
                case WorldGenerationMapViewMode.GeologyErosionResistance:
                    tile.GeologyErosionResistance = ushort.MaxValue;
                    break;
            }

            return tile;
        }

        /// <summary>
        /// 連続環境軸の最小・最大が何を意味するかを、Map Viewerの短い凡例文言へ変換します。
        /// </summary>
        /// <param name="viewMode">凡例を求める連続環境軸です。</param>
        /// <param name="minimumLabel">値0側の説明です。</param>
        /// <param name="maximumLabel">値65,535側の説明です。</param>
        private static void GetContinuousLegendLabels(
            WorldGenerationMapViewMode viewMode,
            out string minimumLabel,
            out string maximumLabel)
        {
            switch (viewMode)
            {
                case WorldGenerationMapViewMode.BaseTemperature:
                case WorldGenerationMapViewMode.TerrainTemperature:
                    minimumLabel = "0 (Cold)";
                    maximumLabel = "65535 (Hot)";
                    return;
                case WorldGenerationMapViewMode.BaseMoisture:
                    minimumLabel = "0 (Dry)";
                    maximumLabel = "65535 (Wet)";
                    return;
                case WorldGenerationMapViewMode.WoodyVegetationPotential:
                    minimumLabel = "0 (Sparse)";
                    maximumLabel = "65535 (Dense)";
                    return;
                case WorldGenerationMapViewMode.TerrainRuggedness:
                    minimumLabel = "0 (Flat)";
                    maximumLabel = "65535 (Rugged)";
                    return;
                case WorldGenerationMapViewMode.CoastalInfluence:
                    minimumLabel = "0 (Inland)";
                    maximumLabel = "65535 (Coastal)";
                    return;
                case WorldGenerationMapViewMode.VolcanicActivityPotential:
                    minimumLabel = "0 (Inactive)";
                    maximumLabel = "65535 (Active)";
                    return;
                case WorldGenerationMapViewMode.GeologyPermeability:
                    minimumLabel = "0 (Impermeable)";
                    maximumLabel = "65535 (Permeable)";
                    return;
                case WorldGenerationMapViewMode.GeologyGroundwaterStorage:
                    minimumLabel = "0 (Low storage)";
                    maximumLabel = "65535 (High storage)";
                    return;
                case WorldGenerationMapViewMode.GeologyErosionResistance:
                    minimumLabel = "0 (Erodible)";
                    maximumLabel = "65535 (Resistant)";
                    return;
                default:
                    minimumLabel = "0 (Low)";
                    maximumLabel = "65535 (High)";
                    return;
            }
        }

        /// <summary>
        /// 連続環境軸の凡例へ、実際の地図色を直接指定した色見本と説明を1行追加します。
        /// </summary>
        /// <param name="displayName">値域端点の意味を示す表示名です。</param>
        /// <param name="color">WorldGenerationMapSamplerと共有する不透明色です。</param>
        private void AddLegendRow(string displayName, Color32 color)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("legend-row");

            VisualElement swatch = new VisualElement();
            swatch.AddToClassList("legend-swatch");
            // UI ToolkitのStyleColorはColor32を直接受け取らないため、同じRGBA値を保ったColorへ明示変換します。
            swatch.style.backgroundColor = (Color)color;
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
            if (displayedTexture == null && generationLabel != null)
            {
                // 初回画像の完成までは地図領域にも同じ進捗を表示します。既存画像があれば再生成中も維持します。
                generationLabel.text = message;
                generationLabel.RemoveFromClassList("hidden");
            }
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
