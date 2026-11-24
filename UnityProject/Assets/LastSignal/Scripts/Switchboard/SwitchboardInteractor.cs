using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastSignal.Switchboard
{
    /// <summary>
    /// Отвечает исключительно за ввод игрока (клики мышью по гнёздам и штекерам коммутатора).
    /// Отделён от SwitchboardController, чтобы ввод можно было легко блокировать
    /// при открытии книги, карты или паузы.
    /// </summary>
    public sealed class SwitchboardInteractor : MonoBehaviour
    {
        [Header("Ссылки")]
        [SerializeField] private SwitchboardController switchboard;
        [SerializeField] private Camera interactionCamera;

        [Header("Параметры рейкаста")]
        [SerializeField] private LayerMask interactionMask = ~0;
        [SerializeField, Min(0.5f)] private float maxInteractionDistance = 10f;
        [SerializeField] private bool interactionEnabled = true;

        public bool InteractionEnabled
        {
            get => interactionEnabled;
            set => interactionEnabled = value;
        }

        private void Awake()
        {
            if (switchboard == null)
            {
                switchboard = GetComponent<SwitchboardController>();
            }

            if (interactionCamera == null)
            {
                interactionCamera = Camera.main;
            }
        }

        private void Update()
        {
            if (!interactionEnabled || switchboard == null)
            {
                return;
            }

            if (interactionCamera == null)
            {
                interactionCamera = Camera.main;
                if (interactionCamera == null)
                {
                    return;
                }
            }

            if (WasRightClickPressed())
            {
                switchboard.ReleaseHeldPlug();
                return;
            }

            if (!WasLeftClickPressed(out Vector2 screenPosition))
            {
                return;
            }

            Ray ray = interactionCamera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, maxInteractionDistance, interactionMask, QueryTriggerInteraction.Collide))
            {
                return;
            }

            // Сначала проверяем клик по гнезду (линии).
            SwitchboardChannel channel = hit.collider.GetComponentInParent<SwitchboardChannel>();
            if (channel != null)
            {
                switchboard.TryInteractWithChannel(channel.ChannelId);
                return;
            }

            // Затем проверяем клик непосредственно по штекеру.
            SwitchboardPlug plug = hit.collider.GetComponentInParent<SwitchboardPlug>();
            if (plug != null)
            {
                switchboard.TryInteractWithPlug(plug);
            }
        }

        public void Configure(SwitchboardController controller, Camera cam)
        {
            switchboard = controller;
            interactionCamera = cam;
        }

        private static bool WasLeftClickPressed(out Vector2 mousePosition)
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

        private static bool WasRightClickPressed()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            return mouse != null && mouse.rightButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }
    }
}
