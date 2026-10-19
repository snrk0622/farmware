// このファイルは、地面・壁・液体・地表を共通定義GUIDへ結び付け、地形ルール用の軽量なBlob Assetへ変換します。
// DefinitionCatalogが永続的な識別を担当し、このカタログは移動・建築が毎回GUIDや文字列を調べず属性を読めるようにします。

using System;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.Simulation
{
    /// <summary>
    /// 地形定義が移動、建築、水密、施設境界へ与える性質をbit単位で保持します。
    /// 同じ定義へ複数の性質を組み合わせ、判定側がBiomeや表示名から意味を推測しないために使用します。
    /// </summary>
    [Flags]
    public enum TerrainBlockDefinitionFlags : ushort
    {
        /// <summary>追加の性質を持たない定義です。</summary>
        None = 0,

        /// <summary>通常の水平方向移動を遮ります。</summary>
        BlocksMovement = 1 << 0,

        /// <summary>通常設備を直接置ける地面です。</summary>
        AllowsNormalEquipment = 1 << 1,

        /// <summary>通常ベルトを直接置ける地面です。</summary>
        AllowsNormalBelt = 1 << 2,

        /// <summary>橋を上へ設置できる穴または液体です。</summary>
        AllowsBridge = 1 << 3,

        /// <summary>進入または橋の設置に耐熱能力を要求します。</summary>
        RequiresHeatResistance = 1 << 4,

        /// <summary>液体を通さない境界として扱えます。</summary>
        Watertight = 1 << 5,

        /// <summary>部屋や施設の閉鎖境界として扱えます。</summary>
        FacilityBoundary = 1 << 6
    }

    /// <summary>
    /// Authoringまたは組み込み定義を地形定義Blobへ変換するための一時入力です。
    /// 構築後は保持せず、実行中は解決済みRuntimeIdを持つTerrainBlockDefinitionBlobを使用します。
    /// </summary>
    public struct TerrainBlockDefinitionBuildEntry
    {
        /// <summary>DefinitionCatalogへ登録済みである必要がある変更禁止GUIDです。</summary>
        public DefinitionGuid Guid;

        /// <summary>GroundBlock、WallBlock、Liquid、TerrainSurfaceのいずれかです。</summary>
        public DefinitionKind Kind;

        /// <summary>採掘等が消費する抽象作業量です。0はTERRAIN-01A時点で未設定であることを表します。</summary>
        public ushort Hardness;

        /// <summary>回収時に得るItem定義です。未実装または回収なしならInvalidです。</summary>
        public DefinitionRuntimeId RecoveredItemRuntimeId;

        /// <summary>移動・建築・水密・施設境界が参照する性質です。</summary>
        public TerrainBlockDefinitionFlags Flags;
    }

    /// <summary>
    /// 1種類の地形ブロックについて、共通定義の実行時IDとゲームルール属性を保持します。
    /// 値はBakerが作成した後は変更せず、全セルから共有します。
    /// </summary>
    public struct TerrainBlockDefinitionBlob
    {
        /// <summary>DefinitionCatalog内でGUID順に割り当てられた実行時IDです。</summary>
        public DefinitionRuntimeId RuntimeId;

        /// <summary>地面、壁、液体、地表のどの区画へ設定できる定義かを表します。</summary>
        public DefinitionKind Kind;

        /// <summary>採掘等が消費する抽象作業量です。単位の最終調整はGATHER-01で行います。</summary>
        public ushort Hardness;

        /// <summary>回収時に得るItem定義です。Invalidなら回収品はまだ接続されていません。</summary>
        public DefinitionRuntimeId RecoveredItemRuntimeId;

        /// <summary>移動・建築・水密・施設境界が参照する検証済み性質です。</summary>
        public TerrainBlockDefinitionFlags Flags;
    }

    /// <summary>
    /// 全地形定義をRuntimeId昇順で保持する読み取り専用Blob Assetのルートです。
    /// RuntimeIdは他分類を挟むため連続とは限らず、検索はTerrainBlockCatalogLookupが担当します。
    /// </summary>
    public struct TerrainBlockCatalogBlob
    {
        /// <summary>RuntimeId昇順へ正規化した地形定義です。</summary>
        public BlobArray<TerrainBlockDefinitionBlob> Definitions;
    }

    /// <summary>
    /// 現在の互換生成器が出力する地形分類を、新しいブロック定義へ変換するための解決済みID一式です。
    /// TERRAIN-01Bで生成器がBlockを直接出力するまでの境界として使用します。
    /// </summary>
    public struct BuiltInTerrainBlockRuntimeIds
    {
        /// <summary>既存Landを表す地面定義です。</summary>
        public DefinitionRuntimeId LandGround;

        /// <summary>既存RockWallを表す壁定義です。</summary>
        public DefinitionRuntimeId NaturalRockWall;

        /// <summary>既存FreshWaterを表す液体定義です。</summary>
        public DefinitionRuntimeId FreshWater;

        /// <summary>既存SaltWaterを表す液体定義です。</summary>
        public DefinitionRuntimeId SaltWater;

        /// <summary>既存Magmaを表す液体定義です。</summary>
        public DefinitionRuntimeId Magma;

        /// <summary>互換変換に必要な5定義がすべて解決済みかを返します。</summary>
        public bool IsValid => LandGround.IsValid
            && NaturalRockWall.IsValid
            && FreshWater.IsValid
            && SaltWater.IsValid
            && Magma.IsValid;
    }

    /// <summary>
    /// Bakerが構築した地形定義と互換変換用IDをECS Worldへ公開するSingleton Componentです。
    /// 全セルが共有する静的状態であり、地形編集によって書き換えません。
    /// </summary>
    public struct TerrainBlockCatalog : IComponentData
    {
        /// <summary>検証済み地形定義の読み取り専用Blob Assetです。</summary>
        public BlobAssetReference<TerrainBlockCatalogBlob> Value;

        /// <summary>既存GeneratedTileを新しいブロック参照へ移すための解決済みIDです。</summary>
        public BuiltInTerrainBlockRuntimeIds BuiltInRuntimeIds;
    }

    /// <summary>
    /// TERRAIN-01Aで既存Surface生成を新しい有効セルへ接続する最小の組み込み定義です。
    /// GUIDはセーブ互換境界になるため変更せず、追加定義は別GUIDとして増やします。
    /// </summary>
    public static class BuiltInTerrainBlockDefinitions
    {
        /// <summary>通常地面の変更禁止GUIDです。</summary>
        public static readonly DefinitionGuid LandGroundGuid =
            new DefinitionGuid(0x8f5c1c7a25b84d10UL, 0xa1d63a6ff1f9a001UL);

        /// <summary>自然岩壁の変更禁止GUIDです。</summary>
        public static readonly DefinitionGuid NaturalRockWallGuid =
            new DefinitionGuid(0x8f5c1c7a25b84d10UL, 0xa1d63a6ff1f9a002UL);

        /// <summary>淡水の変更禁止GUIDです。</summary>
        public static readonly DefinitionGuid FreshWaterGuid =
            new DefinitionGuid(0x8f5c1c7a25b84d10UL, 0xa1d63a6ff1f9a003UL);

        /// <summary>海水の変更禁止GUIDです。</summary>
        public static readonly DefinitionGuid SaltWaterGuid =
            new DefinitionGuid(0x8f5c1c7a25b84d10UL, 0xa1d63a6ff1f9a004UL);

        /// <summary>マグマの変更禁止GUIDです。</summary>
        public static readonly DefinitionGuid MagmaGuid =
            new DefinitionGuid(0x8f5c1c7a25b84d10UL, 0xa1d63a6ff1f9a005UL);

        /// <summary>
        /// 共通DefinitionCatalogへ追加する5件の定義入力を作ります。
        /// </summary>
        /// <param name="referenceStartIndex">先行する定義が使用した参照総数です。組み込み定義は参照を追加しません。</param>
        /// <returns>既存入力の末尾へ順番どおり追加できる共通定義配列です。</returns>
        public static DefinitionBuildEntry[] CreateCommonDefinitions(int referenceStartIndex)
        {
            return new[]
            {
                CreateCommonDefinition(LandGroundGuid, DefinitionKind.GroundBlock, referenceStartIndex),
                CreateCommonDefinition(NaturalRockWallGuid, DefinitionKind.WallBlock, referenceStartIndex),
                CreateCommonDefinition(FreshWaterGuid, DefinitionKind.Liquid, referenceStartIndex),
                CreateCommonDefinition(SaltWaterGuid, DefinitionKind.Liquid, referenceStartIndex),
                CreateCommonDefinition(MagmaGuid, DefinitionKind.Liquid, referenceStartIndex)
            };
        }

        /// <summary>
        /// 地形固有カタログへ入れる最小のゲームルール属性を作ります。
        /// 採掘硬度と回収品はGATHER-01まで未設定とし、現行の移動・配置結果だけを維持します。
        /// </summary>
        /// <returns>共通定義GUIDへ接続する5件の地形固有入力です。</returns>
        public static TerrainBlockDefinitionBuildEntry[] CreateTerrainDefinitions()
        {
            return new[]
            {
                CreateTerrainDefinition(
                    LandGroundGuid,
                    DefinitionKind.GroundBlock,
                    TerrainBlockDefinitionFlags.AllowsNormalEquipment |
                    TerrainBlockDefinitionFlags.AllowsNormalBelt),
                CreateTerrainDefinition(
                    NaturalRockWallGuid,
                    DefinitionKind.WallBlock,
                    TerrainBlockDefinitionFlags.BlocksMovement |
                    TerrainBlockDefinitionFlags.Watertight |
                    TerrainBlockDefinitionFlags.FacilityBoundary),
                CreateTerrainDefinition(
                    FreshWaterGuid,
                    DefinitionKind.Liquid,
                    TerrainBlockDefinitionFlags.AllowsBridge),
                CreateTerrainDefinition(
                    SaltWaterGuid,
                    DefinitionKind.Liquid,
                    TerrainBlockDefinitionFlags.AllowsBridge),
                CreateTerrainDefinition(
                    MagmaGuid,
                    DefinitionKind.Liquid,
                    TerrainBlockDefinitionFlags.AllowsBridge |
                    TerrainBlockDefinitionFlags.RequiresHeatResistance)
            };
        }

        /// <summary>
        /// 共通カタログから互換生成に必要な5件の実行時IDを解決します。
        /// </summary>
        /// <param name="catalog">Bakerが構築した共通定義カタログです。</param>
        /// <param name="runtimeIds">成功時に既存分類からBlockへ変換するID一式を受け取ります。</param>
        /// <returns>5件すべてが共通カタログに存在する場合はtrueです。</returns>
        public static bool TryResolveRuntimeIds(
            BlobAssetReference<DefinitionCatalogBlob> catalog,
            out BuiltInTerrainBlockRuntimeIds runtimeIds)
        {
            runtimeIds = default;
            bool succeeded = DefinitionCatalogLookup.TryResolve(
                    catalog,
                    LandGroundGuid,
                    out runtimeIds.LandGround)
                && DefinitionCatalogLookup.TryResolve(
                    catalog,
                    NaturalRockWallGuid,
                    out runtimeIds.NaturalRockWall)
                && DefinitionCatalogLookup.TryResolve(
                    catalog,
                    FreshWaterGuid,
                    out runtimeIds.FreshWater)
                && DefinitionCatalogLookup.TryResolve(
                    catalog,
                    SaltWaterGuid,
                    out runtimeIds.SaltWater)
                && DefinitionCatalogLookup.TryResolve(
                    catalog,
                    MagmaGuid,
                    out runtimeIds.Magma);
            return succeeded && runtimeIds.IsValid;
        }

        /// <summary>参照を持たない組み込み共通定義を1件作ります。</summary>
        /// <param name="guid">定義作成後に変更しない共通GUIDです。</param>
        /// <param name="kind">地面、壁、液体のいずれかの分類です。</param>
        /// <param name="referenceStartIndex">先行定義が使用済みの参照総数です。</param>
        /// <returns>共通カタログ入力へ追加できる参照0件の定義です。</returns>
        private static DefinitionBuildEntry CreateCommonDefinition(
            DefinitionGuid guid,
            DefinitionKind kind,
            int referenceStartIndex)
        {
            return new DefinitionBuildEntry
            {
                Guid = guid,
                Kind = kind,
                ReferenceStartIndex = referenceStartIndex,
                ReferenceCount = 0
            };
        }

        /// <summary>未調整の硬度・回収品と、確定済みルール属性を持つ組み込み地形定義を作ります。</summary>
        /// <param name="guid">共通定義へ接続する変更禁止GUIDです。</param>
        /// <param name="kind">地面、壁、液体のいずれかの分類です。</param>
        /// <param name="flags">現行地形結果を維持する移動・配置属性です。</param>
        /// <returns>地形固有カタログへ追加できる入力です。</returns>
        private static TerrainBlockDefinitionBuildEntry CreateTerrainDefinition(
            DefinitionGuid guid,
            DefinitionKind kind,
            TerrainBlockDefinitionFlags flags)
        {
            return new TerrainBlockDefinitionBuildEntry
            {
                Guid = guid,
                Kind = kind,
                Hardness = 0,
                RecoveredItemRuntimeId = default,
                Flags = flags
            };
        }
    }

    /// <summary>
    /// 共通DefinitionCatalogと地形固有入力を照合し、RuntimeId順のTerrainBlockCatalogを構築します。
    /// BakerとEditModeテストが同じ検証経路を使い、分類違い、欠落、重複、未登録回収品を実行前に拒否します。
    /// </summary>
    public static class TerrainBlockCatalogBuilder
    {
        /// <summary>
        /// 地形固有入力を検証して読み取り専用Blob Assetを作成します。
        /// </summary>
        /// <param name="definitionCatalog">先に構築済みの共通定義カタログです。</param>
        /// <param name="entries">全GroundBlock、WallBlock、Liquid、TerrainSurfaceに一対一で対応する入力です。</param>
        /// <param name="catalog">成功時に呼び出し側が登録またはDisposeするBlob Assetです。</param>
        /// <returns>全地形定義を重複なく解決できた場合はtrueです。</returns>
        public static bool TryBuild(
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog,
            TerrainBlockDefinitionBuildEntry[] entries,
            out BlobAssetReference<TerrainBlockCatalogBlob> catalog)
        {
            catalog = default;
            if (!definitionCatalog.IsCreated || entries == null)
            {
                return false;
            }

            ref DefinitionCatalogBlob commonRoot = ref definitionCatalog.Value;
            int expectedCount = CountTerrainDefinitions(ref commonRoot);
            if (entries.Length != expectedCount)
            {
                return false;
            }

            ResolvedTerrainDefinition[] resolved =
                new ResolvedTerrainDefinition[entries.Length];
            for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
            {
                TerrainBlockDefinitionBuildEntry entry = entries[entryIndex];
                if (!IsTerrainKind(entry.Kind)
                    || !DefinitionCatalogLookup.TryResolve(
                        definitionCatalog,
                        entry.Guid,
                        out DefinitionRuntimeId runtimeId)
                    || !DefinitionCatalogLookup.TryGetDefinition(
                        definitionCatalog,
                        runtimeId,
                        out DefinitionBlobRecord commonDefinition)
                    || commonDefinition.Kind != entry.Kind
                    || !IsRecoveredItemValid(
                        definitionCatalog,
                        entry.RecoveredItemRuntimeId))
                {
                    return false;
                }

                resolved[entryIndex] = new ResolvedTerrainDefinition
                {
                    RuntimeId = runtimeId,
                    Entry = entry
                };
            }

            Array.Sort(resolved);
            for (int entryIndex = 1; entryIndex < resolved.Length; entryIndex++)
            {
                if (resolved[entryIndex - 1].RuntimeId.Equals(
                    resolved[entryIndex].RuntimeId))
                {
                    return false;
                }
            }

            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref TerrainBlockCatalogBlob root =
                    ref builder.ConstructRoot<TerrainBlockCatalogBlob>();
                BlobBuilderArray<TerrainBlockDefinitionBlob> definitions =
                    builder.Allocate(ref root.Definitions, resolved.Length);
                for (int entryIndex = 0; entryIndex < resolved.Length; entryIndex++)
                {
                    ResolvedTerrainDefinition source = resolved[entryIndex];
                    definitions[entryIndex] = new TerrainBlockDefinitionBlob
                    {
                        RuntimeId = source.RuntimeId,
                        Kind = source.Entry.Kind,
                        Hardness = source.Entry.Hardness,
                        RecoveredItemRuntimeId = source.Entry.RecoveredItemRuntimeId,
                        Flags = source.Entry.Flags
                    };
                }

                catalog = builder.CreateBlobAssetReference<TerrainBlockCatalogBlob>(
                    Allocator.Persistent);
                return true;
            }
            finally
            {
                // BlobBuilderの一時領域だけを解放し、Persistent Blobの所有権は呼び出し側へ渡します。
                builder.Dispose();
            }
        }

        /// <summary>共通カタログに登録された地形4分類の総数を数えます。</summary>
        /// <param name="root">検証対象となる共通定義カタログのルートです。</param>
        /// <returns>GroundBlock、WallBlock、Liquid、TerrainSurfaceの定義総数です。</returns>
        private static int CountTerrainDefinitions(ref DefinitionCatalogBlob root)
        {
            int count = 0;
            for (int definitionIndex = 0;
                 definitionIndex < root.Definitions.Length;
                 definitionIndex++)
            {
                if (IsTerrainKind(root.Definitions[definitionIndex].Kind))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>地形カタログへ格納できる分類かを確認します。</summary>
        /// <param name="kind">共通カタログ内の定義分類です。</param>
        /// <returns>地形4分類のいずれかならtrueです。</returns>
        private static bool IsTerrainKind(DefinitionKind kind)
        {
            return kind >= DefinitionKind.GroundBlock
                && kind <= DefinitionKind.TerrainSurface;
        }

        /// <summary>任意の回収品参照が未設定、またはItem分類を指していることを確認します。</summary>
        /// <param name="definitionCatalog">回収品の分類を照合する共通定義カタログです。</param>
        /// <param name="recoveredItemRuntimeId">地形定義が任意で参照する回収ItemのIDです。</param>
        /// <returns>未設定、または登録済みItem分類ならtrueです。</returns>
        private static bool IsRecoveredItemValid(
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog,
            DefinitionRuntimeId recoveredItemRuntimeId)
        {
            if (!recoveredItemRuntimeId.IsValid)
            {
                return true;
            }

            return DefinitionCatalogLookup.TryGetDefinition(
                    definitionCatalog,
                    recoveredItemRuntimeId,
                    out DefinitionBlobRecord recoveredItem)
                && recoveredItem.Kind == DefinitionKind.Item;
        }

        /// <summary>解決済みRuntimeIdと元入力を並べ替えるための一時値です。</summary>
        private struct ResolvedTerrainDefinition : IComparable<ResolvedTerrainDefinition>
        {
            /// <summary>共通カタログから解決した実行時IDです。</summary>
            public DefinitionRuntimeId RuntimeId;

            /// <summary>地形固有のルール入力です。</summary>
            public TerrainBlockDefinitionBuildEntry Entry;

            /// <summary>RuntimeId昇順の安定したBlob配列を作るために比較します。</summary>
            /// <param name="other">比較する別の解決済み地形定義です。</param>
            /// <returns>このIDが前なら負、同じなら0、後なら正です。</returns>
            public int CompareTo(ResolvedTerrainDefinition other)
            {
                return RuntimeId.Value.CompareTo(other.RuntimeId.Value);
            }
        }
    }

    /// <summary>
    /// 地形定義Blobから実行時IDに対応する属性を取得します。
    /// 配列はRuntimeId昇順ですが他分類を挟むため、添字計算ではなく二分探索を使用します。
    /// </summary>
    public static class TerrainBlockCatalogLookup
    {
        /// <summary>
        /// 指定した実行時IDに対応する地形定義を取得します。
        /// </summary>
        /// <param name="catalog">Bakerが作成した地形定義カタログです。</param>
        /// <param name="runtimeId">セルに保存された共通定義の実行時IDです。</param>
        /// <param name="definition">成功時に分類とルール属性を受け取ります。</param>
        /// <returns>カタログとIDが有効で、対応定義が存在する場合はtrueです。</returns>
        public static bool TryGetDefinition(
            BlobAssetReference<TerrainBlockCatalogBlob> catalog,
            DefinitionRuntimeId runtimeId,
            out TerrainBlockDefinitionBlob definition)
        {
            definition = default;
            if (!catalog.IsCreated || !runtimeId.IsValid)
            {
                return false;
            }

            ref TerrainBlockCatalogBlob root = ref catalog.Value;
            int lowerIndex = 0;
            int upperIndex = root.Definitions.Length - 1;
            while (lowerIndex <= upperIndex)
            {
                int middleIndex = lowerIndex + ((upperIndex - lowerIndex) / 2);
                TerrainBlockDefinitionBlob candidate = root.Definitions[middleIndex];
                int comparison = candidate.RuntimeId.Value.CompareTo(runtimeId.Value);
                if (comparison == 0)
                {
                    definition = candidate;
                    return true;
                }

                if (comparison < 0)
                {
                    lowerIndex = middleIndex + 1;
                }
                else
                {
                    upperIndex = middleIndex - 1;
                }
            }

            return false;
        }
    }
}
