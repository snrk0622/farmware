// このファイルは、キーボード入力を見下ろし2Dカメラの移動へ変換し、最終座標をピクセルグリッドへ揃えます。
// CameraChunkStreamingBridgeは移動後の表示範囲だけをECSへ渡すため、入力やTransformをゲーム状態の正にはしません。

using UnityEngine;
using UnityEngine.InputSystem;

namespace Farmware.WorldGeneration.Presentation
{
    /// <summary>
    /// WASDまたは矢印キーでカメラを移動し、ドット絵が移動中に揺れないよう1ピクセル単位へ丸めます。
    /// Main Cameraへ1つ配置し、通常の入力読取はUpdate、最終的なTransform反映はLateUpdateで行います。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraPanController : MonoBehaviour
    {
        // 通常移動時の速度です。1タイルを1 Unity unitとして、Inspectorから体感を調整します。
        [Tooltip("WASDまたは矢印キーで移動する速さです。単位はタイル/秒で、1タイルを1 Unity unitとして扱います。")]
        [Min(0f)]
        [SerializeField] private float movementSpeedInTilesPerSecond = 24f;

        // Shiftキー中に通常速度へ掛ける倍率です。遠方チャンクの読み込み確認にも使用します。
        [Tooltip("左右どちらかのShiftキーを押している間の速度倍率です。大きくすると地形生成が追いつかない場合があります。")]
        [Min(1f)]
        [SerializeField] private float fastMovementMultiplier = 4f;

        // 1 Unity unitに含めるピクセル数です。フォールバックTileとPixel Perfect CameraのPPUへ合わせます。
        [Tooltip("1 Unity unitあたりのピクセル数です。SpriteとPixel Perfect CameraのAssets PPUと同じ値にしてください。")]
        [Min(1)]
        [SerializeField] private int pixelsPerUnit = 16;

        // 小さなフレーム移動量を丸めで失わないよう、未丸めの論理カメラ座標をフレーム間で保持します。
        private Vector3 desiredWorldPosition;

        /// <summary>
        /// Component読み込み直後にUnityから呼ばれ、Sceneへ保存された初期座標を未丸めの移動開始位置にします。
        /// </summary>
        private void Awake()
        {
            desiredWorldPosition = transform.position;
        }

        /// <summary>
        /// Componentが有効になったときにUnityから呼ばれ、無効中に変更されたTransform座標を取り込み直します。
        /// </summary>
        private void OnEnable()
        {
            desiredWorldPosition = transform.position;
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
            desiredWorldPosition.x += movementDirection.x * travelDistance;
            desiredWorldPosition.y += movementDirection.y * travelDistance;
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
        /// テレポートやテストから、カメラの論理XY座標を指定して同じフレームにピクセル整列済み位置へ反映します。
        /// </summary>
        /// <param name="worldPosition">移動先のワールドXY座標です。カメラのZ座標は変更しません。</param>
        public void SetWorldPosition(Vector2 worldPosition)
        {
            desiredWorldPosition.x = worldPosition.x;
            desiredWorldPosition.y = worldPosition.y;
            ApplyPixelSnappedPosition();
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
            float snappedX = Mathf.Round(desiredWorldPosition.x * safePixelsPerUnit) / safePixelsPerUnit;
            float snappedY = Mathf.Round(desiredWorldPosition.y * safePixelsPerUnit) / safePixelsPerUnit;
            transform.position = new Vector3(snappedX, snappedY, desiredWorldPosition.z);
        }
    }
}
