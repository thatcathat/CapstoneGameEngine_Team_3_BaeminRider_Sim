using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DeliveryRider.Editor
{
    // M1 core loop: sundial day clock, two call slots and the 600 s test day. Safe to run repeatedly.
    public static class CoreLoopInstaller
    {
        private const string SettingsPath = "Assets/_Game/Settings/DaySettings.asset";
        private const float DayLengthSeconds = 600f;
        private const int CallSlots = 2;

        [MenuItem("Delivery Rider/Apply Core Loop (M1)")]
        public static void Apply()
        {
            var session = UnityEngine.Object.FindFirstObjectByType<DeliverySession>(FindObjectsInactive.Include);
            var dayHud = UnityEngine.Object.FindFirstObjectByType<DeliveryDayHud>(FindObjectsInactive.Include);
            if (session == null || dayHud == null) throw new InvalidOperationException("Open a scene with DeliverySession and DeliveryDayHud.");

            var sessionData = new SerializedObject(session);
            sessionData.FindProperty("candidateCount").intValue = CallSlots;
            sessionData.ApplyModifiedProperties();

            var settings = AssetDatabase.LoadAssetAtPath<DeliveryDaySettings>(SettingsPath);
            if (settings != null)
            {
                var settingsData = new SerializedObject(settings);
                settingsData.FindProperty("durationSeconds").floatValue = DayLengthSeconds;
                settingsData.ApplyModifiedProperties();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            InstallSundial(dayHud);
            EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
            EditorSceneManager.SaveScene(session.gameObject.scene);
        }

        private static void InstallSundial(DeliveryDayHud dayHud)
        {
            var hudData = new SerializedObject(dayHud);
            var dayClock = (TMP_Text)hudData.FindProperty("dayClock").objectReferenceValue;
            var panel = (RectTransform)dayClock.transform.parent;

            // Text moves right so the dial owns the left part of the clock panel.
            foreach (RectTransform child in panel)
                if (child.name != "Sundial" && child.anchorMin.x < 0.3f) child.anchorMin = new Vector2(0.32f, child.anchorMin.y);

            var dial = panel.Find("Sundial") as RectTransform ?? NewRect("Sundial", panel);
            dial.anchorMin = new Vector2(0.02f, 0.05f);
            dial.anchorMax = new Vector2(0.30f, 0.95f);
            dial.offsetMin = dial.offsetMax = Vector2.zero;

            var circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            var track = HalfDisc(dial, "Track", circle, new Color32(255, 255, 255, 40), 1f);
            var fill = HalfDisc(dial, "Fill", circle, new Color32(255, 214, 102, 255), 0f);
            fill.transform.SetSiblingIndex(track.transform.GetSiblingIndex() + 1);

            var sun = dial.Find("Sun") as RectTransform ?? NewRect("Sun", dial);
            sun.anchorMin = sun.anchorMax = new Vector2(0.5f, 0.3f);
            sun.pivot = Vector2.one * 0.5f;
            sun.sizeDelta = Vector2.one * 14f;
            if (!sun.TryGetComponent<Image>(out var sunImage)) sunImage = sun.gameObject.AddComponent<Image>();
            sunImage.sprite = circle;
            sunImage.color = new Color32(255, 240, 180, 255);
            sunImage.raycastTarget = false;
            sun.SetAsLastSibling();

            hudData.FindProperty("sundialFill").objectReferenceValue = fill;
            hudData.FindProperty("sundialSun").objectReferenceValue = sun;
            hudData.FindProperty("sundialRadius").floatValue = 30f;
            hudData.ApplyModifiedProperties();
        }

        // A disc anchored a bit below the dial centre; Radial180 from the bottom shows only its upper half.
        private static Image HalfDisc(RectTransform dial, string name, Sprite sprite, Color color, float amount)
        {
            var rect = dial.Find(name) as RectTransform ?? NewRect(name, dial);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.3f);
            rect.pivot = Vector2.one * 0.5f;
            rect.sizeDelta = Vector2.one * 60f;
            rect.anchoredPosition = Vector2.zero;
            if (!rect.TryGetComponent<Image>(out var image)) image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial180;
            image.fillOrigin = (int)Image.Origin180.Bottom;
            image.fillClockwise = true;
            image.fillAmount = amount;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }
    }
}
