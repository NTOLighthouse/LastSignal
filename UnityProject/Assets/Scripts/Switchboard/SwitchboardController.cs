using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Текущая фаза выполнения активного сценарного блока в SwitchboardController.
    /// </summary>
    public enum SwitchboardBlockPhase
    {
        Idle = 0,
        WaitingForIncomingPlug = 1,
        PlayingInitialDialogue = 2,
        WaitingForTransferPlug = 3,
        PlayingTransferDialogue = 4,
        Completed = 5
    }

    /// <summary>
    /// Центральный оркестратор телефонного коммутатора.
    /// Управляет очередью SwitchboardBlockData, переключает PortIndicator,
    /// запускает реплики через DialogueAudioPlayer и рассылает события внешним системам.
    /// </summary>
    public sealed class SwitchboardController : MonoBehaviour
    {
        [Serializable]
        public sealed class BlockUnityEvent : UnityEvent<SwitchboardBlockData> { }

        [Header("Ссылки на модули")]
        [SerializeField] private DialogueAudioPlayer dialoguePlayer;
        [SerializeField] private List<PortIndicator> indicators = new List<PortIndicator>();

        [Header("Очередь сценарных блоков")]
        [SerializeField] private List<SwitchboardBlockData> blocks = new List<SwitchboardBlockData>();

        [Header("Параметры запуска")]
        [SerializeField] private bool autoStartOnPlay = true;
        [SerializeField, Min(0f)] private float delayBetweenBlocks = 0.5f;

        [Header("UnityEvents для привязки в Inspector")]
        [SerializeField] private BlockUnityEvent onBlockStartedEvent = new BlockUnityEvent();
        [SerializeField] private BlockUnityEvent onConnectedEvent = new BlockUnityEvent();
        [SerializeField] private BlockUnityEvent onBlockCompletedEvent = new BlockUnityEvent();
        [SerializeField] private UnityEvent onAllBlocksCompletedEvent = new UnityEvent();

        private readonly Dictionary<int, PortIndicator> _indicatorMap = new Dictionary<int, PortIndicator>();
        private readonly HashSet<int> _connectedPorts = new HashSet<int>();

        private Coroutine _transitionCoroutine;
        private int _blockToken;

        public IReadOnlyList<SwitchboardBlockData> Blocks => blocks;
        public SwitchboardBlockData CurrentBlock { get; private set; }
        public int CurrentBlockIndex { get; private set; } = -1;
        public SwitchboardBlockPhase CurrentPhase { get; private set; } = SwitchboardBlockPhase.Idle;
        public bool IsRunning => CurrentBlock != null && CurrentPhase != SwitchboardBlockPhase.Idle;

        /// <summary>Срабатывает при запуске очередного блока (лампочка incomingPortId начинает мигать).</summary>
        public event Action<SwitchboardBlockData> OnBlockStarted;

        /// <summary>Срабатывает при подключении ожидаемого порта (входящего или целевого при переадресации).</summary>
        public event Action<SwitchboardBlockData> OnConnected;

        /// <summary>Срабатывает при подключении конкретного порта, ожидаемого текущим блоком (передаёт portId).</summary>
        public event Action<SwitchboardBlockData, int> OnExpectedPortConnected;

        /// <summary>Срабатывает, когда начальный диалог завершён и блок ждёт подключения к targetPortId.</summary>
        public event Action<SwitchboardBlockData> OnTransferWaitingStarted;

        /// <summary>Срабатывает, когда текущий блок полностью завершён.</summary>
        public event Action<SwitchboardBlockData> OnBlockCompleted;

        /// <summary>Срабатывает после завершения последнего блока в очереди.</summary>
        public event Action OnAllBlocksCompleted;

        private void Awake()
        {
            if (dialoguePlayer == null)
            {
                dialoguePlayer = GetComponentInChildren<DialogueAudioPlayer>();
            }

            if (indicators.Count == 0)
            {
                GetComponentsInChildren(true, indicators);
            }

            RebuildIndicatorMap();
            TurnOffAllIndicators();
        }

        private void Start()
        {
            if (autoStartOnPlay && blocks.Count > 0)
            {
                StartSequence();
            }
        }

        private void OnDisable()
        {
            StopSequence();
        }

        /// <summary>
        /// Запускает очередь блоков с самого начала.
        /// </summary>
        public void StartSequence()
        {
            StopSequence();

            if (blocks == null || blocks.Count == 0)
            {
                Debug.LogWarning("[SwitchboardController] Очередь блоков пуста.", this);
                return;
            }

            StartNextValidBlockFrom(0);
        }

        /// <summary>
        /// Останавливает выполнение текущего блока, диалога и гасит индикаторы.
        /// </summary>
        public void StopSequence()
        {
            _blockToken++;

            if (_transitionCoroutine != null)
            {
                StopCoroutine(_transitionCoroutine);
                _transitionCoroutine = null;
            }

            if (dialoguePlayer != null)
            {
                dialoguePlayer.Stop();
            }

            CurrentBlock = null;
            CurrentBlockIndex = -1;
            CurrentPhase = SwitchboardBlockPhase.Idle;
            TurnOffAllIndicators();
        }

        /// <summary>
        /// Вызывается скриптом штекера, когда штекер вставлен в указанный порт.
        /// Лампочка этого порта сразу загорается на постоянке (Solid).
        /// </summary>
        public void OnPlugConnected(int portId)
        {
            _connectedPorts.Add(portId);
            SetPortIndicatorState(portId, IndicatorState.Solid);

            if (CurrentBlock == null)
            {
                return;
            }

            if (CurrentPhase == SwitchboardBlockPhase.WaitingForIncomingPlug &&
                portId == CurrentBlock.IncomingPortId)
            {
                HandleIncomingConnected();
                return;
            }

            if (CurrentPhase == SwitchboardBlockPhase.WaitingForTransferPlug &&
                portId == CurrentBlock.TargetPortId)
            {
                HandleTransferConnected();
            }
        }

        /// <summary>
        /// Вызывается скриптом штекера, когда штекер выдернут из указанного порта.
        /// Гасит лампочку (или возвращает в мигание, если на этом порту сейчас ждёт входящий вызов).
        /// </summary>
        public void OnPlugDisconnected(int portId)
        {
            _connectedPorts.Remove(portId);

            bool isWaitingIncoming =
                CurrentBlock != null &&
                CurrentPhase == SwitchboardBlockPhase.WaitingForIncomingPlug &&
                portId == CurrentBlock.IncomingPortId;

            SetPortIndicatorState(portId, isWaitingIncoming ? IndicatorState.Blinking : IndicatorState.Off);
        }

        /// <summary>
        /// Удобное переключение состояния лампочки конкретного порта из кода.
        /// </summary>
        public void SetPortIndicatorState(int portId, IndicatorState state)
        {
            if (_indicatorMap.Count == 0 && indicators.Count > 0)
            {
                RebuildIndicatorMap();
            }

            if (_indicatorMap.TryGetValue(portId, out PortIndicator indicator) && indicator != null)
            {
                indicator.SetState(state);
            }
        }

        private void StartNextValidBlockFrom(int startIndex)
        {
            for (int i = startIndex; i < blocks.Count; i++)
            {
                if (blocks[i] != null)
                {
                    StartBlock(i);
                    return;
                }
            }

            CurrentBlock = null;
            CurrentBlockIndex = -1;
            CurrentPhase = SwitchboardBlockPhase.Idle;

            OnAllBlocksCompleted?.Invoke();
            onAllBlocksCompletedEvent?.Invoke();
        }

        private void StartBlock(int index)
        {
            _blockToken++;
            CurrentBlockIndex = index;
            CurrentBlock = blocks[index];
            CurrentPhase = SwitchboardBlockPhase.WaitingForIncomingPlug;

            SetPortIndicatorState(CurrentBlock.IncomingPortId, IndicatorState.Blinking);

            OnBlockStarted?.Invoke(CurrentBlock);
            onBlockStartedEvent?.Invoke(CurrentBlock);

            // Если штекер уже вставлен в порт входящего вызова к моменту старта блока — сразу принимаем вызов.
            if (CurrentBlock != null &&
                CurrentPhase == SwitchboardBlockPhase.WaitingForIncomingPlug &&
                _connectedPorts.Contains(CurrentBlock.IncomingPortId))
            {
                HandleIncomingConnected();
            }
        }

        private void HandleIncomingConnected()
        {
            SwitchboardBlockData block = CurrentBlock;
            int token = _blockToken;

            CurrentPhase = SwitchboardBlockPhase.PlayingInitialDialogue;
            SetPortIndicatorState(block.IncomingPortId, IndicatorState.Solid);

            OnConnected?.Invoke(block);
            OnExpectedPortConnected?.Invoke(block, block.IncomingPortId);
            onConnectedEvent?.Invoke(block);

            PlayBlockDialogue(block.InitialDialogue, () =>
            {
                if (token != _blockToken || CurrentBlock != block)
                {
                    return;
                }

                OnInitialDialogueFinished(block);
            });
        }

        private void OnInitialDialogueFinished(SwitchboardBlockData block)
        {
            if (!block.RequiresTransfer)
            {
                CompleteCurrentBlock();
                return;
            }

            CurrentPhase = SwitchboardBlockPhase.WaitingForTransferPlug;
            OnTransferWaitingStarted?.Invoke(block);

            // Если целевой порт уже подключён ко времени завершения начального диалога — сразу соединяем.
            if (CurrentBlock == block &&
                CurrentPhase == SwitchboardBlockPhase.WaitingForTransferPlug &&
                _connectedPorts.Contains(block.TargetPortId))
            {
                HandleTransferConnected();
            }
        }

        private void HandleTransferConnected()
        {
            SwitchboardBlockData block = CurrentBlock;
            int token = _blockToken;

            CurrentPhase = SwitchboardBlockPhase.PlayingTransferDialogue;
            SetPortIndicatorState(block.TargetPortId, IndicatorState.Solid);

            OnConnected?.Invoke(block);
            OnExpectedPortConnected?.Invoke(block, block.TargetPortId);
            onConnectedEvent?.Invoke(block);

            PlayBlockDialogue(block.AfterTransferDialogue, () =>
            {
                if (token != _blockToken || CurrentBlock != block)
                {
                    return;
                }

                CompleteCurrentBlock();
            });
        }

        private void CompleteCurrentBlock()
        {
            SwitchboardBlockData finishedBlock = CurrentBlock;
            int nextIndex = CurrentBlockIndex + 1;
            int token = _blockToken;

            CurrentPhase = SwitchboardBlockPhase.Completed;

            if (finishedBlock != null)
            {
                // Если штекер всё ещё воткнут — лампа продолжает гореть Solid, пока его не вытащат.
                SetPortIndicatorState(
                    finishedBlock.IncomingPortId,
                    _connectedPorts.Contains(finishedBlock.IncomingPortId) ? IndicatorState.Solid : IndicatorState.Off);

                if (finishedBlock.RequiresTransfer)
                {
                    SetPortIndicatorState(
                        finishedBlock.TargetPortId,
                        _connectedPorts.Contains(finishedBlock.TargetPortId) ? IndicatorState.Solid : IndicatorState.Off);
                }

                OnBlockCompleted?.Invoke(finishedBlock);
                onBlockCompletedEvent?.Invoke(finishedBlock);
            }

            // Если подписчик события сам остановил или переключил последовательность — не вмешиваемся.
            if (token != _blockToken)
            {
                return;
            }

            if (delayBetweenBlocks > 0.001f)
            {
                _transitionCoroutine = StartCoroutine(AdvanceAfterDelayRoutine(nextIndex, delayBetweenBlocks, token));
            }
            else
            {
                StartNextValidBlockFrom(nextIndex);
            }
        }

        private IEnumerator AdvanceAfterDelayRoutine(int nextIndex, float delaySeconds, int token)
        {
            float remaining = delaySeconds;
            while (remaining > 0f && token == _blockToken)
            {
                remaining -= Time.deltaTime;
                yield return null;
            }

            _transitionCoroutine = null;

            if (token == _blockToken)
            {
                StartNextValidBlockFrom(nextIndex);
            }
        }

        private void PlayBlockDialogue(AudioClip[] clips, Action onComplete)
        {
            if (dialoguePlayer == null)
            {
                Debug.LogWarning("[SwitchboardController] Не назначен DialogueAudioPlayer — диалог пропущен.", this);
                onComplete?.Invoke();
                return;
            }

            dialoguePlayer.PlayDialogue(clips, onComplete);
        }

        private void RebuildIndicatorMap()
        {
            _indicatorMap.Clear();

            for (int i = 0; i < indicators.Count; i++)
            {
                PortIndicator indicator = indicators[i];
                if (indicator == null)
                {
                    continue;
                }

                if (_indicatorMap.ContainsKey(indicator.PortId))
                {
                    Debug.LogWarning(
                        $"[SwitchboardController] Дублирующийся PortIndicator для portId={indicator.PortId}.",
                        indicator);
                }

                _indicatorMap[indicator.PortId] = indicator;
            }
        }

        private void TurnOffAllIndicators()
        {
            for (int i = 0; i < indicators.Count; i++)
            {
                if (indicators[i] != null)
                {
                    indicators[i].SetState(IndicatorState.Off);
                }
            }
        }
    }
}
