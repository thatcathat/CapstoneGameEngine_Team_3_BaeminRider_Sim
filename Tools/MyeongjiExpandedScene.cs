using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeliveryRider.MapTools
{
    // Builds the test scene FourDistrictPrototype_MyeongjiExpanded: a copy of the Myeongji scene with the wider map of
    // the html-tuning branch (buildings, lamps, ground and markings) and nothing else from that branch. Shops, doors,
    // traffic, walkers, hazards and the map screen stay in the old area. The changes the branch made to the shared map
    // prefab are made as overrides in this scene only, so the Myeongji scene does not change.
    // Run once: unity command run_script --file Tools/MyeongjiExpandedScene.cs --entry DeliveryRider.MapTools.MyeongjiExpandedScene.Build
    public static class MyeongjiExpandedScene
    {
        private const string Source = "Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity";
        private const string Target = "Assets/_Game/Scenes/FourDistrictPrototype_MyeongjiExpanded.unity";
        private const string MapPrefab = "Assets/_Game/Prefabs/Myeongji/MyeongjiMap.prefab";
        private const string ExpansionPrefab = "Assets/_Game/Prefabs/Myeongji/MyeongjiMapExpansion.prefab";
        private const string Geometry = "Assets/_Game/Art/Myeongji/Geometry/";
        // The street furniture model; two of its lamps stand in the new crossings, so the branch moved them.
        private const string LampModelGuid = "214412580083e084dbcbc04e75937e11";
        // Ignore Raycast: the walls stop bodies, but the camera and ground probes (Default only) pass through them.
        private const int WallLayer = 2;

        public static string Build()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Target) != null) throw new InvalidOperationException(Target + " already exists.");
            var active = EditorSceneManager.GetActiveScene();
            if (active.isDirty) throw new InvalidOperationException("Save or discard the open scene first: " + active.path);
            if (!AssetDatabase.CopyAsset(Source, Target)) throw new InvalidOperationException("Could not copy the scene.");
            try
            {
                var scene = EditorSceneManager.OpenScene(Target, OpenSceneMode.Single);
                var report = new StringBuilder();
                var map = scene.GetRootGameObjects().First(root => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == MapPrefab);
                SwapGround(map, report);
                AddMarkings(map);
                MoveLamps(map, report);
                var expansion = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ExpansionPrefab), scene);
                AddBoundary();

                Camera.main.farClipPlane = 250f;
                EditorUtility.SetDirty(Camera.main);
                var controller = UnityEngine.Object.FindFirstObjectByType<SceneController>(FindObjectsInactive.Include);
                var controllerData = new SerializedObject(controller);
                controllerData.FindProperty("gameSceneName").stringValue = scene.name;
                controllerData.ApplyModifiedProperties();

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the scene.");
                var scenes = EditorBuildSettings.scenes.ToList();
                if (scenes.All(s => s.path != Target)) scenes.Add(new EditorBuildSettingsScene(Target, true));
                EditorBuildSettings.scenes = scenes.ToArray();

                Check(expansion, report);
                return report.ToString();
            }
            catch
            {
                EditorSceneManager.OpenScene(Source, OpenSceneMode.Single);
                AssetDatabase.DeleteAsset(Target);
                throw;
            }
        }

        // The checks alone, on the open scene.
        public static string Review()
        {
            var expansion = EditorSceneManager.GetActiveScene().GetRootGameObjects()
                .First(root => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == ExpansionPrefab);
            var report = new StringBuilder();
            Check(expansion, report);
            return report.ToString();
        }

        private static void SwapGround(GameObject map, StringBuilder report)
        {
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(Geometry + "Ground_SidewalkStage15.asset");
            var wide = AssetDatabase.LoadAssetAtPath<Mesh>(Geometry + "Ground_Expanded.asset");
            int filters = 0, colliders = 0;
            foreach (var filter in map.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != old) continue;
                filter.sharedMesh = wide;
                PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                filters++;
            }
            foreach (var collider in map.GetComponentsInChildren<MeshCollider>(true))
            {
                if (collider.sharedMesh != old) continue;
                collider.sharedMesh = wide;
                PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
                colliders++;
            }
            if (filters != 1 || colliders != 1) throw new InvalidOperationException($"Expected one ground, found {filters} meshes and {colliders} colliders.");
            report.AppendLine($"ground swapped, {wide.subMeshCount} parts on {map.name}");
        }

        private static void AddMarkings(GameObject map)
        {
            var go = new GameObject("ExpandedMarkings", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(map.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Geometry + "Ground_ExpandedMarkings.asset");
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.sharedMaterials = new[]
            {
                (Material)AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath("41ae19529fbda1b4b91c9094fa4fbfd2")),
                (Material)AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath("52f2f1a7bcfb5e44e82967e0b8ae3c12")),
            };
        }

        private static void MoveLamps(GameObject map, StringBuilder report)
        {
            var moves = new Dictionary<long, float> { { -8060922111896490537, 4.5f }, { -7884857033163993244, -3.199997f } };
            var posts = new Dictionary<float, float> { { 1f, 5.5f }, { -119f, -122.2f } };
            int models = 0, colliders = 0;
            foreach (var t in map.GetComponentsInChildren<Transform>(true))
            {
                var origin = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t);
                if (origin != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(origin, out string guid, out long id)
                    && guid == LampModelGuid && moves.TryGetValue(id, out float x))
                {
                    Place(t, x);
                    models++;
                    continue;
                }
                var local = t.localPosition;
                if (t.GetComponent<BoxCollider>() != null && Mathf.Abs(local.y - 1.5f) < 0.001f && Mathf.Abs(local.z + 4.72f) < 0.001f
                    && posts.TryGetValue(Mathf.Round(local.x), out float postX) && Mathf.Abs(local.x - Mathf.Round(local.x)) < 0.001f)
                {
                    Place(t, postX);
                    colliders++;
                }
            }
            if (models != 2 || colliders != 2) throw new InvalidOperationException($"Expected two lamps, found {models} models and {colliders} colliders.");
            report.AppendLine("two lamps moved");
        }

        private static void Place(Transform t, float x)
        {
            var local = t.localPosition;
            local.x = x;
            t.localPosition = local;
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }

        // Four tall walls around the ground; the same places as the branch's boundary, which also holds in thrown riders.
        private static void AddBoundary()
        {
            var root = new GameObject("MapBoundary");
            Wall(root, "West", new Vector3(-90.3f, 55.86f, -32.5f), new Vector3(0.6f, 128f, 236.2f));
            Wall(root, "East", new Vector3(200.3f, 55.86f, -32.5f), new Vector3(0.6f, 128f, 236.2f));
            Wall(root, "South", new Vector3(55f, 55.86f, -150.3f), new Vector3(291.2f, 128f, 0.6f));
            Wall(root, "North", new Vector3(55f, 55.86f, 85.3f), new Vector3(291.2f, 128f, 0.6f));
        }

        private static void Wall(GameObject root, string name, Vector3 position, Vector3 size)
        {
            var go = new GameObject(name, typeof(BoxCollider)) { layer = WallLayer };
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = position;
            go.GetComponent<BoxCollider>().size = size;
        }

        // Nothing new may stand on what stays in the old area, and the new ground must catch the rider.
        private static void Check(GameObject expansion, StringBuilder report)
        {
            Physics.SyncTransforms();
            var solids = expansion.GetComponentsInChildren<Collider>(true);
            report.AppendLine($"expansion: {solids.Length} colliders, {expansion.GetComponentsInChildren<MeshRenderer>(true).Length} renderers");
            var player = UnityEngine.Object.FindFirstObjectByType<RiderMotor>(FindObjectsInactive.Include);
            report.AppendLine($"rider layer {player.gameObject.layer}, ignores walls: {Physics.GetIgnoreLayerCollision(player.gameObject.layer, WallLayer)}");

            var hit = new HashSet<string>();
            void Test(string kind, Vector3 point)
            {
                foreach (var solid in solids)
                {
                    var bounds = solid.bounds;
                    if (point.x >= bounds.min.x && point.x <= bounds.max.x && point.z >= bounds.min.z && point.z <= bounds.max.z)
                        hit.Add(kind + " in " + solid.name);
                }
            }
            var walkers = UnityEngine.Object.FindFirstObjectByType<PedestrianNetwork>(FindObjectsInactive.Include);
            for (int i = 0; i < walkers.PathCount; i++) foreach (var point in walkers.Path(i).points) Test("walker lane", point);
            var cars = UnityEngine.Object.FindFirstObjectByType<TrafficNetwork>(FindObjectsInactive.Include);
            for (int i = 0; i < cars.PathCount; i++) foreach (var point in cars.Path(i).points) Test("car lane", point);
            foreach (Transform spot in GameObject.Find("MyeongjiLocations").transform) Test("location " + spot.name, spot.position);
            Test("rider start", player.transform.position);
            report.AppendLine($"overlaps with old lanes and places (bounds): {hit.Count}");
            foreach (var line in hit.OrderBy(s => s).Take(30)) report.AppendLine("  " + line);

            foreach (var spot in new[] { new Vector3(-85f, 0f, -145f), new Vector3(195f, 0f, 80f), new Vector3(150f, 0f, -100f), new Vector3(-60f, 0f, 40f), player.transform.position })
            {
                bool ground = Physics.Raycast(new Vector3(spot.x, 40f, spot.z), Vector3.down, out var down, 60f, 1, QueryTriggerInteraction.Ignore);
                report.AppendLine($"ground at {spot.x:F0},{spot.z:F0}: {(ground ? down.collider.name + " y=" + down.point.y.ToString("F2") : "NONE")}");
            }
        }
    }
}
