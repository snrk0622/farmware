// [UNITY-SKILL:SPRITEATLAS]
// [TYPE:PREBUILD]
// [VERSION:2.0.4]
// このファイルは、製品41材質のSprite Atlas V2をPlayerビルド前に更新・検証します。
// TerrainMaterialProductPublisherを唯一の生成元として使い、置換済み素材をビルドで再生成しません。

using System;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// Playerビルド開始前に製品地形Atlasを再生成し、設定漏れや複数ページ化をビルドへ持ち込まないためのEditor専用処理です。
    /// </summary>
    public sealed class WorldGenerationSpriteAtlasPrebuildGenerator : IPreprocessBuildWithReport
    {
        /// <summary>
        /// 他の前処理より特別に先行・後続させる要件がないため、Unity標準の順序0を使用します。
        /// </summary>
        public int callbackOrder => 0;

        /// <summary>
        /// Playerビルド直前にAtlasを現在の元Spriteから再構築し、対象BuildTarget向けのパック結果を検証します。
        /// </summary>
        /// <param name="report">Unityが開始しようとしているビルドと対象Platformの情報です。</param>
        /// <remarks>AtlasアセットとSprite Packer設定を更新し、不正な場合は例外でビルドを中止します。</remarks>
        public void OnPreprocessBuild(BuildReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            // 製品表示は41材質の公開Atlasを正とするため、ビルド直前にも制作設定と完成Assetの一致を検証します。
            TerrainMaterialProductPublisher.PublishAll();
        }
    }

}
