using System.Collections.Generic;
using UnityEngine;

namespace DeliveryRider
{
    // Near soft clouds (DaySkySettings): a few clouds drift with the wind within a radius around the rider. Each one
    // forms (its puffs swell up one by one), lives a while and breaks up again, then forms somewhere else; past the
    // radius it breaks up early. On the curved world distant clouds sink toward the horizon (by a share of the drop),
    // so each cloud's render bounds also cover its bent spot; otherwise clouds bending down into view from above would
    // be culled. The billow, rim and occlusion
    // amounts go to Cloud.shader as globals, and each cloud billows with its own seed.
    public sealed class CloudField : MonoBehaviour
    {
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int SwellId = Shader.PropertyToID("_NearCloudSwell");
        private static readonly int LookId = Shader.PropertyToID("_NearCloudLook");
        private static readonly int CurveId = Shader.PropertyToID("_NearCloudCurve");

        [SerializeField] private DaySkySettings settings;
        [Tooltip("Clouds gather around this (the rider).")]
        [SerializeField] private Transform center;
        [SerializeField] private Mesh[] shapes = System.Array.Empty<Mesh>();
        [SerializeField] private Material material;

        private sealed class Cloud
        {
            public Transform transform;
            public MeshRenderer renderer;
            public float age, lifetime, fade, size, seed;
            public bool leaving;
        }

        private readonly List<Cloud> clouds = new List<Cloud>();
        private MaterialPropertyBlock block;
        private float swellPhase;

        private void Start()
        {
            if (settings == null || center == null || material == null || shapes.Length == 0) { enabled = false; return; }
            block = new MaterialPropertyBlock();
            // The day starts with clouds already formed, at different points of their lives.
            for (int i = 0; i < settings.NearCloudCount; i++)
            {
                var cloud = Add();
                Form(cloud);
                cloud.age = Random.value * cloud.lifetime * 0.6f;
                cloud.fade = 1f;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            swellPhase += settings.NearCloudSwellSpeed * dt;
            Shader.SetGlobalVector(SwellId, new Vector4(settings.NearCloudSwell, swellPhase, settings.NearCloudSwellScale, 0f));
            Shader.SetGlobalVector(LookId, new Vector4(settings.NearCloudRim, settings.NearCloudOcclusion, 0f, 0f));
            Shader.SetGlobalFloat(CurveId, settings.NearCloudCurveShare);
            var drift = settings.WindDirection * (settings.NearCloudSpeed * dt);
            var middle = center.position;
            float radius = settings.NearCloudRadius;
            int wanted = settings.NearCloudCount, staying = 0;
            foreach (var cloud in clouds) if (!cloud.leaving) staying++;
            for (int i = clouds.Count - 1; i >= 0; i--)
            {
                var cloud = clouds[i];
                var position = cloud.transform.position + new Vector3(drift.x, 0f, drift.y);
                cloud.transform.position = position;
                cloud.age += dt;
                float x = position.x - middle.x, z = position.z - middle.z;
                if (!cloud.leaving && (cloud.age > cloud.lifetime || x * x + z * z > radius * radius || staying > wanted))
                {
                    cloud.leaving = true;
                    staying--;
                }
                cloud.fade = Mathf.MoveTowards(cloud.fade, cloud.leaving ? 0f : 1f, dt / settings.NearCloudFadeSeconds);
                if (cloud.leaving && cloud.fade <= 0f)
                {
                    if (staying < wanted) { Form(cloud); staying++; }
                    else { Destroy(cloud.transform.gameObject); clouds.RemoveAt(i); continue; }
                }
                Show(cloud);
            }
            for (; staying < wanted; staying++) Form(Add());
        }

        private Cloud Add()
        {
            var go = new GameObject("Cloud", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            var cloud = new Cloud { transform = go.transform, renderer = go.GetComponent<MeshRenderer>() };
            cloud.renderer.sharedMaterial = material;
            cloud.renderer.receiveShadows = false;
            cloud.renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            clouds.Add(cloud);
            return cloud;
        }

        // A new cloud somewhere around the rider, starting invisible.
        private void Form(Cloud cloud)
        {
            var spot = Random.insideUnitCircle * (settings.NearCloudRadius * 0.9f);
            var altitude = settings.NearCloudAltitude;
            cloud.size = Random.Range(settings.NearCloudSize.x, settings.NearCloudSize.y);
            cloud.transform.SetPositionAndRotation(
                center.position + new Vector3(spot.x, Random.Range(altitude.x, altitude.y), spot.y),
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            cloud.transform.localScale = new Vector3(cloud.size, cloud.size * Random.Range(0.85f, 1.15f), cloud.size);
            cloud.transform.GetComponent<MeshFilter>().sharedMesh = shapes[Random.Range(0, shapes.Length)];
            cloud.age = 0f;
            cloud.lifetime = Random.Range(settings.NearCloudLifetime.x, settings.NearCloudLifetime.y);
            cloud.fade = 0f;
            cloud.seed = Random.Range(0f, 100f);
            cloud.leaving = false;
            Show(cloud);
        }

        private void Show(Cloud cloud)
        {
            block.SetFloat(FadeId, cloud.fade);
            block.SetFloat(SeedId, cloud.seed);
            cloud.renderer.SetPropertyBlock(block);
            cloud.renderer.shadowCastingMode = settings.NearCloudShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            // Shapes are about 1 long and 0.4 high before scaling and the swell; the bounds reach down to where the bend
            // shows them.
            var position = cloud.transform.position;
            float drop = (position.y - CurvedWorld.Bend(position).y) * settings.NearCloudCurveShare;
            float swell = settings.NearCloudSwell * cloud.size * 2f;
            cloud.renderer.bounds = new Bounds(position + Vector3.up * (cloud.size * 0.2f - drop * 0.5f),
                new Vector3(cloud.size * 1.2f + swell, cloud.size * 0.6f + drop + swell, cloud.size * 1.2f + swell));
        }
    }
}
