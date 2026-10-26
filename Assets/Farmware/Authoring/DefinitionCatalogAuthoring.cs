// このファイルは、Inspectorの地形以外の静的定義・動物種固有値と、組み込み地形・材質パレットをECS用カタログへ変換します。
// MonoBehaviourは編集可能な入力だけを保持し、Bakerが検証済みの連続IDと用途別Blob AssetをSimulationへ渡します。

using System;
using System.Collections.Generic;
using Farmware.Simulation;
using Farmware.Simulation.Animals;
using Farmware.WorldGeneration;
using Unity.Entities;
using UnityEngine;

namespace Farmware.Authoring
{
    /// <summary>
    /// アイテム、設備、施設、レシピ、動物種のInspector定義と、組み込み地形・固定材質パレットを用途別Blob AssetへまとめるAuthoring用MonoBehaviourです。
    /// SceneまたはSubSceneへ1件だけ配置し、実行中のゲーム状態ではなく編集可能な定義表の入力として使用します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DefinitionCatalogAuthoring : MonoBehaviour
    {
        // 定義配列はBakerだけが読み、実行時Entityへ管理参照や文字列を持ち込みません。
        [Tooltip("ゲーム内の全静的定義です。GUIDは作成後に変更せず、同じ値を複数の定義へ設定しないでください。")]
        [SerializeField] private DefinitionEntry[] definitions =
            Array.Empty<DefinitionEntry>();

        // 動物固有値は共通定義GUIDへ接続し、種類名や配列添字を実行時の識別子にしません。
        [Tooltip("動物種ごとの生成・移動設定です。DefinitionsでAnimal Speciesに分類した全GUIDを、重複なく1回ずつ設定してください。")]
        [SerializeField] private AnimalSpeciesEntry[] animalSpeciesDefinitions =
            Array.Empty<AnimalSpeciesEntry>();

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
            [Tooltip("定義の分類です。現在この配列へ追加できるのはアイテム、設備、施設、レシピ、動物種です。地形4分類は組み込み定義だけを使用し、手動追加するとBakeエラーになります。作成後は意味を変更しません。")]
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
        /// 1種類の動物について、生成条件、群れ、固定Tick移動、外見差分をInspectorへ表示する入力です。
        /// Bakerだけが読み、文字列GUIDや編集用オブジェクトを実行時Entityへ持ち込みません。
        /// </summary>
        [Serializable]
        private sealed class AnimalSpeciesEntry
        {
            // 共通定義側と同じGUIDを指定し、表示名の変更や配列並べ替えで接続が変わらないようにします。
            [Tooltip("DefinitionsでAnimal Speciesに分類した定義GUIDです。定義作成後は変更しないでください。")]
            [SerializeField] private string definitionGuid = string.Empty;

            // Unityはulongを基底型に持つenumを直接Serializeできないため、Inspector入力だけ符号付き32bit整数で保持します。
            // 使用するのは下位18bitだけで、Bakerが未定義bitと0を拒否してから実行時のAnimalBiomeMaskへ変換します。
            [Tooltip("この動物が自然出現できる18バイオームのbit集合です。下位18bitを使用し、少なくとも1種類を設定してください。")]
            [SerializeField] private int allowedBiomeBits =
                (int)AnimalBiomeMask.TemperateGrassland;

            // 地形分類はバイオームとは別に確認し、水域や岩壁への誤配置を防ぎます。
            [Tooltip("この動物が出現・滞在できる基礎地形です。陸生動物は通常Landだけを選択します。")]
            [SerializeField] private AnimalGroundMask allowedGrounds =
                AnimalGroundMask.Land;

            // 群れ生成時の下限で、0は動物を生成できない曖昧な設定になるため許可しません。
            [Tooltip("1つの群れを作る最小個体数です。単位は個体で、1以上かつ最大個体数以下にしてください。")]
            [SerializeField] private byte minimumHerdSize = 1;

            // 群れ生成時の上限で、ANIMAL-01Bが保存済み乱数状態から範囲内の数を選びます。
            [Tooltip("1つの群れを作る最大個体数です。単位は個体で、最小個体数以上にしてください。")]
            [SerializeField] private byte maximumHerdSize = 1;

            // 地域寸法へ依存しない密度単位にして、後続で地域分割を調整しても意味を保ちます。
            [Tooltip("有効な地形4096タイル当たりの目標個体数です。値を増やすと新規地域の初期個体数が増えます。")]
            [SerializeField] private ushort targetPopulationPer4096EligibleTiles = 1;

            // 待機値は描画秒ではなく共通固定Tickで保存し、フレームレートによる結果差を防ぎます。
            [Tooltip("移動後に次の行動候補を選ぶまでの最小待機時間です。単位は固定Tickです。")]
            [SerializeField] private uint minimumIdleTicks;

            // 上限が下限未満なら範囲抽選できないため、Bakerが設定ミスとして拒否します。
            [Tooltip("移動後に次の行動候補を選ぶまでの最大待機時間です。単位は固定Tickで、最小値以上にしてください。")]
            [SerializeField] private uint maximumIdleTicks;

            // 1歩の所要Tickを整数化し、Time.deltaTimeの累積誤差を動物位置へ持ち込みません。
            [Tooltip("隣接タイルへ1歩進むのに必要な時間です。単位は固定Tickで、1以上にしてください。")]
            [SerializeField] private uint movementStepIntervalTicks = 1;

            // 群れから無制限に離れないための論理距離で、表示範囲やTransform距離には依存しません。
            [Tooltip("群れの基準位置から通常の徘徊で離れてよい最大距離です。単位はタイルで、1以上にしてください。")]
            [SerializeField] private ushort maximumWanderDistanceInTiles = 1;

            // 外見番号は0始まりで個体状態へ保存するため、少なくとも1種類を必須にします。
            [Tooltip("生成時に選べる外見差分の数です。0始まりの番号を個体へ保存するため、順序と既存数を不用意に変更しないでください。")]
            [SerializeField] private byte appearanceVariantCount = 1;

            /// <summary>共通DefinitionCatalogへ接続するGUID文字列です。</summary>
            public string DefinitionGuid => definitionGuid;

            /// <summary>Inspectorの下位18bitを、自然出現を許可する実行時バイオームMaskとして返します。</summary>
            public AnimalBiomeMask AllowedBiomes =>
                (AnimalBiomeMask)(uint)allowedBiomeBits;

            /// <summary>出現・滞在を許可する基礎地形Maskです。</summary>
            public AnimalGroundMask AllowedGrounds => allowedGrounds;

            /// <summary>1群れの最小個体数です。</summary>
            public byte MinimumHerdSize => minimumHerdSize;

            /// <summary>1群れの最大個体数です。</summary>
            public byte MaximumHerdSize => maximumHerdSize;

            /// <summary>有効な地形4096タイル当たりの目標個体数です。</summary>
            public ushort TargetPopulationPer4096EligibleTiles =>
                targetPopulationPer4096EligibleTiles;

            /// <summary>次の行動まで待つ最小固定Tick数です。</summary>
            public uint MinimumIdleTicks => minimumIdleTicks;

            /// <summary>次の行動まで待つ最大固定Tick数です。</summary>
            public uint MaximumIdleTicks => maximumIdleTicks;

            /// <summary>隣接タイルへ1歩進む固定Tick数です。</summary>
            public uint MovementStepIntervalTicks => movementStepIntervalTicks;

            /// <summary>群れの基準位置から離れてよい最大タイル数です。</summary>
            public ushort MaximumWanderDistanceInTiles =>
                maximumWanderDistanceInTiles;

            /// <summary>生成時に選べる0始まりの外見差分数です。</summary>
            public byte AppearanceVariantCount => appearanceVariantCount;
        }

        /// <summary>
        /// DefinitionCatalogAuthoringを、共通定義と動物種定義の検証済みBlob Assetを保持するECS Singleton候補へ変換します。
        /// GUID未設定、重複、参照切れ、分類不一致、動物種固有値の不整合があれば例外を発生させ、破損カタログを生成しません。
        /// </summary>
        private sealed class DefinitionCatalogBaker : Baker<DefinitionCatalogAuthoring>
        {
            /// <summary>
            /// Inspector配列を構築入力へ平坦化し、共通定義と動物種の各Builderによる検証後にBlob Assetを登録します。
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
                    RejectUnsupportedAuthoredTerrainDefinition(
                        sourceDefinition?.Kind ?? DefinitionKind.None,
                        sourceDefinition?.DisplayName,
                        definitionIndex);
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

                // 自然生成で共有する5地形と41材質は、Sceneへ手作業のGUID追加を要求せず全Worldで同じ定義を登録します。
                DefinitionBuildEntry[] builtInTerrainDefinitions =
                    BuiltInTerrainBlockDefinitions.CreateCommonDefinitions(
                        buildReferences.Count,
                        out DefinitionBuildReference[] builtInTerrainReferences);
                buildReferences.AddRange(builtInTerrainReferences);
                int authoredDefinitionCount = buildDefinitions.Length;
                Array.Resize(
                    ref buildDefinitions,
                    authoredDefinitionCount + builtInTerrainDefinitions.Length);
                Array.Copy(
                    builtInTerrainDefinitions,
                    0,
                    buildDefinitions,
                    authoredDefinitionCount,
                    builtInTerrainDefinitions.Length);

                if (!DefinitionCatalogBuilder.TryBuild(
                    buildDefinitions,
                    buildReferences.ToArray(),
                    out BlobAssetReference<DefinitionCatalogBlob> catalog,
                    out DefinitionCatalogValidationError error))
                {
                    throw new InvalidOperationException(
                        CreateValidationMessage(sourceDefinitions, error));
                }

                AnimalSpeciesEntry[] sourceAnimalSpecies =
                    authoring.animalSpeciesDefinitions ?? Array.Empty<AnimalSpeciesEntry>();
                AnimalSpeciesDefinitionBuildEntry[] buildAnimalSpecies =
                    new AnimalSpeciesDefinitionBuildEntry[sourceAnimalSpecies.Length];
                for (int animalIndex = 0;
                     animalIndex < sourceAnimalSpecies.Length;
                     animalIndex++)
                {
                    AnimalSpeciesEntry sourceAnimal = sourceAnimalSpecies[animalIndex];
                    DefinitionGuid.TryParse(
                        sourceAnimal?.DefinitionGuid,
                        out DefinitionGuid animalDefinitionGuid);
                    buildAnimalSpecies[animalIndex] =
                        new AnimalSpeciesDefinitionBuildEntry
                        {
                            DefinitionGuid = animalDefinitionGuid,
                            AllowedBiomes = sourceAnimal?.AllowedBiomes ??
                                AnimalBiomeMask.None,
                            AllowedGrounds = sourceAnimal?.AllowedGrounds ??
                                AnimalGroundMask.None,
                            MinimumHerdSize = sourceAnimal?.MinimumHerdSize ?? 0,
                            MaximumHerdSize = sourceAnimal?.MaximumHerdSize ?? 0,
                            TargetPopulationPer4096EligibleTiles =
                                sourceAnimal?.TargetPopulationPer4096EligibleTiles ?? 0,
                            MinimumIdleTicks = sourceAnimal?.MinimumIdleTicks ?? 0,
                            MaximumIdleTicks = sourceAnimal?.MaximumIdleTicks ?? 0,
                            MovementStepIntervalTicks =
                                sourceAnimal?.MovementStepIntervalTicks ?? 0,
                            MaximumWanderDistanceInTiles =
                                sourceAnimal?.MaximumWanderDistanceInTiles ?? 0,
                            AppearanceVariantCount =
                                sourceAnimal?.AppearanceVariantCount ?? 0
                        };
                }

                if (!AnimalSpeciesCatalogBuilder.TryBuild(
                    catalog,
                    buildAnimalSpecies,
                    out BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog,
                    out AnimalSpeciesCatalogValidationError animalError))
                {
                    // 共通BlobはまだBakerへ登録していないため、この失敗経路では呼び出し側が明示的に破棄します。
                    catalog.Dispose();
                    throw new InvalidOperationException(
                        CreateAnimalValidationMessage(sourceAnimalSpecies, animalError));
                }

                BlobAssetReference<TerrainBlockCatalogBlob> terrainCatalog = default;
                BlobAssetReference<TerrainBlockMaterialCatalogBlob> materialCatalog = default;
                BlobAssetReference<TerrainBlockPaletteCatalogBlob> blockPaletteCatalog = default;
                BuiltInTerrainBlockRuntimeIds terrainRuntimeIds = default;
                bool terrainDefinitionsCreated =
                    BuiltInTerrainBlockDefinitions.TryCreateTerrainDefinitions(
                        catalog,
                        out TerrainBlockDefinitionBuildEntry[] terrainDefinitions);
                bool terrainCatalogCreated = terrainDefinitionsCreated
                    && TerrainBlockCatalogBuilder.TryBuild(
                        catalog,
                        terrainDefinitions,
                        out terrainCatalog);
                bool materialCatalogCreated = terrainCatalogCreated
                    && TerrainBlockMaterialCatalogBuilder.TryBuild(
                        catalog,
                        TerrainBlockMaterialDefinitions.CreateDefault(),
                        out materialCatalog);
                bool blockPaletteCatalogCreated = materialCatalogCreated
                    && TerrainBlockPaletteCatalogBuilder.TryBuild(
                        terrainCatalog,
                        materialCatalog,
                        TerrainBlockPaletteDefinitions.CreateBiomeSurfaceDefaults(),
                        TerrainBlockPaletteDefinitions.CreateGeologyDefaults(),
                        out blockPaletteCatalog);
                bool builtInIdsResolved = blockPaletteCatalogCreated
                    && BuiltInTerrainBlockDefinitions.TryResolveRuntimeIds(
                        catalog,
                        out terrainRuntimeIds);
                if (!terrainDefinitionsCreated
                    || !terrainCatalogCreated
                    || !materialCatalogCreated
                    || !blockPaletteCatalogCreated
                    || !builtInIdsResolved)
                {
                    // どのBlobもまだBakerへ登録していないため、失敗時はこの処理が作成済みBlobの所有権を解放します。
                    if (terrainCatalog.IsCreated)
                    {
                        terrainCatalog.Dispose();
                    }

                    if (materialCatalog.IsCreated)
                    {
                        materialCatalog.Dispose();
                    }

                    if (blockPaletteCatalog.IsCreated)
                    {
                        blockPaletteCatalog.Dispose();
                    }

                    animalCatalog.Dispose();
                    catalog.Dispose();
                    throw new InvalidOperationException(
                        "組み込み地形定義、41材質対応、または固定Blockパレットを共通DefinitionCatalogへ接続できませんでした。");
                }

                // Bakerへ登録して差分Bake時の参照数を正しく管理し、手動Disposeによる二重解放を避けます。
                AddBlobAsset(ref catalog, out _);
                AddBlobAsset(ref animalCatalog, out _);
                AddBlobAsset(ref terrainCatalog, out _);
                AddBlobAsset(ref materialCatalog, out _);
                AddBlobAsset(ref blockPaletteCatalog, out _);
                Entity entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new DefinitionCatalog
                {
                    Value = catalog
                });
                AddComponent(entity, new AnimalSpeciesCatalog
                {
                    Value = animalCatalog
                });
                AddComponent(entity, new TerrainBlockCatalog
                {
                    Value = terrainCatalog,
                    BuiltInRuntimeIds = terrainRuntimeIds
                });
                AddComponent(entity, new TerrainBlockMaterialCatalog
                {
                    Value = materialCatalog
                });
                AddComponent(entity, new TerrainBlockPaletteCatalog
                {
                    Value = blockPaletteCatalog
                });
            }

            /// <summary>
            /// TERRAIN-01Cより前に固有属性の入力欄がない地形分類を共通定義へ追加する設定を拒否します。
            /// Bakerが地形Blobの件数不一致まで進む前に、Inspector上の原因を直接示すために呼び出します。
            /// </summary>
            /// <param name="kind">Inspectorで選択された定義分類です。</param>
            /// <param name="displayName">エラー箇所を識別する編集用表示名です。</param>
            /// <param name="definitionIndex">definitions配列内の0始まりの位置です。</param>
            /// <exception cref="InvalidOperationException">組み込み専用の地形4分類が手動追加されている場合に発生します。</exception>
            private static void RejectUnsupportedAuthoredTerrainDefinition(
                DefinitionKind kind,
                string displayName,
                int definitionIndex)
            {
                bool isTerrainKind = kind == DefinitionKind.GroundBlock
                    || kind == DefinitionKind.WallBlock
                    || kind == DefinitionKind.Liquid
                    || kind == DefinitionKind.TerrainSurface;
                if (!isTerrainKind)
                {
                    return;
                }

                // 現段階は地形固有の硬度・回収物・通行属性をInspectorから入力できないため、
                // 共通定義だけを受理すると対応するTerrainBlock定義が欠けた破損カタログになります。
                string definitionLabel = string.IsNullOrWhiteSpace(displayName)
                    ? $"Definition[{definitionIndex}]"
                    : $"Definition[{definitionIndex}] ({displayName})";
                throw new InvalidOperationException(
                    $"地形分類は組み込み定義だけを使用してください: {definitionLabel}, 分類 {kind}");
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

            /// <summary>
            /// 動物種Builderの位置付きエラーを、Inspectorで修正できる日本語へ変換します。
            /// </summary>
            /// <param name="sourceDefinitions">Inspector上の動物種設定配列です。</param>
            /// <param name="error">失敗理由、入力添字、欠落した実行時IDです。</param>
            /// <returns>設定番号と具体的な制約を含むBakeエラーです。</returns>
            private static string CreateAnimalValidationMessage(
                AnimalSpeciesEntry[] sourceDefinitions,
                AnimalSpeciesCatalogValidationError error)
            {
                string definitionLabel = GetAnimalDefinitionLabel(
                    sourceDefinitions,
                    error.DefinitionIndex);
                string relatedDefinitionLabel = GetAnimalDefinitionLabel(
                    sourceDefinitions,
                    error.RelatedDefinitionIndex);

                // 数値だけのエラーコードを、初心者がInspector上で直せる単位と制約へ置き換えます。
                switch (error.Code)
                {
                    case AnimalSpeciesCatalogValidationErrorCode.DefinitionCatalogIsInvalid:
                        return "動物種設定を接続する共通定義カタログが作成されていません。";
                    case AnimalSpeciesCatalogValidationErrorCode.DefinitionGuidIsInvalid:
                        return $"動物種の定義GUIDが未設定または不正です: {definitionLabel}";
                    case AnimalSpeciesCatalogValidationErrorCode.DefinitionWasNotFound:
                        return $"動物種GUIDに対応する共通定義がありません: {definitionLabel}";
                    case AnimalSpeciesCatalogValidationErrorCode.DefinitionKindMismatch:
                        return $"動物種GUIDの共通定義がAnimal Species分類ではありません: {definitionLabel}";
                    case AnimalSpeciesCatalogValidationErrorCode.DefinitionIsDuplicated:
                        return $"同じ動物種GUIDの設定が重複しています: {definitionLabel} / {relatedDefinitionLabel}";
                    case AnimalSpeciesCatalogValidationErrorCode.DefinitionIsMissing:
                        return $"共通定義の動物種に固有設定がありません: RuntimeId {error.MissingRuntimeId.Value}";
                    case AnimalSpeciesCatalogValidationErrorCode.AllowedBiomesAreInvalid:
                        return $"動物種の対応バイオームが未設定または不正です: {definitionLabel}";
                    case AnimalSpeciesCatalogValidationErrorCode.AllowedGroundsAreInvalid:
                        return $"動物種の有効地形が未設定または不正です: {definitionLabel}";
                    case AnimalSpeciesCatalogValidationErrorCode.HerdSizeRangeIsInvalid:
                        return $"群れサイズは最小1個体で、最大値を最小値以上にしてください: {definitionLabel}";
                    case AnimalSpeciesCatalogValidationErrorCode.PopulationDensityIsInvalid:
                        return $"4096有効タイル当たりの目標個体数は1以上にしてください: {definitionLabel}";
                    case AnimalSpeciesCatalogValidationErrorCode.IdleTickRangeIsInvalid:
                        return $"最大待機Tickを最小待機Tick以上にしてください: {definitionLabel}";
                    case AnimalSpeciesCatalogValidationErrorCode.MovementStepIntervalIsInvalid:
                        return $"1歩の移動間隔は1固定Tick以上にしてください: {definitionLabel}";
                    case AnimalSpeciesCatalogValidationErrorCode.WanderDistanceIsInvalid:
                        return $"最大徘徊距離は1タイル以上にしてください: {definitionLabel}";
                    case AnimalSpeciesCatalogValidationErrorCode.AppearanceVariantCountIsInvalid:
                        return $"外見差分数は1以上にしてください: {definitionLabel}";
                    default:
                        return $"動物種カタログを構築できませんでした: {error.Code}";
                }
            }

            /// <summary>
            /// 動物種設定の添字とGUIDから、Bakeエラーへ含める識別文字列を作ります。
            /// </summary>
            /// <param name="sourceDefinitions">Inspector上の動物種設定配列です。</param>
            /// <param name="definitionIndex">表示する0始まりの入力添字です。</param>
            /// <returns>範囲内なら番号とGUID、範囲外なら「対象なし」です。</returns>
            private static string GetAnimalDefinitionLabel(
                AnimalSpeciesEntry[] sourceDefinitions,
                int definitionIndex)
            {
                if (definitionIndex < 0
                    || definitionIndex >= sourceDefinitions.Length)
                {
                    return "対象なし";
                }

                string guid = sourceDefinitions[definitionIndex]?.DefinitionGuid;
                return string.IsNullOrWhiteSpace(guid)
                    ? $"AnimalSpecies[{definitionIndex}]"
                    : $"AnimalSpecies[{definitionIndex}] ({guid})";
            }
        }
    }
}
