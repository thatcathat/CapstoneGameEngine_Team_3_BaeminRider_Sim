using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    public static class TrafficPilotInstaller
    {
        public const string ScenePath = "Assets/_Game/Scenes/TrafficPilot.unity";
        private const string ArtPath = "Assets/_Game/Art/TrafficPilot";
        private const float VehicleHeight = 0.85f;

        [MenuItem("Delivery Rider/Create Traffic Pilot")]
        public static void Create()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling)
                throw new InvalidOperationException("Exit Play Mode and finish compilation first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene changes first.");
            // Re-running must never overwrite a pilot the user has edited.
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                EditorSceneManager.OpenScene(ScenePath);
                Debug.Log("Existing TrafficPilot opened; no content was overwritten.");
                return;
            }
            Directory.CreateDirectory(ArtPath);
            Directory.CreateDirectory("Assets/_Game/Scenes");
            AssetDatabase.Refresh();
            var source = EditorSceneManager.OpenScene(PrototypeSceneBuilder.GamePath);
            if (!EditorSceneManager.SaveScene(source, ScenePath, true)) throw new InvalidOperationException("Could not copy GameScene.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var roots = scene.GetRootGameObjects();
            var components = roots.SelectMany(g => g.GetComponentsInChildren<Component>(true)).ToArray();
            var session = components.OfType<DeliverySession>().Single();
            var rider = components.OfType<RiderMotor>().Single();
            foreach (var loader in components.OfType<MapLoader>()) loader.gameObject.SetActive(false);
            var road = new GameObject("TrafficPilotWorld").transform;
            BuildRoads(road);
            var sun = new GameObject("PilotSun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.transform.rotation = Quaternion.Euler(45, -25, 0);
            RenderSettings.ambientLight = new Color(0.65f, 0.72f, 0.8f);
            var camera = components.OfType<Camera>().Single(c => c.CompareTag("MainCamera"));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.64f, 0.8f, 0.9f);
            rider.transform.position = Position(-7, -24, 1.08f);
            Set(rider, "followSlopes", p => p.boolValue = true);
            camera.transform.position = rider.transform.position + new Vector3(0, 5, -7);
            camera.transform.LookAt(rider.transform.position + Vector3.up);

            var pickupRoot = roots.Single(g => g.name == "Pick_Up_Spot").transform;
            var shopPositions = new[] { new Vector2(-9, -18), new Vector2(-24, 8), new Vector2(9, 45) };
            for (int i = 0; i < 3; i++)
            {
                var group = pickupRoot.Find("Spot_" + (i + 1));
                if (group == null) throw new InvalidOperationException("Missing pickup group.");
                var p = shopPositions[i];
                group.position = Position(p.x, p.y, 1.018f);
                // Counters remain with the group but face away from the carriageway.
                foreach (var child in group.Cast<Transform>().Where(t => t.name.StartsWith("ShopCounter")))
                    child.position = Position(p.x + (p.x < 0 ? -2.5f : 2.5f), p.y, 0.62f);
                var house = Box("Shop_" + i, Position(p.x + (p.x < 0 ? -6 : 6), p.y, 2.6f),
                    new Vector3(5, 5, 7), Material("Shop" + i, new Color(0.74f, 0.61f + i * 0.08f, 0.45f)), road);
                Box("Awning", house.transform.position + new Vector3(p.x < 0 ? 2.8f : -2.8f, -0.5f, 0),
                    new Vector3(1.5f, 0.2f, 6), Material("Awning", new Color(0.2f, 0.55f, 0.5f)), road, false);
            }
            foreach (var resident in components.OfType<DeliveryResident>())
            {
                Vector2 p;
                switch (resident.name)
                {
                    case "Customer": p = new Vector2(25, -8); break;
                    case "Customer_South": p = new Vector2(8, -30); break;
                    case "Customer_North": p = new Vector2(-8, 45); break;
                    case "Resident_0": p = new Vector2(-7, -12); break;
                    case "Resident_1": p = new Vector2(7, 10); break;
                    default: p = new Vector2(18, 7); break;
                }
                resident.transform.position = Position(p.x, p.y, 1.08f);
            }
            // Keep all incident definitions and recovery rules; use clear, flat side paths for this pilot.
            foreach (var trigger in components.OfType<IncidentTrigger>())
            {
                Vector2 p;
                switch (trigger.name)
                {
                    case "SouthRoadCrash": p = new Vector2(-12, -34); break;
                    case "EastRoadCrash": p = new Vector2(29, 12); break;
                    case "WestTheftZone": p = new Vector2(-22, -10); break;
                    case "NorthReplacementZone": p = new Vector2(14, 10); break;
                    default: p = new Vector2(-24, -24); break;
                }
                trigger.transform.position = Position(p.x, p.y, 1.5f);
                if (!trigger.HasEscapeRoute) continue;
                trigger.ThiefSpawn.position = Position(p.x + 2, p.y, 1.08f);
                for (int i = 0; i < trigger.EscapeRoute.Length; i++)
                    trigger.EscapeRoute[i].position = Position(p.x + 5 + i * 7, p.y, 1.08f);
            }

            var junction = new GameObject("TrafficJunction").AddComponent<TrafficJunction>();
            junction.transform.SetParent(road, false);
            var main = Lane("NorthSouthLane", 40, false, road);
            var cross = Lane("EastWestLane", 32, true, road);
            var vehiclePrefab = VehiclePrefab();
            AddCar(vehiclePrefab, main, junction, session, 15, road, "TrafficVan_A");
            AddCar(vehiclePrefab, main, junction, session, main.Length * 0.5f + 15, road, "TrafficVan_B");
            AddCar(vehiclePrefab, cross, junction, session, 12, road, "TrafficVan_C");
            UpdateMap(components.OfType<DeliveryMapView>().Single());
            var tag = components.OfType<TMPro.TMP_Text>().FirstOrDefault(t => t.name == "PrototypeTag");
            if (tag != null) tag.text = "TRAFFIC / PILOT";
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save TrafficPilot.");
            Debug.Log("TrafficPilot saved: 3 vehicles, 1 junction, slope road, existing delivery and incidents. No Play Mode verification performed.");
        }

        // Smooth elevation transition: plateau -> slope -> plateau. Max grade is 6.56%.
        public static float Elevation(float z) => 1.4f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(12, 44, z));
        private static Vector3 Position(float x, float z, float offset = 0) => new Vector3(x, Elevation(z) + offset, z);

        private static void BuildRoads(Transform parent)
        {
            var asphalt = Material("Asphalt", new Color(0.23f, 0.28f, 0.31f));
            var walk = Material("Walk", new Color(0.76f, 0.75f, 0.67f));
            var green = Material("Ground", new Color(0.58f, 0.68f, 0.51f));
            Surface("Ground", -52, 52, -54, 56, -0.08f, green, parent);
            Surface("MainRoad", -5, 5, -46, 46, 0, asphalt, parent);
            Surface("WestRoad", -38, -5, -5, 5, 0, asphalt, parent);
            Surface("EastRoad", 5, 38, -5, 5, 0, asphalt, parent);
            foreach (int sign in new[] { -1, 1 })
            {
                float x0 = sign < 0 ? -8 : 5;
                float x1 = sign < 0 ? -5 : 8;
                Surface("SidewalkSouth" + sign, x0, x1, -46, -8, 0.025f, walk, parent);
                Surface("SidewalkNorth" + sign, x0, x1, 8, 48, 0.025f, walk, parent);
                float z0 = sign < 0 ? -8 : 5;
                float z1 = sign < 0 ? -5 : 8;
                Surface("SidewalkWest" + sign, -38, -8, z0, z1, 0.025f, walk, parent);
                Surface("SidewalkEast" + sign, 8, 38, z0, z1, 0.025f, walk, parent);
            }
            // Vehicle-free footpath joins the south and west approaches.
            Surface("AlleySouth", -26, -8, -26, -21.5f, 0.01f, walk, parent);
            Surface("AlleyWest", -26, -21.5f, -21.5f, -8, 0.01f, walk, parent);
            var paint = Material("RoadPaint", new Color(0.95f, 0.88f, 0.52f));
            for (int z = -36; z <= 36; z += 6)
                if (Mathf.Abs(z) > 10) Box("CenterLine", Position(0, z, 0.035f), new Vector3(0.12f, 0.02f, 2), paint, parent, false);
            var white = Material("Crosswalk", new Color(0.9f, 0.91f, 0.85f));
            foreach (int sign in new[] { -1, 1 })
            {
                for (int i = -4; i <= 4; i += 2)
                {
                    Box("CrosswalkNS", Position(i, sign * 9, 0.035f), new Vector3(1, 0.02f, 2), white, parent, false);
                    Box("CrosswalkEW", Position(sign * 9, i, 0.035f), new Vector3(2, 0.02f, 1), white, parent, false);
                }
                Box("StopLineNS", Position(sign * 2, -sign * 11, 0.035f), new Vector3(3.7f, 0.02f, 0.3f), white, parent, false);
                Box("StopLineEW", Position(-sign * 11, -sign * 2, 0.035f), new Vector3(0.3f, 0.02f, 3.7f), white, parent, false);
            }
        }

        private static void Surface(string name, float x0, float x1, float z0, float z1, float offset, Material material, Transform parent)
        {
            int strips = Mathf.CeilToInt(z1 - z0);
            var vertices = new Vector3[(strips + 1) * 2];
            var triangles = new int[strips * 6];
            for (int i = 0; i <= strips; i++)
            {
                float z = Mathf.Lerp(z0, z1, (float)i / strips);
                vertices[i * 2] = Position(x0, z, offset);
                vertices[i * 2 + 1] = Position(x1, z, offset);
                if (i == strips) continue;
                int t = i * 6, v = i * 2;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(ArtPath + "/" + name + ".asset"));
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial = material;
            obj.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        private static TrafficRoute Lane(string name, float halfLength, bool eastWest, Transform parent)
        {
            var points = new List<Vector3>();
            Action<float, float> add = (x, z) => points.Add(eastWest ? Position(z, -x, VehicleHeight) : Position(x, z, VehicleHeight));
            for (float z = -halfLength; z < halfLength; z++) add(2, z);
            for (int i = 0; i < 24; i++) { float a = i * Mathf.PI / 24; add(2 * Mathf.Cos(a), halfLength + 2 * Mathf.Sin(a)); }
            for (float z = halfLength; z > -halfLength; z--) add(-2, z);
            for (int i = 0; i < 24; i++) { float a = Mathf.PI + i * Mathf.PI / 24; add(2 * Mathf.Cos(a), -halfLength + 2 * Mathf.Sin(a)); }
            var route = new GameObject(name).AddComponent<TrafficRoute>();
            route.transform.SetParent(parent, false);
            route.Configure(points.ToArray());
            return route;
        }

        private static GameObject VehiclePrefab()
        {
            const string path = "Assets/_Game/Prefabs/TrafficVan.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var obj = new GameObject("TrafficVan");
            var body = obj.AddComponent<Rigidbody>();
            body.isKinematic = true; body.useGravity = false; body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var collider = obj.AddComponent<BoxCollider>(); collider.size = new Vector3(1.8f, 1.2f, 3.6f);
            obj.AddComponent<TrafficVehicle>();
            Box("Body", Vector3.zero, new Vector3(1.8f, 0.8f, 3.6f), Material("Van", new Color(0.91f, 0.62f, 0.18f)), obj.transform, false);
            Box("Cab", new Vector3(0, 0.45f, 0.25f), new Vector3(1.55f, 0.5f, 2.1f), Material("Glass", new Color(0.16f, 0.27f, 0.31f)), obj.transform, false);
            var brakes = Box("BrakeLights", new Vector3(0, 0.05f, -1.82f), new Vector3(1.5f, 0.15f, 0.03f), Material("Brake", new Color(1, 0.08f, 0.03f)), obj.transform, false);
            Set(obj.GetComponent<TrafficVehicle>(), "brakeLights", p => p.objectReferenceValue = brakes.GetComponent<Renderer>());
            foreach (int side in new[] { -1, 1 }) foreach (int end in new[] { -1, 1 })
                Box("Wheel", new Vector3(side * 0.92f, -0.55f, end * 1.1f), new Vector3(0.18f, 0.58f, 0.65f), Material("Tyre", new Color(0.1f, 0.12f, 0.13f)), obj.transform, false);
            var prefab = PrefabUtility.SaveAsPrefabAsset(obj, path);
            UnityEngine.Object.DestroyImmediate(obj);
            return prefab;
        }

        private static void AddCar(GameObject prefab, TrafficRoute route, TrafficJunction junction, DeliverySession session, float start, Transform parent, string name)
        {
            var obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            obj.name = name;
            route.Sample(start, out var position, out var forward);
            obj.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
            var car = obj.GetComponent<TrafficVehicle>();
            Set(car, "session", p => p.objectReferenceValue = session);
            Set(car, "route", p => p.objectReferenceValue = route);
            Set(car, "junction", p => p.objectReferenceValue = junction);
            Set(car, "startDistance", p => p.floatValue = start);
            PrefabUtility.RecordPrefabInstancePropertyModifications(obj.transform);
        }

        private static void UpdateMap(DeliveryMapView map)
        {
            Set(map, "worldMinimum", p => p.vector2Value = new Vector2(-54, -54));
            Set(map, "worldSize", p => p.vector2Value = new Vector2(108, 108));
            var data = new SerializedObject(map);
            var area = (RectTransform)data.FindProperty("mapArea").objectReferenceValue;
            var background = area.Find("MapGeometryBackground");
            if (background == null) throw new InvalidOperationException("Missing map background.");
            var texture = new Texture2D(512, 512, TextureFormat.RGB24, false);
            for (int y = 0; y < 512; y++) for (int x = 0; x < 512; x++)
            {
                float wx = -54 + x * 108f / 511, wz = -54 + y * 108f / 511;
                bool road = (Mathf.Abs(wx) < 5 && Mathf.Abs(wz) < 46) || (Mathf.Abs(wz) < 5 && Mathf.Abs(wx) < 38);
                bool walk = (Mathf.Abs(wx) < 8 && Mathf.Abs(wz) < 48) || (Mathf.Abs(wz) < 8 && Mathf.Abs(wx) < 38);
                bool alley = wx > -26 && wx < -8 && wz > -26 && wz < -21.5f || wx > -26 && wx < -21.5f && wz >= -21.5f && wz < -8;
                texture.SetPixel(x, y, road ? new Color(0.27f, 0.32f, 0.35f) : walk || alley ? new Color(0.78f, 0.77f, 0.69f) : new Color(0.63f, 0.72f, 0.58f));
            }
            texture.Apply();
            string path = ArtPath + "/TrafficPilotMap.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            var image = background.GetComponent<UnityEngine.UI.Image>();
            if (image != null) { image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path); image.color = Color.white; }
            var raw = background.GetComponent<UnityEngine.UI.RawImage>();
            if (raw != null) { raw.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path); raw.color = Color.white; }
            EditorUtility.SetDirty(map);
        }

        private static Material Material(string name, Color color)
        {
            string path = ArtPath + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
            material.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static GameObject Box(string name, Vector3 position, Vector3 size, Material material, Transform parent, bool collider = true)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = size;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
            return obj;
        }

        private static void Set(UnityEngine.Object target, string property, Action<SerializedProperty> write)
        {
            var data = new SerializedObject(target);
            var field = data.FindProperty(property);
            if (field == null) throw new InvalidOperationException("Missing serialized field: " + property);
            write(field);
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
