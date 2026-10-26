// このファイルは、ECSのGround／Wall Runtime IDを、表示カタログの安定材質IDへ高速に解決します。
// Simulationの材質Blobを表示開始時に一度だけコピーし、セル描画ごとの41件線形走査やGround／Wall間のフォールバックを避けます。

using System;
using System.Collections.Generic;
using Farmware.Simulation;
using Unity.Entities;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// GroundまたはWallのRuntime IDと、対応する安定材質IDの検証済み1組です。
    /// Runtime ID昇順配列へ格納し、地形セル描画時の二分探索に使用します。
    /// </summary>
    public readonly struct TerrainMaterialPresentationRuntimeBinding
    {
        /// <summary>Definitionカタログの寿命中だけ有効な1以上の実行時IDです。</summary>
        public readonly int RuntimeIdValue;

        /// <summary>表示カタログと結び付ける1〜41の変更禁止材質IDです。</summary>
        public readonly TerrainBlockMaterialId StableId;

        /// <summary>
        /// 1つの実行時IDと安定材質IDを結び付けます。
        /// </summary>
        /// <param name="runtimeIdValue">1以上のGroundまたはWall実行時IDです。</param>
        /// <param name="stableId">対応する1〜41の安定材質IDです。</param>
        public TerrainMaterialPresentationRuntimeBinding(
            int runtimeIdValue,
            TerrainBlockMaterialId stableId)
        {
            RuntimeIdValue = runtimeIdValue;
            StableId = stableId;
        }
    }

    /// <summary>
    /// Ground／Wall Runtime IDから安定材質IDへ解決する、製品表示専用の不変検索表です。
    /// ChunkTilemapPresenterなどの表示側がECS Blobの登録順やGUIDを推測せずに使用します。
    /// </summary>
    public sealed class TerrainMaterialPresentationRuntimeLookup
    {
        /// <summary>Ground Runtime ID昇順の41件検索表です。</summary>
        private readonly TerrainMaterialPresentationRuntimeBinding[] groundBindings;

        /// <summary>Wall Runtime ID昇順の41件検索表です。</summary>
        private readonly TerrainMaterialPresentationRuntimeBinding[] wallBindings;

        /// <summary>
        /// 検証と整列を終えたGround／Wall検索表を保持します。
        /// </summary>
        /// <param name="groundBindings">Ground Runtime ID昇順の41件です。</param>
        /// <param name="wallBindings">Wall Runtime ID昇順の41件です。</param>
        private TerrainMaterialPresentationRuntimeLookup(
            TerrainMaterialPresentationRuntimeBinding[] groundBindings,
            TerrainMaterialPresentationRuntimeBinding[] wallBindings)
        {
            this.groundBindings = groundBindings;
            this.wallBindings = wallBindings;
        }

        /// <summary>
        /// ECSへ公開された41材質Blobから、製品表示用のRuntime ID検索表を構築します。
        /// </summary>
        /// <param name="catalog">Bakerが構築した材質対応Blobです。</param>
        /// <param name="lookup">成功時に独立したManaged検索表を受け取ります。</param>
        /// <param name="error">失敗時に不完全または重複した対応の理由を受け取ります。</param>
        /// <returns>41材質のGround／Wall対応をフォールバックなしで構築できた場合はtrueです。</returns>
        public static bool TryCreate(
            BlobAssetReference<TerrainBlockMaterialCatalogBlob> catalog,
            out TerrainMaterialPresentationRuntimeLookup lookup,
            out string error)
        {
            lookup = null;
            if (!catalog.IsCreated)
            {
                error = "地形材質Blobが未作成です。";
                return false;
            }

            ref TerrainBlockMaterialCatalogBlob root = ref catalog.Value;
            var definitions = new TerrainBlockMaterialDefinitionBlob[root.Definitions.Length];
            for (int definitionIndex = 0;
                 definitionIndex < root.Definitions.Length;
                 definitionIndex++)
            {
                definitions[definitionIndex] = root.Definitions[definitionIndex];
            }

            return TryCreate(definitions, out lookup, out error);
        }

        /// <summary>
        /// 解決済み材質行の配列から、製品表示用のRuntime ID検索表を構築します。
        /// Editor検証とEditModeテストは一時Blobを作らず、同じ検証規則を直接確認できます。
        /// </summary>
        /// <param name="definitions">登録順を問わない41材質分の解決済み対応です。</param>
        /// <param name="lookup">成功時にRuntime ID昇順の検索表を受け取ります。</param>
        /// <param name="error">失敗時に不正ID、欠落、重複の理由を受け取ります。</param>
        /// <returns>全41材質が完全かつ一意ならtrueです。</returns>
        public static bool TryCreate(
            IReadOnlyList<TerrainBlockMaterialDefinitionBlob> definitions,
            out TerrainMaterialPresentationRuntimeLookup lookup,
            out string error)
        {
            lookup = null;
            if (definitions == null
                || definitions.Count != TerrainBlockMaterialDefinitions.Count)
            {
                int count = definitions?.Count ?? 0;
                error = $"Runtime表示検索表には41材質が必要ですが、現在は{count}件です。";
                return false;
            }

            var ground =
                new TerrainMaterialPresentationRuntimeBinding[TerrainBlockMaterialDefinitions.Count];
            var wall =
                new TerrainMaterialPresentationRuntimeBinding[TerrainBlockMaterialDefinitions.Count];
            var foundStableIds = new bool[TerrainBlockMaterialDefinitions.Count];
            var foundRuntimeIds = new HashSet<int>();

            for (int definitionIndex = 0;
                 definitionIndex < definitions.Count;
                 definitionIndex++)
            {
                TerrainBlockMaterialDefinitionBlob definition = definitions[definitionIndex];
                int stableIndex = (int)definition.StableId
                    - (int)TerrainBlockMaterialDefinitions.First;
                if (stableIndex < 0
                    || stableIndex >= TerrainBlockMaterialDefinitions.Count
                    || foundStableIds[stableIndex])
                {
                    error = $"Runtime表示検索表の安定材質ID {definition.StableId} が範囲外または重複しています。";
                    return false;
                }

                if (!definition.GroundRuntimeId.IsValid
                    || !definition.WallRuntimeId.IsValid)
                {
                    error = $"{definition.StableId} のGroundまたはWall Runtime IDが未解決です。";
                    return false;
                }

                // Runtime IDは共通Definitionカタログ内で一意なので、役割をまたぐ衝突も破損として拒否します。
                if (!foundRuntimeIds.Add(definition.GroundRuntimeId.Value)
                    || !foundRuntimeIds.Add(definition.WallRuntimeId.Value))
                {
                    error = $"{definition.StableId} のGroundまたはWall Runtime IDが別の材質・役割と重複しています。";
                    return false;
                }

                foundStableIds[stableIndex] = true;
                ground[stableIndex] = new TerrainMaterialPresentationRuntimeBinding(
                    definition.GroundRuntimeId.Value,
                    definition.StableId);
                wall[stableIndex] = new TerrainMaterialPresentationRuntimeBinding(
                    definition.WallRuntimeId.Value,
                    definition.StableId);
            }

            Array.Sort(ground, CompareRuntimeId);
            Array.Sort(wall, CompareRuntimeId);
            lookup = new TerrainMaterialPresentationRuntimeLookup(ground, wall);
            error = string.Empty;
            return true;
        }

        /// <summary>
        /// GroundBlock Runtime IDから安定材質IDを解決します。
        /// </summary>
        /// <param name="runtimeId">GeneratedTerrainCellのGroundBlockRuntimeIdです。</param>
        /// <param name="stableId">成功時に対応する1〜41の材質IDを受け取ります。</param>
        /// <returns>Ground検索表に完全一致する有効IDがある場合はtrueです。</returns>
        public bool TryResolveGround(
            DefinitionRuntimeId runtimeId,
            out TerrainBlockMaterialId stableId)
        {
            return TryResolve(groundBindings, runtimeId, out stableId);
        }

        /// <summary>
        /// WallBlock Runtime IDから安定材質IDを解決します。
        /// </summary>
        /// <param name="runtimeId">GeneratedTerrainCellのWallBlockRuntimeIdです。</param>
        /// <param name="stableId">成功時に対応する1〜41の材質IDを受け取ります。</param>
        /// <returns>Wall検索表に完全一致する有効IDがある場合はtrueです。</returns>
        public bool TryResolveWall(
            DefinitionRuntimeId runtimeId,
            out TerrainBlockMaterialId stableId)
        {
            return TryResolve(wallBindings, runtimeId, out stableId);
        }

        /// <summary>
        /// Runtime ID昇順配列を二分探索し、1セルごとの41件線形走査を避けます。
        /// </summary>
        /// <param name="bindings">GroundまたはWall専用の昇順検索表です。</param>
        /// <param name="runtimeId">検索する実行時IDです。</param>
        /// <param name="stableId">成功時の安定材質IDです。</param>
        /// <returns>同じ役割の検索表に完全一致した場合はtrueです。</returns>
        private static bool TryResolve(
            TerrainMaterialPresentationRuntimeBinding[] bindings,
            DefinitionRuntimeId runtimeId,
            out TerrainBlockMaterialId stableId)
        {
            stableId = TerrainBlockMaterialId.None;
            if (!runtimeId.IsValid)
            {
                return false;
            }

            int minimum = 0;
            int maximum = bindings.Length - 1;
            while (minimum <= maximum)
            {
                int middle = minimum + ((maximum - minimum) / 2);
                int middleRuntimeId = bindings[middle].RuntimeIdValue;
                if (middleRuntimeId == runtimeId.Value)
                {
                    stableId = bindings[middle].StableId;
                    return true;
                }

                if (middleRuntimeId < runtimeId.Value)
                {
                    minimum = middle + 1;
                }
                else
                {
                    maximum = middle - 1;
                }
            }

            return false;
        }

        /// <summary>
        /// Array.SortがRuntime ID昇順へ並べるための比較を行います。
        /// </summary>
        /// <param name="left">左側の対応です。</param>
        /// <param name="right">右側の対応です。</param>
        /// <returns>左が小さければ負、同じなら0、大きければ正です。</returns>
        private static int CompareRuntimeId(
            TerrainMaterialPresentationRuntimeBinding left,
            TerrainMaterialPresentationRuntimeBinding right)
        {
            return left.RuntimeIdValue.CompareTo(right.RuntimeIdValue);
        }
    }
}
