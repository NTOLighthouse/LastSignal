using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Управляет игровым правилом курсора: скрывает системную стрелку ОС, отображает
    /// аккуратный диегетический прицел (кружочек с точкой внутри) и контекстную плашку
    /// описания объекта при наведении (как в HTML-прототипе: наушники, фото, письмо и т.д.).
    /// </summary>
    public sealed class DiegeticCursorController : MonoBehaviour
    {
        private enum ReticleState
        {
            Idle = 0,
            Hover = 1,
            Active = 2
        }

        [Header("Ссылки на элементы прицела")]
        [SerializeField] private Canvas reticleCanvas;
        [SerializeField] private RectTransform reticleRoot;
        [SerializeField] private Image ringImage;
        [SerializeField] private Image dotImage;

        [Header("Плашка подсказки при наведении")]
        [SerializeField] private RectTransform hoverLabelRoot;
        [SerializeField] private CanvasGroup hoverLabelCanvasGroup;
        [SerializeField] private Text hoverLabelText;
        [SerializeField] private Vector2 hoverLabelOffset = new Vector2(18f, -24f);
        [SerializeField, Min(1f)] private float hoverLabelFadeSpeed = 14f;

        [Header("Ссылки на системы сцены")]
        [SerializeField] private Camera interactionCamera;
        [SerializeField] private SmoothMouseLookCamera cameraLook;
        [SerializeField] private SwitchboardInteractor switchboardInteractor;
        [SerializeField] private RadioTunerController radioTuner;
        [SerializeField] private BookInspectController bookInspect;
        [SerializeField] private LetterInspectController letterInspect;
        [SerializeField] private MapCameraFocus mapFocus;
        [SerializeField] private SwitchboardCameraFocus switchboardFocus;

        [Header("Правило курсора ОС")]
        [Tooltip("Ограничивать курсор границами окна в собранном билде (в Editor остаётся свободным).")]
        [SerializeField] private bool confineCursorInBuild = true;
        [SerializeField] private LayerMask hoverRaycastMask = ~0;
        [SerializeField, Min(0.5f)] private float maxHoverDistance = 15f;

        [Header("Внешний вид прицела")]
        [SerializeField, Min(8f)] private float baseSizePx = 22f;
        [SerializeField] private Color idleRingColor = new Color(0.96f, 0.90f, 0.78f, 0.78f);
        [SerializeField] private Color hoverRingColor = new Color(1f, 0.84f, 0.48f, 0.96f);
        [SerializeField] private Color activeRingColor = new Color(1f, 0.76f, 0.32f, 1f);
        [SerializeField] private Color idleDotColor = new Color(0.98f, 0.94f, 0.85f, 0.92f);
        [SerializeField] private Color activeDotColor = new Color(1f, 0.88f, 0.56f, 1f);

        [Header("Анимация отклика")]
        [SerializeField, Range(0.8f, 1.8f)] private float hoverRingScale = 1.24f;
        [SerializeField, Range(0.8f, 1.8f)] private float activeRingScale = 1.15f;
        [SerializeField, Min(1f)] private float transitionSpeed = 16f;
        [SerializeField, Range(0f, 0.35f)] private float clickPunchAmount = 0.16f;

        private RectTransform _canvasRect;
        private bool _systemCursorOverride;
        private bool _hasFocus = true;
        private float _currentRingScale = 1f;
        private float _clickPunch;
        private Color _currentRingColor;
        private Color _currentDotColor;
        private string _lastLabelText = string.Empty;
        private float _hoverLabelAlpha;

        public bool IsSystemCursorOverride => _systemCursorOverride;

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

            if (reticleCanvas == null)
            {
                reticleCanvas = GetComponent<Canvas>();
            }

            if (reticleCanvas != null)
            {
                _canvasRect = reticleCanvas.transform as RectTransform;
            }

            if (reticleRoot != null)
            {
                reticleRoot.sizeDelta = new Vector2(baseSizePx, baseSizePx);
            }

            if (hoverLabelCanvasGroup != null)
            {
                hoverLabelCanvasGroup.alpha = 0f;
            }

            _currentRingColor = idleRingColor;
            _currentDotColor = idleDotColor;
            ApplyColorsAndScaleImmediate();
        }

        private void OnEnable()
        {
            _hasFocus = Application.isFocused;
            SyncCursorState(mouseInsideViewport: true);
        }

        private void OnDisable()
        {
            RestoreSystemCursor();
        }

        private void OnDestroy()
        {
            RestoreSystemCursor();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            _hasFocus = hasFocus;
            if (!hasFocus)
            {
                RestoreSystemCursor();
            }
            else
            {
                SyncCursorState(mouseInsideViewport: true);
            }
        }

        /// <summary>
        /// Позволяет временно вернуть системный курсор ОС (например, при открытии меню паузы).
        /// </summary>
        public void SetSystemCursorOverride(bool showSystemCursor)
        {
            _systemCursorOverride = showSystemCursor;
            if (_systemCursorOverride)
            {
                RestoreSystemCursor();
            }
            else
            {
                SyncCursorState(mouseInsideViewport: true);
            }
        }

        private void LateUpdate()
        {
            if (!_hasFocus || _systemCursorOverride)
            {
                SetReticleVisible(false);
                return;
            }

            if (!TryReadMouseScreenPosition(out Vector2 screenPos))
            {
                SetReticleVisible(false);
                return;
            }

            bool insideViewport =
                screenPos.x >= 0f && screenPos.x <= Screen.width &&
                screenPos.y >= 0f && screenPos.y <= Screen.height;

            SyncCursorState(insideViewport);
            if (!insideViewport)
            {
                return;
            }

            UpdateReticlePosition(screenPos, out Vector2 reticleLocalPos);
            UpdateReticleVisualFeedback(screenPos, reticleLocalPos);
        }

        private void SyncCursorState(bool mouseInsideViewport)
        {
            bool useCustomReticle = _hasFocus && !_systemCursorOverride && mouseInsideViewport;

            if (useCustomReticle)
            {
                if (Cursor.visible)
                {
                    Cursor.visible = false;
                }

                CursorLockMode desiredLock = (!Application.isEditor && confineCursorInBuild)
                    ? CursorLockMode.Confined
                    : CursorLockMode.None;

                if (Cursor.lockState != desiredLock)
                {
                    Cursor.lockState = desiredLock;
                }

                SetReticleVisible(true);
            }
            else
            {
                if (!Cursor.visible)
                {
                    Cursor.visible = true;
                }

                if (Cursor.lockState != CursorLockMode.None)
                {
                    Cursor.lockState = CursorLockMode.None;
                }

                SetReticleVisible(false);
            }
        }

        private void RestoreSystemCursor()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            SetReticleVisible(false);
        }

        private void SetReticleVisible(bool visible)
        {
            if (reticleRoot != null && reticleRoot.gameObject.activeSelf != visible)
            {
                reticleRoot.gameObject.SetActive(visible);
            }

            if (hoverLabelRoot != null && !visible && hoverLabelRoot.gameObject.activeSelf)
            {
                hoverLabelRoot.gameObject.SetActive(false);
                _hoverLabelAlpha = 0f;
            }
        }

        private void UpdateReticlePosition(Vector2 screenPos, out Vector2 localPos)
        {
            localPos = Vector2.zero;
            if (reticleRoot == null || _canvasRect == null)
            {
                return;
            }

            Camera uiCamera = reticleCanvas != null && reticleCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? reticleCanvas.worldCamera
                : null;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPos, uiCamera, out localPos))
            {
                reticleRoot.anchoredPosition = localPos;
            }
        }

        private void UpdateReticleVisualFeedback(Vector2 screenPos, Vector2 reticleLocalPos)
        {
            if (WasLeftMousePressed())
            {
                _clickPunch = 1f;
            }

            float dt = Time.unscaledDeltaTime;
            _clickPunch = Mathf.MoveTowards(_clickPunch, 0f, dt * 7f);

            ReticleState state = EvaluateReticleState(screenPos, out string hoverText);

            float targetScale = state switch
            {
                ReticleState.Active => activeRingScale,
                ReticleState.Hover => hoverRingScale,
                _ => 1f
            };
            targetScale *= (1f - _clickPunch * clickPunchAmount);

            Color targetRingColor = state switch
            {
                ReticleState.Active => activeRingColor,
                ReticleState.Hover => hoverRingColor,
                _ => idleRingColor
            };

            Color targetDotColor = state == ReticleState.Idle ? idleDotColor : activeDotColor;

            float t = 1f - Mathf.Exp(-transitionSpeed * dt);
            _currentRingScale = Mathf.Lerp(_currentRingScale, targetScale, t);
            _currentRingColor = Color.Lerp(_currentRingColor, targetRingColor, t);
            _currentDotColor = Color.Lerp(_currentDotColor, targetDotColor, t);

            ApplyColorsAndScaleImmediate();
            UpdateHoverLabel(hoverText, reticleLocalPos, dt);
        }

        private void UpdateHoverLabel(string hoverText, Vector2 reticleLocalPos, float dt)
        {
            if (hoverLabelRoot == null || hoverLabelText == null || hoverLabelCanvasGroup == null)
            {
                return;
            }

            bool hasText = !string.IsNullOrEmpty(hoverText);
            if (hasText && hoverText != _lastLabelText)
            {
                _lastLabelText = hoverText;
                hoverLabelText.text = hoverText;
                LayoutRebuilder.ForceRebuildLayoutImmediate(hoverLabelRoot);
            }

            float targetAlpha = hasText ? 1f : 0f;
            _hoverLabelAlpha = Mathf.MoveTowards(_hoverLabelAlpha, targetAlpha, dt * hoverLabelFadeSpeed);

            bool shouldShow = _hoverLabelAlpha > 0.01f;
            if (hoverLabelRoot.gameObject.activeSelf != shouldShow)
            {
                hoverLabelRoot.gameObject.SetActive(shouldShow);
            }

            if (!shouldShow)
            {
                return;
            }

            hoverLabelCanvasGroup.alpha = _hoverLabelAlpha;

            // Позиционируем плашку рядом с прицелом и удерживаем строго внутри экрана.
            Vector2 desired = reticleLocalPos + hoverLabelOffset;
            if (_canvasRect != null)
            {
                Rect cr = _canvasRect.rect;
                Vector2 sz = hoverLabelRoot.rect.size;
                float pad = 14f;

                if (desired.x + sz.x > cr.xMax - pad)
                {
                    desired.x = reticleLocalPos.x - Mathf.Abs(hoverLabelOffset.x) - sz.x;
                }
                if (desired.y - sz.y < cr.yMin + pad)
                {
                    desired.y = reticleLocalPos.y + Mathf.Abs(hoverLabelOffset.y) + sz.y;
                }

                desired.x = Mathf.Clamp(desired.x, cr.xMin + pad, cr.xMax - sz.x - pad);
                desired.y = Mathf.Clamp(desired.y, cr.yMin + sz.y + pad, cr.yMax - pad);
            }

            hoverLabelRoot.anchoredPosition = desired;
        }

        private void ApplyColorsAndScaleImmediate()
        {
            if (ringImage != null)
            {
                ringImage.color = _currentRingColor;
                ringImage.rectTransform.localScale = new Vector3(_currentRingScale, _currentRingScale, 1f);
            }

            if (dotImage != null)
            {
                dotImage.color = _currentDotColor;
            }
        }

        private ReticleState EvaluateReticleState(Vector2 screenPos, out string hoverText)
        {
            hoverText = string.Empty;

            bool holdingPlug = switchboardInteractor != null && switchboardInteractor.ActivePlug != null;
            bool holdingRadio = radioTuner != null && radioTuner.IsHoldingKnob;

            if (holdingPlug || holdingRadio)
            {
                return ReticleState.Active;
            }

            if (letterInspect != null && letterInspect.IsReading)
            {
                return ReticleState.Idle;
            }

            Camera cam = interactionCamera != null ? interactionCamera : Camera.main;
            if (cam == null)
            {
                return ReticleState.Idle;
            }

            Ray ray = cam.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, maxHoverDistance, hoverRaycastMask, QueryTriggerInteraction.Collide))
            {
                return ReticleState.Idle;
            }

            Transform hitT = hit.transform;
            bool isCameraFocused = cameraLook != null && cameraLook.IsFocused;

            // Если открыта книга — реагируем только на левую страницу абонентов.
            if (bookInspect != null && bookInspect.IsInspecting)
            {
                if (hitT.GetComponentInParent<LogbookSubscribersPage>() != null)
                {
                    return ReticleState.Hover;
                }
                return ReticleState.Idle;
            }

            // Проверяем диегетическую подпись WorldHoverLabel (наушники, фото, письмо, радио и т.д.).
            WorldHoverLabel labelComp = hitT.GetComponentInParent<WorldHoverLabel>();
            if (labelComp != null && labelComp.isActiveAndEnabled && !string.IsNullOrEmpty(labelComp.HoverText))
            {
                if (!labelComp.HideWhileCameraFocused || !isCameraFocused)
                {
                    hoverText = labelComp.HoverText;
                }
            }

            if (hitT.GetComponentInParent<SwitchboardPlug>() != null)
            {
                return ReticleState.Hover;
            }

            if (hitT.GetComponentInParent<LetterInspectController>() != null)
            {
                return ReticleState.Hover;
            }

            if (radioTuner != null && hitT.GetComponentInParent<RadioTunerController>() != null && hitT.name.Contains("Knob"))
            {
                return ReticleState.Hover;
            }

            if (hitT.GetComponentInParent<BookInspectController>() != null)
            {
                return ReticleState.Hover;
            }

            if (mapFocus != null && !mapFocus.IsFocused && hitT.GetComponentInParent<MapCameraFocus>() != null)
            {
                return ReticleState.Hover;
            }

            if (switchboardFocus != null && !switchboardFocus.IsFocused && hitT.GetComponentInParent<SwitchboardCameraFocus>() != null)
            {
                return ReticleState.Hover;
            }

            return !string.IsNullOrEmpty(hoverText) ? ReticleState.Hover : ReticleState.Idle;
        }

        private static bool TryReadMouseScreenPosition(out Vector2 screenPosition)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                screenPosition = mouse.position.ReadValue();
                return true;
            }

            screenPosition = Vector2.zero;
            return false;
#else
            screenPosition = Input.mousePosition;
            return true;
#endif
        }

        private static bool WasLeftMousePressed()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }
    }
}
