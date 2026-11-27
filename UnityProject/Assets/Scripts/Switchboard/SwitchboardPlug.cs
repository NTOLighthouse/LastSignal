using UnityEngine;

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Физический штекер коммутатора.
    /// Запоминает базовую позицию, плавно следует за курсором при взятии,
    /// перемещается в выбранный порт или возвращается на базу.
    /// </summary>
    [DefaultExecutionOrder(0)]
    public sealed class SwitchboardPlug : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("Анимация перемещения")]
        [SerializeField, Min(1f)] private float moveSpeed = 16f;
        [SerializeField, Min(1f)] private float followCursorSpeed = 24f;
        [SerializeField] private Vector3 pickUpOffset = new Vector3(0f, 0.06f, 0.025f);
        [SerializeField] private Vector3 pickUpTiltEuler = new Vector3(-22f, 0f, 0f);

        [Header("Визуальное выделение при взятии (опционально)")]
        [SerializeField] private bool highlightWhenPickedUp = true;
        [SerializeField, ColorUsage(false, true)] private Color pickUpEmission = new Color(0.35f, 0.25f, 0.1f, 1f);

        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private bool _homeCaptured;

        private Renderer _plugRenderer;
        private MaterialPropertyBlock _propertyBlock;

        public SwitchboardPort CurrentPort { get; private set; }
        public bool IsPickedUp { get; private set; }
        public Vector3 HomePosition => _homeCaptured ? _homePosition : transform.position;
        public Quaternion HomeRotation => _homeCaptured ? _homeRotation : transform.rotation;

        private void Awake()
        {
            _plugRenderer = GetComponentInChildren<Renderer>();
            _propertyBlock = new MaterialPropertyBlock();
            ForceDisablePhysics();
        }

        private void ForceDisablePhysics()
        {
            Rigidbody[] rbs = GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rbs.Length; i++)
            {
                if (rbs[i] != null)
                {
                    rbs[i].isKinematic = true;
                    rbs[i].useGravity = false;
                }
            }
        }

        private void Start()
        {
            CaptureHomePoseIfNeeded();
            _targetPosition = _homePosition;
            _targetRotation = _homeRotation;
        }

        private void Update()
        {
            if (!_homeCaptured)
            {
                return;
            }

            float speed = IsPickedUp ? followCursorSpeed : moveSpeed;
            float t = 1f - Mathf.Exp(-speed * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, _targetPosition, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, t);
        }

        /// <summary>
        /// Берёт штекер в «руку»: освобождает порт (если был вставлен), приподнимает и выделяет его.
        /// </summary>
        public void PickUp()
        {
            CaptureHomePoseIfNeeded();
            ForceDisablePhysics();

            if (CurrentPort != null)
            {
                CurrentPort.DetachPlug();
                CurrentPort = null;
            }

            IsPickedUp = true;
            _targetPosition = _homePosition + pickUpOffset;
            _targetRotation = Quaternion.Euler(pickUpTiltEuler) * _homeRotation;
            ApplyHighlight(true);
        }

        /// <summary>
        /// Обновляет целевую мировую позицию и поворот штекера, пока он следует за мышью.
        /// </summary>
        public void SetCarryTarget(Vector3 worldPosition, Quaternion worldRotation)
        {
            if (!IsPickedUp)
            {
                return;
            }

            _targetPosition = worldPosition;
            _targetRotation = worldRotation;
        }

        /// <summary>
        /// Плавно перемещает штекер в указанный порт и привязывает его к этому порту.
        /// </summary>
        public void MoveToPort(SwitchboardPort port)
        {
            if (port == null)
            {
                return;
            }

            CaptureHomePoseIfNeeded();

            if (CurrentPort != null && CurrentPort != port)
            {
                CurrentPort.DetachPlug();
            }

            CurrentPort = port;
            port.AttachPlug(this);
            IsPickedUp = false;

            _targetPosition = port.GetPlugTargetPosition();
            _targetRotation = port.GetPlugTargetRotation();
            ApplyHighlight(false);
        }

        /// <summary>
        /// Возвращает штекер на исходную позицию и освобождает порт (если был вставлен).
        /// </summary>
        public void ReturnHome()
        {
            CaptureHomePoseIfNeeded();

            if (CurrentPort != null)
            {
                CurrentPort.DetachPlug();
                CurrentPort = null;
            }

            IsPickedUp = false;
            _targetPosition = _homePosition;
            _targetRotation = _homeRotation;
            ApplyHighlight(false);
        }

        private void CaptureHomePoseIfNeeded()
        {
            if (_homeCaptured)
            {
                return;
            }

            _homePosition = transform.position;
            _homeRotation = transform.rotation;
            _homeCaptured = true;
        }

        private void ApplyHighlight(bool highlighted)
        {
            if (!highlightWhenPickedUp || _plugRenderer == null)
            {
                return;
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            _plugRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(EmissionColorId, highlighted ? pickUpEmission : Color.black);
            _plugRenderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
