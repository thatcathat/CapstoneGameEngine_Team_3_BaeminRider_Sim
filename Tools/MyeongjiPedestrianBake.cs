using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    // Bake existing sidewalk space, clip roads/solid obstructions, link nearby same-corner endpoints. Lanes follow the
    // middle of the paved strip found beside each road (the Myeongji sidewalks are about 1.2 m wide, so a fixed offset
    // ran into the building line on many blocks), along every road. Where the sidewalk is too narrow or a post blocks
    // it, walkers may use the kerb, the open plot and the outer RoadEdge of a road with traffic, but never go deeper, so
    // they cross no such road (cars and walkers pass through each other). Roads too narrow for traffic are streets
    // without a separate sidewalk: walkers use and cross them freely. Pieces of one sidewalk broken by a post or a joint
    // between roads are joined into one lane first, so lanes end at corners. A gap is bridged straight when that stays
    // clear; otherwise lanes meeting at a block corner are linked through the corner point where their lines cross, and
    // a link bends around a lamp or signal post in the way.
    public static class MyeongjiPedestrianBake
    {
        private const float SampleStep = 1f, RoadMargin = 0.7f, ConnectorReach = 6f, CornerReach = 15f, FeetLift = 0.02f;
        private const float LaneClearance = 0.4f, LaneRoadMargin = 0.4f, LinkClearance = 0.3f, DetourReach = 1f, DetourStep = 0.25f;
        // How far walkers may step off the kerb onto a road with traffic where the sidewalk gives no room.
        private const float RoadEdge = 0.5f;
        // Narrower roads carry no traffic (MyeongjiTrafficBake drives roads of this half width and more).
        private const float TrafficHalfWidth = 3f;
        // How far out from the kerb the paved strip is looked for, and in what steps.
        private const float StripSearch = 3f, StripStep = 0.1f;
        private static readonly Collider[] Obstacles = new Collider[64];
        private static readonly string[] CoatNames = { "Resident_0Coat", "Resident_1Coat", "Resident_2Coat", "CustomerCoat" };
        private const string PrefabPath = "Assets/_Game/Prefabs/Traffic/Pedestrian.prefab";
        private const string MaterialFolder = "Assets/_Game/Art/Materials";

        public static string Apply()
        {
            if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new Exception("Edit mode required.");
            var scene=SceneManager.GetActiveScene();
            if(scene.path!="Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity"||scene.isDirty)throw new Exception("Save the Myeongji scene first.");
            var roots=scene.GetRootGameObjects();var ground=roots.Single(g=>g.name=="MyeongjiRoutes").GetComponent<RoadNetwork>();
            var session=roots.Single(g=>g.name=="DeliverySession").GetComponent<DeliverySession>();
            floor=roots.Single(g=>g.name=="MyeongjiMap").transform.Find("Ground").GetComponentInChildren<MeshCollider>();
            floorRenderer=floor.GetComponent<Renderer>();
            submeshEnds=new int[floor.sharedMesh.subMeshCount];int sum=0;for(int i=0;i<submeshEnds.Length;i++){sum+=(int)floor.sharedMesh.GetIndexCount(i)/3;submeshEnds[i]=sum;}
            var method=typeof(RoadPath).GetMethod("FromSurface",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
            var roads=ground.Roads.Select(r=>(RoadPath)method.Invoke(null,new object[]{r})).ToList();
            foreach(var road in roads)typeof(RoadPath).GetProperty("SidewalkWidth").SetValue(road,2.4f);
            var routes=new List<PedestrianPath>();
            foreach(var road in roads)foreach(int side in new[]{-1,1})BakeSide(road,side,roads,ground,routes);
            routes=JoinStraight(routes,roads,ground);LinkCorners(routes,roads,ground);Validate(routes);
            if(routes.Count==0)throw new Exception("No safe sidewalk paths.");
            var network=roots.Single(g=>g.name=="Pedestrians").GetComponent<PedestrianNetwork>();
            var data=new SerializedObject(network);var coat=data.FindProperty("coats");var coats=Enumerable.Range(0,coat.arraySize).Select(i=>(Material)coat.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
            network.Configure(session,routes.ToArray(),AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Traffic/Pedestrian.prefab").GetComponent<TrafficPedestrian>(),coats);
            data.Update();data.FindProperty("population").intValue=24;data.ApplyModifiedPropertiesWithoutUndo();network.gameObject.SetActive(true);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            return "Saved "+routes.Count+" pedestrian paths ("+routes.Count(p=>p.connector)+" corner links), population 24; roads with traffic are not crossed.";
        }
        static MeshCollider floor;static Renderer floorRenderer;static int[] submeshEnds;

        private static void BakeSide(RoadPath road, int side, List<RoadPath> roads, RoadNetwork ground, List<PedestrianPath> routes)
        {
            var fragment = new List<Vector3>();
            bool fragmentLoose = false;
            int count = Mathf.Max(1, Mathf.CeilToInt(road.Length / SampleStep));
            for (int i = 0; i <= count; i++)
            {
                float along = road.Length * i / count;
                var direction = road.DirectionAt(along);
                var outward = new Vector2(direction.y, -direction.x) * side;
                var kerb = road.PointAt(along);
                // The first clear run of pavement out from the kerb, else of the kerb side of the street; the lane takes
                // its middle.
                bool loose = false, found = Strip(kerb, outward, road.HalfWidth + StripStep, road, roads, ground, false, out float middle);
                if (!found) found = loose = Strip(kerb, outward, road.HalfWidth - RoadEdge + StripStep, road, roads, ground, true, out middle);
                var feet = Vector3.zero;
                bool clear = found && Walkable(kerb + outward * middle, roads, ground, out feet, LaneClearance, LaneRoadMargin, loose);
                if (clear && fragment.Count > 0 && !ClearLine(fragment[fragment.Count - 1], feet, roads, ground, LaneClearance, LaneRoadMargin, loose || fragmentLoose)) clear = false;
                fragmentLoose = loose;
                if (!clear) { AddFragment(fragment, road.SidewalkWidth * 0.5f, routes); fragment.Clear(); }
                else fragment.Add(feet);
            }
            AddFragment(fragment, road.SidewalkWidth * 0.5f, routes);
        }

        // Middle of the first clear run of offsets out from the kerb, starting at `from`.
        private static bool Strip(Vector2 kerb, Vector2 outward, float from, RoadPath road, List<RoadPath> roads, RoadNetwork ground, bool loose, out float middle)
        {
            float first = -1f, last = -1f;
            for (float offset = from; offset <= road.HalfWidth + StripSearch; offset += StripStep)
            {
                if (Walkable(kerb + outward * offset, roads, ground, out _, LaneClearance, LaneRoadMargin, loose)) { if (first < 0f) first = offset; last = offset; }
                else if (first >= 0f) break;
            }
            middle = (first + last) * 0.5f;
            return first >= 0f;
        }

        private static void AddFragment(List<Vector3> points, float halfWidth, List<PedestrianPath> routes)
        {
            if (points.Count < 4 || (points[points.Count - 1] - points[0]).sqrMagnitude < 64f) return;
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

        // Lanes that run on straight into another (across a post or a joint) are joined into one lane, both directions.
        private static List<PedestrianPath> JoinStraight(List<PedestrianPath> lanes, List<RoadPath> roads, RoadNetwork ground)
        {
            int count = lanes.Count;
            var successor = Enumerable.Repeat(-1, count).ToArray();
            var predecessor = Enumerable.Repeat(-1, count).ToArray();
            var bridges = new Vector3[count][];
            for (int a = 0; a < count; a++)
            {
                float best = float.PositiveInfinity;
                for (int b = 0; b < count; b++)
                {
                    if (b == a || b == lanes[a].reverse || predecessor[b] >= 0) continue;
                    float span = Vector3.Distance(lanes[a].points[lanes[a].points.Length - 1], lanes[b].points[0]);
                    if (span >= best) continue;
                    var points = Bridge(lanes[a], lanes[b], roads, ground, true);
                    if (points == null) continue;
                    best = span; successor[a] = b; bridges[a] = points;
                }
                if (successor[a] >= 0) predecessor[successor[a]] = a;
            }
            var used = new bool[count];
            var joined = new List<PedestrianPath>();
            void Chain(int head)
            {
                var points = new List<Vector3>(lanes[head].points);
                used[head] = used[lanes[head].reverse] = true;
                for (int current = head; successor[current] >= 0 && !used[successor[current]]; current = successor[current])
                {
                    var bridge = bridges[current];
                    points.AddRange(bridge.Skip(1).Take(bridge.Length - 2));
                    points.AddRange(lanes[successor[current]].points);
                    used[successor[current]] = used[lanes[successor[current]].reverse] = true;
                }
                AddFragment(points, lanes[head].halfWidth, joined);
            }
            for (int a = 0; a < count; a++) if (!used[a] && predecessor[a] < 0) Chain(a);
            for (int a = 0; a < count; a++) if (!used[a]) Chain(a);
            return joined;
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
                    var points = Bridge(source, destination, roads, ground);
                    if (points == null) continue;
                    int connector = routes.Count;
                    var path = MakePath(points, Mathf.Min(source.halfWidth, destination.halfWidth), a, true);
                    path.next = new[] { b };
                    routes.Add(path);
                    next.Add(connector);
                }
                source.next = next.ToArray();
            }
        }

        // Walkable link from the end of one lane to the start of another, or null: straight when it is clear, otherwise
        // through one bend point near the block corner (a turn) or the middle of the gap (along one sidewalk).
        private static Vector3[] Bridge(PedestrianPath source, PedestrianPath destination, List<RoadPath> roads, RoadNetwork ground, bool straightOnly = false)
        {
            var end = source.points[source.points.Length - 1];
            var start = destination.points[0];
            float span = Vector3.Distance(end, start);
            var outgoing = Heading(source.points.AsEnumerable().Reverse().ToArray(), -1f);
            var incoming = Heading(destination.points, 1f);
            float angle = Vector2.Angle(outgoing, incoming);
            if (straightOnly && (angle >= 30f || span >= 0.1f && Vector2.Angle(outgoing, Flat(start - end)) > 20f)) return null;
            // Lanes of two roads that meet at a joint: a short step keeps the link a real path.
            if (span < 0.1f) return angle <= 150f ? new[] { end, end + new Vector3(outgoing.x, 0f, outgoing.y) * 0.05f, start } : null;
            if (span <= ConnectorReach && ClearLine(end, start, roads, ground, LinkClearance, RoadMargin, true)) return Line(ground, end, start);
            Vector2 bend;
            if (angle < 30f)
            {
                // Same sidewalk, interrupted by a post: the gap must run on along the lane.
                if (span > CornerReach || Vector2.Angle(outgoing, Flat(start - end)) > 20f) return null;
                bend = (Flat(end) + Flat(start)) * 0.5f;
            }
            else if (angle <= 150f)
            {
                // Around a block corner: the lines meet ahead of the end and before the start.
                var from = Flat(end); var gap = Flat(start) - from;
                float cross = outgoing.x * incoming.y - outgoing.y * incoming.x;
                float ahead = (gap.x * incoming.y - gap.y * incoming.x) / cross;
                float before = (outgoing.x * gap.y - outgoing.y * gap.x) / cross;
                if (ahead < -0.5f || before < -0.5f || ahead > CornerReach || before > CornerReach) return null;
                bend = from + outgoing * Mathf.Max(0f, ahead);
            }
            else return null;
            // The nearest bend point that keeps both legs on clear pavement.
            foreach (var shift in Detours)
            {
                var flat = bend + shift;
                var point = new Vector3(flat.x, (end.y + start.y) * 0.5f, flat.y);
                if (!ClearLine(end, point, roads, ground, LinkClearance, RoadMargin, true) || !ClearLine(point, start, roads, ground, LinkClearance, RoadMargin, true)) continue;
                var leg = Line(ground, end, point);
                return leg.Take(leg.Length - 1).Concat(Line(ground, point, start)).ToArray();
            }
            return null;
        }

        // Bend point offsets within DetourReach, nearest first.
        private static readonly Vector2[] Detours = Enumerable.Range(-4, 9).SelectMany(x => Enumerable.Range(-4, 9).Select(z => new Vector2(x, z) * DetourStep))
            .Where(v => v.magnitude <= DetourReach + 0.001f).OrderBy(v => v.sqrMagnitude).ToArray();

        private static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        // Direction over the first few metres of a lane (sign -1 for a lane given end first): the last samples near a
        // corner wander as the paved strip narrows.
        private static Vector2 Heading(Vector3[] points, float sign)
        {
            int i = 1;
            while (i < points.Length - 1 && Vector3.Distance(points[0], points[i]) < 3f) i++;
            return (Flat(points[i] - points[0]) * sign).normalized;
        }

        private static Vector3[] Line(RoadNetwork ground, Vector3 from, Vector3 to)
        {
            int count = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(from, to) / 0.4f) + 1);
            var points = new Vector3[count];
            for (int i = 0; i < count; i++) points[i] = ground.OnGround(Vector3.Lerp(from, to, i / (float)(count - 1)), FeetLift);
            // Keep the exact ends so the links meet their lanes.
            points[0] = from; points[count - 1] = to;
            return points;
        }

        private static bool ClearLine(Vector3 from, Vector3 to, List<RoadPath> roads, RoadNetwork ground, float clearance = LaneClearance, float margin = RoadMargin, bool loose = false)
        {
            int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / 0.4f));
            for (int i = 0; i <= count; i++)
            {
                var point = Vector3.Lerp(from, to, i / (float)count);
                if (!Walkable(new Vector2(point.x, point.z), roads, ground, out var feet, clearance, margin, loose) || Mathf.Abs(feet.y - point.y) > 0.6f) return false;
            }
            return true;
        }

        // Strict: paved sidewalk only, the margin clear of every carriageway. Loose: also the kerb, the open plot, the
        // outer RoadEdge of a road with traffic and the whole of a road without.
        private static bool Walkable(Vector2 flat,List<RoadPath> roads,RoadNetwork ground,out Vector3 feet,float clearance=LaneClearance,float margin=RoadMargin,bool loose=false)
        {
            feet=Vector3.zero;
            if(!floor.Raycast(new Ray(new Vector3(flat.x,25,flat.y),Vector3.down),out var hit,40))return false;
            int sub=Array.FindIndex(submeshEnds,last=>hit.triangleIndex<last);
            if(sub<0)return false;var surface=floorRenderer.sharedMaterials[sub].name;
            if(!surface.Contains("Pavement")&&!(loose&&(surface.Contains("Plot")||surface.Contains("Curb")||surface.Contains("Asphalt"))))return false;
            feet=hit.point+Vector3.up*FeetLift;
            foreach(var road in roads)
            {
                if(loose&&road.HalfWidth<TrafficHalfWidth)continue;
                if(road.Nearest(flat,out _)<(loose?road.HalfWidth-RoadEdge:road.HalfWidth+margin))return false;
            }
            int count=Physics.OverlapCapsuleNonAlloc(feet+Vector3.up*.75f,feet+Vector3.up*1.4f,clearance,Obstacles,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)if(Obstacles[i]!=floor&&Obstacles[i].attachedRigidbody==null)return false;
            return count<Obstacles.Length;
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
