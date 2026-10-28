// このファイルは、製品地形表示で使うSpriteとTintの組を再利用可能なTileへ変換します。
// ChunkTilemapPresenterが所有・破棄し、材質SpriteやUnity Objectをセルごとに生成しないためのRuntime専用キャッシュです。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// SpriteとColor32が同じTileを全チャンクで共有し、Presenter終了時に生成物だけを破棄します。
    /// Project内のSprite、Material、Tileアセットの所有権は持ちません。
    /// </summary>
    internal sealed class TerrainMaterialRuntimeTileCache : IDisposable
    {
        /// <summary>Sprite参照と8bit Tintをキーにして、同じ表示候補のTileを1つだけ保持します。</summary>
        private readonly Dictionary<TileKey, Tile> tiles = new Dictionary<TileKey, Tile>();

        /// <summary>深部黒を1セル全面へ描くために、このキャッシュが所有する1×1 Textureです。</summary>
        private Texture2D deepBlackTexture;

        /// <summary>1×1 Textureを1ワールド単位へ割り当てた、このキャッシュ所有のSpriteです。</summary>
        private Sprite deepBlackSprite;

        /// <summary>4段目以降の乾燥穴で全セルが共有する黒Tileです。</summary>
        private Tile deepBlackTile;

        /// <summary>現在キャッシュしているSprite・Tint組の数です。セル数には比例しません。</summary>
        public int Count => tiles.Count;

        /// <summary>SpriteとTintの組を検索し、初回だけColliderなしの固定Tileを作ります。</summary>
        /// <param name="selection">製品カタログから解決済みのSpriteとTintです。</param>
        /// <returns>全チャンクで共有するTile、またはSprite未設定ならnullです。</returns>
        public TileBase GetOrCreate(TerrainMaterialProductTileSelection selection)
        {
            if (selection.Sprite == null)
            {
                return null;
            }

            Color32 color = selection.Color;
            var key = new TileKey(selection.Sprite, color);
            if (tiles.TryGetValue(key, out Tile existing))
            {
                return existing;
            }

            Tile created = ScriptableObject.CreateInstance<Tile>();
            created.name = $"Runtime Terrain {selection.Sprite.name} {color.r:X2}{color.g:X2}{color.b:X2}{color.a:X2}";
            created.hideFlags = HideFlags.DontSave;
            created.sprite = selection.Sprite;
            created.color = color;
            created.colliderType = Tile.ColliderType.None;
            created.flags = TileFlags.LockColor | TileFlags.LockTransform;
            tiles.Add(key, created);
            return created;
        }

        /// <summary>乾燥穴4段目以降へ使う完全不透明の黒Tileを、キャッシュ寿命中1つだけ返します。</summary>
        /// <returns>1セル全面を覆うColliderなしの共有Tileです。</returns>
        public TileBase GetOrCreateDeepBlackTile()
        {
            if (deepBlackTile != null)
            {
                return deepBlackTile;
            }

            deepBlackTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "Runtime Terrain Deep Black Texture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            deepBlackTexture.SetPixel(0, 0, Color.black);
            deepBlackTexture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            deepBlackSprite = Sprite.Create(
                deepBlackTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 1f,
                extrude: 0,
                SpriteMeshType.FullRect);
            deepBlackSprite.name = "Runtime Terrain Deep Black Sprite";
            deepBlackSprite.hideFlags = HideFlags.DontSave;

            deepBlackTile = ScriptableObject.CreateInstance<Tile>();
            deepBlackTile.name = "Runtime Terrain Deep Black Tile";
            deepBlackTile.hideFlags = HideFlags.DontSave;
            deepBlackTile.sprite = deepBlackSprite;
            deepBlackTile.color = Color.white;
            deepBlackTile.colliderType = Tile.ColliderType.None;
            deepBlackTile.flags = TileFlags.LockColor | TileFlags.LockTransform;
            return deepBlackTile;
        }

        /// <summary>Presenterが生成したTile、黒Sprite、黒Textureだけを破棄します。</summary>
        public void Dispose()
        {
            foreach (Tile tile in tiles.Values)
            {
                DestroyUnityObject(tile);
            }

            tiles.Clear();
            DestroyUnityObject(deepBlackTile);
            DestroyUnityObject(deepBlackSprite);
            DestroyUnityObject(deepBlackTexture);
            deepBlackTile = null;
            deepBlackSprite = null;
            deepBlackTexture = null;
        }

        /// <summary>PlayModeとEditModeのどちらでも、キャッシュ所有Objectを適切なUnity APIで破棄します。</summary>
        /// <param name="value">このキャッシュが生成したObjectです。</param>
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

        /// <summary>Unity Object参照とColor32を値比較できるDictionaryキーです。</summary>
        private readonly struct TileKey : IEquatable<TileKey>
        {
            /// <summary>同一Spriteを判定するUnity Instance IDです。</summary>
            private readonly int spriteInstanceId;

            /// <summary>RGBA 8bitを比較用32bit値へ詰めたTintです。</summary>
            private readonly uint packedColor;

            /// <summary>Sprite参照とTintから再利用キーを作ります。</summary>
            /// <param name="sprite">nullではない表示Spriteです。</param>
            /// <param name="color">Tileへ保存する8bit Tintです。</param>
            public TileKey(Sprite sprite, Color32 color)
            {
                spriteInstanceId = sprite.GetInstanceID();
                packedColor = (uint)(color.r
                    | (color.g << 8)
                    | (color.b << 16)
                    | (color.a << 24));
            }

            /// <summary>Sprite Instance IDとRGBAがすべて同じか比較します。</summary>
            /// <param name="other">比較する別キーです。</param>
            /// <returns>同じ再利用Tileを表す場合はtrueです。</returns>
            public bool Equals(TileKey other)
            {
                return spriteInstanceId == other.spriteInstanceId
                    && packedColor == other.packedColor;
            }

            /// <summary>Object経由でも同じ型のキーを値比較します。</summary>
            /// <param name="obj">比較対象です。</param>
            /// <returns>同じTileKeyならtrueです。</returns>
            public override bool Equals(object obj)
            {
                return obj is TileKey other && Equals(other);
            }

            /// <summary>Dictionary検索用にSpriteとRGBAを混合したハッシュ値を返します。</summary>
            /// <returns>同じキーで必ず同じ32bit値です。</returns>
            public override int GetHashCode()
            {
                unchecked
                {
                    return (spriteInstanceId * 397) ^ (int)packedColor;
                }
            }
        }
    }
}
