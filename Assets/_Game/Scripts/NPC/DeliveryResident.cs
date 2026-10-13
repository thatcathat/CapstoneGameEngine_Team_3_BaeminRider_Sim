using UnityEngine;

namespace DeliveryRider
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class DeliveryResident : MonoBehaviour, IPunchable
    {
        [SerializeField] private DeliverySession session;
        [SerializeField] private float recoveryRadius = 12f;
        [Header("Punch reaction (test values)")]
        [SerializeField, Min(0f)] private float deliveredLaunchSpeed = 14f;
        [SerializeField, Min(0f)] private float deliveredLaunchLift = 9f;
        [SerializeField, Min(0.1f)] private float flightSeconds = 3f;
        [SerializeField, Min(0f)] private float shoveSpeed = 2.5f;
        private Rigidbody body;
        private Vector3 home;
        private Quaternion facing;
        private float flyingUntil = float.NegativeInfinity;
        private bool flying;
        private void Awake() { body = GetComponent<Rigidbody>(); home = transform.position; facing = transform.rotation; }
        private void FixedUpdate()
        {
            // The delivered recipient is allowed to leave the recovery radius until the flight ends.
            if (flying)
            {
                if (Time.time < flyingUntil) return;
                flying = false;
                ReturnHome();
                return;
            }
            if (body.position.y < -3f || Vector3.Distance(body.position, home) > recoveryRadius) ReturnHome();
        }

        // Only a punch completes a delivery; bumping into the recipient does nothing special.
        public bool ReceivePunch(Vector3 direction)
        {
            var horizontal = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            if (session != null && session.TryDeliver(this))
            {
                flying = true;
                flyingUntil = Time.time + flightSeconds;
                body.linearVelocity = horizontal * deliveredLaunchSpeed + Vector3.up * deliveredLaunchLift;
                body.angularVelocity = Random.onUnitSphere * 8f;
                return true;
            }
            if (flying) return false;
            body.AddForce(horizontal * shoveSpeed, ForceMode.VelocityChange);
            return false;
        }

        // Generated calls reuse one recipient and move it to the chosen door.
        public void AssignHome(Vector3 position, Quaternion rotation)
        {
            home = position;
            facing = rotation;
            ReturnHome();
        }

        public void ReturnHome()
        {
            flying = false;
            body.position = home;
            body.rotation = facing;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}
