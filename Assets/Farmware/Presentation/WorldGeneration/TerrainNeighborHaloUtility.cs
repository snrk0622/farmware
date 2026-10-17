// このファイルは、Tilemap分類で参照するチャンク外1セルの地形を同期Burstでまとめて生成します。
// 内部4096セルはECS Bufferを正とし、外周だけをPresenter所有の260要素配列へ一時保存します。

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 地形境界・資源の前後関係に必要な外周を、同じ反映バッチ内で再利用する表示専用計算です。
    /// 配列は呼び出し元が所有し、次のフレームまたは別チャンクを処理する前にFillで上書きします。
    /// </summary>
    public static class TerrainNeighborHaloUtility
    {
        /// <summary>上下66セルずつと左右64セルずつを収める、再利用配列の必要要素数です。</summary>
        public const int TileCount = (WorldGridConstants.LogicalChunkSize * 4) + 4;

        /// <summary>
        /// 連続する反映対象サブチャンクを囲む矩形から、チャンク外1セルだけを生成します。
        /// Job.Runは呼び出し中に終了し、Worker待機やECSの構造変更は発生させません。
        /// </summary>
        /// <param name="settings">この反映バッチで使うワールド生成設定です。</param>
        /// <param name="coordinate">生成する外周の中心チャンク座標です。</param>
        /// <param name="firstSubChunk">先頭の行優先サブチャンク番号（0〜15）です。</param>
        /// <param name="subChunkCount">連続する対象数（1〜残りサブチャンク数）です。</param>
        /// <param name="halo">呼び出し元所有の260要素配列です。対象矩形外の値は更新せず、読んではいけません。</param>
        public static void Fill(in WorldGenerationSettings settings, ChunkCoordinate coordinate,
            int firstSubChunk, int subChunkCount, NativeArray<GeneratedTile> halo)
        {
            if (!halo.IsCreated || halo.Length != TileCount)
            {
                throw new ArgumentException("外周配列は260要素で確保してください。", nameof(halo));
            }

            if (firstSubChunk < 0 || firstSubChunk >= WorldGridConstants.SubChunksPerChunk
                || subChunkCount <= 0
                || subChunkCount > WorldGridConstants.SubChunksPerChunk - firstSubChunk)
            {
                throw new ArgumentOutOfRangeException(nameof(subChunkCount), "反映範囲は0〜15の内部にしてください。");
            }

            int lastSubChunk = firstSubChunk + subChunkCount - 1;
            int firstRow = firstSubChunk / WorldGridConstants.SubChunksPerAxis;
            int lastRow = lastSubChunk / WorldGridConstants.SubChunksPerAxis;
            // 複数行にまたがる場合は横幅全体を含めます。最大でも外周260セルに限定し、複雑な範囲リストを作りません。
            int firstColumn = firstRow == lastRow ? firstSubChunk % WorldGridConstants.SubChunksPerAxis : 0;
            int lastColumn = firstRow == lastRow ? lastSubChunk % WorldGridConstants.SubChunksPerAxis
                : WorldGridConstants.SubChunksPerAxis - 1;
            new FillHaloJob
            {
                Settings = settings,
                Coordinate = coordinate,
                MinimumX = firstColumn * WorldGridConstants.SubChunkSize - 1,
                MaximumX = (lastColumn + 1) * WorldGridConstants.SubChunkSize,
                MinimumY = firstRow * WorldGridConstants.SubChunkSize - 1,
                MaximumY = (lastRow + 1) * WorldGridConstants.SubChunkSize,
                Halo = halo
            }.Run();
        }

        /// <summary>内部セルはECS Buffer、外1セルは直前のFillで準備した配列から取得します。生成処理は行いません。</summary>
        /// <param name="localX">チャンク内の水平座標です。外周を含む-1〜64を指定します。</param>
        /// <param name="localY">チャンク内の垂直座標です。外周を含む-1〜64を指定します。</param>
        /// <param name="tiles">現在のECSチャンクの生成済みBufferです。外周の値より優先します。</param>
        /// <param name="halo">読み取る中心セルを含む反映範囲でFill済みの外周配列です。</param>
        /// <returns>生成済み地形、またはintワールド外を示す非接続の水域値です。</returns>
        public static GeneratedTile GetTile(int localX, int localY, DynamicBuffer<GeneratedTile> tiles,
            NativeArray<GeneratedTile> halo)
        {
            int size = WorldGridConstants.LogicalChunkSize;
            if (localX >= 0 && localX < size && localY >= 0 && localY < size)
            {
                return tiles[WorldGrid.ToTileIndex(localX, localY)];
            }

            if (localX < -1 || localX > size || localY < -1 || localY > size)
            {
                throw new ArgumentOutOfRangeException(nameof(localX), "表示分類の外周は-1〜64だけを参照できます。");
            }

            // 四隅は上下の行へ一度だけ格納し、左右の列には含めないことで重複生成を防ぎます。
            if (localY == -1) return halo[localX + 1];
            if (localY == size) return halo[size + 2 + localX + 1];
            int sideOffset = (size + 2) * 2;
            return halo[sideOffset + (localX == -1 ? 0 : size) + localY];
        }

        /// <summary>必要な外周地形だけをメインスレッドで一括生成する、呼び出し内で完結するBurst Jobです。</summary>
        [BurstCompile]
        private struct FillHaloJob : IJob
        {
            // 呼び出し時の生成設定の値コピーです。Jobは設定Entityを書き換えません。
            public WorldGenerationSettings Settings;
            // 外周の絶対座標を求める中心チャンクです。Run終了までだけ保持します。
            public ChunkCoordinate Coordinate;
            // 対象サブチャンク矩形に1セルの余白を加えた水平最小値です。
            public int MinimumX;
            // 対象サブチャンク矩形に1セルの余白を加えた水平最大値です。
            public int MaximumX;
            // 対象サブチャンク矩形に1セルの余白を加えた垂直最小値です。
            public int MinimumY;
            // 対象サブチャンク矩形に1セルの余白を加えた垂直最大値です。
            public int MaximumY;
            // Presenterから借りる出力配列です。Jobは破棄せず、同期実行中だけ書き込みます。
            [WriteOnly] public NativeArray<GeneratedTile> Halo;

            /// <summary>Runから呼ばれ、外周の各セルを最大1回生成します。対象外の配列要素は保持します。</summary>
            public void Execute()
            {
                int size = WorldGridConstants.LogicalChunkSize;
                int rowLength = size + 2;
                for (int index = 0; index < TileCount; index++)
                {
                    // 一次元の格納位置を上下の行、左右の列の順に逆変換します。
                    int x;
                    int y;
                    if (index < rowLength * 2)
                    {
                        x = index % rowLength - 1;
                        y = index < rowLength ? -1 : size;
                    }
                    else
                    {
                        int sideIndex = index - rowLength * 2;
                        x = sideIndex < size ? -1 : size;
                        y = sideIndex % size;
                    }

                    if (x < MinimumX || x > MaximumX || y < MinimumY || y > MaximumY) continue;
                    // int端の向こう側を反対端へ周回させないため、変換できないセルは土地・岩壁へ接続しない値にします。
                    Halo[index] = WorldGrid.TryToWorldTileCoordinate(Coordinate.X, Coordinate.Y, x, y,
                        out int worldX, out int worldY)
                        ? DeterministicChunkGenerator.GenerateTile(Settings, worldX, worldY)
                        : new GeneratedTile { Ground = GroundType.Water };
                }
            }
        }
    }
}
