// このファイルは、Authoring入力を検証して定義GUIDを連続実行時IDへ変換し、Blob Assetを構築します。
// BakerとEditModeテストが同じ検証処理を使用し、重複や参照切れを実行開始前に拒否します。

using System;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.Simulation
{
    /// <summary>
    /// 1件の静的定義をBlob Assetへ変換するための一時入力です。
    /// Authoring側がInspector値から作成し、構築完了後は保持しません。
    /// </summary>
    public struct DefinitionBuildEntry
    {
        /// <summary>定義作成後に変更しない128bit GUIDです。</summary>
        public DefinitionGuid Guid;

        /// <summary>アイテム、設備、施設、レシピ、動物種のいずれかです。</summary>
        public DefinitionKind Kind;

        /// <summary>DefinitionBuildReference配列内で、この定義の参照が始まる添字です。</summary>
        public int ReferenceStartIndex;

        /// <summary>この定義が持つ参照件数です。</summary>
        public int ReferenceCount;
    }

    /// <summary>
    /// Authoring時にGUIDで指定された定義間参照の一時入力です。
    /// 構築時に存在と分類を検証し、Blob Assetには解決済み実行時IDだけを保存します。
    /// </summary>
    public struct DefinitionBuildReference
    {
        /// <summary>参照先として指定された変更禁止の定義GUIDです。</summary>
        public DefinitionGuid TargetGuid;

        /// <summary>参照先に要求する分類です。誤った種類のアセット接続をBake前に検出します。</summary>
        public DefinitionKind ExpectedKind;
    }

    /// <summary>
    /// 定義カタログを構築できなかった理由です。
    /// Bakerはこの値を日本語のエラーへ変換し、EditModeテストは失敗原因を直接検証します。
    /// </summary>
    public enum DefinitionCatalogValidationErrorCode : byte
    {
        /// <summary>検証とBlob Asset構築が成功しました。</summary>
        None = 0,

        /// <summary>定義入力配列がnullです。</summary>
        DefinitionsAreNull = 1,

        /// <summary>参照入力配列がnullです。</summary>
        ReferencesAreNull = 2,

        /// <summary>定義GUIDが未設定です。</summary>
        DefinitionGuidIsInvalid = 3,

        /// <summary>定義分類が未設定または未対応値です。</summary>
        DefinitionKindIsInvalid = 4,

        /// <summary>定義の参照範囲が入力配列の並びまたは長さと一致しません。</summary>
        ReferenceRangeIsInvalid = 5,

        /// <summary>同じ128bit GUIDが複数の定義へ設定されています。</summary>
        DefinitionGuidIsDuplicated = 6,

        /// <summary>参照先GUIDが未設定です。</summary>
        ReferenceGuidIsInvalid = 7,

        /// <summary>参照先GUIDに対応する定義がカタログ内にありません。</summary>
        ReferencedDefinitionWasNotFound = 8,

        /// <summary>参照先の実際の分類が、参照元の要求する分類と一致しません。</summary>
        ReferencedDefinitionKindMismatch = 9
    }

    /// <summary>
    /// 最初に見つかった定義カタログの検証エラー位置を保持します。
    /// Inspector上のどの定義と参照を直すべきかをBakerのエラーへ含めるために使用します。
    /// </summary>
    public struct DefinitionCatalogValidationError
    {
        /// <summary>失敗理由です。成功時はNoneです。</summary>
        public DefinitionCatalogValidationErrorCode Code;

        /// <summary>問題があるDefinitionBuildEntryの元入力添字です。対象がなければ-1です。</summary>
        public int DefinitionIndex;

        /// <summary>問題がある定義のReferences内での0始まりの添字です。対象がなければ-1です。</summary>
        public int ReferenceIndex;

        /// <summary>重複相手や参照先など、関連する定義の元入力添字です。対象がなければ-1です。</summary>
        public int RelatedDefinitionIndex;
    }

    /// <summary>
    /// 定義入力を一括検証し、GUID順の安定した連続IDと解決済み参照を持つBlob Assetを構築します。
    /// この処理はBakerまたはテストから低頻度に呼び、ゲームの固定Tick中には呼びません。
    /// </summary>
    public static class DefinitionCatalogBuilder
    {
        /// <summary>
        /// 入力を検証してGUID順に並べ、定義カタログのBlob Assetを作成します。
        /// 失敗時はBlob Assetを作らず、最初のエラー位置を返します。
        /// </summary>
        /// <param name="definitions">Inspector上の定義順で並ぶ構築入力です。</param>
        /// <param name="references">各定義の参照をdefinitionsと同じ順に連結した配列です。</param>
        /// <param name="catalog">成功時に呼び出し側が登録またはDisposeするBlob Assetです。</param>
        /// <param name="error">失敗時の理由と元入力添字です。</param>
        /// <returns>全入力が有効でBlob Assetを構築できた場合はtrueです。</returns>
        public static bool TryBuild(
            DefinitionBuildEntry[] definitions,
            DefinitionBuildReference[] references,
            out BlobAssetReference<DefinitionCatalogBlob> catalog,
            out DefinitionCatalogValidationError error)
        {
            catalog = default;
            error = CreateSuccess();
            if (definitions == null)
            {
                error = CreateError(DefinitionCatalogValidationErrorCode.DefinitionsAreNull);
                return false;
            }

            if (references == null)
            {
                error = CreateError(DefinitionCatalogValidationErrorCode.ReferencesAreNull);
                return false;
            }

            DefinitionSortEntry[] sortedDefinitions =
                new DefinitionSortEntry[definitions.Length];
            int expectedReferenceStart = 0;
            for (int definitionIndex = 0;
                 definitionIndex < definitions.Length;
                 definitionIndex++)
            {
                DefinitionBuildEntry definition = definitions[definitionIndex];
                if (!definition.Guid.IsValid)
                {
                    error = CreateError(
                        DefinitionCatalogValidationErrorCode.DefinitionGuidIsInvalid,
                        definitionIndex);
                    return false;
                }

                if (!IsSupportedKind(definition.Kind))
                {
                    error = CreateError(
                        DefinitionCatalogValidationErrorCode.DefinitionKindIsInvalid,
                        definitionIndex);
                    return false;
                }

                // 参照は定義順に隙間なく連結させ、重複範囲や未所有の参照をBlobへ紛れ込ませません。
                if (definition.ReferenceStartIndex != expectedReferenceStart ||
                    definition.ReferenceCount < 0 ||
                    definition.ReferenceCount > references.Length - expectedReferenceStart)
                {
                    error = CreateError(
                        DefinitionCatalogValidationErrorCode.ReferenceRangeIsInvalid,
                        definitionIndex);
                    return false;
                }

                expectedReferenceStart += definition.ReferenceCount;
                sortedDefinitions[definitionIndex] = new DefinitionSortEntry
                {
                    Definition = definition,
                    OriginalIndex = definitionIndex
                };
            }

            if (expectedReferenceStart != references.Length)
            {
                error = CreateError(
                    DefinitionCatalogValidationErrorCode.ReferenceRangeIsInvalid,
                    definitions.Length - 1);
                return false;
            }

            Array.Sort(sortedDefinitions);
            for (int sortedIndex = 1;
                 sortedIndex < sortedDefinitions.Length;
                 sortedIndex++)
            {
                if (sortedDefinitions[sortedIndex - 1]
                    .Definition
                    .Guid
                    .Equals(sortedDefinitions[sortedIndex].Definition.Guid))
                {
                    error = CreateError(
                        DefinitionCatalogValidationErrorCode.DefinitionGuidIsDuplicated,
                        sortedDefinitions[sortedIndex].OriginalIndex,
                        relatedDefinitionIndex:
                            sortedDefinitions[sortedIndex - 1].OriginalIndex);
                    return false;
                }
            }

            DefinitionRuntimeId[] resolvedReferenceIds =
                new DefinitionRuntimeId[references.Length];
            for (int sortedIndex = 0;
                 sortedIndex < sortedDefinitions.Length;
                 sortedIndex++)
            {
                DefinitionSortEntry source = sortedDefinitions[sortedIndex];
                int referenceEndIndex = source.Definition.ReferenceStartIndex +
                    source.Definition.ReferenceCount;
                for (int referenceIndex = source.Definition.ReferenceStartIndex;
                     referenceIndex < referenceEndIndex;
                     referenceIndex++)
                {
                    DefinitionBuildReference reference = references[referenceIndex];
                    // Inspectorでは各定義の参照配列が0から始まるため、平坦配列の添字を定義内の番号へ戻します。
                    int localReferenceIndex =
                        referenceIndex - source.Definition.ReferenceStartIndex;
                    if (!reference.TargetGuid.IsValid)
                    {
                        error = CreateError(
                            DefinitionCatalogValidationErrorCode.ReferenceGuidIsInvalid,
                            source.OriginalIndex,
                            localReferenceIndex);
                        return false;
                    }

                    int targetSortedIndex = FindDefinitionIndex(
                        sortedDefinitions,
                        reference.TargetGuid);
                    if (targetSortedIndex < 0)
                    {
                        error = CreateError(
                            DefinitionCatalogValidationErrorCode.ReferencedDefinitionWasNotFound,
                            source.OriginalIndex,
                            localReferenceIndex);
                        return false;
                    }

                    DefinitionSortEntry target = sortedDefinitions[targetSortedIndex];
                    if (reference.ExpectedKind != target.Definition.Kind)
                    {
                        error = CreateError(
                            DefinitionCatalogValidationErrorCode.ReferencedDefinitionKindMismatch,
                            source.OriginalIndex,
                            localReferenceIndex,
                            target.OriginalIndex);
                        return false;
                    }

                    resolvedReferenceIds[referenceIndex] =
                        new DefinitionRuntimeId(targetSortedIndex + 1);
                }
            }

            catalog = CreateBlobAsset(
                sortedDefinitions,
                resolvedReferenceIds);
            return true;
        }

        /// <summary>
        /// GUID順へ並べた定義と検証済み参照を、読み取り専用Blob Assetへコピーします。
        /// </summary>
        /// <param name="sortedDefinitions">GUID昇順の定義と元入力位置です。</param>
        /// <param name="resolvedReferenceIds">元参照添字に対応する解決済み実行時IDです。</param>
        /// <returns>呼び出し側がBakerへ登録するか、テスト終了時にDisposeするBlob Assetです。</returns>
        private static BlobAssetReference<DefinitionCatalogBlob> CreateBlobAsset(
            DefinitionSortEntry[] sortedDefinitions,
            DefinitionRuntimeId[] resolvedReferenceIds)
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref DefinitionCatalogBlob root =
                    ref builder.ConstructRoot<DefinitionCatalogBlob>();
                BlobBuilderArray<DefinitionBlobRecord> blobDefinitions =
                    builder.Allocate(
                        ref root.Definitions,
                        sortedDefinitions.Length);
                BlobBuilderArray<DefinitionBlobReference> blobReferences =
                    builder.Allocate(
                        ref root.References,
                        resolvedReferenceIds.Length);

                int destinationReferenceIndex = 0;
                for (int sortedIndex = 0;
                     sortedIndex < sortedDefinitions.Length;
                     sortedIndex++)
                {
                    DefinitionBuildEntry source =
                        sortedDefinitions[sortedIndex].Definition;
                    blobDefinitions[sortedIndex] = new DefinitionBlobRecord
                    {
                        Guid = source.Guid,
                        RuntimeId = new DefinitionRuntimeId(sortedIndex + 1),
                        Kind = source.Kind,
                        ReferenceStartIndex = destinationReferenceIndex,
                        ReferenceCount = source.ReferenceCount
                    };

                    // 定義順だけをGUIDで正規化し、各定義内の参照順はAuthoringの意味を保つため変更しません。
                    int sourceReferenceEndIndex = source.ReferenceStartIndex +
                        source.ReferenceCount;
                    for (int sourceReferenceIndex = source.ReferenceStartIndex;
                         sourceReferenceIndex < sourceReferenceEndIndex;
                         sourceReferenceIndex++)
                    {
                        blobReferences[destinationReferenceIndex] =
                            new DefinitionBlobReference
                            {
                                TargetRuntimeId =
                                    resolvedReferenceIds[sourceReferenceIndex]
                            };
                        destinationReferenceIndex++;
                    }
                }

                return builder.CreateBlobAssetReference<DefinitionCatalogBlob>(
                    Allocator.Persistent);
            }
            finally
            {
                // BlobBuilderの一時領域はここで解放し、返却するPersistent Blobだけを呼び出し側へ所有させます。
                builder.Dispose();
            }
        }

        /// <summary>
        /// GUID昇順の定義配列を二分探索し、参照先定義の添字を返します。
        /// </summary>
        /// <param name="sortedDefinitions">GUID昇順へ並べた定義です。</param>
        /// <param name="guid">検索する参照先GUIDです。</param>
        /// <returns>見つかった0始まりの添字、未登録なら-1です。</returns>
        private static int FindDefinitionIndex(
            DefinitionSortEntry[] sortedDefinitions,
            DefinitionGuid guid)
        {
            int lowerIndex = 0;
            int upperIndex = sortedDefinitions.Length - 1;
            while (lowerIndex <= upperIndex)
            {
                int middleIndex = lowerIndex + ((upperIndex - lowerIndex) / 2);
                int comparison = sortedDefinitions[middleIndex]
                    .Definition
                    .Guid
                    .CompareTo(guid);
                if (comparison == 0)
                {
                    return middleIndex;
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

            return -1;
        }

        /// <summary>
        /// DefinitionKindが現在サポートする5分類のいずれかかを確認します。
        /// </summary>
        /// <param name="kind">Inspectorまたはテストから渡された分類値です。</param>
        /// <returns>有効な静的定義分類ならtrueです。</returns>
        private static bool IsSupportedKind(DefinitionKind kind)
        {
            return kind >= DefinitionKind.Item &&
                kind <= DefinitionKind.AnimalSpecies;
        }

        /// <summary>
        /// エラーのない初期結果を作成します。
        /// </summary>
        /// <returns>CodeがNoneで各添字が-1の結果です。</returns>
        private static DefinitionCatalogValidationError CreateSuccess()
        {
            return CreateError(DefinitionCatalogValidationErrorCode.None);
        }

        /// <summary>
        /// 検証コードと元入力位置からBakerとテストで共有する結果を作成します。
        /// </summary>
        /// <param name="code">検証結果または失敗理由です。</param>
        /// <param name="definitionIndex">問題がある定義の元入力添字です。</param>
        /// <param name="referenceIndex">問題がある定義内での0始まりの参照添字です。</param>
        /// <param name="relatedDefinitionIndex">重複相手または参照先の元入力添字です。</param>
        /// <returns>指定した位置を保持する検証結果です。</returns>
        private static DefinitionCatalogValidationError CreateError(
            DefinitionCatalogValidationErrorCode code,
            int definitionIndex = -1,
            int referenceIndex = -1,
            int relatedDefinitionIndex = -1)
        {
            return new DefinitionCatalogValidationError
            {
                Code = code,
                DefinitionIndex = definitionIndex,
                ReferenceIndex = referenceIndex,
                RelatedDefinitionIndex = relatedDefinitionIndex
            };
        }

        /// <summary>
        /// GUIDで安定整列する定義と、エラー表示用の元入力位置を組にした一時データです。
        /// </summary>
        private struct DefinitionSortEntry : IComparable<DefinitionSortEntry>
        {
            /// <summary>GUID、分類、参照範囲を持つ元入力です。</summary>
            public DefinitionBuildEntry Definition;

            /// <summary>Inspector上の定義配列へ戻すための0始まりの添字です。</summary>
            public int OriginalIndex;

            /// <summary>
            /// Definition.Guidを比較し、入力順に依存しない実行時ID順を作ります。
            /// </summary>
            /// <param name="other">比較するもう一方の定義です。</param>
            /// <returns>GUIDが前なら負、同じなら0、後なら正の値です。</returns>
            public int CompareTo(DefinitionSortEntry other)
            {
                return Definition.Guid.CompareTo(other.Definition.Guid);
            }
        }
    }
}
