using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Splines;

namespace SplineCraft
{
    /// <summary>
    /// Continuously deforms a source mesh along a sub-range of a SplineContainer.
    /// Equivalent to Unreal's SplineMeshComponent — multiple deformers can share one spline.
    /// The spline is read-only; this component never modifies spline knots.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SplineMeshDeformer : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────────────

        [Header("Spline Source")]
        [Tooltip("SplineContainer to deform along. Not owned by this component.")]
        public SplineContainer splineContainer;

        [Tooltip("Index of the spline within the container.")]
        public int splineIndex = 0;

        [Header("Sub-Range (metres, -1 = full spline)")]
        [Tooltip("Start arc-length distance. 0 = spline start.")]
        public float startDistance = -1f;
        [Tooltip("End arc-length distance. -1 = spline end.")]
        public float endDistance = -1f;

        [Header("Source Mesh")]
        [Tooltip("Mesh authored as a straight segment. Axis below is its 'forward' direction.")]
        public Mesh sourceMesh;

        [Tooltip("Which local axis of the source mesh runs along the spline direction.")]
        public MeshAxis forwardAxis = MeshAxis.Z;

        [Header("UV Tiling")]
        [Tooltip("Texture units per metre of arc length. Prevents texture stretch on curves.")]
        public float uvTilingScale = 1f;

        [Header("Scale Along Spline")]
        [Tooltip("Multiplies the mesh width (axis perpendicular to forward & up) by this curve over the component range.")]
        public AnimationCurve widthCurve = AnimationCurve.Constant(0, 1, 1);
        [Tooltip("Multiplies the mesh height (up axis) by this curve over the component range.")]
        public AnimationCurve heightCurve = AnimationCurve.Constant(0, 1, 1);

        [Header("Rebuild")]
        [Tooltip("When true the mesh rebuilds every frame — for animated/dynamic splines. Off = editor-only rebuild.")]
        public bool dynamicRebuild = false;

        // ── Internal state ───────────────────────────────────────────────────

        MeshFilter _meshFilter;
        Mesh _deformedMesh;

        // Reusable vertex buffers — allocated once, resized on demand
        List<Vector3> _vertices  = new List<Vector3>();
        List<Vector3> _normals   = new List<Vector3>();
        List<Vector4> _tangents  = new List<Vector4>();
        List<Vector2> _uvs       = new List<Vector2>();
        List<int>     _triangles = new List<int>();

        // ── Unity lifecycle ──────────────────────────────────────────────────

        void OnEnable()
        {
            _meshFilter = GetComponent<MeshFilter>();
            EnsureDeformedMesh();
            Rebuild();
            Spline.Changed += OnSplineChanged;
        }

        void OnDisable()
        {
            Spline.Changed -= OnSplineChanged;
        }

        void OnValidate()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () => { if (this) Rebuild(); };
#endif
        }

        void Update()
        {
            if (dynamicRebuild) Rebuild();
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>Rebuilds the deformed mesh from the current settings.</summary>
        public void Rebuild()
        {
            if (!ValidateInputs()) return;

            var spline = splineContainer.Splines[splineIndex];
            var table  = SplineMathUtils.Build(spline);

            float start = ResolvedStart(table);
            float end   = ResolvedEnd(table);
            if (end <= start) return;

            var frames = BuildFramesForRange(spline, table, start, end);
            DeformMesh(spline, table, frames, start, end);
        }

        // ── Mesh deformation ─────────────────────────────────────────────────

        void DeformMesh(
            ISpline spline, SplineMathUtils.ArcLengthTable table,
            SplineMathUtils.SplineFrame[] frames, float start, float end)
        {
            var srcVerts  = sourceMesh.vertices;
            var srcNorms  = sourceMesh.normals;
            var srcTans   = sourceMesh.tangents;
            var srcUVs    = sourceMesh.uv;
            var srcTris   = sourceMesh.triangles;

            int vc = srcVerts.Length;
            PrepareBuffers(vc, srcTris.Length);

            float rangeLength = end - start;
            (int fwdIdx, int rightIdx, int upIdx) = AxisIndices(forwardAxis);

            for (int i = 0; i < vc; i++)
            {
                Vector3 v = srcVerts[i];

                // The vertex's position along the forward axis maps to arc length within the range
                float localFwd    = GetComponent(v, fwdIdx);   // -0.5 to +0.5 typically
                float normalizedT = localFwd + 0.5f;           // 0-1 along component range
                float worldDist   = start + normalizedT * rangeLength;

                // Evaluate frame at this distance — convert from SplineContainer local to world space
                float splineT    = table.DistanceToT(worldDist);
                var localFrame   = SplineMathUtils.SampleFrameAtT(spline, frames, splineT);
                var frame        = ToWorldSpace(localFrame, splineContainer.transform);

                // Per-axis scale from curves
                float wScale = widthCurve.Evaluate(normalizedT);
                float hScale = heightCurve.Evaluate(normalizedT);

                float rightOffset = GetComponent(v, rightIdx) * wScale;
                float upOffset    = GetComponent(v, upIdx)    * hScale;

                // Place vertex in spline frame space — world-space result, then back to local
                Vector3 worldPos = frame.Position
                    + frame.Right * rightOffset
                    + frame.Up    * upOffset;

                _vertices[i] = transform.InverseTransformPoint(worldPos);

                // Transform normal and tangent into deformed frame
                Vector3 srcN = srcNorms.Length > i ? srcNorms[i] : Vector3.up;
                _normals[i] = transform.InverseTransformDirection(
                    frame.Right * GetComponent(srcN, rightIdx) +
                    frame.Up    * GetComponent(srcN, upIdx) +
                    frame.Tangent * GetComponent(srcN, fwdIdx)).normalized;

                if (srcTans.Length > i)
                {
                    Vector4 st = srcTans[i];
                    Vector3 tn = transform.InverseTransformDirection(
                        frame.Right * GetComponent(new Vector3(st.x, st.y, st.z), rightIdx) +
                        frame.Up    * GetComponent(new Vector3(st.x, st.y, st.z), upIdx) +
                        frame.Tangent * GetComponent(new Vector3(st.x, st.y, st.z), fwdIdx)).normalized;
                    _tangents[i] = new Vector4(tn.x, tn.y, tn.z, st.w);
                }

                // UV: remap U along arc length to prevent texture stretch
                float u = srcUVs.Length > i ? srcUVs[i].x : normalizedT;
                float arcU = (worldDist - start) * uvTilingScale;
                float v2 = srcUVs.Length > i ? srcUVs[i].y : 0f;
                _uvs[i] = new Vector2(arcU, v2);
            }

            for (int i = 0; i < srcTris.Length; i++) _triangles[i] = srcTris[i];

            ApplyToMesh();
        }

        SplineMathUtils.SplineFrame[] BuildFramesForRange(
            ISpline spline, SplineMathUtils.ArcLengthTable table, float start, float end)
        {
            // Build full RMF frames then we index by T — enough frames for smooth deformation
            int frameCount = Mathf.Max(64, sourceMesh.vertexCount / 4);
            return SplineMathUtils.ComputeRMFFrames(spline, frameCount);
        }

        // ── Buffer helpers ───────────────────────────────────────────────────

        void PrepareBuffers(int vertexCount, int triCount)
        {
            _vertices.Clear();  for (int i = 0; i < vertexCount; i++) _vertices.Add(Vector3.zero);
            _normals.Clear();   for (int i = 0; i < vertexCount; i++) _normals.Add(Vector3.up);
            _tangents.Clear();  for (int i = 0; i < vertexCount; i++) _tangents.Add(Vector4.zero);
            _uvs.Clear();       for (int i = 0; i < vertexCount; i++) _uvs.Add(Vector2.zero);
            _triangles.Clear(); for (int i = 0; i < triCount; i++)    _triangles.Add(0);
        }

        void ApplyToMesh()
        {
            _deformedMesh.Clear();
            _deformedMesh.SetVertices(_vertices);
            _deformedMesh.SetNormals(_normals);
            _deformedMesh.SetTangents(_tangents);
            _deformedMesh.SetUVs(0, _uvs);
            _deformedMesh.SetTriangles(_triangles, 0);
            _deformedMesh.RecalculateBounds();
            _meshFilter.sharedMesh = _deformedMesh;
        }

        void EnsureDeformedMesh()
        {
            if (_deformedMesh == null)
            {
                _deformedMesh = new Mesh { name = "SplineDeformed" };
                _deformedMesh.MarkDynamic();
            }
        }

        // ── Validation & helpers ─────────────────────────────────────────────

        bool ValidateInputs()
        {
            if (splineContainer == null || sourceMesh == null) return false;
            if (splineIndex < 0 || splineIndex >= splineContainer.Splines.Count) return false;
            return true;
        }

        float ResolvedStart(SplineMathUtils.ArcLengthTable table) =>
            startDistance < 0f ? 0f : Mathf.Clamp(startDistance, 0f, table.TotalLength);

        float ResolvedEnd(SplineMathUtils.ArcLengthTable table) =>
            endDistance < 0f ? table.TotalLength : Mathf.Clamp(endDistance, 0f, table.TotalLength);

        void OnSplineChanged(Spline changed, int idx, SplineModification mod)
        {
            if (splineContainer != null && splineContainer.Splines.Contains(changed)) Rebuild();
        }

        static SplineMathUtils.SplineFrame ToWorldSpace(SplineMathUtils.SplineFrame f, Transform t) =>
            new SplineMathUtils.SplineFrame
            {
                Position = t.TransformPoint(f.Position),
                Tangent  = t.TransformDirection(f.Tangent).normalized,
                Up       = t.TransformDirection(f.Up).normalized,
                Right    = t.TransformDirection(f.Right).normalized
            };

        static float GetComponent(Vector3 v, int index) =>
            index == 0 ? v.x : index == 1 ? v.y : v.z;

        static (int fwd, int right, int up) AxisIndices(MeshAxis axis) => axis switch
        {
            MeshAxis.X => (0, 1, 2),
            MeshAxis.Y => (1, 0, 2),
            _          => (2, 0, 1),   // Z default
        };
    }

    public enum MeshAxis { X, Y, Z }
}
