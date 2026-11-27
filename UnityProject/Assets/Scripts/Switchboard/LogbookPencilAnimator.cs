using System;
using System.Collections;
using UnityEngine;

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Аниматор карандаша журнала (Logbook_Pencil.001):
    /// плавно поднимает карандаш со стола по дуге Безье, касается кончиком грифеля точно по кривой
    /// поверхности правой страницы (без проваливания под книгу/стол), выводит мягкие рукописные штрихи
    /// синхронно с проявлением текста и звуком графита, затем аккуратно кладёт карандаш обратно на стол.
    /// </summary>
    [DefaultExecutionOrder(0)]
    public sealed class LogbookPencilAnimator : MonoBehaviour
    {
        // Точный замеренный профиль кривизны Logbook_Page_Right.001 (y = 0.0020 .. 0.1560 -> z поверхности).
        private static readonly float[] PageCurveZ =
        {
            0.00740f, 0.01012f, 0.01276f, 0.01527f, 0.01762f,
            0.01978f, 0.02170f, 0.02336f, 0.02472f, 0.02578f,
            0.02651f, 0.02691f, 0.02697f, 0.02669f, 0.02609f,
            0.02517f, 0.02396f
        };

        [Header("Геометрия карандаша")]
        [Tooltip("Локальное смещение острого кончика грифеля (для Logbook_Pencil.001 это -X = -0.081 м).")]
        [SerializeField] private Vector3 localTipOffset = new Vector3(-0.081f, 0f, 0f);
        [Tooltip("Минимальный зазор кончика грифеля над поверхностью страницы, чтобы исключить клиппинг.")]
        [SerializeField, Min(0f)] private float surfaceClearanceMeters = 0.0018f;

        [Header("Плавная хореография письма (12 принципов анимации)")]
        [SerializeField, Min(0.15f)] private float liftDuration = 0.48f;
        [SerializeField, Min(0.15f)] private float returnDuration = 0.52f;
        [SerializeField, Min(0.02f)] private float transitArcHeight = 0.085f;
        [SerializeField, Range(25f, 70f)] private float writingTiltDegrees = 44f;
        [SerializeField, Range(0f, 0.006f)] private float cursiveAmplitudeVertical = 0.0026f;
        [SerializeField, Range(0f, 0.004f)] private float cursiveAmplitudeHorizontal = 0.0016f;
        [SerializeField, Range(4f, 22f)] private float cursiveFrequency = 13.5f;

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
        /// Возвращает точную локальную Z-координату изогнутой поверхности страницы журнала по локальной Y (0.002..0.156).
        /// </summary>
        public static float SampleCurvedPageSurfaceZ(float localYAbs)
        {
            float t = Mathf.InverseLerp(0.0020f, 0.1560f, Mathf.Abs(localYAbs));
            float scaled = t * (PageCurveZ.Length - 1);
            int idx0 = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, PageCurveZ.Length - 1);
            int idx1 = Mathf.Clamp(idx0 + 1, 0, PageCurveZ.Length - 1);
            float frac = scaled - idx0;
            return Mathf.Lerp(PageCurveZ[idx0], PageCurveZ[idx1], frac);
        }

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
                Mathf.Max(0.6f, writeDurationSeconds),
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

            // Фаза 1: Плавный подъём карандаша со стола по дуге Безье к началу строки.
            float elapsed = 0f;
            while (elapsed < liftDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float p = SmootherStep(elapsed / liftDuration);

                Vector3 startPointOnSurface = ClampLocalPointToSurface(localStart);
                ComputePencilPoseOnPage(pageTransform, startPointOnSurface, 0f, 0f, out Vector3 targetPos, out Quaternion targetRot);

                Vector3 liftUpDir = (Vector3.up + pageTransform.forward).normalized;
                Vector3 ctrl = Vector3.Lerp(startPos, targetPos, 0.52f) + liftUpDir * transitArcHeight;
                transform.position = EvalQuadraticBezier(startPos, ctrl, targetPos, p);
                transform.rotation = Quaternion.Slerp(startRot, targetRot, p);
                yield return null;
            }

            // Фаза 2: Плавное рукописное ведение грифеля по кривой поверхности страницы.
            PlayWritingScratchSound(writeDuration);

            elapsed = 0f;
            while (elapsed < writeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float uRaw = Mathf.Clamp01(elapsed / writeDuration);
                float uMove = Mathf.Lerp(uRaw, SmootherStep(uRaw), 0.35f);

                // Мягкое нарастание и затухание рукописных колебаний у начала и конца строки (без рывков).
                float strokeEnv = Mathf.Clamp01(uRaw * 7f) * Mathf.Clamp01((1f - uRaw) * 7f);

                Vector3 localPt = Vector3.Lerp(localStart, localEnd, uMove);
                localPt.x += Mathf.Sin(elapsed * cursiveFrequency) * cursiveAmplitudeVertical * strokeEnv;
                localPt.y += Mathf.Cos(elapsed * cursiveFrequency * 0.72f) * cursiveAmplitudeHorizontal * strokeEnv;
                localPt = ClampLocalPointToSurface(localPt);

                // Лёгкий подъём острия в самые последние 8% (естественный отрыв руки от бумаги).
                float liftOff = Mathf.InverseLerp(0.92f, 1.0f, uRaw) * 0.008f;
                localPt.z += liftOff;

                ComputePencilPoseOnPage(pageTransform, localPt, elapsed, strokeEnv, out Vector3 penPos, out Quaternion penRot);
                transform.position = penPos;
                transform.rotation = penRot;

                onWriteProgress?.Invoke(uRaw);
                yield return null;
            }

            onWriteProgress?.Invoke(1f);

            // Фаза 3: Плавный возврат карандаша по дуге Безье на исходное место на столе.
            Vector3 leavePos = transform.position;
            Quaternion leaveRot = transform.rotation;
            elapsed = 0f;
            while (elapsed < returnDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float p = SmootherStep(elapsed / returnDuration);

                Vector3 ctrl = Vector3.Lerp(leavePos, _restWorldPosition, 0.45f) + Vector3.up * transitArcHeight;
                transform.position = EvalQuadraticBezier(leavePos, ctrl, _restWorldPosition, p);
                transform.rotation = Quaternion.Slerp(leaveRot, _restWorldRotation, p);
                yield return null;
            }

            transform.position = _restWorldPosition;
            transform.rotation = _restWorldRotation;

            IsWriting = false;
            _activeRoutine = null;
            onCompleted?.Invoke();
        }

        private Vector3 ClampLocalPointToSurface(Vector3 localPointOnPage)
        {
            localPointOnPage.z = SampleCurvedPageSurfaceZ(localPointOnPage.y) + surfaceClearanceMeters;
            return localPointOnPage;
        }

        private void ComputePencilPoseOnPage(
            Transform pageTransform,
            Vector3 localTipOnPage,
            float scribbleTime,
            float strokeEnv,
            out Vector3 worldPos,
            out Quaternion worldRot)
        {
            // Для Logbook_Page_Right.001:
            // +forward = нормаль страницы наружу (вверх от бумаги),
            // +up      = слева направо по строке (от корешка к правому краю),
            // +right   = снизу вверх вдоль страницы.
            Vector3 tipWorld = pageTransform.TransformPoint(localTipOnPage);
            Vector3 pageNormal = pageTransform.forward.normalized;
            Vector3 pageRight = pageTransform.up.normalized;
            Vector3 pageDown = -pageTransform.right.normalized;

            // Вектор от острия (-X) к ластику (+X): вверх от бумаги под углом writingTiltDegrees и вправо-вниз (как в правой руке).
            float tiltRad = writingTiltDegrees * Mathf.Deg2Rad;
            float sway = Mathf.Sin(scribbleTime * 8.5f) * 0.035f * strokeEnv;
            Vector3 sideDir = (pageRight * 0.68f + pageDown * 0.73f).normalized;
            Vector3 pencilAxis = (pageNormal * Mathf.Sin(tiltRad) + sideDir * Mathf.Cos(tiltRad) + pageRight * sway).normalized;

            // В левосторонней СК Unity: Quaternion.LookRotation(fwd, up) задаёт local +X = Cross(up, fwd).
            // Берём orthoUp = Cross(pageNormal, pencilAxis) и orthoFwd = Cross(pencilAxis, orthoUp).
            // Тогда Cross(orthoUp, orthoFwd) == +pencilAxis -> кончик (-X) точно направлен в бумагу, тело (+X) строго над бумагой!
            Vector3 orthoUp = Vector3.Cross(pageNormal, pencilAxis);
            if (orthoUp.sqrMagnitude < 0.0001f)
            {
                orthoUp = pageRight;
            }
            orthoUp.Normalize();
            Vector3 orthoFwd = Vector3.Cross(pencilAxis, orthoUp).normalized;

            worldRot = Quaternion.LookRotation(orthoFwd, orthoUp);

            // Ставим центр карандаша так, чтобы острый кончик localTipOffset (-0.081, 0, 0) оказался точно в tipWorld.
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
            if (pencilAudioSource == null) pencilAudioSource = GetComponent<AudioSource>();
            if (pencilAudioSource == null) pencilAudioSource = gameObject.AddComponent<AudioSource>();

            pencilAudioSource.playOnAwake = false;
            pencilAudioSource.loop = false;
            pencilAudioSource.spatialBlend = 0.35f;
        }

        private void CaptureRestPoseIfNeeded()
        {
            if (_restCaptured) return;
            _restWorldPosition = transform.position;
            _restWorldRotation = transform.rotation;
            _restCaptured = true;
        }

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

                    float env = u < 0.22f
                        ? (u / 0.22f)
                        : Mathf.Pow(1f - (u - 0.22f) / 0.78f, 1.8f);
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

        private static Vector3 EvalQuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
        {
            float inv = 1f - t;
            return inv * inv * p0 + 2f * inv * t * p1 + t * t * p2;
        }

        private static float SmootherStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private static void DestroyProceduralClip(ref AudioClip clip)
        {
            if (clip == null) return;
            if (Application.isPlaying) Destroy(clip);
            else DestroyImmediate(clip);
            clip = null;
        }
    }
}
