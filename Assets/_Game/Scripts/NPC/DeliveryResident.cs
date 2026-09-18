using UnityEngine;

namespace DeliveryRider
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class DeliveryResident : MonoBehaviour
    {
        [SerializeField] private DeliverySession session;
        [SerializeField] private float recoveryRadius = 12f;
        private Rigidbody body;
        private Vector3 home;
        private Quaternion facing;
        private void Awake() { body = GetComponent<Rigidbody>(); home = transform.position; facing = transform.rotation; }
        private void FixedUpdate()
        {
            if (body.position.y < -3f || Vector3.Distance(body.position, home) > recoveryRadius) ReturnHome();
        }
        private void OnCollisionEnter(Collision collision)
        {
            if (collision.rigidbody != null && collision.rigidbody.TryGetComponent<RiderMotor>(out _))
                session.TryDeliver(this);
        }
        public void ReturnHome()
        {
            body.position = home;
            body.rotation = facing;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}
