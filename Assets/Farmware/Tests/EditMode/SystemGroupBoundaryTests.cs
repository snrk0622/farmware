// このファイルは、共通Assemblyの依存方向とECSルートSystem GroupのUnity更新ループ接続をEditModeで検証します。
// 後続機能が誤ったレイヤー参照や標準Groupへの直接所属を追加したとき、実行前に境界の退行を検出します。

using System;
using System.Collections.Generic;
using Farmware.Authoring;
using Farmware.Input;
using Farmware.Presentation;
using Farmware.Simulation;
using NUnit.Framework;
using Unity.Entities;
using UnityEditor.Compilation;
using CompilationAssembly = UnityEditor.Compilation.Assembly;

namespace Farmware.Tests.EditMode
{
    /// <summary>
    /// SIM-01Aで確定したAssembly依存方向と共通System Group、および後続の共通初期化Systemの親Groupを保証するテスト群です。
    /// Unity Test RunnerのEditModeから実行し、SceneやDefault Worldは作成しません。
    /// </summary>
    public sealed class SystemGroupBoundaryTests
    {
        /// <summary>
        /// 共通Group、初期化System、固定Tick関連SystemのUpdateInGroup属性を読み、設計どおりの親Groupへ接続されていることを保証します。
        /// </summary>
        [Test]
        public void CommonSystemGroups_AreConnectedToExpectedUnityGroups()
        {
            AssertUpdatesInGroup(
                typeof(FarmwareInitializationSystemGroup),
                typeof(InitializationSystemGroup));
            AssertUpdatesInGroup(
                typeof(FarmwareSimulationSystemGroup),
                typeof(SimulationSystemGroup));
            AssertUpdatesInGroup(
                typeof(FarmwarePresentationSystemGroup),
                typeof(PresentationSystemGroup));
            AssertUpdatesInGroup(
                typeof(FarmwareFixedTickSimulationSystemGroup),
                typeof(FarmwareSimulationSystemGroup),
                expectedOrderLast: true);
            AssertUpdatesInGroup(
                typeof(SimulationTimeInitializationSystem),
                typeof(FarmwareInitializationSystemGroup),
                expectedOrderFirst: true);
            AssertUpdatesInGroup(
                typeof(PersistentIdInitializationSystem),
                typeof(FarmwareInitializationSystemGroup));
            AssertUpdatesInGroup(
                typeof(SimulationTickAdvanceSystem),
                typeof(FarmwareFixedTickSimulationSystemGroup),
                expectedOrderFirst: true);
        }

        /// <summary>
        /// 共通Assemblyのコンパイル参照を読み、Simulationへの一方向依存とInput・Presentation・Authoring間の分離を保証します。
        /// </summary>
        [Test]
        public void CommonAssemblies_KeepOneWayLayerDependencies()
        {
            Dictionary<string, HashSet<string>> referencesByAssembly =
                BuildFarmwareReferenceMap();

            Assert.That(
                referencesByAssembly["Farmware.Simulation"],
                Is.Empty,
                "Simulationは他のFarmwareレイヤーを参照してはいけません。");
            AssertLayerReferencesOnlySimulation(referencesByAssembly, "Farmware.Input");
            AssertLayerReferencesOnlySimulation(referencesByAssembly, "Farmware.Presentation");
            AssertLayerReferencesOnlySimulation(referencesByAssembly, "Farmware.Authoring");
        }

        /// <summary>
        /// 各境界型が意図したAssemblyへコンパイルされ、親フォルダーのasmdefが機能別asmdefへ吸収されていないことを保証します。
        /// </summary>
        [Test]
        public void BoundaryTypes_AreCompiledIntoExpectedAssemblies()
        {
            Assert.That(
                typeof(FarmwareSimulationSystemGroup).Assembly.GetName().Name,
                Is.EqualTo("Farmware.Simulation"));
            Assert.That(
                typeof(InputAssemblyBoundary).Assembly.GetName().Name,
                Is.EqualTo("Farmware.Input"));
            Assert.That(
                typeof(FarmwarePresentationSystemGroup).Assembly.GetName().Name,
                Is.EqualTo("Farmware.Presentation"));
            Assert.That(
                typeof(AuthoringAssemblyBoundary).Assembly.GetName().Name,
                Is.EqualTo("Farmware.Authoring"));
        }

        /// <summary>
        /// Player向けコンパイル対象を読み、Editor専用の共通EditModeテストAssemblyが製品ビルドへ混入しないことを保証します。
        /// </summary>
        [Test]
        public void EditModeTestAssembly_IsExcludedFromPlayerCompilation()
        {
            CompilationAssembly[] playerAssemblies =
                CompilationPipeline.GetAssemblies(AssembliesType.Player);

            // Player用Assemblyを名前で確認し、テストコードやNUnit参照が製品側へ含まれる設定退行を検出します。
            for (int index = 0; index < playerAssemblies.Length; index++)
            {
                Assert.That(
                    playerAssemblies[index].name,
                    Is.Not.EqualTo("Farmware.Tests.EditMode"),
                    "Farmware.Tests.EditModeはEditorだけへ含めてください。");
            }
        }

        /// <summary>
        /// 指定SystemのUpdateInGroup属性を取得し、親Groupと先頭・末尾指定が設計どおり明示されていることを確認します。
        /// </summary>
        /// <param name="systemType">所属先を確認するFarmwareのSystemまたはSystem Group型です。</param>
        /// <param name="expectedParentGroupType">期待する親System Group型です。</param>
        /// <param name="expectedOrderFirst">親Group内の先頭で実行する設計ならtrueです。</param>
        /// <param name="expectedOrderLast">親Group内の末尾で実行する設計ならtrueです。</param>
        private static void AssertUpdatesInGroup(
            Type systemType,
            Type expectedParentGroupType,
            bool expectedOrderFirst = false,
            bool expectedOrderLast = false)
        {
            object[] attributes = systemType.GetCustomAttributes(
                typeof(UpdateInGroupAttribute),
                false);

            Assert.That(
                attributes,
                Has.Length.EqualTo(1),
                $"{systemType.Name}には親Groupを1件だけ指定してください。");
            UpdateInGroupAttribute updateInGroup = (UpdateInGroupAttribute)attributes[0];
            Assert.That(updateInGroup.GroupType, Is.EqualTo(expectedParentGroupType));
            Assert.That(updateInGroup.OrderFirst, Is.EqualTo(expectedOrderFirst));
            Assert.That(updateInGroup.OrderLast, Is.EqualTo(expectedOrderLast));
        }

        /// <summary>
        /// Unityのコンパイル情報から共通Farmware Assemblyだけを抽出し、直接参照するFarmware Assembly名を収集します。
        /// </summary>
        /// <returns>Assembly名をキー、そのAssemblyが直接参照するFarmware Assembly名集合を値とする辞書です。</returns>
        private static Dictionary<string, HashSet<string>> BuildFarmwareReferenceMap()
        {
            string[] commonAssemblyNames =
            {
                "Farmware.Simulation",
                "Farmware.Input",
                "Farmware.Presentation",
                "Farmware.Authoring"
            };
            CompilationAssembly[] editorAssemblies =
                CompilationPipeline.GetAssemblies(AssembliesType.Editor);
            Dictionary<string, HashSet<string>> referencesByAssembly =
                new Dictionary<string, HashSet<string>>();

            // Unityが返す全Assemblyから対象名だけを選び、機能別AssemblyやPackage参照を境界判定へ混ぜません。
            for (int nameIndex = 0; nameIndex < commonAssemblyNames.Length; nameIndex++)
            {
                string commonAssemblyName = commonAssemblyNames[nameIndex];
                CompilationAssembly commonAssembly = FindAssembly(
                    editorAssemblies,
                    commonAssemblyName);
                HashSet<string> farmwareReferences = new HashSet<string>();

                // asmdefの直接参照だけを検査し、参照先がさらに使うAssemblyを自Assemblyの依存として数えません。
                for (int referenceIndex = 0;
                     referenceIndex < commonAssembly.assemblyReferences.Length;
                     referenceIndex++)
                {
                    string referenceName =
                        commonAssembly.assemblyReferences[referenceIndex].name;
                    if (referenceName.StartsWith("Farmware.", StringComparison.Ordinal))
                    {
                        farmwareReferences.Add(referenceName);
                    }
                }

                referencesByAssembly.Add(commonAssemblyName, farmwareReferences);
            }

            return referencesByAssembly;
        }

        /// <summary>
        /// Assembly一覧から完全一致する名前を探し、見つからない場合はテスト失敗として原因を明示します。
        /// </summary>
        /// <param name="assemblies">Unityが現在のEditor向けに解決したAssembly一覧です。</param>
        /// <param name="assemblyName">検索するAssemblyの完全名です。</param>
        /// <returns>指定名と一致するコンパイルAssemblyです。</returns>
        private static CompilationAssembly FindAssembly(
            CompilationAssembly[] assemblies,
            string assemblyName)
        {
            for (int index = 0; index < assemblies.Length; index++)
            {
                if (assemblies[index].name == assemblyName)
                {
                    return assemblies[index];
                }
            }

            Assert.Fail($"共通AssemblyがUnityのコンパイル対象にありません: {assemblyName}");
            return null;
        }

        /// <summary>
        /// Input、Presentation、AuthoringがSimulationだけを直接参照し、互いに依存しないことを確認します。
        /// </summary>
        /// <param name="referencesByAssembly">共通AssemblyごとのFarmware直接参照です。</param>
        /// <param name="layerAssemblyName">確認対象となるレイヤーAssembly名です。</param>
        private static void AssertLayerReferencesOnlySimulation(
            Dictionary<string, HashSet<string>> referencesByAssembly,
            string layerAssemblyName)
        {
            Assert.That(
                referencesByAssembly[layerAssemblyName],
                Is.EquivalentTo(new[] { "Farmware.Simulation" }),
                $"{layerAssemblyName}はFarmware.Simulationだけを直接参照してください。");
        }
    }
}
