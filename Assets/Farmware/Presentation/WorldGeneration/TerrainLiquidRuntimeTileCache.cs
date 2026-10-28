// このファイルは、静止水際と液体深度Overlayに使う実行時Tileを少数の共有Objectへ変換します。
// ChunkTilemapPresenterが所有・破棄し、液体セルごとのScriptableObjectやGameObject生成を防ぎます。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 水際色・7bit形状と深度用の白Spriteを共有Tileとしてキャッシュします。
    /// Project内のSprite、Tile、Materialは借用し、このクラスが作成した一時Objectだけを破棄します。
    /// </summary>
    internal sealed class TerrainLiquidRuntimeTileCache : IDisposable
    {
        /// <summary>基礎液面Tileと岸側クリップ有無から共有Tileを引く検索表です。</summary>
        private readonly Dictionary<long, Tile> surfaceTiles = new Dictionary<long, Tile>();

        /// <summary>全水セルで共有する白い深度Tileです。距離はセル色へ渡し、組合せ別Tileを作りません。</summary>
        private Tile depthTile;

        /// <summary>水際色と7bit形状を共有する静止Tileです。セル数ではなく色×形状数にだけ比例します。</summary>
        private readonly Dictionary<uint, Tile> shorelineTiles = new Dictionary<uint, Tile>();

        /// <summary>深度Overlayと水際の全面描画に共有する1×1白Textureです。</summary>
        private Texture2D whiteTexture;

        /// <summary>1×1白Textureを1セル全面へ拡大する共有Spriteです。</summary>
        private Sprite whiteSprite;

        /// <summary>現在保持している基礎液面Tile数です。液体種ごとに通常・岸側の最大2件です。</summary>
        public int SurfaceTileCount => surfaceTiles.Count;

        /// <summary>共有深度Tileの数です。距離の組合せによらず最大1件です。</summary>
        public int DepthTileCount => depthTile == null ? 0 : 1;

        /// <summary>現在保持する水際Tile数です。固定3色なら上限381件で、内部セル用には生成しません。</summary>
        public int ShorelineTileCount => shorelineTiles.Count;

        /// <summary>RGBへ接触線色、Alphaへ7bit形状を格納した全面Sprite付き静止Tileを共有します。</summary>
        /// <param name="color">製品カタログの水際RGB色です。見た目のAlphaはShaderが決めます。</param>
        /// <param name="mask">有効地形から求めた接触形状です。</param>
        /// <returns>共有水際Tileです。内部セルまたは無効なマスクならnullです。</returns>
        public TileBase GetOrCreateShoreline(Color color, TerrainLiquidEdgeMask mask)
        {
            if (mask == TerrainLiquidEdgeMask.None || (int)mask > TerrainLiquidPresentationUtility.MaximumEdgeMask)
            {
                return null;
            }

            EnsureWhiteSprite();
            Color32 encoded = color;
            encoded.a = (byte)mask;
            uint key = PackColor(encoded);
            if (shorelineTiles.TryGetValue(key, out Tile existing))
            {
                return existing;
            }

            // 一枚の白Spriteを全形状で共有し、Shaderがセル内座標と形状bitから必要な1pxだけを描きます。
            Tile created = ScriptableObject.CreateInstance<Tile>();
            created.name = $"Runtime Liquid Shoreline {key:X8}";
            created.hideFlags = HideFlags.DontSave;
            created.sprite = whiteSprite;
            created.color = encoded;
            created.colliderType = Tile.ColliderType.None;
            created.flags = TileFlags.LockColor | TileFlags.LockTransform;
            shorelineTiles.Add(key, created);
            return created;
        }

        /// <summary>
        /// 基礎液面のSpriteとRGB Tintを共有し、岸側セルだけAlpha 0をShaderへのクリップ印として設定します。
        /// </summary>
        /// <param name="source">カタログへ登録したSprite付き静止Tileです。</param>
        /// <param name="clipSurfaceTop">画面奥側が岸へ接し、上端3pxを隠す場合はtrueです。</param>
        /// <returns>全チャンクで共有する通常または岸側クリップ用Tileです。</returns>
        public TileBase GetOrCreateSurface(Tile source, bool clipSurfaceTop)
        {
            if (source == null || source.sprite == null)
            {
                return null;
            }

            long key = ((long)source.GetInstanceID() << 1) | (clipSurfaceTop ? 1L : 0L);
            if (surfaceTiles.TryGetValue(key, out Tile existing))
            {
                return existing;
            }

            Tile created = ScriptableObject.CreateInstance<Tile>();
            created.name = $"Runtime Liquid Surface {source.name} {(clipSurfaceTop ? "Shore" : "Interior")}";
            created.hideFlags = HideFlags.DontSave;
            created.sprite = source.sprite;
            created.color = new Color(
                source.color.r,
                source.color.g,
                source.color.b,
                clipSurfaceTop ? 0f : 1f);
            created.transform = source.transform;
            created.colliderType = Tile.ColliderType.None;
            created.flags = TileFlags.LockColor | TileFlags.LockTransform;
            surfaceTiles.Add(key, created);
            return created;
        }

        /// <summary>白い全面深度Tileを1件だけ作り、全チャンクで共有します。</summary>
        /// <returns>深度データをセル色で受け取る、ColliderなしTileです。</returns>
        public TileBase GetOrCreateDepthOverlay()
        {
            if (depthTile != null) return depthTile;
            EnsureWhiteSprite();
            depthTile = ScriptableObject.CreateInstance<Tile>();
            depthTile.name = "Runtime Water Depth Field";
            depthTile.hideFlags = HideFlags.DontSave;
            depthTile.sprite = whiteSprite;
            depthTile.color = Color.white;
            depthTile.colliderType = Tile.ColliderType.None;
            depthTile.flags = TileFlags.LockTransform;
            return depthTile;
        }


        /// <summary>このキャッシュが生成したTile、白Sprite、白Textureだけを破棄します。</summary>
        public void Dispose()
        {
            foreach (Tile tile in surfaceTiles.Values)
            {
                DestroyUnityObject(tile);
            }

            DestroyUnityObject(depthTile);
            depthTile = null;


            foreach (Tile tile in shorelineTiles.Values)
            {
                DestroyUnityObject(tile);
            }

            surfaceTiles.Clear();

            shorelineTiles.Clear();
            DestroyUnityObject(whiteSprite);
            DestroyUnityObject(whiteTexture);
            whiteSprite = null;
            whiteTexture = null;
        }

        /// <summary>深度Overlayと水際へ使う白い共有Spriteを初回要求時だけ作ります。</summary>
        private void EnsureWhiteSprite()
        {
            if (whiteSprite != null)
            {
                return;
            }

            whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "Runtime Liquid Depth White Texture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            whiteTexture.SetPixel(0, 0, Color.white);
            whiteTexture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            whiteSprite = Sprite.Create(
                whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 1f,
                extrude: 0,
                SpriteMeshType.FullRect);
            whiteSprite.name = "Runtime Liquid Depth White Sprite";
            whiteSprite.hideFlags = HideFlags.DontSave;
        }

        /// <summary>RGBA 8bitをDictionaryキー用の32bit値へ詰めます。</summary>
        /// <param name="color">量子化済みのTile Tintです。</param>
        /// <returns>同じRGBAで必ず同じ値です。</returns>
        private static uint PackColor(Color32 color)
        {
            return (uint)(color.r
                | (color.g << 8)
                | (color.b << 16)
                | (color.a << 24));
        }

        /// <summary>PlayModeとEditModeのどちらでも所有Objectを適切なUnity APIで破棄します。</summary>
        /// <param name="value">このキャッシュが作成したObjectです。</param>
        private static void DestroyUnityObject(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(value);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }

    }
}
