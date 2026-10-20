// The pool of light a security camera's spotlight casts on the ground: additive, softly filled, with a
// brighter rim so its edge is easy to judge. The disc mesh's UVs run 0-1 across it.
Shader "SphereDescent/Spotlight"
{
    Properties
    {
        _Color ("Color", Color) = (1,0.92,0.7,1)
        _Intensity ("Intensity", Float) = 1
        _Fill ("Fill Brightness", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Spot"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            // Drawn just above the ground; nudged toward the camera so it doesn't flicker into the floor.
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _Fill;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float d = length(i.uv - 0.5) * 2.0;
                float inside = 1.0 - smoothstep(0.92, 1.0, d);
                float rim = smoothstep(0.75, 0.95, d);
                return half4(_Color.rgb * _Intensity * inside * lerp(_Fill, 1.0, rim), 1);
            }
            ENDHLSL
        }
    }
}
