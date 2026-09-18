using System.Collections;
using UnityEngine;

namespace DeliveryRider
{
    public abstract class IncidentDefinition : ScriptableObject
    {
        [SerializeField] private bool requiresOrder = true;
        [SerializeField] private bool requiresFood;
        [SerializeField, Min(0f)] private float kindCooldownSeconds = 30f;
        [SerializeField, Min(1)] private int kindMaxPerDay = 4;
        public float KindCooldownSeconds => Mathf.Max(0f, kindCooldownSeconds);
        public int KindMaxPerDay => Mathf.Max(1, kindMaxPerDay);
        public virtual bool CanContinue(DeliverySession session) => !requiresOrder || session.ActiveOrder != null;
        public virtual bool CanUseSource(IncidentTrigger source, Vector3 playerPosition, bool timed) =>
            !timed || source.Contains(playerPosition);
        public virtual bool CanStart(DeliverySession session) =>
            (!requiresOrder || session.ActiveOrder != null) &&
            (!requiresFood || session.Stage == DeliveryStage.Carrying);
        // Assets contain configuration only; execution state belongs to this routine.
        public abstract IEnumerator Execute(IncidentDirector director, IncidentTrigger source);
    }
}
