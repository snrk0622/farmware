// このファイルは、地形材質の手描きPNGだけへ共通Import設定と16×16 px分割を自動適用します。
// TerrainTileArtGenerationSettingsは分割後のSpriteを参照し、境界合成GeneratorはRead/Write可能な画素を読み取ります。

using System;
using System.Collections.Generic;
using System.IO;
using Farmware.WorldGeneration.Authoring;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 地形材質PNGの自動Import結果を表します。
    /// 自動監視側は変更の有無を使い、同じAssetを繰り返し再Importしないようにします。
    /// </summary>
    public readonly struct TerrainMaterialSpriteImportResult
    {
        /// <summary>Importer設定または新規Sprite分割を変更した場合はtrueです。</summary>
        public bool Changed { get; }

        /// <summary>新規PNGまたは初回Import中の仮区画を12個のSprite区画へ分割した場合はtrueです。</summary>
        public bool CreatedSpriteRects { get; }

        /// <summary>検査または更新したAssets配下のPNGパスです。</summary>
        public string AssetPath { get; }

        /// <summary>1枚の地形材質PNGに対する処理結果を変更不能な値としてまとめます。</summary>
        /// <param name="changed">ImporterまたはSprite区画を変更したかを示します。</param>
        /// <param name="createdSpriteRects">新しいSprite区画を作成または初回Import中に置換したかを示します。</param>
        /// <param name="assetPath">処理したAssets配下のPNGパスです。</param>
        public TerrainMaterialSpriteImportResult(
            bool changed,
            bool createdSpriteRects,
            string assetPath)
        {
            Changed = changed;
            CreatedSpriteRects = createdSpriteRects;
            AssetPath = assetPath;
        }
    }

    /// <summary>
    /// 地形材質の64×48 px入力PNGへ、Pixel Art向けImporter設定と4列×3行のSprite区画を適用します。
    /// 初回Import中にUnityが作った規約外区画だけは置換し、既存区画はSerialized参照を守るため作り直しません。
    /// </summary>
    public static class TerrainMaterialSpriteImportUtility
    {
        /// <summary>自動Importの対象にする、手描き地形材質PNG専用フォルダーです。</summary>
        public const string SourceFolder =
            "Assets/Farmware/Art/WorldGeneration/Source";

        /// <summary>1枚の材質PNGに横方向へ並べる16×16 px Sprite数です。</summary>
        public const int ColumnCount = 4;

        /// <summary>上からGroundTop、Face、FaceBoundaryOverlayを並べる行数です。</summary>
        public const int RowCount = 3;

        /// <summary>1材質PNGを正しく分割したときのSprite総数です。</summary>
        public const int ExpectedSpriteCount = ColumnCount * RowCount;

        /// <summary>境界なしの既存材質が上段と中段だけを分割した場合のSprite総数です。</summary>
        public const int BoundarylessSpriteCount = ColumnCount * 2;

        /// <summary>入力PNGに必要な横幅です。単位は元画像のピクセルです。</summary>
        public const int ExpectedWidthInPixels =
            TerrainTileArtGenerationSettings.TileSizeInPixels * ColumnCount;

        /// <summary>入力PNGに必要な高さです。単位は元画像のピクセルです。</summary>
        public const int ExpectedHeightInPixels =
            TerrainTileArtGenerationSettings.TileSizeInPixels * RowCount;

        /// <summary>
        /// 指定パスが地形材質入力フォルダー配下のPNGかを判定します。
        /// 大文字小文字の違う拡張子もPNGとして扱いますが、別フォルダーの画像は変更しません。
        /// </summary>
        /// <param name="assetPath">Assetsから始まる候補パスです。</param>
        /// <returns>材質入力専用フォルダー配下のPNGならtrueです。</returns>
        public static bool IsMaterialSourceTexture(string assetPath)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            return normalizedPath.StartsWith(SourceFolder + "/", StringComparison.Ordinal)
                && normalizedPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 1枚の地形材質PNGを検査し、共通Importer設定と未分割の新規PNGだけへ4×3分割を適用します。
        /// Unityが既存Spriteへ割り当てたIDは変更せず、Sceneや設定Assetの参照切れを防ぎます。
        /// </summary>
        /// <param name="assetPath">Assetsから始まる材質PNGパスです。</param>
        /// <returns>Importer変更と新規分割の有無を含む処理結果です。</returns>
        /// <exception cref="ArgumentException">対象外パスを指定した場合に送出します。</exception>
        /// <exception cref="InvalidOperationException">寸法、既存分割、Sprite編集能力が規約を満たさない場合に送出します。</exception>
        public static TerrainMaterialSpriteImportResult EnsureImported(string assetPath)
        {
            return EnsureImported(assetPath, false);
        }

        /// <summary>
        /// 1枚の地形材質PNGを検査し、初回Import中だけ規約外の自動生成区画も4×3分割へ置き換えます。
        /// </summary>
        /// <param name="assetPath">Assetsから始まる材質PNGパスです。</param>
        /// <param name="replaceInvalidRectsCreatedDuringInitialImport">
        /// TextureImporterに既存設定がなかった初回Importだけtrueにします。
        /// trueにすると規約外区画のSprite IDを作り直すため、既存参照を持つ通常の再Importには使用しません。
        /// </param>
        /// <returns>Importer変更と新規分割または初回区画置換の有無を含む処理結果です。</returns>
        /// <exception cref="ArgumentException">対象外パスを指定した場合に送出します。</exception>
        /// <exception cref="InvalidOperationException">寸法、既存分割、Sprite編集能力が規約を満たさない場合に送出します。</exception>
        public static TerrainMaterialSpriteImportResult EnsureImported(
            string assetPath,
            bool replaceInvalidRectsCreatedDuringInitialImport)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            if (!IsMaterialSourceTexture(normalizedPath))
            {
                throw new ArgumentException(
                    $"地形材質入力フォルダー配下のPNGではありません: {normalizedPath}",
                    nameof(assetPath));
            }

            TextureImporter importer =
                AssetImporter.GetAtPath(normalizedPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"地形材質PNGのTextureImporterを取得できません: {normalizedPath}");
            }

            // 既にMultipleだった画像の区画は参照元になり得るため、新規初回Importの既定Single区画と区別して扱います。
            bool wasConfiguredAsMultipleSprite =
                importer.textureType == TextureImporterType.Sprite
                && importer.spriteImportMode == SpriteImportMode.Multiple;

            // Sprite区画は元画像の座標を使うため、圧縮や最大Texture寸法の影響を受けるImport後Textureではなく元寸法を確認します。
            importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
            if (sourceWidth != ExpectedWidthInPixels
                || sourceHeight != ExpectedHeightInPixels)
            {
                throw new InvalidOperationException(
                    $"地形材質PNGは{ExpectedWidthInPixels}×{ExpectedHeightInPixels} pxで作成してください。"
                    + $" 現在は{sourceWidth}×{sourceHeight} pxです: {normalizedPath}");
            }

            bool importerChanged = ApplyCommonImporterSettings(importer);
            if (importerChanged)
            {
                // TextureImporterをSprite Multipleとして確定してからData Providerを取得します。
                // 先に保存しないと、新規PNGが通常TextureのProvider契約のままになるためです。
                importer.SaveAndReimport();
                importer = AssetImporter.GetAtPath(normalizedPath) as TextureImporter;
                if (importer == null)
                {
                    throw new InvalidOperationException(
                        $"共通設定適用後のTextureImporterを再取得できません: {normalizedPath}");
                }
            }

            ISpriteEditorDataProvider dataProvider = CreateDataProvider(importer, normalizedPath);
            SpriteRect[] existingRects = dataProvider.GetSpriteRects();
            bool hasNoSpriteRects = existingRects == null || existingRects.Length == 0;
            bool requiresInitialSlicing = !wasConfiguredAsMultipleSprite
                || hasNoSpriteRects
                || HasSingleFullTextureRect(existingRects);
            if (!requiresInitialSlicing)
            {
                try
                {
                    ValidateExistingSpriteRects(existingRects, normalizedPath);
                    return new TerrainMaterialSpriteImportResult(
                        importerChanged,
                        false,
                        normalizedPath);
                }
                catch (InvalidOperationException) when (
                    replaceInvalidRectsCreatedDuringInitialImport)
                {
                    // 初回Importでは、Unityが透明領域を基に横長区画を先に作る場合があります。
                    // importSettingsMissingだった同じImportだけは参照元がまだ存在しないため、固定グリッドへ置換できます。
                }
            }

            ValidateSpriteCreationCapability(dataProvider, normalizedPath);
            CreateSpriteRects(dataProvider, normalizedPath);
            dataProvider.Apply();
            importer.SaveAndReimport();

            // Apply後に実際のImporterから再読込し、途中失敗を正常完了として扱わないよう区画数と配置を検査します。
            importer = AssetImporter.GetAtPath(normalizedPath) as TextureImporter;
            ISpriteEditorDataProvider reloadedProvider =
                CreateDataProvider(importer, normalizedPath);
            ValidateExistingSpriteRects(reloadedProvider.GetSpriteRects(), normalizedPath);
            return new TerrainMaterialSpriteImportResult(true, true, normalizedPath);
        }

        /// <summary>
        /// Unityの初回Importで残る、元画像全体を覆う既定1区画かを判定します。
        /// 材質シートとして有効な既存分割は8または12区画なので、1区画だけは新規分割前として扱えます。
        /// </summary>
        /// <param name="spriteRects">Data Providerから読み取った現在の区画です。</param>
        /// <returns>64×48 px全体を覆う1区画だけならtrueです。</returns>
        private static bool HasSingleFullTextureRect(SpriteRect[] spriteRects)
        {
            if (spriteRects == null || spriteRects.Length != 1)
            {
                return false;
            }

            Rect expectedRect = new Rect(
                0f,
                0f,
                ExpectedWidthInPixels,
                ExpectedHeightInPixels);
            return AreSameRect(spriteRects[0].rect, expectedRect);
        }

        /// <summary>Point、16 PPU、非圧縮など、地形材質入力に共通するTextureImporter設定を揃えます。</summary>
        /// <param name="importer">地形材質PNGを所有するTextureImporterです。</param>
        /// <returns>少なくとも1項目を変更した場合はtrueです。</returns>
        private static bool ApplyCommonImporterSettings(TextureImporter importer)
        {
            bool changed = false;
            changed |= SetIfDifferent(
                importer.textureType,
                TextureImporterType.Sprite,
                value => importer.textureType = value);
            changed |= SetIfDifferent(
                importer.spriteImportMode,
                SpriteImportMode.Multiple,
                value => importer.spriteImportMode = value);
            changed |= SetIfDifferent(
                importer.spritePixelsPerUnit,
                TerrainTileArtGenerator.PixelsPerUnit,
                value => importer.spritePixelsPerUnit = value);
            changed |= SetIfDifferent(
                importer.filterMode,
                FilterMode.Point,
                value => importer.filterMode = value);
            changed |= SetIfDifferent(
                importer.wrapMode,
                TextureWrapMode.Clamp,
                value => importer.wrapMode = value);
            changed |= SetIfDifferent(
                importer.mipmapEnabled,
                false,
                value => importer.mipmapEnabled = value);
            changed |= SetIfDifferent(
                importer.textureCompression,
                TextureImporterCompression.Uncompressed,
                value => importer.textureCompression = value);
            changed |= SetIfDifferent(
                importer.alphaSource,
                TextureImporterAlphaSource.FromInput,
                value => importer.alphaSource = value);
            changed |= SetIfDifferent(
                importer.alphaIsTransparency,
                true,
                value => importer.alphaIsTransparency = value);
            changed |= SetIfDifferent(
                importer.sRGBTexture,
                true,
                value => importer.sRGBTexture = value);
            changed |= SetIfDifferent(
                importer.isReadable,
                true,
                value => importer.isReadable = value);
            changed |= SetIfDifferent(
                importer.npotScale,
                TextureImporterNPOTScale.None,
                value => importer.npotScale = value);

            TextureImporterSettings textureSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(textureSettings);
            if (textureSettings.spriteMeshType != SpriteMeshType.FullRect)
            {
                textureSettings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(textureSettings);
                changed = true;
            }

            return changed;
        }

        /// <summary>Importerから公式Sprite Editor Data Providerを取得し、読み書き前の初期化を完了します。</summary>
        /// <param name="importer">Sprite Multipleとして設定済みのImporterです。</param>
        /// <param name="assetPath">失敗理由へ表示するPNGパスです。</param>
        /// <returns>初期化済みのSprite Editor Data Providerです。</returns>
        private static ISpriteEditorDataProvider CreateDataProvider(
            AssetImporter importer,
            string assetPath)
        {
            if (importer == null)
            {
                throw new InvalidOperationException(
                    $"Sprite分割対象のImporterがありません: {assetPath}");
            }

            SpriteDataProviderFactories factories = new SpriteDataProviderFactories();
            factories.Init();
            ISpriteEditorDataProvider dataProvider =
                factories.GetSpriteEditorDataProviderFromObject(importer);
            if (dataProvider == null)
            {
                throw new InvalidOperationException(
                    $"Sprite Editor Data Providerを取得できません: {assetPath}");
            }

            dataProvider.InitSpriteEditorDataProvider();
            return dataProvider;
        }

        /// <summary>新しい12区画を安全に作れるImporterか、公式能力APIですべて確認します。</summary>
        /// <param name="dataProvider">初期化済みSprite Editor Data Providerです。</param>
        /// <param name="assetPath">失敗理由へ表示するPNGパスです。</param>
        private static void ValidateSpriteCreationCapability(
            ISpriteEditorDataProvider dataProvider,
            string assetPath)
        {
            ISpriteFrameEditCapability editCapability =
                dataProvider.GetDataProvider<ISpriteFrameEditCapability>();
            if (editCapability == null)
            {
                throw new InvalidOperationException(
                    $"Sprite編集能力を確認できないため自動分割を中止しました: {assetPath}");
            }

            // 必要能力を一つでも欠くImporterへSetSpriteRectsすると、既存メタデータを壊す可能性があるため変更前に中断します。
            var capability = editCapability.GetEditCapability();
            bool supportsRequiredEdits =
                capability.HasCapability(EEditCapability.CreateAndDeleteSprite)
                && capability.HasCapability(EEditCapability.EditSpriteName)
                && capability.HasCapability(EEditCapability.EditSpriteRect)
                && capability.HasCapability(EEditCapability.EditPivot);
            if (!supportsRequiredEdits)
            {
                throw new InvalidOperationException(
                    $"4×3分割に必要なSprite編集能力がないため自動分割を中止しました: {assetPath}");
            }
        }

        /// <summary>未分割または初回Import中のPNGへ、上段から左→右の順で12個の16×16 px区画を作成します。</summary>
        /// <param name="dataProvider">Sprite作成能力を確認済みのData Providerです。</param>
        /// <param name="assetPath">Sprite名の基礎名を取得するPNGパスです。</param>
        private static void CreateSpriteRects(
            ISpriteEditorDataProvider dataProvider,
            string assetPath)
        {
            ISpriteNameFileIdDataProvider nameFileIdProvider =
                dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameFileIdProvider == null)
            {
                throw new InvalidOperationException(
                    $"Sprite名とFile IDを安全に保存できないため自動分割を中止しました: {assetPath}");
            }

            string baseName = Path.GetFileNameWithoutExtension(assetPath);
            SpriteRect[] spriteRects = new SpriteRect[ExpectedSpriteCount];
            List<SpriteNameFileIdPair> nameFileIdPairs =
                new List<SpriteNameFileIdPair>(ExpectedSpriteCount);
            int size = TerrainTileArtGenerationSettings.TileSizeInPixels;
            for (int row = 0; row < RowCount; row++)
            {
                for (int column = 0; column < ColumnCount; column++)
                {
                    int index = row * ColumnCount + column;
                    GUID spriteId = GUID.Generate();
                    string spriteName = $"{baseName}_{index}";

                    // Sprite座標は左下原点なので、制作画像の上段をrow 0として扱うためYだけ上下反転します。
                    Rect rect = new Rect(
                        column * size,
                        (RowCount - 1 - row) * size,
                        size,
                        size);
                    spriteRects[index] = new SpriteRect
                    {
                        name = spriteName,
                        rect = rect,
                        alignment = SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                        border = Vector4.zero,
                        spriteID = spriteId
                    };
                    nameFileIdPairs.Add(new SpriteNameFileIdPair(spriteName, spriteId));
                }
            }

            dataProvider.SetSpriteRects(spriteRects);
            nameFileIdProvider.SetNameFileIdPairs(nameFileIdPairs);
        }

        /// <summary>
        /// 既存Sprite区画が参照を維持したまま利用できる規約と一致することを確認します。
        /// 境界なしの既存PNGは上2行の8区画、境界候補を持つPNGは全3行の12区画を許可します。
        /// </summary>
        /// <param name="spriteRects">Importerへ保存されている既存区画です。</param>
        /// <param name="assetPath">期待するSprite名とエラー表示に使うPNGパスです。</param>
        private static void ValidateExistingSpriteRects(
            SpriteRect[] spriteRects,
            string assetPath)
        {
            int spriteCount = spriteRects?.Length ?? 0;
            if (spriteCount != BoundarylessSpriteCount
                && spriteCount != ExpectedSpriteCount)
            {
                throw new InvalidOperationException(
                    $"既存Sprite分割は自動修復しません。参照維持のため、"
                    + $"境界なしは{BoundarylessSpriteCount}区画、境界候補ありは{ExpectedSpriteCount}区画か確認してから"
                    + $"明示的に再分割してください: {assetPath}");
            }

            string baseName = Path.GetFileNameWithoutExtension(assetPath);
            int size = TerrainTileArtGenerationSettings.TileSizeInPixels;
            for (int expectedIndex = 0; expectedIndex < spriteCount; expectedIndex++)
            {
                int row = expectedIndex / ColumnCount;
                int column = expectedIndex % ColumnCount;
                string expectedName = $"{baseName}_{expectedIndex}";
                SpriteRect actual = FindSpriteRectByName(spriteRects, expectedName);
                if (actual == null)
                {
                    throw new InvalidOperationException(
                        $"既存Sprite分割に「{expectedName}」がありません。"
                        + " Serialized参照を守るため自動修復せず停止しました。"
                        + $" 対象: {assetPath}");
                }

                Rect expectedRect = new Rect(
                    column * size,
                    (RowCount - 1 - row) * size,
                    size,
                    size);
                if (!AreSameRect(actual.rect, expectedRect)
                    || actual.alignment != SpriteAlignment.Center
                    || !AreSameVector(actual.pivot, new Vector2(0.5f, 0.5f)))
                {
                    throw new InvalidOperationException(
                        $"既存Sprite分割「{actual.name}」が材質シート規約と一致しません。"
                        + " Serialized参照を守るため自動修復せず停止しました。"
                        + $" 対象: {assetPath}");
                }
            }
        }

        /// <summary>Data Providerの返却順に依存せず、安定名が一致する既存Sprite区画を探します。</summary>
        /// <param name="spriteRects">Importerへ保存されている全区画です。</param>
        /// <param name="expectedName">ファイル名と上段左からの番号で作る期待名です。</param>
        /// <returns>一致した区画、存在しない場合はnullです。</returns>
        private static SpriteRect FindSpriteRectByName(
            SpriteRect[] spriteRects,
            string expectedName)
        {
            for (int index = 0; index < spriteRects.Length; index++)
            {
                if (string.Equals(
                    spriteRects[index].name,
                    expectedName,
                    StringComparison.Ordinal))
                {
                    return spriteRects[index];
                }
            }

            return null;
        }

        /// <summary>浮動小数誤差を許容してSprite区画の位置と寸法を比較します。</summary>
        /// <param name="left">Importerから取得した区画です。</param>
        /// <param name="right">規約から計算した期待区画です。</param>
        /// <returns>位置と寸法がすべて一致する場合はtrueです。</returns>
        private static bool AreSameRect(Rect left, Rect right)
        {
            return Mathf.Approximately(left.x, right.x)
                && Mathf.Approximately(left.y, right.y)
                && Mathf.Approximately(left.width, right.width)
                && Mathf.Approximately(left.height, right.height);
        }

        /// <summary>浮動小数誤差を許容してPivotの2成分を比較します。</summary>
        /// <param name="left">Importerから取得したPivotです。</param>
        /// <param name="right">規約で定めた期待Pivotです。</param>
        /// <returns>2成分が一致する場合はtrueです。</returns>
        private static bool AreSameVector(Vector2 left, Vector2 right)
        {
            return Mathf.Approximately(left.x, right.x)
                && Mathf.Approximately(left.y, right.y);
        }

        /// <summary>現在値が期待値と違う場合だけSetterを呼び、不要な再Importを防ぎます。</summary>
        /// <typeparam name="T">比較するImporter設定値の型です。</typeparam>
        /// <param name="current">現在のImporter設定です。</param>
        /// <param name="expected">地形材質入力に必要な設定です。</param>
        /// <param name="setter">異なる場合だけ呼ぶ代入処理です。</param>
        /// <returns>値を変更した場合はtrueです。</returns>
        private static bool SetIfDifferent<T>(T current, T expected, Action<T> setter)
        {
            if (EqualityComparer<T>.Default.Equals(current, expected))
            {
                return false;
            }

            setter(expected);
            return true;
        }

        /// <summary>Assetパスの区切りをUnity標準のスラッシュへ統一します。</summary>
        /// <param name="assetPath">区切りが混在する可能性のあるパスです。</param>
        /// <returns>nullを空文字へ変換し、バックスラッシュを除いたパスです。</returns>
        private static string NormalizeAssetPath(string assetPath)
        {
            return (assetPath ?? string.Empty).Replace('\\', '/');
        }
    }

    /// <summary>
    /// 地形材質PNGの初回Importを通常のProject変更監視より前に検知します。
    /// 初回であることを監視処理へ渡し、Asepriteから直接Exportした画像にUnityが仮区画を作っても安全に置換できるようにします。
    /// </summary>
    internal sealed class TerrainMaterialSpriteSourcePostprocessor : AssetPostprocessor
    {
        /// <summary>
        /// Textureの読込前に呼ばれ、初回Import状態を記録します。
        /// 既存PNGの再Importでは初回状態を記録しないため、保存済みSprite IDは後段の監視処理で維持されます。
        /// </summary>
        private void OnPreprocessTexture()
        {
            if (!TerrainMaterialSpriteImportUtility.IsMaterialSourceTexture(assetPath))
            {
                return;
            }

            TextureImporter importer = assetImporter as TextureImporter;
            if (importer == null)
            {
                return;
            }

            // importSettingsMissingは対応する.metaがない初回だけtrueになるため、既存参照を持つ再Importと区別できます。
            if (importer.importSettingsMissing)
            {
                TerrainMaterialSpriteImportWatcher.MarkInitialImport(assetPath);
            }
        }
    }

    /// <summary>
    /// Project内のAsset変更後に地形材質入力フォルダーを検査し、新規PNGへ安全なImport規約を適用します。
    /// AssetDatabaseの更新中へ再入しないよう、Project変更通知をEditorの次回更新まで遅延してまとめます。
    /// </summary>
    [InitializeOnLoad]
    internal static class TerrainMaterialSpriteImportWatcher
    {
        // SessionStateへ初回Importを記録するキーの接頭辞です。Assembly再読込を挟んでも同じEditorセッション中は状態を維持します。
        private const string InitialImportSessionKeyPrefix =
            "Farmware.TerrainMaterialSpriteInitialImport.";

        // 同じProject変更通知が連続しても、次回更新へ登録する検査は1回だけにします。
        private static bool scanScheduled;

        // SaveAndReimportがProject変更を再通知しても、自分の処理中へ再入しないための状態です。
        private static bool applyingImportSettings;

        // 不正な既存分割を毎回Consoleへ重複表示せず、内容が変わるまで一度だけ知らせるため保持します。
        private static readonly HashSet<string> ReportedErrors =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Assembly読込時にProject変更監視を登録し、既存入力も一度検査します。</summary>
        static TerrainMaterialSpriteImportWatcher()
        {
            EditorApplication.projectChanged += ScheduleScan;
            ScheduleScan();
        }

        /// <summary>
        /// 対応する.metaがない地形材質PNGを、現在のEditorセッションで初回Import中として記録します。
        /// </summary>
        /// <param name="assetPath">Unityが通知したAssets配下のPNGパスです。</param>
        internal static void MarkInitialImport(string assetPath)
        {
            SessionState.SetBool(BuildInitialImportSessionKey(assetPath), true);
        }

        /// <summary>Asset Import完了後の次回Editor更新へ、材質入力フォルダーの検査を1回だけ予約します。</summary>
        private static void ScheduleScan()
        {
            if (scanScheduled || applyingImportSettings)
            {
                return;
            }

            scanScheduled = true;
            EditorApplication.delayCall += ScanMaterialSourceFolder;
        }

        /// <summary>登録済み材質PNGを順番に検査し、必要なImporter変更と新規分割だけを適用します。</summary>
        private static void ScanMaterialSourceFolder()
        {
            scanScheduled = false;
            if (applyingImportSettings)
            {
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                ScheduleScan();
                return;
            }

            applyingImportSettings = true;
            try
            {
                string[] textureGuids = AssetDatabase.FindAssets(
                    "t:Texture2D",
                    new[] { TerrainMaterialSpriteImportUtility.SourceFolder });
                for (int guidIndex = 0; guidIndex < textureGuids.Length; guidIndex++)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(textureGuids[guidIndex]);
                    if (!TerrainMaterialSpriteImportUtility.IsMaterialSourceTexture(assetPath))
                    {
                        continue;
                    }

                    try
                    {
                        bool isInitialImport = IsInitialImportPending(assetPath);
                        TerrainMaterialSpriteImportResult result =
                            TerrainMaterialSpriteImportUtility.EnsureImported(
                                assetPath,
                                isInitialImport);
                        ClearInitialImport(assetPath);
                        RemoveReportedErrorsForPath(assetPath);
                        if (result.Changed)
                        {
                            string action = result.CreatedSpriteRects
                                ? "共通Importer設定と4×3分割を適用"
                                : "共通Importer設定を修正";
                            Debug.Log($"地形材質PNGへ{action}しました: {assetPath}");
                        }
                    }
                    catch (Exception exception)
                    {
                        string errorKey = $"{assetPath}|{exception.Message}";
                        if (ReportedErrors.Add(errorKey))
                        {
                            Debug.LogError(
                                $"地形材質PNGの自動Importを完了できませんでした。"
                                + $" 既存参照を守るため、この画像の自動変更を停止しました。\n{exception.Message}");
                        }
                    }
                }
            }
            finally
            {
                applyingImportSettings = false;
            }
        }

        /// <summary>指定PNGが現在のEditorセッションで初回Import中かを取得します。</summary>
        /// <param name="assetPath">Assets配下の地形材質PNGパスです。</param>
        /// <returns>対応する.metaがない状態からImportを開始していればtrueです。</returns>
        private static bool IsInitialImportPending(string assetPath)
        {
            return SessionState.GetBool(
                BuildInitialImportSessionKey(assetPath),
                false);
        }

        /// <summary>正常なImporter設定とSprite区画を確認できたPNGの初回Import状態を解除します。</summary>
        /// <param name="assetPath">検査に成功した地形材質PNGパスです。</param>
        private static void ClearInitialImport(string assetPath)
        {
            SessionState.SetBool(BuildInitialImportSessionKey(assetPath), false);
        }

        /// <summary>PNGごとに衝突しないSessionStateキーを作成します。</summary>
        /// <param name="assetPath">Unityが使用するAssets配下のパスです。</param>
        /// <returns>初回Import状態の保存と取得に使うキーです。</returns>
        private static string BuildInitialImportSessionKey(string assetPath)
        {
            return InitialImportSessionKeyPrefix
                + (assetPath ?? string.Empty).Replace('\\', '/');
        }

        /// <summary>対象PNGが正常になったとき、以前同じパスへ記録した重複抑制用エラーを取り除きます。</summary>
        /// <param name="assetPath">正常に検査できた材質PNGパスです。</param>
        private static void RemoveReportedErrorsForPath(string assetPath)
        {
            string prefix = assetPath + "|";
            ReportedErrors.RemoveWhere(
                errorKey => errorKey.StartsWith(prefix, StringComparison.Ordinal));
        }
    }
}
