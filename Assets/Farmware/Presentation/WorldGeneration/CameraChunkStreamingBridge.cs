// このファイルは、表示原点基準の2Dカメラ範囲を絶対論理チャンク矩形へ戻し、ECSへ入力Commandとして渡します。
// カメラやTransformをゲーム状態の正にはせず、ChunkStreamingSystemが参照するObserver Entityだけを更新します。

using Farmware.Simulation;
using Unity.Entities;
using UnityEngine;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 画面に見えるタイル範囲を毎フレーム確認し、変化したときだけChunkStreamingObserverを更新します。
    /// 2Dカメラへ1つ配置し、任意のGridLayoutを指定するとセルサイズや原点を含めて座標変換できます。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class CameraChunkStreamingBridge : MonoBehaviour
    {
        /// <summary>
        /// 画面下端より下に接地した岩壁が上方向へはみ出す量を、読み込み対象へ追加するタイル数です。
        /// ピクセル数をタイル数へ切り上げることで、1ピクセルでも画面内へ入るSpriteの所属チャンクを保持します。
        /// </summary>
        private const int LowerViewportSpriteOverhangInTiles =
            (TerrainLayerPresentationUtility.RockWallElevationInPixels
                + TerrainLayerPresentationUtility.TerrainSurfaceHeightInPixels - 1)
            / TerrainLayerPresentationUtility.TerrainSurfaceHeightInPixels;

        /// <summary>
        /// 画面上端より上に接地した土地断面が下方向へはみ出す量を、読み込み対象へ追加するタイル数です。
        /// 現在の8ピクセル断面では1タイルとなり、チャンク境界直外の土地Spriteが途中で消えることを防ぎます。
        /// </summary>
        private const int UpperViewportSpriteOverhangInTiles =
            (TerrainLayerPresentationUtility.TerrainFrontFaceHeightInPixels
                + TerrainLayerPresentationUtility.TerrainSurfaceHeightInPixels - 1)
            / TerrainLayerPresentationUtility.TerrainSurfaceHeightInPixels;

        /// <summary>
        /// Camera行列から得た連続セル座標を整数境界へ戻す許容幅です。単位はタイルです。
        /// 0.001タイルは16 PPUで0.016ピクセルに相当し、描画上は区別できない丸め誤差だけを吸収します。
        /// </summary>
        private const float CellBoundarySnapToleranceInTiles = 0.001f;

        // 表示範囲を取得する2Dカメラです。未指定なら同じGameObjectのCameraをAwakeで取得します。
        [Tooltip("チャンク読み込み範囲を決める2Dカメラです。未指定なら同じGameObjectのCameraを使用します。")]
        [SerializeField] private Camera targetCamera;

        // ワールド座標を整数タイル座標へ変換するGridです。未指定時は1 Unity unitを1タイルとして扱います。
        [Tooltip("Tilemapを所有するGridです。指定するとセルサイズとGrid原点を考慮して可視タイルを求めます。未指定時は1 Unity unitを1タイルとして扱います。")]
        [SerializeField] private GridLayout targetGrid;

        // 初期スポーン探索が成功または失敗する前に不要なチャンクを要求しないよう、同じCamera上の初期化状態を参照します。
        private InitialCameraPositionController initialPositionController;

        // CameraとTilemapが共有する表示原点です。同じGameObjectのCameraPanControllerがPresentation寿命中だけ保持します。
        private CameraPanController cameraPanController;

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

        // 現在カメラが追従する論理レイヤーです。Observerの外部更新を毎フレーム取り込み、可視矩形の再計算で保持します。
        private WorldLayer currentLayer = WorldLayer.Surface;

        /// <summary>
        /// Component読み込み直後にUnityから呼ばれ、未指定のCamera参照を同じGameObjectから補います。
        /// </summary>
        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }

            initialPositionController = GetComponent<InitialCameraPositionController>();
            cameraPanController = GetComponent<CameraPanController>();
        }

        /// <summary>
        /// MonoBehaviourの各描画フレームで呼ばれ、ECS Worldが利用可能なら可視チャンク矩形を更新します。
        /// Entity作成はWorld初期化後まで遅延できるため、Script実行順へ依存しません。
        /// </summary>
        private void Update()
        {
            // 初期スポーン探索の待機中でもDefault Worldは切り替わる可能性があります。
            // 新WorldへObserverを作るのは待機終了後に遅らせつつ、旧Worldの要求だけは直ちに止める必要があります。
            DestroyOwnedObserverIfDefaultWorldChanged();

            // 初期位置探索中はObserverを作らず、後で不要になる原点チャンクの生成・表示を防ぎます。
            // 探索失敗も完了に含めるため、その場合はScene保存位置から通常どおり読み込みを始めます。
            if (initialPositionController != null
                && !initialPositionController.HasCompletedInitialPositioning)
            {
                return;
            }

            if (targetCamera == null || !TryEnsureObserverEntity())
            {
                return;
            }

            // 潜水など別の入力境界がこのBridge所有ObserverのLayerを変更した場合、その値を先に取り込みます。
            // カメラ移動でX/Y範囲を更新しても4引数互換APIのSurfaceへ戻さず、選択中レイヤーを維持します。
            ChunkStreamingObserver ownedObserver = observerWorld.EntityManager
                .GetComponentData<ChunkStreamingObserver>(observerEntity)
                .Normalized();
            currentLayer = ownedObserver.Layer;

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
        /// Default Worldの変更または消失を、初期スポーン待機判定より先に検出して旧Observerを破棄します。
        /// 新World側のObserverは作成せず、初期位置が確定した後のTryEnsureObserverEntityへ作成を委ねます。
        /// </summary>
        private void DestroyOwnedObserverIfDefaultWorldChanged()
        {
            if (observerWorld == null)
            {
                return;
            }

            World currentWorld = World.DefaultGameObjectInjectionWorld;
            if (observerWorld == currentWorld && observerWorld.IsCreated)
            {
                return;
            }

            // 旧Worldがまだ更新中ならEntityを実際に破棄し、破棄済みなら所有参照と送信キャッシュだけを安全に戻します。
            DestroyOwnedObserverEntity();
        }

        /// <summary>
        /// Componentが無効化またはGameObjectが破棄されたときにUnityから呼ばれ、所有する一時Entityを破棄します。
        /// </summary>
        private void OnDisable()
        {
            DestroyOwnedObserverEntity();
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
                // Default Worldが一時的に失われた場合も、以前のWorldが存続していれば所有Entityだけを残さないよう片付けます。
                DestroyOwnedObserverEntity();
                return false;
            }

            if (observerWorld == currentWorld
                && currentWorld.EntityManager.Exists(observerEntity))
            {
                return true;
            }

            // PlayMode再初期化などでDefault Worldだけが切り替わる場合、古いWorldはまだ有効なことがあります。
            // 所有参照を新Worldへ上書きする前に旧Entityを破棄し、各WorldへObserverが1件ずつ残る状態を防ぎます。
            if (observerWorld != null)
            {
                DestroyOwnedObserverEntity();
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
        /// このBridgeが作成したObserver Entityを、作成元Worldがまだ有効な場合だけ破棄します。
        /// World切替、Component無効化、Default World消失のすべてが同じ後始末を通り、古いObserver参照を残しません。
        /// </summary>
        private void DestroyOwnedObserverEntity()
        {
            if (observerWorld != null
                && observerWorld.IsCreated
                && observerEntity != Entity.Null
                && observerWorld.EntityManager.Exists(observerEntity))
            {
                observerWorld.EntityManager.DestroyEntity(observerEntity);
            }

            observerEntity = Entity.Null;
            observerWorld = null;
            reportedDuplicateObserver = false;
            hasLastObserver = false;
            currentLayer = WorldLayer.Surface;
        }

        /// <summary>
        /// Viewportの左下と右上をワールドへ変換し、少しでも画面へ含まれるチャンクの包含矩形を求めます。
        /// Transformに追従するCamera行列へ戻してから変換し、前フレームの描画用行列を新しい表示原点と混ぜません。
        /// </summary>
        /// <returns>負座標でも床方向除算された可視論理チャンク範囲です。</returns>
        private ChunkStreamingObserver CalculateVisibleChunkBounds()
        {
            // URPのPixel Perfect Cameraは描画直前にworldToCameraMatrixを設定し、描画後もその値を保持します。
            // 原点更新はUpdate中にTransformを移すため、古い行列のままだと新原点を加えた可視要求が512タイルずれます。
            // このBridgeは現在のTransformを読み込み範囲の基準とし、描画時のピクセル整列はURPが再設定します。
            targetCamera.ResetWorldToCameraMatrix();

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
                Vector3 bottomLeftLocal =
                    targetGrid.transform.InverseTransformPoint(bottomLeftWorld);
                Vector3 topRightLocal =
                    targetGrid.transform.InverseTransformPoint(topRightWorld);
                Vector3 bottomLeftCell = targetGrid.LocalToCellInterpolated(bottomLeftLocal);
                Vector3 topRightCell = targetGrid.LocalToCellInterpolated(topRightLocal);

                // 2端点を連続セル座標の最小・最大へ直してから、下限を包含、上限を排他として整数化します。
                // Camera行列の丸めで64が64.00000…になる場合も、描画上識別不能な範囲だけ整数へ戻して隣接チャンクを除外します。
                float minimumCellX = Mathf.Min(bottomLeftCell.x, topRightCell.x);
                float maximumCellX = Mathf.Max(bottomLeftCell.x, topRightCell.x);
                float minimumCellY = Mathf.Min(bottomLeftCell.y, topRightCell.y);
                float maximumCellY = Mathf.Max(bottomLeftCell.y, topRightCell.y);
                minimumTileX = Mathf.FloorToInt(minimumCellX);
                maximumTileX = CalculateUpperExclusiveMaximumTile(maximumCellX);
                minimumTileY = Mathf.FloorToInt(minimumCellY);
                maximumTileY = CalculateUpperExclusiveMaximumTile(maximumCellY);
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

            DisplayOrigin displayOrigin = cameraPanController == null
                ? new DisplayOrigin(Unity.Mathematics.int2.zero)
                : cameraPanController.CurrentDisplayOrigin;
            return CalculateChunkBoundsFromVisibleTileBounds(
                currentLayer,
                AddDisplayOriginSaturated(
                    minimumTileX,
                    displayOrigin.AbsoluteTileCoordinate.x),
                AddDisplayOriginSaturated(
                    maximumTileX,
                    displayOrigin.AbsoluteTileCoordinate.x),
                AddDisplayOriginSaturated(
                    minimumTileY,
                    displayOrigin.AbsoluteTileCoordinate.y),
                AddDisplayOriginSaturated(
                    maximumTileY,
                    displayOrigin.AbsoluteTileCoordinate.y));
        }

        /// <summary>
        /// 表示用の相対タイル座標へ1軸分の表示原点を加え、絶対タイル座標へ戻します。
        /// int世界の端を越えるViewport端は端のセルへ飽和させ、符号反転による誤ったチャンク要求を防ぎます。
        /// </summary>
        /// <param name="relativeTileCoordinate">表示原点から数えた相対タイル座標です。</param>
        /// <param name="displayOriginCoordinate">同じ軸の絶対表示原点です。</param>
        /// <returns>int範囲へ収めた絶対タイル座標です。</returns>
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
        /// 連続セル座標の上限を上限排他的な最大タイル座標へ変換します。
        /// Camera行列由来の微小な整数境界誤差だけを丸め、実際に面積を持つセルを欠落させないようにします。
        /// </summary>
        /// <param name="upperExclusiveCellCoordinate">Viewport上限の連続セル座標です。</param>
        /// <returns>Viewport内に面積を持つ最大の整数タイル座標です。</returns>
        private static int CalculateUpperExclusiveMaximumTile(float upperExclusiveCellCoordinate)
        {
            float nearestInteger = Mathf.Round(upperExclusiveCellCoordinate);
            float stabilizedCoordinate =
                Mathf.Abs(upperExclusiveCellCoordinate - nearestInteger)
                <= CellBoundarySnapToleranceInTiles
                    ? nearestInteger
                    : upperExclusiveCellCoordinate;
            return Mathf.CeilToInt(stabilizedCoordinate) - 1;
        }

        /// <summary>
        /// 論理的に画面へ入るタイル矩形へ土地・岩壁Spriteの上下はみ出し量を足し、必要なチャンク包含矩形へ変換します。
        /// CameraやGridへ依存しない純粋計算として、チャンク境界と負座標をPlayModeテストから検証できます。
        /// </summary>
        /// <param name="minimumTileX">画面へ入る最小の水平ワールドタイル座標です。</param>
        /// <param name="maximumTileX">画面へ入る最大の水平ワールドタイル座標です。</param>
        /// <param name="minimumTileY">画面へ入る最小の垂直ワールドタイル座標です。</param>
        /// <param name="maximumTileY">画面へ入る最大の垂直ワールドタイル座標です。</param>
        /// <returns>Spriteの最大上下はみ出しを含めた論理チャンク包含矩形です。</returns>
        public static ChunkStreamingObserver CalculateChunkBoundsFromVisibleTileBounds(
            int minimumTileX,
            int maximumTileX,
            int minimumTileY,
            int maximumTileY)
        {
            return CalculateChunkBoundsFromVisibleTileBounds(
                WorldLayer.Surface,
                minimumTileX,
                maximumTileX,
                minimumTileY,
                maximumTileY);
        }

        /// <summary>
        /// 指定レイヤーの可視タイル矩形へSpriteの上下はみ出し量を足し、レイヤー付きチャンク包含矩形へ変換します。
        /// </summary>
        /// <param name="layer">カメラが論理的に表示している固定ワールドレイヤーです。</param>
        /// <param name="minimumTileX">画面へ入る最小の水平ワールドタイル座標です。</param>
        /// <param name="maximumTileX">画面へ入る最大の水平ワールドタイル座標です。</param>
        /// <param name="minimumTileY">画面へ入る最小の垂直ワールドタイル座標です。</param>
        /// <param name="maximumTileY">画面へ入る最大の垂直ワールドタイル座標です。</param>
        /// <returns>レイヤーとSpriteの最大上下はみ出しを含めた論理チャンク包含矩形です。</returns>
        public static ChunkStreamingObserver CalculateChunkBoundsFromVisibleTileBounds(
            WorldLayer layer,
            int minimumTileX,
            int maximumTileX,
            int minimumTileY,
            int maximumTileY)
        {
            // 呼び出し側の2端点順に依存しないよう、先に各軸を最小・最大へ正規化します。
            int normalizedMinimumTileX = Mathf.Min(minimumTileX, maximumTileX);
            int normalizedMaximumTileX = Mathf.Max(minimumTileX, maximumTileX);
            int normalizedMinimumTileY = Mathf.Min(minimumTileY, maximumTileY);
            int normalizedMaximumTileY = Mathf.Max(minimumTileY, maximumTileY);

            // 岩壁は接地セルから上へ、土地断面は接地セルから下へ見えるため、Viewportの反対側にある接地セルも読み込みます。
            // int端へ到達した診断値でもオーバーフローしないよう、加減算できる範囲へ飽和させます。
            int expandedMinimumTileY = normalizedMinimumTileY
                < int.MinValue + LowerViewportSpriteOverhangInTiles
                    ? int.MinValue
                    : normalizedMinimumTileY - LowerViewportSpriteOverhangInTiles;
            int expandedMaximumTileY = normalizedMaximumTileY
                > int.MaxValue - UpperViewportSpriteOverhangInTiles
                    ? int.MaxValue
                    : normalizedMaximumTileY + UpperViewportSpriteOverhangInTiles;

            ChunkCoordinate minimumChunk = WorldGrid.ToChunkCoordinate(
                layer,
                normalizedMinimumTileX,
                expandedMinimumTileY);
            ChunkCoordinate maximumChunk = WorldGrid.ToChunkCoordinate(
                layer,
                normalizedMaximumTileX,
                expandedMaximumTileY);
            return new ChunkStreamingObserver
            {
                Layer = layer,
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
            return left.Layer == right.Layer
                && left.MinimumVisibleChunkX == right.MinimumVisibleChunkX
                && left.MaximumVisibleChunkX == right.MaximumVisibleChunkX
                && left.MinimumVisibleChunkY == right.MinimumVisibleChunkY
                && left.MaximumVisibleChunkY == right.MaximumVisibleChunkY;
        }
    }
}
