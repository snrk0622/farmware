// このファイルは、Animal Population開発者表示が可視個体の位置だけを保持するための読取専用キャッシュです。
// ECS Chunkの位置範囲と変更版だけを索引にし、Simulationの個体状態・保存形式・描画方法は変更しません。
using System.Collections.Generic;
using Farmware.Simulation;
using Farmware.Simulation.Animals;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// Overlayが所有する表示用索引です。変更のない画面外Chunkの個体配列を読まず、可視タイル座標だけを保持します。
    /// Chunkは64×64の論理地域ではなくECSの格納単位です。広い範囲を混在保持するChunkでは追加走査が残ります。
    /// </summary>
    public sealed class AnimalPositionViewportCache
    {
        /// <summary>Chunk固有番号ごとの範囲・変更版です。全個体の座標やEntity参照は保持しません。</summary>
        private readonly Dictionary<ulong, ChunkBounds> boundsByChunk = new Dictionary<ulong, ChunkBounds>();
        /// <summary>今回Queryに存在するChunk番号です。破棄されたChunkの索引を更新終了時に除去します。</summary>
        private readonly HashSet<ulong> liveChunks = new HashSet<ulong>();
        /// <summary>列挙中にDictionaryを変更しないため、除去対象番号だけを一時保持して再利用します。</summary>
        private readonly List<ulong> removedChunks = new List<ulong>();
        /// <summary>現在の包含矩形に入るタイル座標だけです。同タイルの複数個体は1件へまとめます。</summary>
        private readonly HashSet<long> visiblePositions = new HashSet<long>();
        /// <summary>visiblePositionsが現在表す固定ワールドレイヤーです。更新のたびに呼び出し側の論理表示層へ合わせます。</summary>
        private WorldLayer visibleLayer = WorldLayer.Surface;

        /// <summary>直近更新で位置範囲を再構築するため読んだ個体数です。診断・負荷テストで使用します。</summary>
        public int BoundsPositionReadCount { get; private set; }
        /// <summary>直近更新で可視範囲と重なるChunkから読んだ個体数です。矩形外の同Chunk個体も含みます。</summary>
        public int VisiblePositionReadCount { get; private set; }
        /// <summary>現在キャッシュする可視タイル数です。Overlayでは最大8192タイルの矩形に制限されます。</summary>
        public int Count => visiblePositions.Count;
        /// <summary>位置範囲だけを保持する現在のECS Chunk数です。個体数ではありません。</summary>
        public int TrackedChunkCount => boundsByChunk.Count;

        /// <summary>Repaint時に呼び、Simulationが更新したChunk範囲と可視位置集合を読み直します。ECSへの書込はありません。</summary>
        /// <param name="manager">Queryと同じWorldです。位置の書込Jobだけを完了させてから読み取ります。</param>
        /// <param name="query">WildAnimalとAnimalTilePositionを持つ、Enableable条件なしのQueryです。</param>
        /// <param name="minimumX">可視範囲の最小絶対水平タイルです。</param>
        /// <param name="minimumY">可視範囲の最小絶対垂直タイルです。</param>
        /// <param name="maximumX">可視範囲の最大絶対水平タイルです。境界を含みます。</param>
        /// <param name="maximumY">可視範囲の最大絶対垂直タイルです。境界を含みます。</param>
        public void Update(EntityManager manager, EntityQuery query,
            int minimumX, int minimumY, int maximumX, int maximumY)
        {
            Update(
                manager,
                query,
                WorldLayer.Surface,
                minimumX,
                minimumY,
                maximumX,
                maximumY);
        }

        /// <summary>
        /// Repaint時に呼び、指定レイヤーの可視個体だけを表示用集合へ読み直します。ECSへの書込はありません。
        /// </summary>
        /// <param name="manager">Queryと同じWorldです。位置の書込Jobだけを完了させてから読み取ります。</param>
        /// <param name="query">WildAnimalとAnimalTilePositionを持つ、Enableable条件なしのQueryです。</param>
        /// <param name="layer">診断表示が現在参照する固定ワールドレイヤーです。</param>
        /// <param name="minimumX">可視範囲の最小絶対水平タイルです。</param>
        /// <param name="minimumY">可視範囲の最小絶対垂直タイルです。</param>
        /// <param name="maximumX">可視範囲の最大絶対水平タイルです。境界を含みます。</param>
        /// <param name="maximumY">可視範囲の最大絶対垂直タイルです。境界を含みます。</param>
        public void Update(EntityManager manager, EntityQuery query,
            WorldLayer layer, int minimumX, int minimumY, int maximumX, int maximumY)
        {
            visiblePositions.Clear(); liveChunks.Clear(); removedChunks.Clear();
            BoundsPositionReadCount = 0; VisiblePositionReadCount = 0;
            visibleLayer = layer;
            // 生成・移動Systemの書込Jobと競合しないよう、この表示が実際に読む型だけを同期します。
            manager.CompleteDependencyBeforeRO<AnimalTilePosition>();
            ComponentTypeHandle<AnimalTilePosition> type = manager.GetComponentTypeHandle<AnimalTilePosition>(true);
            using NativeArray<ArchetypeChunk> chunks = query.ToArchetypeChunkArray(Allocator.Temp);
            foreach (ArchetypeChunk chunk in chunks)
            {
                // SequenceNumberはWorldをまたいで固有なので、解放されたChunkのアドレス再利用を取り違えません。
                ulong key = chunk.SequenceNumber;
                liveChunks.Add(key);
                uint positionVersion = chunk.GetChangeVersion(ref type);
                uint orderVersion = chunk.GetOrderVersion();
                if (!boundsByChunk.TryGetValue(key, out ChunkBounds bounds)
                    || bounds.PositionVersion != positionVersion || bounds.OrderVersion != orderVersion
                    || bounds.Count != chunk.Count)
                {
                    // 位置書込・構造変更・件数変更があったChunkだけ全座標を調べ直します。
                    // このNativeArrayはChunkから借りたビューなのでDisposeせず、呼出し内だけで使用します。
                    NativeArray<AnimalTilePosition> changedPositions = chunk.GetNativeArray(ref type);
                    bounds = BuildBounds(changedPositions, positionVersion, orderVersion);
                    boundsByChunk[key] = bounds;
                    BoundsPositionReadCount += changedPositions.Length;
                }
                byte requestedLayerBit = ToLayerBit(layer);
                if (bounds.Count == 0
                    || requestedLayerBit == 0
                    || (bounds.LayerMask & requestedLayerBit) == 0
                    || minimumX > maximumX || minimumY > maximumY
                    || bounds.MaximumX < minimumX || bounds.MinimumX > maximumX
                    || bounds.MaximumY < minimumY || bounds.MinimumY > maximumY)
                    continue;

                NativeArray<AnimalTilePosition> positions = chunk.GetNativeArray(ref type);
                VisiblePositionReadCount += positions.Length;
                foreach (AnimalTilePosition position in positions)
                {
                    if (position.Layer == layer
                        && position.TileX >= minimumX && position.TileX <= maximumX
                        && position.TileY >= minimumY && position.TileY <= maximumY)
                        visiblePositions.Add(Encode(position.TileX, position.TileY));
                }
            }

            // Chunkの寿命を超えて索引が増え続けないよう、Queryから消えた分だけ削除します。
            foreach (KeyValuePair<ulong, ChunkBounds> entry in boundsByChunk)
                if (!liveChunks.Contains(entry.Key)) removedChunks.Add(entry.Key);
            foreach (ulong key in removedChunks) boundsByChunk.Remove(key);
        }

        /// <summary>タイル描画時に、現在の可視位置集合へ確定個体があるかを返します。</summary>
        /// <param name="tileX">絶対水平タイル座標です。</param>
        /// <param name="tileY">絶対垂直タイル座標です。</param>
        /// <returns>可視矩形内の同タイルに1個体以上いればtrueです。</returns>
        public bool Contains(int tileX, int tileY) => visiblePositions.Contains(Encode(tileX, tileY));

        /// <summary>指定レイヤーとタイルに、直近更新で確定した可視個体があるかを返します。</summary>
        /// <param name="layer">問い合わせる固定ワールドレイヤーです。</param>
        /// <param name="tileX">絶対水平タイル座標です。</param>
        /// <param name="tileY">絶対垂直タイル座標です。</param>
        /// <returns>直近Updateと同じレイヤーかつ同じX/Yに1個体以上いればtrueです。</returns>
        public bool Contains(WorldLayer layer, int tileX, int tileY)
        {
            return layer == visibleLayer && visiblePositions.Contains(Encode(tileX, tileY));
        }

        /// <summary>Worldの切替・破棄時に呼び、旧Worldの位置範囲と表示集合を破棄します。</summary>
        public void Clear()
        {
            boundsByChunk.Clear(); liveChunks.Clear(); removedChunks.Clear(); visiblePositions.Clear();
            BoundsPositionReadCount = 0; VisiblePositionReadCount = 0;
            visibleLayer = WorldLayer.Surface;
        }

        /// <summary>変更されたChunk内の最小・最大座標と版を計算し、次Repaintの除外判定へ渡します。</summary>
        /// <param name="positions">所有権を持たない位置配列ビューです。</param>
        /// <param name="positionVersion">Simulationの位置書込版です。</param>
        /// <param name="orderVersion">Chunkの構造変更版です。</param>
        /// <returns>個体座標を持たない範囲メタデータです。</returns>
        private static ChunkBounds BuildBounds(NativeArray<AnimalTilePosition> positions, uint positionVersion, uint orderVersion)
        {
            var bounds = new ChunkBounds { Count = positions.Length, PositionVersion = positionVersion, OrderVersion = orderVersion,
                MinimumX = int.MaxValue, MinimumY = int.MaxValue, MaximumX = int.MinValue, MaximumY = int.MinValue };
            foreach (AnimalTilePosition position in positions)
            {
                bounds.LayerMask |= ToLayerBit(position.Layer);
                bounds.MinimumX = System.Math.Min(bounds.MinimumX, position.TileX);
                bounds.MinimumY = System.Math.Min(bounds.MinimumY, position.TileY);
                bounds.MaximumX = System.Math.Max(bounds.MaximumX, position.TileX);
                bounds.MaximumY = System.Math.Max(bounds.MaximumY, position.TileY);
            }
            return bounds;
        }

        /// <summary>負座標も衝突しないよう、2つの32bit座標を64bitキーへ格納します。</summary>
        /// <param name="x">絶対水平タイルです。</param>
        /// <param name="y">絶対垂直タイルです。</param>
        /// <returns>HashSet検索にだけ使用するキーです。Simulationの永続IDではありません。</returns>
        private static long Encode(int x, int y) => ((long)x << 32) | (uint)y;

        /// <summary>固定3レイヤーをECS Chunkの粗い除外判定用bitへ変換します。未知値は0として表示対象外にします。</summary>
        /// <param name="layer">個体位置または診断表示が持つ固定レイヤーです。</param>
        /// <returns>Surfaceはbit0、Underwaterはbit1、DeepSeaはbit2、未知値は0です。</returns>
        private static byte ToLayerBit(WorldLayer layer)
        {
            switch (layer)
            {
                case WorldLayer.Surface: return 1 << 0;
                case WorldLayer.Underwater: return 1 << 1;
                case WorldLayer.DeepSea: return 1 << 2;
                default: return 0;
            }
        }

        /// <summary>1 ECS Chunkの除外判定用メタデータです。座標の正はECSのAnimalTilePositionに残します。</summary>
        private struct ChunkBounds
        {
            /// <summary>Chunk内に含まれる固定ワールドレイヤーを3bitで表す、表示専用の粗い索引です。</summary>
            public byte LayerMask;

            /// <summary>位置書込を検知するECS変更版です。書込主体はSimulation Systemです。</summary>
            public uint PositionVersion;
            /// <summary>追加・破棄・アーキタイプ移動を検知するChunk変更版です。</summary>
            public uint OrderVersion;
            /// <summary>メタデータ作成時の個体件数です。空Chunkも識別します。</summary>
            public int Count;
            /// <summary>Chunk内の最小絶対水平タイルです。</summary>
            public int MinimumX;
            /// <summary>Chunk内の最小絶対垂直タイルです。</summary>
            public int MinimumY;
            /// <summary>Chunk内の最大絶対水平タイルです。</summary>
            public int MaximumX;
            /// <summary>Chunk内の最大絶対垂直タイルです。</summary>
            public int MaximumY;
        }
    }
}
