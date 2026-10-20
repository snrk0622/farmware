// このファイルは、Editorの広域マップビューアへ渡す色付きサンプルを、実ゲームと同じ決定論的地形生成関数から作ります。
// EditorWindowのUIやTexture2D生成から純粋計算を分離し、バックグラウンド実行とEditModeテストを可能にします。

using System;
using System.Threading;
using Farmware.Simulation;
using Farmware.WorldGeneration.Presentation;
using UnityEngine;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 広域マップビューアで選択できる表示内容です。
    /// Terrainは実際の最終地形を優先し、Biomeは水域や岩壁の下にある論理バイオームだけを表示します。
    /// </summary>
    public enum WorldGenerationMapViewMode : byte
    {
        /// <summary>論理バイオーム色を基準に、水域、マグマ、岩壁を優先したプレイヤー向け地形を表示します。</summary>
        Terrain = 0,

        /// <summary>水域や障害物に隠れている場所も含め、7種類の論理バイオームを表示します。</summary>
        Biome = 1,

        /// <summary>0〜65535の連続標高を表示します。</summary>
        Elevation = 2,

        /// <summary>標高補正前の0〜65535の基礎気温を表示します。</summary>
        BaseTemperature = 3,

        /// <summary>標高寒冷補正後の0〜65535の地形気温を表示します。</summary>
        TerrainTemperature = 4,

        /// <summary>現在土壌水分と独立した0〜65535の基礎湿度を表示します。</summary>
        BaseMoisture = 5,

        /// <summary>配置済み植生と独立した0〜65535の木本植生ポテンシャルを表示します。</summary>
        WoodyVegetationPotential = 6,

        /// <summary>固定9標高サンプルから求めた0〜65535の地形起伏を表示します。</summary>
        TerrainRuggedness = 7,

        /// <summary>海への近さと開放方向数から求めた0〜65535の海洋影響を表示します。</summary>
        CoastalInfluence = 8,

        /// <summary>旧Weirdnessと独立した0〜65535の火山活動度を表示します。</summary>
        VolcanicActivityPotential = 9,

        /// <summary>卓越基盤地質の8分類を固定色で表示します。</summary>
        GeologyClass = 10,

        /// <summary>地質Definitionの0〜65535の基礎透水性を表示します。</summary>
        GeologyPermeability = 11,

        /// <summary>地質Definitionの0〜65535の地下水保持力を表示します。</summary>
        GeologyGroundwaterStorage = 12,

        /// <summary>地質Definitionの0〜65535の侵食抵抗を表示します。</summary>
        GeologyErosionResistance = 13,

        /// <summary>カルスト・活動的火山の必須条件フラグを固定色で表示します。</summary>
        GeologyFlags = 14,

        /// <summary>支配地質Voronoiサイトをサイト座標由来の疑似色で表示します。</summary>
        GeologyDominantSite = 15,

        /// <summary>隣接する代表サンプルで支配地質サイトが切り替わる箇所を表示します。</summary>
        GeologySiteBoundary = 16
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

        /// <summary>結果画像が表す固定ワールドレイヤーです。</summary>
        public WorldLayer Layer { get; }

        /// <summary>選択レイヤーへ製品用の自然生成が接続済みならtrueです。WORLD-01CではSurfaceだけがtrueです。</summary>
        public bool HasGeneratedData { get; }

        /// <summary>画像1ピクセルが代表するワールドタイル幅です。</summary>
        public int TilesPerPixel { get; }

        /// <summary>Texture2D.SetPixels32へ直接渡せる、下端行から並んだ色配列です。</summary>
        public Color32[] PixelColors { get; }

        /// <summary>
        /// 完成済みの画像情報と色配列をまとめます。
        /// </summary>
        /// <param name="width">画像の横ピクセル数です。</param>
        /// <param name="height">画像の縦ピクセル数です。</param>
        /// <param name="layer">結果が表す固定ワールドレイヤーです。</param>
        /// <param name="hasGeneratedData">選択レイヤーに製品用自然生成結果がある場合はtrueです。</param>
        /// <param name="centerTileX">中央の水平タイル座標です。</param>
        /// <param name="centerTileY">中央の垂直タイル座標です。</param>
        /// <param name="tilesPerPixel">画像1ピクセルあたりのタイル幅です。</param>
        /// <param name="pixelColors">下端行から行優先で並べた色配列です。</param>
        internal WorldGenerationMapSampleResult(
            int width,
            int height,
            WorldLayer layer,
            bool hasGeneratedData,
            int centerTileX,
            int centerTileY,
            int tilesPerPixel,
            Color32[] pixelColors)
        {
            Width = width;
            Height = height;
            Layer = layer;
            HasGeneratedData = hasGeneratedData;
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

        /// <summary>進捗通知でEditorのイベントキューを埋めないための通知間隔です。単位は生成画像の行です。</summary>
        private const int ProgressReportRowInterval = 16;

        /// <summary>
        /// Canvasの縦横比を保ちながら、1辺が上限以内に収まるTextureサンプル数を求めます。
        /// 各軸を別々に制限すると正方形のタイルが長方形へ歪むため、両軸へ同じ縮小率を適用します。
        /// </summary>
        /// <param name="viewportWidth">Canvasの横幅です。単位はEditorポイントです。</param>
        /// <param name="viewportHeight">Canvasの縦幅です。単位はEditorポイントです。</param>
        /// <param name="sampleWidth">縦横比を保ったTextureの横サンプル数です。</param>
        /// <param name="sampleHeight">縦横比を保ったTextureの縦サンプル数です。</param>
        public static void CalculateSampleResolution(
            float viewportWidth,
            float viewportHeight,
            out int sampleWidth,
            out int sampleHeight)
        {
            if (float.IsNaN(viewportWidth) || float.IsInfinity(viewportWidth) || viewportWidth <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(viewportWidth));
            }

            if (float.IsNaN(viewportHeight) || float.IsInfinity(viewportHeight) || viewportHeight <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(viewportHeight));
            }

            // 長辺を基準に1つの縮小率を決めることで、短辺だけが余分に高解像度となる非等方伸縮を防ぎます。
            float longestViewportSide = Math.Max(viewportWidth, viewportHeight);
            float uniformScale = Math.Min(1f, MaximumResolution / longestViewportSide);
            sampleWidth = Mathf.Clamp(
                Mathf.RoundToInt(viewportWidth * uniformScale),
                1,
                MaximumResolution);
            sampleHeight = Mathf.Clamp(
                Mathf.RoundToInt(viewportHeight * uniformScale),
                1,
                MaximumResolution);
        }

        /// <summary>
        /// 表示中Textureの解像度が、現在のCanvasサイズに対する縦横比維持後の解像度と一致するかを返します。
        /// EditorWindowのリサイズ後に、古いTextureを非等方伸縮せず再生成すべきか判定するために使います。
        /// </summary>
        /// <param name="sampleWidth">表示中Textureの横サンプル数です。</param>
        /// <param name="sampleHeight">表示中Textureの縦サンプル数です。</param>
        /// <param name="viewportWidth">現在のCanvas横幅です。</param>
        /// <param name="viewportHeight">現在のCanvas縦幅です。</param>
        /// <returns>表示中Textureを非等方伸縮せずに表示できる場合はtrueです。</returns>
        public static bool DoesSampleResolutionMatchViewport(
            int sampleWidth,
            int sampleHeight,
            float viewportWidth,
            float viewportHeight)
        {
            CalculateSampleResolution(
                viewportWidth,
                viewportHeight,
                out int expectedWidth,
                out int expectedHeight);
            return sampleWidth == expectedWidth && sampleHeight == expectedHeight;
        }

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
        /// <param name="viewMode">最終地形、論理バイオーム、連続環境軸の表示モードです。</param>
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
            return Sample(
                settings,
                WorldLayer.Surface,
                centerTileX,
                centerTileY,
                width,
                height,
                tilesPerPixel,
                viewMode,
                cancellationToken);
        }

        /// <summary>
        /// 指定レイヤーの広域代表座標を走査します。WORLD-01CではSurfaceだけを実Generatorへ渡し、
        /// UnderwaterとDeepSeaはTERRAIN-01C前に地上を複製しないよう未接続色を返します。
        /// </summary>
        /// <param name="settings">実際のBakerと同じ変換から得た地形生成設定です。</param>
        /// <param name="layer">サンプリング対象の固定ワールドレイヤーです。</param>
        /// <param name="centerTileX">画像中央の水平タイル座標です。</param>
        /// <param name="centerTileY">画像中央の垂直タイル座標です。</param>
        /// <param name="width">画像の横ピクセル数です。</param>
        /// <param name="height">画像の縦ピクセル数です。</param>
        /// <param name="tilesPerPixel">1ピクセルごとに進めるワールドタイル数です。</param>
        /// <param name="viewMode">最終地形、論理バイオーム、連続環境軸の表示モードです。</param>
        /// <param name="cancellationToken">新しい操作で不要になった走査を中止するためのTokenです。</param>
        /// <param name="progress">完了割合0〜1の任意通知先です。呼出元でメインスレッドへ戻すProgressを渡し、省略時は通知しません。</param>
        /// <returns>Texture2Dへ適用できる色配列、レイヤー、座標情報です。</returns>
        /// <exception cref="InvalidOperationException">
        /// 座標端で固定サンプル範囲を安全に確保できないなど、地形生成を完了できない場合に発生します。
        /// </exception>
        public static WorldGenerationMapSampleResult Sample(
            in WorldGenerationSettings settings,
            WorldLayer layer,
            int centerTileX,
            int centerTileY,
            int width,
            int height,
            int tilesPerPixel,
            WorldGenerationMapViewMode viewMode,
            CancellationToken cancellationToken,
            IProgress<float> progress = null)
        {
            if (!WorldLayerUtility.IsPlayable(layer))
            {
                throw new ArgumentOutOfRangeException(nameof(layer));
            }

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
            bool hasGeneratedData = layer == WorldLayer.Surface;

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
                    if (hasGeneratedData)
                    {
                        // int座標端で近傍サンプルを確保できない場合は、値0の地形を本物の結果として表示せず、
                        // EditorWindow側のエラー表示へ伝えます。これにより診断画像とSimulationの失敗判定が一致します。
                        if (!DeterministicChunkGenerator.TryGenerateTile(
                                settings,
                                worldTileX,
                                worldTileY,
                                out GeneratedTile generatedTile))
                        {
                            throw new InvalidOperationException(
                                $"地形生成に必要な固定サンプル範囲を確保できません: ({worldTileX}, {worldTileY})");
                        }

                        GeologySiteData geologySite = default;
                        bool isGeologySiteBoundary = false;
                        if (viewMode == WorldGenerationMapViewMode.GeologyDominantSite
                            || viewMode == WorldGenerationMapViewMode.GeologySiteBoundary)
                        {
                            // サイト情報はGeneratedTileへ常駐しないため、この2モードだけで診断用に再生成します。
                            // 境界は隣接する画像サンプル間で比較し、広域ズームでも1ピクセル以上の線として見えるようにします。
                            int boundaryProbeDistance = viewMode
                                    == WorldGenerationMapViewMode.GeologySiteBoundary
                                ? tilesPerPixel
                                : 0;
                            if (!WorldGenerationDebugVisualizationOverlay.TryGetGeologySiteVisualizationData(
                                    settings,
                                    worldTileX,
                                    worldTileY,
                                    boundaryProbeDistance,
                                    out geologySite,
                                    out isGeologySiteBoundary))
                            {
                                throw new InvalidOperationException(
                                    $"地質サイト診断に必要な固定サンプル範囲を確保できません: ({worldTileX}, {worldTileY})");
                            }
                        }

                        pixelColors[(pixelY * width) + pixelX] = GetDisplayColor(
                            viewMode,
                            generatedTile,
                            geologySite,
                            isGeologySiteBoundary);
                    }
                    else
                    {
                        // 未接続層へ地上Generatorを流用すると、別層が正しく生成されたように見えてしまいます。
                        // 暗い透明度なしの固定色で「データなし」を示し、座標操作自体は検証できるようにします。
                        pixelColors[(pixelY * width) + pixelX] = new Color32(24, 28, 36, 255);
                    }
                }

                // ピクセルごとではなく一定行数と最終行で通知し、正しい地形計算を省略せず待ち時間を可視化します。
                if ((pixelY + 1) % ProgressReportRowInterval == 0 || pixelY + 1 == height)
                {
                    progress?.Report((float)(pixelY + 1) / height);
                }
            }

            return new WorldGenerationMapSampleResult(
                width,
                height,
                layer,
                hasGeneratedData,
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
        /// <param name="tilesPerPixel">Texture 1サンプルあたりのタイル数です。</param>
        /// <param name="sampleWidth">表示中Textureの横サンプル数です。</param>
        /// <param name="sampleHeight">表示中Textureの縦サンプル数です。</param>
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
            int sampleWidth,
            int sampleHeight,
            float viewportWidth,
            float viewportHeight,
            float pointerX,
            float pointerY,
            out int worldTileX,
            out int worldTileY)
        {
            ValidateViewportScale(sampleWidth, viewportWidth, nameof(sampleWidth), nameof(viewportWidth));
            ValidateViewportScale(sampleHeight, viewportHeight, nameof(sampleHeight), nameof(viewportHeight));

            // Map ViewerはPoint FilterでTextureを表示するため、同じ表示ピクセル内のPointerは
            // 連続座標の違いにかかわらず、画像生成時と同じ1つの代表タイルを指す必要があります。
            int pixelX = CalculateDisplayedSampleIndex(
                pointerX,
                viewportWidth,
                sampleWidth,
                invertAxis: false);
            int pixelY = CalculateDisplayedSampleIndex(
                pointerY,
                viewportHeight,
                sampleHeight,
                invertAxis: true);
            worldTileX = CalculateSampleCoordinate(
                centerTileX,
                pixelX,
                sampleWidth,
                tilesPerPixel);
            worldTileY = CalculateSampleCoordinate(
                centerTileY,
                pixelY,
                sampleHeight,
                tilesPerPixel);
        }

        /// <summary>
        /// カーソル下のワールド座標を固定したままズーム倍率だけを変更できる、新しい中央座標を計算します。
        /// </summary>
        /// <param name="centerTileX">変更前の中央水平タイル座標です。</param>
        /// <param name="centerTileY">変更前の中央垂直タイル座標です。</param>
        /// <param name="oldTilesPerPixel">変更前の1ピクセルあたりタイル数です。</param>
        /// <param name="newTilesPerPixel">変更後の1ピクセルあたりタイル数です。</param>
        /// <param name="oldSampleWidth">変更前Textureの横サンプル数です。</param>
        /// <param name="oldSampleHeight">変更前Textureの縦サンプル数です。</param>
        /// <param name="newSampleWidth">変更後Textureの横サンプル数です。</param>
        /// <param name="newSampleHeight">変更後Textureの縦サンプル数です。</param>
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
            int oldSampleWidth,
            int oldSampleHeight,
            int newSampleWidth,
            int newSampleHeight,
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
                oldSampleWidth,
                oldSampleHeight,
                viewportWidth,
                viewportHeight,
                pointerX,
                pointerY,
                out int anchoredTileX,
                out int anchoredTileY);

            ValidateViewportScale(
                newSampleWidth,
                viewportWidth,
                nameof(newSampleWidth),
                nameof(viewportWidth));
            ValidateViewportScale(
                newSampleHeight,
                viewportHeight,
                nameof(newSampleHeight),
                nameof(viewportHeight));

            // 新TextureでPointer下に表示される離散ピクセルを先に求め、そのピクセルがanchoredTileを
            // サンプルする中央座標を逆算します。連続オフセットを丸めると表示色と固定点がずれるためです。
            int newPixelX = CalculateDisplayedSampleIndex(
                pointerX,
                viewportWidth,
                newSampleWidth,
                invertAxis: false);
            int newPixelY = CalculateDisplayedSampleIndex(
                pointerY,
                viewportHeight,
                newSampleHeight,
                invertAxis: true);
            long horizontalTileOffset =
                (long)(newPixelX - (newSampleWidth / 2)) * newTilesPerPixel;
            long verticalTileOffset =
                (long)(newPixelY - (newSampleHeight / 2)) * newTilesPerPixel;
            newCenterTileX = ClampToInt((long)anchoredTileX - horizontalTileOffset);
            newCenterTileY = ClampToInt((long)anchoredTileY - verticalTileOffset);
        }

        /// <summary>
        /// Canvas上のPointer座標を、Point Filterで実際に表示されるTextureピクセルIndexへ変換します。
        /// </summary>
        /// <param name="pointerCoordinate">Canvasの対象軸先頭からのPointer位置です。単位はEditorポイントです。</param>
        /// <param name="viewportLength">Canvasの対象軸の長さです。単位はEditorポイントです。</param>
        /// <param name="sampleLength">Textureの対象軸のピクセル数です。</param>
        /// <param name="invertAxis">左上原点のYをTexture下端原点へ反転する場合はtrueです。</param>
        /// <returns>0からsampleLength - 1へ収めたTextureピクセルIndexです。</returns>
        private static int CalculateDisplayedSampleIndex(
            float pointerCoordinate,
            float viewportLength,
            int sampleLength,
            bool invertAxis)
        {
            ValidateViewportScale(
                sampleLength,
                viewportLength,
                nameof(sampleLength),
                nameof(viewportLength));

            // Pointerは通常Canvas内ですが、境界の丸めやPointer退出直前のイベントでも配列外へ出ないよう端へ収めます。
            double scaledCoordinate = pointerCoordinate * sampleLength / viewportLength;
            int displayedIndex;
            if (scaledCoordinate <= 0d)
            {
                displayedIndex = 0;
            }
            else if (scaledCoordinate >= sampleLength)
            {
                displayedIndex = sampleLength - 1;
            }
            else
            {
                displayedIndex = (int)Math.Floor(scaledCoordinate);
            }

            return invertAxis
                ? (sampleLength - 1) - displayedIndex
                : displayedIndex;
        }

        /// <summary>
        /// Canvas上のPointer移動量を、拡大・縮小前Textureが表すワールドタイル移動量へ変換します。
        /// </summary>
        /// <param name="viewportDelta">Canvas上の移動量です。単位はEditorポイントです。</param>
        /// <param name="viewportLength">Canvasの対象軸の長さです。</param>
        /// <param name="sampleLength">Textureの対象軸のサンプル数です。</param>
        /// <param name="tilesPerPixel">Texture 1サンプルが表すタイル数です。</param>
        /// <returns>Canvas移動量に対応する符号付きワールドタイル数です。</returns>
        public static long CalculateTileOffsetFromViewportDelta(
            float viewportDelta,
            float viewportLength,
            int sampleLength,
            int tilesPerPixel)
        {
            ValidateViewportScale(
                sampleLength,
                viewportLength,
                nameof(sampleLength),
                nameof(viewportLength));
            if (tilesPerPixel <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tilesPerPixel));
            }

            double sampleDelta = viewportDelta * sampleLength / viewportLength;
            return (long)Math.Round(sampleDelta * tilesPerPixel);
        }

        /// <summary>
        /// 中心からのワールドタイル差を、拡大・縮小後のCanvas上の表示位置差へ変換します。
        /// グリッドと原点マーカーを背景Textureの地形と一致させるために使います。
        /// </summary>
        /// <param name="tileOffset">中心からの符号付きワールドタイル差です。</param>
        /// <param name="tilesPerPixel">Texture 1サンプルが表すタイル数です。</param>
        /// <param name="sampleLength">Textureの対象軸のサンプル数です。</param>
        /// <param name="viewportLength">Canvasの対象軸の長さです。</param>
        /// <returns>Canvas中心からの符号付きEditorポイント差です。</returns>
        public static float CalculateViewportOffsetFromTileOffset(
            long tileOffset,
            int tilesPerPixel,
            int sampleLength,
            float viewportLength)
        {
            ValidateViewportScale(
                sampleLength,
                viewportLength,
                nameof(sampleLength),
                nameof(viewportLength));
            if (tilesPerPixel <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tilesPerPixel));
            }

            double sampleOffset = tileOffset / (double)tilesPerPixel;
            return (float)(sampleOffset * viewportLength / sampleLength);
        }

        /// <summary>
        /// ワールドタイル境界を、地形Textureの実際のサンプル境界に一致するCanvas位置へ変換します。
        /// </summary>
        /// <param name="centerTileCoordinate">Textureの整数中央サンプルに割り当てたワールドタイル座標です。</param>
        /// <param name="worldBoundaryCoordinate">表示位置を求めるワールドタイル境界座標です。</param>
        /// <param name="tilesPerPixel">Texture 1サンプルが表すタイル数です。</param>
        /// <param name="sampleLength">Textureの対象軸のサンプル数です。</param>
        /// <param name="viewportLength">Canvasの対象軸の長さです。単位はEditorポイントです。</param>
        /// <param name="invertAxis">Texture下端原点をCanvas上端原点へ反転するY軸の場合はtrueです。</param>
        /// <returns>Canvas先頭からワールド境界までのEditorポイント位置です。</returns>
        public static float CalculateViewportPositionFromWorldBoundary(
            int centerTileCoordinate,
            long worldBoundaryCoordinate,
            int tilesPerPixel,
            int sampleLength,
            float viewportLength,
            bool invertAxis)
        {
            ValidateViewportScale(
                sampleLength,
                viewportLength,
                nameof(sampleLength),
                nameof(viewportLength));
            if (tilesPerPixel <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tilesPerPixel));
            }

            // Sample生成はsampleLength / 2の整数除算位置へcenterTileCoordinateを割り当てます。
            // ここで幾何学的中央の0.5倍を使うと奇数解像度だけ半サンプルずれるため、同じ整数中央から境界を求めます。
            double sampleBoundary = (sampleLength / 2)
                + (((double)worldBoundaryCoordinate - centerTileCoordinate) / tilesPerPixel);
            if (invertAxis)
            {
                // Textureの行0は下端ですが、UI ToolkitのY=0は上端なので、境界位置を上下反転します。
                sampleBoundary = sampleLength - sampleBoundary;
            }

            return (float)(sampleBoundary * viewportLength / sampleLength);
        }

        /// <summary>
        /// Textureサンプル数とCanvas長が座標変換に利用できる正の値かを検証します。
        /// </summary>
        /// <param name="sampleLength">Textureの対象軸のサンプル数です。</param>
        /// <param name="viewportLength">Canvasの対象軸の長さです。</param>
        /// <param name="sampleParameterName">例外に含めるサンプル数の引数名です。</param>
        /// <param name="viewportParameterName">例外に含めるCanvas長の引数名です。</param>
        private static void ValidateViewportScale(
            int sampleLength,
            float viewportLength,
            string sampleParameterName,
            string viewportParameterName)
        {
            if (sampleLength <= 0)
            {
                throw new ArgumentOutOfRangeException(sampleParameterName);
            }

            if (viewportLength <= 0f)
            {
                throw new ArgumentOutOfRangeException(viewportParameterName);
            }
        }

        /// <summary>
        /// 生成済みタイルを選択中モードの固定色へ変換します。
        /// 色は観察用であり、生成判定そのものには使用しません。
        /// </summary>
        /// <param name="viewMode">最終地形、論理バイオーム、連続環境軸の表示モードです。</param>
        /// <param name="tile">実ゲームと同じGeneratorが返したタイルです。</param>
        /// <returns>凡例とTexture2Dで共有する不透明色です。</returns>
        public static Color32 GetDisplayColor(
            WorldGenerationMapViewMode viewMode,
            in GeneratedTile tile)
        {
            return GetDisplayColor(viewMode, tile, default, false);
        }

        /// <summary>
        /// 生成済みタイルと診断時だけ求める地質サイト情報を、選択中モードの固定色へ変換します。
        /// Map Viewerのサンプリングと凡例・テストが同じ色写像を共有するために使用します。
        /// </summary>
        /// <param name="viewMode">最終地形、論理バイオーム、環境軸、地質診断の表示モードです。</param>
        /// <param name="tile">実ゲームと同じGeneratorが返したタイルです。</param>
        /// <param name="geologySite">支配サイト表示に使う診断専用サイト情報です。</param>
        /// <param name="isGeologySiteBoundary">境界表示で隣接サンプルとサイトが異なる場合はtrueです。</param>
        /// <returns>凡例とTexture2Dで共有する不透明色です。</returns>
        public static Color32 GetDisplayColor(
            WorldGenerationMapViewMode viewMode,
            in GeneratedTile tile,
            in GeologySiteData geologySite,
            bool isGeologySiteBoundary)
        {
            if (viewMode == WorldGenerationMapViewMode.Biome)
            {
                return GetBiomeColor(tile.Biome);
            }

            // Gameビューの開発者表示と同じ色関数を使い、2つの診断ツールで軸の意味が食い違わないようにします。
            switch (viewMode)
            {
                case WorldGenerationMapViewMode.Elevation:
                    return GetOpaqueEnvironmentColor(
                        WorldGenerationDebugVisualizationMode.Elevation,
                        tile);
                case WorldGenerationMapViewMode.BaseTemperature:
                    return GetOpaqueEnvironmentColor(
                        WorldGenerationDebugVisualizationMode.BaseTemperature,
                        tile);
                case WorldGenerationMapViewMode.TerrainTemperature:
                    return GetOpaqueEnvironmentColor(
                        WorldGenerationDebugVisualizationMode.TerrainTemperature,
                        tile);
                case WorldGenerationMapViewMode.BaseMoisture:
                    return GetOpaqueEnvironmentColor(
                        WorldGenerationDebugVisualizationMode.BaseMoisture,
                        tile);
                case WorldGenerationMapViewMode.WoodyVegetationPotential:
                    return GetOpaqueEnvironmentColor(
                        WorldGenerationDebugVisualizationMode.WoodyVegetationPotential,
                        tile);
                case WorldGenerationMapViewMode.TerrainRuggedness:
                    return GetOpaqueEnvironmentColor(
                        WorldGenerationDebugVisualizationMode.TerrainRuggedness,
                        tile);
                case WorldGenerationMapViewMode.CoastalInfluence:
                    return GetOpaqueEnvironmentColor(
                        WorldGenerationDebugVisualizationMode.CoastalInfluence,
                        tile);
                case WorldGenerationMapViewMode.VolcanicActivityPotential:
                    return GetOpaqueEnvironmentColor(
                        WorldGenerationDebugVisualizationMode.VolcanicActivityPotential,
                        tile);
                case WorldGenerationMapViewMode.GeologyClass:
                    return GetOpaqueEnvironmentColor(
                        WorldGenerationDebugVisualizationMode.GeologyClass,
                        tile);
                case WorldGenerationMapViewMode.GeologyPermeability:
                    return GetOpaqueEnvironmentColor(
                        WorldGenerationDebugVisualizationMode.GeologyPermeability,
                        tile);
                case WorldGenerationMapViewMode.GeologyGroundwaterStorage:
                    return GetOpaqueEnvironmentColor(
                        WorldGenerationDebugVisualizationMode.GeologyGroundwaterStorage,
                        tile);
                case WorldGenerationMapViewMode.GeologyErosionResistance:
                    return GetOpaqueEnvironmentColor(
                        WorldGenerationDebugVisualizationMode.GeologyErosionResistance,
                        tile);
                case WorldGenerationMapViewMode.GeologyFlags:
                    return GetOpaqueEnvironmentColor(
                        WorldGenerationDebugVisualizationMode.GeologyFlags,
                        tile);
                case WorldGenerationMapViewMode.GeologyDominantSite:
                    return GetOpaqueGeologyDiagnosticColor(
                        WorldGenerationDebugVisualizationMode.GeologyDominantSite,
                        tile,
                        geologySite,
                        isGeologySiteBoundary);
                case WorldGenerationMapViewMode.GeologySiteBoundary:
                    return GetOpaqueGeologyDiagnosticColor(
                        WorldGenerationDebugVisualizationMode.GeologySiteBoundary,
                        tile,
                        geologySite,
                        isGeologySiteBoundary);
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
                    // Gameビューと同様に、SoilTypeは内部情報として保持しつつ土地の見た目はBiomeだけで決めます。
                    return GetBiomeColor(tile.Biome);
            }
        }

        /// <summary>
        /// GameビューOverlayの半透明色を、Map Viewerの背景Texture用の不透明色へ変換します。
        /// RGBを共通化しつつ、Editor背景色との合成で値が変わって見えることを防ぎます。
        /// </summary>
        /// <param name="mode">連続環境軸に対応するGameビュー可視化モードです。</param>
        /// <param name="tile">色へ変換する実Generatorの1タイル結果です。</param>
        /// <returns>RGBを共有し、Alphaだけ255へ固定したMap Viewer用色です。</returns>
        private static Color32 GetOpaqueEnvironmentColor(
            WorldGenerationDebugVisualizationMode mode,
            in GeneratedTile tile)
        {
            Color32 color = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(mode, tile);
            color.a = byte.MaxValue;
            return color;
        }

        /// <summary>Gameビューの支配サイト・境界色を、Map Viewerの不透明Texture色へ変換します。</summary>
        /// <param name="mode">支配サイトまたはサイト境界のGameビュー可視化モードです。</param>
        /// <param name="tile">対象タイルです。将来の共通色関数拡張へ同じ入力を渡します。</param>
        /// <param name="geologySite">対象タイルを支配する診断用サイトです。</param>
        /// <param name="isGeologySiteBoundary">隣接サンプルと支配サイトが異なる場合はtrueです。</param>
        /// <returns>RGBを共有し、Alphaだけ255へ固定した色です。</returns>
        private static Color32 GetOpaqueGeologyDiagnosticColor(
            WorldGenerationDebugVisualizationMode mode,
            in GeneratedTile tile,
            in GeologySiteData geologySite,
            bool isGeologySiteBoundary)
        {
            Color32 color = WorldGenerationDebugVisualizationOverlay.GetVisualizationColor(
                mode,
                tile,
                geologySite: geologySite,
                isGeologySiteBoundary: isGeologySiteBoundary);
            color.a = byte.MaxValue;
            return color;
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
