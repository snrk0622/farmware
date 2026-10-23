// このファイルは、Editorの広域マップビューアへ渡す色付きサンプルを、実ゲームと同じ決定論的地形・完成派生水文から作ります。
// EditorWindowのUIやTexture2D生成から純粋計算を分離し、バックグラウンド実行とEditModeテストを可能にします。

using System;
using System.Collections.Generic;
using System.Threading;
using Farmware.Simulation;
using Farmware.WorldGeneration.Presentation;
using Unity.Collections;
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

        /// <summary>水域や障害物に隠れている場所も含め、新18種類の論理バイオームを表示します。</summary>
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
        GeologySiteBoundary = 16,

        /// <summary>8方向の固定流向と下流なし終端を表示します。</summary>
        HydrologyFlowDirection = 17,

        /// <summary>Marine・ClosedBasin・LongDrainageの固定下流終端を表示します。</summary>
        HydrologyTerminal = 18,

        /// <summary>集水面積と4,096件上限打切りを表示します。</summary>
        HydrologyCatchment = 19,

        /// <summary>固定Stream・River接続を表示します。</summary>
        HydrologySurfaceWater = 20,

        /// <summary>閉鎖流域、固定湖、湖面、越流、打切り流域を表示します。</summary>
        HydrologyLake = 21,

        /// <summary>完成チャンクの固定地下水ポテンシャルと接続境界を表示します。</summary>
        HydrologyGroundwater = 22,

        /// <summary>完成チャンクの固定自然土壌飽和ポテンシャルを表示します。</summary>
        HydrologyNaturalSoilSaturation = 23,

        /// <summary>完成チャンクで最終確定した固定泉流を表示します。</summary>
        HydrologySpring = 24,

        /// <summary>完成チャンクの固定自然冠水ポテンシャルを表示します。</summary>
        HydrologyNaturalFlooding = 25,

        /// <summary>完成チャンクの湿原条件外、局所資格、広域規模を表示します。</summary>
        HydrologyWetlandCandidate = 26
    }

    /// <summary>
    /// Map Viewerが完成チャンクから取得した、1つの代表タイルに対応するC4派生水文値です。
    /// Managed配列へ値コピーして保持するため、元のPersistent NativeArrayを破棄した後もHover表示から安全に参照できます。
    /// </summary>
    public readonly struct DerivedHydrologyMapSample
    {
        /// <summary>代表タイルを担当する8×8固定水文セルです。地下水と接続状態を保持します。</summary>
        public readonly GeneratedHydrologyCell Cell;

        /// <summary>代表タイルの最終固定表流水です。泉流判定に使用します。</summary>
        public readonly GeneratedSurfaceWaterTile SurfaceWaterTile;

        /// <summary>代表タイルの自然飽和、自然冠水、湿原フラグです。</summary>
        public readonly GeneratedHydrologyTile HydrologyTile;

        /// <summary>完成診断チャンクからコピーした3種類の固定生成値をまとめます。</summary>
        /// <param name="cell">代表タイルを担当する固定水文セルです。</param>
        /// <param name="surfaceWaterTile">代表タイルの最終固定表流水です。</param>
        /// <param name="hydrologyTile">代表タイルの固定自然水文値です。</param>
        public DerivedHydrologyMapSample(
            in GeneratedHydrologyCell cell,
            in GeneratedSurfaceWaterTile surfaceWaterTile,
            in GeneratedHydrologyTile hydrologyTile)
        {
            Cell = cell;
            SurfaceWaterTile = surfaceWaterTile;
            HydrologyTile = hydrologyTile;
        }
    }

    /// <summary>
    /// Map Viewerが完成地形と同じ固定入力から再取得した、1代表タイルのBiome選択診断です。
    /// 最終IDだけでなく通常首位・次点・8軸適性を保持し、Hover側で分類式を再実装しません。
    /// </summary>
    public readonly struct BiomeClassificationMapSample
    {
        /// <summary>特殊優先後の最終理由と、特殊判定前の通常候補順位です。</summary>
        public readonly FinalBiomeSelection Selection;

        /// <summary>製品分類器が返した値型診断を、完成チャンク破棄後も使えるManaged結果へコピーします。</summary>
        /// <param name="selection">最終選択理由と通常上位2候補です。</param>
        public BiomeClassificationMapSample(in FinalBiomeSelection selection)
        {
            Selection = selection;
        }
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

        /// <summary>
        /// この結果を生成した表示モードです。
        /// 次の重い非同期生成中も前画像を保持するため、現在のDropdownではなく適用済み画像とHover診断を対応させます。
        /// </summary>
        public WorldGenerationMapViewMode ViewMode { get; }

        /// <summary>Texture2D.SetPixels32へ直接渡せる、下端行から並んだ色配列です。</summary>
        public Color32[] PixelColors { get; }

        /// <summary>C5-C完成地形またはC4派生水文の生成に使用した完成診断チャンク数です。ほかのモードでは0です。</summary>
        public int DiagnosticChunkCount { get; }

        /// <summary>
        /// 完成診断チャンクを使うモードのHover表示へ渡す、代表タイル別のC5-C確定地形です。
        /// 元のPersistent NativeArrayはサンプリング終了時に破棄するため、値型のコピーだけを保持します。
        /// </summary>
        private readonly Dictionary<long, GeneratedTile> finalizedTerrainSamples;

        /// <summary>
        /// 派生水文モードのHover表示へ渡す代表タイル別の値コピーです。
        /// 外部から内容を書き換えないよう非公開Dictionaryとして保持し、検索メソッドだけを公開します。
        /// </summary>
        private readonly Dictionary<long, DerivedHydrologyMapSample> derivedHydrologySamples;

        /// <summary>
        /// Terrain・BiomeモードのHover表示へ渡す代表タイル別Biome選択診断です。
        /// 海域には地上Biome選択がないため登録せず、Noneを候補として偽装しません。
        /// </summary>
        private readonly Dictionary<long, BiomeClassificationMapSample>
            biomeClassificationSamples;

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
        /// <param name="viewMode">この結果を生成した地形・環境・地質・水文の表示モードです。</param>
        /// <param name="pixelColors">下端行から行優先で並べた色配列です。</param>
        /// <param name="diagnosticChunkCount">完成地形または派生水文の生成に使用した完成診断チャンク数です。</param>
        /// <param name="finalizedTerrainSamples">Hover用に値コピーした代表タイル別C5-C確定地形です。</param>
        /// <param name="derivedHydrologySamples">Hover用に値コピーした代表タイル別派生水文です。</param>
        /// <param name="biomeClassificationSamples">Hover用に値コピーした代表タイル別Biome選択診断です。</param>
        internal WorldGenerationMapSampleResult(
            int width,
            int height,
            WorldLayer layer,
            bool hasGeneratedData,
            int centerTileX,
            int centerTileY,
            int tilesPerPixel,
            WorldGenerationMapViewMode viewMode,
            Color32[] pixelColors,
            int diagnosticChunkCount,
            Dictionary<long, GeneratedTile> finalizedTerrainSamples,
            Dictionary<long, DerivedHydrologyMapSample> derivedHydrologySamples,
            Dictionary<long, BiomeClassificationMapSample> biomeClassificationSamples)
        {
            Width = width;
            Height = height;
            Layer = layer;
            HasGeneratedData = hasGeneratedData;
            CenterTileX = centerTileX;
            CenterTileY = centerTileY;
            TilesPerPixel = tilesPerPixel;
            ViewMode = viewMode;
            PixelColors = pixelColors;
            DiagnosticChunkCount = diagnosticChunkCount;
            this.finalizedTerrainSamples = finalizedTerrainSamples;
            this.derivedHydrologySamples = derivedHydrologySamples;
            this.biomeClassificationSamples = biomeClassificationSamples;
        }

        /// <summary>C5-C完成チャンクから画像と同時にコピーした代表タイルを取得します。</summary>
        /// <param name="worldTileX">画像がサンプリングした絶対タイルX座標です。</param>
        /// <param name="worldTileY">画像がサンプリングした絶対タイルY座標です。</param>
        /// <param name="tile">成功時に新18Biomeと最終Groundを反映した値コピーを返します。</param>
        /// <returns>現在結果が指定代表タイルの完成地形を保持する場合はtrueです。</returns>
        public bool TryGetFinalizedTerrainSample(
            int worldTileX,
            int worldTileY,
            out GeneratedTile tile)
        {
            if (finalizedTerrainSamples == null)
            {
                tile = default;
                return false;
            }

            return finalizedTerrainSamples.TryGetValue(
                CreateCoordinateKey(worldTileX, worldTileY),
                out tile);
        }

        /// <summary>
        /// 派生水文Textureと同時に生成した代表タイルの固定値を取得します。
        /// Pointerが指す座標はPoint Filter後も画像が実際に使った代表座標へ変換されるため、再生成は不要です。
        /// </summary>
        /// <param name="worldTileX">画像がサンプリングした絶対タイルX座標です。</param>
        /// <param name="worldTileY">画像がサンプリングした絶対タイルY座標です。</param>
        /// <param name="sample">成功時に地下水、表流水、自然水文の値コピーを返します。</param>
        /// <returns>現在結果が指定代表タイルの派生水文値を保持する場合はtrueです。</returns>
        public bool TryGetDerivedHydrologySample(
            int worldTileX,
            int worldTileY,
            out DerivedHydrologyMapSample sample)
        {
            if (derivedHydrologySamples == null)
            {
                sample = default;
                return false;
            }

            return derivedHydrologySamples.TryGetValue(
                CreateCoordinateKey(worldTileX, worldTileY),
                out sample);
        }

        /// <summary>
        /// Terrain・Biome画像と同時に再取得した代表タイルのBiome選択理由と上位候補を返します。
        /// </summary>
        /// <param name="worldTileX">画像がサンプリングした絶対タイルX座標です。</param>
        /// <param name="worldTileY">画像がサンプリングした絶対タイルY座標です。</param>
        /// <param name="sample">成功時に最終理由、通常首位、次点、軸適性の値コピーを返します。</param>
        /// <returns>現在結果が地上Biome選択診断を保持する場合はtrueです。海域や別モードではfalseです。</returns>
        public bool TryGetBiomeClassificationSample(
            int worldTileX,
            int worldTileY,
            out BiomeClassificationMapSample sample)
        {
            if (biomeClassificationSamples == null)
            {
                sample = default;
                return false;
            }

            return biomeClassificationSamples.TryGetValue(
                CreateCoordinateKey(worldTileX, worldTileY),
                out sample);
        }

        /// <summary>符号付き絶対タイルX/Yを衝突しない64bit検索キーへ詰めます。</summary>
        /// <param name="coordinateX">水平絶対タイル座標です。</param>
        /// <param name="coordinateY">垂直絶対タイル座標です。</param>
        /// <returns>Xを上位32bit、Yを下位32bitへ保持する可逆キーです。</returns>
        private static long CreateCoordinateKey(int coordinateX, int coordinateY)
        {
            return ((long)coordinateX << 32) | (uint)coordinateY;
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
        /// 集水・湖まで再計算する水文モードの画像1辺上限です。
        /// 最大4,096セルの有限探索を768²回開始してEditorを長時間占有しないよう、低解像度TextureをPoint拡大します。
        /// </summary>
        public const int MaximumHydrologyResolution = 64;

        /// <summary>
        /// C5-Cまで完成した診断チャンクを再構築するモードで、1軸に生成してよい代表チャンク数です。
        /// 3×3を上限にして、1チャンク数秒の診断生成がパン・ズーム1回で無制限に増えないようにします。
        /// </summary>
        public const int MaximumCompletedChunkSampledChunksPerAxis = 3;

        /// <summary>進捗通知でEditorのイベントキューを埋めないための通知間隔です。単位は生成画像の行です。</summary>
        private const int ProgressReportRowInterval = 16;

        /// <summary>
        /// Terrain・Biome・派生水文では完成チャンク生成が処理時間の大半を占めるため、進捗の95%をチャンク完成数へ割り当てます。
        /// 残り5%は完成値から色とHoverコピーを作る画像行処理へ割り当て、最後の通知を必ず100%にします。
        /// </summary>
        private const float CompletedChunkProgressLimit = 0.95f;

        /// <summary>
        /// Gate待機中も停止と誤認されないよう、完成チャンク要求を受け付けた直後に表示する準備割合です。
        /// 完成チャンク1件目の通知より十分小さくし、実作業以上に進んだ表示へなりにくくします。
        /// </summary>
        private const float CompletedChunkPreparationProgress = 0.01f;

        /// <summary>
        /// 大容量Persistent作業領域を使う完成水文生成を、Editorプロセス内で同時に1要求へ制限します。
        /// パンやズームを連続したとき、取消済み要求と最新要求が並列にNative配列を確保してメモリとCPUを増幅しないためです。
        /// </summary>
        private static readonly SemaphoreSlim CompletedChunkGenerationGate =
            new SemaphoreSlim(1, 1);

        /// <summary>
        /// Terrain・Biome・派生水文の代表座標が各軸最大3論理チャンクへ収まる、現在縮尺用の画像1辺上限を求めます。
        /// 64タイル未満の間隔では開始位置がチャンク末端でも3件以内になるSpanを使い、64以上では各サンプルを別チャンクとして数えます。
        /// </summary>
        /// <param name="tilesPerPixel">1画像ピクセルごとに進める1以上のワールドタイル数です。</param>
        /// <returns>1以上64以下の完成チャンク使用モードのサンプル上限です。</returns>
        public static int CalculateMaximumCompletedChunkResolution(int tilesPerPixel)
        {
            if (tilesPerPixel <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tilesPerPixel));
            }

            if (tilesPerPixel >= WorldGridConstants.LogicalChunkSize)
            {
                // 1サンプルの間隔が1チャンク以上なら、サンプル数そのものが軸上の最大チャンク数になります。
                return MaximumCompletedChunkSampledChunksPerAxis;
            }

            // 開始座標がチャンク末端63でも3チャンク以内に収まる最大距離は128タイルです。
            // 「最初の1サンプル + 距離内へ置ける間隔数」とし、通常水文の64サンプル上限も越えません。
            int maximumSpan =
                (MaximumCompletedChunkSampledChunksPerAxis - 1)
                * WorldGridConstants.LogicalChunkSize;
            return Math.Min(MaximumHydrologyResolution, (maximumSpan / tilesPerPixel) + 1);
        }

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
            CalculateSampleResolution(
                viewportWidth,
                viewportHeight,
                MaximumResolution,
                out sampleWidth,
                out sampleHeight);
        }

        /// <summary>
        /// 呼出元が指定した1辺上限内で、Canvasの縦横比を保つTextureサンプル数を求めます。
        /// 水文モードは最大4,096件探索を伴うため、通常地形より小さい上限を同じ座標変換規則で適用します。
        /// </summary>
        /// <param name="viewportWidth">Canvasの横幅です。単位はEditorポイントです。</param>
        /// <param name="viewportHeight">Canvasの縦幅です。単位はEditorポイントです。</param>
        /// <param name="maximumResolution">長辺へ許可する1以上768以下の最大サンプル数です。</param>
        /// <param name="sampleWidth">縦横比を保ったTextureの横サンプル数です。</param>
        /// <param name="sampleHeight">縦横比を保ったTextureの縦サンプル数です。</param>
        public static void CalculateSampleResolution(
            float viewportWidth,
            float viewportHeight,
            int maximumResolution,
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

            if (maximumResolution <= 0 || maximumResolution > MaximumResolution)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumResolution));
            }

            // 長辺を基準に1つの縮小率を決めることで、短辺だけが余分に高解像度となる非等方伸縮を防ぎます。
            float longestViewportSide = Math.Max(viewportWidth, viewportHeight);
            float uniformScale = Math.Min(1f, maximumResolution / longestViewportSide);
            sampleWidth = Mathf.Clamp(
                Mathf.RoundToInt(viewportWidth * uniformScale),
                1,
                maximumResolution);
            sampleHeight = Mathf.Clamp(
                Mathf.RoundToInt(viewportHeight * uniformScale),
                1,
                maximumResolution);
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
            return DoesSampleResolutionMatchViewport(
                sampleWidth,
                sampleHeight,
                viewportWidth,
                viewportHeight,
                MaximumResolution);
        }

        /// <summary>指定した解像度上限で再計算したTexture寸法と、表示中サンプル寸法が一致するかを返します。</summary>
        /// <param name="sampleWidth">表示中Textureの横サンプル数です。</param>
        /// <param name="sampleHeight">表示中Textureの縦サンプル数です。</param>
        /// <param name="viewportWidth">現在のCanvas横幅です。</param>
        /// <param name="viewportHeight">現在のCanvas縦幅です。</param>
        /// <param name="maximumResolution">現在モードへ適用する1辺上限です。</param>
        /// <returns>同じ上限とCanvasで同じTexture寸法になる場合はtrueです。</returns>
        public static bool DoesSampleResolutionMatchViewport(
            int sampleWidth,
            int sampleHeight,
            float viewportWidth,
            float viewportHeight,
            int maximumResolution)
        {
            CalculateSampleResolution(
                viewportWidth,
                viewportHeight,
                maximumResolution,
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
        /// <param name="viewMode">最終地形、環境、地質、基礎水文、派生水文の表示モードです。</param>
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
        /// <param name="viewMode">最終地形、環境、地質、基礎水文、派生水文の表示モードです。</param>
        /// <param name="cancellationToken">新しい操作で不要になった走査を中止するためのTokenです。</param>
        /// <param name="progress">完了割合0〜1の任意通知先です。呼出元でメインスレッドへ戻すProgressを渡し、省略時は通知しません。</param>
        /// <returns>Texture2Dへ適用できる色配列、レイヤー、座標情報です。</returns>
        /// <exception cref="InvalidOperationException">
        /// 座標端で固定サンプル範囲を安全に確保できない場合や、完成診断チャンクを生成できない場合に発生します。
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

            WorldGenerationMapViewModeDefinition modeDefinition =
                WorldGenerationMapViewModeCatalog.Get(viewMode);
            bool isBaseHydrologyViewMode = modeDefinition.SamplingPath
                == WorldGenerationMapSamplingPath.BaseHydrologyCell;
            bool isDerivedHydrologyViewMode = modeDefinition.SamplingPath
                == WorldGenerationMapSamplingPath.CompletedHydrologyChunk;
            bool isFinalTerrainViewMode = modeDefinition.SamplingPath
                == WorldGenerationMapSamplingPath.CompletedTerrainChunk;
            bool requiresDiagnosticChunk = isFinalTerrainViewMode
                || isDerivedHydrologyViewMode;
            bool usesCompletedChunkProgress = modeDefinition.ProgressPolicy
                == WorldGenerationMapProgressPolicy.CompletedChunksThenPixelRows;
            if (requiresDiagnosticChunk)
            {
                int maximumCompletedChunkResolution =
                    CalculateMaximumCompletedChunkResolution(tilesPerPixel);
                // 完成チャンクは1件に数秒かかるため、UI以外の呼出元からも無制限な範囲を投入できないよう入口で拒否します。
                // 横長・縦長のどちらでも各軸の代表チャンク数を制限する必要があるため、幅と高さを別々に検証します。
                if (width > maximumCompletedChunkResolution)
                {
                    throw new ArgumentOutOfRangeException(nameof(width));
                }

                if (height > maximumCompletedChunkResolution)
                {
                    throw new ArgumentOutOfRangeException(nameof(height));
                }
            }

            Color32[] pixelColors = new Color32[width * height];
            bool hasGeneratedData = layer == WorldLayer.Surface;
            Dictionary<long, GeneratedHydrologyCell> hydrologyCellCache =
                isBaseHydrologyViewMode
                    ? new Dictionary<long, GeneratedHydrologyCell>()
                    : null;
            Dictionary<long, HydrologyDiagnosticChunk> diagnosticChunkCache =
                requiresDiagnosticChunk
                    ? new Dictionary<long, HydrologyDiagnosticChunk>()
                    : null;
            Dictionary<long, GeneratedTile> finalizedTerrainSamples =
                requiresDiagnosticChunk
                    ? new Dictionary<long, GeneratedTile>(pixelColors.Length)
                    : null;
            Dictionary<long, DerivedHydrologyMapSample> derivedHydrologySamples =
                isDerivedHydrologyViewMode
                    ? new Dictionary<long, DerivedHydrologyMapSample>(pixelColors.Length)
                    : null;
            Dictionary<long, BiomeClassificationMapSample> biomeClassificationSamples =
                isFinalTerrainViewMode
                    ? new Dictionary<long, BiomeClassificationMapSample>(pixelColors.Length)
                    : null;
            // Terrain・Biomeの全代表タイルは同じ固定Definitionを使うため、約4KBのFixedList設定をピクセルごとに再構築しません。
            BiomeClassificationSettings biomeSettings = isFinalTerrainViewMode
                ? BiomeDefinitionCatalog.CreateDefault()
                : default;
            // Map ViewerはThreadPoolでSampleを実行し、4フレームを超える場合があります。Temp系Allocatorの簡易APIを避け、
            // 水文モード中だけPersistent作業領域を1組確保して全ピクセルで再利用し、usingで必ず破棄します。
            using NativeArray<HydrologyUpstreamCandidate> hydrologyFrontier =
                isBaseHydrologyViewMode
                    ? new NativeArray<HydrologyUpstreamCandidate>(
                        DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity,
                        Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory)
                    : default;
            using NativeArray<HydrologyUpstreamCandidate> hydrologyBasinMembers =
                isBaseHydrologyViewMode
                    ? new NativeArray<HydrologyUpstreamCandidate>(
                        DeterministicHydrologyLakeResolver.MaximumBasinMembers,
                        Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory)
                    : default;
            using NativeArray<HydrologyBasinLookupSlot> hydrologyBasinLookup =
                isBaseHydrologyViewMode
                    ? new NativeArray<HydrologyBasinLookupSlot>(
                        DeterministicHydrologyLakeResolver.LookupCapacity,
                        Allocator.Persistent,
                        NativeArrayOptions.ClearMemory)
                    : default;
            HydrologyLakeBasinCache hydrologyBasinCache = default;
            int diagnosticChunkCount = 0;
            bool enteredCompletedChunkGenerationGate = false;

            try
            {
                if (requiresDiagnosticChunk && hasGeneratedData)
                {
                    List<ChunkCoordinate> requiredDiagnosticChunks =
                        CollectRequiredDiagnosticChunkCoordinates(
                            settings,
                            layer,
                            centerTileX,
                            centerTileY,
                            width,
                            height,
                            tilesPerPixel,
                            isDerivedHydrologyViewMode);

                    // 直前要求の1チャンク完了待ちでも0%に見え続けないよう、Gate待機へ入る直前に準備開始を通知します。
                    progress?.Report(CompletedChunkPreparationProgress);
                    // 取消待ちの要求はGate取得中にもTokenで終了し、古い要求を順番に実生成しません。
                    CompletedChunkGenerationGate.Wait(cancellationToken);
                    enteredCompletedChunkGenerationGate = true;

                    // C5-C完成地形と派生水文の重い処理は最初の画像行へ偏るため、先に必要チャンクを列挙順で完成させ、
                    // 画像行ではなくチャンク完成数に対応した進捗をユーザーへ返します。
                    for (int chunkIndex = 0;
                        chunkIndex < requiredDiagnosticChunks.Count;
                        chunkIndex++)
                    {
                        GetOrCreateDiagnosticChunk(
                            settings,
                            requiredDiagnosticChunks[chunkIndex],
                            diagnosticChunkCache,
                            cancellationToken);
                        float completedChunkFraction =
                            (chunkIndex + 1f) / requiredDiagnosticChunks.Count;
                        progress?.Report(
                            completedChunkFraction * CompletedChunkProgressLimit);
                    }
                }

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
                            if (requiresDiagnosticChunk)
                            {
                                GeneratedTile finalizedTile = GetFinalizedTerrainTile(
                                    settings,
                                    layer,
                                    worldTileX,
                                    worldTileY,
                                    diagnosticChunkCache,
                                    cancellationToken);
                                finalizedTerrainSamples[
                                    CreateCoordinateKey(worldTileX, worldTileY)] = finalizedTile;
                                if (isFinalTerrainViewMode)
                                {
                                    GeneratedHydrologyTile finalizedHydrologyTile =
                                        GetFinalizedHydrologyTile(
                                            settings,
                                            layer,
                                            worldTileX,
                                            worldTileY,
                                            diagnosticChunkCache,
                                            cancellationToken);
                                    if (finalizedTile.Biome != BiomeId.None)
                                    {
                                        if (!DeterministicBiomeChunkFinalizer.TryGetBiomeSelectionDiagnostics(
                                                settings,
                                                biomeSettings,
                                                worldTileX,
                                                worldTileY,
                                                finalizedTile,
                                                finalizedHydrologyTile,
                                                out FinalBiomeSelection selection))
                                        {
                                            throw new InvalidOperationException(
                                                $"完成地形とBiome選択診断が一致しません: Tile=({worldTileX}, {worldTileY}), Biome={finalizedTile.Biome}");
                                        }

                                        biomeClassificationSamples[
                                            CreateCoordinateKey(worldTileX, worldTileY)] =
                                            new BiomeClassificationMapSample(selection);
                                    }

                                    // Terrain/BiomeはC4水文確定後のC5-C結果を表示し、Biome=Noneの基礎生成を見せません。
                                    pixelColors[(pixelY * width) + pixelX] = GetDisplayColor(
                                        viewMode,
                                        finalizedTile);
                                    continue;
                                }
                            }

                            if (isDerivedHydrologyViewMode)
                            {
                                DerivedHydrologyMapSample derivedSample =
                                    GetDerivedHydrologySample(
                                        settings,
                                        layer,
                                        worldTileX,
                                        worldTileY,
                                        diagnosticChunkCache,
                                        cancellationToken);
                                derivedHydrologySamples[
                                    CreateCoordinateKey(worldTileX, worldTileY)] = derivedSample;
                                pixelColors[(pixelY * width) + pixelX] =
                                    GetHydrologyDisplayColor(
                                        viewMode,
                                        derivedSample.Cell,
                                        derivedSample.HydrologyTile,
                                        derivedSample.SurfaceWaterTile);
                                continue;
                            }

                            if (isBaseHydrologyViewMode)
                            {
                                if (!DeterministicHydrologyGenerator.TryGetCellCoordinate(
                                        settings.Hydrology,
                                        worldTileX,
                                        worldTileY,
                                        out int hydrologyCellX,
                                        out int hydrologyCellY))
                                {
                                    throw new InvalidOperationException(
                                        $"水文セル座標へ変換できません: ({worldTileX}, {worldTileY})");
                                }

                                long hydrologyKey = CreateCoordinateKey(hydrologyCellX, hydrologyCellY);
                                if (!hydrologyCellCache.TryGetValue(
                                        hydrologyKey,
                                        out GeneratedHydrologyCell hydrologyCell))
                                {
                                    // 同じ8×8セルを複数ピクセルが指す拡大表示では、最大4,096件の集水・湖探索を一度だけ行います。
                                    if (!TryGenerateHydrologyCellWithReusableWorkspace(
                                            settings,
                                            hydrologyCellX,
                                            hydrologyCellY,
                                            viewMode == WorldGenerationMapViewMode.HydrologyLake,
                                            hydrologyFrontier,
                                            hydrologyBasinMembers,
                                            hydrologyBasinLookup,
                                            ref hydrologyBasinCache,
                                            out hydrologyCell))
                                    {
                                        throw new InvalidOperationException(
                                            $"固定水文セルを生成できません: ({hydrologyCellX}, {hydrologyCellY})");
                                    }

                                    hydrologyCellCache.Add(hydrologyKey, hydrologyCell);
                                }

                                pixelColors[(pixelY * width) + pixelX] =
                                    GetHydrologyDisplayColor(viewMode, hydrologyCell);
                                continue;
                            }

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
                        float completedRowFraction = (float)(pixelY + 1) / height;
                        float reportedFraction = usesCompletedChunkProgress
                            ? CompletedChunkProgressLimit
                                + ((1f - CompletedChunkProgressLimit)
                                    * completedRowFraction)
                            : completedRowFraction;
                        progress?.Report(reportedFraction);
                    }
                }

                diagnosticChunkCount = diagnosticChunkCache?.Count ?? 0;
            }
            finally
            {
                try
                {
                    if (diagnosticChunkCache != null)
                    {
                        // 完成チャンクの出力配列はSampleだけが所有し、Managedな値コピーを作った後にすべて破棄します。
                        foreach (HydrologyDiagnosticChunk diagnosticChunk in diagnosticChunkCache.Values)
                        {
                            diagnosticChunk.Dispose();
                        }
                    }
                }
                finally
                {
                    if (enteredCompletedChunkGenerationGate)
                    {
                        // 破棄例外時もGateを占有したままにせず、後続のMap Viewer要求が永久待機することを防ぎます。
                        CompletedChunkGenerationGate.Release();
                    }
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
                viewMode,
                pixelColors,
                diagnosticChunkCount,
                finalizedTerrainSamples,
                derivedHydrologySamples,
                biomeClassificationSamples);
        }

        /// <summary>
        /// C5-C地形・Biome画像または派生水文画像とHoverコピーに必要な完成診断チャンクを、最初に現れる順で重複なく列挙します。
        /// 先に総数を確定することで、画像行へ偏らない「完成チャンク数／必要チャンク数」の進捗を表示できます。
        /// </summary>
        /// <param name="settings">代表タイルから中心基準8×8水文セルを求める固定設定です。</param>
        /// <param name="layer">全代表タイルが属する固定ワールドレイヤーです。</param>
        /// <param name="centerTileX">画像中央の水平タイル座標です。</param>
        /// <param name="centerTileY">画像中央の垂直タイル座標です。</param>
        /// <param name="width">画像の横サンプル数です。</param>
        /// <param name="height">画像の縦サンプル数です。</param>
        /// <param name="tilesPerPixel">代表サンプル間のワールドタイル数です。</param>
        /// <param name="includeHydrologyCellOwner">担当水文セルを隣接チャンクから読む派生水文モードならtrueです。</param>
        /// <returns>タイル所有者と水文セル所有者を含む一意な完成診断チャンク座標です。</returns>
        private static List<ChunkCoordinate> CollectRequiredDiagnosticChunkCoordinates(
            in WorldGenerationSettings settings,
            WorldLayer layer,
            int centerTileX,
            int centerTileY,
            int width,
            int height,
            int tilesPerPixel,
            bool includeHydrologyCellOwner)
        {
            List<ChunkCoordinate> requiredChunks = new List<ChunkCoordinate>();
            HashSet<long> requiredChunkKeys = new HashSet<long>();

            for (int pixelY = 0; pixelY < height; pixelY++)
            {
                int worldTileY = CalculateSampleCoordinate(
                    centerTileY,
                    pixelY,
                    height,
                    tilesPerPixel);
                for (int pixelX = 0; pixelX < width; pixelX++)
                {
                    int worldTileX = CalculateSampleCoordinate(
                        centerTileX,
                        pixelX,
                        width,
                        tilesPerPixel);
                    AddRequiredDiagnosticChunk(
                        WorldGrid.ToChunkCoordinate(layer, worldTileX, worldTileY),
                        requiredChunkKeys,
                        requiredChunks);

                    if (!includeHydrologyCellOwner)
                    {
                        // Terrain/Biomeはタイル所有チャンクだけで完結するため、隣接セル所有チャンクを余分に生成しません。
                        continue;
                    }

                    if (!DeterministicHydrologyGenerator.TryGetCellCoordinate(
                            settings.Hydrology,
                            worldTileX,
                            worldTileY,
                            out int cellX,
                            out int cellY))
                    {
                        throw new InvalidOperationException(
                            $"派生水文の担当セル座標へ変換できません: Tile=({worldTileX}, {worldTileY})");
                    }

                    int cellOwnerChunkX = WorldGrid.FloorDivide(
                        cellX,
                        DeterministicHydrologyGenerator.CellsPerChunkAxis);
                    int cellOwnerChunkY = WorldGrid.FloorDivide(
                        cellY,
                        DeterministicHydrologyGenerator.CellsPerChunkAxis);
                    AddRequiredDiagnosticChunk(
                        new ChunkCoordinate(layer, cellOwnerChunkX, cellOwnerChunkY),
                        requiredChunkKeys,
                        requiredChunks);
                }
            }

            return requiredChunks;
        }

        /// <summary>完成診断チャンク座標を、同じX/Yが未登録の場合だけ進捗用の順序付き一覧へ追加します。</summary>
        /// <param name="coordinate">追加候補の固定レイヤー付き論理チャンク座標です。</param>
        /// <param name="requiredChunkKeys">登録済みX/Yを判定する検索集合です。呼出元が所有します。</param>
        /// <param name="requiredChunks">最初に現れた順を保持する座標一覧です。呼出元が所有します。</param>
        private static void AddRequiredDiagnosticChunk(
            ChunkCoordinate coordinate,
            HashSet<long> requiredChunkKeys,
            List<ChunkCoordinate> requiredChunks)
        {
            long key = CreateCoordinateKey(coordinate.X, coordinate.Y);
            if (requiredChunkKeys.Add(key))
            {
                requiredChunks.Add(coordinate);
            }
        }

        /// <summary>
        /// 指定タイルを所有する完成診断チャンクから、C5-Cで確定した地形値をコピーします。
        /// </summary>
        /// <param name="settings">C3〜C5-Cを再構築する固定設定です。</param>
        /// <param name="layer">対象タイルが属する固定ワールドレイヤーです。</param>
        /// <param name="worldTileX">対象の絶対X座標です。</param>
        /// <param name="worldTileY">対象の絶対Y座標です。</param>
        /// <param name="diagnosticChunkCache">Sample終了時に破棄する完成チャンクキャッシュです。</param>
        /// <param name="cancellationToken">未生成チャンクへ進む前に中止を検出するTokenです。</param>
        /// <returns>新18Biomeと最終Groundを持つ値型のコピーです。</returns>
        private static GeneratedTile GetFinalizedTerrainTile(
            in WorldGenerationSettings settings,
            WorldLayer layer,
            int worldTileX,
            int worldTileY,
            Dictionary<long, HydrologyDiagnosticChunk> diagnosticChunkCache,
            CancellationToken cancellationToken)
        {
            ChunkCoordinate coordinate = WorldGrid.ToChunkCoordinate(
                layer,
                worldTileX,
                worldTileY);
            HydrologyDiagnosticChunk chunk = GetOrCreateDiagnosticChunk(
                settings,
                coordinate,
                diagnosticChunkCache,
                cancellationToken);
            ChunkLocalTileCoordinate localTile = WorldGrid.ToLocalTileCoordinate(
                worldTileX,
                worldTileY);
            int tileIndex = WorldGrid.ToTileIndex(localTile.X, localTile.Y);
            return chunk.TerrainTiles[tileIndex];
        }

        /// <summary>
        /// 指定タイルを所有する完成診断チャンクから、Biome特殊優先へ使ったC4自然水文値をコピーします。
        /// Terrain・Biome Hoverが最終IDだけでなく選択理由を再取得するとき、製品最終化と同じ入力を渡すために使用します。
        /// </summary>
        /// <param name="settings">C3〜C5-Cを再構築する固定設定です。</param>
        /// <param name="layer">対象タイルが属する固定ワールドレイヤーです。</param>
        /// <param name="worldTileX">対象の絶対X座標です。</param>
        /// <param name="worldTileY">対象の絶対Y座標です。</param>
        /// <param name="diagnosticChunkCache">Sample終了時に破棄する完成チャンクキャッシュです。</param>
        /// <param name="cancellationToken">未生成チャンクへ進む前に中止を検出するTokenです。</param>
        /// <returns>自然冠水・自然飽和・湿原規模を含む値型コピーです。</returns>
        private static GeneratedHydrologyTile GetFinalizedHydrologyTile(
            in WorldGenerationSettings settings,
            WorldLayer layer,
            int worldTileX,
            int worldTileY,
            Dictionary<long, HydrologyDiagnosticChunk> diagnosticChunkCache,
            CancellationToken cancellationToken)
        {
            ChunkCoordinate coordinate = WorldGrid.ToChunkCoordinate(
                layer,
                worldTileX,
                worldTileY);
            HydrologyDiagnosticChunk chunk = GetOrCreateDiagnosticChunk(
                settings,
                coordinate,
                diagnosticChunkCache,
                cancellationToken);
            ChunkLocalTileCoordinate localTile = WorldGrid.ToLocalTileCoordinate(
                worldTileX,
                worldTileY);
            int tileIndex = WorldGrid.ToTileIndex(localTile.X, localTile.Y);
            return chunk.HydrologyTiles[tileIndex];
        }

        /// <summary>
        /// 代表タイルを含む完成診断チャンクと、担当水文セルを所有する完成診断チャンクからC4固定値を取得します。
        /// 水文セルは中心基準のためチャンク正側4タイルで隣接チャンク所有となり、地形チャンクだけでは取得できません。
        /// </summary>
        /// <param name="settings">C3とC4-A〜Dを製品順に再構築する固定設定です。</param>
        /// <param name="layer">代表タイルが属する固定ワールドレイヤーです。</param>
        /// <param name="worldTileX">代表タイルの絶対X座標です。</param>
        /// <param name="worldTileY">代表タイルの絶対Y座標です。</param>
        /// <param name="diagnosticChunkCache">このSample呼出し中だけ完成チャンクを再利用する所有キャッシュです。</param>
        /// <param name="cancellationToken">次の重いチャンク生成へ進む前に中止を検出するTokenです。</param>
        /// <returns>元NativeArrayからManaged値へコピーした派生水文サンプルです。</returns>
        private static DerivedHydrologyMapSample GetDerivedHydrologySample(
            in WorldGenerationSettings settings,
            WorldLayer layer,
            int worldTileX,
            int worldTileY,
            Dictionary<long, HydrologyDiagnosticChunk> diagnosticChunkCache,
            CancellationToken cancellationToken)
        {
            ChunkCoordinate tileChunkCoordinate = WorldGrid.ToChunkCoordinate(
                layer,
                worldTileX,
                worldTileY);
            HydrologyDiagnosticChunk tileChunk = GetOrCreateDiagnosticChunk(
                settings,
                tileChunkCoordinate,
                diagnosticChunkCache,
                cancellationToken);
            ChunkLocalTileCoordinate localTile = WorldGrid.ToLocalTileCoordinate(
                worldTileX,
                worldTileY);
            int tileIndex = WorldGrid.ToTileIndex(localTile.X, localTile.Y);
            GeneratedSurfaceWaterTile surfaceWaterTile = tileChunk.SurfaceWaterTiles[tileIndex];
            GeneratedHydrologyTile hydrologyTile = tileChunk.HydrologyTiles[tileIndex];

            if (!DeterministicHydrologyGenerator.TryGetCellCoordinate(
                    settings.Hydrology,
                    worldTileX,
                    worldTileY,
                    out int cellX,
                    out int cellY))
            {
                throw new InvalidOperationException(
                    $"派生水文の担当セル座標へ変換できません: Tile=({worldTileX}, {worldTileY})");
            }

            int cellOwnerChunkX = WorldGrid.FloorDivide(
                cellX,
                DeterministicHydrologyGenerator.CellsPerChunkAxis);
            int cellOwnerChunkY = WorldGrid.FloorDivide(
                cellY,
                DeterministicHydrologyGenerator.CellsPerChunkAxis);
            ChunkCoordinate cellOwnerCoordinate = new ChunkCoordinate(
                layer,
                cellOwnerChunkX,
                cellOwnerChunkY);
            HydrologyDiagnosticChunk cellOwnerChunk = GetOrCreateDiagnosticChunk(
                settings,
                cellOwnerCoordinate,
                diagnosticChunkCache,
                cancellationToken);
            int localCellX = cellX
                - (cellOwnerChunkX * DeterministicHydrologyGenerator.CellsPerChunkAxis);
            int localCellY = cellY
                - (cellOwnerChunkY * DeterministicHydrologyGenerator.CellsPerChunkAxis);
            int cellIndex = (localCellY * DeterministicHydrologyGenerator.CellsPerChunkAxis)
                + localCellX;
            if (localCellX < 0
                || localCellX >= DeterministicHydrologyGenerator.CellsPerChunkAxis
                || localCellY < 0
                || localCellY >= DeterministicHydrologyGenerator.CellsPerChunkAxis
                || cellIndex < 0
                || cellIndex >= cellOwnerChunk.HydrologyCells.Length)
            {
                throw new InvalidOperationException(
                    $"派生水文セルのチャンク内Indexが範囲外です: Cell=({cellX}, {cellY}), Owner=({cellOwnerChunkX}, {cellOwnerChunkY})");
            }

            GeneratedHydrologyCell cell = cellOwnerChunk.HydrologyCells[cellIndex];
            return new DerivedHydrologyMapSample(cell, surfaceWaterTile, hydrologyTile);
        }

        /// <summary>
        /// 指定論理チャンクをC5-Cまで完成生成し、同じSample呼出し中は再利用します。
        /// 生成途中の失敗段階を例外へ含め、乾燥結果と生成失敗を同じ色として扱いません。
        /// </summary>
        /// <param name="settings">完成チャンク生成に使う固定設定です。</param>
        /// <param name="coordinate">生成または再利用するSurface論理チャンク座標です。</param>
        /// <param name="diagnosticChunkCache">Sample終了時に全要素を破棄する所有キャッシュです。</param>
        /// <param name="cancellationToken">未生成チャンクへ進む前に中止を検出するTokenです。</param>
        /// <returns>全4配列を完成済みの診断チャンクです。</returns>
        private static HydrologyDiagnosticChunk GetOrCreateDiagnosticChunk(
            in WorldGenerationSettings settings,
            ChunkCoordinate coordinate,
            Dictionary<long, HydrologyDiagnosticChunk> diagnosticChunkCache,
            CancellationToken cancellationToken)
        {
            long key = CreateCoordinateKey(coordinate.X, coordinate.Y);
            if (diagnosticChunkCache.TryGetValue(key, out HydrologyDiagnosticChunk cached))
            {
                return cached;
            }

            cancellationToken.ThrowIfCancellationRequested();
            // Map Viewerは製品Bakerと同じ固定Definitionカタログを使用し、旧7Biomeや表示専用の代替分類へ戻りません。
            BiomeClassificationSettings biomeSettings = BiomeDefinitionCatalog.CreateDefault();
            if (!HydrologyDiagnosticChunk.TryGenerate(
                    settings,
                    biomeSettings,
                    coordinate,
                    out HydrologyDiagnosticChunk generated,
                    out HydrologyDiagnosticGenerationStage stage))
            {
                throw new InvalidOperationException(
                    $"完成診断チャンクを生成できません: Chunk=({coordinate.X}, {coordinate.Y}), Stage={stage}");
            }

            diagnosticChunkCache.Add(key, generated);
            return generated;
        }

        /// <summary>Map Viewerモードが固定水文の再計算を必要とするかを返します。</summary>
        /// <param name="viewMode">判定する広域表示モードです。</param>
        /// <returns>流向から湿原候補までの10水文モードならtrueです。</returns>
        public static bool IsHydrologyViewMode(WorldGenerationMapViewMode viewMode)
        {
            WorldGenerationMapSamplingPath path =
                WorldGenerationMapViewModeCatalog.Get(viewMode).SamplingPath;
            return path == WorldGenerationMapSamplingPath.BaseHydrologyCell
                || path == WorldGenerationMapSamplingPath.CompletedHydrologyChunk;
        }

        /// <summary>C3基礎水文セルだけを単独生成する既存5モードかを返します。</summary>
        /// <param name="viewMode">判定する広域表示モードです。</param>
        /// <returns>流向から湖までならtrueです。</returns>
        public static bool IsBaseHydrologyViewMode(WorldGenerationMapViewMode viewMode)
        {
            return WorldGenerationMapViewModeCatalog.Get(viewMode).SamplingPath
                == WorldGenerationMapSamplingPath.BaseHydrologyCell;
        }

        /// <summary>C5-Cまでの完成診断チャンクを必要とする派生水文5モードかを返します。</summary>
        /// <param name="viewMode">判定する広域表示モードです。</param>
        /// <returns>地下水から湿原候補までならtrueです。</returns>
        public static bool IsDerivedHydrologyViewMode(WorldGenerationMapViewMode viewMode)
        {
            return WorldGenerationMapViewModeCatalog.Get(viewMode).SamplingPath
                == WorldGenerationMapSamplingPath.CompletedHydrologyChunk;
        }

        /// <summary>C5-Cまでの完成診断チャンクを生成してから表示するモードかを返します。</summary>
        /// <param name="viewMode">判定する広域表示モードです。</param>
        /// <returns>最終Terrain、論理Biome、または派生水文5モードならtrueです。</returns>
        public static bool RequiresCompletedDiagnosticChunk(WorldGenerationMapViewMode viewMode)
        {
            WorldGenerationMapSamplingPath path =
                WorldGenerationMapViewModeCatalog.Get(viewMode).SamplingPath;
            return path == WorldGenerationMapSamplingPath.CompletedTerrainChunk
                || path == WorldGenerationMapSamplingPath.CompletedHydrologyChunk;
        }

        /// <summary>
        /// ThreadPool上で4フレームを超えても使用できるPersistent作業領域から、集水・閉鎖流域・湖面まで含む1水文セルを生成します。
        /// 公開単一セルAPIと同じResolver順を使い、同じ閉鎖末端が続く場合だけ流域結果を安全に再利用します。
        /// </summary>
        /// <param name="settings">Seed、GeneratorVersion、固定水文設定です。</param>
        /// <param name="cellX">生成する絶対水文セルX座標です。</param>
        /// <param name="cellY">生成する絶対水文セルY座標です。</param>
        /// <param name="finalizeLakeClassification">C4-Cの最終湖水収支まで反映する場合はtrueです。</param>
        /// <param name="frontier">集水と流域探索で再利用する上流候補Heapです。</param>
        /// <param name="basinMembers">閉鎖流域セルを保持する固定配列です。</param>
        /// <param name="basinLookup">流域内外判定へ使う固定Hash表です。</param>
        /// <param name="basinCache">直前に解決した閉鎖末端と共通湖結果です。</param>
        /// <param name="cell">成功時の全固定水文結果です。</param>
        /// <returns>集水と必要な湖面解決を固定上限内で完了できた場合はtrueです。</returns>
        private static bool TryGenerateHydrologyCellWithReusableWorkspace(
            in WorldGenerationSettings settings,
            int cellX,
            int cellY,
            bool finalizeLakeClassification,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            ref HydrologyLakeBasinCache basinCache,
            out GeneratedHydrologyCell cell)
        {
            if (!DeterministicHydrologyGenerator.TryGenerateCellWithoutLake(
                    settings,
                    cellX,
                    cellY,
                    frontier,
                    0,
                    out cell))
            {
                cell = default;
                return false;
            }

            if (!DeterministicHydrologyLakeResolver.TryApplyLakeState(
                    settings,
                    ref cell,
                    frontier,
                    0,
                    basinMembers,
                    0,
                    basinLookup,
                    0,
                    ref basinCache))
            {
                cell = default;
                basinCache = default;
                return false;
            }

            if (!finalizeLakeClassification)
            {
                return true;
            }

            if (!DeterministicLakeSpringGenerator.TryFinalizeCellLakeForVisualization(
                    settings,
                    ref cell,
                    frontier,
                    basinMembers,
                    basinLookup,
                    ref basinCache))
            {
                cell = default;
                basinCache = default;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Hover表示とEditModeテスト向けに、1水文セルをC3-DまたはC4-Cまで製品Generatorと同じ順で生成します。
        /// </summary>
        /// <param name="settings">Seed、GeneratorVersion、水文・地下水・湖水収支の固定設定です。</param>
        /// <param name="cellX">生成する絶対水文セルX座標です。</param>
        /// <param name="cellY">生成する絶対水文セルY座標です。</param>
        /// <param name="finalizeLakeClassification">一時湖・塩湖・淡水湖の最終区分まで必要な場合はtrueです。</param>
        /// <param name="cell">成功時に要求段階まで確定した水文セルを返します。</param>
        /// <returns>必要な有限探索をすべて完了できた場合はtrueです。</returns>
        public static bool TryGenerateHydrologyCellForDisplay(
            in WorldGenerationSettings settings,
            int cellX,
            int cellY,
            bool finalizeLakeClassification,
            out GeneratedHydrologyCell cell)
        {
            // Lake以外の表示はC3-Dまでの値で足りるため、大きな流域作業配列を確保せず既存の単セル生成を使います。
            if (!finalizeLakeClassification)
            {
                return DeterministicHydrologyGenerator.TryGenerateCell(
                    settings,
                    cellX,
                    cellY,
                    out cell);
            }

            using NativeArray<HydrologyUpstreamCandidate> frontier =
                new NativeArray<HydrologyUpstreamCandidate>(
                    DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<HydrologyUpstreamCandidate> basinMembers =
                new NativeArray<HydrologyUpstreamCandidate>(
                    DeterministicHydrologyLakeResolver.MaximumBasinMembers,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
            using NativeArray<HydrologyBasinLookupSlot> basinLookup =
                new NativeArray<HydrologyBasinLookupSlot>(
                    DeterministicHydrologyLakeResolver.LookupCapacity,
                    Allocator.Temp,
                    NativeArrayOptions.ClearMemory);
            HydrologyLakeBasinCache basinCache = default;

            // Hoverは1セルだけを同期生成するためTempを使い、呼出し終了時にusingで必ず解放します。
            return TryGenerateHydrologyCellWithReusableWorkspace(
                settings,
                cellX,
                cellY,
                finalizeLakeClassification,
                frontier,
                basinMembers,
                basinLookup,
                ref basinCache,
                out cell);
        }

        /// <summary>符号付き水文セルX/Yを衝突しない64bitキャッシュキーへ詰めます。</summary>
        /// <param name="coordinateX">水平水文セル座標です。</param>
        /// <param name="coordinateY">垂直水文セル座標です。</param>
        /// <returns>Xを上位32bit、Yを下位32bitへ保持する可逆キーです。</returns>
        private static long CreateCoordinateKey(int coordinateX, int coordinateY)
        {
            return ((long)coordinateX << 32) | (uint)coordinateY;
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
        /// 固定水文セルをMap Viewer用の不透明色へ変換します。
        /// GameビューOverlayとRGBを共有し、凡例・広域Texture・テストで打切り流域の意味がずれないようにします。
        /// </summary>
        /// <param name="viewMode">流向・終端・集水・表流水・湖の広域表示モードです。</param>
        /// <param name="cell">決定論的Generatorが返した固定水文セルです。</param>
        /// <param name="hasCell">生成成功済みのセルならtrueです。falseは生成待ち色になります。</param>
        /// <returns>Map Viewer背景へ直接設定できるAlpha 255の色です。</returns>
        public static Color32 GetHydrologyDisplayColor(
            WorldGenerationMapViewMode viewMode,
            in GeneratedHydrologyCell cell,
            bool hasCell = true)
        {
            WorldGenerationDebugVisualizationMode overlayMode;
            switch (viewMode)
            {
                case WorldGenerationMapViewMode.HydrologyFlowDirection:
                    overlayMode = WorldGenerationDebugVisualizationMode.HydrologyFlowDirection;
                    break;
                case WorldGenerationMapViewMode.HydrologyTerminal:
                    overlayMode = WorldGenerationDebugVisualizationMode.HydrologyTerminal;
                    break;
                case WorldGenerationMapViewMode.HydrologyCatchment:
                    overlayMode = WorldGenerationDebugVisualizationMode.HydrologyCatchment;
                    break;
                case WorldGenerationMapViewMode.HydrologySurfaceWater:
                    overlayMode = WorldGenerationDebugVisualizationMode.HydrologySurfaceWater;
                    break;
                case WorldGenerationMapViewMode.HydrologyLake:
                    overlayMode = WorldGenerationDebugVisualizationMode.HydrologyLake;
                    break;
                default:
                    return new Color32(0, 0, 0, 0);
            }

            Color32 color = WorldGenerationDebugVisualizationOverlay
                .GetHydrologyVisualizationColor(overlayMode, cell, hasCell);
            color.a = byte.MaxValue;
            return color;
        }

        /// <summary>
        /// 完成チャンクの水文セル、表流水、自然水文タイルをMap Viewer用の不透明色へ変換します。
        /// GameビューOverlayと同じ色関数へ同じ固定値を渡し、RGBと分類境界を2つの診断ツールで共有します。
        /// </summary>
        /// <param name="viewMode">地下水、自然飽和、泉、自然冠水、湿原候補の広域表示モードです。</param>
        /// <param name="cell">地下水ポテンシャルと接続状態を持つ完成水文セルです。</param>
        /// <param name="hydrologyTile">自然飽和、自然冠水、湿原フラグを持つ完成タイルです。</param>
        /// <param name="surfaceWaterTile">最終確定した泉流を持つ完成表流水タイルです。</param>
        /// <param name="hasCell">担当水文セルが完成している場合はtrueです。</param>
        /// <param name="hasTile">2つのタイルBufferが完成している場合はtrueです。</param>
        /// <returns>Map Viewer背景へ直接設定できるAlpha 255の色です。</returns>
        public static Color32 GetHydrologyDisplayColor(
            WorldGenerationMapViewMode viewMode,
            in GeneratedHydrologyCell cell,
            in GeneratedHydrologyTile hydrologyTile,
            in GeneratedSurfaceWaterTile surfaceWaterTile,
            bool hasCell = true,
            bool hasTile = true)
        {
            if (IsBaseHydrologyViewMode(viewMode))
            {
                return GetHydrologyDisplayColor(viewMode, cell, hasCell);
            }

            WorldGenerationDebugVisualizationMode overlayMode;
            switch (viewMode)
            {
                case WorldGenerationMapViewMode.HydrologyGroundwater:
                    overlayMode = WorldGenerationDebugVisualizationMode.HydrologyGroundwater;
                    break;
                case WorldGenerationMapViewMode.HydrologyNaturalSoilSaturation:
                    overlayMode = WorldGenerationDebugVisualizationMode.HydrologyNaturalSoilSaturation;
                    break;
                case WorldGenerationMapViewMode.HydrologySpring:
                    overlayMode = WorldGenerationDebugVisualizationMode.HydrologySpring;
                    break;
                case WorldGenerationMapViewMode.HydrologyNaturalFlooding:
                    overlayMode = WorldGenerationDebugVisualizationMode.HydrologyNaturalFlooding;
                    break;
                case WorldGenerationMapViewMode.HydrologyWetlandCandidate:
                    overlayMode = WorldGenerationDebugVisualizationMode.HydrologyWetlandCandidate;
                    break;
                default:
                    return new Color32(0, 0, 0, 0);
            }

            Color32 color = WorldGenerationDebugVisualizationOverlay
                .GetHydrologyVisualizationColor(
                    overlayMode,
                    cell,
                    hydrologyTile,
                    surfaceWaterTile,
                    hasCell,
                    hasTile);
            color.a = byte.MaxValue;
            return color;
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
        /// 新18種類の論理バイオームを、C5-Cの仮Tileと同じ識別色へ変換します。
        /// </summary>
        /// <param name="biome">色を取得する論理バイオームです。</param>
        /// <returns>凡例と画像で共有する不透明色です。</returns>
        public static Color32 GetBiomeColor(BiomeId biome)
        {
            switch (biome)
            {
                case BiomeId.TemperateGrassland:
                    return new Color32(112, 169, 86, 255);
                case BiomeId.TropicalSavannaWoodland:
                    return new Color32(150, 166, 75, 255);
                case BiomeId.TemperateDeciduousForest:
                    return new Color32(79, 139, 70, 255);
                case BiomeId.WarmTemperateEvergreenForest:
                    return new Color32(50, 122, 77, 255);
                case BiomeId.TemperateMontaneBambooForest:
                    return new Color32(91, 151, 88, 255);
                case BiomeId.TropicalRainforest:
                    return new Color32(38, 112, 67, 255);
                case BiomeId.TropicalSeasonalForest:
                    return new Color32(68, 133, 61, 255);
                case BiomeId.SubtropicalOceanicForest:
                    return new Color32(58, 137, 105, 255);
                case BiomeId.BorealConiferForest:
                    return new Color32(66, 112, 82, 255);
                case BiomeId.Steppe:
                    return new Color32(174, 162, 82, 255);
                case BiomeId.DryShrublandWoodland:
                    return new Color32(154, 130, 74, 255);
                case BiomeId.CactusDesertDryPlateau:
                    return new Color32(188, 151, 83, 255);
                case BiomeId.HotDesert:
                    return new Color32(218, 181, 101, 255);
                case BiomeId.FreshwaterMarshFloodplain:
                    return new Color32(77, 142, 119, 255);
                case BiomeId.SwampForest:
                    return new Color32(54, 116, 90, 255);
                case BiomeId.TundraPolarBarrens:
                    return new Color32(190, 214, 218, 255);
                case BiomeId.AlpineRockland:
                    return new Color32(126, 122, 135, 255);
                case BiomeId.VolcanicGeothermal:
                    return new Color32(132, 68, 57, 255);
                default:
                    // Noneや未定義IDを温帯草原へ見せないことで、分類漏れを診断画像上でも発見できます。
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
