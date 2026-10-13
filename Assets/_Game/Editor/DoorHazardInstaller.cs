using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    public static class DoorHazardInstaller
    {
        [MenuItem("Delivery Rider/Install Door Hazard Pilot")]
        public static void Install()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Stop Play Mode and finish compilation first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene changes first.");
            var scene = EditorSceneManager.OpenScene(TrafficPilotInstaller.ScenePath);
            if (scene.GetRootGameObjects().Any(g => g.name == "DoorHazardPilot"))
            {
                Debug.Log("Door pilot already exists; authored changes preserved.");
                return;
            }
            var session = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<DeliverySession>(true)).Single();
            var root = new GameObject("DoorHazardPilot");
            root.transform.position = new Vector3(-8, 0.03f, -29);
            var hazard = root.AddComponent<SwingDoorHazard>();
            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(root.transform, false);
            var panel = hinge.gameObject.AddComponent<BoxCollider>();
            panel.center = new Vector3(0, 1.2f, 1.2f);
            panel.size = new Vector3(0.14f, 2.4f, 2.4f);
            panel.isTrigger = true;
            var wood = Material("DoorWood", new Color(0.22f, 0.48f, 0.53f));
            var frame = Material("DoorFrame", new Color(0.16f, 0.2f, 0.24f));
            Box("DoorPanel", panel.center, panel.size, wood, hinge, false);
            Box("Handle", new Vector3(0.12f, 1.1f, 2.1f), new Vector3(0.12f, 0.25f, 0.12f), frame, hinge, false);
            Box("Backing", new Vector3(-1.65f, 1.5f, 1.2f), new Vector3(3, 3, 3.1f), Material("DoorWall", new Color(0.75f, 0.68f, 0.54f)), root.transform, true);
            Box("FrameLeft", new Vector3(-0.1f, 1.25f, -0.12f), new Vector3(0.2f, 2.5f, 0.2f), frame, root.transform, true);
            Box("FrameRight", new Vector3(-0.1f, 1.25f, 2.52f), new Vector3(0.2f, 2.5f, 0.2f), frame, root.transform, true);
            Box("FrameTop", new Vector3(-0.1f, 2.55f, 1.2f), new Vector3(0.2f, 0.2f, 2.8f), frame, root.transform, true);
            var lamp = Box("WarningLamp", new Vector3(0.08f, 2.8f, 1.2f), new Vector3(0.18f, 0.22f, 0.55f), Material("DoorWarning", new Color(1, 0.25f, 0.03f)), root.transform, false).GetComponent<Renderer>();
            lamp.enabled = false;
            var speaker = root.AddComponent<AudioSource>();
            speaker.playOnAwake = false;
            speaker.spatialBlend = 1;
            speaker.minDistance = 2; speaker.maxDistance = 14;
            var so = new SerializedObject(hazard);
            so.FindProperty("session").objectReferenceValue = session;
            so.FindProperty("hinge").objectReferenceValue = hinge;
            so.FindProperty("panel").objectReferenceValue = panel;
            so.FindProperty("warningLamp").objectReferenceValue = lamp;
            so.FindProperty("speaker").objectReferenceValue = speaker;
            // Repeatable pilot tuning; the reusable component defaults to HTML's next-order policy.
            so.FindProperty("rearmPolicy").enumValueIndex = (int)DoorRearmPolicy.AfterExitDelay;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.Frame(new Bounds(root.transform.position + Vector3.up, new Vector3(14, 8, 16)), false);
            Debug.Log("Door hazard saved near the south starting sidewalk (-8, -29). Play Mode was not started.");
        }

        private static Material Material(string name, Color color)
        {
            string path = "Assets/_Game/Art/TrafficPilot/" + name + ".mat";
            var result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result != null) return result;
            result = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            result.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(result, path);
            return result;
        }

        private static GameObject Box(string name, Vector3 position, Vector3 size, Material material, Transform parent, bool solid)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = size;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            if (!solid) UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
            return obj;
        }
    }
}
