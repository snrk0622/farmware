// このファイルは、キーボード入力を見下ろし2Dカメラの移動へ変換し、表示原点を管理して最終座標をピクセルグリッドへ揃えます。
// CameraとTilemapは表示原点からの相対座標だけを扱い、CameraChunkStreamingBridgeが絶対タイル座標へ戻してECSへ渡します。

using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// WASDまたは矢印キーでカメラを移動し、ドット絵が移動中に揺れないよう1ピクセル単位へ丸めます。
    /// Main Cameraへ1つ配置し、絶対座標から分離した表示原点をCamera、Grid、Tilemapへ共通提供します。
    /// </summary>
    [DefaultExecutionOrder(-150)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraPanController : MonoBehaviour
    {
        /// <summary>
        /// 新しくCameraPanControllerを追加したときに使用する通常移動速度です。
        /// 480×270の基準画面を落ち着いて見渡せるよう、1秒間に12タイル進む値を使用します。
        /// </summary>
        public const float DefaultMovementSpeedInTilesPerSecond = 12f;

        /// <summary>
        /// 新しくCameraPanControllerを追加したときに使用する高速移動倍率です。
        /// Shift操作でも地形生成を極端に追い越さないよう、通常速度の3倍に制限します。
        /// </summary>
        public const float DefaultFastMovementMultiplier = 3f;

        /// <summary>
        /// 通常移動中に表示原点を自動更新する、現在の表示原点からの距離です。
        /// 64タイルの論理チャンク8個分を使い、Camera Transformを十分小さいfloat座標へ保ちます。
        /// </summary>
        public const int DefaultAutomaticRebaseThresholdInTiles =
            WorldGridConstants.LogicalChunkSize * 8;

        // 通常移動時の速度です。1タイルを1 Unity unitとして、Inspectorから体感を調整します。
        [Tooltip("WASDまたは矢印キーで移動する速さです。単位はタイル/秒で、1タイルを1 Unity unitとして扱います。")]
        [Min(0f)]
        [SerializeField] private float movementSpeedInTilesPerSecond = DefaultMovementSpeedInTilesPerSecond;

        // Shiftキー中に通常速度へ掛ける倍率です。遠方チャンクの読み込み確認にも使用します。
        [Tooltip("左右どちらかのShiftキーを押している間の速度倍率です。大きくすると地形生成が追いつかない場合があります。")]
        [Min(1f)]
        [SerializeField] private float fastMovementMultiplier = DefaultFastMovementMultiplier;

        // Cameraが表示原点から離れ過ぎる前に、Presentationだけを現在チャンク境界へ戻す距離です。
        [Tooltip("自動Floating Originを実行する距離です。単位はタイルです。旧表示を最大ズーム範囲外へ出すため512以上を指定し、大きくすると遠方でfloat精度が低下する点に注意してください。")]
        [Min(DefaultAutomaticRebaseThresholdInTiles)]
        [SerializeField] private int automaticRebaseThresholdInTiles =
            DefaultAutomaticRebaseThresholdInTiles;

        // 1 Unity unitに含めるピクセル数です。フォールバックTileとPixel Perfect CameraのPPUへ合わせます。
        [Tooltip("1 Unity unitあたりのピクセル数です。SpriteとPixel Perfect CameraのAssets PPUと同じ値にしてください。")]
        [Min(1)]
        [SerializeField] private int pixelsPerUnit = 16;

        // 小さなフレーム移動量を丸めで失わないよう、表示原点からの未丸め相対カメラ座標をフレーム間で保持します。
        private Vector3 desiredRelativeWorldPosition;

        // 表示空間のローカルセル(0, 0)に対応する絶対タイル座標です。Presentationだけが変更します。
        private DisplayOrigin currentDisplayOrigin;

        // Presenterが自動移動と明示テレポートで旧Tilemapの片付け方を分けるため、直近の変更理由を保持します。
        private bool lastDisplayOriginChangeWasAutomatic;

        /// <summary>
        /// 現在の表示空間セル(0, 0)へ対応する絶対タイル座標です。
        /// CameraChunkStreamingBridgeと全Tilemap Presenterが同じ値を読み、相対表示と絶対Simulation座標を一致させます。
        /// </summary>
        public DisplayOrigin CurrentDisplayOrigin => currentDisplayOrigin;

        /// <summary>
        /// 表示原点から見た、ピクセル整列前のCamera中心XY座標です。単位はタイルです。
        /// </summary>
        public Vector2 DesiredRelativeWorldPosition => new Vector2(
            desiredRelativeWorldPosition.x,
            desiredRelativeWorldPosition.y);

        /// <summary>
        /// 直近の表示原点変更が通常カメラ移動による自動Floating Originだったかを返します。
        /// ChunkTilemapPresenterが旧表示を画面外で分割消去できるか判断するときに使用します。
        /// </summary>
        public bool LastDisplayOriginChangeWasAutomatic =>
            lastDisplayOriginChangeWasAutomatic;

        /// <summary>
        /// Component読み込み直後にUnityから呼ばれ、Sceneへ保存された初期座標を未丸めの移動開始位置にします。
        /// </summary>
        private void Awake()
        {
            desiredRelativeWorldPosition = transform.position;
            currentDisplayOrigin = new DisplayOrigin(int2.zero);
            lastDisplayOriginChangeWasAutomatic = false;
        }

        /// <summary>
        /// Componentが有効になったときにUnityから呼ばれ、無効中に変更されたTransform座標を取り込み直します。
        /// </summary>
        private void OnEnable()
        {
            desiredRelativeWorldPosition = transform.position;
        }

        /// <summary>
        /// MonoBehaviourの各描画フレームで呼ばれ、現在のキーボード入力を未丸め座標へ加算します。
        /// 一時停止中でも地形を見回せるよう、ゲーム速度の影響を受けないunscaledDeltaTimeを使用します。
        /// </summary>
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            Vector2 movementDirection = ReadMovementDirection(keyboard);
            if (movementDirection.sqrMagnitude <= 0f)
            {
                return;
            }

            // 斜め入力を正規化し、上下または左右だけの入力より約1.41倍速くなることを防ぎます。
            if (movementDirection.sqrMagnitude > 1f)
            {
                movementDirection.Normalize();
            }

            bool isFastMovement = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            float speedMultiplier = isFastMovement ? fastMovementMultiplier : 1f;
            float travelDistance = movementSpeedInTilesPerSecond * speedMultiplier * Time.unscaledDeltaTime;
            desiredRelativeWorldPosition.x += movementDirection.x * travelDistance;
            desiredRelativeWorldPosition.y += movementDirection.y * travelDistance;

            TryAutomaticallyRebaseDisplayOrigin();
            ApplyPixelSnappedPosition();
        }

        /// <summary>
        /// 通常のUpdateがすべて終わった後にUnityから呼ばれ、カメラのXY座標を1ピクセル刻みへ丸めて反映します。
        /// カメラ追従は物理FixedUpdateではなくLateUpdateで行い、描画フレームとのずれを避けます。
        /// </summary>
        private void LateUpdate()
        {
            ApplyPixelSnappedPosition();
        }

        /// <summary>
        /// テストや通常移動の再現から、表示原点基準の相対XY座標を指定して同じフレームに反映します。
        /// 自動更新距離を越えた場合は、実際のキーボード移動と同じFloating Origin処理も実行します。
        /// </summary>
        /// <param name="worldPosition">移動先の表示用相対ワールドXY座標です。カメラのZ座標は変更しません。</param>
        public void SetWorldPosition(Vector2 worldPosition)
        {
            desiredRelativeWorldPosition.x = worldPosition.x;
            desiredRelativeWorldPosition.y = worldPosition.y;
            TryAutomaticallyRebaseDisplayOrigin();
            ApplyPixelSnappedPosition();
        }

        /// <summary>
        /// Simulation上の絶対タイル中央へCameraを移し、同じチャンク境界を新しい表示原点にします。
        /// 初期スポーンやロード後の復帰で大きな絶対座標をTransformへ直接書かないために使用します。
        /// </summary>
        /// <param name="absoluteTileX">移動先セルの絶対水平タイル座標です。</param>
        /// <param name="absoluteTileY">移動先セルの絶対垂直タイル座標です。</param>
        public void SetAbsoluteTileCenter(int absoluteTileX, int absoluteTileY)
        {
            SetAbsoluteWorldPosition(
                absoluteTileX + 0.5d,
                absoluteTileY + 0.5d);
        }

        /// <summary>
        /// Simulation上の連続した絶対座標へCameraを移し、Transformへはチャンク境界からの小さな相対値だけを書き込みます。
        /// </summary>
        /// <param name="absoluteWorldX">移動先の絶対水平座標です。1タイルを1ワールド単位とします。</param>
        /// <param name="absoluteWorldY">移動先の絶対垂直座標です。1タイルを1ワールド単位とします。</param>
        /// <returns>所属タイルがint範囲内で有限値ならtrue、無効な座標なら何も変更せずfalseです。</returns>
        public bool SetAbsoluteWorldPosition(double absoluteWorldX, double absoluteWorldY)
        {
            return SetAbsoluteWorldPosition(
                absoluteWorldX,
                absoluteWorldY,
                false);
        }

        /// <summary>
        /// 絶対Camera座標を現在チャンク境界基準の小さな相対座標へ変換します。
        /// 自動移動と明示テレポートで共通計算を使いながら、Presenterへ変更理由を伝えるため内部で分離しています。
        /// </summary>
        /// <param name="absoluteWorldX">移動先の絶対水平座標です。1タイルを1ワールド単位とします。</param>
        /// <param name="absoluteWorldY">移動先の絶対垂直座標です。1タイルを1ワールド単位とします。</param>
        /// <param name="isAutomaticRebase">通常移動の距離閾値による原点変更ならtrue、明示移動ならfalseです。</param>
        /// <returns>所属タイルがint範囲内で有限値ならtrue、無効な座標なら何も変更せずfalseです。</returns>
        private bool SetAbsoluteWorldPosition(
            double absoluteWorldX,
            double absoluteWorldY,
            bool isAutomaticRebase)
        {
            if (double.IsNaN(absoluteWorldX)
                || double.IsInfinity(absoluteWorldX)
                || double.IsNaN(absoluteWorldY)
                || double.IsInfinity(absoluteWorldY))
            {
                return false;
            }

            double flooredAbsoluteX = System.Math.Floor(absoluteWorldX);
            double flooredAbsoluteY = System.Math.Floor(absoluteWorldY);
            if (flooredAbsoluteX < int.MinValue
                || flooredAbsoluteX > int.MaxValue
                || flooredAbsoluteY < int.MinValue
                || flooredAbsoluteY > int.MaxValue)
            {
                return false;
            }

            int absoluteTileX = (int)flooredAbsoluteX;
            int absoluteTileY = (int)flooredAbsoluteY;
            int alignedOriginX = WorldGrid.FloorDivide(
                absoluteTileX,
                WorldGridConstants.LogicalChunkSize) * WorldGridConstants.LogicalChunkSize;
            int alignedOriginY = WorldGrid.FloorDivide(
                absoluteTileY,
                WorldGridConstants.LogicalChunkSize) * WorldGridConstants.LogicalChunkSize;

            // 原点を先にチャンク境界へそろえ、CameraのTransformへは0〜64付近の相対値だけを渡します。
            currentDisplayOrigin = new DisplayOrigin(new int2(alignedOriginX, alignedOriginY));
            lastDisplayOriginChangeWasAutomatic = isAutomaticRebase;
            desiredRelativeWorldPosition.x = (float)(absoluteWorldX - alignedOriginX);
            desiredRelativeWorldPosition.y = (float)(absoluteWorldY - alignedOriginY);
            ApplyPixelSnappedPosition();
            return true;
        }

        /// <summary>
        /// 未丸めCamera座標が閾値へ達したとき、絶対位置を変えずに表示原点だけを現在チャンク境界へ移します。
        /// Update回数やSimulation Tickには影響せず、Presentationのfloat座標を小さく保つためだけに実行します。
        /// </summary>
        /// <returns>表示原点を更新した場合はtrue、閾値未満またはワールド範囲外ならfalseです。</returns>
        private bool TryAutomaticallyRebaseDisplayOrigin()
        {
            int safeThreshold = Mathf.Max(
                DefaultAutomaticRebaseThresholdInTiles,
                automaticRebaseThresholdInTiles);
            if (Mathf.Abs(desiredRelativeWorldPosition.x) < safeThreshold
                && Mathf.Abs(desiredRelativeWorldPosition.y) < safeThreshold)
            {
                return false;
            }

            // float同士を先に加算すると遠方で桁落ちするため、整数の表示原点をdoubleへ広げてから相対値を加えます。
            double absoluteWorldX = currentDisplayOrigin.AbsoluteTileCoordinate.x
                + (double)desiredRelativeWorldPosition.x;
            double absoluteWorldY = currentDisplayOrigin.AbsoluteTileCoordinate.y
                + (double)desiredRelativeWorldPosition.y;
            return SetAbsoluteWorldPosition(
                absoluteWorldX,
                absoluteWorldY,
                true);
        }

        /// <summary>
        /// 現在の表示原点と相対Camera位置から、Simulation上の連続した絶対Camera位置を取得します。
        /// </summary>
        /// <returns>1タイルを1ワールド単位とする絶対XY座標です。</returns>
        public double2 GetAbsoluteWorldPosition()
        {
            return new double2(
                currentDisplayOrigin.AbsoluteTileCoordinate.x
                    + (double)transform.position.x,
                currentDisplayOrigin.AbsoluteTileCoordinate.y
                    + (double)transform.position.y);
        }

        /// <summary>
        /// WASDと矢印キーを、各軸が-1から1の範囲になる移動方向へまとめます。
        /// </summary>
        /// <param name="keyboard">Input Systemが現在入力を受け取っているKeyboardです。</param>
        /// <returns>右・上を正方向とする2次元入力です。</returns>
        private static Vector2 ReadMovementDirection(Keyboard keyboard)
        {
            float horizontal = 0f;
            float vertical = 0f;

            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            {
                horizontal -= 1f;
            }

            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            {
                horizontal += 1f;
            }

            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            {
                vertical -= 1f;
            }

            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            {
                vertical += 1f;
            }

            return new Vector2(horizontal, vertical);
        }

        /// <summary>
        /// 未丸め座標を指定PPUのピクセル幅へ丸め、Transformへ1回だけ書き込みます。
        /// </summary>
        private void ApplyPixelSnappedPosition()
        {
            int safePixelsPerUnit = Mathf.Max(1, pixelsPerUnit);
            float snappedX = Mathf.Round(desiredRelativeWorldPosition.x * safePixelsPerUnit)
                / safePixelsPerUnit;
            float snappedY = Mathf.Round(desiredRelativeWorldPosition.y * safePixelsPerUnit)
                / safePixelsPerUnit;
            transform.position = new Vector3(
                snappedX,
                snappedY,
                desiredRelativeWorldPosition.z);
        }
    }
}
