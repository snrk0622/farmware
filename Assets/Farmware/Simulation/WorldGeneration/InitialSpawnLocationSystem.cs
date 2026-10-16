// このファイルは、通常生成された地形と資源から新規ワールドの初期スポーン地点を一度だけ探索してECSへ記録します。
// 重い広域探索はBurst Jobへ渡し、完了後のSingleton作成だけをメインスレッドで行うことでUnity Editorの応答停止を避けます。

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace Farmware.WorldGeneration
{
    /// <summary>
    /// JobからSystemへ、初期スポーン候補を発見できたかと建設可能矩形を返す一時データです。
    /// セーブ対象ではなく、探索完了時にInitialSpawnLocationへ変換した後で破棄します。
    /// </summary>
    internal struct InitialSpawnSearchResult
    {
        /// <summary>探索範囲内に条件を満たす候補が見つかった場合は1、見つからなかった場合は0です。</summary>
        public byte Found;

        /// <summary>Foundが1の場合に、通常地形と必須資源の条件を満たした建設可能矩形です。</summary>
        public EquipmentBuildableRegion Region;
    }

    /// <summary>
    /// ワールド設定の準備後に、木・石・水が近い12×8建設可能領域を非同期で探します。
    /// 探索結果をInitialSpawnSearchStatusへ記録し、成功時はInitialSpawnLocation Singletonも1つだけ作成します。
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct InitialSpawnLocationSystem : ISystem
    {
        // 0は未開始、1はJob実行中、2は完了です。毎フレーム同じ広域探索を再実行しないために保持します。
        private byte searchState;

        // Jobの完了確認とWorld破棄時の安全な同期に使います。探索結果以外のECSデータへは依存しません。
        private JobHandle searchHandle;

        // Jobが書き、Systemが完了後に読む1件分の結果です。SystemがPersistent確保と破棄の責任を持ちます。
        private NativeReference<InitialSpawnSearchResult> searchResult;

        // 探索中・成功・失敗をPresentationへ伝えるSingleton Entityです。Systemが作成し、Worldが破棄まで所有します。
        private Entity searchStatusEntity;

        /// <summary>
        /// System作成時に呼ばれ、待機状態Singletonを作成してから地形設定と資源設定がそろうまで探索開始を待機します。
        /// </summary>
        /// <param name="state">Systemの更新条件、依存関係、EntityManagerへアクセスする状態です。</param>
        public void OnCreate(ref SystemState state)
        {
            searchStatusEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(
                searchStatusEntity,
                new InitialSpawnSearchStatus
                {
                    Phase = InitialSpawnSearchPhase.WaitingForSettings
                });
            state.RequireForUpdate<WorldGenerationSettings>();
            state.RequireForUpdate<ResourceGenerationSettings>();
            searchState = 0;
            searchHandle = default;
            searchResult = default;
        }

        /// <summary>
        /// 初回更新で探索Jobを開始し、後続フレームで成功時の初期スポーンまたは失敗状態を記録します。
        /// 完了前にメインスレッドで待機しないため、広い探索範囲でもGameビューとEditor操作を止めません。
        /// </summary>
        /// <param name="state">Job依存関係と、結果Entityを作成するEntityManagerを保持する状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            // SubSceneの再読み込みなどで既存Singletonがある場合は、二重作成せずこのSystemを停止します。
            if (SystemAPI.TryGetSingleton<InitialSpawnLocation>(out _))
            {
                // 探索Jobが結果を書き込んでいる途中でも、書き込み完了前のNativeReferenceを破棄しないよう同期します。
                CompleteSearchJobIfRunning();
                DisposeSearchResultIfCreated();
                searchState = 2;
                SetSearchPhase(ref state, InitialSpawnSearchPhase.Succeeded);
                state.Enabled = false;
                return;
            }

            if (searchState == 0)
            {
                searchResult = new NativeReference<InitialSpawnSearchResult>(Allocator.Persistent);
                FindInitialSpawnJob searchJob = new FindInitialSpawnJob
                {
                    GenerationSettings = SystemAPI.GetSingleton<WorldGenerationSettings>(),
                    ResourceSettings = SystemAPI.GetSingleton<ResourceGenerationSettings>(),
                    Result = searchResult
                };

                // 広域ノイズ生成をWorker Threadで実行し、このフレームでは結果を待たずに制御を返します。
                searchHandle = searchJob.Schedule(state.Dependency);
                state.Dependency = searchHandle;
                searchState = 1;
                SetSearchPhase(ref state, InitialSpawnSearchPhase.Searching);
                return;
            }

            if (searchState != 1 || !searchHandle.IsCompleted)
            {
                return;
            }

            // IsCompleted確認後なので通常は即時終了し、結果を読む前のJob安全性だけを確定します。
            searchHandle.Complete();
            InitialSpawnSearchResult completedResult = searchResult.Value;
            DisposeSearchResultIfCreated();
            searchState = 2;

            if (completedResult.Found == 0)
            {
                SetSearchPhase(ref state, InitialSpawnSearchPhase.Failed);
                Debug.LogError(
                    "初期スポーン地点を探索範囲内で発見できませんでした。カメラはScene保存位置に留まり、その位置からチャンク読み込みを開始します。");
                state.Enabled = false;
                return;
            }

            Entity locationEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(
                locationEntity,
                InitialSpawnLocation.FromRegion(completedResult.Region));
            SetSearchPhase(ref state, InitialSpawnSearchPhase.Succeeded);
            state.Enabled = false;
        }

        /// <summary>
        /// World破棄時に呼ばれ、探索中ならJobを完了させてPersistentな結果領域を必ず解放します。
        /// </summary>
        /// <param name="state">破棄されるSystem状態です。Entityの追加は行いません。</param>
        public void OnDestroy(ref SystemState state)
        {
            CompleteSearchJobIfRunning();
            DisposeSearchResultIfCreated();
        }

        /// <summary>
        /// 探索Jobが実行中の場合だけ完了を待ち、結果領域をメインスレッドから安全に読み取り・破棄できる状態へ戻します。
        /// 既存地点の出現またはWorld破棄で探索を途中終了するときに呼び、完了まで一時的にメインスレッドを待機させます。
        /// </summary>
        private void CompleteSearchJobIfRunning()
        {
            if (searchState == 1)
            {
                searchHandle.Complete();
            }
        }

        /// <summary>
        /// NativeReferenceが確保済みの場合だけ破棄し、二重Disposeを防ぎます。
        /// </summary>
        private void DisposeSearchResultIfCreated()
        {
            if (searchResult.IsCreated)
            {
                searchResult.Dispose();
            }

            searchResult = default;
        }

        /// <summary>
        /// Systemが所有する状態Singletonを更新し、Presentationへ探索の開始または終了を通知します。
        /// </summary>
        /// <param name="state">状態Entityへ書き込むためのEntityManagerを保持するSystem状態です。</param>
        /// <param name="phase">新しく記録する探索段階です。</param>
        private void SetSearchPhase(ref SystemState state, InitialSpawnSearchPhase phase)
        {
            state.EntityManager.SetComponentData(
                searchStatusEntity,
                new InitialSpawnSearchStatus { Phase = phase });
        }

        /// <summary>
        /// 通常生成器だけを使って初期建設領域と周辺必須資源を探索するBurst Jobです。
        /// EntityやTilemapへ触れないため、メインスレッド外でも生成結果を変えずに実行できます。
        /// </summary>
        [BurstCompile]
        private struct FindInitialSpawnJob : IJob
        {
            /// <summary>地形を座標から再生成するため、Singletonからコピーした読み取り専用設定です。</summary>
            public WorldGenerationSettings GenerationSettings;

            /// <summary>木・石の通常配置を候補条件へ含めるため、Singletonからコピーした読み取り専用設定です。</summary>
            public ResourceGenerationSettings ResourceSettings;

            /// <summary>探索終了時に1回だけ書き込む結果です。所有と破棄は外側のSystemが担当します。</summary>
            public NativeReference<InitialSpawnSearchResult> Result;

            /// <summary>
            /// Worker Threadで呼ばれ、原点に近く木・石・水がそろう通常生成領域を探索します。
            /// </summary>
            public void Execute()
            {
                EquipmentBuildableRegionSearchSettings searchSettings =
                    EquipmentBuildableRegionSearchSettings.CreateInitialLocationDefault();
                EquipmentBuildableRegionResourceRequirements resourceRequirements =
                    EquipmentBuildableRegionResourceRequirements.CreateDefault();
                bool found = EquipmentBuildableRegionLocator.TryFindNearestWithResources(
                    GenerationSettings,
                    ResourceSettings,
                    searchSettings,
                    resourceRequirements,
                    out EquipmentBuildableRegion region);

                Result.Value = new InitialSpawnSearchResult
                {
                    Found = found ? (byte)1 : (byte)0,
                    Region = region
                };
            }
        }
    }
}
