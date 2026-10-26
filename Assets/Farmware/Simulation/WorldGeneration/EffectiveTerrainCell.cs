// このファイルは、変更不可の自然生成結果へ疎な地形差分を適用し、現在の有効セルを純粋計算します。
// 移動・建築はこの結果を参照し、Tilemap、自然資源、配置物を地形状態の正として扱いません。

using System;
using Farmware.Simulation;
using Unity.Entities;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 1セルのどの地形区画を自然生成結果から置き換えるかを表します。
    /// bitが立っていればRuntimeIdがInvalidでも「明示的に除去」を意味し、未変更と区別できます。
    /// </summary>
    [Flags]
    public enum TerrainCellChangeMask : byte
    {
        /// <summary>自然生成結果を一切変更しません。</summary>
        None = 0,

        /// <summary>地面ブロックを置換または除去します。</summary>
        GroundBlock = 1 << 0,

        /// <summary>壁ブロックを置換または除去します。</summary>
        WallBlock = 1 << 1,

        /// <summary>液体を置換または除去します。</summary>
        Liquid = 1 << 2,

        /// <summary>地表状態を置換または除去します。</summary>
        TerrainSurface = 1 << 3,

        /// <summary>現在定義済みの全変更bitです。</summary>
        All = GroundBlock | WallBlock | Liquid | TerrainSurface
    }

    /// <summary>
    /// ワールド生成器が直接出力する、変更不可の自然地形セルです。
    /// 1チャンクEntityのDynamic Bufferへ行優先4096件を保持し、疎な変更差分の基準にします。
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct GeneratedTerrainCell : IBufferElementData
    {
        /// <summary>座標由来で固定され、プレイヤー編集では変更しないバイオームです。</summary>
        public BiomeId Biome;

        /// <summary>自然生成された地面定義です。穴や液体セルではInvalidです。</summary>
        public DefinitionRuntimeId GroundBlockRuntimeId;

        /// <summary>自然生成された壁定義です。壁がなければInvalidです。</summary>
        public DefinitionRuntimeId WallBlockRuntimeId;

        /// <summary>自然生成された液体定義です。液体がなければInvalidです。</summary>
        public DefinitionRuntimeId LiquidRuntimeId;

        /// <summary>自然生成された地表定義です。TERRAIN-01Aの互換生成ではInvalidです。</summary>
        public DefinitionRuntimeId TerrainSurfaceRuntimeId;
    }

    /// <summary>
    /// 自然生成から変化した1セル分だけを保持するDynamic Buffer要素です。
    /// 1チャンク内でLocalTileIndex昇順かつ重複なしに並べ、未変更セルへメモリを割り当てません。
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct TerrainCellDelta : IBufferElementData
    {
        /// <summary>64×64チャンク内の行優先セル番号です。範囲は0〜4095です。</summary>
        public ushort LocalTileIndex;

        /// <summary>自然生成結果から明示的に変更する区画です。</summary>
        public TerrainCellChangeMask ChangeMask;

        /// <summary>GroundBlock bitがある場合の置換先です。Invalidなら地面を除去します。</summary>
        public DefinitionRuntimeId GroundBlockRuntimeId;

        /// <summary>WallBlock bitがある場合の置換先です。Invalidなら壁を除去します。</summary>
        public DefinitionRuntimeId WallBlockRuntimeId;

        /// <summary>Liquid bitがある場合の置換先です。Invalidなら液体を除去します。</summary>
        public DefinitionRuntimeId LiquidRuntimeId;

        /// <summary>TerrainSurface bitがある場合の置換先です。Invalidなら地表を除去します。</summary>
        public DefinitionRuntimeId TerrainSurfaceRuntimeId;
    }

    /// <summary>
    /// ストリーミングで破棄される生成チャンクとは別に、変更済みチャンクの差分Bufferを所有する永続状態です。
    /// SAVE-01はこの座標とTerrainCellDeltaを保存し、未変更チャンクにはEntityを作りません。
    /// </summary>
    public struct TerrainChunkDeltaOwner : IComponentData
    {
        /// <summary>差分チャンクが属する固定ワールドレイヤーです。</summary>
        public WorldLayer Layer;

        /// <summary>差分を所有する水平論理チャンク座標です。</summary>
        public int ChunkX;

        /// <summary>差分を所有する垂直論理チャンク座標です。</summary>
        public int ChunkY;
    }

    /// <summary>
    /// 自然生成と変更差分を合成した、現在のゲーム判定用セルです。
    /// 保存の正は自然生成設定とTerrainCellDeltaであり、この値はいつでも再構築できます。
    /// </summary>
    public struct EffectiveTerrainCell
    {
        /// <summary>編集されない固定バイオームです。</summary>
        public BiomeId Biome;

        /// <summary>現在存在する地面定義です。穴または液体セルではInvalidです。</summary>
        public DefinitionRuntimeId GroundBlockRuntimeId;

        /// <summary>現在存在する壁定義です。壁がなければInvalidです。</summary>
        public DefinitionRuntimeId WallBlockRuntimeId;

        /// <summary>現在セルを満たす液体定義です。液体がなければInvalidです。</summary>
        public DefinitionRuntimeId LiquidRuntimeId;

        /// <summary>現在地面の上へ重なる地表定義です。地表がなければInvalidです。</summary>
        public DefinitionRuntimeId TerrainSurfaceRuntimeId;
    }

    /// <summary>
    /// 有効セルを構築できなかった理由です。
    /// 未ロード状態はチャンク検索側で扱い、この純粋計算は渡されたセル内容の整合性だけを検証します。
    /// </summary>
    public enum EffectiveTerrainResolveResult : byte
    {
        /// <summary>有効セルを構築できました。</summary>
        Success = 0,

        /// <summary>自然生成分類または組み込みRuntimeIdが不正です。</summary>
        InvalidBaseTerrain = 1,

        /// <summary>差分に未知bit、範囲外セル、または分類違いの定義参照があります。</summary>
        InvalidDelta = 2,

        /// <summary>合成後に地面と液体が重なるなど、セル区画の組み合わせが不正です。</summary>
        InvalidCombination = 3
    }

    /// <summary>
    /// 生成済みBlockセルへ地形差分を固定順で適用します。
    /// 管理参照やUnityEngine APIを使わないため、Simulation、Burst Job、EditModeテストで共有できます。
    /// </summary>
    public static class EffectiveTerrainResolver
    {
        /// <summary>
        /// 自然地形へ任意の1差分を適用し、現在の有効セルを作ります。
        /// </summary>
        /// <param name="baseCell">変更不可の自然地形です。</param>
        /// <param name="hasDelta">対象セルの差分が存在する場合はtrueです。</param>
        /// <param name="delta">hasDeltaがtrueの場合に適用する差分です。</param>
        /// <param name="catalog">RuntimeIdの分類を検証する地形定義カタログです。</param>
        /// <param name="effectiveCell">成功時に差分適用後の現在地形を受け取ります。</param>
        /// <returns>入力と合成結果が有効ならSuccessです。</returns>
        public static EffectiveTerrainResolveResult TryResolve(
            in GeneratedTerrainCell baseCell,
            bool hasDelta,
            in TerrainCellDelta delta,
            BlobAssetReference<TerrainBlockCatalogBlob> catalog,
            out EffectiveTerrainCell effectiveCell)
        {
            effectiveCell = new EffectiveTerrainCell
            {
                Biome = baseCell.Biome,
                GroundBlockRuntimeId = baseCell.GroundBlockRuntimeId,
                WallBlockRuntimeId = baseCell.WallBlockRuntimeId,
                LiquidRuntimeId = baseCell.LiquidRuntimeId,
                TerrainSurfaceRuntimeId = baseCell.TerrainSurfaceRuntimeId
            };
            if (!IsKnownBiome(baseCell.Biome)
                || !ValidateSlot(
                    catalog,
                    baseCell.GroundBlockRuntimeId,
                    DefinitionKind.GroundBlock)
                || !ValidateSlot(
                    catalog,
                    baseCell.WallBlockRuntimeId,
                    DefinitionKind.WallBlock)
                || !ValidateSlot(
                    catalog,
                    baseCell.LiquidRuntimeId,
                    DefinitionKind.Liquid)
                || !ValidateSlot(
                    catalog,
                    baseCell.TerrainSurfaceRuntimeId,
                    DefinitionKind.TerrainSurface))
            {
                return EffectiveTerrainResolveResult.InvalidBaseTerrain;
            }

            if (hasDelta)
            {
                if (!IsDeltaValid(delta, catalog))
                {
                    return EffectiveTerrainResolveResult.InvalidDelta;
                }

                // 適用順を固定し、呼び出し元やBuffer列挙順によって結果が変わらないようにします。
                if ((delta.ChangeMask & TerrainCellChangeMask.GroundBlock) != 0)
                {
                    effectiveCell.GroundBlockRuntimeId = delta.GroundBlockRuntimeId;
                }

                if ((delta.ChangeMask & TerrainCellChangeMask.WallBlock) != 0)
                {
                    effectiveCell.WallBlockRuntimeId = delta.WallBlockRuntimeId;
                }

                if ((delta.ChangeMask & TerrainCellChangeMask.Liquid) != 0)
                {
                    effectiveCell.LiquidRuntimeId = delta.LiquidRuntimeId;
                }

                if ((delta.ChangeMask & TerrainCellChangeMask.TerrainSurface) != 0)
                {
                    effectiveCell.TerrainSurfaceRuntimeId =
                        delta.TerrainSurfaceRuntimeId;
                }
            }

            // 仕様上Liquidは地面のないセルを満たし、地表は地面へ重なるため、矛盾をキャッシュへ流しません。
            bool groundAndLiquidOverlap = effectiveCell.GroundBlockRuntimeId.IsValid
                && effectiveCell.LiquidRuntimeId.IsValid;
            bool surfaceHasNoGround = effectiveCell.TerrainSurfaceRuntimeId.IsValid
                && !effectiveCell.GroundBlockRuntimeId.IsValid;
            return groundAndLiquidOverlap || surfaceHasNoGround
                ? EffectiveTerrainResolveResult.InvalidCombination
                : EffectiveTerrainResolveResult.Success;
        }

        /// <summary>差分のbit、セル番号、各定義分類を検証します。</summary>
        /// <param name="delta">有効セルへ適用する候補差分です。</param>
        /// <param name="catalog">各RuntimeIdの分類を照合する地形カタログです。</param>
        /// <returns>差分構造と全変更対象の分類が有効ならtrueです。</returns>
        private static bool IsDeltaValid(
            in TerrainCellDelta delta,
            BlobAssetReference<TerrainBlockCatalogBlob> catalog)
        {
            bool hasKnownNonEmptyMask = delta.ChangeMask != TerrainCellChangeMask.None
                && (delta.ChangeMask & ~TerrainCellChangeMask.All) == 0;
            return delta.LocalTileIndex < WorldGridConstants.TilesPerChunk
                && hasKnownNonEmptyMask
                && ValidateChangedSlot(
                    delta.ChangeMask,
                    TerrainCellChangeMask.GroundBlock,
                    delta.GroundBlockRuntimeId,
                    DefinitionKind.GroundBlock,
                    catalog)
                && ValidateChangedSlot(
                    delta.ChangeMask,
                    TerrainCellChangeMask.WallBlock,
                    delta.WallBlockRuntimeId,
                    DefinitionKind.WallBlock,
                    catalog)
                && ValidateChangedSlot(
                    delta.ChangeMask,
                    TerrainCellChangeMask.Liquid,
                    delta.LiquidRuntimeId,
                    DefinitionKind.Liquid,
                    catalog)
                && ValidateChangedSlot(
                    delta.ChangeMask,
                    TerrainCellChangeMask.TerrainSurface,
                    delta.TerrainSurfaceRuntimeId,
                    DefinitionKind.TerrainSurface,
                    catalog);
        }

        /// <summary>変更対象でない区画は値を無視し、変更対象だけ分類を検証します。</summary>
        /// <param name="mask">差分全体の変更bitです。</param>
        /// <param name="slotMask">今回検証する1区画のbitです。</param>
        /// <param name="runtimeId">区画へ設定する定義IDです。</param>
        /// <param name="expectedKind">区画が要求する共通定義分類です。</param>
        /// <param name="catalog">RuntimeIdを解決する地形カタログです。</param>
        /// <returns>未変更、明示除去、または期待分類の登録済み定義ならtrueです。</returns>
        private static bool ValidateChangedSlot(
            TerrainCellChangeMask mask,
            TerrainCellChangeMask slotMask,
            DefinitionRuntimeId runtimeId,
            DefinitionKind expectedKind,
            BlobAssetReference<TerrainBlockCatalogBlob> catalog)
        {
            return (mask & slotMask) == 0
                || ValidateSlot(catalog, runtimeId, expectedKind);
        }

        /// <summary>Invalidを「区画なし」として許可し、有効IDだけを期待分類と照合します。</summary>
        /// <param name="catalog">RuntimeIdを解決する地形カタログです。</param>
        /// <param name="runtimeId">区画が参照する定義ID、または区画なしのInvalidです。</param>
        /// <param name="expectedKind">区画が要求する共通定義分類です。</param>
        /// <returns>区画なし、または期待分類の登録済み定義ならtrueです。</returns>
        private static bool ValidateSlot(
            BlobAssetReference<TerrainBlockCatalogBlob> catalog,
            DefinitionRuntimeId runtimeId,
            DefinitionKind expectedKind)
        {
            if (!runtimeId.IsValid)
            {
                return true;
            }

            return TerrainBlockCatalogLookup.TryGetDefinition(
                    catalog,
                    runtimeId,
                    out TerrainBlockDefinitionBlob definition)
                && definition.Kind == expectedKind;
        }

        /// <summary>海域のNoneと、地上用に固定された18Biomeだけを自然生成値として許可します。</summary>
        /// <param name="biome">生成セルが保持する変更不可のBiome IDです。</param>
        /// <returns>Noneまたは現行18Biomeの安定ID範囲ならtrueです。</returns>
        private static bool IsKnownBiome(BiomeId biome)
        {
            int stableId = (int)biome;
            return biome == BiomeId.None
                || (stableId >= BiomeStableIds.First && stableId <= BiomeStableIds.Last);
        }
    }

    /// <summary>
    /// レイヤー付き絶対座標と永続差分所有者を照合して、有効セル解決へ渡す差分を選びます。
    /// 同じX/Yの別レイヤーや別チャンクのBufferを誤って適用しないためのECS境界です。
    /// </summary>
    public static class EffectiveTerrainCellQuery
    {
        /// <summary>
        /// 指定座標が所有者チャンクに属する場合だけ対応差分を検索し、有効セルを構築します。
        /// </summary>
        /// <param name="coordinate">問い合わせるレイヤー付き絶対セル座標です。</param>
        /// <param name="owner">差分Bufferを所有するレイヤー付きチャンク座標です。</param>
        /// <param name="deltas">ownerに属し、LocalTileIndex昇順へ並ぶ疎な差分です。</param>
        /// <param name="baseCell">生成済みの変更不可な自然地形です。</param>
        /// <param name="catalog">RuntimeIdの分類を検証する地形定義カタログです。</param>
        /// <param name="effectiveCell">成功時に現在の有効地形を受け取ります。</param>
        /// <returns>座標がownerに属し、自然地形と任意差分を正しく合成できた場合はSuccessです。</returns>
        public static EffectiveTerrainResolveResult TryResolve(
            in WorldTileCoordinate coordinate,
            in TerrainChunkDeltaOwner owner,
            DynamicBuffer<TerrainCellDelta> deltas,
            in GeneratedTerrainCell baseCell,
            BlobAssetReference<TerrainBlockCatalogBlob> catalog,
            out EffectiveTerrainCell effectiveCell)
        {
            effectiveCell = default;
            ChunkCoordinate expectedOwner = WorldGrid.ToChunkCoordinate(coordinate);
            if (owner.Layer != expectedOwner.Layer
                || owner.ChunkX != expectedOwner.X
                || owner.ChunkY != expectedOwner.Y)
            {
                return EffectiveTerrainResolveResult.InvalidDelta;
            }

            ChunkLocalTileCoordinate local = WorldGrid.ToLocalTileCoordinate(
                coordinate.X,
                coordinate.Y);
            ushort localTileIndex = (ushort)WorldGrid.ToTileIndex(local.X, local.Y);
            bool hasDelta = TerrainCellDeltaUtility.TryFind(
                deltas,
                localTileIndex,
                out TerrainCellDelta delta);
            return EffectiveTerrainResolver.TryResolve(
                baseCell,
                hasDelta,
                delta,
                catalog,
                out effectiveCell);
        }
    }

    /// <summary>
    /// チャンク内でLocalTileIndex昇順に保持する疎な差分Bufferを検索・更新します。
    /// 変更後が自然生成と同じなら要素を削除し、未変更セルの保存量を増やしません。
    /// </summary>
    public static class TerrainCellDeltaUtility
    {
        /// <summary>
        /// 対象セルの差分を二分探索します。
        /// </summary>
        /// <param name="deltas">LocalTileIndex昇順かつ重複なしの差分Bufferです。</param>
        /// <param name="localTileIndex">0〜4095の検索対象です。</param>
        /// <param name="delta">成功時に対象差分を受け取ります。</param>
        /// <returns>対象セルの差分が存在する場合はtrueです。</returns>
        public static bool TryFind(
            DynamicBuffer<TerrainCellDelta> deltas,
            ushort localTileIndex,
            out TerrainCellDelta delta)
        {
            int index = FindIndex(deltas, localTileIndex, out bool found);
            delta = found ? deltas[index] : default;
            return found;
        }

        /// <summary>
        /// 希望する有効セルと自然地形の差だけをBufferへ挿入または更新します。
        /// </summary>
        /// <param name="deltas">対象チャンクが所有する差分Bufferです。</param>
        /// <param name="localTileIndex">変更する0〜4095のセル番号です。</param>
        /// <param name="baseCell">変更不可の自然地形です。</param>
        /// <param name="desiredCell">Command検証後に確定した希望地形です。</param>
        /// <param name="catalog">希望地形の定義分類と区画構成を検証する地形カタログです。</param>
        /// <returns>範囲内セルを処理できた場合はtrueです。自然状態と同じ場合は既存差分を削除します。</returns>
        public static bool SetOrRemove(
            DynamicBuffer<TerrainCellDelta> deltas,
            ushort localTileIndex,
            in GeneratedTerrainCell baseCell,
            in EffectiveTerrainCell desiredCell,
            BlobAssetReference<TerrainBlockCatalogBlob> catalog)
        {
            if (localTileIndex >= WorldGridConstants.TilesPerChunk
                || desiredCell.Biome != baseCell.Biome)
            {
                return false;
            }

            TerrainCellDelta delta = new TerrainCellDelta
            {
                LocalTileIndex = localTileIndex,
                GroundBlockRuntimeId = desiredCell.GroundBlockRuntimeId,
                WallBlockRuntimeId = desiredCell.WallBlockRuntimeId,
                LiquidRuntimeId = desiredCell.LiquidRuntimeId,
                TerrainSurfaceRuntimeId = desiredCell.TerrainSurfaceRuntimeId
            };
            if (!desiredCell.GroundBlockRuntimeId.Equals(baseCell.GroundBlockRuntimeId))
            {
                delta.ChangeMask |= TerrainCellChangeMask.GroundBlock;
            }

            if (!desiredCell.WallBlockRuntimeId.Equals(baseCell.WallBlockRuntimeId))
            {
                delta.ChangeMask |= TerrainCellChangeMask.WallBlock;
            }

            if (!desiredCell.LiquidRuntimeId.Equals(baseCell.LiquidRuntimeId))
            {
                delta.ChangeMask |= TerrainCellChangeMask.Liquid;
            }

            if (!desiredCell.TerrainSurfaceRuntimeId.Equals(
                baseCell.TerrainSurfaceRuntimeId))
            {
                delta.ChangeMask |= TerrainCellChangeMask.TerrainSurface;
            }

            // public更新API自身でも最終状態を検証し、呼び出し側の確認漏れで破損差分を確定しません。
            if (EffectiveTerrainResolver.TryResolve(
                    baseCell,
                    delta.ChangeMask != TerrainCellChangeMask.None,
                    delta,
                    catalog,
                    out EffectiveTerrainCell validatedCell)
                != EffectiveTerrainResolveResult.Success
                || !HasSameTerrain(desiredCell, validatedCell))
            {
                return false;
            }

            int index = FindIndex(deltas, localTileIndex, out bool found);
            if (delta.ChangeMask == TerrainCellChangeMask.None)
            {
                if (found)
                {
                    deltas.RemoveAt(index);
                }

                return true;
            }

            if (found)
            {
                deltas[index] = delta;
            }
            else
            {
                deltas.Insert(index, delta);
            }

            return true;
        }

        /// <summary>Biomeと4区画が一致し、検証結果が呼び出し側の希望状態を完全に表すか確認します。</summary>
        /// <param name="first">呼び出し側が希望した現在地形です。</param>
        /// <param name="second">自然地形と差分から再構築して検証済みの地形です。</param>
        /// <returns>Biomeと全区画が一致する場合はtrueです。</returns>
        private static bool HasSameTerrain(
            in EffectiveTerrainCell first,
            in EffectiveTerrainCell second)
        {
            return first.Biome == second.Biome
                && first.GroundBlockRuntimeId.Equals(second.GroundBlockRuntimeId)
                && first.WallBlockRuntimeId.Equals(second.WallBlockRuntimeId)
                && first.LiquidRuntimeId.Equals(second.LiquidRuntimeId)
                && first.TerrainSurfaceRuntimeId.Equals(
                    second.TerrainSurfaceRuntimeId);
        }

        /// <summary>二分探索で既存位置、または昇順を保てる挿入位置を返します。</summary>
        /// <param name="deltas">LocalTileIndex昇順の疎な差分Bufferです。</param>
        /// <param name="localTileIndex">検索または挿入するセル番号です。</param>
        /// <param name="found">同じセル番号が既に存在する場合はtrueです。</param>
        /// <returns>既存要素の位置、または昇順を維持する挿入位置です。</returns>
        private static int FindIndex(
            DynamicBuffer<TerrainCellDelta> deltas,
            ushort localTileIndex,
            out bool found)
        {
            int lowerIndex = 0;
            int upperIndex = deltas.Length - 1;
            while (lowerIndex <= upperIndex)
            {
                int middleIndex = lowerIndex + ((upperIndex - lowerIndex) / 2);
                ushort candidateIndex = deltas[middleIndex].LocalTileIndex;
                if (candidateIndex == localTileIndex)
                {
                    found = true;
                    return middleIndex;
                }

                if (candidateIndex < localTileIndex)
                {
                    lowerIndex = middleIndex + 1;
                }
                else
                {
                    upperIndex = middleIndex - 1;
                }
            }

            found = false;
            return lowerIndex;
        }
    }
}
