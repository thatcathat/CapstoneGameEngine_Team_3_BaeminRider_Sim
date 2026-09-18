using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DeliveryRider
{
    public sealed class IncidentDirector : MonoBehaviour
    {
        [SerializeField] private DeliverySession session;
        [SerializeField] private RiderMotor rider;
        [SerializeField, Min(0f)] private float protectionSeconds = 8f;
        [SerializeField, Min(1)] private int maxIncidentsPerDay = 8;
        private readonly Dictionary<Type, int> kindCounts = new Dictionary<Type, int>();
        private readonly Dictionary<Type, double> kindAvailableAt = new Dictionary<Type, double>();
        private int startedCount;
        private bool cancelling;
        private Coroutine routine;
        private IEnumerator activeExecution;
        private IncidentDefinition activeDefinition;
        private double availableAt;
        public bool IsActive { get; private set; }
        public string Notice { get; private set; } = "";
        public RiderMotor Rider => rider;
        public DeliverySession Session => session;
        public event Action Changed;
        public bool CanRequest => isActiveAndEnabled && !IsActive && !session.IsRecoveringFood &&
            session.CanMove && startedCount < maxIncidentsPerDay && Time.timeAsDouble >= availableAt;

        private void OnEnable() => session.Changed += OnSessionChanged;
        private void OnDisable()
        {
            session.Changed -= OnSessionChanged;
            Cancel();
        }
        private void OnSessionChanged()
        {
            if (!cancelling && (session.IsSettled || (IsActive && !activeDefinition.CanContinue(session)))) Cancel();
        }
        public bool IsEligible(IncidentDefinition definition)
        {
            if (definition == null || !definition.CanStart(session)) return false;
            var kind = definition.GetType();
            return (!kindCounts.TryGetValue(kind, out int count) || count < definition.KindMaxPerDay) &&
                (!kindAvailableAt.TryGetValue(kind, out double next) || Time.timeAsDouble >= next);
        }
        public bool TryStart(IncidentDefinition definition, IncidentTrigger source)
        {
            if (!CanRequest || !IsEligible(definition) || source == null) return false;
            IsActive = true;
            activeDefinition = definition;
            startedCount++;
            var kind = definition.GetType();
            kindCounts.TryGetValue(kind, out int count);
            kindCounts[kind] = count + 1;
            routine = StartCoroutine(Run(definition, source));
            return true;
        }
        private IEnumerator Run(IncidentDefinition definition, IncidentTrigger source)
        {
            // Drive the event enumerator here so failures always release player control.
            activeExecution = definition.Execute(this, source);
            try
            {
                while (activeExecution.MoveNext()) yield return activeExecution.Current;
            }
            finally
            {
                DisposeExecution();
                Finish();
            }
        }
        private void DisposeExecution()
        {
            var execution = activeExecution;
            activeExecution = null;
            (execution as IDisposable)?.Dispose();
        }
        public void SetNotice(string value) { Notice = value; Changed?.Invoke(); }
        private void Cancel()
        {
            if (cancelling) return;
            cancelling = true;
            if (routine != null) StopCoroutine(routine);
            DisposeExecution();
            Finish();
            cancelling = false;
        }
        private void Finish()
        {
            routine = null;
            if (rider != null) rider.EndCrash();
            if (IsActive)
            {
                availableAt = Time.timeAsDouble + protectionSeconds;
                kindAvailableAt[activeDefinition.GetType()] = Time.timeAsDouble + activeDefinition.KindCooldownSeconds;
            }
            IsActive = false;
            activeDefinition = null;
            SetNotice("");
        }
    }
}
