using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeliveryRider.Editor
{
    // M3 zone calls: imports the baked route table and wires the 12 shops / 32 doors of FourDistrictPrototype.
    // Doors whose spot ended up on a carriageway (after road edits) are moved onto the sidewalk, and every shop gets
    // a name sign. Safe to run repeatedly; the economy asset is created once and then left to manual tuning.
    public static class ZoneCallInstaller
    {
        private const string RouteJson = "Docs/MapLayout/route_table.json";
        private const string GeometryJson = "Docs/MapLayout/district_geometry.json";
        private const string RouteAssetPath = "Assets/_Game/Settings/RouteTable.asset";
        private const string EconomyAssetPath = "Assets/_Game/Settings/EconomySettings.asset";
        private const string StandName = "RecipientStand";
        private const string SignName = "NameSign";
        private const string FontPath = "Assets/_Game/Art/Fonts/PrototypeKoreanRegular.asset";
        private const string MaterialFolder = "Assets/_Game/Art/Materials/";
        // A door spot closer than this to a carriageway edge is moved onto the sidewalk (alleys: beside the paving).
        private const float CarriagewayMargin = 0.5f, AlleyStep = 1f;
        private static readonly Dictionary<string, Color> ZoneColors = new Dictionary<string, Color>
        {
            { "A", new Color(1f, 0.78f, 0.45f) }, { "B", new Color(0.55f, 0.75f, 1f) },
            { "C", new Color(0.55f, 0.9f, 0.6f) }, { "D", new Color(1f, 0.62f, 0.85f) },
        };

        [Serializable] private class DoorData { public float x, y, z, outX, outZ; public string zone; }
        [Serializable] private class Geometry { public DoorData[] doors; }

        [MenuItem("Delivery Rider/Apply Zone Calls (M3)")]
        public static void Apply()
        {
            var session = UnityEngine.Object.FindFirstObjectByType<DeliverySession>(FindObjectsInactive.Include);
            var locations = GameObject.Find("DeliveryLocations");
            if (session == null || locations == null) throw new InvalidOperationException("Open FourDistrictPrototype first.");

            var table = LoadOrCreate<DeliveryRouteTable>(RouteAssetPath, out _);
            JsonUtility.FromJsonOverwrite(File.ReadAllText(RouteJson), table);
            EditorUtility.SetDirty(table);
            if (!table.IsValid) throw new InvalidOperationException("Route table JSON is incomplete.");
            var economy = LoadOrCreate<DeliveryEconomySettings>(EconomyAssetPath, out _);
            AssetDatabase.SaveAssets();

            var geometry = JsonUtility.FromJson<Geometry>(File.ReadAllText(GeometryJson));
            if (geometry.doors.Length != table.DoorCount) throw new InvalidOperationException("Door count differs between geometry and route table.");

            var root = locations.transform;
            var scene = session.gameObject.scene;
            var roads = RoadPath.FromScene(scene);
            var ground = UnityEngine.Object.FindFirstObjectByType<RoadNetwork>();
            var shops = new Transform[table.ShopCount];
            for (int i = 0; i < shops.Length; i++)
            {
                shops[i] = Require(root, $"{table.ShopZone(i)}_Shop_{i + 1:00}");
                AddSign(shops[i], table.ShopName(i), table.ShopZone(i), roads);
            }

            var moved = new List<string>();
            var doors = new Transform[table.DoorCount];
            for (int i = 0; i < doors.Length; i++)
            {
                var door = Require(root, $"{table.DoorZone(i)}_Door_{i + 1:00}");
                if (MoveOffCarriageway(door, roads, ground)) moved.Add(door.name);
                var stand = door.Find(StandName);
                if (stand == null)
                {
                    stand = new GameObject(StandName).transform;
                    stand.SetParent(door, false);
                }
                // The recipient stands on the entrance marker facing the road (outward points into the building).
                var outward = new Vector3(geometry.doors[i].outX, 0f, geometry.doors[i].outZ);
                stand.position = door.position;
                stand.rotation = outward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(-outward.normalized) : Quaternion.identity;
                doors[i] = stand;
            }

            var residents = UnityEngine.Object.FindObjectsByType<DeliveryResident>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var recipient = residents.FirstOrDefault(r => r.name == "Customer");
            if (recipient == null) throw new InvalidOperationException("A resident named Customer is required as the recipient.");
            // The other fixed-route customers would stand at doors as look-alikes; generated calls do not use them.
            foreach (var resident in residents)
                if (resident.name == "Customer_North" || resident.name == "Customer_South") resident.gameObject.SetActive(false);

            var data = new SerializedObject(session);
            data.FindProperty("routeTable").objectReferenceValue = table;
            data.FindProperty("economy").objectReferenceValue = economy;
            SetArray(data.FindProperty("shopPoints"), shops);
            SetArray(data.FindProperty("doorPoints"), doors);
            data.FindProperty("recipient").objectReferenceValue = recipient;
            data.FindProperty("startZone").stringValue = "C";
            data.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
            EditorSceneManager.SaveScene(session.gameObject.scene);
            Debug.Log($"Zone calls applied: {shops.Length} shops (name signs), {doors.Length} doors, recipient {recipient.name}; "
                + $"moved off the carriageway: {(moved.Count == 0 ? "none" : string.Join(", ", moved))}. Play Mode not started.");
        }

        // A door spot inside (or right at the edge of) a carriageway moves to the nearest point that is clear of every
        // carriageway and still on a sidewalk (or just beside an alley's paving): first straight out from the road it
        // stands in, otherwise the closest such point around it (this matters at junctions). The door location moves as
        // a whole (marker and stand).
        private static bool MoveOffCarriageway(Transform door, List<RoadPath> roads, RoadNetwork ground)
        {
            var flat = new Vector2(door.position.x, door.position.z);
            if (ClearOfCarriageways(flat, roads)) return false;
            RoadPath road = null;
            float along = 0f, nearest = float.PositiveInfinity;
            foreach (var candidate in roads)
            {
                float distance = candidate.Nearest(flat, out float at) - candidate.HalfWidth;
                if (distance < nearest) { nearest = distance; road = candidate; along = at; }
            }
            var center = road.PointAt(along);
            var away = flat - center;
            if (away.sqrMagnitude < 0.0001f) { var d = road.DirectionAt(along); away = new Vector2(d.y, -d.x); }
            var target = center + away.normalized * (road.Alley ? road.HalfWidth + AlleyStep : road.HalfWidth + road.SidewalkWidth * 0.5f);
            if (!Usable(target, roads))
            {
                float best = float.PositiveInfinity;
                for (float radius = 0.5f; radius <= 8f; radius += 0.5f)
                    for (int i = 0; i < 24; i++)
                    {
                        float angle = i * Mathf.PI * 2f / 24f;
                        var point = flat + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                        if (radius < best && Usable(point, roads)) { best = radius; target = point; }
                    }
                if (float.IsPositiveInfinity(best)) throw new InvalidOperationException($"No sidewalk spot near {door.name}.");
            }
            Undo.RecordObject(door, "Move door off the carriageway");
            door.position = ground.OnGround(new Vector3(target.x, 0f, target.y), 0f);
            return true;
        }

        private static bool Usable(Vector2 point, List<RoadPath> roads) => ClearOfCarriageways(point, roads) && BesideRoad(point, roads);

        private static bool ClearOfCarriageways(Vector2 point, List<RoadPath> roads)
        {
            foreach (var road in roads)
                if (road.Near(new Rect(point, Vector2.zero), road.HalfWidth + CarriagewayMargin)
                    && road.Nearest(point, out _) < road.HalfWidth + CarriagewayMargin) return false;
            return true;
        }

        // On a main road's sidewalk, or within a step of an alley's paving.
        private static bool BesideRoad(Vector2 point, List<RoadPath> roads)
        {
            foreach (var road in roads)
            {
                float reach = road.HalfWidth + (road.Alley ? AlleyStep + 0.5f : road.SidewalkWidth - 0.3f);
                if (road.Near(new Rect(point, Vector2.zero), reach) && road.Nearest(point, out _) <= reach) return true;
            }
            return false;
        }

        // A post beside the shop's entrance (on the building side) carrying a board with the shop's name in its zone
        // colour; the board turns to face the camera. Rebuilt on every run.
        private static void AddSign(Transform shop, string name, string zone, List<RoadPath> roads)
        {
            var old = shop.Find(SignName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var sign = new GameObject(SignName).transform;
            Undo.RegisterCreatedObjectUndo(sign.gameObject, "Add shop sign");
            sign.SetParent(shop, false);
            sign.localPosition = AwayFromRoad(shop.position, roads) * 1.1f;

            Shape("Pole", PrimitiveType.Cylinder, sign, new Vector3(0f, 1.6f, 0f), new Vector3(0.12f, 1.6f, 0.12f), Material("SignPole", new Color(0.3f, 0.3f, 0.33f)));
            var board = new GameObject("Board").transform;
            board.SetParent(sign, false);
            board.localPosition = new Vector3(0f, 3.6f, 0f);
            board.gameObject.AddComponent<WorldLabel>();
            float width = Mathf.Max(2.2f, name.Length * 0.85f + 0.8f);
            Shape("Panel", PrimitiveType.Cube, board, new Vector3(0f, 0f, 0.06f), new Vector3(width, 1.2f, 0.08f), Material("SignBoard", new Color(0.12f, 0.12f, 0.14f)));
            var text = new GameObject("Name", typeof(RectTransform)).AddComponent<TextMeshPro>();
            text.transform.SetParent(board, false);
            ((RectTransform)text.transform).sizeDelta = new Vector2(width, 1.2f);
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            text.text = name;
            text.fontSize = 5f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = ZoneColors.TryGetValue(zone, out var color) ? color : Color.white;
            text.textWrappingMode = TextWrappingModes.NoWrap;
        }

        // Unit vector (flat) from the nearest road toward the shop, i.e. toward its building.
        private static Vector3 AwayFromRoad(Vector3 position, List<RoadPath> roads)
        {
            var flat = new Vector2(position.x, position.z);
            RoadPath best = null;
            float bestDistance = float.PositiveInfinity, bestAlong = 0f;
            foreach (var road in roads)
            {
                float distance = road.Nearest(flat, out float along);
                if (distance < bestDistance) { bestDistance = distance; best = road; bestAlong = along; }
            }
            if (best == null) return Vector3.zero;
            var away = (flat - best.PointAt(bestAlong)).normalized;
            return new Vector3(away.x, 0f, away.y);
        }

        // Visual-only primitive (collider removed).
        private static GameObject Shape(string name, PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 scale, Material material)
        {
            var obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = localPosition;
            obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            return obj;
        }

        private static Material Material(string name, Color color)
        {
            string path = MaterialFolder + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
            material.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static T LoadOrCreate<T>(string path, out bool created) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            created = asset == null;
            if (!created) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static Transform Require(Transform root, string name)
        {
            var found = root.Find(name);
            if (found == null) throw new InvalidOperationException("Missing delivery location " + name);
            return found;
        }

        private static void SetArray(SerializedProperty property, Transform[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
