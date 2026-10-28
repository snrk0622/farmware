// このファイルは、地形材質のGroundTop・Face・境界候補を絶対座標から決定論的に選びます。
// 表示の再描画順やチャンクの生成順に依存せず、同じワールドとセルでは常に同じSprite候補を返します。

using Farmware.Simulation;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 41材質の可変長Sprite配列から、個別Weightなしで候補を選ぶ純粋計算です。
    /// 後続のGameビューとMap Viewerが同じ選択結果を共有するために使用します。
    /// </summary>
    public static class TerrainMaterialSpriteVariantSelector
    {
        /// <summary>表示差分の選択規則を固定する版です。規則またはSalt変更時だけ増やします。</summary>
        public const int SelectionVersion = 1;

        /// <summary>GroundTop候補選択を他用途から分離する固定Saltです。</summary>
        private const ulong GroundTopSalt = 0xA9B4C51D7E23680FUL;

        /// <summary>Face候補選択を他用途から分離する固定Saltです。</summary>
        private const ulong FaceSalt = 0xD47A31E9B8056C2FUL;

        /// <summary>境界候補選択を他用途から分離する固定Saltです。</summary>
        private const ulong FaceBoundarySalt = 0x71E26AD493BC850FUL;

        /// <summary>
        /// GroundTop候補をワールド、レイヤー、絶対座標、材質から選びます。
        /// </summary>
        /// <param name="worldSeed">保存済みの64bitワールドシードです。</param>
        /// <param name="coordinate">WorldLayerを含む絶対タイル座標です。</param>
        /// <param name="stableMaterialId">表示する1〜41の安定材質IDです。</param>
        /// <param name="variantCount">1以上のGroundTop候補数です。</param>
        /// <param name="variantIndex">成功時に0以上variantCount未満の添字を受け取ります。</param>
        /// <returns>全入力が有効ならtrueです。</returns>
        public static bool TrySelectGroundTopIndex(
            ulong worldSeed,
            WorldTileCoordinate coordinate,
            TerrainBlockMaterialId stableMaterialId,
            int variantCount,
            out int variantIndex)
        {
            return TrySelectIndex(
                worldSeed,
                coordinate,
                stableMaterialId,
                faceDepth: 0,
                variantCount,
                GroundTopSalt,
                out variantIndex);
        }

        /// <summary>
        /// Face候補をワールド、レイヤー、絶対座標、材質、表示断面段から選びます。
        /// </summary>
        /// <param name="worldSeed">保存済みの64bitワールドシードです。</param>
        /// <param name="coordinate">断面の元セルを示す絶対タイル座標です。</param>
        /// <param name="stableMaterialId">表示する1〜41の安定材質IDです。</param>
        /// <param name="faceDepth">上端を0とする非負の表示断面段です。</param>
        /// <param name="variantCount">1以上のFace候補数です。</param>
        /// <param name="variantIndex">成功時に0以上variantCount未満の添字を受け取ります。</param>
        /// <returns>全入力が有効ならtrueです。</returns>
        public static bool TrySelectFaceIndex(
            ulong worldSeed,
            WorldTileCoordinate coordinate,
            TerrainBlockMaterialId stableMaterialId,
            int faceDepth,
            int variantCount,
            out int variantIndex)
        {
            return TrySelectIndex(
                worldSeed,
                coordinate,
                stableMaterialId,
                faceDepth,
                variantCount,
                FaceSalt,
                out variantIndex);
        }

        /// <summary>
        /// 境界Overlay候補をワールド、レイヤー、絶対座標、材質、表示断面段から選びます。
        /// </summary>
        /// <param name="worldSeed">保存済みの64bitワールドシードです。</param>
        /// <param name="coordinate">境界を置く断面の元セルを示す絶対タイル座標です。</param>
        /// <param name="stableMaterialId">表示する1〜41の安定材質IDです。</param>
        /// <param name="faceDepth">上端を0とする非負の表示断面段です。</param>
        /// <param name="variantCount">1以上の境界候補数です。</param>
        /// <param name="variantIndex">成功時に0以上variantCount未満の添字を受け取ります。</param>
        /// <returns>全入力が有効ならtrueです。</returns>
        public static bool TrySelectBoundaryIndex(
            ulong worldSeed,
            WorldTileCoordinate coordinate,
            TerrainBlockMaterialId stableMaterialId,
            int faceDepth,
            int variantCount,
            out int variantIndex)
        {
            return TrySelectIndex(
                worldSeed,
                coordinate,
                stableMaterialId,
                faceDepth,
                variantCount,
                FaceBoundarySalt,
                out variantIndex);
        }

        /// <summary>
        /// Face優先・境界後続で生成した事前合成配列の添字を計算します。
        /// </summary>
        /// <param name="faceVariantIndex">0以上のFace候補添字です。</param>
        /// <param name="boundaryVariantIndex">0以上boundaryVariantCount未満の境界候補添字です。</param>
        /// <param name="boundaryVariantCount">1以上の境界候補数です。</param>
        /// <returns>Face×境界の行優先配列へ使用する0以上の添字です。</returns>
        public static int CalculateComposedBoundaryIndex(
            int faceVariantIndex,
            int boundaryVariantIndex,
            int boundaryVariantCount)
        {
            return checked((faceVariantIndex * boundaryVariantCount) + boundaryVariantIndex);
        }

        /// <summary>
        /// 用途別Saltと全安定入力を混合し、可変長候補の添字へ変換します。
        /// </summary>
        /// <param name="worldSeed">保存済みワールドシードです。</param>
        /// <param name="coordinate">WorldLayerを含む絶対タイル座標です。</param>
        /// <param name="stableMaterialId">1〜41の安定材質IDです。</param>
        /// <param name="faceDepth">上端を0とする非負の表示断面段です。</param>
        /// <param name="variantCount">1以上の選択候補数です。</param>
        /// <param name="purposeSalt">GroundTop、Face、境界を分離する固定Saltです。</param>
        /// <param name="variantIndex">成功時に候補添字を受け取ります。</param>
        /// <returns>入力が有効で候補を選べた場合はtrueです。</returns>
        private static bool TrySelectIndex(
            ulong worldSeed,
            WorldTileCoordinate coordinate,
            TerrainBlockMaterialId stableMaterialId,
            int faceDepth,
            int variantCount,
            ulong purposeSalt,
            out int variantIndex)
        {
            variantIndex = -1;
            int stableIndex = (int)stableMaterialId
                - (int)TerrainBlockMaterialDefinitions.First;
            if (!WorldLayerUtility.IsPlayable(coordinate.Layer)
                || stableIndex < 0
                || stableIndex >= TerrainBlockMaterialDefinitions.Count
                || faceDepth < 0
                || variantCount <= 0)
            {
                return false;
            }

            ulong hash = WorldSeedUtility.HashCoordinate(
                worldSeed,
                SelectionVersion,
                coordinate.X,
                coordinate.Y,
                purposeSalt,
                (uint)coordinate.Layer);

            // 材質と断面段も混合し、同じ座標でも別材質・別段・別用途の候補列を独立させます。
            hash = WorldSeedUtility.Mix64(
                hash
                ^ ((ulong)(byte)stableMaterialId << 32)
                ^ (uint)faceDepth);
            variantIndex = (int)(hash % (uint)variantCount);
            return true;
        }
    }
}
