// このファイルは、新規ECS Worldへ地形Seedとは独立したAnimalPopulationSeedを一度だけ準備します。
// セーブ読込側が先にSingletonを復元した場合は上書きせず、同じ地域抽選列と動物配置を継続します。

using System;
using Unity.Entities;

namespace Farmware.Simulation.Animals
{
    /// <summary>
    /// 新規ワールド用の個体群SeedをOSが提供するGuidから作成するInitialization Systemです。
    /// 地形SeedやUnityEngine.Randomへ依存しないため、同じ地形Seedから別ワールドを作っても動物配置を固定しません。
    /// </summary>
    [UpdateInGroup(typeof(FarmwareInitializationSystemGroup))]
    [UpdateAfter(typeof(PersistentIdInitializationSystem))]
    public partial struct AnimalPopulationSeedInitializationSystem : ISystem
    {
        /// <summary>既存またはロード済みのAnimalPopulationSeedを数える再利用Queryです。</summary>
        private EntityQuery populationSeedQuery;

        /// <summary>
        /// System作成時にSeed検索用Queryを準備します。Seed自体はロード処理へ上書き猶予を残すため最初の更新で作ります。
        /// </summary>
        /// <param name="state">対象WorldのEntityManagerへアクセスするSystem状態です。</param>
        public void OnCreate(ref SystemState state)
        {
            populationSeedQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<AnimalPopulationSeed>());
        }

        /// <summary>
        /// Seedが未作成の新規Worldだけへ非0の値を追加し、以後の毎フレーム更新を停止します。
        /// </summary>
        /// <param name="state">Singleton Entityの作成とSystem停止に使用する状態です。</param>
        public void OnUpdate(ref SystemState state)
        {
            if (populationSeedQuery.IsEmptyIgnoreFilter)
            {
                Entity seedEntity = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(
                    seedEntity,
                    new AnimalPopulationSeed
                    {
                        Value = CreateNewWorldSeed()
                    });
            }

            // 新規作成または復元済みの確認はWorld寿命中に一度だけ必要です。
            state.Enabled = false;
        }

        /// <summary>
        /// World破棄時に呼ばれます。QueryとSingleton Entityの所有権はWorldにあるため、個別解放はありません。
        /// </summary>
        /// <param name="state">破棄されるSystem状態です。データ変更には使用しません。</param>
        public void OnDestroy(ref SystemState state)
        {
        }

        /// <summary>
        /// OSの乱数源を利用するGuidから64bitを取り出し、予約値0だけを固定値へ置き換えます。
        /// </summary>
        /// <returns>地形Seedと無関係な、新規ワールド用の非0 Seedです。</returns>
        private static ulong CreateNewWorldSeed()
        {
            // managed配列の確保はWorld作成時の1回だけです。固定Tickや大量個体のHot Pathでは実行しません。
            byte[] guidBytes = Guid.NewGuid().ToByteArray();
            ulong seed = BitConverter.ToUInt64(guidBytes, 0);
            return seed == AnimalPopulationSeed.InvalidValue
                ? 0xA0761D6478BD642FUL
                : seed;
        }
    }
}
