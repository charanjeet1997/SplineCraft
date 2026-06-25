using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace SplineCraft.Editor
{
    /// <summary>
    /// Embedded spline editor — click on scene geometry to extrude knots,
    /// drag existing knots to move them. Activated via "Edit Spline" toggle.
    /// </summary>
    public static class SplineEmbeddedEditor
    {
        static readonly Color SplineColor   = new Color(0.3f, 0.9f, 1f, 1f);
        static readonly Color KnotColor     = new Color(1f, 0.9f, 0.1f, 1f);
        static readonly Color SelectedColor = new Color(1f, 0.4f, 0.1f, 1f);
        static readonly Color PreviewColor  = new Color(0.5f, 1f, 0.5f, 0.8f);

        public static bool editMode = false;

        static int _selectedKnot   = -1;
        static int _selectedSpline = -1;
        static Vector3 _previewPos;
        static bool _hasPreview;

        static int _controlId;

        // ── Inspector toggle ─────────────────────────────────────────────────

        public static void DrawEditToggle(SplineContainer container)
        {
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();

            Color bg = editMode ? new Color(1f, 0.55f, 0.1f) : new Color(0.3f, 0.7f, 1f);
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = bg;
            if (GUILayout.Button(editMode ? "■  Exit Spline Edit" : "✎  Edit Spline", GUILayout.Height(30)))
            {
                editMode      = !editMode;
                _selectedKnot = -1;
                _hasPreview   = false;
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
            if (GUILayout.Button("+ New Spline", GUILayout.Height(30), GUILayout.Width(100)))
                _pendingAddSpline = true;

            GUI.backgroundColor = prev;
            EditorGUILayout.EndHorizontal();

            // Spline selector
            if (container != null && container.Splines.Count > 1)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Active Spline:", GUILayout.Width(90));
                for (int i = 0; i < container.Splines.Count; i++)
                {
                    bool active = _selectedSpline == i;
                    var btnColor = active ? new Color(1f, 0.8f, 0.2f) : Color.white;
                    GUI.backgroundColor = btnColor;
                    if (GUILayout.Button($"Spline {i}", GUILayout.Height(22)))
                    {
                        _selectedSpline = i;
                        _selectedKnot   = -1;
                        editMode        = true;
                        SceneView.RepaintAll();
                    }
                    GUI.backgroundColor = Color.white;
                }
                EditorGUILayout.EndHorizontal();
            }
            else if (container != null && container.Splines.Count == 1)
            {
                _selectedSpline = 0;
            }

            EditorGUILayout.Space(4);
        }

        public static bool _pendingAddSpline = false;

        public static void HandlePendingActions(SplineContainer container)
        {
            if (_pendingAddSpline)
            {
                _pendingAddSpline = false;
                Undo.RecordObject(container, "Add Spline");
                container.AddSpline();
                _selectedSpline = container.Splines.Count - 1;
                _selectedKnot   = -1;
                editMode        = true;
                EditorUtility.SetDirty(container);
                SceneView.RepaintAll();
            }
        }

        // ── Scene GUI entry point ────────────────────────────────────────────

        public static void DrawSplineHandles(SplineContainer container)
        {
            if (container == null) return;

            var t = container.transform;

            DrawAllSplines(container, t);

            if (!editMode) return;

            _controlId = GUIUtility.GetControlID(FocusType.Passive);
            HandleInput(container, t);
            DrawKnots(container, t);
            DrawPreview();
            DrawHint();
        }

        // ── Spline curve drawing ─────────────────────────────────────────────

        static void DrawAllSplines(SplineContainer container, Transform t)
        {
            Handles.color = SplineColor;
            foreach (var spline in container.Splines)
                DrawCurve(spline, t);
        }

        static void DrawCurve(ISpline spline, Transform t)
        {
            const int steps = 80;
            Vector3 prev = t.TransformPoint((Vector3)spline.EvaluatePosition(0f));
            for (int i = 1; i <= steps; i++)
            {
                Vector3 pos = t.TransformPoint((Vector3)spline.EvaluatePosition((float)i / steps));
                Handles.DrawLine(prev, pos, 2.5f);
                prev = pos;
            }
        }

        // ── Input handling ───────────────────────────────────────────────────

        static void HandleInput(SplineContainer container, Transform t)
        {
            var e = Event.current;

            // Raycast mouse onto scene geometry for preview and placement
            _hasPreview = RaycastScene(e.mousePosition, out _previewPos);

            switch (e.type)
            {
                case EventType.MouseMove:
                    SceneView.RepaintAll();
                    break;

                case EventType.MouseDown when e.button == 0 && !e.alt:
                    if (_hasPreview)
                    {
                        if (_selectedKnot >= 0 && _selectedSpline >= 0)
                            ExtrudeKnot(container, _selectedSpline, _previewPos, t);
                        else
                            AppendKnot(container, _previewPos, t);

                        e.Use();
                        GUIUtility.hotControl = _controlId;
                    }
                    break;

                case EventType.MouseUp when e.button == 0:
                    if (GUIUtility.hotControl == _controlId)
                        GUIUtility.hotControl = 0;
                    break;

                case EventType.KeyDown when e.keyCode == KeyCode.Escape:
                    editMode = false;
                    _selectedKnot = -1;
                    SceneView.RepaintAll();
                    e.Use();
                    break;
            }
        }

        // ── Knot drawing & selection ─────────────────────────────────────────

        static void DrawKnots(SplineContainer container, Transform t)
        {
            for (int si = 0; si < container.Splines.Count; si++)
            {
                var spline = container.Splines[si] as Spline;
                if (spline == null) continue;

                for (int ki = 0; ki < spline.Count; ki++)
                {
                    Vector3 world = t.TransformPoint((Vector3)(float3)spline[ki].Position);
                    float size    = HandleUtility.GetHandleSize(world) * 0.1f;

                    bool isSelected = _selectedKnot == ki && _selectedSpline == si;
                    Handles.color   = isSelected ? SelectedColor : KnotColor;

                    // Click knot to select
                    if (Handles.Button(world, Quaternion.identity, size, size, Handles.SphereHandleCap))
                    {
                        _selectedKnot   = ki;
                        _selectedSpline = si;
                    }

                    // Move selected knot
                    if (isSelected)
                    {
                        EditorGUI.BeginChangeCheck();
                        Vector3 moved = Handles.PositionHandle(world, t.rotation);
                        if (EditorGUI.EndChangeCheck())
                            MoveKnot(container, spline, ki, moved, t);
                    }
                }
            }
        }

        // ── Knot operations ──────────────────────────────────────────────────

        static void AppendKnot(SplineContainer container, Vector3 worldPos, Transform t)
        {
            if (container.Splines.Count == 0)
                container.AddSpline();

            int si = Mathf.Clamp(_selectedSpline, 0, container.Splines.Count - 1);
            var spline = container.Splines[si] as Spline;
            if (spline == null) return;

            Undo.RecordObject(container, "Add Spline Knot");
            var local = (float3)t.InverseTransformPoint(worldPos);
            spline.Add(new BezierKnot(local), TangentMode.AutoSmooth);

            _selectedKnot   = spline.Count - 1;
            _selectedSpline = si;
            EditorUtility.SetDirty(container);
        }

        static void ExtrudeKnot(SplineContainer container, int splineIndex, Vector3 worldPos, Transform t)
        {
            var spline = container.Splines[splineIndex] as Spline;
            if (spline == null) return;

            Undo.RecordObject(container, "Extrude Spline Knot");
            var local = (float3)t.InverseTransformPoint(worldPos);
            spline.Add(new BezierKnot(local), TangentMode.AutoSmooth);

            _selectedKnot = spline.Count - 1;
            EditorUtility.SetDirty(container);
        }

        static void MoveKnot(SplineContainer container, Spline spline, int ki, Vector3 worldPos, Transform t)
        {
            Undo.RecordObject(container, "Move Spline Knot");
            var k = spline[ki];
            k.Position = (float3)t.InverseTransformPoint(worldPos);
            spline.SetKnot(ki, k);
            EditorUtility.SetDirty(container);
        }

        // ── Preview & hint ───────────────────────────────────────────────────

        static void DrawPreview()
        {
            if (!_hasPreview) return;
            Handles.color = PreviewColor;
            float size = HandleUtility.GetHandleSize(_previewPos) * 0.1f;
            Handles.SphereHandleCap(0, _previewPos, Quaternion.identity, size, EventType.Repaint);
        }

        static void DrawHint()
        {
            Handles.BeginGUI();
            GUI.Label(new Rect(10, Screen.height - 90, 340, 20),
                "LMB: place/extrude knot   |   Click knot to select & move   |   Esc: exit",
                EditorStyles.boldLabel);
            Handles.EndGUI();
        }

        // ── Raycast ──────────────────────────────────────────────────────────

        static bool RaycastScene(Vector2 mousePos, out Vector3 hitPoint)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mousePos);
            if (Physics.Raycast(ray, out var hit))
            {
                hitPoint = hit.point;
                return true;
            }
            // Fallback: intersect with Y=0 plane
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (plane.Raycast(ray, out float d))
            {
                hitPoint = ray.GetPoint(d);
                return true;
            }
            hitPoint = Vector3.zero;
            return false;
        }
    }
}
