// このファイルは、恒久淡水の河川・湖から周辺低地へ自然に冠水しやすい度合いを決定論的に計算します。
// C4-D1では純粋計算と水源選択を確定し、C4-D2のECS Jobが同じAPIへチャンク境界を含む固定表流水を渡します。

using Unity.Collections;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 自然冠水の探索範囲、整数式、湿原候補の最低規模を固定する設定です。
    /// WorldGenerationSettingsへ値として含め、GeneratorVersionと一緒に保存対象とします。
    /// </summary>
    public struct NaturalFloodingGenerationSettings
    {
        /// <summary>各タイルから恒久淡水源を探す半径です。単位はタイルで、設定作成時に決まりワールド中は不変です。</summary>
        public byte SearchRadiusInTiles;

        /// <summary>相対的な低さを0へ下げ切る水面との比高上限です。固定標高値の単位で、設定作成時に決まりワールド中は不変です。</summary>
        public ushort MaximumHeightAboveWater;

        /// <summary>渓流・河川の流出寄与セル相当値を水域規模へ変換する倍率です。設定作成時に決まりワールド中は不変です。</summary>
        public ushort RunoffMagnitudeMultiplier;

        /// <summary>水域への近さへ掛ける整数比重です。設定作成時に決まりワールド中は不変です。</summary>
        public byte WaterProximityWeight;

        /// <summary>水面に対する相対的な低さへ掛ける整数比重です。設定作成時に決まりワールド中は不変です。</summary>
        public byte RelativeLowlandWeight;

        /// <summary>地形の平坦さへ掛ける整数比重です。設定作成時に決まりワールド中は不変です。</summary>
        public byte FlatnessWeight;

        /// <summary>恒久淡水源の流出量または湖面充填率へ掛ける整数比重です。設定作成時に決まりワールド中は不変です。</summary>
        public byte SourceMagnitudeWeight;

        /// <summary>距離・比高・平坦さ・水域規模の比重を合計した除数です。設定作成時に決まりワールド中は不変です。</summary>
        public byte WeightTotal;

        /// <summary>自然冠水条件を満たすNaturalFloodingPotentialの下限です。設定作成時に決まりワールド中は不変です。</summary>
        public ushort NaturalFloodingThreshold;

        /// <summary>広域湿原候補へ含める連続標高の下限です。設定作成時に決まりワールド中は不変です。</summary>
        public ushort WetlandMinimumElevation;

        /// <summary>広域湿原候補へ含める連続標高の上限です。設定作成時に決まりワールド中は不変です。</summary>
        public ushort WetlandMaximumElevation;

        /// <summary>一つの8×8水文セルを湿原規模候補にする適格タイル数の下限です。設定作成時に決まりワールド中は不変です。</summary>
        public byte MinimumEligibleTilesPerHydrologyCell;

        /// <summary>広域湿原として認める上下左右連結の水文セル数の下限です。設定作成時に決まりワールド中は不変です。</summary>
        public byte MinimumConnectedHydrologyCells;

        /// <summary>TERRAIN-01C4-Dで確定した自然冠水と湿原最低規模の設定を作ります。</summary>
        /// <returns>半径32、比重2：2：1：1、閾値39,321、16タイル・4セルの確定設定です。</returns>
        public static NaturalFloodingGenerationSettings CreateDefault()
        {
            return new NaturalFloodingGenerationSettings
            {
                SearchRadiusInTiles = 32,
                MaximumHeightAboveWater = 4096,
                RunoffMagnitudeMultiplier = 512,
                WaterProximityWeight = 2,
                RelativeLowlandWeight = 2,
                FlatnessWeight = 1,
                SourceMagnitudeWeight = 1,
                WeightTotal = 6,
                NaturalFloodingThreshold = 39321,
                WetlandMinimumElevation = 20000,
                WetlandMaximumElevation = 36000,
                MinimumEligibleTilesPerHydrologyCell = 16,
                MinimumConnectedHydrologyCells = 4
            };
        }
    }

    /// <summary>
    /// 一つの対象タイルに対して選ばれた恒久淡水源です。
    /// 距離、規模、絶対座標の順で候補を一意にし、走査順やJob順を生成結果へ持ち込みません。
    /// </summary>
    public struct NaturalFloodingSource
    {
        /// <summary>適格な恒久淡水源が見つかった場合は1、候補がない場合は0です。探索が設定し、一回の計算結果中は不変です。</summary>
        public byte IsValid;

        /// <summary>選択した渓流、河川、恒久淡水湖の固定表流水種別です。探索が設定し、一回の計算結果中は不変です。</summary>
        public SurfaceWaterTileType Type;

        /// <summary>水源タイルの水平絶対タイル座標です。探索が設定し、一回の計算結果中は不変です。</summary>
        public int WorldTileX;

        /// <summary>水源タイルの垂直絶対タイル座標です。探索が設定し、一回の計算結果中は不変です。</summary>
        public int WorldTileY;

        /// <summary>対象から水源までのユークリッド距離を整数平方根で切り捨てた値です。単位はタイルで、探索後は不変です。</summary>
        public byte DistanceInTiles;

        /// <summary>河道標高または恒久湖面標高を表す0〜65,535の固定値です。探索が表流水から写し、計算結果中は不変です。</summary>
        public ushort SurfaceHeight;

        /// <summary>河川流出量または湖面充填率から求めた0〜65,535の水域規模です。探索が導出し、計算結果中は不変です。</summary>
        public ushort Magnitude;
    }

    /// <summary>
    /// 自然冠水度と、その算出に使った4入力をまとめた純粋計算結果です。
    /// C4-Eの診断表示は生成規則を再実装せず、この値と選択水源を表示できます。
    /// </summary>
    public struct NaturalFloodingResult
    {
        /// <summary>半径内に適格な恒久淡水源が存在した場合は1、存在しない場合は0です。計算APIが設定し、結果の寿命中は不変です。</summary>
        public byte HasSource;

        /// <summary>距離から求めた0〜65,535の水域近接度です。計算APIが設定し、結果の寿命中は不変です。</summary>
        public ushort WaterProximity;

        /// <summary>対象標高と水面高の差から求めた0〜65,535の相対低地度です。計算APIが設定し、結果の寿命中は不変です。</summary>
        public ushort RelativeLowland;

        /// <summary>地形起伏を反転した0〜65,535の平坦度です。計算APIが設定し、結果の寿命中は不変です。</summary>
        public ushort Flatness;

        /// <summary>選択水源の流出量または湖面充填率から求めた0〜65,535の規模です。計算APIが設定し、結果の寿命中は不変です。</summary>
        public ushort SourceMagnitude;

        /// <summary>4入力を2：2：1：1で整数合成した0〜65,535の自然冠水度です。計算APIが設定し、結果の寿命中は不変です。</summary>
        public ushort Potential;
    }

    /// <summary>
    /// 固定表流水の矩形領域から優先水源を選び、自然冠水度を整数だけで導出する純粋計算です。
    /// Burst Jobから利用できるよう、管理参照、浮動小数点、実行時のNative Collection確保を使用しません。
    /// </summary>
    public static class DeterministicNaturalFloodingGenerator
    {
        /// <summary>設定がC4-Dの確定契約と一致し、暗黙補正なしで使用できるか検証します。</summary>
        /// <param name="settings">検証する自然冠水・湿原規模設定です。</param>
        /// <returns>確定済みの半径、整数式、境界、最低規模と一致する場合はtrueです。</returns>
        public static bool ValidateSettings(in NaturalFloodingGenerationSettings settings)
        {
            return settings.SearchRadiusInTiles == 32
                && settings.MaximumHeightAboveWater == 4096
                && settings.RunoffMagnitudeMultiplier == 512
                && settings.WaterProximityWeight == 2
                && settings.RelativeLowlandWeight == 2
                && settings.FlatnessWeight == 1
                && settings.SourceMagnitudeWeight == 1
                && settings.WeightTotal == 6
                && settings.NaturalFloodingThreshold == 39321
                && settings.WetlandMinimumElevation == 20000
                && settings.WetlandMaximumElevation == 36000
                && settings.MinimumEligibleTilesPerHydrologyCell == 16
                && settings.MinimumConnectedHydrologyCells == 4;
        }

        /// <summary>
        /// 矩形の固定表流水から半径32以内の最優先水源を選びます。
        /// 最短の整数距離、より大きい規模、X、Yの昇順で一意化します。
        /// </summary>
        /// <param name="settings">検索半径と水域規模倍率を持つ固定設定です。</param>
        /// <param name="targetWorldTileX">評価対象の水平絶対タイル座標です。</param>
        /// <param name="targetWorldTileY">評価対象の垂直絶対タイル座標です。</param>
        /// <param name="waterAreaMinimumX">入力矩形の左端絶対タイル座標です。</param>
        /// <param name="waterAreaMinimumY">入力矩形の下端絶対タイル座標です。</param>
        /// <param name="waterAreaWidth">入力矩形の横幅です。単位はタイルです。</param>
        /// <param name="waterAreaHeight">入力矩形の高さです。単位はタイルです。</param>
        /// <param name="surfaceWater">左下から行優先で並ぶ固定表流水です。</param>
        /// <param name="source">成功時に選択した水源、候補なしの場合はIsValidが0の値です。</param>
        /// <returns>設定、矩形、座標演算が有効で、候補全体を評価できた場合はtrueです。</returns>
        public static bool TryFindPreferredSource(
            in NaturalFloodingGenerationSettings settings,
            int targetWorldTileX,
            int targetWorldTileY,
            int waterAreaMinimumX,
            int waterAreaMinimumY,
            int waterAreaWidth,
            int waterAreaHeight,
            NativeArray<GeneratedSurfaceWaterTile> surfaceWater,
            out NaturalFloodingSource source)
        {
            source = default;
            long requiredLength = (long)waterAreaWidth * waterAreaHeight;
            long waterAreaMaximumX = (long)waterAreaMinimumX + waterAreaWidth - 1L;
            long waterAreaMaximumY = (long)waterAreaMinimumY + waterAreaHeight - 1L;
            if (!ValidateSettings(settings)
                || waterAreaWidth <= 0
                || waterAreaHeight <= 0
                || requiredLength <= 0
                || requiredLength > int.MaxValue
                || !surfaceWater.IsCreated
                || surfaceWater.Length < requiredLength
                || waterAreaMaximumX > int.MaxValue
                || waterAreaMaximumY > int.MaxValue)
            {
                return false;
            }

            int radius = settings.SearchRadiusInTiles;
            long searchMinimumX = (long)targetWorldTileX - radius;
            long searchMinimumY = (long)targetWorldTileY - radius;
            long searchMaximumX = (long)targetWorldTileX + radius;
            long searchMaximumY = (long)targetWorldTileY + radius;
            long intersectionMinimumX = Maximum(searchMinimumX, waterAreaMinimumX);
            long intersectionMinimumY = Maximum(searchMinimumY, waterAreaMinimumY);
            long intersectionMaximumX = Minimum(searchMaximumX, waterAreaMaximumX);
            long intersectionMaximumY = Minimum(searchMaximumY, waterAreaMaximumY);
            if (intersectionMinimumX > intersectionMaximumX
                || intersectionMinimumY > intersectionMaximumY)
            {
                // 世界座標のまま交差判定することで、int両端に離れた矩形でもローカル座標への変換前に終了できます。
                return true;
            }

            // 交差範囲は入力矩形の内側と確認済みなので、差は必ず0〜幅・高さ-1に収まり、安全にintへ変換できます。
            int firstLocalX = (int)(intersectionMinimumX - waterAreaMinimumX);
            int firstLocalY = (int)(intersectionMinimumY - waterAreaMinimumY);
            int lastLocalX = (int)(intersectionMaximumX - waterAreaMinimumX);
            int lastLocalY = (int)(intersectionMaximumY - waterAreaMinimumY);

            long radiusSquared = (long)radius * radius;
            for (int localY = firstLocalY; localY <= lastLocalY; localY++)
            {
                for (int localX = firstLocalX; localX <= lastLocalX; localX++)
                {
                    GeneratedSurfaceWaterTile tile = surfaceWater[(localY * waterAreaWidth) + localX];
                    if (!TryGetSourceMagnitude(settings, tile, out ushort magnitude))
                    {
                        continue;
                    }

                    int worldTileX = waterAreaMinimumX + localX;
                    int worldTileY = waterAreaMinimumY + localY;
                    long deltaX = (long)worldTileX - targetWorldTileX;
                    long deltaY = (long)worldTileY - targetWorldTileY;
                    long squaredDistance = (deltaX * deltaX) + (deltaY * deltaY);
                    if (squaredDistance > radiusSquared)
                    {
                        continue;
                    }

                    byte distance = (byte)IntegerSquareRoot((uint)squaredDistance);
                    if (source.IsValid != 0
                        && !IsPreferredCandidate(distance, magnitude, worldTileX, worldTileY, source))
                    {
                        continue;
                    }

                    source = new NaturalFloodingSource
                    {
                        IsValid = 1,
                        Type = tile.Type,
                        WorldTileX = worldTileX,
                        WorldTileY = worldTileY,
                        DistanceInTiles = distance,
                        SurfaceHeight = tile.SurfaceHeight,
                        Magnitude = magnitude
                    };
                }
            }

            return true;
        }

        /// <summary>選択済み水源と対象地形から4入力および自然冠水度を64bit整数で求めます。</summary>
        /// <param name="settings">比重2：2：1：1と合計6を持つ固定設定です。</param>
        /// <param name="targetElevation">評価対象タイルの0〜65,535連続標高です。</param>
        /// <param name="terrainRuggedness">評価対象タイルの0〜65,535地形起伏です。</param>
        /// <param name="source">TryFindPreferredSourceで選択した水源です。</param>
        /// <param name="result">成功時の4入力と0〜65,535自然冠水度です。</param>
        /// <returns>設定が確定契約と一致し、水源値が有効な場合はtrueです。水源なしもPotential 0で成功します。</returns>
        public static bool TryCalculateNaturalFloodingPotential(
            in NaturalFloodingGenerationSettings settings,
            ushort targetElevation,
            ushort terrainRuggedness,
            in NaturalFloodingSource source,
            out NaturalFloodingResult result)
        {
            result = default;
            if (!ValidateSettings(settings))
            {
                return false;
            }

            if (source.IsValid == 0)
            {
                // 水源がない場合は平坦さだけで冠水させず、仕様どおり全入力0の明示結果にします。
                return true;
            }
            if (source.IsValid != 1
                || source.DistanceInTiles > settings.SearchRadiusInTiles
                || !IsPermanentFreshwaterType(source.Type))
            {
                return false;
            }

            long distance = source.DistanceInTiles;
            long waterProximity = ((settings.SearchRadiusInTiles - distance) * ushort.MaxValue)
                / settings.SearchRadiusInTiles;
            long heightAboveWater = targetElevation > source.SurfaceHeight
                ? targetElevation - (long)source.SurfaceHeight
                : 0L;
            long limitedHeight = Minimum(heightAboveWater, settings.MaximumHeightAboveWater);
            long relativeLowland = ushort.MaxValue
                - ((limitedHeight * ushort.MaxValue) / settings.MaximumHeightAboveWater);
            long flatness = ushort.MaxValue - (long)terrainRuggedness;
            long weightedTotal = (waterProximity * settings.WaterProximityWeight)
                + (relativeLowland * settings.RelativeLowlandWeight)
                + (flatness * settings.FlatnessWeight)
                + ((long)source.Magnitude * settings.SourceMagnitudeWeight);

            result = new NaturalFloodingResult
            {
                HasSource = 1,
                WaterProximity = (ushort)waterProximity,
                RelativeLowland = (ushort)relativeLowland,
                Flatness = (ushort)flatness,
                SourceMagnitude = source.Magnitude,
                Potential = (ushort)(weightedTotal / settings.WeightTotal)
            };
            return true;
        }

        /// <summary>固定表流水1タイルが自然冠水源かを確認し、種別に応じた水域規模を返します。</summary>
        /// <param name="settings">河道流出量の固定倍率を持つ設定です。</param>
        /// <param name="tile">評価する固定表流水タイルです。</param>
        /// <param name="magnitude">適格な場合の0〜65,535水域規模です。</param>
        /// <returns>恒久淡水のStream、River、Lakeで、必要な意味フラグを持つ場合はtrueです。</returns>
        public static bool TryGetSourceMagnitude(
            in NaturalFloodingGenerationSettings settings,
            in GeneratedSurfaceWaterTile tile,
            out ushort magnitude)
        {
            magnitude = default;
            SurfaceWaterTileFlags required = SurfaceWaterTileFlags.NaturalSurfaceWater
                | SurfaceWaterTileFlags.FreshwaterConnected;
            if (!ValidateSettings(settings)
                || (tile.Flags & required) != required
                || !IsPermanentFreshwaterType(tile.Type))
            {
                return false;
            }

            if (tile.Type == SurfaceWaterTileType.PermanentFreshwaterLake)
            {
                magnitude = tile.SourceAmount;
                return true;
            }

            long runoffMagnitude = (long)tile.SourceAmount * settings.RunoffMagnitudeMultiplier;
            magnitude = (ushort)Minimum(ushort.MaxValue, runoffMagnitude);
            return true;
        }

        /// <summary>非負整数の平方根を浮動小数点なしで切り捨て計算します。</summary>
        /// <param name="value">平方根を求める非負の32bit整数です。</param>
        /// <returns>二乗がvalue以下となる最大の整数です。</returns>
        public static uint IntegerSquareRoot(uint value)
        {
            // 2bitずつ処理する標準的な整数平方根により、CPUやBurst設定による浮動小数点丸め差を避けます。
            uint remainder = value;
            uint root = 0;
            uint bit = 1u << 30;
            while (bit > remainder)
            {
                bit >>= 2;
            }

            while (bit != 0)
            {
                uint candidate = root + bit;
                if (remainder >= candidate)
                {
                    remainder -= candidate;
                    root = (root >> 1) + bit;
                }
                else
                {
                    root >>= 1;
                }
                bit >>= 2;
            }

            return root;
        }

        /// <summary>仕様で自然冠水源に限定した渓流、河川、恒久淡水湖かを返します。</summary>
        /// <param name="type">確認する固定表流水種別です。</param>
        /// <returns>SpringStream、塩湖、一時湖を含まない恒久淡水源ならtrueです。</returns>
        private static bool IsPermanentFreshwaterType(SurfaceWaterTileType type)
        {
            return type == SurfaceWaterTileType.Stream
                || type == SurfaceWaterTileType.River
                || type == SurfaceWaterTileType.PermanentFreshwaterLake;
        }

        /// <summary>同じ検索円内の候補を距離、規模、X、Yの固定順で比較します。</summary>
        /// <param name="distance">新候補の整数距離です。</param>
        /// <param name="magnitude">新候補の水域規模です。</param>
        /// <param name="worldTileX">新候補の水平絶対座標です。</param>
        /// <param name="worldTileY">新候補の垂直絶対座標です。</param>
        /// <param name="current">現在選択中の候補です。</param>
        /// <returns>新候補へ置き換えるべき場合はtrueです。</returns>
        private static bool IsPreferredCandidate(
            byte distance,
            ushort magnitude,
            int worldTileX,
            int worldTileY,
            in NaturalFloodingSource current)
        {
            if (distance != current.DistanceInTiles)
            {
                return distance < current.DistanceInTiles;
            }
            if (magnitude != current.Magnitude)
            {
                return magnitude > current.Magnitude;
            }
            if (worldTileX != current.WorldTileX)
            {
                return worldTileX < current.WorldTileX;
            }
            return worldTileY < current.WorldTileY;
        }

        /// <summary>二つの64bit整数の小さい方を返し、UnityEngineやLINQへの依存を避けます。</summary>
        /// <param name="first">比較する最初の値です。</param>
        /// <param name="second">比較する二つ目の値です。</param>
        /// <returns>firstとsecondの小さい方です。</returns>
        private static long Minimum(long first, long second)
        {
            return first < second ? first : second;
        }

        /// <summary>二つの64bit整数の大きい方を返し、UnityEngineやLINQへの依存を避けます。</summary>
        /// <param name="first">比較する最初の値です。</param>
        /// <param name="second">比較する二つ目の値です。</param>
        /// <returns>firstとsecondの大きい方です。</returns>
        private static long Maximum(long first, long second)
        {
            return first > second ? first : second;
        }
    }
}
