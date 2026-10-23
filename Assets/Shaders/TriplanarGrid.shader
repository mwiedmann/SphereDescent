// Triplanar patterns for procedurally generated meshes that have no UVs.
// Zone mode is the synthwave look for the world: dark surfaces with glowing, anti-aliased neon lines in
// the color of the world zone (ZoneMap, set as global arrays) they face, one of four patterns per zone
// (from the geometric set, grids, stripes and dots; the organic one, coral cells, sand ripples, polyp
// clusters and kelp veins; or the crystal one, frost hexagons, cracked ice, snowflakes and wind-blown
// drifts), and a bright seam along zone borders. Wall mode instead draws dark panels with a neon strip
// along the top edge, a dimmer one at the base, and faintly glowing panel lines on the sides. Ball mode
// draws the player's ball: eight equal segments split by three great circles (so rolling is easy to
// see), alternating glossy black and neon in the color of the zone the ball is in, fading across zone
// borders, plus a soft rim glow. Glow is HDR emission, picked up by the bloom effect.
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
        _SeamGlow ("Wall Panel Line Glow", Range(0, 1)) = 0.15
        [Toggle] _BallMode ("Ball Mode", Float) = 0
        _RimStrength ("Ball Rim Glow", Float) = 1.6
        _PatternStrength ("Zone Pattern Strength", Range(0, 1)) = 1
        [Enum(Geometric,0,Organic,1,Crystal,2)] _PatternSet ("Zone Pattern Set", Float) = 0
        _Caustics ("Caustic Light", Range(0, 2)) = 0
        _CausticScale ("Caustic Size", Float) = 6
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
            float _PatternStrength;
            float _PatternSet;
            float _Caustics;
            float _CausticScale;
            float _SeamGlow;
            float _BallMode;
            float _RimStrength;
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
                float2 uv : TEXCOORD0; // walls: x = radius of the ground under this vertex
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
                float groundRadius : TEXCOORD5;
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
                o.groundRadius = v.uv.x > 0.0 ? v.uv.x : _SurfaceRadius;
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

            float2 Hash22 (float2 p)
            {
                float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            // Fades a pattern to its average coverage where its cells get down to a few pixels, so distant floors
            // don't shimmer (g is the pattern's own cell coordinates).
            float FadeOut (float v, float average, float2 g)
            {
                return lerp(average, v, saturate(1.0 - max(fwidth(g.x), fwidth(g.y)) * 3.0));
            }

            // The deep sea's patterns, rounder and less regular than the grids:
            // 0 = coral cells, 1 = sand ripples, 2 = polyp clusters, 3 = kelp veins.
            float OrganicPattern (float2 p, int kind)
            {
                float2 q = p / _GridSize;
                if (kind == 0)
                {
                    // Irregular rounded cells with glowing borders: where the nearest two cell seeds are about
                    // as near as each other.
                    float2 g = q * 0.55, c = floor(g), f = frac(g);
                    float f1 = 8.0, f2 = 8.0;
                    for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 o = float2(x, y);
                        float d = length(o + 0.1 + 0.8 * Hash22(c + o) - f);
                        if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) f2 = d;
                    }
                    float edge = f2 - f1, w = fwidth(edge), width = _LineWidth * 2.2;
                    return FadeOut(1.0 - smoothstep(width - w, width + w, edge), 0.15, g);
                }
                if (kind == 1)
                {
                    // Ripples in sand: wavy, wandering parallel lines.
                    float y = q.y * 1.1 + 0.9 * sin(q.x * 0.7 + 1.7 * sin(q.y * 0.3)) + 0.35 * sin(q.x * 1.6 - q.y * 0.5);
                    return Lines1D(y, _LineWidth * 1.2);
                }
                if (kind == 2)
                {
                    // Round spots of every size, scattered in loose clumps.
                    float2 g = q * 1.2, c = floor(g), f = frac(g);
                    float spot = 0.0, aa = max(fwidth(g.x), fwidth(g.y));
                    for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 o = float2(x, y), cell = c + o;
                        float2 h = Hash22(cell), h2 = Hash22(cell + 17.13);
                        float clump = 0.5 + 0.5 * sin(cell.x * 0.31 + 1.3) * sin(cell.y * 0.27 - 0.7);
                        if (h2.x > 0.3 + 0.6 * clump) continue;
                        float r = 0.1 + 0.2 * h2.y;
                        float d = length(o + 0.15 + 0.7 * h - f);
                        spot = max(spot, 1.0 - smoothstep(r - aa, r + aa, d));
                    }
                    return FadeOut(spot, 0.1, g);
                }
                // A flowing net of thin crossing strands, like kelp or a jellyfish's nerve net.
                float2 v = q * 0.8;
                v += 0.35 * float2(sin(v.y * 1.3 + sin(v.x * 0.7)), sin(v.x * 1.1 + 1.2 * sin(v.y * 0.9)));
                float a = Lines1D(v.x + 0.3 * sin(v.y * 2.1), _LineWidth);
                float b = Lines1D(v.x * 0.5 + v.y * 0.87 + 0.4 * sin(v.x * 1.7), _LineWidth);
                return max(a, b);
            }

            // Distance to the nearest edge between Voronoi cells (approximately), for cells of 1 in g.
            float CellEdge (float2 g)
            {
                float2 c = floor(g), f = frac(g);
                float f1 = 8.0, f2 = 8.0;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 o = float2(x, y);
                    float d = length(o + 0.1 + 0.8 * Hash22(c + o) - f);
                    if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) f2 = d;
                }
                return f2 - f1;
            }

            // The frozen patterns, sharp and crystalline:
            // 0 = frost hexagons, 1 = cracked ice, 2 = snowflakes, 3 = wind-blown drifts.
            float CrystalPattern (float2 p, int kind)
            {
                float2 q = p / _GridSize;
                if (kind == 0)
                {
                    // A honeycomb of hexagonal frost cells.
                    float2 g = q * 0.8, r = float2(1.0, 1.7320508), hr = r * 0.5;
                    float2 a = g - r * floor(g / r) - hr;
                    float2 b = (g - hr) - r * floor((g - hr) / r) - hr;
                    float2 cell = dot(a, a) < dot(b, b) ? a : b;
                    float2 ac = abs(cell);
                    float edge = 0.5 - max(dot(ac, float2(0.5, 0.8660254)), ac.x);
                    float w = fwidth(edge), width = _LineWidth * 0.8;
                    return FadeOut(1.0 - smoothstep(width - w, width + w, edge), 0.08, g);
                }
                if (kind == 1)
                {
                    // Cracked ice: big plates split by bold cracks, crazed with finer ones.
                    float2 g = q * 0.75, g2 = q * 2.1;
                    float e1 = CellEdge(g), e2 = CellEdge(g2 + 31.7);
                    float w1 = fwidth(e1), w2 = fwidth(e2);
                    float bold = 1.0 - smoothstep(_LineWidth * 1.1 - w1, _LineWidth * 1.1 + w1, e1);
                    float fine = 1.0 - smoothstep(_LineWidth * 0.55 - w2, _LineWidth * 0.55 + w2, e2);
                    return max(FadeOut(bold, 0.1, g), FadeOut(fine, 0.05, g2) * 0.55);
                }
                if (kind == 2)
                {
                    // Six-armed snowflakes of every size, scattered and turned every which way.
                    float2 g = q * 0.9, c = floor(g), f = frac(g);
                    float flake = 0.0, aa = max(fwidth(g.x), fwidth(g.y)) * 1.2;
                    for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 o = float2(x, y), cellId = c + o;
                        float2 h = Hash22(cellId), h2 = Hash22(cellId + 9.31);
                        if (h2.x > 0.6) continue;
                        float2 v = o + 0.25 + 0.5 * h - f;
                        float size = 0.18 + 0.2 * h2.y, len = length(v);
                        if (len > size + aa) continue;
                        // Distance from the nearest of the six arms (each a line out from the middle).
                        float ang = atan2(v.y, v.x) + h.x * 6.2831853;
                        float sector = (frac(ang / 1.0471976 + 0.5) - 0.5) * 1.0471976;
                        float arm = len * abs(sin(sector));
                        float thick = 0.025 + 0.012 * (1.0 - len / size);
                        float body = (1.0 - smoothstep(thick - aa, thick + aa, arm)) * (1.0 - smoothstep(size - aa, size + aa, len));
                        flake = max(flake, body);
                    }
                    return FadeOut(flake, 0.06, g);
                }
                // Drifts: long, gently curving streaks blown across the ice, broken into dashes.
                float y = q.y * 0.9 + 0.6 * sin(q.x * 0.25 + 0.7 * sin(q.y * 0.4));
                float row = floor(y);
                float dash = smoothstep(0.15, 0.45, 0.5 + 0.5 * sin(q.x * 0.7 + Hash22(float2(row, 3.7)).x * 6.2831853));
                return Lines1D(y, _LineWidth * 1.1) * lerp(0.3, 1.0, dash);
            }

            float Triplanar (float3 p, float3 n, int kind)
            {
                float3 w = pow(abs(normalize(n)), 12); // sharp blend so patterns don't ghost on curved ground
                w /= (w.x + w.y + w.z);
                return Pattern(p.yz, kind) * w.x + Pattern(p.xz, kind) * w.y + Pattern(p.xy, kind) * w.z;
            }

            // A pattern projected straight down onto the sphere from one direction (a zone's center), so a zone
            // shows a single copy of it. Triplanar blending shows two faint copies wherever the ground faces
            // between two axes.
            float2 PlaneCoords (float3 p, float3 axis)
            {
                float3 helper = abs(axis.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 t1 = normalize(cross(axis, helper));
                float3 t2 = cross(axis, t1);
                return float2(dot(p, t1), dot(p, t2));
            }

            // Rippling caustic light, as cast on a sea floor. The pattern is precomputed into a tiling texture
            // (CausticTexture); two copies drifting different ways, kept only where both are bright, make it
            // shimmer. Mipmaps soften it in the distance.
            TEXTURE2D(_CausticTex);
            SAMPLER(sampler_CausticTex);

            float Caustic (float2 p)
            {
                float2 uv = p / _CausticScale;
                float t = _Time.y;
                float a = SAMPLE_TEXTURE2D(_CausticTex, sampler_CausticTex, uv + t * float2(0.021, 0.013)).r;
                float b = SAMPLE_TEXTURE2D(_CausticTex, sampler_CausticTex, uv * 1.27 + 0.37 - t * float2(0.017, -0.019)).r;
                return min(a, b) * 1.4;
            }

            float TriplanarCaustic (float3 p, float3 n)
            {
                float3 w = pow(abs(normalize(n)), 12);
                w /= (w.x + w.y + w.z);
                return Caustic(p.yz) * w.x + Caustic(p.xz) * w.y + Caustic(p.xy) * w.z;
            }

            // The two zones whose directions are closest to dir, and how close.
            void NearestZones (float3 dir, out int zone, out int zone2, out float best, out float second)
            {
                best = -2; second = -2; zone = 0; zone2 = 0;
                for (int z = 0; z < 12; z++)
                {
                    float d = dot(dir, _ZoneDirs[z].xyz);
                    if (d > best) { second = best; zone2 = zone; best = d; zone = z; }
                    else if (d > second) { second = d; zone2 = z; }
                }
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 albedo;
                float3 emission = 0;
                if (_BallMode > 0.5)
                {
                    // Color of the zone the ball's center is in, blended with the neighboring zone over the
                    // last unit and a half before the border so it changes smoothly.
                    float3 center = GetObjectToWorldMatrix()._m03_m13_m23;
                    int zone, zone2;
                    float best, second;
                    NearestZones(normalize(center), zone, zone2, best, second);
                    float w = 0.5 + 0.5 * saturate((best - second) / 1.05 * length(center) / 1.5);
                    float3 neon = lerp(_ZoneColors[zone2].rgb, _ZoneColors[zone].rgb, w);
                    // Octants of the ball (object space), alternating like a checkerboard. Computed from the
                    // sphere itself rather than projected, so every segment is the same size and shape.
                    // The soft sign anti-aliases the edges.
                    float3 p = normalize(i.gridPos);
                    float3 soft = clamp(p / max(fwidth(p), 1e-4), -1.0, 1.0);
                    float m = 0.5 + 0.5 * soft.x * soft.y * soft.z;

                    albedo = lerp(_DarkColor.rgb, neon * 0.3, m);
                    float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                    float rim = pow(1.0 - saturate(dot(normalize(i.normalWS), viewDir)), 3.0);
                    emission = neon * m * _GlowIntensity + lerp(neon, 1.0, 0.5) * rim * _RimStrength;
                }
                else if (_ZoneMode > 0.5)
                {
                    float3 dir = normalize(i.positionWS);
                    int zone, zone2;
                    float best, second;
                    NearestZones(dir, zone, zone2, best, second);
                    // Distance (world units) to the border with the nearest other zone. Adjacent icosahedron
                    // directions are ~1.05 apart, which turns the dot-product gap into a sine distance.
                    float borderDist = (best - second) / 1.05 * length(i.positionWS);

                    float3 neon = _ZoneColors[zone].rgb;
                    albedo = _DarkColor.rgb * lerp(1.0, 0.4 + neon * 2.0, _ZoneMix) * _Color.rgb;
                    float caustic = _Caustics > 0.0
                        ? (_WallMode > 0.5 ? TriplanarCaustic(i.positionWS, i.normalWS) : Caustic(PlaneCoords(i.positionWS, _ZoneDirs[zone].xyz))) * _Caustics
                        : 0.0;
                    float3 causticLight = lerp(neon, float3(0.6, 0.95, 1.0), 0.6) * caustic;

                    if (_WallMode > 0.5)
                    {
                        // Height above the floor this wall stands on.
                        float h = length(i.positionWS) - i.groundRadius;
                        float aa = fwidth(h);
                        float top = smoothstep(_WallHeight - _StripWidth - aa, _WallHeight - _StripWidth + aa, h);
                        float base = 1.0 - smoothstep(_StripWidth * 0.5 - aa, _StripWidth * 0.5 + aa, h);
                        // The flat top face glows dimmer than the strip down its sides, so walls read as
                        // edged in neon rather than capped with solid bars.
                        float topFace = saturate((dot(normalize(i.normalWS), normalize(i.positionWS)) - 0.8) * 5.0);
                        // Panel lines on the sides glow faintly, below the bloom threshold so they don't halo.
                        // Not on the top, where they'd look like joints between wall pieces.
                        float seams = (1.0 - topFace) * saturate(Triplanar(i.gridPos, i.gridNormal, 0));
                        emission = neon * (top * lerp(1.0, 0.3, topFace) + base * 0.35 + seams * _SeamGlow * _PatternStrength)
                            + causticLight * 0.5;
                    }
                    else
                    {
                        // Floors: the zone's pattern projected down from the zone's center, one copy only.
                        float2 zp = PlaneCoords(i.gridPos, _ZoneDirs[zone].xyz);
                        int kind = zone % 4;
                        float m = saturate(_PatternSet > 1.5 ? CrystalPattern(zp, kind) : _PatternSet > 0.5 ? OrganicPattern(zp, kind) : Pattern(zp, kind)) * _PatternStrength;
                        float bw = fwidth(borderDist);
                        float border = 1.0 - smoothstep(_ZoneBorder - bw, _ZoneBorder + bw, borderDist);
                        emission = lerp(neon * m, neon * 1.5, border) + causticLight;
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
