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

        // Цвета переводятся в линейное пространство, чтобы в Game View они не становились серыми.
        private readonly Color _ink = UiColor(226, 216, 190);
        private readonly Color _dimInk = UiColor(142, 135, 115);
        private readonly Color _amber = UiColor(196, 137, 67);
        private readonly Color _dark = UiColor(13, 15, 13);
        private readonly Color _panel = UiColor(18, 20, 18, 248);
        private readonly Color _panelSoft = UiColor(24, 25, 22, 242);
        private readonly Color _border = UiColor(83, 76, 58);
        private readonly Color _red = UiColor(133, 55, 42);

        private GameState _state = new GameState();
        private ScreenMode _mode = ScreenMode.MainMenu;
        private ScreenMode _returnMode = ScreenMode.Playing;
        private EndingType _ending;
        private float _patienceLeft;
        private bool _stylesReady;

        private GUIStyle _title;
        private GUIStyle _heading;
        private GUIStyle _body;
        private GUIStyle _small;
        private GUIStyle _button;
        private GUIStyle _dangerButton;
        private Font _uiFont;
        private float _uiScale = 1f;
        private float _offsetX;
        private float _offsetY;
        private float _styleScale;

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

        [SerializeField] private bool showOverlay = false;

        // Главная точка отрисовки: выбирает нужный экран и масштабирует интерфейс.
        private void OnGUI()
        {
            if (!showOverlay)
            {
                return;
            }

            _uiScale = Mathf.Min(Screen.width / DesignWidth, Screen.height / DesignHeight);
            _offsetX = (Screen.width - DesignWidth * _uiScale) * 0.5f;
            _offsetY = (Screen.height - DesignHeight * _uiScale) * 0.5f;
            EnsureStyles(_uiScale);

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
        }

        // Главное меню с началом новой игры и продолжением сохранения.
        private void DrawMainMenu()
        {
            DrawPanel(new Rect(48, 66, 430, 430));
            DrawLabel(new Rect(78, 96, 360, 104), "ПОСЛЕДНИЙ\nСИГНАЛ", _title);
            DrawRect(new Rect(78, 211, 128, 2), _amber);
            DrawLabel(new Rect(78, 230, 340, 48), "Одна комната. Три ночи. Голоса в эфире.", _heading);
            DrawLabel(new Rect(78, 292, 340, 94),
                "Ты никого не видишь. Только слышишь.\nКаждый приказ, каждая просьба и каждая ложь\nпроходят через твои наушники.", _body);
            DrawLabel(new Rect(78, 394, 340, 22), "УЗЕЛ СВЯЗИ «МАЯК»", _small);

            if (DrawButton(new Rect(78, 430, 158, 40), "НОВАЯ ИГРА", _button))
            {
                StartNewGame();
            }

            GUI.enabled = PlayerPrefs.HasKey(SaveKey);
            if (DrawButton(new Rect(250, 430, 168, 40), "ПРОДОЛЖИТЬ", _button))
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

            DrawPanel(new Rect(26, 236, 590, 238));
            DrawLabel(new Rect(48, 254, 542, 26), $"ЛИНИЯ {call.Number:00} / 24   ·   {call.CallSign}", _heading);
            DrawLabel(new Rect(48, 284, 542, 20), call.Speaker.ToUpperInvariant(), _small);
            DrawRect(new Rect(48, 312, 542, 2), _amber);
            DrawLabel(new Rect(48, 328, 542, 82), $"— {call.Transcript}", _body);
            DrawLabel(new Rect(48, 418, 542, 44), $"УЛИКА: {call.Clue}", _small);

            DrawPanel(new Rect(634, 236, 300, 238), _panelSoft);
            DrawLabel(new Rect(656, 254, 256, 26), "РЕШЕНИЕ", _heading);

            ChoiceDefinition[] availableChoices = AvailableChoices(call).ToArray();
            float y = 292f;
            foreach (ChoiceDefinition choice in availableChoices)
            {
                GUI.enabled = interactive;
                float height = availableChoices.Length >= 4 ? 36f : availableChoices.Length == 3 ? 43f : 50f;
                if (DrawButton(new Rect(656, y, 256, height), choice.Label, _button))
                {
                    SelectChoice(choice, false);
                }
                y += height + 8f;
            }
            GUI.enabled = true;

            if (call.PatienceSeconds > 0f)
            {
                float ratio = Mathf.Clamp01(_patienceLeft / call.PatienceSeconds);
                DrawRect(new Rect(656, 449, 256, 5), UiColor(55, 52, 43));
                DrawRect(new Rect(656, 449, 256 * ratio, 5), ratio < 0.3f ? _red : _amber);
                DrawLabel(new Rect(656, 456, 256, 16), $"ТЕРПЕНИЕ: {Mathf.CeilToInt(_patienceLeft)} С", _small);
            }

            GUI.enabled = interactive;
            if (DrawButton(new Rect(26, 490, 132, 34), "ЖУРНАЛ", _button)) OpenOverlay(ScreenMode.Journal);
            if (DrawButton(new Rect(168, 490, 132, 34), "КНИГА", _button)) OpenOverlay(ScreenMode.CodeBook);
            if (DrawButton(new Rect(310, 490, 132, 34), "КАРТА", _button)) OpenOverlay(ScreenMode.Map);
            if (DrawButton(new Rect(746, 490, 188, 34), "СНЯТЬ НАУШНИКИ", _dangerButton))
            {
                Finish(FinalAction.RemoveHeadphones);
            }
            GUI.enabled = true;
        }

        private void DrawTopBar(CallDefinition call)
        {
            DrawRect(new Rect(0, 0, 960, 54), UiColor(12, 14, 12, 250));
            DrawRect(new Rect(0, 53, 960, 1), _border);
            DrawLabel(new Rect(26, 15, 280, 26), "УЗЕЛ «МАЯК»", _heading);
            DrawLabel(new Rect(340, 15, 244, 26), $"НОЧЬ {call.Night}   ·   {call.Time}", _heading);
            DrawLabel(new Rect(644, 18, 62, 20), "ЛИНИИ", _small);
            DrawLamp(new Rect(706, 18, 14, 14), _amber, true);
            DrawLamp(new Rect(738, 18, 14, 14), call.Night >= 2 ? _amber : _dimInk, call.Night >= 2);
            DrawLamp(new Rect(770, 18, 14, 14), call.Night >= 3 ? _red : _dimInk, call.Night >= 3);
            DrawLabel(new Rect(828, 18, 104, 20), $"{call.Number:00}/24", _small);
        }

        // Короткая заставка между ночами показывает, как меняется комната.
        private void DrawNightInterlude()
        {
            DrawPanel(new Rect(150, 116, 660, 318));
            int night = CurrentCall.Night;
            string heading = night == 2 ? "НОЧЬ 2 · ЧУЖИЕ ГОЛОСА" : "НОЧЬ 3 · РАССВЕТ";
            string body = night == 2
                ? "На столе вторая кружка, пустая. Строка «Берёза» в книге зачёркнута карандашом. На потолке трещина. Время 22:00."
                : "Левая ладонь Ларина перевязана бинтом, на костяшках ссадины. Время 03:00. Где-то далеко глухо бухает, пыль на столе чуть вздрагивает.";

            DrawLabel(new Rect(184, 150, 592, 58), heading, _title);
            DrawRect(new Rect(184, 218, 128, 2), _amber);
            DrawLabel(new Rect(184, 242, 592, 96), body, _body);
            DrawLabel(new Rect(184, 344, 300, 24), "СОХРАНЕНИЕ ВЫПОЛНЕНО", _small);
            if (DrawButton(new Rect(184, 380, 240, 36), "НАДЕТЬ НАУШНИКИ", _button))
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
                DrawLabel(new Rect(100, 130, 760, 40), "Записей пока нет.", _body);
            }
            else
            {
                IEnumerable<JournalEntry> entries = _state.Journal.Skip(Math.Max(0, _state.Journal.Count - 8));
                float y = 116f;
                foreach (JournalEntry entry in entries)
                {
                    DrawLabel(new Rect(92, y, 126, 24), $"{entry.CallNumber:00} · {entry.CallSign}", _small);
                    DrawLabel(new Rect(220, y, 645, 42), $"{entry.Decision} — {entry.Consequence}", _small);
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
            DrawLabel(new Rect(105, 120, 730, 330), text, _body);
            DrawOverlayClose();
        }

        // Карта помогает не перепутать школу, овраг, высоту и узел связи.
        private void DrawMap()
        {
            DrawOverlayFrame("КАРТА УЧАСТКА · РЕКА ВЕЖА");
            Rect map = new Rect(112, 130, 700, 250);
            DrawRect(map, UiColor(38, 37, 29));
            DrawRect(new Rect(map.x, map.y, map.width, 1), _border);
            DrawRect(new Rect(map.x, map.yMax - 1, map.width, 1), _border);
            DrawRect(new Rect(map.x, map.y, 1, map.height), _border);
            DrawRect(new Rect(map.xMax - 1, map.y, 1, map.height), _border);

            // Ровная координатная сетка делает схему похожей на рабочую военную карту.
            Color grid = UiColor(58, 55, 42);
            for (int column = 1; column < 7; column++)
            {
                DrawRect(new Rect(map.x + column * 100, map.y, 1, map.height), grid);
            }
            for (int row = 1; row < 5; row++)
            {
                DrawRect(new Rect(map.x, map.y + row * 50, map.width, 1), grid);
            }

            // Река идёт с небольшими изломами, мост точно пересекает её по центру.
            Color river = UiColor(38, 78, 82);
            DrawRect(new Rect(446, 130, 26, 62), river);
            DrawRect(new Rect(452, 192, 26, 63), river);
            DrawRect(new Rect(446, 255, 26, 65), river);
            DrawRect(new Rect(452, 320, 26, 60), river);
            DrawRect(new Rect(420, 246, 84, 14), _border);
            DrawRect(new Rect(422, 248, 80, 10), _amber);
            DrawLabel(new Rect(412, 264, 104, 18), "МОСТ", _small);
            DrawLabel(new Rect(480, 218, 80, 18), "ВЕЖА", _small);

            DrawMapMarker(148, 164, "18-Б", "ШКОЛА");
            DrawMapMarker(174, 292, "18-В", "ОВРАГ");
            DrawMapMarker(588, 174, "ВЫСОТА", "«ЁЖ»");
            DrawMapMarker(566, 302, "12-Б", "«МАЯК»");

            DrawLabel(new Rect(780, 142, 18, 18), "С", _small);
            DrawRect(new Rect(786, 160, 2, 28), _amber);
            DrawRect(new Rect(781, 160, 12, 2), _amber);
            DrawOverlayClose();
        }

        private void DrawMapMarker(float x, float y, string code, string label)
        {
            DrawRect(new Rect(x, y, 12, 12), _amber);
            DrawRect(new Rect(x + 4, y + 4, 4, 4), _dark);
            DrawLabel(new Rect(x + 20, y - 3, 190, 22), $"{code} · {label}", _body);
        }

        // Экран концовки показывает результат и приглашает пройти игру иначе.
        private void DrawEnding()
        {
            EndingDefinition ending = CallDatabase.Endings[_ending];
            DrawPanel(new Rect(100, 62, 760, 416));
            DrawLabel(new Rect(134, 92, 692, 60), ending.Title, _title);
            DrawLabel(new Rect(134, 158, 692, 30), ending.Mood.ToUpperInvariant(), _heading);
            DrawRect(new Rect(134, 201, 128, 2), _amber);
            DrawLabel(new Rect(134, 228, 692, 116), ending.Summary, _body);

            int savedCount = new[]
            {
                StoryFlag.YozhSaved, StoryFlag.TopolSaved, StoryFlag.SorokaSaved,
                StoryFlag.RomashkaSaved, StoryFlag.ShiloSignalUnderstood, StoryFlag.TanksDestroyed
            }.Count(_state.Has);
            DrawLabel(new Rect(134, 354, 692, 24), $"ИТОГИ ЖУРНАЛА: {savedCount} ИЗ 6 КЛЮЧЕВЫХ ЛИНИЙ СОХРАНЕНЫ", _small);

            if (DrawButton(new Rect(134, 404, 220, 38), "ПРОЙТИ ИНАЧЕ", _button))
            {
                StartNewGame();
            }
            if (DrawButton(new Rect(370, 404, 220, 38), "ГЛАВНОЕ МЕНЮ", _button))
            {
                _mode = ScreenMode.MainMenu;
            }
        }

        private void DrawOverlayFrame(string title)
        {
            DrawRect(new Rect(0, 0, 960, 540), new Color(0f, 0f, 0f, 0.58f));
            DrawPanel(new Rect(64, 62, 832, 416));
            DrawLabel(new Rect(92, 82, 700, 34), title, _heading);
            DrawRect(new Rect(92, 116, 744, 2), _amber);
        }

        private void DrawOverlayClose()
        {
            if (DrawButton(new Rect(720, 420, 150, 38), "ЗАКРЫТЬ", _button))
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
        }

        private Rect ScaleRect(Rect rect)
        {
            return new Rect(
                _offsetX + rect.x * _uiScale,
                _offsetY + rect.y * _uiScale,
                rect.width * _uiScale,
                rect.height * _uiScale);
        }

        private void DrawLabel(Rect rect, string text, GUIStyle style)
        {
            GUI.Label(ScaleRect(rect), text, style);
        }

        private bool DrawButton(Rect rect, string text, GUIStyle style)
        {
            return GUI.Button(ScaleRect(rect), text, style);
        }

        private void DrawRect(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(ScaleRect(rect), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void DrawPanel(Rect rect, Color? fill = null)
        {
            // Тонкая рамка и глубокий тёмный фон выглядят как часть радиорубки, а не стандартное окно Unity.
            DrawRect(new Rect(rect.x + 5, rect.y + 6, rect.width, rect.height), new Color(0f, 0f, 0f, 0.42f));
            DrawRect(rect, _border);
            DrawRect(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), fill ?? _panel);
        }

        private void DrawLamp(Rect rect, Color color, bool lit)
        {
            DrawRect(new Rect(rect.x - 4, rect.y - 4, rect.width + 8, rect.height + 8), UiColor(73, 66, 49));
            DrawRect(rect, lit ? color : _dimInk);
        }

        // Стили создаются один раз, чтобы не собирать их заново каждый кадр.
        private void EnsureStyles(float scale)
        {
            if (_stylesReady && Mathf.Abs(_styleScale - scale) < 0.01f)
            {
                return;
            }

            // Arial надёжно показывает кириллицу и сохраняет нормальные интервалы между буквами.
            _uiFont = Font.CreateDynamicFontFromOSFont("Arial", 18);
            if (_uiFont == null)
            {
                _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            _title = NewStyle(38, FontStyle.Bold, _ink, TextAnchor.UpperLeft, true);
            _heading = NewStyle(18, FontStyle.Bold, _ink, TextAnchor.UpperLeft, true);
            _body = NewStyle(17, FontStyle.Normal, _ink, TextAnchor.UpperLeft, true);
            _small = NewStyle(12, FontStyle.Normal, _dimInk, TextAnchor.UpperLeft, true);
            _button = NewButtonStyle(_amber);
            _dangerButton = NewButtonStyle(_red);
            _styleScale = scale;
            _stylesReady = true;
        }

        private GUIStyle NewStyle(int size, FontStyle fontStyle, Color color, TextAnchor alignment, bool wordWrap)
        {
            return new GUIStyle(GUI.skin.label)
            {
                font = _uiFont,
                fontSize = Mathf.Max(1, Mathf.RoundToInt(size * _uiScale)),
                fontStyle = fontStyle,
                alignment = alignment,
                wordWrap = wordWrap,
                normal = { textColor = color }
            };
        }

        private GUIStyle NewButtonStyle(Color accent)
        {
            GUIStyle style = new GUIStyle
            {
                font = _uiFont,
                fontSize = Mathf.Max(1, Mathf.RoundToInt(13f * _uiScale)),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(
                    Mathf.RoundToInt(8f * _uiScale),
                    Mathf.RoundToInt(8f * _uiScale),
                    Mathf.RoundToInt(5f * _uiScale),
                    Mathf.RoundToInt(5f * _uiScale)),
                normal = { background = MakeTexture(UiColor(35, 37, 32)), textColor = _ink },
                hover = { background = MakeTexture(accent), textColor = _dark },
                active = { background = MakeTexture(new Color(accent.r * 0.72f, accent.g * 0.72f, accent.b * 0.72f)), textColor = _ink },
                focused = { background = MakeTexture(accent), textColor = _dark }
            };
            style.onNormal.background = style.hover.background;
            style.onNormal.textColor = _dark;
            style.onHover.background = style.hover.background;
            style.onHover.textColor = _dark;
            style.onActive.background = style.active.background;
            style.onActive.textColor = _ink;
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

        private static Color UiColor(byte red, byte green, byte blue, byte alpha = 255)
        {
            return ((Color)new Color32(red, green, blue, alpha)).linear;
        }
    }
}
