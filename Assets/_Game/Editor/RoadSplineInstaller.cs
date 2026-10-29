using System;
using System.IO;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

namespace DeliveryRider.Editor
{
    // Converts the 22 four-district roads into editable splines with mesh road surfaces (Splines package),
    // and gives the terrain a road-free ground texture. After this, the "Roads" splines in the scene are the
    // road layout to edit; moving a knot rebuilds the surfaces (RoadNetwork).
    // Running this again overwrites hand edits with the baked layout, so it refuses when a Roads root exists
    // unless "Rebuild Road Splines From Data" is used.
    public static class RoadSplineInstaller
    {
        private const string RootName = "Roads";
        private const string MaterialFolder = "Assets/_Game/Art/Roads";
        private const string GroundSource = "Docs/MapLayout/district_ground.png";
        private const string GroundTexture = "Assets/_Game/Art/FourDistrict/DistrictGround.png";
        private const string GroundMaterial = "Assets/_Game/Art/FourDistrict/Ground.mat";
        // AutoSmooth tension: low keeps rounded corners close to the original polyline, which the call routes,
        // hazard slots and buildings were laid out against.
        private const float Tension = 0.2f;
        private const float MainSidewalk = 3f;
        // Links missing from the trial build's road data: (new id, width, from road end, to road start).
        // N closes the outer ring at the top of the map, where G and A3 stopped 30 m apart.
        private static readonly (string id, float width, string fromRoad, bool fromEnd, string toRoad, bool toEnd)[] Links =
        {
            ("N", 10f, "G", true, "A3", false),
        };

        [MenuItem("Delivery Rider/Apply Road Splines")]
        public static void Apply() => Run(false);

        // Adds link roads that are missing from an existing Roads root, keeping hand-edited splines.
        [MenuItem("Delivery Rider/Add Missing Road Links")]
        public static void AddMissingLinks()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene changes first.");
            var scene = EditorSceneManager.OpenScene(DistrictLayout.ScenePath, OpenSceneMode.Single);
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (root == null) throw new InvalidOperationException("Run Apply Road Splines first.");
            int added = AddLinks(root.transform);
            root.GetComponent<RoadNetwork>().RebuildAll();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"Road links added: {added}. Re-run hazard slots, buildings and traffic. Play Mode not started.");
        }

        private static int AddLinks(Transform root)
        {
            int added = 0;
            foreach (var link in Links)
            {
                if (root.Find("Road_" + link.id) != null) continue;
                var from = root.Find("Road_" + link.fromRoad)?.GetComponent<SplineContainer>();
                var to = root.Find("Road_" + link.toRoad)?.GetComponent<SplineContainer>();
                if (from == null || to == null) throw new InvalidOperationException($"Roads {link.fromRoad} and {link.toRoad} are required for link {link.id}.");
                Vector3 a = from.EvaluatePosition(link.fromEnd ? 1f : 0f);
                Vector3 c = to.EvaluatePosition(link.toEnd ? 1f : 0f);
                var template = from.GetComponent<MeshRenderer>();
                var go = new GameObject("Road_" + link.id);
                go.transform.SetParent(root, false);
                var container = go.AddComponent<SplineContainer>();
                container.Spline.Clear();
                container.Spline.Add(new BezierKnot((float3)a), TangentMode.AutoSmooth, Tension);
                container.Spline.Add(new BezierKnot((float3)c), TangentMode.AutoSmooth, Tension);
                go.AddComponent<RoadSurface>().Configure(RoadKind.Main, link.width, MainSidewalk);
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = template.sharedMaterials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                added++;
            }
            return added;
        }

        [MenuItem("Delivery Rider/Rebuild Road Splines From Data")]
        public static void RebuildFromData()
        {
            if (!EditorUtility.DisplayDialog("Rebuild road splines", "Replace the Roads splines with the baked layout? Hand edits to the splines are lost.", "Rebuild", "Cancel")) return;
            Run(true);
        }

        private static void Run(bool replace)
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Stop Play Mode and finish compilation first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene changes first.");
            var scene = EditorSceneManager.OpenScene(DistrictLayout.ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var existing = roots.FirstOrDefault(g => g.name == RootName);
            if (existing != null)
            {
                if (!replace) { Debug.Log("Roads already exist; edit the splines directly or use Rebuild Road Splines From Data."); return; }
                UnityEngine.Object.DestroyImmediate(existing);
            }

            var world = roots.Single(g => g.name == "FourDistrictWorld").transform;
            var ground = world.GetComponentsInChildren<MeshCollider>(true).Where(c => c.name.StartsWith("DistrictGround")).Cast<Collider>().ToArray();
            if (ground.Length == 0) throw new InvalidOperationException("Terrain colliders (DistrictGround_*) are required.");
            // The baked centreline markers are superseded by the splines.
            var centerlines = world.Find("RoadCenterlines");
            if (centerlines != null) UnityEngine.Object.DestroyImmediate(centerlines.gameObject);

            var sidewalk = Material("RoadSidewalk", new Color(0.78f, 0.76f, 0.71f));
            var asphalt = Material("RoadAsphalt", new Color(0.27f, 0.29f, 0.32f));
            var alley = Material("RoadAlley", new Color(0.6f, 0.53f, 0.43f));
            var marking = Material("RoadMarking", new Color(0.95f, 0.93f, 0.85f));

            var root = new GameObject(RootName);
            var network = root.AddComponent<RoadNetwork>();
            network.Configure(ground);
            var layout = DistrictLayout.ReadLayout();
            foreach (var road in layout.roads)
            {
                bool isAlley = road.id.StartsWith("L");
                var go = new GameObject("Road_" + road.id);
                go.transform.SetParent(root.transform, false);
                var container = go.AddComponent<SplineContainer>();
                var spline = container.Spline;
                spline.Clear();
                foreach (var point in road.points) spline.Add(new BezierKnot((float3)point), TangentMode.AutoSmooth, Tension);
                var surface = go.AddComponent<RoadSurface>();
                surface.Configure(isAlley ? RoadKind.Alley : RoadKind.Main, road.width, isAlley ? 0f : MainSidewalk);
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = new[] { sidewalk, isAlley ? alley : asphalt, marking };
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }
            AddLinks(root.transform);
            network.RebuildAll();

            ApplyGroundTexture();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the road splines.");
            Debug.Log($"Road splines built: {layout.roads.Length} roads on {ground.Length} terrain colliders. Play Mode not started.");
        }

        // The terrain keeps only zone tints; the old painted roads would show around the curved surfaces.
        private static void ApplyGroundTexture()
        {
            File.Copy(GroundSource, GroundTexture, true);
            AssetDatabase.ImportAsset(GroundTexture);
            var importer = (TextureImporter)AssetImporter.GetAtPath(GroundTexture);
            importer.anisoLevel = 4;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            var material = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterial);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(GroundTexture));
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        private static Material Material(string name, Color color)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/_Game/Art", "Roads");
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find(CurvedWorldInstaller.LitShader)) { name = name, color = color };
            material.SetFloat("_Smoothness", 0.08f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
