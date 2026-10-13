using UnityEngine;

namespace DeliveryRider
{
    // Points describe a closed lane at vehicle pivot height, including its elevation.
    public sealed class TrafficRoute : MonoBehaviour
    {
        [SerializeField] private Vector3[] points = System.Array.Empty<Vector3>();
        private float[] distances;
        public float Length { get; private set; }

        private void Awake() => Rebuild();
        public void Configure(Vector3[] localPoints) { points = localPoints; Rebuild(); }

        public void Rebuild()
        {
            distances = new float[points.Length + 1];
            Length = 0;
            if (points.Length < 3) return;
            for (int i = 0; i < points.Length; i++)
            {
                Length += Vector3.Distance(transform.TransformPoint(points[i]),
                    transform.TransformPoint(points[(i + 1) % points.Length]));
                distances[i + 1] = Length;
            }
        }

        public void Sample(float distance, out Vector3 position, out Vector3 forward)
        {
            position = transform.position;
            forward = transform.forward;
            if (Length <= 0.01f || distances == null) return;
            distance = Mathf.Repeat(distance, Length);
            int segment = System.Array.BinarySearch(distances, distance);
            if (segment < 0) segment = ~segment - 1;
            segment = Mathf.Clamp(segment, 0, points.Length - 1);
            var a = transform.TransformPoint(points[segment]);
            var b = transform.TransformPoint(points[(segment + 1) % points.Length]);
            float length = distances[segment + 1] - distances[segment];
            position = Vector3.Lerp(a, b, length > 0 ? (distance - distances[segment]) / length : 0);
            forward = (b - a).normalized;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < points.Length; i++)
                Gizmos.DrawLine(transform.TransformPoint(points[i]), transform.TransformPoint(points[(i + 1) % points.Length]));
        }
    }
}
