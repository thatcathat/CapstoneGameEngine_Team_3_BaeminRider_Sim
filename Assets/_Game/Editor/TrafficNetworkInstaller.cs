using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    // Bakes the four-district traffic network from the scene's main road splines and replaces the pilot traffic:
    //   junctions where main roads meet or cross, two right-hand lanes per road edge between junctions,
    //   straight/left/right connectors inside each junction, and U-turns at dead ends.
    // Vehicle prefabs (car, taxi, van, bus) are generated as simple shapes. Re-run after editing the roads.
    public static class TrafficNetworkInstaller
    {
        private const string RootName = "Traffic";
        private const string PrefabFolder = "Assets/_Game/Prefabs/Traffic";
        private const string MaterialFolder = "Assets/_Game/Art/Props";
        // Geometry rules in metres (test values).
        private const float EndpointReach = 4f;      // a road end this close to another road joins it
        private const float MergeRadius = 14f;       // junction candidates closer than this are one junction
        private const float TouchReach = 3f;         // a road passes through a junction within its half width + this
        private const float StopBack = 3.5f;         // lanes stop this far beyond the other roads' carriageway edge
        private const float MinimumEdge = 4f;
        private const float StraightLimit = 14f, TurnLimit = 7f, UTurnLimit = 5f;

        private sealed class Node { public Vector2 center; public bool deadEnd; public readonly List<(int road, float along)> touches = new List<(int, float)>(); }
        private sealed class Lane { public int edge, startNode, endNode, path; public List<Vector2> flat; }

        [MenuItem("Delivery Rider/Apply Traffic Network")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Stop Play Mode and finish compilation first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene changes first.");
            var scene = EditorSceneManager.OpenScene(DistrictLayout.ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var session = roots.SelectMany(g => g.GetComponentsInChildren<DeliverySession>(true)).Single();
            var roadNetwork = roots.SelectMany(g => g.GetComponentsInChildren<RoadNetwork>(true)).Single();
            var roads = RoadPath.FromScene(scene).Where(r => !r.Alley).ToList();

            // The pilot lanes and vans are replaced by the network.
            var world = roots.Single(g => g.name == "FourDistrictWorld").transform;
            foreach (var old in world.Cast<Transform>().Where(t => t.name.StartsWith("TrafficLane_") || t.name.StartsWith("Traffic_")).ToList())
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            var previous = roots.FirstOrDefault(g => g.name == RootName);
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous);

            var nodes = FindJunctions(roads);
            var edges = SplitEdges(roads, nodes);
            var paths = new List<TrafficPath>();
            var lanes = new List<Lane>();
            for (int e = 0; e < edges.Count; e++)
            {
                var (road, a0, a1, n0, n1) = edges[e];
                lanes.Add(MakeLane(roads[road], a0, a1, e, n0, n1, roadNetwork, paths));
                lanes.Add(MakeLane(roads[road], a1, a0, e, n1, n0, roadNetwork, paths));
            }
            int connectors = BuildConnectors(nodes, lanes, roadNetwork, paths);

            var junctions = nodes.Select(n => new TrafficJunctionData { center = roadNetwork.OnGround(new Vector3(n.center.x, 0f, n.center.y), 0.1f) }).ToArray();
            var root = new GameObject(RootName);
            var network = root.AddComponent<TrafficNetwork>();
            network.Configure(session, paths.ToArray(), junctions, VehicleKinds());
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the traffic network.");

            int deadEnds = nodes.Count(n => n.deadEnd);
            int stuck = paths.Count(p => p.next.Length == 0);
            int reachable = Reachable(paths);
            Debug.Log($"Traffic network: {nodes.Count - deadEnds} junctions, {deadEnds} dead ends, {edges.Count} road edges, {lanes.Count} lanes, " +
                $"{connectors} connectors; paths without exit {stuck}; reachable from the first lane {reachable}/{paths.Count}. Play Mode not started.");
        }

        private static List<Node> FindJunctions(List<RoadPath> roads)
        {
            var candidates = new List<Vector2>();
            for (int a = 0; a < roads.Count; a++)
            {
                foreach (var end in new[] { roads[a].Points[0], roads[a].Points[roads[a].Points.Length - 1] })
                    for (int b = 0; b < roads.Count; b++)
                    {
                        if (b == a) continue;
                        if (roads[b].Nearest(end, out float along) < roads[b].HalfWidth + EndpointReach) candidates.Add(roads[b].PointAt(along));
                    }
                for (int b = a + 1; b < roads.Count; b++) candidates.AddRange(Crossings(roads[a], roads[b]));
            }
            var nodes = new List<Node>();
            var counts = new List<int>();
            foreach (var c in candidates)
            {
                int hit = nodes.FindIndex(n => Vector2.Distance(n.center, c) < MergeRadius);
                if (hit < 0) { nodes.Add(new Node { center = c }); counts.Add(1); continue; }
                counts[hit]++;
                nodes[hit].center += (c - nodes[hit].center) / counts[hit];
            }
            foreach (var node in nodes)
                for (int r = 0; r < roads.Count; r++)
                    if (roads[r].Nearest(node.center, out float along) < roads[r].HalfWidth + TouchReach) node.touches.Add((r, along));
            return nodes.Where(n => n.touches.Select(t => t.road).Distinct().Count() >= 2).ToList();
        }

        private static IEnumerable<Vector2> Crossings(RoadPath a, RoadPath b)
        {
            if (!a.Near(b.Bounds, 1f)) yield break;
            for (int i = 1; i < a.Points.Length; i++)
                for (int j = 1; j < b.Points.Length; j++)
                    if (Intersect(a.Points[i - 1], a.Points[i], b.Points[j - 1], b.Points[j], out var p)) yield return p;
        }

        // Splits each road into edges between the junctions it passes; open road ends become dead-end nodes (U-turns).
        private static List<(int road, float a0, float a1, int n0, int n1)> SplitEdges(List<RoadPath> roads, List<Node> nodes)
        {
            var edges = new List<(int, float, float, int, int)>();
            for (int r = 0; r < roads.Count; r++)
            {
                var road = roads[r];
                var events = new List<(float along, int node, float cut)>();
                for (int n = 0; n < nodes.Count; n++)
                {
                    if (nodes[n].deadEnd) continue;
                    foreach (var touch in nodes[n].touches)
                    {
                        if (touch.road != r) continue;
                        float other = nodes[n].touches.Where(t => t.road != r).Select(t => roads[t.road].HalfWidth).DefaultIfEmpty(0f).Max();
                        events.Add((touch.along, n, other + StopBack));
                        break;
                    }
                }
                events.Sort((x, y) => x.along.CompareTo(y.along));
                float start = 0f;
                int startNode = -1;
                int index = 0;
                if (events.Count > 0 && events[0].along - events[0].cut <= 1.5f)
                {
                    start = events[0].along + events[0].cut;
                    startNode = events[0].node;
                    index = 1;
                }
                else startNode = DeadEnd(nodes, road, 0f);
                for (; index < events.Count; index++)
                {
                    var e = events[index];
                    float end = e.along - e.cut;
                    if (end - start >= MinimumEdge) edges.Add((r, start, end, startNode, e.node));
                    start = e.along + e.cut;
                    startNode = e.node;
                }
                if (road.Length - start >= MinimumEdge) edges.Add((r, start, road.Length, startNode, DeadEnd(nodes, road, road.Length)));
            }
            return edges;
        }

        private static int DeadEnd(List<Node> nodes, RoadPath road, float along)
        {
            nodes.Add(new Node { center = road.PointAt(along), deadEnd = true });
            return nodes.Count - 1;
        }

        // Right-hand traffic: each lane runs half a carriageway to the right of the road centre line.
        private static Lane MakeLane(RoadPath road, float from, float to, int edge, int startNode, int endNode, RoadNetwork ground, List<TrafficPath> paths)
        {
            float offset = road.HalfWidth * 0.5f;
            int samples = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(to - from)) + 1);
            var flat = new List<Vector2>(samples);
            for (int i = 0; i < samples; i++)
            {
                float along = Mathf.Lerp(from, to, i / (float)(samples - 1));
                var dir = road.DirectionAt(along) * Mathf.Sign(to - from);
                flat.Add(road.PointAt(along) + new Vector2(dir.y, -dir.x) * offset);
            }
            var lane = new Lane { edge = edge, startNode = startNode, endNode = endNode, path = paths.Count, flat = flat };
            paths.Add(Path(flat, ground, -1, 100f));
            return lane;
        }

        private static int BuildConnectors(List<Node> nodes, List<Lane> lanes, RoadNetwork ground, List<TrafficPath> paths)
        {
            int count = 0;
            for (int n = 0; n < nodes.Count; n++)
            {
                var incoming = lanes.Where(l => l.endNode == n).ToList();
                var outgoing = lanes.Where(l => l.startNode == n).ToList();
                foreach (var inLane in incoming)
                {
                    var next = new List<int>();
                    var weights = new List<float>();
                    var inDir = (inLane.flat[inLane.flat.Count - 1] - inLane.flat[inLane.flat.Count - 2]).normalized;
                    foreach (var outLane in outgoing)
                    {
                        bool uTurn = outLane.edge == inLane.edge;
                        if (uTurn && !nodes[n].deadEnd) continue;
                        var outDir = (outLane.flat[1] - outLane.flat[0]).normalized;
                        float angle = Vector2.SignedAngle(inDir, outDir);
                        if (!uTurn && Mathf.Abs(angle) > 150f) continue;
                        float limit = uTurn ? UTurnLimit : Mathf.Abs(angle) < 30f ? StraightLimit : TurnLimit;
                        next.Add(AddConnector(inLane, outLane, inDir, outDir, n, limit, ground, paths));
                        weights.Add(!uTurn && Mathf.Abs(angle) < 30f ? 3f : 1f);
                        count++;
                    }
                    if (next.Count == 0)
                    {
                        // A junction with no way on: turn back rather than stop forever.
                        var back = outgoing.FirstOrDefault(l => l.edge == inLane.edge);
                        if (back != null)
                        {
                            next.Add(AddConnector(inLane, back, inDir, (back.flat[1] - back.flat[0]).normalized, n, UTurnLimit, ground, paths));
                            weights.Add(1f);
                            count++;
                        }
                    }
                    paths[inLane.path].next = next.ToArray();
                    paths[inLane.path].nextWeights = weights.ToArray();
                }
            }
            return count;
        }

        private static int AddConnector(Lane from, Lane to, Vector2 inDir, Vector2 outDir, int junction, float limit, RoadNetwork ground, List<TrafficPath> paths)
        {
            var p0 = from.flat[from.flat.Count - 1];
            var p3 = to.flat[0];
            // Handle length relative to the chord: ~quarter-circle for turns, ~semicircle for U-turns. A fixed
            // minimum made short right turns loop back on themselves.
            float reach = Vector2.Distance(p0, p3) * (from.edge == to.edge ? 0.67f : 0.4f);
            var p1 = p0 + inDir * reach;
            var p2 = p3 - outDir * reach;
            float approx = Vector2.Distance(p0, p1) + Vector2.Distance(p1, p2) + Vector2.Distance(p2, p3);
            int samples = Mathf.Max(4, Mathf.CeilToInt(approx));
            var flat = new List<Vector2>(samples + 1);
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples, u = 1f - t;
                flat.Add(u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3);
            }
            var path = Path(flat, ground, junction, limit);
            path.next = new[] { to.path };
            path.nextWeights = new[] { 1f };
            paths.Add(path);
            return paths.Count - 1;
        }

        private static TrafficPath Path(List<Vector2> flat, RoadNetwork ground, int junction, float limit)
        {
            var points = flat.Select(p => ground.OnGround(new Vector3(p.x, 0f, p.y), 0.1f)).ToArray();
            var cumulative = new float[points.Length];
            for (int i = 1; i < points.Length; i++) cumulative[i] = cumulative[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            return new TrafficPath { points = points, cumulative = cumulative, length = cumulative[points.Length - 1], junction = junction, speedLimit = limit };
        }

        private static int Reachable(List<TrafficPath> paths)
        {
            if (paths.Count == 0) return 0;
            var seen = new HashSet<int> { 0 };
            var queue = new Queue<int>();
            queue.Enqueue(0);
            while (queue.Count > 0)
                foreach (int next in paths[queue.Dequeue()].next)
                    if (seen.Add(next)) queue.Enqueue(next);
            return seen.Count;
        }

        private static bool Intersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 point)
        {
            point = default;
            var r = b - a;
            var s = d - c;
            float denominator = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(denominator) < 1e-6f) return false;
            float t = ((c.x - a.x) * s.y - (c.y - a.y) * s.x) / denominator;
            float u = ((c.x - a.x) * r.y - (c.y - a.y) * r.x) / denominator;
            if (t < 0f || t > 1f || u < 0f || u > 1f) return false;
            point = a + r * t;
            return true;
        }

        // ---- vehicle prefabs ----

        private static TrafficNetwork.VehicleKind[] VehicleKinds()
        {
            if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder("Assets/_Game/Prefabs", "Traffic");
            return new[]
            {
                Kind(Vehicle("Car", new Vector3(1.8f, 1.45f, 4.2f), 12f, false, false), 0.5f,
                    new Color(0.82f, 0.2f, 0.2f), new Color(0.2f, 0.35f, 0.75f), new Color(0.78f, 0.79f, 0.8f), new Color(0.15f, 0.16f, 0.18f), new Color(0.95f, 0.95f, 0.93f)),
                Kind(Vehicle("Taxi", new Vector3(1.8f, 1.45f, 4.3f), 13f, true, false), 0.2f, new Color(0.98f, 0.78f, 0.15f)),
                Kind(Vehicle("Van", new Vector3(1.9f, 2f, 4.8f), 10f, false, false), 0.2f, new Color(0.93f, 0.93f, 0.9f), new Color(0.6f, 0.62f, 0.64f)),
                Kind(Vehicle("Bus", new Vector3(2.5f, 3f, 10f), 8f, false, true), 0.1f, new Color(0.2f, 0.6f, 0.35f), new Color(0.2f, 0.45f, 0.75f)),
            };
        }

        private static TrafficNetwork.VehicleKind Kind(TrafficCar prefab, float share, params Color[] colors) =>
            new TrafficNetwork.VehicleKind { prefab = prefab, share = share, colors = colors };

        private static TrafficCar Vehicle(string name, Vector3 size, float cruise, bool taxi, bool bus)
        {
            const float clearance = 0.25f;
            var root = new GameObject(name);
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, clearance + size.y * 0.5f, 0f);
            collider.size = size;
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var car = root.AddComponent<TrafficCar>();

            var paint = Material("VehicleBody", Color.white);
            var glass = Material("Glass", new Color(0.25f, 0.35f, 0.45f));
            var tyre = Material("Tyre", new Color(0.08f, 0.08f, 0.08f));
            var brake = Material("Brake", new Color(0.9f, 0.1f, 0.1f));
            GameObject shell;
            if (bus)
            {
                shell = Part("Body", PrimitiveType.Cube, root.transform, new Vector3(0f, clearance + size.y * 0.5f, 0f), size, paint);
                Part("Windows", PrimitiveType.Cube, root.transform, new Vector3(0f, clearance + size.y * 0.68f, 0.2f), new Vector3(size.x + 0.02f, size.y * 0.28f, size.z * 0.85f), glass);
            }
            else
            {
                float lower = size.y * 0.55f;
                shell = Part("Body", PrimitiveType.Cube, root.transform, new Vector3(0f, clearance + lower * 0.5f, 0f), new Vector3(size.x, lower, size.z), paint);
                Part("Cabin", PrimitiveType.Cube, root.transform, new Vector3(0f, clearance + lower + (size.y - lower) * 0.5f, -size.z * 0.06f),
                    new Vector3(size.x * 0.9f, size.y - lower, size.z * (name == "Van" ? 0.8f : 0.55f)), glass);
            }
            if (taxi) Part("TaxiSign", PrimitiveType.Cube, root.transform, new Vector3(0f, clearance + size.y + 0.12f, -size.z * 0.06f), new Vector3(0.7f, 0.25f, 0.3f), Material("TaxiSign", new Color(1f, 0.95f, 0.8f)));
            for (int i = 0; i < 4; i++)
            {
                var wheel = Part("Wheel", PrimitiveType.Cylinder, root.transform,
                    new Vector3((i % 2 == 0 ? -1 : 1) * size.x * 0.5f, 0.38f, (i < 2 ? 1 : -1) * size.z * 0.33f), new Vector3(0.76f, 0.12f, 0.76f), tyre);
                wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            var lights = Part("BrakeLights", PrimitiveType.Cube, root.transform, new Vector3(0f, clearance + size.y * (bus ? 0.25f : 0.35f), -size.z * 0.5f - 0.02f),
                new Vector3(size.x * 0.8f, 0.14f, 0.04f), brake);
            var lightRenderer = lights.GetComponent<Renderer>();
            lightRenderer.enabled = false;

            var data = new SerializedObject(car);
            data.FindProperty("cruiseSpeed").floatValue = cruise;
            data.FindProperty("braking").floatValue = bus ? 5f : 7f;
            data.FindProperty("acceleration").floatValue = bus ? 1.6f : 2.5f;
            data.FindProperty("followGap").floatValue = bus ? 3.5f : 2.5f;
            data.FindProperty("bodyRenderer").objectReferenceValue = shell.GetComponent<Renderer>();
            data.FindProperty("brakeLights").objectReferenceValue = lightRenderer;
            data.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{name}.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            return prefab.GetComponent<TrafficCar>();
        }

        private static GameObject Part(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            return part;
        }

        private static Material Material(string name, Color color)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find(CurvedWorldInstaller.LitShader)) { name = name, color = color };
            material.SetFloat("_Smoothness", 0.3f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
