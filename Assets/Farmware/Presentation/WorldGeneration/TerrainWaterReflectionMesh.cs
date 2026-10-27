// Dirtyなサブチャンクの反射断片をTexture単位のMeshへまとめます。
// 1波・1セルごとのGameObjectは作らず、描画の設定見本とMaterialはPresenterから借用します。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>1チャンクの16サブチャンク分の反射Meshを所有し、Dirty更新・原点移動・破棄を受け付けます。</summary>
    internal sealed class TerrainWaterReflectionMesh : IDisposable
    {
        /// <summary>親Gridへ相対配置する所有Rootです。Presenterが原点変更時に移動します。</summary>
        private readonly GameObject root;
        /// <summary>サブチャンク番号とTextureの組で共有する描画バッチです。</summary>
        private readonly Dictionary<(int, Texture2D), Batch> batches = new Dictionary<(int, Texture2D), Batch>();
        /// <summary>毎回確保せず、Uploadへ流す頂点配列群を所有するバッチです。</summary>
        private sealed class Batch
        {
            /// <summary>このバッチだけが所有し、破棄する描画Objectです。</summary>
            public GameObject Object;
            /// <summary>このバッチだけが所有する動的Meshです。</summary>
            public Mesh Mesh;
            /// <summary>チャンク内のタイル位置です。Dirty更新中に再利用します。</summary>
            public readonly List<Vector3> Positions = new List<Vector3>();
            /// <summary>元Sprite基準のUVです。横揺れ用の余白も保持し、変位後の読取範囲はShaderが制限します。</summary>
            public readonly List<Vector2> Uvs = new List<Vector2>();
            /// <summary>Atlas内で元Spriteだけを読むためのUV境界です。</summary>
            public readonly List<Vector4> Bounds = new List<Vector4>();
            /// <summary>反射元からの減衰深度・終端・受け手水面からの揺れ深度（px）です。</summary>
            public readonly List<Vector3> Depths = new List<Vector3>();
            /// <summary>四角形を三角形へ分ける頂点番号です。</summary>
            public readonly List<int> Indices = new List<int>();
        }

        /// <summary>チャンクに反射が必要になったときだけ、所有Rootを作成します。</summary>
        /// <param name="parent">表示原点が管理されるWorld Gridです。</param>
        public TerrainWaterReflectionMesh(Transform parent)
        {
            root = new GameObject("Water Reflection Projection") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(parent, false);
        }

        /// <summary>チャンク位置を更新します。頂点データや模様の抽選は変更しません。</summary>
        /// <param name="position">Gridから見た原点相対のチャンク位置です。</param>
        public void Reposition(Vector3 position) => root.transform.localPosition = position;

        /// <summary>Dirtyサブチャンクの旧頂点を空にし、次のAppendへ備えます。</summary>
        /// <param name="subChunk">0〜15の更新対象です。</param>
        public void Begin(int subChunk)
        {
            foreach (var pair in batches)
            {
                if (pair.Key.Item1 != subChunk) continue;
                Batch b = pair.Value;
                b.Positions.Clear(); b.Uvs.Clear(); b.Bounds.Clear(); b.Depths.Clear(); b.Indices.Clear();
            }
        }

        /// <summary>水域で切り抜いた反射を、同じTextureの描画バッチへ追加します。</summary>
        /// <param name="subChunk">Dirty対象の番号です。</param>
        /// <param name="sprite">借用するFullRect・回転なしの元画像です。</param>
        /// <param name="slice">接地点から計算した位置・読取範囲です。</param>
        /// <param name="settings">MaterialとSorting設定を借用する見本Rendererです。</param>
        /// <param name="waterSurfaceY">受け手の実水面の高さです。省略時は投影起点を使います。</param>
        public void Append(int subChunk, Sprite sprite, TerrainWaterReflectionSlice slice, Renderer settings,
            float waterSurfaceY = float.NaN)
        {
            var key = (subChunk, sprite.texture);
            if (!batches.TryGetValue(key, out Batch b))
            {
                var obj = new GameObject("Water Reflection Batch", typeof(MeshFilter), typeof(MeshRenderer));
                obj.hideFlags = HideFlags.DontSave;
                obj.layer = settings.gameObject.layer;
                obj.transform.SetParent(root.transform, false);
                var mesh = new Mesh { name = "Runtime Water Reflection Mesh", hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
                obj.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = obj.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = settings.sharedMaterial;
                renderer.sortingLayerID = settings.sortingLayerID;
                renderer.sortingOrder = settings.sortingOrder;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                // MaterialをTexture別に複製せず、バッチの借用Textureだけを上書きします。
                var block = new MaterialPropertyBlock();
                block.SetTexture("_MainTex", sprite.texture);
                block.SetVector("_MainTex_TexelSize", new Vector4(1f / sprite.texture.width,
                    1f / sprite.texture.height, sprite.texture.width, sprite.texture.height));
                renderer.SetPropertyBlock(block);
                b = new Batch { Object = obj, Mesh = mesh };
                batches.Add(key, b);
            }

            // textureRectはAtlasの実配置を使います。移動後UVもこの矩形の外へは読ませません。
            Rect r = sprite.textureRect;
            Vector4 uvBounds = new Vector4(r.xMin / sprite.texture.width, r.yMin / sprite.texture.height,
                r.xMax / sprite.texture.width, r.yMax / sprite.texture.height);
            Rect d = slice.Destination;
            Rect s = slice.Source;
            // 同じ受け手画素には、反射元が左右のどちらでも同じ変位を使います。
            // 反射元ごとの高さで揺れを弱めると、岸の段差で隣り合う像が重複したり裂けたりするためです。
            float surfaceY = float.IsNaN(waterSurfaceY)
                ? d.yMax + slice.DepthPixels.y / TerrainLiquidPresentationUtility.PixelsPerCell : waterSurfaceY;
            int start = b.Positions.Count;
            b.Positions.Add(new Vector3(d.xMin, d.yMin)); b.Positions.Add(new Vector3(d.xMax, d.yMin));
            b.Positions.Add(new Vector3(d.xMax, d.yMax)); b.Positions.Add(new Vector3(d.xMin, d.yMax));
            b.Uvs.Add(MapUv(s.xMin, s.yMax, uvBounds)); b.Uvs.Add(MapUv(s.xMax, s.yMax, uvBounds));
            b.Uvs.Add(MapUv(s.xMax, s.yMin, uvBounds)); b.Uvs.Add(MapUv(s.xMin, s.yMin, uvBounds));
            for (int i = 0; i < 4; i++)
            {
                b.Bounds.Add(uvBounds);
                b.Depths.Add(new Vector3(i < 2 ? slice.DepthPixels.x : slice.DepthPixels.y,
                    slice.MaximumDepthPixels,
                    (surfaceY - (i < 2 ? d.yMin : d.yMax)) * TerrainLiquidPresentationUtility.PixelsPerCell));
            }
            b.Indices.Add(start); b.Indices.Add(start + 1); b.Indices.Add(start + 2);
            b.Indices.Add(start); b.Indices.Add(start + 2); b.Indices.Add(start + 3);
        }

        /// <summary>Dirty対象のMeshだけGPUへ送り、空になったバッチを非表示にします。</summary>
        /// <param name="subChunk">更新対象の番号です。</param>
        public void Upload(int subChunk)
        {
            foreach (var pair in batches)
            {
                if (pair.Key.Item1 != subChunk) continue;
                Batch b = pair.Value;
                b.Mesh.Clear();
                b.Object.SetActive(b.Positions.Count > 0);
                if (b.Positions.Count == 0) continue;
                b.Mesh.SetVertices(b.Positions); b.Mesh.SetUVs(0, b.Uvs);
                b.Mesh.SetUVs(1, b.Bounds); b.Mesh.SetUVs(2, b.Depths);
                b.Mesh.SetTriangles(b.Indices, 0); b.Mesh.RecalculateBounds();
            }
        }

        /// <summary>正規化した元Sprite内座標をAtlas UVへ変換します。</summary>
        /// <param name="x">Sprite内の横位置です。</param>
        /// <param name="y">Sprite内の縦位置です。</param>
        /// <param name="bounds">Atlas内の元Sprite矩形です。</param>
        /// <returns>借用TextureのUVです。</returns>
        private static Vector2 MapUv(float x, float y, Vector4 bounds) =>
            new Vector2(Mathf.LerpUnclamped(bounds.x, bounds.z, x), Mathf.LerpUnclamped(bounds.y, bounds.w, y));

        /// <summary>チャンク消去時に所有MeshとObjectだけを破棄します。共有MaterialとSpriteは破棄しません。</summary>
        public void Dispose()
        {
            foreach (Batch b in batches.Values) Destroy(b.Mesh);
            batches.Clear();
            Destroy(root);
        }

        /// <summary>実行状態に合わせ、所有Objectを破棄します。</summary>
        /// <param name="value">このクラスが作ったMeshまたはRootです。</param>
        private static void Destroy(UnityEngine.Object value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
