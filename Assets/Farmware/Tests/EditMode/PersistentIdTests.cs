// このファイルは、ワールド共通永続IDの初期化、単調増加、安定順一括発行、上限処理、保存復元をEditModeで検証します。
// 動物や設備の実装へ依存しない最小データを使い、すべての実行時オブジェクトが共有する発行規則を保証します。

using Farmware.Simulation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace Farmware.Tests.EditMode
{
    /// <summary>
    /// SIM-01Cの共通永続IDアロケーターが、0を発行せず、削除後も再利用せず、入力順に依存しないことを保証します。
    /// Unity Test RunnerのEditModeから実行し、SceneやDefault Worldは使用しません。
    /// </summary>
    public sealed class PersistentIdTests
    {
        /// <summary>
        /// 初期化Systemが新規WorldへNextId 1のSingletonを1件作り、復元済みSingletonがあれば上書きしないことを保証します。
        /// </summary>
        [Test]
        public void InitializationSystem_CreatesInitialStateWithoutOverwritingRestoredState()
        {
            using World newWorld = new World("Persistent ID New World Test");
            SystemHandle newWorldInitialization =
                newWorld.GetOrCreateSystem<PersistentIdInitializationSystem>();

            newWorldInitialization.Update(newWorld.Unmanaged);

            Assert.That(CountAllocatorStates(newWorld), Is.EqualTo(1));
            Assert.That(GetAllocatorState(newWorld).NextId, Is.EqualTo(1ul));
            Assert.That(GetAllocatorState(newWorld).IsExhausted, Is.False);

            using World restoredWorld = new World("Persistent ID Restored World Test");
            Entity restoredEntity = restoredWorld.EntityManager.CreateEntity();
            restoredWorld.EntityManager.AddComponentData(
                restoredEntity,
                new PersistentIdAllocatorState
                {
                    NextId = 42,
                    ExhaustedValue = 0
                });
            SystemHandle restoredWorldInitialization =
                restoredWorld.GetOrCreateSystem<PersistentIdInitializationSystem>();

            restoredWorldInitialization.Update(restoredWorld.Unmanaged);

            Assert.That(CountAllocatorStates(restoredWorld), Is.EqualTo(1));
            Assert.That(GetAllocatorState(restoredWorld).NextId, Is.EqualTo(42ul));
        }

        /// <summary>
        /// 単体発行が1から単調に増え、途中のオブジェクトが削除された想定でも過去IDを再利用しないことを保証します。
        /// </summary>
        [Test]
        public void TryAllocate_StartsAtOneAndNeverReusesEarlierIds()
        {
            PersistentIdAllocatorState state = PersistentIdAllocatorState.CreateInitial();

            Assert.That(PersistentIdAllocator.TryAllocate(ref state, out PersistentObjectId firstId), Is.True);
            Assert.That(PersistentIdAllocator.TryAllocate(ref state, out PersistentObjectId secondId), Is.True);

            // アロケーターはオブジェクトの削除通知を受け取らないため、firstIdが消えても次の番号だけを発行します。
            Assert.That(PersistentIdAllocator.TryAllocate(ref state, out PersistentObjectId afterDeletionId), Is.True);

            Assert.That(PersistentObjectId.CreateInvalid().IsValid, Is.False);
            Assert.That(firstId.Value, Is.EqualTo(1ul));
            Assert.That(secondId.Value, Is.EqualTo(2ul));
            Assert.That(afterDeletionId.Value, Is.EqualTo(3ul));
            Assert.That(state.NextId, Is.EqualTo(4ul));
        }

        /// <summary>
        /// 同じ要求集合を逆順で渡しても安定キー順の割り当て結果が一致し、元の入力配列を変更しないことを保証します。
        /// </summary>
        [Test]
        public void TryAllocateBatch_SortsByStableKeysIndependentlyOfInputOrder()
        {
            PersistentIdAllocationRequest firstRequest = CreateRequest(-2, 5, 10);
            PersistentIdAllocationRequest secondRequest = CreateRequest(-2, 5, 11);
            PersistentIdAllocationRequest thirdRequest = CreateRequest(4, -1, 1);

            using NativeArray<PersistentIdAllocationRequest> forwardRequests =
                new NativeArray<PersistentIdAllocationRequest>(
                    new[] { firstRequest, secondRequest, thirdRequest },
                    Allocator.Temp);
            using NativeArray<PersistentIdAllocationRequest> reverseRequests =
                new NativeArray<PersistentIdAllocationRequest>(
                    new[] { thirdRequest, secondRequest, firstRequest },
                    Allocator.Temp);
            using NativeArray<PersistentIdAssignment> forwardAssignments =
                new NativeArray<PersistentIdAssignment>(3, Allocator.Temp);
            using NativeArray<PersistentIdAssignment> reverseAssignments =
                new NativeArray<PersistentIdAssignment>(3, Allocator.Temp);
            PersistentIdAllocatorState forwardState = PersistentIdAllocatorState.CreateInitial();
            PersistentIdAllocatorState reverseState = PersistentIdAllocatorState.CreateInitial();

            Assert.That(
                PersistentIdAllocator.TryAllocateBatch(
                    ref forwardState,
                    forwardRequests,
                    forwardAssignments),
                Is.True);
            Assert.That(
                PersistentIdAllocator.TryAllocateBatch(
                    ref reverseState,
                    reverseRequests,
                    reverseAssignments),
                Is.True);

            // 結果配列は両方とも同じ安定キー順になり、それぞれへ1、2、3が対応します。
            for (int assignmentIndex = 0;
                 assignmentIndex < forwardAssignments.Length;
                 assignmentIndex++)
            {
                Assert.That(
                    reverseAssignments[assignmentIndex].Request.MajorOrder,
                    Is.EqualTo(forwardAssignments[assignmentIndex].Request.MajorOrder));
                Assert.That(
                    reverseAssignments[assignmentIndex].Request.MinorOrder,
                    Is.EqualTo(forwardAssignments[assignmentIndex].Request.MinorOrder));
                Assert.That(
                    reverseAssignments[assignmentIndex].Request.Sequence,
                    Is.EqualTo(forwardAssignments[assignmentIndex].Request.Sequence));
                Assert.That(
                    forwardAssignments[assignmentIndex].Id.Value,
                    Is.EqualTo((ulong)assignmentIndex + 1));
                Assert.That(
                    reverseAssignments[assignmentIndex].Id.Value,
                    Is.EqualTo(forwardAssignments[assignmentIndex].Id.Value));
            }

            Assert.That(reverseRequests[0].MajorOrder, Is.EqualTo(thirdRequest.MajorOrder));
            Assert.That(forwardState.NextId, Is.EqualTo(4ul));
            Assert.That(reverseState.NextId, Is.EqualTo(forwardState.NextId));
        }

        /// <summary>
        /// 同じ地域座標と受付順を異なるWorldLayer相当のScopeへ分け、重複拒否せずScope順にIDを発行することを保証します。
        /// </summary>
        [Test]
        public void TryAllocateBatch_SameKeysDifferentScopes_DoNotCollide()
        {
            PersistentIdAllocationRequest underwater = CreateRequest(-2, 5, 10, scopeOrder: 2);
            PersistentIdAllocationRequest surface = CreateRequest(-2, 5, 10, scopeOrder: 1);
            using NativeArray<PersistentIdAllocationRequest> requests =
                new NativeArray<PersistentIdAllocationRequest>(
                    new[] { underwater, surface },
                    Allocator.Temp);
            using NativeArray<PersistentIdAssignment> assignments =
                new NativeArray<PersistentIdAssignment>(2, Allocator.Temp);
            PersistentIdAllocatorState state = PersistentIdAllocatorState.CreateInitial();

            Assert.That(
                PersistentIdAllocator.TryAllocateBatch(ref state, requests, assignments),
                Is.True);
            Assert.That(assignments[0].Request.ScopeOrder, Is.EqualTo(1));
            Assert.That(assignments[0].Id.Value, Is.EqualTo(1ul));
            Assert.That(assignments[1].Request.ScopeOrder, Is.EqualTo(2));
            Assert.That(assignments[1].Id.Value, Is.EqualTo(2ul));
        }

        /// <summary>
        /// 重複した安定キーまたは残数を越える一括要求を、IDを一部消費せず全体として拒否することを保証します。
        /// </summary>
        [Test]
        public void TryAllocateBatch_InvalidBatchDoesNotConsumeIds()
        {
            PersistentIdAllocationRequest duplicateRequest = CreateRequest(1, 2, 3);
            using NativeArray<PersistentIdAllocationRequest> duplicateRequests =
                new NativeArray<PersistentIdAllocationRequest>(
                    new[] { duplicateRequest, duplicateRequest },
                    Allocator.Temp);
            using NativeArray<PersistentIdAssignment> duplicateAssignments =
                new NativeArray<PersistentIdAssignment>(2, Allocator.Temp);
            PersistentIdAllocatorState duplicateState = PersistentIdAllocatorState.CreateInitial();

            Assert.That(
                PersistentIdAllocator.TryAllocateBatch(
                    ref duplicateState,
                    duplicateRequests,
                    duplicateAssignments),
                Is.False);
            Assert.That(duplicateState.NextId, Is.EqualTo(1ul));

            using NativeArray<PersistentIdAllocationRequest> mismatchedRequests =
                new NativeArray<PersistentIdAllocationRequest>(
                    new[] { CreateRequest(0, 0, 0) },
                    Allocator.Temp);
            using NativeArray<PersistentIdAssignment> mismatchedAssignments =
                new NativeArray<PersistentIdAssignment>(0, Allocator.Temp);
            PersistentIdAllocatorState mismatchedState = PersistentIdAllocatorState.CreateInitial();

            // 出力先が不足した状態で一部だけ発行すると呼び出し側へ対応付けられないため、発行前に全体を拒否します。
            Assert.That(
                PersistentIdAllocator.TryAllocateBatch(
                    ref mismatchedState,
                    mismatchedRequests,
                    mismatchedAssignments),
                Is.False);
            Assert.That(mismatchedState.NextId, Is.EqualTo(1ul));

            using NativeArray<PersistentIdAllocationRequest> overflowRequests =
                new NativeArray<PersistentIdAllocationRequest>(
                    new[] { CreateRequest(0, 0, 0), CreateRequest(0, 0, 1) },
                    Allocator.Temp);
            using NativeArray<PersistentIdAssignment> overflowAssignments =
                new NativeArray<PersistentIdAssignment>(2, Allocator.Temp);
            PersistentIdAllocatorState overflowState = new PersistentIdAllocatorState
            {
                NextId = ulong.MaxValue,
                ExhaustedValue = 0
            };

            Assert.That(
                PersistentIdAllocator.TryAllocateBatch(
                    ref overflowState,
                    overflowRequests,
                    overflowAssignments),
                Is.False);
            Assert.That(overflowState.NextId, Is.EqualTo(ulong.MaxValue));
            Assert.That(overflowState.IsExhausted, Is.False);
        }

        /// <summary>
        /// ulong.MaxValueを最後の有効IDとして1回発行し、その後は0へ周回せず失敗し続けることを保証します。
        /// </summary>
        [Test]
        public void TryAllocate_StopsAfterIssuingMaximumValue()
        {
            PersistentIdAllocatorState state = new PersistentIdAllocatorState
            {
                NextId = ulong.MaxValue,
                ExhaustedValue = 0
            };

            Assert.That(PersistentIdAllocator.TryAllocate(ref state, out PersistentObjectId lastId), Is.True);
            Assert.That(lastId.Value, Is.EqualTo(ulong.MaxValue));
            Assert.That(state.IsExhausted, Is.True);

            Assert.That(PersistentIdAllocator.TryAllocate(ref state, out PersistentObjectId rejectedId), Is.False);
            Assert.That(rejectedId.IsValid, Is.False);
            Assert.That(state.NextId, Is.EqualTo(ulong.MaxValue));
        }

        /// <summary>
        /// 次回発行位置と上限到達状態をSnapshotへ保存して復元し、発行済みIDをロード後に再利用しないことを保証します。
        /// </summary>
        [Test]
        public void Snapshot_RestoresNextIdAndRejectsMalformedData()
        {
            PersistentIdAllocatorState originalState = PersistentIdAllocatorState.CreateInitial();
            Assert.That(PersistentIdAllocator.TryAllocate(ref originalState, out _), Is.True);
            Assert.That(PersistentIdAllocator.TryAllocate(ref originalState, out _), Is.True);

            Assert.That(
                PersistentIdAllocator.TryCreateSnapshot(
                    originalState,
                    out PersistentIdAllocatorSnapshot snapshot),
                Is.True);
            Assert.That(
                PersistentIdAllocator.TryRestoreSnapshot(
                    snapshot,
                    out PersistentIdAllocatorState restoredState),
                Is.True);
            Assert.That(
                PersistentIdAllocator.TryAllocate(
                    ref restoredState,
                    out PersistentObjectId loadedWorldId),
                Is.True);
            Assert.That(loadedWorldId.Value, Is.EqualTo(3ul));

            PersistentIdAllocatorSnapshot malformedSnapshot = new PersistentIdAllocatorSnapshot
            {
                NextId = 12,
                ExhaustedValue = 1
            };
            Assert.That(
                PersistentIdAllocator.TryRestoreSnapshot(
                    malformedSnapshot,
                    out PersistentIdAllocatorState rejectedState),
                Is.False);
            Assert.That(rejectedState.NextId, Is.Zero);

            PersistentIdAllocatorState maximumState = new PersistentIdAllocatorState
            {
                NextId = ulong.MaxValue,
                ExhaustedValue = 0
            };
            Assert.That(PersistentIdAllocator.TryAllocate(ref maximumState, out _), Is.True);
            Assert.That(
                PersistentIdAllocator.TryCreateSnapshot(
                    maximumState,
                    out PersistentIdAllocatorSnapshot exhaustedSnapshot),
                Is.True);
            Assert.That(
                PersistentIdAllocator.TryRestoreSnapshot(
                    exhaustedSnapshot,
                    out PersistentIdAllocatorState exhaustedRestoredState),
                Is.True);
            Assert.That(exhaustedRestoredState.IsExhausted, Is.True);
            Assert.That(
                PersistentIdAllocator.TryAllocate(ref exhaustedRestoredState, out _),
                Is.False,
                "上限到達済み状態をロードした後もID 0へ周回してはいけません。");
        }

        /// <summary>
        /// 一括発行テストで使う4段階の安定キーを、各値が分かる形で作成します。
        /// </summary>
        /// <param name="majorOrder">Scopeの後に比較する地域やCommand分類相当の値です。</param>
        /// <param name="minorOrder">Majorの後に比較する地域やオブジェクト分類相当の値です。</param>
        /// <param name="sequence">最後に比較する一意な受付順です。</param>
        /// <param name="scopeOrder">WorldLayerなど、同じ座標空間を分離する最優先の範囲値です。</param>
        /// <returns>指定値を保持する生成要求です。</returns>
        private static PersistentIdAllocationRequest CreateRequest(
            long majorOrder,
            long minorOrder,
            ulong sequence,
            long scopeOrder = 0)
        {
            return new PersistentIdAllocationRequest
            {
                ScopeOrder = scopeOrder,
                MajorOrder = majorOrder,
                MinorOrder = minorOrder,
                Sequence = sequence
            };
        }

        /// <summary>
        /// 指定Worldに存在するPersistentIdAllocatorState Component数を数えます。
        /// </summary>
        /// <param name="world">アロケーター状態を検索するテストWorldです。</param>
        /// <returns>PersistentIdAllocatorStateを持つEntity数です。</returns>
        private static int CountAllocatorStates(World world)
        {
            EntityQuery query = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PersistentIdAllocatorState>());
            int count = query.CalculateEntityCount();
            query.Dispose();
            return count;
        }

        /// <summary>
        /// 指定WorldのPersistentIdAllocatorState Singletonを読み取り、一時Queryを破棄します。
        /// </summary>
        /// <param name="world">アロケーター状態を所有するテストWorldです。</param>
        /// <returns>Worldで唯一のアロケーター状態です。</returns>
        private static PersistentIdAllocatorState GetAllocatorState(World world)
        {
            EntityQuery query = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PersistentIdAllocatorState>());
            PersistentIdAllocatorState state =
                query.GetSingleton<PersistentIdAllocatorState>();
            query.Dispose();
            return state;
        }
    }
}
