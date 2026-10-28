// このファイルは、製品2段Wallによる資源遮蔽の共通表示規則を定義します。
// Tilemapへ実際に描画するChunkTilemapPresenterと数値を分離し、Sceneを開かないEditModeテストでも高さ規則を検証できるようにします。

using Farmware.Simulation;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 岩壁際の通常資源を固定描画レイヤーへ振り分ける規則を定義します。
    /// すべて純粋計算であり、ECSデータ、Tilemap、アセットを変更しません。
    /// </summary>
    public static class TerrainLayerPresentationUtility
    {
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
