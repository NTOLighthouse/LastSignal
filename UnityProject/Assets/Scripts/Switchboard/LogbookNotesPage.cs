using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Управляет правой страницей журнала (утраченные узлы + рукописные заметки ефр. Ларина на полях).
    /// При добавлении заметки через AddNote(text) запускает анимацию карандаша (LogbookPencilAnimator),
    /// который поднимается, водит по нужной строке со звуком письма и кладётся обратно на стол,
    /// пока текст плавно проявляется прямо под его остриём.
    /// </summary>
    public sealed class LogbookNotesPage : MonoBehaviour
    {
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private const int TexWidth = 1024;
        private const int TexHeight = 1430;
        private const float NotesStartX = 86f;
        private const float NotesWidth = 852f;
        private const float FirstNoteY = 562f;
        private const float NoteRowPitch = 86f;
        private const int MaxVisibleNotes = 8;

        private sealed class NoteEntry
        {
            public string FullText;
            public float Progress; // 0..1
            public Text UiText;
        }

        [Header("Ссылки")]
        [SerializeField] private LogbookPencilAnimator pencilAnimator;
        [SerializeField] private MeshRenderer rightPageRenderer;
        [SerializeField] private Texture2D basePageTexture;

        [Header("Параметры появления заметок")]
        [SerializeField, Min(0.4f)] private float writeDurationPerNote = 1.35f;

        [Header("Начальные заметки (опционально, видны сразу)")]
        [SerializeField] private List<string> initialInstantNotes = new List<string>();

        [Header("Заготовки заметок для быстрого теста (клавиша N или ContextMenu)")]
        [SerializeField] private bool enableTestHotKey = true;
        [SerializeField] private List<string> presetNotes = new List<string>
        {
            "Нет в книге — нет в эфире",
            "Мост: «Якорю — рассвет» + пароль суток",
            "Особисты в эфир не ходят. Приходят ногами и с бумагой",
            "Свой адрес — в эфир ни слова. Чужим нужен для одного",
            "Кобра — «Работайте». Гром — координаты. Ромашка — «пожалуйста»"
        };

        private readonly List<NoteEntry> _notes = new List<NoteEntry>();
        private readonly Queue<string> _pendingNoteQueue = new Queue<string>();

        private RenderTexture _pageRenderTexture;
        private GameObject _offscreenRig;
        private Camera _offscreenCamera;
        private Transform _notesContainer;
        private Font _handwritingFont;
        private bool _isWritingFromQueue;
        private int _nextPresetIndex;

        public int NoteCount => _notes.Count;

        private void Awake()
        {
            if (rightPageRenderer == null)
            {
                rightPageRenderer = GetComponent<MeshRenderer>();
            }

            ResolveFont();
            BuildOffscreenCanvas();

            for (int i = 0; i < initialInstantNotes.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(initialInstantNotes[i]))
                {
                    AddNoteInstant(initialInstantNotes[i]);
                }
            }

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
            if (enableTestHotKey && WasTestKeyPressed())
            {
                WriteNextPresetNote();
            }
        }

        /// <summary>
        /// Добавляет новую рукописную заметку с анимацией карандаша и звуком письма по бумаге.
        /// Если карандаш уже пишет предыдущую заметку — ставит новую в очередь.
        /// </summary>
        public void AddNote(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || HasNote(text))
            {
                return;
            }

            _pendingNoteQueue.Enqueue(text.Trim());
            TryStartNextQueuedNote();
        }

        /// <summary>
        /// Мгновенно вписывает заметку на страницу без анимации карандаша.
        /// </summary>
        public void AddNoteInstant(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || HasNote(text))
            {
                return;
            }

            CreateNoteEntry(text.Trim(), 1f);
            RedrawPage();
        }

        /// <summary>
        /// Очищает все заметки на правой странице.
        /// </summary>
        public void ClearNotes()
        {
            _pendingNoteQueue.Clear();
            for (int i = 0; i < _notes.Count; i++)
            {
                if (_notes[i].UiText != null)
                {
                    Destroy(_notes[i].UiText.gameObject);
                }
            }

            _notes.Clear();
            RedrawPage();
        }

        /// <summary>
        /// Пишет следующую заготовленную заметку из списка presetNotes (удобно для проверки анимации).
        /// </summary>
        [ContextMenu("Тест: написать следующую заметку")]
        public void WriteNextPresetNote()
        {
            if (presetNotes == null || presetNotes.Count == 0)
            {
                return;
            }

            while (_nextPresetIndex < presetNotes.Count)
            {
                string candidate = presetNotes[_nextPresetIndex++];
                if (!string.IsNullOrWhiteSpace(candidate) && !HasNote(candidate))
                {
                    AddNote(candidate);
                    return;
                }
            }
        }

        private bool HasNote(string text)
        {
            string trimmed = text.Trim();
            for (int i = 0; i < _notes.Count; i++)
            {
                if (string.Equals(_notes[i].FullText, trimmed, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            foreach (string queued in _pendingNoteQueue)
            {
                if (string.Equals(queued, trimmed, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void TryStartNextQueuedNote()
        {
            if (_isWritingFromQueue || _pendingNoteQueue.Count == 0)
            {
                return;
            }

            string text = _pendingNoteQueue.Dequeue();
            int rowIndex = _notes.Count;
            NoteEntry entry = CreateNoteEntry(text, 0f);

            if (pencilAnimator == null)
            {
                entry.Progress = 1f;
                UpdateNoteText(entry);
                RedrawPage();
                TryStartNextQueuedNote();
                return;
            }

            _isWritingFromQueue = true;
            ComputeLocalLineBoundsOnPage(rowIndex, out Vector3 localStart, out Vector3 localEnd);

            pencilAnimator.PlayWriteAnimation(
                transform,
                localStart,
                localEnd,
                writeDurationPerNote,
                onWriteProgress: progress =>
                {
                    entry.Progress = progress;
                    UpdateNoteText(entry);
                    RedrawPage();
                },
                onCompleted: () =>
                {
                    entry.Progress = 1f;
                    UpdateNoteText(entry);
                    RedrawPage();
                    _isWritingFromQueue = false;
                    TryStartNextQueuedNote();
                });
        }

        private NoteEntry CreateNoteEntry(string text, float initialProgress)
        {
            int index = Mathf.Min(_notes.Count, MaxVisibleNotes - 1);

            GameObject go = new GameObject($"Note_{_notes.Count}");
            go.layer = 5;
            go.transform.SetParent(_notesContainer, false);

            Text uiText = go.AddComponent<Text>();
            uiText.font = _handwritingFont;
            uiText.fontSize = 31;
            uiText.fontStyle = FontStyle.Italic;
            uiText.color = new Color(0.14f, 0.14f, 0.17f, 0.92f);
            uiText.alignment = TextAnchor.UpperLeft;
            uiText.horizontalOverflow = HorizontalWrapMode.Wrap;
            uiText.verticalOverflow = VerticalWrapMode.Overflow;
            uiText.lineSpacing = 1.05f;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(NotesStartX, -(FirstNoteY + index * NoteRowPitch));
            rt.sizeDelta = new Vector2(NotesWidth, NoteRowPitch * 1.4f);
            rt.localRotation = Quaternion.Euler(0f, 0f, -0.5f + (index % 2) * 0.9f);

            var entry = new NoteEntry
            {
                FullText = text,
                Progress = initialProgress,
                UiText = uiText
            };

            _notes.Add(entry);
            UpdateNoteText(entry);
            return entry;
        }

        private static void UpdateNoteText(NoteEntry entry)
        {
            if (entry.UiText == null)
            {
                return;
            }

            if (entry.Progress <= 0.001f)
            {
                entry.UiText.text = string.Empty;
                return;
            }

            string prefixed = "— " + entry.FullText;
            int visibleChars = Mathf.Clamp(Mathf.CeilToInt(prefixed.Length * entry.Progress), 1, prefixed.Length);
            entry.UiText.text = prefixed.Substring(0, visibleChars);
        }

        /// <summary>
        /// Вычисляет локальные 3D-координаты начала и конца строки на меше правой страницы
        /// (Logbook_Page_Right.001: x = [-0.1075..+0.1075] снизу вверх, y = [+0.002..+0.156] слева направо, z ≈ +0.025 поверхность).
        /// </summary>
        private static void ComputeLocalLineBoundsOnPage(int rowIndex, out Vector3 localStart, out Vector3 localEnd)
        {
            int clampedRow = Mathf.Clamp(rowIndex, 0, MaxVisibleNotes - 1);
            float yPx = FirstNoteY + clampedRow * NoteRowPitch + 24f;
            float v = 1f - (yPx / TexHeight);

            float localX = Mathf.Lerp(-0.1075f, 0.1075f, v);
            float localYStart = Mathf.Lerp(0.002f, 0.156f, 0.11f);
            float localYEnd = Mathf.Lerp(0.002f, 0.156f, 0.86f);

            // Поверхность изогнутой страницы: около корешка z ≈ 0.015, ближе к внешнему краю z ≈ 0.026.
            localStart = new Vector3(localX, localYStart, 0.019f);
            localEnd = new Vector3(localX, localYEnd, 0.026f);
        }

        private void BuildOffscreenCanvas()
        {
            _pageRenderTexture = new RenderTexture(TexWidth, TexHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = "RT_Logbook_RightPage",
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
                useMipMap = true,
                autoGenerateMips = true
            };
            _pageRenderTexture.Create();

            _offscreenRig = new GameObject("Offscreen_LogbookRightPageRig");
            _offscreenRig.transform.position = new Vector3(-280f, -500f, 0f);

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
            _offscreenCamera.cullingMask = 1 << 5;
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

            GameObject notesGo = new GameObject("NotesContainer");
            notesGo.layer = 5;
            notesGo.transform.SetParent(canvasGo.transform, false);
            RectTransform notesRt = notesGo.AddComponent<RectTransform>();
            notesRt.anchorMin = Vector2.zero;
            notesRt.anchorMax = Vector2.one;
            notesRt.offsetMin = Vector2.zero;
            notesRt.offsetMax = Vector2.zero;
            _notesContainer = notesGo.transform;

            if (rightPageRenderer != null)
            {
                Material runtimeMat = new Material(rightPageRenderer.sharedMaterial);
                if (runtimeMat.HasProperty(BaseMapId)) runtimeMat.SetTexture(BaseMapId, _pageRenderTexture);
                if (runtimeMat.HasProperty(MainTexId)) runtimeMat.SetTexture(MainTexId, _pageRenderTexture);
                rightPageRenderer.material = runtimeMat;
            }
        }

        private void RedrawPage()
        {
            if (_offscreenCamera != null)
            {
                _offscreenCamera.Render();
            }
        }

        private void ResolveFont()
        {
            _handwritingFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Georgia", "Times New Roman", "Segoe Print", "Arial" }, 32);

            if (_handwritingFont == null)
            {
                _handwritingFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
        }

        private static bool WasTestKeyPressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            return kb != null && kb.nKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.N);
#endif
        }
    }
}
