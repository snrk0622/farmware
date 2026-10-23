// このファイルは、Tilemap分類で参照する中心チャンクと周囲8チャンクの完成地形をまとめて扱います。
// 内部・外周ともECSの完成Bufferを正とし、外周1セルだけをPresenter所有の260要素配列へ一時保存します。

using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 中心チャンクと周囲8チャンクの完成済み地形Bufferを、中心基準のローカル座標で読み取る借用値です。
    /// Presenterと開発者用可視化が、水文・Biome確定前の単一タイル生成へ戻らず同じ完成地形を参照するために使います。
    /// </summary>
    public readonly struct CompletedChunkTerrainNeighborhood
    {
        // 左下チャンクの完成地形Bufferを同期処理中だけ借用します。この値は配列を所有・破棄しません。
        private readonly NativeArray<GeneratedTile> southWest;

        // 下チャンクの完成地形Bufferを同期処理中だけ借用します。この値は配列を所有・破棄しません。
        private readonly NativeArray<GeneratedTile> south;

        // 右下チャンクの完成地形Bufferを同期処理中だけ借用します。この値は配列を所有・破棄しません。
        private readonly NativeArray<GeneratedTile> southEast;

        // 左チャンクの完成地形Bufferを同期処理中だけ借用します。この値は配列を所有・破棄しません。
        private readonly NativeArray<GeneratedTile> west;

        // 中心チャンクの完成地形Bufferを同期処理中だけ借用します。この値は配列を所有・破棄しません。
        private readonly NativeArray<GeneratedTile> center;

        // 右チャンクの完成地形Bufferを同期処理中だけ借用します。この値は配列を所有・破棄しません。
        private readonly NativeArray<GeneratedTile> east;

        // 左上チャンクの完成地形Bufferを同期処理中だけ借用します。この値は配列を所有・破棄しません。
        private readonly NativeArray<GeneratedTile> northWest;

        // 上チャンクの完成地形Bufferを同期処理中だけ借用します。この値は配列を所有・破棄しません。
        private readonly NativeArray<GeneratedTile> north;

        // 右上チャンクの完成地形Bufferを同期処理中だけ借用します。この値は配列を所有・破棄しません。
        private readonly NativeArray<GeneratedTile> northEast;

        // 3×3の各位置に実在する完成チャンクがあるかを9bitで保持し、ワールド外を代替配列と区別します。
        private readonly ushort availabilityMask;

        /// <summary>同期Jobへ渡せる有効な中心4096セルを保持する場合はtrueです。</summary>
        public bool HasValidCenter => IsChunkAvailable(0, 0)
            && center.IsCreated
            && center.Length == WorldGridConstants.TilesPerChunk;

        /// <summary>
        /// 3×3チャンクの完成地形Bufferを借用して近傍を作ります。
        /// ワールド端の外側に当たる配列だけはdefaultを渡せます。Job安全性のため内部では中心配列を代入し、
        /// availabilityMaskにより実在する隣接チャンクと区別します。中心と実在チャンクは必ず4096要素を渡します。
        /// </summary>
        /// <param name="southWest">左下チャンクの完成地形、またはワールド外ならdefaultです。</param>
        /// <param name="south">下チャンクの完成地形、またはワールド外ならdefaultです。</param>
        /// <param name="southEast">右下チャンクの完成地形、またはワールド外ならdefaultです。</param>
        /// <param name="west">左チャンクの完成地形、またはワールド外ならdefaultです。</param>
        /// <param name="center">中心チャンクの完成地形です。</param>
        /// <param name="east">右チャンクの完成地形、またはワールド外ならdefaultです。</param>
        /// <param name="northWest">左上チャンクの完成地形、またはワールド外ならdefaultです。</param>
        /// <param name="north">上チャンクの完成地形、またはワールド外ならdefaultです。</param>
        /// <param name="northEast">右上チャンクの完成地形、またはワールド外ならdefaultです。</param>
        public CompletedChunkTerrainNeighborhood(
            NativeArray<GeneratedTile> southWest,
            NativeArray<GeneratedTile> south,
            NativeArray<GeneratedTile> southEast,
            NativeArray<GeneratedTile> west,
            NativeArray<GeneratedTile> center,
            NativeArray<GeneratedTile> east,
            NativeArray<GeneratedTile> northWest,
            NativeArray<GeneratedTile> north,
            NativeArray<GeneratedTile> northEast)
        {
            availabilityMask = BuildAvailabilityMask(
                southWest, south, southEast,
                west, center, east,
                northWest, north, northEast);
            // Unity Jobは未使用でもdefault NativeArrayをJobデータ内に許さないため、ワールド外だけ中心配列で代用します。
            // TryGetTileはavailabilityMaskを先に確認するので、代用品を実在する地形として返すことはありません。
            NativeArray<GeneratedTile> fallback = IsUsable(center) ? center : default;
            this.southWest = IsUsable(southWest) ? southWest : fallback;
            this.south = IsUsable(south) ? south : fallback;
            this.southEast = IsUsable(southEast) ? southEast : fallback;
            this.west = IsUsable(west) ? west : fallback;
            this.center = center;
            this.east = IsUsable(east) ? east : fallback;
            this.northWest = IsUsable(northWest) ? northWest : fallback;
            this.north = IsUsable(north) ? north : fallback;
            this.northEast = IsUsable(northEast) ? northEast : fallback;
        }

        /// <summary>
        /// 中心チャンク基準のローカル座標から、3×3内の完成地形を取得します。
        /// </summary>
        /// <param name="localX">中心チャンク左下を0とする水平座標です。-64〜127を扱えます。</param>
        /// <param name="localY">中心チャンク左下を0とする垂直座標です。-64〜127を扱えます。</param>
        /// <param name="tile">成功時に該当する完成済み地形を返します。</param>
        /// <returns>対応チャンクが存在して4096要素の完成Bufferを持つ場合はtrueです。ワールド外または範囲外ではfalseです。</returns>
        public bool TryGetTile(int localX, int localY, out GeneratedTile tile)
        {
            tile = default;
            int size = WorldGridConstants.LogicalChunkSize;
            int chunkOffsetX = localX < 0 ? -1 : localX >= size ? 1 : 0;
            int chunkOffsetY = localY < 0 ? -1 : localY >= size ? 1 : 0;
            int sourceX = localX - (chunkOffsetX * size);
            int sourceY = localY - (chunkOffsetY * size);
            if (sourceX < 0 || sourceX >= size || sourceY < 0 || sourceY >= size)
            {
                // 3×3を越える入力を端チャンクへ丸めると別座標を読むため、明示的に拒否します。
                return false;
            }

            if (!IsChunkAvailable(chunkOffsetX, chunkOffsetY))
            {
                // Job安全性用の代替配列をワールド外の地形として読み取らないよう、配列選択より先に拒否します。
                return false;
            }

            NativeArray<GeneratedTile> source = SelectChunk(chunkOffsetX, chunkOffsetY);
            if (!source.IsCreated || source.Length != WorldGridConstants.TilesPerChunk)
            {
                // default配列はintワールド端の外側を表し、土地や岩壁へ接続しない値として呼び出し側が扱います。
                return false;
            }

            tile = source[WorldGrid.ToTileIndex(sourceX, sourceY)];
            return true;
        }

        /// <summary>3×3内の相対チャンク位置に対応する借用Bufferを返します。</summary>
        /// <param name="offsetX">中心からの水平チャンク差（-1〜1）です。</param>
        /// <param name="offsetY">中心からの垂直チャンク差（-1〜1）です。</param>
        /// <returns>対応する完成Buffer、またはワールド外を表すdefault配列です。</returns>
        private NativeArray<GeneratedTile> SelectChunk(int offsetX, int offsetY)
        {
            if (offsetY < 0)
            {
                return offsetX < 0 ? southWest : offsetX > 0 ? southEast : south;
            }

            if (offsetY > 0)
            {
                return offsetX < 0 ? northWest : offsetX > 0 ? northEast : north;
            }

            return offsetX < 0 ? west : offsetX > 0 ? east : center;
        }

        /// <summary>9配列のうち4096要素を持つ位置を、左下から行優先の9bitへ変換します。</summary>
        /// <param name="southWest">左下チャンクです。</param>
        /// <param name="south">下チャンクです。</param>
        /// <param name="southEast">右下チャンクです。</param>
        /// <param name="west">左チャンクです。</param>
        /// <param name="center">中心チャンクです。</param>
        /// <param name="east">右チャンクです。</param>
        /// <param name="northWest">左上チャンクです。</param>
        /// <param name="north">上チャンクです。</param>
        /// <param name="northEast">右上チャンクです。</param>
        /// <returns>実在する完成チャンク位置だけが1の9bit値です。</returns>
        private static ushort BuildAvailabilityMask(
            NativeArray<GeneratedTile> southWest,
            NativeArray<GeneratedTile> south,
            NativeArray<GeneratedTile> southEast,
            NativeArray<GeneratedTile> west,
            NativeArray<GeneratedTile> center,
            NativeArray<GeneratedTile> east,
            NativeArray<GeneratedTile> northWest,
            NativeArray<GeneratedTile> north,
            NativeArray<GeneratedTile> northEast)
        {
            ushort mask = 0;
            if (IsUsable(southWest)) mask |= 1 << 0;
            if (IsUsable(south)) mask |= 1 << 1;
            if (IsUsable(southEast)) mask |= 1 << 2;
            if (IsUsable(west)) mask |= 1 << 3;
            if (IsUsable(center)) mask |= 1 << 4;
            if (IsUsable(east)) mask |= 1 << 5;
            if (IsUsable(northWest)) mask |= 1 << 6;
            if (IsUsable(north)) mask |= 1 << 7;
            if (IsUsable(northEast)) mask |= 1 << 8;
            return mask;
        }

        /// <summary>借用配列が完成地形1チャンク分として使用可能かを返します。</summary>
        /// <param name="tiles">検証する借用配列です。</param>
        /// <returns>作成済みで4096要素ならtrueです。</returns>
        private static bool IsUsable(NativeArray<GeneratedTile> tiles)
        {
            return tiles.IsCreated && tiles.Length == WorldGridConstants.TilesPerChunk;
        }

        /// <summary>中心から-1〜1の相対位置が実在する完成チャンクかを返します。</summary>
        /// <param name="offsetX">中心からの水平チャンク差です。</param>
        /// <param name="offsetY">中心からの垂直チャンク差です。</param>
        /// <returns>該当位置が完成Bufferとして渡された場合はtrueです。</returns>
        private bool IsChunkAvailable(int offsetX, int offsetY)
        {
            int bitIndex = ((offsetY + 1) * 3) + offsetX + 1;
            return bitIndex >= 0
                && bitIndex < 9
                && (availabilityMask & (1 << bitIndex)) != 0;
        }
    }

    /// <summary>
    /// ECSの完成チャンク索引から、同期表示処理へ安全に貸し出せる3×3地形近傍を構築します。
    /// </summary>
    public static class CompletedChunkTerrainNeighborhoodFactory
    {
        /// <summary>
        /// 中心とワールド内の周囲8チャンクが完成Bufferを持つ場合だけ、借用近傍を返します。
        /// </summary>
        /// <param name="entityManager">Bufferの存在と長さを検証して借用する現在WorldのEntityManagerです。</param>
        /// <param name="completedEntities">完成状態だけを座標で引ける、呼び出し中の一時索引です。</param>
        /// <param name="centerCoordinate">近傍の中心となる論理チャンク座標です。</param>
        /// <param name="neighborhood">成功時に3×3の借用Bufferを返します。呼び出し中だけ使用してください。</param>
        /// <returns>必要な完成Bufferをすべて借用できた場合はtrueです。ワールド外の近傍は不足扱いにしません。</returns>
        public static bool TryCreate(
            EntityManager entityManager,
            IReadOnlyDictionary<ChunkCoordinate, Entity> completedEntities,
            ChunkCoordinate centerCoordinate,
            out CompletedChunkTerrainNeighborhood neighborhood)
        {
            neighborhood = default;
            if (!TryGetChunk(entityManager, completedEntities, centerCoordinate, -1, -1, out NativeArray<GeneratedTile> southWest)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, 0, -1, out NativeArray<GeneratedTile> south)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, 1, -1, out NativeArray<GeneratedTile> southEast)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, -1, 0, out NativeArray<GeneratedTile> west)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, 0, 0, out NativeArray<GeneratedTile> center)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, 1, 0, out NativeArray<GeneratedTile> east)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, -1, 1, out NativeArray<GeneratedTile> northWest)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, 0, 1, out NativeArray<GeneratedTile> north)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, 1, 1, out NativeArray<GeneratedTile> northEast))
            {
                return false;
            }

            neighborhood = new CompletedChunkTerrainNeighborhood(
                southWest, south, southEast,
                west, center, east,
                northWest, north, northEast);
            return true;
        }

        /// <summary>1つの相対チャンクについて、ワールド外なら空借用、ワールド内なら完成Bufferを返します。</summary>
        /// <param name="entityManager">対象Entityの有効性とBufferを確認する管理境界です。</param>
        /// <param name="completedEntities">完成済み座標だけを含む索引です。</param>
        /// <param name="center">中心チャンクです。</param>
        /// <param name="offsetX">中心からの水平差です。</param>
        /// <param name="offsetY">中心からの垂直差です。</param>
        /// <param name="tiles">成功時に借用Buffer、ワールド外ならdefaultを返します。</param>
        /// <returns>ワールド内の必要Bufferを借用できたか、座標自体がワールド外ならtrueです。</returns>
        private static bool TryGetChunk(
            EntityManager entityManager,
            IReadOnlyDictionary<ChunkCoordinate, Entity> completedEntities,
            ChunkCoordinate center,
            int offsetX,
            int offsetY,
            out NativeArray<GeneratedTile> tiles)
        {
            tiles = default;
            long chunkX = (long)center.X + offsetX;
            long chunkY = (long)center.Y + offsetY;
            if (chunkX < WorldGridConstants.MinimumLogicalChunkCoordinate
                || chunkX > WorldGridConstants.MaximumLogicalChunkCoordinate
                || chunkY < WorldGridConstants.MinimumLogicalChunkCoordinate
                || chunkY > WorldGridConstants.MaximumLogicalChunkCoordinate)
            {
                // intタイル座標へ変換できない外側にはEntityを要求せず、非接続領域として扱います。
                return true;
            }

            var coordinate = new ChunkCoordinate(center.Layer, (int)chunkX, (int)chunkY);
            if (!completedEntities.TryGetValue(coordinate, out Entity entity)
                || !entityManager.Exists(entity)
                || !entityManager.HasBuffer<GeneratedTile>(entity))
            {
                return false;
            }

            DynamicBuffer<GeneratedTile> buffer = entityManager.GetBuffer<GeneratedTile>(entity, true);
            if (buffer.Length != WorldGridConstants.TilesPerChunk)
            {
                return false;
            }

            // AsNativeArrayはコピーせず、同期処理の終了までだけECS Bufferを読み取り専用で貸し出します。
            tiles = buffer.AsNativeArray();
            return true;
        }
    }

    /// <summary>
    /// 地形境界・資源の前後関係に必要な外周を、同じ反映バッチ内で再利用する表示専用計算です。
    /// 配列は呼び出し元が所有し、次のフレームまたは別チャンクを処理する前にFillで上書きします。
    /// </summary>
    public static class TerrainNeighborHaloUtility
    {
        /// <summary>上下66セルずつと左右64セルずつを収める、再利用配列の必要要素数です。</summary>
        public const int TileCount = (WorldGridConstants.LogicalChunkSize * 4) + 4;

        /// <summary>
        /// 連続する反映対象サブチャンクを囲む矩形から、チャンク外1セルだけを完成Bufferから複写します。
        /// Job.Runは呼び出し中に終了し、Worker待機やECSの構造変更は発生させません。
        /// </summary>
        /// <param name="neighborhood">中心と周囲8チャンクの完成済み地形Bufferを借用した近傍です。</param>
        /// <param name="firstSubChunk">先頭の行優先サブチャンク番号（0〜15）です。</param>
        /// <param name="subChunkCount">連続する対象数（1〜残りサブチャンク数）です。</param>
        /// <param name="halo">呼び出し元所有の260要素配列です。対象矩形外の値は更新せず、読んではいけません。</param>
        /// <exception cref="ArgumentException">中心完成チャンクがないか、外周配列が260要素でない場合です。</exception>
        /// <exception cref="ArgumentOutOfRangeException">サブチャンク範囲が0〜15を越える場合です。</exception>
        public static void Fill(in CompletedChunkTerrainNeighborhood neighborhood,
            int firstSubChunk, int subChunkCount, NativeArray<GeneratedTile> halo)
        {
            if (!halo.IsCreated || halo.Length != TileCount)
            {
                throw new ArgumentException("外周配列は260要素で確保してください。", nameof(halo));
            }

            if (!neighborhood.HasValidCenter)
            {
                throw new ArgumentException("外周複写には4096要素の完成中心チャンクが必要です。", nameof(neighborhood));
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
                Neighborhood = neighborhood,
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

        /// <summary>必要な外周地形だけを完成Bufferから一括複写する、呼び出し内で完結するBurst Jobです。</summary>
        [BurstCompile]
        private struct FillHaloJob : IJob
        {
            // 中心と周囲8チャンクの完成Bufferを同期実行中だけ借用し、Jobは書き込みも破棄もしません。
            [ReadOnly] public CompletedChunkTerrainNeighborhood Neighborhood;
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

            /// <summary>Runから呼ばれ、外周の各セルを最大1回複写します。対象外の配列要素は保持します。</summary>
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
                    // int端のワールド外だけは完成Bufferが存在しないため、土地・岩壁へ接続しない水域値にします。
                    Halo[index] = Neighborhood.TryGetTile(x, y, out GeneratedTile tile)
                        ? tile
                        : new GeneratedTile { Ground = GroundType.Water };
                }
            }
        }
    }
}
