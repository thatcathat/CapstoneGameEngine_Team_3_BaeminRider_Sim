using UnityEngine;

namespace DeliveryRider
{
    // A road vehicle on the TrafficNetwork: follows lanes and junction connectors, keeps distance to the car ahead,
    // waits for junction reservations and for room on the exit lane, and brakes for people or props in its lane.
    // Kinematic; hitting the rider knocks them away (same rule as the former pilot vans).
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class TrafficCar : MonoBehaviour
    {
        [Header("Driving (test values)")]
        [SerializeField, Min(0.1f)] private float cruiseSpeed = 12f;
        [SerializeField, Min(0.1f)] private float acceleration = 2.5f;
        [SerializeField, Min(0.1f)] private float braking = 7f;
        [SerializeField, Min(0.1f)] private float slopeSpeed = 9f;
        [SerializeField, Min(0.1f)] private float followGap = 2.5f;
        [SerializeField, Min(0.1f)] private float obstacleClearance = 1f;
        [SerializeField, Min(0f)] private float riderBrakeDelay = 2f;
        [SerializeField] private LayerMask obstacleMask = ~0;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Renderer brakeLights;
        private const int ObstacleScanInterval = 3;
        private readonly Collider[] hits = new Collider[16];
        private TrafficNetwork network;
        private Rigidbody body;
        private BoxCollider shape;
        private System.Random random;
        private float speed;
        private float contactHoldUntil;
        private int heldJunction = -1;
        private int scanFrame;
        private float cachedObstacle = float.PositiveInfinity;
        private float riderDetectedAt = -1f;

        public int PathIndex { get; private set; } = -1;
        public int NextIndex { get; private set; } = -1;
        public float Distance { get; private set; }
        private float length = -1f;
        public float Length => length > 0f ? length : length = GetComponent<BoxCollider>().size.z;

        public void Paint(Color color)
        {
            if (bodyRenderer == null) return;
            var block = new MaterialPropertyBlock();
            bodyRenderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            bodyRenderer.SetPropertyBlock(block);
        }

        public void Initialize(TrafficNetwork owner, int path, float distance, int randomSeed)
        {
            network = owner;
            riderDetectedAt = -1f;
            random = new System.Random(randomSeed);
            body = GetComponent<Rigidbody>();
            shape = GetComponent<BoxCollider>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            PathIndex = path;
            Distance = distance;
            NextIndex = network.ChooseNext(path, random);
            network.Enter(this, path);
            network.Sample(path, distance, out var position, out var forward);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
            speed = cruiseSpeed * 0.5f;
        }

        private void FixedUpdate()
        {
            if (network == null || PathIndex < 0) return;
            var session = network.Session;
            if (session != null && (session.IsPaused || session.IsSettled)) { speed = 0f; return; }
            float step = Time.fixedDeltaTime;
            var path = network.Path(PathIndex);
            float half = Length * 0.5f;
            float stopping = speed * speed / (2f * braking);
            float lookAhead = Mathf.Max(8f, stopping + followGap + 6f);

            // Car ahead along the planned paths.
            float available = network.GapAhead(this, lookAhead) - followGap;

            // Junction ahead: reserve it and make sure the exit lane has room, otherwise stop at the lane end.
            if (!path.IsConnector && NextIndex >= 0)
            {
                var connector = network.Path(NextIndex);
                float toStopLine = path.length - Distance - half - 0.3f;
                if (connector.IsConnector && toStopLine < stopping + 4f && heldJunction != connector.junction)
                {
                    int exitLane = connector.next.Length > 0 ? connector.next[0] : -1;
                    bool room = exitLane < 0 || network.FreeEntry(exitLane) > Length + followGap + 1f;
                    if (room && network.TryEnterJunction(connector.junction, this, NextIndex)) heldJunction = connector.junction;
                    else
                    {
                        // Waiting for exit room must not hold a place in the junction queue.
                        if (!room) network.LeaveJunction(connector.junction, this);
                        available = Mathf.Min(available, toStopLine);
                    }
                }
            }

            // People, residents and props in the lane (other cars are handled above).
            if (++scanFrame >= ObstacleScanInterval || speed < 0.5f)
            {
                scanFrame = 0;
                cachedObstacle = ObstacleDistance(Mathf.Min(14f, stopping + obstacleClearance + 4f));
            }
            available = Mathf.Min(available, cachedObstacle - obstacleClearance);

            network.Sample(PathIndex, Distance, out _, out var forward);
            float desired = Mathf.Min(cruiseSpeed, path.speedLimit);
            if (Mathf.Abs(forward.y) > 0.03f) desired = Mathf.Min(desired, slopeSpeed);
            desired = Mathf.Min(desired, Mathf.Sqrt(2f * braking * Mathf.Max(0f, available)));
            if (Time.time < contactHoldUntil) desired = 0f;
            speed = Mathf.MoveTowards(speed, desired, (desired < speed ? braking : acceleration) * step);
            float travel = Mathf.Clamp(Mathf.Min(speed * step, available), 0f, speed * step);
            if (travel < speed * step) speed = travel / step;
            Advance(travel);

            network.Sample(PathIndex, Distance, out var position, out forward);
            body.MovePosition(position);
            body.MoveRotation(Quaternion.LookRotation(forward, Vector3.up));
            if (brakeLights != null) brakeLights.enabled = desired < cruiseSpeed * 0.7f;
        }

        private void Advance(float travel)
        {
            Distance += travel;
            int guard = 0;
            while (Distance > network.Path(PathIndex).length && guard++ < 4)
            {
                if (NextIndex < 0) { Distance = network.Path(PathIndex).length; return; }
                Distance -= network.Path(PathIndex).length;
                bool leavingConnector = network.Path(PathIndex).IsConnector;
                network.Leave(this, PathIndex);
                PathIndex = NextIndex;
                network.Enter(this, PathIndex);
                NextIndex = network.ChooseNext(PathIndex, random);
                if (leavingConnector && heldJunction >= 0)
                {
                    network.LeaveJunction(heldJunction, this);
                    heldJunction = -1;
                }
            }
        }

        // Steps along the planned lane ahead and returns the distance to the first non-vehicle collider.
        private float ObstacleDistance(float scan)
        {
            var half = new Vector3(shape.size.x * 0.5f + 0.1f, shape.size.y * 0.5f, 0.6f);
            float obstacleDistance = float.PositiveInfinity;
            float riderDistance = float.PositiveInfinity;
            for (float d = 1f; d <= scan; d += 1f)
            {
                float along = Distance + Length * 0.5f + d;
                int path = PathIndex;
                if (along > network.Path(path).length && NextIndex >= 0) { along -= network.Path(path).length; path = NextIndex; }
                network.Sample(path, along, out var p, out var forward);
                var rotation = Quaternion.LookRotation(forward, Vector3.up);
                int count = Physics.OverlapBoxNonAlloc(p + Vector3.up * shape.center.y, half, hits, rotation, obstacleMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    var hit = hits[i];
                    if (hit.attachedRigidbody == body || hit is MeshCollider || hit.GetComponentInParent<TrafficCar>() != null) continue;
                    if (hit.attachedRigidbody != null && hit.attachedRigidbody.TryGetComponent<RiderMotor>(out _))
                        riderDistance = Mathf.Min(riderDistance, d);
                    else
                        obstacleDistance = Mathf.Min(obstacleDistance, d);
                }
            }
            // Delay only the rider response; other obstacles and junctions still stop the car normally.
            if (float.IsPositiveInfinity(riderDistance)) riderDetectedAt = -1f;
            else
            {
                if (riderDetectedAt < 0f) riderDetectedAt = Time.time;
                bool mapOpen = network.Session != null && network.Session.IsMapOpen;
                if (mapOpen || Time.time - riderDetectedAt >= riderBrakeDelay)
                    obstacleDistance = Mathf.Min(obstacleDistance, riderDistance);
            }
            return obstacleDistance;
        }

        private void OnCollisionEnter(Collision collision)
        {
            float impactSpeed = speed;
            riderDetectedAt = Time.time - riderBrakeDelay;
            speed = 0f;
            contactHoldUntil = Time.time + 0.5f;
            if (impactSpeed < 1f || collision.rigidbody == null || !collision.rigidbody.TryGetComponent<RiderMotor>(out var rider)) return;
            var away = Vector3.ProjectOnPlane(rider.transform.position - body.position, Vector3.up).normalized;
            rider.TryTrafficCrash(away * Mathf.Min(impactSpeed, 4f) + Vector3.up * 2f, 1.1f);
        }

        private void OnCollisionStay(Collision collision) { speed = 0f; contactHoldUntil = Time.time + 0.2f; }

        private void OnDisable()
        {
            if (network == null) return;
            network.Leave(this, PathIndex);
            if (heldJunction >= 0) network.LeaveJunction(heldJunction, this);
            heldJunction = -1;
        }
    }
}
