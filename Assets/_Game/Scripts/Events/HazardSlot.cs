using System;
using UnityEngine;

namespace DeliveryRider
{
    // Objects are always placed for the day and strike any time (even before pickup).
    // Triggers are switched on per call and strike only after the food has been picked up.
    public enum HazardRole { Object, Trigger }

    // Placement data for one hazard on the map; HazardPlanner turns it on and off.
    [DisallowMultipleComponent]
    public sealed class HazardSlot : MonoBehaviour
    {
        [SerializeField] private string type;
        [SerializeField] private HazardRole role;
        [SerializeField, Min(1)] private int unlockDay = 1;
        [Tooltip("Ranked objects appear when rank < the day's placement share (trial build OBJ_DAY). 0 means always placed.")]
        [SerializeField, Range(0f, 1f)] private float rank;
        [SerializeField] private bool ranked;
        [SerializeField] private string zone;
        [SerializeField] private bool alley;
        [Tooltip("Call routes (shop index x door count + door index) that pass within 7 m.")]
        [SerializeField] private int[] routes = Array.Empty<int>();

        public string Type => type;
        public HazardRole Role => role;
        public int UnlockDay => unlockDay;
        public float Rank => rank;
        public bool Ranked => ranked;
        public string Zone => zone;
        public bool Alley => alley;
        // Set by the planner; hazards check it before starting an attack.
        public bool StrikeAllowed { get; set; } = true;

        public bool IsOnRoute(int route) => route >= 0 && Array.IndexOf(routes, route) >= 0;

        public void Configure(string slotType, HazardRole slotRole, int slotUnlockDay, bool isRanked, float slotRank,
            string slotZone, bool isAlley, int[] slotRoutes)
        {
            type = slotType;
            role = slotRole;
            unlockDay = slotUnlockDay;
            ranked = isRanked;
            rank = slotRank;
            zone = slotZone;
            alley = isAlley;
            routes = slotRoutes ?? Array.Empty<int>();
        }
    }
}
