// このファイルは、旧一体型地形の寸法、液面アニメーション、製品2段Wallによる資源遮蔽の共通表示規則を定義します。
// Tilemapへ実際に描画するChunkTilemapPresenterと数値を分離し、Sceneを開かないEditModeテストでも高さ規則を検証できるようにします。

using Farmware.Simulation;
using UnityEngine;

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
    /// 一体型ブロックSpriteの寸法、液面オーバーレイ、岩壁際の通常資源を固定描画レイヤーへ振り分ける規則を定義します。
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
        /// 1セルのLiquid Runtime IDから、重ねる液面アニメーション種類を返します。
        /// </summary>
        /// <param name="terrainCell">ECSを正とする1セル分の自然生成Blockセルです。</param>
        /// <param name="runtimeIds">淡水、海水、マグマを識別する組み込みIDです。</param>
        /// <returns>淡水・海水・マグマの表示種類です。通常地形ならNoneです。</returns>
        public static LiquidAnimationKind ClassifyLiquidAnimation(
            in GeneratedTerrainCell terrainCell,
            in BuiltInTerrainBlockRuntimeIds runtimeIds)
        {
            if (terrainCell.LiquidRuntimeId.IsValid
                && terrainCell.LiquidRuntimeId.Value == runtimeIds.Magma.Value)
            {
                return LiquidAnimationKind.Magma;
            }

            if (terrainCell.LiquidRuntimeId.IsValid
                && terrainCell.LiquidRuntimeId.Value == runtimeIds.FreshWater.Value)
            {
                return LiquidAnimationKind.FreshWater;
            }

            return terrainCell.LiquidRuntimeId.IsValid
                && terrainCell.LiquidRuntimeId.Value == runtimeIds.SaltWater.Value
                ? LiquidAnimationKind.SaltWater
                : LiquidAnimationKind.None;
        }

        /// <summary>
        /// 通常地面の資源を、画面手前にある2段Wallより後ろへ描画する必要があるか判定します。
        /// </summary>
        /// <param name="resourceTile">現在セルの資源データです。配置なしなら背面へ分けません。</param>
        /// <param name="currentTerrainCell">資源の接地セルです。岩壁上資源は専用レイヤーを使うため対象外です。</param>
        /// <param name="nearFrontTerrainCell">画面手前方向の1セル目、ワールドYが1小さいセルです。</param>
        /// <param name="farFrontTerrainCell">画面手前方向の2セル目、ワールドYが2小さいセルです。</param>
        /// <returns>通常地面に資源があり、手前2セルのどちらかがWall根元ならtrueです。</returns>
        public static bool ShouldRenderResourceBehindRockWall(
            GeneratedResourceTile resourceTile,
            GeneratedTerrainCell currentTerrainCell,
            GeneratedTerrainCell nearFrontTerrainCell,
            GeneratedTerrainCell farFrontTerrainCell)
        {
            return resourceTile.Type != GeneratedResourceType.None
                && !currentTerrainCell.WallBlockRuntimeId.IsValid
                && (nearFrontTerrainCell.WallBlockRuntimeId.IsValid
                    || farFrontTerrainCell.WallBlockRuntimeId.IsValid);
        }

    }
}
