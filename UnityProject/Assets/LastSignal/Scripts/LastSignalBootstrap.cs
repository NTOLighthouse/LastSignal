using UnityEngine;

namespace LastSignal
{
    public static class LastSignalBootstrap
    {
        // Отключено на время работы с 3D-сценой, чтобы прототипный IMGUI-оверлей не перекрывал экран.
        public static bool EnableOverlayPrototype = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateGame()
        {
            if (!EnableOverlayPrototype)
            {
                return;
            }

            if (Object.FindAnyObjectByType<LastSignalGame>() != null)
            {
                return;
            }

            GameObject root = new GameObject("Last Signal Game");
            root.AddComponent<LastSignalGame>();
        }
    }
}
