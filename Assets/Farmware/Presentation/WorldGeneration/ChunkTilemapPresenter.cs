// このファイルは、ECSで生成済みの地形・資源チャンクデータを16×16サブチャンク単位でTilemapへ反映・消去します。
// Tilemapは表示専用キャッシュであり、地形はGeneratedTile、資源はGeneratedResourceTileをゲーム状態の正として参照します。

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// カメラに見える生成済みチャンクだけを、共有されたフレーム予算内でTilemapへ反映します。
    /// 画面外またはアンロード済みチャンクも同じ単位で消去し、4096タイル一括更新による停止を避けます。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChunkTilemapPresenter : MonoBehaviour
    {
        // ProfilerでTilemapの収集・反映・消去に使ったCPU時間をまとめて確認する計測区間です。
        private static readonly ProfilerMarker PresentationProfilerMarker =
            new ProfilerMarker("Farmware.WorldGeneration.TilemapPresentation");

        // URP 2D Rendererで頂点色と透明度を扱えるShader名です。利用できない環境では標準Sprite Shaderへフォールバックします。
        private const string PrimaryRockWallShadowShaderName =
            "Universal Render Pipeline/2D/Sprite-Unlit-Default";

        // URP固有Shaderが見つからない場合にも、黒い頂点色を透明合成できる標準Sprite Shader名です。
        private const string FallbackRockWallShadowShaderName = "Sprites/Default";

        // 地形だけを描画する専用Tilemapです。このComponentは担当範囲のセルを書き換えます。
        [Tooltip("生成地形を描画する専用Tilemapです。このComponentが表示・消去するため、手描きタイルとは分けてください。")]
        [SerializeField] private Tilemap targetTilemap;

        // 基礎資源アイコンだけを地形の上へ描画する専用Tilemapです。採取状態の正には使用しません。
        [Tooltip("木・石・砂の仮アイコンを描画する専用Tilemapです。地形Tilemapより手前のSorting Orderへ設定してください。")]
        [SerializeField] private Tilemap resourceTilemap;

        // 草原の通常地面へ使用する表示専用Tileです。ゲームルール判定には使いません。
        [Tooltip("草原バイオームの通常地面に表示するTileです。未指定の場合、そのセルは空表示になります。")]
        [SerializeField] private TileBase grasslandTile;

        // 森林の植生または通常地面へ使用する表示専用Tileです。
        [Tooltip("森林バイオームの地面に表示するTileです。未指定の場合、そのセルは空表示になります。")]
        [SerializeField] private TileBase forestTile;

        // 砂漠の砂地または通常地面へ使用する表示専用Tileです。
        [Tooltip("砂漠バイオームの地面に表示するTileです。未指定の場合、そのセルは空表示になります。")]
        [SerializeField] private TileBase desertTile;

        // 岩地の地面へ使用する表示専用Tileです。障害物の岩壁Tileとは別の、建築可能な地面を表します。
        [Tooltip("岩地バイオームの地面に表示するTileです。岩壁とは異なり、通常地面として扱われます。")]
        [SerializeField] private TileBase rocklandTile;

        // 湿地の地面へ使用する表示専用Tileです。水域そのものはfreshWaterTileまたはsaltWaterTileを優先します。
        [Tooltip("湿地バイオームの陸地に表示するTileです。川・池・海のセルには水域Tileが優先されます。")]
        [SerializeField] private TileBase wetlandTile;

        // 雪原の地面へ使用する表示専用Tileです。凍結水面は未実装のため水域表示には使用しません。
        [Tooltip("雪原バイオームの陸地に表示するTileです。現在、水域は凍結させず通常の水域Tileを表示します。")]
        [SerializeField] private TileBase snowfieldTile;

        // 火山の通常地面へ使用する表示専用Tileです。マグマ本体は次の専用Tileを優先します。
        [Tooltip("火山バイオームの陸地に表示するTileです。マグマ地形とは別の基礎地面です。")]
        [SerializeField] private TileBase volcanicTile;

        // GroundType.Magmaへ使用する表示専用Tileで、水域や火山地面とは別のゲームルールを視覚化します。
        [Tooltip("マグマ地形に表示するTileです。建築・通常移動・通常リンクを遮断する判定はECSデータで行います。")]
        [SerializeField] private TileBase magmaTile;

        // WaterSalinity.FreshWaterへ使用する表示専用Tileです。
        [Tooltip("川と池の淡水域に表示するTileです。採水種別はECSデータで判定し、この参照には依存しません。")]
        [SerializeField] private TileBase freshWaterTile;

        // WaterSalinity.SaltWaterへ使用する表示専用Tileです。旧waterTile参照は海水表示として引き継ぎます。
        [FormerlySerializedAs("waterTile")]
        [Tooltip("海の海水域に表示するTileです。採水種別はECSデータで判定し、この参照には依存しません。")]
        [SerializeField] private TileBase saltWaterTile;

        // GroundType.RockWallの全セルへ共通で使用し、内部の暗さは別の連続グラデーションメッシュで後付けします。
        [Tooltip("すべての岩壁に表示する共通Tileです。内部の暗さは別Tileやセル色ではなく影メッシュで後付けし、建築・リンク遮断判定はECSデータで行います。")]
        [SerializeField] private TileBase rockWallTile;

        // GeneratedResourceType.Treeへ使用する透明背景の表示専用Tileです。
        [Tooltip("樹木資源に表示する仮Tileです。資源の種類と存在はECSバッファを正とします。")]
        [SerializeField] private TileBase treeResourceTile;

        // GeneratedResourceType.Stoneへ使用する透明背景の表示専用Tileです。
        [Tooltip("石資源に表示する仮Tileです。資源の種類と存在はECSバッファを正とします。")]
        [SerializeField] private TileBase stoneResourceTile;

        // GeneratedResourceType.Sandへ使用する透明背景の表示専用Tileです。
        [Tooltip("砂資源に表示する仮Tileです。砂地地形そのものとは別の採取候補表示です。")]
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

        // DictionaryとHashSetを走査中に変更しないため、削除対象を一時退避する再利用Listです。
        private readonly List<ChunkCoordinate> coordinateScratch = new List<ChunkCoordinate>();

        // SetTilesBlockへ渡す256要素の再利用配列です。サブチャンクごとのmanaged allocationを防ぎます。
        private TileBase[] subChunkTileBuffer;

        // 消去用のnullだけを持つ256要素配列です。各フレームで作り直しません。
        private TileBase[] emptySubChunkTileBuffer;

        // 資源Tilemapへ渡す256要素の再利用配列です。地形配列と分け、透明セルも同時更新します。
        private TileBase[] resourceSubChunkTileBuffer;

        // 表示済みチャンクへ重ねた影GameObjectとMeshを座標別に保持し、アンロード時に同じ単位で破棄します。
        private readonly Dictionary<ChunkCoordinate, RockWallShadowChunkPresentation>
            rockWallShadowByCoordinate =
                new Dictionary<ChunkCoordinate, RockWallShadowChunkPresentation>();

        // 影距離の探索半径を含む岩壁有無を1チャンク分保持し、各頂点で生成判定を繰り返さない再利用配列です。
        private bool[] rockWallShadowMaskBuffer;

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

        /// <summary>
        /// Editor構築ツールまたはテストから、地形・資源Tilemapと分類ごとの表示Tileをまとめて設定します。
        /// ゲームルールは変更せず、ECSバッファをどのTileBaseで見せるかだけを更新します。
        /// </summary>
        /// <param name="terrainTilemap">生成地形だけを描画する専用Tilemapです。</param>
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
        /// <param name="rockWall">GroundType.RockWallの全セルに表示する共通Tileです。</param>
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
        /// Component読み込み直後にUnityから呼ばれ、16×16タイルの再利用配列を1回だけ確保します。
        /// </summary>
        private void Awake()
        {
            int tileCount = WorldGridConstants.SubChunkSize * WorldGridConstants.SubChunkSize;
            subChunkTileBuffer = new TileBase[tileCount];
            emptySubChunkTileBuffer = new TileBase[tileCount];
            resourceSubChunkTileBuffer = new TileBase[tileCount];
            AllocateRockWallShadowBuffers();
            EnsureFreshWaterFallbackTile();
        }

        /// <summary>
        /// チャンク影の距離場、頂点、色、三角形を組み立てる再利用配列をComponent寿命中に1回だけ確保します。
        /// </summary>
        private void AllocateRockWallShadowBuffers()
        {
            int maskPadding = RockWallAppearanceUtility.ShadowSearchRadiusInTiles + 1;
            int maskSize = WorldGridConstants.LogicalChunkSize + (maskPadding * 2);
            rockWallShadowMaskBuffer = new bool[maskSize * maskSize];

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
        /// MonoBehaviourの各描画フレームで呼ばれ、表示対象の収集と予算分割された反映・消去を実行します。
        /// </summary>
        private void Update()
        {
            using (PresentationProfilerMarker.Auto())
            {
                if (targetTilemap == null || resourceTilemap == null || !TryEnsureQueries())
                {
                    return;
                }

                if (observerQuery.CalculateEntityCount() != 1)
                {
                    return;
                }

                ChunkStreamingObserver observer = observerQuery.GetSingleton<ChunkStreamingObserver>();
                CollectPresentationWork(observer);

                int remainingBudget = GetSubChunkBudget();
                remainingBudget = ProcessClearQueue(remainingBudget);
                ProcessApplyQueue(remainingBudget);
            }
        }

        /// <summary>
        /// Componentが無効化されたときにUnityから呼ばれ、このComponentが表示したタイルだけを即時消去します。
        /// 再有効化時にはActiveなECSデータから再構築できるため、Tilemapを状態保存には使用しません。
        /// </summary>
        private void OnDisable()
        {
            if (targetTilemap != null || resourceTilemap != null)
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

                for (int index = 0; index < coordinateScratch.Count; index++)
                {
                    ClearWholeChunkImmediately(coordinateScratch[index]);
                }
            }

            DestroyAllRockWallShadows();
            ResetPresentationCaches();
        }

        /// <summary>
        /// Component破棄時にUnityから呼ばれ、明示作成したEntityQueryを解放します。
        /// </summary>
        private void OnDestroy()
        {
            DisposeQueries();
            DestroyAllRockWallShadows();

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
            observerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ChunkStreamingObserver>());
            streamingSettingsQuery =
                entityManager.CreateEntityQuery(ComponentType.ReadOnly<ChunkStreamingSettings>());
            generationSettingsQuery =
                entityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldGenerationSettings>());
            queriesAreCreated = true;
            return true;
        }

        /// <summary>
        /// 現在のECSチャンクを調べ、可視チャンクの反映と非表示・消失チャンクの消去を予約します。
        /// </summary>
        /// <param name="observer">カメラが要求する可視チャンク包含矩形です。</param>
        private void CollectPresentationWork(ChunkStreamingObserver observer)
        {
            EntityManager entityManager = presentationWorld.EntityManager;
            using NativeArray<Entity> entities = chunkQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<ChunkCoordinateComponent> coordinates =
                chunkQuery.ToComponentDataArray<ChunkCoordinateComponent>(Allocator.Temp);
            using NativeArray<ChunkGenerationStatus> statuses =
                chunkQuery.ToComponentDataArray<ChunkGenerationStatus>(Allocator.Temp);
            seenCoordinates.Clear();

            // 保持対象チャンクはカメラ周辺だけに制限されています。ここでは表示状態の変化時だけQueueを更新します。
            for (int index = 0; index < entities.Length; index++)
            {
                ChunkCoordinate coordinate = new ChunkCoordinate(coordinates[index].X, coordinates[index].Y);
                ChunkGenerationStatus status = statuses[index];
                seenCoordinates.Add(coordinate);

                bool isVisible = Contains(observer, coordinate);
                bool hasCompleteData = status.Phase == ChunkGenerationPhase.ReadyToApply
                    || status.Phase == ChunkGenerationPhase.Active;

                if (isVisible && hasCompleteData)
                {
                    ScheduleApply(coordinate, entities[index]);
                }
                else
                {
                    CancelApplyAndScheduleClearIfNeeded(coordinate);
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
        /// 未表示の可視チャンクを反映待ちへ追加し、すでに待機中なら最新Entity参照へ更新します。
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
                }
            }

            return remainingBudget;
        }

        /// <summary>
        /// 反映待ちを16×16単位で処理し、1チャンク完了時だけECS状態をActiveへ進めます。
        /// </summary>
        /// <param name="remainingBudget">このフレームで処理できる残りサブチャンク数です。</param>
        private void ProcessApplyQueue(int remainingBudget)
        {
            if (generationSettingsQuery.CalculateEntityCount() != 1)
            {
                // 境界の隣接地形をSimulationと同じ設定で判定できない間は、誤った影を確定せず次フレームへ待ちます。
                return;
            }

            WorldGenerationSettings generationSettings =
                generationSettingsQuery.GetSingleton<WorldGenerationSettings>();
            EntityManager entityManager = presentationWorld.EntityManager;
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
                    return;
                }

                ApplySubChunk(
                    coordinate,
                    progress.NextSubChunkIndex,
                    tiles,
                    resources,
                    generationSettings);
                progress.NextSubChunkIndex++;
                remainingBudget--;

                if (progress.NextSubChunkIndex >= WorldGridConstants.SubChunksPerChunk)
                {
                    // 全4096タイルがそろった時点で1チャンク1Meshを作り、サブチャンクごとのGameObject生成を避けます。
                    BuildRockWallShadowMesh(coordinate, tiles, generationSettings);
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
        }

        /// <summary>
        /// 1つの16×16サブチャンクをDynamic Bufferから再利用配列へコピーし、Tilemapへ一括反映します。
        /// </summary>
        /// <param name="coordinate">対象の論理チャンク座標です。</param>
        /// <param name="subChunkIndex">行優先の0〜15サブチャンク番号です。</param>
        /// <param name="tiles">4096件の生成済みタイルBufferです。</param>
        /// <param name="resources">地形と同じ行優先4096件の生成済み資源Bufferです。</param>
        /// <param name="generationSettings">チャンク外周の隣接岩壁を再生成する設定です。</param>
        private void ApplySubChunk(
            ChunkCoordinate coordinate,
            int subChunkIndex,
            DynamicBuffer<GeneratedTile> tiles,
            DynamicBuffer<GeneratedResourceTile> resources,
            in WorldGenerationSettings generationSettings)
        {
            int subChunkX = subChunkIndex % WorldGridConstants.SubChunksPerAxis;
            int subChunkY = subChunkIndex / WorldGridConstants.SubChunksPerAxis;
            int localStartX = subChunkX * WorldGridConstants.SubChunkSize;
            int localStartY = subChunkY * WorldGridConstants.SubChunkSize;

            for (int localY = 0; localY < WorldGridConstants.SubChunkSize; localY++)
            {
                for (int localX = 0; localX < WorldGridConstants.SubChunkSize; localX++)
                {
                    int sourceIndex = WorldGrid.ToTileIndex(localStartX + localX, localStartY + localY);
                    int destinationIndex = (localY * WorldGridConstants.SubChunkSize) + localX;
                    GeneratedTile tile = tiles[sourceIndex];
                    RockWallAppearance rockWallAppearance = ClassifyRockWallAppearance(
                        coordinate,
                        localStartX + localX,
                        localStartY + localY,
                        tile,
                        tiles,
                        generationSettings);
                    subChunkTileBuffer[destinationIndex] = SelectTile(tile);

                    // 内部影に覆われる資源アイコンだけを隠します。ECSの資源データと露出面の表示は維持します。
                    resourceSubChunkTileBuffer[destinationIndex] =
                        RockWallAppearanceUtility.ShouldDisplayResource(rockWallAppearance)
                            ? SelectResourceTile(resources[sourceIndex])
                            : null;
                }
            }

            BoundsInt bounds = CreateSubChunkBounds(coordinate, subChunkIndex);
            targetTilemap.SetTilesBlock(bounds, subChunkTileBuffer);
            resourceTilemap.SetTilesBlock(bounds, resourceSubChunkTileBuffer);
        }

        /// <summary>
        /// 1つの16×16サブチャンクをnull配列で上書きし、表示オブジェクトだけを消去します。
        /// </summary>
        /// <param name="coordinate">対象の論理チャンク座標です。</param>
        /// <param name="subChunkIndex">行優先の0〜15サブチャンク番号です。</param>
        private void ClearSubChunk(ChunkCoordinate coordinate, int subChunkIndex)
        {
            if (subChunkIndex == 0)
            {
                // Tilemap消去を分割している間に古い影だけが残らないよう、チャンク影は消去開始時にまとめて外します。
                DestroyRockWallShadow(coordinate);
            }

            BoundsInt bounds = CreateSubChunkBounds(coordinate, subChunkIndex);
            if (targetTilemap != null)
            {
                targetTilemap.SetTilesBlock(bounds, emptySubChunkTileBuffer);
            }

            if (resourceTilemap != null)
            {
                resourceTilemap.SetTilesBlock(bounds, emptySubChunkTileBuffer);
            }
        }

        /// <summary>
        /// Component無効化時に1チャンク分を即時消去し、次回有効化時の古い表示を残しません。
        /// </summary>
        /// <param name="coordinate">消去する論理チャンク座標です。</param>
        private void ClearWholeChunkImmediately(ChunkCoordinate coordinate)
        {
            for (int subChunkIndex = 0;
                 subChunkIndex < WorldGridConstants.SubChunksPerChunk;
                 subChunkIndex++)
            {
                ClearSubChunk(coordinate, subChunkIndex);
            }
        }

        /// <summary>
        /// 論理チャンク座標とサブチャンク番号から、Tilemapセル用の16×16 BoundsIntを作ります。
        /// </summary>
        /// <param name="coordinate">親となる論理チャンク座標です。</param>
        /// <param name="subChunkIndex">行優先の0〜15サブチャンク番号です。</param>
        /// <returns>Z=0平面にある16×16×1セルの範囲です。</returns>
        private static BoundsInt CreateSubChunkBounds(ChunkCoordinate coordinate, int subChunkIndex)
        {
            int subChunkX = subChunkIndex % WorldGridConstants.SubChunksPerAxis;
            int subChunkY = subChunkIndex / WorldGridConstants.SubChunksPerAxis;
            int worldTileX = WorldGrid.ToWorldTileX(
                coordinate.X,
                subChunkX * WorldGridConstants.SubChunkSize);
            int worldTileY = WorldGrid.ToWorldTileY(
                coordinate.Y,
                subChunkY * WorldGridConstants.SubChunkSize);

            return new BoundsInt(
                worldTileX,
                worldTileY,
                0,
                WorldGridConstants.SubChunkSize,
                WorldGridConstants.SubChunkSize,
                1);
        }

        /// <summary>
        /// ゲームルール用GeneratedTileを、Inspectorで割り当てた表示専用Tileへ変換します。
        /// </summary>
        /// <param name="tile">ECSを正とする1タイル分の基礎地形データです。</param>
        /// <returns>水域・マグマ・岩壁を優先し、それ以外はバイオームに対応するTileです。</returns>
        private TileBase SelectTile(GeneratedTile tile)
        {
            if (tile.Ground == GroundType.Water)
            {
                // 移行直後の旧Sceneでも淡水を空表示にしないよう、淡水参照が未設定なら海水Tileへフォールバックします。
                return tile.WaterSalinity == WaterSalinity.FreshWater && freshWaterTile != null
                    ? freshWaterTile
                    : saltWaterTile;
            }

            if (tile.Ground == GroundType.Magma)
            {
                return magmaTile;
            }

            if (tile.Ground == GroundType.RockWall)
            {
                return rockWallTile;
            }

            if (tile.Biome == BiomeId.Forest)
            {
                return forestTile;
            }

            if (tile.Biome == BiomeId.Desert)
            {
                return desertTile;
            }

            if (tile.Biome == BiomeId.Rockland)
            {
                return rocklandTile;
            }

            if (tile.Biome == BiomeId.Wetland)
            {
                return wetlandTile;
            }

            if (tile.Biome == BiomeId.Snowfield)
            {
                return snowfieldTile;
            }

            if (tile.Biome == BiomeId.Volcanic)
            {
                return volcanicTile;
            }

            return grasslandTile;
        }

        /// <summary>
        /// 生成済みチャンクBufferを優先して上下左右を調べ、資源を残す露出面と影で隠す内部を分類します。
        /// 影の濃さは別途連続距離場から求めるため、この分類は段階的なTile色には使用しません。
        /// </summary>
        /// <param name="coordinate">中心タイルを所有する論理チャンク座標です。</param>
        /// <param name="localTileX">チャンク内の水平タイル座標です。</param>
        /// <param name="localTileY">チャンク内の垂直タイル座標です。</param>
        /// <param name="tile">中心の生成済み地形データです。</param>
        /// <param name="tiles">同じチャンクの4096件の生成済み地形Bufferです。</param>
        /// <param name="generationSettings">チャンク外の隣接セルを再生成する設定です。</param>
        /// <returns>中心が非岩壁ならNone、空地へ接する岩壁ならExposed、それ以外はInteriorです。</returns>
        private static RockWallAppearance ClassifyRockWallAppearance(
            ChunkCoordinate coordinate,
            int localTileX,
            int localTileY,
            GeneratedTile tile,
            DynamicBuffer<GeneratedTile> tiles,
            in WorldGenerationSettings generationSettings)
        {
            if (tile.Ground != GroundType.RockWall)
            {
                return RockWallAppearance.None;
            }

            bool touchesOpenTile = !IsRockWallNeighbor(
                    coordinate,
                    localTileX - 1,
                    localTileY,
                    tiles,
                    generationSettings)
                || !IsRockWallNeighbor(
                    coordinate,
                    localTileX + 1,
                    localTileY,
                    tiles,
                    generationSettings)
                || !IsRockWallNeighbor(
                    coordinate,
                    localTileX,
                    localTileY - 1,
                    tiles,
                    generationSettings)
                || !IsRockWallNeighbor(
                    coordinate,
                    localTileX,
                    localTileY + 1,
                    tiles,
                    generationSettings);
            return touchesOpenTile
                ? RockWallAppearance.Exposed
                : RockWallAppearance.Interior;
        }

        /// <summary>
        /// 同一チャンク内なら生成済みBufferを読み、外側ならワールド座標から岩壁判定だけを再生成します。
        /// </summary>
        /// <param name="coordinate">中心タイルを所有する論理チャンク座標です。</param>
        /// <param name="neighborLocalX">隣接セルの水平チャンク内座標です。影マスク生成時は探索余白まで範囲外になります。</param>
        /// <param name="neighborLocalY">隣接セルの垂直チャンク内座標です。影マスク生成時は探索余白まで範囲外になります。</param>
        /// <param name="tiles">同じチャンクの生成済み地形Bufferです。</param>
        /// <param name="generationSettings">境界外セルの決定論的生成設定です。</param>
        /// <returns>対象隣接セルが岩壁ならtrueです。</returns>
        private static bool IsRockWallNeighbor(
            ChunkCoordinate coordinate,
            int neighborLocalX,
            int neighborLocalY,
            DynamicBuffer<GeneratedTile> tiles,
            in WorldGenerationSettings generationSettings)
        {
            bool isInsideChunk = neighborLocalX >= 0
                && neighborLocalX < WorldGridConstants.LogicalChunkSize
                && neighborLocalY >= 0
                && neighborLocalY < WorldGridConstants.LogicalChunkSize;
            if (isInsideChunk)
            {
                int neighborIndex = WorldGrid.ToTileIndex(neighborLocalX, neighborLocalY);
                return tiles[neighborIndex].Ground == GroundType.RockWall;
            }

            int worldTileX = WorldGrid.ToWorldTileX(coordinate.X, neighborLocalX);
            int worldTileY = WorldGrid.ToWorldTileY(coordinate.Y, neighborLocalY);
            return DeterministicChunkGenerator.IsRockWall(
                generationSettings,
                worldTileX,
                worldTileY);
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
            FillRockWallShadowMask(coordinate, tiles, generationSettings);
            FillRockWallShadowTileOpacityBuffer();

            int vertexSize = WorldGridConstants.LogicalChunkSize + 1;
            int opacitySize = WorldGridConstants.LogicalChunkSize + 2;
            int worldMinimumX = WorldGrid.ToWorldTileX(coordinate.X, 0);
            int worldMinimumY = WorldGrid.ToWorldTileY(coordinate.Y, 0);
            Vector3 meshOrigin = targetTilemap.CellToLocal(
                new Vector3Int(worldMinimumX, worldMinimumY, 0));
            byte maximumAlpha = 0;

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
                            worldMinimumX + vertexX,
                            worldMinimumY + vertexY,
                            0));
                    rockWallShadowVertexBuffer[vertexIndex] = cellCorner - meshOrigin;
                    rockWallShadowColorBuffer[vertexIndex] = new Color32(0, 0, 0, alpha);
                }
            }

            if (maximumAlpha == 0)
            {
                // 小さな岩壁群に内部がない場合は透明Meshを保持せず、描画バッチと管理対象を増やしません。
                DestroyRockWallShadow(coordinate);
                return;
            }

            RockWallShadowChunkPresentation presentation =
                GetOrCreateRockWallShadowPresentation(coordinate);
            if (presentation == null)
            {
                return;
            }

            presentation.GameObject.transform.localPosition = meshOrigin;
            presentation.Mesh.Clear();
            presentation.Mesh.vertices = rockWallShadowVertexBuffer;
            presentation.Mesh.colors32 = rockWallShadowColorBuffer;
            presentation.Mesh.triangles = rockWallShadowTriangleBuffer;
            presentation.Mesh.RecalculateBounds();
        }

        /// <summary>
        /// 影の探索半径を含むチャンク周辺を一度だけ岩壁判定し、距離計算から座標ノイズの重複評価を除きます。
        /// </summary>
        /// <param name="coordinate">中心となる論理チャンク座標です。</param>
        /// <param name="tiles">中心チャンクの生成済み地形Bufferです。</param>
        /// <param name="generationSettings">余白部分を決定論的に再生成する設定です。</param>
        private void FillRockWallShadowMask(
            ChunkCoordinate coordinate,
            DynamicBuffer<GeneratedTile> tiles,
            in WorldGenerationSettings generationSettings)
        {
            int maskPadding = RockWallAppearanceUtility.ShadowSearchRadiusInTiles + 1;
            int maskSize = WorldGridConstants.LogicalChunkSize + (maskPadding * 2);

            for (int maskY = 0; maskY < maskSize; maskY++)
            {
                int localTileY = maskY - maskPadding;
                for (int maskX = 0; maskX < maskSize; maskX++)
                {
                    int localTileX = maskX - maskPadding;
                    rockWallShadowMaskBuffer[(maskY * maskSize) + maskX] =
                        IsRockWallNeighbor(
                            coordinate,
                            localTileX,
                            localTileY,
                            tiles,
                            generationSettings);
                }
            }
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

            // 地形より手前、資源Tilemapより奥へ置き、岩壁内部の暗さがHUDや資源表示全体を覆わないようにします。
            TilemapRenderer terrainRenderer = targetTilemap.GetComponent<TilemapRenderer>();
            if (terrainRenderer != null)
            {
                shadowRenderer.sortingLayerID = terrainRenderer.sortingLayerID;
                shadowRenderer.sortingOrder = terrainRenderer.sortingOrder + 1;
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
        /// 正規化前のObserverにも対応し、指定チャンクがカメラ表示矩形に含まれるか判定します。
        /// </summary>
        /// <param name="observer">Presentation Bridgeが送った可視範囲です。</param>
        /// <param name="coordinate">検査する論理チャンク座標です。</param>
        /// <returns>4辺を含む矩形内ならtrueです。</returns>
        private static bool Contains(ChunkStreamingObserver observer, ChunkCoordinate coordinate)
        {
            int minimumX = Mathf.Min(observer.MinimumVisibleChunkX, observer.MaximumVisibleChunkX);
            int maximumX = Mathf.Max(observer.MinimumVisibleChunkX, observer.MaximumVisibleChunkX);
            int minimumY = Mathf.Min(observer.MinimumVisibleChunkY, observer.MaximumVisibleChunkY);
            int maximumY = Mathf.Max(observer.MinimumVisibleChunkY, observer.MaximumVisibleChunkY);
            return coordinate.X >= minimumX
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
            coordinateScratch.Clear();
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
    }
}
