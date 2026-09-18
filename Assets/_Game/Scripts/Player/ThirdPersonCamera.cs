using UnityEngine;

namespace DeliveryRider
{
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private RiderMotor motor;
        [SerializeField] private float distance = 7f;
        [SerializeField] private float sensitivity = 0.12f;
        [SerializeField] private float pitch = 26f;
        [SerializeField] private float targetHeight = 1f;
        [SerializeField] private LayerMask obstructionMask = 1;
        private float yaw;

        private void LateUpdate()
        {
            var look = motor.Look;
            yaw += look.x * sensitivity;
            pitch = Mathf.Clamp(pitch - look.y * sensitivity, 12f, 65f);
            var focus = target.position + Vector3.up * targetHeight;
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            var direction = rotation * Vector3.back;
            var actualDistance = distance;
            if (Physics.SphereCast(focus, 0.22f, direction, out var hit, distance, obstructionMask, QueryTriggerInteraction.Ignore))
                actualDistance = Mathf.Max(0.35f, hit.distance - 0.15f);
            transform.SetPositionAndRotation(focus + direction * actualDistance, rotation);
        }
    }
}
