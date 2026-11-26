using System.Collections;
using UnityEngine;
using LastSignal.SwitchboardSystem;

namespace LastSignal.StoryCore
{
    /// <summary>
    /// Режиссёр состояния одной сцены подвала на протяжении всех трёх ночей (ГДД «Комната по ночам»).
    /// Управляет: трещиной потолка, перечёркиванием «Берёзы» в книге и на карте, аварийным красным светом,
    /// поломкой ламп коммутатора в Ночь 3, вздрагиванием лампы и тряской камеры при разрывах,
    /// а также 10 секундами тишины и темноты после гибели «Берёзы».
    /// </summary>
    public sealed class RoomAtmosphereController : MonoBehaviour
    {
        [Header("Ссылки на системы сцены")]
        [SerializeField] private SmoothMouseLookCamera cameraLook;
        [SerializeField] private SwitchboardLineRouter lineRouter;
        [SerializeField] private StoryFlagsState flagsState;
        [SerializeField] private LogbookSubscribersPage subscribersPage;
        [SerializeField] private LogbookSpreadController spreadController;

        [Header("Элементы комнаты (ГДД «Комната по ночам»)")]
        [SerializeField] private GameObject night2CeilingCrack;
        [SerializeField] private GameObject mapBerezaCrossVisual;
        [SerializeField] private Light kerosenePointLight;
        [SerializeField] private Light roomWarmBounceLight;
        [SerializeField] private Light emergencyRedLight;
        [SerializeField] private ParticleSystem ceilingDustParticles;

        [Header("Звуки фронта (далёкие и близкие разрывы)")]
        [SerializeField] private AudioSource frontAudioSource;
        [SerializeField] private AudioClip customDistantBoomClip;
        [SerializeField] private AudioClip customCloseExplosionClip;
        [SerializeField, Range(0f, 1f)] private float distantBoomVolume = 0.42f;
        [SerializeField, Range(0f, 1f)] private float closeBoomVolume = 0.85f;

        [Header("Текущая стадия комнаты (только чтение)")]
        [SerializeField] private NightStage currentStage = NightStage.Night1;

        private float _normalKeroseneIntensity = 1.6f;
        private float _normalBounceIntensity = 0.6f;
        private AudioClip _procDistantBoom;
        private AudioClip _procCloseBoom;
        private Coroutine _blackoutCoroutine;

        private void Awake()
        {
            if (cameraLook == null && Camera.main != null)
            {
                cameraLook = Camera.main.GetComponent<SmoothMouseLookCamera>();
            }

            if (kerosenePointLight != null) _normalKeroseneIntensity = kerosenePointLight.intensity;
            if (roomWarmBounceLight != null) _normalBounceIntensity = roomWarmBounceLight.intensity;

            EnsureFrontAudio();
        }

        private void OnDestroy()
        {
            if (_procDistantBoom != null) Destroy(_procDistantBoom);
            if (_procCloseBoom != null) Destroy(_procCloseBoom);
        }

        /// <summary>
        /// Применяет визуальное и техническое состояние комнаты для заданной стадии игры.
        /// </summary>
        public void ApplyNightStage(NightStage stage)
        {
            currentStage = stage;

            bool isNight2OrLater = stage >= NightStage.Night2;
            bool isEmergencyRed = stage == NightStage.Night3AfterShelling;

            if (night2CeilingCrack != null)
            {
                night2CeilingCrack.SetActive(isNight2OrLater);
            }

            if (mapBerezaCrossVisual != null)
            {
                mapBerezaCrossVisual.SetActive(isNight2OrLater);
            }

            if (subscribersPage != null)
            {
                subscribersPage.SetBerezaCrossedOut(isNight2OrLater);
            }

            if (isNight2OrLater && flagsState != null)
            {
                flagsState.MarkBerezaDestroyed();
            }

            if (stage >= NightStage.Night3 && flagsState != null)
            {
                flagsState.UnlockReservePasswordTable();
                spreadController?.RefreshSpread2();
            }

            ApplyLightingMode(isEmergencyRed);

            if (lineRouter != null)
            {
                lineRouter.ClearDamagedPorts();
                if (isEmergencyRed)
                {
                    // По ГДД после 04:36 Ночи 3 половина ламп мертва (оставляем ключевые финальные линии).
                    lineRouter.SetPortDamaged(SubscriberId.Bereza, true);
                    lineRouter.SetPortDamaged(SubscriberId.Kuznitsa, true);
                    lineRouter.SetPortDamaged(SubscriberId.Shilo, true);
                    lineRouter.SetPortDamaged(SubscriberId.Romashka, true);
                }
            }
        }

        /// <summary>
        /// Исполняет режиссёрский эффект, вызванный узлом диалога.
        /// </summary>
        public void ExecuteRoomEffect(RoomEventEffect effect)
        {
            switch (effect)
            {
                case RoomEventEffect.DistantExplosion:
                    TriggerDistantExplosion();
                    break;

                case RoomEventEffect.CloseExplosion:
                    TriggerCloseExplosion(0.9f);
                    break;

                case RoomEventEffect.BlackoutSilence10s:
                    TriggerBlackoutSilence(10f);
                    break;

                case RoomEventEffect.SwitchToEmergencyRed:
                    ApplyNightStage(NightStage.Night3AfterShelling);
                    break;

                case RoomEventEffect.CrossOutBereza:
                    if (subscribersPage != null) subscribersPage.SetBerezaCrossedOut(true);
                    if (mapBerezaCrossVisual != null) mapBerezaCrossVisual.SetActive(true);
                    flagsState?.MarkBerezaDestroyed();
                    break;

                case RoomEventEffect.OpenReservePasswords:
                    flagsState?.UnlockReservePasswordTable();
                    spreadController?.RefreshSpread2();
                    break;
            }
        }

        [ContextMenu("Тест: далёкий разрыв")]
        public void TriggerDistantExplosion()
        {
            PlayBoom(close: false);
            if (ceilingDustParticles != null)
            {
                ceilingDustParticles.Emit(10);
            }
            if (cameraLook != null)
            {
                // Далёкие разрывы камеру почти не трясут (едва заметное дрожание пола).
                cameraLook.AddTraumaShake(0.12f);
            }
        }

        [ContextMenu("Тест: близкий удар по подвалу")]
        public void TriggerCloseExplosion(float trauma = 0.9f)
        {
            PlayBoom(close: true);
            if (ceilingDustParticles != null)
            {
                ceilingDustParticles.Emit(48);
            }
            if (cameraLook != null)
            {
                cameraLook.AddTraumaShake(trauma);
            }
        }

        /// <summary>
        /// 10 секунд тишины и погашенных ламп после гибели «Берёзы», после чего все лампы вспыхивают разом.
        /// </summary>
        public void TriggerBlackoutSilence(float durationSeconds = 10f)
        {
            if (_blackoutCoroutine != null)
            {
                StopCoroutine(_blackoutCoroutine);
            }
            _blackoutCoroutine = StartCoroutine(BlackoutRoutine(durationSeconds));
        }

        private IEnumerator BlackoutRoutine(float seconds)
        {
            lineRouter?.SetBlackout(true);
            float remaining = Mathf.Max(0.5f, seconds);
            while (remaining > 0f)
            {
                remaining -= Time.deltaTime;
                yield return null;
            }

            lineRouter?.FlashAllAliveLamps();
            _blackoutCoroutine = null;
        }

        private void ApplyLightingMode(bool emergencyRed)
        {
            if (kerosenePointLight != null)
            {
                kerosenePointLight.intensity = emergencyRed ? _normalKeroseneIntensity * 0.12f : _normalKeroseneIntensity;
            }

            if (roomWarmBounceLight != null)
            {
                roomWarmBounceLight.intensity = emergencyRed ? _normalBounceIntensity * 0.15f : _normalBounceIntensity;
            }

            if (emergencyRedLight != null)
            {
                emergencyRedLight.enabled = emergencyRed;
            }
        }

        private void PlayBoom(bool close)
        {
            EnsureFrontAudio();
            if (frontAudioSource == null) return;

            AudioClip clip = close
                ? (customCloseExplosionClip != null ? customCloseExplosionClip : _procCloseBoom)
                : (customDistantBoomClip != null ? customDistantBoomClip : _procDistantBoom);

            if (clip != null)
            {
                frontAudioSource.pitch = Random.Range(0.92f, 1.06f);
                frontAudioSource.PlayOneShot(clip, close ? closeBoomVolume : distantBoomVolume);
            }
        }

        private void EnsureFrontAudio()
        {
            if (frontAudioSource == null)
            {
                frontAudioSource = gameObject.AddComponent<AudioSource>();
            }
            frontAudioSource.playOnAwake = false;
            frontAudioSource.loop = false;
            frontAudioSource.spatialBlend = 0f;

            if (_procDistantBoom == null) _procDistantBoom = CreateBoomClip(duration: 1.6f, lowpassFactor: 0.045f, seed: 701);
            if (_procCloseBoom == null) _procCloseBoom = CreateBoomClip(duration: 2.2f, lowpassFactor: 0.14f, seed: 911);
        }

        private static AudioClip CreateBoomClip(float duration, float lowpassFactor, int seed)
        {
            const int sr = 44100;
            int count = Mathf.RoundToInt(sr * duration);
            float[] data = new float[count];
            var rng = new System.Random(seed);
            float lp1 = 0f, lp2 = 0f;

            for (int i = 0; i < count; i++)
            {
                float u = i / (float)count;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                float sub = Mathf.Sin(2f * Mathf.PI * (52f - 28f * u) * (i / (float)sr));

                lp1 += lowpassFactor * (white - lp1);
                lp2 += lowpassFactor * (lp1 - lp2);

                float attack = Mathf.Clamp01(u * 40f);
                float decay = Mathf.Exp(-u * 3.4f);
                data[i] = Mathf.Clamp((lp2 * 2.8f + sub * 0.45f) * attack * decay, -0.95f, 0.95f);
            }

            AudioClip clip = AudioClip.Create($"Front_Boom_{seed}", count, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
