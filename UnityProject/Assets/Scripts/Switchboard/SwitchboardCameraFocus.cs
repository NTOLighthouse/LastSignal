using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Управляет плавным приближением и фиксацией камеры на коммутаторе с небольшим остаточным люфтом (5–7°).
    /// Активируется при взятии любого штекера или при клике ЛКМ по коммутатору.
    /// Выход из фокуса — по ПКМ (когда в руке нет штекера) или по клавише Escape.
    /// </summary>
    public sealed class SwitchboardCameraFocus : MonoBehaviour
    {
        [Header("Ссылки")]
        [SerializeField] private SmoothMouseLookCamera cameraLook;
        [SerializeField] private Camera interactionCamera;
        [SerializeField] private SwitchboardInteractor interactor;

        [Header("Точка фокуса камеры")]
        [Tooltip("Трансформ позиции и направления камеры в режиме фокуса на коммутаторе.")]
        [SerializeField] private Transform focusCameraPoint;

        [Header("Остаточное движение камеры в фокусе (градусы)")]
        [SerializeField, Range(0f, 15f)] private float focusMaxHorizontalAngle = 6f;
        [SerializeField, Range(0f, 15f)] private float focusMaxVerticalAngle = 5f;

        [Header("Взаимодействие")]
        [SerializeField] private LayerMask raycastMask = ~0;
        [SerializeField, Min(0.5f)] private float maxRayDistance = 15f;
        [SerializeField] private bool exitFocusOnRightClick = true;
        [SerializeField] private bool exitFocusOnEscape = true;

        private bool _hadActivePlugAtFrameStart;

        public bool IsFocused => cameraLook != null && ReferenceEquals(cameraLook.CurrentFocusOwner, this);

        private void Awake()
        {
            if (interactor == null)
            {
                interactor = GetComponent<SwitchboardInteractor>();
            }

            if (interactionCamera == null)
            {
                interactionCamera = Camera.main;
            }

            if (cameraLook == null && interactionCamera != null)
            {
                cameraLook = interactionCamera.GetComponent<SmoothMouseLookCamera>();
            }
        }

        private void Update()
        {
            if (cameraLook == null)
            {
                return;
            }

            bool hasActivePlugNow = interactor != null && interactor.ActivePlug != null;

            // Если игрок держит штекер — гарантируем фокус на коммутаторе.
            if (hasActivePlugNow)
            {
                if (!IsFocused)
                {
                    EnterFocus();
                }

                _hadActivePlugAtFrameStart = true;
                return;
            }

            // Выход по Escape (только если фокус сейчас на коммутаторе).
            if (exitFocusOnEscape && WasEscapePressed() && IsFocused)
            {
                ExitFocus();
                _hadActivePlugAtFrameStart = false;
                return;
            }

            // Выход по ПКМ (только если фокус на коммутаторе и ПКМ не ушёл на возврат штекера).
            if (exitFocusOnRightClick && WasRightMousePressed() && IsFocused && !_hadActivePlugAtFrameStart)
            {
                ExitFocus();
                _hadActivePlugAtFrameStart = false;
                return;
            }

            // Активация фокуса по клику ЛКМ по коммутатору или штекеру.
            if (!IsFocused && WasLeftMousePressed(out Vector2 mousePos))
            {
                if (IsClickOnSwitchboardOrPlug(mousePos))
                {
                    EnterFocus();
                }
            }

            _hadActivePlugAtFrameStart = false;
        }

        /// <summary>
        /// Плавно приближает и фиксирует камеру на коммутаторе.
        /// </summary>
        public void EnterFocus()
        {
            if (cameraLook == null)
            {
                return;
            }

            Vector3 targetPos;
            Quaternion targetRot;

            if (focusCameraPoint != null)
            {
                targetPos = focusCameraPoint.position;
                targetRot = focusCameraPoint.rotation;
            }
            else
            {
                Vector3 boardCenter = transform.position + new Vector3(0f, 0.14f, 0.05f);
                targetPos = boardCenter + new Vector3(0f, 0.10f, 0.51f);
                targetRot = Quaternion.LookRotation((boardCenter - targetPos).normalized, Vector3.up);
            }

            cameraLook.FocusOn(targetPos, targetRot, focusMaxHorizontalAngle, focusMaxVerticalAngle, owner: this);
        }

        /// <summary>
        /// Плавно возвращает камеру к общему обзору стола.
        /// </summary>
        public void ExitFocus()
        {
            if (cameraLook != null)
            {
                cameraLook.ClearFocus(owner: this);
            }
        }

        private bool IsClickOnSwitchboardOrPlug(Vector2 mousePos)
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

            if (hit.collider.GetComponentInParent<SwitchboardPlug>() != null)
            {
                return true;
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
