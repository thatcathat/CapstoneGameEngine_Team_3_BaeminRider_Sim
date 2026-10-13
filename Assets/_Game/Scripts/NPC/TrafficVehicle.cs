using UnityEngine;

namespace DeliveryRider
{
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class TrafficVehicle : MonoBehaviour
    {
        [SerializeField] private DeliverySession session;
        [SerializeField] private TrafficRoute route;
        [SerializeField] private TrafficJunction junction;
        [SerializeField, Min(0)] private float startDistance;
        [SerializeField, Min(0.1f)] private float cruiseSpeed = 5f;
        [SerializeField, Min(0.1f)] private float acceleration = 2f;
        [SerializeField, Min(0.1f)] private float braking = 8f;
        [SerializeField, Min(0.1f)] private float cornerSpeed = 2f;
        [SerializeField, Min(0.1f)] private float slopeSpeed = 3.5f;
        [SerializeField, Min(0.1f)] private float clearance = 0.7f;
        [SerializeField] private LayerMask obstacleMask = ~0;
        [SerializeField] private Renderer brakeLights;
        private readonly Collider[] hits = new Collider[32];
        private Rigidbody body;
        private BoxCollider shape;
        private float distance;
        private float speed;
        private float contactHoldUntil;
        private bool reserved;
        private bool entered;
        public float CurrentSpeed => speed;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            shape = GetComponent<BoxCollider>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        private void Start()
        {
            if (route == null || session == null || route.Length < 1)
            {
                Debug.LogError("Traffic vehicle requires a valid route and delivery session.", this);
                enabled = false;
                return;
            }
            distance = startDistance;
            route.Sample(distance, out var position, out var forward);
            body.position = position;
            body.rotation = Rotation(forward);
        }

        private static Quaternion Rotation(Vector3 forward) => Quaternion.LookRotation(forward, Vector3.up);

        private void FixedUpdate()
        {
            if (session == null || route == null || session.IsPaused || session.IsSettled) { speed = 0; return; }
            float step = Time.fixedDeltaTime;
            route.Sample(distance, out var position, out var forward);
            route.Sample(distance + 5, out _, out var ahead);
            float desired = Vector3.Angle(forward, ahead) > 12 ? cornerSpeed : cruiseSpeed;
            if (Mathf.Abs(forward.y) > 0.025f) desired = Mathf.Min(desired, slopeSpeed);
            float stopping = speed * speed / (2 * braking);
            float scan = Mathf.Max(4, stopping + clearance + speed * step + 1);
            float available = ObstacleDistance(scan);

            if (junction != null)
            {
                // Keep ownership until the rear bumper has cleared the intersection.
                bool inside = junction.Contains(position, shape.size.z * 0.5f + clearance);
                if (reserved && inside) entered = true;
                if (reserved && entered && !inside)
                {
                    junction.Release(this);
                    reserved = entered = false;
                }
                // A pedestrian blocking the approach must not reserve the crossing forever.
                if (reserved && !entered && available <= clearance)
                {
                    junction.Release(this);
                    reserved = false;
                }
                if (!reserved)
                {
                    bool approaching = false;
                    for (float d = 0; d <= scan + 1; d += 0.5f)
                    {
                        route.Sample(distance + d, out var next, out _);
                        if (!junction.Contains(next, shape.size.z * 0.5f + clearance)) continue;
                        approaching = true;
                        if (available <= d + clearance)
                        {
                            junction.Release(this);
                            break;
                        }
                        reserved = junction.Request(this);
                        if (!reserved) available = Mathf.Min(available, Mathf.Max(0, d - 0.5f));
                        break;
                    }
                    if (!approaching) junction.Release(this);
                }
            }
            desired = Mathf.Min(desired, Mathf.Sqrt(2 * braking * Mathf.Max(0, available - clearance)));
            if (Time.time < contactHoldUntil) desired = 0;
            speed = Mathf.MoveTowards(speed, desired, (desired < speed ? braking : acceleration) * step);
            // An emergency stop wins over easing when someone enters the remaining braking distance.
            float travel = Mathf.Min(speed * step, Mathf.Max(0, available - clearance));
            if (Time.time < contactHoldUntil) travel = speed = 0;
            else if (travel < speed * step) speed = travel / step;
            distance = Mathf.Repeat(distance + travel, route.Length);
            route.Sample(distance, out position, out forward);
            body.MovePosition(position);
            body.MoveRotation(Rotation(forward));
            if (brakeLights != null) brakeLights.enabled = desired < cruiseSpeed * 0.8f;
        }

        private float ObstacleDistance(float scan)
        {
            // Sample the lane instead of casting a straight ray through a bend or hill.
            var half = shape.size * 0.5f + new Vector3(0.12f, 0, 0.12f);
            for (float d = 0; d <= scan; d += 0.5f)
            {
                route.Sample(distance + d, out var p, out var forward);
                var rotation = Rotation(forward);
                int count = Physics.OverlapBoxNonAlloc(p + rotation * shape.center, half, hits,
                    rotation, obstacleMask, QueryTriggerInteraction.Ignore);
                if (count == hits.Length) return Mathf.Max(0, d - 0.5f);
                for (int i = 0; i < count; i++)
                    if (hits[i].attachedRigidbody != body)
                        return Mathf.Max(0, d - 0.5f);
            }
            return scan;
        }

        private void OnCollisionEnter(Collision collision)
        {
            float impactSpeed = speed;
            speed = 0;
            contactHoldUntil = Time.time + 0.5f;
            if (session == null || collision.rigidbody == null || impactSpeed < 1) return;
            if (!collision.rigidbody.TryGetComponent<RiderMotor>(out var rider)) return;
            var away = Vector3.ProjectOnPlane(rider.transform.position - body.position, Vector3.up).normalized;
            rider.TryTrafficCrash(away * Mathf.Min(impactSpeed, 4) + Vector3.up * 2, 1.1f);
        }

        private void OnCollisionStay(Collision collision) { speed = 0; contactHoldUntil = Time.time + 0.2f; }
        private void OnDisable() { if (junction != null) junction.Release(this); reserved = entered = false; }
    }
}
