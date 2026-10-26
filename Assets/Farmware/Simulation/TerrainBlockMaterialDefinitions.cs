// このファイルは、初回41地形材質の安定IDと、共通Item・Ground形態・Wall形態の明示的な対応を定義します。
// DefinitionCatalogAuthoringが共通定義へ登録し、後続のBlockパレット生成と採掘・再配置が同じ対応表を参照します。

using System;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.Simulation
{
    /// <summary>
    /// 初回41地形材質へ割り当てる変更禁止の数値IDです。
    /// 表示名やDefinitionの登録順から材質を推測せず、保存・生成・表示の共通キーとして使用します。
    /// </summary>
    public enum TerrainBlockMaterialId : byte
    {
        /// <summary>未設定または検証失敗を表す予約値です。</summary>
        None = 0,
        /// <summary>草を含む標準的な壌土です。</summary>
        GrassLoam = 1,
        /// <summary>草原の明るい表土です。</summary>
        MeadowSoil = 2,
        /// <summary>粘土質の表土です。</summary>
        ClaySoil = 3,
        /// <summary>乾燥した草地の土です。</summary>
        DryGrassSoil = 4,
        /// <summary>熱帯の赤色土です。</summary>
        TropicalRedSoil = 5,
        /// <summary>砂分を多く含む土です。</summary>
        SandySoil = 6,
        /// <summary>森林の壌土です。</summary>
        ForestLoam = 7,
        /// <summary>腐植を多く含む土です。</summary>
        HumusSoil = 8,
        /// <summary>山地の壌土です。</summary>
        MountainLoam = 9,
        /// <summary>高山の岩屑地です。</summary>
        AlpineScree = 10,
        /// <summary>島嶼の湿潤な壌土です。</summary>
        IslandLoam = 11,
        /// <summary>寒冷針葉樹林のポドゾル土です。</summary>
        PodzolSoil = 12,
        /// <summary>飽和した有機物由来の泥炭土です。</summary>
        PeatSoil = 13,
        /// <summary>ステップの乾いた表土です。</summary>
        SteppeSoil = 14,
        /// <summary>乾燥低木地の土です。</summary>
        DryScrubSoil = 15,
        /// <summary>礫を主体とする地面材質です。</summary>
        Gravel = 16,
        /// <summary>砂漠の礫地です。</summary>
        DesertGravel = 17,
        /// <summary>砂漠の砂地です。</summary>
        DesertSand = 18,
        /// <summary>湿原の泥です。</summary>
        MarshMud = 19,
        /// <summary>河川が堆積させた沖積土です。</summary>
        AlluvialSoil = 20,
        /// <summary>永久凍土傾向の土です。</summary>
        PermafrostSoil = 21,
        /// <summary>ツンドラの薄い表土です。</summary>
        TundraSoil = 22,
        /// <summary>火山灰を主体とする表土です。</summary>
        VolcanicAshSoil = 23,
        /// <summary>多孔質な火山礫です。</summary>
        Scoria = 24,
        /// <summary>玄武岩質の材質です。</summary>
        Basalt = 25,
        /// <summary>締め固まった未固結堆積物です。</summary>
        CompactedSediment = 26,
        /// <summary>砂岩質の材質です。</summary>
        Sandstone = 27,
        /// <summary>礫岩質の材質です。</summary>
        Conglomerate = 28,
        /// <summary>石灰岩質の材質です。</summary>
        Limestone = 29,
        /// <summary>ドロマイト質の材質です。</summary>
        Dolomite = 30,
        /// <summary>泥岩質の材質です。</summary>
        Mudstone = 31,
        /// <summary>粘板岩質の材質です。</summary>
        Slate = 32,
        /// <summary>花崗岩質の材質です。</summary>
        Granite = 33,
        /// <summary>花崗閃緑岩質の材質です。</summary>
        Granodiorite = 34,
        /// <summary>結晶片岩質の材質です。</summary>
        Schist = 35,
        /// <summary>片麻岩質の材質です。</summary>
        Gneiss = 36,
        /// <summary>珪岩質の材質です。</summary>
        Quartzite = 37,
        /// <summary>安山岩質の材質です。</summary>
        Andesite = 38,
        /// <summary>凝灰岩質の材質です。</summary>
        Tuff = 39,
        /// <summary>蛇紋岩質の材質です。</summary>
        Serpentinite = 40,
        /// <summary>かんらん岩質の材質です。</summary>
        Peridotite = 41
    }

    /// <summary>
    /// 1材質の安定IDと、回収Item・Ground形態・Wall形態の三者対応を保持します。
    /// GUID同士の関係を文字列加工で推測せず、このDefinitionを正として相互変換します。
    /// </summary>
    public struct TerrainBlockMaterialDefinition
    {
        /// <summary>名称や配列位置から独立した1〜41の安定材質IDです。</summary>
        public TerrainBlockMaterialId StableId;

        /// <summary>GroundとWallのどちらを回収しても得られる共通Itemの変更禁止GUIDです。</summary>
        public DefinitionGuid ItemGuid;

        /// <summary>穴または液体セルへ設置すると使うGroundBlockの変更禁止GUIDです。</summary>
        public DefinitionGuid GroundGuid;

        /// <summary>既存Ground上へ設置すると使うWallBlockの変更禁止GUIDです。</summary>
        public DefinitionGuid WallGuid;
    }

    /// <summary>
    /// 実行時IDへ解決済みの1材質Definitionです。
    /// 生成・採掘・配置が共通カタログを毎回GUID検索せず三者対応を参照するために使用します。
    /// </summary>
    public struct TerrainBlockMaterialDefinitionBlob
    {
        /// <summary>保存と固定生成で使用する1〜41の安定材質IDです。</summary>
        public TerrainBlockMaterialId StableId;

        /// <summary>インベントリで保持する共通Itemの実行時IDです。</summary>
        public DefinitionRuntimeId ItemRuntimeId;

        /// <summary>GroundBlock形態の実行時IDです。</summary>
        public DefinitionRuntimeId GroundRuntimeId;

        /// <summary>WallBlock形態の実行時IDです。</summary>
        public DefinitionRuntimeId WallRuntimeId;
    }

    /// <summary>
    /// 41材質を安定ID昇順で保持する読み取り専用Blob Assetです。
    /// 配列添字は検証後だけ安定IDから導出し、登録順には依存しません。
    /// </summary>
    public struct TerrainBlockMaterialCatalogBlob
    {
        /// <summary>安定ID1〜41に対応する解決済みDefinitionです。</summary>
        public BlobArray<TerrainBlockMaterialDefinitionBlob> Definitions;
    }

    /// <summary>
    /// Bakerが構築した地形材質対応表をECS Worldへ公開するSingleton Componentです。
    /// 静的Definitionなので固定Tickや地形編集では書き換えません。
    /// </summary>
    public struct TerrainBlockMaterialCatalog : IComponentData
    {
        /// <summary>41材質の解決済みItem・Ground・Wall対応を保持するBlob Assetです。</summary>
        public BlobAssetReference<TerrainBlockMaterialCatalogBlob> Value;
    }

    /// <summary>
    /// 初回41材質の固定表を作り、共通Definition入力と地形固有入力へ変換します。
    /// 固定GUIDの数値規則は新規定義作成時だけ使用し、実行時の材質解決は必ず明示対応表を参照します。
    /// </summary>
    public static class TerrainBlockMaterialDefinitions
    {
        /// <summary>有効な最小安定材質IDです。</summary>
        public const TerrainBlockMaterialId First = TerrainBlockMaterialId.GrassLoam;

        /// <summary>有効な最大安定材質IDです。</summary>
        public const TerrainBlockMaterialId Last = TerrainBlockMaterialId.Peridotite;

        /// <summary>初回リリースで固定する地形材質数です。</summary>
        public const int Count = (int)Last - (int)First + 1;

        /// <summary>各材質にItem、Ground、Wallの3共通Definitionがあることを表す件数です。</summary>
        public const int CommonDefinitionCount = Count * 3;

        /// <summary>各材質にGroundとWallの2地形Definitionがあることを表す件数です。</summary>
        public const int TerrainDefinitionCount = Count * 2;

        // GUID上位値を専用名前空間として固定し、既存の暫定地形5定義やInspector定義と区別します。
        private const ulong GuidNamespaceHigh = 0x4b9d67e23c1146a8UL;

        // 下位値は「材質ID×16 + 役割番号」で固定します。既存IDの意味を変えず、追加材質は末尾IDだけにします。
        private const ulong GuidNamespaceLow = 0xb71e000000000000UL;
        private const byte ItemRole = 1;
        private const byte GroundRole = 2;
        private const byte WallRole = 3;

        /// <summary>
        /// 安定ID順の41材質Definitionを作ります。
        /// </summary>
        /// <returns>各行がItem・Ground・Wallを明示保持する新しい配列です。</returns>
        public static TerrainBlockMaterialDefinition[] CreateDefault()
        {
            TerrainBlockMaterialDefinition[] definitions =
                new TerrainBlockMaterialDefinition[Count];
            for (int materialIndex = 0; materialIndex < Count; materialIndex++)
            {
                TerrainBlockMaterialId stableId =
                    (TerrainBlockMaterialId)(materialIndex + (int)First);
                definitions[materialIndex] = new TerrainBlockMaterialDefinition
                {
                    StableId = stableId,
                    ItemGuid = CreateStableGuid(stableId, ItemRole),
                    GroundGuid = CreateStableGuid(stableId, GroundRole),
                    WallGuid = CreateStableGuid(stableId, WallRole)
                };
            }

            return definitions;
        }

        /// <summary>
        /// 41材質のID完全性と三者GUIDの一意性を検証します。
        /// </summary>
        /// <param name="definitions">登録順を問わない材質Definition配列です。</param>
        /// <returns>ID1〜41が各1回あり、全123 GUIDが有効かつ一意ならtrueです。</returns>
        public static bool Validate(TerrainBlockMaterialDefinition[] definitions)
        {
            if (definitions == null || definitions.Length != Count)
            {
                return false;
            }

            bool[] foundStableIds = new bool[Count];
            var foundGuids = new System.Collections.Generic.HashSet<DefinitionGuid>();
            for (int definitionIndex = 0;
                 definitionIndex < definitions.Length;
                 definitionIndex++)
            {
                TerrainBlockMaterialDefinition definition = definitions[definitionIndex];
                int stableIndex = (int)definition.StableId - (int)First;
                if (stableIndex < 0
                    || stableIndex >= Count
                    || foundStableIds[stableIndex]
                    || !definition.ItemGuid.IsValid
                    || !definition.GroundGuid.IsValid
                    || !definition.WallGuid.IsValid
                    || !foundGuids.Add(definition.ItemGuid)
                    || !foundGuids.Add(definition.GroundGuid)
                    || !foundGuids.Add(definition.WallGuid))
                {
                    return false;
                }

                foundStableIds[stableIndex] = true;
            }

            return true;
        }

        /// <summary>
        /// 指定した安定材質IDの明示対応表を取得します。
        /// </summary>
        /// <param name="definitions">検証対象または既定の材質Definition配列です。</param>
        /// <param name="stableId">検索する1〜41の安定材質IDです。</param>
        /// <param name="definition">成功時にItem・Ground・WallのGUID対応を受け取ります。</param>
        /// <returns>有効なIDが配列内にちょうど存在する場合はtrueです。</returns>
        public static bool TryGetDefinition(
            TerrainBlockMaterialDefinition[] definitions,
            TerrainBlockMaterialId stableId,
            out TerrainBlockMaterialDefinition definition)
        {
            definition = default;
            if (definitions == null)
            {
                return false;
            }

            for (int definitionIndex = 0;
                 definitionIndex < definitions.Length;
                 definitionIndex++)
            {
                if (definitions[definitionIndex].StableId == stableId)
                {
                    definition = definitions[definitionIndex];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 41材質のItem・Ground・Wallを共通DefinitionCatalogへ追加する入力へ変換します。
        /// </summary>
        /// <param name="referenceStartIndex">先行定義が使用した参照総数です。</param>
        /// <param name="references">Ground・Wallから同じ材質Itemを指す82件の明示参照を受け取ります。</param>
        /// <returns>安定材質ID順、各行Item・Ground・Wall順の123入力です。</returns>
        public static DefinitionBuildEntry[] CreateCommonDefinitions(
            int referenceStartIndex,
            out DefinitionBuildReference[] references)
        {
            TerrainBlockMaterialDefinition[] materials = CreateDefault();
            DefinitionBuildEntry[] definitions =
                new DefinitionBuildEntry[CommonDefinitionCount];
            references = new DefinitionBuildReference[TerrainDefinitionCount];
            int destinationIndex = 0;
            int referenceIndex = 0;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                TerrainBlockMaterialDefinition material = materials[materialIndex];
                definitions[destinationIndex++] = CreateCommonDefinition(
                    material.ItemGuid,
                    DefinitionKind.Item,
                    referenceStartIndex + referenceIndex,
                    0);
                definitions[destinationIndex++] = CreateCommonDefinition(
                    material.GroundGuid,
                    DefinitionKind.GroundBlock,
                    referenceStartIndex + referenceIndex,
                    1);
                references[referenceIndex++] = new DefinitionBuildReference
                {
                    TargetGuid = material.ItemGuid,
                    ExpectedKind = DefinitionKind.Item
                };
                definitions[destinationIndex++] = CreateCommonDefinition(
                    material.WallGuid,
                    DefinitionKind.WallBlock,
                    referenceStartIndex + referenceIndex,
                    1);
                references[referenceIndex++] = new DefinitionBuildReference
                {
                    TargetGuid = material.ItemGuid,
                    ExpectedKind = DefinitionKind.Item
                };
            }

            return definitions;
        }

        /// <summary>
        /// 共通DefinitionCatalogからItem実行時IDを解決し、82件のGround・Wall地形入力を作ります。
        /// </summary>
        /// <param name="definitionCatalog">41材質の123共通Definitionを含む検証済みカタログです。</param>
        /// <param name="definitions">成功時にGround・Wallを交互に並べた地形固有入力を受け取ります。</param>
        /// <returns>全材質のItem・Ground・Wallと分類を解決できた場合はtrueです。</returns>
        public static bool TryCreateTerrainDefinitions(
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog,
            out TerrainBlockDefinitionBuildEntry[] definitions)
        {
            definitions = null;
            TerrainBlockMaterialDefinition[] materials = CreateDefault();
            if (!definitionCatalog.IsCreated || !Validate(materials))
            {
                return false;
            }

            TerrainBlockDefinitionBuildEntry[] result =
                new TerrainBlockDefinitionBuildEntry[TerrainDefinitionCount];
            int destinationIndex = 0;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                TerrainBlockMaterialDefinition material = materials[materialIndex];
                if (!TryResolveKind(
                        definitionCatalog,
                        material.ItemGuid,
                        DefinitionKind.Item,
                        out DefinitionRuntimeId itemRuntimeId)
                    || !TryResolveKind(
                        definitionCatalog,
                        material.GroundGuid,
                        DefinitionKind.GroundBlock,
                        out _)
                    || !TryResolveKind(
                        definitionCatalog,
                        material.WallGuid,
                        DefinitionKind.WallBlock,
                        out _))
                {
                    return false;
                }

                result[destinationIndex++] = new TerrainBlockDefinitionBuildEntry
                {
                    Guid = material.GroundGuid,
                    Kind = DefinitionKind.GroundBlock,
                    Hardness = 0,
                    RecoveredItemRuntimeId = itemRuntimeId,
                    Flags = TerrainBlockDefinitionFlags.AllowsNormalEquipment
                        | TerrainBlockDefinitionFlags.AllowsNormalBelt
                };
                result[destinationIndex++] = new TerrainBlockDefinitionBuildEntry
                {
                    Guid = material.WallGuid,
                    Kind = DefinitionKind.WallBlock,
                    Hardness = 0,
                    RecoveredItemRuntimeId = itemRuntimeId,
                    Flags = TerrainBlockDefinitionFlags.BlocksMovement
                        | TerrainBlockDefinitionFlags.Watertight
                        | TerrainBlockDefinitionFlags.FacilityBoundary
                };
            }

            definitions = result;
            return true;
        }

        /// <summary>材質IDと形態番号から新規定義作成時の固定GUIDを作ります。</summary>
        /// <param name="stableId">変更禁止の1〜41材質IDです。</param>
        /// <param name="role">Item、Ground、Wallを区別する1〜3の番号です。</param>
        /// <returns>材質と役割の組合せに一意な128bit GUIDです。</returns>
        private static DefinitionGuid CreateStableGuid(
            TerrainBlockMaterialId stableId,
            byte role)
        {
            ulong stableSuffix = ((ulong)stableId << 4) | role;
            return new DefinitionGuid(
                GuidNamespaceHigh,
                GuidNamespaceLow | stableSuffix);
        }

        /// <summary>指定された参照範囲を持つ共通Definition入力を1件作ります。</summary>
        /// <param name="guid">変更禁止の定義GUIDです。</param>
        /// <param name="kind">Item、GroundBlock、WallBlockのいずれかです。</param>
        /// <param name="referenceStartIndex">この定義の参照が始まる全体配列内の位置です。</param>
        /// <param name="referenceCount">この定義が持つ参照件数です。</param>
        /// <returns>共通カタログへ追加できるDefinition入力です。</returns>
        private static DefinitionBuildEntry CreateCommonDefinition(
            DefinitionGuid guid,
            DefinitionKind kind,
            int referenceStartIndex,
            int referenceCount)
        {
            return new DefinitionBuildEntry
            {
                Guid = guid,
                Kind = kind,
                ReferenceStartIndex = referenceStartIndex,
                ReferenceCount = referenceCount
            };
        }

        /// <summary>GUIDを解決し、期待した共通Definition分類かを確認します。</summary>
        /// <param name="catalog">検索する共通Definitionカタログです。</param>
        /// <param name="guid">検索する変更禁止GUIDです。</param>
        /// <param name="expectedKind">期待するItem、GroundBlock、WallBlock分類です。</param>
        /// <param name="runtimeId">成功時に解決済み実行時IDを受け取ります。</param>
        /// <returns>GUIDが存在し分類も一致する場合はtrueです。</returns>
        private static bool TryResolveKind(
            BlobAssetReference<DefinitionCatalogBlob> catalog,
            DefinitionGuid guid,
            DefinitionKind expectedKind,
            out DefinitionRuntimeId runtimeId)
        {
            runtimeId = default;
            return DefinitionCatalogLookup.TryResolve(catalog, guid, out runtimeId)
                && DefinitionCatalogLookup.TryGetDefinition(
                    catalog,
                    runtimeId,
                    out DefinitionBlobRecord definition)
                && definition.Kind == expectedKind;
        }
    }

    /// <summary>
    /// 41材質の三者対応を検証し、共通Definitionの実行時IDへ解決したBlob Assetを構築します。
    /// BakerとEditModeテストが同じ検証を使い、欠落・重複・分類違いを実行開始前に拒否します。
    /// </summary>
    public static class TerrainBlockMaterialCatalogBuilder
    {
        /// <summary>
        /// 材質Definitionを安定ID順へ正規化し、読み取り専用Blob Assetへ変換します。
        /// </summary>
        /// <param name="definitionCatalog">Item・Ground・Wallを登録済みの共通Definitionカタログです。</param>
        /// <param name="materials">登録順を問わない41材質の明示対応表です。</param>
        /// <param name="catalog">成功時に呼び出し側が登録またはDisposeするBlob Assetです。</param>
        /// <returns>完全な41材質を重複なく正しい分類へ解決できた場合はtrueです。</returns>
        public static bool TryBuild(
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog,
            TerrainBlockMaterialDefinition[] materials,
            out BlobAssetReference<TerrainBlockMaterialCatalogBlob> catalog)
        {
            catalog = default;
            if (!definitionCatalog.IsCreated
                || !TerrainBlockMaterialDefinitions.Validate(materials))
            {
                return false;
            }

            TerrainBlockMaterialDefinitionBlob[] resolved =
                new TerrainBlockMaterialDefinitionBlob[materials.Length];
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                TerrainBlockMaterialDefinition source = materials[materialIndex];
                if (!TryResolveKind(
                        definitionCatalog,
                        source.ItemGuid,
                        DefinitionKind.Item,
                        out DefinitionRuntimeId itemRuntimeId)
                    || !TryResolveKind(
                        definitionCatalog,
                        source.GroundGuid,
                        DefinitionKind.GroundBlock,
                        out DefinitionRuntimeId groundRuntimeId)
                    || !TryResolveKind(
                        definitionCatalog,
                        source.WallGuid,
                        DefinitionKind.WallBlock,
                        out DefinitionRuntimeId wallRuntimeId))
                {
                    return false;
                }

                int stableIndex = (int)source.StableId
                    - (int)TerrainBlockMaterialDefinitions.First;
                resolved[stableIndex] = new TerrainBlockMaterialDefinitionBlob
                {
                    StableId = source.StableId,
                    ItemRuntimeId = itemRuntimeId,
                    GroundRuntimeId = groundRuntimeId,
                    WallRuntimeId = wallRuntimeId
                };
            }

            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref TerrainBlockMaterialCatalogBlob root =
                    ref builder.ConstructRoot<TerrainBlockMaterialCatalogBlob>();
                BlobBuilderArray<TerrainBlockMaterialDefinitionBlob> definitions =
                    builder.Allocate(ref root.Definitions, resolved.Length);
                for (int materialIndex = 0; materialIndex < resolved.Length; materialIndex++)
                {
                    definitions[materialIndex] = resolved[materialIndex];
                }

                catalog = builder.CreateBlobAssetReference<TerrainBlockMaterialCatalogBlob>(
                    Allocator.Persistent);
                return true;
            }
            finally
            {
                // 一時Builderだけを解放し、Persistent Blobの所有権は呼び出し側へ渡します。
                builder.Dispose();
            }
        }

        /// <summary>GUIDを解決し、材質表が要求するDefinition分類と一致するか確認します。</summary>
        /// <param name="catalog">検索する共通Definitionカタログです。</param>
        /// <param name="guid">Item、Ground、WallのいずれかのGUIDです。</param>
        /// <param name="expectedKind">材質表が要求するDefinition分類です。</param>
        /// <param name="runtimeId">成功時に解決済み実行時IDを受け取ります。</param>
        /// <returns>GUIDが存在し、分類も一致する場合はtrueです。</returns>
        private static bool TryResolveKind(
            BlobAssetReference<DefinitionCatalogBlob> catalog,
            DefinitionGuid guid,
            DefinitionKind expectedKind,
            out DefinitionRuntimeId runtimeId)
        {
            runtimeId = default;
            return DefinitionCatalogLookup.TryResolve(catalog, guid, out runtimeId)
                && DefinitionCatalogLookup.TryGetDefinition(
                    catalog,
                    runtimeId,
                    out DefinitionBlobRecord definition)
                && definition.Kind == expectedKind;
        }
    }

    /// <summary>
    /// 実行時の安定材質IDから、共通Item・Ground・Wallの実行時IDを取得します。
    /// 有効範囲を検証してから連続配列を参照し、未設定IDを別材質として読みません。
    /// </summary>
    public static class TerrainBlockMaterialCatalogLookup
    {
        /// <summary>
        /// 指定した安定材質IDの解決済みDefinitionを取得します。
        /// </summary>
        /// <param name="catalog">Bakerが構築した41材質カタログです。</param>
        /// <param name="stableId">検索する1〜41の安定材質IDです。</param>
        /// <param name="definition">成功時にItem・Ground・Wallの実行時IDを受け取ります。</param>
        /// <returns>カタログとIDが有効で、対応行が一致する場合はtrueです。</returns>
        public static bool TryGetDefinition(
            BlobAssetReference<TerrainBlockMaterialCatalogBlob> catalog,
            TerrainBlockMaterialId stableId,
            out TerrainBlockMaterialDefinitionBlob definition)
        {
            definition = default;
            if (!catalog.IsCreated)
            {
                return false;
            }

            int stableIndex = (int)stableId
                - (int)TerrainBlockMaterialDefinitions.First;
            ref TerrainBlockMaterialCatalogBlob root = ref catalog.Value;
            if (stableIndex < 0 || stableIndex >= root.Definitions.Length)
            {
                return false;
            }

            TerrainBlockMaterialDefinitionBlob candidate = root.Definitions[stableIndex];
            if (candidate.StableId != stableId)
            {
                return false;
            }

            definition = candidate;
            return true;
        }
    }
}
