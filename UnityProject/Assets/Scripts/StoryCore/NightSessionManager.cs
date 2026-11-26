using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LastSignal.StoryCore
{
    /// <summary>
    /// Главный оркестратор смен (Ночь 1, Ночь 2, Ночь 3).
    /// Управляет игровыми часами, расписанием входящих (1–3 параллельные линии) и исходящих звонков,
    /// сохранениями между ночами, титрами переходов и запуском концовок.
    /// </summary>
    public sealed class NightSessionManager : MonoBehaviour
    {
        [Header("Ссылки на модули ядра")]
        [SerializeField] private StoryFlagsState flagsState;
        [SerializeField] private SwitchboardLineRouter lineRouter;
        [SerializeField] private DialogueRunner dialogueRunner;
        [SerializeField] private DeskClockController deskClock;
        [SerializeField] private RoomAtmosphereController roomAtmosphere;
        [SerializeField] private HeadphonesActionController headphonesAction;
        [SerializeField] private IntermissionAndEndingUI intermissionUI;
        [SerializeField] private LogbookSpreadController spreadController;

        [Header("Расписание трёх ночей (ScriptableObject)")]
        [SerializeField] private List<NightScheduleAsset> nightSchedules = new List<NightScheduleAsset>();

        [Header("Параметры запуска")]
        [Tooltip("Автоматически начинать Ночь 1 при нажатии Play.")]
        [SerializeField] private bool autoStartOnPlay = false;
        [SerializeField] private bool showOpeningTitleCard = true;
        [SerializeField, Range(0, 2)] private int startNightIndex = 0;

        [Header("Живое состояние смены (только чтение в Inspector)")]
        [SerializeField] private int activeNightIndex = -1;
        [SerializeField] private string currentClockDisplay = "23:40";
        [SerializeField] private int remainingScheduledCallsCount;
        [SerializeField] private bool nightRunning;

        private readonly List<CallScenarioAsset> _pendingIncomingCalls = new List<CallScenarioAsset>();
        private readonly List<CallScenarioAsset> _availableOutgoingCalls = new List<CallScenarioAsset>();
        private readonly Queue<CallScenarioAsset> _answeredCallQueue = new Queue<CallScenarioAsset>();

        private NightScheduleAsset _currentSchedule;
        private float _sequenceCooldownTimer;
        private bool _endingTriggered;
        private Coroutine _transitionCoroutine;

        public NightScheduleAsset CurrentSchedule => _currentSchedule;
        public bool IsNightRunning => nightRunning;

        public event Action<NightScheduleAsset> OnNightStarted;
        public event Action<NightScheduleAsset> OnNightCompleted;
        public event Action<EndingType> OnEndingTriggered;

        private void OnEnable()
        {
            if (lineRouter != null)
            {
                lineRouter.OnIncomingCallAnswered += HandleIncomingCallAnswered;
                lineRouter.OnIncomingCallTimedOut += HandleIncomingCallTimedOut;
                lineRouter.OnNonRingingPortConnected += HandleNonRingingPortConnected;
            }

            if (dialogueRunner != null)
            {
                dialogueRunner.OnCallFinished += HandleCallFinished;
                dialogueRunner.OnEndingRequested += TriggerEnding;
            }

            if (headphonesAction != null)
            {
                headphonesAction.OnHeadphonesRemoved += HandleHeadphonesRemoved;
            }
        }

        private void OnDisable()
        {
            if (lineRouter != null)
            {
                lineRouter.OnIncomingCallAnswered -= HandleIncomingCallAnswered;
                lineRouter.OnIncomingCallTimedOut -= HandleIncomingCallTimedOut;
                lineRouter.OnNonRingingPortConnected -= HandleNonRingingPortConnected;
            }

            if (dialogueRunner != null)
            {
                dialogueRunner.OnCallFinished -= HandleCallFinished;
                dialogueRunner.OnEndingRequested -= TriggerEnding;
            }

            if (headphonesAction != null)
            {
                headphonesAction.OnHeadphonesRemoved -= HandleHeadphonesRemoved;
            }
        }

        private void Start()
        {
            if (autoStartOnPlay && nightSchedules.Count > 0)
            {
                StartNight(Mathf.Clamp(startNightIndex, 0, nightSchedules.Count - 1));
            }
        }

        private void Update()
        {
            if (deskClock != null)
            {
                currentClockDisplay = deskClock.FormattedTime;
            }

            if (!nightRunning || _endingTriggered || _currentSchedule == null)
            {
                return;
            }

            if (_sequenceCooldownTimer > 0f)
            {
                _sequenceCooldownTimer -= Time.deltaTime;
            }

            TryDispatchScheduledCalls();
            TryStartNextQueuedDialogue();
            CheckNightCompletion();
        }

        [ContextMenu("Запустить Ночь 1")]
        public void StartNight1FromMenu() => StartNight(0);

        [ContextMenu("Запустить Ночь 2")]
        public void StartNight2FromMenu() => StartNight(1);

        [ContextMenu("Запустить Ночь 3")]
        public void StartNight3FromMenu() => StartNight(2);

        /// <summary>
        /// Запускает указанную смену (0 = Ночь 1, 1 = Ночь 2, 2 = Ночь 3) с сохранением контрольной точки.
        /// </summary>
        public void StartNight(int scheduleIndex)
        {
            if (scheduleIndex < 0 || scheduleIndex >= nightSchedules.Count || nightSchedules[scheduleIndex] == null)
            {
                Debug.LogWarning($"[NightSessionManager] Не найден NightScheduleAsset по индексу {scheduleIndex}.", this);
                return;
            }

            if (_transitionCoroutine != null)
            {
                StopCoroutine(_transitionCoroutine);
                _transitionCoroutine = null;
            }

            _endingTriggered = false;
            nightRunning = false;
            activeNightIndex = scheduleIndex;
            _currentSchedule = nightSchedules[scheduleIndex];

            dialogueRunner?.StopCurrentCall(notifyFinished: false);
            lineRouter?.ClearAllRingingLines();
            _pendingIncomingCalls.Clear();
            _availableOutgoingCalls.Clear();
            _answeredCallQueue.Clear();

            if (flagsState != null)
            {
                flagsState.SetCurrentNight(_currentSchedule.nightNumber);
                if (_currentSchedule.hasReservePassword)
                {
                    flagsState.UnlockReservePasswordTable();
                }
                flagsState.SaveNightCheckpoint(_currentSchedule.nightNumber);
            }

            if (roomAtmosphere != null)
            {
                roomAtmosphere.ApplyNightStage(_currentSchedule.initialRoomStage);
            }

            if (headphonesAction != null)
            {
                headphonesAction.IsRemovalAllowed = _currentSchedule.allowHeadphonesRemovalEnding;
            }

            if (deskClock != null)
            {
                deskClock.SetClock(_currentSchedule.startHour, _currentSchedule.startMinute, _currentSchedule.gameMinutesPerRealSecond);
                deskClock.SetRunning(false);
            }

            if (spreadController != null)
            {
                spreadController.RefreshSpread2();
            }

            BuildCallQueuesForSchedule(_currentSchedule);

            if (showOpeningTitleCard && intermissionUI != null && !string.IsNullOrWhiteSpace(_currentSchedule.openingTypewriterText))
            {
                _transitionCoroutine = StartCoroutine(BeginNightAfterOpeningCardRoutine(_currentSchedule));
            }
            else
            {
                ActivateNightGameplay();
            }
        }

        /// <summary>
        /// Завершает игру одной из шести концовок ГДД и открывает лист итогов.
        /// </summary>
        public void TriggerEnding(EndingType ending)
        {
            if (_endingTriggered || ending == EndingType.None)
            {
                return;
            }

            _endingTriggered = true;
            nightRunning = false;
            deskClock?.SetRunning(false);
            dialogueRunner?.StopCurrentCall(notifyFinished: false);
            lineRouter?.ClearAllRingingLines();

            OnEndingTriggered?.Invoke(ending);

            if (intermissionUI != null)
            {
                intermissionUI.ShowEndingAndSummary(ending, flagsState, RestartFromCurrentNightOrBeginning);
            }
        }

        private IEnumerator BeginNightAfterOpeningCardRoutine(NightScheduleAsset schedule)
        {
            bool finished = false;
            intermissionUI.ShowTypewriterCard(schedule.openingTypewriterText, schedule.openingCardDuration, () => finished = true);
            while (!finished)
            {
                yield return null;
            }

            _transitionCoroutine = null;
            ActivateNightGameplay();
        }

        private void ActivateNightGameplay()
        {
            nightRunning = true;
            _sequenceCooldownTimer = 0.5f;
            deskClock?.SetRunning(true);
            OnNightStarted?.Invoke(_currentSchedule);
        }

        private void BuildCallQueuesForSchedule(NightScheduleAsset schedule)
        {
            if (schedule.calls == null)
            {
                return;
            }

            for (int i = 0; i < schedule.calls.Count; i++)
            {
                CallScenarioAsset call = schedule.calls[i];
                if (call == null)
                {
                    continue;
                }

                if (call.isPlayerInitiatedOutgoing)
                {
                    _availableOutgoingCalls.Add(call);
                }
                else
                {
                    _pendingIncomingCalls.Add(call);
                }
            }

            remainingScheduledCallsCount = _pendingIncomingCalls.Count;
        }

        private void TryDispatchScheduledCalls()
        {
            if (_pendingIncomingCalls.Count == 0 || lineRouter == null)
            {
                return;
            }

            int activeLines = lineRouter.RingingCount + (dialogueRunner != null && dialogueRunner.IsRunning ? 1 : 0);
            if (activeLines >= _currentSchedule.maxSimultaneousIncomingLines)
            {
                return;
            }

            for (int i = 0; i < _pendingIncomingCalls.Count; i++)
            {
                CallScenarioAsset candidate = _pendingIncomingCalls[i];
                if (!AreCallPrerequisitesMet(candidate))
                {
                    continue;
                }

                bool readyByTime = candidate.startImmediatelyInSequence
                    ? (_sequenceCooldownTimer <= 0f && activeLines == 0)
                    : IsClockAtOrPast(candidate.scheduledHour, candidate.scheduledMinute);

                if (!readyByTime)
                {
                    continue;
                }

                if (lineRouter.IsPortRinging(candidate.subscriberPort))
                {
                    continue;
                }

                float patience = candidate.patienceOverrideSeconds < 0f
                    ? 0f
                    : (candidate.patienceOverrideSeconds > 0f ? candidate.patienceOverrideSeconds : _currentSchedule.defaultPatienceSeconds);

                _pendingIncomingCalls.RemoveAt(i);
                remainingScheduledCallsCount = _pendingIncomingCalls.Count;
                lineRouter.BeginIncomingRing(candidate, patience);
                return;
            }
        }

        private bool AreCallPrerequisitesMet(CallScenarioAsset call)
        {
            if (flagsState == null)
            {
                return true;
            }

            if (!flagsState.CheckCondition(call.requiredFlag, call.forbiddenFlag, null))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(call.requiredCompletedCallId) &&
                !flagsState.IsCallCompleted(call.requiredCompletedCallId))
            {
                return false;
            }

            return true;
        }

        private bool IsClockAtOrPast(int hour, int minute)
        {
            if (deskClock == null)
            {
                return true;
            }

            float startMin = NormalizeNightMinutes(_currentSchedule.startHour, _currentSchedule.startMinute);
            float targetMin = NormalizeNightMinutes(hour, minute);
            if (targetMin < startMin)
            {
                targetMin += 1440f;
            }

            float currentMin = NormalizeNightMinutes(deskClock.CurrentHour, deskClock.CurrentMinute);
            if (currentMin < startMin)
            {
                currentMin += 1440f;
            }

            return currentMin >= targetMin;
        }

        private static float NormalizeNightMinutes(int hour, int minute)
        {
            return Mathf.Repeat(hour * 60f + minute, 1440f);
        }

        private void HandleIncomingCallAnswered(CallScenarioAsset call, SubscriberId port)
        {
            if (call == null)
            {
                return;
            }

            if (dialogueRunner != null && !dialogueRunner.IsRunning)
            {
                dialogueRunner.StartCall(call);
            }
            else
            {
                _answeredCallQueue.Enqueue(call);
            }
        }

        private void HandleIncomingCallTimedOut(CallScenarioAsset call, SubscriberId port)
        {
            if (call == null)
            {
                return;
            }

            if (flagsState != null && call.flagOnHangUp != StoryFlag.None)
            {
                flagsState.SetFlag(call.flagOnHangUp, call.flagOnHangUpValue);
            }

            flagsState?.MarkCallCompleted(call.callId);
            _sequenceCooldownTimer = 1.0f;
        }

        private void HandleNonRingingPortConnected(SubscriberId port)
        {
            if (!nightRunning || _endingTriggered)
            {
                return;
            }

            // Если диалог уже идёт (например, ждёт подключения 2-го шнура) — не перебиваем его исходящим звонком.
            if (dialogueRunner != null && dialogueRunner.IsRunning)
            {
                return;
            }

            // Ищем доступный исходящий сценарий для этого абонента (например, игрок сам звонит Грому, Ромашке или Кобре).
            for (int i = 0; i < _availableOutgoingCalls.Count; i++)
            {
                CallScenarioAsset outgoing = _availableOutgoingCalls[i];
                if (outgoing.subscriberPort == port && AreCallPrerequisitesMet(outgoing))
                {
                    _availableOutgoingCalls.RemoveAt(i);
                    dialogueRunner?.StartCall(outgoing);
                    return;
                }
            }
        }

        private void HandleCallFinished(CallScenarioAsset call, bool hungUpEarly)
        {
            _sequenceCooldownTimer = call != null ? Mathf.Max(0.5f, call.delayBeforeRingingSeconds) : 1.0f;
            TryStartNextQueuedDialogue();
        }

        private void TryStartNextQueuedDialogue()
        {
            if (dialogueRunner == null || dialogueRunner.IsRunning || _answeredCallQueue.Count == 0)
            {
                return;
            }

            CallScenarioAsset next = _answeredCallQueue.Dequeue();
            if (next != null)
            {
                dialogueRunner.StartCall(next);
            }
        }

        private void CheckNightCompletion()
        {
            if (_pendingIncomingCalls.Count > 0 ||
                _answeredCallQueue.Count > 0 ||
                (lineRouter != null && lineRouter.RingingCount > 0) ||
                (dialogueRunner != null && dialogueRunner.IsRunning))
            {
                return;
            }

            nightRunning = false;
            deskClock?.SetRunning(false);
            OnNightCompleted?.Invoke(_currentSchedule);

            _transitionCoroutine = StartCoroutine(FinishNightAndAdvanceRoutine(_currentSchedule));
        }

        private IEnumerator FinishNightAndAdvanceRoutine(NightScheduleAsset finishedSchedule)
        {
            if (intermissionUI != null && !string.IsNullOrWhiteSpace(finishedSchedule.closingTypewriterText))
            {
                bool done = false;
                intermissionUI.ShowTypewriterCard(finishedSchedule.closingTypewriterText, finishedSchedule.closingCardDuration, () => done = true);
                while (!done)
                {
                    yield return null;
                }
            }

            _transitionCoroutine = null;

            int nextIndex = activeNightIndex + 1;
            if (nextIndex < nightSchedules.Count && nightSchedules[nextIndex] != null)
            {
                StartNight(nextIndex);
            }
        }

        private void HandleHeadphonesRemoved()
        {
            if (_currentSchedule != null && _currentSchedule.allowHeadphonesRemovalEnding)
            {
                TriggerEnding(EndingType.Silence);
            }
        }

        private void RestartFromCurrentNightOrBeginning()
        {
            if (flagsState != null)
            {
                flagsState.ResetAll();
            }

            intermissionUI?.HideImmediate();
            if (nightSchedules.Count > 0)
            {
                StartNight(0);
            }
        }
    }
}
