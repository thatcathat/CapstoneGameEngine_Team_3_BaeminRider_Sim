using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeliveryRider.Editor
{
    public static class RiderReactionInstaller
    {
        [MenuItem("Delivery Rider/Apply Rider Reactions")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Use edit mode.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/_Game/Scenes/FourDistrictPrototype.unity") throw new InvalidOperationException("Open FourDistrictPrototype.");
            var motor = UnityEngine.Object.FindFirstObjectByType<RiderMotor>();
            var network = UnityEngine.Object.FindFirstObjectByType<RoadNetwork>();
            if (motor == null || network == null) throw new InvalidOperationException("Rider and roads are required.");
            var root = motor.transform;
            var visual = root.Find("VisualRoot");
            if (visual == null)
            {
                var mesh = root.GetComponent<MeshFilter>();
                var renderer = root.GetComponent<MeshRenderer>();
                if (mesh == null || renderer == null) throw new InvalidOperationException("Bind the existing model visual explicitly.");
                var go = new GameObject("VisualRoot", typeof(MeshFilter), typeof(MeshRenderer));
                Undo.RegisterCreatedObjectUndo(go, "Add capsule visual");
                go.layer = root.gameObject.layer;
                visual = go.transform;
                visual.SetParent(root, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                var copy = go.GetComponent<MeshRenderer>();
                EditorUtility.CopySerialized(renderer, copy);
                copy.enabled = true;
                Undo.RecordObject(renderer, "Hide root visual");
                renderer.enabled = false;
                var session = UnityEngine.Object.FindFirstObjectByType<DeliverySession>();
                if (session != null)
                {
                    var bag = new SerializedObject(session).FindProperty("foodBag").objectReferenceValue as GameObject;
                    if (bag != null && bag.transform.parent == root) Undo.SetTransformParent(bag.transform, visual, "Attach food bag to visual");
                }
            }
            // Existing decoration meshes have no gameplay components or colliders.
            foreach (var name in new[] { "Head", "Face" })
            {
                var decoration = root.Find(name);
                if (decoration != null && decoration.GetComponent<MeshRenderer>() != null
                    && decoration.GetComponent<Collider>() == null && decoration.GetComponent<Rigidbody>() == null)
                    Undo.SetTransformParent(decoration, visual, "Attach capsule decoration to visual");
            }
            var presenter = root.GetComponent<RiderReactionVisual>();
            if (presenter == null) presenter = Undo.AddComponent<RiderReactionVisual>(root.gameObject);
            var visualData = new SerializedObject(presenter);
            visualData.FindProperty("motor").objectReferenceValue = motor;
            visualData.FindProperty("visualRoot").objectReferenceValue = visual;
            visualData.ApplyModifiedProperties();
            var recovery = root.GetComponent<RiderSafeRecovery>();
            if (recovery == null) recovery = Undo.AddComponent<RiderSafeRecovery>(root.gameObject);
            var recoveryData = new SerializedObject(recovery);
            recoveryData.FindProperty("network").objectReferenceValue = network;
            recoveryData.ApplyModifiedProperties();
            EditorUtility.SetDirty(motor);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save scene.");
        }
    }
}
