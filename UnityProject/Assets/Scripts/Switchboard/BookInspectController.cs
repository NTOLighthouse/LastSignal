using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Управляет плавным поднятием книги (журнала) со стола и фиксацией её перед взглядом игрока.
    /// Использует S-кривую (smootherstep) с мягкой дугой движения и плавно включает размытие фона (Depth of Field Volume),
    /// имитируя фокусировку глаз на страницах. Книга остаётся неподвижной перед взглядом, у камеры сохраняется люфт 5–7°.
    /// </summary>
    public sealed class BookInspectController : MonoBehaviour
    {
        [Header("Ссылки")]
        [SerializeField] private SmoothMouseLookCamera cameraLook;
        [SerializeField] private Camera interactionCamera;
        [SerializeField] private SwitchboardInteractor switchboardInteractor;

        [Header("Точка фиксации книги перед взглядом")]
        [Tooltip("Неподвижная мировая точка, куда плавно поднимается книга. Если не задана — вычисляется перед камерой.")]
        [SerializeField] private Transform inspectPoseTarget;
        [SerializeField, Min(0.15f)] private float fallbackDistanceFromCamera = 0.40f;
        [SerializeField] private float fallbackVerticalOffset = -0.064f;
        [SerializeField, Range(-30f, 30f)] private float fallbackTiltDegrees = 20f;

        [Header("Плавная анимация книги")]
        [Tooltip("Длительность плавного поднятия/опускания книги (в секундах).")]
        [SerializeField, Min(0.1f)] private float transitionDuration = 0.65f;
        [Tooltip("Небольшая естественная дуга вверх при переносе книги со стола к глазам.")]
        [SerializeField, Min(0f)] private float liftArcHeight = 0.035f;

        [Header("Остаточное движение камеры (градусы)")]
        [SerializeField, Range(0f, 15f)] private float focusMaxHorizontalAngle = 6f;
        [SerializeField, Range(0f, 15f)] private float focusMaxVerticalAngle = 5f;

        [Header("Размытие фона при фокусировке на книге (Depth of Field)")]
        [SerializeField] private Volume backgroundBlurVolume;
        [SerializeField, Range(0f, 1f)] private float maxBlurWeight = 1f;

        [Header("Взаимодействие")]
        [SerializeField] private LayerMask raycastMask = ~0;
        [SerializeField, Min(0.5f)] private float maxRayDistance = 15f;
        [SerializeField] private bool closeOnRightClick = true;
        [SerializeField] private bool closeOnEscape = true;

        private Vector3 _tablePosition;
        private Quaternion _tableRotation;
        private Vector3 _inspectPosition;
        private Quaternion _inspectRotation;
        private bool _homeCaptured;

        private float _blendProgress;

        public bool IsInspecting { get; private set; }

        private void Awake()
        {
            if (interactionCamera == null)
            {
                interactionCamera = Camera.main;
            }

            if (cameraLook == null && interactionCamera != null)
            {
                cameraLook = interactionCamera.GetComponent<SmoothMouseLookCamera>();
            }
        }

        private void Start()
        {
            CaptureTablePoseIfNeeded();
            _inspectPosition = _tablePosition;
            _inspectRotation = _tableRotation;
            _blendProgress = 0f;

            if (backgroundBlurVolume != null)
            {
                backgroundBlurVolume.weight = 0f;
            }
        }

        private void OnEnable()
        {
            if (cameraLook != null)
            {
                cameraLook.OnFocusOwnerChanged += HandleCameraFocusOwnerChanged;
            }
        }

        private void OnDisable()
        {
            if (cameraLook != null)
            {
                cameraLook.OnFocusOwnerChanged -= HandleCameraFocusOwnerChanged;
            }

            if (backgroundBlurVolume != null)
            {
                backgroundBlurVolume.weight = 0f;
            }
        }

        private void Update()
        {
            UpdateBookMotionAndBlur();

            if (cameraLook == null || GameplayInputGate.IsBlocked)
            {
                return;
            }

            // Не перехватываем клики, пока игрок держит штекер коммутатора в руке.
            if (switchboardInteractor != null && switchboardInteractor.ActivePlug != null)
            {
                return;
            }

            if (IsInspecting)
            {
                if ((closeOnEscape && WasEscapePressed()) ||
                    (closeOnRightClick && WasRightMousePressed()))
                {
                    PutBookBack();
                }

                return;
            }

            if (WasLeftMousePressed(out Vector2 mousePos) &&
                !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) &&
                IsClickOnBook(mousePos))
            {
                PickUpBook();
            }
        }

        /// <summary>
        /// Плавно поднимает книгу со стола, фиксирует её перед взглядом и размывает фон.
        /// </summary>
        public void PickUpBook()
        {
            CaptureTablePoseIfNeeded();
            IsInspecting = true;

            if (inspectPoseTarget != null)
            {
                _inspectPosition = inspectPoseTarget.position;
                _inspectRotation = inspectPoseTarget.rotation;
            }
            else if (cameraLook != null)
            {
                ComputeFixedInspectPose(out _inspectPosition, out _inspectRotation);
            }

            if (cameraLook != null)
            {
                cameraLook.FocusOn(
                    cameraLook.DefaultWorldPosition,
                    cameraLook.DefaultWorldRotation,
                    focusMaxHorizontalAngle,
                    focusMaxVerticalAngle,
                    owner: this);
            }
        }

        /// <summary>
        /// Плавно кладёт книгу обратно на стол, убирает размытие фона и возвращает обычный обзор.
        /// </summary>
        public void PutBookBack()
        {
            CaptureTablePoseIfNeeded();
            IsInspecting = false;

            if (cameraLook != null)
            {
                cameraLook.ClearFocus(owner: this);
            }
        }

        private void UpdateBookMotionAndBlur()
        {
            if (!_homeCaptured)
            {
                return;
            }

            float targetBlend = IsInspecting ? 1f : 0f;
            if (!Mathf.Approximately(_blendProgress, targetBlend))
            {
                float step = Time.unscaledDeltaTime / Mathf.Max(0.05f, transitionDuration);
                _blendProgress = Mathf.MoveTowards(_blendProgress, targetBlend, step);

                // Пятая степень (Smootherstep): абсолютно мягкий старт и мягкая остановка без рывков.
                float s = SmootherStep(_blendProgress);
                Vector3 pos = Vector3.Lerp(_tablePosition, _inspectPosition, s);
                pos.y += Mathf.Sin(s * Mathf.PI) * liftArcHeight;

                transform.position = pos;
                transform.rotation = Quaternion.Slerp(_tableRotation, _inspectRotation, s);

                if (backgroundBlurVolume != null &&
                    (cameraLook == null || cameraLook.CurrentFocusOwner == null || ReferenceEquals(cameraLook.CurrentFocusOwner, this)))
                {
                    backgroundBlurVolume.weight = s * maxBlurWeight;
                }
            }
        }

        private static float SmootherStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private void HandleCameraFocusOwnerChanged(object newOwner)
        {
            if (IsInspecting && !ReferenceEquals(newOwner, this))
            {
                IsInspecting = false;
            }
        }

        private void ComputeFixedInspectPose(out Vector3 pos, out Quaternion rot)
        {
            Vector3 camPos = cameraLook.DefaultWorldPosition;
            Quaternion camRot = cameraLook.DefaultWorldRotation;

            Vector3 camFwd = camRot * Vector3.forward;
            Vector3 camUp = camRot * Vector3.up;
            Vector3 camRight = camRot * Vector3.right;

            pos = camPos + camFwd * fallbackDistanceFromCamera + camUp * fallbackVerticalOffset;

            Vector3 pagesNormal = Quaternion.AngleAxis(fallbackTiltDegrees, camRight) * (-camFwd);
            pagesNormal = Quaternion.AngleAxis(-8f, camUp) * pagesNormal;
            rot = Quaternion.LookRotation(pagesNormal.normalized, camRight);
        }

        private void CaptureTablePoseIfNeeded()
        {
            if (_homeCaptured)
            {
                return;
            }

            _tablePosition = transform.position;
            _tableRotation = transform.rotation;
            _homeCaptured = true;
        }

        private bool IsClickOnBook(Vector2 mousePos)
        {
            Camera cam = interactionCamera != null ? interactionCamera : Camera.main;
            if (cam == null)
            {
                return false;
            }

            Ray ray = cam.ScreenPointToRay(mousePos);
            if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, raycastMask, QueryTriggerInteraction.Collide))
            {
                return false;
            }

            return hit.transform == transform || hit.transform.IsChildOf(transform);
        }

        private static bool WasLeftMousePressed(out Vector2 mousePosition)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                mousePosition = mouse.position.ReadValue();
                return true;
            }

            mousePosition = Vector2.zero;
            return false;
#else
            if (Input.GetMouseButtonDown(0))
            {
                mousePosition = Input.mousePosition;
                return true;
            }

            mousePosition = Vector2.zero;
            return false;
#endif
        }

        private static bool WasRightMousePressed()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            return mouse != null && mouse.rightButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }

        private static bool WasEscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
