using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastSignal.StoryCore
{
    /// <summary>
    /// Записанное игроком ключевое слово из субтитров (координаты, позывной гонца, улика).
    /// </summary>
    [Serializable]
    public sealed class RecordedKeywordEntry
    {
        public string keywordId;
        public string displayText;
        public int nightRecorded;
    }

    /// <summary>
    /// Центральное хранилище прогресса прохождения: 8 сюжетных флагов из ГДД,
    /// записанные из субтитров ключевые слова и сохранение контрольной точки между ночами.
    /// Все поля открыты в Inspector для удобной отладки любых веток и концовок.
    /// </summary>
    public sealed class StoryFlagsState : MonoBehaviour
    {
        private const string SaveKeyPrefix = "LastSignal_NightCheckpoint_";

        [Header("Текущий прогресс смены")]
        [SerializeField, Range(0, 3)] private int currentNight = 1;
        [SerializeField] private bool reservePasswordTableUnlocked = false;
        [SerializeField] private bool berezaDestroyed = false;

        [Header("8 сюжетных флагов (ГДД)")]
        [Tooltip("Спас «Тополя» в ночь 2 — его рота держит заслон на дороге.")]
        [SerializeField] private bool topolSaved;

        [Tooltip("Уничтожил танки в овраге (18-В) в ночь 2 — к мосту идёт меньше машин.")]
        [SerializeField] private bool tanksDestroyed;

        [Tooltip("«Сорока» (школа в 18-Б) жива — влияет на эпилог.")]
        [SerializeField] private bool sorokaAlive = true;

        [Tooltip("«Ромашка» предупреждена об ударе в ночь 2 — влияет на её реплики.")]
        [SerializeField] private bool romashkaWarned;

        [Tooltip("О звонке «Зимы» доложено Кобре в ночь 2 — влияет на реплики штаба.")]
        [SerializeField] private bool zimaReported;

        [Tooltip("Адрес «Маяка» НЕ назван фальшивому гонцу «Скворцу» в ночь 2.")]
        [SerializeField] private bool addressNotRevealed;

        [Tooltip("Понял условный сигнал «Шила» («погода лётная») в ночь 3 — батальон цел.")]
        [SerializeField] private bool shiloSignalUnderstood;

        [Tooltip("Поверил «Зиме» в ночь 3 — открывает вариант «мост не трогать».")]
        [SerializeField] private bool trustedZima;

        [Header("Записанные слова из субтитров (оперативный журнал)")]
        [SerializeField] private List<RecordedKeywordEntry> recordedKeywords = new List<RecordedKeywordEntry>();

        [Header("Завершённые звонки текущей сессии")]
        [SerializeField] private List<string> completedCallIds = new List<string>();

        public int CurrentNight => currentNight;
        public bool ReservePasswordTableUnlocked => reservePasswordTableUnlocked;
        public bool BerezaDestroyed => berezaDestroyed;
        public IReadOnlyList<RecordedKeywordEntry> RecordedKeywords => recordedKeywords;
        public IReadOnlyList<string> CompletedCallIds => completedCallIds;

        public event Action<StoryFlag, bool> OnFlagChanged;
        public event Action<RecordedKeywordEntry> OnKeywordRecorded;
        public event Action OnStateResetOrLoaded;

        [Serializable]
        private sealed class CheckpointSaveData
        {
            public int night;
            public bool reserveUnlocked;
            public bool berezaDead;
            public bool topolSaved;
            public bool tanksDestroyed;
            public bool sorokaAlive;
            public bool romashkaWarned;
            public bool zimaReported;
            public bool addressNotRevealed;
            public bool shiloSignalUnderstood;
            public bool trustedZima;
            public List<RecordedKeywordEntry> keywords = new List<RecordedKeywordEntry>();
        }

        public bool GetFlag(StoryFlag flag)
        {
            return flag switch
            {
                StoryFlag.TopolSaved => topolSaved,
                StoryFlag.TanksDestroyed => tanksDestroyed,
                StoryFlag.SorokaAlive => sorokaAlive,
                StoryFlag.RomashkaWarned => romashkaWarned,
                StoryFlag.ZimaReported => zimaReported,
                StoryFlag.AddressNotRevealed => addressNotRevealed,
                StoryFlag.ShiloSignalUnderstood => shiloSignalUnderstood,
                StoryFlag.TrustedZima => trustedZima,
                _ => false
            };
        }

        public void SetFlag(StoryFlag flag, bool value)
        {
            if (flag == StoryFlag.None)
            {
                return;
            }

            if (GetFlag(flag) == value)
            {
                return;
            }

            switch (flag)
            {
                case StoryFlag.TopolSaved: topolSaved = value; break;
                case StoryFlag.TanksDestroyed: tanksDestroyed = value; break;
                case StoryFlag.SorokaAlive: sorokaAlive = value; break;
                case StoryFlag.RomashkaWarned: romashkaWarned = value; break;
                case StoryFlag.ZimaReported: zimaReported = value; break;
                case StoryFlag.AddressNotRevealed: addressNotRevealed = value; break;
                case StoryFlag.ShiloSignalUnderstood: shiloSignalUnderstood = value; break;
                case StoryFlag.TrustedZima: trustedZima = value; break;
            }

            OnFlagChanged?.Invoke(flag, value);
        }

        public void SetCurrentNight(int night)
        {
            currentNight = Mathf.Clamp(night, 0, 3);
        }

        public void UnlockReservePasswordTable()
        {
            if (reservePasswordTableUnlocked)
            {
                return;
            }

            reservePasswordTableUnlocked = true;
            OnStateResetOrLoaded?.Invoke();
        }

        public void MarkBerezaDestroyed()
        {
            if (berezaDestroyed)
            {
                return;
            }

            berezaDestroyed = true;
            OnStateResetOrLoaded?.Invoke();
        }

        public bool RecordKeyword(string keywordId, string displayText)
        {
            if (string.IsNullOrWhiteSpace(keywordId))
            {
                return false;
            }

            string normalizedId = keywordId.Trim();
            if (HasKeyword(normalizedId))
            {
                return false;
            }

            var entry = new RecordedKeywordEntry
            {
                keywordId = normalizedId,
                displayText = string.IsNullOrWhiteSpace(displayText) ? normalizedId : displayText.Trim(),
                nightRecorded = currentNight
            };

            recordedKeywords.Add(entry);
            OnKeywordRecorded?.Invoke(entry);
            return true;
        }

        public bool HasKeyword(string keywordId)
        {
            if (string.IsNullOrWhiteSpace(keywordId))
            {
                return true;
            }

            string trimmed = keywordId.Trim();
            for (int i = 0; i < recordedKeywords.Count; i++)
            {
                if (string.Equals(recordedKeywords[i].keywordId, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public void MarkCallCompleted(string callId)
        {
            if (string.IsNullOrWhiteSpace(callId) || completedCallIds.Contains(callId))
            {
                return;
            }

            completedCallIds.Add(callId);
        }

        public bool IsCallCompleted(string callId)
        {
            return !string.IsNullOrWhiteSpace(callId) && completedCallIds.Contains(callId);
        }

        /// <summary>
        /// Правило ГДД для варианта «ждать» в кульминации Ночи 3:
        /// батальон успевает перейти мост, если выполнено минимум 2 дела из 3
        /// (спас Тополя, уничтожил танки в овраге, понял сигнал Шила).
        /// </summary>
        public bool CanBattalionReachBridge()
        {
            int score = 0;
            if (topolSaved) score++;
            if (tanksDestroyed) score++;
            if (shiloSignalUnderstood) score++;
            return score >= 2;
        }

        public bool CheckCondition(StoryFlag requiredFlag, StoryFlag forbiddenFlag, string requiredKeywordId)
        {
            if (requiredFlag != StoryFlag.None && !GetFlag(requiredFlag))
            {
                return false;
            }

            if (forbiddenFlag != StoryFlag.None && GetFlag(forbiddenFlag))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(requiredKeywordId) && !HasKeyword(requiredKeywordId))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Сохраняет состояние флагов в начале смены (сохранение между ночами по ГДД).
        /// </summary>
        public void SaveNightCheckpoint(int nightNumber)
        {
            var data = new CheckpointSaveData
            {
                night = nightNumber,
                reserveUnlocked = reservePasswordTableUnlocked,
                berezaDead = berezaDestroyed,
                topolSaved = topolSaved,
                tanksDestroyed = tanksDestroyed,
                sorokaAlive = sorokaAlive,
                romashkaWarned = romashkaWarned,
                zimaReported = zimaReported,
                addressNotRevealed = addressNotRevealed,
                shiloSignalUnderstood = shiloSignalUnderstood,
                trustedZima = trustedZima,
                keywords = new List<RecordedKeywordEntry>(recordedKeywords)
            };

            string json = JsonUtility.ToJson(data);
            PlayerPrefs.SetString(SaveKeyPrefix + nightNumber, json);
            PlayerPrefs.SetInt(SaveKeyPrefix + "LatestNight", nightNumber);
            PlayerPrefs.Save();
        }

        public bool LoadNightCheckpoint(int nightNumber)
        {
            string key = SaveKeyPrefix + nightNumber;
            if (!PlayerPrefs.HasKey(key))
            {
                return false;
            }

            string json = PlayerPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            CheckpointSaveData data = JsonUtility.FromJson<CheckpointSaveData>(json);
            if (data == null)
            {
                return false;
            }

            currentNight = data.night;
            reservePasswordTableUnlocked = data.reserveUnlocked;
            berezaDestroyed = data.berezaDead;
            topolSaved = data.topolSaved;
            tanksDestroyed = data.tanksDestroyed;
            sorokaAlive = data.sorokaAlive;
            romashkaWarned = data.romashkaWarned;
            zimaReported = data.zimaReported;
            addressNotRevealed = data.addressNotRevealed;
            shiloSignalUnderstood = data.shiloSignalUnderstood;
            trustedZima = data.trustedZima;
            recordedKeywords = data.keywords ?? new List<RecordedKeywordEntry>();
            completedCallIds.Clear();

            OnStateResetOrLoaded?.Invoke();
            return true;
        }

        [ContextMenu("Сбросить все флаги и записи")]
        public void ResetAll()
        {
            currentNight = 1;
            reservePasswordTableUnlocked = false;
            berezaDestroyed = false;
            topolSaved = false;
            tanksDestroyed = false;
            sorokaAlive = true;
            romashkaWarned = false;
            zimaReported = false;
            addressNotRevealed = false;
            shiloSignalUnderstood = false;
            trustedZima = false;
            recordedKeywords.Clear();
            completedCallIds.Clear();
            OnStateResetOrLoaded?.Invoke();
        }
    }
}
