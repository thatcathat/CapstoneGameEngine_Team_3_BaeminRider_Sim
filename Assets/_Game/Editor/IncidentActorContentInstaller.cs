using System;
using UnityEditor;
using UnityEngine;

namespace DeliveryRider.Editor
{
    public static class IncidentActorContentInstaller
    {
        [MenuItem("Delivery Rider/Install Incident Actor Prefabs")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            foreach (string filter in new[] { "t:CrashIncidentDefinition", "t:TheftIncidentDefinition" })
            {
                foreach (string guid in AssetDatabase.FindAssets(filter, new[] { "Assets/_Game/Settings/Incidents" }))
                {
                    var definition = AssetDatabase.LoadAssetAtPath<IncidentDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                    Configure(definition);
                    AssetDatabase.SaveAssetIfDirty(definition);
                }
            }
        }

        public static void Configure(IncidentDefinition definition)
        {
            if (definition.ActorPrefab != null) return;
            IncidentActor prefab;
            if (definition is CrashIncidentDefinition) prefab = CreatePrefab<CrashIncidentActor>("CrashIncident");
            else if (definition is TheftIncidentDefinition) prefab = CreatePrefab<TheftIncidentActor>("TheftIncident");
            else throw new InvalidOperationException("Unsupported incident definition: " + definition.name);

            var data = new SerializedObject(definition);
            data.FindProperty("actorPrefab").objectReferenceValue = prefab;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T CreatePrefab<T>(string name) where T : IncidentActor
        {
            const string folder = "Assets/_Game/Prefabs";
            string path = folder + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new InvalidOperationException("Prefab is missing the expected actor component: " + path);
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/_Game", "Prefabs");

            var go = new GameObject(name);
            try
            {
                go.AddComponent<T>();
                return PrefabUtility.SaveAsPrefabAsset(go, path).GetComponent<T>();
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
