using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    public static class TireHazardInstaller
    {
        private const string ArtPath = "Assets/_Game/Art/TrafficPilot/";
        [MenuItem("Delivery Rider/Install Tire Hazard Pilot")]
        public static void Install()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Finish compilation and stop Play Mode first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene changes first.");
            var scene = EditorSceneManager.OpenScene(TrafficPilotInstaller.ScenePath);
            if (scene.GetRootGameObjects().Any(g => g.name == "TireHazardPilot"))
            { Debug.Log("Tire pilot already exists; authored changes preserved."); return; }
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var session = all.Select(t => t.GetComponent<DeliverySession>()).Single(x => x != null);
            var door = all.Select(t => t.GetComponent<SwingDoorHazard>()).Single(x => x != null);
            var root = new GameObject("TireHazardPilot");
            var flat = new Vector3[13];
            for (int i = 0; i < flat.Length; i++) flat[i] = new Vector3(6.5f-i, 0.65f, -31);
            Create("DoorChainTire", flat, session, door, false, root.transform);
            var slope = new Vector3[23];
            for (int i = 0; i < slope.Length; i++)
            {
                float z = 40-i;
                slope[i] = new Vector3(-6.5f, TrafficPilotInstaller.Elevation(z)+0.65f, z);
            }
            Create("SlopeTire", slope, session, null, true, root.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save tire pilot scene.");
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            Debug.Log("Saved door-linked flat tire and independent northern slope tire. Play Mode not started.");
        }

        private static void Create(string name, Vector3[] points, DeliverySession session, SwingDoorHazard door, bool proximity, Transform parent)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.position = new Vector3(points[0].x, 0, points[0].z);
            var hazard = obj.AddComponent<RollingTireHazard>();
            var wheel = new GameObject("Wheel").transform;
            wheel.SetParent(obj.transform, false);
            wheel.position = points[0];
            wheel.rotation = Quaternion.LookRotation((points[1]-points[0]).normalized, Vector3.up);
            wheel.gameObject.AddComponent<MeshFilter>().sharedMesh = TireMesh();
            wheel.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Material("TireRubber", new Color(0.08f, 0.09f, 0.11f));
            var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stripe.name = "RotationMarker";
            stripe.transform.SetParent(wheel, false);
            stripe.transform.localPosition = new Vector3(0.17f, 0.435f, 0);
            stripe.transform.localScale = new Vector3(0.025f, 0.18f, 0.08f);
            UnityEngine.Object.DestroyImmediate(stripe.GetComponent<Collider>());
            stripe.GetComponent<Renderer>().sharedMaterial = Material("TireMarker", new Color(0.95f, 0.83f, 0.32f));
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lamp.name = "WarningLamp";
            lamp.transform.SetParent(obj.transform, false);
            lamp.transform.position = points[0] + Vector3.up;
            lamp.transform.localScale = new Vector3(0.25f, 0.3f, 0.25f);
            UnityEngine.Object.DestroyImmediate(lamp.GetComponent<Collider>());
            var renderer = lamp.GetComponent<Renderer>();
            renderer.sharedMaterial = Material("DoorWarning", new Color(1, 0.25f, 0.03f));
            renderer.enabled = false;
            var so = new SerializedObject(hazard);
            so.FindProperty("session").objectReferenceValue = session;
            so.FindProperty("linkedDoor").objectReferenceValue = door;
            so.FindProperty("wheel").objectReferenceValue = wheel;
            so.FindProperty("warningLamp").objectReferenceValue = renderer;
            so.FindProperty("proximityActivation").boolValue = proximity;
            so.FindProperty("rearmOnNextOrder").boolValue = false;
            so.FindProperty("approachCenter").vector3Value = new Vector3(0, points[0].y+0.4f, -5);
            so.FindProperty("approachSize").vector3Value = new Vector3(3, 4, 5);
            var path = so.FindProperty("localPath");
            path.arraySize = points.Length;
            for (int i = 0; i < points.Length; i++) path.GetArrayElementAtIndex(i).vector3Value = obj.transform.InverseTransformPoint(points[i]);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material Material(string name, Color color)
        {
            string path = ArtPath + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            mat.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static Mesh TireMesh()
        {
            string path = ArtPath + "HazardTire.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            const int rings = 20, sides = 8;
            var vertices = new Vector3[rings*sides];
            var triangles = new int[rings*sides*6];
            for (int r = 0; r < rings; r++) for (int s = 0; s < sides; s++)
            {
                float a = r*2*Mathf.PI/rings, b = s*2*Mathf.PI/sides;
                float radial = 0.435f+0.115f*Mathf.Cos(b);
                int index = r*sides+s;
                vertices[index] = new Vector3(0.17f*Mathf.Sin(b), radial*Mathf.Cos(a), radial*Mathf.Sin(a));
                int nextR = ((r+1)%rings)*sides+s, nextS = r*sides+(s+1)%sides, diagonal = ((r+1)%rings)*sides+(s+1)%sides;
                int t = index*6;
                triangles[t] = index; triangles[t+1] = nextR; triangles[t+2] = nextS;
                triangles[t+3] = nextS; triangles[t+4] = nextR; triangles[t+5] = diagonal;
            }
            var mesh = new Mesh { name = "HazardTire", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
