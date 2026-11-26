using UnityEngine;

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Нативный физичный 3D-провод коммутатора (PBD Verlet + 2-hop Bending Constraint + гладкая Catmull-Rom трубка).
    /// - 12 физических узлов обеспечивают мгновенную сходимость, стабильность и отсутствие заломов/петель.
    /// - 30 визуальных колец по Catmull-Rom дают идеально гладкий объёмный 3D-шнур (0 GC-аллокаций, 1 Draw Call).
    /// - Автоматически засыпает (0 ms CPU), когда оба конца в покое, и мгновенно просыпается при движении штекера.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SwitchboardCordPhysics : MonoBehaviour
    {
        [Header("Точки крепления")]
        [SerializeField] private Transform boardAnchor;
        [SerializeField] private Vector3 boardExitLocalDir = new Vector3(0f, 0.25f, 1f);

        [SerializeField] private Transform plugTransform;
        [SerializeField] private Vector3 plugTailLocalOffset = new Vector3(0f, 0f, -0.0135f);
        [SerializeField] private Vector3 plugExitLocalDir = Vector3.back;

        [Header("Геометрия и детализация")]
        [SerializeField, Range(8, 20)] private int simNodeCount = 12;
        [SerializeField, Range(16, 48)] private int visualRingCount = 30;
        [SerializeField, Range(5, 10)] private int radialSegments = 6;
        [SerializeField, Min(0.001f)] private float cordRadius = 0.0040f;
        [SerializeField, Min(0.1f)] private float uvTilingAlongLength = 10f;

        [Header("Длина шнура (противовес внутри коммутатора)")]
        [SerializeField, Min(0.005f)] private float extraSlackMeters = 0.032f;
        [SerializeField, Range(1.0f, 1.35f)] private float slackMultiplier = 1.08f;
        [SerializeField, Min(0.08f)] private float minCordLength = 0.145f;
        [SerializeField, Min(0.15f)] private float maxCordLength = 0.42f;

        [Header("Физика (PBD + жёсткость кабеля на изгиб)")]
        [SerializeField] private float gravity = -9.81f;
        [SerializeField, Range(0.85f, 0.99f)] private float airDamping = 0.94f;
        [SerializeField, Range(0.5f, 0.98f)] private float deskFriction = 0.80f;
        [SerializeField, Range(4, 16)] private int constraintIterations = 10;
        [Tooltip("Жёсткость 2-hop ограничения изгиба: запрещает резкие складки и самопересечения кабеля.")]
        [SerializeField, Range(0f, 1f)] private float bendResistance = 0.55f;
        [Tooltip("Минимальный косинус половинного угла сгиба (0.90 ≈ плавный минимальный радиус изгиба).")]
        [SerializeField, Range(0.6f, 0.98f)] private float bendMinSpanRatio = 0.90f;
        [SerializeField, Range(0f, 0.6f)] private float exitTangentWeight = 0.22f;

        [Header("Аналитические коллизии")]
        [SerializeField] private float deskSurfaceY = 0.708f;
        [SerializeField] private float panelFrontLimitZ = -0.225f;
        [SerializeField] private bool clampAgainstPanelZ = true;

        [Header("Оптимизация (Sleep-режим)")]
        [SerializeField] private bool enableSleepOptimization = true;
        [SerializeField, Min(0.00001f)] private float sleepVelocityThreshold = 0.00015f;
        [SerializeField, Min(0.1f)] private float sleepDelaySeconds = 0.45f;

        private const float FixedStep = 1f / 90f;
        private const int MaxSubstepsPerFrame = 3;

        private MeshFilter _meshFilter;
        private Mesh _tubeMesh;

        private Vector3[] _pos;
        private Vector3[] _prevPos;
        private Vector3[] _smoothCurve;

        private Vector3[] _vertices;
        private Vector3[] _normals;
        private Vector2[] _uvs;
        private int[] _triangles;

        private float _accumulator;
        private float _stillTimer;
        private bool _isSleeping;
        private Vector3 _lastStartWorld;
        private Vector3 _lastEndWorld;
        private bool _initialized;

        private void Awake()
        {
            InitializeIfNeeded();
        }

        private void Start()
        {
            InitializeIfNeeded();
            WarmUpSimulation(60);
            RebuildTubeMesh();
        }

        private void OnDestroy()
        {
            if (_tubeMesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_tubeMesh);
                }
                else
                {
                    DestroyImmediate(_tubeMesh);
                }
                _tubeMesh = null;
            }
        }

        private void LateUpdate()
        {
            if (!_initialized || boardAnchor == null || plugTransform == null)
            {
                return;
            }

            GetEndpoints(out Vector3 startPos, out Vector3 endPos, out Vector3 startDir, out Vector3 endDir);

            if (_isSleeping)
            {
                if ((startPos - _lastStartWorld).sqrMagnitude > 1e-8f ||
                    (endPos - _lastEndWorld).sqrMagnitude > 1e-8f)
                {
                    _isSleeping = false;
                    _stillTimer = 0f;
                }
                else
                {
                    return;
                }
            }

            _lastStartWorld = startPos;
            _lastEndWorld = endPos;

            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            _accumulator += dt;

            int steps = 0;
            float maxNodeMoveSqr = 0f;

            while (_accumulator >= FixedStep && steps < MaxSubstepsPerFrame)
            {
                float stepMove = StepSimulation(FixedStep, startPos, endPos, startDir, endDir);
                if (stepMove > maxNodeMoveSqr)
                {
                    maxNodeMoveSqr = stepMove;
                }

                _accumulator -= FixedStep;
                steps++;
            }

            _pos[0] = startPos;
            _pos[simNodeCount - 1] = endPos;

            RebuildTubeMesh();

            if (enableSleepOptimization && steps > 0)
            {
                float threshSqr = sleepVelocityThreshold * sleepVelocityThreshold;
                if (maxNodeMoveSqr < threshSqr)
                {
                    _stillTimer += dt;
                    if (_stillTimer >= sleepDelaySeconds)
                    {
                        _isSleeping = true;
                        _accumulator = 0f;
                    }
                }
                else
                {
                    _stillTimer = 0f;
                }
            }
        }

        public void WakeUp()
        {
            _isSleeping = false;
            _stillTimer = 0f;
        }

        [ContextMenu("Обновить статическую форму провода")]
        public void BakeStaticPreview()
        {
            _initialized = false;
            InitializeIfNeeded();
            WarmUpSimulation(80);
            RebuildTubeMesh();
        }

        private void InitializeIfNeeded()
        {
            if (_initialized)
            {
                return;
            }

            _meshFilter = GetComponent<MeshFilter>();
            simNodeCount = Mathf.Clamp(simNodeCount, 8, 20);
            visualRingCount = Mathf.Clamp(visualRingCount, 16, 48);
            radialSegments = Mathf.Clamp(radialSegments, 5, 10);

            _pos = new Vector3[simNodeCount];
            _prevPos = new Vector3[simNodeCount];
            _smoothCurve = new Vector3[visualRingCount];

            int vertCount = visualRingCount * radialSegments;
            int triCount = (visualRingCount - 1) * radialSegments * 6;
            _vertices = new Vector3[vertCount];
            _normals = new Vector3[vertCount];
            _uvs = new Vector2[vertCount];
            _triangles = new int[triCount];

            for (int i = 0; i < visualRingCount; i++)
            {
                float v = (i / (float)(visualRingCount - 1)) * uvTilingAlongLength;
                for (int r = 0; r < radialSegments; r++)
                {
                    float u = r / (float)radialSegments;
                    _uvs[i * radialSegments + r] = new Vector2(u, v);
                }
            }

            int triIdx = 0;
            for (int i = 0; i < visualRingCount - 1; i++)
            {
                int rowA = i * radialSegments;
                int rowB = (i + 1) * radialSegments;
                for (int r = 0; r < radialSegments; r++)
                {
                    int rNext = (r + 1) % radialSegments;
                    int a0 = rowA + r;
                    int a1 = rowA + rNext;
                    int b0 = rowB + r;
                    int b1 = rowB + rNext;

                    _triangles[triIdx++] = a0;
                    _triangles[triIdx++] = b0;
                    _triangles[triIdx++] = a1;

                    _triangles[triIdx++] = a1;
                    _triangles[triIdx++] = b0;
                    _triangles[triIdx++] = b1;
                }
            }

            _tubeMesh = new Mesh { name = $"{name}_TubeMesh" };
            _tubeMesh.MarkDynamic();
            _tubeMesh.vertices = _vertices;
            _tubeMesh.normals = _normals;
            _tubeMesh.uv = _uvs;
            _tubeMesh.triangles = _triangles;
            _meshFilter.sharedMesh = _tubeMesh;

            if (boardAnchor != null && plugTransform != null)
            {
                GetEndpoints(out Vector3 startPos, out Vector3 endPos, out Vector3 startDir, out Vector3 endDir);
                // Стартуем из чистой кубической кривой Безье без петель — сразу на нужном склоне.
                float span = Mathf.Max(0.05f, Vector3.Distance(startPos, endPos));
                Vector3 c1 = startPos + startDir * (span * 0.45f);
                Vector3 c2 = endPos + endDir * (span * 0.35f);
                for (int i = 0; i < simNodeCount; i++)
                {
                    float u = i / (float)(simNodeCount - 1);
                    Vector3 p = EvalCubicBezier(startPos, c1, c2, endPos, u);
                    _pos[i] = p;
                    _prevPos[i] = p;
                }
                _lastStartWorld = startPos;
                _lastEndWorld = endPos;
            }

            _initialized = true;
            _isSleeping = false;
            _stillTimer = 0f;
        }

        private void WarmUpSimulation(int iterations)
        {
            if (boardAnchor == null || plugTransform == null)
            {
                return;
            }

            GetEndpoints(out Vector3 startPos, out Vector3 endPos, out Vector3 startDir, out Vector3 endDir);
            for (int i = 0; i < iterations; i++)
            {
                StepSimulation(FixedStep, startPos, endPos, startDir, endDir);
            }

            for (int i = 0; i < simNodeCount; i++)
            {
                _prevPos[i] = _pos[i];
            }
        }

        private float StepSimulation(float dt, Vector3 startPos, Vector3 endPos, Vector3 startDir, Vector3 endDir)
        {
            float directDist = Vector3.Distance(startPos, endPos);
            float effectiveLen = Mathf.Clamp(directDist * slackMultiplier + extraSlackMeters, minCordLength, maxCordLength);
            float segmentLen = effectiveLen / (simNodeCount - 1);
            float minTwoHopDist = segmentLen * 2f * bendMinSpanRatio;

            float dtSq = dt * dt;
            Vector3 gravDisp = new Vector3(0f, gravity * dtSq, 0f);
            float floorY = deskSurfaceY + cordRadius;

            // 1. Интеграция Верле.
            for (int i = 1; i < simNodeCount - 1; i++)
            {
                Vector3 current = _pos[i];
                Vector3 vel = (current - _prevPos[i]) * airDamping;

                if (current.y <= floorY + 0.002f)
                {
                    vel.x *= deskFriction;
                    vel.z *= deskFriction;
                }

                _prevPos[i] = current;
                _pos[i] = current + vel + gravDisp;
            }

            _pos[0] = startPos;
            _pos[simNodeCount - 1] = endPos;

            // 2. Итеративное решение ограничений PBD.
            for (int iter = 0; iter < constraintIterations; iter++)
            {
                _pos[0] = startPos;
                _pos[simNodeCount - 1] = endPos;

                // Мягкое направляющее усилие выхода кабеля из втулки и хвостовика.
                if (exitTangentWeight > 0.001f && simNodeCount > 3)
                {
                    Vector3 ideal1 = startPos + startDir * segmentLen;
                    _pos[1] = Vector3.Lerp(_pos[1], ideal1, exitTangentWeight * 0.35f);

                    Vector3 idealEnd = endPos + endDir * segmentLen;
                    _pos[simNodeCount - 2] = Vector3.Lerp(_pos[simNodeCount - 2], idealEnd, exitTangentWeight * 0.35f);
                }

                // 1-hop Distance Constraints (точная длина каждого сегмента).
                for (int i = 0; i < simNodeCount - 1; i++)
                {
                    Vector3 delta = _pos[i + 1] - _pos[i];
                    float dist = delta.magnitude;
                    if (dist < 1e-6f)
                    {
                        continue;
                    }

                    float diff = (dist - segmentLen) / dist;

                    if (i == 0)
                    {
                        _pos[i + 1] -= delta * diff;
                    }
                    else if (i + 1 == simNodeCount - 1)
                    {
                        _pos[i] += delta * diff;
                    }
                    else
                    {
                        Vector3 corr = delta * (diff * 0.5f);
                        _pos[i] += corr;
                        _pos[i + 1] -= corr;
                    }
                }

                // 2-hop Bending Constraints (нативное сопротивление кабеля острым перегибам и петлям).
                if (bendResistance > 0.001f)
                {
                    float bendAlpha = bendResistance * 0.5f;
                    for (int i = 0; i < simNodeCount - 2; i++)
                    {
                        Vector3 delta = _pos[i + 2] - _pos[i];
                        float dist = delta.magnitude;
                        if (dist >= minTwoHopDist || dist < 1e-6f)
                        {
                            continue;
                        }

                        // Раздвигаем узлы i и i+2, не давая проводу складываться под острым углом.
                        float push = ((minTwoHopDist - dist) / dist) * bendAlpha;
                        Vector3 corr = delta * push;

                        bool lockA = i == 0;
                        bool lockB = (i + 2) == simNodeCount - 1;

                        if (lockA && !lockB)
                        {
                            _pos[i + 2] += corr;
                        }
                        else if (!lockA && lockB)
                        {
                            _pos[i] -= corr;
                        }
                        else if (!lockA)
                        {
                            _pos[i] -= corr * 0.5f;
                            _pos[i + 2] += corr * 0.5f;
                        }
                    }
                }

                // Быстрые аналитические коллизии (поверхность стола и лицевая панель).
                for (int i = 1; i < simNodeCount - 1; i++)
                {
                    Vector3 p = _pos[i];
                    if (p.y < floorY)
                    {
                        p.y = floorY;
                    }

                    if (clampAgainstPanelZ && p.z < panelFrontLimitZ)
                    {
                        p.z = panelFrontLimitZ;
                    }

                    _pos[i] = p;
                }
            }

            _pos[0] = startPos;
            _pos[simNodeCount - 1] = endPos;

            float maxMoveSqr = 0f;
            for (int i = 1; i < simNodeCount - 1; i++)
            {
                float m = (_pos[i] - _prevPos[i]).sqrMagnitude;
                if (m > maxMoveSqr)
                {
                    maxMoveSqr = m;
                }
            }

            return maxMoveSqr;
        }

        private void GetEndpoints(out Vector3 startPos, out Vector3 endPos, out Vector3 startDir, out Vector3 endDir)
        {
            startPos = boardAnchor.position;
            startDir = boardAnchor.TransformDirection(boardExitLocalDir).normalized;

            endPos = plugTransform.TransformPoint(plugTailLocalOffset);
            endDir = plugTransform.TransformDirection(plugExitLocalDir).normalized;
        }

        private void RebuildTubeMesh()
        {
            if (_tubeMesh == null)
            {
                return;
            }

            // 1. Строим идеально гладкую кривую Catmull-Rom через физические узлы.
            int lastSim = simNodeCount - 1;
            for (int i = 0; i < visualRingCount; i++)
            {
                float scaled = (i / (float)(visualRingCount - 1)) * lastSim;
                int seg = Mathf.Min(Mathf.FloorToInt(scaled), lastSim - 1);
                float u = scaled - seg;

                Vector3 p0 = _pos[Mathf.Max(0, seg - 1)];
                Vector3 p1 = _pos[seg];
                Vector3 p2 = _pos[seg + 1];
                Vector3 p3 = _pos[Mathf.Min(lastSim, seg + 2)];

                _smoothCurve[i] = EvalCatmullRom(p0, p1, p2, p3, u);
            }

            _smoothCurve[0] = _pos[0];
            _smoothCurve[visualRingCount - 1] = _pos[lastSim];

            // 2. Генерируем 3D-трубку с Parallel Transport Frame (без перекрутов).
            Matrix4x4 worldToLocal = transform.worldToLocalMatrix;

            Vector3 prevTangent = (_smoothCurve[1] - _smoothCurve[0]).normalized;
            if (prevTangent.sqrMagnitude < 1e-6f)
            {
                prevTangent = Vector3.up;
            }

            Vector3 normal = Vector3.Cross(prevTangent, Vector3.right);
            if (normal.sqrMagnitude < 1e-4f)
            {
                normal = Vector3.Cross(prevTangent, Vector3.forward);
            }
            normal.Normalize();

            for (int i = 0; i < visualRingCount; i++)
            {
                Vector3 tangent;
                if (i == 0)
                {
                    tangent = (_smoothCurve[1] - _smoothCurve[0]).normalized;
                }
                else if (i == visualRingCount - 1)
                {
                    tangent = (_smoothCurve[i] - _smoothCurve[i - 1]).normalized;
                }
                else
                {
                    tangent = (_smoothCurve[i + 1] - _smoothCurve[i - 1]).normalized;
                }

                if (tangent.sqrMagnitude < 1e-6f)
                {
                    tangent = prevTangent;
                }

                Quaternion rot = Quaternion.FromToRotation(prevTangent, tangent);
                normal = (rot * normal).normalized;
                Vector3 binormal = Vector3.Cross(tangent, normal).normalized;
                prevTangent = tangent;

                Vector3 centerWorld = _smoothCurve[i];
                int rowOffset = i * radialSegments;

                for (int r = 0; r < radialSegments; r++)
                {
                    float angle = (r / (float)radialSegments) * Mathf.PI * 2f;
                    Vector3 radialDirWorld = normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);
                    Vector3 vertWorld = centerWorld + radialDirWorld * cordRadius;

                    _vertices[rowOffset + r] = worldToLocal.MultiplyPoint3x4(vertWorld);
                    _normals[rowOffset + r] = worldToLocal.MultiplyVector(radialDirWorld).normalized;
                }
            }

            _tubeMesh.vertices = _vertices;
            _tubeMesh.normals = _normals;
            _tubeMesh.RecalculateBounds();
        }

        private static Vector3 EvalCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private static Vector3 EvalCubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return (u * u * u) * p0 +
                   (3f * u * u * t) * p1 +
                   (3f * u * t * t) * p2 +
                   (t * t * t) * p3;
        }
    }
}
