using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    // Places the houses of Assets/_Game/Prefabs/Map.prefab on the four-district terrain in three passes:
    //   1. Real positions: every prefab house moved with the fitted map transform, kept where it fits.
    //   2. Frontage: free street edges (main roads and alleys) lined with the same house models facing the street.
    //   3. Interior: remaining block space inside the play area filled on a grid.
    // The Map prefab is the real street layout; the four-district map is the trial build's simplified, enlarged copy.
    // Road clearance and street frontage follow the scene's Roads splines (RoadPath), so edited roads are respected.
    // The map transform was fitted by matching the four-district main-road centrelines to the prefab asphalt
    // (chamfer distance search, 2026-10-14; mean centreline distance 3.2 prefab metres). Houses keep their original size,
    // because the enlarged house size (x1.61) left almost no house inside the blocks.
    // Every house keeps roads, sidewalks, delivery points, hazard slots and existing landmarks clear. The root is rebuilt on every run.
    public static class NeighborhoodBuildingInstaller
    {
        private const string MapPrefabPath = "Assets/_Game/Prefabs/Map.prefab";
        private const string RootName = "NeighborhoodBuildings";
        // district (x, z) = MapScale * R(RotationDegrees) * prefab (x, z) + Offset, rotation counter-clockwise from +x toward +z.
        private const float MapScale = 1.61f;
        private const float RotationDegrees = -4f;
        private static readonly Vector2 Offset = new Vector2(59.444f, 72.086f);
        private const float HouseScale = 1f;
        // Clearances and spacing in district metres (test values).
        private const float MainRoadClearance = 3f;     // sidewalk
        private const float AlleyClearance = 0.8f;
        private const float DeliveryPointClearance = 7f;
        private const float HazardSlotClearance = 4.5f;
        private const float HouseGap = 1.2f;
        private const float InteriorStep = 9f;
        private const float GroundSink = 0.15f;
        private const int Seed = 7;

        private sealed class Context
        {
            public DistrictLayout.Layout Layout;
            public List<RoadPath> Roads;
            public float MinX, MinZ, MaxX, MaxZ;
            public List<Vector3> KeepClear;
            public List<Vector3> Slots;
            public List<Vector2[]> Placed = new List<Vector2[]>();
            public List<Vector2> PlayArea;
            public Transform Parent;
            public Dictionary<string, int> Reasons = new Dictionary<string, int>();
        }

        [MenuItem("Delivery Rider/Apply Neighborhood Buildings")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Stop Play Mode and finish compilation first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene changes first.");
            var map = AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefabPath);
            var houseGroup = map != null ? map.transform.Find("House") : null;
            if (houseGroup == null) throw new InvalidOperationException("Map prefab with a House group is required.");
            var templates = houseGroup.Cast<Transform>().ToArray();

            var scene = EditorSceneManager.OpenScene(DistrictLayout.ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var old = roots.FirstOrDefault(g => g.name == RootName);
            if (old != null)
            {
                UnityEngine.Object.DestroyImmediate(old);
                roots = scene.GetRootGameObjects();
            }

            var layout = DistrictLayout.ReadLayout();
            var ctx = new Context
            {
                Layout = layout,
                MinX = layout.minX + 2f, MinZ = layout.minZ + 2f,
                MaxX = layout.minX + layout.nx - 3f, MaxZ = layout.minZ + layout.nz - 3f,
                KeepClear = new List<Vector3>(),
                Slots = roots.SelectMany(g => g.GetComponentsInChildren<HazardSlot>(true)).Select(s => s.transform.position).ToList(),
                Roads = RoadPath.FromScene(scene),
                Parent = new GameObject(RootName).transform,
            };
            ctx.PlayArea = Hull(ctx.Roads.Where(r => !r.Alley).SelectMany(r => r.Points));
            var locations = roots.SelectMany(g => g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "DeliveryLocations");
            if (locations != null) foreach (Transform point in locations) ctx.KeepClear.Add(point.position);
            Physics.SyncTransforms();

            // 1. Real positions.
            float cos = Mathf.Cos(RotationDegrees * Mathf.Deg2Rad), sin = Mathf.Sin(RotationDegrees * Mathf.Deg2Rad);
            int real = 0;
            foreach (var source in templates)
            {
                var p = source.position;
                var center = new Vector2(MapScale * (cos * p.x - sin * p.z) + Offset.x, MapScale * (sin * p.x + cos * p.z) + Offset.y);
                if (TryPlace(ctx, source, center, source.eulerAngles.y - RotationDegrees, false)) real++;
            }

            // 2. Frontage along every road edge, both sides.
            var random = new System.Random(Seed);
            int frontage = 0;
            foreach (var road in ctx.Roads)
            {
                float edge = road.HalfWidth + Clearance(road) + 0.3f;
                foreach (int side in new[] { -1, 1 })
                {
                    float t = 2f;
                    while (t < road.Length - 2f)
                    {
                        var template = templates[random.Next(templates.Length)];
                        var size = new Vector2(template.lossyScale.x, template.lossyScale.z) * HouseScale;
                        float middle = t + size.x * 0.5f;
                        var along = road.DirectionAt(middle);
                        var normal = new Vector2(-along.y, along.x);
                        var facing = -normal * side;   // the house's local +z points at the street
                        var center = road.PointAt(middle) + normal * side * (edge + size.y * 0.5f);
                        float yaw = Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg;
                        if (TryPlace(ctx, template, center, yaw, true)) { frontage++; t += size.x + HouseGap; }
                        else t += 3f;
                    }
                }
            }

            // 3. Interior grid inside the play area, aligned with the nearest main road.
            int interior = 0;
            var bounds = new Rect(ctx.PlayArea.Min(p => p.x), ctx.PlayArea.Min(p => p.y), 0f, 0f);
            bounds.xMax = ctx.PlayArea.Max(p => p.x);
            bounds.yMax = ctx.PlayArea.Max(p => p.y);
            for (float x = bounds.xMin; x <= bounds.xMax; x += InteriorStep)
                for (float z = bounds.yMin; z <= bounds.yMax; z += InteriorStep)
                {
                    var center = new Vector2(x, z);
                    if (!InsidePolygon(ctx.PlayArea, center)) continue;
                    var template = templates[random.Next(templates.Length)];
                    if (TryPlace(ctx, template, center, NearestMainRoadYaw(ctx.Roads, center), true)) interior++;
                }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the neighborhood buildings.");
            Debug.Log($"Neighborhood buildings: real positions {real} of {templates.Length}, frontage {frontage}, interior {interior}; " +
                "left out: " + string.Join(", ", ctx.Reasons.Select(r => $"{r.Key} {r.Value}")) + ". Play Mode not started.");
        }

        private static bool TryPlace(Context ctx, Transform template, Vector2 center, float yaw, bool fill)
        {
            var size = template.lossyScale * HouseScale;
            var corners = Corners(center, new Vector2(size.x, size.z) * 0.5f, yaw);
            string reason = Reject(ctx, corners, fill);
            if (reason == null)
            {
                float ground = corners.Concat(new[] { center }).Min(c => DistrictLayout.Height(ctx.Layout, c.x, c.y));
                var house = UnityEngine.Object.Instantiate(template.gameObject, ctx.Parent);
                house.name = (fill ? "Fill_" : "Real_") + template.name;
                house.transform.SetPositionAndRotation(new Vector3(center.x, ground - GroundSink + size.y * 0.5f, center.y), Quaternion.Euler(0f, yaw, 0f));
                house.transform.localScale = size;
                Physics.SyncTransforms();
                if (OverlapsSceneObject(house)) { UnityEngine.Object.DestroyImmediate(house); reason = "overlaps landmark or prop"; }
                else
                {
                    foreach (var t in house.GetComponentsInChildren<Transform>(true))
                        GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                    ctx.Placed.Add(corners);
                    return true;
                }
            }
            // Fill attempts are expected to fail often; only real-position results are reported.
            if (!fill) ctx.Reasons[reason] = ctx.Reasons.TryGetValue(reason, out int n) ? n + 1 : 1;
            return false;
        }

        private static string Reject(Context ctx, Vector2[] corners, bool fill)
        {
            if (corners.Any(c => c.x < ctx.MinX || c.x > ctx.MaxX || c.y < ctx.MinZ || c.y > ctx.MaxZ)) return "outside terrain";
            var area = Rect.MinMaxRect(corners.Min(c => c.x), corners.Min(c => c.y), corners.Max(c => c.x), corners.Max(c => c.y));
            foreach (var road in ctx.Roads)
            {
                float clearance = road.HalfWidth + Clearance(road);
                if (!road.Near(area, clearance)) continue;
                var points = road.Points;
                for (int i = 1; i < points.Length; i++)
                {
                    var a = points[i - 1];
                    var b = points[i];
                    if (Mathf.Max(a.x, b.x) < area.xMin - clearance || Mathf.Min(a.x, b.x) > area.xMax + clearance
                        || Mathf.Max(a.y, b.y) < area.yMin - clearance || Mathf.Min(a.y, b.y) > area.yMax + clearance) continue;
                    if (RectSegmentDistance(corners, a, b) < clearance) return road.Alley ? "alley" : "main road or sidewalk";
                }
            }
            if (ctx.KeepClear.Any(p => RectPointDistance(corners, new Vector2(p.x, p.z)) < DeliveryPointClearance)) return "shop, door or home";
            if (ctx.Slots.Any(p => RectPointDistance(corners, new Vector2(p.x, p.z)) < HazardSlotClearance)) return "hazard slot";
            if (ctx.Placed.Any(other => RectsOverlap(corners, other, HouseGap))) return "another house";
            return null;
        }

        // Landmark buildings, delivery props and hazard colliders must stay clear; the terrain mesh and other houses are ignored here.
        private static bool OverlapsSceneObject(GameObject house)
        {
            var box = house.GetComponent<BoxCollider>();
            if (box == null) return false;
            var half = Vector3.Scale(box.size, house.transform.lossyScale) * 0.5f - Vector3.one * 0.1f;
            var center = house.transform.TransformPoint(box.center);
            foreach (var hit in Physics.OverlapBox(center, half, house.transform.rotation, ~0, QueryTriggerInteraction.Collide))
            {
                if (hit is MeshCollider || hit.transform.IsChildOf(house.transform)) continue;
                if (hit.transform.root.name == RootName) continue;
                return true;
            }
            return false;
        }

        // Main roads keep their sidewalk clear; alleys only a small margin.
        private static float Clearance(RoadPath road) => road.Alley ? AlleyClearance : Mathf.Max(MainRoadClearance, road.SidewalkWidth);

        private static float NearestMainRoadYaw(List<RoadPath> roads, Vector2 p)
        {
            float best = float.PositiveInfinity, yaw = 0f;
            foreach (var road in roads)
            {
                if (road.Alley) continue;
                float d = road.Nearest(p, out float along);
                if (d >= best) continue;
                best = d;
                var dir = road.DirectionAt(along);
                yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            }
            return yaw;
        }

        private static Vector2[] Corners(Vector2 center, Vector2 half, float yawDegrees)
        {
            float r = yawDegrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            var result = new Vector2[4];
            var signs = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            for (int i = 0; i < 4; i++)
            {
                float lx = signs[i].x * half.x, lz = signs[i].y * half.y;
                // Unity yaw: local x -> (cos, -sin), local z -> (sin, cos) in (x, z)
                result[i] = center + new Vector2(lx * c + lz * s, -lx * s + lz * c);
            }
            return result;
        }

        // Separating axis test on the four edge normals; rectangles closer than gap count as overlapping.
        private static bool RectsOverlap(Vector2[] a, Vector2[] b, float gap)
        {
            foreach (var rect in new[] { a, b })
                for (int i = 0; i < 2; i++)
                {
                    var edge = rect[i + 1] - rect[i];
                    var axis = new Vector2(-edge.y, edge.x).normalized;
                    float minA = float.PositiveInfinity, maxA = float.NegativeInfinity, minB = float.PositiveInfinity, maxB = float.NegativeInfinity;
                    foreach (var p in a) { float v = Vector2.Dot(p, axis); minA = Mathf.Min(minA, v); maxA = Mathf.Max(maxA, v); }
                    foreach (var p in b) { float v = Vector2.Dot(p, axis); minB = Mathf.Min(minB, v); maxB = Mathf.Max(maxB, v); }
                    if (maxA + gap <= minB || maxB + gap <= minA) return false;
                }
            return true;
        }

        private static float RectPointDistance(Vector2[] rect, Vector2 p)
        {
            if (InsidePolygon(rect, p)) return 0f;
            float best = float.PositiveInfinity;
            for (int i = 0; i < 4; i++) best = Mathf.Min(best, PointSegment(p, rect[i], rect[(i + 1) % 4]));
            return best;
        }

        private static float RectSegmentDistance(Vector2[] rect, Vector2 a, Vector2 b)
        {
            if (InsidePolygon(rect, a) || InsidePolygon(rect, b)) return 0f;
            float best = float.PositiveInfinity;
            for (int i = 0; i < 4; i++)
            {
                var c = rect[i];
                var d = rect[(i + 1) % 4];
                if (Intersects(a, b, c, d)) return 0f;
                best = Mathf.Min(best, Mathf.Min(Mathf.Min(PointSegment(a, c, d), PointSegment(b, c, d)), Mathf.Min(PointSegment(c, a, b), PointSegment(d, a, b))));
            }
            return best;
        }

        // Convex polygon test (works for the house rectangles and the convex play-area hull).
        private static bool InsidePolygon(IList<Vector2> polygon, Vector2 p)
        {
            bool? sign = null;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                bool side = Cross(b - a, p - a) >= 0f;
                if (sign == null) sign = side;
                else if (side != sign) return false;
            }
            return true;
        }

        private static List<Vector2> Hull(IEnumerable<Vector2> points)
        {
            var sorted = points.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
            var hull = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                foreach (var p in sorted)
                {
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], p - hull[hull.Count - 2]) <= 0f) hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
                sorted.Reverse();
            }
            return hull;
        }

        private static bool Intersects(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = Cross(b - a, c - a), d2 = Cross(b - a, d - a), d3 = Cross(d - c, a - c), d4 = Cross(d - c, b - c);
            return d1 * d2 < 0f && d3 * d4 < 0f;
        }

        private static float PointSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
