// このファイルは、通常生成済みタイルの適性と用途別ノイズから基礎資源を決定論的に配置します。
// 地形生成順やチャンク境界に依存せず、木・石・砂をワールド全体へ同じ規則で群生させます。
// 水はGeneratedResourceTileへ配置せず、GeneratedTileの淡水・海水属性そのものを採水源として扱います。

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 1タイルの地形適性を尊重しながら、種類ごとに独立した低周波ノイズで基礎資源を選びます。
    /// UnityEngine.Randomや共有乱数状態を使わないため、Burst Jobと初期位置探索から同じ結果を得られます。
    /// </summary>
    public static class DeterministicResourceGenerator
    {
        /// <summary>
        /// 指定ワールドタイルへ配置する基礎資源と表示差分を生成します。
        /// </summary>
        /// <param name="worldSettings">シードと生成器バージョンを保持するワールド設定です。</param>
        /// <param name="resourceSettings">群れサイズと種類別密度を保持する資源設定です。</param>
        /// <param name="terrainTile">同じ座標ですでに生成された地形・資源適性です。</param>
        /// <param name="worldTileX">符号付きワールド水平タイル座標です。</param>
        /// <param name="worldTileY">符号付きワールド垂直タイル座標です。</param>
        /// <returns>配置なしを含む1タイル分の資源データです。</returns>
        public static GeneratedResourceTile GenerateResource(
            in WorldGenerationSettings worldSettings,
            in ResourceGenerationSettings resourceSettings,
            in GeneratedTile terrainTile,
            int worldTileX,
            int worldTileY)
        {
            GeneratedResourceType selectedType = GeneratedResourceType.None;
            int selectedScore = -1;

            // 種類ごとの候補形状は独立ノイズで保ちます。候補同士が重なるセルでは、閾値超過量が最大の1種類だけを選びます。
            TrySelectResource(
                ResourceSuitability.Tree,
                GeneratedResourceType.Tree,
                WorldGenerationPurposeSalts.TreeResource,
                resourceSettings.TreeThreshold,
                terrainTile.ResourceSuitability,
                worldSettings,
                resourceSettings,
                worldTileX,
                worldTileY,
                ref selectedType,
                ref selectedScore);
            TrySelectResource(
                ResourceSuitability.Stone,
                GeneratedResourceType.Stone,
                WorldGenerationPurposeSalts.StoneResource,
                resourceSettings.StoneThreshold,
                terrainTile.ResourceSuitability,
                worldSettings,
                resourceSettings,
                worldTileX,
                worldTileY,
                ref selectedType,
                ref selectedScore);
            TrySelectResource(
                ResourceSuitability.Sand,
                GeneratedResourceType.Sand,
                WorldGenerationPurposeSalts.SandResource,
                resourceSettings.SandThreshold,
                terrainTile.ResourceSuitability,
                worldSettings,
                resourceSettings,
                worldTileX,
                worldTileY,
                ref selectedType,
                ref selectedScore);

            if (selectedType == GeneratedResourceType.None)
            {
                return default;
            }

            ulong variantHash = WorldSeedUtility.HashCoordinate(
                worldSettings.WorldSeed,
                worldSettings.GeneratorVersion,
                worldTileX,
                worldTileY,
                WorldGenerationPurposeSalts.ResourceVisualVariant,
                (uint)selectedType);
            return new GeneratedResourceTile
            {
                Type = selectedType,
                VisualVariant = (byte)(variantHash & 3UL)
            };
        }

        /// <summary>
        /// 1種類の地形適性と群生ノイズを評価し、現在候補より閾値超過量が大きい場合だけ置き換えます。
        /// </summary>
        /// <param name="requiredSuitability">対象資源を置ける地形適性フラグです。</param>
        /// <param name="resourceType">条件を満たした場合に選ぶ安定資源IDです。</param>
        /// <param name="purposeSalt">他資源から独立したノイズを得る用途別ソルトです。</param>
        /// <param name="threshold">この値以上で配置候補になる0〜65535の下限です。</param>
        /// <param name="terrainSuitability">生成済み地形が持つ資源適性フラグです。</param>
        /// <param name="worldSettings">ワールドシードと生成器バージョンです。</param>
        /// <param name="resourceSettings">ノイズ格子サイズとオクターブ数です。</param>
        /// <param name="worldTileX">水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">垂直ワールドタイル座標です。</param>
        /// <param name="selectedType">現在選ばれている資源IDです。より強い候補なら更新します。</param>
        /// <param name="selectedScore">現在候補の閾値超過量です。より強い候補なら更新します。</param>
        private static void TrySelectResource(
            ResourceSuitability requiredSuitability,
            GeneratedResourceType resourceType,
            ulong purposeSalt,
            ushort threshold,
            ResourceSuitability terrainSuitability,
            in WorldGenerationSettings worldSettings,
            in ResourceGenerationSettings resourceSettings,
            int worldTileX,
            int worldTileY,
            ref GeneratedResourceType selectedType,
            ref int selectedScore)
        {
            if ((terrainSuitability & requiredSuitability) == 0)
            {
                return;
            }

            ushort sample = DeterministicNoise.SampleFractalNoise(
                worldSettings.WorldSeed,
                worldSettings.GeneratorVersion,
                worldTileX,
                worldTileY,
                resourceSettings.ClusterCellSizeInTiles,
                resourceSettings.OctaveCount,
                purposeSalt);
            if (sample < threshold)
            {
                return;
            }

            int score = sample - threshold;
            if (score > selectedScore)
            {
                selectedType = resourceType;
                selectedScore = score;
            }
        }

        /// <summary>
        /// 1つの資源IDを初期位置探索用のビットフラグへ変換します。
        /// </summary>
        /// <param name="resourceType">変換する基礎資源の安定IDです。</param>
        /// <returns>対応する1bit、または配置なしの場合はNoneです。</returns>
        public static NaturalResourceTypeMask ToMask(GeneratedResourceType resourceType)
        {
            switch (resourceType)
            {
                case GeneratedResourceType.Tree:
                    return NaturalResourceTypeMask.Tree;
                case GeneratedResourceType.Stone:
                    return NaturalResourceTypeMask.Stone;
                case GeneratedResourceType.Sand:
                    return NaturalResourceTypeMask.Sand;
                default:
                    return NaturalResourceTypeMask.None;
            }
        }
    }
}
