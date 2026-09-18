using System;
using System.Text;
using TMPro;
using UnityEngine;

namespace DeliveryRider
{
    public sealed class DeliveryDayHud : MonoBehaviour
    {
        [SerializeField] private DeliverySession session;
        [SerializeField] private TMP_Text dayClock;
        [SerializeField] private TMP_Text orderClock;
        [SerializeField] private TMP_Text cashLabel;
        [SerializeField] private GameObject settlementPanel;
        [SerializeField] private TMP_Text summary;
        [SerializeField] private TMP_Text deliveries;
        [SerializeField] private TMP_Text pageLabel;
        [SerializeField] private UnityEngine.UI.Button previousPage;
        [SerializeField] private UnityEngine.UI.Button nextPage;
        [SerializeField] private UnityEngine.UI.Button backToTitle;
        [SerializeField] private UnityEngine.UI.Button replay;
        private const int ResultsPerPage = 5;
        private int page;
        private int lastDaySeconds = -1;
        private int lastOrderSeconds = -1;
        private bool lastLate;

        private void Awake()
        {
            previousPage.onClick.AddListener(() => ChangePage(-1));
            nextPage.onClick.AddListener(() => ChangePage(1));
            backToTitle.onClick.AddListener(session.BackToTitle);
            replay.onClick.AddListener(session.ReplayDay);
        }
        private void OnEnable() { session.Changed += Refresh; Refresh(); }
        private void OnDisable() => session.Changed -= Refresh;
        private void Update() => RefreshClocks();

        private void Refresh()
        {
            lastDaySeconds = lastOrderSeconds = -1;
            settlementPanel.SetActive(session.IsSettled);
            cashLabel.text = $"보유 {session.Cash:N0}원";
            RefreshClocks();
            if (session.IsSettled) RefreshSettlement();
        }

        private void RefreshClocks()
        {
            int day = (int)Math.Ceiling(session.DayRemainingSeconds);
            if (day != lastDaySeconds)
            {
                dayClock.text = session.IsSettled ? "오늘 영업 종료" : session.DayPhase == WorkDayPhase.Overtime
                    ? "영업 종료 / 마지막 배달" : "하루 " + FormatTime(day);
                dayClock.color = day <= 30 ? new Color32(255, 195, 79, 255) : Color.white;
                lastDaySeconds = day;
            }
            bool late = session.IsOrderLate;
            int order = session.ActiveOrder == null ? -2 : (int)Math.Ceiling(late ? session.OrderOverdueSeconds : session.OrderRemainingSeconds);
            if (order != lastOrderSeconds || late != lastLate)
            {
                orderClock.text = session.ActiveOrder == null ? "주문 대기" : (late ? "지각 +" : "주문 ") + FormatTime(order);
                orderClock.color = late ? new Color32(255, 137, 123, 255) : new Color32(91, 229, 205, 255);
                lastOrderSeconds = order; lastLate = late;
            }
        }

        private void ChangePage(int direction)
        {
            page = Mathf.Clamp(page + direction, 0, Mathf.Max(0, (session.Results.Count - 1) / ResultsPerPage));
            RefreshSettlement();
        }
        private void RefreshSettlement()
        {
            summary.text = $"총 {session.CompletedCount}건  ·  정시 {session.OnTimeCount}건  ·  지각 {session.LateCount}건\n"
                + $"오늘 수익 {session.Cash:N0}원  /  활동 {FormatTime((int)Math.Ceiling(session.SettlementElapsedSeconds))}";
            int pages = Mathf.Max(1, (session.Results.Count + ResultsPerPage - 1) / ResultsPerPage);
            page = Mathf.Clamp(page, 0, pages - 1);
            var text = new StringBuilder();
            int start = page * ResultsPerPage;
            int end = Mathf.Min(start + ResultsPerPage, session.Results.Count);
            for (int i = start; i < end; i++)
            {
                var result = session.Results[i];
                text.Append($"#{result.Number:000}  {result.Restaurant} → {result.Customer}\n");
                text.Append($"{(result.WasLate ? "지각" : "정시")}  ·  소요 {FormatTime((int)Math.Ceiling(result.DurationSeconds))}  ·  +{result.Reward:N0}원\n");
                if (i < end - 1) text.Append('\n');
            }
            deliveries.text = session.Results.Count == 0 ? "오늘 완료한 배달이 없습니다.\n다음 도전에서는 주문을 수락해 보세요." : text.ToString();
            pageLabel.text = $"{page + 1} / {pages}";
            previousPage.interactable = page > 0;
            nextPage.interactable = page + 1 < pages;
        }
        private static string FormatTime(int seconds) => $"{Mathf.Max(0, seconds) / 60:00}:{Mathf.Max(0, seconds) % 60:00}";
    }
}
