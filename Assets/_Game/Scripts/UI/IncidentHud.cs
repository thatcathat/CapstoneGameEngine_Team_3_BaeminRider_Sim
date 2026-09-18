using TMPro;
using UnityEngine;

namespace DeliveryRider
{
    public sealed class IncidentHud : MonoBehaviour
    {
        [SerializeField] private IncidentDirector director;
        [SerializeField] private DeliverySession session;
        [SerializeField] private TMP_Text label;
        private void OnEnable() { director.Changed += Refresh; session.Changed += Refresh; Refresh(); }
        private void OnDisable() { director.Changed -= Refresh; session.Changed -= Refresh; }
        private void Refresh()
        {
            label.text = director.Notice;
            label.gameObject.SetActive(!session.IsPaused && !session.IsSettled && !string.IsNullOrEmpty(director.Notice));
        }
    }
}
