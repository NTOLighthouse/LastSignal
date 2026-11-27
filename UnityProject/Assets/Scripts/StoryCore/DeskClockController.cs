using UnityEngine;

namespace LastSignal.StoryCore
{
    /// <summary>
    /// Живые дежурные часы на столе (Clock_Root.001).
    /// Плавно вращают часовую, минутную и секундную стрелки в соответствии с игровым временем смены
    /// и издают мягкий мерный тик.
    /// </summary>
    public sealed class DeskClockController : MonoBehaviour
    {
        [Header("Ссылки на стрелки")]
        [SerializeField] private Transform hourHand;
        [SerializeField] private Transform minuteHand;
        [SerializeField] private Transform secondHand;

        [Header("Ось вращения стрелок (в локальных координатах циферблата)")]
        [SerializeField] private Vector3 localRotationAxis = Vector3.right;
        [Tooltip("+1 или -1 для совпадения направления хода по часовой стрелке.")]
        [SerializeField] private float clockwiseSign = -1f;

        [Header("Игровое время")]
        [SerializeField, Range(0, 23)] private int startHour = 23;
        [SerializeField, Range(0, 59)] private int startMinute = 40;
        [SerializeField, Min(0f)] private float gameMinutesPerRealSecond = 0.45f;
        [SerializeField] private bool isRunning = true;

        [Header("Мерный тик часов")]
        [SerializeField] private AudioSource tickAudioSource;
        [SerializeField] private bool enableSoftTick = true;
        [SerializeField, Range(0f, 0.3f)] private float tickVolume = 0.045f;

        private float _totalGameMinutes;
        private float _secondAccumulator;
        private int _lastSecondTick;
        private AudioClip _tickClip;

        public float TotalGameMinutes => _totalGameMinutes;
        public int CurrentHour => Mathf.FloorToInt(Mathf.Repeat(_totalGameMinutes, 1440f) / 60f);
        public int CurrentMinute => Mathf.FloorToInt(Mathf.Repeat(_totalGameMinutes, 60f));
        public string FormattedTime => $"{CurrentHour:00}:{CurrentMinute:00}";

        /// <summary>Временно отключает звук тиканья часов (например, 10 секунд полной тишины в финале Ночи 1).</summary>
        public bool SilenceTick { get; set; }

        private void Awake()
        {
            _totalGameMinutes = startHour * 60f + startMinute;
            EnsureTickAudio();
            ApplyHandsImmediate();
        }

        private void OnDestroy()
        {
            if (_tickClip != null) Destroy(_tickClip);
        }

        private void Update()
        {
            if (!isRunning)
            {
                return;
            }

            float dt = Time.deltaTime;
            _totalGameMinutes = Mathf.Repeat(_totalGameMinutes + dt * gameMinutesPerRealSecond, 1440f);
            _secondAccumulator += dt;

            int currentSec = Mathf.FloorToInt(_secondAccumulator);
            if (currentSec != _lastSecondTick)
            {
                _lastSecondTick = currentSec;
                if (enableSoftTick && !SilenceTick && tickAudioSource != null && _tickClip != null)
                {
                    tickAudioSource.pitch = (currentSec % 2 == 0) ? 1.0f : 0.92f;
                    tickAudioSource.PlayOneShot(_tickClip, tickVolume);
                }
            }

            ApplyHandsImmediate();
        }

        public void SetClock(int hour, int minute, float minutesPerRealSecond)
        {
            startHour = Mathf.Clamp(hour, 0, 23);
            startMinute = Mathf.Clamp(minute, 0, 59);
            gameMinutesPerRealSecond = Mathf.Max(0f, minutesPerRealSecond);
            _totalGameMinutes = startHour * 60f + startMinute;
            ApplyHandsImmediate();
        }

        public void SetRunning(bool running)
        {
            isRunning = running;
        }

        private void ApplyHandsImmediate()
        {
            Vector3 axis = localRotationAxis.sqrMagnitude > 0.001f ? localRotationAxis.normalized : Vector3.right;
            float hours12 = Mathf.Repeat(_totalGameMinutes / 60f, 12f);
            float minutes60 = Mathf.Repeat(_totalGameMinutes, 60f);
            float seconds60 = Mathf.Repeat(_secondAccumulator, 60f);

            if (hourHand != null)
            {
                hourHand.localRotation = Quaternion.AngleAxis(clockwiseSign * hours12 * 30f, axis);
            }

            if (minuteHand != null)
            {
                minuteHand.localRotation = Quaternion.AngleAxis(clockwiseSign * minutes60 * 6f, axis);
            }

            if (secondHand != null)
            {
                secondHand.localRotation = Quaternion.AngleAxis(clockwiseSign * Mathf.Floor(seconds60) * 6f, axis);
            }
        }

        private void EnsureTickAudio()
        {
            if (!enableSoftTick) return;
            if (tickAudioSource == null) tickAudioSource = GetComponent<AudioSource>();
            if (tickAudioSource == null) tickAudioSource = gameObject.AddComponent<AudioSource>();

            tickAudioSource.playOnAwake = false;
            tickAudioSource.loop = false;
            tickAudioSource.spatialBlend = 0.85f;
            tickAudioSource.minDistance = 0.4f;
            tickAudioSource.maxDistance = 4.0f;

            const int sr = 44100;
            int samples = 900;
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float u = i / (float)samples;
                float env = Mathf.Exp(-u * 26f);
                float click = Mathf.Sin(u * Mathf.PI * 2f * 28f) * 0.7f + Mathf.Sin(u * Mathf.PI * 2f * 64f) * 0.3f;
                data[i] = click * env * 0.55f;
            }
            _tickClip = AudioClip.Create("Clock_SoftTick", samples, 1, sr, false);
            _tickClip.SetData(data, 0);
        }
    }
}
