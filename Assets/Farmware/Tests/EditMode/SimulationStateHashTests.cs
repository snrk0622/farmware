// このファイルは、SIM-01Fの固定バイト表現、状態選別、安定ID順、Command列とJob条件に依存しない決定論を検証します。
// テスト専用の最小CommandとGameplay Jobを2つのECS Worldで実行し、論理状態だけを比較できることを保証します。

using System;
using Farmware.Simulation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;

namespace Farmware.Tests.EditMode
{
    /// <summary>
    /// 状態ハッシュ決定論テストで、永続ID付きEntityを作成するためのCommand入力です。
    /// 製品用Commandが未実装でも、共通Command列から同じ論理状態へ到達する契約を確認します。
    /// </summary>
    public struct StateHashTestCreateCommand : IComponentData
    {
        /// <summary>作成するテストEntityへ付ける1以上の論理IDです。</summary>
        public ulong PersistentId;

        /// <summary>Gameplay Jobが固定Tick内で更新する初期整数値です。</summary>
        public int InitialValue;
    }

    /// <summary>
    /// 状態ハッシュへ含めるテスト専用の論理値です。
    /// PersistentObjectIdと組み合わせ、ECSのEntity Indexではなく永続ID順で正規化します。
    /// </summary>
    public struct StateHashTestValue : IComponentData
    {
        /// <summary>Commandで設定し、Gameplay Jobが永続IDに応じて変更する値です。</summary>
        public int Value;
    }

    /// <summary>
    /// 同じGameplay計算をメインスレッドまたは並列Jobのどちらで実行するか指定するテスト設定です。
    /// これは実行条件であり論理ゲーム状態ではないため、状態ハッシュには含めません。
    /// </summary>
    public struct StateHashTestExecutionMode : IComponentData
    {
        /// <summary>0ならRun、1ならScheduleParallelで同じ計算を実行します。</summary>
        public byte UseParallelJob;
    }

    /// <summary>
    /// 一方のWorldだけでEntity IndexとChunk配置をずらすための無関係なテストComponentです。
    /// 論理Simulation状態ではないため、状態ハッシュから除外されることを確認します。
    /// </summary>
    public struct StateHashTestUnrelatedData : IComponentData
    {
        /// <summary>Entityを区別するだけの値で、ゲーム結果には使用しません。</summary>
        public int Value;
    }

    /// <summary>
    /// 状態ハッシュ用の作成Commandを1件だけ担当し、無効IDを状態変更前に拒否します。
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(FarmwareCommandValidationSystemGroup))]
    public partial struct StateHashTestValidationSystem : ISystem
    {
        /// <summary>追加の永続状態を必要としないため、作成時には何も準備しません。</summary>
        /// <param name="state">テストWorldのSystem状態です。</param>
        public void OnCreate(ref SystemState state)
        {
        }

        /// <summary>
        /// ActiveなテストCommandの処理権を取得し、永続IDが1以上ならCommit可能へ進めます。
        /// </summary>
        /// <param name="state">Command状態を読み書きするテストWorldです。</param>
        public void OnUpdate(ref SystemState state)
        {
            foreach ((RefRW<SimulationCommand> commandReference,
                     RefRO<StateHashTestCreateCommand> payloadReference) in
                     SystemAPI.Query<RefRW<SimulationCommand>,
                         RefRO<StateHashTestCreateCommand>>()
                         .WithAll<SimulationCommandActive>())
            {
                SimulationCommand command = commandReference.ValueRO;
                if (!SimulationCommandTransaction.TryClaimValidation(ref command))
                {
                    commandReference.ValueRW = command;
                    continue;
                }

                if (payloadReference.ValueRO.PersistentId
                    == PersistentObjectId.InvalidValue)
                {
                    SimulationCommandTransaction.RejectValidation(
                        ref command,
                        SimulationCommand.FirstFeatureFailureCode);
                }
                else
                {
                    SimulationCommandTransaction.AcceptValidation(ref command);
                }

                commandReference.ValueRW = command;
            }
        }

        /// <summary>World破棄時に呼ばれます。所有するNative Collectionはありません。</summary>
        /// <param name="state">破棄されるテストWorldのSystem状態です。</param>
        public void OnDestroy(ref SystemState state)
        {
        }
    }

    /// <summary>
    /// 検証済みのテストCommandから、永続IDと論理値を持つEntityをCommand専用ECBへ記録します。
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(FarmwareCommandCommitSystemGroup))]
    public partial struct StateHashTestCommitSystem : ISystem
    {
        /// <summary>Command専用ECB Singletonが存在する固定Tickだけ更新するよう登録します。</summary>
        /// <param name="state">更新条件を保持するSystem状態です。</param>
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SimulationCommandEntityCommandBufferSystem.Singleton>();
        }

        /// <summary>
        /// ReadyToCommitなCommandごとにEntity作成を記録し、全記録後に成功状態へ進めます。
        /// </summary>
        /// <param name="state">ECB取得とCommand状態更新に使用するSystem状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            SimulationCommandEntityCommandBufferSystem.Singleton bufferSingleton =
                SystemAPI.GetSingleton<SimulationCommandEntityCommandBufferSystem.Singleton>();
            EntityCommandBuffer commandBuffer =
                bufferSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            foreach ((RefRW<SimulationCommand> commandReference,
                     RefRO<StateHashTestCreateCommand> payloadReference) in
                     SystemAPI.Query<RefRW<SimulationCommand>,
                         RefRO<StateHashTestCreateCommand>>()
                         .WithAll<SimulationCommandActive>())
            {
                SimulationCommand command = commandReference.ValueRO;
                if (!SimulationCommandTransaction.TryBeginCommit(ref command))
                {
                    commandReference.ValueRW = command;
                    continue;
                }

                StateHashTestCreateCommand payload = payloadReference.ValueRO;
                Entity createdEntity = commandBuffer.CreateEntity();
                commandBuffer.AddComponent(
                    createdEntity,
                    new PersistentObjectId
                    {
                        Value = payload.PersistentId
                    });
                commandBuffer.AddComponent(
                    createdEntity,
                    new StateHashTestValue
                    {
                        Value = payload.InitialValue
                    });

                SimulationCommandTransaction.CompleteCommit(ref command);
                commandReference.ValueRW = command;
            }
        }

        /// <summary>World破棄時に呼ばれます。ECBの解放は共通ECB Systemが担当します。</summary>
        /// <param name="state">破棄されるテストWorldのSystem状態です。</param>
        public void OnDestroy(ref SystemState state)
        {
        }
    }

    /// <summary>
    /// 永続IDから求めた同じ整数加算を、RunまたはScheduleParallelで実行するJobです。
    /// 書き込み先は各Entityで独立しているため、実行順が変わっても論理結果は一致する必要があります。
    /// </summary>
    public partial struct StateHashTestValueUpdateJob : IJobEntity
    {
        /// <summary>
        /// 1件の値へ永続IDの下位範囲から求めた決定的な加算を適用します。
        /// </summary>
        /// <param name="value">このEntityだけが所有する更新対象値です。</param>
        /// <param name="persistentId">実行順に依存しない計算元の論理IDです。</param>
        public void Execute(
            ref StateHashTestValue value,
            in PersistentObjectId persistentId)
        {
            value.Value += checked((int)persistentId.Value * 3);
        }
    }

    /// <summary>
    /// テスト設定に従って同じGameplay JobをメインスレッドまたはWorker Threadへ接続します。
    /// 並列側も同じ固定Tick内で完了させ、ハッシュ取得時に未完了Jobを残しません。
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(FarmwareFixedTickGameplaySystemGroup))]
    public partial struct StateHashTestGameplaySystem : ISystem
    {
        /// <summary>実行方式Singletonが存在するテストWorldだけ更新するよう登録します。</summary>
        /// <param name="state">更新条件を保持するSystem状態です。</param>
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<StateHashTestExecutionMode>();
        }

        /// <summary>
        /// 同一のJobを選択されたスケジュール方法で実行し、比較前に依存関係を完了します。
        /// </summary>
        /// <param name="state">Job依存関係と実行設定を所有するSystem状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            StateHashTestExecutionMode mode =
                SystemAPI.GetSingleton<StateHashTestExecutionMode>();
            StateHashTestValueUpdateJob job = new StateHashTestValueUpdateJob();
            if (mode.UseParallelJob != 0)
            {
                state.Dependency = job.ScheduleParallel(state.Dependency);
                state.Dependency.Complete();
            }
            else
            {
                job.Run();
            }
        }

        /// <summary>World破棄時に呼ばれます。Jobは各更新内で完了しているため追加解放はありません。</summary>
        /// <param name="state">破棄されるテストWorldのSystem状態です。</param>
        public void OnDestroy(ref SystemState state)
        {
        }
    }

    /// <summary>
    /// 状態ハッシュのバイト契約、共通状態の検証、異なるJob条件での決定論を保証するEditModeテスト群です。
    /// </summary>
    public sealed class SimulationStateHashTests
    {
        /// <summary>テスト固有の永続ID付き値を表す最初の機能セクションです。</summary>
        private const uint TestValueSectionId =
            SimulationStateHashSchema.FirstFeatureSectionId;

        /// <summary>
        /// 同じ型付き整数列は同じ128bit値となり、値を1つ変更すると異なる値になることを保証します。
        /// </summary>
        [Test]
        public void Builder_SameCanonicalValuesMatchAndChangedValueDiffers()
        {
            SimulationStateHash first = BuildPrimitiveHash(lastValue: 99);
            SimulationStateHash same = BuildPrimitiveHash(lastValue: 99);
            SimulationStateHash changed = BuildPrimitiveHash(lastValue: 100);

            Assert.That(first, Is.EqualTo(same));
            Assert.That(first, Is.Not.EqualTo(changed));
            Assert.That(first.SchemaVersion, Is.EqualTo(1u));
            Assert.That(first.ToString().Length, Is.EqualTo(32));
        }

        /// <summary>
        /// セクションIDの重複と逆順を拒否し、機能追加順でハッシュ結果が揺れない契約を保証します。
        /// </summary>
        [Test]
        public void Builder_RejectsDuplicateOrDescendingSectionIds()
        {
            SimulationStateHashBuilder duplicateBuilder =
                new SimulationStateHashBuilder(SimulationStateHashSchema.CurrentVersion);
            duplicateBuilder.BeginSection(10);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => duplicateBuilder.BeginSection(10));

            SimulationStateHashBuilder descendingBuilder =
                new SimulationStateHashBuilder(SimulationStateHashSchema.CurrentVersion);
            descendingBuilder.BeginSection(20);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => descendingBuilder.BeginSection(19));
        }

        /// <summary>
        /// Complete後の追記と二重Completeを拒否し、途中状態を別の正式ハッシュへ流用しないことを保証します。
        /// </summary>
        [Test]
        public void Builder_RejectsWritesAfterCompletion()
        {
            SimulationStateHashBuilder builder =
                new SimulationStateHashBuilder(SimulationStateHashSchema.CurrentVersion);
            builder.BeginSection(1);
            builder.WriteUInt32(42);
            builder.Complete();

            Assert.Throws<InvalidOperationException>(() => builder.WriteByte(1));
            Assert.Throws<InvalidOperationException>(() => builder.Complete());
        }

        /// <summary>
        /// 必須Singletonが欠けたWorldではハッシュを返さず、任意の既定値で不完全な状態を比較しないことを保証します。
        /// </summary>
        [Test]
        public void CoreHasher_RejectsMissingRequiredSingleton()
        {
            using World world = new World("State Hash Missing Singleton Test");

            bool succeeded = SimulationCoreStateHasher.TryCompute(
                world.EntityManager,
                out SimulationStateHash hash,
                out SimulationStateHashError error);

            Assert.That(succeeded, Is.False);
            Assert.That(hash, Is.EqualTo(default(SimulationStateHash)));
            Assert.That(
                error.Code,
                Is.EqualTo(SimulationStateHashErrorCode.RequiredSingletonMissing));
        }

        /// <summary>
        /// 必須Singletonが複数あるWorldを拒否し、Entity検索順で一方を選ぶ非決定的な処理を防ぐことを保証します。
        /// </summary>
        [Test]
        public void CoreHasher_RejectsDuplicatedRequiredSingleton()
        {
            using World world = CreateCoreStateWorld();
            Entity duplicateEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(
                duplicateEntity,
                new SimulationTickState
                {
                    CurrentTick = 7
                });

            bool succeeded = SimulationCoreStateHasher.TryCompute(
                world.EntityManager,
                out _,
                out SimulationStateHashError error);

            Assert.That(succeeded, Is.False);
            Assert.That(
                error.Code,
                Is.EqualTo(SimulationStateHashErrorCode.SingletonDuplicated));
        }

        /// <summary>
        /// 固定Tick設定の0をハッシュへ含めず、Rate Managerが使用する正規化前の不正状態を見逃さないことを保証します。
        /// </summary>
        [Test]
        public void CoreHasher_RejectsUnnormalizedFixedTickConfiguration()
        {
            using World world = CreateCoreStateWorld();
            EntityQuery configurationQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<FixedTickConfiguration>());
            try
            {
                Entity configurationEntity = configurationQuery.GetSingletonEntity();
                world.EntityManager.SetComponentData(
                    configurationEntity,
                    new FixedTickConfiguration
                    {
                        TicksPerSecond = 0,
                        MaximumTicksPerFrame =
                            FixedTickConfiguration.DefaultMaximumTicksPerFrame
                    });
            }
            finally
            {
                configurationQuery.Dispose();
            }

            bool succeeded = SimulationCoreStateHasher.TryCompute(
                world.EntityManager,
                out _,
                out SimulationStateHashError error);

            Assert.That(succeeded, Is.False);
            Assert.That(
                error.Code,
                Is.EqualTo(SimulationStateHashErrorCode.CoreStateIsInvalid));
        }

        /// <summary>
        /// Queueへ未処理Commandが残るTick途中ではハッシュを返さず、入力と確定状態を混ぜないことを保証します。
        /// </summary>
        [Test]
        public void CoreHasher_RejectsPendingCommand()
        {
            using World world = CreateCoreStateWorld();
            bool accepted = SimulationCommandQueue.TryEnqueue(
                world.EntityManager,
                new StateHashTestCreateCommand
                {
                    PersistentId = 1,
                    InitialValue = 10
                },
                out _,
                out _);

            bool succeeded = SimulationCoreStateHasher.TryCompute(
                world.EntityManager,
                out _,
                out SimulationStateHashError error);

            Assert.That(accepted, Is.True);
            Assert.That(succeeded, Is.False);
            Assert.That(
                error.Code,
                Is.EqualTo(SimulationStateHashErrorCode.PendingCommandExists));
        }

        /// <summary>
        /// 定義入力順が異なってもGUID順へ正規化された同じカタログなら同じ共通ハッシュになることを保証します。
        /// </summary>
        [Test]
        public void CoreHasher_DefinitionCatalogInputOrderDoesNotChangeHash()
        {
            DefinitionBuildEntry firstDefinition = CreateDefinition(0x10, DefinitionKind.Item);
            DefinitionBuildEntry secondDefinition =
                CreateDefinition(0x20, DefinitionKind.Equipment);
            BlobAssetReference<DefinitionCatalogBlob> firstCatalog = default;
            BlobAssetReference<DefinitionCatalogBlob> secondCatalog = default;

            try
            {
                Assert.That(
                    DefinitionCatalogBuilder.TryBuild(
                        new[] { firstDefinition, secondDefinition },
                        Array.Empty<DefinitionBuildReference>(),
                        out firstCatalog,
                        out _),
                    Is.True);
                Assert.That(
                    DefinitionCatalogBuilder.TryBuild(
                        new[] { secondDefinition, firstDefinition },
                        Array.Empty<DefinitionBuildReference>(),
                        out secondCatalog,
                        out _),
                    Is.True);

                using World firstWorld = CreateCoreStateWorld();
                using World secondWorld = CreateCoreStateWorld();
                AddDefinitionCatalog(firstWorld, firstCatalog);
                AddDefinitionCatalog(secondWorld, secondCatalog);

                Assert.That(
                    SimulationCoreStateHasher.TryCompute(
                        firstWorld.EntityManager,
                        out SimulationStateHash firstHash,
                        out _),
                    Is.True);
                Assert.That(
                    SimulationCoreStateHasher.TryCompute(
                        secondWorld.EntityManager,
                        out SimulationStateHash secondHash,
                        out _),
                    Is.True);
                Assert.That(firstHash, Is.EqualTo(secondHash));
            }
            finally
            {
                if (firstCatalog.IsCreated)
                {
                    firstCatalog.Dispose();
                }

                if (secondCatalog.IsCreated)
                {
                    secondCatalog.Dispose();
                }
            }
        }

        /// <summary>
        /// 同じCommand列とTick数なら、Entity IndexをずらしRunとScheduleParallelを切り替えても同じハッシュになることを保証します。
        /// </summary>
        [Test]
        public void SameCommandsAndTicks_ProduceSameHashAcrossJobConditions()
        {
            using World sequentialWorld = CreateDeterminismWorld(
                useParallelJob: false,
                unrelatedEntityCount: 0,
                out FarmwareFixedTickSimulationSystemGroup sequentialFixedTickGroup);
            using World parallelWorld = CreateDeterminismWorld(
                useParallelJob: true,
                unrelatedEntityCount: 37,
                out FarmwareFixedTickSimulationSystemGroup parallelFixedTickGroup);

            EnqueueSameCommandSequence(sequentialWorld);
            EnqueueSameCommandSequence(parallelWorld);
            UpdateFixedTickGroup(sequentialWorld, sequentialFixedTickGroup, 0.1f);
            UpdateFixedTickGroup(parallelWorld, parallelFixedTickGroup, 0.1f);

            Assert.That(
                TryComputeTestWorldHash(sequentialWorld, out SimulationStateHash sequentialHash),
                Is.True);
            Assert.That(
                TryComputeTestWorldHash(parallelWorld, out SimulationStateHash parallelHash),
                Is.True);
            Assert.That(sequentialHash, Is.EqualTo(parallelHash));
        }

        /// <summary>
        /// 永続IDに対応する論理値を1つ変更すると、同じEntity件数でも状態ハッシュが変わることを保証します。
        /// </summary>
        [Test]
        public void ChangedAuthoritativeValue_ChangesStateHash()
        {
            using World world = CreateDeterminismWorld(
                useParallelJob: false,
                unrelatedEntityCount: 0,
                out FarmwareFixedTickSimulationSystemGroup fixedTickGroup);
            EnqueueSameCommandSequence(world);
            UpdateFixedTickGroup(world, fixedTickGroup, 0.1f);

            Assert.That(
                TryComputeTestWorldHash(world, out SimulationStateHash beforeHash),
                Is.True);
            EntityQuery query = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<StateHashTestValue>(),
                ComponentType.ReadOnly<PersistentObjectId>());
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            try
            {
                StateHashTestValue changedValue =
                    world.EntityManager.GetComponentData<StateHashTestValue>(entities[0]);
                changedValue.Value++;
                world.EntityManager.SetComponentData(entities[0], changedValue);
            }
            finally
            {
                entities.Dispose();
                query.Dispose();
            }

            Assert.That(
                TryComputeTestWorldHash(world, out SimulationStateHash afterHash),
                Is.True);
            Assert.That(afterHash, Is.Not.EqualTo(beforeHash));
        }

        /// <summary>
        /// 同じ永続IDを持つ複数Entityを拒否し、並び替え後も一意に識別できない状態を比較へ使わないことを保証します。
        /// </summary>
        [Test]
        public void FeatureHasher_RejectsDuplicatedPersistentIds()
        {
            using World world = CreateCoreStateWorld();
            AddTestValueEntity(world, persistentId: 5, value: 10);
            AddTestValueEntity(world, persistentId: 5, value: 20);

            bool succeeded = TryComputeTestWorldHash(world, out _);

            Assert.That(succeeded, Is.False);
        }

        /// <summary>
        /// Builderの各整数書き込みとセクション境界から、比較用の小さな状態ハッシュを作成します。
        /// </summary>
        /// <param name="lastValue">変更検出を確認する末尾の32bit値です。</param>
        /// <returns>指定した正規データ列の128bit値です。</returns>
        private static SimulationStateHash BuildPrimitiveHash(int lastValue)
        {
            SimulationStateHashBuilder builder =
                new SimulationStateHashBuilder(SimulationStateHashSchema.CurrentVersion);
            builder.BeginSection(10);
            builder.WriteByte(0x7f);
            builder.WriteUInt16(0x1234);
            builder.WriteUInt32(0x89abcdef);
            builder.WriteUInt64(0x0123456789abcdef);
            builder.BeginSection(20);
            builder.WriteInt32(lastValue);
            return builder.Complete();
        }

        /// <summary>
        /// 固定Tick、永続IDアロケーター、空Command Queueを持つ最小の安定状態Worldを作成します。
        /// </summary>
        /// <returns>呼び出し側がusingで破棄するテストWorldです。</returns>
        private static World CreateCoreStateWorld()
        {
            World world = new World("Simulation State Hash Core Test");
            Entity timeEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(
                timeEntity,
                FixedTickConfiguration.CreateDefault());
            world.EntityManager.AddComponentData(
                timeEntity,
                new SimulationTickState
                {
                    CurrentTick = 0
                });
            world.EntityManager.AddComponentData(
                timeEntity,
                new SimulationPauseState
                {
                    Value = 0
                });

            Entity allocatorEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(
                allocatorEntity,
                new PersistentIdAllocatorState
                {
                    NextId = 4,
                    ExhaustedValue = 0
                });

            SystemHandle queueInitializationSystem =
                world.GetOrCreateSystem<SimulationCommandQueueInitializationSystem>();
            queueInitializationSystem.Update(world.Unmanaged);
            return world;
        }

        /// <summary>
        /// 共通状態Worldへ定義カタログSingletonを追加します。
        /// Blob Asset自体のDispose責任は、このメソッドではなく作成したテストが持ちます。
        /// </summary>
        /// <param name="world">カタログを公開するテストWorldです。</param>
        /// <param name="catalog">テスト終了まで有効なBlob Asset参照です。</param>
        private static void AddDefinitionCatalog(
            World world,
            BlobAssetReference<DefinitionCatalogBlob> catalog)
        {
            Entity catalogEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(
                catalogEntity,
                new DefinitionCatalog
                {
                    Value = catalog
                });
        }

        /// <summary>
        /// 下位64bitで順序を確認できる参照なし定義を作成します。
        /// </summary>
        /// <param name="guidLow">0以外の定義GUID下位64bitです。</param>
        /// <param name="kind">定義の分類です。</param>
        /// <returns>参照範囲が空の定義構築入力です。</returns>
        private static DefinitionBuildEntry CreateDefinition(
            ulong guidLow,
            DefinitionKind kind)
        {
            return new DefinitionBuildEntry
            {
                Guid = new DefinitionGuid(0, guidLow),
                Kind = kind,
                ReferenceStartIndex = 0,
                ReferenceCount = 0
            };
        }

        /// <summary>
        /// Command処理とGameplay Jobを固定Tick順へ接続し、任意数の無関係Entityで実行時配置を変えたWorldを作成します。
        /// </summary>
        /// <param name="useParallelJob">Gameplay計算をScheduleParallelで実行する場合はtrueです。</param>
        /// <param name="unrelatedEntityCount">Command受付前に作成し、Entity Indexをずらす無関係Entity数です。</param>
        /// <param name="fixedTickGroup">描画フレーム相当の更新を呼ぶ親Groupです。</param>
        /// <returns>呼び出し側がusingで破棄するテストWorldです。</returns>
        private static World CreateDeterminismWorld(
            bool useParallelJob,
            int unrelatedEntityCount,
            out FarmwareFixedTickSimulationSystemGroup fixedTickGroup)
        {
            World world = CreateCoreStateWorld();
            for (int entityIndex = 0;
                 entityIndex < unrelatedEntityCount;
                 entityIndex++)
            {
                Entity unrelatedEntity = world.EntityManager.CreateEntity();
                world.EntityManager.AddComponentData(
                    unrelatedEntity,
                    new StateHashTestUnrelatedData
                    {
                        Value = entityIndex
                    });
            }

            Entity modeEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(
                modeEntity,
                new StateHashTestExecutionMode
                {
                    UseParallelJob = useParallelJob ? (byte)1 : (byte)0
                });

            fixedTickGroup =
                world.GetOrCreateSystemManaged<FarmwareFixedTickSimulationSystemGroup>();
            SystemHandle tickAdvanceSystem =
                world.GetOrCreateSystem<SimulationTickAdvanceSystem>();
            FarmwareCommandProcessingSystemGroup commandGroup =
                world.GetOrCreateSystemManaged<FarmwareCommandProcessingSystemGroup>();
            FarmwareCommandValidationSystemGroup validationGroup =
                world.GetOrCreateSystemManaged<FarmwareCommandValidationSystemGroup>();
            SystemHandle validationSystem =
                world.GetOrCreateSystem<StateHashTestValidationSystem>();
            SystemHandle validationEndSystem =
                world.GetOrCreateSystem<SimulationCommandValidationEndSystem>();
            FarmwareCommandCommitSystemGroup commitGroup =
                world.GetOrCreateSystemManaged<FarmwareCommandCommitSystemGroup>();
            SystemHandle commitSystem =
                world.GetOrCreateSystem<StateHashTestCommitSystem>();
            SimulationCommandEntityCommandBufferSystem commandBufferSystem =
                world.GetOrCreateSystemManaged<SimulationCommandEntityCommandBufferSystem>();
            SystemHandle completionSystem =
                world.GetOrCreateSystem<SimulationCommandCompletionSystem>();
            FarmwareFixedTickGameplaySystemGroup gameplayGroup =
                world.GetOrCreateSystemManaged<FarmwareFixedTickGameplaySystemGroup>();
            SystemHandle gameplaySystem =
                world.GetOrCreateSystem<StateHashTestGameplaySystem>();

            validationGroup.AddSystemToUpdateList(validationSystem);
            validationGroup.SortSystems();
            commitGroup.AddSystemToUpdateList(commitSystem);
            commitGroup.SortSystems();
            commandGroup.AddSystemToUpdateList(validationGroup);
            commandGroup.AddSystemToUpdateList(validationEndSystem);
            commandGroup.AddSystemToUpdateList(commitGroup);
            commandGroup.AddSystemToUpdateList(commandBufferSystem);
            commandGroup.AddSystemToUpdateList(completionSystem);
            commandGroup.SortSystems();
            gameplayGroup.AddSystemToUpdateList(gameplaySystem);
            gameplayGroup.SortSystems();
            fixedTickGroup.AddSystemToUpdateList(tickAdvanceSystem);
            fixedTickGroup.AddSystemToUpdateList(commandGroup);
            fixedTickGroup.AddSystemToUpdateList(gameplayGroup);
            fixedTickGroup.SortSystems();
            return world;
        }

        /// <summary>
        /// 2つのWorldへ同じ受付順、永続ID、初期値を持つCommand列を追加します。
        /// ID順とは異なる受付順にして、後段の正規化が受付順へ依存しないことも確認します。
        /// </summary>
        /// <param name="world">空Command Queueを持つ比較対象Worldです。</param>
        private static void EnqueueSameCommandSequence(World world)
        {
            StateHashTestCreateCommand[] commands =
            {
                new StateHashTestCreateCommand
                {
                    PersistentId = 3,
                    InitialValue = 30
                },
                new StateHashTestCreateCommand
                {
                    PersistentId = 1,
                    InitialValue = 10
                },
                new StateHashTestCreateCommand
                {
                    PersistentId = 2,
                    InitialValue = 20
                }
            };

            for (int commandIndex = 0; commandIndex < commands.Length; commandIndex++)
            {
                bool accepted = SimulationCommandQueue.TryEnqueue(
                    world.EntityManager,
                    commands[commandIndex],
                    out _,
                    out ulong receiptNumber);
                Assert.That(accepted, Is.True);
                Assert.That(receiptNumber, Is.EqualTo((ulong)commandIndex + 1));
            }
        }

        /// <summary>
        /// 描画フレーム時間を一時設定し、固定Tick Groupを更新してCommandとGameplayを同じTickで完了します。
        /// </summary>
        /// <param name="world">時間を所有するテストWorldです。</param>
        /// <param name="fixedTickGroup">更新するFarmware固定Tick Groupです。</param>
        /// <param name="frameDeltaTime">今回与える描画フレーム秒数です。</param>
        private static void UpdateFixedTickGroup(
            World world,
            FarmwareFixedTickSimulationSystemGroup fixedTickGroup,
            float frameDeltaTime)
        {
            TimeData currentTime = world.Time;
            world.PushTime(new TimeData(
                currentTime.ElapsedTime + frameDeltaTime,
                frameDeltaTime));
            fixedTickGroup.Update();
            world.PopTime();
        }

        /// <summary>
        /// 共通状態とテスト用永続Entityを正規化し、完成した状態ハッシュを計算します。
        /// </summary>
        /// <param name="world">固定Tick完了後の比較対象Worldです。</param>
        /// <param name="hash">成功時に共通状態とテスト値を含む128bit値を受け取ります。</param>
        /// <returns>共通状態が有効で永続IDが一意ならtrueです。</returns>
        private static bool TryComputeTestWorldHash(
            World world,
            out SimulationStateHash hash)
        {
            SimulationStateHashBuilder builder =
                new SimulationStateHashBuilder(SimulationStateHashSchema.CurrentVersion);
            if (!SimulationCoreStateHasher.TryAppendTo(
                    world.EntityManager,
                    ref builder,
                    out _))
            {
                hash = default;
                return false;
            }

            EntityQuery query = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PersistentObjectId>(),
                ComponentType.ReadOnly<StateHashTestValue>());
            NativeArray<PersistentObjectId> ids = default;
            NativeArray<StateHashTestValue> values = default;
            NativeArray<StateHashTestRecord> records = default;
            try
            {
                ids = query.ToComponentDataArray<PersistentObjectId>(Allocator.Temp);
                values = query.ToComponentDataArray<StateHashTestValue>(Allocator.Temp);
                records = new NativeArray<StateHashTestRecord>(
                    ids.Length,
                    Allocator.Temp);
                for (int recordIndex = 0; recordIndex < records.Length; recordIndex++)
                {
                    records[recordIndex] = new StateHashTestRecord
                    {
                        PersistentId = ids[recordIndex].Value,
                        Value = values[recordIndex].Value
                    };
                }

                records.Sort();
                for (int recordIndex = 0; recordIndex < records.Length; recordIndex++)
                {
                    bool idIsInvalid = records[recordIndex].PersistentId
                        == PersistentObjectId.InvalidValue;
                    bool idIsDuplicated = recordIndex > 0
                        && records[recordIndex - 1].PersistentId
                            == records[recordIndex].PersistentId;
                    if (idIsInvalid || idIsDuplicated)
                    {
                        hash = default;
                        return false;
                    }
                }

                builder.BeginSection(TestValueSectionId);
                builder.WriteInt32(records.Length);
                for (int recordIndex = 0; recordIndex < records.Length; recordIndex++)
                {
                    builder.WriteUInt64(records[recordIndex].PersistentId);
                    builder.WriteInt32(records[recordIndex].Value);
                }

                hash = builder.Complete();
                return true;
            }
            finally
            {
                if (records.IsCreated)
                {
                    records.Dispose();
                }

                if (values.IsCreated)
                {
                    values.Dispose();
                }

                if (ids.IsCreated)
                {
                    ids.Dispose();
                }

                query.Dispose();
            }
        }

        /// <summary>
        /// 重複ID検証用に永続IDと論理値を同じEntityへ追加します。
        /// </summary>
        /// <param name="world">追加先のテストWorldです。</param>
        /// <param name="persistentId">Entityを論理的に識別する値です。</param>
        /// <param name="value">状態ハッシュへ含める整数値です。</param>
        private static void AddTestValueEntity(
            World world,
            ulong persistentId,
            int value)
        {
            Entity entity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(
                entity,
                new PersistentObjectId
                {
                    Value = persistentId
                });
            world.EntityManager.AddComponentData(
                entity,
                new StateHashTestValue
                {
                    Value = value
                });
        }

        /// <summary>
        /// テスト用Entityを永続ID順へ並べるための一時レコードです。
        /// NativeArray.Sortが同じ規則を使えるよう、IDだけで全順序を定義します。
        /// </summary>
        private struct StateHashTestRecord : IComparable<StateHashTestRecord>
        {
            /// <summary>Entity Indexの代わりに並びを固定する1以上の永続IDです。</summary>
            public ulong PersistentId;

            /// <summary>該当永続IDが持つ論理Simulation値です。</summary>
            public int Value;

            /// <summary>
            /// 永続IDの数値昇順を返し、Entity生成順やECS Chunk順を比較結果から除外します。
            /// </summary>
            /// <param name="other">比較するもう一つの一時レコードです。</param>
            /// <returns>このIDが前なら負、同じなら0、後なら正の値です。</returns>
            public readonly int CompareTo(StateHashTestRecord other)
            {
                return PersistentId.CompareTo(other.PersistentId);
            }
        }
    }
}
