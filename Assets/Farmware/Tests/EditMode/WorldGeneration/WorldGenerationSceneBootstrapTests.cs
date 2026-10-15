// このファイルは、フォールバックScene構築メニューがユーザーの未保存Sceneと複数Scene構成を保護することを検証します。
// 実際のSceneやアセットを変更せず、保存確認、構築、復元の各処理をDelegateで差し替えて実行順と例外経路を保証します。

using System;
using System.Collections.Generic;
using System.Reflection;
using Farmware.WorldGeneration.Editor;
using NUnit.Framework;
using UnityEditor.SceneManagement;

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
