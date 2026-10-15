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
        [Header("Avoidance (test values)")]
        [Tooltip("How long to keep to the passing side before stepping back into the walking lane.")]
        [SerializeField, Min(0f)] private float avoidHoldSeconds = 1.2f;
        [Tooltip("Turn back when neither side of the sidewalk is free for this long.")]
        [SerializeField, Min(0f)] private float turnBackSeconds = 1f;
        [Header("Punch response (test values)")]
        [SerializeField] private float punchSpeed = 5f;
        [SerializeField] private float punchLift = 3.5f;
        [SerializeField] private float minimumFlight = 0.5f;
        [SerializeField] private float returnTimeout = 6f;
        [Header("Body knock and chain (test values)")]
        [Tooltip("Rider speed needed to bowl a walker over by running into them.")]
        [SerializeField, Min(0f)] private float knockMinimumSpeed = 4f;
        [Tooltip("Share of the rider's speed the bowled walker flies off with.")]
        [SerializeField, Min(0f)] private float knockSpeedShare = 1.2f;
        [Tooltip("A flying pedestrian at least this fast knocks over the walkers it hits.")]
        [SerializeField, Min(0f)] private float chainMinimumSpeed = 4f;
        [SerializeField, Range(0f, 1f)] private float chainSpeedShare = 0.7f;
        [Tooltip("How many times one hit may be passed on from pedestrian to pedestrian.")]
        [SerializeField, Min(0)] private int chainLimit = 2;
        [Tooltip("A pedestrian sent flying by another one knocks the rider down when flying at them this fast.")]
        [SerializeField, Min(0f)] private float riderHitMinimumSpeed = 6f;
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
        // Signed sideways offset from the path centre; positive keeps right. Avoidance moves it across the sidewalk.
        private float sideOffset, avoidUntil, blockedSince = -1f;
        private int chainDepth;
        private Vector3 flightVelocity;

        private bool CanReact => network != null && !flying
            && (network.Session == null || !network.Session.IsPaused && !network.Session.IsSettled);

        public void Configure(PedestrianNetwork owner, int route, float along, int randomSeed, Transform appearance)
        { network = owner; startPath = route; startDistance = along; seed = randomSeed; visual = appearance; }
        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            random = new System.Random(seed);
            speed = Mathf.Lerp(walkingSpeed.x, walkingSpeed.y, (float)random.NextDouble());
            path = startPath; distance = startDistance;
            sideOffset = laneOffset;
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
                // Velocity before this physics step, so a contact sees the speed the flight hit with.
                flightVelocity = body.linearVelocity;
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
            var currentRoute = network.Path(path);
            var right = Vector3.Cross(Vector3.up, forward);
            if (Time.time >= nextObstacleAt)
            {
                nextObstacleAt = Time.time + 0.1f;
                blocked = Blocked(body.position, forward) && !TrySidestep(currentFeet, forward, right, currentRoute);
                if (!blocked && Time.time >= avoidUntil) TryReturnToLane(currentFeet, forward, right, currentRoute);
            }
            if (blocked)
            {
                if (blockedSince < 0f) blockedSince = Time.time;
                else if (Time.time - blockedSince >= turnBackSeconds) TurnBack(currentRoute);
                return;
            }
            blockedSince = -1f;
            var currentTarget = currentFeet + right * Lane(currentRoute);
            // An outer corner or U-turn is longer than the centre line. Catch up before advancing
            // route distance, otherwise repeated turns leave the actor increasingly behind its path.
            // Sidesteps use the same catch-up, so the walker moves across before going on.
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
            var target = feet + Vector3.Cross(Vector3.up, forward) * Lane(route);
            // Turn/side changes ease toward the new walking lane instead of teleporting sideways.
            body.MovePosition(Vector3.MoveTowards(body.position, target, speed * Time.fixedDeltaTime));
            body.MoveRotation(Quaternion.Slerp(body.rotation, Quaternion.LookRotation(forward), turnSpeed * Time.fixedDeltaTime));
        }

        private static float Limit(PedestrianPath route) => Mathf.Max(0f, route.halfWidth - 0.35f);
        private float Lane(PedestrianPath route) => Mathf.Clamp(sideOffset, -Limit(route), Limit(route));

        // Passes on the left first, as when overtaking, then tries the right edge of the sidewalk.
        private bool TrySidestep(Vector3 feet, Vector3 forward, Vector3 right, PedestrianPath route)
        {
            float limit = Limit(route), current = Lane(route);
            for (int side = -1; side <= 1; side += 2)
            {
                float offset = side * limit;
                if (Mathf.Abs(offset - current) < 0.3f) continue;
                var spot = feet + right * offset;
                if (!HasRoom(spot) || Blocked(spot, forward)) continue;
                sideOffset = offset;
                avoidUntil = Time.time + avoidHoldSeconds;
                return true;
            }
            return false;
        }

        private void TryReturnToLane(Vector3 feet, Vector3 forward, Vector3 right, PedestrianPath route)
        {
            if (Mathf.Approximately(sideOffset, laneOffset)) return;
            var spot = feet + right * Mathf.Clamp(laneOffset, -Limit(route), Limit(route));
            if (HasRoom(spot) && !Blocked(spot, forward)) sideOffset = laneOffset;
            else avoidUntil = Time.time + 0.3f;
        }

        // Both sides stayed blocked: walk the same sidewalk the other way instead of waiting forever.
        private void TurnBack(PedestrianPath route)
        {
            distance = Mathf.Clamp(route.length - distance, 0f, route.length);
            path = route.reverse;
            sideOffset = laneOffset;
            avoidUntil = 0f;
            blocked = false; blockedSince = -1f; nextObstacleAt = 0f;
            waitUntil = Time.time + 0.3f;
        }

        private bool Blocked(Vector3 feet, Vector3 direction)
        {
            int count = Physics.CapsuleCastNonAlloc(feet + Vector3.up * 0.35f, feet + Vector3.up * 1.45f,
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
            if (!CanReact || network.Session != null && network.Session.IsMapOpen) return false;
            var away = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            Launch(away * punchSpeed + Vector3.up * punchLift, 0);
            return true;
        }

        // Running into a walker at speed bowls them over. Never a delivery or food recovery; those need a punch.
        public bool TryBodyKnock(Vector3 riderVelocity)
        {
            var horizontal = Vector3.ProjectOnPlane(riderVelocity, Vector3.up);
            if (!CanReact || horizontal.magnitude < knockMinimumSpeed) return false;
            Launch(horizontal * knockSpeedShare + Vector3.up * punchLift, 0);
            return true;
        }

        private void Launch(Vector3 velocity, int depth)
        {
            flying = true; hitAt = Time.time; floorAt = float.NegativeInfinity;
            chainDepth = depth;
            blocked = false; blockedSince = -1f;
            body.isKinematic = false;
            body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.WakeUp();
            body.linearVelocity = flightVelocity = velocity;
        }

        // A fast flier passes the hit on: walkers it reaches fall over, and a pedestrian sent flying
        // by another one can bowl the rider over. The rider's own victims never knock the rider down.
        private void OnCollisionEnter(Collision collision)
        {
            if (!flying || collision.rigidbody == null) return;
            var horizontal = Vector3.ProjectOnPlane(flightVelocity, Vector3.up);
            float flightSpeed = horizontal.magnitude;
            if (collision.rigidbody.TryGetComponent<TrafficPedestrian>(out var other))
            {
                if (chainDepth < chainLimit && flightSpeed >= chainMinimumSpeed && other.CanReact)
                    other.Launch((horizontal + Vector3.up * punchLift) * chainSpeedShare, chainDepth + 1);
            }
            else if (chainDepth > 0 && flightSpeed >= riderHitMinimumSpeed && collision.rigidbody.TryGetComponent<RiderMotor>(out var rider)
                && Vector3.Dot(horizontal, rider.transform.position - body.position) > 0f)
                rider.TryContactCrash(horizontal / flightSpeed * Mathf.Min(flightSpeed, 4f) + Vector3.up * 2f, 1.1f);
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
            sideOffset = laneOffset; avoidUntil = 0f;
            blocked = false; blockedSince = -1f; nextObstacleAt = 0f;
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
