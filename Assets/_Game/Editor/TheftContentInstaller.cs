using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    public static class TheftContentInstaller
    {
        [MenuItem("Delivery Rider/Install Theft And Timed Incidents")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene changes first.");
            string previous = SceneManager.GetActiveScene().path;
            var scene = EditorSceneManager.OpenScene(PrototypeSceneBuilder.GamePath);
            ApplyToScene(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(previous) && previous != scene.path) EditorSceneManager.OpenScene(previous);
        }

        public static void ApplyToScene(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            var director = roots.SelectMany(r => r.GetComponentsInChildren<IncidentDirector>(true)).Single();
            var hud = roots.SelectMany(r => r.GetComponentsInChildren<GameHud>(true)).Single();
            var map = hud.GetComponent<DeliveryMapView>();
            var prefab = CreateThief();
            var chase = Definition("TheftChase", prefab, TheftRecoveryMode.ChaseThenReplacement);
            var replacement = Definition("TheftReplacement", prefab, TheftRecoveryMode.ReplacementOnly);
            var west = Source(director, "WestTheftZone", new Vector3(-14, 1.5f, 4), chase, true, true,
                new Vector3(-11, 1, 4), new[] { new Vector3(0, 1, 4), new Vector3(0, 1, 23) });
            Source(director, "NorthReplacementZone", new Vector3(0, 1.5f, 17), replacement, true, false,
                new Vector3(0, 1, 20), Array.Empty<Vector3>());
            var south = Source(director, "SouthTimedTheft", new Vector3(0, 1.5f, -15), chase, false, true,
                new Vector3(0, 1, -11), new[] { new Vector3(0, 1, 4), new Vector3(23, 1, 4) });
            var scheduler = director.GetComponent<TimedIncidentScheduler>();
            if (scheduler == null)
            {
                scheduler = director.gameObject.AddComponent<TimedIncidentScheduler>();
                Ref(scheduler, "director", director);
                var data = new SerializedObject(scheduler);
                var sources = data.FindProperty("sources"); sources.arraySize = 2;
                sources.GetArrayElementAtIndex(0).objectReferenceValue = west;
                sources.GetArrayElementAtIndex(1).objectReferenceValue = south;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            var font = hud.GetComponentsInChildren<TMP_Text>(true).First().font;
            OrderContentInstaller.AddGlyphs(font);
            var mapData = new SerializedObject(map);
            if (mapData.FindProperty("thiefMarker").objectReferenceValue == null)
            {
                var customer = (RectTransform)mapData.FindProperty("activeCustomerMarker").objectReferenceValue;
                var marker = UnityEngine.Object.Instantiate(customer, customer.parent);
                marker.name = "ThiefMarker";
                marker.GetComponent<UnityEngine.UI.Image>().color = new Color32(190, 98, 239, 255);
                foreach (var text in marker.GetComponentsInChildren<TMP_Text>(true)) text.text = "도둑";
                foreach (var graphic in marker.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
                marker.gameObject.SetActive(false);
                Ref(map, "thiefMarker", marker);
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static FoodThief CreateThief()
        {
            const string path = "Assets/_Game/Prefabs/FoodThief.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<FoodThief>(path);
            if (existing != null) return existing;
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Prefabs")) AssetDatabase.CreateFolder("Assets/_Game", "Prefabs");
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                go.name = "FoodThief";
                go.GetComponent<Renderer>().sharedMaterial = Material("ThiefPurple", new Color32(151, 64, 207, 255));
                var body = go.AddComponent<Rigidbody>();
                body.constraints = RigidbodyConstraints.FreezeRotation;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                go.AddComponent<FoodThief>();
                var bag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bag.name = "StolenFood"; bag.transform.SetParent(go.transform, false);
                bag.transform.localPosition = new Vector3(0, 0, -0.5f);
                bag.transform.localScale = new Vector3(0.65f, 0.65f, 0.4f);
                UnityEngine.Object.DestroyImmediate(bag.GetComponent<Collider>());
                bag.GetComponent<Renderer>().sharedMaterial = Material("StolenFoodGold", new Color32(255, 191, 64, 255));
                return PrefabUtility.SaveAsPrefabAsset(go, path).GetComponent<FoodThief>();
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static TheftIncidentDefinition Definition(string name, FoodThief prefab, TheftRecoveryMode mode)
        {
            string path = "Assets/_Game/Settings/Incidents/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<TheftIncidentDefinition>(path);
            if (existing != null) return existing;
            var definition = ScriptableObject.CreateInstance<TheftIncidentDefinition>();
            var data = new SerializedObject(definition);
            data.FindProperty("recoveryMode").enumValueIndex = (int)mode;
            data.FindProperty("thiefPrefab").objectReferenceValue = prefab;
            data.FindProperty("requiresFood").boolValue = true;
            data.FindProperty("kindCooldownSeconds").floatValue = 35f;
            data.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(definition, path);
            return definition;
        }

        private static IncidentTrigger Source(IncidentDirector director, string name, Vector3 position,
            IncidentDefinition definition, bool zone, bool timed, Vector3 spawn, Vector3[] route)
        {
            var found = director.transform.Find(name);
            if (found != null) return found.GetComponent<IncidentTrigger>();
            var go = new GameObject(name);
            go.transform.SetParent(director.transform, false); go.transform.position = position;
            var box = go.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(4, 3, 4);
            var trigger = go.AddComponent<IncidentTrigger>();
            Ref(trigger, "director", director);
            var spawnPoint = new GameObject("ThiefSpawn").transform;
            spawnPoint.SetParent(go.transform, false); spawnPoint.position = spawn;
            var data = new SerializedObject(trigger);
            data.FindProperty("allowZone").boolValue = zone;
            data.FindProperty("allowTimed").boolValue = timed;
            data.FindProperty("thiefSpawn").objectReferenceValue = spawnPoint;
            var entries = data.FindProperty("events"); entries.arraySize = 1;
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("definition").objectReferenceValue = definition;
            entries.GetArrayElementAtIndex(0).FindPropertyRelative("weight").floatValue = 1f;
            var points = data.FindProperty("escapeRoute"); points.arraySize = route.Length;
            for (int i = 0; i < route.Length; i++)
            {
                var point = new GameObject("EscapePoint" + (i + 1)).transform;
                point.SetParent(go.transform, false); point.position = route[i];
                points.GetArrayElementAtIndex(i).objectReferenceValue = point;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "TheftGroundMarker"; marker.transform.SetParent(go.transform, false);
            marker.transform.localPosition = new Vector3(0, -1.43f, 0);
            marker.transform.localScale = new Vector3(4, 0.025f, 4);
            UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.GetComponent<Renderer>().sharedMaterial = Material(timed && !zone ? "TimedTheftBlue" : "TheftZonePurple",
                timed && !zone ? new Color32(70, 120, 225, 255) : new Color32(175, 80, 220, 255));
            return trigger;
        }
        private static Material Material(string name, Color color)
        {
            string path = "Assets/_Game/Settings/Incidents/" + name + ".asset";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        private static void Ref(UnityEngine.Object owner, string name, UnityEngine.Object value)
        {
            var data = new SerializedObject(owner); data.FindProperty(name).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
