using UnityEngine;

namespace DeliveryRider
{
    // A reusable wave of solid, dynamic melons. Contact disables control; physics supplies the push.
    public sealed class RollingMelonHazard : MonoBehaviour
    {
        private enum Phase { Ready, Rolling, Spent }
        [SerializeField] private DeliverySession session;
        [SerializeField] private RollingMelon[] melons = System.Array.Empty<RollingMelon>();
        [SerializeField, Min(0.1f)] private float approachRadius = 6f;
        [SerializeField, Min(0.1f)] private float rollSpeed = 6f;
        [SerializeField, Min(0f)] private float driveAcceleration = 12f;
        [SerializeField, Min(0.1f)] private float waveSeconds = 4f;
        [SerializeField, Min(0.1f)] private float tumbleSeconds = 2.2f;
        [SerializeField, Min(0f)] private float rearmSeconds = 10f;
        [SerializeField, Min(0f)] private float rearmDistance = 8f;
        private RiderMotor rider;
        private HazardSlot slot;
        private Phase phase;
        private float elapsed;
        private bool hitThisWave;
        private bool ready;

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
            var offset = rider.transform.position - transform.position;
            float distance = Vector3.ProjectOnPlane(offset, Vector3.up).magnitude;
            switch (phase)
            {
                case Phase.Ready:
                    if ((slot == null || slot.StrikeAllowed) && Mathf.Abs(offset.y) < 3f && distance <= approachRadius)
                    {
                        phase = Phase.Rolling;
                        elapsed = 0f;
                        hitThisWave = false;
                        var direction = Vector3.ProjectOnPlane(-transform.forward, Vector3.up).normalized;
                        foreach (var melon in melons) melon.Release(direction, rollSpeed);
                    }
                    break;
                case Phase.Rolling:
                    if (elapsed >= waveSeconds) { Finish(); break; }
                    foreach (var melon in melons) melon.Drive(rollSpeed, driveAcceleration);
                    break;
                case Phase.Spent:
                    if (elapsed >= rearmSeconds && distance > rearmDistance) ResetWave();
                    break;
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
            foreach (var melon in melons) melon.Park();
            phase = Phase.Ready;
            elapsed = 0f;
            hitThisWave = false;
        }

        private void Finish()
        {
            foreach (var melon in melons) if (melon != null) melon.Hide();
            if (phase != Phase.Spent) elapsed = 0f;
            phase = Phase.Spent;
        }
    }
}
