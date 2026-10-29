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
    public static class MyeongjiPedestrianBake
    {
        private const float SampleStep = 1f, RoadMargin = 0.7f, ConnectorReach = 6f, FeetLift = 0.02f;
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
            foreach(var road in roads.Where(r=>r.HalfWidth>=2))foreach(int side in new[]{-1,1})BakeSide(road,side,roads,ground,routes);
            LinkCorners(routes,roads,ground);Validate(routes);
            if(routes.Count==0)throw new Exception("No safe sidewalk paths.");
            var network=roots.Single(g=>g.name=="Pedestrians").GetComponent<PedestrianNetwork>();
            var data=new SerializedObject(network);var coat=data.FindProperty("coats");var coats=Enumerable.Range(0,coat.arraySize).Select(i=>(Material)coat.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
            network.Configure(session,routes.ToArray(),AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Traffic/Pedestrian.prefab").GetComponent<TrafficPedestrian>(),coats);
            data.Update();data.FindProperty("population").intValue=24;data.ApplyModifiedPropertiesWithoutUndo();network.gameObject.SetActive(true);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            return "Saved "+routes.Count+" pedestrian paths ("+routes.Count(p=>p.connector)+" corner links), population 24; no road crossings.";
        }
        static MeshCollider floor;static Renderer floorRenderer;static int[] submeshEnds;

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

        private static bool Walkable(Vector2 flat,List<RoadPath> roads,RoadNetwork ground,out Vector3 feet)
        {
            feet=Vector3.zero;
            if(!floor.Raycast(new Ray(new Vector3(flat.x,25,flat.y),Vector3.down),out var hit,40))return false;
            int sub=Array.FindIndex(submeshEnds,last=>hit.triangleIndex<last);
            if(sub<0||!floorRenderer.sharedMaterials[sub].name.Contains("Pavement"))return false;
            feet=hit.point+Vector3.up*FeetLift;
            foreach(var road in roads)if(road.Nearest(flat,out _) <road.HalfWidth+.7f)return false;
            int count=Physics.OverlapCapsuleNonAlloc(feet+Vector3.up*.75f,feet+Vector3.up*1.4f,.65f,Obstacles,~0,QueryTriggerInteraction.Ignore);
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
