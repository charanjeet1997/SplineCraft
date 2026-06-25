using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace SplineCraft.Editor
{
    [CustomEditor(typeof(SplineInstancer))]
    public class SplineInstancerEditor : UnityEditor.Editor
    {
        static readonly Color RangeColor    = new Color(1f, 0.7f, 0.2f, 1f);
        static readonly Color InstanceColor = new Color(1f, 0.7f, 0.2f, 0.6f);

        public override void OnInspectorGUI()
        {
            var instancer = (SplineInstancer)target;

            SplineEmbeddedEditor.DrawEditToggle(instancer.splineContainer);
            SplineEmbeddedEditor.HandlePendingActions(instancer.splineContainer);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "Places repeated prefab instances at arc-length-even intervals along a spline sub-range.\n" +
                "Use case examples: fence posts + rail connectors, bollards, light poles, mooring points.\n" +
                "Tip — fence: item=post prefab, connector=rail mesh, fit mode=EvenRedistribute.\n" +
                "Tip — wall gap: set start/end distances to leave a gap where a gate sits.",
                MessageType.Info);

            DrawDefaultInspector();

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(instancer.splineContainer == null || instancer.itemPrefab == null))
            {
                if (GUILayout.Button("Rebuild"))
                    instancer.Rebuild();

                EditorGUILayout.Space();

                var prevColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
                if (GUILayout.Button("Clear Instances", GUILayout.Height(24)))
                {
                    Undo.RecordObject(instancer.gameObject, "Clear Instances");
                    instancer.ClearInstances();
                }
                if (GUILayout.Button("Clear All Splines", GUILayout.Height(24)))
                {
                    if (EditorUtility.DisplayDialog("Clear All Splines",
                        "This will remove all splines from the SplineContainer. Undo to recover.",
                        "Clear", "Cancel"))
                    {
                        Undo.RecordObject(instancer.splineContainer, "Clear All Splines");
                        for (int i = instancer.splineContainer.Splines.Count - 1; i >= 0; i--)
                            instancer.splineContainer.RemoveSplineAt(i);
                        instancer.splineContainer.AddSpline();
                        instancer.ClearInstances();
                        EditorUtility.SetDirty(instancer.splineContainer);
                    }
                }
                GUI.backgroundColor = prevColor;

                EditorGUILayout.Space();

                if (GUILayout.Button("Bake to Static (irreversible without undo)"))
                {
                    if (EditorUtility.DisplayDialog(
                        "Bake to Static",
                        "This converts live instances to a single baked mesh and disables the component. Use Ctrl+Z to undo.",
                        "Bake", "Cancel"))
                    {
                        Undo.RecordObject(instancer.gameObject, "Bake SplineInstancer");
                        instancer.BakeToStatic();
                    }
                }
            }

            if (instancer.splineContainer == null)
                EditorGUILayout.HelpBox("Assign a SplineContainer.", MessageType.Warning);
            else if (instancer.itemPrefab == null)
                EditorGUILayout.HelpBox("Assign an Item Prefab to preview instances.", MessageType.Warning);
        }

        void OnSceneGUI()
        {
            var instancer = (SplineInstancer)target;
            if (instancer.splineContainer == null) return;

            SplineEmbeddedEditor.DrawSplineHandles(instancer.splineContainer);

            var containerTransform = instancer.splineContainer.transform;

            foreach (var spline in instancer.splineContainer.Splines)
            {
                if (spline.Count < 2) continue;
                var table  = SplineMathUtils.Build(spline);
                var frames = SplineMathUtils.ComputeRMFFrames(spline, 256);

                float start = instancer.startDistance < 0f ? 0f :
                    Mathf.Clamp(instancer.startDistance, 0f, table.TotalLength);
                float end = instancer.endDistance < 0f ? table.TotalLength :
                    Mathf.Clamp(instancer.endDistance, 0f, table.TotalLength);

                DrawRangeGizmo(spline, table, containerTransform, start, end, instancer.gameObject.name);
                DrawInstanceGizmos(spline, table, frames, containerTransform, instancer, start, end);
            }
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

        static void DrawInstanceGizmos(
            ISpline spline, SplineMathUtils.ArcLengthTable table,
            SplineMathUtils.SplineFrame[] frames, Transform containerTransform,
            SplineInstancer instancer, float start, float end)
        {
            if (instancer.spacingMultiplier <= 0f || instancer.itemPrefab == null) return;

            Handles.color = InstanceColor;
            float rangeLength = end - start;
            var mesh = instancer.itemPrefab.GetComponentInChildren<MeshFilter>()?.sharedMesh;
            Vector3 bSize = mesh != null ? mesh.bounds.size : Vector3.one;
            float axisSize = instancer.forwardAxis switch {
                InstanceAxis.X    or InstanceAxis.NegX  => bSize.x,
                InstanceAxis.Y    or InstanceAxis.NegY  => bSize.y,
                InstanceAxis.Z    or InstanceAxis.NegZ  => bSize.z,
                InstanceAxis.XY   or InstanceAxis.NegXY => Mathf.Max(bSize.x, bSize.y),
                InstanceAxis.XZ   or InstanceAxis.NegXZ => Mathf.Max(bSize.x, bSize.z),
                InstanceAxis.YZ   or InstanceAxis.NegYZ => Mathf.Max(bSize.y, bSize.z),
                _                                       => bSize.z,
            };
            float spacing = Mathf.Max(0.01f, axisSize) * instancer.spacingMultiplier;
            int count = Mathf.Max(2, Mathf.RoundToInt(rangeLength / spacing) + 1);
            float step = rangeLength / (count - 1);

            for (int i = 0; i < count; i++)
            {
                float d = start + i * step;
                float t = table.DistanceToT(d);
                var frame = SplineMathUtils.SampleFrameAtT(spline, frames, t);
                Vector3 pos = containerTransform.TransformPoint(frame.Position);
                Vector3 tan = containerTransform.TransformDirection(frame.Tangent).normalized;

                Handles.DrawWireCube(pos, Vector3.one * 0.3f);
                Handles.DrawLine(pos, pos + tan * 0.6f);
            }
        }
    }
}
