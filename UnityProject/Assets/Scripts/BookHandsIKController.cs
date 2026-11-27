using LastSignal.StoryCore;
using LastSignal.SwitchboardSystem;
using UnityEngine;

namespace LastSignal
{
    /// <summary>
    /// Monolithic coordinator for Left and Right SimpleHandIK, Torso Lean, and ProceduralFingerGrip:
    /// - Keeps Idle animation, Switchboard Plugs, and base Book holding intact.
    /// - Radio: static radioKnobGrip with bounded [-25, +25] deg wrist rock (zero 360 deg flip).
    /// - Pencil: right hand takes pencil EVEN while left hand holds open book, using clean PencilPresetGrip.
    /// - Page Turn: frame-synced directly to LogbookSpreadController.TurnProgress01 along a compact lower-third arc (+2.8cm clearance).
    /// - Letter: cleanly drives two hands with left hand releasing before VertHinge folds.
    /// Runs LateUpdate at order 10 (strictly BEFORE SimpleHandIK at 20 and ProceduralFingerGrip at 30).
    /// </summary>
    [DefaultExecutionOrder(10)]
    public sealed class BookHandsIKController : MonoBehaviour
    {
        [Header("IK рук")]
        public SimpleHandIK leftHandIK;
        public SimpleHandIK rightHandIK;

        [Header("Процедурный хват пальцев (не используется для книги!)")]
        public ProceduralFingerGrip leftFingers;
        public ProceduralFingerGrip rightFingers;

        [Header("Привставание и наклон корпуса за дальними объектами (синхронно с камерой)")]
        public Transform torsoRoot;
        [Range(0f, 0.12f)] public float maxLeanDist = 0.095f;
        [Range(0f, 0.05f)] public float maxStandUpLiftY = 0.035f;
        [Range(0f, 10f)] public float maxLeanForwardAngle = 3.0f;
        [Range(0.05f, 0.55f)] public float leanSmoothTime = 0.30f;
        [HideInInspector] public float leanSpeed = 6.5f;

        [Header("Ссылки на книгу")]
        public BookInspectController bookInspectController;
        public LogbookSpreadController spreadController;

        [Header("Точки хвата на книге (дочерние к Logbook_Root.001)")]
        public Transform leftHandGrip;
        public Transform rightHandGrip;

        [Header("Плавность переходов IK")]
        [Min(0.5f)] public float pickUpBlendSpeed = 6.5f;
        [Min(0.05f)] public float putDownFollowDuration = 0.52f;
        [Min(0.5f)] public float putDownBlendSpeed = 4.5f;

        [Header("Компактное перелистывание страницы (нижняя треть, синхронно с листом)")]
        [Tooltip("Локальный X на книге для нижней трети страницы (не перекрывает текст по центру).")]
        public float pageTurnLowerThirdX = -0.078f;
        [Tooltip("Правый край старта/финиша дуги (локальный Y книги).")]
        public float pageTurnRightY = 0.145f;
        [Tooltip("Левый предел хода руки (~68% ширины разворота, чуть левее корешка).")]
        public float pageTurnLeftLimitY = -0.055f;
        [Tooltip("Отступ перед плоскостью листа к камере (+2.8 см), чтобы кисть не резала бумагу.")]
        [Range(0.015f, 0.05f)] public float pageTurnClearanceZ = 0.028f;
        [Tooltip("Высота дуги листа над книгой.")]
        [Range(0.03f, 0.12f)] public float pageTurnArcLiftZ = 0.068f;
        [Min(0.05f)] public float pageTurnReturnDuration = 0.20f;

        [Header("Штекеры коммутатора")]
        public SwitchboardInteractor switchboardInteractor;
        public SwitchboardPlug leftPlug;
        public Transform leftPlugHandGrip;
        public SwitchboardPlug rightPlug;
        public Transform rightPlugHandGrip;
        [Min(0.5f)] public float plugGrabBlendSpeed = 7.5f;
        [Min(0.5f)] public float plugReleaseBlendSpeed = 6.5f;
        public float plugFistBackOffset = 0.082f;
        public float plugFistDorsalOffset = 0.006f;

        [Header("Радио (крутилка настройки волны, ограниченный наклон кисти)")]
        public RadioTunerController radioTuner;
        public Transform radioKnobGrip;
        [Range(5f, 25f)] public float radioMaxWristRockDeg = 20f;

        [Header("Карандаш (анимация записи)")]
        public LogbookPencilAnimator pencilAnimator;
        public Transform pencilGrip;

        [Header("Интерактивное письмо")]
        public LetterInteraction letterInteraction;

        // Torso rest state & unified body lean offset (shared synchronously with CameraHandFollow!)
        private Vector3 _restTorsoLocalPos;
        private Quaternion _restTorsoLocalRot;
        private Vector3 _currentBodyOffset;
        private Vector3 _bodyOffsetVelocity;
        private Vector3 _leftShoulderInTorso;
        private Vector3 _rightShoulderInTorso;
        private float _leftArmTotalLen = 0.525f;
        private float _rightArmTotalLen = 0.525f;
        private bool _torsoCaptured;

        // Left hand desk-edge rest anchor (prevents idle left hand from pushing into switchboard while leaning right!)
        private Transform _leftDeskSupportTarget;
        private Vector3 _leftDeskRestWorldPos;
        private Quaternion _leftDeskRestWorldRot = Quaternion.identity;
        private bool _leftDeskRestCaptured;
        private bool _leftIsDeskSupporting;

        // Radio grip base rotation (static parent, bounded rock)
        private Quaternion _radioGripBaseLocalRot;
        private bool _radioGripCaptured;
        private float _radioRockCurrentDeg;

        private Transform _rightProxyTarget;
        private Transform _leftPlugProxyTarget;
        private Transform _rightPlugProxyTarget;
        private bool _wasInspecting;
        private float _putDownTimer;
        private bool _isPuttingDown;

        // Page turn smooth return tracking
        private bool _wasTurningPage;
        private float _pageTurnReturnTimer;
        private Vector3 _pageTurnLeavePos;
        private Quaternion _pageTurnLeaveRot;

        private bool _leftUsingPlug;
        private bool _rightUsingPlug;

        public float RightHandWeight => rightHandIK != null ? rightHandIK.SmoothedWeight : 0f;
        public float LeftHandWeight => leftHandIK != null ? leftHandIK.SmoothedWeight : 0f;
        public Vector3 CurrentBodyWorldOffset => _currentBodyOffset;
        public bool IsReadingLetter => letterInteraction != null && letterInteraction.IsReading;

        public bool IsRightHandTouchingGrip(Transform expectedGrip, float maxDistMeters = 0.045f)
        {
            if (rightHandIK == null || rightHandIK.hand == null || expectedGrip == null)
            {
                return false;
            }
            if (rightHandIK.SmoothedWeight < 0.72f)
            {
                return false;
            }
            return Vector3.Distance(rightHandIK.hand.position, expectedGrip.position) <= maxDistMeters;
        }

        /// <summary>
        /// Supplies the active hand reach target for CameraHandFollow (additive camera eye-look).
        /// Ignores left desk-support anchor so camera tracks the active reaching hand.
        /// </summary>
        public bool TryGetActiveReachTarget(out Vector3 worldTargetPos, out float activeWeight, out float sideSign, out bool isReadingBook)
        {
            worldTargetPos = Vector3.zero;
            activeWeight = 0f;
            sideSign = 0f;
            isReadingBook = bookInspectController != null && bookInspectController.IsInspecting;

            float lw = (leftHandIK != null && !_leftIsDeskSupporting) ? leftHandIK.SmoothedWeight : 0f;
            float rw = (rightHandIK != null) ? rightHandIK.SmoothedWeight : 0f;

            if (rw > 0.1f && rw >= lw)
            {
                worldTargetPos = rightHandIK.SmoothedTargetPosition;
                activeWeight = rw;
                sideSign = 1f;
                return true;
            }
            if (lw > 0.1f)
            {
                worldTargetPos = leftHandIK.SmoothedTargetPosition;
                activeWeight = lw;
                sideSign = -1f;
                return true;
            }
            return false;
        }

        private void Awake()
        {
            ResolveReferences();
            CaptureTorsoRestPose();
            CaptureRadioGripBase();
            EnsureRightProxyTarget();
            EnsurePlugProxyTargets();
            EnsureLeftDeskSupportTarget();
            ResetHandWeightsToIdle();
        }

        private void Start()
        {
            CaptureTorsoRestPose();
            CaptureRadioGripBase();
            CaptureLeftDeskRestPose();
            ResetHandWeightsToIdle();
        }

        private void ResetHandWeightsToIdle()
        {
            if (leftHandIK != null)
            {
                leftHandIK.ResetImmediate();
            }
            if (rightHandIK != null)
            {
                rightHandIK.ResetImmediate();
            }
            if (leftFingers != null)
            {
                leftFingers.ResetAnglesImmediate();
            }
            if (rightFingers != null)
            {
                rightFingers.ResetAnglesImmediate();
            }
            if (_torsoCaptured && torsoRoot != null)
            {
                torsoRoot.localPosition = _restTorsoLocalPos;
                torsoRoot.localRotation = _restTorsoLocalRot;
            }

            _currentBodyOffset = Vector3.zero;
            _bodyOffsetVelocity = Vector3.zero;
            _leftIsDeskSupporting = false;
            if (_radioGripCaptured && radioKnobGrip != null)
            {
                radioKnobGrip.localRotation = _radioGripBaseLocalRot;
            }

            _radioRockCurrentDeg = 0f;
            _wasInspecting = false;
            _isPuttingDown = false;
            _putDownTimer = 0f;
            _wasTurningPage = false;
            _pageTurnReturnTimer = 0f;
            _leftUsingPlug = false;
            _rightUsingPlug = false;
        }

        private void OnDestroy()
        {
            if (_rightProxyTarget != null) Destroy(_rightProxyTarget.gameObject);
            if (_leftPlugProxyTarget != null) Destroy(_leftPlugProxyTarget.gameObject);
            if (_rightPlugProxyTarget != null) Destroy(_rightPlugProxyTarget.gameObject);
            if (_leftDeskSupportTarget != null) Destroy(_leftDeskSupportTarget.gameObject);
        }

        private void Update()
        {
            if (leftHandIK == null && rightHandIK == null)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            bool pencilWriting = pencilAnimator != null && pencilAnimator.IsWriting && pencilGrip != null;

            // Enable subtle living hand breathing when holding the open book or reading the letter
            bool breatheHold = (bookInspectController != null && bookInspectController.IsInspecting) || IsReadingLetter;
            if (leftHandIK != null) leftHandIK.enableHandBreathing = breatheHold;
            if (rightHandIK != null) rightHandIK.enableHandBreathing = breatheHold && !pencilWriting;

            // Smoothly return radio grip rock angle to 0 when not turning radio
            bool radioActive = radioTuner != null && radioTuner.IsTurningOrHolding && radioKnobGrip != null;
            if (!radioActive && _radioGripCaptured && radioKnobGrip != null && Mathf.Abs(_radioRockCurrentDeg) > 0.05f)
            {
                _radioRockCurrentDeg = Mathf.MoveTowards(_radioRockCurrentDeg, 0f, dt * 120f);
                ApplyRadioGripRock(_radioRockCurrentDeg);
            }

            // --- 1. BOOK (+ PENCIL OVERRIDE FOR RIGHT HAND WHILE HOLDING BOOK!) ---
            bool inspecting = bookInspectController != null && bookInspectController.IsInspecting;
            if (inspecting && !_wasInspecting)
            {
                _isPuttingDown = false;
                _putDownTimer = 0f;
                _wasTurningPage = false;
                _pageTurnReturnTimer = 0f;
                SyncRightProxyToGrip();
            }
            else if (!inspecting && _wasInspecting)
            {
                _isPuttingDown = true;
                _putDownTimer = putDownFollowDuration;
                _wasTurningPage = false;
                _pageTurnReturnTimer = 0f;
                SyncRightProxyToGrip();
            }
            _wasInspecting = inspecting;

            bool bookActive = inspecting || _isPuttingDown;
            if (bookActive)
            {
                _leftUsingPlug = false;
                _rightUsingPlug = false;
                _leftIsDeskSupporting = false;
                if (leftFingers != null) leftFingers.SetPose(HandPose.Idle);

                // Left hand ALWAYS holds book left edge cleanly
                if (leftHandIK != null)
                {
                    leftHandIK.clampWristRotation = false;
                    if (leftHandGrip != null) leftHandIK.target = leftHandGrip;
                }

                float bookTargetWeight;
                float bookSpeed;
                if (inspecting)
                {
                    bookTargetWeight = 1f;
                    bookSpeed = pickUpBlendSpeed;
                }
                else
                {
                    _putDownTimer -= dt;
                    if (_putDownTimer > 0f)
                    {
                        bookTargetWeight = 1f;
                        bookSpeed = pickUpBlendSpeed;
                    }
                    else
                    {
                        bookTargetWeight = 0f;
                        bookSpeed = putDownBlendSpeed;
                        bool leftIdle = leftHandIK == null || leftHandIK.SmoothedWeight <= 0.01f;
                        bool rightIdle = rightHandIK == null || rightHandIK.SmoothedWeight <= 0.01f;
                        if (leftIdle && rightIdle)
                        {
                            _isPuttingDown = false;
                        }
                    }
                }

                if (leftHandIK != null)
                {
                    leftHandIK.weight = bookTargetWeight;
                }

                // Right hand: if pencil is writing, right hand grabs pencilGrip with clean preset!
                // Otherwise, right hand holds book edge OR follows synchronized compact page-turn arc.
                if (rightHandIK != null)
                {
                    if (pencilWriting)
                    {
                        _wasTurningPage = false;
                        _pageTurnReturnTimer = 0f;
                        rightHandIK.clampWristRotation = true;
                        rightHandIK.target = pencilGrip;
                        rightHandIK.weight = 1f;
                        if (rightFingers != null) rightFingers.SetPose(HandPose.PencilWrite);
                    }
                    else
                    {
                        if (rightFingers != null) rightFingers.SetPose(HandPose.Idle);
                        rightHandIK.clampWristRotation = false;
                        UpdateBookRightHandProxy(inspecting, dt);
                        if (_rightProxyTarget != null) rightHandIK.target = _rightProxyTarget;
                        rightHandIK.weight = bookTargetWeight;
                    }
                }
                return;
            }

            // --- 2. PENCIL WRITING WHEN BOOK IS CLOSED ON DESK (RIGHT HAND) ---
            if (pencilWriting)
            {
                _leftUsingPlug = false;
                _rightUsingPlug = false;

                if (rightHandIK != null)
                {
                    rightHandIK.target = pencilGrip;
                    rightHandIK.clampWristRotation = true;
                    rightHandIK.weight = 1f;
                    if (rightFingers != null) rightFingers.SetPose(HandPose.PencilWrite);
                }

                UpdateIdleLeftHandOrDeskSupport(dt);
                return;
            }

            // --- 3. INTERACTIVE LETTER (TWO HANDS) ---
            if (letterInteraction != null && letterInteraction.IsActive)
            {
                _leftUsingPlug = false;
                _rightUsingPlug = false;

                if (rightHandIK != null)
                {
                    if (letterInteraction.WantsRightHand && letterInteraction.RightGrip != null)
                    {
                        rightHandIK.target = letterInteraction.RightGrip;
                        rightHandIK.clampWristRotation = true;
                        rightHandIK.weight = 1f;
                        if (rightFingers != null) rightFingers.SetPose(HandPose.LetterPinch);
                    }
                    else
                    {
                        ReleaseHandSmooth(rightHandIK, rightFingers, dt);
                    }
                }

                if (leftHandIK != null)
                {
                    if (letterInteraction.WantsLeftHand && letterInteraction.LeftGrip != null)
                    {
                        _leftIsDeskSupporting = false;
                        leftHandIK.target = letterInteraction.LeftGrip;
                        leftHandIK.clampWristRotation = true;
                        leftHandIK.weight = 1f;
                        if (leftFingers != null) leftFingers.SetPose(HandPose.LetterPinch);
                    }
                    else
                    {
                        // Plant left hand on desk edge while leaning right, or smoothly return to pure Idle once seated!
                        UpdateIdleLeftHandOrDeskSupport(dt);
                    }
                }
                return;
            }

            // --- 4. RADIO TUNING KNOB (RIGHT HAND, STATIC GRIP + BOUNDED [-25, +25] WRIST ROCK) ---
            if (radioActive)
            {
                _leftUsingPlug = false;
                _rightUsingPlug = false;

                CaptureRadioGripBase();
                float maxRock = Mathf.Clamp(radioMaxWristRockDeg, 0f, 25f);
                float targetRock = Mathf.Sin(radioTuner.KnobAngleDegrees * Mathf.Deg2Rad) * maxRock;
                _radioRockCurrentDeg = Mathf.Lerp(_radioRockCurrentDeg, targetRock, 1f - Mathf.Exp(-14f * dt));
                ApplyRadioGripRock(_radioRockCurrentDeg);

                if (rightHandIK != null)
                {
                    rightHandIK.target = radioKnobGrip;
                    rightHandIK.clampWristRotation = true;
                    rightHandIK.weight = 1f;
                    if (rightFingers != null) rightFingers.SetPose(HandPose.RadioKnob);
                }

                UpdateIdleLeftHandOrDeskSupport(dt);
                return;
            }

            // --- 5. SWITCHBOARD PLUGS (STRICT LEFT->LEFT, RIGHT->RIGHT) ---
            EnsurePlugProxyTargets();
            bool leftPlugPicked = leftPlug != null && (leftPlug.IsPickedUp || (switchboardInteractor != null && switchboardInteractor.ActivePlug == leftPlug));
            if (leftPlugPicked || _leftUsingPlug)
            {
                _leftIsDeskSupporting = false;
                UpdatePlugHand(leftHandIK, leftFingers, leftPlug, leftPlugHandGrip, _leftPlugProxyTarget, true, dt, ref _leftUsingPlug);
            }
            else
            {
                UpdateIdleLeftHandOrDeskSupport(dt);
            }
            UpdatePlugHand(rightHandIK, rightFingers, rightPlug, rightPlugHandGrip, _rightPlugProxyTarget, false, dt, ref _rightUsingPlug);
        }

        /// <summary>
        /// When left hand is free (Idle) while torso stands/leans forward (> 0.03m) for right-hand reach (letter/radio/right plug),
        /// braces the left hand on its captured desk-edge rest point so it leans naturally on the desk and NEVER clips into the switchboard!
        /// </summary>
        private void UpdateIdleLeftHandOrDeskSupport(float dt)
        {
            if (leftHandIK == null) return;

            float leanMag = _currentBodyOffset.magnitude;
            bool rightActive = rightHandIK != null && (rightHandIK.weight > 0.05f || rightHandIK.SmoothedWeight > 0.05f);

            if (_leftDeskRestCaptured && _leftDeskSupportTarget != null && rightActive && leanMag > 0.025f)
            {
                _leftIsDeskSupporting = true;
                _leftDeskSupportTarget.position = _leftDeskRestWorldPos;
                _leftDeskSupportTarget.rotation = _leftDeskRestWorldRot;
                leftHandIK.target = _leftDeskSupportTarget;
                leftHandIK.clampWristRotation = true;
                leftHandIK.weight = Mathf.Clamp01((leanMag - 0.015f) / 0.075f);
                if (leftFingers != null) leftFingers.SetPose(HandPose.Idle);
            }
            else
            {
                _leftIsDeskSupporting = false;
                ReleaseHandSmooth(leftHandIK, leftFingers, dt);
            }
        }

        private void UpdateBookRightHandProxy(bool inspecting, float dt)
        {
            if (_rightProxyTarget == null || rightHandGrip == null || bookInspectController == null)
            {
                return;
            }

            bool turning = inspecting && spreadController != null && spreadController.IsTurningPage;
            if (turning)
            {
                _wasTurningPage = true;
                _pageTurnReturnTimer = 0f;

                Transform book = bookInspectController.transform;
                float t = Mathf.Clamp01(spreadController.TurnProgress01);
                bool forward = spreadController.TurningForward;

                // Compact arc strictly along lower third of the page (+2.8cm clearance in front of leaf)
                float startY = forward ? pageTurnRightY : pageTurnLeftLimitY;
                float endY = forward ? pageTurnLeftLimitY : pageTurnRightY;
                float curY = Mathf.Lerp(startY, endY, t);

                float archSin = Mathf.Sin(t * Mathf.PI);
                float surfaceZ = LogbookPencilAnimator.SampleCurvedPageSurfaceZ(Mathf.Abs(curY));
                float curZ = surfaceZ + archSin * pageTurnArcLiftZ + pageTurnClearanceZ;

                // Offset wrist slightly downward-right so fingers guide the lower edge cleanly without covering center text
                Vector3 localContact = new Vector3(pageTurnLowerThirdX, curY, curZ);
                Vector3 worldContact = book.TransformPoint(localContact);
                Vector3 fingerDir = (book.right * 0.62f - book.up * Mathf.Lerp(0.55f, 0.25f, forward ? t : (1f - t)) - book.forward * 0.22f).normalized;
                Vector3 dorsalUp = Vector3.ProjectOnPlane(book.forward * 0.92f + book.up * 0.20f, fingerDir).normalized;

                _rightProxyTarget.position = worldContact - fingerDir * 0.068f;
                _rightProxyTarget.rotation = Quaternion.LookRotation(fingerDir, dorsalUp);
                return;
            }

            if (_wasTurningPage)
            {
                _wasTurningPage = false;
                _pageTurnReturnTimer = Mathf.Max(0.05f, pageTurnReturnDuration);
                _pageTurnLeavePos = _rightProxyTarget.position;
                _pageTurnLeaveRot = _rightProxyTarget.rotation;
            }

            if (_pageTurnReturnTimer > 0f)
            {
                _pageTurnReturnTimer = Mathf.Max(0f, _pageTurnReturnTimer - dt);
                float u = SmoothStep(1f - (_pageTurnReturnTimer / Mathf.Max(0.05f, pageTurnReturnDuration)));
                Transform book = bookInspectController.transform;
                Vector3 ctrl = Vector3.Lerp(_pageTurnLeavePos, rightHandGrip.position, 0.5f) + book.forward * 0.025f;
                _rightProxyTarget.position = EvalQuadraticBezier(_pageTurnLeavePos, ctrl, rightHandGrip.position, u);
                _rightProxyTarget.rotation = Quaternion.Slerp(_pageTurnLeaveRot, rightHandGrip.rotation, u);
                return;
            }

            SyncRightProxyToGrip();
        }

        private void ApplyRadioGripRock(float rockDeg)
        {
            if (!_radioGripCaptured || radioKnobGrip == null) return;
            float clamped = Mathf.Clamp(rockDeg, -25f, 25f);
            // Rock smoothly around grip's local forward axis (+Z, aligned with knob shaft)
            radioKnobGrip.localRotation = _radioGripBaseLocalRot * Quaternion.AngleAxis(clamped, Vector3.forward);
        }

        private void CaptureRadioGripBase()
        {
            if (_radioGripCaptured || radioKnobGrip == null) return;
            _radioGripBaseLocalRot = radioKnobGrip.localRotation;
            _radioGripCaptured = true;
        }

        private void LateUpdate()
        {
            UpdateTorsoLean(Time.unscaledDeltaTime);
        }

        private void UpdateTorsoLean(float dt)
        {
            if (!_torsoCaptured || torsoRoot == null)
            {
                return;
            }

            // Capture left hand's true idle desk-edge position on first idle frame if not yet captured
            if (!_leftDeskRestCaptured && leftHandIK != null && leftHandIK.hand != null && leftHandIK.SmoothedWeight < 0.01f)
            {
                CaptureLeftDeskRestPose();
            }

            Transform parent = torsoRoot.parent;
            Vector3 desiredWorldOffset = Vector3.zero;
            float maxExcess = 0f;

            // Only evaluate body lean for truly active task hands (never for the desk-support anchor!)
            if (!_leftIsDeskSupporting)
            {
                EvaluateHandLean(leftHandIK, _leftShoulderInTorso, _leftArmTotalLen, parent, ref desiredWorldOffset, ref maxExcess);
            }
            EvaluateHandLean(rightHandIK, _rightShoulderInTorso, _rightArmTotalLen, parent, ref desiredWorldOffset, ref maxExcess);

            // Smooth unified _currentBodyOffset via SmoothDamp (0.18s) — shared identically by Torso AND CameraHandFollow!
            if (dt > 0f)
            {
                _currentBodyOffset = Vector3.SmoothDamp(
                    _currentBodyOffset,
                    desiredWorldOffset,
                    ref _bodyOffsetVelocity,
                    Mathf.Max(0.04f, leanSmoothTime),
                    Mathf.Infinity,
                    dt);
            }

            if (desiredWorldOffset == Vector3.zero && _currentBodyOffset.sqrMagnitude < 1e-7f)
            {
                _currentBodyOffset = Vector3.zero;
                _bodyOffsetVelocity = Vector3.zero;
            }

            Vector3 localOffset = parent != null
                ? parent.InverseTransformVector(_currentBodyOffset)
                : _currentBodyOffset;
            torsoRoot.localPosition = _restTorsoLocalPos + localOffset;

            // Gentle forward pitch while leaning/standing up (no artificial Z-cap — full physical reach!)
            Quaternion targetLocalRot = _restTorsoLocalRot;
            float currentMag = _currentBodyOffset.magnitude;
            if (currentMag > 0.002f)
            {
                float leanRatio = Mathf.Clamp01(currentMag / Mathf.Max(0.05f, maxLeanDist));
                float pitchDeg = leanRatio * maxLeanForwardAngle;
                Vector3 leanHoriz = new Vector3(_currentBodyOffset.x, 0f, _currentBodyOffset.z);
                if (leanHoriz.sqrMagnitude > 1e-5f)
                {
                    Vector3 worldTiltAxis = Vector3.Cross(Vector3.up, leanHoriz.normalized).normalized;
                    Vector3 localTiltAxis = parent != null
                        ? parent.InverseTransformDirection(worldTiltAxis)
                        : worldTiltAxis;
                    targetLocalRot = Quaternion.AngleAxis(pitchDeg, localTiltAxis) * _restTorsoLocalRot;
                }
            }

            if (dt > 0f)
            {
                float rotT = 1f - Mathf.Exp(-(1f / Mathf.Max(0.04f, leanSmoothTime)) * dt);
                torsoRoot.localRotation = Quaternion.Slerp(torsoRoot.localRotation, targetLocalRot, rotT);
            }

            if (currentMag <= 0.001f && Quaternion.Angle(torsoRoot.localRotation, _restTorsoLocalRot) < 0.05f)
            {
                torsoRoot.localPosition = _restTorsoLocalPos;
                torsoRoot.localRotation = _restTorsoLocalRot;
            }
        }

        private void EvaluateHandLean(
            SimpleHandIK ik,
            Vector3 shoulderLocalInTorso,
            float invariantArmLen,
            Transform torsoParent,
            ref Vector3 bestWorldOffset,
            ref float maxExcess)
        {
            if (ik == null || ik.target == null)
            {
                return;
            }

            // ZERO binary threshold steps and ZERO forward kick on release!
            // When the hand has released (ik.weight <= 0), targetLean is strictly Vector3.zero.
            // While reaching/holding (ik.weight > 0), multiply raw lean continuously by ik.SmoothedWeight (smooth 0 -> 1 curve).
            if (ik.weight <= 0.001f)
            {
                return;
            }

            float continuousWeight = Mathf.Clamp01(ik.SmoothedWeight);
            if (continuousWeight <= 0.0001f)
            {
                return;
            }

            // Compute distance strictly from IMMUTABLE rest shoulder world pose (NEVER live upperArm.position -> zero feedback loop!)
            Vector3 restShoulderInParent = _restTorsoLocalPos + _restTorsoLocalRot * Vector3.Scale(shoulderLocalInTorso, torsoRoot.localScale);
            Vector3 restShoulderWorld = torsoParent != null
                ? torsoParent.TransformPoint(restShoulderInParent)
                : restShoulderInParent;

            float reachThreshold = invariantArmLen * 0.88f;
            Vector3 toTarget = ik.target.position - restShoulderWorld;
            float dist = toTarget.magnitude;

            if (dist > reachThreshold && dist > 1e-5f)
            {
                float cappedMaxLean = Mathf.Clamp(maxLeanDist, 0f, 0.12f);
                float rawExcess = Mathf.Clamp(dist - reachThreshold, 0f, cappedMaxLean);
                float weightedExcess = rawExcess * continuousWeight;
                if (weightedExcess > maxExcess)
                {
                    maxExcess = weightedExcess;
                    Vector3 dir = toTarget / dist;
                    Vector3 reachDir = new Vector3(dir.x, dir.y * 0.20f, dir.z);
                    if (reachDir.sqrMagnitude > 1e-5f)
                    {
                        reachDir.Normalize();
                    }
                    float cappedMaxLift = Mathf.Clamp(maxStandUpLiftY, 0f, 0.035f);
                    float liftY = Mathf.Clamp(weightedExcess * 0.35f, 0f, cappedMaxLift);
                    Vector3 candidate = reachDir * weightedExcess + Vector3.up * liftY;

                    // Strictly cap horizontal & vertical body displacement so shoulders NEVER overtake the camera
                    Vector2 horiz = new Vector2(candidate.x, candidate.z);
                    if (horiz.magnitude > cappedMaxLean)
                    {
                        horiz = horiz.normalized * cappedMaxLean;
                        candidate.x = horiz.x;
                        candidate.z = horiz.y;
                    }
                    candidate.y = Mathf.Clamp(candidate.y, -cappedMaxLift * 0.5f, cappedMaxLift);
                    bestWorldOffset = candidate;
                }
            }
        }

        private void ReleaseHandSmooth(SimpleHandIK ik, ProceduralFingerGrip fingers, float dt)
        {
            if (fingers != null)
            {
                fingers.SetPose(HandPose.Idle);
            }
            if (ik != null)
            {
                // Let SimpleHandIK SmoothDamp _smoothWeight cleanly to 0 while coasting from last pose (zero air snap!)
                ik.weight = 0f;
                if (ik.SmoothedWeight <= 0.01f)
                {
                    ik.target = null;
                    ik.clampWristRotation = false;
                }
            }
        }

        private void UpdatePlugHand(
            SimpleHandIK ik,
            ProceduralFingerGrip fingers,
            SwitchboardPlug plug,
            Transform plugGrip,
            Transform proxyTarget,
            bool isLeft,
            float dt,
            ref bool usingPlug)
        {
            if (ik == null)
            {
                return;
            }

            bool picked = plug != null && (plug.IsPickedUp || (switchboardInteractor != null && switchboardInteractor.ActivePlug == plug));

            if (picked && plugGrip != null && proxyTarget != null)
            {
                usingPlug = true;
                UpdatePlugFistProxy(ik, plugGrip, proxyTarget, isLeft);
                ik.clampWristRotation = true;
                ik.target = proxyTarget;
                ik.weight = 1f;
                if (fingers != null)
                {
                    fingers.SetPose(HandPose.PlugPinch);
                }
            }
            else
            {
                if (fingers != null)
                {
                    fingers.SetPose(HandPose.Idle);
                }

                ik.weight = 0f;
                if (ik.SmoothedWeight <= 0.01f)
                {
                    ik.clampWristRotation = false;
                    usingPlug = false;
                }
            }
        }

        private void UpdatePlugFistProxy(SimpleHandIK ik, Transform plugGrip, Transform proxyTarget, bool isLeft)
        {
            Vector3 handlePos = plugGrip.position;
            Vector3 shoulderPos = ik.upperArm != null ? ik.upperArm.position : (handlePos + new Vector3(isLeft ? 0.12f : -0.12f, -0.05f, 0.55f));
            Vector3 toHandle = handlePos - shoulderPos;
            Vector3 flatForward = new Vector3(toHandle.x, 0f, toHandle.z);
            if (flatForward.sqrMagnitude < 1e-5f)
            {
                flatForward = Vector3.back;
            }
            flatForward.Normalize();

            float pitchY = Mathf.Clamp(toHandle.y * 0.35f, -0.22f, 0.20f);
            Vector3 inwardSide = isLeft ? Vector3.left : Vector3.right;
            Vector3 fingerDir = (flatForward + inwardSide * 0.24f + Vector3.up * pitchY).normalized;

            Vector3 outwardSide = isLeft ? Vector3.right : Vector3.left;
            Vector3 rawDorsal = (Vector3.up * 0.62f + outwardSide * 0.78f).normalized;
            Vector3 dorsalUp = Vector3.ProjectOnPlane(rawDorsal, fingerDir);
            if (dorsalUp.sqrMagnitude < 1e-5f)
            {
                dorsalUp = Vector3.ProjectOnPlane(Vector3.up, fingerDir);
            }
            dorsalUp.Normalize();

            proxyTarget.position = handlePos - fingerDir * plugFistBackOffset + dorsalUp * plugFistDorsalOffset - Vector3.up * 0.012f;
            proxyTarget.rotation = Quaternion.LookRotation(fingerDir, dorsalUp);
        }

        private void EnsurePlugProxyTargets()
        {
            if (_leftPlugProxyTarget == null)
            {
                var go = new GameObject("LeftPlugIK_RuntimeTarget");
                go.hideFlags = HideFlags.DontSave;
                _leftPlugProxyTarget = go.transform;
            }
            if (_rightPlugProxyTarget == null)
            {
                var go = new GameObject("RightPlugIK_RuntimeTarget");
                go.hideFlags = HideFlags.DontSave;
                _rightPlugProxyTarget = go.transform;
            }
        }

        private void SyncRightProxyToGrip()
        {
            if (_rightProxyTarget != null && rightHandGrip != null)
            {
                _rightProxyTarget.position = rightHandGrip.position;
                _rightProxyTarget.rotation = rightHandGrip.rotation;
            }
        }

        private void EnsureRightProxyTarget()
        {
            if (_rightProxyTarget != null) return;
            var go = new GameObject("RightHandIK_RuntimeTarget");
            go.hideFlags = HideFlags.DontSave;
            _rightProxyTarget = go.transform;
            SyncRightProxyToGrip();
        }

        private void EnsureLeftDeskSupportTarget()
        {
            if (_leftDeskSupportTarget != null) return;
            var go = new GameObject("LeftDeskSupport_RuntimeTarget");
            go.hideFlags = HideFlags.DontSave;
            _leftDeskSupportTarget = go.transform;
        }

        private void CaptureLeftDeskRestPose()
        {
            if (_leftDeskRestCaptured || leftHandIK == null || leftHandIK.hand == null) return;
            _leftDeskRestWorldPos = leftHandIK.hand.position;
            _leftDeskRestWorldRot = Quaternion.LookRotation(leftHandIK.hand.up, leftHandIK.hand.forward);
            EnsureLeftDeskSupportTarget();
            _leftDeskSupportTarget.position = _leftDeskRestWorldPos;
            _leftDeskSupportTarget.rotation = _leftDeskRestWorldRot;
            _leftDeskRestCaptured = true;
        }

        private void CaptureTorsoRestPose()
        {
            if (_torsoCaptured) return;
            if (torsoRoot == null)
            {
                torsoRoot = ResolveTorsoRoot();
            }
            if (torsoRoot == null) return;

            _restTorsoLocalPos = torsoRoot.localPosition;
            _restTorsoLocalRot = torsoRoot.localRotation;

            if (leftHandIK != null && leftHandIK.upperArm != null)
            {
                _leftShoulderInTorso = torsoRoot.InverseTransformPoint(leftHandIK.upperArm.position);
                _leftArmTotalLen = leftHandIK.UpperArmLength + leftHandIK.ForearmLength;
            }
            if (rightHandIK != null && rightHandIK.upperArm != null)
            {
                _rightShoulderInTorso = torsoRoot.InverseTransformPoint(rightHandIK.upperArm.position);
                _rightArmTotalLen = rightHandIK.UpperArmLength + rightHandIK.ForearmLength;
            }

            _torsoCaptured = true;
        }

        private Transform ResolveTorsoRoot()
        {
            if (leftHandIK != null && leftHandIK.upperArm != null && rightHandIK != null && rightHandIK.upperArm != null)
            {
                Transform a = leftHandIK.upperArm.parent;
                while (a != null)
                {
                    if (rightHandIK.upperArm.IsChildOf(a))
                    {
                        return a;
                    }
                    a = a.parent;
                }
            }
            return transform;
        }

        private void ResolveReferences()
        {
            if (bookInspectController == null)
                bookInspectController = FindFirstObjectByType<BookInspectController>();
            if (spreadController == null && bookInspectController != null)
                spreadController = bookInspectController.GetComponent<LogbookSpreadController>();
            if (switchboardInteractor == null)
                switchboardInteractor = FindFirstObjectByType<SwitchboardInteractor>();
            if (radioTuner == null)
                radioTuner = FindFirstObjectByType<RadioTunerController>();
            if (pencilAnimator == null)
                pencilAnimator = FindFirstObjectByType<LogbookPencilAnimator>();
            if (letterInteraction == null)
                letterInteraction = FindFirstObjectByType<LetterInteraction>();

            if (leftFingers == null && leftHandIK != null)
                leftFingers = leftHandIK.GetComponent<ProceduralFingerGrip>();
            if (rightFingers == null && rightHandIK != null)
                rightFingers = rightHandIK.GetComponent<ProceduralFingerGrip>();

            if (leftPlug == null || rightPlug == null)
            {
                SwitchboardPlug[] plugs = FindObjectsByType<SwitchboardPlug>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (plugs.Length >= 2)
                {
                    System.Array.Sort(plugs, (a, b) => b.transform.position.x.CompareTo(a.transform.position.x));
                    if (leftPlug == null) leftPlug = plugs[0];
                    if (rightPlug == null) rightPlug = plugs[1];
                }
            }

            if (leftPlugHandGrip == null && leftPlug != null)
                leftPlugHandGrip = leftPlug.transform.Find("handGrip");
            if (rightPlugHandGrip == null && rightPlug != null)
                rightPlugHandGrip = rightPlug.transform.Find("handGrip");

            if (radioKnobGrip == null && radioTuner != null)
            {
                radioKnobGrip = radioTuner.transform.Find("radioKnobGrip");
                if (radioKnobGrip == null && radioTuner.TuningKnob != null)
                    radioKnobGrip = radioTuner.TuningKnob.Find("radioKnobGrip");
            }
            if (pencilGrip == null && pencilAnimator != null)
                pencilGrip = pencilAnimator.transform.Find("pencilGrip");
        }

        private static Vector3 EvalQuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
        {
            float inv = 1f - t;
            return inv * inv * p0 + 2f * inv * t * p1 + t * t * p2;
        }

        private static float SmoothStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
