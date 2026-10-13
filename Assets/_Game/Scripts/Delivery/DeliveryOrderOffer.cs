using UnityEngine;

namespace DeliveryRider
{
    // One call. Values are copied at creation so later asset edits never change an offered price.
    public sealed class DeliveryOrderOffer
    {
        public int Number { get; }
        public Transform PickupPoint { get; }
        public DeliveryResident Recipient { get; }
        // Where the recipient stands; the recipient is moved here on accept.
        public Transform DestinationPoint { get; }
        public string RestaurantName { get; }
        public string CustomerName { get; }
        public string FoodName { get; }
        public int OnTimeReward { get; }
        public int LateReward { get; }
        public float TimeLimitSeconds { get; }
        public float Distance { get; }
        public string TierLabel { get; }
        public string PickupZone { get; }
        public string DestinationZone { get; }
        public int ShopIndex { get; }
        public int DoorIndex { get; }
        public bool IsValid => PickupPoint != null && Recipient != null && DestinationPoint != null;

        // Generated call: current-zone shop to a door in another zone.
        public DeliveryOrderOffer(int number, Transform pickupPoint, DeliveryResident recipient, Transform destinationPoint,
            string restaurantName, string customerName, string foodName, int onTimeReward, int lateReward, float timeLimitSeconds,
            float distance, string tierLabel, string pickupZone, string destinationZone, int shopIndex, int doorIndex)
        {
            Number = number;
            PickupPoint = pickupPoint;
            Recipient = recipient;
            DestinationPoint = destinationPoint;
            RestaurantName = restaurantName;
            CustomerName = customerName;
            FoodName = foodName;
            OnTimeReward = onTimeReward;
            LateReward = lateReward;
            TimeLimitSeconds = timeLimitSeconds;
            Distance = distance;
            TierLabel = tierLabel;
            PickupZone = pickupZone;
            DestinationZone = destinationZone;
            ShopIndex = shopIndex;
            DoorIndex = doorIndex;
        }
    }
}
