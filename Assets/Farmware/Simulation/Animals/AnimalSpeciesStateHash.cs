// このファイルは、動物種の静的な生成・移動設定を共通状態ハッシュへ追加します。
// 共通DefinitionCatalogの後へ機能固有セクションとして接続し、同じGUIDでも調整値が異なるWorldを同一状態と誤判定しないようにします。

using Unity.Entities;

namespace Farmware.Simulation.Animals
{
    /// <summary>
    /// 動物種の状態ハッシュ入力を作れなかった理由です。
    /// Singletonの欠落・重複と、Blob内容の不整合を分けて診断へ返します。
    /// </summary>
    public enum AnimalSpeciesStateHashErrorCode : byte
    {
        /// <summary>動物種の静的定義をすべてハッシュへ追加できました。</summary>
        None = 0,

        /// <summary>共通DefinitionCatalog Singletonが存在しません。</summary>
        DefinitionCatalogIsMissing = 1,

        /// <summary>共通DefinitionCatalog Singletonが複数存在します。</summary>
        DefinitionCatalogIsDuplicated = 2,

        /// <summary>AnimalSpeciesCatalog Singletonが存在しません。</summary>
        AnimalCatalogIsMissing = 3,

        /// <summary>AnimalSpeciesCatalog Singletonが複数存在します。</summary>
        AnimalCatalogIsDuplicated = 4,

        /// <summary>Blob Asset、実行時ID、分類、並び、または動物種の調整値が不正です。</summary>
        CatalogDataIsInvalid = 5
    }

    /// <summary>
    /// 動物種の状態ハッシュ処理結果です。
    /// 例外を通常の検証分岐に使わず、呼び出し側がログやテストで理由を確認できるようにします。
    /// </summary>
    public readonly struct AnimalSpeciesStateHashError
    {
        /// <summary>成功ならNone、失敗なら最初に見つかった理由です。</summary>
        public readonly AnimalSpeciesStateHashErrorCode Code;

        /// <summary>
        /// 指定した理由を持つ検証結果を作成します。
        /// </summary>
        /// <param name="code">成功または失敗理由です。</param>
        public AnimalSpeciesStateHashError(AnimalSpeciesStateHashErrorCode code)
        {
            Code = code;
        }

        /// <summary>動物種の静的定義を完全に追加できた場合はtrueです。</summary>
        public bool Succeeded => Code == AnimalSpeciesStateHashErrorCode.None;
    }

    /// <summary>
    /// ECS Worldに一意に存在する共通定義と動物種定義を検証し、固定順で状態ハッシュへ追加します。
    /// 呼び出し側は先にSimulationCoreStateHasherを成功させ、最後にBuilderをCompleteします。
    /// </summary>
    public static class AnimalSpeciesStateHasher
    {
        /// <summary>
        /// 動物種の静的な生成・移動設定へ割り当てる最初の機能固有セクションIDです。
        /// 今後個体群など別の動物状態を追加するときは、この値より大きいIDを使用します。
        /// </summary>
        public const uint SpeciesDefinitionSectionId =
            SimulationStateHashSchema.FirstFeatureSectionId;

        /// <summary>
        /// World内の2つの定義Singletonを一意に取得し、動物種の論理値だけをハッシュへ追加します。
        /// </summary>
        /// <param name="entityManager">固定Tick完了境界のECS Worldを読み取るEntityManagerです。</param>
        /// <param name="builder">共通セクションを追加済みで、まだCompleteしていないBuilderです。</param>
        /// <param name="error">失敗時のSingletonまたはBlob不整合理由です。</param>
        /// <returns>全動物種を検証して追加できた場合はtrueです。</returns>
        public static bool TryAppendTo(
            EntityManager entityManager,
            ref SimulationStateHashBuilder builder,
            out AnimalSpeciesStateHashError error)
        {
            EntityQuery definitionQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<DefinitionCatalog>());
            EntityQuery animalQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AnimalSpeciesCatalog>());
            try
            {
                int definitionCount = definitionQuery.CalculateEntityCount();
                if (definitionCount != 1)
                {
                    error = new AnimalSpeciesStateHashError(
                        definitionCount == 0
                            ? AnimalSpeciesStateHashErrorCode.DefinitionCatalogIsMissing
                            : AnimalSpeciesStateHashErrorCode.DefinitionCatalogIsDuplicated);
                    return false;
                }

                int animalCount = animalQuery.CalculateEntityCount();
                if (animalCount != 1)
                {
                    error = new AnimalSpeciesStateHashError(
                        animalCount == 0
                            ? AnimalSpeciesStateHashErrorCode.AnimalCatalogIsMissing
                            : AnimalSpeciesStateHashErrorCode.AnimalCatalogIsDuplicated);
                    return false;
                }

                DefinitionCatalog definitionCatalog =
                    definitionQuery.GetSingleton<DefinitionCatalog>();
                AnimalSpeciesCatalog animalCatalog =
                    animalQuery.GetSingleton<AnimalSpeciesCatalog>();
                return TryAppendCatalogs(
                    definitionCatalog.Value,
                    animalCatalog.Value,
                    ref builder,
                    out error);
            }
            finally
            {
                // EntityQueryは一時的な検索器なので、この呼び出し内で確実に破棄します。
                definitionQuery.Dispose();
                animalQuery.Dispose();
            }
        }

        /// <summary>
        /// 検証済みであるべき2つのBlobを照合し、実行時ID順の全フィールドを固定幅整数として追加します。
        /// </summary>
        /// <param name="definitionCatalog">動物種の実行時IDと分類を確認する共通定義Blobです。</param>
        /// <param name="animalCatalog">動物種固有の生成・移動設定Blobです。</param>
        /// <param name="builder">動物種セクションを書き込む状態ハッシュBuilderです。</param>
        /// <param name="error">Blob内容が不正な場合の理由です。</param>
        /// <returns>共通定義との一対一対応と全値を確認して追加できた場合はtrueです。</returns>
        private static bool TryAppendCatalogs(
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog,
            BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog,
            ref SimulationStateHashBuilder builder,
            out AnimalSpeciesStateHashError error)
        {
            if (!definitionCatalog.IsCreated || !animalCatalog.IsCreated)
            {
                error = new AnimalSpeciesStateHashError(
                    AnimalSpeciesStateHashErrorCode.CatalogDataIsInvalid);
                return false;
            }

            ref DefinitionCatalogBlob definitionRoot = ref definitionCatalog.Value;
            ref AnimalSpeciesCatalogBlob animalRoot = ref animalCatalog.Value;
            int expectedAnimalCount = 0;
            for (int definitionIndex = 0;
                 definitionIndex < definitionRoot.Definitions.Length;
                 definitionIndex++)
            {
                if (definitionRoot.Definitions[definitionIndex].Kind
                    == DefinitionKind.AnimalSpecies)
                {
                    expectedAnimalCount++;
                }
            }

            if (animalRoot.Species.Length != expectedAnimalCount)
            {
                error = new AnimalSpeciesStateHashError(
                    AnimalSpeciesStateHashErrorCode.CatalogDataIsInvalid);
                return false;
            }

            builder.BeginSection(SpeciesDefinitionSectionId);
            builder.WriteInt32(animalRoot.Species.Length);
            int previousRuntimeId = DefinitionRuntimeId.InvalidValue;
            for (int speciesIndex = 0;
                 speciesIndex < animalRoot.Species.Length;
                 speciesIndex++)
            {
                AnimalSpeciesDefinitionBlob species = animalRoot.Species[speciesIndex];
                int runtimeId = species.SpeciesRuntimeId.Value;
                bool runtimeIdIsOutsideCatalog = runtimeId <= previousRuntimeId
                    || runtimeId <= DefinitionRuntimeId.InvalidValue
                    || runtimeId > definitionRoot.Definitions.Length;
                if (runtimeIdIsOutsideCatalog)
                {
                    error = new AnimalSpeciesStateHashError(
                        AnimalSpeciesStateHashErrorCode.CatalogDataIsInvalid);
                    return false;
                }

                DefinitionBlobRecord commonDefinition =
                    definitionRoot.Definitions[runtimeId - 1];
                if (!commonDefinition.RuntimeId.Equals(species.SpeciesRuntimeId)
                    || commonDefinition.Kind != DefinitionKind.AnimalSpecies
                    || !HasValidValues(species))
                {
                    error = new AnimalSpeciesStateHashError(
                        AnimalSpeciesStateHashErrorCode.CatalogDataIsInvalid);
                    return false;
                }

                // フィールド順と型はハッシュ契約なので、構造体の生メモリやpaddingは使用しません。
                builder.WriteInt32(runtimeId);
                builder.WriteByte((byte)species.AllowedBiomes);
                builder.WriteByte((byte)species.AllowedGrounds);
                builder.WriteByte(species.MinimumHerdSize);
                builder.WriteByte(species.MaximumHerdSize);
                builder.WriteUInt16(species.TargetPopulationPer4096EligibleTiles);
                builder.WriteUInt32(species.MinimumIdleTicks);
                builder.WriteUInt32(species.MaximumIdleTicks);
                builder.WriteUInt32(species.MovementStepIntervalTicks);
                builder.WriteUInt16(species.MaximumWanderDistanceInTiles);
                builder.WriteByte(species.AppearanceVariantCount);
                previousRuntimeId = runtimeId;
            }

            error = new AnimalSpeciesStateHashError(
                AnimalSpeciesStateHashErrorCode.None);
            return true;
        }

        /// <summary>
        /// Blob内の動物種設定がBuilderと同じ値域契約を満たすか確認します。
        /// </summary>
        /// <param name="species">Bake後の1動物種設定です。</param>
        /// <returns>未知bit、空Mask、0禁止値、逆転した範囲がなければtrueです。</returns>
        private static bool HasValidValues(AnimalSpeciesDefinitionBlob species)
        {
            bool biomesAreValid = species.AllowedBiomes != AnimalBiomeMask.None
                && (species.AllowedBiomes & ~AnimalBiomeMask.All) == 0;
            bool groundsAreValid = species.AllowedGrounds != AnimalGroundMask.None
                && (species.AllowedGrounds & ~AnimalGroundMask.All) == 0;
            return biomesAreValid
                && groundsAreValid
                && species.MinimumHerdSize > 0
                && species.MinimumHerdSize <= species.MaximumHerdSize
                && species.TargetPopulationPer4096EligibleTiles > 0
                && species.MinimumIdleTicks <= species.MaximumIdleTicks
                && species.MovementStepIntervalTicks > 0
                && species.MaximumWanderDistanceInTiles > 0
                && species.AppearanceVariantCount > 0;
        }
    }
}
