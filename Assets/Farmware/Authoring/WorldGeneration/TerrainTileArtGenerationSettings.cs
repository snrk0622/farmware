// このファイルは、地形材質Sprite生成に必要な手描き入力と調整値を保持します。
// Runtimeの地形状態は保持せず、Editor生成ツールが完成Spriteを作るためのAuthoringアセットとしてだけ使用します。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Farmware.WorldGeneration.Authoring
{
    /// <summary>
    /// 1種類のWallTop外周を作る5枚の手描き部品を保持します。
    /// 右辺と右側の角は生成ツールが左側部品を水平反転して補います。
    /// </summary>
    [Serializable]
    public sealed class TerrainWallTopRimInput
    {
        // 上側の外周幅内へ貼る16×16 px透明Spriteです。
        [Tooltip("WallTopの上辺部品です。16×16 px透明Spriteの上辺幅内へ通常色で描いてください。")]
        [SerializeField] private Sprite topEdge;

        // 手前側の外周幅内へ貼る16×16 px透明Spriteです。
        [Tooltip("WallTopの下辺部品です。16×16 px透明Spriteの下辺幅内へ通常色で描いてください。")]
        [SerializeField] private Sprite bottomEdge;

        // 左外周へ貼る部品であり、右辺の生成元にもなります。
        [Tooltip("WallTopの左辺部品です。右辺はこのSpriteを水平反転して作るため、左右非対称の光や模様を入れないでください。")]
        [SerializeField] private Sprite leftEdge;

        // 上辺と左辺が同時に露出する場合の角部品であり、右上角の生成元にもなります。
        [Tooltip("WallTopの左上角部品です。右上角は水平反転で作るため、左右非対称の光や模様を入れないでください。")]
        [SerializeField] private Sprite topLeftCorner;

        // 下辺と左辺が同時に露出する場合の角部品であり、右下角の生成元にもなります。
        [Tooltip("WallTopの左下角部品です。右下角は水平反転で作るため、左右非対称の光や模様を入れないでください。")]
        [SerializeField] private Sprite bottomLeftCorner;

        /// <summary>上辺の手描き部品を返します。</summary>
        public Sprite TopEdge => topEdge;

        /// <summary>下辺の手描き部品を返します。</summary>
        public Sprite BottomEdge => bottomEdge;

        /// <summary>左辺と、水平反転して作る右辺の手描き元を返します。</summary>
        public Sprite LeftEdge => leftEdge;

        /// <summary>左上角と、水平反転して作る右上角の手描き元を返します。</summary>
        public Sprite TopLeftCorner => topLeftCorner;

        /// <summary>左下角と、水平反転して作る右下角の手描き元を返します。</summary>
        public Sprite BottomLeftCorner => bottomLeftCorner;

    }

    /// <summary>
    /// 1材質分の手描き入力、WallTop生成規則、直前の生成記録を保持します。
    /// 同じ材質のGroundTopとWallTopは別入力ですが、FaceはGround崖面とWall前面で共有します。
    /// </summary>
    [Serializable]
    public sealed class TerrainMaterialArtSource
    {
        /// <summary>WallTop上辺幅の初期値です。単位は元解像度のピクセルです。</summary>
        public const int DefaultTopRimWidth = 3;

        /// <summary>WallTop左右辺幅の初期値です。単位は元解像度のピクセルです。</summary>
        public const int DefaultSideRimWidth = 4;

        /// <summary>WallTop下辺幅の初期値です。単位は元解像度のピクセルです。</summary>
        public const int DefaultBottomRimWidth = 5;

        /// <summary>WallTop外周とWall用境界Overlayを初期状態で暗くするかを表します。</summary>
        public const bool DefaultDarkenWallSurface = true;

        /// <summary>WallTop外周とWall用境界Overlayへ初期状態で掛けるRGB係数です。</summary>
        public const float DefaultWallDarkenFactor = 0.85f;

        // 出力フォルダー名と将来の材質Definition対応に使う、英数字中心の安定IDです。
        [Tooltip("材質の安定IDです。出力ファイル名にも使うため、英数字・ハイフン・アンダースコアだけで一意にしてください。")]
        [SerializeField] private string materialId;

        // アート制作とInspector確認をしやすくする補助表示名であり、ゲーム内ローカライズ名ではありません。
        [Tooltip("Inspectorと制作記録で使う日本語名です。ゲーム内表示名や安定IDには使用しません。")]
        [SerializeField] private string japaneseName;

        // Ground上面へ直接使用する可変長の完成Sprite入力です。Runtimeでは全要素を均等に選びます。
        [Tooltip("Ground上面へ使う16×16 px Sprite差分です。1件以上登録し、全要素を均等確率で使用します。")]
        [SerializeField] private Sprite[] groundTopSprites = Array.Empty<Sprite>();

        // Ground崖面とWall前面の全段で共有する可変長の通常断面です。Runtimeでは全要素を均等に選びます。
        [Tooltip("Ground崖面とWall前面で共用する16×16 px Face差分です。1件以上登録し、全要素を均等確率で使用します。")]
        [SerializeField] private Sprite[] faceSprites = Array.Empty<Sprite>();

        // 断面1段目へ追加する任意Overlayです。空配列なら境界なしとしてFaceをそのまま使用し、登録時は均等に選びます。
        [Tooltip("断面1段目へ重ねる透明な16×16 px Overlay差分です。全要素を均等確率で使用し、境界不要の材質は空配列にしてください。")]
        [SerializeField] private Sprite[] faceBoundaryOverlays = Array.Empty<Sprite>();

        // WallTopの16接続Spriteを作る5部品一式です。幅は材質単位で共有します。
        [Tooltip("WallTop外周の5部品セットです。1件以上登録し、右側部品は登録せず左側から生成します。")]
        [SerializeField] private TerrainWallTopRimInput[] wallTopRims =
            Array.Empty<TerrainWallTopRimInput>();

        // 斜め見下ろしで奥側に見える上辺の幅です。単位は元解像度のピクセルです。
        [Tooltip("WallTopの上辺幅です。単位はpxで、既定値3です。変更すると全外周差分の中央高さも変わります。")]
        [Min(1)]
        [SerializeField] private int topRimWidth = DefaultTopRimWidth;

        // 左右で共通する外周幅です。右側は左部品の反転なので別値を持ちません。
        [Tooltip("WallTopの左右共通の外周幅です。単位はpxで、既定値4です。変更すると全外周差分の中央幅も変わります。")]
        [Min(1)]
        [SerializeField] private int sideRimWidth = DefaultSideRimWidth;

        // 斜め見下ろしで手前側に見える下辺の幅です。単位は元解像度のピクセルです。
        [Tooltip("WallTopの下辺幅です。単位はpxで、既定値5です。変更すると全外周差分の中央高さも変わります。")]
        [Min(1)]
        [SerializeField] private int bottomRimWidth = DefaultBottomRimWidth;

        // trueならWallTop外周とWall用FaceBoundaryOverlayだけを同じ係数で暗くします。
        [Tooltip("有効ならWallTop外周とWall用FaceBoundaryOverlayだけを機械的に暗くします。通常材質は有効を基本とします。")]
        [SerializeField] private bool darkenWallSurface = DefaultDarkenWallSurface;

        // sRGB 8bit RGBへ乗算する係数です。Face本体と共通暗色内部には適用しません。
        [Tooltip("WallTop外周とWall用境界OverlayのRGBへ乗算する係数です。0より大きく1以下、初期比較値は0.85です。")]
        [Range(0.01f, 1f)]
        [SerializeField] private float wallDarkenFactor = DefaultWallDarkenFactor;

        // 最後に生成した入力Fingerprintです。現在値と異なる場合だけInspectorへ要再生成と表示します。
        [Tooltip("Editor生成ツールが管理する最終生成Fingerprintです。手入力せず、要再生成判定に使用します。")]
        [HideInInspector]
        [SerializeField] private string lastGeneratedFingerprint;

        // 前回この材質から生成したAssetだけを記録し、差分数を減らした再生成時に古い出力だけを安全に削除します。
        [Tooltip("Editor生成ツールが管理する生成Assetパスです。手入力せず、同じ材質の古い出力だけを整理するために使用します。")]
        [HideInInspector]
        [SerializeField] private string[] lastGeneratedAssetPaths = Array.Empty<string>();

        /// <summary>英語の安定材質IDを返します。</summary>
        public string MaterialId => materialId;

        /// <summary>制作確認用の日本語名を返します。</summary>
        public string JapaneseName => japaneseName;

        /// <summary>GroundTopの可変長Sprite入力を返します。Runtimeでは全要素を均等に選びます。</summary>
        public Sprite[] GroundTopSprites => groundTopSprites;

        /// <summary>Ground崖面とWall前面で共有するFace入力を返します。</summary>
        public Sprite[] FaceSprites => faceSprites;

        /// <summary>断面1段目へ任意に重ねるFaceBoundaryOverlay入力を返します。</summary>
        public Sprite[] FaceBoundaryOverlays => faceBoundaryOverlays;

        /// <summary>WallTopの5部品から成る外周セット入力を返します。</summary>
        public TerrainWallTopRimInput[] WallTopRims => wallTopRims;

        /// <summary>WallTop上辺の幅を元解像度ピクセル単位で返します。</summary>
        public int TopRimWidth => topRimWidth;

        /// <summary>WallTop左右辺の共通幅を元解像度ピクセル単位で返します。</summary>
        public int SideRimWidth => sideRimWidth;

        /// <summary>WallTop下辺の幅を元解像度ピクセル単位で返します。</summary>
        public int BottomRimWidth => bottomRimWidth;

        /// <summary>WallTop外周とWall用境界Overlayを機械的に暗くする場合はtrueを返します。</summary>
        public bool DarkenWallSurface => darkenWallSurface;

        /// <summary>暗色化を有効にしたときのsRGB RGB乗算係数を返します。</summary>
        public float WallDarkenFactor => wallDarkenFactor;

        /// <summary>直前に正常完了した生成で記録した入力Fingerprintを返します。</summary>
        public string LastGeneratedFingerprint => lastGeneratedFingerprint;

        /// <summary>直前の正常な生成で、この材質が所有したAssetパス一覧を返します。</summary>
        public IReadOnlyList<string> LastGeneratedAssetPaths => lastGeneratedAssetPaths;

        /// <summary>
        /// 生成が全出力のImportまで完了した後に、要再生成判定と古い出力整理に使う記録を更新します。
        /// </summary>
        /// <param name="fingerprint">生成へ使用した全入力と調整値のFingerprintです。</param>
        /// <param name="generatedAssetPaths">今回この材質が生成したプロジェクト相対Assetパスです。</param>
        public void RecordSuccessfulGeneration(string fingerprint, string[] generatedAssetPaths)
        {
            lastGeneratedFingerprint = fingerprint ?? string.Empty;
            lastGeneratedAssetPaths = generatedAssetPaths ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// 全材質で共有するWall内部色、生成先、材質別入力を保持する地形タイル生成用設定アセットです。
    /// ProjectビューのCreateメニューから作成し、専用Inspectorの検証・生成ボタンで使用します。
    /// </summary>
    [CreateAssetMenu(
        fileName = "TerrainTileArtGenerationSettings",
        menuName = "Farmware/Terrain Tile Art Generation Settings")]
    public sealed class TerrainTileArtGenerationSettings : ScriptableObject
    {
        /// <summary>すべての入力・出力Spriteが使用する一辺の固定ピクセル数です。</summary>
        public const int TileSizeInPixels = 16;

        // 全材質のWallTop中央へ使う共通色です。ColorUsageでAlphaを隠し、OnValidateでも不透明へ固定します。
        [Tooltip("全材質のWallTop中央を埋める共通暗色です。Alphaは常に100%で、変更すると全材質が要再生成になります。")]
        [ColorUsage(false, false)]
        [SerializeField] private Color wallInteriorDark = new Color32(13, 16, 22, 255);

        // 生成PNGを保存するAssets配下のルートです。材質IDごとの子フォルダーはツールが作ります。
        [Tooltip("完成Spriteを保存するAssets配下のフォルダーです。材質IDごとの子フォルダーは生成ツールが作成します。")]
        [SerializeField] private string outputRootFolder =
            "Assets/Farmware/Art/Generated/TerrainTiles";

        // 41材質を最終的に登録する可変長配列です。まず代表材質から順に設定できます。
        [Tooltip("材質別の手描き入力と生成設定です。まず代表5材質から登録し、最終的に41材質を管理します。")]
        [SerializeField] private TerrainMaterialArtSource[] materials =
            Array.Empty<TerrainMaterialArtSource>();

        /// <summary>全材質のWallTop中央へ使う、不透明な共通暗色を8bit RGBAで返します。</summary>
        public Color32 WallInteriorDark => (Color32)wallInteriorDark;

        /// <summary>生成PNGを保存するAssets配下のルートフォルダーを返します。</summary>
        public string OutputRootFolder => outputRootFolder;

        /// <summary>材質別の手描き入力と生成設定を登録順で返します。</summary>
        public TerrainMaterialArtSource[] Materials => materials;

        /// <summary>
        /// Inspector編集またはAsset読込時にUnityから呼ばれ、共通内部色のAlphaだけを仕様どおり不透明へ固定します。
        /// 外周幅や暗色化係数などの不正値は黙って補正せず、生成前検証で理由を示して拒否します。
        /// </summary>
        private void OnValidate()
        {
            wallInteriorDark.a = 1f;
        }
    }
}
