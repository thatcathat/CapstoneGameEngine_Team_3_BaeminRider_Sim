using System.Collections;
using UnityEngine;

namespace DeliveryRider
{
    // One spawned object owns the behavior for each running incident.
    public abstract class IncidentActor : MonoBehaviour
    {
        public abstract IEnumerator Execute(IncidentDirector director, IncidentTrigger source, IncidentDefinition definition);
    }
}
