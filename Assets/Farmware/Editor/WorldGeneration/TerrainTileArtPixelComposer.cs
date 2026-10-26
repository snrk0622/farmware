// このファイルは、地形材質の暗色化と断面境界合成を16×16 px画素配列へ適用します。
// AssetDatabaseへ依存しない純粋処理なので、生成ツールとEditModeテストが同じ規則を共有できます。

using System;
using Farmware.WorldGeneration.Authoring;
using UnityEngine;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// Wall表示用のRGB暗色化と、FaceへFaceBoundaryOverlayを重ねる画素処理を提供します。
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
                    // 透明部分はFaceを残し、境界素材の空白によって断面が欠けないようにします。
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
        /// 1チャンネルのsRGB値へ係数を掛け、最も近い8bit整数へ丸めます。
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
        /// 入力がnullでなく16×16 px分の画素を持つことを、公開合成処理の入口で検証します。
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
    }
}
