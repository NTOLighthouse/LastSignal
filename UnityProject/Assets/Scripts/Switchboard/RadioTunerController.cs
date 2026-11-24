using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Управляет ручкой настройки радиоприёмника:
    /// - при удержании ЛКМ на ручке фиксирует камеру на месте (люфт ~2° без приближения);
    /// - движение мыши влево-вправо плавно вращает ручку и двигает стрелку по шкале частот;
    /// - воспроизводит шум эфира СТРОГО в момент вращения ручки (в остальной игре тишина).
    /// </summary>
    public sealed class RadioTunerController : MonoBehaviour
    {
        [Header("Ссылки")]
        [SerializeField] private SmoothMouseLookCamera cameraLook;
        [SerializeField] private Camera interactionCamera;
        [SerializeField] private SwitchboardInteractor switchboardInteractor;

        [Header("Элементы радио")]
        [SerializeField] private Transform tuningKnob;
        [SerializeField] private Transform dialNeedle;

        [Header("Движение ручки и стрелки")]
        [SerializeField] private Vector3 knobRotationAxis = Vector3.right;
        [SerializeField] private float knobTotalRotationDegrees = 540f;
        [SerializeField] private bool invertKnobRotation = true;
        [SerializeField] private Vector3 needleSlideLocalAxis = Vector3.up;
        [SerializeField] private float needleMinOffset = -0.072f;
        [SerializeField] private float needleMaxOffset = 0.072f;

        [Header("Параметры настройки частоты")]
        [SerializeField, Range(0f, 1f)] private float initialTuning = 0.5f;
        [SerializeField, Min(0.0001f)] private float mouseDragSensitivity = 0.0018f;
        [SerializeField, Min(1f)] private float visualSmoothSpeed = 18f;
        [SerializeField] private float minFrequencyMHz = 3.0f;
        [SerializeField] private float maxFrequencyMHz = 12.0f;

        [Header("Фиксация камеры на месте при вращении")]
        [SerializeField, Range(0f, 5f)] private float holdMaxHorizontalAngle = 2f;
        [SerializeField, Range(0f, 5f)] private float holdMaxVerticalAngle = 1.5f;

        [Header("Звук радиопомех (только при вращении)")]
        [SerializeField] private AudioSource staticAudioSource;
        [SerializeField] private AudioClip customStaticClip;
        [SerializeField, Range(0f, 1f)] private float maxStaticVolume = 0.34f;
        [SerializeField, Min(0.05f)] private float staticFadeInSeconds = 0.24f;
        [SerializeField, Min(0.05f)] private float staticFadeOutSeconds = 0.38f;
        [SerializeField, Min(0.05f)] private float turnHoldGraceSeconds = 0.16f;

        [Header("Рейкаст")]
        [SerializeField] private LayerMask raycastMask = ~0;
        [SerializeField, Min(0.5f)] private float maxRayDistance = 15f;

        private Quaternion _knobBaseLocalRotation;
        private Vector3 _needleCenterLocalPosition;
        private bool _posesCaptured;

        private AudioClip _proceduralStaticClip;
        private bool _isHoldingKnob;
        private Vector2 _lastMousePosition;
        private float _targetTuning;
        private float _displayTuning;
        private float _turnActivityTimer;
        private float _turnSpeedNormalized;
        private float _staticEnvelope;
        private float _currentStaticVolume;

        public float NormalizedTuning => _targetTuning;
        public float CurrentFrequencyMHz => Mathf.Lerp(minFrequencyMHz, maxFrequencyMHz, _targetTuning);
        public bool IsHoldingKnob => _isHoldingKnob;

        /// <summary>Срабатывает при изменении настройки (значение 0..1).</summary>
        public event Action<float> OnTuningChanged;

        /// <summary>Срабатывает при изменении частоты (в МГц).</summary>
        public event Action<float> OnFrequencyChanged;

        private void Awake()
        {
            if (interactionCamera == null)
            {
                interactionCamera = Camera.main;
            }

            if (cameraLook == null && interactionCamera != null)
            {
                cameraLook = interactionCamera.GetComponent<SmoothMouseLookCamera>();
            }

            CaptureInitialPoses();
            EnsureStaticAudioSource();

            _targetTuning = Mathf.Clamp01(initialTuning);
            _displayTuning = _targetTuning;
            ApplyVisualsImmediate(_displayTuning);
        }

        private void OnDisable()
        {
            if (_isHoldingKnob)
            {
                EndKnobHold();
            }

            StopStaticImmediately();
        }

        private void OnDestroy()
        {
            if (_proceduralStaticClip != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_proceduralStaticClip);
                }
                else
                {
                    DestroyImmediate(_proceduralStaticClip);
                }
                _proceduralStaticClip = null;
            }
        }

        private void Update()
        {
            HandleInput();
            UpdateVisuals();
            UpdateStaticAudio();
        }

        private void HandleInput()
        {
            // Не перехватываем клики, если игрок держит штекер коммутатора.
            if (switchboardInteractor != null && switchboardInteractor.ActivePlug != null)
            {
                if (_isHoldingKnob)
                {
                    EndKnobHold();
                }
                return;
            }

            if (!_isHoldingKnob)
            {
                if (WasLeftMousePressed(out Vector2 mousePos) && IsHitOnTuningKnob(mousePos))
                {
                    BeginKnobHold(mousePos);
                }
                return;
            }

            // Если ЛКМ отпущена — отпускаем ручку и возвращаем свободный обзор.
            if (!IsLeftMouseHeld(out Vector2 currentMousePos))
            {
                EndKnobHold();
                return;
            }

            float deltaX = currentMousePos.x - _lastMousePosition.x;
            _lastMousePosition = currentMousePos;

            if (Mathf.Abs(deltaX) > 0.15f)
            {
                float previous = _targetTuning;
                _targetTuning = Mathf.Clamp01(_targetTuning + deltaX * mouseDragSensitivity);

                // Отмечаем активное вращение ручки и его скорость для плавного эфирного звука.
                _turnActivityTimer = turnHoldGraceSeconds;
                float instantSpeed = Mathf.Clamp01(Mathf.Abs(deltaX) / 22f);
                _turnSpeedNormalized = Mathf.Lerp(_turnSpeedNormalized, instantSpeed, 0.35f);

                if (!Mathf.Approximately(previous, _targetTuning))
                {
                    OnTuningChanged?.Invoke(_targetTuning);
                    OnFrequencyChanged?.Invoke(CurrentFrequencyMHz);
                }
            }
            else
            {
                _turnSpeedNormalized = Mathf.MoveTowards(_turnSpeedNormalized, 0f, Time.unscaledDeltaTime * 4f);
            }
        }

        private void BeginKnobHold(Vector2 mousePos)
        {
            _isHoldingKnob = true;
            _lastMousePosition = mousePos;
            _turnActivityTimer = 0f;

            if (cameraLook != null)
            {
                cameraLook.BeginLookHold(holdMaxHorizontalAngle, holdMaxVerticalAngle, owner: this);
            }
        }

        private void EndKnobHold()
        {
            _isHoldingKnob = false;
            _turnActivityTimer = 0f;

            if (cameraLook != null)
            {
                cameraLook.EndLookHold(owner: this);
            }
        }

        private void UpdateVisuals()
        {
            if (!_posesCaptured)
            {
                return;
            }

            float t = 1f - Mathf.Exp(-visualSmoothSpeed * Time.unscaledDeltaTime);
            _displayTuning = Mathf.Lerp(_displayTuning, _targetTuning, t);
            ApplyVisualsImmediate(_displayTuning);
        }

        private void ApplyVisualsImmediate(float normalized)
        {
            if (tuningKnob != null)
            {
                float sign = invertKnobRotation ? -1f : 1f;
                float angle = sign * (normalized - 0.5f) * knobTotalRotationDegrees;
                tuningKnob.localRotation = _knobBaseLocalRotation * Quaternion.AngleAxis(angle, knobRotationAxis.normalized);
            }

            if (dialNeedle != null)
            {
                float slide = Mathf.Lerp(needleMinOffset, needleMaxOffset, normalized);
                dialNeedle.localPosition = _needleCenterLocalPosition + needleSlideLocalAxis.normalized * slide;
            }
        }

        private void UpdateStaticAudio()
        {
            float dt = Time.unscaledDeltaTime;
            if (_turnActivityTimer > 0f)
            {
                _turnActivityTimer -= dt;
            }
            else
            {
                _turnSpeedNormalized = Mathf.MoveTowards(_turnSpeedNormalized, 0f, dt * 4f);
            }

            bool shouldPlayNoise = _isHoldingKnob && _turnActivityTimer > 0f;
            float targetEnv = shouldPlayNoise ? 1f : 0f;
            float fadeDuration = shouldPlayNoise ? staticFadeInSeconds : staticFadeOutSeconds;

            _staticEnvelope = Mathf.MoveTowards(_staticEnvelope, targetEnv, dt / Mathf.Max(0.02f, fadeDuration));

            // Мягкая S-кривая для максимально плавного, незаметного возникновения и затухания шума.
            float smoothEnv = _staticEnvelope * _staticEnvelope * (3f - 2f * _staticEnvelope);
            _currentStaticVolume = smoothEnv * maxStaticVolume;

            if (staticAudioSource == null)
            {
                return;
            }

            if (_currentStaticVolume > 0.0005f)
            {
                if (!staticAudioSource.isPlaying)
                {
                    EnsureStaticClipAssigned();
                    staticAudioSource.Play();
                }

                staticAudioSource.volume = _currentStaticVolume;
                // Смещение тембра по шкале + лёгкий подъём тона при быстрой прокрутке (эффект гетеродина).
                float basePitch = Mathf.Lerp(0.92f, 1.24f, _displayTuning);
                staticAudioSource.pitch = basePitch + _turnSpeedNormalized * 0.14f;
            }
            else
            {
                _currentStaticVolume = 0f;
                staticAudioSource.volume = 0f;
                if (staticAudioSource.isPlaying)
                {
                    staticAudioSource.Stop();
                }
            }
        }

        private void StopStaticImmediately()
        {
            _staticEnvelope = 0f;
            _currentStaticVolume = 0f;
            _turnActivityTimer = 0f;
            _turnSpeedNormalized = 0f;
            if (staticAudioSource != null)
            {
                staticAudioSource.volume = 0f;
                staticAudioSource.Stop();
            }
        }

        private void CaptureInitialPoses()
        {
            if (_posesCaptured)
            {
                return;
            }

            if (tuningKnob != null)
            {
                _knobBaseLocalRotation = tuningKnob.localRotation;
            }

            if (dialNeedle != null)
            {
                _needleCenterLocalPosition = dialNeedle.localPosition;
            }

            _posesCaptured = true;
        }

        private void EnsureStaticAudioSource()
        {
            if (staticAudioSource == null)
            {
                staticAudioSource = GetComponent<AudioSource>();
            }

            if (staticAudioSource == null)
            {
                staticAudioSource = gameObject.AddComponent<AudioSource>();
            }

            staticAudioSource.playOnAwake = false;
            staticAudioSource.loop = true;
            staticAudioSource.volume = 0f;
            staticAudioSource.spatialBlend = 0.5f;
            EnsureStaticClipAssigned();
        }

        private void EnsureStaticClipAssigned()
        {
            if (staticAudioSource == null)
            {
                return;
            }

            if (customStaticClip != null)
            {
                if (staticAudioSource.clip != customStaticClip)
                {
                    staticAudioSource.clip = customStaticClip;
                }
                return;
            }

            if (_proceduralStaticClip == null)
            {
                _proceduralStaticClip = CreateRadioStaticClip();
            }

            if (staticAudioSource.clip != _proceduralStaticClip)
            {
                staticAudioSource.clip = _proceduralStaticClip;
            }
        }

        /// <summary>
        /// Генерирует зацикленный звук аналогового КВ-радиоприёмника при прокрутке:
        /// яркое высокочастотное шипение эфира + тонкие скользящие гетеродинные свисты + сухой треск.
        /// </summary>
        private static AudioClip CreateRadioStaticClip()
        {
            const int sampleRate = 44100;
            const float duration = 3.0f;
            int sampleCount = Mathf.RoundToInt(sampleRate * duration);
            float[] data = new float[sampleCount];

            var rng = new System.Random(1943);
            float lpMid = 0f;
            float hpMid = 0f;
            float prevMid = 0f;
            float hpHigh = 0f;
            float prevWhite = 0f;
            float whistlePhase1 = 0f;
            float whistlePhase2 = 0f;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);

                // 1. Среднечастотная основа эфирного шума (~600–3500 Гц).
                lpMid += 0.36f * (white - lpMid);
                hpMid = 0.88f * (hpMid + lpMid - prevMid);
                prevMid = lpMid;

                // 2. Яркий высокочастотный «песок» и воздух аналогового радио (3–9 кГц).
                hpHigh = 0.62f * (hpHigh + white - prevWhite);
                prevWhite = white;

                // 3. Характерные высокие гетеродинные свисты («уии-пиу») при прохождении мимо несущих волн.
                float sweepFreq1 = 2400f + 1350f * Mathf.Sin(t * Mathf.PI * 2f * 0.85f + 0.4f);
                float sweepEnv1 = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 2f * 1.15f)), 4f);
                whistlePhase1 += (2f * Mathf.PI * sweepFreq1) / sampleRate;
                float whistle1 = Mathf.Sin(whistlePhase1) * sweepEnv1 * 0.14f;

                float sweepFreq2 = 3900f + 1600f * Mathf.Cos(t * Mathf.PI * 2f * 1.35f);
                float sweepEnv2 = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 2f * 1.7f + 1.9f)), 6f);
                whistlePhase2 += (2f * Mathf.PI * sweepFreq2) / sampleRate;
                float whistle2 = Mathf.Sin(whistlePhase2) * sweepEnv2 * 0.09f;

                // 4. Сухой высокочастотный треск статика.
                float crackle = rng.NextDouble() < 0.0018 ? (float)(rng.NextDouble() * 2.0 - 1.0) * 0.52f : 0f;

                float sample = hpMid * 0.48f + hpHigh * 0.34f + whistle1 + whistle2 + crackle;
                data[i] = Mathf.Clamp(sample, -0.95f, 0.95f);
            }

            // Плавный кроссфейд на стыке петли, чтобы не было щелчка при зацикливании.
            int fadeSamples = 1024;
            for (int i = 0; i < fadeSamples; i++)
            {
                float w = i / (float)fadeSamples;
                data[i] = Mathf.Lerp(data[sampleCount - fadeSamples + i], data[i], w);
            }

            AudioClip clip = AudioClip.Create("Radio_ProceduralStatic", sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private bool IsHitOnTuningKnob(Vector2 mousePos)
        {
            if (tuningKnob == null)
            {
                return false;
            }

            Camera cam = interactionCamera != null ? interactionCamera : Camera.main;
            if (cam == null)
            {
                return false;
            }

            Ray ray = cam.ScreenPointToRay(mousePos);
            if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, raycastMask, QueryTriggerInteraction.Collide))
            {
                return false;
            }

            return hit.transform == tuningKnob || hit.transform.IsChildOf(tuningKnob);
        }

        private static bool WasLeftMousePressed(out Vector2 mousePosition)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                mousePosition = mouse.position.ReadValue();
                return true;
            }

            mousePosition = Vector2.zero;
            return false;
#else
            if (Input.GetMouseButtonDown(0))
            {
                mousePosition = Input.mousePosition;
                return true;
            }

            mousePosition = Vector2.zero;
            return false;
#endif
        }

        private static bool IsLeftMouseHeld(out Vector2 mousePosition)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                mousePosition = mouse.position.ReadValue();
                return true;
            }

            mousePosition = Vector2.zero;
            return false;
#else
            if (Input.GetMouseButton(0))
            {
                mousePosition = Input.mousePosition;
                return true;
            }

            mousePosition = Vector2.zero;
            return false;
#endif
        }
    }
}
