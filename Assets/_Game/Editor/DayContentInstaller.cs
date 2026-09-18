using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    public static class DayContentInstaller
    {
        private static TMP_FontAsset font;
        private static readonly Color Navy = new Color32(19, 39, 48, 255);
        private static readonly Color Cream = new Color32(246, 244, 232, 255);
        private static readonly Color Teal = new Color32(40, 197, 177, 255);

        [MenuItem("Delivery Rider/Install Timers And Settlement")]
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
            Debug.Log("Day settings, timers and settlement saved. No Play Mode execution.");
        }

        public static void ApplyToScene(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            var session = roots.SelectMany(g => g.GetComponentsInChildren<DeliverySession>(true)).Single();
            var hud = roots.SelectMany(g => g.GetComponentsInChildren<GameHud>(true)).Single();
            font = hud.GetComponentsInChildren<TMP_Text>(true).First().font;
            OrderContentInstaller.AddGlyphs(font);
            const string settingsPath = "Assets/_Game/Settings/DaySettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<DeliveryDaySettings>(settingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<DeliveryDaySettings>();
                AssetDatabase.CreateAsset(settings, settingsPath);
            }
            var sessionData = new SerializedObject(session);
            if (sessionData.FindProperty("daySettings").objectReferenceValue == null) Ref(session, "daySettings", settings);
            var tag = hud.transform.Find("PrototypeTag"); if (tag != null) tag.gameObject.SetActive(false);

            var clocks = Panel(hud.transform, "DayClocks", new Vector2(0.685f, 0.85f), new Vector2(0.975f, 0.97f), Navy);
            var day = Text(clocks, "DayClock", "하루 05:00", 18, Cream, new Vector2(0.04f, 0.65f), new Vector2(0.96f, 0.98f));
            var order = Text(clocks, "OrderClock", "주문 대기", 18, Teal, new Vector2(0.04f, 0.33f), new Vector2(0.96f, 0.65f));
            var cash = Text(clocks, "Cash", "보유 0원", 18, Cream, new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.34f));

            var panel = Panel(hud.transform, "SettlementPanel", Vector2.zero, Vector2.one, new Color32(19, 39, 48, 250));
            var card = Panel(panel, "Card", new Vector2(0.1f, 0.09f), new Vector2(0.9f, 0.91f), Cream);
            Text(card, "Heading", "오늘의 배달 정산", 32, Navy, new Vector2(0.055f, 0.86f), new Vector2(0.95f, 0.97f));
            var summary = Text(card, "Summary", "", 22, Navy, new Vector2(0.055f, 0.72f), new Vector2(0.95f, 0.87f));
            var deliveries = Text(card, "Deliveries", "", 17, Navy, new Vector2(0.055f, 0.19f), new Vector2(0.95f, 0.70f));
            deliveries.alignment = TextAlignmentOptions.TopLeft;
            var title = Button(card, "BackToTitle", "타이틀로", new Vector2(0.055f, 0.055f), new Vector2(0.28f, 0.15f));
            var previous = Button(card, "Previous", "이전", new Vector2(0.33f, 0.055f), new Vector2(0.43f, 0.15f));
            var page = Text(card, "Page", "1 / 1", 18, Navy, new Vector2(0.44f, 0.055f), new Vector2(0.56f, 0.15f)); page.alignment = TextAlignmentOptions.Center;
            var next = Button(card, "Next", "다음", new Vector2(0.57f, 0.055f), new Vector2(0.67f, 0.15f));
            var replay = Button(card, "Replay", "다시 플레이", new Vector2(0.72f, 0.055f), new Vector2(0.945f, 0.15f));
            Text(panel, "ReplayNote", "다시 플레이는 수익과 기록을 초기화합니다. 진행 저장은 아직 지원하지 않습니다.", 14, Cream, new Vector2(0.1f, 0.025f), new Vector2(0.9f, 0.07f)).alignment = TextAlignmentOptions.Center;

            var dayHud = hud.GetComponent<DeliveryDayHud>();
            if (dayHud == null) dayHud = hud.gameObject.AddComponent<DeliveryDayHud>();
            Ref(dayHud, "session", session); Ref(dayHud, "dayClock", day); Ref(dayHud, "orderClock", order); Ref(dayHud, "cashLabel", cash);
            Ref(dayHud, "settlementPanel", panel.gameObject); Ref(dayHud, "summary", summary); Ref(dayHud, "deliveries", deliveries); Ref(dayHud, "pageLabel", page);
            Ref(dayHud, "previousPage", previous); Ref(dayHud, "nextPage", next); Ref(dayHud, "backToTitle", title); Ref(dayHud, "replay", replay);
            panel.SetAsLastSibling(); panel.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var found = parent.Find(name);
            var rect = found == null ? new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>() : (RectTransform)found;
            if (found == null) rect.SetParent(parent, false);
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }
        private static RectTransform Panel(Transform parent, string name, Vector2 min, Vector2 max, Color color)
        {
            var rect = Rect(parent, name, min, max); var image = rect.GetComponent<UnityEngine.UI.Image>();
            if (image == null) image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color; image.raycastTarget = false; return rect;
        }
        private static TMP_Text Text(Transform parent, string name, string value, float size, Color color, Vector2 min, Vector2 max)
        {
            var rect = Rect(parent, name, min, max); var text = rect.GetComponent<TextMeshProUGUI>();
            if (text == null) text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.text = value; text.fontSize = size; text.color = color;
            text.raycastTarget = false; text.alignment = TextAlignmentOptions.MidlineLeft; return text;
        }
        private static UnityEngine.UI.Button Button(Transform parent, string name, string label, Vector2 min, Vector2 max)
        {
            var rect = Panel(parent, name, min, max, Teal); var button = rect.GetComponent<UnityEngine.UI.Button>();
            if (button == null) button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = rect.GetComponent<UnityEngine.UI.Image>(); button.targetGraphic.raycastTarget = true;
            Text(rect, "Label", label, 19, Navy, Vector2.zero, Vector2.one).alignment = TextAlignmentOptions.Center;
            return button;
        }
        private static void Ref(UnityEngine.Object owner, string field, UnityEngine.Object value)
        {
            var data = new SerializedObject(owner); data.FindProperty(field).objectReferenceValue = value; data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
