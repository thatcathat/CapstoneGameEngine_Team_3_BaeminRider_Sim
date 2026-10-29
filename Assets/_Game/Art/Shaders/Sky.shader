// Skybox for the work day (Docs/하늘_시간대_구현계획.md, steps 2 and 4): zenith-to-horizon gradient, sun disc and glow, and
// a far cloud layer that drifts with the wind and gathers or clears as its noise evolves. The far clouds are drawn to
// match the near soft clouds (Cloud.shader): rounded lobes make scalloped heaps, their thickness gives a surface that
// faces the viewer and leans out at the edges, and that surface takes the same soft light ramp, darker thick middles
// and bright thin edges (rim and occlusion amounts shared with the near clouds, _NearCloudLook). After closing the sun
// sinks below the horizon and its colour becomes the afterglow. Directions below the horizon, which the curved world
// shows past its bent edge, keep the horizon colour; DaySky gives the horizon the fog colour, so the fogged world
// meets the sky without a band. DaySky sets every property each frame on a runtime copy; the defaults are the midday
// look for edit mode.
Shader "DeliveryRider/Sky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.42, 0.66, 0.88, 1)
        _HorizonColor ("Horizon (fog colour)", Color) = (0.776, 0.875, 0.847, 1)
        _SunColor ("Sun", Color) = (1, 0.97, 0.92, 1)
        _SunDirection ("Sun Direction (toward the sun)", Vector) = (0, 0.906, -0.423, 0)
        _SunSize ("Sun Radius (degrees)", Range(0.5, 6)) = 2.5
        _Dusk ("Dusk After Closing", Range(0, 1)) = 0
        _CloudCoverage ("Cloud Coverage", Range(0, 1)) = 0.45
        _CloudScale ("Cloud Scale", Float) = 1.6
        _CloudOffset ("Cloud Offset (wind drift)", Vector) = (0, 0, 0, 0)
        _CloudTime ("Cloud Time (shape change)", Float) = 0
        _CloudLit ("Cloud Lit Side", Color) = (1, 0.98, 0.95, 1)
        _CloudShade ("Cloud Shaded Side", Color) = (0.69, 0.76, 0.8, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Skybox"
        }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _ZenithColor, _HorizonColor, _SunColor, _SunDirection, _CloudOffset, _CloudLit, _CloudShade;
            float _SunSize, _Dusk, _CloudCoverage, _CloudScale, _CloudTime;
            CBUFFER_END

            // Rim and occlusion of the near clouds (x, y), set by CloudField; zero in edit mode.
            float4 _NearCloudLook;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            // Weighted toward the broad octaves, so clouds read as rounded heaps rather than speckled holes.
            float Fbm(float3 p)
            {
                float sum = 0.0, amplitude = 0.56;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    sum += amplitude * Noise(p);
                    p = p * 2.02 + float3(13.1, 7.7, 3.3);
                    amplitude *= 0.42;
                }
                return sum;
            }

            // Rounded lobes: 1 at a wandering point in each cell, falling off with distance (inverted cellular noise).
            float Lobes(float2 p, float time)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                float nearest = 1.0;
                [unroll] for (int y = -1; y <= 1; y++)
                    [unroll] for (int x = -1; x <= 1; x++)
                    {
                        float2 offset = float2(x, y);
                        float2 seed = float2(Hash(float3(cell + offset, 1.7)), Hash(float3(cell + offset, 9.2)));
                        float2 spot = offset + 0.5 + 0.35 * sin(time + 6.2831853 * seed);
                        nearest = min(nearest, length(spot - f));
                    }
                return 1.0 - nearest;
            }

            // Cloud thickness on the cloud plane: below 0 is open sky, 0 the edge. Wide, slow patches raise or lower the
            // local coverage, so clouds gather in some places and clear up in others; the broad noise lays out the heaps
            // and two sizes of lobes round off their edges like the puffs of the near clouds.
            float CloudThickness(float2 uv)
            {
                float patches = Noise(float3(uv * 0.23, _CloudTime * 0.35 + 5.0));
                float coverage = saturate(_CloudCoverage + (patches - 0.5) * 0.6);
                float threshold = lerp(0.78, 0.22, coverage);
                float lobes = Lobes(uv * 3.0, _CloudTime * 6.0) * 0.7 + Lobes(uv * 7.0 + 17.0, _CloudTime * 9.0) * 0.3;
                // Steep, so the dome rounds off at the edge and the middle stays smooth.
                return (Fbm(float3(uv, _CloudTime)) + (lobes - 0.55) * 0.28 - threshold) * 7.0;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.direction);
                float3 sunDir = normalize(_SunDirection.xyz);
                float height = dir.y;

                // Gradient; below the horizon the horizon colour continues.
                float3 color = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(saturate(height), 0.45));
                float sunDot = dot(dir, sunDir);
                float towardSun = pow(saturate(sunDot), 6.0);
                float lowSun = 1.0 - saturate(sunDir.y * 2.5);
                color += _SunColor.rgb * towardSun * (0.08 + 0.32 * lowSun) * (1.0 - saturate(height * 1.5));

                // Sun disc and glow, cut by the horizon.
                float angle = acos(clamp(sunDot, -1.0, 1.0));
                float edge = max(fwidth(angle), 1e-4);
                float above = smoothstep(-edge, edge, height);
                float disc = 1.0 - smoothstep(radians(_SunSize) - edge, radians(_SunSize) + edge, angle);
                float glow = pow(saturate(sunDot), 300.0) * 0.6 + pow(saturate(sunDot), 40.0) * 0.25;
                color += (_SunColor.rgb * glow + lerp(float3(1.0, 1.0, 1.0), _SunColor.rgb, 0.35) * 1.4 * disc) * above;

                // Far clouds on a plane above, thinning toward the horizon. Worked out for every direction: the edge
                // smoothing needs screen derivatives, which a branch would break.
                {
                    float2 uv = dir.xz / (max(height, 0.0) + 0.12) * _CloudScale - _CloudOffset.xy;
                    float thickness = CloudThickness(uv);
                    float cover = smoothstep(0.0, max(fwidth(thickness) * 1.5, 0.02), thickness);
                    // A domed surface over the thickness: it faces the viewer, leans outward toward the edges and a
                    // little up, so the side toward the sun lights like a near cloud's.
                    const float delta = 0.02;
                    float dome = sqrt(saturate(thickness));
                    float2 slope = (float2(sqrt(saturate(CloudThickness(uv + float2(delta, 0.0)))),
                        sqrt(saturate(CloudThickness(uv + float2(0.0, delta))))) - dome) / delta;
                    float3 normal = normalize(-dir + float3(-slope.x, 0.0, -slope.y) * 0.03 + float3(0.0, 0.25, 0.0));
                    // The same ramp as the near clouds, fading as the sun goes after closing.
                    float lit = smoothstep(0.05, 0.75, dot(normal, sunDir) * 0.5 + 0.5) * (1.0 - 0.8 * _Dusk);
                    // The cloud colours come from DaySky, the same as the near clouds'.
                    float3 cloud = lerp(_CloudShade.rgb, _CloudLit.rgb, lit) * lerp(1.0, lerp(1.0, 0.55, saturate(thickness)), _NearCloudLook.y);
                    cloud += _CloudLit.rgb * pow(1.0 - dome, 3.0) * _NearCloudLook.x + _SunColor.rgb * towardSun * 0.35;
                    color = lerp(color, cloud, cover * smoothstep(0.02, 0.22, height));
                }

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
