using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace SplineCraft.Editor
{
    [CustomEditor(typeof(SplineMeshDeformer))]
    public class SplineMeshDeformerEditor : UnityEditor.Editor
    {
        static readonly Color RangeColor = new Color(0.2f, 0.8f, 1f, 1f);

        public override void OnInspectorGUI()
        {
            var deformer = (SplineMeshDeformer)target;

            SplineEmbeddedEditor.DrawEditToggle(deformer.splineContainer);
            SplineEmbeddedEditor.HandlePendingActions(deformer.splineContainer);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "Deforms a mesh continuously along a spline sub-range.\n" +
                "Use case examples: walls, pipes, cables, curved road shoulders.\n" +
                "Set start/end distances to cover only part of the spline (Unreal-style multi-deformer pattern).",
                MessageType.Info);

            DrawDefaultInspector();

            EditorGUILayout.Space();
            if (GUILayout.Button("Rebuild Mesh"))
                deformer.Rebuild();

            if (deformer.splineContainer == null || deformer.sourceMesh == null)
                EditorGUILayout.HelpBox("Assign a SplineContainer and Source Mesh to preview.", MessageType.Warning);
        }

        void OnSceneGUI()
        {
            var deformer = (SplineMeshDeformer)target;
            if (deformer.splineContainer == null) return;
            if (deformer.splineIndex < 0 || deformer.splineIndex >= deformer.splineContainer.Splines.Count) return;

            var spline = deformer.splineContainer.Splines[deformer.splineIndex];
            if (spline.Count < 2) return;
            var table  = SplineMathUtils.Build(spline);

            float start = deformer.startDistance < 0f ? 0f :
                Mathf.Clamp(deformer.startDistance, 0f, table.TotalLength);
            float end = deformer.endDistance < 0f ? table.TotalLength :
                Mathf.Clamp(deformer.endDistance, 0f, table.TotalLength);

            SplineEmbeddedEditor.DrawSplineHandles(deformer.splineContainer);
            DrawRangeGizmo(spline, table, deformer.splineContainer.transform, start, end, deformer.gameObject.name);
        }

        static void DrawRangeGizmo(
            ISpline spline, SplineMathUtils.ArcLengthTable table,
            Transform containerTransform, float start, float end, string label)
        {
            Handles.color = RangeColor;

            const int segments = 64;
            Vector3 prev = Vector3.zero;
            bool hasPrev = false;

            for (int i = 0; i <= segments; i++)
            {
                float d = Mathf.Lerp(start, end, (float)i / segments);
                float t = table.DistanceToT(d);
                Vector3 pos = containerTransform.TransformPoint((Vector3)spline.EvaluatePosition(t));

                if (hasPrev) Handles.DrawLine(prev, pos, 3f);
                prev = pos;
                hasPrev = true;
            }

            float tStart = table.DistanceToT(start);
            Vector3 labelPos = containerTransform.TransformPoint((Vector3)spline.EvaluatePosition(tStart));
            Handles.Label(labelPos + Vector3.up * 0.5f, $"{label}\n{start:F1}m – {end:F1}m");
        }
    }
}
