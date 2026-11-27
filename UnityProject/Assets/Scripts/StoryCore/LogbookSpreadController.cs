using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using LastSignal.SwitchboardSystem;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastSignal.StoryCore
{
    /// <summary>
    /// Управляет перелистыванием книги позывных между Разворотом I и Разворотом II
    /// с бесшовной 3D-анимацией изгибающегося двустороннего листа (Logbook_TurningPage)
    /// и сохранением разлинованной текстуры тетради на всех страницах.
    /// </summary>
    public sealed class LogbookSpreadController : MonoBehaviour
    {
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private const int TexWidth = 1024;
        private const int TexHeight = 1430;

        // Размеры страниц из мешей Logbook_Page_Left/Right.001
        private const float PageHalfHeightX = 0.1075f;
        private const float SpineInnerY = 0.0020f;
        private const float PageWidthY = 0.1540f;
        private const float SpineBaseZ = 0.00740f;
        private const float TurningLeafClearanceZ = 0.0012f;

        private const int GridCols = 18;
        private const int GridRows = 8;

        [Header("Ссылки на книгу и данные")]
        [SerializeField] private BookInspectController bookInspectController;
        [SerializeField] private LogbookSubscribersPage subscribersPage;
        [SerializeField] private LogbookNotesPage notesPage;
        [SerializeField] private StoryFlagsState flagsState;
        [SerializeField] private Camera interactionCamera;

        [Header("Меши книги")]
        [SerializeField] private MeshRenderer leftPageRenderer;
        [SerializeField] private MeshRenderer rightPageRenderer;
        [SerializeField] private Transform bookmarkTransform;

        [Header("Разлинованные базовые текстуры Разворота II")]
        [SerializeField] private Texture2D spread2LeftBaseTexture;
        [SerializeField] private Texture2D spread2RightBaseTexture;

        [Header("3D-анимация перелистывания листа")]
        [SerializeField, Min(0.2f)] private float pageTurnDuration = 0.56f;
        [SerializeField, Range(0f, 0.05f)] private float pageArchLiftHeight = 0.022f;
        [SerializeField, Range(0f, 0.7f)] private float paperFlexLagRadians = 0.44f;

        [Header("Звук перелистывания")]
        [SerializeField] private AudioSource pageFlipAudioSource;
        [SerializeField, Range(0f, 1f)] private float flipVolume = 0.45f;

        [Header("Состояние (только чтение)")]
        [SerializeField] private int currentSpreadIndex = 0;

        private RenderTexture _spread2LeftRT;
        private RenderTexture _spread2RightRT;
        private GameObject _offscreenRig;
        private Camera _leftCam;
        private Camera _rightCam;

        private GameObject _night3StrikeObj;
        private GameObject _reservePanelObj;
        private Text _keywordsBodyText;

        // Процедурный двусторонний 3D-лист для бесшовного перелистывания
        private GameObject _turningLeafGo;
        private MeshFilter _turningLeafFilter;
        private MeshRenderer _turningLeafRenderer;
        private Mesh _turningLeafMesh;
        private Material _leafFrontMat;
        private Material _leafBackMat;
        private Vector3[] _leafVerts;
        private Coroutine _turnCoroutine;
        private int _turningTargetSpread = -1;

        private Font _bookFont;
        private AudioClip _proceduralFlipClip;

        public int CurrentSpreadIndex => currentSpreadIndex;
        public bool IsTurningPage => _turnCoroutine != null;
        public bool IsPageTurnPending { get; private set; }
        public float TurnProgress01 { get; private set; }
        public bool TurningForward { get; private set; }
        public event Action<int> OnSpreadChanged;

        /// <summary>
        /// Опциональный внешний координатор (например, хореография рук): получает целевой разворот (0/1)
        /// и колбэк запуска 3D-перелистывания. Если возвращает true — страница ждёт сигнала от координатора.
        /// </summary>
        public Func<int, Action, bool> OnBeforePageTurn;

        private void Awake()
        {
            if (bookInspectController == null) bookInspectController = GetComponent<BookInspectController>();
            if (interactionCamera == null) interactionCamera = Camera.main;

            ResolveFont();
            BuildSpread2Canvases();
            BuildTurningLeafMesh();
            EnsureAudioSource();
        }

        private void Start()
        {
            RefreshSpread2();
            ApplySpreadTexturesImmediate(0);
        }

        private void OnEnable()
        {
            if (flagsState != null)
            {
                flagsState.OnKeywordRecorded += HandleKeywordRecorded;
                flagsState.OnStateResetOrLoaded += RefreshSpread2;
            }

            if (notesPage != null)
            {
                notesPage.OnBeforeStartWrite = HandlePrepareForPencilNote;
            }
        }

        private void OnDisable()
        {
            if (flagsState != null)
            {
                flagsState.OnKeywordRecorded -= HandleKeywordRecorded;
                flagsState.OnStateResetOrLoaded -= RefreshSpread2;
            }

            if (notesPage != null && ReferenceEquals(notesPage.OnBeforeStartWrite, (Func<Action, bool>)HandlePrepareForPencilNote))
            {
                notesPage.OnBeforeStartWrite = null;
            }
        }

        private void OnDestroy()
        {
            if (_offscreenRig != null) Destroy(_offscreenRig);
            if (_turningLeafGo != null) Destroy(_turningLeafGo);
            if (_turningLeafMesh != null) Destroy(_turningLeafMesh);
            if (_leafFrontMat != null) Destroy(_leafFrontMat);
            if (_leafBackMat != null) Destroy(_leafBackMat);
            if (_spread2LeftRT != null) { _spread2LeftRT.Release(); Destroy(_spread2LeftRT); }
            if (_spread2RightRT != null) { _spread2RightRT.Release(); Destroy(_spread2RightRT); }
            if (_proceduralFlipClip != null) Destroy(_proceduralFlipClip);
        }

        private void Update()
        {
            if (GameplayInputGate.IsBlocked ||
                bookInspectController == null ||
                !bookInspectController.IsInspecting ||
                IsTurningPage ||
                IsPageTurnPending)
            {
                return;
            }

            if (WasFlipNextPressed())
            {
                SetSpread(currentSpreadIndex == 0 ? 1 : 0, playSound: true);
                return;
            }

            if (WasFlipPrevPressed())
            {
                SetSpread(currentSpreadIndex == 1 ? 0 : 1, playSound: true);
                return;
            }

            if (WasLeftMousePressed(out Vector2 mousePos) &&
                !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) &&
                IsClickOnBookmark(mousePos))
            {
                SetSpread(currentSpreadIndex == 0 ? 1 : 0, playSound: true);
            }
        }

        /// <summary>
        /// Плавно перелистывает книгу между Разворотом I (0) и Разворотом II (1) с 3D-изгибом листа.
        /// </summary>
        public void SetSpread(int spreadIndex, bool playSound = true)
        {
            int target = Mathf.Clamp(spreadIndex, 0, 1);
            if (target == currentSpreadIndex && !IsTurningPage && !IsPageTurnPending)
            {
                return;
            }

            if ((IsTurningPage || IsPageTurnPending) && target == _turningTargetSpread)
            {
                return;
            }

            if (!gameObject.activeInHierarchy || _turningLeafGo == null)
            {
                IsPageTurnPending = false;
                currentSpreadIndex = target;
                ApplySpreadTexturesImmediate(target);
                OnSpreadChanged?.Invoke(currentSpreadIndex);
                return;
            }

            if (OnBeforePageTurn != null)
            {
                _turningTargetSpread = target;
                IsPageTurnPending = true;
                if (OnBeforePageTurn.Invoke(target, () => ExecutePageTurnNow(target, playSound)))
                {
                    return;
                }

                IsPageTurnPending = false;
            }

            ExecutePageTurnNow(target, playSound);
        }

        public void CancelPendingPageTurn()
        {
            if (!IsPageTurnPending)
            {
                return;
            }

            IsPageTurnPending = false;
            _turningTargetSpread = -1;
        }

        private void ExecutePageTurnNow(int target, bool playSound)
        {
            IsPageTurnPending = false;

            if (_turnCoroutine != null)
            {
                StopCoroutine(_turnCoroutine);
                _turnCoroutine = null;
            }

            _turningTargetSpread = target;
            _turnCoroutine = StartCoroutine(AnimatePageTurnRoutine(target, playSound));
        }

        public void RefreshSpread2()
        {
            bool reserveUnlocked = flagsState != null && flagsState.ReservePasswordTableUnlocked;

            if (_night3StrikeObj != null)
            {
                _night3StrikeObj.SetActive(reserveUnlocked);
            }

            if (_reservePanelObj != null)
            {
                _reservePanelObj.SetActive(reserveUnlocked);
            }

            if (_keywordsBodyText != null)
            {
                var sb = new StringBuilder();
                if (flagsState == null || flagsState.RecordedKeywords.Count == 0)
                {
                    sb.AppendLine("Пока нет оперативных записей.");
                    sb.AppendLine();
                    sb.AppendLine("Нажимайте на подсвеченные слова");
                    sb.AppendLine("в субтитрах, чтобы записать");
                    sb.AppendLine("координаты, позывные и улики.");
                }
                else
                {
                    for (int i = 0; i < flagsState.RecordedKeywords.Count; i++)
                    {
                        RecordedKeywordEntry item = flagsState.RecordedKeywords[i];
                        sb.Append("— ").Append(item.displayText);
                        if (item.nightRecorded > 0)
                        {
                            sb.Append("  (Ночь ").Append(item.nightRecorded).Append(')');
                        }
                        sb.AppendLine();
                    }
                }

                _keywordsBodyText.text = sb.ToString();
            }

            if (_leftCam != null) _leftCam.Render();
            if (_rightCam != null) _rightCam.Render();
        }

        private Action _pendingAfterTurnCallback;

        /// <summary>
        /// Автоматически перелистывает книгу на Разворот I (страница заметок) перед тем, как карандаш начнёт писать.
        /// </summary>
        private bool HandlePrepareForPencilNote(Action onReadyToWrite)
        {
            if (currentSpreadIndex == 0 && !IsTurningPage)
            {
                return false;
            }

            _pendingAfterTurnCallback += onReadyToWrite;

            if (!IsTurningPage || _turningTargetSpread != 0)
            {
                SetSpread(0, playSound: true);
            }

            return true;
        }

        private IEnumerator AnimatePageTurnRoutine(int targetSpread, bool playSound)
        {
            bool forward = targetSpread == 1; // 0 -> 1 (справа налево), 1 -> 0 (слева направо)
            RefreshSpread2();

            Texture s1Left = subscribersPage != null ? subscribersPage.PageTexture : null;
            Texture s1Right = notesPage != null ? notesPage.PageTexture : null;
            Texture s2Left = _spread2LeftRT;
            Texture s2Right = _spread2RightRT;

            if (subscribersPage != null)
            {
                subscribersPage.AllowPageInteraction = false;
            }

            // Назначаем обе стороны перелистываемого листа:
            // Лицевая (справа, +Z при p=0) = Разворот I Справа; Обратная (слева, +Z при p=1) = Разворот II Слева.
            SetMaterialTexture(_leafFrontMat, s1Right);
            SetMaterialTexture(_leafBackMat, s2Left);

            if (forward)
            {
                // Под листом справа сразу открывается Разворот II Справа; слева пока остаётся Разворот I Слева.
                ApplyTextureToRenderer(leftPageRenderer, s1Left);
                ApplyTextureToRenderer(rightPageRenderer, s2Right);
                DeformTurningLeaf(progress01: 0f, turningForward: true);
            }
            else
            {
                // Под листом слева сразу открывается Разворот I Слева; справа пока остаётся Разворот II Справа.
                ApplyTextureToRenderer(leftPageRenderer, s1Left);
                ApplyTextureToRenderer(rightPageRenderer, s2Right);
                DeformTurningLeaf(progress01: 1f, turningForward: false);
            }

            _turningLeafGo.SetActive(true);
            if (playSound)
            {
                PlayFlipSound();
            }

            TurningForward = forward;
            TurnProgress01 = 0f;

            float elapsed = 0f;
            float dur = Mathf.Max(0.2f, pageTurnDuration);
            while (elapsed < dur)
            {
                elapsed += Time.unscaledDeltaTime;
                float s = SmootherStep(elapsed / dur);
                TurnProgress01 = s;
                float p = forward ? s : (1f - s);
                DeformTurningLeaf(p, turningForward: forward);
                yield return null;
            }

            TurnProgress01 = 1f;
            currentSpreadIndex = targetSpread;
            _turningTargetSpread = -1;
            ApplySpreadTexturesImmediate(currentSpreadIndex);
            _turningLeafGo.SetActive(false);
            _turnCoroutine = null;

            OnSpreadChanged?.Invoke(currentSpreadIndex);

            if (currentSpreadIndex == 0 && _pendingAfterTurnCallback != null)
            {
                Action cb = _pendingAfterTurnCallback;
                _pendingAfterTurnCallback = null;
                cb.Invoke();
            }
        }

        private void ApplySpreadTexturesImmediate(int spread)
        {
            if (subscribersPage != null)
            {
                subscribersPage.AllowPageInteraction = (spread == 0);
            }

            if (spread == 0)
            {
                ApplyTextureToRenderer(leftPageRenderer, subscribersPage != null ? subscribersPage.PageTexture : null);
                ApplyTextureToRenderer(rightPageRenderer, notesPage != null ? notesPage.PageTexture : null);
            }
            else
            {
                RefreshSpread2();
                ApplyTextureToRenderer(leftPageRenderer, _spread2LeftRT);
                ApplyTextureToRenderer(rightPageRenderer, _spread2RightRT);
            }
        }

        private void BuildTurningLeafMesh()
        {
            _turningLeafGo = new GameObject("Logbook_TurningPage");
            _turningLeafGo.transform.SetParent(transform, false);
            _turningLeafGo.transform.localPosition = Vector3.zero;
            _turningLeafGo.transform.localRotation = Quaternion.identity;
            _turningLeafGo.transform.localScale = Vector3.one;

            _turningLeafFilter = _turningLeafGo.AddComponent<MeshFilter>();
            _turningLeafRenderer = _turningLeafGo.AddComponent<MeshRenderer>();

            int vertsPerSide = (GridCols + 1) * (GridRows + 1);
            _leafVerts = new Vector3[vertsPerSide * 2];
            Vector2[] uvs = new Vector2[vertsPerSide * 2];

            for (int r = 0; r <= GridRows; r++)
            {
                float v = r / (float)GridRows;
                for (int c = 0; c <= GridCols; c++)
                {
                    float u = c / (float)GridCols;
                    int idx = r * (GridCols + 1) + c;
                    uvs[idx] = new Vector2(u, v);
                    uvs[vertsPerSide + idx] = new Vector2(1f - u, v);
                }
            }

            int quadCount = GridCols * GridRows;
            int[] frontTris = new int[quadCount * 6];
            int[] backTris = new int[quadCount * 6];
            int ti = 0;

            for (int r = 0; r < GridRows; r++)
            {
                for (int c = 0; c < GridCols; c++)
                {
                    int i00 = r * (GridCols + 1) + c;
                    int i10 = i00 + 1;
                    int i01 = i00 + (GridCols + 1);
                    int i11 = i01 + 1;

                    // Лицевая сторона (+Z при p=0)
                    frontTris[ti] = i00;
                    frontTris[ti + 1] = i01;
                    frontTris[ti + 2] = i11;
                    frontTris[ti + 3] = i00;
                    frontTris[ti + 4] = i11;
                    frontTris[ti + 5] = i10;

                    // Обратная сторона (+Z при p=1)
                    int b00 = vertsPerSide + i00;
                    int b10 = vertsPerSide + i10;
                    int b01 = vertsPerSide + i01;
                    int b11 = vertsPerSide + i11;
                    backTris[ti] = b00;
                    backTris[ti + 1] = b11;
                    backTris[ti + 2] = b01;
                    backTris[ti + 3] = b00;
                    backTris[ti + 4] = b10;
                    backTris[ti + 5] = b11;

                    ti += 6;
                }
            }

            _turningLeafMesh = new Mesh { name = "Logbook_TurningLeafMesh" };
            _turningLeafMesh.MarkDynamic();
            _turningLeafMesh.vertices = _leafVerts;
            _turningLeafMesh.uv = uvs;
            _turningLeafMesh.subMeshCount = 2;
            _turningLeafMesh.SetTriangles(frontTris, 0);
            _turningLeafMesh.SetTriangles(backTris, 1);

            DeformTurningLeaf(0f, true);
            _turningLeafFilter.sharedMesh = _turningLeafMesh;

            Material templateMat = rightPageRenderer != null ? rightPageRenderer.sharedMaterial : null;
            Shader fallbackShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _leafFrontMat = templateMat != null ? new Material(templateMat) : new Material(fallbackShader);
            _leafBackMat = templateMat != null ? new Material(templateMat) : new Material(fallbackShader);
            _turningLeafRenderer.sharedMaterials = new[] { _leafFrontMat, _leafBackMat };

            _turningLeafGo.SetActive(false);
        }

        private void DeformTurningLeaf(float progress01, bool turningForward)
        {
            if (_turningLeafMesh == null || _leafVerts == null) return;

            int vertsPerSide = (GridCols + 1) * (GridRows + 1);
            float baseTheta = Mathf.Clamp01(progress01) * Mathf.PI;
            float midSin = Mathf.Sin(baseTheta);
            float dirSign = turningForward ? 1f : -1f;

            for (int r = 0; r <= GridRows; r++)
            {
                float v = r / (float)GridRows;
                float x = Mathf.Lerp(-PageHalfHeightX, PageHalfHeightX, v);

                for (int c = 0; c <= GridCols; c++)
                {
                    float u = c / (float)GridCols;
                    float radius = SpineInnerY + u * PageWidthY;
                    float restCurveZ = LogbookPencilAnimator.SampleCurvedPageSurfaceZ(radius) - SpineBaseZ;

                    // Мягкое отставание внешнего края листа + опережение нижнего уголка (как при реальном перелистывании пальцем).
                    float lag = midSin * paperFlexLagRadians * u * u;
                    float cornerLead = midSin * 0.14f * u * (1f - v);
                    float theta = Mathf.Clamp(baseTheta - dirSign * (lag - cornerLead), 0f, Mathf.PI);

                    float cosT = Mathf.Cos(theta);
                    float sinT = Mathf.Sin(theta);
                    float restBlend = Mathf.Abs(cosT);
                    float archLift = sinT * pageArchLiftHeight * Mathf.Sin(u * Mathf.PI);

                    float y = radius * cosT;
                    float z = SpineBaseZ + radius * sinT + restCurveZ * restBlend + archLift + TurningLeafClearanceZ;

                    int idx = r * (GridCols + 1) + c;
                    Vector3 pt = new Vector3(x, y, z);
                    _leafVerts[idx] = pt;
                    _leafVerts[vertsPerSide + idx] = pt;
                }
            }

            _turningLeafMesh.vertices = _leafVerts;
            _turningLeafMesh.RecalculateNormals();
            _turningLeafMesh.RecalculateBounds();
        }

        private void HandleKeywordRecorded(RecordedKeywordEntry entry)
        {
            RefreshSpread2();
        }

        private void BuildSpread2Canvases()
        {
            _spread2LeftRT = CreatePageRT("RT_Logbook_Spread2_Left");
            _spread2RightRT = CreatePageRT("RT_Logbook_Spread2_Right");

            _offscreenRig = new GameObject("Offscreen_LogbookSpread2Rig");
            _offscreenRig.transform.position = new Vector3(-620f, -500f, 0f);

            // --- СТРАНИЦА 3 (СЛЕВА): РАЗЛИНОВАННАЯ ТАБЛИЦА ПАРОЛЕЙ + ДИНАМИЧЕСКАЯ ВКЛЕЙКА НОЧИ 3 ---
            Canvas leftCanvas = CreateOffscreenPage(_offscreenRig.transform, "Spread2_Left", Vector3.zero, _spread2LeftRT, spread2LeftBaseTexture, out _leftCam);

            // Карандашная черта отмены для строки «Ночь 3 · Колодец — Сентябрь» (y=452..558, центр 505)
            _night3StrikeObj = CreateLine(leftCanvas.transform, "Night3Strike",
                new Vector2(64, -503), new Vector2(888, 6), new Color(0.52f, 0.12f, 0.09f, 0.90f));
            _night3StrikeObj.SetActive(false);

            // Вклейка резервной таблицы из конверта (Ночь 3)
            GameObject reserveBg = new GameObject("ReserveEnvelopePanel");
            reserveBg.layer = 5;
            reserveBg.transform.SetParent(leftCanvas.transform, false);
            Image resImg = reserveBg.AddComponent<Image>();
            resImg.color = new Color(0.93f, 0.87f, 0.73f, 0.96f);
            RectTransform resRt = reserveBg.GetComponent<RectTransform>();
            resRt.anchorMin = new Vector2(0, 1);
            resRt.anchorMax = new Vector2(0, 1);
            resRt.pivot = new Vector2(0, 1);
            resRt.anchoredPosition = new Vector2(72, -736);
            resRt.sizeDelta = new Vector2(860, 290);

            CreateLine(reserveBg.transform, "BorderTop", new Vector2(0, 0), new Vector2(860, 4), new Color(0.50f, 0.16f, 0.12f, 0.85f));
            CreateLine(reserveBg.transform, "BorderBottom", new Vector2(0, -286), new Vector2(860, 4), new Color(0.50f, 0.16f, 0.12f, 0.85f));
            CreateText(reserveBg.transform, "ResTitle", "РЕЗЕРВНАЯ ТАБЛИЦА (ИЗ КОНВЕРТА · НОЧЬ 3)", 25, FontStyle.Bold,
                new Color(0.52f, 0.14f, 0.10f, 1f), new Vector2(28, -24), new Vector2(804, 44), TextAnchor.MiddleLeft);
            CreateText(reserveBg.transform, "ResBody",
                "ПАРОЛЬ:  Уголь\nОТЗЫВ:   Ладога\n\nОсновная таблица захвачена на развалинах «Берёзы».\nВсякий, кто отвечает «Сентябрь», — чужой.",
                26, FontStyle.Italic, new Color(0.15f, 0.11f, 0.08f, 1f),
                new Vector2(28, -82), new Vector2(804, 190), TextAnchor.UpperLeft);

            _reservePanelObj = reserveBg;
            _reservePanelObj.SetActive(false);

            // --- СТРАНИЦА 4 (СПРАВА): РАЗЛИНОВАННЫЙ ЖУРНАЛ ОПЕРАТИВНЫХ ЗАПИСЕЙ ---
            Canvas rightCanvas = CreateOffscreenPage(_offscreenRig.transform, "Spread2_Right", new Vector3(1600f, 0f, 0f), _spread2RightRT, spread2RightBaseTexture, out _rightCam);

            // Текст выровнен точно по горизонтальным линейкам (шаг 86px, начиная с y = 216)
            _keywordsBodyText = CreateText(rightCanvas.transform, "KeywordsBody", string.Empty, 32, FontStyle.Italic,
                new Color(0.14f, 0.14f, 0.18f, 0.94f), new Vector2(106, -216), new Vector2(826, 1080), TextAnchor.UpperLeft);
            _keywordsBodyText.lineSpacing = 2.15f;
        }

        private static RenderTexture CreatePageRT(string rtName)
        {
            var rt = new RenderTexture(TexWidth, TexHeight, 16, RenderTextureFormat.ARGB32)
            {
                name = rtName,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
                useMipMap = true,
                autoGenerateMips = true
            };
            rt.Create();
            return rt;
        }

        private static Canvas CreateOffscreenPage(Transform parent, string name, Vector3 localOffset, RenderTexture targetRT, Texture2D baseTex, out Camera cam)
        {
            GameObject holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localOffset;

            GameObject camGo = new GameObject("Cam");
            camGo.transform.SetParent(holder.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0f, -10f);
            cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = TexHeight * 0.5f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.87f, 0.81f, 0.68f, 1f);
            cam.cullingMask = 1 << 5;
            cam.targetTexture = targetRT;
            cam.enabled = false;

            GameObject canvasGo = new GameObject("Canvas");
            canvasGo.layer = 5;
            canvasGo.transform.SetParent(holder.transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 10f;

            RectTransform rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(TexWidth, TexHeight);

            GameObject bg = new GameObject("PaperBg");
            bg.layer = 5;
            bg.transform.SetParent(canvasGo.transform, false);
            RectTransform bgRt = bg.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            if (baseTex != null)
            {
                RawImage raw = bg.AddComponent<RawImage>();
                raw.texture = baseTex;
            }
            else
            {
                Image img = bg.AddComponent<Image>();
                img.color = new Color(0.87f, 0.81f, 0.69f, 1f);
            }

            return canvas;
        }

        private Text CreateText(Transform parent, string name, string content, int size, FontStyle style, Color color, Vector2 pos, Vector2 dims, TextAnchor align)
        {
            GameObject go = new GameObject(name);
            go.layer = 5;
            go.transform.SetParent(parent, false);
            Text txt = go.AddComponent<Text>();
            txt.font = _bookFont;
            txt.text = content;
            txt.fontSize = size;
            txt.fontStyle = style;
            txt.color = color;
            txt.alignment = align;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos;
            rt.sizeDelta = dims;
            return txt;
        }

        private static GameObject CreateLine(Transform parent, string name, Vector2 pos, Vector2 dims, Color color)
        {
            GameObject go = new GameObject(name);
            go.layer = 5;
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.color = color;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos;
            rt.sizeDelta = dims;
            return go;
        }

        private static void ApplyTextureToRenderer(MeshRenderer renderer, Texture tex)
        {
            if (renderer == null || tex == null) return;
            SetMaterialTexture(renderer.material, tex);
        }

        private static void SetMaterialTexture(Material mat, Texture tex)
        {
            if (mat == null || tex == null) return;
            if (mat.HasProperty(BaseMapId)) mat.SetTexture(BaseMapId, tex);
            if (mat.HasProperty(MainTexId)) mat.SetTexture(MainTexId, tex);
        }

        private bool IsClickOnBookmark(Vector2 mousePos)
        {
            if (bookmarkTransform == null) return false;
            Camera cam = interactionCamera != null ? interactionCamera : Camera.main;
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(mousePos);
            if (Physics.Raycast(ray, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Collide))
            {
                return hit.transform == bookmarkTransform || hit.transform.IsChildOf(bookmarkTransform);
            }

            return false;
        }

        private void EnsureAudioSource()
        {
            if (pageFlipAudioSource == null) pageFlipAudioSource = gameObject.AddComponent<AudioSource>();
            pageFlipAudioSource.playOnAwake = false;
            pageFlipAudioSource.loop = false;
            pageFlipAudioSource.spatialBlend = 0.25f;
        }

        private void PlayFlipSound()
        {
            if (pageFlipAudioSource == null) return;
            if (_proceduralFlipClip == null) _proceduralFlipClip = CreatePageFlipClip();
            pageFlipAudioSource.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
            pageFlipAudioSource.PlayOneShot(_proceduralFlipClip, flipVolume);
        }

        private static AudioClip CreatePageFlipClip()
        {
            const int sr = 44100;
            const float dur = 0.34f;
            int count = Mathf.RoundToInt(sr * dur);
            float[] data = new float[count];
            var rng = new System.Random(4219);
            float lp = 0f, hp = 0f, prev = 0f;

            for (int i = 0; i < count; i++)
            {
                float u = i / (float)count;
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.38f * (w - lp);
                hp = 0.80f * (hp + lp - prev);
                prev = lp;

                float swoosh1 = Mathf.Exp(-Mathf.Pow((u - 0.32f) / 0.18f, 2f));
                float swoosh2 = Mathf.Exp(-Mathf.Pow((u - 0.72f) / 0.16f, 2f)) * 0.65f;
                float env = (swoosh1 + swoosh2) * Mathf.Clamp01(u * 16f) * Mathf.Clamp01((1f - u) * 12f);
                data[i] = Mathf.Clamp(hp * env * 0.85f, -0.9f, 0.9f);
            }

            AudioClip clip = AudioClip.Create("Book_PageFlip", count, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void ResolveFont()
        {
            _bookFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Georgia", "Times New Roman", "Segoe UI", "Arial" }, 32);
            if (_bookFont == null)
            {
                _bookFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
        }

        private static float SmootherStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private static bool WasFlipNextPressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            bool key = kb != null && (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame);
            bool wheel = mouse != null && mouse.scroll.ReadValue().y < -10f;
            return key || wheel;
#else
            return Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.Tab) || Input.mouseScrollDelta.y < -0.1f;
#endif
        }

        private static bool WasFlipPrevPressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            bool key = kb != null && (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame);
            bool wheel = mouse != null && mouse.scroll.ReadValue().y > 10f;
            return key || wheel;
#else
            return Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow) || Input.mouseScrollDelta.y > 0.1f;
#endif
        }

        private static bool WasLeftMousePressed(out Vector2 mousePos)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                mousePos = mouse.position.ReadValue();
                return true;
            }
            mousePos = Vector2.zero;
            return false;
#else
            if (Input.GetMouseButtonDown(0))
            {
                mousePos = Input.mousePosition;
                return true;
            }
            mousePos = Vector2.zero;
            return false;
#endif
        }
    }
}
