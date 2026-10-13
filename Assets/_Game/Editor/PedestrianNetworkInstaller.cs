using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    // Bake existing sidewalk space, clip roads/solid obstructions, link nearby same-corner endpoints.
    public static class PedestrianNetworkInstaller
    {
        private const float SampleStep = 1f, RoadMargin = 0.7f, ConnectorReach = 6f, FeetLift = 0.06f;
        private static readonly Collider[] Obstacles = new Collider[64];
        private static readonly string[] CoatNames = { "Resident_0Coat", "Resident_1Coat", "Resident_2Coat", "CustomerCoat" };

        [MenuItem("Delivery Rider/Apply Pedestrian Network")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Use edit mode after compilation.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/_Game/Scenes/FourDistrictPrototype.unity") throw new InvalidOperationException("Open FourDistrictPrototype.");
            var session = UnityEngine.Object.FindFirstObjectByType<DeliverySession>();
            var ground = UnityEngine.Object.FindFirstObjectByType<RoadNetwork>();
            if (session == null || ground == null) throw new InvalidOperationException("Delivery session and roads are required.");
            var roads = RoadPath.FromScene(scene);
            var routes = new List<PedestrianPath>();
            foreach (var road in roads.Where(r => !r.Alley && r.SidewalkWidth >= 1.6f))
                foreach (int side in new[] { -1, 1 }) BakeSide(road, side, roads, ground, routes);
            if (routes.Count == 0) throw new InvalidOperationException("No clear sidewalk segments were found.");
            LinkCorners(routes, roads, ground);
            Validate(routes);

            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Pedestrians");
            if (root != null && root.GetComponent<PedestrianNetwork>() == null) throw new InvalidOperationException("Pedestrians root is not owned by this installer.");
            if (root == null) { root = new GameObject("Pedestrians"); Undo.RegisterCreatedObjectUndo(root, "Add sidewalk traffic"); }
            var network = root.GetComponent<PedestrianNetwork>() ?? Undo.AddComponent<PedestrianNetwork>(root);
            var residents = Spawn(root.transform, network, routes);
            Undo.RecordObject(network, "Update pedestrian routes");
            network.Configure(session, routes.ToArray(), residents.ToArray());
            EditorUtility.SetDirty(network);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save pedestrian network.");
            Debug.Log($"Pedestrians: {routes.Count(p => !p.connector)} directed sidewalk segments, {routes.Count(p => p.connector)} corner connectors, {residents.Count} NPCs. No road crossings. Play Mode not started.");
        }

        private static void BakeSide(RoadPath road, int side, List<RoadPath> roads, RoadNetwork ground, List<PedestrianPath> routes)
        {
            var fragment = new List<Vector3>();
            int count = Mathf.Max(1, Mathf.CeilToInt(road.Length / SampleStep));
            for (int i = 0; i <= count; i++)
            {
                float along = road.Length * i / count;
                var direction = road.DirectionAt(along);
                var flat = road.PointAt(along) + new Vector2(direction.y, -direction.x) * (side * (road.HalfWidth + road.SidewalkWidth * 0.5f));
                bool clear = Walkable(flat, roads, ground, out var feet);
                if (clear && fragment.Count > 0 && !ClearLine(fragment[fragment.Count - 1], feet, roads, ground)) clear = false;
                if (!clear) { AddFragment(fragment, road.SidewalkWidth * 0.5f, routes); fragment.Clear(); }
                else fragment.Add(feet);
            }
            AddFragment(fragment, road.SidewalkWidth * 0.5f, routes);
        }

        private static void AddFragment(List<Vector3> points, float halfWidth, List<PedestrianPath> routes)
        {
            if (points.Count < 4 || (points[points.Count - 1] - points[0]).sqrMagnitude < 4f) return;
            int forward = routes.Count, reverse = forward + 1;
            routes.Add(MakePath(points.ToArray(), halfWidth, reverse, false));
            routes.Add(MakePath(points.AsEnumerable().Reverse().ToArray(), halfWidth, forward, false));
        }

        private static PedestrianPath MakePath(Vector3[] points, float halfWidth, int reverse, bool connector)
        {
            var cumulative = new float[points.Length];
            for (int i = 1; i < points.Length; i++) cumulative[i] = cumulative[i - 1] + Vector3.Distance(points[i], points[i - 1]);
            return new PedestrianPath { points = points, cumulative = cumulative, length = cumulative[cumulative.Length - 1], halfWidth = halfWidth, reverse = reverse, connector = connector };
        }

        private static void LinkCorners(List<PedestrianPath> routes, List<RoadPath> roads, RoadNetwork ground)
        {
            int lanes = routes.Count;
            for (int a = 0; a < lanes; a++)
            {
                var source = routes[a];
                var end = source.points[source.points.Length - 1];
                var next = new List<int>();
                for (int b = 0; b < lanes; b++)
                {
                    if (b == a || b == source.reverse) continue;
                    var destination = routes[b];
                    var start = destination.points[0];
                    float span = Vector3.Distance(end, start);
                    if (span > ConnectorReach || span < 0.1f || !ClearLine(end, start, roads, ground)) continue;
                    int count = Mathf.Max(2, Mathf.CeilToInt(span / 0.4f) + 1);
                    var points = new Vector3[count];
                    for (int i = 0; i < count; i++) points[i] = ground.OnGround(Vector3.Lerp(end, start, i / (float)(count - 1)), FeetLift);
                    int connector = routes.Count;
                    var path = MakePath(points, Mathf.Min(source.halfWidth, destination.halfWidth), a, true);
                    path.next = new[] { b };
                    routes.Add(path);
                    next.Add(connector);
                }
                source.next = next.ToArray();
            }
        }

        private static bool ClearLine(Vector3 from, Vector3 to, List<RoadPath> roads, RoadNetwork ground)
        {
            int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / 0.4f));
            for (int i = 0; i <= count; i++)
            {
                var point = Vector3.Lerp(from, to, i / (float)count);
                if (!Walkable(new Vector2(point.x, point.z), roads, ground, out var feet) || Mathf.Abs(feet.y - point.y) > 0.6f) return false;
            }
            return true;
        }

        private static bool Walkable(Vector2 flat, List<RoadPath> roads, RoadNetwork ground, out Vector3 feet)
        {
            feet = ground.OnGround(new Vector3(flat.x, 0f, flat.y), FeetLift);
            bool onSidewalk = false;
            foreach (var road in roads)
            {
                if (!road.Near(new Rect(flat.x, flat.y, 0f, 0f), road.HalfWidth + road.SidewalkWidth + 1f)) continue;
                float distance = road.Nearest(flat, out _);
                if (distance < road.HalfWidth + RoadMargin) return false;
                if (!road.Alley && distance <= road.HalfWidth + road.SidewalkWidth - 0.65f) onSidewalk = true;
            }
            if (!onSidewalk) return false;
            // Inspect both walking directions' offsets plus body radius.
            int count = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * 0.75f, feet + Vector3.up * 1.4f,
                0.65f, Obstacles, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Obstacles[i].attachedRigidbody == null) return false;
            return count < Obstacles.Length;
        }

        private static List<TrafficPedestrian> Spawn(Transform root, PedestrianNetwork network, List<PedestrianPath> paths)
        {
            var residents = new List<TrafficPedestrian>();
            var placed = new List<Vector3>();
            var random = new System.Random(37);
            var lanes = Enumerable.Range(0, paths.Count).Where(i => !paths[i].connector && paths[i].length > 6f).ToArray();
            float total = lanes.Sum(i => paths[i].length);
            int attempts = 0;
            while (residents.Count < network.Population && lanes.Length > 0 && attempts++ < network.Population * 60)
            {
                float pick = (float)random.NextDouble() * total;
                int lane = lanes[lanes.Length - 1];
                foreach (int index in lanes) { if (pick < paths[index].length) { lane = index; break; } pick -= paths[index].length; }
                float distance = 1f + (float)random.NextDouble() * (paths[lane].length - 2f);
                Sample(paths[lane], distance, out var feet, out var forward);
                feet += Vector3.Cross(Vector3.up, forward) * 0.35f;
                if (placed.Any(p => (p - feet).sqrMagnitude < 9f)) continue;
                string name = "Pedestrian_" + residents.Count.ToString("00");
                var previous = root.Find(name);
                var person = previous != null ? previous.GetComponent<TrafficPedestrian>() : null;
                if (previous != null && person == null) throw new InvalidOperationException("Unexpected user object: " + name);
                if (person == null) person = CreatePerson(name, root, residents.Count);
                Undo.RecordObject(person.transform, "Place sidewalk NPC");
                person.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(forward));
                person.gameObject.SetActive(true);
                Undo.RecordObject(person, "Configure sidewalk NPC");
                person.Configure(network, lane, distance, random.Next(), person.transform.Find("Visual"));
                EditorUtility.SetDirty(person);
                residents.Add(person); placed.Add(feet);
            }
            foreach (var other in root.GetComponentsInChildren<TrafficPedestrian>(true))
                if (!residents.Contains(other)) other.gameObject.SetActive(false);
            return residents;
        }

        private static void Sample(PedestrianPath path, float distance, out Vector3 feet, out Vector3 forward)
        {
            int i = Array.BinarySearch(path.cumulative, distance);
            if (i < 0) i = ~i - 1;
            i = Mathf.Clamp(i, 0, path.points.Length - 2);
            feet = Vector3.Lerp(path.points[i], path.points[i + 1], (distance - path.cumulative[i]) / (path.cumulative[i + 1] - path.cumulative[i]));
            forward = Vector3.ProjectOnPlane(path.points[i + 1] - path.points[i], Vector3.up).normalized;
        }

        private static TrafficPedestrian CreatePerson(string name, Transform parent, int index)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Add walking NPC");
            go.transform.SetParent(parent, false);
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true; body.useGravity = false; body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var shape = go.AddComponent<CapsuleCollider>();
            shape.center = new Vector3(0f, 0.9f, 0f); shape.height = 1.8f; shape.radius = 0.28f;
            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            Primitive("Body", PrimitiveType.Capsule, visual, new Vector3(0f, 0.75f, 0f), new Vector3(0.5f, 0.75f, 0.5f), CoatNames[index % CoatNames.Length]);
            Primitive("Head", PrimitiveType.Sphere, visual, new Vector3(0f, 1.65f, 0f), Vector3.one * 0.4f, "Skin");
            Primitive("Face", PrimitiveType.Cube, visual, new Vector3(0f, 1.65f, 0.18f), new Vector3(0.2f, 0.07f, 0.08f), "Face");
            return go.AddComponent<TrafficPedestrian>();
        }

        private static void Primitive(string name, PrimitiveType kind, Transform parent, Vector3 position, Vector3 scale, string material)
        {
            var go = GameObject.CreatePrimitive(kind);
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/Materials/" + material + ".mat");
        }

        private static void Validate(List<PedestrianPath> paths)
        {
            for (int p = 0; p < paths.Count; p++)
            {
                var path = paths[p];
                if (path.points.Length < 2 || path.length <= 0f || path.reverse < 0 || path.reverse >= paths.Count)
                    throw new InvalidOperationException("Invalid pedestrian path " + p);
                foreach (int next in path.next)
                    if (next < 0 || next >= paths.Count || Vector3.Distance(path.points[path.points.Length - 1], paths[next].points[0]) > 0.1f)
                        throw new InvalidOperationException("Disconnected pedestrian path " + p);
            }
        }
    }
}
