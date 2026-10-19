// このファイルは、ワールド固有の動物個体群Seed、地域抽選状態、全野生個体を共通状態ハッシュへ追加します。
// Entity格納順を正規化し、個体の種と外見を静的カタログへ照合してから、同じ論理状態へ同じ値を返します。

using System;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.Simulation.Animals
{
    /// <summary>
    /// 動物個体群の状態ハッシュ入力を作れなかった理由です。
    /// Singletonの個数異常と、地域・個体の保存契約違反を診断側へ返します。
    /// </summary>
    public enum AnimalPopulationStateHashErrorCode : byte
    {
        /// <summary>個体群の全論理状態を固定順で追加できました。</summary>
        None = 0,

        /// <summary>ワールド固有のAnimalPopulationSeed Singletonが存在しません。</summary>
        PopulationSeedIsMissing = 1,

        /// <summary>AnimalPopulationSeed Singletonが複数存在します。</summary>
        PopulationSeedIsDuplicated = 2,

        /// <summary>Seedが0、地域座標が重複、または地域の次回受付順が0です。</summary>
        RegionStateIsInvalid = 3,

        /// <summary>永続ID、種ID、群れID、または外見差分を含む個体状態が不正です。</summary>
        AnimalStateIsInvalid = 4
    }

    /// <summary>
    /// 動物個体群の状態ハッシュ処理結果です。
    /// 例外を通常の状態検証に使わず、テストや診断が失敗理由を比較できるようにします。
    /// </summary>
    public readonly struct AnimalPopulationStateHashError
    {
        /// <summary>成功ならNone、失敗なら最初に見つかった個体群状態の問題です。</summary>
        public readonly AnimalPopulationStateHashErrorCode Code;

        /// <summary>指定された成功・失敗理由を保持する結果を作ります。</summary>
        /// <param name="code">個体群ハッシュの処理結果です。</param>
        public AnimalPopulationStateHashError(AnimalPopulationStateHashErrorCode code)
        {
            Code = code;
        }

        /// <summary>個体群状態を完全に追加できた場合はtrueです。</summary>
        public bool Succeeded => Code == AnimalPopulationStateHashErrorCode.None;
    }

    /// <summary>
    /// ECS Worldの動物個体群を静的な種定義へ照合し、地域座標と永続IDで正規化して機能固有セクションへ追加します。
    /// AnimalSpeciesStateHasherの成功後に呼び、最後のCompleteは呼び出し側が一度だけ行います。
    /// </summary>
    public static class AnimalPopulationStateHasher
    {
        /// <summary>動物種定義1024の直後へ割り当てる、動物個体群の安定したセクションIDです。</summary>
        public const uint PopulationSectionId =
            AnimalSpeciesStateHasher.SpeciesDefinitionSectionId + 1;

        /// <summary>
        /// 動物種カタログ、Seed Singleton、全地域状態、全野生個体を検証し、順序を正規化してBuilderへ追加します。
        /// </summary>
        /// <param name="entityManager">固定Tick完了境界のECS Worldを読み取るEntityManagerです。</param>
        /// <param name="builder">共通状態と動物種定義を追加済みの状態ハッシュBuilderです。</param>
        /// <param name="error">失敗時のSingletonまたは保存状態の不整合理由です。</param>
        /// <returns>全論理状態を追加できた場合はtrueです。</returns>
        public static bool TryAppendTo(
            EntityManager entityManager,
            ref SimulationStateHashBuilder builder,
            out AnimalPopulationStateHashError error)
        {
            EntityQuery animalCatalogQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AnimalSpeciesCatalog>());
            EntityQuery seedQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AnimalPopulationSeed>());
            EntityQuery regionQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<AnimalPopulationRegionState>());
            EntityQuery wildAnimalQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<WildAnimal>());
            EntityQuery animalQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<WildAnimal>(),
                ComponentType.ReadOnly<PersistentObjectId>(),
                ComponentType.ReadOnly<AnimalSpecies>(),
                ComponentType.ReadOnly<AnimalTilePosition>(),
                ComponentType.ReadOnly<AnimalHerdMembership>(),
                ComponentType.ReadOnly<AnimalAppearanceVariant>());
            NativeArray<AnimalPopulationRegionState> regions = default;
            NativeArray<AnimalPopulationRegionHashRecord> regionRecords = default;
            NativeArray<PersistentObjectId> ids = default;
            NativeArray<AnimalSpecies> species = default;
            NativeArray<AnimalTilePosition> positions = default;
            NativeArray<AnimalHerdMembership> herds = default;
            NativeArray<AnimalAppearanceVariant> variants = default;
            NativeArray<AnimalPopulationHashRecord> animalRecords = default;
            try
            {
                int animalCatalogCount = animalCatalogQuery.CalculateEntityCount();
                if (animalCatalogCount != 1)
                {
                    // 種定義が一意でなければ個体の種IDと外見番号を検証できないため、比較可能な状態として扱いません。
                    error = new AnimalPopulationStateHashError(
                        AnimalPopulationStateHashErrorCode.AnimalStateIsInvalid);
                    return false;
                }

                AnimalSpeciesCatalog animalCatalog =
                    animalCatalogQuery.GetSingleton<AnimalSpeciesCatalog>();
                if (!animalCatalog.Value.IsCreated)
                {
                    error = new AnimalPopulationStateHashError(
                        AnimalPopulationStateHashErrorCode.AnimalStateIsInvalid);
                    return false;
                }

                int seedCount = seedQuery.CalculateEntityCount();
                if (seedCount != 1)
                {
                    error = new AnimalPopulationStateHashError(
                        seedCount == 0
                            ? AnimalPopulationStateHashErrorCode.PopulationSeedIsMissing
                            : AnimalPopulationStateHashErrorCode.PopulationSeedIsDuplicated);
                    return false;
                }

                AnimalPopulationSeed seed = seedQuery.GetSingleton<AnimalPopulationSeed>();
                if (!seed.IsValid)
                {
                    error = new AnimalPopulationStateHashError(
                        AnimalPopulationStateHashErrorCode.RegionStateIsInvalid);
                    return false;
                }

                regions = regionQuery.ToComponentDataArray<AnimalPopulationRegionState>(Allocator.Temp);
                regionRecords = new NativeArray<AnimalPopulationRegionHashRecord>(
                    regions.Length,
                    Allocator.Temp);
                for (int index = 0; index < regions.Length; index++)
                {
                    AnimalPopulationRegionState region = regions[index];
                    regionRecords[index] = new AnimalPopulationRegionHashRecord(region);
                }

                regionRecords.Sort();
                if (!ValidateRegions(regionRecords))
                {
                    error = new AnimalPopulationStateHashError(
                        AnimalPopulationStateHashErrorCode.RegionStateIsInvalid);
                    return false;
                }

                ids = animalQuery.ToComponentDataArray<PersistentObjectId>(Allocator.Temp);
                species = animalQuery.ToComponentDataArray<AnimalSpecies>(Allocator.Temp);
                positions = animalQuery.ToComponentDataArray<AnimalTilePosition>(Allocator.Temp);
                herds = animalQuery.ToComponentDataArray<AnimalHerdMembership>(Allocator.Temp);
                variants = animalQuery.ToComponentDataArray<AnimalAppearanceVariant>(Allocator.Temp);
                animalRecords = new NativeArray<AnimalPopulationHashRecord>(
                    ids.Length,
                    Allocator.Temp);
                if (wildAnimalQuery.CalculateEntityCount() != ids.Length)
                {
                    // WildAnimalタグだけ残り、位置などの必須Componentが欠けた個体を「存在しない個体」として黙って除外しません。
                    error = new AnimalPopulationStateHashError(
                        AnimalPopulationStateHashErrorCode.AnimalStateIsInvalid);
                    return false;
                }

                for (int index = 0; index < ids.Length; index++)
                {
                    animalRecords[index] = new AnimalPopulationHashRecord(
                        ids[index],
                        species[index],
                        positions[index],
                        herds[index],
                        variants[index]);
                }

                animalRecords.Sort();
                if (!ValidateAnimals(animalRecords, animalCatalog.Value))
                {
                    error = new AnimalPopulationStateHashError(
                        AnimalPopulationStateHashErrorCode.AnimalStateIsInvalid);
                    return false;
                }

                AppendState(seed, regionRecords, animalRecords, ref builder);
                error = new AnimalPopulationStateHashError(
                    AnimalPopulationStateHashErrorCode.None);
                return true;
            }
            finally
            {
                // 一時配列とQueryはWorldへ残さず、途中で検証に失敗した場合も必ず破棄します。
                if (regions.IsCreated)
                {
                    regions.Dispose();
                }

                if (regionRecords.IsCreated)
                {
                    regionRecords.Dispose();
                }

                if (ids.IsCreated)
                {
                    ids.Dispose();
                }

                if (species.IsCreated)
                {
                    species.Dispose();
                }

                if (positions.IsCreated)
                {
                    positions.Dispose();
                }

                if (herds.IsCreated)
                {
                    herds.Dispose();
                }

                if (variants.IsCreated)
                {
                    variants.Dispose();
                }

                if (animalRecords.IsCreated)
                {
                    animalRecords.Dispose();
                }

                animalCatalogQuery.Dispose();
                seedQuery.Dispose();
                regionQuery.Dispose();
                wildAnimalQuery.Dispose();
                animalQuery.Dispose();
            }
        }

        /// <summary>地域座標の重複と、保存後に再開できない0の受付順を拒否します。</summary>
        private static bool ValidateRegions(NativeArray<AnimalPopulationRegionHashRecord> records)
        {
            for (int index = 0; index < records.Length; index++)
            {
                AnimalPopulationRegionHashRecord current = records[index];
                bool isDuplicated = index > 0
                    && records[index - 1].RegionX == current.RegionX
                    && records[index - 1].RegionY == current.RegionY;
                if (current.NextSpawnSequence == 0 || isDuplicated)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 個体IDと群れ参照に加え、種IDの存在と外見番号が静的な動物種定義の範囲内であることを確認します。
        /// </summary>
        /// <param name="records">永続ID順へ整列済みの個体状態です。</param>
        /// <param name="animalCatalog">種IDと外見差分数を照合する検証済み動物種カタログです。</param>
        /// <returns>全個体が保存契約と現在の種定義を満たす場合はtrueです。</returns>
        private static bool ValidateAnimals(
            NativeArray<AnimalPopulationHashRecord> records,
            BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog)
        {
            for (int index = 0; index < records.Length; index++)
            {
                AnimalPopulationHashRecord current = records[index];
                bool idIsDuplicated = index > 0
                    && records[index - 1].PersistentId == current.PersistentId;
                bool speciesWasFound = AnimalSpeciesCatalogLookup.TryGetDefinition(
                    animalCatalog,
                    new DefinitionRuntimeId(current.SpeciesRuntimeId),
                    out AnimalSpeciesDefinitionBlob speciesDefinition);
                if (current.PersistentId == PersistentObjectId.InvalidValue
                    || idIsDuplicated
                    || !speciesWasFound
                    || current.AppearanceVariant >= speciesDefinition.AppearanceVariantCount
                    || current.HerdId == PersistentObjectId.InvalidValue
                    || !ReferencesSameSpeciesHerdLeader(records, current))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 群れIDを二分探索し、同種かつ自分のIDを群れIDに持つ先頭個体への直接参照か検証します。
        /// 存在だけを調べると循環参照やメンバーへの参照も通るため、参照先の所属契約まで照合します。
        /// </summary>
        /// <param name="records">永続ID順へ整列済みの全個体状態です。</param>
        /// <param name="animal">所属先を検証する個体です。先頭自身の自己参照は正常です。</param>
        /// <returns>同種の実在する群れ先頭を直接指す場合はtrueです。</returns>
        private static bool ReferencesSameSpeciesHerdLeader(
            NativeArray<AnimalPopulationHashRecord> records,
            AnimalPopulationHashRecord animal)
        {
            int minimum = 0;
            int maximum = records.Length - 1;
            while (minimum <= maximum)
            {
                int middle = minimum + ((maximum - minimum) / 2);
                ulong candidate = records[middle].PersistentId;
                if (candidate == animal.HerdId)
                {
                    AnimalPopulationHashRecord leader = records[middle];
                    return leader.HerdId == leader.PersistentId
                        && leader.SpeciesRuntimeId == animal.SpeciesRuntimeId;
                }

                if (candidate < animal.HerdId)
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

        /// <summary>検証・整列済みの論理値を、構造体paddingへ依存しない固定順で書き込みます。</summary>
        private static void AppendState(
            AnimalPopulationSeed seed,
            NativeArray<AnimalPopulationRegionHashRecord> regions,
            NativeArray<AnimalPopulationHashRecord> animals,
            ref SimulationStateHashBuilder builder)
        {
            builder.BeginSection(PopulationSectionId);
            builder.WriteUInt64(seed.Value);
            builder.WriteInt32(regions.Length);
            for (int index = 0; index < regions.Length; index++)
            {
                AnimalPopulationRegionHashRecord region = regions[index];
                builder.WriteInt32(region.RegionX);
                builder.WriteInt32(region.RegionY);
                builder.WriteUInt64(region.RandomState);
                builder.WriteUInt64(region.NextSpawnSequence);
                builder.WriteUInt64(region.InitializedAtTick);
            }

            builder.WriteInt32(animals.Length);
            for (int index = 0; index < animals.Length; index++)
            {
                AnimalPopulationHashRecord animal = animals[index];
                builder.WriteUInt64(animal.PersistentId);
                builder.WriteInt32(animal.SpeciesRuntimeId);
                builder.WriteInt32(animal.TileX);
                builder.WriteInt32(animal.TileY);
                builder.WriteUInt64(animal.HerdId);
                builder.WriteByte(animal.AppearanceVariant);
            }
        }

        /// <summary>地域状態を座標順へ並べるための、一時的な固定値レコードです。</summary>
        private readonly struct AnimalPopulationRegionHashRecord : IComparable<AnimalPopulationRegionHashRecord>
        {
            /// <summary>地域の水平論理チャンク座標です。</summary>
            public readonly int RegionX;

            /// <summary>地域の垂直論理チャンク座標です。</summary>
            public readonly int RegionY;

            /// <summary>次回抽選が継続する64bit乱数状態です。</summary>
            public readonly ulong RandomState;

            /// <summary>次回個体へ割り当てる地域内受付順です。</summary>
            public readonly ulong NextSpawnSequence;

            /// <summary>地域の初期補充を確定した固定Tickです。</summary>
            public readonly ulong InitializedAtTick;

            /// <summary>保存対象Componentから、並べ替え用の値コピーを作ります。</summary>
            public AnimalPopulationRegionHashRecord(AnimalPopulationRegionState state)
            {
                RegionX = state.RegionX;
                RegionY = state.RegionY;
                RandomState = state.RandomState;
                NextSpawnSequence = state.NextSpawnSequence;
                InitializedAtTick = state.InitializedAtTick;
            }

            /// <summary>Entity生成順を無視し、地域Xの後に地域Yで昇順比較します。</summary>
            public int CompareTo(AnimalPopulationRegionHashRecord other)
            {
                int xComparison = RegionX.CompareTo(other.RegionX);
                return xComparison != 0 ? xComparison : RegionY.CompareTo(other.RegionY);
            }
        }

        /// <summary>個体状態を永続ID順へ並べるための、一時的な固定値レコードです。</summary>
        private readonly struct AnimalPopulationHashRecord : IComparable<AnimalPopulationHashRecord>
        {
            /// <summary>個体の一意な永続IDです。</summary>
            public readonly ulong PersistentId;

            /// <summary>共通定義カタログの動物種Runtime IDです。</summary>
            public readonly int SpeciesRuntimeId;

            /// <summary>個体の絶対水平タイル座標です。</summary>
            public readonly int TileX;

            /// <summary>個体の絶対垂直タイル座標です。</summary>
            public readonly int TileY;

            /// <summary>同じ群れの先頭個体が持つ永続IDです。</summary>
            public readonly ulong HerdId;

            /// <summary>動物種内の0始まりの外見差分番号です。</summary>
            public readonly byte AppearanceVariant;

            /// <summary>複数Componentから、1個体分の並べ替え用値コピーを作ります。</summary>
            public AnimalPopulationHashRecord(
                PersistentObjectId id,
                AnimalSpecies species,
                AnimalTilePosition position,
                AnimalHerdMembership herd,
                AnimalAppearanceVariant variant)
            {
                PersistentId = id.Value;
                SpeciesRuntimeId = species.RuntimeId.Value;
                TileX = position.TileX;
                TileY = position.TileY;
                HerdId = herd.HerdId;
                AppearanceVariant = variant.Value;
            }

            /// <summary>Entity格納順を無視し、永続IDの昇順だけで比較します。</summary>
            public int CompareTo(AnimalPopulationHashRecord other)
            {
                return PersistentId.CompareTo(other.PersistentId);
            }
        }
    }
}
