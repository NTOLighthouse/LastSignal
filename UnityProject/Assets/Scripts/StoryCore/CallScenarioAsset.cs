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
    /// Опциональные аудиоклипы для динамически собранного сообщения корректировки огня (Звонок 1.3).
    /// Если клип не назначен — реплика воспроизводится по скорости чтения субтитров.
    /// </summary>
    [Serializable]
    public sealed class FireMissionVoiceVariant
    {
        public string squareCode = "16-Б";
        public bool oneShell = true;
        public AudioClip larinClip;
        public AudioClip zharovClip;
    }

    /// <summary>
    /// Конфигурация узла кульминации гибели «Берёзы» (Звонок 1.6) со слотами под готовую озвучку и SFX.
    /// </summary>
    [Serializable]
    public sealed class BerezaDeathClimaxData
    {
        public bool enabled = false;
        [Tooltip("Свист приближающегося снаряда (~1.1с). Если пусто — процедурный звук.")]
        public AudioClip shellWhistleClip;
        [Tooltip("Удар и треск в эфире. Если пусто — процедурный звук.")]
        public AudioClip impactAndCrackleClip;
        [Tooltip("Обрывающаяся реплика Дёмина («…Маяк! Маяк, по нам…»).")]
        public AudioClip deminCutOffClip;
        [Tooltip("Белый шум перед резким обрывом (~1.7с). Если пусто — процедурный звук.")]
        public AudioClip whiteNoiseClip;
        [Min(0.5f)] public float whistleDuration = 1.1f;
        [Min(0.5f)] public float whiteNoiseDuration = 1.7f;
        [Min(1f)] public float absoluteSilenceDuration = 10.0f;
        [Min(0.5f)] public float allLampsBlinkDuration = 3.0f;
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

        [Tooltip("Отображать субтитры курсивом (шёпот, вполголоса).")]
        public bool isItalic = false;

        [TextArea(2, 5)]
        [Tooltip("Текст субтитров. Для кликабельного слова используйте формат [видимый текст|id_слова].")]
        public string subtitleText = string.Empty;

        [Tooltip("Аудиоклип озвучки. Если не назначен — используется скорость чтения субтитров / Fallback Duration.")]
        public AudioClip voiceClip;

        [Tooltip("Минимальная длительность показа реплики (сек), если озвучка ещё не назначена (0 = авто по длине текста).")]
        [Min(0f)] public float fallbackDuration = 0f;

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

        [Tooltip("Для WaitRecordKeywords: список ID слов, которые игрок должен кликнуть в субтитрах.")]
        public List<string> requiredKeywordIds = new List<string>();

        [Tooltip("Короткая ненавязчивая подсказка внизу экрана, пока диалог ждёт действия (опционально).")]
        public string actionHintText = string.Empty;

        [Tooltip("Мягкая подсказка, если игрок воткнул штекер не в то гнездо при WaitConnectPort.")]
        public string wrongPortHintText = string.Empty;

        [Header("Подключение/отключение линий на этом узле")]
        [Tooltip("Если указано — при завершении ожидания WaitConnectPort запускает фон второй линии.")]
        public SubscriberAmbientProfile secondaryAmbientProfile = SubscriberAmbientProfile.None;
        public AudioClip secondaryAmbientClip;
        [Range(0f, 1f)] public float secondaryAmbientVolume = 0.28f;

        [Tooltip("Порты, которые вешают трубку в конце этого узла (щелчок реле, стоп фона, лампа гаснет).")]
        public List<SubscriberId> remoteHangUpPorts = new List<SubscriberId>();

        [Tooltip("Если включено — основная линия звонка вешает трубку (пауза по выдёргиванию отключается).")]
        public bool hangUpPrimaryLine = false;

        [Tooltip("Остановить фон второй линии (например, когда Кобра вешает трубку, а Кузница ещё говорит).")]
        public bool stopSecondaryAmbient = false;

        [Header("Сборка сообщения «Грому» (только для WaitFireMissionAssembly)")]
        public string fireMissionSuccessNodeId = string.Empty;
        public string fireMissionFailureNodeId = string.Empty;
        public List<FireMissionVoiceVariant> fireMissionVoiceVariants = new List<FireMissionVoiceVariant>();

        [Header("Кульминация гибели «Берёзы» (Звонок 1.6)")]
        public BerezaDeathClimaxData berezaClimax = new BerezaDeathClimaxData();

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
        [Tooltip("Процедурный профиль фона линии, если callerAmbientLoop ещё не назначен.")]
        public SubscriberAmbientProfile proceduralAmbientProfile = SubscriberAmbientProfile.None;
        [Range(0f, 1f)] public float callerAmbientVolume = 0.28f;

        [Header("Расписание и условия запуска")]
        [Tooltip("Игровое время появления звонка (час 0..23 и минута 0..59).")]
        [Range(0, 23)] public int scheduledHour = 23;
        [Range(0, 59)] public int scheduledMinute = 40;

        [Tooltip("Если включено — звонок стартует по очереди после предыдущего (с задержкой), не дожидаясь точных часов.")]
        public bool startImmediatelyInSequence = true;
        [Min(0f)] public float delayBeforeRingingSeconds = 1.5f;

        [Tooltip("Пауза после завершения этого звонка перед следующим звонком в очереди (сек).")]
        [Min(0f)] public float postCallPauseSeconds = 3.5f;

        [Tooltip("Если включено — в середине паузы после этого звонка гремит один глухой далёкий разрыв (вздрагивает лампа).")]
        public bool distantExplosionMidPostCallPause = false;

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
