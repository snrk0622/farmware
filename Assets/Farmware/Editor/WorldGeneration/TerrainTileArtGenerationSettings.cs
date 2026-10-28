// このファイルは、地形材質Spriteの手描き入力と壁表示用の調整値をEditor内だけで保持します。
// Runtimeの地形状態は持たず、地形タイル生成ツールと検品Scene構築ツールの入力としてだけ使用します。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farmware.WorldGeneration.Authoring
{
    /// <summary>
    /// 1材質分のGroundTop、共通Face、任意のFaceBoundaryOverlayと壁暗化設定を保持します。
    /// Wall上面はGroundTopと同じSpriteを参照し、表示時に暗色Tintを掛けて区別します。
    /// </summary>
    [Serializable]
    public sealed class TerrainMaterialArtSource
    {
        /// <summary>Wall上面とWall用FaceBoundaryOverlayを初期状態で暗くするかを表します。</summary>
        public const bool DefaultDarkenWallSurface = true;

        /// <summary>Wall上面とWall用FaceBoundaryOverlayへ初期状態で掛けるRGB係数です。</summary>
        public const float DefaultWallDarkenFactor = 0.85f;

        // 出力フォルダー名と将来の材質Definition対応に使う、英数字中心の安定IDです。
        [Tooltip("材質の安定IDです。出力ファイル名にも使うため、英数字・ハイフン・アンダースコアだけで一意にしてください。")]
        [SerializeField] private string materialId;

        // 制作時の確認をしやすくする補助名であり、ゲーム内ローカライズ名ではありません。
        [Tooltip("Inspectorと制作記録で使う日本語名です。ゲーム内表示名や安定IDには使用しません。")]
        [SerializeField] private string japaneseName;

        // Ground上面とWall上面で共有する可変長の完成Spriteです。選択はワールド座標から決定論的に行います。
        [Tooltip("Ground上面とWall上面で共有する16×16 px Sprite差分です。1件以上登録し、全要素を均等確率で使用します。")]
        [SerializeField] private Sprite[] groundTopSprites = Array.Empty<Sprite>();

        // Ground崖面とWall前面の全段で共有する可変長の通常断面です。
        [Tooltip("Ground崖面とWall前面で共有する16×16 px Face差分です。1件以上登録し、全要素を均等確率で使用します。")]
        [SerializeField] private Sprite[] faceSprites = Array.Empty<Sprite>();

        // 断面1段目へ追加する任意Overlayです。空配列ならFaceを1段目からそのまま使います。
        [Tooltip("断面1段目へ重ねる透明な16×16 px Overlay差分です。境界不要の材質は空配列にしてください。")]
        [SerializeField] private Sprite[] faceBoundaryOverlays = Array.Empty<Sprite>();

        // trueならWall上面の表示TintとWall用FaceBoundaryOverlayの生成に同じ係数を使います。
        [Tooltip("有効ならWall上面を表示時に暗くし、Wall用FaceBoundaryOverlayも同じ係数で暗く生成します。通常材質は有効を基本とします。")]
        [SerializeField] private bool darkenWallSurface = DefaultDarkenWallSurface;

        // sRGB 8bit RGBへ乗算する係数です。Face本体には適用しません。
        [Tooltip("Wall上面の表示TintとWall用境界OverlayのRGBへ乗算する係数です。0より大きく1以下、初期値は0.85です。")]
        [Range(0.01f, 1f)]
        [SerializeField] private float wallDarkenFactor = DefaultWallDarkenFactor;

        // 最後に生成したFace・境界・暗化設定のFingerprintです。GroundTopは派生PNGへ使わないため含めません。
        [Tooltip("Editor生成ツールが管理する境界合成Fingerprintです。手入力せず、Face・境界・暗化設定の要更新判定に使用します。")]
        [HideInInspector]
        [SerializeField] private string lastGeneratedFingerprint;

        // 前回この材質から生成したAssetだけを記録し、差分数を減らした再生成時に古い出力を安全に削除します。
        [Tooltip("Editor生成ツールが管理する生成Assetパスです。手入力せず、同じ材質の古い出力だけを整理するために使用します。")]
        [HideInInspector]
        [SerializeField] private string[] lastGeneratedAssetPaths = Array.Empty<string>();

        /// <summary>英語の安定材質IDを返します。</summary>
        public string MaterialId => materialId;

        /// <summary>制作確認用の日本語名を返します。</summary>
        public string JapaneseName => japaneseName;

        /// <summary>Ground上面とWall上面で共有するSprite入力を返します。</summary>
        public Sprite[] GroundTopSprites => groundTopSprites;

        /// <summary>Ground崖面とWall前面で共有するFace入力を返します。</summary>
        public Sprite[] FaceSprites => faceSprites;

        /// <summary>断面1段目へ任意に重ねるFaceBoundaryOverlay入力を返します。</summary>
        public Sprite[] FaceBoundaryOverlays => faceBoundaryOverlays;

        /// <summary>Wall上面とWall用境界Overlayを機械的に暗くする場合はtrueを返します。</summary>
        public bool DarkenWallSurface => darkenWallSurface;

        /// <summary>暗色化を有効にしたときのsRGB RGB乗算係数を返します。</summary>
        public float WallDarkenFactor => wallDarkenFactor;

        /// <summary>直前に正常完了した生成で記録した入力Fingerprintを返します。</summary>
        public string LastGeneratedFingerprint => lastGeneratedFingerprint;

        /// <summary>直前の正常な生成で、この材質が所有したAssetパス一覧を返します。</summary>
        public IReadOnlyList<string> LastGeneratedAssetPaths => lastGeneratedAssetPaths;

        /// <summary>
        /// 全出力のImport完了後に、要再生成判定と古い出力整理に使う記録を更新します。
        /// </summary>
        /// <param name="fingerprint">境界合成へ使用したFace・境界・暗化設定のFingerprintです。</param>
        /// <param name="generatedAssetPaths">今回この材質が生成したプロジェクト相対Assetパスです。</param>
        public void RecordSuccessfulGeneration(string fingerprint, string[] generatedAssetPaths)
        {
            lastGeneratedFingerprint = fingerprint ?? string.Empty;
            lastGeneratedAssetPaths = generatedAssetPaths ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// 生成先と材質別入力を保持する地形タイル生成用設定アセットです。
    /// ProjectビューのCreateメニューから作成し、専用Inspectorの検証・生成ボタンで使用します。
    /// </summary>
    [CreateAssetMenu(
        fileName = "TerrainTileArtGenerationSettings",
        menuName = "Farmware/Terrain Tile Art Generation Settings")]
    public sealed class TerrainTileArtGenerationSettings : ScriptableObject
    {
        /// <summary>すべての入力・出力Spriteが使用する一辺の固定ピクセル数です。</summary>
        public const int TileSizeInPixels = 16;

        // 生成PNGを保存するAssets配下のルートです。材質IDごとの子フォルダーはツールが作ります。
        [Tooltip("境界合成Spriteを保存するAssets配下のフォルダーです。材質IDごとの子フォルダーは生成ツールが作成します。")]
        [SerializeField] private string outputRootFolder =
            "Assets/Farmware/Art/Generated/TerrainTiles";

        // 最終的に41材質を登録する可変長配列です。代表材質から段階的に設定できます。
        [Tooltip("材質別の手描き入力と壁暗化設定です。代表材質から追加し、最終的に41材質を管理します。")]
        [SerializeField] private TerrainMaterialArtSource[] materials =
            Array.Empty<TerrainMaterialArtSource>();

        /// <summary>生成PNGを保存するAssets配下のルートフォルダーを返します。</summary>
        public string OutputRootFolder => outputRootFolder;

        /// <summary>材質別の手描き入力と表示設定を登録順で返します。</summary>
        public TerrainMaterialArtSource[] Materials => materials;
    }
}
