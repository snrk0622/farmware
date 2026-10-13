// このファイルは、16×24の土地ブロック、16×28の岩壁ブロック、液面アニメーションに共通する表示寸法を定義します。
// Tilemapへ実際に描画するChunkTilemapPresenterと数値を分離し、Sceneを開かないEditModeテストでも高さ規則を検証できるようにします。

namespace Farmware.WorldGeneration.Presentation
{
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
    /// 一体型ブロックSpriteの寸法と、GeneratedTileに対応する液面オーバーレイを定義します。
    /// すべて純粋計算であり、ECSデータ、Tilemap、アセットを変更しません。
    /// </summary>
    public static class TerrainLayerPresentationUtility
    {
        /// <summary>
        /// 土地と岩壁で共通に使用する上面の高さです。
        /// 16 PPUのため、上面は論理セル1個分と正確に一致します。
        /// </summary>
        public const int TerrainSurfaceHeightInPixels = 16;

        /// <summary>
        /// 土地Spriteへ一体化する前面断面の高さです。
        /// 水・マグマより土地が8px高く見えるよう、上面の下へ追加します。
        /// </summary>
        public const int TerrainFrontFaceHeightInPixels = 8;

        /// <summary>
        /// 土地上面16pxと前面断面8pxを結合したSprite全体の高さです。
        /// </summary>
        public const int CombinedTerrainSpriteHeightInPixels =
            TerrainSurfaceHeightInPixels + TerrainFrontFaceHeightInPixels;

        /// <summary>
        /// 16×24土地Spriteを論理セルへ合わせる正規化Pivot Yです。
        /// 下端から16pxのPivotをセル中央へ置くことで、上面と断面の境界をセル下端へ合わせます。
        /// </summary>
        public const float CombinedTerrainPivotY =
            TerrainSurfaceHeightInPixels / (float)CombinedTerrainSpriteHeightInPixels;

        /// <summary>
        /// 水・マグマの上面を土地より画面下へ投影するピクセル数です。
        /// 16pxセルの半分だけ下げることで、土地断面の下端から液面を開始し、液面下辺は手前の土地に隠れる構図にします。
        /// </summary>
        public const int LiquidSurfaceDepthInPixels = 8;

        /// <summary>
        /// 16pxを1セルとするTilemap上での液面表示オフセットです。
        /// 画面下方向へ移動するため負値とし、静止液面とアニメーションの両Tilemapへ同じ値を適用します。
        /// </summary>
        public const float LiquidSurfaceOffsetInCells =
            -LiquidSurfaceDepthInPixels / (float)TerrainSurfaceHeightInPixels;

        /// <summary>
        /// 岩壁上面を基準面より上へ持ち上げる高さです。
        /// 16pxの上面に対して12pxの側面を使う1:0.75比率とし、上面、側面、内部影、岩壁上資源を同じ高さへ揃えます。
        /// </summary>
        public const int RockWallElevationInPixels = 12;

        /// <summary>
        /// 岩壁上面16pxと側面12pxを結合したSprite全体の高さです。
        /// </summary>
        public const int CombinedRockWallSpriteHeightInPixels =
            TerrainSurfaceHeightInPixels + RockWallElevationInPixels;

        /// <summary>
        /// 16×28岩壁Spriteを基準面の論理セルへ合わせる正規化Pivot Yです。
        /// 下端から8pxの位置をセル中央へ置くことで、側面は基準面から上へ12px、上面はその上へ表示されます。
        /// </summary>
        public const float CombinedRockWallPivotY =
            TerrainFrontFaceHeightInPixels / (float)CombinedRockWallSpriteHeightInPixels;

        /// <summary>
        /// 16pxを1セルとするTilemap上での岩壁表示高です。
        /// Tilemap Transformと影Meshのオフセットへ同じ値を渡し、レイヤーごとの高さずれを防ぎます。
        /// </summary>
        public const float RockWallElevationInCells =
            RockWallElevationInPixels / (float)TerrainSurfaceHeightInPixels;

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
