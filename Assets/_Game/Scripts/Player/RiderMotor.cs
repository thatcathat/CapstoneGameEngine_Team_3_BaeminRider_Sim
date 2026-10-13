using UnityEngine;
using UnityEngine.InputSystem;

namespace DeliveryRider
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RiderMotor : MonoBehaviour
    {
        [SerializeField] private Transform view;
        [SerializeField] private DeliverySession session;
        [SerializeField] private float speed = 5f;
        [SerializeField] private float acceleration = 22f;
        [SerializeField] private float turnSpeed = 12f;
        [SerializeField] private float recoveryHeight = -8f;
        [SerializeField] private bool followSlopes;
        [SerializeField, Range(0, 60)] private float maximumSlope = 35;
        [SerializeField] private LayerMask groundMask = 1;
        [SerializeField, Min(0.05f)] private float groundReach = 0.3f;
        [SerializeField, Min(1)] private float safeFallDepth = 6;
        [Header("Punch and jump (test values)")]
        [SerializeField, Min(0.05f)] private float punchCooldown = 0.35f;
        [SerializeField, Min(0.1f)] private float punchReach = 1.2f;
        [SerializeField, Min(0.1f)] private float punchRadius = 0.9f;
        [SerializeField, Min(0f)] private float punchHeight = 0.9f;
        [SerializeField] private LayerMask punchMask = ~0;
        [SerializeField, Min(0.1f)] private float jumpSpeed = 5.5f;
        private readonly Collider[] punchHits = new Collider[16];
        private readonly System.Collections.Generic.List<IPunchable> punched = new System.Collections.Generic.List<IPunchable>(8);
        private float nextPunchAt;
        private bool jumpQueued;
        public event System.Action Punched;
        // Raised when an authored hazard actually hits (crash or slip); used for chain ignition.
        public event System.Action<UnityEngine.Object> HazardHit;
        private readonly RaycastHit[] groundHits = new RaycastHit[16];
        private CapsuleCollider capsule;
        private float trafficRecoveryUntil;
        private float nextTrafficHitAt;
        private bool trafficRecoveryActive;
        private UnityEngine.Object lastHazard;
        private float lastHazardHitAt = float.NegativeInfinity;
        private int hazardChainCount;
        [SerializeField, Min(0)] private float slipSteering = 4;
        [SerializeField, Min(0.1f)] private float slipMaximumSpeed = 8;
        private UnityEngine.Object slipOwner;
        private float slipUntil;
        private float contactFailsafeUntil;
        private bool groundedForHazards;
        public bool IsGroundedForHazards => groundedForHazards;
        public bool IsSlipping => slipOwner != null && Time.time < slipUntil;
        public int HazardChainCount => Time.time - lastHazardHitAt <= 8 ? hazardChainCount : 0;
        private Rigidbody body;
        private InputActionMap gameplay;
        private InputAction move;
        private InputAction look;
        private InputAction pause;
        private InputAction map;
        private InputAction callFirst;
        private InputAction callSecond;
        private InputAction punch;
        private InputAction jump;
        private Vector2 movement;
        private Vector3 spawn;
        public Vector2 Look => session.CanMove ? look.ReadValue<Vector2>() : Vector2.zero;
        public float Speed => speed;
        public bool HasInline { get; private set; }

        // Inline skates only raise speed (with a little inertia); time limits are unchanged.
        public void EquipInline(float speedMultiplier, float accelerationMultiplier)
        {
            if (HasInline) return;
            HasInline = true;
            speed *= Mathf.Max(1f, speedMultiplier);
            acceleration *= Mathf.Clamp(accelerationMultiplier, 0.1f, 1f);
        }
        public bool IsRecovering => trafficRecoveryActive;

        // Traffic never changes food ownership.
        public bool TryTrafficCrash(Vector3 impulse, float recoverySeconds)
            => TryContactCrash(impulse, recoverySeconds);

        // Contact hazards share a short recovery.
        public bool TryContactCrash(Vector3 impulse, float recoverySeconds)
        {
            if (session == null || !session.CanMove || IsRecovering || Time.time < nextTrafficHitAt) return false;
            lastHazard = null;
            hazardChainCount = 0;
            ApplyContactCrash(impulse, recoverySeconds);
            return true;
        }

        // Distinct authored hazards may replace a contact impulse, never an owned incident.
        public bool TryHazardCrash(UnityEngine.Object source, Vector3 impulse, float recoverySeconds)
        {
            if (!AcceptHazard(source)) return false;
            ApplyContactCrash(impulse, recoverySeconds);
            HazardHit?.Invoke(source);
            return true;
        }

        public bool TryHazardSlip(UnityEngine.Object source, Vector3 direction, float duration)
        {
            if (!groundedForHazards || !AcceptHazard(source)) return false;
            trafficRecoveryActive = false;
            slipOwner = source;
            slipUntil = Time.time + Mathf.Max(0.1f, duration);
            var horizontal = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            if (horizontal.magnitude < speed) horizontal = Vector3.ProjectOnPlane(direction, Vector3.up).normalized * speed;
            horizontal = Vector3.ClampMagnitude(horizontal, slipMaximumSpeed);
            body.linearVelocity = new Vector3(horizontal.x, body.linearVelocity.y, horizontal.z);
            HazardHit?.Invoke(source);
            return true;
        }

        public void CancelSlip(UnityEngine.Object source)
        {
            if (slipOwner == source) slipOwner = null;
        }

        private bool AcceptHazard(UnityEngine.Object source)
        {
            if (source == null || session == null || session.IsPaused || session.IsMapOpen || session.IsSettled) return false;
            bool chain = lastHazard != null && lastHazard != source && Time.time - lastHazardHitAt <= 8;
            if (Time.time - lastHazardHitAt < 0.35f) return false;
            if (!chain && (IsRecovering || Time.time < nextTrafficHitAt)) return false;
            hazardChainCount = chain ? hazardChainCount + 1 : 1;
            lastHazard = source;
            lastHazardHitAt = Time.time;
            return true;
        }

        private void ApplyContactCrash(Vector3 impulse, float recoverySeconds)
        {
            slipOwner = null;
            groundedForHazards = false;
            trafficRecoveryActive = true;
            trafficRecoveryUntil = Time.time + Mathf.Max(0.1f, recoverySeconds);
            contactFailsafeUntil = trafficRecoveryUntil + 6;
            nextTrafficHitAt = trafficRecoveryUntil + 1;
            movement = Vector2.zero;
            body.linearVelocity = Vector3.zero;
            body.AddForce(impulse, ForceMode.VelocityChange);
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            spawn = transform.position;
            gameplay = new InputActionMap("Gameplay");
            move = gameplay.AddAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            look = gameplay.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            pause = gameplay.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            map = gameplay.AddAction("Map", InputActionType.Button, "<Keyboard>/m");
            callFirst = gameplay.AddAction("CallFirst", InputActionType.Button, "<Keyboard>/1");
            callSecond = gameplay.AddAction("CallSecond", InputActionType.Button, "<Keyboard>/2");
            punch = gameplay.AddAction("Punch", InputActionType.Button, "<Keyboard>/f");
            punch.AddBinding("<Mouse>/leftButton");
            jump = gameplay.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
        }

        private void OnEnable() => gameplay.Enable();
        private void OnDisable() { gameplay.Disable(); movement = Vector2.zero; slipOwner = null; trafficRecoveryActive = false; }
        private void OnDestroy() => gameplay.Dispose();

        private void Update()
        {
            if (session.IsSettled)
            {
                slipOwner = null;
                trafficRecoveryActive = false;
                groundedForHazards = false;
                body.linearVelocity = body.angularVelocity = Vector3.zero;
            }
            else if (session.IsMapOpen) slipOwner = null;
            if (pause.WasPressedThisFrame()) session.HandleEscape();
            else if (callFirst.WasPressedThisFrame()) session.HandleCallKey(0);
            else if (callSecond.WasPressedThisFrame()) session.HandleCallKey(1);
            else if (map.WasPressedThisFrame()) session.ToggleMap();
            movement = session.CanMove ? Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f) : Vector2.zero;
            if (session.CanPickup) session.TryPickup();
            if (session.CanMove && punch.WasPressedThisFrame()) TryPunch();
            if (session.CanMove && jump.WasPressedThisFrame()) jumpQueued = true;
        }

        // Punches go where the camera looks, so a standing rider can still aim at the recipient.
        private void TryPunch()
        {
            if (Time.time < nextPunchAt || IsSlipping) return;
            nextPunchAt = Time.time + punchCooldown;
            var forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : transform.forward;
            body.MoveRotation(Quaternion.LookRotation(forward));
            var center = body.position + forward * punchReach + Vector3.up * punchHeight;
            int count = Physics.OverlapSphereNonAlloc(center, punchRadius, punchHits, punchMask, QueryTriggerInteraction.Ignore);
            punched.Clear();
            for (int i = 0; i < count; i++)
            {
                var hit = punchHits[i];
                if (hit.attachedRigidbody == body) continue;
                var target = hit.GetComponentInParent<IPunchable>();
                if (target == null || punched.Contains(target)) continue;
                punched.Add(target);
                target.ReceivePunch(forward);
            }
            Punched?.Invoke();
        }

        private void FixedUpdate()
        {
            groundedForHazards = body.linearVelocity.y <= 0.5f && TryGround(out _, false);
            if (trafficRecoveryActive && Time.time >= trafficRecoveryUntil && (groundedForHazards || Time.time >= contactFailsafeUntil))
            {
                trafficRecoveryActive = false;
                nextTrafficHitAt = Mathf.Max(nextTrafficHitAt, Time.time + 1);
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            bool belowSafeGround = followSlopes && body.position.y < spawn.y - safeFallDepth;
            if (body.position.y < recoveryHeight || belowSafeGround)
            {
                body.position = spawn;
                body.linearVelocity = body.angularVelocity = Vector3.zero;
                trafficRecoveryActive = false;
                slipOwner = null;
                return;
            }
            // Map input restrictions must not cancel the physical accident impulse.
            if (IsRecovering && !session.IsSettled)
            {
                return;
            }
            var forward = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(view.right, Vector3.up).normalized;
            var direction = session.CanMove ? forward * movement.y + right * movement.x : Vector3.zero;
            var current = body.linearVelocity;
            bool jumping = jumpQueued && groundedForHazards && session.CanMove && !IsSlipping;
            jumpQueued = false;
            if (IsSlipping && session.CanMove)
            {
                var drift = Vector3.ProjectOnPlane(current, Vector3.up);
                drift += direction * (slipSteering * Time.fixedDeltaTime);
                if (groundedForHazards && TryGround(out var slipNormal))
                {
                    drift += Vector3.ProjectOnPlane(Physics.gravity, slipNormal) * Time.fixedDeltaTime;
                    drift = Vector3.ClampMagnitude(drift, slipMaximumSpeed);
                    body.linearVelocity = Vector3.ProjectOnPlane(drift, slipNormal).normalized * drift.magnitude;
                    body.AddForce(-Physics.gravity, ForceMode.Acceleration);
                    spawn = body.position;
                }
                else
                {
                    drift = Vector3.ClampMagnitude(drift, slipMaximumSpeed);
                    body.linearVelocity = new Vector3(drift.x, current.y, drift.z);
                }
                return;
            }
            var horizontal = session.CanMove ? Vector3.MoveTowards(new Vector3(current.x, 0f, current.z), direction * speed, acceleration * Time.fixedDeltaTime) : Vector3.zero;
            if (jumping) body.linearVelocity = new Vector3(horizontal.x, jumpSpeed, horizontal.z);
            else if (followSlopes && current.y <= 1f && TryGround(out var normal))
            {
                var tangent = Vector3.ProjectOnPlane(horizontal, normal).normalized * horizontal.magnitude;
                body.linearVelocity = tangent;
                // Cancel gravity only while grounded; airborne accident impulses remain untouched.
                body.AddForce(-Physics.gravity, ForceMode.Acceleration);
                if (session.CanMove) spawn = body.position;
            }
            else body.linearVelocity = new Vector3(horizontal.x, current.y, horizontal.z);
            if (direction.sqrMagnitude > 0.01f)
                body.MoveRotation(Quaternion.Slerp(body.rotation, Quaternion.LookRotation(direction), turnSpeed * Time.fixedDeltaTime));
        }

        private bool TryGround(out Vector3 normal, bool snap = true)
        {
            normal = Vector3.up;
            if (capsule == null) return false;
            var bounds = capsule.bounds;
            float radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.85f;
            var origin = new Vector3(bounds.center.x, bounds.min.y + radius + 0.1f, bounds.center.z);
            int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, groundHits,
                groundReach + 0.1f, groundMask, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (hit.rigidbody != null || hit.normal.y < Mathf.Cos(maximumSlope * Mathf.Deg2Rad) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                normal = hit.normal;
                found = true;
            }
            // Closing a small ground gap follows a descending road without cancelling a launch.
            if (snap && found && nearest > 0.1f) body.position += Vector3.down * Mathf.Min(nearest - 0.1f, groundReach);
            return found;
        }
    }
}
