using System.Collections.Generic;
using LastSignal.Dialogue;
using LastSignal.Switchboard;
using UnityEngine;

namespace LastSignal.Scenario
{
    /// <summary>
    /// Что делает сценарный блок, если игрок выдернул штекер прямо во время диалога.
    /// </summary>
    public enum DisconnectBehavior
    {
        [Tooltip("Поставить диалог на паузу, снова замигать лампой и продолжить с того же места, когда игрок воткнёт штекер обратно.")]
        PauseAndWaitReconnect = 0,

        [Tooltip("Немедленно оборвать звонок и засчитать его как сорванный (Failed).")]
        AbortAndFail = 1,

        [Tooltip("Завершить разговор досрочно и перейти к следующему этапу.")]
        FinishEarly = 2
    }

    /// <summary>
    /// Текущая фаза выполнения активного сценарного блока.
    /// </summary>
    public enum ScenarioBlockPhase
    {
        Idle = 0,
        DelayBeforeRing = 1,
        Ringing = 2,
        PlayingOpening = 3,
        WaitingForSecondConnector = 4,
        PlayingWrongTargetFeedback = 5,
        PlayingBridgedDialogue = 6,
        WaitingForUnplug = 7,
        Completed = 8,
        Missed = 9,
        Failed = 10
    }

    /// <summary>
    /// Отдельный сценарный блок коммутатора («Блок 1 — Берёза», «Блок 2 — Кузница -> Кобра» и т.д.).
    /// Хранится как отдельный ScriptableObject-ассет: удобно создавать, редактировать и переиспользовать без конфликтов в сцене.
    /// </summary>
    [CreateAssetMenu(fileName = "CallBlock_New", menuName = "Last Signal/Call Scenario Block", order = 1)]
    public sealed class CallBlockAsset : ScriptableObject
    {
        [Header("1. Идентификация блока")]
        [SerializeField] private string blockId = "Block_01";
        [SerializeField] private string displayTitle = "Блок 1 — Входящий вызов";

        [Header("2. Входящий сигнал на коммутаторе")]
        [Tooltip("Какая лампочка начнёт мигать и в какое гнездо игрок должен воткнуть 1-й штекер.")]
        [SerializeField] private SwitchboardChannelId callerChannel = SwitchboardChannelId.Bereza;

        [Tooltip("Пауза (в секундах) после запуска блока до того, как начнёт мигать лампочка.")]
        [SerializeField, Min(0f)] private float delayBeforeRing = 1.0f;

        [Tooltip("Терпение абонента в секундах (0 = ждёт бесконечно, как в Ночи 1; 30/15 — как в Ночах 2–3).")]
        [SerializeField, Min(0f)] private float patienceSeconds = 0f;

        [Tooltip("При какой доле оставшегося терпения лампа переходит в частое тревожное мигание (Urgent).")]
        [SerializeField, Range(0.05f, 0.8f)] private float urgentThresholdRatio = 0.35f;

        [Header("3. Поведение потока и разрыва связи")]
        [SerializeField] private DisconnectBehavior disconnectBehavior = DisconnectBehavior.PauseAndWaitReconnect;

        [Tooltip("Если включено — после окончания реплик блок ждёт, пока игрок сам выдернет штекеры (реалистичное завершение связи).")]
        [SerializeField] private bool waitForUnplugToComplete = false;

        [Tooltip("Автоматически запускать следующий блок в очереди контроллера после завершения этого.")]
        [SerializeField] private bool autoAdvanceToNextBlock = true;

        [Header("4. Этап 1 — Реплики при ответе (1-й штекер в Caller)")]
        [SerializeField] private List<DialogueLineData> openingLines = new List<DialogueLineData>();

        [Header("5. Этап 2 — Ожидание второго коннектора (опционально)")]
        [Tooltip("Включите, если абонент просит соединить его с другим конкретным каналом.")]
        [SerializeField] private bool requiresSecondConnector = false;

        [Tooltip("Конкретный абонент, в гнездо которого игрок должен воткнуть 2-й штекер.")]
        [SerializeField] private SwitchboardChannelId targetChannel = SwitchboardChannelId.Kobra;

        [Tooltip("Подсвечивать ли целевую лампу мягким пульсом (WaitingTarget), пока игрок ищет гнездо.")]
        [SerializeField] private bool highlightTargetLamp = false;

        [Tooltip("Реплики, если игрок воткнул 2-й штекер НЕ в того абонента (после этого система продолжает ждать правильного).")]
        [SerializeField] private List<DialogueLineData> wrongTargetLines = new List<DialogueLineData>();

        [Tooltip("Диалог после того, как оба штекера соединили Caller и Target.")]
        [SerializeField] private List<DialogueLineData> bridgedLines = new List<DialogueLineData>();

        [Header("6. Последствия и сигналы дирижёру")]
        [Tooltip("Пометить линию вызывающего как Dead после завершения блока (например, гибель «Берёзы»).")]
        [SerializeField] private bool markCallerDeadOnComplete = false;

        [SerializeField] private string signalOnRinging = string.Empty;
        [SerializeField] private string signalOnAnswered = string.Empty;
        [SerializeField] private string signalOnWaitingTarget = string.Empty;
        [SerializeField] private string signalOnBridged = string.Empty;
        [SerializeField] private string signalOnCompleted = string.Empty;
        [SerializeField] private string signalOnMissedOrFailed = string.Empty;

        public string BlockId => blockId;
        public string DisplayTitle => displayTitle;
        public SwitchboardChannelId CallerChannel => callerChannel;
        public float DelayBeforeRing => delayBeforeRing;
        public float PatienceSeconds => patienceSeconds;
        public float UrgentThresholdRatio => urgentThresholdRatio;
        public DisconnectBehavior DisconnectBehavior => disconnectBehavior;
        public bool WaitForUnplugToComplete => waitForUnplugToComplete;
        public bool AutoAdvanceToNextBlock => autoAdvanceToNextBlock;

        public IReadOnlyList<DialogueLineData> OpeningLines => openingLines;
        public bool RequiresSecondConnector => requiresSecondConnector;
        public SwitchboardChannelId TargetChannel => targetChannel;
        public bool HighlightTargetLamp => highlightTargetLamp;
        public IReadOnlyList<DialogueLineData> WrongTargetLines => wrongTargetLines;
        public IReadOnlyList<DialogueLineData> BridgedLines => bridgedLines;

        public bool MarkCallerDeadOnComplete => markCallerDeadOnComplete;
        public string SignalOnRinging => signalOnRinging;
        public string SignalOnAnswered => signalOnAnswered;
        public string SignalOnWaitingTarget => signalOnWaitingTarget;
        public string SignalOnBridged => signalOnBridged;
        public string SignalOnCompleted => signalOnCompleted;
        public string SignalOnMissedOrFailed => signalOnMissedOrFailed;

        /// <summary>
        /// Программная инициализация данных (удобно для генерации стартовых ассетов в редакторе).
        /// </summary>
        public void ConfigureBasic(
            string id,
            string title,
            SwitchboardChannelId caller,
            float delaySeconds,
            float patience,
            List<DialogueLineData> opening,
            bool requireSecond = false,
            SwitchboardChannelId target = SwitchboardChannelId.None,
            List<DialogueLineData> wrongTarget = null,
            List<DialogueLineData> bridged = null,
            bool waitForUnplug = false)
        {
            blockId = id;
            displayTitle = title;
            callerChannel = caller;
            delayBeforeRing = Mathf.Max(0f, delaySeconds);
            patienceSeconds = Mathf.Max(0f, patience);
            openingLines = opening ?? new List<DialogueLineData>();
            requiresSecondConnector = requireSecond;
            targetChannel = target;
            wrongTargetLines = wrongTarget ?? new List<DialogueLineData>();
            bridgedLines = bridged ?? new List<DialogueLineData>();
            waitForUnplugToComplete = waitForUnplug;
        }
    }
}
