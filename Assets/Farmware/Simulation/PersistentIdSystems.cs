// このファイルは、新しいECS Worldへ永続IDアロケーターのSingletonを1件だけ準備します。
// 実際のID発行はPersistentIdAllocatorが担当し、このSystemは初期状態の所有場所だけを作成します。

using Unity.Entities;

namespace Farmware.Simulation
{
    /// <summary>
    /// World開始時にPersistentIdAllocatorState Singletonが未作成なら、ID 1から始まる初期状態を作るSystemです。
    /// セーブロード側が先に復元状態を作成した場合は上書きせず、その状態を引き続き使用します。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareInitializationSystemGroup))]
    public partial struct PersistentIdInitializationSystem : ISystem
    {
        /// <summary>
        /// 既存のアロケーター状態を数えるQueryです。System作成からWorld破棄まで保持し、Unityが破棄を管理します。
        /// </summary>
        private EntityQuery allocatorStateQuery;

        /// <summary>
        /// System作成時にアロケーター状態用Queryを準備します。Entityは初回更新時に必要な場合だけ作成します。
        /// </summary>
        /// <param name="state">Queryを作成するWorldのSystem状態です。</param>
        public void OnCreate(ref SystemState state)
        {
            allocatorStateQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<PersistentIdAllocatorState>());
        }

        /// <summary>
        /// 初回更新時に既存状態の有無を確認し、未作成なら新規ワールド用Singletonを追加して自身を停止します。
        /// </summary>
        /// <param name="state">Entity作成とSystem停止に使用する状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            if (allocatorStateQuery.IsEmptyIgnoreFilter)
            {
                Entity allocatorEntity = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(
                    allocatorEntity,
                    PersistentIdAllocatorState.CreateInitial());
            }

            // 初期化または復元済み状態の確認はWorld開始時だけ必要なので、毎フレームのQuery実行を避けます。
            state.Enabled = false;
        }

        /// <summary>
        /// World破棄時に呼ばれます。QueryとEntityはWorldが所有するため、個別の解放処理はありません。
        /// </summary>
        /// <param name="state">破棄されるSystem状態です。Entityの変更には使用しません。</param>
        public void OnDestroy(ref SystemState state)
        {
        }
    }
}
