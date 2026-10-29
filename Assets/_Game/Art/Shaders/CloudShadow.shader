// Cloud shadow pattern for the sun's light cookie (Docs/하늘_시간대_구현계획.md, step 6). DaySky draws one tile into a
// small repeating render texture every frame and scrolls it over the world with the wind; the sunlight is multiplied
// by it. The noise wraps around the tile edges, and like the far cloud layer in Sky.shader it gathers and clears with
// time and follows the cloud coverage. White is open sky, darker is cloud shadow.
Shader "Hidden/DeliveryRider/CloudShadow"
{
    Properties
    {
        _Coverage ("Coverage", Range(0, 1)) = 0.45
        _Strength ("Strength", Range(0, 1)) = 0.3
        _ShapeTime ("Shape Time", Float) = 0
    }

    SubShader
    {
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Noise cells across one tile at the broadest octave.
            #define BASE_PERIOD 3.0

            CBUFFER_START(UnityPerMaterial)
            float _Coverage, _Strength, _ShapeTime;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // Value noise whose lattice wraps every `period` cells in x and y, so the tile repeats without seams.
            float Noise(float3 x, float period)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float2 a = i.xy - period * floor(i.xy / period);
                float2 b = a + 1.0;
                b -= period * floor(b / period);
                float z = i.z;
                return lerp(lerp(lerp(Hash(float3(a.x, a.y, z)), Hash(float3(b.x, a.y, z)), f.x),
                                 lerp(Hash(float3(a.x, b.y, z)), Hash(float3(b.x, b.y, z)), f.x), f.y),
                            lerp(lerp(Hash(float3(a.x, a.y, z + 1.0)), Hash(float3(b.x, a.y, z + 1.0)), f.x),
                                 lerp(Hash(float3(a.x, b.y, z + 1.0)), Hash(float3(b.x, b.y, z + 1.0)), f.x), f.y), f.z);
            }

            // Same octave weights as the far cloud layer; each octave doubles the period, so the sum still wraps.
            float Fbm(float2 uv, float time)
            {
                float sum = 0.0, amplitude = 0.56, period = BASE_PERIOD;
                [unroll] for (int octave = 0; octave < 4; octave++)
                {
                    sum += amplitude * Noise(float3(uv * period + float2(13.0, 7.0) * octave, time + 3.3 * octave), period);
                    period *= 2.0;
                    time *= 2.02;
                    amplitude *= 0.42;
                }
                return sum;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Wide patches raise or lower the coverage, as in Sky.shader, with softer edges for a shadow.
                float patches = Noise(float3(input.uv * 2.0, _ShapeTime * 0.35 + 5.0), 2.0);
                float coverage = saturate(_Coverage + (patches - 0.5) * 0.6);
                float threshold = lerp(0.78, 0.22, coverage);
                float density = smoothstep(threshold - 0.03, threshold + 0.1, Fbm(input.uv, _ShapeTime));
                float light = 1.0 - _Strength * density;
                return half4(light, light, light, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
