// このファイルは、Tilemap分類で参照する中心チャンクと周囲8チャンクの完成地形・永続差分をまとめて扱います。
// 内部・外周ともECSの自然地形Bufferへ差分を適用した有効地形を正とし、必要な同期処理へ貸し出します。

using System;
using System.Collections.Generic;
using Farmware.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 中心チャンクと周囲8チャンクの完成済み地形Bufferを、中心基準のローカル座標で読み取る借用値です。
    /// Presenterと開発者用可視化が、水文・Biome確定前の単一タイル生成へ戻らず同じ完成地形を参照するために使います。
    /// </summary>
    public readonly struct CompletedChunkTerrainNeighborhood
    {
        // 左下チャンクの完成地形Bufferを同期処理中だけ借用します。この値は配列を所有・破棄しません。
        private readonly NativeArray<GeneratedTerrainCell> southWest;

        // 下チャンクの完成地形Bufferを同期処理中だけ借用します。この値は配列を所有・破棄しません。
        private readonly NativeArray<GeneratedTerrainCell> south;

        // 右下チャンクの完成地形Bufferを同期処理中だけ借用します。この値は配列を所有・破棄しません。
        private readonly NativeArray<GeneratedTerrainCell> southEast;

        // 左チャンクの完成地形Bufferを同期処理中だけ借用します。この値は配列を所有・破棄しません。
        private readonly NativeArray<GeneratedTerrainCell> west;

        // 中心チャンクの完成地形Bufferを同期処理中だけ借用します。この値は配列を所有・破棄しません。
        private readonly NativeArray<GeneratedTerrainCell> center;

        // 右チャンクの完成地形Bufferを同期処理中だけ借用します。この値は配列を所有・破棄しません。
        private readonly NativeArray<GeneratedTerrainCell> east;

        // 左上チャンクの完成地形Bufferを同期処理中だけ借用します。この値は配列を所有・破棄しません。
        private readonly NativeArray<GeneratedTerrainCell> northWest;

        // 上チャンクの完成地形Bufferを同期処理中だけ借用します。この値は配列を所有・破棄しません。
        private readonly NativeArray<GeneratedTerrainCell> north;

        // 右上チャンクの完成地形Bufferを同期処理中だけ借用します。この値は配列を所有・破棄しません。
        private readonly NativeArray<GeneratedTerrainCell> northEast;

        // 左下から右上までの各チャンクに対応する疎な地形差分です。所有者がない位置はdefaultで表します。
        private readonly NativeArray<TerrainCellDelta> southWestDeltas;
        private readonly NativeArray<TerrainCellDelta> southDeltas;
        private readonly NativeArray<TerrainCellDelta> southEastDeltas;
        private readonly NativeArray<TerrainCellDelta> westDeltas;
        private readonly NativeArray<TerrainCellDelta> centerDeltas;
        private readonly NativeArray<TerrainCellDelta> eastDeltas;
        private readonly NativeArray<TerrainCellDelta> northWestDeltas;
        private readonly NativeArray<TerrainCellDelta> northDeltas;
        private readonly NativeArray<TerrainCellDelta> northEastDeltas;

        // 3×3の各位置に差分所有者があるかを9bitで保持し、差分なしと未作成配列を区別します。
        private readonly ushort deltaAvailabilityMask;

        // 自然生成セルへ差分を適用するとき、Runtime IDの区画種別を検証する読み取り専用カタログです。
        private readonly BlobAssetReference<TerrainBlockCatalogBlob> terrainCatalog;

        // 3×3の各位置に実在する完成チャンクがあるかを9bitで保持し、ワールド外を代替配列と区別します。
        private readonly ushort availabilityMask;

        /// <summary>同期表示処理で読める有効な中心4096セルを保持する場合はtrueです。</summary>
        public bool HasValidCenter => IsChunkAvailable(0, 0)
            && center.IsCreated
            && center.Length == WorldGridConstants.TilesPerChunk;

        /// <summary>
        /// 3×3チャンクの完成地形Bufferを借用して近傍を作ります。
        /// ワールド端の外側に当たる配列だけはdefaultを渡せます。availabilityMaskにより実在する隣接チャンクと区別し、
        /// 中心と実在チャンクは必ず4096要素を渡します。
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
            NativeArray<GeneratedTerrainCell> southWest,
            NativeArray<GeneratedTerrainCell> south,
            NativeArray<GeneratedTerrainCell> southEast,
            NativeArray<GeneratedTerrainCell> west,
            NativeArray<GeneratedTerrainCell> center,
            NativeArray<GeneratedTerrainCell> east,
            NativeArray<GeneratedTerrainCell> northWest,
            NativeArray<GeneratedTerrainCell> north,
            NativeArray<GeneratedTerrainCell> northEast)
        {
            availabilityMask = BuildAvailabilityMask(
                southWest, south, southEast,
                west, center, east,
                northWest, north, northEast);
            this.southWest = southWest;
            this.south = south;
            this.southEast = southEast;
            this.west = west;
            this.center = center;
            this.east = east;
            this.northWest = northWest;
            this.north = north;
            this.northEast = northEast;
            southWestDeltas = default;
            southDeltas = default;
            southEastDeltas = default;
            westDeltas = default;
            centerDeltas = default;
            eastDeltas = default;
            northWestDeltas = default;
            northDeltas = default;
            northEastDeltas = default;
            deltaAvailabilityMask = 0;
            terrainCatalog = default;
        }

        /// <summary>
        /// 3×3の完成自然地形と任意の疎な変更差分を借用し、表示・診断が同じ有効地形を読める近傍を作ります。
        /// 配列は呼び出し元が所有し、この値は同期処理中だけ参照します。
        /// </summary>
        /// <param name="southWest">左下チャンクの自然地形です。</param>
        /// <param name="south">下チャンクの自然地形です。</param>
        /// <param name="southEast">右下チャンクの自然地形です。</param>
        /// <param name="west">左チャンクの自然地形です。</param>
        /// <param name="center">中心チャンクの自然地形です。</param>
        /// <param name="east">右チャンクの自然地形です。</param>
        /// <param name="northWest">左上チャンクの自然地形です。</param>
        /// <param name="north">上チャンクの自然地形です。</param>
        /// <param name="northEast">右上チャンクの自然地形です。</param>
        /// <param name="southWestDeltas">左下チャンクの疎な差分です。</param>
        /// <param name="southDeltas">下チャンクの疎な差分です。</param>
        /// <param name="southEastDeltas">右下チャンクの疎な差分です。</param>
        /// <param name="westDeltas">左チャンクの疎な差分です。</param>
        /// <param name="centerDeltas">中心チャンクの疎な差分です。</param>
        /// <param name="eastDeltas">右チャンクの疎な差分です。</param>
        /// <param name="northWestDeltas">左上チャンクの疎な差分です。</param>
        /// <param name="northDeltas">上チャンクの疎な差分です。</param>
        /// <param name="northEastDeltas">右上チャンクの疎な差分です。</param>
        /// <param name="deltaAvailabilityMask">差分所有者が存在する位置を表す9bitです。</param>
        /// <param name="terrainCatalog">差分適用時のRuntime ID分類を検証するカタログです。</param>
        public CompletedChunkTerrainNeighborhood(
            NativeArray<GeneratedTerrainCell> southWest,
            NativeArray<GeneratedTerrainCell> south,
            NativeArray<GeneratedTerrainCell> southEast,
            NativeArray<GeneratedTerrainCell> west,
            NativeArray<GeneratedTerrainCell> center,
            NativeArray<GeneratedTerrainCell> east,
            NativeArray<GeneratedTerrainCell> northWest,
            NativeArray<GeneratedTerrainCell> north,
            NativeArray<GeneratedTerrainCell> northEast,
            NativeArray<TerrainCellDelta> southWestDeltas,
            NativeArray<TerrainCellDelta> southDeltas,
            NativeArray<TerrainCellDelta> southEastDeltas,
            NativeArray<TerrainCellDelta> westDeltas,
            NativeArray<TerrainCellDelta> centerDeltas,
            NativeArray<TerrainCellDelta> eastDeltas,
            NativeArray<TerrainCellDelta> northWestDeltas,
            NativeArray<TerrainCellDelta> northDeltas,
            NativeArray<TerrainCellDelta> northEastDeltas,
            ushort deltaAvailabilityMask,
            BlobAssetReference<TerrainBlockCatalogBlob> terrainCatalog)
            : this(
                southWest, south, southEast,
                west, center, east,
                northWest, north, northEast)
        {
            this.southWestDeltas = southWestDeltas;
            this.southDeltas = southDeltas;
            this.southEastDeltas = southEastDeltas;
            this.westDeltas = westDeltas;
            this.centerDeltas = centerDeltas;
            this.eastDeltas = eastDeltas;
            this.northWestDeltas = northWestDeltas;
            this.northDeltas = northDeltas;
            this.northEastDeltas = northEastDeltas;
            this.deltaAvailabilityMask = deltaAvailabilityMask;
            this.terrainCatalog = terrainCatalog;
        }

        /// <summary>
        /// 中心チャンク基準のローカル座標から、3×3内の完成地形を取得します。
        /// </summary>
        /// <param name="localX">中心チャンク左下を0とする水平座標です。-64〜127を扱えます。</param>
        /// <param name="localY">中心チャンク左下を0とする垂直座標です。-64〜127を扱えます。</param>
        /// <param name="tile">成功時に該当する完成済み地形を返します。</param>
        /// <returns>対応チャンクが存在して4096要素の完成Bufferを持つ場合はtrueです。ワールド外または範囲外ではfalseです。</returns>
        public bool TryGetTile(int localX, int localY, out GeneratedTerrainCell tile)
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
                // default配列をワールド外の地形として読み取らないよう、配列選択より先に拒否します。
                return false;
            }

            NativeArray<GeneratedTerrainCell> source = SelectChunk(chunkOffsetX, chunkOffsetY);
            if (!source.IsCreated || source.Length != WorldGridConstants.TilesPerChunk)
            {
                // default配列はintワールド端の外側を表し、土地や岩壁へ接続しない値として呼び出し側が扱います。
                return false;
            }

            int tileIndex = WorldGrid.ToTileIndex(sourceX, sourceY);
            tile = source[tileIndex];
            if (!terrainCatalog.IsCreated || !IsDeltaChunkAvailable(chunkOffsetX, chunkOffsetY))
            {
                return true;
            }

            NativeArray<TerrainCellDelta> deltas = SelectDeltaChunk(chunkOffsetX, chunkOffsetY);
            bool hasDelta = TryFindDelta(deltas, (ushort)tileIndex, out TerrainCellDelta delta);
            if (EffectiveTerrainResolver.TryResolve(
                    tile,
                    hasDelta,
                    delta,
                    terrainCatalog,
                    out EffectiveTerrainCell effectiveCell)
                != EffectiveTerrainResolveResult.Success)
            {
                return false;
            }

            tile = ToGeneratedTerrainCell(effectiveCell);
            return true;
        }

        /// <summary>3×3内の相対位置へ対応する疎な差分Bufferを返します。</summary>
        private NativeArray<TerrainCellDelta> SelectDeltaChunk(int offsetX, int offsetY)
        {
            if (offsetY < 0)
            {
                return offsetX < 0 ? southWestDeltas : offsetX > 0 ? southEastDeltas : southDeltas;
            }

            if (offsetY > 0)
            {
                return offsetX < 0 ? northWestDeltas : offsetX > 0 ? northEastDeltas : northDeltas;
            }

            return offsetX < 0 ? westDeltas : offsetX > 0 ? eastDeltas : centerDeltas;
        }

        /// <summary>LocalTileIndex昇順の疎な差分を二分探索し、描画順に依存せず対象セルだけを取得します。</summary>
        private static bool TryFindDelta(
            NativeArray<TerrainCellDelta> deltas,
            ushort localTileIndex,
            out TerrainCellDelta delta)
        {
            int minimum = 0;
            int maximum = deltas.Length - 1;
            while (minimum <= maximum)
            {
                int middle = minimum + ((maximum - minimum) / 2);
                TerrainCellDelta candidate = deltas[middle];
                if (candidate.LocalTileIndex == localTileIndex)
                {
                    delta = candidate;
                    return true;
                }

                if (candidate.LocalTileIndex < localTileIndex)
                {
                    minimum = middle + 1;
                }
                else
                {
                    maximum = middle - 1;
                }
            }

            delta = default;
            return false;
        }

        /// <summary>有効地形を既存Presentation APIが共有する値型へコピーします。</summary>
        private static GeneratedTerrainCell ToGeneratedTerrainCell(in EffectiveTerrainCell cell)
        {
            return new GeneratedTerrainCell
            {
                Biome = cell.Biome,
                GroundBlockRuntimeId = cell.GroundBlockRuntimeId,
                WallBlockRuntimeId = cell.WallBlockRuntimeId,
                LiquidRuntimeId = cell.LiquidRuntimeId,
                TerrainSurfaceRuntimeId = cell.TerrainSurfaceRuntimeId
            };
        }

        /// <summary>3×3内の相対チャンク位置に対応する借用Bufferを返します。</summary>
        /// <param name="offsetX">中心からの水平チャンク差（-1〜1）です。</param>
        /// <param name="offsetY">中心からの垂直チャンク差（-1〜1）です。</param>
        /// <returns>対応する完成Buffer、またはワールド外を表すdefault配列です。</returns>
        private NativeArray<GeneratedTerrainCell> SelectChunk(int offsetX, int offsetY)
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
            NativeArray<GeneratedTerrainCell> southWest,
            NativeArray<GeneratedTerrainCell> south,
            NativeArray<GeneratedTerrainCell> southEast,
            NativeArray<GeneratedTerrainCell> west,
            NativeArray<GeneratedTerrainCell> center,
            NativeArray<GeneratedTerrainCell> east,
            NativeArray<GeneratedTerrainCell> northWest,
            NativeArray<GeneratedTerrainCell> north,
            NativeArray<GeneratedTerrainCell> northEast)
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
        private static bool IsUsable(NativeArray<GeneratedTerrainCell> tiles)
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

        /// <summary>中心から-1〜1の相対位置に差分所有者があるかを返します。</summary>
        private bool IsDeltaChunkAvailable(int offsetX, int offsetY)
        {
            int bitIndex = ((offsetY + 1) * 3) + offsetX + 1;
            return bitIndex >= 0
                && bitIndex < 9
                && (deltaAvailabilityMask & (1 << bitIndex)) != 0;
        }
    }

    /// <summary>
    /// ECSの完成チャンク索引から、同期表示処理へ安全に貸し出せる3×3地形近傍を構築します。
    /// </summary>
    public static class CompletedChunkTerrainNeighborhoodFactory
    {
        /// <summary>
        /// 完成自然地形と永続差分の座標索引から、現在の有効地形を返す3×3近傍を構築します。
        /// 差分所有者がないチャンクは自然生成をそのまま使用し、別レイヤー・別座標の差分は参照しません。
        /// </summary>
        /// <param name="entityManager">自然地形と差分Bufferを借用する現在Worldの管理境界です。</param>
        /// <param name="completedEntities">完成自然地形を座標で引く索引です。</param>
        /// <param name="deltaOwnerEntities">永続差分所有者をレイヤー付き座標で引く索引です。</param>
        /// <param name="centerCoordinate">近傍の中心となる論理チャンク座標です。</param>
        /// <param name="terrainCatalog">差分Runtime IDの分類を検証する地形カタログです。</param>
        /// <param name="neighborhood">成功時に有効地形を返す借用近傍です。</param>
        /// <returns>必要な自然地形と、存在する差分所有者をすべて検証して借用できた場合はtrueです。</returns>
        public static bool TryCreateEffective(
            EntityManager entityManager,
            IReadOnlyDictionary<ChunkCoordinate, Entity> completedEntities,
            IReadOnlyDictionary<ChunkCoordinate, Entity> deltaOwnerEntities,
            ChunkCoordinate centerCoordinate,
            BlobAssetReference<TerrainBlockCatalogBlob> terrainCatalog,
            out CompletedChunkTerrainNeighborhood neighborhood)
        {
            neighborhood = default;
            if (!terrainCatalog.IsCreated
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, -1, -1, out NativeArray<GeneratedTerrainCell> southWest)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, 0, -1, out NativeArray<GeneratedTerrainCell> south)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, 1, -1, out NativeArray<GeneratedTerrainCell> southEast)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, -1, 0, out NativeArray<GeneratedTerrainCell> west)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, 0, 0, out NativeArray<GeneratedTerrainCell> center)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, 1, 0, out NativeArray<GeneratedTerrainCell> east)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, -1, 1, out NativeArray<GeneratedTerrainCell> northWest)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, 0, 1, out NativeArray<GeneratedTerrainCell> north)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, 1, 1, out NativeArray<GeneratedTerrainCell> northEast)
                || !TryGetDeltaChunk(entityManager, deltaOwnerEntities, centerCoordinate, -1, -1, out NativeArray<TerrainCellDelta> southWestDeltas, out bool hasSouthWestDeltas)
                || !TryGetDeltaChunk(entityManager, deltaOwnerEntities, centerCoordinate, 0, -1, out NativeArray<TerrainCellDelta> southDeltas, out bool hasSouthDeltas)
                || !TryGetDeltaChunk(entityManager, deltaOwnerEntities, centerCoordinate, 1, -1, out NativeArray<TerrainCellDelta> southEastDeltas, out bool hasSouthEastDeltas)
                || !TryGetDeltaChunk(entityManager, deltaOwnerEntities, centerCoordinate, -1, 0, out NativeArray<TerrainCellDelta> westDeltas, out bool hasWestDeltas)
                || !TryGetDeltaChunk(entityManager, deltaOwnerEntities, centerCoordinate, 0, 0, out NativeArray<TerrainCellDelta> centerDeltas, out bool hasCenterDeltas)
                || !TryGetDeltaChunk(entityManager, deltaOwnerEntities, centerCoordinate, 1, 0, out NativeArray<TerrainCellDelta> eastDeltas, out bool hasEastDeltas)
                || !TryGetDeltaChunk(entityManager, deltaOwnerEntities, centerCoordinate, -1, 1, out NativeArray<TerrainCellDelta> northWestDeltas, out bool hasNorthWestDeltas)
                || !TryGetDeltaChunk(entityManager, deltaOwnerEntities, centerCoordinate, 0, 1, out NativeArray<TerrainCellDelta> northDeltas, out bool hasNorthDeltas)
                || !TryGetDeltaChunk(entityManager, deltaOwnerEntities, centerCoordinate, 1, 1, out NativeArray<TerrainCellDelta> northEastDeltas, out bool hasNorthEastDeltas))
            {
                return false;
            }

            ushort deltaMask = 0;
            if (hasSouthWestDeltas) deltaMask |= 1 << 0;
            if (hasSouthDeltas) deltaMask |= 1 << 1;
            if (hasSouthEastDeltas) deltaMask |= 1 << 2;
            if (hasWestDeltas) deltaMask |= 1 << 3;
            if (hasCenterDeltas) deltaMask |= 1 << 4;
            if (hasEastDeltas) deltaMask |= 1 << 5;
            if (hasNorthWestDeltas) deltaMask |= 1 << 6;
            if (hasNorthDeltas) deltaMask |= 1 << 7;
            if (hasNorthEastDeltas) deltaMask |= 1 << 8;

            neighborhood = new CompletedChunkTerrainNeighborhood(
                southWest, south, southEast,
                west, center, east,
                northWest, north, northEast,
                southWestDeltas, southDeltas, southEastDeltas,
                westDeltas, centerDeltas, eastDeltas,
                northWestDeltas, northDeltas, northEastDeltas,
                deltaMask,
                terrainCatalog);
            return true;
        }

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
            if (!TryGetChunk(entityManager, completedEntities, centerCoordinate, -1, -1, out NativeArray<GeneratedTerrainCell> southWest)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, 0, -1, out NativeArray<GeneratedTerrainCell> south)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, 1, -1, out NativeArray<GeneratedTerrainCell> southEast)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, -1, 0, out NativeArray<GeneratedTerrainCell> west)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, 0, 0, out NativeArray<GeneratedTerrainCell> center)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, 1, 0, out NativeArray<GeneratedTerrainCell> east)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, -1, 1, out NativeArray<GeneratedTerrainCell> northWest)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, 0, 1, out NativeArray<GeneratedTerrainCell> north)
                || !TryGetChunk(entityManager, completedEntities, centerCoordinate, 1, 1, out NativeArray<GeneratedTerrainCell> northEast))
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
            out NativeArray<GeneratedTerrainCell> tiles)
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
                || !entityManager.HasBuffer<GeneratedTerrainCell>(entity))
            {
                return false;
            }

            DynamicBuffer<GeneratedTerrainCell> buffer = entityManager.GetBuffer<GeneratedTerrainCell>(entity, true);
            if (buffer.Length != WorldGridConstants.TilesPerChunk)
            {
                return false;
            }

            // AsNativeArrayはコピーせず、同期処理の終了までだけECS Bufferを読み取り専用で貸し出します。
            tiles = buffer.AsNativeArray();
            return true;
        }

        /// <summary>相対チャンクの差分所有者を検証して借用し、所有者なしを正常な空差分として返します。</summary>
        private static bool TryGetDeltaChunk(
            EntityManager entityManager,
            IReadOnlyDictionary<ChunkCoordinate, Entity> deltaOwnerEntities,
            ChunkCoordinate center,
            int offsetX,
            int offsetY,
            out NativeArray<TerrainCellDelta> deltas,
            out bool hasOwner)
        {
            deltas = default;
            hasOwner = false;
            long chunkX = (long)center.X + offsetX;
            long chunkY = (long)center.Y + offsetY;
            if (chunkX < WorldGridConstants.MinimumLogicalChunkCoordinate
                || chunkX > WorldGridConstants.MaximumLogicalChunkCoordinate
                || chunkY < WorldGridConstants.MinimumLogicalChunkCoordinate
                || chunkY > WorldGridConstants.MaximumLogicalChunkCoordinate)
            {
                return true;
            }

            var coordinate = new ChunkCoordinate(center.Layer, (int)chunkX, (int)chunkY);
            if (!deltaOwnerEntities.TryGetValue(coordinate, out Entity entity))
            {
                return true;
            }

            if (!entityManager.Exists(entity)
                || !entityManager.HasComponent<TerrainChunkDeltaOwner>(entity)
                || !entityManager.HasBuffer<TerrainCellDelta>(entity))
            {
                return false;
            }

            TerrainChunkDeltaOwner owner = entityManager.GetComponentData<TerrainChunkDeltaOwner>(entity);
            if (owner.Layer != coordinate.Layer
                || owner.ChunkX != coordinate.X
                || owner.ChunkY != coordinate.Y)
            {
                return false;
            }

            deltas = entityManager.GetBuffer<TerrainCellDelta>(entity, true).AsNativeArray();
            hasOwner = true;
            return true;
        }
    }

}
