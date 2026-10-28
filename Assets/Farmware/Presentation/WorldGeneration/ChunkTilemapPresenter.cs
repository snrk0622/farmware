// このファイルは、ECSで生成済みの地形・資源チャンクデータを、上面・断面・液体・水際・Wall影のTilemapと反射Meshへ16×16サブチャンク単位で反映・消去します。
// チャンク別ローカルTilemapを自動原点変更で再利用し、製品カタログの上面・断面・液体を描画します。
// Tilemapは表示専用キャッシュであり、地形はGeneratedTerrainCell、資源はGeneratedResourceTileを正として参照します。
// 分類は中心と周囲8チャンクの完成自然地形へ永続差分を重ねた有効地形を直接参照します。

using System.Collections.Generic;
using Farmware.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
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

        // 分類済み配列を現在有効な全TilemapへSetTilesBlockする時間を確認する計測区間です。
        private static readonly ProfilerMarker SubChunkTilemapWriteProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.SubChunkTilemapWrite");

        // 旧表示のnull配列を現在有効な全TilemapへSetTilesBlockする時間を確認する計測区間です。
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

        // Dirtyサブチャンクの露出Wall根元を横連続区間へ圧縮し、動的影Casterへ反映する時間です。
        private static readonly ProfilerMarker TerrainWallDynamicShadowUpdateProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.TerrainWallDynamicShadowUpdate");

        // 自動原点変更時に、タイル内容を変更せず既存表示ルートだけを移動した時間です。
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

        // 41材質の16×16 Ground上面を描画する設定見本です。実セルはチャンク別Tilemapへ書き込みます。
        [Tooltip("41材質の16×16 Ground上面を描画する専用Tilemapです。行単位の重なりを正しく処理するためIndividual Modeで使用してください。")]
        [SerializeField] private Tilemap targetTilemap;

        // 水・マグマの静止Surfaceを描画します。製品Sceneは論理セル位置を維持し、岸側だけShaderで上端3pxを隠します。
        [Tooltip("淡水・海水・マグマの基礎Surface専用Tilemapです。製品SceneではTransformを移動せず、TerrainLiquidSurface Materialで岸側上端3pxだけを隠します。")]
        [SerializeField] private Tilemap liquidBaseTilemap;

        // 岩壁に隠れない通常地面の資源を、岩壁より前へ描画する専用Tilemapです。採取状態の正には使用しません。
        [Tooltip("通常地面の木・石・砂のうち、直前方に岩壁がない資源を描画する専用Tilemapです。岩壁より前のSorting Orderへ設定してください。")]
        [SerializeField] private Tilemap resourceTilemap;

        // 直前方が岩壁の通常資源だけを岩壁より先に描き、壁Spriteと重なる下部を隠す専用Tilemapです。
        [Tooltip("直前方のセルが岩壁である通常資源を描画する専用Tilemapです。岩壁より後ろのSorting Orderへ設定してください。")]
        [SerializeField] private Tilemap resourceBehindRockWallTilemap;

        // 製品マグマセルだけへ、基礎液面と同じ岸側クリップ印で連続する網状模様を描きます。
        [Tooltip("マグマの手続き型網状模様を描くTilemapです。液面と同じ論理座標に置き、専用Materialで網目寸法と発光を調整します。")]
        [SerializeField] private Tilemap magmaSurfacePatternTilemap;

        // 直下床がある水域の基底色です。45pxの光学減衰は前面の断面側で評価します。
        [Tooltip("直下床がある淡水・海水の基底色Tilemapです。TerrainLiquidDepthWithFloor Materialを設定し、断面・床より後ろに置きます。")]
        [SerializeField] private Tilemap liquidDepthWithFloorTilemap;

        // 直下床がない水域の基底色です。93pxの光学減衰は前面の断面側で評価します。
        [Tooltip("直下床がない淡水・海水の基底色Tilemapです。TerrainLiquidDepthWithoutFloor Materialを設定し、断面より後ろに置きます。")]
        [SerializeField] private Tilemap liquidDepthWithoutFloorTilemap;

        // Surfaceから見える直下Underwater、またはUnderwaterから見える直下DeepSeaのGround上面だけを描きます。
        [Tooltip("水域の4段目以降へ直下1層のGround上面を簡易投影するTilemapです。選択やSimulation処理には使用しません。")]
        [SerializeField] private Tilemap underwaterFloorPreviewTilemap;

        // 水では水深による断面、マグマでは液面より上のGroundの縁3pxを描く共通レイヤーです。
        [Tooltip("水中Ground断面とマグマ奥岸の縁3pxを表示するTilemapです。TerrainSubmergedGroundFace Materialがセルの分類印で描き分け、水の基底色より前に置きます。")]
        [SerializeField] private Tilemap submergedGroundFaceTilemap;

        // 淡水・海水の短い画素模様を、断面より前・反射より後ろに独立して重ねます。
        [Tooltip("短い水面模様専用Tilemapです。Water Surface Pattern Materialを設定します。水際・水深の調整値には影響しません。")]
        [SerializeField] private Tilemap waterSurfacePatternTilemap;

        // 反射MeshのMaterial・Sorting設定の見本です。このTilemap自体へ反射Tileは書き込みません。
        [Tooltip("接地点から投影する反射Meshの描画設定見本です。TerrainWaterReflection Materialを設定し、水面模様より前、水際線より後ろへ置きます。")]
        [SerializeField] private Tilemap waterReflectionTilemap;

        /// <summary>反射元Spriteを1ページへまとめる製品Atlasです。Scene接続時に設定し、実行時は借用します。</summary>
        [SerializeField, Tooltip("反射元Faceを収録した、回転なし・FullRectの製品Atlasです。水中断面やSimulationは変更しません。")]
        private UnityEngine.U2D.SpriteAtlas waterReflectionAtlas;

        /// <summary>表示中チャンクの反射Mesh所有者です。Dirtyサブチャンクだけを再構築します。</summary>
        private readonly Dictionary<ChunkCoordinate, TerrainWaterReflectionMesh> reflectionMeshes =
            new Dictionary<ChunkCoordinate, TerrainWaterReflectionMesh>();

        /// <summary>製品Atlasから取り出したSpriteコピーです。Presenterが最後に破棄します。</summary>
        private readonly Dictionary<Sprite, Sprite> reflectionAtlasSprites = new Dictionary<Sprite, Sprite>();

        /// <summary>原点と時計だけを書き換える共有反射Materialの実行時コピーです。</summary>
        private Material reflectionRuntimeMaterial;

        /// <summary>Project内の反射Materialです。実行時の変更は書き戻しません。</summary>
        private Material reflectionSourceMaterial;

        /// <summary>反射へ最後に適用した整数原点です。変更時だけRoot群を移動します。</summary>
        private int2 reflectionMaterialOrigin;

        /// <summary>絶対原点0と未初期化を区別します。</summary>
        private bool hasReflectionMaterialOrigin;

        // 奥・左・右・左右内角・裏側終端の7bit形状を持つ静止Tileを水面の最前面へ描きます。
        [Tooltip("淡水・海水・マグマの奥・左・右接触線を描く水際アニメーションTilemapです。画面手前辺は描きません。")]
        [SerializeField] private Tilemap liquidEdgeWaveTilemap;

        // 論理Wall根元の2タイル奥へ、製品カタログのGroundTopを描画します。
        [Tooltip("製品Wall上面のGroundTopを描画するTilemapです。Scene側で論理根元から画面奥へ2タイル持ち上げます。")]
        [SerializeField] private Tilemap rockWallTilemap;

        // 製品カタログ経路で、乾燥穴へ投影したGround崖面3段だけを描画します。
        [Tooltip("製品材質のGround崖面3段を描画するTilemapです。Farmware/Terrain/Dry Cliff Face Materialを設定し、48px連続減衰を適用してください。")]
        [SerializeField] private Tilemap groundFaceTilemap;

        // 製品カタログ経路で、論理Wall根元から上へ積む前面2段だけを描画します。
        [Tooltip("製品材質のWall前面2段を描画するTilemapです。Scene側の+2Y補正で最下段を論理根元へ合わせます。")]
        [SerializeField] private Tilemap wallFaceTilemap;

        // Wall上面と前面の結合シルエットを左1px・手前3pxへ複製し、光源に依存しない接地点を描きます。
        [Tooltip("製品Wallの固定接地影を左1px・手前3pxへ表示するTilemapです。TerrainWallContactShadow Materialを設定し、Wall本体より後ろへ描画してください。")]
        [SerializeField] private Tilemap wallContactShadowLeftTilemap;

        // Wall上面と前面の結合シルエットを右1px・手前3pxへ複製し、左右対称の接地点を描きます。
        [Tooltip("製品Wallの固定接地影を右1px・手前3pxへ表示するTilemapです。TerrainWallContactShadow Materialを設定し、Wall本体より後ろへ描画してください。")]
        [SerializeField] private Tilemap wallContactShadowRightTilemap;

        // Wall前面1段目へ32px連続折れ目影の0〜15pxを重ねます。
        [Tooltip("製品Wall前面1段目の32px折れ目影Tilemapです。TerrainWallFoldShadowTop Materialを設定してください。")]
        [SerializeField] private Tilemap wallFoldShadowTopTilemap;

        // Wall前面2段目へ32px連続折れ目影の16〜31pxを重ねます。
        [Tooltip("製品Wall前面2段目の32px折れ目影Tilemapです。TerrainWallFoldShadowBottom Materialを設定してください。")]
        [SerializeField] private Tilemap wallFoldShadowBottomTilemap;

        // 乾燥穴の全セルへ黒い下地を置き、3段目の減衰後と4段目以降を完全な暗黒にします。
        [Tooltip("製品材質の乾燥穴へ完全な黒い下地を描画するTilemapです。Ground Faceより後ろのSorting Orderへ設定してください。")]
        [SerializeField] private Tilemap dryCliffDeepBlackTilemap;

        // 岩壁上に生成された資源だけを上面と同じ高さへ持ち上げ、側面へ埋まる表示を防ぎます。
        [Tooltip("露出したWall上の資源を、製品Wall上面と同じく論理根元から2タイル奥へ表示する専用Tilemapです。")]
        [SerializeField] private Tilemap rockWallResourceTilemap;

        // Cameraと全Tilemapで共有する表示原点を提供します。Tilemapセルへ絶対座標を直接書かないために使用します。
        [Tooltip("CameraとTilemapで共有する表示原点の所有者です。未指定時は絶対原点(0, 0)を使用します。")]
        [SerializeField] private CameraPanController cameraPanController;

        // 消去済みの表示単位だけを保持する上限です。可視チャンク数の上限やSimulationの保持範囲ではありません。
        [Min(0)]
        [Tooltip("消去済みチャンク表示を再利用する空きプールの上限（チャンク数）です。小さくするとメモリを減らせますが、再作成が増えます。")]
        [SerializeField] private int maximumPooledChunkPresentations = 8;

        // 41材質を完全登録した必須カタログです。未設定または不完全なら描画を開始しません。
        [Tooltip("41材質を完全登録した製品表示カタログです。未設定・不足・不正値を拒否し、別素材へ読み替えません。")]
        [SerializeField] private TerrainMaterialPresentationCatalog materialPresentationCatalog;

        // 3液体の基礎Tile、水際色、深度色を現行カタログへ完全登録した場合だけ製品Liquid経路を有効にします。
        [Tooltip("淡水・海水・マグマを各1件登録した製品Liquid表示カタログです。未設定・不足・不正値を拒否します。")]
        [SerializeField] private TerrainLiquidPresentationCatalog liquidPresentationCatalog;

        // 可視・部分構築・消去途中の表示単位を論理座標で保持します。Simulation状態は所有しません。
        private readonly Dictionary<ChunkCoordinate, ChunkLocalTilemapPresentation> chunkLocalPresentations =
            new Dictionary<ChunkCoordinate, ChunkLocalTilemapPresentation>();

        // 全16サブチャンクの消去が完了した表示単位だけを再貸し出しする、上限付き空きプールです。
        private readonly Stack<ChunkLocalTilemapPresentation> pooledChunkLocalPresentations =
            new Stack<ChunkLocalTilemapPresentation>();

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

        /// <summary>有効地形から抽出した岸へ照明を配置する表示専用Componentです。Scene接続時に設定します。</summary>
        [Tooltip("マグマ岸の有効地形を受け取る照明Presenterです。地形のDirty更新と同じ寿命で岸候補を更新します。")]
        [SerializeField] private TerrainMagmaShoreLighting magmaShoreLighting;

        /// <summary>製品Scene接続時に岸の照明の受け取り先を設定します。</summary>
        /// <param name="lighting">同じGround GameObjectに配置する照明Presenterです。</param>
        public void ConfigureMagmaShoreLighting(TerrainMagmaShoreLighting lighting)
        {
            magmaShoreLighting = lighting;
        }

        // 座標、状態、タイルBufferを持つチャンクだけを取得する、World寿命中再利用するQueryです。
        private EntityQuery chunkQuery;

        // カメラが要求する可視矩形を取得するSingleton Queryです。
        private EntityQuery observerQuery;

        // AuthoringからBakeされたTilemap適用予算を取得するSingleton Queryです。
        private EntityQuery streamingSettingsQuery;

        // Liquid Runtime IDを淡水・海水・マグマ表示へ対応付けるSingleton Queryです。
        private EntityQuery terrainCatalogQuery;

        // 自然生成へ重ねる永続地形差分を座標別に取得するQueryです。表示はこの差分を合成した有効地形を参照します。
        private EntityQuery terrainDeltaQuery;

        // 41材質のGround／Wall Runtime IDと安定材質IDの対応を取得するSingleton Queryです。
        private EntityQuery terrainMaterialCatalogQuery;

        // 表示差分の決定論的選択へ使う保存済みWorldSeedを取得するSingleton Queryです。
        private EntityQuery worldGenerationSettingsQuery;

        // 現在Worldのカタログから毎フレーム取得し、サブチャンク分類中に同じ組み込みIDを再利用します。
        private BuiltInTerrainBlockRuntimeIds terrainRuntimeIds;

        // 差分Runtime IDの分類検証と有効地形合成へ使う、現在Worldの読み取り専用地形カタログです。
        private BlobAssetReference<TerrainBlockCatalogBlob> terrainCatalogBlob;

        // 現在Worldの41材質Blobから一度だけ構築し、セル分類中のRuntime ID解決に再利用します。
        private TerrainMaterialPresentationRuntimeLookup materialRuntimeLookup;

        // 現在Worldで固定された64bit Seedです。表示原点や描画順を候補選択へ混ぜないために保持します。
        private ulong presentationWorldSeed;

        // SpriteとTintごとのTileを共有し、セル単位のScriptableObject生成を防ぎます。
        private TerrainMaterialRuntimeTileCache materialTileCache;

        // 深度Overlayと反射Proxyの共有Tileを、セル数ではなく色・Sprite差分数に応じて保持します。
        private TerrainLiquidRuntimeTileCache liquidTileCache;

        // 製品カタログ、Runtime検索表、WorldSeedの初期化を現在World向けに完了した場合はtrueです。
        private bool materialPresentationIsReady;

        // 不完全な製品設定を毎フレームConsoleへ繰り返さないため、最初の拒否理由を報告済みか保持します。
        private bool materialPresentationFailureWasLogged;

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

        // 水文・Biomeまで確定した地形Bufferを持つEntityを座標で引き、表示外周が基礎生成へ戻らないようにします。
        private readonly Dictionary<ChunkCoordinate, Entity> completedChunkEntities =
            new Dictionary<ChunkCoordinate, Entity>();

        // ストリーミングEntityと寿命が異なる永続差分所有者を、レイヤー付きチャンク座標で引く再利用索引です。
        private readonly Dictionary<ChunkCoordinate, Entity> terrainDeltaOwnerEntities =
            new Dictionary<ChunkCoordinate, Entity>();

        // 各差分Bufferの内容ハッシュです。毎フレーム疎な差分だけを走査し、変更された近傍だけをDirty再描画します。
        private readonly Dictionary<ChunkCoordinate, ulong> terrainDeltaFingerprints =
            new Dictionary<ChunkCoordinate, ulong>();

        // Tilemapへ最後に反映した中心・周辺・直下の完成Entity寿命と差分内容の合成ハッシュです。
        // Floating Originでは論理地形が変わらないため値を維持します。
        private readonly Dictionary<ChunkCoordinate, ulong> displayedTerrainFingerprints =
            new Dictionary<ChunkCoordinate, ulong>();

        // 消去を要求した順番と途中位置を保持し、フレーム予算を超えないよう分割処理します。
        private readonly Queue<ChunkClearProgress> clearQueue = new Queue<ChunkClearProgress>();

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

        // 論理セル原点の基礎液面Tilemapへ渡す256要素の再利用配列です。土地セルにはnullを設定します。
        private TileBase[] liquidBaseSubChunkTileBuffer;

        // マグマ模様Tilemapへ渡す256要素の再利用配列です。マグマ以外のセルにはnullを設定します。
        private TileBase[] magmaSurfacePatternSubChunkTileBuffer;

        // 直下床ありの水だけへ45px深度Overlay Tileを置く256要素の再利用配列です。
        private TileBase[] liquidDepthWithFloorSubChunkTileBuffer;

        // 直下床なしの水だけへ93px深度Overlay Tileを置く256要素の再利用配列です。
        private TileBase[] liquidDepthWithoutFloorSubChunkTileBuffer;

        // 水種・床有無・その列の投影段数を渡す、Presenter寿命中の再利用配列です。隣列とは補間しません。
        private Color[] waterDepthSubChunkColorBuffer;

        // 色付きTileの一括反映に使う256件です。更新ごとに配列を確保しません。
        private TileChangeData[] waterTileChangeBuffer;

        // 水中断面またはマグマ岸の縁のSpriteを保持します。距離と分類印は上の共通色配列へ分離します。
        private TileBase[] submergedGroundFaceSubChunkTileBuffer;

        // 水域4段目以降へ直下1層のGround上面を置く256要素の再利用配列です。
        private TileBase[] underwaterFloorPreviewSubChunkTileBuffer;

        // 淡水・海水セルだけへ共有全面Tileを置く256件です。模様ごとのObjectは作りません。
        private TileBase[] waterSurfacePatternSubChunkTileBuffer;

        // 7bit接触形状に対応する静止水際Tileを置く256要素の再利用配列です。
        private TileBase[] liquidEdgeWaveSubChunkTileBuffer;

        // 岩壁セルだけへ製品Wall上面Tileを置く256要素の再利用配列です。下層の土地はGround Tilemapへ残します。
        private TileBase[] rockWallSubChunkTileBuffer;

        // 製品Ground崖面を投影先セルへ置く256要素の再利用配列です。
        private TileBase[] groundFaceSubChunkTileBuffer;

        // 製品Wall前面を投影先セルへ置く256要素の再利用配列です。
        private TileBase[] wallFaceSubChunkTileBuffer;

        // Wallと最下段Faceの結合シルエットを左側固定接地影へ渡す256要素の再利用配列です。
        private TileBase[] wallContactShadowLeftSubChunkTileBuffer;

        // Wallと最下段Faceの結合シルエットを右側固定接地影へ渡す256要素の再利用配列です。
        private TileBase[] wallContactShadowRightSubChunkTileBuffer;

        // Wall前面1段目だけを32px折れ目影の上半分へ渡す256要素の再利用配列です。
        private TileBase[] wallFoldShadowTopSubChunkTileBuffer;

        // Wall前面2段目だけを32px折れ目影の下半分へ渡す256要素の再利用配列です。
        private TileBase[] wallFoldShadowBottomSubChunkTileBuffer;

        // 現在反映中の16×16サブチャンクについて、手前にFaceが露出するWall根元だけを保持する再利用配列です。
        private bool[] wallDynamicShadowRootMaskBuffer;

        // Wallマスクから作った横連続区間をサブチャンク間で再利用し、Dirty反映ごとのList生成を避けます。
        private readonly List<TerrainWallShadowRun> wallDynamicShadowRunBuffer =
            new List<TerrainWallShadowRun>(WorldGridConstants.SubChunkSize * WorldGridConstants.SubChunkSize);

        // 論理チャンクごとのComposite Shadow Casterを保持し、カリング・Floating Originと同じ寿命で管理します。
        private readonly Dictionary<ChunkCoordinate, TerrainWallDynamicShadowChunkPresentation>
            wallDynamicShadowByCoordinate =
                new Dictionary<ChunkCoordinate, TerrainWallDynamicShadowChunkPresentation>();

        // 画面外になった動的影を全破棄せず、上限付きで次のチャンク表示へ再利用します。
        private readonly Stack<TerrainWallDynamicShadowChunkPresentation>
            pooledWallDynamicShadowPresentations =
                new Stack<TerrainWallDynamicShadowChunkPresentation>();

        // Runtimeで矩形Casterの透明SpriteRendererへ設定する1×1 Unit Spriteです。このPresenterだけが所有します。
        private Sprite generatedWallDynamicShadowUnitSprite;

        // 乾燥穴の深部黒をFaceより後ろへ置く256要素の再利用配列です。
        private TileBase[] dryCliffDeepBlackSubChunkTileBuffer;

        // 露出したWall上の資源だけを、製品では論理根元から2タイル上の上面へ描画する256要素の再利用配列です。
        private TileBase[] rockWallResourceSubChunkTileBuffer;

        /// <summary>部分公開する岩壁影の三角形Indexを再利用する配列です。容量は1チャンク分です。</summary>
        private readonly List<int> rockWallShadowVisibleTriangles = new List<int>(WorldGridConstants.TilesPerChunk * 6);

        // 表示済みチャンクへ重ねた影GameObjectとMeshを座標別に保持し、アンロード時に同じ単位で破棄します。
        private readonly Dictionary<ChunkCoordinate, RockWallShadowChunkPresentation>
            rockWallShadowByCoordinate =
                new Dictionary<ChunkCoordinate, RockWallShadowChunkPresentation>();

        // 影距離の探索半径を含む80×80の岩壁有無です。GC確保せず同期更新するためNative配列を使います。
        // Awakeで一度確保し、全チャンクで再利用してOnDestroyで破棄します。フレーム間の借用参照は保持しません。
        private NativeArray<bool> rockWallShadowMaskBuffer;

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

        // 現在Tilemapセルと岩壁影Meshの配置に使用している表示原点です。Camera側の変更検出と旧セル消去に使います。
        private DisplayOrigin presentedDisplayOrigin;

        /// <summary>水際・水面模様Shaderへ絶対画素原点を渡す共通プロパティIDです。文字列検索を更新のたびに行いません。</summary>
        private static readonly int ShorelineWorldPixelOffsetId = Shader.PropertyToID("_WorldPixelOffset");

        /// <summary>水際・水面模様の全チャンクで同じ時計を使うため、表示時間（秒）を渡す共通プロパティIDです。</summary>
        private static readonly int ShorelineAnimationTimeId = Shader.PropertyToID("_AnimationTimeSeconds");

        /// <summary>Scene見本から借用する水際Rendererです。破棄時に元のMaterial参照へ戻します。</summary>
        private TilemapRenderer shorelineMaterialRenderer;

        /// <summary>Project内の元Materialです。PresenterはこのAssetを変更・破棄しません。</summary>
        private Material shorelineSourceMaterial;

        /// <summary>全チャンクで共有し、Presenterが生成・破棄する水際Materialの実行時コピーです。</summary>
        private Material shorelineRuntimeMaterial;

        /// <summary>最後にShaderへ送った原点です。原点が変わらないフレームでは原点用プロパティを書き換えません。</summary>
        private int2 shorelineMaterialOrigin;

        /// <summary>原点(0,0)を未初期化と区別するためのフラグです。</summary>
        private bool hasShorelineMaterialOrigin;

        /// <summary>水面模様用の借用Rendererです。設定変更・破棄時に元のMaterialへ戻します。</summary>
        private TilemapRenderer surfacePatternMaterialRenderer;

        /// <summary>変更せず借用するProject内の水面模様Materialです。</summary>
        private Material surfacePatternSourceMaterial;

        /// <summary>全チャンクで共有し、Presenterが生成・破棄する水面模様Materialのコピーです。</summary>
        private Material surfacePatternRuntimeMaterial;

        /// <summary>最後に水面模様へ送ったタイル原点です。変更時だけ画素オフセットを更新します。</summary>
        private int2 surfacePatternMaterialOrigin;

        /// <summary>水面模様の原点を初回送信したかを保持します。破棄時にfalseへ戻します。</summary>
        private bool hasSurfacePatternMaterialOrigin;

        /// <summary>マグマ模様用の借用Rendererです。実行時Materialの設定先としてPresenterの寿命中保持します。</summary>
        private TilemapRenderer magmaSurfacePatternMaterialRenderer;

        /// <summary>変更せず借用するProject内のマグマ模様Materialです。</summary>
        private Material magmaSurfacePatternSourceMaterial;

        /// <summary>全チャンクで共有し、Presenterが生成・破棄するマグマ模様Materialの実行時コピーです。</summary>
        private Material magmaSurfacePatternRuntimeMaterial;

        /// <summary>最後にマグマ模様へ送ったタイル原点です。Floating Origin変更時だけ更新します。</summary>
        private int2 magmaSurfacePatternMaterialOrigin;

        /// <summary>マグマ模様へ絶対原点を一度でも送ったかを、原点(0,0)と区別して保持します。</summary>
        private bool hasMagmaSurfacePatternMaterialOrigin;

        // presentedDisplayOriginを初期化済みか示し、絶対原点(0, 0)自体も有効値として区別します。
        private bool hasPresentedDisplayOrigin;

        /// <summary>構築ツールまたはテストから、地面・資源の表示先と資源Tileを設定します。地形の見た目は製品カタログから選びます。</summary>
        /// <param name="terrainTilemap">Ground上面の描画設定見本です。</param>
        /// <param name="generatedResourceTilemap">Wallより手前に描く資源レイヤーの見本です。</param>
        /// <param name="treeResource">樹木の仮資源Tileです。</param>
        /// <param name="stoneResource">石の仮資源Tileです。</param>
        /// <param name="sandResource">砂の仮資源Tileです。</param>
        public void Configure(Tilemap terrainTilemap, Tilemap generatedResourceTilemap,
            TileBase treeResource, TileBase stoneResource, TileBase sandResource)
        {
            targetTilemap = terrainTilemap;
            resourceTilemap = generatedResourceTilemap;
            treeResourceTile = treeResource;
            stoneResourceTile = stoneResource;
            sandResourceTile = sandResource;
        }

        /// <summary>
        /// 画面手前側の2セル以内にあるWallに隠す通常資源専用Tilemapを設定します。
        /// </summary>
        /// <param name="behindRockWallTilemap">岩壁より小さいSorting Orderで描画する資源Tilemapです。</param>
        public void ConfigureResourceOcclusion(Tilemap behindRockWallTilemap)
        {
            resourceBehindRockWallTilemap = behindRockWallTilemap;
        }

        /// <summary>
        /// Editor構築ツールまたはテストから、液面、マグマ模様、岩壁と岩壁上資源の表示先を設定します。
        /// ECSのGeneratedTerrainCellは変更せず、表示の高さだけを組み立てるPresentation設定を保存します。
        /// </summary>
        /// <param name="baseLiquidTilemap">淡水・海水・マグマの静止した基礎Tile専用Tilemapです。</param>
        /// <param name="magmaPatternTilemap">マグマだけへ手続き型の網状模様を描くTilemapです。</param>
        /// <param name="combinedRockWallTilemap">製品Wall上面を根元の2タイル奥へ描画する専用Tilemapです。</param>
        /// <param name="raisedRockWallResourceTilemap">露出したWall上の資源を、製品では論理根元から2タイル上へ持ち上げる専用Tilemapです。</param>
        public void ConfigureTerrainEffects(
            Tilemap baseLiquidTilemap,
            Tilemap magmaPatternTilemap,
            Tilemap combinedRockWallTilemap,
            Tilemap raisedRockWallResourceTilemap)
        {
            liquidBaseTilemap = baseLiquidTilemap;
            magmaSurfacePatternTilemap = magmaPatternTilemap;
            rockWallTilemap = combinedRockWallTilemap;
            rockWallResourceTilemap = raisedRockWallResourceTilemap;
        }

        /// <summary>
        /// Editor構築ツールまたはテストから、41材質の製品上面・断面表示を設定します。
        /// 全参照が設定された場合だけ新経路を有効にし、不完全なカタログを旧Biome Tileへ代替しません。
        /// </summary>
        /// <param name="catalog">41材質を完全登録した製品表示カタログです。</param>
        /// <param name="groundFaces">乾燥崖3段と48px連続減衰を描画するTilemapです。</param>
        /// <param name="wallFaces">Wall前面2段を描画するTilemapです。</param>
        /// <param name="deepBlack">乾燥穴の黒い下地を描画するTilemapです。</param>
        public void ConfigureTerrainMaterialPresentation(
            TerrainMaterialPresentationCatalog catalog,
            Tilemap groundFaces,
            Tilemap wallFaces,
            Tilemap deepBlack)
        {
            bool configurationChanged = materialPresentationCatalog != catalog
                || groundFaceTilemap != groundFaces
                || wallFaceTilemap != wallFaces
                || dryCliffDeepBlackTilemap != deepBlack;
            if (!configurationChanged)
            {
                return;
            }

            // PlayMode中の設定変更では旧Tilemap群を同じフレームに消し、旧表示と製品表示を重ねません。
            if (Application.isPlaying)
            {
                ClearAllPresentedChunksImmediately();
            }

            materialPresentationCatalog = catalog;
            groundFaceTilemap = groundFaces;
            wallFaceTilemap = wallFaces;
            dryCliffDeepBlackTilemap = deepBlack;
            ResetMaterialPresentationState(disposeTileCache: true);
        }

        /// <summary>
        /// Editor構築ツールまたはテストから、製品Wallの固定接地影と32px折れ目影に使う4レイヤーを設定します。
        /// 動的影Casterは光源を参照せず、製品材質経路が有効な場合に手前へ露出したWall根元から自動生成します。
        /// </summary>
        /// <param name="contactLeft">左1px・手前3pxの固定接地影Tilemapです。</param>
        /// <param name="contactRight">右1px・手前3pxの固定接地影Tilemapです。</param>
        /// <param name="foldTop">32px折れ目影の0〜15pxを描くTilemapです。</param>
        /// <param name="foldBottom">32px折れ目影の16〜31pxを描くTilemapです。</param>
        public void ConfigureTerrainWallShadowPresentation(
            Tilemap contactLeft,
            Tilemap contactRight,
            Tilemap foldTop,
            Tilemap foldBottom)
        {
            bool configurationChanged = wallContactShadowLeftTilemap != contactLeft
                || wallContactShadowRightTilemap != contactRight
                || wallFoldShadowTopTilemap != foldTop
                || wallFoldShadowBottomTilemap != foldBottom;
            if (!configurationChanged)
            {
                return;
            }

            // PlayMode中の差し替えでは旧TilemapとCasterを先に消し、部分設定の影を新しい地形へ重ねません。
            if (Application.isPlaying)
            {
                ClearAllPresentedChunksImmediately();
            }

            wallContactShadowLeftTilemap = contactLeft;
            wallContactShadowRightTilemap = contactRight;
            wallFoldShadowTopTilemap = foldTop;
            wallFoldShadowBottomTilemap = foldBottom;
        }

        /// <summary>
        /// Editor構築ツールまたはテストから、製品Liquid表示カタログ、専用レイヤー、反射の描画設定を設定します。
        /// 全参照が揃った製品カタログだけを使用し、マグマ模様と水面模様の描画先を分離します。
        /// </summary>
        /// <param name="catalog">淡水・海水・マグマを完全登録した製品Liquid表示カタログです。</param>
        /// <param name="depthWithFloor">直下床あり45px深度OverlayのTilemapです。</param>
        /// <param name="depthWithoutFloor">直下床なし93px深度OverlayのTilemapです。</param>
        /// <param name="floorPreview">直下1層のGround上面を簡易投影するTilemapです。</param>
        /// <param name="submergedFaces">乾燥崖から分離した水中Ground断面のTilemapです。</param>
        /// <param name="surfacePattern">淡水・海水の短い水面模様専用Tilemapです。</param>
        /// <param name="magmaPattern">マグマの網状模様専用Tilemapです。</param>
        /// <param name="reflection">反射MeshへMaterialとSortingを渡す設定見本Tilemapです。</param>
        /// <param name="reflectionAtlas">反射元Spriteをまとめる製品Atlasです。</param>
        /// <param name="edgeWave">静止接触形状とShader明暗を描く水際Tilemapです。</param>
        public void ConfigureTerrainLiquidPresentation(
            TerrainLiquidPresentationCatalog catalog,
            Tilemap depthWithFloor,
            Tilemap depthWithoutFloor,
            Tilemap floorPreview,
            Tilemap submergedFaces,
            Tilemap surfacePattern,
            Tilemap magmaPattern,
            Tilemap reflection,
            UnityEngine.U2D.SpriteAtlas reflectionAtlas,
            Tilemap edgeWave)
        {
            bool configurationChanged = liquidPresentationCatalog != catalog
                || liquidDepthWithFloorTilemap != depthWithFloor
                || liquidDepthWithoutFloorTilemap != depthWithoutFloor
                || underwaterFloorPreviewTilemap != floorPreview
                || submergedGroundFaceTilemap != submergedFaces
                || waterSurfacePatternTilemap != surfacePattern
                || magmaSurfacePatternTilemap != magmaPattern
                || waterReflectionTilemap != reflection
                || waterReflectionAtlas != reflectionAtlas
                || liquidEdgeWaveTilemap != edgeWave;
            if (!configurationChanged)
            {
                return;
            }

            // PlayMode中の設定変更では旧Tilemap群を同じフレームに消し、新旧Liquid表現の重なりを防ぎます。
            if (Application.isPlaying)
            {
                ClearAllPresentedChunksImmediately();
            }

            liquidPresentationCatalog = catalog;
            liquidDepthWithFloorTilemap = depthWithFloor;
            liquidDepthWithoutFloorTilemap = depthWithoutFloor;
            underwaterFloorPreviewTilemap = floorPreview;
            submergedGroundFaceTilemap = submergedFaces;
            waterSurfacePatternTilemap = surfacePattern;
            magmaSurfacePatternTilemap = magmaPattern;
            waterReflectionTilemap = reflection;
            if (waterReflectionAtlas != reflectionAtlas)
            {
                // 設定変更前のMeshは上で消去済みです。別Atlasの同名Spriteを混在させません。
                foreach (Sprite sprite in reflectionAtlasSprites.Values) DestroyUnityObject(sprite);
                reflectionAtlasSprites.Clear();
            }
            waterReflectionAtlas = reflectionAtlas;
            liquidEdgeWaveTilemap = edgeWave;
            ResetMaterialPresentationState(disposeTileCache: true);
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
            magmaSurfacePatternSubChunkTileBuffer = new TileBase[tileCount];
            liquidDepthWithFloorSubChunkTileBuffer = new TileBase[tileCount];
            liquidDepthWithoutFloorSubChunkTileBuffer = new TileBase[tileCount];
            waterDepthSubChunkColorBuffer = new Color[tileCount];
            waterTileChangeBuffer = new TileChangeData[tileCount];
            submergedGroundFaceSubChunkTileBuffer = new TileBase[tileCount];
            underwaterFloorPreviewSubChunkTileBuffer = new TileBase[tileCount];
            waterSurfacePatternSubChunkTileBuffer = new TileBase[tileCount];
            liquidEdgeWaveSubChunkTileBuffer = new TileBase[tileCount];
            rockWallSubChunkTileBuffer = new TileBase[tileCount];
            groundFaceSubChunkTileBuffer = new TileBase[tileCount];
            wallFaceSubChunkTileBuffer = new TileBase[tileCount];
            wallContactShadowLeftSubChunkTileBuffer = new TileBase[tileCount];
            wallContactShadowRightSubChunkTileBuffer = new TileBase[tileCount];
            wallFoldShadowTopSubChunkTileBuffer = new TileBase[tileCount];
            wallFoldShadowBottomSubChunkTileBuffer = new TileBase[tileCount];
            wallDynamicShadowRootMaskBuffer = new bool[tileCount];
            dryCliffDeepBlackSubChunkTileBuffer = new TileBase[tileCount];
            rockWallResourceSubChunkTileBuffer = new TileBase[tileCount];
            AllocateRockWallShadowBuffers();
            EnsureWallDynamicShadowUnitSprite();
        }

        /// <summary>
        /// 動的影Casterの透明SpriteRendererが1×1 Unity Unitの矩形輪郭を持つよう、共有Spriteを1枚だけ作ります。
        /// </summary>
        private void EnsureWallDynamicShadowUnitSprite()
        {
            if (generatedWallDynamicShadowUnitSprite != null)
            {
                return;
            }

            Texture2D texture = Texture2D.whiteTexture;
            generatedWallDynamicShadowUnitSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                texture.width,
                0,
                SpriteMeshType.FullRect);
            generatedWallDynamicShadowUnitSprite.name = "Generated Terrain Wall Shadow Unit Sprite";
            generatedWallDynamicShadowUnitSprite.hideFlags = HideFlags.DontSave;
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
        /// MonoBehaviourの各描画フレームで呼ばれ、表示対象の収集と予算分割された反映・消去を実行します。
        /// </summary>
        private void Update()
        {
            using (PresentationProfilerMarker.Auto())
            {
                // チャンク用Rendererが見本Materialを複製参照する前に、全チャンク共有の実行時Materialを用意します。
                if (!TrySynchronizeShorelineMaterial() || !TrySynchronizeSurfacePatternMaterial()
                    || !TrySynchronizeMagmaSurfacePatternMaterial()
                    || !TrySynchronizeReflectionMaterial())
                {
                    return;
                }

                // カタログ未設定のSceneを旧表示へ読み替えません。設定漏れは一度だけ具体的に通知し、描画を開始しません。
                if (materialPresentationCatalog == null || liquidPresentationCatalog == null)
                {
                    LogMaterialPresentationFailureOnce("41材質と3液体の製品表示カタログを両方設定してください。");
                    return;
                }
                if (targetTilemap == null || liquidBaseTilemap == null || resourceTilemap == null
                    || resourceBehindRockWallTilemap == null || rockWallTilemap == null
                    || groundFaceTilemap == null || wallFaceTilemap == null || dryCliffDeepBlackTilemap == null
                    || magmaSurfacePatternTilemap == null || liquidDepthWithFloorTilemap == null
                    || liquidDepthWithoutFloorTilemap == null || underwaterFloorPreviewTilemap == null
                    || submergedGroundFaceTilemap == null || waterSurfacePatternTilemap == null
                    || waterReflectionTilemap == null || waterReflectionAtlas == null || liquidEdgeWaveTilemap == null)
                {
                    LogMaterialPresentationFailureOnce("製品の上面・断面・液体・反射のTilemapと製品Atlasの参照をすべて設定してください。");
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

                if (terrainCatalogQuery.CalculateEntityCount() != 1)
                {
                    return;
                }

                TerrainBlockCatalog terrainCatalog = terrainCatalogQuery
                    .GetSingleton<TerrainBlockCatalog>();
                terrainRuntimeIds = terrainCatalog.BuiltInRuntimeIds;
                terrainCatalogBlob = terrainCatalog.Value;
                if (!terrainRuntimeIds.IsValid || !terrainCatalogBlob.IsCreated)
                {
                    return;
                }

                if (!TryPrepareMaterialPresentation())
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
                bool requiresImmediateVisibleRebuild = displayOriginChange == DisplayOriginChangeKind.Explicit;
                using (PresentationWorkCollectionProfilerMarker.Auto())
                {
                    // 明示移動では、余白まで同期再構築して停止を増やさないよう可視範囲だけに限定します。
                    // 先行準備は次の通常フレームから同じ予算で再開します。
                    CollectPresentationWork(observer, requiresImmediateVisibleRebuild);
                }

                if (requiresImmediateVisibleRebuild)
                {
                    using (FloatingOriginRebaseProfilerMarker.Auto())
                    {
                        // 原点変更直後は新しいCamera周辺を同じフレームに完成させ、空白フレームを見せません。
                        // 自動変更は既存表示の位置だけを変え、この再構築を行いません。
                        int unlimitedVisibleBudget = ProcessClearQueue(int.MaxValue);
                        ProcessApplyQueue(unlimitedVisibleBudget);
                    }

                    return;
                }

                int remainingBudget = GetSubChunkBudget();
                remainingBudget = ProcessClearQueue(remainingBudget);
                remainingBudget = ProcessApplyQueue(remainingBudget);
            }
        }

        /// <summary>各Updateの先頭で水際Material・表示時間・絶対原点を同期します。Assetは変更せず、複製は全チャンクで1件共有します。</summary>
        /// <returns>製品液体が未設定、または必要な水際Materialを同期できた場合はtrueです。</returns>
        private bool TrySynchronizeShorelineMaterial()
        {
            if (liquidPresentationCatalog == null || liquidEdgeWaveTilemap == null)
            {
                return true;
            }

            TilemapRenderer renderer = liquidEdgeWaveTilemap.GetComponent<TilemapRenderer>();
            if (shorelineRuntimeMaterial == null || shorelineMaterialRenderer != renderer)
            {
                DisposeShorelineMaterial();
                if (renderer == null || renderer.sharedMaterial == null
                    || renderer.sharedMaterial.shader.name != "Farmware/Terrain/Liquid Shoreline")
                {
                    LogMaterialPresentationFailureOnce("製品水際TilemapにはLiquid Shoreline Materialが必要です。");
                    return false;
                }

                shorelineMaterialRenderer = renderer;
                shorelineSourceMaterial = renderer.sharedMaterial;
                shorelineRuntimeMaterial = new Material(shorelineSourceMaterial)
                {
                    name = "Runtime Liquid Shoreline",
                    hideFlags = HideFlags.DontSave
                };
                renderer.sharedMaterial = shorelineRuntimeMaterial;
            }

            // Time.timeは見た目専用の共通時計です。論理Tickや液体量へ影響させず、Shader側でコマを保持します。
            // セルやチャンクを更新せず、共有Materialの時刻1件だけを毎描画フレームへ渡します。
            shorelineRuntimeMaterial.SetFloat(ShorelineAnimationTimeId, Time.time);

            int2 origin = cameraPanController == null
                ? int2.zero
                : cameraPanController.CurrentDisplayOrigin.AbsoluteTileCoordinate;
            if (!hasShorelineMaterialOrigin
                || origin.x != shorelineMaterialOrigin.x || origin.y != shorelineMaterialOrigin.y)
            {
                // 絶対タイル座標を整数のまま周期内へ丸め、遠方でもfloatへ巨大座標を渡しません。
                shorelineRuntimeMaterial.SetVector(ShorelineWorldPixelOffsetId, new Vector4(
                    TerrainLiquidPresentationUtility.GetShorelineOriginPixelOffset(origin.x),
                    TerrainLiquidPresentationUtility.GetShorelineOriginPixelOffset(origin.y), 0f, 0f));
                shorelineMaterialOrigin = origin;
                hasShorelineMaterialOrigin = true;
            }

            return true;
        }

        /// <summary>設定先変更またはOnDestroyで、借用Materialを戻して実行時コピーだけを破棄します。</summary>
        private void DisposeShorelineMaterial()
        {
            if (shorelineMaterialRenderer != null
                && shorelineMaterialRenderer.sharedMaterial == shorelineRuntimeMaterial)
            {
                shorelineMaterialRenderer.sharedMaterial = shorelineSourceMaterial;
            }

            if (shorelineRuntimeMaterial != null)
            {
                DestroyUnityObject(shorelineRuntimeMaterial);
            }

            shorelineRuntimeMaterial = null;
            shorelineSourceMaterial = null;
            shorelineMaterialRenderer = null;
            hasShorelineMaterialOrigin = false;
        }

        /// <summary>各Updateで水面模様の共通時計・絶対原点を同期します。Assetや個々のTileは毎フレーム変更しません。</summary>
        /// <returns>未接続、または共有Materialを正常に同期できた場合はtrueです。</returns>
        private bool TrySynchronizeSurfacePatternMaterial()
        {
            if (liquidPresentationCatalog == null || waterSurfacePatternTilemap == null)
            {
                return true;
            }

            TilemapRenderer renderer = waterSurfacePatternTilemap.GetComponent<TilemapRenderer>();
            if (surfacePatternRuntimeMaterial == null || surfacePatternMaterialRenderer != renderer)
            {
                DisposeSurfacePatternMaterial();
                if (renderer == null || renderer.sharedMaterial == null
                    || renderer.sharedMaterial.shader.name != "Farmware/Terrain/Water Surface Pattern")
                {
                    LogMaterialPresentationFailureOnce("水面模様TilemapにはWater Surface Pattern Materialが必要です。");
                    return false;
                }

                // Projectの調整値を保護し、チャンク複製の前に1件の共有コピーへ差し替えます。
                surfacePatternMaterialRenderer = renderer;
                surfacePatternSourceMaterial = renderer.sharedMaterial;
                surfacePatternRuntimeMaterial = new Material(surfacePatternSourceMaterial)
                {
                    name = "Runtime Water Surface Pattern",
                    hideFlags = HideFlags.DontSave
                };
                renderer.sharedMaterial = surfacePatternRuntimeMaterial;
            }

            // 見た目だけの時間です。水際と時計を共有しても、各模様の位相・周期・休止はShader内で独立します。
            surfacePatternRuntimeMaterial.SetFloat(ShorelineAnimationTimeId, Time.time);
            int2 origin = cameraPanController == null
                ? int2.zero
                : cameraPanController.CurrentDisplayOrigin.AbsoluteTileCoordinate;
            if (!hasSurfacePatternMaterialOrigin || origin.x != surfacePatternMaterialOrigin.x
                || origin.y != surfacePatternMaterialOrigin.y)
            {
                // 既存水際と同じ4096px整数周期を使い、巨大な絶対座標をfloatへ変換しません。
                surfacePatternRuntimeMaterial.SetVector(ShorelineWorldPixelOffsetId, new Vector4(
                    TerrainLiquidPresentationUtility.GetShorelineOriginPixelOffset(origin.x),
                    TerrainLiquidPresentationUtility.GetShorelineOriginPixelOffset(origin.y), 0f, 0f));
                surfacePatternMaterialOrigin = origin;
                hasSurfacePatternMaterialOrigin = true;
            }
            return true;
        }

        /// <summary>設定先変更・OnDestroyで借用参照を戻し、自分が所有する水面模様Materialだけを破棄します。</summary>
        private void DisposeSurfacePatternMaterial()
        {
            if (surfacePatternMaterialRenderer != null
                && surfacePatternMaterialRenderer.sharedMaterial == surfacePatternRuntimeMaterial)
            {
                surfacePatternMaterialRenderer.sharedMaterial = surfacePatternSourceMaterial;
            }

            if (surfacePatternRuntimeMaterial != null)
            {
                DestroyUnityObject(surfacePatternRuntimeMaterial);
            }

            surfacePatternRuntimeMaterial = null;
            surfacePatternSourceMaterial = null;
            surfacePatternMaterialRenderer = null;
            hasSurfacePatternMaterialOrigin = false;
        }

        /// <summary>各Updateでマグマ網状模様の共通時計・絶対原点を同期します。セルやMaterial Assetは毎フレーム変更しません。</summary>
        /// <returns>製品液体が未設定、または専用Materialを正常に同期できた場合はtrueです。</returns>
        private bool TrySynchronizeMagmaSurfacePatternMaterial()
        {
            if (liquidPresentationCatalog == null || magmaSurfacePatternTilemap == null)
            {
                return true;
            }

            TilemapRenderer renderer = magmaSurfacePatternTilemap.GetComponent<TilemapRenderer>();
            if (magmaSurfacePatternRuntimeMaterial == null
                || magmaSurfacePatternMaterialRenderer != renderer)
            {
                DisposeMagmaSurfacePatternMaterial();
                if (renderer == null || renderer.sharedMaterial == null
                    || renderer.sharedMaterial.shader.name != "Farmware/Terrain/Magma Surface Pattern")
                {
                    LogMaterialPresentationFailureOnce(
                        "マグマ模様TilemapにはMagma Surface Pattern Materialが必要です。");
                    return false;
                }

                // Inspectorで比較調整するAssetを保護し、実行時に変わる時計と原点は共有コピー1件だけへ渡します。
                magmaSurfacePatternMaterialRenderer = renderer;
                magmaSurfacePatternSourceMaterial = renderer.sharedMaterial;
                magmaSurfacePatternRuntimeMaterial = new Material(magmaSurfacePatternSourceMaterial)
                {
                    name = "Runtime Magma Surface Pattern",
                    hideFlags = HideFlags.DontSave
                };
                renderer.sharedMaterial = magmaSurfacePatternRuntimeMaterial;
            }

            // 表示専用時間は論理固定Tickやマグマ生成結果へ書き戻さず、Shader側で指定FPSへ段階化します。
            magmaSurfacePatternRuntimeMaterial.SetFloat(ShorelineAnimationTimeId, Time.time);
            int2 origin = cameraPanController == null
                ? int2.zero
                : cameraPanController.CurrentDisplayOrigin.AbsoluteTileCoordinate;
            if (!hasMagmaSurfacePatternMaterialOrigin
                || origin.x != magmaSurfacePatternMaterialOrigin.x
                || origin.y != magmaSurfacePatternMaterialOrigin.y)
            {
                // 水際・水面模様と同じ4096px周期へ整数のまま戻し、遠方座標のfloat精度低下を避けます。
                magmaSurfacePatternRuntimeMaterial.SetVector(ShorelineWorldPixelOffsetId, new Vector4(
                    TerrainLiquidPresentationUtility.GetShorelineOriginPixelOffset(origin.x),
                    TerrainLiquidPresentationUtility.GetShorelineOriginPixelOffset(origin.y), 0f, 0f));
                magmaSurfacePatternMaterialOrigin = origin;
                hasMagmaSurfacePatternMaterialOrigin = true;
            }

            return true;
        }

        /// <summary>設定先変更・OnDestroyで借用参照を戻し、自分が所有するマグマ模様Materialだけを破棄します。</summary>
        private void DisposeMagmaSurfacePatternMaterial()
        {
            if (magmaSurfacePatternMaterialRenderer != null
                && magmaSurfacePatternMaterialRenderer.sharedMaterial == magmaSurfacePatternRuntimeMaterial)
            {
                magmaSurfacePatternMaterialRenderer.sharedMaterial = magmaSurfacePatternSourceMaterial;
            }

            if (magmaSurfacePatternRuntimeMaterial != null)
            {
                DestroyUnityObject(magmaSurfacePatternRuntimeMaterial);
            }

            magmaSurfacePatternRuntimeMaterial = null;
            magmaSurfacePatternSourceMaterial = null;
            magmaSurfacePatternMaterialRenderer = null;
            hasMagmaSurfacePatternMaterialOrigin = false;
        }

        /// <summary>描画前に共有反射Materialを作り、共通時計・絶対画素原点だけを同期します。</summary>
        /// <returns>未接続、または必要なMaterialを用意できた場合はtrueです。</returns>
        private bool TrySynchronizeReflectionMaterial()
        {
            if (liquidPresentationCatalog == null || waterReflectionTilemap == null) return true;
            Renderer renderer = waterReflectionTilemap.GetComponent<Renderer>();
            if (reflectionRuntimeMaterial == null)
            {
                if (renderer.sharedMaterial == null
                    || renderer.sharedMaterial.shader.name != "Farmware/Terrain/Water Reflection Proxy")
                {
                    LogMaterialPresentationFailureOnce("反射投影にはWater Reflection Proxy Materialが必要です。");
                    return false;
                }
                reflectionSourceMaterial = renderer.sharedMaterial;
                reflectionRuntimeMaterial = new Material(reflectionSourceMaterial)
                { name = "Runtime Water Reflection", hideFlags = HideFlags.DontSave };
                renderer.sharedMaterial = reflectionRuntimeMaterial;
            }
            reflectionRuntimeMaterial.SetFloat(ShorelineAnimationTimeId, Time.time);
            int2 origin = cameraPanController == null ? int2.zero : cameraPanController.CurrentDisplayOrigin.AbsoluteTileCoordinate;
            if (!hasReflectionMaterialOrigin || math.any(reflectionMaterialOrigin != origin))
            {
                reflectionRuntimeMaterial.SetVector(ShorelineWorldPixelOffsetId, new Vector4(
                    TerrainLiquidPresentationUtility.GetShorelineOriginPixelOffset(origin.x),
                    TerrainLiquidPresentationUtility.GetShorelineOriginPixelOffset(origin.y), 0, 0));
                // チャンク内の頂点は維持し、表示原点が変わった場合だけRoot群を移動します。
                foreach (var pair in reflectionMeshes)
                    pair.Value.Reposition(CalculateRockWallShadowMinimumCell(pair.Key, new DisplayOrigin(origin)));
                reflectionMaterialOrigin = origin;
                hasReflectionMaterialOrigin = true;
            }
            return true;
        }

        /// <summary>全反射Meshを破棄します。World・設定変更とComponent無効化から呼びます。</summary>
        private void DestroyReflectionMeshes()
        {
            foreach (TerrainWaterReflectionMesh mesh in reflectionMeshes.Values) mesh.Dispose();
            reflectionMeshes.Clear();
        }

        /// <summary>
        /// Camera側の表示原点変更を検出し、旧相対セルを片付けて新しい原点で再収集できる状態へ戻します。
        /// 自動変更では位置だけ更新し、セル内容・影・反映進捗を保ちます。
        /// 明示テレポートでは旧Viewportとの重なりを避けるため即時消去します。
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
                || rockWallShadowByCoordinate.Count > 0
                || wallDynamicShadowByCoordinate.Count > 0;
            if (hadPresentedContent)
            {
                bool isAutomaticRebase = cameraPanController != null
                    && cameraPanController.LastDisplayOriginChangeWasAutomatic;
                if (isAutomaticRebase)
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

                            foreach (KeyValuePair<ChunkCoordinate, TerrainWallDynamicShadowChunkPresentation> pair in
                                     wallDynamicShadowByCoordinate)
                            {
                                // Casterはチャンク内ローカル形状なので、原点変更ではルート位置だけを動かして再利用します。
                                pair.Value.Reposition(
                                    targetTilemap.CellToLocal(
                                        CalculateRockWallShadowMinimumCell(pair.Key, currentOrigin)));
                            }
                        }

                        RepositionRockWallShadows(currentOrigin);
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
            DestroyAllWallDynamicShadowPresentations();
            DestroyChunkLocalPresentations();
            DisposeShorelineMaterial();
            DisposeSurfacePatternMaterial();
            DisposeMagmaSurfacePatternMaterial();
            if (waterReflectionTilemap != null && reflectionRuntimeMaterial != null)
                waterReflectionTilemap.GetComponent<Renderer>().sharedMaterial = reflectionSourceMaterial;
            DestroyUnityObject(reflectionRuntimeMaterial);
            foreach (Sprite sprite in reflectionAtlasSprites.Values) DestroyUnityObject(sprite);
            reflectionAtlasSprites.Clear();
            // マスク構築は呼び出し内の同期処理なので、破棄時にWorkerの完了を待つ必要はありません。
            if (rockWallShadowMaskBuffer.IsCreated)
            {
                rockWallShadowMaskBuffer.Dispose();
            }

            // 各影Meshは共有Materialを所有しないため、Presenterが作成したMaterialだけを最後に1回破棄します。
            if (generatedRockWallShadowMaterial != null)
            {
                DestroyUnityObject(generatedRockWallShadowMaterial);
                generatedRockWallShadowMaterial = null;
            }

            // Texture2D.whiteTextureはUnity共有物なので破棄せず、そこから作ったSpriteだけを所有者として破棄します。
            if (generatedWallDynamicShadowUnitSprite != null)
            {
                DestroyUnityObject(generatedWallDynamicShadowUnitSprite);
                generatedWallDynamicShadowUnitSprite = null;
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
                ComponentType.ReadOnly<GeneratedTerrainCell>(),
                ComponentType.ReadOnly<GeneratedResourceTile>());
            observerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ChunkStreamingObserver>());
            streamingSettingsQuery =
                entityManager.CreateEntityQuery(ComponentType.ReadOnly<ChunkStreamingSettings>());
            terrainCatalogQuery =
                entityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainBlockCatalog>());
            terrainDeltaQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<TerrainChunkDeltaOwner>(),
                ComponentType.ReadOnly<TerrainCellDelta>());
            terrainMaterialCatalogQuery =
                entityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainBlockMaterialCatalog>());
            worldGenerationSettingsQuery =
                entityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldGenerationSettings>());
            ResetMaterialPresentationState(disposeTileCache: true);
            queriesAreCreated = true;
            return true;
        }

        /// <summary>
        /// 現在Worldの41材質BlobとWorldSeedを製品カタログへ接続し、セル走査前に一度だけ検証します。
        /// </summary>
        /// <returns>フォールバックなしで全材質を解決できる場合はtrueです。</returns>
        private bool TryPrepareMaterialPresentation()
        {
            if (materialPresentationIsReady)
            {
                return true;
            }

            if (materialPresentationCatalog == null)
            {
                return false;
            }

            if (!materialPresentationCatalog.TryInitialize(out string catalogError))
            {
                LogMaterialPresentationFailureOnce(catalogError);
                return false;
            }

            if (liquidPresentationCatalog != null
                && !liquidPresentationCatalog.TryInitialize(out string liquidCatalogError))
            {
                LogMaterialPresentationFailureOnce(liquidCatalogError);
                return false;
            }

            if (terrainMaterialCatalogQuery.CalculateEntityCount() != 1
                || worldGenerationSettingsQuery.CalculateEntityCount() != 1)
            {
                LogMaterialPresentationFailureOnce(
                    "製品材質表示には地形材質カタログとワールド生成設定が各1件必要です。");
                return false;
            }

            TerrainBlockMaterialCatalog runtimeCatalog =
                terrainMaterialCatalogQuery.GetSingleton<TerrainBlockMaterialCatalog>();
            if (!TerrainMaterialPresentationRuntimeLookup.TryCreate(
                    runtimeCatalog.Value,
                    out materialRuntimeLookup,
                    out string runtimeError))
            {
                LogMaterialPresentationFailureOnce(runtimeError);
                return false;
            }

            presentationWorldSeed = worldGenerationSettingsQuery
                .GetSingleton<WorldGenerationSettings>()
                .WorldSeed;
            if (materialTileCache == null)
            {
                materialTileCache = new TerrainMaterialRuntimeTileCache();
            }

            if (liquidPresentationCatalog != null && liquidTileCache == null)
            {
                liquidTileCache = new TerrainLiquidRuntimeTileCache();
            }

            materialPresentationIsReady = true;
            return true;
        }

        /// <summary>製品材質表示の初期化状態を破棄し、Worldまたは設定変更後に再検証できる状態へ戻します。</summary>
        /// <param name="disposeTileCache">生成済みRuntime Tileも破棄する場合はtrueです。</param>
        private void ResetMaterialPresentationState(bool disposeTileCache)
        {
            materialPresentationIsReady = false;
            materialPresentationFailureWasLogged = false;
            materialRuntimeLookup = null;
            presentationWorldSeed = 0UL;
            if (disposeTileCache && materialTileCache != null)
            {
                materialTileCache.Dispose();
                materialTileCache = null;
            }

            if (disposeTileCache && liquidTileCache != null)
            {
                liquidTileCache.Dispose();
                liquidTileCache = null;
            }
        }

        /// <summary>不完全な製品設定を最初の1回だけConsoleへ報告し、毎フレームの同文エラーを防ぎます。</summary>
        /// <param name="reason">人間がカタログまたはScene参照を修正できる具体的な拒否理由です。</param>
        private void LogMaterialPresentationFailureOnce(string reason)
        {
            if (materialPresentationFailureWasLogged)
            {
                return;
            }

            materialPresentationFailureWasLogged = true;
            Debug.LogError($"地形材質の製品表示を開始できません。{reason}", this);
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
            completedChunkEntities.Clear();
            CacheTerrainDeltaOwners(entityManager);

            // 可視チャンクを予約する前に、近傍参照へ使える完成Bufferを全ロード範囲から座標索引化します。
            // 各部分が読む近傍を完成済み在庫Maskで判定し、確定前の単一タイル生成へ戻りません。
            for (int index = 0; index < entities.Length; index++)
            {
                ChunkGenerationStatus status = statuses[index];
                bool hasCompleteData = status.Phase == ChunkGenerationPhase.ReadyToApply
                    || status.Phase == ChunkGenerationPhase.Active
                    || status.Phase == ChunkGenerationPhase.Inactive;
                if (!hasCompleteData
                    || !entityManager.Exists(entities[index])
                    || !entityManager.HasBuffer<GeneratedTerrainCell>(entities[index]))
                {
                    continue;
                }

                DynamicBuffer<GeneratedTerrainCell> completedTiles =
                    entityManager.GetBuffer<GeneratedTerrainCell>(entities[index], true);
                if (completedTiles.Length != WorldGridConstants.TilesPerChunk)
                {
                    continue;
                }

                var completedCoordinate = new ChunkCoordinate(
                    coordinates[index].Layer,
                    coordinates[index].X,
                    coordinates[index].Y);
                completedChunkEntities[completedCoordinate] = entities[index];
            }

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

                    // 中心が完成した時点で予約し、各サブチャンクの必要近傍は反映時に検証します。
                    // 反対側の未完成チャンクのために、入力が揃った範囲まで待たせません。
                    bool hasCompleteCenter = isInsidePreparation && hasCompleteData
                        && completedChunkEntities.ContainsKey(coordinate);
                    if (hasCompleteCenter)
                    {
                        ulong terrainFingerprint = CalculatePresentationTerrainFingerprint(coordinate);
                        if (displayedTerrainFingerprints.TryGetValue(
                                coordinate,
                                out ulong displayedFingerprint)
                            && displayedFingerprint != terrainFingerprint)
                        {
                            // 中心または境界・直下へ影響する差分が変わった場合だけ、既存表示をDirtyとして再構築します。
                            CancelApplyAndScheduleClearIfNeeded(coordinate);
                        }

                        ScheduleApply(coordinate, entities[index], CalculatePendingTerrainFingerprint(coordinate));
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
        /// 永続地形差分をレイヤー付きチャンク座標で索引化し、疎なBuffer内容の決定論的ハッシュを更新します。
        /// 差分Entityはストリーミング対象外なので、画面外へ出ても破棄せず再訪時に同じ有効地形へ戻せます。
        /// </summary>
        /// <param name="entityManager">現在のDefault Worldから差分ComponentとBufferを読む管理境界です。</param>
        private void CacheTerrainDeltaOwners(EntityManager entityManager)
        {
            terrainDeltaOwnerEntities.Clear();
            terrainDeltaFingerprints.Clear();
            using NativeArray<Entity> entities = terrainDeltaQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<TerrainChunkDeltaOwner> owners =
                terrainDeltaQuery.ToComponentDataArray<TerrainChunkDeltaOwner>(Allocator.Temp);
            for (int index = 0; index < entities.Length; index++)
            {
                Entity entity = entities[index];
                TerrainChunkDeltaOwner owner = owners[index];
                var coordinate = new ChunkCoordinate(owner.Layer, owner.ChunkX, owner.ChunkY);
                DynamicBuffer<TerrainCellDelta> deltas =
                    entityManager.GetBuffer<TerrainCellDelta>(entity, true);
                if (!terrainDeltaOwnerEntities.TryAdd(coordinate, entity))
                {
                    throw new System.InvalidOperationException(
                        $"同じチャンク座標の地形差分所有者が重複しています: {coordinate}");
                }

                terrainDeltaFingerprints.Add(coordinate, CalculateDeltaFingerprint(deltas));
            }
        }

        /// <summary>LocalTileIndex順の疎な差分内容を64bit FNV-1aへ畳み込み、生成順に依存しないDirty判定値を作ります。</summary>
        private static ulong CalculateDeltaFingerprint(DynamicBuffer<TerrainCellDelta> deltas)
        {
            const ulong offsetBasis = 1469598103934665603UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offsetBasis;
            for (int index = 0; index < deltas.Length; index++)
            {
                TerrainCellDelta delta = deltas[index];
                hash = (hash ^ delta.LocalTileIndex) * prime;
                hash = (hash ^ (byte)delta.ChangeMask) * prime;
                hash = (hash ^ (uint)delta.GroundBlockRuntimeId.Value) * prime;
                hash = (hash ^ (uint)delta.WallBlockRuntimeId.Value) * prime;
                hash = (hash ^ (uint)delta.LiquidRuntimeId.Value) * prime;
                hash = (hash ^ (uint)delta.TerrainSurfaceRuntimeId.Value) * prime;
            }

            return (hash ^ (uint)deltas.Length) * prime;
        }

        /// <summary>
        /// 中心と周囲8チャンク、および直下レイヤーの完成Entityと差分ハッシュを固定順で合成します。
        /// 完成自然地形は公開後不変なのでEntity世代で寿命を識別し、可変差分はBuffer内容をDirty条件にします。
        /// </summary>
        private ulong CalculatePresentationTerrainFingerprint(ChunkCoordinate center)
        {
            const ulong offsetBasis = 1469598103934665603UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offsetBasis;
            hash = AppendNeighborhoodTerrainEntityFingerprints(hash, center, prime);
            hash = AppendNeighborhoodDeltaFingerprints(hash, center, prime);
            if (TryGetLowerLayer(center.Layer, out WorldLayer lowerLayer))
            {
                var lowerCenter = new ChunkCoordinate(lowerLayer, center.X, center.Y);
                hash = AppendNeighborhoodTerrainEntityFingerprints(hash, lowerCenter, prime);
                hash = AppendNeighborhoodDeltaFingerprints(
                    hash,
                    lowerCenter,
                    prime);
            }

            return hash;
        }

        /// <summary>部分表示の更新境界を、中心Entity・全疎差分・直下層から計算します。隣接の新規到着だけでは進捗を捨てません。</summary>
        /// <param name="center">表示中の論理チャンクです。</param>
        /// <returns>中心寿命・地形編集・直下層変更を検知する指紋です。</returns>
        private ulong CalculatePendingTerrainFingerprint(ChunkCoordinate center)
        {
            const ulong prime = 1099511628211UL;
            Entity entity = completedChunkEntities[center];
            ulong hash = ((ulong)(uint)entity.Index << 32) | (uint)entity.Version;
            hash = AppendNeighborhoodDeltaFingerprints(hash, center, prime);
            if (TryGetLowerLayer(center.Layer, out WorldLayer lowerLayer))
            {
                var lower = new ChunkCoordinate(lowerLayer, center.X, center.Y);
                hash = AppendNeighborhoodTerrainEntityFingerprints(hash, lower, prime);
                hash = AppendNeighborhoodDeltaFingerprints(hash, lower, prime);
            }
            return hash;
        }

        /// <summary>部分表示が実際に使った近傍Entityだけを照合し、削除・再利用されたデータを混ぜないようにします。</summary>
        /// <param name="coordinate">部分反映中の中心です。</param>
        /// <param name="progress">過去に参照した近傍の寿命を保持する進捗です。</param>
        /// <returns>使用済み入力のEntity寿命が同じ場合はtrueです。未使用近傍の新規到着は無視します。</returns>
        private bool UsedNeighborhoodIsCurrent(ChunkCoordinate coordinate, ChunkApplyProgress progress)
        {
            for (int index = 0; index < 9; index++)
            {
                if ((progress.UsedChunkMask & (1 << index)) == 0) continue;
                var neighbor = new ChunkCoordinate(coordinate.Layer, coordinate.X + index % 3 - 1, coordinate.Y + index / 3 - 1);
                if (!completedChunkEntities.TryGetValue(neighbor, out Entity entity) || entity != progress.UsedEntities[index]) return false;
            }
            return true;
        }

        /// <summary>反映した部分が参照した完成近傍のEntity寿命を保存します。Entityは所有しません。</summary>
        /// <param name="coordinate">中心チャンクです。</param>
        /// <param name="progress">部分反映進捗です。</param>
        /// <param name="subChunkIndex">今回反映した番号です。</param>
        private void RememberUsedNeighborhood(ChunkCoordinate coordinate, ChunkApplyProgress progress, int subChunkIndex)
        {
            ushort required = TerrainSubChunkDependencyUtility.GetRequiredChunkMask(coordinate, subChunkIndex);
            for (int index = 0; index < 9; index++)
            {
                if ((required & (1 << index)) == 0) continue;
                var neighbor = new ChunkCoordinate(coordinate.Layer, coordinate.X + index % 3 - 1, coordinate.Y + index / 3 - 1);
                progress.UsedEntities[index] = completedChunkEntities[neighbor];
            }
            progress.UsedChunkMask |= required;
        }

        /// <summary>
        /// 3×3の完成自然地形Entityを固定順で畳み込みます。ワールド内の1件でも未完成なら不完全Markerだけを使い、
        /// 直下層が部分ロードされる順番を表示Fingerprintへ持ち込みません。
        /// </summary>
        private ulong AppendNeighborhoodTerrainEntityFingerprints(
            ulong hash,
            ChunkCoordinate center,
            ulong prime)
        {
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    long chunkX = (long)center.X + offsetX;
                    long chunkY = (long)center.Y + offsetY;
                    bool isOutsideWorld = chunkX < WorldGridConstants.MinimumLogicalChunkCoordinate
                        || chunkX > WorldGridConstants.MaximumLogicalChunkCoordinate
                        || chunkY < WorldGridConstants.MinimumLogicalChunkCoordinate
                        || chunkY > WorldGridConstants.MaximumLogicalChunkCoordinate;
                    if (!isOutsideWorld
                        && !completedChunkEntities.ContainsKey(new ChunkCoordinate(
                            center.Layer,
                            (int)chunkX,
                            (int)chunkY)))
                    {
                        // 部分的な直下層は床プレビューに使わないため、到着順ではなく未完成という1状態へ正規化します。
                        return (hash ^ 0UL) * prime;
                    }
                }
            }

            hash = (hash ^ 1UL) * prime;
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    long chunkX = (long)center.X + offsetX;
                    long chunkY = (long)center.Y + offsetY;
                    ulong value = ulong.MaxValue;
                    if (chunkX >= WorldGridConstants.MinimumLogicalChunkCoordinate
                        && chunkX <= WorldGridConstants.MaximumLogicalChunkCoordinate
                        && chunkY >= WorldGridConstants.MinimumLogicalChunkCoordinate
                        && chunkY <= WorldGridConstants.MaximumLogicalChunkCoordinate)
                    {
                        Entity entity = completedChunkEntities[new ChunkCoordinate(
                            center.Layer,
                            (int)chunkX,
                            (int)chunkY)];
                        // EntityのIndexとVersionを組にし、同じIndexが再利用されても別の完成地形寿命として検出します。
                        value = ((ulong)(uint)entity.Index << 32) | (uint)entity.Version;
                    }

                    hash = (hash ^ value) * prime;
                }
            }

            return hash;
        }

        /// <summary>3×3チャンクを南西から北東の固定順に走査し、存在しない差分も0として位置を保持します。</summary>
        private ulong AppendNeighborhoodDeltaFingerprints(
            ulong hash,
            ChunkCoordinate center,
            ulong prime)
        {
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    long chunkX = (long)center.X + offsetX;
                    long chunkY = (long)center.Y + offsetY;
                    ulong value = 0UL;
                    if (chunkX >= WorldGridConstants.MinimumLogicalChunkCoordinate
                        && chunkX <= WorldGridConstants.MaximumLogicalChunkCoordinate
                        && chunkY >= WorldGridConstants.MinimumLogicalChunkCoordinate
                        && chunkY <= WorldGridConstants.MaximumLogicalChunkCoordinate)
                    {
                        var coordinate = new ChunkCoordinate(
                            center.Layer,
                            (int)chunkX,
                            (int)chunkY);
                        terrainDeltaFingerprints.TryGetValue(coordinate, out value);
                    }

                    hash = (hash ^ value) * prime;
                }
            }

            return hash;
        }

        /// <summary>
        /// 未表示の可視・先行準備チャンクを反映待ちへ追加し、すでに待機中なら最新Entity参照へ更新します。
        /// </summary>
        /// <param name="coordinate">反映する論理チャンク座標です。</param>
        /// <param name="entity">GeneratedTerrainCell Bufferを所有する現在のEntityです。</param>
        private void ScheduleApply(
            ChunkCoordinate coordinate,
            Entity entity,
            ulong terrainFingerprint)
        {
            if (displayedCoordinates.Contains(coordinate) || pendingClearCoordinates.Contains(coordinate))
            {
                return;
            }

            if (applyProgressByCoordinate.TryGetValue(coordinate, out ChunkApplyProgress progress))
            {
                if (progress.TerrainFingerprint != terrainFingerprint || !UsedNeighborhoodIsCurrent(coordinate, progress))
                {
                    // 部分反映中に差分が変わった場合は、新旧サブチャンクを混在させず先頭からやり直します。
                    CancelApplyAndScheduleClearIfNeeded(coordinate);
                    return;
                }

                progress.Entity = entity;
                return;
            }

            applyProgressByCoordinate.Add(
                coordinate,
                new ChunkApplyProgress(entity, terrainFingerprint));
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
                    // 次回ロード時は現在の変更差分と比較し直すため、表示完了時の指紋も同じ寿命で破棄します。
                    displayedTerrainFingerprints.Remove(progress.Coordinate);
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
            EntityManager entityManager = presentationWorld.EntityManager;
            // 各座標を1回ずつ調べ、未完成近傍を待つ先頭のために後続の可視部分まで止めないようにします。
            int coordinatesToVisit = applyQueue.Count;
            while (remainingBudget > 0 && coordinatesToVisit-- > 0 && applyQueue.Count > 0)
            {
                ChunkCoordinate coordinate = applyQueue.Dequeue();
                if (!applyProgressByCoordinate.TryGetValue(coordinate, out ChunkApplyProgress progress)) continue;
                if (!entityManager.Exists(progress.Entity)
                    || !entityManager.HasBuffer<GeneratedTerrainCell>(progress.Entity)
                    || !entityManager.HasBuffer<GeneratedResourceTile>(progress.Entity))
                {
                    CancelApplyAndScheduleClearIfNeeded(coordinate);
                    continue;
                }
                DynamicBuffer<GeneratedResourceTile> resources = entityManager.GetBuffer<GeneratedResourceTile>(progress.Entity, true);
                if (resources.Length != WorldGridConstants.TilesPerChunk
                    || !CompletedChunkTerrainNeighborhoodFactory.TryCreateEffectiveAvailable(entityManager,
                        completedChunkEntities, terrainDeltaOwnerEntities, coordinate, terrainCatalogBlob, out CompletedChunkTerrainNeighborhood neighborhood))
                {
                    applyQueue.Enqueue(coordinate);
                    continue;
                }
                ushort readyMask = TerrainSubChunkDependencyUtility.GetReadySubChunkMask(coordinate, neighborhood.AvailableChunkMask);
                CompletedChunkTerrainNeighborhood lowerNeighborhood = default;
                bool hasLower = TryGetLowerLayer(coordinate.Layer, out WorldLayer lowerLayer)
                    && CompletedChunkTerrainNeighborhoodFactory.TryCreateEffective(entityManager, completedChunkEntities,
                        terrainDeltaOwnerEntities, new ChunkCoordinate(lowerLayer, coordinate.X, coordinate.Y), terrainCatalogBlob, out lowerNeighborhood);
                bool appliedAny = false;
                for (int index = 0; index < WorldGridConstants.SubChunksPerChunk && remainingBudget > 0; index++)
                {
                    int bit = 1 << index;
                    if ((readyMask & bit) == 0 || (progress.AppliedSubChunkMask & bit) != 0) continue;
                    // 必要な入力が全て揃った範囲だけを、既存の分類・Tilemap・反射・影更新へ渡します。
                    ApplySubChunk(coordinate, index, resources, neighborhood, hasLower, lowerNeighborhood);
                    RememberUsedNeighborhood(coordinate, progress, index);
                    progress.AppliedSubChunkMask |= (ushort)bit;
                    progress.NextSubChunkIndex++;
                    remainingBudget--;
                    appliedAny = true;
                }
                bool complete = progress.AppliedSubChunkMask == ushort.MaxValue;
                if (appliedAny)
                {
                    // 影も同じ完成部分の三角形だけを公開し、未完成範囲の仮地形を画面へ混ぜません。
                    BuildRockWallShadowMeshForSubChunks(coordinate, neighborhood, progress.AppliedSubChunkMask);
                }
                if (complete)
                {
                    applyProgressByCoordinate.Remove(coordinate);
                    displayedCoordinates.Add(coordinate);
                    displayedTerrainFingerprints[coordinate] = CalculatePresentationTerrainFingerprint(coordinate);
                    ChunkGenerationStatus status = entityManager.GetComponentData<ChunkGenerationStatus>(progress.Entity);
                    if (status.Phase == ChunkGenerationPhase.ReadyToApply)
                    {
                        status.Phase = ChunkGenerationPhase.Active;
                        entityManager.SetComponentData(progress.Entity, status);
                    }
                }
                else applyQueue.Enqueue(coordinate);
            }
            return remainingBudget;
        }

        /// <summary>
        /// 1つの16×16サブチャンクをDynamic Bufferから再利用配列へコピーし、Tilemapへ一括反映します。
        /// </summary>
        /// <param name="coordinate">対象の論理チャンク座標です。</param>
        /// <param name="subChunkIndex">行優先の0〜15サブチャンク番号です。</param>
        /// <param name="resources">地形と同じ行優先4096件の生成済み資源Bufferです。</param>
        /// <param name="terrainNeighborhood">中心と周囲8チャンクの完成地形です。断面・液体・水際分類に使用します。</param>
        /// <param name="hasLowerNeighborhood">直下WorldLayerの3×3完成地形が準備済みならtrueです。</param>
        /// <param name="lowerNeighborhood">直下床プレビューに使う完成地形です。未準備時は読みません。</param>
        private void ApplySubChunk(
            ChunkCoordinate coordinate,
            int subChunkIndex,
            DynamicBuffer<GeneratedResourceTile> resources,
            in CompletedChunkTerrainNeighborhood terrainNeighborhood,
            bool hasLowerNeighborhood,
            in CompletedChunkTerrainNeighborhood lowerNeighborhood)
        {
            int subChunkX = subChunkIndex % WorldGridConstants.SubChunksPerAxis;
            int subChunkY = subChunkIndex / WorldGridConstants.SubChunksPerAxis;
            int localStartX = subChunkX * WorldGridConstants.SubChunkSize;
            int localStartY = subChunkY * WorldGridConstants.SubChunkSize;
            magmaShoreLighting?.BeginSubChunk(coordinate, subChunkIndex);
            bool wallShadowPresentationIsConfigured =
                HasCompleteTerrainWallShadowConfiguration();
            if (reflectionMeshes.TryGetValue(coordinate, out TerrainWaterReflectionMesh reflectionMesh))
                reflectionMesh.Begin(subChunkIndex);

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
                        // 表示は自然生成Bufferを直接読まず、疎な変更差分を適用した有効地形だけを参照します。
                        // 同じ近傍スナップショットを境界・断面・影へ共有することで、処理順による見た目の食い違いを防ぎます。
                        if (!terrainNeighborhood.TryGetTile(tileLocalX, tileLocalY, out GeneratedTerrainCell terrainCell))
                        {
                            throw new System.InvalidOperationException(
                                $"完成済み地形近傍から中心セルを取得できません: Chunk=({coordinate.X}, {coordinate.Y}), Local=({tileLocalX}, {tileLocalY})");
                        }

                        bool isRockWall = terrainCell.WallBlockRuntimeId.IsValid;
                        RockWallAppearance rockWallAppearance = ClassifyRockWallAppearance(
                            tileLocalX,
                            tileLocalY,
                            terrainCell,
                            terrainNeighborhood);

                        groundFaceSubChunkTileBuffer[destinationIndex] = null;
                        wallFaceSubChunkTileBuffer[destinationIndex] = null;
                        wallContactShadowLeftSubChunkTileBuffer[destinationIndex] = null;
                        wallContactShadowRightSubChunkTileBuffer[destinationIndex] = null;
                        wallFoldShadowTopSubChunkTileBuffer[destinationIndex] = null;
                        wallFoldShadowBottomSubChunkTileBuffer[destinationIndex] = null;
                        wallDynamicShadowRootMaskBuffer[destinationIndex] = false;
                        dryCliffDeepBlackSubChunkTileBuffer[destinationIndex] = null;
                        liquidDepthWithFloorSubChunkTileBuffer[destinationIndex] = null;
                        liquidDepthWithoutFloorSubChunkTileBuffer[destinationIndex] = null;
                        submergedGroundFaceSubChunkTileBuffer[destinationIndex] = null;
                        waterDepthSubChunkColorBuffer[destinationIndex] = Color.white;
                        underwaterFloorPreviewSubChunkTileBuffer[destinationIndex] = null;
                        waterSurfacePatternSubChunkTileBuffer[destinationIndex] = null;
                        liquidEdgeWaveSubChunkTileBuffer[destinationIndex] = null;

                        // 製品経路は上面・断面・深部黒を分離し、境界あり1段目も合成済みSprite 1枚として選びます。
                        int wallFaceDepth = ClassifyProductMaterialTiles(
                            coordinate,
                            tileLocalX,
                            tileLocalY,
                            destinationIndex,
                            terrainCell,
                            terrainNeighborhood);

                        if (wallShadowPresentationIsConfigured)
                        {
                            // 左右の固定影は同じ結合シルエットを別Tilemapへ渡し、Scene側の±1px・手前3px Transformで表示します。
                            // Wall上面とFace投影先は同時に成立しないため、nullでない方を1セルのシルエットとして使用できます。
                            TileBase wallSilhouetteTile = rockWallSubChunkTileBuffer[destinationIndex]
                                ?? wallFaceSubChunkTileBuffer[destinationIndex];
                            wallContactShadowLeftSubChunkTileBuffer[destinationIndex] = wallSilhouetteTile;
                            wallContactShadowRightSubChunkTileBuffer[destinationIndex] = wallSilhouetteTile;

                            // 折れ目影は段別Materialで開始行0／16を固定し、段境界でAlphaが濃く戻らないようにします。
                            if (wallFaceDepth == 0)
                            {
                                wallFoldShadowTopSubChunkTileBuffer[destinationIndex] =
                                    wallFaceSubChunkTileBuffer[destinationIndex];
                            }
                            else if (wallFaceDepth == 1)
                            {
                                wallFoldShadowBottomSubChunkTileBuffer[destinationIndex] =
                                    wallFaceSubChunkTileBuffer[destinationIndex];
                            }

                        }

                        TerrainLiquidVisualKind liquidKind =
                            TerrainLiquidPresentationUtility.Classify(
                                terrainCell,
                                terrainRuntimeIds);
                        liquidBaseSubChunkTileBuffer[destinationIndex] =
                            SelectProductLiquidSurfaceTile(
                                liquidKind,
                                terrainNeighborhood,
                                tileLocalX,
                                tileLocalY);
                        // 手続き模様Shaderは基礎液面と同じSprite・岸側クリップ印を使い、マグマ以外には描きません。
                        magmaSurfacePatternSubChunkTileBuffer[destinationIndex] =
                            liquidKind == TerrainLiquidVisualKind.Magma
                                ? liquidBaseSubChunkTileBuffer[destinationIndex]
                                : null;
                        // Tilemapと同じ差分込み近傍から岸を収集し、照明のためにTilemapを再走査しません。
                        if (liquidKind == TerrainLiquidVisualKind.Magma)
                            magmaShoreLighting?.CollectMagmaCell(coordinate, subChunkIndex,
                                tileLocalX, tileLocalY, terrainNeighborhood);
                        ClassifyProductLiquidTiles(
                            coordinate,
                            subChunkIndex,
                            tileLocalX,
                            tileLocalY,
                            destinationIndex,
                            terrainCell,
                            terrainNeighborhood,
                            hasLowerNeighborhood,
                            lowerNeighborhood);

                        // 内部影に覆われる資源アイコンだけを隠します。ECSの資源データと露出面の表示は維持します。
                        TileBase selectedResource =
                            RockWallAppearanceUtility.ShouldDisplayResource(rockWallAppearance)
                                ? SelectResourceTile(resources[sourceIndex])
                                : null;
                        // TilemapRendererの行順は別Tilemap間を交互に並べないため、遠方座標でも確実な固定レイヤーへ振り分けます。
                        // 製品Wallは論理根元から画面奥へ2タイル伸びるため、手前1〜2セルのWallに重なる資源を背面へ回します。
                        bool isBehindFrontRockWall = false;
                        if (!isRockWall && selectedResource != null)
                        {
                            if (!terrainNeighborhood.TryGetTile(
                                    tileLocalX,
                                    tileLocalY - 1,
                                    out GeneratedTerrainCell nearFrontTerrainCell)
                                || !terrainNeighborhood.TryGetTile(
                                    tileLocalX,
                                    tileLocalY - 2,
                                    out GeneratedTerrainCell farFrontTerrainCell))
                            {
                                throw new System.InvalidOperationException(
                                    $"完成済み地形近傍からWall遮蔽用の手前2セルを取得できません: Chunk=({coordinate.X}, {coordinate.Y}), Local=({tileLocalX}, {tileLocalY - 2})");
                            }

                            isBehindFrontRockWall =
                                TerrainLayerPresentationUtility.ShouldRenderResourceBehindRockWall(
                                    resources[sourceIndex],
                                    terrainCell,
                                    nearFrontTerrainCell,
                                    farFrontTerrainCell);
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
                if (magmaSurfacePatternTilemap != null)
                {
                    WriteSubChunkLayer(coordinate, magmaSurfacePatternTilemap, bounds, magmaSurfacePatternSubChunkTileBuffer);
                }

                WriteSubChunkLayer(
                    coordinate,
                    liquidDepthWithFloorTilemap,
                    bounds,
                    liquidDepthWithFloorSubChunkTileBuffer,
                    waterDepthSubChunkColorBuffer);
                WriteSubChunkLayer(
                    coordinate,
                    liquidDepthWithoutFloorTilemap,
                    bounds,
                    liquidDepthWithoutFloorSubChunkTileBuffer,
                    waterDepthSubChunkColorBuffer);
                WriteSubChunkLayer(coordinate, submergedGroundFaceTilemap, bounds,
                    submergedGroundFaceSubChunkTileBuffer, waterDepthSubChunkColorBuffer);
                WriteSubChunkLayer(
                    coordinate,
                    underwaterFloorPreviewTilemap,
                    bounds,
                    underwaterFloorPreviewSubChunkTileBuffer);
                if (reflectionMeshes.TryGetValue(coordinate, out TerrainWaterReflectionMesh uploadedReflection))
                    uploadedReflection.Upload(subChunkIndex);
                WriteSubChunkLayer(coordinate, waterSurfacePatternTilemap, bounds,
                    waterSurfacePatternSubChunkTileBuffer, waterDepthSubChunkColorBuffer);
                WriteSubChunkLayer(
                    coordinate,
                    liquidEdgeWaveTilemap,
                    bounds,
                    liquidEdgeWaveSubChunkTileBuffer);

                if (rockWallTilemap != null)
                {
                    WriteSubChunkLayer(coordinate, rockWallTilemap, bounds, rockWallSubChunkTileBuffer);
                }

                WriteSubChunkLayer(
                    coordinate,
                    groundFaceTilemap,
                    bounds,
                    groundFaceSubChunkTileBuffer);
                WriteSubChunkLayer(
                    coordinate,
                    wallFaceTilemap,
                    bounds,
                    wallFaceSubChunkTileBuffer);
                WriteSubChunkLayer(
                    coordinate,
                    wallContactShadowLeftTilemap,
                    bounds,
                    wallContactShadowLeftSubChunkTileBuffer);
                WriteSubChunkLayer(
                    coordinate,
                    wallContactShadowRightTilemap,
                    bounds,
                    wallContactShadowRightSubChunkTileBuffer);
                WriteSubChunkLayer(
                    coordinate,
                    wallFoldShadowTopTilemap,
                    bounds,
                    wallFoldShadowTopSubChunkTileBuffer);
                WriteSubChunkLayer(
                    coordinate,
                    wallFoldShadowBottomTilemap,
                    bounds,
                    wallFoldShadowBottomSubChunkTileBuffer);
                WriteSubChunkLayer(
                    coordinate,
                    dryCliffDeepBlackTilemap,
                    bounds,
                    dryCliffDeepBlackSubChunkTileBuffer);

                if (rockWallResourceTilemap != null)
                {
                    WriteSubChunkLayer(coordinate, rockWallResourceTilemap, bounds, rockWallResourceSubChunkTileBuffer);
                }

            }

            if (materialPresentationIsReady && wallShadowPresentationIsConfigured)
            {
                using (TerrainWallDynamicShadowUpdateProfilerMarker.Auto())
                {
                    // 全Wallを2段手前へ投影すると奥側Casterが手前のWall上面を自己遮蔽するため、
                    // 完成済み南隣を含めてFaceが露出する前縁だけを根元として抽出します。
                    TerrainWallShadowUtility.FillExposedSouthRootMask(
                        terrainNeighborhood,
                        localStartX,
                        localStartY,
                        wallDynamicShadowRootMaskBuffer);
                    TerrainWallShadowUtility.BuildHorizontalRuns(
                        wallDynamicShadowRootMaskBuffer,
                        localStartX,
                        localStartY,
                        wallDynamicShadowRunBuffer);
                    GetOrCreateWallDynamicShadowPresentation(coordinate)
                        .UpdateSubChunk(subChunkIndex, wallDynamicShadowRunBuffer);
                }
            }
        }

        /// <summary>
        /// 固定接地影と折れ目影の4レイヤーがすべて揃い、動的影Casterを同じ製品経路で公開できるか確認します。
        /// </summary>
        /// <returns>部分設定を混在させずWall影一式を表示できる場合はtrueです。</returns>
        private bool HasCompleteTerrainWallShadowConfiguration()
        {
            return targetTilemap != null
                && wallContactShadowLeftTilemap != null
                && wallContactShadowRightTilemap != null
                && wallFoldShadowTopTilemap != null
                && wallFoldShadowBottomTilemap != null;
        }

        /// <summary>
        /// 1セル分の製品上面、Ground崖面、Wall前面、乾燥穴の黒下地を再利用Tileへ分類します。
        /// </summary>
        /// <param name="chunkCoordinate">絶対座標へ戻す対象論理チャンクです。</param>
        /// <param name="localX">チャンク内の表示対象Xです。</param>
        /// <param name="localY">チャンク内の表示対象Yです。</param>
        /// <param name="destinationIndex">256要素の再利用配列へ書く位置です。</param>
        /// <param name="terrainCell">現在表示セルの完成済み地形です。</param>
        /// <param name="terrainNeighborhood">最大3段奥の投影元を含む完成地形近傍です。</param>
        /// <returns>Wall前面なら0〜1の段数、それ以外は-1です。</returns>
        private int ClassifyProductMaterialTiles(
            ChunkCoordinate chunkCoordinate,
            int localX,
            int localY,
            int destinationIndex,
            GeneratedTerrainCell terrainCell,
            in CompletedChunkTerrainNeighborhood terrainNeighborhood)
        {
            subChunkTileBuffer[destinationIndex] = null;
            rockWallSubChunkTileBuffer[destinationIndex] = null;

            if (!WorldGrid.TryToWorldTileCoordinate(
                    chunkCoordinate.X,
                    chunkCoordinate.Y,
                    localX,
                    localY,
                    out int worldX,
                    out int worldY))
            {
                return -1;
            }

            var currentCoordinate = new WorldTileCoordinate(
                chunkCoordinate.Layer,
                worldX,
                worldY);
            bool isWall = terrainCell.WallBlockRuntimeId.IsValid;
            bool isLiquid = terrainCell.LiquidRuntimeId.IsValid;

            if (isWall)
            {
                if (TerrainMaterialProductTileResolver.TryResolveTop(
                        materialPresentationCatalog,
                        materialRuntimeLookup,
                        presentationWorldSeed,
                        currentCoordinate,
                        terrainCell.WallBlockRuntimeId,
                        forWall: true,
                        out TerrainMaterialProductTileSelection wallTop))
                {
                    rockWallSubChunkTileBuffer[destinationIndex] =
                        materialTileCache.GetOrCreate(wallTop);
                }
            }
            else if (!isLiquid && terrainCell.GroundBlockRuntimeId.IsValid)
            {
                if (TerrainMaterialProductTileResolver.TryResolveTop(
                        materialPresentationCatalog,
                        materialRuntimeLookup,
                        presentationWorldSeed,
                        currentCoordinate,
                        terrainCell.GroundBlockRuntimeId,
                        forWall: false,
                        out TerrainMaterialProductTileSelection groundTop))
                {
                    subChunkTileBuffer[destinationIndex] =
                        materialTileCache.GetOrCreate(groundTop);
                }
            }

            bool isDryVoid = TerrainMaterialVerticalProjectionUtility.IsDryVoid(terrainCell);
            dryCliffDeepBlackSubChunkTileBuffer[destinationIndex] = isDryVoid
                ? materialTileCache.GetOrCreateDeepBlackTile()
                : null;

            // Wall前面はGroundや乾燥穴の上にも投影できるため、同じセルでGround崖面より優先します。
            if (TerrainMaterialVerticalProjectionUtility.TryProjectWallFace(
                    terrainNeighborhood,
                    localX,
                    localY,
                    out TerrainMaterialFaceProjection wallProjection)
                && TryCreateFaceSourceCoordinate(
                    chunkCoordinate,
                    wallProjection,
                    out WorldTileCoordinate wallSourceCoordinate)
                && TerrainMaterialProductTileResolver.TryResolveFace(
                    materialPresentationCatalog,
                    materialRuntimeLookup,
                    presentationWorldSeed,
                    wallSourceCoordinate,
                    wallProjection.SourceCell.WallBlockRuntimeId,
                    wallProjection.FaceDepth,
                    forWall: true,
                    out TerrainMaterialProductTileSelection wallFace))
            {
                wallFaceSubChunkTileBuffer[destinationIndex] =
                    materialTileCache.GetOrCreate(wallFace);
                return wallProjection.FaceDepth;
            }

            if (TerrainMaterialVerticalProjectionUtility.TryProjectGroundFace(
                    terrainNeighborhood,
                    localX,
                    localY,
                    out TerrainMaterialFaceProjection groundProjection)
                && TryCreateFaceSourceCoordinate(
                    chunkCoordinate,
                    groundProjection,
                    out WorldTileCoordinate groundSourceCoordinate)
                && TerrainMaterialProductTileResolver.TryResolveFace(
                    materialPresentationCatalog,
                    materialRuntimeLookup,
                    presentationWorldSeed,
                    groundSourceCoordinate,
                    groundProjection.SourceCell.GroundBlockRuntimeId,
                    groundProjection.FaceDepth,
                    forWall: false,
                    out TerrainMaterialProductTileSelection groundFace))
            {
                groundFaceSubChunkTileBuffer[destinationIndex] =
                    materialTileCache.GetOrCreate(groundFace);
            }

            return -1;
        }

        /// <summary>
        /// 1セル分の製品Liquid深度、直下床、水際7bit、垂直面反射Proxyを再利用Tileへ分類します。
        /// </summary>
        /// <param name="chunkCoordinate">絶対座標へ戻す対象論理チャンクです。</param>
        /// <param name="localX">チャンク内の表示対象Xです。</param>
        /// <param name="localY">チャンク内の表示対象Yです。</param>
        /// <param name="destinationIndex">256要素の再利用配列へ書く位置です。</param>
        /// <param name="terrainCell">現在表示セルの完成済み地形です。</param>
        /// <param name="terrainNeighborhood">水際から最大6段奥の投影元を含む完成地形近傍です。</param>
        /// <param name="subChunkIndex">反射Meshを更新する0〜15の番号です。</param>
        /// <param name="hasLowerNeighborhood">直下WorldLayerの完成3×3近傍を借用できた場合はtrueです。</param>
        /// <param name="lowerNeighborhood">直下床プレビューへ使う完成地形です。未準備時は読みません。</param>
        private void ClassifyProductLiquidTiles(
            ChunkCoordinate chunkCoordinate,
            int subChunkIndex,
            int localX,
            int localY,
            int destinationIndex,
            GeneratedTerrainCell terrainCell,
            in CompletedChunkTerrainNeighborhood terrainNeighborhood,
            bool hasLowerNeighborhood,
            in CompletedChunkTerrainNeighborhood lowerNeighborhood)
        {
            TerrainLiquidVisualKind kind = TerrainLiquidPresentationUtility.Classify(
                terrainCell,
                terrainRuntimeIds);
            if (kind == TerrainLiquidVisualKind.None
                || !liquidPresentationCatalog.TryGetDefinition(
                    kind,
                    out TerrainLiquidPresentationDefinition definition))
            {
                return;
            }

            TerrainLiquidEdgeMask edgeMask = TerrainLiquidPresentationUtility.SelectEdgeMask(
                terrainNeighborhood,
                localX,
                localY);
            liquidEdgeWaveSubChunkTileBuffer[destinationIndex] =
                liquidTileCache.GetOrCreateShoreline(definition.ShorelineColor, edgeMask);

            // マグマでも水面より上のGroundの縁とWall前面は必要です。液面より下の断面・青深度・反射だけを省きます。
            if (kind == TerrainLiquidVisualKind.Magma)
            {
                groundFaceSubChunkTileBuffer[destinationIndex] = null;
                // Wall前面は+2Y補正後に根元より上へ描かれます。書込先セルがマグマでも消してはいけません。
                if (TerrainLiquidPresentationUtility.TryProjectGroundFaceAtLiquidBank(
                        terrainNeighborhood, localX, localY, terrainRuntimeIds,
                        out TerrainLiquidFaceProjection bankProjection))
                {
                    submergedGroundFaceSubChunkTileBuffer[destinationIndex] =
                        SelectProductLiquidGroundFace(chunkCoordinate, bankProjection);
                    waterDepthSubChunkColorBuffer[destinationIndex] = TerrainWaterRenderUtility.EncodeMagmaBankFace();
                }
                return;
            }

            int depthStage = TerrainLiquidPresentationUtility.GetClampedWaterDepth(
                terrainNeighborhood,
                localX,
                localY,
                terrainRuntimeIds);
            if (depthStage < 0)
            {
                return;
            }

            bool hasUnderwaterFloor = TryGetUnderwaterFloor(
                hasLowerNeighborhood,
                lowerNeighborhood,
                localX,
                localY,
                out GeneratedTerrainCell lowerCell);
            // 水色は一定の基底色、断面はこの列の実際の岸からの距離だけで合成します。
            // 連結した水でも別の岸の投影距離は共有せず、隣列へ余計な断面を作りません。
            waterDepthSubChunkColorBuffer[destinationIndex] = TerrainWaterRenderUtility.EncodeCell(
                kind, depthStage, hasUnderwaterFloor, depthStage == 0);
            TileBase depthTile = liquidTileCache.GetOrCreateDepthOverlay();
            // 模様の位置・濃度はShaderの責務です。CPUは実際の水域と奥岸クリップだけを渡します。
            waterSurfacePatternSubChunkTileBuffer[destinationIndex] = depthTile;
            groundFaceSubChunkTileBuffer[destinationIndex] = null;
            if (hasUnderwaterFloor)
            {
                liquidDepthWithFloorSubChunkTileBuffer[destinationIndex] = depthTile;
            }
            else
            {
                liquidDepthWithoutFloorSubChunkTileBuffer[destinationIndex] = depthTile;
            }

            // 4段目以降は直下1層のGround上面へ切り替えます。下層Entityが未準備なら透視せず床なし規則を使います。
            if (hasUnderwaterFloor && depthStage >= TerrainMaterialVerticalProjectionUtility.GroundFaceDepth)
            {
                if (WorldGrid.TryToWorldTileCoordinate(
                        chunkCoordinate.X,
                        chunkCoordinate.Y,
                        localX,
                        localY,
                        out int floorWorldX,
                        out int floorWorldY)
                    && TryGetLowerLayer(chunkCoordinate.Layer, out WorldLayer lowerLayer)
                    && TerrainMaterialProductTileResolver.TryResolveTop(
                        materialPresentationCatalog,
                        materialRuntimeLookup,
                        presentationWorldSeed,
                        new WorldTileCoordinate(lowerLayer, floorWorldX, floorWorldY),
                        lowerCell.GroundBlockRuntimeId,
                        forWall: false,
                        out TerrainMaterialProductTileSelection floorSelection))
                {
                    underwaterFloorPreviewSubChunkTileBuffer[destinationIndex] =
                        materialTileCache.GetOrCreate(floorSelection);
                }
            }
            else if (TerrainLiquidPresentationUtility.TryProjectGroundFaceThroughWater(
                    terrainNeighborhood,
                    localX,
                    localY,
                    terrainRuntimeIds,
                    out TerrainLiquidFaceProjection faceProjection))
            {
                // 実際の岸の真下だけへ断面を置き、Shaderで1から水の被覆率を引いた値を可視Alphaにします。
                submergedGroundFaceSubChunkTileBuffer[destinationIndex] =
                    SelectProductLiquidGroundFace(chunkCoordinate, faceProjection);
            }

            // 反射元の幅から左右2pxへ輪郭を動かせるよう、受け手から左右1列のWallも読みます。
            // 各断片はこの水セル内だけに切り抜き、余白で陸地・別セルの3pxの縁を覆いません。
            float bankOffset = TerrainLiquidPresentationUtility.SurfaceClipPixels
                / (float)TerrainLiquidPresentationUtility.PixelsPerCell;
            var waterBounds = new Rect(localX, localY, 1f, depthStage == 0 ? 1f - bankOffset : 1f);
            float waterSurfaceY = localY + depthStage + 1f - bankOffset;
            for (int sourceOffsetX = -1; sourceOffsetX <= 1; sourceOffsetX++)
            {
                int sourceX = localX + sourceOffsetX;
                if (!TerrainLiquidPresentationUtility.TryFindWallReflectionSource(
                    terrainNeighborhood,
                    sourceX,
                    localY,
                    terrainRuntimeIds,
                    kind,
                    out TerrainMaterialFaceProjection reflectionProjection)
                || !TryCreateFaceSourceCoordinate(
                    chunkCoordinate,
                    reflectionProjection,
                    out WorldTileCoordinate reflectionSourceCoordinate)) continue;
                // 反射元の接地点は動かさず、2枚の垂直画像をそれぞれ実水面で切り抜きます。
                float contactY = reflectionProjection.SourceLocalY;
                float planeY = contactY - bankOffset;
                for (int stage = 0; stage < TerrainLiquidPresentationUtility.MaximumReflectionDepth; stage++)
                {
                    if (!TerrainWaterReflectionGeometry.TryProjectToWater(new Rect(sourceX, contactY + stage, 1f, 1f),
                        contactY, planeY, waterBounds, TerrainLiquidPresentationUtility.ReflectionVerticalCompression,
                        TerrainLiquidPresentationUtility.ReflectionVisibleDepthPixels, out TerrainWaterReflectionSlice slice,
                        TerrainLiquidPresentationUtility.ReflectionHorizontalPaddingPixels)
                        || !TerrainMaterialProductTileResolver.TryResolveFace(materialPresentationCatalog,
                            materialRuntimeLookup, presentationWorldSeed, reflectionSourceCoordinate,
                            reflectionProjection.SourceCell.WallBlockRuntimeId,
                            TerrainLiquidPresentationUtility.GetReflectedWallFaceDepth(stage), forWall: true,
                            out TerrainMaterialProductTileSelection reflectionFace)) continue;

                    if (!reflectionAtlasSprites.TryGetValue(reflectionFace.Sprite, out Sprite packed))
                    {
                        packed = waterReflectionAtlas.GetSprite(reflectionFace.Sprite.name);
                        if (packed == null) throw new System.InvalidOperationException("製品Atlasに反射元Spriteがありません。");
                        reflectionAtlasSprites.Add(reflectionFace.Sprite, packed);
                    }
                    if (!reflectionMeshes.TryGetValue(chunkCoordinate, out TerrainWaterReflectionMesh mesh))
                    {
                        mesh = new TerrainWaterReflectionMesh(waterReflectionTilemap.transform.parent);
                        mesh.Reposition(CalculateRockWallShadowMinimumCell(chunkCoordinate, presentedDisplayOrigin));
                        reflectionMeshes.Add(chunkCoordinate, mesh);
                    }
                    mesh.Append(subChunkIndex, packed, slice, waterReflectionTilemap.GetComponent<Renderer>(), waterSurfaceY);
                }
            }
        }

        /// <summary>水中断面とマグマ岸で同じGround材質・境界完成Spriteを選び、白Tintの共有Tileへ変換します。</summary>
        /// <param name="coordinate">表示先の論理チャンクです。投影元の絶対座標を解決します。</param>
        /// <param name="projection">完成近傍から求めた投影元Groundと段数です。</param>
        /// <returns>成功時はキャッシュが所有するTile、不正な参照や未登録材質ならnullです。</returns>
        private TileBase SelectProductLiquidGroundFace(
            ChunkCoordinate coordinate, TerrainLiquidFaceProjection projection)
        {
            if (!TryCreateLiquidFaceSourceCoordinate(coordinate, projection, out WorldTileCoordinate sourceCoordinate)
                || !TerrainMaterialProductTileResolver.TryResolveSubmergedGroundFace(
                    materialPresentationCatalog, materialRuntimeLookup, presentationWorldSeed, sourceCoordinate,
                    projection.SourceCell.GroundBlockRuntimeId, projection.FaceDepth,
                    out TerrainMaterialProductTileSelection selection))
            {
                return null;
            }

            // 表示先セルへ距離やマグマ印を別送するため、素材とTileの共有キーへ混ぜません。
            return materialTileCache.GetOrCreate(selection);
        }

        /// <summary>製品Liquidカタログから、通常セルまたは岸側3pxクリップ用の共有Surface Tileを取得します。</summary>
        /// <param name="kind">ECS Runtime IDから分類済みの液体種別です。</param>
        /// <param name="terrainNeighborhood">岸へ接する画面奥側セルか判定する完成地形近傍です。</param>
        /// <param name="localX">中心チャンク基準の現在セルXです。</param>
        /// <param name="localY">中心チャンク基準の現在セルYです。</param>
        /// <returns>上端3pxクリップMaterialで描く共有Tile、または通常地形ならnullです。</returns>
        private TileBase SelectProductLiquidSurfaceTile(
            TerrainLiquidVisualKind kind,
            in CompletedChunkTerrainNeighborhood terrainNeighborhood,
            int localX,
            int localY)
        {
            if (liquidPresentationCatalog == null
                || liquidTileCache == null
                || !liquidPresentationCatalog.TryGetDefinition(
                    kind,
                    out TerrainLiquidPresentationDefinition definition))
            {
                return null;
            }

            bool clipSurfaceTop = TerrainLiquidPresentationUtility.ShouldClipSurfaceTop(
                terrainNeighborhood,
                localX,
                localY);
            return liquidTileCache.GetOrCreateSurface(
                definition.SurfaceTile,
                clipSurfaceTop);
        }

        /// <summary>液体Face投影元の中心基準ローカル座標を、レイヤー付き絶対座標へ安全に変換します。</summary>
        /// <param name="chunkCoordinate">表示対象の中心論理チャンクです。</param>
        /// <param name="projection">水際奥にあるGround投影元です。</param>
        /// <param name="sourceCoordinate">成功時に決定論的Sprite選択へ使う絶対座標を受け取ります。</param>
        /// <returns>int座標域の内側ならtrueです。</returns>
        private static bool TryCreateLiquidFaceSourceCoordinate(
            ChunkCoordinate chunkCoordinate,
            TerrainLiquidFaceProjection projection,
            out WorldTileCoordinate sourceCoordinate)
        {
            sourceCoordinate = default;
            if (!WorldGrid.TryToWorldTileCoordinate(
                    chunkCoordinate.X,
                    chunkCoordinate.Y,
                    projection.SourceLocalX,
                    projection.SourceLocalY,
                    out int worldX,
                    out int worldY))
            {
                return false;
            }

            sourceCoordinate = new WorldTileCoordinate(
                chunkCoordinate.Layer,
                worldX,
                worldY);
            return true;
        }

        /// <summary>現在表示層の直下1層を、固定3層契約から明示的に求めます。</summary>
        /// <param name="currentLayer">現在Gameビューへ表示しているSurfaceまたはUnderwaterです。</param>
        /// <param name="lowerLayer">成功時にUnderwaterまたはDeepSeaを受け取ります。</param>
        /// <returns>直下層を持つ場合はtrue、DeepSeaまたはNoneならfalseです。</returns>
        private static bool TryGetLowerLayer(WorldLayer currentLayer, out WorldLayer lowerLayer)
        {
            if (currentLayer == WorldLayer.Surface)
            {
                lowerLayer = WorldLayer.Underwater;
                return true;
            }

            if (currentLayer == WorldLayer.Underwater)
            {
                lowerLayer = WorldLayer.DeepSea;
                return true;
            }

            lowerLayer = WorldLayer.None;
            return false;
        }

        /// <summary>チャンク外を含む投影元ローカル座標を、レイヤー付き絶対座標へ安全に変換します。</summary>
        /// <param name="chunkCoordinate">表示対象の中心論理チャンクです。</param>
        /// <param name="projection">中心基準の投影元ローカル座標です。</param>
        /// <param name="sourceCoordinate">成功時に決定論的Sprite選択へ使う絶対座標を受け取ります。</param>
        /// <returns>int座標域の内側ならtrueです。</returns>
        private static bool TryCreateFaceSourceCoordinate(
            ChunkCoordinate chunkCoordinate,
            TerrainMaterialFaceProjection projection,
            out WorldTileCoordinate sourceCoordinate)
        {
            sourceCoordinate = default;
            if (!WorldGrid.TryToWorldTileCoordinate(
                    chunkCoordinate.X,
                    chunkCoordinate.Y,
                    projection.SourceLocalX,
                    projection.SourceLocalY,
                    out int worldX,
                    out int worldY))
            {
                return false;
            }

            sourceCoordinate = new WorldTileCoordinate(
                chunkCoordinate.Layer,
                worldX,
                worldY);
            return true;
        }

        /// <summary>
        /// 1レイヤーの分類済み配列を反映します。既知の空領域へのnull書込だけを省きます。
        /// </summary>
        /// <param name="coordinate">対象チャンクの絶対座標です。</param>
        /// <param name="template">対象レイヤーのScene見本です。未設定なら反映しません。</param>
        /// <param name="bounds">チャンク内の16×16セル範囲です。</param>
        /// <param name="tiles">全256セルの分類済み配列です。呼び出し中だけ参照します。</param>
        /// <param name="colors">水種・床・投影段数などのセルデータ色です。通常レイヤーはnullです。</param>
        /// <returns>一括書込を実行した場合はtrue、省略した場合はfalseです。</returns>
        private bool WriteSubChunkLayer(ChunkCoordinate coordinate, Tilemap template, BoundsInt bounds, TileBase[] tiles, Color[] colors = null)
        {
            if (template == null) return false;
            GetTilemapForChunk(coordinate, template, true);
            if (colors != null)
            {
                // Spriteとデータを別々に更新せず、同じDirtyサブチャンクの一括書込で揃えます。
                for (int index = 0; index < tiles.Length; index++)
                {
                    var position = new Vector3Int(bounds.xMin + index % bounds.size.x,
                        bounds.yMin + index / bounds.size.x, bounds.zMin);
                    waterTileChangeBuffer[index] = new TileChangeData(position, tiles[index], colors[index], Matrix4x4.identity);
                }
            }
            // 空セル履歴は表示単位が所有し、プール返却時に消去します。Tilemapへセルごとの再確保は行いません。
            return chunkLocalPresentations[coordinate].WriteSubChunk(template, bounds, tiles,
                colors == null ? null : waterTileChangeBuffer);
        }

        /// <summary>通常予算または全消去から呼び、チャンク内の1部分を消去します。最初の部分で影・反射・照明も外します。</summary>
        /// <param name="coordinate">表示を所有する絶対チャンク座標です。</param>
        /// <param name="subChunkIndex">0〜15のサブチャンク番号です。</param>
        private void ClearSubChunk(ChunkCoordinate coordinate, int subChunkIndex)
        {
            if (subChunkIndex == 0)
            {
                magmaShoreLighting?.RemoveChunk(coordinate);
                if (reflectionMeshes.Remove(coordinate, out TerrainWaterReflectionMesh reflection)) reflection.Dispose();
                DestroyRockWallShadow(coordinate);
                ReleaseWallDynamicShadowPresentation(coordinate);
            }
            using (SubChunkTilemapClearProfilerMarker.Auto())
            {
                if (chunkLocalPresentations.TryGetValue(coordinate, out ChunkLocalTilemapPresentation presentation))
                {
                    // 最初に非表示にし、分割消去中の断片を画面へ残さないようにします。
                    presentation.ClearSubChunk(CreateSubChunkBounds(coordinate, subChunkIndex), emptySubChunkTileBuffer);
                }
            }
        }

        /// <summary>無効化・World切替時に、同じ論理チャンクの全表示を同期消去します。</summary>
        /// <param name="coordinate">消去する絶対チャンク座標です。</param>
        private void ClearWholeChunkImmediately(ChunkCoordinate coordinate)
        {
            for (int index = 0; index < WorldGridConstants.SubChunksPerChunk; index++)
            {
                ClearSubChunk(coordinate, index);
            }
        }

        /// <summary>
        /// 現在のWorldから描画した可能性がある全チャンクを即時消去し、表示キャッシュと岩壁影を初期状態へ戻します。
        /// Component無効化とDefault World切り替えの両方から呼ばれ、TilemapをSimulation状態として残さないために使用します。
        /// </summary>
        private void ClearAllPresentedChunksImmediately()
        {
            magmaShoreLighting?.ClearCandidates();
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

            DestroyAllRockWallShadows();
            DestroyAllWallDynamicShadowPresentations();
            // 即時消去は設定変更・テレポート・World切替に限ります。空きプールも破棄して設定やWorldを持ち越しません。
            DestroyChunkLocalPresentations();
            ResetPresentationCaches();
        }

        /// <summary>表示原点から独立した、チャンク内0〜63セルの部分範囲を返します。</summary>
        /// <param name="coordinate">呼び出し元が処理している絶対チャンク座標です。セル範囲には混ぜません。</param>
        /// <param name="subChunkIndex">行優先の0〜15部分番号です。</param>
        /// <returns>16×16×1のローカルセル範囲です。</returns>
        private BoundsInt CreateSubChunkBounds(ChunkCoordinate coordinate, int subChunkIndex)
        {
            return new BoundsInt(
                (subChunkIndex % WorldGridConstants.SubChunksPerAxis) * WorldGridConstants.SubChunkSize,
                (subChunkIndex / WorldGridConstants.SubChunksPerAxis) * WorldGridConstants.SubChunkSize,
                0, WorldGridConstants.SubChunkSize, WorldGridConstants.SubChunkSize, 1);
        }

        /// <summary>
        /// 新しい表示単位を作るときだけ、Scene上の上面、製品断面、製品Liquidレイヤーを固定順で取得します。毎フレームの反映・消去では配列を作りません。
        /// </summary>
        /// <returns>表示設定の見本です。セル内容はコピーしません。</returns>
        private Tilemap[] GetTilemapTemplates()
        {
            return new[] { targetTilemap, liquidBaseTilemap, resourceTilemap, resourceBehindRockWallTilemap,
                magmaSurfacePatternTilemap, rockWallTilemap, groundFaceTilemap, wallFaceTilemap,
                wallContactShadowLeftTilemap, wallContactShadowRightTilemap,
                wallFoldShadowTopTilemap, wallFoldShadowBottomTilemap,
                dryCliffDeepBlackTilemap, liquidDepthWithFloorTilemap,
                liquidDepthWithoutFloorTilemap, underwaterFloorPreviewTilemap, submergedGroundFaceTilemap,
                waterSurfacePatternTilemap, liquidEdgeWaveTilemap, rockWallResourceTilemap };
        }

        /// <summary>
        /// チャンク内の書き込み先を取得します。新方式の初回反映だけ空き表示を借ります。
        /// </summary>
        /// <param name="coordinate">対象の絶対チャンク座標です。</param>
        /// <param name="template">Scene上のレイヤー設定見本です。</param>
        /// <param name="createIfMissing">未割当チャンクへ表示単位を貸し出してよい場合はtrueです。</param>
        /// <returns>チャンク専用Tilemapです。未割当ならnullです。</returns>
        private Tilemap GetTilemapForChunk(ChunkCoordinate coordinate, Tilemap template, bool createIfMissing)
        {
            if (template == null)
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
            DestroyReflectionMeshes();
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
        /// 論理チャンク用の動的影表示を取得し、再利用時も現在のFloating Origin位置へルートを合わせます。
        /// </summary>
        /// <param name="coordinate">影を表示する論理チャンク座標です。</param>
        /// <returns>現在チャンクへ貸し出した動的影表示です。</returns>
        private TerrainWallDynamicShadowChunkPresentation GetOrCreateWallDynamicShadowPresentation(
            ChunkCoordinate coordinate)
        {
            if (!wallDynamicShadowByCoordinate.TryGetValue(
                    coordinate,
                    out TerrainWallDynamicShadowChunkPresentation presentation))
            {
                EnsureWallDynamicShadowUnitSprite();
                presentation = pooledWallDynamicShadowPresentations.Count > 0
                    ? pooledWallDynamicShadowPresentations.Pop()
                    : new TerrainWallDynamicShadowChunkPresentation(
                        targetTilemap.transform,
                        generatedWallDynamicShadowUnitSprite,
                        targetTilemap.gameObject.layer);
                wallDynamicShadowByCoordinate.Add(coordinate, presentation);
            }

            presentation.Reposition(
                targetTilemap.CellToLocal(
                    CalculateRockWallShadowMinimumCell(coordinate, presentedDisplayOrigin)));
            presentation.Root.SetActive(true);
            return presentation;
        }

        /// <summary>
        /// 画面外になったチャンクの動的影を非表示にし、上限内なら次のチャンクへ再利用します。
        /// </summary>
        /// <param name="coordinate">貸し出しを終了する論理チャンク座標です。</param>
        private void ReleaseWallDynamicShadowPresentation(ChunkCoordinate coordinate)
        {
            if (!wallDynamicShadowByCoordinate.Remove(
                    coordinate,
                    out TerrainWallDynamicShadowChunkPresentation presentation))
            {
                return;
            }

            presentation.Clear();
            if (pooledWallDynamicShadowPresentations.Count
                < Mathf.Max(0, maximumPooledChunkPresentations))
            {
                pooledWallDynamicShadowPresentations.Push(presentation);
            }
            else
            {
                presentation.Destroy();
            }
        }

        /// <summary>
        /// Component無効化、World切替、破棄時に、貸し出し中とプール内の動的影GameObjectをすべて破棄します。
        /// </summary>
        private void DestroyAllWallDynamicShadowPresentations()
        {
            foreach (TerrainWallDynamicShadowChunkPresentation presentation in
                     wallDynamicShadowByCoordinate.Values)
            {
                presentation.Destroy();
            }

            wallDynamicShadowByCoordinate.Clear();
            while (pooledWallDynamicShadowPresentations.Count > 0)
            {
                pooledWallDynamicShadowPresentations.Pop().Destroy();
            }
        }

        /// <summary>変更差分適用済み近傍の4方向を調べ、資源を残す露出面と内部を区別します。</summary>
        /// <param name="localX">中心セルの水平ローカル座標です。</param>
        /// <param name="localY">中心セルの垂直ローカル座標です。</param>
        /// <param name="tile">中心セルの現在の生成地形です。</param>
        /// <param name="neighborhood">中心と周囲8チャンクの有効地形を同じ規則で解決する近傍です。</param>
        /// <returns>非岩壁ならNone、非岩壁に接する岩壁ならExposed、それ以外はInteriorです。</returns>
        private static RockWallAppearance ClassifyRockWallAppearance(
            int localX,
            int localY,
            GeneratedTerrainCell terrainCell,
            in CompletedChunkTerrainNeighborhood neighborhood)
        {
            if (!terrainCell.WallBlockRuntimeId.IsValid)
            {
                return RockWallAppearance.None;
            }

            // 影の連続濃度は別処理です。ここでは資源を隠すかどうかだけを従来と同じ上下左右で決めます。
            if (!neighborhood.TryGetTile(localX - 1, localY, out GeneratedTerrainCell west)
                || !neighborhood.TryGetTile(localX + 1, localY, out GeneratedTerrainCell east)
                || !neighborhood.TryGetTile(localX, localY - 1, out GeneratedTerrainCell south)
                || !neighborhood.TryGetTile(localX, localY + 1, out GeneratedTerrainCell north))
            {
                throw new System.InvalidOperationException(
                    $"完成済み地形近傍から岩壁の4近傍を取得できません: Local=({localX}, {localY})");
            }

            bool touchesOpenTile =
                !west.WallBlockRuntimeId.IsValid
                || !east.WallBlockRuntimeId.IsValid
                || !south.WallBlockRuntimeId.IsValid
                || !north.WallBlockRuntimeId.IsValid;
            return touchesOpenTile ? RockWallAppearance.Exposed : RockWallAppearance.Interior;
        }

        /// <summary>
        /// 生成済みチャンクと探索余白の岩壁マスクから距離場を作り、頂点アルファが連続する黒い影Meshを更新します。
        /// </summary>
        /// <param name="coordinate">影を重ねる論理チャンク座標です。</param>
        /// <param name="neighborhood">中心と周囲8チャンクの完成済み地形Bufferです。</param>
        private void BuildRockWallShadowMesh(
            ChunkCoordinate coordinate,
            in CompletedChunkTerrainNeighborhood neighborhood)
        {
            BuildRockWallShadowMeshForSubChunks(coordinate, neighborhood, ushort.MaxValue);
        }

        /// <summary>完成済み部分の影だけを同じ距離場計算で公開します。参照半径を含め入力が揃ったMaskだけを受け取ります。</summary>
        /// <param name="coordinate">中心座標です。</param>
        /// <param name="neighborhood">存在する完成入力だけを含む借用近傍です。</param>
        /// <param name="visibleSubChunks">地形と同時に公開する16bitです。</param>
        private void BuildRockWallShadowMeshForSubChunks(ChunkCoordinate coordinate,
            in CompletedChunkTerrainNeighborhood neighborhood, ushort visibleSubChunks)
        {
            // using宣言は早期returnを含むメソッド終了時に自動で閉じ、影構築全体を漏れなく計測します。
            using ProfilerMarker.AutoScope buildScope =
                RockWallShadowBuildProfilerMarker.Auto();
            using (RockWallShadowMaskProfilerMarker.Auto())
            {
                FillRockWallShadowMask(neighborhood);
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

                // 製品Wallの根元から2タイル持ち上げ、上面と内部影をそろえます。
                presentation.GameObject.transform.localPosition = meshOrigin
                    + (Vector3.up * GetRockWallVisualElevationInCells());
                presentation.Mesh.Clear();
                presentation.Mesh.vertices = rockWallShadowVertexBuffer;
                presentation.Mesh.colors32 = rockWallShadowColorBuffer;
                if (visibleSubChunks == ushort.MaxValue) presentation.Mesh.triangles = rockWallShadowTriangleBuffer;
                else
                {
                    // 未完成領域にも作業用頂点はありますが、必要入力が揃った部分の三角形だけを描画へ渡します。
                    rockWallShadowVisibleTriangles.Clear();
                    for (int y = 0; y < WorldGridConstants.LogicalChunkSize; y++)
                        for (int x = 0; x < WorldGridConstants.LogicalChunkSize; x++)
                        {
                            int subIndex = y / WorldGridConstants.SubChunkSize * WorldGridConstants.SubChunksPerAxis + x / WorldGridConstants.SubChunkSize;
                            if ((visibleSubChunks & (1 << subIndex)) == 0) continue;
                            int first = (y * WorldGridConstants.LogicalChunkSize + x) * 6;
                            for (int triangle = 0; triangle < 6; triangle++) rockWallShadowVisibleTriangles.Add(rockWallShadowTriangleBuffer[first + triangle]);
                        }
                    presentation.Mesh.SetTriangles(rockWallShadowVisibleTriangles, 0);
                }
                presentation.Mesh.RecalculateBounds();
            }
        }

        /// <summary>
        /// 自動Floating Origin前の岩壁影を、新しい表示原点からの相対位置へ移します。
        /// </summary>
        /// <param name="currentOrigin">Cameraが変更した新しい表示原点です。</param>
        private void RepositionRockWallShadows(DisplayOrigin currentOrigin)
        {
            using (RockWallShadowRepositionProfilerMarker.Auto())
            {
                foreach (KeyValuePair<ChunkCoordinate, RockWallShadowChunkPresentation> pair in
                         rockWallShadowByCoordinate)
                {
                    // Mesh頂点はチャンク左下を原点とするため、GameObject位置だけを変えれば形状と色をそのまま使えます。
                    pair.Value.GameObject.transform.localPosition =
                        targetTilemap.CellToLocal(
                            CalculateRockWallShadowMinimumCell(pair.Key, currentOrigin))
                        + (Vector3.up * GetRockWallVisualElevationInCells());
                }
            }
        }

        /// <summary>指定セルの直下1層に、Wallで塞がれていないGround上面があるか判定します。</summary>
        /// <param name="hasLowerNeighborhood">直下WorldLayerの近傍を利用できる場合はtrueです。</param>
        /// <param name="lowerNeighborhood">直下WorldLayerの完成済み地形近傍です。</param>
        /// <param name="localX">中心チャンク基準のX座標です。</param>
        /// <param name="localY">中心チャンク基準のY座標です。</param>
        /// <param name="lowerCell">床がある場合に、その材質解決へ使う直下セルを受け取ります。</param>
        /// <returns>直下セルがGroundを持ち、Wallを持たない場合はtrueです。</returns>
        private static bool TryGetUnderwaterFloor(
            bool hasLowerNeighborhood,
            in CompletedChunkTerrainNeighborhood lowerNeighborhood,
            int localX,
            int localY,
            out GeneratedTerrainCell lowerCell)
        {
            lowerCell = default;
            return hasLowerNeighborhood
                && lowerNeighborhood.TryGetTile(localX, localY, out lowerCell)
                && lowerCell.GroundBlockRuntimeId.IsValid
                && !lowerCell.WallBlockRuntimeId.IsValid;
        }

        /// <summary>岩壁内部影を製品Wall上面と同じ位置へ持ち上げる、固定の表示高を返します。</summary>
        /// <returns>論理Wall根元から画面奥へ2タイルです。</returns>
        private float GetRockWallVisualElevationInCells()
        {
            return TerrainMaterialVerticalProjectionUtility.WallTopOffsetInTiles;
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
        /// 影の探索半径を含むチャンク周辺を同期判定し、自然地形と疎な変更差分を同じ影マスクへ反映します。
        /// </summary>
        /// <param name="neighborhood">中心と周囲8チャンクの完成済み地形Bufferです。</param>
        private void FillRockWallShadowMask(
            in CompletedChunkTerrainNeighborhood neighborhood)
        {
            // 近傍内のAsNativeArrayはBufferを複製しない借用です。この同期処理中はECSの構造変更を行いません。
            RockWallAppearanceUtility.FillShadowMask(neighborhood, rockWallShadowMaskBuffer);
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
                terrainCatalogQuery.Dispose();
                terrainDeltaQuery.Dispose();
                terrainMaterialCatalogQuery.Dispose();
                worldGenerationSettingsQuery.Dispose();
            }

            queriesAreCreated = false;
            presentationWorld = null;
            ResetMaterialPresentationState(disposeTileCache: true);
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
            completedChunkEntities.Clear();
            terrainDeltaOwnerEntities.Clear();
            terrainDeltaFingerprints.Clear();
            displayedTerrainFingerprints.Clear();
            clearQueue.Clear();
            coordinateScratch.Clear();
        }

        /// <summary>
        /// Camera側で検出した表示原点変更の種類です。
        /// 変更理由により、位置だけ変更できるか、即時消去が必要かを区別します。
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
            /// <summary>GeneratedTerrainCell Bufferを読み取る現在のチャンクEntityです。</summary>
            public Entity Entity;

            /// <summary>
            /// 中心Entity寿命・全近傍のTerrainCellDelta・直下層から作った部分表示入力の指紋です。
            /// 反映途中に入力が変わった場合、異なる時点のサブチャンクを混在させず先頭から作り直すために保持します。
            /// </summary>
            public readonly ulong TerrainFingerprint;

            /// <summary>反映済みサブチャンク数です。範囲は0〜16です。具体的な反映位置はAppliedSubChunkMaskで保持します。</summary>
            public int NextSubChunkIndex;

            /// <summary>反映済みの16サブチャンクを表すMaskです。到着順によらず同じ完成入力だけを表示します。</summary>
            public ushort AppliedSubChunkMask;
            /// <summary>反映済み部分が実際に読んだ近傍の9bitです。新規到着と寿命変更を区別します。</summary>
            public ushort UsedChunkMask;
            /// <summary>使用済み近傍EntityのIndex・Versionです。チャンク進捗の寿命中だけ保持します。</summary>
            public readonly Entity[] UsedEntities = new Entity[9];

            /// <summary>
            /// 新しい反映作業を先頭サブチャンクから開始します。
            /// </summary>
            /// <param name="entity">生成済みタイルを所有するEntityです。</param>
            /// <param name="terrainFingerprint">反映開始時点の地形差分入力を識別する決定論的な値です。</param>
            public ChunkApplyProgress(Entity entity, ulong terrainFingerprint)
            {
                Entity = entity;
                TerrainFingerprint = terrainFingerprint;
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

    }
}
