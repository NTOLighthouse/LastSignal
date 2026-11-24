using UnityEngine;

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Конфигурация одного сценарного блока коммутатора (например, «Блок 1 — Берёза»).
    /// Создаётся через ПКМ в Project -> Create -> Switchboard -> Block Data.
    /// </summary>
    [CreateAssetMenu(fileName = "SwitchboardBlock_01", menuName = "Switchboard/Block Data", order = 1)]
    public sealed class SwitchboardBlockData : ScriptableObject
    {
        [Header("Основная информация")]
        [SerializeField] private string blockName = "Блок 1 — Новый вызов";

        [Header("Входящий вызов")]
        [Tooltip("ID порта, лампочка которого начнёт мигать при старте блока.")]
        [SerializeField] private int incomingPortId = 1;

        [Tooltip("Реплики, воспроизводимые после подключения первого штекера в incomingPortId.")]
        [SerializeField] private AudioClip[] initialDialogue;

        [Header("Переадресация (опционально)")]
        [Tooltip("Если включено — после начального диалога блок ждёт подключения второго штекера в targetPortId.")]
        [SerializeField] private bool requiresTransfer = false;

        [Tooltip("ID целевого порта, с которым нужно соединить абонента.")]
        [SerializeField] private int targetPortId = 2;

        [Tooltip("Реплики, воспроизводимые после успешного соединения с targetPortId.")]
        [SerializeField] private AudioClip[] afterTransferDialogue;

        public string BlockName => blockName;
        public int IncomingPortId => incomingPortId;
        public AudioClip[] InitialDialogue => initialDialogue;
        public bool RequiresTransfer => requiresTransfer;
        public int TargetPortId => targetPortId;
        public AudioClip[] AfterTransferDialogue => afterTransferDialogue;
    }
}
