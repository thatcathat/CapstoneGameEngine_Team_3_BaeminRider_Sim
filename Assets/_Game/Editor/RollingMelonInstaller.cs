using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DeliveryRider.Editor
{
    // Builds the melon pile prefab used by HazardSlotInstaller. The pile itself is laid out at runtime
    // by RollingMelonHazard, so the melons here only carry mesh, body and size.
    public static class RollingMelonInstaller
    {
        private const string Art = "Assets/_Game/Art/FourDistrict/";
        // Enough melons for the largest pile (3 x 3 base: 9 + 4 + 1).
        private const int MaximumMelons = 14;

        public static RollingMelonHazard CreatePrefab(DeliverySession session, string path)
        {
            if (session == null || session.Player == null) throw new InvalidOperationException("A player is required for melon sizing.");
            var player = session.Player.GetComponent<CapsuleCollider>();
            if (player == null) throw new InvalidOperationException("Player capsule is missing.");
            float diameter = player.height * Mathf.Abs(player.transform.lossyScale.y) * 0.3f;
            var root = new GameObject("MelonPile");
            var hazard = root.AddComponent<RollingMelonHazard>();
            var mesh = MelonMesh();
            var materials = new[] { MakeMaterial("RollingMelonGreen", new Color(0.24f, 0.6f, 0.12f)), MakeMaterial("RollingMelonStripe", new Color(0.035f, 0.19f, 0.04f)) };
            var actors = new RollingMelon[MaximumMelons];
            for (int i = 0; i < actors.Length; i++)
            {
                var melon = new GameObject("RollingMelon_" + i);
                melon.transform.SetParent(root.transform, false);
                melon.transform.localPosition = new Vector3(0f, diameter * 0.5f, 0f);
                melon.transform.localScale = Vector3.one * diameter;
                melon.AddComponent<MeshFilter>().sharedMesh = mesh;
                melon.AddComponent<MeshRenderer>().sharedMaterials = materials;
                melon.AddComponent<SphereCollider>().radius = 0.5f;
                var body = melon.AddComponent<Rigidbody>();
                body.mass = 8f;
                body.useGravity = true;
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.maxAngularVelocity = 60f;
                actors[i] = melon.AddComponent<RollingMelon>();
            }
            var data = new SerializedObject(hazard);
            var array = data.FindProperty("melons");
            array.arraySize = actors.Length;
            for (int i = 0; i < actors.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = actors[i];
            data.ApplyModifiedPropertiesWithoutUndo();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) throw new InvalidOperationException("Could not save " + path);
            return prefab.GetComponent<RollingMelonHazard>();
        }

        private static Material MakeMaterial(string name, Color color)
        {
            string path = Art + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var material = new Material(Shader.Find(CurvedWorldInstaller.LitShader)) { name = name, color = color };
            material.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Mesh MelonMesh()
        {
            const string path = Art + "RollingMelon.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var mesh = UnityEngine.Object.Instantiate(primitive.GetComponent<MeshFilter>().sharedMesh);
            UnityEngine.Object.DestroyImmediate(primitive);
            mesh.name = "StripedMelon";
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var green = new List<int>();
            var dark = new List<int>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var center = (vertices[triangles[i]] + vertices[triangles[i + 1]] + vertices[triangles[i + 2]]) / 3f;
                float angle = Mathf.Atan2(center.z, center.x) + Mathf.PI;
                var target = Mathf.Repeat(angle * 6f / (2f * Mathf.PI), 1f) < 0.3f ? dark : green;
                target.Add(triangles[i]); target.Add(triangles[i + 1]); target.Add(triangles[i + 2]);
            }
            mesh.subMeshCount = 2;
            mesh.SetTriangles(green, 0);
            mesh.SetTriangles(dark, 1);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
