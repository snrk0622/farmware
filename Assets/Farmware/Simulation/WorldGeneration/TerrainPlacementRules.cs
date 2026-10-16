// このファイルは、生成済みタイルの配置属性と配置したい対象を組み合わせ、地形だけを理由とする配置可否を純粋計算します。
// 後続の建築Command、初期設備領域探索、開発者用可視化が同じ判定を共有し、GroundTypeを個別に再解釈しないための境界です。

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
        HeatResistanceRequired = 3
    }

    /// <summary>
    /// GeneratedTileへ記録済みの正方向属性から、通常設備・通常ベルト・橋の配置可否を判定します。
    /// Entity、Tilemap、生成データを変更しないため、Simulation、Presentation、テストから同じ結果を再利用できます。
    /// </summary>
    public static class TerrainPlacementRules
    {
        /// <summary>
        /// 生成済みタイルへ対象を配置できるか、地形属性だけから判定します。
        /// </summary>
        /// <param name="tile">決定論的生成器または生成済みチャンクから読み取った基礎タイルです。</param>
        /// <param name="placementKind">配置したい通常設備、通常ベルト、橋のいずれかです。</param>
        /// <param name="hasHeatResistance">橋定義が耐熱性を持つ場合はtrueです。橋以外では参照しません。</param>
        /// <returns>許可、地形不可、耐熱不足、未対応種別のいずれかです。</returns>
        public static TerrainPlacementResult Evaluate(
            in GeneratedTile tile,
            TerrainPlacementKind placementKind,
            bool hasHeatResistance = false)
        {
            return Evaluate(tile.GameplayFlags, placementKind, hasHeatResistance);
        }

        /// <summary>
        /// タイルから取り出した配置属性へ対象を適用し、地形だけを理由とする配置可否を判定します。
        /// Hot Pathでも使用できるよう、管理参照や文字列を使わず値型だけで完結させます。
        /// </summary>
        /// <param name="gameplayFlags">生成済みタイルが保持する正方向の配置属性です。</param>
        /// <param name="placementKind">配置したい通常設備、通常ベルト、橋のいずれかです。</param>
        /// <param name="hasHeatResistance">橋定義が耐熱性を持つ場合はtrueです。橋以外では参照しません。</param>
        /// <returns>許可、地形不可、耐熱不足、未対応種別のいずれかです。</returns>
        public static TerrainPlacementResult Evaluate(
            TileGameplayFlags gameplayFlags,
            TerrainPlacementKind placementKind,
            bool hasHeatResistance = false)
        {
            switch (placementKind)
            {
                case TerrainPlacementKind.NormalEquipment:
                    return EvaluateRequiredFlag(
                        gameplayFlags,
                        TileGameplayFlags.AllowsNormalEquipment);
                case TerrainPlacementKind.NormalBelt:
                    return EvaluateRequiredFlag(
                        gameplayFlags,
                        TileGameplayFlags.AllowsNormalBelt);
                case TerrainPlacementKind.Bridge:
                    // 橋を置けない地形では、耐熱性の有無を調べる前に地形不可として返します。
                    if ((gameplayFlags & TileGameplayFlags.AllowsBridge) == 0)
                    {
                        return TerrainPlacementResult.TerrainDoesNotAllowPlacement;
                    }

                    // マグマのように耐熱性を要求する地形だけ、橋定義側の能力を追加で検証します。
                    if ((gameplayFlags & TileGameplayFlags.RequiresHeatResistance) != 0
                        && !hasHeatResistance)
                    {
                        return TerrainPlacementResult.HeatResistanceRequired;
                    }

                    return TerrainPlacementResult.Allowed;
                default:
                    // 予約値や破損した入力を許可へ倒すと未知の建築物を置けるため、明示的な未対応結果を返します。
                    return TerrainPlacementResult.UnsupportedPlacementKind;
            }
        }

        /// <summary>
        /// 通常設備または通常ベルトに必要な1つの正方向属性を確認します。
        /// </summary>
        /// <param name="gameplayFlags">生成済みタイルが保持する配置属性です。</param>
        /// <param name="requiredFlag">対象を許可するために必要な1つの属性です。</param>
        /// <returns>必要属性があればAllowed、なければTerrainDoesNotAllowPlacementです。</returns>
        private static TerrainPlacementResult EvaluateRequiredFlag(
            TileGameplayFlags gameplayFlags,
            TileGameplayFlags requiredFlag)
        {
            return (gameplayFlags & requiredFlag) != 0
                ? TerrainPlacementResult.Allowed
                : TerrainPlacementResult.TerrainDoesNotAllowPlacement;
        }
    }
}
