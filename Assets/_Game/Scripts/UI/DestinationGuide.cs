using TMPro;
using UnityEngine;

namespace DeliveryRider
{
    public sealed class DestinationGuide : MonoBehaviour
    {
        [SerializeField] private DeliverySession session;
        [SerializeField] private Camera view;
        [SerializeField] private RectTransform indicator;
        [SerializeField] private RectTransform arrow;
        [SerializeField] private TMP_Text distanceLabel;
        [SerializeField] private Transform beacon;
        [SerializeField] private LineRenderer ring;
        [SerializeField] private TMP_Text beaconLabel;
        private int previousMeters = -1;
        private DeliveryStage previousStage;
        private bool previousChasing;

        private void LateUpdate()
        {
            var target = session.Target;
            bool visible = target != null;
            beacon.gameObject.SetActive(visible);
            indicator.gameObject.SetActive(visible && !session.IsMapOpen && !session.IsPaused);
            if (!visible) return;
            beacon.position = new Vector3(target.position.x, 0.08f, target.position.z);
            var point = view.WorldToViewportPoint(target.position + Vector3.up * 2f);
            var direction = new Vector2(point.x - 0.5f, point.y - 0.5f);
            if (point.z < 0) direction = -direction;
            bool offscreen = point.z < 0 || point.x < 0.06f || point.x > 0.94f || point.y < 0.2f || point.y > 0.79f;
            if (offscreen)
            {
                if (direction.sqrMagnitude < 0.0001f) direction = Vector2.down;
                float scaleX = Mathf.Abs(direction.x) < 0.0001f ? float.MaxValue : 0.44f / Mathf.Abs(direction.x);
                float scaleY = Mathf.Abs(direction.y) < 0.0001f ? float.MaxValue : (direction.y > 0 ? 0.29f : 0.3f) / Mathf.Abs(direction.y);
                direction *= Mathf.Min(scaleX, scaleY);
                point.x = 0.5f + direction.x; point.y = 0.5f + direction.y;
            }
            indicator.anchorMin = indicator.anchorMax = new Vector2(point.x, point.y);
            arrow.localRotation = offscreen ? Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f) : Quaternion.identity;
            int meters = Mathf.CeilToInt(session.TargetDistance);
            if (meters != previousMeters || previousStage != session.Stage || previousChasing != session.IsChasingFood)
            {
                bool pickup = session.Stage == DeliveryStage.Pickup || (session.IsRecoveringFood && !session.IsChasingFood);
                var color = session.IsChasingFood ? new Color32(211, 132, 255, 255) : pickup ? new Color32(255, 191, 64, 255) : new Color32(40, 197, 177, 255);
                distanceLabel.text = (session.IsChasingFood ? "도둑" : session.IsRecoveringFood ? "재수령" : pickup ? "수령" : "전달") + $" {meters}m";
                ring.startColor = ring.endColor = color;
                beaconLabel.color = color;
                beaconLabel.text = session.IsChasingFood ? "부딪혀서 음식 회수" : session.IsRecoveringFood ? "여기서 음식 다시 받기" : pickup ? "여기서 음식 수령" : "이 고객에게 전달";
                previousMeters = meters; previousStage = session.Stage;
                previousChasing = session.IsChasingFood;
            }
        }
    }
}
