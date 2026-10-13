// このファイルは、ECSが生成した2つの隣接地形から、断面・岩壁側面・液面アニメーションの表示種類と表示高を純粋計算します。
// Tilemapへ実際に描画するChunkTilemapPresenterと判定を分離し、Sceneを開かないEditModeテストでも表示規則を検証できるようにします。

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 通常地形の手前へ重ねる断面素材の種類です。
    /// ゲームルール用の地形IDではなく、どの見た目を選ぶかだけをPresentation層で表します。
    /// </summary>
    public enum TerrainFrontEdgeKind : byte
    {
        /// <summary>断面を描画しない通常状態です。</summary>
        None = 0,

        /// <summary>草原・森林・湿地に使用する土の断面です。</summary>
        Soil = 1,

        /// <summary>砂漠に使用する砂の断面です。</summary>
        Sand = 2,

        /// <summary>岩地・火山に使用する岩の断面です。</summary>
        Rock = 3,

        /// <summary>雪原に使用する雪と土の断面です。</summary>
        Snow = 4
    }

    /// <summary>
    /// 液体セルへ重ねるアニメーション素材の種類です。
    /// 液体の種類や採水可否は変更せず、透明な波・気泡の選択だけに使用します。
    /// </summary>
    public enum LiquidAnimationKind : byte
    {
        /// <summary>アニメーションを描画しない通常地形です。</summary>
        None = 0,

        /// <summary>川と池へ重ねる淡水の波です。</summary>
        FreshWater = 1,

        /// <summary>海へ重ねる海水の波です。</summary>
        SaltWater = 2,

        /// <summary>マグマへ重ねる発光気泡です。</summary>
        Magma = 3
    }

    /// <summary>
    /// GeneratedTileを読み取り、Forager風の直線的な手前断面と液面オーバーレイを分類します。
    /// すべて純粋計算であり、ECSデータ、Tilemap、アセットを変更しません。
    /// </summary>
    public static class TerrainLayerPresentationUtility
    {
        /// <summary>
        /// 水・マグマの上面を土地より画面下へ投影するピクセル数です。
        /// 16pxセルの半分だけ下げることで、土地断面の下端から液面を開始し、液面下辺は手前の土地に隠れる構図にします。
        /// </summary>
        public const int LiquidSurfaceDepthInPixels = 8;

        /// <summary>
        /// 16pxを1セルとするTilemap上での液面表示オフセットです。
        /// 画面下方向へ移動するため負値とし、静止液面とアニメーションの両Tilemapへ同じ値を適用します。
        /// </summary>
        public const float LiquidSurfaceOffsetInCells = -LiquidSurfaceDepthInPixels / 16f;

        /// <summary>
        /// 岩壁上面を基準面より上へ持ち上げる高さです。
        /// 16pxの上面に対して12pxの側面を使う1:0.75比率とし、上面、側面、内部影、岩壁上資源を同じ高さへ揃えます。
        /// </summary>
        public const int RockWallElevationInPixels = 12;

        /// <summary>
        /// 16pxを1セルとするTilemap上での岩壁表示高です。
        /// Tilemap Transformと影Meshのオフセットへ同じ値を渡し、レイヤーごとの高さずれを防ぎます。
        /// </summary>
        public const float RockWallElevationInCells = RockWallElevationInPixels / 16f;

        /// <summary>
        /// 中心セルの下層にある土地と手前セルを比較し、水・マグマへ落ちる場合だけ断面種類を返します。
        /// 岩壁セルにも土地ブロックがあるため、岩壁の手前が低地なら岩壁側面とは別に土地断面も返します。
        /// </summary>
        /// <param name="centerTile">断面を付ける候補となる中心セルです。</param>
        /// <param name="frontTile">画面手前に相当するワールドY-1のセルです。</param>
        /// <returns>描画する断面種類です。条件外ならNoneです。</returns>
        public static TerrainFrontEdgeKind ClassifyTerrainFrontEdge(
            GeneratedTile centerTile,
            GeneratedTile frontTile)
        {
            bool centerHasLandBase = centerTile.Ground != GroundType.Water
                && centerTile.Ground != GroundType.Magma;
            bool frontIsLowland = frontTile.Ground == GroundType.Water
                || frontTile.Ground == GroundType.Magma;
            if (!centerHasLandBase || !frontIsLowland)
            {
                return TerrainFrontEdgeKind.None;
            }

            // GroundType.SandはBiome移行帯でも砂面を示すため、Biomeより先に砂断面へ分類します。
            if (centerTile.Ground == GroundType.Sand || centerTile.Biome == BiomeId.Desert)
            {
                return TerrainFrontEdgeKind.Sand;
            }

            if (centerTile.Biome == BiomeId.Rockland || centerTile.Biome == BiomeId.Volcanic)
            {
                return TerrainFrontEdgeKind.Rock;
            }

            if (centerTile.Biome == BiomeId.Snowfield)
            {
                return TerrainFrontEdgeKind.Snow;
            }

            return TerrainFrontEdgeKind.Soil;
        }

        /// <summary>
        /// 岩壁の手前だけへ側面を表示するか判定します。
        /// </summary>
        /// <param name="centerTile">岩壁候補となる中心セルです。</param>
        /// <param name="frontTile">画面手前に相当するワールドY-1のセルです。</param>
        /// <returns>中心が岩壁で手前が岩壁でない場合だけtrueです。</returns>
        public static bool ShouldDisplayRockWallFrontFace(
            GeneratedTile centerTile,
            GeneratedTile frontTile)
        {
            return centerTile.Ground == GroundType.RockWall
                && frontTile.Ground != GroundType.RockWall;
        }

        /// <summary>
        /// 1セルの地形と塩分から、重ねる液面アニメーション種類を返します。
        /// </summary>
        /// <param name="tile">ECSを正とする1セル分の生成地形です。</param>
        /// <returns>淡水・海水・マグマの表示種類です。通常地形ならNoneです。</returns>
        public static LiquidAnimationKind ClassifyLiquidAnimation(GeneratedTile tile)
        {
            if (tile.Ground == GroundType.Magma)
            {
                return LiquidAnimationKind.Magma;
            }

            if (tile.Ground != GroundType.Water)
            {
                return LiquidAnimationKind.None;
            }

            return tile.WaterSalinity == WaterSalinity.FreshWater
                ? LiquidAnimationKind.FreshWater
                : LiquidAnimationKind.SaltWater;
        }
    }
}
