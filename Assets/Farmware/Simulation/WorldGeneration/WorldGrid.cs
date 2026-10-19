// このファイルは、無限ワールドのタイル・論理チャンク・サブチャンク間の整数座標変換を担当します。
// 生成Systemと表示側が同じ変換を使うことで、特に負座標のチャンク境界で食い違いが起きないようにします。

using System;
using Farmware.Simulation;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 固定ワールドレイヤーと絶対タイルX/Yをまとめた、1セルの安定した論理座標です。
    /// 地形差分、占有、個体位置、診断表示が同じX/Yの別レイヤーを混同しないために使用します。
    /// </summary>
    public readonly struct WorldTileCoordinate : IEquatable<WorldTileCoordinate>
    {
        /// <summary>セルが属する固定ワールドレイヤーです。通常状態ではNoneを使用しません。</summary>
        public readonly WorldLayer Layer;

        /// <summary>水平方向の絶対タイル座標です。ワールド原点より左では負になります。</summary>
        public readonly int X;

        /// <summary>垂直方向の絶対タイル座標です。ワールド原点より下では負になります。</summary>
        public readonly int Y;

        /// <summary>
        /// レイヤーと絶対X/Yから論理セル座標を作ります。
        /// </summary>
        /// <param name="layer">セルが属する固定ワールドレイヤーです。</param>
        /// <param name="x">水平方向の絶対タイル座標です。</param>
        /// <param name="y">垂直方向の絶対タイル座標です。</param>
        public WorldTileCoordinate(WorldLayer layer, int x, int y)
        {
            Layer = layer;
            X = x;
            Y = y;
        }

        /// <summary>
        /// 2つのセル座標が同じレイヤーと絶対X/Yを表すか判定します。
        /// </summary>
        /// <param name="other">比較するセル座標です。</param>
        /// <returns>Layer、X、Yがすべて同じ場合はtrueです。</returns>
        public bool Equals(WorldTileCoordinate other)
        {
            return Layer == other.Layer && X == other.X && Y == other.Y;
        }

        /// <summary>
        /// オブジェクトが同じセル座標か判定します。
        /// </summary>
        /// <param name="obj">比較対象です。</param>
        /// <returns>同じWorldTileCoordinate値ならtrueです。</returns>
        public override bool Equals(object obj)
        {
            return obj is WorldTileCoordinate other && Equals(other);
        }

        /// <summary>
        /// Dictionary等の実行時検索に使う、レイヤーを含むハッシュ値を返します。
        /// セーブIDや生成乱数には使用せず、決定論的生成はWorldSeedUtilityを使用します。
        /// </summary>
        /// <returns>Layer、X、Yから求めた32bitハッシュです。</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Layer;
                hash = (hash * 397) ^ X;
                return (hash * 397) ^ Y;
            }
        }

        /// <summary>
        /// デバッグ表示用にレイヤーと絶対座標を返します。
        /// </summary>
        /// <returns>「Layer:(X, Y)」形式の文字列です。</returns>
        public override string ToString()
        {
            return $"{Layer}:({X}, {Y})";
        }
    }

    /// <summary>
    /// ワールド生成で固定して扱うグリッド寸法をまとめます。
    /// 生成処理、セーブ差分、表示更新のすべてが同じ値を参照します。
    /// </summary>
    public static class WorldGridConstants
    {
        /// <summary>1辺に含まれるタイル数です。仕様で64タイルに固定されています。</summary>
        public const int LogicalChunkSize = 64;

        /// <summary>1サブチャンクの1辺に含まれるタイル数です。仕様で16タイルに固定されています。</summary>
        public const int SubChunkSize = 16;

        /// <summary>1論理チャンクに含まれるタイル総数です。64×64で4096タイルです。</summary>
        public const int TilesPerChunk = LogicalChunkSize * LogicalChunkSize;

        /// <summary>論理チャンクの1辺に並ぶサブチャンク数です。64÷16で4個です。</summary>
        public const int SubChunksPerAxis = LogicalChunkSize / SubChunkSize;

        /// <summary>1論理チャンクに含まれるサブチャンク総数です。4×4で16個です。</summary>
        public const int SubChunksPerChunk = SubChunksPerAxis * SubChunksPerAxis;

        /// <summary>
        /// 64×64の全ローカルタイルをintの絶対タイル座標へ安全に戻せる、最小の論理チャンク座標です。
        /// ストリーミング範囲の下限として使用し、int最小値より反対側へ周回することを防ぎます。
        /// </summary>
        public const int MinimumLogicalChunkCoordinate = int.MinValue / LogicalChunkSize;

        /// <summary>
        /// 64×64の全ローカルタイルをintの絶対タイル座標へ安全に戻せる、最大の論理チャンク座標です。
        /// ストリーミング範囲の上限として使用し、int最大値より反対側へ周回することを防ぎます。
        /// </summary>
        public const int MaximumLogicalChunkCoordinate = int.MaxValue / LogicalChunkSize;
    }

    /// <summary>
    /// 64×64タイル単位の論理チャンク座標を表します。
    /// ECSのチャンクEntity、ストリーミング要求、セーブデータの安定したキーとして使用します。
    /// </summary>
    public readonly struct ChunkCoordinate : IEquatable<ChunkCoordinate>
    {
        /// <summary>チャンクが属する固定ワールドレイヤーです。通常状態ではNoneを使用しません。</summary>
        public readonly WorldLayer Layer;

        /// <summary>水平方向の論理チャンク座標です。ワールド原点より左では負になります。</summary>
        public readonly int X;

        /// <summary>垂直方向の論理チャンク座標です。ワールド原点より下では負になります。</summary>
        public readonly int Y;

        /// <summary>
        /// Surfaceレイヤーと指定した2軸の値から論理チャンク座標を作ります。
        /// WORLD-01C以前の地上専用呼び出しを安全に移行する互換入口であり、新規の多層処理はLayerを明示するコンストラクターを使います。
        /// </summary>
        /// <param name="x">水平方向の論理チャンク座標です。</param>
        /// <param name="y">垂直方向の論理チャンク座標です。</param>
        public ChunkCoordinate(int x, int y)
            : this(WorldLayer.Surface, x, y)
        {
        }

        /// <summary>
        /// 固定ワールドレイヤーと2軸の値から論理チャンク座標を作ります。
        /// </summary>
        /// <param name="layer">チャンクが属する固定ワールドレイヤーです。</param>
        /// <param name="x">水平方向の論理チャンク座標です。</param>
        /// <param name="y">垂直方向の論理チャンク座標です。</param>
        public ChunkCoordinate(WorldLayer layer, int x, int y)
        {
            Layer = layer;
            X = x;
            Y = y;
        }

        /// <summary>
        /// 2つのチャンク座標が同じ位置を表すか判定します。
        /// </summary>
        /// <param name="other">比較するチャンク座標です。</param>
        /// <returns>Layer、X、Yがすべて同じ場合はtrueです。</returns>
        public bool Equals(ChunkCoordinate other)
        {
            return Layer == other.Layer && X == other.X && Y == other.Y;
        }

        /// <summary>
        /// オブジェクトが同じチャンク座標か判定します。
        /// </summary>
        /// <param name="obj">比較対象です。</param>
        /// <returns>同じChunkCoordinate値ならtrueです。</returns>
        public override bool Equals(object obj)
        {
            return obj is ChunkCoordinate other && Equals(other);
        }

        /// <summary>
        /// Dictionaryなどの実行時検索に使うハッシュ値を返します。
        /// セーブ用IDや生成乱数には使わず、生成にはWorldSeedUtilityの安定ハッシュを使用します。
        /// </summary>
        /// <returns>現在のXとYから求めた32bitハッシュです。</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Layer;
                hash = (hash * 397) ^ X;
                return (hash * 397) ^ Y;
            }
        }

        /// <summary>
        /// デバッグ表示用に読みやすい座標文字列を返します。
        /// </summary>
        /// <returns>「Layer:(X, Y)」形式の文字列です。</returns>
        public override string ToString()
        {
            return $"{Layer}:({X}, {Y})";
        }
    }

    /// <summary>
    /// 論理チャンク内の0〜63のローカルタイル座標を表します。
    /// ワールド座標からチャンク配列の添字へ変換するときに使用します。
    /// </summary>
    public readonly struct ChunkLocalTileCoordinate
    {
        /// <summary>チャンク左端からの水平タイル位置です。範囲は0〜63です。</summary>
        public readonly int X;

        /// <summary>チャンク下端からの垂直タイル位置です。範囲は0〜63です。</summary>
        public readonly int Y;

        /// <summary>
        /// チャンク内ローカル座標を作ります。
        /// 呼び出し側は0〜63の範囲へ変換済みの値を渡します。
        /// </summary>
        /// <param name="x">チャンク内の水平位置です。</param>
        /// <param name="y">チャンク内の垂直位置です。</param>
        public ChunkLocalTileCoordinate(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// 論理チャンク内で4×4に並ぶサブチャンク座標を表します。
    /// Tilemap更新、カリング、差分保存を16×16タイル単位に分割するときに使用します。
    /// </summary>
    public readonly struct SubChunkCoordinate
    {
        /// <summary>チャンク内の水平サブチャンク位置です。範囲は0〜3です。</summary>
        public readonly int X;

        /// <summary>チャンク内の垂直サブチャンク位置です。範囲は0〜3です。</summary>
        public readonly int Y;

        /// <summary>
        /// チャンク内サブチャンク座標を作ります。
        /// </summary>
        /// <param name="x">水平サブチャンク位置です。</param>
        /// <param name="y">垂直サブチャンク位置です。</param>
        public SubChunkCoordinate(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// タイル座標、論理チャンク座標、配列添字を相互変換する純粋計算を提供します。
    /// MonoBehaviourやTransformに依存しないため、ECS JobとEditModeテストの両方から利用できます。
    /// </summary>
    public static class WorldGrid
    {
        /// <summary>
        /// 正の除数に対して、負の値も数学上の床方向へ丸める整数除算を行います。
        /// C#の整数除算は0方向へ丸めるため、-1タイルを誤って0チャンクへ入れないために必要です。
        /// </summary>
        /// <param name="value">割られる整数です。</param>
        /// <param name="positiveDivisor">0より大きい除数です。</param>
        /// <returns>valueをpositiveDivisorで割り、負の無限大方向へ丸めた商です。</returns>
        public static int FloorDivide(int value, int positiveDivisor)
        {
            int quotient = value / positiveDivisor;
            int remainder = value % positiveDivisor;

            // C#は負数を0方向へ丸めます。余りが負なら商を1つ下げ、床方向の結果へ補正します。
            if (remainder < 0)
            {
                quotient--;
            }

            return quotient;
        }

        /// <summary>
        /// ワールドタイル座標から、そのタイルを所有する論理チャンク座標を求めます。
        /// </summary>
        /// <param name="tileX">符号付きのワールド水平タイル座標です。</param>
        /// <param name="tileY">符号付きのワールド垂直タイル座標です。</param>
        /// <returns>対象タイルを含む64×64論理チャンクの座標です。</returns>
        public static ChunkCoordinate ToChunkCoordinate(int tileX, int tileY)
        {
            return ToChunkCoordinate(WorldLayer.Surface, tileX, tileY);
        }

        /// <summary>
        /// レイヤー付きワールドタイル座標から、そのタイルを所有する論理チャンク座標を求めます。
        /// X/Yの変換規則は全レイヤー共通ですが、戻り値へLayerを保持して別層の索引衝突を防ぎます。
        /// </summary>
        /// <param name="layer">タイルが属する固定ワールドレイヤーです。</param>
        /// <param name="tileX">符号付きのワールド水平タイル座標です。</param>
        /// <param name="tileY">符号付きのワールド垂直タイル座標です。</param>
        /// <returns>対象タイルを含むレイヤー付き64×64論理チャンク座標です。</returns>
        public static ChunkCoordinate ToChunkCoordinate(WorldLayer layer, int tileX, int tileY)
        {
            return new ChunkCoordinate(
                layer,
                FloorDivide(tileX, WorldGridConstants.LogicalChunkSize),
                FloorDivide(tileY, WorldGridConstants.LogicalChunkSize));
        }

        /// <summary>
        /// レイヤー付きセル座標から、そのセルを所有する論理チャンク座標を求めます。
        /// </summary>
        /// <param name="tile">変換するレイヤー付き絶対セル座標です。</param>
        /// <returns>同じレイヤーを保持する論理チャンク座標です。</returns>
        public static ChunkCoordinate ToChunkCoordinate(in WorldTileCoordinate tile)
        {
            return ToChunkCoordinate(tile.Layer, tile.X, tile.Y);
        }

        /// <summary>
        /// ワールドタイル座標を、所属チャンク内の0〜63のローカル座標へ変換します。
        /// </summary>
        /// <param name="tileX">符号付きのワールド水平タイル座標です。</param>
        /// <param name="tileY">符号付きのワールド垂直タイル座標です。</param>
        /// <returns>必ず各軸が0〜63になるチャンク内座標です。</returns>
        public static ChunkLocalTileCoordinate ToLocalTileCoordinate(int tileX, int tileY)
        {
            ChunkCoordinate chunk = ToChunkCoordinate(tileX, tileY);
            return new ChunkLocalTileCoordinate(
                tileX - (chunk.X * WorldGridConstants.LogicalChunkSize),
                tileY - (chunk.Y * WorldGridConstants.LogicalChunkSize));
        }

        /// <summary>
        /// 論理チャンク座標とチャンク内座標から、ワールド水平タイル座標を復元します。
        /// </summary>
        /// <param name="chunkX">論理チャンクの水平座標です。</param>
        /// <param name="localX">チャンク内の水平座標です。0〜63以外を渡す場合は、先にTryToWorldTileCoordinateで範囲を検証してください。</param>
        /// <returns>符号付きのワールド水平タイル座標です。</returns>
        public static int ToWorldTileX(int chunkX, int localX)
        {
            return (chunkX * WorldGridConstants.LogicalChunkSize) + localX;
        }

        /// <summary>
        /// 論理チャンク座標とチャンク内座標から、ワールド垂直タイル座標を復元します。
        /// </summary>
        /// <param name="chunkY">論理チャンクの垂直座標です。</param>
        /// <param name="localY">チャンク内の垂直座標です。0〜63以外を渡す場合は、先にTryToWorldTileCoordinateで範囲を検証してください。</param>
        /// <returns>符号付きのワールド垂直タイル座標です。</returns>
        public static int ToWorldTileY(int chunkY, int localY)
        {
            return (chunkY * WorldGridConstants.LogicalChunkSize) + localY;
        }

        /// <summary>
        /// 論理チャンク座標とローカル座標を64bitで合成し、intの絶対タイル座標へ安全に変換します。
        /// 境界ハイライトや岩壁影がチャンク外の隣接セルを調べる場合に、ワールド反対端への整数周回を防ぎます。
        /// </summary>
        /// <param name="chunkX">基準となる水平論理チャンク座標です。</param>
        /// <param name="chunkY">基準となる垂直論理チャンク座標です。</param>
        /// <param name="localX">基準チャンク左端からの水平タイル差です。0〜63の外側も検証できます。</param>
        /// <param name="localY">基準チャンク下端からの垂直タイル差です。0〜63の外側も検証できます。</param>
        /// <param name="worldTileX">成功時に得られる水平絶対タイル座標です。</param>
        /// <param name="worldTileY">成功時に得られる垂直絶対タイル座標です。</param>
        /// <returns>両軸がint範囲内ならtrue、どちらかがワールド座標域の外ならfalseです。</returns>
        public static bool TryToWorldTileCoordinate(
            int chunkX,
            int chunkY,
            int localX,
            int localY,
            out int worldTileX,
            out int worldTileY)
        {
            long candidateX = ((long)chunkX * WorldGridConstants.LogicalChunkSize) + localX;
            long candidateY = ((long)chunkY * WorldGridConstants.LogicalChunkSize) + localY;
            if (candidateX < int.MinValue
                || candidateX > int.MaxValue
                || candidateY < int.MinValue
                || candidateY > int.MaxValue)
            {
                worldTileX = default;
                worldTileY = default;
                return false;
            }

            worldTileX = (int)candidateX;
            worldTileY = (int)candidateY;
            return true;
        }

        /// <summary>
        /// チャンク内の2次元タイル座標を、4096要素の連続バッファ用添字へ変換します。
        /// Xを先に進める行優先配置に統一し、Jobと表示側が同じ並びを使えるようにします。
        /// </summary>
        /// <param name="localX">0〜63のチャンク内水平座標です。</param>
        /// <param name="localY">0〜63のチャンク内垂直座標です。</param>
        /// <returns>0〜4095のバッファ添字です。</returns>
        public static int ToTileIndex(int localX, int localY)
        {
            return (localY * WorldGridConstants.LogicalChunkSize) + localX;
        }

        /// <summary>
        /// チャンク内タイル座標から、そのタイルを含む16×16サブチャンク座標を求めます。
        /// </summary>
        /// <param name="localX">0〜63のチャンク内水平座標です。</param>
        /// <param name="localY">0〜63のチャンク内垂直座標です。</param>
        /// <returns>各軸が0〜3のサブチャンク座標です。</returns>
        public static SubChunkCoordinate ToSubChunkCoordinate(int localX, int localY)
        {
            return new SubChunkCoordinate(
                localX / WorldGridConstants.SubChunkSize,
                localY / WorldGridConstants.SubChunkSize);
        }
    }
}
