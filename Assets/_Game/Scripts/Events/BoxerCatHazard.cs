using UnityEngine;
namespace DeliveryRider
{
    public sealed class BoxerCatHazard : MonoBehaviour, IPlacedHazard, IPunchable
    {
        private enum Phase { Wander, Rise, Approach, Circle, KeepDistance, AttackApproach, Windup, Strike, Recover, Rest }
        [SerializeField] private Transform torso, head, leftHand, rightHand;
        [SerializeField] private GameObject[] gloves;
        [SerializeField] private float detectionRadius = 5f, speedMultiplier = 1.3f, leash = 20f, attackRange = 1.3f;
        [SerializeField] private Vector2 attackDelay = new Vector2(0.6f, 1.8f);
        [SerializeField] private float windup = 0.45f, strikeDuration = 0.15f, recovery = 0.8f;
        [SerializeField] private float circleSpeedMultiplier = 0.8f, retreatSpeedMultiplier = 0.4f;
        [SerializeField] private float spacingDistance = 1.7f, spacingTolerance = 0.35f;
        [SerializeField, Min(0.1f)] private float acceleration = 18f, deceleration = 24f;
        [SerializeField] private Vector2 directionChangeSeconds = new Vector2(2f, 4f);
        [SerializeField, Min(0.1f)] private float obstacleTurnCooldown = 0.8f;
        private Vector3 moveVelocity;
        private float directionTimer, obstacleTurnTimer;
        [SerializeField] private float hopHeight = 0.12f, hopFrequency = 2.2f;
        private Transform visuals;
        private float hopTime;
        private DeliverySession session;
        private RoadNetwork ground;
        private RiderMotor rider;
        private Vector3 home, wanderTarget, strikeDirection;
        private Phase phase;
        private float timer, wait = -1f, wanderTime, orbitSign, recoveryGrace;
        private int hits, hand;
        private bool struck;
        private readonly RaycastHit[] obstacles = new RaycastHit[32];
        public void Place(DeliverySession owner, RoadNetwork roads, HazardCandidate spot) { session = owner; ground = roads; }
        private void Awake()
        {
            home = transform.position;
            rider = session != null && session.Player != null ? session.Player.GetComponent<RiderMotor>() : null;
            if (rider == null) { enabled = false; return; }
            orbitSign = Random.value < 0.5f ? -1f : 1f;
            ScheduleDirectionChange();
            // Lift the whole visible cat together without moving its ground navigation or hit collider.
            visuals = new GameObject("BoxerFootwork").transform;
            visuals.SetParent(transform, false);
            while (transform.childCount > 1)
            {
                var child = transform.GetChild(0);
                if (child == visuals) child = transform.GetChild(1);
                child.SetParent(visuals, false);
            }
            Pose(0f);
        }
        private void OnEnable() { if (rider != null) rider.SafePositionRestored += ResetCat; }
        private void OnDisable() { if (rider != null) rider.SafePositionRestored -= ResetCat; }
        private static Vector3 Flat(Vector3 value) => Vector3.ProjectOnPlane(value, Vector3.up);
        private void ResetCat()
        {
            phase = Phase.Rest; timer = 10f; wait = -1f; hits = 0;
            hopTime = 0f;
            moveVelocity = Vector3.zero;
            ScheduleDirectionChange();
            obstacleTurnTimer = 0f;
            if (visuals != null) visuals.localPosition = Vector3.zero;
            transform.position = home; Pose(0f);
        }
        public bool ReceivePunch(Vector3 direction)
        {
            if (session == null || session.IsPaused || session.IsSettled || phase == Phase.Wander || phase == Phase.Rest) return false;
            if (++hits >= 3) ResetCat();
            return true;
        }
        private bool Clear(Vector3 direction, float distance)
        {
            int count = Physics.SphereCastNonAlloc(transform.position + Vector3.up * 0.65f, 0.25f, direction, obstacles, distance, ~0, QueryTriggerInteraction.Ignore);
            if (count == obstacles.Length) return false;
            for (int i = 0; i < count; i++)
            {
                var t = obstacles[i].transform;
                if (t.IsChildOf(transform) || t.IsChildOf(rider.transform)) continue;
                return false;
            }
            return true;
        }
        private void Move(Vector3 target, float speed, bool orbit, bool curve = true)
        {
            var delta = Flat(target - transform.position);
            var direction = delta.normalized;
            if (orbit && curve)
            {
                var radial = Flat(transform.position - rider.transform.position).normalized;
                direction = (direction + Vector3.Cross(Vector3.up, radial) * orbitSign * Mathf.Clamp01(delta.magnitude) * 0.9f).normalized;
            }
            // Slow near the destination; velocity interpolation also brakes before reversing direction.
            float desiredSpeed = Mathf.Min(speed, Mathf.Sqrt(2f * deceleration * delta.magnitude));
            var desiredVelocity = direction * desiredSpeed;
            bool braking = desiredVelocity.sqrMagnitude < moveVelocity.sqrMagnitude || Vector3.Dot(desiredVelocity, moveVelocity) < 0f;
            moveVelocity = Vector3.MoveTowards(moveVelocity, desiredVelocity, (braking ? deceleration : acceleration) * Time.fixedDeltaTime);
            float step = moveVelocity.magnitude * Time.fixedDeltaTime;
            if (step < 0.00001f) return;
            var travelDirection = moveVelocity.normalized;
            if (!Clear(travelDirection, step + 0.05f))
            {
                // A collision stops travel immediately, but only one direction reversal is allowed per cooldown.
                moveVelocity = Vector3.zero;
                if (orbit && obstacleTurnTimer <= 0f)
                {
                    ReverseDirection();
                    obstacleTurnTimer = obstacleTurnCooldown;
                }
                return;
            }
            var next = transform.position + travelDirection * step;
            transform.position = ground != null ? ground.OnGround(next, 0f) : next;
            if (!orbit) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(travelDirection), 1f - Mathf.Exp(-10f * Time.fixedDeltaTime));
        }
        private void ScheduleDirectionChange() => directionTimer = Random.Range(Mathf.Max(0.1f, directionChangeSeconds.x), Mathf.Max(directionChangeSeconds.x, directionChangeSeconds.y));
        private void ReverseDirection()
        {
            orbitSign = -orbitSign;
            ScheduleDirectionChange();
        }
        private void FixedUpdate()
        {
            if (rider == null || session.IsPaused) return;
            if (session.IsSettled) { if (phase != Phase.Rest) ResetCat(); return; }
            float dt = Time.fixedDeltaTime;
            var toward = Flat(rider.transform.position - transform.position);
            float distance = toward.magnitude;
            timer -= dt;
            obstacleTurnTimer = Mathf.Max(0f, obstacleTurnTimer - dt);
            if (phase == Phase.Rest)
            {
                if (timer <= 0f && Flat(rider.transform.position - home).magnitude > 8f) phase = Phase.Wander;
                return;
            }
            if (phase == Phase.Wander)
            {
                wanderTime -= dt;
                if (wanderTime <= 0f) { var p = Random.insideUnitCircle * 3f; wanderTarget = home + new Vector3(p.x, 0f, p.y); wanderTime = Random.Range(2f, 4f); }
                Move(wanderTarget, 1f, false);
                if (distance <= detectionRadius && Mathf.Abs(rider.transform.position.y - transform.position.y) < 3f && Clear(toward.normalized, distance)) { phase = Phase.Rise; timer = 0.8f; }
                return;
            }
            if (Flat(transform.position - home).magnitude >= leash) { ResetCat(); return; }
            if (toward.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(toward);
            if (phase == Phase.Rise) { Move(transform.position, 0f, true, false); Pose(1f - Mathf.Clamp01(timer / 0.8f)); if (timer <= 0f) phase = SelectFootwork(distance); return; }
            Pose(1f);
            hopTime += dt;
            bool footwork = phase == Phase.Circle || phase == Phase.KeepDistance || phase == Phase.Approach || phase == Phase.AttackApproach || phase == Phase.Recover;
            directionTimer -= dt;
            if (directionTimer <= 0f && obstacleTurnTimer <= 0f) ReverseDirection();
            if (!footwork) Move(transform.position, 0f, true, false);
            visuals.localPosition = footwork ? Vector3.up * (Mathf.Abs(Mathf.Sin(hopTime * Mathf.PI * hopFrequency)) * hopHeight) : Vector3.zero;
            if (rider.IsRecovering) recoveryGrace = 1f; else recoveryGrace = Mathf.Max(0f, recoveryGrace - dt);
            if (phase == Phase.Approach || phase == Phase.Circle || phase == Phase.KeepDistance)
            {
                phase = SelectFootwork(distance);
                TickFootwork(distance, toward);
                // Keep one random deadline across spacing corrections; draw again after each attack.
                if (recoveryGrace > 0f) { wait = -1f; return; }
                if (wait < 0f) wait = Random.Range(attackDelay.x, attackDelay.y);
                if (phase != Phase.Approach) wait -= dt;
                if (wait <= 0f) { phase = Phase.AttackApproach; wait = -1f; }
            }
            else if (phase == Phase.AttackApproach)
            {
                if (recoveryGrace > 0f) { phase = SelectFootwork(distance); return; }
                Move(rider.transform.position + Flat(rider.transform.forward).normalized, rider.Speed * speedMultiplier, true);
                if (Flat(rider.transform.position - transform.position).magnitude <= attackRange)
                { phase = Phase.Windup; timer = windup; }
            }
            else if (phase == Phase.Windup && timer <= 0f)
            { phase = Phase.Strike; timer = strikeDuration; struck = false; strikeDirection = toward.normalized; }
            else if (phase == Phase.Strike)
            {
                var fist = hand == 0 ? leftHand : rightHand;
                fist.localPosition += Vector3.forward * 0.6f;
                if (!struck && distance <= attackRange && Mathf.Abs(rider.transform.position.y - transform.position.y) < 2f && recoveryGrace <= 0f && Vector3.Dot(strikeDirection, toward.normalized) > 0.6f && Clear(toward.normalized, distance))
                { struck = true; rider.TryHazardCrash(this, strikeDirection * 6f + Vector3.up * 2f, 0.6f); }
                if (timer <= 0f) { phase = Phase.Recover; timer = recovery; hand = 1 - hand; }
            }
            else if (phase == Phase.Recover)
            {
                TickFootwork(distance, toward);
                if (timer <= 0f) phase = SelectFootwork(distance);
            }
        }
        private Phase SelectFootwork(float distance)
        {
            // Hysteresis lets a correction finish at the desired radius before orbiting again.
            // Movement stops within 1 mm; use a wider arrival band so float rounding cannot
            // strand Approach at e.g. 1.700001 m and freeze the attack countdown forever.
            float arrivalTolerance = Mathf.Max(0.01f, spacingTolerance * 0.25f);
            if (phase == Phase.KeepDistance && distance < spacingDistance - arrivalTolerance) return Phase.KeepDistance;
            if (phase == Phase.Approach && distance > spacingDistance + arrivalTolerance) return Phase.Approach;
            if (distance < spacingDistance - spacingTolerance) return Phase.KeepDistance;
            if (distance > spacingDistance + spacingTolerance) return Phase.Approach;
            return Phase.Circle;
        }
        private void TickFootwork(float distance, Vector3 toward)
        {
            var radial = distance > 0.001f ? -toward.normalized : -transform.forward;
            var movement = phase == Phase.Recover ? SelectFootwork(distance) : phase;
            switch (movement)
            {
                case Phase.Approach:
                    Move(rider.transform.position + radial * spacingDistance, rider.Speed * circleSpeedMultiplier, true, false);
                    break;
                case Phase.KeepDistance:
                    Move(rider.transform.position + radial * spacingDistance, rider.Speed * retreatSpeedMultiplier, true, false);
                    break;
                case Phase.Circle:
                    // Preserve the current radius inside the band rather than pulling toward an exact orbit.
                    float radius = Mathf.Clamp(distance, spacingDistance - spacingTolerance, spacingDistance + spacingTolerance);
                    float angle = 25f * orbitSign;
                    var target = rider.transform.position + Quaternion.AngleAxis(angle, Vector3.up) * radial * radius;
                    Move(target, rider.Speed * circleSpeedMultiplier, true, false);
                    break;
            }
        }
        private void Pose(float upright)
        {
            torso.localPosition = Vector3.Lerp(new Vector3(0, 0.45f, 0), new Vector3(0, 0.75f, 0), upright);
            torso.localRotation = Quaternion.Euler(Mathf.Lerp(90f, 0f, upright), 0, 0);
            head.localPosition = Vector3.Lerp(new Vector3(0, 0.6f, 0.45f), new Vector3(0, 1.3f, 0.08f), upright);
            leftHand.localPosition = Vector3.Lerp(new Vector3(-0.25f, 0.2f, 0.35f), new Vector3(-0.36f, 0.95f, 0.3f), upright);
            rightHand.localPosition = Vector3.Lerp(new Vector3(0.25f, 0.2f, 0.35f), new Vector3(0.36f, 0.95f, 0.3f), upright);
            foreach (var glove in gloves) glove.SetActive(upright > 0.5f);
        }
    }
}
