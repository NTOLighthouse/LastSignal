using UnityEngine;

namespace LastSignal.Switchboard
{
    /// <summary>
    /// Физический штекер коммутатора (один из двух коннекторов).
    /// Отвечает за своё состояние (в доке / в руке / вставлен) и плавную дуговую анимацию перемещения.
    /// </summary>
    public sealed class SwitchboardPlug : MonoBehaviour
    {
        [Header("Параметры штекера")]
        [SerializeField] private int plugIndex = 1;
        [SerializeField] private Transform dockReference;

        [Header("Анимация перемещения")]
        [SerializeField, Min(1f)] private float moveSpeed = 16f;
        [SerializeField, Min(0f)] private float heldLiftHeight = 0.055f;
        [SerializeField, Range(-35f, 35f)] private float heldTiltAngle = -18f;
        [SerializeField, Min(0f)] private float transitArcClearance = 0.055f;

        private Vector3 _dockWorldPos;
        private Quaternion _dockWorldRot;
        private Vector3 _startAnimPos;
        private Vector3 _targetWorldPos;
        private Quaternion _targetWorldRot;
        private float _transitProgress = 1f;
        private bool _hasPreviewOverride;
        private Vector3 _previewWorldPos;
        private Quaternion _previewWorldRot;
        private bool _dockPoseCaptured;

        public int PlugIndex => plugIndex;
        public Transform DockReference => dockReference;
        public PlugState State { get; private set; } = PlugState.Docked;
        public SwitchboardChannel ConnectedChannel { get; private set; }
        public bool IsFree => State != PlugState.Plugged;

        private void Awake()
        {
            CaptureDockPoseIfNeeded();
            _startAnimPos = _dockWorldPos;
            _targetWorldPos = _dockWorldPos;
            _targetWorldRot = _dockWorldRot;
            _transitProgress = 1f;
        }

        private void Update()
        {
            if (!_dockPoseCaptured)
            {
                return;
            }

            if (State == PlugState.Held && _hasPreviewOverride)
            {
                float followT = 1f - Mathf.Exp(-moveSpeed * 1.35f * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, _previewWorldPos, followT);
                transform.rotation = Quaternion.Slerp(transform.rotation, _previewWorldRot, followT);
                return;
            }

            if (_transitProgress < 1f)
            {
                _transitProgress = Mathf.MoveTowards(_transitProgress, 1f, Time.deltaTime * 4.5f);
                float smooth = Mathf.SmoothStep(0f, 1f, _transitProgress);
                float arc = Mathf.Sin(smooth * Mathf.PI) * transitArcClearance;
                Vector3 basePos = Vector3.Lerp(_startAnimPos, _targetWorldPos, smooth);
                transform.position = basePos + new Vector3(0f, arc * 0.35f, arc);
            }
            else
            {
                float t = 1f - Mathf.Exp(-moveSpeed * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, _targetWorldPos, t);
            }

            float rotT = 1f - Mathf.Exp(-moveSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetWorldRot, rotT);
        }

        public void Configure(int index, Transform dock)
        {
            plugIndex = index;
            dockReference = dock;
        }

        public void MoveToDock()
        {
            CaptureDockPoseIfNeeded();
            State = PlugState.Docked;
            ConnectedChannel = null;
            _hasPreviewOverride = false;
            BeginTransitTo(_dockWorldPos, _dockWorldRot);
        }

        public void MoveToHeld()
        {
            CaptureDockPoseIfNeeded();
            State = PlugState.Held;
            ConnectedChannel = null;
            _hasPreviewOverride = false;
            Vector3 heldPos = _dockWorldPos + new Vector3(0f, heldLiftHeight, 0.02f);
            Quaternion heldRot = Quaternion.AngleAxis(heldTiltAngle, Vector3.right) * _dockWorldRot;
            BeginTransitTo(heldPos, heldRot);
        }

        public void SetHeldPreviewPose(Vector3 worldPos, Quaternion worldRot)
        {
            if (State != PlugState.Held)
            {
                return;
            }

            _hasPreviewOverride = true;
            _previewWorldPos = worldPos;
            _previewWorldRot = worldRot;
        }

        public void ClearHeldPreviewPose()
        {
            _hasPreviewOverride = false;
        }

        public void MoveToChannel(SwitchboardChannel channel)
        {
            CaptureDockPoseIfNeeded();
            State = PlugState.Plugged;
            ConnectedChannel = channel;
            _hasPreviewOverride = false;
            BeginTransitTo(channel.GetPlugWorldPosition(), channel.GetPlugWorldRotation());
        }

        private void BeginTransitTo(Vector3 targetPos, Quaternion targetRot)
        {
            _startAnimPos = transform.position;
            _targetWorldPos = targetPos;
            _targetWorldRot = targetRot;
            float distance = Vector3.Distance(_startAnimPos, _targetWorldPos);
            _transitProgress = distance > 0.025f ? 0f : 1f;
        }

        private void CaptureDockPoseIfNeeded()
        {
            if (_dockPoseCaptured)
            {
                return;
            }

            _dockWorldPos = transform.position;
            _dockWorldRot = transform.rotation;
            _dockPoseCaptured = true;
        }
    }
}
