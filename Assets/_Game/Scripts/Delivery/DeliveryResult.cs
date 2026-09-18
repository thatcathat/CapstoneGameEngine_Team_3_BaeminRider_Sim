namespace DeliveryRider
{
    // Snapshot completed deliveries so asset edits cannot rewrite historical earnings.
    public sealed class DeliveryResult
    {
        public int Number { get; }
        public string Restaurant { get; }
        public string Customer { get; }
        public double DurationSeconds { get; }
        public bool WasLate { get; }
        public int Reward { get; }

        public DeliveryResult(DeliveryOrderOffer order, double durationSeconds, bool wasLate, int reward)
        {
            Number = order.Number;
            Restaurant = order.Definition.RestaurantName;
            Customer = order.Definition.CustomerName;
            DurationSeconds = durationSeconds;
            WasLate = wasLate;
            Reward = reward;
        }
    }
}
