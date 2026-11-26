using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Управляет левой страницей журнала (таблица из 10 позывных абонентов).
    /// Поддерживает наведение и клик по каждой строке по UV-координатам меша страницы,
    /// янтарную вспышку подтверждения, звук карандаша и рассылку событий (для первого диалога и сюжета).
    /// </summary>
    public sealed class LogbookSubscribersPage : MonoBehaviour
    {
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private const int TexWidth = 1024;
        private const int TexHeight = 1430;
        private const float TableX0 = 56f;
        private const float TableWidth = 912f;
        private const float RowStartY = 238f;
        private const float RowHeight = 106f;

        private static readonly (string id, string displayName)[] Subscribers =
        {
            ("mayak",    "Маяк"),
            ("bereza",   "Берёза"),
            ("sokol2",   "Сокол-2"),
            ("kobra",    "Кобра"),
            ("grom",     "Гром"),
            ("yozh",     "Ёж"),
            ("shilo",    "Шило"),
            ("romashka", "Ромашка"),
            ("kuznitsa", "Кузница"),
            ("yakor",    "Якорь"),
        };

        [Serializable]
        public sealed class StringUnityEvent : UnityEvent<string> { }

        [Header("Ссылки")]
        [SerializeField] private BookInspectController bookInspectController;
        [SerializeField] private LogbookPencilAnimator pencilAnimator;
        [SerializeField] private Camera interactionCamera;
        [SerializeField] private MeshRenderer leftPageRenderer;
        [SerializeField] private Texture2D basePageTexture;

        [Header("Настройки взаимодействия")]
        [SerializeField] private LayerMask raycastMask = ~0;
        [SerializeField, Min(0.5f)] private float maxRayDistance = 10f;
        [SerializeField, Min(0.2f)] private float clickFlashDuration = 0.85f;
        [SerializeField] private bool logClicksToConsole = true;

        [Header("События клика по абоненту")]
        [SerializeField] private StringUnityEvent onSubscriberClickedEvent = new StringUnityEvent();

        private RenderTexture _pageRenderTexture;
        private GameObject _offscreenRig;
        private Camera _offscreenCamera;
        private RectTransform _hoverHighlightRect;
        private Image _hoverHighlightImage;
        private RectTransform _flashHighlightRect;
        private Image _flashHighlightImage;
        private RectTransform _berezaStrikeRect;

        private int _hoveredRowIndex = -1;
        private int _flashedRowIndex = -1;
        private float _flashStrength;
        private bool _berezaCrossedOut;
        private bool _dirty;

        public string LastClickedSubscriberId { get; private set; }
        public Texture PageTexture => _pageRenderTexture;
        public bool AllowPageInteraction { get; set; } = true;

        /// <summary>Срабатывает при клике по строке абонента: (id позывного, отображаемое имя).</summary>
        public event Action<string, string> OnSubscriberClicked;

        private void Awake()
        {
            if (leftPageRenderer == null)
            {
                leftPageRenderer = GetComponent<MeshRenderer>();
            }

            if (interactionCamera == null)
            {
                interactionCamera = Camera.main;
            }

            if (bookInspectController == null)
            {
                bookInspectController = GetComponentInParent<BookInspectController>();
            }

            BuildOffscreenCanvas();
            RedrawPage();
        }

        private void OnDestroy()
        {
            if (_offscreenRig != null)
            {
                Destroy(_offscreenRig);
            }

            if (_pageRenderTexture != null)
            {
                _pageRenderTexture.Release();
                Destroy(_pageRenderTexture);
            }
        }

        private void Update()
        {
            if (_flashStrength > 0f)
            {
                _flashStrength = Mathf.MoveTowards(_flashStrength, 0f, Time.unscaledDeltaTime / Mathf.Max(0.1f, clickFlashDuration));
                _dirty = true;
            }

            bool isBookOpen = (bookInspectController == null || bookInspectController.IsInspecting) && AllowPageInteraction;
            if (!isBookOpen)
            {
                if (_hoveredRowIndex != -1)
                {
                    _hoveredRowIndex = -1;
                    _dirty = true;
                }
            }
            else
            {
                UpdateHoverAndClick();
            }

            if (_dirty)
            {
                RedrawPage();
                _dirty = false;
            }
        }

        /// <summary>
        /// Зачёркивает или восстанавливает строку «Берёзы» в таблице абонентов.
        /// </summary>
        public void SetBerezaCrossedOut(bool crossedOut)
        {
            if (_berezaCrossedOut == crossedOut)
            {
                return;
            }

            _berezaCrossedOut = crossedOut;
            _dirty = true;
        }

        private void UpdateHoverAndClick()
        {
            Camera cam = interactionCamera != null ? interactionCamera : Camera.main;
            if (cam == null || !TryGetMousePosition(out Vector2 mousePos))
            {
                return;
            }

            int nextRow = -1;
            Ray ray = cam.ScreenPointToRay(mousePos);
            if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, raycastMask, QueryTriggerInteraction.Collide))
            {
                if (hit.transform == transform || hit.transform.IsChildOf(transform))
                {
                    nextRow = ResolveRowIndexFromUV(hit.textureCoord);
                }
            }

            if (nextRow != _hoveredRowIndex)
            {
                _hoveredRowIndex = nextRow;
                _dirty = true;
            }

            if (nextRow >= 0 && WasLeftMousePressed())
            {
                var sub = Subscribers[nextRow];
                _flashedRowIndex = nextRow;
                _flashStrength = 1f;
                LastClickedSubscriberId = sub.id;
                _dirty = true;

                if (pencilAnimator != null)
                {
                    pencilAnimator.PlayShortScratchSound();
                }

                if (logClicksToConsole)
                {
                    Debug.Log($"[Logbook] Выбран абонент: {sub.displayName} ({sub.id})");
                }

                OnSubscriberClicked?.Invoke(sub.id, sub.displayName);
                onSubscriberClickedEvent?.Invoke(sub.id);
            }
        }

        private static int ResolveRowIndexFromUV(Vector2 uv)
        {
            float px = uv.x * TexWidth;
            float py = (1f - uv.y) * TexHeight;

            if (px < TableX0 || px > TableX0 + TableWidth)
            {
                return -1;
            }

            if (py < RowStartY || py >= RowStartY + Subscribers.Length * RowHeight)
            {
                return -1;
            }

            int idx = Mathf.FloorToInt((py - RowStartY) / RowHeight);
            return idx >= 0 && idx < Subscribers.Length ? idx : -1;
        }

        private void BuildOffscreenCanvas()
        {
            _pageRenderTexture = new RenderTexture(TexWidth, TexHeight, 16, RenderTextureFormat.ARGB32)
            {
                name = "RT_Logbook_LeftPage",
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
                useMipMap = true,
                autoGenerateMips = true
            };
            _pageRenderTexture.Create();

            _offscreenRig = new GameObject("Offscreen_LogbookLeftPageRig");
            _offscreenRig.transform.position = new Vector3(-420f, -500f, 0f);

            GameObject camGo = new GameObject("PageCamera");
            camGo.transform.SetParent(_offscreenRig.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0f, -10f);
            _offscreenCamera = camGo.AddComponent<Camera>();
            _offscreenCamera.orthographic = true;
            _offscreenCamera.orthographicSize = TexHeight * 0.5f;
            _offscreenCamera.nearClipPlane = 0.1f;
            _offscreenCamera.farClipPlane = 50f;
            _offscreenCamera.clearFlags = CameraClearFlags.SolidColor;
            _offscreenCamera.backgroundColor = new Color(0.87f, 0.81f, 0.68f, 1f);
            _offscreenCamera.cullingMask = 1 << 5; // UI layer
            _offscreenCamera.targetTexture = _pageRenderTexture;
            _offscreenCamera.enabled = false;

            GameObject canvasGo = new GameObject("PageCanvas");
            canvasGo.layer = 5;
            canvasGo.transform.SetParent(_offscreenRig.transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _offscreenCamera;
            canvas.planeDistance = 10f;

            RectTransform canvasRect = canvasGo.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(TexWidth, TexHeight);

            // Базовая текстура таблицы позывных.
            GameObject bgGo = new GameObject("BaseTexture");
            bgGo.layer = 5;
            bgGo.transform.SetParent(canvasGo.transform, false);
            RawImage bgImg = bgGo.AddComponent<RawImage>();
            bgImg.texture = basePageTexture;
            RectTransform bgRect = bgGo.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // Мягкая подсветка при наведении.
            _hoverHighlightRect = CreateTopLeftRect("HoverHighlight", canvasGo.transform, out _hoverHighlightImage);
            _hoverHighlightImage.color = new Color(0.86f, 0.60f, 0.26f, 0.22f);
            _hoverHighlightRect.gameObject.SetActive(false);

            // Янтарная вспышка при клике по строке.
            _flashHighlightRect = CreateTopLeftRect("FlashHighlight", canvasGo.transform, out _flashHighlightImage);
            _flashHighlightImage.color = new Color(0.95f, 0.64f, 0.24f, 0f);
            _flashHighlightRect.gameObject.SetActive(false);

            // Линия зачёркивания «Берёзы» (индекс 1).
            _berezaStrikeRect = CreateTopLeftRect("BerezaStrike", canvasGo.transform, out Image strikeImg);
            strikeImg.color = new Color(0.11f, 0.09f, 0.07f, 0.88f);
            SetRowRect(_berezaStrikeRect, TableX0 + 8f, RowStartY + RowHeight + 48f, TableWidth - 16f, 6f);
            _berezaStrikeRect.gameObject.SetActive(false);

            if (leftPageRenderer != null)
            {
                Material runtimeMat = new Material(leftPageRenderer.sharedMaterial);
                if (runtimeMat.HasProperty(BaseMapId)) runtimeMat.SetTexture(BaseMapId, _pageRenderTexture);
                if (runtimeMat.HasProperty(MainTexId)) runtimeMat.SetTexture(MainTexId, _pageRenderTexture);
                leftPageRenderer.material = runtimeMat;
            }
        }

        private void RedrawPage()
        {
            if (_offscreenCamera == null)
            {
                return;
            }

            bool showHover = _hoveredRowIndex >= 0 && (_hoveredRowIndex != _flashedRowIndex || _flashStrength <= 0.05f);
            _hoverHighlightRect.gameObject.SetActive(showHover);
            if (showHover)
            {
                SetRowRect(_hoverHighlightRect, TableX0, RowStartY + _hoveredRowIndex * RowHeight, TableWidth, RowHeight - 3f);
            }

            bool showFlash = _flashedRowIndex >= 0 && _flashStrength > 0.01f;
            _flashHighlightRect.gameObject.SetActive(showFlash);
            if (showFlash)
            {
                SetRowRect(_flashHighlightRect, TableX0, RowStartY + _flashedRowIndex * RowHeight, TableWidth, RowHeight - 3f);
                _flashHighlightImage.color = new Color(0.95f, 0.64f, 0.24f, 0.45f * _flashStrength);
            }

            _berezaStrikeRect.gameObject.SetActive(_berezaCrossedOut);
            _offscreenCamera.Render();
        }

        private static RectTransform CreateTopLeftRect(string name, Transform parent, out Image image)
        {
            GameObject go = new GameObject(name);
            go.layer = 5;
            go.transform.SetParent(parent, false);
            image = go.AddComponent<Image>();
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            return rt;
        }

        private static void SetRowRect(RectTransform rt, float x, float yFromTop, float width, float height)
        {
            rt.anchoredPosition = new Vector2(x, -yFromTop);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static bool TryGetMousePosition(out Vector2 mousePosition)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                mousePosition = mouse.position.ReadValue();
                return true;
            }

            mousePosition = Vector2.zero;
            return false;
#else
            mousePosition = Input.mousePosition;
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
