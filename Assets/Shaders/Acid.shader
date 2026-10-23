// The surface of an acid pool, boiling: dark green churning with brighter toxic currents, and crowded with
// bubbles of two sizes that swell (a bright rim around a faint dome) and then pop into a quickly spreading
// ring. Unlit; the bright parts are over 1 so bloom makes them glow. The core's lava is the same surface in
// reds and oranges, with fewer, bigger bubbles.
Shader "SphereDescent/Acid"
{
    Properties
    {
        _Deep ("Deep Color", Color) = (0.02,0.12,0.02,1)
        _Glow ("Glow Color", Color) = (0.45,1,0.1,1)
        _Intensity ("Glow Intensity", Float) = 2.2
        _Scale ("Swirl Size", Float) = 2.5
        _BubbleSize ("Bubble Size", Float) = 1
        _BubbleAmount ("Bubble Amount", Range(0, 1)) = 1
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
                float _BubbleSize;
                float _BubbleAmount;
            CBUFFER_END

            float _GraphicsQuality; // 0 = low, 1 = medium, 2 = high (global, set by GraphicsQuality)

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

            float3 Hash3 (float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            // One layer of bubbles in cells of cellSize (world units): each cell may hold a bubble at a random
            // spot that swells over its life and then pops. The surface cuts through these 3D bubbles, so they
            // show as circles. Returns the bubbles' brightness; pops come back separately.
            float Bubbles (float3 pos, float cellSize, float speed, float density, out float pops)
            {
                float3 q = pos / cellSize;
                float3 c = floor(q);
                float aa = fwidth(q.x) + fwidth(q.y) + fwidth(q.z);
                float bright = 0;
                pops = 0;
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    float3 cc = c + float3(x, y, z);
                    float3 h = Hash3(cc);
                    float rate = speed * (0.75 + 0.5 * h.y);
                    float cycle = _Time.y * rate + h.z * 13.1;
                    float life = frac(cycle);
                    // Each time round, whether there's a bubble here and where it sits is settled when it's born and
                    // kept for its whole life, so bubbles never blink in or out halfway. Boiling comes and goes in
                    // patches that drift about (judged at birth too), so bubbles cluster.
                    float turn = floor(cycle);
                    float born = (turn - h.z * 13.1) / rate;
                    float3 hr = Hash3(cc + turn * 1.618);
                    float hot = 0.35 + 0.65 * saturate(0.5 + 0.5 * sin(cc.x * 0.55 * cellSize + born * 0.3)
                                                             * sin(cc.z * 0.5 * cellSize - born * 0.23 + cc.y * 0.4 * cellSize));
                    if (hr.x > density * hot) continue;
                    float3 center = cc + 0.2 + 0.6 * Hash3(cc + 7.7 + turn * 3.1);
                    float d = length(q - center);
                    float size = 0.1 + 0.38 * smoothstep(0.0, 0.85, life);
                    if (life < 0.85)
                    {
                        // A rounded dome: a thin bright edge, a glowing body and a small shine near the top.
                        float inside = smoothstep(size + aa, size - aa, d);
                        float rim = inside * smoothstep(size - 0.06 - aa, size - 0.02, d);
                        float dome = inside * (0.35 + 0.35 * sqrt(saturate(1.0 - d * d / max(size * size, 1e-4))));
                        float shine = smoothstep(size * 0.28 + aa, size * 0.1, length(q - center - float3(0.3, 0.3, -0.3) * size));
                        bright = max(bright, rim * 0.9 + dome + shine * 1.6);
                    }
                    else
                    {
                        // Popped: a thin ring spreading outward and fading.
                        float s = (life - 0.85) / 0.15;
                        float ring = smoothstep(0.05 + aa, 0.0, abs(d - (size + s * 0.25))) * (1.0 - s);
                        pops = max(pops, ring);
                    }
                }
                return bright;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 p = i.positionWS / _Scale;
                float t = _Time.y;
                // Churning currents: a few drifting, warping sine waves, quicker than a lazy swirl.
                float w = sin(p.x * 1.7 + sin(p.z * 1.3 + t * 1.1) * 1.8 + t * 0.9)
                        + sin(p.z * 2.1 + sin(p.y * 1.1 - t * 1.0) * 1.8 - t * 0.8)
                        + sin(p.y * 1.9 + sin(p.x * 1.2 + t * 0.95) * 1.8 + t * 0.7);
                float churn = smoothstep(0.4, 2.6, w);

                float popsBig, popsSmall = 0.0, small = 0.0;
                float big = Bubbles(i.positionWS, 1.2 * _BubbleSize, 0.18, 0.5 * _BubbleAmount, popsBig);
                // The small bubbles are left out on low graphics quality: they cost as much as the big ones.
                if (_GraphicsQuality > 0.5)
                    small = Bubbles(i.positionWS + 17.3, 0.5 * _BubbleSize, 0.25, 0.3 * _BubbleAmount, popsSmall);
                float bubbles = max(big, small * 0.8);
                float pops = max(popsBig, popsSmall * 0.8);

                float3 glow = _Glow.rgb * _Intensity;
                float3 col = lerp(_Deep.rgb, glow * 0.55, churn * 0.6);
                col += glow * bubbles;
                col += lerp(glow, float3(1.6, 2.2, 1.4), 0.4) * pops * 1.3;
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
