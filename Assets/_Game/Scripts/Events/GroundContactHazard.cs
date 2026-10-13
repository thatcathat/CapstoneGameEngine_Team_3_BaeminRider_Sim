using UnityEngine;

namespace DeliveryRider
{
    // Hydrant behaves like a manhole (approach, warning, burst) with its own launch numbers set per instance.
    public enum GroundHazardKind { Manhole, Melon, Hydrant }

    [RequireComponent(typeof(BoxCollider))]
    public sealed class GroundContactHazard : MonoBehaviour
    {
        private enum Phase { Ready, Warning, Active, Spent }
        [SerializeField] private DeliverySession session;
        [SerializeField] private GroundHazardKind kind;
        [SerializeField] private Renderer warningLamp;
        [SerializeField] private GameObject effect;
        [SerializeField, Min(0.1f)] private float approachRadius = 2.8f;
        [SerializeField, Min(0.1f)] private float warningSeconds = 0.7f;
        [SerializeField, Min(0.1f)] private float activeSeconds = 0.6f;
        [SerializeField, Min(0)] private float forwardImpulse = 6;
        [SerializeField, Min(0)] private float upwardImpulse = 4.5f;
        [SerializeField, Min(0.1f)] private float slipSeconds = 2.2f;
        [SerializeField, Min(0)] private float rearmSeconds = 10;
        [SerializeField, Min(1)] private float rearmDistance = 4;
        private BoxCollider volume;
        private CapsuleCollider playerShape;
        private RiderMotor rider;
        private Phase phase;
        private float elapsed;
        private bool contactConsumed;
        private HazardSlot slot;
        private bool CanStrike => slot == null || slot.StrikeAllowed;

        private void Awake()
        {
            volume = GetComponent<BoxCollider>();
            slot = GetComponent<HazardSlot>();
            if (session == null || session.Player == null)
            { Debug.LogError("Ground hazard requires a delivery session.", this); enabled = false; return; }
            rider = session.Player.GetComponent<RiderMotor>();
            playerShape = session.Player.GetComponent<CapsuleCollider>();
            if (rider == null || playerShape == null) { enabled = false; return; }
            volume.isTrigger = true;
            ClearEffects();
        }

        // Settlement stops FixedUpdate via timeScale; teardown must still run.
        private void Update()
        {
            if (session != null && (session.IsSettled || session.IsMapOpen && phase != Phase.Ready && phase != Phase.Spent)) Finish();
        }
        private void OnDisable() { ClearEffects(); if (rider != null) rider.CancelSlip(this); phase = Phase.Spent; elapsed = 0; }
        private void FixedUpdate()
        {
            if (session.IsPaused || session.IsSettled) return;
            elapsed += Time.fixedDeltaTime;
            switch (phase)
            {
                case Phase.Ready:
                    if (session.IsMapOpen || !CanStrike) break;
                    if (kind == GroundHazardKind.Melon) { TryContact(); break; }
                    Vector3 local = transform.InverseTransformPoint(rider.transform.position);
                    if (Mathf.Abs(local.y) < 3 && new Vector2(local.x, local.z).sqrMagnitude <= approachRadius * approachRadius)
                        Enter(Phase.Warning);
                    break;
                case Phase.Warning:
                    if (warningLamp != null) warningLamp.enabled = Mathf.Repeat(elapsed, 0.2f) < 0.12f;
                    if (elapsed >= warningSeconds)
                    {
                        Enter(Phase.Active);
                        if (warningLamp != null) warningLamp.enabled = true;
                        if (effect != null) effect.SetActive(true);
                    }
                    break;
                case Phase.Active:
                    TryContact();
                    if (phase == Phase.Active && elapsed >= activeSeconds) Finish();
                    break;
                case Phase.Spent:
                    if (elapsed >= rearmSeconds && Vector3.Distance(rider.transform.position, transform.position) > rearmDistance)
                        Enter(Phase.Ready);
                    break;
            }
        }
        private void TryContact()
        {
            if (contactConsumed || session.IsMapOpen || kind == GroundHazardKind.Melon && !rider.IsGroundedForHazards) return;
            if (!Physics.ComputePenetration(volume, transform.position, transform.rotation, playerShape,
                playerShape.transform.position, playerShape.transform.rotation, out _, out _)) return;
            contactConsumed = true;
            if (kind == GroundHazardKind.Melon) rider.TryHazardSlip(this, transform.forward, slipSeconds);
            else rider.TryHazardCrash(this, transform.forward * forwardImpulse + transform.up * upwardImpulse, 0.6f);
            // Consume even rejected contact: this activation cannot hit repeatedly later.
            if (kind == GroundHazardKind.Melon) Finish();
        }
        private void Enter(Phase value) { phase = value; elapsed = 0; if (value == Phase.Ready || value == Phase.Warning) contactConsumed = false; }
        private void Finish() { if (phase != Phase.Spent) Enter(Phase.Spent); ClearEffects(); }
        private void ClearEffects() { if (warningLamp != null) warningLamp.enabled = false; if (effect != null) effect.SetActive(false); }
    }
}
