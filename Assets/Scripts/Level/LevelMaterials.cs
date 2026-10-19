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

        public const string GridShaderName = "SphereDescent/TriplanarGrid";
        public const string LitShaderName = "Universal Render Pipeline/Lit";

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
        }

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
