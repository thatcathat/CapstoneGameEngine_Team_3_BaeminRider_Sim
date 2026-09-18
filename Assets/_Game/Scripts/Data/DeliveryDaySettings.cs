using UnityEngine;

namespace DeliveryRider
{
    [CreateAssetMenu(menuName = "Delivery Rider/Day Settings", fileName = "DaySettings")]
    public sealed class DeliveryDaySettings : ScriptableObject
    {
        [SerializeField, Min(1f)] private float durationSeconds = 300f;
        public float DurationSeconds => Mathf.Max(1f, durationSeconds);
    }
}
