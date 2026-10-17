// このファイルは、生成済み地形から野生動物の初期候補を決定論的に選ぶ共通パイプラインです。
// ANIMAL-01Bの初期補充とANIMAL-02Bのリスポーンが同じ適格判定・群れ形成・抽選状態を再利用できるよう、ECS Systemから分離しています。

using Farmware.Simulation;
using Farmware.Simulation.Animals;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Farmware.WorldGeneration.Animals
{
    /// <summary>
    /// 個体Entityを作る直前の、永続ID以外が確定した生成候補です。
    /// 一時NativeListだけに保持し、共通IDアロケーターが全要求を受理した場合に限ってEntityへ変換します。
    /// </summary>
    public struct AnimalSpawnCandidate
    {
        /// <summary>候補を所有する64×64地域の水平論理チャンク座標です。</summary>
        public int RegionX;

        /// <summary>候補を所有する64×64地域の垂直論理チャンク座標です。</summary>
        public int RegionY;

        /// <summary>生成する動物種の共通実行時IDです。</summary>
        public DefinitionRuntimeId SpeciesRuntimeId;

        /// <summary>Simulationへ記録する絶対水平タイル座標です。</summary>
        public int TileX;

        /// <summary>Simulationへ記録する絶対垂直タイル座標です。</summary>
        public int TileY;

        /// <summary>動物種内で選択した0始まりの外見差分番号です。</summary>
        public byte AppearanceVariant;

        /// <summary>地域内で初期補充とリスポーンを通して単調増加するID発行要求順です。</summary>
        public ulong SpawnSequence;

        /// <summary>この候補と同じ群れの先頭候補が、一括候補配列内にある添字です。</summary>
        public int HerdLeaderCandidateIndex;
    }

    /// <summary>
    /// 地域Seed、地形適格性、動物種定義から初期個体群を作る純粋計算です。
    /// Transform、GameObject、UnityEngine.Randomを使わず、負座標でも同じ入力から同じ候補を返します。
    /// </summary>
    public static class AnimalPopulationGenerator
    {
        /// <summary>動物種定義の密度が基準とする64×64地域の総タイル数です。</summary>
        private const int DensityReferenceTileCount = WorldGridConstants.TilesPerChunk;

        /// <summary>
        /// ワールド固有Seedと地域座標から、保存可能な初回抽選状態を作ります。
        /// </summary>
        /// <param name="populationSeed">地形Seedとは独立した、新規ワールド固有のSeedです。</param>
        /// <param name="regionX">64×64地域の水平論理チャンク座標です。</param>
        /// <param name="regionY">64×64地域の垂直論理チャンク座標です。</param>
        /// <returns>未初期化地域へ設定する乱数状態と1始まりの受付順です。</returns>
        public static AnimalPopulationRegionState CreateInitialRegionState(
            AnimalPopulationSeed populationSeed,
            int regionX,
            int regionY)
        {
            ulong state = Mix(populationSeed.Value ^ 0x9E3779B97F4A7C15UL);
            state = Mix(state ^ unchecked((ulong)(long)regionX));
            state = Mix(state ^ unchecked((ulong)(long)regionY) ^ 0xD1B54A32D192ED03UL);
            return new AnimalPopulationRegionState
            {
                RegionX = regionX,
                RegionY = regionY,
                RandomState = state,
                NextSpawnSequence = 1,
                InitializedAtTick = 0
            };
        }

        /// <summary>
        /// 全動物種を実行時ID順に処理し、地域の有効地形面積へ比例した初期個体群を候補一覧へ追加します。
        /// </summary>
        /// <param name="catalog">検証済みの動物種カタログです。</param>
        /// <param name="tiles">対象地域の行優先4096タイルです。</param>
        /// <param name="regionState">抽選前の地域状態です。成功時だけ次回状態へ進みます。</param>
        /// <param name="candidates">複数地域をまとめられる候補一覧です。既存要素は保持します。</param>
        /// <returns>入力が有効で、候補をすべて追加できた場合はtrueです。</returns>
        public static bool TryAppendInitialPopulation(
            BlobAssetReference<AnimalSpeciesCatalogBlob> catalog,
            NativeArray<GeneratedTile> tiles,
            ref AnimalPopulationRegionState regionState,
            NativeList<AnimalSpawnCandidate> candidates)
        {
            if (!catalog.IsCreated
                || !tiles.IsCreated
                || tiles.Length != WorldGridConstants.TilesPerChunk
                || regionState.NextSpawnSequence == 0
                || !candidates.IsCreated)
            {
                return false;
            }

            AnimalPopulationRegionState stagedState = regionState;
            int originalCandidateCount = candidates.Length;
            using NativeList<int> occupiedLocalTileIndices =
                new NativeList<int>(16, Allocator.Temp);

            ref AnimalSpeciesCatalogBlob root = ref catalog.Value;
            for (int speciesIndex = 0; speciesIndex < root.Species.Length; speciesIndex++)
            {
                AnimalSpeciesDefinitionBlob species = root.Species[speciesIndex];
                using NativeList<int> eligibleLocalTileIndices =
                    new NativeList<int>(64, Allocator.Temp);

                for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
                {
                    // 候補集合は地形と種定義だけから作り、抽選Seedによって地形そのものを変更しません。
                    if (IsTileEligible(tiles[tileIndex], species))
                    {
                        eligibleLocalTileIndices.Add(tileIndex);
                    }
                }

                int targetPopulation = CalculateTargetPopulation(
                    eligibleLocalTileIndices.Length,
                    species.TargetPopulationPer4096EligibleTiles);
                if (targetPopulation < species.MinimumHerdSize)
                {
                    // 群れ最小数を満たせない小さな地形断片には半端な単独個体を置かず、密度誤差として扱います。
                    continue;
                }

                if (!TryAppendSpeciesPopulation(
                        species,
                        targetPopulation,
                        eligibleLocalTileIndices.AsArray(),
                        occupiedLocalTileIndices,
                        ref stagedState,
                        candidates))
                {
                    candidates.ResizeUninitialized(originalCandidateCount);
                    return false;
                }
            }

            regionState = stagedState;
            return true;
        }

        /// <summary>
        /// 1種類の動物について、指定不足数を群れ単位で候補へ追加します。
        /// 将来のリスポーンは地域内の既存位置をoccupiedLocalTileIndicesへ渡し、同じ生成規則を再利用します。
        /// </summary>
        /// <param name="species">生成対象の検証済み種定義です。</param>
        /// <param name="requestedPopulation">今回補充したい個体数です。</param>
        /// <param name="eligibleLocalTileIndices">種のバイオーム・地形条件を満たす地域内添字です。</param>
        /// <param name="occupiedLocalTileIndices">この処理を含む既存候補が使用済みの地域内添字です。</param>
        /// <param name="regionState">地域別乱数状態と次回受付順です。成功時だけ進めます。</param>
        /// <param name="candidates">生成候補の追加先です。</param>
        /// <returns>入力と受付順に問題がなく、作成可能な群れをすべて追加できた場合はtrueです。</returns>
        public static bool TryAppendSpeciesPopulation(
            AnimalSpeciesDefinitionBlob species,
            int requestedPopulation,
            NativeArray<int> eligibleLocalTileIndices,
            NativeList<int> occupiedLocalTileIndices,
            ref AnimalPopulationRegionState regionState,
            NativeList<AnimalSpawnCandidate> candidates)
        {
            if (requestedPopulation < 0
                || !eligibleLocalTileIndices.IsCreated
                || !occupiedLocalTileIndices.IsCreated
                || !candidates.IsCreated
                || regionState.NextSpawnSequence == 0
                || species.MinimumHerdSize == 0
                || species.MinimumHerdSize > species.MaximumHerdSize
                || species.AppearanceVariantCount == 0)
            {
                return false;
            }

            if (requestedPopulation == 0)
            {
                return true;
            }

            // occupiedLocalTileIndicesには別種だけが利用できるタイルも含まれます。
            // 単純に全占有数を引くと、この種の有効面積まで過小評価するため、実際に重なる位置だけを数えます。
            int availableTileCount = CountAvailableTiles(
                eligibleLocalTileIndices,
                occupiedLocalTileIndices);
            int populationToCreate = math.min(requestedPopulation, availableTileCount);
            if (populationToCreate < species.MinimumHerdSize
                || (ulong)populationToCreate
                    > ulong.MaxValue - regionState.NextSpawnSequence)
            {
                return populationToCreate < species.MinimumHerdSize;
            }

            AnimalPopulationRegionState stagedState = regionState;
            int originalCandidateCount = candidates.Length;
            int originalOccupiedCount = occupiedLocalTileIndices.Length;
            int remaining = populationToCreate;

            while (remaining >= species.MinimumHerdSize)
            {
                int herdSize = SelectHerdSize(
                    ref stagedState.RandomState,
                    remaining,
                    species.MinimumHerdSize,
                    species.MaximumHerdSize);

                // 適格タイルが複数の小さな領域へ分かれていると、抽選した大きさだけが置けない場合があります。
                // そのまま終了すると、小さい有効な群れを複数作れる地域まで空になるため、最小群れ数まで縮めて再探索します。
                int anchorLocalTileIndex;
                while (!TryFindHerdAnchor(
                           eligibleLocalTileIndices,
                           occupiedLocalTileIndices,
                           herdSize,
                           species.MaximumWanderDistanceInTiles,
                           ref stagedState.RandomState,
                           out anchorLocalTileIndex))
                {
                    herdSize--;
                    if (herdSize < species.MinimumHerdSize)
                    {
                        break;
                    }
                }

                if (herdSize < species.MinimumHerdSize)
                {
                    // 最小群れさえ置けない残数は、無理に徘徊距離契約を破らず未補充とします。
                    break;
                }

                int herdLeaderCandidateIndex = candidates.Length;
                AppendCandidate(
                    species,
                    anchorLocalTileIndex,
                    herdLeaderCandidateIndex,
                    ref stagedState,
                    candidates);
                occupiedLocalTileIndices.Add(anchorLocalTileIndex);

                for (int memberIndex = 1; memberIndex < herdSize; memberIndex++)
                {
                    if (!TryFindHerdMember(
                            eligibleLocalTileIndices,
                            occupiedLocalTileIndices,
                            anchorLocalTileIndex,
                            species.MaximumWanderDistanceInTiles,
                            ref stagedState.RandomState,
                            out int memberLocalTileIndex))
                    {
                        // 事前に必要数を数えているため通常は到達しません。到達時は部分群れと乱数消費を全て戻します。
                        candidates.ResizeUninitialized(originalCandidateCount);
                        occupiedLocalTileIndices.ResizeUninitialized(originalOccupiedCount);
                        return false;
                    }

                    AppendCandidate(
                        species,
                        memberLocalTileIndex,
                        herdLeaderCandidateIndex,
                        ref stagedState,
                        candidates);
                    occupiedLocalTileIndices.Add(memberLocalTileIndex);
                }

                remaining -= herdSize;
            }

            regionState = stagedState;
            return true;
        }

        /// <summary>
        /// GeneratedTileのバイオームと基礎地形を、動物種の別Maskへ明示的に対応付けて適格性を返します。
        /// </summary>
        /// <param name="tile">判定する生成済み地形タイルです。</param>
        /// <param name="species">許可Maskを持つ動物種定義です。</param>
        /// <returns>バイオームと基礎地形の両方が許可される場合はtrueです。</returns>
        public static bool IsTileEligible(
            GeneratedTile tile,
            AnimalSpeciesDefinitionBlob species)
        {
            AnimalBiomeMask biomeMask = ToAnimalBiomeMask(tile.Biome);
            AnimalGroundMask groundMask = ToAnimalGroundMask(tile.Ground);
            return AnimalSpeciesCatalogLookup.IsBiomeAllowed(
                    species.AllowedBiomes,
                    biomeMask)
                && AnimalSpeciesCatalogLookup.IsGroundAllowed(
                    species.AllowedGrounds,
                    groundMask);
        }

        /// <summary>
        /// 有効タイル数と4096タイル当たり密度から、四捨五入した地域目標数を求めます。
        /// </summary>
        /// <param name="eligibleTileCount">対象種が滞在できる地域内タイル数です。</param>
        /// <param name="populationPer4096Tiles">全4096タイルが有効な場合の目標個体数です。</param>
        /// <returns>0以上の地域目標個体数です。</returns>
        public static int CalculateTargetPopulation(
            int eligibleTileCount,
            ushort populationPer4096Tiles)
        {
            if (eligibleTileCount <= 0 || populationPer4096Tiles == 0)
            {
                return 0;
            }

            long scaledPopulation = (long)eligibleTileCount * populationPer4096Tiles;
            return (int)((scaledPopulation + (DensityReferenceTileCount / 2))
                / DensityReferenceTileCount);
        }

        /// <summary>
        /// 残数を群れ最小未満へしない範囲で、今回の群れ数を決定します。
        /// </summary>
        /// <param name="randomState">地域別乱数状態です。</param>
        /// <param name="remaining">まだ配置していない個体数です。</param>
        /// <param name="minimumHerdSize">1群れの最小個体数です。</param>
        /// <param name="maximumHerdSize">1群れの最大個体数です。</param>
        /// <returns>定義範囲内で、残りも0または最小数以上になる個体数です。</returns>
        private static int SelectHerdSize(
            ref ulong randomState,
            int remaining,
            int minimumHerdSize,
            int maximumHerdSize)
        {
            if (remaining <= maximumHerdSize)
            {
                return remaining;
            }

            int maximumForCurrentHerd = math.min(
                maximumHerdSize,
                remaining - minimumHerdSize);
            return minimumHerdSize + NextInt(
                ref randomState,
                maximumForCurrentHerd - minimumHerdSize + 1);
        }

        /// <summary>
        /// 群れ全員を徘徊距離内へ置ける未使用タイルを、抽選開始位置から循環走査して探します。
        /// </summary>
        private static bool TryFindHerdAnchor(
            NativeArray<int> eligibleTiles,
            NativeList<int> occupiedTiles,
            int herdSize,
            int maximumWanderDistance,
            ref ulong randomState,
            out int anchorTileIndex)
        {
            anchorTileIndex = default;
            if (eligibleTiles.Length == 0)
            {
                return false;
            }

            int startIndex = NextInt(ref randomState, eligibleTiles.Length);
            for (int offset = 0; offset < eligibleTiles.Length; offset++)
            {
                int candidate = eligibleTiles[(startIndex + offset) % eligibleTiles.Length];
                if (Contains(occupiedTiles, candidate))
                {
                    continue;
                }

                int nearbyAvailableCount = CountNearbyAvailableTiles(
                    eligibleTiles,
                    occupiedTiles,
                    candidate,
                    maximumWanderDistance);
                if (nearbyAvailableCount >= herdSize)
                {
                    anchorTileIndex = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 群れ中心から許可距離内にある未使用タイルを、抽選開始位置から循環走査して1件選びます。
        /// </summary>
        private static bool TryFindHerdMember(
            NativeArray<int> eligibleTiles,
            NativeList<int> occupiedTiles,
            int anchorTileIndex,
            int maximumWanderDistance,
            ref ulong randomState,
            out int memberTileIndex)
        {
            memberTileIndex = default;
            int startIndex = NextInt(ref randomState, eligibleTiles.Length);
            for (int offset = 0; offset < eligibleTiles.Length; offset++)
            {
                int candidate = eligibleTiles[(startIndex + offset) % eligibleTiles.Length];
                if (!Contains(occupiedTiles, candidate)
                    && IsWithinWanderDistance(
                        candidate,
                        anchorTileIndex,
                        maximumWanderDistance))
                {
                    memberTileIndex = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>指定中心の徘徊距離内にある未使用適格タイル数を数えます。</summary>
        private static int CountNearbyAvailableTiles(
            NativeArray<int> eligibleTiles,
            NativeList<int> occupiedTiles,
            int anchorTileIndex,
            int maximumWanderDistance)
        {
            int count = 0;
            for (int index = 0; index < eligibleTiles.Length; index++)
            {
                int candidate = eligibleTiles[index];
                if (!Contains(occupiedTiles, candidate)
                    && IsWithinWanderDistance(
                        candidate,
                        anchorTileIndex,
                        maximumWanderDistance))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// この動物種の適格タイルのうち、先行する種や群れがまだ使用していない件数を返します。
        /// </summary>
        /// <param name="eligibleTiles">現在の動物種が利用できる地域内タイル添字です。</param>
        /// <param name="occupiedTiles">全動物種で既に使用した地域内タイル添字です。</param>
        /// <returns>両一覧を照合した未使用適格タイル数です。</returns>
        private static int CountAvailableTiles(
            NativeArray<int> eligibleTiles,
            NativeList<int> occupiedTiles)
        {
            int count = 0;
            for (int index = 0; index < eligibleTiles.Length; index++)
            {
                if (!Contains(occupiedTiles, eligibleTiles[index]))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>行優先添字同士のChebyshev距離が許可範囲内かを判定します。</summary>
        private static bool IsWithinWanderDistance(
            int firstTileIndex,
            int secondTileIndex,
            int maximumWanderDistance)
        {
            int firstX = firstTileIndex % WorldGridConstants.LogicalChunkSize;
            int firstY = firstTileIndex / WorldGridConstants.LogicalChunkSize;
            int secondX = secondTileIndex % WorldGridConstants.LogicalChunkSize;
            int secondY = secondTileIndex / WorldGridConstants.LogicalChunkSize;
            return math.max(math.abs(firstX - secondX), math.abs(firstY - secondY))
                <= maximumWanderDistance;
        }

        /// <summary>小さな個体群候補用Listに、同じ地域内添字がすでにあるかを線形検索します。</summary>
        private static bool Contains(NativeList<int> values, int candidate)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>地域内添字を絶対座標へ変換し、次回受付順と外見差分を進めて候補へ追加します。</summary>
        private static void AppendCandidate(
            AnimalSpeciesDefinitionBlob species,
            int localTileIndex,
            int herdLeaderCandidateIndex,
            ref AnimalPopulationRegionState regionState,
            NativeList<AnimalSpawnCandidate> candidates)
        {
            int localX = localTileIndex % WorldGridConstants.LogicalChunkSize;
            int localY = localTileIndex / WorldGridConstants.LogicalChunkSize;
            candidates.Add(new AnimalSpawnCandidate
            {
                RegionX = regionState.RegionX,
                RegionY = regionState.RegionY,
                SpeciesRuntimeId = species.SpeciesRuntimeId,
                TileX = WorldGrid.ToWorldTileX(regionState.RegionX, localX),
                TileY = WorldGrid.ToWorldTileY(regionState.RegionY, localY),
                AppearanceVariant = (byte)NextInt(
                    ref regionState.RandomState,
                    species.AppearanceVariantCount),
                SpawnSequence = regionState.NextSpawnSequence,
                HerdLeaderCandidateIndex = herdLeaderCandidateIndex
            });
            regionState.NextSpawnSequence++;
        }

        /// <summary>BiomeIdを動物定義の同名bitへ明示的に変換します。</summary>
        private static AnimalBiomeMask ToAnimalBiomeMask(BiomeId biome)
        {
            switch (biome)
            {
                case BiomeId.Grassland: return AnimalBiomeMask.Grassland;
                case BiomeId.Forest: return AnimalBiomeMask.Forest;
                case BiomeId.Desert: return AnimalBiomeMask.Desert;
                case BiomeId.Rockland: return AnimalBiomeMask.Rockland;
                case BiomeId.Wetland: return AnimalBiomeMask.Wetland;
                case BiomeId.Snowfield: return AnimalBiomeMask.Snowfield;
                case BiomeId.Volcanic: return AnimalBiomeMask.Volcanic;
                default: return AnimalBiomeMask.None;
            }
        }

        /// <summary>GroundTypeの予約値に依存せず、動物定義の同名bitへ明示的に変換します。</summary>
        private static AnimalGroundMask ToAnimalGroundMask(GroundType ground)
        {
            switch (ground)
            {
                case GroundType.Land: return AnimalGroundMask.Land;
                case GroundType.Water: return AnimalGroundMask.Water;
                case GroundType.RockWall: return AnimalGroundMask.RockWall;
                case GroundType.Magma: return AnimalGroundMask.Magma;
                default: return AnimalGroundMask.None;
            }
        }

        /// <summary>SplitMix64の混合処理で、入力bitの偏りを地域別状態へ持ち込みにくくします。</summary>
        private static ulong Mix(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }

        /// <summary>保存済み状態を1回進め、0以上exclusiveMaximum未満の整数へ変換します。</summary>
        private static int NextInt(ref ulong state, int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 1)
            {
                state += 0x9E3779B97F4A7C15UL;
                return 0;
            }

            state += 0x9E3779B97F4A7C15UL;
            ulong randomValue = Mix(state);
            return (int)(randomValue % (uint)exclusiveMaximum);
        }
    }
}
