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
        /// 群れ分割の完全配置探索を行う最大個体数です。
        /// 現在の鶏は地域当たり8羽ですが、将来極端に高密度な定義が追加されても再帰が深くなり過ぎないよう上限を設けます。
        /// </summary>
        private const int MaximumPopulationForCompleteLayoutSearch = 64;

        /// <summary>
        /// 完全配置探索で試す、群れ中心とメンバーのタイル選択の合計上限です。
        /// 配置問題は組み合わせが増え得るため、固定Tickを長時間占有せず従来の部分補充へ戻れる有限値にします。
        /// </summary>
        private const int MaximumCompleteLayoutSearchAttempts = 4096;

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
        /// <param name="resources">同じ添字に対応する行優先4096件の資源予約です。非空セルへは動物を生成しません。</param>
        /// <param name="regionState">抽選前の地域状態です。成功時だけ次回状態へ進みます。</param>
        /// <param name="candidates">複数地域をまとめられる候補一覧です。既存要素は保持します。</param>
        /// <returns>入力が有効で、候補をすべて追加できた場合はtrueです。</returns>
        public static bool TryAppendInitialPopulation(
            BlobAssetReference<AnimalSpeciesCatalogBlob> catalog,
            NativeArray<GeneratedTile> tiles,
            NativeArray<GeneratedResourceTile> resources,
            ref AnimalPopulationRegionState regionState,
            NativeList<AnimalSpawnCandidate> candidates)
        {
            if (!catalog.IsCreated
                || !tiles.IsCreated
                || tiles.Length != WorldGridConstants.TilesPerChunk
                || !resources.IsCreated
                || resources.Length != tiles.Length
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
                int eligibleTerrainTileCount = 0;

                for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
                {
                    // 地形条件と優先する資源予約から候補集合を作り、動物の抽選で地形や資源を書き換えません。
                    if (IsTileEligible(tiles[tileIndex], species))
                    {
                        // 密度は従来どおり有効地形面積を基準にし、資源予約は実際の配置先だけを制限します。
                        // 木や石が増えたことを理由に種定義の地域目標密度そのものを変えないためです。
                        eligibleTerrainTileCount++;
                        if (IsSpawnTileEligible(tiles[tileIndex], resources[tileIndex], species))
                        {
                            eligibleLocalTileIndices.Add(tileIndex);
                        }
                    }
                }

                int targetPopulation = CalculateTargetPopulation(
                    eligibleTerrainTileCount,
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
        /// <param name="eligibleLocalTileIndices">種のバイオーム・地形条件を満たし、資源予約を除外済みの地域内添字です。</param>
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
            int remainingSearchAttempts = MaximumCompleteLayoutSearchAttempts;

            // 先に全体を満たす群れ分割を探索します。今回置ける群れだけを即座に確定すると、
            // 5タイル領域と3タイル領域へ8羽を置けるのに4羽を先に選んで1羽を残す、といった不足が起きるためです。
            if (populationToCreate <= MaximumPopulationForCompleteLayoutSearch
                && TryAppendCompletePopulationLayout(
                    species,
                    populationToCreate,
                    eligibleLocalTileIndices,
                    occupiedLocalTileIndices,
                    ref stagedState,
                    candidates,
                    ref remainingSearchAttempts))
            {
                regionState = stagedState;
                return true;
            }

            // 全数を満たせない地形や探索上限到達時も、作れる有効な群れまで失わないよう探索中の変更を全て戻します。
            candidates.ResizeUninitialized(originalCandidateCount);
            occupiedLocalTileIndices.ResizeUninitialized(originalOccupiedCount);
            stagedState = regionState;

            if (!TryAppendGreedyPopulation(
                    species,
                    populationToCreate,
                    eligibleLocalTileIndices,
                    occupiedLocalTileIndices,
                    ref stagedState,
                    candidates))
            {
                candidates.ResizeUninitialized(originalCandidateCount);
                occupiedLocalTileIndices.ResizeUninitialized(originalOccupiedCount);
                return false;
            }

            regionState = stagedState;
            return true;
        }

        /// <summary>
        /// 残数をすべて有効な群れへ分割できる配置を、抽選した大きさから有限回のバックトラックで探します。
        /// </summary>
        /// <param name="species">生成対象の動物種定義です。</param>
        /// <param name="remaining">まだ配置していない個体数です。</param>
        /// <param name="eligibleTiles">対象種が利用できる地域内タイル添字です。</param>
        /// <param name="occupiedTiles">先行種と探索中の群れが使用済みの地域内タイル添字です。</param>
        /// <param name="regionState">成功経路だけを反映する乱数状態と受付順です。</param>
        /// <param name="candidates">成功経路の候補追加先です。</param>
        /// <param name="remainingSearchAttempts">中心とメンバーの探索全体で共有する残りタイル選択回数です。</param>
        /// <returns>残数をすべて配置できた場合はtrueです。</returns>
        private static bool TryAppendCompletePopulationLayout(
            AnimalSpeciesDefinitionBlob species,
            int remaining,
            NativeArray<int> eligibleTiles,
            NativeList<int> occupiedTiles,
            ref AnimalPopulationRegionState regionState,
            NativeList<AnimalSpawnCandidate> candidates,
            ref int remainingSearchAttempts)
        {
            if (remaining == 0)
            {
                return true;
            }

            if (remaining < species.MinimumHerdSize || remainingSearchAttempts <= 0)
            {
                return false;
            }

            AnimalPopulationRegionState sizeSelectionState = regionState;
            int preferredHerdSize = SelectHerdSize(
                ref sizeSelectionState.RandomState,
                remaining,
                species.MinimumHerdSize,
                species.MaximumHerdSize);
            int maximumForCurrentHerd = GetMaximumHerdSizeForRemaining(
                remaining,
                species.MinimumHerdSize,
                species.MaximumHerdSize);
            int herdSizeOptionCount =
                maximumForCurrentHerd - species.MinimumHerdSize + 1;

            for (int optionIndex = 0; optionIndex < herdSizeOptionCount; optionIndex++)
            {
                // 抽選値、より小さい値、より大きい値の順で試し、通常地形の抽選結果を最優先で維持します。
                int herdSize = GetHerdSizeForOption(
                    preferredHerdSize,
                    species.MinimumHerdSize,
                    maximumForCurrentHerd,
                    optionIndex);
                int remainingAfterHerd = remaining - herdSize;
                if (!CanSplitPopulationIntoHerds(
                        remainingAfterHerd,
                        species.MinimumHerdSize,
                        species.MaximumHerdSize))
                {
                    continue;
                }

                AnimalPopulationRegionState anchorSelectionState = sizeSelectionState;
                int startIndex = NextInt(
                    ref anchorSelectionState.RandomState,
                    eligibleTiles.Length);
                for (int offset = 0; offset < eligibleTiles.Length; offset++)
                {
                    if (remainingSearchAttempts <= 0)
                    {
                        return false;
                    }

                    int anchorLocalTileIndex =
                        eligibleTiles[(startIndex + offset) % eligibleTiles.Length];
                    if (Contains(occupiedTiles, anchorLocalTileIndex)
                        || CountNearbyAvailableTiles(
                            eligibleTiles,
                            occupiedTiles,
                            anchorLocalTileIndex,
                            species.MaximumWanderDistanceInTiles) < herdSize)
                    {
                        continue;
                    }

                    remainingSearchAttempts--;

                    int candidateCountBeforeHerd = candidates.Length;
                    int occupiedCountBeforeHerd = occupiedTiles.Length;
                    AnimalPopulationRegionState branchState = anchorSelectionState;
                    int herdLeaderCandidateIndex = candidates.Length;
                    AppendCandidate(
                        species,
                        anchorLocalTileIndex,
                        herdLeaderCandidateIndex,
                        ref branchState,
                        candidates);
                    occupiedTiles.Add(anchorLocalTileIndex);

                    if (TryAppendCompleteHerdMembers(
                        species,
                        herdSize - 1,
                        remainingAfterHerd,
                        anchorLocalTileIndex,
                        eligibleTiles,
                        occupiedTiles,
                        ref branchState,
                        candidates,
                        herdLeaderCandidateIndex,
                        ref remainingSearchAttempts))
                    {
                        regionState = branchState;
                        return true;
                    }

                    // この中心または群れサイズでは後続まで満たせないため、候補・占有・乱数状態を分岐前へ戻します。
                    candidates.ResizeUninitialized(candidateCountBeforeHerd);
                    occupiedTiles.ResizeUninitialized(occupiedCountBeforeHerd);
                }
            }

            return false;
        }

        /// <summary>
        /// 群れのメンバーを1羽ずつ選び、後続群れまで配置できない場合は別のメンバーへ戻って再探索します。
        /// 最初の経路は従来と同じ抽選順にし、変更不要な通常配置の乱数消費を維持します。
        /// </summary>
        /// <param name="species">徘徊距離と外見差分数を持つ動物種定義です。</param>
        /// <param name="remainingMembers">現在の群れでまだ追加していない個体数です。</param>
        /// <param name="populationAfterHerd">この群れの完成後に別の群れへ配置する個体数です。</param>
        /// <param name="anchorLocalTileIndex">現在の群れ先頭が使う地域内タイル添字です。</param>
        /// <param name="eligibleTiles">対象種が利用できる地域内タイル添字です。</param>
        /// <param name="occupiedTiles">先行個体と探索中の個体が使うタイル一覧です。失敗分岐の追加分は戻します。</param>
        /// <param name="regionState">成功経路だけを反映する乱数状態と受付順です。</param>
        /// <param name="candidates">成功経路の候補追加先です。既存候補は変更しません。</param>
        /// <param name="herdLeaderCandidateIndex">現在の群れの先頭候補がある添字です。</param>
        /// <param name="remainingSearchAttempts">中心とメンバーで共有し、失敗しても消費を戻さない残り探索回数です。</param>
        /// <returns>現在の群れと後続群れの全個体を配置できた場合はtrueです。</returns>
        private static bool TryAppendCompleteHerdMembers(
            AnimalSpeciesDefinitionBlob species,
            int remainingMembers,
            int populationAfterHerd,
            int anchorLocalTileIndex,
            NativeArray<int> eligibleTiles,
            NativeList<int> occupiedTiles,
            ref AnimalPopulationRegionState regionState,
            NativeList<AnimalSpawnCandidate> candidates,
            int herdLeaderCandidateIndex,
            ref int remainingSearchAttempts)
        {
            if (remainingMembers == 0)
            {
                // 群れ単体の完成では確定せず、後続が使う中心やメンバーを塞いでいないかも確認します。
                return TryAppendCompletePopulationLayout(
                    species, populationAfterHerd, eligibleTiles, occupiedTiles,
                    ref regionState, candidates, ref remainingSearchAttempts);
            }

            if (remainingSearchAttempts <= 0
                || CountNearbyAvailableTiles(
                    eligibleTiles, occupiedTiles, anchorLocalTileIndex,
                    species.MaximumWanderDistanceInTiles) < remainingMembers)
            {
                // 現在の群れさえ完成できない分岐は、組合せを増やす前に打ち切ります。
                return false;
            }

            AnimalPopulationRegionState memberSelectionState = regionState;
            int startIndex = NextInt(ref memberSelectionState.RandomState, eligibleTiles.Length);
            for (int offset = 0; offset < eligibleTiles.Length; offset++)
            {
                if (remainingSearchAttempts <= 0)
                {
                    return false;
                }

                int memberTileIndex = eligibleTiles[(startIndex + offset) % eligibleTiles.Length];
                if (Contains(occupiedTiles, memberTileIndex)
                    || !IsWithinWanderDistance(
                        memberTileIndex, anchorLocalTileIndex, species.MaximumWanderDistanceInTiles))
                {
                    continue;
                }

                remainingSearchAttempts--;
                int candidateCountBeforeMember = candidates.Length;
                int occupiedCountBeforeMember = occupiedTiles.Length;
                AnimalPopulationRegionState branchState = memberSelectionState;
                AppendCandidate(species, memberTileIndex, herdLeaderCandidateIndex,
                    ref branchState, candidates);
                occupiedTiles.Add(memberTileIndex);

                if (TryAppendCompleteHerdMembers(
                    species, remainingMembers - 1, populationAfterHerd, anchorLocalTileIndex,
                    eligibleTiles, occupiedTiles, ref branchState, candidates,
                    herdLeaderCandidateIndex, ref remainingSearchAttempts))
                {
                    regionState = branchState;
                    return true;
                }

                // 不成立のメンバーだけでなく、その先で試した群れも戻した状態から別タイルを試します。
                // 乱数と受付順はbranchStateへ分離しているため、失敗した分岐が次の候補へ漏れません。
                candidates.ResizeUninitialized(candidateCountBeforeMember);
                occupiedTiles.ResizeUninitialized(occupiedCountBeforeMember);
            }

            return false;
        }

        /// <summary>
        /// 完全配置が不可能な場合に、従来どおり抽選サイズを縮めながら配置可能な群れだけを追加します。
        /// </summary>
        /// <param name="species">生成対象の動物種定義です。</param>
        /// <param name="populationToCreate">今回配置を試みる総個体数です。</param>
        /// <param name="eligibleTiles">対象種が利用できる地域内タイル添字です。</param>
        /// <param name="occupiedTiles">先行種とこの処理で使用済みの地域内タイル添字です。</param>
        /// <param name="regionState">配置で進める乱数状態と受付順です。</param>
        /// <param name="candidates">生成候補の追加先です。</param>
        /// <returns>作成途中の群れを残さず処理できた場合はtrueです。地形都合の未補充はtrueに含みます。</returns>
        private static bool TryAppendGreedyPopulation(
            AnimalSpeciesDefinitionBlob species,
            int populationToCreate,
            NativeArray<int> eligibleTiles,
            NativeList<int> occupiedTiles,
            ref AnimalPopulationRegionState regionState,
            NativeList<AnimalSpawnCandidate> candidates)
        {
            int remaining = populationToCreate;
            while (remaining >= species.MinimumHerdSize)
            {
                int herdSize = SelectHerdSize(
                    ref regionState.RandomState,
                    remaining,
                    species.MinimumHerdSize,
                    species.MaximumHerdSize);
                int anchorLocalTileIndex;
                while (!TryFindHerdAnchor(
                           eligibleTiles,
                           occupiedTiles,
                           herdSize,
                           species.MaximumWanderDistanceInTiles,
                           ref regionState.RandomState,
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
                    // 最小群れさえ置けない残数は、徘徊距離契約を破らず未補充とします。
                    break;
                }

                int herdLeaderCandidateIndex = candidates.Length;
                AppendCandidate(
                    species,
                    anchorLocalTileIndex,
                    herdLeaderCandidateIndex,
                    ref regionState,
                    candidates);
                occupiedTiles.Add(anchorLocalTileIndex);
                if (!TryAppendHerdMembers(
                        species,
                        herdSize,
                        anchorLocalTileIndex,
                        eligibleTiles,
                        occupiedTiles,
                        ref regionState,
                        candidates,
                        herdLeaderCandidateIndex))
                {
                    return false;
                }

                remaining -= herdSize;
            }

            return true;
        }

        /// <summary>先頭以外の群れ個体を、同じ中心から許可距離内の未使用タイルへ追加します。</summary>
        /// <param name="species">徘徊距離と外見差分数を持つ動物種定義です。</param>
        /// <param name="herdSize">先頭を含む今回の群れ総数です。</param>
        /// <param name="anchorLocalTileIndex">すでに追加済みの群れ先頭タイルです。</param>
        /// <param name="eligibleTiles">対象種が利用できる地域内タイル添字です。</param>
        /// <param name="occupiedTiles">先頭と追加済み個体を含む使用済みタイル添字です。</param>
        /// <param name="regionState">外見差分抽選と受付順を進める地域状態です。</param>
        /// <param name="candidates">群れ個体候補の追加先です。</param>
        /// <param name="herdLeaderCandidateIndex">各個体へ記録する候補配列内の群れ先頭位置です。</param>
        /// <returns>必要な全メンバーを追加できた場合はtrueです。</returns>
        private static bool TryAppendHerdMembers(
            AnimalSpeciesDefinitionBlob species,
            int herdSize,
            int anchorLocalTileIndex,
            NativeArray<int> eligibleTiles,
            NativeList<int> occupiedTiles,
            ref AnimalPopulationRegionState regionState,
            NativeList<AnimalSpawnCandidate> candidates,
            int herdLeaderCandidateIndex)
        {
            for (int memberIndex = 1; memberIndex < herdSize; memberIndex++)
            {
                if (!TryFindHerdMember(
                        eligibleTiles,
                        occupiedTiles,
                        anchorLocalTileIndex,
                        species.MaximumWanderDistanceInTiles,
                        ref regionState.RandomState,
                        out int memberLocalTileIndex))
                {
                    return false;
                }

                AppendCandidate(
                    species,
                    memberLocalTileIndex,
                    herdLeaderCandidateIndex,
                    ref regionState,
                    candidates);
                occupiedTiles.Add(memberLocalTileIndex);
            }

            return true;
        }

        /// <summary>現在の群れを置いた後の残数を、最小〜最大群れの合計へ分割できるか判定します。</summary>
        /// <param name="population">分割可能性を調べる残個体数です。</param>
        /// <param name="minimumHerdSize">1群れに必要な最小個体数です。</param>
        /// <param name="maximumHerdSize">1群れへ入れられる最大個体数です。</param>
        /// <returns>0群れ以上の有効な群れ数の合計で個体数を表せる場合はtrueです。</returns>
        private static bool CanSplitPopulationIntoHerds(
            int population,
            int minimumHerdSize,
            int maximumHerdSize)
        {
            if (population == 0)
            {
                return true;
            }

            int minimumHerdCount = (population + maximumHerdSize - 1) / maximumHerdSize;
            int maximumHerdCount = population / minimumHerdSize;
            return minimumHerdCount <= maximumHerdCount;
        }

        /// <summary>残数を最小未満で残さない、今回選択可能な最大群れ数を返します。</summary>
        /// <param name="remaining">今回を含めてまだ配置していない個体数です。</param>
        /// <param name="minimumHerdSize">後続群れにも必要な最小個体数です。</param>
        /// <param name="maximumHerdSize">種定義が許可する1群れの最大個体数です。</param>
        /// <returns>今回の群れへ割り当てられる最大個体数です。</returns>
        private static int GetMaximumHerdSizeForRemaining(
            int remaining,
            int minimumHerdSize,
            int maximumHerdSize)
        {
            return remaining <= maximumHerdSize
                ? remaining
                : math.min(maximumHerdSize, remaining - minimumHerdSize);
        }

        /// <summary>抽選値を最初に、次に小さい値、最後に大きい値となる試行順から群れ数を返します。</summary>
        /// <param name="preferredHerdSize">乱数から最初に選ばれた群れ個体数です。</param>
        /// <param name="minimumHerdSize">今回選択できる最小個体数です。</param>
        /// <param name="maximumHerdSize">今回選択できる最大個体数です。</param>
        /// <param name="optionIndex">0始まりの代替案番号です。</param>
        /// <returns>この代替案で試す群れ個体数です。</returns>
        private static int GetHerdSizeForOption(
            int preferredHerdSize,
            int minimumHerdSize,
            int maximumHerdSize,
            int optionIndex)
        {
            int smallerOptionCount = preferredHerdSize - minimumHerdSize;
            if (optionIndex == 0)
            {
                return preferredHerdSize;
            }

            if (optionIndex <= smallerOptionCount)
            {
                return preferredHerdSize - optionIndex;
            }

            return preferredHerdSize + (optionIndex - smallerOptionCount);
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
        /// 地形条件と、動物より優先される資源予約を合わせて生成候補を判定します。
        /// 初期生成、後続リスポーン向けの候補抽出、分布監査、開発者表示で同じ条件を共有します。
        /// </summary>
        /// <param name="tile">動物種の対応条件を照合する生成済み地形です。</param>
        /// <param name="resource">同じタイルに予約された資源です。None以外は生成候補にしません。</param>
        /// <param name="species">生成対象の動物種定義です。</param>
        /// <returns>資源がなく、種の地形条件も満たす場合はtrueです。</returns>
        public static bool IsSpawnTileEligible(
            GeneratedTile tile,
            GeneratedResourceTile resource,
            AnimalSpeciesDefinitionBlob species)
        {
            return resource.Type == GeneratedResourceType.None && IsTileEligible(tile, species);
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
