// Triplanar grid / checker for procedurally generated meshes that have no UVs.
// World-space grid lines give the curved floors a sense of speed and scale; object-space checker
// mode (used on the ball) makes rolling visible.
// Zone mode is the synthwave look for the world: dark surfaces with glowing, anti-aliased neon lines in
// the color of the world zone (ZoneMap, set as global arrays) they face, one of four line patterns per
// zone, and a bright seam along zone borders. Wall mode instead draws dark panels with a neon strip
// along the top edge and a dimmer one at the base. Glow is HDR emission, picked up by the bloom effect.
// Universal Render Pipeline: lit with URP's PBR lighting and main-light shadows.
Shader "SphereDescent/TriplanarGrid"
{
    Properties
    {
        [MainColor] _Color ("Color", Color) = (1,1,1,1)
        _LineColor ("Line Color", Color) = (0.5,0.5,0.5,1)
        _GridSize ("Grid Size", Float) = 2
        _LineWidth ("Line Width", Range(0, 0.5)) = 0.04
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0
        [Toggle] _ObjectSpace ("Object Space", Float) = 0
        [Toggle] _Checker ("Checker Instead Of Lines", Float) = 0
        [Toggle] _ZoneMode ("Color By World Zone", Float) = 0
        _ZoneMix ("Zone Color Strength", Range(0,1)) = 0.6
        _ZoneBorder ("Zone Border Width", Float) = 0.2
        _DarkColor ("Dark Surface Color", Color) = (0.035,0.025,0.07,1)
        _GlowIntensity ("Glow Intensity", Float) = 1.8
        [Toggle] _WallMode ("Wall Mode", Float) = 0
        _SurfaceRadius ("Wall Base Radius", Float) = 0
        _WallHeight ("Wall Height", Float) = 4
        _StripWidth ("Neon Strip Width", Float) = 0.22
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
            float4 _DarkColor;
            float _GlowIntensity;
            float _WallMode;
            float _SurfaceRadius;
            float _WallHeight;
            float _StripWidth;
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

            // Coverage of lines along integer coordinates of q (cell units), anti-aliased with screen-space
            // derivatives. Where lines get denser than a few pixels they fade to their average coverage, so
            // distant floors don't shimmer.
            float Lines1D (float q, float width)
            {
                float w = fwidth(q);
                float d = 0.5 - abs(frac(q) - 0.5);
                float l = 1.0 - smoothstep(width - w, width + w, d);
                return lerp(width * 2.0, l, saturate(1.0 - w * 3.0));
            }

            float Lines2D (float2 q, float width)
            {
                return max(Lines1D(q.x, width), Lines1D(q.y, width));
            }

            // 0 = square grid, 1 = diagonal grid, 2 = stripes, 3 = dots, 4 = checker (the ball)
            float Pattern (float2 p, int kind)
            {
                float2 q = p / _GridSize;
                if (kind == 0) return Lines2D(q, _LineWidth);
                if (kind == 1) return Lines2D(float2(q.x + q.y, q.x - q.y) * 0.7071, _LineWidth);
                if (kind == 2) return Lines1D((q.x + q.y) * 1.4142, _LineWidth);
                if (kind == 3)
                {
                    float d = length(frac(q) - 0.5);
                    float w = fwidth(d);
                    float spot = 1.0 - smoothstep(0.12 - w, 0.12 + w, d);
                    return lerp(0.045, spot, saturate(1.0 - fwidth(q.x) * 3.0));
                }
                float2 c = floor(q);
                return fmod(abs(c.x + c.y), 2.0);
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
                float3 emission = 0;
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

                    float3 neon = _ZoneColors[zone].rgb;
                    albedo = _DarkColor.rgb * lerp(1.0, 0.4 + neon * 2.0, _ZoneMix) * _Color.rgb;

                    if (_WallMode > 0.5)
                    {
                        // Height above the floor this wall stands on.
                        float h = length(i.positionWS) - _SurfaceRadius;
                        float aa = fwidth(h);
                        float top = smoothstep(_WallHeight - _StripWidth - aa, _WallHeight - _StripWidth + aa, h);
                        float base = 1.0 - smoothstep(_StripWidth * 0.5 - aa, _StripWidth * 0.5 + aa, h);
                        // The flat top face glows dimmer than the strip down its sides, so walls read as
                        // edged in neon rather than capped with solid bars.
                        float topFace = saturate((dot(normalize(i.normalWS), normalize(i.positionWS)) - 0.8) * 5.0);
                        emission = neon * (top * lerp(1.0, 0.3, topFace) + base * 0.35);
                        // Panel seams.
                        albedo *= 1.0 - 0.6 * saturate(Triplanar(i.gridPos, i.gridNormal, 0));
                    }
                    else
                    {
                        float m = saturate(Triplanar(i.gridPos, i.gridNormal, zone % 4));
                        float bw = fwidth(borderDist);
                        float border = 1.0 - smoothstep(_ZoneBorder - bw, _ZoneBorder + bw, borderDist);
                        emission = lerp(neon * m, neon * 1.5, border);
                        // Ceilings (the underside of the shell above) glow less, so they don't crowd the view.
                        if (dot(i.normalWS, i.positionWS) < 0) emission *= 0.35;
                    }
                    emission *= _GlowIntensity * _Color.rgb;
                }
                else
                {
                    float m = saturate(Triplanar(i.gridPos, i.gridNormal, _Checker > 0.5 ? 4 : 0));
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
                surface.emission = emission;

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
