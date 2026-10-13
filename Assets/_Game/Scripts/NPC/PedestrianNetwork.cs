using System;
using UnityEngine;

namespace DeliveryRider
{
    [Serializable]
    public sealed class PedestrianPath
    {
        public Vector3[] points = Array.Empty<Vector3>();
        public float[] cumulative = Array.Empty<float>();
        public float length;
        public float halfWidth = 1.5f;
        public int reverse;
        public bool connector;
        public int[] next = Array.Empty<int>();
    }

    // Independent sidewalk traffic; no vehicle junction reservations or delivery recipient logic.
    public sealed class PedestrianNetwork : MonoBehaviour
    {
        [SerializeField] private DeliverySession session;
        [SerializeField] private PedestrianPath[] paths = Array.Empty<PedestrianPath>();
        [SerializeField] private TrafficPedestrian[] pedestrians = Array.Empty<TrafficPedestrian>();
        [SerializeField, Min(0.5f)] private float spawnSpacing = 3f;
        [SerializeField, Min(0)] private int population = 48;
        public DeliverySession Session => session;
        public int PathCount => paths.Length;
        public int PedestrianCount => pedestrians.Length;
        public int Population => population;
        private void Awake()
        {
            for (int i = 0; i < pedestrians.Length; i++)
                if (pedestrians[i] != null) pedestrians[i].gameObject.SetActive(i < population);
        }
        public PedestrianPath Path(int index) => paths[index];
        public void Configure(DeliverySession owner, PedestrianPath[] routes, TrafficPedestrian[] residents)
        { session = owner; paths = routes; pedestrians = residents; }

        public void Sample(int index, float distance, out Vector3 feet, out Vector3 forward)
        {
            var path = paths[index];
            int i = Array.BinarySearch(path.cumulative, Mathf.Clamp(distance, 0f, path.length));
            if (i < 0) i = ~i - 1;
            i = Mathf.Clamp(i, 0, path.points.Length - 2);
            float span = path.cumulative[i + 1] - path.cumulative[i];
            feet = Vector3.Lerp(path.points[i], path.points[i + 1], span > 0f ? (distance - path.cumulative[i]) / span : 0f);
            forward = Vector3.ProjectOnPlane(path.points[i + 1] - path.points[i], Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        }

        public int Next(int index, System.Random random)
        {
            var path = paths[index];
            return path.next.Length == 0 ? path.reverse : path.next[random.Next(path.next.Length)];
        }

        public bool FindReturn(Vector3 from, TrafficPedestrian self, out int pathIndex, out float distance, out Vector3 feet)
        {
            pathIndex = -1; distance = 0f; feet = from;
            float best = float.PositiveInfinity;
            for (int p = 0; p < paths.Length; p++)
            {
                var path = paths[p];
                if (path.connector) continue;
                for (int i = 0; i < path.points.Length - 1; i++)
                {
                    var a = path.points[i];
                    var ab = path.points[i + 1] - a;
                    float t = Mathf.Clamp01(Vector3.Dot(from - a, ab) / Mathf.Max(0.001f, ab.sqrMagnitude));
                    float along = Mathf.Lerp(path.cumulative[i], path.cumulative[i + 1], t);
                    Sample(p, along, out var position, out var direction);
                    position += Vector3.Cross(Vector3.up, direction) * Mathf.Min(0.35f, path.halfWidth - 0.35f);
                    float squared = (position - from).sqrMagnitude;
                    if (squared >= best || !Free(position, self) || !self.HasRoom(position)) continue;
                    best = squared; pathIndex = p; distance = along; feet = position;
                }
            }
            return pathIndex >= 0;
        }

        public bool Free(Vector3 feet, TrafficPedestrian self)
        {
            foreach (var other in pedestrians)
                if (other != null && other != self && other.gameObject.activeInHierarchy
                    && (other.transform.position - feet).sqrMagnitude < spawnSpacing * spawnSpacing) return false;
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.7f, 0.7f);
            foreach (var path in paths)
                for (int i = 1; i < path.points.Length; i++) Gizmos.DrawLine(path.points[i - 1], path.points[i]);
        }
    }
}
