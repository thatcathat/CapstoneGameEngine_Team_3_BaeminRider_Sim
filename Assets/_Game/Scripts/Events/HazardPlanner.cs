using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeliveryRider
{
    // Decides which hazard slots exist today and which triggers are live for the current call.
    // Rules follow the v5.2 trial build (roll() / objOn / ignition); every number is a test value.
    public sealed class HazardPlanner : MonoBehaviour
    {
        [SerializeField] private DeliverySession session;
        [SerializeField] private RiderMotor rider;
        [SerializeField] private HazardSlot[] slots = Array.Empty<HazardSlot>();
        [Header("Per day (index 0 = day 1, later days use the last value)")]
        [SerializeField] private float[] objectShare = { 0.4f, 0.55f, 0.7f, 0.85f };
        [SerializeField] private int[] triggersPerCall = { 5, 8, 10, 13 };
        [Header("Per call roll")]
        [SerializeField, Range(0f, 1f)] private float routeShare = 0.7f;
        [SerializeField, Range(0f, 1f)] private float typeGuarantee = 0.5f;
        [SerializeField, Range(0f, 1f)] private float comboFirst = 0.35f;
        [Header("Chain ignition")]
        [SerializeField, Range(0f, 1f)] private float igniteChance = 0.8f;
        [SerializeField, Range(0f, 1f)] private float igniteChanceFromObject = 0.3f;
        [SerializeField, Min(0f)] private float igniteRadius = 40f;
        [Tooltip("Ignition may add this many triggers above the per-call count.")]
        [SerializeField, Min(0)] private int igniteExtraLimit = 4;

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
            float share = PerDay(objectShare);
            foreach (var slot in slots)
            {
                if (slot == null) continue;
                bool unlocked = slot.UnlockDay <= day;
                bool placed = slot.Role == HazardRole.Object && unlocked && (!slot.Ranked || slot.Rank < share);
                slot.StrikeAllowed = slot.Role == HazardRole.Object;
                slot.gameObject.SetActive(placed);
            }
            OnSessionChanged();
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
                if (slot != null && slot.Role == HazardRole.Trigger) slot.StrikeAllowed = strike;
        }

        // A new call switches every trigger off, then lights about 70% near this call's route and the rest anywhere.
        private void Roll(DeliveryOrderOffer call)
        {
            pool.Clear();
            foreach (var slot in slots)
            {
                if (slot == null || slot.Role != HazardRole.Trigger) continue;
                slot.gameObject.SetActive(false);
                if (slot.UnlockDay <= day) pool.Add(slot);
            }
            liveTriggers = 0;
            perType.Clear();
            callTarget = Mathf.Min(PerDayCount(), pool.Count);
            int typeCount = CountTypes(pool);
            int typeCap = typeCount >= 4 ? Mathf.Max(2, Mathf.CeilToInt(callTarget * 0.25f)) : callTarget;

            // Shuffle with combo members pulled slightly forward, as the trial build does.
            order.Clear();
            foreach (var slot in pool) order[slot] = UnityEngine.Random.value - (slot.Combo >= 0 ? comboFirst : 0f);
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
                if (slot == null || slot.Role != HazardRole.Trigger || slot.gameObject.activeSelf || slot.UnlockDay > day) continue;
                if ((slot.transform.position - hit.transform.position).sqrMagnitude > radius) continue;
                if (UnityEngine.Random.Range(0, ++seen) == 0) pick = slot;
            }
            if (pick != null) Light(pick);
        }

        private void Light(HazardSlot slot)
        {
            Switch(slot);
            if (slot.Combo < 0) return;
            foreach (var other in slots)
                if (other != null && other != slot && other.Combo == slot.Combo && other.Role == HazardRole.Trigger
                    && other.UnlockDay <= day && !other.gameObject.activeSelf) Switch(other);
        }

        private void Switch(HazardSlot slot)
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
        private float PerDay(float[] values) => values.Length == 0 ? 1f : values[Mathf.Clamp(day - 1, 0, values.Length - 1)];

        private static int CountTypes(List<HazardSlot> list)
        {
            var seen = new HashSet<string>();
            foreach (var slot in list) seen.Add(slot.Type);
            return seen.Count;
        }
    }
}
