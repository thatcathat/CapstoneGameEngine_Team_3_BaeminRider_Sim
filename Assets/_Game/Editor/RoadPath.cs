using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    // A road spline from the scene's RoadNetwork, sampled into a flat (x, z) polyline for editor placement code.
    // Installers that must follow hand-edited roads (hazard slots, neighbourhood buildings) read these instead of
    // the baked district_geometry.json polylines.
    public sealed class RoadPath
    {
        private const float Spacing = 1f;
        public string Id { get; private set; }
        public bool Alley { get; private set; }
        public float HalfWidth { get; private set; }
        public float SidewalkWidth { get; private set; }
        public Vector2[] Points { get; private set; }
        public float Length { get; private set; }
        public Rect Bounds { get; private set; }
        private float[] cumulative;

        public static List<RoadPath> FromScene(Scene scene)
        {
            RoadNetwork network = null;
            foreach (var root in scene.GetRootGameObjects())
                if ((network = root.GetComponentInChildren<RoadNetwork>(true)) != null) break;
            if (network == null) throw new InvalidOperationException("The scene has no Roads (RoadNetwork). Run Apply Road Splines first.");
            var paths = new List<RoadPath>();
            foreach (var road in network.Roads) paths.Add(FromSurface(road));
            return paths;
        }

        private static RoadPath FromSurface(RoadSurface road)
        {
            var container = road.Container;
            float length = container.CalculateLength();
            int samples = Mathf.Max(2, Mathf.CeilToInt(length / Spacing) + 1);
            var points = new Vector2[samples];
            var cumulative = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                Vector3 p = container.EvaluatePosition(i / (float)(samples - 1));
                points[i] = new Vector2(p.x, p.z);
                cumulative[i] = i == 0 ? 0f : cumulative[i - 1] + Vector2.Distance(points[i - 1], points[i]);
            }
            var min = points[0];
            var max = points[0];
            foreach (var p in points) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            return new RoadPath
            {
                Id = road.name.StartsWith("Road_") ? road.name.Substring(5) : road.name,
                Alley = road.Kind == RoadKind.Alley,
                HalfWidth = road.HalfWidth,
                SidewalkWidth = road.SidewalkWidth,
                Points = points,
                cumulative = cumulative,
                Length = cumulative[samples - 1],
                Bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y),
            };
        }

        public Vector2 PointAt(float distance)
        {
            int i = Segment(distance);
            float span = Mathf.Max(0.0001f, cumulative[i + 1] - cumulative[i]);
            return Vector2.Lerp(Points[i], Points[i + 1], (distance - cumulative[i]) / span);
        }

        public Vector2 DirectionAt(float distance)
        {
            int i = Segment(distance);
            return (Points[i + 1] - Points[i]).normalized;
        }

        // Distance along the road of the point nearest to p, and the horizontal distance to it.
        public float Nearest(Vector2 p, out float along)
        {
            float best = float.PositiveInfinity;
            along = 0f;
            for (int i = 1; i < Points.Length; i++)
            {
                var a = Points[i - 1];
                var ab = Points[i] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
                float d = Vector2.Distance(p, a + ab * t);
                if (d >= best) continue;
                best = d;
                along = cumulative[i - 1] + (cumulative[i] - cumulative[i - 1]) * t;
            }
            return best;
        }

        // True when a box around the road (grown by `margin`) can touch the given rectangle; a cheap pre-check.
        public bool Near(Rect area, float margin)
        {
            var b = Bounds;
            return area.xMax >= b.xMin - margin && area.xMin <= b.xMax + margin && area.yMax >= b.yMin - margin && area.yMin <= b.yMax + margin;
        }

        private int Segment(float distance)
        {
            distance = Mathf.Clamp(distance, 0f, Length);
            int i = Array.BinarySearch(cumulative, distance);
            if (i < 0) i = ~i - 1;
            return Mathf.Clamp(i, 0, Points.Length - 2);
        }
    }
}
