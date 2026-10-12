using UnityEngine;

namespace DeliveryRider
{
    public enum TheftRecoveryMode { ChaseThenReplacement, ReplacementOnly }

    [CreateAssetMenu(menuName = "Delivery Rider/Incidents/Food Theft")]
    public sealed class TheftIncidentDefinition : IncidentDefinition
    {
        [SerializeField] private TheftRecoveryMode recoveryMode;
        [SerializeField] private FoodThief thiefPrefab;
        [SerializeField, Min(0.1f)] private float thiefSpeed = 3.6f;
        [SerializeField, Min(1f)] private float escapeSeconds = 18f;
        public TheftRecoveryMode RecoveryMode => recoveryMode;
        public FoodThief ThiefPrefab => thiefPrefab;
        public float ThiefSpeed => thiefSpeed;
        public float EscapeSeconds => escapeSeconds;
        public override bool CanStart(DeliverySession session) =>
            base.CanStart(session) && ActorPrefab is TheftIncidentActor;
        public override bool CanUseSource(IncidentTrigger source, Vector3 playerPosition, bool timed) =>
            recoveryMode == TheftRecoveryMode.ReplacementOnly || (thiefPrefab != null && source.HasEscapeRoute);
    }
}
