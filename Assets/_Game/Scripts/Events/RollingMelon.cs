using UnityEngine;

namespace DeliveryRider
{
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class RollingMelon : MonoBehaviour
    {
        private RollingMelonHazard owner;
        private Rigidbody body;
        private Vector3 home, direction;
        private Quaternion homeRotation;
        private float radius;

        public void Prepare(RollingMelonHazard hazard)
        {
            owner = hazard;
            body = GetComponent<Rigidbody>();
            radius = GetComponent<SphereCollider>().radius * Mathf.Abs(transform.lossyScale.x);
        }

        public bool IsLoose => body != null && !body.isKinematic;

        // Parks the melon at a new resting place in the pile (the pile is laid out afresh for every wave).
        public void Park(Vector3 localPosition, Quaternion localRotation)
        {
            Hide();
            home = localPosition;
            homeRotation = localRotation;
            transform.localPosition = home;
            transform.localRotation = homeRotation;
            gameObject.SetActive(true);
        }

        public void Release(Vector3 rollDirection, float speed, float lift)
        {
            direction = rollDirection;
            body.isKinematic = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = direction * speed + Vector3.up * lift;
            body.angularVelocity = Vector3.Cross(Vector3.up, direction) * (speed / radius);
            body.WakeUp();
        }

        public void Hide()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (!body.isKinematic) body.linearVelocity = body.angularVelocity = Vector3.zero;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.isKinematic = true;
            gameObject.SetActive(false);
        }

        private void OnCollisionEnter(Collision collision) => Contact(collision);
        private void OnCollisionStay(Collision collision) => Contact(collision);
        private void Contact(Collision collision)
        {
            if (!IsLoose || !gameObject.activeSelf || collision.rigidbody == null) return;
            var other = collision.rigidbody;
            if (other.TryGetComponent<TrafficCar>(out _)) Hide();
            else if (other.TryGetComponent<TrafficPedestrian>(out var person))
            {
                const float knockSpeed = 3f, knockLift = 1.2f;
                person.TryHazardKnock(direction * knockSpeed + Vector3.up * knockLift);
                Hide();
            }
            else if (other.TryGetComponent<RiderMotor>(out var rider))
            {
                if (owner != null) owner.Contact(this, rider);
                Hide();
            }
        }
    }
}
