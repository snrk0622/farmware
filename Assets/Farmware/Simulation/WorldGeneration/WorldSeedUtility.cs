// このファイルは、入力文字列を安定したワールドシードへ変換し、用途と座標ごとの独立した乱数値を作ります。
// 地形・資源・装飾が同じ乱数列を順番に消費しない設計にすることで、機能追加が既存生成結果をずらすことを防ぎます。

using System.Text;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 生成用途ごとに固定した64bitソルトを定義します。
    /// 新しい用途は既存値を変更せず新規追加し、過去バージョンの生成結果を維持します。
    /// </summary>
    public static class WorldGenerationPurposeSalts
    {
        /// <summary>標高ノイズ専用のソルトです。</summary>
        public const ulong Elevation = 0xA0761D6478BD642FUL;

        /// <summary>気温ノイズ専用のソルトです。</summary>
        public const ulong Temperature = 0xE7037ED1A0B428DBUL;

        /// <summary>湿度ノイズ専用のソルトです。</summary>
        public const ulong Moisture = 0x8EBC6AF09C88C6E3UL;

        /// <summary>バイオーム境界で地面種別を段階的に混ぜる座標抽選専用のソルトです。</summary>
        public const ulong BiomeTransition = 0x1D8E4E27C47D124FUL;

        /// <summary>樹木資源の群生ノイズ専用のソルトです。</summary>
        public const ulong TreeResource = 0xD6E8FEB86659FD93UL;

        /// <summary>石資源の群生ノイズ専用のソルトです。</summary>
        public const ulong StoneResource = 0xA5A3564E27F8862BUL;

        /// <summary>水資源の群生ノイズ専用のソルトです。</summary>
        public const ulong WaterResource = 0x9E6C63D0676A9A99UL;

        /// <summary>砂資源の群生ノイズ専用のソルトです。</summary>
        public const ulong SandResource = 0xC2B2AE3D27D4EB4FUL;

        /// <summary>資源スプライトの見た目差分専用のソルトです。</summary>
        public const ulong ResourceVisualVariant = 0x165667B19E3779F9UL;
    }

    /// <summary>
    /// 文字列シードと整数座標から、実行環境に依存しない64bit値を作ります。
    /// 新規ワールド作成時の文字列変換と、Burst Job内の座標ハッシュの両方で使用します。
    /// </summary>
    public static class WorldSeedUtility
    {
        // FNV-1a 64bitの仕様で定められた初期値です。文字列の安定ハッシュ開始値として使います。
        private const ulong FnvOffsetBasis = 14695981039346656037UL;

        // FNV-1a 64bitの仕様で定められた乗数です。バイトごとの混合に使います。
        private const ulong FnvPrime = 1099511628211UL;

        /// <summary>
        /// 任意の文字列をUTF-8バイト列としてFNV-1a 64bitへ変換します。
        /// Unityや.NETのバージョンで変わるstring.GetHashCodeは使わないため、同じ文字列は同じ値になります。
        /// </summary>
        /// <param name="seedText">プレイヤーが入力したシード文字列です。nullは空文字列として扱います。</param>
        /// <returns>ワールド保存と生成処理に使用する安定した64bitシードです。</returns>
        public static ulong FromString(string seedText)
        {
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(seedText ?? string.Empty);
            ulong hash = FnvOffsetBasis;

            // UTF-8を明示することで、OSの既定文字コードに左右されず日本語を含む入力も同じ値になります。
            for (int index = 0; index < utf8Bytes.Length; index++)
            {
                hash ^= utf8Bytes[index];
                hash *= FnvPrime;
            }

            return hash;
        }

        /// <summary>
        /// ワールドシード、生成バージョン、座標、用途、候補番号を1つの64bit値へ混合します。
        /// 呼び出し順を状態として持たないため、チャンク生成順やJob並列順が変わっても結果は変わりません。
        /// </summary>
        /// <param name="worldSeed">文字列から変換済みのワールドシードです。</param>
        /// <param name="generatorVersion">互換性を管理する生成器バージョンです。</param>
        /// <param name="coordinateX">対象のワールド、グリッド、または候補の水平整数座標です。</param>
        /// <param name="coordinateY">対象のワールド、グリッド、または候補の垂直整数座標です。</param>
        /// <param name="purposeSalt">地形や資源など用途ごとに固定したソルトです。</param>
        /// <param name="candidateIndex">同じ座標で複数候補を試す場合の0始まり番号です。</param>
        /// <returns>すべての入力に対応する安定した64bitハッシュです。</returns>
        public static ulong HashCoordinate(
            ulong worldSeed,
            int generatorVersion,
            int coordinateX,
            int coordinateY,
            ulong purposeSalt,
            uint candidateIndex = 0)
        {
            unchecked
            {
                // 符号付き座標はuintへビット表現のまま移し、正負を衝突させず64bitへ広げます。
                ulong hash = Mix64(worldSeed ^ purposeSalt);
                hash = Mix64(hash ^ (uint)generatorVersion);
                hash = Mix64(hash ^ (uint)coordinateX);
                hash = Mix64(hash ^ (uint)coordinateY);
                hash = Mix64(hash ^ candidateIndex);
                return hash;
            }
        }

        /// <summary>
        /// 64bit入力の各ビットを十分に拡散させます。
        /// SplitMix64の最終化処理を固定実装し、System.Randomへ依存しない座標ハッシュを構成します。
        /// </summary>
        /// <param name="value">混合する64bit値です。</param>
        /// <returns>入力ビットを拡散した64bit値です。</returns>
        public static ulong Mix64(ulong value)
        {
            unchecked
            {
                value += 0x9E3779B97F4A7C15UL;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }

        /// <summary>
        /// 64bitハッシュを0〜65535の正規化済み整数値へ変換します。
        /// 浮動小数点の差異を避けながら、ノイズ補間や閾値判定へ使用できます。
        /// </summary>
        /// <param name="hash">HashCoordinateなどで得た64bit値です。</param>
        /// <returns>上位16bitから取り出した0〜65535の値です。</returns>
        public static ushort ToUnitUShort(ulong hash)
        {
            return (ushort)(hash >> 48);
        }
    }
}
