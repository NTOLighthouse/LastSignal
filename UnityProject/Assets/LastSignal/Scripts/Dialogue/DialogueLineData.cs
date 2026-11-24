using System;
using UnityEngine;

namespace LastSignal.Dialogue
{
    /// <summary>
    /// Данные одной голосовой реплики внутри диалоговой последовательности.
    /// Длительность определяется автоматически по AudioClip; если клип ещё не добавлен — по fallbackDuration.
    /// </summary>
    [Serializable]
    public sealed class DialogueLineData
    {
        [Tooltip("Кто произносит реплику (отображается в логах и субтитрах).")]
        [SerializeField] private string speakerName = "Абонент";

        [Tooltip("Текст реплики для субтитров и отладки.")]
        [SerializeField, TextArea(2, 4)] private string subtitleText = string.Empty;

        [Tooltip("Аудиофайл реплики. Следующая реплика начнётся строго после окончания этого файла.")]
        [SerializeField] private AudioClip voiceClip;

        [Tooltip("Длительность в секундах, если AudioClip пока не вставлен. 0 = авторасчёт по длине текста.")]
        [SerializeField, Min(0f)] private float fallbackDuration = 0f;

        [Tooltip("Естественная пауза после окончания реплики перед следующей (в секундах).")]
        [SerializeField, Min(0f)] private float pauseAfterLine = 0.35f;

        [Tooltip("Опциональный ключ сигнала дирижёру при старте этой реплики (например: 'ShowBookHint').")]
        [SerializeField] private string signalOnStart = string.Empty;

        [Tooltip("Опциональный ключ сигнала дирижёру сразу после окончания этой реплики.")]
        [SerializeField] private string signalOnEnd = string.Empty;

        public string SpeakerName => speakerName;
        public string SubtitleText => subtitleText;
        public AudioClip VoiceClip => voiceClip;
        public float FallbackDuration => fallbackDuration;
        public float PauseAfterLine => pauseAfterLine;
        public string SignalOnStart => signalOnStart;
        public string SignalOnEnd => signalOnEnd;

        public DialogueLineData() { }

        public DialogueLineData(
            string speaker,
            string text,
            AudioClip clip = null,
            float fallbackSeconds = 0f,
            float pauseAfter = 0.35f,
            string startSignal = "",
            string endSignal = "")
        {
            speakerName = speaker;
            subtitleText = text;
            voiceClip = clip;
            fallbackDuration = Mathf.Max(0f, fallbackSeconds);
            pauseAfterLine = Mathf.Max(0f, pauseAfter);
            signalOnStart = startSignal ?? string.Empty;
            signalOnEnd = endSignal ?? string.Empty;
        }

        /// <summary>
        /// Возвращает резервную длительность (если AudioClip отсутствует).
        /// </summary>
        public float ResolveFallbackDuration()
        {
            if (fallbackDuration > 0.01f)
            {
                return fallbackDuration;
            }

            int length = string.IsNullOrEmpty(subtitleText) ? 0 : subtitleText.Length;
            return Mathf.Clamp(1.4f + length * 0.045f, 1.6f, 8.0f);
        }
    }
}
