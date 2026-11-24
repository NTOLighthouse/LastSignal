using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastSignal.Switchboard
{
    /// <summary>
    /// Главный аппаратный фасад коммутатора: управляет 10 линиями (лампа + гнездо) и 2 штекерами.
    /// Не знает ничего про сюжет и реплики — только про состояние железа и рассылку событий.
    /// </summary>
    public sealed class SwitchboardController : MonoBehaviour
    {
        [Header("Аппаратные элементы коммутатора")]
        [SerializeField] private List<SwitchboardChannel> channels = new List<SwitchboardChannel>();
        [SerializeField] private List<SwitchboardPlug> plugs = new List<SwitchboardPlug>();

        [Header("Звуки механики (опционально)")]
        [SerializeField] private AudioSource sfxAudioSource;
        [SerializeField] private AudioClip plugInClip;
        [SerializeField] private AudioClip plugOutClip;
        [SerializeField] private bool generateFallbackMechanicalSfx = true;

        [Header("Материалы для автопривязки")]
        [SerializeField] private Material defaultLampOffMaterial;
        [SerializeField] private Material defaultLampLitMaterial;
        [SerializeField] private Material defaultCordMaterial;

        private AudioClip _proceduralPlugInClip;
        private AudioClip _proceduralPlugOutClip;
        private AudioClip _proceduralRingTickClip;

        private readonly Dictionary<SwitchboardChannelId, SwitchboardChannel> _channelMap =
            new Dictionary<SwitchboardChannelId, SwitchboardChannel>();

        private SwitchboardPlug _heldPlug;
        private (SwitchboardChannelId first, SwitchboardChannelId second) _activeBridge =
            (SwitchboardChannelId.None, SwitchboardChannelId.None);

        /// <summary>Срабатывает, когда штекер вставлен в указанный канал.</summary>
        public event Action<SwitchboardChannelId, SwitchboardPlug> OnChannelPlugged;

        /// <summary>Срабатывает, когда штекер выдернут из указанного канала.</summary>
        public event Action<SwitchboardChannelId, SwitchboardPlug> OnChannelUnplugged;

        /// <summary>Срабатывает, когда оба штекера вставлены в два разных канала (мост установлен).</summary>
        public event Action<SwitchboardChannelId, SwitchboardChannelId> OnBridgeConnected;

        /// <summary>Срабатывает, когда существующий мост между двумя каналами разорван.</summary>
        public event Action<SwitchboardChannelId, SwitchboardChannelId> OnBridgeDisconnected;

        public IReadOnlyList<SwitchboardChannel> Channels => channels;
        public IReadOnlyList<SwitchboardPlug> Plugs => plugs;
        public SwitchboardPlug HeldPlug => _heldPlug;
        public bool HasActiveBridge => _activeBridge.first != SwitchboardChannelId.None &&
                                       _activeBridge.second != SwitchboardChannelId.None;

        private void Awake()
        {
            EnsureSfxAudioSource();
            RebuildChannelMap();
        }

        private void OnDestroy()
        {
            DestroyProceduralClip(ref _proceduralPlugInClip);
            DestroyProceduralClip(ref _proceduralPlugOutClip);
            DestroyProceduralClip(ref _proceduralRingTickClip);
        }

        private void Start()
        {
            // При старте гасим все лампы (если сценарий сам не зажёг нужные).
            foreach (SwitchboardChannel channel in channels)
            {
                if (channel != null && channel.LampState == LampState.Off)
                {
                    channel.ForceSetLampState(LampState.Off);
                }
            }
        }

        #region Управление лампами

        public void SetLampState(SwitchboardChannelId channelId, LampState state)
        {
            if (TryGetChannel(channelId, out SwitchboardChannel channel))
            {
                channel.SetLampState(state);
            }
        }

        public void ForceSetLampState(SwitchboardChannelId channelId, LampState state)
        {
            if (TryGetChannel(channelId, out SwitchboardChannel channel))
            {
                channel.ForceSetLampState(state);
            }
        }

        public void MarkChannelDead(SwitchboardChannelId channelId)
        {
            if (TryGetChannel(channelId, out SwitchboardChannel channel))
            {
                if (channel.IsOccupied)
                {
                    TryUnplugChannel(channelId);
                }
                channel.ForceSetLampState(LampState.Dead);
            }
        }

        public void SetAllLampsOff(bool keepDeadChannels = true)
        {
            foreach (SwitchboardChannel channel in channels)
            {
                if (channel == null)
                {
                    continue;
                }

                if (keepDeadChannels && channel.IsDead)
                {
                    continue;
                }

                channel.ForceSetLampState(LampState.Off);
            }
        }

        #endregion

        #region Запросы состояния

        public bool TryGetChannel(SwitchboardChannelId channelId, out SwitchboardChannel channel)
        {
            if (_channelMap.Count == 0 && channels.Count > 0)
            {
                RebuildChannelMap();
            }

            return _channelMap.TryGetValue(channelId, out channel);
        }

        public bool IsChannelPlugged(SwitchboardChannelId channelId)
        {
            return TryGetChannel(channelId, out SwitchboardChannel channel) && channel.IsOccupied;
        }

        public int GetPluggedCount()
        {
            int count = 0;
            for (int i = 0; i < plugs.Count; i++)
            {
                if (plugs[i] != null && plugs[i].State == PlugState.Plugged)
                {
                    count++;
                }
            }
            return count;
        }

        public bool AreChannelsBridged(SwitchboardChannelId a, SwitchboardChannelId b)
        {
            if (!HasActiveBridge || a == SwitchboardChannelId.None || b == SwitchboardChannelId.None)
            {
                return false;
            }

            return (_activeBridge.first == a && _activeBridge.second == b) ||
                   (_activeBridge.first == b && _activeBridge.second == a);
        }

        public bool TryGetOtherBridgedChannel(SwitchboardChannelId from, out SwitchboardChannelId other)
        {
            if (HasActiveBridge)
            {
                if (_activeBridge.first == from)
                {
                    other = _activeBridge.second;
                    return true;
                }

                if (_activeBridge.second == from)
                {
                    other = _activeBridge.first;
                    return true;
                }
            }

            other = SwitchboardChannelId.None;
            return false;
        }

        #endregion

        #region Взаимодействие со штекерами и гнёздами

        /// <summary>
        /// Удобное взаимодействие кликом по гнезду:
        /// если в гнезде есть штекер — выдёргивает его; если пусто — вставляет выбранный или первый свободный.
        /// </summary>
        public bool TryInteractWithChannel(SwitchboardChannelId channelId)
        {
            if (!TryGetChannel(channelId, out SwitchboardChannel channel))
            {
                return false;
            }

            if (channel.IsOccupied)
            {
                return TryUnplugChannel(channelId);
            }

            return TryPlugChannel(channelId, _heldPlug);
        }

        /// <summary>
        /// Взаимодействие кликом непосредственно по штекеру:
        /// если он вставлен — выдёргивает в док; если в доке — берёт в руку (или опускает обратно).
        /// </summary>
        public bool TryInteractWithPlug(SwitchboardPlug plug)
        {
            if (plug == null)
            {
                return false;
            }

            if (plug.State == PlugState.Plugged && plug.ConnectedChannel != null)
            {
                return TryUnplugChannel(plug.ConnectedChannel.ChannelId);
            }

            if (_heldPlug == plug)
            {
                ReleaseHeldPlug();
                return true;
            }

            ReleaseHeldPlug();
            _heldPlug = plug;
            _heldPlug.MoveToHeld();
            return true;
        }

        public void ReleaseHeldPlug()
        {
            if (_heldPlug != null && _heldPlug.State == PlugState.Held)
            {
                _heldPlug.MoveToDock();
            }
            _heldPlug = null;
        }

        public bool TryPlugChannel(SwitchboardChannelId channelId, SwitchboardPlug preferredPlug = null)
        {
            if (!TryGetChannel(channelId, out SwitchboardChannel channel))
            {
                return false;
            }

            if (channel.IsOccupied || channel.IsDead)
            {
                return false;
            }

            SwitchboardPlug plug = ResolveFreePlug(preferredPlug);
            if (plug == null)
            {
                return false;
            }

            if (_heldPlug == plug)
            {
                _heldPlug = null;
            }

            channel.AttachPlug(plug);
            plug.MoveToChannel(channel);
            PlayPlugInSfx();

            UpdateBridgeStateInternal(out var brokenBridge, out var newBridge);
            OnChannelPlugged?.Invoke(channelId, plug);
            if (brokenBridge.first != SwitchboardChannelId.None)
            {
                OnBridgeDisconnected?.Invoke(brokenBridge.first, brokenBridge.second);
            }
            if (newBridge.first != SwitchboardChannelId.None)
            {
                OnBridgeConnected?.Invoke(newBridge.first, newBridge.second);
            }
            return true;
        }

        public bool TryUnplugChannel(SwitchboardChannelId channelId)
        {
            if (!TryGetChannel(channelId, out SwitchboardChannel channel) || !channel.IsOccupied)
            {
                return false;
            }

            SwitchboardPlug plug = channel.ConnectedPlug;
            channel.DetachPlug();
            plug.MoveToDock();
            PlayPlugOutSfx();

            UpdateBridgeStateInternal(out var brokenBridge, out var newBridge);
            if (brokenBridge.first != SwitchboardChannelId.None)
            {
                OnBridgeDisconnected?.Invoke(brokenBridge.first, brokenBridge.second);
            }
            OnChannelUnplugged?.Invoke(channelId, plug);
            if (newBridge.first != SwitchboardChannelId.None)
            {
                OnBridgeConnected?.Invoke(newBridge.first, newBridge.second);
            }
            return true;
        }

        public void PlayIncomingRingPulse(float volume = 0.22f)
        {
            EnsureSfxAudioSource();
            if (sfxAudioSource == null || !generateFallbackMechanicalSfx)
            {
                return;
            }

            if (_proceduralRingTickClip == null)
            {
                _proceduralRingTickClip = CreateProceduralClickClip("SB_RelayPulse", 0.055f, 880f, 420f, 0.35f);
            }

            sfxAudioSource.PlayOneShot(_proceduralRingTickClip, volume);
        }

        public void UnplugAll()
        {
            ReleaseHeldPlug();
            for (int i = 0; i < channels.Count; i++)
            {
                if (channels[i] != null && channels[i].IsOccupied)
                {
                    TryUnplugChannel(channels[i].ChannelId);
                }
            }
        }

        #endregion

        #region Автонастройка по иерархии Mayak_FullScene

        [ContextMenu("Автопривязка элементов коммутатора")]
        public void AutoBindFromHierarchy()
        {
            channels.Clear();
            plugs.Clear();

            Transform[] allTransforms = GetComponentsInChildren<Transform>(true);

            // 1. Привязываем 10 гнёзд и 10 лампочек (01..10).
            for (int i = 1; i <= 10; i++)
            {
                string suffix = i.ToString("00");
                Transform jackTransform = FindTransformByPrefix(allTransforms, $"Switchboard_Jack_{suffix}");
                Transform bulbTransform = FindTransformByPrefix(allTransforms, $"Switchboard_LampBulb_{suffix}");

                if (jackTransform == null)
                {
                    Debug.LogWarning($"[Switchboard] Не найден объект Switchboard_Jack_{suffix} внутри {name}.");
                    continue;
                }

                SwitchboardLamp lamp = null;
                if (bulbTransform != null)
                {
                    lamp = bulbTransform.GetComponent<SwitchboardLamp>();
                    if (lamp == null)
                    {
                        lamp = bulbTransform.gameObject.AddComponent<SwitchboardLamp>();
                    }

                    Renderer bulbRenderer = bulbTransform.GetComponent<Renderer>();
                    lamp.Configure(bulbRenderer, defaultLampOffMaterial, defaultLampLitMaterial);
                }

                SwitchboardChannel channel = jackTransform.GetComponent<SwitchboardChannel>();
                if (channel == null)
                {
                    channel = jackTransform.gameObject.AddComponent<SwitchboardChannel>();
                }

                channel.Configure((SwitchboardChannelId)i, lamp);
                EnsureClickableCollider(jackTransform.gameObject, new Vector3(0.03f, 0.04f, 0.04f));
                channels.Add(channel);
            }

            // 2. Привязываем 2 штекера и 2 дока (01..02).
            for (int p = 1; p <= 2; p++)
            {
                string suffix = p.ToString("00");
                Transform plugTransform = FindTransformByPrefix(allTransforms, $"Switchboard_Plug_{suffix}");
                Transform dockTransform = FindTransformByPrefix(allTransforms, $"Switchboard_Dock_{suffix}");

                if (plugTransform == null)
                {
                    Debug.LogWarning($"[Switchboard] Не найден объект Switchboard_Plug_{suffix} внутри {name}.");
                    continue;
                }

                SwitchboardPlug plug = plugTransform.GetComponent<SwitchboardPlug>();
                if (plug == null)
                {
                    plug = plugTransform.gameObject.AddComponent<SwitchboardPlug>();
                }

                plug.Configure(p, dockTransform);
                EnsureClickableCollider(plugTransform.gameObject, new Vector3(0.035f, 0.035f, 0.11f));
                plugs.Add(plug);
            }

            RebuildChannelMap();
            Debug.Log($"[Switchboard] Автопривязка завершена: каналов = {channels.Count}, штекеров = {plugs.Count}.");
        }

        public void SetDefaultLampMaterials(Material offMaterial, Material litMaterial)
        {
            defaultLampOffMaterial = offMaterial;
            defaultLampLitMaterial = litMaterial;
        }

        #endregion

        private void RebuildChannelMap()
        {
            _channelMap.Clear();
            for (int i = 0; i < channels.Count; i++)
            {
                SwitchboardChannel ch = channels[i];
                if (ch != null && ch.ChannelId != SwitchboardChannelId.None)
                {
                    _channelMap[ch.ChannelId] = ch;
                }
            }
        }

        private SwitchboardPlug ResolveFreePlug(SwitchboardPlug preferred)
        {
            if (preferred != null && preferred.IsFree)
            {
                return preferred;
            }

            for (int i = 0; i < plugs.Count; i++)
            {
                if (plugs[i] != null && plugs[i].IsFree)
                {
                    return plugs[i];
                }
            }

            return null;
        }

        private void UpdateBridgeStateInternal(
            out (SwitchboardChannelId first, SwitchboardChannelId second) brokenBridge,
            out (SwitchboardChannelId first, SwitchboardChannelId second) newBridge)
        {
            brokenBridge = (SwitchboardChannelId.None, SwitchboardChannelId.None);
            newBridge = (SwitchboardChannelId.None, SwitchboardChannelId.None);

            SwitchboardChannelId first = SwitchboardChannelId.None;
            SwitchboardChannelId second = SwitchboardChannelId.None;

            for (int i = 0; i < plugs.Count; i++)
            {
                SwitchboardPlug plug = plugs[i];
                if (plug != null && plug.State == PlugState.Plugged && plug.ConnectedChannel != null)
                {
                    if (first == SwitchboardChannelId.None)
                    {
                        first = plug.ConnectedChannel.ChannelId;
                    }
                    else if (second == SwitchboardChannelId.None && plug.ConnectedChannel.ChannelId != first)
                    {
                        second = plug.ConnectedChannel.ChannelId;
                    }
                }
            }

            bool hadBridge = HasActiveBridge;
            bool nowHasBridge = first != SwitchboardChannelId.None && second != SwitchboardChannelId.None;

            if (hadBridge && (!nowHasBridge || !IsSamePair(_activeBridge, (first, second))))
            {
                brokenBridge = _activeBridge;
                _activeBridge = (SwitchboardChannelId.None, SwitchboardChannelId.None);
            }

            if (nowHasBridge && (!hadBridge || !IsSamePair(_activeBridge, (first, second))))
            {
                _activeBridge = (first, second);
                newBridge = _activeBridge;
            }
        }

        private static bool IsSamePair(
            (SwitchboardChannelId a, SwitchboardChannelId b) left,
            (SwitchboardChannelId a, SwitchboardChannelId b) right)
        {
            return (left.a == right.a && left.b == right.b) ||
                   (left.a == right.b && left.b == right.a);
        }

        private void EnsureSfxAudioSource()
        {
            if (sfxAudioSource == null)
            {
                sfxAudioSource = GetComponent<AudioSource>();
            }

            if (sfxAudioSource == null)
            {
                sfxAudioSource = gameObject.AddComponent<AudioSource>();
            }

            sfxAudioSource.playOnAwake = false;
            sfxAudioSource.loop = false;
            sfxAudioSource.spatialBlend = 0f;
        }

        private void PlayPlugInSfx()
        {
            if (plugInClip != null)
            {
                PlaySfx(plugInClip);
                return;
            }

            if (!generateFallbackMechanicalSfx)
            {
                return;
            }

            if (_proceduralPlugInClip == null)
            {
                _proceduralPlugInClip = CreateProceduralClickClip("SB_PlugIn", 0.065f, 480f, 1160f, 0.42f);
            }

            PlaySfx(_proceduralPlugInClip);
        }

        private void PlayPlugOutSfx()
        {
            if (plugOutClip != null)
            {
                PlaySfx(plugOutClip);
                return;
            }

            if (!generateFallbackMechanicalSfx)
            {
                return;
            }

            if (_proceduralPlugOutClip == null)
            {
                _proceduralPlugOutClip = CreateProceduralClickClip("SB_PlugOut", 0.055f, 920f, 340f, 0.36f);
            }

            PlaySfx(_proceduralPlugOutClip);
        }

        private void PlaySfx(AudioClip clip, float volume = 1f)
        {
            EnsureSfxAudioSource();
            if (sfxAudioSource != null && clip != null)
            {
                sfxAudioSource.PlayOneShot(clip, volume);
            }
        }

        private static AudioClip CreateProceduralClickClip(
            string clipName,
            float durationSeconds,
            float startFreq,
            float endFreq,
            float peakAmplitude)
        {
            const int sampleRate = 22050;
            int sampleCount = Mathf.Max(64, Mathf.RoundToInt(sampleRate * durationSeconds));
            float[] samples = new float[sampleCount];

            float phase = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleCount;
                float freq = Mathf.Lerp(startFreq, endFreq, t);
                phase += (2f * Mathf.PI * freq) / sampleRate;

                float env = Mathf.Exp(-t * 9.5f) * Mathf.Clamp01(t * 40f);
                float click = Mathf.Sin(phase) * 0.75f + Mathf.Sin(phase * 2.7f) * 0.25f;
                samples[i] = Mathf.Clamp(click * env * peakAmplitude, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static void DestroyProceduralClip(ref AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(clip);
            }
            else
            {
                DestroyImmediate(clip);
            }

            clip = null;
        }

        private static Transform FindTransformByPrefix(Transform[] transforms, string prefix)
        {
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return transforms[i];
                }
            }
            return null;
        }

        private static void EnsureClickableCollider(GameObject target, Vector3 minSize)
        {
            if (target.GetComponent<Collider>() != null)
            {
                return;
            }

            BoxCollider box = target.AddComponent<BoxCollider>();
            MeshFilter mf = target.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Bounds bounds = mf.sharedMesh.bounds;
                box.center = bounds.center;
                box.size = new Vector3(
                    Mathf.Max(bounds.size.x, minSize.x),
                    Mathf.Max(bounds.size.y, minSize.y),
                    Mathf.Max(bounds.size.z, minSize.z));
            }
            else
            {
                box.size = minSize;
            }
        }
    }
}
