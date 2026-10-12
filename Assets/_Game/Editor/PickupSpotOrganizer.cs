using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    public static class PickupSpotOrganizer
    {
        [MenuItem("Delivery Rider/Organize Pickup Spots")]
        public static void OrganizeCurrentScene() => Organize(SceneManager.GetActiveScene());

        public static void Organize(Scene scene)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            string[] pickupNames = { "RestaurantPickup", "RestaurantPickup_Lunch", "RestaurantPickup_Bakery" };
            string[] markerNames = { "PickupMarker", "PickupMarker_1", "PickupMarker_2" };
            var pickups = pickupNames.Select(name => OrderContentInstaller.FindSceneObject(scene, name)).ToArray();
            var markers = markerNames.Select(name => OrderContentInstaller.FindSceneObject(scene, name)).ToArray();
            if (pickups.Any(p => p == null) || markers.Any(m => m == null))
                throw new InvalidOperationException("All three pickup points and markers are required.");
            var labels = pickups.Select(p =>
            {
                var label = p.GetComponentInChildren<WorldLabel>(true);
                if (label == null && p.transform.parent != null && p.transform.parent.name.StartsWith("Spot_"))
                    label = p.transform.parent.GetComponentInChildren<WorldLabel>(true);
                return label;
            }).ToArray();
            if (labels.Any(label => label == null)) throw new InvalidOperationException("Pickup labels are required.");

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Organize Pickup Spots");
            var root = OrderContentInstaller.FindSceneObject(scene, "Pick_Up_Spot");
            if (root == null) root = CreateContainer(scene, "Pick_Up_Spot", null, Vector3.zero);
            for (int i = 0; i < pickups.Length; i++)
            {
                var spot = root.transform.Find("Spot_" + (i + 1));
                if (spot == null) spot = CreateContainer(scene, "Spot_" + (i + 1), root.transform, pickups[i].transform.position).transform;
                Undo.SetTransformParent(pickups[i].transform, spot, "Group Pickup Point");
                Undo.SetTransformParent(labels[i].transform, spot, "Group Pickup Label");
                Undo.SetTransformParent(markers[i].transform, spot, "Group Pickup Marker");
                if (i > 0)
                {
                    var counter = OrderContentInstaller.FindSceneObject(scene, "ShopCounter_" + i);
                    if (counter != null) Undo.SetTransformParent(counter.transform, spot, "Group Shop Counter");
                }
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static GameObject CreateContainer(Scene scene, string name, Transform parent, Vector3 position)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetParent(parent, true);
            go.transform.position = position;
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            return go;
        }
    }
}
