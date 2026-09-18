using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryRider
{
    public enum DeliveryStage { Available, Pickup, Carrying, Complete, FoodLost }
    public enum WorkDayPhase { Working, Overtime, Settlement }

    public sealed class DeliverySession : MonoBehaviour
    {
        [SerializeField] private Transform player;
        [SerializeField] private DeliveryOrderRoute[] orderRoutes = Array.Empty<DeliveryOrderRoute>();
        [SerializeField, Range(1, 3)] private int candidateCount = 3;
        [SerializeField] private GameObject foodBag;
        [SerializeField] private float pickupRange = 2.8f;
        [SerializeField] private DeliveryDaySettings daySettings;
        public event Action Changed;
        public DeliveryStage Stage { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsMapOpen { get; private set; } = true;
        public Transform Player => player;
        public int CompletedCount { get; private set; }
        public WorkDayPhase DayPhase { get; private set; }
        public long Cash { get; private set; }
        public int OnTimeCount { get; private set; }
        public int LateCount => CompletedCount - OnTimeCount;
        public DeliveryResult LastResult { get; private set; }
        public double SettlementElapsedSeconds { get; private set; }
        private readonly List<DeliveryResult> results = new List<DeliveryResult>();
        public IReadOnlyList<DeliveryResult> Results => results;
        private double dayStartedAt;
        private double dayDuration;
        private double orderAcceptedTime;
        private double orderDeadline;
        private int activeOnTimeReward;
        private int activeLateReward;
        private bool leavingScene;
        public bool IsSettled => DayPhase == WorkDayPhase.Settlement;
        public bool IsDayClosed => DayPhase != WorkDayPhase.Working || Time.timeAsDouble >= dayStartedAt + dayDuration;
        public double DayRemainingSeconds => Math.Max(0d, dayStartedAt + dayDuration - Time.timeAsDouble);
        public double OrderRemainingSeconds => ActiveOrder == null ? 0d : Math.Max(0d, orderDeadline - Time.timeAsDouble);
        public double OrderOverdueSeconds => ActiveOrder == null ? 0d : Math.Max(0d, Time.timeAsDouble - orderDeadline);
        public bool IsOrderLate => ActiveOrder != null && Time.timeAsDouble > orderDeadline;
        private readonly List<DeliveryOrderOffer> candidates = new List<DeliveryOrderOffer>();
        private int nextRoute;
        private int nextOrderNumber = 1;
        public IReadOnlyList<DeliveryOrderOffer> Candidates => candidates;
        public DeliveryOrderOffer SelectedOrder { get; private set; }
        public DeliveryOrderOffer ActiveOrder { get; private set; }
        public DeliveryOrderOffer LastCompletedOrder { get; private set; }
        public float AcceptedAt { get; private set; }
        public bool CanChooseOrder => !IsPaused && !IsDayClosed && ActiveOrder == null && (Stage == DeliveryStage.Available || Stage == DeliveryStage.Complete);
        private RiderMotor rider;
        private Transform recoveryTarget;
        private bool awaitingThief;
        public bool IsRecoveringFood => Stage == DeliveryStage.FoodLost;
        public bool IsChasingFood => IsRecoveringFood && recoveryTarget != null;
        public bool CanMove => !IsPaused && !IsMapOpen && !IsSettled && (rider == null || !rider.IsRecovering);
        public Transform Target => ActiveOrder == null ? null : IsChasingFood ? recoveryTarget :
            Stage == DeliveryStage.Pickup || IsRecoveringFood ? ActiveOrder.Route.PickupPoint : ActiveOrder.Route.Customer.transform;
        public float TargetDistance => Target == null ? 0f : Vector3.Distance(player.position, Target.position);
        public bool CanPickup => (Stage == DeliveryStage.Pickup || (IsRecoveringFood && !IsChasingFood)) && CanMove && ActiveOrder != null
            && Vector3.Distance(player.position, ActiveOrder.Route.PickupPoint.position) <= pickupRange;

        private void Awake()
        {
            rider = player.GetComponent<RiderMotor>();
            dayStartedAt = Time.timeAsDouble;
            dayDuration = daySettings != null ? daySettings.DurationSeconds : 300d;
            FillCandidates(); SelectedOrder = candidates.Count > 0 ? candidates[0] : null;
        }
        private void Start() { Time.timeScale = 1f; Notify(); }
        private void Update()
        {
            RefreshDayPhase();
            if (IsRecoveringFood && awaitingThief && (recoveryTarget == null || !recoveryTarget.gameObject.activeInHierarchy))
                RequireReplacement(recoveryTarget);
        }

        public bool TryLoseFood(Transform thief)
        {
            RefreshDayPhase();
            if (!CanMove || ActiveOrder == null || Stage != DeliveryStage.Carrying) return false;
            recoveryTarget = thief;
            awaitingThief = thief != null;
            Stage = DeliveryStage.FoodLost;
            Notify();
            return true;
        }

        public bool TryRecoverFood(Transform thief)
        {
            if (!CanMove || !IsChasingFood || recoveryTarget != thief) return false;
            recoveryTarget = null;
            awaitingThief = false;
            Stage = DeliveryStage.Carrying;
            Notify();
            return true;
        }

        public void RequireReplacement(Transform thief)
        {
            if (!IsRecoveringFood || recoveryTarget != thief) return;
            recoveryTarget = null;
            awaitingThief = false;
            Notify();
        }

        // Called by both Update and entry points, preventing frame-order races at closing time.
        private void RefreshDayPhase()
        {
            if (DayPhase != WorkDayPhase.Working || !IsDayClosed) return;
            candidates.Clear();
            SelectedOrder = null;
            if (ActiveOrder != null) DayPhase = WorkDayPhase.Overtime;
            else EnterSettlement();
            Notify();
        }

        private void EnterSettlement()
        {
            if (IsSettled) return;
            DayPhase = WorkDayPhase.Settlement;
            SettlementElapsedSeconds = Time.timeAsDouble - dayStartedAt;
            IsPaused = false;
            IsMapOpen = false;
            candidates.Clear();
            SelectedOrder = null;
            Time.timeScale = 0f;
        }

        public void SelectCandidate(int index)
        {
            RefreshDayPhase();
            if (!CanChooseOrder || index < 0 || index >= candidates.Count) return;
            SelectedOrder = candidates[index];
            Notify();
        }

        public void AcceptOrder() => TryAcceptOrder(SelectedOrder);

        public bool TryAcceptOrder(DeliveryOrderOffer offer)
        {
            RefreshDayPhase();
            if (!CanChooseOrder || offer == null || !offer.Route.IsValid || !candidates.Contains(offer)) return false;
            ActiveOrder = offer;
            candidates.Remove(offer);
            SelectedOrder = null;
            AcceptedAt = Time.time;
            orderAcceptedTime = Time.timeAsDouble;
            orderDeadline = orderAcceptedTime + Math.Max(1d, offer.Definition.TimeLimitSeconds);
            activeOnTimeReward = Mathf.Max(0, offer.Definition.OnTimeReward);
            activeLateReward = Mathf.Max(0, offer.Definition.LateReward);
            ActiveOrder.Route.Customer.ReturnHome();
            Stage = DeliveryStage.Pickup;
            IsMapOpen = false;
            Notify();
            return true;
        }

        public bool TryPickup()
        {
            RefreshDayPhase();
            if (!CanPickup) return false;
            recoveryTarget = null;
            awaitingThief = false;
            Stage = DeliveryStage.Carrying;
            Notify();
            return true;
        }

        public bool TryDeliver(DeliveryResident recipient)
        {
            RefreshDayPhase();
            if (!CanMove || Stage != DeliveryStage.Carrying || ActiveOrder == null || recipient != ActiveOrder.Route.Customer) return false;
            bool late = IsOrderLate;
            int reward = late ? activeLateReward : activeOnTimeReward;
            var result = new DeliveryResult(ActiveOrder, Time.timeAsDouble - orderAcceptedTime, late, reward);
            LastCompletedOrder = ActiveOrder;
            ActiveOrder = null;
            Stage = DeliveryStage.Complete;
            CompletedCount++;
            if (!late) OnTimeCount++;
            LastResult = result;
            results.Add(result);
            Cash += reward;
            if (IsDayClosed) EnterSettlement();
            else
            {
                FillCandidates();
                SelectedOrder = candidates.Count > 0 ? candidates[0] : null;
                IsMapOpen = true;
            }
            Notify();
            return true;
        }

        private void FillCandidates()
        {
            if (IsDayClosed || orderRoutes == null || orderRoutes.Length == 0) return;
            // Round-robin offers make route replacement visible and avoid duplicates in the current list.
            int attempts = 0;
            while (candidates.Count < candidateCount && attempts < orderRoutes.Length)
            {
                var route = orderRoutes[nextRoute];
                nextRoute = (nextRoute + 1) % orderRoutes.Length;
                attempts++;
                if (route == null || !route.IsValid) continue;
                if (ActiveOrder != null && ActiveOrder.Definition == route.Definition) continue;
                if (candidates.Exists(offer => offer.Definition == route.Definition)) continue;
                candidates.Add(new DeliveryOrderOffer(nextOrderNumber++, route));
            }
        }

        public void TogglePause()
        {
            RefreshDayPhase();
            if (IsSettled) return;
            IsPaused = !IsPaused;
            Time.timeScale = IsPaused ? 0f : 1f;
            Notify();
        }

        public void ToggleMap()
        {
            RefreshDayPhase();
            if (IsPaused || IsSettled) return;
            IsMapOpen = !IsMapOpen;
            Notify();
        }

        public void HandleEscape()
        {
            if (!IsPaused && IsMapOpen) ToggleMap();
            else TogglePause();
        }

        public void BackToTitle()
        {
            if (leavingScene) return;
            leavingScene = true;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadSceneAsync("TitleScene");
        }

        public void ReplayDay()
        {
            if (!IsSettled || leavingScene) return;
            leavingScene = true;
            Time.timeScale = 1f;
            SceneManager.LoadSceneAsync("GameScene");
        }

        private void Notify()
        {
            foodBag.SetActive(Stage == DeliveryStage.Carrying);
            Cursor.lockState = CanMove ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !CanMove;
            Changed?.Invoke();
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
