using TMPro;
using UnityEngine;

namespace DeliveryRider
{
    public sealed class GameHud : MonoBehaviour
    {
        [SerializeField] private DeliverySession session;
        [SerializeField] private TMP_Text objective;
        [SerializeField] private TMP_Text distance;
        [SerializeField] private TMP_Text prompt;
        [SerializeField] private TMP_Text cardTitle;
        [SerializeField] private TMP_Text cardBody;
        [SerializeField] private TMP_Text actionLabel;
        [SerializeField] private GameObject orderCard;
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private UnityEngine.UI.Button accept;
        [SerializeField] private UnityEngine.UI.Button resume;
        [SerializeField] private UnityEngine.UI.Button title;
        [SerializeField] private UnityEngine.UI.Button[] candidateButtons = System.Array.Empty<UnityEngine.UI.Button>();
        private TMP_Text[] candidateLabels;
        private int lastDistance = -1;
        private bool nearPickup;

        private void Awake()
        {
            accept.onClick.AddListener(session.AcceptOrder);
            resume.onClick.AddListener(session.TogglePause);
            title.onClick.AddListener(session.BackToTitle);
            candidateLabels = new TMP_Text[candidateButtons.Length];
            for (int i = 0; i < candidateButtons.Length; i++)
            {
                int slot = i;
                candidateLabels[i] = candidateButtons[i].GetComponentInChildren<TMP_Text>(true);
                candidateButtons[i].onClick.AddListener(() => session.SelectCandidate(slot));
            }
        }
        private void OnEnable() { session.Changed += Refresh; Refresh(); }
        private void OnDisable() => session.Changed -= Refresh;

        private void Refresh()
        {
            pausePanel.SetActive(session.IsPaused);
            orderCard.SetActive(!session.IsPaused && session.IsMapOpen);
            switch (session.Stage)
            {
                case DeliveryStage.Available:
                    objective.text = "주문 후보 중 한 건을 선택하세요";
                    cardTitle.text = "어떤 주문을 배달할까요?";
                    break;
                case DeliveryStage.Pickup:
                    objective.text = $"01  {session.ActiveOrder.Definition.RestaurantName}에서 음식 받기";
                    cardTitle.text = $"진행 중 주문 #{session.ActiveOrder.Number:000}"; break;
                case DeliveryStage.Carrying:
                    objective.text = $"02  {session.ActiveOrder.Definition.CustomerName}에게 전달하기";
                    cardTitle.text = $"진행 중 주문 #{session.ActiveOrder.Number:000}"; break;
                case DeliveryStage.Complete:
                    objective.text = "배달 완료!";
                    cardTitle.text = session.LastResult == null ? "배달 완료!" :
                        $"배달 완료 · {(session.LastResult.WasLate ? "지각" : "정시")} +{session.LastResult.Reward:N0}원";
                    break;
                case DeliveryStage.FoodLost:
                    objective.text = session.IsChasingFood ? "도둑에게 부딪혀 음식을 회수하세요" : $"{session.ActiveOrder.Definition.RestaurantName}에서 음식 다시 받기";
                    cardTitle.text = session.IsChasingFood ? "음식 도난 · 도둑 추격" : "음식 분실 · 음식점 재수령";
                    break;
            }
            if (session.DayPhase == WorkDayPhase.Overtime) cardTitle.text = "영업 종료 · 현재 배달을 마무리하세요";
            if (session.IsSettled) objective.text = "오늘의 영업이 종료되었습니다";
            RefreshCandidates();
            distance.text = session.ActiveOrder != null ? "목적지로 이동하세요" : $"주문 후보 {session.Candidates.Count}개 · 완료 {session.CompletedCount}건";
            lastDistance = -1;
            UpdatePrompt();
        }

        private void RefreshCandidates()
        {
            for (int i = 0; i < candidateButtons.Length; i++)
            {
                bool exists = i < session.Candidates.Count;
                candidateButtons[i].gameObject.SetActive(exists);
                if (!exists) continue;
                var offer = session.Candidates[i];
                bool selected = offer == session.SelectedOrder;
                candidateLabels[i].text = $"#{offer.Number:000}";
                candidateButtons[i].interactable = session.CanChooseOrder;
                candidateButtons[i].GetComponent<UnityEngine.UI.Image>().color = selected
                    ? new Color32(40, 197, 177, 255) : new Color32(221, 232, 224, 255);
            }
            accept.interactable = session.CanChooseOrder && session.SelectedOrder != null;
            var selectedOrder = session.ActiveOrder ?? session.SelectedOrder;
            if (selectedOrder == null)
            {
                cardBody.text = "선택할 주문이 없습니다.";
                actionLabel.text = "주문 없음";
                return;
            }
            var data = selectedOrder.Definition;
            cardBody.text = $"주문 #{selectedOrder.Number:000}  ·  {data.FoodName}\n"
                + $"수령  {data.RestaurantName}\n전달  {data.CustomerName}\n\n"
                + $"정시 {data.OnTimeReward:N0}원 / 지각 {data.LateReward:N0}원\n"
                + $"제한 시간 {data.TimeLimitSeconds:0}초\n<size=14>수락 즉시 시간 시작 · 늦어도 배달 가능</size>";
            actionLabel.text = session.ActiveOrder == null ? $"#{selectedOrder.Number:000} 주문 수락" : "배달 진행 중 · 추가 수락 불가";
        }

        private void Update()
        {
            if (session.ActiveOrder == null || session.IsPaused) return;
            int meters = Mathf.CeilToInt(session.TargetDistance);
            if (meters != lastDistance) { distance.text = $"목적지까지  {meters} m"; lastDistance = meters; }
            if (nearPickup != session.CanPickup) UpdatePrompt();
        }

        private void UpdatePrompt()
        {
            nearPickup = session.CanPickup;
            prompt.text = session.IsSettled ? "오늘의 배달 결과를 확인하세요" : session.IsPaused ? "Esc  계속하기" : session.IsMapOpen ? (session.ActiveOrder != null ? "현재 목적지 확인   ·   M / Esc  지도 닫기" : "고객 프로필 선택   ·   M / Esc  지도 닫기")
                : session.IsChasingFood ? "보라색 도둑에게 부딪혀 회수   ·   M  지도   ·   Esc  일시정지"
                : session.IsRecoveringFood ? (nearPickup ? "E  음식 다시 받기   ·   M  지도" : "음식점으로 돌아가세요   ·   M  지도   ·   Esc  일시정지")
                : session.Stage == DeliveryStage.Carrying ? $"{session.ActiveOrder.Definition.CustomerName}에게 전달   ·   M  지도   ·   Esc  일시정지"
                : nearPickup ? "E  음식 수령   ·   M  지도" : "W A S D  이동   ·   마우스  시점   ·   M  지도   ·   Esc  일시정지";
        }
    }
}
