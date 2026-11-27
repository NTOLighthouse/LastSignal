using UnityEngine;

namespace LastSignal
{
    /// <summary>
    /// Analytical 2-bone IK (Law of Cosines) with built-in virtual smoothed target (SmoothDamp on position & weight)
    /// to eliminate start/stop jerks and target-switching snaps.
    /// Execution Order 20: runs strictly AFTER item movers (0) & torso lean (10), BEFORE fingers (30) & camera follow (40).
    /// NEVER modifies upperArm.position directly (only rotations of upperArm, forearm, hand).
    /// </summary>
    [DefaultExecutionOrder(20)]
    public sealed class SimpleHandIK : MonoBehaviour
    {
        [Header("Кости руки")]
        public Transform upperArm;
        public Transform forearm;
        public Transform hand;

        [Header("Таргеты")]
        public Transform target;
        public Transform pole;

        [Header("Настройки IK")]
        public bool isLeftHand = false;
        public bool matchTargetRotation = true;
        [Range(0f, 1f)] public float weight = 0f;
        [Range(0f, 0.8f)] public float forearmTwistShare = 0.45f;
        public Vector3 handRotationOffsetEuler = Vector3.zero;

        [Header("Тяжёлая плавность рук (Metro SmoothDamp)")]
        [Tooltip("Время сглаживания позиции виртуального таргета (сек, 0.14-0.18 для увесистого хода).")]
        [Range(0.02f, 0.45f)] public float positionSmoothTime = 0.16f;
        [Tooltip("Время сглаживания веса IK при отрыве/возврате на стол (сек, 0.24-0.28).")]
        [Range(0.04f, 0.55f)] public float weightSmoothTime = 0.26f;
        [Tooltip("Скорость сглаживания поворота кисти (7-9 для плавного тактильного вращения).")]
        [Range(3f, 25f)] public float rotationSmoothSpeed = 8f;

        [Header("Живое дыхание рук при удержании книги/письма")]
        public bool enableHandBreathing = false;
        [Range(0f, 0.008f)] public float breathingAmplitude = 0.0025f;

        [Header("Ограничение выворота кисти")]
        [Tooltip("Ограничивает локальный поворот кисти относительно предплечья, чтобы запястье не выворачивалось на 180°.")]
        public bool clampWristRotation = false;
        [Range(20f, 120f)] public float maxWristLocalAngle = 68f;

        [Header("Мягкое удлинение руки (Soft-Stretch, без искажения кисти)")]
        [Tooltip("Максимальный коэффициент мягкого удлинения костей руки при дотягивании (1.18 = +18%). localScale не изменяется.")]
        [Range(1f, 1.25f)] public float maxStretchFactor = 1.18f;
        [Tooltip("Время плавного сглаживания удлинения костей (сек).")]
        [Range(0.02f, 0.35f)] public float stretchSmoothTime = 0.10f;

        // Legacy serialized fields kept for scene compatibility
        [HideInInspector] public bool allowShoulderReach = false;
        [HideInInspector] [Range(0f, 0.5f)] public float maxShoulderReachOffset = 0.35f;
        [HideInInspector] [Range(0f, 1f)] public float gripCurl = 0f;
        [HideInInspector] public Transform gripContactTarget;
        [HideInInspector] [Range(0.003f, 0.025f)] public float fingerContactRadius = 0.0085f;

        // Cached rest bone local positions for drift-free Soft-Stretch (NEVER touches localScale!)
        private Vector3 _restForearmLocalPos;
        private Vector3 _restHandLocalPos;
        private float _restUpperLen = 0.30f;
        private float _restForearmLen = 0.28f;
        private bool _restBonesCaptured;
        private bool _stretchModifiedBones;
        private float _currentStretch = 1f;
        private float _stretchVelocity;

        // Virtual smoothed target state (never teleports when target changes!)
        private Vector3 _smoothPos;
        private Vector3 _posVelocity;
        private Quaternion _smoothRot = Quaternion.identity;
        private float _smoothWeight;
        private float _weightVelocity;
        private bool _smoothInitialized;
        private float _breathClock;
        private float _breathBlend;

        public float SmoothedWeight => _smoothWeight;
        public float CurrentStretchFactor => _currentStretch;
        public Vector3 SmoothedTargetPosition => _smoothInitialized
            ? _smoothPos
            : (target != null ? target.position : (hand != null ? hand.position : transform.position));

        public float UpperArmLength => _restBonesCaptured
            ? _restUpperLen
            : ((upperArm != null && forearm != null) ? Vector3.Distance(upperArm.position, forearm.position) : 0.30f);

        public float ForearmLength => _restBonesCaptured
            ? _restForearmLen
            : ((forearm != null && hand != null) ? Vector3.Distance(forearm.position, hand.position) : 0.28f);

        public float MaxReach => (UpperArmLength + ForearmLength) * 0.92f;

        private void Awake()
        {
            CaptureRestBonePositions();
            ResetImmediate();
        }

        private void Start()
        {
            CaptureRestBonePositions();
            ResetImmediate();
        }

        private void CaptureRestBonePositions()
        {
            if (_restBonesCaptured || upperArm == null || forearm == null || hand == null)
            {
                return;
            }

            _restForearmLocalPos = forearm.localPosition;
            _restHandLocalPos = hand.localPosition;
            float uLen = Vector3.Distance(upperArm.position, forearm.position);
            float fLen = Vector3.Distance(forearm.position, hand.position);
            if (uLen > 0.001f) _restUpperLen = uLen;
            if (fLen > 0.001f) _restForearmLen = fLen;
            _restBonesCaptured = true;
        }

        private void RestoreRestBonePositions()
        {
            if (!_stretchModifiedBones || !_restBonesCaptured)
            {
                return;
            }

            if (forearm != null) forearm.localPosition = _restForearmLocalPos;
            if (hand != null) hand.localPosition = _restHandLocalPos;
            _stretchModifiedBones = false;
        }

        public void ResetImmediate()
        {
            RestoreRestBonePositions();
            weight = 0f;
            clampWristRotation = false;
            _smoothWeight = 0f;
            _weightVelocity = 0f;
            _posVelocity = Vector3.zero;
            _smoothInitialized = false;
            _currentStretch = 1f;
            _stretchVelocity = 0f;
        }

        private void LateUpdate()
        {
            if (upperArm == null || forearm == null || hand == null)
            {
                return;
            }

            CaptureRestBonePositions();
            // Always restore exact rest bone positions before measuring l1/l2 or early-outing to Idle (zero drift!)
            RestoreRestBonePositions();

            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f)
            {
                dt = Time.deltaTime;
            }

            float desiredWeight = (target != null) ? Mathf.Clamp01(weight) : 0f;

            // Smooth weight with SmoothDamp (silky ease-in & ease-out with zero start/stop jerk)
            if (dt > 0f)
            {
                _smoothWeight = Mathf.SmoothDamp(
                    _smoothWeight,
                    desiredWeight,
                    ref _weightVelocity,
                    Mathf.Max(0.01f, weightSmoothTime),
                    Mathf.Infinity,
                    dt);
            }

            // Clean idle early-out once fully damped back to rest
            if (desiredWeight <= 0.001f && _smoothWeight <= 0.003f)
            {
                _smoothWeight = 0f;
                _weightVelocity = 0f;
                _posVelocity = Vector3.zero;
                _smoothInitialized = false;
                _currentStretch = 1f;
                _stretchVelocity = 0f;
                return;
            }

            // Initialize virtual target seamlessly from current animated hand pose on wake-up (zero frame-1 snap!)
            if (!_smoothInitialized)
            {
                _smoothPos = hand.position;
                // Convert Hand bone orientation (up = fingers, forward = dorsal) to Target convention (forward = fingers, up = dorsal)
                _smoothRot = Quaternion.LookRotation(hand.up, hand.forward);
                _posVelocity = Vector3.zero;
                _smoothInitialized = true;
            }

            // Smoothly guide virtual target toward active target (never teleports on target swap!)
            // When returning to Idle (target == null or desiredWeight == 0), hold/coast smoothly at last pose while _smoothWeight fades out.
            if (target != null && desiredWeight > 0.001f)
            {
                if (dt > 0f)
                {
                    _smoothPos = Vector3.SmoothDamp(
                        _smoothPos,
                        target.position,
                        ref _posVelocity,
                        Mathf.Max(0.01f, positionSmoothTime),
                        Mathf.Infinity,
                        dt);

                    float rotT = 1f - Mathf.Exp(-rotationSmoothSpeed * dt);
                    _smoothRot = Quaternion.Slerp(_smoothRot, target.rotation, rotT);
                }
            }
            else if (dt > 0f)
            {
                // Damp residual velocity smoothly while fading weight out so hand never jerks in mid-air
                _posVelocity = Vector3.Lerp(_posVelocity, Vector3.zero, 1f - Mathf.Exp(-12f * dt));
                _smoothPos += _posVelocity * (dt * 0.25f);
            }

            float w = Mathf.Clamp01(_smoothWeight);
            if (w <= 0.001f)
            {
                return;
            }

            // Cache Animator pose so we can cleanly Slerp between idle and IK
            Quaternion animUpperRot = upperArm.rotation;
            Quaternion animForearmRot = forearm.rotation;
            Quaternion animHandRot = hand.rotation;
            Quaternion animHandLocalRot = Quaternion.Inverse(animForearmRot) * animHandRot;

            float l1 = Vector3.Distance(upperArm.position, forearm.position);
            float l2 = Vector3.Distance(forearm.position, hand.position);
            if (l1 < 0.001f || l2 < 0.001f)
            {
                return;
            }

            // NEVER modify upperArm.position! Only solve rotations using virtual _smoothPos (+ subtle breathing) & _smoothRot.
            Vector3 solvePos = _smoothPos;
            float targetBreath = enableHandBreathing ? 1f : 0f;
            if (dt > 0f)
            {
                _breathBlend = Mathf.MoveTowards(_breathBlend, targetBreath, dt * 1.5f);
                if (_breathBlend > 0.001f)
                {
                    _breathClock += dt;
                    float phase = isLeftHand ? 0.0f : 1.35f;
                    float bx = Mathf.Cos(_breathClock * 0.82f + phase) * (breathingAmplitude * 0.65f) * _breathBlend;
                    float by = Mathf.Sin(_breathClock * 1.24f + phase) * breathingAmplitude * _breathBlend;
                    solvePos += new Vector3(bx, by, 0f);
                }
            }

            Vector3 a = upperArm.position;

            Vector3 toTarget = solvePos - a;
            float rawDist = toTarget.magnitude;
            float baseMaxReach = (l1 + l2) * 0.992f;

            // Soft-Stretch: when target exceeds natural reach, softly extend working segment lengths up to maxStretchFactor (+18%)
            // without ever touching localScale (zero palm/finger distortion) and cleanly restoring rest lengths in Idle.
            float maxStretch = Mathf.Clamp(maxStretchFactor, 1f, 1.25f);
            float desiredStretch = 1f;
            if (maxStretch > 1.0005f && rawDist > baseMaxReach && baseMaxReach > 1e-4f)
            {
                float neededRatio = Mathf.Clamp(rawDist / baseMaxReach, 1f, maxStretch);
                desiredStretch = Mathf.Lerp(1f, neededRatio, w);
            }

            if (dt > 0f)
            {
                _currentStretch = Mathf.SmoothDamp(
                    _currentStretch,
                    desiredStretch,
                    ref _stretchVelocity,
                    Mathf.Max(0.02f, stretchSmoothTime),
                    Mathf.Infinity,
                    dt);
            }
            else
            {
                _currentStretch = desiredStretch;
            }

            if (desiredStretch <= 1.0002f && Mathf.Abs(_currentStretch - 1f) < 0.0003f)
            {
                _currentStretch = 1f;
                _stretchVelocity = 0f;
            }

            float stretch = Mathf.Clamp(_currentStretch, 1f, maxStretch);
            float sL1 = l1 * stretch;
            float sL2 = l2 * stretch;
            float maxReach = (sL1 + sL2) * 0.995f;
            float minReach = Mathf.Abs(sL1 - sL2) + 0.015f;
            float dist = Mathf.Clamp(rawDist, minReach, maxReach);
            Vector3 dir = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : Vector3.forward;

            Vector3 polePos = pole != null ? pole.position : (a + Vector3.down * 0.4f);
            Vector3 poleDir = Vector3.ProjectOnPlane(polePos - a, dir);
            if (poleDir.sqrMagnitude < 1e-6f)
            {
                poleDir = Vector3.ProjectOnPlane(Vector3.down, dir);
            }
            if (poleDir.sqrMagnitude < 1e-6f)
            {
                poleDir = Vector3.ProjectOnPlane(Vector3.right, dir);
            }
            poleDir.Normalize();

            // Law of cosines for elbow position (using soft-stretched segment lengths sL1, sL2)
            float cosAngleA = Mathf.Clamp((sL1 * sL1 + dist * dist - sL2 * sL2) / (2f * sL1 * dist), -1f, 1f);
            float sinAngleA = Mathf.Sqrt(Mathf.Max(0f, 1f - cosAngleA * cosAngleA));
            Vector3 desiredElbow = a + dir * (sL1 * cosAngleA) + poleDir * (sL1 * sinAngleA);

            Vector3 desiredUpperDir = (desiredElbow - a).normalized;
            Vector3 desiredLowerDir = (a + dir * dist - desiredElbow).normalized;

            // Solve upper arm rotation
            Vector3 curUpperDir = (forearm.position - a).normalized;
            upperArm.rotation = Quaternion.FromToRotation(curUpperDir, desiredUpperDir) * upperArm.rotation;

            Vector3 planeNormal = Vector3.Cross(desiredUpperDir, desiredLowerDir);
            if (planeNormal.sqrMagnitude > 1e-6f)
            {
                planeNormal.Normalize();
                Vector3 curSide = Vector3.ProjectOnPlane(upperArm.right, desiredUpperDir);
                Vector3 targetSide = (isLeftHand ? -1f : 1f) * planeNormal;
                if (curSide.sqrMagnitude > 1e-6f)
                {
                    float roll = Vector3.SignedAngle(curSide.normalized, targetSide, desiredUpperDir);
                    upperArm.rotation = Quaternion.AngleAxis(roll, desiredUpperDir) * upperArm.rotation;
                }
            }

            // Solve forearm rotation
            Vector3 curLowerDir = (hand.position - forearm.position).normalized;
            forearm.rotation = Quaternion.FromToRotation(curLowerDir, desiredLowerDir) * forearm.rotation;

            Quaternion solvedUpperRot = upperArm.rotation;
            Quaternion solvedForearmRot = forearm.rotation;
            Quaternion solvedHandRot = hand.rotation;

            if (matchTargetRotation)
            {
                Vector3 fingerDir = _smoothRot * Vector3.forward;
                Vector3 dorsalUp = Vector3.ProjectOnPlane(_smoothRot * Vector3.up, fingerDir);
                if (dorsalUp.sqrMagnitude < 1e-6f)
                {
                    dorsalUp = Vector3.ProjectOnPlane(Vector3.up, fingerDir);
                }
                dorsalUp.Normalize();

                solvedHandRot = Quaternion.LookRotation(dorsalUp, fingerDir) * Quaternion.Euler(handRotationOffsetEuler);

                if (forearmTwistShare > 0.001f)
                {
                    Vector3 curForearmDorsal = Vector3.ProjectOnPlane(forearm.forward, desiredLowerDir);
                    Vector3 desiredDorsal = Vector3.ProjectOnPlane(dorsalUp, desiredLowerDir);
                    if (curForearmDorsal.sqrMagnitude > 1e-6f && desiredDorsal.sqrMagnitude > 1e-6f)
                    {
                        float twist = Vector3.SignedAngle(curForearmDorsal.normalized, desiredDorsal.normalized, desiredLowerDir);
                        solvedForearmRot = Quaternion.AngleAxis(twist * forearmTwistShare, desiredLowerDir) * solvedForearmRot;
                    }
                }

                if (clampWristRotation)
                {
                    Quaternion localHand = Quaternion.Inverse(solvedForearmRot) * solvedHandRot;
                    float angleFromIdle = Quaternion.Angle(animHandLocalRot, localHand);
                    if (angleFromIdle > maxWristLocalAngle)
                    {
                        Quaternion clampedLocal = Quaternion.RotateTowards(animHandLocalRot, localHand, maxWristLocalAngle);
                        solvedHandRot = solvedForearmRot * clampedLocal;
                    }
                }
            }

            // Blend Animator idle pose with solved IK pose using SmoothDamp weight
            upperArm.rotation = Quaternion.Slerp(animUpperRot, solvedUpperRot, w);
            forearm.rotation = Quaternion.Slerp(animForearmRot, solvedForearmRot, w);
            if (matchTargetRotation)
            {
                hand.rotation = Quaternion.Slerp(animHandRot, solvedHandRot, w);
            }

            // Apply Soft-Stretch strictly via bone positions along bone axes (NEVER modifies localScale!)
            if (stretch > 1.0005f)
            {
                Vector3 upperVec = forearm.position - a;
                Vector3 lowerVec = hand.position - forearm.position;
                forearm.position = a + upperVec * stretch;
                hand.position = forearm.position + lowerVec * stretch;
                _stretchModifiedBones = true;
            }
        }
    }
}
