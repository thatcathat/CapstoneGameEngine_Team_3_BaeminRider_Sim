using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    public static class OrderContentInstaller
    {
        private const string OrdersFolder = "Assets/_Game/Settings/Orders";
        private static readonly string[] Restaurants = { "골목 분식", "노을 도시락", "공원 베이커리" };
        private static readonly string[] Customers = { "민트하우스 주민", "남쪽 골목 주민", "북쪽 공원 주민" };
        private static readonly string[] Foods = { "떡볶이 세트", "따뜻한 도시락", "빵과 우유" };

        [MenuItem("Delivery Rider/Install Order Candidates")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before editing scenes.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene changes first.");
            var previousPath = SceneManager.GetActiveScene().path;
            var scene = EditorSceneManager.OpenScene(PrototypeSceneBuilder.GamePath);
            ApplyToScene(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(previousPath) && previousPath != scene.path) EditorSceneManager.OpenScene(previousPath);
            Debug.Log("Orders installed: 3 pickup points, 3 customers, 6 definitions, 3 choice buttons. No Play Mode run.");
        }

        public static void ApplyToScene(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            var session = roots.SelectMany(r => r.GetComponentsInChildren<DeliverySession>(true)).Single();
            var hud = roots.SelectMany(r => r.GetComponentsInChildren<GameHud>(true)).Single();
            var pickup0 = roots.Single(r => r.name == "RestaurantPickup");
            var customer0 = roots.Single(r => r.name == "Customer");
            var marker0 = roots.Single(r => r.name == "PickupMarker");
            var orderCard = hud.transform.Find("OrderCard") as RectTransform;
            if (orderCard == null) throw new InvalidOperationException("Existing OrderCard is required.");
            var bodyText = orderCard.Find("Body").GetComponent<TMP_Text>();
            var font = bodyText.font;
            AddGlyphs(font);

            // Preserve existing positions and references. Only new locations receive initial positions.
            var pickups = new[]
            {
                pickup0,
                CloneIfMissing(scene, pickup0, "RestaurantPickup_Lunch", new Vector3(7, 1, -12)),
                CloneIfMissing(scene, pickup0, "RestaurantPickup_Bakery", new Vector3(-7, 1, 21))
            };
            var residents = new[]
            {
                customer0,
                CloneIfMissing(scene, customer0, "Customer_South", new Vector3(-7, 1.1f, -17)),
                CloneIfMissing(scene, customer0, "Customer_North", new Vector3(7, 1.1f, 25))
            };
            for (int i = 0; i < 3; i++)
            {
                SetWorldLabel(pickups[i], Restaurants[i], font);
                SetWorldLabel(residents[i], Customers[i], font);
                SetReference(residents[i].GetComponent<DeliveryResident>(), "session", session);
                if (i == 0) continue;
                CloneIfMissing(scene, marker0, "PickupMarker_" + i, new Vector3(pickups[i].transform.position.x, 0.06f, pickups[i].transform.position.z));
                AddCounter(scene, "ShopCounter_" + i, pickups[i].transform.position + new Vector3(i == 1 ? 2.4f : -2.4f, -0.4f, 0), i == 1
                    ? new Color32(220, 139, 89, 255) : new Color32(154, 174, 107, 255));
            }

            Directory.CreateDirectory(OrdersFolder);
            AssetDatabase.Refresh();
            var serialized = new SerializedObject(session);
            var routes = serialized.FindProperty("orderRoutes");
            // Preserve an already-installed catalogue, including user-authored routes and prices.
            if (routes.arraySize == 0)
            {
                routes.arraySize = 6;
                for (int i = 0; i < routes.arraySize; i++)
                {
                    int restaurant = i % 3;
                    int customer = i < 3 ? i : (i + 1) % 3;
                    string path = $"{OrdersFolder}/Order_{i + 1:00}.asset";
                    var definition = AssetDatabase.LoadAssetAtPath<DeliveryOrderDefinition>(path);
                    if (definition == null)
                    {
                        definition = ScriptableObject.CreateInstance<DeliveryOrderDefinition>();
                        var data = new SerializedObject(definition);
                        data.FindProperty("restaurantName").stringValue = Restaurants[restaurant];
                        data.FindProperty("customerName").stringValue = Customers[customer];
                        data.FindProperty("foodName").stringValue = Foods[restaurant];
                        data.FindProperty("onTimeReward").intValue = 5000 + (i % 3) * 500;
                        data.FindProperty("lateReward").intValue = 1000;
                        data.FindProperty("timeLimitSeconds").floatValue = 60 + (i % 3) * 30;
                        data.ApplyModifiedPropertiesWithoutUndo();
                        AssetDatabase.CreateAsset(definition, path);
                    }
                    var route = routes.GetArrayElementAtIndex(i);
                    route.FindPropertyRelative("definition").objectReferenceValue = definition;
                    route.FindPropertyRelative("pickupPoint").objectReferenceValue = pickups[restaurant].transform;
                    route.FindPropertyRelative("customer").objectReferenceValue = residents[customer].GetComponent<DeliveryResident>();
                }
                serialized.FindProperty("candidateCount").intValue = 3;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            if (orderCard.Find("MapHost") != null)
            {
                MapContentInstaller.ApplyToScene(scene);
                return;
            }
            SetRect(orderCard, new Vector2(0.18f, 0.19f), new Vector2(0.82f, 0.81f));
            SetRect(orderCard.Find("Heading") as RectTransform, new Vector2(0.05f, 0.83f), new Vector2(0.95f, 0.97f));
            orderCard.Find("Heading").GetComponent<TMP_Text>().fontSize = 27;
            SetRect(bodyText.rectTransform, new Vector2(0.4f, 0.25f), new Vector2(0.95f, 0.8f));
            bodyText.fontSize = 18;
            SetRect(orderCard.Find("Accept") as RectTransform, new Vector2(0.4f, 0.06f), new Vector2(0.95f, 0.2f));
            var buttonTemplate = orderCard.Find("Accept").gameObject;
            var hudData = new SerializedObject(hud);
            var buttons = hudData.FindProperty("candidateButtons"); buttons.arraySize = 3;
            for (int i = 0; i < 3; i++)
            {
                string name = "Candidate_" + i;
                var child = orderCard.Find(name);
                if (child == null)
                {
                    var clone = UnityEngine.Object.Instantiate(buttonTemplate, orderCard);
                    clone.name = name; child = clone.transform;
                }
                SetRect(child as RectTransform, new Vector2(0.05f, 0.59f - i * 0.23f), new Vector2(0.36f, 0.79f - i * 0.23f));
                var label = child.GetComponentInChildren<TMP_Text>(true);
                label.font = font; label.fontSize = 16; label.alignment = TextAlignmentOptions.Center;
                label.text = $"주문 {i + 1}\n{Restaurants[i]}";
                buttons.GetArrayElementAtIndex(i).objectReferenceValue = child.GetComponent<UnityEngine.UI.Button>();
            }
            hudData.ApplyModifiedPropertiesWithoutUndo();
            var tag = hud.transform.Find("PrototypeTag");
            if (tag != null) tag.GetComponent<TMP_Text>().text = "도보 / 주문 선택";
            EditorSceneManager.MarkSceneDirty(scene);
        }

        internal static void AddGlyphs(TMP_FontAsset font)
        {
            string characters = string.Concat(Directory.GetFiles("Assets/_Game", "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
            characters = new string(characters.Where(c => !char.IsControl(c) && !font.HasCharacter(c)).Distinct().ToArray());
            if (characters.Length == 0) return;
            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            bool added = font.TryAddCharacters(characters, out string missing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(font);
            foreach (var texture in font.atlasTextures) EditorUtility.SetDirty(texture);
            if (!added) throw new InvalidOperationException("Missing font characters: " + missing);
        }

        private static GameObject CloneIfMissing(Scene scene, GameObject template, string name, Vector3 position)
        {
            var existing = scene.GetRootGameObjects().FirstOrDefault(r => r.name == name);
            if (existing != null) return existing;
            var created = UnityEngine.Object.Instantiate(template);
            created.name = name; created.transform.position = position;
            SceneManager.MoveGameObjectToScene(created, scene);
            return created;
        }

        private static void SetWorldLabel(GameObject root, string text, TMP_FontAsset font)
        {
            var label = root.GetComponentInChildren<TMP_Text>(true);
            if (label == null) throw new InvalidOperationException("Missing label: " + root.name);
            label.text = text; label.font = font; label.rectTransform.sizeDelta = new Vector2(8, 1.5f);
            EditorUtility.SetDirty(label);
        }

        private static void AddCounter(Scene scene, string name, Vector3 position, Color color)
        {
            if (scene.GetRootGameObjects().Any(r => r.name == name)) return;
            string path = "Assets/_Game/Settings/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color = color;
                AssetDatabase.CreateAsset(material, path);
            }
            var counter = GameObject.CreatePrimitive(PrimitiveType.Cube); counter.name = name;
            counter.transform.position = position; counter.transform.localScale = new Vector3(1.4f, 1.2f, 3.2f);
            counter.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void SetRect(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void SetReference(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target); serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
