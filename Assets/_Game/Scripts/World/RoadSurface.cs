using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace DeliveryRider
{
    public enum RoadKind { Main, Alley }

    // Visual road surface built along this object's spline: sidewalks, carriageway (or alley paving) and centre dashes.
    // Visual only (no colliders); the player walks on the terrain collider. RoadNetwork rebuilds it whenever a spline changes.
    [RequireComponent(typeof(SplineContainer), typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class RoadSurface : MonoBehaviour
    {
        // Sub-mesh order matches the MeshRenderer materials.
        public const int SidewalkSubmesh = 0, CarriagewaySubmesh = 1, MarkingSubmesh = 2;

        [SerializeField] private RoadKind kind;
        [SerializeField, Min(1f)] private float width = 8f;
        [SerializeField, Min(0f)] private float sidewalkWidth = 3f;
        [SerializeField, Min(0.1f)] private float sampleSpacing = 0.5f;
        [Header("Centre dashes (main roads)")]
        [SerializeField, Min(0.1f)] private float dashLength = 3f;
        [SerializeField, Min(0.1f)] private float dashGap = 3f;
        [SerializeField, Min(0.01f)] private float dashWidth = 0.22f;
        [Tooltip("Dashes stop this far from road ends and from other main roads' carriageways.")]
        [SerializeField, Min(0f)] private float junctionClearance = 2f;

        // Layering above the terrain: sidewalk < alley paving < asphalt < markings, so a crossing road's
        // asphalt covers the other road's sidewalk at junctions.
        // Steps of several centimetres keep the layers from flickering against each other at a distance.
        private const float SidewalkLift = 0.04f, AlleyLift = 0.07f, AsphaltLift = 0.1f, MarkingLift = 0.14f;
        private const float LateralSpacing = 2f;

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>() };
        private SplineContainer container;
        // Flat (x, z) bounds of carriageway and sidewalks; a cheap pre-check before nearest-point queries.
        public Bounds Footprint { get; private set; }
        // Shape signature of the last build; selecting a spline also raises change events, which must not rebuild.
        [SerializeField, HideInInspector] private int builtShape;

        public bool ShapeChanged() => ShapeSignature() != builtShape;

        private int ShapeSignature()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + width.GetHashCode();
                hash = hash * 31 + sidewalkWidth.GetHashCode();
                hash = hash * 31 + transform.localToWorldMatrix.GetHashCode();
                foreach (var knot in Container.Spline.Knots) hash = hash * 31 + knot.GetHashCode();
                hash = hash * 31 + Container.Spline.Closed.GetHashCode();
                return hash;
            }
        }

        public RoadKind Kind => kind;
        public float HalfWidth => width * 0.5f;
        public float SidewalkWidth => kind == RoadKind.Main ? sidewalkWidth : 0f;
        public SplineContainer Container => container != null ? container : container = GetComponent<SplineContainer>();

        public void Configure(RoadKind roadKind, float roadWidth, float sidewalk)
        {
            kind = roadKind;
            width = roadWidth;
            sidewalkWidth = sidewalk;
        }

        public void Rebuild(RoadNetwork network)
        {
            var spline = Container.Spline;
            if (spline == null || spline.Count < 2) return;
            float length = Container.CalculateLength();
            int samples = Mathf.Max(2, Mathf.CeilToInt(length / sampleSpacing) + 1);

            vertices.Clear();
            uvs.Clear();
            foreach (var list in triangles) list.Clear();

            var centers = new Vector3[samples];
            var rights = new Vector3[samples];
            var distances = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)(samples - 1);
                centers[i] = (Vector3)Container.EvaluatePosition(t);
                var tangent = (Vector3)Container.EvaluateTangent(t);
                tangent.y = 0f;
                rights[i] = tangent.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, tangent.normalized) : (i > 0 ? rights[i - 1] : Vector3.right);
                distances[i] = i == 0 ? 0f : distances[i - 1] + Vector3.Distance(Flat(centers[i]), Flat(centers[i - 1]));
            }

            float half = HalfWidth;
            if (kind == RoadKind.Main && sidewalkWidth > 0f)
            {
                Strip(network, centers, rights, distances, half, half + sidewalkWidth, SidewalkLift, SidewalkSubmesh);
                Strip(network, centers, rights, distances, -half - sidewalkWidth, -half, SidewalkLift, SidewalkSubmesh);
            }
            Strip(network, centers, rights, distances, -half, half, kind == RoadKind.Main ? AsphaltLift : AlleyLift, CarriagewaySubmesh);
            if (kind == RoadKind.Main) Dashes(network, length);
            // Ends that meet another road get round caps, so corners where two roads join show no notch.
            for (int end = 0; end < 2; end++)
            {
                var point = end == 0 ? centers[0] : centers[samples - 1];
                if (!network.TouchesOtherRoad(this, point)) continue;
                if (kind == RoadKind.Main && sidewalkWidth > 0f) Disc(network, point, half + sidewalkWidth, SidewalkLift, SidewalkSubmesh);
                Disc(network, point, half, kind == RoadKind.Main ? AsphaltLift : AlleyLift, CarriagewaySubmesh);
            }

            var filter = GetComponent<MeshFilter>();
            var mesh = filter.sharedMesh;
            if (mesh == null || mesh.name != name + "_Road")
            {
                mesh = new Mesh { name = name + "_Road" };
                filter.sharedMesh = mesh;
            }
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = triangles.Length;
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            builtShape = ShapeSignature();
        }

        // A band between two lateral offsets. Columns every ~2 m across keep wide bands on the terrain: with
        // only edge vertices, a terrain bulge between them showed through the asphalt.
        private void Strip(RoadNetwork network, Vector3[] centers, Vector3[] rights, float[] distances, float from, float to, float lift, int submesh)
        {
            int columns = Mathf.Max(1, Mathf.CeilToInt((to - from) / LateralSpacing));
            int start = vertices.Count;
            for (int i = 0; i < centers.Length; i++)
                for (int c = 0; c <= columns; c++)
                {
                    float u = c / (float)columns;
                    vertices.Add(network.OnGround(centers[i] + rights[i] * Mathf.Lerp(from, to, u), lift));
                    uvs.Add(new Vector2(u, distances[i] / Mathf.Max(0.1f, to - from)));
                }
            var list = triangles[submesh];
            int stride = columns + 1;
            for (int i = 0; i < centers.Length - 1; i++)
                for (int c = 0; c < columns; c++)
                {
                    int a = start + i * stride + c;
                    list.Add(a); list.Add(a + stride); list.Add(a + 1);
                    list.Add(a + 1); list.Add(a + stride); list.Add(a + stride + 1);
                }
        }

        private void Dashes(RoadNetwork network, float length)
        {
            for (float d = junctionClearance; d + dashLength <= length - junctionClearance; d += dashLength + dashGap)
            {
                float t0 = d / length, t1 = (d + dashLength) / length;
                Vector3 a = Container.EvaluatePosition(t0), b = Container.EvaluatePosition(t1);
                if (network.InsideOtherCarriageway(this, (a + b) * 0.5f, junctionClearance)) continue;
                var right = Vector3.Cross(Vector3.up, Flat(b - a).normalized) * (dashWidth * 0.5f);
                int start = vertices.Count;
                vertices.Add(network.OnGround(a - right, MarkingLift));
                vertices.Add(network.OnGround(a + right, MarkingLift));
                vertices.Add(network.OnGround(b - right, MarkingLift));
                vertices.Add(network.OnGround(b + right, MarkingLift));
                for (int i = 0; i < 4; i++) uvs.Add(new Vector2(i % 2, i / 2));
                var list = triangles[MarkingSubmesh];
                list.Add(start); list.Add(start + 2); list.Add(start + 1);
                list.Add(start + 1); list.Add(start + 2); list.Add(start + 3);
            }
        }

        // Round cap made of rings (~2 m apart) so it follows the terrain like the strips.
        private void Disc(RoadNetwork network, Vector3 center, float radius, float lift, int submesh)
        {
            const int segments = 24;
            int rings = Mathf.Max(1, Mathf.CeilToInt(radius / LateralSpacing));
            int start = vertices.Count;
            vertices.Add(network.OnGround(center, lift));
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int ring = 1; ring <= rings; ring++)
                for (int i = 0; i < segments; i++)
                {
                    float angle = i * Mathf.PI * 2f / segments;
                    var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (ring / (float)rings);
                    vertices.Add(network.OnGround(center + offset * radius, lift));
                    uvs.Add(new Vector2(0.5f + offset.x * 0.5f, 0.5f + offset.z * 0.5f));
                }
            var list = triangles[submesh];
            for (int i = 0; i < segments; i++)
            {
                list.Add(start);
                list.Add(start + 1 + (i + 1) % segments);
                list.Add(start + 1 + i);
            }
            for (int ring = 1; ring < rings; ring++)
            {
                int inner = start + 1 + (ring - 1) * segments, outer = inner + segments;
                for (int i = 0; i < segments; i++)
                {
                    int j = (i + 1) % segments;
                    list.Add(inner + i); list.Add(inner + j); list.Add(outer + i);
                    list.Add(inner + j); list.Add(outer + j); list.Add(outer + i);
                }
            }
        }

        // Horizontal distance from a world point to this road's centre line.
        public float DistanceToCentre(Vector3 world)
        {
            var local = Container.transform.InverseTransformPoint(world);
            SplineUtility.GetNearestPoint(Container.Spline, (float3)local, out float3 nearest, out _, 4, 2);
            var p = Container.transform.TransformPoint(nearest);
            return Vector2.Distance(new Vector2(p.x, p.z), new Vector2(world.x, world.z));
        }

        public void UpdateFootprint()
        {
            float reach = HalfWidth + (kind == RoadKind.Main ? sidewalkWidth : 0f);
            var bounds = new Bounds();
            const int samples = 32;
            for (int i = 0; i <= samples; i++)
            {
                var p = Flat((Vector3)Container.EvaluatePosition(i / (float)samples));
                if (i == 0) bounds = new Bounds(p, Vector3.zero); else bounds.Encapsulate(p);
            }
            bounds.Expand(new Vector3(reach * 2f + 2f, 0f, reach * 2f + 2f));
            Footprint = bounds;
        }

        public bool FootprintContains(Vector3 world, float margin)
        {
            var b = Footprint;
            return world.x >= b.min.x - margin && world.x <= b.max.x + margin && world.z >= b.min.z - margin && world.z <= b.max.z + margin;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
