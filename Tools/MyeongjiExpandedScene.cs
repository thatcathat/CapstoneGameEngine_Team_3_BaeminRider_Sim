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
    // the html-tuning branch (buildings, lamps, ground, markings and street details) and nothing else from that branch.
    // Shops, doors, traffic, walkers, hazards and the map screen stay in the old area. The changes the branch made to the
    // shared map prefab are made as overrides in this scene only, so the Myeongji scene does not change.
    // Run once: unity command run_script --file Tools/MyeongjiExpandedScene.cs --entry DeliveryRider.MapTools.MyeongjiExpandedScene.Build
    public static class MyeongjiExpandedScene
    {
        private const string Source = "Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity";
        private const string Target = "Assets/_Game/Scenes/FourDistrictPrototype_MyeongjiExpanded.unity";
        private const string Reference = "Assets/HtmlTuningReference.unity";
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

        // Brings the branch's latest look into the existing scene: building meshes and materials, street details and
        // evening lights across the wider map, and the old-area props where the branch keeps them. Its art is already in
        // main, so only scene data moves; gameplay and the old buildings' colliders stay. Safe to run again.
        // Needs the branch's scene at Reference for the run; delete it afterwards:
        //   git show origin/html-tuning:Assets/_Game/Scenes/FourDistrictPrototype_Myeongji.unity > Assets/HtmlTuningReference.unity
        public static string ImportDetails()
        {
            var active = EditorSceneManager.GetActiveScene();
            if (active.isDirty) throw new InvalidOperationException("Save or discard the open scene first: " + active.path);
            var scene = EditorSceneManager.OpenScene(Target, OpenSceneMode.Single);
            var reference = EditorSceneManager.OpenScene(Reference, OpenSceneMode.Additive);
            var report = new StringBuilder();
            var expansion = Root(scene, "MyeongjiMapExpansion");
            try
            {
                var map = Root(scene, "MyeongjiMap");
                var theirMap = Root(reference, "MyeongjiMap");
                var theirExpansion = Root(reference, "MyeongjiMapExpansion");
                CopyLooks(theirExpansion, expansion, report);
                FitColliders(expansion, report);
                ReplaceGroup(theirExpansion, expansion, "StreetDetails", report);
                foreach (var group in new[] { "StreetGreenery", "EntranceDetails", "EveningLighting" })
                    MatchChildren(theirMap.transform.Find(group), map.transform.Find(group), report);
                RebindEvening(map, expansion, theirMap, report);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the scene.");
            }
            finally
            {
                EditorSceneManager.CloseScene(reference, true);
            }
            Check(expansion, report);
            return report.ToString();
        }

        // Adds a visual-only row outside each boundary, reusing this scene's building meshes and materials.
        public static string PlaceBoundaryBuildings()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(Target);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(Target, OpenSceneMode.Additive);
            try
            {
                if (scene.isDirty) throw new InvalidOperationException("Save the expanded scene before placing buildings.");
                var boundary = Root(scene, "MapBoundary").transform;
                var templates = Root(scene, "MyeongjiMapExpansion").GetComponentsInChildren<MeshRenderer>()
                    .Where(r => r.transform.parent.name.StartsWith("Buildings_"))
                    .OrderBy(r => r.name, StringComparer.Ordinal).ToArray();
                if (templates.Length == 0) throw new InvalidOperationException("Missing expansion building models.");

                var group = new GameObject("BoundaryBuildings");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(group, scene);
                var report = new StringBuilder();
                try
                {
                    var random = new System.Random(471);
                    foreach (string side in new[] { "North", "South", "West", "East" })
                    {
                        var wall = boundary.Find(side).GetComponent<BoxCollider>().bounds;
                        bool alongX = side == "North" || side == "South";
                        float cursor = (alongX ? wall.min.x : wall.min.z) + 2f;
                        float end = (alongX ? wall.max.x : wall.max.z) - 2f;
                        int count = 0;
                        while (cursor < end)
                        {
                            var model = templates[random.Next(templates.Length)];
                            var mesh = model.GetComponent<MeshFilter>().sharedMesh;
                            var house = new GameObject(side + "_" + model.name, typeof(MeshFilter), typeof(MeshRenderer));
                            house.transform.SetParent(group.transform, false);
                            house.transform.rotation = Quaternion.Euler(0f, alongX ? 0f : 90f, 0f) * model.transform.rotation;
                            house.transform.localScale = model.transform.lossyScale;
                            house.GetComponent<MeshFilter>().sharedMesh = mesh;
                            house.GetComponent<MeshRenderer>().sharedMaterials = model.sharedMaterials;

                            // Use real vertices so a rotated or off-centre mesh touches the outer wall face.
                            var vertices = mesh.vertices;
                            var bounds = new Bounds(house.transform.TransformPoint(vertices[0]), Vector3.zero);
                            foreach (var vertex in vertices) bounds.Encapsulate(house.transform.TransformPoint(vertex));
                            float width = alongX ? bounds.size.x : bounds.size.z;
                            if (cursor + width > end)
                            {
                                UnityEngine.Object.DestroyImmediate(house);
                                break;
                            }
                            float x = alongX ? cursor - bounds.min.x :
                                side == "East" ? wall.max.x - bounds.min.x : wall.min.x - bounds.max.x;
                            float z = !alongX ? cursor - bounds.min.z :
                                side == "North" ? wall.max.z - bounds.min.z : wall.min.z - bounds.max.z;
                            house.transform.position = new Vector3(x, model.bounds.min.y - bounds.min.y, z);
                            cursor += width + random.Next(2, 6);
                            count++;
                        }
                        if (count == 0) throw new InvalidOperationException("No buildings placed on " + side);
                        report.AppendLine($"{side}: {count}");
                    }
                }
                catch
                {
                    UnityEngine.Object.DestroyImmediate(group);
                    throw;
                }
                var old = scene.GetRootGameObjects().FirstOrDefault(r => r != group && r.name == group.name);
                if (old != null) UnityEngine.Object.DestroyImmediate(old);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save boundary buildings.");
                return report.ToString();
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        // The new buildings' colliders alone, on the saved scene.
        public static string FitBuildingColliders()
        {
            var active = EditorSceneManager.GetActiveScene();
            if (active.isDirty) throw new InvalidOperationException("Save or discard the open scene first: " + active.path);
            var scene = EditorSceneManager.OpenScene(Target, OpenSceneMode.Single);
            var report = new StringBuilder();
            var expansion = Root(scene, "MyeongjiMapExpansion");
            FitColliders(expansion, report);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the scene.");
            Check(expansion, report);
            return report.ToString();
        }

        // The branch left each new building's collider in its old shape under the new look. Here they share the look's
        // mesh, as the old buildings do, so the rider stops where the wall appears to be.
        private static void FitColliders(GameObject expansion, StringBuilder report)
        {
            int changed = 0;
            foreach (var collider in expansion.GetComponentsInChildren<MeshCollider>(true))
            {
                var mesh = collider.GetComponent<MeshFilter>().sharedMesh;
                if (collider.sharedMesh == mesh) continue;
                collider.sharedMesh = mesh;
                PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
                changed++;
            }
            report.AppendLine($"colliders fitted to the look: {changed}");
        }

        private static GameObject Root(UnityEngine.SceneManagement.Scene scene, string name) =>
            scene.GetRootGameObjects().Single(root => root.name == name);

        private static string PathUnder(Transform t, Transform root) =>
            t.parent == root ? t.name : PathUnder(t.parent, root) + "/" + t.name;

        private static void CopyLooks(GameObject from, GameObject to, StringBuilder report)
        {
            int meshes = 0, materials = 0;
            foreach (var theirs in from.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (theirs.transform.parent.name == "StreetDetails") continue;
                var mine = to.transform.Find(PathUnder(theirs.transform, from.transform)).GetComponent<MeshRenderer>();
                var filter = mine.GetComponent<MeshFilter>();
                var mesh = theirs.GetComponent<MeshFilter>().sharedMesh;
                if (mesh == null || theirs.sharedMaterials.Any(material => material == null)) throw new InvalidOperationException("Art missing from main: " + theirs.name);
                if (filter.sharedMesh != mesh)
                {
                    filter.sharedMesh = mesh;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                    meshes++;
                }
                if (!mine.sharedMaterials.SequenceEqual(theirs.sharedMaterials))
                {
                    mine.sharedMaterials = theirs.sharedMaterials;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(mine);
                    materials++;
                }
            }
            report.AppendLine($"looks: {meshes} meshes, {materials} material lists");
        }

        private static void ReplaceGroup(GameObject from, GameObject to, string name, StringBuilder report)
        {
            var old = to.transform.Find(name);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var theirs = from.transform.Find(name);
            if (theirs.GetComponentsInChildren<MonoBehaviour>(true).Length > 0) throw new InvalidOperationException(name + " should hold no scripts.");
            var copy = UnityEngine.Object.Instantiate(theirs.gameObject, to.transform, false);
            copy.name = name;
            report.AppendLine($"{name}: {copy.transform.childCount} children");
        }

        // Matches a group's children by name, in world poses because the groups' own rotations differ between the scenes.
        private static void MatchChildren(Transform from, Transform to, StringBuilder report)
        {
            int moved = 0, added = 0, removed = 0;
            foreach (var mine in to.Cast<Transform>().ToArray())
            {
                if (from.Find(mine.name) != null) continue;
                UnityEngine.Object.DestroyImmediate(mine.gameObject);
                removed++;
            }
            foreach (Transform theirs in from)
            {
                var mine = to.Find(theirs.name);
                if (mine == null)
                {
                    mine = UnityEngine.Object.Instantiate(theirs.gameObject, to).transform;
                    mine.name = theirs.name;
                    added++;
                }
                else if ((mine.position - theirs.position).sqrMagnitude > 1e-4f || Quaternion.Angle(mine.rotation, theirs.rotation) > 0.01f) moved++;
                mine.SetPositionAndRotation(theirs.position, theirs.rotation);
            }
            report.AppendLine($"{to.name}: {moved} moved, {added} added, {removed} removed, {to.childCount} now");
        }

        private static void RebindEvening(GameObject map, GameObject expansion, GameObject theirMap, StringBuilder report)
        {
            var controller = map.GetComponentInChildren<NeighborhoodEveningLights>(true);
            var to = new SerializedObject(controller);
            var from = new SerializedObject(theirMap.GetComponentInChildren<NeighborhoodEveningLights>(true));
            foreach (var (list, slotMaterial) in new[] { ("windows", "windowMaterial"), ("lamps", "lampMaterial") })
            {
                var theirs = from.FindProperty(list);
                var mine = to.FindProperty(list);
                var expected = to.FindProperty(slotMaterial).objectReferenceValue;
                mine.arraySize = theirs.arraySize;
                for (int i = 0; i < theirs.arraySize; i++)
                {
                    var source = theirs.GetArrayElementAtIndex(i);
                    var target = mine.GetArrayElementAtIndex(i);
                    var theirRenderer = (Renderer)source.FindPropertyRelative("renderer").objectReferenceValue;
                    var root = theirRenderer.transform.root == theirMap.transform ? map.transform : expansion.transform;
                    var renderer = root.Find(PathUnder(theirRenderer.transform, theirRenderer.transform.root)).GetComponent<Renderer>();
                    int slot = source.FindPropertyRelative("slot").intValue;
                    if (renderer.sharedMaterials[slot] != expected) throw new InvalidOperationException($"{renderer.name} slot {slot} is not {slotMaterial}.");
                    target.FindPropertyRelative("renderer").objectReferenceValue = renderer;
                    target.FindPropertyRelative("slot").intValue = slot;
                    target.FindPropertyRelative("group").intValue = source.FindPropertyRelative("group").intValue;
                }
            }
            var theirLights = from.FindProperty("streetLights");
            var lights = to.FindProperty("streetLights");
            lights.arraySize = theirLights.arraySize;
            var host = controller.transform;
            for (int i = 0; i < theirLights.arraySize; i++)
                lights.GetArrayElementAtIndex(i).objectReferenceValue = host.Find(theirLights.GetArrayElementAtIndex(i).objectReferenceValue.name).GetComponent<Light>();
            to.ApplyModifiedPropertiesWithoutUndo();
            report.AppendLine($"evening: {to.FindProperty("windows").arraySize} windows, {to.FindProperty("lamps").arraySize} lamps, {lights.arraySize} lights");
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
            var map = Root(expansion.scene, "MyeongjiMap").transform;
            var solids = expansion.GetComponentsInChildren<Collider>(true)
                .Concat(new[] { "StreetGreenery", "EntranceDetails" }.SelectMany(group => map.Find(group).GetComponentsInChildren<Collider>(true))).ToArray();
            report.AppendLine($"expansion and old-area props: {solids.Length} colliders, {expansion.GetComponentsInChildren<MeshRenderer>(true).Length} expansion renderers");
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
            var planner = UnityEngine.Object.FindFirstObjectByType<HazardPlanner>(FindObjectsInactive.Include);
            var slots = (HazardCandidates)new SerializedObject(planner).FindProperty("candidates").objectReferenceValue;
            foreach (var slot in slots.Candidates) Test("hazard slot " + slot.type, slot.position);
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
