using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    public static class RollingMelonInstaller
    {
        private const string Art = "Assets/_Game/Art/FourDistrict/";

        [MenuItem("Delivery Rider/Upgrade Melon Slots")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Stop Play Mode and finish compilation first.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != DistrictLayout.ScenePath || scene.isDirty) throw new InvalidOperationException("Open the saved FourDistrictPrototype scene first.");
            var session = UnityEngine.Object.FindFirstObjectByType<DeliverySession>();
            var slots = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<HazardSlot>(true)).Where(s => s.Type == "melon").ToArray();
            int changed = 0;
            foreach (var slot in slots)
            {
                if (slot.GetComponent<RollingMelonHazard>() != null) continue;
                Build(slot.gameObject, session);
                changed++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save melon upgrade.");
            Debug.Log($"Rolling melon slots upgraded: {changed}/{slots.Length}. Play Mode not started.");
        }

        public static void Build(GameObject root, DeliverySession session)
        {
            if (root.GetComponent<RollingMelonHazard>() != null) return;
            if (session == null || session.Player == null) throw new InvalidOperationException("A player is required for melon sizing.");
            var player = session.Player.GetComponent<CapsuleCollider>();
            if (player == null) throw new InvalidOperationException("Player capsule is missing.");
            float diameter = player.height * Mathf.Abs(player.transform.lossyScale.y) * 0.3f;
            var positions = new Vector3[7];
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 world = root.transform.TransformPoint(new Vector3((i % 3 - 1) * diameter * 1.35f, 0f, 2.8f + i / 3 * diameter * 1.5f));
                var ground = Physics.RaycastAll(world + Vector3.up * 10f, Vector3.down, 25f, ~0, QueryTriggerInteraction.Ignore)
                    .Where(h => h.collider is MeshCollider && h.rigidbody == null).OrderBy(h => h.distance).ToArray();
                if (ground.Length == 0) throw new InvalidOperationException($"No ground at {root.name} melon {i}.");
                positions[i] = ground[0].point + Vector3.up * (diameter * 0.5f + 0.03f);
            }
            var mesh = MelonMesh();
            var materials = new[] { MakeMaterial("RollingMelonGreen", new Color(0.24f, 0.6f, 0.12f)), MakeMaterial("RollingMelonStripe", new Color(0.035f, 0.19f, 0.04f)) };
            var old = root.GetComponent<GroundContactHazard>();
            if (old != null)
            {
                UnityEngine.Object.DestroyImmediate(old);
                var volume = root.GetComponent<BoxCollider>();
                if (volume != null) UnityEngine.Object.DestroyImmediate(volume);
                foreach (Transform child in root.transform.Cast<Transform>().ToArray())
                    if (child.name == "Pulp" || child.name == "Rind" || child.name == "Warning" || child.name == "Burst") UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            var hazard = root.AddComponent<RollingMelonHazard>();
            var actors = new RollingMelon[positions.Length];
            for (int i = 0; i < actors.Length; i++)
            {
                var melon = new GameObject("RollingMelon_" + i);
                melon.transform.SetParent(root.transform, false);
                melon.transform.position = positions[i];
                melon.transform.localScale = Vector3.one * diameter;
                melon.AddComponent<MeshFilter>().sharedMesh = mesh;
                melon.AddComponent<MeshRenderer>().sharedMaterials = materials;
                melon.AddComponent<SphereCollider>().radius = 0.5f;
                var body = melon.AddComponent<Rigidbody>();
                body.mass = 8f;
                body.useGravity = true;
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.maxAngularVelocity = 60f;
                actors[i] = melon.AddComponent<RollingMelon>();
            }
            var data = new SerializedObject(hazard);
            data.FindProperty("session").objectReferenceValue = session;
            var array = data.FindProperty("melons");
            array.arraySize = actors.Length;
            for (int i = 0; i < actors.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = actors[i];
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material MakeMaterial(string name, Color color)
        {
            string path = Art + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
            material.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Mesh MelonMesh()
        {
            const string path = Art + "RollingMelon.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var mesh = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);
            UnityEngine.Object.DestroyImmediate(primitive);
            mesh.name = "StripedMelon";
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var green = new List<int>();
            var dark = new List<int>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var center = (vertices[triangles[i]] + vertices[triangles[i + 1]] + vertices[triangles[i + 2]]) / 3f;
                float angle = Mathf.Atan2(center.z, center.x) + Mathf.PI;
                var target = Mathf.Repeat(angle * 6f / (2f * Mathf.PI), 1f) < 0.3f ? dark : green;
                target.Add(triangles[i]); target.Add(triangles[i + 1]); target.Add(triangles[i + 2]);
            }
            mesh.subMeshCount = 2;
            mesh.SetTriangles(green, 0);
            mesh.SetTriangles(dark, 1);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
