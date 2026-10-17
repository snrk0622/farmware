// このファイルは、動物種のGUID接続、生成条件、群れ、固定Tick移動、外見差分の検証契約を保証します。
// Sceneを使わず、DefinitionCatalogAuthoringのBakerと同じ2つのBuilderを順に実行します。

using Farmware.Simulation;
using Farmware.Simulation.Animals;
using NUnit.Framework;
using Unity.Entities;

namespace Farmware.Tests.EditMode
{
    /// <summary>
    /// ANIMAL-01Aで追加した動物種カタログの構築、検索、異常入力拒否を検証します。
    /// 後続の生成・移動Systemが未検証のInspector値を受け取らないことを保証するテストです。
    /// </summary>
    public sealed class AnimalSpeciesCatalogTests
    {
        /// <summary>テスト内で鶏を表す固定GUIDの下位64bitです。</summary>
        private const ulong ChickenGuidLow = 0x10;

        /// <summary>
        /// 鶏の全設定が共通実行時IDへ接続され、許可した草原・陸地だけを判定できることを保証します。
        /// </summary>
        [Test]
        public void TryBuild_CreatesChickenDefinitionAndEligibilityLookup()
        {
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog =
                CreateDefinitionCatalog(
                    CreateDefinition(CreateGuid(ChickenGuidLow), DefinitionKind.AnimalSpecies));
            BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog = default;
            try
            {
                bool succeeded = AnimalSpeciesCatalogBuilder.TryBuild(
                    definitionCatalog,
                    new[] { CreateChickenDefinition() },
                    out animalCatalog,
                    out AnimalSpeciesCatalogValidationError error);

                Assert.That(succeeded, Is.True);
                Assert.That(error.Code, Is.EqualTo(AnimalSpeciesCatalogValidationErrorCode.None));
                Assert.That(
                    DefinitionCatalogLookup.TryResolve(
                        definitionCatalog,
                        CreateGuid(ChickenGuidLow),
                        out DefinitionRuntimeId chickenRuntimeId),
                    Is.True);
                Assert.That(
                    AnimalSpeciesCatalogLookup.TryGetDefinition(
                        animalCatalog,
                        chickenRuntimeId,
                        out AnimalSpeciesDefinitionBlob chicken),
                    Is.True);
                Assert.That(chicken.AllowedBiomes, Is.EqualTo(AnimalBiomeMask.Grassland));
                Assert.That(chicken.AllowedGrounds, Is.EqualTo(AnimalGroundMask.Land));
                Assert.That(chicken.MinimumHerdSize, Is.EqualTo(3));
                Assert.That(chicken.MaximumHerdSize, Is.EqualTo(6));
                Assert.That(chicken.TargetPopulationPer4096EligibleTiles, Is.EqualTo(8));
                Assert.That(chicken.MinimumIdleTicks, Is.EqualTo(20));
                Assert.That(chicken.MaximumIdleTicks, Is.EqualTo(80));
                Assert.That(chicken.MovementStepIntervalTicks, Is.EqualTo(5));
                Assert.That(chicken.MaximumWanderDistanceInTiles, Is.EqualTo(8));
                Assert.That(chicken.AppearanceVariantCount, Is.EqualTo(3));
                Assert.That(
                    AnimalSpeciesCatalogLookup.IsBiomeAllowed(
                        chicken.AllowedBiomes,
                        AnimalBiomeMask.Grassland),
                    Is.True);
                Assert.That(
                    AnimalSpeciesCatalogLookup.IsBiomeAllowed(
                        chicken.AllowedBiomes,
                        AnimalBiomeMask.Forest),
                    Is.False);
                Assert.That(
                    AnimalSpeciesCatalogLookup.IsGroundAllowed(
                        chicken.AllowedGrounds,
                        AnimalGroundMask.Land),
                    Is.True);
                Assert.That(
                    AnimalSpeciesCatalogLookup.IsGroundAllowed(
                        chicken.AllowedGrounds,
                        AnimalGroundMask.Water),
                    Is.False);
            }
            finally
            {
                if (animalCatalog.IsCreated)
                {
                    animalCatalog.Dispose();
                }

                definitionCatalog.Dispose();
            }
        }

        /// <summary>
        /// 共通定義と動物設定の入力順を逆転しても、実行時ID昇順の同じカタログになることを保証します。
        /// </summary>
        [Test]
        public void TryBuild_SortsSpeciesByResolvedRuntimeId()
        {
            DefinitionGuid chickenGuid = CreateGuid(0x20);
            DefinitionGuid secondSpeciesGuid = CreateGuid(0x30);
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog =
                CreateDefinitionCatalog(
                    CreateDefinition(secondSpeciesGuid, DefinitionKind.AnimalSpecies),
                    CreateDefinition(chickenGuid, DefinitionKind.AnimalSpecies));
            BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog = default;
            try
            {
                AnimalSpeciesDefinitionBuildEntry secondSpecies = CreateChickenDefinition();
                secondSpecies.DefinitionGuid = secondSpeciesGuid;
                AnimalSpeciesDefinitionBuildEntry chicken = CreateChickenDefinition();
                chicken.DefinitionGuid = chickenGuid;

                bool succeeded = AnimalSpeciesCatalogBuilder.TryBuild(
                    definitionCatalog,
                    new[] { secondSpecies, chicken },
                    out animalCatalog,
                    out AnimalSpeciesCatalogValidationError error);

                Assert.That(succeeded, Is.True);
                Assert.That(error.Code, Is.EqualTo(AnimalSpeciesCatalogValidationErrorCode.None));
                Assert.That(animalCatalog.Value.Species.Length, Is.EqualTo(2));
                Assert.That(
                    animalCatalog.Value.Species[0].SpeciesRuntimeId.Value,
                    Is.LessThan(animalCatalog.Value.Species[1].SpeciesRuntimeId.Value));
            }
            finally
            {
                if (animalCatalog.IsCreated)
                {
                    animalCatalog.Dispose();
                }

                definitionCatalog.Dispose();
            }
        }

        /// <summary>
        /// 共通定義に存在しないGUIDを拒否し、別カタログのRuntimeIdへ偶然接続しないことを保証します。
        /// </summary>
        [Test]
        public void TryBuild_RejectsUnknownDefinitionGuid()
        {
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog =
                CreateDefinitionCatalog(
                    CreateDefinition(CreateGuid(0x40), DefinitionKind.AnimalSpecies));
            try
            {
                AnimalSpeciesDefinitionBuildEntry chicken = CreateChickenDefinition();
                chicken.DefinitionGuid = CreateGuid(0x41);

                bool succeeded = AnimalSpeciesCatalogBuilder.TryBuild(
                    definitionCatalog,
                    new[] { chicken },
                    out BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog,
                    out AnimalSpeciesCatalogValidationError error);

                Assert.That(succeeded, Is.False);
                Assert.That(animalCatalog.IsCreated, Is.False);
                Assert.That(
                    error.Code,
                    Is.EqualTo(AnimalSpeciesCatalogValidationErrorCode.DefinitionWasNotFound));
                Assert.That(error.DefinitionIndex, Is.EqualTo(0));
            }
            finally
            {
                definitionCatalog.Dispose();
            }
        }

        /// <summary>
        /// GUIDが存在してもItem分類なら拒否し、品目を動物種として誤解釈しないことを保証します。
        /// </summary>
        [Test]
        public void TryBuild_RejectsDefinitionKindMismatch()
        {
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog =
                CreateDefinitionCatalog(
                    CreateDefinition(CreateGuid(ChickenGuidLow), DefinitionKind.Item));
            try
            {
                bool succeeded = AnimalSpeciesCatalogBuilder.TryBuild(
                    definitionCatalog,
                    new[] { CreateChickenDefinition() },
                    out BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog,
                    out AnimalSpeciesCatalogValidationError error);

                Assert.That(succeeded, Is.False);
                Assert.That(animalCatalog.IsCreated, Is.False);
                Assert.That(
                    error.Code,
                    Is.EqualTo(AnimalSpeciesCatalogValidationErrorCode.DefinitionKindMismatch));
            }
            finally
            {
                definitionCatalog.Dispose();
            }
        }

        /// <summary>
        /// 共通カタログに登録した動物種の固有設定が欠けている場合、空の既定値で続行しないことを保証します。
        /// </summary>
        [Test]
        public void TryBuild_RejectsMissingAnimalSpeciesSettings()
        {
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog =
                CreateDefinitionCatalog(
                    CreateDefinition(CreateGuid(ChickenGuidLow), DefinitionKind.AnimalSpecies));
            try
            {
                bool succeeded = AnimalSpeciesCatalogBuilder.TryBuild(
                    definitionCatalog,
                    System.Array.Empty<AnimalSpeciesDefinitionBuildEntry>(),
                    out BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog,
                    out AnimalSpeciesCatalogValidationError error);

                Assert.That(succeeded, Is.False);
                Assert.That(animalCatalog.IsCreated, Is.False);
                Assert.That(
                    error.Code,
                    Is.EqualTo(AnimalSpeciesCatalogValidationErrorCode.DefinitionIsMissing));
                Assert.That(error.MissingRuntimeId.IsValid, Is.True);
            }
            finally
            {
                definitionCatalog.Dispose();
            }
        }

        /// <summary>
        /// 同じ動物種へ2件の設定を入力した場合、どちらかを暗黙採用せず重複位置を返すことを保証します。
        /// </summary>
        [Test]
        public void TryBuild_RejectsDuplicatedAnimalSpeciesSettings()
        {
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog =
                CreateDefinitionCatalog(
                    CreateDefinition(CreateGuid(ChickenGuidLow), DefinitionKind.AnimalSpecies));
            try
            {
                AnimalSpeciesDefinitionBuildEntry chicken = CreateChickenDefinition();
                bool succeeded = AnimalSpeciesCatalogBuilder.TryBuild(
                    definitionCatalog,
                    new[] { chicken, chicken },
                    out BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog,
                    out AnimalSpeciesCatalogValidationError error);

                Assert.That(succeeded, Is.False);
                Assert.That(animalCatalog.IsCreated, Is.False);
                Assert.That(
                    error.Code,
                    Is.EqualTo(AnimalSpeciesCatalogValidationErrorCode.DefinitionIsDuplicated));
                Assert.That(error.DefinitionIndex, Is.EqualTo(1));
                Assert.That(error.RelatedDefinitionIndex, Is.EqualTo(0));
            }
            finally
            {
                definitionCatalog.Dispose();
            }
        }

        /// <summary>
        /// 空または未定義bitを含むバイオーム・地形Maskを拒否し、生成可能位置が曖昧にならないことを保証します。
        /// </summary>
        /// <param name="invalidBiomeMask">検証する不正なバイオームMaskの数値です。</param>
        /// <param name="invalidGroundMask">検証する不正な基礎地形Maskの数値です。</param>
        /// <param name="expectedError">最初に検出すべきエラーです。</param>
        [TestCase(0, (byte)AnimalGroundMask.Land, AnimalSpeciesCatalogValidationErrorCode.AllowedBiomesAreInvalid)]
        [TestCase(128, (byte)AnimalGroundMask.Land, AnimalSpeciesCatalogValidationErrorCode.AllowedBiomesAreInvalid)]
        [TestCase((byte)AnimalBiomeMask.Grassland, 0, AnimalSpeciesCatalogValidationErrorCode.AllowedGroundsAreInvalid)]
        [TestCase((byte)AnimalBiomeMask.Grassland, 128, AnimalSpeciesCatalogValidationErrorCode.AllowedGroundsAreInvalid)]
        public void TryBuild_RejectsInvalidEligibilityMasks(
            byte invalidBiomeMask,
            byte invalidGroundMask,
            AnimalSpeciesCatalogValidationErrorCode expectedError)
        {
            AnimalSpeciesDefinitionBuildEntry chicken = CreateChickenDefinition();
            chicken.AllowedBiomes = (AnimalBiomeMask)invalidBiomeMask;
            chicken.AllowedGrounds = (AnimalGroundMask)invalidGroundMask;

            AssertRejectedDefinition(chicken, expectedError);
        }

        /// <summary>
        /// 群れ、密度、固定Tick移動、徘徊、外見差分の不正な境界値を、それぞれ固有の理由で拒否します。
        /// </summary>
        /// <param name="caseIndex">CreateChickenDefinitionへ適用する不正値の種類です。</param>
        /// <param name="expectedError">最初に検出すべきエラーです。</param>
        [TestCase(0, AnimalSpeciesCatalogValidationErrorCode.HerdSizeRangeIsInvalid)]
        [TestCase(1, AnimalSpeciesCatalogValidationErrorCode.HerdSizeRangeIsInvalid)]
        [TestCase(2, AnimalSpeciesCatalogValidationErrorCode.PopulationDensityIsInvalid)]
        [TestCase(3, AnimalSpeciesCatalogValidationErrorCode.IdleTickRangeIsInvalid)]
        [TestCase(4, AnimalSpeciesCatalogValidationErrorCode.MovementStepIntervalIsInvalid)]
        [TestCase(5, AnimalSpeciesCatalogValidationErrorCode.WanderDistanceIsInvalid)]
        [TestCase(6, AnimalSpeciesCatalogValidationErrorCode.AppearanceVariantCountIsInvalid)]
        public void TryBuild_RejectsInvalidPopulationAndMovementRanges(
            int caseIndex,
            AnimalSpeciesCatalogValidationErrorCode expectedError)
        {
            AnimalSpeciesDefinitionBuildEntry chicken = CreateChickenDefinition();
            switch (caseIndex)
            {
                case 0:
                    chicken.MinimumHerdSize = 0;
                    break;
                case 1:
                    chicken.MinimumHerdSize = 7;
                    chicken.MaximumHerdSize = 6;
                    break;
                case 2:
                    chicken.TargetPopulationPer4096EligibleTiles = 0;
                    break;
                case 3:
                    chicken.MinimumIdleTicks = 81;
                    chicken.MaximumIdleTicks = 80;
                    break;
                case 4:
                    chicken.MovementStepIntervalTicks = 0;
                    break;
                case 5:
                    chicken.MaximumWanderDistanceInTiles = 0;
                    break;
                case 6:
                    chicken.AppearanceVariantCount = 0;
                    break;
            }

            AssertRejectedDefinition(chicken, expectedError);
        }

        /// <summary>
        /// 複数bitや未定義bitを単一候補として渡しても許可判定がtrueにならないことを保証します。
        /// </summary>
        [Test]
        public void EligibilityLookup_RejectsCompositeAndUnknownCandidateBits()
        {
            Assert.That(
                AnimalSpeciesCatalogLookup.IsBiomeAllowed(
                    AnimalBiomeMask.All,
                    AnimalBiomeMask.Grassland | AnimalBiomeMask.Forest),
                Is.False);
            Assert.That(
                AnimalSpeciesCatalogLookup.IsBiomeAllowed(
                    AnimalBiomeMask.All,
                    (AnimalBiomeMask)128),
                Is.False);
            Assert.That(
                AnimalSpeciesCatalogLookup.IsGroundAllowed(
                    AnimalGroundMask.All,
                    AnimalGroundMask.Land | AnimalGroundMask.Water),
                Is.False);
            Assert.That(
                AnimalSpeciesCatalogLookup.IsGroundAllowed(
                    AnimalGroundMask.All,
                    (AnimalGroundMask)128),
                Is.False);
        }

        /// <summary>
        /// 同じ鶏GUIDでも密度または移動値が変われば、動物種の機能固有状態ハッシュが変わることを保証します。
        /// </summary>
        [Test]
        public void SpeciesStateHash_ChangesWhenChickenSettingsChange()
        {
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog =
                CreateDefinitionCatalog(
                    CreateDefinition(CreateGuid(ChickenGuidLow), DefinitionKind.AnimalSpecies));
            BlobAssetReference<AnimalSpeciesCatalogBlob> firstAnimalCatalog = default;
            BlobAssetReference<AnimalSpeciesCatalogBlob> changedAnimalCatalog = default;
            try
            {
                AnimalSpeciesDefinitionBuildEntry firstChicken = CreateChickenDefinition();
                AnimalSpeciesDefinitionBuildEntry changedChicken = CreateChickenDefinition();
                changedChicken.MovementStepIntervalTicks++;
                Assert.That(
                    AnimalSpeciesCatalogBuilder.TryBuild(
                        definitionCatalog,
                        new[] { firstChicken },
                        out firstAnimalCatalog,
                        out _),
                    Is.True);
                Assert.That(
                    AnimalSpeciesCatalogBuilder.TryBuild(
                        definitionCatalog,
                        new[] { changedChicken },
                        out changedAnimalCatalog,
                        out _),
                    Is.True);

                SimulationStateHash firstHash = ComputeSpeciesStateHash(
                    definitionCatalog,
                    firstAnimalCatalog);
                SimulationStateHash sameHash = ComputeSpeciesStateHash(
                    definitionCatalog,
                    firstAnimalCatalog);
                SimulationStateHash changedHash = ComputeSpeciesStateHash(
                    definitionCatalog,
                    changedAnimalCatalog);

                Assert.That(firstHash, Is.EqualTo(sameHash));
                Assert.That(firstHash, Is.Not.EqualTo(changedHash));
            }
            finally
            {
                if (firstAnimalCatalog.IsCreated)
                {
                    firstAnimalCatalog.Dispose();
                }

                if (changedAnimalCatalog.IsCreated)
                {
                    changedAnimalCatalog.Dispose();
                }

                definitionCatalog.Dispose();
            }
        }

        /// <summary>
        /// 動物種カタログSingletonがないWorldを拒否し、空カタログとして正式な比較値を返さないことを保証します。
        /// </summary>
        [Test]
        public void SpeciesStateHash_RejectsMissingAnimalCatalogSingleton()
        {
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog =
                CreateDefinitionCatalog(
                    CreateDefinition(CreateGuid(ChickenGuidLow), DefinitionKind.AnimalSpecies));
            using World world = new World("Animal Species Hash Missing Catalog Test");
            try
            {
                Entity definitionEntity = world.EntityManager.CreateEntity();
                world.EntityManager.AddComponentData(
                    definitionEntity,
                    new DefinitionCatalog
                    {
                        Value = definitionCatalog
                    });
                SimulationStateHashBuilder builder = new SimulationStateHashBuilder(
                    SimulationStateHashSchema.CurrentVersion);

                bool succeeded = AnimalSpeciesStateHasher.TryAppendTo(
                    world.EntityManager,
                    ref builder,
                    out AnimalSpeciesStateHashError error);

                Assert.That(succeeded, Is.False);
                Assert.That(
                    error.Code,
                    Is.EqualTo(AnimalSpeciesStateHashErrorCode.AnimalCatalogIsMissing));
            }
            finally
            {
                definitionCatalog.Dispose();
            }
        }

        /// <summary>
        /// 指定した動物種設定が期待理由で拒否され、失敗時Blob Assetが作成されないことを共通確認します。
        /// </summary>
        /// <param name="definition">1箇所を不正にした動物種設定です。</param>
        /// <param name="expectedError">期待する最初の検証エラーです。</param>
        private static void AssertRejectedDefinition(
            AnimalSpeciesDefinitionBuildEntry definition,
            AnimalSpeciesCatalogValidationErrorCode expectedError)
        {
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog =
                CreateDefinitionCatalog(
                    CreateDefinition(CreateGuid(ChickenGuidLow), DefinitionKind.AnimalSpecies));
            try
            {
                bool succeeded = AnimalSpeciesCatalogBuilder.TryBuild(
                    definitionCatalog,
                    new[] { definition },
                    out BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog,
                    out AnimalSpeciesCatalogValidationError error);

                Assert.That(succeeded, Is.False);
                Assert.That(animalCatalog.IsCreated, Is.False);
                Assert.That(error.Code, Is.EqualTo(expectedError));
                Assert.That(error.DefinitionIndex, Is.EqualTo(0));
            }
            finally
            {
                definitionCatalog.Dispose();
            }
        }

        /// <summary>
        /// 2つの定義Blobを一時WorldのSingletonへ設定し、動物種セクションだけの比較用ハッシュを作ります。
        /// </summary>
        /// <param name="definitionCatalog">共通DefinitionCatalogです。</param>
        /// <param name="animalCatalog">対応するAnimalSpeciesCatalogです。</param>
        /// <returns>動物種の全静的値を固定順で含む状態ハッシュです。</returns>
        private static SimulationStateHash ComputeSpeciesStateHash(
            BlobAssetReference<DefinitionCatalogBlob> definitionCatalog,
            BlobAssetReference<AnimalSpeciesCatalogBlob> animalCatalog)
        {
            using World world = new World("Animal Species Hash Test");
            Entity catalogEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(
                catalogEntity,
                new DefinitionCatalog
                {
                    Value = definitionCatalog
                });
            world.EntityManager.AddComponentData(
                catalogEntity,
                new AnimalSpeciesCatalog
                {
                    Value = animalCatalog
                });
            SimulationStateHashBuilder builder = new SimulationStateHashBuilder(
                SimulationStateHashSchema.CurrentVersion);

            bool succeeded = AnimalSpeciesStateHasher.TryAppendTo(
                world.EntityManager,
                ref builder,
                out AnimalSpeciesStateHashError error);
            Assert.That(
                succeeded,
                Is.True,
                $"動物種状態ハッシュの構築に失敗しました: {error.Code}");
            return builder.Complete();
        }

        /// <summary>
        /// 鶏のANIMAL-01A開発用初期値を持つ構築入力を作ります。
        /// </summary>
        /// <returns>草原の陸地に3〜6羽の群れで出現する鶏設定です。</returns>
        private static AnimalSpeciesDefinitionBuildEntry CreateChickenDefinition()
        {
            return new AnimalSpeciesDefinitionBuildEntry
            {
                DefinitionGuid = CreateGuid(ChickenGuidLow),
                AllowedBiomes = AnimalBiomeMask.Grassland,
                AllowedGrounds = AnimalGroundMask.Land,
                MinimumHerdSize = 3,
                MaximumHerdSize = 6,
                TargetPopulationPer4096EligibleTiles = 8,
                MinimumIdleTicks = 20,
                MaximumIdleTicks = 80,
                MovementStepIntervalTicks = 5,
                MaximumWanderDistanceInTiles = 8,
                AppearanceVariantCount = 3
            };
        }

        /// <summary>
        /// 動物種Builderの前提となる共通DefinitionCatalogを作成し、失敗時はテストを即座に失敗させます。
        /// </summary>
        /// <param name="definitions">共通カタログへ登録する定義です。</param>
        /// <returns>呼び出し側が必ずDisposeする検証済みBlob Assetです。</returns>
        private static BlobAssetReference<DefinitionCatalogBlob> CreateDefinitionCatalog(
            params DefinitionBuildEntry[] definitions)
        {
            bool succeeded = DefinitionCatalogBuilder.TryBuild(
                definitions,
                System.Array.Empty<DefinitionBuildReference>(),
                out BlobAssetReference<DefinitionCatalogBlob> catalog,
                out DefinitionCatalogValidationError error);
            Assert.That(
                succeeded,
                Is.True,
                $"テスト準備用の共通定義カタログ構築に失敗しました: {error.Code}");
            return catalog;
        }

        /// <summary>
        /// 参照を持たない最小の共通定義入力を作ります。
        /// </summary>
        /// <param name="guid">定義へ割り当てる有効GUIDです。</param>
        /// <param name="kind">定義分類です。</param>
        /// <returns>参照範囲が空の共通定義入力です。</returns>
        private static DefinitionBuildEntry CreateDefinition(
            DefinitionGuid guid,
            DefinitionKind kind)
        {
            return new DefinitionBuildEntry
            {
                Guid = guid,
                Kind = kind,
                ReferenceStartIndex = 0,
                ReferenceCount = 0
            };
        }

        /// <summary>
        /// テストで読みやすいよう、下位64bitだけが異なる有効GUIDを作成します。
        /// </summary>
        /// <param name="low">GUID順を決める1以上の下位64bit値です。</param>
        /// <returns>上位64bitが0、下位64bitが指定値のDefinitionGuidです。</returns>
        private static DefinitionGuid CreateGuid(ulong low)
        {
            return new DefinitionGuid(0, low);
        }
    }
}
