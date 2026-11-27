using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using LastSignal.StoryCore;

namespace LastSignal.EditorTools
{
    /// <summary>
    /// Генерирует и обновляет все ScriptableObject-ассеты Ночи 1 («Тихий эфир»):
    /// 6 звонков (1.1–1.6) и расписание Night_01_Schedule.asset с дословными текстами сценария.
    /// </summary>
    public static class Night1ScenarioBuilder
    {
        private const string FolderPath = "Assets/StoryData/Night1";

        private static readonly Color DeminColor = new Color(0.96f, 0.78f, 0.44f, 1f);
        private static readonly Color LarinColor = new Color(0.84f, 0.90f, 0.95f, 1f);
        private static readonly Color BoikoColor = new Color(0.92f, 0.72f, 0.48f, 1f);
        private static readonly Color KobraColor = new Color(0.86f, 0.54f, 0.46f, 1f);
        private static readonly Color KostrovColor = new Color(0.76f, 0.90f, 0.64f, 1f);
        private static readonly Color ZharovColor = new Color(0.95f, 0.64f, 0.36f, 1f);
        private static readonly Color MalcevColor = new Color(0.68f, 0.84f, 0.78f, 1f);
        private static readonly Color LisinaColor = new Color(0.94f, 0.80f, 0.82f, 1f);
        private static readonly Color RudoiColor = new Color(0.76f, 0.82f, 0.90f, 1f);

        [MenuItem("Last Signal/Rebuild Night 1 Scenario Assets")]
        public static void BuildAllNight1Assets()
        {
            if (!AssetDatabase.IsValidFolder("Assets/StoryData"))
            {
                AssetDatabase.CreateFolder("Assets", "StoryData");
            }
            if (!AssetDatabase.IsValidFolder(FolderPath))
            {
                AssetDatabase.CreateFolder("Assets/StoryData", "Night1");
            }

            CallScenarioAsset call11 = BuildCall11();
            CallScenarioAsset call12 = BuildCall12();
            CallScenarioAsset call13 = BuildCall13();
            CallScenarioAsset call14 = BuildCall14();
            CallScenarioAsset call15 = BuildCall15();
            CallScenarioAsset call16 = BuildCall16();

            NightScheduleAsset schedule = LoadOrCreate<NightScheduleAsset>($"{FolderPath}/Night_01_Schedule.asset");
            schedule.nightNumber = 1;
            schedule.nightTitle = "Ночь первая. Тихий эфир";
            schedule.initialRoomStage = NightStage.Night1;
            schedule.openingTypewriterText =
                "Крутояр. Подвал почтового отделения. Запасной узел связи. Позывной — „Маяк“. 23:40";
            schedule.openingCardDuration = 3.5f;
            schedule.startHour = 23;
            schedule.startMinute = 40;
            schedule.gameMinutesPerRealSecond = 0.45f;
            schedule.maxSimultaneousIncomingLines = 1;
            schedule.defaultPatienceSeconds = 0f; // Ночь 1 без таймера терпения
            schedule.allowHeadphonesRemovalEnding = false;
            schedule.passwordPrompt = "Ольха";
            schedule.passwordResponse = "Кремень";
            schedule.hasReservePassword = false;
            schedule.calls = new List<CallScenarioAsset> { call11, call12, call13, call14, call15, call16 };
            schedule.closingTypewriterText =
                "Центральный узел уничтожен прямым попаданием в 00:17. Это был предпоследний из шести узлов участка. С этой минуты вся связь участка идёт через „Маяк“.";
            schedule.closingSubtitleText = "Ночь вторая. Чужие голоса";
            schedule.closingSubtitleDelay = 3.0f;
            schedule.closingCardDuration = 6.0f;

            EditorUtility.SetDirty(schedule);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Night1ScenarioBuilder] Все 6 звонков Ночи 1 и Night_01_Schedule успешно собраны.");
        }

        private static CallScenarioAsset BuildCall11()
        {
            var c = LoadOrCreate<CallScenarioAsset>($"{FolderPath}/Call_1_1_Bereza_Intro.asset");
            c.callId = "1.1";
            c.callTitle = "1.1. «Берёза» вызывает «Маяк». Книга и пароль";
            c.nightNumber = 1;
            c.subscriberPort = SubscriberId.Bereza;
            c.isPlayerInitiatedOutgoing = false;
            c.proceduralAmbientProfile = SubscriberAmbientProfile.CentralHub;
            c.callerAmbientVolume = 0.24f;
            c.scheduledHour = 23;
            c.scheduledMinute = 40;
            c.startImmediatelyInSequence = true;
            c.delayBeforeRingingSeconds = 1.5f;
            c.postCallPauseSeconds = 3.5f;
            c.distantExplosionMidPostCallPause = true;
            c.patienceOverrideSeconds = -1f;
            c.pauseAndResumeOnDisconnect = true;
            c.entryNodeId = "n01_demin_hail";

            c.nodes = new List<DialogueNodeData>
            {
                new DialogueNodeData
                {
                    nodeId = "n01_demin_hail",
                    speakerName = "ДЁМИН («Берёза»)",
                    speakerColor = DeminColor,
                    subtitleText = "Маяк, Маяк, я Берёза. Как слышно? Приём.",
                    choices = new List<DialogueChoiceData>
                    {
                        new DialogueChoiceData { choiceText = "Маяк слушает.", nextNodeId = "n02_larin_listen" }
                    }
                },
                new DialogueNodeData
                {
                    nodeId = "n02_larin_listen",
                    speakerName = "ЛАРИН («Маяк»)",
                    speakerColor = LarinColor,
                    subtitleText = "Маяк слушает.",
                    autoNextNodeId = "n03_demin_intro"
                },
                new DialogueNodeData
                {
                    nodeId = "n03_demin_intro",
                    speakerName = "ДЁМИН («Берёза»)",
                    speakerColor = DeminColor,
                    subtitleText = "О, живой голос. Сержант Дёмин, центральный узел. А ты, стало быть, ефрейтор Ларин. Матвей, так? Запомни, Матвей: в эфире ты — Маяк, и больше никто. Ты у нас запасной, так что ночь будет скучная: всё идёт через меня, тебе — крошки. Но крошки тоже надо уметь подбирать.",
                    autoNextNodeId = "n04_demin_book"
                },
                new DialogueNodeData
                {
                    nodeId = "n04_demin_book",
                    speakerName = "ДЁМИН («Берёза»)",
                    speakerColor = DeminColor,
                    subtitleText = "И учти: нас на весь участок двое и осталось — я да ты. Остальные узлы заречные выбили за неделю: поймают голос в эфире, вычислят, где сидит передатчик, — и через час туда прилетает. Тебя у них в списках нет, твой подвал три недели молчал. Вот и не болтай лишнего. Если со мной что случится, спросить тебе будет не у кого, так что слушай и запоминай. Книгу видишь? Серая, справа. Открой.",
                    waitAction = DialogueWaitAction.WaitOpenBook,
                    actionHintText = "Откройте серую книгу позывных справа на столе (ЛКМ по книге)",
                    autoNextNodeId = "n05_demin_romashka"
                },
                new DialogueNodeData
                {
                    nodeId = "n05_demin_romashka",
                    speakerName = "ДЁМИН («Берёза»)",
                    speakerColor = DeminColor,
                    subtitleText = "Там все, кто имеет право тебя беспокоить. Позывной, кто такой, где сидит. Найди мне, кто такая Ромашка.",
                    waitAction = DialogueWaitAction.WaitClickSubscriberInBook,
                    expectedSubscriber = SubscriberId.Romashka,
                    actionHintText = "Найдите и нажмите строку «Ромашка» на 1-м развороте книги (ЛКМ)",
                    autoNextNodeId = "n06_demin_rules"
                },
                new DialogueNodeData
                {
                    nodeId = "n06_demin_rules",
                    speakerName = "ДЁМИН («Берёза»)",
                    speakerColor = DeminColor,
                    subtitleText = "Верно, медицина. Первое правило: нет в книге — нет в эфире. Второе: сомневаешься — спроси пароль. Сегодня пароль «Ольха». Ты говоришь «Ольха», тебе отвечают… что? Глянь в таблицу. А теперь спроси меня.",
                    choices = new List<DialogueChoiceData>
                    {
                        new DialogueChoiceData { choiceText = "Пароль. Ольха.", nextNodeId = "n07_larin_password" }
                    }
                },
                new DialogueNodeData
                {
                    nodeId = "n07_larin_password",
                    speakerName = "ЛАРИН («Маяк»)",
                    speakerColor = LarinColor,
                    subtitleText = "Пароль. Ольха.",
                    autoNextNodeId = "n08_demin_end"
                },
                new DialogueNodeData
                {
                    nodeId = "n08_demin_end",
                    speakerName = "ДЁМИН («Берёза»)",
                    speakerColor = DeminColor,
                    subtitleText = "Кремень. Вот так. Не ответили — клади трубку, хоть там сам генерал. И у тебя могут спросить — тогда отвечаешь ты. Третье правило скажу потом, оно невесёлое. Сейчас к тебе пойдут люди. Работай. Конец связи.",
                    logbookNoteToAdd = "Нет в книге — нет в эфире",
                    hangUpPrimaryLine = true,
                    waitAction = DialogueWaitAction.WaitAllPlugsDisconnected,
                    actionHintText = "Вытащите шнур из коммутатора (ПКМ — вернуть на базу)",
                    autoNextNodeId = string.Empty
                }
            };

            EditorUtility.SetDirty(c);
            return c;
        }

        private static CallScenarioAsset BuildCall12()
        {
            var c = LoadOrCreate<CallScenarioAsset>($"{FolderPath}/Call_1_2_Kuznitsa_Kobra.asset");
            c.callId = "1.2";
            c.callTitle = "1.2. «Кузница» просит «Кобру». Простое соединение";
            c.nightNumber = 1;
            c.subscriberPort = SubscriberId.Kuznitsa;
            c.isPlayerInitiatedOutgoing = false;
            c.proceduralAmbientProfile = SubscriberAmbientProfile.TruckIdleDepot;
            c.callerAmbientVolume = 0.24f;
            c.scheduledHour = 23;
            c.scheduledMinute = 46;
            c.startImmediatelyInSequence = true;
            c.delayBeforeRingingSeconds = 3.5f;
            c.postCallPauseSeconds = 3.5f;
            c.patienceOverrideSeconds = -1f;
            c.pauseAndResumeOnDisconnect = true;
            c.entryNodeId = "n01_boiko_ask";

            c.nodes = new List<DialogueNodeData>
            {
                new DialogueNodeData
                {
                    nodeId = "n01_boiko_ask",
                    speakerName = "БОЙКО («Кузница»)",
                    speakerColor = BoikoColor,
                    subtitleText = "Маяк, Кузница беспокоит. Старшина Бойко. Мне бы Кобру, сынок. У меня по накладной три ящика тушёнки, а по факту два, и я желаю знать, кто из нас сошёл с ума — я или бумага.",
                    waitAction = DialogueWaitAction.WaitConnectPort,
                    expectedSubscriber = SubscriberId.Kobra,
                    actionHintText = "Соедините «Кузницу» с «Коброй» (второй шнур в гнездо «Кобра»)",
                    wrongPortHintText = "Нужно соединить с „Коброй“",
                    secondaryAmbientProfile = SubscriberAmbientProfile.StaffClockMap,
                    secondaryAmbientVolume = 0.22f,
                    autoNextNodeId = "n02_kobra_ans"
                },
                new DialogueNodeData
                {
                    nodeId = "n02_kobra_ans",
                    speakerName = "КОБРА",
                    speakerColor = KobraColor,
                    subtitleText = "Кобра.",
                    autoNextNodeId = "n03_boiko_report"
                },
                new DialogueNodeData
                {
                    nodeId = "n03_boiko_report",
                    speakerName = "БОЙКО («Кузница»)",
                    speakerColor = BoikoColor,
                    subtitleText = "Товарищ полк… виноват. Кобра, я Кузница. Докладываю о недостаче…",
                    autoNextNodeId = "n04_kobra_cut"
                },
                new DialogueNodeData
                {
                    nodeId = "n04_kobra_cut",
                    speakerName = "КОБРА",
                    speakerColor = KobraColor,
                    subtitleText = "Кузница. Третий ящик съел мой штаб. Спиши. Ещё раз займёшь линию тушёнкой — сам пойдёшь в ящик. Конец связи. Работайте.",
                    stopSecondaryAmbient = true,
                    remoteHangUpPorts = new List<SubscriberId> { SubscriberId.Kobra },
                    autoNextNodeId = "n05_boiko_mutter"
                },
                new DialogueNodeData
                {
                    nodeId = "n05_boiko_mutter",
                    speakerName = "БОЙКО («Кузница»)",
                    speakerColor = BoikoColor,
                    isItalic = true,
                    subtitleText = "Слыхал? «Работайте». Он это каждый раз говорит. Хоть бы раз сказал «спасибо». Ладно, Маяк, бывай.",
                    hangUpPrimaryLine = true,
                    waitAction = DialogueWaitAction.WaitAllPlugsDisconnected,
                    actionHintText = "Вытащите шнуры из коммутатора (ПКМ — вернуть на базу)",
                    autoNextNodeId = string.Empty
                }
            };

            EditorUtility.SetDirty(c);
            return c;
        }

        private static CallScenarioAsset BuildCall13()
        {
            var c = LoadOrCreate<CallScenarioAsset>($"{FolderPath}/Call_1_3_Yozh_Grom.asset");
            c.callId = "1.3";
            c.callTitle = "1.3. «Ёж» вызывает огонь. Передача координат";
            c.nightNumber = 1;
            c.subscriberPort = SubscriberId.Yozh;
            c.isPlayerInitiatedOutgoing = false;
            c.proceduralAmbientProfile = SubscriberAmbientProfile.WindDistantShots;
            c.callerAmbientVolume = 0.24f;
            c.scheduledHour = 23;
            c.scheduledMinute = 52;
            c.startImmediatelyInSequence = true;
            c.delayBeforeRingingSeconds = 3.5f;
            c.postCallPauseSeconds = 3.5f;
            c.patienceOverrideSeconds = -1f;
            c.pauseAndResumeOnDisconnect = true;
            c.entryNodeId = "n01_kostrov_call";

            List<FireMissionVoiceVariant> CreateVoiceSlots() => new List<FireMissionVoiceVariant>
            {
                new FireMissionVoiceVariant { squareCode = "16-Б", oneShell = true },
                new FireMissionVoiceVariant { squareCode = "16-Б", oneShell = false },
                new FireMissionVoiceVariant { squareCode = "16-В", oneShell = true },
                new FireMissionVoiceVariant { squareCode = "16-В", oneShell = false },
                new FireMissionVoiceVariant { squareCode = "15-Б", oneShell = true },
                new FireMissionVoiceVariant { squareCode = "15-Б", oneShell = false }
            };

            c.nodes = new List<DialogueNodeData>
            {
                new DialogueNodeData
                {
                    nodeId = "n01_kostrov_call",
                    speakerName = "КОСТРОВ («Ёж»)",
                    speakerColor = KostrovColor,
                    subtitleText = "Маяк, Маяк, Ёж на связи! Лейтенант Костров, высота двести четырнадцать. Гром мне не отвечает, у них, наверное, опять уши заложило. Передай им: пристрелочный, квадрат [шестнадцать-Б|sq_16b], [один снаряд|ammo_1]. Как понял?",
                    waitAction = DialogueWaitAction.WaitRecordKeywords,
                    requiredKeywordIds = new List<string> { "sq_16b", "ammo_1" },
                    actionHintText = "Нажмите на оба подсвеченных слова в субтитрах, чтобы записать их в журнал",
                    postLineDelay = 0.1f,
                    autoNextNodeId = "n02_wait_grom"
                },
                new DialogueNodeData
                {
                    nodeId = "n02_wait_grom",
                    speakerName = string.Empty,
                    subtitleText = string.Empty,
                    waitAction = DialogueWaitAction.WaitConnectPort,
                    expectedSubscriber = SubscriberId.Grom,
                    actionHintText = "Вызовите «Гром» (второй шнур в гнездо «Гром»)",
                    wrongPortHintText = "Нужно вызвать «Гром»",
                    secondaryAmbientProfile = SubscriberAmbientProfile.ArtilleryBattery,
                    secondaryAmbientVolume = 0.25f,
                    postLineDelay = 0.1f,
                    autoNextNodeId = "n03_zharov_hello"
                },
                new DialogueNodeData
                {
                    nodeId = "n03_zharov_hello",
                    speakerName = "ЖАРОВ («Гром»)",
                    speakerColor = ZharovColor,
                    subtitleText = "ГРОМ СЛУШАЕТ! Говори громче, я контуженный!",
                    waitAction = DialogueWaitAction.WaitFireMissionAssembly,
                    fireMissionSuccessNodeId = "n04_kostrov_hit",
                    fireMissionFailureNodeId = "n05_kostrov_miss",
                    fireMissionVoiceVariants = CreateVoiceSlots()
                },
                new DialogueNodeData
                {
                    nodeId = "n04_kostrov_hit",
                    speakerName = "КОСТРОВ («Ёж»)",
                    speakerColor = KostrovColor,
                    subtitleText = "Есть! Видел! Красиво легло. Спасибо, Маяк. Слушай, а ты новенький? Я тоже. Третья неделя. Ну… держись там. Конец связи.",
                    stopSecondaryAmbient = true,
                    remoteHangUpPorts = new List<SubscriberId> { SubscriberId.Grom },
                    hangUpPrimaryLine = true,
                    waitAction = DialogueWaitAction.WaitAllPlugsDisconnected,
                    actionHintText = "Вытащите шнуры из коммутатора (ПКМ — вернуть на базу)",
                    autoNextNodeId = string.Empty
                },
                new DialogueNodeData
                {
                    nodeId = "n05_kostrov_miss",
                    speakerName = "КОСТРОВ («Ёж»)",
                    speakerColor = KostrovColor,
                    subtitleText = "Маяк, это что было?! Это мне за спину прилетело! Шестнадцать-БЭ! Борис! Один снаряд!",
                    autoNextNodeId = "n06_zharov_repeat"
                },
                new DialogueNodeData
                {
                    nodeId = "n06_zharov_repeat",
                    speakerName = "ЖАРОВ («Гром»)",
                    speakerColor = ZharovColor,
                    subtitleText = "Не понял?! Повтори точнее, Маяк!",
                    waitAction = DialogueWaitAction.WaitFireMissionAssembly,
                    fireMissionSuccessNodeId = "n04_kostrov_hit",
                    fireMissionFailureNodeId = "n05_kostrov_miss",
                    fireMissionVoiceVariants = CreateVoiceSlots()
                }
            };

            EditorUtility.SetDirty(c);
            return c;
        }

        private static CallScenarioAsset BuildCall14()
        {
            var c = LoadOrCreate<CallScenarioAsset>($"{FolderPath}/Call_1_4_Shilo_Kobra.asset");
            c.callId = "1.4";
            c.callTitle = "1.4. «Шило». Доклад и условный сигнал";
            c.nightNumber = 1;
            c.subscriberPort = SubscriberId.Shilo;
            c.isPlayerInitiatedOutgoing = false;
            c.proceduralAmbientProfile = SubscriberAmbientProfile.NightForestCrickets;
            c.callerAmbientVolume = 0.24f;
            c.scheduledHour = 23;
            c.scheduledMinute = 58;
            c.startImmediatelyInSequence = true;
            c.delayBeforeRingingSeconds = 3.5f;
            c.postCallPauseSeconds = 3.5f;
            c.patienceOverrideSeconds = -1f;
            c.pauseAndResumeOnDisconnect = true;
            c.entryNodeId = "n01_malcev_call";

            c.nodes = new List<DialogueNodeData>
            {
                new DialogueNodeData
                {
                    nodeId = "n01_malcev_call",
                    speakerName = "МАЛЬЦЕВ («Шило»)",
                    speakerColor = MalcevColor,
                    isItalic = true,
                    subtitleText = "Маяк. Шило. Коротко. Дай Кобру.",
                    waitAction = DialogueWaitAction.WaitConnectPort,
                    expectedSubscriber = SubscriberId.Kobra,
                    actionHintText = "Соедините «Шило» с «Коброй» (второй шнур в гнездо «Кобра»)",
                    wrongPortHintText = "Нужно соединить с „Коброй“",
                    secondaryAmbientProfile = SubscriberAmbientProfile.StaffClockMap,
                    secondaryAmbientVolume = 0.22f,
                    autoNextNodeId = "n02_kobra_ans"
                },
                new DialogueNodeData
                {
                    nodeId = "n02_kobra_ans",
                    speakerName = "КОБРА",
                    speakerColor = KobraColor,
                    subtitleText = "Кобра.",
                    autoNextNodeId = "n03_malcev_report"
                },
                new DialogueNodeData
                {
                    nodeId = "n03_malcev_report",
                    speakerName = "МАЛЬЦЕВ («Шило»)",
                    speakerColor = MalcevColor,
                    isItalic = true,
                    subtitleText = "Шило. В Сосновке заречные. Пехота, до роты. Техники пока нет. Местные не ушли — не успели.",
                    autoNextNodeId = "n04_kobra_order"
                },
                new DialogueNodeData
                {
                    nodeId = "n04_kobra_order",
                    speakerName = "КОБРА",
                    speakerColor = KobraColor,
                    subtitleText = "Понял. Иди глубже, ищи технику. Конец связи. Работайте.",
                    stopSecondaryAmbient = true,
                    remoteHangUpPorts = new List<SubscriberId> { SubscriberId.Kobra },
                    autoNextNodeId = "n05_malcev_ask"
                },
                new DialogueNodeData
                {
                    nodeId = "n05_malcev_ask",
                    speakerName = "МАЛЬЦЕВ («Шило»)",
                    speakerColor = MalcevColor,
                    isItalic = true,
                    subtitleText = "Новый голос. Ты откуда, земляк?",
                    choices = new List<DialogueChoiceData>
                    {
                        new DialogueChoiceData { choiceText = "С севера.", nextNodeId = "n06a_larin_north" },
                        new DialogueChoiceData { choiceText = "Не положено.", nextNodeId = "n06b_larin_refuse" }
                    }
                },
                new DialogueNodeData
                {
                    nodeId = "n06a_larin_north",
                    speakerName = "ЛАРИН («Маяк»)",
                    speakerColor = LarinColor,
                    subtitleText = "С севера.",
                    autoNextNodeId = "n07a_malcev_scold"
                },
                new DialogueNodeData
                {
                    nodeId = "n07a_malcev_scold",
                    speakerName = "МАЛЬЦЕВ («Шило»)",
                    speakerColor = MalcevColor,
                    isItalic = true,
                    subtitleText = "Зря сказал. В эфире про себя — ничего.",
                    autoNextNodeId = "n08_malcev_signal"
                },
                new DialogueNodeData
                {
                    nodeId = "n06b_larin_refuse",
                    speakerName = "ЛАРИН («Маяк»)",
                    speakerColor = LarinColor,
                    subtitleText = "Не положено.",
                    autoNextNodeId = "n07b_malcev_praise"
                },
                new DialogueNodeData
                {
                    nodeId = "n07b_malcev_praise",
                    speakerName = "МАЛЬЦЕВ («Шило»)",
                    speakerColor = MalcevColor,
                    isItalic = true,
                    subtitleText = "Правильно. Никому не говори. Даже мне. Особенно мне.",
                    autoNextNodeId = "n08_malcev_signal"
                },
                new DialogueNodeData
                {
                    nodeId = "n08_malcev_signal",
                    speakerName = "МАЛЬЦЕВ («Шило»)",
                    speakerColor = MalcevColor,
                    isItalic = true,
                    subtitleText = "Теперь слушай внимательно, повторять не буду. Я иду далеко. Если меня возьмут — посадят к рации и заставят говорить. Голос будет мой, слова — ихние. Так вот. Если я в эфире скажу «погода лётная» — значит, у меня ствол у затылка. Что бы я после этого ни сказал — всё враньё. Запомнил?",
                    choices = new List<DialogueChoiceData>
                    {
                        new DialogueChoiceData { choiceText = "Погода лётная. Запомнил.", nextNodeId = "n09_larin_confirm" }
                    }
                },
                new DialogueNodeData
                {
                    nodeId = "n09_larin_confirm",
                    speakerName = "ЛАРИН («Маяк»)",
                    speakerColor = LarinColor,
                    subtitleText = "Погода лётная. Запомнил.",
                    autoNextNodeId = "n10_malcev_end"
                },
                new DialogueNodeData
                {
                    nodeId = "n10_malcev_end",
                    speakerName = "МАЛЬЦЕВ («Шило»)",
                    speakerColor = MalcevColor,
                    isItalic = true,
                    subtitleText = "В журнал не пиши. В голову пиши. Конец связи.",
                    hangUpPrimaryLine = true,
                    waitAction = DialogueWaitAction.WaitAllPlugsDisconnected,
                    actionHintText = "Вытащите шнуры из коммутатора (ПКМ — вернуть на базу)",
                    autoNextNodeId = string.Empty
                }
            };

            EditorUtility.SetDirty(c);
            return c;
        }

        private static CallScenarioAsset BuildCall15()
        {
            var c = LoadOrCreate<CallScenarioAsset>($"{FolderPath}/Call_1_5_Romashka_Yakor.asset");
            c.callId = "1.5";
            c.callTitle = "1.5. «Ромашка» и «Якорь». Слово для моста";
            c.nightNumber = 1;
            c.subscriberPort = SubscriberId.Romashka;
            c.isPlayerInitiatedOutgoing = false;
            c.proceduralAmbientProfile = SubscriberAmbientProfile.MedSanBat;
            c.callerAmbientVolume = 0.24f;
            c.scheduledHour = 0;
            c.scheduledMinute = 5;
            c.startImmediatelyInSequence = true;
            c.delayBeforeRingingSeconds = 3.5f;
            c.postCallPauseSeconds = 3.5f;
            c.patienceOverrideSeconds = -1f;
            c.pauseAndResumeOnDisconnect = true;
            c.entryNodeId = "n01_lisina_call";

            c.nodes = new List<DialogueNodeData>
            {
                new DialogueNodeData
                {
                    nodeId = "n01_lisina_call",
                    speakerName = "ЛИСИНА («Ромашка»)",
                    speakerColor = LisinaColor,
                    subtitleText = "Маяк, это Ромашка. Военврач Лисина. Соедините меня, пожалуйста, с Якорем.",
                    waitAction = DialogueWaitAction.WaitConnectPort,
                    expectedSubscriber = SubscriberId.Yakor,
                    actionHintText = "Соедините «Ромашку» с «Якорем» (второй шнур в гнездо «Якорь»)",
                    wrongPortHintText = "Нужно соединить с „Якорем“",
                    secondaryAmbientProfile = SubscriberAmbientProfile.BridgeWaterMetal,
                    secondaryAmbientVolume = 0.25f,
                    autoNextNodeId = "n02_rudoi_ans"
                },
                new DialogueNodeData
                {
                    nodeId = "n02_rudoi_ans",
                    speakerName = "РУДОЙ («Якорь»)",
                    speakerColor = RudoiColor,
                    subtitleText = "Якорь — Маяку. Слушаю.",
                    autoNextNodeId = "n03_lisina_bridge"
                },
                new DialogueNodeData
                {
                    nodeId = "n03_lisina_bridge",
                    speakerName = "ЛИСИНА («Ромашка»)",
                    speakerColor = LisinaColor,
                    subtitleText = "Якорь, это Ромашка. Мне каждую ночь возить раненых с того берега через ваш мост. Шофёры говорят, вы под него что-то вешаете.",
                    autoNextNodeId = "n04_rudoi_explosives"
                },
                new DialogueNodeData
                {
                    nodeId = "n04_rudoi_explosives",
                    speakerName = "РУДОЙ («Якорь»)",
                    speakerColor = RudoiColor,
                    subtitleText = "Взрывчатку. Четыреста килограммов.",
                    autoNextNodeId = "n05_lisina_ride"
                },
                new DialogueNodeData
                {
                    nodeId = "n05_lisina_ride",
                    speakerName = "ЛИСИНА («Ромашка»)",
                    speakerColor = LisinaColor,
                    subtitleText = "…А нам по нему ездить.",
                    autoNextNodeId = "n06_rudoi_command"
                },
                new DialogueNodeData
                {
                    nodeId = "n06_rudoi_command",
                    speakerName = "РУДОЙ («Якорь»)",
                    speakerColor = RudoiColor,
                    subtitleText = "Ездите. Без команды не взорвётся. Команда придёт по связи. От Маяка.",
                    autoNextNodeId = "n07_lisina_boy"
                },
                new DialogueNodeData
                {
                    nodeId = "n07_lisina_boy",
                    speakerName = "ЛИСИНА («Ромашка»)",
                    speakerColor = LisinaColor,
                    subtitleText = "От этого мальчика?",
                    autoNextNodeId = "n08_rudoi_phrase"
                },
                new DialogueNodeData
                {
                    nodeId = "n08_rudoi_phrase",
                    speakerName = "РУДОЙ («Якорь»)",
                    speakerColor = RudoiColor,
                    subtitleText = "Другой связи может не остаться. Маяк, вы слышали. Команду на подрыв я приму только от вас, только с паролем суток и только словами «Якорю — рассвет». Запишите.",
                    autoNextNodeId = "n09_lisina_plea"
                },
                new DialogueNodeData
                {
                    nodeId = "n09_lisina_plea",
                    speakerName = "ЛИСИНА («Ромашка»)",
                    speakerColor = LisinaColor,
                    subtitleText = "Маяк, голубчик… пожалуйста, не торопитесь с этим словом, когда мои машины на том берегу. Спасибо.",
                    logbookNoteToAdd = "Мост: „Якорю — рассвет“ + пароль суток",
                    stopSecondaryAmbient = true,
                    remoteHangUpPorts = new List<SubscriberId> { SubscriberId.Yakor },
                    hangUpPrimaryLine = true,
                    waitAction = DialogueWaitAction.WaitAllPlugsDisconnected,
                    actionHintText = "Вытащите шнуры из коммутатора (ПКМ — вернуть на базу)",
                    autoNextNodeId = string.Empty
                }
            };

            EditorUtility.SetDirty(c);
            return c;
        }

        private static CallScenarioAsset BuildCall16()
        {
            var c = LoadOrCreate<CallScenarioAsset>($"{FolderPath}/Call_1_6_Bereza_Death.asset");
            c.callId = "1.6";
            c.callTitle = "1.6. «Берёза». Третье правило и гибель центрального узла";
            c.nightNumber = 1;
            c.subscriberPort = SubscriberId.Bereza;
            c.isPlayerInitiatedOutgoing = false;
            c.proceduralAmbientProfile = SubscriberAmbientProfile.CentralHub;
            c.callerAmbientVolume = 0.24f;
            c.scheduledHour = 0;
            c.scheduledMinute = 14;
            c.startImmediatelyInSequence = true;
            c.delayBeforeRingingSeconds = 3.5f;
            c.postCallPauseSeconds = 0f;
            c.patienceOverrideSeconds = -1f;
            c.pauseAndResumeOnDisconnect = true;
            c.entryNodeId = "n01_demin_rule3";

            c.nodes = new List<DialogueNodeData>
            {
                new DialogueNodeData
                {
                    nodeId = "n01_demin_rule3",
                    speakerName = "ДЁМИН («Берёза»)",
                    speakerColor = DeminColor,
                    subtitleText = "Маяк, я Берёза. Ну что, не уснул? Слушай третье правило. Особый отдел, контрразведка, всякие «я от самого главного» — в эфир не выходят. Никогда. Они приходят ногами и с бумагой. Если кто-то в наушниках говорит тебе «это секретно, никому не докладывай» — это не наш. Запиши на полях.",
                    logbookNoteToAdd = "Особисты в эфир не ходят. Приходят ногами и с бумагой",
                    autoNextNodeId = "n02_demin_rule4"
                },
                new DialogueNodeData
                {
                    nodeId = "n02_demin_rule4",
                    speakerName = "ДЁМИН («Берёза»)",
                    speakerColor = DeminColor,
                    subtitleText = "И четвёртое, последнее. Где сидишь — в эфир ни слова: ни квадрат, ни дом, ни улицу. Свои твой адрес знают, а чужим он нужен только для одного. Запомнил?",
                    choices = new List<DialogueChoiceData>
                    {
                        new DialogueChoiceData { choiceText = "Запомнил.", nextNodeId = "n03_larin_remember" }
                    }
                },
                new DialogueNodeData
                {
                    nodeId = "n03_larin_remember",
                    speakerName = "ЛАРИН («Маяк»)",
                    speakerColor = LarinColor,
                    subtitleText = "Запомнил.",
                    logbookNoteToAdd = "Свой адрес — в эфир ни слова. Свои его знают, чужим он нужен для одного",
                    autoNextNodeId = "n04_demin_voices"
                },
                new DialogueNodeData
                {
                    nodeId = "n04_demin_voices",
                    speakerName = "ДЁМИН («Берёза»)",
                    speakerColor = DeminColor,
                    subtitleText = "И ещё. Голоса запоминай. Книгу можно украсть, пароль можно выбить. А голос… Кобра всегда говорит «Работайте». Гром всегда повторяет координаты. Ромашка всегда говорит «пожалуйста». Если Ромашка перестала быть вежливой — это не Ромашка. Понял?",
                    choices = new List<DialogueChoiceData>
                    {
                        new DialogueChoiceData { choiceText = "Понял.", nextNodeId = "n05_larin_understood" }
                    }
                },
                new DialogueNodeData
                {
                    nodeId = "n05_larin_understood",
                    speakerName = "ЛАРИН («Маяк»)",
                    speakerColor = LarinColor,
                    subtitleText = "Понял.",
                    logbookNoteToAdd = "Кобра — „Работайте“. Гром повторяет координаты. Ромашка — „пожалуйста“",
                    autoNextNodeId = "n06_demin_tea"
                },
                new DialogueNodeData
                {
                    nodeId = "n06_demin_tea",
                    speakerName = "ДЁМИН («Берёза»)",
                    speakerColor = DeminColor,
                    subtitleText = "Ну и добро. Завтра сам к тебе зайду, Матвей, чаю принесу, у тебя там, говорят, один кипяток…",
                    postLineDelay = 0.1f,
                    autoNextNodeId = "bereza_climax"
                },
                new DialogueNodeData
                {
                    nodeId = "bereza_climax",
                    speakerName = string.Empty,
                    subtitleText = string.Empty,
                    berezaClimax = new BerezaDeathClimaxData
                    {
                        enabled = true,
                        whistleDuration = 1.1f,
                        whiteNoiseDuration = 1.7f,
                        absoluteSilenceDuration = 10.0f,
                        allLampsBlinkDuration = 3.0f
                    },
                    autoNextNodeId = string.Empty
                }
            };

            EditorUtility.SetDirty(c);
            return c;
        }

        private static T LoadOrCreate<T>(string assetPath) where T : ScriptableObject
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (existing != null)
            {
                return existing;
            }

            T created = ScriptableObject.CreateInstance<T>();
            string dir = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            AssetDatabase.CreateAsset(created, assetPath);
            return created;
        }
    }
}
