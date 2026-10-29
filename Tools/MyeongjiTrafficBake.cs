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
    public static class MyeongjiTrafficBake
    {
        private const string RootName = "Traffic";
        private const string PrefabFolder = "Assets/_Game/Prefabs/Traffic";
        private const string MaterialFolder = "Assets/_Game/Art/Props";
        // Geometry rules in metres (test values).
        private const float EndpointReach = 4f;      // a road end this close to another road joins it
        private const float MergeRadius = 3f;       // junction candidates closer than this are one junction
        private const float TouchReach = 3f;         // a road passes through a junction within its half width + this
        private const float StopBack = 3.5f;         // lanes stop this far beyond the other roads' carriageway edge
        private const float MinimumEdge = 4f;
        private const float StraightLimit = 14f, TurnLimit = 7f, UTurnLimit = 5f;

        private sealed class Node { public Vector2 center; public bool deadEnd; public readonly List<(int road, float along)> touches = new List<(int, float)>(); }
        private sealed class Lane { public int edge, startNode, endNode, path; public List<Vector2> flat; }

        public static string Apply()
        {
            if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new Exception("Edit mode required.");
            var scene=SceneManager.GetActiveScene();
            if(scene.path!="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity"||scene.isDirty)throw new Exception("Save the Myeongji scene first.");
            const string backup="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji_BeforeTraffic.unity";
            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(backup)==null&&!AssetDatabase.CopyAsset(scene.path,backup))throw new Exception("Backup failed.");
            var roots=scene.GetRootGameObjects();
            var session=roots.Single(g=>g.name=="DeliverySession").GetComponent<DeliverySession>();
            var ground=roots.Single(g=>g.name=="MyeongjiRoutes").GetComponent<RoadNetwork>();
            var method=typeof(RoadPath).GetMethod("FromSurface",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
            var roads=ground.Roads.Where(r=>r.HalfWidth>=3).Select(r=>(RoadPath)method.Invoke(null,new object[]{r})).ToList();
            var nodes=FindJunctions(roads);var edges=SplitEdges(roads,nodes);var paths=new List<TrafficPath>();var lanes=new List<Lane>();
            for(int e=0;e<edges.Count;e++){var edge=edges[e];lanes.Add(MakeLane(roads[edge.road],edge.a0,edge.a1,e,edge.n0,edge.n1,ground,paths));lanes.Add(MakeLane(roads[edge.road],edge.a1,edge.a0,e,edge.n1,edge.n0,ground,paths));}
            BuildConnectors(nodes,lanes,ground,paths);
            // Validate the largest retained vehicle's swept footprint against solid scenery.
            Physics.SyncTransforms();var hits=new List<string>();
            for(int i=0;i<paths.Count;i++)for(int j=1;j<paths[i].points.Length;j++){
                var a=paths[i].points[j-1];var b=paths[i].points[j];var q=Quaternion.LookRotation(b-a);
                foreach(var c in Physics.OverlapBox((a+b)*.5f+Vector3.up*1.2f,new Vector3(1.02f,.85f,2.5f),q,~0,QueryTriggerInteraction.Ignore))
                    if(c.attachedRigidbody==null&&!c.transform.IsChildOf(roots.Single(g=>g.name=="MyeongjiMap").transform.Find("Ground")))hits.Add(i+":"+c.name);
            }
            if(hits.Count>0)throw new Exception("Vehicle clearance: "+string.Join(",",hits.Distinct().Take(12)));
            if(paths.Any(p=>p.next.Length==0)||Reachable(paths)!=paths.Count)throw new Exception("Disconnected traffic paths.");
            var network=roots.Single(g=>g.name=="Traffic").GetComponent<TrafficNetwork>();
            var kinds=(TrafficNetwork.VehicleKind[])typeof(TrafficNetwork).GetField("kinds",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(network);
            network.Configure(session,paths.ToArray(),nodes.Select(n=>new TrafficJunctionData{center=ground.OnGround(new Vector3(n.center.x,0,n.center.y),.02f)}).ToArray(),kinds.Where(k=>k.prefab!=null&&k.prefab.Length<=5).ToArray());
            var so=new SerializedObject(network);so.FindProperty("vehicleCount").intValue=12;so.ApplyModifiedPropertiesWithoutUndo();
            network.gameObject.SetActive(true);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            return "Saved "+paths.Count+" connected traffic paths, "+nodes.Count+" junctions, 12 vehicles, no buses.";
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
                else { start = road.Id == "main" ? 4f : 0f; startNode = DeadEnd(nodes, road, start); }
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
            float reach = Vector2.Distance(p0, p3) * (from.edge == to.edge ? 0.67f : 0.65f);
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
            var points = flat.Select(p => ground.OnGround(new Vector3(p.x, 0f, p.y), 0.02f)).ToArray();
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

    }
}

