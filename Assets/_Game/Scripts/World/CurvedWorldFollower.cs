using UnityEngine;

namespace DeliveryRider
{
    // Keeps an object the curved-world shader cannot bend (TextMeshPro text) on the bent world: each frame it moves to
    // the bent position of its original spot under its parent. Runs after the scripts that move or turn its parents
    // in LateUpdate (WorldLabel, NPC and camera follow).
    [DefaultExecutionOrder(1000)]
    public sealed class CurvedWorldFollower : MonoBehaviour
    {
        private Vector3 restPosition;

        private void Awake() => restPosition = transform.localPosition;
        private void LateUpdate()
        {
            var parent = transform.parent;
            transform.position = CurvedWorld.Bend(parent != null ? parent.TransformPoint(restPosition) : restPosition);
        }
    }
}
