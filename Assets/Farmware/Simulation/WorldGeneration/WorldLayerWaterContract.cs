// このファイルは、WORLD-01Bで確定した多層水域の製品用純粋計算契約を定義します。
// 後続の地形、液体、施設、移動Systemはここで定めた判定順と結果を利用し、TilemapやGameObjectを状態の正にしません。

using Farmware.Simulation;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 論理的に常在するレイヤーセルを、現在のSimulationで判定できるか表します。
    /// チャンクの常駐状態にだけ使用し、Unloadedをセルやレイヤーの不存在として扱いません。
    /// </summary>
    public enum WorldLayerCellReadiness : byte
    {
        /// <summary>論理セルは存在しますが、地形の生成または保存差分の読込が必要です。</summary>
        Unloaded = 0,

        /// <summary>有効地形と媒体を読み取り、移動や液体接続を判定できます。</summary>
        Ready = 1,
    }

    /// <summary>
    /// 多層遷移に必要な、セル内部の固体・空気・水の区別です。
    /// GroundBlockやLiquid等から導出する実行時の意味であり、この列挙値だけを地形差分として保存しません。
    /// </summary>
    public enum WorldLayerCellMedium : byte
    {
        /// <summary>未掘削の下層など、内部へ入れない固体です。</summary>
        Solid = 0,

        /// <summary>排水済み施設など、液体のない呼吸可能候補空間です。</summary>
        DryAir = 1,

        /// <summary>潜水・浮上の遷移元または遷移先になれる開放水域です。</summary>
        Water = 2,
    }

    /// <summary>
    /// 自然生成される1つのX/Y水柱が、どのレイヤーまで水域になるかを表します。
    /// 具体的な沖合判定や生成閾値はTERRAIN-01で決め、この契約では層ごとの意味だけを固定します。
    /// </summary>
    public enum NaturalWaterColumnKind : byte
    {
        /// <summary>地上は陸地で、自然状態の下層は未掘削の固体です。</summary>
        Land = 0,

        /// <summary>池、川、沿岸を含み、SurfaceとUnderwaterが水、DeepSeaが固体の自然水域です。</summary>
        StandardWater = 1,

        /// <summary>広い自然水域の沖合に限り、固定3層すべてが水になる深い自然水域です。</summary>
        OffshoreDeepWater = 2,
    }

    /// <summary>
    /// 2セルの間を通行と液体が接続できるか表す境界状態です。
    /// 通常の上下境界は有効地形から導出し、閉鎖水密配置物だけを後から合成します。
    /// </summary>
    public enum WorldLayerBoundaryKind : byte
    {
        /// <summary>地形や閉鎖配置物による遮断がなく、接続判定を続けられる境界です。</summary>
        Open = 0,

        /// <summary>上層の地面または下層の天井が残り、有効地形によって遮断された境界です。</summary>
        TerrainBlocked = 1,

        /// <summary>水密扉などの配置物が閉じ、通行と液体接続を遮断している境界です。</summary>
        ClosedWatertightBarrier = 2,
    }

    /// <summary>
    /// 施設単位の排水機能が、現在の固定Tickで成立しているか表します。
    /// 設備構成から再計算する値であり、配置・設備状態と重複する保存値にはしません。
    /// </summary>
    public enum FacilityDrainageState : byte
    {
        /// <summary>排水施設が未成立、停止、または成立条件を失った状態です。</summary>
        Unavailable = 0,

        /// <summary>接続中の水を除去し、対象施設の乾燥を維持できる状態です。</summary>
        Operational = 1,
    }

    /// <summary>
    /// セル間の通常移動と、潜水・浮上によるレイヤー移動を区別します。
    /// 同一レイヤーの連続8方向移動そのものはPLAYER-01で実装し、この契約は共有辺の候補だけを扱います。
    /// </summary>
    public enum WorldLayerTraversalKind : byte
    {
        /// <summary>同じレイヤーで上下左右の1辺を共有するセル間の境界確認です。</summary>
        Horizontal = 0,

        /// <summary>同じX/Yにある上下隣接レイヤー間の潜水または浮上です。</summary>
        VerticalLayerChange = 1,
    }

    /// <summary>
    /// 多層セル間の遷移を副作用なしで判定した結果です。
    /// 失敗理由と準備待ちを分け、呼び出し側が位置やレイヤーを部分変更しないために使用します。
    /// </summary>
    public enum WorldLayerTransitionResult : byte
    {
        /// <summary>地形・媒体・境界の条件を満たし、遷移を許可できます。</summary>
        Allowed = 0,

        /// <summary>移動先を生成または読み込み、同じ論理入力で再判定する必要があります。</summary>
        NeedsPreparation = 1,

        /// <summary>Noneまたは未知のレイヤーIDが入力されました。</summary>
        BlockedByInvalidLayer = 2,

        /// <summary>上下移動でX/Yが異なるか、水平移動でレイヤーが異なります。</summary>
        BlockedByMismatchedLocation = 3,

        /// <summary>水平共有辺または上下隣接層の条件を満たしません。</summary>
        BlockedByNonAdjacentDestination = 4,

        /// <summary>遷移元または遷移先が未掘削の固体です。</summary>
        BlockedBySolid = 5,

        /// <summary>地面、天井、閉鎖水密配置物が境界を遮断しています。</summary>
        BlockedByBoundary = 6,

        /// <summary>潜水・浮上の一方または両方が開放水域ではありません。</summary>
        BlockedByNonWaterVerticalCell = 7,

        /// <summary>排水なしで水と乾燥空間が接続され、媒体更新後の再判定が必要です。</summary>
        NeedsMediumUpdate = 8,

        /// <summary>準備状態、媒体、移動種別、境界、排水状態のいずれかに未知値が含まれます。</summary>
        BlockedByInvalidContractValue = 9,
    }

    /// <summary>
    /// 1つのレイヤーセルについて、多層遷移の純粋判定に必要な最小状態を保持します。
    /// WORLD-01Cで導入する正式なLayer付き座標型ではなく、現在の契約テストと後続実装の入力境界です。
    /// </summary>
    public readonly struct WorldLayerCellState
    {
        /// <summary>セルの符号付き絶対X座標です。作成後は変更しません。</summary>
        public int X { get; }

        /// <summary>セルの符号付き絶対Y座標です。作成後は変更しません。</summary>
        public int Y { get; }

        /// <summary>セルが属する固定ワールドレイヤーです。作成後は変更しません。</summary>
        public WorldLayer Layer { get; }

        /// <summary>セルの有効地形を現在判定できるかを表す準備状態です。</summary>
        public WorldLayerCellReadiness Readiness { get; }

        /// <summary>有効地形・液体・施設状態から導出済みのセル媒体です。</summary>
        public WorldLayerCellMedium Medium { get; }

        /// <summary>
        /// 多層遷移判定へ渡すセル状態を作成します。
        /// </summary>
        /// <param name="x">符号付き絶対X座標です。</param>
        /// <param name="y">符号付き絶対Y座標です。</param>
        /// <param name="layer">固定保存IDを持つワールドレイヤーです。</param>
        /// <param name="readiness">未ロードまたは判定可能の準備状態です。</param>
        /// <param name="medium">固体、乾燥空気、開放水域のいずれかです。</param>
        public WorldLayerCellState(
            int x,
            int y,
            WorldLayer layer,
            WorldLayerCellReadiness readiness,
            WorldLayerCellMedium medium)
        {
            X = x;
            Y = y;
            Layer = layer;
            Readiness = readiness;
            Medium = medium;
        }
    }

    /// <summary>
    /// 自然水柱、上下境界、固定Tick媒体更新、多層遷移の製品用契約を提供します。
    /// すべてのメソッドは入力を書き換えない純粋計算で、ECS Systemやテストから同じ判定を再利用できます。
    /// </summary>
    public static class WorldLayerWaterContract
    {
        /// <summary>
        /// 自然生成Blockセルと沖合候補から、固定3層へ適用する自然水柱区分を返します。
        /// </summary>
        /// <param name="terrainCell">地上生成で確定したBiomeとLiquidを持つ自然生成セルです。</param>
        /// <param name="runtimeIds">淡水、海水、マグマを識別する組み込みRuntime IDです。</param>
        /// <param name="isOffshoreDeepCandidate">海の中で沖合深水域として選ばれたセルならtrueです。</param>
        /// <param name="columnKind">成功時に得られる陸地、通常水域、沖合深水域の分類です。</param>
        /// <returns>Liquidが未設定または既知の組み込みIDならtrue、未知のLiquid IDならfalseです。</returns>
        public static bool TryResolveNaturalWaterColumnKind(
            in GeneratedTerrainCell terrainCell,
            in BuiltInTerrainBlockRuntimeIds runtimeIds,
            bool isOffshoreDeepCandidate,
            out NaturalWaterColumnKind columnKind)
        {
            if (!runtimeIds.IsValid)
            {
                columnKind = default;
                return false;
            }

            if (!terrainCell.LiquidRuntimeId.IsValid
                || terrainCell.LiquidRuntimeId.Value == runtimeIds.Magma.Value)
            {
                // マグマは水柱ではなく、液体なしの陸地と同じく下層を自然水域へ接続しません。
                columnKind = NaturalWaterColumnKind.Land;
                return true;
            }

            if (terrainCell.LiquidRuntimeId.Value == runtimeIds.FreshWater.Value)
            {
                // 淡水は沖合候補が誤って渡されてもDeepSeaへせず、Underwaterまでの通常水域に固定します。
                columnKind = NaturalWaterColumnKind.StandardWater;
                return true;
            }

            if (terrainCell.LiquidRuntimeId.Value == runtimeIds.SaltWater.Value)
            {
                // 地上Biomeを持たない海だけを深水候補にし、内陸の塩湖を通常水域へ固定します。
                columnKind = terrainCell.Biome == BiomeId.None && isOffshoreDeepCandidate
                    ? NaturalWaterColumnKind.OffshoreDeepWater
                    : NaturalWaterColumnKind.StandardWater;
                return true;
            }

            columnKind = default;
            return false;
        }

        /// <summary>
        /// 自然地形の水柱区分とレイヤーから、そのセルの初期媒体を返します。
        /// </summary>
        /// <param name="columnKind">陸地、通常水域、沖合深水域のいずれかです。</param>
        /// <param name="layer">媒体を問い合わせる固定ワールドレイヤーです。</param>
        /// <param name="medium">成功時に得られる自然状態の媒体です。</param>
        /// <returns>列挙値とレイヤーが製品契約内ならtrue、未知値ならfalseです。</returns>
        public static bool TryResolveNaturalMedium(
            NaturalWaterColumnKind columnKind,
            WorldLayer layer,
            out WorldLayerCellMedium medium)
        {
            if (!WorldLayerUtility.IsPlayable(layer))
            {
                medium = default;
                return false;
            }

            if (columnKind == NaturalWaterColumnKind.Land)
            {
                // 地上の陸地セルは歩行可能な空気側で、自然状態の海中・深海は未掘削の固体です。
                medium = layer == WorldLayer.Surface
                    ? WorldLayerCellMedium.DryAir
                    : WorldLayerCellMedium.Solid;
                return true;
            }

            if (columnKind == NaturalWaterColumnKind.StandardWater)
            {
                // 池、川、沿岸を含む自然水域は海中まで成立し、深海だけを沖合へ限定します。
                medium = layer == WorldLayer.DeepSea
                    ? WorldLayerCellMedium.Solid
                    : WorldLayerCellMedium.Water;
                return true;
            }

            if (columnKind == NaturalWaterColumnKind.OffshoreDeepWater)
            {
                medium = WorldLayerCellMedium.Water;
                return true;
            }

            medium = default;
            return false;
        }

        /// <summary>
        /// 上層の地面、下層の天井、閉鎖水密配置物を順に合成し、上下境界の現在状態を返します。
        /// </summary>
        /// <param name="hasUpperCellGround">変更差分適用後の上層セルに地面が残る場合はtrueです。</param>
        /// <param name="hasLowerCellCeiling">変更差分適用後の下層セルに天井が残る場合はtrueです。</param>
        /// <param name="hasClosedWatertightPlacement">閉じた水密扉等が境界を占有する場合はtrueです。</param>
        /// <returns>有効地形を優先して合成した上下境界です。</returns>
        public static WorldLayerBoundaryKind ResolveVerticalBoundary(
            bool hasUpperCellGround,
            bool hasLowerCellCeiling,
            bool hasClosedWatertightPlacement)
        {
            // 通常の上下接続は独立保存せず、境界の片側に地形が残るだけでも必ず遮断します。
            if (hasUpperCellGround || hasLowerCellCeiling)
            {
                return WorldLayerBoundaryKind.TerrainBlocked;
            }

            // 地形が開いている場合だけ、特殊な水密配置物の開閉状態を追加で合成します。
            if (hasClosedWatertightPlacement)
            {
                return WorldLayerBoundaryKind.ClosedWatertightBarrier;
            }

            return WorldLayerBoundaryKind.Open;
        }

        /// <summary>
        /// 現在媒体、外水への開口、排水成立状態から、同じ固定Tickの移動判定前に適用する媒体を返します。
        /// </summary>
        /// <param name="currentMedium">地形変更と前Tickの施設状態を反映した現在媒体です。</param>
        /// <param name="hasOpenWaterConnection">開いた境界を通して外水または上層水へ直接つながる場合はtrueです。</param>
        /// <param name="drainageState">対象施設の排水機能が現在成立しているかを表します。</param>
        /// <returns>媒体更新段階の完了後に移動判定が読む媒体です。</returns>
        public static WorldLayerCellMedium ResolveMediumAtFixedTick(
            WorldLayerCellMedium currentMedium,
            bool hasOpenWaterConnection,
            FacilityDrainageState drainageState)
        {
            // 排水設備は地形を掘削しないため、Solidを空気や水へ変換しません。
            if (currentMedium == WorldLayerCellMedium.Solid)
            {
                return WorldLayerCellMedium.Solid;
            }

            // 稼働中の施設は既存水と開口からの流入を抽象的に排出し、同じ固定Tickから乾燥を維持します。
            if (drainageState == FacilityDrainageState.Operational)
            {
                return WorldLayerCellMedium.DryAir;
            }

            // 上が水のセルを1階層掘った場合を含め、排水なしの開口は移動判定より先に自動浸水します。
            if (currentMedium == WorldLayerCellMedium.Water || hasOpenWaterConnection)
            {
                return WorldLayerCellMedium.Water;
            }

            // 密閉は既存水を消しませんが、元から乾燥していて外水接続もなければ乾燥を保てます。
            return WorldLayerCellMedium.DryAir;
        }

        /// <summary>
        /// 2セルの位置、準備状態、媒体、境界から、水平移動または潜水・浮上の候補結果を返します。
        /// </summary>
        /// <param name="source">現在位置に相当する判定済みセルです。</param>
        /// <param name="destination">移動先に相当するセルです。</param>
        /// <param name="traversalKind">同一レイヤーの共有辺か、上下レイヤー変更かを指定します。</param>
        /// <param name="boundaryKind">2セルの間にある、導出・合成済みの境界です。</param>
        /// <param name="connectedDryFacilityDrainageState">水と乾燥室の水平移動時に、乾燥側の排水が成立しているかを指定します。</param>
        /// <returns>許可、準備待ち、媒体更新待ち、または具体的な遮断理由です。</returns>
        public static WorldLayerTransitionResult EvaluateTransition(
            WorldLayerCellState source,
            WorldLayerCellState destination,
            WorldLayerTraversalKind traversalKind,
            WorldLayerBoundaryKind boundaryKind,
            FacilityDrainageState connectedDryFacilityDrainageState =
                FacilityDrainageState.Unavailable)
        {
            if ((source.Readiness != WorldLayerCellReadiness.Unloaded
                    && source.Readiness != WorldLayerCellReadiness.Ready)
                || (destination.Readiness != WorldLayerCellReadiness.Unloaded
                    && destination.Readiness != WorldLayerCellReadiness.Ready)
                || (source.Medium != WorldLayerCellMedium.Solid
                    && source.Medium != WorldLayerCellMedium.DryAir
                    && source.Medium != WorldLayerCellMedium.Water)
                || (destination.Medium != WorldLayerCellMedium.Solid
                    && destination.Medium != WorldLayerCellMedium.DryAir
                    && destination.Medium != WorldLayerCellMedium.Water)
                || (traversalKind != WorldLayerTraversalKind.Horizontal
                    && traversalKind != WorldLayerTraversalKind.VerticalLayerChange)
                || (boundaryKind != WorldLayerBoundaryKind.Open
                    && boundaryKind != WorldLayerBoundaryKind.TerrainBlocked
                    && boundaryKind != WorldLayerBoundaryKind.ClosedWatertightBarrier)
                || (connectedDryFacilityDrainageState != FacilityDrainageState.Unavailable
                    && connectedDryFacilityDrainageState != FacilityDrainageState.Operational))
            {
                // 未知値を通常移動や開口へ丸めると破損データから通過できるため、準備判定より先に拒否します。
                return WorldLayerTransitionResult.BlockedByInvalidContractValue;
            }

            if (!WorldLayerUtility.IsPlayable(source.Layer)
                || !WorldLayerUtility.IsPlayable(destination.Layer))
            {
                return WorldLayerTransitionResult.BlockedByInvalidLayer;
            }

            // レイヤー、座標、隣接関係はチャンクを読まなくても判断できるため、不要な生成要求を出す前に検証します。
            if (traversalKind == WorldLayerTraversalKind.VerticalLayerChange)
            {
                if (source.X != destination.X || source.Y != destination.Y)
                {
                    return WorldLayerTransitionResult.BlockedByMismatchedLocation;
                }

                if (!WorldLayerUtility.AreVerticallyAdjacent(source.Layer, destination.Layer))
                {
                    return WorldLayerTransitionResult.BlockedByNonAdjacentDestination;
                }
            }
            else
            {
                if (source.Layer != destination.Layer)
                {
                    return WorldLayerTransitionResult.BlockedByMismatchedLocation;
                }

                // int端同士の減算が周回しないようlongへ拡張し、上下左右の共有辺だけを受理します。
                long deltaX = (long)destination.X - source.X;
                long deltaY = (long)destination.Y - source.Y;
                bool sharesEdge = ((deltaX == -1 || deltaX == 1) && deltaY == 0)
                    || ((deltaY == -1 || deltaY == 1) && deltaX == 0);
                if (!sharesEdge)
                {
                    return WorldLayerTransitionResult.BlockedByNonAdjacentDestination;
                }
            }

            // 構造的に妥当な要求だけを準備対象とし、未ロードを不存在や固体と誤認せず読込後に再判定させます。
            if (source.Readiness == WorldLayerCellReadiness.Unloaded
                || destination.Readiness == WorldLayerCellReadiness.Unloaded)
            {
                return WorldLayerTransitionResult.NeedsPreparation;
            }

            if (source.Medium == WorldLayerCellMedium.Solid
                || destination.Medium == WorldLayerCellMedium.Solid)
            {
                return WorldLayerTransitionResult.BlockedBySolid;
            }

            if (boundaryKind != WorldLayerBoundaryKind.Open)
            {
                return WorldLayerTransitionResult.BlockedByBoundary;
            }

            if (traversalKind == WorldLayerTraversalKind.VerticalLayerChange)
            {
                // 上下移動は水同士だけに限定し、水中から直下の乾燥室へ直接入る特例を作りません。
                if (source.Medium != WorldLayerCellMedium.Water
                    || destination.Medium != WorldLayerCellMedium.Water)
                {
                    return WorldLayerTransitionResult.BlockedByNonWaterVerticalCell;
                }

                return WorldLayerTransitionResult.Allowed;
            }

            if (source.Medium == destination.Medium)
            {
                return WorldLayerTransitionResult.Allowed;
            }

            // 同一レイヤーの水密扉では、施設排水が水の流入を相殺している間だけ水と乾燥室を直接往来できます。
            if (connectedDryFacilityDrainageState == FacilityDrainageState.Operational)
            {
                return WorldLayerTransitionResult.Allowed;
            }

            // 排水なしの水・空気接続は先に浸水させ、古いDryAir状態のまま移動を確定しません。
            return WorldLayerTransitionResult.NeedsMediumUpdate;
        }
    }
}
