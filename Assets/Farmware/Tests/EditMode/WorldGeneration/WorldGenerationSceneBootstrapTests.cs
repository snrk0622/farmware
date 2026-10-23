// このファイルは、フォールバックScene構築メニューがユーザーの未保存Sceneと複数Scene構成を保護することを検証します。
// 実際のSceneやアセットを変更せず、保存確認、構築、復元の各処理をDelegateで差し替えて実行順と例外経路を保証します。

using System;
using System.Collections.Generic;
using System.Reflection;
using Farmware.Authoring;
using Farmware.Simulation;
using Farmware.Simulation.Animals;
using Farmware.WorldGeneration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Farmware.WorldGeneration.Tests
{
    /// <summary>
    /// WorldGenerationSceneBootstrapの対話メニューが、キャンセルと構築失敗を含めて元のScene構成を守ることを保証します。
    /// </summary>
    public sealed class WorldGenerationSceneBootstrapTests
    {
        /// <summary>
        /// 保存確認をキャンセルした場合、Scene構成の取得、アセット構築、Scene復元のいずれも開始しないことを検証します。
        /// </summary>
        [Test]
        public void RunInteractiveBuild_SaveCancelled_DoesNotStartBuild()
        {
            var calls = new List<string>();

            // 保存確認以外のDelegateが呼ばれた場合に履歴へ残し、キャンセル後の副作用を検出します。
            InvokeInteractiveBuild(
                () =>
                {
                    calls.Add("confirm");
                    return false;
                },
                () =>
                {
                    calls.Add("capture");
                    return Array.Empty<SceneSetup>();
                },
                () => calls.Add("build"),
                _ => calls.Add("restore"));

            CollectionAssert.AreEqual(new[] { "confirm" }, calls);
        }

        /// <summary>
        /// 構築が成功した場合、保存確認後にScene構成を退避し、構築完了後に同じ構成を復元することを検証します。
        /// </summary>
        [Test]
        public void RunInteractiveBuild_BuildSucceeded_RestoresCapturedSceneSetup()
        {
            var calls = new List<string>();
            SceneSetup[] capturedSetup = Array.Empty<SceneSetup>();
            SceneSetup[] restoredSetup = null;

            // Delegateの呼び出し履歴と復元へ渡された配列を記録し、処理順とデータの受け渡しを同時に確認します。
            InvokeInteractiveBuild(
                () =>
                {
                    calls.Add("confirm");
                    return true;
                },
                () =>
                {
                    calls.Add("capture");
                    return capturedSetup;
                },
                () => calls.Add("build"),
                setup =>
                {
                    calls.Add("restore");
                    restoredSetup = setup;
                });

            CollectionAssert.AreEqual(new[] { "confirm", "capture", "build", "restore" }, calls);
            Assert.That(restoredSetup, Is.SameAs(capturedSetup));
        }

        /// <summary>
        /// 構築中に例外が発生してもScene構成を復元し、その後に元の例外を呼び出し元へ返すことを検証します。
        /// </summary>
        [Test]
        public void RunInteractiveBuild_BuildFailed_RestoresSceneSetupAndRethrows()
        {
            var calls = new List<string>();
            SceneSetup[] capturedSetup = Array.Empty<SceneSetup>();
            SceneSetup[] restoredSetup = null;

            // Reflection呼び出しでは対象メソッドの例外がTargetInvocationExceptionに包まれるため、内側の型まで確認します。
            TargetInvocationException invocationException = Assert.Throws<TargetInvocationException>(
                () => InvokeInteractiveBuild(
                    () =>
                    {
                        calls.Add("confirm");
                        return true;
                    },
                    () =>
                    {
                        calls.Add("capture");
                        return capturedSetup;
                    },
                    () =>
                    {
                        calls.Add("build");
                        throw new InvalidOperationException("テスト用の構築失敗です。");
                    },
                    setup =>
                    {
                        calls.Add("restore");
                        restoredSetup = setup;
                    }));

            CollectionAssert.AreEqual(new[] { "confirm", "capture", "build", "restore" }, calls);
            Assert.That(restoredSetup, Is.SameAs(capturedSetup));
            Assert.That(invocationException.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        /// <summary>
        /// Scene構築ツールが鶏1種のGUID、草原・陸地条件、群れ、密度、固定Tick移動、外見差分を再現することを保証します。
        /// </summary>
        [Test]
        public void ConfigurePrototypeAnimalDefinitions_AppliesChickenDefinition()
        {
            GameObject settingsObject = new GameObject("Animal Definition Test");
            try
            {
                DefinitionCatalogAuthoring authoring =
                    settingsObject.AddComponent<DefinitionCatalogAuthoring>();
                MethodInfo configureMethod = typeof(WorldGenerationSceneBootstrap).GetMethod(
                    "ConfigurePrototypeAnimalDefinitions",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(configureMethod, Is.Not.Null, "鶏定義の構築処理が見つかりません。");

                configureMethod.Invoke(null, new object[] { authoring });

                SerializedObject serializedAuthoring = new SerializedObject(authoring);
                SerializedProperty definitions = serializedAuthoring.FindProperty("definitions");
                SerializedProperty animalDefinitions =
                    serializedAuthoring.FindProperty("animalSpeciesDefinitions");
                Assert.That(definitions.arraySize, Is.EqualTo(1));
                Assert.That(animalDefinitions.arraySize, Is.EqualTo(1));

                SerializedProperty chickenDefinition = definitions.GetArrayElementAtIndex(0);
                SerializedProperty chickenSettings = animalDefinitions.GetArrayElementAtIndex(0);
                Assert.That(
                    chickenDefinition.FindPropertyRelative("displayName").stringValue,
                    Is.EqualTo("Chicken"));
                Assert.That(
                    chickenDefinition.FindPropertyRelative("kind").intValue,
                    Is.EqualTo((int)DefinitionKind.AnimalSpecies));
                Assert.That(
                    chickenDefinition.FindPropertyRelative("guid").stringValue,
                    Is.EqualTo("1728e39573b24efa996020e9493cc742"));
                Assert.That(
                    chickenSettings.FindPropertyRelative("definitionGuid").stringValue,
                    Is.EqualTo("1728e39573b24efa996020e9493cc742"));
                Assert.That(
                    chickenSettings.FindPropertyRelative("allowedBiomeBits").intValue,
                    Is.EqualTo((int)AnimalBiomeMask.TemperateGrassland));
                Assert.That(
                    chickenSettings.FindPropertyRelative("allowedGrounds").intValue,
                    Is.EqualTo((int)AnimalGroundMask.Land));
                Assert.That(
                    chickenSettings.FindPropertyRelative("minimumHerdSize").intValue,
                    Is.EqualTo(3));
                Assert.That(
                    chickenSettings.FindPropertyRelative("maximumHerdSize").intValue,
                    Is.EqualTo(6));
                Assert.That(
                    chickenSettings.FindPropertyRelative("targetPopulationPer4096EligibleTiles").intValue,
                    Is.EqualTo(8));
                Assert.That(
                    chickenSettings.FindPropertyRelative("minimumIdleTicks").longValue,
                    Is.EqualTo(20));
                Assert.That(
                    chickenSettings.FindPropertyRelative("maximumIdleTicks").longValue,
                    Is.EqualTo(80));
                Assert.That(
                    chickenSettings.FindPropertyRelative("movementStepIntervalTicks").longValue,
                    Is.EqualTo(5));
                Assert.That(
                    chickenSettings.FindPropertyRelative("maximumWanderDistanceInTiles").intValue,
                    Is.EqualTo(8));
                Assert.That(
                    chickenSettings.FindPropertyRelative("appearanceVariantCount").intValue,
                    Is.EqualTo(3));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settingsObject);
            }
        }

        /// <summary>
        /// 地形固有値の入力欄が未実装の段階ではInspector由来の地形分類を明示拒否し、件数不一致だけの曖昧なBake失敗にしないことを保証します。
        /// </summary>
        /// <param name="terrainKind">TERRAIN-01Cまで組み込み専用とする地形分類です。</param>
        [TestCase(DefinitionKind.GroundBlock)]
        [TestCase(DefinitionKind.WallBlock)]
        [TestCase(DefinitionKind.Liquid)]
        [TestCase(DefinitionKind.TerrainSurface)]
        public void RejectUnsupportedAuthoredTerrainDefinition_TerrainKind_Throws(
            DefinitionKind terrainKind)
        {
            Type bakerType = typeof(DefinitionCatalogAuthoring).GetNestedType(
                "DefinitionCatalogBaker",
                BindingFlags.NonPublic);
            Assert.That(bakerType, Is.Not.Null, "DefinitionCatalogのBakerが見つかりません。");
            MethodInfo validationMethod = bakerType.GetMethod(
                "RejectUnsupportedAuthoredTerrainDefinition",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(validationMethod, Is.Not.Null, "地形分類の事前検証が見つかりません。");

            // privateのBaker検証を直接呼び、地形Blob構築前に具体的な理由で停止することを確認します。
            TargetInvocationException invocationException =
                Assert.Throws<TargetInvocationException>(
                    () => validationMethod.Invoke(
                        null,
                        new object[] { terrainKind, "Custom Terrain", 2 }));
            Assert.That(
                invocationException.InnerException,
                Is.TypeOf<InvalidOperationException>());
            StringAssert.Contains(
                "TERRAIN-01Cまで組み込み定義だけ",
                invocationException.InnerException?.Message);
            StringAssert.Contains(
                "Definition[2] (Custom Terrain)",
                invocationException.InnerException?.Message);
        }

        /// <summary>
        /// 動物種設定のInspector見出しが、GUIDで対応する共通定義の表示名を使用することを保証します。
        /// </summary>
        [Test]
        public void BuildAnimalSpeciesElementLabel_MatchingGuid_UsesDefinitionDisplayName()
        {
            GameObject settingsObject = new GameObject("Animal Definition Label Test");
            try
            {
                DefinitionCatalogAuthoring authoring =
                    settingsObject.AddComponent<DefinitionCatalogAuthoring>();
                MethodInfo configureMethod = typeof(WorldGenerationSceneBootstrap).GetMethod(
                    "ConfigurePrototypeAnimalDefinitions",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.That(configureMethod, Is.Not.Null, "鶏定義の構築処理が見つかりません。");
                configureMethod.Invoke(null, new object[] { authoring });

                SerializedObject serializedAuthoring = new SerializedObject(authoring);
                SerializedProperty definitions = serializedAuthoring.FindProperty("definitions");
                SerializedProperty animalDefinitions =
                    serializedAuthoring.FindProperty("animalSpeciesDefinitions");

                // 動物種固有設定には名前を重複保存せず、共通GUIDから現在の表示名を取得します。
                string label = DefinitionCatalogAuthoringEditor.BuildAnimalSpeciesElementLabel(
                    definitions,
                    animalDefinitions.GetArrayElementAtIndex(0),
                    0);

                Assert.That(label, Is.EqualTo("0: Chicken"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settingsObject);
            }
        }

        /// <summary>
        /// 共通定義に存在しないGUIDでも要素番号と短縮GUIDを表示し、設定ミスを見つけられることを保証します。
        /// </summary>
        [Test]
        public void BuildAnimalSpeciesElementLabel_UnknownGuid_ShowsUnresolvedGuid()
        {
            GameObject settingsObject = new GameObject("Unknown Animal Definition Label Test");
            try
            {
                DefinitionCatalogAuthoring authoring =
                    settingsObject.AddComponent<DefinitionCatalogAuthoring>();
                SerializedObject serializedAuthoring = new SerializedObject(authoring);
                SerializedProperty definitions = serializedAuthoring.FindProperty("definitions");
                SerializedProperty animalDefinitions =
                    serializedAuthoring.FindProperty("animalSpeciesDefinitions");
                animalDefinitions.arraySize = 1;
                SerializedProperty animalSpecies = animalDefinitions.GetArrayElementAtIndex(0);
                animalSpecies.FindPropertyRelative("definitionGuid").stringValue =
                    "aaaaaaaa111122223333444444444444";

                // 名前解決に失敗しても単なるElement 0へ戻さず、問題のGUIDを見出しへ残します。
                string label = DefinitionCatalogAuthoringEditor.BuildAnimalSpeciesElementLabel(
                    definitions,
                    animalSpecies,
                    0);

                Assert.That(label, Is.EqualTo("0: 名前未解決（aaaaaaaa）"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settingsObject);
            }
        }

        /// <summary>
        /// 非公開の対話実行処理を呼び出し、UnityのダイアログやScene切替を発生させずに制御フローを検証します。
        /// </summary>
        /// <param name="confirmSceneSave">保存確認結果を返し、呼び出し履歴を記録するDelegateです。</param>
        /// <param name="captureSceneSetup">退避対象のScene構成を返すDelegateです。</param>
        /// <param name="build">構築成功または構築失敗を再現するDelegateです。</param>
        /// <param name="restoreSceneSetup">復元対象と呼び出し履歴を記録するDelegateです。</param>
        private static void InvokeInteractiveBuild(
            Func<bool> confirmSceneSave,
            Func<SceneSetup[]> captureSceneSetup,
            Action build,
            Action<SceneSetup[]> restoreSceneSetup)
        {
            MethodInfo workflowMethod = typeof(WorldGenerationSceneBootstrap).GetMethod(
                "RunInteractiveBuild",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(workflowMethod, Is.Not.Null, "対話実行処理が見つかりません。");

            // private実装を公開APIへ広げずに検証するため、テストAssemblyからReflectionでDelegate一式を渡します。
            workflowMethod.Invoke(
                null,
                new object[]
                {
                    confirmSceneSave,
                    captureSceneSetup,
                    build,
                    restoreSceneSetup
                });
        }
    }
}
