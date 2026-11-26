using UnityEngine;
using LastSignal.SwitchboardSystem;

namespace LastSignal.StoryCore
{
    /// <summary>
    /// Связывает ручку настройки радиоприёмника (RadioTunerController) с разборчивостью голоса
    /// и уровнем эфирных помех при звонках на общей частоте (Тополь, Сорока, Скворец).
    /// Чем дальше ручка от нужной частоты — тем громче помехи и тише/глуше голос абонента.
    /// Для обычных проводных линий не вмешивается и держит помехи на нуле.
    /// </summary>
    public sealed class RadioSignalMixer : MonoBehaviour
    {
        [Header("Ссылки")]
        [SerializeField] private RadioTunerController radioTuner;
        [SerializeField] private AudioSource interferenceStaticSource;

        [Header("Параметры слышимости на расстроенной частоте")]
        [SerializeField, Range(0f, 1f)] private float minVoiceVolumeOffFreq = 0.14f;
        [SerializeField, Range(0f, 1f)] private float maxVoiceVolumeOnFreq = 0.95f;
        [SerializeField, Range(300f, 2000f)] private float minCutoffHzOffFreq = 580f;
        [SerializeField, Range(2000f, 8000f)] private float maxCutoffHzOnFreq = 4200f;
        [SerializeField, Range(0f, 0.8f)] private float maxInterferenceVolume = 0.34f;
        [SerializeField, Min(1f)] private float smoothingSpeed = 10f;

        [Header("Живое состояние (только чтение в Inspector)")]
        [SerializeField] private bool tuningActiveForCurrentCall;
        [SerializeField, Range(0f, 1f)] private float targetFrequency01 = 0.5f;
        [SerializeField, Range(0f, 1f)] private float currentClarity01 = 1f;

        private AudioSource _boundVoiceSource;
        private AudioLowPassFilter _voiceLowPass;
        private float _tolerance = 0.09f;
        private AudioClip _interferenceClip;

        public float SignalClarity01 => currentClarity01;
        public bool IsTunedIn => !tuningActiveForCurrentCall || currentClarity01 >= 0.82f;

        private void Awake()
        {
            if (radioTuner == null)
            {
                radioTuner = FindFirstObjectByType<RadioTunerController>();
            }

            EnsureInterferenceSource();
        }

        private void OnDestroy()
        {
            if (_interferenceClip != null) Destroy(_interferenceClip);
        }

        public void BindVoiceSource(AudioSource voiceSource, AudioSource ambientSource)
        {
            _boundVoiceSource = voiceSource;
            if (_boundVoiceSource != null)
            {
                _voiceLowPass = _boundVoiceSource.GetComponent<AudioLowPassFilter>();
                if (_voiceLowPass == null)
                {
                    _voiceLowPass = _boundVoiceSource.gameObject.AddComponent<AudioLowPassFilter>();
                }
                _voiceLowPass.cutoffFrequency = maxCutoffHzOnFreq;
            }
        }

        public void SetCallTuningProfile(bool requiresTuning, float targetFreq01, float tolerance)
        {
            tuningActiveForCurrentCall = requiresTuning;
            targetFrequency01 = Mathf.Clamp01(targetFreq01);
            _tolerance = Mathf.Clamp(tolerance, 0.03f, 0.35f);

            if (!tuningActiveForCurrentCall)
            {
                currentClarity01 = 1f;
                ApplyImmediate();
            }
        }

        public void ClearCallTuningProfile()
        {
            tuningActiveForCurrentCall = false;
            currentClarity01 = 1f;
            ApplyImmediate();
        }

        private void Update()
        {
            float targetClarity = 1f;
            if (tuningActiveForCurrentCall && radioTuner != null)
            {
                float dist = Mathf.Abs(radioTuner.NormalizedTuning - targetFrequency01);
                float normalizedDist = Mathf.InverseLerp(_tolerance * 0.65f, _tolerance * 3.6f, dist);
                targetClarity = 1f - (normalizedDist * normalizedDist * (3f - 2f * normalizedDist));
            }

            float k = 1f - Mathf.Exp(-smoothingSpeed * Time.unscaledDeltaTime);
            currentClarity01 = Mathf.Lerp(currentClarity01, targetClarity, k);
            ApplyImmediate();
        }

        private void ApplyImmediate()
        {
            if (_boundVoiceSource != null)
            {
                _boundVoiceSource.volume = Mathf.Lerp(minVoiceVolumeOffFreq, maxVoiceVolumeOnFreq, currentClarity01);
            }

            if (_voiceLowPass != null)
            {
                _voiceLowPass.cutoffFrequency = Mathf.Lerp(minCutoffHzOffFreq, maxCutoffHzOnFreq, currentClarity01);
            }

            if (interferenceStaticSource != null)
            {
                float noiseVol = tuningActiveForCurrentCall ? (1f - currentClarity01) * maxInterferenceVolume : 0f;
                if (noiseVol > 0.002f)
                {
                    if (!interferenceStaticSource.isPlaying)
                    {
                        interferenceStaticSource.Play();
                    }
                    interferenceStaticSource.volume = noiseVol;
                }
                else
                {
                    interferenceStaticSource.volume = 0f;
                    if (interferenceStaticSource.isPlaying)
                    {
                        interferenceStaticSource.Stop();
                    }
                }
            }
        }

        private void EnsureInterferenceSource()
        {
            if (interferenceStaticSource == null)
            {
                interferenceStaticSource = gameObject.AddComponent<AudioSource>();
            }
            interferenceStaticSource.playOnAwake = false;
            interferenceStaticSource.loop = true;
            interferenceStaticSource.volume = 0f;
            interferenceStaticSource.spatialBlend = 0f;

            const int sr = 44100;
            int count = sr * 2;
            float[] data = new float[count];
            var rng = new System.Random(1945);
            float lp = 0f, hp = 0f, prev = 0f;
            for (int i = 0; i < count; i++)
            {
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.30f * (w - lp);
                hp = 0.85f * (hp + lp - prev);
                prev = lp;
                float crackle = rng.NextDouble() < 0.0014 ? (float)(rng.NextDouble() * 2.0 - 1.0) * 0.45f : 0f;
                data[i] = Mathf.Clamp(hp * 0.65f + crackle, -0.9f, 0.9f);
            }
            _interferenceClip = AudioClip.Create("Radio_OffFreqInterference", count, 1, sr, false);
            _interferenceClip.SetData(data, 0);
            interferenceStaticSource.clip = _interferenceClip;
        }
    }
}
