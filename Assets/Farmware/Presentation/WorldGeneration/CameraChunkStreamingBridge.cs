// このファイルは、2Dカメラの表示範囲を整数の論理チャンク矩形へ変換し、ECSへ入力Commandとして渡します。
// カメラやTransformをゲーム状態の正にはせず、ChunkStreamingSystemが参照するObserver Entityだけを更新します。

using Unity.Entities;
using UnityEngine;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 画面に見えるタイル範囲を毎フレーム確認し、変化したときだけChunkStreamingObserverを更新します。
    /// 2Dカメラへ1つ配置し、任意のGridLayoutを指定するとセルサイズや原点を含めて座標変換できます。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraChunkStreamingBridge : MonoBehaviour
    {
        // 表示範囲を取得する2Dカメラです。未指定なら同じGameObjectのCameraをAwakeで取得します。
        [Tooltip("チャンク読み込み範囲を決める2Dカメラです。未指定なら同じGameObjectのCameraを使用します。")]
        [SerializeField] private Camera targetCamera;

        // ワールド座標を整数タイル座標へ変換するGridです。未指定時は1 Unity unitを1タイルとして扱います。
        [Tooltip("Tilemapを所有するGridです。指定するとセルサイズとGrid原点を考慮して可視タイルを求めます。未指定時は1 Unity unitを1タイルとして扱います。")]
        [SerializeField] private GridLayout targetGrid;

        // このComponentが作成し、OnDisableで破棄するObserver Entityです。セーブ対象にはしません。
        private Entity observerEntity;

        // Observer Entityが属するDefault Worldです。World再作成を検知するため参照を保持します。
        private World observerWorld;

        // 同じWorldへ複数のObserverが存在したとき、Consoleへの警告を1回だけに抑えるフラグです。
        private bool reportedDuplicateObserver;

        // 前回送信した包含矩形です。カメラが同じチャンク内にいるフレームではECS書き込みを省略します。
        private ChunkStreamingObserver lastObserver;

        // lastObserverへ有効な値を送信済みかを示し、初回だけ必ずComponentを書き込めるようにします。
        private bool hasLastObserver;

        /// <summary>
        /// Component読み込み直後にUnityから呼ばれ、未指定のCamera参照を同じGameObjectから補います。
        /// </summary>
        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }
        }

        /// <summary>
        /// MonoBehaviourの各描画フレームで呼ばれ、ECS Worldが利用可能なら可視チャンク矩形を更新します。
        /// Entity作成はWorld初期化後まで遅延できるため、Script実行順へ依存しません。
        /// </summary>
        private void Update()
        {
            if (targetCamera == null || !TryEnsureObserverEntity())
            {
                return;
            }

            ChunkStreamingObserver currentObserver = CalculateVisibleChunkBounds();
            if (hasLastObserver && ObserversAreEqual(lastObserver, currentObserver))
            {
                return;
            }

            observerWorld.EntityManager.SetComponentData(observerEntity, currentObserver);
            lastObserver = currentObserver;
            hasLastObserver = true;
        }

        /// <summary>
        /// Componentが無効化またはGameObjectが破棄されたときにUnityから呼ばれ、所有する一時Entityを破棄します。
        /// </summary>
        private void OnDisable()
        {
            if (observerWorld != null
                && observerWorld.IsCreated
                && observerWorld.EntityManager.Exists(observerEntity))
            {
                observerWorld.EntityManager.DestroyEntity(observerEntity);
            }

            observerEntity = Entity.Null;
            observerWorld = null;
            hasLastObserver = false;
        }

        /// <summary>
        /// Default World内にこのComponentが所有するObserver Entityを1つだけ用意します。
        /// </summary>
        /// <returns>更新可能なEntityが存在する場合はtrue、World未初期化または重複時はfalseです。</returns>
        private bool TryEnsureObserverEntity()
        {
            World currentWorld = World.DefaultGameObjectInjectionWorld;
            if (currentWorld == null || !currentWorld.IsCreated)
            {
                return false;
            }

            if (observerWorld == currentWorld
                && currentWorld.EntityManager.Exists(observerEntity))
            {
                return true;
            }

            EntityQuery existingObserverQuery = currentWorld.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ChunkStreamingObserver>());
            int existingObserverCount = existingObserverQuery.CalculateEntityCount();
            existingObserverQuery.Dispose();

            // Singletonを前提とするため、別カメラがすでに入力している場合は所有権を奪わず待機します。
            if (existingObserverCount > 0)
            {
                if (!reportedDuplicateObserver)
                {
                    Debug.LogWarning(
                        "ChunkStreamingObserverがすでに存在します。CameraChunkStreamingBridgeは1つだけ有効にしてください。",
                        this);
                    reportedDuplicateObserver = true;
                }

                return false;
            }

            observerWorld = currentWorld;
            observerEntity = currentWorld.EntityManager.CreateEntity(typeof(ChunkStreamingObserver));
            hasLastObserver = false;
            reportedDuplicateObserver = false;
            return true;
        }

        /// <summary>
        /// Viewportの左下と右上をワールドへ変換し、少しでも画面へ含まれるチャンクの包含矩形を求めます。
        /// </summary>
        /// <returns>負座標でも床方向除算された可視論理チャンク範囲です。</returns>
        private ChunkStreamingObserver CalculateVisibleChunkBounds()
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
                float minimumWorldX = Mathf.Min(bottomLeftWorld.x, topRightWorld.x);
                float maximumWorldX = Mathf.Max(bottomLeftWorld.x, topRightWorld.x);
                float minimumWorldY = Mathf.Min(bottomLeftWorld.y, topRightWorld.y);
                float maximumWorldY = Mathf.Max(bottomLeftWorld.y, topRightWorld.y);
                minimumTileX = Mathf.FloorToInt(minimumWorldX);
                maximumTileX = Mathf.CeilToInt(maximumWorldX) - 1;
                minimumTileY = Mathf.FloorToInt(minimumWorldY);
                maximumTileY = Mathf.CeilToInt(maximumWorldY) - 1;
            }

            ChunkCoordinate minimumChunk = WorldGrid.ToChunkCoordinate(minimumTileX, minimumTileY);
            ChunkCoordinate maximumChunk = WorldGrid.ToChunkCoordinate(maximumTileX, maximumTileY);
            return new ChunkStreamingObserver
            {
                MinimumVisibleChunkX = minimumChunk.X,
                MaximumVisibleChunkX = maximumChunk.X,
                MinimumVisibleChunkY = minimumChunk.Y,
                MaximumVisibleChunkY = maximumChunk.Y
            };
        }

        /// <summary>
        /// 2つのObserverが同じ包含矩形を表すか比較します。
        /// </summary>
        /// <param name="left">前回ECSへ送信した範囲です。</param>
        /// <param name="right">現在のカメラから計算した範囲です。</param>
        /// <returns>4辺がすべて一致する場合はtrueです。</returns>
        private static bool ObserversAreEqual(ChunkStreamingObserver left, ChunkStreamingObserver right)
        {
            return left.MinimumVisibleChunkX == right.MinimumVisibleChunkX
                && left.MaximumVisibleChunkX == right.MaximumVisibleChunkX
                && left.MinimumVisibleChunkY == right.MinimumVisibleChunkY
                && left.MaximumVisibleChunkY == right.MaximumVisibleChunkY;
        }
    }
}
