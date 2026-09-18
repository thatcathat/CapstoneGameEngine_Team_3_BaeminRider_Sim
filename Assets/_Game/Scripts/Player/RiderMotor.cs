using UnityEngine;
using UnityEngine.InputSystem;

namespace DeliveryRider
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RiderMotor : MonoBehaviour
    {
        [SerializeField] private Transform view;
        [SerializeField] private DeliverySession session;
        [SerializeField] private float speed = 5f;
        [SerializeField] private float acceleration = 22f;
        [SerializeField] private float turnSpeed = 12f;
        [SerializeField] private float recoveryHeight = -8f;
        private Rigidbody body;
        private InputActionMap gameplay;
        private InputAction move;
        private InputAction look;
        private InputAction interact;
        private InputAction pause;
        private InputAction map;
        private Vector2 movement;
        private Vector3 spawn;
        public Vector2 Look => session.CanMove ? look.ReadValue<Vector2>() : Vector2.zero;
        public float Speed => speed;
        public bool IsRecovering { get; private set; }

        public void BeginCrash(Vector3 impulse)
        {
            IsRecovering = true;
            movement = Vector2.zero;
            body.linearVelocity = Vector3.zero;
            body.AddForce(impulse, ForceMode.VelocityChange);
        }

        public void EndCrash()
        {
            if (!IsRecovering) return;
            IsRecovering = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            spawn = transform.position;
            gameplay = new InputActionMap("Gameplay");
            move = gameplay.AddAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            look = gameplay.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            interact = gameplay.AddAction("Interact", InputActionType.Button, "<Keyboard>/e");
            pause = gameplay.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            map = gameplay.AddAction("Map", InputActionType.Button, "<Keyboard>/m");
        }

        private void OnEnable() => gameplay.Enable();
        private void OnDisable() { gameplay.Disable(); movement = Vector2.zero; }
        private void OnDestroy() => gameplay.Dispose();

        private void Update()
        {
            if (pause.WasPressedThisFrame()) session.HandleEscape();
            else if (map.WasPressedThisFrame()) session.ToggleMap();
            movement = session.CanMove ? Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f) : Vector2.zero;
            if (session.CanMove && interact.WasPressedThisFrame()) session.TryPickup();
        }

        private void FixedUpdate()
        {
            // Map input restrictions must not cancel the physical accident impulse.
            if (IsRecovering && !session.IsSettled)
            {
                if (body.position.y < recoveryHeight) { body.position = spawn; EndCrash(); }
                return;
            }
            var forward = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(view.right, Vector3.up).normalized;
            var direction = session.CanMove ? forward * movement.y + right * movement.x : Vector3.zero;
            var current = body.linearVelocity;
            var horizontal = session.CanMove ? Vector3.MoveTowards(new Vector3(current.x, 0f, current.z), direction * speed, acceleration * Time.fixedDeltaTime) : Vector3.zero;
            body.linearVelocity = new Vector3(horizontal.x, current.y, horizontal.z);
            if (direction.sqrMagnitude > 0.01f)
                body.MoveRotation(Quaternion.Slerp(body.rotation, Quaternion.LookRotation(direction), turnSpeed * Time.fixedDeltaTime));
            if (body.position.y < recoveryHeight)
            {
                body.position = spawn;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }
    }
}
