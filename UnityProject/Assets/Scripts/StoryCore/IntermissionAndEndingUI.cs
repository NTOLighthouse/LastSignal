using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastSignal.StoryCore
{
    /// <summary>
    /// Управляет полноэкранной заставкой и титрами-машинописью на абсолютно чёрном фоне,
    /// нативно блокируя управление и скрывая сцену с первого кадра до плавного открытия,
    /// а также Экраном итогов из ГДД (лист журнала связи).
    /// </summary>
    public sealed class IntermissionAndEndingUI : MonoBehaviour
    {
        [Header("Ссылки на элементы UI")]
        [SerializeField] private UnityEngine.GameObject overlayRoot;
        [SerializeField] private UnityEngine.CanvasGroup overlayCanvasGroup;

        [Header("Поведение на старте")]
        [Tooltip("Начинать с абсолютно чёрного экрана до первого кадра, чтобы игрок не видел уровень до конца заставки.")]
        [SerializeField] private bool startOpaqueBlackOnAwake = true;

        [Header("Титры-машинопись")]
        [SerializeField] private UnityEngine.GameObject typewriterGroup;
        [SerializeField] private TMP_Text typewriterText;
        [SerializeField, Min(5f)] private float charactersPerSecond = 32f;

        [Header("Экран итогов (Лист журнала связи)")]
        [SerializeField] private UnityEngine.GameObject summaryGroup;
        [SerializeField] private TMP_Text endingTitleText;
        [SerializeField] private TMP_Text endingSubtitleText;
        [SerializeField] private TMP_Text journalRowsText;
        [SerializeField] private UnityEngine.UI.Button restartButton;

        private Coroutine _activeCoroutine;
        private Action _onRestartCallback;
        private bool _isHoldingOpaqueBlack;

        public event Action OnContinueRequested;
        public event Action OnRestartRequested;

        private void Awake()
        {
            if (restartButton != null)
            {
                restartButton.onClick.RemoveAllListeners();
                restartButton.onClick.AddListener(() =>
                {
                    OnRestartRequested?.Invoke();
                    _onRestartCallback?.Invoke();
                });
            }

            if (startOpaqueBlackOnAwake)
            {
                EngageOpaqueBlackImmediate();
            }
            else
            {
                HideImmediate();
            }
        }

        private void OnDisable()
        {
            GameplayInputGate.ReleaseBlock(this);
        }

        /// <summary>
        /// Мгновенно (без мелькания сцены) закрывает экран чистым чёрным фоном и блокирует управление.
        /// </summary>
        public void EngageOpaqueBlackImmediate()
        {
            StopActiveRoutine();
            _isHoldingOpaqueBlack = true;
            GameplayInputGate.AcquireBlock(this, hideReticle: true);

            if (overlayRoot != null) overlayRoot.SetActive(true);
            if (summaryGroup != null) summaryGroup.SetActive(false);
            if (typewriterGroup != null) typewriterGroup.SetActive(false);

            if (overlayCanvasGroup != null)
            {
                overlayCanvasGroup.alpha = 1f;
                overlayCanvasGroup.blocksRaycasts = true;
                overlayCanvasGroup.interactable = true;
            }
        }

        /// <summary>
        /// Показывает чёрный экран с посимвольной белой машинописью, затем плавно открывает подвал
        /// и разблокирует управление (с курсором строго в центре экрана).
        /// </summary>
        public Coroutine ShowTypewriterCard(string text, float holdDurationSeconds, Action onFinished = null)
        {
            StopActiveRoutine();
            _activeCoroutine = StartCoroutine(TypewriterCardRoutine(text ?? string.Empty, holdDurationSeconds, onFinished));
            return _activeCoroutine;
        }

        public void ShowEndingAndSummary(EndingType ending, StoryFlagsState flags, Action onRestart)
        {
            StopActiveRoutine();
            _isHoldingOpaqueBlack = false;
            _onRestartCallback = onRestart;

            // Блокируем игровой мир, но оставляем прицел/курсор видимым для нажатия кнопки перезапуска.
            GameplayInputGate.AcquireBlock(this, hideReticle: false);

            if (overlayRoot != null) overlayRoot.SetActive(true);
            if (typewriterGroup != null) typewriterGroup.SetActive(false);
            if (summaryGroup != null) summaryGroup.SetActive(true);

            if (overlayCanvasGroup != null)
            {
                overlayCanvasGroup.alpha = 1f;
                overlayCanvasGroup.blocksRaycasts = true;
                overlayCanvasGroup.interactable = true;
            }

            GetEndingHeader(ending, out string title, out string subtitle);
            if (endingTitleText != null) endingTitleText.text = title;
            if (endingSubtitleText != null) endingSubtitleText.text = subtitle;
            if (journalRowsText != null) journalRowsText.text = BuildJournalSummaryText(ending, flags);
        }

        public void HideImmediate()
        {
            StopActiveRoutine();
            _isHoldingOpaqueBlack = false;
            GameplayInputGate.ReleaseBlock(this);

            if (overlayCanvasGroup != null)
            {
                overlayCanvasGroup.alpha = 0f;
                overlayCanvasGroup.blocksRaycasts = false;
                overlayCanvasGroup.interactable = false;
            }

            if (typewriterGroup != null) typewriterGroup.SetActive(false);
            if (summaryGroup != null) summaryGroup.SetActive(false);
            if (overlayRoot != null) overlayRoot.SetActive(false);
        }

        private IEnumerator TypewriterCardRoutine(string fullText, float holdSeconds, Action onFinished)
        {
            GameplayInputGate.AcquireBlock(this, hideReticle: true);

            if (overlayRoot != null) overlayRoot.SetActive(true);
            if (summaryGroup != null) summaryGroup.SetActive(false);
            if (typewriterGroup != null) typewriterGroup.SetActive(true);

            if (typewriterText != null)
            {
                typewriterText.text = fullText;
                typewriterText.ForceMeshUpdate();
                typewriterText.maxVisibleCharacters = 0;
            }

            if (overlayCanvasGroup != null)
            {
                overlayCanvasGroup.blocksRaycasts = true;
                overlayCanvasGroup.interactable = true;

                // Если экран ещё не полностью чёрный (переход между ночами) — плавно уходим в абсолютный чёрный.
                if (!_isHoldingOpaqueBlack && overlayCanvasGroup.alpha < 0.99f)
                {
                    yield return FadeCanvasGroupRoutine(overlayCanvasGroup.alpha, 1f, 0.55f);
                }
                else
                {
                    overlayCanvasGroup.alpha = 1f;
                }
            }

            _isHoldingOpaqueBlack = false;

            // Короткая атмосферная пауза в полной темноте перед первыми буквами.
            yield return WaitUnscaledSeconds(0.35f);

            if (typewriterText != null)
            {
                typewriterText.ForceMeshUpdate();
                int totalChars = typewriterText.textInfo.characterCount;
                if (totalChars <= 0) totalChars = fullText.Length;

                float visible = 0f;
                while (visible < totalChars)
                {
                    if (WasSkipPressed())
                    {
                        break;
                    }

                    visible += Time.unscaledDeltaTime * charactersPerSecond;
                    typewriterText.maxVisibleCharacters = Mathf.Clamp(Mathf.FloorToInt(visible), 0, totalChars);
                    yield return null;
                }

                typewriterText.maxVisibleCharacters = totalChars;
            }

            float remaining = Mathf.Max(0.8f, holdSeconds);
            while (remaining > 0f)
            {
                if (WasSkipPressed())
                {
                    OnContinueRequested?.Invoke();
                    break;
                }

                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }

            // Плавно открываем сцену из темноты; управление и курсор в центре экрана включаются ровно по завершении заставки.
            if (overlayCanvasGroup != null)
            {
                yield return FadeCanvasGroupRoutine(1f, 0f, 0.75f);
                overlayCanvasGroup.blocksRaycasts = false;
                overlayCanvasGroup.interactable = false;
            }

            if (typewriterGroup != null) typewriterGroup.SetActive(false);
            if (overlayRoot != null) overlayRoot.SetActive(false);

            _activeCoroutine = null;
            GameplayInputGate.ReleaseBlock(this);
            onFinished?.Invoke();
        }

        private static IEnumerator WaitUnscaledSeconds(float seconds)
        {
            float t = seconds;
            while (t > 0f)
            {
                t -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private IEnumerator FadeCanvasGroupRoutine(float from, float to, float duration)
        {
            float t = 0f;
            float inv = 1f / Mathf.Max(0.05f, duration);
            while (t < 1f)
            {
                t = Mathf.Clamp01(t + Time.unscaledDeltaTime * inv);
                float s = t * t * t * (t * (t * 6f - 15f) + 10f);
                overlayCanvasGroup.alpha = Mathf.Lerp(from, to, s);
                yield return null;
            }

            overlayCanvasGroup.alpha = to;
        }

        private static void GetEndingHeader(EndingType ending, out string title, out string subtitle)
        {
            switch (ending)
            {
                case EndingType.MayakBurns:
                    title = "КОНЦОВКА I · «МАЯК ГОРИТ»";
                    subtitle = "Батальон успел перейти реку. В 06:10 в подвал впервые падает дневной свет.";
                    break;
                case EndingType.OrdersAreOrders:
                    title = "КОНЦОВКА II · «ПРИКАЗ ЕСТЬ ПРИКАЗ»";
                    subtitle = "Мост уничтожен вовремя. Фронт удержан, но в эфире пусто.";
                    break;
                case EndingType.FireOnMyself:
                    title = "КОНЦОВКА III · «ОГОНЬ НА СЕБЯ»";
                    subtitle = "Танковая колонна и мост уничтожены. На смену Ларину заступает «Маяк-2».";
                    break;
                case EndingType.Silence:
                    title = "КОНЦОВКА IV · «ТИШИНА»";
                    subtitle = "Узел «Маяк» прекратил работу в 04:21. Ефрейтор Ларин числится пропавшим без вести.";
                    break;
                case EndingType.ForeignVoice:
                    title = "КОНЦОВКА V · «ЧУЖОЙ ГОЛОС»";
                    subtitle = "Вы выполняли приказы противника. Шаги на лестнице.";
                    break;
                case EndingType.Address:
                    title = "РАННЯЯ КОНЦОВКА · «АДРЕС»";
                    subtitle = "Узел «Маяк» прекратил работу в 02:43. Ефрейтор Ларин погиб.";
                    break;
                default:
                    title = "ЖУРНАЛ СВЯЗИ · ИТОГИ СМЕНЫ";
                    subtitle = string.Empty;
                    break;
            }
        }

        private static string BuildJournalSummaryText(EndingType ending, StoryFlagsState flags)
        {
            if (ending == EndingType.Address)
            {
                return "ЛИСТ ЖУРНАЛА СВЯЗИ · УЗЕЛ «МАЯК»\n\n" +
                       "Скворец   —   Адрес назван. 02:43.";
            }

            bool topol = flags != null && flags.GetFlag(StoryFlag.TopolSaved);
            bool soroka = flags == null || flags.GetFlag(StoryFlag.SorokaAlive);
            bool romashka = flags != null && flags.GetFlag(StoryFlag.RomashkaWarned);
            bool shilo = flags != null && flags.GetFlag(StoryFlag.ShiloSignalUnderstood);
            bool battalionSaved = ending == EndingType.MayakBurns;

            var sb = new StringBuilder();
            sb.AppendLine("ЛИСТ ЖУРНАЛА СВЯЗИ · УЗЕЛ «МАЯК»");
            sb.AppendLine();
            sb.AppendLine($"Ёж         —   {(battalionSaved ? "на связи" : "на связь не выходит")}  (Ночь 3)");
            sb.AppendLine($"Тополь     —   {(topol ? "на связи" : "на связь не выходит")}  (Ночь 2)");
            sb.AppendLine($"Сорока     —   {(soroka ? "на связи" : "на связь не выходит")}  (Ночь 2)");
            sb.AppendLine($"Ромашка    —   {(romashka && battalionSaved ? "на связи" : "на связь не выходит")}  (Ночь 2–3)");
            sb.AppendLine($"Шило       —   {(shilo ? "сигнал понят · на связь не выходит" : "на связь не выходит")}  (Ночь 3)");
            sb.AppendLine($"Кобра      —   {(battalionSaved ? "на связи" : "на связь не выходит")}  (Ночь 3)");
            return sb.ToString();
        }

        private void StopActiveRoutine()
        {
            if (_activeCoroutine != null)
            {
                StopCoroutine(_activeCoroutine);
                _activeCoroutine = null;
            }
        }

        private static bool WasSkipPressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            return kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
#endif
        }
    }
}
