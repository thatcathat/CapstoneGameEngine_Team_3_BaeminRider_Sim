using System.Collections.Generic;
using UnityEngine;

namespace DeliveryRider
{
    // One vehicle reserves the crossing; queued traffic waits outside the crossing.
    public sealed class TrafficJunction : MonoBehaviour
    {
        [SerializeField] private Vector2 halfSize = new Vector2(6, 6);
        private readonly List<TrafficVehicle> queue = new List<TrafficVehicle>();
        private TrafficVehicle owner;

        public bool Contains(Vector3 position, float margin = 0)
        {
            var p = transform.InverseTransformPoint(position);
            return Mathf.Abs(p.x) <= halfSize.x + margin && Mathf.Abs(p.z) <= halfSize.y + margin;
        }

        public bool Request(TrafficVehicle vehicle)
        {
            for (int i = queue.Count - 1; i >= 0; i--)
                if (queue[i] == null || !queue[i].isActiveAndEnabled) queue.RemoveAt(i);
            if (owner != null && !owner.isActiveAndEnabled) owner = null;
            if (owner == vehicle) return true;
            if (!queue.Contains(vehicle)) queue.Add(vehicle);
            if (owner == null && queue.Count > 0 && queue[0] == vehicle)
            {
                owner = vehicle;
                queue.RemoveAt(0);
                return true;
            }
            return false;
        }

        public void Release(TrafficVehicle vehicle)
        {
            queue.Remove(vehicle);
            if (owner == vehicle) owner = null;
        }

        private void OnDisable() { owner = null; queue.Clear(); }
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(halfSize.x * 2, 0.2f, halfSize.y * 2));
        }
    }
}
