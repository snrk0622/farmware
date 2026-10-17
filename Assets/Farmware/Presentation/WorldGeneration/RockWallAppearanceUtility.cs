// このファイルは、岩壁の露出面判定と、内部へ重ねる連続グラデーション影の不透明度を純粋計算します。
// ChunkTilemapPresenterの影メッシュと開発者用Rock Wall可視化が同じ距離・補間規則を共有します。
// マスク生成だけは呼び出し元所有のNative配列を同期Burst Jobで更新し、余白の座標ノイズ計算をまとめます。

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 資源表示の可否を決めるため、非岩壁、空間へ接する露出面、囲まれた内部を区別します。
    /// 影の濃さはこの列挙値ではなく、RockWallAppearanceUtilityが返す連続した不透明度を使用します。
    /// </summary>
    public enum RockWallAppearance : byte
    {
        /// <summary>中心タイルが岩壁ではなく、岩壁用の表示処理を行わない状態です。</summary>
        None = 0,

        /// <summary>上下左右のいずれかが非岩壁で、岩壁本来の色を見せる露出面です。</summary>
        Exposed = 1,

        /// <summary>上下左右を岩壁に囲まれ、グラデーション影と資源非表示を適用する内部です。</summary>
        Interior = 2
    }

    /// <summary>
    /// 岩壁から最寄りの非岩壁までのユークリッド距離を調べ、内部影の連続した不透明度を返します。
    /// 座標版は未ロードの隣接チャンクも決定論的に再生成するため、チャンク境界に影の継ぎ目を作りません。
    /// </summary>
    public static class RockWallAppearanceUtility
    {
        /// <summary>最寄りの空地を探す半径です。これより深い内部は最大濃度として扱います。</summary>
        public const int ShadowSearchRadiusInTiles = 7;

        /// <summary>64セルと両側8セルの探索余白を含む、影マスク1辺のタイル数です。</summary>
        public const int ShadowMaskSizeInTiles = WorldGridConstants.LogicalChunkSize
            + (2 * (ShadowSearchRadiusInTiles + 1));

        /// <summary>露出面の次から最大濃度へ到達するまでの距離です。短めにして浅い内部から明確に暗くします。</summary>
        public const float ShadowFadeDistanceInTiles = 4f;

        /// <summary>Mindustry風に深部をほぼ黒くしつつ、完全な不透明だけを避ける影メッシュの最大不透明度です。</summary>
        public const float MaximumShadowOpacity = 0.995f;

        /// <summary>
        /// チャンク完成時に岩壁マスクを全件更新します。中心は確定済み地形、外周だけは決定論的な再生成を使います。
        /// 同期Runで呼び出し中に終了するため、配列を保持せず、Worker待ちや影の公開遅延を追加しません。
        /// </summary>
        /// <param name="settings">再生成に使う固定設定です。Jobへ値コピーします。</param>
        /// <param name="coordinate">対象チャンクの絶対整数座標です。</param>
        /// <param name="tiles">読み取り専用の確定済み4096セルです。呼び出し中は変更・破棄しないでください。</param>
        /// <param name="mask">呼び出し元が所有・破棄する80×80の再利用配列です。全要素を上書きします。</param>
        /// <exception cref="ArgumentException">入力地形または出力マスクの要素数が契約と異なる場合です。</exception>
        public static void FillShadowMask(
            in WorldGenerationSettings settings,
            ChunkCoordinate coordinate,
            NativeArray<GeneratedTile> tiles,
            NativeArray<bool> mask)
        {
            // Burst側では例外による入力検査へ依存せず、公開APIの入口で配列の大きさを検証します。
            if (tiles.Length != WorldGridConstants.TilesPerChunk
                || mask.Length != ShadowMaskSizeInTiles * ShadowMaskSizeInTiles)
            {
                throw new ArgumentException("岩壁影マスクには4096セルの地形と80×80の出力配列が必要です。");
            }

            var job = new FillShadowMaskJob
            {
                Settings = settings,
                Coordinate = coordinate,
                Tiles = tiles,
                Mask = mask
            };
            // Schedule→Completeではなく、その場で計算を完了させます。UnityEngine.ObjectやECS QueryはJobへ渡しません。
            job.Run();
        }

        /// <summary>生成規則を変えず、中心Bufferの参照と外周ノイズ計算をまとめてBurstへ渡す同期Jobです。</summary>
        [BurstCompile]
        private struct FillShadowMaskJob : IJob
        {
            /// <summary>呼び出し時の生成設定です。Job中は読み取りだけを行います。</summary>
            public WorldGenerationSettings Settings;

            /// <summary>マスク中心の絶対チャンク座標です。表示原点やTransformには依存しません。</summary>
            public ChunkCoordinate Coordinate;

            /// <summary>呼び出し中だけ借りる確定済み地形です。読み取り専用で保持・破棄しません。</summary>
            [ReadOnly] public NativeArray<GeneratedTile> Tiles;

            /// <summary>呼び出し元所有の出力です。このJobが全要素へ書き、同期終了後すぐに参照できます。</summary>
            [WriteOnly] public NativeArray<bool> Mask;

            /// <summary>メインスレッド上で80×80を行優先で埋め、ワールド外は従来どおり非岩壁にします。</summary>
            public void Execute()
            {
                int padding = ShadowSearchRadiusInTiles + 1;
                for (int y = 0; y < ShadowMaskSizeInTiles; y++)
                {
                    int localY = y - padding;
                    for (int x = 0; x < ShadowMaskSizeInTiles; x++)
                    {
                        int localX = x - padding;
                        bool isRockWall;
                        if (localX >= 0 && localX < WorldGridConstants.LogicalChunkSize
                            && localY >= 0 && localY < WorldGridConstants.LogicalChunkSize)
                        {
                            // 中心を再生成しないことで、ECS側の確定結果を表示の正として維持します。
                            isRockWall = Tiles[WorldGrid.ToTileIndex(localX, localY)].Ground == GroundType.RockWall;
                        }
                        else if (WorldGrid.TryToWorldTileCoordinate(Coordinate.X, Coordinate.Y,
                                     localX, localY, out int worldX, out int worldY))
                        {
                            isRockWall = DeterministicChunkGenerator.IsRockWall(Settings, worldX, worldY);
                        }
                        else
                        {
                            // int端の外周を反対側のワールドへ周回させず、影が閉じないよう空地として扱います。
                            isRockWall = false;
                        }

                        Mask[(y * ShadowMaskSizeInTiles) + x] = isRockWall;
                    }
                }
            }
        }

        /// <summary>
        /// ワールド座標の中心岩壁が、空間へ接する露出面か囲まれた内部かを分類します。
        /// </summary>
        /// <param name="settings">水域優先を含む最終岩壁判定に使う生成設定です。</param>
        /// <param name="worldTileX">中心の符号付き水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">中心の符号付き垂直ワールドタイル座標です。</param>
        /// <returns>非岩壁ならNone、4方向の空地へ接すればExposed、それ以外はInteriorです。</returns>
        public static RockWallAppearance ClassifyAt(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            if (!DeterministicChunkGenerator.IsRockWall(settings, worldTileX, worldTileY))
            {
                return RockWallAppearance.None;
            }

            // 資源を露出面にだけ残すため、リンクの正方形グリッドと同じ上下左右の接続で外周を判定します。
            bool touchesOpenTile =
                !DeterministicChunkGenerator.IsRockWall(settings, worldTileX - 1, worldTileY)
                || !DeterministicChunkGenerator.IsRockWall(settings, worldTileX + 1, worldTileY)
                || !DeterministicChunkGenerator.IsRockWall(settings, worldTileX, worldTileY - 1)
                || !DeterministicChunkGenerator.IsRockWall(settings, worldTileX, worldTileY + 1);
            return touchesOpenTile
                ? RockWallAppearance.Exposed
                : RockWallAppearance.Interior;
        }

        /// <summary>
        /// 指定ワールド座標について、露出面では0、岩壁の奥ほど最大値へ滑らかに近づく影不透明度を返します。
        /// 開発者用可視化向けの座標版であり、通常描画ではチャンク内の岩壁マスクをまとめて処理します。
        /// </summary>
        /// <param name="settings">水域優先を含む最終岩壁判定に使う生成設定です。</param>
        /// <param name="worldTileX">中心の符号付き水平ワールドタイル座標です。</param>
        /// <param name="worldTileY">中心の符号付き垂直ワールドタイル座標です。</param>
        /// <returns>0以上MaximumShadowOpacity以下の連続した影不透明度です。</returns>
        public static float CalculateShadowOpacityAt(
            in WorldGenerationSettings settings,
            int worldTileX,
            int worldTileY)
        {
            if (!DeterministicChunkGenerator.IsRockWall(settings, worldTileX, worldTileY))
            {
                return 0f;
            }

            int nearestOpenDistanceSquared = int.MaxValue;
            int maximumDistanceSquared = ShadowSearchRadiusInTiles * ShadowSearchRadiusInTiles;

            // 円形範囲で最短距離を探し、マンハッタン距離の菱形帯ではなく丸みのある影勾配にします。
            for (int offsetY = -ShadowSearchRadiusInTiles;
                 offsetY <= ShadowSearchRadiusInTiles;
                 offsetY++)
            {
                for (int offsetX = -ShadowSearchRadiusInTiles;
                     offsetX <= ShadowSearchRadiusInTiles;
                     offsetX++)
                {
                    int distanceSquared = (offsetX * offsetX) + (offsetY * offsetY);
                    if (distanceSquared == 0
                        || distanceSquared > maximumDistanceSquared
                        || distanceSquared >= nearestOpenDistanceSquared)
                    {
                        continue;
                    }

                    if (!DeterministicChunkGenerator.IsRockWall(
                            settings,
                            worldTileX + offsetX,
                            worldTileY + offsetY))
                    {
                        nearestOpenDistanceSquared = distanceSquared;
                    }
                }
            }

            return CalculateShadowOpacityFromNearestOpenDistanceSquared(
                nearestOpenDistanceSquared);
        }

        /// <summary>
        /// 最寄り空地までの距離をEase-Out曲線へ通し、浅い内部から濃くなる連続した影不透明度へ変換します。
        /// </summary>
        /// <param name="nearestOpenDistanceSquared">タイル中心間の距離の二乗です。見つからない場合はint.MaxValueです。</param>
        /// <returns>露出面は0、探索半径より深い内部はMaximumShadowOpacityとなる値です。</returns>
        public static float CalculateShadowOpacityFromNearestOpenDistanceSquared(
            int nearestOpenDistanceSquared)
        {
            if (nearestOpenDistanceSquared <= 1)
            {
                return 0f;
            }

            if (nearestOpenDistanceSquared == int.MaxValue)
            {
                return MaximumShadowOpacity;
            }

            float distanceInTiles = Mathf.Sqrt(nearestOpenDistanceSquared);
            float normalizedDepth = Mathf.Clamp01(
                (distanceInTiles - 1f) / ShadowFadeDistanceInTiles);

            // 3次Ease-Outは露出面直後から素早く暗くなり、深部へ近づくほど緩やかに最大濃度へ収束します。
            // 露出面そのものは上の早期returnで0を保つため、岩壁の輪郭は明るいまま読み取れます。
            float inverseDepth = 1f - normalizedDepth;
            float strengthenedDepth = 1f - (inverseDepth * inverseDepth * inverseDepth);
            return strengthenedDepth * MaximumShadowOpacity;
        }

        /// <summary>
        /// 1頂点を囲む4タイル中心の影を平均し、隣接タイル間で共有できる頂点不透明度を返します。
        /// </summary>
        /// <param name="bottomLeft">頂点左下にあるタイル中心の影不透明度です。</param>
        /// <param name="bottomRight">頂点右下にあるタイル中心の影不透明度です。</param>
        /// <param name="topLeft">頂点左上にあるタイル中心の影不透明度です。</param>
        /// <param name="topRight">頂点右上にあるタイル中心の影不透明度です。</param>
        /// <returns>4値の算術平均です。Meshの三角形補間によりタイル内部も連続変化します。</returns>
        public static float CalculateVertexOpacity(
            float bottomLeft,
            float bottomRight,
            float topLeft,
            float topRight)
        {
            return (bottomLeft + bottomRight + topLeft + topRight) * 0.25f;
        }

        /// <summary>
        /// 岩壁内部では資源アイコンを隠し、通常地面と露出面では表示する共通規則を返します。
        /// ECS側の資源データは変更せず、影で覆う奥側だけPresentation上の重なりを防ぎます。
        /// </summary>
        /// <param name="appearance">中心タイルの岩壁表示分類です。</param>
        /// <returns>非岩壁または露出面ならtrue、囲まれた岩壁内部ではfalseです。</returns>
        public static bool ShouldDisplayResource(RockWallAppearance appearance)
        {
            return appearance == RockWallAppearance.None
                || appearance == RockWallAppearance.Exposed;
        }
    }
}
