// このファイルは、変更差分適用後の有効セルに対する移動可否を、移動主体の能力と地形定義から純粋計算します。
// PLAYER-01以降の衝突処理はこの結果を使用し、建築可否やTilemapの見た目を歩行判定へ流用しません。

using Farmware.Simulation;
using Unity.Entities;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 移動主体が穴、液体、高温へ対応できるかを0または1で保持します。
    /// ECS ComponentやBurst Jobへ安全に渡せるよう、管理参照や可変コレクションを持ちません。
    /// </summary>
    public struct TerrainTraversalCapabilities
    {
        /// <summary>地面のない乾燥した穴を飛行等で通過できる場合は1です。</summary>
        public byte CanCrossGap;

        /// <summary>水や海水等の液体セルへ進入できる場合は1です。</summary>
        public byte CanEnterLiquid;

        /// <summary>マグマ等の耐熱必須セルへ進入できる場合は1です。</summary>
        public byte HasHeatResistance;
    }

    /// <summary>
    /// 有効セルと移動能力を評価した結果です。
    /// UIやAIが停止理由を区別できるよう、単純なboolではなく具体的な理由を返します。
    /// </summary>
    public enum TerrainTraversalResult : byte
    {
        /// <summary>地形だけを理由とする移動を許可します。</summary>
        Allowed = 0,

        /// <summary>壁が通常移動を遮っています。</summary>
        WallBlocked = 1,

        /// <summary>地面も液体もない穴を通過できません。</summary>
        GapCapabilityRequired = 2,

        /// <summary>液体へ進入する能力がありません。</summary>
        LiquidCapabilityRequired = 3,

        /// <summary>高温地形へ進入する耐熱能力がありません。</summary>
        HeatResistanceRequired = 4,

        /// <summary>セルが未登録または分類違いの地形定義を参照しています。</summary>
        TerrainDefinitionIsInvalid = 5,

        /// <summary>地面と液体が重なるなど、セル区画の組み合わせが不正です。</summary>
        TerrainCombinationIsInvalid = 6
    }

    /// <summary>
    /// 現在の有効地形と主体能力だけから、地形上の移動可否を判定します。
    /// 配置物Collider、他個体の占有、装備耐久などはPLAYER-01以降の上位検証で合成します。
    /// </summary>
    public static class TerrainTraversalRules
    {
        /// <summary>
        /// 有効セルへ進入できるかを判定します。
        /// </summary>
        /// <param name="cell">自然生成と変更差分を合成済みの現在地形です。</param>
        /// <param name="catalog">RuntimeIdを地形属性へ解決するカタログです。</param>
        /// <param name="capabilities">移動主体が持つ穴・液体・耐熱能力です。</param>
        /// <returns>許可、能力不足、定義不整合のいずれかです。</returns>
        public static TerrainTraversalResult Evaluate(
            in EffectiveTerrainCell cell,
            BlobAssetReference<TerrainBlockCatalogBlob> catalog,
            in TerrainTraversalCapabilities capabilities)
        {
            if (!TryGetOptionalDefinition(
                    catalog,
                    cell.GroundBlockRuntimeId,
                    DefinitionKind.GroundBlock,
                    out bool hasGround,
                    out _)
                || !TryGetOptionalDefinition(
                    catalog,
                    cell.WallBlockRuntimeId,
                    DefinitionKind.WallBlock,
                    out bool hasWall,
                    out TerrainBlockDefinitionBlob wall)
                || !TryGetOptionalDefinition(
                    catalog,
                    cell.LiquidRuntimeId,
                    DefinitionKind.Liquid,
                    out bool hasLiquid,
                    out TerrainBlockDefinitionBlob liquid)
                || !TryGetOptionalDefinition(
                    catalog,
                    cell.TerrainSurfaceRuntimeId,
                    DefinitionKind.TerrainSurface,
                    out bool hasSurface,
                    out _))
            {
                return TerrainTraversalResult.TerrainDefinitionIsInvalid;
            }

            if ((hasGround && hasLiquid) || (hasSurface && !hasGround))
            {
                return TerrainTraversalResult.TerrainCombinationIsInvalid;
            }

            if (hasWall &&
                (wall.Flags & TerrainBlockDefinitionFlags.BlocksMovement) != 0)
            {
                return TerrainTraversalResult.WallBlocked;
            }

            if (hasGround)
            {
                return TerrainTraversalResult.Allowed;
            }

            if (!hasLiquid)
            {
                return capabilities.CanCrossGap == 1
                    ? TerrainTraversalResult.Allowed
                    : TerrainTraversalResult.GapCapabilityRequired;
            }

            if ((liquid.Flags & TerrainBlockDefinitionFlags.RequiresHeatResistance) != 0
                && capabilities.HasHeatResistance != 1)
            {
                return TerrainTraversalResult.HeatResistanceRequired;
            }

            return capabilities.CanEnterLiquid == 1
                ? TerrainTraversalResult.Allowed
                : TerrainTraversalResult.LiquidCapabilityRequired;
        }

        /// <summary>Invalidを区画なしとして許可し、有効IDだけを期待分類と照合します。</summary>
        /// <param name="catalog">RuntimeIdを解決する地形定義カタログです。</param>
        /// <param name="runtimeId">区画が参照する定義ID、または区画なしのInvalidです。</param>
        /// <param name="expectedKind">区画が要求する定義分類です。</param>
        /// <param name="hasDefinition">区画に有効な定義IDがある場合はtrueです。</param>
        /// <param name="definition">成功時に解決した属性、区画なしならdefaultです。</param>
        /// <returns>区画なし、または期待分類の登録済み定義ならtrueです。</returns>
        private static bool TryGetOptionalDefinition(
            BlobAssetReference<TerrainBlockCatalogBlob> catalog,
            DefinitionRuntimeId runtimeId,
            DefinitionKind expectedKind,
            out bool hasDefinition,
            out TerrainBlockDefinitionBlob definition)
        {
            hasDefinition = runtimeId.IsValid;
            definition = default;
            if (!hasDefinition)
            {
                return true;
            }

            return TerrainBlockCatalogLookup.TryGetDefinition(
                    catalog,
                    runtimeId,
                    out definition)
                && definition.Kind == expectedKind;
        }
    }
}
