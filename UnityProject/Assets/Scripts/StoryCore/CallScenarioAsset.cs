using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastSignal.StoryCore
{
    /// <summary>
    /// Вариант реплики или действия Ларина внутри узла диалога.
    /// Поддерживает проверку записанных слов из журнала и сюжетных флагов.
    /// </summary>
    [Serializable]
    public sealed class DialogueChoiceData
    {
        [Tooltip("Текст кнопки ответа Ларина.")]
        public string choiceText = "Маяк слушает.";

        [Tooltip("ID следующего узла диалога (nodeId). Если пусто — звонок завершается.")]
        public string nextNodeId = string.Empty;

        [Header("Условия доступности (опционально)")]
        [Tooltip("Если указано — вариант появится только если игрок записал это слово из субтитров (например, '18_v').")]
        public string requiredKeywordId = string.Empty;

        [Tooltip("Показывать недоступный вариант заблокированным вместо скрытия.")]
        public bool showLockedWhenMissingKeyword = false;

        public StoryFlag requiredFlag = StoryFlag.None;
        public StoryFlag forbiddenFlag = StoryFlag.None;

        [Header("Последствия выбора")]
        public StoryFlag flagToSet = StoryFlag.None;
        public bool flagValue = true;

        [Tooltip("Пометка карандашом на полях книги, появляющаяся сразу после выбора.")]
        public string logbookNoteToAdd = string.Empty;

        [Tooltip("Если указано — после выбора сразу срабатывает концовка.")]
        public EndingType endingToTrigger = EndingType.None;
    }

    /// <summary>
    /// Один шаг (реплика или точка ожидания) внутри звонка.
    /// В поле subtitleText можно выделять кликабельные слова простым синтаксисом:
    /// [шестнадцать-Б|sq_16b] — слово подсветится янтарём и по клику запишется в журнал под ID 'sq_16b'.
    /// </summary>
    [Serializable]
    public sealed class DialogueNodeData
    {
        [Tooltip("Уникальное имя узла внутри этого звонка (например: intro, ask_book, password, end).")]
        public string nodeId = "node_01";

        [Header("Реплика и звук")]
        public string speakerName = "ДЁМИН";
        public Color speakerColor = new Color(0.96f, 0.78f, 0.44f, 1f);

        [TextArea(2, 5)]
        [Tooltip("Текст субтитров. Для кликабельного слова используйте формат [видимый текст|id_слова].")]
        public string subtitleText = string.Empty;

        [Tooltip("Аудиоклип озвучки. Если не назначен — используется Fallback Duration.")]
        public AudioClip voiceClip;

        [Tooltip("Длительность показа реплики (сек), если озвучка ещё не записана.")]
        [Min(0.5f)] public float fallbackDuration = 4.5f;

        [Tooltip("Небольшая пауза после завершения реплики перед переходом к следующему узлу.")]
        [Min(0f)] public float postLineDelay = 0.35f;

        [Header("Условие показа (если не выполнено — сразу перейти к skipToNodeId)")]
        public StoryFlag conditionRequiredFlag = StoryFlag.None;
        public StoryFlag conditionForbiddenFlag = StoryFlag.None;
        public string skipToNodeId = string.Empty;

        [Header("Ожидание действия игрока (опционально)")]
        public DialogueWaitAction waitAction = DialogueWaitAction.None;

        [Tooltip("Для WaitClickSubscriberInBook или WaitConnectPort/WaitDisconnectPort.")]
        public SubscriberId expectedSubscriber = SubscriberId.None;

        [Tooltip("Короткая ненавязчивая подсказка внизу экрана, пока диалог ждёт действия (опционально).")]
        public string actionHintText = string.Empty;

        [Header("Реакция книги, флагов и комнаты")]
        [Tooltip("Заметка на полях книги, которую Ларин запишет карандашом в этом узле.")]
        public string logbookNoteToAdd = string.Empty;

        public StoryFlag flagToSet = StoryFlag.None;
        public bool flagValue = true;

        public RoomEventEffect roomEffect = RoomEventEffect.None;
        public EndingType endingToTrigger = EndingType.None;

        [Header("Ветвление и переход")]
        [Tooltip("Если список пуст — автоматический переход в Auto Next Node Id.")]
        public List<DialogueChoiceData> choices = new List<DialogueChoiceData>();

        [Tooltip("ID следующего узла, если выборов нет. Пусто = завершить звонок.")]
        public string autoNextNodeId = string.Empty;
    }

    /// <summary>
    /// Конфигурация одного сюжетного звонка (1 файл = 1 номер из сценария ГДД: 1.1, 1.2, 2.3 и т.д.).
    /// Создаётся через ПКМ в Project -> Create -> Last Signal -> Call Scenario.
    /// </summary>
    [CreateAssetMenu(fileName = "Call_1_1_NewCall", menuName = "Last Signal/Call Scenario", order = 1)]
    public sealed class CallScenarioAsset : ScriptableObject
    {
        [Header("Паспорт звонка")]
        public string callId = "1.1";
        public string callTitle = "1.1. Новый звонок";
        [Range(1, 3)] public int nightNumber = 1;

        [Header("Канал и тип вызова")]
        [Tooltip("Порт коммутатора, на котором приходит (или куда делается) вызов.")]
        public SubscriberId subscriberPort = SubscriberId.Bereza;

        [Tooltip("Если включено — звонок НЕ мигает сам, а запускается, когда игрок САМ втыкает шнур в порт абонента.")]
        public bool isPlayerInitiatedOutgoing = false;

        [Tooltip("Фоновый звук абонента в эфире (гул узла, лес со сверчками, пишущая машинка и т.д.).")]
        public AudioClip callerAmbientLoop;
        [Range(0f, 1f)] public float callerAmbientVolume = 0.28f;

        [Header("Расписание и условия запуска")]
        [Tooltip("Игровое время появления звонка (час 0..23 и минута 0..59).")]
        [Range(0, 23)] public int scheduledHour = 23;
        [Range(0, 59)] public int scheduledMinute = 40;

        [Tooltip("Если включено — звонок стартует по очереди после предыдущего (с задержкой), не дожидаясь точных часов.")]
        public bool startImmediatelyInSequence = true;
        [Min(0f)] public float delayBeforeRingingSeconds = 1.5f;

        [Tooltip("Переопределение терпения абонента (0 = взять из настроек ночи, < 0 = бесконечное терпение).")]
        public float patienceOverrideSeconds = 0f;

        [Tooltip("Условия запуска этого звонка (опционально).")]
        public StoryFlag requiredFlag = StoryFlag.None;
        public StoryFlag forbiddenFlag = StoryFlag.None;
        public string requiredCompletedCallId = string.Empty;

        [Header("Настройка частоты на рации (для общей частоты)")]
        [Tooltip("Если включено — голос и шум зависят от положения ручки радиоприёмника.")]
        public bool requiresRadioTuning = false;
        [Range(0.05f, 0.95f)] public float targetRadioFrequency01 = 0.65f;
        [Range(0.03f, 0.25f)] public float radioTuningTolerance = 0.09f;

        [Header("Реакция на выдёргивание штекера")]
        [Tooltip("Если включено — при выдёргивании штекера диалог ставится на паузу и продолжается с того же места после повторного подключения.")]
        public bool pauseAndResumeOnDisconnect = true;

        [Tooltip("Если пауза отключена и включён этот флаг — выдёргивание штекера считается отказом и завершает звонок.")]
        public bool treatHangUpAsRefusal = false;

        [Tooltip("Флаг, выставляемый при отказе (или истечении терпения).")]
        public StoryFlag flagOnHangUp = StoryFlag.None;
        public bool flagOnHangUpValue = false;

        [Header("Граф узлов диалога")]
        public string entryNodeId = "intro";
        public List<DialogueNodeData> nodes = new List<DialogueNodeData>();

        public DialogueNodeData FindNode(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || nodes == null)
            {
                return null;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] != null && string.Equals(nodes[i].nodeId, nodeId, StringComparison.Ordinal))
                {
                    return nodes[i];
                }
            }

            return null;
        }

        public DialogueNodeData GetEntryNode()
        {
            DialogueNodeData entry = FindNode(entryNodeId);
            if (entry != null)
            {
                return entry;
            }

            return nodes != null && nodes.Count > 0 ? nodes[0] : null;
        }
    }
}
