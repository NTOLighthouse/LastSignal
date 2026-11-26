using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastSignal
{
    /// <summary>
    /// Нативный шлюз блокировки игрового ввода (заставка, титры между ночами, экран итогов, меню).
    /// Использует учёт владельцев (reference-counted), чтобы разные системы не перебивали друг друга.
    /// </summary>
    public static class GameplayInputGate
    {
        private static readonly Dictionary<object, bool> ActiveBlockers = new Dictionary<object, bool>();

        public static bool IsBlocked => ActiveBlockers.Count > 0;

        public static bool ShouldHideReticle
        {
            get
            {
                foreach (var kv in ActiveBlockers)
                {
                    if (kv.Value)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>Срабатывает при переходе состояния блокировки (true = заблокировано, false = разблокировано).</summary>
        public static event Action<bool> OnBlockedChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            ActiveBlockers.Clear();
            OnBlockedChanged = null;
        }

        public static void AcquireBlock(object owner, bool hideReticle = true)
        {
            if (owner == null)
            {
                return;
            }

            bool wasBlocked = IsBlocked;
            ActiveBlockers[owner] = hideReticle;

            if (!wasBlocked && IsBlocked)
            {
                OnBlockedChanged?.Invoke(true);
            }
        }

        public static void ReleaseBlock(object owner)
        {
            if (owner == null || !ActiveBlockers.Remove(owner))
            {
                return;
            }

            if (!IsBlocked)
            {
                OnBlockedChanged?.Invoke(false);
            }
        }
    }
}
