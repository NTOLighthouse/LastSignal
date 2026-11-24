using UnityEngine;

namespace LastSignal
{
    public static class LastSignalBootstrap
    {
        // Отключено, чтобы скриптовый 2D-UI не перекрывал 3D-сцену коммутатора.
        // [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateGame()
        {
            if (Object.FindAnyObjectByType<LastSignalGame>() != null)
            {
                return;
            }

            // Логика и новый интерфейс создаются поверх готовой 3D-сцены.
            GameObject root = new GameObject("Last Signal Game");
            root.AddComponent<LastSignalGame>();
        }
    }
}
