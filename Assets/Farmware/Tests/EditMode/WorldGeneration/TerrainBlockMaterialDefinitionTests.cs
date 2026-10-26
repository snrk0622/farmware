// このファイルは、初回41地形材質の安定ID、共通Item・Ground・Wall対応、共通カタログへの接続を検証します。
// 各テストは純粋なDefinition入力と一時Blobだけを使い、Sceneや表示実装へ依存せずC6-B1の契約を保証します。

using System;
using System.Collections.Generic;
using Farmware.Simulation;
using NUnit.Framework;
using Unity.Entities;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 41材質の完全性、三者GUIDの一意性、登録順非依存、回収Item接続を保証するEditModeテストです。
    /// </summary>
    public sealed class TerrainBlockMaterialDefinitionTests
    {
        /// <summary>各テストで作成し、終了時に破棄する共通Definition Blobです。</summary>
        private BlobAssetReference<DefinitionCatalogBlob> definitionCatalog;

        /// <summary>各テストで作成し、終了時に破棄する地形Definition Blobです。</summary>
        private BlobAssetReference<TerrainBlockCatalogBlob> terrainCatalog;

        /// <summary>各テストで作成し、終了時に破棄する41材質対応Blobです。</summary>
        private BlobAssetReference<TerrainBlockMaterialCatalogBlob> materialCatalog;

        /// <summary>
        /// テスト中に作成されたPersistent Blobをすべて解放し、後続テストへメモリを残しません。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (materialCatalog.IsCreated)
            {
                materialCatalog.Dispose();
            }

            if (terrainCatalog.IsCreated)
            {
                terrainCatalog.Dispose();
            }

            if (definitionCatalog.IsCreated)
            {
                definitionCatalog.Dispose();
            }
        }

        /// <summary>
        /// 仕様表の先頭から末尾まで41個の安定IDが連続し、予約値0を使用しないことを保証します。
        /// </summary>
        [Test]
        public void StableIds_MatchFortyOneMaterialContract()
        {
            Assert.That(TerrainBlockMaterialDefinitions.First,
                Is.EqualTo(TerrainBlockMaterialId.GrassLoam));
            Assert.That(TerrainBlockMaterialDefinitions.Last,
                Is.EqualTo(TerrainBlockMaterialId.Peridotite));
            Assert.That(TerrainBlockMaterialDefinitions.Count, Is.EqualTo(41));
            Assert.That((byte)TerrainBlockMaterialId.None, Is.Zero);
            Assert.That((byte)TerrainBlockMaterialId.GrassLoam, Is.EqualTo(1));
            Assert.That((byte)TerrainBlockMaterialId.Gravel, Is.EqualTo(16));
            Assert.That((byte)TerrainBlockMaterialId.Basalt, Is.EqualTo(25));
            Assert.That((byte)TerrainBlockMaterialId.Peridotite, Is.EqualTo(41));
        }

        /// <summary>
        /// 既定表が41行を持ち、全123 GUIDが有効かつ重複せず、各安定IDを検索できることを保証します。
        /// </summary>
        [Test]
        public void DefaultDefinitions_ContainUniqueItemGroundAndWallMappings()
        {
            TerrainBlockMaterialDefinition[] definitions =
                TerrainBlockMaterialDefinitions.CreateDefault();

            Assert.That(TerrainBlockMaterialDefinitions.Validate(definitions), Is.True);
            Assert.That(definitions.Length, Is.EqualTo(41));
            var guids = new HashSet<DefinitionGuid>();
            for (byte stableValue = (byte)TerrainBlockMaterialDefinitions.First;
                 stableValue <= (byte)TerrainBlockMaterialDefinitions.Last;
                 stableValue++)
            {
                TerrainBlockMaterialId stableId = (TerrainBlockMaterialId)stableValue;
                Assert.That(
                    TerrainBlockMaterialDefinitions.TryGetDefinition(
                        definitions,
                        stableId,
                        out TerrainBlockMaterialDefinition definition),
                    Is.True,
                    $"材質ID {stableId} の明示対応がありません。");
                Assert.That(definition.StableId, Is.EqualTo(stableId));
                Assert.That(guids.Add(definition.ItemGuid), Is.True);
                Assert.That(guids.Add(definition.GroundGuid), Is.True);
                Assert.That(guids.Add(definition.WallGuid), Is.True);
            }

            Assert.That(guids.Count, Is.EqualTo(123));
        }

        /// <summary>
        /// Item・Ground・Wallの共通Definition分類と、Ground・Wall双方の回収Itemが同じ材質Itemを指すことを保証します。
        /// </summary>
        [Test]
        public void BuiltInCatalogs_ResolveEveryMaterialAndSharedRecoveredItem()
        {
            BuildCatalogs(TerrainBlockMaterialDefinitions.CreateDefault());

            ref TerrainBlockMaterialCatalogBlob materialRoot =
                ref materialCatalog.Value;
            Assert.That(materialRoot.Definitions.Length, Is.EqualTo(41));
            for (byte stableValue = (byte)TerrainBlockMaterialDefinitions.First;
                 stableValue <= (byte)TerrainBlockMaterialDefinitions.Last;
                 stableValue++)
            {
                TerrainBlockMaterialId stableId = (TerrainBlockMaterialId)stableValue;
                Assert.That(
                    TerrainBlockMaterialCatalogLookup.TryGetDefinition(
                        materialCatalog,
                        stableId,
                        out TerrainBlockMaterialDefinitionBlob material),
                    Is.True,
                    $"材質ID {stableId} の実行時対応がありません。");
                AssertDefinitionKind(material.ItemRuntimeId, DefinitionKind.Item);
                AssertDefinitionKind(material.GroundRuntimeId, DefinitionKind.GroundBlock);
                AssertDefinitionKind(material.WallRuntimeId, DefinitionKind.WallBlock);

                Assert.That(
                    TerrainBlockCatalogLookup.TryGetDefinition(
                        terrainCatalog,
                        material.GroundRuntimeId,
                        out TerrainBlockDefinitionBlob ground),
                    Is.True);
                Assert.That(
                    TerrainBlockCatalogLookup.TryGetDefinition(
                        terrainCatalog,
                        material.WallRuntimeId,
                        out TerrainBlockDefinitionBlob wall),
                    Is.True);
                Assert.That(ground.RecoveredItemRuntimeId,
                    Is.EqualTo(material.ItemRuntimeId));
                Assert.That(wall.RecoveredItemRuntimeId,
                    Is.EqualTo(material.ItemRuntimeId));
                Assert.That(
                    wall.Flags & TerrainBlockDefinitionFlags.Watertight,
                    Is.Not.EqualTo(TerrainBlockDefinitionFlags.None));
                Assert.That(
                    wall.Flags & TerrainBlockDefinitionFlags.BlocksMovement,
                    Is.Not.EqualTo(TerrainBlockDefinitionFlags.None));
            }
        }

        /// <summary>
        /// 材質表の登録順だけを逆転しても、安定IDから同じ三者GUIDと実行時IDを取得できることを保証します。
        /// </summary>
        [Test]
        public void MaterialCatalog_DefinitionOrderDoesNotChangeResolvedMappings()
        {
            TerrainBlockMaterialDefinition[] reversed =
                TerrainBlockMaterialDefinitions.CreateDefault();
            Array.Reverse(reversed);

            BuildCatalogs(reversed);
            for (byte stableValue = (byte)TerrainBlockMaterialDefinitions.First;
                 stableValue <= (byte)TerrainBlockMaterialDefinitions.Last;
                 stableValue++)
            {
                TerrainBlockMaterialId stableId = (TerrainBlockMaterialId)stableValue;
                Assert.That(
                    TerrainBlockMaterialCatalogLookup.TryGetDefinition(
                        materialCatalog,
                        stableId,
                        out TerrainBlockMaterialDefinitionBlob resolved),
                    Is.True);
                Assert.That(resolved.StableId, Is.EqualTo(stableId));
            }
        }

        /// <summary>
        /// 安定ID重複、三者GUID重複、行欠落をそれぞれ拒否し、不完全な材質表を公開しないことを保証します。
        /// </summary>
        [Test]
        public void Validation_RejectsDuplicateIdDuplicateGuidAndMissingMaterial()
        {
            TerrainBlockMaterialDefinition[] duplicateId =
                TerrainBlockMaterialDefinitions.CreateDefault();
            TerrainBlockMaterialDefinition changed = duplicateId[1];
            changed.StableId = duplicateId[0].StableId;
            duplicateId[1] = changed;
            Assert.That(TerrainBlockMaterialDefinitions.Validate(duplicateId), Is.False);

            TerrainBlockMaterialDefinition[] duplicateGuid =
                TerrainBlockMaterialDefinitions.CreateDefault();
            changed = duplicateGuid[1];
            changed.ItemGuid = duplicateGuid[0].GroundGuid;
            duplicateGuid[1] = changed;
            Assert.That(TerrainBlockMaterialDefinitions.Validate(duplicateGuid), Is.False);

            TerrainBlockMaterialDefinition[] missing =
                new TerrainBlockMaterialDefinition[TerrainBlockMaterialDefinitions.Count - 1];
            Array.Copy(
                TerrainBlockMaterialDefinitions.CreateDefault(),
                missing,
                missing.Length);
            Assert.That(TerrainBlockMaterialDefinitions.Validate(missing), Is.False);
        }

        /// <summary>
        /// 予約ID、範囲外ID、未作成Blobを検索しても別材質へ丸めずfalseを返すことを保証します。
        /// </summary>
        [Test]
        public void Lookup_RejectsInvalidStableIdsAndMissingCatalog()
        {
            Assert.That(
                TerrainBlockMaterialCatalogLookup.TryGetDefinition(
                    default,
                    TerrainBlockMaterialId.GrassLoam,
                    out _),
                Is.False);

            BuildCatalogs(TerrainBlockMaterialDefinitions.CreateDefault());
            Assert.That(
                TerrainBlockMaterialCatalogLookup.TryGetDefinition(
                    materialCatalog,
                    TerrainBlockMaterialId.None,
                    out _),
                Is.False);
            Assert.That(
                TerrainBlockMaterialCatalogLookup.TryGetDefinition(
                    materialCatalog,
                    (TerrainBlockMaterialId)42,
                    out _),
                Is.False);
        }

        /// <summary>
        /// 組み込み共通定義、地形定義、材質対応Blobを本番Bakerと同じ順序で構築します。
        /// </summary>
        /// <param name="materials">材質カタログBuilderへ渡す41材質の入力です。</param>
        private void BuildCatalogs(TerrainBlockMaterialDefinition[] materials)
        {
            DefinitionBuildEntry[] commonDefinitions =
                BuiltInTerrainBlockDefinitions.CreateCommonDefinitions(
                    0,
                    out DefinitionBuildReference[] commonReferences);
            Assert.That(
                DefinitionCatalogBuilder.TryBuild(
                    commonDefinitions,
                    commonReferences,
                    out definitionCatalog,
                    out DefinitionCatalogValidationError definitionError),
                Is.True,
                $"共通Definitionを構築できませんでした: {definitionError.Code}");
            Assert.That(
                BuiltInTerrainBlockDefinitions.TryCreateTerrainDefinitions(
                    definitionCatalog,
                    out TerrainBlockDefinitionBuildEntry[] terrainDefinitions),
                Is.True,
                "41材質を含む地形入力を解決できませんでした。");
            Assert.That(
                TerrainBlockCatalogBuilder.TryBuild(
                    definitionCatalog,
                    terrainDefinitions,
                    out terrainCatalog),
                Is.True,
                "41材質を含む地形カタログを構築できませんでした。");
            Assert.That(
                TerrainBlockMaterialCatalogBuilder.TryBuild(
                    definitionCatalog,
                    materials,
                    out materialCatalog),
                Is.True,
                "41材質対応カタログを構築できませんでした。");
        }

        /// <summary>
        /// 共通Definition実行時IDが期待分類を指すことを確認します。
        /// </summary>
        /// <param name="runtimeId">材質対応Blobから取得した実行時IDです。</param>
        /// <param name="expectedKind">Item、GroundBlock、WallBlockの期待分類です。</param>
        private void AssertDefinitionKind(
            DefinitionRuntimeId runtimeId,
            DefinitionKind expectedKind)
        {
            Assert.That(
                DefinitionCatalogLookup.TryGetDefinition(
                    definitionCatalog,
                    runtimeId,
                    out DefinitionBlobRecord definition),
                Is.True);
            Assert.That(definition.Kind, Is.EqualTo(expectedKind));
        }
    }
}
