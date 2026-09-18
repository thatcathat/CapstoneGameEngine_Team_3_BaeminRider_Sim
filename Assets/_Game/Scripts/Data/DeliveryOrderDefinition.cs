using UnityEngine;

namespace DeliveryRider
{
    [CreateAssetMenu(menuName = "Delivery Rider/Order Definition", fileName = "Order")]
    public sealed class DeliveryOrderDefinition : ScriptableObject
    {
        [SerializeField] private string restaurantName;
        [SerializeField] private string customerName;
        [SerializeField] private string foodName;
        [SerializeField, Min(0)] private int onTimeReward = 5000;
        [SerializeField, Min(0)] private int lateReward = 1000;
        [SerializeField, Min(1)] private float timeLimitSeconds = 90f;

        public string RestaurantName => restaurantName;
        public string CustomerName => customerName;
        public string FoodName => foodName;
        public int OnTimeReward => onTimeReward;
        public int LateReward => lateReward;
        public float TimeLimitSeconds => timeLimitSeconds;
    }
}
