using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Последовательно воспроизводит набор голосовых реплик (AudioClip) через AudioSource.
    /// Следующая реплика стартует строго после фактического окончания предыдущей.
    /// </summary>
    public sealed class DialogueAudioPlayer : MonoBehaviour
    {
        [Header("Настройки аудио")]
        [SerializeField] private AudioSource audioSource;

        [Tooltip("Небольшая естественная пауза между соседними репликами (в секундах).")]
        [SerializeField, Min(0f)] private float pauseBetweenLines = 0.2f;

        private Coroutine _playbackCoroutine;
        private int _playbackToken;

        public bool IsPlaying { get; private set; }
        public int CurrentLineIndex { get; private set; } = -1;

        public event Action<int, AudioClip> OnLineStarted;
        public event Action OnDialogueCompleted;
        public event Action OnDialogueStopped;

        private void Awake()
        {
            EnsureAudioSource();
        }

        /// <summary>
        /// Запускает последовательное воспроизведение массива/списка реплик.
        /// Если уже идёт другой диалог — корректно прерывает его.
        /// </summary>
        public void PlayDialogue(
            IReadOnlyList<AudioClip> clips,
            Action onComplete = null,
            Action<int> onLineStarted = null)
        {
            StopInternal(notifyStopped: IsPlaying);

            if (clips == null || clips.Count == 0)
            {
                onComplete?.Invoke();
                OnDialogueCompleted?.Invoke();
                return;
            }

            EnsureAudioSource();
            _playbackToken++;
            IsPlaying = true;
            _playbackCoroutine = StartCoroutine(PlayRoutine(clips, _playbackToken, onComplete, onLineStarted));
        }

        /// <summary>
        /// Немедленно останавливает текущее воспроизведение диалога.
        /// </summary>
        public void Stop()
        {
            StopInternal(notifyStopped: IsPlaying);
        }

        private void StopInternal(bool notifyStopped)
        {
            _playbackToken++;
            IsPlaying = false;
            CurrentLineIndex = -1;

            if (_playbackCoroutine != null)
            {
                StopCoroutine(_playbackCoroutine);
                _playbackCoroutine = null;
            }

            if (audioSource != null)
            {
                audioSource.Stop();
                audioSource.clip = null;
            }

            if (notifyStopped)
            {
                OnDialogueStopped?.Invoke();
            }
        }

        private IEnumerator PlayRoutine(
            IReadOnlyList<AudioClip> clips,
            int token,
            Action onComplete,
            Action<int> onLineStarted)
        {
            for (int i = 0; i < clips.Count; i++)
            {
                if (token != _playbackToken)
                {
                    yield break;
                }

                AudioClip clip = clips[i];
                if (clip == null)
                {
                    Debug.LogWarning($"[DialogueAudioPlayer] Пропущен пустой AudioClip на индексе {i}.", this);
                    continue;
                }

                CurrentLineIndex = i;
                onLineStarted?.Invoke(i);
                OnLineStarted?.Invoke(i, clip);

                if (token != _playbackToken)
                {
                    yield break;
                }

                yield return PlaySingleClipRoutine(clip, token);

                if (token != _playbackToken)
                {
                    yield break;
                }

                bool hasNextValidClip = HasValidClipAfter(clips, i);
                if (hasNextValidClip && pauseBetweenLines > 0.001f)
                {
                    yield return WaitSecondsRoutine(pauseBetweenLines, token);
                }
            }

            if (token != _playbackToken)
            {
                yield break;
            }

            IsPlaying = false;
            CurrentLineIndex = -1;
            _playbackCoroutine = null;

            onComplete?.Invoke();
            OnDialogueCompleted?.Invoke();
        }

        private IEnumerator PlaySingleClipRoutine(AudioClip clip, int token)
        {
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
                Debug.LogWarning($"[DialogueAudioPlayer] Не удалось загрузить аудиоклип '{clip.name}'.", this);
                yield break;
            }

            audioSource.Stop();
            audioSource.loop = false;
            audioSource.clip = clip;
            audioSource.time = 0f;
            audioSource.Play();

            float pitchAbs = Mathf.Max(0.01f, Mathf.Abs(audioSource.pitch));
            float expectedDuration = clip.length / pitchAbs;
            float activeElapsed = 0f;
            bool playbackStarted = false;

            while (token == _playbackToken)
            {
                // Не считаем клип завершённым, если звук на глобальной паузе или окно игры свёрнуто.
                if (AudioListener.pause || !Application.isFocused)
                {
                    yield return null;
                    continue;
                }

                if (audioSource.isPlaying)
                {
                    playbackStarted = true;
                    activeElapsed += Time.unscaledDeltaTime;

                    // Страховка на случай сбоя аудиодрайвера.
                    if (activeElapsed >= expectedDuration + 0.5f)
                    {
                        break;
                    }
                }
                else
                {
                    // Клип начал играть и остановился в активном окне — воспроизведение честно завершилось.
                    if (playbackStarted)
                    {
                        break;
                    }

                    // Даём аудиоустройству короткий запас на старт воспроизведения в первые кадры.
                    activeElapsed += Time.unscaledDeltaTime;
                    if (activeElapsed >= Mathf.Max(0.25f, expectedDuration))
                    {
                        break;
                    }
                }

                yield return null;
            }

            if (token == _playbackToken && audioSource != null)
            {
                audioSource.Stop();
                audioSource.clip = null;
            }
        }

        private IEnumerator WaitSecondsRoutine(float seconds, int token)
        {
            float remaining = seconds;
            while (remaining > 0f && token == _playbackToken)
            {
                remaining -= Time.deltaTime;
                yield return null;
            }
        }

        private static bool HasValidClipAfter(IReadOnlyList<AudioClip> clips, int currentIndex)
        {
            for (int i = currentIndex + 1; i < clips.Count; i++)
            {
                if (clips[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsureAudioSource()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }

            audioSource.playOnAwake = false;
            audioSource.loop = false;
        }
    }
}
