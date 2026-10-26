// このファイルは、変更差分適用後の有効セルと配置したい対象を組み合わせ、地形だけの配置可否を純粋計算します。
// 後続の建築Command、初期地点探索、開発者用可視化が同じ判定を共有し、BiomeやTilemapから可否を推測しないための境界です。

using Farmware.Simulation;
using Unity.Entities;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 地形上へ直接配置したい対象の分類です。
    /// 建築Commandと配置プレビューが、設備・ベルト・橋で同じ地形判定APIを使用するために指定します。
    /// </summary>
    public enum TerrainPlacementKind : byte
    {
        /// <summary>配置対象が未指定の状態です。共通判定では許可しません。</summary>
        None = 0,

        /// <summary>中央コアを含む、通常地面へ置く設備です。</summary>
        NormalEquipment = 1,

        /// <summary>橋を介さず、通常地面へ直接置くベルトです。</summary>
        NormalBelt = 2,

        /// <summary>水域またはマグマの上へ基礎レイヤーとして置く橋です。</summary>
        Bridge = 3
    }

    /// <summary>
    /// 地形属性だけを評価した配置判定結果です。
    /// 資材不足、既設物との占有競合、未解禁などは含めず、後続の建築Commandが別の検証結果と組み合わせます。
    /// </summary>
    public enum TerrainPlacementResult : byte
    {
        /// <summary>現在の共通地形判定が扱わない配置対象です。</summary>
        UnsupportedPlacementKind = 0,

        /// <summary>指定した対象を、この地形へ配置できます。</summary>
        Allowed = 1,

        /// <summary>地形が指定した対象の配置を許可していません。</summary>
        TerrainDoesNotAllowPlacement = 2,

        /// <summary>橋は配置できますが、橋定義に耐熱性が必要です。</summary>
        HeatResistanceRequired = 3,

        /// <summary>有効セルが未登録または分類違いの地形定義を参照しています。</summary>
        TerrainDefinitionIsInvalid = 4,

        /// <summary>地面と液体が同時に存在するなど、有効セルの区画構成が不正です。</summary>
        TerrainCombinationIsInvalid = 5
    }

    /// <summary>
    /// 地形Definitionへ記録済みの正方向属性から、通常設備・通常ベルト・橋の配置可否を判定します。
    /// Entity、Tilemap、生成データを変更しないため、Simulation、Presentation、テストから同じ結果を再利用できます。
    /// </summary>
    public static class TerrainPlacementRules
    {
        /// <summary>
        /// 変更差分適用後の有効セルへ対象を配置できるか、地形区画と定義属性だけから判定します。
        /// 自然資源、配置物、所持品、到達距離は別のCommand検証で合成し、この結果だけで最終確定しません。
        /// </summary>
        /// <param name="cell">自然生成と変更差分を合成済みの現在地形です。</param>
        /// <param name="catalog">各RuntimeIdの分類と配置属性を保持する地形定義カタログです。</param>
        /// <param name="placementKind">通常設備、通常ベルト、橋のいずれかです。</param>
        /// <param name="hasHeatResistance">橋定義が耐熱性を持つ場合はtrueです。</param>
        /// <returns>地形だけを理由とする許可または具体的な拒否理由です。</returns>
        public static TerrainPlacementResult Evaluate(
            in EffectiveTerrainCell cell,
            BlobAssetReference<TerrainBlockCatalogBlob> catalog,
            TerrainPlacementKind placementKind,
            bool hasHeatResistance = false)
        {
            if (!TryGetOptionalDefinition(
                    catalog,
                    cell.GroundBlockRuntimeId,
                    DefinitionKind.GroundBlock,
                    out bool hasGround,
                    out TerrainBlockDefinitionBlob ground)
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
                return TerrainPlacementResult.TerrainDefinitionIsInvalid;
            }

            if ((hasGround && hasLiquid) || (hasSurface && !hasGround))
            {
                return TerrainPlacementResult.TerrainCombinationIsInvalid;
            }

            // 壁があるセルへ地面設備や橋を重ねず、壁の撤去を別Commandとして明示させます。
            if (hasWall &&
                (wall.Flags & TerrainBlockDefinitionFlags.BlocksMovement) != 0)
            {
                return TerrainPlacementResult.TerrainDoesNotAllowPlacement;
            }

            switch (placementKind)
            {
                case TerrainPlacementKind.NormalEquipment:
                    return !hasGround || hasLiquid
                        ? TerrainPlacementResult.TerrainDoesNotAllowPlacement
                        : EvaluateRequiredFlag(
                            ground.Flags,
                            TerrainBlockDefinitionFlags.AllowsNormalEquipment);
                case TerrainPlacementKind.NormalBelt:
                    return !hasGround || hasLiquid
                        ? TerrainPlacementResult.TerrainDoesNotAllowPlacement
                        : EvaluateRequiredFlag(
                            ground.Flags,
                            TerrainBlockDefinitionFlags.AllowsNormalBelt);
                case TerrainPlacementKind.Bridge:
                    if (hasGround)
                    {
                        return TerrainPlacementResult.TerrainDoesNotAllowPlacement;
                    }

                    // 液体のない穴は橋を許可し、液体セルは定義属性と耐熱要件を追加確認します。
                    if (!hasLiquid)
                    {
                        return TerrainPlacementResult.Allowed;
                    }

                    if ((liquid.Flags & TerrainBlockDefinitionFlags.AllowsBridge) == 0)
                    {
                        return TerrainPlacementResult.TerrainDoesNotAllowPlacement;
                    }

                    return (liquid.Flags & TerrainBlockDefinitionFlags.RequiresHeatResistance) != 0
                        && !hasHeatResistance
                            ? TerrainPlacementResult.HeatResistanceRequired
                            : TerrainPlacementResult.Allowed;
                default:
                    return TerrainPlacementResult.UnsupportedPlacementKind;
            }
        }

        /// <summary>地形定義属性版の必須flag判定です。</summary>
        /// <param name="flags">対象地形定義が持つ全属性です。</param>
        /// <param name="requiredFlag">配置を許可するために必要な1属性です。</param>
        /// <returns>必須属性があればAllowed、なければTerrainDoesNotAllowPlacementです。</returns>
        private static TerrainPlacementResult EvaluateRequiredFlag(
            TerrainBlockDefinitionFlags flags,
            TerrainBlockDefinitionFlags requiredFlag)
        {
            return (flags & requiredFlag) != 0
                ? TerrainPlacementResult.Allowed
                : TerrainPlacementResult.TerrainDoesNotAllowPlacement;
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
