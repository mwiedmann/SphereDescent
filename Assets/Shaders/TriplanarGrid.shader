// Triplanar grid / checker for procedurally generated meshes that have no UVs.
// World-space grid lines give the curved floors a sense of speed and scale; object-space checker
// mode (used on the ball) makes rolling visible.
// Zone mode tints and patterns surfaces by which of the 12 world zones (ZoneMap, set as global
// arrays) they face, with a dark seam along zone borders, so each side of the world looks different.
Shader "SphereDescent/TriplanarGrid"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
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
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        fixed4 _Color;
        fixed4 _LineColor;
        float _GridSize;
        float _LineWidth;
        half _Glossiness;
        half _Metallic;
        float _ObjectSpace;
        float _Checker;
        float _ZoneMode;
        float _ZoneMix;
        float _ZoneBorder;
        float _PatternContrast;

        float4 _ZoneDirs[12];
        float4 _ZoneColors[12];

        struct Input
        {
            float3 gridPos;
            float3 gridNormal;
            float3 worldPos;
        };

        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            float3 worldNormal = UnityObjectToWorldNormal(v.normal);
            o.gridPos = lerp(worldPos, v.vertex.xyz, _ObjectSpace);
            o.gridNormal = lerp(worldNormal, v.normal, _ObjectSpace);
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

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 albedo;
            if (_ZoneMode > 0.5)
            {
                float3 dir = normalize(IN.worldPos);
                float best = -2, second = -2;
                int zone = 0;
                for (int i = 0; i < 12; i++)
                {
                    float d = dot(dir, _ZoneDirs[i].xyz);
                    if (d > best) { second = best; best = d; zone = i; }
                    else if (d > second) { second = d; }
                }
                // Distance (world units) to the border with the nearest other zone. Adjacent icosahedron
                // directions are ~1.05 apart, which turns the dot-product gap into a sine distance.
                float borderDist = (best - second) / 1.05 * length(IN.worldPos);

                float3 baseCol = lerp(_Color.rgb, _Color.rgb * _ZoneColors[zone].rgb * 1.1, _ZoneMix);
                float m = saturate(Triplanar(IN.gridPos, IN.gridNormal, zone % 4));
                albedo = lerp(baseCol, baseCol * (1.0 - _PatternContrast), m);
                albedo = lerp(albedo, baseCol * 0.25, step(borderDist, _ZoneBorder));
            }
            else
            {
                float m = saturate(Triplanar(IN.gridPos, IN.gridNormal, _Checker > 0.5 ? 1 : 0));
                albedo = lerp(_Color.rgb, _LineColor.rgb, m);
            }
            o.Albedo = albedo;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
