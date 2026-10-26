// このファイルは、完成済み地形セルからGround崖面3段とWall前面2段の投影元を求めます。
// ChunkTilemapPresenterはこの純粋な分類結果を使い、セルごとのGameObjectを作らずDirtyサブチャンクへ完成Spriteを一括反映します。

using Farmware.Simulation;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 1つの表示セルへ投影する断面の元セル、用途、上端からの段数をまとめます。
    /// 製品PresenterとEditModeテストが、チャンク境界を含む同じ投影規則を共有するために使用します。
    /// </summary>
    public readonly struct TerrainMaterialFaceProjection
    {
        /// <summary>断面の材質Runtime IDを所有する完成済み元セルです。</summary>
        public GeneratedTerrainCell SourceCell { get; }

        /// <summary>中心チャンク左端を0とした投影元の水平座標です。隣接チャンクでは範囲外になれます。</summary>
        public int SourceLocalX { get; }

        /// <summary>中心チャンク下端を0とした投影元の垂直座標です。隣接チャンクでは範囲外になれます。</summary>
        public int SourceLocalY { get; }

        /// <summary>断面上端を0とする段数です。Groundは0〜2、Wallは0〜1です。</summary>
        public int FaceDepth { get; }

        /// <summary>Wall前面ならtrue、Ground崖面ならfalseです。</summary>
        public bool IsWall { get; }

        /// <summary>検証済みの投影元と段数を変更不能な値として保持します。</summary>
        /// <param name="sourceCell">投影元の完成済み地形セルです。</param>
        /// <param name="sourceLocalX">中心チャンク基準の投影元Xです。</param>
        /// <param name="sourceLocalY">中心チャンク基準の投影元Yです。</param>
        /// <param name="faceDepth">断面上端を0とする段数です。</param>
        /// <param name="isWall">Wall前面ならtrueです。</param>
        public TerrainMaterialFaceProjection(
            GeneratedTerrainCell sourceCell,
            int sourceLocalX,
            int sourceLocalY,
            int faceDepth,
            bool isWall)
        {
            SourceCell = sourceCell;
            SourceLocalX = sourceLocalX;
            SourceLocalY = sourceLocalY;
            FaceDepth = faceDepth;
            IsWall = isWall;
        }
    }

    /// <summary>
    /// 画面手前を負Yとする表示規則に従い、現在セルの奥側からGround／Wall断面を投影します。
    /// 3×3完成チャンク近傍だけを読み、生成順や表示原点には依存しません。
    /// </summary>
    public static class TerrainMaterialVerticalProjectionUtility
    {
        /// <summary>乾燥したGround崖面を表示する固定段数です。</summary>
        public const int GroundFaceDepth = 3;

        /// <summary>Wall前面を表示する固定段数です。</summary>
        public const int WallFaceDepth = 2;

        /// <summary>16pxを3段連結した乾燥崖の連続減衰距離です。</summary>
        public const int DryCliffFadePixels = GroundFaceDepth * 16;

        /// <summary>
        /// 現在セルへ投影されるWall前面を、奥側1〜2セルの最初のWallから求めます。
        /// </summary>
        /// <param name="neighborhood">中心と周囲8チャンクの完成済み地形です。</param>
        /// <param name="localX">表示対象セルの中心チャンク基準Xです。</param>
        /// <param name="localY">表示対象セルの中心チャンク基準Yです。</param>
        /// <param name="projection">成功時にWall材質と0〜1段目の情報を受け取ります。</param>
        /// <returns>現在セル自身がWallでなく、奥側2セル以内にWallがある場合はtrueです。</returns>
        public static bool TryProjectWallFace(
            in CompletedChunkTerrainNeighborhood neighborhood,
            int localX,
            int localY,
            out TerrainMaterialFaceProjection projection)
        {
            projection = default;
            if (!neighborhood.TryGetTile(localX, localY, out GeneratedTerrainCell current)
                || current.WallBlockRuntimeId.IsValid)
            {
                return false;
            }

            for (int faceDepth = 0; faceDepth < WallFaceDepth; faceDepth++)
            {
                int sourceY = localY + faceDepth + 1;
                if (!neighborhood.TryGetTile(localX, sourceY, out GeneratedTerrainCell source))
                {
                    return false;
                }

                // 異材質かどうかは接続判定へ使いません。最初に見つかったWallの材質だけを前面Spriteへ使います。
                if (source.WallBlockRuntimeId.IsValid)
                {
                    projection = new TerrainMaterialFaceProjection(
                        source,
                        localX,
                        sourceY,
                        faceDepth,
                        isWall: true);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 液体もBlockもない乾燥穴へ投影されるGround崖面を、奥側1〜3セルの最初の上面から求めます。
        /// </summary>
        /// <param name="neighborhood">中心と周囲8チャンクの完成済み地形です。</param>
        /// <param name="localX">表示対象セルの中心チャンク基準Xです。</param>
        /// <param name="localY">表示対象セルの中心チャンク基準Yです。</param>
        /// <param name="projection">成功時にGround材質と0〜2段目の情報を受け取ります。</param>
        /// <returns>現在セルが乾燥穴で、奥側3セル以内にGround上面がある場合はtrueです。</returns>
        public static bool TryProjectGroundFace(
            in CompletedChunkTerrainNeighborhood neighborhood,
            int localX,
            int localY,
            out TerrainMaterialFaceProjection projection)
        {
            projection = default;
            if (!neighborhood.TryGetTile(localX, localY, out GeneratedTerrainCell current)
                || !IsDryVoid(current))
            {
                return false;
            }

            for (int faceDepth = 0; faceDepth < GroundFaceDepth; faceDepth++)
            {
                int sourceY = localY + faceDepth + 1;
                if (!neighborhood.TryGetTile(localX, sourceY, out GeneratedTerrainCell source))
                {
                    return false;
                }

                // Wallが見つかった場合はWall前面の責務です。Ground面を重ねず、用途別の境界完成Spriteを混在させません。
                if (source.WallBlockRuntimeId.IsValid)
                {
                    return false;
                }

                if (source.GroundBlockRuntimeId.IsValid)
                {
                    projection = new TerrainMaterialFaceProjection(
                        source,
                        localX,
                        sourceY,
                        faceDepth,
                        isWall: false);
                    return true;
                }

                // 別の液体や未対応地形を越えて遠方のGroundを投影すると穴の深さを偽るため、乾燥した空セルだけを連続させます。
                if (!IsDryVoid(source))
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>地形セルがBlockもLiquidも持たない乾燥穴か判定します。</summary>
        /// <param name="cell">完成済みまたは差分適用済みの地形セルです。</param>
        /// <returns>Ground、Wall、Liquidがすべて未設定ならtrueです。</returns>
        public static bool IsDryVoid(GeneratedTerrainCell cell)
        {
            return !cell.GroundBlockRuntimeId.IsValid
                && !cell.WallBlockRuntimeId.IsValid
                && !cell.LiquidRuntimeId.IsValid;
        }

        /// <summary>穴上端からの画素深度に対応するFace可視Alphaを1px単位で返します。</summary>
        /// <param name="depthFromTopPixels">穴上端を0とした非負の画素深度です。</param>
        /// <returns>0pxで1、48px以降で0となる線形Alphaです。</returns>
        public static float GetDryCliffFaceAlpha(int depthFromTopPixels)
        {
            if (depthFromTopPixels <= 0)
            {
                return 1f;
            }

            if (depthFromTopPixels >= DryCliffFadePixels)
            {
                return 0f;
            }

            return 1f - (depthFromTopPixels / (float)DryCliffFadePixels);
        }

        /// <summary>各16px段の上端をShaderへ渡すための可視Alphaを返します。</summary>
        /// <param name="faceDepth">Ground崖面の0〜2段目です。</param>
        /// <returns>段の上端に対応する1、2/3、1/3のAlphaです。</returns>
        public static float GetDryCliffStageStartAlpha(int faceDepth)
        {
            if (faceDepth < 0 || faceDepth >= GroundFaceDepth)
            {
                return 0f;
            }

            return GetDryCliffFaceAlpha(faceDepth * 16);
        }
    }
}
