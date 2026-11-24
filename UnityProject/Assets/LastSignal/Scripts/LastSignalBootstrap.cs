using UnityEngine;

namespace LastSignal
{
    public static class LastSignalBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
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
