// このファイルは、Inspectorで編集する静的定義GUIDと参照をECS用の定義カタログへ変換します。
// MonoBehaviourは入力だけを保持し、Bakerが検証済みの連続IDとBlob AssetをSimulationへ渡します。

using System;
using System.Collections.Generic;
using Farmware.Simulation;
using Unity.Entities;
using UnityEngine;

namespace Farmware.Authoring
{
    /// <summary>
    /// アイテム、設備、施設、レシピ、動物種の静的定義を1つのBlob AssetへまとめるAuthoring用MonoBehaviourです。
    /// SceneまたはSubSceneへ1件だけ配置し、実行中のゲーム状態ではなく編集可能な定義表の入力として使用します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DefinitionCatalogAuthoring : MonoBehaviour
    {
        // 定義配列はBakerだけが読み、実行時Entityへ管理参照や文字列を持ち込みません。
        [Tooltip("ゲーム内の全静的定義です。GUIDは作成後に変更せず、同じ値を複数の定義へ設定しないでください。")]
        [SerializeField] private DefinitionEntry[] definitions =
            Array.Empty<DefinitionEntry>();

        /// <summary>
        /// 1件の静的定義をInspectorへ表示するAuthoring専用データです。
        /// 表示名はエラー確認用だけに使い、実行時Blob Assetには文字列を保存しません。
        /// </summary>
        [Serializable]
        private sealed class DefinitionEntry
        {
            // 開発者がInspectorで定義を識別する名前で、ゲーム表示名やセーブキーには使用しません。
            [Tooltip("Inspectorとエラー表示で定義を見分けるための名前です。ゲーム表示名やセーブIDには使用しません。")]
            [SerializeField] private string displayName = string.Empty;

            // 数値はセーブ互換境界になるため、既存分類の値を後から変更しません。
            [Tooltip("定義の分類です。アイテム、設備、施設、レシピ、動物種から選び、作成後は意味を変更しません。")]
            [SerializeField] private DefinitionKind kind = DefinitionKind.None;

            // GUIDは定義の永続キーなので、名前変更や配列並べ替えでも同じ文字列を維持します。
            [Tooltip("定義を永続的に識別する128bit GUIDです。32桁またはハイフン付きで入力し、定義作成後は変更しないでください。")]
            [SerializeField] private string guid = string.Empty;

            // 参照先はBakerが存在と分類を検証し、実行時には連続IDへ置き換えます。
            [Tooltip("この定義が参照する別定義です。BakerがGUIDの存在と期待分類を検証し、実行時IDへ解決します。")]
            [SerializeField] private DefinitionReference[] references =
                Array.Empty<DefinitionReference>();

            /// <summary>エラー文へ含めるInspector用の表示名です。</summary>
            public string DisplayName => displayName;

            /// <summary>Blob Assetへ保存する定義分類です。</summary>
            public DefinitionKind Kind => kind;

            /// <summary>DefinitionGuidへ変換するInspector文字列です。</summary>
            public string Guid => guid;

            /// <summary>この定義から参照する別定義のAuthoring入力です。</summary>
            public DefinitionReference[] References => references;
        }

        /// <summary>
        /// GUIDで指定した参照先と、接続を許可する定義分類を保持します。
        /// 例としてレシピからアイテムを参照するときはExpectedKindをItemへ設定します。
        /// </summary>
        [Serializable]
        private sealed class DefinitionReference
        {
            // 文字列はBake時だけ読み、解決後のBlob Assetには整数IDだけを保存します。
            [Tooltip("参照先定義の128bit GUIDです。対象を削除またはGUID変更した場合はBakeエラーになります。")]
            [SerializeField] private string targetGuid = string.Empty;

            // 同じGUIDが存在しても分類を取り違えた参照を実行前に拒否します。
            [Tooltip("参照先に期待する分類です。実際の分類と異なる場合はBakeエラーになります。")]
            [SerializeField] private DefinitionKind expectedKind = DefinitionKind.None;

            /// <summary>DefinitionGuidへ変換する参照先のInspector文字列です。</summary>
            public string TargetGuid => targetGuid;

            /// <summary>参照先へ要求する定義分類です。</summary>
            public DefinitionKind ExpectedKind => expectedKind;
        }

        /// <summary>
        /// DefinitionCatalogAuthoringを、検証済みBlob Assetを保持するECS Singleton候補へ変換します。
        /// GUID未設定、重複、参照切れ、分類不一致があれば例外を発生させ、破損カタログを生成しません。
        /// </summary>
        private sealed class DefinitionCatalogBaker : Baker<DefinitionCatalogAuthoring>
        {
            /// <summary>
            /// Inspector配列を構築入力へ平坦化し、共通Builderの検証後にBlob Assetを登録します。
            /// </summary>
            /// <param name="authoring">SceneまたはSubSceneに配置された定義カタログ入力です。</param>
            public override void Bake(DefinitionCatalogAuthoring authoring)
            {
                DefinitionEntry[] sourceDefinitions =
                    authoring.definitions ?? Array.Empty<DefinitionEntry>();
                List<DefinitionBuildReference> buildReferences =
                    new List<DefinitionBuildReference>();
                DefinitionBuildEntry[] buildDefinitions =
                    new DefinitionBuildEntry[sourceDefinitions.Length];

                for (int definitionIndex = 0;
                     definitionIndex < sourceDefinitions.Length;
                     definitionIndex++)
                {
                    DefinitionEntry sourceDefinition = sourceDefinitions[definitionIndex];
                    DefinitionReference[] sourceReferences =
                        sourceDefinition?.References ?? Array.Empty<DefinitionReference>();
                    int referenceStartIndex = buildReferences.Count;

                    // 文字列GUIDはBake時だけ解析し、失敗時はInvalidを渡して共通Builderから位置付きエラーを得ます。
                    DefinitionGuid.TryParse(
                        sourceDefinition?.Guid,
                        out DefinitionGuid definitionGuid);
                    for (int referenceIndex = 0;
                         referenceIndex < sourceReferences.Length;
                         referenceIndex++)
                    {
                        DefinitionReference sourceReference =
                            sourceReferences[referenceIndex];
                        DefinitionGuid.TryParse(
                            sourceReference?.TargetGuid,
                            out DefinitionGuid targetGuid);
                        buildReferences.Add(new DefinitionBuildReference
                        {
                            TargetGuid = targetGuid,
                            ExpectedKind = sourceReference?.ExpectedKind ??
                                DefinitionKind.None
                        });
                    }

                    buildDefinitions[definitionIndex] = new DefinitionBuildEntry
                    {
                        Guid = definitionGuid,
                        Kind = sourceDefinition?.Kind ?? DefinitionKind.None,
                        ReferenceStartIndex = referenceStartIndex,
                        ReferenceCount = sourceReferences.Length
                    };
                }

                if (!DefinitionCatalogBuilder.TryBuild(
                    buildDefinitions,
                    buildReferences.ToArray(),
                    out BlobAssetReference<DefinitionCatalogBlob> catalog,
                    out DefinitionCatalogValidationError error))
                {
                    throw new InvalidOperationException(
                        CreateValidationMessage(sourceDefinitions, error));
                }

                // Bakerへ登録して差分Bake時の参照数を正しく管理し、手動Disposeによる二重解放を避けます。
                AddBlobAsset(ref catalog, out _);
                Entity entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new DefinitionCatalog
                {
                    Value = catalog
                });
            }

            /// <summary>
            /// 共通Builderの位置付きエラーを、初心者がInspector上の修正場所を探せる日本語へ変換します。
            /// </summary>
            /// <param name="sourceDefinitions">表示名を取得する元Authoring配列です。</param>
            /// <param name="error">失敗理由と元入力添字です。</param>
            /// <returns>Definition番号、表示名、参照番号を含むBakeエラーです。</returns>
            private static string CreateValidationMessage(
                DefinitionEntry[] sourceDefinitions,
                DefinitionCatalogValidationError error)
            {
                string definitionLabel = GetDefinitionLabel(
                    sourceDefinitions,
                    error.DefinitionIndex);
                string relatedDefinitionLabel = GetDefinitionLabel(
                    sourceDefinitions,
                    error.RelatedDefinitionIndex);

                // 各検証コードを明示的に分け、Inspectorで直す場所と理由が一つのメッセージから分かるようにします。
                switch (error.Code)
                {
                    case DefinitionCatalogValidationErrorCode.DefinitionGuidIsInvalid:
                        return $"定義GUIDが未設定または不正です: {definitionLabel}";
                    case DefinitionCatalogValidationErrorCode.DefinitionKindIsInvalid:
                        return $"定義分類が未設定または未対応です: {definitionLabel}";
                    case DefinitionCatalogValidationErrorCode.ReferenceRangeIsInvalid:
                        return $"定義参照の内部範囲が不正です: {definitionLabel}";
                    case DefinitionCatalogValidationErrorCode.DefinitionGuidIsDuplicated:
                        return $"定義GUIDが重複しています: {definitionLabel} / {relatedDefinitionLabel}";
                    case DefinitionCatalogValidationErrorCode.ReferenceGuidIsInvalid:
                        return $"参照先GUIDが未設定または不正です: {definitionLabel}, 参照 {error.ReferenceIndex}";
                    case DefinitionCatalogValidationErrorCode.ReferencedDefinitionWasNotFound:
                        return $"参照先GUIDに対応する定義がありません: {definitionLabel}, 参照 {error.ReferenceIndex}";
                    case DefinitionCatalogValidationErrorCode.ReferencedDefinitionKindMismatch:
                        return $"参照先の分類が一致しません: {definitionLabel}, 参照 {error.ReferenceIndex}, 参照先 {relatedDefinitionLabel}";
                    default:
                        return $"定義カタログを構築できませんでした: {error.Code}";
                }
            }

            /// <summary>
            /// 定義添字と任意の表示名から、Bakeエラーへ含める識別文字列を作ります。
            /// </summary>
            /// <param name="sourceDefinitions">Inspector上の定義配列です。</param>
            /// <param name="definitionIndex">表示する0始まりの定義添字です。</param>
            /// <returns>範囲内なら番号と表示名、範囲外なら「対象なし」です。</returns>
            private static string GetDefinitionLabel(
                DefinitionEntry[] sourceDefinitions,
                int definitionIndex)
            {
                if (definitionIndex < 0 ||
                    definitionIndex >= sourceDefinitions.Length)
                {
                    return "対象なし";
                }

                string displayName = sourceDefinitions[definitionIndex]?.DisplayName;
                return string.IsNullOrWhiteSpace(displayName)
                    ? $"Definition[{definitionIndex}]"
                    : $"Definition[{definitionIndex}] ({displayName})";
            }
        }
    }
}
