using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Splines;

namespace SplineCraft
{
    public enum InstanceAxis
    {
        X, NegX,
        Y, NegY,
        Z, NegZ
    }

    /// <summary>
    /// Places repeated prefab instances at arc-length-even intervals along a SplineContainer sub-range.
    /// Equivalent to Unreal's construction-script actor placement pattern along a spline.
    /// The spline is read-only; this component never modifies spline knots.
    /// </summary>
    [ExecuteAlways]
    public class SplineInstancer : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────────────────────

        [Header("Spline Source")]
        [Tooltip("SplineContainer to place along. Not owned by this component.")]
        public SplineContainer splineContainer;
        public int splineIndex = 0;

        [Header("Sub-Range (metres, -1 = full spline)")]
        public float startDistance = -1f;
        public float endDistance   = -1f;

        [Header("Item Prefab")]
        [Tooltip("Prefab placed at each interval position.")]
        public GameObject itemPrefab;

        [Header("Connector Mesh (optional)")]
        [Tooltip("Mesh stretched straight between consecutive instances (e.g. fence rail). Curvature-following is a future extension.")]
        public Mesh connectorMesh;
        public Material connectorMaterial;

        [Header("Orientation")]
        [Tooltip("Which local axis of the prefab points along the spline tangent (its 'forward').")]
        public InstanceAxis forwardAxis = InstanceAxis.Z;

        [Header("Spacing")]
        [Tooltip("Multiplier on the prefab's mesh bounds along the forward axis. 1 = tight, 1.1 = 10% gap.")]
        public float spacingMultiplier = 1f;

        [Tooltip("How instances fill the range.")]
        public FitMode fitMode = FitMode.EvenRedistribute;

        [Header("Output")]
        public OutputMode outputMode = OutputMode.LiveInstances;

        // ── Internal state ───────────────────────────────────────────────────

        readonly List<GameObject>  _liveItems      = new List<GameObject>();
        readonly List<GameObject>  _liveConnectors = new List<GameObject>();
        GameObject _bakedRoot;

        // ── Unity lifecycle ──────────────────────────────────────────────────

        void OnEnable()
        {
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

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>Clears and regenerates all instances.</summary>
        public void Rebuild()
        {
            ClearAll();
            if (!ValidateInputs()) return;

            var spline = splineContainer.Splines[splineIndex];
            var table  = SplineMathUtils.Build(spline);
            var frames = SplineMathUtils.ComputeRMFFrames(spline, 512);

            float start = ResolvedStart(table);
            float end   = ResolvedEnd(table);
            if (end <= start) return;

            var positions = ComputeInstancePositions(table, start, end);
            if (positions.Count == 0) return;

            if (outputMode == OutputMode.LiveInstances)
                SpawnLiveInstances(spline, table, frames, positions, start, end);
            else
                BakeCombinedMesh(spline, table, frames, positions, start, end);
        }

        /// <summary>
        /// Converts live instances to a baked static mesh, removes live GameObjects,
        /// and disables this component. For shipping — irreversible without undo.
        /// </summary>
        public void BakeToStatic()
        {
            if (outputMode != OutputMode.LiveInstances) return;
            outputMode = OutputMode.BakedMesh;
            Rebuild();
            enabled = false;
        }

        // ── Instance position computation ─────────────────────────────────────

        List<float> ComputeInstancePositions(
            SplineMathUtils.ArcLengthTable table, float start, float end)
        {
            float rangeLength = end - start;
            float spacing = ComputeSpacing();
            var positions = new List<float>();

            switch (fitMode)
            {
                case FitMode.FixedSpacingStretchLast:
                    for (float d = start; d <= end + 1e-4f; d += spacing)
                        positions.Add(Mathf.Min(d, end));
                    break;

                case FitMode.EvenRedistribute:
                    int count = Mathf.Max(2, Mathf.RoundToInt(rangeLength / spacing) + 1);
                    float step = rangeLength / (count - 1);
                    for (int i = 0; i < count; i++)
                        positions.Add(start + i * step);
                    break;

                case FitMode.FixedCount:
                    int fixedCount = Mathf.Max(2, Mathf.RoundToInt(rangeLength / spacing));
                    float fixedStep = rangeLength / (fixedCount - 1);
                    for (int i = 0; i < fixedCount; i++)
                        positions.Add(start + i * fixedStep);
                    break;
            }

            return positions;
        }

        // ── Live instance placement ───────────────────────────────────────────

        void SpawnLiveInstances(
            ISpline spline, SplineMathUtils.ArcLengthTable table,
            SplineMathUtils.SplineFrame[] frames,
            List<float> positions, float start, float end)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                var frame = SampleFrame(spline, table, frames, positions[i]);
                var go    = SpawnItem(frame, i);
                _liveItems.Add(go);

                if (connectorMesh != null && i < positions.Count - 1)
                {
                    var nextFrame = SampleFrame(spline, table, frames, positions[i + 1]);
                    var conn = SpawnConnector(frame, nextFrame, i);
                    _liveConnectors.Add(conn);
                }
            }
        }

        GameObject SpawnItem(SplineMathUtils.SplineFrame frame, int index)
        {
            var go = itemPrefab != null
                ? (GameObject)UnityEditor_InstantiatePrefabOrCreate(itemPrefab, transform)
                : new GameObject($"Item_{index}");

            go.transform.position = transform.TransformPoint(
                transform.InverseTransformPoint(frame.Position));
            go.transform.position = frame.Position;
            go.transform.rotation = AxisRotation(frame.Tangent, frame.Up, forwardAxis);
            go.transform.SetParent(transform, worldPositionStays: true);
            go.name = $"Item_{index}";
            return go;
        }

        GameObject SpawnConnector(
            SplineMathUtils.SplineFrame from, SplineMathUtils.SplineFrame to, int index)
        {
            var go = new GameObject($"Connector_{index}");
            go.transform.SetParent(transform, worldPositionStays: true);

            Vector3 mid = (from.Position + to.Position) * 0.5f;
            Vector3 dir = (to.Position - from.Position);
            float length = dir.magnitude;

            go.transform.position = mid;
            go.transform.rotation = Quaternion.LookRotation(dir.normalized, from.Up);
            go.transform.localScale = new Vector3(1f, 1f, length);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = connectorMesh;
            if (connectorMaterial != null) mr.sharedMaterial = connectorMaterial;

            return go;
        }

        // ── Baked mesh output ─────────────────────────────────────────────────

        void BakeCombinedMesh(
            ISpline spline, SplineMathUtils.ArcLengthTable table,
            SplineMathUtils.SplineFrame[] frames,
            List<float> positions, float start, float end)
        {
            if (itemPrefab == null) return;

            var sourceMesh = GetPrefabMesh(itemPrefab);
            if (sourceMesh == null) return;

            var combines = new List<CombineInstance>();

            foreach (float dist in positions)
            {
                var frame = SampleFrame(spline, table, frames, dist);
                var matrix = Matrix4x4.TRS(
                    frame.Position,
                    Quaternion.LookRotation(frame.Tangent, frame.Up),
                    Vector3.one);

                combines.Add(new CombineInstance
                {
                    mesh = sourceMesh,
                    transform = transform.worldToLocalMatrix * matrix
                });
            }

            BuildChunkedMesh(combines);
        }

        void BuildChunkedMesh(List<CombineInstance> combines)
        {
            const int maxVerts = 65000;
            _bakedRoot = new GameObject("Baked_SplineInstances");
            _bakedRoot.transform.SetParent(transform, worldPositionStays: false);

            var chunk = new List<CombineInstance>();
            int chunkVerts = 0;
            int chunkIndex = 0;

            foreach (var ci in combines)
            {
                int vcount = ci.mesh.vertexCount;
                if (chunkVerts + vcount > maxVerts && chunk.Count > 0)
                {
                    CreateChunkObject(chunk, chunkIndex++, _bakedRoot.transform);
                    chunk.Clear();
                    chunkVerts = 0;
                }
                chunk.Add(ci);
                chunkVerts += vcount;
            }

            if (chunk.Count > 0)
                CreateChunkObject(chunk, chunkIndex, _bakedRoot.transform);
        }

        void CreateChunkObject(List<CombineInstance> combines, int index, Transform parent)
        {
            var go = new GameObject($"BakedChunk_{index}");
            go.transform.SetParent(parent, worldPositionStays: false);

            var mesh = new Mesh { name = $"BakedMesh_{index}" };
            mesh.CombineMeshes(combines.ToArray(), mergeSubMeshes: true, useMatrices: true);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
        }

        // ── Cleanup ───────────────────────────────────────────────────────────

        void ClearAll()
        {
            // Destroy by children — list is unreliable across domain reloads
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate_Safe(transform.GetChild(i).gameObject);

            _liveItems.Clear();
            _liveConnectors.Clear();
            _bakedRoot = null;
        }

        // ── Validation & helpers ─────────────────────────────────────────────

        float ComputeSpacing()
        {
            var mesh = GetPrefabMesh(itemPrefab);
            if (mesh == null) return spacingMultiplier;
            Vector3 size = mesh.bounds.size;
            float axisSize = forwardAxis switch
            {
                InstanceAxis.X or InstanceAxis.NegX => size.x,
                InstanceAxis.Y or InstanceAxis.NegY => size.y,
                _                                   => size.z,
            };
            return Mathf.Max(0.01f, axisSize) * Mathf.Max(0.01f, spacingMultiplier);
        }

        bool ValidateInputs()
        {
            if (splineContainer == null) return false;
            if (splineIndex < 0 || splineIndex >= splineContainer.Splines.Count) return false;
            if (spacingMultiplier <= 0f) return false;
            return true;
        }

        float ResolvedStart(SplineMathUtils.ArcLengthTable table) =>
            startDistance < 0f ? 0f : Mathf.Clamp(startDistance, 0f, table.TotalLength);

        float ResolvedEnd(SplineMathUtils.ArcLengthTable table) =>
            endDistance < 0f ? table.TotalLength : Mathf.Clamp(endDistance, 0f, table.TotalLength);

        SplineMathUtils.SplineFrame SampleFrame(
            ISpline spline, SplineMathUtils.ArcLengthTable table,
            SplineMathUtils.SplineFrame[] frames, float worldDist)
        {
            float t = table.DistanceToT(worldDist);
            var local = SplineMathUtils.SampleFrameAtT(spline, frames, t);
            return ToWorldSpace(local, splineContainer.transform);
        }

        static SplineMathUtils.SplineFrame ToWorldSpace(SplineMathUtils.SplineFrame f, Transform t) =>
            new SplineMathUtils.SplineFrame
            {
                Position = t.TransformPoint(f.Position),
                Tangent  = t.TransformDirection(f.Tangent).normalized,
                Up       = t.TransformDirection(f.Up).normalized,
                Right    = t.TransformDirection(f.Right).normalized
            };

        void OnSplineChanged(Spline changed, int idx, SplineModification mod)
        {
            if (splineContainer != null && splineContainer.Splines.Contains(changed)) Rebuild();
        }

        static Mesh GetPrefabMesh(GameObject prefab)
        {
            var mf = prefab.GetComponentInChildren<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        // Editor/runtime safe instantiation and destroy
        static GameObject UnityEditor_InstantiatePrefabOrCreate(GameObject prefab, Transform parent)
        {
#if UNITY_EDITOR
            return (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
#else
            return Instantiate(prefab, parent);
#endif
        }

        static Quaternion AxisRotation(Vector3 tangent, Vector3 up, InstanceAxis axis)
        {
            Vector3 right = Vector3.Cross(tangent, up).normalized;
            return axis switch
            {
                InstanceAxis.X    => Quaternion.LookRotation(tangent, up) * Quaternion.Euler(0, -90, 0),
                InstanceAxis.NegX => Quaternion.LookRotation(tangent, up) * Quaternion.Euler(0,  90, 0),
                InstanceAxis.Y    => Quaternion.LookRotation(tangent, up) * Quaternion.Euler( 90, 0, 0),
                InstanceAxis.NegY => Quaternion.LookRotation(tangent, up) * Quaternion.Euler(-90, 0, 0),
                InstanceAxis.NegZ => Quaternion.LookRotation(-tangent, up),
                _                 => Quaternion.LookRotation(tangent, up),  // Z
            };
        }

        static void DestroyImmediate_Safe(GameObject go)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(go);
            else Destroy(go);
#else
            Destroy(go);
#endif
        }
    }

    public enum FitMode
    {
        /// <summary>Fixed spacing; last gap is stretched or compressed to reach the end.</summary>
        FixedSpacingStretchLast,
        /// <summary>Counts how many fit, then redistributes evenly across the full range.</summary>
        EvenRedistribute,
        /// <summary>Derives a fixed count from spacing, ignores leftover distance.</summary>
        FixedCount
    }

    public enum OutputMode
    {
        /// <summary>Spawns actual GameObjects — editable individually in the hierarchy.</summary>
        LiveInstances,
        /// <summary>Combines into a single static mesh (chunked at 64k verts) for runtime perf.</summary>
        BakedMesh
    }
}
