// このファイルは、地形のサブチャンク表示で読む完成チャンクの範囲を計算します。
// 未完成地形の代用をせず、断面・反射・影・岸照明に必要な入力が揃った部分だけを表示するために使います。
using System;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>16×16サブチャンクの保守的な参照範囲と、表示可能なMaskを求める純粋計算です。</summary>
    public static class TerrainSubChunkDependencyUtility
    {
        /// <summary>岸区画の最大幅、隣接判定1セル、Wall投影2セルを含む最大参照半径です。影8・水中断面6も包含します。</summary>
        public const int ReadRadiusInTiles = TerrainMagmaShoreLighting.MaximumPlacementCellSize
            + TerrainMaterialVerticalProjectionUtility.WallTopOffsetInTiles + 1;

        /// <summary>指定サブチャンクの分類と影に必要な、ワールド内の3×3位置を9bitへ変換します。</summary>
        /// <param name="coordinate">中心のレイヤー付きチャンク座標です。</param>
        /// <param name="subChunkIndex">行優先0〜15です。</param>
        /// <returns>南西をbit0、中心をbit4、北東をbit8とする必要Maskです。</returns>
        public static ushort GetRequiredChunkMask(ChunkCoordinate coordinate, int subChunkIndex)
        {
            if (subChunkIndex < 0 || subChunkIndex >= WorldGridConstants.SubChunksPerChunk)
                throw new ArgumentOutOfRangeException(nameof(subChunkIndex));
            int startX = subChunkIndex % WorldGridConstants.SubChunksPerAxis * WorldGridConstants.SubChunkSize;
            int startY = subChunkIndex / WorldGridConstants.SubChunksPerAxis * WorldGridConstants.SubChunkSize;
            int minX = startX < ReadRadiusInTiles ? -1 : 0;
            int minY = startY < ReadRadiusInTiles ? -1 : 0;
            int maxX = startX + WorldGridConstants.SubChunkSize + ReadRadiusInTiles > WorldGridConstants.LogicalChunkSize ? 1 : 0;
            int maxY = startY + WorldGridConstants.SubChunkSize + ReadRadiusInTiles > WorldGridConstants.LogicalChunkSize ? 1 : 0;
            ushort mask = 0;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    // intタイル座標域の外には完成データを要求しません。負座標はfloatへ変換せずlongで加算します。
                    long worldX = (long)coordinate.X + x;
                    long worldY = (long)coordinate.Y + y;
                    if (worldX < WorldGridConstants.MinimumLogicalChunkCoordinate || worldX > WorldGridConstants.MaximumLogicalChunkCoordinate
                        || worldY < WorldGridConstants.MinimumLogicalChunkCoordinate || worldY > WorldGridConstants.MaximumLogicalChunkCoordinate) continue;
                    mask |= (ushort)(1 << ((y + 1) * 3 + x + 1));
                }
            return mask;
        }

        /// <summary>完成済み入力だけで描画できるサブチャンク集合を返します。未完成位置を空地として扱いません。</summary>
        /// <param name="coordinate">中心座標です。</param>
        /// <param name="availableChunkMask">完成Bufferの9bitです。</param>
        /// <returns>各bitが0〜15の表示可能サブチャンクに対応します。</returns>
        public static ushort GetReadySubChunkMask(ChunkCoordinate coordinate, ushort availableChunkMask)
        {
            ushort mask = 0;
            for (int index = 0; index < WorldGridConstants.SubChunksPerChunk; index++)
            {
                ushort required = GetRequiredChunkMask(coordinate, index);
                if ((availableChunkMask & required) == required) mask |= (ushort)(1 << index);
            }
            return mask;
        }
    }
}
