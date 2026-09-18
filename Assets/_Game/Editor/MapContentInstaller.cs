using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    public static class MapContentInstaller
    {
        private static readonly Color Navy = new Color32(19, 39, 48, 255);
        private static readonly Color Teal = new Color32(40, 197, 177, 255);
        private static readonly Color Gold = new Color32(240, 170, 45, 255);
        private static TMP_FontAsset font;

        [MenuItem("Delivery Rider/Install Map And Guidance")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before editing scenes.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene changes first.");
            string previous = SceneManager.GetActiveScene().path;
            var scene = EditorSceneManager.OpenScene(PrototypeSceneBuilder.GamePath);
            ApplyToScene(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(previous) && previous != scene.path) EditorSceneManager.OpenScene(previous);
            Debug.Log("Map, profile markers, order detail and destination guidance saved. Play Mode was not started.");
        }

        public static void ApplyToScene(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            var session = roots.SelectMany(r => r.GetComponentsInChildren<DeliverySession>(true)).Single();
            var hud = roots.SelectMany(r => r.GetComponentsInChildren<GameHud>(true)).Single();
            var camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
            var card = hud.transform.Find("OrderCard") as RectTransform;
            if (card == null) throw new InvalidOperationException("Existing order UI required.");
            font = card.Find("Body").GetComponent<TMP_Text>().font;
            OrderContentInstaller.AddGlyphs(font);
            SetRect(card, new Vector2(0.035f, 0.12f), new Vector2(0.965f, 0.84f));
            SetRect(card.Find("Heading") as RectTransform, new Vector2(0.025f, 0.87f), new Vector2(0.82f, 0.98f));
            card.Find("Heading").GetComponent<TMP_Text>().fontSize = 26;
            SetRect(card.Find("Body") as RectTransform, new Vector2(0.64f, 0.32f), new Vector2(0.97f, 0.80f));
            card.Find("Body").GetComponent<TMP_Text>().fontSize = 17;
            SetRect(card.Find("Accept") as RectTransform, new Vector2(0.64f, 0.16f), new Vector2(0.97f, 0.29f));
            card.Find("Accept").GetComponentInChildren<TMP_Text>(true).fontSize = 19;

            var host = Rect(card, "MapHost", new Vector2(0.025f, 0.08f), new Vector2(0.61f, 0.85f));
            var area = Rect(host, "MapArea", Vector2.zero, Vector2.one);
            var fitter = GetOrAdd<UnityEngine.UI.AspectRatioFitter>(area.gameObject);
            fitter.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent; fitter.aspectRatio = 1;
            var background = GetOrAdd<UnityEngine.UI.Image>(area.gameObject); background.color = new Color32(214, 226, 209, 255); background.raycastTarget = false;
            foreach (var root in roots)
            {
                bool road = root.name == "MainRoad" || root.name == "CrossRoad";
                bool building = root.name.StartsWith("House_") || root.name == "Restaurant";
                if (!road && !building) continue;
                var renderer = root.GetComponent<Renderer>(); if (renderer == null) continue;
                var bounds = renderer.bounds;
                var rect = Rect(area, "Block_" + root.name, Normalize(bounds.min), Normalize(bounds.max));
                var image = GetOrAdd<UnityEngine.UI.Image>(rect.gameObject); image.raycastTarget = false;
                image.color = road ? new Color32(111, 135, 132, 255) : new Color32(172, 186, 172, 255);
            }
            var pickupLine = Line(area, "PickupConnection", Gold);
            var deliveryLine = Line(area, "DeliveryConnection", Teal);

            var data = new SerializedObject(session);
            var routes = data.FindProperty("orderRoutes");
            if (routes.arraySize < 3) throw new InvalidOperationException("Install the order catalogue first.");
            var pickupPoints = new Transform[3]; var shops = new UnityEngine.UI.Image[3];
            for (int i = 0; i < 3; i++)
            {
                var route = routes.GetArrayElementAtIndex(i);
                pickupPoints[i] = (Transform)route.FindPropertyRelative("pickupPoint").objectReferenceValue;
                var definition = (DeliveryOrderDefinition)route.FindPropertyRelative("definition").objectReferenceValue;
                var marker = Marker(area, "Shop_" + i, new Vector2(88, 24), Gold);
                Text(marker, "Label", definition.RestaurantName, 12, Navy, Vector2.zero, Vector2.one);
                shops[i] = marker.GetComponent<UnityEngine.UI.Image>();
            }
            var hudData = new SerializedObject(hud);
            var buttonRefs = hudData.FindProperty("candidateButtons");
            var markers = new RectTransform[3]; var tethers = new RectTransform[3];
            for (int i = 0; i < 3; i++)
            {
                tethers[i] = Line(area, "ProfileTether_" + i, Navy);
                var button = (UnityEngine.UI.Button)buttonRefs.GetArrayElementAtIndex(i).objectReferenceValue;
                if (button == null) throw new InvalidOperationException("Missing existing candidate button.");
                var marker = (RectTransform)button.transform; marker.SetParent(area, false);
                marker.anchorMin = marker.anchorMax = marker.pivot = Vector2.one * 0.5f;
                marker.sizeDelta = new Vector2(60, 62); marker.anchoredPosition = Vector2.zero;
                var label = marker.Find("Label").GetComponent<TMP_Text>();
                SetRect(label.rectTransform, new Vector2(0, 0), new Vector2(1, 0.36f)); label.fontSize = 14;
                Profile(marker);
                markers[i] = marker;
                var navigation = button.navigation; navigation.mode = UnityEngine.UI.Navigation.Mode.None; button.navigation = navigation;
            }
            // Keep candidate buttons above every tether, so overlapping markers remain clickable.
            foreach (var marker in markers) marker.SetAsLastSibling();
            var active = Marker(area, "ActiveCustomer", new Vector2(60, 62), Teal); Profile(active);
            var activeLabel = Text(active, "Label", "진행", 14, Navy, Vector2.zero, new Vector2(1, 0.36f));
            var player = Rect(area, "PlayerMarker", Vector2.one * 0.5f, Vector2.one * 0.5f); player.sizeDelta = new Vector2(24, 24);
            Text(player, "Arrow", "▲", 24, Navy, Vector2.zero, Vector2.one);
            Text(card, "Legend", "▲ 내 위치    노랑 음식점    민트 고객", 15, Navy, new Vector2(0.025f, 0.005f), new Vector2(0.61f, 0.07f));
            Text(card, "MapNote", "북쪽 ↑ 고정\n선은 목적지 연결을 표시합니다.\n지도에서는 시간이 흐릅니다.", 15, Navy, new Vector2(0.64f, 0.015f), new Vector2(0.97f, 0.15f));
            var closeRect = Marker(card, "CloseMap", new Vector2(132, 38), Navy);
            SetRect(closeRect, new Vector2(0.84f, 0.89f), new Vector2(0.975f, 0.975f));
            var close = GetOrAdd<UnityEngine.UI.Button>(closeRect.gameObject); close.targetGraphic = closeRect.GetComponent<UnityEngine.UI.Image>(); close.targetGraphic.raycastTarget = true;
            Text(closeRect, "Label", "닫기  M", 17, Color.white, Vector2.zero, Vector2.one);

            var map = GetOrAdd<DeliveryMapView>(hud.gameObject);
            Ref(map, "session", session); Ref(map, "mapArea", area); Ref(map, "playerMarker", player);
            Refs(map, "candidateMarkers", markers); Refs(map, "candidateTethers", tethers);
            Refs(map, "pickupPoints", pickupPoints); Refs(map, "pickupMarkers", shops);
            Ref(map, "activeCustomerMarker", active); Ref(map, "activeCustomerLabel", activeLabel);
            Ref(map, "pickupConnection", pickupLine); Ref(map, "deliveryConnection", deliveryLine); Ref(map, "closeButton", close);
            AddGuidance(scene, hud, session, camera);
            // Pause panel stays in front of every newly added UI element.
            hud.transform.Find("PauseOverlay").SetAsLastSibling();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void AddGuidance(Scene scene, GameHud hud, DeliverySession session, Camera camera)
        {
            var indicator = Marker(hud.transform, "DestinationIndicator", new Vector2(110, 70), new Color32(19, 39, 48, 235));
            var arrow = Text(indicator, "Arrow", "▲", 26, Color.white, new Vector2(0.3f, 0.45f), new Vector2(0.7f, 1));
            var text = Text(indicator, "Distance", "목적지", 16, Color.white, Vector2.zero, new Vector2(1, 0.45f));
            var beaconObject = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "ActiveDestination");
            if (beaconObject == null) beaconObject = new GameObject("ActiveDestination");
            var ring = GetOrAdd<LineRenderer>(beaconObject);
            ring.useWorldSpace = false; ring.loop = true; ring.widthMultiplier = 0.12f; ring.positionCount = 40;
            for (int i = 0; i < 40; i++)
            {
                float angle = i * Mathf.PI * 2f / 40;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * 1.25f, 0, Mathf.Sin(angle) * 1.25f));
            }
            const string materialPath = "Assets/_Game/Settings/Materials/DestinationRing.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            ring.sharedMaterial = material;
            var labelObject = beaconObject.transform.Find("DestinationLabel");
            if (labelObject == null) { labelObject = new GameObject("DestinationLabel").transform; labelObject.SetParent(beaconObject.transform, false); }
            var labelGameObject = labelObject.gameObject;
            var worldText = GetOrAdd<TextMeshPro>(labelGameObject); worldText.font = font; worldText.fontSize = 4;
            labelGameObject.transform.localPosition = Vector3.up * 4;
            worldText.text = "현재 목적지"; worldText.alignment = TextAlignmentOptions.Center; worldText.rectTransform.sizeDelta = new Vector2(9, 1.5f);
            GetOrAdd<WorldLabel>(labelGameObject);
            var guide = GetOrAdd<DestinationGuide>(hud.gameObject);
            Ref(guide, "session", session); Ref(guide, "view", camera); Ref(guide, "indicator", indicator); Ref(guide, "arrow", arrow.rectTransform);
            Ref(guide, "distanceLabel", text); Ref(guide, "beacon", beaconObject.transform); Ref(guide, "ring", ring); Ref(guide, "beaconLabel", worldText);
        }

        private static Vector2 Normalize(Vector3 p) => new Vector2((p.x + 29) / 58f, (p.z + 26) / 58f);
        private static T GetOrAdd<T>(GameObject obj) where T : Component
        {
            var component = obj.GetComponent<T>();
            return component != null ? component : obj.AddComponent<T>();
        }
        private static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var existing = parent.Find(name);
            var rect = existing == null ? new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>() : (RectTransform)existing;
            if (existing == null) rect.SetParent(parent, false);
            SetRect(rect, min, max); return rect;
        }
        private static void SetRect(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min; rect.anchorMax = max; rect.pivot = Vector2.one * 0.5f;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        private static RectTransform Marker(Transform parent, string name, Vector2 size, Color color)
        {
            var rect = Rect(parent, name, Vector2.one * 0.5f, Vector2.one * 0.5f); rect.sizeDelta = size;
            var image = GetOrAdd<UnityEngine.UI.Image>(rect.gameObject); image.color = color; image.raycastTarget = false;
            return rect;
        }
        private static TMP_Text Text(Transform parent, string name, string value, float size, Color color, Vector2 min, Vector2 max)
        {
            var rect = Rect(parent, name, min, max); var text = GetOrAdd<TextMeshProUGUI>(rect.gameObject);
            text.font = font; text.text = value; text.fontSize = size; text.color = color; text.raycastTarget = false; text.alignment = TextAlignmentOptions.Center;
            return text;
        }
        private static RectTransform Line(Transform parent, string name, Color color)
        {
            var rect = Marker(parent, name, new Vector2(1, 2), color); rect.pivot = new Vector2(0, 0.5f); return rect;
        }
        private static void Profile(Transform marker)
        {
            var head = Marker(marker, "ProfileHead", new Vector2(13, 13), Navy); head.anchoredPosition = new Vector2(0, 14);
            var body = Marker(marker, "ProfileBody", new Vector2(25, 14), Navy); body.anchoredPosition = new Vector2(0, -1);
            var circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            head.GetComponent<UnityEngine.UI.Image>().sprite = circle; body.GetComponent<UnityEngine.UI.Image>().sprite = circle;
        }
        private static void Ref(UnityEngine.Object owner, string field, UnityEngine.Object value)
        {
            var data = new SerializedObject(owner); data.FindProperty(field).objectReferenceValue = value; data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void Refs(UnityEngine.Object owner, string field, UnityEngine.Object[] values)
        {
            var data = new SerializedObject(owner); var array = data.FindProperty(field); array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
