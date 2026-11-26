using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LastSignal.StoryCore
{
    /// <summary>
    /// Модель отображения одного варианта ответа Ларина в UI субтитров.
    /// </summary>
    public sealed class SubtitleChoiceView
    {
        public int Index;
        public string Text;
        public bool Interactable;
        public string LockedReason;
    }

    /// <summary>
    /// Компактный атмосферный UI субтитров в духе Metro 2033:
    /// - полупрозрачное дымчатое тёмное облако без жёстких рамок в самом низу экрана (не перекрывает книгу);
    /// - посимвольное появление ВСЕХ текстов (имя говорящего, реплика, подсказка действия, варианты ответа);
    /// - кликабельные подсвеченные слова ([текст|id]) через нативные TMP-ссылки;
    /// - мгновенное скрытие/возврат без потери прогресса при паузе выдёргиванием штекера.
    /// </summary>
    public sealed class SubtitleDialogueUI : MonoBehaviour, IPointerClickHandler
    {
        private static readonly Regex KeywordTokenRegex = new Regex(@"\[([^|\]]+)\|([^\]]+)\]", RegexOptions.Compiled);

        [Header("Ссылки на элементы UI (uGUI + TMP)")]
        [SerializeField] private UnityEngine.CanvasGroup rootCanvasGroup;
        [SerializeField] private UnityEngine.GameObject subtitlePanel;
        [SerializeField] private TMP_Text speakerNameText;
        [SerializeField] private TMP_Text subtitleBodyText;

        [Header("Подсказка действия")]
        [SerializeField] private UnityEngine.GameObject actionHintPanel;
        [SerializeField] private TMP_Text actionHintText;

        [Header("Панель выборов Ларина")]
        [SerializeField] private UnityEngine.Transform choicesContainer;
        [SerializeField] private UnityEngine.UI.Button choiceButtonTemplate;

        [Header("Посимвольное появление текста (Typewriter)")]
        [Tooltip("Базовая скорость печати субтитров (символов в секунду), если реплика без озвучки.")]
        [SerializeField, Min(8f)] private float defaultCharactersPerSecond = 44f;
        [SerializeField, Min(15f)] private float choiceCharactersPerSecond = 85f;
        [SerializeField, Min(15f)] private float hintCharactersPerSecond = 55f;

        [Header("Цвета кликабельных слов в субтитрах")]
        [SerializeField] private string keywordHighlightHex = "#F5C257";
        [SerializeField] private string keywordRecordedHex = "#9AD69A";

        [Header("Плавность проявления облака")]
        [SerializeField, Min(1f)] private float fadeSpeed = 12f;

        private readonly Dictionary<string, string> _currentLineKeywords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<UnityEngine.UI.Button> _spawnedChoiceButtons = new List<UnityEngine.UI.Button>();
        private readonly List<TMP_Text> _spawnedChoiceLabels = new List<TMP_Text>();

        private string _lastRawSubtitle = string.Empty;
        private float _targetAlpha;
        private bool _pausedHidden;
        private Canvas _parentCanvas;

        // Текущее состояние посимвольной печати
        private float _bodyRevealProgress;
        private int _bodyTotalChars;
        private float _activeBodyCharsPerSec = 44f;

        private float _speakerRevealProgress;
        private int _speakerTotalChars;

        private float _hintRevealProgress;
        private int _hintTotalChars;

        private float _choicesRevealTime;

        public bool IsLineRevealFinished => _bodyRevealProgress >= _bodyTotalChars;

        /// <summary>Вызывается при клике игрока по подсвеченному слову в субтитрах (keywordId, displayText).</summary>
        public event Action<string, string> OnKeywordClicked;

        /// <summary>Вызывается при выборе кнопки ответа Ларина (index).</summary>
        public event Action<int> OnChoiceSelected;

        private void Awake()
        {
            _parentCanvas = GetComponentInParent<Canvas>();

            if (choiceButtonTemplate != null)
            {
                choiceButtonTemplate.gameObject.SetActive(false);
            }

            if (subtitleBodyText != null)
            {
                subtitleBodyText.raycastTarget = true;
            }

            HideAll();
            if (rootCanvasGroup != null)
            {
                rootCanvasGroup.alpha = 0f;
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (rootCanvasGroup != null)
            {
                float desiredAlpha = _pausedHidden ? 0f : _targetAlpha;
                if (!Mathf.Approximately(rootCanvasGroup.alpha, desiredAlpha))
                {
                    rootCanvasGroup.alpha = Mathf.MoveTowards(rootCanvasGroup.alpha, desiredAlpha, dt * fadeSpeed);
                }

                bool interactive = !_pausedHidden && rootCanvasGroup.alpha > 0.05f;
                rootCanvasGroup.blocksRaycasts = interactive;
                rootCanvasGroup.interactable = interactive;
            }

            if (_pausedHidden)
            {
                return;
            }

            UpdateTypewriterReveals(dt);
        }

        /// <summary>
        /// Показывает реплику абонента с плавным посимвольным появлением (адаптирует скорость под длительность озвучки).
        /// </summary>
        public void ShowLine(
            string speakerName,
            Color speakerColor,
            string rawSubtitleText,
            Func<string, bool> isKeywordRecorded,
            float targetRevealDuration = 0f)
        {
            _lastRawSubtitle = rawSubtitleText ?? string.Empty;
            _targetAlpha = 1f;

            if (subtitlePanel != null)
            {
                subtitlePanel.SetActive(true);
            }

            if (speakerNameText != null)
            {
                string cleanSpeaker = string.IsNullOrWhiteSpace(speakerName) ? string.Empty : $"·  {speakerName.ToUpperInvariant()}";
                speakerNameText.text = cleanSpeaker;
                speakerNameText.color = speakerColor;
                speakerNameText.gameObject.SetActive(!string.IsNullOrWhiteSpace(speakerName));
                speakerNameText.ForceMeshUpdate();
                _speakerTotalChars = speakerNameText.textInfo.characterCount;
                _speakerRevealProgress = 0f;
                speakerNameText.maxVisibleCharacters = 0;
            }

            RenderFormattedSubtitle(isKeywordRecorded, resetTypewriter: true, targetRevealDuration: targetRevealDuration);
        }

        /// <summary>
        /// Временно прячет или возвращает UI субтитров при выдёргивании/обратном подключении штекера
        /// без сброса текста, выбора и прогресса печати.
        /// </summary>
        public void SetPausedHidden(bool hidden)
        {
            _pausedHidden = hidden;
        }

        /// <summary>
        /// Обновляет цвет кликабельного слова (жёлтое -> записанное зелёное) без перезапуска печати текста.
        /// </summary>
        public void RefreshRecordedKeywords(Func<string, bool> isKeywordRecorded)
        {
            if (!string.IsNullOrEmpty(_lastRawSubtitle))
            {
                RenderFormattedSubtitle(isKeywordRecorded, resetTypewriter: false, targetRevealDuration: 0f);
            }
        }

        public void ShowChoices(IReadOnlyList<SubtitleChoiceView> choices)
        {
            HideChoices();
            if (choices == null || choices.Count == 0 || choicesContainer == null || choiceButtonTemplate == null)
            {
                return;
            }

            _targetAlpha = 1f;
            _choicesRevealTime = 0f;
            choicesContainer.gameObject.SetActive(true);

            for (int i = 0; i < choices.Count; i++)
            {
                SubtitleChoiceView view = choices[i];
                UnityEngine.UI.Button btn = Instantiate(choiceButtonTemplate, choicesContainer);
                btn.gameObject.name = $"ChoiceBtn_{i}";
                btn.gameObject.SetActive(true);
                btn.interactable = view.Interactable;

                TMP_Text label = btn.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.text = view.Interactable
                        ? $"—  {view.Text}"
                        : $"[×]  {view.Text}  <color=#9E8B74><i>({view.LockedReason})</i></color>";
                    label.color = view.Interactable
                        ? new Color(0.96f, 0.91f, 0.80f, 1f)
                        : new Color(0.62f, 0.56f, 0.48f, 0.78f);
                    label.ForceMeshUpdate();
                    label.maxVisibleCharacters = 0;
                    _spawnedChoiceLabels.Add(label);
                }

                int capturedIndex = view.Index;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnChoiceSelected?.Invoke(capturedIndex));
                _spawnedChoiceButtons.Add(btn);
            }
        }

        public void HideChoices()
        {
            for (int i = 0; i < _spawnedChoiceButtons.Count; i++)
            {
                if (_spawnedChoiceButtons[i] != null)
                {
                    Destroy(_spawnedChoiceButtons[i].gameObject);
                }
            }
            _spawnedChoiceButtons.Clear();
            _spawnedChoiceLabels.Clear();

            if (choicesContainer != null)
            {
                choicesContainer.gameObject.SetActive(false);
            }
        }

        public void ShowActionHint(string hintText)
        {
            if (actionHintPanel == null || actionHintText == null)
            {
                return;
            }

            bool hasHint = !string.IsNullOrWhiteSpace(hintText);
            actionHintPanel.SetActive(hasHint);
            if (hasHint)
            {
                _targetAlpha = 1f;
                actionHintText.text = hintText;
                actionHintText.ForceMeshUpdate();
                _hintTotalChars = actionHintText.textInfo.characterCount;
                _hintRevealProgress = 0f;
                actionHintText.maxVisibleCharacters = 0;
            }
        }

        public void HideActionHint()
        {
            if (actionHintPanel != null)
            {
                actionHintPanel.SetActive(false);
            }
        }

        public void HideAll()
        {
            _targetAlpha = 0f;
            _pausedHidden = false;
            _lastRawSubtitle = string.Empty;
            _currentLineKeywords.Clear();
            HideChoices();
            HideActionHint();

            if (subtitlePanel != null)
            {
                subtitlePanel.SetActive(false);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_pausedHidden || subtitleBodyText == null || _currentLineKeywords.Count == 0)
            {
                return;
            }

            Camera uiCamera = (_parentCanvas != null && _parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? _parentCanvas.worldCamera
                : null;

            int linkIndex = TMP_TextUtilities.FindIntersectingLink(subtitleBodyText, eventData.position, uiCamera);
            if (linkIndex < 0)
            {
                return;
            }

            TMP_LinkInfo linkInfo = subtitleBodyText.textInfo.linkInfo[linkIndex];
            string keywordId = linkInfo.GetLinkID();
            if (!string.IsNullOrEmpty(keywordId))
            {
                string display = _currentLineKeywords.TryGetValue(keywordId, out string cached)
                    ? cached
                    : linkInfo.GetLinkText();
                OnKeywordClicked?.Invoke(keywordId, display);
            }
        }

        private void UpdateTypewriterReveals(float dt)
        {
            if (speakerNameText != null && _speakerRevealProgress < _speakerTotalChars)
            {
                _speakerRevealProgress = Mathf.Min(_speakerTotalChars, _speakerRevealProgress + dt * 75f);
                speakerNameText.maxVisibleCharacters = Mathf.CeilToInt(_speakerRevealProgress);
            }

            if (subtitleBodyText != null && _bodyRevealProgress < _bodyTotalChars)
            {
                _bodyRevealProgress = Mathf.Min(_bodyTotalChars, _bodyRevealProgress + dt * _activeBodyCharsPerSec);
                subtitleBodyText.maxVisibleCharacters = Mathf.CeilToInt(_bodyRevealProgress);
            }

            if (actionHintText != null && actionHintPanel != null && actionHintPanel.activeSelf && _hintRevealProgress < _hintTotalChars)
            {
                _hintRevealProgress = Mathf.Min(_hintTotalChars, _hintRevealProgress + dt * hintCharactersPerSecond);
                actionHintText.maxVisibleCharacters = Mathf.CeilToInt(_hintRevealProgress);
            }

            if (_spawnedChoiceLabels.Count > 0)
            {
                _choicesRevealTime += dt;
                for (int i = 0; i < _spawnedChoiceLabels.Count; i++)
                {
                    TMP_Text lbl = _spawnedChoiceLabels[i];
                    if (lbl == null) continue;
                    float localTime = Mathf.Max(0f, _choicesRevealTime - i * 0.08f);
                    int total = lbl.textInfo.characterCount;
                    int vis = Mathf.Clamp(Mathf.CeilToInt(localTime * choiceCharactersPerSecond), 0, total);
                    if (lbl.maxVisibleCharacters != vis)
                    {
                        lbl.maxVisibleCharacters = vis;
                    }
                }
            }
        }

        private void RenderFormattedSubtitle(Func<string, bool> isKeywordRecorded, bool resetTypewriter, float targetRevealDuration)
        {
            _currentLineKeywords.Clear();
            if (subtitleBodyText == null)
            {
                return;
            }

            string formatted = KeywordTokenRegex.Replace(_lastRawSubtitle, match =>
            {
                string visibleWord = match.Groups[1].Value.Trim();
                string keywordId = match.Groups[2].Value.Trim();
                _currentLineKeywords[keywordId] = visibleWord;

                bool recorded = isKeywordRecorded != null && isKeywordRecorded(keywordId);
                string hex = recorded ? keywordRecordedHex : keywordHighlightHex;
                string suffix = recorded ? " [+]" : string.Empty;
                return $"<link=\"{keywordId}\"><color={hex}><u><b>{visibleWord}{suffix}</b></u></color></link>";
            });

            bool wasComplete = !resetTypewriter && _bodyRevealProgress >= _bodyTotalChars;
            subtitleBodyText.text = formatted;
            subtitleBodyText.ForceMeshUpdate();
            _bodyTotalChars = subtitleBodyText.textInfo.characterCount;

            if (resetTypewriter)
            {
                _bodyRevealProgress = 0f;
                subtitleBodyText.maxVisibleCharacters = 0;
                if (targetRevealDuration > 0.3f && _bodyTotalChars > 0)
                {
                    // Растягиваем печать чуть быстрее длительности реплики (85%), чтобы игрок успевал читать и кликать слова.
                    _activeBodyCharsPerSec = Mathf.Clamp(_bodyTotalChars / (targetRevealDuration * 0.82f), 26f, 75f);
                }
                else
                {
                    _activeBodyCharsPerSec = defaultCharactersPerSecond;
                }
            }
            else if (wasComplete)
            {
                _bodyRevealProgress = _bodyTotalChars;
                subtitleBodyText.maxVisibleCharacters = _bodyTotalChars;
            }
            else
            {
                subtitleBodyText.maxVisibleCharacters = Mathf.Clamp(Mathf.CeilToInt(_bodyRevealProgress), 0, _bodyTotalChars);
            }
        }
    }
}
