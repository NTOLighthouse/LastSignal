using UnityEngine;

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Небольшая диегетическая подпись объекта при наведении прицела
    /// (наушники, фотография семьи, письмо, кружка и т.д.).
    /// </summary>
    public sealed class WorldHoverLabel : MonoBehaviour
    {
        [Tooltip("Текст подсказки, появляющейся рядом с прицелом при наведении.")]
        [SerializeField] private string hoverText = string.Empty;

        [Tooltip("Скрывать подсказку, когда камера уже в режиме фокуса (например, при чтении книги/письма или на карте).")]
        [SerializeField] private bool hideWhileCameraFocused = false;

        public string HoverText => hoverText;
        public bool HideWhileCameraFocused => hideWhileCameraFocused;

        private void OnEnable()
        {
            // Наличие OnEnable включает стандартный чекбокс активности компонента в Inspector.
        }

        public void SetText(string text)
        {
            hoverText = text ?? string.Empty;
        }
    }
}
