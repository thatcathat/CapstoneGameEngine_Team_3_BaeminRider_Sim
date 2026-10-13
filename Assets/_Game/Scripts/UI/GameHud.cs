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

        private void Awake()
        {
            accept.onClick.AddListener(session.AcceptOrder);
            resume.onClick.AddListener(session.TogglePause);

            // 기존 코드: title.onClick.AddListener(session.BackToTitle);
            // 변경된 코드: 일시정지(시간 정지)를 풀고 SceneController를 통해 타이틀 씬으로 이동
            title.onClick.AddListener(() =>
            {
                Time.timeScale = 1f; // 일시정지 상태에서 나갈 경우를 대비해 시간 흐름 정상화
                SceneController.Instance.NextScene(ESceneType.Title);
            });

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
            string orderTitle = session.ActiveOrder == null ? null : $"진행 중 주문 #{session.ActiveOrder.Number:000}";
            switch (session.Stage)
            {
                case DeliveryStage.Available:
                    objective.text = session.HasDayStarted ? "1  콜 확인" : "집을 나서면 하루가 시작됩니다   ·   1  콜 확인";
                    cardTitle.text = "지도   ·   1  콜 확인";
                    break;
                case DeliveryStage.Pickup:
                    objective.text = $"01  {session.ActiveOrder.RestaurantName}에서 음식 받기";
                    cardTitle.text = orderTitle; break;
                case DeliveryStage.Carrying:
                    objective.text = $"02  {session.ActiveOrder.CustomerName}을(를) 주먹으로 쳐서 전달";
                    cardTitle.text = orderTitle; break;
                case DeliveryStage.Complete:
                    objective.text = "배달 완료!   ·   1  다음 콜 확인";
                    cardTitle.text = session.LastResult == null ? "배달 완료!" :
                        $"배달 완료 · {(session.LastResult.WasLate ? "지각" : "정시")} +{session.LastResult.Reward:N0}원";
                    break;
                case DeliveryStage.FoodLost:
                    objective.text = session.IsChasingFood ? "도둑을 주먹으로 쳐서 음식을 회수하세요" : $"{session.ActiveOrder.RestaurantName}에서 음식 다시 받기";
                    cardTitle.text = session.IsChasingFood ? "음식 도난 · 도둑 추격" : "음식 분실 · 음식점 재수령";
                    break;
            }
            if (session.IsCallOpen) cardTitle.text = "콜 확인   ·   1 / 2  수락";
            if (session.DayPhase == WorkDayPhase.Overtime) cardTitle.text = "영업 종료 · 현재 배달을 마무리하세요";
            if (session.IsSettled) objective.text = "오늘의 영업이 종료되었습니다";
            RefreshCandidates();
            distance.text = session.ActiveOrder != null ? "목적지로 이동하세요" : $"오늘 완료 {session.CompletedCount}건";
            lastDistance = -1;
            UpdatePrompt();
        }

        private void RefreshCandidates()
        {
            for (int i = 0; i < candidateButtons.Length; i++)
            {
                bool exists = session.IsCallOpen && i < session.Candidates.Count;
                candidateButtons[i].gameObject.SetActive(exists);
                if (!exists) continue;
                var offer = session.Candidates[i];
                bool selected = offer == session.SelectedOrder;
                candidateLabels[i].text = $"{i + 1}  #{offer.Number:000}";
                candidateButtons[i].interactable = session.CanChooseOrder;
                candidateButtons[i].GetComponent<UnityEngine.UI.Image>().color = selected
                    ? new Color32(40, 197, 177, 255) : new Color32(221, 232, 224, 255);
            }
            accept.gameObject.SetActive(session.IsCallOpen);
            accept.interactable = session.CanChooseOrder && session.SelectedOrder != null;
            var selectedOrder = session.ActiveOrder ?? (session.IsCallOpen ? session.SelectedOrder : null);
            if (selectedOrder == null)
            {
                cardBody.text = session.CanOpenCall ? "1 키를 누르면 콜 화면이 열립니다.\n지도를 보는 동안에도 시간은 흐릅니다." : "지도를 보는 동안에도 시간은 흐릅니다.";
                actionLabel.text = "주문 없음";
                return;
            }
            var data = selectedOrder;
            string clock = session.ActiveOrder == null || !session.HasPickedUp ? "음식을 받는 순간 시작" : "진행 중";
            string from = data.PickupZone == null ? data.RestaurantName : $"{data.RestaurantName}  ({ZoneLabel(data.PickupZone)})";
            string to = data.DestinationZone == null ? data.CustomerName : $"{data.CustomerName}  ({ZoneLabel(data.DestinationZone)})";
            string distanceLine = data.Distance > 0f ? $"큰길 기준 {data.Distance:0}m · {data.TierLabel}\n" : "";
            cardBody.text = $"주문 #{selectedOrder.Number:000}  ·  {data.FoodName}\n"
                + $"수령  {from}\n전달  {to}\n{distanceLine}\n"
                + $"정시 {data.OnTimeReward:N0}원 / 지각 {data.LateReward:N0}원\n"
                + $"제한 시간 {data.TimeLimitSeconds:0}초 ({clock})\n<size=14>늦어도 배달 가능 · 콜 화면에서는 시간이 멈춤</size>";
            actionLabel.text = session.ActiveOrder == null ? $"#{selectedOrder.Number:000} 주문 수락" : "배달 진행 중 · 추가 수락 불가";
        }

        private string ZoneLabel(string zone)
            => session.RouteTable != null ? $"{zone} {session.RouteTable.ZoneName(zone)}" : zone;

        private void Update()
        {
            if (session.ActiveOrder == null || session.IsPaused) return;
            int meters = Mathf.CeilToInt(session.TargetDistance);
            if (meters != lastDistance) { distance.text = $"목적지까지  {meters} m"; lastDistance = meters; }
        }

        private void UpdatePrompt()
        {
            string common = "M  지도   ·   Esc  일시정지";
            prompt.text = session.IsSettled ? "오늘의 배달 결과를 확인하세요"
                : session.IsPaused ? "Esc  계속하기"
                : session.IsCallOpen ? "1 / 2  콜 수락   ·   클릭  자세히 보기   ·   Esc  닫기"
                : session.IsMapOpen ? "M / Esc  지도 닫기   ·   지도를 보는 동안에도 시간이 흐릅니다"
                : session.IsChasingFood ? $"F / 클릭  음식을 가져간 도둑을 쳐서 회수   ·   {common}"
                : session.IsRecoveringFood ? $"음식점에 가면 자동으로 다시 받습니다   ·   {common}"
                : session.Stage == DeliveryStage.Carrying ? $"F / 클릭  {session.ActiveOrder.CustomerName} 쳐서 전달   ·   Space  점프   ·   {common}"
                : session.Stage == DeliveryStage.Pickup ? $"음식점에 가면 자동으로 수령합니다   ·   {common}"
                : session.CanOpenCall ? $"1  콜 확인   ·   {common}"
                : $"W A S D  이동   ·   F  주먹   ·   Space  점프   ·   {common}";
        }
    }
}
