// このファイルは、41地形材質の製品表示カタログ、Runtime ID解決、決定論的Sprite差分選択を検証します。
// Sceneや既存の暫定表示を使わず、C6-Dの製品Presenterが依存する入力契約をEditModeの純粋な準備データで保証します。

using System;
using System.Collections.Generic;
using Farmware.Simulation;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 41材質カタログの完全性、用途別Runtime ID、可変長Sprite候補の安定選択を保証するEditModeテストです。
    /// </summary>
    public sealed class TerrainMaterialPresentationCatalogTests
    {
        /// <summary>各テストで作成し、終了時に即時破棄する一時Unity Objectです。</summary>
        private readonly List<UnityEngine.Object> temporaryObjects =
            new List<UnityEngine.Object>();

        /// <summary>
        /// テスト用のSprite、Texture、ScriptableObjectを破棄し、別テストへUnity Objectを残しません。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            for (int objectIndex = temporaryObjects.Count - 1;
                 objectIndex >= 0;
                 objectIndex--)
            {
                UnityEngine.Object.DestroyImmediate(temporaryObjects[objectIndex]);
            }

            temporaryObjects.Clear();
        }

        /// <summary>
        /// 41材質を逆順登録しても完全性検証に成功し、安定IDから同じ行を取得できることを保証します。
        /// </summary>
        [Test]
        public void Catalog_AcceptsCompleteFortyOneMaterialsIndependentOfRegistrationOrder()
        {
            Sprite groundTop = CreateSprite("GroundTop");
            Sprite face = CreateSprite("Face");
            TerrainMaterialPresentationDefinition[] definitions =
                CreateCompleteDefinitions(groundTop, face);
            Array.Reverse(definitions);
            TerrainMaterialPresentationCatalog catalog = CreateCatalog(definitions);

            Assert.That(catalog.DefinitionCount, Is.EqualTo(41));
            Assert.That(catalog.TryInitialize(out string error), Is.True, error);
            for (byte stableValue = (byte)TerrainBlockMaterialDefinitions.First;
                 stableValue <= (byte)TerrainBlockMaterialDefinitions.Last;
                 stableValue++)
            {
                TerrainBlockMaterialId stableId = (TerrainBlockMaterialId)stableValue;
                Assert.That(
                    catalog.TryGetDefinition(stableId, out TerrainMaterialPresentationDefinition definition),
                    Is.True,
                    $"材質ID {stableId} の製品表示設定がありません。");
                Assert.That(definition.StableId, Is.EqualTo(stableId));
                Assert.That(definition.GetGroundTopSprite(0), Is.SameAs(groundTop));
                Assert.That(definition.GetFaceSprite(0), Is.SameAs(face));
            }
        }

        /// <summary>
        /// 41行あっても安定IDの重複と欠落があればカタログを公開しないことを保証します。
        /// </summary>
        [Test]
        public void Catalog_RejectsDuplicateStableIdAndMissingMaterial()
        {
            Sprite groundTop = CreateSprite("GroundTop");
            Sprite face = CreateSprite("Face");
            TerrainMaterialPresentationDefinition[] definitions =
                CreateCompleteDefinitions(groundTop, face);
            definitions[1] = CreateDefinition(
                TerrainBlockMaterialId.GrassLoam,
                groundTop,
                face);
            TerrainMaterialPresentationCatalog catalog = CreateCatalog(definitions);

            Assert.That(catalog.TryInitialize(out string error), Is.False);
            StringAssert.Contains("重複", error);
            Assert.That(
                catalog.TryGetDefinition(TerrainBlockMaterialId.MeadowSoil, out _),
                Is.False);
        }

        /// <summary>
        /// 必須候補の空、同一配列内の重複、寸法違い、PPU違い、範囲外暗色化係数を拒否します。
        /// </summary>
        [Test]
        public void Definition_RejectsInvalidRequiredSpriteAndDarkenSettings()
        {
            Sprite valid = CreateSprite("Valid");
            Sprite wrongSize = CreateSprite("WrongSize", width: 8, height: 16);
            Sprite wrongPixelsPerUnit = CreateSprite("WrongPpu", pixelsPerUnit: 32f);

            TerrainMaterialPresentationDefinition emptyGround =
                new TerrainMaterialPresentationDefinition(
                    TerrainBlockMaterialId.GrassLoam,
                    Array.Empty<Sprite>(),
                    new[] { valid },
                    0,
                    Array.Empty<Sprite>(),
                    Array.Empty<Sprite>(),
                    true,
                    0.85f);
            Assert.That(emptyGround.TryValidate(out _), Is.False);

            TerrainMaterialPresentationDefinition duplicateFace =
                new TerrainMaterialPresentationDefinition(
                    TerrainBlockMaterialId.GrassLoam,
                    new[] { valid },
                    new[] { valid, valid },
                    0,
                    Array.Empty<Sprite>(),
                    Array.Empty<Sprite>(),
                    true,
                    0.85f);
            Assert.That(duplicateFace.TryValidate(out string duplicateError), Is.False);
            StringAssert.Contains("重複", duplicateError);

            TerrainMaterialPresentationDefinition nullFace =
                new TerrainMaterialPresentationDefinition(
                    TerrainBlockMaterialId.GrassLoam,
                    new[] { valid },
                    new Sprite[] { null },
                    0,
                    Array.Empty<Sprite>(),
                    Array.Empty<Sprite>(),
                    true,
                    0.85f);
            Assert.That(nullFace.TryValidate(out string nullError), Is.False);
            StringAssert.Contains("未設定", nullError);

            Assert.That(
                CreateDefinition(
                    TerrainBlockMaterialId.GrassLoam,
                    wrongSize,
                    valid).TryValidate(out _),
                Is.False);
            Assert.That(
                CreateDefinition(
                    TerrainBlockMaterialId.GrassLoam,
                    valid,
                    wrongPixelsPerUnit).TryValidate(out _),
                Is.False);

            TerrainMaterialPresentationDefinition invalidFactor =
                new TerrainMaterialPresentationDefinition(
                    TerrainBlockMaterialId.GrassLoam,
                    new[] { valid },
                    new[] { valid },
                    0,
                    Array.Empty<Sprite>(),
                    Array.Empty<Sprite>(),
                    true,
                    1.01f);
            Assert.That(invalidFactor.TryValidate(out string factorError), Is.False);
            StringAssert.Contains("WallDarkenFactor", factorError);

            TerrainMaterialPresentationDefinition zeroFactor =
                new TerrainMaterialPresentationDefinition(
                    TerrainBlockMaterialId.GrassLoam,
                    new[] { valid },
                    new[] { valid },
                    0,
                    Array.Empty<Sprite>(),
                    Array.Empty<Sprite>(),
                    true,
                    0f);
            Assert.That(zeroFactor.TryValidate(out _), Is.False);

            TerrainMaterialPresentationDefinition invalidStableId =
                CreateDefinition(TerrainBlockMaterialId.None, valid, valid);
            Assert.That(invalidStableId.TryValidate(out _), Is.False);
        }

        /// <summary>
        /// 境界なしは完成Spriteを持たず、境界ありはFace×境界のGround／Wall完成件数を要求することを保証します。
        /// </summary>
        [Test]
        public void Definition_ValidatesPrecomposedBoundaryCountsAndWallReuseRule()
        {
            Sprite groundTop = CreateSprite("GroundTop");
            Sprite faceA = CreateSprite("FaceA");
            Sprite faceB = CreateSprite("FaceB");
            Sprite groundA = CreateSprite("GroundA");
            Sprite groundB = CreateSprite("GroundB");
            Sprite groundC = CreateSprite("GroundC");
            Sprite groundD = CreateSprite("GroundD");
            Sprite wallA = CreateSprite("WallA");
            Sprite wallB = CreateSprite("WallB");
            Sprite wallC = CreateSprite("WallC");
            Sprite wallD = CreateSprite("WallD");

            TerrainMaterialPresentationDefinition withBoundary =
                new TerrainMaterialPresentationDefinition(
                    TerrainBlockMaterialId.GrassLoam,
                    new[] { groundTop },
                    new[] { faceA, faceB },
                    2,
                    new[] { groundA, groundB, groundC, groundD },
                    new[] { wallA, wallB, wallC, wallD },
                    true,
                    0.85f);
            Assert.That(withBoundary.TryValidate(out string validError), Is.True, validError);
            Assert.That(withBoundary.GetBoundaryFaceSprite(1, 0, false), Is.SameAs(groundC));
            Assert.That(withBoundary.GetBoundaryFaceSprite(1, 1, true), Is.SameAs(wallD));

            TerrainMaterialPresentationDefinition missingWallCombinations =
                new TerrainMaterialPresentationDefinition(
                    TerrainBlockMaterialId.GrassLoam,
                    new[] { groundTop },
                    new[] { faceA, faceB },
                    2,
                    new[] { groundA, groundB, groundC, groundD },
                    new[] { wallA },
                    true,
                    0.85f);
            Assert.That(missingWallCombinations.TryValidate(out _), Is.False);

            TerrainMaterialPresentationDefinition reuseGroundForWall =
                new TerrainMaterialPresentationDefinition(
                    TerrainBlockMaterialId.GrassLoam,
                    new[] { groundTop },
                    new[] { faceA, faceB },
                    2,
                    new[] { groundA, groundB, groundC, groundD },
                    Array.Empty<Sprite>(),
                    false,
                    1f);
            Assert.That(reuseGroundForWall.TryValidate(out string reuseError), Is.True, reuseError);
            Assert.That(reuseGroundForWall.GetBoundaryFaceSprite(0, 1, true), Is.SameAs(groundB));

            TerrainMaterialPresentationDefinition boundaryFreeWithGeneratedSprite =
                new TerrainMaterialPresentationDefinition(
                    TerrainBlockMaterialId.Basalt,
                    new[] { groundTop },
                    new[] { faceA },
                    0,
                    new[] { groundA },
                    Array.Empty<Sprite>(),
                    true,
                    0.85f);
            Assert.That(boundaryFreeWithGeneratedSprite.TryValidate(out _), Is.False);
        }

        /// <summary>
        /// 登録順に依存せず全41材質のGround／Wall Runtime IDを解決し、役割をまたぐ代替をしないことを保証します。
        /// </summary>
        [Test]
        public void RuntimeLookup_ResolvesAllMaterialsWithoutGroundWallFallback()
        {
            TerrainBlockMaterialDefinitionBlob[] definitions =
                CreateRuntimeDefinitions();
            Array.Reverse(definitions);

            Assert.That(
                TerrainMaterialPresentationRuntimeLookup.TryCreate(
                    definitions,
                    out TerrainMaterialPresentationRuntimeLookup lookup,
                    out string error),
                Is.True,
                error);

            for (byte stableValue = (byte)TerrainBlockMaterialDefinitions.First;
                 stableValue <= (byte)TerrainBlockMaterialDefinitions.Last;
                 stableValue++)
            {
                int stableIndex = stableValue - (byte)TerrainBlockMaterialDefinitions.First;
                TerrainBlockMaterialId stableId = (TerrainBlockMaterialId)stableValue;
                DefinitionRuntimeId groundRuntimeId =
                    new DefinitionRuntimeId(1000 + (stableIndex * 2));
                DefinitionRuntimeId wallRuntimeId =
                    new DefinitionRuntimeId(1001 + (stableIndex * 2));
                Assert.That(lookup.TryResolveGround(groundRuntimeId, out TerrainBlockMaterialId groundId), Is.True);
                Assert.That(groundId, Is.EqualTo(stableId));
                Assert.That(lookup.TryResolveWall(wallRuntimeId, out TerrainBlockMaterialId wallId), Is.True);
                Assert.That(wallId, Is.EqualTo(stableId));

                // 同じ材質でもGround IDをWall検索へ流用せず、破損したセルを別役割として描画しません。
                Assert.That(lookup.TryResolveWall(groundRuntimeId, out _), Is.False);
                Assert.That(lookup.TryResolveGround(wallRuntimeId, out _), Is.False);
            }
        }

        /// <summary>
        /// Runtime検索表が材質ID重複、未解決ID、Ground／Wall間を含む実行時ID重複を拒否します。
        /// </summary>
        [Test]
        public void RuntimeLookup_RejectsIncompleteOrDuplicateMappings()
        {
            TerrainBlockMaterialDefinitionBlob[] duplicateStableId =
                CreateRuntimeDefinitions();
            duplicateStableId[1].StableId = TerrainBlockMaterialId.GrassLoam;
            Assert.That(
                TerrainMaterialPresentationRuntimeLookup.TryCreate(
                    duplicateStableId,
                    out _,
                    out _),
                Is.False);

            TerrainBlockMaterialDefinitionBlob[] invalidGround =
                CreateRuntimeDefinitions();
            invalidGround[0].GroundRuntimeId = default;
            Assert.That(
                TerrainMaterialPresentationRuntimeLookup.TryCreate(
                    invalidGround,
                    out _,
                    out _),
                Is.False);

            TerrainBlockMaterialDefinitionBlob[] duplicateAcrossRoles =
                CreateRuntimeDefinitions();
            duplicateAcrossRoles[1].GroundRuntimeId =
                duplicateAcrossRoles[0].WallRuntimeId;
            Assert.That(
                TerrainMaterialPresentationRuntimeLookup.TryCreate(
                    duplicateAcrossRoles,
                    out _,
                    out string duplicateError),
                Is.False);
            StringAssert.Contains("重複", duplicateError);

            Assert.That(
                TerrainMaterialPresentationRuntimeLookup.TryCreate(
                    new TerrainBlockMaterialDefinitionBlob[40],
                    out _,
                    out _),
                Is.False);
        }

        /// <summary>
        /// 負座標を含む同じ入力は同じ添字を返し、全用途の結果が候補範囲内に収まることを保証します。
        /// </summary>
        [Test]
        public void VariantSelector_IsRepeatableAndStaysInsideVariableLengthArrays()
        {
            const ulong WorldSeed = 0x123456789ABCDEF0UL;
            var coordinate = new WorldTileCoordinate(WorldLayer.DeepSea, -12345, -67890);

            Assert.That(
                TerrainMaterialSpriteVariantSelector.TrySelectGroundTopIndex(
                    WorldSeed,
                    coordinate,
                    TerrainBlockMaterialId.Peridotite,
                    7,
                    out int firstGround),
                Is.True);
            Assert.That(
                TerrainMaterialSpriteVariantSelector.TrySelectGroundTopIndex(
                    WorldSeed,
                    coordinate,
                    TerrainBlockMaterialId.Peridotite,
                    7,
                    out int secondGround),
                Is.True);
            Assert.That(secondGround, Is.EqualTo(firstGround));
            Assert.That(firstGround, Is.InRange(0, 6));

            Assert.That(
                TerrainMaterialSpriteVariantSelector.TrySelectFaceIndex(
                    WorldSeed,
                    coordinate,
                    TerrainBlockMaterialId.Peridotite,
                    5,
                    11,
                    out int face),
                Is.True);
            Assert.That(face, Is.InRange(0, 10));
            Assert.That(
                TerrainMaterialSpriteVariantSelector.TrySelectBoundaryIndex(
                    WorldSeed,
                    coordinate,
                    TerrainBlockMaterialId.Peridotite,
                    5,
                    3,
                    out int boundary),
                Is.True);
            Assert.That(boundary, Is.InRange(0, 2));
        }

        /// <summary>
        /// レイヤー、材質、断面段、用途別Saltが候補列へ反映され、無効入力を既定候補へ丸めないことを保証します。
        /// </summary>
        [Test]
        public void VariantSelector_SeparatesStableInputsAndRejectsInvalidValues()
        {
            const ulong WorldSeed = 123456789UL;
            const int SampleCount = 64;
            var groundSequence = new int[SampleCount];
            var faceSequence = new int[SampleCount];
            var otherLayerSequence = new int[SampleCount];
            var otherMaterialSequence = new int[SampleCount];
            var otherDepthSequence = new int[SampleCount];
            var otherSeedSequence = new int[SampleCount];
            var shiftedCoordinateSequence = new int[SampleCount];

            for (int sampleIndex = 0; sampleIndex < SampleCount; sampleIndex++)
            {
                var surfaceCoordinate =
                    new WorldTileCoordinate(WorldLayer.Surface, sampleIndex - 32, sampleIndex * 3);
                var underwaterCoordinate =
                    new WorldTileCoordinate(WorldLayer.Underwater, sampleIndex - 32, sampleIndex * 3);
                Assert.That(
                    TerrainMaterialSpriteVariantSelector.TrySelectGroundTopIndex(
                        WorldSeed,
                        surfaceCoordinate,
                        TerrainBlockMaterialId.GrassLoam,
                        97,
                        out groundSequence[sampleIndex]),
                    Is.True);
                Assert.That(
                    TerrainMaterialSpriteVariantSelector.TrySelectFaceIndex(
                        WorldSeed,
                        surfaceCoordinate,
                        TerrainBlockMaterialId.GrassLoam,
                        0,
                        97,
                        out faceSequence[sampleIndex]),
                    Is.True);
                Assert.That(
                    TerrainMaterialSpriteVariantSelector.TrySelectGroundTopIndex(
                        WorldSeed,
                        underwaterCoordinate,
                        TerrainBlockMaterialId.GrassLoam,
                        97,
                        out otherLayerSequence[sampleIndex]),
                    Is.True);
                Assert.That(
                    TerrainMaterialSpriteVariantSelector.TrySelectGroundTopIndex(
                        WorldSeed,
                        surfaceCoordinate,
                        TerrainBlockMaterialId.Basalt,
                        97,
                        out otherMaterialSequence[sampleIndex]),
                    Is.True);
                Assert.That(
                    TerrainMaterialSpriteVariantSelector.TrySelectFaceIndex(
                        WorldSeed,
                        surfaceCoordinate,
                        TerrainBlockMaterialId.GrassLoam,
                        1,
                        97,
                        out otherDepthSequence[sampleIndex]),
                    Is.True);
                Assert.That(
                    TerrainMaterialSpriteVariantSelector.TrySelectGroundTopIndex(
                        WorldSeed + 1UL,
                        surfaceCoordinate,
                        TerrainBlockMaterialId.GrassLoam,
                        97,
                        out otherSeedSequence[sampleIndex]),
                    Is.True);
                Assert.That(
                    TerrainMaterialSpriteVariantSelector.TrySelectGroundTopIndex(
                        WorldSeed,
                        new WorldTileCoordinate(
                            WorldLayer.Surface,
                            surfaceCoordinate.X + 1,
                            surfaceCoordinate.Y),
                        TerrainBlockMaterialId.GrassLoam,
                        97,
                        out shiftedCoordinateSequence[sampleIndex]),
                    Is.True);
            }

            Assert.That(TerrainMaterialSpriteVariantSelector.SelectionVersion, Is.EqualTo(1));
            CollectionAssert.AreNotEqual(groundSequence, faceSequence);
            CollectionAssert.AreNotEqual(groundSequence, otherLayerSequence);
            CollectionAssert.AreNotEqual(groundSequence, otherMaterialSequence);
            CollectionAssert.AreNotEqual(faceSequence, otherDepthSequence);
            CollectionAssert.AreNotEqual(groundSequence, otherSeedSequence);
            CollectionAssert.AreNotEqual(groundSequence, shiftedCoordinateSequence);

            var validCoordinate = new WorldTileCoordinate(WorldLayer.Surface, 0, 0);
            Assert.That(
                TerrainMaterialSpriteVariantSelector.TrySelectGroundTopIndex(
                    WorldSeed,
                    new WorldTileCoordinate((WorldLayer)99, 0, 0),
                    TerrainBlockMaterialId.GrassLoam,
                    1,
                    out int invalidLayerIndex),
                Is.False);
            Assert.That(invalidLayerIndex, Is.EqualTo(-1));
            Assert.That(
                TerrainMaterialSpriteVariantSelector.TrySelectGroundTopIndex(
                    WorldSeed,
                    validCoordinate,
                    TerrainBlockMaterialId.None,
                    1,
                    out _),
                Is.False);
            Assert.That(
                TerrainMaterialSpriteVariantSelector.TrySelectFaceIndex(
                    WorldSeed,
                    validCoordinate,
                    TerrainBlockMaterialId.GrassLoam,
                    -1,
                    1,
                    out _),
                Is.False);
            Assert.That(
                TerrainMaterialSpriteVariantSelector.TrySelectGroundTopIndex(
                    WorldSeed,
                    validCoordinate,
                    TerrainBlockMaterialId.GrassLoam,
                    0,
                    out _),
                Is.False);
        }

        /// <summary>
        /// Face優先・境界後続の事前合成配列添字が、生成ツールとRuntimeで同じになることを保証します。
        /// </summary>
        [Test]
        public void ComposedBoundaryIndex_UsesFaceMajorOrdering()
        {
            Assert.That(
                TerrainMaterialSpriteVariantSelector.CalculateComposedBoundaryIndex(0, 0, 3),
                Is.EqualTo(0));
            Assert.That(
                TerrainMaterialSpriteVariantSelector.CalculateComposedBoundaryIndex(0, 2, 3),
                Is.EqualTo(2));
            Assert.That(
                TerrainMaterialSpriteVariantSelector.CalculateComposedBoundaryIndex(1, 0, 3),
                Is.EqualTo(3));
            Assert.That(
                TerrainMaterialSpriteVariantSelector.CalculateComposedBoundaryIndex(2, 2, 3),
                Is.EqualTo(8));
        }

        /// <summary>
        /// 全41安定IDについて、境界なしの最小有効表示設定を作ります。
        /// </summary>
        /// <param name="groundTop">全行で共有する有効なテスト用上面Spriteです。</param>
        /// <param name="face">全行で共有する有効なテスト用断面Spriteです。</param>
        /// <returns>安定ID昇順の41材質設定です。</returns>
        private static TerrainMaterialPresentationDefinition[] CreateCompleteDefinitions(
            Sprite groundTop,
            Sprite face)
        {
            var definitions =
                new TerrainMaterialPresentationDefinition[TerrainBlockMaterialDefinitions.Count];
            for (int materialIndex = 0;
                 materialIndex < definitions.Length;
                 materialIndex++)
            {
                TerrainBlockMaterialId stableId = (TerrainBlockMaterialId)(
                    materialIndex + (int)TerrainBlockMaterialDefinitions.First);
                definitions[materialIndex] = CreateDefinition(stableId, groundTop, face);
            }

            return definitions;
        }

        /// <summary>
        /// 境界なしの最小有効な1材質表示設定を作ります。
        /// </summary>
        /// <param name="stableId">1〜41の安定材質IDです。</param>
        /// <param name="groundTop">16×16 px・16 PPUの上面Spriteです。</param>
        /// <param name="face">16×16 px・16 PPUの断面Spriteです。</param>
        /// <returns>既定0.85暗色化を持つ表示設定です。</returns>
        private static TerrainMaterialPresentationDefinition CreateDefinition(
            TerrainBlockMaterialId stableId,
            Sprite groundTop,
            Sprite face)
        {
            return new TerrainMaterialPresentationDefinition(
                stableId,
                new[] { groundTop },
                new[] { face },
                0,
                Array.Empty<Sprite>(),
                Array.Empty<Sprite>(),
                true,
                0.85f);
        }

        /// <summary>
        /// 全41材質へ一意なGround／Wall Runtime IDを割り当てた検証入力を作ります。
        /// </summary>
        /// <returns>安定ID昇順の41件です。</returns>
        private static TerrainBlockMaterialDefinitionBlob[] CreateRuntimeDefinitions()
        {
            var definitions =
                new TerrainBlockMaterialDefinitionBlob[TerrainBlockMaterialDefinitions.Count];
            for (int materialIndex = 0;
                 materialIndex < definitions.Length;
                 materialIndex++)
            {
                TerrainBlockMaterialId stableId = (TerrainBlockMaterialId)(
                    materialIndex + (int)TerrainBlockMaterialDefinitions.First);
                definitions[materialIndex] = new TerrainBlockMaterialDefinitionBlob
                {
                    StableId = stableId,
                    ItemRuntimeId = new DefinitionRuntimeId(500 + materialIndex),
                    GroundRuntimeId = new DefinitionRuntimeId(1000 + (materialIndex * 2)),
                    WallRuntimeId = new DefinitionRuntimeId(1001 + (materialIndex * 2))
                };
            }

            return definitions;
        }

        /// <summary>
        /// 一時ScriptableObjectへ41材質配列を設定し、終了時の破棄対象へ登録します。
        /// </summary>
        /// <param name="definitions">Inspector相当の表示設定配列です。</param>
        /// <returns>未検証状態の一時カタログです。</returns>
        private TerrainMaterialPresentationCatalog CreateCatalog(
            TerrainMaterialPresentationDefinition[] definitions)
        {
            TerrainMaterialPresentationCatalog catalog =
                ScriptableObject.CreateInstance<TerrainMaterialPresentationCatalog>();
            catalog.SetDefinitions(definitions);
            temporaryObjects.Add(catalog);
            return catalog;
        }

        /// <summary>
        /// 指定寸法とPPUの一時Texture／Spriteを作り、終了時の破棄対象へ登録します。
        /// </summary>
        /// <param name="name">失敗時に候補を識別するObject名です。</param>
        /// <param name="width">TextureとSprite矩形の幅pxです。</param>
        /// <param name="height">TextureとSprite矩形の高さpxです。</param>
        /// <param name="pixelsPerUnit">Spriteの1 Unity unitあたりピクセル数です。</param>
        /// <returns>指定条件を持つ一時Spriteです。</returns>
        private Sprite CreateSprite(
            string name,
            int width = 16,
            int height = 16,
            float pixelsPerUnit = 16f)
        {
            var texture = new Texture2D(width, height)
            {
                name = $"{name}Texture"
            };
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit);
            sprite.name = name;
            temporaryObjects.Add(sprite);
            temporaryObjects.Add(texture);
            return sprite;
        }
    }
}
