using UnityEngine;

namespace DeliveryRider
{
    // Chicken: passing close to the roadside chickens sets the whole flock on the rider for a while; each peck knocks
    // a little. The flock keeps up with the rider's speed, so it ends after a few pecks or when the chase runs out.
    public sealed class ChaseFlockHazard : MonoBehaviour, IPlacedHazard
    {
        private enum Phase { Idle, Chasing, Spent }
        [SerializeField] private DeliverySession session;
        [SerializeField] private Transform[] birds = System.Array.Empty<Transform>();
        [Header("Test values")]
        [SerializeField, Min(0.1f)] private float triggerRadius = 3f;
        [SerializeField, Min(0.1f)] private float chaseSeconds = 6f;
        [Tooltip("Slowest chase speed; the flock runs at the rider's speed times riderSpeedShare when that is faster.")]
        [SerializeField, Min(0.1f)] private float chaseSpeed = 5.2f;
        [SerializeField, Min(0f)] private float riderSpeedShare = 1.15f;
        [SerializeField, Min(0.1f)] private float maximumChaseSpeed = 20f;
        [Tooltip("The flock gives up after this many pecks that knocked the rider.")]
        [SerializeField, Min(1)] private int maximumPecks = 3;
        [SerializeField, Min(1f)] private float giveUpDistance = 40f;
        [SerializeField, Min(0.1f)] private float peckRadius = 0.9f;
        [SerializeField, Min(0.1f)] private float peckCooldown = 1.5f;
        [SerializeField, Min(0f)] private float peckImpulse = 3f;
        [SerializeField, Min(0f)] private float peckLift = 2.5f;
        [SerializeField, Min(0.1f)] private float peckRecovery = 0.45f;
        [SerializeField, Min(0f)] private float riderCenterHeight = 1.08f;
        private RiderMotor rider;
        private HazardSlot slot;
        private Vector3[] homes;
        private Quaternion[] facings;
        private Phase phase;
        private float elapsed;
        private float nextPeckAt;
        private int pecks;
        private int activatedOrder = -1;

        public void Place(DeliverySession owner, RoadNetwork ground, HazardCandidate spot) => session = owner;

        private void Awake()
        {
            rider = session != null && session.Player != null ? session.Player.GetComponent<RiderMotor>() : null;
            if (rider == null || birds.Length == 0) { enabled = false; return; }
            slot = GetComponent<HazardSlot>();
            homes = new Vector3[birds.Length];
            facings = new Quaternion[birds.Length];
            for (int i = 0; i < birds.Length; i++) { homes[i] = birds[i].localPosition; facings[i] = birds[i].localRotation; }
        }

        private void OnDisable() { if (homes != null) GoHome(); phase = Phase.Spent; }
        private void OnEnable() { if (homes != null) phase = Phase.Spent; }
        private void Update() { if (session.IsSettled && phase == Phase.Chasing) { GoHome(); phase = Phase.Spent; } }

        private void FixedUpdate()
        {
            if (session.IsPaused || session.IsSettled) return;
            switch (phase)
            {
                case Phase.Idle:
                    if ((slot == null || slot.StrikeAllowed) && !session.IsMapOpen && !rider.IsRecovering && Near(transform.position, triggerRadius))
                    {
                        activatedOrder = session.ActiveOrder != null ? session.ActiveOrder.Number : -1;
                        elapsed = 0f;
                        pecks = 0;
                        nextPeckAt = Time.time + 0.6f;
                        phase = Phase.Chasing;
                    }
                    break;
                case Phase.Chasing:
                    Chase();
                    break;
                case Phase.Spent:
                    var order = session.ActiveOrder;
                    if (order != null && order.Number != activatedOrder && !Near(transform.position, triggerRadius * 3f)) phase = Phase.Idle;
                    break;
            }
        }

        private void Chase()
        {
            elapsed += Time.fixedDeltaTime;
            Vector3 player = rider.transform.position;
            if (elapsed >= chaseSeconds || pecks >= maximumPecks || session.IsMapOpen || !Near(transform.position, giveUpDistance)) { GoHome(); phase = Phase.Spent; return; }
            Vector3 feet = player - Vector3.up * riderCenterHeight;
            float speed = Mathf.Clamp(rider.HorizontalSpeed * riderSpeedShare, chaseSpeed, Mathf.Max(chaseSpeed, maximumChaseSpeed));
            bool pecked = false;
            for (int i = 0; i < birds.Length; i++)
            {
                // Each chicken aims at its own spot around the rider so the flock surrounds rather than stacks.
                float angle = i * Mathf.PI * 2f / birds.Length;
                Vector3 target = feet + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.6f;
                Vector3 position = Vector3.MoveTowards(birds[i].position, target, speed * Time.fixedDeltaTime);
                position.y = Mathf.MoveTowards(birds[i].position.y, feet.y, 4f * Time.fixedDeltaTime);
                Vector3 step = position - birds[i].position;
                birds[i].position = position;
                if (new Vector2(step.x, step.z).sqrMagnitude > 0.0001f) birds[i].rotation = Quaternion.LookRotation(new Vector3(step.x, 0f, step.z));
                if (!pecked && Time.time >= nextPeckAt && Near(birds[i].position + Vector3.up * riderCenterHeight, peckRadius))
                {
                    pecked = true;
                    nextPeckAt = Time.time + peckCooldown;
                    var push = Vector3.ProjectOnPlane(player - birds[i].position, Vector3.up).normalized;
                    if (rider.TryHazardCrash(this, push * peckImpulse + Vector3.up * peckLift, peckRecovery)) pecks++;
                }
            }
        }

        private bool Near(Vector3 point, float radius)
        {
            Vector3 offset = rider.transform.position - point;
            return Mathf.Abs(offset.y) < 2.5f && new Vector2(offset.x, offset.z).sqrMagnitude <= radius * radius;
        }

        private void GoHome()
        {
            for (int i = 0; i < birds.Length; i++)
                if (birds[i] != null) { birds[i].localPosition = homes[i]; birds[i].localRotation = facings[i]; }
        }
    }
}
