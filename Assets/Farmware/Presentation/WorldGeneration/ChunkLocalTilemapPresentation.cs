// このファイルは、1論理チャンクの表示専用Tilemap群を所有します。
// ChunkTilemapPresenterがECSから読み取った地形だけを書き込み、原点変更時にはセル内容を保ってルート位置だけを更新します。

using UnityEngine;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 64×64チャンク内のセル0〜63へ書くTilemapをまとめ、Presenterの有効表示と空きプールで再利用します。
    /// 地形の正ではなく、Presenterが生成・消去・破棄の責任を持つ表示キャッシュです。
    /// </summary>
    internal sealed class ChunkLocalTilemapPresentation
    {
        // 現行のXY平面を正面から見るOrthographic描画で、奥のチャンクを先に描くためのZ差です。
        // 大きい絶対Yは使わず、表示原点からの相対チャンク行だけへ掛けます。XYのセル位置やSimulation座標は変えません。
        private const float ChunkRowDepthStepInWorldUnits = 1f / 1024f;

        /// <summary>小さい相対座標へ置くチャンク表示ルートです。Presenterだけが位置と有効状態を変更します。</summary>
        public readonly GameObject Root;

        // Scene上の設定見本です。各複製Tilemapとの対応をこの表示単位の寿命中保持します。
        private readonly Tilemap[] templates;

        // チャンクローカルのセルだけを持つ実行時Tilemapです。素材とMaterialは見本と共有し、複製しません。
        private readonly Tilemap[] tilemaps;

        /// <summary>
        /// 初めてプールが不足した際に呼び、Sceneの各レイヤー設定を複製した空の表示単位を作ります。
        /// Tilemap以外のComponentやセル内容を複製しないため、Presenterの二重作成を防ぎます。
        /// </summary>
        /// <param name="sourceTilemaps">Scene上の各レイヤー見本です。任意レイヤーのnullも保持します。</param>
        public ChunkLocalTilemapPresentation(Tilemap[] sourceTilemaps)
        {
            templates = sourceTilemaps;
            tilemaps = new Tilemap[templates.Length];
            Root = new GameObject("Chunk Local Terrain", typeof(Grid));
            Root.hideFlags = HideFlags.DontSave;

            // 座標表示に使うWorld Gridは動かさず、その子に表示専用Gridを置きます。
            // 独立したGridを持たせることで、セル0〜63の意味が他チャンクのTransformへ依存しません。
            Grid sourceGrid = templates[0].layoutGrid;
            Root.transform.SetParent(sourceGrid.transform, false);
            Grid grid = Root.GetComponent<Grid>();
            grid.cellSize = sourceGrid.cellSize;
            grid.cellGap = sourceGrid.cellGap;
            grid.cellLayout = sourceGrid.cellLayout;
            grid.cellSwizzle = sourceGrid.cellSwizzle;

            for (int index = 0; index < templates.Length; index++)
            {
                Tilemap source = templates[index];
                if (source == null)
                {
                    continue;
                }

                GameObject layer = new GameObject(source.name, typeof(Tilemap), typeof(TilemapRenderer));
                layer.transform.SetParent(Root.transform, false);
                layer.transform.localPosition = source.transform.localPosition;
                layer.transform.localRotation = source.transform.localRotation;
                layer.transform.localScale = source.transform.localScale;
                layer.layer = source.gameObject.layer;
                Tilemap tilemap = layer.GetComponent<Tilemap>();
                tilemap.tileAnchor = source.tileAnchor;
                tilemap.orientation = source.orientation;
                tilemap.orientationMatrix = source.orientationMatrix;
                tilemap.color = source.color;
                tilemap.animationFrameRate = source.animationFrameRate;
                tilemaps[index] = tilemap;

                // 固定レイヤー順、Individual/TopLeft、はみ出しカリングを見本と同じに保ちます。
                // チャンク全体をSortingGroupで束ねると行同士の重なりが変わるため、SortingGroupは追加しません。
                TilemapRenderer sourceRenderer = source.GetComponent<TilemapRenderer>();
                TilemapRenderer renderer = layer.GetComponent<TilemapRenderer>();
                if (sourceRenderer != null)
                {
                    renderer.sharedMaterials = sourceRenderer.sharedMaterials;
                    renderer.sortingLayerID = sourceRenderer.sortingLayerID;
                    renderer.sortingOrder = sourceRenderer.sortingOrder;
                    renderer.mode = sourceRenderer.mode;
                    renderer.sortOrder = sourceRenderer.sortOrder;
                    renderer.maskInteraction = sourceRenderer.maskInteraction;
                    renderer.detectChunkCullingBounds = sourceRenderer.detectChunkCullingBounds;
                    renderer.chunkCullingBounds = sourceRenderer.chunkCullingBounds;
                }
            }
        }

        /// <summary>
        /// サブチャンク反映・消去時に、Scene見本へ対応するチャンク内Tilemapを返します。セルや状態は変更しません。
        /// </summary>
        /// <param name="template">Presenterが従来使用していたScene上のTilemapです。</param>
        /// <returns>対応する表示用Tilemapです。任意レイヤーが未設定ならnullです。</returns>
        public Tilemap GetTilemap(Tilemap template)
        {
            for (int index = 0; index < templates.Length; index++)
            {
                if (templates[index] == template)
                {
                    return tilemaps[index];
                }
            }

            return null;
        }

        /// <summary>
        /// 貸し出し時と原点変更時に、絶対チャンク位置から求めた小さい相対セルへ表示ルートだけを配置します。
        /// </summary>
        /// <param name="relativeMinimum">World Grid上でのチャンク左下の相対セルです。</param>
        public void Reposition(Vector3Int relativeMinimum)
        {
            Grid parentGrid = Root.transform.parent.GetComponent<Grid>();
            Vector3 position = parentGrid.CellToLocal(relativeMinimum);

            // 同じSorting Orderの別Tilemap間では、TopLeftだけでは上下チャンクの描画順を保証できません。
            // 現行URP 2DのOrthographic距離ソートへ小さいZ差を与え、画面奥の行を先、手前の行を後に描きます。
            // レイヤー間は従来のSorting Orderが優先するため、液面・土地・岩壁・影・資源の前後関係は保ちます。
            position.z += (relativeMinimum.y / (float)WorldGridConstants.LogicalChunkSize)
                * ChunkRowDepthStepInWorldUnits;
            Root.transform.localPosition = position;
        }

        /// <summary>
        /// 画面外になった表示を非表示にし、指定サブチャンクだけを空配列で消去します。配列はPresenterと共有します。
        /// </summary>
        /// <param name="bounds">チャンク内セルで指定した16×16の消去範囲です。</param>
        /// <param name="emptyTiles">全要素がnullである256要素の再利用配列です。</param>
        public void ClearSubChunk(BoundsInt bounds, TileBase[] emptyTiles)
        {
            Root.SetActive(false);
            for (int index = 0; index < tilemaps.Length; index++)
            {
                if (tilemaps[index] != null)
                {
                    tilemaps[index].SetTilesBlock(bounds, emptyTiles);
                }
            }
        }

        /// <summary>
        /// Presenter破棄時または空きプール上限超過時に、所有する表示ルートと全レイヤーを破棄します。
        /// 共有Sprite・Tile・Materialはこの表示単位の所有物ではないため破棄しません。
        /// </summary>
        public void Destroy()
        {
            Root.SetActive(false);
            if (Application.isPlaying)
            {
                Object.Destroy(Root);
            }
            else
            {
                Object.DestroyImmediate(Root);
            }
        }
    }
}
