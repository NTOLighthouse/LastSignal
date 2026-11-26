using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Управляет приближением и фиксацией камеры на настенной карте.
    /// По клику ЛКМ по карте приближает камеру так, чтобы вся карта была в кадре,
    /// оставляя небольшое остаточное движение мышью. Выход — по ПКМ или Escape.
    /// </summary>
    public sealed class MapCameraFocus : MonoBehaviour
    {
        [Header("Ссылки")]
        [SerializeField] private SmoothMouseLookCamera cameraLook;
        [SerializeField] private Camera interactionCamera;
        [SerializeField] private SwitchboardInteractor switchboardInteractor;

        [Header("Точка фокуса камеры на карте")]
        [SerializeField] private Transform focusCameraPoint;

        [Header("Остаточное движение и плавность перелёта")]
        [SerializeField, Min(0.2f)] private float focusDurationSeconds = 1.0f;
        [SerializeField, Range(0f, 15f)] private float focusMaxHorizontalAngle = 6f;
        [SerializeField, Range(0f, 15f)] private float focusMaxVerticalAngle = 5f;

        [Header("Настройки взаимодействия")]
        [SerializeField] private LayerMask raycastMask = ~0;
        [SerializeField, Min(0.5f)] private float maxRayDistance = 15f;
        [SerializeField] private bool exitFocusOnRightClick = true;
        [SerializeField] private bool exitFocusOnEscape = true;

        public bool IsFocused => cameraLook != null && ReferenceEquals(cameraLook.CurrentFocusOwner, this);

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

        private void Update()
        {
            if (cameraLook == null || GameplayInputGate.IsBlocked)
            {
                return;
            }

            // Не перехватываем клики, пока игрок держит штекер коммутатора в руке.
            if (switchboardInteractor != null && switchboardInteractor.ActivePlug != null)
            {
                return;
            }

            if (IsFocused)
            {
                if ((exitFocusOnEscape && WasEscapePressed()) ||
                    (exitFocusOnRightClick && WasRightMousePressed()))
                {
                    ExitFocus();
                }

                return;
            }

            if (WasLeftMousePressed(out Vector2 mousePos) && IsClickOnMap(mousePos))
            {
                EnterFocus();
            }
        }

        /// <summary>
        /// Плавно приближает и фиксирует камеру на карте.
        /// </summary>
        public void EnterFocus()
        {
            if (cameraLook == null || focusCameraPoint == null)
            {
                return;
            }

            cameraLook.FocusOn(
                focusCameraPoint.position,
                focusCameraPoint.rotation,
                focusMaxHorizontalAngle,
                focusMaxVerticalAngle,
                owner: this,
                durationSeconds: focusDurationSeconds);
        }

        /// <summary>
        /// Возвращает камеру в общий обзор.
        /// </summary>
        public void ExitFocus()
        {
            if (cameraLook != null)
            {
                cameraLook.ClearFocus(owner: this, durationSeconds: focusDurationSeconds);
            }
        }

        private bool IsClickOnMap(Vector2 mousePos)
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
