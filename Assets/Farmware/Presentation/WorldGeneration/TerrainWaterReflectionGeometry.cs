// 反射元の接地点から垂直Spriteを反転投影し、水面と重なる部分だけを求めます。
// Wallの探索や描画Objectの寿命とは独立した純粋計算なので、将来の木・設備にも同じ規則を使えます。
using UnityEngine;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>水面で切り抜いた反射1枚の位置、元画像の読取範囲、接地点からの深度です。</summary>
    public readonly struct TerrainWaterReflectionSlice
    {
        /// <summary>水面と交差する投影先です。単位はタイルで、計算中だけ保持します。</summary>
        public readonly Rect Destination;
        /// <summary>元Sprite基準の正規化範囲です。横余白は0〜1を越え、下端は反射上端、上端は反射下端へ対応します。</summary>
        public readonly Rect Source;
        /// <summary>投影先の下端・上端の順で保持する、反射面からの距離（px）です。</summary>
        public readonly Vector2 DepthPixels;
        /// <summary>対象の高さ・設定から求めた減衰終端（px）です。水面セルごとに開始し直しません。</summary>
        public readonly float MaximumDepthPixels;

        /// <summary>投影計算が成功したときに、その結果を値としてまとめます。外部状態は変更しません。</summary>
        /// <param name="destination">タイル単位の描画矩形です。</param>
        /// <param name="source">元画像基準の正規化範囲です。横揺れの余白では0〜1を越えます。</param>
        /// <param name="depthPixels">下端・上端の深度（px）です。</param>
        /// <param name="maximumDepthPixels">対象ごとの減衰終端（px）です。</param>
        public TerrainWaterReflectionSlice(Rect destination, Rect source, Vector2 depthPixels, float maximumDepthPixels)
        {
            Destination = destination;
            Source = source;
            DepthPixels = depthPixels;
            MaximumDepthPixels = maximumDepthPixels;
        }
    }

    /// <summary>対象種別や岸の隣接セル数によらず、接地点・高さ・水面の交差から反射範囲を決めます。</summary>
    public static class TerrainWaterReflectionGeometry
    {
        /// <summary>垂直画像を接地点基準で反転・圧縮し、水域と最大深度で切り抜きます。</summary>
        /// <param name="sourceBounds">反射元画像のタイル単位の配置範囲です。</param>
        /// <param name="contactY">反射元の接地点Yです。対象を水際へ移動する補正には使いません。</param>
        /// <param name="planeY">投影像の起点Yです。Wallでは接地点から水面高さ差3pxを引いた位置です。</param>
        /// <param name="waterBounds">実際に水面が見えるタイル単位の矩形です。</param>
        /// <param name="compression">垂直圧縮率です。正の有限値を指定します。</param>
        /// <param name="maximumDepthPixels">投影の最大長（px）です。高さによる上限と併用します。</param>
        /// <param name="slice">成功時の描画位置と画像読取範囲です。</param>
        /// <param name="horizontalPaddingPixels">輪郭の横揺れに備える両側の余白（px）です。水域の範囲は広げません。</param>
        /// <returns>正の面積で交差する場合だけtrueです。SceneやSimulationへ副作用はありません。</returns>
        public static bool TryProjectToWater(Rect sourceBounds, float contactY, float planeY,
            Rect waterBounds, float compression, float maximumDepthPixels, out TerrainWaterReflectionSlice slice,
            float horizontalPaddingPixels = 0f)
        {
            slice = default;
            if (!(sourceBounds.width > 0f && sourceBounds.height > 0f && compression > 0f
                && maximumDepthPixels > 0f) || !IsFinite(sourceBounds.xMin) || !IsFinite(sourceBounds.xMax)
                || !IsFinite(sourceBounds.yMin) || !IsFinite(sourceBounds.yMax) || !IsFinite(contactY)
                || !IsFinite(planeY) || !IsFinite(compression) || !IsFinite(maximumDepthPixels)
                || !IsFinite(horizontalPaddingPixels) || horizontalPaddingPixels < 0f
                || !IsFinite(waterBounds.xMin) || !IsFinite(waterBounds.xMax)
                || !IsFinite(waterBounds.yMin) || !IsFinite(waterBounds.yMax)) return false;

            // 接地点より下を反射せず、画像の高さに応じた投影を先に作ります。水際へ吸着させません。
            float top = Mathf.Min(planeY, planeY - (sourceBounds.yMin - contactY) * compression);
            float bottom = Mathf.Max(planeY - maximumDepthPixels / TerrainLiquidPresentationUtility.PixelsPerCell,
                planeY - (sourceBounds.yMax - contactY) * compression);
            // 元画像側だけに余白を用意し、横へ動いた輪郭も実際の水域内で描けるようにします。
            // この余白ではUVが0〜1を越えます。Shaderが変位後のUVを検査し、Atlasの隣画像は読みません。
            float padding = horizontalPaddingPixels / TerrainLiquidPresentationUtility.PixelsPerCell;
            float left = Mathf.Max(sourceBounds.xMin - padding, waterBounds.xMin);
            float right = Mathf.Min(sourceBounds.xMax + padding, waterBounds.xMax);
            top = Mathf.Min(top, waterBounds.yMax);
            bottom = Mathf.Max(bottom, waterBounds.yMin);
            if (!(right > left && top > bottom)) return false;

            // 切り抜いた位置から元画像へ逆変換し、複数セルでも画像と減衰を開始し直さないようにします。
            Rect source = Rect.MinMaxRect((left - sourceBounds.xMin) / sourceBounds.width,
                ((planeY - top) / compression + contactY - sourceBounds.yMin) / sourceBounds.height,
                (right - sourceBounds.xMin) / sourceBounds.width,
                ((planeY - bottom) / compression + contactY - sourceBounds.yMin) / sourceBounds.height);
            slice = new TerrainWaterReflectionSlice(Rect.MinMaxRect(left, bottom, right, top), source,
                new Vector2((planeY - bottom) * TerrainLiquidPresentationUtility.PixelsPerCell,
                    (planeY - top) * TerrainLiquidPresentationUtility.PixelsPerCell), maximumDepthPixels);
            return true;
        }

        /// <summary>無効値がMeshへ入らないよう、有限値であることを検査します。</summary>
        /// <param name="value">検査するタイル位置または倍率です。</param>
        /// <returns>NaNでも無限大でもない場合はtrueです。</returns>
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
