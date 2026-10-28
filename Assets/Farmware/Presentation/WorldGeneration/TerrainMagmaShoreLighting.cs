// このファイルは、ChunkTilemapPresenterがECSの有効地形から抽出したマグマ岸だけへ、少数のLight2Dを再利用して配置します。
// 地形判定をTilemapから再構築せず、Dirtyサブチャンクの候補キャッシュと表示用Cameraを使います。Simulationや保存データは変更しません。
using System;
using System.Collections.Generic;
using Farmware.Simulation;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>製品Ground Presenterに1個配置し、ワールドに固定した岸光源の表示範囲と寿命を管理します。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ChunkTilemapPresenter))]
    // URP Light2DのLateUpdate（既定順0）が境界を更新する前に光源を配置し、移動フレームの古い位置でカリングされることを防ぎます。
    [DefaultExecutionOrder(-50)]
    public sealed class TerrainMagmaShoreLighting : MonoBehaviour
    {
        /// <summary>候補選択と光源位置反映を分離計測するProfiler Markerです。Simulation時間は含みません。</summary>
        private static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("Farmware.WorldGeneration.MagmaShoreLighting");
        /// <summary>共通表示原点を持つCameraです。Editor接続時に設定し、原点変更を毎フレーム読みます。</summary>
        [Tooltip("マグマ岸の可視範囲と表示原点を供給するCameraPanControllerです。Scene接続ツールが設定します。")]
        [SerializeField] private CameraPanController cameraController;
        /// <summary>岸の橙色です。Inspectorで設定し、選択更新時に光源へ反映します。</summary>
        [Tooltip("岸から地面・壁へ届く光の色です。マグマ表面のHDR色とは独立しています。")]
        [SerializeField] private Color lightColor = new Color(1f, 0.22f, 0.035f);
        /// <summary>Light2Dの相対強度です。Inspectorで調整します。</summary>
        [Tooltip("周囲を照らす光の相対強度です。0で照明を消し、値を上げるほど橙色が強くなります。")]
        [Range(0f, 1f)] [SerializeField] private float intensity = 0.12f;
        /// <summary>強度が一定の中心半径です。単位はワールド単位です。</summary>
        [Tooltip("光源中心で光が減衰しない半径です。単位はワールド単位（1タイル=1）です。")]
        [Min(0f)] [SerializeField] private float innerRadius = 0.25f;
        /// <summary>光が届く外半径です。単位はワールド単位です。</summary>
        [Tooltip("光が届く最大半径です。単位はワールド単位です。増やすと岸から広い範囲へ光が届きます。")]
        [Min(0.1f)] [SerializeField] private float outerRadius = 3f;
        /// <summary>光源を1個選ぶ固定区画の一辺です。単位はタイルで、Play開始から再構築まで固定します。</summary>
        [Tooltip("絶対座標に固定する照明区画の一辺です。単位はタイルです。各区画から岸を1セルだけ選びます。変更後はPlayを再開してください。")]
        [Range(2, 8)] [SerializeField] private int placementCellSize = 3;
        /// <summary>静止地形で光源候補を選び直す間隔です。単位は実時間秒です。</summary>
        [Tooltip("静止時に可視光源を再確認する間隔です。単位は実時間秒です。カメラ移動・ズーム・Dirty地形は同じフレームに反映します。")]
        [Min(0.05f)] [SerializeField] private float refreshInterval = 0.5f;
        /// <summary>借用Cameraです。AwakeとConfigureで参照をキャッシュします。</summary>
        private Camera targetCamera;
        /// <summary>絶対チャンク座標ごとの16サブチャンクの岸候補です。地形表示の寿命に合わせて更新・除去します。</summary>
        private readonly Dictionary<ChunkCoordinate, List<int2>[]> candidates = new Dictionary<ChunkCoordinate, List<int2>[]>();
        /// <summary>選択時に可視候補だけを集める再利用配列です。</summary>
        private readonly List<int2> visibleCandidates = new List<int2>(256);
        /// <summary>現在の光源に対応する絶対タイル座標です。原点変更でも同じ地点を保ちます。</summary>
        private readonly List<int2> selected = new List<int2>(16);
        /// <summary>このComponentだけが生成・破棄するLight2Dプールです。</summary>
        private readonly List<Light2D> lights = new List<Light2D>(16);
        /// <summary>可視矩形のCamera中心です。光源位置の選択には使わず、遠距離の精度を保つためdoubleを使います。</summary>
        private double2 absoluteCenter;
        /// <summary>前回選択時のCamera縦半幅です。ズーム変更も同じフレームにカリングへ反映します。</summary>
        private float previousOrthographicSize;
        /// <summary>前回選択時のCamera縦横比です。Gameビューのサイズ変更を検知します。</summary>
        private float previousAspect;
        /// <summary>選択更新を待つ次回の実時間秒です。</summary>
        private float nextRefresh;
        /// <summary>Dirty地形の候補更新を次のLateUpdateへ通知します。</summary>
        private bool dirty = true;
        /// <summary>並べ替えdelegateの反復生成を避けるため、初期化時に保持します。</summary>
        private Comparison<int2> comparison;

        /// <summary>Play開始時にUnityが呼び、Camera参照と比較delegateを初期化します。</summary>
        private void Awake()
        {
            targetCamera = cameraController != null ? cameraController.GetComponent<Camera>() : null;
            comparison = CompareCandidates;
        }

        /// <summary>製品Scene接続時に共通Cameraを設定します。Simulationには書き込みません。</summary>
        /// <param name="controller">表示原点を供給するCameraです。</param>
        public void Configure(CameraPanController controller)
        {
            cameraController = controller;
            targetCamera = controller != null ? controller.GetComponent<Camera>() : null;
            dirty = true;
        }

        /// <summary>Dirtyサブチャンクを分類する直前に、同じ部分の古い岸候補だけを消します。</summary>
        /// <param name="coordinate">WorldLayerを含む絶対チャンク座標です。</param>
        /// <param name="index">行優先のサブチャンク番号0〜15です。</param>
        public void BeginSubChunk(ChunkCoordinate coordinate, int index)
        {
            if (!candidates.TryGetValue(coordinate, out List<int2>[] chunks))
            {
                chunks = new List<int2>[WorldGridConstants.SubChunksPerChunk];
                candidates.Add(coordinate, chunks);
            }
            chunks[index]?.Clear();
            dirty = true;
        }

        /// <summary>壁に隠れていないマグマが画面上の岸へ接する場合だけ、絶対タイル座標を表示候補へ記録します。</summary>
        /// <param name="coordinate">現在のチャンクです。</param>
        /// <param name="index">更新中のサブチャンク番号です。</param>
        /// <param name="x">チャンク内のX座標です。</param>
        /// <param name="y">チャンク内のY座標です。</param>
        /// <param name="neighborhood">Tilemapと同じ、変更差分を適用済みのECS完成地形です。</param>
        public void CollectMagmaCell(ChunkCoordinate coordinate, int index, int x, int y,
            in CompletedChunkTerrainNeighborhood neighborhood)
        {
            if (!neighborhood.TryGetTile(x, y, out GeneratedTerrainCell current)
                || !IsVisibleMagmaShore(neighborhood, x, y, current.LiquidRuntimeId)) return;
            int2 absolute = new int2((int)((long)coordinate.X * WorldGridConstants.LogicalChunkSize + x),
                (int)((long)coordinate.Y * WorldGridConstants.LogicalChunkSize + y));
            if (!IsFixedWorldAnchor(neighborhood, x, y, absolute, current.LiquidRuntimeId)) return;
            List<int2>[] chunks = candidates[coordinate];
            if (chunks[index] == null) chunks[index] = new List<int2>(16);
            chunks[index].Add(absolute);
        }

        /// <summary>指定セルが同種マグマの見えている岸か、共通の表示投影から確認します。</summary>
        /// <param name="neighborhood">差分込みの完成地形です。</param>
        /// <param name="x">調べるチャンク基準Xです。</param>
        /// <param name="y">調べるチャンク基準Yです。</param>
        /// <param name="magmaId">呼び出し元でマグマと分類済みのLiquid IDです。</param>
        /// <returns>同種マグマが壁に隠れず、画面上の岸に接する場合はtrueです。</returns>
        private static bool IsVisibleMagmaShore(in CompletedChunkTerrainNeighborhood neighborhood,
            int x, int y, DefinitionRuntimeId magmaId)
        {
            if (!magmaId.IsValid || !neighborhood.TryGetTile(x, y, out GeneratedTerrainCell current)
                || current.LiquidRuntimeId.Value != magmaId.Value || IsCoveredByWall(neighborhood, x, y)) return false;
            // 論理的にはマグマが連続していても、手前の岩壁の上面・前面が液面へ重なれば画面上では岸になります。
            // 壁の裏へ光源を置くと壁の中央から光が出て見えるため、表示時と同じ投影高を考慮します。
            return IsVisibleShore(neighborhood, x - 1, y, current)
                || IsVisibleShore(neighborhood, x + 1, y, current)
                || IsVisibleShore(neighborhood, x, y - 1, current)
                || IsVisibleShore(neighborhood, x, y + 1, current);
        }

        /// <summary>固定区画の中心に最も近い岸を1セル選び、カメラやロード順で光源位置が変わることを防ぎます。</summary>
        /// <param name="neighborhood">区画がチャンク境界を越える場合も含む完成近傍です。</param>
        /// <param name="x">現在セルのチャンク基準Xです。</param>
        /// <param name="y">現在セルのチャンク基準Yです。</param>
        /// <param name="absolute">現在セルの絶対座標です。</param>
        /// <param name="magmaId">マグマのLiquid IDです。</param>
        /// <returns>現在セルが区画の固定光源ならtrueです。</returns>
        private bool IsFixedWorldAnchor(in CompletedChunkTerrainNeighborhood neighborhood,
            int x, int y, int2 absolute, DefinitionRuntimeId magmaId)
        {
            int size = Mathf.Clamp(placementCellSize, 2, 8);
            // C#の負数剰余を0以上へ補正し、負側でも同じ絶対格子を使います。大きい絶対値はfloatへ変換しません。
            int startX = x - ((absolute.x % size + size) % size);
            int startY = y - ((absolute.y % size + size) % size);
            int bestDistance = int.MaxValue;
            int bestX = 0;
            int bestY = 0;
            for (int row = 0; row < size; row++)
                for (int column = 0; column < size; column++)
                {
                    int candidateX = startX + column;
                    int candidateY = startY + row;
                    if (!IsVisibleMagmaShore(neighborhood, candidateX, candidateY, magmaId)) continue;
                    // 中心からの距離を2倍した整数で比較します。同距離は走査順のY・Xで固定します。
                    int dx = column * 2 - (size - 1);
                    int dy = row * 2 - (size - 1);
                    int distance = dx * dx + dy * dy;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    bestX = candidateX;
                    bestY = candidateY;
                }
            return bestDistance != int.MaxValue && bestX == x && bestY == y;
        }

        /// <summary>周囲4方向が異種地形、または投影された壁で液面が隠れる場合を表示上の岸とします。</summary>
        /// <param name="neighborhood">同じ分類で借用中の完成近傍です。</param>
        /// <param name="x">隣セルのXです。</param>
        /// <param name="y">隣セルのYです。</param>
        /// <param name="current">呼び出し元でマグマと分類した現在セルです。</param>
        /// <returns>完成した隣セルに地形境界または壁の被覆がある場合はtrueです。未完成近傍は岸にしません。</returns>
        private static bool IsVisibleShore(in CompletedChunkTerrainNeighborhood neighborhood, int x, int y,
            in GeneratedTerrainCell current)
        {
            return neighborhood.TryGetTile(x, y, out GeneratedTerrainCell neighbor)
                && (neighbor.LiquidRuntimeId.Value != current.LiquidRuntimeId.Value
                    || neighbor.GroundBlockRuntimeId.IsValid || neighbor.WallBlockRuntimeId.IsValid
                    || IsCoveredByWall(neighborhood, x, y));
        }

        /// <summary>手前のWallから投影される前面・上面が、指定した液面セルを隠すか確認します。</summary>
        /// <param name="neighborhood">Tilemapと同じ差分込みの完成地形です。</param>
        /// <param name="x">画面上の液面セルXです。</param>
        /// <param name="y">画面上の液面セルYです。</param>
        /// <returns>手前1セルのWall前面、または手前2セルのWall上面で隠れる場合はtrueです。</returns>
        private static bool IsCoveredByWall(in CompletedChunkTerrainNeighborhood neighborhood, int x, int y)
        {
            // Wallの上面は根元から+2Y、前面はその間を埋めます。共通の表示高を使い、光だけ別の高さにしません。
            for (int offset = 1; offset <= TerrainMaterialVerticalProjectionUtility.WallTopOffsetInTiles; offset++)
                if (neighborhood.TryGetTile(x, y - offset, out GeneratedTerrainCell source)
                    && source.WallBlockRuntimeId.IsValid) return true;
            return false;
        }

        /// <summary>表示チャンクの消去開始時に、そのチャンクの岸候補をまとめて除去します。</summary>
        /// <param name="coordinate">消去する絶対チャンクです。</param>
        public void RemoveChunk(ChunkCoordinate coordinate)
        {
            candidates.Remove(coordinate);
            dirty = true;
        }

        /// <summary>World・WorldLayer変更やPresenter無効化時に候補と現在光源を消します。光源プールは再利用します。</summary>
        public void ClearCandidates()
        {
            candidates.Clear();
            visibleCandidates.Clear();
            selected.Clear();
            foreach (Light2D light in lights) if (light != null) light.enabled = false;
            dirty = true;
        }

        /// <summary>Cameraと地形表示の更新後に候補を選び、表示原点へ相対化した光源位置を反映します。</summary>
        private void LateUpdate()
        {
            using (UpdateMarker.Auto()) UpdateLights();
        }

        /// <summary>LateUpdateの計測区間内で、選択更新と原点に対応する位置反映を行います。</summary>
        private void UpdateLights()
        {
            if (targetCamera == null || cameraController == null) return;
            int2 origin = cameraController.CurrentDisplayOrigin.AbsoluteTileCoordinate;
            Vector3 cameraPosition = targetCamera.transform.position;
            bool viewChanged = absoluteCenter.x != origin.x + (double)cameraPosition.x
                || absoluteCenter.y != origin.y + (double)cameraPosition.y
                || previousOrthographicSize != targetCamera.orthographicSize || previousAspect != targetCamera.aspect;
            // 地形の固定配置は再計算せず、移動フレームだけ可視範囲を更新します。高速移動でも照明の出現を遅らせません。
            if (dirty || viewChanged || Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
                dirty = false;
                SelectVisibleLights(origin);
            }
            // Floating Originのフレームは選択周期を待たず全光源を再配置し、旧相対位置へ光を残しません。
            for (int i = 0; i < lights.Count; i++)
            {
                Light2D light = lights[i];
                light.enabled = i < selected.Count && intensity > 0f;
                if (i >= selected.Count) continue;
                int2 absolute = selected[i];
                light.transform.position = new Vector3((float)((long)absolute.x - origin.x) + 0.5f,
                    (float)((long)absolute.y - origin.y) + 0.5f, 0f);
                light.color = lightColor;
                light.intensity = intensity;
                light.pointLightOuterRadius = Mathf.Max(0.1f, outerRadius);
                light.pointLightInnerRadius = Mathf.Clamp(innerRadius, 0f, light.pointLightOuterRadius);
            }
        }

        /// <summary>固定済み光源のうち、Camera可視矩形へ光が届くものをすべて表示します。</summary>
        /// <param name="origin">現在の表示原点です。</param>
        private void SelectVisibleLights(int2 origin)
        {
            Vector3 position = targetCamera.transform.position;
            absoluteCenter = new double2(origin.x + (double)position.x, origin.y + (double)position.y);
            previousOrthographicSize = targetCamera.orthographicSize;
            previousAspect = targetCamera.aspect;
            double halfHeight = targetCamera.orthographicSize + outerRadius;
            double halfWidth = targetCamera.orthographicSize * targetCamera.aspect + outerRadius;
            visibleCandidates.Clear();
            foreach (List<int2>[] chunks in candidates.Values)
                foreach (List<int2> cells in chunks)
                    if (cells != null)
                        foreach (int2 cell in cells)
                            if (Math.Abs(cell.x + 0.5 - absoluteCenter.x) <= halfWidth
                                && Math.Abs(cell.y + 0.5 - absoluteCenter.y) <= halfHeight) visibleCandidates.Add(cell);
            if (comparison == null) comparison = CompareCandidates;
            visibleCandidates.Sort(comparison);
            selected.Clear();
            // 灯数で近い岸だけを残すと、カメラを動かしたとき同じ地面への光も入れ替わります。
            // 密度は固定区画で制限済みなので、ここでは届く光源を落とさず、画面外だけを除外します。
            selected.AddRange(visibleCandidates);
            while (lights.Count < selected.Count)
            {
                var child = new GameObject("Magma Shore Light");
                child.transform.SetParent(transform, false);
                Light2D light = child.AddComponent<Light2D>();
                light.lightType = Light2D.LightType.Point;
                // 既存の全体照明を暗くせず光を足せるよう、製品Renderer2DのAdditive（index 1）を使います。
                light.blendStyleIndex = 1;
                light.targetSortingLayers = new[] { SortingLayer.NameToID("Default"), SortingLayer.NameToID("TerrainWall") };
                light.overlapOperation = Light2D.OverlapOperation.Additive;
                // 今回は検証済みの照り返しを採用します。動的投影影は別の照明仕様で扱うため有効にしません。
                light.shadowsEnabled = false;
                lights.Add(light);
            }
        }

        /// <summary>絶対座標順で並べ、カメラ位置やチャンクのロード順によって表示順が揺れるのを防ぎます。</summary>
        /// <param name="left">比較する左候補です。</param>
        /// <param name="right">比較する右候補です。</param>
        /// <returns>Y、Xの順に比較した結果です。</returns>
        private static int CompareCandidates(int2 left, int2 right)
        {
            int result = left.y.CompareTo(right.y);
            return result != 0 ? result : left.x.CompareTo(right.x);
        }

        /// <summary>無効化時は照明だけを消します。再有効化に備えて地形候補とプールは保持します。</summary>
        private void OnDisable()
        {
            foreach (Light2D light in lights) if (light != null) light.enabled = false;
            dirty = true;
        }

        /// <summary>Component破棄時に、借用していない所有光源だけを破棄します。</summary>
        private void OnDestroy()
        {
            foreach (Light2D light in lights)
                if (light != null)
                {
                    if (Application.isPlaying) Destroy(light.gameObject);
                    else DestroyImmediate(light.gameObject);
                }
        }
    }
}
