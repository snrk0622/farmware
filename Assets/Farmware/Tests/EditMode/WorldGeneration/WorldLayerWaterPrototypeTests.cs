// このファイルは、WORLD-01Aで合意した多層水域ルールを少数セルの純粋なモデルで確認します。
// 製品用Runtime型ではなくEditModeテスト内だけに置き、試作結果をWORLD-01Bの契約へ暗黙に持ち込まないようにします。

using NUnit.Framework;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 地上・海中・深海の遷移、人工造成、上下遮断、排水施設による乾燥維持を比較する設計試作です。
    /// Unity Test RunnerのEditModeテストから実行し、製品型を作る前に合意した規則と残る未決事項を分離します。
    /// </summary>
    public sealed class WorldLayerWaterPrototypeTests
    {
        /// <summary>
        /// 試作で比較する固定3レイヤーです。
        /// 値や保存IDは製品契約ではなく、WORLD-01Bで別途確定します。
        /// </summary>
        private enum PrototypeWorldLayer
        {
            /// <summary>地上の試作レイヤーです。</summary>
            Surface,

            /// <summary>海中の試作レイヤーです。</summary>
            Underwater,

            /// <summary>深海の試作レイヤーです。</summary>
            DeepSea,
        }

        /// <summary>
        /// 論理的に常に存在する固定3層のセルを、現在判定できるか示す準備状態です。
        /// チャンクを必要時だけ生成・常駐させても、未ロードをレイヤー不存在として扱わないために使います。
        /// </summary>
        private enum PrototypeCellAvailability
        {
            /// <summary>論理セルは存在しますが、地形を判定する前にロードまたは生成が必要な状態です。</summary>
            Unloaded,

            /// <summary>地形と媒体を読み取って遷移を判定できる状態です。</summary>
            Ready,
        }

        /// <summary>
        /// 試作セルの内部を占める媒体です。
        /// GroundBlock、WallBlock、Liquidなどの製品用地形型へはまだ対応付けません。
        /// </summary>
        private enum PrototypeCellMedium
        {
            /// <summary>掘削されておらず、プレイヤーが入れない固体です。</summary>
            Solid,

            /// <summary>排水済みの部屋など、液体のない呼吸可能候補空間です。</summary>
            DryAir,

            /// <summary>潜水または浮上の遷移元・先になれる開放水域候補です。</summary>
            Water,
        }

        /// <summary>
        /// 2セル間をどの方向へ移動するかを表します。
        /// 通常移動とレイヤー移動では水域条件が異なるため、同じ判定へ混ぜません。
        /// </summary>
        private enum PrototypeTraversalKind
        {
            /// <summary>同じレイヤー内で隣接セルへ移動する試作です。</summary>
            Horizontal,

            /// <summary>同じX/Yの隣接レイヤーへ潜水または浮上する試作です。</summary>
            VerticalLayerChange,
        }

        /// <summary>
        /// 2セル間にある境界の候補状態です。
        /// 排水機能は境界ではなく施設全体の状態なので、別の列挙値で表します。
        /// </summary>
        private enum PrototypeBoundaryKind
        {
            /// <summary>地面や閉鎖扉がなく、2セルが直接つながる候補状態です。</summary>
            Open,

            /// <summary>現在の有効地形にある地面または天井から、上下遮断を導出した状態です。</summary>
            TerrainDerivedGround,

            /// <summary>水密扉などが閉じて通行と液体接続を遮断している状態です。</summary>
            ClosedWatertightBarrier,
        }

        /// <summary>
        /// 乾燥室へ作用する排水機能の試作状態です。
        /// 施設の具体的な成立条件や設備構成はWORLD-01B以降で決めるため、今回は稼働可否だけを比較します。
        /// </summary>
        private enum PrototypeDrainageState
        {
            /// <summary>排水施設が未成立、または成立条件を失って排水できない状態です。</summary>
            Unavailable,

            /// <summary>排水施設として成立し、接続中の水を除去して乾燥を維持できる状態です。</summary>
            Operational,
        }

        /// <summary>
        /// 試作の遷移判定結果です。
        /// 水・空気境界の環境更新が先に必要な場合は、移動許可へ丸めず専用結果を返します。
        /// </summary>
        private enum PrototypeTransitionResult
        {
            /// <summary>今回の仮仕様だけで遷移を許可できる結果です。</summary>
            Allowed,

            /// <summary>移動先をロードまたは生成してから再判定する必要があります。</summary>
            NeedsPreparation,

            /// <summary>遷移元または遷移先が固体で、セル内へ入れません。</summary>
            BlockedBySolid,

            /// <summary>地面、天井、閉鎖水密扉などの境界が移動を遮断しています。</summary>
            BlockedByBoundary,

            /// <summary>上下移動の遷移元と遷移先でX/Yが異なり、同座標条件を満たしません。</summary>
            BlockedByDifferentHorizontalCoordinate,

            /// <summary>通常の水平移動として指定されましたが、遷移元と遷移先のレイヤーが異なります。</summary>
            BlockedByDifferentLayerForHorizontalTraversal,

            /// <summary>隣接していないレイヤーへの移動で、中間レイヤーを飛ばそうとしています。</summary>
            BlockedByNonAdjacentLayer,

            /// <summary>潜水・浮上の少なくとも一方が開放水域ではありません。</summary>
            BlockedByNonWaterVerticalCell,

            /// <summary>開口後の浸水または排水を固定Tickで反映してから、移動を再判定する必要があります。</summary>
            NeedsFacilityWaterStateUpdate,
        }

        /// <summary>
        /// 1つのレイヤーセルについて、遷移判定に必要な最小状態だけを保持します。
        /// 座標、地形定義、部屋ID、酸素などは今回の比較に不要なため含めません。
        /// </summary>
        private readonly struct PrototypeCellState
        {
            /// <summary>
            /// セルの絶対X座標です。
            /// テストケースが作成して判定終了まで変更せず、製品用の座標範囲や格納型はまだ確定しません。
            /// </summary>
            public int X { get; }

            /// <summary>
            /// セルの絶対Y座標です。
            /// テストケースが作成して判定終了まで変更せず、上下レイヤー間の同座標条件に使います。
            /// </summary>
            public int Y { get; }

            /// <summary>
            /// セルが属する試作レイヤーです。
            /// テストケースが作成して判定終了まで変更しません。
            /// </summary>
            public PrototypeWorldLayer Layer { get; }

            /// <summary>
            /// セルを判定できる準備状態です。
            /// テストケースがUnloadedとReadyの差を比較するために設定します。
            /// </summary>
            public PrototypeCellAvailability Availability { get; }

            /// <summary>
            /// セル内部の固体・空気・水の候補状態です。
            /// AvailabilityがReadyのときだけ遷移可否の判定材料に使います。
            /// </summary>
            public PrototypeCellMedium Medium { get; }

            /// <summary>
            /// 試作セルのレイヤー、準備状態、媒体をまとめて作成します。
            /// </summary>
            /// <param name="x">試作セルの絶対X座標です。</param>
            /// <param name="y">試作セルの絶対Y座標です。</param>
            /// <param name="layer">セルが属する固定3レイヤーのいずれかです。</param>
            /// <param name="availability">未ロードまたは判定可能のいずれかです。</param>
            /// <param name="medium">固体、乾燥空気、水のいずれかです。</param>
            public PrototypeCellState(
                int x,
                int y,
                PrototypeWorldLayer layer,
                PrototypeCellAvailability availability,
                PrototypeCellMedium medium)
            {
                X = x;
                Y = y;
                Layer = layer;
                Availability = availability;
                Medium = medium;
            }
        }

        /// <summary>
        /// 上層セルの地面と下層セルの天井から、2層間の通常境界を導出します。
        /// 上下接続を独立保存せず、有効地形をSource of TruthにするB1の合意を試すための純粋関数です。
        /// </summary>
        private static class PrototypeVerticalBoundaryEvaluator
        {
            /// <summary>
            /// 上層の地面または下層の天井が残っていれば遮断とし、両方なければ開口を返します。
            /// 特殊ハッチ等の配置物は製品契約が未確定なので、この通常地形の導出には含めません。
            /// </summary>
            /// <param name="hasUpperCellGround">変更差分適用後の上層セルに地面がある場合はtrueです。</param>
            /// <param name="hasLowerCellCeiling">変更差分適用後の下層セルに天井がある場合はtrueです。</param>
            /// <returns>有効地形から導出した上下境界です。</returns>
            public static PrototypeBoundaryKind Resolve(
                bool hasUpperCellGround,
                bool hasLowerCellCeiling)
            {
                // 境界の片側だけに遮断地形が残る場合も通過させず、地形差分の更新漏れを隠しません。
                if (hasUpperCellGround || hasLowerCellCeiling)
                {
                    return PrototypeBoundaryKind.TerrainDerivedGround;
                }

                // 上層の地面と下層の天井を両方除いたときだけ、通常の上下開口として扱います。
                return PrototypeBoundaryKind.Open;
            }
        }

        /// <summary>
        /// 排水施設の現在状態から、次の固定Tick境界で室内を満たす媒体を求めます。
        /// 試作では有限水量や排水速度を扱わず、施設として成立したTickに水がなくなる合意を直接比較します。
        /// </summary>
        private static class PrototypeFacilityWaterEvaluator
        {
            /// <summary>
            /// 現在の室内媒体、外水との開口、排水状態を読み、次の固定Tickで使う媒体を返します。
            /// 入力値自体は変更せず、同じ入力から常に同じ結果を返します。
            /// </summary>
            /// <param name="currentMedium">評価前の室内媒体です。</param>
            /// <param name="hasOpenExteriorWaterConnection">扉などの開口を通して外水と直接つながっている場合はtrueです。</param>
            /// <param name="drainageState">施設全体の排水機能が成立しているかを表します。</param>
            /// <returns>次の固定Tick境界から室内へ適用する媒体です。</returns>
            public static PrototypeCellMedium ResolveAtFixedTick(
                PrototypeCellMedium currentMedium,
                bool hasOpenExteriorWaterConnection,
                PrototypeDrainageState drainageState)
            {
                // 固体は部屋の水・空気状態ではないため、排水設備によって空間へ変換しません。
                if (currentMedium == PrototypeCellMedium.Solid)
                {
                    return PrototypeCellMedium.Solid;
                }

                // 稼働中の排水施設は、既存の水と開口から入る水を抽象的に除去して乾燥状態を維持します。
                if (drainageState == PrototypeDrainageState.Operational)
                {
                    return PrototypeCellMedium.DryAir;
                }

                // 排水機能がなければ、密閉しても既にある水は自然消滅せず、外水への開口があれば乾燥室も浸水します。
                if (currentMedium == PrototypeCellMedium.Water
                    || hasOpenExteriorWaterConnection)
                {
                    return PrototypeCellMedium.Water;
                }

                // 外水との接続がなく、元から乾燥している室は、排水設備がなくてもその状態を保ちます。
                return PrototypeCellMedium.DryAir;
            }
        }

        /// <summary>
        /// 少数セル試作の遷移条件を、状態を書き換えず同じ入力から同じ結果へ変換します。
        /// WORLD-01Aの合意事項を比較するための試作であり、製品用の移動・液体・部屋Systemではありません。
        /// </summary>
        private static class PrototypeTransitionEvaluator
        {
            /// <summary>
            /// 遷移元・先セル、移動方向、境界状態から試作結果を返します。
            /// ロード要求や浸水処理は起動せず、副作用のない比較だけを行います。
            /// </summary>
            /// <param name="source">現在位置に相当する試作セルです。</param>
            /// <param name="destination">移動先に相当する試作セルです。</param>
            /// <param name="traversalKind">同一レイヤー移動か、上下レイヤー移動かを指定します。</param>
            /// <param name="boundaryKind">2セル間にある開口、地面、閉鎖水密境界の候補状態です。</param>
            /// <param name="connectedDryFacilityDrainageState">水側と乾燥側を比べる場合に、乾燥側施設の排水機能が成立しているかを指定します。通常は排水なしとして省略できます。</param>
            /// <returns>許可、準備待ち、遮断、または施設状態更新待ちを区別した結果です。</returns>
            public static PrototypeTransitionResult Evaluate(
                PrototypeCellState source,
                PrototypeCellState destination,
                PrototypeTraversalKind traversalKind,
                PrototypeBoundaryKind boundaryKind,
                PrototypeDrainageState connectedDryFacilityDrainageState =
                    PrototypeDrainageState.Unavailable)
            {
                // 固定3層は論理的に存在するため、未ロードを不存在や通行不可とせず、準備後の再判定を要求します。
                if (source.Availability == PrototypeCellAvailability.Unloaded
                    || destination.Availability == PrototypeCellAvailability.Unloaded)
                {
                    return PrototypeTransitionResult.NeedsPreparation;
                }

                // 固体セルは境界が開いていても内部へ入れないため、境界判定とは別の理由で拒否します。
                if (source.Medium == PrototypeCellMedium.Solid
                    || destination.Medium == PrototypeCellMedium.Solid)
                {
                    return PrototypeTransitionResult.BlockedBySolid;
                }

                // 上下地形から導出した遮断と閉鎖水密扉は、両側の媒体が同じでも通行を遮断します。
                if (boundaryKind == PrototypeBoundaryKind.TerrainDerivedGround
                    || boundaryKind == PrototypeBoundaryKind.ClosedWatertightBarrier)
                {
                    return PrototypeTransitionResult.BlockedByBoundary;
                }

                if (traversalKind == PrototypeTraversalKind.VerticalLayerChange)
                {
                    if (source.X != destination.X || source.Y != destination.Y)
                    {
                        return PrototypeTransitionResult.BlockedByDifferentHorizontalCoordinate;
                    }

                    int layerDistance = (int)destination.Layer - (int)source.Layer;
                    if (layerDistance < -1 || layerDistance > 1 || layerDistance == 0)
                    {
                        return PrototypeTransitionResult.BlockedByNonAdjacentLayer;
                    }

                    // 現行仮仕様の潜水・浮上は両端が開放水域の場合だけなので、乾燥室へ上下移動させません。
                    if (source.Medium != PrototypeCellMedium.Water
                        || destination.Medium != PrototypeCellMedium.Water)
                    {
                        return PrototypeTransitionResult.BlockedByNonWaterVerticalCell;
                    }

                    return PrototypeTransitionResult.Allowed;
                }

                // Horizontalは同一レイヤー内の通常移動だけを表すため、別レイヤーを指定して上下条件を迂回させません。
                if (source.Layer != destination.Layer)
                {
                    return PrototypeTransitionResult.BlockedByDifferentLayerForHorizontalTraversal;
                }

                // 同じ媒体同士なら、開いた同一レイヤー境界を通常移動できます。
                if (source.Medium == destination.Medium)
                {
                    return PrototypeTransitionResult.Allowed;
                }

                // 合意した排水施設が稼働中なら、開口からの流入を相殺して乾燥側へ直接出入りできます。
                if (connectedDryFacilityDrainageState == PrototypeDrainageState.Operational)
                {
                    return PrototypeTransitionResult.Allowed;
                }

                // 排水なしの開口は次の固定Tickで乾燥側を浸水させるため、古い媒体状態のまま移動を確定しません。
                return PrototypeTransitionResult.NeedsFacilityWaterStateUpdate;
            }
        }

        /// <summary>
        /// 地上・海中・深海の開放水域を準備し、隣接層だけ移動できて地上から深海へ飛ばないことを保証します。
        /// </summary>
        [Test]
        public void NaturalWaterColumn_AllowsAdjacentLayersAndRejectsLayerSkip()
        {
            PrototypeCellState surface = ReadyWater(PrototypeWorldLayer.Surface);
            PrototypeCellState underwater = ReadyWater(PrototypeWorldLayer.Underwater);
            PrototypeCellState deepSea = ReadyWater(PrototypeWorldLayer.DeepSea);
            PrototypeCellState displacedUnderwater = ReadyWater(
                PrototypeWorldLayer.Underwater,
                x: 1,
                y: 0);

            PrototypeTransitionResult surfaceToUnderwater = PrototypeTransitionEvaluator.Evaluate(
                surface,
                underwater,
                PrototypeTraversalKind.VerticalLayerChange,
                PrototypeBoundaryKind.Open);
            PrototypeTransitionResult underwaterToDeepSea = PrototypeTransitionEvaluator.Evaluate(
                underwater,
                deepSea,
                PrototypeTraversalKind.VerticalLayerChange,
                PrototypeBoundaryKind.Open);
            PrototypeTransitionResult surfaceToDeepSea = PrototypeTransitionEvaluator.Evaluate(
                surface,
                deepSea,
                PrototypeTraversalKind.VerticalLayerChange,
                PrototypeBoundaryKind.Open);
            PrototypeTransitionResult surfaceToDisplacedUnderwater =
                PrototypeTransitionEvaluator.Evaluate(
                    surface,
                    displacedUnderwater,
                    PrototypeTraversalKind.VerticalLayerChange,
                    PrototypeBoundaryKind.Open);

            Assert.That(surfaceToUnderwater, Is.EqualTo(PrototypeTransitionResult.Allowed));
            Assert.That(underwaterToDeepSea, Is.EqualTo(PrototypeTransitionResult.Allowed));
            Assert.That(
                surfaceToDeepSea,
                Is.EqualTo(PrototypeTransitionResult.BlockedByNonAdjacentLayer));
            Assert.That(
                surfaceToDisplacedUnderwater,
                Is.EqualTo(PrototypeTransitionResult.BlockedByDifferentHorizontalCoordinate));
        }

        /// <summary>
        /// 人工海中の成立過程を状態別に準備し、未ロード、未掘削、乾燥掘削、水の充填、上下開口を区別できることを保証します。
        /// </summary>
        [Test]
        public void ArtificialUnderwaterCell_DistinguishesLoadingExcavationAndFloodingStages()
        {
            PrototypeCellState surface = ReadyWater(PrototypeWorldLayer.Surface);
            PrototypeCellState unloaded = new PrototypeCellState(
                0,
                0,
                PrototypeWorldLayer.Underwater,
                PrototypeCellAvailability.Unloaded,
                PrototypeCellMedium.Water);
            PrototypeCellState excavatedDry = new PrototypeCellState(
                0,
                0,
                PrototypeWorldLayer.Underwater,
                PrototypeCellAvailability.Ready,
                PrototypeCellMedium.DryAir);
            PrototypeCellState unexcavatedSolid = new PrototypeCellState(
                0,
                0,
                PrototypeWorldLayer.Underwater,
                PrototypeCellAvailability.Ready,
                PrototypeCellMedium.Solid);
            PrototypeCellState flooded = ReadyWater(PrototypeWorldLayer.Underwater);
            PrototypeBoundaryKind blockedByUpperGround =
                PrototypeVerticalBoundaryEvaluator.Resolve(
                    hasUpperCellGround: true,
                    hasLowerCellCeiling: false);
            PrototypeBoundaryKind blockedByLowerCeiling =
                PrototypeVerticalBoundaryEvaluator.Resolve(
                    hasUpperCellGround: false,
                    hasLowerCellCeiling: true);
            PrototypeBoundaryKind openedByTerrainChanges =
                PrototypeVerticalBoundaryEvaluator.Resolve(
                    hasUpperCellGround: false,
                    hasLowerCellCeiling: false);

            Assert.That(
                EvaluateVertical(surface, unloaded, openedByTerrainChanges),
                Is.EqualTo(PrototypeTransitionResult.NeedsPreparation));
            Assert.That(
                EvaluateVertical(surface, excavatedDry, openedByTerrainChanges),
                Is.EqualTo(PrototypeTransitionResult.BlockedByNonWaterVerticalCell));
            Assert.That(
                EvaluateVertical(surface, unexcavatedSolid, openedByTerrainChanges),
                Is.EqualTo(PrototypeTransitionResult.BlockedBySolid));
            Assert.That(
                EvaluateVertical(surface, flooded, blockedByUpperGround),
                Is.EqualTo(PrototypeTransitionResult.BlockedByBoundary));
            Assert.That(
                EvaluateVertical(surface, flooded, blockedByLowerCeiling),
                Is.EqualTo(PrototypeTransitionResult.BlockedByBoundary));
            Assert.That(
                EvaluateVertical(surface, flooded, openedByTerrainChanges),
                Is.EqualTo(PrototypeTransitionResult.Allowed));
        }

        /// <summary>
        /// 浸水済み施設と外水への開口を準備し、密閉だけでは排水されず、排水施設の成立後は開口中も乾燥を維持できることを保証します。
        /// </summary>
        [Test]
        public void DrainageFacility_ClearsWaterAtFixedTickAndKeepsOpenDoorDry()
        {
            PrototypeCellState underwater = ReadyWater(PrototypeWorldLayer.Underwater);
            PrototypeCellMedium sealedWithoutDrainage =
                PrototypeFacilityWaterEvaluator.ResolveAtFixedTick(
                    PrototypeCellMedium.Water,
                    hasOpenExteriorWaterConnection: false,
                    drainageState: PrototypeDrainageState.Unavailable);
            PrototypeCellMedium drainedWhenFacilityBecomesOperational =
                PrototypeFacilityWaterEvaluator.ResolveAtFixedTick(
                    sealedWithoutDrainage,
                    hasOpenExteriorWaterConnection: false,
                    drainageState: PrototypeDrainageState.Operational);
            PrototypeCellMedium maintainedWhileDoorIsOpen =
                PrototypeFacilityWaterEvaluator.ResolveAtFixedTick(
                    drainedWhenFacilityBecomesOperational,
                    hasOpenExteriorWaterConnection: true,
                    drainageState: PrototypeDrainageState.Operational);
            PrototypeCellMedium floodedAfterDrainageLoss =
                PrototypeFacilityWaterEvaluator.ResolveAtFixedTick(
                    maintainedWhileDoorIsOpen,
                    hasOpenExteriorWaterConnection: true,
                    drainageState: PrototypeDrainageState.Unavailable);
            PrototypeCellState dryRoom = new PrototypeCellState(
                1,
                0,
                PrototypeWorldLayer.Underwater,
                PrototypeCellAvailability.Ready,
                maintainedWhileDoorIsOpen);

            PrototypeTransitionResult closed = PrototypeTransitionEvaluator.Evaluate(
                underwater,
                dryRoom,
                PrototypeTraversalKind.Horizontal,
                PrototypeBoundaryKind.ClosedWatertightBarrier);
            PrototypeTransitionResult directlyOpen = PrototypeTransitionEvaluator.Evaluate(
                underwater,
                dryRoom,
                PrototypeTraversalKind.Horizontal,
                PrototypeBoundaryKind.Open,
                PrototypeDrainageState.Operational);
            PrototypeTransitionResult openWithoutDrainageBeforeEnvironmentUpdate =
                PrototypeTransitionEvaluator.Evaluate(
                    underwater,
                    dryRoom,
                    PrototypeTraversalKind.Horizontal,
                    PrototypeBoundaryKind.Open,
                    PrototypeDrainageState.Unavailable);

            Assert.That(sealedWithoutDrainage, Is.EqualTo(PrototypeCellMedium.Water));
            Assert.That(
                drainedWhenFacilityBecomesOperational,
                Is.EqualTo(PrototypeCellMedium.DryAir));
            Assert.That(maintainedWhileDoorIsOpen, Is.EqualTo(PrototypeCellMedium.DryAir));
            Assert.That(floodedAfterDrainageLoss, Is.EqualTo(PrototypeCellMedium.Water));
            Assert.That(closed, Is.EqualTo(PrototypeTransitionResult.BlockedByBoundary));
            Assert.That(directlyOpen, Is.EqualTo(PrototypeTransitionResult.Allowed));
            Assert.That(
                openWithoutDrainageBeforeEnvironmentUpdate,
                Is.EqualTo(PrototypeTransitionResult.NeedsFacilityWaterStateUpdate));
        }

        /// <summary>
        /// 同一レイヤーで同じ媒体を持つ隣接セルを準備し、上下移動専用の水域条件を通常移動へ誤適用しないことを保証します。
        /// </summary>
        [Test]
        public void SameLayerMatchingMedium_AllowsOrdinaryMovement()
        {
            PrototypeCellState firstDryCell = new PrototypeCellState(
                0,
                0,
                PrototypeWorldLayer.Underwater,
                PrototypeCellAvailability.Ready,
                PrototypeCellMedium.DryAir);
            PrototypeCellState secondDryCell = new PrototypeCellState(
                1,
                0,
                PrototypeWorldLayer.Underwater,
                PrototypeCellAvailability.Ready,
                PrototypeCellMedium.DryAir);

            PrototypeTransitionResult actual = PrototypeTransitionEvaluator.Evaluate(
                firstDryCell,
                secondDryCell,
                PrototypeTraversalKind.Horizontal,
                PrototypeBoundaryKind.Open);

            Assert.That(actual, Is.EqualTo(PrototypeTransitionResult.Allowed));
        }

        /// <summary>
        /// 異なるレイヤーの同一媒体セルを水平移動として渡し、潜水・浮上の条件を迂回できないことを保証します。
        /// </summary>
        [Test]
        public void HorizontalTraversal_RejectsDifferentLayers()
        {
            PrototypeCellState surface = ReadyWater(PrototypeWorldLayer.Surface);
            PrototypeCellState underwater = ReadyWater(PrototypeWorldLayer.Underwater);

            PrototypeTransitionResult actual = PrototypeTransitionEvaluator.Evaluate(
                surface,
                underwater,
                PrototypeTraversalKind.Horizontal,
                PrototypeBoundaryKind.Open);

            Assert.That(
                actual,
                Is.EqualTo(
                    PrototypeTransitionResult.BlockedByDifferentLayerForHorizontalTraversal));
        }

        /// <summary>
        /// 指定レイヤーにある、ロード済みの開放水域セルを作成します。
        /// テストの準備コードで同じ3値を繰り返さず、比較対象のレイヤーを読み取りやすくするために使います。
        /// </summary>
        /// <param name="layer">水域セルを作る試作レイヤーです。</param>
        /// <param name="x">水域セルの絶対X座標です。省略時は0です。</param>
        /// <param name="y">水域セルの絶対Y座標です。省略時は0です。</param>
        /// <returns>判定可能で媒体がWaterの試作セルです。</returns>
        private static PrototypeCellState ReadyWater(
            PrototypeWorldLayer layer,
            int x = 0,
            int y = 0)
        {
            return new PrototypeCellState(
                x,
                y,
                layer,
                PrototypeCellAvailability.Ready,
                PrototypeCellMedium.Water);
        }

        /// <summary>
        /// 同じX/Yにある2セルの上下遷移を評価します。
        /// 人工造成テストで移動方向を毎回書かず、変化させる状態と境界を明確にするために使います。
        /// </summary>
        /// <param name="source">現在位置に相当する試作セルです。</param>
        /// <param name="destination">直上または直下にある試作セルです。</param>
        /// <param name="boundaryKind">上下セル間の開口または遮断状態です。</param>
        /// <returns>上下レイヤー移動として評価した試作結果です。</returns>
        private static PrototypeTransitionResult EvaluateVertical(
            PrototypeCellState source,
            PrototypeCellState destination,
            PrototypeBoundaryKind boundaryKind)
        {
            return PrototypeTransitionEvaluator.Evaluate(
                source,
                destination,
                PrototypeTraversalKind.VerticalLayerChange,
                boundaryKind);
        }
    }
}
