using System.Collections.Generic;
using UnityEngine;

namespace LastSignal.StoryCore
{
    /// <summary>
    /// Компактный генератор процедурных фонов и SFX для эфира и подвала.
    /// Используется как качественный fallback, если в Inspector ещё не назначены готовые AudioClip.
    /// Все клипы кешируются один раз на время игровой сессии.
    /// </summary>
    public static class ProceduralRadioAudio
    {
        private static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearStaticCache()
        {
            Cache.Clear();
        }

        public static AudioClip GetRelayClick()
        {
            return GetOrCreate("Proc_RelayClick", () =>
            {
                const int sr = 44100;
                int count = Mathf.RoundToInt(sr * 0.065f);
                float[] data = new float[count];
                var rng = new System.Random(101);
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)sr;
                    float env1 = Mathf.Exp(-t * 140f);
                    float env2 = t > 0.014f ? Mathf.Exp(-(t - 0.014f) * 170f) * 0.65f : 0f;
                    float metal = Mathf.Sin(2f * Mathf.PI * 1950f * t) * 0.45f
                                + Mathf.Sin(2f * Mathf.PI * 3600f * t) * 0.35f;
                    float clickNoise = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.45f;
                    data[i] = Mathf.Clamp((metal + clickNoise) * (env1 + env2), -0.95f, 0.95f);
                }
                return CreateClip("Proc_RelayClick", data, sr);
            });
        }

        public static AudioClip GetMatchStrikeAndIgnite()
        {
            return GetOrCreate("Proc_MatchStrike", () =>
            {
                const int sr = 44100;
                float dur = 1.45f;
                int count = Mathf.RoundToInt(sr * dur);
                float[] data = new float[count];
                var rng = new System.Random(202);
                float lp = 0f, hp = 0f, prev = 0f;
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)sr;
                    float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lp += 0.32f * (w - lp);
                    hp = 0.78f * (hp + lp - prev);
                    prev = lp;

                    // 0.05..0.32s: scratch of match on box
                    float scratch = 0f;
                    if (t >= 0.05f && t <= 0.34f)
                    {
                        float u = (t - 0.05f) / 0.29f;
                        float env = Mathf.Sin(u * Mathf.PI);
                        scratch = hp * env * 0.85f;
                    }

                    // 0.30..1.35s: soft flare whoosh + kerosene wick catching
                    float whoosh = 0f;
                    if (t >= 0.28f)
                    {
                        float u = (t - 0.28f) / 1.15f;
                        float flareEnv = Mathf.Clamp01(u * 9f) * Mathf.Exp(-u * 3.2f);
                        float rumble = Mathf.Sin(2f * Mathf.PI * 95f * t) * 0.22f;
                        whoosh = (lp * 0.75f + rumble) * flareEnv * 0.9f;
                    }

                    data[i] = Mathf.Clamp(scratch + whoosh, -0.95f, 0.95f);
                }
                return CreateClip("Proc_MatchStrike", data, sr);
            });
        }

        public static AudioClip GetRoomLampHissLoop()
        {
            return GetOrCreate("Proc_RoomLampHiss", () =>
            {
                const int sr = 44100;
                int count = sr * 4;
                float[] data = new float[count];
                var rng = new System.Random(303);
                float lp1 = 0f, lp2 = 0f, hp = 0f, prev = 0f;
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)sr;
                    float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lp1 += 0.08f * (w - lp1);
                    lp2 += 0.04f * (lp1 - lp2);
                    hp = 0.88f * (hp + lp1 - prev);
                    prev = lp1;

                    // Subtle low basement hum + kerosene wick hiss
                    float hum = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.08f;
                    float hiss = hp * 0.28f + lp2 * 0.25f;
                    data[i] = Mathf.Clamp(hum + hiss, -0.6f, 0.6f);
                }
                ApplyLoopCrossfade(data, 2048);
                return CreateClip("Proc_RoomLampHiss", data, sr);
            });
        }

        public static AudioClip GetWaterDrop()
        {
            return GetOrCreate("Proc_WaterDrop", () =>
            {
                const int sr = 44100;
                int count = Mathf.RoundToInt(sr * 0.16f);
                float[] data = new float[count];
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)sr;
                    float u = t / 0.16f;
                    float freq = Mathf.Lerp(920f, 1680f, u * u);
                    float env = Mathf.Clamp01(u * 25f) * Mathf.Exp(-u * 9.5f);
                    data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.55f;
                }
                return CreateClip("Proc_WaterDrop", data, sr);
            });
        }

        public static AudioClip GetDistantArtilleryShot()
        {
            return GetOrCreate("Proc_ArtilleryShot", () =>
            {
                const int sr = 44100;
                int count = Mathf.RoundToInt(sr * 0.95f);
                float[] data = new float[count];
                var rng = new System.Random(404);
                float lp1 = 0f, lp2 = 0f;
                for (int i = 0; i < count; i++)
                {
                    float u = i / (float)count;
                    float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lp1 += 0.12f * (w - lp1);
                    lp2 += 0.08f * (lp1 - lp2);
                    float punch = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(115f, 38f, u) * (i / (float)sr));
                    float env = Mathf.Clamp01(u * 80f) * Mathf.Exp(-u * 6.5f);
                    data[i] = Mathf.Clamp((lp2 * 2.4f + punch * 0.55f) * env, -0.95f, 0.95f);
                }
                return CreateClip("Proc_ArtilleryShot", data, sr);
            });
        }

        public static AudioClip GetIncomingShellWhistle(float duration = 1.15f)
        {
            return GetOrCreate($"Proc_ShellWhistle_{Mathf.RoundToInt(duration * 100)}", () =>
            {
                const int sr = 44100;
                int count = Mathf.RoundToInt(sr * Mathf.Max(0.4f, duration));
                float[] data = new float[count];
                var rng = new System.Random(505);
                float phase = 0f;
                float lp = 0f;
                for (int i = 0; i < count; i++)
                {
                    float u = i / (float)count;
                    // Rising intensity, falling Doppler pitch
                    float freq = Mathf.Lerp(1450f, 520f, u * u);
                    phase += 2f * Mathf.PI * freq / sr;
                    float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lp += 0.25f * (w - lp);
                    float env = Mathf.Pow(u, 1.45f) * Mathf.Clamp01((1f - u) * 35f);
                    float tone = Mathf.Sin(phase) * 0.55f + Mathf.Sin(phase * 2.01f) * 0.18f;
                    data[i] = Mathf.Clamp((tone + lp * 0.35f) * env * 0.9f, -0.95f, 0.95f);
                }
                return CreateClip("Proc_ShellWhistle", data, sr);
            });
        }

        public static AudioClip GetImpactAndRadioCrackle()
        {
            return GetOrCreate("Proc_ImpactRadioCrackle", () =>
            {
                const int sr = 44100;
                int count = Mathf.RoundToInt(sr * 1.9f);
                float[] data = new float[count];
                var rng = new System.Random(606);
                float lp1 = 0f, lp2 = 0f;
                for (int i = 0; i < count; i++)
                {
                    float u = i / (float)count;
                    float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lp1 += 0.16f * (w - lp1);
                    lp2 += 0.10f * (lp1 - lp2);
                    float sub = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(68f, 24f, u) * (i / (float)sr));
                    float crackle = rng.NextDouble() < 0.015 ? (float)(rng.NextDouble() * 2.0 - 1.0) * 0.85f : 0f;
                    float boomEnv = Mathf.Clamp01(u * 120f) * Mathf.Exp(-u * 2.8f);
                    float staticEnv = Mathf.Exp(-u * 1.3f);
                    data[i] = Mathf.Clamp((lp2 * 3.0f + sub * 0.7f) * boomEnv + (w * 0.35f + crackle) * staticEnv * 0.45f, -0.98f, 0.98f);
                }
                return CreateClip("Proc_ImpactRadioCrackle", data, sr);
            });
        }

        public static AudioClip GetWhiteNoiseLoop()
        {
            return GetOrCreate("Proc_WhiteNoiseLoop", () =>
            {
                const int sr = 44100;
                int count = sr * 2;
                float[] data = new float[count];
                var rng = new System.Random(707);
                float lp = 0f, hp = 0f, prev = 0f;
                for (int i = 0; i < count; i++)
                {
                    float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lp += 0.42f * (w - lp);
                    hp = 0.82f * (hp + lp - prev);
                    prev = lp;
                    float pop = rng.NextDouble() < 0.002 ? (float)(rng.NextDouble() * 2.0 - 1.0) * 0.6f : 0f;
                    data[i] = Mathf.Clamp(hp * 0.75f + pop, -0.92f, 0.92f);
                }
                ApplyLoopCrossfade(data, 1024);
                return CreateClip("Proc_WhiteNoiseLoop", data, sr);
            });
        }

        public static AudioClip GetCallerAmbient(SubscriberAmbientProfile profile)
        {
            if (profile == SubscriberAmbientProfile.None)
            {
                return null;
            }

            return GetOrCreate($"Proc_Ambient_{profile}", () => BuildCallerAmbientClip(profile));
        }

        private static AudioClip BuildCallerAmbientClip(SubscriberAmbientProfile profile)
        {
            const int sr = 44100;
            const float dur = 4.0f;
            int count = Mathf.RoundToInt(sr * dur);
            float[] data = new float[count];
            var rng = new System.Random(1000 + (int)profile * 97);
            float lp = 0f, lp2 = 0f, hp = 0f, prev = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)sr;
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.14f * (w - lp);
                lp2 += 0.04f * (lp - lp2);
                hp = 0.86f * (hp + lp - prev);
                prev = lp;

                float sample = 0f;
                switch (profile)
                {
                    case SubscriberAmbientProfile.CentralHub:
                        // Large switchboard hum + faint electrical chatter
                        float hubHum = Mathf.Sin(2f * Mathf.PI * 100f * t) * 0.14f
                                     + Mathf.Sin(2f * Mathf.PI * 150f * t) * 0.07f;
                        float chatterMod = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 1.7f * t) * Mathf.Sin(2f * Mathf.PI * 3.1f * t);
                        sample = hubHum + hp * (0.20f + 0.18f * chatterMod) + lp2 * 0.25f;
                        break;

                    case SubscriberAmbientProfile.TruckIdleDepot:
                        // Idling diesel engine pulse (~11 Hz) + subtle metallic clatter
                        float engine = Mathf.Sin(2f * Mathf.PI * 44f * t) * (0.65f + 0.35f * Mathf.Sin(2f * Mathf.PI * 11f * t));
                        float clank = (Mathf.Repeat(t, 1.35f) < 0.04f) ? hp * 0.65f : 0f;
                        sample = engine * 0.24f + lp2 * 0.32f + clank + hp * 0.08f;
                        break;

                    case SubscriberAmbientProfile.StaffClockMap:
                        // Quiet HQ room + steady clock tick every 0.5s + paper rustle
                        float tickPhase = Mathf.Repeat(t, 0.5f);
                        float tick = tickPhase < 0.015f ? Mathf.Sin(2f * Mathf.PI * 2200f * tickPhase) * Mathf.Exp(-tickPhase * 180f) * 0.42f : 0f;
                        float rustle = (t > 1.4f && t < 1.9f) ? hp * Mathf.Sin((t - 1.4f) / 0.5f * Mathf.PI) * 0.28f : 0f;
                        sample = lp2 * 0.14f + tick + rustle + hp * 0.06f;
                        break;

                    case SubscriberAmbientProfile.WindDistantShots:
                        // Open hillside wind gusts + rare distant rifle pops
                        float gust = 0.45f + 0.55f * Mathf.Sin(2f * Mathf.PI * 0.35f * t);
                        float shot = (Mathf.Abs(t - 1.2f) < 0.06f || Mathf.Abs(t - 3.1f) < 0.06f) ? lp * 0.9f : 0f;
                        sample = lp * 0.35f * gust + hp * 0.18f * gust + shot;
                        break;

                    case SubscriberAmbientProfile.ArtilleryBattery:
                        // Low rumble + occasional brass shell casing clink
                        float clinkPhase = Mathf.Repeat(t + 0.7f, 1.8f);
                        float clink = clinkPhase < 0.09f
                            ? (Mathf.Sin(2f * Mathf.PI * 3100f * clinkPhase) + Mathf.Sin(2f * Mathf.PI * 4350f * clinkPhase)) * 0.16f * Mathf.Exp(-clinkPhase * 32f)
                            : 0f;
                        sample = lp2 * 0.42f + hp * 0.16f + clink;
                        break;

                    case SubscriberAmbientProfile.NightForestCrickets:
                        // Quiet night breeze + soft 4.2kHz cricket chirps
                        float chirpCycle = Mathf.Repeat(t, 0.65f);
                        float chirpEnv = chirpCycle < 0.18f ? Mathf.Sin(chirpCycle / 0.18f * Mathf.PI) * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 38f * chirpCycle)) : 0f;
                        float cricket = Mathf.Sin(2f * Mathf.PI * 4100f * t) * chirpEnv * 0.14f;
                        sample = lp2 * 0.14f + hp * 0.08f + cricket;
                        break;

                    case SubscriberAmbientProfile.MedSanBat:
                        // Soft muffled tent murmur + occasional glass/metal tray clink
                        float trayPhase = Mathf.Repeat(t + 0.4f, 2.1f);
                        float tray = trayPhase < 0.07f ? Mathf.Sin(2f * Mathf.PI * 3400f * trayPhase) * Mathf.Exp(-trayPhase * 45f) * 0.20f : 0f;
                        sample = lp2 * 0.28f + hp * 0.10f + tray;
                        break;

                    case SubscriberAmbientProfile.BridgeWaterMetal:
                        // Flowing river water under bridge + occasional metallic knock
                        float river = lp * 0.30f + hp * 0.22f;
                        float knockPhase = Mathf.Repeat(t + 1.1f, 2.3f);
                        float knock = knockPhase < 0.05f ? Mathf.Sin(2f * Mathf.PI * 680f * knockPhase) * Mathf.Exp(-knockPhase * 65f) * 0.35f : 0f;
                        sample = river + knock;
                        break;
                }

                data[i] = Mathf.Clamp(sample, -0.85f, 0.85f);
            }

            ApplyLoopCrossfade(data, 2048);
            return CreateClip($"Proc_Ambient_{profile}", data, sr);
        }

        private static void ApplyLoopCrossfade(float[] data, int fadeSamples)
        {
            int n = Mathf.Min(fadeSamples, data.Length / 4);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                int tailIdx = data.Length - n + i;
                data[i] = Mathf.Lerp(data[tailIdx], data[i], t);
            }
        }

        private static AudioClip GetOrCreate(string key, System.Func<AudioClip> factory)
        {
            if (Cache.TryGetValue(key, out AudioClip existing) && existing != null)
            {
                return existing;
            }

            AudioClip created = factory();
            Cache[key] = created;
            return created;
        }

        private static AudioClip CreateClip(string clipName, float[] samples, int sampleRate)
        {
            AudioClip clip = AudioClip.Create(clipName, samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
