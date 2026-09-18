using UnityEngine;

namespace DeliveryRider
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class FoodThief : MonoBehaviour
    {
        private Rigidbody body;
        private DeliverySession session;
        private Vector3[] route;
        private int waypoint;
        private float speed;
        private double expiresAt;
        public bool Finished { get; private set; }
        private void Awake() => body = GetComponent<Rigidbody>();
        public void Initialize(DeliverySession owner, Transform[] points, float moveSpeed, float lifetime)
        {
            session = owner;
            speed = Mathf.Max(0.1f, moveSpeed);
            expiresAt = Time.timeAsDouble + Mathf.Max(1f, lifetime);
            route = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++) route[i] = points[i].position;
        }
        private void Update()
        {
            if (Finished || session == null) return;
            if (session.IsSettled || !session.IsChasingFood || session.Target != transform ||
                Time.timeAsDouble >= expiresAt || transform.position.y < -3f ||
                (transform.position - session.Player.position).sqrMagnitude > 3600f) Escape();
        }
        private void FixedUpdate()
        {
            if (Finished || session == null || route == null || session.IsPaused || session.IsSettled) return;
            if (waypoint >= route.Length) { Escape(); return; }
            var direction = Vector3.ProjectOnPlane(route[waypoint] - body.position, Vector3.up);
            if (direction.sqrMagnitude < 0.36f)
            {
                waypoint++;
                if (waypoint >= route.Length) Escape();
                return;
            }
            direction.Normalize();
            body.linearVelocity = new Vector3(direction.x * speed, body.linearVelocity.y, direction.z * speed);
            body.MoveRotation(Quaternion.LookRotation(direction));
        }
        private void OnCollisionEnter(Collision collision) => TryCatch(collision);
        private void OnCollisionStay(Collision collision) => TryCatch(collision);
        private void TryCatch(Collision collision)
        {
            if (Finished || session == null || collision.rigidbody == null) return;
            if (collision.rigidbody.GetComponent<RiderMotor>() == null) return;
            if (!session.TryRecoverFood(transform)) return;
            Finished = true;
            gameObject.SetActive(false);
        }
        private void Escape()
        {
            if (Finished) return;
            Finished = true;
            if (session != null) session.RequireReplacement(transform);
            gameObject.SetActive(false);
        }
        private void OnDisable()
        {
            Finished = true;
            if (session != null) session.RequireReplacement(transform);
        }
    }
}
