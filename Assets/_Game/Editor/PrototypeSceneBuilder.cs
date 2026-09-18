using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem.UI;

namespace DeliveryRider.Editor
{
    public static class PrototypeSceneBuilder
    {
        public const string TitlePath = "Assets/_Game/Scenes/TitleScene.unity";
        public const string GamePath = "Assets/_Game/Scenes/GameScene.unity";
        private const string FontPath = "Assets/_Game/Art/Fonts/PrototypeKoreanRegular.asset";
        private static TMP_FontAsset font;
        private static readonly Color Navy = new Color32(19, 39, 48, 255);
        private static readonly Color Teal = new Color32(40, 197, 177, 255);
        private static readonly Color Cream = new Color32(246, 244, 232, 255);

        [MenuItem("Delivery Rider/Create Missing Prototype Scenes")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before building scenes.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene changes before building.");
            Directory.CreateDirectory("Assets/_Game/Scenes");
            Directory.CreateDirectory("Assets/_Game/Settings/Materials");
            AssetDatabase.Refresh();
            font = LoadFont();
            if (!File.Exists(TitlePath)) CreateTitle();
            if (!File.Exists(GamePath)) CreateGame();
            var otherScenes = EditorBuildSettings.scenes.Where(s => s.path != TitlePath && s.path != GamePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(TitlePath, true), new EditorBuildSettingsScene(GamePath, true) }.Concat(otherScenes).ToArray();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(TitlePath);
            Debug.Log("Prototype scenes ready: TitleScene → GameScene. Existing scenes preserved.");
        }

        private static TMP_FontAsset LoadFont()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (existing != null) return existing;
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/_Game/Art/Fonts/NotoSansKR-Regular.otf");
            if (source == null) throw new InvalidOperationException("Noto Sans KR font has not imported yet.");
            var asset = TMP_FontAsset.CreateFontAsset(source, 48, 5, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048);
            asset.name = "PrototypeKoreanRegular";
            string text = string.Concat(Directory.GetFiles("Assets/_Game", "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
            text += new string(Enumerable.Range(32, 95).Select(i => (char)i).ToArray());
            text = new string(text.Where(c => !char.IsControl(c)).Distinct().ToArray());
            if (!asset.TryAddCharacters(text, out string missing)) Debug.LogWarning("Font missing characters: " + missing);
            asset.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(asset, FontPath);
            foreach (var texture in asset.atlasTextures) AssetDatabase.AddObjectToAsset(texture, asset);
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        public static void UpdateSceneFonts()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            font = LoadFont();
            foreach (var path in new[] { TitlePath, GamePath })
            {
                var scene = EditorSceneManager.OpenScene(path);
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                    {
                        text.font = font;
                        EditorUtility.SetDirty(text);
                    }
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(TitlePath);
        }

        private static void CreateTitle()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = MakeCamera(new Vector3(27f, 27f, -31f), new Vector3(0, 0, 3));
            camera.orthographic = true;
            camera.orthographicSize = 24;
            BuildTown();
            var canvas = Canvas(camera);
            var panel = Panel("TitlePanel", canvas.transform, new Vector2(0, 0), new Vector2(0.47f, 1), Navy);
            var stack = Rect("TitleContent", panel.transform, new Vector2(0.12f, 0.16f), new Vector2(0.88f, 0.85f));
            var layout = stack.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing = 14;
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            StackText(stack, "RIDER / FIRST SHIFT", 20, Teal, 35);
            StackText(stack, "오늘도\n배달 중", 60, Cream, 150);
            StackText(stack, "작은 골목에서 시작하는\n나의 첫 배달 이야기", 24, new Color32(184, 204, 204, 255), 70);
            StackText(stack, "걸어서 출발하고, 부딪혀 전달하세요.", 17, Cream, 26);
            var start = StackButton(stack, "StartButton", "배달 시작", Teal, Navy);
            var quit = StackButton(stack, "QuitButton", "게임 종료", new Color32(38, 60, 70, 255), Cream);
            var menu = new GameObject("SceneMenu").AddComponent<SceneMenu>();
            Ref(menu, "startButton", start); Ref(menu, "quitButton", quit);
            TextAt(canvas.transform, "Edition", "FIRST DELIVERY\n작은 동네, 첫 번째 출근", 24, Navy, new Vector2(0.66f, 0.82f), new Vector2(0.96f, 0.96f));
            TextAt(canvas.transform, "Footer", "3인칭 배달 시뮬레이터  /  PROTOTYPE 01", 15, new Color32(160, 185, 187, 255), new Vector2(0.055f, 0.035f), new Vector2(0.46f, 0.08f));
            EditorSceneManager.SaveScene(scene, TitlePath);
        }

        private static void CreateGame()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildTown();
            var camera = MakeCamera(new Vector3(0, 5, -17), Vector3.zero);
            var session = new GameObject("DeliverySession").AddComponent<DeliverySession>();
            var player = Person("Player", new Vector3(0, 1.1f, -11), Teal, true);
            var motor = player.AddComponent<RiderMotor>();
            var follow = camera.gameObject.AddComponent<ThirdPersonCamera>();
            Ref(follow, "target", player.transform); Ref(follow, "motor", motor);
            Ref(motor, "view", camera.transform); Ref(motor, "session", session);
            var bag = Box("FoodBag", new Vector3(0, 0.15f, -0.55f), new Vector3(0.8f, 0.9f, 0.5f), Mat("Bag", new Color32(251, 184, 67, 255)), false, player.transform);
            bag.SetActive(false);
            var customer = Person("Customer", new Vector3(9, 1.1f, 15), new Color32(42, 177, 151, 255), false).AddComponent<DeliveryResident>();
            Ref(customer, "session", session);
            Label("CustomerLabel", "배달 고객", customer.transform, new Vector3(0, 2, 0), Teal);
            var restaurant = new GameObject("RestaurantPickup"); restaurant.transform.position = new Vector3(-7, 1, 4);
            Label("PickupLabel", "음식 받는 곳", restaurant.transform, new Vector3(0, 2.1f, 0), new Color32(255, 183, 65, 255));
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder); marker.name = "PickupMarker";
            marker.transform.position = new Vector3(-7, 0.06f, 4); marker.transform.localScale = new Vector3(3, 0.025f, 3);
            marker.GetComponent<Renderer>().sharedMaterial = Mat("PickupGold", new Color32(255, 192, 68, 255));
            UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
            for (int i = 0; i < 3; i++)
            {
                var resident = Person("Resident_" + i, new Vector3(3.5f, 1.1f, i * 7 - 4), new Color32(209, 132, 108, 255), false).AddComponent<DeliveryResident>();
                Ref(resident, "session", session);
            }
            Ref(session, "player", player.transform); Ref(session, "foodBag", bag);
            CreateHud(camera, session);
            OrderContentInstaller.ApplyToScene(scene);
            MapContentInstaller.ApplyToScene(scene);
            DayContentInstaller.ApplyToScene(scene);
            IncidentContentInstaller.ApplyToScene(scene);
            TheftContentInstaller.ApplyToScene(scene);
            EditorSceneManager.SaveScene(scene, GamePath);
        }

        private static void CreateHud(Camera camera, DeliverySession session)
        {
            var canvas = Canvas(camera);
            var hud = canvas.gameObject.AddComponent<GameHud>(); Ref(hud, "session", session);
            var bar = Panel("ObjectiveBar", canvas.transform, new Vector2(0.025f, 0.85f), new Vector2(0.65f, 0.965f), Navy);
            var objective = TextAt(bar.transform, "Objective", "", 26, Cream, new Vector2(0.035f, 0.38f), new Vector2(0.97f, 0.95f));
            var distance = TextAt(bar.transform, "Distance", "", 15, Teal, new Vector2(0.035f, 0.04f), new Vector2(0.97f, 0.4f));
            TextAt(canvas.transform, "PrototypeTag", "도보 / 첫 배달 테스트", 18, Navy, new Vector2(0.76f, 0.9f), new Vector2(0.98f, 0.97f));
            var bottom = Panel("Controls", canvas.transform, new Vector2(0.2f, 0.025f), new Vector2(0.8f, 0.1f), Navy);
            var prompt = TextAt(bottom.transform, "Prompt", "", 18, Cream, new Vector2(0.02f, 0), new Vector2(0.98f, 1)); prompt.alignment = TextAlignmentOptions.Center;
            var card = Panel("OrderCard", canvas.transform, new Vector2(0.28f, 0.27f), new Vector2(0.72f, 0.73f), Cream);
            var cardTitle = TextAt(card.transform, "Heading", "", 30, Navy, new Vector2(0.07f, 0.76f), new Vector2(0.93f, 0.96f));
            var cardBody = TextAt(card.transform, "Body", "", 19, Navy, new Vector2(0.07f, 0.28f), new Vector2(0.93f, 0.74f));
            var accept = ButtonAt(card.transform, "Accept", "주문 수락", new Vector2(0.07f, 0.07f), new Vector2(0.93f, 0.24f), Teal, Navy);
            var pause = Panel("PauseOverlay", canvas.transform, Vector2.zero, Vector2.one, new Color(0.035f, 0.08f, 0.11f, 0.92f));
            TextAt(pause.transform, "Heading", "잠시 쉬어가기", 44, Cream, new Vector2(0.3f, 0.61f), new Vector2(0.7f, 0.74f)).alignment = TextAlignmentOptions.Center;
            var resume = ButtonAt(pause.transform, "Resume", "계속 배달하기", new Vector2(0.35f, 0.45f), new Vector2(0.65f, 0.55f), Teal, Navy);
            var title = ButtonAt(pause.transform, "Title", "타이틀로 돌아가기", new Vector2(0.35f, 0.31f), new Vector2(0.65f, 0.41f), Cream, Navy);
            Ref(hud, "objective", objective); Ref(hud, "distance", distance); Ref(hud, "prompt", prompt);
            Ref(hud, "cardTitle", cardTitle); Ref(hud, "cardBody", cardBody); Ref(hud, "actionLabel", accept.GetComponentInChildren<TMP_Text>());
            Ref(hud, "orderCard", card); Ref(hud, "pausePanel", pause); Ref(hud, "accept", accept); Ref(hud, "resume", resume); Ref(hud, "title", title);
            pause.SetActive(false);
        }

        private static void BuildTown()
        {
            var ground = Mat("Ground", new Color32(220, 225, 204, 255));
            var asphalt = Mat("Asphalt", new Color32(70, 89, 93, 255));
            var sidewalk = Mat("Sidewalk", new Color32(212, 210, 190, 255));
            Box("Ground", new Vector3(0, -0.25f, 3), new Vector3(58, 0.5f, 58), ground);
            Box("MainRoad", new Vector3(0, 0.005f, 3), new Vector3(8, 0.015f, 55), asphalt, false);
            Box("CrossRoad", new Vector3(0, 0.012f, 4), new Vector3(55, 0.015f, 7), asphalt, false);
            var stripe = Mat("RoadPaint", Cream);
            for (int z = -21; z <= 27; z += 6) if (z < 0 || z > 8)
                Box("RoadDash", new Vector3(0, 0.03f, z), new Vector3(0.16f, 0.025f, 2.2f), stripe, false);
            for (int x = -3; x <= 3; x++) Box("Crosswalk", new Vector3(x, 0.04f, -0.4f), new Vector3(0.55f, 0.02f, 2), stripe, false);
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Sidewalk", new Vector3(side * 7, 0, 3), new Vector3(6, 0.035f, 55), sidewalk, false);
                for (int row = 0; row < 3; row++)
                {
                    var center = new Vector3(side * 16, 0, row * 17 - 15);
                    var color = row % 2 == 0 ? new Color32(226, 177, 137, 255) : new Color32(146, 182, 182, 255);
                    Building("House_" + side + "_" + row, center, new Vector3(9, 5 + row * 1.6f, 10), Mat("House" + row, color));
                }
            }
            Building("Restaurant", new Vector3(-13, 0, 4), new Vector3(7, 4, 6), Mat("Restaurant", new Color32(244, 214, 156, 255)));
            Box("RestaurantAwning", new Vector3(-8.8f, 2.8f, 4), new Vector3(1.8f, 0.2f, 6), Mat("Awning", Teal));
            for (int i = 0; i < 8; i++)
            {
                float x = i < 4 ? -24 : 24; float z = (i % 4) * 12 - 17;
                Box("TreeTrunk", new Vector3(x, 1, z), new Vector3(0.45f, 2, 0.45f), Mat("Wood", new Color32(112, 87, 64, 255)));
                var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere); crown.name = "TreeCrown";
                crown.transform.position = new Vector3(x, 3, z); crown.transform.localScale = new Vector3(3.2f, 3.8f, 3.2f);
                crown.GetComponent<Renderer>().sharedMaterial = Mat("Leaves", new Color32(102, 155, 117, 255));
                UnityEngine.Object.DestroyImmediate(crown.GetComponent<Collider>());
            }
            var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f;
            light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(48, -35, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.72f, 0.77f, 0.8f);
            RenderSettings.fog = true; RenderSettings.fogColor = new Color32(198, 223, 216, 255); RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 40; RenderSettings.fogEndDistance = 100;
        }

        private static void Building(string name, Vector3 center, Vector3 size, Material material)
        {
            var root = Box(name, center + Vector3.up * size.y / 2, size, material);
            var roof = Mat("Roof", new Color32(74, 113, 120, 255));
            Box("Roof", new Vector3(0, size.y / 2 + 0.15f, 0), new Vector3(size.x + 0.5f, 0.3f, size.z + 0.5f), roof, false, root.transform);
            for (int i = -1; i <= 1; i++)
                Box("Window", new Vector3(i * 2.3f, 0.3f, -size.z / 2 - 0.02f), new Vector3(1.2f, 1.5f, 0.06f), Mat("Windows", new Color32(53, 92, 103, 255)), false, root.transform);
        }

        private static GameObject Person(string name, Vector3 position, Color color, bool player)
        {
            var person = GameObject.CreatePrimitive(PrimitiveType.Capsule); person.name = name;
            person.transform.position = position; person.layer = player ? 2 : 0;
            person.GetComponent<Renderer>().sharedMaterial = Mat(player ? "RiderCoat" : name + "Coat", color);
            var body = person.AddComponent<Rigidbody>(); body.mass = player ? 75 : 45;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearDamping = player ? 0 : 4;
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere); head.name = "Head"; head.transform.SetParent(person.transform, false);
            head.transform.localPosition = new Vector3(0, 0.75f, 0); head.transform.localScale = Vector3.one * 0.7f;
            head.GetComponent<Renderer>().sharedMaterial = Mat("Skin", new Color32(246, 209, 166, 255)); UnityEngine.Object.DestroyImmediate(head.GetComponent<Collider>());
            Box("Face", new Vector3(0, 0.79f, 0.32f), new Vector3(0.3f, 0.08f, 0.06f), Mat("Face", Navy), false, person.transform);
            return person;
        }

        private static Camera MakeCamera(Vector3 position, Vector3 target)
        {
            var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.gameObject.AddComponent<AudioListener>(); camera.transform.position = position; camera.transform.LookAt(target);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(198, 223, 216, 255);
            camera.nearClipPlane = 0.1f; camera.farClipPlane = 200; camera.fieldOfView = 58;
            camera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            return camera;
        }

        private static UnityEngine.Canvas Canvas(Camera camera)
        {
            var canvas = new GameObject("UI", typeof(RectTransform)).AddComponent<UnityEngine.Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 0.5f;
            var scaler = canvas.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;
            canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var events = new GameObject("EventSystem").AddComponent<UnityEngine.EventSystems.EventSystem>();
            events.gameObject.AddComponent<InputSystemUIInputModule>();
            return canvas;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            return rect;
        }
        private static GameObject Panel(string name, Transform parent, Vector2 min, Vector2 max, Color color)
        {
            var rect = Rect(name, parent, min, max); var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = false; return rect.gameObject;
        }
        private static TMP_Text TextAt(Transform parent, string name, string value, float size, Color color, Vector2 min, Vector2 max)
        {
            var rect = Rect(name, parent, min, max); var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.text = value; text.fontSize = size; text.color = color; text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.MidlineLeft; text.enableAutoSizing = false; return text;
        }
        private static void StackText(Transform parent, string value, float size, Color color, float height)
        {
            var text = TextAt(parent, "Label", value, size, color, Vector2.zero, Vector2.one);
            var element = text.gameObject.AddComponent<UnityEngine.UI.LayoutElement>(); element.preferredHeight = height; element.minHeight = height;
        }
        private static UnityEngine.UI.Button StackButton(Transform parent, string name, string value, Color bg, Color fg)
        {
            var button = ButtonAt(parent, name, value, Vector2.zero, Vector2.one, bg, fg);
            var element = button.gameObject.AddComponent<UnityEngine.UI.LayoutElement>(); element.preferredHeight = 60; element.minHeight = 60;
            return button;
        }
        private static UnityEngine.UI.Button ButtonAt(Transform parent, string name, string value, Vector2 min, Vector2 max, Color bg, Color fg)
        {
            var panel = Panel(name, parent, min, max, bg); var image = panel.GetComponent<UnityEngine.UI.Image>(); image.raycastTarget = true;
            var button = panel.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.highlightedColor = new Color(0.88f, 0.98f, 0.95f); colors.pressedColor = new Color(0.7f, 0.88f, 0.83f); button.colors = colors;
            var text = TextAt(panel.transform, "Label", value, 23, fg, new Vector2(0.03f, 0), new Vector2(0.97f, 1)); text.alignment = TextAlignmentOptions.Center;
            return button;
        }
        private static void Label(string name, string value, Transform parent, Vector3 local, Color color)
        {
            var obj = new GameObject(name); obj.transform.SetParent(parent, false); obj.transform.localPosition = local;
            var text = obj.AddComponent<TextMeshPro>(); text.font = font; text.text = value; text.fontSize = 4; text.color = color;
            text.alignment = TextAlignmentOptions.Center; text.rectTransform.sizeDelta = new Vector2(7, 1.5f); obj.AddComponent<WorldLabel>();
        }
        private static GameObject Box(string name, Vector3 position, Vector3 size, Material material, bool collider = true, Transform parent = null)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = name;
            obj.transform.position = parent == null ? position : parent.position + parent.rotation * position;
            obj.transform.rotation = parent == null ? Quaternion.identity : parent.rotation;
            obj.transform.localScale = size;
            if (parent != null) obj.transform.SetParent(parent, true);
            obj.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
            return obj;
        }
        private static Material Mat(string name, Color color)
        {
            string path = "Assets/_Game/Settings/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.name = name; material.color = color; material.SetFloat("_Smoothness", 0.12f);
            AssetDatabase.CreateAsset(material, path); return material;
        }
        private static void Ref(UnityEngine.Object owner, string property, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(owner); serialized.FindProperty(property).objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
