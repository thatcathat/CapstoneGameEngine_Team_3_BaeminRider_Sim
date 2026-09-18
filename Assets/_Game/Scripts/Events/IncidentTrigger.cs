using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeliveryRider
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class IncidentTrigger : MonoBehaviour
    {
        [Serializable]
        private sealed class Entry
        {
            public IncidentDefinition definition;
            [Min(0f)] public float weight = 1f;
        }
        [SerializeField] private IncidentDirector director;
        [SerializeField] private Entry[] events = Array.Empty<Entry>();
        [SerializeField, Range(0f, 1f)] private float chance = 1f;
        [SerializeField, Min(0f)] private float cooldownSeconds = 20f;
        [SerializeField, Min(1)] private int maxPerDay = 3;
        [SerializeField] private Transform impactDirection;
        [SerializeField] private bool allowZone = true;
        [SerializeField] private bool allowTimed;
        [SerializeField, Min(0.1f)] private float timedRange = 6f;
        [SerializeField] private Transform thiefSpawn;
        [SerializeField] private Transform[] escapeRoute = Array.Empty<Transform>();
        private readonly HashSet<Collider> occupants = new HashSet<Collider>();
        private BoxCollider volume;
        private double availableAt;
        private int count;
        public Transform ThiefSpawn => thiefSpawn;
        public Transform[] EscapeRoute => escapeRoute;
        public bool HasEscapeRoute
        {
            get
            {
                if (thiefSpawn == null || escapeRoute.Length == 0) return false;
                foreach (var point in escapeRoute) if (point == null) return false;
                return true;
            }
        }
        public Vector3 ImpactDirection => Vector3.ProjectOnPlane(
            impactDirection != null ? impactDirection.forward : transform.forward, Vector3.up).normalized;

        private void Awake() { volume = GetComponent<BoxCollider>(); volume.isTrigger = true; }
        private void OnDisable() => occupants.Clear();
        private void OnTriggerEnter(Collider other)
        {
            var body = other.attachedRigidbody;
            if (director == null || body == null || body.GetComponent<RiderMotor>() != director.Rider) return;
            bool first = occupants.Count == 0;
            occupants.Add(other);
            if (first && allowZone) TryActivate(false);
        }

        public bool IsCandidate(bool timed)
        {
            if (!isActiveAndEnabled || director == null || !director.CanRequest || count >= maxPerDay || Time.timeAsDouble < availableAt) return false;
            if (timed && (!allowTimed || (director.Rider.transform.position - transform.position).sqrMagnitude > timedRange * timedRange)) return false;
            if (!timed && !allowZone) return false;
            foreach (var entry in events)
                if (Eligible(entry, timed)) return true;
            return false;
        }
        private bool Eligible(Entry entry, bool timed) => entry != null && entry.weight > 0f &&
            director.IsEligible(entry.definition) && entry.definition.CanUseSource(this, director.Rider.transform.position, timed);

        public bool TryActivate(bool timed)
        {
            if (!IsCandidate(timed)) return false;
            // One roll per entry, including failed rolls. Staying inside never retries.
            if (UnityEngine.Random.value >= chance) return false;
            float total = 0f;
            foreach (var entry in events)
                if (Eligible(entry, timed)) total += entry.weight;
            if (total <= 0f) return false;
            float roll = UnityEngine.Random.value * total;
            IncidentDefinition selected = null;
            foreach (var entry in events)
            {
                if (!Eligible(entry, timed)) continue;
                selected = entry.definition;
                roll -= entry.weight;
                if (roll <= 0f) break;
            }
            if (!director.TryStart(selected, this)) return false;
            count++;
            availableAt = Time.timeAsDouble + cooldownSeconds;
            return true;
        }
        private void OnTriggerExit(Collider other) => occupants.Remove(other);
        public bool Contains(Vector3 point)
        {
            if (!isActiveAndEnabled || volume == null) return false;
            var local = transform.InverseTransformPoint(point) - volume.center;
            var half = volume.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }
        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.7f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.DrawRay(box.center, Vector3.forward * 3f);
        }
    }
}
