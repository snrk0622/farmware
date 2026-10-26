// このファイルは、製品地形表示で共通使用するWall固定影・折れ目影・動的影の寸法と、露出したWall根元を横連続区間へ圧縮する純粋計算を提供します。
// ChunkTilemapPresenterは結果だけをTilemapとShadow Caster 2Dへ渡し、材質IDや表示原点を影形状の判定へ混ぜません。

using System;
using System.Collections.Generic;
using Farmware.Simulation;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 1行内で連続する露出Wall根元を、動的影用Shadow Caster 2Dへ渡す長方形1個として表します。
    /// TerrainWallDynamicShadowChunkPresentationがチャンク表示の寿命中だけ読み取り、Simulation状態として保存しません。
    /// </summary>
    public readonly struct TerrainWallShadowRun
    {
        /// <summary>論理チャンク左端を0とする連続区間の開始Xです。</summary>
        public int StartX { get; }

        /// <summary>論理チャンク下端を0とする、根元を投影するWall上面のYです。</summary>
        public int Y { get; }

        /// <summary>同じ行で連続するWallセル数です。常に1以上です。</summary>
        public int Length { get; }

        /// <summary>検証済みの開始位置と長さを変更不能な値として保持します。</summary>
        /// <param name="startX">論理チャンク内の開始Xです。</param>
        /// <param name="y">論理チャンク内のWall上面Yです。</param>
        /// <param name="length">連続するセル数です。</param>
        public TerrainWallShadowRun(int startX, int y, int length)
        {
            if (length <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), length, "Wall影区間の長さは1以上が必要です。");
            }

            StartX = startX;
            Y = y;
            Length = length;
        }
    }

    /// <summary>
    /// 材質に依存しないWall影の固定値と分類処理をまとめます。
    /// EditModeテストとRuntime表示が同じ1px・3px・32px規則を共有するために使用します。
    /// </summary>
    public static class TerrainWallShadowUtility
    {
        /// <summary>GroundなどWall投影影を受ける描画へ使う既定Sorting Layer名です。</summary>
        public const string GroundShadowReceiverSortingLayerName = "Default";

        /// <summary>
        /// Wall上面・Face・折れ目影をGroundの影受け面から分離するSorting Layer名です。
        /// 影ありLightはこのLayerを照らさず、同位置の影なしLightで壁の明るさだけを再現します。
        /// </summary>
        public const string WallSortingLayerName = "TerrainWall";

        /// <summary>地形Spriteと影の1 Unity Unit当たりの画素数です。</summary>
        public const int PixelsPerUnit = 16;

        /// <summary>固定接地影を左右へ広げる画素数です。</summary>
        public const int ContactShadowHorizontalSpreadPixels = 1;

        /// <summary>固定接地影を画面手前へ移す画素数です。画面手前は負Yなので負値です。</summary>
        public const int ContactShadowOffsetYPixels = -3;

        /// <summary>左右それぞれの固定接地影へ掛けるAlphaです。</summary>
        public const float ContactShadowLayerOpacity = 0.22f;

        /// <summary>Wall前面上端での折れ目影Alphaです。</summary>
        public const float FoldShadowTopAlpha = 0.35f;

        /// <summary>Wall前面2段を通して連続減衰する折れ目影の画素数です。</summary>
        public const int FoldShadowDepthPixels = 32;

        /// <summary>動的影の起点をWall上面から最下段Face根元へ移すタイル数です。</summary>
        public const int DynamicShadowCasterOffsetYInTiles =
            -TerrainMaterialVerticalProjectionUtility.WallFaceDepth;

        /// <summary>
        /// 16×16サブチャンクの露出したWall根元を走査し、同じ行の隣接根元を材質に関係なく1区間へまとめます。
        /// 呼び出し元がListを再利用することで、Dirty更新ごとのmanaged allocationを避けます。
        /// </summary>
        /// <param name="rootMask">行優先256要素の露出根元有無です。材質IDは含めません。</param>
        /// <param name="localStartX">論理チャンク内におけるサブチャンク左端Xです。</param>
        /// <param name="localStartY">論理チャンク内におけるサブチャンク下端Yです。</param>
        /// <param name="runs">結果を書き込む再利用Listです。呼び出し時に内容を消去します。</param>
        public static void BuildHorizontalRuns(
            bool[] rootMask,
            int localStartX,
            int localStartY,
            List<TerrainWallShadowRun> runs)
        {
            int expectedLength = WorldGridConstants.SubChunkSize * WorldGridConstants.SubChunkSize;
            if (rootMask == null || rootMask.Length != expectedLength)
            {
                throw new ArgumentException($"Wall根元マスクには{expectedLength}要素が必要です。", nameof(rootMask));
            }

            if (runs == null)
            {
                throw new ArgumentNullException(nameof(runs));
            }

            runs.Clear();
            for (int row = 0; row < WorldGridConstants.SubChunkSize; row++)
            {
                int column = 0;
                while (column < WorldGridConstants.SubChunkSize)
                {
                    int maskIndex = (row * WorldGridConstants.SubChunkSize) + column;
                    if (!rootMask[maskIndex])
                    {
                        column++;
                        continue;
                    }

                    int runStart = column;
                    // 材質IDを比較せず根元の連続だけを見るため、異材質Wallの共有辺にも影の切れ目を作りません。
                    while (column < WorldGridConstants.SubChunkSize
                           && rootMask[(row * WorldGridConstants.SubChunkSize) + column])
                    {
                        column++;
                    }

                    runs.Add(new TerrainWallShadowRun(
                        localStartX + runStart,
                        localStartY + row,
                        column - runStart));
                }
            }
        }

        /// <summary>
        /// 現在の有効地形から、画面手前側にWallがなくFaceを表示するWallセルだけを根元マスクへ書き込みます。
        /// 全Wallを2タイル手前へ投影すると奥側Casterが手前のWall上面へ重なって自己影を作るため、露出前縁だけへ限定します。
        /// </summary>
        /// <param name="neighborhood">中心と周囲8チャンクの完成済み有効地形です。</param>
        /// <param name="localStartX">論理チャンク内におけるサブチャンク左端Xです。</param>
        /// <param name="localStartY">論理チャンク内におけるサブチャンク下端Yです。</param>
        /// <param name="rootMask">行優先256要素の再利用配列です。全要素を上書きします。</param>
        public static void FillExposedSouthRootMask(
            in CompletedChunkTerrainNeighborhood neighborhood,
            int localStartX,
            int localStartY,
            bool[] rootMask)
        {
            int expectedLength = WorldGridConstants.SubChunkSize * WorldGridConstants.SubChunkSize;
            if (rootMask == null || rootMask.Length != expectedLength)
            {
                throw new ArgumentException($"Wall根元マスクには{expectedLength}要素が必要です。", nameof(rootMask));
            }

            for (int localY = 0; localY < WorldGridConstants.SubChunkSize; localY++)
            {
                for (int localX = 0; localX < WorldGridConstants.SubChunkSize; localX++)
                {
                    int chunkLocalX = localStartX + localX;
                    int chunkLocalY = localStartY + localY;
                    int destinationIndex = (localY * WorldGridConstants.SubChunkSize) + localX;
                    bool hasCurrent = neighborhood.TryGetTile(
                        chunkLocalX,
                        chunkLocalY,
                        out GeneratedTerrainCell current);
                    bool hasSouth = neighborhood.TryGetTile(
                        chunkLocalX,
                        chunkLocalY - 1,
                        out GeneratedTerrainCell south);

                    // 南隣がワールド外または空なら前面が露出します。完成済みの南隣がWallなら内部セルなのでCasterを作りません。
                    rootMask[destinationIndex] = hasCurrent
                        && current.WallBlockRuntimeId.IsValid
                        && (!hasSouth || !south.WallBlockRuntimeId.IsValid);
                }
            }
        }

        /// <summary>
        /// Wall前面上端からの画素行に対し、32px全体で段境界をリセットしない折れ目影Alphaを返します。
        /// </summary>
        /// <param name="globalPixelRow">1段目上端を0、2段目上端を16とする画素行です。</param>
        /// <returns>範囲内では0以上0.35以下、範囲外では0です。</returns>
        public static float CalculateFoldShadowAlpha(int globalPixelRow)
        {
            if (globalPixelRow < 0 || globalPixelRow >= FoldShadowDepthPixels)
            {
                return 0f;
            }

            // 最終対象行31をAlpha 0にするため、32ではなく31で正規化します。
            float fade = 1f - (globalPixelRow / (float)(FoldShadowDepthPixels - 1));
            return FoldShadowTopAlpha * fade;
        }
    }
}
