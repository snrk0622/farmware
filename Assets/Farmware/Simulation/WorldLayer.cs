// このファイルは、Farmwareの全機能が共有する固定ワールドレイヤーIDを定義します。
// 座標、チャンク、個体、施設、保存キーはWORLD-01C以降でこのIDを組み込み、文字列やTransformのZ座標から層を推測しません。

namespace Farmware.Simulation
{
    /// <summary>
    /// 同じ絶対X/Yへ重なる、地上・海中・深海の固定3レイヤーを表す保存用の安定IDです。
    /// Simulation、保存、Presentationの境界で共有し、既存値の番号は将来も変更または再利用しません。
    /// </summary>
    public enum WorldLayer : byte
    {
        /// <summary>
        /// 未設定または壊れた入力を表す予約値です。有効なセル、チャンク、個体の位置には使用しません。
        /// </summary>
        None = 0,

        /// <summary>
        /// 陸地、水面、地上建築が存在する地上レイヤーです。固定保存IDは1です。
        /// </summary>
        Surface = 1,

        /// <summary>
        /// 通常の自然水域と、地上から1階層掘って造成する水域が存在する海中レイヤーです。固定保存IDは2です。
        /// </summary>
        Underwater = 2,

        /// <summary>
        /// 広い自然水域の沖合と、海中からさらに1階層掘って造成する水域が存在する深海レイヤーです。固定保存IDは3です。
        /// </summary>
        DeepSea = 3,
    }

    /// <summary>
    /// WorldLayerの有効性と上下の隣接関係を、保存IDの偶然の並びへ依存せず判定します。
    /// WORLD-01Bの水域遷移と、後続の座標・チャンク・保存実装が同じ固定3層の関係を共有するために使用します。
    /// </summary>
    public static class WorldLayerUtility
    {
        /// <summary>
        /// 指定値がゲーム内で使用できる固定3レイヤーのいずれかを表すか判定します。
        /// </summary>
        /// <param name="layer">保存値または実行時データから読み取ったレイヤーIDです。</param>
        /// <returns>Surface、Underwater、DeepSeaならtrue、Noneや未知値ならfalseです。</returns>
        public static bool IsPlayable(WorldLayer layer)
        {
            return layer == WorldLayer.Surface
                || layer == WorldLayer.Underwater
                || layer == WorldLayer.DeepSea;
        }

        /// <summary>
        /// 2つのレイヤーが、中間層を飛ばさず潜水または浮上できる上下隣接層か判定します。
        /// </summary>
        /// <param name="first">比較する一方のレイヤーです。</param>
        /// <param name="second">比較するもう一方のレイヤーです。</param>
        /// <returns>SurfaceとUnderwater、またはUnderwaterとDeepSeaの組み合わせならtrueです。</returns>
        public static bool AreVerticallyAdjacent(WorldLayer first, WorldLayer second)
        {
            // 保存IDが将来追加されても、数値差だけで未知レイヤーを隣接扱いしないよう有効な組み合わせを明示します。
            return (first == WorldLayer.Surface && second == WorldLayer.Underwater)
                || (first == WorldLayer.Underwater && second == WorldLayer.Surface)
                || (first == WorldLayer.Underwater && second == WorldLayer.DeepSea)
                || (first == WorldLayer.DeepSea && second == WorldLayer.Underwater);
        }
    }
}
