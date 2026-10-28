// このファイルは、淡水・海水・マグマの表示種別、深度、岸との接触辺、反射対象を純粋計算で分類します。
// ChunkTilemapPresenterは結果だけをDirtyサブチャンクへ反映し、論理Liquid量、通行、WorldLayerを変更しません。

using System;
using Farmware.Simulation;


namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 製品表示で区別する液体の種類です。
    /// ECSのLiquid Runtime IDを表示用Assetへ対応付けるためだけに使い、Simulation状態として保存しません。
    /// </summary>
    public enum TerrainLiquidVisualKind : byte
    {
        /// <summary>Liquidを持たない通常セル、または未解決のRuntime IDです。</summary>
        None = 0,

        /// <summary>川、池、泉流に使う半透明の淡水です。</summary>
        FreshWater = 1,

        /// <summary>海に使う半透明の海水です。</summary>
        SaltWater = 2,

        /// <summary>背後の断面を最初の段から隠す不透明なマグマです。</summary>
        Magma = 3
    }

    /// <summary>
    /// 1つの液体セルへ描く奥・左・右の接触線、左右の内角、裏側の終端クリップを7bitで表します。
    /// 形状だけを共有Tileの頂点Alphaへ渡し、明暗の動きは水際Shaderが共通時間から計算します。
    /// </summary>
    [Flags]
    public enum TerrainLiquidEdgeMask : byte
    {
        /// <summary>接触線を描かない内部セルです。</summary>
        None = 0,

        /// <summary>画面奥、すなわちワールドYが1大きい辺です。</summary>
        Back = 1 << 0,

        /// <summary>画面左、すなわちワールドXが1小さい辺です。</summary>
        Left = 1 << 1,

        /// <summary>画面右、すなわちワールドXが1大きい辺です。</summary>
        Right = 1 << 2,

        /// <summary>左奥の岸から隣の奥辺へつなぐ、セル上端3pxと接点の短い線です。</summary>
        LeftInnerCorner = 1 << 3,

        /// <summary>右奥の岸から隣の奥辺へつなぐ、セル上端3pxと接点の短い線です。</summary>
        RightInnerCorner = 1 << 4,

        /// <summary>左岸が地面の裏側へ回り込む終端で、左線の上端3pxを隠す印です。</summary>
        LeftBackEndpoint = 1 << 5,

        /// <summary>右岸が地面の裏側へ回り込む終端で、右線の上端3pxを隠す印です。</summary>
        RightBackEndpoint = 1 << 6
    }

    /// <summary>
    /// 水中へ続けるGround断面、または液面より上の岸の縁について、投影元と岸からの段数を保持します。
    /// Face素材は地形材質カタログから解決し、液体専用断面Spriteを作らないために使用します。
    /// </summary>
    public readonly struct TerrainLiquidFaceProjection
    {
        /// <summary>投影元Groundの材質Runtime IDを含む完成済みセルです。</summary>
        public GeneratedTerrainCell SourceCell { get; }

        /// <summary>中心チャンク左端を0とした投影元X座標です。</summary>
        public int SourceLocalX { get; }

        /// <summary>中心チャンク下端を0とした投影元Y座標です。</summary>
        public int SourceLocalY { get; }

        /// <summary>水際直下を0とする断面段です。床なしでは0〜5を使用します。</summary>
        public int FaceDepth { get; }

        /// <summary>検証済みの投影元と段数を変更不能な値として保持します。</summary>
        /// <param name="sourceCell">Ground材質を所有する完成済みセルです。</param>
        /// <param name="sourceLocalX">中心チャンク基準の投影元Xです。</param>
        /// <param name="sourceLocalY">中心チャンク基準の投影元Yです。</param>
        /// <param name="faceDepth">水際直下を0とする段数です。</param>
        public TerrainLiquidFaceProjection(
            GeneratedTerrainCell sourceCell,
            int sourceLocalX,
            int sourceLocalY,
            int faceDepth)
        {
            SourceCell = sourceCell;
            SourceLocalX = sourceLocalX;
            SourceLocalY = sourceLocalY;
            FaceDepth = faceDepth;
        }
    }

    /// <summary>
    /// 16px論理セルを保ったまま、液面を3px低く見せる製品表示規則を定義します。
    /// すべて純粋計算で、Tilemap、ECS Buffer、Assetを変更しません。
    /// </summary>
    public static class TerrainLiquidPresentationUtility
    {
        /// <summary>淡水・海水・マグマで共通して液面上端から隠す画素数です。</summary>
        public const int SurfaceClipPixels = 3;

        /// <summary>1つの論理地形セルを構成する画素数です。</summary>
        public const int PixelsPerCell = 16;

        /// <summary>接触辺3種、内角2種、終端クリップ2種を組み合わせた、Shaderへ渡せる最大マスク値です。</summary>
        public const int MaximumEdgeMask = 127;

        /// <summary>水際の明暗模様を繰り返す幅です。絶対座標をこの画素数で丸めて遠方のfloat精度低下を防ぎます。</summary>
        public const int ShorelinePatternPeriodPixels = 4096;

        /// <summary>直下床ありの青Overlayが最大Alphaへ達する、クリップ後の表示深度です。</summary>
        public const int WithFloorFadePixels = 45;

        /// <summary>直下床なしの青Overlayが不透明へ達する、クリップ後の表示深度です。</summary>
        public const int WithoutFloorFadePixels = 93;

        /// <summary>直下床ありの青Overlayが到達して維持する最大Alphaです。</summary>
        public const float WithFloorMaximumAlpha = 0.5f;

        /// <summary>直下床なしの青Overlayが到達して維持する最大Alphaです。</summary>
        public const float WithoutFloorMaximumAlpha = 1f;

        /// <summary>通常Faceを水越しに続ける最大段数です。</summary>
        public const int MaximumFaceDepth = 6;

        /// <summary>深度勾配が最大Alphaへ到達し、以降の水面を同じ濃度へ固定する表示段です。</summary>
        public const int SaturatedDepthStage = MaximumFaceDepth;



        /// <summary>垂直面反射Proxyを表示する最大段数です。16px×2段で32pxです。</summary>
        public const int MaximumReflectionDepth = 2;

        /// <summary>反射Proxyを斜め見下ろしへ合わせて縦方向へ圧縮する倍率です。</summary>
        public const float ReflectionVerticalCompression = 0.75f;

        /// <summary>水中床を隠しすぎない反射Proxyの最大Alphaです。</summary>
        public const float ReflectionMaximumAlpha = 0.22f;

        /// <summary>2段のWall Faceを0.75倍した、反射全体の表示深度です。水面から24pxで透明になります。</summary>
        public const float ReflectionVisibleDepthPixels = MaximumReflectionDepth * PixelsPerCell * ReflectionVerticalCompression;

        /// <summary>輪郭を動かすShaderとMeshが共有する最大横変位（素材px）です。Dirty更新時に両側へ余白を確保します。</summary>
        public const int ReflectionHorizontalPaddingPixels = 2;

        /// <summary>
        /// ECSの組み込みRuntime IDから製品表示用の液体種別を求めます。
        /// </summary>
        /// <param name="cell">表示する完成済み地形セルです。</param>
        /// <param name="runtimeIds">現在Worldの組み込みLiquid Runtime IDです。</param>
        /// <returns>淡水、海水、マグマ、またはNoneです。</returns>
        public static TerrainLiquidVisualKind Classify(
            in GeneratedTerrainCell cell,
            in BuiltInTerrainBlockRuntimeIds runtimeIds)
        {
            if (!cell.LiquidRuntimeId.IsValid)
            {
                return TerrainLiquidVisualKind.None;
            }

            if (cell.LiquidRuntimeId.Value == runtimeIds.FreshWater.Value)
            {
                return TerrainLiquidVisualKind.FreshWater;
            }

            if (cell.LiquidRuntimeId.Value == runtimeIds.SaltWater.Value)
            {
                return TerrainLiquidVisualKind.SaltWater;
            }

            return cell.LiquidRuntimeId.Value == runtimeIds.Magma.Value
                ? TerrainLiquidVisualKind.Magma
                : TerrainLiquidVisualKind.None;
        }

        /// <summary>
        /// 現在の液体セルがGroundまたはWallへ接する奥・左・右の辺、斜め奥の内角、裏側の終端を選びます。
        /// 手前辺は斜め見下ろしで前景を横切るため、隣接地形にかかわらず選択しません。
        /// </summary>
        /// <param name="neighborhood">中心と周囲8チャンクの完成済み地形です。</param>
        /// <param name="localX">中心チャンク基準の現在セルXです。</param>
        /// <param name="localY">中心チャンク基準の現在セルYです。</param>
        /// <returns>0〜127の接触形状です。現在セルが液体でなければNoneです。</returns>
        public static TerrainLiquidEdgeMask SelectEdgeMask(
            in CompletedChunkTerrainNeighborhood neighborhood,
            int localX,
            int localY)
        {
            if (!neighborhood.TryGetTile(localX, localY, out GeneratedTerrainCell current)
                || !current.LiquidRuntimeId.IsValid)
            {
                return TerrainLiquidEdgeMask.None;
            }

            TerrainLiquidEdgeMask mask = TerrainLiquidEdgeMask.None;
            if (IsSolidNeighbor(neighborhood, localX, localY + 1))
            {
                mask |= TerrainLiquidEdgeMask.Back;
            }

            if (IsSolidNeighbor(neighborhood, localX - 1, localY))
            {
                mask |= TerrainLiquidEdgeMask.Left;
            }

            if (IsSolidNeighbor(neighborhood, localX + 1, localY))
            {
                mask |= TerrainLiquidEdgeMask.Right;
            }

            // 奥と横が同種の水で、斜め奥だけが岸なら、通常の3方向判定では内角の3pxが欠けます。
            // 水が実際につながる場合だけ短い線を補い、別種の液体や空セルをまたいで接続しません。
            if ((mask & TerrainLiquidEdgeMask.Back) == 0
                && IsSameLiquidNeighbor(neighborhood, localX, localY + 1, current))
            {
                // 横の岸が斜め奥へ続かない場所は、地面の裏側へ回り込む終端です。
                // 左右を別々に判定し、縦岸の途中や反対側の連続線まで3px切らないようにします。
                if ((mask & TerrainLiquidEdgeMask.Left) != 0
                    && !IsSolidNeighbor(neighborhood, localX - 1, localY + 1))
                {
                    mask |= TerrainLiquidEdgeMask.LeftBackEndpoint;
                }

                if ((mask & TerrainLiquidEdgeMask.Right) != 0
                    && !IsSolidNeighbor(neighborhood, localX + 1, localY + 1))
                {
                    mask |= TerrainLiquidEdgeMask.RightBackEndpoint;
                }

                if ((mask & TerrainLiquidEdgeMask.Left) == 0
                    && IsSameLiquidNeighbor(neighborhood, localX - 1, localY, current)
                    && IsSolidNeighbor(neighborhood, localX - 1, localY + 1))
                {
                    mask |= TerrainLiquidEdgeMask.LeftInnerCorner;
                }

                if ((mask & TerrainLiquidEdgeMask.Right) == 0
                    && IsSameLiquidNeighbor(neighborhood, localX + 1, localY, current)
                    && IsSolidNeighbor(neighborhood, localX + 1, localY + 1))
                {
                    mask |= TerrainLiquidEdgeMask.RightInnerCorner;
                }
            }

            return mask;
        }

        /// <summary>水際Shaderと同じ1px接触形状を、画素単位の回帰テスト用に求めます。状態は変更しません。</summary>
        /// <param name="mask">奥・左右・内角と裏側終端クリップの接触形状です。</param>
        /// <param name="pixelX">セル左端を0とする0〜15の画素列です。</param>
        /// <param name="pixelY">セル下端を0とする0〜15の画素行です。</param>
        /// <returns>この画素が静止した接触線上ならtrueです。</returns>
        public static bool IsShorelinePixel(TerrainLiquidEdgeMask mask, int pixelX, int pixelY)
        {
            if (pixelX < 0 || pixelX >= PixelsPerCell || pixelY < 0 || pixelY >= PixelsPerCell)
            {
                return false;
            }

            int top = PixelsPerCell - SurfaceClipPixels - 1;
            bool back = (mask & TerrainLiquidEdgeMask.Back) != 0;
            // 奥岸と裏側の終端だけ上端3pxを隠します。終端でない縦岸は全16pxを描いて接続を保ちます。
            bool leftRow = pixelY <= top || (!back && (mask & TerrainLiquidEdgeMask.LeftBackEndpoint) == 0);
            bool rightRow = pixelY <= top || (!back && (mask & TerrainLiquidEdgeMask.RightBackEndpoint) == 0);
            return (back && pixelY == top)
                || (pixelX == 0 && leftRow && (mask & TerrainLiquidEdgeMask.Left) != 0)
                || (pixelX == PixelsPerCell - 1 && rightRow && (mask & TerrainLiquidEdgeMask.Right) != 0)
                || (!back && pixelX == 0 && pixelY >= top && (mask & TerrainLiquidEdgeMask.LeftInnerCorner) != 0)
                || (!back && pixelX == PixelsPerCell - 1 && pixelY >= top && (mask & TerrainLiquidEdgeMask.RightInnerCorner) != 0);
        }

        /// <summary>表示原点を模様周期内の絶対画素オフセットへ変換し、Floating Originでも明暗の位置を保ちます。</summary>
        /// <param name="absoluteTileCoordinate">表示原点のXまたはYです。単位は論理タイルです。</param>
        /// <returns>0以上4096未満の画素オフセットです。</returns>
        public static int GetShorelineOriginPixelOffset(int absoluteTileCoordinate)
        {
            // 先にlongへ拡張することで、int座標の端でも16倍が周回しないようにします。
            long pixels = (long)absoluteTileCoordinate * PixelsPerCell;
            long remainder = pixels % ShorelinePatternPeriodPixels;
            return (int)(remainder < 0 ? remainder + ShorelinePatternPeriodPixels : remainder);
        }

        /// <summary>内角をつなぐ2つの隣接セルが、岸ではなく現在セルと同じ液体か確認します。</summary>
        /// <param name="neighborhood">差分適用済みの完成地形です。</param>
        /// <param name="localX">確認するXです。</param>
        /// <param name="localY">確認するYです。</param>
        /// <param name="current">比較元の液体セルです。</param>
        /// <returns>同種液体が岸に遮られず存在する場合はtrueです。</returns>
        private static bool IsSameLiquidNeighbor(
            in CompletedChunkTerrainNeighborhood neighborhood, int localX, int localY,
            in GeneratedTerrainCell current)
        {
            return neighborhood.TryGetTile(localX, localY, out GeneratedTerrainCell neighbor)
                && !neighbor.GroundBlockRuntimeId.IsValid
                && !neighbor.WallBlockRuntimeId.IsValid
                && neighbor.LiquidRuntimeId.Value == current.LiquidRuntimeId.Value;
        }

        /// <summary>
        /// 液体領域全体へ16pxごとの隙間を作らず、画面奥側が岸に接する先頭セルだけを3pxクリップするか判定します。
        /// </summary>
        /// <param name="neighborhood">中心と周囲8チャンクの完成済み地形です。</param>
        /// <param name="localX">中心チャンク基準の現在セルXです。</param>
        /// <param name="localY">中心チャンク基準の現在セルYです。</param>
        /// <returns>現在セルが液体で、画面奥側の隣接セルがGroundまたはWallならtrueです。</returns>
        public static bool ShouldClipSurfaceTop(
            in CompletedChunkTerrainNeighborhood neighborhood,
            int localX,
            int localY)
        {
            return neighborhood.TryGetTile(localX, localY, out GeneratedTerrainCell current)
                && current.LiquidRuntimeId.IsValid
                && IsSolidNeighbor(neighborhood, localX, localY + 1);
        }

        /// <summary>
        /// 水際から現在セルまで淡水・海水が連続する段数を求めます。水種の境界で断面距離をリセットしません。
        /// 6段より深い水域は勾配完了段へ丸め、描画範囲外の探索や全水域走査を行いません。
        /// </summary>
        /// <param name="neighborhood">中心と周囲8チャンクの完成済み地形です。</param>
        /// <param name="localX">中心チャンク基準の現在セルXです。</param>
        /// <param name="localY">中心チャンク基準の現在セルYです。</param>
        /// <param name="runtimeIds">淡水・海水・マグマを識別するRuntime IDです。</param>
        /// <returns>水際直下の0から、96px以深の勾配完了を表す6までの表示段です。水でなければ-1です。</returns>
        public static int GetClampedWaterDepth(
            in CompletedChunkTerrainNeighborhood neighborhood,
            int localX,
            int localY,
            in BuiltInTerrainBlockRuntimeIds runtimeIds)
        {
            if (!neighborhood.TryGetTile(localX, localY, out GeneratedTerrainCell current))
            {
                return -1;
            }

            TerrainLiquidVisualKind currentKind = Classify(current, runtimeIds);
            if (!IsTransparentWater(currentKind))
            {
                return -1;
            }

            for (int distance = 1; distance <= MaximumFaceDepth; distance++)
            {
                if (!neighborhood.TryGetTile(localX, localY + distance, out GeneratedTerrainCell candidate))
                {
                    return SaturatedDepthStage;
                }

                if (candidate.GroundBlockRuntimeId.IsValid
                    || candidate.WallBlockRuntimeId.IsValid)
                {
                    return distance - 1;
                }

                if (!IsTransparentWater(Classify(candidate, runtimeIds)))
                {
                    return SaturatedDepthStage;
                }
            }

            // 5を返すと最終16pxの勾配が深部の全タイルで繰り返され、横帯として見えるため、
            // 次の段6を返して開始時点から最大Alphaへ固定します。
            return SaturatedDepthStage;
        }


        /// <summary>全3液体に共通する奥岸の直下へ、液面より上に露出するGround断面を投影します。</summary>
        /// <param name="neighborhood">差分適用後の完成済み3×3チャンク近傍です。</param>
        /// <param name="localX">表示先の中心チャンク基準Xです。</param>
        /// <param name="localY">表示先の中心チャンク基準Yです。</param>
        /// <param name="runtimeIds">淡水・海水・マグマを識別するRuntime IDです。</param>
        /// <param name="projection">成功時に、直上のGround材質と段数0を受け取ります。</param>
        /// <returns>既知液体のすぐ奥にGroundがある場合はtrueです。Wallの下地Groundも含みます。</returns>
        public static bool TryProjectGroundFaceAtLiquidBank(
            in CompletedChunkTerrainNeighborhood neighborhood,
            int localX,
            int localY,
            in BuiltInTerrainBlockRuntimeIds runtimeIds,
            out TerrainLiquidFaceProjection projection)
        {
            projection = default;
            // 遠方の岸を透視せず、Surface上端3pxを切るセルと同じ直上の有効地形だけを読みます。
            if (!neighborhood.TryGetTile(localX, localY, out GeneratedTerrainCell current)
                || Classify(current, runtimeIds) == TerrainLiquidVisualKind.None
                || !neighborhood.TryGetTile(localX, localY + 1, out GeneratedTerrainCell source)
                || !source.GroundBlockRuntimeId.IsValid)
            {
                return false;
            }

            projection = new TerrainLiquidFaceProjection(source, localX, localY + 1, 0);
            return true;
        }

        /// <summary>
        /// 淡水・海水が奥側Groundまで連続する場合に、液体越しへ通常Faceを最大6段投影します。
        /// Ground上のWallは、その設置面Groundを水中断面の元にします。マグマ、未知の液体、GroundのないWall、空セルは越えません。
        /// </summary>
        /// <param name="neighborhood">中心と周囲8チャンクの完成済み地形です。</param>
        /// <param name="localX">中心チャンク基準の表示先Xです。</param>
        /// <param name="localY">中心チャンク基準の表示先Yです。</param>
        /// <param name="runtimeIds">現在Worldの組み込みLiquid Runtime IDです。</param>
        /// <param name="projection">成功時にGround材質と0〜5段目の情報を受け取ります。</param>
        /// <returns>表示先が淡水または海水で、6段以内の奥側にGroundがある場合はtrueです。</returns>
        public static bool TryProjectGroundFaceThroughWater(
            in CompletedChunkTerrainNeighborhood neighborhood,
            int localX,
            int localY,
            in BuiltInTerrainBlockRuntimeIds runtimeIds,
            out TerrainLiquidFaceProjection projection)
        {
            projection = default;
            if (!neighborhood.TryGetTile(localX, localY, out GeneratedTerrainCell current))
            {
                return false;
            }

            TerrainLiquidVisualKind currentKind = Classify(current, runtimeIds);
            if (!IsTransparentWater(currentKind))
            {
                return false;
            }

            // 水面より上の縁はマグマと同じ岸判定です。水だけは続いて深い断面も探索できます。
            if (TryProjectGroundFaceAtLiquidBank(neighborhood, localX, localY, runtimeIds, out projection))
            {
                return true;
            }

            for (int distance = 1; distance <= MaximumFaceDepth; distance++)
            {
                int sourceY = localY + distance;
                if (!neighborhood.TryGetTile(localX, sourceY, out GeneratedTerrainCell candidate))
                {
                    return false;
                }

                if (candidate.GroundBlockRuntimeId.IsValid)
                {
                    projection = new TerrainLiquidFaceProjection(
                        candidate,
                        localX,
                        sourceY,
                        distance - 1);
                    return true;
                }

                if (candidate.WallBlockRuntimeId.IsValid)
                {
                    // Wallの水面より上のFace・反射とは別に、設置面Groundの水中断面が必要です。
                    // Groundを持たないWallだけは材質を推測せず、ここで投影を止めます。
                    return false;
                }

                if (!IsTransparentWater(Classify(candidate, runtimeIds)))
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// 淡水・海水セルの奥側2段以内にあるWall前面を、水面反射Proxyの投影元として求めます。
        /// 水平なGroundTop、WallTop、床は入力候補にしないため反射へ混入しません。
        /// </summary>
        /// <param name="neighborhood">中心と周囲8チャンクの完成済み地形です。</param>
        /// <param name="localX">中心チャンク基準の反射元列Xです。受け手の左右1列も探索します。</param>
        /// <param name="localY">受け手水面と同じ高さの探索開始Yです。</param>
        /// <param name="runtimeIds">現在Worldの組み込みLiquid Runtime IDです。</param>
        /// <param name="receivingWaterKind">実際に描く受け手セルの液体種別です。反射元列が陸地でも水域の条件はこの値で判定します。</param>
        /// <param name="projection">成功時にWallのセルと接地点座標を受け取ります。描くFaceは別途高さから選びます。</param>
        /// <returns>投影が届く範囲の最寄りWallがある場合はtrueです。地面上の区間は後段の矩形交差で隠します。</returns>
        public static bool TryFindWallReflectionSource(
            in CompletedChunkTerrainNeighborhood neighborhood,
            int localX,
            int localY,
            in BuiltInTerrainBlockRuntimeIds runtimeIds,
            TerrainLiquidVisualKind receivingWaterKind,
            out TerrainMaterialFaceProjection projection)
        {
            projection = default;
            if (!neighborhood.TryGetTile(localX, localY, out _))
            {
                return false;
            }

            if (!IsTransparentWater(receivingWaterKind))
            {
                return false;
            }

            for (int distance = 1; distance <= MaximumReflectionDepth; distance++)
            {
                int sourceY = localY + distance;
                if (!neighborhood.TryGetTile(localX, sourceY, out GeneratedTerrainCell candidate))
                {
                    return false;
                }

                if (candidate.WallBlockRuntimeId.IsValid)
                {
                    projection = new TerrainMaterialFaceProjection(
                        candidate,
                        localX,
                        sourceY,
                        0,
                        isWall: true);
                    return true;
                }

                // 岸から1セル離れたWallでも投影が届く可能性があります。地面は反射元にせず探索だけ通します。
                // 淡水と海水の境界は連続した水面です。マグマ・未知の液体・空の穴だけは越えません。
                if (!candidate.GroundBlockRuntimeId.IsValid && !IsTransparentWater(Classify(candidate, runtimeIds)))
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// 断面・減衰距離・反射元の探索中に、連続した透明水域として通過できる種別かを返します。
        /// 描画探索だけの判定であり、各セルの液体IDや水色は変更しません。淡水と海水の境目に偽の深部を作らないため共用します。
        /// </summary>
        /// <param name="kind">完成地形から分類した液体の表示種別です。</param>
        /// <returns>淡水または海水の場合はtrue、マグマ・未知の液体・空セルはfalseです。</returns>
        private static bool IsTransparentWater(TerrainLiquidVisualKind kind)
        {
            return kind == TerrainLiquidVisualKind.FreshWater || kind == TerrainLiquidVisualKind.SaltWater;
        }

        /// <summary>
        /// 穴上端からの画素深度に対する青Overlay Alphaを、3pxクリップ込みで返します。
        /// </summary>
        /// <param name="depthFromTopPixels">穴上端を0とする非負の画素深度です。</param>
        /// <param name="hasUnderwaterFloor">直下1層に表示可能な床がある場合はtrueです。</param>
        /// <returns>床ありは48pxで0.5、床なしは96pxで1へ達し、その後は最大値を維持します。</returns>
        public static float GetDepthOverlayAlpha(
            float depthFromTopPixels,
            bool hasUnderwaterFloor)
        {
            if (depthFromTopPixels <= SurfaceClipPixels)
            {
                return 0f;
            }

            int fadePixels = hasUnderwaterFloor
                ? WithFloorFadePixels
                : WithoutFloorFadePixels;
            float maximumAlpha = hasUnderwaterFloor
                ? WithFloorMaximumAlpha
                : WithoutFloorMaximumAlpha;
            float normalizedDepth = (depthFromTopPixels - SurfaceClipPixels) / (float)fadePixels;
            if (normalizedDepth >= 1f)
            {
                return maximumAlpha;
            }

            return normalizedDepth * maximumAlpha;
        }


        /// <summary>接地点に近い下段から反射するため、上端0のWall Face段数へ変換します。</summary>
        /// <param name="heightStage">接地点から上へ数えた0〜1の段です。</param>
        /// <returns>元WallのFace段数です。範囲外は-1を返し、画像は選択しません。</returns>
        public static int GetReflectedWallFaceDepth(int heightStage)
        {
            return heightStage >= 0 && heightStage < MaximumReflectionDepth
                ? MaximumReflectionDepth - 1 - heightStage : -1;
        }

        /// <summary>水面から離れるほど反射を薄くし、24pxの投影終端で矩形状に切れない可視率を返します。</summary>
        /// <param name="depthFromSurfacePixels">3px低い実水面を0とする表示画素深度です。</param>
        /// <returns>水面より上は0、水面で1、24px以深で0です。</returns>
        public static float GetReflectionVisibility(float depthFromSurfacePixels)
        {
            if (depthFromSurfacePixels < 0f)
            {
                return 0f;
            }
            float visibility = 1f - depthFromSurfacePixels / ReflectionVisibleDepthPixels;
            return visibility > 0f ? visibility : 0f;
        }

        /// <summary>指定セルがGroundまたはWallを持つ岸か判定します。</summary>
        /// <param name="neighborhood">完成済み地形近傍です。</param>
        /// <param name="localX">確認する中心基準Xです。</param>
        /// <param name="localY">確認する中心基準Yです。</param>
        /// <returns>完成セルが存在し、GroundまたはWallを持つ場合はtrueです。</returns>
        private static bool IsSolidNeighbor(
            in CompletedChunkTerrainNeighborhood neighborhood,
            int localX,
            int localY)
        {
            return neighborhood.TryGetTile(localX, localY, out GeneratedTerrainCell neighbor)
                && (neighbor.GroundBlockRuntimeId.IsValid
                    || neighbor.WallBlockRuntimeId.IsValid);
        }

        /// <summary>C#の負剰余を0以上へ直し、負のWorld座標や時間入力でも位相を固定します。</summary>
        /// <param name="value">剰余を求める整数です。</param>
        /// <param name="modulus">正の周期です。</param>
        /// <returns>0以上modulus未満の剰余です。</returns>
        private static int PositiveModulo(int value, int modulus)
        {
            int remainder = value % modulus;
            return remainder < 0 ? remainder + modulus : remainder;
        }
    }
}
