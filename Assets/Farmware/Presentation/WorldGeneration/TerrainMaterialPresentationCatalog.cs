// このファイルは、41地形材質の製品表示に必要な上面・断面・境界合成済みSpriteを1つのカタログへまとめます。
// Editorの素材生成設定とは分離し、後続の地形PresenterがSimulationの安定材質IDから製品用Spriteだけを参照できる境界を作ります。

using System;
using System.Collections.Generic;
using Farmware.Simulation;
using UnityEngine;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 1材質分の製品表示SpriteとWall表示規則を保持します。
    /// 地形表示カタログのInspector、後続のPresenter、事前合成ツールが共通して使用します。
    /// </summary>
    [Serializable]
    public sealed class TerrainMaterialPresentationDefinition
    {
        /// <summary>保存・生成・表示で共通使用する、1〜41の変更禁止材質IDです。</summary>
        [SerializeField]
        [Tooltip("この表示設定に対応する1〜41の安定材質IDです。変更するとRuntime IDとの対応が変わります。")]
        private TerrainBlockMaterialId stableId;

        /// <summary>GroundとWall上面で共有する16×16 pxの均等選択候補です。</summary>
        [SerializeField]
        [Tooltip("GroundとWall上面で共有する16×16 px Spriteです。候補は個別Weightを持たず均等に選ばれます。")]
        private Sprite[] groundTopSprites = Array.Empty<Sprite>();

        /// <summary>Ground崖面とWall前面で共有する16×16 pxの均等選択候補です。</summary>
        [SerializeField]
        [Tooltip("Ground崖面とWall前面で共有する16×16 px Spriteです。1段目を含む全断面で使用します。")]
        private Sprite[] faceSprites = Array.Empty<Sprite>();

        /// <summary>手描き境界Overlayの候補数です。0なら境界合成を行わない材質です。</summary>
        [SerializeField]
        [Min(0)]
        [Tooltip("事前合成へ使った境界Overlayの候補数です。0は境界なし、1以上は境界ありを表します。")]
        private int faceBoundaryVariantCount;

        /// <summary>Face候補と境界候補を全組合せした、Ground崖面1段目用の完成Spriteです。</summary>
        [SerializeField]
        [Tooltip("Face×境界Overlayを元色のまま事前合成した16×16 px完成Spriteです。Runtimeでは再合成しません。")]
        private Sprite[] groundBoundaryFaceSprites = Array.Empty<Sprite>();

        /// <summary>Face候補と暗色境界候補を全組合せした、Wall前面1段目用の完成Spriteです。</summary>
        [SerializeField]
        [Tooltip("Face×暗色化済み境界Overlayを事前合成した16×16 px完成Spriteです。Wall暗色化が無効なら空にします。")]
        private Sprite[] wallBoundaryFaceSprites = Array.Empty<Sprite>();

        /// <summary>Wall上面とWall用境界Overlayへ材質共通の暗色化を適用するかを表します。</summary>
        [SerializeField]
        [Tooltip("有効にするとWall上面とWall用境界Overlayへ暗色化係数を適用します。Face本体は暗色化しません。")]
        private bool darkenWallSurface = true;

        /// <summary>Wall上面とWall用境界OverlayのsRGB各成分へ掛ける0超1以下の係数です。</summary>
        [SerializeField]
        [Range(0.01f, 1f)]
        [Tooltip("Wall上面とWall用境界Overlayへ掛ける暗色化係数です。既定値0.85で、0より大きく1以下にします。")]
        private float wallDarkenFactor = 0.85f;

        /// <summary>この表示設定に対応する変更禁止材質IDです。</summary>
        public TerrainBlockMaterialId StableId => stableId;

        /// <summary>GroundとWall上面で共有する候補数です。</summary>
        public int GroundTopSpriteCount => groundTopSprites?.Length ?? 0;

        /// <summary>Ground崖面とWall前面で共有する断面候補数です。</summary>
        public int FaceSpriteCount => faceSprites?.Length ?? 0;

        /// <summary>境界Overlayの候補数です。0なら境界なしです。</summary>
        public int FaceBoundaryVariantCount => faceBoundaryVariantCount;

        /// <summary>境界あり材質ならtrueです。</summary>
        public bool HasFaceBoundary => faceBoundaryVariantCount > 0;

        /// <summary>Wall上面とWall用境界Overlayを暗色化する設定ならtrueです。</summary>
        public bool DarkenWallSurface => darkenWallSurface;

        /// <summary>Wall上面とWall用境界Overlayへ掛ける暗色化係数です。</summary>
        public float WallDarkenFactor => wallDarkenFactor;

        /// <summary>
        /// UnityがInspectorで配列要素を追加するときに、空の1材質設定を作ります。
        /// 有効なIDとSpriteは未設定なので、公開前の完全性検証を通るまで製品表示には使用されません。
        /// </summary>
        public TerrainMaterialPresentationDefinition()
        {
        }

        /// <summary>
        /// Inspectorシリアライズと同じ値から、テストまたはEditor公開処理が1材質設定を作ります。
        /// </summary>
        /// <param name="stableId">1〜41の安定材質IDです。</param>
        /// <param name="groundTopSprites">GroundとWall上面で共有する候補です。</param>
        /// <param name="faceSprites">Ground崖面とWall前面で共有する候補です。</param>
        /// <param name="faceBoundaryVariantCount">境界Overlayの候補数です。境界なしなら0です。</param>
        /// <param name="groundBoundaryFaceSprites">元色境界を合成済みのGround用完成候補です。</param>
        /// <param name="wallBoundaryFaceSprites">暗色境界を合成済みのWall用完成候補です。</param>
        /// <param name="darkenWallSurface">Wall表示を暗色化する場合はtrueです。</param>
        /// <param name="wallDarkenFactor">暗色化するsRGB成分へ掛ける係数です。</param>
        public TerrainMaterialPresentationDefinition(
            TerrainBlockMaterialId stableId,
            Sprite[] groundTopSprites,
            Sprite[] faceSprites,
            int faceBoundaryVariantCount,
            Sprite[] groundBoundaryFaceSprites,
            Sprite[] wallBoundaryFaceSprites,
            bool darkenWallSurface,
            float wallDarkenFactor)
        {
            this.stableId = stableId;
            // 呼び出し元が後から元配列を書き換えて検証済み設定を変えないよう、初期化時に配列だけを複製します。
            this.groundTopSprites = CloneOrEmpty(groundTopSprites);
            this.faceSprites = CloneOrEmpty(faceSprites);
            this.faceBoundaryVariantCount = faceBoundaryVariantCount;
            this.groundBoundaryFaceSprites = CloneOrEmpty(groundBoundaryFaceSprites);
            this.wallBoundaryFaceSprites = CloneOrEmpty(wallBoundaryFaceSprites);
            this.darkenWallSurface = darkenWallSurface;
            this.wallDarkenFactor = wallDarkenFactor;
        }

        /// <summary>
        /// 決定論的に選択済みの添字から上面Spriteを取得します。
        /// </summary>
        /// <param name="variantIndex">0以上GroundTopSpriteCount未満の候補添字です。</param>
        /// <returns>GroundとWall上面で共有するSpriteです。</returns>
        public Sprite GetGroundTopSprite(int variantIndex)
        {
            return groundTopSprites[variantIndex];
        }

        /// <summary>
        /// 決定論的に選択済みの添字から通常断面Spriteを取得します。
        /// </summary>
        /// <param name="variantIndex">0以上FaceSpriteCount未満の候補添字です。</param>
        /// <returns>Ground崖面とWall前面で共有するSpriteです。</returns>
        public Sprite GetFaceSprite(int variantIndex)
        {
            return faceSprites[variantIndex];
        }

        /// <summary>
        /// Faceと境界の選択結果から、用途別の事前合成済みSpriteを取得します。
        /// </summary>
        /// <param name="faceVariantIndex">選択済みFace候補添字です。</param>
        /// <param name="boundaryVariantIndex">選択済み境界候補添字です。</param>
        /// <param name="forWall">Wall前面用を取得する場合はtrue、Ground崖面用ならfalseです。</param>
        /// <returns>Runtimeで追加合成せず1枚描画できる完成Spriteです。</returns>
        public Sprite GetBoundaryFaceSprite(
            int faceVariantIndex,
            int boundaryVariantIndex,
            bool forWall)
        {
            int composedIndex = TerrainMaterialSpriteVariantSelector
                .CalculateComposedBoundaryIndex(
                    faceVariantIndex,
                    boundaryVariantIndex,
                    faceBoundaryVariantCount);

            // 暗色化しない例外材質はGround用と同じ完成Spriteを使い、同一画像の重複生成を避けます。
            Sprite[] candidates = forWall && darkenWallSurface
                ? wallBoundaryFaceSprites
                : groundBoundaryFaceSprites;
            return candidates[composedIndex];
        }

        /// <summary>
        /// 1材質の必須Sprite、寸法、候補一意性、境界合成件数、暗色化係数を検証します。
        /// </summary>
        /// <param name="error">失敗時にInspectorで修正できる具体的な理由を受け取ります。</param>
        /// <returns>製品表示がフォールバックなしで全候補を選択できる場合はtrueです。</returns>
        public bool TryValidate(out string error)
        {
            int stableIndex = (int)stableId - (int)TerrainBlockMaterialDefinitions.First;
            if (stableIndex < 0 || stableIndex >= TerrainBlockMaterialDefinitions.Count)
            {
                error = $"安定材質ID {stableId} は1〜41の範囲外です。";
                return false;
            }

            if (!TryValidateSpriteCandidates(
                    groundTopSprites,
                    "GroundTop",
                    requireAtLeastOne: true,
                    out error)
                || !TryValidateSpriteCandidates(
                    faceSprites,
                    "Face",
                    requireAtLeastOne: true,
                    out error))
            {
                return false;
            }

            if (wallDarkenFactor <= 0f || wallDarkenFactor > 1f)
            {
                error = $"{stableId} のWallDarkenFactorは0より大きく1以下にしてください。";
                return false;
            }

            if (faceBoundaryVariantCount < 0)
            {
                error = $"{stableId} の境界候補数は0以上にしてください。";
                return false;
            }

            int expectedComposedCount = FaceSpriteCount * faceBoundaryVariantCount;
            if (faceBoundaryVariantCount == 0)
            {
                if ((groundBoundaryFaceSprites?.Length ?? 0) != 0
                    || (wallBoundaryFaceSprites?.Length ?? 0) != 0)
                {
                    error = $"{stableId} は境界なしなので、用途別の境界合成済みSpriteを空にしてください。";
                    return false;
                }

                error = string.Empty;
                return true;
            }

            if (!TryValidateSpriteCandidates(
                    groundBoundaryFaceSprites,
                    "Ground境界合成結果",
                    requireAtLeastOne: true,
                    out error)
                || groundBoundaryFaceSprites.Length != expectedComposedCount)
            {
                if (string.IsNullOrEmpty(error))
                {
                    error = $"{stableId} のGround境界合成結果はFace×境界の{expectedComposedCount}件必要です。";
                }

                return false;
            }

            if (darkenWallSurface)
            {
                if (!TryValidateSpriteCandidates(
                        wallBoundaryFaceSprites,
                        "Wall境界合成結果",
                        requireAtLeastOne: true,
                        out error)
                    || wallBoundaryFaceSprites.Length != expectedComposedCount)
                {
                    if (string.IsNullOrEmpty(error))
                    {
                        error = $"{stableId} のWall境界合成結果はFace×境界の{expectedComposedCount}件必要です。";
                    }

                    return false;
                }
            }
            else if ((wallBoundaryFaceSprites?.Length ?? 0) != 0)
            {
                error = $"{stableId} はWall暗色化なしなので、Wall境界合成結果を空にしてください。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 1用途の候補配列が必須件数、16×16 px、16 PPU、重複なしを満たすか確認します。
        /// </summary>
        /// <param name="sprites">検証するSprite候補です。</param>
        /// <param name="label">エラーで候補用途を特定する表示名です。</param>
        /// <param name="requireAtLeastOne">空配列を拒否する場合はtrueです。</param>
        /// <param name="error">失敗時の具体的な理由を受け取ります。</param>
        /// <returns>全候補が製品の16pxタイル契約を満たす場合はtrueです。</returns>
        private bool TryValidateSpriteCandidates(
            Sprite[] sprites,
            string label,
            bool requireAtLeastOne,
            out string error)
        {
            if (sprites == null || (requireAtLeastOne && sprites.Length == 0))
            {
                error = $"{stableId} の{label}候補を1件以上設定してください。";
                return false;
            }

            var uniqueSprites = new HashSet<Sprite>();
            for (int spriteIndex = 0; spriteIndex < sprites.Length; spriteIndex++)
            {
                Sprite sprite = sprites[spriteIndex];
                if (sprite == null)
                {
                    error = $"{stableId} の{label}[{spriteIndex}]が未設定です。";
                    return false;
                }

                if (!uniqueSprites.Add(sprite))
                {
                    error = $"{stableId} の{label}に同じSpriteが重複しています。暗黙のWeightになるため重複できません。";
                    return false;
                }

                if (!Mathf.Approximately(sprite.rect.width, 16f)
                    || !Mathf.Approximately(sprite.rect.height, 16f)
                    || !Mathf.Approximately(sprite.pixelsPerUnit, 16f))
                {
                    error = $"{stableId} の{label}[{spriteIndex}]は16×16 px・16 PPUにしてください。";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Sprite参照は共有したまま配列コンテナだけを複製し、外部からの要素差し替えを防ぎます。
        /// </summary>
        /// <param name="sprites">Editor公開処理またはテストが渡した候補配列です。</param>
        /// <returns>nullなら共有空配列、それ以外なら同じSprite参照を持つ新しい配列です。</returns>
        private static Sprite[] CloneOrEmpty(Sprite[] sprites)
        {
            return sprites == null
                ? Array.Empty<Sprite>()
                : (Sprite[])sprites.Clone();
        }
    }

    /// <summary>
    /// 41材質の製品表示設定を安定IDで検索できるScriptableObjectです。
    /// Sceneは個別Spriteではなくこのアセットを1つ参照し、材質追加漏れを起動前に検出します。
    /// </summary>
    public sealed class TerrainMaterialPresentationCatalog : ScriptableObject
    {
        /// <summary>登録順を問わない41材質分の製品表示設定です。</summary>
        [SerializeField]
        [Tooltip("41材質を各1件登録する製品表示設定です。欠落・重複・不正Spriteがあるとカタログを公開しません。")]
        private TerrainMaterialPresentationDefinition[] definitions =
            Array.Empty<TerrainMaterialPresentationDefinition>();

        /// <summary>検証成功後だけ保持する、安定ID添字のRuntime検索配列です。</summary>
        [NonSerialized]
        private TerrainMaterialPresentationDefinition[] definitionsByStableId;

        /// <summary>Inspectorへ登録されている行数です。完全な製品カタログでは41です。</summary>
        public int DefinitionCount => definitions?.Length ?? 0;

        /// <summary>
        /// テストまたはEditor公開処理が、Inspectorと同じ41材質配列を設定します。
        /// 製品Runtimeではアセットに保存済みの値を使用します。
        /// </summary>
        /// <param name="definitions">登録順を問わない41材質分の表示設定です。</param>
        internal void SetDefinitions(TerrainMaterialPresentationDefinition[] definitions)
        {
            // Editor公開処理が保持する元配列を後から並べ替えても、検証済みアセットの対応を変えません。
            this.definitions = definitions == null
                ? Array.Empty<TerrainMaterialPresentationDefinition>()
                : (TerrainMaterialPresentationDefinition[])definitions.Clone();
            definitionsByStableId = null;
        }

        /// <summary>
        /// 41材質の完全性と各行を検証し、安定ID添字の検索表を一度だけ構築します。
        /// </summary>
        /// <param name="error">失敗時に欠落、重複、またはSprite設定の理由を受け取ります。</param>
        /// <returns>全41材質をフォールバックなしで検索できる場合はtrueです。</returns>
        public bool TryInitialize(out string error)
        {
            if (definitionsByStableId != null)
            {
                error = string.Empty;
                return true;
            }

            if (definitions == null
                || definitions.Length != TerrainBlockMaterialDefinitions.Count)
            {
                error = $"表示カタログには41材質が必要ですが、現在は{DefinitionCount}件です。";
                return false;
            }

            var builtLookup =
                new TerrainMaterialPresentationDefinition[TerrainBlockMaterialDefinitions.Count];
            for (int definitionIndex = 0;
                 definitionIndex < definitions.Length;
                 definitionIndex++)
            {
                TerrainMaterialPresentationDefinition definition = definitions[definitionIndex];
                if (definition == null)
                {
                    error = $"表示カタログの行{definitionIndex}が未設定です。";
                    return false;
                }

                if (!definition.TryValidate(out error))
                {
                    return false;
                }

                int stableIndex = (int)definition.StableId
                    - (int)TerrainBlockMaterialDefinitions.First;
                if (builtLookup[stableIndex] != null)
                {
                    error = $"表示カタログで安定材質ID {definition.StableId} が重複しています。";
                    return false;
                }

                builtLookup[stableIndex] = definition;
            }

            // 件数が41でも同じIDの重複があると別IDが欠落するため、全添字を明示確認します。
            for (int stableIndex = 0; stableIndex < builtLookup.Length; stableIndex++)
            {
                if (builtLookup[stableIndex] == null)
                {
                    TerrainBlockMaterialId missingId =
                        (TerrainBlockMaterialId)(stableIndex + (int)TerrainBlockMaterialDefinitions.First);
                    error = $"表示カタログに安定材質ID {missingId} がありません。";
                    return false;
                }
            }

            definitionsByStableId = builtLookup;
            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 安定材質IDから検証済みの製品表示設定を取得します。
        /// </summary>
        /// <param name="stableId">検索する1〜41の材質IDです。</param>
        /// <param name="definition">成功時に対応する表示設定を受け取ります。</param>
        /// <returns>カタログ全体が有効で、指定IDが範囲内ならtrueです。</returns>
        public bool TryGetDefinition(
            TerrainBlockMaterialId stableId,
            out TerrainMaterialPresentationDefinition definition)
        {
            definition = null;
            int stableIndex = (int)stableId - (int)TerrainBlockMaterialDefinitions.First;
            if (stableIndex < 0
                || stableIndex >= TerrainBlockMaterialDefinitions.Count
                || !TryInitialize(out _))
            {
                return false;
            }

            definition = definitionsByStableId[stableIndex];
            return true;
        }

        /// <summary>
        /// Unityがアセットを読み込んだ直後に、以前の検索表を破棄します。
        /// シリアライズ値を正として次回検索時に再検証するため、Editor編集前の配列を使い続けません。
        /// </summary>
        private void OnEnable()
        {
            definitionsByStableId = null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Inspectorで値が変わったときに検索表を破棄します。
        /// 次のプレビューまたはPlay開始時に、変更後の41材質を必ず再検証します。
        /// </summary>
        private void OnValidate()
        {
            definitionsByStableId = null;
        }
#endif
    }
}
