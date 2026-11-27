using LastSignal.SwitchboardSystem;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LastSignal
{
    /// <summary>
    /// Interactive 4-panel folded letter with Metro-style unhurried cutscene pacing, heavy EaseInOutQuart curves,
    /// human micro-pauses, physical contact verification, and single-knob animSpeedMultiplier.
    /// </summary>
    [DefaultExecutionOrder(0)]
    public sealed class LetterInteraction : MonoBehaviour
    {
        public enum LetterState
        {
            Idle = 0,
            ReachingRight = 1,
            GripPause = 2,
            BringingToCamera = 3,
            LeftHandJoin = 4,
            UnfoldingVert = 5,
            UnfoldMidPause = 6,
            UnfoldingHoriz = 7,
            Reading = 8,
            FoldingHoriz = 9,
            FoldMidPause = 10,
            ReleasingLeft = 11,
            FoldingVert = 12,
            ReturningToHome = 13,
            SettlingAtHome = 14
        }

        [Header("Общий темп катсцены письма")]
        [Tooltip("Множитель скорости всей анимации письма (1 = размеренный темп Metro, 1.2 = на 20% быстрее, 0.8 = медленнее).")]
        [Range(0.3f, 2.5f)] public float animSpeedMultiplier = 1f;

        [Header("Петли листа (сложено текстом внутрь -178° -> развёрнуто 0°)")]
        public Transform horizHingeRight;
        public Transform horizHingeLeft;
        public Vector3 horizHingeAxis = Vector3.right;
        public float horizFoldedAngle = -178f;

        public Transform vertHinge;
        public Vector3 vertHingeAxis = Vector3.up;
        public float vertFoldedAngle = -178f;

        [Header("Микро-смещение VertHinge по Z")]
        public float vertUnfoldedLocalZ = -0.0008f;
        public float vertFoldedLocalZ = -0.0072f;

        [Header("Точки хвата руками")]
        public Transform rightFoldedGrip;
        public Transform leftUnfoldedGrip;

        [Header("Ссылки на камеру и окружение")]
        public Camera interactionCamera;
        public SmoothMouseLookCamera cameraLook;
        public SwitchboardInteractor switchboardInteractor;
        public Volume backgroundBlurVolume;
        [Range(0f, 1f)] public float maxBlurWeight = 0.85f;

        [Header("Поза чтения перед камерой")]
        [Min(0.18f)] public float readDistanceFromCamera = 0.36f;
        public float readVerticalOffset = -0.015f;
        [Range(-25f, 25f)] public float readTiltDegrees = 6f;

        [Header("Тайминги ОТКРЫТИЯ (сек, ~2.8-3.0с в сумме)")]
        [Min(0.2f)] public float reachDuration = 0.60f;
        [Range(0.02f, 0.12f)] public float reachTouchThresholdMeters = 0.045f;
        [Min(0.5f)] public float reachSafetyTimeout = 1.10f;
        [Min(0.02f)] public float gripPauseDuration = 0.14f;
        [Min(0.2f)] public float bringDuration = 0.80f;
        [Min(0.1f)] public float leftJoinDuration = 0.35f;
        [Min(0.15f)] public float unfoldVertDuration = 0.55f;
        [Min(0.02f)] public float unfoldMidPauseDuration = 0.12f;
        [Min(0.15f)] public float unfoldHorizDuration = 0.60f;

        [Header("Тайминги ЗАКРЫТИЯ (сек, ~2.4с в сумме)")]
        [Min(0.15f)] public float foldHorizDuration = 0.50f;
        [Min(0.02f)] public float foldMidPauseDuration = 0.10f;
        [Min(0.1f)] public float leftReleaseDelay = 0.35f;
        [Min(0.15f)] public float foldVertDuration = 0.50f;
        [Min(0.2f)] public float returnDuration = 0.80f;
        [Range(0.02f, 0.30f)] public float settleReleasePause = 0.12f;

        [Header("Живое дыхание бумаги при чтении")]
        [Range(0f, 0.008f)] public float readingBreathAmplitude = 0.0024f;

        [Header("Контроллер рук")]
        public BookHandsIKController handsController;

        [Header("Рейкаст и управление")]
        public LayerMask raycastMask = ~0;
        [Min(0.5f)] public float maxRayDistance = 15f;
        public bool closeOnRightClick = true;
        public bool closeOnEscape = true;

        private Vector3 _homePos;
        private Quaternion _homeRot;
        private bool _posesCaptured;

        private Vector3 _readPos;
        private Quaternion _readRot;
        private Vector3 _phaseStartPos;
        private Quaternion _phaseStartRot;
        private LetterState _state = LetterState.Idle;
        private float _stateTimer;
        private float _readingTime;

        public LetterState CurrentState => _state;
        public bool IsActive => _state != LetterState.Idle;
        public bool IsReading => _state == LetterState.Reading || _state == LetterState.UnfoldingHoriz || _state == LetterState.UnfoldMidPause;
        public bool WantsRightHand => _state != LetterState.Idle;
        // Left hand joins cleanly at LeftHandJoin, holds through Reading & FoldingHoriz, then smoothly releases at ReleasingLeft BEFORE FoldingVert & ReturningToHome!
        public bool WantsLeftHand =>
            _state == LetterState.LeftHandJoin ||
            _state == LetterState.UnfoldingVert ||
            _state == LetterState.UnfoldMidPause ||
            _state == LetterState.UnfoldingHoriz ||
            _state == LetterState.Reading ||
            _state == LetterState.FoldingHoriz ||
            _state == LetterState.FoldMidPause;
        public Transform RightGrip => rightFoldedGrip;
        public Transform LeftGrip => leftUnfoldedGrip;

        private void Awake()
        {
            ResolveReferences();
            CaptureInitialPoses();
            ForceResetToIdle();
        }

        private void Start()
        {
            ResolveReferences();
            CaptureInitialPoses();
            ForceResetToIdle();
        }

        private void OnEnable()
        {
            if (cameraLook != null)
            {
                cameraLook.OnFocusOwnerChanged += HandleCameraFocusOwnerChanged;
            }
        }

        private void OnDisable()
        {
            if (cameraLook != null)
            {
                cameraLook.OnFocusOwnerChanged -= HandleCameraFocusOwnerChanged;
            }
        }

        public void ForceResetToIdle()
        {
            _state = LetterState.Idle;
            _stateTimer = 0f;
            _readingTime = 0f;
            if (_posesCaptured)
            {
                transform.position = _homePos;
                transform.rotation = _homeRot;
                ApplyHingeAngles(horizFoldedAngle, vertFoldedAngle);
            }
            ApplyBlur(0f);
        }

        private void Update()
        {
            float scaledDt = Time.unscaledDeltaTime * Mathf.Clamp(animSpeedMultiplier, 0.25f, 3f);
            UpdateStateMachine(scaledDt, Time.unscaledDeltaTime);

            if (GameplayInputGate.IsBlocked)
            {
                return;
            }

            if (switchboardInteractor != null && switchboardInteractor.ActivePlug != null)
            {
                return;
            }

            if (_state == LetterState.Reading ||
                _state == LetterState.LeftHandJoin ||
                _state == LetterState.UnfoldingVert ||
                _state == LetterState.UnfoldMidPause ||
                _state == LetterState.UnfoldingHoriz ||
                _state == LetterState.BringingToCamera)
            {
                if ((closeOnRightClick && WasRightMousePressed()) ||
                    (closeOnEscape && WasEscapePressed()))
                {
                    BeginClose();
                }
                return;
            }

            if (_state == LetterState.Idle &&
                WasLeftMousePressed(out Vector2 mousePos) &&
                !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) &&
                IsClickOnLetter(mousePos))
            {
                BeginOpen();
            }
        }

        public void BeginOpen()
        {
            if (_state != LetterState.Idle) return;
            CaptureInitialPoses();
            ComputeReadPose(out _readPos, out _readRot);

            _state = LetterState.ReachingRight;
            _stateTimer = 0f;
            _readingTime = 0f;
        }

        public void BeginClose()
        {
            if (_state == LetterState.Idle ||
                _state == LetterState.FoldingHoriz ||
                _state == LetterState.FoldMidPause ||
                _state == LetterState.ReleasingLeft ||
                _state == LetterState.FoldingVert ||
                _state == LetterState.ReturningToHome ||
                _state == LetterState.SettlingAtHome)
            {
                return;
            }

            if (_state == LetterState.ReachingRight || _state == LetterState.GripPause)
            {
                ForceResetToIdle();
                if (cameraLook != null) cameraLook.ClearFocus(owner: this);
                return;
            }

            if (_state == LetterState.BringingToCamera || _state == LetterState.LeftHandJoin)
            {
                _phaseStartPos = transform.position;
                _phaseStartRot = transform.rotation;
                _state = LetterState.ReturningToHome;
                _stateTimer = 0f;
                if (cameraLook != null) cameraLook.ClearFocus(owner: this);
                return;
            }

            _state = (_state == LetterState.UnfoldingVert)
                ? LetterState.ReleasingLeft
                : LetterState.FoldingHoriz;
            _stateTimer = 0f;

            if (cameraLook != null)
            {
                cameraLook.ClearFocus(owner: this);
            }
        }

        private void UpdateStateMachine(float dt, float unscaledDt)
        {
            if (!_posesCaptured) return;

            switch (_state)
            {
                case LetterState.ReachingRight:
                {
                    // 1. Calm reach forward (~0.60s) — letter strictly stationary on switchboard!
                    transform.position = _homePos;
                    transform.rotation = _homeRot;
                    ApplyHingeAngles(horizFoldedAngle, vertFoldedAngle);

                    _stateTimer += dt;
                    bool physicallyTouching = handsController != null
                        ? handsController.IsRightHandTouchingGrip(rightFoldedGrip, reachTouchThresholdMeters)
                        : (_stateTimer >= reachDuration);
                    bool minTimeMet = _stateTimer >= reachDuration * 0.72f;
                    bool safetyDone = _stateTimer >= Mathf.Max(reachDuration, reachSafetyTimeout);

                    if ((physicallyTouching && minTimeMet) || safetyDone)
                    {
                        _state = LetterState.GripPause;
                        _stateTimer = 0f;
                    }
                    break;
                }

                case LetterState.GripPause:
                {
                    // 2. Tactile 0.14s micro-pause as fingers close cleanly onto the folded paper
                    transform.position = _homePos;
                    transform.rotation = _homeRot;
                    ApplyHingeAngles(horizFoldedAngle, vertFoldedAngle);

                    _stateTimer += dt;
                    if (_stateTimer >= gripPauseDuration)
                    {
                        _phaseStartPos = transform.position;
                        _phaseStartRot = transform.rotation;
                        _state = LetterState.BringingToCamera;
                        _stateTimer = 0f;

                        if (cameraLook != null)
                        {
                            cameraLook.FocusOn(
                                cameraLook.DefaultWorldPosition,
                                cameraLook.DefaultWorldRotation,
                                5f, 4f,
                                owner: this,
                                durationSeconds: bringDuration / Mathf.Clamp(animSpeedMultiplier, 0.25f, 3f));
                        }
                    }
                    break;
                }

                case LetterState.BringingToCamera:
                {
                    // 3. Deliberate curved Bezier pull from actual _phaseStartPos to _readPos (C0/C1 continuous seam!)
                    _stateTimer += dt;
                    float u = EaseInOutQuart(_stateTimer / Mathf.Max(0.08f, bringDuration));
                    Vector3 arcMid = Vector3.Lerp(_phaseStartPos, _readPos, 0.48f) + new Vector3(-0.025f, 0.045f, 0.02f);
                    transform.position = EvalQuadraticBezier(_phaseStartPos, arcMid, _readPos, u);
                    transform.rotation = Quaternion.Slerp(_phaseStartRot, _readRot, u);
                    ApplyHingeAngles(horizFoldedAngle, vertFoldedAngle);
                    ApplyBlur(u * 0.55f);

                    if (_stateTimer >= bringDuration)
                    {
                        transform.position = _readPos;
                        transform.rotation = _readRot;
                        _state = LetterState.LeftHandJoin;
                        _stateTimer = 0f;
                    }
                    break;
                }

                case LetterState.LeftHandJoin:
                {
                    // 4. Left hand smoothly glides up to leftUnfoldedGrip (~0.35s) before unfolding starts
                    transform.position = _readPos;
                    transform.rotation = _readRot;
                    ApplyHingeAngles(horizFoldedAngle, vertFoldedAngle);

                    _stateTimer += dt;
                    if (_stateTimer >= leftJoinDuration)
                    {
                        _state = LetterState.UnfoldingVert;
                        _stateTimer = 0f;
                    }
                    break;
                }

                case LetterState.UnfoldingVert:
                {
                    // 5a. First fold (VertHinge) opens unhurriedly (~0.55s) with EaseInOutQuart
                    _stateTimer += dt;
                    float u = EaseInOutQuart(_stateTimer / Mathf.Max(0.08f, unfoldVertDuration));
                    float vertAngle = Mathf.Lerp(vertFoldedAngle, 0f, u);
                    ApplyHingeAngles(horizFoldedAngle, vertAngle);
                    ApplyBlur(Mathf.Lerp(0.55f, 0.78f, u));

                    if (_stateTimer >= unfoldVertDuration)
                    {
                        ApplyHingeAngles(horizFoldedAngle, 0f);
                        _state = LetterState.UnfoldMidPause;
                        _stateTimer = 0f;
                    }
                    break;
                }

                case LetterState.UnfoldMidPause:
                {
                    // 5b. Human 0.12s pause between the two folds
                    ApplyHingeAngles(horizFoldedAngle, 0f);
                    _stateTimer += dt;
                    if (_stateTimer >= unfoldMidPauseDuration)
                    {
                        _state = LetterState.UnfoldingHoriz;
                        _stateTimer = 0f;
                    }
                    break;
                }

                case LetterState.UnfoldingHoriz:
                {
                    // 5c. Second fold (HorizHinges) opens smoothly (~0.60s) and settles gently into hands
                    _stateTimer += dt;
                    float u = EaseInOutQuart(_stateTimer / Mathf.Max(0.08f, unfoldHorizDuration));
                    float horizAngle = Mathf.Lerp(horizFoldedAngle, 0f, u);
                    ApplyHingeAngles(horizAngle, 0f);
                    ApplyBlur(Mathf.Lerp(0.78f, 1f, u));

                    // Gentle paper settle dip in the final 20%
                    float settle = Mathf.Sin(Mathf.InverseLerp(0.75f, 1f, _stateTimer / Mathf.Max(0.08f, unfoldHorizDuration)) * Mathf.PI) * -0.0022f;
                    transform.position = _readPos + Vector3.up * settle;

                    if (_stateTimer >= unfoldHorizDuration)
                    {
                        ApplyHingeAngles(0f, 0f);
                        ApplyBlur(1f);
                        transform.position = _readPos;
                        _state = LetterState.Reading;
                        _readingTime = 0f;
                    }
                    break;
                }

                case LetterState.Reading:
                {
                    // Organic two-frequency living breath so paper & hands never freeze like stone
                    _readingTime += unscaledDt;
                    float fade = EaseInOutCubic(Mathf.Clamp01(_readingTime / 0.8f));
                    float dy = Mathf.Sin(_readingTime * 1.15f) * readingBreathAmplitude * fade;
                    float dx = Mathf.Cos(_readingTime * 0.78f) * (readingBreathAmplitude * 0.6f) * fade;
                    float sway = Mathf.Sin(_readingTime * 0.92f) * 0.35f * fade;
                    transform.position = _readPos + new Vector3(dx, dy, 0f);
                    transform.rotation = _readRot * Quaternion.Euler(sway * 0.5f, sway, 0f);
                    break;
                }

                case LetterState.FoldingHoriz:
                {
                    // Close 1: 2nd fold closes smoothly (~0.50s) with EaseInOutQuart
                    _stateTimer += dt;
                    float u = EaseInOutQuart(_stateTimer / Mathf.Max(0.08f, foldHorizDuration));
                    float horizAngle = Mathf.Lerp(0f, horizFoldedAngle, u);
                    ApplyHingeAngles(horizAngle, 0f);
                    ApplyBlur(Mathf.Lerp(1f, 0.72f, u));

                    if (_stateTimer >= foldHorizDuration)
                    {
                        ApplyHingeAngles(horizFoldedAngle, 0f);
                        _state = LetterState.FoldMidPause;
                        _stateTimer = 0f;
                    }
                    break;
                }

                case LetterState.FoldMidPause:
                {
                    // Close 2: brief 0.10s pause before left hand lets go
                    ApplyHingeAngles(horizFoldedAngle, 0f);
                    _stateTimer += dt;
                    if (_stateTimer >= foldMidPauseDuration)
                    {
                        _state = LetterState.ReleasingLeft;
                        _stateTimer = 0f;
                    }
                    break;
                }

                case LetterState.ReleasingLeft:
                {
                    // Close 3: Left hand smoothly detaches & returns to desk (~0.35s) BEFORE VertHinge folds
                    ApplyHingeAngles(horizFoldedAngle, 0f);
                    _stateTimer += dt;
                    if (_stateTimer >= leftReleaseDelay)
                    {
                        _state = LetterState.FoldingVert;
                        _stateTimer = 0f;
                    }
                    break;
                }

                case LetterState.FoldingVert:
                {
                    // Close 4: 1st fold closes cleanly (~0.50s) with EaseInOutQuart
                    _stateTimer += dt;
                    float u = EaseInOutQuart(_stateTimer / Mathf.Max(0.08f, foldVertDuration));
                    float vertAngle = Mathf.Lerp(0f, vertFoldedAngle, u);
                    ApplyHingeAngles(horizFoldedAngle, vertAngle);
                    ApplyBlur(Mathf.Lerp(0.72f, 0.35f, u));

                    if (_stateTimer >= foldVertDuration)
                    {
                        ApplyHingeAngles(horizFoldedAngle, vertFoldedAngle);
                        // Capture exact live transform so ReturningToHome starts with zero seam jump!
                        _phaseStartPos = transform.position;
                        _phaseStartRot = transform.rotation;
                        _state = LetterState.ReturningToHome;
                        _stateTimer = 0f;
                    }
                    break;
                }

                case LetterState.ReturningToHome:
                {
                    // Close 5: Body & camera calmly lean forward as right hand places letter back (~0.80s), C0/C1 continuous!
                    _stateTimer += dt;
                    float u = EaseInOutQuart(_stateTimer / Mathf.Max(0.08f, returnDuration));
                    Vector3 arcMid = Vector3.Lerp(_phaseStartPos, _homePos, 0.52f) + new Vector3(-0.02f, 0.04f, 0.015f);
                    transform.position = EvalQuadraticBezier(_phaseStartPos, arcMid, _homePos, u);
                    transform.rotation = Quaternion.Slerp(_phaseStartRot, _homeRot, u);
                    ApplyHingeAngles(horizFoldedAngle, vertFoldedAngle);
                    ApplyBlur(Mathf.Lerp(0.35f, 0f, u));

                    if (_stateTimer >= returnDuration)
                    {
                        transform.position = _homePos;
                        transform.rotation = _homeRot;
                        ApplyHingeAngles(horizFoldedAngle, vertFoldedAngle);
                        ApplyBlur(0f);
                        _state = LetterState.SettlingAtHome;
                        _stateTimer = 0f;
                    }
                    break;
                }

                case LetterState.SettlingAtHome:
                {
                    // Close 6: 0.12s release micro-pause at rest position before right hand glides back to desk
                    transform.position = _homePos;
                    transform.rotation = _homeRot;
                    ApplyHingeAngles(horizFoldedAngle, vertFoldedAngle);
                    _stateTimer += dt;
                    if (_stateTimer >= settleReleasePause)
                    {
                        ForceResetToIdle();
                    }
                    break;
                }
            }
        }

        public void ApplyHingeAngles(float horizDeg, float vertDeg)
        {
            Vector3 hAx = horizHingeAxis.sqrMagnitude > 1e-5f ? horizHingeAxis.normalized : Vector3.right;
            if (horizHingeRight != null)
            {
                horizHingeRight.localRotation = Quaternion.AngleAxis(horizDeg, hAx);
            }
            if (horizHingeLeft != null)
            {
                horizHingeLeft.localRotation = Quaternion.AngleAxis(horizDeg, hAx);
            }

            if (vertHinge != null)
            {
                Vector3 vAx = vertHingeAxis.sqrMagnitude > 1e-5f ? vertHingeAxis.normalized : Vector3.up;
                vertHinge.localRotation = Quaternion.AngleAxis(vertDeg, vAx);

                float foldRatio = Mathf.Clamp01(Mathf.Abs(vertDeg) / Mathf.Max(1f, Mathf.Abs(vertFoldedAngle)));
                Vector3 lp = vertHinge.localPosition;
                lp.z = Mathf.Lerp(vertUnfoldedLocalZ, vertFoldedLocalZ, foldRatio);
                vertHinge.localPosition = lp;
            }
        }

        private void ComputeReadPose(out Vector3 pos, out Quaternion rot)
        {
            Camera cam = interactionCamera != null ? interactionCamera : Camera.main;
            Vector3 camPos = cameraLook != null ? cameraLook.DefaultWorldPosition : (cam != null ? cam.transform.position : Vector3.zero);
            Quaternion camRot = cameraLook != null ? cameraLook.DefaultWorldRotation : (cam != null ? cam.transform.rotation : Quaternion.identity);

            Vector3 camFwd = camRot * Vector3.forward;
            Vector3 camUp = camRot * Vector3.up;
            Vector3 camRight = camRot * Vector3.right;

            pos = camPos + camFwd * readDistanceFromCamera + camUp * readVerticalOffset;
            Vector3 sheetForward = Quaternion.AngleAxis(readTiltDegrees, camRight) * camFwd;
            Vector3 sheetUp = Vector3.ProjectOnPlane(camUp, sheetForward).normalized;
            rot = Quaternion.LookRotation(sheetForward.normalized, sheetUp);
        }

        private void ApplyBlur(float normalizedWeight)
        {
            if (backgroundBlurVolume != null &&
                (cameraLook == null || cameraLook.CurrentFocusOwner == null || ReferenceEquals(cameraLook.CurrentFocusOwner, this)))
            {
                backgroundBlurVolume.weight = Mathf.Clamp01(normalizedWeight) * maxBlurWeight;
            }
        }

        private void HandleCameraFocusOwnerChanged(object newOwner)
        {
            if (IsActive && !ReferenceEquals(newOwner, this))
            {
                BeginClose();
            }
        }

        private void CaptureInitialPoses()
        {
            if (_posesCaptured) return;
            _homePos = transform.position;
            _homeRot = transform.rotation;
            _posesCaptured = true;
        }

        private void ResolveReferences()
        {
            if (interactionCamera == null) interactionCamera = Camera.main;
            if (cameraLook == null && interactionCamera != null)
                cameraLook = interactionCamera.GetComponent<SmoothMouseLookCamera>();
            if (switchboardInteractor == null)
                switchboardInteractor = FindFirstObjectByType<SwitchboardInteractor>();
            if (handsController == null)
                handsController = FindFirstObjectByType<BookHandsIKController>();
        }

        private bool IsClickOnLetter(Vector2 mousePos)
        {
            Camera cam = interactionCamera != null ? interactionCamera : Camera.main;
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(mousePos);
            if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, raycastMask, QueryTriggerInteraction.Collide))
            {
                return false;
            }

            return hit.transform == transform || hit.transform.IsChildOf(transform);
        }

        private static Vector3 EvalQuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
        {
            float inv = 1f - t;
            return inv * inv * p0 + 2f * inv * t * p1 + t * t * p2;
        }

        private static float EaseInOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f
                ? 4f * t * t * t
                : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
        }

        private static float EaseInOutQuart(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f
                ? 8f * t * t * t * t
                : 1f - Mathf.Pow(-2f * t + 2f, 4f) * 0.5f;
        }

        private static bool WasLeftMousePressed(out Vector2 mousePosition)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                mousePosition = mouse.position.ReadValue();
                return true;
            }
            mousePosition = Vector2.zero;
            return false;
#else
            if (Input.GetMouseButtonDown(0))
            {
                mousePosition = Input.mousePosition;
                return true;
            }
            mousePosition = Vector2.zero;
            return false;
#endif
        }

        private static bool WasRightMousePressed()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            return mouse != null && mouse.rightButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }

        private static bool WasEscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
