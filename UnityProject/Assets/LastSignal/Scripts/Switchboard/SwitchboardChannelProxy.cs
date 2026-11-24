using UnityEngine;

namespace LastSignal.Switchboard
{
    /// <summary>
    /// Прокси-компонент для лампы или таблички с позывным, чтобы клик/наведение
    /// по мигающей лампочке или подписи тоже срабатывал на соответствующий канал коммутатора.
    /// </summary>
    public sealed class SwitchboardChannelProxy : MonoBehaviour
    {
        [SerializeField] private SwitchboardChannel targetChannel;

        public SwitchboardChannel TargetChannel => targetChannel;

        public void Configure(SwitchboardChannel channel)
        {
            targetChannel = channel;
        }
    }
}
