using UnityEngine;

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Одно гнездо (порт) телефонного коммутатора.
    /// Хранит portId, точку посадки штекера, ссылку на подключённый штекер
    /// и управляет жёлтой аниме-обводкой при наведении.
    /// </summary>
    public sealed class SwitchboardPort : MonoBehaviour
    {
        [Header("Настройки порта")]
        [SerializeField] private int portId = 1;

        [Tooltip("Опциональная точка посадки штекера. Если не задана — рассчитывается от transform порта.")]
        [SerializeField] private Transform snapPoint;

        [Header("Авторасчёт посадки (если Snap Point не задан)")]
        [Tooltip("Локальная ось порта, направленная вглубь панели коммутатора (для модели Mayak это +X).")]
        [SerializeField] private Vector3 localInsertAxis = Vector3.right;

        [Tooltip("Вынос базовой точки штекера наружу от гнезда (в метрах).")]
        [SerializeField, Min(0f)] private float plugStandoffDistance = 0.068f;

        [Tooltip("Отступ перед гнездом, когда игрок навёл на него взятый штекер.")]
        [SerializeField, Min(0f)] private float hoverPreviewOffset = 0.042f;

        [Header("Обводка при наведении")]
        [SerializeField] private MeshOutlineHighlighter outlineHighlighter;

        public int PortId => portId;
        public Transform SnapPoint => snapPoint != null ? snapPoint : transform;
        public SwitchboardPlug CurrentPlug { get; private set; }
        public bool IsOccupied => CurrentPlug != null;

        private void Awake()
        {
            if (outlineHighlighter == null)
            {
                outlineHighlighter = GetComponent<MeshOutlineHighlighter>();
            }

            SetHighlight(false);
        }

        /// <summary>
        /// Единичный вектор направления ВГЛУБЬ панели коммутатора.
        /// </summary>
        public Vector3 GetInwardWorldDirection()
        {
            if (snapPoint != null)
            {
                return snapPoint.forward;
            }

            Vector3 dir = transform.TransformDirection(localInsertAxis);
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : -transform.forward;
        }

        /// <summary>
        /// Возвращает мировую позицию, в которую должен встать штекер при подключении.
        /// </summary>
        public Vector3 GetPlugTargetPosition()
        {
            if (snapPoint != null)
            {
                return snapPoint.position;
            }

            Vector3 inward = GetInwardWorldDirection();
            return transform.position - inward * plugStandoffDistance;
        }

        /// <summary>
        /// Позиция парения штекера прямо перед портом при наведении курсора.
        /// </summary>
        public Vector3 GetHoverPreviewPosition()
        {
            return GetPlugTargetPosition() - GetInwardWorldDirection() * hoverPreviewOffset;
        }

        /// <summary>
        /// Возвращает мировой поворот штекера при подключении.
        /// </summary>
        public Quaternion GetPlugTargetRotation()
        {
            if (snapPoint != null)
            {
                return snapPoint.rotation;
            }

            Vector3 inward = GetInwardWorldDirection();
            return Quaternion.LookRotation(inward, Vector3.up);
        }

        /// <summary>
        /// Фиксирует подключённый штекер за этим портом.
        /// </summary>
        public void AttachPlug(SwitchboardPlug plug)
        {
            CurrentPlug = plug;
            SetHighlight(false);
        }

        /// <summary>
        /// Освобождает порт от подключённого штекера.
        /// </summary>
        public void DetachPlug()
        {
            CurrentPlug = null;
        }

        /// <summary>
        /// Включает или выключает жёлтую аниме-обводку разъёма (только при наведении на свободный порт).
        /// </summary>
        public void SetHighlight(bool visible)
        {
            if (outlineHighlighter == null)
            {
                outlineHighlighter = GetComponent<MeshOutlineHighlighter>();
            }

            if (outlineHighlighter != null)
            {
                outlineHighlighter.SetOutlined(visible && !IsOccupied);
            }
        }
    }
}
