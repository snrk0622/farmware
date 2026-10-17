// このファイルは、動物種のAuthoring入力を共通DefinitionCatalogへ接続し、検証済みBlob Assetを構築します。
// BakerとEditModeテストが同じ処理を使うことで、生成開始後に定義漏れや単位不整合が判明することを防ぎます。

using System;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.Simulation.Animals
{
    /// <summary>
    /// 1種類の動物設定をBlob Assetへ変換するための一時入力です。
    /// GUIDは共通DefinitionCatalogでAnimalSpeciesに分類された定義へ解決され、構築完了後は保持しません。
    /// </summary>
    public struct AnimalSpeciesDefinitionBuildEntry
    {
        /// <summary>共通DefinitionCatalogに登録済みの変更禁止GUIDです。</summary>
        public DefinitionGuid DefinitionGuid;

        /// <summary>出現を許可する1種類以上のバイオームMaskです。</summary>
        public AnimalBiomeMask AllowedBiomes;

        /// <summary>出現・滞在を許可する1種類以上の基礎地形Maskです。</summary>
        public AnimalGroundMask AllowedGrounds;

        /// <summary>1群れの最小個体数です。単位は個体です。</summary>
        public byte MinimumHerdSize;

        /// <summary>1群れの最大個体数です。単位は個体です。</summary>
        public byte MaximumHerdSize;

        /// <summary>有効な地形4096タイル当たりの目標個体数です。</summary>
        public ushort TargetPopulationPer4096EligibleTiles;

        /// <summary>次の行動まで待つ最小時間です。単位は固定Tickです。</summary>
        public uint MinimumIdleTicks;

        /// <summary>次の行動まで待つ最大時間です。単位は固定Tickです。</summary>
        public uint MaximumIdleTicks;

        /// <summary>隣接タイルへ1歩進む時間です。単位は固定Tickです。</summary>
        public uint MovementStepIntervalTicks;

        /// <summary>群れの基準位置から離れてよい最大距離です。単位はタイルです。</summary>
        public ushort MaximumWanderDistanceInTiles;

        /// <summary>生成時に選択できる外見差分の数です。</summary>
        public byte AppearanceVariantCount;
    }

    /// <summary>
    /// 動物種カタログを構築できなかった理由です。
    /// Bakerは日本語エラーへ変換し、テストは失敗した契約をこの値で特定します。
    /// </summary>
    public enum AnimalSpeciesCatalogValidationErrorCode : byte
    {
        /// <summary>検証とBlob Asset構築が成功しました。</summary>
        None = 0,

        /// <summary>動物種設定の入力配列がnullです。</summary>
        DefinitionsAreNull = 1,

        /// <summary>共通DefinitionCatalogが未作成です。</summary>
        DefinitionCatalogIsInvalid = 2,

        /// <summary>動物種設定のGUIDが未設定です。</summary>
        DefinitionGuidIsInvalid = 3,

        /// <summary>GUIDに対応する共通定義がありません。</summary>
        DefinitionWasNotFound = 4,

        /// <summary>GUIDに対応する共通定義がAnimalSpecies分類ではありません。</summary>
        DefinitionKindMismatch = 5,

        /// <summary>同じ動物種へ複数の設定が入力されています。</summary>
        DefinitionIsDuplicated = 6,

        /// <summary>共通カタログの動物種に対応する設定がありません。</summary>
        DefinitionIsMissing = 7,

        /// <summary>バイオームMaskが空か、未定義bitを含みます。</summary>
        AllowedBiomesAreInvalid = 8,

        /// <summary>基礎地形Maskが空か、未定義bitを含みます。</summary>
        AllowedGroundsAreInvalid = 9,

        /// <summary>群れの最小値が0か、最大値を超えています。</summary>
        HerdSizeRangeIsInvalid = 10,

        /// <summary>有効地形4096タイル当たりの目標個体数が0です。</summary>
        PopulationDensityIsInvalid = 11,

        /// <summary>最小待機Tickが最大待機Tickを超えています。</summary>
        IdleTickRangeIsInvalid = 12,

        /// <summary>1歩の移動に必要な固定Tick数が0です。</summary>
        MovementStepIntervalIsInvalid = 13,

        /// <summary>通常の徘徊で許可する最大距離が0です。</summary>
        WanderDistanceIsInvalid = 14,

        /// <summary>選択可能な外見差分数が0です。</summary>
        AppearanceVariantCountIsInvalid = 15
    }

    /// <summary>
    /// 最初に見つかった動物種設定の検証エラーと入力位置を保持します。
    /// Inspector上の修正対象と重複相手を、例外メッセージとテストへ伝えるために使用します。
    /// </summary>
    public struct AnimalSpeciesCatalogValidationError
    {
        /// <summary>失敗理由です。成功時はNoneです。</summary>
        public AnimalSpeciesCatalogValidationErrorCode Code;

        /// <summary>問題があるAnimalSpeciesDefinitionBuildEntryの入力添字です。対象がなければ-1です。</summary>
        public int DefinitionIndex;

        /// <summary>重複相手の入力添字です。対象がなければ-1です。</summary>
        public int RelatedDefinitionIndex;

        /// <summary>設定が欠けている共通DefinitionCatalog上の実行時IDです。対象がなければ0です。</summary>
        public DefinitionRuntimeId MissingRuntimeId;
    }

    /// <summary>
    /// 共通定義と動物固有設定を照合し、実行時ID昇順のAnimalSpeciesCatalogBlobを作成します。
    /// この処理はBakerまたはEditModeテストからだけ呼び、ゲームの通常Tickでは実行しません。
    /// </summary>
    public static class AnimalSpeciesCatalogBuilder
    {
        /// <summary>
        /// 全入力を検証し、成功時だけ永続AllocatorのBlob Assetを返します。
        /// </summary>
        /// <param name="definitionCatalog">GUIDと分類を解決する共通定義カタログです。</param>
        /// <param name="definitions">Inspectorから変換した動物種固有設定です。</param>
        /// <param name="catalog">成功時に呼び出し側がBakerへ登録するかDisposeするBlob Assetです。</param>
        /// <param name="error">失敗時の理由と入力位置です。</param>
        /// <returns>全定義が一対一で有効ならtrueです。</returns>
        public static bool TryBuild(
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog,
            AnimalSpeciesDefinitionBuildEntry[] definitions,
            out BlobAssetReference<AnimalSpeciesCatalogBlob> catalog,
            out AnimalSpeciesCatalogValidationError error)
        {
            catalog = default;
            error = CreateSuccess();
            if (!definitionCatalog.IsCreated)
            {
                error = CreateError(
                    AnimalSpeciesCatalogValidationErrorCode.DefinitionCatalogIsInvalid);
                return false;
            }

            if (definitions == null)
            {
                error = CreateError(
                    AnimalSpeciesCatalogValidationErrorCode.DefinitionsAreNull);
                return false;
            }

            DefinitionSortEntry[] sortedDefinitions =
                new DefinitionSortEntry[definitions.Length];
            for (int definitionIndex = 0;
                 definitionIndex < definitions.Length;
                 definitionIndex++)
            {
                AnimalSpeciesDefinitionBuildEntry definition =
                    definitions[definitionIndex];
                if (!definition.DefinitionGuid.IsValid)
                {
                    error = CreateError(
                        AnimalSpeciesCatalogValidationErrorCode.DefinitionGuidIsInvalid,
                        definitionIndex);
                    return false;
                }

                if (!DefinitionCatalogLookup.TryResolve(
                        definitionCatalog,
                        definition.DefinitionGuid,
                        out DefinitionRuntimeId runtimeId)
                    || !DefinitionCatalogLookup.TryGetDefinition(
                        definitionCatalog,
                        runtimeId,
                        out DefinitionBlobRecord definitionRecord))
                {
                    error = CreateError(
                        AnimalSpeciesCatalogValidationErrorCode.DefinitionWasNotFound,
                        definitionIndex);
                    return false;
                }

                if (definitionRecord.Kind != DefinitionKind.AnimalSpecies)
                {
                    error = CreateError(
                        AnimalSpeciesCatalogValidationErrorCode.DefinitionKindMismatch,
                        definitionIndex);
                    return false;
                }

                if (!TryValidateDefinition(definition, definitionIndex, out error))
                {
                    return false;
                }

                sortedDefinitions[definitionIndex] = new DefinitionSortEntry
                {
                    Definition = definition,
                    RuntimeId = runtimeId,
                    OriginalIndex = definitionIndex
                };
            }

            Array.Sort(sortedDefinitions);
            for (int sortedIndex = 1;
                 sortedIndex < sortedDefinitions.Length;
                 sortedIndex++)
            {
                if (sortedDefinitions[sortedIndex - 1].RuntimeId.Equals(
                    sortedDefinitions[sortedIndex].RuntimeId))
                {
                    error = CreateError(
                        AnimalSpeciesCatalogValidationErrorCode.DefinitionIsDuplicated,
                        sortedDefinitions[sortedIndex].OriginalIndex,
                        sortedDefinitions[sortedIndex - 1].OriginalIndex);
                    return false;
                }
            }

            if (!TryFindMissingDefinition(
                    definitionCatalog,
                    sortedDefinitions,
                    out DefinitionRuntimeId missingRuntimeId))
            {
                error = CreateError(
                    AnimalSpeciesCatalogValidationErrorCode.DefinitionIsMissing,
                    missingRuntimeId: missingRuntimeId);
                return false;
            }

            catalog = CreateBlobAsset(sortedDefinitions);
            return true;
        }

        /// <summary>
        /// 1件の数値範囲とMaskを検証し、0や未定義bitを実行時設定へ持ち込まないようにします。
        /// </summary>
        /// <param name="definition">検証する動物種固有設定です。</param>
        /// <param name="definitionIndex">エラーへ記録する元入力添字です。</param>
        /// <param name="error">失敗時の理由です。</param>
        /// <returns>全フィールドを後続Systemが追加防御なしで読める場合はtrueです。</returns>
        private static bool TryValidateDefinition(
            AnimalSpeciesDefinitionBuildEntry definition,
            int definitionIndex,
            out AnimalSpeciesCatalogValidationError error)
        {
            if (definition.AllowedBiomes == AnimalBiomeMask.None
                || (definition.AllowedBiomes & ~AnimalBiomeMask.All) != 0)
            {
                error = CreateError(
                    AnimalSpeciesCatalogValidationErrorCode.AllowedBiomesAreInvalid,
                    definitionIndex);
                return false;
            }

            if (definition.AllowedGrounds == AnimalGroundMask.None
                || (definition.AllowedGrounds & ~AnimalGroundMask.All) != 0)
            {
                error = CreateError(
                    AnimalSpeciesCatalogValidationErrorCode.AllowedGroundsAreInvalid,
                    definitionIndex);
                return false;
            }

            if (definition.MinimumHerdSize == 0
                || definition.MinimumHerdSize > definition.MaximumHerdSize)
            {
                error = CreateError(
                    AnimalSpeciesCatalogValidationErrorCode.HerdSizeRangeIsInvalid,
                    definitionIndex);
                return false;
            }

            if (definition.TargetPopulationPer4096EligibleTiles == 0)
            {
                error = CreateError(
                    AnimalSpeciesCatalogValidationErrorCode.PopulationDensityIsInvalid,
                    definitionIndex);
                return false;
            }

            if (definition.MinimumIdleTicks > definition.MaximumIdleTicks)
            {
                error = CreateError(
                    AnimalSpeciesCatalogValidationErrorCode.IdleTickRangeIsInvalid,
                    definitionIndex);
                return false;
            }

            if (definition.MovementStepIntervalTicks == 0)
            {
                error = CreateError(
                    AnimalSpeciesCatalogValidationErrorCode.MovementStepIntervalIsInvalid,
                    definitionIndex);
                return false;
            }

            if (definition.MaximumWanderDistanceInTiles == 0)
            {
                error = CreateError(
                    AnimalSpeciesCatalogValidationErrorCode.WanderDistanceIsInvalid,
                    definitionIndex);
                return false;
            }

            if (definition.AppearanceVariantCount == 0)
            {
                error = CreateError(
                    AnimalSpeciesCatalogValidationErrorCode.AppearanceVariantCountIsInvalid,
                    definitionIndex);
                return false;
            }

            error = CreateSuccess();
            return true;
        }

        /// <summary>
        /// 共通カタログ内のAnimalSpeciesが、固有設定へ漏れなく一度ずつ現れることを確認します。
        /// </summary>
        /// <param name="definitionCatalog">全静的定義を保持する共通カタログです。</param>
        /// <param name="sortedDefinitions">実行時ID昇順へ並べた動物種設定です。</param>
        /// <param name="missingRuntimeId">失敗時に固有設定がなかった動物種IDを受け取ります。</param>
        /// <returns>共通カタログの全AnimalSpeciesへ固有設定がある場合はtrueです。</returns>
        private static bool TryFindMissingDefinition(
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog,
            DefinitionSortEntry[] sortedDefinitions,
            out DefinitionRuntimeId missingRuntimeId)
        {
            ref DefinitionCatalogBlob root = ref definitionCatalog.Value;
            int animalDefinitionIndex = 0;
            for (int definitionIndex = 0;
                 definitionIndex < root.Definitions.Length;
                 definitionIndex++)
            {
                DefinitionBlobRecord definition = root.Definitions[definitionIndex];
                if (definition.Kind != DefinitionKind.AnimalSpecies)
                {
                    continue;
                }

                if (animalDefinitionIndex >= sortedDefinitions.Length
                    || !sortedDefinitions[animalDefinitionIndex].RuntimeId.Equals(
                        definition.RuntimeId))
                {
                    missingRuntimeId = definition.RuntimeId;
                    return false;
                }

                animalDefinitionIndex++;
            }

            missingRuntimeId = default;
            return animalDefinitionIndex == sortedDefinitions.Length;
        }

        /// <summary>
        /// 検証済み入力を、SystemとJobが共有できる読み取り専用Blob Assetへコピーします。
        /// </summary>
        /// <param name="sortedDefinitions">実行時ID昇順の動物種設定です。</param>
        /// <returns>呼び出し側がBakerへ登録するか、テスト終了時にDisposeするBlob Assetです。</returns>
        private static BlobAssetReference<AnimalSpeciesCatalogBlob> CreateBlobAsset(
            DefinitionSortEntry[] sortedDefinitions)
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref AnimalSpeciesCatalogBlob root =
                    ref builder.ConstructRoot<AnimalSpeciesCatalogBlob>();
                BlobBuilderArray<AnimalSpeciesDefinitionBlob> species =
                    builder.Allocate(ref root.Species, sortedDefinitions.Length);
                for (int definitionIndex = 0;
                     definitionIndex < sortedDefinitions.Length;
                     definitionIndex++)
                {
                    DefinitionSortEntry source = sortedDefinitions[definitionIndex];
                    species[definitionIndex] = new AnimalSpeciesDefinitionBlob
                    {
                        SpeciesRuntimeId = source.RuntimeId,
                        AllowedBiomes = source.Definition.AllowedBiomes,
                        AllowedGrounds = source.Definition.AllowedGrounds,
                        MinimumHerdSize = source.Definition.MinimumHerdSize,
                        MaximumHerdSize = source.Definition.MaximumHerdSize,
                        TargetPopulationPer4096EligibleTiles =
                            source.Definition.TargetPopulationPer4096EligibleTiles,
                        MinimumIdleTicks = source.Definition.MinimumIdleTicks,
                        MaximumIdleTicks = source.Definition.MaximumIdleTicks,
                        MovementStepIntervalTicks =
                            source.Definition.MovementStepIntervalTicks,
                        MaximumWanderDistanceInTiles =
                            source.Definition.MaximumWanderDistanceInTiles,
                        AppearanceVariantCount = source.Definition.AppearanceVariantCount
                    };
                }

                return builder.CreateBlobAssetReference<AnimalSpeciesCatalogBlob>(
                    Allocator.Persistent);
            }
            finally
            {
                builder.Dispose();
            }
        }

        /// <summary>成功を表す位置情報なしの検証結果を作ります。</summary>
        /// <returns>CodeがNoneで、添字が-1の結果です。</returns>
        private static AnimalSpeciesCatalogValidationError CreateSuccess()
        {
            return CreateError(AnimalSpeciesCatalogValidationErrorCode.None);
        }

        /// <summary>
        /// 検証コードと任意の位置情報から統一されたエラー値を作ります。
        /// </summary>
        /// <param name="code">失敗理由または成功を表すNoneです。</param>
        /// <param name="definitionIndex">問題がある入力添字です。</param>
        /// <param name="relatedDefinitionIndex">重複相手の入力添字です。</param>
        /// <param name="missingRuntimeId">固有設定が欠けた共通動物種IDです。</param>
        /// <returns>Bakerとテストが共有できる検証結果です。</returns>
        private static AnimalSpeciesCatalogValidationError CreateError(
            AnimalSpeciesCatalogValidationErrorCode code,
            int definitionIndex = -1,
            int relatedDefinitionIndex = -1,
            DefinitionRuntimeId missingRuntimeId = default)
        {
            return new AnimalSpeciesCatalogValidationError
            {
                Code = code,
                DefinitionIndex = definitionIndex,
                RelatedDefinitionIndex = relatedDefinitionIndex,
                MissingRuntimeId = missingRuntimeId
            };
        }

        /// <summary>
        /// 構築中だけ使用する動物種設定と解決済みIDの組です。
        /// Array.Sortにより入力順を除去し、Blob Assetの並びを決定論的にします。
        /// </summary>
        private struct DefinitionSortEntry : IComparable<DefinitionSortEntry>
        {
            /// <summary>Inspectorから変換した動物種固有設定です。</summary>
            public AnimalSpeciesDefinitionBuildEntry Definition;

            /// <summary>共通DefinitionCatalogから解決した実行時IDです。</summary>
            public DefinitionRuntimeId RuntimeId;

            /// <summary>エラーでInspector位置を示すための元入力添字です。</summary>
            public int OriginalIndex;

            /// <summary>
            /// 実行時IDで昇順比較し、同じIDは元入力添字で安定化して、Blob Assetと重複診断を実行環境から独立させます。
            /// </summary>
            /// <param name="other">比較する別の動物種設定です。</param>
            /// <returns>このIDが前なら負、同じなら0、後なら正の値です。</returns>
            public readonly int CompareTo(DefinitionSortEntry other)
            {
                int runtimeIdComparison = RuntimeId.Value.CompareTo(other.RuntimeId.Value);
                if (runtimeIdComparison != 0)
                {
                    return runtimeIdComparison;
                }

                // 同じ実行時IDが重複した場合も、診断へ返す元入力添字の順序をArray.Sortの実装差へ依存させません。
                return OriginalIndex.CompareTo(other.OriginalIndex);
            }
        }
    }
}
