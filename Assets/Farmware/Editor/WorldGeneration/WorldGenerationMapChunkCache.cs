// このファイルは、Map Viewerのパン・ズーム・表示モード切替で、同じ完成自然地形の再生成を避けるEditor専用キャッシュです。
// Window単位で上限付きのManaged値コピーを保持し、各Sampleへ独立したNative配列を返します。ECSや地形差分は保持しません。
using System.Collections.Generic;
using Farmware.Simulation;
using Unity.Collections;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>同じ固定設定・Definitionで生成した診断チャンクをWindowの寿命中だけ再利用します。</summary>
    /// <remarks>Map Samplerの完成生成Gate内から直列に使用します。Window終了時は所有参照を手放すだけで、実行中要求を破棄しません。</remarks>
    public sealed class WorldGenerationMapChunkCache
    {
        /// <summary>通常の最大4×4チャンクと派生水文の隣接所有者を保持できる上限です。単位はチャンクです。</summary>
        public const int MaximumCachedChunks = 32;
        /// <summary>レイヤー付き座標ごとの不変な自然生成値です。変更差分を混ぜず、各要求の最新Snapshotで別途適用します。</summary>
        private readonly Dictionary<ChunkCoordinate, Snapshot> snapshots = new Dictionary<ChunkCoordinate, Snapshot>();
        /// <summary>容量超過時の除去順です。今回必要な既存値は生成開始前に末尾へ移し、他の古い値から除去します。</summary>
        private readonly Queue<ChunkCoordinate> insertionOrder = new Queue<ChunkCoordinate>();
        /// <summary>現在のキャッシュへ適用したSeed・GeneratorVersionを含む固定設定の値コピーです。</summary>
        private WorldGenerationSettings cachedSettings;
        /// <summary>最初の要求が設定を登録したかを示します。</summary>
        private bool hasSettings;
        /// <summary>現在保持する自然生成チャンク数です。Editor診断と所有権の回帰確認に使います。</summary>
        public int Count => snapshots.Count;

        /// <summary>同じ生成設定の自然地形があれば、呼び出し側がDisposeする独立コピーを返します。</summary>
        /// <param name="settings">今回の全固定生成設定です。</param>
        /// <param name="coordinate">復元するレイヤー付き座標です。</param>
        /// <param name="chunk">成功時は新規Native配列の所有者、未登録ならnullです。</param>
        /// <returns>同じ設定・座標の完成結果を復元できればtrueです。</returns>
        public bool TryRestore(in WorldGenerationSettings settings, ChunkCoordinate coordinate, out HydrologyDiagnosticChunk chunk)
        {
            SynchronizeSettings(settings);
            if (snapshots.TryGetValue(coordinate, out Snapshot snapshot))
            {
                chunk = snapshot.Restore(coordinate);
                return true;
            }
            chunk = null;
            return false;
        }

        /// <summary>非同期生成前に存在だけを調べ、Main Threadで不要なNativeコピーを作らないようにします。</summary>
        /// <param name="settings">今回の固定生成設定です。</param>
        /// <param name="coordinate">レイヤー付き座標です。</param>
        /// <returns>同じ設定の完成値があればtrueです。</returns>
        internal bool Contains(in WorldGenerationSettings settings, ChunkCoordinate coordinate)
        {
            SynchronizeSettings(settings);
            return snapshots.ContainsKey(coordinate);
        }

        /// <summary>今回使う既存値を除去順の末尾へ移し、未訪問分の追加で同じ要求の入力を失わないようにします。</summary>
        /// <param name="settings">今回の固定生成設定です。異なる設定の値は先に破棄します。</param>
        /// <param name="coordinates">生成開始前に確定した要求座標です。件数は保持上限以内に制限します。</param>
        internal void PrioritizeRequired(in WorldGenerationSettings settings, IReadOnlyList<ChunkCoordinate> coordinates)
        {
            SynchronizeSettings(settings);
            var required = new HashSet<ChunkCoordinate>();
            for (int index = 0; index < coordinates.Count; index++) required.Add(coordinates[index]);
            var retained = new List<ChunkCoordinate>();
            int previousCount = insertionOrder.Count;
            // 最大32件だけを一度並べ替えます。以後の不足分追加では今回不要な古い値から除去されます。
            for (int index = 0; index < previousCount; index++)
            {
                ChunkCoordinate coordinate = insertionOrder.Dequeue();
                if (required.Contains(coordinate)) retained.Add(coordinate);
                else insertionOrder.Enqueue(coordinate);
            }
            for (int index = 0; index < retained.Count; index++) insertionOrder.Enqueue(retained[index]);
        }

        /// <summary>生成が全段階成功した自然チャンクを値コピーし、保持上限を超えた古い値を除去します。</summary>
        /// <param name="settings">生成に使用した全固定設定です。</param>
        /// <param name="chunk">差分適用前の完成結果です。Native配列の所有権は呼び出し側に残ります。</param>
        public void Remember(in WorldGenerationSettings settings, HydrologyDiagnosticChunk chunk)
        {
            Remember(settings, chunk.Coordinate, chunk.TerrainRuntimeIds, chunk.TerrainTiles, chunk.TerrainCells,
                chunk.HydrologyCells, chunk.SurfaceWaterTiles, chunk.HydrologyTiles);
        }

        /// <summary>専用診断Worldの完成Bufferから直接値コピーし、中間Native配列を確保しません。</summary>
        /// <param name="settings">生成済み固定設定です。</param>
        /// <param name="coordinate">完成チャンクの座標です。</param>
        /// <param name="runtimeIds">生成に使った定義IDです。</param>
        /// <param name="tiles">完成環境タイルです。</param>
        /// <param name="cells">完成自然Blockセルです。</param>
        /// <param name="hydrology">完成水文セルです。</param>
        /// <param name="surfaceWater">完成表流水です。</param>
        /// <param name="hydrologyTiles">完成自然水文です。</param>
        internal void Remember(in WorldGenerationSettings settings, ChunkCoordinate coordinate,
            BuiltInTerrainBlockRuntimeIds runtimeIds, NativeArray<GeneratedTile> tiles,
            NativeArray<GeneratedTerrainCell> cells, NativeArray<GeneratedHydrologyCell> hydrology,
            NativeArray<GeneratedSurfaceWaterTile> surfaceWater, NativeArray<GeneratedHydrologyTile> hydrologyTiles)
        {
            SynchronizeSettings(settings);
            if (snapshots.ContainsKey(coordinate)) return;
            snapshots.Add(coordinate, new Snapshot(runtimeIds, tiles, cells, hydrology, surfaceWater, hydrologyTiles));
            insertionOrder.Enqueue(coordinate);
            // 配列はManaged値コピーだけなので、取消・Window終了時にNative資源の解放待ちは生じません。
            while (snapshots.Count > MaximumCachedChunks) snapshots.Remove(insertionOrder.Dequeue());
        }

        /// <summary>Seedだけでなく全固定設定を比較し、別設定の完成結果を暗黙に再利用しないようにします。</summary>
        /// <param name="settings">今回の固定生成設定です。</param>
        private void SynchronizeSettings(in WorldGenerationSettings settings)
        {
            if (hasSettings && cachedSettings.Equals(settings)) return;
            snapshots.Clear();
            insertionOrder.Clear();
            cachedSettings = settings;
            hasSettings = true;
        }

        /// <summary>Native配列の所有者が破棄された後も使える、1チャンクの自然生成値コピーです。</summary>
        private sealed class Snapshot
        {
            /// <summary>同じDomainの固定Definitionに対応するRuntime IDです。</summary>
            private readonly BuiltInTerrainBlockRuntimeIds runtimeIds;
            /// <summary>4096件の完成環境タイルです。構築後は変更しません。</summary>
            private readonly GeneratedTile[] tiles;
            /// <summary>4096件の自然生成Blockセルです。疎な変更差分は含みません。</summary>
            private readonly GeneratedTerrainCell[] cells;
            /// <summary>64件の完成固定水文セルです。</summary>
            private readonly GeneratedHydrologyCell[] hydrology;
            /// <summary>4096件の完成表流水です。</summary>
            private readonly GeneratedSurfaceWaterTile[] surfaceWater;
            /// <summary>4096件の完成自然水文タイルです。</summary>
            private readonly GeneratedHydrologyTile[] hydrologyTiles;

            /// <summary>成功済みの5配列だけをコピーし、生成元のEntityやNative配列は借用しません。</summary>
            /// <param name="runtimeIds">完成結果に対応する固定Definition IDです。</param>
            /// <param name="tiles">完成環境タイルです。</param>
            /// <param name="cells">完成自然Blockセルです。</param>
            /// <param name="hydrology">完成水文セルです。</param>
            /// <param name="surfaceWater">完成表流水です。</param>
            /// <param name="hydrologyTiles">完成自然水文です。</param>
            public Snapshot(BuiltInTerrainBlockRuntimeIds runtimeIds, NativeArray<GeneratedTile> tiles,
                NativeArray<GeneratedTerrainCell> cells, NativeArray<GeneratedHydrologyCell> hydrology,
                NativeArray<GeneratedSurfaceWaterTile> surfaceWater, NativeArray<GeneratedHydrologyTile> hydrologyTiles)
            {
                this.runtimeIds = runtimeIds;
                this.tiles = tiles.ToArray();
                this.cells = cells.ToArray();
                this.hydrology = hydrology.ToArray();
                this.surfaceWater = surfaceWater.ToArray();
                this.hydrologyTiles = hydrologyTiles.ToArray();
            }

            /// <summary>キャッシュへ書き戻せない独立したNative配列として復元します。</summary>
            /// <param name="coordinate">コピー元のレイヤー付き座標です。</param>
            /// <returns>呼び出し側だけがDisposeする完成チャンクです。</returns>
            public HydrologyDiagnosticChunk Restore(ChunkCoordinate coordinate)
            {
                return HydrologyDiagnosticChunk.CopyFromCache(coordinate, runtimeIds,
                    tiles, cells, hydrology, surfaceWater, hydrologyTiles);
            }
        }
    }
}
