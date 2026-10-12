using UnityEngine;

namespace DeliveryRider
{
    [CreateAssetMenu(menuName = "Delivery Rider/Incidents/Crash")]
    public sealed class CrashIncidentDefinition : IncidentDefinition
    {
        [SerializeField, Min(0.1f)] private float warningSeconds = 0.8f;
        [SerializeField, Min(0f)] private float horizontalImpulse = 8f;
        [SerializeField, Min(0f)] private float upwardImpulse = 4f;
        [SerializeField, Min(0.1f)] private float recoverySeconds = 2f;

        public float WarningSeconds => warningSeconds;
        public float HorizontalImpulse => horizontalImpulse;
        public float UpwardImpulse => upwardImpulse;
        public float RecoverySeconds => recoverySeconds;
        public override bool CanStart(DeliverySession session) =>
            base.CanStart(session) && ActorPrefab is CrashIncidentActor;
    }
}
