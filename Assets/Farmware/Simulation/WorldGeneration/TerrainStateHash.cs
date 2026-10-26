// このファイルは、地形定義属性、自然生成Blockパレット、レイヤー付き疎な変更差分を決定論的な状態ハッシュへ追加します。
// 表示キャッシュやロード順は含めず、保存対象となる論理値だけを固定順で比較できるようにします。

using System;
using Farmware.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// 地形状態をハッシュへ追加できなかった理由です。
    /// Singleton不整合、カタログ破損、差分破損を分け、保存前監査で原因を特定できるようにします。
    /// </summary>
    public enum TerrainStateHashErrorCode : byte
    {
        /// <summary>全地形状態を正常に追加できました。</summary>
        None = 0,

        /// <summary>共通DefinitionCatalogが存在しないか複数存在します。</summary>
        DefinitionCatalogIsInvalid = 1,

        /// <summary>TerrainBlockCatalogが存在しないか複数存在します。</summary>
        TerrainCatalogIsInvalid = 2,

        /// <summary>共通定義と地形固有定義の対応、分類、属性が不正です。</summary>
        TerrainCatalogDataIsInvalid = 3,

        /// <summary>差分所有者の座標、重複、Buffer順、変更bit、定義参照が不正です。</summary>
        TerrainDeltaDataIsInvalid = 4,

        /// <summary>TerrainBlockPaletteCatalogが存在しないか複数存在します。</summary>
        TerrainPaletteCatalogIsInvalid = 5,

        /// <summary>Biome・地質パレットの行、範囲、重み、定義参照が不正です。</summary>
        TerrainPaletteDataIsInvalid = 6
    }

    /// <summary>
    /// 地形状態ハッシュ処理の成功または最初の失敗理由を保持します。
    /// 例外を通常の検証分岐に使わず、テストと診断が同じ結果を読めるようにします。
    /// </summary>
    public readonly struct TerrainStateHashError
    {
        /// <summary>成功ならNone、失敗なら最初に見つかった理由です。</summary>
        public readonly TerrainStateHashErrorCode Code;

        /// <summary>指定した成功または失敗理由を持つ結果を作ります。</summary>
        public TerrainStateHashError(TerrainStateHashErrorCode code)
        {
            Code = code;
        }

        /// <summary>地形状態を完全に追加できた場合はtrueです。</summary>
        public bool Succeeded => Code == TerrainStateHashErrorCode.None;
    }

    /// <summary>
    /// 地形定義と永続差分をECS Worldから読み、Entity作成順に依存しない順序で状態ハッシュへ追加します。
    /// 呼び出し側は必要な先行セクションを追加した後に呼び、最後にBuilder.Completeを1回だけ実行します。
    /// </summary>
    public static class TerrainStateHasher
    {
        /// <summary>地形定義属性へ割り当てた機能固有セクションIDです。</summary>
        public const uint TerrainDefinitionSectionId =
            SimulationStateHashSchema.FirstFeatureSectionId + 128;

        /// <summary>自然生成Blockパレットへ割り当てた機能固有セクションIDです。</summary>
        public const uint TerrainPaletteSectionId = TerrainDefinitionSectionId + 1;

        /// <summary>レイヤー付き地形差分へ割り当てた機能固有セクションIDです。</summary>
        public const uint TerrainDeltaSectionId = TerrainDefinitionSectionId + 2;

        /// <summary>
        /// World内の定義Singletonと全差分所有者を検証し、論理値を固定順で追加します。
        /// </summary>
        /// <param name="entityManager">固定Tick完了境界のECS Worldです。</param>
        /// <param name="builder">まだCompleteしていない状態ハッシュBuilderです。</param>
        /// <param name="error">失敗時に最初の不整合理由を受け取ります。</param>
        /// <returns>地形定義と全差分を追加できた場合はtrueです。</returns>
        public static bool TryAppendTo(
            EntityManager entityManager,
            ref SimulationStateHashBuilder builder,
            out TerrainStateHashError error)
        {
            if (!TryGetSingleCatalog(
                    entityManager,
                    out DefinitionCatalog definitionCatalog)
                || !definitionCatalog.Value.IsCreated)
            {
                error = new TerrainStateHashError(
                    TerrainStateHashErrorCode.DefinitionCatalogIsInvalid);
                return false;
            }

            if (!TryGetSingleCatalog(
                    entityManager,
                    out TerrainBlockCatalog terrainCatalog)
                || !terrainCatalog.Value.IsCreated)
            {
                error = new TerrainStateHashError(
                    TerrainStateHashErrorCode.TerrainCatalogIsInvalid);
                return false;
            }

            if (!TryGetSingleCatalog(
                    entityManager,
                    out TerrainBlockPaletteCatalog paletteCatalog)
                || !paletteCatalog.Value.IsCreated)
            {
                error = new TerrainStateHashError(
                    TerrainStateHashErrorCode.TerrainPaletteCatalogIsInvalid);
                return false;
            }

            if (!BuiltInTerrainBlockDefinitions.TryResolveRuntimeIds(
                    definitionCatalog.Value,
                    out BuiltInTerrainBlockRuntimeIds expectedBuiltInRuntimeIds)
                || !HasSameBuiltInRuntimeIds(
                    terrainCatalog.BuiltInRuntimeIds,
                    expectedBuiltInRuntimeIds))
            {
                error = new TerrainStateHashError(
                    TerrainStateHashErrorCode.TerrainCatalogDataIsInvalid);
                return false;
            }

            if (!TryAppendTerrainDefinitions(
                definitionCatalog.Value,
                terrainCatalog.Value,
                ref builder))
            {
                error = new TerrainStateHashError(
                    TerrainStateHashErrorCode.TerrainCatalogDataIsInvalid);
                return false;
            }

            if (!TryAppendTerrainPalette(
                    paletteCatalog.Value,
                    terrainCatalog.Value,
                    ref builder))
            {
                error = new TerrainStateHashError(
                    TerrainStateHashErrorCode.TerrainPaletteDataIsInvalid);
                return false;
            }

            if (!TryAppendTerrainDeltas(
                entityManager,
                terrainCatalog.Value,
                ref builder))
            {
                error = new TerrainStateHashError(
                    TerrainStateHashErrorCode.TerrainDeltaDataIsInvalid);
                return false;
            }

            error = new TerrainStateHashError(TerrainStateHashErrorCode.None);
            return true;
        }

        /// <summary>
        /// 18Biomeと8地質の固定Blockパレットを、実際の生成参照順で検証して追加します。
        /// </summary>
        /// <param name="paletteCatalog">自然Ground・Wall候補を保持する固定パレットBlobです。</param>
        /// <param name="terrainCatalog">各候補Runtime IDのGround／Wall分類を照合する地形Blobです。</param>
        /// <param name="builder">自然生成パレットセクションを書き込む状態ハッシュBuilderです。</param>
        /// <returns>行、Range、重み、定義分類がすべて現行契約どおりならtrueです。</returns>
        private static bool TryAppendTerrainPalette(
            BlobAssetReference<TerrainBlockPaletteCatalogBlob> paletteCatalog,
            BlobAssetReference<TerrainBlockCatalogBlob> terrainCatalog,
            ref SimulationStateHashBuilder builder)
        {
            ref TerrainBlockPaletteCatalogBlob root = ref paletteCatalog.Value;
            if (root.BiomeSurfacePalettes.Length
                    != TerrainBlockPaletteDefinitions.BiomePaletteCount
                || root.GeologyPalettes.Length
                    != TerrainBlockPaletteDefinitions.GeologyPaletteCount)
            {
                return false;
            }

            builder.BeginSection(TerrainPaletteSectionId);
            builder.WriteInt32(root.BiomeSurfacePalettes.Length);
            builder.WriteInt32(root.GeologyPalettes.Length);
            builder.WriteInt32(root.Entries.Length);
            int expectedStartIndex = 0;
            for (int biomeIndex = 0;
                 biomeIndex < root.BiomeSurfacePalettes.Length;
                 biomeIndex++)
            {
                BiomeSurfaceBlockPaletteBlob row = root.BiomeSurfacePalettes[biomeIndex];
                BiomeId expectedBiome = (BiomeId)(BiomeStableIds.First + biomeIndex);
                if (row.BiomeId != expectedBiome)
                {
                    return false;
                }

                builder.WriteByte((byte)row.BiomeId);
                if (!TryAppendPaletteRange(
                        ref root,
                        terrainCatalog,
                        row.SurfaceGround,
                        DefinitionKind.GroundBlock,
                        ref expectedStartIndex,
                        ref builder))
                {
                    return false;
                }
            }

            for (int geologyIndex = 0;
                 geologyIndex < root.GeologyPalettes.Length;
                 geologyIndex++)
            {
                GeologyBlockPaletteBlob row = root.GeologyPalettes[geologyIndex];
                GeologyClass expectedGeology = (GeologyClass)(
                    (int)GeologyClass.UnconsolidatedSediment + geologyIndex);
                if (row.GeologyClass != expectedGeology)
                {
                    return false;
                }

                builder.WriteByte((byte)row.GeologyClass);
                if (!TryAppendPaletteRange(
                        ref root,
                        terrainCatalog,
                        row.ExposedGround,
                        DefinitionKind.GroundBlock,
                        ref expectedStartIndex,
                        ref builder)
                    || !TryAppendPaletteRange(
                        ref root,
                        terrainCatalog,
                        row.NaturalWall,
                        DefinitionKind.WallBlock,
                        ref expectedStartIndex,
                        ref builder))
                {
                    return false;
                }
            }

            // どの行からも参照されない余剰Entryも生成契約の破損なので、状態ハッシュへ黙って残しません。
            return expectedStartIndex == root.Entries.Length;
        }

        /// <summary>1パレット範囲の連続性、重み合計、材質ID、Block分類を検証して固定順で追加します。</summary>
        /// <param name="root">全行と平坦化Entryを保持するパレットBlob本体です。</param>
        /// <param name="terrainCatalog">Block Runtime IDの分類を照合する地形Blobです。</param>
        /// <param name="range">今回追加する連続Entry範囲です。</param>
        /// <param name="expectedKind">Ground行またはWall行が要求する定義分類です。</param>
        /// <param name="expectedStartIndex">直前範囲の末尾であり、成功時に今回範囲の末尾へ進めます。</param>
        /// <param name="builder">現在のパレットセクションへ論理値を書き込むBuilderです。</param>
        /// <returns>範囲が隙間なく有効で、全候補が現行パレット契約を満たす場合はtrueです。</returns>
        private static bool TryAppendPaletteRange(
            ref TerrainBlockPaletteCatalogBlob root,
            BlobAssetReference<TerrainBlockCatalogBlob> terrainCatalog,
            TerrainBlockPaletteRange range,
            DefinitionKind expectedKind,
            ref int expectedStartIndex,
            ref SimulationStateHashBuilder builder)
        {
            if (range.StartIndex != expectedStartIndex
                || range.Count <= 0
                || range.StartIndex > root.Entries.Length - range.Count)
            {
                return false;
            }

            builder.WriteInt32(range.Count);
            int totalWeight = 0;
            for (int relativeIndex = 0; relativeIndex < range.Count; relativeIndex++)
            {
                TerrainBlockPaletteEntryBlob entry =
                    root.Entries[range.StartIndex + relativeIndex];
                int materialId = (int)entry.MaterialId;
                if (materialId < (int)TerrainBlockMaterialId.GrassLoam
                    || materialId > (int)TerrainBlockMaterialId.Peridotite
                    || entry.Weight == 0
                    || !TerrainBlockCatalogLookup.TryGetDefinition(
                        terrainCatalog,
                        entry.BlockRuntimeId,
                        out TerrainBlockDefinitionBlob definition)
                    || definition.Kind != expectedKind)
                {
                    return false;
                }

                totalWeight += entry.Weight;
                builder.WriteByte((byte)entry.MaterialId);
                builder.WriteInt32(entry.BlockRuntimeId.Value);
                builder.WriteUInt16(entry.Weight);
            }

            if (totalWeight != TerrainBlockPaletteDefinitions.RequiredWeightTotal)
            {
                return false;
            }

            expectedStartIndex += range.Count;
            return true;
        }

        /// <summary>指定ComponentがWorldにちょうど1件ある場合だけ値を取得します。</summary>
        /// <typeparam name="T">取得するunmanaged Singleton Component型です。</typeparam>
        /// <param name="entityManager">検索対象となるECS WorldのEntityManagerです。</param>
        /// <param name="catalog">成功時に唯一のComponent値を受け取ります。</param>
        /// <returns>対象Componentが1件だけ存在する場合はtrueです。</returns>
        private static bool TryGetSingleCatalog<T>(EntityManager entityManager, out T catalog)
            where T : unmanaged, IComponentData
        {
            EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            try
            {
                if (query.CalculateEntityCount() != 1)
                {
                    catalog = default;
                    return false;
                }

                catalog = query.GetSingleton<T>();
                return true;
            }
            finally
            {
                query.Dispose();
            }
        }

        /// <summary>共通定義と地形固有定義を照合し、RuntimeId昇順で全属性を追加します。</summary>
        /// <param name="definitionCatalog">GUID、RuntimeId、分類を保持する共通定義Blobです。</param>
        /// <param name="terrainCatalog">地形固有属性を保持するBlobです。</param>
        /// <param name="builder">地形定義セクションを書き込む状態ハッシュBuilderです。</param>
        /// <returns>両カタログが一対一で有効ならtrueです。</returns>
        private static bool TryAppendTerrainDefinitions(
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog,
            BlobAssetReference<TerrainBlockCatalogBlob> terrainCatalog,
            ref SimulationStateHashBuilder builder)
        {
            ref DefinitionCatalogBlob commonRoot = ref definitionCatalog.Value;
            ref TerrainBlockCatalogBlob terrainRoot = ref terrainCatalog.Value;
            int expectedTerrainCount = 0;
            for (int definitionIndex = 0;
                 definitionIndex < commonRoot.Definitions.Length;
                 definitionIndex++)
            {
                if (IsTerrainKind(commonRoot.Definitions[definitionIndex].Kind))
                {
                    expectedTerrainCount++;
                }
            }

            if (terrainRoot.Definitions.Length != expectedTerrainCount)
            {
                return false;
            }

            builder.BeginSection(TerrainDefinitionSectionId);
            builder.WriteInt32(terrainRoot.Definitions.Length);
            int previousRuntimeId = DefinitionRuntimeId.InvalidValue;
            for (int terrainIndex = 0;
                 terrainIndex < terrainRoot.Definitions.Length;
                 terrainIndex++)
            {
                TerrainBlockDefinitionBlob terrain =
                    terrainRoot.Definitions[terrainIndex];
                int runtimeId = terrain.RuntimeId.Value;
                if (runtimeId <= previousRuntimeId
                    || runtimeId > commonRoot.Definitions.Length
                    || runtimeId <= DefinitionRuntimeId.InvalidValue)
                {
                    return false;
                }

                DefinitionBlobRecord common = commonRoot.Definitions[runtimeId - 1];
                if (!common.RuntimeId.Equals(terrain.RuntimeId)
                    || common.Kind != terrain.Kind
                    || !IsTerrainKind(terrain.Kind)
                    || (terrain.Flags & ~AllKnownFlags()) != 0
                    || !IsRecoveredItemValid(
                        ref commonRoot,
                        terrain.RecoveredItemRuntimeId))
                {
                    return false;
                }

                // Blobのpaddingは環境差を持ち得るため、フィールドを固定幅で明示的に書き込みます。
                builder.WriteInt32(runtimeId);
                builder.WriteByte((byte)terrain.Kind);
                builder.WriteUInt16(terrain.Hardness);
                builder.WriteInt32(terrain.RecoveredItemRuntimeId.Value);
                builder.WriteUInt16((ushort)terrain.Flags);
                previousRuntimeId = runtimeId;
            }

            return true;
        }

        /// <summary>所有者とBufferの一対一対応を検証し、レイヤー、チャンクX/Y順へ正規化した全差分を追加します。</summary>
        /// <param name="entityManager">差分所有者とBufferを保持するECS Worldです。</param>
        /// <param name="terrainCatalog">差分RuntimeIdの分類を検証する地形定義Blobです。</param>
        /// <param name="builder">地形差分セクションを書き込む状態ハッシュBuilderです。</param>
        /// <returns>全所有者と差分が正規状態ならtrueです。</returns>
        private static bool TryAppendTerrainDeltas(
            EntityManager entityManager,
            BlobAssetReference<TerrainBlockCatalogBlob> terrainCatalog,
            ref SimulationStateHashBuilder builder)
        {
            EntityQuery ownerQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<TerrainChunkDeltaOwner>());
            EntityQuery bufferQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<TerrainCellDelta>());
            try
            {
                // AND条件のQueryだけでは片側Componentが欠けた破損Entityを見落とすため、
                // 両集合の件数と各OwnerのBuffer有無を独立して確認します。
                if (ownerQuery.CalculateEntityCount() != bufferQuery.CalculateEntityCount())
                {
                    return false;
                }

                NativeArray<Entity> entities = ownerQuery.ToEntityArray(Allocator.Temp);
                NativeArray<TerrainChunkDeltaOwner> owners =
                    ownerQuery.ToComponentDataArray<TerrainChunkDeltaOwner>(Allocator.Temp);
                NativeArray<OrderedDeltaOwner> ordered = new NativeArray<OrderedDeltaOwner>(
                    entities.Length,
                    Allocator.Temp);
                try
                {
                    for (int ownerIndex = 0; ownerIndex < owners.Length; ownerIndex++)
                    {
                        if (!entityManager.HasBuffer<TerrainCellDelta>(entities[ownerIndex]))
                        {
                            return false;
                        }

                        ordered[ownerIndex] = new OrderedDeltaOwner
                        {
                            Entity = entities[ownerIndex],
                            Owner = owners[ownerIndex]
                        };
                    }

                    ordered.Sort();
                    builder.BeginSection(TerrainDeltaSectionId);
                    builder.WriteInt32(ordered.Length);
                    for (int ownerIndex = 0; ownerIndex < ordered.Length; ownerIndex++)
                    {
                        OrderedDeltaOwner current = ordered[ownerIndex];
                        bool duplicatesPrevious = ownerIndex > 0
                            && current.HasSameCoordinate(ordered[ownerIndex - 1]);
                        bool coordinateIsInvalid = !WorldLayerUtility.IsPlayable(
                                current.Owner.Layer)
                            || current.Owner.ChunkX
                                < WorldGridConstants.MinimumLogicalChunkCoordinate
                            || current.Owner.ChunkX
                                > WorldGridConstants.MaximumLogicalChunkCoordinate
                            || current.Owner.ChunkY
                                < WorldGridConstants.MinimumLogicalChunkCoordinate
                            || current.Owner.ChunkY
                                > WorldGridConstants.MaximumLogicalChunkCoordinate;
                        DynamicBuffer<TerrainCellDelta> deltas =
                            entityManager.GetBuffer<TerrainCellDelta>(
                                current.Entity,
                                true);
                        if (duplicatesPrevious || coordinateIsInvalid || deltas.Length == 0)
                        {
                            return false;
                        }

                        builder.WriteByte((byte)current.Owner.Layer);
                        builder.WriteInt32(current.Owner.ChunkX);
                        builder.WriteInt32(current.Owner.ChunkY);
                        builder.WriteInt32(deltas.Length);
                        ushort previousLocalIndex = 0;
                        for (int deltaIndex = 0;
                             deltaIndex < deltas.Length;
                             deltaIndex++)
                        {
                            TerrainCellDelta delta = deltas[deltaIndex];
                            bool orderIsInvalid = deltaIndex > 0
                                && delta.LocalTileIndex <= previousLocalIndex;
                            if (orderIsInvalid
                                || !TryAppendDelta(
                                    delta,
                                    terrainCatalog,
                                    ref builder))
                            {
                                return false;
                            }

                            previousLocalIndex = delta.LocalTileIndex;
                        }
                    }

                    return true;
                }
                finally
                {
                    ordered.Dispose();
                    owners.Dispose();
                    entities.Dispose();
                }
            }
            finally
            {
                bufferQuery.Dispose();
                ownerQuery.Dispose();
            }
        }

        /// <summary>1差分の構造と参照分類を検証し、意味を持つ区画だけを追加します。</summary>
        /// <param name="delta">LocalTileIndex順に読み取った1セル分の差分です。</param>
        /// <param name="terrainCatalog">変更対象のRuntimeId分類を検証する地形Blobです。</param>
        /// <param name="builder">現在の地形差分セクションへ値を追加するBuilderです。</param>
        /// <returns>差分が有効で全論理値を追加できた場合はtrueです。</returns>
        private static bool TryAppendDelta(
            in TerrainCellDelta delta,
            BlobAssetReference<TerrainBlockCatalogBlob> terrainCatalog,
            ref SimulationStateHashBuilder builder)
        {
            if (delta.LocalTileIndex >= WorldGridConstants.TilesPerChunk
                || delta.ChangeMask == TerrainCellChangeMask.None
                || (delta.ChangeMask & ~TerrainCellChangeMask.All) != 0
                || !ValidateChangedSlot(
                    terrainCatalog,
                    delta.ChangeMask,
                    TerrainCellChangeMask.GroundBlock,
                    delta.GroundBlockRuntimeId,
                    DefinitionKind.GroundBlock)
                || !ValidateChangedSlot(
                    terrainCatalog,
                    delta.ChangeMask,
                    TerrainCellChangeMask.WallBlock,
                    delta.WallBlockRuntimeId,
                    DefinitionKind.WallBlock)
                || !ValidateChangedSlot(
                    terrainCatalog,
                    delta.ChangeMask,
                    TerrainCellChangeMask.Liquid,
                    delta.LiquidRuntimeId,
                    DefinitionKind.Liquid)
                || !ValidateChangedSlot(
                    terrainCatalog,
                    delta.ChangeMask,
                    TerrainCellChangeMask.TerrainSurface,
                    delta.TerrainSurfaceRuntimeId,
                    DefinitionKind.TerrainSurface))
            {
                return false;
            }

            builder.WriteUInt16(delta.LocalTileIndex);
            builder.WriteByte((byte)delta.ChangeMask);
            WriteChangedRuntimeId(
                delta.ChangeMask,
                TerrainCellChangeMask.GroundBlock,
                delta.GroundBlockRuntimeId,
                ref builder);
            WriteChangedRuntimeId(
                delta.ChangeMask,
                TerrainCellChangeMask.WallBlock,
                delta.WallBlockRuntimeId,
                ref builder);
            WriteChangedRuntimeId(
                delta.ChangeMask,
                TerrainCellChangeMask.Liquid,
                delta.LiquidRuntimeId,
                ref builder);
            WriteChangedRuntimeId(
                delta.ChangeMask,
                TerrainCellChangeMask.TerrainSurface,
                delta.TerrainSurfaceRuntimeId,
                ref builder);
            return true;
        }

        /// <summary>変更対象の有効IDだけを期待分類と照合し、Invalidによる明示除去を許可します。</summary>
        /// <param name="catalog">RuntimeIdを解決する地形定義Blobです。</param>
        /// <param name="mask">差分全体の変更bitです。</param>
        /// <param name="slotMask">今回確認する1区画の変更bitです。</param>
        /// <param name="runtimeId">区画へ設定する定義ID、または明示除去のInvalidです。</param>
        /// <param name="expectedKind">区画が要求する共通定義分類です。</param>
        /// <returns>未変更、明示除去、または期待分類の登録済み定義ならtrueです。</returns>
        private static bool ValidateChangedSlot(
            BlobAssetReference<TerrainBlockCatalogBlob> catalog,
            TerrainCellChangeMask mask,
            TerrainCellChangeMask slotMask,
            DefinitionRuntimeId runtimeId,
            DefinitionKind expectedKind)
        {
            if ((mask & slotMask) == 0 || !runtimeId.IsValid)
            {
                return true;
            }

            return TerrainBlockCatalogLookup.TryGetDefinition(
                    catalog,
                    runtimeId,
                    out TerrainBlockDefinitionBlob definition)
                && definition.Kind == expectedKind;
        }

        /// <summary>変更bitがある区画だけRuntimeIdをハッシュへ追加します。</summary>
        /// <param name="mask">差分全体の変更bitです。</param>
        /// <param name="slotMask">今回書き込む1区画の変更bitです。</param>
        /// <param name="runtimeId">明示除去のInvalidを含む変更後定義IDです。</param>
        /// <param name="builder">現在の差分セクションへ値を追加するBuilderです。</param>
        private static void WriteChangedRuntimeId(
            TerrainCellChangeMask mask,
            TerrainCellChangeMask slotMask,
            DefinitionRuntimeId runtimeId,
            ref SimulationStateHashBuilder builder)
        {
            if ((mask & slotMask) != 0)
            {
                builder.WriteInt32(runtimeId.Value);
            }
        }

        /// <summary>地形カタログへ格納できる4分類かを確認します。</summary>
        /// <param name="kind">共通カタログ内の定義分類です。</param>
        /// <returns>地形4分類のいずれかならtrueです。</returns>
        private static bool IsTerrainKind(DefinitionKind kind)
        {
            return kind >= DefinitionKind.GroundBlock
                && kind <= DefinitionKind.TerrainSurface;
        }

        /// <summary>現在定義済みの属性bitを一つのMaskとして返します。</summary>
        /// <returns>TERRAIN-01Aが認識する全地形属性の組み合わせです。</returns>
        private static TerrainBlockDefinitionFlags AllKnownFlags()
        {
            return TerrainBlockDefinitionFlags.BlocksMovement
                | TerrainBlockDefinitionFlags.AllowsNormalEquipment
                | TerrainBlockDefinitionFlags.AllowsNormalBelt
                | TerrainBlockDefinitionFlags.AllowsBridge
                | TerrainBlockDefinitionFlags.RequiresHeatResistance
                | TerrainBlockDefinitionFlags.Watertight
                | TerrainBlockDefinitionFlags.FacilityBoundary;
        }

        /// <summary>回収品が未設定、または共通カタログ内のItem分類であることを確認します。</summary>
        /// <param name="root">回収品の分類を照合する共通定義Blobです。</param>
        /// <param name="recoveredItemRuntimeId">地形定義が参照する任意の回収品IDです。</param>
        /// <returns>未設定、または登録済みItem分類ならtrueです。</returns>
        private static bool IsRecoveredItemValid(
            ref DefinitionCatalogBlob root,
            DefinitionRuntimeId recoveredItemRuntimeId)
        {
            if (!recoveredItemRuntimeId.IsValid)
            {
                return true;
            }

            int index = recoveredItemRuntimeId.Value - 1;
            return index >= 0
                && index < root.Definitions.Length
                && root.Definitions[index].RuntimeId.Equals(recoveredItemRuntimeId)
                && root.Definitions[index].Kind == DefinitionKind.Item;
        }

        /// <summary>自然Block生成用IDが固定GUIDから再解決した正しい対応と一致するか確認します。</summary>
        /// <param name="actual">TerrainBlockCatalog Componentに保存された現在値です。</param>
        /// <param name="expected">共通DefinitionCatalogの固定GUIDから再解決した期待値です。</param>
        /// <returns>5種類の用途とRuntimeIdがすべて一致する場合はtrueです。</returns>
        private static bool HasSameBuiltInRuntimeIds(
            in BuiltInTerrainBlockRuntimeIds actual,
            in BuiltInTerrainBlockRuntimeIds expected)
        {
            return actual.IsValid
                && actual.LandGround.Equals(expected.LandGround)
                && actual.NaturalRockWall.Equals(expected.NaturalRockWall)
                && actual.FreshWater.Equals(expected.FreshWater)
                && actual.SaltWater.Equals(expected.SaltWater)
                && actual.Magma.Equals(expected.Magma);
        }

        /// <summary>Entity作成順を除外し、レイヤー、チャンクX、チャンクYだけで差分所有者を並べます。</summary>
        private struct OrderedDeltaOwner : IComparable<OrderedDeltaOwner>
        {
            /// <summary>並べ替え後にBufferを取得する実行時Entityです。比較キーには使用しません。</summary>
            public Entity Entity;

            /// <summary>保存対象となるレイヤー付き論理チャンク座標です。</summary>
            public TerrainChunkDeltaOwner Owner;

            /// <summary>レイヤー、X、Yの辞書順で比較します。</summary>
            /// <param name="other">比較する別の差分所有者です。</param>
            /// <returns>この座標が前なら負、同じなら0、後なら正です。</returns>
            public int CompareTo(OrderedDeltaOwner other)
            {
                int layerComparison = Owner.Layer.CompareTo(other.Owner.Layer);
                if (layerComparison != 0)
                {
                    return layerComparison;
                }

                int xComparison = Owner.ChunkX.CompareTo(other.Owner.ChunkX);
                return xComparison != 0
                    ? xComparison
                    : Owner.ChunkY.CompareTo(other.Owner.ChunkY);
            }

            /// <summary>別Entityが同じレイヤー付きチャンクを所有していないか確認します。</summary>
            /// <param name="other">比較する別の差分所有者です。</param>
            /// <returns>Layer、ChunkX、ChunkYがすべて同じ場合はtrueです。</returns>
            public bool HasSameCoordinate(OrderedDeltaOwner other)
            {
                return Owner.Layer == other.Owner.Layer
                    && Owner.ChunkX == other.Owner.ChunkX
                    && Owner.ChunkY == other.Owner.ChunkY;
            }
        }
    }
}
