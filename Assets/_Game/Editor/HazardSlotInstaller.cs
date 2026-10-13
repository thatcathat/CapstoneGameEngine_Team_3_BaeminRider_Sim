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
    // M5 hazard slots: builds the authored slots from Docs/MapLayout/hazard_slots.json under one "HazardSlots" root
    // and wires a HazardPlanner. The root is generated content and is rebuilt on every run.
    // Types without a Unity implementation yet are skipped and reported.
    public static class HazardSlotInstaller
    {
        private const string SlotJson = "Docs/MapLayout/hazard_slots.json";
        private const string RootName = "HazardSlots";
        private static readonly string[] RankedTypes = { "manhole", "melon", "door" };

        [Serializable] private class SlotData
        {
            public string type, zone, road;
            public bool isObject, alley;
            public int unlockDay, side, combo;
            public float rank, x, z, dirX, dirZ, halfWidth;
            public int[] routes;
        }
        [Serializable] private class SlotFile { public SlotData[] slots; }

        [MenuItem("Delivery Rider/Apply Hazard Slots (M5)")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Stop Play Mode and finish compilation first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene changes first.");
            var scene = EditorSceneManager.OpenScene(FourDistrictInstaller.ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var components = roots.SelectMany(g => g.GetComponentsInChildren<Component>(true)).ToArray();
            var session = components.OfType<DeliverySession>().Single();
            var rider = components.OfType<RiderMotor>().Single();
            var representative = roots.Single(g => g.name == "RepresentativeIncidents").transform;
            var sourceDoor = representative.GetComponentsInChildren<SwingDoorHazard>(true).Single(d => d.name == "DistrictDoor");
            var sourceTire = representative.GetComponentsInChildren<RollingTireHazard>(true).Single(t => t.name == "DistrictDoorTire");

            // The slot system replaces the fixed L7/L8 course (door, tire, manhole, melon and the theft zone).
            foreach (Transform child in representative)
                if (child.name == "DistrictDoor" || child.name == "DistrictDoorTire" || child.name == "Manhole" || child.name == "Melon"
                    || child.name == "DistrictTheft" || child.name == "TheftMarker")
                    child.gameObject.SetActive(false);

            var old = roots.FirstOrDefault(g => g.name == RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            var parent = new GameObject(RootName).transform;

            var layout = FourDistrictInstaller.ReadLayout();
            var file = JsonUtility.FromJson<SlotFile>(File.ReadAllText(SlotJson));
            var built = new List<HazardSlot>();
            var skipped = new Dictionary<string, int>();
            for (int i = 0; i < file.slots.Length; i++)
            {
                var s = file.slots[i];
                var along = new Vector3(s.dirX, 0f, s.dirZ).normalized;
                var right = Vector3.Cross(Vector3.up, along);
                var center = new Vector3(s.x, 0f, s.z);
                GameObject obj;
                switch (s.type)
                {
                    case "manhole":
                        obj = DistrictIncidentInstaller.CreateGround(layout, session, GroundHazardKind.Manhole, Ground(layout, center), along, parent);
                        break;
                    case "melon":
                        obj = DistrictIncidentInstaller.CreateGround(layout, session, GroundHazardKind.Melon,
                            Ground(layout, center + right * Side(s) * Mathf.Max(0f, s.halfWidth - 1.2f)), along, parent);
                        break;
                    case "door":
                        obj = BuildDoor(sourceDoor, layout, s, center, along, right, parent);
                        break;
                    case "tire":
                        obj = BuildTire(sourceTire, session, layout, s, center, along, right, parent);
                        break;
                    case "hydrant":
                        obj = BuildHydrant(session, layout, s, center, right, parent);
                        break;
                    case "duck":
                        obj = BuildDucks(session, layout, s, center, along, right, parent);
                        break;
                    case "chicken":
                        obj = BuildChickens(session, layout, s, center, along, right, parent);
                        break;
                    case "theft":
                        obj = BuildPigeon(session, layout, s, center, along, right, parent, i);
                        break;
                    default:
                        skipped[s.type] = skipped.TryGetValue(s.type, out int n) ? n + 1 : 1;
                        continue;
                }
                obj.name = $"{i:00}_{s.zone}_{s.type}" + (s.alley ? "_alley" : "") + (s.combo >= 0 ? $"_combo{s.combo}" : "");
                obj.SetActive(true);
                var slot = obj.AddComponent<HazardSlot>();
                slot.Configure(s.type, s.isObject ? HazardRole.Object : HazardRole.Trigger, Mathf.Max(1, s.unlockDay),
                    RankedTypes.Contains(s.type), s.rank, s.zone, s.alley, s.combo, s.routes);
                built.Add(slot);
            }

            var planner = parent.gameObject.AddComponent<HazardPlanner>();
            var data = new SerializedObject(planner);
            data.FindProperty("session").objectReferenceValue = session;
            data.FindProperty("rider").objectReferenceValue = rider;
            var array = data.FindProperty("slots");
            array.arraySize = built.Count;
            for (int i = 0; i < built.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = built[i];
            data.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the hazard slots.");
            string skippedText = skipped.Count == 0 ? "none" : string.Join(", ", skipped.Select(p => $"{p.Key} {p.Value}"));
            Debug.Log($"Hazard slots built: {built.Count} of {file.slots.Length} (skipped: {skippedText}). Play Mode not started.");
        }

        private static float Side(SlotData s) => s.side == 0 ? 1f : Mathf.Sign(s.side);

        // A door stands at the building line (alleys: the alley edge) and swings toward the road centre.
        private static GameObject BuildDoor(SwingDoorHazard source, FourDistrictInstaller.Layout layout, SlotData s,
            Vector3 center, Vector3 along, Vector3 right, Transform parent)
        {
            float side = Side(s);
            float edge = s.alley ? s.halfWidth : s.halfWidth + 3f;
            var door = UnityEngine.Object.Instantiate(source, parent);
            var position = Ground(layout, center + right * side * edge) + Vector3.up * 0.03f;
            door.transform.SetPositionAndRotation(position, Quaternion.LookRotation(side < 0 ? along : -along));
            return door.gameObject;
        }

        // A tire rolls across the road from one sidewalk when the rider comes within about 7 m along the road.
        private static GameObject BuildTire(RollingTireHazard source, DeliverySession session, FourDistrictInstaller.Layout layout, SlotData s,
            Vector3 center, Vector3 along, Vector3 right, Transform parent)
        {
            var tire = UnityEngine.Object.Instantiate(source, parent);
            var root = tire.transform;
            root.SetPositionAndRotation(Ground(layout, center), Quaternion.LookRotation(along));
            float side = Side(s);
            float start = s.halfWidth + (s.alley ? 0.5f : 2.5f);
            float end = s.halfWidth + 1f;
            const int points = 17;
            var data = new SerializedObject(tire);
            data.FindProperty("linkedDoor").objectReferenceValue = null;
            data.FindProperty("session").objectReferenceValue = session;
            data.FindProperty("proximityActivation").boolValue = true;
            data.FindProperty("approachCenter").vector3Value = new Vector3(0f, 1f, 0f);
            data.FindProperty("approachSize").vector3Value = new Vector3(s.halfWidth * 2f + 4f, 3f, 14f);
            var path = data.FindProperty("localPath");
            path.arraySize = points;
            var first = Vector3.zero;
            for (int i = 0; i < points; i++)
            {
                float lateral = Mathf.Lerp(start, -end, i / (float)(points - 1)) * side;
                var world = Ground(layout, center + right * lateral) + Vector3.up * 0.65f;
                if (i == 0) first = world;
                path.GetArrayElementAtIndex(i).vector3Value = root.InverseTransformPoint(world);
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            var wheel = (Transform)data.FindProperty("wheel").objectReferenceValue;
            wheel.position = first;
            wheel.rotation = Quaternion.LookRotation(-right * side);
            var lamp = (Renderer)data.FindProperty("warningLamp").objectReferenceValue;
            lamp.transform.position = first + Vector3.up;
            lamp.enabled = false;
            return tire.gameObject;
        }

        // Hydrant: stands on the sidewalk edge; touching it after the warning throws the rider high into the road.
        private static GameObject BuildHydrant(DeliverySession session, FourDistrictInstaller.Layout layout, SlotData s,
            Vector3 center, Vector3 right, Transform parent)
        {
            float side = Side(s);
            var obj = new GameObject("Hydrant");
            obj.transform.SetParent(parent, false);
            obj.transform.SetPositionAndRotation(Ground(layout, center + right * side * (s.halfWidth + 0.6f)), Quaternion.LookRotation(-right * side));
            var volume = obj.AddComponent<BoxCollider>();
            volume.isTrigger = true;
            volume.center = new Vector3(0f, 1f, 0f);
            volume.size = new Vector3(1.6f, 2.2f, 1.6f);
            var red = DistrictIncidentInstaller.Material("HydrantRed", new Color(0.8f, 0.12f, 0.1f));
            Child(DistrictIncidentInstaller.Shape("Post", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.45f, 0.5f, 0.45f), red, obj.transform), new Vector3(0f, 0.5f, 0f));
            Child(DistrictIncidentInstaller.Shape("Cap", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.5f, 0.3f, 0.5f), red, obj.transform), new Vector3(0f, 1.02f, 0f));
            var lamp = DistrictIncidentInstaller.Shape("Warning", PrimitiveType.Cylinder, Vector3.zero, new Vector3(1.6f, 0.02f, 1.6f),
                DistrictIncidentInstaller.Material("HazardWarning", new Color(1f, 0.65f, 0.08f)), obj.transform);
            Child(lamp, new Vector3(0f, 0.05f, 0f));
            lamp.GetComponent<Renderer>().enabled = false;
            var water = DistrictIncidentInstaller.Shape("Burst", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.7f, 2.5f, 0.7f),
                DistrictIncidentInstaller.Material("HydrantWater", new Color(0.55f, 0.8f, 1f)), obj.transform);
            Child(water, new Vector3(0f, 3.2f, 0f));
            water.SetActive(false);
            var hazard = obj.AddComponent<GroundContactHazard>();
            var data = new SerializedObject(hazard);
            data.FindProperty("session").objectReferenceValue = session;
            data.FindProperty("kind").enumValueIndex = (int)GroundHazardKind.Hydrant;
            data.FindProperty("warningLamp").objectReferenceValue = lamp.GetComponent<Renderer>();
            data.FindProperty("effect").objectReferenceValue = water;
            data.FindProperty("approachRadius").floatValue = 1.8f;
            data.FindProperty("warningSeconds").floatValue = 0.5f;
            data.FindProperty("activeSeconds").floatValue = 0.8f;
            data.FindProperty("forwardImpulse").floatValue = 2f;
            data.FindProperty("upwardImpulse").floatValue = 11f;
            data.ApplyModifiedPropertiesWithoutUndo();
            return obj;
        }

        // Duck flock: five ducks cross the road (kerb to kerb) when the rider comes within about 8 m along the road.
        private static GameObject BuildDucks(DeliverySession session, FourDistrictInstaller.Layout layout, SlotData s,
            Vector3 center, Vector3 along, Vector3 right, Transform parent)
        {
            var obj = new GameObject("DuckFlock");
            obj.transform.SetParent(parent, false);
            obj.transform.SetPositionAndRotation(Ground(layout, center), Quaternion.LookRotation(along));
            float side = Side(s), reach = s.halfWidth + 2f;
            var start = obj.transform.InverseTransformPoint(Ground(layout, center + right * side * reach));
            var end = obj.transform.InverseTransformPoint(Ground(layout, center - right * side * reach));
            var yellow = DistrictIncidentInstaller.Material("DuckYellow", new Color(0.98f, 0.85f, 0.25f));
            var orange = DistrictIncidentInstaller.Material("BeakOrange", new Color(0.95f, 0.5f, 0.1f));
            var birds = new Transform[5];
            for (int i = 0; i < birds.Length; i++)
            {
                var bird = new GameObject("Duck_" + i).transform;
                bird.SetParent(obj.transform, false);
                Child(DistrictIncidentInstaller.Shape("Body", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.45f, 0.35f, 0.6f), yellow, bird), new Vector3(0f, 0.22f, 0f));
                Child(DistrictIncidentInstaller.Shape("Head", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.25f, 0.25f, 0.25f), yellow, bird), new Vector3(0f, 0.48f, 0.22f));
                Child(DistrictIncidentInstaller.Shape("Beak", PrimitiveType.Cube, Vector3.zero, new Vector3(0.1f, 0.05f, 0.14f), orange, bird), new Vector3(0f, 0.46f, 0.38f));
                bird.gameObject.SetActive(false);
                birds[i] = bird;
            }
            var hazard = obj.AddComponent<CrossingFlockHazard>();
            var data = new SerializedObject(hazard);
            data.FindProperty("session").objectReferenceValue = session;
            SetArray(data.FindProperty("birds"), birds);
            data.FindProperty("localStart").vector3Value = start;
            data.FindProperty("localEnd").vector3Value = end;
            data.FindProperty("approachSize").vector3Value = new Vector3(s.halfWidth * 2f + 4f, 3f, 16f);
            data.ApplyModifiedPropertiesWithoutUndo();
            return obj;
        }

        // Chickens: four chickens on the road edge; brushing past them sets the flock chasing.
        private static GameObject BuildChickens(DeliverySession session, FourDistrictInstaller.Layout layout, SlotData s,
            Vector3 center, Vector3 along, Vector3 right, Transform parent)
        {
            float side = Side(s);
            var obj = new GameObject("Chickens");
            obj.transform.SetParent(parent, false);
            obj.transform.SetPositionAndRotation(Ground(layout, center + right * side * Mathf.Max(0f, s.halfWidth - 0.8f)), Quaternion.LookRotation(along));
            var white = DistrictIncidentInstaller.Material("ChickenWhite", new Color(0.95f, 0.95f, 0.92f));
            var red = DistrictIncidentInstaller.Material("ChickenComb", new Color(0.85f, 0.1f, 0.1f));
            var birds = new Transform[4];
            for (int i = 0; i < birds.Length; i++)
            {
                var bird = new GameObject("Chicken_" + i).transform;
                bird.SetParent(obj.transform, false);
                float angle = i * Mathf.PI * 0.5f;
                bird.localPosition = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.7f;
                bird.localRotation = Quaternion.Euler(0f, i * 90f + 45f, 0f);
                Child(DistrictIncidentInstaller.Shape("Body", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.4f, 0.4f, 0.5f), white, bird), new Vector3(0f, 0.3f, 0f));
                Child(DistrictIncidentInstaller.Shape("Head", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.22f, 0.24f, 0.22f), white, bird), new Vector3(0f, 0.58f, 0.18f));
                Child(DistrictIncidentInstaller.Shape("Comb", PrimitiveType.Cube, Vector3.zero, new Vector3(0.05f, 0.1f, 0.12f), red, bird), new Vector3(0f, 0.72f, 0.18f));
                birds[i] = bird;
            }
            var hazard = obj.AddComponent<ChaseFlockHazard>();
            var data = new SerializedObject(hazard);
            data.FindProperty("session").objectReferenceValue = session;
            SetArray(data.FindProperty("birds"), birds);
            data.ApplyModifiedPropertiesWithoutUndo();
            return obj;
        }

        // Pigeon snatch: a pigeon on a short pole beside the road; its escape route follows the road away from the slot.
        private static GameObject BuildPigeon(DeliverySession session, FourDistrictInstaller.Layout layout, SlotData s,
            Vector3 center, Vector3 along, Vector3 right, Transform parent, int index)
        {
            float side = Side(s);
            var obj = new GameObject("PigeonSnatch");
            obj.transform.SetParent(parent, false);
            obj.transform.SetPositionAndRotation(Ground(layout, center + right * side * (s.halfWidth + 1f)), Quaternion.LookRotation(along));
            var gray = DistrictIncidentInstaller.Material("PigeonGray", new Color(0.55f, 0.58f, 0.65f));
            Child(DistrictIncidentInstaller.Shape("Pole", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.12f, 1.25f, 0.12f),
                DistrictIncidentInstaller.Material("PolePaint", new Color(0.3f, 0.3f, 0.32f)), obj.transform), new Vector3(0f, 1.25f, 0f));
            var perch = new GameObject("Perch").transform;
            perch.SetParent(obj.transform, false);
            perch.position = obj.transform.position + Vector3.up * 2.75f;
            perch.rotation = Quaternion.LookRotation(-right * side);

            var pigeon = new GameObject("Pigeon");
            pigeon.transform.SetParent(obj.transform, false);
            pigeon.transform.SetPositionAndRotation(perch.position, perch.rotation);
            Child(DistrictIncidentInstaller.Shape("Body", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.35f, 0.3f, 0.45f), gray, pigeon.transform), Vector3.zero);
            Child(DistrictIncidentInstaller.Shape("Head", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.18f, 0.18f, 0.18f), gray, pigeon.transform), new Vector3(0f, 0.15f, 0.2f));
            var bag = DistrictIncidentInstaller.Shape("StolenFood", PrimitiveType.Cube, Vector3.zero, new Vector3(0.3f, 0.22f, 0.3f),
                DistrictIncidentInstaller.Material("StolenFood", new Color(0.95f, 0.6f, 0.2f)), pigeon.transform);
            Child(bag, new Vector3(0f, -0.25f, 0f));
            bag.SetActive(false);
            var body = pigeon.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.mass = 0.3f;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            pigeon.AddComponent<SphereCollider>().radius = 0.35f;
            var thief = pigeon.AddComponent<FoodThief>();

            var lamp = DistrictIncidentInstaller.Shape("Warning", PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.35f,
                DistrictIncidentInstaller.Material("HazardWarning", new Color(1f, 0.65f, 0.08f)), obj.transform);
            Child(lamp, new Vector3(0f, 3.3f, 0f));
            lamp.GetComponent<Renderer>().enabled = false;

            // Flies along the road, away in a direction that alternates between slots, low enough to punch.
            var direction = index % 2 == 0 ? along : -along;
            var routeRoot = new GameObject("EscapeRoute").transform;
            routeRoot.SetParent(obj.transform, false);
            var route = new Transform[16];
            for (int i = 0; i < route.Length; i++)
            {
                var point = new GameObject("Point_" + i).transform;
                point.SetParent(routeRoot, false);
                point.position = Ground(layout, center + direction * (4f + i * 3f)) + Vector3.up * 1.5f;
                route[i] = point;
            }

            var hazard = obj.AddComponent<PigeonSnatchHazard>();
            var data = new SerializedObject(hazard);
            data.FindProperty("session").objectReferenceValue = session;
            data.FindProperty("pigeon").objectReferenceValue = thief;
            data.FindProperty("perch").objectReferenceValue = perch;
            SetArray(data.FindProperty("escapeRoute"), route);
            data.FindProperty("warningLamp").objectReferenceValue = lamp.GetComponent<Renderer>();
            data.ApplyModifiedPropertiesWithoutUndo();
            return obj;
        }

        private static void Child(GameObject shape, Vector3 localPosition)
        {
            shape.transform.localPosition = localPosition;
            shape.transform.localRotation = Quaternion.identity;
        }

        private static void SetArray(SerializedProperty property, UnityEngine.Object[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static Vector3 Ground(FourDistrictInstaller.Layout layout, Vector3 p)
        {
            p.y = FourDistrictInstaller.Height(layout, p.x, p.z);
            return p;
        }
    }
}
