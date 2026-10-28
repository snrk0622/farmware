// このファイルは、水の基底色・水中断面と、マグマ奥岸の露出断面へ渡すセル固有の表示データを作ります。
// 岸からの垂直投影距離はその列だけの値です。隣列と平均したり断面を横へ延長したりしません。
// 地形の正はSimulationにあり、本処理はDirtyサブチャンクの描画反映時だけ使用します。
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>水種・床有無・投影段数を符号化し、Presenter所有のTilemapへ反映する表示専用処理です。</summary>
    public static class TerrainWaterRenderUtility
    {
        /// <summary>マグマ奥岸のGround断面を、上端3pxだけ不透明に残す専用のセルデータを返します。</summary>
        /// <returns>Alpha 0は断面Shader用のマグマ印です。水の奥岸印128/255・通常印1と区別します。</returns>
        public static Color EncodeMagmaBankFace()
        {
            // 透明度ではなく分類印です。Shaderは元SpriteのAlphaを維持し、液面より下だけを破棄します。
            return Color.clear;
        }

        /// <summary>同じTileの色だけの変更でも描画が更新されるよう、Dirty範囲を反映します。</summary>
        /// <param name="map">Presenterが所有する書込先です。</param>
        /// <param name="changes">Dirty領域の全セルを含む再利用配列です。呼出し後は保持しません。</param>
        public static void ApplyTiles(Tilemap map, TileChangeData[] changes)
        {
            // Unity 6000.3のSetTilesでは、同じTile参照の色だけを変更すると描画値が残る場合があります。
            // 全TilemapでなくDirty範囲だけを更新します。Refreshを後にするとLockColorがデータ色を白へ戻します。
            for (int index = 0; index < changes.Length; index++)
            {
                if (changes[index].tile != null) map.RefreshTile(changes[index].position);
            }
            map.SetTiles(changes, true);
        }

        /// <summary>水色と断面に必要な値をセル色へ符号化します。色そのものや隣列の距離は含めません。</summary>
        /// <param name="kind">淡水または海水です。</param>
        /// <param name="stage">その列の奥岸からの投影段数0〜6です。6は96px以深を表します。</param>
        /// <param name="hasFloor">直下床が存在する場合はtrueです。</param>
        /// <param name="clipTop">実際の奥岸に接する先頭セルだけtrueです。</param>
        /// <returns>Rは段数/6、Gは床有無、Bは海水印、Aは奥岸印です。Alphaを0にしません。</returns>
        public static Color EncodeCell(TerrainLiquidVisualKind kind, int stage, bool hasFloor, bool clipTop)
        {
            // Rの7段階はLinear色変換後も識別できる間隔を取り、Shaderで元の段数へ復元します。
            return new Color(Mathf.Clamp(stage, 0, 6) / 6f, hasFloor ? 1f : 0f,
                kind == TerrainLiquidVisualKind.SaltWater ? 1f : 0f, clipTop ? 128f / 255f : 1f);
        }
    }
}
