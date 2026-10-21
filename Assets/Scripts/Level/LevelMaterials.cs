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
            var m = new Material(Shader.Find(LitShaderName)) { color = new Color(color.r, color.g, color.b, alpha) };
            m.SetFloat("_Surface", 1f); // transparent
            m.SetFloat("_Blend", 0f);   // alpha
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * glow);
            return m;
        }

        public static Material MakeMetal(Color color, float metallic, float smoothness)
        {
            var m = new Material(Shader.Find(LitShaderName)) { color = color };
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        /// <summary>Plain black with no shine, for the darkness at the bottom of holes.</summary>
        public static Material MakeBlack()
        {
            var m = new Material(Shader.Find(LitShaderName)) { color = Color.black };
            m.SetFloat("_Smoothness", 0f);
            m.SetFloat("_SpecularHighlights", 0f);
            m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            return m;
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
