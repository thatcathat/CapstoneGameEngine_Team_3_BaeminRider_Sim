using System.Collections.Generic;
using UnityEngine;

namespace DeliveryRider
{
    // A road vehicle on the TrafficNetwork: follows lanes and junction connectors, keeps distance to the car ahead,
    // waits for junction reservations and for room on the exit lane, and brakes only for the rider in its lane (some cars, by chance, do not).
    // Kinematic; hitting the rider knocks them away (same rule as the former pilot vans).
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class TrafficCar : MonoBehaviour
    {
        [Header("Driving (test values)")]
        [SerializeField, Min(0.1f)] private float cruiseSpeed = 12f;
        [SerializeField, Min(0.1f)] private float acceleration = 2.5f;
        [SerializeField, Min(0.1f)] private float braking = 7f;
        [SerializeField, Min(0.1f)] private float slopeSpeed = 9f;
        [SerializeField, Min(0.1f)] private float followGap = 2.5f;
        [SerializeField, Min(0.1f)] private float obstacleClearance = 1f;
        [SerializeField, Min(0f)] private float riderBrakeDelay = 2f;
        [Tooltip("Chance, rolled once per encounter, that this car neither brakes nor stops for the rider. The map always brakes.")]
        [SerializeField, Range(0f, 1f)] private float riderIgnoreChance = 0.3f;
        [Tooltip("A new encounter (and a new roll) starts once the rider has been out of the lane this long.")]
        [SerializeField, Min(0f)] private float riderForgetSeconds = 1f;
        [Header("Hitting the rider (test values)")]
        [Tooltip("Total rider launch speed cap, including upward motion. 11 is half the previous full-speed head-on cap of 22.")]
        [SerializeField, Min(1f)] private float maximumLaunchSpeed = 11f;
        [Tooltip("The front is treated as a bonnet sloping up by this angle, so the bounce itself throws the rider upward.")]
        [SerializeField, Range(0f, 60f)] private float bonnetAngle = 30f;
        [Tooltip("Normal-direction restitution before the launch speed cap: 0 follows the car, 1 gives a fully elastic bounce.")]
        [SerializeField, Range(0f, 1f)] private float restitution = 0.6f;
        [Tooltip("Share of the rider's own motion along the car's front (mostly sideways) kept through the hit.")]
        [SerializeField, Range(0f, 1f)] private float sideSpeedKeep = 0.5f;
        [Tooltip("Shortest time between two relaunches of the rider by this kind of hit.")]
        [SerializeField, Min(0.05f)] private float relaunchInterval = 0.3f;
        [SerializeField] private LayerMask obstacleMask = ~0;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Renderer brakeLights;
        private const int ObstacleScanInterval = 3;
        internal const float JunctionStopMargin = 0.3f;
        private const float JunctionEntryClearance = 1f;
        private struct JunctionClearance
        {
            public int junction;
            public float remaining;
        }
        private readonly Collider[] hits = new Collider[16];
        private readonly List<JunctionClearance> clearingJunctions = new List<JunctionClearance>(2);
        private TrafficNetwork network;
        private Rigidbody body;
        private BoxCollider shape;
        private System.Random random;
        private float speed;
        private float contactHoldUntil;
        private int heldJunction = -1;
        private int scanFrame;
        private float cachedObstacle = float.PositiveInfinity;
        private float riderDetectedAt = -1f;
        private float riderLastSeenAt = float.NegativeInfinity;
        private bool ignoringRider;

        public int PathIndex { get; private set; } = -1;
        public int NextIndex { get; private set; } = -1;
        public float Distance { get; private set; }
        private float length = -1f;
        public float Length => length > 0f ? length : length = GetComponent<BoxCollider>().size.z;

        public void Paint(Color color)
        {
            if (bodyRenderer == null) return;
            var block = new MaterialPropertyBlock();
            bodyRenderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            bodyRenderer.SetPropertyBlock(block);
        }

        public void Initialize(TrafficNetwork owner, int path, float distance, int randomSeed)
        {
            network = owner;
            riderDetectedAt = -1f;
            random = new System.Random(randomSeed);
            body = GetComponent<Rigidbody>();
            shape = GetComponent<BoxCollider>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            PathIndex = path;
            Distance = distance;
            NextIndex = network.ChooseNext(path, random);
            network.Enter(this, path);
            network.Sample(path, distance, out var position, out var forward);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
            speed = cruiseSpeed * 0.5f;
        }

        private void FixedUpdate()
        {
            if (network == null || PathIndex < 0) return;
            var session = network.Session;
            if (session != null && (session.IsPaused || session.IsSettled)) { speed = 0f; return; }
            float step = Time.fixedDeltaTime;
            var path = network.Path(PathIndex);
            float half = Length * 0.5f;
            float stopping = speed * speed / (2f * braking);
            float lookAhead = Mathf.Max(8f, stopping + followGap + 6f);

            // Car ahead along the planned paths.
            float available = network.GapAhead(this, lookAhead) - followGap;

            // Junction ahead: reserve it and make sure the exit lane has room, otherwise stop at the lane end.
            if (!path.IsConnector && NextIndex >= 0)
            {
                var connector = network.Path(NextIndex);
                float toStopLine = path.length - Distance - half - JunctionStopMargin;
                if (connector.IsConnector && toStopLine < stopping + 4f && heldJunction != connector.junction)
                {
                    int exitLane = connector.next.Length > 0 ? connector.next[0] : -1;
                    bool room = exitLane < 0 || network.HasEntryRoom(exitLane, this, followGap, JunctionEntryClearance);
                    if (room && network.TryEnterJunction(connector.junction, this, NextIndex)) heldJunction = connector.junction;
                    else
                    {
                        // Waiting for exit room must not hold a place in the junction queue.
                        if (!room) network.LeaveJunction(connector.junction, this);
                        available = Mathf.Min(available, toStopLine);
                    }
                }
            }

            // Only the rider in the lane stops the car; props and other people are driven through (other cars are handled above).
            if (++scanFrame >= ObstacleScanInterval || speed < 0.5f)
            {
                scanFrame = 0;
                cachedObstacle = RiderDistance(Mathf.Min(14f, stopping + obstacleClearance + 4f));
            }
            available = Mathf.Min(available, cachedObstacle - obstacleClearance);

            network.Sample(PathIndex, Distance, out _, out var forward);
            float desired = Mathf.Min(cruiseSpeed, path.speedLimit);
            if (Mathf.Abs(forward.y) > 0.03f) desired = Mathf.Min(desired, slopeSpeed);
            desired = Mathf.Min(desired, Mathf.Sqrt(2f * braking * Mathf.Max(0f, available)));
            if (Time.time < contactHoldUntil) desired = 0f;
            speed = Mathf.MoveTowards(speed, desired, (desired < speed ? braking : acceleration) * step);
            float travel = Mathf.Clamp(Mathf.Min(speed * step, available), 0f, speed * step);
            if (travel < speed * step) speed = travel / step;
            Advance(travel);

            network.Sample(PathIndex, Distance, out var position, out forward);
            body.MovePosition(position);
            body.MoveRotation(Quaternion.LookRotation(forward, Vector3.up));
            if (brakeLights != null) brakeLights.enabled = desired < cruiseSpeed * 0.7f;
        }

        private void Advance(float travel)
        {
            ClearJunctionsAfterTravel(travel);
            Distance += travel;
            int guard = 0;
            while (Distance > network.Path(PathIndex).length && guard++ < 4)
            {
                if (NextIndex < 0) { Distance = network.Path(PathIndex).length; return; }
                Distance -= network.Path(PathIndex).length;
                bool leavingConnector = network.Path(PathIndex).IsConnector;
                network.Leave(this, PathIndex);
                PathIndex = NextIndex;
                network.Enter(this, PathIndex);
                NextIndex = network.ChooseNext(PathIndex, random);
                if (leavingConnector && heldJunction >= 0)
                {
                    // Distance includes this step's overshoot beyond the connector's end.
                    float remaining = Length * 0.5f + JunctionStopMargin - Distance;
                    if (remaining > 0f)
                        clearingJunctions.Add(new JunctionClearance { junction = heldJunction, remaining = remaining });
                    else network.LeaveJunction(heldJunction, this);
                    heldJunction = -1;
                }
            }
        }

        private void ClearJunctionsAfterTravel(float travel)
        {
            for (int i = clearingJunctions.Count - 1; i >= 0; i--)
            {
                var clearance = clearingJunctions[i];
                clearance.remaining -= travel;
                if (clearance.remaining <= 0f)
                {
                    network.LeaveJunction(clearance.junction, this);
                    clearingJunctions.RemoveAt(i);
                }
                else clearingJunctions[i] = clearance;
            }
        }

        // Steps along the planned lane ahead and returns the distance to the rider, once the brake delay has passed.
        private float RiderDistance(float scan)
        {
            var half = new Vector3(shape.size.x * 0.5f + 0.1f, shape.size.y * 0.5f, 0.6f);
            float riderDistance = float.PositiveInfinity;
            for (float d = 1f; d <= scan && float.IsPositiveInfinity(riderDistance); d += 1f)
            {
                float along = Distance + Length * 0.5f + d;
                int path = PathIndex;
                if (along > network.Path(path).length && NextIndex >= 0) { along -= network.Path(path).length; path = NextIndex; }
                network.Sample(path, along, out var p, out var forward);
                var rotation = Quaternion.LookRotation(forward, Vector3.up);
                int count = Physics.OverlapBoxNonAlloc(p + Vector3.up * shape.center.y, half, hits, rotation, obstacleMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                    if (IsRider(hits[i].attachedRigidbody)) { riderDistance = d; break; }
            }
            if (float.IsPositiveInfinity(riderDistance)) { riderDetectedAt = -1f; return riderDistance; }
            bool ignore = IgnoresRider();
            if (riderDetectedAt < 0f) riderDetectedAt = Time.time;
            if (ignore) return float.PositiveInfinity;
            bool mapOpen = network.Session != null && network.Session.IsMapOpen;
            return mapOpen || Time.time - riderDetectedAt >= riderBrakeDelay ? riderDistance : float.PositiveInfinity;
        }

        private static bool IsRider(Rigidbody other) => other != null && other.TryGetComponent<RiderMotor>(out _);

        // One roll per encounter; the decision holds until the rider has left the lane for riderForgetSeconds.
        // While the map is open the car always brakes, since the rider cannot move.
        private bool IgnoresRider()
        {
            if (Time.time - riderLastSeenAt > riderForgetSeconds) ignoringRider = random.NextDouble() < riderIgnoreChance;
            riderLastSeenAt = Time.time;
            return ignoringRider && !(network.Session != null && network.Session.IsMapOpen);
        }

        // Bumping props or other people never stops the car; rider contact does unless this car ignores the rider.
        private void OnCollisionEnter(Collision collision)
        {
            if (!IsRider(collision.rigidbody)) return;
            var rider = collision.rigidbody.GetComponent<RiderMotor>();
            // Every car throws the rider by the same bounce; a car that does not stop may throw again on any contact.
            if (IgnoresRider())
            {
                if (TryBounce(rider, collision, speed, out var again)) rider.TryTrafficRelaunch(again, 1.1f, relaunchInterval);
                return;
            }
            if (TryBounce(rider, collision, speed, out var launch)) rider.TryTrafficCrash(launch, 1.1f);
            riderDetectedAt = Time.time - riderBrakeDelay;
            speed = 0f;
            contactHoldUntil = Time.time + 0.5f;
        }

        private void OnCollisionStay(Collision collision)
        {
            if (!IsRider(collision.rigidbody)) return;
            if (IgnoresRider())
            {
                var rider = collision.rigidbody.GetComponent<RiderMotor>();
                if (TryBounce(rider, collision, speed, out var launch)) rider.TryTrafficRelaunch(launch, 1.1f, relaunchInterval);
                return;
            }
            speed = 0f;
            contactHoldUntil = Time.time + 0.2f;
        }

        // The rider bounces off the car like off a much heavier body whose front is a bonnet sloping up by
        // bonnetAngle: along that sloped normal it leaves at (1 + restitution) times the car's speed plus
        // restitution times any speed it ran in with, so the hit throws it up as well as away; sideSpeedKeep of
        // its motion along the front is kept. Motion away from the car (being pushed) does not soften the
        // bounce, so a pushed rider is thrown clear.
        private bool TryBounce(RiderMotor rider, Collision collision, float carSpeed, out Vector3 launch)
        {
            launch = Vector3.zero;
            var toRider = Vector3.ProjectOnPlane(rider.transform.position - body.position, Vector3.up);
            var flat = collision.contactCount > 0 ? Vector3.ProjectOnPlane(collision.GetContact(0).normal, Vector3.up) : toRider;
            if (flat.sqrMagnitude < 0.0001f) flat = toRider;
            flat.Normalize();
            if (Vector3.Dot(flat, toRider) < 0f) flat = -flat;
            var carVelocity = transform.forward * carSpeed;
            // A side scrape, or a car crawling, does not throw the rider.
            if (Vector3.Dot(carVelocity, flat) < 1f) return false;
            float slope = bonnetAngle * Mathf.Deg2Rad;
            var normal = flat * Mathf.Cos(slope) + Vector3.up * Mathf.Sin(slope);
            float carAlong = Vector3.Dot(carVelocity, normal);
            var riderVelocity = Vector3.ProjectOnPlane(rider.ContactVelocity, Vector3.up);
            float riderAlong = Vector3.Dot(riderVelocity, normal);
            float approach = Mathf.Min(riderAlong, 0f);
            var alongFront = (riderVelocity - normal * riderAlong) * sideSpeedKeep;
            launch = Vector3.ClampMagnitude(alongFront + normal * ((1f + restitution) * carAlong - restitution * approach), maximumLaunchSpeed);
            return true;
        }

        private void OnDisable()
        {
            if (network == null) return;
            network.Leave(this, PathIndex);
            if (heldJunction >= 0) network.LeaveJunction(heldJunction, this);
            foreach (var clearance in clearingJunctions) network.LeaveJunction(clearance.junction, this);
            clearingJunctions.Clear();
            heldJunction = -1;
        }
    }
}
