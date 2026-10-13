using UnityEngine;

namespace DeliveryRider
{
    // Duck flock: when the rider comes near, a line of birds waddles across the road; touching one bounces the rider off.
    public sealed class CrossingFlockHazard : MonoBehaviour
    {
        private enum Phase { Ready, Crossing, Spent }
        [SerializeField] private DeliverySession session;
        [SerializeField] private Transform[] birds = System.Array.Empty<Transform>();
        [Tooltip("Crossing line in local space; birds walk from start to end.")]
        [SerializeField] private Vector3 localStart;
        [SerializeField] private Vector3 localEnd;
        [SerializeField] private Vector3 approachCenter = new Vector3(0f, 1f, 0f);
        [SerializeField] private Vector3 approachSize = new Vector3(12f, 3f, 16f);
        [Header("Test values")]
        [SerializeField, Min(0.1f)] private float walkSpeed = 2.6f;
        [SerializeField, Min(0.1f)] private float spacing = 1.1f;
        [SerializeField, Min(0.1f)] private float hitRadius = 0.7f;
        [SerializeField, Min(0f)] private float bounceImpulse = 6.5f;
        [SerializeField, Min(0f)] private float bounceLift = 3f;
        [SerializeField, Min(0.1f)] private float recoverySeconds = 0.8f;
        private RiderMotor rider;
        private HazardSlot slot;
        private Phase phase;
        private float head;
        private bool hitThisCrossing;
        private int activatedOrder = -1;

        private void Awake()
        {
            rider = session != null && session.Player != null ? session.Player.GetComponent<RiderMotor>() : null;
            if (rider == null || birds.Length == 0) { enabled = false; return; }
            slot = GetComponent<HazardSlot>();
            HideBirds();
        }

        private void OnDisable() { HideBirds(); phase = Phase.Spent; }
        private void OnEnable() { if (rider != null) phase = Phase.Spent; }

        private void Update() { if (session.IsSettled && phase == Phase.Crossing) Finish(); }

        private void FixedUpdate()
        {
            if (session.IsPaused || session.IsSettled) return;
            if (session.IsMapOpen && phase == Phase.Crossing) { Finish(); return; }
            switch (phase)
            {
                case Phase.Ready:
                    if ((slot == null || slot.StrikeAllowed) && !session.IsMapOpen && !rider.IsRecovering && InApproach()) Begin();
                    break;
                case Phase.Crossing:
                    Advance();
                    break;
                case Phase.Spent:
                    // One crossing per call; the next call re-arms it.
                    var order = session.ActiveOrder;
                    if (order != null && order.Number != activatedOrder && !InApproach()) phase = Phase.Ready;
                    break;
            }
        }

        private void Begin()
        {
            activatedOrder = session.ActiveOrder != null ? session.ActiveOrder.Number : -1;
            head = 0f;
            hitThisCrossing = false;
            phase = Phase.Crossing;
        }

        private void Advance()
        {
            head += walkSpeed * Time.fixedDeltaTime;
            Vector3 start = transform.TransformPoint(localStart), end = transform.TransformPoint(localEnd);
            float length = Vector3.Distance(start, end);
            Vector3 player = rider.transform.position;
            for (int i = 0; i < birds.Length; i++)
            {
                float along = head - i * spacing;
                bool visible = along >= 0f && along <= length;
                if (birds[i].gameObject.activeSelf != visible) birds[i].gameObject.SetActive(visible);
                if (!visible) continue;
                birds[i].position = Vector3.Lerp(start, end, along / length);
                birds[i].rotation = Quaternion.LookRotation(end - start);
                Vector3 offset = player - birds[i].position;
                if (hitThisCrossing || Mathf.Abs(offset.y) > 1.8f || new Vector2(offset.x, offset.z).sqrMagnitude > hitRadius * hitRadius) continue;
                hitThisCrossing = true;
                var push = Vector3.ProjectOnPlane(offset, Vector3.up).normalized;
                if (push.sqrMagnitude < 0.01f) push = -transform.forward;
                rider.TryHazardCrash(this, push * bounceImpulse + Vector3.up * bounceLift, recoverySeconds);
            }
            if (head - (birds.Length - 1) * spacing > length) Finish();
        }

        private bool InApproach()
        {
            Vector3 p = transform.InverseTransformPoint(rider.transform.position) - approachCenter;
            Vector3 half = approachSize * 0.5f;
            return Mathf.Abs(p.x) <= half.x && Mathf.Abs(p.y) <= half.y && Mathf.Abs(p.z) <= half.z;
        }

        private void Finish() { HideBirds(); phase = Phase.Spent; }
        private void HideBirds() { foreach (var bird in birds) if (bird != null) bird.gameObject.SetActive(false); }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(approachCenter, approachSize);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(localStart, localEnd);
        }
    }
}
