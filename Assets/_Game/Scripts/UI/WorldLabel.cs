using UnityEngine;

namespace DeliveryRider
{
    public sealed class WorldLabel : MonoBehaviour
    {
        private Camera view;
        private void Start() => view = Camera.main;
        private void LateUpdate()
        {
            if (view != null) transform.rotation = view.transform.rotation;
        }
    }
}
