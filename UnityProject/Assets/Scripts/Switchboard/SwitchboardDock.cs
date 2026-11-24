using UnityEngine;

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Место хранения (док) штекера на панели коммутатора.
    /// Позволяет кликом вернуть взятый штекер на его обычную базу и подсвечивается аниме-обводкой при наведении.
    /// </summary>
    public sealed class SwitchboardDock : MonoBehaviour
    {
        [Header("Привязка (опционально)")]
        [Tooltip("Если указан конкретный штекер — док принимает его; если пусто — принимает любой активный штекер.")]
        [SerializeField] private SwitchboardPlug assignedPlug;

        [Header("Предпросмотр при наведении")]
        [SerializeField] private Vector3 hoverPreviewOffset = new Vector3(0f, 0.11f, 0f);

        [Header("Обводка при наведении")]
        [SerializeField] private MeshOutlineHighlighter outlineHighlighter;

        public SwitchboardPlug AssignedPlug => assignedPlug;

        private void Awake()
        {
            if (outlineHighlighter == null)
            {
                outlineHighlighter = GetComponent<MeshOutlineHighlighter>();
            }

            SetHighlight(false);
        }

        /// <summary>
        /// Проверяет, подходит ли этот док для возврата указанного штекера.
        /// </summary>
        public bool CanAcceptPlug(SwitchboardPlug plug)
        {
            if (plug == null)
            {
                return false;
            }

            return assignedPlug == null || assignedPlug == plug;
        }

        /// <summary>
        /// Позиция парения штекера над доком при наведении.
        /// </summary>
        public Vector3 GetHoverPreviewPosition()
        {
            return transform.position + hoverPreviewOffset;
        }

        /// <summary>
        /// Включает или выключает жёлтую аниме-обводку дока (только при наведении).
        /// </summary>
        public void SetHighlight(bool visible)
        {
            if (outlineHighlighter == null)
            {
                outlineHighlighter = GetComponent<MeshOutlineHighlighter>();
            }

            if (outlineHighlighter != null)
            {
                outlineHighlighter.SetOutlined(visible);
            }
        }
    }
}
