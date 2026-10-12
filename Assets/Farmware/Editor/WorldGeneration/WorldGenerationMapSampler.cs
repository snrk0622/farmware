// このファイルは、Editorの広域マップビューアへ渡す色付きサンプルを、実ゲームと同じ決定論的地形生成関数から作ります。
// EditorWindowのUIやTexture2D生成から純粋計算を分離し、バックグラウンド実行とEditModeテストを可能にします。

using System;
using System.Threading;
using UnityEngine;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 広域マップビューアで選択できる表示内容です。
    /// Terrainは実際の最終地形を優先し、Biomeは水域や岩壁の下にある論理バイオームだけを表示します。
    /// </summary>
    public enum WorldGenerationMapViewMode : byte
    {
        /// <summary>バイオーム地面、水域、マグマ、岩壁を最終生成結果に近い色で表示します。</summary>
        Terrain = 0,

        /// <summary>水域や障害物に隠れている場所も含め、7種類の論理バイオームを表示します。</summary>
        Biome = 1
    }

    /// <summary>
    /// 1回の広域サンプリング結果を保持します。
    /// PixelColorsの行0はTexture2Dと同じ下端で、ワールドYが小さい側に対応します。
    /// </summary>
    public sealed class WorldGenerationMapSampleResult
    {
        /// <summary>結果画像の横ピクセル数です。生成完了後は変更されません。</summary>
        public int Width { get; }

        /// <summary>結果画像の縦ピクセル数です。生成完了後は変更されません。</summary>
        public int Height { get; }

        /// <summary>結果画像中央に対応するワールド水平タイル座標です。</summary>
        public int CenterTileX { get; }

        /// <summary>結果画像中央に対応するワールド垂直タイル座標です。</summary>
        public int CenterTileY { get; }

        /// <summary>画像1ピクセルが代表するワールドタイル幅です。</summary>
        public int TilesPerPixel { get; }

        /// <summary>Texture2D.SetPixels32へ直接渡せる、下端行から並んだ色配列です。</summary>
        public Color32[] PixelColors { get; }

        /// <summary>
        /// 完成済みの画像情報と色配列をまとめます。
        /// </summary>
        /// <param name="width">画像の横ピクセル数です。</param>
        /// <param name="height">画像の縦ピクセル数です。</param>
        /// <param name="centerTileX">中央の水平タイル座標です。</param>
        /// <param name="centerTileY">中央の垂直タイル座標です。</param>
        /// <param name="tilesPerPixel">画像1ピクセルあたりのタイル幅です。</param>
        /// <param name="pixelColors">下端行から行優先で並べた色配列です。</param>
        internal WorldGenerationMapSampleResult(
            int width,
            int height,
            int centerTileX,
            int centerTileY,
            int tilesPerPixel,
            Color32[] pixelColors)
        {
            Width = width;
            Height = height;
            CenterTileX = centerTileX;
            CenterTileY = centerTileY;
            TilesPerPixel = tilesPerPixel;
            PixelColors = pixelColors;
        }

        /// <summary>
        /// 指定画像ピクセルが実際にサンプリングしたワールドタイル座標を返します。
        /// テストとカーソル表示が、画像生成と同じ座標変換を共有するために使用します。
        /// </summary>
        /// <param name="pixelX">左端を0とする画像ピクセルXです。</param>
        /// <param name="pixelY">下端を0とする画像ピクセルYです。</param>
        /// <param name="worldTileX">対応する水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">対応する垂直ワールドタイル座標です。</param>
        public void GetSampledWorldTile(
            int pixelX,
            int pixelY,
            out int worldTileX,
            out int worldTileY)
        {
            if (pixelX < 0 || pixelX >= Width)
            {
                throw new ArgumentOutOfRangeException(nameof(pixelX));
            }

            if (pixelY < 0 || pixelY >= Height)
            {
                throw new ArgumentOutOfRangeException(nameof(pixelY));
            }

            worldTileX = WorldGenerationMapSampler.CalculateSampleCoordinate(
                CenterTileX,
                pixelX,
                Width,
                TilesPerPixel);
            worldTileY = WorldGenerationMapSampler.CalculateSampleCoordinate(
                CenterTileY,
                pixelY,
                Height,
                TilesPerPixel);
        }
    }

    /// <summary>
    /// 広域表示用の代表座標を走査し、実ゲームと同じDeterministicChunkGeneratorの結果を色へ変換します。
    /// 独自の簡易バイオーム計算を持たないことが、Gameビューとの一致を保証する重要な境界です。
    /// </summary>
    public static class WorldGenerationMapSampler
    {
        /// <summary>1回の誤操作で巨大配列を確保しないための画像1辺上限です。単位はピクセルです。</summary>
        public const int MaximumResolution = 768;

        /// <summary>
        /// 指定範囲を1ピクセル1代表タイルで走査し、表示用の色配列を作ります。
        /// 呼び出し元はこの純粋計算をTask.Runへ渡せますが、返却後のTexture2D生成はEditorメインスレッドで行います。
        /// </summary>
        /// <param name="settings">実際のBakerと同じ変換から得た地形生成設定です。</param>
        /// <param name="centerTileX">画像中央の水平タイル座標です。</param>
        /// <param name="centerTileY">画像中央の垂直タイル座標です。</param>
        /// <param name="width">画像の横ピクセル数です。</param>
        /// <param name="height">画像の縦ピクセル数です。</param>
        /// <param name="tilesPerPixel">1ピクセルごとに進めるワールドタイル数です。</param>
        /// <param name="viewMode">最終地形または論理バイオームの表示モードです。</param>
        /// <param name="cancellationToken">新しい操作で不要になった走査を中止するためのTokenです。</param>
        /// <returns>Texture2Dへ適用できる色配列と座標情報です。</returns>
        public static WorldGenerationMapSampleResult Sample(
            in WorldGenerationSettings settings,
            int centerTileX,
            int centerTileY,
            int width,
            int height,
            int tilesPerPixel,
            WorldGenerationMapViewMode viewMode,
            CancellationToken cancellationToken)
        {
            if (width <= 0 || width > MaximumResolution)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0 || height > MaximumResolution)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            if (tilesPerPixel <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tilesPerPixel));
            }

            Color32[] pixelColors = new Color32[width * height];

            // Texture2Dの配列は下端行から始まるため、pixelYの増加をワールドYの増加へそのまま対応させます。
            for (int pixelY = 0; pixelY < height; pixelY++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int worldTileY = CalculateSampleCoordinate(
                    centerTileY,
                    pixelY,
                    height,
                    tilesPerPixel);

                for (int pixelX = 0; pixelX < width; pixelX++)
                {
                    // 1行が長い場合も操作後のキャンセルへ早く反応できるよう、32ピクセルごとに確認します。
                    if ((pixelX & 31) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    int worldTileX = CalculateSampleCoordinate(
                        centerTileX,
                        pixelX,
                        width,
                        tilesPerPixel);
                    GeneratedTile generatedTile = DeterministicChunkGenerator.GenerateTile(
                        settings,
                        worldTileX,
                        worldTileY);
                    pixelColors[(pixelY * width) + pixelX] = GetDisplayColor(
                        viewMode,
                        generatedTile);
                }
            }

            return new WorldGenerationMapSampleResult(
                width,
                height,
                centerTileX,
                centerTileY,
                tilesPerPixel,
                pixelColors);
        }

        /// <summary>
        /// 画像ピクセル位置を、そのピクセルが代表する符号付きワールドタイル座標へ変換します。
        /// longで途中計算し、極端な座標とズームでもintの折り返しを起こしません。
        /// </summary>
        /// <param name="centerTileCoordinate">対象軸の中央タイル座標です。</param>
        /// <param name="pixelCoordinate">左端または下端を0とするピクセル座標です。</param>
        /// <param name="pixelLength">対象軸の画像ピクセル数です。</param>
        /// <param name="tilesPerPixel">1ピクセルあたりのタイル数です。</param>
        /// <returns>Generatorへ渡せるint範囲のワールドタイル座標です。</returns>
        public static int CalculateSampleCoordinate(
            int centerTileCoordinate,
            int pixelCoordinate,
            int pixelLength,
            int tilesPerPixel)
        {
            long offset = (long)(pixelCoordinate - (pixelLength / 2)) * tilesPerPixel;
            return ClampToInt((long)centerTileCoordinate + offset);
        }

        /// <summary>
        /// UI Toolkitの左上原点カーソル位置を、マップ中央基準のワールドタイル座標へ変換します。
        /// </summary>
        /// <param name="centerTileX">表示中央の水平タイル座標です。</param>
        /// <param name="centerTileY">表示中央の垂直タイル座標です。</param>
        /// <param name="tilesPerPixel">1画面ピクセルあたりのタイル数です。</param>
        /// <param name="viewportWidth">マップ表示領域の横幅です。単位はEditorポイントです。</param>
        /// <param name="viewportHeight">マップ表示領域の縦幅です。単位はEditorポイントです。</param>
        /// <param name="pointerX">表示領域左端からのカーソルXです。</param>
        /// <param name="pointerY">表示領域上端からのカーソルYです。</param>
        /// <param name="worldTileX">カーソル下の水平タイル座標です。</param>
        /// <param name="worldTileY">カーソル下の垂直タイル座標です。</param>
        public static void CalculateWorldTileAtPointer(
            int centerTileX,
            int centerTileY,
            int tilesPerPixel,
            float viewportWidth,
            float viewportHeight,
            float pointerX,
            float pointerY,
            out int worldTileX,
            out int worldTileY)
        {
            double horizontalOffset = (pointerX - (viewportWidth * 0.5d)) * tilesPerPixel;
            double verticalOffset = ((viewportHeight * 0.5d) - pointerY) * tilesPerPixel;
            worldTileX = ClampToInt((long)Math.Round(centerTileX + horizontalOffset));
            worldTileY = ClampToInt((long)Math.Round(centerTileY + verticalOffset));
        }

        /// <summary>
        /// カーソル下のワールド座標を固定したままズーム倍率だけを変更できる、新しい中央座標を計算します。
        /// </summary>
        /// <param name="centerTileX">変更前の中央水平タイル座標です。</param>
        /// <param name="centerTileY">変更前の中央垂直タイル座標です。</param>
        /// <param name="oldTilesPerPixel">変更前の1ピクセルあたりタイル数です。</param>
        /// <param name="newTilesPerPixel">変更後の1ピクセルあたりタイル数です。</param>
        /// <param name="viewportWidth">マップ表示領域の横幅です。</param>
        /// <param name="viewportHeight">マップ表示領域の縦幅です。</param>
        /// <param name="pointerX">ズーム中心にするカーソルXです。</param>
        /// <param name="pointerY">ズーム中心にするカーソルYです。</param>
        /// <param name="newCenterTileX">変更後の中央水平タイル座標です。</param>
        /// <param name="newCenterTileY">変更後の中央垂直タイル座標です。</param>
        public static void CalculateZoomedCenter(
            int centerTileX,
            int centerTileY,
            int oldTilesPerPixel,
            int newTilesPerPixel,
            float viewportWidth,
            float viewportHeight,
            float pointerX,
            float pointerY,
            out int newCenterTileX,
            out int newCenterTileY)
        {
            CalculateWorldTileAtPointer(
                centerTileX,
                centerTileY,
                oldTilesPerPixel,
                viewportWidth,
                viewportHeight,
                pointerX,
                pointerY,
                out int anchoredTileX,
                out int anchoredTileY);

            double horizontalPixels = pointerX - (viewportWidth * 0.5d);
            double verticalPixels = (viewportHeight * 0.5d) - pointerY;
            newCenterTileX = ClampToInt(
                (long)Math.Round(anchoredTileX - (horizontalPixels * newTilesPerPixel)));
            newCenterTileY = ClampToInt(
                (long)Math.Round(anchoredTileY - (verticalPixels * newTilesPerPixel)));
        }

        /// <summary>
        /// 生成済みタイルを選択中モードの固定色へ変換します。
        /// 色は観察用であり、生成判定そのものには使用しません。
        /// </summary>
        /// <param name="viewMode">最終地形または論理バイオームの表示モードです。</param>
        /// <param name="tile">実ゲームと同じGeneratorが返したタイルです。</param>
        /// <returns>凡例とTexture2Dで共有する不透明色です。</returns>
        public static Color32 GetDisplayColor(
            WorldGenerationMapViewMode viewMode,
            in GeneratedTile tile)
        {
            if (viewMode == WorldGenerationMapViewMode.Biome)
            {
                return GetBiomeColor(tile.Biome);
            }

            // 最終地形の優先順位を反映し、水域や障害物が論理バイオーム色に埋もれないようにします。
            switch (tile.Ground)
            {
                case GroundType.Water:
                    return tile.WaterSalinity == WaterSalinity.SaltWater
                        ? new Color32(42, 101, 176, 255)
                        : new Color32(76, 157, 211, 255);
                case GroundType.Magma:
                    return new Color32(238, 82, 24, 255);
                case GroundType.RockWall:
                    return new Color32(52, 52, 60, 255);
                default:
                    return GetBiomeColor(tile.Biome);
            }
        }

        /// <summary>
        /// 7種類の論理バイオームを、フォールバックTileに近い識別色へ変換します。
        /// </summary>
        /// <param name="biome">色を取得する論理バイオームです。</param>
        /// <returns>凡例と画像で共有する不透明色です。</returns>
        public static Color32 GetBiomeColor(BiomeId biome)
        {
            switch (biome)
            {
                case BiomeId.Forest:
                    return new Color32(61, 112, 66, 255);
                case BiomeId.Desert:
                    return new Color32(210, 177, 100, 255);
                case BiomeId.Rockland:
                    return new Color32(128, 123, 137, 255);
                case BiomeId.Wetland:
                    return new Color32(72, 128, 103, 255);
                case BiomeId.Snowfield:
                    return new Color32(198, 225, 232, 255);
                case BiomeId.Volcanic:
                    return new Color32(130, 67, 57, 255);
                case BiomeId.Grassland:
                    return new Color32(112, 169, 86, 255);
                default:
                    return new Color32(24, 24, 30, 255);
            }
        }

        /// <summary>
        /// longで安全に計算した座標を、Generatorが受け取るint範囲へ飽和変換します。
        /// </summary>
        /// <param name="value">int範囲を越える可能性がある座標です。</param>
        /// <returns>int最小値から最大値へ収めた値です。</returns>
        private static int ClampToInt(long value)
        {
            if (value < int.MinValue)
            {
                return int.MinValue;
            }

            if (value > int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)value;
        }
    }
}
