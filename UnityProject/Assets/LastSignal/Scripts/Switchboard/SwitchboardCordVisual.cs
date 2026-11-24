using UnityEngine;

namespace LastSignal.Switchboard
{
    /// <summary>
    /// Визуализирует тканевый коммутационный шнур с естественным провисанием
    /// от гнезда-дока до хвостовика штекера.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class SwitchboardCordVisual : MonoBehaviour
    {
        [Header("Точки крепления шнура")]
        [SerializeField] private Transform startAnchor;
        [SerializeField] private Vector3 startWorldOffset = new Vector3(0f, 0.008f, 0.026f);
        [SerializeField] private Transform plugTransform;
        [SerializeField] private Vector3 plugTailLocalOffset = new Vector3(0f, 0f, -0.014f);

        [Header("Форма и провисание")]
        [SerializeField, Range(6, 32)] private int segmentCount = 16;
        [SerializeField, Min(0.002f)] private float cordRadius = 0.0042f;
        [SerializeField, Min(0f)] private float baseSag = 0.055f;
        [SerializeField, Min(0f)] private float extraSlackWhenDocked = 0.03f;
        [SerializeField] private float forwardClearance = 0.025f;

        private LineRenderer _lineRenderer;
        private Vector3[] _points;

        private void Awake()
        {
            EnsureLineRenderer();
        }

        private void LateUpdate()
        {
            if (plugTransform == null)
            {
                return;
            }

            EnsureLineRenderer();
            if (_points == null || _points.Length != segmentCount)
            {
                _points = new Vector3[segmentCount];
                _lineRenderer.positionCount = segmentCount;
            }

            Vector3 p0 = startAnchor != null
                ? startAnchor.position + startWorldOffset
                : transform.position;
            Vector3 p2 = plugTransform.TransformPoint(plugTailLocalOffset);

            float dist = Vector3.Distance(p0, p2);
            float sag = baseSag + Mathf.Clamp01(0.18f - dist) * extraSlackWhenDocked * 4f;

            // Контрольная точка дуги Беье смещается вниз (провисание под весом шнура) и чуть наружу (+Z к оператору),
            // чтобы шнур не проваливался сквозь металлическую панель коммутатора.
            Vector3 mid = (p0 + p2) * 0.5f;
            mid.y -= sag;
            mid.z += forwardClearance;

            for (int i = 0; i < segmentCount; i++)
            {
                float t = i / (float)(segmentCount - 1);
                float inv = 1f - t;
                _points[i] = inv * inv * p0 + 2f * inv * t * mid + t * t * p2;
            }

            _lineRenderer.SetPositions(_points);
        }

        public void Configure(Transform dockAnchor, Transform plug, Material cordMaterial)
        {
            startAnchor = dockAnchor;
            plugTransform = plug;
            EnsureLineRenderer();

            if (cordMaterial != null)
            {
                _lineRenderer.sharedMaterial = cordMaterial;
            }
        }

        private void EnsureLineRenderer()
        {
            if (_lineRenderer == null)
            {
                _lineRenderer = GetComponent<LineRenderer>();
            }

            _lineRenderer.useWorldSpace = true;
            _lineRenderer.startWidth = cordRadius * 2f;
            _lineRenderer.endWidth = cordRadius * 2f;
            _lineRenderer.numCapVertices = 4;
            _lineRenderer.numCornerVertices = 4;
            _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _lineRenderer.receiveShadows = true;
            _lineRenderer.textureMode = LineTextureMode.Tile;
        }
    }
}
