using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LastSignal.SwitchboardSystem;

namespace LastSignal.StoryCore
{
    /// <summary>
    /// Исполнитель ветвящихся звонков (CallScenarioAsset).
    /// Проигрывает голос и фон абонента, посимвольно выводит субтитры с кликабельными словами,
    /// ждёт игровых действий (книга, штекер, радио), показывает выборы Ларина и НАТИВНО ставит
    /// разговор на паузу при выдёргивании штекера, продолжая его ровно с того же места после повторного подключения.
    /// </summary>
    public sealed class DialogueRunner : MonoBehaviour
    {
        [Header("Ссылки на системы ядра")]
        [SerializeField] private StoryFlagsState flagsState;
        [SerializeField] private SwitchboardLineRouter lineRouter;
        [SerializeField] private SubtitleDialogueUI subtitleUI;
        [SerializeField] private RoomAtmosphereController roomAtmosphere;
        [SerializeField] private RadioSignalMixer radioSignalMixer;

        [Header("Ссылки на предметы комнаты")]
        [SerializeField] private BookInspectController bookInspect;
        [SerializeField] private LogbookSubscribersPage subscribersPage;
        [SerializeField] private LogbookNotesPage notesPage;
        [SerializeField] private LogbookPencilAnimator pencilAnimator;
        [SerializeField] private LogbookSpreadController spreadController;

        [Header("Источники звука эфира")]
        [SerializeField] private AudioSource voiceAudioSource;
        [SerializeField] private AudioSource callerAmbientSource;
        [SerializeField, Range(0f, 1f)] private float voiceVolume = 0.92f;

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
        private SubscriberId _expectedSubscriber;
        private readonly List<DialogueChoiceData> _visibleChoiceData = new List<DialogueChoiceData>();

        public bool IsRunning => activeCall != null;
        public bool IsPausedByDisconnect => isPausedByDisconnect;
        public CallScenarioAsset ActiveCall => activeCall;

        public event Action<CallScenarioAsset, bool> OnCallFinished;
        public event Action<EndingType> OnEndingRequested;

        private void Awake()
        {
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

        public void StartCall(CallScenarioAsset call)
        {
            if (call == null)
            {
                return;
            }

            StopCurrentCall(notifyFinished: false);

            activeCall = call;
            activeNodeId = string.Empty;
            isPausedByDisconnect = false;
            _runToken++;

            StartCallerAmbient(call);

            if (radioSignalMixer != null)
            {
                radioSignalMixer.SetCallTuningProfile(
                    call.requiresRadioTuning,
                    call.targetRadioFrequency01,
                    call.radioTuningTolerance);
            }

            _callCoroutine = StartCoroutine(RunCallRoutine(call, _runToken));
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

            activeCall = null;
            activeNodeId = string.Empty;
            isPausedByDisconnect = false;
            isWaitingForPlayerChoice = false;
            activeWaitAction = DialogueWaitAction.None;
            _selectedChoiceIndex = -1;
            _waitConditionSatisfied = false;

            if (voiceAudioSource != null)
            {
                voiceAudioSource.Stop();
                voiceAudioSource.clip = null;
            }

            if (callerAmbientSource != null)
            {
                callerAmbientSource.Stop();
                callerAmbientSource.clip = null;
            }

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

        private IEnumerator RunCallRoutine(CallScenarioAsset call, int token)
        {
            DialogueNodeData current = call.GetEntryNode();

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

                // 1. Показываем субтитры с посимвольным появлением (синхронизированным с длительностью реплики).
                if (subtitleUI != null && !string.IsNullOrWhiteSpace(current.subtitleText))
                {
                    subtitleUI.HideChoices();
                    float expectedDur = current.voiceClip != null ? current.voiceClip.length : current.fallbackDuration;
                    subtitleUI.ShowLine(
                        current.speakerName,
                        current.speakerColor,
                        current.subtitleText,
                        id => flagsState != null && flagsState.HasKeyword(id),
                        targetRevealDuration: expectedDur);
                }

                // 2. Проигрываем озвучку (или ждём fallbackDuration с поддержкой нативной паузы при выдёргивании шнура).
                yield return PlayVoiceOrWaitRoutine(current, token);
                if (token != _runToken) yield break;

                // 3. Применяем немедленные последствия узла.
                ApplyNodeEffects(current);

                if (current.endingToTrigger != EndingType.None)
                {
                    EndingType ending = current.endingToTrigger;
                    StopCurrentCall(notifyFinished: false);
                    OnEndingRequested?.Invoke(ending);
                    yield break;
                }

                // 4. Ожидание игрового действия (если задано).
                if (current.waitAction != DialogueWaitAction.None)
                {
                    yield return WaitForGameplayActionRoutine(current, token);
                    if (token != _runToken) yield break;
                }

                // 5. Варианты ответа Ларина.
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

                // 6. Короткая пауза перед следующей репликой.
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

        private IEnumerator WaitWhilePausedByDisconnect(int token)
        {
            while (isPausedByDisconnect && token == _runToken)
            {
                yield return null;
            }
        }

        private IEnumerator PlayVoiceOrWaitRoutine(DialogueNodeData node, int token)
        {
            if (node.voiceClip != null && voiceAudioSource != null)
            {
                voiceAudioSource.Stop();
                voiceAudioSource.clip = node.voiceClip;
                voiceAudioSource.volume = voiceVolume;
                voiceAudioSource.Play();

                float duration = node.voiceClip.length;
                float elapsed = 0f;
                bool started = false;

                while (token == _runToken)
                {
                    if (isPausedByDisconnect || AudioListener.pause || !Application.isFocused)
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

                yield break;
            }

            // Текстовый режим (пока озвучка не назначена): ждём как минимум до завершения посимвольной печати и fallbackDuration.
            if (!string.IsNullOrWhiteSpace(node.subtitleText))
            {
                float remaining = Mathf.Max(0.8f, node.fallbackDuration);
                while (token == _runToken)
                {
                    if (!isPausedByDisconnect)
                    {
                        remaining -= Time.deltaTime;
                        bool revealDone = subtitleUI == null || subtitleUI.IsLineRevealFinished;
                        if (remaining <= 0f && revealDone)
                        {
                            break;
                        }
                    }

                    yield return null;
                }
            }
        }

        private IEnumerator WaitForGameplayActionRoutine(DialogueNodeData node, int token)
        {
            activeWaitAction = node.waitAction;
            _expectedSubscriber = node.expectedSubscriber;
            _waitConditionSatisfied = CheckImmediateWaitCondition(node.waitAction, node.expectedSubscriber);

            if (!_waitConditionSatisfied && subtitleUI != null && !string.IsNullOrWhiteSpace(node.actionHintText))
            {
                subtitleUI.ShowActionHint(node.actionHintText);
            }

            while (!_waitConditionSatisfied && token == _runToken)
            {
                if (!isPausedByDisconnect)
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
                }

                yield return null;
            }

            activeWaitAction = DialogueWaitAction.None;
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
                _ => false
            };
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
            if (isPausedByDisconnect || activeCall == null)
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

            if (pencilAnimator != null)
            {
                pencilAnimator.PlayShortScratchSound();
            }

            if (spreadController != null)
            {
                spreadController.RefreshSpread2();
            }

            if (subtitleUI != null)
            {
                subtitleUI.RefreshRecordedKeywords(id => flagsState.HasKeyword(id));
            }
        }

        private void HandleChoiceSelected(int choiceIndex)
        {
            if (isWaitingForPlayerChoice && !isPausedByDisconnect)
            {
                _selectedChoiceIndex = choiceIndex;
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

            if (!isPausedByDisconnect &&
                activeWaitAction == DialogueWaitAction.WaitConnectPort &&
                (_expectedSubscriber == SubscriberId.None || port == _expectedSubscriber))
            {
                _waitConditionSatisfied = true;
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

            if (activeCall == null || port != activeCall.subscriberPort)
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
            if (call.callerAmbientLoop != null)
            {
                callerAmbientSource.clip = call.callerAmbientLoop;
                callerAmbientSource.loop = true;
                callerAmbientSource.volume = call.callerAmbientVolume;
                callerAmbientSource.Play();
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
        }
    }
}
