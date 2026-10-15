// このファイルは、連続ノイズを歪ませて不規則な岩壁地形をワールド座標だけから生成します。
// DeterministicChunkGeneratorは水域を先に判定してから本クラスを使い、開発者用表示も同じ生値・平滑化・通路削り規則を参照します。

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 岩壁生成の途中値を、開発者用HUDと軽量テストへ渡す不変データです。
    /// ゲームルールはFinalCandidateだけを使用し、表示確認では生の密度場や通路削りの影響も確認できます。
    /// </summary>
    public readonly struct RockWallGenerationDebugInfo
    {
        /// <summary>座標ワープ後に粗密ノイズを合成した0〜65535の密度値です。</summary>
        public readonly ushort Density;

        /// <summary>局所平滑化前の密度が閾値以上ならtrueです。</summary>
        public readonly bool RawCandidate;

        /// <summary>長く連続する通路用ノイズ帯に含まれ、岩壁から削られる場合はtrueです。</summary>
        public readonly bool Corridor;

        /// <summary>局所平滑化と通路削りを適用した最終岩壁候補ならtrueです。</summary>
        public readonly bool FinalCandidate;

        /// <summary>
        /// 指定座標で計算済みの密度と各生成段階をまとめます。
        /// </summary>
        /// <param name="density">合成済みの0〜65535密度値です。</param>
        /// <param name="rawCandidate">平滑化前の候補状態です。</param>
        /// <param name="corridor">通路として削る座標ならtrueです。</param>
        /// <param name="finalCandidate">全規則適用後の候補状態です。</param>
        public RockWallGenerationDebugInfo(
            ushort density,
            bool rawCandidate,
            bool corridor,
            bool finalCandidate)
        {
            Density = density;
            RawCandidate = rawCandidate;
            Corridor = corridor;
            FinalCandidate = finalCandidate;
        }
    }

    /// <summary>
    /// 複数解像度の連続ノイズ、座標ワープ、境界平滑化、曲がった通路削りを組み合わせて岩壁を生成します。
    /// 固定の楕円や長方形を置かないため、同じ輪郭の繰り返しを避けつつチャンク境界でも連続します。
    /// </summary>
    public static class DeterministicRockWallGenerator
    {
        /// <summary>密度閾値の近傍だけ4方向を調べる幅です。明白な内外で追加ノイズ計算を避けます。</summary>
        private const int SmoothingBand = 1400;

        /// <summary>中心を含む5点のうち、岩壁側へ残すために必要な最小候補数です。</summary>
        private const int RequiredCrossCandidateCount = 3;

        /// <summary>
        /// 指定座標が局所平滑化と通路削り後の岩壁候補に含まれるか判定します。
        /// 水域による抑制は呼び出し側が担当するため、本メソッドは陸上へ置く候補形状だけを返します。
        /// </summary>
        /// <param name="settings">ワールドシードと岩壁ノイズ設定です。</param>
        /// <param name="worldTileX">符号付きワールド水平タイル座標です。</param>
        /// <param name="worldTileY">符号付きワールド垂直タイル座標です。</param>
        /// <returns>最終岩壁候補ならtrueです。</returns>
        public static bool IsRockWallCandidate(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            return GetDebugInfo(settings, worldTileX, worldTileY).FinalCandidate;
        }

        /// <summary>
        /// 指定座標について、生の密度、平滑化前候補、通路、最終候補を一度に求めます。
        /// 開発者用可視化が生成アルゴリズムと同じ値を表示できるようにする診断用入口です。
        /// </summary>
        /// <param name="settings">ワールドシードと岩壁ノイズ設定です。</param>
        /// <param name="worldTileX">符号付きワールド水平タイル座標です。</param>
        /// <param name="worldTileY">符号付きワールド垂直タイル座標です。</param>
        /// <returns>岩壁生成の各段階をまとめた値です。</returns>
        public static RockWallGenerationDebugInfo GetDebugInfo(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            ushort density = SampleDensity(settings, worldTileX, worldTileY);
            int threshold = settings.RockWallNoiseThreshold;
            bool rawCandidate = density >= threshold;
            bool smoothedCandidate;

            // 閾値から十分離れた座標は結論が明白です。輪郭付近だけ5点多数決を行い、生成Jobの負荷を抑えます。
            if (density >= threshold + SmoothingBand)
            {
                smoothedCandidate = true;
            }
            else if (density < threshold - SmoothingBand)
            {
                smoothedCandidate = false;
            }
            else
            {
                int candidateCount = rawCandidate ? 1 : 0;
                candidateCount += IsDensityAboveThreshold(settings, worldTileX + 1, worldTileY) ? 1 : 0;
                candidateCount += IsDensityAboveThreshold(settings, worldTileX - 1, worldTileY) ? 1 : 0;
                candidateCount += IsDensityAboveThreshold(settings, worldTileX, worldTileY + 1) ? 1 : 0;
                candidateCount += IsDensityAboveThreshold(settings, worldTileX, worldTileY - 1) ? 1 : 0;
                smoothedCandidate = candidateCount >= RequiredCrossCandidateCount;
            }

            bool corridor = IsCorridor(settings, worldTileX, worldTileY);
            return new RockWallGenerationDebugInfo(
                density,
                rawCandidate,
                corridor,
                smoothedCandidate && !corridor);
        }

        /// <summary>
        /// 低周波の座標ワープ後に広域形状と細部ノイズを合成し、岩壁の連続密度を返します。
        /// </summary>
        /// <param name="settings">密度・細部・ワープの調整値です。</param>
        /// <param name="worldTileX">サンプリングする水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">サンプリングする垂直ワールドタイル座標です。</param>
        /// <returns>0〜65535の合成密度です。</returns>
        public static ushort SampleDensity(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            int massCellSize = settings.RockWallMassCellSizeInTiles > 0
                ? settings.RockWallMassCellSizeInTiles
                : 24;
            int detailCellSize = settings.RockWallDetailCellSizeInTiles > 0
                ? settings.RockWallDetailCellSizeInTiles
                : 8;
            int warpCellSize = massCellSize * 2;
            int warpAmplitude = settings.RockWallWarpAmplitudeInTiles > 0
                ? settings.RockWallWarpAmplitudeInTiles
                : 0;

            ushort warpXSample = DeterministicNoise.SampleValueNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                warpCellSize,
                WorldGenerationPurposeSalts.RockWallWarpX);
            ushort warpYSample = DeterministicNoise.SampleValueNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                warpCellSize,
                WorldGenerationPurposeSalts.RockWallWarpY);

            // ushort中心値を符号付きオフセットへ写し、最大振幅を超えない整数ワープ座標を作ります。
            int warpedX = worldTileX + (((int)warpXSample - 32768) * warpAmplitude / 32768);
            int warpedY = worldTileY + (((int)warpYSample - 32768) * warpAmplitude / 32768);
            ushort mass = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                warpedX,
                warpedY,
                massCellSize,
                3,
                WorldGenerationPurposeSalts.RockWallMass);
            ushort detail = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                warpedX,
                warpedY,
                detailCellSize,
                2,
                WorldGenerationPurposeSalts.RockWallDetail);

            // 広域形状をやや強く残しつつ細部を約4割混ぜ、Mindustry風の凹凸や枝を増やします。
            return (ushort)(((mass * 5) + (detail * 3)) / 8);
        }

        /// <summary>
        /// 別の低周波ノイズの中心等高線を細い帯として使い、岩壁群を横切る曲がった通路を作ります。
        /// </summary>
        /// <param name="settings">通路幅と岩壁の基準スケールを含む設定です。</param>
        /// <param name="worldTileX">サンプリングする水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">サンプリングする垂直ワールドタイル座標です。</param>
        /// <returns>岩壁から削る通路帯ならtrueです。</returns>
        public static bool IsCorridor(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            if (settings.RockWallCorridorHalfWidth == 0)
            {
                return false;
            }

            int massCellSize = settings.RockWallMassCellSizeInTiles > 0
                ? settings.RockWallMassCellSizeInTiles
                : 24;
            ushort corridorSample = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                massCellSize * 3,
                2,
                WorldGenerationPurposeSalts.RockWallCorridor);
            int distanceFromCenterLine = corridorSample >= 32768
                ? corridorSample - 32768
                : 32768 - corridorSample;
            return distanceFromCenterLine <= settings.RockWallCorridorHalfWidth;
        }

        /// <summary>
        /// 指定座標の密度だけを閾値と比較し、境界多数決の重複処理を短く保ちます。
        /// </summary>
        /// <param name="settings">岩壁密度設定です。</param>
        /// <param name="worldTileX">水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">垂直ワールドタイル座標です。</param>
        /// <returns>生の密度が閾値以上ならtrueです。</returns>
        private static bool IsDensityAboveThreshold(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            return SampleDensity(settings, worldTileX, worldTileY) >= settings.RockWallNoiseThreshold;
        }
    }
}
