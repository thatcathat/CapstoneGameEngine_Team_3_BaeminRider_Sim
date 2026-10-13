using System;
using System.IO;
using UnityEngine;

namespace DeliveryRider.Editor
{
    // Baked four-district planning data (Docs/MapLayout/district_geometry.json from Tools/build_district_geometry.py).
    // The scene itself was generated once by the former FourDistrictInstaller (removed 2026-10-14; see git history).
    public static class DistrictLayout
    {
        public const string ScenePath = "Assets/_Game/Scenes/FourDistrictPrototype.unity";

        [Serializable] public class Road { public string id; public float width; public Vector3[] points; }
        [Serializable] public class Destination
        {
            public float x, y, z, outX, outZ;
            public string zone;
            public int node;
            public Vector3 Position => new Vector3(x, y, z);
            public Vector3 Outward => new Vector3(outX, 0, outZ).normalized;
        }
        [Serializable] public class Flat { public float x, y, z, radius; }
        [Serializable] public class Layout
        {
            public int nx, nz;
            public float minX, minZ, maximumGradePercent, maximumHeight, heightScale;
            public float[] heights;
            public Road[] roads;
            public Destination[] stores, doors;
            public Destination home;
            public Flat[] flats;
        }

        public static Layout ReadLayout() => JsonUtility.FromJson<Layout>(File.ReadAllText("Docs/MapLayout/district_geometry.json"));

        // Height of the shared 1 m terrain grid (same triangulation as the ground meshes).
        public static float Height(Layout d, float x, float z)
        {
            float fx = Mathf.Clamp(x - d.minX, 0, d.nx - 1.0001f), fz = Mathf.Clamp(z - d.minZ, 0, d.nz - 1.0001f);
            int ix = (int)fx, iz = (int)fz;
            float u = fx - ix, v = fz - iz;
            float a = d.heights[iz * d.nx + ix], b = d.heights[iz * d.nx + ix + 1], c = d.heights[(iz + 1) * d.nx + ix], e = d.heights[(iz + 1) * d.nx + ix + 1];
            return u + v <= 1 ? a + (b - a) * u + (c - a) * v : e + (c - e) * (1 - u) + (b - e) * (1 - v);
        }
    }
}
