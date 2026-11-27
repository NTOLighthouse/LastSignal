using UnityEngine;

namespace LastSignal
{
    /// <summary>
    /// Живое плавное покачивание пламени керосиновой лампы и мягкое мерцание её света.
    /// </summary>
    public sealed class KeroseneLampFlicker : MonoBehaviour
    {
        [Header("Ссылки")]
        [SerializeField] private Light lampLight;
        [SerializeField] private Transform flameVisual;
        [SerializeField] private Transform innerFlameVisual;

        [Header("Покачивание пламени")]
        [SerializeField] private float swaySpeed = 1.65f;
        [SerializeField] private float swayAngleDegrees = 6.5f;
        [SerializeField] private float scalePulseAmount = 0.07f;

        [Header("Мерцание света")]
        [SerializeField] private float baseIntensity = 3.8f;
        [SerializeField, Range(0f, 0.35f)] private float flickerAmplitude = 0.09f;
        [SerializeField] private float flickerSpeed = 2.8f;
        [SerializeField] private float lightPositionJitter = 0.004f;

        private Quaternion _flameBaseRot;
        private Vector3 _flameBaseScale;
        private Vector3 _innerBaseScale;
        private Vector3 _lightBaseLocalPos;
        private float _seed;
        private float _intensityMultiplier = 1f;
        private float _shudderStrength;
        private float _shudderPhase;

        /// <summary>
        /// Внешний множитель яркости лампы (0 = потушена, 1 = нормальное горение).
        /// Используется при зажигании спичкой в начале Ночи 1 и аварийных режимах.
        /// </summary>
        public float IntensityMultiplier
        {
            get => _intensityMultiplier;
            set => _intensityMultiplier = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Короткое вздрагивание пламени керосиновой лампы от далёкого или близкого разрыва.
        /// </summary>
        public void TriggerFlameShudder(float strength = 1f)
        {
            _shudderStrength = Mathf.Clamp(Mathf.Max(_shudderStrength, strength), 0f, 2f);
            _shudderPhase = 0f;
        }

        private void Awake()
        {
            _seed = Random.Range(10f, 999f);

            if (flameVisual != null)
            {
                _flameBaseRot = flameVisual.localRotation;
                _flameBaseScale = flameVisual.localScale;
            }

            if (innerFlameVisual != null)
            {
                _innerBaseScale = innerFlameVisual.localScale;
            }

            if (lampLight != null)
            {
                _lightBaseLocalPos = lampLight.transform.localPosition;
                if (baseIntensity <= 0.001f)
                {
                    baseIntensity = lampLight.intensity;
                }
            }
        }

        private void Update()
        {
            float t = Time.time;

            if (_shudderStrength > 0.001f)
            {
                _shudderPhase += Time.unscaledDeltaTime * 28f;
                _shudderStrength = Mathf.MoveTowards(_shudderStrength, 0f, Time.unscaledDeltaTime * 1.85f);
            }

            float shudderWave = _shudderStrength > 0.001f ? Mathf.Sin(_shudderPhase) * _shudderStrength : 0f;

            // Два слоя мягкого шума Перлина: медленное дыхание воздуха + лёгкое дрожание фитиля.
            float slowX = Mathf.PerlinNoise(_seed, t * swaySpeed) * 2f - 1f;
            float slowZ = Mathf.PerlinNoise(_seed + 37.1f, t * (swaySpeed * 0.85f)) * 2f - 1f;
            float fast = Mathf.PerlinNoise(_seed + 113.7f, t * flickerSpeed) * 2f - 1f;
            float micro = Mathf.PerlinNoise(_seed + 241.3f, t * (flickerSpeed * 2.3f)) * 2f - 1f;

            float combinedFlicker = slowX * 0.45f + fast * 0.4f + micro * 0.15f;
            float flameScaleMult = Mathf.Lerp(0.01f, 1f, _intensityMultiplier);

            if (flameVisual != null)
            {
                float rotX = (slowX * 0.75f + fast * 0.25f) * swayAngleDegrees + shudderWave * 14f;
                float rotZ = (slowZ * 0.75f + micro * 0.25f) * swayAngleDegrees + shudderWave * 9f;
                flameVisual.localRotation = _flameBaseRot * Quaternion.Euler(rotX, 0f, rotZ);

                float stretchY = (1f + combinedFlicker * scalePulseAmount - Mathf.Abs(shudderWave) * 0.16f) * flameScaleMult;
                float squeezeXZ = (1f - combinedFlicker * (scalePulseAmount * 0.45f) + Mathf.Abs(shudderWave) * 0.10f) * flameScaleMult;
                flameVisual.localScale = new Vector3(
                    _flameBaseScale.x * squeezeXZ,
                    _flameBaseScale.y * stretchY,
                    _flameBaseScale.z * squeezeXZ);
            }

            if (innerFlameVisual != null)
            {
                float innerY = (1f + (fast * 0.6f + micro * 0.4f) * (scalePulseAmount * 0.7f)) * flameScaleMult;
                innerFlameVisual.localScale = new Vector3(
                    _innerBaseScale.x * flameScaleMult,
                    _innerBaseScale.y * innerY,
                    _innerBaseScale.z * flameScaleMult);
            }

            if (lampLight != null)
            {
                float shudderLight = 1f + shudderWave * 0.22f;
                lampLight.intensity = Mathf.Max(0f, baseIntensity * _intensityMultiplier * (1f + combinedFlicker * flickerAmplitude) * shudderLight);
                lampLight.transform.localPosition = _lightBaseLocalPos + new Vector3(
                    slowZ * lightPositionJitter,
                    combinedFlicker * (lightPositionJitter * 0.7f),
                    slowX * lightPositionJitter);
            }
        }

        public void Configure(Light lightSource, Transform outerFlame, Transform innerFlame, float intensity)
        {
            lampLight = lightSource;
            flameVisual = outerFlame;
            innerFlameVisual = innerFlame;
            baseIntensity = intensity;
        }
    }
}
