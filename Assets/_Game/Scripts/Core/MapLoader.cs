using UnityEngine;

namespace DeliveryRider
{
    [DefaultExecutionOrder(-100)]
    public sealed class MapLoader : MonoBehaviour
    {
        [SerializeField] private GameObject mapPrefab;

        private void Awake()
        {
            if (mapPrefab == null)
            {
                Debug.LogError("MapLoader requires a map prefab.", this);
                return;
            }

            var map = Instantiate(mapPrefab, transform);
            map.name = mapPrefab.name;
        }
    }
}
