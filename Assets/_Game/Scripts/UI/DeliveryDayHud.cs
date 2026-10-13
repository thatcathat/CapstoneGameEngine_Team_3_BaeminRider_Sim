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
        [Tooltip("Sundial arc filled by the elapsed share of the day. The day length itself is never shown as a number.")]
        [SerializeField] private UnityEngine.UI.Image sundialFill;
        [SerializeField] private RectTransform sundialSun;
        [SerializeField, Min(1f)] private float sundialRadius = 46f;
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
        [Header("Run economy")]
        [SerializeField] private TMP_Text heading;
        [SerializeField] private TMP_Text ledger;
        [SerializeField] private UnityEngine.UI.Button buyInline;
        [SerializeField] private UnityEngine.UI.Button repay;
        private const int ResultsPerPage = 5;
        private static readonly string[] DayPhaseNames = { "아침", "점심", "오후", "저녁 · 곧 마감", "출발 전", "영업 종료 / 마지막 배달", "오늘 영업 종료" };
        private int page;
        private int lastDayPhase = -1;
        private int lastOrderSeconds = -1;
        private bool lastLate;

        private void Awake()
        {
            previousPage.onClick.AddListener(() => ChangePage(-1));
            nextPage.onClick.AddListener(() => ChangePage(1));

            // 정산을 마치는 두 버튼 모두 세션이 다음 날을 저장한 뒤 씬을 전환한다.
            backToTitle.onClick.AddListener(session.SaveAndLeaveToTitle);
            replay.onClick.AddListener(session.ContinueToNextDay);
            if (buyInline != null) buyInline.onClick.AddListener(() => session.TryBuyInline());
            if (repay != null) repay.onClick.AddListener(() => session.TryRepayDebt());
        }
        private void OnEnable() { session.Changed += Refresh; Refresh(); }
        private void OnDisable() => session.Changed -= Refresh;
        private void Update() => RefreshClocks();

        private void Refresh()
        {
            lastDayPhase = lastOrderSeconds = -1;
            settlementPanel.SetActive(session.IsSettled);
            var run = session.Progress;
            cashLabel.text = session.IsSettled ? $"{run.Day}일차 · 보유 {run.Cash:N0}원 · 빚 {run.Debt:N0}원"
                : $"{run.Day}일차{(run.HasInline ? " · 인라인" : "")} · 보유 {run.Cash + session.Cash:N0}원 · 빚 {run.Debt:N0}원";
            RefreshClocks();
            if (session.IsSettled) RefreshSettlement();
        }

        private void RefreshClocks()
        {
            float progress = (float)session.DayProgress;
            if (sundialFill != null) sundialFill.fillAmount = progress;
            if (sundialSun != null)
            {
                // The sun travels from the left (morning) to the right (evening) across the upper half circle.
                float angle = Mathf.PI * (1f - progress);
                sundialSun.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * sundialRadius;
            }
            int phase = session.IsSettled ? 6 : session.DayPhase == WorkDayPhase.Overtime ? 5 : !session.HasDayStarted ? 4 : Mathf.Min(3, (int)(progress * 4f));
            if (phase != lastDayPhase)
            {
                dayClock.text = DayPhaseNames[phase];
                dayClock.color = phase == 3 || phase == 5 ? new Color32(255, 195, 79, 255) : Color.white;
                if (sundialFill != null) sundialFill.color = phase == 3 || phase == 5 ? new Color32(255, 160, 72, 255) : new Color32(255, 214, 102, 255);
                lastDayPhase = phase;
            }
            bool late = session.IsOrderLate;
            bool waiting = session.ActiveOrder != null && !session.HasPickedUp;
            int order = session.ActiveOrder == null ? -2 : (int)Math.Ceiling(late ? session.OrderOverdueSeconds : session.OrderRemainingSeconds);
            if (order != lastOrderSeconds || late != lastLate)
            {
                orderClock.text = session.ActiveOrder == null ? "주문 대기" : waiting ? "수령 전 · 제한 " + FormatTime(order) : (late ? "지각 +" : "주문 ") + FormatTime(order);
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
            var run = session.Progress;
            if (run.IsEnded) { ShowEnding(run); return; }
            if (heading != null) heading.text = $"{run.Day}일차 배달 정산";
            RefreshLedger(run);
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
        private void RefreshLedger(RunProgress run)
        {
            var economy = session.Economy;
            bool hasEconomy = economy != null;
            if (ledger != null)
            {
                var text = new StringBuilder();
                text.Append($"오늘 수익  +{session.Cash:N0}원\n");
                text.Append($"생활비  -{run.LivingCostPaid:N0}원\n");
                if (run.DebtAddedToday > 0) text.Append($"<color=#FF8A7B>생활비 부족분 빚으로  +{run.DebtAddedToday:N0}원</color>\n");
                text.Append($"\n보유 현금  {run.Cash:N0}원\n남은 빚  {run.Debt:N0}원");
                ledger.text = text.ToString();
            }
            if (buyInline != null)
            {
                buyInline.gameObject.SetActive(hasEconomy);
                if (hasEconomy)
                {
                    buyInline.interactable = session.CanBuyInline;
                    SetLabel(buyInline, run.HasInline ? "인라인 보유 중" : run.Day < economy.InlineSaleDay
                        ? $"인라인은 {economy.InlineSaleDay}일차부터 판매" : $"인라인 구매  {economy.InlinePrice:N0}원  (다음 날부터)");
                }
            }
            if (repay != null)
            {
                repay.gameObject.SetActive(hasEconomy && run.Debt > 0);
                repay.interactable = run.CanRepay;
                SetLabel(repay, run.CanRepay ? $"빚 {run.Debt:N0}원 한 번에 갚기" : $"빚 갚기  ({run.Debt - run.Cash:N0}원 부족)");
            }
            replay.gameObject.SetActive(true);
            SetLabel(replay, $"다음 날  ({run.Day + 1}일차)");
            SetLabel(backToTitle, "저장하고 타이틀로");
        }

        private void ShowEnding(RunProgress run)
        {
            if (heading != null) heading.text = "빚을 모두 갚았습니다!";
            summary.text = $"{run.Day}일 동안 배달해 빚을 청산했습니다.";
            deliveries.text = $"누적 배달 {run.TotalDeliveries}건\n남은 현금 {run.Cash:N0}원\n\n수고하셨습니다.";
            if (ledger != null) ledger.text = "";
            if (buyInline != null) buyInline.gameObject.SetActive(false);
            if (repay != null) repay.gameObject.SetActive(false);
            replay.gameObject.SetActive(false);
            previousPage.gameObject.SetActive(false);
            nextPage.gameObject.SetActive(false);
            pageLabel.text = "";
            SetLabel(backToTitle, "타이틀로");
        }

        private static void SetLabel(UnityEngine.UI.Button button, string text)
        {
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null && label.text != text) label.text = text;
        }

        private static string FormatTime(int seconds) => $"{Mathf.Max(0, seconds) / 60:00}:{Mathf.Max(0, seconds) % 60:00}";
    }
}
