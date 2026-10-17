using System.Collections.Generic;
using UnityEngine;

namespace DeliveryRider
{
    // Spawns today's hazards at random baked candidates, then decides which triggers are live for the current call.
    // Objects: the day's count per type, always on. Triggers: a pool per type, a few lit per call.
    // Call rolls follow the v5.2 trial build (roll() / ignition); every number is a test value.
    public sealed class HazardPlanner : MonoBehaviour
    {
        [SerializeField] private DeliverySession session;
        [SerializeField] private RiderMotor rider;
        [SerializeField] private RoadNetwork ground;
        [SerializeField] private HazardSpawnSettings settings;
        [SerializeField] private HazardCandidates candidates;
        [Tooltip("Off: always use the seed below, to reproduce a layout.")]
        [SerializeField] private bool randomEachRun = true;
        [SerializeField] private int seed = 11;
        [Header("Per day (index 0 = day 1, later days use the last value)")]
        [SerializeField] private int[] triggersPerCall = { 5, 8, 10, 13 };
        [Header("Per call roll")]
        [SerializeField, Range(0f, 1f)] private float routeShare = 0.7f;
        [SerializeField, Range(0f, 1f)] private float typeGuarantee = 0.5f;
        [Header("Chain ignition")]
        [SerializeField, Range(0f, 1f)] private float igniteChance = 0.8f;
        [SerializeField, Range(0f, 1f)] private float igniteChanceFromObject = 0.3f;
        [SerializeField, Min(0f)] private float igniteRadius = 40f;
        [Tooltip("Ignition may add this many triggers above the per-call count.")]
        [SerializeField, Min(0)] private int igniteExtraLimit = 4;

        private readonly List<HazardSlot> slots = new List<HazardSlot>();
        private readonly List<HazardSlot> pool = new List<HazardSlot>();
        private readonly List<HazardSlot> nearRoute = new List<HazardSlot>();
        private readonly Dictionary<string, int> perType = new Dictionary<string, int>();
        private readonly Dictionary<HazardSlot, float> order = new Dictionary<HazardSlot, float>();
        private int day = 1;
        private int rolledOrder = -1;
        private int liveTriggers;
        private int callTarget;
        public int LiveTriggers => liveTriggers;

        private void Start()
        {
            day = session.Progress != null ? session.Progress.Day : 1;
            Spawn();
            OnSessionChanged();
        }

        // Each unlocked type takes its count from shuffled candidates, keeping the spacing to every hazard placed so far
        // (objects first, so their daily counts are met before the trigger pools fill the remaining room).
        private void Spawn()
        {
            if (settings == null || candidates == null || ground == null) { Debug.LogError("Hazard planner needs spawn settings, candidates and the road network.", this); return; }
            var random = randomEachRun ? new System.Random() : new System.Random(seed);
            var placed = new List<(Vector3 position, string type)>();
            var shuffled = new List<int>();
            // Prefabs are instantiated under an inactive parent so they can be fitted before they wake.
            var staging = new GameObject("HazardStaging");
            staging.SetActive(false);
            foreach (var role in new[] { HazardRole.Object, HazardRole.Trigger })
                foreach (var kind in settings.Kinds)
                {
                    if (kind.role != role || kind.prefab == null || kind.unlockDay > day) continue;
                    int want = kind.Count(day), count = 0;
                    shuffled.Clear();
                    for (int i = 0; i < candidates.Candidates.Count; i++)
                        if (candidates.Candidates[i].type == kind.type) shuffled.Add(i);
                    for (int i = shuffled.Count - 1; i > 0; i--) { int j = random.Next(i + 1); (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]); }
                    foreach (int index in shuffled)
                    {
                        if (count >= want) break;
                        var spot = candidates.Candidates[index];
                        if (!HasRoom(placed, spot.position, kind.type)) continue;
                        SpawnOne(kind, spot, staging.transform);
                        placed.Add((spot.position, kind.type));
                        count++;
                    }
                    if (count < want) Debug.LogWarning($"Hazards: placed {count} of {want} {kind.type} (not enough spaced candidates).", this);
                }
            Destroy(staging);
        }

        private bool HasRoom(List<(Vector3 position, string type)> placed, Vector3 position, string type)
        {
            foreach (var other in placed)
            {
                float gap = other.type == type ? settings.SameTypeSpacing : settings.Spacing;
                var offset = other.position - position;
                if (offset.x * offset.x + offset.z * offset.z < gap * gap) return false;
            }
            return true;
        }

        private void SpawnOne(HazardSpawnSettings.Kind kind, HazardCandidate spot, Transform staging)
        {
            var obj = Instantiate(kind.prefab, spot.position, spot.rotation, staging);
            obj.name = $"{slots.Count:00}_{kind.type}" + (spot.alley ? "_alley" : "");
            foreach (var hazard in obj.GetComponents<IPlacedHazard>()) hazard.Place(session, ground, spot);
            var slot = obj.AddComponent<HazardSlot>();
            slot.Configure(kind.type, kind.role, kind.unlockDay, candidates.Routes(spot.spot));
            // Objects stay on all day; triggers wait switched off until a call lights them.
            bool alwaysActive = kind.role == HazardRole.Object || kind.type == "chicken";
            slot.StrikeAllowed = alwaysActive;
            obj.SetActive(alwaysActive);
            obj.transform.SetParent(transform, true);
            slots.Add(slot);
        }

        private void OnEnable()
        {
            session.Changed += OnSessionChanged;
            if (rider != null) rider.HazardHit += OnHazardHit;
        }

        private void OnDisable()
        {
            session.Changed -= OnSessionChanged;
            if (rider != null) rider.HazardHit -= OnHazardHit;
        }

        private void OnSessionChanged()
        {
            var active = session.ActiveOrder;
            if (active != null && active.Number != rolledOrder)
            {
                rolledOrder = active.Number;
                Roll(active);
            }
            bool strike = active != null && session.HasPickedUp;
            foreach (var slot in slots)
                if (slot != null && IsCallTrigger(slot)) slot.StrikeAllowed = strike;
        }

        // A new call switches every trigger off, then lights about 70% near this call's route and the rest anywhere.
        private void Roll(DeliveryOrderOffer call)
        {
            pool.Clear();
            foreach (var slot in slots)
            {
                if (slot == null || !IsCallTrigger(slot)) continue;
                slot.gameObject.SetActive(false);
                if (slot.UnlockDay <= day) pool.Add(slot);
            }
            liveTriggers = 0;
            perType.Clear();
            callTarget = Mathf.Min(PerDayCount(), pool.Count);
            int typeCount = CountTypes(pool);
            int typeCap = typeCount >= 4 ? Mathf.Max(2, Mathf.CeilToInt(callTarget * 0.25f)) : callTarget;

            order.Clear();
            foreach (var slot in pool) order[slot] = UnityEngine.Random.value;
            pool.Sort((a, b) => order[a].CompareTo(order[b]));

            int route = RouteIndex(call);
            nearRoute.Clear();
            foreach (var slot in pool) if (slot.IsOnRoute(route)) nearRoute.Add(slot);

            // Each unlocked type gets a chance to show up once on the route.
            foreach (var slot in nearRoute)
            {
                if (liveTriggers >= callTarget) break;
                if (perType.ContainsKey(slot.Type) || UnityEngine.Random.value > typeGuarantee) { perType.TryAdd(slot.Type, 0); continue; }
                Light(slot);
            }
            int routeTarget = Mathf.RoundToInt(callTarget * routeShare);
            foreach (var slot in nearRoute)
            {
                if (liveTriggers >= routeTarget) break;
                if (!slot.gameObject.activeSelf && Count(slot.Type) < typeCap) Light(slot);
            }
            foreach (var slot in pool)
            {
                if (liveTriggers >= callTarget) break;
                if (!slot.gameObject.activeSelf && Count(slot.Type) < typeCap) Light(slot);
            }
        }

        // Getting hit lights one dormant trigger nearby, so trouble tends to follow trouble.
        private void OnHazardHit(UnityEngine.Object source)
        {
            if (session.ActiveOrder == null || !(source is Component component)) return;
            var hit = component.GetComponentInParent<HazardSlot>();
            if (hit == null) return;
            float chance = hit.Role == HazardRole.Object ? igniteChanceFromObject : igniteChance;
            if (UnityEngine.Random.value > chance || liveTriggers >= callTarget + igniteExtraLimit) return;
            HazardSlot pick = null;
            int seen = 0;
            float radius = igniteRadius * igniteRadius;
            foreach (var slot in slots)
            {
                if (slot == null || !IsCallTrigger(slot) || slot.gameObject.activeSelf || slot.UnlockDay > day) continue;
                if ((slot.transform.position - hit.transform.position).sqrMagnitude > radius) continue;
                if (UnityEngine.Random.Range(0, ++seen) == 0) pick = slot;
            }
            if (pick != null) Light(pick);
        }

        // Chickens stay active independently of calls and food; the other trigger types use the call pool.
        private static bool IsCallTrigger(HazardSlot slot) => slot.Role == HazardRole.Trigger && slot.Type != "chicken";

        private void Light(HazardSlot slot)
        {
            slot.StrikeAllowed = session.ActiveOrder != null && session.HasPickedUp;
            slot.gameObject.SetActive(true);
            liveTriggers++;
            perType[slot.Type] = Count(slot.Type) + 1;
        }

        private int RouteIndex(DeliveryOrderOffer call)
        {
            var table = session.RouteTable;
            return table == null || call.ShopIndex < 0 || call.DoorIndex < 0 ? -1 : call.ShopIndex * table.DoorCount + call.DoorIndex;
        }

        private int Count(string type) => perType.TryGetValue(type, out int value) ? value : 0;
        private int PerDayCount() => triggersPerCall.Length == 0 ? 0 : triggersPerCall[Mathf.Clamp(day - 1, 0, triggersPerCall.Length - 1)];

        private static int CountTypes(List<HazardSlot> list)
        {
            var seen = new HashSet<string>();
            foreach (var slot in list) seen.Add(slot.Type);
            return seen.Count;
        }
    }
}
