using System;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>Material templates for a world. LevelBuilder makes tinted copies per layer.</summary>
    [Serializable]
    public class LevelMaterials
    {
        public Material ground;
        public Material wall;
        public Material ball;
        public Material pad;
        public Material ringUp;
        public Material ringDrop;
        public Material exit;
        public Material beam;
        public Material spot;
        public Material acid;
        [Tooltip("Templates for particles: soft see-through puffs (steam) and glowing additive sparks (sparks, trails, " +
                 "drifting specks). Saved as assets so builds keep these versions of the particle shader; the game " +
                 "uses copies of them.")]
        public Material softPuff;
        public Material spark;
        [Tooltip("Templates for the lit materials made in code: see-through glass, plain metal and matte black. Saved " +
                 "as assets for the same reason; MakeGlass, MakeMetal and MakeBlack copy them.")]
        public Material glass;
        public Material metal;
        public Material black;

        // The templates in use (set from these slots), for the static makers.
        static Material glassTemplate, metalTemplate, blackTemplate;

        public const string GridShaderName = "SphereDescent/TriplanarGrid";
        public const string LitShaderName = "Universal Render Pipeline/Lit";
        public const string BeamShaderName = "SphereDescent/LightBeam";
        public const string SpotShaderName = "SphereDescent/Spotlight";
        public const string AcidShaderName = "SphereDescent/Acid";

        /// <summary>Fills any unassigned slot with a runtime material so the game still runs without the setup step.</summary>
        public void EnsureDefaults()
        {
            if (ground == null) ground = MakeGrid(new Color(0.75f, 0.85f, 0.9f), new Color(0.45f, 0.55f, 0.6f), 2f, false);
            if (wall == null) wall = MakeGrid(new Color(0.85f, 0.6f, 0.45f), new Color(0.6f, 0.4f, 0.3f), 1f, false);
            if (ball == null) ball = MakeBall();
            if (pad == null) pad = MakeEmissive(new Color(0.2f, 0.9f, 1f), 2f);
            if (ringUp == null) ringUp = MakeEmissive(new Color(0.2f, 0.9f, 1f), 1.5f);
            if (ringDrop == null) ringDrop = MakeEmissive(new Color(1f, 0.45f, 0.05f), 1.8f);
            if (exit == null) exit = MakeEmissive(new Color(1f, 0.85f, 0.2f), 3f);
            if (beam == null) beam = MakeBeam();
            if (spot == null) spot = new Material(Shader.Find(SpotShaderName));
            if (acid == null) acid = new Material(Shader.Find(AcidShaderName));
            if (softPuff == null) softPuff = MakeSoftPuff();
            if (spark == null) spark = MakeSpark();
            if (glass == null) glass = NewGlass();
            if (metal == null) metal = NewMetal();
            if (black == null) black = NewBlack();
            glassTemplate = glass;
            metalTemplate = metal;
            blackTemplate = black;
        }

        /// <summary>A new soft puff material (a copy of the template, with its round, fading blob).</summary>
        public Material NewSoftPuff()
        {
            var m = new Material(softPuff);
            m.SetTexture("_BaseMap", SoftDot);
            return m;
        }

        /// <summary>A new glowing spark material (a copy of the template, with its round, fading blob).</summary>
        public Material NewSpark()
        {
            var m = new Material(spark);
            m.SetTexture("_BaseMap", SoftDot);
            return m;
        }

        /// <summary>Light beam rising from holes and boost pads; LevelBuilder tints copies to match the rings.</summary>
        public static Material MakeBeam() => new Material(Shader.Find(BeamShaderName));

        public static Material MakeGrid(Color baseColor, Color lineColor, float gridSize, bool objectSpaceChecker)
        {
            var shader = Shader.Find(GridShaderName) ?? Shader.Find(LitShaderName);
            var m = new Material(shader) { color = baseColor };
            if (m.HasProperty("_LineColor")) m.SetColor("_LineColor", lineColor);
            if (m.HasProperty("_GridSize")) m.SetFloat("_GridSize", gridSize);
            if (m.HasProperty("_ObjectSpace")) m.SetFloat("_ObjectSpace", objectSpaceChecker ? 1f : 0f);
            if (m.HasProperty("_Checker")) m.SetFloat("_Checker", objectSpaceChecker ? 1f : 0f);
            return m;
        }

        /// <summary>
        /// The player's ball: eight segments (so you can see it roll), alternating glossy black and
        /// neon in the current zone's color, with a soft rim glow.
        /// </summary>
        public static Material MakeBall()
        {
            var m = MakeGrid(Color.white, Color.white, 0.5f, true);
            m.SetFloat("_BallMode", 1f);
            m.SetFloat("_Glossiness", 0.85f);
            m.SetFloat("_GlowIntensity", 1.6f);
            m.SetFloat("_RimStrength", 1.2f);
            return m;
        }

        /// <summary>See-through and faintly glowing: a soft halo around something bright.</summary>
        public static Material MakeGlass(Color color, float alpha, float glow)
        {
            var m = glassTemplate != null ? new Material(glassTemplate) : NewGlass();
            m.EnableKeyword("_EMISSION");
            m.color = new Color(color.r, color.g, color.b, alpha);
            m.SetColor("_EmissionColor", color * glow);
            return m;
        }

        /// <summary>The glass template itself: transparent, alpha-blended, glowing.</summary>
        public static Material NewGlass()
        {
            var m = new Material(Shader.Find(LitShaderName)) { color = new Color(1f, 1f, 1f, 0.1f) };
            m.SetFloat("_Surface", 1f); // transparent
            m.SetFloat("_Blend", 0f);   // alpha
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.white * 0.2f);
            // Marks the glow as real, so saving the template keeps it (and builds keep the glowing version).
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }

        public static Material MakeMetal(Color color, float metallic, float smoothness)
        {
            var m = metalTemplate != null ? new Material(metalTemplate) : NewMetal();
            m.color = color;
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        /// <summary>The metal template itself: plain opaque lit, no glow.</summary>
        public static Material NewMetal() => new Material(Shader.Find(LitShaderName)) { color = Color.grey };

        /// <summary>Plain black with no shine, for the darkness at the bottom of holes.</summary>
        public static Material MakeBlack() => blackTemplate != null ? new Material(blackTemplate) : NewBlack();

        /// <summary>The matte black template itself.</summary>
        public static Material NewBlack()
        {
            var m = new Material(Shader.Find(LitShaderName)) { color = Color.black };
            m.SetFloat("_Smoothness", 0f);
            m.SetFloat("_SpecularHighlights", 0f);
            m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            return m;
        }

        /// <summary>
        /// Soft see-through puffs for particles such as steam: a round blob that fades out to its edge (drawn
        /// at startup rather than loaded), alpha-blended so it never darkens what's behind it much.
        /// </summary>
        public static Material MakeSoftPuff()
        {
            var m = new Material(ParticleShader);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetTexture("_BaseMap", SoftDot);
            return m;
        }

        /// <summary>Glowing sparks for particles: the same soft blob, added on top, so it only ever brightens.</summary>
        public static Material MakeSpark()
        {
            var m = MakeSoftPuff();
            m.SetFloat("_Blend", 2f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            // Over 1, so bloom makes them glow.
            m.SetColor("_BaseColor", new Color(3f, 3f, 3f, 1f));
            return m;
        }

        /// <summary>
        /// URP's unlit particle shader (BuildShaders makes sure builds carry it), or the lit shader should it ever
        /// be missing, so a missing shader dims an effect rather than stopping the world being built.
        /// </summary>
        public static Shader ParticleShader =>
            Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find(LitShaderName);

        static Texture2D softDot;

        /// <summary>A white disc whose alpha falls smoothly from the middle to nothing at the edge.</summary>
        static Texture2D SoftDot
        {
            get
            {
                if (softDot != null) return softDot;
                const int size = 64;
                softDot = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Soft Dot" };
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        px[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * (3f - 2f * a) * 255f));
                    }
                softDot.SetPixels32(px);
                softDot.Apply();
                return softDot;
            }
        }

        public static Material MakeEmissive(Color color, float intensity)
        {
            var m = new Material(Shader.Find(LitShaderName)) { color = color };
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }
    }
}
