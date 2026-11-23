using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastSignal
{
    /// <summary>
    /// Плавное следование камеры за положением курсора мыши в ограниченных углах обзора.
    /// Не блокирует курсор и не выходит за пределы заданных ограничений.
    /// </summary>
    public sealed class SmoothMouseLookCamera : MonoBehaviour
    {
        [Header("Ограничения поворота (градусы в каждую сторону)")]
        [SerializeField, Range(0f, 89f)] private float maxHorizontalAngle = 40f;
        [SerializeField, Range(0f, 89f)] private float maxVerticalAngle = 10f;

        [Header("Плавность следования")]
        [SerializeField, Min(0.01f)] private float smoothTime = 0.2f;
        [SerializeField, Range(0f, 0.25f)] private float centerDeadzone = 0.04f;
        [SerializeField, Range(1f, 2.5f)] private float responseCurve = 1.15f;

        private Quaternion _baseLocalRotation;
        private float _currentYaw;
        private float _currentPitch;
        private float _yawVelocity;
        private float _pitchVelocity;
        private float _targetYaw;
        private float _targetPitch;

        private void Awake()
        {
            _baseLocalRotation = transform.localRotation;
        }

        private void OnEnable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _yawVelocity = 0f;
            _pitchVelocity = 0f;
        }

        private void LateUpdate()
        {
            if (TryReadNormalizedMouse(out Vector2 normalized))
            {
                float nx = ApplyDeadzoneAndCurve(normalized.x);
                float ny = ApplyDeadzoneAndCurve(normalized.y);

                _targetYaw = Mathf.Clamp(nx * maxHorizontalAngle, -maxHorizontalAngle, maxHorizontalAngle);
                // В Unity поворот вокруг локальной оси X в минус поднимает камеру вверх.
                _targetPitch = Mathf.Clamp(-ny * maxVerticalAngle, -maxVerticalAngle, maxVerticalAngle);
            }

            float dt = Time.unscaledDeltaTime;
            if (dt > 0f)
            {
                _currentYaw = Mathf.SmoothDamp(_currentYaw, _targetYaw, ref _yawVelocity, smoothTime, Mathf.Infinity, dt);
                _currentPitch = Mathf.SmoothDamp(_currentPitch, _targetPitch, ref _pitchVelocity, smoothTime, Mathf.Infinity, dt);
            }

            // Жёсткое ограничение после сглаживания гарантирует, что камера никогда не выйдет за лимиты.
            _currentYaw = Mathf.Clamp(_currentYaw, -maxHorizontalAngle, maxHorizontalAngle);
            _currentPitch = Mathf.Clamp(_currentPitch, -maxVerticalAngle, maxVerticalAngle);

            transform.localRotation = _baseLocalRotation * Quaternion.Euler(_currentPitch, _currentYaw, 0f);
        }

        private float ApplyDeadzoneAndCurve(float value)
        {
            float abs = Mathf.Abs(value);
            if (abs <= centerDeadzone)
            {
                return 0f;
            }

            float remapped = Mathf.Clamp01((abs - centerDeadzone) / (1f - centerDeadzone));
            float curved = Mathf.Pow(remapped, responseCurve);
            return Mathf.Sign(value) * curved;
        }

        private static bool TryReadNormalizedMouse(out Vector2 normalized)
        {
            normalized = Vector2.zero;
            int width = Screen.width;
            int height = Screen.height;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            Vector2 mousePos;
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return false;
            }
            mousePos = mouse.position.ReadValue();
#else
            mousePos = Input.mousePosition;
#endif

            // Игнорируем неинициализированную позицию (0, 0) до первого движения мыши внутри окна.
            if (mousePos.sqrMagnitude < 0.0001f && !Application.isFocused)
            {
                return false;
            }

            float nx = Mathf.Clamp((mousePos.x / width) * 2f - 1f, -1f, 1f);
            float ny = Mathf.Clamp((mousePos.y / height) * 2f - 1f, -1f, 1f);
            normalized = new Vector2(nx, ny);
            return true;
        }
    }
}
