using UnityEngine;

namespace DeliveryRider
{
    // Ducks emerge from a nearby building and join the crossing at the kerb.
    public sealed class CrossingFlockHazard : MonoBehaviour, IPlacedHazard
    {
        public const int FlockSize = 10;
        private enum Phase { Ready, Crossing, Spent }
        [SerializeField] private DeliverySession session;
        [SerializeField] private Transform[] birds = System.Array.Empty<Transform>();
        [Tooltip("Crossing line in local space; birds walk from start to end.")]
        [SerializeField] private Vector3 localStart;
        [SerializeField] private Vector3 localEnd;
        [Tooltip("Close range that always starts the crossing (and must be left before it re-arms).")]
        [SerializeField] private Vector3 approachCenter = new Vector3(0f, 1f, 0f);
        [SerializeField] private Vector3 approachSize = new Vector3(12f, 3f, 16f);
        [Tooltip("Minimum trigger reach in each direction along the road, including previously placed flocks.")]
        [SerializeField, Min(0f)] private float approachReach = 30f;
        private Vector3 TriggerSize => new Vector3(approachSize.x, approachSize.y, Mathf.Max(approachSize.z, approachReach * 2f));

        [Header("Test values")]
        [Tooltip("Furthest distance along the road at which the timed start looks for an approaching rider.")]
        [SerializeField, Min(0f)] private float maximumLeadDistance = 50f;
        [Tooltip("Start this much earlier than the computed meeting time, so the line is already on the road.")]
        [SerializeField, Min(0f)] private float arrivalMargin = 0.3f;
        private Vector3 entrance;
        [SerializeField, Min(0.1f)] private float walkSpeed = 2.6f;
        [SerializeField, Min(0.1f)] private float spacing = 1.1f;
        [SerializeField, Min(0.1f)] private float hitRadius = 0.7f;
        [SerializeField, Min(0f)] private float bounceImpulse = 6.5f;
        [SerializeField, Min(0f)] private float bounceLift = 3f;
        [SerializeField, Min(0.1f)] private float recoverySeconds = 0.8f;
        private RiderMotor rider;
        private CapsuleCollider riderCapsule;
        private HazardSlot slot;
        private Phase phase;
        private float head;
        private float jumpHeight;
        private float jumpVelocity;
        private bool contactConsumed;

        // Runtime spawn: the ducks cross kerb to kerb (2 m past each kerb), from the slot's side.
        public void Place(DeliverySession owner, RoadNetwork ground, HazardCandidate spot)
        {
            session = owner;
            GetKerbs(ground, spot, out var first, out var second);
            localStart = transform.InverseTransformPoint(first);
            localEnd = transform.InverseTransformPoint(second);
            approachSize = new Vector3(spot.halfWidth * 2f + 4f, 3f, 16f);
        }

        private void Awake()
        {
            rider = session != null && session.Player != null ? session.Player.GetComponent<RiderMotor>() : null;
            if (rider == null || birds.Length == 0) { enabled = false; return; }
            // Expand flocks serialized before the ten-duck pattern.
            if (birds.Length < FlockSize)
            {
                int existing = birds.Length;
                System.Array.Resize(ref birds, FlockSize);
                for (int i = existing; i < birds.Length; i++)
                {
                    birds[i] = Instantiate(birds[0], birds[0].parent);
                    birds[i].name = "Duck_" + i;
                }
            }
            riderCapsule = rider.GetComponent<CapsuleCollider>();
            slot = GetComponent<HazardSlot>();
            if (!FindEntrance()) { HideBirds(); enabled = false; return; }
            HideBirds();
        }

        private void OnDisable()
        {
            if (rider != null) rider.Jumped -= OnRiderJumped;
            HideBirds();
            phase = Phase.Spent;
        }

        private void OnEnable()
        {
            if (rider == null) return;
            rider.Jumped += OnRiderJumped;
            jumpHeight = jumpVelocity = 0f;
            phase = Phase.Ready;
            ParkBirds();
        }

        private void OnRiderJumped(float lift)
        {
            if (phase != Phase.Spent && jumpHeight == 0f) jumpVelocity = lift;
        }

        private void Update() { if (session.IsSettled && phase == Phase.Crossing) Finish(); }

        private void FixedUpdate()
        {
            if (session.IsPaused || session.IsSettled) return;
            if (session.IsMapOpen && phase == Phase.Crossing) { Finish(); return; }
            if (jumpVelocity != 0f || jumpHeight > 0f)
            {
                jumpVelocity += Physics.gravity.y * Time.fixedDeltaTime;
                jumpHeight = Mathf.Max(0f, jumpHeight + jumpVelocity * Time.fixedDeltaTime);
                if (jumpHeight == 0f) jumpVelocity = 0f;
            }
            switch (phase)
            {
                case Phase.Ready:
                    if ((slot == null || slot.StrikeAllowed) && !session.IsMapOpen && !rider.IsRecovering && (InApproach() || MeetsRider())) Begin();
                    break;
                case Phase.Crossing:
                    Advance();
                    break;
                case Phase.Spent:
                    // Leaving the approach area re-arms this crossing without a new call.
                    if (!InApproach()) { phase = Phase.Ready; ParkBirds(); }
                    break;
            }
        }

        // True when the rider, heading for the crossing line, would reach it no later than the middle of the duck line
        // reaches the rider's lane (with arrivalMargin to spare).
        private bool MeetsRider()
        {
            Vector3 start = transform.TransformPoint(localStart), end = transform.TransformPoint(localEnd);
            var line = Vector3.ProjectOnPlane(end - start, Vector3.up);
            float length = line.magnitude;
            if (length < 0.1f) return false;
            var local = transform.InverseTransformPoint(rider.transform.position);
            if (Mathf.Abs(local.z) > maximumLeadDistance || Mathf.Abs(local.y) > 3f || Mathf.Abs(local.x - approachCenter.x) > approachSize.x * 0.5f) return false;
            float closing = -Mathf.Sign(local.z) * transform.InverseTransformDirection(rider.Velocity).z;
            if (closing < 0.5f) return false;
            float lateral = Mathf.Clamp(Vector3.Dot(Vector3.ProjectOnPlane(rider.transform.position - start, Vector3.up), line / length), 0f, length);
            float ducks = (Vector3.Distance(entrance, start) + lateral + (birds.Length - 1) * spacing * 0.5f) / walkSpeed;
            return Mathf.Abs(local.z) / closing <= ducks + arrivalMargin;
        }

        private void Begin()
        {
            head = 0f;
            contactConsumed = false;
            phase = Phase.Crossing;
        }

        private bool FindEntrance()
        {
            Vector3 first = transform.TransformPoint(localStart);
            Vector3 second = transform.TransformPoint(localEnd);
            if (!TryEntrance(first, second, out entrance, out bool reverse)) return false;
            if (reverse) (localStart, localEnd) = (localEnd, localStart);
            return true;
        }

        internal static bool CanPlace(RoadNetwork ground, HazardCandidate spot)
        {
            GetKerbs(ground, spot, out var first, out var second);
            return TryEntrance(first, second, out _, out _);
        }

        private static void GetKerbs(RoadNetwork ground, HazardCandidate spot, out Vector3 first, out Vector3 second)
        {
            var across = spot.rotation * Vector3.right * (spot.side * (spot.halfWidth + 2f));
            first = ground.OnGround(spot.position + across, 0f);
            second = ground.OnGround(spot.position - across, 0f);
        }

        private static readonly float[] WallAngles = { 0f, 20f, -20f, 40f, -40f, 60f, -60f };

        private static bool TryEntrance(Vector3 first, Vector3 second, out Vector3 entrance, out bool reverse)
        {
            float nearest = 5f; // Maximum clear walk from the crossing start to a facade.
            entrance = default;
            bool found = FindWall(first, first - second, ref nearest, ref entrance);
            reverse = FindWall(second, second - first, ref nearest, ref entrance);
            return found || reverse;
        }

        private static bool FindWall(Vector3 kerb, Vector3 outward, ref float nearest, ref Vector3 entrance)
        {
            bool found = false;
            Vector3 direction = Vector3.ProjectOnPlane(outward, Vector3.up).normalized;
            foreach (float angle in WallAngles)
            {
                Vector3 ray = Quaternion.AngleAxis(angle, Vector3.up) * direction;
                if (!Physics.Raycast(kerb + Vector3.up, ray, out var hit, nearest, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                for (Transform node = hit.transform; node != null; node = node.parent)
                {
                    if (!node.name.StartsWith("Building", System.StringComparison.OrdinalIgnoreCase) && node.name != "NeighborhoodBuildings") continue;
                    entrance = hit.point + ray * 0.45f - Vector3.up;
                    nearest = hit.distance;
                    found = true;
                    break;
                }
            }
            return found;
        }

        private void ParkBirds()
        {
            HideBirds();
            foreach (var bird in birds) bird.position = entrance;
        }

        private void Advance()
        {
            head += walkSpeed * Time.fixedDeltaTime;
            Vector3 start = transform.TransformPoint(localStart), end = transform.TransformPoint(localEnd);
            float approach = Vector3.Distance(entrance, start);
            float length = approach + Vector3.Distance(start, end);
            bool anyContact = false;
            Vector3 player = rider.transform.position;
            for (int i = 0; i < birds.Length; i++)
            {
                float along = head - i * spacing;
                if (along < 0f) continue;
                bool visible = along <= length;
                if (birds[i].gameObject.activeSelf != visible) birds[i].gameObject.SetActive(visible);
                if (!visible) continue;
                Vector3 from = along < approach ? entrance : start;
                Vector3 to = along < approach ? start : end;
                float distance = along < approach ? along : along - approach;
                birds[i].position = Vector3.MoveTowards(from, to, distance) + Vector3.up * jumpHeight;
                birds[i].rotation = Quaternion.LookRotation(to - from);
                Vector3 offset = player - birds[i].position;
                Vector3 duckCenter = birds[i].TransformPoint(new Vector3(0f, 0.3f, 0f));
                bool touching = riderCapsule != null
                    ? (riderCapsule.ClosestPoint(duckCenter) - duckCenter).sqrMagnitude <= hitRadius * hitRadius
                    : Mathf.Abs(offset.y) <= 1.8f && new Vector2(offset.x, offset.z).sqrMagnitude <= hitRadius * hitRadius;
                if (!touching) continue;
                anyContact = true;
                if (contactConsumed) continue;
                var push = Vector3.ProjectOnPlane(offset, Vector3.up).normalized;
                if (push.sqrMagnitude < 0.01f) push = -transform.forward;
                contactConsumed = rider.TryHazardCrash(this, push * bounceImpulse + Vector3.up * bounceLift, recoverySeconds, allowJumpRecontact: true);
            }
            if (!anyContact) contactConsumed = false;
            if (head - (birds.Length - 1) * spacing > length) Finish();
        }

        private bool InApproach()
        {
            Vector3 p = transform.InverseTransformPoint(rider.transform.position) - approachCenter;
            Vector3 half = TriggerSize * 0.5f;
            return Mathf.Abs(p.x) <= half.x && Mathf.Abs(p.y) <= half.y && Mathf.Abs(p.z) <= half.z;
        }

        private void Finish() { HideBirds(); phase = Phase.Spent; }
        private void HideBirds() { foreach (var bird in birds) if (bird != null) bird.gameObject.SetActive(false); }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(approachCenter, TriggerSize);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(localStart, localEnd);
        }
    }
}
