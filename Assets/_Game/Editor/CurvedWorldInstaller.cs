using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeliveryRider.Editor
{
    // Curved-world look: switches the project's URP Lit materials to DeliveryRider/Curved Lit (same properties and
    // keywords), puts CurvedWorld on the follow camera with the camera's target as pivot, and makes world-space text
    // follow the bend. Safe to run repeatedly. Installers that create Lit materials use LitShader so new ones bend too.
    public static class CurvedWorldInstaller
    {
        public const string LitShader = "DeliveryRider/Curved Lit";
        private const string UrpLitShader = "Universal Render Pipeline/Lit";

        [MenuItem("Delivery Rider/Apply Curved World")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Use edit mode after compilation.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/_Game/Scenes/FourDistrictPrototype.unity") throw new InvalidOperationException("Open FourDistrictPrototype.");
            var curved = Shader.Find(LitShader);
            if (curved == null) throw new InvalidOperationException(LitShader + " is missing.");
            var follow = UnityEngine.Object.FindFirstObjectByType<ThirdPersonCamera>();
            var pivot = follow != null ? new SerializedObject(follow).FindProperty("target").objectReferenceValue as Transform : null;
            if (pivot == null) throw new InvalidOperationException("The follow camera and its target are required.");
            int materials = SwitchMaterials(curved);

            var world = follow.GetComponent<CurvedWorld>();
            if (world == null) world = Undo.AddComponent<CurvedWorld>(follow.gameObject);
            var worldData = new SerializedObject(world);
            worldData.FindProperty("pivot").objectReferenceValue = pivot;
            worldData.ApplyModifiedProperties();

            // DestinationGuide places its whole beacon (ring and label) on the bend itself.
            var guide = UnityEngine.Object.FindFirstObjectByType<DestinationGuide>(FindObjectsInactive.Include);
            var beacon = guide != null ? new SerializedObject(guide).FindProperty("beacon").objectReferenceValue as Transform : null;
            bool InBeacon(Component c) => beacon != null && c.transform.IsChildOf(beacon);
            var objects = scene.GetRootGameObjects();
            int followers = 0;
            foreach (var text in objects.SelectMany(g => g.GetComponentsInChildren<TextMeshPro>(true)))
            {
                if (InBeacon(text) || text.GetComponent<CurvedWorldFollower>() != null) continue;
                Undo.AddComponent<CurvedWorldFollower>(text.gameObject);
                followers++;
            }
            // Anything else drawn without the curved shader would float above the bent world at a distance.
            var unbent = objects.SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
                .Where(r => !InBeacon(r) && r.GetComponentInParent<TextMeshPro>(true) == null)
                .SelectMany(r => r.sharedMaterials).Where(m => m != null && m.shader != curved)
                .Select(m => $"{m.name} ({m.shader.name})").Distinct().ToArray();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save scene.");
            Debug.Log($"Curved world applied: {materials} materials switched to {LitShader}, pivot {pivot.name}, {followers} text followers added; "
                + $"other scene materials not bending: {(unbent.Length == 0 ? "none" : string.Join(", ", unbent))}. Play Mode not started.");
        }

        private static int SwitchMaterials(Shader curved)
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Game" }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material == null || material.shader.name != UrpLitShader) continue;
                int queue = material.rawRenderQueue;
                Undo.RecordObject(material, "Use curved Lit");
                material.shader = curved;
                material.renderQueue = queue;
                EditorUtility.SetDirty(material);
                // Only the switched materials; SaveAssets would also write unrelated dirty assets (font glyph caches).
                AssetDatabase.SaveAssetIfDirty(material);
                count++;
            }
            return count;
        }
    }
}
