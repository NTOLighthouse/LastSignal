using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LastSignal.Editor
{
    public static class LastSignalValidator
    {
        [MenuItem("Last Signal/Validate Game")]
        public static void ValidateProject()
        {
            var calls = CallDatabase.Calls;
            Require(calls.Count == 24, $"Expected 24 calls, found {calls.Count}.");
            Require(calls.Select(call => call.Number).SequenceEqual(Enumerable.Range(1, 24)),
                "Call numbers must be sequential from 1 through 24.");

            // По сценарию звонки распределены так: 6 в первую ночь, 8 во вторую и 10 в третью.
            int[] expectedCallsByNight = { 6, 8, 10 };
            for (int night = 1; night <= 3; night++)
            {
                Require(calls.Count(call => call.Night == night) == expectedCallsByNight[night - 1],
                    $"Night {night} must contain exactly {expectedCallsByNight[night - 1]} calls.");
            }

            Require(calls.All(call => !string.IsNullOrWhiteSpace(call.CallSign)),
                "Every call needs a call sign.");
            Require(calls.All(call => call.Choices != null && call.Choices.Length >= 1),
                "Every call needs at least one source-authored action.");
            Require(Enum.GetValues(typeof(StoryFlag)).Length == 7,
                "The game design defines exactly seven consequence flags.");
            Require(Enum.GetValues(typeof(EndingType)).Length == 5,
                "The game must expose exactly five endings.");
            Require(CallDatabase.Endings.Count == 5,
                "Every ending needs authored result text.");
            Require(calls.Last().Choices.Count(choice => choice.FinalAction != FinalAction.None) == 3,
                "The final call must expose the three bridge actions from the scenario.");
            Require(calls.Last().Choices.Count(choice => choice.RequiredFlag == StoryFlag.BelievedWinter) == 1,
                "The 'do not touch the bridge' action must appear only after believing Winter.");

            string authoredText = string.Join("\n", calls.Select(call =>
                call.Transcript + "\n" + call.Clue + "\n" +
                string.Join("\n", call.Choices.Select(choice => choice.Label + "\n" + choice.Consequence))));

            // Защита от случайного возвращения старых придуманных реплик и исключённой ветки.
            Require(!authoredText.Contains("«янтарь»") && !authoredText.Contains("«берег»") && !authoredText.Contains("18-Г"),
                "Invented prototype passwords or coordinates remain in authored text.");
            Require(!authoredText.Contains("Скворец"),
                "The excluded courier branch must not appear in the 24-call version.");

            Debug.Log("LAST SIGNAL VALIDATION PASSED: 24 source-authored calls (6/8/10), 7 flags, 5 endings.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Last Signal validation failed: " + message);
            }
        }
    }
}
