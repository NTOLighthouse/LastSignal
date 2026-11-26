using System;

namespace LastSignal.StoryCore
{
    /// <summary>
    /// Позывные и порты коммутатора. Числовые значения 1..10 строго соответствуют portId на панели.
    /// </summary>
    public enum SubscriberId
    {
        None = 0,
        Bereza = 1,
        Sokol2 = 2,
        Kobra = 3,
        Grom = 4,
        Yozh = 5,
        Shilo = 6,
        Romashka = 7,
        Kuznitsa = 8,
        Yakor = 9,
        OpenChannel = 10
    }

    /// <summary>
    /// Восемь сюжетных флагов «да / нет» из ГДД, определяющих реплики и концовки игры.
    /// </summary>
    public enum StoryFlag
    {
        None = 0,
        TopolSaved = 1,
        TanksDestroyed = 2,
        SorokaAlive = 3,
        RomashkaWarned = 4,
        ZimaReported = 5,
        AddressNotRevealed = 6,
        ShiloSignalUnderstood = 7,
        TrustedZima = 8
    }

    /// <summary>
    /// Шесть концовок игры из ГДД (пять основных и ранняя концовка «Адрес»).
    /// </summary>
    public enum EndingType
    {
        None = 0,
        MayakBurns = 1,
        OrdersAreOrders = 2,
        FireOnMyself = 3,
        Silence = 4,
        ForeignVoice = 5,
        Address = 6
    }

    /// <summary>
    /// Состояния единственной сцены подвала на протяжении пролога и трёх ночей.
    /// </summary>
    public enum NightStage
    {
        Prologue = 0,
        Night1 = 1,
        Night2 = 2,
        Night2AfterStrike = 3,
        Night3 = 4,
        Night3AfterShelling = 5
    }

    /// <summary>
    /// Интерактивное игровое действие, которого диалог может ждать прямо посреди звонка.
    /// </summary>
    public enum DialogueWaitAction
    {
        None = 0,
        WaitOpenBook = 1,
        WaitClickSubscriberInBook = 2,
        WaitConnectPort = 3,
        WaitDisconnectPort = 4,
        WaitTuneRadio = 5
    }

    /// <summary>
    /// Режиссёрский эффект в подвале, вызываемый в конкретном узле диалога.
    /// </summary>
    public enum RoomEventEffect
    {
        None = 0,
        DistantExplosion = 1,
        CloseExplosion = 2,
        BlackoutSilence10s = 3,
        SwitchToEmergencyRed = 4,
        CrossOutBereza = 5,
        OpenReservePasswords = 6,
        TriggerEnding = 7
    }

    /// <summary>
    /// Вспомогательные преобразования для позывных и строковых ключей книги.
    /// </summary>
    public static class SubscriberIdUtility
    {
        public static string ToBookKey(SubscriberId id)
        {
            return id switch
            {
                SubscriberId.Bereza => "bereza",
                SubscriberId.Sokol2 => "sokol2",
                SubscriberId.Kobra => "kobra",
                SubscriberId.Grom => "grom",
                SubscriberId.Yozh => "yozh",
                SubscriberId.Shilo => "shilo",
                SubscriberId.Romashka => "romashka",
                SubscriberId.Kuznitsa => "kuznitsa",
                SubscriberId.Yakor => "yakor",
                _ => string.Empty
            };
        }

        public static string ToDisplayName(SubscriberId id)
        {
            return id switch
            {
                SubscriberId.Bereza => "Берёза",
                SubscriberId.Sokol2 => "Сокол-2",
                SubscriberId.Kobra => "Кобра",
                SubscriberId.Grom => "Гром",
                SubscriberId.Yozh => "Ёж",
                SubscriberId.Shilo => "Шило",
                SubscriberId.Romashka => "Ромашка",
                SubscriberId.Kuznitsa => "Кузница",
                SubscriberId.Yakor => "Якорь",
                SubscriberId.OpenChannel => "Общая частота",
                _ => "—"
            };
        }

        public static SubscriberId FromPortId(int portId)
        {
            return Enum.IsDefined(typeof(SubscriberId), portId)
                ? (SubscriberId)portId
                : SubscriberId.None;
        }
    }
}
