// このファイルは、複数解像度のノイズと座標ワープを使い、火山バイオーム内の不規則なマグマ形状を生成します。
// DeterministicChunkGeneratorはバイオームと火山適性を判定した後に本クラスを呼び、開発者表示とテストも同じ密度・平滑化規則を参照します。

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 広域密度、細部密度、座標ワープ、境界平滑化を組み合わせ、Mindustry風の入り江・半島・島を持つマグマ形状を生成します。
    /// 岩地の希少なマグマは別プロファイルを維持するため、本クラスは火山バイオーム専用です。
    /// </summary>
    public static class DeterministicMagmaGenerator
    {
        /// <summary>密度閾値の近傍だけ4方向を調べる幅です。明白な内外で追加ノイズ計算を避けます。</summary>
        private const int SmoothingBand = 1200;

        /// <summary>中心を含む5点のうち、マグマ側へ残すために必要な最小候補数です。</summary>
        private const int RequiredCrossCandidateCount = 3;

        /// <summary>
        /// 低周波の座標ワープ後に広域形状と細部ノイズを合成し、火山マグマの連続密度を返します。
        /// ワールド座標だけを入力にするため、チャンク生成順や隣接チャンクのロード状態には依存しません。
        /// </summary>
        /// <param name="settings">ワールドシードとマグマ形状の調整値です。</param>
        /// <param name="worldTileX">サンプリングする水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">サンプリングする垂直ワールドタイル座標です。</param>
        /// <returns>0〜65535の合成マグマ密度です。</returns>
        public static ushort SampleDensity(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            int massCellSize = settings.MagmaCellSizeInTiles > 0
                ? settings.MagmaCellSizeInTiles
                : 24;
            int detailCellSize = settings.MagmaDetailCellSizeInTiles > 0
                ? settings.MagmaDetailCellSizeInTiles
                : 8;
            int warpCellSize = massCellSize * 2;
            int warpAmplitude = settings.MagmaWarpAmplitudeInTiles > 0
                ? settings.MagmaWarpAmplitudeInTiles
                : 0;

            ushort warpXSample = DeterministicNoise.SampleValueNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                warpCellSize,
                WorldGenerationPurposeSalts.MagmaWarpX);
            ushort warpYSample = DeterministicNoise.SampleValueNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                worldTileX,
                worldTileY,
                warpCellSize,
                WorldGenerationPurposeSalts.MagmaWarpY);

            // ushortの中心値を符号付きオフセットへ変換し、最大振幅を超えない整数座標ワープを作ります。
            int warpedX = worldTileX + (((int)warpXSample - 32768) * warpAmplitude / 32768);
            int warpedY = worldTileY + (((int)warpYSample - 32768) * warpAmplitude / 32768);
            ushort mass = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                warpedX,
                warpedY,
                massCellSize,
                settings.MagmaOctaveCount,
                WorldGenerationPurposeSalts.Magma);
            ushort detail = DeterministicNoise.SampleFractalNoise(
                settings.WorldSeed,
                settings.GeneratorVersion,
                warpedX,
                warpedY,
                detailCellSize,
                2,
                WorldGenerationPurposeSalts.MagmaDetail);

            // 広いマグマ海を維持する大域形状を5、細かな入り江や半島を作る細部を3の比率で合成します。
            return (ushort)(((mass * 5) + (detail * 3)) / 8);
        }

        /// <summary>
        /// 中心密度を指定閾値と比較し、輪郭付近だけ十字5点の多数決で1タイルの穴や突起を抑えます。
        /// マグマ本体と、それより広い淡水抑制域の両方が同じ輪郭処理を共有するために使用します。
        /// </summary>
        /// <param name="settings">ワールドシードとマグマ形状の調整値です。</param>
        /// <param name="worldTileX">判定する水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">判定する垂直ワールドタイル座標です。</param>
        /// <param name="threshold">候補とする0〜65535の密度下限です。</param>
        /// <param name="centerDensity">呼び出し側で計算済みの中心密度です。</param>
        /// <returns>境界平滑化後も指定領域に含まれる場合はtrueです。</returns>
        public static bool IsSmoothedCandidate(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            ushort threshold,
            ushort centerDensity)
        {
            bool rawCandidate = centerDensity >= threshold;

            // 閾値から十分離れた座標は結論が明白なので、多数決に必要な追加密度を計算しません。
            if (centerDensity >= threshold + SmoothingBand)
            {
                return true;
            }

            if (centerDensity < threshold - SmoothingBand)
            {
                return false;
            }

            int candidateCount = rawCandidate ? 1 : 0;
            candidateCount += IsDensityAboveThreshold(settings, worldTileX + 1, worldTileY, threshold) ? 1 : 0;
            candidateCount += IsDensityAboveThreshold(settings, worldTileX - 1, worldTileY, threshold) ? 1 : 0;
            candidateCount += IsDensityAboveThreshold(settings, worldTileX, worldTileY + 1, threshold) ? 1 : 0;
            candidateCount += IsDensityAboveThreshold(settings, worldTileX, worldTileY - 1, threshold) ? 1 : 0;
            return candidateCount >= RequiredCrossCandidateCount;
        }

        /// <summary>
        /// 指定座標の合成密度だけを閾値と比較し、境界多数決の重複処理を短く保ちます。
        /// </summary>
        /// <param name="settings">ワールドシードとマグマ形状の調整値です。</param>
        /// <param name="worldTileX">判定する水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">判定する垂直ワールドタイル座標です。</param>
        /// <param name="threshold">候補とする0〜65535の密度下限です。</param>
        /// <returns>生の合成密度が閾値以上ならtrueです。</returns>
        private static bool IsDensityAboveThreshold(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY,
            ushort threshold)
        {
            return SampleDensity(settings, worldTileX, worldTileY) >= threshold;
        }
    }
}
