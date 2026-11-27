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
        [SerializeField] private KeroseneLampFlicker keroseneLampFlicker;
        [SerializeField] private DeskClockController deskClock;

        [Header("Элементы комнаты (ГДД «Комната по ночам»)")]
        [SerializeField] private GameObject night2CeilingCrack;
        [SerializeField] private GameObject mapBerezaCrossVisual;
        [SerializeField] private Light kerosenePointLight;
        [SerializeField] private Light roomWarmBounceLight;
        [SerializeField] private Light emergencyRedLight;
        [SerializeField] private ParticleSystem ceilingDustParticles;

        [Header("Звуки фронта и комнаты (можно подставить свои AudioClip)")]
        [SerializeField] private AudioSource frontAudioSource;
        [SerializeField] private AudioSource roomAmbientSource;
        [SerializeField] private AudioClip customMatchStrikeClip;
        [SerializeField] private AudioClip customRoomLampHissClip;
        [SerializeField] private AudioClip customWaterDropClip;
        [SerializeField] private AudioClip customArtilleryShotClip;
        [SerializeField] private AudioClip customDistantBoomClip;
        [SerializeField] private AudioClip customCloseExplosionClip;
        [SerializeField, Range(0f, 1f)] private float roomAmbientVolume = 0.14f;
        [SerializeField, Range(0f, 1f)] private float waterDropVolume = 0.10f;
        [SerializeField, Range(0f, 1f)] private float distantBoomVolume = 0.42f;
        [SerializeField, Range(0f, 1f)] private float closeBoomVolume = 0.85f;

        [Header("Текущая стадия комнаты (только чтение)")]
        [SerializeField] private NightStage currentStage = NightStage.Night1;

        private float _normalKeroseneIntensity = 1.6f;
        private float _normalBounceIntensity = 0.6f;
        private AudioClip _procDistantBoom;
        private AudioClip _procCloseBoom;
        private Coroutine _blackoutCoroutine;
        private Coroutine _lampIgniteCoroutine;
        private bool _isTotalSilence;
        private float _nextWaterDropTimer = 7f;

        public bool IsTotalSilence => _isTotalSilence;

        private void Awake()
        {
            if (cameraLook == null && Camera.main != null)
            {
                cameraLook = Camera.main.GetComponent<SmoothMouseLookCamera>();
            }

            if (keroseneLampFlicker == null)
            {
                keroseneLampFlicker = FindFirstObjectByType<KeroseneLampFlicker>();
            }

            if (deskClock == null)
            {
                deskClock = FindFirstObjectByType<DeskClockController>();
            }

            if (kerosenePointLight != null) _normalKeroseneIntensity = kerosenePointLight.intensity;
            if (roomWarmBounceLight != null) _normalBounceIntensity = roomWarmBounceLight.intensity;

            EnsureFrontAudio();
            EnsureRoomAmbientAudio();
        }

        private void Update()
        {
            if (_isTotalSilence)
            {
                return;
            }

            _nextWaterDropTimer -= Time.deltaTime;
            if (_nextWaterDropTimer <= 0f)
            {
                _nextWaterDropTimer = Random.Range(8f, 17f);
                PlayWaterDrop();
            }
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

                case RoomEventEffect.SubtleDistantBoom:
                    TriggerSubtleDistantBoom();
                    break;

                case RoomEventEffect.ArtilleryShotAndImpact:
                    StartCoroutine(PlayArtilleryShotThenImpactRoutine());
                    break;
            }
        }

        /// <summary>
        /// Готовит керосиновую лампу в потушенном состоянии перед вступлением Ночи 1.
        /// </summary>
        public void PrepareLampUnlitForIntro()
        {
            if (_lampIgniteCoroutine != null)
            {
                StopCoroutine(_lampIgniteCoroutine);
                _lampIgniteCoroutine = null;
            }

            if (keroseneLampFlicker != null)
            {
                keroseneLampFlicker.IntensityMultiplier = 0f;
            }

            if (roomWarmBounceLight != null)
            {
                roomWarmBounceLight.intensity = 0f;
            }

            if (roomAmbientSource != null && roomAmbientSource.isPlaying)
            {
                roomAmbientSource.Pause();
            }
        }

        /// <summary>
        /// Звук чиркающей спички и плавное зажигание керосиновой лампы (вступление Ночи 1).
        /// </summary>
        public void PlayMatchStrikeAndIgniteLamp()
        {
            EnsureFrontAudio();
            AudioClip clip = customMatchStrikeClip != null ? customMatchStrikeClip : ProceduralRadioAudio.GetMatchStrikeAndIgnite();
            if (frontAudioSource != null && clip != null)
            {
                frontAudioSource.pitch = 1f;
                frontAudioSource.PlayOneShot(clip, 0.72f);
            }

            if (_lampIgniteCoroutine != null)
            {
                StopCoroutine(_lampIgniteCoroutine);
            }
            _lampIgniteCoroutine = StartCoroutine(IgniteLampRoutine());
        }

        /// <summary>
        /// Включает или выключает абсолютную гробовую тишину комнаты (шипение лампы, капли, тиканье часов).
        /// </summary>
        public void SetTotalSilence(bool silent)
        {
            _isTotalSilence = silent;

            if (deskClock != null)
            {
                deskClock.SilenceTick = silent;
            }

            if (roomAmbientSource != null)
            {
                if (silent)
                {
                    roomAmbientSource.Pause();
                }
                else
                {
                    if (roomAmbientSource.clip == null) EnsureRoomAmbientAudio();
                    roomAmbientSource.volume = roomAmbientVolume;
                    if (!roomAmbientSource.isPlaying) roomAmbientSource.Play();
                }
            }
        }

        /// <summary>
        /// Мягкий далёкий разрыв: глухой звук, вздрагивание пламени лампы, лёгкая пыль, без сильной тряски камеры.
        /// </summary>
        public void TriggerSubtleDistantBoom(float dustCount = 8f)
        {
            PlayBoom(close: false);
            if (keroseneLampFlicker != null)
            {
                keroseneLampFlicker.TriggerFlameShudder(0.75f);
            }
            if (ceilingDustParticles != null && dustCount > 0f)
            {
                ceilingDustParticles.Emit(Mathf.RoundToInt(dustCount));
            }
        }

        /// <summary>
        /// Далёкий выстрел орудия -> пауза ~1.1с -> далёкий разрыв (Звонок 1.3 «Ёж» / «Гром»).
        /// </summary>
        public IEnumerator PlayArtilleryShotThenImpactRoutine()
        {
            EnsureFrontAudio();
            AudioClip shotClip = customArtilleryShotClip != null ? customArtilleryShotClip : ProceduralRadioAudio.GetDistantArtilleryShot();
            if (frontAudioSource != null && shotClip != null)
            {
                frontAudioSource.pitch = Random.Range(0.95f, 1.04f);
                frontAudioSource.PlayOneShot(shotClip, 0.50f);
            }

            yield return new WaitForSeconds(1.1f);
            TriggerSubtleDistantBoom(dustCount: 10f);
        }

        /// <summary>
        /// Вздрагивание пламени и осыпание пыли при сильном ударе (гибель «Берёзы»).
        /// </summary>
        public void TriggerBerezaImpactVisuals()
        {
            if (keroseneLampFlicker != null)
            {
                keroseneLampFlicker.TriggerFlameShudder(1.45f);
            }
            if (ceilingDustParticles != null)
            {
                ceilingDustParticles.Emit(32);
            }
            if (cameraLook != null)
            {
                cameraLook.AddTraumaShake(0.22f);
            }
        }

        public void PlayFrontOneShot(AudioClip clip, float volume = 0.8f, float pitch = 1f)
        {
            EnsureFrontAudio();
            if (frontAudioSource != null && clip != null)
            {
                frontAudioSource.pitch = pitch;
                frontAudioSource.PlayOneShot(clip, volume);
            }
        }

        private IEnumerator IgniteLampRoutine()
        {
            // Короткая задержка на звук чирканья спички перед вспышкой фитиля
            yield return new WaitForSecondsRealtime(0.26f);

            float t = 0f;
            const float dur = 0.85f;
            while (t < 1f)
            {
                t = Mathf.Clamp01(t + Time.unscaledDeltaTime / dur);
                float ease = t * t * (3f - 2f * t);
                if (keroseneLampFlicker != null)
                {
                    keroseneLampFlicker.IntensityMultiplier = ease;
                }
                if (roomWarmBounceLight != null)
                {
                    roomWarmBounceLight.intensity = _normalBounceIntensity * ease;
                }
                yield return null;
            }

            if (keroseneLampFlicker != null) keroseneLampFlicker.IntensityMultiplier = 1f;
            if (roomWarmBounceLight != null) roomWarmBounceLight.intensity = _normalBounceIntensity;

            if (!_isTotalSilence)
            {
                SetTotalSilence(false);
            }

            _lampIgniteCoroutine = null;
        }

        private void EnsureRoomAmbientAudio()
        {
            if (roomAmbientSource == null)
            {
                roomAmbientSource = gameObject.AddComponent<AudioSource>();
            }
            roomAmbientSource.playOnAwake = false;
            roomAmbientSource.loop = true;
            roomAmbientSource.spatialBlend = 0f;
            roomAmbientSource.volume = roomAmbientVolume;
            roomAmbientSource.clip = customRoomLampHissClip != null ? customRoomLampHissClip : ProceduralRadioAudio.GetRoomLampHissLoop();
            if (!_isTotalSilence && !roomAmbientSource.isPlaying)
            {
                roomAmbientSource.Play();
            }
        }

        private void PlayWaterDrop()
        {
            if (_isTotalSilence || frontAudioSource == null) return;
            AudioClip drop = customWaterDropClip != null ? customWaterDropClip : ProceduralRadioAudio.GetWaterDrop();
            if (drop != null)
            {
                frontAudioSource.pitch = Random.Range(0.88f, 1.16f);
                frontAudioSource.PlayOneShot(drop, waterDropVolume);
            }
        }

        [ContextMenu("Тест: далёкий разрыв")]
        public void TriggerDistantExplosion()
        {
            PlayBoom(close: false);
            if (keroseneLampFlicker != null)
            {
                keroseneLampFlicker.TriggerFlameShudder(0.85f);
            }
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
            if (keroseneLampFlicker != null)
            {
                keroseneLampFlicker.TriggerFlameShudder(1.6f);
            }
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
