using UnityEngine;
using UnityEngine.UI;

namespace DeliveryRider
{
    // Day-clock sundial drawn by the UI Sundial shader. DeliveryDayHud passes the day progress and phase; the view
    // eases into the after-hours dusk and works on a runtime copy of the material, so Play Mode never edits the asset.
    [RequireComponent(typeof(Image))]
    public sealed class SundialView : MonoBehaviour
    {
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int DuskId = Shader.PropertyToID("_Dusk");
        private static readonly int WarnId = Shader.PropertyToID("_Warn");
        private static readonly int SizeId = Shader.PropertyToID("_Size");

        [Tooltip("Seconds to fade into or out of the after-hours sky.")]
        [SerializeField, Min(0.01f)] private float duskSeconds = 2f;
        private Image image;
        private Material material;
        private float progress, dusk, targetDusk, warn;

        public void Show(float dayProgress, bool afterHours, bool closingSoon)
        {
            progress = Mathf.Clamp01(dayProgress);
            targetDusk = afterHours ? 1f : 0f;
            warn = closingSoon ? 1f : 0f;
        }

        private void Awake()
        {
            image = GetComponent<Image>();
            material = new Material(image.material) { hideFlags = HideFlags.HideAndDontSave };
            image.material = material;
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }

        // Unscaled, so the dusk still settles while the settlement screen stops time.
        private void LateUpdate()
        {
            dusk = Mathf.MoveTowards(dusk, targetDusk, Time.unscaledDeltaTime / duskSeconds);
            material.SetFloat(ProgressId, progress);
            material.SetFloat(DuskId, dusk);
            material.SetFloat(WarnId, warn);
            material.SetVector(SizeId, ((RectTransform)transform).rect.size);
        }
    }
}
