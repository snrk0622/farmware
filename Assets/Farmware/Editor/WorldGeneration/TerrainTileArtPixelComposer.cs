// このファイルは、C6-Aの地形材質入力を16×16 pxの完成画素へ合成する純粋処理を提供します。
// AssetDatabaseやTextureImporterへ依存しないため、接続Mask、反転、暗色化、Overlay合成をEditModeテストで直接検証できます。

using System;
using Farmware.WorldGeneration.Authoring;
using UnityEngine;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// Wallセルの上下左右に、同じ材質のWallが接続している方向を4bitで表します。
    /// Runtime実装時も同じbit順を使い、生成ファイル名のMaskと表示判定を一致させます。
    /// </summary>
    [Flags]
    public enum TerrainWallConnectionMask
    {
        /// <summary>上下左右のどこにも同材質Wallがなく、四辺すべてを表示する状態です。</summary>
        None = 0,

        /// <summary>上側へ同材質Wallがあり、上辺を隠すbitです。</summary>
        Up = 1 << 0,

        /// <summary>右側へ同材質Wallがあり、右辺を隠すbitです。</summary>
        Right = 1 << 1,

        /// <summary>下側へ同材質Wallがあり、下辺を隠すbitです。</summary>
        Down = 1 << 2,

        /// <summary>左側へ同材質Wallがあり、左辺を隠すbitです。</summary>
        Left = 1 << 3,

        /// <summary>全方向を同材質Wallに囲まれ、セル全体を共通暗色にする状態です。</summary>
        All = Up | Right | Down | Left
    }

    /// <summary>
    /// 16×16 pxのColor32配列を使い、WallTopと断面1段目の完成画素を決定論的に作ります。
    /// 配列の原点はTexture2D.GetPixels32と同じ左下で、添字はy×16+xです。
    /// </summary>
    public static class TerrainTileArtPixelComposer
    {
        /// <summary>1枚の地形材質Spriteに含まれる固定画素数です。</summary>
        public const int PixelCount =
            TerrainTileArtGenerationSettings.TileSizeInPixels
            * TerrainTileArtGenerationSettings.TileSizeInPixels;

        /// <summary>
        /// sRGBの8bit RGBへ同じ係数を乗算し、最も近い整数へ丸めます。
        /// Alphaは透明・不透明の意味を維持するため変更しません。
        /// </summary>
        /// <param name="source">暗色化する元の8bit RGBAです。</param>
        /// <param name="factor">0より大きく1以下のRGB乗算係数です。</param>
        /// <returns>Alphaを維持し、RGBだけを暗色化した色です。</returns>
        public static Color32 Darken(Color32 source, float factor)
        {
            if (factor <= 0f || factor > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(factor),
                    factor,
                    "暗色化係数は0より大きく1以下である必要があります。");
            }

            return new Color32(
                RoundColorChannel(source.r, factor),
                RoundColorChannel(source.g, factor),
                RoundColorChannel(source.b, factor),
                source.a);
        }

        /// <summary>
        /// Faceの上へFaceBoundaryOverlayを重ね、GroundまたはWall用の断面1段目を作ります。
        /// Wall用でもFace本体は変更せず、Overlayの不透明画素だけを暗色化します。
        /// </summary>
        /// <param name="facePixels">暗色化せず下地として使う16×16 pxのFace画素です。</param>
        /// <param name="overlayPixels">透明または不透明だけを持つ16×16 pxの境界Overlay画素です。</param>
        /// <param name="darkenOverlay">Wall用としてOverlayだけを暗色化する場合はtrueです。</param>
        /// <param name="darkenFactor">Wall用Overlayへ適用するRGB乗算係数です。</param>
        /// <returns>FaceとOverlayを事前合成した256画素の新しい配列です。</returns>
        public static Color32[] ComposeFaceBoundary(
            Color32[] facePixels,
            Color32[] overlayPixels,
            bool darkenOverlay,
            float darkenFactor)
        {
            ValidatePixelArray(facePixels, nameof(facePixels));
            ValidatePixelArray(overlayPixels, nameof(overlayPixels));
            if (darkenOverlay && (darkenFactor <= 0f || darkenFactor > 1f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(darkenFactor),
                    darkenFactor,
                    "暗色化係数は0より大きく1以下である必要があります。");
            }

            Color32[] result = new Color32[PixelCount];
            for (int pixelIndex = 0; pixelIndex < PixelCount; pixelIndex++)
            {
                Color32 overlay = overlayPixels[pixelIndex];
                if (overlay.a == 0)
                {
                    // 透明部分はFaceをそのまま残し、境界素材の空白で断面を欠けさせません。
                    result[pixelIndex] = facePixels[pixelIndex];
                    continue;
                }

                if (overlay.a != byte.MaxValue)
                {
                    throw new ArgumentException(
                        "FaceBoundaryOverlayに半透明画素があります。Alphaは0または255だけを使用してください。",
                        nameof(overlayPixels));
                }

                result[pixelIndex] = darkenOverlay
                    ? Darken(overlay, darkenFactor)
                    : overlay;
            }

            return result;
        }

        /// <summary>
        /// 5枚の外周部品、共通内部色、上下左右の同材質接続Maskから1枚のWallTopを作ります。
        /// 二辺が同時に露出する交差領域だけ角部品を使い、一辺だけ露出する場合はその辺部品を端まで使います。
        /// </summary>
        /// <param name="topEdgePixels">上辺用の16×16 px透明入力です。</param>
        /// <param name="bottomEdgePixels">下辺用の16×16 px透明入力です。</param>
        /// <param name="leftEdgePixels">左辺用かつ右辺の反転元となる16×16 px透明入力です。</param>
        /// <param name="topLeftCornerPixels">左上角用かつ右上角の反転元となる入力です。</param>
        /// <param name="bottomLeftCornerPixels">左下角用かつ右下角の反転元となる入力です。</param>
        /// <param name="connectionMask">同材質Wallが接続している上下左右の4bitです。</param>
        /// <param name="topWidth">上辺の幅で、単位は元解像度ピクセルです。</param>
        /// <param name="sideWidth">左右辺で共有する幅で、単位は元解像度ピクセルです。</param>
        /// <param name="bottomWidth">下辺の幅で、単位は元解像度ピクセルです。</param>
        /// <param name="interiorDark">外周がない画素へ使う、全材質共通の不透明色です。</param>
        /// <param name="darkenRim">材質外周だけを機械的に暗色化する場合はtrueです。</param>
        /// <param name="darkenFactor">材質外周へ適用するRGB乗算係数です。</param>
        /// <returns>接続状態に対応する256画素の新しいWallTop配列です。</returns>
        public static Color32[] ComposeWallTop(
            Color32[] topEdgePixels,
            Color32[] bottomEdgePixels,
            Color32[] leftEdgePixels,
            Color32[] topLeftCornerPixels,
            Color32[] bottomLeftCornerPixels,
            TerrainWallConnectionMask connectionMask,
            int topWidth,
            int sideWidth,
            int bottomWidth,
            Color32 interiorDark,
            bool darkenRim,
            float darkenFactor)
        {
            ValidatePixelArray(topEdgePixels, nameof(topEdgePixels));
            ValidatePixelArray(bottomEdgePixels, nameof(bottomEdgePixels));
            ValidatePixelArray(leftEdgePixels, nameof(leftEdgePixels));
            ValidatePixelArray(topLeftCornerPixels, nameof(topLeftCornerPixels));
            ValidatePixelArray(bottomLeftCornerPixels, nameof(bottomLeftCornerPixels));
            ValidateWidths(topWidth, sideWidth, bottomWidth);
            if ((connectionMask & ~TerrainWallConnectionMask.All) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(connectionMask),
                    connectionMask,
                    "Wall接続Maskは上下左右の4bitだけを使用してください。");
            }

            if (darkenRim && (darkenFactor <= 0f || darkenFactor > 1f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(darkenFactor),
                    darkenFactor,
                    "暗色化係数は0より大きく1以下である必要があります。");
            }

            // 共通内部色は材質差を持たず、暗色化対象にもならないため、最初に全画素へ敷きます。
            interiorDark.a = byte.MaxValue;
            Color32[] result = new Color32[PixelCount];
            for (int pixelIndex = 0; pixelIndex < PixelCount; pixelIndex++)
            {
                result[pixelIndex] = interiorDark;
            }

            int size = TerrainTileArtGenerationSettings.TileSizeInPixels;
            bool exposeTop = (connectionMask & TerrainWallConnectionMask.Up) == 0;
            bool exposeRight = (connectionMask & TerrainWallConnectionMask.Right) == 0;
            bool exposeBottom = (connectionMask & TerrainWallConnectionMask.Down) == 0;
            bool exposeLeft = (connectionMask & TerrainWallConnectionMask.Left) == 0;

            for (int y = 0; y < size; y++)
            {
                bool insideTop = y >= size - topWidth;
                bool insideBottom = y < bottomWidth;
                for (int x = 0; x < size; x++)
                {
                    bool insideLeft = x < sideWidth;
                    bool insideRight = x >= size - sideWidth;
                    int destinationIndex = ToIndex(x, y);

                    // 二辺が露出する交差領域では専用角を優先し、辺同士の上書き順で模様が変わることを防ぎます。
                    if (exposeTop && exposeLeft && insideTop && insideLeft)
                    {
                        CopyRimPixel(
                            topLeftCornerPixels,
                            destinationIndex,
                            destinationIndex,
                            result,
                            darkenRim,
                            darkenFactor);
                    }
                    else if (exposeTop && exposeRight && insideTop && insideRight)
                    {
                        int sourceIndex = ToIndex(size - 1 - x, y);
                        CopyRimPixel(
                            topLeftCornerPixels,
                            sourceIndex,
                            destinationIndex,
                            result,
                            darkenRim,
                            darkenFactor);
                    }
                    else if (exposeBottom && exposeLeft && insideBottom && insideLeft)
                    {
                        CopyRimPixel(
                            bottomLeftCornerPixels,
                            destinationIndex,
                            destinationIndex,
                            result,
                            darkenRim,
                            darkenFactor);
                    }
                    else if (exposeBottom && exposeRight && insideBottom && insideRight)
                    {
                        int sourceIndex = ToIndex(size - 1 - x, y);
                        CopyRimPixel(
                            bottomLeftCornerPixels,
                            sourceIndex,
                            destinationIndex,
                            result,
                            darkenRim,
                            darkenFactor);
                    }
                    else if (exposeTop && insideTop)
                    {
                        CopyRimPixel(
                            topEdgePixels,
                            destinationIndex,
                            destinationIndex,
                            result,
                            darkenRim,
                            darkenFactor);
                    }
                    else if (exposeBottom && insideBottom)
                    {
                        CopyRimPixel(
                            bottomEdgePixels,
                            destinationIndex,
                            destinationIndex,
                            result,
                            darkenRim,
                            darkenFactor);
                    }
                    else if (exposeLeft && insideLeft)
                    {
                        CopyRimPixel(
                            leftEdgePixels,
                            destinationIndex,
                            destinationIndex,
                            result,
                            darkenRim,
                            darkenFactor);
                    }
                    else if (exposeRight && insideRight)
                    {
                        int sourceIndex = ToIndex(size - 1 - x, y);
                        CopyRimPixel(
                            leftEdgePixels,
                            sourceIndex,
                            destinationIndex,
                            result,
                            darkenRim,
                            darkenFactor);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 外周幅から中央領域が最低1 px残ることを検証し、無効な配列アクセスや重複外周を防ぎます。
        /// </summary>
        /// <param name="topWidth">検証する上辺幅です。</param>
        /// <param name="sideWidth">検証する左右共通幅です。</param>
        /// <param name="bottomWidth">検証する下辺幅です。</param>
        public static void ValidateWidths(int topWidth, int sideWidth, int bottomWidth)
        {
            int size = TerrainTileArtGenerationSettings.TileSizeInPixels;
            if (topWidth < 1 || sideWidth < 1 || bottomWidth < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(topWidth),
                    "WallTopの上・左右・下の外周幅はすべて1 px以上である必要があります。");
            }

            if (size - sideWidth * 2 < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sideWidth),
                    "WallTopの左右外周を除いた中央幅は1 px以上必要です。");
            }

            if (size - topWidth - bottomWidth < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bottomWidth),
                    "WallTopの上下外周を除いた中央高さは1 px以上必要です。");
            }
        }

        /// <summary>
        /// 1チャンネルのsRGB値へ係数を掛け、仕様どおり最も近い8bit整数へ丸めます。
        /// </summary>
        /// <param name="channel">0〜255の元チャンネル値です。</param>
        /// <param name="factor">0より大きく1以下の乗算係数です。</param>
        /// <returns>0〜255へ収めた暗色化後の値です。</returns>
        private static byte RoundColorChannel(byte channel, float factor)
        {
            return (byte)Mathf.Clamp(
                Mathf.RoundToInt(channel * factor),
                byte.MinValue,
                byte.MaxValue);
        }

        /// <summary>
        /// 透明な部品画素なら共通内部色を残し、不透明なら必要に応じて暗色化して出力へコピーします。
        /// 半透明はピクセルアート制作規則に反するため、その場で生成を中断します。
        /// </summary>
        /// <param name="source">コピー元の16×16 px配列です。</param>
        /// <param name="sourceIndex">反転も反映したコピー元添字です。</param>
        /// <param name="destinationIndex">完成WallTop内の書き込み先添字です。</param>
        /// <param name="destination">共通内部色で初期化済みの完成配列です。</param>
        /// <param name="darken">不透明な外周画素を暗色化する場合はtrueです。</param>
        /// <param name="factor">暗色化する場合のRGB乗算係数です。</param>
        private static void CopyRimPixel(
            Color32[] source,
            int sourceIndex,
            int destinationIndex,
            Color32[] destination,
            bool darken,
            float factor)
        {
            Color32 sourceColor = source[sourceIndex];
            if (sourceColor.a == 0)
            {
                return;
            }

            if (sourceColor.a != byte.MaxValue)
            {
                throw new ArgumentException(
                    "WallTop外周部品に半透明画素があります。Alphaは0または255だけを使用してください。",
                    nameof(source));
            }

            destination[destinationIndex] = darken
                ? Darken(sourceColor, factor)
                : sourceColor;
        }

        /// <summary>
        /// 入力がnullでなく16×16 px分の画素を持つことを、公開合成処理の入口で共通検証します。
        /// </summary>
        /// <param name="pixels">検証する画素配列です。</param>
        /// <param name="parameterName">例外へ含める呼び出し元引数名です。</param>
        private static void ValidatePixelArray(Color32[] pixels, string parameterName)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (pixels.Length != PixelCount)
            {
                throw new ArgumentException(
                    $"地形材質Spriteは16×16 pxの{PixelCount}画素である必要があります。",
                    parameterName);
            }
        }

        /// <summary>
        /// 左下原点のx・y座標を、Texture2D.GetPixels32と同じ一次元添字へ変換します。
        /// </summary>
        /// <param name="x">0〜15の水平座標です。</param>
        /// <param name="y">0〜15の垂直座標です。</param>
        /// <returns>0〜255の画素添字です。</returns>
        private static int ToIndex(int x, int y)
        {
            return y * TerrainTileArtGenerationSettings.TileSizeInPixels + x;
        }
    }
}
