using System.Collections.Generic;
using UnityEngine;

namespace LastSignal.StoryCore
{
    /// <summary>
    /// Конфигурация одной игровой смены (Ночь 1, Ночь 2 или Ночь 3).
    /// Создаётся через ПКМ в Project -> Create -> Last Signal -> Night Schedule.
    /// </summary>
    [CreateAssetMenu(fileName = "Night_01_Schedule", menuName = "Last Signal/Night Schedule", order = 2)]
    public sealed class NightScheduleAsset : ScriptableObject
    {
        [Header("Паспорт ночи")]
        [Range(1, 3)] public int nightNumber = 1;
        public string nightTitle = "Ночь первая. Тихий эфир";
        public NightStage initialRoomStage = NightStage.Night1;

        [Header("Вступительный титр (машинопись на чёрном экране)")]
        [TextArea(2, 4)]
        public string openingTypewriterText =
            "Крутояр. Подвал почтового отделения. Запасной узел связи. Позывной — «Маяк». 23:40";
        [Min(0f)] public float openingCardDuration = 4.5f;

        [Header("Игровые часы и нагрузка смены")]
        [Range(0, 23)] public int startHour = 23;
        [Range(0, 59)] public int startMinute = 40;
        [Tooltip("Сколько игровых минут проходит за 1 реальную секунду.")]
        [Min(0.05f)] public float gameMinutesPerRealSecond = 0.45f;

        [Tooltip("Максимум одновременно мигающих входящих линий (Ночь 1 = 1, Ночь 2 = 2, Ночь 3 = 3).")]
        [Range(1, 3)] public int maxSimultaneousIncomingLines = 1;

        [Tooltip("Таймер терпения абонента в секундах (0 = без таймера; Ночь 2 = 30; Ночь 3 = 15).")]
        [Min(0f)] public float defaultPatienceSeconds = 0f;

        [Tooltip("Разрешено ли снимать наушники для концовки «Тишина» в эту ночь.")]
        public bool allowHeadphonesRemovalEnding = false;

        [Header("Пароль суток (для справки и таблицы в книге)")]
        public string passwordPrompt = "Ольха";
        public string passwordResponse = "Кремень";
        public bool hasReservePassword = false;
        public string reservePasswordPrompt = "Уголь";
        public string reservePasswordResponse = "Ладога";

        [Header("Очередь звонков этой ночи")]
        public List<CallScenarioAsset> calls = new List<CallScenarioAsset>();

        [Header("Завершающий титр после последнего звонка")]
        [TextArea(2, 4)]
        public string closingTypewriterText =
            "Центральный узел уничтожен прямым попаданием в 00:17. Это был предпоследний из шести узлов участка. С этой минуты вся связь участка идёт через «Маяк».";
        [Min(0f)] public float closingCardDuration = 6.0f;
    }
}
