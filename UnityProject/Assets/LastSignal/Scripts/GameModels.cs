using System;
using System.Collections.Generic;

namespace LastSignal
{
    // Эти отметки запоминают важные решения игрока и влияют на дальнейшие звонки.
    public enum StoryFlag
    {
        TopolSaved,
        TanksDestroyed,
        ShiloSignalUnderstood,
        SorokaSaved,
        RomashkaSaved,
        YozhSaved,
        BelievedWinter
    }

    // Действие игрока в финале определяет одну из пяти концовок.
    public enum FinalAction
    {
        None,
        Wait,
        DawnNow,
        FireOnSelf,
        FollowWinter,
        RemoveHeadphones
    }

    // Внутренние названия пяти возможных результатов прохождения.
    public enum EndingType
    {
        LighthouseBurns,
        OrdersAreOrders,
        FireOnSelf,
        Silence,
        ForeignVoice
    }

    // Один вариант ответа: надпись на кнопке, последствие и возможная отметка сюжета.
    public sealed class ChoiceDefinition
    {
        public string Label;
        public string Consequence;
        public StoryFlag[] SetFlags;
        public FinalAction FinalAction;
        // Если отметка указана, вариант появится только после нужного решения.
        public StoryFlag? RequiredFlag;

        public ChoiceDefinition(
            string label,
            string consequence,
            StoryFlag[] setFlags = null,
            FinalAction finalAction = FinalAction.None,
            StoryFlag? requiredFlag = null)
        {
            Label = label;
            Consequence = consequence;
            SetFlags = setFlags ?? Array.Empty<StoryFlag>();
            FinalAction = finalAction;
            RequiredFlag = requiredFlag;
        }
    }

    // Полное описание одного звонка, которое затем показывает игровой экран.
    public sealed class CallDefinition
    {
        public int Number;
        public int Night;
        public string Time;
        public string CallSign;
        public string Speaker;
        public string Transcript;
        public string Clue;
        public float PatienceSeconds;
        public ChoiceDefinition[] Choices;

        public CallDefinition(
            int number,
            int night,
            string time,
            string callSign,
            string speaker,
            string transcript,
            string clue,
            float patienceSeconds,
            params ChoiceDefinition[] choices)
        {
            Number = number;
            Night = night;
            Time = time;
            CallSign = callSign;
            Speaker = speaker;
            Transcript = transcript;
            Clue = clue;
            PatienceSeconds = patienceSeconds;
            Choices = choices;
        }
    }

    [Serializable]
    // Одна строка журнала: кто звонил, что выбрал игрок и к чему это привело.
    public sealed class JournalEntry
    {
        public int CallNumber;
        public string CallSign;
        public string Decision;
        public string Consequence;

        public JournalEntry(int callNumber, string callSign, string decision, string consequence)
        {
            CallNumber = callNumber;
            CallSign = callSign;
            Decision = decision;
            Consequence = consequence;
        }
    }

    [Serializable]
    // Всё, что нужно сохранить между запусками игры.
    public sealed class GameState
    {
        public int CurrentCallIndex;
        public List<StoryFlag> Flags = new List<StoryFlag>();
        public List<JournalEntry> Journal = new List<JournalEntry>();

        public bool Has(StoryFlag flag)
        {
            return Flags.Contains(flag);
        }

        public void Set(StoryFlag flag)
        {
            // Одну и ту же отметку достаточно сохранить один раз.
            if (!Flags.Contains(flag))
            {
                Flags.Add(flag);
            }
        }
    }

    // Текст, который игрок увидит после получения концовки.
    public sealed class EndingDefinition
    {
        public readonly string Title;
        public readonly string Summary;
        public readonly string Mood;

        public EndingDefinition(string title, string summary, string mood)
        {
            Title = title;
            Summary = summary;
            Mood = mood;
        }
    }
}
