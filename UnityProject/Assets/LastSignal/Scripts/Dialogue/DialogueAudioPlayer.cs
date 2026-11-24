using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LastSignal.Dialogue
{
    /// <summary>
    /// Надёжный последовательный проигрыватель голосовых реплик.
    /// Гарантирует, что следующая реплика запускается строго после реального завершения предыдущего AudioClip,
    /// устойчив к паузе, потере фокуса окна и мгновенному прерыванию при выдёргивании штекера.
    /// </summary>
    public sealed class DialogueAudioPlayer : MonoBehaviour
    {
        [Header("Аудио-источник для голосов")]
        [SerializeField] private AudioSource voiceAudioSource;

        [Header("Отладка")]
        [SerializeField] private bool logPlaybackToConsole = true;

        private Coroutine _activeCoroutine;
        private int _playbackToken;
        private bool _isPaused;
        private bool _skipLineRequested;

        public bool IsPlaying { get; private set; }
        public bool IsPaused => _isPaused;
        public DialogueLineData CurrentLine { get; private set; }
        public int CurrentLineIndex { get; private set; } = -1;

        /// <summary>Срабатывает в момент старта каждой реплики.</summary>
        public event Action<DialogueLineData, int> OnLineStarted;

        /// <summary>Срабатывает сразу после окончания каждой реплики (до паузы pauseAfterLine).</summary>
        public event Action<DialogueLineData, int> OnLineFinished;

        /// <summary>Срабатывает, когда вся последовательность реплик полностью доиграла до конца.</summary>
        public event Action OnSequenceCompleted;

        /// <summary>Срабатывает, если последовательность была принудительно остановлена.</summary>
        public event Action OnSequenceCancelled;

        private void Awake()
        {
            EnsureAudioSource();
        }

        /// <summary>
        /// Запускает последовательное воспроизведение списка реплик.
        /// Если уже играет другая последовательность — корректно прерывает её.
        /// </summary>
        public void PlaySequence(IReadOnlyList<DialogueLineData> lines, Action onCompleted = null)
        {
            Stop(notifyCancelled: IsPlaying);

            if (lines == null || lines.Count == 0)
            {
                onCompleted?.Invoke();
                OnSequenceCompleted?.Invoke();
                return;
            }

            EnsureAudioSource();
            _playbackToken++;
            _isPaused = false;
            _skipLineRequested = false;
            IsPlaying = true;
            _activeCoroutine = StartCoroutine(PlaySequenceRoutine(lines, _playbackToken, onCompleted));
        }

        /// <summary>
        /// Ставит текущую реплику и ожидание на паузу (например, при временном разрыве линии).
        /// </summary>
        public void Pause()
        {
            if (!IsPlaying || _isPaused)
            {
                return;
            }

            _isPaused = true;
            if (voiceAudioSource != null && voiceAudioSource.isPlaying)
            {
                voiceAudioSource.Pause();
            }
        }

        /// <summary>
        /// Продолжает воспроизведение строго с места паузы.
        /// </summary>
        public void Resume()
        {
            if (!IsPlaying || !_isPaused)
            {
                return;
            }

            _isPaused = false;
            if (voiceAudioSource != null && voiceAudioSource.clip != null)
            {
                voiceAudioSource.UnPause();
            }
        }

        /// <summary>
        /// Полностью останавливает текущую последовательность реплик и сбрасывает токен.
        /// </summary>
        public void Stop(bool notifyCancelled = true)
        {
            bool wasPlaying = IsPlaying;
            _playbackToken++;
            _isPaused = false;
            _skipLineRequested = false;
            IsPlaying = false;
            CurrentLine = null;
            CurrentLineIndex = -1;

            if (_activeCoroutine != null)
            {
                StopCoroutine(_activeCoroutine);
                _activeCoroutine = null;
            }

            if (voiceAudioSource != null)
            {
                voiceAudioSource.Stop();
                voiceAudioSource.clip = null;
            }

            if (wasPlaying && notifyCancelled)
            {
                OnSequenceCancelled?.Invoke();
            }
        }

        /// <summary>
        /// Пропускает текущую реплику (удобно для быстрого тестирования сценария).
        /// </summary>
        [ContextMenu("Пропустить текущую реплику")]
        public void SkipCurrentLine()
        {
            if (!IsPlaying)
            {
                return;
            }

            _skipLineRequested = true;
            if (voiceAudioSource != null && voiceAudioSource.isPlaying)
            {
                voiceAudioSource.Stop();
            }
        }

        public void Configure(AudioSource audioSource)
        {
            voiceAudioSource = audioSource;
        }

        private IEnumerator PlaySequenceRoutine(IReadOnlyList<DialogueLineData> lines, int token, Action onCompleted)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (token != _playbackToken)
                {
                    yield break;
                }

                DialogueLineData line = lines[i];
                if (line == null)
                {
                    continue;
                }

                CurrentLine = line;
                CurrentLineIndex = i;
                _skipLineRequested = false;

                if (logPlaybackToConsole)
                {
                    string clipInfo = line.VoiceClip != null
                        ? $"clip='{line.VoiceClip.name}' ({line.VoiceClip.length:0.00}s)"
                        : $"без клипа (fallback {line.ResolveFallbackDuration():0.00}s)";
                    Debug.Log($"[Dialogue] [{i + 1}/{lines.Count}] {line.SpeakerName}: «{line.SubtitleText}» [{clipInfo}]");
                }

                OnLineStarted?.Invoke(line, i);

                // Если подписчик события остановил плеер прямо из OnLineStarted — выходим.
                if (token != _playbackToken)
                {
                    yield break;
                }

                if (line.VoiceClip != null)
                {
                    yield return PlayAudioClipStrict(line.VoiceClip, token);
                }
                else
                {
                    yield return WaitPausableDuration(line.ResolveFallbackDuration(), token, allowSkip: true);
                }

                if (token != _playbackToken)
                {
                    yield break;
                }

                OnLineFinished?.Invoke(line, i);

                if (token != _playbackToken)
                {
                    yield break;
                }

                // Небольшая пауза между репликами (если это не последняя, или пауза явно задана).
                if (line.PauseAfterLine > 0.001f)
                {
                    yield return WaitPausableDuration(line.PauseAfterLine, token, allowSkip: true);
                }
            }

            if (token != _playbackToken)
            {
                yield break;
            }

            IsPlaying = false;
            CurrentLine = null;
            CurrentLineIndex = -1;
            _activeCoroutine = null;

            onCompleted?.Invoke();
            OnSequenceCompleted?.Invoke();
        }

        private IEnumerator PlayAudioClipStrict(AudioClip clip, int token)
        {
            // Гарантируем, что аудиоданные загружены в память до старта Play().
            if (clip.loadState == AudioDataLoadState.Unloaded)
            {
                clip.LoadAudioData();
            }

            float loadTimeout = 5f;
            while (clip.loadState == AudioDataLoadState.Loading && loadTimeout > 0f)
            {
                if (token != _playbackToken)
                {
                    yield break;
                }
                loadTimeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            if (clip.loadState == AudioDataLoadState.Failed)
            {
                Debug.LogWarning($"[Dialogue] Не удалось загрузить аудиоклип '{clip.name}'. Пропуск.");
                yield break;
            }

            voiceAudioSource.Stop();
            voiceAudioSource.loop = false;
            voiceAudioSource.clip = clip;
            voiceAudioSource.time = 0f;
            voiceAudioSource.Play();

            // Если игра прямо сейчас на паузе — ставим на паузу сразу.
            if (_isPaused)
            {
                voiceAudioSource.Pause();
            }

            float pitchAbs = Mathf.Max(0.01f, Mathf.Abs(voiceAudioSource.pitch));
            float expectedDuration = clip.length / pitchAbs;
            float activeElapsed = 0f;
            bool playbackStarted = false;

            while (token == _playbackToken)
            {
                if (_skipLineRequested)
                {
                    voiceAudioSource.Stop();
                    break;
                }

                // Во время паузы или свёрнутого окна не считаем клип завершённым.
                if (_isPaused || AudioListener.pause || !Application.isFocused)
                {
                    yield return null;
                    continue;
                }

                if (voiceAudioSource.isPlaying)
                {
                    playbackStarted = true;
                    activeElapsed += Time.unscaledDeltaTime;

                    // Защита от зависания аудиодрайвера (на случай нештатного поведения).
                    if (activeElapsed >= expectedDuration + 0.5f)
                    {
                        break;
                    }
                }
                else
                {
                    // Если клип уже начал играть и теперь остановился в активном окне — он честно доиграл.
                    if (playbackStarted)
                    {
                        break;
                    }

                    // Если аудиоустройство ещё не подхватило старт в первый кадр, ждём до 0.25с.
                    activeElapsed += Time.unscaledDeltaTime;
                    if (activeElapsed >= Mathf.Max(0.25f, expectedDuration))
                    {
                        break;
                    }
                }

                yield return null;
            }

            if (token == _playbackToken && voiceAudioSource != null)
            {
                voiceAudioSource.Stop();
                voiceAudioSource.clip = null;
            }
        }

        private IEnumerator WaitPausableDuration(float durationSeconds, int token, bool allowSkip)
        {
            float remaining = durationSeconds;
            while (remaining > 0f && token == _playbackToken)
            {
                if (allowSkip && _skipLineRequested)
                {
                    break;
                }

                if (!_isPaused)
                {
                    remaining -= Time.deltaTime;
                }

                yield return null;
            }
        }

        private void EnsureAudioSource()
        {
            if (voiceAudioSource == null)
            {
                voiceAudioSource = GetComponent<AudioSource>();
            }

            if (voiceAudioSource == null)
            {
                voiceAudioSource = gameObject.AddComponent<AudioSource>();
            }

            voiceAudioSource.playOnAwake = false;
            voiceAudioSource.loop = false;
            voiceAudioSource.spatialBlend = 0f; // Голос звучит прямо в наушниках Ларина.
        }
    }
}
