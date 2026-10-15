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
            home = transform.localPosition;
            homeRotation = transform.localRotation;
            radius = GetComponent<SphereCollider>().radius * Mathf.Abs(transform.lossyScale.x);
        }

        public void Park()
        {
            Hide();
            transform.localPosition = home;
            transform.localRotation = homeRotation;
            gameObject.SetActive(true);
        }

        public void Release(Vector3 rollDirection, float speed)
        {
            direction = rollDirection;
            body.isKinematic = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = direction * speed;
            body.angularVelocity = Vector3.Cross(Vector3.up, direction) * (speed / radius);
            body.WakeUp();
        }

        public void Drive(float speed, float acceleration)
        {
            if (body.isKinematic || !gameObject.activeInHierarchy) return;
            // Force-based drive leaves blocking, pushing and deflection to the collision solver.
            var horizontal = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            body.AddForce(Vector3.ClampMagnitude((direction * speed - horizontal) / Time.fixedDeltaTime, acceleration), ForceMode.Acceleration);
            var angular = Vector3.Cross(Vector3.up, horizontal) / radius;
            body.AddTorque(Vector3.ClampMagnitude((angular - body.angularVelocity) / Time.fixedDeltaTime, acceleration / radius), ForceMode.Acceleration);
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
            if (owner != null && collision.rigidbody != null && collision.rigidbody.TryGetComponent<RiderMotor>(out var rider))
                owner.Contact(rider);
        }
    }
}
