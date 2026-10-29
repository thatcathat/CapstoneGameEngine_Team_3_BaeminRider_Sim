using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    public static class MusclemanInstaller
    {
        public const string PrefabPath = "Assets/_Game/Prefabs/Hazards/Muscleman.prefab";
        private const string SettingsPath = "Assets/_Game/Settings/HazardSpawnSettings.asset";
        private const string CandidatesPath = "Assets/_Game/Settings/HazardCandidates.asset";

        [MenuItem("Delivery Rider/Apply Muscleman Hazard")]
        public static void Apply() => ApplyToScene(SceneManager.GetActiveScene());

        // Adds only the new pattern; existing prefab settings and all other candidate positions are preserved.
        public static string ApplyToScene(Scene scene)
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Use edit mode after compilation.");
            if (scene.path != "Assets/_Game/Scenes/FourDistrictPrototype.unity") throw new InvalidOperationException("Open FourDistrictPrototype.");
            var components = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Component>(true)).ToArray();
            var session = components.OfType<DeliverySession>().Single();
            var network = components.OfType<PedestrianNetwork>().Single();
            var ground = components.OfType<RoadNetwork>().Single();
            var prefab = CreatePrefab(session);
            var settings = AssetDatabase.LoadAssetAtPath<HazardSpawnSettings>(SettingsPath);
            var kinds = settings.Kinds.ToList();
            var kind = kinds.FirstOrDefault(k => k.type == "muscleman");
            if (kind == null)
            {
                kind = new HazardSpawnSettings.Kind { type = "muscleman", role = HazardRole.Object, unlockDay = 1, countPerDay = new[] { 8, 8, 8, 8 } };
                kinds.Add(kind);
            }
            kind.prefab = prefab;
            settings.Configure(kinds.ToArray());
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            var asset = AssetDatabase.LoadAssetAtPath<HazardCandidates>(CandidatesPath);
            var places = asset.Candidates.Where(c => c.type != "muscleman").ToList();
            int prefix = places.Count == 0 ? 0 : places.Max(c => c.spot) + 1;
            var spots = asset.Spots.Take(prefix).ToList();
            int count = AppendCandidates(spots, places, session, network, ground);
            if (count == 0) throw new InvalidOperationException("No sidewalk candidates with a valid 20 m sidewalk approach and valid left/right corners.");
            asset.Configure(spots.ToArray(), places.ToArray());
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            return $"Muscleman: {count} sidewalk candidates; day-1 object, daily count preserved or initialised to 8. No Play Mode test.";
        }

        public static GameObject CreatePrefab(DeliverySession session)
        {
            var rider = session.Player.GetComponent<RiderMotor>();
            var shape = rider.GetComponent<CapsuleCollider>();
            var visual = rider.transform.Find("VisualRoot");
            var sourceMesh = visual != null ? visual.GetComponent<MeshFilter>() : rider.GetComponent<MeshFilter>();
            if (sourceMesh == null || sourceMesh.sharedMesh == null) throw new InvalidOperationException("Player capsule mesh required.");
            var sourceRenderer = sourceMesh.GetComponent<MeshRenderer>();
            var root = new GameObject("Muscleman");
            try
            {
                var trigger = root.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.center = new Vector3(0f, 2.5f, 0f);
                trigger.size = new Vector3(2f, 5f, 2f);
                var actor = new GameObject("Runner").transform;
                actor.SetParent(root.transform, false);
                actor.localScale = Vector3.one * 1.5f;
                var body = new GameObject("Body", typeof(MeshFilter), typeof(MeshRenderer));
                body.transform.SetParent(actor, false);
                body.transform.localPosition = Vector3.up * (shape.height * Mathf.Abs(shape.transform.lossyScale.y) * 0.5f);
                body.transform.localScale = sourceMesh.transform.lossyScale;
                body.GetComponent<MeshFilter>().sharedMesh = sourceMesh.sharedMesh;
                body.GetComponent<MeshRenderer>().sharedMaterials = sourceRenderer.sharedMaterials;
                actor.gameObject.SetActive(false);
                var hazard = root.AddComponent<MusclemanHazard>();
                var data = new SerializedObject(hazard);
                data.FindProperty("runner").objectReferenceValue = actor;
                data.FindProperty("runnerHeight").floatValue = shape.height * Mathf.Abs(shape.transform.lossyScale.y) * 1.5f;
                data.FindProperty("runnerRadius").floatValue = shape.radius * Mathf.Max(Mathf.Abs(shape.transform.lossyScale.x), Mathf.Abs(shape.transform.lossyScale.z)) * 1.5f;
                data.ApplyModifiedPropertiesWithoutUndo();
                bool success;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out success);
                if (!success || prefab == null) throw new InvalidOperationException("Could not save Muscleman prefab.");
                return prefab;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        public static int AppendCandidates(List<HazardCandidates.Spot> spots, List<HazardCandidate> places,
            DeliverySession session, PedestrianNetwork network, RoadNetwork ground)
        {
            var sessionData = new SerializedObject(session);
            var keepOut = new List<Vector3> { session.Player.position };
            foreach (string property in new[] { "shopPoints", "doorPoints" })
            {
                var array = sessionData.FindProperty(property);
                for (int i = 0; i < array.arraySize; i++)
                    if (array.GetArrayElementAtIndex(i).objectReferenceValue is Transform t) keepOut.Add(t.position);
            }
            var groundArray = new SerializedObject(ground).FindProperty("ground");
            var terrain = new HashSet<Collider>();
            for (int i = 0; i < groundArray.arraySize; i++) terrain.Add((Collider)groundArray.GetArrayElementAtIndex(i).objectReferenceValue);
            Physics.SyncTransforms();
            int count = 0;
            for (int p = 0; p < network.PathCount; p++)
            {
                var lane = network.Path(p);
                if (lane.connector || p > lane.reverse || lane.length < 8f || lane.halfWidth < 0.8f) continue;
                // Both travel directions must support a sidewalk turn; the player's heading chooses on activation.
                if (MusclemanHazard.ChooseTurn(network, p) < 0 || MusclemanHazard.ChooseTurn(network, lane.reverse) < 0) continue;
                for (float d = 3f; d <= lane.length - 3f; d += 12f)
                {
                    network.Sample(p, d, out var feet, out var forward);
                    if (keepOut.Any(v => Vector3.ProjectOnPlane(v - feet, Vector3.up).sqrMagnitude < 100f)) continue;
                    bool clear = true;
                    var approach = new List<int>();
                    foreach (int direction in new[] { p, lane.reverse })
                    {
                        float triggerDistance = direction == p ? d : lane.length - d;
                        if (!MusclemanHazard.TryApproach(network, direction, triggerDistance, 20f, 0.75f, 20f, approach, out float spawnDistance)) { clear = false; break; }
                        network.Sample(approach[0], spawnDistance, out var position, out _);
                        var hits = Physics.OverlapCapsule(position + Vector3.up * 0.8f, position + Vector3.up * 2.2f, 0.75f, ~0, QueryTriggerInteraction.Ignore);
                        if (hits.Any(h => h.attachedRigidbody == null && !terrain.Contains(h))) { clear = false; break; }
                    }
                    if (!clear) continue;
                    if (Physics.OverlapCapsule(feet + Vector3.up * 0.8f, feet + Vector3.up * 2.2f, 0.75f, ~0, QueryTriggerInteraction.Ignore)
                        .Any(h => h.attachedRigidbody == null && !terrain.Contains(h))) continue;
                    places.Add(new HazardCandidate { type = "muscleman", spot = spots.Count, position = feet,
                        rotation = Quaternion.LookRotation(forward), halfWidth = lane.halfWidth });
                    spots.Add(new HazardCandidates.Spot { position = feet });
                    count++;
                }
            }
            return count;
        }
    }
}
