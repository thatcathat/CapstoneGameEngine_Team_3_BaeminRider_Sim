using UnityEngine;

namespace DeliveryRider
{
    public abstract class IncidentDefinition : ScriptableObject
    {
        [SerializeField] private IncidentActor actorPrefab;
        public IncidentActor ActorPrefab => actorPrefab;
        public virtual bool CanUseSource(IncidentTrigger source, Vector3 playerPosition, bool timed) =>
            !timed || source.Contains(playerPosition);
        public virtual bool CanStart(DeliverySession session) => actorPrefab != null;
    }
}
