// このファイルは、C5-Eの複数Seed・連続完成チャンクを生成し、Biome候補・面積・境界・領域を監査します。
// 製品と同じHydrologyDiagnosticChunkと分類器を再利用し、旧単一タイル分類や表示側の再分類を行いません。

using System;
using Farmware.Simulation;
using Unity.Collections;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 完成チャンクの全タイルを固定順で走査し、C5-Eの再現可能な監査結果を作ります。
    /// Scene、ECS World、アセットを変更せず、生成または分類の不整合は部分結果へせず例外で停止します。
    /// </summary>
    public static class BiomeDistributionAnalyzer
    {
        /// <summary>BiomeId.Noneを含む配列スロット数です。</summary>
        public const int BiomeSlotCount = BiomeStableIds.Last + 1;

        /// <summary>通常候補の適性を集計する軸数です。</summary>
        public const int CandidateAxisCount = 8;

        /// <summary>異種境界で絶対差を集計する連続環境軸数です。</summary>
        public const int BoundaryAxisCount = 7;

        /// <summary>異種境界で重複して数える理由bit数です。</summary>
        public const int BoundaryReasonCount = 11;

        /// <summary>FNV-1a 64bit署名の初期値です。</summary>
        private const ulong SignatureOffsetBasis = 14695981039346656037UL;

        /// <summary>FNV-1a 64bit署名で入力値ごとに掛ける素数です。</summary>
        private const ulong SignaturePrime = 1099511628211UL;

        /// <summary>
        /// 管理配列へ保持する1タイルの監査用スナップショットです。
        /// NativeArrayの寿命をチャンク単位に限定しつつ、チャンク境界をまたぐ隣接と領域を後で調べます。
        /// </summary>
        private struct BiomeAuditSample
        {
            /// <summary>新18Biome分類対象の陸地ならtrueです。</summary>
            public bool IsLand;
            /// <summary>特殊優先適用後の最終Biomeです。</summary>
            public BiomeId FinalBiome;
            /// <summary>特殊優先前の通常首位Biomeです。</summary>
            public BiomeId NormalWinner;
            /// <summary>最終Biomeを決めた優先段階です。</summary>
            public BiomeSelectionReason SelectionReason;
            /// <summary>地形気温の連続値です。</summary>
            public ushort TerrainTemperature;
            /// <summary>基礎湿度の連続値です。</summary>
            public ushort BaseMoisture;
            /// <summary>標高の連続値です。</summary>
            public ushort Elevation;
            /// <summary>木本植生ポテンシャルの連続値です。</summary>
            public ushort WoodyVegetation;
            /// <summary>地形起伏の連続値です。</summary>
            public ushort TerrainRuggedness;
            /// <summary>火山活動度の連続値です。</summary>
            public ushort VolcanicActivity;
            /// <summary>海洋影響の連続値です。</summary>
            public ushort CoastalInfluence;
            /// <summary>固定地質分類です。</summary>
            public GeologyClass GeologyClass;
            /// <summary>淡水接続・湿原資格・広域規模の固定水文bitです。</summary>
            public GeneratedHydrologyTileFlags HydrologyFlags;
        }

        /// <summary>
        /// 複数Seedをまたいで加算する可変集計です。
        /// Analyzer内部だけが変更し、完了時に結果型へ配列の所有権を渡します。
        /// </summary>
        private sealed class AuditAccumulator
        {
            /// <summary>Biome別の最終面積です。</summary>
            public readonly int[] AreaCounts = new int[BiomeSlotCount];
            /// <summary>Biome別の通常候補資格件数です。</summary>
            public readonly int[] CandidateCounts = new int[BiomeSlotCount];
            /// <summary>Biome別の通常首位件数です。</summary>
            public readonly int[] NormalWinnerCounts = new int[BiomeSlotCount];
            /// <summary>Biome・候補軸別の適性合計です。</summary>
            public readonly long[] AxisFitSums = new long[BiomeSlotCount * CandidateAxisCount];
            /// <summary>Biome・候補軸別に適性へDefinition重みを掛けたScore寄与合計です。</summary>
            public readonly long[] AxisContributionSums = new long[BiomeSlotCount * CandidateAxisCount];
            /// <summary>順不同Biomeペア別の陸地隣接辺数です。</summary>
            public readonly int[] AdjacencyCounts = new int[BiomeSlotCount * BiomeSlotCount];
            /// <summary>順不同Biomeペア・理由別の成立件数です。</summary>
            public readonly int[] BoundaryReasonCounts = new int[BiomeSlotCount * BiomeSlotCount * BoundaryReasonCount];
            /// <summary>順不同Biomeペア・環境軸別の絶対差合計です。</summary>
            public readonly long[] BoundaryDifferenceSums = new long[BiomeSlotCount * BiomeSlotCount * BoundaryAxisCount];
            /// <summary>順不同Biomeペア・環境軸別の最大絶対差です。</summary>
            public readonly ushort[] BoundaryDifferenceMaxima = new ushort[BiomeSlotCount * BiomeSlotCount * BoundaryAxisCount];
            /// <summary>Biome別の4近傍連結領域数です。</summary>
            public readonly int[] RegionCounts = new int[BiomeSlotCount];
            /// <summary>Biome別の最大4近傍連結領域面積です。</summary>
            public readonly int[] MaximumRegionAreas = new int[BiomeSlotCount];
            /// <summary>Biome別の面積1〜3タイル領域数です。</summary>
            public readonly int[] SmallRegionCounts = new int[BiomeSlotCount];
            /// <summary>Biome別の監査窓端へ接した領域数です。</summary>
            public readonly int[] ClippedRegionCounts = new int[BiomeSlotCount];
            /// <summary>海域を含む走査済みタイル総数です。</summary>
            public int TotalTiles;
            /// <summary>新18Biome分類対象の陸地タイル総数です。</summary>
            public int LandTiles;
            /// <summary>BiomeId.Noneとして除外した海域タイル総数です。</summary>
            public int OceanTiles;
            /// <summary>東・北へ一度だけ数えた陸地隣接辺総数です。</summary>
            public int TotalLandAdjacencyEdges;
            /// <summary>最終Biomeが異なる陸地境界総数です。</summary>
            public int DifferentBiomeEdges;
            /// <summary>活動的火山中核優先で選ばれたタイル数です。</summary>
            public int VolcanicCoreSelections;
            /// <summary>通常候補から火山Biomeになった周縁タイル数です。</summary>
            public int VolcanicFringeSelections;
            /// <summary>淡水湿原・氾濫原への昇格数です。</summary>
            public int FreshwaterMarshPromotions;
            /// <summary>湿地林・沼沢林への昇格数です。</summary>
            public int SwampForestPromotions;
            /// <summary>固定順の走査結果から更新する比較用署名です。</summary>
            public ulong Signature = SignatureOffsetBasis;
        }

        /// <summary>
        /// 指定した全Seedで連続完成チャンクを生成し、候補、最終面積、全隣接、境界理由、領域形状を返します。
        /// </summary>
        /// <param name="auditSettings">Seed、チャンク半径、GeneratorVersionを固定した監査条件です。</param>
        /// <returns>全Seedが候補なしや不一致なく完走した場合の不変な監査結果です。</returns>
        public static BiomeDistributionAuditResult Analyze(BiomeDistributionAuditSettings auditSettings)
        {
            if (auditSettings == null)
            {
                throw new ArgumentNullException(nameof(auditSettings));
            }

            BiomeClassificationSettings biomeSettings = BiomeDefinitionCatalog.CreateDefault();
            if (!BiomeDefinitionCatalog.ValidateSettings(biomeSettings))
            {
                throw new InvalidOperationException("新18Biome Definitionの既定設定が検証に失敗しました。");
            }

            int chunksPerAxis = checked((auditSettings.ChunkRadius * 2) + 1);
            int chunksPerSeed = checked(chunksPerAxis * chunksPerAxis);
            int tilesPerAxis = checked(chunksPerAxis * WorldGridConstants.LogicalChunkSize);
            int tilesPerSeed = checked(tilesPerAxis * tilesPerAxis);
            var accumulator = new AuditAccumulator();

            for (int seedIndex = 0; seedIndex < auditSettings.SeedTexts.Count; seedIndex++)
            {
                WorldGenerationSettings worldSettings = WorldGenerationSettings.CreateDefault(
                    WorldSeedUtility.FromString(auditSettings.SeedTexts[seedIndex]),
                    auditSettings.GeneratorVersion);
                var samples = new BiomeAuditSample[tilesPerSeed];
                GenerateSeedSamples(
                    worldSettings,
                    biomeSettings,
                    auditSettings.ChunkRadius,
                    tilesPerAxis,
                    samples,
                    accumulator);
                AuditAdjacency(worldSettings, tilesPerAxis, samples, accumulator);
                AuditRegions(tilesPerAxis, samples, accumulator);
            }

            return new BiomeDistributionAuditResult(
                accumulator.AreaCounts,
                accumulator.CandidateCounts,
                accumulator.NormalWinnerCounts,
                accumulator.AxisFitSums,
                accumulator.AxisContributionSums,
                accumulator.AdjacencyCounts,
                accumulator.BoundaryReasonCounts,
                accumulator.BoundaryDifferenceSums,
                accumulator.BoundaryDifferenceMaxima,
                accumulator.RegionCounts,
                accumulator.MaximumRegionAreas,
                accumulator.SmallRegionCounts,
                accumulator.ClippedRegionCounts,
                auditSettings.SeedTexts.Count,
                chunksPerSeed,
                accumulator.TotalTiles,
                accumulator.LandTiles,
                accumulator.OceanTiles,
                accumulator.TotalLandAdjacencyEdges,
                accumulator.DifferentBiomeEdges,
                accumulator.VolcanicCoreSelections,
                accumulator.VolcanicFringeSelections,
                accumulator.FreshwaterMarshPromotions,
                accumulator.SwampForestPromotions,
                accumulator.Signature);
        }

        /// <summary>
        /// 1 Seedの連続チャンクを製品順で完成させ、管理配列へ行優先で格納します。
        /// </summary>
        private static void GenerateSeedSamples(
            in WorldGenerationSettings worldSettings,
            in BiomeClassificationSettings biomeSettings,
            int chunkRadius,
            int tilesPerAxis,
            BiomeAuditSample[] samples,
            AuditAccumulator accumulator)
        {
            for (int chunkY = -chunkRadius; chunkY <= chunkRadius; chunkY++)
            {
                for (int chunkX = -chunkRadius; chunkX <= chunkRadius; chunkX++)
                {
                    var coordinate = new ChunkCoordinate(WorldLayer.Surface, chunkX, chunkY);
                    if (!HydrologyDiagnosticChunk.TryGenerate(
                            worldSettings,
                            biomeSettings,
                            coordinate,
                            out HydrologyDiagnosticChunk chunk,
                            out HydrologyDiagnosticGenerationStage stage))
                    {
                        throw new InvalidOperationException(
                            $"完成チャンク生成に失敗しました。Seed={worldSettings.WorldSeed}, Chunk=({chunkX},{chunkY}), Stage={stage}");
                    }

                    // Persistent NativeArrayを保持する診断チャンクは、1チャンクの転記後すぐ解放して監査時のピークメモリを抑えます。
                    using (chunk)
                    {
                        CopyChunkSamples(
                            worldSettings,
                            biomeSettings,
                            chunkRadius,
                            tilesPerAxis,
                            chunkX,
                            chunkY,
                            chunk,
                            samples,
                            accumulator);
                    }
                }
            }
        }

        /// <summary>
        /// 完成済み4096タイルを再診断し、保存値一致、候補存在、特殊優先契約を検証して集計します。
        /// </summary>
        private static void CopyChunkSamples(
            in WorldGenerationSettings worldSettings,
            in BiomeClassificationSettings biomeSettings,
            int chunkRadius,
            int tilesPerAxis,
            int chunkX,
            int chunkY,
            HydrologyDiagnosticChunk chunk,
            BiomeAuditSample[] samples,
            AuditAccumulator accumulator)
        {
            int chunkOffsetX = checked((chunkX + chunkRadius) * WorldGridConstants.LogicalChunkSize);
            int chunkOffsetY = checked((chunkY + chunkRadius) * WorldGridConstants.LogicalChunkSize);
            for (int localY = 0; localY < WorldGridConstants.LogicalChunkSize; localY++)
            {
                for (int localX = 0; localX < WorldGridConstants.LogicalChunkSize; localX++)
                {
                    int localIndex = (localY * WorldGridConstants.LogicalChunkSize) + localX;
                    int sampleIndex = ((chunkOffsetY + localY) * tilesPerAxis) + chunkOffsetX + localX;
                    int worldTileX = checked((chunkX * WorldGridConstants.LogicalChunkSize) + localX);
                    int worldTileY = checked((chunkY * WorldGridConstants.LogicalChunkSize) + localY);
                    GeneratedTile tile = chunk.TerrainTiles[localIndex];
                    GeneratedHydrologyTile hydrologyTile = chunk.HydrologyTiles[localIndex];
                    if (!DeterministicEnvironmentGenerator.TryGenerate(
                            worldSettings,
                            worldTileX,
                            worldTileY,
                            out GeneratedEnvironmentData environment))
                    {
                        throw new InvalidOperationException($"環境再生成に失敗しました。Tile=({worldTileX},{worldTileY})");
                    }

                    VerifySavedEnvironment(tile, environment, worldTileX, worldTileY);
                    accumulator.TotalTiles++;
                    if (environment.Elevation < worldSettings.SeaLevelThreshold)
                    {
                        if (tile.Biome != BiomeId.None)
                        {
                            throw new InvalidOperationException($"海域に地上Biomeが保存されています。Tile=({worldTileX},{worldTileY}), Biome={tile.Biome}");
                        }

                        accumulator.OceanTiles++;
                        accumulator.Signature = MixSignature(accumulator.Signature, 0);
                        continue;
                    }

                    BiomeClassificationInput input = new BiomeClassificationInput
                    {
                        Environment = environment,
                        GeologyClass = tile.GeologyClass,
                        GeologyFlags = tile.GeologyFlags,
                        SeaLevelThreshold = worldSettings.SeaLevelThreshold,
                        HydrologyFlags = hydrologyTile.Flags
                    };
                    if (!DeterministicBiomeClassifier.TrySelectFinalBiome(
                            biomeSettings,
                            worldSettings.WorldSeed,
                            worldSettings.GeneratorVersion,
                            input,
                            out FinalBiomeSelection selection)
                        || selection.StableId != (byte)tile.Biome)
                    {
                        throw new InvalidOperationException($"候補なし、または完成Biomeとの不一致です。Tile=({worldTileX},{worldTileY}), Saved={tile.Biome}");
                    }

                    BiomeAuditSample sample = CreateSample(tile, hydrologyTile, selection, environment);
                    samples[sampleIndex] = sample;
                    accumulator.LandTiles++;
                    accumulator.AreaCounts[selection.StableId]++;
                    accumulator.NormalWinnerCounts[selection.NormalCandidates.First.StableId]++;
                    AuditCandidates(worldSettings, biomeSettings, input, accumulator);
                    AuditSelectionReason(biomeSettings, input, selection, accumulator, worldTileX, worldTileY);
                    accumulator.Signature = MixSignature(accumulator.Signature, selection.StableId);
                    accumulator.Signature = MixSignature(accumulator.Signature, selection.NormalCandidates.First.StableId);
                    accumulator.Signature = MixSignature(accumulator.Signature, (byte)selection.Reason);
                    accumulator.Signature = MixSignature(accumulator.Signature, selection.NormalCandidates.CandidateCount);
                }
            }
        }

        /// <summary>完成Bufferへ保存された7環境軸が同座標の純粋再生成値と一致することを保証します。</summary>
        private static void VerifySavedEnvironment(
            in GeneratedTile tile,
            in GeneratedEnvironmentData environment,
            int worldTileX,
            int worldTileY)
        {
            if (tile.Elevation != environment.Elevation
                || tile.Temperature != environment.BaseTemperature
                || tile.Moisture != environment.BaseMoisture
                || tile.TerrainTemperature != environment.TerrainTemperature
                || tile.WoodyVegetationPotential != environment.WoodyVegetationPotential
                || tile.TerrainRuggedness != environment.TerrainRuggedness
                || tile.CoastalInfluence != environment.CoastalInfluence
                || tile.VolcanicActivityPotential != environment.VolcanicActivityPotential)
            {
                throw new InvalidOperationException($"完成Bufferと環境再生成値が一致しません。Tile=({worldTileX},{worldTileY})");
            }
        }

        /// <summary>完成タイル、自然水文、分類診断を隣接・領域監査用の小さな値へ変換します。</summary>
        private static BiomeAuditSample CreateSample(
            in GeneratedTile tile,
            in GeneratedHydrologyTile hydrologyTile,
            in FinalBiomeSelection selection,
            in GeneratedEnvironmentData environment)
        {
            return new BiomeAuditSample
            {
                IsLand = true,
                FinalBiome = tile.Biome,
                NormalWinner = (BiomeId)selection.NormalCandidates.First.StableId,
                SelectionReason = selection.Reason,
                TerrainTemperature = environment.TerrainTemperature,
                BaseMoisture = environment.BaseMoisture,
                Elevation = environment.Elevation,
                WoodyVegetation = environment.WoodyVegetationPotential,
                TerrainRuggedness = environment.TerrainRuggedness,
                VolcanicActivity = environment.VolcanicActivityPotential,
                CoastalInfluence = environment.CoastalInfluence,
                GeologyClass = tile.GeologyClass,
                HydrologyFlags = hydrologyTile.Flags
            };
        }

        /// <summary>1陸地タイルについて、通常Definitionごとの候補資格と8軸適性を集計します。</summary>
        private static void AuditCandidates(
            in WorldGenerationSettings worldSettings,
            in BiomeClassificationSettings biomeSettings,
            in BiomeClassificationInput input,
            AuditAccumulator accumulator)
        {
            for (int definitionIndex = 0; definitionIndex < biomeSettings.Definitions.Length; definitionIndex++)
            {
                BiomeDefinition definition = biomeSettings.Definitions[definitionIndex];
                if (!DeterministicBiomeClassifier.TryEvaluateNormalCandidate(
                        biomeSettings,
                        worldSettings.WorldSeed,
                        worldSettings.GeneratorVersion,
                        definition,
                        input,
                        out BiomeCandidateEvaluation evaluation))
                {
                    continue;
                }

                int stableId = evaluation.StableId;
                accumulator.CandidateCounts[stableId]++;
                AddCandidateFits(
                    accumulator.AxisFitSums,
                    accumulator.AxisContributionSums,
                    stableId,
                    evaluation,
                    definition.AxisWeights);
                accumulator.Signature = MixCandidateEvaluation(accumulator.Signature, evaluation);
            }
        }

        /// <summary>候補評価の8軸適性と重み込みScore寄与をBiome別の固定スロットへ加算します。</summary>
        private static void AddCandidateFits(
            long[] fitSums,
            long[] contributionSums,
            int stableId,
            in BiomeCandidateEvaluation evaluation,
            in BiomeAxisWeights weights)
        {
            int offset = stableId * CandidateAxisCount;
            AddAxisValue(fitSums, contributionSums, offset, BiomeAuditAxis.TerrainTemperature, evaluation.TerrainTemperatureFit, weights.TerrainTemperature);
            AddAxisValue(fitSums, contributionSums, offset, BiomeAuditAxis.BaseMoisture, evaluation.BaseMoistureFit, weights.BaseMoisture);
            AddAxisValue(fitSums, contributionSums, offset, BiomeAuditAxis.Elevation, evaluation.ElevationFit, weights.Elevation);
            AddAxisValue(fitSums, contributionSums, offset, BiomeAuditAxis.WoodyVegetation, evaluation.WoodyVegetationFit, weights.WoodyVegetation);
            AddAxisValue(fitSums, contributionSums, offset, BiomeAuditAxis.TerrainRuggedness, evaluation.TerrainRuggednessFit, weights.TerrainRuggedness);
            AddAxisValue(fitSums, contributionSums, offset, BiomeAuditAxis.Geology, evaluation.GeologyFit, weights.Geology);
            AddAxisValue(fitSums, contributionSums, offset, BiomeAuditAxis.VolcanicActivity, evaluation.VolcanicActivityFit, weights.VolcanicActivity);
            AddAxisValue(fitSums, contributionSums, offset, BiomeAuditAxis.CoastalInfluence, evaluation.CoastalInfluenceFit, weights.CoastalInfluence);
        }

        /// <summary>1軸の適性と、64bitへ拡張してから重みを掛けたScore寄与を同じ軸スロットへ加算します。</summary>
        private static void AddAxisValue(
            long[] fitSums,
            long[] contributionSums,
            int offset,
            BiomeAuditAxis axis,
            ushort fit,
            byte weight)
        {
            int index = offset + (int)axis;
            fitSums[index] += fit;
            contributionSums[index] += (long)fit * weight;
        }

        /// <summary>候補のID、8軸適性、Score、同点Variationを固定順で比較署名へ混ぜます。</summary>
        private static ulong MixCandidateEvaluation(ulong signature, in BiomeCandidateEvaluation evaluation)
        {
            signature = MixSignature(signature, evaluation.StableId);
            signature = MixSignature(signature, evaluation.TerrainTemperatureFit);
            signature = MixSignature(signature, evaluation.BaseMoistureFit);
            signature = MixSignature(signature, evaluation.ElevationFit);
            signature = MixSignature(signature, evaluation.WoodyVegetationFit);
            signature = MixSignature(signature, evaluation.TerrainRuggednessFit);
            signature = MixSignature(signature, evaluation.GeologyFit);
            signature = MixSignature(signature, evaluation.VolcanicActivityFit);
            signature = MixSignature(signature, evaluation.CoastalInfluenceFit);
            signature = MixSignature(signature, (ulong)evaluation.Score);
            return MixSignature(signature, evaluation.CandidateVariation);
        }

        /// <summary>火山中核／周縁と水文特殊Host契約を検証し、選択理由別件数を集計します。</summary>
        private static void AuditSelectionReason(
            in BiomeClassificationSettings biomeSettings,
            in BiomeClassificationInput input,
            in FinalBiomeSelection selection,
            AuditAccumulator accumulator,
            int worldTileX,
            int worldTileY)
        {
            switch (selection.Reason)
            {
                case BiomeSelectionReason.NormalCandidate:
                    if (selection.StableId == BiomeStableIds.VolcanicGeothermal)
                    {
                        accumulator.VolcanicFringeSelections++;
                    }
                    break;
                case BiomeSelectionReason.ActiveVolcanicCore:
                    if (selection.StableId != BiomeStableIds.VolcanicGeothermal
                        || (input.GeologyFlags & GeologyDefinitionFlags.SupportsActiveVolcanism) == 0
                        || input.Environment.VolcanicActivityPotential < DeterministicEnvironmentGenerator.EnvironmentBand5Threshold)
                    {
                        throw new InvalidOperationException($"火山中核優先の条件が不整合です。Tile=({worldTileX},{worldTileY})");
                    }
                    accumulator.VolcanicCoreSelections++;
                    break;
                case BiomeSelectionReason.FreshwaterMarshPromotion:
                    if (selection.StableId != BiomeStableIds.FreshwaterMarshFloodplain
                        || !ContainsHost(biomeSettings.FreshwaterMarshHostIds, selection.NormalCandidates.First.StableId))
                    {
                        throw new InvalidOperationException($"淡水湿原のHost契約が不整合です。Tile=({worldTileX},{worldTileY})");
                    }
                    accumulator.FreshwaterMarshPromotions++;
                    break;
                case BiomeSelectionReason.SwampForestPromotion:
                    if (selection.StableId != BiomeStableIds.SwampForest
                        || !ContainsHost(biomeSettings.SwampForestHostIds, selection.NormalCandidates.First.StableId))
                    {
                        throw new InvalidOperationException($"湿地林のHost契約が不整合です。Tile=({worldTileX},{worldTileY})");
                    }
                    accumulator.SwampForestPromotions++;
                    break;
                default:
                    throw new InvalidOperationException($"地上タイルの選択理由が未設定です。Tile=({worldTileX},{worldTileY})");
            }
        }

        /// <summary>固定長Host一覧に通常首位IDが含まれるか、登録順に依存せず確認します。</summary>
        private static bool ContainsHost(in FixedList32Bytes<byte> hostIds, byte stableId)
        {
            for (int index = 0; index < hostIds.Length; index++)
            {
                if (hostIds[index] == stableId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>東と北だけを調べ、同じ辺を重複せず全陸地隣接と異種境界へ加算します。</summary>
        private static void AuditAdjacency(
            in WorldGenerationSettings worldSettings,
            int tilesPerAxis,
            BiomeAuditSample[] samples,
            AuditAccumulator accumulator)
        {
            for (int y = 0; y < tilesPerAxis; y++)
            {
                for (int x = 0; x < tilesPerAxis; x++)
                {
                    int index = (y * tilesPerAxis) + x;
                    if (!samples[index].IsLand)
                    {
                        continue;
                    }

                    if (x + 1 < tilesPerAxis)
                    {
                        AuditEdge(worldSettings, samples[index], samples[index + 1], accumulator);
                    }

                    if (y + 1 < tilesPerAxis)
                    {
                        AuditEdge(worldSettings, samples[index], samples[index + tilesPerAxis], accumulator);
                    }
                }
            }
        }

        /// <summary>1つの陸地隣接辺を順不同ペアへ集計し、異種なら差と複数理由を記録します。</summary>
        private static void AuditEdge(
            in WorldGenerationSettings worldSettings,
            in BiomeAuditSample first,
            in BiomeAuditSample second,
            AuditAccumulator accumulator)
        {
            if (!second.IsLand)
            {
                return;
            }

            int pairIndex = GetPairIndex(first.FinalBiome, second.FinalBiome);
            accumulator.TotalLandAdjacencyEdges++;
            accumulator.AdjacencyCounts[pairIndex]++;
            accumulator.Signature = MixSignature(accumulator.Signature, (ulong)pairIndex);
            if (first.FinalBiome == second.FinalBiome)
            {
                return;
            }

            accumulator.DifferentBiomeEdges++;
            AddBoundaryDifference(accumulator, pairIndex, BiomeBoundaryAxis.TerrainTemperature, first.TerrainTemperature, second.TerrainTemperature);
            AddBoundaryDifference(accumulator, pairIndex, BiomeBoundaryAxis.BaseMoisture, first.BaseMoisture, second.BaseMoisture);
            AddBoundaryDifference(accumulator, pairIndex, BiomeBoundaryAxis.Elevation, first.Elevation, second.Elevation);
            AddBoundaryDifference(accumulator, pairIndex, BiomeBoundaryAxis.WoodyVegetation, first.WoodyVegetation, second.WoodyVegetation);
            AddBoundaryDifference(accumulator, pairIndex, BiomeBoundaryAxis.TerrainRuggedness, first.TerrainRuggedness, second.TerrainRuggedness);
            AddBoundaryDifference(accumulator, pairIndex, BiomeBoundaryAxis.VolcanicActivity, first.VolcanicActivity, second.VolcanicActivity);
            AddBoundaryDifference(accumulator, pairIndex, BiomeBoundaryAxis.CoastalInfluence, first.CoastalInfluence, second.CoastalInfluence);
            BiomeBoundaryReasonFlags reasons = DetermineBoundaryReasons(worldSettings, first, second);
            for (int reasonIndex = 0; reasonIndex < BoundaryReasonCount; reasonIndex++)
            {
                BiomeBoundaryReasonFlags reason = (BiomeBoundaryReasonFlags)(1 << reasonIndex);
                if ((reasons & reason) != 0)
                {
                    accumulator.BoundaryReasonCounts[(pairIndex * BoundaryReasonCount) + reasonIndex]++;
                }
            }

            accumulator.Signature = MixSignature(accumulator.Signature, (ulong)reasons);
        }

        /// <summary>1境界の指定連続軸について絶対差合計と最大値を更新します。</summary>
        private static void AddBoundaryDifference(
            AuditAccumulator accumulator,
            int pairIndex,
            BiomeBoundaryAxis axis,
            ushort first,
            ushort second)
        {
            ushort difference = (ushort)Math.Abs((int)first - second);
            int index = (pairIndex * BoundaryAxisCount) + (int)axis;
            accumulator.BoundaryDifferenceSums[index] += difference;
            if (difference > accumulator.BoundaryDifferenceMaxima[index])
            {
                accumulator.BoundaryDifferenceMaxima[index] = difference;
            }

            // 境界本数と理由が同じでも環境差が変わった調整を署名で検出できるよう、軸と差を固定順で混ぜます。
            accumulator.Signature = MixSignature(accumulator.Signature, (ulong)axis);
            accumulator.Signature = MixSignature(accumulator.Signature, difference);
        }

        /// <summary>分類首位、離散環境区分、地質、水文、特殊優先の差を重複可能な理由bitへ変換します。</summary>
        private static BiomeBoundaryReasonFlags DetermineBoundaryReasons(
            in WorldGenerationSettings worldSettings,
            in BiomeAuditSample first,
            in BiomeAuditSample second)
        {
            BiomeBoundaryReasonFlags reasons = BiomeBoundaryReasonFlags.None;
            if (first.NormalWinner != second.NormalWinner) reasons |= BiomeBoundaryReasonFlags.NormalWinner;
            if (DeterministicEnvironmentGenerator.ClassifyTerrainTemperature(first.TerrainTemperature, worldSettings)
                != DeterministicEnvironmentGenerator.ClassifyTerrainTemperature(second.TerrainTemperature, worldSettings)) reasons |= BiomeBoundaryReasonFlags.TemperatureBand;
            if (DeterministicEnvironmentGenerator.ClassifyBaseMoisture(first.BaseMoisture, worldSettings)
                != DeterministicEnvironmentGenerator.ClassifyBaseMoisture(second.BaseMoisture, worldSettings)) reasons |= BiomeBoundaryReasonFlags.MoistureBand;
            if (DeterministicEnvironmentGenerator.ClassifyElevation(first.Elevation, worldSettings)
                != DeterministicEnvironmentGenerator.ClassifyElevation(second.Elevation, worldSettings)) reasons |= BiomeBoundaryReasonFlags.ElevationBand;
            if (DeterministicEnvironmentGenerator.ClassifyContinuousEnvironment(first.WoodyVegetation)
                != DeterministicEnvironmentGenerator.ClassifyContinuousEnvironment(second.WoodyVegetation)) reasons |= BiomeBoundaryReasonFlags.WoodyBand;
            if (DeterministicEnvironmentGenerator.ClassifyContinuousEnvironment(first.TerrainRuggedness)
                != DeterministicEnvironmentGenerator.ClassifyContinuousEnvironment(second.TerrainRuggedness)) reasons |= BiomeBoundaryReasonFlags.RuggednessBand;
            if (first.GeologyClass != second.GeologyClass) reasons |= BiomeBoundaryReasonFlags.Geology;
            if (DeterministicEnvironmentGenerator.ClassifyContinuousEnvironment(first.VolcanicActivity)
                != DeterministicEnvironmentGenerator.ClassifyContinuousEnvironment(second.VolcanicActivity)) reasons |= BiomeBoundaryReasonFlags.VolcanicBand;
            if (DeterministicEnvironmentGenerator.ClassifyContinuousEnvironment(first.CoastalInfluence)
                != DeterministicEnvironmentGenerator.ClassifyContinuousEnvironment(second.CoastalInfluence)) reasons |= BiomeBoundaryReasonFlags.CoastalBand;
            if (first.SelectionReason != second.SelectionReason) reasons |= BiomeBoundaryReasonFlags.SelectionReason;
            if (first.HydrologyFlags != second.HydrologyFlags) reasons |= BiomeBoundaryReasonFlags.Hydrology;
            return reasons;
        }

        /// <summary>1 Seedの全陸地を4近傍探索し、領域面積、小領域、窓端接触をBiome別に集計します。</summary>
        private static void AuditRegions(int tilesPerAxis, BiomeAuditSample[] samples, AuditAccumulator accumulator)
        {
            var visited = new bool[samples.Length];
            var queue = new int[samples.Length];
            for (int start = 0; start < samples.Length; start++)
            {
                if (visited[start] || !samples[start].IsLand)
                {
                    continue;
                }

                BiomeId biome = samples[start].FinalBiome;
                int head = 0;
                int tail = 0;
                int area = 0;
                bool clipped = false;
                queue[tail++] = start;
                visited[start] = true;
                while (head < tail)
                {
                    int current = queue[head++];
                    int x = current % tilesPerAxis;
                    int y = current / tilesPerAxis;
                    area++;
                    clipped |= x == 0 || y == 0 || x == tilesPerAxis - 1 || y == tilesPerAxis - 1;
                    TryEnqueueRegionNeighbor(current - 1, x > 0, biome, samples, visited, queue, ref tail);
                    TryEnqueueRegionNeighbor(current + 1, x + 1 < tilesPerAxis, biome, samples, visited, queue, ref tail);
                    TryEnqueueRegionNeighbor(current - tilesPerAxis, y > 0, biome, samples, visited, queue, ref tail);
                    TryEnqueueRegionNeighbor(current + tilesPerAxis, y + 1 < tilesPerAxis, biome, samples, visited, queue, ref tail);
                }

                int biomeIndex = (int)biome;
                accumulator.RegionCounts[biomeIndex]++;
                accumulator.MaximumRegionAreas[biomeIndex] = Math.Max(accumulator.MaximumRegionAreas[biomeIndex], area);
                if (area <= 3) accumulator.SmallRegionCounts[biomeIndex]++;
                if (clipped) accumulator.ClippedRegionCounts[biomeIndex]++;
                accumulator.Signature = MixSignature(accumulator.Signature, (ulong)biomeIndex);
                accumulator.Signature = MixSignature(accumulator.Signature, (ulong)area);
                accumulator.Signature = MixSignature(accumulator.Signature, clipped ? 1UL : 0UL);
            }
        }

        /// <summary>4近傍探索の範囲内・未訪問・同一Biomeだけを固定配列キューへ追加します。</summary>
        private static void TryEnqueueRegionNeighbor(
            int neighbor,
            bool inside,
            BiomeId biome,
            BiomeAuditSample[] samples,
            bool[] visited,
            int[] queue,
            ref int tail)
        {
            if (!inside || visited[neighbor] || !samples[neighbor].IsLand || samples[neighbor].FinalBiome != biome)
            {
                return;
            }

            visited[neighbor] = true;
            queue[tail++] = neighbor;
        }

        /// <summary>2つのBiome IDを小さい順へ正規化し、順不同ペアの固定添字を返します。</summary>
        private static int GetPairIndex(BiomeId first, BiomeId second)
        {
            int low = Math.Min((int)first, (int)second);
            int high = Math.Max((int)first, (int)second);
            return (low * BiomeSlotCount) + high;
        }

        /// <summary>1つの符号なし値をFNV-1a 64bit署名へ8byte固定順で混ぜます。</summary>
        private static ulong MixSignature(ulong signature, ulong value)
        {
            for (int byteIndex = 0; byteIndex < sizeof(ulong); byteIndex++)
            {
                signature ^= (byte)(value >> (byteIndex * 8));
                signature *= SignaturePrime;
            }

            return signature;
        }
    }
}
