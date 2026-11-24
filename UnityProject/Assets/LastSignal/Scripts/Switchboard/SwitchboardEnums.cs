namespace LastSignal.Switchboard
{
    /// <summary>
    /// Все 10 линий (гнёзд и ламп) на панели коммутатора узла «Маяк».
    /// Нумерация 1..10 соответствует объектам Switchboard_Jack_01..10 и Switchboard_Lamp_01..10.
    /// </summary>
    public enum SwitchboardChannelId
    {
        None = 0,
        Bereza = 1,        // 01 — Берёза (центральный узел, сержант Дёмин)
        Sokol2 = 2,        // 02 — Сокол-2 (штаб дивизии, капитан Вельский)
        Kobra = 3,         // 03 — Кобра (командир полка, полковник Ардов)
        Grom = 4,          // 04 — Гром (артбатарея, капитан Жаров)
        Yozh = 5,          // 05 — Ёж (1-й батальон, лейтенант Костров)
        Shilo = 6,         // 06 — Шило (разведгруппа, сержант Мальцев)
        Romashka = 7,      // 07 — Ромашка (медсанбат, военврач Лисина)
        Kuznitsa = 8,      // 08 — Кузница (склад и подвоз, старшина Бойко)
        Yakor = 9,         // 09 — Якорь (сапёры у моста, лейтенант Рудой)
        OpenFrequency = 10 // 10 — Общая частота (линия без подписи)
    }

    /// <summary>
    /// Визуальное и логическое состояние сигнальной лампочки канала.
    /// </summary>
    public enum LampState
    {
        Off = 0,           // Погашена
        Incoming = 1,      // Входящий вызов (ритмичное мигание)
        Urgent = 2,        // Абонент теряет терпение (частое тревожное мигание)
        Connected = 3,     // Линия подключена (ровное тёплое свечение)
        WaitingTarget = 4, // Мягкое пульсирующее ожидание (опциональная подсказка)
        Dead = 5           // Линия потеряна / лампа разбита (не реагирует на вызовы)
    }

    /// <summary>
    /// Состояние физического штекера коммутатора.
    /// </summary>
    public enum PlugState
    {
        Docked = 0,  // Лежит в своём гнезде-доке
        Held = 1,    // Взят игроком (приподнят / перетаскивается к гнезду)
        Plugged = 2  // Вставлен в линию коммутатора
    }

    /// <summary>
    /// Вспомогательные методы отображения позывных и описаний линий.
    /// </summary>
    public static class SwitchboardChannelUtility
    {
        public static string GetDisplayName(SwitchboardChannelId channelId)
        {
            return channelId switch
            {
                SwitchboardChannelId.Bereza => "01 · Берёза",
                SwitchboardChannelId.Sokol2 => "02 · Сокол-2",
                SwitchboardChannelId.Kobra => "03 · Кобра",
                SwitchboardChannelId.Grom => "04 · Гром",
                SwitchboardChannelId.Yozh => "05 · Ёж",
                SwitchboardChannelId.Shilo => "06 · Шило",
                SwitchboardChannelId.Romashka => "07 · Ромашка",
                SwitchboardChannelId.Kuznitsa => "08 · Кузница",
                SwitchboardChannelId.Yakor => "09 · Якорь",
                SwitchboardChannelId.OpenFrequency => "10 · Общая частота",
                _ => "—"
            };
        }

        public static string GetCallsignOnly(SwitchboardChannelId channelId)
        {
            return channelId switch
            {
                SwitchboardChannelId.Bereza => "Берёза",
                SwitchboardChannelId.Sokol2 => "Сокол-2",
                SwitchboardChannelId.Kobra => "Кобра",
                SwitchboardChannelId.Grom => "Гром",
                SwitchboardChannelId.Yozh => "Ёж",
                SwitchboardChannelId.Shilo => "Шило",
                SwitchboardChannelId.Romashka => "Ромашка",
                SwitchboardChannelId.Kuznitsa => "Кузница",
                SwitchboardChannelId.Yakor => "Якорь",
                SwitchboardChannelId.OpenFrequency => "Общая частота",
                _ => "Не выбран"
            };
        }

        public static string GetSubscriberNote(SwitchboardChannelId channelId)
        {
            return channelId switch
            {
                SwitchboardChannelId.Bereza => "центральный узел · сержант Дёмин",
                SwitchboardChannelId.Sokol2 => "штаб дивизии · капитан Вельский",
                SwitchboardChannelId.Kobra => "командир полка · полковник Ардов",
                SwitchboardChannelId.Grom => "артбатарея · капитан Жаров",
                SwitchboardChannelId.Yozh => "1-й батальон · лейтенант Костров",
                SwitchboardChannelId.Shilo => "разведгруппа · сержант Мальцев",
                SwitchboardChannelId.Romashka => "медсанбат · военврач Лисина",
                SwitchboardChannelId.Kuznitsa => "склад и подвоз · старшина Бойко",
                SwitchboardChannelId.Yakor => "сапёры у моста · лейтенант Рудой",
                SwitchboardChannelId.OpenFrequency => "открытая линия без подписи",
                _ => string.Empty
            };
        }

        public static int GetSlotNumber(SwitchboardChannelId channelId)
        {
            return (int)channelId;
        }
    }
}
