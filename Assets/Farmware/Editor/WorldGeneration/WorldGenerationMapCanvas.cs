// このファイルは、広域マップTexture、チャンク境界、原点印をUI ToolkitのEditorWindow内へ描画します。
// マウスドラッグとホイール入力を座標変更要求へ変換しますが、地形生成やEditorWindowの状態管理は担当しません。

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// 生成済みの広域マップTextureを表示し、パン・ズーム・カーソル座標イベントを通知するVisualElementです。
    /// generateVisualContentで描くため、タイル数に比例するVisualElementを作らず、大きな地図でもUI階層を小さく保ちます。
    /// </summary>
    public sealed class WorldGenerationMapCanvas : VisualElement
    {
        /// <summary>現在表示する読み取り専用Textureです。所有と破棄はEditorWindowが担当します。</summary>
        private Texture2D mapTexture;

        /// <summary>表示中Textureの中央に対応する水平ワールドタイル座標です。</summary>
        private int centerTileX;

        /// <summary>表示中Textureの中央に対応する垂直ワールドタイル座標です。</summary>
        private int centerTileY;

        /// <summary>表示中Textureの1ピクセルが代表するワールドタイル幅です。</summary>
        private int tilesPerPixel = 4;

        /// <summary>チャンク・サブチャンク境界を重ねる場合はtrueです。</summary>
        private bool showGrid = true;

        /// <summary>左ドラッグ中のPointer IDです。ドラッグしていない場合は-1です。</summary>
        private int capturedPointerId = -1;

        /// <summary>左ドラッグを開始したCanvasローカル座標です。</summary>
        private Vector2 dragStartPosition;

        /// <summary>ドラッグ完了時の座標計算へ使う、一時的な画面ピクセル移動量です。</summary>
        private Vector2 dragOffset;

        /// <summary>ドラッグ完了時に、画面ピクセル単位の移動量をEditorWindowへ通知します。</summary>
        public Action<Vector2> PanCompleted { get; set; }

        /// <summary>ホイール操作時に、方向とCanvasローカル座標をEditorWindowへ通知します。</summary>
        public Action<float, Vector2> ZoomRequested { get; set; }

        /// <summary>カーソル移動時に、Canvasローカル座標をEditorWindowへ通知します。</summary>
        public Action<Vector2> PointerMoved { get; set; }

        /// <summary>
        /// 描画コールバックとPointerイベントを登録し、キーボードフォーカス可能な地図領域を作ります。
        /// </summary>
        public WorldGenerationMapCanvas()
        {
            focusable = true;
            AddToClassList("map-canvas");
            generateVisualContent += GenerateMapVisualContent;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            RegisterCallback<WheelEvent>(OnWheel);
        }

        /// <summary>
        /// 新しく生成されたTextureと、その座標条件を現在表示へ適用します。
        /// </summary>
        /// <param name="texture">EditorWindowが所有する広域マップTextureです。</param>
        /// <param name="newCenterTileX">Texture中央の水平タイル座標です。</param>
        /// <param name="newCenterTileY">Texture中央の垂直タイル座標です。</param>
        /// <param name="newTilesPerPixel">Texture 1ピクセルが表すタイル数です。</param>
        public void SetMapTexture(
            Texture2D texture,
            int newCenterTileX,
            int newCenterTileY,
            int newTilesPerPixel)
        {
            mapTexture = texture;
            centerTileX = newCenterTileX;
            centerTileY = newCenterTileY;
            tilesPerPixel = Math.Max(1, newTilesPerPixel);
            dragOffset = Vector2.zero;

            // Painter2DのfillTextureはUnity 6.3で色Textureを白く描く場合があるため、
            // UI Toolkitが正式に対応するbackground-imageとして表示し、Painter2DはOverlayだけに限定します。
            style.backgroundImage = new StyleBackground(texture);
            MarkDirtyRepaint();
        }

        /// <summary>
        /// チャンク境界の表示状態を変更し、地形Textureを作り直さずOverlayだけ再描画します。
        /// </summary>
        /// <param name="visible">境界を表示する場合はtrueです。</param>
        public void SetGridVisible(bool visible)
        {
            showGrid = visible;
            MarkDirtyRepaint();
        }

        /// <summary>
        /// Pointerを捕捉して左ドラッグを開始します。
        /// 右クリックなど他のEditor操作と競合しないよう、button 0だけを処理します。
        /// </summary>
        /// <param name="evt">UI Toolkitが通知したPointer押下イベントです。</param>
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || capturedPointerId >= 0)
            {
                return;
            }

            capturedPointerId = evt.pointerId;
            dragStartPosition = evt.localPosition;
            dragOffset = Vector2.zero;
            // CapturePointerはVisualElement向けの拡張メソッドなので、対象がこのCanvasであることを明示します。
            this.CapturePointer(evt.pointerId);
            Focus();
            evt.StopPropagation();
        }

        /// <summary>
        /// Pointer移動をカーソル情報へ通知し、ドラッグ中なら既存Textureを即座に追従表示します。
        /// </summary>
        /// <param name="evt">現在のCanvasローカルPointer位置です。</param>
        private void OnPointerMove(PointerMoveEvent evt)
        {
            PointerMoved?.Invoke(evt.localPosition);

            if (capturedPointerId != evt.pointerId || !this.HasPointerCapture(evt.pointerId))
            {
                return;
            }

            // 地形再計算はPointerUp後に1回だけ行い、ドラッグ中は最終移動量だけを記録します。
            // Textureと境界線を別々にずらすと位置関係を誤認するため、確定前の見た目は動かしません。
            // PointerEventのlocalPositionはVector3なので、2D UIで使うXYだけを明示的に取り出します。
            Vector3 currentPointerPosition = evt.localPosition;
            dragOffset = new Vector2(currentPointerPosition.x, currentPointerPosition.y) - dragStartPosition;
            evt.StopPropagation();
        }

        /// <summary>
        /// 左ドラッグを終了し、最終的な画面移動量から新しい中心を計算するようEditorWindowへ通知します。
        /// </summary>
        /// <param name="evt">Pointer解放イベントです。</param>
        private void OnPointerUp(PointerUpEvent evt)
        {
            if (capturedPointerId != evt.pointerId)
            {
                return;
            }

            Vector2 completedOffset = dragOffset;
            this.ReleasePointer(evt.pointerId);
            capturedPointerId = -1;
            dragOffset = Vector2.zero;
            MarkDirtyRepaint();

            if (completedOffset.sqrMagnitude >= 4f)
            {
                PanCompleted?.Invoke(completedOffset);
            }

            evt.StopPropagation();
        }

        /// <summary>
        /// Editorのフォーカス変更などでPointer捕捉を失った場合に、一時移動だけを解除します。
        /// 不完全なドラッグで中心座標を変更しないため、PanCompletedは通知しません。
        /// </summary>
        /// <param name="evt">Pointer捕捉解除イベントです。</param>
        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            capturedPointerId = -1;
            dragOffset = Vector2.zero;
            MarkDirtyRepaint();
        }

        /// <summary>
        /// ホイール方向とカーソル位置をEditorWindowへ渡し、カーソル中心ズームを要求します。
        /// </summary>
        /// <param name="evt">ホイール移動量を含むUI Toolkitイベントです。</param>
        private void OnWheel(WheelEvent evt)
        {
            ZoomRequested?.Invoke(evt.delta.y, evt.localMousePosition);
            evt.StopPropagation();
        }

        /// <summary>
        /// 背景、地形Texture、サブチャンク・チャンク線、原点印、画面中央印を順番に描きます。
        /// </summary>
        /// <param name="context">UI Toolkitから渡される現在フレームの描画Contextです。</param>
        private void GenerateMapVisualContent(MeshGenerationContext context)
        {
            float width = contentRect.width;
            float height = contentRect.height;
            if (width < 1f || height < 1f)
            {
                return;
            }

            if (mapTexture == null)
            {
                return;
            }

            // 地形Texture本体はSetMapTextureでbackground-imageへ設定済みです。
            // ここでは背景より手前に必要な境界線と位置マーカーだけを追加描画します。
            Painter2D painter = context.painter2D;

            if (showGrid)
            {
                DrawGrid(painter, width, height, WorldGridConstants.SubChunkSize, new Color(1f, 1f, 1f, 0.12f));
                DrawGrid(painter, width, height, WorldGridConstants.LogicalChunkSize, new Color(0.25f, 0.9f, 1f, 0.5f));
            }

            DrawWorldOrigin(painter, width, height);
            DrawCenterMarker(painter, width, height);
        }

        /// <summary>
        /// 指定グリッド寸法の境界線を、現在の中心・ズーム・一時ドラッグ位置に合わせて描きます。
        /// </summary>
        /// <param name="painter">現在フレームのPainter2Dです。</param>
        /// <param name="width">Canvasの横幅です。</param>
        /// <param name="height">Canvasの縦幅です。</param>
        /// <param name="gridSizeInTiles">線を引くタイル間隔です。</param>
        /// <param name="lineColor">境界線の色です。</param>
        private void DrawGrid(
            Painter2D painter,
            float width,
            float height,
            int gridSizeInTiles,
            Color lineColor)
        {
            float gridSpacingInPixels = gridSizeInTiles / (float)tilesPerPixel;
            if (gridSpacingInPixels < 4f)
            {
                return;
            }

            painter.strokeColor = lineColor;
            painter.lineWidth = gridSizeInTiles == WorldGridConstants.LogicalChunkSize ? 1.5f : 1f;

            double minimumWorldX = centerTileX - ((width * 0.5d) * tilesPerPixel);
            long firstWorldX = (long)Math.Floor(minimumWorldX / gridSizeInTiles) * gridSizeInTiles;
            for (long worldX = firstWorldX;
                 worldX <= centerTileX + ((width * 0.5d) * tilesPerPixel);
                 worldX += gridSizeInTiles)
            {
                float pixelX = (float)(
                    (width * 0.5d) + ((worldX - centerTileX) / (double)tilesPerPixel));
                painter.BeginPath();
                painter.MoveTo(new Vector2(pixelX, 0f));
                painter.LineTo(new Vector2(pixelX, height));
                painter.Stroke();
            }

            double minimumWorldY = centerTileY - ((height * 0.5d) * tilesPerPixel);
            long firstWorldY = (long)Math.Floor(minimumWorldY / gridSizeInTiles) * gridSizeInTiles;
            for (long worldY = firstWorldY;
                 worldY <= centerTileY + ((height * 0.5d) * tilesPerPixel);
                 worldY += gridSizeInTiles)
            {
                float pixelY = (float)(
                    (height * 0.5d) - ((worldY - centerTileY) / (double)tilesPerPixel));
                painter.BeginPath();
                painter.MoveTo(new Vector2(0f, pixelY));
                painter.LineTo(new Vector2(width, pixelY));
                painter.Stroke();
            }
        }

        /// <summary>
        /// ワールド原点が現在範囲内にある場合、赤い十字として表示します。
        /// </summary>
        /// <param name="painter">現在フレームのPainter2Dです。</param>
        /// <param name="width">Canvasの横幅です。</param>
        /// <param name="height">Canvasの縦幅です。</param>
        private void DrawWorldOrigin(Painter2D painter, float width, float height)
        {
            float originX = (width * 0.5f) - (centerTileX / (float)tilesPerPixel);
            float originY = (height * 0.5f) + (centerTileY / (float)tilesPerPixel);
            if (originX < 0f || originX > width || originY < 0f || originY > height)
            {
                return;
            }

            painter.strokeColor = new Color32(255, 80, 80, 255);
            painter.lineWidth = 2f;
            DrawCross(painter, originX, originY, 7f);
        }

        /// <summary>
        /// 現在の検索中心を常に見失わないよう、Canvas中央へ小さな白い十字を描きます。
        /// </summary>
        /// <param name="painter">現在フレームのPainter2Dです。</param>
        /// <param name="width">Canvasの横幅です。</param>
        /// <param name="height">Canvasの縦幅です。</param>
        private static void DrawCenterMarker(Painter2D painter, float width, float height)
        {
            painter.strokeColor = new Color(1f, 1f, 1f, 0.85f);
            painter.lineWidth = 1.5f;
            DrawCross(painter, width * 0.5f, height * 0.5f, 5f);
        }

        /// <summary>
        /// 指定位置を中心とする水平・垂直の短い線を描きます。
        /// </summary>
        /// <param name="painter">描画先Painter2Dです。</param>
        /// <param name="x">十字中心のX座標です。</param>
        /// <param name="y">十字中心のY座標です。</param>
        /// <param name="radius">中心から各方向へ伸ばす長さです。</param>
        private static void DrawCross(Painter2D painter, float x, float y, float radius)
        {
            painter.BeginPath();
            painter.MoveTo(new Vector2(x - radius, y));
            painter.LineTo(new Vector2(x + radius, y));
            painter.MoveTo(new Vector2(x, y - radius));
            painter.LineTo(new Vector2(x, y + radius));
            painter.Stroke();
        }

    }
}
