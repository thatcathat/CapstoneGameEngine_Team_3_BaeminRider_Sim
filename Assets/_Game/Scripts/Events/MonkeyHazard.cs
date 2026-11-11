using UnityEngine;

namespace DeliveryRider
{
    // The player remains a dynamic physics body throughout the carry, so traffic keeps its normal hit response.
    public sealed class MonkeyHazard : MonoBehaviour, IPlacedHazard
    {
        private enum Phase { Sitting, Jumping, Returning, Carrying, Spent }
        [SerializeField] private Transform model, leftArm, rightArm;
        [Header("Test values")]
        [SerializeField, Min(0.1f)] private float detectionRadius = 5f;
        [SerializeField, Min(0.1f)] private float jumpSeconds = 0.5f;
        [SerializeField, Min(0f)] private float jumpHeight = 0.8f;
        [Tooltip("Lead the player's horizontal velocity by this fraction of the jump duration. Target stays fixed after takeoff.")]
        [SerializeField, Range(0f, 1f)] private float jumpPredictionShare = 0.85f;
        [SerializeField, Min(0f)] private float maximumPredictionDistance = 6f;
        [SerializeField, Min(0.1f)] private float returnSpeed = 3f;
        [SerializeField, Min(0.1f)] private float contactRadius = 0.4f;
        [SerializeField, Min(0.1f)] private float carrySeconds = 4f;
        [SerializeField] private Vector2 turnInterval = new Vector2(0.35f, 0.8f);
        private DeliverySession session;
        private RoadNetwork ground;
        private RiderMotor rider;
        private CapsuleCollider riderShape;
        private HazardSlot slot;
        private Vector3 home, jumpStart, jumpTarget, heading;
        private Quaternion homeRotation;
        private Phase phase;
        private float elapsed, turnAt;
        private readonly RaycastHit[] hits = new RaycastHit[32];

        public void Place(DeliverySession owner, RoadNetwork roads, HazardCandidate spot) { session = owner; ground = roads; }
        private void Awake()
        {
            rider = session != null && session.Player != null ? session.Player.GetComponent<RiderMotor>() : null;
            if (rider == null || model == null) { enabled = false; return; }
            riderShape = rider.GetComponent<CapsuleCollider>();
            slot = GetComponent<HazardSlot>();
            home = transform.position;
            homeRotation = transform.rotation;
            Pose(false);
        }
        private void OnEnable()
        {
            if (rider == null) return;
            rider.ReactionChanged += OnReaction;
            rider.SafePositionRestored += OnSafeReturn;
        }
        private void OnDisable()
        {
            if (rider == null) return;
            rider.ReactionChanged -= OnReaction;
            rider.SafePositionRestored -= OnSafeReturn;
            rider.EndMonkeyCarry(this);
        }
        private void OnReaction(RiderReactionState state)
        {
            if (phase == Phase.Carrying && state != RiderReactionState.Normal) Finish();
        }
        private void OnSafeReturn() { if (phase == Phase.Carrying) Finish(); }
        private static Vector3 Flat(Vector3 value) => Vector3.ProjectOnPlane(value, Vector3.up);
        private Vector3 Ground(Vector3 point) => ground != null ? ground.OnGround(point, 0f) : new Vector3(point.x, home.y, point.z);

        private void FixedUpdate()
        {
            if (rider == null) return;
            if (session.IsSettled || session.IsMapOpen)
            {
                if (phase == Phase.Carrying) Finish();
                return;
            }
            if (session.IsPaused) return;
            float dt = Time.fixedDeltaTime;
            switch (phase)
            {
                case Phase.Sitting:
                    if ((slot == null || slot.StrikeAllowed) && !rider.IsRecovering && !rider.IsMonkeyCarried
                        && Flat(rider.transform.position - home).sqrMagnitude <= detectionRadius * detectionRadius
                        && Mathf.Abs(rider.transform.position.y - home.y) < 3f)
                    {
                        jumpStart = transform.position;
                        var lead = Vector3.ClampMagnitude(Flat(rider.Velocity) * (jumpSeconds * jumpPredictionShare), maximumPredictionDistance);
                        jumpTarget = ClipJumpTarget(Ground(rider.transform.position + lead));
                        heading = Flat(jumpTarget - jumpStart).normalized;
                        if (heading.sqrMagnitude > 0f) transform.rotation = Quaternion.LookRotation(heading);
                        elapsed = 0f; phase = Phase.Jumping; Pose(true);
                    }
                    break;
                case Phase.Jumping:
                    elapsed += dt;
                    float t = Mathf.Clamp01(elapsed / jumpSeconds);
                    var before = transform.position;
                    var next = Vector3.Lerp(jumpStart, jumpTarget, t) + Vector3.up * (4f * jumpHeight * t * (1f - t));
                    if (Blocked(before + Vector3.up * 0.65f, next - before))
                    { phase = Phase.Returning; transform.position = Ground(before); break; }
                    // Substeps compare against the actual player capsule, including its height when jumping.
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(before, next) / (contactRadius * 0.5f)));
                    for (int i = 1; i <= steps; i++)
                    {
                        var center = Vector3.Lerp(before, next, (float)i / steps) + Vector3.up * 0.65f;
                        if (riderShape != null && (riderShape.ClosestPoint(center) - center).sqrMagnitude <= contactRadius * contactRadius
                            && rider.TryBeginMonkeyCarry(this, heading, carrySeconds))
                        { phase = Phase.Carrying; elapsed = turnAt = 0f; Pose(true); return; }
                    }
                    transform.position = next;
                    if (t >= 1f) phase = Phase.Returning;
                    break;
                case Phase.Returning:
                    var delta = Flat(home - transform.position);
                    var step = delta.normalized * Mathf.Min(delta.magnitude, returnSpeed * dt);
                    if (!Blocked(transform.position + Vector3.up * 0.65f, step)) transform.position = Ground(transform.position + step);
                    if (delta.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(delta);
                    if (delta.sqrMagnitude <= 0.01f) ResetHome();
                    break;
                case Phase.Carrying:
                    elapsed += dt;
                    if (elapsed >= turnAt)
                    {
                        heading = Quaternion.Euler(0f, Random.Range(-120f, 120f), 0f) * heading;
                        if (heading.sqrMagnitude < 0.01f) heading = rider.transform.forward;
                        turnAt = elapsed + Random.Range(Mathf.Max(0.1f, turnInterval.x), Mathf.Max(turnInterval.x, turnInterval.y));
                    }
                    if (Blocked(rider.transform.position, heading * 1.5f)) heading = Quaternion.Euler(0f, 110f, 0f) * heading;
                    if (!rider.SteerMonkeyCarry(this, heading)) Finish();
                    break;
                case Phase.Spent:
                    if (Flat(rider.transform.position - home).sqrMagnitude > detectionRadius * detectionRadius) ResetHome();
                    break;
            }
        }
        private bool Blocked(Vector3 origin, Vector3 step)
        {
            if (step.sqrMagnitude < 0.000001f) return false;
            int count = Physics.SphereCastNonAlloc(origin, 0.25f, step.normalized, hits, step.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                // Dynamic actors must still be able to collide with the carried player.
                if (hit.collider.attachedRigidbody != null || hit.normal.y > 0.6f) continue;
                return true;
            }
            return count == hits.Length;
        }
        private Vector3 ClipJumpTarget(Vector3 target)
        {
            // Keep a ground-level route home, rather than jumping over a wall the monkey cannot walk back across.
            var delta = target - home;
            float distance = delta.magnitude;
            if (distance < 0.001f) return target;
            int count = Physics.SphereCastNonAlloc(home + Vector3.up * 0.65f, 0.25f, delta / distance,
                hits, distance, ~0, QueryTriggerInteraction.Ignore);
            float reach = distance;
            for (int i = 0; i < count; i++)
                if (hits[i].collider.attachedRigidbody == null && hits[i].normal.y <= 0.6f)
                    reach = Mathf.Min(reach, Mathf.Max(0f, hits[i].distance - 0.1f));
            if (count == hits.Length) reach = 0f;
            return Ground(home + delta / distance * reach);
        }
        private void LateUpdate()
        {
            if (phase != Phase.Carrying || rider == null) return;
            if (!rider.IsMonkeyCarried) { Finish(); return; }
            var feet = rider.transform.position;
            if (riderShape != null) feet.y = riderShape.bounds.min.y;
            transform.SetPositionAndRotation(feet - rider.transform.forward * 0.55f, rider.transform.rotation);
        }
        private void Pose(bool standing)
        {
            model.localPosition = standing ? Vector3.up * 0.25f : Vector3.zero;
            if (leftArm != null) leftArm.localRotation = Quaternion.Euler(standing ? -65f : 15f, 0f, -15f);
            if (rightArm != null) rightArm.localRotation = Quaternion.Euler(standing ? -65f : 15f, 0f, 15f);
        }
        private void Finish()
        {
            rider.EndMonkeyCarry(this);
            model.gameObject.SetActive(false);
            transform.SetPositionAndRotation(home, homeRotation);
            phase = Phase.Spent;
        }
        private void ResetHome()
        {
            transform.SetPositionAndRotation(home, homeRotation);
            model.gameObject.SetActive(true);
            Pose(false); phase = Phase.Sitting;
        }
    }
}
