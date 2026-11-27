using UnityEngine;

namespace LastSignal
{
    /// <summary>
    /// Pure additive camera offset provider (Variant B architecture):
    /// NEVER writes to transform.position or transform.rotation directly!
    /// SmoothMouseLookCamera (sole writer, order 40) queries ComputeAdditiveOffset() using its immutable pure base pose
    /// and writes transform ONCE per frame — mathematically eliminating any feedback loop or camera dive.
    /// Hard-clamps total camera offset to <= 0.09m horizontal/forward and <= 0.035m upward lift,
    /// returning exact Vector3.zero / Quaternion.identity when hands are Idle.
    /// </summary>
    public sealed class CameraHandFollow : MonoBehaviour
    {
        [Header("Ссылки")]
        public BookHandsIKController handsController;
        public SmoothMouseLookCamera mouseLookCamera;

        [Header("Безопасные лимиты смещения камеры (жёсткий потолок)")]
        [Tooltip("Максимальный горизонтальный/вперёд сдвиг камеры при наклоне (<= 0.09 м).")]
        [Range(0f, 0.09f)] public float maxTotalHorizontalShift = 0.09f;
        [Tooltip("Максимальный подъём камеры вверх при привставании (<= 0.035 м).")]
        [Range(0f, 0.035f)] public float maxTotalVerticalLift = 0.035f;
        [Tooltip("Доля смещения корпуса, синхронно передаваемая в позицию камеры (плечи никогда не обгоняют камеру).")]
        [Range(0f, 1f)] public float bodyLeanToCameraScale = 0.95f;

        [Header("Кинематографичный поворот головы (стиль Metro)")]
        [Range(0f, 0.06f)] public float maxLateralShiftMeters = 0.035f;
        [Range(0f, 0.05f)] public float maxForwardShiftMeters = 0.030f;
        [Range(0f, 8f)] public float maxYawDegrees = 3.2f;
        [Range(0f, 6f)] public float maxPitchDegrees = 2.5f;
        [Range(0f, 4f)] public float maxRollDegrees = 2.0f;
        [Range(0f, 6f)] public float letterReadPitchDegrees = 2.5f;

        [Header("Ослабление в режиме чтения книги и киношный SmoothDamp")]
        [Range(0f, 1f)] public float bookReadingMultiplier = 0.30f;
        [Range(0.08f, 0.70f)] public float smoothTime = 0.36f;

        private Vector3 _curWorldPosOffset;
        private Vector3 _curHeadWorldPosOffset;
        private Vector3 _headPosVel;
        private float _curYaw;
        private float _yawVel;
        private float _curPitch;
        private float _pitchVel;
        private float _curRoll;
        private float _rollVel;

        public Vector3 CurrentWorldPosOffset => _curWorldPosOffset;
        public Quaternion CurrentLocalRotOffset => Quaternion.Euler(_curPitch, _curYaw, _curRoll);

        private void Awake()
        {
            ResolveReferences();
            ResetOffsetsImmediate();
        }

        private void Start()
        {
            ResolveReferences();
            ResetOffsetsImmediate();
        }

        private void OnDisable()
        {
            ResetOffsetsImmediate();
        }

        public void ResetOffsetsImmediate()
        {
            _curWorldPosOffset = Vector3.zero;
            _curHeadWorldPosOffset = Vector3.zero;
            _headPosVel = Vector3.zero;
            _curYaw = 0f;
            _yawVel = 0f;
            _curPitch = 0f;
            _pitchVel = 0f;
            _curRoll = 0f;
            _rollVel = 0f;
        }

        /// <summary>
        /// Called exclusively by SmoothMouseLookCamera at the end of LateUpdate.
        /// Pure function of basePos/baseRot and hand/body state — never modifies Transform directly.
        /// </summary>
        public void ComputeAdditiveOffset(
            Vector3 pureBasePos,
            Quaternion pureBaseRot,
            float dt,
            out Vector3 worldPosOffset,
            out Quaternion localRotOffset)
        {
            if (handsController == null) ResolveReferences();
            if (handsController == null || dt <= 0f)
            {
                worldPosOffset = _curWorldPosOffset;
                localRotOffset = Quaternion.Euler(_curPitch, _curYaw, _curRoll);
                return;
            }

            Vector3 targetHeadPosOffset = Vector3.zero;
            float targetYaw = 0f;
            float targetPitch = 0f;
            float targetRoll = 0f;

            // 1. Synchronous 1:1 lockstep body lean (already SmoothDamp'd in BookHandsIKController — zero phase lag so shoulders NEVER overtake camera!)
            Vector3 bodyOffset = handsController.CurrentBodyWorldOffset;
            Vector3 bodyCamOffset = bodyOffset.sqrMagnitude > 1e-8f
                ? bodyOffset * Mathf.Clamp01(bodyLeanToCameraScale)
                : Vector3.zero;

            // 2. Subtle eye/head follow toward active reaching hand (continuous weight, zero binary jumps!)
            if (handsController.TryGetActiveReachTarget(out Vector3 handTargetWorld, out float activeWeight, out float handSideSign, out bool isReadingBook))
            {
                float mult = isReadingBook ? bookReadingMultiplier : 1f;
                float influence = Mathf.Clamp01(activeWeight) * mult;

                if (influence > 0.0001f)
                {
                    Vector3 toTargetLocal = Quaternion.Inverse(pureBaseRot) * (handTargetWorld - pureBasePos);
                    float forwardDist = Mathf.Max(0.20f, toTargetLocal.z);

                    float normX = Mathf.Clamp(toTargetLocal.x / 0.45f, -1f, 1f);
                    float normY = Mathf.Clamp(toTargetLocal.y / 0.35f, -1f, 1f);
                    float forwardPull = Mathf.Clamp01((forwardDist - 0.48f) / 0.40f);

                    Vector3 headShiftLocal = new Vector3(
                        normX * maxLateralShiftMeters * influence,
                        normY * (maxLateralShiftMeters * 0.20f) * influence,
                        forwardPull * maxForwardShiftMeters * influence);
                    targetHeadPosOffset = pureBaseRot * headShiftLocal;

                    targetYaw = normX * maxYawDegrees * influence;
                    targetPitch = -normY * maxPitchDegrees * influence;
                    float rollDir = Mathf.Abs(normX) > 0.08f ? normX : handSideSign * 0.5f;
                    targetRoll = -Mathf.Clamp(rollDir, -1f, 1f) * maxRollDegrees * influence;
                }
            }

            if (handsController.IsReadingLetter)
            {
                targetPitch += letterReadPitchDegrees;
            }

            // 3. SmoothDamp head follow & rotation channels cleanly
            float st = Mathf.Max(0.08f, smoothTime);
            _curHeadWorldPosOffset = Vector3.SmoothDamp(_curHeadWorldPosOffset, targetHeadPosOffset, ref _headPosVel, st, Mathf.Infinity, dt);
            _curYaw = Mathf.SmoothDamp(_curYaw, targetYaw, ref _yawVel, st, Mathf.Infinity, dt);
            _curPitch = Mathf.SmoothDamp(_curPitch, targetPitch, ref _pitchVel, st, Mathf.Infinity, dt);
            _curRoll = Mathf.SmoothDamp(_curRoll, targetRoll, ref _rollVel, st, Mathf.Infinity, dt);

            // 4. Combine lockstep body lean + smoothed head offset and hard-clamp to safe camera bounds (<= 0.09m XZ, <= 0.035m Y)
            Vector3 combinedOffset = bodyCamOffset + _curHeadWorldPosOffset;
            float maxHoriz = Mathf.Clamp(maxTotalHorizontalShift, 0f, 0.09f);
            Vector2 curHoriz = new Vector2(combinedOffset.x, combinedOffset.z);
            if (curHoriz.magnitude > maxHoriz)
            {
                curHoriz = curHoriz.normalized * maxHoriz;
                combinedOffset.x = curHoriz.x;
                combinedOffset.z = curHoriz.y;
            }
            float maxLift = Mathf.Clamp(maxTotalVerticalLift, 0f, 0.035f);
            combinedOffset.y = Mathf.Clamp(combinedOffset.y, -maxLift * 0.5f, maxLift);
            _curWorldPosOffset = combinedOffset;

            // Cleanly settle to exact zero when idle
            if (bodyCamOffset == Vector3.zero &&
                targetHeadPosOffset == Vector3.zero &&
                targetYaw == 0f && targetPitch == 0f && targetRoll == 0f &&
                _curHeadWorldPosOffset.sqrMagnitude < 1e-8f &&
                Mathf.Abs(_curYaw) < 0.005f &&
                Mathf.Abs(_curPitch) < 0.005f &&
                Mathf.Abs(_curRoll) < 0.005f)
            {
                ResetOffsetsImmediate();
                worldPosOffset = Vector3.zero;
                localRotOffset = Quaternion.identity;
                return;
            }

            worldPosOffset = _curWorldPosOffset;
            localRotOffset = Quaternion.Euler(_curPitch, _curYaw, _curRoll);
        }

        private void ResolveReferences()
        {
            if (handsController == null) handsController = FindFirstObjectByType<BookHandsIKController>();
            if (mouseLookCamera == null) mouseLookCamera = GetComponent<SmoothMouseLookCamera>();
        }
    }
}
