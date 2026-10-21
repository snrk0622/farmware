// このファイルは、TERRAIN-01C3-C2の空間Summaryキャッシュと、キャッシュを使う集水Resolverを提供します。
// 4,096件上限へ触れる場合はC3-C1の基準Resolverへ全面フォールバックし、集約による選択順や固定結果の変化を防ぎます。

using System;
using Unity.Collections;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// Seed・GeneratorVersionを固定した1ワールド内で、整列済み空間Summaryを識別する内部キーです。
    /// 表示チャンクや構築順を含めず、絶対水文座標とLevelだけで同じ領域を再利用します。
    /// </summary>
    internal struct HydrologyAggregationSummaryKey : IEquatable<HydrologyAggregationSummaryKey>
    {
        /// <summary>Summary領域の最小水平絶対水文座標です。</summary>
        public int MinimumCellX;

        /// <summary>Summary領域の最小垂直絶対水文座標です。</summary>
        public int MinimumCellY;

        /// <summary>0〜6の空間Summary Levelです。</summary>
        public byte Level;

        /// <summary>
        /// 二つのキーが同じ絶対領域とLevelを表すかを比較します。
        /// </summary>
        /// <param name="other">比較する別のSummaryキーです。</param>
        /// <returns>全固定値が一致する場合はtrueです。</returns>
        public bool Equals(HydrologyAggregationSummaryKey other)
        {
            return MinimumCellX == other.MinimumCellX
                && MinimumCellY == other.MinimumCellY
                && Level == other.Level;
        }

        /// <summary>
        /// boxed比較が必要な診断経路でも同じ値比較を使用します。
        /// </summary>
        /// <param name="obj">比較対象のobjectです。</param>
        /// <returns>同じ型かつ同じ固定値ならtrueです。</returns>
        public override bool Equals(object obj)
        {
            return obj is HydrologyAggregationSummaryKey other && Equals(other);
        }

        /// <summary>
        /// NativeParallelHashMapの格納位置を決める安定した32bitハッシュを返します。
        /// ハッシュ衝突時もEqualsで区別されるため、固定生成結果の正しさはこの分布へ依存しません。
        /// </summary>
        /// <returns>絶対座標とLevelを混合した32bit値です。</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = MinimumCellX;
                hash = (hash * 397) ^ MinimumCellY;
                return (hash * 397) ^ Level;
            }
        }
    }

    /// <summary>
    /// Summary利用数、分解数、キャッシュ命中、基準経路への復帰を診断する1回の集水統計です。
    /// 固定生成結果には含めず、性能比較と将来のEditor可視化だけで使用します。
    /// </summary>
    internal struct HydrologyAggregatedCatchmentStatistics
    {
        /// <summary>一括加算できた空間Summary数です。</summary>
        public int AggregatedSummaryCount;

        /// <summary>出口不一致、分解理由、上限越境により下位Levelへ展開した回数です。</summary>
        public int DecomposedSummaryCount;

        /// <summary>構築済みSummaryをキャッシュから取得した回数です。</summary>
        public int CacheHitCount;

        /// <summary>今回初めて固定入力からSummaryを構築した回数です。</summary>
        public int CacheMissCount;

        /// <summary>4,096件境界の正規化順を守るため、C3-C1基準Resolverで再計算した回数です。</summary>
        public int BaselineFallbackCount;
    }

    /// <summary>
    /// 1ワールド設定に属する空間Summary、外部入口、再利用作業配列を所有する内部キャッシュです。
    /// Main Threadで直列構築する比較実装であり、Jobへ書込可能コンテナを共有したりECS状態の正にしたりしません。
    /// </summary>
    internal sealed class HydrologyAggregationSummaryCache : IDisposable
    {
        /// <summary>このキャッシュの全Summaryを再生成できる、ワールド寿命中固定の設定コピーです。</summary>
        private readonly WorldGenerationSettings settings;

        /// <summary>絶対領域キーから固定Summaryを引くNativeキャッシュです。Dispose責任はこのクラスにあります。</summary>
        private NativeParallelHashMap<HydrologyAggregationSummaryKey, HydrologyCatchmentAggregationSummary> summaries;

        /// <summary>各Summaryキーに属する外部入口を保持するNative複数値キャッシュです。Dispose責任はこのクラスにあります。</summary>
        private NativeParallelMultiHashMap<HydrologyAggregationSummaryKey, HydrologyCatchmentExternalEntrance> entrances;

        /// <summary>Summary新規構築時に最大4,096領域セルを保持するPersistent作業配列です。</summary>
        private NativeArray<GeneratedHydrologyCell> summaryCells;

        /// <summary>Summary新規構築時に出口までの内部距離を保持するPersistent作業配列です。</summary>
        private NativeArray<ushort> summaryDistances;

        /// <summary>Summary新規構築時に最大260外部入口を受け取るPersistent作業配列です。</summary>
        private NativeArray<HydrologyCatchmentExternalEntrance> summaryEntrances;

        /// <summary>Resolverが現在のSummary入口だけを読むため、キャッシュ値を複製するPersistent Listです。</summary>
        private NativeList<HydrologyCatchmentExternalEntrance> currentEntrances;

        /// <summary>集約Resolverと基準フォールバックが共有し、呼出しごとの大容量確保を避ける上流Heap配列です。</summary>
        private NativeArray<HydrologyUpstreamCandidate> catchmentFrontier;

        /// <summary>Dispose後の再利用と二重解放を防ぐ、キャッシュ所有者だけが変更する寿命状態です。</summary>
        private bool disposed;

        /// <summary>
        /// 固定ワールド設定と予定Summary数から、比較用キャッシュと全Native作業領域を確保します。
        /// </summary>
        /// <param name="settings">このキャッシュ寿命中に変更しないワールド生成設定です。</param>
        /// <param name="summaryCapacity">再確保前に保持したいSummary件数です。1以上を指定します。</param>
        public HydrologyAggregationSummaryCache(in WorldGenerationSettings settings, int summaryCapacity)
        {
            if (!DeterministicHydrologyGenerator.ValidateSettings(settings.Hydrology))
            {
                throw new ArgumentException("固定水文設定が不正です。", nameof(settings));
            }

            if (summaryCapacity <= 0 || summaryCapacity > 4096)
            {
                throw new ArgumentOutOfRangeException(nameof(summaryCapacity));
            }

            this.settings = settings;
            summaries = new NativeParallelHashMap<HydrologyAggregationSummaryKey, HydrologyCatchmentAggregationSummary>(
                summaryCapacity,
                Allocator.Persistent);
            entrances = new NativeParallelMultiHashMap<HydrologyAggregationSummaryKey, HydrologyCatchmentExternalEntrance>(
                summaryCapacity * DeterministicHydrologyAggregationSummary.MaximumExternalEntranceCapacity,
                Allocator.Persistent);
            summaryCells = new NativeArray<GeneratedHydrologyCell>(
                DeterministicHydrologyAggregationSummary.MaximumCellCapacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            summaryDistances = new NativeArray<ushort>(
                DeterministicHydrologyAggregationSummary.MaximumCellCapacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            summaryEntrances = new NativeArray<HydrologyCatchmentExternalEntrance>(
                DeterministicHydrologyAggregationSummary.MaximumExternalEntranceCapacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            currentEntrances = new NativeList<HydrologyCatchmentExternalEntrance>(
                DeterministicHydrologyAggregationSummary.MaximumExternalEntranceCapacity,
                Allocator.Persistent);
            catchmentFrontier = new NativeArray<HydrologyUpstreamCandidate>(
                DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            disposed = false;
        }

        /// <summary>このキャッシュに固定したワールド生成設定を値として返します。</summary>
        internal WorldGenerationSettings Settings => settings;

        /// <summary>現在取得したSummaryの外部入口だけを保持する再利用Listを返します。</summary>
        internal NativeList<HydrologyCatchmentExternalEntrance> CurrentEntrances => currentEntrances;

        /// <summary>集約探索と基準フォールバックが順番に再利用する上流Heap配列を返します。</summary>
        internal NativeArray<HydrologyUpstreamCandidate> CatchmentFrontier => catchmentFrontier;

        /// <summary>現在キャッシュ済みのSummary件数を性能記録用に返します。</summary>
        internal int SummaryCount => summaries.IsCreated ? summaries.Count() : 0;

        /// <summary>
        /// 指定セルを含むLevel領域をキャッシュから取得し、未構築なら固定入力から一度だけ生成します。
        /// </summary>
        /// <param name="cellX">領域選択に使う水平絶対水文座標です。</param>
        /// <param name="cellY">領域選択に使う垂直絶対水文座標です。</param>
        /// <param name="level">0〜6の空間Summary Levelです。</param>
        /// <param name="summary">成功時の固定Summaryです。</param>
        /// <param name="cacheHit">既存値を使った場合はtrue、新規構築した場合はfalseです。</param>
        /// <returns>キャッシュが有効で、領域キーとSummaryを取得できた場合はtrueです。</returns>
        internal bool TryGetOrBuild(
            int cellX,
            int cellY,
            int level,
            out HydrologyCatchmentAggregationSummary summary,
            out bool cacheHit)
        {
            summary = default;
            cacheHit = false;
            currentEntrances.Clear();
            if (disposed
                || level < 0
                || level > settings.Hydrology.MaximumAggregationSummaryLevel
                || !TryCreateKey(cellX, cellY, level, out HydrologyAggregationSummaryKey key))
            {
                return false;
            }

            if (summaries.TryGetValue(key, out summary))
            {
                cacheHit = true;
                CopyCachedEntrances(key);
                return true;
            }

            if (!DeterministicHydrologyAggregationSummary.TryBuildAligned(
                settings,
                cellX,
                cellY,
                level,
                summaryCells,
                summaryDistances,
                summaryEntrances,
                out summary))
            {
                return false;
            }

            if (!summaries.TryAdd(key, summary))
            {
                return false;
            }

            for (int index = 0; index < summary.ExternalEntranceCount; index++)
            {
                HydrologyCatchmentExternalEntrance entrance = summaryEntrances[index];
                entrances.Add(key, entrance);
                currentEntrances.Add(entrance);
            }

            return true;
        }

        /// <summary>
        /// 所有するNativeコンテナを一度だけ破棄します。
        /// Jobへ貸し出さないMain Thread比較実装のため、呼出元は使用終了後に同期点を追加せずDisposeできます。
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (catchmentFrontier.IsCreated) catchmentFrontier.Dispose();
            if (currentEntrances.IsCreated) currentEntrances.Dispose();
            if (summaryEntrances.IsCreated) summaryEntrances.Dispose();
            if (summaryDistances.IsCreated) summaryDistances.Dispose();
            if (summaryCells.IsCreated) summaryCells.Dispose();
            if (entrances.IsCreated) entrances.Dispose();
            if (summaries.IsCreated) summaries.Dispose();
        }

        /// <summary>キャッシュ済みの複数値入口を現在の再利用Listへ複製します。</summary>
        /// <param name="key">取得済みSummaryと同じ絶対領域キーです。</param>
        private void CopyCachedEntrances(in HydrologyAggregationSummaryKey key)
        {
            foreach (HydrologyCatchmentExternalEntrance entrance in entrances.GetValuesForKey(key))
            {
                currentEntrances.Add(entrance);
            }
        }

        /// <summary>任意セル座標をLevel幅へ床方向整列し、キャッシュキーを作ります。</summary>
        /// <param name="cellX">領域選択に使う水平絶対水文座標です。</param>
        /// <param name="cellY">領域選択に使う垂直絶対水文座標です。</param>
        /// <param name="level">0〜6の空間Summary Levelです。</param>
        /// <param name="key">成功時の整列済み絶対領域キーです。</param>
        /// <returns>両軸の整列結果がint範囲内ならtrueです。</returns>
        private static bool TryCreateKey(
            int cellX,
            int cellY,
            int level,
            out HydrologyAggregationSummaryKey key)
        {
            key = default;
            int sideLength = 1 << level;
            if (!DeterministicHydrologyAggregationSummary.TryGetAlignedOrigin(
                    cellX,
                    sideLength,
                    out int minimumCellX)
                || !DeterministicHydrologyAggregationSummary.TryGetAlignedOrigin(
                    cellY,
                    sideLength,
                    out int minimumCellY))
            {
                return false;
            }

            key = new HydrologyAggregationSummaryKey
            {
                MinimumCellX = minimumCellX,
                MinimumCellY = minimumCellY,
                Level = (byte)level
            };
            return true;
        }
    }

    /// <summary>
    /// キャッシュ済み空間Summaryで完全な小規模集水域をまとめ、上限付近だけC3-C1基準探索へ戻す内部Resolverです。
    /// キャッシュ有無・構築順によらずHydrologyCatchmentResultを一致させる比較経路として使用します。
    /// </summary>
    internal static class DeterministicHydrologyAggregatedCatchmentResolver
    {
        /// <summary>HydrologyMoisture合計をRunoffEquivalentCellsへ戻す固定除数です。</summary>
        private const long MaximumNormalizedMoisture = ushort.MaxValue;

        /// <summary>
        /// 下流終端は既存Resolver、集水はSummaryキャッシュで求め、製品のGeneratedHydrologyCellと同じ全固定値を組み立てます。
        /// C3-C2の比較計測と同値性テストだけで使い、ChunkGenerationSystemの既定Job経路は性能判断まで変更しません。
        /// </summary>
        /// <param name="cache">同じ固定ワールド設定を所有するSummaryキャッシュです。</param>
        /// <param name="cellX">生成する水平絶対水文座標です。</param>
        /// <param name="cellY">生成する垂直絶対水文座標です。</param>
        /// <param name="cell">成功時の下流・集水・流出を含む固定水文セルです。</param>
        /// <param name="statistics">集水中のSummary利用・分解・命中・フォールバック回数です。</param>
        /// <returns>下流終端と集約集水の両方を完全に生成できた場合はtrueです。</returns>
        internal static bool TryGenerateCell(
            HydrologyAggregationSummaryCache cache,
            int cellX,
            int cellY,
            out GeneratedHydrologyCell cell,
            out HydrologyAggregatedCatchmentStatistics statistics)
        {
            cell = default;
            statistics = default;
            if (cache == null
                || !DeterministicHydrologyDownstreamResolver.TryResolveWithStartCell(
                    cache.Settings,
                    cellX,
                    cellY,
                    out cell,
                    out HydrologyDownstreamResult downstreamResult)
                || !TryResolve(cache, cellX, cellY, out HydrologyCatchmentResult catchmentResult, out statistics))
            {
                return false;
            }

            cell.DownstreamReachCellX = downstreamResult.ReachedCellX;
            cell.DownstreamReachCellY = downstreamResult.ReachedCellY;
            cell.DownstreamEdgeCount = downstreamResult.EdgeCount;
            cell.HydrologyMoisture = catchmentResult.TargetHydrologyMoisture;
            cell.CatchmentAreaCells = catchmentResult.CatchmentAreaCells;
            cell.RunoffMoistureSum = catchmentResult.RunoffMoistureSum;
            cell.RunoffEquivalentCells = catchmentResult.RunoffEquivalentCells;
            cell.MaximumContributingDistance = catchmentResult.MaximumContributingDistance;
            cell.ConnectionFlags = downstreamResult.Terminal | catchmentResult.SurfaceWaterConnectionFlags;
            cell.CatchmentFlags = catchmentResult.CatchmentFlags;
            // このResolverはC3-C2の集水Summary比較専用です。C3-D1の閉鎖流域解決を混ぜると、
            // 集水キャッシュだけの効果を測る既存ベンチマークへ湖探索時間が混入するため、ここでは基礎水文値だけを返します。
            return true;
        }

        /// <summary>
        /// 指定セルの集水結果を空間Summaryで求め、上限境界では基準Resolverへ安全に戻します。
        /// </summary>
        /// <param name="cache">同じWorldGenerationSettingsを所有する直列Summaryキャッシュです。</param>
        /// <param name="targetCellX">集水先の水平絶対水文座標です。</param>
        /// <param name="targetCellY">集水先の垂直絶対水文座標です。</param>
        /// <param name="result">成功時のC3-C1基準経路と同じ固定集水結果です。</param>
        /// <param name="statistics">今回の集約、分解、命中、フォールバック回数です。</param>
        /// <returns>固定入力、キャッシュ、全参照座標が有効で完全な結果を得られた場合はtrueです。</returns>
        internal static bool TryResolve(
            HydrologyAggregationSummaryCache cache,
            int targetCellX,
            int targetCellY,
            out HydrologyCatchmentResult result,
            out HydrologyAggregatedCatchmentStatistics statistics)
        {
            result = default;
            statistics = default;
            if (cache == null)
            {
                return false;
            }

            WorldGenerationSettings settings = cache.Settings;
            NativeArray<HydrologyUpstreamCandidate> frontier = cache.CatchmentFrontier;
            if (!DeterministicHydrologyGenerator.TryGenerateFlowCell(
                    settings,
                    targetCellX,
                    targetCellY,
                    out GeneratedHydrologyCell startCell)
                || !DeterministicHydrologyCatchmentResolver.TryCreateCandidate(
                    settings,
                    startCell,
                    0,
                    out HydrologyUpstreamCandidate target))
            {
                return false;
            }

            int frontierLength = 0;
            if (!DeterministicHydrologyCatchmentResolver.TryPush(frontier, 0, ref frontierLength, target))
            {
                return false;
            }

            uint selectedCount = 0;
            long moistureSum = 0L;
            ushort maximumDistance = 0;
            int maximumCells = settings.Hydrology.MaximumCatchmentContributingCells;

            while (frontierLength > 0)
            {
                if (selectedCount >= maximumCells)
                {
                    return ResolveWithBaseline(cache, startCell, ref statistics, out result);
                }

                HydrologyUpstreamCandidate current =
                    DeterministicHydrologyCatchmentResolver.Pop(frontier, 0, ref frontierLength);
                bool aggregated = false;
                for (int level = settings.Hydrology.MaximumAggregationSummaryLevel; level >= 0; level--)
                {
                    if (!cache.TryGetOrBuild(
                            current.CellX,
                            current.CellY,
                            level,
                            out HydrologyCatchmentAggregationSummary summary,
                            out bool cacheHit))
                    {
                        return false;
                    }

                    if (cacheHit) statistics.CacheHitCount++;
                    else statistics.CacheMissCount++;

                    if (!summary.CanAggregate
                        || summary.OutletCellX != current.CellX
                        || summary.OutletCellY != current.CellY
                        || !DeterministicHydrologyAggregationSummary.CanAccumulateWithoutCrossingLimit(
                            summary,
                            selectedCount,
                            maximumCells))
                    {
                        statistics.DecomposedSummaryCount++;
                        continue;
                    }

                    selectedCount += summary.CellCount;
                    moistureSum += summary.RunoffMoistureSum;
                    int summaryMaximumDistance = current.FlowDistanceFromTarget + summary.MaximumDistanceToOutlet;
                    if (summaryMaximumDistance >= ushort.MaxValue)
                    {
                        return false;
                    }

                    if (summaryMaximumDistance > maximumDistance)
                    {
                        maximumDistance = (ushort)summaryMaximumDistance;
                    }

                    NativeList<HydrologyCatchmentExternalEntrance> externalEntrances = cache.CurrentEntrances;
                    for (int entranceIndex = 0; entranceIndex < externalEntrances.Length; entranceIndex++)
                    {
                        HydrologyCatchmentExternalEntrance entrance = externalEntrances[entranceIndex];
                        int entranceDistance = current.FlowDistanceFromTarget + entrance.DistanceToOutlet;
                        if (entranceDistance >= ushort.MaxValue)
                        {
                            return false;
                        }

                        var candidate = new HydrologyUpstreamCandidate
                        {
                            CellX = entrance.CellX,
                            CellY = entrance.CellY,
                            PlateauRank = entrance.PlateauRank,
                            FlowDistanceFromTarget = (ushort)entranceDistance,
                            HydrologyHeight = entrance.HydrologyHeight,
                            HydrologyMoisture = entrance.HydrologyMoisture
                        };
                        if (!DeterministicHydrologyCatchmentResolver.TryPush(
                            frontier,
                            0,
                            ref frontierLength,
                            candidate))
                        {
                            return false;
                        }
                    }

                    statistics.AggregatedSummaryCount++;
                    aggregated = true;
                    break;
                }

                if (aggregated)
                {
                    // ちょうど上限へ達した場合も、外部入口や別枝との正規化順を推測せず基準経路で最終判定します。
                    if (selectedCount == maximumCells)
                    {
                        return ResolveWithBaseline(cache, startCell, ref statistics, out result);
                    }

                    continue;
                }

                // 終端セルなどLevel 0でもまとめられない1セルは、C3-C1と同じ8逆辺検査へ展開します。
                selectedCount++;
                moistureSum += current.HydrologyMoisture;
                if (current.FlowDistanceFromTarget > maximumDistance)
                {
                    maximumDistance = current.FlowDistanceFromTarget;
                }

                if (selectedCount == maximumCells)
                {
                    return ResolveWithBaseline(cache, startCell, ref statistics, out result);
                }

                for (int directionIndex = 1; directionIndex <= 8; directionIndex++)
                {
                    DeterministicHydrologyCatchmentResolver.GetDirectionOffset(
                        (HydrologyFlowDirection)directionIndex,
                        out int offsetX,
                        out int offsetY);
                    if (!DeterministicHydrologyCatchmentResolver.TryAdd(
                            current.CellX,
                            offsetX,
                            out int neighborX)
                        || !DeterministicHydrologyCatchmentResolver.TryAdd(
                            current.CellY,
                            offsetY,
                            out int neighborY)
                        || !DeterministicHydrologyGenerator.TryGenerateFlowCell(
                            settings,
                            neighborX,
                            neighborY,
                            out GeneratedHydrologyCell neighbor))
                    {
                        return false;
                    }

                    if (!neighbor.HasDownstream
                        || neighbor.DownstreamCellX != current.CellX
                        || neighbor.DownstreamCellY != current.CellY)
                    {
                        continue;
                    }

                    ushort upstreamDistance = (ushort)(current.FlowDistanceFromTarget + 1);
                    if (!DeterministicHydrologyCatchmentResolver.TryCreateCandidate(
                            settings,
                            neighbor,
                            upstreamDistance,
                            out HydrologyUpstreamCandidate upstream)
                        || !DeterministicHydrologyCatchmentResolver.TryPush(
                            frontier,
                            0,
                            ref frontierLength,
                            upstream))
                    {
                        return false;
                    }
                }
            }

            if (selectedCount == 0
                || selectedCount > maximumCells
                || moistureSum < 0L
                || moistureSum > (long)maximumCells * MaximumNormalizedMoisture)
            {
                return false;
            }

            ushort runoffEquivalentCells = (ushort)(moistureSum / MaximumNormalizedMoisture);
            result = new HydrologyCatchmentResult
            {
                CatchmentAreaCells = selectedCount,
                RunoffMoistureSum = moistureSum,
                RunoffEquivalentCells = runoffEquivalentCells,
                TargetHydrologyMoisture = target.HydrologyMoisture,
                MaximumContributingDistance = maximumDistance,
                CatchmentFlags = HydrologyCatchmentFlags.None,
                SurfaceWaterConnectionFlags =
                    DeterministicHydrologyCatchmentResolver.ClassifySurfaceWaterConnection(
                        settings.Hydrology,
                        runoffEquivalentCells)
            };
            return true;
        }

        /// <summary>上限境界の結果だけをC3-C1基準Resolverで再計算し、優先順と打切り判定を完全一致させます。</summary>
        /// <param name="cache">基準探索用Heapと固定設定を所有するキャッシュです。</param>
        /// <param name="startCell">対象セルの生成済みD8基礎値です。</param>
        /// <param name="statistics">フォールバック回数を加算する今回の診断統計です。</param>
        /// <param name="result">成功時の正規化済み基準集水結果です。</param>
        /// <returns>C3-C1基準Resolverが有限探索を完了できた場合はtrueです。</returns>
        private static bool ResolveWithBaseline(
            HydrologyAggregationSummaryCache cache,
            in GeneratedHydrologyCell startCell,
            ref HydrologyAggregatedCatchmentStatistics statistics,
            out HydrologyCatchmentResult result)
        {
            statistics.BaselineFallbackCount++;
            return DeterministicHydrologyCatchmentResolver.TryResolveWithStartCell(
                cache.Settings,
                startCell,
                cache.CatchmentFrontier,
                out result);
        }
    }
}
