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
    /// 1つの液体セルへ描く奥・左・右の接触線を3bitで表します。
    /// 0を除く1〜7は、Editorが生成する7種類の水際アニメーションTileへそのまま対応します。
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
        Right = 1 << 2
    }

    /// <summary>
    /// 液体越しに続ける通常Ground断面の投影元と、水際からの段数を保持します。
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

        /// <summary>垂直面反射Proxyを表示する最大段数です。16px×2段で32pxです。</summary>
        public const int MaximumReflectionDepth = 2;

        /// <summary>反射Proxyを斜め見下ろしへ合わせて縦方向へ圧縮する倍率です。</summary>
        public const float ReflectionVerticalCompression = 0.75f;

        /// <summary>水中床を隠しすぎない反射Proxyの最大Alphaです。</summary>
        public const float ReflectionMaximumAlpha = 0.22f;

        /// <summary>行別の反射変位を進める速度です。単位は画素段階/秒です。</summary>
        public const float ReflectionWavePixelsPerSecond = 4f;

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
        /// 現在の液体セルがGroundまたはWallへ接する奥・左・右の辺を選びます。
        /// 手前辺は斜め見下ろしで前景を横切るため、隣接地形にかかわらず選択しません。
        /// </summary>
        /// <param name="neighborhood">中心と周囲8チャンクの完成済み地形です。</param>
        /// <param name="localX">中心チャンク基準の現在セルXです。</param>
        /// <param name="localY">中心チャンク基準の現在セルYです。</param>
        /// <returns>0〜7の3bit接触辺です。現在セルが液体でなければNoneです。</returns>
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

            return mask;
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
        /// 水際から現在セルまで同じ水が連続する段数を求めます。
        /// 6段より深い水域は最深段へ丸め、描画範囲外の探索や全水域走査を行いません。
        /// </summary>
        /// <param name="neighborhood">中心と周囲8チャンクの完成済み地形です。</param>
        /// <param name="localX">中心チャンク基準の現在セルXです。</param>
        /// <param name="localY">中心チャンク基準の現在セルYです。</param>
        /// <param name="runtimeIds">淡水・海水・マグマを識別するRuntime IDです。</param>
        /// <returns>水際直下の0から、6段以上を表す5までの表示段です。水でなければ-1です。</returns>
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
            if (currentKind != TerrainLiquidVisualKind.FreshWater
                && currentKind != TerrainLiquidVisualKind.SaltWater)
            {
                return -1;
            }

            for (int distance = 1; distance <= MaximumFaceDepth; distance++)
            {
                if (!neighborhood.TryGetTile(localX, localY + distance, out GeneratedTerrainCell candidate))
                {
                    return MaximumFaceDepth - 1;
                }

                if (candidate.GroundBlockRuntimeId.IsValid
                    || candidate.WallBlockRuntimeId.IsValid)
                {
                    return distance - 1;
                }

                if (Classify(candidate, runtimeIds) != currentKind)
                {
                    return MaximumFaceDepth - 1;
                }
            }

            return MaximumFaceDepth - 1;
        }

        /// <summary>
        /// 同じ水が奥側Groundまで連続する場合に、液体越しへ通常Faceを最大6段投影します。
        /// マグマ、別液体、Wall、空セルを越えず、液体専用断面Spriteを必要としません。
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
            if (currentKind != TerrainLiquidVisualKind.FreshWater
                && currentKind != TerrainLiquidVisualKind.SaltWater)
            {
                return false;
            }

            for (int distance = 1; distance <= MaximumFaceDepth; distance++)
            {
                int sourceY = localY + distance;
                if (!neighborhood.TryGetTile(localX, sourceY, out GeneratedTerrainCell candidate))
                {
                    return false;
                }

                if (candidate.WallBlockRuntimeId.IsValid)
                {
                    // Wall前面とその反射は別経路で扱い、Ground Faceを同じセルへ重ねません。
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

                if (Classify(candidate, runtimeIds) != currentKind)
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
        /// <param name="localX">中心チャンク基準の水面Xです。</param>
        /// <param name="localY">中心チャンク基準の水面Yです。</param>
        /// <param name="runtimeIds">現在Worldの組み込みLiquid Runtime IDです。</param>
        /// <param name="projection">成功時にWallのFace投影元と0〜1段目を受け取ります。</param>
        /// <returns>同じ水がWallまで連続し、32px以内へ垂直面だけを映せる場合はtrueです。</returns>
        public static bool TryProjectWallReflection(
            in CompletedChunkTerrainNeighborhood neighborhood,
            int localX,
            int localY,
            in BuiltInTerrainBlockRuntimeIds runtimeIds,
            out TerrainMaterialFaceProjection projection)
        {
            projection = default;
            if (!neighborhood.TryGetTile(localX, localY, out GeneratedTerrainCell current))
            {
                return false;
            }

            TerrainLiquidVisualKind currentKind = Classify(current, runtimeIds);
            if (currentKind != TerrainLiquidVisualKind.FreshWater
                && currentKind != TerrainLiquidVisualKind.SaltWater)
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
                        distance - 1,
                        isWall: true);
                    return true;
                }

                if (Classify(candidate, runtimeIds) != currentKind)
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// 穴上端からの画素深度に対する青Overlay Alphaを、3pxクリップ込みで返します。
        /// </summary>
        /// <param name="depthFromTopPixels">穴上端を0とする非負の画素深度です。</param>
        /// <param name="hasUnderwaterFloor">直下1層に表示可能な床がある場合はtrueです。</param>
        /// <returns>床ありは48pxで0.5、床なしは96pxで1へ達し、その後は最大値を維持します。</returns>
        public static float GetDepthOverlayAlpha(
            int depthFromTopPixels,
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

        /// <summary>16px段の上端に対応する青Overlay Alphaを返します。</summary>
        /// <param name="depthStage">水際直下を0とする非負の表示段です。</param>
        /// <param name="hasUnderwaterFloor">直下1層に表示可能な床がある場合はtrueです。</param>
        /// <returns>Shaderへ頂点色として渡す段開始Alphaです。</returns>
        public static float GetDepthStageStartAlpha(
            int depthStage,
            bool hasUnderwaterFloor)
        {
            return depthStage < 0
                ? 0f
                : GetDepthOverlayAlpha(depthStage * PixelsPerCell, hasUnderwaterFloor);
        }

        /// <summary>
        /// 4段階の時間と画素行から、0、+1、0、-1の整数水平変位を行別位相付きで返します。
        /// Shaderと同じ規則をCPUテストでも検証できるようにします。
        /// </summary>
        /// <param name="animationStep">毎秒4段階で増える整数時間です。</param>
        /// <param name="pixelRow">反射Sprite内の0以上の画素行です。</param>
        /// <returns>-1、0、+1のいずれかの画素変位です。</returns>
        public static int GetReflectionHorizontalOffset(int animationStep, int pixelRow)
        {
            int phase = PositiveModulo(animationStep + pixelRow, 4);
            if (phase == 1)
            {
                return 1;
            }

            return phase == 3 ? -1 : 0;
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
