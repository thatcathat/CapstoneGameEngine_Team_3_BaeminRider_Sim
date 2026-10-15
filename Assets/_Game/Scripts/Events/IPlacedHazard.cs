namespace DeliveryRider
{
    // A hazard prefab spawned at runtime: before it wakes it receives the scene's session and fits itself to its spot.
    public interface IPlacedHazard
    {
        void Place(DeliverySession owner, RoadNetwork ground, HazardCandidate spot);
    }
}
