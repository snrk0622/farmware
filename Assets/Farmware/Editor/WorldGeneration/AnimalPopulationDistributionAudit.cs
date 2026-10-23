// このファイルは、複数の地形Seed・個体群Seed・地域について鶏の候補面積、目標数、確定数、群れ構成を集計します。
// Editor専用の純粋計算として実行し、Scene、ECS World、実際のセーブ状態は変更しません。

using System;
using System.Collections.Generic;
using System.Text;
using Farmware.Simulation;
using Farmware.Simulation.Animals;
using Farmware.WorldGeneration.Animals;
using Unity.Collections;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 動物個体群の分布監査で使う地形Seed、独立した個体群Seed、地域範囲、生成器版を保持します。
    /// </summary>
    public sealed class AnimalPopulationDistributionAuditSettings
    {
        /// <summary>各監査ケースで地形を決めるワールドSeed文字列です。</summary>
        public IReadOnlyList<string> TerrainSeedTexts { get; }

        /// <summary>同じ添字の地形Seedと組み合わせる、0ではない個体群Seedです。</summary>
        public IReadOnlyList<ulong> PopulationSeeds { get; }

        /// <summary>原点から各軸の正負へ調べる64×64地域数です。</summary>
        public int RegionRadius { get; }

        /// <summary>地形生成結果へ渡すGeneratorVersionです。</summary>
        public int GeneratorVersion { get; }

        /// <summary>配列を複製し、監査中に呼び出し元から条件を変更できない設定を作ります。</summary>
        /// <param name="terrainSeedTexts">1件以上のワールドSeed文字列です。</param>
        /// <param name="populationSeeds">地形Seedと同数の、0ではない個体群Seedです。</param>
        /// <param name="regionRadius">0以上の地域半径です。</param>
        /// <param name="generatorVersion">C5-Cの現在GeneratorVersionです。旧版は監査でも混在させません。</param>
        public AnimalPopulationDistributionAuditSettings(
            string[] terrainSeedTexts,
            ulong[] populationSeeds,
            int regionRadius,
            int generatorVersion)
        {
            if (terrainSeedTexts == null || terrainSeedTexts.Length == 0)
            {
                throw new ArgumentException("動物分布監査には1件以上の地形Seedが必要です。", nameof(terrainSeedTexts));
            }

            if (populationSeeds == null || populationSeeds.Length != terrainSeedTexts.Length)
            {
                throw new ArgumentException("個体群Seedは地形Seedと同じ件数が必要です。", nameof(populationSeeds));
            }

            if (regionRadius < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(regionRadius), "地域半径は0以上である必要があります。");
            }

            if (generatorVersion != WorldGenerationSettings.CurrentGeneratorVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(generatorVersion),
                    $"動物分布監査は現在GeneratorVersion {WorldGenerationSettings.CurrentGeneratorVersion} だけを受理します。");
            }

            string[] ownedTerrainSeeds = (string[])terrainSeedTexts.Clone();
            ulong[] ownedPopulationSeeds = (ulong[])populationSeeds.Clone();
            for (int index = 0; index < ownedTerrainSeeds.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(ownedTerrainSeeds[index]))
                {
                    throw new ArgumentException($"地形Seed文字列が空です。index={index}", nameof(terrainSeedTexts));
                }

                if (ownedPopulationSeeds[index] == AnimalPopulationSeed.InvalidValue)
                {
                    throw new ArgumentException($"個体群Seedに予約値0は使えません。index={index}", nameof(populationSeeds));
                }
            }

            TerrainSeedTexts = ownedTerrainSeeds;
            PopulationSeeds = ownedPopulationSeeds;
            RegionRadius = regionRadius;
            GeneratorVersion = generatorVersion;
        }

        /// <summary>通常の開発確認用に、3組のSeedで原点周囲9×9地域を調べる設定を返します。</summary>
        /// <returns>約100万タイルをバックグラウンドで監査する既定設定です。</returns>
        public static AnimalPopulationDistributionAuditSettings CreateDefault()
        {
            return new AnimalPopulationDistributionAuditSettings(
                new[]
                {
                    "animal-audit-grassland",
                    "animal-audit-mixed",
                    "animal-audit-negative"
                },
                new[]
                {
                    0x12C4A579D3E6B801UL,
                    0xA97134E8C2056FBDUL,
                    0x5D02F1AC947EB638UL
                },
                4,
                WorldGenerationSettings.CurrentGeneratorVersion);
        }
    }

    /// <summary>
    /// 動物個体群監査の集計値と、Unity Consoleへ表示できる日本語レポートです。
    /// </summary>
    public sealed class AnimalPopulationDistributionAuditResult
    {
        /// <summary>監査した地形Seed・個体群Seedの組数です。</summary>
        public int SeedPairCount { get; }

        /// <summary>全Seedで調べた64×64地域の総数です。</summary>
        public int RegionCount { get; }

        /// <summary>鶏の草原かつ陸地条件を満たしたタイル総数です。</summary>
        public long EligibleTileCount { get; }

        /// <summary>有効面積と密度8から算出した目標個体数の総計です。</summary>
        public int TargetPopulation { get; }

        /// <summary>群れ条件と重複回避を適用して作れた候補個体数です。</summary>
        public int SpawnedPopulation { get; }

        /// <summary>目標より候補数が少なかった地域数です。小さな草原断片も含みます。</summary>
        public int UnderfilledRegionCount { get; }

        /// <summary>3〜6羽または最大徘徊距離8の契約を破った群れ数です。</summary>
        public int InvalidHerdCount { get; }

        /// <summary>同じ地域内で2個体が同じタイルを使った件数です。</summary>
        public int DuplicatePositionCount { get; }

        /// <summary>候補位置、外見差分、群れ区切りを走査順で混ぜた比較用64bit署名です。</summary>
        public ulong Signature { get; }

        /// <summary>調整前後で同じ条件を比較するための日本語集計レポートです。</summary>
        public string Report { get; }

        /// <summary>Analyzerが完成させた集計値から不変な結果と表示文を作ります。</summary>
        internal AnimalPopulationDistributionAuditResult(
            int seedPairCount,
            int regionCount,
            long eligibleTileCount,
            int targetPopulation,
            int spawnedPopulation,
            int underfilledRegionCount,
            int invalidHerdCount,
            int duplicatePositionCount,
            ulong signature)
        {
            SeedPairCount = seedPairCount;
            RegionCount = regionCount;
            EligibleTileCount = eligibleTileCount;
            TargetPopulation = targetPopulation;
            SpawnedPopulation = spawnedPopulation;
            UnderfilledRegionCount = underfilledRegionCount;
            InvalidHerdCount = invalidHerdCount;
            DuplicatePositionCount = duplicatePositionCount;
            Signature = signature;

            StringBuilder report = new StringBuilder(320);
            report.AppendLine("[Farmware] 動物個体群分布監査が完了しました。");
            report.Append("Seed組=").Append(SeedPairCount)
                .Append("  地域=").Append(RegionCount)
                .Append("  適格タイル=").Append(EligibleTileCount).AppendLine();
            report.Append("目標個体=").Append(TargetPopulation)
                .Append("  確定候補=").Append(SpawnedPopulation)
                .Append("  未充足地域=").Append(UnderfilledRegionCount).AppendLine();
            report.Append("不正群れ=").Append(InvalidHerdCount)
                .Append("  重複位置=").Append(DuplicatePositionCount)
                .Append("  署名=0x").Append(Signature.ToString("x16"));
            Report = report.ToString();
        }
    }

    /// <summary>
    /// 現在の鶏初期値を用い、実際の地形生成器と共通個体生成パイプラインを多数地域で監査します。
    /// </summary>
    public static class AnimalPopulationDistributionAnalyzer
    {
        /// <summary>署名を始めるFNV-1a 64bitのoffset basisです。</summary>
        private const ulong SignatureOffsetBasis = 14695981039346656037UL;

        /// <summary>署名へ1値を混ぜるFNV-1a 64bitのprimeです。</summary>
        private const ulong SignaturePrime = 1099511628211UL;

        /// <summary>設定範囲の全地域を生成し、面積比例、群れ、重複、再現性を集計します。</summary>
        /// <param name="settings">固定した複数Seedと地域半径です。</param>
        /// <returns>個体数、契約違反件数、比較署名を含む監査結果です。</returns>
        public static AnimalPopulationDistributionAuditResult Analyze(
            AnimalPopulationDistributionAuditSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            AnimalSpeciesDefinitionBlob chicken = CreateChickenDefinition();
            int regionWidth = (settings.RegionRadius * 2) + 1;
            int regionCount = settings.TerrainSeedTexts.Count * regionWidth * regionWidth;
            long eligibleTileCount = 0;
            int targetPopulation = 0;
            int spawnedPopulation = 0;
            int underfilledRegionCount = 0;
            int invalidHerdCount = 0;
            int duplicatePositionCount = 0;
            ulong signature = SignatureOffsetBasis;

            for (int seedIndex = 0; seedIndex < settings.TerrainSeedTexts.Count; seedIndex++)
            {
                WorldGenerationSettings terrainSettings = WorldGenerationSettings.CreateDefault(
                    WorldSeedUtility.FromString(settings.TerrainSeedTexts[seedIndex]),
                    settings.GeneratorVersion);
                AnimalPopulationSeed populationSeed = new AnimalPopulationSeed
                {
                    Value = settings.PopulationSeeds[seedIndex]
                };

                for (int regionY = -settings.RegionRadius; regionY <= settings.RegionRadius; regionY++)
                {
                    for (int regionX = -settings.RegionRadius; regionX <= settings.RegionRadius; regionX++)
                    {
                        AnalyzeRegion(
                            terrainSettings,
                            populationSeed,
                            chicken,
                            regionX,
                            regionY,
                            ref eligibleTileCount,
                            ref targetPopulation,
                            ref spawnedPopulation,
                            ref underfilledRegionCount,
                            ref invalidHerdCount,
                            ref duplicatePositionCount,
                            ref signature);
                    }
                }
            }

            return new AnimalPopulationDistributionAuditResult(
                settings.TerrainSeedTexts.Count,
                regionCount,
                eligibleTileCount,
                targetPopulation,
                spawnedPopulation,
                underfilledRegionCount,
                invalidHerdCount,
                duplicatePositionCount,
                signature);
        }

        /// <summary>1地域の4096タイルを生成し、共通候補生成結果を全体集計へ加算します。</summary>
        private static void AnalyzeRegion(
            WorldGenerationSettings terrainSettings,
            AnimalPopulationSeed populationSeed,
            AnimalSpeciesDefinitionBlob species,
            int regionX,
            int regionY,
            ref long totalEligibleTileCount,
            ref int totalTargetPopulation,
            ref int totalSpawnedPopulation,
            ref int underfilledRegionCount,
            ref int invalidHerdCount,
            ref int duplicatePositionCount,
            ref ulong signature)
        {
            // EditorメニューはTask.Runから呼ぶため、メインスレッド専用のAllocator.Tempではなく、
            // 管理ThreadPool上でも明示的に確保・破棄できるTempJobを使用します。
            using NativeList<int> eligibleTiles = new NativeList<int>(Allocator.TempJob);
            using NativeList<int> occupiedTiles = new NativeList<int>(Allocator.TempJob);
            using NativeList<AnimalSpawnCandidate> candidates =
                new NativeList<AnimalSpawnCandidate>(Allocator.TempJob);

            int minimumTileX = regionX * WorldGridConstants.LogicalChunkSize;
            int minimumTileY = regionY * WorldGridConstants.LogicalChunkSize;
            ResourceGenerationSettings resourceSettings = ResourceGenerationSettings.CreateDefault();
            BiomeClassificationSettings biomeSettings = BiomeDefinitionCatalog.CreateDefault();
            if (!HydrologyDiagnosticChunk.TryGenerate(
                    terrainSettings,
                    biomeSettings,
                    new ChunkCoordinate(WorldLayer.Surface, regionX, regionY),
                    out HydrologyDiagnosticChunk generatedTerrainChunk,
                    out HydrologyDiagnosticGenerationStage generationStage))
            {
                // 基礎生成のBiome=Noneを動物適性へ渡すと全候補が消えるため、部分結果へ退行せず監査を失敗させます。
                throw new InvalidOperationException(
                    $"動物分布監査のC5-C完成地形を生成できません。region=({regionX},{regionY}), stage={generationStage}");
            }

            // 1地域は1論理チャンクと同寸法です。監査終了時に4つのPersistent配列を必ず破棄します。
            using HydrologyDiagnosticChunk terrainChunk = generatedTerrainChunk;
            int eligibleTerrainTileCount = 0;
            for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
            {
                for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
                {
                    int tileIndex = (localY * WorldGridConstants.LogicalChunkSize) + localX;
                    GeneratedTile tile = terrainChunk.TerrainTiles[tileIndex];
                    if (AnimalPopulationGenerator.IsTileEligible(tile, species))
                    {
                        eligibleTerrainTileCount++;
                        // Runtimeと同じ資源予約を生成してから動物候補を抽出し、監査だけが木や石へ重なる配置を許さないようにします。
                        GeneratedResourceTile resource = DeterministicResourceGenerator.GenerateResource(
                            terrainSettings, resourceSettings, tile,
                            minimumTileX + localX, minimumTileY + localY);
                        if (AnimalPopulationGenerator.IsSpawnTileEligible(tile, resource, species))
                        {
                            eligibleTiles.Add(tileIndex);
                        }
                    }
                }
            }

            int target = AnimalPopulationGenerator.CalculateTargetPopulation(
                eligibleTerrainTileCount,
                species.TargetPopulationPer4096EligibleTiles);
            AnimalPopulationRegionState regionState =
                AnimalPopulationGenerator.CreateInitialRegionState(
                    populationSeed,
                    regionX,
                    regionY);
            bool succeeded = AnimalPopulationGenerator.TryAppendSpeciesPopulation(
                species,
                target,
                eligibleTiles.AsArray(),
                occupiedTiles,
                ref regionState,
                candidates);
            if (!succeeded)
            {
                throw new InvalidOperationException($"動物候補生成に失敗しました。region=({regionX},{regionY})");
            }

            totalEligibleTileCount += eligibleTerrainTileCount;
            totalTargetPopulation += target;
            totalSpawnedPopulation += candidates.Length;
            if (candidates.Length < target)
            {
                underfilledRegionCount++;
            }

            ValidateRegionCandidates(
                candidates,
                species,
                ref invalidHerdCount,
                ref duplicatePositionCount,
                ref signature);
        }

        /// <summary>1地域の候補について位置重複、群れ数、群れ中心からの最大距離を検査します。</summary>
        private static void ValidateRegionCandidates(
            NativeList<AnimalSpawnCandidate> candidates,
            AnimalSpeciesDefinitionBlob species,
            ref int invalidHerdCount,
            ref int duplicatePositionCount,
            ref ulong signature)
        {
            HashSet<long> positions = new HashSet<long>();
            Dictionary<int, int> herdSizes = new Dictionary<int, int>();
            for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                AnimalSpawnCandidate candidate = candidates[candidateIndex];
                long positionKey = ((long)candidate.TileX << 32) | (uint)candidate.TileY;
                if (!positions.Add(positionKey))
                {
                    duplicatePositionCount++;
                }

                if (!herdSizes.TryGetValue(candidate.HerdLeaderCandidateIndex, out int herdSize))
                {
                    herdSize = 0;
                }

                herdSizes[candidate.HerdLeaderCandidateIndex] = herdSize + 1;
                MixSignature(ref signature, unchecked((ulong)positionKey));
                MixSignature(ref signature, candidate.AppearanceVariant);
                MixSignature(ref signature, unchecked((ulong)candidate.HerdLeaderCandidateIndex));
            }

            foreach (KeyValuePair<int, int> herd in herdSizes)
            {
                bool leaderIndexIsInvalid = herd.Key < 0 || herd.Key >= candidates.Length;
                if (leaderIndexIsInvalid
                    || herd.Value < species.MinimumHerdSize
                    || herd.Value > species.MaximumHerdSize)
                {
                    invalidHerdCount++;
                    continue;
                }

                AnimalSpawnCandidate leader = candidates[herd.Key];
                for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
                {
                    AnimalSpawnCandidate member = candidates[candidateIndex];
                    if (member.HerdLeaderCandidateIndex != herd.Key)
                    {
                        continue;
                    }

                    int distance = Math.Max(
                        Math.Abs(member.TileX - leader.TileX),
                        Math.Abs(member.TileY - leader.TileY));
                    if (distance > species.MaximumWanderDistanceInTiles)
                    {
                        invalidHerdCount++;
                        break;
                    }
                }
            }
        }

        /// <summary>ANIMAL-01Aの正式な鶏初期値を、監査用の実行時定義として返します。</summary>
        private static AnimalSpeciesDefinitionBlob CreateChickenDefinition()
        {
            return new AnimalSpeciesDefinitionBlob
            {
                SpeciesRuntimeId = new DefinitionRuntimeId(1),
                AllowedBiomes = AnimalBiomeMask.TemperateGrassland,
                AllowedGrounds = AnimalGroundMask.Land,
                MinimumHerdSize = 3,
                MaximumHerdSize = 6,
                TargetPopulationPer4096EligibleTiles = 8,
                MinimumIdleTicks = 20,
                MaximumIdleTicks = 80,
                MovementStepIntervalTicks = 5,
                MaximumWanderDistanceInTiles = 8,
                AppearanceVariantCount = 3
            };
        }

        /// <summary>1つの64bit値を比較署名へ順番に混ぜます。</summary>
        private static void MixSignature(ref ulong signature, ulong value)
        {
            for (int byteIndex = 0; byteIndex < 8; byteIndex++)
            {
                signature ^= (byte)(value >> (byteIndex * 8));
                signature *= SignaturePrime;
            }
        }
    }
}
