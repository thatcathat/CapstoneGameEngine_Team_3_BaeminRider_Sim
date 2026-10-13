using System.Collections.Generic;
using UnityEngine;

namespace DeliveryRider
{
    // Cache road centres once; only inspect nearby ground/clearance when a recovery is needed.
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class RiderSafeRecovery : MonoBehaviour
    {
        [SerializeField] private RoadNetwork network;
        [SerializeField] private LayerMask groundMask = 1;
        [SerializeField, Min(1f)] private float sampleSpacing = 4f;
        [SerializeField, Min(0.01f)] private float groundClearance = 0.04f;
        private readonly List<Vector3> points = new List<Vector3>();
        private readonly List<float> widths = new List<float>();
        private readonly Collider[] overlaps = new Collider[32];
        private readonly RaycastHit[] groundHits = new RaycastHit[32];
        private CapsuleCollider capsule;
        private bool cached;
        private Vector3 start;

        private void Awake() { start = transform.position; CacheRoads(); }
        private void CacheRoads()
        {
            if (cached) return;
            cached = true;
            capsule = GetComponent<CapsuleCollider>();
            if (network == null) return;
            foreach (var road in network.Roads)
            {
                int count = Mathf.Max(1, Mathf.CeilToInt(road.Container.CalculateLength() / sampleSpacing));
                for (int i = 0; i <= count; i++)
                {
                    Vector3 position = road.Container.EvaluatePosition(i / (float)count);
                    points.Add(network.OnGround(position, 0f));
                    widths.Add(Mathf.Max(0.5f, road.HalfWidth - capsule.radius));
                }
            }
        }

        public bool IsSafe(Vector3 position)
        {
            CacheRoads();
            if (!TryStand(position, out var stand) || Mathf.Abs(position.y - stand.y) > 0.6f) return false;
            if (points.Count == 0) return HasRoom(stand);
            for (int i = 0; i < points.Count; i++)
            {
                var offset = position - points[i];
                // Half the sampling interval bridges the gaps between centre samples.
                float radius = widths[i] + sampleSpacing * 0.5f;
                if (offset.x * offset.x + offset.z * offset.z <= radius * radius
                    && Mathf.Abs(stand.y - (points[i].y + HalfHeight - capsule.center.y)) < 0.75f)
                    return HasRoom(stand);
            }
            return false;
        }

        public Vector3 FindSafePosition(Vector3 from, Vector3 fallback)
        {
            CacheRoads();
            Vector3 best = fallback;
            float closest = float.PositiveInfinity;
            foreach (var point in points)
            {
                float distance = (new Vector2(point.x - from.x, point.z - from.z)).sqrMagnitude;
                if (distance >= closest || !TryStand(point, out var stand) || !HasRoom(stand)) continue;
                // A roof above a road centre is not a valid recovery floor.
                if (Mathf.Abs(stand.y - (point.y + HalfHeight - capsule.center.y)) > 0.75f) continue;
                closest = distance;
                best = stand;
            }
            if (closest < float.PositiveInfinity) return best;
            if (TryStand(fallback, out var safeFallback) && HasRoom(safeFallback)) return safeFallback;
            if (TryStand(start, out var safeStart) && HasRoom(safeStart)) return safeStart;
            return fallback;
        }

        private float HalfHeight => capsule.height * Mathf.Abs(transform.lossyScale.y) * 0.5f;
        private bool TryStand(Vector3 position, out Vector3 stand)
        {
            stand = position;
            int count = Physics.RaycastNonAlloc(position + Vector3.up * 3f, Vector3.down, groundHits, 8f, groundMask, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            RaycastHit ground = default;
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (hit.rigidbody != null || hit.normal.y < 0.82f || hit.distance >= nearest
                    || hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
                nearest = hit.distance;
                ground = hit;
            }
            if (nearest == float.PositiveInfinity) return false;
            float slopeLift = capsule.radius * (1f / ground.normal.y - 1f);
            stand.y = ground.point.y + HalfHeight - capsule.center.y + groundClearance + slopeLift;
            return true;
        }
        private bool HasRoom(Vector3 position)
        {
            float radius = capsule.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)) * 0.95f;
            Vector3 centre = position + transform.TransformVector(capsule.center);
            float length = Mathf.Max(0f, HalfHeight - radius);
            int count = Physics.OverlapCapsuleNonAlloc(centre - Vector3.up * length, centre + Vector3.up * length,
                radius, overlaps, groundMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (overlaps[i].transform != transform && !overlaps[i].transform.IsChildOf(transform)) return false;
            return count < overlaps.Length;
        }
    }
}
