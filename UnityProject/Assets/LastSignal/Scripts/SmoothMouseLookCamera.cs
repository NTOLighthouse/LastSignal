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

        [Header("Плавный фокус на объекте")]
        [Tooltip("Длительность плавного перелёта камеры по S-кривой (в секундах).")]
        [SerializeField, Min(0.1f)] private float defaultFocusDuration = 0.75f;
        [SerializeField, Range(0f, 30f)] private float defaultFocusHorizontalAngle = 6f;
        [SerializeField, Range(0f, 30f)] private float defaultFocusVerticalAngle = 5f;

        private Vector3 _defaultWorldPosition;
        private Quaternion _defaultWorldRotation;

        private Vector3 _currentBasePosition;
        private Quaternion _currentBaseRotation;

        private Vector3 _transitionStartPosition;
        private Quaternion _transitionStartRotation;
        private float _transitionStartMaxH;
        private float _transitionStartMaxV;

        private Vector3 _transitionTargetPosition;
        private Quaternion _transitionTargetRotation;
        private float _transitionTargetMaxH;
        private float _transitionTargetMaxV;

        private float _transitionProgress = 1f;
        private float _transitionDuration = 0.75f;

        private bool _isFocused;
        private object _currentFocusOwner;

        private bool _isLookHoldActive;
        private object _lookHoldOwner;
        private float _holdCenterYaw;
        private float _holdCenterPitch;
        private float _holdHalfRangeH;
        private float _holdHalfRangeV;
        private Vector2 _holdAnchorNormalizedMouse;

        private float _activeMaxHorizontalAngle;
        private float _activeMaxVerticalAngle;

        private float _currentYaw;
        private float _currentPitch;
        private float _yawVelocity;
        private float _pitchVelocity;
        private float _targetYaw;
        private float _targetPitch;

        public bool IsFocused => _isFocused;
        public object CurrentFocusOwner => _currentFocusOwner;
        public Vector3 DefaultWorldPosition => _defaultWorldPosition;
        public Quaternion DefaultWorldRotation => _defaultWorldRotation;

        /// <summary>Срабатывает при смене владельца фокуса камеры (null = возврат к общему обзору).</summary>
        public event System.Action<object> OnFocusOwnerChanged;

        private void Awake()
        {
            _defaultWorldPosition = transform.position;
            _defaultWorldRotation = transform.rotation;

            _currentBasePosition = _defaultWorldPosition;
            _currentBaseRotation = _defaultWorldRotation;

            _transitionStartPosition = _defaultWorldPosition;
            _transitionStartRotation = _defaultWorldRotation;
            _transitionTargetPosition = _defaultWorldPosition;
            _transitionTargetRotation = _defaultWorldRotation;

            _activeMaxHorizontalAngle = maxHorizontalAngle;
            _activeMaxVerticalAngle = maxVerticalAngle;
            _transitionStartMaxH = maxHorizontalAngle;
            _transitionStartMaxV = maxVerticalAngle;
            _transitionTargetMaxH = maxHorizontalAngle;
            _transitionTargetMaxV = maxVerticalAngle;
            _transitionProgress = 1f;
        }

        private void OnEnable()
        {
            _yawVelocity = 0f;
            _pitchVelocity = 0f;
        }

        /// <summary>
        /// Плавно (по S-кривой без рывков) приближает и фиксирует камеру на указанной точке.
        /// </summary>
        public void FocusOn(
            Vector3 targetWorldPosition,
            Quaternion targetWorldRotation,
            float? maxHorizontal = null,
            float? maxVertical = null,
            object owner = null,
            float? durationSeconds = null)
        {
            _isLookHoldActive = false;
            _lookHoldOwner = null;

            bool ownerChanged = !_isFocused || !ReferenceEquals(_currentFocusOwner, owner);
            _isFocused = true;
            _currentFocusOwner = owner;

            float targetH = maxHorizontal ?? defaultFocusHorizontalAngle;
            float targetV = maxVertical ?? defaultFocusVerticalAngle;
            float dur = durationSeconds ?? defaultFocusDuration;

            BeginSmoothTransition(targetWorldPosition, targetWorldRotation, targetH, targetV, dur);

            if (ownerChanged)
            {
                OnFocusOwnerChanged?.Invoke(_currentFocusOwner);
            }
        }

        /// <summary>
        /// Нативно ограничивает поворот камеры вокруг её ТЕКУЩЕГО угла взгляда без изменения базовой позы.
        /// После вызова EndLookHold камера продолжает обзор ровно с текущего угла без каких-либо перелётов.
        /// </summary>
        public void BeginLookHold(float maxHorizontalOffset = 2f, float maxVerticalOffset = 1.5f, object owner = null)
        {
            _isLookHoldActive = true;
            _lookHoldOwner = owner;
            _holdCenterYaw = _currentYaw;
            _holdCenterPitch = _currentPitch;
            _holdHalfRangeH = Mathf.Max(0f, maxHorizontalOffset);
            _holdHalfRangeV = Mathf.Max(0f, maxVerticalOffset);

            if (TryReadNormalizedMouse(out Vector2 normalized))
            {
                _holdAnchorNormalizedMouse = normalized;
            }
            else
            {
                _holdAnchorNormalizedMouse = Vector2.zero;
            }
        }

        /// <summary>
        /// Снимает локальное ограничение угла взгляда, оставляя камеру ровно там, куда она смотрит.
        /// </summary>
        public void EndLookHold(object owner = null)
        {
            if (!_isLookHoldActive)
            {
                return;
            }

            if (owner != null && !ReferenceEquals(_lookHoldOwner, owner))
            {
                return;
            }

            _isLookHoldActive = false;
            _lookHoldOwner = null;
        }

        /// <summary>
        /// Плавно возвращает камеру в исходную позицию и обычные лимиты обзора.
        /// Если передан owner — снимает фокус только если он сейчас принадлежит этому owner.
        /// </summary>
        public void ClearFocus(object owner = null, float? durationSeconds = null)
        {
            if (!_isFocused)
            {
                return;
            }

            if (owner != null && !ReferenceEquals(_currentFocusOwner, owner))
            {
                return;
            }

            _isFocused = false;
            _currentFocusOwner = null;

            float dur = durationSeconds ?? defaultFocusDuration;
            BeginSmoothTransition(_defaultWorldPosition, _defaultWorldRotation, maxHorizontalAngle, maxVerticalAngle, dur);

            OnFocusOwnerChanged?.Invoke(null);
        }

        private void BeginSmoothTransition(
            Vector3 targetPos,
            Quaternion targetRot,
            float targetMaxH,
            float targetMaxV,
            float durationSeconds)
        {
            // Впекаем текущий визуальный поворот камеры (включая смещение от мыши) в стартовую точку,
            // чтобы при смене лимитов (например 40° -> 6°) не было ни малейшего скачка!
            _transitionStartPosition = transform.position;
            _transitionStartRotation = transform.rotation;
            _transitionStartMaxH = _activeMaxHorizontalAngle;
            _transitionStartMaxV = _activeMaxVerticalAngle;

            _transitionTargetPosition = targetPos;
            _transitionTargetRotation = targetRot;
            _transitionTargetMaxH = targetMaxH;
            _transitionTargetMaxV = targetMaxV;

            _currentYaw = 0f;
            _currentPitch = 0f;
            _targetYaw = 0f;
            _targetPitch = 0f;
            _yawVelocity = 0f;
            _pitchVelocity = 0f;

            _transitionDuration = Mathf.Max(0.1f, durationSeconds);
            _transitionProgress = 0f;
        }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;

            if (_transitionProgress < 1f && dt > 0f)
            {
                _transitionProgress = Mathf.MoveTowards(_transitionProgress, 1f, dt / _transitionDuration);
                float s = SmootherStep(_transitionProgress);

                _currentBasePosition = Vector3.Lerp(_transitionStartPosition, _transitionTargetPosition, s);
                _currentBaseRotation = Quaternion.Slerp(_transitionStartRotation, _transitionTargetRotation, s);
                _activeMaxHorizontalAngle = Mathf.Lerp(_transitionStartMaxH, _transitionTargetMaxH, s);
                _activeMaxVerticalAngle = Mathf.Lerp(_transitionStartMaxV, _transitionTargetMaxV, s);
            }

            // Во время активного перелёта мягко включаем слежение за мышью ближе к концу траектории,
            // чтобы взгляд идеально плавно переходил в интерактивный режим.
            float lookBlend = _transitionProgress < 1f ? SmootherStep(Mathf.InverseLerp(0.35f, 1f, _transitionProgress)) : 1f;

            if (TryReadNormalizedMouse(out Vector2 normalized))
            {
                if (_isLookHoldActive)
                {
                    Vector2 delta = normalized - _holdAnchorNormalizedMouse;
                    float offsetYaw = Mathf.Clamp(delta.x * _holdHalfRangeH * 2f, -_holdHalfRangeH, _holdHalfRangeH);
                    float offsetPitch = Mathf.Clamp(-delta.y * _holdHalfRangeV * 2f, -_holdHalfRangeV, _holdHalfRangeV);
                    _targetYaw = Mathf.Clamp(_holdCenterYaw + offsetYaw, -_activeMaxHorizontalAngle, _activeMaxHorizontalAngle);
                    _targetPitch = Mathf.Clamp(_holdCenterPitch + offsetPitch, -_activeMaxVerticalAngle, _activeMaxVerticalAngle);
                }
                else
                {
                    float nx = ApplyDeadzoneAndCurve(normalized.x);
                    float ny = ApplyDeadzoneAndCurve(normalized.y);

                    _targetYaw = Mathf.Clamp(nx * _activeMaxHorizontalAngle, -_activeMaxHorizontalAngle, _activeMaxHorizontalAngle) * lookBlend;
                    // В Unity поворот вокруг локальной оси X в минус поднимает камеру вверх.
                    _targetPitch = Mathf.Clamp(-ny * _activeMaxVerticalAngle, -_activeMaxVerticalAngle, _activeMaxVerticalAngle) * lookBlend;
                }
            }

            if (dt > 0f)
            {
                _currentYaw = Mathf.SmoothDamp(_currentYaw, _targetYaw, ref _yawVelocity, smoothTime, Mathf.Infinity, dt);
                _currentPitch = Mathf.SmoothDamp(_currentPitch, _targetPitch, ref _pitchVelocity, smoothTime, Mathf.Infinity, dt);
            }

            // Жёсткое ограничение после сглаживания гарантирует, что камера никогда не выйдет за лимиты.
            _currentYaw = Mathf.Clamp(_currentYaw, -_activeMaxHorizontalAngle, _activeMaxHorizontalAngle);
            _currentPitch = Mathf.Clamp(_currentPitch, -_activeMaxVerticalAngle, _activeMaxVerticalAngle);

            transform.position = _currentBasePosition;
            transform.rotation = _currentBaseRotation * Quaternion.Euler(_currentPitch, _currentYaw, 0f);
        }

        private static float SmootherStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
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
