// The surface of an acid pool: dark green with brighter toxic swirls drifting across it and bubbles that
// swell and pop. Unlit; the bright parts are over 1 so bloom makes them glow.
Shader "SphereDescent/Acid"
{
    Properties
    {
        _Deep ("Deep Color", Color) = (0.02,0.12,0.02,1)
        _Glow ("Glow Color", Color) = (0.45,1,0.1,1)
        _Intensity ("Glow Intensity", Float) = 2.2
        _Scale ("Swirl Size", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Acid"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Deep;
                float4 _Glow;
                float _Intensity;
                float _Scale;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float fogFactor : TEXCOORD1; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
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
                float3 p = i.positionWS / _Scale;
                float t = _Time.y;
                // Swirls: a few drifting sine waves warping each other.
                float w = sin(p.x * 1.7 + sin(p.z * 1.3 + t * 0.6) * 1.5 + t * 0.4)
                        + sin(p.z * 2.1 + sin(p.y * 1.1 - t * 0.5) * 1.5 - t * 0.35)
                        + sin(p.y * 1.9 + sin(p.x * 1.2 + t * 0.45) * 1.5 + t * 0.3);
                float swirl = smoothstep(0.6, 2.4, w);
                // Bubbles: in a coarse grid, each cell's bubble grows and pops on its own timer.
                float3 cell = floor(i.positionWS * 1.5);
                float3 local = frac(i.positionWS * 1.5) - 0.5;
                float h = Hash(cell);
                float life = frac(t * (0.3 + h * 0.4) + h * 7.0);
                float size = 0.12 + 0.25 * life;
                float bubble = h > 0.55 ? smoothstep(size, size - 0.06, length(local)) * smoothstep(size - 0.12, size - 0.06, length(local)) * (1.0 - life) : 0.0;
                float3 col = lerp(_Deep.rgb, _Glow.rgb * _Intensity, swirl * 0.7) + _Glow.rgb * _Intensity * bubble * 1.5;
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
