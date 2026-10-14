// このファイルは、Simulationが決定した初期スポーン探索結果を読み、Play開始時のカメラ位置を一度だけ確定します。
// 成功時はスポーンへ移動し、失敗時はScene保存位置を採用します。探索結果はECSを正とし、Camera Transformへ渡すPresentation境界だけを担当します。

using Unity.Entities;
using UnityEngine;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// InitialSpawnLocationまたは探索失敗状態を待ち、成功時だけ対象タイル中央へCameraPanControllerを移動します。
    /// CameraChunkStreamingBridgeより先に実行し、不要な原点チャンクを初回要求しないようにします。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CameraPanController))]
    public sealed class InitialCameraPositionController : MonoBehaviour
    {
        // カメラ移動とピクセル整列を一元化するため、Transformへ直接座標を書かず既存Controllerを経由します。
        [Tooltip("初期スポーン地点へ移動させるCameraPanControllerです。未指定なら同じGameObjectから取得します。")]
        [SerializeField] private CameraPanController cameraPanController;

        // タイル中央をワールド座標へ変換するGridです。地形Tilemapと同じGridを指定します。
        [Tooltip("初期スポーンの整数タイル座標をワールド中央座標へ変換するGridです。未指定時は1タイルを1 Unity unitとして扱います。")]
        [SerializeField] private GridLayout targetGrid;

        // InitialSpawnLocationを読むQueryが属するWorldです。PlayMode中のWorld再作成を検知するため保持します。
        private World spawnLocationWorld;

        // 初期スポーンSingletonだけを読むQueryです。毎フレームQueryを作り直さないため再利用します。
        private EntityQuery spawnLocationQuery;

        // 初期スポーン探索の成功・失敗を読むQueryです。失敗時もカメラ初期化の待機を終えるために使用します。
        private EntityQuery searchStatusQuery;

        // spawnLocationQueryを現在のWorld向けに作成済みかを示します。
        private bool queryIsCreated;

        // このPlay開始中に初期カメラ位置を適用済みかを示し、プレイヤー移動を後から上書きしないようにします。
        private bool hasAppliedInitialPosition;

        // 成功時の移動または失敗時のScene位置採用が確定したことを示し、ストリーミング待機を解除します。
        private bool hasCompletedInitialPositioning;

        // Singleton重複を検出したとき、同じ警告を毎フレーム出さないためのフラグです。
        private bool reportedDuplicateLocation;

        // 探索状態Singletonの重複を検出したとき、同じ警告を毎フレーム出さないためのフラグです。
        private bool reportedDuplicateSearchStatus;

        /// <summary>このPlay開始中に、記録済み初期スポーン地点をカメラへ適用済みの場合はtrueです。</summary>
        public bool HasAppliedInitialPosition => hasAppliedInitialPosition;

        /// <summary>
        /// 初期スポーン地点の適用、または探索失敗時のScene保存位置採用が確定した場合はtrueです。
        /// CameraChunkStreamingBridgeはこの値がtrueになってから最初のチャンク要求を作成します。
        /// </summary>
        public bool HasCompletedInitialPositioning => hasCompletedInitialPositioning;

        /// <summary>
        /// Editor構築ツールまたはテストから、移動Controllerと地形Gridをまとめて設定します。
        /// </summary>
        /// <param name="panController">初期XY座標とピクセル整列を反映するカメラ移動Controllerです。</param>
        /// <param name="grid">スポーンタイル中央をワールド座標へ変換するGridです。</param>
        public void Configure(CameraPanController panController, GridLayout grid)
        {
            cameraPanController = panController;
            targetGrid = grid;
        }

        /// <summary>
        /// Component読み込み直後にUnityから呼ばれ、未設定のCameraPanControllerを同じGameObjectから補います。
        /// </summary>
        private void Awake()
        {
            if (cameraPanController == null)
            {
                cameraPanController = GetComponent<CameraPanController>();
            }
        }

        /// <summary>
        /// Componentが有効になったときにUnityから呼ばれ、Play開始ごとの一度だけ適用できる状態へ戻します。
        /// </summary>
        private void OnEnable()
        {
            hasAppliedInitialPosition = false;
            hasCompletedInitialPositioning = false;
            reportedDuplicateLocation = false;
            reportedDuplicateSearchStatus = false;
        }

        /// <summary>
        /// MonoBehaviourの各描画フレームで呼ばれ、探索成功時はカメラを移動し、失敗時はScene保存位置の採用を確定します。
        /// </summary>
        private void Update()
        {
            // 初期位置を適用済みでもDefault WorldはPlay中に再作成される場合があります。
            // 完了判定より先にQueryを確認し、新しいWorldならTryEnsureQuery内で適用状態を戻します。
            if (cameraPanController == null
                || !TryEnsureQuery()
                || hasCompletedInitialPositioning)
            {
                return;
            }

            int locationCount = spawnLocationQuery.CalculateEntityCount();
            if (locationCount == 0)
            {
                int searchStatusCount = searchStatusQuery.CalculateEntityCount();
                if (searchStatusCount == 1)
                {
                    InitialSpawnSearchStatus searchStatus =
                        searchStatusQuery.GetSingleton<InitialSpawnSearchStatus>();

                    // 探索失敗時は移動先が存在しないため、Sceneへ保存されたカメラ座標を正式な代替位置として採用します。
                    if (searchStatus.Phase == InitialSpawnSearchPhase.Failed)
                    {
                        hasCompletedInitialPositioning = true;
                    }

                    return;
                }

                if (searchStatusCount > 1 && !reportedDuplicateSearchStatus)
                {
                    Debug.LogError(
                        $"InitialSpawnSearchStatusが{searchStatusCount}件あります。探索状態は1件だけにしてください。",
                        this);
                    reportedDuplicateSearchStatus = true;
                }

                return;
            }

            if (locationCount != 1)
            {
                if (!reportedDuplicateLocation)
                {
                    Debug.LogError(
                        $"InitialSpawnLocationが{locationCount}件あります。初期スポーン地点は1件だけにしてください。",
                        this);
                    reportedDuplicateLocation = true;
                }

                return;
            }

            InitialSpawnLocation spawnLocation =
                spawnLocationQuery.GetSingleton<InitialSpawnLocation>();
            Vector3 spawnWorldPosition;
            if (targetGrid == null)
            {
                spawnWorldPosition = new Vector3(
                    spawnLocation.TileX + 0.5f,
                    spawnLocation.TileY + 0.5f,
                    0f);
            }
            else
            {
                Vector3Int spawnCell = new Vector3Int(
                    spawnLocation.TileX,
                    spawnLocation.TileY,
                    0);
                Vector3 cellMinimum = targetGrid.CellToWorld(spawnCell);
                Vector3 cellMaximum = targetGrid.CellToWorld(
                    new Vector3Int(spawnCell.x + 1, spawnCell.y + 1, spawnCell.z));

                // GridLayoutにはGrid.GetCellCenterWorldがないため、対角2点の中点から同じセル中央を求めます。
                spawnWorldPosition = (cellMinimum + cellMaximum) * 0.5f;
            }

            // ZはCameraPanControllerが現在値を維持するため、ここではタイル中央のXYだけを渡します。
            cameraPanController.SetWorldPosition(
                new Vector2(spawnWorldPosition.x, spawnWorldPosition.y));
            hasAppliedInitialPosition = true;
            hasCompletedInitialPositioning = true;
        }

        /// <summary>
        /// Default Worldの作成待ちと再作成を扱い、現在World用の初期位置と探索状態のQueryを用意します。
        /// </summary>
        /// <returns>現在のWorldから初期スポーン地点と探索状態を読み取れるQueryがある場合はtrueです。</returns>
        private bool TryEnsureQuery()
        {
            World currentWorld = World.DefaultGameObjectInjectionWorld;
            if (currentWorld == null || !currentWorld.IsCreated)
            {
                return false;
            }

            if (queryIsCreated && spawnLocationWorld == currentWorld)
            {
                return true;
            }

            DisposeQuery();
            spawnLocationWorld = currentWorld;
            spawnLocationQuery = currentWorld.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<InitialSpawnLocation>());
            searchStatusQuery = currentWorld.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<InitialSpawnSearchStatus>());
            queryIsCreated = true;
            hasAppliedInitialPosition = false;
            hasCompletedInitialPositioning = false;
            reportedDuplicateLocation = false;
            reportedDuplicateSearchStatus = false;
            return true;
        }

        /// <summary>
        /// Component破棄時にUnityから呼ばれ、このPresentationが作成したQueryを解放します。
        /// </summary>
        private void OnDestroy()
        {
            DisposeQuery();
        }

        /// <summary>
        /// 現在のWorld向けQueryと所有Worldが有効な場合だけQueryを破棄し、World再作成時の古い参照を残しません。
        /// PlayMode終了時はECS Worldが先に破棄される場合があるため、破棄済みEntityManagerへDisposeを要求しません。
        /// </summary>
        private void DisposeQuery()
        {
            if (queryIsCreated
                && spawnLocationWorld != null
                && spawnLocationWorld.IsCreated)
            {
                spawnLocationQuery.Dispose();
                searchStatusQuery.Dispose();
            }

            spawnLocationWorld = null;
            spawnLocationQuery = default;
            searchStatusQuery = default;
            queryIsCreated = false;
        }
    }
}
