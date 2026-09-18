using UnityEngine;

namespace DeliveryRider
{
    public sealed class TimedIncidentScheduler : MonoBehaviour
    {
        [SerializeField] private IncidentDirector director;
        [SerializeField] private IncidentTrigger[] sources = System.Array.Empty<IncidentTrigger>();
        [SerializeField, Min(1f)] private float minimumInterval = 25f;
        [SerializeField, Min(1f)] private float maximumInterval = 40f;
        private double nextAt;
        private void OnEnable() => Schedule();
        private void Schedule()
        {
            float minimum = Mathf.Max(1f, minimumInterval);
            nextAt = Time.timeAsDouble + Random.Range(minimum, Mathf.Max(minimum, maximumInterval));
        }
        private void Update()
        {
            if (Time.timeAsDouble < nextAt) return;
            Schedule(); // No backlog, even if no source or event can run this time.
            if (!director.CanRequest) return;
            IncidentTrigger selected = null;
            int candidates = 0;
            foreach (var source in sources)
            {
                if (source == null || !source.IsCandidate(true)) continue;
                candidates++;
                if (Random.Range(0, candidates) == 0) selected = source;
            }
            if (selected != null) selected.TryActivate(true);
        }
    }
}
