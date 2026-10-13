using System;
using UnityEngine;

namespace DeliveryRider
{
    // Baked main-road distances between the 12 shops and 32 doors (Tools/build_route_table.py).
    // Field names match Docs/MapLayout/route_table.json so the installer can overwrite them from JSON.
    [CreateAssetMenu(menuName = "Delivery Rider/Route Table", fileName = "RouteTable")]
    public sealed class DeliveryRouteTable : ScriptableObject
    {
        [SerializeField] private string[] zoneIds = Array.Empty<string>();
        [SerializeField] private string[] zoneNames = Array.Empty<string>();
        [SerializeField] private string[] shopNames = Array.Empty<string>();
        [SerializeField] private string[] shopZones = Array.Empty<string>();
        [SerializeField] private string[] doorZones = Array.Empty<string>();
        [SerializeField] private float[] shopToDoor = Array.Empty<float>();
        [SerializeField] private float[] doorToDoor = Array.Empty<float>();

        public int ShopCount => shopNames.Length;
        public int DoorCount => doorZones.Length;
        public bool IsValid => ShopCount > 0 && DoorCount > 0 && shopZones.Length == ShopCount
            && shopToDoor.Length == ShopCount * DoorCount && doorToDoor.Length == DoorCount * DoorCount;
        public string ShopName(int shop) => shopNames[shop];
        public string ShopZone(int shop) => shopZones[shop];
        public string DoorZone(int door) => doorZones[door];
        public float ShopToDoor(int shop, int door) => shopToDoor[shop * DoorCount + door];
        public float DoorToDoor(int from, int to) => doorToDoor[from * DoorCount + to];

        public string ZoneName(string zone)
        {
            int index = Array.IndexOf(zoneIds, zone);
            return index >= 0 && index < zoneNames.Length ? zoneNames[index] : zone;
        }
    }
}
