using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LastSignal.SwitchboardSystem;

namespace LastSignal.StoryCore
{
    /// <summary>
    /// Исполнитель ветвящихся звонков (CallScenarioAsset).
    /// Поддерживает:
    /// - гибкое воспроизведение реплик (AudioClip с ожиданием конца ИЛИ посимвольные субтитры с кликом-пропуском);
    /// - двойной фон линий (например, Кузница + Кобра, Ёж + Гром, Шило + Кобра, Ромашка + Якорь);
    /// - кликабельные ключевые слова с анимацией записи карандашом и занесением в журнал;
    /// - UI сборки корректировки огня для «Грома» (Звонок 1.3);
    /// - мягкую подсказку при ошибке гнезда;
    /// - нативную паузу при выдёргивании шнура (и корректный отбой со стороны абонента);
    /// - заскриптованную кульминацию гибели «Берёзы» (Звонок 1.6) с 10 секундами абсолютной тишины.
    /// </summary>
    public sealed class DialogueRunner : MonoBehaviour
    {
        private static readonly Color LarinColor = new Color(0.84f, 0.90f, 0.95f, 1f);
        private static readonly Color ZharovColor = new Color(0.95f, 0.64f, 0.36f, 1f);
        private static readonly Color DeminColor = new Color(0.96f, 0.78f, 0.44f, 1f);

        [Header("Ссылки на системы ядра")]
        [SerializeField] private StoryFlagsState flagsState;
        [SerializeField] private SwitchboardLineRouter lineRouter;
        [SerializeField] private SubtitleDialogueUI subtitleUI;
        [SerializeField] private RoomAtmosphereController roomAtmosphere;
        [SerializeField] private RadioSignalMixer radioSignalMixer;
        [SerializeField] private DeskClockController deskClock;

        [Header("Ссылки на предметы комнаты")]
        [SerializeField] private BookInspectController bookInspect;
        [SerializeField] private LetterInteraction letterInteraction;
        [SerializeField] private LogbookSubscribersPage subscribersPage;
        [SerializeField] private LogbookNotesPage notesPage;
        [SerializeField] private LogbookPencilAnimator pencilAnimator;
        [SerializeField] private LogbookSpreadController spreadController;

        [Header("Источники звука эфира")]
        [SerializeField] private AudioSource voiceAudioSource;
        [SerializeField] private AudioSource callerAmbientSource;
        [SerializeField] private AudioSource secondaryAmbientSource;
        [SerializeField] private AudioSource climaxSfxSource;
        [SerializeField, Range(0f, 1f)] private float voiceVolume = 0.92f;

        [Header("Текстовый режим без озвучки")]
        [Tooltip("Скорость чтения субтитров (символов/сек), если voiceClip не назначен и fallbackDuration == 0.")]
        [SerializeField, Min(10f)] private float readingCharsPerSecond = 26f;

        [Header("Отладка текущего звонка (только чтение)")]
        [SerializeField] private CallScenarioAsset activeCall;
        [SerializeField] private string activeNodeId = string.Empty;
        [SerializeField] private bool isPausedByDisconnect;
        [SerializeField] private bool isWaitingForPlayerChoice;
        [SerializeField] private DialogueWaitAction activeWaitAction = DialogueWaitAction.None;

        private Coroutine _callCoroutine;
        private int _runToken;
        private int _selectedChoiceIndex = -1;
        private bool _waitConditionSatisfied;
        private bool _advanceLineRequested;
        private bool _primaryLineHungUp;
        private bool _isInScriptedClimax;
        private SubscriberId _expectedSubscriber;
        private string _activeWrongPortHint = string.Empty;
        private readonly List<string> _requiredKeywordsToWait = new List<string>();
        private readonly List<DialogueChoiceData> _visibleChoiceData = new List<DialogueChoiceData>();

        private bool _fireMissionSubmitted;
        private string _fireMissionSquare;
        private bool _fireMissionOneShell;
        private int _keywordJotRowCounter;

        public bool IsRunning => activeCall != null;
        public bool IsPausedByDisconnect => isPausedByDisconnect;
        public CallScenarioAsset ActiveCall => activeCall;

        public event Action<CallScenarioAsset, bool> OnCallFinished;
        public event Action<EndingType> OnEndingRequested;

        private void Awake()
        {
            if (letterInteraction == null) letterInteraction = FindFirstObjectByType<LetterInteraction>();
            if (deskClock == null) deskClock = FindFirstObjectByType<DeskClockController>();

            EnsureAudioSources();
            if (radioSignalMixer != null)
            {
                radioSignalMixer.BindVoiceSource(voiceAudioSource, callerAmbientSource);
            }
        }

        private void OnEnable()
        {
            if (subtitleUI != null)
            {
                subtitleUI.OnKeywordClicked += HandleKeywordClicked;
                subtitleUI.OnChoiceSelected += HandleChoiceSelected;
                subtitleUI.OnSubtitleAdvanceClicked += HandleSubtitleAdvanceClicked;
            }

            if (subscribersPage != null)
            {
                subscribersPage.OnSubscriberClicked += HandleBookSubscriberClicked;
            }

            if (lineRouter != null)
            {
                lineRouter.OnAnyPortConnected += HandleAnyPortConnected;
                lineRouter.OnAnyPortDisconnected += HandleAnyPortDisconnected;
            }
        }

        private void OnDisable()
        {
            if (subtitleUI != null)
            {
                subtitleUI.OnKeywordClicked -= HandleKeywordClicked;
                subtitleUI.OnChoiceSelected -= HandleChoiceSelected;
                subtitleUI.OnSubtitleAdvanceClicked -= HandleSubtitleAdvanceClicked;
            }

            if (subscribersPage != null)
            {
                subscribersPage.OnSubscriberClicked -= HandleBookSubscriberClicked;
            }

            if (lineRouter != null)
            {
                lineRouter.OnAnyPortConnected -= HandleAnyPortConnected;
                lineRouter.OnAnyPortDisconnected -= HandleAnyPortDisconnected;
            }

            StopCurrentCall(notifyFinished: false);
        }

        public void StartCall(CallScenarioAsset call, string startFromNodeId = null)
        {
            if (call == null)
            {
                return;
            }

            StopCurrentCall(notifyFinished: false);

            activeCall = call;
            activeNodeId = string.Empty;
            isPausedByDisconnect = false;
            _primaryLineHungUp = false;
            _isInScriptedClimax = false;
            _advanceLineRequested = false;
            _runToken++;

            StartCallerAmbient(call);

            if (radioSignalMixer != null)
            {
                radioSignalMixer.SetCallTuningProfile(
                    call.requiresRadioTuning,
                    call.targetRadioFrequency01,
                    call.radioTuningTolerance);
            }

            _callCoroutine = StartCoroutine(RunCallRoutine(call, _runToken, startFromNodeId));
        }

        public void StopCurrentCall(bool notifyFinished = false, bool hungUpEarly = false)
        {
            CallScenarioAsset finished = activeCall;
            _runToken++;

            if (_callCoroutine != null)
            {
                StopCoroutine(_callCoroutine);
                _callCoroutine = null;
            }

            if (finished != null && lineRouter != null)
            {
                lineRouter.SetPortOnPausedHold(finished.subscriberPort, false);
            }

            if (_isInScriptedClimax && roomAtmosphere != null)
            {
                roomAtmosphere.SetTotalSilence(false);
            }

            activeCall = null;
            activeNodeId = string.Empty;
            isPausedByDisconnect = false;
            isWaitingForPlayerChoice = false;
            activeWaitAction = DialogueWaitAction.None;
            _primaryLineHungUp = false;
            _isInScriptedClimax = false;
            _selectedChoiceIndex = -1;
            _waitConditionSatisfied = false;
            _advanceLineRequested = false;
            _activeWrongPortHint = string.Empty;
            _requiredKeywordsToWait.Clear();

            StopSource(voiceAudioSource);
            StopSource(callerAmbientSource);
            StopSource(secondaryAmbientSource);
            StopSource(climaxSfxSource);

            if (radioSignalMixer != null)
            {
                radioSignalMixer.ClearCallTuningProfile();
            }

            if (subtitleUI != null)
            {
                subtitleUI.HideAll();
            }

            if (notifyFinished && finished != null)
            {
                OnCallFinished?.Invoke(finished, hungUpEarly);
            }
        }

        private IEnumerator RunCallRoutine(CallScenarioAsset call, int token, string startFromNodeId)
        {
            DialogueNodeData current = !string.IsNullOrWhiteSpace(startFromNodeId)
                ? (call.FindNode(startFromNodeId) ?? call.GetEntryNode())
                : call.GetEntryNode();

            while (current != null && token == _runToken)
            {
                yield return WaitWhilePausedByDisconnect(token);
                if (token != _runToken) yield break;

                activeNodeId = current.nodeId;

                if (flagsState != null && !flagsState.CheckCondition(current.conditionRequiredFlag, current.conditionForbiddenFlag, null))
                {
                    current = call.FindNode(current.skipToNodeId);
                    continue;
                }

                // 0. Специальный заскриптованный узел кульминации гибели «Берёзы» (Звонок 1.6).
                if (current.berezaClimax != null && current.berezaClimax.enabled)
                {
                    yield return RunBerezaDeathClimaxRoutine(current.berezaClimax, token);
                    if (token != _runToken) yield break;
                    current = call.FindNode(current.autoNextNodeId);
                    continue;
                }

                // 1. Показываем субтитры с посимвольным появлением (синхронизированным с длительностью реплики).
                bool hasSubtitle = !string.IsNullOrWhiteSpace(current.subtitleText);
                if (subtitleUI != null && hasSubtitle)
                {
                    subtitleUI.HideChoices();
                    subtitleUI.HideFireMissionAssembly();
                    float expectedDur = ComputeLineDuration(current);
                    subtitleUI.ShowLine(
                        current.speakerName,
                        current.speakerColor,
                        current.subtitleText,
                        id => flagsState != null && flagsState.HasKeyword(id),
                        targetRevealDuration: expectedDur,
                        isItalic: current.isItalic);
                }

                // 2. Проигрываем озвучку (или ждём скорость чтения с поддержкой клика по субтитрам).
                if (hasSubtitle || current.voiceClip != null)
                {
                    yield return PlayVoiceOrWaitRoutine(current, token);
                    if (token != _runToken) yield break;
                }

                // 3. Применяем немедленные последствия узла (пометки в книге, флаги, отбой линий, эффекты комнаты).
                ApplyNodeEffects(current);

                if (current.endingToTrigger != EndingType.None)
                {
                    EndingType ending = current.endingToTrigger;
                    StopCurrentCall(notifyFinished: false);
                    OnEndingRequested?.Invoke(ending);
                    yield break;
                }

                // 4. Специальный режим сборки сообщения для «Грома» (Звонок 1.3).
                if (current.waitAction == DialogueWaitAction.WaitFireMissionAssembly)
                {
                    string nextAfterFireMission = null;
                    yield return RunFireMissionAssemblyRoutine(current, token, nextId => nextAfterFireMission = nextId);
                    if (token != _runToken) yield break;
                    current = call.FindNode(nextAfterFireMission);
                    continue;
                }

                // 5. Ожидание игрового действия (если задано).
                if (current.waitAction != DialogueWaitAction.None)
                {
                    yield return WaitForGameplayActionRoutine(current, token);
                    if (token != _runToken) yield break;

                    // Если подключили вторую линию — запускаем её фоновый звук.
                    if (current.waitAction == DialogueWaitAction.WaitConnectPort &&
                        (current.secondaryAmbientClip != null || current.secondaryAmbientProfile != SubscriberAmbientProfile.None))
                    {
                        StartSecondaryAmbient(current.secondaryAmbientClip, current.secondaryAmbientProfile, current.secondaryAmbientVolume);
                    }
                }

                // 6. Варианты ответа Ларина.
                if (current.choices != null && current.choices.Count > 0)
                {
                    DialogueChoiceData chosen = null;
                    yield return WaitForPlayerChoiceRoutine(current.choices, token, c => chosen = c);
                    if (token != _runToken) yield break;

                    if (chosen != null)
                    {
                        ApplyChoiceEffects(chosen);

                        if (chosen.endingToTrigger != EndingType.None)
                        {
                            EndingType ending = chosen.endingToTrigger;
                            StopCurrentCall(notifyFinished: false);
                            OnEndingRequested?.Invoke(ending);
                            yield break;
                        }

                        current = call.FindNode(chosen.nextNodeId);
                        continue;
                    }

                    current = null;
                    continue;
                }

                // 7. Короткая пауза перед следующей репликой.
                if (current.postLineDelay > 0.01f)
                {
                    float remaining = current.postLineDelay;
                    while (remaining > 0f && token == _runToken)
                    {
                        if (!isPausedByDisconnect)
                        {
                            remaining -= Time.deltaTime;
                        }
                        yield return null;
                    }
                }

                current = call.FindNode(current.autoNextNodeId);
            }

            if (token == _runToken)
            {
                if (flagsState != null)
                {
                    flagsState.MarkCallCompleted(call.callId);
                }

                StopCurrentCall(notifyFinished: true, hungUpEarly: false);
            }
        }

        private float ComputeLineDuration(DialogueNodeData node)
        {
            if (node.voiceClip != null)
            {
                return node.voiceClip.length;
            }

            if (node.fallbackDuration > 0.1f)
            {
                return node.fallbackDuration;
            }

            string raw = node.subtitleText ?? string.Empty;
            float autoSec = raw.Length / Mathf.Max(10f, readingCharsPerSecond) + 1.15f;
            return Mathf.Clamp(autoSec, 2.0f, 11.5f);
        }

        private IEnumerator WaitWhilePausedByDisconnect(int token)
        {
            while (isPausedByDisconnect && token == _runToken)
            {
                yield return null;
            }
        }

        private IEnumerator PlayVoiceOrWaitRoutine(DialogueNodeData node, int token)
        {
            _advanceLineRequested = false;

            if (node.voiceClip != null && voiceAudioSource != null)
            {
                yield return PlayAudioClipRoutine(node.voiceClip, token, allowDisconnectPause: !_isInScriptedClimax);
                yield break;
            }

            // Текстовый режим (пока озвучка не назначена): ждём скорость чтения, но позволяем кликом ускорить/пропустить строку.
            if (!string.IsNullOrWhiteSpace(node.subtitleText))
            {
                float remaining = ComputeLineDuration(node);
                while (token == _runToken)
                {
                    if (!isPausedByDisconnect)
                    {
                        bool revealDone = subtitleUI == null || subtitleUI.IsLineRevealFinished;
                        if (_advanceLineRequested && revealDone)
                        {
                            _advanceLineRequested = false;
                            break;
                        }

                        remaining -= Time.deltaTime;
                        if (remaining <= 0f && revealDone)
                        {
                            break;
                        }
                    }

                    yield return null;
                }
            }
        }

        private IEnumerator PlayAudioClipRoutine(AudioClip clip, int token, bool allowDisconnectPause)
        {
            if (clip == null || voiceAudioSource == null)
            {
                yield break;
            }

            voiceAudioSource.Stop();
            voiceAudioSource.clip = clip;
            voiceAudioSource.volume = voiceVolume;
            voiceAudioSource.Play();

            float duration = clip.length;
            float elapsed = 0f;
            bool started = false;

            while (token == _runToken)
            {
                if ((allowDisconnectPause && isPausedByDisconnect) || AudioListener.pause || !Application.isFocused)
                {
                    yield return null;
                    continue;
                }

                if (voiceAudioSource.isPlaying)
                {
                    started = true;
                    elapsed += Time.unscaledDeltaTime;
                    if (elapsed >= duration + 0.5f)
                    {
                        break;
                    }
                }
                else
                {
                    if (started)
                    {
                        break;
                    }

                    elapsed += Time.unscaledDeltaTime;
                    if (elapsed >= Mathf.Max(0.25f, duration))
                    {
                        break;
                    }
                }

                yield return null;
            }

            if (token == _runToken && voiceAudioSource != null)
            {
                voiceAudioSource.Stop();
                voiceAudioSource.clip = null;
            }
        }

        private IEnumerator PlayQuickSpokenLineRoutine(
            string speaker,
            Color color,
            string text,
            AudioClip clip,
            int token,
            bool isItalic = false)
        {
            _advanceLineRequested = false;
            float dur = clip != null
                ? clip.length
                : Mathf.Clamp((text ?? string.Empty).Length / Mathf.Max(10f, readingCharsPerSecond) + 1.0f, 2.0f, 8.0f);

            if (subtitleUI != null)
            {
                subtitleUI.ShowLine(speaker, color, text, id => flagsState != null && flagsState.HasKeyword(id), targetRevealDuration: dur, isItalic: isItalic);
            }

            if (clip != null && voiceAudioSource != null)
            {
                yield return PlayAudioClipRoutine(clip, token, allowDisconnectPause: !_isInScriptedClimax);
            }
            else
            {
                float rem = dur;
                while (token == _runToken)
                {
                    if (!isPausedByDisconnect || _isInScriptedClimax)
                    {
                        bool revealDone = subtitleUI == null || subtitleUI.IsLineRevealFinished;
                        if (_advanceLineRequested && revealDone)
                        {
                            _advanceLineRequested = false;
                            break;
                        }
                        rem -= Time.deltaTime;
                        if (rem <= 0f && revealDone)
                        {
                            break;
                        }
                    }
                    yield return null;
                }
            }

            if (token == _runToken)
            {
                yield return WaitPausableSeconds(0.35f, token);
            }
        }

        private IEnumerator WaitForGameplayActionRoutine(DialogueNodeData node, int token)
        {
            activeWaitAction = node.waitAction;
            _expectedSubscriber = node.expectedSubscriber;
            _activeWrongPortHint = node.wrongPortHintText ?? string.Empty;
            _requiredKeywordsToWait.Clear();
            if (node.requiredKeywordIds != null)
            {
                for (int i = 0; i < node.requiredKeywordIds.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(node.requiredKeywordIds[i]))
                    {
                        _requiredKeywordsToWait.Add(node.requiredKeywordIds[i].Trim());
                    }
                }
            }

            _waitConditionSatisfied = CheckImmediateWaitCondition(node.waitAction, node.expectedSubscriber);

            if (!_waitConditionSatisfied && subtitleUI != null)
            {
                if (node.waitAction == DialogueWaitAction.WaitAllPlugsDisconnected)
                {
                    subtitleUI.HideAll();
                }

                if (!string.IsNullOrWhiteSpace(node.actionHintText))
                {
                    subtitleUI.ShowActionHint(node.actionHintText);
                }
            }

            while (!_waitConditionSatisfied && token == _runToken)
            {
                if (!isPausedByDisconnect || activeWaitAction == DialogueWaitAction.WaitAllPlugsDisconnected)
                {
                    if (activeWaitAction == DialogueWaitAction.WaitOpenBook &&
                        bookInspect != null && bookInspect.IsInspecting)
                    {
                        _waitConditionSatisfied = true;
                    }
                    else if (activeWaitAction == DialogueWaitAction.WaitTuneRadio &&
                             radioSignalMixer != null && radioSignalMixer.IsTunedIn)
                    {
                        _waitConditionSatisfied = true;
                    }
                    else if (activeWaitAction == DialogueWaitAction.WaitAllPlugsDisconnected &&
                             (lineRouter == null || lineRouter.AreAllPlugsAtBase()))
                    {
                        _waitConditionSatisfied = true;
                    }
                    else if (activeWaitAction == DialogueWaitAction.WaitRecordKeywords &&
                             AreAllRequiredKeywordsRecorded())
                    {
                        _waitConditionSatisfied = true;
                    }
                }

                yield return null;
            }

            activeWaitAction = DialogueWaitAction.None;
            _activeWrongPortHint = string.Empty;
            _requiredKeywordsToWait.Clear();
            if (subtitleUI != null)
            {
                subtitleUI.HideActionHint();
            }
        }

        private bool CheckImmediateWaitCondition(DialogueWaitAction action, SubscriberId expected)
        {
            return action switch
            {
                DialogueWaitAction.WaitOpenBook => bookInspect != null && bookInspect.IsInspecting,
                DialogueWaitAction.WaitConnectPort => lineRouter != null && lineRouter.IsPortConnected(expected),
                DialogueWaitAction.WaitDisconnectPort => lineRouter != null && !lineRouter.IsPortConnected(expected),
                DialogueWaitAction.WaitTuneRadio => radioSignalMixer != null && radioSignalMixer.IsTunedIn,
                DialogueWaitAction.WaitRecordKeywords => AreAllRequiredKeywordsRecorded(),
                DialogueWaitAction.WaitAllPlugsDisconnected => lineRouter == null || lineRouter.AreAllPlugsAtBase(),
                _ => false
            };
        }

        private bool AreAllRequiredKeywordsRecorded()
        {
            if (_requiredKeywordsToWait.Count == 0)
            {
                return true;
            }
            if (flagsState == null)
            {
                return false;
            }

            for (int i = 0; i < _requiredKeywordsToWait.Count; i++)
            {
                if (!flagsState.HasKeyword(_requiredKeywordsToWait[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private IEnumerator RunFireMissionAssemblyRoutine(DialogueNodeData node, int token, Action<string> onNextNodeResolved)
        {
            _fireMissionSubmitted = false;
            _fireMissionSquare = null;
            _fireMissionOneShell = true;

            bool hasSquare = flagsState != null && flagsState.HasKeyword("sq_16b");
            bool hasAmmo = flagsState != null && flagsState.HasKeyword("ammo_1");

            if (subtitleUI != null)
            {
                subtitleUI.ShowFireMissionAssembly(hasSquare, hasAmmo, (sq, oneShell) =>
                {
                    _fireMissionSquare = sq;
                    _fireMissionOneShell = oneShell;
                    _fireMissionSubmitted = true;
                });
            }

            while (!_fireMissionSubmitted && token == _runToken)
            {
                yield return null;
            }

            if (token != _runToken)
            {
                yield break;
            }

            string sqCode = _fireMissionSquare ?? "16-Б";
            bool oneShell = _fireMissionOneShell;

            string sqWords = sqCode switch
            {
                "16-Б" => "шестнадцать-Б",
                "16-В" => "шестнадцать-В",
                "15-Б" => "пятнадцать-Б",
                _ => sqCode
            };
            string ammoLarinWords = oneShell ? "один снаряд" : "три снаряда";
            string ammoZharovWords = oneShell ? "один" : "три";

            FireMissionVoiceVariant variant = FindFireMissionVariant(node, sqCode, oneShell);

            string larinLine = $"Гром, я Маяк. Пристрелочный, квадрат {sqWords}, {ammoLarinWords}.";
            yield return PlayQuickSpokenLineRoutine("ЛАРИН («Маяк»)", LarinColor, larinLine, variant?.larinClip, token);
            if (token != _runToken) yield break;

            string zharovLine = $"Повторяю: {sqWords}, {ammoZharovWords}, пристрелочный. Так? Даю!";
            yield return PlayQuickSpokenLineRoutine("ЖАРОВ («Гром»)", ZharovColor, zharovLine, variant?.zharovClip, token);
            if (token != _runToken) yield break;

            // Далёкий выстрел орудия -> ~1.1с -> далёкий разрыв (пламя лампы вздрагивает, лёгкая пыль).
            if (roomAtmosphere != null)
            {
                yield return roomAtmosphere.PlayArtilleryShotThenImpactRoutine();
            }
            else
            {
                yield return WaitPausableSeconds(1.1f, token);
            }

            if (token != _runToken) yield break;

            yield return WaitPausableSeconds(0.55f, token);
            if (token != _runToken) yield break;

            bool isCorrect = string.Equals(sqCode, "16-Б", StringComparison.OrdinalIgnoreCase) && oneShell;
            onNextNodeResolved?.Invoke(isCorrect ? node.fireMissionSuccessNodeId : node.fireMissionFailureNodeId);
        }

        private static FireMissionVoiceVariant FindFireMissionVariant(DialogueNodeData node, string sqCode, bool oneShell)
        {
            if (node.fireMissionVoiceVariants == null) return null;
            for (int i = 0; i < node.fireMissionVoiceVariants.Count; i++)
            {
                var v = node.fireMissionVoiceVariants[i];
                if (v != null && string.Equals(v.squareCode, sqCode, StringComparison.OrdinalIgnoreCase) && v.oneShell == oneShell)
                {
                    return v;
                }
            }
            return null;
        }

        private IEnumerator RunBerezaDeathClimaxRoutine(BerezaDeathClimaxData climax, int token)
        {
            _isInScriptedClimax = true;
            _primaryLineHungUp = true;
            isPausedByDisconnect = false;

            // 1. Мягко закрываем книгу и/или письмо, чтобы игрок видел коммутатор.
            if (bookInspect != null && (bookInspect.IsInspecting || bookInspect.IsPickUpPending))
            {
                bookInspect.PutBookBack();
            }
            if (letterInteraction != null && letterInteraction.IsActive)
            {
                letterInteraction.BeginClose();
            }

            if (deskClock != null)
            {
                deskClock.SetClock(0, 17, 0f);
            }

            if (subtitleUI != null)
            {
                subtitleUI.HideAll();
            }

            yield return WaitUnscaledSeconds(0.45f, token);
            if (token != _runToken) yield break;

            // 2. Нарастающий свист снаряда (~1.1с).
            float whistleDur = Mathf.Max(0.5f, climax.whistleDuration);
            AudioClip whistleClip = climax.shellWhistleClip != null
                ? climax.shellWhistleClip
                : ProceduralRadioAudio.GetIncomingShellWhistle(whistleDur);
            if (climaxSfxSource != null && whistleClip != null)
            {
                climaxSfxSource.Stop();
                climaxSfxSource.loop = false;
                climaxSfxSource.clip = whistleClip;
                climaxSfxSource.volume = 0.85f;
                climaxSfxSource.Play();
            }

            yield return WaitUnscaledSeconds(whistleDur, token);
            if (token != _runToken) yield break;

            // 3. Громкий удар и треск в эфире (пламя лампы вздрагивает, сыплется пыль с потолка).
            AudioClip impactClip = climax.impactAndCrackleClip != null
                ? climax.impactAndCrackleClip
                : ProceduralRadioAudio.GetImpactAndRadioCrackle();
            if (roomAtmosphere != null)
            {
                roomAtmosphere.PlayFrontOneShot(impactClip, 0.92f, 1f);
                roomAtmosphere.TriggerBerezaImpactVisuals();
            }
            else if (climaxSfxSource != null && impactClip != null)
            {
                climaxSfxSource.PlayOneShot(impactClip, 0.92f);
            }

            yield return WaitUnscaledSeconds(0.28f, token);
            if (token != _runToken) yield break;

            // 4. Дёмин обрывается на полуслове: «…Маяк! Маяк, по нам…»
            if (subtitleUI != null)
            {
                float cutDur = climax.deminCutOffClip != null ? climax.deminCutOffClip.length : 1.35f;
                subtitleUI.ShowLine("ДЁМИН («Берёза»)", DeminColor, "…Маяк! Маяк, по нам…", null, targetRevealDuration: cutDur);
            }

            if (climax.deminCutOffClip != null)
            {
                yield return PlayAudioClipRoutine(climax.deminCutOffClip, token, allowDisconnectPause: false);
            }
            else
            {
                yield return WaitUnscaledSeconds(1.4f, token);
            }
            if (token != _runToken) yield break;

            // 5. Белый шум (~1.7с) -> резкий обрыв.
            StopSource(callerAmbientSource);
            StopSource(secondaryAmbientSource);

            AudioClip noiseClip = climax.whiteNoiseClip != null
                ? climax.whiteNoiseClip
                : ProceduralRadioAudio.GetWhiteNoiseLoop();
            if (climaxSfxSource != null && noiseClip != null)
            {
                climaxSfxSource.Stop();
                climaxSfxSource.clip = noiseClip;
                climaxSfxSource.loop = true;
                climaxSfxSource.volume = 0.62f;
                climaxSfxSource.Play();
            }

            yield return WaitUnscaledSeconds(Mathf.Max(0.5f, climax.whiteNoiseDuration), token);
            if (token != _runToken) yield break;

            // 6. Резкий обрыв. Субтитры скрываются. Все 10 ламп коммутатора гаснут.
            StopSource(climaxSfxSource);
            StopSource(voiceAudioSource);
            if (subtitleUI != null) subtitleUI.HideAll();

            lineRouter?.SetBlackout(true);
            flagsState?.MarkBerezaDestroyed();

            // 7. 10 секунд полной, абсолютной тишины (шипение лампы, тиканье часов, капли, фон линий — всё выключено).
            roomAtmosphere?.SetTotalSilence(true);

            yield return WaitUnscaledSeconds(Mathf.Max(1f, climax.absoluteSilenceDuration), token);
            if (token != _runToken) yield break;

            // 8. Ровно через 10 секунд: возвращается фоновый звук комнаты, серия щелчков реле и ВСЕ 10 ламп мигают одновременно!
            roomAtmosphere?.SetTotalSilence(false);
            lineRouter?.FlashAllAliveLamps();

            yield return WaitUnscaledSeconds(Mathf.Max(0.5f, climax.allLampsBlinkDuration), token);
            if (token != _runToken) yield break;

            _isInScriptedClimax = false;
        }

        private IEnumerator WaitForPlayerChoiceRoutine(
            List<DialogueChoiceData> choices,
            int token,
            Action<DialogueChoiceData> onChosen)
        {
            _visibleChoiceData.Clear();
            var views = new List<SubtitleChoiceView>();

            for (int i = 0; i < choices.Count; i++)
            {
                DialogueChoiceData ch = choices[i];
                if (ch == null) continue;

                bool flagsOk = flagsState == null || flagsState.CheckCondition(ch.requiredFlag, ch.forbiddenFlag, null);
                if (!flagsOk) continue;

                bool keywordOk = flagsState == null || flagsState.HasKeyword(ch.requiredKeywordId);
                if (!keywordOk && !ch.showLockedWhenMissingKeyword) continue;

                int viewIndex = _visibleChoiceData.Count;
                _visibleChoiceData.Add(ch);
                views.Add(new SubtitleChoiceView
                {
                    Index = viewIndex,
                    Text = ch.choiceText,
                    Interactable = keywordOk,
                    LockedReason = keywordOk ? string.Empty : "не записано в журнал"
                });
            }

            if (views.Count == 0)
            {
                onChosen?.Invoke(null);
                yield break;
            }

            isWaitingForPlayerChoice = true;
            _selectedChoiceIndex = -1;

            if (subtitleUI != null)
            {
                subtitleUI.ShowChoices(views);
            }

            while (_selectedChoiceIndex < 0 && token == _runToken)
            {
                yield return null;
            }

            isWaitingForPlayerChoice = false;
            if (subtitleUI != null)
            {
                subtitleUI.HideChoices();
            }

            if (token == _runToken && _selectedChoiceIndex >= 0 && _selectedChoiceIndex < _visibleChoiceData.Count)
            {
                onChosen?.Invoke(_visibleChoiceData[_selectedChoiceIndex]);
            }
        }

        private void PauseActiveCallByDisconnect()
        {
            if (isPausedByDisconnect || activeCall == null || _primaryLineHungUp || _isInScriptedClimax)
            {
                return;
            }

            isPausedByDisconnect = true;

            if (voiceAudioSource != null && voiceAudioSource.isPlaying)
            {
                voiceAudioSource.Pause();
            }

            if (callerAmbientSource != null && callerAmbientSource.isPlaying)
            {
                callerAmbientSource.Pause();
            }

            if (secondaryAmbientSource != null && secondaryAmbientSource.isPlaying)
            {
                secondaryAmbientSource.Pause();
            }

            if (subtitleUI != null)
            {
                subtitleUI.SetPausedHidden(true);
            }

            if (lineRouter != null)
            {
                lineRouter.SetPortOnPausedHold(activeCall.subscriberPort, true);
            }
        }

        private void ResumeActiveCallAfterReconnect()
        {
            if (!isPausedByDisconnect || activeCall == null)
            {
                return;
            }

            isPausedByDisconnect = false;

            if (lineRouter != null)
            {
                lineRouter.SetPortOnPausedHold(activeCall.subscriberPort, false);
            }

            if (voiceAudioSource != null && voiceAudioSource.clip != null)
            {
                voiceAudioSource.UnPause();
            }

            if (callerAmbientSource != null && callerAmbientSource.clip != null)
            {
                callerAmbientSource.UnPause();
            }

            if (secondaryAmbientSource != null && secondaryAmbientSource.clip != null)
            {
                secondaryAmbientSource.UnPause();
            }

            if (subtitleUI != null)
            {
                subtitleUI.SetPausedHidden(false);
            }
        }

        private void ApplyNodeEffects(DialogueNodeData node)
        {
            if (flagsState != null && node.flagToSet != StoryFlag.None)
            {
                flagsState.SetFlag(node.flagToSet, node.flagValue);
            }

            if (notesPage != null && !string.IsNullOrWhiteSpace(node.logbookNoteToAdd))
            {
                notesPage.AddNote(node.logbookNoteToAdd);
            }

            if (node.stopSecondaryAmbient)
            {
                StopSource(secondaryAmbientSource);
            }

            if (node.remoteHangUpPorts != null && node.remoteHangUpPorts.Count > 0 && lineRouter != null)
            {
                for (int i = 0; i < node.remoteHangUpPorts.Count; i++)
                {
                    SubscriberId p = node.remoteHangUpPorts[i];
                    if (p == SubscriberId.None) continue;
                    lineRouter.SetPortRemoteHangUp(p, true, playClick: true);
                    if (activeCall != null && p == activeCall.subscriberPort)
                    {
                        _primaryLineHungUp = true;
                        StopSource(callerAmbientSource);
                    }
                }
            }

            if (node.hangUpPrimaryLine && activeCall != null)
            {
                _primaryLineHungUp = true;
                StopSource(callerAmbientSource);
                if (lineRouter != null)
                {
                    lineRouter.SetPortRemoteHangUp(activeCall.subscriberPort, true, playClick: true);
                }
            }

            if (roomAtmosphere != null && node.roomEffect != RoomEventEffect.None)
            {
                roomAtmosphere.ExecuteRoomEffect(node.roomEffect);
            }
        }

        private void ApplyChoiceEffects(DialogueChoiceData choice)
        {
            if (flagsState != null && choice.flagToSet != StoryFlag.None)
            {
                flagsState.SetFlag(choice.flagToSet, choice.flagValue);
            }

            if (notesPage != null && !string.IsNullOrWhiteSpace(choice.logbookNoteToAdd))
            {
                notesPage.AddNote(choice.logbookNoteToAdd);
            }
        }

        private void HandleKeywordClicked(string keywordId, string displayText)
        {
            if (flagsState == null || isPausedByDisconnect)
            {
                return;
            }

            bool newlyRecorded = flagsState.RecordKeyword(keywordId, displayText);
            if (!newlyRecorded)
            {
                return;
            }

            TriggerQuickPencilJot();

            if (spreadController != null)
            {
                spreadController.RefreshSpread2();
            }

            if (subtitleUI != null)
            {
                subtitleUI.RefreshRecordedKeywords(id => flagsState.HasKeyword(id));
            }

            if (activeWaitAction == DialogueWaitAction.WaitRecordKeywords && AreAllRequiredKeywordsRecorded())
            {
                _waitConditionSatisfied = true;
            }
        }

        private void TriggerQuickPencilJot()
        {
            if (pencilAnimator == null) return;

            if (!pencilAnimator.IsWriting && notesPage != null)
            {
                int row = _keywordJotRowCounter++ % 5;
                float v = 1f - ((260f + row * 95f) / 1430f);
                float localX = Mathf.Lerp(-0.1075f, 0.1075f, v);
                float localYStart = Mathf.Lerp(0.002f, 0.156f, 0.16f);
                float localYEnd = Mathf.Lerp(0.002f, 0.156f, 0.64f);
                Vector3 localStart = new Vector3(localX, localYStart, LogbookPencilAnimator.SampleCurvedPageSurfaceZ(localYStart));
                Vector3 localEnd = new Vector3(localX, localYEnd, LogbookPencilAnimator.SampleCurvedPageSurfaceZ(localYEnd));

                pencilAnimator.PlayWriteAnimation(notesPage.transform, localStart, localEnd, 0.72f);
            }
            else
            {
                pencilAnimator.PlayShortScratchSound();
            }
        }

        private void HandleChoiceSelected(int choiceIndex)
        {
            if (isWaitingForPlayerChoice && !isPausedByDisconnect)
            {
                _selectedChoiceIndex = choiceIndex;
            }
        }

        private void HandleSubtitleAdvanceClicked()
        {
            if (!isPausedByDisconnect && !isWaitingForPlayerChoice && !_isInScriptedClimax)
            {
                _advanceLineRequested = true;
            }
        }

        private void HandleBookSubscriberClicked(string subscriberBookKey, string displayName)
        {
            if (isPausedByDisconnect || activeWaitAction != DialogueWaitAction.WaitClickSubscriberInBook)
            {
                return;
            }

            string expectedKey = SubscriberIdUtility.ToBookKey(_expectedSubscriber);
            if (string.IsNullOrEmpty(expectedKey) ||
                string.Equals(subscriberBookKey, expectedKey, StringComparison.OrdinalIgnoreCase))
            {
                _waitConditionSatisfied = true;
            }
        }

        private void HandleAnyPortConnected(SubscriberId port)
        {
            // Если игрок воткнул штекер обратно в порт поставленного на паузу звонка — продолжаем ровно с того же места!
            if (activeCall != null && isPausedByDisconnect && port == activeCall.subscriberPort)
            {
                ResumeActiveCallAfterReconnect();
                return;
            }

            if (!isPausedByDisconnect && activeWaitAction == DialogueWaitAction.WaitConnectPort)
            {
                if (_expectedSubscriber == SubscriberId.None || port == _expectedSubscriber)
                {
                    _waitConditionSatisfied = true;
                }
                else if (activeCall != null && port != activeCall.subscriberPort && !string.IsNullOrWhiteSpace(_activeWrongPortHint))
                {
                    subtitleUI?.ShowActionHint(_activeWrongPortHint);
                }
            }
        }

        private void HandleAnyPortDisconnected(SubscriberId port)
        {
            if (activeWaitAction == DialogueWaitAction.WaitDisconnectPort &&
                (_expectedSubscriber == SubscriberId.None || port == _expectedSubscriber))
            {
                _waitConditionSatisfied = true;
                return;
            }

            if (activeWaitAction == DialogueWaitAction.WaitAllPlugsDisconnected)
            {
                if (lineRouter == null || lineRouter.AreAllPlugsAtBase())
                {
                    _waitConditionSatisfied = true;
                }
                return;
            }

            if (activeCall == null || port != activeCall.subscriberPort || _primaryLineHungUp || _isInScriptedClimax)
            {
                return;
            }

            // По умолчанию нативно ставим диалог на паузу до повторного подключения штекера.
            if (activeCall.pauseAndResumeOnDisconnect)
            {
                PauseActiveCallByDisconnect();
                return;
            }

            // Явный сброс/отказ (если звонок специально настроен как отказ по выдёргиванию).
            if (activeCall.treatHangUpAsRefusal)
            {
                if (flagsState != null && activeCall.flagOnHangUp != StoryFlag.None)
                {
                    flagsState.SetFlag(activeCall.flagOnHangUp, activeCall.flagOnHangUpValue);
                }

                flagsState?.MarkCallCompleted(activeCall.callId);
                StopCurrentCall(notifyFinished: true, hungUpEarly: true);
            }
        }

        private void StartCallerAmbient(CallScenarioAsset call)
        {
            if (callerAmbientSource == null) return;
            callerAmbientSource.Stop();
            AudioClip clip = call.callerAmbientLoop != null
                ? call.callerAmbientLoop
                : ProceduralRadioAudio.GetCallerAmbient(call.proceduralAmbientProfile);
            if (clip != null)
            {
                callerAmbientSource.clip = clip;
                callerAmbientSource.loop = true;
                callerAmbientSource.volume = call.callerAmbientVolume;
                callerAmbientSource.Play();
            }
        }

        private void StartSecondaryAmbient(AudioClip customClip, SubscriberAmbientProfile profile, float volume)
        {
            EnsureAudioSources();
            if (secondaryAmbientSource == null) return;
            secondaryAmbientSource.Stop();
            AudioClip clip = customClip != null ? customClip : ProceduralRadioAudio.GetCallerAmbient(profile);
            if (clip != null)
            {
                secondaryAmbientSource.clip = clip;
                secondaryAmbientSource.loop = true;
                secondaryAmbientSource.volume = Mathf.Clamp01(volume);
                secondaryAmbientSource.Play();
            }
        }

        private IEnumerator WaitPausableSeconds(float seconds, int token)
        {
            float remaining = seconds;
            while (remaining > 0f && token == _runToken)
            {
                if (!isPausedByDisconnect || _isInScriptedClimax)
                {
                    remaining -= Time.deltaTime;
                }
                yield return null;
            }
        }

        private IEnumerator WaitUnscaledSeconds(float seconds, int token)
        {
            float remaining = seconds;
            while (remaining > 0f && token == _runToken)
            {
                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static void StopSource(AudioSource src)
        {
            if (src != null)
            {
                src.Stop();
                src.clip = null;
            }
        }

        private void EnsureAudioSources()
        {
            if (voiceAudioSource == null) voiceAudioSource = gameObject.AddComponent<AudioSource>();
            voiceAudioSource.playOnAwake = false;
            voiceAudioSource.loop = false;
            voiceAudioSource.spatialBlend = 0f;

            if (callerAmbientSource == null) callerAmbientSource = gameObject.AddComponent<AudioSource>();
            callerAmbientSource.playOnAwake = false;
            callerAmbientSource.loop = true;
            callerAmbientSource.spatialBlend = 0f;

            if (secondaryAmbientSource == null)
            {
                GameObject secGo = new GameObject("SecondaryCallerAmbientSource");
                secGo.transform.SetParent(transform, false);
                secondaryAmbientSource = secGo.AddComponent<AudioSource>();
                var hp = secGo.AddComponent<AudioHighPassFilter>();
                hp.cutoffFrequency = 320f;
                var lp = secGo.AddComponent<AudioLowPassFilter>();
                lp.cutoffFrequency = 3100f;
            }
            secondaryAmbientSource.playOnAwake = false;
            secondaryAmbientSource.loop = true;
            secondaryAmbientSource.spatialBlend = 0f;

            if (climaxSfxSource == null)
            {
                GameObject sfxGo = new GameObject("ClimaxRadioSfxSource");
                sfxGo.transform.SetParent(transform, false);
                climaxSfxSource = sfxGo.AddComponent<AudioSource>();
            }
            climaxSfxSource.playOnAwake = false;
            climaxSfxSource.loop = false;
            climaxSfxSource.spatialBlend = 0f;
        }
    }
}
