using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace SplineCraft.Editor
{
    public static class SplineCraftMenu
    {
        const string kRoot = "Tools/SplineCraft/";

        // ── Spline Path ──────────────────────────────────────────────────────

        [MenuItem(kRoot + "Create Spline Path", priority = 0)]
        static void CreateSplinePath()
        {
            var go = new GameObject("SplinePath");
            go.AddComponent<SplineContainer>();
            PlaceAtSceneCenter(go);
            RegisterUndo(go, "Create Spline Path");
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
        }

        // ── Mesh Deformer ────────────────────────────────────────────────────

        [MenuItem(kRoot + "Add Mesh Deformer", priority = 11)]
        static void AddMeshDeformer()
        {
            var splineContainer = FindOrPromptSplineContainer();
            if (splineContainer == null) return;

            var go = new GameObject("SplineMeshDeformer");
            var deformer = go.AddComponent<SplineMeshDeformer>();
            deformer.splineContainer = splineContainer;
            PlaceAtSceneCenter(go);
            RegisterUndo(go, "Add Mesh Deformer");
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);

            Debug.Log("[SplineCraft] MeshDeformer created. Assign a Source Mesh in the inspector, then click Rebuild Mesh.");
        }

        [MenuItem(kRoot + "Add Mesh Deformer", validate = true)]
        static bool ValidateAddMeshDeformer() => HasSplineContainerInScene();

        // ── Instancer ────────────────────────────────────────────────────────

        [MenuItem(kRoot + "Add Instancer", priority = 12)]
        static void AddInstancer()
        {
            var splineContainer = FindOrPromptSplineContainer();
            if (splineContainer == null) return;

            var go = new GameObject("SplineInstancer");
            var instancer = go.AddComponent<SplineInstancer>();
            instancer.splineContainer = splineContainer;
            PlaceAtSceneCenter(go);
            RegisterUndo(go, "Add Instancer");
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);

            Debug.Log("[SplineCraft] Instancer created. Assign an Item Prefab and set Spacing in the inspector.");
        }

        [MenuItem(kRoot + "Add Instancer", validate = true)]
        static bool ValidateAddInstancer() => HasSplineContainerInScene();

        // ── Quick Setup (full scene scaffold) ────────────────────────────────

        [MenuItem(kRoot + "Quick Setup — Deformer Scene", priority = 30)]
        static void QuickSetupDeformer()
        {
            var root = new GameObject("SplineCraft_Deformer");
            Undo.RegisterCreatedObjectUndo(root, "Quick Setup Deformer");

            var pathGo = new GameObject("SplinePath");
            pathGo.transform.SetParent(root.transform);
            var container = pathGo.AddComponent<SplineContainer>();

            var deformerGo = new GameObject("MeshDeformer");
            deformerGo.transform.SetParent(root.transform);
            var deformer = deformerGo.AddComponent<SplineMeshDeformer>();
            deformer.splineContainer = container;

            PlaceAtSceneCenter(root);
            Selection.activeGameObject = deformerGo;
            EditorGUIUtility.PingObject(deformerGo);

            Debug.Log("[SplineCraft] Quick setup done. Select MeshDeformer, assign a Source Mesh, then Rebuild Mesh.");
        }

        [MenuItem(kRoot + "Quick Setup — Instancer Scene", priority = 31)]
        static void QuickSetupInstancer()
        {
            var root = new GameObject("SplineCraft_Instancer");
            Undo.RegisterCreatedObjectUndo(root, "Quick Setup Instancer");

            var pathGo = new GameObject("SplinePath");
            pathGo.transform.SetParent(root.transform);
            var container = pathGo.AddComponent<SplineContainer>();

            var instancerGo = new GameObject("Instancer");
            instancerGo.transform.SetParent(root.transform);
            var instancer = instancerGo.AddComponent<SplineInstancer>();
            instancer.splineContainer = container;

            PlaceAtSceneCenter(root);
            Selection.activeGameObject = instancerGo;
            EditorGUIUtility.PingObject(instancerGo);

            Debug.Log("[SplineCraft] Quick setup done. Select Instancer, assign an Item Prefab, set Spacing, then Rebuild.");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        static SplineContainer FindOrPromptSplineContainer()
        {
            // If a SplineContainer is selected, use it
            if (Selection.activeGameObject != null)
            {
                var sc = Selection.activeGameObject.GetComponent<SplineContainer>();
                if (sc != null) return sc;
            }

            // Otherwise find the first one in the scene
            var found = Object.FindFirstObjectByType<SplineContainer>();
            if (found != null) return found;

            EditorUtility.DisplayDialog(
                "No SplineContainer found",
                "Create a Spline Path first via Tools → SplineCraft → Create Spline Path.",
                "OK");
            return null;
        }

        static bool HasSplineContainerInScene() =>
            Object.FindFirstObjectByType<SplineContainer>() != null;

        static void PlaceAtSceneCenter(GameObject go)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv != null)
                go.transform.position = sv.camera.transform.position + sv.camera.transform.forward * 5f;
        }

        static void RegisterUndo(GameObject go, string name) =>
            Undo.RegisterCreatedObjectUndo(go, name);
    }
}
