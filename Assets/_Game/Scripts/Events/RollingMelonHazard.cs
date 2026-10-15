using UnityEngine;

namespace DeliveryRider
{
    // A reusable wave of solid, dynamic melons. Contact disables control; physics supplies the push.
    // The melons wait as a stacked pile laid out afresh for every wave. The wave starts when the rider nears
    // the pile and rolls along the road toward the rider; the pile collapses front-bottom first.
    public sealed class RollingMelonHazard : MonoBehaviour
    {
        private enum Phase { Ready, Rolling, Spent }
        [SerializeField] private DeliverySession session;
        [Tooltip("Every melon a pile may use; a wave takes the first pileCount of them and hides the rest.")]
        [SerializeField] private RollingMelon[] melons = System.Array.Empty<RollingMelon>();
        [Header("Pile (test values)")]
        [Tooltip("Middle of the pile in local space (y is ignored; each melon sits on the ground below it).")]
        [SerializeField] private Vector3 pileOffset = new Vector3(0f, 0f, 3.5f);
        [Tooltip("Smallest and largest number of melons in a pile.")]
        [SerializeField] private Vector2Int pileCount = new Vector2Int(6, 10);
        [Tooltip("Upper and back melons start up to this much later, so the pile tumbles instead of launching as one.")]
        [SerializeField, Min(0f)] private float releaseSpread = 0.4f;
        [Header("Wave (test values)")]
        [Tooltip("Smallest start distance, measured from the middle of the melons.")]
        [SerializeField, Min(0.1f)] private float approachRadius = 6f;
        [Tooltip("The start distance grows to the rider's speed times this, so fast riders see the melons coming.")]
        [SerializeField, Min(0f)] private float approachLeadSeconds = 1.2f;
        [SerializeField, Min(0.1f)] private float rollSpeed = 6f;
        [SerializeField, Min(0f)] private float driveAcceleration = 12f;
        [SerializeField, Min(0.1f)] private float waveSeconds = 4f;
        [SerializeField, Min(0.1f)] private float tumbleSeconds = 2.2f;
        [SerializeField, Min(0f)] private float rearmSeconds = 10f;
        [SerializeField, Min(0f)] private float rearmDistance = 8f;
        // Base footprints (columns x rows); each layer above is one smaller in both directions.
        private static readonly Vector2Int[] Bases = { new Vector2Int(2, 2), new Vector2Int(3, 2), new Vector2Int(2, 3), new Vector2Int(3, 3) };
        private const float Gap = 1.04f, LayerRise = 0.68f, Jitter = 0.04f;
        private readonly RaycastHit[] groundHits = new RaycastHit[8];
        private RiderMotor rider;
        private HazardSlot slot;
        private Phase phase;
        private float elapsed;
        private bool hitThisWave;
        private bool ready;
        private int active;
        private int[] layers;
        private float[] releaseAt;
        private Vector3 direction;

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
            layers = new int[melons.Length];
            releaseAt = new float[melons.Length];
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
                    if ((slot == null || slot.StrikeAllowed) && Mathf.Abs(offset.y) < 3f && distance <= reach) BeginWave(flatOffset);
                    break;
                case Phase.Rolling:
                    if (elapsed >= waveSeconds) { Finish(); break; }
                    for (int i = 0; i < active; i++)
                    {
                        var melon = melons[i];
                        if (!melon.IsLoose && elapsed >= releaseAt[i]) melon.Release(direction, layers[i] > 0 ? rollSpeed * 0.5f : rollSpeed);
                        melon.Drive(rollSpeed, driveAcceleration);
                    }
                    break;
                case Phase.Spent:
                    if (elapsed >= rearmSeconds && distance > rearmDistance) ResetWave();
                    break;
            }
        }

        // Roll along the road toward whichever end the rider is coming from. Melons nearest the rider and on the
        // bottom layer go first; the rest follow within releaseSpread.
        private void BeginWave(Vector3 towardRider)
        {
            phase = Phase.Rolling;
            elapsed = 0f;
            hitThisWave = false;
            var along = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            direction = Vector3.Dot(towardRider, along) >= 0f ? along : -along;
            var center = transform.TransformPoint(pileOffset);
            int topLayer = 0;
            float front = float.NegativeInfinity, back = float.PositiveInfinity;
            for (int i = 0; i < active; i++)
            {
                topLayer = Mathf.Max(topLayer, layers[i]);
                float lead = Vector3.Dot(melons[i].transform.position - center, direction);
                front = Mathf.Max(front, lead);
                back = Mathf.Min(back, lead);
            }
            for (int i = 0; i < active; i++)
            {
                float height = topLayer == 0 ? 0f : layers[i] / (float)topLayer;
                float behind = front - back < 0.01f ? 0f : (front - Vector3.Dot(melons[i].transform.position - center, direction)) / (front - back);
                releaseAt[i] = releaseSpread * (0.6f * height + 0.4f * behind);
            }
        }

        public void Contact(RiderMotor target)
        {
            if (phase != Phase.Rolling || hitThisWave || target != rider || session.IsMapOpen || session.IsSettled) return;
            // Do not stop or remove the melons when a hit succeeds.
            if (rider.TryHazardTumble(this, tumbleSeconds)) hitThisWave = true;
        }

        private void ResetWave()
        {
            LayOutPile();
            phase = Phase.Ready;
            elapsed = 0f;
            hitThisWave = false;
        }

        // A square-packed pyramid on a random base, filled bottom-up with a random number of melons,
        // each nudged and turned a little so no two piles look the same.
        private void LayOutPile()
        {
            float diameter = Mathf.Abs(melons[0].transform.localScale.x);
            int want = Mathf.Clamp(Random.Range(pileCount.x, pileCount.y + 1), 1, melons.Length);
            var footprint = Bases[Bases.Length - 1];
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var candidate = Bases[Random.Range(0, Bases.Length)];
                if (Capacity(candidate) >= want) { footprint = candidate; break; }
            }
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
                        layers[active] = layer;
                        melons[active].Park(local, Random.rotation);
                        active++;
                    }
            }
            for (int i = active; i < melons.Length; i++) melons[i].Hide();
        }

        private static int Capacity(Vector2Int footprint)
        {
            int total = 0;
            for (int layer = 0; footprint.x - layer > 0 && footprint.y - layer > 0; layer++) total += (footprint.x - layer) * (footprint.y - layer);
            return total;
        }

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
