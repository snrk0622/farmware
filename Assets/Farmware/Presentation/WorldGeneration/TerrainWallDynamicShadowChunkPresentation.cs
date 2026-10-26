// このファイルは、1論理チャンクのWall動的影をサブチャンク単位で更新し、横連続区間ごとのShadow Caster 2Dを再利用します。
// ChunkTilemapPresenterが寿命、カリング、Floating Origin位置を管理し、光源そのものは参照しません。

using System.Collections.Generic;
using Farmware.Simulation;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// 1チャンク分のComposite Shadow Caster 2Dと16個のDirty更新単位を所有します。
    /// 表示対象外では非表示にして再利用し、セルごとの生成・破棄を毎フレーム行いません。
    /// </summary>
    internal sealed class TerrainWallDynamicShadowChunkPresentation
    {
        /// <summary>チャンク全体のCasterをまとめ、表示原点変更時に一括移動するルートです。</summary>
        public GameObject Root { get; }

        // 各サブチャンクの横連続区間を再利用します。List自体はこの表示の寿命中保持します。
        private readonly List<GameObject>[] casterObjectsBySubChunk;

        // 新しい区間が必要になったときだけ使う、透明な1×1 Unity Unitの共有Spriteです。
        private readonly Sprite unitShadowSprite;

        // 生成するGameObjectへ設定するUnity Layerです。光のCulling Maskと一致させます。
        private readonly int unityLayer;

        /// <summary>
        /// 空のCompositeルートを作り、実際のCasterはDirtyサブチャンクが初めて非空になったときだけ追加します。
        /// </summary>
        /// <param name="parent">地形Gridと同じ変換を共有する親Transformです。</param>
        /// <param name="shadowSprite">透明Rendererの矩形輪郭に使う共有Spriteです。</param>
        /// <param name="layer">検証用および将来の製品用Light 2Dが照らすUnity Layerです。</param>
        public TerrainWallDynamicShadowChunkPresentation(
            Transform parent,
            Sprite shadowSprite,
            int layer)
        {
            unitShadowSprite = shadowSprite;
            unityLayer = layer;
            casterObjectsBySubChunk = new List<GameObject>[WorldGridConstants.SubChunksPerChunk];

            Root = new GameObject("Terrain Wall Dynamic Shadows", typeof(CompositeShadowCaster2D));
            Root.hideFlags = HideFlags.DontSave;
            Root.layer = unityLayer;
            Root.transform.SetParent(parent, false);
        }

        /// <summary>
        /// 1サブチャンクの連続Wall区間へCasterを割り当て、余った既存Casterは非表示にします。
        /// </summary>
        /// <param name="subChunkIndex">行優先の0〜15サブチャンク番号です。</param>
        /// <param name="runs">論理チャンク内座標で表した横連続Wall区間です。</param>
        public void UpdateSubChunk(
            int subChunkIndex,
            IReadOnlyList<TerrainWallShadowRun> runs)
        {
            if (subChunkIndex < 0 || subChunkIndex >= WorldGridConstants.SubChunksPerChunk)
            {
                throw new System.ArgumentOutOfRangeException(nameof(subChunkIndex));
            }

            List<GameObject> casterObjects = casterObjectsBySubChunk[subChunkIndex];
            if (casterObjects == null)
            {
                casterObjects = new List<GameObject>();
                casterObjectsBySubChunk[subChunkIndex] = casterObjects;
            }

            for (int runIndex = 0; runIndex < runs.Count; runIndex++)
            {
                GameObject casterObject = runIndex < casterObjects.Count
                    ? casterObjects[runIndex]
                    : CreateCasterObject(subChunkIndex, casterObjects.Count, casterObjects);
                TerrainWallShadowRun run = runs[runIndex];

                // Spriteは中央Pivotの1×1 Unitなので、区間中央へ置きX Scaleだけをセル数へ広げます。
                // YはWall前面2段分だけ手前へ移し、影がWall上面ではなく最下段Face根元から伸びるようにします。
                casterObject.transform.localPosition = new Vector3(
                    run.StartX + (run.Length * 0.5f),
                    run.Y + TerrainWallShadowUtility.DynamicShadowCasterOffsetYInTiles + 0.5f,
                    0f);
                casterObject.transform.localScale = new Vector3(run.Length, 1f, 1f);
                casterObject.SetActive(true);
            }

            for (int runIndex = runs.Count; runIndex < casterObjects.Count; runIndex++)
            {
                casterObjects[runIndex].SetActive(false);
            }

            Root.SetActive(true);
        }

        /// <summary>指定サブチャンクのCasterを非表示にし、次のDirty反映で再利用できるよう保持します。</summary>
        /// <param name="subChunkIndex">行優先の0〜15サブチャンク番号です。</param>
        public void ClearSubChunk(int subChunkIndex)
        {
            if (subChunkIndex < 0 || subChunkIndex >= WorldGridConstants.SubChunksPerChunk)
            {
                throw new System.ArgumentOutOfRangeException(nameof(subChunkIndex));
            }

            List<GameObject> casterObjects = casterObjectsBySubChunk[subChunkIndex];
            if (casterObjects == null)
            {
                return;
            }

            for (int index = 0; index < casterObjects.Count; index++)
            {
                casterObjects[index].SetActive(false);
            }
        }

        /// <summary>チャンク表示を非表示にし、全サブチャンクのCasterを再貸し出し可能な空状態へ戻します。</summary>
        public void Clear()
        {
            Root.SetActive(false);
            for (int subChunkIndex = 0;
                 subChunkIndex < casterObjectsBySubChunk.Length;
                 subChunkIndex++)
            {
                ClearSubChunk(subChunkIndex);
            }
        }

        /// <summary>Floating Originから求めたチャンク左下へルートだけを移動し、Caster形状を再生成しません。</summary>
        /// <param name="localPosition">地形Grid親を基準とするチャンク左下位置です。</param>
        public void Reposition(Vector3 localPosition)
        {
            Root.transform.localPosition = localPosition;
        }

        /// <summary>所有するComposite、サブチャンクルート、Casterをまとめて破棄します。共有Spriteは破棄しません。</summary>
        public void Destroy()
        {
            Root.SetActive(false);
            if (Application.isPlaying)
            {
                Object.Destroy(Root);
            }
            else
            {
                Object.DestroyImmediate(Root);
            }
        }

        /// <summary>
        /// 透明SpriteRendererから公開APIだけで矩形Shadow Casterを作り、以後はTransform変更で区間長へ追従させます。
        /// </summary>
        /// <param name="subChunkIndex">Hierarchy名へ含めるサブチャンク番号です。</param>
        /// <param name="runIndex">Hierarchy名へ含める再利用スロット番号です。</param>
        /// <param name="owner">作成したObjectを保持するListです。</param>
        /// <returns>非表示状態で初期化したCaster GameObjectです。</returns>
        private GameObject CreateCasterObject(
            int subChunkIndex,
            int runIndex,
            List<GameObject> owner)
        {
            var casterObject = new GameObject(
                $"Wall Shadow Run {subChunkIndex:00}-{runIndex:00}");
            casterObject.SetActive(false);
            casterObject.hideFlags = HideFlags.DontSave;
            casterObject.layer = unityLayer;
            casterObject.transform.SetParent(Root.transform, false);

            // ShadowCaster2Dは公開Runtime APIでSpriteRendererの矩形Boundsを初期輪郭として取得します。
            // Renderer自体は完全透明にし、Collider2Dを作らずPresentationと物理判定を分離します。
            SpriteRenderer renderer = casterObject.AddComponent<SpriteRenderer>();
            renderer.sprite = unitShadowSprite;
            renderer.color = Color.clear;
            renderer.enabled = true;

            ShadowCaster2D caster = casterObject.AddComponent<ShadowCaster2D>();
            caster.castingOption = ShadowCaster2D.ShadowCastingOptions.CastShadow;
            caster.trimEdge = 0f;

            owner.Add(casterObject);
            return casterObject;
        }
    }
}
