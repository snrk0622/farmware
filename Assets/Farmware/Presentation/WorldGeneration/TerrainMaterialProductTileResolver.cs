// このファイルは、Runtime ID、絶対座標、断面段から製品表示に使うSpriteとTintを解決します。
// カタログ完全性と決定論的差分選択を1か所で組み合わせ、Presenterが別材質へフォールバックすることを防ぎます。

using Farmware.Simulation;
using UnityEngine;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 製品Tileへ設定する完成Spriteと頂点Tintを保持します。
    /// TileはSpriteとTintの組ごとに再利用し、セルごとには生成しません。
    /// </summary>
    public readonly struct TerrainMaterialProductTileSelection
    {
        /// <summary>製品カタログから選択した16×16 px完成Spriteです。</summary>
        public Sprite Sprite { get; }

        /// <summary>Wall暗色化または乾燥崖の段上端Alphaを含むTile Tintです。</summary>
        public Color Color { get; }

        /// <summary>選択済みSpriteとTintを変更不能な値として保持します。</summary>
        /// <param name="sprite">選択した完成Spriteです。</param>
        /// <param name="color">Tileへ設定するRGBA Tintです。</param>
        public TerrainMaterialProductTileSelection(Sprite sprite, Color color)
        {
            Sprite = sprite;
            Color = color;
        }
    }

    /// <summary>
    /// 完全な製品カタログとRuntime検索表から、Ground／Wallの上面・断面表示を解決します。
    /// 入力不正時はfalseを返し、仮Spriteや別材質を選びません。
    /// </summary>
    public static class TerrainMaterialProductTileResolver
    {
        /// <summary>
        /// GroundまたはWall上面の共有Spriteを選び、Wallだけへ材質設定の暗色化を適用します。
        /// </summary>
        /// <param name="catalog">41材質を完全登録した製品表示カタログです。</param>
        /// <param name="runtimeLookup">ECS Runtime IDから安定材質IDを引く検索表です。</param>
        /// <param name="worldSeed">保存済みの64bitワールドシードです。</param>
        /// <param name="coordinate">上面元セルの絶対座標です。</param>
        /// <param name="runtimeId">GroundまたはWallのRuntime IDです。</param>
        /// <param name="forWall">Wall上面ならtrue、Ground上面ならfalseです。</param>
        /// <param name="selection">成功時に共有Spriteと用途別Tintを受け取ります。</param>
        /// <returns>Runtime IDとカタログ行を解決し、候補を選択できた場合はtrueです。</returns>
        public static bool TryResolveTop(
            TerrainMaterialPresentationCatalog catalog,
            TerrainMaterialPresentationRuntimeLookup runtimeLookup,
            ulong worldSeed,
            WorldTileCoordinate coordinate,
            DefinitionRuntimeId runtimeId,
            bool forWall,
            out TerrainMaterialProductTileSelection selection)
        {
            selection = default;
            if (!TryResolveDefinition(
                    catalog,
                    runtimeLookup,
                    runtimeId,
                    forWall,
                    out TerrainMaterialPresentationDefinition definition)
                || !TerrainMaterialSpriteVariantSelector.TrySelectGroundTopIndex(
                    worldSeed,
                    coordinate,
                    definition.StableId,
                    definition.GroundTopSpriteCount,
                    out int variantIndex))
            {
                return false;
            }

            float tint = forWall && definition.DarkenWallSurface
                ? definition.WallDarkenFactor
                : 1f;
            selection = new TerrainMaterialProductTileSelection(
                definition.GetGroundTopSprite(variantIndex),
                new Color(tint, tint, tint, 1f));
            return true;
        }

        /// <summary>
        /// Ground崖面またはWall前面のSpriteを選び、1段目だけ用途別の境界合成済みSpriteへ置き換えます。
        /// </summary>
        /// <param name="catalog">41材質を完全登録した製品表示カタログです。</param>
        /// <param name="runtimeLookup">ECS Runtime IDから安定材質IDを引く検索表です。</param>
        /// <param name="worldSeed">保存済みの64bitワールドシードです。</param>
        /// <param name="sourceCoordinate">断面元セルの絶対座標です。</param>
        /// <param name="runtimeId">投影元GroundまたはWallのRuntime IDです。</param>
        /// <param name="faceDepth">断面上端を0とする段数です。</param>
        /// <param name="forWall">Wall前面ならtrue、Ground崖面ならfalseです。</param>
        /// <param name="selection">成功時に完成Spriteと用途別Tintを受け取ります。</param>
        /// <returns>Runtime ID、カタログ行、Face候補を解決できた場合はtrueです。</returns>
        public static bool TryResolveFace(
            TerrainMaterialPresentationCatalog catalog,
            TerrainMaterialPresentationRuntimeLookup runtimeLookup,
            ulong worldSeed,
            WorldTileCoordinate sourceCoordinate,
            DefinitionRuntimeId runtimeId,
            int faceDepth,
            bool forWall,
            out TerrainMaterialProductTileSelection selection)
        {
            selection = default;
            if (faceDepth < 0
                || !TryResolveDefinition(
                    catalog,
                    runtimeLookup,
                    runtimeId,
                    forWall,
                    out TerrainMaterialPresentationDefinition definition)
                || !TerrainMaterialSpriteVariantSelector.TrySelectFaceIndex(
                    worldSeed,
                    sourceCoordinate,
                    definition.StableId,
                    faceDepth,
                    definition.FaceSpriteCount,
                    out int faceVariantIndex))
            {
                return false;
            }

            Sprite selectedSprite = definition.GetFaceSprite(faceVariantIndex);
            if (faceDepth == 0 && definition.HasFaceBoundary)
            {
                if (!TerrainMaterialSpriteVariantSelector.TrySelectBoundaryIndex(
                        worldSeed,
                        sourceCoordinate,
                        definition.StableId,
                        faceDepth,
                        definition.FaceBoundaryVariantCount,
                        out int boundaryVariantIndex))
                {
                    return false;
                }

                // 境界OverlayはEditorで用途別に合成済みなので、Runtimeでは完成Spriteを1枚だけ選びます。
                selectedSprite = definition.GetBoundaryFaceSprite(
                    faceVariantIndex,
                    boundaryVariantIndex,
                    forWall);
            }

            float alpha = forWall
                ? 1f
                : TerrainMaterialVerticalProjectionUtility.GetDryCliffStageStartAlpha(faceDepth);
            if (alpha <= 0f)
            {
                return false;
            }

            selection = new TerrainMaterialProductTileSelection(
                selectedSprite,
                new Color(1f, 1f, 1f, alpha));
            return true;
        }

        /// <summary>用途別Runtime IDから、対応する製品表示定義をフォールバックなしで取得します。</summary>
        /// <param name="catalog">完全な製品表示カタログです。</param>
        /// <param name="runtimeLookup">役割別Runtime検索表です。</param>
        /// <param name="runtimeId">GroundまたはWall Runtime IDです。</param>
        /// <param name="forWall">Wall検索ならtrueです。</param>
        /// <param name="definition">成功時に安定材質ID対応の表示定義を受け取ります。</param>
        /// <returns>検索表と製品カタログの両方に対応がある場合はtrueです。</returns>
        private static bool TryResolveDefinition(
            TerrainMaterialPresentationCatalog catalog,
            TerrainMaterialPresentationRuntimeLookup runtimeLookup,
            DefinitionRuntimeId runtimeId,
            bool forWall,
            out TerrainMaterialPresentationDefinition definition)
        {
            definition = null;
            if (catalog == null || runtimeLookup == null)
            {
                return false;
            }

            TerrainBlockMaterialId stableId;
            bool resolved = forWall
                ? runtimeLookup.TryResolveWall(runtimeId, out stableId)
                : runtimeLookup.TryResolveGround(runtimeId, out stableId);
            return resolved && catalog.TryGetDefinition(stableId, out definition);
        }
    }
}
