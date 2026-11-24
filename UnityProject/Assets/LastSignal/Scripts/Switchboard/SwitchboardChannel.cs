using UnityEngine;

namespace LastSignal.Switchboard
{
    /// <summary>
    /// Представляет одно гнездо (линию абонента) на коммутаторе:
    /// хранит позывной, ссылку на свою лампу, точку посадки штекера и текущее подключение.
    /// </summary>
    public sealed class SwitchboardChannel : MonoBehaviour
    {
        [Header("Идентификация линии")]
        [SerializeField] private SwitchboardChannelId channelId = SwitchboardChannelId.None;
        [SerializeField] private SwitchboardLamp lamp;

        [Header("Посадка штекера в гнездо")]
        [Tooltip("Опциональный ручной якорь для позиции/поворота вставленного штекера. Если не задан — рассчитывается автоматически.")]
        [SerializeField] private Transform customPlugAnchor;

        [Tooltip("Локальная ось гнезда, направленная ВГЛУБЬ панели (для модели Mayak_FullScene это +X).")]
        [SerializeField] private Vector3 localInsertDirection = Vector3.right;

        [Tooltip("Вынос базовой точки штекера наружу от гнезда вдоль оси вставки (в метрах).")]
        [SerializeField] private float plugStandoffDistance = 0.068f;

        private SwitchboardPlug _connectedPlug;

        public SwitchboardChannelId ChannelId => channelId;
        public string DisplayName => SwitchboardChannelUtility.GetDisplayName(channelId);
        public SwitchboardLamp Lamp => lamp;
        public SwitchboardPlug ConnectedPlug => _connectedPlug;
        public bool IsOccupied => _connectedPlug != null;
        public bool IsDead => lamp != null && lamp.CurrentState == LampState.Dead;
        public LampState LampState => lamp != null ? lamp.CurrentState : LampState.Off;

        /// <summary>
        /// Настраивает канал программно (используется автопривязкой).
        /// </summary>
        public void Configure(SwitchboardChannelId id, SwitchboardLamp channelLamp)
        {
            channelId = id;
            lamp = channelLamp;
        }

        public void SetLampState(LampState state)
        {
            if (lamp != null)
            {
                lamp.SetState(state);
            }
        }

        public void ForceSetLampState(LampState state)
        {
            if (lamp != null)
            {
                lamp.ForceSetState(state);
            }
        }

        /// <summary>
        /// Мировая позиция, в которую должен встать штекер при подключении к этому гнезду.
        /// </summary>
        public Vector3 GetPlugWorldPosition()
        {
            if (customPlugAnchor != null)
            {
                return customPlugAnchor.position;
            }

            Vector3 inwardDir = GetInwardWorldDirection();
            return transform.position - inwardDir * plugStandoffDistance;
        }

        /// <summary>
        /// Мировой поворот штекера (ось +Z штекера направлена вглубь гнезда).
        /// </summary>
        public Quaternion GetPlugWorldRotation()
        {
            if (customPlugAnchor != null)
            {
                return customPlugAnchor.rotation;
            }

            Vector3 inwardDir = GetInwardWorldDirection();
            return Quaternion.LookRotation(inwardDir, Vector3.up);
        }

        internal void AttachPlug(SwitchboardPlug plug)
        {
            _connectedPlug = plug;
        }

        internal void DetachPlug()
        {
            _connectedPlug = null;
        }

        private Vector3 GetInwardWorldDirection()
        {
            Vector3 dir = transform.TransformDirection(localInsertDirection);
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : -transform.forward;
        }
    }
}
