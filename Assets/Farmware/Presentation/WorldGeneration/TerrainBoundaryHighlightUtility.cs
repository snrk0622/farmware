// このファイルは、生成済み地形の8近傍から境界ハイライトの基本Sprite番号と回転数を決定します。
// ChunkTilemapPresenterが表示へ使い、ECSの地形データや生成結果そのものは変更しません。

using System;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 中央セルと同じ領域へ接続している8方向を、1ビットずつ保持します。
    /// 境界選択の入力に使い、斜め接続は両隣の上下左右も接続している場合だけ有効です。
    /// </summary>
    [Flags]
    public enum BoundaryNeighborConnection : byte
    {
        /// <summary>どの方向にも接続していない状態です。</summary>
        None = 0,

        /// <summary>北側のセルへ接続しています。</summary>
        North = 1 << 0,

        /// <summary>東側のセルへ接続しています。</summary>
        East = 1 << 1,

        /// <summary>南側のセルへ接続しています。</summary>
        South = 1 << 2,

        /// <summary>西側のセルへ接続しています。</summary>
        West = 1 << 3,

        /// <summary>北東側のセルへ接続しています。</summary>
        NorthEast = 1 << 4,

        /// <summary>南東側のセルへ接続しています。</summary>
        SouthEast = 1 << 5,

        /// <summary>南西側のセルへ接続しています。</summary>
        SouthWest = 1 << 6,

        /// <summary>北西側のセルへ接続しています。</summary>
        NorthWest = 1 << 7
    }

    /// <summary>
    /// 横一列の境界ハイライト画像に収録した14種類の基本形を表します。
    /// 数値は画像の左から右への1始まり番号と一致し、PresenterがSprite配列を参照するときに使います。
    /// </summary>
    public enum BoundaryHighlightShape : byte
    {
        /// <summary>境界を描画しない状態です。</summary>
        None = 0,

        /// <summary>画像の左から1番目の基本形です。</summary>
        Shape01 = 1,

        /// <summary>画像の左から2番目の基本形です。</summary>
        Shape02 = 2,

        /// <summary>画像の左から3番目の基本形です。</summary>
        Shape03 = 3,

        /// <summary>画像の左から4番目の基本形です。</summary>
        Shape04 = 4,

        /// <summary>画像の左から5番目の基本形です。</summary>
        Shape05 = 5,

        /// <summary>画像の左から6番目の基本形です。</summary>
        Shape06 = 6,

        /// <summary>画像の左から7番目の基本形です。</summary>
        Shape07 = 7,

        /// <summary>画像の左から8番目の基本形です。</summary>
        Shape08 = 8,

        /// <summary>画像の左から9番目の基本形です。</summary>
        Shape09 = 9,

        /// <summary>画像の左から10番目の基本形です。</summary>
        Shape10 = 10,

        /// <summary>画像の左から11番目の基本形です。</summary>
        Shape11 = 11,

        /// <summary>画像の左から12番目の基本形です。</summary>
        Shape12 = 12,

        /// <summary>画像の左から13番目の基本形です。</summary>
        Shape13 = 13,

        /// <summary>画像の左から14番目の基本形です。</summary>
        Shape14 = 14
    }

    /// <summary>
    /// 14種類の基本形のどれを何回時計回りに回転して描画するかを保持します。
    /// Presenterはこの値だけを読み、反転を使わずに実行時Tileを選びます。
    /// </summary>
    public readonly struct BoundaryHighlightSelection
    {
        /// <summary>選択された基本形です。境界がない場合はNoneです。</summary>
        public BoundaryHighlightShape Shape { get; }

        /// <summary>基本Spriteを時計回りに90度回転する回数です。値は0〜3です。</summary>
        public byte QuarterTurnsClockwise { get; }

        /// <summary>描画すべき境界が存在するかを返します。</summary>
        public bool HasHighlight => Shape != BoundaryHighlightShape.None;

        /// <summary>
        /// 基本形と時計回り回転数から、Presenterが利用する選択結果を作ります。
        /// </summary>
        /// <param name="shape">画像内の基本形です。</param>
        /// <param name="quarterTurnsClockwise">時計回りの90度回転数です。</param>
        public BoundaryHighlightSelection(
            BoundaryHighlightShape shape,
            byte quarterTurnsClockwise)
        {
            Shape = shape;
            QuarterTurnsClockwise = (byte)(quarterTurnsClockwise & 3);
        }
    }

    /// <summary>
    /// 中央セルの周囲8方向にある生成済み地形を、座標の取り違えを防ぐ名前付きデータとして保持します。
    /// 値型のためサブチャンク走査中にmanaged allocationを発生させません。
    /// </summary>
    public readonly struct TerrainNeighborTiles
    {
        /// <summary>北側の生成済み地形です。</summary>
        public GeneratedTerrainCell North { get; }

        /// <summary>東側の生成済み地形です。</summary>
        public GeneratedTerrainCell East { get; }

        /// <summary>南側の生成済み地形です。</summary>
        public GeneratedTerrainCell South { get; }

        /// <summary>西側の生成済み地形です。</summary>
        public GeneratedTerrainCell West { get; }

        /// <summary>北東側の生成済み地形です。</summary>
        public GeneratedTerrainCell NorthEast { get; }

        /// <summary>南東側の生成済み地形です。</summary>
        public GeneratedTerrainCell SouthEast { get; }

        /// <summary>南西側の生成済み地形です。</summary>
        public GeneratedTerrainCell SouthWest { get; }

        /// <summary>北西側の生成済み地形です。</summary>
        public GeneratedTerrainCell NorthWest { get; }

        /// <summary>
        /// 中央セルを囲む8方向の地形を、北から時計回りの明示的な順番で保存します。
        /// </summary>
        /// <param name="north">北側の地形です。</param>
        /// <param name="east">東側の地形です。</param>
        /// <param name="south">南側の地形です。</param>
        /// <param name="west">西側の地形です。</param>
        /// <param name="northEast">北東側の地形です。</param>
        /// <param name="southEast">南東側の地形です。</param>
        /// <param name="southWest">南西側の地形です。</param>
        /// <param name="northWest">北西側の地形です。</param>
        public TerrainNeighborTiles(
            GeneratedTerrainCell north,
            GeneratedTerrainCell east,
            GeneratedTerrainCell south,
            GeneratedTerrainCell west,
            GeneratedTerrainCell northEast,
            GeneratedTerrainCell southEast,
            GeneratedTerrainCell southWest,
            GeneratedTerrainCell northWest)
        {
            North = north;
            East = east;
            South = south;
            West = west;
            NorthEast = northEast;
            SouthEast = southEast;
            SouthWest = southWest;
            NorthWest = northWest;
        }
    }

    /// <summary>
    /// 8近傍の接続状態を47種類へ正規化し、14基本形と回転だけで境界ハイライトを選択します。
    /// 土地と岩壁で「同じ領域」の条件を分け、Biome差による不要な境界は作りません。
    /// </summary>
    public static class TerrainBoundaryHighlightUtility
    {
        /// <summary>横長画像へ左から右に収録する基本Spriteの固定枚数です。</summary>
        public const int ExpectedSpriteCount = 14;

        /// <summary>白い境界線をBase Tileへ自然に馴染ませる、表示時の既定不透明度です。</summary>
        public const float DefaultOpacity = 0.2f;

        /// <summary>
        /// 8近傍を土地の連続面として調べるか、岩壁だけの連続面として調べるかを内部で区別します。
        /// delegateを毎セルへ渡さず、サブチャンク走査中の不要なmanaged allocationを避けるために使用します。
        /// </summary>
        private enum ConnectionRegion : byte
        {
            /// <summary>水・マグマ以外を陸続きとみなします。</summary>
            Land = 0,

            /// <summary>岩壁だけを同じ領域とみなします。</summary>
            RockWall = 1
        }

        // 境界形状を表す内部ビットです。下位4ビットは外周、上位4ビットは内角を北から時計回りに表します。
        private const byte NorthEdge = 1 << 0;
        private const byte EastEdge = 1 << 1;
        private const byte SouthEdge = 1 << 2;
        private const byte WestEdge = 1 << 3;
        private const byte NorthEastInnerCorner = 1 << 4;
        private const byte SouthEastInnerCorner = 1 << 5;
        private const byte SouthWestInnerCorner = 1 << 6;
        private const byte NorthWestInnerCorner = 1 << 7;

        // 画像の左から右へ並ぶ14基本形を、外周線と内角のビット構成で表した不変テーブルです。
        private static readonly byte[] CanonicalFeatureMasks =
        {
            0x00,
            0x0F,
            0x0E,
            0x0A,
            0x0C,
            0x1C,
            0x01,
            0x21,
            0x41,
            0x61,
            0x10,
            0x30,
            0x50,
            0x70,
            0xF0
        };

        // 256通りの境界特徴から選択結果をO(1)で得るため、型初期化時に1回だけ構築する参照表です。
        private static readonly BoundaryHighlightSelection[] SelectionByFeatureMask =
            CreateSelectionByFeatureMask();

        /// <summary>
        /// 斜め方向は隣接する上下左右の両方が接続している場合だけ残し、無効な斜め接続を除去します。
        /// </summary>
        /// <param name="connections">中央セルと同じ領域である8方向のビット集合です。</param>
        /// <returns>47種類の有効な接続状態のいずれかです。</returns>
        public static BoundaryNeighborConnection NormalizeConnections(
            BoundaryNeighborConnection connections)
        {
            BoundaryNeighborConnection normalized = connections;
            normalized = KeepDiagonalOnlyWithCardinals(
                normalized,
                BoundaryNeighborConnection.NorthEast,
                BoundaryNeighborConnection.North,
                BoundaryNeighborConnection.East);
            normalized = KeepDiagonalOnlyWithCardinals(
                normalized,
                BoundaryNeighborConnection.SouthEast,
                BoundaryNeighborConnection.South,
                BoundaryNeighborConnection.East);
            normalized = KeepDiagonalOnlyWithCardinals(
                normalized,
                BoundaryNeighborConnection.SouthWest,
                BoundaryNeighborConnection.South,
                BoundaryNeighborConnection.West);
            normalized = KeepDiagonalOnlyWithCardinals(
                normalized,
                BoundaryNeighborConnection.NorthWest,
                BoundaryNeighborConnection.North,
                BoundaryNeighborConnection.West);
            return normalized;
        }

        /// <summary>
        /// 正規化済み接続状態から、外周線と内角を表す8ビットの境界特徴を作ります。
        /// </summary>
        /// <param name="connections">中央セルと同じ領域である8方向です。未正規化でも安全に処理します。</param>
        /// <returns>下位4ビットが外周、上位4ビットが内角の境界特徴です。</returns>
        public static byte BuildHighlightFeatureMask(BoundaryNeighborConnection connections)
        {
            BoundaryNeighborConnection normalized = NormalizeConnections(connections);
            byte features = 0;

            // 上下左右に同じ領域がなければ、その方向へ外周線を描きます。
            if (!Has(normalized, BoundaryNeighborConnection.North))
            {
                features |= NorthEdge;
            }

            if (!Has(normalized, BoundaryNeighborConnection.East))
            {
                features |= EastEdge;
            }

            if (!Has(normalized, BoundaryNeighborConnection.South))
            {
                features |= SouthEdge;
            }

            if (!Has(normalized, BoundaryNeighborConnection.West))
            {
                features |= WestEdge;
            }

            // 両側の上下左右が接続していて対角だけ欠ける場合は、へこみを示す内角を描きます。
            if (Has(normalized, BoundaryNeighborConnection.North)
                && Has(normalized, BoundaryNeighborConnection.East)
                && !Has(normalized, BoundaryNeighborConnection.NorthEast))
            {
                features |= NorthEastInnerCorner;
            }

            if (Has(normalized, BoundaryNeighborConnection.East)
                && Has(normalized, BoundaryNeighborConnection.South)
                && !Has(normalized, BoundaryNeighborConnection.SouthEast))
            {
                features |= SouthEastInnerCorner;
            }

            if (Has(normalized, BoundaryNeighborConnection.South)
                && Has(normalized, BoundaryNeighborConnection.West)
                && !Has(normalized, BoundaryNeighborConnection.SouthWest))
            {
                features |= SouthWestInnerCorner;
            }

            if (Has(normalized, BoundaryNeighborConnection.West)
                && Has(normalized, BoundaryNeighborConnection.North)
                && !Has(normalized, BoundaryNeighborConnection.NorthWest))
            {
                features |= NorthWestInnerCorner;
            }

            return features;
        }

        /// <summary>
        /// 接続状態に対応する14基本形と時計回り回転数を返します。
        /// </summary>
        /// <param name="connections">中央セルと同じ領域である8方向です。</param>
        /// <returns>境界がなければNone、それ以外は基本形と0〜3回転です。</returns>
        public static BoundaryHighlightSelection SelectForConnections(
            BoundaryNeighborConnection connections)
        {
            byte features = BuildHighlightFeatureMask(connections);
            return SelectionByFeatureMask[features];
        }

        /// <summary>
        /// 土地セルについて、水・マグマだけを領域外として境界ハイライトを選びます。
        /// 異なるBiomeや岩壁は陸続きとして扱うため、Biome境界へ線は描きません。
        /// </summary>
        /// <param name="current">判定対象の中央地形です。</param>
        /// <param name="neighbors">中央を囲む8方向の地形です。</param>
        /// <returns>中央が通常土地でなければNone、それ以外は土地境界の選択結果です。</returns>
        public static BoundaryHighlightSelection SelectLand(
            GeneratedTerrainCell current,
            in TerrainNeighborTiles neighbors)
        {
            if (!current.GroundBlockRuntimeId.IsValid
                || current.WallBlockRuntimeId.IsValid
                || current.LiquidRuntimeId.IsValid)
            {
                return default;
            }

            BoundaryNeighborConnection connections = BuildConnections(
                neighbors,
                ConnectionRegion.Land);
            return SelectForConnections(connections);
        }

        /// <summary>
        /// 岩壁セルについて、隣接する岩壁だけを同じ領域として上面境界ハイライトを選びます。
        /// </summary>
        /// <param name="current">判定対象の中央地形です。</param>
        /// <param name="neighbors">中央を囲む8方向の地形です。</param>
        /// <returns>中央が岩壁でなければNone、それ以外は岩壁上面境界の選択結果です。</returns>
        public static BoundaryHighlightSelection SelectRockWall(
            GeneratedTerrainCell current,
            in TerrainNeighborTiles neighbors)
        {
            if (!current.WallBlockRuntimeId.IsValid)
            {
                return default;
            }

            BoundaryNeighborConnection connections = BuildConnections(
                neighbors,
                ConnectionRegion.RockWall);
            return SelectForConnections(connections);
        }

        /// <summary>
        /// 指定した基本形が画像で表す境界特徴を返します。
        /// テストとEditor検証から、画像順と選択表の対応を確認するために使用します。
        /// </summary>
        /// <param name="shape">確認する基本形です。</param>
        /// <returns>下位4ビットが外周、上位4ビットが内角の特徴です。</returns>
        /// <exception cref="ArgumentOutOfRangeException">定義外の基本形が渡された場合に送出します。</exception>
        public static byte GetCanonicalFeatureMask(BoundaryHighlightShape shape)
        {
            int index = (int)shape;
            if (index < 0 || index >= CanonicalFeatureMasks.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(shape), shape, "未定義の境界基本形です。");
            }

            return CanonicalFeatureMasks[index];
        }

        /// <summary>
        /// 境界特徴を時計回りへ90度単位で回転し、反転を使わない派生形を作ります。
        /// </summary>
        /// <param name="features">回転前の外周・内角ビットです。</param>
        /// <param name="quarterTurnsClockwise">時計回りの90度回転数です。4以上は4で循環します。</param>
        /// <returns>指定回数だけ時計回りに回転した特徴ビットです。</returns>
        public static byte RotateHighlightFeatureMask(byte features, int quarterTurnsClockwise)
        {
            int normalizedTurns = quarterTurnsClockwise & 3;
            byte rotated = features;
            for (int turn = 0; turn < normalizedTurns; turn++)
            {
                rotated = RotateHighlightFeatureMaskOnceClockwise(rotated);
            }

            return rotated;
        }

        /// <summary>
        /// 斜め接続を、その両側の上下左右が接続している場合だけ保持します。
        /// </summary>
        /// <param name="connections">現在の接続ビットです。</param>
        /// <param name="diagonal">確認する斜め方向です。</param>
        /// <param name="firstCardinal">斜めに接する1つ目の上下左右方向です。</param>
        /// <param name="secondCardinal">斜めに接する2つ目の上下左右方向です。</param>
        /// <returns>無効な斜めビットを除去した接続状態です。</returns>
        private static BoundaryNeighborConnection KeepDiagonalOnlyWithCardinals(
            BoundaryNeighborConnection connections,
            BoundaryNeighborConnection diagonal,
            BoundaryNeighborConnection firstCardinal,
            BoundaryNeighborConnection secondCardinal)
        {
            if (Has(connections, diagonal)
                && (!Has(connections, firstCardinal) || !Has(connections, secondCardinal)))
            {
                connections &= ~diagonal;
            }

            return connections;
        }

        /// <summary>
        /// 指定ビットが接続状態へ含まれるかを、enumの型を保ったまま確認します。
        /// </summary>
        /// <param name="connections">確認対象の接続状態です。</param>
        /// <param name="direction">確認する1方向のビットです。</param>
        /// <returns>その方向が接続していればtrueです。</returns>
        private static bool Has(
            BoundaryNeighborConnection connections,
            BoundaryNeighborConnection direction)
        {
            return (connections & direction) != 0;
        }

        /// <summary>
        /// 8方向の地形へ同じ接続条件を適用し、境界選択用のビット集合へ変換します。
        /// </summary>
        /// <param name="neighbors">中央を囲む8方向の地形です。</param>
        /// <param name="region">土地または岩壁のどちらを同じ領域として調べるかです。</param>
        /// <returns>条件を満たす方向だけを含む接続ビットです。</returns>
        private static BoundaryNeighborConnection BuildConnections(
            in TerrainNeighborTiles neighbors,
            ConnectionRegion region)
        {
            BoundaryNeighborConnection connections = BoundaryNeighborConnection.None;
            AddConnectionIfMatched(ref connections, BoundaryNeighborConnection.North, neighbors.North, region);
            AddConnectionIfMatched(ref connections, BoundaryNeighborConnection.East, neighbors.East, region);
            AddConnectionIfMatched(ref connections, BoundaryNeighborConnection.South, neighbors.South, region);
            AddConnectionIfMatched(ref connections, BoundaryNeighborConnection.West, neighbors.West, region);
            AddConnectionIfMatched(ref connections, BoundaryNeighborConnection.NorthEast, neighbors.NorthEast, region);
            AddConnectionIfMatched(ref connections, BoundaryNeighborConnection.SouthEast, neighbors.SouthEast, region);
            AddConnectionIfMatched(ref connections, BoundaryNeighborConnection.SouthWest, neighbors.SouthWest, region);
            AddConnectionIfMatched(ref connections, BoundaryNeighborConnection.NorthWest, neighbors.NorthWest, region);
            return connections;
        }

        /// <summary>
        /// 1方向の地形が接続条件を満たす場合だけ、対応ビットを追加します。
        /// </summary>
        /// <param name="connections">更新する接続ビットです。</param>
        /// <param name="direction">追加候補の方向です。</param>
        /// <param name="tile">その方向にある生成済み地形です。</param>
        /// <param name="region">土地または岩壁のどちらを同じ領域として調べるかです。</param>
        private static void AddConnectionIfMatched(
            ref BoundaryNeighborConnection connections,
            BoundaryNeighborConnection direction,
            GeneratedTerrainCell tile,
            ConnectionRegion region)
        {
            if (IsConnected(tile, region))
            {
                connections |= direction;
            }
        }

        /// <summary>
        /// 選択中の領域種別に従い、隣接地形を同じ領域とみなすか判定します。
        /// </summary>
        /// <param name="tile">隣接する生成済み地形です。</param>
        /// <param name="region">土地または岩壁の判定種別です。</param>
        /// <returns>土地では水・マグマ以外、岩壁では岩壁ならtrueです。</returns>
        private static bool IsConnected(GeneratedTerrainCell tile, ConnectionRegion region)
        {
            if (region == ConnectionRegion.RockWall)
            {
                return tile.WallBlockRuntimeId.IsValid;
            }

            return !tile.LiquidRuntimeId.IsValid
                && (tile.GroundBlockRuntimeId.IsValid || tile.WallBlockRuntimeId.IsValid);
        }

        /// <summary>
        /// 14基本形を0〜3回回転し、47種類すべてを直接参照できる表へ展開します。
        /// </summary>
        /// <returns>境界特徴を添字として選択結果を取得できる256要素の配列です。</returns>
        private static BoundaryHighlightSelection[] CreateSelectionByFeatureMask()
        {
            var selections = new BoundaryHighlightSelection[byte.MaxValue + 1];
            for (int shapeIndex = 1; shapeIndex < CanonicalFeatureMasks.Length; shapeIndex++)
            {
                BoundaryHighlightShape shape = (BoundaryHighlightShape)shapeIndex;
                byte canonicalFeatures = CanonicalFeatureMasks[shapeIndex];
                for (byte rotation = 0; rotation < 4; rotation++)
                {
                    byte rotatedFeatures = RotateHighlightFeatureMask(canonicalFeatures, rotation);
                    // 回転対称な基本形は同じ特徴へ複数回到達します。最初の0回転を残し、保存結果を安定させます。
                    if (!selections[rotatedFeatures].HasHighlight)
                    {
                        selections[rotatedFeatures] = new BoundaryHighlightSelection(shape, rotation);
                    }
                }
            }

            return selections;
        }

        /// <summary>
        /// 外周4方向と内角4方向を、一段階だけ時計回りへ移します。
        /// </summary>
        /// <param name="features">回転前の境界特徴です。</param>
        /// <returns>時計回りに90度回転した境界特徴です。</returns>
        private static byte RotateHighlightFeatureMaskOnceClockwise(byte features)
        {
            byte rotated = 0;
            RotateBit(features, ref rotated, NorthEdge, EastEdge);
            RotateBit(features, ref rotated, EastEdge, SouthEdge);
            RotateBit(features, ref rotated, SouthEdge, WestEdge);
            RotateBit(features, ref rotated, WestEdge, NorthEdge);
            RotateBit(features, ref rotated, NorthEastInnerCorner, SouthEastInnerCorner);
            RotateBit(features, ref rotated, SouthEastInnerCorner, SouthWestInnerCorner);
            RotateBit(features, ref rotated, SouthWestInnerCorner, NorthWestInnerCorner);
            RotateBit(features, ref rotated, NorthWestInnerCorner, NorthEastInnerCorner);
            return rotated;
        }

        /// <summary>
        /// 元の特徴に1ビットがある場合だけ、回転後の対応位置へビットを立てます。
        /// </summary>
        /// <param name="source">回転前の境界特徴です。</param>
        /// <param name="destination">回転後の境界特徴を蓄積する値です。</param>
        /// <param name="sourceBit">確認する回転前ビットです。</param>
        /// <param name="destinationBit">対応する回転後ビットです。</param>
        private static void RotateBit(
            byte source,
            ref byte destination,
            byte sourceBit,
            byte destinationBit)
        {
            if ((source & sourceBit) != 0)
            {
                destination |= destinationBit;
            }
        }
    }
}
