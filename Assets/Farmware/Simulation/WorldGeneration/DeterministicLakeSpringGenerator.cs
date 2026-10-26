// このファイルは、地下水計算後の湖候補を淡水湖・塩湖・一時湖へ確定し、地下水域の低い縁へ泉を追加します。
// DeterministicSurfaceWaterGeneratorの候補とDeterministicGroundwaterHydrologyGeneratorの地下水を読み、基礎D8・集水・流出量は変更しません。

using Unity.Collections;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 湖水収支と泉の固定整数式、区分境界、泉抑制範囲を再計算する設定です。
    /// WorldGenerationSettingsへ値として含め、GeneratorVersionと一緒にワールド寿命中は固定します。
    /// </summary>
    public struct LakeSpringGenerationSettings
    {
        /// <summary>湖面充填率へ掛ける整数比重です。</summary>
        public byte LakeFillWeight;

        /// <summary>湖末端の地下水ポテンシャルへ掛ける整数比重です。</summary>
        public byte LakeGroundwaterWeight;

        /// <summary>気温と基礎湿度から求めた気候保持力へ掛ける整数比重です。</summary>
        public byte LakeClimateRetentionWeight;

        /// <summary>湖水収支の3入力比重を合計した固定除数です。</summary>
        public byte LakeWeightTotal;

        /// <summary>越流しない湖を一時湖から恒久塩湖へ切り替える水収支下限です。</summary>
        public ushort PermanentSaltwaterLakeThreshold;

        /// <summary>越流しない湖を恒久塩湖から恒久淡水湖へ切り替える水収支下限です。</summary>
        public ushort PermanentFreshwaterLakeThreshold;

        /// <summary>泉放出度で地下水ポテンシャルへ掛ける整数比重です。</summary>
        public byte SpringGroundwaterWeight;

        /// <summary>泉放出度で隣接セルへの落差Supportへ掛ける整数比重です。</summary>
        public byte SpringDropWeight;

        /// <summary>泉放出度の地下水と落差の比重を合計した固定除数です。</summary>
        public byte SpringWeightTotal;

        /// <summary>水文セルの代表標高差を0〜65,535の落差Supportへ変換する固定倍率です。</summary>
        public ushort SpringDropMultiplier;

        /// <summary>地下水域の低い縁を固定泉源として採用する放出度下限です。</summary>
        public ushort SpringDischargeThreshold;

        /// <summary>密集した泉候補から最大候補だけを残す半径です。単位は水文セルです。</summary>
        public byte SpringSuppressionRadiusInCells;

        /// <summary>TERRAIN-01C4-Cで確定した湖水収支と泉の整数設定を作ります。</summary>
        /// <returns>比重2：1：1、湖区分境界、泉落差倍率、放出境界、4セル抑制半径を持つ設定です。</returns>
        public static LakeSpringGenerationSettings CreateDefault()
        {
            return new LakeSpringGenerationSettings
            {
                LakeFillWeight = 2,
                LakeGroundwaterWeight = 1,
                LakeClimateRetentionWeight = 1,
                LakeWeightTotal = 4,
                PermanentSaltwaterLakeThreshold = 26214,
                PermanentFreshwaterLakeThreshold = 52428,
                SpringGroundwaterWeight = 2,
                SpringDropWeight = 1,
                SpringWeightTotal = 3,
                SpringDropMultiplier = 16,
                SpringDischargeThreshold = 39321,
                SpringSuppressionRadiusInCells = 4
            };
        }
    }

    /// <summary>
    /// 地下水計算後に確定する湖候補の最終区分です。
    /// 表流水Bufferの安定Typeへ変換する純粋計算結果、水文セルの固定診断値、C4-C以降の監査理由に使用します。
    /// </summary>
    public enum LakeWaterBalanceClassification : byte
    {
        /// <summary>湖候補ではない、または区分をまだ計算していない状態です。</summary>
        None = 0,

        /// <summary>越流辺があり、水が滞留・濃縮し続けない恒久淡水湖です。</summary>
        OverflowFreshwater = 1,

        /// <summary>固定水面を持たず、降雨時だけ可変水面になり得る一時湖・乾燥湖底です。</summary>
        Temporary = 2,

        /// <summary>固定水面は維持する一方、淡水接続と淡水冠水源には数えない恒久塩湖です。</summary>
        PermanentSaltwater = 3,

        /// <summary>越流はないものの水収支が十分に高い閉鎖恒久淡水湖です。</summary>
        ClosedFreshwater = 4
    }

    /// <summary>
    /// 一つの水文セルで評価した泉候補です。
    /// 候補比較、抑制、細分化経路生成へ必要な固定値だけを保持します。
    /// </summary>
    public struct SpringCandidate
    {
        /// <summary>全条件を満たし、抑制比較へ参加できる場合は1、それ以外は0です。</summary>
        public byte IsCandidate;

        /// <summary>泉源候補の水平絶対水文セル座標です。</summary>
        public int CellX;

        /// <summary>泉源候補の垂直絶対水文セル座標です。</summary>
        public int CellY;

        /// <summary>泉水を流す最も低い適格隣接セルの水平座標です。</summary>
        public int OutflowCellX;

        /// <summary>泉水を流す最も低い適格隣接セルの垂直座標です。</summary>
        public int OutflowCellY;

        /// <summary>地下水2、落差Support1から求めた0〜65,535の固定放出度です。</summary>
        public ushort DischargePotential;
    }

    /// <summary>
    /// 湖候補、地下水、気候、近傍水文セルだけから湖区分と泉を決定する純粋計算です。
    /// 呼出し側所有の固定長NativeArrayを再利用し、Burst Job内で管理メモリを確保しません。
    /// </summary>
    public static class DeterministicLakeSpringGenerator
    {
        /// <summary>泉経路がチャンクへ入れる1セル外周を含む、泉源走査の各軸セル数です。</summary>
        public const int SpringSourceCellsPerAxis = 11;

        /// <summary>泉源走査へ4セル抑制半径を加えた、候補キャッシュの各軸セル数です。</summary>
        public const int SpringCandidateCellsPerAxis = 19;

        /// <summary>候補自身の8近傍も完全に評価する、水文キャッシュの各軸セル数です。</summary>
        public const int SpringHydrologyCellsPerAxis = 21;

        /// <summary>泉候補キャッシュに必要な固定要素数です。</summary>
        public const int SpringCandidateCellCount =
            SpringCandidateCellsPerAxis * SpringCandidateCellsPerAxis;

        /// <summary>泉水文キャッシュに必要な固定要素数です。</summary>
        public const int SpringHydrologyCellCount =
            SpringHydrologyCellsPerAxis * SpringHydrologyCellsPerAxis;

        /// <summary>泉水文21×21の正規表流水涵養有無を一度だけ保持する固定要素数です。</summary>
        public const int SpringRechargeCandidateCount = SpringHydrologyCellCount;

        /// <summary>交差9×9の先頭から泉水文21×21の先頭まで戻る固定セル数です。</summary>
        public const int SpringHydrologyOriginOffsetFromIntersecting = 6;

        /// <summary>設定が確定したC4-C契約と一致するか検証します。</summary>
        /// <param name="settings">検証する湖水収支・泉設定です。</param>
        /// <returns>暗黙補正せず固定生成へ使用できる場合はtrueです。</returns>
        public static bool ValidateSettings(in LakeSpringGenerationSettings settings)
        {
            return settings.LakeFillWeight == 2
                && settings.LakeGroundwaterWeight == 1
                && settings.LakeClimateRetentionWeight == 1
                && settings.LakeWeightTotal == 4
                && settings.PermanentSaltwaterLakeThreshold == 26214
                && settings.PermanentFreshwaterLakeThreshold == 52428
                && settings.PermanentSaltwaterLakeThreshold
                    < settings.PermanentFreshwaterLakeThreshold
                && settings.SpringGroundwaterWeight == 2
                && settings.SpringDropWeight == 1
                && settings.SpringWeightTotal == 3
                && settings.SpringDropMultiplier == 16
                && settings.SpringDischargeThreshold == 39321
                && settings.SpringSuppressionRadiusInCells == 4;
        }

        /// <summary>気温と乾燥圧から0〜65,535の気候保持力を求め、湖水収支を64bit整数で合成します。</summary>
        /// <param name="settings">比重2：1：1と合計4を持つ固定設定です。</param>
        /// <param name="lakeFillFraction">閉鎖流域で確定した0〜65,535の湖面充填率です。</param>
        /// <param name="groundwaterPotential">流域末端の0〜65,535地下水ポテンシャルです。</param>
        /// <param name="terrainTemperature">流域末端の標高寒冷補正後の地形気温です。</param>
        /// <param name="hydrologyMoisture">流域末端の固定9地点基礎湿度平均です。</param>
        /// <param name="waterBalance">成功時の0〜65,535湖水収支です。</param>
        /// <returns>設定が確定契約と一致し、整数式を評価できた場合はtrueです。</returns>
        public static bool TryCalculateLakeWaterBalance(
            in LakeSpringGenerationSettings settings,
            ushort lakeFillFraction,
            ushort groundwaterPotential,
            ushort terrainTemperature,
            ushort hydrologyMoisture,
            out ushort waterBalance)
        {
            waterBalance = default;
            if (!ValidateSettings(settings))
            {
                return false;
            }

            long evaporationPressure =
                ((long)terrainTemperature + (ushort.MaxValue - (long)hydrologyMoisture)) / 2L;
            long climateRetention = ushort.MaxValue - evaporationPressure;
            long weightedTotal = ((long)lakeFillFraction * settings.LakeFillWeight)
                + ((long)groundwaterPotential * settings.LakeGroundwaterWeight)
                + (climateRetention * settings.LakeClimateRetentionWeight);
            waterBalance = (ushort)(weightedTotal / settings.LakeWeightTotal);
            return true;
        }

        /// <summary>越流を最優先し、越流なしの水収支を二つの境界で一時湖・塩湖・淡水湖へ分けます。</summary>
        /// <param name="settings">湖区分境界を持つ固定設定です。</param>
        /// <param name="hasOverflowEdge">有効な固定越流辺がある場合はtrueです。</param>
        /// <param name="waterBalance">0〜65,535の湖水収支です。</param>
        /// <returns>固定表流水へ適用する最終湖区分です。設定不正時はNoneです。</returns>
        public static LakeWaterBalanceClassification ClassifyLake(
            in LakeSpringGenerationSettings settings,
            bool hasOverflowEdge,
            ushort waterBalance)
        {
            if (!ValidateSettings(settings))
            {
                return LakeWaterBalanceClassification.None;
            }

            if (hasOverflowEdge)
            {
                return LakeWaterBalanceClassification.OverflowFreshwater;
            }

            if (waterBalance < settings.PermanentSaltwaterLakeThreshold)
            {
                return LakeWaterBalanceClassification.Temporary;
            }

            return waterBalance < settings.PermanentFreshwaterLakeThreshold
                ? LakeWaterBalanceClassification.PermanentSaltwater
                : LakeWaterBalanceClassification.ClosedFreshwater;
        }

        /// <summary>一つの湖面候補へ最終区分と固定水面・淡水接続フラグを適用します。</summary>
        /// <param name="settings">湖水収支設定です。</param>
        /// <param name="candidate">C4-A2が生成した湖面候補です。</param>
        /// <param name="hasOverflowEdge">湖末端に有効な越流辺がある場合はtrueです。</param>
        /// <param name="groundwaterPotential">湖末端の固定地下水ポテンシャルです。</param>
        /// <param name="terrainTemperature">湖末端の地形気温です。</param>
        /// <param name="hydrologyMoisture">湖末端の基礎湿度です。</param>
        /// <param name="finalized">成功時の最終表流水要素です。</param>
        /// <param name="classification">成功時の湖最終区分です。</param>
        /// <param name="waterBalance">成功時の湖水収支です。</param>
        /// <returns>入力が湖面候補で、設定と水収支を完全に評価できた場合はtrueです。</returns>
        public static bool TryFinalizeLakeCandidate(
            in LakeSpringGenerationSettings settings,
            in GeneratedSurfaceWaterTile candidate,
            bool hasOverflowEdge,
            ushort groundwaterPotential,
            ushort terrainTemperature,
            ushort hydrologyMoisture,
            out GeneratedSurfaceWaterTile finalized,
            out LakeWaterBalanceClassification classification,
            out ushort waterBalance)
        {
            finalized = default;
            classification = LakeWaterBalanceClassification.None;
            waterBalance = default;
            if (candidate.Type != SurfaceWaterTileType.LakeCandidate
                || !TryCalculateLakeWaterBalance(
                    settings,
                    candidate.SourceAmount,
                    groundwaterPotential,
                    terrainTemperature,
                    hydrologyMoisture,
                    out waterBalance))
            {
                return false;
            }

            classification = ClassifyLake(settings, hasOverflowEdge, waterBalance);
            finalized = candidate;
            // 湖候補は地下水の長期涵養履歴として残しますが、最終的な固定水面・淡水接続は区分ごとに再設定します。
            finalized.Flags = SurfaceWaterTileFlags.RechargeCandidate;
            switch (classification)
            {
                case LakeWaterBalanceClassification.OverflowFreshwater:
                case LakeWaterBalanceClassification.ClosedFreshwater:
                    finalized.Type = SurfaceWaterTileType.PermanentFreshwaterLake;
                    finalized.Flags |= SurfaceWaterTileFlags.NaturalSurfaceWater
                        | SurfaceWaterTileFlags.FreshwaterConnected;
                    return true;
                case LakeWaterBalanceClassification.PermanentSaltwater:
                    finalized.Type = SurfaceWaterTileType.PermanentSaltwaterLake;
                    finalized.Flags |= SurfaceWaterTileFlags.NaturalSurfaceWater;
                    return true;
                case LakeWaterBalanceClassification.Temporary:
                    // LakeCandidate型を乾燥湖底の診断根拠として残し、固定水面フラグだけを付けません。
                    return true;
                default:
                    finalized = default;
                    return false;
            }
        }

        /// <summary>地下水2、代表標高の正の落差Support1から泉放出度を64bit整数で求めます。</summary>
        /// <param name="settings">落差倍率16と比重2：1を持つ固定設定です。</param>
        /// <param name="groundwaterPotential">泉源候補セルの地下水ポテンシャルです。</param>
        /// <param name="hydrologyHeight">泉源候補セルの代表標高です。</param>
        /// <param name="neighborMinimumHeight">地下水接続を持たない最低隣接セルの代表標高です。</param>
        /// <param name="dischargePotential">成功時の0〜65,535泉放出度です。</param>
        /// <returns>設定が有効で、隣接セルが泉源候補より低い場合はtrueです。</returns>
        public static bool TryCalculateSpringDischarge(
            in LakeSpringGenerationSettings settings,
            ushort groundwaterPotential,
            ushort hydrologyHeight,
            ushort neighborMinimumHeight,
            out ushort dischargePotential)
        {
            dischargePotential = default;
            if (!ValidateSettings(settings) || neighborMinimumHeight >= hydrologyHeight)
            {
                return false;
            }

            long dropSupport = (long)(hydrologyHeight - neighborMinimumHeight)
                * settings.SpringDropMultiplier;
            if (dropSupport > ushort.MaxValue)
            {
                dropSupport = ushort.MaxValue;
            }

            long weightedTotal = ((long)groundwaterPotential * settings.SpringGroundwaterWeight)
                + (dropSupport * settings.SpringDropWeight);
            dischargePotential = (ushort)(weightedTotal / settings.SpringWeightTotal);
            return true;
        }

        /// <summary>8近傍から地下水接続を持たず、現在セルより低い最小標高セルを安定座標順で選びます。</summary>
        /// <param name="current">評価する泉源候補セルです。</param>
        /// <param name="neighbors">周囲8セルだけを格納した配列です。</param>
        /// <param name="selected">成功時の最も低い適格隣接セルです。</param>
        /// <returns>低い非地下水セルが一つ以上ある場合はtrueです。</returns>
        public static bool TrySelectLowestSpringOutflowNeighbor(
            in GeneratedHydrologyCell current,
            NativeArray<GeneratedHydrologyCell> neighbors,
            out GeneratedHydrologyCell selected)
        {
            selected = default;
            if (!neighbors.IsCreated || neighbors.Length < 8)
            {
                return false;
            }

            bool found = false;
            for (int index = 0; index < 8; index++)
            {
                GeneratedHydrologyCell candidate = neighbors[index];
                bool hasGroundwater = (candidate.ConnectionFlags & HydrologyConnectionFlags.Groundwater) != 0;
                if (hasGroundwater || candidate.HydrologyHeight >= current.HydrologyHeight)
                {
                    continue;
                }

                if (!found
                    || candidate.HydrologyHeight < selected.HydrologyHeight
                    || (candidate.HydrologyHeight == selected.HydrologyHeight
                        && IsCoordinateEarlier(candidate.CellX, candidate.CellY, selected.CellX, selected.CellY)))
                {
                    found = true;
                    selected = candidate;
                }
            }

            return found;
        }

        /// <summary>抑制範囲内で二つの泉候補を比較し、放出度、X、Yの順で先になるか返します。</summary>
        /// <param name="candidate">比較する候補です。</param>
        /// <param name="other">現在の基準候補です。</param>
        /// <returns>candidateを泉源として残すべき場合はtrueです。</returns>
        public static bool IsPreferredSpringCandidate(
            in SpringCandidate candidate,
            in SpringCandidate other)
        {
            if (candidate.DischargePotential != other.DischargePotential)
            {
                return candidate.DischargePotential > other.DischargePotential;
            }

            return IsCoordinateEarlier(candidate.CellX, candidate.CellY, other.CellX, other.CellY);
        }

        /// <summary>
        /// 1チャンクの湖面候補だけを最終的な一時湖、塩湖、淡水湖へ置き換えます。
        /// C4-D2の周辺冠水源再構築で泉を生成せず、恒久淡水湖かどうかだけを製品規則と同じ計算で確定します。
        /// </summary>
        /// <param name="settings">湖水収支、地下水、気候を再現する固定ワールド生成設定です。</param>
        /// <param name="surfaceWater">成功時にLakeCandidateを最終湖区分へ置き換える4096件の表流水です。</param>
        /// <param name="frontier">湖末端の集水探索へ再利用する固定長Heapです。</param>
        /// <param name="basinMembers">閉鎖流域セルを保持する固定長配列です。</param>
        /// <param name="basinLookup">閉鎖流域所属を検索する固定Hash表です。</param>
        /// <returns>全湖候補を固定上限内で最終区分へ変換できた場合はtrueです。</returns>
        internal static bool TryFinalizeSurfaceWaterLakes(
            in WorldGenerationSettings settings,
            NativeArray<GeneratedSurfaceWaterTile> surfaceWater,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup)
        {
            if (!ValidateSettings(settings.LakeSpring)
                || !DeterministicGroundwaterGenerator.ValidateSettings(settings.Groundwater)
                || !surfaceWater.IsCreated
                || surfaceWater.Length < WorldGridConstants.TilesPerChunk
                || !frontier.IsCreated
                || frontier.Length < DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity
                || !basinMembers.IsCreated
                || basinMembers.Length < DeterministicHydrologyLakeResolver.MaximumBasinMembers
                || !basinLookup.IsCreated
                || basinLookup.Length < DeterministicHydrologyLakeResolver.LookupCapacity)
            {
                return false;
            }

            FixedList4096Bytes<LakeClassificationCacheEntry> lakeClassifications = default;
            HydrologyLakeBasinCache lakeCache = default;
            for (int tileIndex = 0; tileIndex < WorldGridConstants.TilesPerChunk; tileIndex++)
            {
                GeneratedSurfaceWaterTile tile = surfaceWater[tileIndex];
                if (tile.Type != SurfaceWaterTileType.LakeCandidate)
                {
                    continue;
                }

                if (!TryGetLakeClassification(
                        settings,
                        tile.SourceCellX,
                        tile.SourceCellY,
                        tile.SourceAmount,
                        frontier,
                        basinMembers,
                        basinLookup,
                        ref lakeCache,
                        ref lakeClassifications,
                        out LakeClassificationCacheEntry classification)
                    || !TryFinalizeLakeCandidate(
                        settings.LakeSpring,
                        tile,
                        classification.HasOverflow != 0,
                        classification.GroundwaterPotential,
                        classification.TerrainTemperature,
                        classification.HydrologyMoisture,
                        out GeneratedSurfaceWaterTile finalized,
                        out _,
                        out _))
                {
                    return false;
                }

                surfaceWater[tileIndex] = finalized;
            }

            return true;
        }

        /// <summary>
        /// 完成済み地下水へ湖最終区分と抑制済み泉を適用し、64水文セルと4096表流水を一括更新します。
        /// </summary>
        /// <param name="settings">湖・泉を含む固定ワールド生成設定です。</param>
        /// <param name="chunkX">対象の水平論理チャンク座標です。</param>
        /// <param name="chunkY">対象の垂直論理チャンク座標です。</param>
        /// <param name="hydrologyCells">成功時に湖接続と泉Streamを反映する行優先64水文セルです。</param>
        /// <param name="intersectingCells">地下水反映済みの交差9×9水文セルです。</param>
        /// <param name="surfaceWater">成功時に湖区分と泉流を反映する行優先4096表流水です。</param>
        /// <param name="frontier">外周水文セルの有限集水探索へ再利用する作業配列です。</param>
        /// <param name="basinMembers">外周湖状態の再構築へ再利用する流域作業配列です。</param>
        /// <param name="basinLookup">外周湖状態の再構築へ再利用する固定Hash表です。</param>
        /// <param name="pathWorkspace">泉の最大16×16経路探索へ再利用する作業配列です。</param>
        /// <param name="pathTiles">一つの泉経路を保持する作業配列です。</param>
        /// <param name="springSurfaceRechargeCandidates">通常表流水生成が同じ河道経路から記録した21×21涵養フラグです。</param>
        /// <param name="springHydrologyCells">21×21の地下水・湖状態を一時保持する配列です。</param>
        /// <param name="springCandidates">19×19の泉候補を一時保持する配列です。</param>
        /// <returns>湖と泉を固定上限内で完全に確定できた場合はtrueです。</returns>
        internal static bool TryFinalizeChunk(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            NativeArray<GeneratedHydrologyCell> hydrologyCells,
            NativeArray<GeneratedHydrologyCell> intersectingCells,
            NativeArray<GeneratedSurfaceWaterTile> surfaceWater,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            NativeArray<SurfaceWaterPathSearchNode> pathWorkspace,
            NativeArray<SurfaceWaterPathTile> pathTiles,
            NativeArray<SurfaceRechargeCandidate> springSurfaceRechargeCandidates,
            NativeArray<GeneratedHydrologyCell> springHydrologyCells,
            NativeArray<SpringCandidate> springCandidates)
        {
            // 同期ハッシュと単体テストでは1担当で周辺水文を準備します。製品Jobは同じ処理を8並列で先行実行します。
            return TryPrepareSpringHydrologyRowsLane(
                    settings,
                    chunkX,
                    chunkY,
                    intersectingCells,
                    frontier,
                    basinMembers,
                    basinLookup,
                    springHydrologyCells,
                    0,
                    1)
                && TryFinalizePreparedChunkWithRechargeRefresh(
                    settings,
                    chunkX,
                    chunkY,
                    hydrologyCells,
                    intersectingCells,
                    surfaceWater,
                    frontier,
                    basinMembers,
                    basinLookup,
                    pathWorkspace,
                    pathTiles,
                    springSurfaceRechargeCandidates,
                    springHydrologyCells,
                    springCandidates);
        }

        /// <summary>
        /// 21×21周辺水文のうち指定行担当だけを生成し、製品Jobが8担当へ安全に分割できるようにします。
        /// </summary>
        /// <param name="settings">湖・泉を含む固定ワールド生成設定です。</param>
        /// <param name="chunkX">対象の水平論理チャンク座標です。</param>
        /// <param name="chunkY">対象の垂直論理チャンク座標です。</param>
        /// <param name="intersectingCells">地下水反映済みの交差9×9水文セルです。</param>
        /// <param name="frontier">担当ごとに独立した集水Heap領域を持つ作業配列です。</param>
        /// <param name="basinMembers">担当ごとに独立した閉鎖流域セル領域を持つ作業配列です。</param>
        /// <param name="basinLookup">担当ごとに独立した流域座標Hash領域を持つ作業配列です。</param>
        /// <param name="springHydrologyCells">担当行だけを書き込む21×21水文出力です。</param>
        /// <param name="laneIndex">0以上laneCount未満の担当番号です。</param>
        /// <param name="laneCount">全行を剰余分割する担当数です。</param>
        /// <returns>担当行の全セルを固定上限内で完成できた場合はtrueです。</returns>
        internal static bool TryPrepareSpringHydrologyRowsLane(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            NativeArray<GeneratedHydrologyCell> intersectingCells,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            NativeArray<GeneratedHydrologyCell> springHydrologyCells,
            int laneIndex,
            int laneCount)
        {
            if (!ValidateSettings(settings.LakeSpring)
                || !DeterministicGroundwaterGenerator.ValidateSettings(settings.Groundwater)
                || !intersectingCells.IsCreated
                || intersectingCells.Length < DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount
                || !springHydrologyCells.IsCreated
                || springHydrologyCells.Length < SpringHydrologyCellCount
                || laneCount <= 0
                || laneIndex < 0
                || laneIndex >= laneCount
                || !frontier.IsCreated
                || frontier.Length < laneCount * DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity
                || !basinMembers.IsCreated
                || basinMembers.Length < laneCount * DeterministicHydrologyLakeResolver.MaximumBasinMembers
                || !basinLookup.IsCreated
                || basinLookup.Length < laneCount * DeterministicHydrologyLakeResolver.LookupCapacity
                || !TryGetChunkOrigins(
                    settings.Hydrology,
                    chunkX,
                    chunkY,
                    out _,
                    out _,
                    out int firstCellX,
                    out int firstCellY))
            {
                return false;
            }

            int frontierStart = laneIndex * DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity;
            int memberStart = laneIndex * DeterministicHydrologyLakeResolver.MaximumBasinMembers;
            int lookupStart = laneIndex * DeterministicHydrologyLakeResolver.LookupCapacity;
            NativeArray<HydrologyUpstreamCandidate> laneFrontier = frontier.GetSubArray(
                frontierStart,
                DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity);
            NativeArray<HydrologyUpstreamCandidate> laneBasinMembers = basinMembers.GetSubArray(
                memberStart,
                DeterministicHydrologyLakeResolver.MaximumBasinMembers);
            NativeArray<HydrologyBasinLookupSlot> laneBasinLookup = basinLookup.GetSubArray(
                lookupStart,
                DeterministicHydrologyLakeResolver.LookupCapacity);
            FixedList4096Bytes<LakeClassificationCacheEntry> lakeClassifications = default;
            HydrologyLakeBasinCache lakeCache = default;
            int hydrologyOriginX = firstCellX - 6;
            int hydrologyOriginY = firstCellY - 6;

            // 行番号を担当数で分けるため、別担当は同じ出力要素や作業領域へ同時に書き込みません。
            for (int offsetY = laneIndex; offsetY < SpringHydrologyCellsPerAxis; offsetY += laneCount)
            {
                for (int offsetX = 0; offsetX < SpringHydrologyCellsPerAxis; offsetX++)
                {
                    long cellLongX = (long)hydrologyOriginX + offsetX;
                    long cellLongY = (long)hydrologyOriginY + offsetY;
                    if (cellLongX < int.MinValue || cellLongX > int.MaxValue
                        || cellLongY < int.MinValue || cellLongY > int.MaxValue
                        || !TryGenerateSpringHydrologyCell(
                            settings,
                            (int)cellLongX,
                            (int)cellLongY,
                            firstCellX,
                            firstCellY,
                            intersectingCells,
                            laneFrontier,
                            laneBasinMembers,
                            laneBasinLookup,
                            ref lakeCache,
                            ref lakeClassifications,
                            out GeneratedHydrologyCell cell))
                    {
                        return false;
                    }

                    springHydrologyCells[(offsetY * SpringHydrologyCellsPerAxis) + offsetX] = cell;
                }
            }

            return true;
        }

        /// <summary>
        /// 21×21泉水文の涵養を通常表流水と同じ河道・河川幅・海面規則で再構築し、地下水を更新します。
        /// キャッシュ済み隣接セルの河道幅も対象へ反映し、対象自身のConnectionFlagsだけを見る近似を避けます。
        /// </summary>
        /// <param name="settings">表流水、地下水、湖・泉を含む固定ワールド生成設定です。</param>
        /// <param name="chunkX">対象の水平論理チャンク座標です。</param>
        /// <param name="chunkY">対象の垂直論理チャンク座標です。</param>
        /// <param name="springSurfaceRechargeCandidates">通常表流水生成が同じ河道経路から記録した21×21涵養フラグです。</param>
        /// <param name="springHydrologyCells">更新対象の21×21周辺水文です。</param>
        /// <returns>有限走査内ですべての涵養と地下水を再構築できた場合はtrueです。</returns>
        internal static bool TryRefreshSpringHydrologyRecharge(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            NativeArray<SurfaceRechargeCandidate> springSurfaceRechargeCandidates,
            NativeArray<GeneratedHydrologyCell> springHydrologyCells)
        {
            return TryRefreshSpringHydrologyRecharge(
                settings,
                chunkX,
                chunkY,
                springSurfaceRechargeCandidates,
                springHydrologyCells,
                default);
        }

        /// <summary>
        /// 21×21泉水文の涵養と地下水を更新し、テストや監査で必要な場合だけ最終涵養フラグも出力します。
        /// </summary>
        /// <param name="settings">表流水、地下水、湖・泉を含む固定ワールド生成設定です。</param>
        /// <param name="chunkX">対象の水平論理チャンク座標です。</param>
        /// <param name="chunkY">対象の垂直論理チャンク座標です。</param>
        /// <param name="springSurfaceRechargeCandidates">通常表流水生成が同じ河道経路から記録した21×21涵養フラグです。</param>
        /// <param name="springHydrologyCells">更新対象の21×21周辺水文です。</param>
        /// <param name="springRechargeCandidates">正規の涵養有無を受け取る21×21行優先配列です。</param>
        /// <returns>有限走査内ですべての涵養と地下水を再構築できた場合はtrueです。</returns>
        internal static bool TryRefreshSpringHydrologyRecharge(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            NativeArray<SurfaceRechargeCandidate> springSurfaceRechargeCandidates,
            NativeArray<GeneratedHydrologyCell> springHydrologyCells,
            NativeArray<SurfaceRechargeCandidate> springRechargeCandidates)
        {
            if (!DeterministicEnvironmentGenerator.ValidateSettings(settings)
                || !DeterministicHydrologyGenerator.ValidateSettings(settings.Hydrology)
                || !DeterministicGroundwaterGenerator.ValidateSettings(settings.Groundwater)
                || !springSurfaceRechargeCandidates.IsCreated
                || springSurfaceRechargeCandidates.Length < SpringRechargeCandidateCount
                || !springHydrologyCells.IsCreated
                || springHydrologyCells.Length < SpringHydrologyCellCount
                || (springRechargeCandidates.IsCreated
                    && springRechargeCandidates.Length < SpringRechargeCandidateCount)
                || !TryGetChunkOrigins(
                    settings.Hydrology,
                    chunkX,
                    chunkY,
                    out _,
                    out _,
                    out _,
                    out _))
            {
                return false;
            }

            for (int index = 0; index < SpringRechargeCandidateCount; index++)
            {
                GeneratedHydrologyCell cell = springHydrologyCells[index];
                bool previousRecharge = (cell.ConnectionFlags
                    & (HydrologyConnectionFlags.Stream | HydrologyConnectionFlags.River)) != 0
                    || (cell.LakeFlags & HydrologyLakeFlags.SurfaceCell) != 0;
                // 既存21×21生成が使った対象自身の河道・湖を基準にし、通常表流水と同じ経路から得た隣接河道分を追加します。
                bool hasRecharge = previousRecharge
                    || springSurfaceRechargeCandidates[index].HasRecharge != 0;
                if (springRechargeCandidates.IsCreated)
                {
                    springRechargeCandidates[index] = new SurfaceRechargeCandidate
                    {
                        HasRecharge = (byte)(hasRecharge ? 1 : 0)
                    };
                }

                // 隣接河道で涵養有無が変わったセルだけを再計算し、同じ入力の441セルを重複サンプルしません。
                if (hasRecharge == previousRecharge)
                {
                    continue;
                }

                if (!DeterministicGroundwaterHydrologyGenerator.TryGenerateCellGroundwater(
                        settings,
                        cell,
                        hasRecharge,
                        out ushort groundwaterPotential))
                {
                    return false;
                }

                cell.GroundwaterPotential = groundwaterPotential;
                cell.ConnectionFlags &= ~HydrologyConnectionFlags.Groundwater;
                if (groundwaterPotential >= settings.Groundwater.GroundwaterConnectionThreshold)
                {
                    cell.ConnectionFlags |= HydrologyConnectionFlags.Groundwater;
                }
                springHydrologyCells[index] = cell;
            }

            return true;
        }

        /// <summary>
        /// 正規表流水涵養で21×21地下水を更新した後、準備済み湖・泉結果を対象チャンクへ適用します。
        /// 失敗時は公開用64セルと4096表流水の更新を開始せず、呼出し元がBlockセルを含む6 Bufferをまとめて非公開にできます。
        /// </summary>
        /// <param name="settings">湖・泉を含む固定ワールド生成設定です。</param>
        /// <param name="chunkX">対象の水平論理チャンク座標です。</param>
        /// <param name="chunkY">対象の垂直論理チャンク座標です。</param>
        /// <param name="hydrologyCells">湖接続と泉Streamを反映する64水文セルです。</param>
        /// <param name="intersectingCells">地下水反映済みの交差9×9水文セルです。</param>
        /// <param name="surfaceWater">湖区分と泉流を反映する4096固定表流水です。</param>
        /// <param name="frontier">河道供給元と湖末端の再構築へ再利用する集水Heapです。</param>
        /// <param name="basinMembers">湖末端の閉鎖流域セル領域です。</param>
        /// <param name="basinLookup">湖末端の流域所属Hash領域です。</param>
        /// <param name="pathWorkspace">通常河道と泉経路へ再利用する探索配列です。</param>
        /// <param name="pathTiles">一つの河道または泉経路を保持する配列です。</param>
        /// <param name="springSurfaceRechargeCandidates">通常表流水生成が同じ河道経路から記録した21×21涵養フラグです。</param>
        /// <param name="springHydrologyCells">21×21周辺水文です。</param>
        /// <param name="springCandidates">19×19泉候補です。</param>
        /// <returns>涵養再構築と湖・泉適用を一括で完了できた場合はtrueです。</returns>
        internal static bool TryFinalizePreparedChunkWithRechargeRefresh(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            NativeArray<GeneratedHydrologyCell> hydrologyCells,
            NativeArray<GeneratedHydrologyCell> intersectingCells,
            NativeArray<GeneratedSurfaceWaterTile> surfaceWater,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            NativeArray<SurfaceWaterPathSearchNode> pathWorkspace,
            NativeArray<SurfaceWaterPathTile> pathTiles,
            NativeArray<SurfaceRechargeCandidate> springSurfaceRechargeCandidates,
            NativeArray<GeneratedHydrologyCell> springHydrologyCells,
            NativeArray<SpringCandidate> springCandidates)
        {
            return TryRefreshSpringHydrologyRecharge(
                    settings,
                    chunkX,
                    chunkY,
                    springSurfaceRechargeCandidates,
                    springHydrologyCells)
                && TryFinalizePreparedChunk(
                    settings,
                    chunkX,
                    chunkY,
                    hydrologyCells,
                    intersectingCells,
                    surfaceWater,
                    frontier,
                    basinMembers,
                    basinLookup,
                    pathWorkspace,
                    pathTiles,
                    springSurfaceRechargeCandidates,
                    springHydrologyCells,
                    springCandidates);
        }

        /// <summary>
        /// 準備済み21×21周辺水文から湖区分と泉を確定し、64水文セルと4096表流水を一括更新します。
        /// </summary>
        /// <param name="settings">湖・泉を含む固定ワールド生成設定です。</param>
        /// <param name="chunkX">対象の水平論理チャンク座標です。</param>
        /// <param name="chunkY">対象の垂直論理チャンク座標です。</param>
        /// <param name="hydrologyCells">湖・泉接続を反映する行優先64水文セルです。</param>
        /// <param name="intersectingCells">地下水反映済みの交差9×9水文セルです。</param>
        /// <param name="surfaceWater">湖区分と泉流を反映する行優先4096表流水です。</param>
        /// <param name="frontier">湖末端の再現へ使う集水Heap領域です。</param>
        /// <param name="basinMembers">湖末端の閉鎖流域セル領域です。</param>
        /// <param name="basinLookup">湖末端の流域所属Hash領域です。</param>
        /// <param name="pathWorkspace">泉の細分化経路探索に使う作業配列です。</param>
        /// <param name="pathTiles">一つの泉経路を保持する作業配列です。</param>
        /// <param name="springSurfaceRechargeCandidates">通常河道の中心線または河川幅が入る21×21セルです。</param>
        /// <param name="springHydrologyCells">先行行Jobが準備した21×21完成水文です。</param>
        /// <param name="springCandidates">19×19泉候補を一度ずつ保持する作業配列です。</param>
        /// <returns>湖区分と抑制済み泉を部分結果なしで反映できた場合はtrueです。</returns>
        internal static bool TryFinalizePreparedChunk(
            in WorldGenerationSettings settings,
            int chunkX,
            int chunkY,
            NativeArray<GeneratedHydrologyCell> hydrologyCells,
            NativeArray<GeneratedHydrologyCell> intersectingCells,
            NativeArray<GeneratedSurfaceWaterTile> surfaceWater,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            NativeArray<SurfaceWaterPathSearchNode> pathWorkspace,
            NativeArray<SurfaceWaterPathTile> pathTiles,
            NativeArray<SurfaceRechargeCandidate> springSurfaceRechargeCandidates,
            NativeArray<GeneratedHydrologyCell> springHydrologyCells,
            NativeArray<SpringCandidate> springCandidates)
        {
            if (!ValidateSettings(settings.LakeSpring)
                || !DeterministicGroundwaterGenerator.ValidateSettings(settings.Groundwater)
                || !hydrologyCells.IsCreated
                || hydrologyCells.Length < DeterministicHydrologyGenerator.CellsPerChunk
                || !intersectingCells.IsCreated
                || intersectingCells.Length < DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount
                || !surfaceWater.IsCreated || surfaceWater.Length < WorldGridConstants.TilesPerChunk
                || !frontier.IsCreated
                || frontier.Length < DeterministicHydrologyCatchmentResolver.MaximumFrontierCapacity
                || !basinMembers.IsCreated
                || basinMembers.Length < DeterministicHydrologyLakeResolver.MaximumBasinMembers
                || !basinLookup.IsCreated
                || basinLookup.Length < DeterministicHydrologyLakeResolver.LookupCapacity
                || !pathWorkspace.IsCreated
                || pathWorkspace.Length < DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount
                || !pathTiles.IsCreated
                || pathTiles.Length < DeterministicSurfaceWaterPathGenerator.MaximumSearchNodeCount
                || !springSurfaceRechargeCandidates.IsCreated
                || springSurfaceRechargeCandidates.Length < SpringRechargeCandidateCount
                || !springHydrologyCells.IsCreated
                || springHydrologyCells.Length < SpringHydrologyCellCount
                || !springCandidates.IsCreated
                || springCandidates.Length < SpringCandidateCellCount
                || !TryGetChunkOrigins(
                    settings.Hydrology,
                    chunkX,
                    chunkY,
                    out int minimumTileX,
                    out int minimumTileY,
                    out int firstCellX,
                    out int firstCellY))
            {
                return false;
            }

            FixedList4096Bytes<LakeClassificationCacheEntry> lakeClassifications = default;
            HydrologyLakeBasinCache lakeCache = default;

            // 地下水反映済み9×9セルのLake接続を最終区分へ合わせ、中央8×8を公開用Bufferへ戻します。
            for (int index = 0; index < DeterministicGroundwaterHydrologyGenerator.IntersectingCellCount; index++)
            {
                GeneratedHydrologyCell cell = intersectingCells[index];
                if (!TryFinalizeCellLakeConnection(
                        settings,
                        ref cell,
                        frontier,
                        basinMembers,
                        basinLookup,
                        ref lakeCache,
                        ref lakeClassifications))
                {
                    return false;
                }

                intersectingCells[index] = cell;
                int offsetX = index % DeterministicGroundwaterHydrologyGenerator.IntersectingCellsPerAxis;
                int offsetY = index / DeterministicGroundwaterHydrologyGenerator.IntersectingCellsPerAxis;
                if (!TrySynchronizeFinalizedLakeConnection(
                        cell,
                        offsetX,
                        offsetY,
                        springHydrologyCells))
                {
                    return false;
                }

                if (offsetX < DeterministicHydrologyGenerator.CellsPerChunkAxis
                    && offsetY < DeterministicHydrologyGenerator.CellsPerChunkAxis)
                {
                    hydrologyCells[(offsetY * DeterministicHydrologyGenerator.CellsPerChunkAxis) + offsetX] = cell;
                }
            }

            // 湖面候補を一時湖・塩湖・淡水湖へ置き換えます。同じ末端の計算は固定長キャッシュで再利用します。
            for (int tileIndex = 0; tileIndex < WorldGridConstants.TilesPerChunk; tileIndex++)
            {
                GeneratedSurfaceWaterTile tile = surfaceWater[tileIndex];
                if (tile.Type != SurfaceWaterTileType.LakeCandidate)
                {
                    continue;
                }

                if (!TryGetLakeClassification(
                        settings,
                        tile.SourceCellX,
                        tile.SourceCellY,
                        tile.SourceAmount,
                        frontier,
                        basinMembers,
                        basinLookup,
                        ref lakeCache,
                        ref lakeClassifications,
                        out LakeClassificationCacheEntry classification)
                    || !TryFinalizeLakeCandidate(
                        settings.LakeSpring,
                        tile,
                        classification.HasOverflow != 0,
                        classification.GroundwaterPotential,
                        classification.TerrainTemperature,
                        classification.HydrologyMoisture,
                        out GeneratedSurfaceWaterTile finalized,
                        out _,
                        out _))
                {
                    return false;
                }

                surfaceWater[tileIndex] = finalized;
            }

            // 候補範囲の各セルを一度だけ評価し、抑制比較で地下水生成や8近傍探索を繰り返しません。
            for (int candidateY = 0; candidateY < SpringCandidateCellsPerAxis; candidateY++)
            {
                for (int candidateX = 0; candidateX < SpringCandidateCellsPerAxis; candidateX++)
                {
                    int hydrologyX = candidateX + 1;
                    int hydrologyY = candidateY + 1;
                    int hydrologyIndex = (hydrologyY * SpringHydrologyCellsPerAxis) + hydrologyX;
                    GeneratedHydrologyCell current = springHydrologyCells[hydrologyIndex];
                    springCandidates[(candidateY * SpringCandidateCellsPerAxis) + candidateX] =
                        TryBuildSpringCandidateFromCache(
                            settings.LakeSpring,
                            current,
                            springSurfaceRechargeCandidates[hydrologyIndex].HasRecharge != 0,
                            hydrologyX,
                            hydrologyY,
                            springHydrologyCells,
                            out SpringCandidate candidate)
                            ? candidate
                            : default;
                }
            }

            // チャンクへ入り得る11×11泉源だけを調べ、半径4セル内の最大候補だけから固定小川を描きます。
            for (int sourceY = 4; sourceY < 4 + SpringSourceCellsPerAxis; sourceY++)
            {
                for (int sourceX = 4; sourceX < 4 + SpringSourceCellsPerAxis; sourceX++)
                {
                    SpringCandidate source = springCandidates[
                        (sourceY * SpringCandidateCellsPerAxis) + sourceX];
                    if (source.IsCandidate == 0
                        || IsSuppressedSpringCandidate(
                            settings.LakeSpring,
                            source,
                            sourceX,
                            sourceY,
                            springCandidates))
                    {
                        continue;
                    }

                    int sourceHydrologyX = sourceX + 1;
                    int sourceHydrologyY = sourceY + 1;
                    GeneratedHydrologyCell springCell = springHydrologyCells[
                        (sourceHydrologyY * SpringHydrologyCellsPerAxis) + sourceHydrologyX];
                    if (!TrySetSyntheticSpringEdge(ref springCell, source)
                        || !DeterministicSurfaceWaterPathGenerator.TryBuildChannelCenterLine(
                            settings,
                            springCell,
                            pathWorkspace,
                            pathTiles,
                            out int pathLength)
                        || !TryApplySpringPath(
                            settings,
                            source,
                            minimumTileX,
                            minimumTileY,
                            pathTiles,
                            pathLength,
                            surfaceWater))
                    {
                        return false;
                    }

                    int intersectingX = source.CellX - firstCellX;
                    int intersectingY = source.CellY - firstCellY;
                    if (intersectingX >= 0
                        && intersectingX < DeterministicGroundwaterHydrologyGenerator.IntersectingCellsPerAxis
                        && intersectingY >= 0
                        && intersectingY < DeterministicGroundwaterHydrologyGenerator.IntersectingCellsPerAxis)
                    {
                        int intersectingIndex =
                            (intersectingY * DeterministicGroundwaterHydrologyGenerator.IntersectingCellsPerAxis)
                            + intersectingX;
                        GeneratedHydrologyCell updated = intersectingCells[intersectingIndex];
                        updated.ConnectionFlags |= HydrologyConnectionFlags.Stream;
                        intersectingCells[intersectingIndex] = updated;

                        if (intersectingX < DeterministicHydrologyGenerator.CellsPerChunkAxis
                            && intersectingY < DeterministicHydrologyGenerator.CellsPerChunkAxis)
                        {
                            int localIndex =
                                (intersectingY * DeterministicHydrologyGenerator.CellsPerChunkAxis)
                                + intersectingX;
                            updated = hydrologyCells[localIndex];
                            updated.ConnectionFlags |= HydrologyConnectionFlags.Stream;
                            hydrologyCells[localIndex] = updated;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 交差9×9で最終確定した湖接続と診断区分を、同じ絶対セルの泉用21×21キャッシュへ同期します。
        /// </summary>
        /// <param name="finalizedCell">一時湖または恒久湖の判定を反映済みの交差水文セルです。</param>
        /// <param name="intersectingOffsetX">交差9×9内の水平位置です。0〜8を指定します。</param>
        /// <param name="intersectingOffsetY">交差9×9内の垂直位置です。0〜8を指定します。</param>
        /// <param name="springHydrologyCells">同期先となる21×21の泉判定用水文キャッシュです。</param>
        /// <returns>同じ絶対座標のキャッシュ要素へ湖接続と区分を同期できた場合はtrueです。</returns>
        internal static bool TrySynchronizeFinalizedLakeConnection(
            in GeneratedHydrologyCell finalizedCell,
            int intersectingOffsetX,
            int intersectingOffsetY,
            NativeArray<GeneratedHydrologyCell> springHydrologyCells)
        {
            if (!springHydrologyCells.IsCreated
                || springHydrologyCells.Length < SpringHydrologyCellCount
                || intersectingOffsetX < 0
                || intersectingOffsetX >= DeterministicGroundwaterHydrologyGenerator.IntersectingCellsPerAxis
                || intersectingOffsetY < 0
                || intersectingOffsetY >= DeterministicGroundwaterHydrologyGenerator.IntersectingCellsPerAxis)
            {
                return false;
            }

            int cacheX = intersectingOffsetX + SpringHydrologyOriginOffsetFromIntersecting;
            int cacheY = intersectingOffsetY + SpringHydrologyOriginOffsetFromIntersecting;
            int cacheIndex = (cacheY * SpringHydrologyCellsPerAxis) + cacheX;
            GeneratedHydrologyCell cachedCell = springHydrologyCells[cacheIndex];
            if (cachedCell.CellX != finalizedCell.CellX || cachedCell.CellY != finalizedCell.CellY)
            {
                return false;
            }

            // 涵養再構築後の地下水値や河道接続をセル全体の代入で失わないため、
            // 湖区分が変更するLakeビットだけを最終結果へ置き換えます。
            cachedCell.ConnectionFlags =
                (cachedCell.ConnectionFlags & ~HydrologyConnectionFlags.Lake)
                | (finalizedCell.ConnectionFlags & HydrologyConnectionFlags.Lake);
            cachedCell.LakeClassification = finalizedCell.LakeClassification;
            springHydrologyCells[cacheIndex] = cachedCell;
            return true;
        }

        /// <summary>
        /// 一つのセルの湖候補接続と診断用区分を最終水収支へ合わせ、一時湖だけLake接続を外します。
        /// 呼出し元所有の作業領域とキャッシュを更新しますが、ECSへ直接公開しません。
        /// </summary>
        private static bool TryFinalizeCellLakeConnection(
            in WorldGenerationSettings settings,
            ref GeneratedHydrologyCell cell,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            ref HydrologyLakeBasinCache lakeCache,
            ref FixedList4096Bytes<LakeClassificationCacheEntry> classifications)
        {
            if ((cell.LakeFlags & HydrologyLakeFlags.SurfaceCell) == 0)
            {
                cell.LakeClassification = LakeWaterBalanceClassification.None;
                return true;
            }

            if (!TryGetLakeClassification(
                    settings,
                    cell.DownstreamReachCellX,
                    cell.DownstreamReachCellY,
                    cell.LakeFillFraction,
                    frontier,
                    basinMembers,
                    basinLookup,
                    ref lakeCache,
                    ref classifications,
                    out LakeClassificationCacheEntry classification))
            {
                return false;
            }

            if (classification.Classification == LakeWaterBalanceClassification.Temporary)
            {
                cell.ConnectionFlags &= ~HydrologyConnectionFlags.Lake;
            }
            else
            {
                cell.ConnectionFlags |= HydrologyConnectionFlags.Lake;
            }

            // 表流水Bufferと水文診断が別々に最終区分を再計算しないよう、同じ確定値をセルへ保持します。
            cell.LakeClassification = classification.Classification;

            return true;
        }

        /// <summary>
        /// Editor診断が製品生成と同じ湖水収支を使えるよう、準備済み水文セルの最終湖区分を確定します。
        /// </summary>
        /// <param name="settings">湖水収支、地下水、基礎環境を含む固定生成設定です。</param>
        /// <param name="cell">C3-Dまでの湖状態を持ち、成功時にC4-Cの接続と区分が入るセルです。</param>
        /// <param name="frontier">末端セルの集水探索へ再利用する固定長Heapです。</param>
        /// <param name="basinMembers">閉鎖流域セルを保持する固定長配列です。</param>
        /// <param name="basinLookup">閉鎖流域所属を引く固定Hash表です。</param>
        /// <param name="lakeCache">同じ末端の流域計算を再利用する呼出し元キャッシュです。</param>
        /// <returns>湖面候補でない場合を含め、製品経路と同じ最終区分を確定できた場合はtrueです。</returns>
        internal static bool TryFinalizeCellLakeForVisualization(
            in WorldGenerationSettings settings,
            ref GeneratedHydrologyCell cell,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            ref HydrologyLakeBasinCache lakeCache)
        {
            FixedList4096Bytes<LakeClassificationCacheEntry> classifications = default;
            return TryFinalizeCellLakeConnection(
                settings,
                ref cell,
                frontier,
                basinMembers,
                basinLookup,
                ref lakeCache,
                ref classifications);
        }

        /// <summary>
        /// 湖末端の水文・地下水・気候を一度だけ生成し、固定長キャッシュへ保存します。
        /// 引数の末端座標と期待充填率が一致しない場合はfalseを返し、部分区分を使わせません。
        /// </summary>
        private static bool TryGetLakeClassification(
            in WorldGenerationSettings settings,
            int sinkCellX,
            int sinkCellY,
            ushort expectedFillFraction,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            ref HydrologyLakeBasinCache lakeCache,
            ref FixedList4096Bytes<LakeClassificationCacheEntry> classifications,
            out LakeClassificationCacheEntry result)
        {
            for (int index = 0; index < classifications.Length; index++)
            {
                LakeClassificationCacheEntry cached = classifications[index];
                if (cached.CellX == sinkCellX && cached.CellY == sinkCellY)
                {
                    result = cached;
                    return cached.FillFraction == expectedFillFraction;
                }
            }

            result = default;
            if (classifications.Length >= classifications.Capacity
                || !DeterministicHydrologyGenerator.TryGenerateCell(
                    settings,
                    sinkCellX,
                    sinkCellY,
                    frontier,
                    0,
                    basinMembers,
                    0,
                    basinLookup,
                    0,
                    ref lakeCache,
                    out GeneratedHydrologyCell sink)
                || (sink.LakeFlags & HydrologyLakeFlags.FixedLake) == 0
                || sink.LakeFillFraction != expectedFillFraction
                || !DeterministicGroundwaterHydrologyGenerator.TryGenerateCellGroundwater(
                    settings,
                    sink,
                    true,
                    out ushort groundwaterPotential)
                || !TryGetCellCenterTile(sinkCellX, sinkCellY, out int centerTileX, out int centerTileY)
                || !DeterministicEnvironmentGenerator.TryGenerateBaseClimate(
                    settings,
                    centerTileX,
                    centerTileY,
                    out GeneratedEnvironmentData environment)
                || !TryCalculateLakeWaterBalance(
                    settings.LakeSpring,
                    sink.LakeFillFraction,
                    groundwaterPotential,
                    environment.TerrainTemperature,
                    sink.HydrologyMoisture,
                    out ushort waterBalance))
            {
                return false;
            }

            result = new LakeClassificationCacheEntry
            {
                CellX = sinkCellX,
                CellY = sinkCellY,
                FillFraction = sink.LakeFillFraction,
                GroundwaterPotential = groundwaterPotential,
                TerrainTemperature = environment.TerrainTemperature,
                HydrologyMoisture = sink.HydrologyMoisture,
                WaterBalance = waterBalance,
                Classification = ClassifyLake(
                    settings.LakeSpring,
                    (sink.LakeFlags & HydrologyLakeFlags.Overflow) != 0,
                    waterBalance),
                HasOverflow = (byte)(((sink.LakeFlags & HydrologyLakeFlags.Overflow) != 0) ? 1 : 0)
            };
            classifications.Add(result);
            return result.Classification != LakeWaterBalanceClassification.None;
        }

        /// <summary>
        /// 交差9×9を再利用し、外周だけを固定上限の純粋生成で補って泉用21×21セルを作ります。
        /// 帯水係数が地下水境界未満なら代表標高だけを返し、高価な集水探索を安全に省略します。
        /// </summary>
        private static bool TryGenerateSpringHydrologyCell(
            in WorldGenerationSettings settings,
            int cellX,
            int cellY,
            int firstCellX,
            int firstCellY,
            NativeArray<GeneratedHydrologyCell> intersectingCells,
            NativeArray<HydrologyUpstreamCandidate> frontier,
            NativeArray<HydrologyUpstreamCandidate> basinMembers,
            NativeArray<HydrologyBasinLookupSlot> basinLookup,
            ref HydrologyLakeBasinCache lakeCache,
            ref FixedList4096Bytes<LakeClassificationCacheEntry> classifications,
            out GeneratedHydrologyCell cell)
        {
            int intersectingX = cellX - firstCellX;
            int intersectingY = cellY - firstCellY;
            if (intersectingX >= 0
                && intersectingX < DeterministicGroundwaterHydrologyGenerator.IntersectingCellsPerAxis
                && intersectingY >= 0
                && intersectingY < DeterministicGroundwaterHydrologyGenerator.IntersectingCellsPerAxis)
            {
                cell = intersectingCells[
                    (intersectingY * DeterministicGroundwaterHydrologyGenerator.IntersectingCellsPerAxis)
                    + intersectingX];
                return true;
            }

            // 地下水値は「涵養値×帯水係数÷65,535」なので、帯水係数そのものが接続境界未満なら
            // 最大涵養でも泉源になれません。この場合は8近傍比較に必要な代表標高だけを生成し、
            // 最大4,096セルの集水探索と湖解決を省略して周辺21×21の費用を抑えます。
            if (!DeterministicHydrologyGenerator.TryGenerateFlowCell(
                    settings,
                    cellX,
                    cellY,
                    out GeneratedHydrologyCell flowCell)
                || !DeterministicGroundwaterHydrologyGenerator.TrySampleAdjustedGeologyAverage(
                    settings,
                    cellX,
                    cellY,
                    out ushort adjustedPermeability,
                    out ushort adjustedGroundwaterStorage))
            {
                cell = default;
                return false;
            }

            ushort maximumGroundwaterPotential = adjustedPermeability < adjustedGroundwaterStorage
                ? adjustedPermeability
                : adjustedGroundwaterStorage;
            if (maximumGroundwaterPotential < settings.Groundwater.GroundwaterConnectionThreshold)
            {
                cell = flowCell;
                return true;
            }

            if (!DeterministicHydrologyGenerator.TryGenerateCell(
                    settings,
                    cellX,
                    cellY,
                    frontier,
                    0,
                    basinMembers,
                    0,
                    basinLookup,
                    0,
                    ref lakeCache,
                    out cell))
            {
                return false;
            }

            bool hasRechargeCandidate = (cell.ConnectionFlags
                & (HydrologyConnectionFlags.Stream | HydrologyConnectionFlags.River)) != 0
                || (cell.LakeFlags & HydrologyLakeFlags.SurfaceCell) != 0;
            if (!DeterministicGroundwaterHydrologyGenerator.TryGenerateCellGroundwater(
                    settings,
                    cell,
                    hasRechargeCandidate,
                    out ushort groundwaterPotential))
            {
                return false;
            }

            cell.GroundwaterPotential = groundwaterPotential;
            cell.ConnectionFlags &= ~HydrologyConnectionFlags.Groundwater;
            if (groundwaterPotential >= settings.Groundwater.GroundwaterConnectionThreshold)
            {
                cell.ConnectionFlags |= HydrologyConnectionFlags.Groundwater;
            }

            return TryFinalizeCellLakeConnection(
                settings,
                ref cell,
                frontier,
                basinMembers,
                basinLookup,
                ref lakeCache,
                ref classifications);
        }

        /// <summary>
        /// 21×21キャッシュの中央セルと8近傍から、一つの泉候補を組み立てます。
        /// 入力配列は読み取りだけとし、全条件を満たさない場合はdefault候補とfalseを返します。
        /// </summary>
        /// <param name="hasSurfaceChannel">通常河道の中心線または河川幅が対象セルへ入る場合はtrueです。</param>
        internal static bool TryBuildSpringCandidateFromCache(
            in LakeSpringGenerationSettings settings,
            in GeneratedHydrologyCell current,
            bool hasSurfaceChannel,
            int cacheX,
            int cacheY,
            NativeArray<GeneratedHydrologyCell> hydrologyCells,
            out SpringCandidate candidate)
        {
            candidate = default;
            HydrologyConnectionFlags permanentWater = current.ConnectionFlags
                & (HydrologyConnectionFlags.Stream | HydrologyConnectionFlags.River | HydrologyConnectionFlags.Lake);
            if ((current.ConnectionFlags & HydrologyConnectionFlags.Groundwater) == 0
                || permanentWater != HydrologyConnectionFlags.None
                || hasSurfaceChannel
                || current.IsOceanOutlet)
            {
                return false;
            }

            bool found = false;
            GeneratedHydrologyCell selected = default;
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    if (offsetX == 0 && offsetY == 0)
                    {
                        continue;
                    }

                    GeneratedHydrologyCell neighbor = hydrologyCells[
                        ((cacheY + offsetY) * SpringHydrologyCellsPerAxis) + cacheX + offsetX];
                    if ((neighbor.ConnectionFlags & HydrologyConnectionFlags.Groundwater) != 0
                        || neighbor.HydrologyHeight >= current.HydrologyHeight)
                    {
                        continue;
                    }

                    if (!found
                        || neighbor.HydrologyHeight < selected.HydrologyHeight
                        || (neighbor.HydrologyHeight == selected.HydrologyHeight
                            && IsCoordinateEarlier(
                                neighbor.CellX,
                                neighbor.CellY,
                                selected.CellX,
                                selected.CellY)))
                    {
                        found = true;
                        selected = neighbor;
                    }
                }
            }

            if (!found
                || !TryCalculateSpringDischarge(
                    settings,
                    current.GroundwaterPotential,
                    current.HydrologyHeight,
                    selected.HydrologyHeight,
                    out ushort discharge)
                || discharge < settings.SpringDischargeThreshold)
            {
                return false;
            }

            candidate = new SpringCandidate
            {
                IsCandidate = 1,
                CellX = current.CellX,
                CellY = current.CellY,
                OutflowCellX = selected.CellX,
                OutflowCellY = selected.CellY,
                DischargePotential = discharge
            };
            return true;
        }

        /// <summary>
        /// 半径4セルの円内に、放出度または座標順で先行する別候補があるか調べます。
        /// 候補配列は読み取りだけで、抑制対象ならtrueを返します。
        /// </summary>
        private static bool IsSuppressedSpringCandidate(
            in LakeSpringGenerationSettings settings,
            in SpringCandidate source,
            int sourceX,
            int sourceY,
            NativeArray<SpringCandidate> candidates)
        {
            int radius = settings.SpringSuppressionRadiusInCells;
            int radiusSquared = radius * radius;
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if ((offsetX == 0 && offsetY == 0)
                        || (offsetX * offsetX) + (offsetY * offsetY) > radiusSquared)
                    {
                        continue;
                    }

                    SpringCandidate other = candidates[
                        ((sourceY + offsetY) * SpringCandidateCellsPerAxis) + sourceX + offsetX];
                    if (other.IsCandidate != 0 && IsPreferredSpringCandidate(other, source))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 泉候補の隣接流出先を、既存D8とは別の合成Stream辺として経路生成器へ渡します。
        /// 作業用セルだけを書き換え、公開用の基礎D8は変更しません。
        /// </summary>
        private static bool TrySetSyntheticSpringEdge(
            ref GeneratedHydrologyCell cell,
            in SpringCandidate candidate)
        {
            int deltaX = candidate.OutflowCellX - candidate.CellX;
            int deltaY = candidate.OutflowCellY - candidate.CellY;
            HydrologyFlowDirection direction = ToFlowDirection(deltaX, deltaY);
            if (direction == HydrologyFlowDirection.None)
            {
                return false;
            }

            cell.DownstreamCellX = candidate.OutflowCellX;
            cell.DownstreamCellY = candidate.OutflowCellY;
            cell.FlowDirection = direction;
            cell.ConnectionFlags |= HydrologyConnectionFlags.Stream;
            return true;
        }

        /// <summary>
        /// 細分化済み泉経路を対象チャンクへ適用し、既存の強い水面分類は上書きしません。
        /// 対象チャンク内の表流水だけを書き換え、範囲外経路は隣接チャンク自身の生成へ委ねます。
        /// </summary>
        internal static bool TryApplySpringPath(
            in WorldGenerationSettings settings,
            in SpringCandidate source,
            int minimumTileX,
            int minimumTileY,
            NativeArray<SurfaceWaterPathTile> pathTiles,
            int pathLength,
            NativeArray<GeneratedSurfaceWaterTile> surfaceWater)
        {
            if (pathLength <= 0 || pathLength > pathTiles.Length)
            {
                return false;
            }

            for (int index = 0; index < pathLength; index++)
            {
                SurfaceWaterPathTile pathTile = pathTiles[index];
                int localX = pathTile.WorldTileX - minimumTileX;
                int localY = pathTile.WorldTileY - minimumTileY;
                if (localX < 0 || localX >= WorldGridConstants.LogicalChunkSize
                    || localY < 0 || localY >= WorldGridConstants.LogicalChunkSize)
                {
                    continue;
                }

                if (!DeterministicEnvironmentGenerator.TryGenerateBaseClimate(
                        settings,
                        pathTile.WorldTileX,
                        pathTile.WorldTileY,
                        out GeneratedEnvironmentData environment))
                {
                    return false;
                }

                // 通常河道と同じく海面未満は既存海水域として維持し、泉流や淡水接続を追加しません。
                if (environment.Elevation < settings.SeaLevelThreshold)
                {
                    continue;
                }

                int tileIndex = WorldGrid.ToTileIndex(localX, localY);
                GeneratedSurfaceWaterTile existing = surfaceWater[tileIndex];
                if (existing.Type == SurfaceWaterTileType.PermanentSaltwaterLake)
                {
                    // 淡水小川が塩湖へ合流しても、1本の流入だけで湖全体を淡水へ分類し直しません。
                    continue;
                }

                if (existing.Type != SurfaceWaterTileType.None
                    && existing.Type != SurfaceWaterTileType.LakeCandidate)
                {
                    // 河川や恒久淡水湖の代表値は維持し、淡水・自然水面フラグだけを保証します。
                    existing.Flags |= SurfaceWaterTileFlags.NaturalSurfaceWater
                        | SurfaceWaterTileFlags.FreshwaterConnected
                        | SurfaceWaterTileFlags.RechargeCandidate;
                    surfaceWater[tileIndex] = existing;
                    continue;
                }

                surfaceWater[tileIndex] = new GeneratedSurfaceWaterTile
                {
                    Type = SurfaceWaterTileType.SpringStream,
                    Flags = SurfaceWaterTileFlags.RechargeCandidate
                        | SurfaceWaterTileFlags.NaturalSurfaceWater
                        | SurfaceWaterTileFlags.FreshwaterConnected
                        | SurfaceWaterTileFlags.ChannelCenterLine,
                    SurfaceHeight = environment.Elevation,
                    SourceAmount = source.DischargePotential,
                    SourceCellX = source.CellX,
                    SourceCellY = source.CellY
                };
            }

            return true;
        }

        /// <summary>
        /// 論理チャンク左下タイルと、交差9×9水文Bufferの先頭セル座標を安全な64bit中間計算で求めます。
        /// 出力以外の状態は変更せず、座標範囲外または不正水文設定ではfalseを返します。
        /// </summary>
        private static bool TryGetChunkOrigins(
            in HydrologyGenerationSettings hydrologySettings,
            int chunkX,
            int chunkY,
            out int minimumTileX,
            out int minimumTileY,
            out int firstCellX,
            out int firstCellY)
        {
            minimumTileX = default;
            minimumTileY = default;
            firstCellX = default;
            firstCellY = default;
            long tileX = (long)chunkX * WorldGridConstants.LogicalChunkSize;
            long tileY = (long)chunkY * WorldGridConstants.LogicalChunkSize;
            long maximumTileX = tileX + WorldGridConstants.LogicalChunkSize - 1L;
            long maximumTileY = tileY + WorldGridConstants.LogicalChunkSize - 1L;
            if (tileX < int.MinValue || maximumTileX > int.MaxValue
                || tileY < int.MinValue || maximumTileY > int.MaxValue
                || !DeterministicHydrologyGenerator.TryGetCellCoordinate(
                    hydrologySettings,
                    (int)tileX,
                    (int)tileY,
                    out firstCellX,
                    out firstCellY))
            {
                return false;
            }

            minimumTileX = (int)tileX;
            minimumTileY = (int)tileY;
            return true;
        }

        /// <summary>
        /// 水文セル中心の絶対タイル座標をint範囲内で求めます。
        /// 出力以外の状態は変更せず、8倍した座標が範囲外ならfalseを返します。
        /// </summary>
        private static bool TryGetCellCenterTile(
            int cellX,
            int cellY,
            out int tileX,
            out int tileY)
        {
            tileX = default;
            tileY = default;
            long horizontal = (long)cellX * 8L;
            long vertical = (long)cellY * 8L;
            if (horizontal < int.MinValue || horizontal > int.MaxValue
                || vertical < int.MinValue || vertical > int.MaxValue)
            {
                return false;
            }

            tileX = (int)horizontal;
            tileY = (int)vertical;
            return true;
        }

        /// <summary>
        /// 隣接セル差分をHydrologyFlowDirectionの安定値へ変換します。
        /// 8近傍以外はNoneを返し、外部状態を変更しません。
        /// </summary>
        private static HydrologyFlowDirection ToFlowDirection(int deltaX, int deltaY)
        {
            if (deltaX == 1 && deltaY == 0) return HydrologyFlowDirection.East;
            if (deltaX == 1 && deltaY == 1) return HydrologyFlowDirection.NorthEast;
            if (deltaX == 0 && deltaY == 1) return HydrologyFlowDirection.North;
            if (deltaX == -1 && deltaY == 1) return HydrologyFlowDirection.NorthWest;
            if (deltaX == -1 && deltaY == 0) return HydrologyFlowDirection.West;
            if (deltaX == -1 && deltaY == -1) return HydrologyFlowDirection.SouthWest;
            if (deltaX == 0 && deltaY == -1) return HydrologyFlowDirection.South;
            return deltaX == 1 && deltaY == -1
                ? HydrologyFlowDirection.SouthEast
                : HydrologyFlowDirection.None;
        }

        /// <summary>
        /// X、次にYの昇順で一意な座標同点順を作ります。
        /// 四つの座標値を比較するだけで、外部状態を変更しません。
        /// </summary>
        private static bool IsCoordinateEarlier(int x, int y, int otherX, int otherY)
        {
            return x < otherX || (x == otherX && y < otherY);
        }

        /// <summary>同じ閉鎖流域の湖区分入力と結果を、チャンク生成中だけ再利用する固定長値です。</summary>
        private struct LakeClassificationCacheEntry
        {
            /// <summary>閉鎖流域末端の水平水文セル座標です。</summary>
            public int CellX;

            /// <summary>閉鎖流域末端の垂直水文セル座標です。</summary>
            public int CellY;

            /// <summary>C3-D1で確定した湖面充填率です。</summary>
            public ushort FillFraction;

            /// <summary>C4-B2で同じ固定入力から再現した末端地下水です。</summary>
            public ushort GroundwaterPotential;

            /// <summary>末端中心タイルの地形気温です。</summary>
            public ushort TerrainTemperature;

            /// <summary>末端水文セルの固定9地点基礎湿度です。</summary>
            public ushort HydrologyMoisture;

            /// <summary>比重2：1：1で求めた湖水収支です。</summary>
            public ushort WaterBalance;

            /// <summary>越流を優先して確定した最終湖区分です。</summary>
            public LakeWaterBalanceClassification Classification;

            /// <summary>有効な固定越流辺があれば1、それ以外は0です。</summary>
            public byte HasOverflow;
        }
    }
}
