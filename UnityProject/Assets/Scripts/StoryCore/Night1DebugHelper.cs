using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastSignal.StoryCore
{
    /// <summary>
    /// Удобный отладочный хелпер для тестирования и презентации Ночи 1:
    /// F1..F6 — мгновенный переход на звонки 1.1..1.6;
    /// F7 — мгновенный переход на кульминацию гибели «Берёзы».
    /// Все те же команды доступны через ПКМ (ContextMenu) в Inspector.
    /// </summary>
    public sealed class Night1DebugHelper : MonoBehaviour
    {
        [SerializeField] private NightSessionManager sessionManager;
        [SerializeField] private bool enableFunctionHotkeys = true;

        private void Awake()
        {
            if (sessionManager == null)
            {
                sessionManager = FindFirstObjectByType<NightSessionManager>();
            }
        }

        private void Update()
        {
            if (!enableFunctionHotkeys || sessionManager == null)
            {
                return;
            }

            if (WasKeyPressed(KeyCode.F1)) JumpToCall_1_1();
            else if (WasKeyPressed(KeyCode.F2)) JumpToCall_1_2();
            else if (WasKeyPressed(KeyCode.F3)) JumpToCall_1_3();
            else if (WasKeyPressed(KeyCode.F4)) JumpToCall_1_4();
            else if (WasKeyPressed(KeyCode.F5)) JumpToCall_1_5();
            else if (WasKeyPressed(KeyCode.F6)) JumpToCall_1_6();
            else if (WasKeyPressed(KeyCode.F7)) JumpToCall_1_6_Climax();
        }

        [ContextMenu("F1: Звонок 1.1 — Берёза (Книга и пароль)")]
        public void JumpToCall_1_1() => sessionManager?.DebugJumpToNight1Call(0);

        [ContextMenu("F2: Звонок 1.2 — Кузница и Кобра")]
        public void JumpToCall_1_2() => sessionManager?.DebugJumpToNight1Call(1);

        [ContextMenu("F3: Звонок 1.3 — Ёж и Гром (Координаты)")]
        public void JumpToCall_1_3() => sessionManager?.DebugJumpToNight1Call(2);

        [ContextMenu("F4: Звонок 1.4 — Шило и Кобра (Условный сигнал)")]
        public void JumpToCall_1_4() => sessionManager?.DebugJumpToNight1Call(3);

        [ContextMenu("F5: Звонок 1.5 — Ромашка и Якорь (Мост)")]
        public void JumpToCall_1_5() => sessionManager?.DebugJumpToNight1Call(4);

        [ContextMenu("F6: Звонок 1.6 — Берёза (Третье правило и гибель)")]
        public void JumpToCall_1_6() => sessionManager?.DebugJumpToNight1Call(5);

        [ContextMenu("F7: Кульминация гибели «Берёзы» (сразу взрыв и 10с тишины)")]
        public void JumpToCall_1_6_Climax() => sessionManager?.DebugJumpToNight1Call(5, startAtBerezaClimax: true);

        private static bool WasKeyPressed(KeyCode keyCode)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb == null) return false;
            return keyCode switch
            {
                KeyCode.F1 => kb.f1Key.wasPressedThisFrame,
                KeyCode.F2 => kb.f2Key.wasPressedThisFrame,
                KeyCode.F3 => kb.f3Key.wasPressedThisFrame,
                KeyCode.F4 => kb.f4Key.wasPressedThisFrame,
                KeyCode.F5 => kb.f5Key.wasPressedThisFrame,
                KeyCode.F6 => kb.f6Key.wasPressedThisFrame,
                KeyCode.F7 => kb.f7Key.wasPressedThisFrame,
                _ => false
            };
#else
            return Input.GetKeyDown(keyCode);
#endif
        }
    }
}
