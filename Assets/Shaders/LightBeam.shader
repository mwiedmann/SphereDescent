// A column of light rising from a hole or boost pad, so you can spot it over the maze walls and head for
// it. Additive, so it glows (and blooms) without hiding what's behind it. Brightest down the middle of the
// column as seen from any side, fading out toward the top and in softly at the base, with faint bands
// drifting upward. The mesh's UV y runs 0 at the base to 1 at the top.
Shader "SphereDescent/LightBeam"
{
    Properties
    {
        _Color ("Color", Color) = (0.2,0.9,1,1)
        _Intensity ("Intensity", Float) = 1.6
        _FadeStart ("Fade Start (fraction of height)", Range(0, 1)) = 0.3
        _BandSpeed ("Band Speed", Float) = 1.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Beam"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _FadeStart;
                float _BandSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(pos.positionWS);
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                // Facing the camera = the middle of the column; grazing = its soft edges.
                float facing = abs(dot(normalize(i.normalWS), normalize(i.viewWS)));
                float core = facing * facing;
                float h = i.uv.y;
                float fade = (1.0 - smoothstep(_FadeStart, 1.0, h)) * smoothstep(0.0, 0.04, h);
                float bands = 0.8 + 0.2 * sin(h * 60.0 - _Time.y * _BandSpeed * 6.0);
                float3 color = _Color.rgb * _Intensity * core * fade * bands;
                // Additive: fog fades the beam toward black (nothing added) rather than toward the fog color.
                color = MixFogColor(color, float3(0, 0, 0), i.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
