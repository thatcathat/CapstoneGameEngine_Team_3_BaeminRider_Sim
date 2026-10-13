using UnityEngine;

namespace DeliveryRider
{
    // Temporary capsule presenter. Replace this component with a model/ragdoll presenter later.
    public sealed class RiderReactionVisual : MonoBehaviour
    {
        [SerializeField] private RiderMotor motor;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Vector2 flightSpinRange = new Vector2(180f, 540f);
        [SerializeField, Min(0.01f)] private float poseSmoothTime = 0.12f;
        [SerializeField, Min(0f)] private float groundVisualClearance = 0.02f;
        private Vector3 restPosition, restScale;
        private Bounds capsuleMeshBounds;
        private Quaternion restRotation;
        private float spin, hitUntil, getUpStarted;
        private Quaternion getUpRotation;
        private bool ready;

        private void Awake()
        {
            if (visualRoot == null || motor == null) { enabled = false; return; }
            restPosition = visualRoot.localPosition;
            restRotation = visualRoot.localRotation;
            restScale = visualRoot.localScale;
            var mesh = visualRoot.GetComponent<MeshFilter>();
            capsuleMeshBounds = mesh != null && mesh.sharedMesh != null ? mesh.sharedMesh.bounds : new Bounds(Vector3.zero, new Vector3(1f, 2f, 1f));
            ready = true;
        }
        private void OnEnable()
        {
            if (motor == null) return;
            motor.ReactionChanged += OnState;
            motor.ReactionImpulse += OnImpulse;
            motor.SafePositionRestored += Restore;
        }
        private void OnDisable()
        {
            if (motor != null)
            {
                motor.ReactionChanged -= OnState;
                motor.ReactionImpulse -= OnImpulse;
                motor.SafePositionRestored -= Restore;
            }
            Restore();
        }
        private void OnState(RiderReactionState state)
        {
            if (!ready) return;
            if (state == RiderReactionState.GettingUp)
            {
                getUpStarted = Time.time;
                getUpRotation = visualRoot.localRotation;
            }
            if (state == RiderReactionState.Normal) Restore();
        }
        private void OnImpulse(Vector3 velocity) { hitUntil = Time.time + poseSmoothTime; }
        private void Restore()
        {
            if (!ready || visualRoot == null) return;
            visualRoot.localPosition = restPosition;
            visualRoot.localRotation = restRotation;
            visualRoot.localScale = restScale;
            spin = hitUntil = 0f;
        }
        private void LateUpdate()
        {
            if (!ready || motor.CameraFrozen || Time.deltaTime <= 0f) return;
            var state = motor.ReactionState;
            Quaternion pose = restRotation;
            var horizontal = Vector3.ProjectOnPlane(motor.Velocity, Vector3.up);
            var forward = horizontal.sqrMagnitude > 0.01f ? horizontal.normalized : motor.transform.forward;
            var worldFacing = Quaternion.LookRotation(forward);
            var localFacing = Quaternion.Inverse(motor.transform.rotation) * worldFacing;
            switch (state)
            {
                case RiderReactionState.Airborne:
                    spin += Mathf.Lerp(flightSpinRange.x, flightSpinRange.y, Mathf.Clamp01(motor.Velocity.magnitude / 25f)) * Time.deltaTime;
                    pose = localFacing * Quaternion.Euler(spin % 360f, 0f, Mathf.Sin(Time.time * 9f) * 18f) * restRotation;
                    break;
                case RiderReactionState.Sliding:
                    pose = localFacing * Quaternion.Euler(-65f, 0f, Mathf.Sin(Time.time * 13f) * 12f) * restRotation;
                    break;
                case RiderReactionState.Rolling:
                    spin += horizontal.magnitude * 90f * Time.deltaTime;
                    pose = localFacing * Quaternion.Euler(horizontal.magnitude > 1.5f ? spin % 360f : 90f, 0f, 10f) * restRotation;
                    break;
                case RiderReactionState.GettingUp:
                    pose = Quaternion.Slerp(getUpRotation, restRotation, Mathf.Clamp01((Time.time - getUpStarted) / motor.GetUpDuration));
                    break;
            }
            visualRoot.localRotation = state == RiderReactionState.GettingUp ? pose
                : Quaternion.Slerp(visualRoot.localRotation, pose, 1f - Mathf.Exp(-Time.deltaTime / poseSmoothTime));
            float squash = state != RiderReactionState.Normal && Time.time < hitUntil ? 0.85f : 1f;
            visualRoot.localScale = Vector3.Scale(restScale, new Vector3(1f / Mathf.Sqrt(squash), squash, 1f / Mathf.Sqrt(squash)));
            AlignCapsuleWithGround(state);
        }

        private void AlignCapsuleWithGround(RiderReactionState state)
        {
            var position = restPosition;
            if (motor.IsReactionGrounded && (state == RiderReactionState.Rolling || state == RiderReactionState.GettingUp
                || state == RiderReactionState.Sliding))
            {
                var normal = motor.ReactionGroundNormal;
                var extents = Vector3.Scale(capsuleMeshBounds.extents, visualRoot.lossyScale);
                float radius = Mathf.Max(Mathf.Abs(extents.x), Mathf.Abs(extents.z));
                float middle = Mathf.Max(0f, Mathf.Abs(extents.y) - radius);
                float reach = radius + middle * Mathf.Abs(Vector3.Dot(visualRoot.up, normal));
                var centre = visualRoot.parent.TransformPoint(restPosition)
                    + visualRoot.TransformVector(capsuleMeshBounds.center);
                float offset = reach + groundVisualClearance - Vector3.Dot(centre - motor.ReactionGroundPoint, normal);
                position += visualRoot.parent.InverseTransformVector(normal * offset);
            }
            visualRoot.localPosition = state == RiderReactionState.GettingUp ? position
                : Vector3.Lerp(visualRoot.localPosition, position, 1f - Mathf.Exp(-Time.deltaTime / poseSmoothTime));
        }
    }
}
