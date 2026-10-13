using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeliveryRider.Editor
{
    // M3 zone calls: imports the baked route table and wires the 12 shops / 32 doors of FourDistrictPrototype.
    // Safe to run repeatedly; the economy asset is created once and then left to manual tuning.
    public static class ZoneCallInstaller
    {
        private const string RouteJson = "Docs/MapLayout/route_table.json";
        private const string GeometryJson = "Docs/MapLayout/district_geometry.json";
        private const string RouteAssetPath = "Assets/_Game/Settings/RouteTable.asset";
        private const string EconomyAssetPath = "Assets/_Game/Settings/EconomySettings.asset";
        private const string StandName = "RecipientStand";

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
            var shops = new Transform[table.ShopCount];
            for (int i = 0; i < shops.Length; i++)
                shops[i] = Require(root, $"{table.ShopZone(i)}_Shop_{i + 1:00}");

            var doors = new Transform[table.DoorCount];
            for (int i = 0; i < doors.Length; i++)
            {
                var door = Require(root, $"{table.DoorZone(i)}_Door_{i + 1:00}");
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
            Debug.Log($"Zone calls applied: {shops.Length} shops, {doors.Length} doors, recipient {recipient.name}. Play Mode not started.");
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
