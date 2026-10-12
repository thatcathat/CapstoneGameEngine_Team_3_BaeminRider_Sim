using System;
using System.Collections;
using UnityEngine;

namespace DeliveryRider
{
    public sealed class IncidentDirector : MonoBehaviour
    {
        [SerializeField] private DeliverySession session;
        [SerializeField] private RiderMotor rider;
        private bool cancelling;
        private Coroutine routine;
        private IEnumerator activeExecution;
        private IncidentActor activeActor;
        private FoodThief spawnedThief;
        public bool IsActive { get; private set; }
        public string Notice { get; private set; } = "";
        public RiderMotor Rider => rider;
        public DeliverySession Session => session;
        public event Action Changed;
        public bool CanRequest => isActiveAndEnabled && !cancelling && !session.IsPaused && !session.IsSettled;

        private void OnEnable() => session.Changed += OnSessionChanged;
        private void OnDisable()
        {
            session.Changed -= OnSessionChanged;
            Cancel();
        }
        private void OnSessionChanged()
        {
            if (!cancelling && session.IsSettled) Cancel();
        }
        public bool IsEligible(IncidentDefinition definition)
        {
            return definition != null && definition.CanStart(session);
        }
        public bool TryStart(IncidentDefinition definition, IncidentTrigger source)
        {
            if (!CanRequest || !IsEligible(definition) || source == null) return false;
            if (IsActive) Cancel();
            IsActive = true;
            var startedRoutine = StartCoroutine(Run(definition, source));
            if (IsActive) routine = startedRoutine;
            return true;
        }
        private IEnumerator Run(IncidentDefinition definition, IncidentTrigger source)
        {
            try
            {
                // The director creates objects; the spawned actor starts their behavior.
                var spawn = source.ActorSpawn;
                activeActor = Instantiate(definition.ActorPrefab, spawn.position, spawn.rotation, transform);
                if (definition is TheftIncidentDefinition theft &&
                    theft.RecoveryMode == TheftRecoveryMode.ChaseThenReplacement)
                {
                    if (theft.ThiefPrefab == null || !source.HasEscapeRoute)
                        throw new InvalidOperationException("Theft incident requires a thief prefab and escape route.");
                    spawnedThief = Instantiate(theft.ThiefPrefab, source.ThiefSpawn.position, source.ThiefSpawn.rotation, activeActor.transform);
                    ((TheftIncidentActor)activeActor).SetThief(spawnedThief);
                }
                activeExecution = activeActor.Execute(this, source, definition);
                while (activeActor != null && activeActor.isActiveAndEnabled && activeExecution.MoveNext())
                    yield return activeExecution.Current;
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
            if (spawnedThief != null)
            {
                if (session != null && session.IsRecoveringFood) session.RequireReplacement(spawnedThief.transform);
                spawnedThief.gameObject.SetActive(false);
                Destroy(spawnedThief.gameObject);
                spawnedThief = null;
            }
            if (activeActor != null)
            {
                activeActor.gameObject.SetActive(false);
                Destroy(activeActor.gameObject);
                activeActor = null;
            }
            IsActive = false;
            SetNotice("");
        }
    }
}
