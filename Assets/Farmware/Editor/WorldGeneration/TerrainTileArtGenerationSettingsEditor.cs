// このファイルは、TerrainTileArtGenerationSettingsを編集・検証・生成する専用Inspectorを提供します。
// Unity標準の配列編集を維持しながら、選択材質のSprite数、壁暗化設定、生成結果を見やすく表示します。

using System;
using System.Collections.Generic;
using System.IO;
using Farmware.WorldGeneration.Authoring;
using UnityEditor;
using UnityEngine;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 地形タイル生成設定へ、代表材質の試作と41材質一括生成の入口を追加します。
    /// GroundTopは複製せず、FaceBoundaryのGround／Wall用合成結果だけを一覧表示します。
    /// </summary>
    [CustomEditor(typeof(TerrainTileArtGenerationSettings))]
    public sealed class TerrainTileArtGenerationSettingsEditor : UnityEditor.Editor
    {
        // 16×16 pxのドットを4倍で確認するプレビュー表示サイズです。
        private const float PreviewImageSize = 64f;

        // Inspectorを縦長にしすぎず各画像も読めるよう、横方向の最大セル数を4に固定します。
        private const int MaximumPreviewColumns = 4;

        // Inspector内でプレビュー・単体生成するMaterials配列位置です。Assetには保存しません。
        private int selectedMaterialIndex;

        // 最後にボタンで実行した検証結果です。毎Repaintで入力Textureを読み直さないため保持します。
        private TerrainTileArtValidationReport lastValidationReport;

        // 直前の生成または検証結果を短く表示します。
        private string lastOperationMessage;

        // 生成結果一覧全体と用途別一覧の開閉状態です。
        private bool showGeneratedPreviews = true;
        private bool showGroundBoundaryPreviews = true;
        private bool showWallBoundaryPreviews = true;

        // 現在キャッシュしている材質位置とFingerprintです。
        private int cachedPreviewMaterialIndex = -1;
        private string cachedPreviewFingerprint;

        // Ground／Wall用FaceBoundary生成物を用途別に保持します。
        private readonly List<GeneratedPreviewEntry> groundBoundaryPreviewEntries =
            new List<GeneratedPreviewEntry>();
        private readonly List<GeneratedPreviewEntry> wallBoundaryPreviewEntries =
            new List<GeneratedPreviewEntry>();

        // 各プレビューセルの識別名を中央揃えで表示するため、一度だけ作るGUIStyleです。
        private GUIStyle previewLabelStyle;

        /// <summary>生成済みSpriteの表示名、選択用内容、Asset参照をまとめます。</summary>
        private sealed class GeneratedPreviewEntry
        {
            /// <summary>セル上部へ表示するFace・Boundary番号です。</summary>
            public string Label { get; }

            /// <summary>Projectビューで選択する生成済みSpriteです。</summary>
            public Sprite Sprite { get; }

            /// <summary>AssetパスTooltipを持つ透明ボタン内容です。</summary>
            public GUIContent ButtonContent { get; }

            /// <summary>生成済みSpriteをInspectorの1セルとして表示できる形へまとめます。</summary>
            /// <param name="label">差分番号を示す短い表示名です。</param>
            /// <param name="sprite">AssetDatabaseから読み込んだ完成Spriteです。</param>
            /// <param name="assetPath">Tooltipへ表示するAssetパスです。</param>
            public GeneratedPreviewEntry(string label, Sprite sprite, string assetPath)
            {
                Label = label;
                Sprite = sprite;
                ButtonContent = new GUIContent(string.Empty, assetPath);
            }
        }

        /// <summary>プレビュー識別名用Styleを必要になった時点で一度だけ作ります。</summary>
        private GUIStyle PreviewLabelStyle
        {
            get
            {
                if (previewLabelStyle == null)
                {
                    previewLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        wordWrap = true
                    };
                }

                return previewLabelStyle;
            }
        }

        /// <summary>UnityがInspectorを描画するときに設定、要約、操作、結果を順に表示します。</summary>
        public override void OnInspectorGUI()
        {
            TerrainTileArtGenerationSettings settings =
                (TerrainTileArtGenerationSettings)target;

            // 標準描画に任せることで、可変長配列、Object Picker、UndoをUnity標準挙動に揃えます。
            bool changed = DrawDefaultInspector();
            if (changed)
            {
                lastValidationReport = null;
                lastOperationMessage = "設定が変更されました。対象材質を再検証してください。";
                InvalidatePreviewCache();
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("地形タイル生成ツール", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "GroundTopはGroundとWallで同じ入力Spriteを使い、生成ツールでは複製しません。"
                + " Wall上面は表示時Tint、Wall用FaceBoundaryOverlayは生成時に同じ係数で暗くします。"
                + " 入力PNGはSourceフォルダーへ置くと共通Import設定と4×3分割が自動適用されます。",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "境界なし: FaceBoundaryOverlayを0件にし、GroundTopとFaceを直接使うため生成操作は不要です。\n"
                + "境界あり: FaceBoundaryOverlayを1件以上登録し、Face・Overlay・Wall暗色化設定を変更した後に境界Spriteを更新してください。"
                + " GroundTopだけの変更は直接反映されるため再生成不要です。",
                MessageType.None);

            TerrainMaterialArtSource[] materials = settings.Materials;
            if (materials == null || materials.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "Materialsへ材質を1件以上追加すると、検証と生成を実行できます。",
                    MessageType.Warning);
                return;
            }

            selectedMaterialIndex = Mathf.Clamp(selectedMaterialIndex, 0, materials.Length - 1);
            selectedMaterialIndex = EditorGUILayout.Popup(
                "対象材質",
                selectedMaterialIndex,
                BuildMaterialChoices(materials));
            TerrainMaterialArtSource selectedMaterial = materials[selectedMaterialIndex];
            DrawSelectedMaterialSummary(settings, selectedMaterial);
            DrawOperationButtons(settings, selectedMaterial);
            DrawLastReport();
            DrawGeneratedPreviews(selectedMaterial);
        }

        /// <summary>選択材質のSprite数、暗化方法、要再生成状態を表示します。</summary>
        /// <param name="settings">出力先を持つ設定アセットです。</param>
        /// <param name="material">Inspectorで選択中の材質です。</param>
        private static void DrawSelectedMaterialSummary(
            TerrainTileArtGenerationSettings settings,
            TerrainMaterialArtSource material)
        {
            if (material == null)
            {
                EditorGUILayout.HelpBox("選択材質がnullです。", MessageType.Error);
                return;
            }

            TerrainTileArtSpriteCounts counts =
                TerrainTileArtGenerator.CalculateSpriteCounts(material);
            EditorGUILayout.LabelField(
                "Sprite数",
                $"手描き {counts.HandDrawnInputCount} / 生成 {counts.GeneratedOutputCount} / Runtime候補 {counts.RuntimeCandidateCount}");
            EditorGUILayout.LabelField(
                "生成内訳",
                $"Ground境界 {counts.GroundBoundaryOutputCount} / Wall境界 {counts.WallBoundaryOutputCount}");
            EditorGUILayout.LabelField(
                "Wall上面",
                material.DarkenWallSurface
                    ? $"GroundTop共有 / 表示時RGB×{material.WallDarkenFactor:0.00}"
                    : "GroundTop共有 / 暗色化なし");

            bool hasBoundaryInputs = material.FaceBoundaryOverlays != null
                && material.FaceBoundaryOverlays.Length > 0;
            bool hasPreviousGeneratedAssets = material.LastGeneratedAssetPaths.Count > 0;
            bool requiresRegeneration =
                TerrainTileArtGenerator.RequiresRegeneration(settings, material);
            if (!hasBoundaryInputs && !hasPreviousGeneratedAssets)
            {
                EditorGUILayout.HelpBox(
                    "境界なし: 派生PNGの生成は不要です。GroundTopとFaceの修正は再Import後に直接反映されます。",
                    MessageType.Info);
            }
            else if (!hasBoundaryInputs)
            {
                EditorGUILayout.HelpBox(
                    "境界なしへ変更されています。対象材質の境界Spriteを整理し、以前の生成PNGを削除してください。",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    requiresRegeneration
                        ? "境界あり: 未生成、またはFace・Overlay・Wall暗色化設定が前回生成時から変わっています。"
                        : "境界あり: Ground／Wall用の境界合成Spriteは現在の入力と一致しています。",
                    requiresRegeneration ? MessageType.Warning : MessageType.Info);
            }
        }

        /// <summary>対象材質と全材質の検証・生成ボタンを表示します。</summary>
        /// <param name="settings">操作対象の設定アセットです。</param>
        /// <param name="selectedMaterial">境界生成または旧出力整理の要否を表示へ反映する選択材質です。</param>
        private void DrawOperationButtons(
            TerrainTileArtGenerationSettings settings,
            TerrainMaterialArtSource selectedMaterial)
        {
            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("対象材質を検証"))
                {
                    lastValidationReport =
                        TerrainTileArtGenerator.ValidateMaterial(settings, selectedMaterialIndex);
                    lastOperationMessage = lastValidationReport.HasErrors
                        ? "対象材質の検証でエラーが見つかりました。"
                        : "対象材質の検証が完了しました。";
                }

                bool hasBoundaryInputs = selectedMaterial?.FaceBoundaryOverlays != null
                    && selectedMaterial.FaceBoundaryOverlays.Length > 0;
                bool hasObsoleteGeneratedAssets = !hasBoundaryInputs
                    && selectedMaterial != null
                    && selectedMaterial.LastGeneratedAssetPaths.Count > 0;
                string selectedActionLabel = hasBoundaryInputs
                    ? "対象材質の境界Spriteを更新"
                    : hasObsoleteGeneratedAssets
                        ? "不要な境界Spriteを整理"
                        : "境界なし（生成不要）";
                using (new EditorGUI.DisabledScope(
                    selectedMaterial == null
                    || !hasBoundaryInputs && !hasObsoleteGeneratedAssets))
                {
                    if (GUILayout.Button(selectedActionLabel))
                    {
                        RunSelectedGeneration(settings);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全材質を検証"))
                {
                    lastValidationReport = TerrainTileArtGenerator.ValidateAll(settings);
                    lastOperationMessage = lastValidationReport.HasErrors
                        ? "全材質の検証でエラーが見つかりました。"
                        : "全材質の検証が完了しました。";
                }

                if (GUILayout.Button("全材質の境界Spriteを更新"))
                {
                    RunAllGeneration(settings);
                }
            }
        }

        /// <summary>選択材質だけを同期生成し、完了結果または失敗理由を表示用に保存します。</summary>
        /// <param name="settings">操作対象の設定アセットです。</param>
        private void RunSelectedGeneration(TerrainTileArtGenerationSettings settings)
        {
            try
            {
                lastValidationReport =
                    TerrainTileArtGenerator.ValidateMaterial(settings, selectedMaterialIndex);
                if (lastValidationReport.HasErrors)
                {
                    lastOperationMessage =
                        "対象材質の境界Spriteを更新できません。下の検証エラーを修正してください。";
                    return;
                }

                TerrainTileArtGenerationResult result =
                    TerrainTileArtGenerator.GenerateMaterial(settings, selectedMaterialIndex);
                lastOperationMessage = result.GeneratedSpriteCount > 0
                    ? $"{result.MaterialId}: 境界Sprite {result.GeneratedSpriteCount}枚の更新が完了しました。"
                    : $"{result.MaterialId}: 境界なし材質の不要な生成物を整理しました。";
                InvalidatePreviewCache();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                lastOperationMessage = $"対象材質生成に失敗しました: {exception.Message}";
            }
        }

        /// <summary>全材質を同期生成し、完了数または失敗理由を表示用に保存します。</summary>
        /// <param name="settings">操作対象の設定アセットです。</param>
        private void RunAllGeneration(TerrainTileArtGenerationSettings settings)
        {
            try
            {
                lastValidationReport = TerrainTileArtGenerator.ValidateAll(settings);
                if (lastValidationReport.HasErrors)
                {
                    lastOperationMessage =
                        "全材質の境界Spriteを更新できません。下の検証エラーを修正してください。";
                    return;
                }

                IReadOnlyList<TerrainTileArtGenerationResult> results =
                    TerrainTileArtGenerator.GenerateAll(settings);
                int generatedCount = 0;
                for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
                {
                    generatedCount = checked(
                        generatedCount + results[resultIndex].GeneratedSpriteCount);
                }

                lastOperationMessage =
                    $"全{results.Count}材質を確認し、境界Sprite {generatedCount}枚の更新が完了しました。"
                    + " 境界なし材質は派生PNGを生成していません。";
                InvalidatePreviewCache();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                lastOperationMessage = $"全材質生成に失敗しました: {exception.Message}";
            }
        }

        /// <summary>最後の操作結果と検証で見つかった全エラー・警告を表示します。</summary>
        private void DrawLastReport()
        {
            if (!string.IsNullOrEmpty(lastOperationMessage))
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.HelpBox(lastOperationMessage, MessageType.Info);
            }

            if (lastValidationReport == null)
            {
                return;
            }

            for (int index = 0; index < lastValidationReport.Errors.Count; index++)
            {
                EditorGUILayout.HelpBox(lastValidationReport.Errors[index], MessageType.Error);
            }

            for (int index = 0; index < lastValidationReport.Warnings.Count; index++)
            {
                EditorGUILayout.HelpBox(lastValidationReport.Warnings[index], MessageType.Warning);
            }
        }

        /// <summary>選択材質のGround／Wall境界合成結果を画像グリッドで表示します。</summary>
        /// <param name="material">生成履歴を持つ、現在選択中の材質です。</param>
        private void DrawGeneratedPreviews(TerrainMaterialArtSource material)
        {
            EditorGUILayout.Space(10f);
            showGeneratedPreviews = EditorGUILayout.Foldout(
                showGeneratedPreviews,
                "生成結果プレビュー",
                true);
            if (!showGeneratedPreviews)
            {
                return;
            }

            if (material == null || material.LastGeneratedAssetPaths.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "FaceBoundaryOverlayがある材質を生成すると、Ground／Wall境界をここへ一覧表示します。",
                    MessageType.Info);
                return;
            }

            int cachedCount = groundBoundaryPreviewEntries.Count + wallBoundaryPreviewEntries.Count;
            if (cachedPreviewMaterialIndex != selectedMaterialIndex
                || !string.Equals(
                    cachedPreviewFingerprint,
                    material.LastGeneratedFingerprint,
                    StringComparison.Ordinal)
                || cachedCount != material.LastGeneratedAssetPaths.Count)
            {
                RebuildPreviewCache(material);
            }

            if (GUILayout.Button("一覧を再読込", GUILayout.Width(96f)))
            {
                RebuildPreviewCache(material);
            }

            showGroundBoundaryPreviews = EditorGUILayout.Foldout(
                showGroundBoundaryPreviews,
                $"Ground境界 ({groundBoundaryPreviewEntries.Count}枚)",
                true);
            if (showGroundBoundaryPreviews)
            {
                DrawPreviewGrid(groundBoundaryPreviewEntries);
            }

            showWallBoundaryPreviews = EditorGUILayout.Foldout(
                showWallBoundaryPreviews,
                $"Wall境界 ({wallBoundaryPreviewEntries.Count}枚)",
                true);
            if (showWallBoundaryPreviews)
            {
                if (material.DarkenWallSurface)
                {
                    DrawPreviewGrid(wallBoundaryPreviewEntries);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Wall暗色化が無効なため、Wall境界はGround境界Spriteを再利用します。",
                        MessageType.Info);
                }
            }
        }

        /// <summary>生成履歴のパスを用途別に分類してAsset参照を読み込みます。</summary>
        /// <param name="material">再構築する選択材質です。</param>
        private void RebuildPreviewCache(TerrainMaterialArtSource material)
        {
            groundBoundaryPreviewEntries.Clear();
            wallBoundaryPreviewEntries.Clear();
            cachedPreviewMaterialIndex = selectedMaterialIndex;
            cachedPreviewFingerprint = material?.LastGeneratedFingerprint;
            if (material == null)
            {
                return;
            }

            IReadOnlyList<string> generatedPaths = material.LastGeneratedAssetPaths;
            for (int pathIndex = 0; pathIndex < generatedPaths.Count; pathIndex++)
            {
                string assetPath = generatedPaths[pathIndex];
                if (string.IsNullOrWhiteSpace(assetPath))
                {
                    continue;
                }

                Sprite sprite = LoadGeneratedSprite(assetPath);
                string fileName = Path.GetFileNameWithoutExtension(assetPath);
                if (fileName.Contains("_GroundFace_", StringComparison.Ordinal))
                {
                    groundBoundaryPreviewEntries.Add(
                        new GeneratedPreviewEntry(
                            BuildBoundaryPreviewLabel(fileName, "GroundFace"),
                            sprite,
                            assetPath));
                }
                else if (fileName.Contains("_WallFace_", StringComparison.Ordinal))
                {
                    wallBoundaryPreviewEntries.Add(
                        new GeneratedPreviewEntry(
                            BuildBoundaryPreviewLabel(fileName, "WallFace"),
                            sprite,
                            assetPath));
                }
            }
        }

        /// <summary>生成PNGから完成Spriteを取得します。</summary>
        /// <param name="assetPath">生成履歴へ保存したPNGのAssetパスです。</param>
        /// <returns>最初に見つかったSprite、またはImport不備ならnullです。</returns>
        private static Sprite LoadGeneratedSprite(string assetPath)
        {
            Sprite directSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (directSprite != null)
            {
                return directSprite;
            }

            UnityEngine.Object[] importedObjects = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int objectIndex = 0; objectIndex < importedObjects.Length; objectIndex++)
            {
                if (importedObjects[objectIndex] is Sprite importedSprite)
                {
                    return importedSprite;
                }
            }

            return null;
        }

        /// <summary>キャッシュ済み生成結果をInspector幅に応じて1〜4列へ折り返して描画します。</summary>
        /// <param name="entries">同じ用途に分類済みの生成Sprite一覧です。</param>
        private void DrawPreviewGrid(IReadOnlyList<GeneratedPreviewEntry> entries)
        {
            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox("生成済みSpriteがありません。", MessageType.Info);
                return;
            }

            float cellWidth = PreviewImageSize + 10f;
            int columns = Mathf.Clamp(
                Mathf.FloorToInt((EditorGUIUtility.currentViewWidth - 36f) / cellWidth),
                1,
                MaximumPreviewColumns);
            for (int rowStart = 0; rowStart < entries.Count; rowStart += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int column = 0; column < columns; column++)
                    {
                        int entryIndex = rowStart + column;
                        if (entryIndex >= entries.Count)
                        {
                            GUILayout.Space(cellWidth);
                            continue;
                        }

                        DrawPreviewCell(entries[entryIndex], cellWidth);
                    }
                }
            }
        }

        /// <summary>1枚の生成Spriteを識別名と4倍画像ボタンで表示します。</summary>
        /// <param name="entry">表示名、画像、Assetパスを持つ生成結果です。</param>
        /// <param name="cellWidth">Inspector内でこのセルへ割り当てる横幅です。</param>
        private void DrawPreviewCell(GeneratedPreviewEntry entry, float cellWidth)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(cellWidth)))
            {
                GUILayout.Label(
                    entry.Label,
                    PreviewLabelStyle,
                    GUILayout.Width(cellWidth),
                    GUILayout.Height(30f));
                if (entry.Sprite == null)
                {
                    GUILayout.Box(
                        "Missing",
                        GUILayout.Width(PreviewImageSize),
                        GUILayout.Height(PreviewImageSize));
                    return;
                }

                Rect outerRect = GUILayoutUtility.GetRect(
                    PreviewImageSize + 2f,
                    PreviewImageSize + 2f,
                    GUILayout.Width(PreviewImageSize + 2f),
                    GUILayout.Height(PreviewImageSize + 2f));
                GUI.Box(outerRect, GUIContent.none);
                Rect imageRect = new Rect(
                    outerRect.x + 1f,
                    outerRect.y + 1f,
                    PreviewImageSize,
                    PreviewImageSize);
                Texture2D texture = entry.Sprite.texture;
                Rect sourceRect = entry.Sprite.textureRect;
                Rect normalizedSourceRect = new Rect(
                    sourceRect.x / texture.width,
                    sourceRect.y / texture.height,
                    sourceRect.width / texture.width,
                    sourceRect.height / texture.height);
                GUI.DrawTextureWithTexCoords(imageRect, texture, normalizedSourceRect, true);
                if (GUI.Button(imageRect, entry.ButtonContent, GUIStyle.none))
                {
                    Selection.activeObject = entry.Sprite;
                    EditorGUIUtility.PingObject(entry.Sprite);
                }
            }
        }

        /// <summary>境界ファイル名からFace番号とBoundary番号だけを取り出します。</summary>
        /// <param name="fileName">拡張子を除いた生成ファイル名です。</param>
        /// <param name="usageMarker">GroundFaceまたはWallFaceの用途文字列です。</param>
        /// <returns>「f00 × b00」のような差分組合せラベルです。</returns>
        private static string BuildBoundaryPreviewLabel(string fileName, string usageMarker)
        {
            string marker = $"_{usageMarker}_";
            int markerIndex = fileName.LastIndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return fileName;
            }

            return fileName.Substring(markerIndex + marker.Length).Replace("_", " × ");
        }

        /// <summary>次のInspector描画でAsset参照を読み直すようキャッシュを無効化します。</summary>
        private void InvalidatePreviewCache()
        {
            cachedPreviewMaterialIndex = -1;
            cachedPreviewFingerprint = null;
        }

        /// <summary>Materials配列を日本語名と安定IDを併記したPopup表示名へ変換します。</summary>
        /// <param name="materials">登録順の材質設定配列です。</param>
        /// <returns>配列位置と1対1に対応するPopup選択肢です。</returns>
        private static string[] BuildMaterialChoices(TerrainMaterialArtSource[] materials)
        {
            string[] choices = new string[materials.Length];
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                TerrainMaterialArtSource material = materials[materialIndex];
                if (material == null)
                {
                    choices[materialIndex] = $"{materialIndex}: null";
                    continue;
                }

                string id = string.IsNullOrWhiteSpace(material.MaterialId)
                    ? "ID未設定"
                    : material.MaterialId;
                string japaneseName = string.IsNullOrWhiteSpace(material.JapaneseName)
                    ? "日本語名未設定"
                    : material.JapaneseName;
                choices[materialIndex] = $"{materialIndex}: {id} / {japaneseName}";
            }

            return choices;
        }
    }
}
