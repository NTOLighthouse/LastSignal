using System.Collections.Generic;
using UnityEngine;

namespace LastSignal.SwitchboardSystem
{
    /// <summary>
    /// Рисует чёткую аниме/outliner-обводку вокруг 3D-силуэта объекта через Stencil-маску.
    /// Обводка рисуется строго по внешнему контуру поверх геометрии панели и никогда не прячется в модели коммутатора.
    /// </summary>
    public sealed class MeshOutlineHighlighter : MonoBehaviour
    {
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly Dictionary<Mesh, Mesh> SmoothMeshCache = new Dictionary<Mesh, Mesh>();

        [Header("Стиль аниме-обводки")]
        [SerializeField] private Material maskMaterialTemplate;
        [SerializeField] private Material outlineMaterialTemplate;
        [SerializeField] private Color outlineColor = new Color(1f, 0.86f, 0.1f, 1f);
        [SerializeField, Range(0.0005f, 0.03f)] private float outlineWidth = 0.0045f;

        private MeshRenderer _maskRenderer;
        private MeshRenderer _outlineRenderer;
        private MaterialPropertyBlock _propertyBlock;
        private bool _initialized;

        public bool IsOutlined => _outlineRenderer != null && _outlineRenderer.enabled;

        private void Awake()
        {
            EnsureInitialized();
            SetOutlined(false);
        }

        /// <summary>
        /// Включает или выключает аниме-обводку на этом объекте.
        /// </summary>
        public void SetOutlined(bool visible)
        {
            EnsureInitialized();
            if (_maskRenderer == null || _outlineRenderer == null)
            {
                return;
            }

            _maskRenderer.enabled = visible;
            _outlineRenderer.enabled = visible;

            if (visible)
            {
                ApplyStyle();
            }
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _propertyBlock = new MaterialPropertyBlock();

            MeshFilter sourceFilter = GetComponent<MeshFilter>();
            if (sourceFilter == null || sourceFilter.sharedMesh == null)
            {
                return;
            }

            Material maskMat = ResolveMaskMaterial();
            Material outlineMat = ResolveOutlineMaterial();
            if (maskMat == null || outlineMat == null)
            {
                Debug.LogWarning("[MeshOutlineHighlighter] Не найдены шейдеры 'LastSignal/AnimeOutlineMask' или 'LastSignal/AnimeOutline'.", this);
                return;
            }

            // 1. Объект Stencil-маски (повторяет точный силуэт самого объекта).
            GameObject maskObj = new GameObject("AnimeOutlineMask");
            maskObj.transform.SetParent(transform, false);
            maskObj.transform.localPosition = Vector3.zero;
            maskObj.transform.localRotation = Quaternion.identity;
            maskObj.transform.localScale = Vector3.one;
            maskObj.layer = gameObject.layer;

            MeshFilter maskFilter = maskObj.AddComponent<MeshFilter>();
            maskFilter.sharedMesh = sourceFilter.sharedMesh;

            _maskRenderer = maskObj.AddComponent<MeshRenderer>();
            _maskRenderer.sharedMaterial = maskMat;
            _maskRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _maskRenderer.receiveShadows = false;
            _maskRenderer.enabled = false;

            // 2. Объект самой обводки (раздутый меш со сглаженными нормалями, рисуется строго снаружи маски).
            GameObject outlineObj = new GameObject("AnimeOutlineMesh");
            outlineObj.transform.SetParent(transform, false);
            outlineObj.transform.localPosition = Vector3.zero;
            outlineObj.transform.localRotation = Quaternion.identity;
            outlineObj.transform.localScale = Vector3.one;
            outlineObj.layer = gameObject.layer;

            MeshFilter outlineFilter = outlineObj.AddComponent<MeshFilter>();
            outlineFilter.sharedMesh = GetOrCreateSmoothedMesh(sourceFilter.sharedMesh);

            _outlineRenderer = outlineObj.AddComponent<MeshRenderer>();
            _outlineRenderer.sharedMaterial = outlineMat;
            _outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _outlineRenderer.receiveShadows = false;
            _outlineRenderer.enabled = false;

            ApplyStyle();
        }

        private void ApplyStyle()
        {
            if (_outlineRenderer == null)
            {
                return;
            }

            _outlineRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(OutlineColorId, outlineColor);
            _propertyBlock.SetFloat(OutlineWidthId, outlineWidth);
            _outlineRenderer.SetPropertyBlock(_propertyBlock);
        }

        private Material ResolveMaskMaterial()
        {
            if (maskMaterialTemplate != null)
            {
                return maskMaterialTemplate;
            }

            Shader shader = Shader.Find("LastSignal/AnimeOutlineMask");
            if (shader == null)
            {
                return null;
            }

            maskMaterialTemplate = new Material(shader);
            return maskMaterialTemplate;
        }

        private Material ResolveOutlineMaterial()
        {
            if (outlineMaterialTemplate != null)
            {
                return outlineMaterialTemplate;
            }

            Shader shader = Shader.Find("LastSignal/AnimeOutline");
            if (shader == null)
            {
                return null;
            }

            outlineMaterialTemplate = new Material(shader);
            return outlineMaterialTemplate;
        }

        /// <summary>
        /// Создаёт (и кеширует) копию меша с усреднёнными нормалями в общих вершинах,
        /// чтобы обводка была цельной и гладкой даже на острых гранях.
        /// </summary>
        private static Mesh GetOrCreateSmoothedMesh(Mesh source)
        {
            if (SmoothMeshCache.TryGetValue(source, out Mesh cached) && cached != null)
            {
                return cached;
            }

            Mesh copy = Instantiate(source);
            copy.name = source.name + "_AnimeOutline";

            Vector3[] vertices = copy.vertices;
            Vector3[] normals = copy.normals;
            if (normals == null || normals.Length != vertices.Length)
            {
                copy.RecalculateNormals();
                normals = copy.normals;
            }

            var accumulated = new Dictionary<Vector3Int, Vector3>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3Int key = Quantize(vertices[i]);
                if (accumulated.TryGetValue(key, out Vector3 sum))
                {
                    accumulated[key] = sum + normals[i];
                }
                else
                {
                    accumulated[key] = normals[i];
                }
            }

            Vector3[] smoothNormals = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 n = accumulated[Quantize(vertices[i])];
                smoothNormals[i] = n.sqrMagnitude > 0.000001f ? n.normalized : normals[i];
            }

            copy.normals = smoothNormals;
            SmoothMeshCache[source] = copy;
            return copy;
        }

        private static Vector3Int Quantize(Vector3 v)
        {
            return new Vector3Int(
                Mathf.RoundToInt(v.x * 10000f),
                Mathf.RoundToInt(v.y * 10000f),
                Mathf.RoundToInt(v.z * 10000f));
        }
    }
}
