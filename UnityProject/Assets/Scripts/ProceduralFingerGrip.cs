using System.Collections.Generic;
using UnityEngine;

namespace LastSignal
{
    public enum HandPose
    {
        Idle = 0,
        PlugPinch = 1,
        RadioKnob = 2,
        LetterPinch = 3,
        PencilWrite = 4
    }

    /// <summary>
    /// Clean preset-based finger pose controller running in LateUpdate strictly AFTER 2-bone IK.
    /// - Captures restLocalRot ONCE in Awake/Start and NEVER accumulates rotation or alters localPosition/localScale.
    /// - Auto-verifies inward curl axis sign per finger once from straight rest pose.
    /// - Smoothly Lerps each phalanx to the target HandPose angles (Idle, PlugPinch, RadioKnob, LetterPinch, PencilWrite).
    /// Do NOT use for the callsign logbook (keep book hands on Idle pose).
    /// </summary>
    [DefaultExecutionOrder(30)]
    public sealed class ProceduralFingerGrip : MonoBehaviour
    {
        [System.Serializable]
        public sealed class FingerChain
        {
            public string fingerName;
            public bool isThumb;
            public Transform[] phalanges;
            [HideInInspector] public Quaternion[] restLocalRots;
            [HideInInspector] public float[] phalanxLengths;
            [HideInInspector] public float[] currentAngles;
            [HideInInspector] public Vector3[] verifiedAxes;
            [HideInInspector] public bool[] axisVerified;
        }

        [Header("Кисть и пальцы")]
        public Transform handBone;
        public bool isLeftHand = false;
        public List<FingerChain> fingers = new List<FingerChain>();

        [Header("Базовые оси сгибания (знак авто-проверяется внутрь ладони)")]
        public Vector3 fingerCurlAxis = Vector3.left;
        public Vector3 thumbCurlAxis = Vector3.left;

        [Header("Параметры плавности смены позы (живой тактильный темп)")]
        [Range(2f, 25f)] public float poseBlendSpeed = 7f;

        private HandPose _currentPose = HandPose.Idle;
        private bool _restCaptured;

        public HandPose CurrentPose => _currentPose;
        public bool IsGripping => _currentPose != HandPose.Idle;

        private void Awake()
        {
            EnsureFingersInitialized();
            CaptureRestPosesOnce();
            ResetAnglesImmediate();
        }

        private void Start()
        {
            EnsureFingersInitialized();
            CaptureRestPosesOnce();
            ResetAnglesImmediate();
        }

        public void SetPose(HandPose pose)
        {
            if (_currentPose == pose) return;
            EnsureFingersInitialized();
            CaptureRestPosesOnce();
            _currentPose = pose;
        }

        // Backwards-compatible convenience wrappers
        public void StartGrip(GameObject targetObject) => SetPose(HandPose.PlugPinch);
        public void StartPencilPresetGrip() => SetPose(HandPose.PencilWrite);
        public void ReleaseGrip() => SetPose(HandPose.Idle);

        public void ResetAnglesImmediate()
        {
            _currentPose = HandPose.Idle;
            for (int f = 0; f < fingers.Count; f++)
            {
                FingerChain chain = fingers[f];
                if (chain == null || chain.phalanges == null || chain.currentAngles == null) continue;
                for (int p = 0; p < chain.currentAngles.Length; p++)
                {
                    chain.currentAngles[p] = 0f;
                    if (_restCaptured && chain.restLocalRots != null && p < chain.restLocalRots.Length && chain.phalanges[p] != null)
                    {
                        chain.phalanges[p].localRotation = chain.restLocalRots[p];
                    }
                }
            }
        }

        public void AutoPopulateFromHand()
        {
            if (handBone == null)
            {
                SimpleHandIK ik = GetComponent<SimpleHandIK>();
                if (ik != null)
                {
                    handBone = ik.hand;
                    isLeftHand = ik.isLeftHand;
                }
            }

            if (handBone == null) return;

            fingers.Clear();
            string suffix = isLeftHand ? ".L" : ".R";

            Transform t1 = handBone.Find("Daumen1" + suffix);
            Transform t2 = t1 != null ? t1.Find("Daumen2" + suffix) : null;
            Transform t3 = t2 != null ? t2.Find("Daumen3" + suffix) : null;
            AddFingerChain("Thumb", true, t1, t2, t3);

            string[] prefixes = { "Z_Finger", "M_Finger", "R_Finger", "K_Finger" };
            string[] labels = { "Index", "Middle", "Ring", "Pinky" };
            for (int i = 0; i < prefixes.Length; i++)
            {
                Transform f1 = handBone.Find(prefixes[i] + "1" + suffix);
                Transform f2 = f1 != null ? f1.Find(prefixes[i] + "2" + suffix) : null;
                Transform f3 = f2 != null ? f2.Find(prefixes[i] + "3" + suffix) : null;
                Transform f4 = f3 != null ? f3.Find(prefixes[i] + "4" + suffix) : null;
                AddFingerChain(labels[i], false, f2, f3, f4);
            }

            EnsureRuntimeArrays();
        }

        private void AddFingerChain(string label, bool isThumb, Transform b0, Transform b1, Transform b2)
        {
            var list = new List<Transform>(3);
            if (b0 != null) list.Add(b0);
            if (b1 != null) list.Add(b1);
            if (b2 != null) list.Add(b2);
            if (list.Count == 0) return;

            fingers.Add(new FingerChain
            {
                fingerName = label,
                isThumb = isThumb,
                phalanges = list.ToArray()
            });
        }

        private void EnsureFingersInitialized()
        {
            if (fingers == null || fingers.Count == 0)
            {
                AutoPopulateFromHand();
            }
            else
            {
                EnsureRuntimeArrays();
            }
        }

        private void EnsureRuntimeArrays()
        {
            for (int f = 0; f < fingers.Count; f++)
            {
                FingerChain chain = fingers[f];
                if (chain == null || chain.phalanges == null) continue;
                int count = chain.phalanges.Length;

                if (chain.currentAngles == null || chain.currentAngles.Length != count)
                    chain.currentAngles = new float[count];
                if (chain.restLocalRots == null || chain.restLocalRots.Length != count)
                    chain.restLocalRots = new Quaternion[count];
                if (chain.verifiedAxes == null || chain.verifiedAxes.Length != count)
                    chain.verifiedAxes = new Vector3[count];
                if (chain.axisVerified == null || chain.axisVerified.Length != count)
                    chain.axisVerified = new bool[count];

                if (chain.phalanxLengths == null || chain.phalanxLengths.Length != count)
                {
                    chain.phalanxLengths = new float[count];
                    for (int p = 0; p < count; p++)
                    {
                        float len = 0.022f;
                        if (chain.phalanges[p] != null && p + 1 < count && chain.phalanges[p + 1] != null)
                        {
                            len = Vector3.Distance(chain.phalanges[p].position, chain.phalanges[p + 1].position);
                        }
                        chain.phalanxLengths[p] = len < 0.005f ? 0.022f : len;
                    }
                }
            }
        }

        private void CaptureRestPosesOnce()
        {
            if (_restCaptured) return;
            EnsureRuntimeArrays();

            for (int f = 0; f < fingers.Count; f++)
            {
                FingerChain chain = fingers[f];
                if (chain == null || chain.phalanges == null) continue;
                for (int p = 0; p < chain.phalanges.Length; p++)
                {
                    Transform bone = chain.phalanges[p];
                    if (bone != null)
                    {
                        chain.restLocalRots[p] = bone.localRotation;
                    }
                    chain.axisVerified[p] = false;
                }
            }

            _restCaptured = true;
        }

        private void LateUpdate()
        {
            if (fingers == null || fingers.Count == 0) return;
            if (!_restCaptured) CaptureRestPosesOnce();

            // Leave Animator completely untouched once Idle pose has cleanly reached 0 deg
            if (_currentPose == HandPose.Idle && AllAnglesZero()) return;

            float dt = Time.unscaledDeltaTime;
            float lerpT = 1f - Mathf.Exp(-poseBlendSpeed * dt);
            Vector3 defaultFingerAxis = fingerCurlAxis.sqrMagnitude > 1e-5f ? fingerCurlAxis.normalized : Vector3.left;
            Vector3 palmCenter = ComputePalmCenterWorld();
            Vector3 indexTipWorld = ComputeIndexTipWorld();

            for (int f = 0; f < fingers.Count; f++)
            {
                FingerChain chain = fingers[f];
                if (chain == null || chain.phalanges == null) continue;

                Transform tipBone = chain.phalanges[chain.phalanges.Length - 1];
                float tipLen = (chain.phalanxLengths != null && chain.phalanxLengths.Length > 0)
                    ? chain.phalanxLengths[chain.phalanxLengths.Length - 1]
                    : 0.022f;

                for (int p = 0; p < chain.phalanges.Length; p++)
                {
                    Transform bone = chain.phalanges[p];
                    if (bone == null) continue;

                    Quaternion restRot = chain.restLocalRots[p];
                    Transform childBone = (p + 1 < chain.phalanges.Length) ? chain.phalanges[p + 1] : null;
                    float len = (chain.phalanxLengths != null && p < chain.phalanxLengths.Length) ? chain.phalanxLengths[p] : 0.022f;

                    if (!chain.axisVerified[p])
                    {
                        if (chain.isThumb)
                        {
                            // For thumb: test all 6 local axes (±X, ±Y, ±Z) at +20 deg and pick the one
                            // that moves ThumbTip closest to IndexTip (clean opposition pinch, never outward/down!)
                            chain.verifiedAxes[p] = VerifyThumbAxisTowardIndex(bone, restRot, tipBone, tipLen, indexTipWorld);
                        }
                        else
                        {
                            // For 4 fingers: verify inward curl axis once on proximal phalanx (p == 0)
                            chain.verifiedAxes[p] = (p == 0)
                                ? VerifyInwardAxis(bone, childBone, restRot, defaultFingerAxis, len, palmCenter)
                                : chain.verifiedAxes[0];
                        }
                        chain.axisVerified[p] = true;
                    }

                    float maxAllowed = chain.isThumb ? 25f : 70f;
                    float targetAngle = Mathf.Clamp(GetPoseAngle(_currentPose, chain, p), 0f, maxAllowed);
                    float cur = Mathf.Lerp(chain.currentAngles[p], targetAngle, lerpT);
                    if (_currentPose == HandPose.Idle && cur < 0.08f)
                    {
                        cur = 0f;
                    }
                    cur = Mathf.Clamp(cur, 0f, maxAllowed);
                    chain.currentAngles[p] = cur;

                    // ONLY modify localRotation strictly from captured restRot (NEVER localPosition or localScale!)
                    bone.localRotation = cur > 0.01f
                        ? restRot * Quaternion.AngleAxis(cur, chain.verifiedAxes[p])
                        : restRot;
                }
            }
        }

        private static float GetPoseAngle(HandPose pose, FingerChain chain, int phalanx)
        {
            if (pose == HandPose.Idle) return 0f;

            bool isThumb = chain.isThumb;
            string name = chain.fingerName;

            switch (pose)
            {
                case HandPose.PlugPinch:
                    // Thumb: keep Thumb1 at rest (0..5 deg), bend only Thumb2 & Thumb3 gently (16-20 deg) toward Index
                    if (isThumb) return phalanx == 0 ? 4f : (phalanx == 1 ? 18f : 20f);
                    if (name == "Index") return phalanx == 0 ? 38f : (phalanx == 1 ? 42f : 34f);
                    if (name == "Middle") return phalanx == 0 ? 40f : (phalanx == 1 ? 42f : 36f);
                    return phalanx == 0 ? 54f : (phalanx == 1 ? 56f : 48f);

                case HandPose.RadioKnob:
                    // Soft fingertip grip on radio dial
                    if (isThumb) return phalanx == 0 ? 3f : (phalanx == 1 ? 14f : 16f);
                    if (name == "Index" || name == "Middle") return phalanx == 0 ? 28f : (phalanx == 1 ? 32f : 26f);
                    return phalanx == 0 ? 32f : (phalanx == 1 ? 34f : 26f);

                case HandPose.LetterPinch:
                    // Clean paper-edge pinch
                    if (isThumb) return phalanx == 0 ? 4f : (phalanx == 1 ? 16f : 18f);
                    if (name == "Index") return phalanx == 0 ? 32f : (phalanx == 1 ? 34f : 26f);
                    if (name == "Middle") return phalanx == 0 ? 45f : (phalanx == 1 ? 48f : 38f);
                    return phalanx == 0 ? 52f : (phalanx == 1 ? 54f : 44f);

                case HandPose.PencilWrite:
                    // Natural tripod writing grip: gentle thumb opposition (Thumb1 <= 5 deg, Thumb2/3 18-22 deg)
                    if (isThumb) return phalanx == 0 ? 5f : (phalanx == 1 ? 18f : 22f);
                    if (name == "Index") return phalanx == 0 ? 38f : (phalanx == 1 ? 30f : 24f);
                    if (name == "Middle") return phalanx == 0 ? 40f : (phalanx == 1 ? 34f : 26f);
                    if (name == "Ring") return phalanx == 0 ? 58f : (phalanx == 1 ? 60f : 50f);
                    return phalanx == 0 ? 62f : (phalanx == 1 ? 64f : 52f);

                default:
                    return 0f;
            }
        }

        private Vector3 ComputeIndexTipWorld()
        {
            for (int f = 0; f < fingers.Count; f++)
            {
                FingerChain ch = fingers[f];
                if (ch != null && ch.fingerName == "Index" && ch.phalanges != null && ch.phalanges.Length > 0)
                {
                    Transform tip = ch.phalanges[ch.phalanges.Length - 1];
                    if (tip != null)
                    {
                        return tip.position + tip.up * 0.022f;
                    }
                }
            }
            return ComputePalmCenterWorld();
        }

        private static readonly Vector3[] AllSixLocalAxes =
        {
            Vector3.right, Vector3.left,
            Vector3.up, Vector3.down,
            Vector3.forward, Vector3.back
        };

        private static Vector3 VerifyThumbAxisTowardIndex(
            Transform bone,
            Quaternion restRot,
            Transform thumbTipBone,
            float thumbTipLen,
            Vector3 indexTipWorld)
        {
            Vector3 bestAxis = Vector3.right;
            float bestDist = float.MaxValue;

            for (int i = 0; i < AllSixLocalAxes.Length; i++)
            {
                Vector3 candidate = AllSixLocalAxes[i];
                bone.localRotation = restRot * Quaternion.AngleAxis(20f, candidate);
                Vector3 tipPos = thumbTipBone != null
                    ? (thumbTipBone.position + thumbTipBone.up * thumbTipLen)
                    : (bone.position + bone.up * thumbTipLen);
                float d = Vector3.Distance(tipPos, indexTipWorld);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestAxis = candidate;
                }
            }

            bone.localRotation = restRot;
            return bestAxis;
        }

        private Vector3 ComputePalmCenterWorld()
        {
            if (handBone != null)
            {
                return handBone.position + handBone.up * 0.055f - handBone.forward * 0.055f;
            }
            return transform.position;
        }

        private static Vector3 GetTipWorldPosition(Transform bone, Transform childBone, float phalanxLen)
        {
            return childBone != null ? childBone.position : (bone.position + bone.up * phalanxLen);
        }

        private static Vector3 VerifyInwardAxis(
            Transform bone,
            Transform childBone,
            Quaternion restRot,
            Vector3 candidateAxis,
            float phalanxLen,
            Vector3 inwardRefPoint)
        {
            bone.localRotation = restRot * Quaternion.AngleAxis(15f, candidateAxis);
            float distPlus = Vector3.Distance(GetTipWorldPosition(bone, childBone, phalanxLen), inwardRefPoint);

            bone.localRotation = restRot * Quaternion.AngleAxis(-15f, candidateAxis);
            float distMinus = Vector3.Distance(GetTipWorldPosition(bone, childBone, phalanxLen), inwardRefPoint);

            bone.localRotation = restRot;
            return distPlus <= distMinus ? candidateAxis : -candidateAxis;
        }

        private bool AllAnglesZero()
        {
            for (int f = 0; f < fingers.Count; f++)
            {
                FingerChain chain = fingers[f];
                if (chain == null || chain.currentAngles == null) continue;
                for (int p = 0; p < chain.currentAngles.Length; p++)
                {
                    if (chain.currentAngles[p] > 0.01f) return false;
                }
            }
            return true;
        }
    }
}
