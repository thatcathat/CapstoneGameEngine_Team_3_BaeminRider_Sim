using UnityEngine;
using UnityEngine.Rendering;

namespace DeliveryRider
{
    // Curved-world look: the rendered world drops away with the squared horizontal distance from the pivot (the rider).
    // Rendering only; physics, AI and gameplay stay flat. Materials using DeliveryRider/Curved Lit bend in the shader;
    // things drawn with other shaders (TextMeshPro, the destination ring) are placed with Bend/Tilt instead.
    // Active in Play Mode only, so the Scene view stays flat while editing.
    [RequireComponent(typeof(Camera))]
    public sealed class CurvedWorld : MonoBehaviour
    {
        private static readonly int PivotId = Shader.PropertyToID("_CurvedWorldPivot");
        private static readonly int CurvatureId = Shader.PropertyToID("_CurvedWorldCurvature");
        private static CurvedWorld active;

        [Tooltip("Centre of the bend. Usually the rider; the camera itself when empty.")]
        [SerializeField] private Transform pivot;
        [Tooltip("Drop per squared metre of horizontal distance (test value). 0.001 lowers the world 2.5 m at 50 m and 10 m at 100 m; 0 turns the look off.")]
        [SerializeField, Range(0f, 0.005f)] private float curvature = 0.001f;
        [Tooltip("Extra field of view for culling. The bend pulls distant objects toward the screen centre, so objects just outside the normal view would pop in late.")]
        [SerializeField, Range(0f, 30f)] private float cullingFovMargin = 10f;
        private Camera lens;

        // Same bend as CurvedWorld.hlsl, for objects placed or projected by hand.
        public static Vector3 Bend(Vector3 position)
        {
            if (active == null) return position;
            var center = active.Center;
            float x = position.x - center.x, z = position.z - center.z;
            position.y -= (x * x + z * z) * active.curvature;
            return position;
        }

        // Rotation that lays a flat object (rotation identity) along the bent ground at this position.
        public static Quaternion Tilt(Vector3 position)
        {
            if (active == null) return Quaternion.identity;
            var center = active.Center;
            float slope = 2f * active.curvature;
            return Quaternion.FromToRotation(Vector3.up, new Vector3(slope * (position.x - center.x), 1f, slope * (position.z - center.z)));
        }

        private Vector3 Center => pivot != null ? pivot.position : transform.position;

        private void Awake() => lens = GetComponent<Camera>();
        private void OnEnable()
        {
            active = this;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }
        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            if (active == this) active = null;
            // Shader globals outlive Play Mode and the scene; leave the world flat behind.
            Shader.SetGlobalFloat(CurvatureId, 0f);
            lens.ResetCullingMatrix();
        }

        // After every LateUpdate, so the camera and the pivot have reached their final pose for this frame.
        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != lens) return;
            Shader.SetGlobalVector(PivotId, Center);
            Shader.SetGlobalFloat(CurvatureId, curvature);
            lens.cullingMatrix = Matrix4x4.Perspective(Mathf.Min(lens.fieldOfView + cullingFovMargin, 170f), lens.aspect,
                lens.nearClipPlane, lens.farClipPlane) * lens.worldToCameraMatrix;
        }
    }
}
