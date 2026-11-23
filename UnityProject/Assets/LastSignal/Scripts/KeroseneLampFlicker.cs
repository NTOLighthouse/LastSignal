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

            // Два слоя мягкого шума Перлина: медленное дыхание воздуха + лёгкое дрожание фитиля.
            float slowX = Mathf.PerlinNoise(_seed, t * swaySpeed) * 2f - 1f;
            float slowZ = Mathf.PerlinNoise(_seed + 37.1f, t * (swaySpeed * 0.85f)) * 2f - 1f;
            float fast = Mathf.PerlinNoise(_seed + 113.7f, t * flickerSpeed) * 2f - 1f;
            float micro = Mathf.PerlinNoise(_seed + 241.3f, t * (flickerSpeed * 2.3f)) * 2f - 1f;

            float combinedFlicker = slowX * 0.45f + fast * 0.4f + micro * 0.15f;

            if (flameVisual != null)
            {
                float rotX = (slowX * 0.75f + fast * 0.25f) * swayAngleDegrees;
                float rotZ = (slowZ * 0.75f + micro * 0.25f) * swayAngleDegrees;
                flameVisual.localRotation = _flameBaseRot * Quaternion.Euler(rotX, 0f, rotZ);

                float stretchY = 1f + combinedFlicker * scalePulseAmount;
                float squeezeXZ = 1f - combinedFlicker * (scalePulseAmount * 0.45f);
                flameVisual.localScale = new Vector3(
                    _flameBaseScale.x * squeezeXZ,
                    _flameBaseScale.y * stretchY,
                    _flameBaseScale.z * squeezeXZ);
            }

            if (innerFlameVisual != null)
            {
                float innerY = 1f + (fast * 0.6f + micro * 0.4f) * (scalePulseAmount * 0.7f);
                innerFlameVisual.localScale = new Vector3(
                    _innerBaseScale.x,
                    _innerBaseScale.y * innerY,
                    _innerBaseScale.z);
            }

            if (lampLight != null)
            {
                lampLight.intensity = baseIntensity * (1f + combinedFlicker * flickerAmplitude);
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
