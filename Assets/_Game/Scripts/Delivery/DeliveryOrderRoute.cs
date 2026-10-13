using System;
using UnityEngine;

namespace DeliveryRider
{
    // Scene references belong to the scene; reusable pricing/text lives in the asset.
    [Serializable]
    public sealed class DeliveryOrderRoute
    {
        [SerializeField] private DeliveryOrderDefinition definition;
        [SerializeField] private Transform pickupPoint;
        [SerializeField] private DeliveryResident customer;
        public DeliveryOrderDefinition Definition => definition;
        public Transform PickupPoint => pickupPoint;
        public DeliveryResident Customer => customer;
        public bool IsValid => definition != null && pickupPoint != null && customer != null;
    }

    // One call. Values are copied at creation so later asset edits never change an offered price.
    public sealed class DeliveryOrderOffer
    {
        public int Number { get; }
        // Set only for legacy fixed routes.
        public DeliveryOrderRoute Route { get; }
        public Transform PickupPoint { get; }
        public DeliveryResident Recipient { get; }
        // Where the recipient stands; for generated calls the recipient is moved here on accept.
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
        public bool MovesRecipient { get; }
        public bool IsValid => PickupPoint != null && Recipient != null && DestinationPoint != null;

        // Legacy fixed route (GameScene and scenes without a route table).
        public DeliveryOrderOffer(int number, DeliveryOrderRoute route)
        {
            Number = number;
            Route = route;
            PickupPoint = route.PickupPoint;
            Recipient = route.Customer;
            DestinationPoint = route.Customer != null ? route.Customer.transform : null;
            RestaurantName = route.Definition.RestaurantName;
            CustomerName = route.Definition.CustomerName;
            FoodName = route.Definition.FoodName;
            OnTimeReward = route.Definition.OnTimeReward;
            LateReward = route.Definition.LateReward;
            TimeLimitSeconds = route.Definition.TimeLimitSeconds;
            ShopIndex = -1;
            DoorIndex = -1;
        }

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
            MovesRecipient = true;
        }
    }
}
