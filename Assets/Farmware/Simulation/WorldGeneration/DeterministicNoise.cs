// このファイルは、整数演算だけで連続する2D値ノイズとフラクタルノイズを計算します。
// UnityやOSごとの浮動小数点実装差を生成結果へ持ち込まず、隣接チャンクを未生成のまま境界を連続させます。

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// ワールドタイル座標から、0〜65535の決定論的な連続ノイズを返します。
    /// 地形の標高、気温、湿度をチャンク境界に依存せず求めるために使用します。
    /// </summary>
    public static class DeterministicNoise
    {
        // 補間係数の1.0を表す固定小数点値です。65536を1とするQ16形式を使用します。
        private const long FixedOne = 65536L;

        /// <summary>
        /// 指定した格子間隔の2D値ノイズを整数補間で求めます。
        /// </summary>
        /// <param name="worldSeed">ワールドの安定64bitシードです。</param>
        /// <param name="generatorVersion">生成器バージョンです。</param>
        /// <param name="worldTileX">サンプリングするワールド水平タイル座標です。</param>
        /// <param name="worldTileY">サンプリングするワールド垂直タイル座標です。</param>
        /// <param name="cellSizeInTiles">ノイズ格子1辺のタイル数です。大きいほど変化が緩やかになります。</param>
        /// <param name="purposeSalt">標高、気温、湿度など用途専用のソルトです。</param>
        /// <returns>0〜65535の補間済みノイズ値です。</returns>
        public static ushort SampleValueNoise(
            ulong worldSeed,
            int generatorVersion,
            int worldTileX,
            int worldTileY,
            int cellSizeInTiles,
            ulong purposeSalt)
        {
            // 不正な設定値でも0除算でJob全体を壊さないよう、最小1タイルへ丸めます。
            int safeCellSize = cellSizeInTiles > 0 ? cellSizeInTiles : 1;
            int gridX = WorldGrid.FloorDivide(worldTileX, safeCellSize);
            int gridY = WorldGrid.FloorDivide(worldTileY, safeCellSize);
            int localX = worldTileX - (gridX * safeCellSize);
            int localY = worldTileY - (gridY * safeCellSize);

            long smoothX = SmoothStep(ToFixedFraction(localX, safeCellSize));
            long smoothY = SmoothStep(ToFixedFraction(localY, safeCellSize));

            int bottomLeft = SampleGridPoint(worldSeed, generatorVersion, gridX, gridY, purposeSalt);
            int bottomRight = SampleGridPoint(worldSeed, generatorVersion, gridX + 1, gridY, purposeSalt);
            int topLeft = SampleGridPoint(worldSeed, generatorVersion, gridX, gridY + 1, purposeSalt);
            int topRight = SampleGridPoint(worldSeed, generatorVersion, gridX + 1, gridY + 1, purposeSalt);

            // まずX方向、次にY方向へ補間すると、4つの格子値から連続した面を得られます。
            int bottom = Lerp(bottomLeft, bottomRight, smoothX);
            int top = Lerp(topLeft, topRight, smoothX);
            return (ushort)Lerp(bottom, top, smoothY);
        }

        /// <summary>
        /// 複数解像度の値ノイズを重ね、広域形状と細部を両立したフラクタルノイズを求めます。
        /// 各オクターブの重みは前段の半分とし、呼び出し順に依存しない別候補番号でサンプリングします。
        /// </summary>
        /// <param name="worldSeed">ワールドの安定64bitシードです。</param>
        /// <param name="generatorVersion">生成器バージョンです。</param>
        /// <param name="worldTileX">サンプリングするワールド水平タイル座標です。</param>
        /// <param name="worldTileY">サンプリングするワールド垂直タイル座標です。</param>
        /// <param name="baseCellSizeInTiles">最も低周波な格子1辺のタイル数です。</param>
        /// <param name="octaveCount">重ねる解像度の数です。1以上を指定します。</param>
        /// <param name="purposeSalt">用途専用のソルトです。</param>
        /// <returns>重み付き平均後の0〜65535のノイズ値です。</returns>
        public static ushort SampleFractalNoise(
            ulong worldSeed,
            int generatorVersion,
            int worldTileX,
            int worldTileY,
            int baseCellSizeInTiles,
            int octaveCount,
            ulong purposeSalt)
        {
            ulong weightedTotal = 0;
            ulong totalWeight = 0;
            uint weight = 65536;
            int cellSize = baseCellSizeInTiles > 0 ? baseCellSizeInTiles : 1;
            int safeOctaveCount = octaveCount > 0 ? octaveCount : 1;

            // 高周波になるほど振幅を半分にし、広い地形のまとまりを細部より強く残します。
            for (int octave = 0; octave < safeOctaveCount; octave++)
            {
                ulong octaveSalt = WorldSeedUtility.Mix64(purposeSalt ^ (uint)octave);
                ushort sample = SampleValueNoise(
                    worldSeed,
                    generatorVersion,
                    worldTileX,
                    worldTileY,
                    cellSize,
                    octaveSalt);

                weightedTotal += (ulong)sample * weight;
                totalWeight += weight;
                weight = weight > 1 ? weight >> 1 : 1;
                cellSize = cellSize > 1 ? cellSize >> 1 : 1;
            }

            return (ushort)(weightedTotal / totalWeight);
        }

        /// <summary>
        /// 1つのノイズ格子点に割り当てる0〜65535の値を座標ハッシュから求めます。
        /// </summary>
        /// <param name="worldSeed">ワールドシードです。</param>
        /// <param name="generatorVersion">生成器バージョンです。</param>
        /// <param name="gridX">ノイズ格子の水平座標です。</param>
        /// <param name="gridY">ノイズ格子の垂直座標です。</param>
        /// <param name="purposeSalt">用途とオクターブを分離するソルトです。</param>
        /// <returns>格子点の0〜65535の値です。</returns>
        private static int SampleGridPoint(
            ulong worldSeed,
            int generatorVersion,
            int gridX,
            int gridY,
            ulong purposeSalt)
        {
            ulong hash = WorldSeedUtility.HashCoordinate(
                worldSeed,
                generatorVersion,
                gridX,
                gridY,
                purposeSalt);
            return WorldSeedUtility.ToUnitUShort(hash);
        }

        /// <summary>
        /// 格子内の整数位置をQ16固定小数点の0.0以上1.0未満へ変換します。
        /// </summary>
        /// <param name="localCoordinate">格子内の0始まり位置です。</param>
        /// <param name="cellSizeInTiles">格子1辺のタイル数です。</param>
        /// <returns>65536を1.0とする固定小数点値です。</returns>
        private static long ToFixedFraction(int localCoordinate, int cellSizeInTiles)
        {
            return ((long)localCoordinate * FixedOne) / cellSizeInTiles;
        }

        /// <summary>
        /// 線形補間の角を滑らかにする3次SmoothStepをQ16整数演算で計算します。
        /// 格子境界の傾きを0に近づけ、チャンクをまたぐ見た目の折れを減らします。
        /// </summary>
        /// <param name="value">0〜65535のQ16補間係数です。</param>
        /// <returns>SmoothStep適用後のQ16補間係数です。</returns>
        private static long SmoothStep(long value)
        {
            return (value * value * ((3 * FixedOne) - (2 * value))) / (FixedOne * FixedOne);
        }

        /// <summary>
        /// 2つの整数値をQ16係数で線形補間します。
        /// </summary>
        /// <param name="from">係数0のときに返す値です。</param>
        /// <param name="to">係数1に近づくほど近づく値です。</param>
        /// <param name="fixedAmount">65536を1.0とする補間係数です。</param>
        /// <returns>補間後の整数値です。</returns>
        private static int Lerp(int from, int to, long fixedAmount)
        {
            return from + (int)(((long)(to - from) * fixedAmount) / FixedOne);
        }
    }
}
