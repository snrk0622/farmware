// このファイルは、保存基盤接続前の既存SceneへWORLD-01Dのワールド／プレイヤー境界を準備します。
// SAVE-01で読込済みIDが供給された場合は上書きせず、欠落が全項目に及ぶ開発用Worldだけを固定値で初期化します。

using Unity.Entities;

namespace Farmware.Simulation
{
    /// <summary>
    /// 新しい開発用ECS WorldへWorldId、ローカルPlayerId、Input用Contextを1組だけ作成する初期化Systemです。
    /// SampleSceneの既存起動経路を維持し、保存読込が部分的または重複した不正状態は推測で修復しません。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareInitializationSystemGroup))]
    [UpdateAfter(typeof(SimulationTimeInitializationSystem))]
    public partial struct WorldSessionInitializationSystem : ISystem
    {
        /// <summary>
        /// System作成時に呼ばれます。既存状態の有無は最初の更新でまとめて確認します。
        /// </summary>
        /// <param name="state">WorldのEntityManagerとSystem有効状態へアクセスする状態です。</param>
        public void OnCreate(ref SystemState state)
        {
        }

        /// <summary>
        /// World／Player／Local Contextがすべて未作成なら、固定の開発用WorldとPlayer 1を作成します。
        /// いずれかが既に存在する場合はSAVE-01等の外部準備を尊重し、値を追加・上書きしません。
        /// </summary>
        /// <param name="state">Component件数確認とEntity作成に使用するSystem状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            EntityQuery worldIdentityQuery = state.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<WorldSessionIdentity>());
            EntityQuery playerIdentityQuery = state.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerIdentity>());
            EntityQuery localPlayerContextQuery = state.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<LocalPlayerContext>());
            try
            {
                bool sessionIsCompletelyAbsent =
                    worldIdentityQuery.CalculateEntityCount() == 0
                    && playerIdentityQuery.CalculateEntityCount() == 0
                    && localPlayerContextQuery.CalculateEntityCount() == 0;
                if (sessionIsCompletelyAbsent)
                {
                    PlayerId localPlayerId = new PlayerId(1);

                    Entity worldEntity = state.EntityManager.CreateEntity();
                    state.EntityManager.AddComponentData(
                        worldEntity,
                        new WorldSessionIdentity
                        {
                            Id = WorldId.DevelopmentDefault
                        });

                    Entity playerEntity = state.EntityManager.CreateEntity();
                    state.EntityManager.AddComponentData(
                        playerEntity,
                        new PlayerIdentity
                        {
                            Id = localPlayerId
                        });

                    Entity localContextEntity = state.EntityManager.CreateEntity();
                    state.EntityManager.AddComponentData(
                        localContextEntity,
                        new LocalPlayerContext
                        {
                            PlayerId = localPlayerId
                        });
                }
            }
            finally
            {
                // 初期化時だけ使用するQueryは、開発用状態を作った場合も既存状態を尊重した場合も解放します。
                worldIdentityQuery.Dispose();
                playerIdentityQuery.Dispose();
                localPlayerContextQuery.Dispose();
            }

            // IDはWorld寿命中に作り直さないため、最初の確認後はこのSystemを停止します。
            state.Enabled = false;
        }

        /// <summary>
        /// World破棄時に呼ばれます。作成したEntityの寿命はWorldが所有するため、個別解放はありません。
        /// </summary>
        /// <param name="state">破棄されるSystem状態です。Entity変更には使用しません。</param>
        public void OnDestroy(ref SystemState state)
        {
        }
    }
}
