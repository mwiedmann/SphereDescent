// Triplanar grid / checker for procedurally generated meshes that have no UVs.
// World-space grid lines give the curved floors a sense of speed and scale; object-space checker
// mode (used on the ball) makes rolling visible.
// Zone mode tints and patterns surfaces by which of the 12 world zones (ZoneMap, set as global
// arrays) they face, with a dark seam along zone borders, so each side of the world looks different.
// Universal Render Pipeline: lit with URP's PBR lighting and main-light shadows.
Shader "SphereDescent/TriplanarGrid"
{
    Properties
    {
        [MainColor] _Color ("Color", Color) = (1,1,1,1)
        _LineColor ("Line Color", Color) = (0.5,0.5,0.5,1)
        _GridSize ("Grid Size", Float) = 2
        _LineWidth ("Line Width", Range(0, 0.5)) = 0.04
        _Glossiness ("Smoothness", Range(0,1)) = 0.25
        _Metallic ("Metallic", Range(0,1)) = 0
        [Toggle] _ObjectSpace ("Object Space", Float) = 0
        [Toggle] _Checker ("Checker Instead Of Lines", Float) = 0
        [Toggle] _ZoneMode ("Color By World Zone", Float) = 0
        _ZoneMix ("Zone Color Strength", Range(0,1)) = 0.6
        _ZoneBorder ("Zone Border Width", Float) = 0.5
        _PatternContrast ("Pattern Contrast", Range(0,1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float4 _LineColor;
            float _GridSize;
            float _LineWidth;
            float _Glossiness;
            float _Metallic;
            float _ObjectSpace;
            float _Checker;
            float _ZoneMode;
            float _ZoneMix;
            float _ZoneBorder;
            float _PatternContrast;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 _ZoneDirs[12];
            float4 _ZoneColors[12];

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 gridPos : TEXCOORD2;
                float3 gridNormal : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = normalWS;
                o.gridPos = lerp(pos.positionWS, v.positionOS.xyz, _ObjectSpace);
                o.gridNormal = lerp(normalWS, v.normalOS, _ObjectSpace);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            // 0 = grid lines, 1 = checker, 2 = diagonal stripes, 3 = dots
            float Pattern (float2 p, int kind)
            {
                float2 q = p / _GridSize;
                if (kind == 0)
                {
                    float2 f = abs(frac(q) - 0.5);
                    return 1.0 - step(_LineWidth, 0.5 - max(f.x, f.y));
                }
                if (kind == 1)
                {
                    float2 c = floor(q);
                    return fmod(abs(c.x + c.y), 2.0);
                }
                if (kind == 2)
                {
                    return step(frac((q.x + q.y) * 0.5), 0.3);
                }
                return 1.0 - step(0.2, length(frac(q) - 0.5));
            }

            float Triplanar (float3 p, float3 n, int kind)
            {
                float3 w = pow(abs(normalize(n)), 12); // sharp blend so patterns don't ghost on curved ground
                w /= (w.x + w.y + w.z);
                return Pattern(p.yz, kind) * w.x + Pattern(p.xz, kind) * w.y + Pattern(p.xy, kind) * w.z;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 albedo;
                if (_ZoneMode > 0.5)
                {
                    float3 dir = normalize(i.positionWS);
                    float best = -2, second = -2;
                    int zone = 0;
                    for (int z = 0; z < 12; z++)
                    {
                        float d = dot(dir, _ZoneDirs[z].xyz);
                        if (d > best) { second = best; best = d; zone = z; }
                        else if (d > second) { second = d; }
                    }
                    // Distance (world units) to the border with the nearest other zone. Adjacent icosahedron
                    // directions are ~1.05 apart, which turns the dot-product gap into a sine distance.
                    float borderDist = (best - second) / 1.05 * length(i.positionWS);

                    float3 baseCol = lerp(_Color.rgb, _Color.rgb * _ZoneColors[zone].rgb * 1.1, _ZoneMix);
                    float m = saturate(Triplanar(i.gridPos, i.gridNormal, zone % 4));
                    albedo = lerp(baseCol, baseCol * (1.0 - _PatternContrast), m);
                    albedo = lerp(albedo, baseCol * 0.25, step(borderDist, _ZoneBorder));
                }
                else
                {
                    float m = saturate(Triplanar(i.gridPos, i.gridNormal, _Checker > 0.5 ? 1 : 0));
                    albedo = lerp(_Color.rgb, _LineColor.rgb, m);
                }

                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.normalWS = NormalizeNormalPerPixel(i.normalWS);
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                input.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                input.fogCoord = i.fogFactor;
                input.bakedGI = SampleSH(input.normalWS);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = _Metallic;
                surface.smoothness = _Glossiness;
                surface.occlusion = 1;
                surface.alpha = 1;
                surface.normalTS = float3(0, 0, 1);

                half4 color = UniversalFragmentPBR(input, surface);
                color.rgb = MixFog(color.rgb, i.fogFactor);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 vert (Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - positionWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 frag () : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 vert (Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return TransformObjectToHClip(v.positionOS.xyz);
            }

            half frag () : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings vert (Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
