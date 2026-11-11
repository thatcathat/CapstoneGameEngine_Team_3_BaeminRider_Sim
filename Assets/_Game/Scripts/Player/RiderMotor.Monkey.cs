using UnityEngine;

namespace DeliveryRider
{
    public sealed partial class RiderMotor
    {
        private Object monkeyOwner;
        private Vector3 monkeyDirection;
        private float monkeyUntil;
        public bool IsMonkeyCarried => monkeyOwner != null && Time.time < monkeyUntil && ReactionState == RiderReactionState.Normal;

        public bool TryBeginMonkeyCarry(Object source, Vector3 direction, float seconds)
        {
            if (source == null || !isActiveAndEnabled || session == null || !session.CanMove || IsMonkeyCarried
                || ReactionState != RiderReactionState.Normal) return false;
            monkeyOwner = source;
            monkeyUntil = Time.time + Mathf.Max(0.1f, seconds);
            monkeyDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            jumpQueued = false;
            movement = airMovement = Vector2.zero;
            return true;
        }

        public bool SteerMonkeyCarry(Object source, Vector3 direction)
        {
            if (source != monkeyOwner || !IsMonkeyCarried) return false;
            monkeyDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            return true;
        }

        public void EndMonkeyCarry(Object source)
        {
            if (source == monkeyOwner) ClearMonkeyCarry();
        }

        private void ClearMonkeyCarry()
        {
            monkeyOwner = null;
            monkeyUntil = 0f;
            monkeyDirection = Vector3.zero;
            // Do not touch velocity: an accident may already have supplied its launch impulse.
        }
    }
}
