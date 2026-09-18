using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    public static class IncidentContentInstaller
    {
        [MenuItem("Delivery Rider/Install Incident Zones")]
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
            var session = roots.SelectMany(r => r.GetComponentsInChildren<DeliverySession>(true)).Single();
            var rider = roots.SelectMany(r => r.GetComponentsInChildren<RiderMotor>(true)).Single();
            var hud = roots.SelectMany(r => r.GetComponentsInChildren<GameHud>(true)).Single();
            var root = roots.FirstOrDefault(r => r.name == "Incidents");
            if (root == null) { root = new GameObject("Incidents"); SceneManager.MoveGameObjectToScene(root, scene); }
            var director = root.GetComponent<IncidentDirector>();
            if (director == null) director = root.AddComponent<IncidentDirector>();
            Ref(director, "session", session); Ref(director, "rider", rider);
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Settings/Incidents"))
                AssetDatabase.CreateFolder("Assets/_Game/Settings", "Incidents");
            var mild = Definition("CrashMild", 6f, 3f, 1.5f);
            var strong = Definition("CrashStrong", 9f, 4f, 2.5f);
            Zone(root.transform, director, "SouthRoadCrash", new Vector3(0f, 1.5f, -3f), 90f, new[] { mild });
            Zone(root.transform, director, "EastRoadCrash", new Vector3(14f, 1.5f, 4f), 0f, new[] { mild, strong });

            var font = hud.GetComponentsInChildren<TMP_Text>(true).First().font;
            OrderContentInstaller.AddGlyphs(font);
            var existing = hud.transform.Find("IncidentNotice");
            var notice = existing != null ? existing.gameObject : new GameObject("IncidentNotice", typeof(RectTransform));
            if (existing == null) notice.transform.SetParent(hud.transform, false);
            var rect = (RectTransform)notice.transform;
            rect.anchorMin = new Vector2(0.20f, 0.73f); rect.anchorMax = new Vector2(0.80f, 0.82f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var label = notice.GetComponent<TextMeshProUGUI>();
            if (label == null) label = notice.AddComponent<TextMeshProUGUI>();
            label.font = font; label.fontSize = 24; label.fontStyle = FontStyles.Bold;
            label.color = new Color32(255, 205, 90, 255); label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false; label.text = "";
            var display = hud.GetComponent<IncidentHud>();
            if (display == null) display = hud.gameObject.AddComponent<IncidentHud>();
            Ref(display, "director", director); Ref(display, "session", session); Ref(display, "label", label);
            var pause = hud.transform.Find("PauseOverlay"); if (pause != null) pause.SetAsLastSibling();
            var settlement = hud.transform.Find("SettlementPanel"); if (settlement != null) settlement.SetAsLastSibling();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static CrashIncidentDefinition Definition(string name, float horizontal, float upward, float recovery)
        {
            string path = "Assets/_Game/Settings/Incidents/" + name + ".asset";
            var definition = AssetDatabase.LoadAssetAtPath<CrashIncidentDefinition>(path);
            if (definition != null) return definition;
            definition = ScriptableObject.CreateInstance<CrashIncidentDefinition>();
            var data = new SerializedObject(definition);
            data.FindProperty("horizontalImpulse").floatValue = horizontal;
            data.FindProperty("upwardImpulse").floatValue = upward;
            data.FindProperty("recoverySeconds").floatValue = recovery;
            data.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(definition, path);
            return definition;
        }
        private static void Zone(Transform parent, IncidentDirector director, string name, Vector3 position, float yaw, IncidentDefinition[] definitions)
        {
            // Preserve authored positions, lists and tuning on subsequent installation.
            if (parent.Find(name) != null) return;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false); go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var box = go.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(4f, 3f, 4f);
            var trigger = go.AddComponent<IncidentTrigger>();
            Ref(trigger, "director", director);
            var data = new SerializedObject(trigger);
            var entries = data.FindProperty("events"); entries.arraySize = definitions.Length;
            for (int i = 0; i < definitions.Length; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("definition").objectReferenceValue = definitions[i];
                entry.FindPropertyRelative("weight").floatValue = 1f;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            const string materialPath = "Assets/_Game/Settings/Incidents/HazardMarker.asset";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.SetColor("_BaseColor", new Color(1f, 0.38f, 0.08f));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "HazardGroundMarker"; marker.transform.SetParent(go.transform, false);
            marker.transform.localPosition = new Vector3(0f, -1.43f, 0f);
            marker.transform.localScale = new Vector3(4f, 0.025f, 4f);
            UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.GetComponent<Renderer>().sharedMaterial = material;
        }
        private static void Ref(UnityEngine.Object owner, string name, UnityEngine.Object value)
        {
            var data = new SerializedObject(owner);
            data.FindProperty(name).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
