using UnityEngine;

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Возможные состояния индикаторной лампочки порта коммутатора.
    /// </summary>
    public enum IndicatorState
    {
        Off = 0,
        Blinking = 1,
        Solid = 2
    }

    /// <summary>
    /// Управляет одной индикаторной лампочкой на порту коммутатора.
    /// Поддерживает управление через Renderer (материал/цвет/emission), Light и/или отдельный GameObject.
    /// </summary>
    public sealed class PortIndicator : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("Идентификация порта")]
        [SerializeField] private int portId = 1;

        [Header("Визуальные ссылки (все поля опциональны)")]
        [SerializeField] private Renderer bulbRenderer;
        [SerializeField] private Light indicatorLight;
        [SerializeField] private GameObject litVisualObject;

        [Header("Материалы (опционально)")]
        [SerializeField] private Material offMaterial;
        [SerializeField] private Material onMaterial;

        [Header("Цвета и свечение")]
        [SerializeField] private Color offColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        [SerializeField] private Color onColor = new Color(1f, 0.78f, 0.35f, 1f);
        [SerializeField, ColorUsage(false, true)] private Color onEmissionColor = new Color(2.5f, 1.6f, 0.45f, 1f);
        [SerializeField, Min(0f)] private float solidLightIntensity = 1.5f;

        [Header("Настройки мигания")]
        [Tooltip("Скорость мигания (циклов в секунду).")]
        [SerializeField, Min(0.1f)] private float blinkSpeed = 2f;

        [Tooltip("Плавная пульсация вместо резкого переключения вкл/выкл.")]
        [SerializeField] private bool smoothBlink = false;

        [Header("Текущее состояние")]
        [SerializeField] private IndicatorState currentState = IndicatorState.Off;

        private MaterialPropertyBlock _propertyBlock;
        private float _blinkTimer;

        public int PortId => portId;
        public IndicatorState CurrentState => currentState;

        private void Awake()
        {
            if (bulbRenderer == null)
            {
                bulbRenderer = GetComponent<Renderer>();
            }

            if (indicatorLight == null)
            {
                indicatorLight = GetComponentInChildren<Light>();
            }

            _propertyBlock = new MaterialPropertyBlock();
            ApplyVisualIntensity(currentState == IndicatorState.Solid ? 1f : 0f);
        }

        private void Update()
        {
            if (currentState != IndicatorState.Blinking)
            {
                return;
            }

            _blinkTimer += Time.deltaTime;
            float phase = _blinkTimer * blinkSpeed * Mathf.PI * 2f;
            float wave = 0.5f - 0.5f * Mathf.Cos(phase);
            float intensity = smoothBlink ? wave : (wave >= 0.5f ? 1f : 0f);

            ApplyVisualIntensity(intensity);
        }

        /// <summary>
        /// Переключает состояние лампочки (Off / Blinking / Solid).
        /// </summary>
        public void SetState(IndicatorState state)
        {
            if (currentState == state && Application.isPlaying)
            {
                return;
            }

            currentState = state;
            _blinkTimer = 0f;

            switch (currentState)
            {
                case IndicatorState.Off:
                    ApplyVisualIntensity(0f);
                    break;
                case IndicatorState.Solid:
                    ApplyVisualIntensity(1f);
                    break;
                case IndicatorState.Blinking:
                    ApplyVisualIntensity(1f);
                    break;
            }
        }

        private void ApplyVisualIntensity(float intensity)
        {
            float clamped = Mathf.Clamp01(intensity);
            bool isLit = clamped > 0.05f;

            if (litVisualObject != null)
            {
                litVisualObject.SetActive(isLit);
            }

            if (indicatorLight != null)
            {
                indicatorLight.enabled = isLit;
                indicatorLight.intensity = solidLightIntensity * clamped;
            }

            if (bulbRenderer != null)
            {
                if (isLit && onMaterial != null && bulbRenderer.sharedMaterial != onMaterial)
                {
                    bulbRenderer.sharedMaterial = onMaterial;
                }
                else if (!isLit && offMaterial != null && bulbRenderer.sharedMaterial != offMaterial)
                {
                    bulbRenderer.sharedMaterial = offMaterial;
                }

                if (_propertyBlock == null)
                {
                    _propertyBlock = new MaterialPropertyBlock();
                }

                Color baseColor = Color.Lerp(offColor, onColor, clamped);
                Color emission = onEmissionColor * clamped;

                bulbRenderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorId, baseColor);
                _propertyBlock.SetColor(LegacyColorId, baseColor);
                _propertyBlock.SetColor(EmissionColorId, emission);
                bulbRenderer.SetPropertyBlock(_propertyBlock);
            }
        }
    }
}
