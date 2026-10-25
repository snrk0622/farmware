// このファイルは、C6-Aの地形材質設定を検証し、手描き入力から完成PNG Spriteを生成します。
// 画素規則はTerrainTileArtPixelComposerへ分離し、このクラスはAsset読込、Fingerprint、出力、Import設定を担当します。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Farmware.WorldGeneration.Authoring;
using UnityEditor;
using UnityEngine;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 1材質について、手描き入力、Editor生成物、Runtime参照候補のSprite数をまとめます。
    /// Inspectorで差分数と生成費用を事前確認するために使用します。
    /// </summary>
    public readonly struct TerrainTileArtSpriteCounts
    {
        /// <summary>手描きする入力Sprite数を返します。</summary>
        public int HandDrawnInputCount { get; }

        /// <summary>EditorがPNGとして生成する完成Sprite数を返します。</summary>
        public int GeneratedOutputCount { get; }

        /// <summary>将来Runtimeが選択対象にする完成Sprite候補数を返します。</summary>
        public int RuntimeCandidateCount { get; }

        /// <summary>WallTopの16接続Sprite数を返します。</summary>
        public int WallTopOutputCount { get; }

        /// <summary>Ground用のFaceと境界Overlayの全組合せ数を返します。</summary>
        public int GroundBoundaryOutputCount { get; }

        /// <summary>暗色化が有効な場合に追加するWall用境界合成Sprite数を返します。</summary>
        public int WallBoundaryOutputCount { get; }

        /// <summary>
        /// 計算済みの各Sprite数を変更不能な値としてまとめます。
        /// </summary>
        /// <param name="handDrawnInputCount">手描き入力の合計数です。</param>
        /// <param name="generatedOutputCount">Editor生成物の合計数です。</param>
        /// <param name="runtimeCandidateCount">Runtime参照候補の合計数です。</param>
        /// <param name="wallTopOutputCount">WallTop生成物の内訳です。</param>
        /// <param name="groundBoundaryOutputCount">Ground用境界合成の内訳です。</param>
        /// <param name="wallBoundaryOutputCount">Wall用境界合成の内訳です。</param>
        public TerrainTileArtSpriteCounts(
            int handDrawnInputCount,
            int generatedOutputCount,
            int runtimeCandidateCount,
            int wallTopOutputCount,
            int groundBoundaryOutputCount,
            int wallBoundaryOutputCount)
        {
            HandDrawnInputCount = handDrawnInputCount;
            GeneratedOutputCount = generatedOutputCount;
            RuntimeCandidateCount = runtimeCandidateCount;
            WallTopOutputCount = wallTopOutputCount;
            GroundBoundaryOutputCount = groundBoundaryOutputCount;
            WallBoundaryOutputCount = wallBoundaryOutputCount;
        }
    }

    /// <summary>
    /// 生成前検証で見つかったエラーと警告を保持します。
    /// エラーが1件でもあれば生成を拒否し、警告だけなら明示したうえで生成できます。
    /// </summary>
    public sealed class TerrainTileArtValidationReport
    {
        // 呼び出し元が追加順のまま表示できる、生成を止める問題の一覧です。
        private readonly List<string> errors = new List<string>();

        // 生成は可能でも、人間が差分数や制作状態を確認すべき事項の一覧です。
        private readonly List<string> warnings = new List<string>();

        /// <summary>生成を止める問題を追加順で返します。</summary>
        public IReadOnlyList<string> Errors => errors;

        /// <summary>生成前に確認すべき注意を追加順で返します。</summary>
        public IReadOnlyList<string> Warnings => warnings;

        /// <summary>生成を止める問題が1件以上ある場合はtrueを返します。</summary>
        public bool HasErrors => errors.Count > 0;

        /// <summary>
        /// 生成を拒否する理由を1件追加します。
        /// </summary>
        /// <param name="message">Inspectorへそのまま表示できる日本語の理由です。</param>
        public void AddError(string message)
        {
            errors.Add(message);
        }

        /// <summary>
        /// 生成可能だが確認してほしい注意を1件追加します。
        /// </summary>
        /// <param name="message">Inspectorへそのまま表示できる日本語の注意です。</param>
        public void AddWarning(string message)
        {
            warnings.Add(message);
        }
    }

    /// <summary>
    /// 1材質の生成完了後に、作成数、保存先、FingerprintをInspectorへ返します。
    /// </summary>
    public sealed class TerrainTileArtGenerationResult
    {
        /// <summary>生成した材質の安定IDを返します。</summary>
        public string MaterialId { get; }

        /// <summary>今回書き込み、Importした完成Sprite数を返します。</summary>
        public int GeneratedSpriteCount { get; }

        /// <summary>材質別出力フォルダーのプロジェクト相対パスを返します。</summary>
        public string OutputFolder { get; }

        /// <summary>正常完了時に記録した入力Fingerprintを返します。</summary>
        public string Fingerprint { get; }

        /// <summary>
        /// 1材質分の正常な生成結果を変更不能な値としてまとめます。
        /// </summary>
        /// <param name="materialId">生成対象の安定材質IDです。</param>
        /// <param name="generatedSpriteCount">生成・ImportしたSprite数です。</param>
        /// <param name="outputFolder">材質別出力フォルダーです。</param>
        /// <param name="fingerprint">生成に使用した入力Fingerprintです。</param>
        public TerrainTileArtGenerationResult(
            string materialId,
            int generatedSpriteCount,
            string outputFolder,
            string fingerprint)
        {
            MaterialId = materialId;
            GeneratedSpriteCount = generatedSpriteCount;
            OutputFolder = outputFolder;
            Fingerprint = fingerprint;
        }
    }

    /// <summary>
    /// 地形材質設定の検証、要再生成判定、PNG生成、Sprite Import設定を一元化します。
    /// メニューや専用Inspectorはこの公開APIを呼び、別の生成規則を持ちません。
    /// </summary>
    public static class TerrainTileArtGenerator
    {
        /// <summary>Fingerprintへ含め、合成規則を変えた際に既存出力を必ず古いと判定する版です。</summary>
        public const int GeneratorVersion = 1;

        /// <summary>入力Spriteと完成Spriteで共通して使用するPixels Per Unitです。</summary>
        public const float PixelsPerUnit = 16f;

        /// <summary>
        /// 1材質の配列数から、仕様書第3.3節の入力・生成・Runtime候補数を計算します。
        /// </summary>
        /// <param name="material">数える材質設定です。</param>
        /// <returns>WallTopと境界合成の内訳を含むSprite数です。</returns>
        public static TerrainTileArtSpriteCounts CalculateSpriteCounts(
            TerrainMaterialArtSource material)
        {
            if (material == null)
            {
                return new TerrainTileArtSpriteCounts(0, 0, 0, 0, 0, 0);
            }

            int groundCount = GetLength(material.GroundTopSprites);
            int faceCount = GetLength(material.FaceSprites);
            int boundaryCount = GetLength(material.FaceBoundaryOverlays);
            int wallSetCount = GetLength(material.WallTopRims);
            int wallTopOutputCount = checked(16 * wallSetCount);
            int groundBoundaryOutputCount = boundaryCount > 0
                ? checked(faceCount * boundaryCount)
                : 0;
            int wallBoundaryOutputCount = material.DarkenWallSurface
                ? groundBoundaryOutputCount
                : 0;
            int handDrawnInputCount = checked(
                groundCount + faceCount + boundaryCount + 5 * wallSetCount);
            int generatedOutputCount = checked(
                wallTopOutputCount
                + groundBoundaryOutputCount
                + wallBoundaryOutputCount);
            int runtimeCandidateCount = checked(
                groundCount
                + faceCount
                + wallTopOutputCount
                + groundBoundaryOutputCount
                + wallBoundaryOutputCount);

            return new TerrainTileArtSpriteCounts(
                handDrawnInputCount,
                generatedOutputCount,
                runtimeCandidateCount,
                wallTopOutputCount,
                groundBoundaryOutputCount,
                wallBoundaryOutputCount);
        }

        /// <summary>
        /// 設定アセット全体を検証し、41材質の準備状況と全入力の問題をまとめます。
        /// </summary>
        /// <param name="settings">検証するC6-A設定アセットです。</param>
        /// <returns>エラーと警告を追加順で保持するReportです。</returns>
        public static TerrainTileArtValidationReport ValidateAll(
            TerrainTileArtGenerationSettings settings)
        {
            TerrainTileArtValidationReport report = new TerrainTileArtValidationReport();
            if (!ValidateSettingsHeader(settings, report))
            {
                return report;
            }

            TerrainMaterialArtSource[] materials = settings.Materials;
            if (materials.Length != 41)
            {
                report.AddWarning(
                    $"最終仕様は41材質ですが、現在の登録数は{materials.Length}件です。"
                    + " C6-Aの代表材質だけを試す間は生成できます。");
            }

            HashSet<string> materialIds = new HashSet<string>(StringComparer.Ordinal);
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                TerrainMaterialArtSource material = materials[materialIndex];
                ValidateMaterial(settings, material, materialIndex, report);
                if (material == null || string.IsNullOrWhiteSpace(material.MaterialId))
                {
                    continue;
                }

                if (!materialIds.Add(material.MaterialId))
                {
                    report.AddError(
                        $"材質[{materialIndex}]のMaterial ID「{material.MaterialId}」が重複しています。");
                }
            }

            return report;
        }

        /// <summary>
        /// 選択した1材質と共通設定だけを検証します。
        /// ほかの未制作材質が空でも、代表材質から段階的に試作できるようにします。
        /// </summary>
        /// <param name="settings">検証するC6-A設定アセットです。</param>
        /// <param name="materialIndex">Materials配列内の0始まりの対象位置です。</param>
        /// <returns>選択材質の生成可否を示すReportです。</returns>
        public static TerrainTileArtValidationReport ValidateMaterial(
            TerrainTileArtGenerationSettings settings,
            int materialIndex)
        {
            TerrainTileArtValidationReport report = new TerrainTileArtValidationReport();
            if (!ValidateSettingsHeader(settings, report))
            {
                return report;
            }

            if (materialIndex < 0 || materialIndex >= settings.Materials.Length)
            {
                report.AddError($"材質Index {materialIndex} はMaterials配列の範囲外です。");
                return report;
            }

            TerrainMaterialArtSource selectedMaterial = settings.Materials[materialIndex];
            ValidateMaterial(settings, selectedMaterial, materialIndex, report);
            if (selectedMaterial == null || string.IsNullOrWhiteSpace(selectedMaterial.MaterialId))
            {
                return report;
            }

            for (int otherIndex = 0; otherIndex < settings.Materials.Length; otherIndex++)
            {
                if (otherIndex == materialIndex)
                {
                    continue;
                }

                TerrainMaterialArtSource otherMaterial = settings.Materials[otherIndex];
                if (otherMaterial != null
                    && string.Equals(
                        selectedMaterial.MaterialId,
                        otherMaterial.MaterialId,
                        StringComparison.Ordinal))
                {
                    report.AddError(
                        $"材質[{materialIndex}]と材質[{otherIndex}]でMaterial ID「{selectedMaterial.MaterialId}」が重複しています。");
                }
            }

            return report;
        }

        /// <summary>
        /// 現在の共通設定、材質設定、入力Sprite内容から、要再生成判定用のFingerprintを作ります。
        /// SpriteのGUIDだけでなく依存Asset Hashを含め、同じファイルを描き直した変更も検出します。
        /// </summary>
        /// <param name="settings">共通内部色と出力先を持つ設定アセットです。</param>
        /// <param name="material">Fingerprintを計算する材質です。</param>
        /// <returns>Unity Hash128を小文字16進文字列にした安定Fingerprintです。</returns>
        public static string CalculateFingerprint(
            TerrainTileArtGenerationSettings settings,
            TerrainMaterialArtSource material)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            StringBuilder source = new StringBuilder(2048);
            source.Append("generator=").Append(GeneratorVersion).Append('|');
            source.Append("output=").Append(settings.OutputRootFolder).Append('|');
            AppendColor(source, settings.WallInteriorDark);
            source.Append("id=").Append(material.MaterialId).Append('|');
            source.Append("widths=")
                .Append(material.TopRimWidth).Append(',')
                .Append(material.SideRimWidth).Append(',')
                .Append(material.BottomRimWidth).Append('|');
            source.Append("darken=").Append(material.DarkenWallSurface).Append('|');
            source.Append("factor=")
                .Append(material.WallDarkenFactor.ToString("R", CultureInfo.InvariantCulture))
                .Append('|');
            AppendSprites(source, "ground", material.GroundTopSprites);
            AppendSprites(source, "face", material.FaceSprites);
            AppendSprites(source, "boundary", material.FaceBoundaryOverlays);

            TerrainWallTopRimInput[] rims = material.WallTopRims;
            source.Append("rims=").Append(GetLength(rims)).Append('|');
            if (rims != null)
            {
                for (int rimIndex = 0; rimIndex < rims.Length; rimIndex++)
                {
                    TerrainWallTopRimInput rim = rims[rimIndex];
                    source.Append("rim[").Append(rimIndex).Append("]=");
                    if (rim == null)
                    {
                        source.Append("null|");
                        continue;
                    }

                    AppendSpriteIdentity(source, rim.TopEdge);
                    AppendSpriteIdentity(source, rim.BottomEdge);
                    AppendSpriteIdentity(source, rim.LeftEdge);
                    AppendSpriteIdentity(source, rim.TopLeftCorner);
                    AppendSpriteIdentity(source, rim.BottomLeftCorner);
                }
            }

            return Hash128.Compute(source.ToString()).ToString();
        }

        /// <summary>
        /// 入力または調整値が未生成、または直前の正常生成から変わっている場合にtrueを返します。
        /// </summary>
        /// <param name="settings">共通設定を持つ設定アセットです。</param>
        /// <param name="material">確認する材質です。</param>
        /// <returns>完成Spriteを再生成すべき場合はtrueです。</returns>
        public static bool RequiresRegeneration(
            TerrainTileArtGenerationSettings settings,
            TerrainMaterialArtSource material)
        {
            if (settings == null || material == null)
            {
                return true;
            }

            string currentFingerprint = CalculateFingerprint(settings, material);
            return !string.Equals(
                currentFingerprint,
                material.LastGeneratedFingerprint,
                StringComparison.Ordinal);
        }

        /// <summary>
        /// 選択した1材質を検証し、16接続WallTopと必要な断面境界合成PNGを生成・Importします。
        /// 途中で失敗した場合は成功記録を更新せず、Inspectorへ要再生成状態を残します。
        /// </summary>
        /// <param name="settings">共通設定と材質入力を持つ設定アセットです。</param>
        /// <param name="materialIndex">Materials配列内の0始まりの対象位置です。</param>
        /// <returns>生成数、保存先、Fingerprintを含む正常終了結果です。</returns>
        public static TerrainTileArtGenerationResult GenerateMaterial(
            TerrainTileArtGenerationSettings settings,
            int materialIndex)
        {
            TerrainTileArtValidationReport report = ValidateMaterial(settings, materialIndex);
            if (report.HasErrors)
            {
                throw new InvalidOperationException(BuildValidationErrorMessage(report));
            }

            TerrainMaterialArtSource material = settings.Materials[materialIndex];
            string fingerprint = CalculateFingerprint(settings, material);
            string materialFolder = CombineAssetPath(
                settings.OutputRootFolder,
                material.MaterialId);
            Dictionary<Sprite, Color32[]> pixelCache = new Dictionary<Sprite, Color32[]>();
            List<string> generatedPaths = new List<string>();

            GenerateWallTopSprites(
                settings,
                material,
                materialFolder,
                pixelCache,
                generatedPaths);
            GenerateFaceBoundarySprites(
                material,
                materialFolder,
                pixelCache,
                generatedPaths);

            // 新出力がすべて成功した後だけ、前回この材質が所有していた不要Assetを消します。
            // 先に消すと途中失敗時に前回の利用可能な完成Spriteまで失うため、順序を固定します。
            RemoveObsoleteGeneratedAssets(
                material.LastGeneratedAssetPaths,
                generatedPaths);

            // 全PNGの書き込み、Import、不要出力の整理が成功してから記録し、途中失敗を「最新」と誤表示しません。
            material.RecordSuccessfulGeneration(fingerprint, generatedPaths.ToArray());
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            return new TerrainTileArtGenerationResult(
                material.MaterialId,
                generatedPaths.Count,
                materialFolder,
                fingerprint);
        }

        /// <summary>
        /// 登録済みの全材質を一括検証してから順番に生成します。
        /// 1材質でも不正なら何も書き始めず、部分的に新旧出力が混在することを防ぎます。
        /// </summary>
        /// <param name="settings">全材質を保持する設定アセットです。</param>
        /// <returns>登録順の材質別生成結果です。</returns>
        public static IReadOnlyList<TerrainTileArtGenerationResult> GenerateAll(
            TerrainTileArtGenerationSettings settings)
        {
            TerrainTileArtValidationReport report = ValidateAll(settings);
            if (report.HasErrors)
            {
                throw new InvalidOperationException(BuildValidationErrorMessage(report));
            }

            List<TerrainTileArtGenerationResult> results =
                new List<TerrainTileArtGenerationResult>(settings.Materials.Length);
            for (int materialIndex = 0;
                 materialIndex < settings.Materials.Length;
                 materialIndex++)
            {
                results.Add(GenerateMaterial(settings, materialIndex));
            }

            return results;
        }

        /// <summary>
        /// 共通設定の存在と安全なAssets配下の出力先を検証します。
        /// </summary>
        /// <param name="settings">検証する設定アセットです。</param>
        /// <param name="report">見つかった理由を追加するReportです。</param>
        /// <returns>材質配列まで検証を続行できる場合はtrueです。</returns>
        private static bool ValidateSettingsHeader(
            TerrainTileArtGenerationSettings settings,
            TerrainTileArtValidationReport report)
        {
            if (settings == null)
            {
                report.AddError("TerrainTileArtGenerationSettingsが指定されていません。");
                return false;
            }

            string outputRoot = NormalizeAssetPath(settings.OutputRootFolder);
            if (string.IsNullOrWhiteSpace(outputRoot)
                || !outputRoot.StartsWith("Assets/", StringComparison.Ordinal)
                || outputRoot.Contains("..", StringComparison.Ordinal)
                || Path.HasExtension(outputRoot))
            {
                report.AddError(
                    "Output Root Folderは、拡張子と「..」を含まないAssets配下のフォルダーを指定してください。");
            }

            if (settings.Materials == null || settings.Materials.Length == 0)
            {
                report.AddError("Materialsへ生成対象の材質を1件以上登録してください。");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 1材質のID、配列、幅、係数、全Sprite参照と画素Alphaを検証します。
        /// </summary>
        /// <param name="settings">共通設定を持つ親アセットです。</param>
        /// <param name="material">検証する材質です。</param>
        /// <param name="materialIndex">エラー表示に使うMaterials配列位置です。</param>
        /// <param name="report">見つかった理由を追加するReportです。</param>
        private static void ValidateMaterial(
            TerrainTileArtGenerationSettings settings,
            TerrainMaterialArtSource material,
            int materialIndex,
            TerrainTileArtValidationReport report)
        {
            string prefix = $"材質[{materialIndex}]";
            if (material == null)
            {
                report.AddError($"{prefix}がnullです。");
                return;
            }

            if (!IsValidMaterialId(material.MaterialId))
            {
                report.AddError(
                    $"{prefix}のMaterial ID「{material.MaterialId}」は空、または英数字・ハイフン・アンダースコア以外を含みます。");
            }

            try
            {
                TerrainTileArtPixelComposer.ValidateWidths(
                    material.TopRimWidth,
                    material.SideRimWidth,
                    material.BottomRimWidth);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                report.AddError($"{prefix}の外周幅が不正です: {exception.Message}");
            }

            if (material.WallDarkenFactor <= 0f || material.WallDarkenFactor > 1f)
            {
                report.AddError(
                    $"{prefix}のWall Darken Factorは0より大きく1以下にしてください。");
            }

            ValidateSpriteArray(
                material.GroundTopSprites,
                true,
                true,
                $"{prefix} GroundTop",
                report);
            ValidateSpriteArray(
                material.FaceSprites,
                true,
                true,
                $"{prefix} Face",
                report);
            ValidateSpriteArray(
                material.FaceBoundaryOverlays,
                false,
                true,
                $"{prefix} FaceBoundaryOverlay",
                report);
            ValidateWallRims(material.WallTopRims, prefix, report);

            TerrainTileArtSpriteCounts counts = CalculateSpriteCounts(material);
            if (counts.GeneratedOutputCount >= 128)
            {
                report.AddWarning(
                    $"{prefix}は完成Spriteを{counts.GeneratedOutputCount}枚生成します。"
                    + $" 内訳: WallTop {counts.WallTopOutputCount}、Ground境界 {counts.GroundBoundaryOutputCount}、Wall境界 {counts.WallBoundaryOutputCount}。"
                    + " 意図した差分数か確認してください。");
            }

            // 共通色はOnValidateで不透明になりますが、Scriptから作った未検証値もここで明示的に拒否します。
            if (settings.WallInteriorDark.a != byte.MaxValue)
            {
                report.AddError("Wall Interior DarkのAlphaは255である必要があります。");
            }
        }

        /// <summary>
        /// Sprite配列を検証し、任意配列だけは0件を許可します。
        /// </summary>
        /// <param name="sprites">検証するSprite配列です。</param>
        /// <param name="required">1件以上を必須にする場合はtrueです。</param>
        /// <param name="requireBinaryAlpha">透明OverlayとしてAlpha 0/255だけを要求する場合はtrueです。</param>
        /// <param name="label">エラー箇所を示す材質・部位名です。</param>
        /// <param name="report">見つかった理由を追加するReportです。</param>
        private static void ValidateSpriteArray(
            Sprite[] sprites,
            bool required,
            bool requireBinaryAlpha,
            string label,
            TerrainTileArtValidationReport report)
        {
            if (sprites == null || sprites.Length == 0)
            {
                if (required)
                {
                    report.AddError($"{label}を1件以上登録してください。");
                }

                return;
            }

            for (int spriteIndex = 0; spriteIndex < sprites.Length; spriteIndex++)
            {
                ValidateSprite(
                    sprites[spriteIndex],
                    requireBinaryAlpha,
                    $"{label}[{spriteIndex}]",
                    report);
            }
        }

        /// <summary>
        /// WallTop外周セットの必須5参照、16×16 px、読み取り可否を検証します。
        /// </summary>
        /// <param name="rims">検証する外周セット配列です。</param>
        /// <param name="prefix">材質位置を示す表示名です。</param>
        /// <param name="report">見つかった理由を追加するReportです。</param>
        private static void ValidateWallRims(
            TerrainWallTopRimInput[] rims,
            string prefix,
            TerrainTileArtValidationReport report)
        {
            if (rims == null || rims.Length == 0)
            {
                report.AddError($"{prefix} WallTop Rimsを1件以上登録してください。");
                return;
            }

            for (int rimIndex = 0; rimIndex < rims.Length; rimIndex++)
            {
                TerrainWallTopRimInput rim = rims[rimIndex];
                string rimLabel = $"{prefix} WallTop Rims[{rimIndex}]";
                if (rim == null)
                {
                    report.AddError($"{rimLabel}がnullです。");
                    continue;
                }

                ValidateSprite(rim.TopEdge, true, $"{rimLabel} Top Edge", report);
                ValidateSprite(rim.BottomEdge, true, $"{rimLabel} Bottom Edge", report);
                ValidateSprite(rim.LeftEdge, true, $"{rimLabel} Left Edge", report);
                ValidateSprite(
                    rim.TopLeftCorner,
                    true,
                    $"{rimLabel} Top Left Corner",
                    report);
                ValidateSprite(
                    rim.BottomLeftCorner,
                    true,
                    $"{rimLabel} Bottom Left Corner",
                    report);
            }
        }

        /// <summary>
        /// 1枚の入力Spriteについて、参照、寸法、CPU読取設定、半透明画素を検証します。
        /// </summary>
        /// <param name="sprite">検証する入力Spriteです。</param>
        /// <param name="requireBinaryAlpha">全画素をAlpha 0/255だけに制限する場合はtrueです。</param>
        /// <param name="label">エラー箇所を示す部位名です。</param>
        /// <param name="report">見つかった理由を追加するReportです。</param>
        private static void ValidateSprite(
            Sprite sprite,
            bool requireBinaryAlpha,
            string label,
            TerrainTileArtValidationReport report)
        {
            if (sprite == null)
            {
                report.AddError($"{label}のSprite参照が未設定です。");
                return;
            }

            Rect rect = sprite.rect;
            int size = TerrainTileArtGenerationSettings.TileSizeInPixels;
            if (!Mathf.Approximately(rect.width, size)
                || !Mathf.Approximately(rect.height, size))
            {
                report.AddError(
                    $"{label}「{sprite.name}」は{rect.width}×{rect.height} pxです。16×16 pxにしてください。");
                return;
            }

            if (sprite.texture == null || !sprite.texture.isReadable)
            {
                report.AddError(
                    $"{label}「{sprite.name}」はCPUから読めません。Texture Import SettingsのRead/Writeを有効にしてください。");
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(sprite);
            TextureImporter importer = string.IsNullOrEmpty(assetPath)
                ? null
                : AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    report.AddError(
                        $"{label}「{sprite.name}」は圧縮されています。Texture CompressionをNoneにしてください。");
                }

                if (importer.filterMode != FilterMode.Point)
                {
                    report.AddError(
                        $"{label}「{sprite.name}」のFilter ModeをPointへ変更してください。");
                }

                if (importer.mipmapEnabled)
                {
                    report.AddError(
                        $"{label}「{sprite.name}」のGenerate Mip Mapsを無効にしてください。");
                }

                if (!importer.sRGBTexture)
                {
                    report.AddError(
                        $"{label}「{sprite.name}」はsRGBとしてImportしてください。");
                }

                if (!Mathf.Approximately(importer.spritePixelsPerUnit, PixelsPerUnit))
                {
                    report.AddError(
                        $"{label}「{sprite.name}」のPixels Per Unitを16へ変更してください。");
                }

                TextureImporterSettings textureSettings = new TextureImporterSettings();
                importer.ReadTextureSettings(textureSettings);
                if (textureSettings.spriteMeshType != SpriteMeshType.FullRect)
                {
                    report.AddError(
                        $"{label}「{sprite.name}」のMesh TypeをFull Rectへ変更してください。");
                }
            }

            if (!requireBinaryAlpha)
            {
                return;
            }

            try
            {
                Color32[] pixels = ReadSpritePixels(sprite);
                for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
                {
                    byte alpha = pixels[pixelIndex].a;
                    if (alpha != byte.MinValue && alpha != byte.MaxValue)
                    {
                        report.AddError(
                            $"{label}「{sprite.name}」に半透明画素があります。Alphaは0または255だけを使用してください。");
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                report.AddError($"{label}「{sprite.name}」の画素を読めません: {exception.Message}");
            }
        }

        /// <summary>
        /// 外周セットごとにMask 0〜15のWallTopを作り、材質別WallTopフォルダーへ書き込みます。
        /// </summary>
        /// <param name="settings">共通内部色を持つ設定です。</param>
        /// <param name="material">生成する材質です。</param>
        /// <param name="materialFolder">材質別の出力ルートです。</param>
        /// <param name="pixelCache">同じ入力Spriteの再読込を避ける材質内キャッシュです。</param>
        /// <param name="generatedPaths">正常にImportした出力パスの記録先です。</param>
        private static void GenerateWallTopSprites(
            TerrainTileArtGenerationSettings settings,
            TerrainMaterialArtSource material,
            string materialFolder,
            Dictionary<Sprite, Color32[]> pixelCache,
            List<string> generatedPaths)
        {
            string outputFolder = CombineAssetPath(materialFolder, "WallTop");
            for (int rimIndex = 0; rimIndex < material.WallTopRims.Length; rimIndex++)
            {
                TerrainWallTopRimInput rim = material.WallTopRims[rimIndex];
                Color32[] top = GetSpritePixels(rim.TopEdge, pixelCache);
                Color32[] bottom = GetSpritePixels(rim.BottomEdge, pixelCache);
                Color32[] left = GetSpritePixels(rim.LeftEdge, pixelCache);
                Color32[] topLeft = GetSpritePixels(rim.TopLeftCorner, pixelCache);
                Color32[] bottomLeft = GetSpritePixels(rim.BottomLeftCorner, pixelCache);

                for (int maskValue = 0; maskValue < 16; maskValue++)
                {
                    Color32[] pixels = TerrainTileArtPixelComposer.ComposeWallTop(
                        top,
                        bottom,
                        left,
                        topLeft,
                        bottomLeft,
                        (TerrainWallConnectionMask)maskValue,
                        material.TopRimWidth,
                        material.SideRimWidth,
                        material.BottomRimWidth,
                        settings.WallInteriorDark,
                        material.DarkenWallSurface,
                        material.WallDarkenFactor);
                    string fileName =
                        $"{material.MaterialId}_WallTop_w{rimIndex:D2}_m{maskValue:X1}.png";
                    string assetPath = CombineAssetPath(outputFolder, fileName);
                    WriteAndImportSprite(assetPath, pixels);
                    generatedPaths.Add(assetPath);
                }
            }
        }

        /// <summary>
        /// FaceとFaceBoundaryOverlayの全組合せを作り、Ground用と必要なWall用へ分けて出力します。
        /// </summary>
        /// <param name="material">生成する材質です。</param>
        /// <param name="materialFolder">材質別の出力ルートです。</param>
        /// <param name="pixelCache">同じ入力Spriteの再読込を避ける材質内キャッシュです。</param>
        /// <param name="generatedPaths">正常にImportした出力パスの記録先です。</param>
        private static void GenerateFaceBoundarySprites(
            TerrainMaterialArtSource material,
            string materialFolder,
            Dictionary<Sprite, Color32[]> pixelCache,
            List<string> generatedPaths)
        {
            if (material.FaceBoundaryOverlays.Length == 0)
            {
                return;
            }

            string outputFolder = CombineAssetPath(materialFolder, "FaceBoundary");
            for (int faceIndex = 0; faceIndex < material.FaceSprites.Length; faceIndex++)
            {
                Color32[] face = GetSpritePixels(
                    material.FaceSprites[faceIndex],
                    pixelCache);
                for (int boundaryIndex = 0;
                     boundaryIndex < material.FaceBoundaryOverlays.Length;
                     boundaryIndex++)
                {
                    Color32[] boundary = GetSpritePixels(
                        material.FaceBoundaryOverlays[boundaryIndex],
                        pixelCache);
                    Color32[] groundPixels =
                        TerrainTileArtPixelComposer.ComposeFaceBoundary(
                            face,
                            boundary,
                            false,
                            1f);
                    string groundName =
                        $"{material.MaterialId}_GroundFace_f{faceIndex:D2}_b{boundaryIndex:D2}.png";
                    string groundPath = CombineAssetPath(outputFolder, groundName);
                    WriteAndImportSprite(groundPath, groundPixels);
                    generatedPaths.Add(groundPath);

                    if (!material.DarkenWallSurface)
                    {
                        // 暗色化が無効ならGround合成結果をWallでも再利用し、同一PNGを重複生成しません。
                        continue;
                    }

                    Color32[] wallPixels =
                        TerrainTileArtPixelComposer.ComposeFaceBoundary(
                            face,
                            boundary,
                            true,
                            material.WallDarkenFactor);
                    string wallName =
                        $"{material.MaterialId}_WallFace_f{faceIndex:D2}_b{boundaryIndex:D2}.png";
                    string wallPath = CombineAssetPath(outputFolder, wallName);
                    WriteAndImportSprite(wallPath, wallPixels);
                    generatedPaths.Add(wallPath);
                }
            }
        }

        /// <summary>
        /// Spriteが属するTextureからrect部分の16×16画素だけを左下原点で取り出します。
        /// </summary>
        /// <param name="sprite">Read/Writeが有効な16×16 px Spriteです。</param>
        /// <returns>Texture2D.GetPixels32と同じ左下原点の256画素です。</returns>
        private static Color32[] ReadSpritePixels(Sprite sprite)
        {
            if (sprite == null)
            {
                throw new ArgumentNullException(nameof(sprite));
            }

            Texture2D texture = sprite.texture;
            if (texture == null || !texture.isReadable)
            {
                throw new InvalidOperationException(
                    $"Sprite「{sprite.name}」のTextureでRead/Writeが無効です。");
            }

            Rect rect = sprite.rect;
            int x = Mathf.RoundToInt(rect.x);
            int y = Mathf.RoundToInt(rect.y);
            int width = Mathf.RoundToInt(rect.width);
            int height = Mathf.RoundToInt(rect.height);
            Color32[] allPixels = texture.GetPixels32(0);
            return CopyRect(allPixels, texture.width, x, y, width, height);
        }

        /// <summary>
        /// Texture全体の画素配列からSprite rectだけを行単位でコピーします。
        /// </summary>
        /// <param name="source">Texture2D全体の左下原点画素です。</param>
        /// <param name="sourceWidth">Texture全体の横幅です。</param>
        /// <param name="x">切り出し矩形の左端です。</param>
        /// <param name="y">切り出し矩形の下端です。</param>
        /// <param name="width">切り出す横幅です。</param>
        /// <param name="height">切り出す縦幅です。</param>
        /// <returns>同じ左下原点を維持した切り出し画素です。</returns>
        private static Color32[] CopyRect(
            Color32[] source,
            int sourceWidth,
            int x,
            int y,
            int width,
            int height)
        {
            Color32[] result = new Color32[checked(width * height)];
            for (int row = 0; row < height; row++)
            {
                int sourceOffset = checked((y + row) * sourceWidth + x);
                int destinationOffset = checked(row * width);
                Array.Copy(source, sourceOffset, result, destinationOffset, width);
            }

            return result;
        }

        /// <summary>
        /// 同じSpriteの画素を材質生成中に一度だけ読み、WallTop 16状態などで再利用します。
        /// </summary>
        /// <param name="sprite">読み取る入力Spriteです。</param>
        /// <param name="cache">Sprite参照をキーにした材質内キャッシュです。</param>
        /// <returns>Sprite rectから切り出した256画素です。</returns>
        private static Color32[] GetSpritePixels(
            Sprite sprite,
            Dictionary<Sprite, Color32[]> cache)
        {
            if (!cache.TryGetValue(sprite, out Color32[] pixels))
            {
                pixels = ReadSpritePixels(sprite);
                cache.Add(sprite, pixels);
            }

            return pixels;
        }

        /// <summary>
        /// 256画素をPNGへ書き、Point・非圧縮・単一Spriteとして同期Importします。
        /// 同じパスを再生成した場合は.metaとGUIDを維持し、将来の参照を壊しません。
        /// </summary>
        /// <param name="assetPath">Assetsから始まる出力PNGパスです。</param>
        /// <param name="pixels">左下原点の16×16 px完成画素です。</param>
        private static void WriteAndImportSprite(string assetPath, Color32[] pixels)
        {
            int size = TerrainTileArtGenerationSettings.TileSizeInPixels;
            if (pixels == null || pixels.Length != size * size)
            {
                throw new ArgumentException("出力画素は16×16 pxである必要があります。", nameof(pixels));
            }

            string absolutePath = AssetPathToAbsolutePath(assetPath);
            string absoluteFolder = Path.GetDirectoryName(absolutePath);
            if (string.IsNullOrEmpty(absoluteFolder))
            {
                throw new InvalidOperationException($"出力フォルダーを解決できません: {assetPath}");
            }

            Directory.CreateDirectory(absoluteFolder);
            Texture2D texture = new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = Path.GetFileNameWithoutExtension(assetPath),
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                File.WriteAllBytes(absolutePath, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"生成PNGのTextureImporterを取得できませんでした: {assetPath}");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;

            TextureImporterSettings importerSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(importerSettings);
            importerSettings.spriteMeshType = SpriteMeshType.FullRect;
            importerSettings.spriteAlignment = (int)SpriteAlignment.Center;
            importerSettings.spritePivot = new Vector2(0.5f, 0.5f);
            importer.SetTextureSettings(importerSettings);
            importer.SaveAndReimport();
        }

        /// <summary>
        /// 前回この材質が生成したAssetのうち、今回の出力一覧に含まれないPNGだけを削除します。
        /// 記録外Assetやフォルダーは触らず、差分数を減らした再生成で古い完成Spriteが残ることだけを防ぎます。
        /// </summary>
        /// <param name="previousPaths">前回の正常生成で設定Assetへ記録した所有パスです。</param>
        /// <param name="currentPaths">今回すべて正常に書き込んだ所有パスです。</param>
        private static void RemoveObsoleteGeneratedAssets(
            IReadOnlyList<string> previousPaths,
            IReadOnlyList<string> currentPaths)
        {
            if (previousPaths == null || previousPaths.Count == 0)
            {
                return;
            }

            HashSet<string> currentPathSet =
                new HashSet<string>(currentPaths, StringComparer.Ordinal);
            for (int pathIndex = 0; pathIndex < previousPaths.Count; pathIndex++)
            {
                string previousPath = NormalizeAssetPath(previousPaths[pathIndex]);
                if (string.IsNullOrEmpty(previousPath)
                    || currentPathSet.Contains(previousPath))
                {
                    continue;
                }

                bool isOwnedGeneratedPng =
                    previousPath.StartsWith("Assets/", StringComparison.Ordinal)
                    && previousPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    && (previousPath.Contains("/WallTop/", StringComparison.Ordinal)
                        || previousPath.Contains("/FaceBoundary/", StringComparison.Ordinal));
                if (!isOwnedGeneratedPng)
                {
                    throw new InvalidOperationException(
                        $"記録された生成Assetパスが想定形式ではないため削除を中止しました: {previousPath}");
                }

                if (AssetDatabase.LoadMainAssetAtPath(previousPath) != null
                    && !AssetDatabase.DeleteAsset(previousPath))
                {
                    throw new InvalidOperationException(
                        $"不要になった旧生成Spriteを削除できませんでした: {previousPath}");
                }
            }
        }

        /// <summary>
        /// Sprite配列をFingerprintへ、件数・順序・Asset内容を含めて追加します。
        /// </summary>
        /// <param name="builder">Fingerprint元文字列の追記先です。</param>
        /// <param name="label">部位間の衝突を防ぐ識別名です。</param>
        /// <param name="sprites">登録順も意味を持つSprite入力です。</param>
        private static void AppendSprites(
            StringBuilder builder,
            string label,
            Sprite[] sprites)
        {
            builder.Append(label).Append('=').Append(GetLength(sprites)).Append('|');
            if (sprites == null)
            {
                return;
            }

            for (int spriteIndex = 0; spriteIndex < sprites.Length; spriteIndex++)
            {
                builder.Append(label).Append('[').Append(spriteIndex).Append("]=");
                AppendSpriteIdentity(builder, sprites[spriteIndex]);
            }
        }

        /// <summary>
        /// SpriteのGUID、Local File ID、依存HashをFingerprint元へ追加します。
        /// 同じTexture内の別Sliceと、同じGUIDの画像内容更新を両方区別します。
        /// </summary>
        /// <param name="builder">Fingerprint元文字列の追記先です。</param>
        /// <param name="sprite">識別するSprite参照です。</param>
        private static void AppendSpriteIdentity(StringBuilder builder, Sprite sprite)
        {
            if (sprite == null)
            {
                builder.Append("sprite:null|");
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(sprite);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                sprite,
                out string guid,
                out long localId);
            Hash128 dependencyHash = string.IsNullOrEmpty(assetPath)
                ? default
                : AssetDatabase.GetAssetDependencyHash(assetPath);
            builder.Append("sprite:")
                .Append(guid).Append(':')
                .Append(localId).Append(':')
                .Append(dependencyHash).Append('|');
        }

        /// <summary>
        /// 共通内部色をRGBA順でFingerprint元へ追加し、Alpha固定も判定へ含めます。
        /// </summary>
        /// <param name="builder">Fingerprint元文字列の追記先です。</param>
        /// <param name="color">追加する8bit RGBAです。</param>
        private static void AppendColor(StringBuilder builder, Color32 color)
        {
            builder.Append("interior=")
                .Append(color.r).Append(',')
                .Append(color.g).Append(',')
                .Append(color.b).Append(',')
                .Append(color.a).Append('|');
        }

        /// <summary>
        /// 検証Reportの全エラーを、生成例外へ含める複数行メッセージへ変換します。
        /// </summary>
        /// <param name="report">少なくとも1件のErrorを持つReportです。</param>
        /// <returns>先頭説明と箇条書きエラーを持つ日本語文字列です。</returns>
        private static string BuildValidationErrorMessage(
            TerrainTileArtValidationReport report)
        {
            StringBuilder message = new StringBuilder("地形材質Spriteを生成できません。\n");
            for (int errorIndex = 0; errorIndex < report.Errors.Count; errorIndex++)
            {
                message.Append("- ").Append(report.Errors[errorIndex]).Append('\n');
            }

            return message.ToString().TrimEnd();
        }

        /// <summary>
        /// Material IDが安全なフォルダー名として使えるASCII英数字、ハイフン、アンダースコアだけか確認します。
        /// </summary>
        /// <param name="materialId">検証する安定材質IDです。</param>
        /// <returns>空でなく許可文字だけならtrueです。</returns>
        private static bool IsValidMaterialId(string materialId)
        {
            if (string.IsNullOrWhiteSpace(materialId))
            {
                return false;
            }

            for (int characterIndex = 0;
                 characterIndex < materialId.Length;
                 characterIndex++)
            {
                char character = materialId[characterIndex];
                bool isAsciiLetter = character >= 'A' && character <= 'Z'
                    || character >= 'a' && character <= 'z';
                bool isDigit = character >= '0' && character <= '9';
                if (!isAsciiLetter && !isDigit && character != '-' && character != '_')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Unity配列がnullの場合も0件として数え、Sprite数計算を簡潔に保ちます。
        /// </summary>
        /// <typeparam name="T">数える配列要素の型です。</typeparam>
        /// <param name="items">nullを許容する配列です。</param>
        /// <returns>nullなら0、それ以外はLengthです。</returns>
        private static int GetLength<T>(T[] items)
        {
            return items?.Length ?? 0;
        }

        /// <summary>
        /// Assets相対パスの区切りをスラッシュへ統一し、末尾スラッシュを除きます。
        /// </summary>
        /// <param name="assetPath">正規化するプロジェクト相対パスです。</param>
        /// <returns>Unity AssetDatabaseで使用できる形式のパスです。</returns>
        private static string NormalizeAssetPath(string assetPath)
        {
            return assetPath?.Trim().Replace('\\', '/').TrimEnd('/');
        }

        /// <summary>
        /// 2つのAssetパス断片をOSに依存しないスラッシュ区切りで連結します。
        /// </summary>
        /// <param name="left">親フォルダーのAssetパスです。</param>
        /// <param name="right">子フォルダーまたはファイル名です。</param>
        /// <returns>正規化済みの連結Assetパスです。</returns>
        private static string CombineAssetPath(string left, string right)
        {
            return $"{NormalizeAssetPath(left)}/{right.TrimStart('/', '\\')}";
        }

        /// <summary>
        /// Assetsから始まるプロジェクト相対パスを、現在Projectの絶対ファイルパスへ変換します。
        /// </summary>
        /// <param name="assetPath">Assetsから始まる正規化済みパスです。</param>
        /// <returns>File APIで使用する絶対パスです。</returns>
        private static string AssetPathToAbsolutePath(string assetPath)
        {
            string normalized = NormalizeAssetPath(assetPath);
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Assets配下以外へ地形材質Spriteを出力できません: {assetPath}");
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new InvalidOperationException("Unity Projectのルートフォルダーを解決できません。");
            }

            return Path.Combine(projectRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
