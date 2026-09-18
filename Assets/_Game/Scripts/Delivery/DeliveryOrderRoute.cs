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

    public sealed class DeliveryOrderOffer
    {
        public int Number { get; }
        public DeliveryOrderRoute Route { get; }
        public DeliveryOrderDefinition Definition => Route.Definition;
        public DeliveryOrderOffer(int number, DeliveryOrderRoute route) { Number = number; Route = route; }
    }
}
