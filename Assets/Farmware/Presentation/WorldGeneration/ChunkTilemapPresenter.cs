// このファイルは、ECSで生成済みの地形・資源チャンクデータを、バイオームだけを示す一体型土地、岩壁Sprite、液面を含む複数Tilemapへ16×16サブチャンク単位で反映・消去します。
// 既定のチャンク別ローカルTilemapを自動原点変更で再利用し、比較・診断用に旧共有Tilemap方式へ切り替えられます。
// Tilemapは表示専用キャッシュであり、地形はGeneratedTile、資源はGeneratedResourceTileをゲーム状態の正として参照します。
// 分類用の外周1セルは反映バッチごとに同期Burstでまとめ、同じ隣接地形の重複生成を減らします。

using System.Collections.Generic;
using Farmware.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 可視範囲と外側1チャンクの生成済み地形を、共有されたフレーム予算内でTilemapへ反映します。
    /// 先行準備範囲の外またはアンロード済みチャンクも同じ単位で消去し、表示遅れと一括更新による停止を抑えます。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChunkTilemapPresenter : MonoBehaviour
    {
        // ProfilerでTilemapの収集・反映・消去に使ったCPU時間をまとめて確認する計測区間です。
        private static readonly ProfilerMarker PresentationProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.TilemapPresentation");

        // 自動Floating Originの再描画だけを通常のTilemap反映から分離し、Profilerで原点変更フレームの負荷を確認します。
        private static readonly ProfilerMarker FloatingOriginRebaseProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.FloatingOriginRebase");

        // 表示原点変更時のキャッシュ移動と旧岩壁影破棄を、再描画本体と分けて確認する計測区間です。
        private static readonly ProfilerMarker DisplayOriginSynchronizationProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.DisplayOriginSynchronization");

        // ECSチャンク状態から反映・消去Queueを更新する時間を、Tilemap書き込みから分けて確認する計測区間です。
        private static readonly ProfilerMarker PresentationWorkCollectionProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.PresentationWorkCollection");

        // 1サブチャンク256セルの地形・境界・資源表示を再利用配列へ分類する時間を確認する計測区間です。
        private static readonly ProfilerMarker SubChunkClassificationProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.SubChunkClassification");

        // 分類の前処理も含めて比較できるよう、外周地形の同期Burst生成を独立して計測します。
        private static readonly ProfilerMarker TerrainNeighborHaloProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.TerrainNeighborHaloBuild");

        // 分類済み配列を9枚のTilemapへSetTilesBlockする時間を確認する計測区間です。
        private static readonly ProfilerMarker SubChunkTilemapWriteProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.SubChunkTilemapWrite");

        // 旧表示のnull配列を9枚のTilemapへSetTilesBlockする時間を確認する計測区間です。
        private static readonly ProfilerMarker SubChunkTilemapClearProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.SubChunkTilemapClear");

        // 1チャンクの岩壁影をマスク作成からMesh反映まで構築する総時間を確認する計測区間です。
        private static readonly ProfilerMarker RockWallShadowBuildProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.RockWallShadowBuild");

        // チャンク外余白を含む岩壁判定マスクの作成時間を確認する計測区間です。
        private static readonly ProfilerMarker RockWallShadowMaskProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.RockWallShadowMask");

        // 岩壁マスクから最寄り空地距離と不透明度を求める時間を確認する計測区間です。
        private static readonly ProfilerMarker RockWallShadowOpacityProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.RockWallShadowOpacity");

        // 不透明度から影Meshの頂点座標と頂点色を作る時間を確認する計測区間です。
        private static readonly ProfilerMarker RockWallShadowGeometryProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.RockWallShadowGeometry");

        // 影用GameObjectの取得・作成とMeshデータ反映に使う時間を確認する計測区間です。
        private static readonly ProfilerMarker RockWallShadowUploadProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.RockWallShadowUpload");

        // 自動Floating Origin時に既存の岩壁影Meshを新しい相対位置へ動かす時間を確認する計測区間です。
        private static readonly ProfilerMarker RockWallShadowRepositionProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.RockWallShadowReposition");

        // 比較方式で、タイル内容を変更せず既存表示ルートだけを移動した時間です。
        private static readonly ProfilerMarker ChunkTilemapRepositionProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.ChunkTilemapReposition");

        // 生成側の最低プリロード1チャンク内だけを表示も準備します。保持余白全体へ表示を広げないための固定上限です。
        // 速度36では64タイル分を約1.78秒で移動するため、通常の分割反映を画面へ入る前に進められます。
        private const int PresentationPreloadMarginInChunks = 1;

        // URP 2D Rendererで頂点色と透明度を扱えるShader名です。利用できない環境では標準Sprite Shaderへフォールバックします。
        private const string PrimaryRockWallShadowShaderName =
            "Universal Render Pipeline/2D/Sprite-Unlit-Default";

        // URP固有Shaderが見つからない場合にも、黒い頂点色を透明合成できる標準Sprite Shader名です。
        private const string FallbackRockWallShadowShaderName = "Sprites/Default";

        // 上面16pxと前面断面8pxを一体化した土地ブロックを描画します。液体は半セル下げるため別Tilemapへ分けます。
        [Tooltip("水・マグマを除く16×24の土地ブロックを描画する専用Tilemapです。行単位の重なりを正しく処理するためIndividual Modeで使用してください。")]
        [SerializeField] private Tilemap targetTilemap;

        // 水・マグマの静止した基礎色を描画し、論理セルは変えずに表示だけ8px下げます。
        [Tooltip("淡水・海水・マグマの基礎Tile専用Tilemapです。Scene側のTransform Yを-8/16セルへ設定してください。")]
        [SerializeField] private Tilemap liquidBaseTilemap;

        // 岩壁に隠れない通常地面の資源を、岩壁より前へ描画する専用Tilemapです。採取状態の正には使用しません。
        [Tooltip("通常地面の木・石・砂のうち、直前方に岩壁がない資源を描画する専用Tilemapです。岩壁より前のSorting Orderへ設定してください。")]
        [SerializeField] private Tilemap resourceTilemap;

        // 直前方が岩壁の通常資源だけを岩壁より先に描き、壁Spriteと重なる下部を隠す専用Tilemapです。
        [Tooltip("直前方のセルが岩壁である通常資源を描画する専用Tilemapです。岩壁より後ろのSorting Orderへ設定してください。")]
        [SerializeField] private Tilemap resourceBehindRockWallTilemap;

        // 水面とマグマ面の透明アニメーションだけを描画し、静止した基礎色やゲーム状態から分離します。
        [Tooltip("淡水・海水・マグマへ透明な4フレームアニメーションを重ねる専用Tilemapです。静止液面と同じ-8px位置へ設定してください。")]
        [SerializeField] private Tilemap liquidAnimationTilemap;

        // 上面16pxと側面12pxを一体化した岩壁ブロックを、基準面と同じ論理セル位置へ描画します。
        [FormerlySerializedAs("rockWallTopTilemap")]
        [Tooltip("すべての岩壁へ16×28の一体型ブロックを描画する専用Tilemapです。Transformは移動せず、Sprite Pivotで12pxの高さを表します。")]
        [SerializeField] private Tilemap rockWallTilemap;

        // 岩壁上に生成された資源だけを上面と同じ高さへ持ち上げ、側面へ埋まる表示を防ぎます。
        [Tooltip("露出した岩壁上の資源を岩壁上面と同じ12px上へ表示する専用Tilemapです。ゲーム状態の正には使用しません。")]
        [SerializeField] private Tilemap rockWallResourceTilemap;

        // 通常土地の水・マグマ側だけへ境界線を重ね、Biome同士の境界は描画しない専用Tilemapです。
        [Tooltip("通常土地と水・マグマの境界ハイライト専用Tilemapです。Groundより前、資源と岩壁より後ろのSorting Orderにしてください。")]
        [SerializeField] private Tilemap landBoundaryHighlightTilemap;

        // 岩壁上面の外周と内角へ境界線を重ね、側面や通常土地の表示から分離する専用Tilemapです。
        [Tooltip("岩壁上面の境界ハイライト専用Tilemapです。Transform Yを12/16セル上げ、岩壁より前へ描画してください。")]
        [SerializeField] private Tilemap rockWallBoundaryHighlightTilemap;

        // Cameraと全Tilemapで共有する表示原点を提供します。Tilemapセルへ絶対座標を直接書かないために使用します。
        [Tooltip("CameraとTilemapで共有する表示原点の所有者です。未指定時は絶対原点(0, 0)を使用します。")]
        [SerializeField] private CameraPanController cameraPanController;

        // 自動原点変更で既存表示を再利用する既定方式です。OFFは旧共有方式との診断・比較用に残します。
        [Tooltip("既定ONです。64×64チャンクごとのTilemapを保持し、自動原点変更では位置だけ更新します。OFFは旧共有方式で、切替時は表示を作り直します。")]
        [SerializeField] private bool useChunkLocalTilemaps = true;

        // 消去済みの表示単位だけを保持する上限です。可視チャンク数の上限やSimulationの保持範囲ではありません。
        [Min(0)]
        [Tooltip("消去済みチャンク表示を再利用する空きプールの上限（チャンク数）です。小さくするとメモリを減らせますが、再作成が増えます。")]
        [SerializeField] private int maximumPooledChunkPresentations = 8;

        // Inspectorの要求と実際にセルを書いた方式を区別し、切替前の方式で古い表示を消去します。
        private bool chunkLocalTilemapsAreActive;

        // 可視・部分構築・消去途中の表示単位を論理座標で保持します。Simulation状態は所有しません。
        private readonly Dictionary<ChunkCoordinate, ChunkLocalTilemapPresentation> chunkLocalPresentations =
            new Dictionary<ChunkCoordinate, ChunkLocalTilemapPresentation>();

        // 全16サブチャンクの消去が完了した表示単位だけを再貸し出しする、上限付き空きプールです。
        private readonly Stack<ChunkLocalTilemapPresentation> pooledChunkLocalPresentations =
            new Stack<ChunkLocalTilemapPresentation>();

        // 草原Biomeの土地へ使用するTileです。プレイヤー表示にはSoilTypeを反映せず、バイオームの識別だけに使います。
        [Tooltip("草原バイオームの土地に表示するTileです。SoilTypeによる見た目の差は付けません。")]
        [SerializeField] private TileBase grasslandTile;

        // 森林Biomeの土地へ使用するTileです。異なる土壌でも同じ森林表示を維持します。
        [Tooltip("森林バイオームの土地に表示するTileです。SoilTypeによる見た目の差は付けません。")]
        [SerializeField] private TileBase forestTile;

        // 砂漠Biomeの土地へ使用するTileです。異なる土壌でも同じ砂漠表示を維持します。
        [Tooltip("砂漠バイオームの土地に表示するTileです。SoilTypeによる見た目の差は付けません。")]
        [SerializeField] private TileBase desertTile;

        // 岩地Biomeの土地へ使用するTileです。障害物の岩壁Tileとは別で、SoilTypeは表示へ反映しません。
        [Tooltip("岩地バイオームの土地に表示するTileです。岩壁とは別で、SoilTypeによる見た目の差は付けません。")]
        [SerializeField] private TileBase rocklandTile;

        // 湿地Biomeの土地へ使用するTileです。水域セルでは専用Tileを優先し、SoilTypeは表示へ反映しません。
        [Tooltip("湿地バイオームの土地に表示するTileです。川・池・海の水域セルでは専用Tileを優先します。")]
        [SerializeField] private TileBase wetlandTile;

        // 雪原Biomeの土地へ使用するTileです。水域セルでは専用Tileを優先し、SoilTypeは表示へ反映しません。
        [Tooltip("雪原バイオームの土地に表示するTileです。水域セルでは専用Tileを優先します。")]
        [SerializeField] private TileBase snowfieldTile;

        // 火山Biomeの土地へ使用するTileです。マグマセルでは専用Tileを優先し、SoilTypeは表示へ反映しません。
        [Tooltip("火山バイオームの土地に表示するTileです。マグマセルでは専用Tileを優先します。")]
        [SerializeField] private TileBase volcanicTile;

        // GroundType.Magmaへ使用する表示専用Tileで、水域や火山地面とは別のゲームルールを視覚化します。
        [Tooltip("マグマ地形に表示するTileです。通常設備・通常ベルト・通常移動の禁止と、橋の耐熱要件はECSデータで判定します。")]
        [SerializeField] private TileBase magmaTile;

        // WaterSalinity.FreshWaterへ使用する表示専用Tileです。
        [Tooltip("川と池の淡水域に表示するTileです。採水種別はECSデータで判定し、この参照には依存しません。")]
        [SerializeField] private TileBase freshWaterTile;

        // WaterSalinity.SaltWaterへ使用する表示専用Tileです。旧waterTile参照は海水表示として引き継ぎます。
        [FormerlySerializedAs("waterTile")]
        [Tooltip("海の海水域に表示するTileです。採水種別はECSデータで判定し、この参照には依存しません。")]
        [SerializeField] private TileBase saltWaterTile;

        // GroundType.RockWallの全セルへ、上面と12px側面を一体化して表示する共通Tileです。
        [Tooltip("すべての岩壁に表示する16×28の共通Tileです。内部の暗さは影メッシュで後付けします。")]
        [SerializeField] private TileBase rockWallTile;

        // 淡水セルへ重ねる透明なAnimatedTileです。基礎のFreshWater Tileは下層に残します。
        [Tooltip("淡水セルへ重ねる透明なAnimatedTileです。未設定なら淡水は静止表示のままです。")]
        [SerializeField] private TileBase freshWaterAnimationTile;

        // 海水セルへ重ねる透明なAnimatedTileです。基礎のSaltWater Tileは下層に残します。
        [Tooltip("海水セルへ重ねる透明なAnimatedTileです。未設定なら海水は静止表示のままです。")]
        [SerializeField] private TileBase saltWaterAnimationTile;

        // マグマセルへ重ねる透明なAnimatedTileです。基礎のMagma Tileは下層に残します。
        [Tooltip("マグマセルへ重ねる透明なAnimatedTileです。未設定ならマグマは静止表示のままです。")]
        [SerializeField] private TileBase magmaAnimationTile;

        // 224×16画像を左から右へ16pxずつSliceした14枚です。配列順はBoundaryHighlightShapeの1〜14と一致します。
        [Tooltip("境界ハイライト画像を左から右へ分割した14枚のSpriteです。順番を変えると47状態の形が崩れます。")]
        [SerializeField] private Sprite[] boundaryHighlightSprites;

        // 白い境界線へ一様に掛ける不透明度です。PNGの1px形状は変えず、Base Tileとの馴染みだけを調整します。
        [Range(0f, 1f)]
        [Tooltip("境界ハイライトの不透明度です。0で完全透明、1で不透明になり、下げるほどBase Tileへ馴染みます。")]
        [SerializeField] private float boundaryHighlightOpacity =
            TerrainBoundaryHighlightUtility.DefaultOpacity;

        // GeneratedResourceType.Treeへ使用する透明背景の表示専用Tileです。
        [Tooltip("樹木資源に表示する仮Tileです。資源の種類と存在はECSバッファを正とします。")]
        [SerializeField] private TileBase treeResourceTile;

        // GeneratedResourceType.Stoneへ使用する透明背景の表示専用Tileです。
        [Tooltip("石資源に表示する仮Tileです。資源の種類と存在はECSバッファを正とします。")]
        [SerializeField] private TileBase stoneResourceTile;

        // GeneratedResourceType.Sandへ使用する透明背景の表示専用Tileです。
        [Tooltip("砂資源に表示する仮Tileです。砂質土そのものとは別の採取候補表示です。")]
        [SerializeField] private TileBase sandResourceTile;

        // World再作成を検知し、EntityQueryを作り直すために保持する現在のDefault Worldです。
        private World presentationWorld;

        // 座標、状態、タイルBufferを持つチャンクだけを取得する、World寿命中再利用するQueryです。
        private EntityQuery chunkQuery;

        // カメラが要求する可視矩形を取得するSingleton Queryです。
        private EntityQuery observerQuery;

        // AuthoringからBakeされたTilemap適用予算を取得するSingleton Queryです。
        private EntityQuery streamingSettingsQuery;

        // チャンク境界外の岩壁隣接判定を、Simulationと同じ生成規則で行うためのSingleton Queryです。
        private EntityQuery generationSettingsQuery;

        // Queryを現在のWorld向けに作成済みかを示し、未初期化のEntityQueryを使用しないために保持します。
        private bool queriesAreCreated;

        // 現在Tilemapへ反映している固定ワールドレイヤーです。別層へ切り替わったフレームで旧表示を即時消去するために保持します。
        private WorldLayer presentedLayer = WorldLayer.Surface;

        // presentedLayerへObserverの有効値を一度でも記録したかを示し、初回表示で不要な全消去を避けます。
        private bool hasPresentedLayer;

        // 反映途中チャンクのEntityと次のサブチャンク番号を座標別に保持するPresentation専用キャッシュです。
        private readonly Dictionary<ChunkCoordinate, ChunkApplyProgress> applyProgressByCoordinate =
            new Dictionary<ChunkCoordinate, ChunkApplyProgress>();

        // 反映を開始した順番を保ち、1チャンクだけを毎フレーム最初から探し直さないための待ち行列です。
        private readonly Queue<ChunkCoordinate> applyQueue = new Queue<ChunkCoordinate>();

        // 16サブチャンクすべてをTilemapへ反映済みの座標です。Simulationの正データではありません。
        private readonly HashSet<ChunkCoordinate> displayedCoordinates = new HashSet<ChunkCoordinate>();

        // 画面外またはEntity消失により消去が必要な座標を、重複なく保持します。
        private readonly HashSet<ChunkCoordinate> pendingClearCoordinates = new HashSet<ChunkCoordinate>();

        // 現フレームのQueryに存在した座標を再利用し、アンロード差分検出で毎フレームHashSetを確保しないようにします。
        private readonly HashSet<ChunkCoordinate> seenCoordinates = new HashSet<ChunkCoordinate>();

        // 消去を要求した順番と途中位置を保持し、フレーム予算を超えないよう分割処理します。
        private readonly Queue<ChunkClearProgress> clearQueue = new Queue<ChunkClearProgress>();

        // 自動原点変更前の相対セルを画面外で分割消去し、新原点側の表示を先に完成させるための待ち行列です。
        private readonly Queue<StaleOriginClearProgress> staleOriginClearQueue =
            new Queue<StaleOriginClearProgress>();

        // DictionaryとHashSetを走査中に変更しないため、削除対象を一時退避する再利用Listです。
        private readonly List<ChunkCoordinate> coordinateScratch = new List<ChunkCoordinate>();

        // SetTilesBlockへ渡す256要素の再利用配列です。サブチャンクごとのmanaged allocationを防ぎます。
        private TileBase[] subChunkTileBuffer;

        // 消去用のnullだけを持つ256要素配列です。各フレームで作り直しません。
        private TileBase[] emptySubChunkTileBuffer;

        // 資源Tilemapへ渡す256要素の再利用配列です。地形配列と分け、透明セルも同時更新します。
        private TileBase[] resourceSubChunkTileBuffer;

        // 手前岩壁に隠す資源Tilemapへ渡す256要素の再利用配列です。遠方座標でも透明ソート値に依存しません。
        private TileBase[] resourceBehindRockWallSubChunkTileBuffer;

        // 8px下げた静止液面Tilemapへ渡す256要素の再利用配列です。土地セルにはnullを設定します。
        private TileBase[] liquidBaseSubChunkTileBuffer;

        // 液面アニメーションTilemapへ渡す256要素の再利用配列です。通常地形セルにはnullを設定します。
        private TileBase[] liquidAnimationSubChunkTileBuffer;

        // 岩壁セルだけへ一体型ブロックTileを置く256要素の再利用配列です。下層の土地はGround Tilemapへ残します。
        private TileBase[] rockWallSubChunkTileBuffer;

        // 露出した岩壁上の資源だけを12px上へ描画する256要素の再利用配列です。
        private TileBase[] rockWallResourceSubChunkTileBuffer;

        // 通常土地の境界ハイライトTilemapへ渡す256要素の再利用配列です。
        private TileBase[] landBoundaryHighlightSubChunkTileBuffer;

        // 岩壁上面の境界ハイライトTilemapへ渡す256要素の再利用配列です。
        private TileBase[] rockWallBoundaryHighlightSubChunkTileBuffer;

        // 14基本形を4方向へ回した実行時Tileです。このComponentがAwakeで作成し、OnDestroyで破棄します。
        private Tile[,] generatedBoundaryHighlightTiles;

        // 表示済みチャンクへ重ねた影GameObjectとMeshを座標別に保持し、アンロード時に同じ単位で破棄します。
        private readonly Dictionary<ChunkCoordinate, RockWallShadowChunkPresentation>
            rockWallShadowByCoordinate =
                new Dictionary<ChunkCoordinate, RockWallShadowChunkPresentation>();

        // 自動原点変更前から存在し、新原点側の同じ論理チャンクで再利用できる岩壁影座標です。
        // 原点変更フレームだけ保持し、再反映されなかった座標は同じフレーム末に破棄します。
        private readonly HashSet<ChunkCoordinate> reusableRockWallShadowCoordinates =
            new HashSet<ChunkCoordinate>();

        // 影距離の探索半径を含む80×80の岩壁有無です。同期Burst Jobへ貸すためNative配列を使います。
        // Awakeで一度確保し、全チャンクで再利用してOnDestroyで破棄します。未完了Jobは保持しません。
        private NativeArray<bool> rockWallShadowMaskBuffer;

        // 分類用の外周1セルを保持する260要素です。Awakeで確保しOnDestroyで破棄します。
        // チャンクと反映フレームが変わるたびに必要範囲を上書きし、フレームをまたぐキャッシュにはしません。
        private NativeArray<GeneratedTile> terrainNeighborHaloBuffer;

        // チャンク境界の頂点を計算するため、-1〜64の66×66タイル中心の影不透明度を保持する再利用配列です。
        private float[] rockWallShadowTileOpacityBuffer;

        // 64×64タイルの境界を共有する65×65頂点を、チャンクごとのMeshへコピーする再利用配列です。
        private Vector3[] rockWallShadowVertexBuffer;

        // 各頂点の黒色と連続アルファを、チャンクごとのMeshへコピーする再利用配列です。
        private Color32[] rockWallShadowColorBuffer;

        // 64×64の各四角形を2三角形へ分ける固定添字です。全チャンクで同じ配列を再利用します。
        private int[] rockWallShadowTriangleBuffer;

        // 全チャンク影が共有する実行時Materialです。このComponentが作成し、OnDestroyで破棄します。
        private Material generatedRockWallShadowMaterial;

        // 旧Sceneが淡水Tile参照をまだ保存していない場合だけ、既存水面Spriteを着色して作る実行時フォールバックです。
        // 正式なTileアセットが設定されている場合は作成せず、このComponentが作成した場合だけOnDestroyで破棄します。
        private Tile generatedFreshWaterFallbackTile;

        // 現在Tilemapセルと岩壁影Meshの配置に使用している表示原点です。Camera側の変更検出と旧セル消去に使います。
        private DisplayOrigin presentedDisplayOrigin;

        // presentedDisplayOriginを初期化済みか示し、絶対原点(0, 0)自体も有効値として区別します。
        private bool hasPresentedDisplayOrigin;

        /// <summary>
        /// 比較実行時に表示方式を切り替えます。旧方式の表示を消してから切り替え、次のUpdateでECSから再反映します。
        /// </summary>
        /// <param name="enabled">チャンク内ローカルTilemapを使用する場合はtrueです。</param>
        public void ConfigureChunkLocalPresentation(bool enabled)
        {
            if (chunkLocalTilemapsAreActive == enabled && useChunkLocalTilemaps == enabled)
            {
                return;
            }

            ClearAllPresentedChunksImmediately();
            useChunkLocalTilemaps = enabled;
            chunkLocalTilemapsAreActive = enabled;
        }

        /// <summary>
        /// Editor構築ツールまたはテストから、地形・資源TilemapとBiomeごとの基準Tileをまとめて設定します。
        /// ゲームルールは変更せず、ECSのBiomeをどのTileで見せるかだけを更新します。
        /// </summary>
        /// <param name="terrainTilemap">水・マグマを除く生成地形だけを描画する専用Tilemapです。</param>
        /// <param name="generatedResourceTilemap">生成資源だけを描画する専用Tilemapです。</param>
        /// <param name="grassland">草原バイオームの通常地面に表示するTileです。</param>
        /// <param name="forest">森林バイオームの地面に表示するTileです。</param>
        /// <param name="desert">砂漠バイオームの地面に表示するTileです。</param>
        /// <param name="rockland">岩地バイオームの地面に表示するTileです。</param>
        /// <param name="wetland">湿地バイオームの地面に表示するTileです。</param>
        /// <param name="snowfield">雪原バイオームの地面に表示するTileです。</param>
        /// <param name="volcanic">火山バイオームの地面に表示するTileです。</param>
        /// <param name="magma">GroundType.Magmaの全セルに表示するTileです。</param>
        /// <param name="freshWater">淡水の川と池に表示するTileです。</param>
        /// <param name="saltWater">海水の海に表示するTileです。</param>
        /// <param name="rockWall">GroundType.RockWallの全セルで、専用上面Tilemapへ表示する共通Tileです。</param>
        /// <param name="treeResource">樹木資源に表示するTileです。</param>
        /// <param name="stoneResource">石資源に表示するTileです。</param>
        /// <param name="sandResource">砂資源に表示するTileです。</param>
        public void Configure(
            Tilemap terrainTilemap,
            Tilemap generatedResourceTilemap,
            TileBase grassland,
            TileBase forest,
            TileBase desert,
            TileBase rockland,
            TileBase wetland,
            TileBase snowfield,
            TileBase volcanic,
            TileBase magma,
            TileBase freshWater,
            TileBase saltWater,
            TileBase rockWall,
            TileBase treeResource,
            TileBase stoneResource,
            TileBase sandResource)
        {
            targetTilemap = terrainTilemap;
            resourceTilemap = generatedResourceTilemap;
            grasslandTile = grassland;
            forestTile = forest;
            desertTile = desert;
            rocklandTile = rockland;
            wetlandTile = wetland;
            snowfieldTile = snowfield;
            volcanicTile = volcanic;
            magmaTile = magma;
            freshWaterTile = freshWater;
            saltWaterTile = saltWater;
            rockWallTile = rockWall;
            treeResourceTile = treeResource;
            stoneResourceTile = stoneResource;
            sandResourceTile = sandResource;
        }

        /// <summary>
        /// 直前方の岩壁に隠す通常資源専用Tilemapを設定します。
        /// </summary>
        /// <param name="behindRockWallTilemap">岩壁より小さいSorting Orderで描画する資源Tilemapです。</param>
        public void ConfigureResourceOcclusion(Tilemap behindRockWallTilemap)
        {
            resourceBehindRockWallTilemap = behindRockWallTilemap;
        }

        /// <summary>
        /// Editor構築ツールまたはテストから、低い液面、一体型岩壁、液面アニメーション用の表示レイヤー・Tileを設定します。
        /// ECSのGeneratedTileは変更せず、表示の高さだけを組み立てるPresentation設定を保存します。
        /// </summary>
        /// <param name="baseLiquidTilemap">淡水・海水・マグマの静止した基礎Tile専用Tilemapです。</param>
        /// <param name="animatedLiquidTilemap">淡水・海水・マグマの透明アニメーション専用Tilemapです。</param>
        /// <param name="combinedRockWallTilemap">16×28の岩壁ブロックを基準セルへ描画する専用Tilemapです。</param>
        /// <param name="raisedRockWallResourceTilemap">露出した岩壁上の資源を12px持ち上げる専用Tilemapです。</param>
        /// <param name="freshWaterAnimation">淡水へ重ねる透明AnimatedTileです。</param>
        /// <param name="saltWaterAnimation">海水へ重ねる透明AnimatedTileです。</param>
        /// <param name="magmaAnimation">マグマへ重ねる透明AnimatedTileです。</param>
        public void ConfigureTerrainEffects(
            Tilemap baseLiquidTilemap,
            Tilemap animatedLiquidTilemap,
            Tilemap combinedRockWallTilemap,
            Tilemap raisedRockWallResourceTilemap,
            TileBase freshWaterAnimation,
            TileBase saltWaterAnimation,
            TileBase magmaAnimation)
        {
            liquidBaseTilemap = baseLiquidTilemap;
            liquidAnimationTilemap = animatedLiquidTilemap;
            rockWallTilemap = combinedRockWallTilemap;
            rockWallResourceTilemap = raisedRockWallResourceTilemap;
            freshWaterAnimationTile = freshWaterAnimation;
            saltWaterAnimationTile = saltWaterAnimation;
            magmaAnimationTile = magmaAnimation;
        }

        /// <summary>
        /// Editor構築ツールまたはテストから、通常土地・岩壁の境界レイヤーと14基本Spriteを設定します。
        /// 配列は呼び出し元と共有せず、PlayMode中なら回転済みの実行時Tileを直ちに作り直します。
        /// </summary>
        /// <param name="landHighlightTilemap">通常土地の水・マグマ側へ境界を重ねるTilemapです。</param>
        /// <param name="rockWallHighlightTilemap">岩壁上面の外周・内角へ境界を重ねるTilemapです。</param>
        /// <param name="highlightSprites">画像の左から右へShape01〜Shape14の順に並ぶ14枚のSpriteです。</param>
        /// <param name="opacity">白い境界線へ一様に掛ける0〜1の不透明度です。</param>
        public void ConfigureBoundaryHighlights(
            Tilemap landHighlightTilemap,
            Tilemap rockWallHighlightTilemap,
            Sprite[] highlightSprites,
            float opacity = TerrainBoundaryHighlightUtility.DefaultOpacity)
        {
            landBoundaryHighlightTilemap = landHighlightTilemap;
            rockWallBoundaryHighlightTilemap = rockWallHighlightTilemap;
            boundaryHighlightSprites = highlightSprites == null
                ? null
                : (Sprite[])highlightSprites.Clone();
            boundaryHighlightOpacity = Mathf.Clamp01(opacity);

            // EditModeのScene構築中は一時Tileを保存せず、PlayModeでAwake済みのテスト設定だけを即時反映します。
            if (Application.isPlaying)
            {
                RebuildBoundaryHighlightTiles();
            }
        }

        /// <summary>
        /// Editor構築ツールまたはテストから、Cameraと全Tilemapが共有する表示原点の所有者を設定します。
        /// </summary>
        /// <param name="panController">絶対座標から分離した表示原点を保持するCamera Controllerです。</param>
        public void ConfigureDisplayOrigin(CameraPanController panController)
        {
            cameraPanController = panController;
        }

        /// <summary>
        /// Component読み込み直後にUnityから呼ばれ、16×16タイルの再利用配列を1回だけ確保します。
        /// </summary>
        private void Awake()
        {
            int tileCount = WorldGridConstants.SubChunkSize * WorldGridConstants.SubChunkSize;
            subChunkTileBuffer = new TileBase[tileCount];
            emptySubChunkTileBuffer = new TileBase[tileCount];
            resourceSubChunkTileBuffer = new TileBase[tileCount];
            resourceBehindRockWallSubChunkTileBuffer = new TileBase[tileCount];
            liquidBaseSubChunkTileBuffer = new TileBase[tileCount];
            liquidAnimationSubChunkTileBuffer = new TileBase[tileCount];
            rockWallSubChunkTileBuffer = new TileBase[tileCount];
            rockWallResourceSubChunkTileBuffer = new TileBase[tileCount];
            landBoundaryHighlightSubChunkTileBuffer = new TileBase[tileCount];
            rockWallBoundaryHighlightSubChunkTileBuffer = new TileBase[tileCount];
            terrainNeighborHaloBuffer = new NativeArray<GeneratedTile>(TerrainNeighborHaloUtility.TileCount,
                Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            AllocateRockWallShadowBuffers();
            EnsureFreshWaterFallbackTile();
            RebuildBoundaryHighlightTiles();
        }

        /// <summary>
        /// チャンク影の距離場、頂点、色、三角形を組み立てる再利用配列をComponent寿命中に1回だけ確保します。
        /// </summary>
        private void AllocateRockWallShadowBuffers()
        {
            int maskSize = RockWallAppearanceUtility.ShadowMaskSizeInTiles;
            rockWallShadowMaskBuffer = new NativeArray<bool>(maskSize * maskSize,
                Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            int opacitySize = WorldGridConstants.LogicalChunkSize + 2;
            rockWallShadowTileOpacityBuffer = new float[opacitySize * opacitySize];

            int vertexSize = WorldGridConstants.LogicalChunkSize + 1;
            int vertexCount = vertexSize * vertexSize;
            rockWallShadowVertexBuffer = new Vector3[vertexCount];
            rockWallShadowColorBuffer = new Color32[vertexCount];
            rockWallShadowTriangleBuffer = new int[
                WorldGridConstants.LogicalChunkSize
                * WorldGridConstants.LogicalChunkSize
                * 6];
            FillRockWallShadowTriangleBuffer(vertexSize);
        }

        /// <summary>
        /// 64×64の各四角形を、カメラ側を向く2枚の三角形へ分割する固定添字を作ります。
        /// </summary>
        /// <param name="vertexSize">1辺の頂点数です。64タイルでは65です。</param>
        private void FillRockWallShadowTriangleBuffer(int vertexSize)
        {
            int triangleIndex = 0;
            for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
            {
                for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
                {
                    int bottomLeft = (localY * vertexSize) + localX;
                    int bottomRight = bottomLeft + 1;
                    int topLeft = bottomLeft + vertexSize;
                    int topRight = topLeft + 1;

                    // Main Cameraは負Z側から見るため、頂点を時計回りに並べて面の表をCamera側へ向けます。
                    rockWallShadowTriangleBuffer[triangleIndex++] = bottomLeft;
                    rockWallShadowTriangleBuffer[triangleIndex++] = topLeft;
                    rockWallShadowTriangleBuffer[triangleIndex++] = bottomRight;
                    rockWallShadowTriangleBuffer[triangleIndex++] = bottomRight;
                    rockWallShadowTriangleBuffer[triangleIndex++] = topLeft;
                    rockWallShadowTriangleBuffer[triangleIndex++] = topRight;
                }
            }
        }

        /// <summary>
        /// 淡水参照が未保存の旧Sceneでも川と池を海と見分けられるよう、既存水面Spriteから一時Tileを1つだけ作ります。
        /// Editor構築ツールでFreshWater.assetを割り当てた後は、この処理は何も変更しません。
        /// </summary>
        private void EnsureFreshWaterFallbackTile()
        {
            if (freshWaterTile != null || !(saltWaterTile is Tile saltWaterSource))
            {
                return;
            }

            generatedFreshWaterFallbackTile = ScriptableObject.CreateInstance<Tile>();
            generatedFreshWaterFallbackTile.name = "Generated Fresh Water Fallback";
            generatedFreshWaterFallbackTile.sprite = saltWaterSource.sprite;
            generatedFreshWaterFallbackTile.transform = saltWaterSource.transform;
            generatedFreshWaterFallbackTile.flags = saltWaterSource.flags;
            generatedFreshWaterFallbackTile.colliderType = saltWaterSource.colliderType;
            // 元の海水色へ少し緑を残した明色を掛け、仮素材の段階でも淡水と海水を目視で区別します。
            generatedFreshWaterFallbackTile.color = new Color32(190, 255, 230, 255);
            freshWaterTile = generatedFreshWaterFallbackTile;
        }

        /// <summary>
        /// 14枚の基本Spriteから時計回り0〜3回転の実行時Tileを1度だけ作り、セル走査中の生成を避けます。
        /// </summary>
        private void RebuildBoundaryHighlightTiles()
        {
            DestroyGeneratedBoundaryHighlightTiles();
            if (boundaryHighlightSprites == null
                || boundaryHighlightSprites.Length
                    != TerrainBoundaryHighlightUtility.ExpectedSpriteCount)
            {
                return;
            }

            generatedBoundaryHighlightTiles = new Tile[
                TerrainBoundaryHighlightUtility.ExpectedSpriteCount,
                4];
            for (int shapeIndex = 0;
                 shapeIndex < TerrainBoundaryHighlightUtility.ExpectedSpriteCount;
                 shapeIndex++)
            {
                Sprite sprite = boundaryHighlightSprites[shapeIndex];
                if (sprite == null)
                {
                    continue;
                }

                for (int rotation = 0; rotation < 4; rotation++)
                {
                    Tile generatedTile = ScriptableObject.CreateInstance<Tile>();
                    generatedTile.name =
                        $"Generated Boundary Highlight {shapeIndex + 1:00} R{rotation}";
                    generatedTile.sprite = sprite;
                    // 元画像は不透明白のまま保ち、一様なαだけを表示時に掛けて色付きBase Tileへ自然に馴染ませます。
                    generatedTile.color = new Color(
                        1f,
                        1f,
                        1f,
                        Mathf.Clamp01(boundaryHighlightOpacity));
                    generatedTile.colliderType = Tile.ColliderType.None;
                    // Unityの正Z回転は反時計回りなので、負角度で画像を時計回りへ回します。拡大・反転は加えません。
                    generatedTile.transform = Matrix4x4.Rotate(
                        Quaternion.Euler(0f, 0f, -90f * rotation));
                    generatedTile.flags = TileFlags.LockColor | TileFlags.LockTransform;
                    generatedBoundaryHighlightTiles[shapeIndex, rotation] = generatedTile;
                }
            }
        }

        /// <summary>
        /// このComponentが作成した最大56個の境界Tileだけを破棄し、SceneやProjectのSpriteは破棄しません。
        /// </summary>
        private void DestroyGeneratedBoundaryHighlightTiles()
        {
            if (generatedBoundaryHighlightTiles == null)
            {
                return;
            }

            int shapeCount = generatedBoundaryHighlightTiles.GetLength(0);
            int rotationCount = generatedBoundaryHighlightTiles.GetLength(1);
            for (int shapeIndex = 0; shapeIndex < shapeCount; shapeIndex++)
            {
                for (int rotation = 0; rotation < rotationCount; rotation++)
                {
                    Tile generatedTile = generatedBoundaryHighlightTiles[shapeIndex, rotation];
                    if (generatedTile != null)
                    {
                        DestroyUnityObject(generatedTile);
                    }
                }
            }

            generatedBoundaryHighlightTiles = null;
        }

        /// <summary>
        /// MonoBehaviourの各描画フレームで呼ばれ、表示対象の収集と予算分割された反映・消去を実行します。
        /// </summary>
        private void Update()
        {
            using (PresentationProfilerMarker.Auto())
            {
                // Inspectorでの比較切替も同じ消去経路を通し、異なるセル座標方式を混在させません。
                if (useChunkLocalTilemaps != chunkLocalTilemapsAreActive)
                {
                    ConfigureChunkLocalPresentation(useChunkLocalTilemaps);
                }

                if (targetTilemap == null
                    || liquidBaseTilemap == null
                    || resourceTilemap == null
                    || resourceBehindRockWallTilemap == null
                    || landBoundaryHighlightTilemap == null
                    || rockWallBoundaryHighlightTilemap == null)
                {
                    return;
                }

                if (!TryEnsureQueries())
                {
                    return;
                }

                if (observerQuery.CalculateEntityCount() != 1)
                {
                    return;
                }

                DisplayOriginChangeKind displayOriginChange;
                using (DisplayOriginSynchronizationProfilerMarker.Auto())
                {
                    displayOriginChange = SynchronizeDisplayOrigin();
                }

                // Streaming Systemより先にPresenterだけが更新される経路でも、旧SceneのNoneをSurfaceとして扱います。
                // 正規化本体をComponentと共有し、生成側と表示側で異なる検索キーを作らないようにします。
                ChunkStreamingObserver observer = observerQuery
                    .GetSingleton<ChunkStreamingObserver>()
                    .Normalized();

                // 異なるLayerは同じX/Yでも別の論理場所です。通常の消去予算へ回すと旧地形が数フレーム残るため、
                // レイヤー変更フレームだけ全表示を同期消去してから新しいLayerを収集します。
                SynchronizePresentedLayer(observer.Layer);
                bool requiresImmediateVisibleRebuild = displayOriginChange != DisplayOriginChangeKind.None
                    && !(chunkLocalTilemapsAreActive && displayOriginChange == DisplayOriginChangeKind.Automatic);
                using (PresentationWorkCollectionProfilerMarker.Auto())
                {
                    // 共有方式の原点変更と明示移動では、余白まで同期再構築して停止を増やさないよう可視範囲だけに限定します。
                    // 先行準備は次の通常フレームから同じ予算で再開します。
                    CollectPresentationWork(observer, requiresImmediateVisibleRebuild);
                }

                if (requiresImmediateVisibleRebuild)
                {
                    using (FloatingOriginRebaseProfilerMarker.Auto())
                    {
                        // 原点変更直後は新しいCamera周辺を同じフレームに完成させ、空白フレームを見せません。
                        // 自動変更の旧セルは設定閾値以上離れた画面外にあるため、通常予算で後から消去します。
                        int unlimitedVisibleBudget = ProcessClearQueue(int.MaxValue);
                        ProcessApplyQueue(unlimitedVisibleBudget);
                        DestroyUnusedRebasedRockWallShadows();
                        ProcessStaleOriginClearQueue(GetSubChunkBudget());
                    }

                    return;
                }

                int remainingBudget = GetSubChunkBudget();
                remainingBudget = ProcessClearQueue(remainingBudget);
                remainingBudget = ProcessApplyQueue(remainingBudget);
                ProcessStaleOriginClearQueue(remainingBudget);
            }
        }

        /// <summary>
        /// Camera側の表示原点変更を検出し、旧相対セルを片付けて新しい原点で再収集できる状態へ戻します。
        /// 旧共有方式の自動変更では旧セルを分割消去へ移します。既定のローカル方式では位置だけ更新し、反映進捗を保ちます。
        /// 明示テレポートでは両方式とも重なりを避けるため即時消去します。
        /// </summary>
        /// <returns>変更なし、自動変更、明示変更のいずれかです。</returns>
        private DisplayOriginChangeKind SynchronizeDisplayOrigin()
        {
            DisplayOrigin currentOrigin = cameraPanController == null
                ? new DisplayOrigin(int2.zero)
                : cameraPanController.CurrentDisplayOrigin;
            if (!hasPresentedDisplayOrigin)
            {
                presentedDisplayOrigin = currentOrigin;
                hasPresentedDisplayOrigin = true;
                return DisplayOriginChangeKind.None;
            }

            if (DisplayOriginsAreEqual(presentedDisplayOrigin, currentOrigin))
            {
                return DisplayOriginChangeKind.None;
            }

            bool hadPresentedContent = displayedCoordinates.Count > 0
                || applyProgressByCoordinate.Count > 0
                || pendingClearCoordinates.Count > 0
                || rockWallShadowByCoordinate.Count > 0;
            if (hadPresentedContent)
            {
                bool isAutomaticRebase = cameraPanController != null
                    && cameraPanController.LastDisplayOriginChangeWasAutomatic;
                if (isAutomaticRebase)
                {
                    if (chunkLocalTilemapsAreActive)
                    {
                        // 同じ論理チャンクの内容は変化しないため、完成済み集合と構築途中の番号を保持します。
                        // 原点変更だけを理由に分類・SetTilesBlock・旧セル消去を予約しません。
                        using (FloatingOriginRebaseProfilerMarker.Auto())
                        {
                            using (ChunkTilemapRepositionProfilerMarker.Auto())
                            {
                                foreach (KeyValuePair<ChunkCoordinate, ChunkLocalTilemapPresentation> pair in chunkLocalPresentations)
                                {
                                    pair.Value.Reposition(CalculateRockWallShadowMinimumCell(pair.Key, currentOrigin));
                                }
                            }

                            PrepareRockWallShadowsForAutomaticRebase(currentOrigin);
                            reusableRockWallShadowCoordinates.Clear();
                        }
                    }
                    else
                    {
                        MoveCurrentPresentationToStaleClearQueue(
                            presentedDisplayOrigin,
                            currentOrigin);
                    }
                }
                else
                {
                    // 明示テレポートは移動距離が短い場合もあり、旧セルが新Viewportへ重なるため即時消去します。
                    ClearAllPresentedChunksImmediately();
                }
            }

            presentedDisplayOrigin = currentOrigin;
            return cameraPanController != null
                    && cameraPanController.LastDisplayOriginChangeWasAutomatic
                ? DisplayOriginChangeKind.Automatic
                : DisplayOriginChangeKind.Explicit;
        }

        /// <summary>
        /// Observerの論理レイヤー変更を検出し、旧レイヤーから反映したTilemap、途中Queue、岩壁影を同じフレームで消去します。
        /// </summary>
        /// <param name="currentLayer">正規化済みObserverが現在要求している固定ワールドレイヤーです。</param>
        /// <returns>初回記録ではなく、以前と異なるレイヤーへ切り替わった場合はtrueです。</returns>
        private bool SynchronizePresentedLayer(WorldLayer currentLayer)
        {
            if (!hasPresentedLayer)
            {
                presentedLayer = currentLayer;
                hasPresentedLayer = true;
                return false;
            }

            if (presentedLayer == currentLayer)
            {
                return false;
            }

            // レイヤーは重ねて表示する背景ではなく、同じX/Yを別の場所として扱う排他的な表示対象です。
            // 旧表示を1サブチャンクずつ消すとSimulation切替後も残像するため、稀な層移動では原子的な切替を優先します。
            ClearAllPresentedChunksImmediately();
            presentedLayer = currentLayer;
            return true;
        }

        /// <summary>
        /// 現在の原点でTilemapへ書いた可能性があるチャンクを、旧原点付きの分割消去Queueへ移します。
        /// 新しい可視チャンクと同じ論理座標でも別の相対セルへ書けるよう、通常の表示管理キャッシュは直ちに空にします。
        /// 岩壁影は論理チャンク内容が変わらないため、新原点へ移動して再利用候補として保持します。
        /// </summary>
        /// <param name="staleOrigin">旧Tilemapセルの計算に使用した表示原点です。</param>
        /// <param name="currentOrigin">Cameraが変更した新しい表示原点です。</param>
        private void MoveCurrentPresentationToStaleClearQueue(
            DisplayOrigin staleOrigin,
            DisplayOrigin currentOrigin)
        {
            coordinateScratch.Clear();
            foreach (ChunkCoordinate coordinate in displayedCoordinates)
            {
                coordinateScratch.Add(coordinate);
            }

            foreach (ChunkCoordinate coordinate in applyProgressByCoordinate.Keys)
            {
                if (!coordinateScratch.Contains(coordinate))
                {
                    coordinateScratch.Add(coordinate);
                }
            }

            foreach (ChunkCoordinate coordinate in pendingClearCoordinates)
            {
                if (!coordinateScratch.Contains(coordinate))
                {
                    coordinateScratch.Add(coordinate);
                }
            }

            // 影Meshの頂点はチャンク内ローカル座標なので、GameObjectの位置だけを新原点へ合わせれば再生成せず使えます。
            // 同じフレームに新Tilemapも完成するため、Cameraへ古い位置の影だけが描画される中間フレームは発生しません。
            PrepareRockWallShadowsForAutomaticRebase(currentOrigin);
            applyProgressByCoordinate.Clear();
            applyQueue.Clear();
            displayedCoordinates.Clear();
            pendingClearCoordinates.Clear();
            seenCoordinates.Clear();
            clearQueue.Clear();

            for (int index = 0; index < coordinateScratch.Count; index++)
            {
                staleOriginClearQueue.Enqueue(
                    new StaleOriginClearProgress(
                        coordinateScratch[index],
                        staleOrigin));
            }

            coordinateScratch.Clear();
        }

        /// <summary>
        /// 2つの表示原点が同じ絶対タイルをローカルセル(0, 0)へ対応させるか比較します。
        /// </summary>
        /// <param name="left">現在Tilemapへ使用中の表示原点です。</param>
        /// <param name="right">Cameraが現在提供している表示原点です。</param>
        /// <returns>水平・垂直の両座標が一致する場合はtrueです。</returns>
        private static bool DisplayOriginsAreEqual(DisplayOrigin left, DisplayOrigin right)
        {
            return left.AbsoluteTileCoordinate.x == right.AbsoluteTileCoordinate.x
                && left.AbsoluteTileCoordinate.y == right.AbsoluteTileCoordinate.y;
        }

        /// <summary>
        /// Componentが無効化されたときにUnityから呼ばれ、このComponentが表示したタイルだけを即時消去します。
        /// 再有効化時にはActiveなECSデータから再構築できるため、Tilemapを状態保存には使用しません。
        /// </summary>
        private void OnDisable()
        {
            ClearAllPresentedChunksImmediately();
        }

        /// <summary>
        /// Component破棄時にUnityから呼ばれ、EntityQuery、影マスクのNative配列、所有する描画Objectを解放します。
        /// </summary>
        private void OnDestroy()
        {
            DisposeQueries();
            DestroyAllRockWallShadows();
            DestroyChunkLocalPresentations();
            DestroyGeneratedBoundaryHighlightTiles();

            // マスクJobは呼び出し内で終了するため、破棄時にWorkerの完了を待つ必要はありません。
            if (rockWallShadowMaskBuffer.IsCreated)
            {
                rockWallShadowMaskBuffer.Dispose();
            }

            // 分類用外周も同期Runだけで使用するため、この時点で配列を借りている未完了Jobはありません。
            if (terrainNeighborHaloBuffer.IsCreated)
            {
                terrainNeighborHaloBuffer.Dispose();
            }

            // 各影Meshは共有Materialを所有しないため、Presenterが作成したMaterialだけを最後に1回破棄します。
            if (generatedRockWallShadowMaterial != null)
            {
                DestroyUnityObject(generatedRockWallShadowMaterial);
                generatedRockWallShadowMaterial = null;
            }

            // Sceneアセットから参照したTileは破棄せず、このComponentがAwakeで作った一時Tileだけを所有者として破棄します。
            if (generatedFreshWaterFallbackTile != null)
            {
                DestroyUnityObject(generatedFreshWaterFallbackTile);
                generatedFreshWaterFallbackTile = null;
            }
        }

        /// <summary>
        /// Default World向けQueryを用意し、Play Mode終了やWorld再作成時には古いQueryを破棄します。
        /// </summary>
        /// <returns>有効なWorldとQueryを利用できる場合はtrueです。</returns>
        private bool TryEnsureQueries()
        {
            World currentWorld = World.DefaultGameObjectInjectionWorld;
            bool currentWorldIsUsable = currentWorld != null && currentWorld.IsCreated;
            if (queriesAreCreated
                && presentationWorld == currentWorld
                && currentWorldIsUsable)
            {
                return true;
            }

            // Queryを作り直す状況では、旧Worldの表示キャッシュを先に完全消去します。
            // Worldが一時的にnullまたは破棄済みになった場合も、別シードの古いTileを画面へ残さないために必要です。
            if (queriesAreCreated)
            {
                ClearAllPresentedChunksImmediately();
            }

            DisposeQueries();
            if (!currentWorldIsUsable)
            {
                return false;
            }

            presentationWorld = currentWorld;
            EntityManager entityManager = currentWorld.EntityManager;
            chunkQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ChunkCoordinateComponent>(),
                ComponentType.ReadOnly<ChunkGenerationStatus>(),
                ComponentType.ReadOnly<GeneratedTile>(),
                ComponentType.ReadOnly<GeneratedResourceTile>());
            observerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ChunkStreamingObserver>());
            streamingSettingsQuery =
                entityManager.CreateEntityQuery(ComponentType.ReadOnly<ChunkStreamingSettings>());
            generationSettingsQuery =
                entityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldGenerationSettings>());
            queriesAreCreated = true;
            return true;
        }

        /// <summary>
        /// 現在のECSチャンクを調べ、可視範囲と外側1チャンクの先行反映、範囲外・消失チャンクの消去を予約します。
        /// </summary>
        /// <param name="observer">カメラが要求する可視チャンク包含矩形です。</param>
        /// <param name="visibleOnly">原点変更の同期反映を可視範囲だけに抑える場合はtrueです。</param>
        private void CollectPresentationWork(ChunkStreamingObserver observer, bool visibleOnly)
        {
            EntityManager entityManager = presentationWorld.EntityManager;
            using NativeArray<Entity> entities = chunkQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<ChunkCoordinateComponent> coordinates =
                chunkQuery.ToComponentDataArray<ChunkCoordinateComponent>(Allocator.Temp);
            using NativeArray<ChunkGenerationStatus> statuses =
                chunkQuery.ToComponentDataArray<ChunkGenerationStatus>(Allocator.Temp);
            seenCoordinates.Clear();

            // ECS Queryの列挙順で画面外が先頭になることがあるため、可視集合を先に予約します。
            // 2回とも既存の配列を読むだけで、別のソート配列や毎フレームのmanaged allocationは追加しません。
            for (int pass = 0; pass < 2; pass++)
            {
                for (int index = 0; index < entities.Length; index++)
                {
                    ChunkCoordinate coordinate = new ChunkCoordinate(
                        coordinates[index].Layer,
                        coordinates[index].X,
                        coordinates[index].Y);
                    bool isVisible = Contains(observer, coordinate, 0);
                    if (isVisible != (pass == 0))
                    {
                        continue;
                    }

                    ChunkGenerationStatus status = statuses[index];
                    seenCoordinates.Add(coordinate);
                    bool isInsidePreparation = isVisible || (!visibleOnly
                        && Contains(observer, coordinate, PresentationPreloadMarginInChunks));
                    // Streamingは画面外の完成データをInactiveへ進めます。表示準備の有効性をActiveだけで判断すると
                    // 毎回消去・再構築してしまうため、確定Bufferを持つInactiveも再利用します。
                    bool hasCompleteData = status.Phase == ChunkGenerationPhase.ReadyToApply
                        || status.Phase == ChunkGenerationPhase.Active
                        || status.Phase == ChunkGenerationPhase.Inactive;

                    if (isInsidePreparation && hasCompleteData)
                    {
                        ScheduleApply(coordinate, entities[index]);
                    }
                    else
                    {
                        CancelApplyAndScheduleClearIfNeeded(coordinate);
                    }
                }
            }

            // Entityがアンロードで消えた座標はQuery結果に現れないため、表示キャッシュとの差分から消去を予約します。
            coordinateScratch.Clear();
            foreach (ChunkCoordinate coordinate in displayedCoordinates)
            {
                if (!seenCoordinates.Contains(coordinate))
                {
                    coordinateScratch.Add(coordinate);
                }
            }

            foreach (ChunkCoordinate coordinate in applyProgressByCoordinate.Keys)
            {
                if (!seenCoordinates.Contains(coordinate) && !coordinateScratch.Contains(coordinate))
                {
                    coordinateScratch.Add(coordinate);
                }
            }

            for (int index = 0; index < coordinateScratch.Count; index++)
            {
                CancelApplyAndScheduleClearIfNeeded(coordinateScratch[index]);
            }
        }

        /// <summary>
        /// 未表示の可視・先行準備チャンクを反映待ちへ追加し、すでに待機中なら最新Entity参照へ更新します。
        /// </summary>
        /// <param name="coordinate">反映する論理チャンク座標です。</param>
        /// <param name="entity">GeneratedTile Bufferを所有する現在のEntityです。</param>
        private void ScheduleApply(ChunkCoordinate coordinate, Entity entity)
        {
            if (displayedCoordinates.Contains(coordinate) || pendingClearCoordinates.Contains(coordinate))
            {
                return;
            }

            if (applyProgressByCoordinate.TryGetValue(coordinate, out ChunkApplyProgress progress))
            {
                progress.Entity = entity;
                return;
            }

            applyProgressByCoordinate.Add(coordinate, new ChunkApplyProgress(entity));
            applyQueue.Enqueue(coordinate);
        }

        /// <summary>
        /// 途中反映を中止し、1セルでも書き込んだ可能性がある座標を消去待ちへ追加します。
        /// </summary>
        /// <param name="coordinate">画面外またはアンロードになった論理チャンク座標です。</param>
        private void CancelApplyAndScheduleClearIfNeeded(ChunkCoordinate coordinate)
        {
            bool hadApplyProgress = applyProgressByCoordinate.Remove(coordinate);
            bool wasDisplayed = displayedCoordinates.Contains(coordinate);
            if (!hadApplyProgress && !wasDisplayed)
            {
                return;
            }

            if (pendingClearCoordinates.Add(coordinate))
            {
                clearQueue.Enqueue(new ChunkClearProgress(coordinate));
            }
        }

        /// <summary>
        /// 消去待ちを16×16単位で処理し、使用しなかった残り予算を返します。
        /// </summary>
        /// <param name="remainingBudget">このフレームで処理できる残りサブチャンク数です。</param>
        /// <returns>消去後に反映処理へ渡せる残り予算です。</returns>
        private int ProcessClearQueue(int remainingBudget)
        {
            while (remainingBudget > 0 && clearQueue.Count > 0)
            {
                ChunkClearProgress progress = clearQueue.Peek();
                ClearSubChunk(progress.Coordinate, progress.NextSubChunkIndex);
                progress.NextSubChunkIndex++;
                remainingBudget--;

                if (progress.NextSubChunkIndex >= WorldGridConstants.SubChunksPerChunk)
                {
                    clearQueue.Dequeue();
                    pendingClearCoordinates.Remove(progress.Coordinate);
                    displayedCoordinates.Remove(progress.Coordinate);
                    ReleaseChunkLocalPresentation(progress.Coordinate);
                }
            }

            return remainingBudget;
        }

        /// <summary>
        /// 反映待ちを16×16単位で処理し、1チャンク完了時だけECS状態をActiveへ進めます。
        /// </summary>
        /// <param name="remainingBudget">このフレームで処理できる残りサブチャンク数です。</param>
        /// <returns>反映後に旧原点セルの消去へ渡せる残り予算です。</returns>
        private int ProcessApplyQueue(int remainingBudget)
        {
            if (generationSettingsQuery.CalculateEntityCount() != 1)
            {
                // 境界の隣接地形をSimulationと同じ設定で判定できない間は、誤った影を確定せず次フレームへ待ちます。
                return remainingBudget;
            }

            WorldGenerationSettings generationSettings =
                generationSettingsQuery.GetSingleton<WorldGenerationSettings>();
            EntityManager entityManager = presentationWorld.EntityManager;
            // 同一呼び出し内の連続サブチャンクだけで外周を共有します。次フレームは必ず現在の設定で再生成します。
            ChunkApplyProgress preparedProgress = null;
            while (remainingBudget > 0 && applyQueue.Count > 0)
            {
                ChunkCoordinate coordinate = applyQueue.Peek();
                if (!applyProgressByCoordinate.TryGetValue(coordinate, out ChunkApplyProgress progress))
                {
                    // Queueには残っていても画面外移動でDictionaryから除かれた項目は、安全に読み飛ばします。
                    applyQueue.Dequeue();
                    continue;
                }

                if (!entityManager.Exists(progress.Entity)
                    || !entityManager.HasBuffer<GeneratedTile>(progress.Entity)
                    || !entityManager.HasBuffer<GeneratedResourceTile>(progress.Entity))
                {
                    CancelApplyAndScheduleClearIfNeeded(coordinate);
                    applyQueue.Dequeue();
                    continue;
                }

                DynamicBuffer<GeneratedTile> tiles = entityManager.GetBuffer<GeneratedTile>(progress.Entity, true);
                DynamicBuffer<GeneratedResourceTile> resources =
                    entityManager.GetBuffer<GeneratedResourceTile>(progress.Entity, true);
                if (tiles.Length != WorldGridConstants.TilesPerChunk
                    || resources.Length != WorldGridConstants.TilesPerChunk)
                {
                    // Job完了前または不正バッファは描画せず、次フレームに再確認します。
                    return remainingBudget;
                }

                if (preparedProgress != progress)
                {
                    int batchCount = math.min(remainingBudget,
                        WorldGridConstants.SubChunksPerChunk - progress.NextSubChunkIndex);
                    using (TerrainNeighborHaloProfilerMarker.Auto())
                    {
                        TerrainNeighborHaloUtility.Fill(generationSettings, coordinate,
                            progress.NextSubChunkIndex, batchCount, terrainNeighborHaloBuffer);
                    }

                    preparedProgress = progress;
                }

                ApplySubChunk(
                    coordinate,
                    progress.NextSubChunkIndex,
                    tiles,
                    resources);
                progress.NextSubChunkIndex++;
                remainingBudget--;

                if (progress.NextSubChunkIndex >= WorldGridConstants.SubChunksPerChunk)
                {
                    // 自動原点変更では同じ論理チャンクの既存Meshを使い、地形が新規または変化した通常反映だけ再構築します。
                    // 全4096タイルがそろった時点で1チャンク1Meshを作り、サブチャンクごとのGameObject生成を避けます。
                    if (!TryReuseRockWallShadowAfterAutomaticRebase(coordinate))
                    {
                        BuildRockWallShadowMesh(coordinate, tiles, generationSettings);
                    }

                    applyProgressByCoordinate.Remove(coordinate);
                    applyQueue.Dequeue();
                    displayedCoordinates.Add(coordinate);

                    ChunkGenerationStatus status = entityManager.GetComponentData<ChunkGenerationStatus>(progress.Entity);
                    if (status.Phase == ChunkGenerationPhase.ReadyToApply)
                    {
                        status.Phase = ChunkGenerationPhase.Active;
                        entityManager.SetComponentData(progress.Entity, status);
                    }
                }
            }

            return remainingBudget;
        }

        /// <summary>
        /// 自動Floating Origin前の画面外セルを、通常のサブチャンク予算内で古い表示原点から消去します。
        /// 新しい可視セルの反映を先に行うことで、掃除のために現在画面へ空白を作らないようにします。
        /// </summary>
        /// <param name="remainingBudget">このフレームで処理できる残りサブチャンク数です。</param>
        /// <returns>消去後の残り予算です。</returns>
        private int ProcessStaleOriginClearQueue(int remainingBudget)
        {
            while (remainingBudget > 0 && staleOriginClearQueue.Count > 0)
            {
                StaleOriginClearProgress progress = staleOriginClearQueue.Peek();
                ClearSubChunk(
                    progress.Coordinate,
                    progress.NextSubChunkIndex,
                    progress.DisplayOrigin,
                    false);
                progress.NextSubChunkIndex++;
                remainingBudget--;

                if (progress.NextSubChunkIndex >= WorldGridConstants.SubChunksPerChunk)
                {
                    staleOriginClearQueue.Dequeue();
                }
            }

            return remainingBudget;
        }

        /// <summary>
        /// 1つの16×16サブチャンクをDynamic Bufferから再利用配列へコピーし、Tilemapへ一括反映します。
        /// </summary>
        /// <param name="coordinate">対象の論理チャンク座標です。</param>
        /// <param name="subChunkIndex">行優先の0〜15サブチャンク番号です。</param>
        /// <param name="tiles">4096件の生成済みタイルBufferです。</param>
        /// <param name="resources">地形と同じ行優先4096件の生成済み資源Bufferです。</param>
        private void ApplySubChunk(
            ChunkCoordinate coordinate,
            int subChunkIndex,
            DynamicBuffer<GeneratedTile> tiles,
            DynamicBuffer<GeneratedResourceTile> resources)
        {
            int subChunkX = subChunkIndex % WorldGridConstants.SubChunksPerAxis;
            int subChunkY = subChunkIndex / WorldGridConstants.SubChunksPerAxis;
            int localStartX = subChunkX * WorldGridConstants.SubChunkSize;
            int localStartY = subChunkY * WorldGridConstants.SubChunkSize;

            using (SubChunkClassificationProfilerMarker.Auto())
            {
                for (int localY = 0; localY < WorldGridConstants.SubChunkSize; localY++)
                {
                    for (int localX = 0; localX < WorldGridConstants.SubChunkSize; localX++)
                    {
                        int sourceIndex = WorldGrid.ToTileIndex(localStartX + localX, localStartY + localY);
                        int destinationIndex = (localY * WorldGridConstants.SubChunkSize) + localX;
                        int tileLocalX = localStartX + localX;
                        int tileLocalY = localStartY + localY;
                        GeneratedTile tile = tiles[sourceIndex];
                        RockWallAppearance rockWallAppearance = ClassifyRockWallAppearance(
                            tileLocalX,
                            tileLocalY,
                            tile,
                            tiles,
                            terrainNeighborHaloBuffer);
                        bool isLiquid = tile.Ground == GroundType.Water
                            || tile.Ground == GroundType.Magma;

                        BoundaryHighlightSelection landBoundarySelection = default;
                        BoundaryHighlightSelection rockWallBoundarySelection = default;
                        if (tile.Ground == GroundType.Land || tile.Ground == GroundType.RockWall)
                        {
                            // 内部は現在のECS Buffer、外周は同じ反映バッチで一度だけ生成した値を読み、ロード順へ依存しません。
                            TerrainNeighborTiles neighbors = GetTerrainNeighborTiles(
                                tileLocalX,
                                tileLocalY,
                                tiles,
                                terrainNeighborHaloBuffer);
                            if (tile.Ground == GroundType.Land)
                            {
                                landBoundarySelection =
                                    TerrainBoundaryHighlightUtility.SelectLand(tile, neighbors);
                            }
                            else
                            {
                                rockWallBoundarySelection =
                                    TerrainBoundaryHighlightUtility.SelectRockWall(tile, neighbors);
                            }
                        }

                        // 土地Spriteは上面と断面を一体化し、液体だけを別Tilemapで8px下へ投影します。
                        // Individual ModeとTop Left順により、画面奥の行から先に描きます。
                        // 画面手前の土地ブロックを後から重ねることで、奥側の不要な断面を自然に隠します。
                        subChunkTileBuffer[destinationIndex] = isLiquid
                            ? null
                            : SelectLandTile(tile);
                        liquidBaseSubChunkTileBuffer[destinationIndex] = isLiquid
                            ? SelectLiquidBaseTile(tile)
                            : null;
                        liquidAnimationSubChunkTileBuffer[destinationIndex] =
                            SelectLiquidAnimationTile(tile);

                        // 岩壁セルにもBiome由来の16×24土地ブロックを残し、その上へ16×28岩壁ブロックを重ねます。
                        // 岩壁SpriteのPivotが側面12px分の高さを表すため、Tilemap Transform自体は基準面から動かしません。
                        bool isRockWall = tile.Ground == GroundType.RockWall;
                        rockWallSubChunkTileBuffer[destinationIndex] = isRockWall
                            ? rockWallTile
                            : null;
                        landBoundaryHighlightSubChunkTileBuffer[destinationIndex] =
                            SelectBoundaryHighlightTile(landBoundarySelection);
                        rockWallBoundaryHighlightSubChunkTileBuffer[destinationIndex] =
                            SelectBoundaryHighlightTile(rockWallBoundarySelection);

                        // 内部影に覆われる資源アイコンだけを隠します。ECSの資源データと露出面の表示は維持します。
                        TileBase selectedResource =
                            RockWallAppearanceUtility.ShouldDisplayResource(rockWallAppearance)
                                ? SelectResourceTile(resources[sourceIndex])
                                : null;
                        // TilemapRendererの行順は別Tilemap間を交互に並べないため、遠方座標でも確実な固定レイヤーへ振り分けます。
                        // 画面手前はワールドYが小さい方向なので、直下1セルが岩壁なら資源を壁より先に描いて重なる部分を隠します。
                        bool isBehindFrontRockWall = false;
                        if (!isRockWall && selectedResource != null)
                        {
                            GeneratedTile frontTerrainTile = TerrainNeighborHaloUtility.GetTile(
                                tileLocalX,
                                tileLocalY - 1,
                                tiles,
                                terrainNeighborHaloBuffer);
                            isBehindFrontRockWall =
                                TerrainLayerPresentationUtility.ShouldRenderResourceBehindRockWall(
                                    resources[sourceIndex],
                                    tile,
                                    frontTerrainTile);
                        }
                        resourceSubChunkTileBuffer[destinationIndex] = !isRockWall
                            && !isBehindFrontRockWall
                                ? selectedResource
                                : null;
                        resourceBehindRockWallSubChunkTileBuffer[destinationIndex] =
                            isBehindFrontRockWall
                                ? selectedResource
                                : null;
                        rockWallResourceSubChunkTileBuffer[destinationIndex] = isRockWall
                            ? selectedResource
                            : null;
                    }
                }
            }

            using (SubChunkTilemapWriteProfilerMarker.Auto())
            {
                BoundsInt bounds = CreateSubChunkBounds(coordinate, subChunkIndex);
                WriteSubChunkLayer(coordinate, targetTilemap, bounds, subChunkTileBuffer);
                WriteSubChunkLayer(coordinate, liquidBaseTilemap, bounds, liquidBaseSubChunkTileBuffer);
                WriteSubChunkLayer(coordinate, resourceTilemap, bounds, resourceSubChunkTileBuffer);
                WriteSubChunkLayer(coordinate, resourceBehindRockWallTilemap,
                    bounds,
                    resourceBehindRockWallSubChunkTileBuffer);
                if (liquidAnimationTilemap != null)
                {
                    WriteSubChunkLayer(coordinate, liquidAnimationTilemap, bounds, liquidAnimationSubChunkTileBuffer);
                }

                if (rockWallTilemap != null)
                {
                    WriteSubChunkLayer(coordinate, rockWallTilemap, bounds, rockWallSubChunkTileBuffer);
                }

                if (rockWallResourceTilemap != null)
                {
                    WriteSubChunkLayer(coordinate, rockWallResourceTilemap, bounds, rockWallResourceSubChunkTileBuffer);
                }

                WriteSubChunkLayer(coordinate, landBoundaryHighlightTilemap,
                    bounds,
                    landBoundaryHighlightSubChunkTileBuffer);
                WriteSubChunkLayer(coordinate, rockWallBoundaryHighlightTilemap,
                    bounds,
                    rockWallBoundaryHighlightSubChunkTileBuffer);
            }
        }

        /// <summary>
        /// 1レイヤーの分類済み配列を反映します。比較ONだけ既知の空領域へのnull書込を省き、共有方式は従来どおり反映します。
        /// </summary>
        /// <param name="coordinate">対象チャンクの絶対座標です。</param>
        /// <param name="template">対象レイヤーのScene見本です。未設定なら反映しません。</param>
        /// <param name="bounds">現在の表示方式で作成した16×16のセル範囲です。</param>
        /// <param name="tiles">全256セルの分類済み配列です。呼び出し中だけ参照します。</param>
        /// <returns>SetTilesBlockを実行した場合はtrue、省略した場合はfalseです。</returns>
        private bool WriteSubChunkLayer(ChunkCoordinate coordinate, Tilemap template, BoundsInt bounds, TileBase[] tiles)
        {
            if (template == null) return false;
            Tilemap target = GetTilemapForChunk(coordinate, template, true);
            if (chunkLocalTilemapsAreActive)
            {
                // 空であるという履歴はチャンク表示が所有します。絶対座標で別Dictionaryを持たず、プールと一緒に寿命を管理します。
                return chunkLocalPresentations[coordinate].WriteSubChunk(template, bounds, tiles);
            }

            // 共有方式は旧原点の遅延消去とセル範囲が交差し得るため、この局所的な履歴管理を適用しません。
            target.SetTilesBlock(bounds, tiles);
            return true;
        }

        /// <summary>
        /// 1つの16×16サブチャンクをnull配列で上書きし、表示オブジェクトだけを消去します。
        /// </summary>
        /// <param name="coordinate">対象の論理チャンク座標です。</param>
        /// <param name="subChunkIndex">行優先の0〜15サブチャンク番号です。</param>
        private void ClearSubChunk(ChunkCoordinate coordinate, int subChunkIndex)
        {
            ClearSubChunk(
                coordinate,
                subChunkIndex,
                presentedDisplayOrigin,
                true);
        }

        /// <summary>
        /// 指定した表示原点で配置した16×16サブチャンクをnull配列で上書きします。
        /// 自動Floating Origin後の旧セルは現在の岩壁影Dictionaryと別寿命なので、影破棄を無効にして使用します。
        /// </summary>
        /// <param name="coordinate">対象の論理チャンク座標です。</param>
        /// <param name="subChunkIndex">行優先の0〜15サブチャンク番号です。</param>
        /// <param name="cellDisplayOrigin">対象セルを書き込んだ時点の表示原点です。</param>
        /// <param name="destroyCurrentShadow">現在の論理座標に対応する岩壁影も破棄する場合はtrueです。</param>
        private void ClearSubChunk(
            ChunkCoordinate coordinate,
            int subChunkIndex,
            DisplayOrigin cellDisplayOrigin,
            bool destroyCurrentShadow)
        {
            if (subChunkIndex == 0 && destroyCurrentShadow)
            {
                // Tilemap消去を分割している間に古い影だけが残らないよう、チャンク影は消去開始時にまとめて外します。
                DestroyRockWallShadow(coordinate);
            }

            using (SubChunkTilemapClearProfilerMarker.Auto())
            {
                if (chunkLocalTilemapsAreActive)
                {
                    if (chunkLocalPresentations.TryGetValue(coordinate, out ChunkLocalTilemapPresentation presentation))
                    {
                        // 消去中の断片が画面へ残らないよう、最初に非表示にしてから通常予算でセルを空にします。
                        presentation.ClearSubChunk(
                            CreateSubChunkBounds(coordinate, subChunkIndex),
                            emptySubChunkTileBuffer);
                    }

                    return;
                }

                BoundsInt bounds = CreateSubChunkBounds(
                    coordinate,
                    subChunkIndex,
                    cellDisplayOrigin);
                if (targetTilemap != null)
                {
                    targetTilemap.SetTilesBlock(bounds, emptySubChunkTileBuffer);
                }

                if (resourceTilemap != null)
                {
                    resourceTilemap.SetTilesBlock(bounds, emptySubChunkTileBuffer);
                }

                if (resourceBehindRockWallTilemap != null)
                {
                    resourceBehindRockWallTilemap.SetTilesBlock(bounds, emptySubChunkTileBuffer);
                }

                if (liquidBaseTilemap != null)
                {
                    liquidBaseTilemap.SetTilesBlock(bounds, emptySubChunkTileBuffer);
                }

                if (liquidAnimationTilemap != null)
                {
                    liquidAnimationTilemap.SetTilesBlock(bounds, emptySubChunkTileBuffer);
                }

                if (rockWallTilemap != null)
                {
                    rockWallTilemap.SetTilesBlock(bounds, emptySubChunkTileBuffer);
                }

                if (rockWallResourceTilemap != null)
                {
                    rockWallResourceTilemap.SetTilesBlock(bounds, emptySubChunkTileBuffer);
                }

                if (landBoundaryHighlightTilemap != null)
                {
                    landBoundaryHighlightTilemap.SetTilesBlock(bounds, emptySubChunkTileBuffer);
                }

                if (rockWallBoundaryHighlightTilemap != null)
                {
                    rockWallBoundaryHighlightTilemap.SetTilesBlock(bounds, emptySubChunkTileBuffer);
                }
            }
        }

        /// <summary>
        /// Component無効化時に1チャンク分を即時消去し、次回有効化時の古い表示を残しません。
        /// </summary>
        /// <param name="coordinate">消去する論理チャンク座標です。</param>
        private void ClearWholeChunkImmediately(ChunkCoordinate coordinate)
        {
            ClearWholeChunkImmediately(
                coordinate,
                presentedDisplayOrigin,
                true);
        }

        /// <summary>
        /// 指定表示原点で配置した1チャンク分を即時消去します。
        /// World切替時には、分割消去途中の旧原点セルも残さず片付けるために使用します。
        /// </summary>
        /// <param name="coordinate">消去する論理チャンク座標です。</param>
        /// <param name="cellDisplayOrigin">対象セルを書き込んだ時点の表示原点です。</param>
        /// <param name="destroyCurrentShadow">現在の論理座標に対応する岩壁影も破棄する場合はtrueです。</param>
        private void ClearWholeChunkImmediately(
            ChunkCoordinate coordinate,
            DisplayOrigin cellDisplayOrigin,
            bool destroyCurrentShadow)
        {
            for (int subChunkIndex = 0;
                 subChunkIndex < WorldGridConstants.SubChunksPerChunk;
                 subChunkIndex++)
            {
                ClearSubChunk(
                    coordinate,
                    subChunkIndex,
                    cellDisplayOrigin,
                    destroyCurrentShadow);
            }
        }

        /// <summary>
        /// 現在のWorldから描画した可能性がある全チャンクを即時消去し、表示キャッシュと岩壁影を初期状態へ戻します。
        /// Component無効化とDefault World切り替えの両方から呼ばれ、TilemapをSimulation状態として残さないために使用します。
        /// </summary>
        private void ClearAllPresentedChunksImmediately()
        {
            coordinateScratch.Clear();
            foreach (ChunkCoordinate coordinate in displayedCoordinates)
            {
                coordinateScratch.Add(coordinate);
            }

            foreach (ChunkCoordinate coordinate in applyProgressByCoordinate.Keys)
            {
                if (!coordinateScratch.Contains(coordinate))
                {
                    coordinateScratch.Add(coordinate);
                }
            }

            // 部分反映後に画面外へ出たチャンクは消去待ち集合にだけ残る場合があります。
            // キューを破棄する前にこの座標も全消去し、再有効化後へ古いセルを持ち越さないようにします。
            foreach (ChunkCoordinate coordinate in pendingClearCoordinates)
            {
                if (!coordinateScratch.Contains(coordinate))
                {
                    coordinateScratch.Add(coordinate);
                }
            }

            for (int index = 0; index < coordinateScratch.Count; index++)
            {
                ClearWholeChunkImmediately(coordinateScratch[index]);
            }

            // 自動原点変更後の分割消去が残っていても、Component無効化やWorld切替では全旧セルを即時消去します。
            while (staleOriginClearQueue.Count > 0)
            {
                StaleOriginClearProgress staleProgress = staleOriginClearQueue.Dequeue();
                ClearWholeChunkImmediately(
                    staleProgress.Coordinate,
                    staleProgress.DisplayOrigin,
                    false);
            }

            DestroyAllRockWallShadows();
            // 即時消去は方式切替・テレポート・World切替に限ります。空きプールも破棄して設定やWorldを持ち越しません。
            DestroyChunkLocalPresentations();
            ResetPresentationCaches();
        }

        /// <summary>
        /// 論理チャンク座標とサブチャンク番号から、現在の方式に対応する16×16 BoundsIntを作ります。
        /// 比較方式では0〜63のチャンク内セルを使い、原点変更によってセル位置を変更しません。
        /// </summary>
        /// <param name="coordinate">親となる論理チャンク座標です。</param>
        /// <param name="subChunkIndex">行優先の0〜15サブチャンク番号です。</param>
        /// <returns>Z=0平面にある16×16×1セルの範囲です。</returns>
        private BoundsInt CreateSubChunkBounds(ChunkCoordinate coordinate, int subChunkIndex)
        {
            if (chunkLocalTilemapsAreActive)
            {
                return new BoundsInt(
                    (subChunkIndex % WorldGridConstants.SubChunksPerAxis) * WorldGridConstants.SubChunkSize,
                    (subChunkIndex / WorldGridConstants.SubChunksPerAxis) * WorldGridConstants.SubChunkSize,
                    0,
                    WorldGridConstants.SubChunkSize,
                    WorldGridConstants.SubChunkSize,
                    1);
            }

            return CreateSubChunkBounds(
                coordinate,
                subChunkIndex,
                presentedDisplayOrigin);
        }

        /// <summary>
        /// 新しい表示単位を作るときだけ、Scene上の9レイヤーを固定順で取得します。毎フレームの反映・消去では配列を作りません。
        /// </summary>
        /// <returns>表示設定の見本です。セル内容はコピーしません。</returns>
        private Tilemap[] GetTilemapTemplates()
        {
            return new[] { targetTilemap, liquidBaseTilemap, resourceTilemap, resourceBehindRockWallTilemap,
                liquidAnimationTilemap, rockWallTilemap, rockWallResourceTilemap,
                landBoundaryHighlightTilemap, rockWallBoundaryHighlightTilemap };
        }

        /// <summary>
        /// 現在の比較方式に対応する書き込み先を取得します。新方式の初回反映だけ空き表示を借ります。
        /// </summary>
        /// <param name="coordinate">対象の絶対チャンク座標です。</param>
        /// <param name="template">Scene上のレイヤー設定見本です。</param>
        /// <param name="createIfMissing">未割当チャンクへ表示単位を貸し出してよい場合はtrueです。</param>
        /// <returns>旧方式では見本そのもの、新方式ではチャンク専用Tilemapです。</returns>
        private Tilemap GetTilemapForChunk(ChunkCoordinate coordinate, Tilemap template, bool createIfMissing)
        {
            if (!chunkLocalTilemapsAreActive || template == null)
            {
                return template;
            }

            if (!chunkLocalPresentations.TryGetValue(coordinate, out ChunkLocalTilemapPresentation presentation))
            {
                if (!createIfMissing)
                {
                    return null;
                }

                presentation = pooledChunkLocalPresentations.Count > 0
                    ? pooledChunkLocalPresentations.Pop()
                    : new ChunkLocalTilemapPresentation(GetTilemapTemplates());
                presentation.Reposition(CalculateRockWallShadowMinimumCell(coordinate, presentedDisplayOrigin));
                presentation.Root.SetActive(true);
                chunkLocalPresentations.Add(coordinate, presentation);
            }

            return presentation.GetTilemap(template);
        }

        /// <summary>
        /// 全16サブチャンク消去後だけ表示単位を返却し、別チャンクへ古いセルが混ざることを防ぎます。
        /// </summary>
        /// <param name="coordinate">消去を終えた論理チャンク座標です。</param>
        private void ReleaseChunkLocalPresentation(ChunkCoordinate coordinate)
        {
            if (!chunkLocalPresentations.Remove(coordinate, out ChunkLocalTilemapPresentation presentation))
            {
                return;
            }

            presentation.Root.SetActive(false);
            if (pooledChunkLocalPresentations.Count < Mathf.Max(0, maximumPooledChunkPresentations))
            {
                pooledChunkLocalPresentations.Push(presentation);
            }
            else
            {
                presentation.Destroy();
            }
        }

        /// <summary>
        /// 無効化、World切替、方式切替、破棄で、貸し出し中と空きプールの実行時表示をすべて破棄します。
        /// </summary>
        private void DestroyChunkLocalPresentations()
        {
            foreach (ChunkLocalTilemapPresentation presentation in chunkLocalPresentations.Values)
            {
                presentation.Destroy();
            }

            chunkLocalPresentations.Clear();
            while (pooledChunkLocalPresentations.Count > 0)
            {
                pooledChunkLocalPresentations.Pop().Destroy();
            }
        }

        /// <summary>
        /// 論理チャンク座標、サブチャンク番号、書き込み時の表示原点からTilemapセル範囲を作ります。
        /// 自動原点変更後も旧セルだけを正しい相対位置から消去できるよう、原点を引数で受け取ります。
        /// </summary>
        /// <param name="coordinate">親となる論理チャンク座標です。</param>
        /// <param name="subChunkIndex">行優先の0〜15サブチャンク番号です。</param>
        /// <param name="cellDisplayOrigin">Tilemapセル位置の基準とする表示原点です。</param>
        /// <returns>Z=0平面にある16×16×1セルの範囲です。</returns>
        private static BoundsInt CreateSubChunkBounds(
            ChunkCoordinate coordinate,
            int subChunkIndex,
            DisplayOrigin cellDisplayOrigin)
        {
            int subChunkX = subChunkIndex % WorldGridConstants.SubChunksPerAxis;
            int subChunkY = subChunkIndex / WorldGridConstants.SubChunksPerAxis;
            int worldTileX = WorldGrid.ToWorldTileX(
                coordinate.X,
                subChunkX * WorldGridConstants.SubChunkSize);
            int worldTileY = WorldGrid.ToWorldTileY(
                coordinate.Y,
                subChunkY * WorldGridConstants.SubChunkSize);

            RelativeCellCoordinate relativeCell =
                WorldCoordinateConversion.ToRelativeCellCoordinate(
                    new int2(worldTileX, worldTileY),
                    cellDisplayOrigin);
            if (relativeCell.X < int.MinValue
                || relativeCell.X > int.MaxValue
                || relativeCell.Y < int.MinValue
                || relativeCell.Y > int.MaxValue)
            {
                throw new System.InvalidOperationException(
                    $"表示原点から離れ過ぎたチャンクをTilemapへ配置できません: Chunk=({coordinate.X}, {coordinate.Y}), Relative=({relativeCell.X}, {relativeCell.Y})");
            }

            return new BoundsInt(
                (int)relativeCell.X,
                (int)relativeCell.Y,
                0,
                WorldGridConstants.SubChunkSize,
                WorldGridConstants.SubChunkSize,
                1);
        }

        /// <summary>中央セルの8近傍を、現在のECS Bufferとこの反映バッチの外周配列から取得します。</summary>
        /// <param name="localTileX">中心セルの水平ローカル座標です。</param>
        /// <param name="localTileY">中心セルの垂直ローカル座標です。</param>
        /// <param name="tiles">現在のチャンクの生成済み地形Bufferです。</param>
        /// <param name="halo">中心セルを含む反映範囲でFill済みの外周配列です。</param>
        /// <returns>境界表示へ渡す8方向の生成地形です。</returns>
        private static TerrainNeighborTiles GetTerrainNeighborTiles(
            int localTileX, int localTileY, DynamicBuffer<GeneratedTile> tiles,
            NativeArray<GeneratedTile> halo)
        {
            return new TerrainNeighborTiles(
                TerrainNeighborHaloUtility.GetTile(localTileX, localTileY + 1, tiles, halo),
                TerrainNeighborHaloUtility.GetTile(localTileX + 1, localTileY, tiles, halo),
                TerrainNeighborHaloUtility.GetTile(localTileX, localTileY - 1, tiles, halo),
                TerrainNeighborHaloUtility.GetTile(localTileX - 1, localTileY, tiles, halo),
                TerrainNeighborHaloUtility.GetTile(localTileX + 1, localTileY + 1, tiles, halo),
                TerrainNeighborHaloUtility.GetTile(localTileX + 1, localTileY - 1, tiles, halo),
                TerrainNeighborHaloUtility.GetTile(localTileX - 1, localTileY - 1, tiles, halo),
                TerrainNeighborHaloUtility.GetTile(localTileX - 1, localTileY + 1, tiles, halo));
        }

        /// <summary>
        /// 純粋計算で得た基本形と回転数を、Awakeで事前作成した実行時Tileへ変換します。
        /// </summary>
        /// <param name="selection">境界基本形と時計回り回転数です。</param>
        /// <returns>対応するTileです。境界なし、未設定、範囲外ならnullです。</returns>
        private TileBase SelectBoundaryHighlightTile(BoundaryHighlightSelection selection)
        {
            if (!selection.HasHighlight || generatedBoundaryHighlightTiles == null)
            {
                return null;
            }

            int shapeIndex = (int)selection.Shape - 1;
            int rotation = selection.QuarterTurnsClockwise;
            if (shapeIndex < 0
                || shapeIndex >= generatedBoundaryHighlightTiles.GetLength(0)
                || rotation >= generatedBoundaryHighlightTiles.GetLength(1))
            {
                return null;
            }

            return generatedBoundaryHighlightTiles[shapeIndex, rotation];
        }

        /// <summary>
        /// ECS地形から、液面アニメーション専用Tilemapへ重ねるTileを選択します。
        /// </summary>
        /// <param name="tile">ECSを正とする1セル分の生成地形です。</param>
        /// <returns>淡水・海水・マグマのAnimatedTileです。通常地形ならnullです。</returns>
        private TileBase SelectLiquidAnimationTile(GeneratedTile tile)
        {
            switch (TerrainLayerPresentationUtility.ClassifyLiquidAnimation(tile))
            {
                case LiquidAnimationKind.FreshWater:
                    return freshWaterAnimationTile;
                case LiquidAnimationKind.SaltWater:
                    return saltWaterAnimationTile;
                case LiquidAnimationKind.Magma:
                    return magmaAnimationTile;
                default:
                    return null;
            }
        }

        /// <summary>
        /// 液体のゲームルール用GeneratedTileを、8px下げる静止液面Tileへ変換します。
        /// </summary>
        /// <param name="tile">ECSを正とする水またはマグマ1セル分の生成地形です。</param>
        /// <returns>淡水・海水・マグマの基礎Tileです。液体でなければnullです。</returns>
        private TileBase SelectLiquidBaseTile(GeneratedTile tile)
        {
            if (tile.Ground == GroundType.Water)
            {
                // 移行直後の旧Sceneでも淡水を空表示にしないよう、淡水参照が未設定なら海水Tileへフォールバックします。
                return tile.WaterSalinity == WaterSalinity.FreshWater && freshWaterTile != null
                    ? freshWaterTile
                    : saltWaterTile;
            }

            return tile.Ground == GroundType.Magma
                ? magmaTile
                : null;
        }

        /// <summary>
        /// 液体を除くゲームルール用GeneratedTileを、論理バイオームに対応する一体型土地Tileへ変換します。
        /// SoilTypeは資源生成の内部情報として保持しますが、プレイヤー向け土地表示には反映しません。
        /// </summary>
        /// <param name="tile">ECSを正とする1タイル分の土地地形データです。</param>
        /// <returns>岩壁セルを含めて論理バイオームに対応する土地の基準面Tileです。</returns>
        private TileBase SelectLandTile(GeneratedTile tile)
        {
            return SelectBiomeBaseTile(tile.Biome);
        }

        /// <summary>
        /// 論理Biomeを7枚の基準土地Tileへ変換します。
        /// </summary>
        /// <param name="biome">Simulationが決定した論理バイオームです。</param>
        /// <returns>指定Biomeの基準Tileです。未設定値は草原へ安全にフォールバックします。</returns>
        private TileBase SelectBiomeBaseTile(BiomeId biome)
        {
            if (biome == BiomeId.Forest)
            {
                return forestTile;
            }

            if (biome == BiomeId.Desert)
            {
                return desertTile;
            }

            if (biome == BiomeId.Rockland)
            {
                return rocklandTile;
            }

            if (biome == BiomeId.Wetland)
            {
                return wetlandTile;
            }

            if (biome == BiomeId.Snowfield)
            {
                return snowfieldTile;
            }

            if (biome == BiomeId.Volcanic)
            {
                return volcanicTile;
            }

            return grasslandTile;
        }

        /// <summary>現在のBufferと準備済み外周の4方向を調べ、資源を残す露出面と内部を区別します。</summary>
        /// <param name="localX">中心セルの水平ローカル座標です。</param>
        /// <param name="localY">中心セルの垂直ローカル座標です。</param>
        /// <param name="tile">中心セルの現在の生成地形です。</param>
        /// <param name="tiles">現在のチャンクの生成済み地形Bufferです。</param>
        /// <param name="halo">中心セルを含む反映範囲でFill済みの外周配列です。</param>
        /// <returns>非岩壁ならNone、非岩壁に接する岩壁ならExposed、それ以外はInteriorです。</returns>
        private static RockWallAppearance ClassifyRockWallAppearance(
            int localX, int localY, GeneratedTile tile, DynamicBuffer<GeneratedTile> tiles,
            NativeArray<GeneratedTile> halo)
        {
            if (tile.Ground != GroundType.RockWall)
            {
                return RockWallAppearance.None;
            }

            // 影の連続濃度は別処理です。ここでは資源を隠すかどうかだけを従来と同じ上下左右で決めます。
            bool touchesOpenTile =
                TerrainNeighborHaloUtility.GetTile(localX - 1, localY, tiles, halo).Ground != GroundType.RockWall
                || TerrainNeighborHaloUtility.GetTile(localX + 1, localY, tiles, halo).Ground != GroundType.RockWall
                || TerrainNeighborHaloUtility.GetTile(localX, localY - 1, tiles, halo).Ground != GroundType.RockWall
                || TerrainNeighborHaloUtility.GetTile(localX, localY + 1, tiles, halo).Ground != GroundType.RockWall;
            return touchesOpenTile ? RockWallAppearance.Exposed : RockWallAppearance.Interior;
        }

        /// <summary>
        /// 生成済みチャンクと探索余白の岩壁マスクから距離場を作り、頂点アルファが連続する黒い影Meshを更新します。
        /// </summary>
        /// <param name="coordinate">影を重ねる論理チャンク座標です。</param>
        /// <param name="tiles">同じチャンクの4096件の生成済み地形Bufferです。</param>
        /// <param name="generationSettings">チャンク外の探索余白を同じ規則で再生成する設定です。</param>
        private void BuildRockWallShadowMesh(
            ChunkCoordinate coordinate,
            DynamicBuffer<GeneratedTile> tiles,
            in WorldGenerationSettings generationSettings)
        {
            // using宣言は早期returnを含むメソッド終了時に自動で閉じ、影構築全体を漏れなく計測します。
            using ProfilerMarker.AutoScope buildScope =
                RockWallShadowBuildProfilerMarker.Auto();
            using (RockWallShadowMaskProfilerMarker.Auto())
            {
                FillRockWallShadowMask(coordinate, tiles, generationSettings);
            }

            using (RockWallShadowOpacityProfilerMarker.Auto())
            {
                FillRockWallShadowTileOpacityBuffer();
            }

            int vertexSize = WorldGridConstants.LogicalChunkSize + 1;
            int opacitySize = WorldGridConstants.LogicalChunkSize + 2;
            Vector3Int relativeMinimum = CalculateRockWallShadowMinimumCell(
                coordinate,
                presentedDisplayOrigin);
            Vector3 meshOrigin = targetTilemap.CellToLocal(relativeMinimum);
            byte maximumAlpha = 0;

            using (RockWallShadowGeometryProfilerMarker.Auto())
            {
                // 4つのタイル中心を共有頂点へ平均し、MeshRendererの三角形補間でタイル内部の段差を消します。
                for (int vertexY = 0; vertexY < vertexSize; vertexY++)
                {
                    for (int vertexX = 0; vertexX < vertexSize; vertexX++)
                    {
                        int vertexIndex = (vertexY * vertexSize) + vertexX;
                        int bottomLeftOpacityIndex = (vertexY * opacitySize) + vertexX;
                        float vertexOpacity = RockWallAppearanceUtility.CalculateVertexOpacity(
                            rockWallShadowTileOpacityBuffer[bottomLeftOpacityIndex],
                            rockWallShadowTileOpacityBuffer[bottomLeftOpacityIndex + 1],
                            rockWallShadowTileOpacityBuffer[bottomLeftOpacityIndex + opacitySize],
                            rockWallShadowTileOpacityBuffer[bottomLeftOpacityIndex + opacitySize + 1]);
                        byte alpha = (byte)Mathf.Clamp(
                            Mathf.RoundToInt(vertexOpacity * byte.MaxValue),
                            0,
                            byte.MaxValue);
                        if (alpha > maximumAlpha)
                        {
                            maximumAlpha = alpha;
                        }

                        Vector3 cellCorner = targetTilemap.CellToLocal(
                            new Vector3Int(
                                relativeMinimum.x + vertexX,
                                relativeMinimum.y + vertexY,
                                0));
                        rockWallShadowVertexBuffer[vertexIndex] = cellCorner - meshOrigin;
                        rockWallShadowColorBuffer[vertexIndex] = new Color32(0, 0, 0, alpha);
                    }
                }
            }

            if (maximumAlpha == 0)
            {
                // 小さな岩壁群に内部がない場合は透明Meshを保持せず、描画バッチと管理対象を増やしません。
                DestroyRockWallShadow(coordinate);
                return;
            }

            using (RockWallShadowUploadProfilerMarker.Auto())
            {
                RockWallShadowChunkPresentation presentation =
                    GetOrCreateRockWallShadowPresentation(coordinate);
                if (presentation == null)
                {
                    return;
                }

                // 岩壁上面Tilemapと同じ12px上へ移し、基準面に影だけ残るずれを防ぎます。
                presentation.GameObject.transform.localPosition = meshOrigin
                    + (Vector3.up * TerrainLayerPresentationUtility.RockWallElevationInCells);
                presentation.Mesh.Clear();
                presentation.Mesh.vertices = rockWallShadowVertexBuffer;
                presentation.Mesh.colors32 = rockWallShadowColorBuffer;
                presentation.Mesh.triangles = rockWallShadowTriangleBuffer;
                presentation.Mesh.RecalculateBounds();
            }
        }

        /// <summary>
        /// 自動Floating Origin前の岩壁影を、新しい表示原点からの相対位置へ移して再利用候補として記録します。
        /// </summary>
        /// <param name="currentOrigin">Cameraが変更した新しい表示原点です。</param>
        private void PrepareRockWallShadowsForAutomaticRebase(DisplayOrigin currentOrigin)
        {
            reusableRockWallShadowCoordinates.Clear();
            using (RockWallShadowRepositionProfilerMarker.Auto())
            {
                foreach (KeyValuePair<ChunkCoordinate, RockWallShadowChunkPresentation> pair in
                         rockWallShadowByCoordinate)
                {
                    // Mesh頂点はチャンク左下を原点とするため、GameObject位置だけを変えれば形状と色をそのまま使えます。
                    pair.Value.GameObject.transform.localPosition =
                        targetTilemap.CellToLocal(
                            CalculateRockWallShadowMinimumCell(pair.Key, currentOrigin))
                        + (Vector3.up * TerrainLayerPresentationUtility.RockWallElevationInCells);
                    reusableRockWallShadowCoordinates.Add(pair.Key);
                }
            }
        }

        /// <summary>
        /// 新原点側で同じ論理チャンクが完成したとき、移動済みの岩壁影Meshを再構築せず採用します。
        /// </summary>
        /// <param name="coordinate">新しいTilemap反映が完了した論理チャンク座標です。</param>
        /// <returns>再利用可能な既存影を採用した場合はtrue、新規構築が必要な場合はfalseです。</returns>
        private bool TryReuseRockWallShadowAfterAutomaticRebase(ChunkCoordinate coordinate)
        {
            if (!reusableRockWallShadowCoordinates.Remove(coordinate))
            {
                return false;
            }

            // 座標集合と辞書は同時に更新しますが、将来の破棄経路追加で不整合が起きても影を省略しないよう辞書も確認します。
            return rockWallShadowByCoordinate.ContainsKey(coordinate);
        }

        /// <summary>
        /// 自動原点変更後の新しい可視範囲で採用されなかった旧岩壁影を破棄します。
        /// </summary>
        private void DestroyUnusedRebasedRockWallShadows()
        {
            coordinateScratch.Clear();
            foreach (ChunkCoordinate coordinate in reusableRockWallShadowCoordinates)
            {
                // HashSetを列挙中に変更しないよう、一度既存の再利用Listへ座標だけを退避します。
                coordinateScratch.Add(coordinate);
            }

            for (int index = 0; index < coordinateScratch.Count; index++)
            {
                DestroyRockWallShadow(coordinateScratch[index]);
            }

            reusableRockWallShadowCoordinates.Clear();
            coordinateScratch.Clear();
        }

        /// <summary>
        /// 論理チャンク左下を表示原点基準のTilemapセルへ変換し、影Meshの配置セルを返します。
        /// </summary>
        /// <param name="coordinate">影を重ねる絶対論理チャンク座標です。</param>
        /// <param name="displayOrigin">相対表示座標の基準とする表示原点です。</param>
        /// <returns>対象チャンク左下の相対Tilemapセルです。</returns>
        private static Vector3Int CalculateRockWallShadowMinimumCell(
            ChunkCoordinate coordinate,
            DisplayOrigin displayOrigin)
        {
            int worldMinimumX = WorldGrid.ToWorldTileX(coordinate.X, 0);
            int worldMinimumY = WorldGrid.ToWorldTileY(coordinate.Y, 0);

            // Simulationの絶対チャンク座標を表示原点からの相対セルへ戻し、Tilemapと同じ位置へ影を重ねます。
            // 変換結果は64bitになり得るため、UnityのVector3Intへ狭める前に範囲を検証します。
            RelativeCellCoordinate relativeMinimumCoordinate =
                WorldCoordinateConversion.ToRelativeCellCoordinate(
                    new int2(worldMinimumX, worldMinimumY),
                    displayOrigin);
            if (relativeMinimumCoordinate.X < int.MinValue
                || relativeMinimumCoordinate.X > int.MaxValue
                || relativeMinimumCoordinate.Y < int.MinValue
                || relativeMinimumCoordinate.Y > int.MaxValue)
            {
                throw new System.InvalidOperationException(
                    $"表示原点から離れ過ぎた岩壁影を配置できません: Chunk=({coordinate.X}, {coordinate.Y}), Relative=({relativeMinimumCoordinate.X}, {relativeMinimumCoordinate.Y})");
            }

            return new Vector3Int(
                (int)relativeMinimumCoordinate.X,
                (int)relativeMinimumCoordinate.Y,
                0);
        }

        /// <summary>
        /// 影の探索半径を含むチャンク周辺を同期Burst Jobで判定し、余白ノイズ計算のC#実行負荷を減らします。
        /// </summary>
        /// <param name="coordinate">中心となる論理チャンク座標です。</param>
        /// <param name="tiles">中心チャンクの生成済み地形Bufferです。</param>
        /// <param name="generationSettings">余白部分を決定論的に再生成する設定です。</param>
        private void FillRockWallShadowMask(
            ChunkCoordinate coordinate,
            DynamicBuffer<GeneratedTile> tiles,
            in WorldGenerationSettings generationSettings)
        {
            // AsNativeArrayはBufferを複製しない借用です。同期Run終了まで構造変更せず、メソッド外へ保持しません。
            RockWallAppearanceUtility.FillShadowMask(generationSettings, coordinate,
                tiles.AsNativeArray(), rockWallShadowMaskBuffer);
        }

        /// <summary>
        /// -1〜64の各タイル中心について最寄り空地までの円形距離を求め、SmoothStep済み不透明度へ変換します。
        /// </summary>
        private void FillRockWallShadowTileOpacityBuffer()
        {
            int searchRadius = RockWallAppearanceUtility.ShadowSearchRadiusInTiles;
            int maximumDistanceSquared = searchRadius * searchRadius;
            int maskPadding = searchRadius + 1;
            int maskSize = WorldGridConstants.LogicalChunkSize + (maskPadding * 2);
            int opacitySize = WorldGridConstants.LogicalChunkSize + 2;

            for (int opacityY = 0; opacityY < opacitySize; opacityY++)
            {
                int maskCenterY = opacityY - 1 + maskPadding;
                for (int opacityX = 0; opacityX < opacitySize; opacityX++)
                {
                    int opacityIndex = (opacityY * opacitySize) + opacityX;
                    int maskCenterX = opacityX - 1 + maskPadding;
                    if (!rockWallShadowMaskBuffer[(maskCenterY * maskSize) + maskCenterX])
                    {
                        rockWallShadowTileOpacityBuffer[opacityIndex] = 0f;
                        continue;
                    }

                    int nearestOpenDistanceSquared = int.MaxValue;
                    // 平方根は最終候補1件だけに使い、探索中は距離の二乗で比較してチャンク構築負荷を抑えます。
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

                            int neighborMaskIndex =
                                ((maskCenterY + offsetY) * maskSize)
                                + maskCenterX
                                + offsetX;
                            if (!rockWallShadowMaskBuffer[neighborMaskIndex])
                            {
                                nearestOpenDistanceSquared = distanceSquared;
                            }
                        }
                    }

                    rockWallShadowTileOpacityBuffer[opacityIndex] =
                        RockWallAppearanceUtility
                            .CalculateShadowOpacityFromNearestOpenDistanceSquared(
                                nearestOpenDistanceSquared);
                }
            }
        }

        /// <summary>
        /// 指定チャンク用のMesh、GameObject、Rendererを必要なときだけ作成し、既存なら再利用します。
        /// </summary>
        /// <param name="coordinate">Presentationを所有する論理チャンク座標です。</param>
        /// <returns>作成または取得したPresentationです。Shaderが見つからない場合だけnullです。</returns>
        private RockWallShadowChunkPresentation GetOrCreateRockWallShadowPresentation(
            ChunkCoordinate coordinate)
        {
            if (rockWallShadowByCoordinate.TryGetValue(
                    coordinate,
                    out RockWallShadowChunkPresentation existingPresentation))
            {
                return existingPresentation;
            }

            Material shadowMaterial = EnsureRockWallShadowMaterial();
            if (shadowMaterial == null)
            {
                return null;
            }

            GameObject shadowObject = new GameObject(
                $"Rock Wall Shadow ({coordinate.X}, {coordinate.Y})",
                typeof(MeshFilter),
                typeof(MeshRenderer));
            shadowObject.transform.SetParent(targetTilemap.transform, false);

            Mesh shadowMesh = new Mesh
            {
                name = $"Rock Wall Shadow Mesh ({coordinate.X}, {coordinate.Y})"
            };
            MeshFilter meshFilter = shadowObject.GetComponent<MeshFilter>();
            meshFilter.sharedMesh = shadowMesh;

            MeshRenderer shadowRenderer = shadowObject.GetComponent<MeshRenderer>();
            shadowRenderer.sharedMaterial = shadowMaterial;
            shadowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            shadowRenderer.receiveShadows = false;
            shadowRenderer.lightProbeUsage = LightProbeUsage.Off;
            shadowRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            shadowRenderer.allowOcclusionWhenDynamic = false;

            // 持ち上げた岩壁上面より手前、資源Tilemapより奥へ置き、内部の暗さがHUDや資源表示全体を覆わないようにします。
            TilemapRenderer terrainRenderer = targetTilemap.GetComponent<TilemapRenderer>();
            if (terrainRenderer != null)
            {
                shadowRenderer.sortingLayerID = terrainRenderer.sortingLayerID;
                // 岩壁上面より手前、資源より奥に置き、持ち上げた岩盤内部だけを暗くします。
                shadowRenderer.sortingOrder = terrainRenderer.sortingOrder + 5;
            }

            RockWallShadowChunkPresentation presentation =
                new RockWallShadowChunkPresentation(
                    shadowObject,
                    shadowMesh);
            rockWallShadowByCoordinate.Add(coordinate, presentation);
            return presentation;
        }

        /// <summary>
        /// 頂点色を透明合成できるMaterialを1つだけ作り、すべてのチャンク影で共有します。
        /// </summary>
        /// <returns>使用可能なMaterialです。対応Shaderが見つからない場合はnullです。</returns>
        private Material EnsureRockWallShadowMaterial()
        {
            if (generatedRockWallShadowMaterial != null)
            {
                return generatedRockWallShadowMaterial;
            }

            Shader shadowShader = Shader.Find(PrimaryRockWallShadowShaderName);
            if (shadowShader == null)
            {
                shadowShader = Shader.Find(FallbackRockWallShadowShaderName);
            }

            if (shadowShader == null)
            {
                Debug.LogError(
                    "岩壁のグラデーション影に使用できるSprite Shaderが見つかりません。",
                    this);
                return null;
            }

            generatedRockWallShadowMaterial = new Material(shadowShader)
            {
                name = "Generated Rock Wall Gradient Shadow Material",
                hideFlags = HideFlags.DontSave
            };
            generatedRockWallShadowMaterial.mainTexture = Texture2D.whiteTexture;
            if (generatedRockWallShadowMaterial.HasProperty("_Color"))
            {
                generatedRockWallShadowMaterial.SetColor("_Color", Color.white);
            }

            if (generatedRockWallShadowMaterial.HasProperty("_BaseColor"))
            {
                generatedRockWallShadowMaterial.SetColor("_BaseColor", Color.white);
            }

            return generatedRockWallShadowMaterial;
        }

        /// <summary>
        /// 指定チャンクの影MeshとGameObjectを破棄し、Presentation辞書から取り除きます。
        /// </summary>
        /// <param name="coordinate">アンロードまたは再構築する論理チャンク座標です。</param>
        private void DestroyRockWallShadow(ChunkCoordinate coordinate)
        {
            reusableRockWallShadowCoordinates.Remove(coordinate);
            if (!rockWallShadowByCoordinate.Remove(
                    coordinate,
                    out RockWallShadowChunkPresentation presentation))
            {
                return;
            }

            presentation.Destroy();
        }

        /// <summary>
        /// Component無効化・破棄時に、所有する全チャンク影を解放してSceneへ実行時Objectを残しません。
        /// </summary>
        private void DestroyAllRockWallShadows()
        {
            foreach (RockWallShadowChunkPresentation presentation in
                     rockWallShadowByCoordinate.Values)
            {
                presentation.Destroy();
            }

            rockWallShadowByCoordinate.Clear();
            reusableRockWallShadowCoordinates.Clear();
        }

        /// <summary>
        /// PlayModeではフレーム末、EditModeテストでは即時に、所有するUnityEngine.Objectを安全に破棄します。
        /// </summary>
        /// <param name="ownedObject">このPresenterだけが作成・所有するObjectです。</param>
        private static void DestroyUnityObject(Object ownedObject)
        {
            if (ownedObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(ownedObject);
            }
            else
            {
                DestroyImmediate(ownedObject);
            }
        }

        /// <summary>
        /// ゲーム状態用GeneratedResourceTileを、透明背景の表示専用Tileへ変換します。
        /// </summary>
        /// <param name="resource">ECSを正とする1タイル分の基礎資源データです。</param>
        /// <returns>資源種類に対応するTile、または配置なしの場合はnullです。</returns>
        private TileBase SelectResourceTile(GeneratedResourceTile resource)
        {
            switch (resource.Type)
            {
                case GeneratedResourceType.Tree:
                    return treeResourceTile;
                case GeneratedResourceType.Stone:
                    return stoneResourceTile;
                case GeneratedResourceType.Sand:
                    return sandResourceTile;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Authoring設定から1フレームのサブチャンク処理数を取得し、不正値は最低1へ補正します。
        /// </summary>
        /// <returns>反映と消去で共有する正のフレーム予算です。</returns>
        private int GetSubChunkBudget()
        {
            if (streamingSettingsQuery.CalculateEntityCount() != 1)
            {
                return 1;
            }

            ChunkStreamingSettings settings =
                streamingSettingsQuery.GetSingleton<ChunkStreamingSettings>();
            return Mathf.Max(1, settings.MaximumSubChunkApplicationsPerFrame);
        }

        /// <summary>
        /// 正規化前のObserverにも対応し、指定チャンクが可視矩形と指定余白内に含まれるか判定します。
        /// </summary>
        /// <param name="observer">Presentation Bridgeが送った可視範囲です。</param>
        /// <param name="coordinate">検査する論理チャンク座標です。</param>
        /// <param name="margin">四辺の外側へ追加する非負のチャンク数です。表示準備では最大1を渡します。</param>
        /// <returns>4辺を含む矩形内ならtrueです。</returns>
        private static bool Contains(ChunkStreamingObserver observer, ChunkCoordinate coordinate, int margin)
        {
            // int端のObserverでも余白の加減算が反対端へ周回しないよう、計算だけ64bitへ広げます。
            // 実際のEntity生成はStreaming側で有効なタイル領域へ制限され、この判定は要求を追加しません。
            long minimumX = (long)Mathf.Min(observer.MinimumVisibleChunkX, observer.MaximumVisibleChunkX) - margin;
            long maximumX = (long)Mathf.Max(observer.MinimumVisibleChunkX, observer.MaximumVisibleChunkX) + margin;
            long minimumY = (long)Mathf.Min(observer.MinimumVisibleChunkY, observer.MaximumVisibleChunkY) - margin;
            long maximumY = (long)Mathf.Max(observer.MinimumVisibleChunkY, observer.MaximumVisibleChunkY) + margin;
            return coordinate.Layer == observer.Layer
                && coordinate.X >= minimumX
                && coordinate.X <= maximumX
                && coordinate.Y >= minimumY
                && coordinate.Y <= maximumY;
        }

        /// <summary>
        /// World切り替えまたはComponent破棄時に、明示作成したQueryを安全に解放します。
        /// </summary>
        private void DisposeQueries()
        {
            if (!queriesAreCreated)
            {
                return;
            }

            // Play Mode終了ではWorldが先に破棄される場合があります。その場合QueryもWorldと一緒に解放済みです。
            if (presentationWorld != null && presentationWorld.IsCreated)
            {
                chunkQuery.Dispose();
                observerQuery.Dispose();
                streamingSettingsQuery.Dispose();
                generationSettingsQuery.Dispose();
            }

            queriesAreCreated = false;
            presentationWorld = null;
        }

        /// <summary>
        /// 表示・反映・消去の管理キャッシュを空にし、再有効化時にECSから再構築できる状態へ戻します。
        /// </summary>
        private void ResetPresentationCaches()
        {
            applyProgressByCoordinate.Clear();
            applyQueue.Clear();
            displayedCoordinates.Clear();
            pendingClearCoordinates.Clear();
            seenCoordinates.Clear();
            clearQueue.Clear();
            staleOriginClearQueue.Clear();
            coordinateScratch.Clear();
            reusableRockWallShadowCoordinates.Clear();
        }

        /// <summary>
        /// Camera側で検出した表示原点変更の種類です。
        /// 変更理由により、旧セルを画面外で分割消去できるか、即時消去が必要かを区別します。
        /// </summary>
        private enum DisplayOriginChangeKind : byte
        {
            /// <summary>表示原点は変化していません。</summary>
            None = 0,

            /// <summary>通常カメラ移動が設定閾値へ達した自動変更です。</summary>
            Automatic = 1,

            /// <summary>初期配置、ロード、テストなどから要求された明示変更です。</summary>
            Explicit = 2
        }

        /// <summary>
        /// 1つの論理チャンクへ重ねる影GameObjectと、明示的な破棄が必要な実行時Meshをまとめて所有します。
        /// ChunkTilemapPresenterだけが生成・破棄し、ECSの地形状態には使用しません。
        /// </summary>
        private sealed class RockWallShadowChunkPresentation
        {
            /// <summary>MeshFilterとMeshRendererを持ち、Ground Tilemapの子として表示されるGameObjectです。</summary>
            public readonly GameObject GameObject;

            /// <summary>チャンクの65×65頂点と連続アルファを保持する、実行時生成Meshです。</summary>
            public readonly Mesh Mesh;

            /// <summary>
            /// 新しいチャンク影の所有Objectを登録します。
            /// </summary>
            /// <param name="gameObject">Scene階層へ追加済みの影GameObjectです。</param>
            /// <param name="mesh">GameObjectのMeshFilterへ設定済みの実行時Meshです。</param>
            public RockWallShadowChunkPresentation(GameObject gameObject, Mesh mesh)
            {
                GameObject = gameObject;
                Mesh = mesh;
            }

            /// <summary>
            /// MeshとGameObjectを所有者として破棄し、アンロード後に描画データを残しません。
            /// </summary>
            public void Destroy()
            {
                DestroyUnityObject(Mesh);
                DestroyUnityObject(GameObject);
            }
        }

        /// <summary>
        /// 1チャンクのTilemap反映途中位置を保持するPresentation専用の参照型です。
        /// Dictionaryから取得した後に値を書き戻さず進捗を更新するため、短寿命の生成ごとに1回だけ確保します。
        /// </summary>
        private sealed class ChunkApplyProgress
        {
            /// <summary>GeneratedTile Bufferを読み取る現在のチャンクEntityです。</summary>
            public Entity Entity;

            /// <summary>次に反映する行優先のサブチャンク番号です。範囲は0〜16です。</summary>
            public int NextSubChunkIndex;

            /// <summary>
            /// 新しい反映作業を先頭サブチャンクから開始します。
            /// </summary>
            /// <param name="entity">生成済みタイルを所有するEntityです。</param>
            public ChunkApplyProgress(Entity entity)
            {
                Entity = entity;
                NextSubChunkIndex = 0;
            }
        }

        /// <summary>
        /// 1チャンクのTilemap消去途中位置を保持するPresentation専用の参照型です。
        /// Queueの先頭を取り出したまま次番号を更新できるため、毎フレームの再登録を避けます。
        /// </summary>
        private sealed class ChunkClearProgress
        {
            /// <summary>消去対象の論理チャンク座標です。</summary>
            public readonly ChunkCoordinate Coordinate;

            /// <summary>次に消去する行優先のサブチャンク番号です。範囲は0〜16です。</summary>
            public int NextSubChunkIndex;

            /// <summary>
            /// 新しい消去作業を先頭サブチャンクから開始します。
            /// </summary>
            /// <param name="coordinate">消去対象の論理チャンク座標です。</param>
            public ChunkClearProgress(ChunkCoordinate coordinate)
            {
                Coordinate = coordinate;
                NextSubChunkIndex = 0;
            }
        }

        /// <summary>
        /// 自動Floating Origin前の相対セルと消去途中位置を保持するPresentation専用の参照型です。
        /// 論理チャンクが新原点側へ再表示されても、旧原点を使って古い画面外セルだけを消去します。
        /// </summary>
        private sealed class StaleOriginClearProgress
        {
            /// <summary>消去対象の論理チャンク座標です。</summary>
            public readonly ChunkCoordinate Coordinate;

            /// <summary>対象セルを書き込んだ時点の表示原点です。</summary>
            public readonly DisplayOrigin DisplayOrigin;

            /// <summary>次に消去する行優先のサブチャンク番号です。範囲は0〜16です。</summary>
            public int NextSubChunkIndex;

            /// <summary>
            /// 旧原点に残った1チャンクの消去を、先頭サブチャンクから開始します。
            /// </summary>
            /// <param name="coordinate">消去対象の論理チャンク座標です。</param>
            /// <param name="displayOrigin">対象セルを書き込んだ時点の表示原点です。</param>
            public StaleOriginClearProgress(
                ChunkCoordinate coordinate,
                DisplayOrigin displayOrigin)
            {
                Coordinate = coordinate;
                DisplayOrigin = displayOrigin;
                NextSubChunkIndex = 0;
            }
        }
    }
}
