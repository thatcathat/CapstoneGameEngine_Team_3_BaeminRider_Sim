using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DeliveryRider.Editor
{
    // Day-clock sundial: one Image drawn by the UI Sundial shader (SundialView) in the left part of the day-clock
    // panel, replacing the earlier filled half discs and sun dot. Safe to run repeatedly.
    public static class SundialInstaller
    {
        private const string ShaderName = "DeliveryRider/UI Sundial";
        private const string MaterialPath = "Assets/_Game/Art/Materials/UISundial.mat";
        // The dial owns the left third of the day-clock panel; the clock texts start after it.
        private const float DialEnd = 0.34f, TextStart = 0.36f;

        [MenuItem("Delivery Rider/Apply Sundial")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Use edit mode after compilation.");
            var dayHud = UnityEngine.Object.FindFirstObjectByType<DeliveryDayHud>(FindObjectsInactive.Include);
            if (dayHud == null) throw new InvalidOperationException("Open a scene with DeliveryDayHud.");
            Build(dayHud);
            var scene = dayHud.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save scene.");
            Debug.Log("Sundial applied: UI Sundial shader on the day-clock panel. Play Mode not started.");
        }

        public static void Build(DeliveryDayHud dayHud)
        {
            var hudData = new SerializedObject(dayHud);
            var dayClock = (TMP_Text)hudData.FindProperty("dayClock").objectReferenceValue;
            var panel = (RectTransform)dayClock.transform.parent;

            foreach (RectTransform child in panel)
                if (child.name != "Sundial" && child.anchorMin.x < TextStart)
                {
                    Undo.RecordObject(child, "Move clock text");
                    child.anchorMin = new Vector2(TextStart, child.anchorMin.y);
                }

            var dial = panel.Find("Sundial") as RectTransform;
            if (dial == null)
            {
                dial = new GameObject("Sundial", typeof(RectTransform)).GetComponent<RectTransform>();
                Undo.RegisterCreatedObjectUndo(dial.gameObject, "Add sundial");
                dial.gameObject.layer = panel.gameObject.layer;
                dial.SetParent(panel, false);
            }
            Undo.RecordObject(dial, "Place sundial");
            dial.anchorMin = new Vector2(0.01f, 0.02f);
            dial.anchorMax = new Vector2(DialEnd, 0.98f);
            dial.offsetMin = dial.offsetMax = Vector2.zero;
            // The old dial pieces (filled half discs and the sun dot).
            foreach (var old in new[] { "Track", "Fill", "Sun" })
            {
                var part = dial.Find(old);
                if (part != null) Undo.DestroyObjectImmediate(part.gameObject);
            }

            var image = dial.GetComponent<Image>();
            if (image == null) image = Undo.AddComponent<Image>(dial.gameObject);
            Undo.RecordObject(image, "Draw sundial");
            image.sprite = null;
            image.color = Color.white;
            image.raycastTarget = false;
            image.material = LoadOrCreateMaterial();
            var view = dial.GetComponent<SundialView>();
            if (view == null) view = Undo.AddComponent<SundialView>(dial.gameObject);

            hudData.FindProperty("sundial").objectReferenceValue = view;
            hudData.ApplyModifiedProperties();
        }

        private static Material LoadOrCreateMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException(ShaderName + " is missing.");
            material = new Material(shader) { name = "UISundial" };
            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }
    }
}
