using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    public static class MonkeyInstaller
    {
        public const string PrefabPath = "Assets/_Game/Prefabs/Hazards/Monkey.prefab";
        [MenuItem("Delivery Rider/Apply Monkey Hazard")]
        public static void Apply() => Debug.Log(ApplyToScene(GameScenes.RequireActive()));

        public static string ApplyToScene(Scene scene)
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Edit mode after compilation required.");
            var planner = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<HazardPlanner>(true)).Single();
            var data = new SerializedObject(planner);
            var settings = (HazardSpawnSettings)data.FindProperty("settings").objectReferenceValue;
            var candidates = (HazardCandidates)data.FindProperty("candidates").objectReferenceValue;
            var places = candidates.Candidates.Where(c => c.type != "monkey").ToList();
            AppendCandidates(places);
            if (!places.Any(c => c.type == "monkey")) throw new InvalidOperationException("No sidewalk candidates for monkeys.");
            candidates.Configure(candidates.Spots.ToArray(), places.ToArray());
            EditorUtility.SetDirty(candidates);
            var kinds = settings.Kinds.ToList();
            var kind = kinds.FirstOrDefault(k => k.type == "monkey");
            if (kind == null)
            {
                kind = new HazardSpawnSettings.Kind { type = "monkey", role = HazardRole.Object, unlockDay = 1, countPerDay = new[] { 3 } };
                kinds.Add(kind);
            }
            kind.prefab = CreatePrefab();
            settings.Configure(kinds.ToArray()); EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(candidates); AssetDatabase.SaveAssetIfDirty(settings);
            return $"{scene.name}: monkey {places.Count(c => c.type == "monkey")} candidates, target {kind.Count(1)}. No Play Mode.";
        }

        public static void AppendCandidates(List<HazardCandidate> places)
        {
            // Hydrant candidates already provide static-obstacle clearance on sidewalks.
            foreach (var source in places.Where(c => c.type == "hydrant").ToArray())
            { var copy = source; copy.type = "monkey"; places.Add(copy); }
        }

        public static GameObject CreatePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;
            var brown = Material("MonkeyBrown", new Color(0.32f, 0.16f, 0.07f));
            var tan = Material("MonkeyTan", new Color(0.8f, 0.58f, 0.34f));
            var dark = Material("MonkeyDark", new Color(0.045f, 0.025f, 0.015f));
            var root = new GameObject("Monkey"); root.SetActive(false);
            try
            {
                var model = new GameObject("Model").transform; model.SetParent(root.transform, false);
                Part(model, "Body", new Vector3(0, 0.5f, 0), new Vector3(0.65f, 0.8f, 0.5f), brown);
                Part(model, "Belly", new Vector3(0, 0.49f, 0.22f), new Vector3(0.43f, 0.53f, 0.12f), tan);
                Part(model, "Head", new Vector3(0, 1.03f, 0.02f), Vector3.one * 0.61f, brown);
                Part(model, "Face", new Vector3(0, 1.01f, 0.25f), new Vector3(0.47f, 0.4f, 0.18f), tan);
                Part(model, "Muzzle", new Vector3(0, 0.92f, 0.34f), new Vector3(0.33f, 0.18f, 0.17f), tan);
                Part(model, "Nose", new Vector3(0, 0.99f, 0.4f), new Vector3(0.1f, 0.06f, 0.06f), dark);
                Transform left = null, right = null;
                for (int side = -1; side <= 1; side += 2)
                {
                    Part(model, side < 0 ? "LeftEar" : "RightEar", new Vector3(side * 0.34f, 1.03f, 0), new Vector3(0.25f, 0.29f, 0.17f), brown);
                    Part(model, "InnerEar", new Vector3(side * 0.36f, 1.03f, 0.08f), new Vector3(0.14f, 0.18f, 0.06f), tan);
                    Part(model, "Eye", new Vector3(side * 0.115f, 1.07f, 0.34f), Vector3.one * 0.065f, dark);
                    Part(model, "BentLeg", new Vector3(side * 0.26f, 0.2f, 0.1f), new Vector3(0.3f, 0.35f, 0.4f), brown);
                    Part(model, "Foot", new Vector3(side * 0.26f, 0.09f, 0.32f), new Vector3(0.25f, 0.17f, 0.35f), tan);
                    var arm = new GameObject(side < 0 ? "LeftArm" : "RightArm").transform;
                    arm.SetParent(model, false); arm.localPosition = new Vector3(side * 0.33f, 0.75f, 0);
                    Part(arm, "Arm", new Vector3(0, -0.28f, 0), new Vector3(0.18f, 0.65f, 0.18f), brown, PrimitiveType.Capsule);
                    Part(arm, "Hand", new Vector3(0, -0.57f, 0), Vector3.one * 0.23f, tan);
                    if (side < 0) left = arm; else right = arm;
                }
                for (int i = 0; i < 5; i++)
                {
                    float a = i * 0.5f;
                    Part(model, $"Tail_{i:00}", new Vector3(0, 0.22f + (1f - Mathf.Cos(a)) * 0.3f, -0.3f - Mathf.Sin(a) * 0.5f), Vector3.one * 0.17f, brown);
                }
                var hazard = root.AddComponent<MonkeyHazard>(); var data = new SerializedObject(hazard);
                data.FindProperty("model").objectReferenceValue = model;
                data.FindProperty("leftArm").objectReferenceValue = left;
                data.FindProperty("rightArm").objectReferenceValue = right;
                data.ApplyModifiedPropertiesWithoutUndo();
                root.SetActive(true);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssetIfDirty(brown); AssetDatabase.SaveAssetIfDirty(tan); AssetDatabase.SaveAssetIfDirty(dark);
                return prefab;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void Part(Transform parent, string name, Vector3 position, Vector3 scale, Material material, PrimitiveType shape = PrimitiveType.Sphere)
        {
            var obj = GameObject.CreatePrimitive(shape); obj.name = name; obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position; obj.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>()); obj.GetComponent<Renderer>().sharedMaterial = material;
        }
        private static Material Material(string name, Color color)
        {
            string path = "Assets/_Game/Art/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var source = AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Game/Art/Materials" })
                .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(m => m.shader.name.Contains("Curved"));
            material = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = name; material.color = color; AssetDatabase.CreateAsset(material, path); return material;
        }
    }
}
