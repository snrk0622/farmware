// このファイルは、淡水・海水・マグマの製品用Surface Tile、水際色、深度色をまとめます。
// 素材生成設定とは分離し、ChunkTilemapPresenterがLiquid Runtime IDから完成Assetだけを引く境界にします。

using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 1液体種の製品表示Assetと調整色を保持します。
    /// Inspector、Editor公開ツール、製品Presenterが同じ色設定を共有するために使用します。
    /// </summary>
    [Serializable]
    public sealed class TerrainLiquidPresentationDefinition
    {
        /// <summary>淡水、海水、マグマのいずれかを表す固定表示種別です。</summary>
        [SerializeField]
        [Tooltip("この行が表す液体種です。Noneは製品カタログへ登録できません。")]
        private TerrainLiquidVisualKind kind;

        /// <summary>上端3pxクリップ用Materialで描く不透明または半透明の基礎Tileです。</summary>
        [SerializeField]
        [Tooltip("論理セルを動かさず岸側セルだけ上端3pxを隠す、16×16 Sprite付きの静止Tileです。Colliderは付けません。")]
        private Tile surfaceTile;

        /// <summary>水際Shaderへ渡すRGB色です。形状や再生時間は保持しません。</summary>
        [SerializeField]
        [Tooltip("水際の基準RGB色です。接触形状は有効地形、濃度と明暗の周期はLiquid Shoreline Materialが決めます。")]
        private Color shorelineColor = Color.white;

        /// <summary>共有水際Tileへ渡すRGB色です。Alphaは形状データに使うため常に1を返します。</summary>
        public Color ShorelineColor => new Color(shorelineColor.r, shorelineColor.g, shorelineColor.b, 1f);

        /// <summary>淡水・海水の深度Overlayへ乗算するRGB色です。マグマでは使用しません。</summary>
        [SerializeField]
        [Tooltip("淡水・海水の深度Overlay色です。Alphaは45px/93pxの連続勾配から計算するため、ここではRGBだけを使用します。")]
        private Color depthOverlayColor = new Color(0.18f, 0.55f, 0.72f, 1f);

        /// <summary>この行が表す固定液体種です。</summary>
        public TerrainLiquidVisualKind Kind => kind;

        /// <summary>上端3pxクリップを掛ける液面の基礎Tileです。</summary>
        public Tile SurfaceTile => surfaceTile;

        /// <summary>淡水・海水の深度Overlayへ使う不透明なRGB色です。</summary>
        public Color DepthOverlayColor => new Color(
            depthOverlayColor.r,
            depthOverlayColor.g,
            depthOverlayColor.b,
            1f);

        /// <summary>Inspectorで空行を追加できるよう、未設定の定義を作ります。</summary>
        public TerrainLiquidPresentationDefinition()
        {
        }

        /// <summary>テストまたはEditor公開処理から、Inspectorと同じ1液体設定を作ります。</summary>
        /// <param name="kind">淡水、海水、マグマのいずれかです。</param>
        /// <param name="surfaceTile">16×16のSpriteを持つ静止した基礎液面Tileです。</param>
        /// <param name="shorelineColor">接触線の基準RGB色です。</param>
        /// <param name="depthOverlayColor">淡水・海水の深度Overlay RGBです。</param>
        public TerrainLiquidPresentationDefinition(
            TerrainLiquidVisualKind kind,
            Tile surfaceTile,
            Color shorelineColor,
            Color depthOverlayColor)
        {
            this.kind = kind;
            this.surfaceTile = surfaceTile;
            this.shorelineColor = shorelineColor;
            this.depthOverlayColor = depthOverlayColor;
        }

        /// <summary>液体種、基礎Tile、水際色、深度色が製品表示に使えるか検証します。</summary>
        /// <param name="error">失敗時にInspectorで修正できる日本語理由を受け取ります。</param>
        /// <returns>製品表示へ必要な参照と色が有効な場合はtrueです。</returns>
        public bool TryValidate(out string error)
        {
            if (kind == TerrainLiquidVisualKind.None)
            {
                error = "液体表示種別Noneは製品カタログへ登録できません。";
                return false;
            }

            if (surfaceTile == null)
            {
                error = $"{kind} のSurface Tileが未設定です。";
                return false;
            }

            if (surfaceTile.sprite == null)
            {
                error = $"{kind} のSurface Tileに16×16 Spriteがありません。";
                return false;
            }

            if (!Mathf.Approximately(
                    surfaceTile.sprite.rect.width,
                    TerrainLiquidPresentationUtility.PixelsPerCell)
                || !Mathf.Approximately(
                    surfaceTile.sprite.rect.height,
                    TerrainLiquidPresentationUtility.PixelsPerCell)
                || !Mathf.Approximately(
                    surfaceTile.sprite.pixelsPerUnit,
                    TerrainLiquidPresentationUtility.PixelsPerCell))
            {
                error = $"{kind} のSurface Spriteは16×16 px・16 PPUで作成してください。";
                return false;
            }

            if (!Mathf.Approximately(surfaceTile.color.a, 1f))
            {
                error = $"{kind} のSurface Tile Color Alphaは1にしてください。透明度はSprite画素と製品Shaderで管理します。";
                return false;
            }

            if (!IsValidShorelineChannel(shorelineColor.r)
                || !IsValidShorelineChannel(shorelineColor.g)
                || !IsValidShorelineChannel(shorelineColor.b))
            {
                error = $"{kind} の水際色へ0〜1の有限なRGB値を設定してください。";
                return false;
            }

            if (float.IsNaN(depthOverlayColor.r)
                || float.IsNaN(depthOverlayColor.g)
                || float.IsNaN(depthOverlayColor.b)
                || float.IsInfinity(depthOverlayColor.r)
                || float.IsInfinity(depthOverlayColor.g)
                || float.IsInfinity(depthOverlayColor.b))
            {
                error = $"{kind} の深度Overlay色に有限のRGB値を設定してください。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>水際色を8bit Tile Tintへ変換する前に、範囲外やNaNを拒否します。</summary>
        /// <param name="value">確認するRGB成分です。</param>
        /// <returns>0〜1の有限値ならtrueです。</returns>
        private static bool IsValidShorelineChannel(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 1f;
        }
    }

    /// <summary>
    /// 淡水・海水・マグマを各1件登録し、固定種別で検索できる製品Liquid表示カタログです。
    /// Sceneは個別の形状Assetではなく本Assetを1件だけ参照します。
    /// </summary>
    public sealed class TerrainLiquidPresentationCatalog : ScriptableObject
    {
        /// <summary>静止形状とShader明暗へ切り替えた製品カタログの形式番号です。</summary>
        public const int CurrentFormatVersion = 2;

        /// <summary>Editor公開時に保存する形式番号です。旧AnimatedTile配列を新しい意味へ読み替えません。</summary>
        [SerializeField]
        [Tooltip("製品公開ツールが設定する形式番号です。旧形式は再公開が必要です。手動では変更しません。")]
        private int formatVersion;

        /// <summary>登録順を問わない淡水・海水・マグマ3件の製品表示設定です。</summary>
        [SerializeField]
        [Tooltip("淡水・海水・マグマを各1件登録します。欠落・重複・無効な色があると製品表示を開始しません。")]
        private TerrainLiquidPresentationDefinition[] definitions =
            Array.Empty<TerrainLiquidPresentationDefinition>();

        /// <summary>検証成功後だけ保持する、表示種別値で引けるRuntime検索配列です。</summary>
        [NonSerialized]
        private TerrainLiquidPresentationDefinition[] definitionsByKind;

        /// <summary>Inspectorへ登録されている行数です。完全なカタログでは3です。</summary>
        public int DefinitionCount => definitions?.Length ?? 0;

        /// <summary>テストまたはEditor公開処理から、Inspectorと同じ3行を設定します。</summary>
        /// <param name="definitions">淡水・海水・マグマを各1件含む配列です。</param>
        internal void SetDefinitions(TerrainLiquidPresentationDefinition[] definitions)
        {
            formatVersion = CurrentFormatVersion;
            this.definitions = definitions == null
                ? Array.Empty<TerrainLiquidPresentationDefinition>()
                : (TerrainLiquidPresentationDefinition[])definitions.Clone();
            definitionsByKind = null;
        }

        /// <summary>形式番号と3液体の参照・色を検証し、種別検索表を一度だけ構築します。</summary>
        /// <param name="error">失敗時に不足または重複の具体的な理由を受け取ります。</param>
        /// <returns>全3液体をフォールバックなしで検索できる場合はtrueです。</returns>
        public bool TryInitialize(out string error)
        {
            if (formatVersion != CurrentFormatVersion)
            {
                error = "Liquid表示カタログの形式が異なります。Publish Liquid Presentation Assetsで再公開してください。";
                return false;
            }

            if (definitionsByKind != null)
            {
                error = string.Empty;
                return true;
            }

            if (definitions == null || definitions.Length != 3)
            {
                error = $"Liquid表示カタログには淡水・海水・マグマの3件が必要ですが、現在は{DefinitionCount}件です。";
                return false;
            }

            var builtLookup = new TerrainLiquidPresentationDefinition[4];
            for (int index = 0; index < definitions.Length; index++)
            {
                TerrainLiquidPresentationDefinition definition = definitions[index];
                if (definition == null)
                {
                    error = $"Liquid表示カタログの行{index}が未設定です。";
                    return false;
                }

                if (!definition.TryValidate(out error))
                {
                    return false;
                }

                int kindIndex = (int)definition.Kind;
                if (kindIndex <= 0
                    || kindIndex >= builtLookup.Length
                    || builtLookup[kindIndex] != null)
                {
                    error = $"Liquid表示カタログで種別 {definition.Kind} が重複または範囲外です。";
                    return false;
                }

                builtLookup[kindIndex] = definition;
            }

            for (int kindIndex = 1; kindIndex < builtLookup.Length; kindIndex++)
            {
                if (builtLookup[kindIndex] == null)
                {
                    error = $"Liquid表示カタログに {(TerrainLiquidVisualKind)kindIndex} がありません。";
                    return false;
                }
            }

            definitionsByKind = builtLookup;
            error = string.Empty;
            return true;
        }

        /// <summary>液体表示種別から検証済みの製品定義を取得します。</summary>
        /// <param name="kind">淡水、海水、マグマのいずれかです。</param>
        /// <param name="definition">成功時に該当する定義を受け取ります。</param>
        /// <returns>カタログ全体が有効で、kindが3液体のいずれかならtrueです。</returns>
        public bool TryGetDefinition(
            TerrainLiquidVisualKind kind,
            out TerrainLiquidPresentationDefinition definition)
        {
            definition = null;
            int index = (int)kind;
            if (index <= 0
                || index >= 4
                || !TryInitialize(out _))
            {
                return false;
            }

            definition = definitionsByKind[index];
            return true;
        }

        /// <summary>Asset読み込み直後に以前の検索表を破棄し、保存値を再検証します。</summary>
        private void OnEnable()
        {
            definitionsByKind = null;
        }

#if UNITY_EDITOR
        /// <summary>Inspector変更時に検索表を破棄し、次のPlay開始で変更後の値を検証します。</summary>
        private void OnValidate()
        {
            definitionsByKind = null;
        }
#endif
    }
}
