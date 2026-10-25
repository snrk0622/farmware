// このファイルは、TerrainTileArtGenerationSettingsを編集・検証・生成する専用Inspectorを提供します。
// 通常のSerializedProperty表示を維持しながら、選択材質のSprite数、要再生成状態、生成操作を追加します。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Farmware.WorldGeneration.Authoring;
using UnityEditor;
using UnityEngine;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// C6-A地形タイル生成設定へ、段階的な代表材質試作と41材質一括生成の入口を追加します。
    /// UndoとAsset保存は標準SerializedObjectへ任せ、生成時だけTerrainTileArtGeneratorを呼びます。
    /// </summary>
    [CustomEditor(typeof(TerrainTileArtGenerationSettings))]
    public sealed class TerrainTileArtGenerationSettingsEditor : UnityEditor.Editor
    {
        // Inspector幅に応じて折り返しつつ、16×16 pxの模様を確認できる表示サイズです。
        private const float PreviewImageSize = 64f;

        // 16接続を4×4で読みやすく並べるため、横方向の最大セル数を4に固定します。
        private const int MaximumPreviewColumns = 4;

        // Inspector内でプレビュー・単体生成するMaterials配列位置です。設定Asset自体へ制作上不要な選択状態を保存しません。
        [Tooltip("専用Inspectorでプレビュー・単体生成する材質Indexです。地形材質設定アセットやRuntime結果には保存しません。")]
        [SerializeField] private int selectedMaterialIndex;

        // 最後にボタンで実行した検証結果です。全Repaintで入力Textureを読み直さないよう明示操作時だけ更新します。
        private TerrainTileArtValidationReport lastValidationReport;

        // 直前の生成または検証結果を短く表示し、操作が完了した材質と出力枚数を確認できるようにします。
        private string lastOperationMessage;

        // 生成結果一覧全体の開閉状態です。大量のSprite設定を編集するときは閉じてInspectorを短くできます。
        private bool showGeneratedPreviews = true;

        // 16接続WallTop一覧の開閉状態です。
        private bool showWallTopPreviews = true;

        // Ground用FaceBoundary合成結果一覧の開閉状態です。
        private bool showGroundBoundaryPreviews = true;

        // Wall用FaceBoundary合成結果一覧の開閉状態です。
        private bool showWallBoundaryPreviews = true;

        // 現在キャッシュしているMaterials配列位置です。選択変更時だけAssetDatabaseから読み直します。
        private int cachedPreviewMaterialIndex = -1;

        // 現在キャッシュしている正常生成Fingerprintです。再生成後に一覧を自動更新するため使用します。
        private string cachedPreviewFingerprint;

        // WallTop生成物をMask順で保持し、OnInspectorGUIの再描画ごとのAsset検索を避けます。
        private readonly List<GeneratedPreviewEntry> wallTopPreviewEntries =
            new List<GeneratedPreviewEntry>();

        // Ground用FaceBoundary生成物をFace・Boundary順で保持します。
        private readonly List<GeneratedPreviewEntry> groundBoundaryPreviewEntries =
            new List<GeneratedPreviewEntry>();

        // Wall用FaceBoundary生成物をFace・Boundary順で保持します。
        private readonly List<GeneratedPreviewEntry> wallBoundaryPreviewEntries =
            new List<GeneratedPreviewEntry>();

        // 各プレビューセルの短い識別名を中央揃えで表示するため、一度だけ作るGUIStyleです。
        private GUIStyle previewLabelStyle;

        /// <summary>
        /// 生成済みSpriteの表示名、選択用GUIContent、Asset参照をまとめます。
        /// Inspector再描画のたびに文字列やGUIContentを作り直さないためのEditor専用キャッシュです。
        /// </summary>
        private sealed class GeneratedPreviewEntry
        {
            /// <summary>セル上部へ表示するMaskまたはFace・Boundary番号です。</summary>
            public string Label { get; }

            /// <summary>Projectビューで選択する生成済みSpriteです。削除済みの場合はnullです。</summary>
            public Sprite Sprite { get; }

            /// <summary>AssetパスTooltipを持つ、再利用可能な透明ボタン内容です。</summary>
            public GUIContent ButtonContent { get; }

            /// <summary>
            /// 生成済みSpriteをInspectorの1セルとして表示できる形へまとめます。
            /// </summary>
            /// <param name="label">Maskまたは差分番号を示す短い表示名です。</param>
            /// <param name="sprite">AssetDatabaseから読み込んだ完成Spriteです。</param>
            /// <param name="assetPath">マウスを重ねたときに表示するプロジェクト相対パスです。</param>
            public GeneratedPreviewEntry(string label, Sprite sprite, string assetPath)
            {
                Label = label;
                Sprite = sprite;
                ButtonContent = new GUIContent(string.Empty, assetPath);
            }
        }

        /// <summary>
        /// プレビューの識別名を中央揃え・折り返しで描くStyleを必要になった時点で一度だけ作ります。
        /// </summary>
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

        /// <summary>
        /// UnityがInspectorを描画するときに呼び、標準設定、材質プレビュー、検証・生成ボタンを順に表示します。
        /// </summary>
        public override void OnInspectorGUI()
        {
            TerrainTileArtGenerationSettings settings =
                (TerrainTileArtGenerationSettings)target;

            // 標準描画を使うことで、可変長配列の追加・削除、Object Picker、Undo、Prefab差分をUnity標準挙動に揃えます。
            bool changed = DrawDefaultInspector();
            if (changed)
            {
                lastValidationReport = null;
                lastOperationMessage = "設定が変更されました。対象材質を再検証してください。";
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("C6-A 生成ツール", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "入力Spriteは16×16 px、Read/Write有効、Alpha 0/255で用意してください。"
                + " 生成先の同名PNGは更新しますが、手描き入力は変更しません。",
                MessageType.Info);

            TerrainMaterialArtSource[] materials = settings.Materials;
            if (materials == null || materials.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "Materialsへ材質を1件以上追加すると、検証と生成を実行できます。",
                    MessageType.Warning);
                return;
            }

            selectedMaterialIndex = Mathf.Clamp(
                selectedMaterialIndex,
                0,
                materials.Length - 1);
            selectedMaterialIndex = EditorGUILayout.Popup(
                "対象材質",
                selectedMaterialIndex,
                BuildMaterialChoices(materials));

            TerrainMaterialArtSource selectedMaterial = materials[selectedMaterialIndex];
            DrawSelectedMaterialSummary(settings, selectedMaterial);
            DrawOperationButtons(settings);
            DrawLastReport();
            DrawGeneratedPreviews(selectedMaterial);
        }

        /// <summary>
        /// 材質ID、日本語名、Sprite数、要再生成状態を選択材質の下へ表示します。
        /// </summary>
        /// <param name="settings">共通内部色と出力先を持つ設定アセットです。</param>
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
                $"WallTop {counts.WallTopOutputCount} / Ground境界 {counts.GroundBoundaryOutputCount} / Wall境界 {counts.WallBoundaryOutputCount}");
            EditorGUILayout.LabelField(
                "中央暗色領域",
                $"{16 - material.SideRimWidth * 2}×{16 - material.TopRimWidth - material.BottomRimWidth} px");

            bool requiresRegeneration =
                TerrainTileArtGenerator.RequiresRegeneration(settings, material);
            EditorGUILayout.HelpBox(
                requiresRegeneration
                    ? "未生成、または入力・設定が前回生成時から変わっています。"
                    : "入力・設定は前回の正常生成時と一致しています。",
                requiresRegeneration ? MessageType.Warning : MessageType.Info);

        }

        /// <summary>
        /// 選択材質と全材質の検証・生成ボタンを表示し、例外をConsoleとInspectorの両方へ残します。
        /// </summary>
        /// <param name="settings">操作対象の設定アセットです。</param>
        private void DrawOperationButtons(TerrainTileArtGenerationSettings settings)
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

                if (GUILayout.Button("対象材質を生成"))
                {
                    RunSelectedGeneration(settings);
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

                if (GUILayout.Button("全材質を生成"))
                {
                    RunAllGeneration(settings);
                }
            }
        }

        /// <summary>
        /// 選択材質だけを同期生成し、完了結果または失敗理由をInspectorへ保存します。
        /// </summary>
        /// <param name="settings">操作対象の設定アセットです。</param>
        private void RunSelectedGeneration(TerrainTileArtGenerationSettings settings)
        {
            try
            {
                lastValidationReport =
                    TerrainTileArtGenerator.ValidateMaterial(settings, selectedMaterialIndex);
                if (lastValidationReport.HasErrors)
                {
                    lastOperationMessage = "対象材質を生成できません。下の検証エラーを修正してください。";
                    return;
                }

                TerrainTileArtGenerationResult result =
                    TerrainTileArtGenerator.GenerateMaterial(settings, selectedMaterialIndex);
                InvalidatePreviewCache();
                lastOperationMessage =
                    $"{result.MaterialId}: {result.GeneratedSpriteCount}枚を生成しました。出力: {result.OutputFolder}";
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                lastOperationMessage = $"生成に失敗しました: {exception.Message}";
            }
        }

        /// <summary>
        /// 全材質の事前検証後、出力枚数を確認してから一括生成します。
        /// </summary>
        /// <param name="settings">操作対象の設定アセットです。</param>
        private void RunAllGeneration(TerrainTileArtGenerationSettings settings)
        {
            lastValidationReport = TerrainTileArtGenerator.ValidateAll(settings);
            if (lastValidationReport.HasErrors)
            {
                lastOperationMessage = "全材質を生成できません。下の検証エラーを修正してください。";
                return;
            }

            int totalOutputCount = 0;
            for (int materialIndex = 0;
                 materialIndex < settings.Materials.Length;
                 materialIndex++)
            {
                totalOutputCount = checked(
                    totalOutputCount
                    + TerrainTileArtGenerator.CalculateSpriteCounts(
                        settings.Materials[materialIndex]).GeneratedOutputCount);
            }

            bool accepted = EditorUtility.DisplayDialog(
                "全地形材質Spriteを生成",
                $"{settings.Materials.Length}材質、合計{totalOutputCount}枚のPNGを生成・再Importします。続行しますか？",
                "生成する",
                "キャンセル");
            if (!accepted)
            {
                lastOperationMessage = "全材質生成をキャンセルしました。";
                return;
            }

            try
            {
                IReadOnlyList<TerrainTileArtGenerationResult> results =
                    TerrainTileArtGenerator.GenerateAll(settings);
                int generatedCount = 0;
                for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
                {
                    generatedCount = checked(
                        generatedCount + results[resultIndex].GeneratedSpriteCount);
                }

                lastOperationMessage =
                    $"全{results.Count}材質、{generatedCount}枚のSprite生成が完了しました。";
                InvalidatePreviewCache();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                lastOperationMessage = $"全材質生成に失敗しました: {exception.Message}";
            }
        }

        /// <summary>
        /// 最後の操作結果と、検証で見つかった全エラー・警告をHelpBoxとして表示します。
        /// </summary>
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

            for (int errorIndex = 0;
                 errorIndex < lastValidationReport.Errors.Count;
                 errorIndex++)
            {
                EditorGUILayout.HelpBox(
                    lastValidationReport.Errors[errorIndex],
                    MessageType.Error);
            }

            for (int warningIndex = 0;
                 warningIndex < lastValidationReport.Warnings.Count;
                 warningIndex++)
            {
                EditorGUILayout.HelpBox(
                    lastValidationReport.Warnings[warningIndex],
                    MessageType.Warning);
            }
        }

        /// <summary>
        /// 選択材質の16接続WallTopとGround／Wall境界合成結果を、比較しやすい画像グリッドで表示します。
        /// 画像をクリックするとProjectビューで対象Spriteを選択し、Importerや拡大表示へすぐ移動できます。
        /// </summary>
        /// <param name="material">生成履歴とAssetパスを持つ、現在選択中の材質です。</param>
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
                    "対象材質を生成すると、WallTop 16接続状態とGround／Wall境界をここへ一覧表示します。",
                    MessageType.Info);
                return;
            }

            // 生成履歴の変更またはDomain Reloadによる空キャッシュを検出した場合だけAsset参照を再構築し、
            // 通常のInspector再描画ではAssetDatabaseを繰り返し検索しないようにします。
            int cachedPreviewCount = wallTopPreviewEntries.Count
                + groundBoundaryPreviewEntries.Count
                + wallBoundaryPreviewEntries.Count;
            if (cachedPreviewMaterialIndex != selectedMaterialIndex
                || !string.Equals(
                    cachedPreviewFingerprint,
                    material.LastGeneratedFingerprint,
                    StringComparison.Ordinal)
                || cachedPreviewCount != material.LastGeneratedAssetPaths.Count)
            {
                RebuildPreviewCache(material);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox(
                    "画像をクリックするとProjectビューで生成Spriteを選択します。",
                    MessageType.None);
                if (GUILayout.Button("一覧を再読込", GUILayout.Width(96f)))
                {
                    RebuildPreviewCache(material);
                }
            }

            showWallTopPreviews = EditorGUILayout.Foldout(
                showWallTopPreviews,
                $"WallTop 16接続 ({wallTopPreviewEntries.Count}枚)",
                true);
            if (showWallTopPreviews)
            {
                DrawPreviewGrid(wallTopPreviewEntries);
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
                        "Wall暗色化が無効なため、Wall境界はGround境界の生成Spriteを再利用します。",
                        MessageType.Info);
                }
            }
        }

        /// <summary>
        /// 生成履歴のパスを用途別に分類し、画像、Tooltip、比較用ラベルを一度だけ読み込みます。
        /// </summary>
        /// <param name="material">再構築する選択材質です。</param>
        private void RebuildPreviewCache(TerrainMaterialArtSource material)
        {
            wallTopPreviewEntries.Clear();
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
                if (assetPath.Contains("/WallTop/", StringComparison.Ordinal))
                {
                    wallTopPreviewEntries.Add(
                        new GeneratedPreviewEntry(
                            BuildWallTopPreviewLabel(fileName),
                            sprite,
                            assetPath));
                }
                else if (fileName.Contains("_GroundFace_", StringComparison.Ordinal))
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

        /// <summary>
        /// 生成PNGから完成Spriteを取得します。
        /// UnityはImport直後とDomain Reload後でPNGの主ObjectをTextureまたはSpriteとして返す場合があるため、
        /// 直接取得できないときは同じPNG内のサブアセットも調べます。
        /// </summary>
        /// <param name="assetPath">生成履歴へ保存したPNGのプロジェクト相対パスです。</param>
        /// <returns>最初に見つかったSprite、またはImport不備・削除済みならnullです。</returns>
        private static Sprite LoadGeneratedSprite(string assetPath)
        {
            Sprite directSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (directSprite != null)
            {
                return directSprite;
            }

            // Single Sprite設定でもSpriteがTextureのサブアセットになる環境があるため、型を確認しながら探索します。
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

        /// <summary>
        /// キャッシュ済みの生成結果をInspector幅に応じて1〜4列へ折り返して描画します。
        /// </summary>
        /// <param name="entries">同じ用途に分類済みの生成Sprite一覧です。</param>
        private void DrawPreviewGrid(IReadOnlyList<GeneratedPreviewEntry> entries)
        {
            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "生成済みSpriteが見つかりません。一覧を再読込するか、対象材質を再生成してください。",
                    MessageType.Warning);
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

        /// <summary>
        /// 1枚の生成Spriteを識別名と4倍画像ボタンで表示し、クリック時にProjectビューへ誘導します。
        /// </summary>
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

                // 16×16画像を64×64へ整数倍拡大し、ドットの形と境界を比較しやすくします。
                // 通常の画像付きButtonはTextureを原寸付近で描くため、背景枠・画像・透明ボタンを分けます。
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
                GUI.DrawTextureWithTexCoords(
                    imageRect,
                    texture,
                    normalizedSourceRect,
                    true);

                // GUIStyle.noneの透明ボタンを画像へ重ね、拡大表示を隠さず選択操作とTooltipだけを受け持たせます。
                if (GUI.Button(imageRect, entry.ButtonContent, GUIStyle.none))
                {
                    Selection.activeObject = entry.Sprite;
                    EditorGUIUtility.PingObject(entry.Sprite);
                }
            }
        }

        /// <summary>
        /// WallTopファイル名から外周セット番号、16進Mask、同材質Wallへの接続方向を組み立てます。
        /// </summary>
        /// <param name="fileName">拡張子を除いた生成ファイル名です。</param>
        /// <returns>例として「w00 m3 / 上+右」のような比較用ラベルを返します。</returns>
        private static string BuildWallTopPreviewLabel(string fileName)
        {
            int wallMarker = fileName.LastIndexOf("_w", StringComparison.Ordinal);
            int maskMarker = fileName.LastIndexOf("_m", StringComparison.Ordinal);
            if (wallMarker < 0 || maskMarker <= wallMarker || maskMarker + 2 >= fileName.Length)
            {
                return fileName;
            }

            string rimNumber = fileName.Substring(wallMarker + 2, maskMarker - wallMarker - 2);
            string maskText = fileName.Substring(maskMarker + 2);
            if (!int.TryParse(
                maskText,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out int maskValue))
            {
                return $"w{rimNumber} m{maskText}";
            }

            return $"w{rimNumber} m{maskText}\n{BuildConnectionDirectionLabel(maskValue)}";
        }

        /// <summary>
        /// Ground／Wall境界のファイル名からFace番号とBoundary番号だけを取り出します。
        /// </summary>
        /// <param name="fileName">拡張子を除いた生成ファイル名です。</param>
        /// <param name="usageMarker">GroundFaceまたはWallFaceの用途文字列です。</param>
        /// <returns>例として「f00 × b00」のような差分組合せラベルを返します。</returns>
        private static string BuildBoundaryPreviewLabel(
            string fileName,
            string usageMarker)
        {
            string marker = $"_{usageMarker}_";
            int markerIndex = fileName.LastIndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return fileName;
            }

            return fileName.Substring(markerIndex + marker.Length).Replace("_", " × ");
        }

        /// <summary>
        /// 4bit Maskを、外周を隠す同材質Wallの接続方向へ変換します。
        /// </summary>
        /// <param name="maskValue">上1・右2・下4・左8で構成する0〜15の値です。</param>
        /// <returns>接続なし、全接続、または「上+右」のような方向一覧です。</returns>
        private static string BuildConnectionDirectionLabel(int maskValue)
        {
            if (maskValue == 0)
            {
                return "接続なし";
            }

            if (maskValue == (int)TerrainWallConnectionMask.All)
            {
                return "全接続";
            }

            List<string> directions = new List<string>(4);
            if ((maskValue & (int)TerrainWallConnectionMask.Up) != 0)
            {
                directions.Add("上");
            }

            if ((maskValue & (int)TerrainWallConnectionMask.Right) != 0)
            {
                directions.Add("右");
            }

            if ((maskValue & (int)TerrainWallConnectionMask.Down) != 0)
            {
                directions.Add("下");
            }

            if ((maskValue & (int)TerrainWallConnectionMask.Left) != 0)
            {
                directions.Add("左");
            }

            return string.Join("+", directions);
        }

        /// <summary>
        /// 生成または対象切替後に、次のInspector描画でAsset参照を読み直すようキャッシュを無効化します。
        /// </summary>
        private void InvalidatePreviewCache()
        {
            cachedPreviewMaterialIndex = -1;
            cachedPreviewFingerprint = null;
        }

        /// <summary>
        /// Materials配列を、日本語名と安定IDを併記したPopup表示名へ変換します。
        /// </summary>
        /// <param name="materials">登録順の材質設定配列です。</param>
        /// <returns>配列位置と1対1に対応するPopup選択肢です。</returns>
        private static string[] BuildMaterialChoices(TerrainMaterialArtSource[] materials)
        {
            string[] choices = new string[materials.Length];
            for (int materialIndex = 0;
                 materialIndex < materials.Length;
                 materialIndex++)
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
