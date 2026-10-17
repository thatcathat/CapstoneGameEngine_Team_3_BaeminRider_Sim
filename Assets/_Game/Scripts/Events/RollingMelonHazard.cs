using UnityEngine;

namespace DeliveryRider
{
    // A reusable pile that bursts outward once, then lets gravity and collisions drive each melon.
    public sealed class RollingMelonHazard : MonoBehaviour, IPlacedHazard
    {
        private enum Phase { Ready, Rolling, Spent }
        [SerializeField] private DeliverySession session;
        [Tooltip("Every melon a pile may use; a wave takes the first pileCount of them and hides the rest.")]
        [SerializeField] private RollingMelon[] melons = System.Array.Empty<RollingMelon>();
        [Header("Pile (test values)")]
        [Tooltip("Middle of the pile in local space (y is ignored; each melon sits on the ground below it).")]
        [SerializeField] private Vector3 pileOffset = Vector3.zero;
        [Tooltip("Smallest and largest number of melons in a pile.")]
        [SerializeField] private Vector2Int pileCount = new Vector2Int(14, 14);
        [Header("Wave (test values)")]
        [Tooltip("Smallest start distance, measured from the middle of the melons.")]
        [SerializeField, Min(0.1f)] private float approachRadius = 6f;
        [Tooltip("The start distance grows to the rider's speed times this, so fast riders see the melons coming.")]
        [SerializeField, Min(0f)] private float approachLeadSeconds = 1.2f;
        [SerializeField, Min(0.1f)] private float rollSpeed = 6f;
        [SerializeField, Min(0f)] private float burstLift = 1.2f;
        [SerializeField, Range(0.1f, 10f)] private float waveSeconds = 10f;
        [SerializeField, Min(0.1f)] private float tumbleSeconds = 0.1f;
        [Tooltip("Extra horizontal speed when another melon hits the rider while rolling or getting up.")]
        [SerializeField, Min(0f)] private float downedPushSpeed = 1.5f;
        [SerializeField, Min(0f)] private float rearmSeconds = 10f;
        [SerializeField, Min(0f)] private float rearmDistance = 8f;
        // A compact three-level pyramid: nine on the ground, four above, one on top.
        private const float Gap = 1.02f, LayerRise = 0.72f, Jitter = 0.015f;
        private readonly RaycastHit[] groundHits = new RaycastHit[8];
        private RiderMotor rider;
        private HazardSlot slot;
        private Phase phase;
        private float elapsed;
        private bool ready;
        private int active;

        public void Place(DeliverySession owner, RoadNetwork ground, HazardCandidate spot) => session = owner;

        private void Awake()
        {
            rider = session != null && session.Player != null ? session.Player.GetComponent<RiderMotor>() : null;
            slot = GetComponent<HazardSlot>();
            if (rider == null || melons.Length == 0) { enabled = false; return; }
            foreach (var melon in melons)
            {
                if (melon == null) { enabled = false; return; }
                melon.Prepare(this);
            }
            ready = true;
            ResetWave();
        }

        private void OnEnable() { if (ready) ResetWave(); }
        private void OnDisable() { if (ready) Finish(); }
        private void Update()
        {
            if (ready && (session.IsSettled || session.IsMapOpen && phase == Phase.Rolling)) Finish();
        }

        private void FixedUpdate()
        {
            if (!ready || session.IsPaused || session.IsSettled || session.IsMapOpen) return;
            elapsed += Time.fixedDeltaTime;
            var offset = rider.transform.position - transform.TransformPoint(pileOffset);
            var flatOffset = Vector3.ProjectOnPlane(offset, Vector3.up);
            float distance = flatOffset.magnitude;
            switch (phase)
            {
                case Phase.Ready:
                    float reach = Mathf.Max(approachRadius, rider.HorizontalSpeed * approachLeadSeconds);
                    if ((slot == null || slot.StrikeAllowed) && Mathf.Abs(offset.y) < 3f && distance <= reach) BeginWave();
                    break;
                case Phase.Rolling:
                    if (elapsed >= Mathf.Min(waveSeconds, 10f)) Finish();
                    break;
                case Phase.Spent:
                    if (elapsed >= rearmSeconds && distance > rearmDistance) ResetWave();
                    break;
            }
        }

        private void BeginWave()
        {
            phase = Phase.Rolling;
            elapsed = 0f;
            float startAngle = Random.Range(0f, 360f);
            for (int i = 0; i < active; i++)
            {
                // Distribute around the whole circle, with a little variation per melon.
                float angle = startAngle + i * 360f / active + Random.Range(-10f, 10f);
                var outward = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                melons[i].Release(outward, rollSpeed * Random.Range(0.65f, 1f), burstLift);
            }
        }

        public void Contact(RollingMelon melon, RiderMotor target)
        {
            if (phase != Phase.Rolling || melon == null || target != rider || session.IsMapOpen || session.IsSettled) return;
            var push = Vector3.zero;
            if (rider.ReactionState == RiderReactionState.Rolling || rider.ReactionState == RiderReactionState.GettingUp)
            {
                var away = Vector3.ProjectOnPlane(rider.transform.position - melon.transform.position, Vector3.up);
                if (away.sqrMagnitude < 0.0001f) away = -rider.transform.forward;
                push = away.normalized * downedPushSpeed;
            }
            rider.TryHazardTumble(melon, tumbleSeconds, true, push);
        }

        private void ResetWave()
        {
            LayOutPile();
            phase = Phase.Ready;
            elapsed = 0f;
        }

        // Keep the base centred and fill the 3 x 3, 2 x 2 and 1 x 1 layers in order.
        private void LayOutPile()
        {
            float diameter = Mathf.Abs(melons[0].transform.localScale.x);
            int want = Mathf.Clamp(Random.Range(pileCount.x, pileCount.y + 1), 1, melons.Length);
            var footprint = new Vector2Int(3, 3);
            active = 0;
            for (int layer = 0; active < want; layer++)
            {
                int columns = footprint.x - layer, rows = footprint.y - layer;
                if (columns <= 0 || rows <= 0) break;
                for (int row = 0; row < rows && active < want; row++)
                    for (int column = 0; column < columns && active < want; column++)
                    {
                        var local = pileOffset + new Vector3((column - (columns - 1) * 0.5f) * diameter * Gap + Random.Range(-Jitter, Jitter) * diameter,
                            0f, (row - (rows - 1) * 0.5f) * diameter * Gap + Random.Range(-Jitter, Jitter) * diameter);
                        local.y = GroundHeight(local) + diameter * (0.5f + layer * LayerRise) + 0.03f;
                        melons[active].Park(local, Random.rotation);
                        active++;
                    }
            }
            for (int i = active; i < melons.Length; i++) melons[i].Hide();
        }

        public static float PileHalfExtent(float diameter) => diameter * (Gap + Jitter + 0.5f);

        // Local height of the terrain under a local point (the static ground mesh, not props or bodies).
        private float GroundHeight(Vector3 local)
        {
            local.y = 0f;
            var world = transform.TransformPoint(local);
            int count = Physics.RaycastNonAlloc(world + Vector3.up * 5f, Vector3.down, groundHits, 10f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity, height = 0f;
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (!(hit.collider is MeshCollider) || hit.rigidbody != null || hit.distance >= nearest) continue;
                nearest = hit.distance;
                height = transform.InverseTransformPoint(hit.point).y;
            }
            return height;
        }

        private void Finish()
        {
            foreach (var melon in melons) if (melon != null) melon.Hide();
            if (phase != Phase.Spent) elapsed = 0f;
            phase = Phase.Spent;
        }
    }
}
