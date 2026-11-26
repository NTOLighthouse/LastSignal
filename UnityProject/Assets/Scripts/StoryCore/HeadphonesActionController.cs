using System;
using UnityEngine;
using UnityEngine.EventSystems;
using LastSignal.SwitchboardSystem;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastSignal.StoryCore
{
    /// <summary>
    /// Механика «Снять наушники» (ГДД: щелчок с удержанием по наушникам).
    /// В Ночь 3 при удержании ЛКМ плавно поднимает наушники со стола/головы и запускает концовку «Тишина».
    /// </summary>
    public sealed class HeadphonesActionController : MonoBehaviour
    {
        [Header("Ссылки")]
        [SerializeField] private Camera interactionCamera;
        [SerializeField] private SwitchboardInteractor switchboardInteractor;

        [Header("Параметры удержания")]
        [Tooltip("Разрешено ли сейчас завершить игру снятием наушников (включается в Ночь 3).")]
        [SerializeField] private bool isRemovalAllowed = false;
        [SerializeField, Min(0.4f)] private float holdDurationSeconds = 1.6f;
        [SerializeField] private float liftVisualOffsetY = 0.025f;
        [SerializeField] private LayerMask raycastMask = ~0;
        [SerializeField, Min(0.5f)] private float maxRayDistance = 12f;

        private Vector3 _restLocalPos;
        private bool _isHolding;
        private float _holdProgress;
        private bool _triggered;

        public bool IsRemovalAllowed
        {
            get => isRemovalAllowed;
            set => isRemovalAllowed = value;
        }

        public float HoldProgress01 => _holdProgress;
        public event Action OnHeadphonesRemoved;

        private void Awake()
        {
            _restLocalPos = transform.localPosition;
            if (interactionCamera == null) interactionCamera = Camera.main;
        }

        private void Update()
        {
            if (_triggered)
            {
                return;
            }

            bool canInteract = isRemovalAllowed &&
                               !GameplayInputGate.IsBlocked &&
                               (switchboardInteractor == null || switchboardInteractor.ActivePlug == null) &&
                               !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());

            if (!canInteract)
            {
                ReleaseHold();
                return;
            }

            if (IsLeftMouseHeld(out Vector2 mousePos) && IsHitOnHeadphones(mousePos))
            {
                _isHolding = true;
                _holdProgress = Mathf.Clamp01(_holdProgress + Time.unscaledDeltaTime / Mathf.Max(0.2f, holdDurationSeconds));
                ApplyVisualLift(_holdProgress);

                if (_holdProgress >= 1f)
                {
                    _triggered = true;
                    _isHolding = false;
                    OnHeadphonesRemoved?.Invoke();
                }
            }
            else if (_isHolding || _holdProgress > 0f)
            {
                ReleaseHold();
            }
        }

        public void ResetActionState()
        {
            _triggered = false;
            _isHolding = false;
            _holdProgress = 0f;
            transform.localPosition = _restLocalPos;
        }

        private void ReleaseHold()
        {
            _isHolding = false;
            if (_holdProgress > 0f)
            {
                _holdProgress = Mathf.MoveTowards(_holdProgress, 0f, Time.unscaledDeltaTime * 3.5f);
                ApplyVisualLift(_holdProgress);
            }
        }

        private void ApplyVisualLift(float t)
        {
            float s = t * t * (3f - 2f * t);
            transform.localPosition = _restLocalPos + Vector3.up * (s * liftVisualOffsetY);
        }

        private bool IsHitOnHeadphones(Vector2 mousePos)
        {
            Camera cam = interactionCamera != null ? interactionCamera : Camera.main;
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(mousePos);
            if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, raycastMask, QueryTriggerInteraction.Collide))
            {
                return false;
            }

            return hit.transform == transform || hit.transform.IsChildOf(transform);
        }

        private static bool IsLeftMouseHeld(out Vector2 mousePos)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                mousePos = mouse.position.ReadValue();
                return true;
            }
            mousePos = Vector2.zero;
            return false;
#else
            if (Input.GetMouseButton(0))
            {
                mousePos = Input.mousePosition;
                return true;
            }
            mousePos = Vector2.zero;
            return false;
#endif
        }
    }
}
