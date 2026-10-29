// Day-clock sundial drawn in one UI quad with signed distances, so it stays sharp at any screen size: a sky window
// whose colours follow the day, the sun riding an arc from the left horizon (morning) to the right (evening) with
// rays and glow, phase beads at the quarters, a small skyline the sun sets behind, and stars after hours.
// SundialView feeds _Progress, _Dusk, _Warn and _Size (the rect in canvas units) on a runtime copy of the material.
// With _Hanging the dial hangs from the top edge of the screen: the same picture mirrored, its horizon along the top
// edge and the sun dipping through lunch at the bottom of the arc, without the skyline (it would hang upside down) and
// with a light rim and a soft shadow to set it off the scene. Colour properties are authored in sRGB like other UI
// colours.
Shader "DeliveryRider/UI Sundial"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)

        [Header(Day state (set by SundialView))]
        _Progress ("Day Progress", Range(0, 1)) = 0.45
        _Dusk ("After Hours", Range(0, 1)) = 0
        _Warn ("Closing Soon", Range(0, 1)) = 0
        _Size ("Rect Size (canvas units)", Vector) = (104, 78, 0, 0)
        [Toggle] _Hanging ("Hanging From The Top Edge", Float) = 0

        [Header(Sky)]
        _MorningTop ("Morning Top", Color) = (0.48, 0.77, 0.94, 1)
        _MorningHorizon ("Morning Horizon", Color) = (1.0, 0.88, 0.69, 1)
        _NoonTop ("Noon Top", Color) = (0.30, 0.67, 0.93, 1)
        _NoonHorizon ("Noon Horizon", Color) = (0.80, 0.93, 1.0, 1)
        _EveningTop ("Evening Top", Color) = (0.46, 0.40, 0.76, 1)
        _EveningHorizon ("Evening Horizon", Color) = (1.0, 0.56, 0.36, 1)
        _DuskTop ("After Hours Top", Color) = (0.10, 0.14, 0.31, 1)
        _DuskHorizon ("After Hours Horizon", Color) = (0.70, 0.35, 0.45, 1)

        [Header(Dial)]
        _Track ("Track", Color) = (0.96, 0.95, 0.91, 0.45)
        _ArcStart ("Arc Start", Color) = (1.0, 0.84, 0.40, 1)
        _ArcEnd ("Arc End", Color) = (1.0, 0.47, 0.28, 1)
        _SunCore ("Sun Core", Color) = (1.0, 0.97, 0.82, 1)
        _SunRim ("Sun Rim", Color) = (1.0, 0.78, 0.25, 1)
        _SunSet ("Sun At Sunset", Color) = (1.0, 0.46, 0.26, 1)
        _Skyline ("Skyline", Color) = (0.09, 0.17, 0.21, 1)
        _Frame ("Hanging Rim", Color) = (1, 1, 1, 1)

        // UI masking, as UI/Default.
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float4 _ClipRect;
            int _UIVertexColorAlwaysGammaSpace;
            float _Progress, _Dusk, _Warn, _Hanging;
            float4 _Size;
            fixed4 _MorningTop, _MorningHorizon, _NoonTop, _NoonHorizon, _EveningTop, _EveningHorizon, _DuskTop, _DuskHorizon;
            fixed4 _Track, _ArcStart, _ArcEnd, _SunCore, _SunRim, _SunSet, _Skyline, _Frame;
            // Star positions in sky-radius units, with a twinkle phase.
            static const float3 Stars[5] = { float3(-0.46, 0.52, 0.0), float3(0.10, 0.78, 1.7), float3(0.48, 0.40, 3.1),
                float3(-0.20, 0.30, 4.4), float3(0.30, 0.64, 5.6) };

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace()) v.color.rgb = UIGammaToLinear(v.color.rgb);
                o.color = v.color * _Color;
                return o;
            }

            // Straight colour laid over premultiplied accumulation.
            void Over(inout float4 acc, float3 color, float alpha)
            {
                acc.rgb = color * alpha + acc.rgb * (1.0 - alpha);
                acc.a = alpha + acc.a * (1.0 - alpha);
            }

            // Coverage of a signed distance (negative inside), anti-aliased over one screen pixel.
            float Fill(float distance, float pixel) { return saturate(0.5 - distance / pixel); }

            float Hash(float n) { return frac(sin(n * 12.9898 + 4.1414) * 43758.5453); }

            // Coverage of one row of buildings: cells of the given width, heights between low and high (canvas units).
            float Skyline(float2 p, float span, float cell, float offset, float low, float high, float seed, float px)
            {
                float index = floor((p.x + span + offset) / cell);
                float left = index * cell - span - offset;
                float height = lerp(low, high, Hash(index + seed));
                float edge = max(max(left - p.x, p.x - (left + cell * 0.94)), p.y - height);
                return Fill(edge, px) * step(-span, p.x) * step(p.x, span);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Canvas units with the origin in the middle of the horizon line (the top edge when hanging, with y
                // running down, so the rest of the picture is the standing one mirrored).
                float hanging = step(0.5, _Hanging);
                float2 size = max(_Size.xy, 1.0);
                float radius = lerp(min(size.x * 0.34, size.y * 0.64), min(size.x * 0.37, size.y * 0.74), hanging);
                float2 uv = float2(i.uv.x, lerp(i.uv.y, 1.0 - i.uv.y, hanging));
                float2 p = uv * size - float2(size.x * 0.5, size.y * 0.24 * (1.0 - hanging));
                float px = max(max(fwidth(p.x), fwidth(p.y)), 1e-4);
                float t = saturate(_Progress);
                float dusk = saturate(_Dusk);
                float pulse = saturate(_Warn) * (0.5 + 0.5 * sin(_Time.y * 5.0));
                float above = saturate(0.5 + p.y / px);
                float dist = length(p);
                float4 acc = 0;

                // Hanging: soft shadow and a light rim around the half disc.
                float rimEdge = radius + 3.0;
                Over(acc, float3(0.0, 0.0, 0.0), 0.22 * (1.0 - smoothstep(rimEdge - 2.0, rimEdge + 9.0, dist)) * hanging);
                Over(acc, _Frame.rgb, Fill(dist - rimEdge, px) * _Frame.a * hanging);

                // Sky window: morning -> noon -> evening, with the after-hours dusk on top.
                float toNoon = smoothstep(0.0, 0.4, t), toEvening = smoothstep(0.6, 1.0, t);
                float3 top = lerp(lerp(_MorningTop.rgb, _NoonTop.rgb, toNoon), _EveningTop.rgb, toEvening);
                float3 low = lerp(lerp(_MorningHorizon.rgb, _NoonHorizon.rgb, toNoon), _EveningHorizon.rgb, toEvening);
                top = lerp(top, _DuskTop.rgb, dusk);
                low = lerp(low, _DuskHorizon.rgb, dusk);
                float skyRadius = radius - 2.0;
                float3 sky = lerp(low, top, saturate(p.y / skyRadius)) * lerp(1.0, 0.9, smoothstep(skyRadius * 0.7, skyRadius, dist));
                float window = Fill(dist - skyRadius, px) * above;
                Over(acc, sky, window);

                // Stars after hours.
                for (int s = 0; s < 5; s++)
                {
                    float twinkle = 0.55 + 0.45 * sin(_Time.y * 2.3 + Stars[s].z);
                    Over(acc, float3(1.0, 0.97, 0.88), Fill(length(p - Stars[s].xy * skyRadius) - 0.9, px) * dusk * twinkle * window);
                }

                // Sun position on the arc; after hours it sinks below the horizon.
                float angle = UNITY_PI * (1.0 - t);
                float sunRadius = radius * 0.17;
                float2 sun = radius * float2(cos(angle), sin(angle)) - float2(0.0, dusk * sunRadius * 2.4);
                float sunDist = length(p - sun);
                float3 rim = lerp(_SunRim.rgb, _SunSet.rgb, max(smoothstep(0.7, 1.0, t), dusk));

                // Glow behind the skyline, fading before the rect edge so sunrise and sunset are not cut off.
                float glow = pow(saturate(1.0 - sunDist / (sunRadius * 2.4)), 2.0) * (0.45 + 0.25 * pulse) * (1.0 - 0.5 * dusk);
                Over(acc, rim, glow * above * saturate((size.x * 0.5 - abs(p.x)) / 3.0));

                // Skyline in the middle of the horizon (the arc ends stay clear so sunrise and sunset show):
                // a hazy back row, then a darker, lower front row.
                float span = radius * 0.8;
                float standing = 1.0 - hanging;
                Over(acc, lerp(sky, _Skyline.rgb, 0.55), Skyline(p, span, radius * 0.19, 0.0, radius * 0.16, radius * 0.3, 7.0, px) * above * standing);
                Over(acc, _Skyline.rgb, Skyline(p, span, radius * 0.11, radius * 0.05, radius * 0.05, radius * 0.16, 0.0, px) * above * standing);

                // Horizon line (standing only; hanging, the screen edge is the horizon) and arc track.
                Over(acc, _Track.rgb, Fill(max(abs(p.y) - 0.6, abs(p.x) - (radius + sunRadius)), px) * _Track.a * standing);
                float ring = abs(dist - radius);
                Over(acc, _Track.rgb, Fill(ring - 1.1, px) * above * _Track.a);

                // Travelled arc, warming toward evening and pulsing when the day is about to close.
                float along = 1.0 - atan2(max(p.y, 0.0), p.x) / UNITY_PI;
                float3 arc = lerp(_ArcStart.rgb, _ArcEnd.rgb, smoothstep(0.35, 1.0, along));
                arc = lerp(arc, _DuskHorizon.rgb, dusk * 0.4) * (1.0 + 0.3 * pulse);
                Over(acc, arc, Fill(max(ring - 1.7, (along - t) * UNITY_PI * radius), px) * above);

                // Phase beads at the quarters (morning / lunch / afternoon / evening); passed beads take the arc colour.
                for (int k = 1; k <= 3; k++)
                {
                    float at = k * 0.25;
                    float a = UNITY_PI * (1.0 - at);
                    float passed = step(at, t);
                    float3 bead = lerp(_Track.rgb, lerp(_ArcStart.rgb, _ArcEnd.rgb, smoothstep(0.35, 1.0, at)), passed);
                    Over(acc, bead, Fill(length(p - radius * float2(cos(a), sin(a))) - 2.1, px) * lerp(0.85, 1.0, passed));
                }

                // Rays turning slowly around the sun, then the disc.
                float rayAngle = atan2(p.y - sun.y, p.x - sun.x) - _Time.y * 0.35;
                float rays = smoothstep(0.75, 0.98, abs(cos(rayAngle * 4.0)));
                float band = saturate((sunDist - sunRadius * 1.3) / px + 0.5) * saturate((sunRadius * 1.95 - sunDist) / (sunRadius * 0.65));
                Over(acc, rim, rays * band * above * (1.0 - dusk) * 0.9);
                float3 core = lerp(_SunCore.rgb, rim, saturate(sunDist / sunRadius) * 0.85);
                Over(acc, core, Fill(sunDist - sunRadius, px) * above * (1.0 - 0.35 * dusk));

                fixed4 color = fixed4(acc.rgb / max(acc.a, 1e-4), acc.a) * i.color;
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
