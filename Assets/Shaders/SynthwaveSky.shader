// Synthwave skybox: a deep purple sky fading to a magenta horizon, a field of stars, and a striped retro
// sun sitting on the horizon. On a planet "up" depends on where you stand, so the horizon follows the
// global _PlayerUp and the sun the global _SkySunDir (both set by GameManager.UpdateSky).
Shader "SphereDescent/SynthwaveSky"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.03,0.01,0.10,1)
        _MidColor ("Mid Color", Color) = (0.22,0.04,0.36,1)
        _HorizonColor ("Horizon Color", Color) = (1.0,0.25,0.55,1)
        _BelowColor ("Below Horizon Color", Color) = (0.06,0.01,0.12,1)
        _SunTopColor ("Sun Top Color", Color) = (1.0,0.85,0.25,1)
        _SunBottomColor ("Sun Bottom Color", Color) = (1.0,0.2,0.6,1)
        _SunSize ("Sun Angular Radius", Range(0.05, 0.6)) = 0.22
        _SunGlow ("Sun Brightness", Float) = 1.6
        _StarDensity ("Star Density", Range(0, 0.02)) = 0.004
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TopColor;
                float4 _MidColor;
                float4 _HorizonColor;
                float4 _BelowColor;
                float4 _SunTopColor;
                float4 _SunBottomColor;
                float _SunSize;
                float _SunGlow;
                float _StarDensity;
            CBUFFER_END

            float3 _PlayerUp;
            float3 _SkySunDir;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float Hash (float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 up = dot(_PlayerUp, _PlayerUp) > 0.5 ? normalize(_PlayerUp) : float3(0, 1, 0);
                float h = dot(d, up);

                // Gradient: horizon glow -> purple -> near-black overhead.
                float3 col = h >= 0
                    ? lerp(lerp(_HorizonColor.rgb, _MidColor.rgb, saturate(h * 5.0)), _TopColor.rgb, saturate((h - 0.2) * 1.6))
                    : lerp(_HorizonColor.rgb * 0.5, _BelowColor.rgb, saturate(-h * 6.0));

                // Stars, fading out toward the horizon glow.
                float3 cell = floor(d * 220.0);
                float star = step(1.0 - _StarDensity, Hash(cell));
                float twinkle = 0.6 + 0.4 * Hash(cell + 7.0);
                col += star * twinkle * saturate((h - 0.08) * 4.0);

                // Retro sun: disc on the horizon, yellow at the top to pink at the bottom, with horizontal
                // gaps that get wider toward the horizon.
                float3 sunDir = normalize(_SkySunDir);
                float sunAngle = acos(saturate(dot(d, sunDir)));
                if (sunAngle < _SunSize)
                {
                    float sunUp = dot(sunDir, up);
                    float rel = (h - sunUp) / _SunSize; // -1 at the bottom of the disc, 1 at the top
                    float3 sunCol = lerp(_SunBottomColor.rgb, _SunTopColor.rgb, saturate(rel * 0.5 + 0.5)) * _SunGlow;
                    float gap = rel < 0.1 ? step(frac(rel * 6.0), saturate(0.15 - rel * 0.35)) : 0.0;
                    float edge = smoothstep(_SunSize, _SunSize * 0.97, sunAngle);
                    col = lerp(col, sunCol, edge * (1.0 - gap));
                }
                // Soft glow around the sun.
                col += _SunBottomColor.rgb * 0.25 * saturate(1.0 - sunAngle / (_SunSize * 3.0));

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
