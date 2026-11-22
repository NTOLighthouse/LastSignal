using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastSignal
{
    public sealed class LastSignalGame : MonoBehaviour
    {
        // Интерфейс рисуется в размере 960×540 и затем подстраивается под окно Unity.
        private const float DesignWidth = 960f;
        private const float DesignHeight = 540f;
        private const string SaveKey = "LastSignal.Save.V1";

        // Какой экран сейчас открыт: меню, звонок, справочник или концовка.
        private enum ScreenMode
        {
            MainMenu,
            Playing,
            NightInterlude,
            Journal,
            CodeBook,
            Map,
            Ending
        }

        private readonly Color _ink = new Color32(230, 216, 178, 255);
        private readonly Color _dimInk = new Color32(148, 137, 107, 255);
        private readonly Color _amber = new Color32(231, 153, 56, 255);
        private readonly Color _dark = new Color32(22, 20, 16, 255);
        private readonly Color _panel = new Color32(40, 36, 27, 245);
        private readonly Color _red = new Color32(178, 52, 38, 255);
        private readonly Color _green = new Color32(93, 146, 82, 255);

        private GameState _state = new GameState();
        private ScreenMode _mode = ScreenMode.MainMenu;
        private ScreenMode _returnMode = ScreenMode.Playing;
        private EndingType _ending;
        private float _patienceLeft;
        private float _callStartedAt;
        private bool _stylesReady;

        private GUIStyle _title;
        private GUIStyle _heading;
        private GUIStyle _body;
        private GUIStyle _small;
        private GUIStyle _button;
        private GUIStyle _dangerButton;
        private GUIStyle _box;
        private GUIStyle _label;

        // Возвращает текущий звонок и не позволяет выйти за границы списка.
        private CallDefinition CurrentCall
        {
            get
            {
                int index = Mathf.Clamp(_state.CurrentCallIndex, 0, CallDatabase.Calls.Count - 1);
                return CallDatabase.Calls[index];
            }
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;
        }

        private void Update()
        {
            // В первой ночи таймера нет. Во второй и третьей ожидание постепенно сокращается.
            if (_mode != ScreenMode.Playing || CurrentCall.PatienceSeconds <= 0f)
            {
                return;
            }

            _patienceLeft -= Time.unscaledDeltaTime;
            if (_patienceLeft <= 0f)
            {
                _patienceLeft = 0f;
                SelectChoice(AvailableChoices(CurrentCall).First(), true);
            }
        }

        // Главная точка отрисовки: выбирает нужный экран и масштабирует интерфейс.
        private void OnGUI()
        {
            EnsureStyles();

            float scale = Mathf.Min(Screen.width / DesignWidth, Screen.height / DesignHeight);
            float offsetX = (Screen.width - DesignWidth * scale) * 0.5f;
            float offsetY = (Screen.height - DesignHeight * scale) * 0.5f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0f), Quaternion.identity, Vector3.one * scale);

            DrawRect(new Rect(0, 0, DesignWidth, DesignHeight), _dark);
            DrawNoiseLines();

            switch (_mode)
            {
                case ScreenMode.MainMenu:
                    DrawMainMenu();
                    break;
                case ScreenMode.Playing:
                    DrawPlaying();
                    break;
                case ScreenMode.NightInterlude:
                    DrawNightInterlude();
                    break;
                case ScreenMode.Journal:
                    DrawPlaying(false);
                    DrawJournal();
                    break;
                case ScreenMode.CodeBook:
                    DrawPlaying(false);
                    DrawCodeBook();
                    break;
                case ScreenMode.Map:
                    DrawPlaying(false);
                    DrawMap();
                    break;
                case ScreenMode.Ending:
                    DrawEnding();
                    break;
            }

            GUI.matrix = Matrix4x4.identity;
        }

        // Главное меню с началом новой игры и продолжением сохранения.
        private void DrawMainMenu()
        {
            GUI.Label(new Rect(70, 52, 820, 116), "ПОСЛЕДНИЙ\nСИГНАЛ", _title);
            GUI.Label(new Rect(72, 184, 600, 60), "Одна комната. Три ночи. Голоса в эфире.", _heading);
            GUI.Label(new Rect(72, 252, 540, 90),
                "Ты никого не видишь. Только слышишь.\nКаждый приказ, каждая просьба и каждая ложь\nпроходят через твои наушники.", _body);

            DrawRect(new Rect(670, 85, 210, 310), new Color32(29, 25, 18, 255));
            DrawRect(new Rect(695, 120, 160, 18), _amber);
            for (int i = 0; i < 6; i++)
            {
                Color lamp = i == 1 ? _green : i == 4 ? _red : _amber;
                DrawLamp(new Rect(706 + (i % 3) * 50, 174 + (i / 3) * 60, 18, 18), lamp, true);
            }
            GUI.Label(new Rect(696, 310, 160, 56), "УЗЕЛ СВЯЗИ\n«МАЯК»", _heading);

            if (GUI.Button(new Rect(72, 370, 260, 44), "НОВАЯ ИГРА", _button))
            {
                StartNewGame();
            }

            GUI.enabled = PlayerPrefs.HasKey(SaveKey);
            if (GUI.Button(new Rect(72, 425, 260, 44), "ПРОДОЛЖИТЬ", _button))
            {
                LoadGame();
            }
            GUI.enabled = true;

        }

        // Основной экран звонка: реплика слева, решения справа, справочники снизу.
        private void DrawPlaying(bool interactive = true)
        {
            CallDefinition call = CurrentCall;
            DrawTopBar(call);

            GUI.Box(new Rect(32, 88, 570, 354), GUIContent.none, _box);
            GUI.Label(new Rect(56, 108, 510, 28), $"ЛИНИЯ {call.Number:00} / 24   ·   {call.CallSign}", _heading);
            GUI.Label(new Rect(56, 142, 510, 24), call.Speaker.ToUpperInvariant(), _small);
            DrawRect(new Rect(56, 174, 520, 2), _amber);
            GUI.Label(new Rect(56, 194, 510, 132), $"— {call.Transcript}", _body);
            GUI.Label(new Rect(56, 336, 510, 72), $"УЛИКА: {call.Clue}", _small);

            GUI.Box(new Rect(622, 88, 306, 354), GUIContent.none, _box);
            GUI.Label(new Rect(646, 108, 250, 26), "РЕШЕНИЕ", _heading);

            ChoiceDefinition[] availableChoices = AvailableChoices(call).ToArray();
            float y = 145f;
            foreach (ChoiceDefinition choice in availableChoices)
            {
                GUI.enabled = interactive;
                float height = availableChoices.Length >= 4 ? 50f : 57f;
                if (GUI.Button(new Rect(646, y, 258, height), choice.Label, _button))
                {
                    SelectChoice(choice, false);
                }
                y += height + 11f;
            }
            GUI.enabled = true;

            if (call.PatienceSeconds > 0f)
            {
                float ratio = Mathf.Clamp01(_patienceLeft / call.PatienceSeconds);
                DrawRect(new Rect(646, 405, 258, 8), new Color32(75, 62, 42, 255));
                DrawRect(new Rect(646, 405, 258 * ratio, 8), ratio < 0.3f ? _red : _amber);
                GUI.Label(new Rect(646, 416, 258, 18), $"ТЕРПЕНИЕ ЛИНИИ: {Mathf.CeilToInt(_patienceLeft)} С", _small);
            }

            GUI.enabled = interactive;
            if (GUI.Button(new Rect(32, 462, 150, 40), "ЖУРНАЛ", _button)) OpenOverlay(ScreenMode.Journal);
            if (GUI.Button(new Rect(194, 462, 150, 40), "КНИГА", _button)) OpenOverlay(ScreenMode.CodeBook);
            if (GUI.Button(new Rect(356, 462, 150, 40), "КАРТА", _button)) OpenOverlay(ScreenMode.Map);
            if (GUI.Button(new Rect(732, 462, 196, 40), "СНЯТЬ НАУШНИКИ", _dangerButton))
            {
                Finish(FinalAction.RemoveHeadphones);
            }
            GUI.enabled = true;
        }

        private void DrawTopBar(CallDefinition call)
        {
            DrawRect(new Rect(0, 0, 960, 66), new Color32(13, 12, 10, 255));
            GUI.Label(new Rect(32, 17, 310, 34), "УЗЕЛ «МАЯК»", _heading);
            GUI.Label(new Rect(350, 17, 250, 34), $"НОЧЬ {call.Night}   ·   {call.Time}", _heading);
            GUI.Label(new Rect(630, 20, 80, 24), "ЛИНИИ", _small);
            DrawLamp(new Rect(708, 21, 18, 18), _amber, true);
            DrawLamp(new Rect(744, 21, 18, 18), call.Night >= 2 ? _amber : _dimInk, call.Night >= 2);
            DrawLamp(new Rect(780, 21, 18, 18), call.Night >= 3 ? _red : _dimInk, call.Night >= 3);
            GUI.Label(new Rect(824, 20, 104, 24), $"{call.Number:00}/24", _small);
        }

        // Короткая заставка между ночами показывает, как меняется комната.
        private void DrawNightInterlude()
        {
            int night = CurrentCall.Night;
            string heading = night == 2 ? "НОЧЬ 2 · ЧУЖИЕ ГОЛОСА" : "НОЧЬ 3 · РАССВЕТ";
            string body = night == 2
                ? "На столе вторая кружка, пустая. Строка «Берёза» в книге зачёркнута карандашом. На потолке трещина. Время 22:00."
                : "Левая ладонь Ларина перевязана бинтом, на костяшках ссадины. Время 03:00. Где-то далеко глухо бухает, пыль на столе чуть вздрагивает.";

            GUI.Label(new Rect(90, 122, 780, 80), heading, _title);
            GUI.Label(new Rect(150, 245, 660, 110), body, _body);
            GUI.Label(new Rect(150, 345, 660, 36), "СОХРАНЕНИЕ ВЫПОЛНЕНО", _small);
            if (GUI.Button(new Rect(350, 410, 260, 48), "НАДЕТЬ НАУШНИКИ", _button))
            {
                _mode = ScreenMode.Playing;
                ResetPatience();
            }
        }

        // Журнал выводит последние решения игрока и их последствия.
        private void DrawJournal()
        {
            DrawOverlayFrame("ЖУРНАЛ СВЯЗИ");
            if (_state.Journal.Count == 0)
            {
                GUI.Label(new Rect(100, 130, 760, 40), "Записей пока нет.", _body);
            }
            else
            {
                IEnumerable<JournalEntry> entries = _state.Journal.Skip(Math.Max(0, _state.Journal.Count - 8));
                float y = 116f;
                foreach (JournalEntry entry in entries)
                {
                    GUI.Label(new Rect(92, y, 126, 24), $"{entry.CallNumber:00} · {entry.CallSign}", _small);
                    GUI.Label(new Rect(220, y, 645, 42), $"{entry.Decision} — {entry.Consequence}", _small);
                    y += 44f;
                }
            }
            DrawOverlayClose();
        }

        // Книга напоминает речевые привычки, по которым можно узнать подделку.
        private void DrawCodeBook()
        {
            DrawOverlayFrame("КНИГА ПОЗЫВНЫХ · ПОМЕТКИ ДЁМИНА");
            string text =
                "БЕРЁЗА · зовёт Матвеем\n" +
                "КОБРА · «Конец связи. Работайте»\n" +
                "СОКОЛ-2 · «Отметьте время»\n" +
                "ГРОМ · повторяет координаты\n" +
                "ЁЖ · «Как понял?»\n" +
                "ШИЛО · шёпот, «земляк»\n" +
                "РОМАШКА · «пожалуйста», «голубчик»\n" +
                "ЯКОРЬ · всегда требует пароль\n" +
                "КУЗНИЦА · «Кузница беспокоит», ворчит\n\n" +
                "ПОМЕТКА: пароль может знать враг, привычку — нет.";
            GUI.Label(new Rect(105, 120, 730, 330), text, _body);
            DrawOverlayClose();
        }

        // Карта помогает не перепутать школу, овраг, высоту и узел связи.
        private void DrawMap()
        {
            DrawOverlayFrame("КАРТА УЧАСТКА · РЕКА ВЕЖА");
            DrawRect(new Rect(112, 130, 700, 250), new Color32(55, 51, 38, 255));
            DrawRect(new Rect(445, 130, 38, 250), new Color32(48, 85, 90, 255));
            GUI.Label(new Rect(438, 230, 80, 24), "ВЕЖА", _small);
            GUI.Label(new Rect(150, 172, 180, 30), "18-Б · ШКОЛА", _body);
            GUI.Label(new Rect(170, 280, 180, 30), "18-В · ОВРАГ", _body);
            GUI.Label(new Rect(570, 185, 180, 30), "ВЫСОТА · «ЁЖ»", _body);
            GUI.Label(new Rect(555, 305, 210, 30), "12-Б · «МАЯК»", _body);
            DrawRect(new Rect(420, 242, 88, 18), _amber);
            GUI.Label(new Rect(395, 265, 160, 24), "МОСТ", _small);
            DrawOverlayClose();
        }

        // Экран концовки показывает результат и приглашает пройти игру иначе.
        private void DrawEnding()
        {
            EndingDefinition ending = CallDatabase.Endings[_ending];
            GUI.Label(new Rect(66, 54, 830, 76), ending.Title, _title);
            GUI.Label(new Rect(70, 145, 820, 34), ending.Mood.ToUpperInvariant(), _heading);
            GUI.Box(new Rect(68, 200, 824, 190), GUIContent.none, _box);
            GUI.Label(new Rect(98, 225, 760, 128), ending.Summary, _body);

            int savedCount = new[]
            {
                StoryFlag.YozhSaved, StoryFlag.TopolSaved, StoryFlag.SorokaSaved,
                StoryFlag.RomashkaSaved, StoryFlag.ShiloSignalUnderstood, StoryFlag.TanksDestroyed
            }.Count(_state.Has);
            GUI.Label(new Rect(98, 350, 760, 24), $"ИТОГИ ЖУРНАЛА: {savedCount} ИЗ 6 КЛЮЧЕВЫХ ЛИНИЙ СОХРАНЕНЫ", _small);

            if (GUI.Button(new Rect(210, 430, 250, 48), "ПРОЙТИ ИНАЧЕ", _button))
            {
                StartNewGame();
            }
            if (GUI.Button(new Rect(500, 430, 250, 48), "ГЛАВНОЕ МЕНЮ", _button))
            {
                _mode = ScreenMode.MainMenu;
            }
        }

        private void DrawOverlayFrame(string title)
        {
            DrawRect(new Rect(52, 50, 856, 440), new Color32(16, 14, 11, 252));
            GUI.Box(new Rect(64, 62, 832, 416), GUIContent.none, _box);
            GUI.Label(new Rect(92, 82, 700, 34), title, _heading);
            DrawRect(new Rect(92, 116, 744, 2), _amber);
        }

        private void DrawOverlayClose()
        {
            if (GUI.Button(new Rect(720, 420, 150, 38), "ЗАКРЫТЬ", _button))
            {
                _mode = _returnMode;
            }
        }

        private void OpenOverlay(ScreenMode overlay)
        {
            _returnMode = _mode;
            _mode = overlay;
        }

        // Новая игра очищает старые решения и начинает с первого звонка.
        private void StartNewGame()
        {
            _state = new GameState();
            _mode = ScreenMode.Playing;
            ResetPatience();
            SaveGame();
        }

        private void SelectChoice(ChoiceDefinition choice, bool timedOut)
        {
            CallDefinition call = CurrentCall;
            string decision = timedOut ? $"Время вышло: {choice.Label}" : choice.Label;

            // Каждое решение сразу попадает в журнал и сохраняет свои последствия.
            _state.Journal.Add(new JournalEntry(call.Number, call.CallSign, decision, choice.Consequence));
            foreach (StoryFlag flag in choice.SetFlags)
            {
                _state.Set(flag);
            }

            if (choice.FinalAction != FinalAction.None)
            {
                Finish(choice.FinalAction);
                return;
            }

            int previousNight = call.Night;
            _state.CurrentCallIndex++;
            if (_state.CurrentCallIndex >= CallDatabase.Calls.Count)
            {
                Finish(FinalAction.DawnNow);
                return;
            }

            SaveGame();
            if (CurrentCall.Night != previousNight)
            {
                _mode = ScreenMode.NightInterlude;
            }
            else
            {
                ResetPatience();
            }
        }

        private IEnumerable<ChoiceDefinition> AvailableChoices(CallDefinition call)
        {
            // Например, приказ «мост не трогать» доступен только тому, кто поверил Зиме.
            return call.Choices.Where(choice => !choice.RequiredFlag.HasValue || _state.Has(choice.RequiredFlag.Value));
        }

        private void Finish(FinalAction action)
        {
            switch (action)
            {
                case FinalAction.Wait:
                    // Скрытое правило сценария: для спасения батальона нужны любые два условия из трёх.
                    int conditions = 0;
                    if (_state.Has(StoryFlag.TopolSaved)) conditions++;
                    if (_state.Has(StoryFlag.TanksDestroyed)) conditions++;
                    if (_state.Has(StoryFlag.ShiloSignalUnderstood)) conditions++;
                    _ending = conditions >= 2 ? EndingType.LighthouseBurns : EndingType.FireOnSelf;
                    break;
                case FinalAction.DawnNow:
                    _ending = EndingType.OrdersAreOrders;
                    break;
                case FinalAction.FireOnSelf:
                    _ending = EndingType.FireOnSelf;
                    break;
                case FinalAction.FollowWinter:
                    _ending = EndingType.ForeignVoice;
                    break;
                case FinalAction.RemoveHeadphones:
                    _ending = EndingType.Silence;
                    break;
                default:
                    _ending = EndingType.OrdersAreOrders;
                    break;
            }

            _mode = ScreenMode.Ending;
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
        }

        // Сохраняем только номер звонка, сюжетные отметки и журнал.
        private void SaveGame()
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(_state));
            PlayerPrefs.Save();
        }

        // Если сохранение повреждено, игра безопасно начинает новую историю.
        private void LoadGame()
        {
            try
            {
                _state = JsonUtility.FromJson<GameState>(PlayerPrefs.GetString(SaveKey));
                if (_state == null || _state.CurrentCallIndex < 0 || _state.CurrentCallIndex >= CallDatabase.Calls.Count)
                {
                    throw new InvalidOperationException("Save is outside the call list.");
                }
            }
            catch (Exception)
            {
                _state = new GameState();
            }

            _mode = ScreenMode.Playing;
            ResetPatience();
        }

        private void ResetPatience()
        {
            _patienceLeft = CurrentCall.PatienceSeconds;
            _callStartedAt = Time.unscaledTime;
        }

        private void DrawNoiseLines()
        {
            for (int y = 0; y < DesignHeight; y += 8)
            {
                DrawRect(new Rect(0, y, DesignWidth, 1), new Color(1f, 1f, 1f, 0.012f));
            }
        }

        private static void DrawRect(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void DrawLamp(Rect rect, Color color, bool lit)
        {
            DrawRect(new Rect(rect.x - 4, rect.y - 4, rect.width + 8, rect.height + 8), new Color32(73, 66, 49, 255));
            DrawRect(rect, lit ? color : _dimInk);
        }

        // Стили создаются один раз, чтобы не собирать их заново каждый кадр.
        private void EnsureStyles()
        {
            if (_stylesReady)
            {
                return;
            }

            _title = NewStyle(42, FontStyle.Bold, _ink, TextAnchor.UpperLeft, true);
            _heading = NewStyle(20, FontStyle.Bold, _ink, TextAnchor.UpperLeft, true);
            _body = NewStyle(18, FontStyle.Normal, _ink, TextAnchor.UpperLeft, true);
            _small = NewStyle(13, FontStyle.Normal, _dimInk, TextAnchor.UpperLeft, true);
            _label = NewStyle(15, FontStyle.Bold, _amber, TextAnchor.MiddleLeft, true);

            _box = new GUIStyle(GUI.skin.box)
            {
                border = new RectOffset(2, 2, 2, 2),
                normal = { background = MakeTexture(_panel) }
            };

            _button = NewButtonStyle(_amber, _dark);
            _dangerButton = NewButtonStyle(_red, Color.white);
            _stylesReady = true;
        }

        private GUIStyle NewStyle(int size, FontStyle fontStyle, Color color, TextAnchor alignment, bool wordWrap)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = fontStyle,
                alignment = alignment,
                wordWrap = wordWrap,
                normal = { textColor = color }
            };
        }

        private GUIStyle NewButtonStyle(Color accent, Color text)
        {
            GUIStyle style = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                border = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(10, 10, 7, 7),
                normal = { background = MakeTexture(new Color32(53, 47, 34, 255)), textColor = text },
                hover = { background = MakeTexture(accent), textColor = text },
                active = { background = MakeTexture(new Color(accent.r * 0.75f, accent.g * 0.75f, accent.b * 0.75f)), textColor = text },
                focused = { background = MakeTexture(accent), textColor = text }
            };
            return style;
        }

        private static Texture2D MakeTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
