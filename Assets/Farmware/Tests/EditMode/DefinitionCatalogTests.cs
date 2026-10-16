// このファイルは、静的定義GUIDの解析、Blob Asset構築、実行時ID解決、異常入力の拒否を検証します。
// Sceneを使わず、DefinitionCatalogAuthoringのBakerと同じ共通Builderを直接実行します。

using Farmware.Simulation;
using NUnit.Framework;
using Unity.Entities;

namespace Farmware.Tests.EditMode
{
    /// <summary>
    /// SIM-01Dで追加した定義GUID、連続実行時ID、参照解決、Bake前検証を保証するEditModeテスト群です。
    /// 各テストは作成したPersistent Blob Assetを必ずDisposeし、テスト間へNativeメモリを残しません。
    /// </summary>
    public sealed class DefinitionCatalogTests
    {
        /// <summary>
        /// 32桁形式と一般的なハイフン付き形式が同じ128bit値になり、正規形へ戻せることを保証します。
        /// </summary>
        [Test]
        public void DefinitionGuid_ParsesSupportedFormatsAndReturnsCanonicalText()
        {
            const string compactGuid = "00112233445566778899aabbccddeeff";
            const string hyphenatedGuid = "00112233-4455-6677-8899-aabbccddeeff";

            bool compactSucceeded = DefinitionGuid.TryParse(
                compactGuid,
                out DefinitionGuid compactResult);
            bool hyphenatedSucceeded = DefinitionGuid.TryParse(
                hyphenatedGuid,
                out DefinitionGuid hyphenatedResult);

            Assert.That(compactSucceeded, Is.True);
            Assert.That(hyphenatedSucceeded, Is.True);
            Assert.That(compactResult, Is.EqualTo(hyphenatedResult));
            Assert.That(compactResult.ToString(), Is.EqualTo(compactGuid));
        }

        /// <summary>
        /// 不正な文字列と全0の予約値を定義GUIDとして受理しないことを保証します。
        /// </summary>
        [Test]
        public void DefinitionGuid_RejectsInvalidAndAllZeroValues()
        {
            Assert.That(
                DefinitionGuid.TryParse("not-a-guid", out _),
                Is.False);
            Assert.That(
                DefinitionGuid.TryParse(
                    "00000000000000000000000000000000",
                    out _),
                Is.False);
        }

        /// <summary>
        /// 定義GUIDが未設定なら構築を拒否し、部分的なBlob Assetを返さないことを保証します。
        /// </summary>
        [Test]
        public void TryBuild_RejectsUnsetDefinitionGuidWithoutCreatingCatalog()
        {
            DefinitionBuildEntry[] definitions =
            {
                CreateDefinition(
                    DefinitionGuid.Invalid,
                    DefinitionKind.Item,
                    referenceCount: 0)
            };

            bool succeeded = DefinitionCatalogBuilder.TryBuild(
                definitions,
                System.Array.Empty<DefinitionBuildReference>(),
                out BlobAssetReference<DefinitionCatalogBlob> catalog,
                out DefinitionCatalogValidationError error);

            Assert.That(succeeded, Is.False);
            Assert.That(catalog.IsCreated, Is.False);
            Assert.That(
                error.Code,
                Is.EqualTo(
                    DefinitionCatalogValidationErrorCode.DefinitionGuidIsInvalid));
            Assert.That(error.DefinitionIndex, Is.EqualTo(0));
        }

        /// <summary>
        /// 入力配列の順序に関係なくGUID昇順で連続IDが決まり、参照が整数IDへ解決されることを保証します。
        /// </summary>
        [Test]
        public void TryBuild_AssignsStableRuntimeIdsAndResolvesReferences()
        {
            DefinitionGuid itemGuid = CreateGuid(0x10);
            DefinitionGuid animalGuid = CreateGuid(0x20);
            DefinitionGuid recipeGuid = CreateGuid(0x30);
            DefinitionBuildEntry[] definitions =
            {
                new DefinitionBuildEntry
                {
                    Guid = recipeGuid,
                    Kind = DefinitionKind.Recipe,
                    ReferenceStartIndex = 0,
                    ReferenceCount = 1
                },
                new DefinitionBuildEntry
                {
                    Guid = animalGuid,
                    Kind = DefinitionKind.AnimalSpecies,
                    ReferenceStartIndex = 1,
                    ReferenceCount = 0
                },
                new DefinitionBuildEntry
                {
                    Guid = itemGuid,
                    Kind = DefinitionKind.Item,
                    ReferenceStartIndex = 1,
                    ReferenceCount = 0
                }
            };
            DefinitionBuildReference[] references =
            {
                new DefinitionBuildReference
                {
                    TargetGuid = itemGuid,
                    ExpectedKind = DefinitionKind.Item
                }
            };

            bool succeeded = DefinitionCatalogBuilder.TryBuild(
                definitions,
                references,
                out BlobAssetReference<DefinitionCatalogBlob> catalog,
                out DefinitionCatalogValidationError error);

            Assert.That(succeeded, Is.True, error.Code.ToString());
            try
            {
                Assert.That(
                    DefinitionCatalogLookup.TryResolve(
                        catalog,
                        itemGuid,
                        out DefinitionRuntimeId itemRuntimeId),
                    Is.True);
                Assert.That(itemRuntimeId.Value, Is.EqualTo(1));

                Assert.That(
                    DefinitionCatalogLookup.TryResolve(
                        catalog,
                        animalGuid,
                        out DefinitionRuntimeId animalRuntimeId),
                    Is.True);
                Assert.That(animalRuntimeId.Value, Is.EqualTo(2));

                Assert.That(
                    DefinitionCatalogLookup.TryResolve(
                        catalog,
                        recipeGuid,
                        out DefinitionRuntimeId recipeRuntimeId),
                    Is.True);
                Assert.That(recipeRuntimeId.Value, Is.EqualTo(3));

                // レシピのGUID参照はItemの連続IDへ変換され、実行中に文字列探索を必要としません。
                Assert.That(
                    DefinitionCatalogLookup.TryGetReference(
                        catalog,
                        recipeRuntimeId,
                        0,
                        out DefinitionRuntimeId targetRuntimeId),
                    Is.True);
                Assert.That(targetRuntimeId, Is.EqualTo(itemRuntimeId));
            }
            finally
            {
                catalog.Dispose();
            }
        }

        /// <summary>
        /// 同じGUIDを異なる分類へ設定しても重複として拒否し、破損Blob Assetを返さないことを保証します。
        /// </summary>
        [Test]
        public void TryBuild_RejectsDuplicatedGuidWithoutCreatingCatalog()
        {
            DefinitionGuid duplicatedGuid = CreateGuid(0x40);
            DefinitionBuildEntry[] definitions =
            {
                CreateDefinition(duplicatedGuid, DefinitionKind.Item, 0),
                CreateDefinition(duplicatedGuid, DefinitionKind.Equipment, 0)
            };

            bool succeeded = DefinitionCatalogBuilder.TryBuild(
                definitions,
                System.Array.Empty<DefinitionBuildReference>(),
                out BlobAssetReference<DefinitionCatalogBlob> catalog,
                out DefinitionCatalogValidationError error);

            Assert.That(succeeded, Is.False);
            Assert.That(catalog.IsCreated, Is.False);
            Assert.That(
                error.Code,
                Is.EqualTo(
                    DefinitionCatalogValidationErrorCode.DefinitionGuidIsDuplicated));
            Assert.That(error.DefinitionIndex, Is.EqualTo(1));
            Assert.That(error.RelatedDefinitionIndex, Is.EqualTo(0));
        }

        /// <summary>
        /// カタログに存在しないGUIDを参照した場合、参照切れとして構築全体を拒否することを保証します。
        /// </summary>
        [Test]
        public void TryBuild_RejectsMissingReferencedDefinition()
        {
            DefinitionBuildEntry[] definitions =
            {
                CreateDefinition(
                    CreateGuid(0x50),
                    DefinitionKind.Recipe,
                    referenceCount: 1)
            };
            DefinitionBuildReference[] references =
            {
                new DefinitionBuildReference
                {
                    TargetGuid = CreateGuid(0x51),
                    ExpectedKind = DefinitionKind.Item
                }
            };

            bool succeeded = DefinitionCatalogBuilder.TryBuild(
                definitions,
                references,
                out BlobAssetReference<DefinitionCatalogBlob> catalog,
                out DefinitionCatalogValidationError error);

            Assert.That(succeeded, Is.False);
            Assert.That(catalog.IsCreated, Is.False);
            Assert.That(
                error.Code,
                Is.EqualTo(
                    DefinitionCatalogValidationErrorCode.ReferencedDefinitionWasNotFound));
            Assert.That(error.DefinitionIndex, Is.EqualTo(0));
            Assert.That(error.ReferenceIndex, Is.EqualTo(0));
        }

        /// <summary>
        /// 参照先GUIDが未設定なら構築を拒否し、未解決IDを含むBlob Assetを返さないことを保証します。
        /// </summary>
        [Test]
        public void TryBuild_RejectsUnsetReferenceGuidWithoutCreatingCatalog()
        {
            DefinitionBuildEntry[] definitions =
            {
                CreateDefinition(
                    CreateGuid(0x52),
                    DefinitionKind.Recipe,
                    referenceCount: 1)
            };
            DefinitionBuildReference[] references =
            {
                new DefinitionBuildReference
                {
                    TargetGuid = DefinitionGuid.Invalid,
                    ExpectedKind = DefinitionKind.Item
                }
            };

            bool succeeded = DefinitionCatalogBuilder.TryBuild(
                definitions,
                references,
                out BlobAssetReference<DefinitionCatalogBlob> catalog,
                out DefinitionCatalogValidationError error);

            Assert.That(succeeded, Is.False);
            Assert.That(catalog.IsCreated, Is.False);
            Assert.That(
                error.Code,
                Is.EqualTo(
                    DefinitionCatalogValidationErrorCode.ReferenceGuidIsInvalid));
            Assert.That(error.DefinitionIndex, Is.EqualTo(0));
            Assert.That(error.ReferenceIndex, Is.EqualTo(0));
        }

        /// <summary>
        /// 参照GUIDが存在しても期待分類と実際の分類が違う場合、誤接続として構築を拒否することを保証します。
        /// </summary>
        [Test]
        public void TryBuild_RejectsReferencedDefinitionKindMismatch()
        {
            DefinitionGuid equipmentGuid = CreateGuid(0x60);
            DefinitionBuildEntry[] definitions =
            {
                CreateDefinition(
                    CreateGuid(0x61),
                    DefinitionKind.Recipe,
                    referenceCount: 1),
                CreateDefinition(
                    equipmentGuid,
                    DefinitionKind.Equipment,
                    referenceCount: 0,
                    referenceStartIndex: 1)
            };
            DefinitionBuildReference[] references =
            {
                new DefinitionBuildReference
                {
                    TargetGuid = equipmentGuid,
                    ExpectedKind = DefinitionKind.Item
                }
            };

            bool succeeded = DefinitionCatalogBuilder.TryBuild(
                definitions,
                references,
                out BlobAssetReference<DefinitionCatalogBlob> catalog,
                out DefinitionCatalogValidationError error);

            Assert.That(succeeded, Is.False);
            Assert.That(catalog.IsCreated, Is.False);
            Assert.That(
                error.Code,
                Is.EqualTo(
                    DefinitionCatalogValidationErrorCode.ReferencedDefinitionKindMismatch));
            Assert.That(error.DefinitionIndex, Is.EqualTo(0));
            Assert.That(error.RelatedDefinitionIndex, Is.EqualTo(1));
        }

        /// <summary>
        /// 定義の参照範囲に隙間がある入力を拒否し、未所有参照をBlob Assetへ紛れ込ませないことを保証します。
        /// </summary>
        [Test]
        public void TryBuild_RejectsNonContiguousReferenceRange()
        {
            DefinitionBuildEntry[] definitions =
            {
                CreateDefinition(
                    CreateGuid(0x70),
                    DefinitionKind.Item,
                    referenceCount: 0,
                    referenceStartIndex: 1)
            };
            DefinitionBuildReference[] references =
            {
                new DefinitionBuildReference
                {
                    TargetGuid = CreateGuid(0x70),
                    ExpectedKind = DefinitionKind.Item
                }
            };

            bool succeeded = DefinitionCatalogBuilder.TryBuild(
                definitions,
                references,
                out BlobAssetReference<DefinitionCatalogBlob> catalog,
                out DefinitionCatalogValidationError error);

            Assert.That(succeeded, Is.False);
            Assert.That(catalog.IsCreated, Is.False);
            Assert.That(
                error.Code,
                Is.EqualTo(
                    DefinitionCatalogValidationErrorCode.ReferenceRangeIsInvalid));
        }

        /// <summary>
        /// テストで読みやすいよう、下位64bitだけが異なる有効GUIDを作成します。
        /// </summary>
        /// <param name="low">GUID順と期待実行時IDを決める下位64bit値です。</param>
        /// <returns>上位64bitが0、下位64bitが1以上の有効GUIDです。</returns>
        private static DefinitionGuid CreateGuid(ulong low)
        {
            return new DefinitionGuid(0, low);
        }

        /// <summary>
        /// 参照範囲を明示した最小の定義入力を作成し、各テストの準備重複を減らします。
        /// </summary>
        /// <param name="guid">定義へ割り当てる有効GUIDです。</param>
        /// <param name="kind">定義分類です。</param>
        /// <param name="referenceCount">この定義が所有する参照件数です。</param>
        /// <param name="referenceStartIndex">連結参照配列内の開始添字です。</param>
        /// <returns>指定値を保持する構築入力です。</returns>
        private static DefinitionBuildEntry CreateDefinition(
            DefinitionGuid guid,
            DefinitionKind kind,
            int referenceCount,
            int referenceStartIndex = 0)
        {
            return new DefinitionBuildEntry
            {
                Guid = guid,
                Kind = kind,
                ReferenceStartIndex = referenceStartIndex,
                ReferenceCount = referenceCount
            };
        }
    }
}
