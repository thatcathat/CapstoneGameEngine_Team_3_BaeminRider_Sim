using UnityEngine;

namespace DeliveryRider
{
    // Pigeon snatch: a perched pigeon swoops on a rider carrying food, grabs it and flies off along the road.
    // Recovery reuses FoodThief: punch the pigeon to get the food back, or re-collect at the shop if it escapes.
    public sealed class PigeonSnatchHazard : MonoBehaviour
    {
        private enum Phase { Ready, Warning, Swoop, Flying, Spent }
        [SerializeField] private DeliverySession session;
        [SerializeField] private FoodThief pigeon;
        [SerializeField] private Transform perch;
        [SerializeField] private Transform[] escapeRoute = System.Array.Empty<Transform>();
        [SerializeField] private Renderer warningLamp;
        [Header("Test values")]
        [SerializeField, Min(0.1f)] private float triggerRadius = 4f;
        [SerializeField, Min(0.05f)] private float warningSeconds = 0.5f;
        [SerializeField, Min(0.05f)] private float swoopSeconds = 0.35f;
        [SerializeField, Min(0.1f)] private float flySpeed = 5.4f;
        [SerializeField, Min(1f)] private float escapeSeconds = 35f;
        private RiderMotor rider;
        private HazardSlot slot;
        private Phase phase;
        private float elapsed;
        private Vector3 swoopFrom;
        private int activatedOrder = -1;

        private void Awake()
        {
            rider = session != null && session.Player != null ? session.Player.GetComponent<RiderMotor>() : null;
            if (rider == null || pigeon == null || perch == null || escapeRoute.Length == 0) { enabled = false; return; }
            slot = GetComponent<HazardSlot>();
            Perch();
        }

        private void OnEnable() { if (rider != null) { Perch(); phase = Phase.Spent; } }
        private void OnDisable()
        {
            SetLamp(false);
            // A pigeon still carrying food is a live recovery target; it keeps flying on its own.
            if (phase != Phase.Flying && pigeon != null) pigeon.gameObject.SetActive(false);
        }

        private void Update() { if (session.IsSettled && phase != Phase.Spent && phase != Phase.Flying) { SetLamp(false); Perch(); phase = Phase.Spent; } }

        private void FixedUpdate()
        {
            if (session.IsPaused || session.IsSettled) return;
            elapsed += Time.fixedDeltaTime;
            switch (phase)
            {
                case Phase.Ready:
                    if ((slot == null || slot.StrikeAllowed) && !session.IsMapOpen && session.Stage == DeliveryStage.Carrying
                        && !rider.IsRecovering && Vector3.Distance(rider.transform.position, perch.position) <= triggerRadius)
                    {
                        activatedOrder = session.ActiveOrder.Number;
                        Enter(Phase.Warning);
                    }
                    break;
                case Phase.Warning:
                    SetLamp(Mathf.Repeat(elapsed, 0.2f) < 0.12f);
                    if (session.IsMapOpen || session.Stage != DeliveryStage.Carrying) { SetLamp(false); Perch(); Enter(Phase.Spent); break; }
                    if (elapsed >= warningSeconds) { SetLamp(false); swoopFrom = pigeon.transform.position; Enter(Phase.Swoop); }
                    break;
                case Phase.Swoop:
                    Vector3 head = rider.transform.position + Vector3.up * 0.6f;
                    pigeon.transform.position = Vector3.Lerp(swoopFrom, head, Mathf.Clamp01(elapsed / swoopSeconds));
                    if (elapsed < swoopSeconds) break;
                    if (session.TryLoseFood(pigeon.transform))
                    {
                        pigeon.Initialize(session, escapeRoute, flySpeed, escapeSeconds);
                        Enter(Phase.Flying);
                    }
                    else { Perch(); Enter(Phase.Spent); }
                    break;
                case Phase.Flying:
                    if (pigeon.Finished || !pigeon.gameObject.activeInHierarchy) Enter(Phase.Spent);
                    break;
                case Phase.Spent:
                    var order = session.ActiveOrder;
                    if (order != null && order.Number != activatedOrder && Vector3.Distance(rider.transform.position, perch.position) > triggerRadius * 2f)
                    {
                        Perch();
                        Enter(Phase.Ready);
                    }
                    break;
            }
        }

        private void Perch()
        {
            if (pigeon == null || perch == null) return;
            pigeon.gameObject.SetActive(true);
            pigeon.transform.SetPositionAndRotation(perch.position, perch.rotation);
            var body = pigeon.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic) body.linearVelocity = Vector3.zero;
        }

        private void Enter(Phase value) { phase = value; elapsed = 0f; }
        private void SetLamp(bool on) { if (warningLamp != null) warningLamp.enabled = on; }
    }
}
