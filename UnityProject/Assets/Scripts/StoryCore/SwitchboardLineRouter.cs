using System;
using System.Collections.Generic;
using UnityEngine;
using LastSignal.SwitchboardSystem;

namespace LastSignal.StoryCore
{
    /// <summary>
    /// Маршрутизатор линий коммутатора нового поколения (поддерживает 1–3 параллельных входящих звонка,
    /// таймер терпения абонента, исходящие вызовы от игрока, переадресацию вторым шнуром,
    /// поломку ламп в Ночь 3 и полное затемнение ламп при гибели «Берёзы»).
    /// </summary>
    public sealed class SwitchboardLineRouter : MonoBehaviour
    {
        [Serializable]
        public sealed class RingingLineInfo
        {
            public SubscriberId port;
            public CallScenarioAsset call;
            public float remainingPatience;
            public bool hasPatienceTimer;
        }

        [Header("Ссылки на физику коммутатора")]
        [SerializeField] private SwitchboardInteractor interactor;
        [SerializeField] private List<PortIndicator> portIndicators = new List<PortIndicator>();

        [Header("Живое состояние линий (только чтение в Inspector)")]
        [SerializeField] private List<RingingLineInfo> activeRingingLines = new List<RingingLineInfo>();
        [SerializeField] private List<SubscriberId> connectedPortsDebug = new List<SubscriberId>();

        private readonly Dictionary<int, PortIndicator> _indicatorByPortId = new Dictionary<int, PortIndicator>();
        private readonly HashSet<int> _connectedPortIds = new HashSet<int>();
        private readonly HashSet<int> _damagedPortIds = new HashSet<int>();
        private readonly HashSet<int> _pausedHoldPortIds = new HashSet<int>();
        private readonly List<RingingLineInfo> _expiredBuffer = new List<RingingLineInfo>();
        private bool _blackoutActive;

        public int RingingCount => activeRingingLines.Count;
        public IReadOnlyList<RingingLineInfo> RingingLines => activeRingingLines;

        /// <summary>Игрок ответил на мигающий входящий звонок (воткнул шнур в его гнездо).</summary>
        public event Action<CallScenarioAsset, SubscriberId> OnIncomingCallAnswered;

        /// <summary>У мигающего звонка закончилось терпение до ответа игрока.</summary>
        public event Action<CallScenarioAsset, SubscriberId> OnIncomingCallTimedOut;

        /// <summary>Игрок воткнул шнур в порт, который сейчас НЕ мигает (исходящий вызов или второй шнур переадресации).</summary>
        public event Action<SubscriberId> OnNonRingingPortConnected;

        /// <summary>Игрок физически подключил любой порт (всегда вызывается).</summary>
        public event Action<SubscriberId> OnAnyPortConnected;

        /// <summary>Игрок выдернул шнур из порта.</summary>
        public event Action<SubscriberId> OnAnyPortDisconnected;

        private void Awake()
        {
            if (interactor == null)
            {
                interactor = FindFirstObjectByType<SwitchboardInteractor>();
            }

            CollectIndicatorsIfNeeded();
            TurnOffAllIndicators();
        }

        private void OnEnable()
        {
            if (interactor != null)
            {
                interactor.OnPortConnected += HandlePortConnected;
                interactor.OnPortDisconnected += HandlePortDisconnected;
            }
        }

        private void OnDisable()
        {
            if (interactor != null)
            {
                interactor.OnPortConnected -= HandlePortConnected;
                interactor.OnPortDisconnected -= HandlePortDisconnected;
            }
        }

        private void Update()
        {
            if (activeRingingLines.Count == 0)
            {
                return;
            }

            float dt = Time.deltaTime;
            _expiredBuffer.Clear();

            for (int i = 0; i < activeRingingLines.Count; i++)
            {
                RingingLineInfo line = activeRingingLines[i];
                if (!line.hasPatienceTimer)
                {
                    continue;
                }

                line.remainingPatience -= dt;
                if (line.remainingPatience <= 0f)
                {
                    _expiredBuffer.Add(line);
                }
            }

            for (int i = 0; i < _expiredBuffer.Count; i++)
            {
                RingingLineInfo expired = _expiredBuffer[i];
                activeRingingLines.Remove(expired);
                RefreshLampForPort((int)expired.port);
                OnIncomingCallTimedOut?.Invoke(expired.call, expired.port);
            }
        }

        public bool IsPortConnected(SubscriberId port)
        {
            return _connectedPortIds.Contains((int)port);
        }

        public bool IsPortRinging(SubscriberId port)
        {
            return FindRingingIndex(port) >= 0;
        }

        /// <summary>
        /// Запускает мигание лампы входящего вызова на указанном порту.
        /// Если штекер уже вставлен в этот порт — немедленно отвечает на вызов.
        /// </summary>
        public bool BeginIncomingRing(CallScenarioAsset call, float patienceSeconds)
        {
            if (call == null || call.subscriberPort == SubscriberId.None)
            {
                return false;
            }

            int portId = (int)call.subscriberPort;
            if (_damagedPortIds.Contains(portId) || IsPortRinging(call.subscriberPort))
            {
                return false;
            }

            // Если шнур уже вставлен в этот порт — сразу принимаем вызов.
            if (_connectedPortIds.Contains(portId))
            {
                RefreshLampForPort(portId);
                OnIncomingCallAnswered?.Invoke(call, call.subscriberPort);
                return true;
            }

            var info = new RingingLineInfo
            {
                port = call.subscriberPort,
                call = call,
                hasPatienceTimer = patienceSeconds > 0.01f,
                remainingPatience = Mathf.Max(0f, patienceSeconds)
            };

            activeRingingLines.Add(info);
            RefreshLampForPort(portId);
            return true;
        }

        public void CancelIncomingRing(SubscriberId port)
        {
            int idx = FindRingingIndex(port);
            if (idx >= 0)
            {
                activeRingingLines.RemoveAt(idx);
                RefreshLampForPort((int)port);
            }
        }

        public void ClearAllRingingLines()
        {
            activeRingingLines.Clear();
            _pausedHoldPortIds.Clear();
            RefreshAllLamps();
        }

        /// <summary>
        /// Переводит порт прерванного звонка в мягкое мигание «на удержании», пока игрок не воткнёт штекер обратно.
        /// Не запускает звонок заново — DialogueRunner сам возобновит его по OnAnyPortConnected.
        /// </summary>
        public void SetPortOnPausedHold(SubscriberId port, bool onHold)
        {
            int id = (int)port;
            if (onHold) _pausedHoldPortIds.Add(id);
            else _pausedHoldPortIds.Remove(id);
            RefreshLampForPort(id);
        }

        /// <summary>
        /// Включает или выключает режим «сломанная лампа» (Ночь 3 после 04:36).
        /// </summary>
        public void SetPortDamaged(SubscriberId port, bool damaged)
        {
            int id = (int)port;
            if (damaged)
            {
                _damagedPortIds.Add(id);
                CancelIncomingRing(port);
            }
            else
            {
                _damagedPortIds.Remove(id);
            }

            RefreshLampForPort(id);
        }

        public void ClearDamagedPorts()
        {
            _damagedPortIds.Clear();
            RefreshAllLamps();
        }

        /// <summary>
        /// Полностью гасит все лампы (например, 10 секунд тишины после гибели «Берёзы»).
        /// </summary>
        public void SetBlackout(bool blackout)
        {
            _blackoutActive = blackout;
            RefreshAllLamps();
        }

        /// <summary>
        /// Заставляет мигать ВСЕ неповреждённые лампы одновременно (финал Ночи 1 — шквал вызовов после гибели «Берёзы»).
        /// </summary>
        public void FlashAllAliveLamps()
        {
            _blackoutActive = false;
            foreach (var kv in _indicatorByPortId)
            {
                if (kv.Value != null)
                {
                    kv.Value.SetState(_damagedPortIds.Contains(kv.Key) ? IndicatorState.Off : IndicatorState.Blinking);
                }
            }
        }

        public void RefreshAllLamps()
        {
            foreach (var kv in _indicatorByPortId)
            {
                RefreshLampForPort(kv.Key);
            }
        }

        private void HandlePortConnected(int portId)
        {
            _connectedPortIds.Add(portId);
            SyncConnectedDebugList();

            SubscriberId subscriber = SubscriberIdUtility.FromPortId(portId);
            int ringIndex = FindRingingIndex(subscriber);
            if (ringIndex >= 0)
            {
                CallScenarioAsset answeredCall = activeRingingLines[ringIndex].call;
                activeRingingLines.RemoveAt(ringIndex);
                RefreshLampForPort(portId);

                OnAnyPortConnected?.Invoke(subscriber);
                OnIncomingCallAnswered?.Invoke(answeredCall, subscriber);
                return;
            }

            RefreshLampForPort(portId);
            OnAnyPortConnected?.Invoke(subscriber);
            OnNonRingingPortConnected?.Invoke(subscriber);
        }

        private void HandlePortDisconnected(int portId)
        {
            _connectedPortIds.Remove(portId);
            SyncConnectedDebugList();
            RefreshLampForPort(portId);

            SubscriberId subscriber = SubscriberIdUtility.FromPortId(portId);
            OnAnyPortDisconnected?.Invoke(subscriber);
        }

        private void RefreshLampForPort(int portId)
        {
            if (!_indicatorByPortId.TryGetValue(portId, out PortIndicator indicator) || indicator == null)
            {
                return;
            }

            if (_blackoutActive || _damagedPortIds.Contains(portId))
            {
                indicator.SetState(IndicatorState.Off);
                return;
            }

            if (_connectedPortIds.Contains(portId))
            {
                indicator.SetState(IndicatorState.Solid);
                return;
            }

            SubscriberId sub = SubscriberIdUtility.FromPortId(portId);
            bool shouldBlink = IsPortRinging(sub) || _pausedHoldPortIds.Contains(portId);
            indicator.SetState(shouldBlink ? IndicatorState.Blinking : IndicatorState.Off);
        }

        private int FindRingingIndex(SubscriberId port)
        {
            for (int i = 0; i < activeRingingLines.Count; i++)
            {
                if (activeRingingLines[i].port == port)
                {
                    return i;
                }
            }

            return -1;
        }

        private void CollectIndicatorsIfNeeded()
        {
            if (portIndicators.Count == 0)
            {
                portIndicators.AddRange(FindObjectsByType<PortIndicator>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            }

            _indicatorByPortId.Clear();
            for (int i = 0; i < portIndicators.Count; i++)
            {
                PortIndicator ind = portIndicators[i];
                if (ind != null)
                {
                    _indicatorByPortId[ind.PortId] = ind;
                }
            }
        }

        private void TurnOffAllIndicators()
        {
            foreach (var kv in _indicatorByPortId)
            {
                if (kv.Value != null)
                {
                    kv.Value.SetState(IndicatorState.Off);
                }
            }
        }

        private void SyncConnectedDebugList()
        {
            connectedPortsDebug.Clear();
            foreach (int id in _connectedPortIds)
            {
                connectedPortsDebug.Add(SubscriberIdUtility.FromPortId(id));
            }
        }
    }
}
