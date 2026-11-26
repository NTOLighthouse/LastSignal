using System;
using System.Collections;
using UnityEngine;

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Аниматор карандаша журнала (Logbook_Pencil.001):
    /// плавно поднимает карандаш со стола, водит кончиком грифеля по нужной строке правой страницы
    /// синхронно с проявлением заметки и звуком карандаша по бумаге, затем кладёт карандаш обратно на стол.
    /// </summary>
    public sealed class LogbookPencilAnimator : MonoBehaviour
    {
        [Header("Геометрия карандаша")]
        [Tooltip("Локальное смещение кончика грифеля (для модели Logbook_Pencil.001 это -X = -0.081 м).")]
        [SerializeField] private Vector3 localTipOffset = new Vector3(-0.081f, 0f, 0f);

        [Header("Параметры анимации письма")]
        [SerializeField, Min(0.05f)] private float liftDuration = 0.28f;
        [SerializeField, Min(0.05f)] private float returnDuration = 0.34f;
        [SerializeField, Min(0f)] private float transitArcHeight = 0.065f;
        [SerializeField, Range(15f, 75f)] private float writingTiltDegrees = 46f;
        [SerializeField, Min(0f)] private float scribbleAmplitudeX = 0.0055f;
        [SerializeField, Min(0f)] private float scribbleAmplitudeY = 0.0038f;
        [SerializeField, Min(1f)] private float scribbleFrequency = 34f;

        [Header("Звук карандаша по бумаге")]
        [SerializeField] private AudioSource pencilAudioSource;
        [SerializeField] private AudioClip customWritingClip;
        [SerializeField] private AudioClip customShortTickClip;
        [SerializeField, Range(0f, 1f)] private float writingVolume = 0.55f;
        [SerializeField, Range(0f, 1f)] private float shortTickVolume = 0.4f;

        private Vector3 _restWorldPosition;
        private Quaternion _restWorldRotation;
        private bool _restCaptured;

        private Coroutine _activeRoutine;
        private AudioClip _proceduralWritingClip;
        private AudioClip _proceduralShortClip;

        public bool IsWriting { get; private set; }

        private void Awake()
        {
            EnsureAudioSource();
        }

        private void Start()
        {
            CaptureRestPoseIfNeeded();
        }

        private void OnDestroy()
        {
            DestroyProceduralClip(ref _proceduralWritingClip);
            DestroyProceduralClip(ref _proceduralShortClip);
        }

        /// <summary>
        /// Проигрывает короткий штрих карандаша (например, при клике по строке позывного на левой странице).
        /// </summary>
        public void PlayShortScratchSound()
        {
            EnsureAudioSource();
            if (pencilAudioSource == null)
            {
                return;
            }

            AudioClip clip = customShortTickClip != null
                ? customShortTickClip
                : (_proceduralShortClip ??= CreatePencilScratchClip("Pencil_ShortTick", 0.22f, 2, 1945));

            pencilAudioSource.PlayOneShot(clip, shortTickVolume);
        }

        /// <summary>
        /// Запускает полную анимацию: подъём карандаша -> письмо по указанной строке страницы со звуком -> возврат на стол.
        /// </summary>
        /// <param name="pageTransform">Трансформ правой страницы (Logbook_Page_Right.001).</param>
        /// <param name="localStartOnPage">Локальная точка начала строки на странице.</param>
        /// <param name="localEndOnPage">Локальная точка конца строки на странице.</param>
        /// <param name="writeDurationSeconds">Длительность вождения по странице.</param>
        /// <param name="onWriteProgress">Коллбэк прогресса письма 0..1 (для плавного проявления текста).</param>
        /// <param name="onCompleted">Коллбэк после полного возврата карандаша на стол.</param>
        public void PlayWriteAnimation(
            Transform pageTransform,
            Vector3 localStartOnPage,
            Vector3 localEndOnPage,
            float writeDurationSeconds,
            Action<float> onWriteProgress = null,
            Action onCompleted = null)
        {
            if (pageTransform == null)
            {
                onWriteProgress?.Invoke(1f);
                onCompleted?.Invoke();
                return;
            }

            CaptureRestPoseIfNeeded();

            if (_activeRoutine != null)
            {
                StopCoroutine(_activeRoutine);
                _activeRoutine = null;
            }

            _activeRoutine = StartCoroutine(WriteRoutine(
                pageTransform,
                localStartOnPage,
                localEndOnPage,
                Mathf.Max(0.35f, writeDurationSeconds),
                onWriteProgress,
                onCompleted));
        }

        private IEnumerator WriteRoutine(
            Transform pageTransform,
            Vector3 localStart,
            Vector3 localEnd,
            float writeDuration,
            Action<float> onWriteProgress,
            Action onCompleted)
        {
            IsWriting = true;

            Vector3 startPos = transform.position;
            Quaternion startRot = transform.rotation;

            // Фаза 1: Плавно поднимаем карандаш со стола и подводим острие к началу строки.
            float elapsed = 0f;
            while (elapsed < liftDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float s = SmootherStep(elapsed / liftDuration);

                ComputePencilPoseOnPage(pageTransform, localStart, 0f, out Vector3 targetPos, out Quaternion targetRot);
                Vector3 pos = Vector3.Lerp(startPos, targetPos, s);
                pos += pageTransform.forward * (Mathf.Sin(s * Mathf.PI) * transitArcHeight);

                transform.position = pos;
                transform.rotation = Quaternion.Slerp(startRot, targetRot, s);
                yield return null;
            }

            // Фаза 2: Карандаш водит по строке с живым письмом и звуком грифеля по бумаге.
            PlayWritingScratchSound(writeDuration);

            elapsed = 0f;
            while (elapsed < writeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(elapsed / writeDuration);

                Vector3 localLinePoint = Vector3.Lerp(localStart, localEnd, u);
                // Живые мелкие рукописные штрихи в плоскости страницы.
                float scribbleX = Mathf.Sin(elapsed * scribbleFrequency) * scribbleAmplitudeX;
                float scribbleY = Mathf.Cos(elapsed * scribbleFrequency * 0.68f) * scribbleAmplitudeY;
                localLinePoint.x += scribbleX;
                localLinePoint.y += scribbleY;

                ComputePencilPoseOnPage(pageTransform, localLinePoint, elapsed, out Vector3 penPos, out Quaternion penRot);
                transform.position = penPos;
                transform.rotation = penRot;

                onWriteProgress?.Invoke(u);
                yield return null;
            }

            onWriteProgress?.Invoke(1f);

            // Фаза 3: Плавно возвращаем карандаш обратно на его исходное место на столе.
            Vector3 leavePos = transform.position;
            Quaternion leaveRot = transform.rotation;
            elapsed = 0f;
            while (elapsed < returnDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float s = SmootherStep(elapsed / returnDuration);

                Vector3 pos = Vector3.Lerp(leavePos, _restWorldPosition, s);
                pos.y += Mathf.Sin(s * Mathf.PI) * transitArcHeight;

                transform.position = pos;
                transform.rotation = Quaternion.Slerp(leaveRot, _restWorldRotation, s);
                yield return null;
            }

            transform.position = _restWorldPosition;
            transform.rotation = _restWorldRotation;

            IsWriting = false;
            _activeRoutine = null;
            onCompleted?.Invoke();
        }

        private void ComputePencilPoseOnPage(
            Transform pageTransform,
            Vector3 localTipOnPage,
            float scribbleTime,
            out Vector3 worldPos,
            out Quaternion worldRot)
        {
            // Для Logbook_Page_Right.001: +forward = нормаль страницы наружу, +up = вправо, +right = вверх по странице.
            Vector3 tipWorld = pageTransform.TransformPoint(localTipOnPage);
            Vector3 pageNormal = pageTransform.forward.normalized;
            Vector3 pageRight = pageTransform.up.normalized;
            Vector3 pageDown = -pageTransform.right.normalized;

            // Ось карандаша (+X, от грифеля к ластику) наклонена от поверхности страницы вправо-вниз (как в правой руке).
            float tiltRad = writingTiltDegrees * Mathf.Deg2Rad;
            float wobble = Mathf.Sin(scribbleTime * 19f) * 0.04f;
            Vector3 sideDir = (pageRight * 0.72f + pageDown * 0.69f).normalized;
            Vector3 pencilAxis = (pageNormal * Mathf.Sin(tiltRad) + sideDir * Mathf.Cos(tiltRad) + pageRight * wobble).normalized;

            // Поворачиваем так, чтобы локальный +X карандаша совпал с pencilAxis.
            Vector3 orthoUp = Vector3.Cross(pageNormal, pencilAxis);
            if (orthoUp.sqrMagnitude < 0.0001f)
            {
                orthoUp = Vector3.up;
            }
            orthoUp.Normalize();
            Vector3 orthoFwd = Vector3.Cross(pencilAxis, orthoUp).normalized;

            // Базис: right = pencilAxis, up = orthoUp, forward = orthoFwd.
            // В Quaternion.LookRotation(fwd, up): local +Z = fwd, local +Y = up, local +X = cross(up, fwd).
            // Нам нужно cross(up, fwd) == pencilAxis -> up = orthoUp, fwd = -orthoFwd.
            worldRot = Quaternion.LookRotation(-orthoFwd, orthoUp);

            // Смещаем центр карандаша так, чтобы кончик localTipOffset (-0.081, 0, 0) оказался точно в tipWorld.
            worldPos = tipWorld - (worldRot * localTipOffset);
        }

        private void PlayWritingScratchSound(float duration)
        {
            EnsureAudioSource();
            if (pencilAudioSource == null)
            {
                return;
            }

            if (customWritingClip != null)
            {
                pencilAudioSource.PlayOneShot(customWritingClip, writingVolume);
                return;
            }

            if (_proceduralWritingClip == null)
            {
                _proceduralWritingClip = CreatePencilScratchClip("Pencil_Writing", 1.35f, 8, 1943);
            }

            pencilAudioSource.pitch = Mathf.Clamp(1.35f / Mathf.Max(0.4f, duration), 0.75f, 1.35f);
            pencilAudioSource.PlayOneShot(_proceduralWritingClip, writingVolume);
        }

        private void EnsureAudioSource()
        {
            if (pencilAudioSource == null)
            {
                pencilAudioSource = GetComponent<AudioSource>();
            }

            if (pencilAudioSource == null)
            {
                pencilAudioSource = gameObject.AddComponent<AudioSource>();
            }

            pencilAudioSource.playOnAwake = false;
            pencilAudioSource.loop = false;
            pencilAudioSource.spatialBlend = 0.35f;
        }

        private void CaptureRestPoseIfNeeded()
        {
            if (_restCaptured)
            {
                return;
            }

            _restWorldPosition = transform.position;
            _restWorldRotation = transform.rotation;
            _restCaptured = true;
        }

        /// <summary>
        /// Процедурно синтезирует серию сухих графитных штрихов карандаша по бумаге (~2800–4300 Гц),
        /// повторяя акустику из HTML-прототипа.
        /// </summary>
        private static AudioClip CreatePencilScratchClip(string clipName, float durationSec, int strokes, int seed)
        {
            const int sampleRate = 44100;
            int totalSamples = Mathf.Max(512, Mathf.RoundToInt(sampleRate * durationSec));
            float[] buffer = new float[totalSamples];

            var rng = new System.Random(seed);
            float stepSec = durationSec / Mathf.Max(1, strokes);

            for (int s = 0; s < strokes; s++)
            {
                float startSec = s * stepSec;
                float strokeDur = stepSec * 0.74f;
                int startSample = Mathf.RoundToInt(startSec * sampleRate);
                int strokeSamples = Mathf.RoundToInt(strokeDur * sampleRate);

                // Простой резонансный полосовой фильтр (2-pole biquad) на 2900–4200 Гц.
                float fcStart = 2850f + (s % 3) * 420f;
                float fcEnd = 4150f;
                float q = 3.0f;

                float y1 = 0f, y2 = 0f, x1 = 0f, x2 = 0f;
                for (int i = 0; i < strokeSamples && (startSample + i) < totalSamples; i++)
                {
                    float u = i / (float)Mathf.Max(1, strokeSamples);
                    float fc = Mathf.Lerp(fcStart, fcEnd, u);
                    float w0 = 2f * Mathf.PI * fc / sampleRate;
                    float alpha = Mathf.Sin(w0) / (2f * q);
                    float cosW0 = Mathf.Cos(w0);

                    float b0 = alpha;
                    float b2 = -alpha;
                    float a0 = 1f + alpha;
                    float a1 = -2f * cosW0;
                    float a2 = 1f - alpha;

                    float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                    float y = (b0 * white + b2 * x2 - a1 * y1 - a2 * y2) / a0;
                    x2 = x1; x1 = white;
                    y2 = y1; y1 = y;

                    // Плавная атака и сухой спад каждого штриха.
                    float env = u < 0.22f
                        ? (u / 0.22f)
                        : Mathf.Pow(1f - (u - 0.22f) / 0.78f, 1.8f);

                    // Мелкая текстура шероховатости бумаги.
                    float grain = 0.75f + 0.25f * Mathf.Sin(u * Mathf.PI * 10f);

                    buffer[startSample + i] += y * env * grain * 1.15f;
                }
            }

            for (int i = 0; i < totalSamples; i++)
            {
                buffer[i] = Mathf.Clamp(buffer[i], -0.95f, 0.95f);
            }

            AudioClip clip = AudioClip.Create(clipName, totalSamples, 1, sampleRate, false);
            clip.SetData(buffer, 0);
            return clip;
        }

        private static float SmootherStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private static void DestroyProceduralClip(ref AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(clip);
            }
            else
            {
                DestroyImmediate(clip);
            }

            clip = null;
        }
    }
}
