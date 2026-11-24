using UnityEngine;

namespace LastSignal.Switchboard
{
    /// <summary>
    /// Управляет визуальным свечением и миганием одной лампочки коммутатора.
    /// Использует MaterialPropertyBlock, чтобы не создавать копии материалов в рантайме.
    /// </summary>
    public sealed class SwitchboardLamp : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("Ссылки на рендер и материалы")]
        [SerializeField] private Renderer bulbRenderer;
        [SerializeField] private Material offMaterial;
        [SerializeField] private Material litMaterial;

        [Header("Цвета свечения (HDR Emission)")]
        [SerializeField, ColorUsage(false, true)] private Color incomingEmission = new Color(3.2f, 1.85f, 0.45f, 1f);
        [SerializeField, ColorUsage(false, true)] private Color urgentEmission = new Color(4.2f, 0.65f, 0.25f, 1f);
        [SerializeField, ColorUsage(false, true)] private Color connectedEmission = new Color(2.9f, 2.0f, 0.72f, 1f);
        [SerializeField, ColorUsage(false, true)] private Color waitingTargetEmission = new Color(1.7f, 1.25f, 0.45f, 1f);

        [Header("Базовые цвета стекла при свечении")]
        [SerializeField] private Color litBaseColor = new Color(1f, 0.83f, 0.48f, 1f);
        [SerializeField] private Color urgentBaseColor = new Color(1f, 0.45f, 0.30f, 1f);
        [SerializeField] private Color dimGlassColor = new Color(0.24f, 0.18f, 0.11f, 1f);

        [Header("Частота мигания (циклов в секунду)")]
        [SerializeField, Min(0.1f)] private float incomingBlinkHz = 1.6f;
        [SerializeField, Min(0.1f)] private float urgentBlinkHz = 3.8f;
        [SerializeField, Min(0.1f)] private float waitingPulseHz = 0.9f;

        [Header("Текущее состояние")]
        [SerializeField] private LampState currentState = LampState.Off;

        private MaterialPropertyBlock _propertyBlock;
        private float _stateTimer;
        private bool _usingLitMaterial;

        public LampState CurrentState => currentState;

        private void Awake()
        {
            if (bulbRenderer == null)
            {
                bulbRenderer = GetComponent<Renderer>();
            }

            _propertyBlock = new MaterialPropertyBlock();
            ApplyVisualStateImmediate();
        }

        private void Update()
        {
            if (bulbRenderer == null)
            {
                return;
            }

            switch (currentState)
            {
                case LampState.Incoming:
                {
                    _stateTimer += Time.deltaTime;
                    // Выразительное ламповое мигание с плавным накалом нити.
                    float wave = 0.5f - 0.5f * Mathf.Cos(_stateTimer * incomingBlinkHz * Mathf.PI * 2f);
                    float intensity = Mathf.Pow(wave, 0.7f);
                    ApplyLitProperties(
                        Color.Lerp(dimGlassColor, litBaseColor, intensity),
                        incomingEmission * intensity);
                    break;
                }
                case LampState.Urgent:
                {
                    _stateTimer += Time.deltaTime;
                    float wave = 0.5f - 0.5f * Mathf.Cos(_stateTimer * urgentBlinkHz * Mathf.PI * 2f);
                    float intensity = wave > 0.25f ? 1f : 0.08f;
                    ApplyLitProperties(
                        Color.Lerp(dimGlassColor, urgentBaseColor, intensity),
                        urgentEmission * intensity);
                    break;
                }
                case LampState.WaitingTarget:
                {
                    _stateTimer += Time.deltaTime;
                    float wave = 0.35f + 0.65f * (0.5f - 0.5f * Mathf.Cos(_stateTimer * waitingPulseHz * Mathf.PI * 2f));
                    ApplyLitProperties(
                        Color.Lerp(dimGlassColor, litBaseColor, wave),
                        waitingTargetEmission * wave);
                    break;
                }
            }
        }

        /// <summary>
        /// Задаёт новый режим работы лампы. Если лампа в состоянии Dead,
        /// обычные команды её не включают (нужен явный сброс через ForceSetState).
        /// </summary>
        public void SetState(LampState newState)
        {
            if (currentState == LampState.Dead && newState != LampState.Dead)
            {
                return;
            }

            if (currentState == newState)
            {
                return;
            }

            currentState = newState;
            _stateTimer = 0f;
            ApplyVisualStateImmediate();
        }

        /// <summary>
        /// Принудительно сбрасывает состояние лампы (включая выход из Dead при перезапуске смены).
        /// </summary>
        public void ForceSetState(LampState newState)
        {
            currentState = newState;
            _stateTimer = 0f;
            ApplyVisualStateImmediate();
        }

        /// <summary>
        /// Программная привязка ссылок (используется автонастройкой сцены).
        /// </summary>
        public void Configure(Renderer rendererComponent, Material offMat, Material litMat)
        {
            bulbRenderer = rendererComponent;
            if (offMat != null) offMaterial = offMat;
            if (litMat != null) litMaterial = litMat;

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            ApplyVisualStateImmediate();
        }

        private void ApplyVisualStateImmediate()
        {
            if (bulbRenderer == null)
            {
                return;
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            switch (currentState)
            {
                case LampState.Off:
                case LampState.Dead:
                    SwitchMaterial(useLit: false);
                    bulbRenderer.SetPropertyBlock(null);
                    break;

                case LampState.Connected:
                    SwitchMaterial(useLit: true);
                    ApplyLitProperties(litBaseColor, connectedEmission);
                    break;

                case LampState.Incoming:
                    SwitchMaterial(useLit: true);
                    ApplyLitProperties(litBaseColor, incomingEmission);
                    break;

                case LampState.Urgent:
                    SwitchMaterial(useLit: true);
                    ApplyLitProperties(urgentBaseColor, urgentEmission);
                    break;

                case LampState.WaitingTarget:
                    SwitchMaterial(useLit: true);
                    ApplyLitProperties(litBaseColor, waitingTargetEmission);
                    break;
            }
        }

        private void SwitchMaterial(bool useLit)
        {
            if (useLit)
            {
                if (litMaterial != null && (!_usingLitMaterial || bulbRenderer.sharedMaterial != litMaterial))
                {
                    bulbRenderer.sharedMaterial = litMaterial;
                }
                _usingLitMaterial = true;
            }
            else
            {
                if (offMaterial != null && (_usingLitMaterial || bulbRenderer.sharedMaterial != offMaterial))
                {
                    bulbRenderer.sharedMaterial = offMaterial;
                }
                _usingLitMaterial = false;
            }
        }

        private void ApplyLitProperties(Color baseColor, Color emissionColor)
        {
            bulbRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, baseColor);
            _propertyBlock.SetColor(EmissionColorId, emissionColor);
            bulbRenderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
