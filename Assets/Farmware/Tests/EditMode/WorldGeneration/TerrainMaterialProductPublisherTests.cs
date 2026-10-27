// このファイルは、今回の41材質画像が境界あり11材質として製品カタログとAtlasへ完全公開されていることを検証します。
// 制作元SourceやEditor専用設定が製品カタログへ直接残らず、公開済みSpriteだけでRuntime表示できることを保証します。

using System;
using System.Collections.Generic;
using System.Linq;
using Farmware.Simulation;
using Farmware.WorldGeneration.Editor;
using Farmware.WorldGeneration.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// 41材質の公開カタログ、境界11材質、Source分離、Atlas収録件数をEditModeで保証します。
    /// 製品Scene接続前に、素材公開だけを独立して失敗理由まで確認するために使用します。
    /// </summary>
    public sealed class TerrainMaterialProductPublisherTests
    {
        // 今回完成した画像の下段に不透明画素があり、設定へ境界Overlayを登録した11材質です。
        private static readonly TerrainBlockMaterialId[] ExpectedBoundaryStableIds =
        {
            TerrainBlockMaterialId.GrassLoam,
            TerrainBlockMaterialId.MeadowSoil,
            TerrainBlockMaterialId.DryGrassSoil,
            TerrainBlockMaterialId.ForestLoam,
            TerrainBlockMaterialId.HumusSoil,
            TerrainBlockMaterialId.MountainLoam,
            TerrainBlockMaterialId.IslandLoam,
            TerrainBlockMaterialId.PodzolSoil,
            TerrainBlockMaterialId.SteppeSoil,
            TerrainBlockMaterialId.DryScrubSoil,
            TerrainBlockMaterialId.TundraSoil
        };

        /// <summary>
        /// 公開処理が仕様と同じ11材質を固定し、件数だけ合って別材質へずれた状態を拒否できることを確認します。
        /// </summary>
        [Test]
        public void BoundaryMaterialContract_ContainsExpectedElevenStableIds()
        {
            string[] actual = TerrainMaterialProductPublisher.GetExpectedBoundaryMaterialIds();
            string[] expected = ExpectedBoundaryStableIds.Select(id => id.ToString()).ToArray();

            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(actual, Has.Length.EqualTo(11));
        }

        /// <summary>
        /// 保存済み製品カタログが41安定IDを完全登録し、境界あり11件・なし30件を正しい候補数で保持することを確認します。
        /// </summary>
        [Test]
        public void PublishedCatalog_ContainsAllMaterialsWithElevenBoundaries()
        {
            TerrainMaterialPresentationCatalog catalog =
                AssetDatabase.LoadAssetAtPath<TerrainMaterialPresentationCatalog>(
                    TerrainMaterialProductPublisher.CatalogPath);
            Assert.That(catalog, Is.Not.Null, "製品用地形材質カタログが未公開です。");
            Assert.That(catalog.TryInitialize(out string error), Is.True, error);
            Assert.That(
                catalog.DefinitionCount,
                Is.EqualTo(TerrainBlockMaterialDefinitions.Count));

            var expectedBoundarySet =
                new HashSet<TerrainBlockMaterialId>(ExpectedBoundaryStableIds);
            int actualBoundaryCount = 0;
            for (int value = (int)TerrainBlockMaterialDefinitions.First;
                 value <= (int)TerrainBlockMaterialDefinitions.Last;
                 value++)
            {
                TerrainBlockMaterialId stableId = (TerrainBlockMaterialId)value;
                Assert.That(
                    catalog.TryGetDefinition(stableId, out TerrainMaterialPresentationDefinition definition),
                    Is.True,
                    $"製品カタログから{stableId}を取得できません。");

                bool shouldHaveBoundary = expectedBoundarySet.Contains(stableId);
                Assert.That(definition.HasFaceBoundary, Is.EqualTo(shouldHaveBoundary), stableId.ToString());
                Assert.That(
                    definition.FaceBoundaryVariantCount,
                    Is.EqualTo(shouldHaveBoundary
                        ? TerrainMaterialProductPublisher.ExpectedBoundaryVariantCount
                        : 0),
                    stableId.ToString());
                if (definition.HasFaceBoundary)
                {
                    actualBoundaryCount++;
                }

                AssertDefinitionUsesPublishedAssetsOnly(definition);
            }

            Assert.That(actualBoundaryCount, Is.EqualTo(11));
        }

        /// <summary>
        /// 製品Atlasが41材質の上面・断面328枚と、11材質のGround／Wall境界352枚を漏れなく収録することを確認します。
        /// </summary>
        [Test]
        public void ProductAtlas_ContainsAllPublishedMaterialTextures()
        {
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(
                TerrainMaterialProductPublisher.AtlasPath);
            Assert.That(atlas, Is.Not.Null, "製品用地形材質Atlasが未公開です。");

            string[] packablePaths = atlas.GetPackables()
                .Select(AssetDatabase.GetAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            const int regularSpriteCount = TerrainBlockMaterialDefinitions.Count * 8;
            const int composedBoundarySpriteCount = 11 * 4 * 4 * 2;
            Assert.That(
                packablePaths,
                Has.Length.EqualTo(regularSpriteCount + composedBoundarySpriteCount));

            for (int index = 0; index < packablePaths.Length; index++)
            {
                string path = packablePaths[index];
                bool isPublishedRegular = path.StartsWith(
                    TerrainMaterialProductPublisher.PublishedSpriteRootFolder + "/",
                    StringComparison.Ordinal);
                bool isGeneratedBoundary = path.StartsWith(
                    "Assets/Farmware/Art/Generated/TerrainTiles/",
                    StringComparison.Ordinal);
                Assert.That(
                    isPublishedRegular || isGeneratedBoundary,
                    Is.True,
                    $"製品Atlasへ制作入力または想定外Assetが混入しています: {path}");
            }
        }

        /// <summary>1材質の通常候補と境界完成候補がSource外の製品Assetだけを参照することを確認します。</summary>
        /// <param name="definition">完全性検証済みの1材質定義です。</param>
        private static void AssertDefinitionUsesPublishedAssetsOnly(
            TerrainMaterialPresentationDefinition definition)
        {
            for (int index = 0; index < definition.GroundTopSpriteCount; index++)
            {
                AssertProductSpritePath(definition.GetGroundTopSprite(index));
            }

            for (int index = 0; index < definition.FaceSpriteCount; index++)
            {
                AssertProductSpritePath(definition.GetFaceSprite(index));
            }

            for (int faceIndex = 0; faceIndex < definition.FaceSpriteCount; faceIndex++)
            {
                for (int boundaryIndex = 0;
                     boundaryIndex < definition.FaceBoundaryVariantCount;
                     boundaryIndex++)
                {
                    AssertProductSpritePath(
                        definition.GetBoundaryFaceSprite(faceIndex, boundaryIndex, forWall: false));
                    AssertProductSpritePath(
                        definition.GetBoundaryFaceSprite(faceIndex, boundaryIndex, forWall: true));
                }
            }
        }

        /// <summary>SpriteのAssetパスがEditor専用Sourceまたは生成設定ではないことを確認します。</summary>
        /// <param name="sprite">製品カタログから取得した候補Spriteです。</param>
        private static void AssertProductSpritePath(Sprite sprite)
        {
            Assert.That(sprite, Is.Not.Null);
            string path = AssetDatabase.GetAssetPath(sprite);
            Assert.That(
                path.StartsWith("Assets/Farmware/Art/WorldGeneration/Source/", StringComparison.Ordinal),
                Is.False,
                $"製品カタログが制作元Spriteを直接参照しています: {path}");
            Assert.That(
                path,
                Is.Not.EqualTo(TerrainMaterialProductPublisher.GenerationSettingsPath));
        }
    }
}
