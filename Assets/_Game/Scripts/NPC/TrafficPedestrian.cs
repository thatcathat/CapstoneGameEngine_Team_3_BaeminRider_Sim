using UnityEngine;

namespace DeliveryRider
{
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class TrafficPedestrian : MonoBehaviour, IPunchable
    {
        [SerializeField] private PedestrianNetwork network;
        [SerializeField] private int startPath;
        [SerializeField] private float startDistance;
        [SerializeField] private int seed = 7;
        [SerializeField] private Transform visual;
        [Header("Walking (test values)")]
        [SerializeField] private Vector2 walkingSpeed = new Vector2(1.1f, 1.65f);
        [SerializeField, Min(0f)] private float laneOffset = 0.35f;
        [SerializeField, Min(0.1f)] private float turnSpeed = 8f;
        [SerializeField, Min(0.1f)] private float obstacleReach = 1.1f;
        [SerializeField] private LayerMask obstacleMask = ~0;
        [Header("Punch response (test values)")]
        [SerializeField] private float punchSpeed = 5f;
        [SerializeField] private float punchLift = 3.5f;
        [SerializeField] private float minimumFlight = 0.5f;
        [SerializeField] private float returnTimeout = 6f;
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private readonly Collider[] overlaps = new Collider[16];
        private Rigidbody body;
        private System.Random random;
        private int path;
        private float distance, speed, waitUntil, hitAt, nextReturnAt, idleAt, nextObstacleAt;
        private bool flying, blocked;
        private float floorAt = float.NegativeInfinity;
        private Quaternion restVisual;
        private float visualSpin;

        public void Configure(PedestrianNetwork owner, int route, float along, int randomSeed, Transform appearance)
        { network = owner; startPath = route; startDistance = along; seed = randomSeed; visual = appearance; }
        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            random = new System.Random(seed);
            speed = Mathf.Lerp(walkingSpeed.x, walkingSpeed.y, (float)random.NextDouble());
            path = startPath; distance = startDistance;
            idleAt = Time.time + 8f + (float)random.NextDouble() * 15f;
            if (visual != null) restVisual = visual.localRotation;
            SetWalking();
        }

        private void SetWalking()
        {
            flying = false;
            body.isKinematic = true;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            if (visual != null) visual.localRotation = restVisual;
        }

        private void FixedUpdate()
        {
            if (network == null || path < 0 || path >= network.PathCount) return;
            if (network.Session != null && (network.Session.IsPaused || network.Session.IsSettled))
            {
                if (network.Session.IsSettled && flying) { body.linearVelocity = body.angularVelocity = Vector3.zero; }
                return;
            }
            if (flying)
            {
                bool landed = Time.time - floorAt <= Time.fixedDeltaTime * 2f && body.linearVelocity.magnitude < 1.5f;
                if (Time.time >= nextReturnAt && (Time.time - hitAt >= minimumFlight && landed
                    || Time.time - hitAt >= returnTimeout || body.position.y < -8f)) ReturnToPath();
                return;
            }
            if (Time.time < waitUntil) return;
            if (Time.time >= idleAt)
            {
                waitUntil = Time.time + 0.5f + (float)random.NextDouble();
                idleAt = Time.time + 10f + (float)random.NextDouble() * 20f;
                return;
            }
            network.Sample(path, distance, out var currentFeet, out var forward);
            if (Time.time >= nextObstacleAt)
            {
                blocked = Blocked(forward);
                nextObstacleAt = Time.time + 0.1f;
            }
            if (blocked) return;
            var currentRoute = network.Path(path);
            var currentTarget = currentFeet + Vector3.Cross(Vector3.up, forward)
                * Mathf.Min(laneOffset, Mathf.Max(0f, currentRoute.halfWidth - 0.35f));
            // An outer corner or U-turn is longer than the centre line. Catch up before advancing
            // route distance, otherwise repeated turns leave the actor increasingly behind its path.
            if ((currentTarget - body.position).sqrMagnitude > 0.0144f)
            {
                body.MovePosition(Vector3.MoveTowards(body.position, currentTarget, speed * Time.fixedDeltaTime));
                body.MoveRotation(Quaternion.Slerp(body.rotation, Quaternion.LookRotation(forward), turnSpeed * Time.fixedDeltaTime));
                return;
            }
            distance += speed * Time.fixedDeltaTime;
            var route = network.Path(path);
            if (distance >= route.length)
            {
                int next = network.Next(path, random);
                distance -= route.length;
                if (next == route.reverse) waitUntil = Time.time + 0.3f;
                path = next;
                nextObstacleAt = 0f;
                route = network.Path(path);
            }
            network.Sample(path, distance, out var feet, out forward);
            var offset = Vector3.Cross(Vector3.up, forward) * Mathf.Min(laneOffset, Mathf.Max(0f, route.halfWidth - 0.35f));
            var target = feet + offset;
            // Turn/side changes ease toward the new walking lane instead of teleporting sideways.
            body.MovePosition(Vector3.MoveTowards(body.position, target, speed * Time.fixedDeltaTime));
            body.MoveRotation(Quaternion.Slerp(body.rotation, Quaternion.LookRotation(forward), turnSpeed * Time.fixedDeltaTime));
        }

        private bool Blocked(Vector3 direction)
        {
            int count = Physics.CapsuleCastNonAlloc(body.position + Vector3.up * 0.35f, body.position + Vector3.up * 1.45f,
                0.28f, direction, hits, obstacleReach, obstacleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (hits[i].rigidbody != body && hits[i].normal.y < 0.7f) return true;
            return count == hits.Length;
        }

        public bool HasRoom(Vector3 feet)
        {
            int count = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * 0.35f, feet + Vector3.up * 1.45f,
                0.27f, overlaps, obstacleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) if (overlaps[i].attachedRigidbody != body) return false;
            return count < overlaps.Length;
        }

        public bool ReceivePunch(Vector3 direction)
        {
            if (network == null || flying || network.Session != null && (network.Session.IsPaused || network.Session.IsMapOpen || network.Session.IsSettled)) return false;
            flying = true; hitAt = Time.time; floorAt = float.NegativeInfinity;
            body.isKinematic = false;
            body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.WakeUp();
            var away = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            body.linearVelocity = away * punchSpeed + Vector3.up * punchLift;
            return true;
        }

        private void OnCollisionStay(Collision collision)
        {
            if (!flying) return;
            for (int i = 0; i < collision.contactCount; i++)
                if (collision.GetContact(i).normal.y > 0.7f) { floorAt = Time.time; break; }
        }

        private void ReturnToPath()
        {
            nextReturnAt = Time.time + 0.5f;
            if (!network.FindReturn(body.position, this, out var route, out var along, out var feet)) return;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            body.position = feet;
            path = route; distance = along;
            network.Sample(path, distance, out _, out var forward);
            body.rotation = Quaternion.LookRotation(forward);
            visualSpin = 0f;
            SetWalking();
            waitUntil = Time.time + 0.5f;
            blocked = false; nextObstacleAt = 0f;
        }

        private void LateUpdate()
        {
            if (visual == null || network == null || network.Session != null && (network.Session.IsPaused || network.Session.IsSettled)) return;
            if (flying)
            {
                visualSpin += 260f * Time.deltaTime;
                visual.localRotation = Quaternion.Euler(visualSpin, 0f, 12f) * restVisual;
            }
            else visual.localRotation = restVisual * Quaternion.Euler(0f, 0f, !blocked && Time.time >= waitUntil ? Mathf.Sin(Time.time * speed * 5f) * 3f : 0f);
        }
    }
}
