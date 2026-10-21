using System;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// The look of a world: zone colors, how surfaces glow, the sky, the light and the air. Worlds take turns
    /// through the GameManager's list of styles. Gameplay colors (hole rings, pads, beams, hazards, the gold
    /// core) are the same in every style, so they always mean the same thing.
    /// </summary>
    [Serializable]
    public class VisualStyle
    {
        [Serializable]
        public struct Zone
        {
            public string name;
            public Color color;
        }

        public string name = "Synthwave";

        [Header("Zones")]
        [Tooltip("Twelve zones, each with a name for the HUD and the color its lines glow.")]
        public Zone[] zones;

        [Header("Surfaces")]
        public Color darkColor = new Color(0.035f, 0.025f, 0.07f);
        [Tooltip("How strongly the zone color tints the dark surfaces.")]
        [Range(0f, 1f)] public float groundZoneMix = 0.6f;
        [Range(0f, 1f)] public float wallZoneMix = 0.9f;
        public float glowIntensity = 1.8f;
        [Tooltip("Brightness of the per-zone line patterns (grid, diagonal, stripes, dots).")]
        [Range(0f, 1f)] public float patternStrength = 1f;
        [Tooltip("Shimmering caustic light rippling over the floors and walls, as under water. 0 = none.")]
        [Range(0f, 2f)] public float caustics = 0f;
        [Tooltip("Size of the caustic ripples in world units.")]
        public float causticScale = 6f;

        [Header("Sky")]
        public Color skyTop = new Color(0.03f, 0.01f, 0.10f);
        public Color skyMid = new Color(0.22f, 0.04f, 0.36f);
        public Color skyHorizon = new Color(1f, 0.25f, 0.55f);
        public Color skyBelow = new Color(0.06f, 0.01f, 0.12f);
        [Tooltip("0 hides the sun.")]
        public float sunSize = 0.22f;
        public float sunGlow = 1.6f;
        public Color sunTop = new Color(1f, 0.85f, 0.25f);
        public Color sunBottom = new Color(1f, 0.2f, 0.6f);
        [Range(0f, 0.02f)] public float starDensity = 0.004f;
        [Tooltip("How fast the stars drift (0 = still).")]
        public float starDrift = 0f;
        [Tooltip("Shafts of light fanning down from overhead. 0 = none.")]
        [Range(0f, 1f)] public float lightRays = 0f;
        [Tooltip("Background below the surface, where the layers above are hidden.")]
        public Color innerBackground = Color.black;

        [Header("Light and air")]
        public Color sunLight = new Color(1f, 0.85f, 0.95f);
        public float sunLightIntensity = 1f;
        public Color ambientLight = new Color(0.32f, 0.24f, 0.42f);
        [Tooltip("Exponential-squared fog density. 0 = no fog.")]
        public float fogDensity = 0f;
        public Color fogColor = Color.black;
        [Tooltip("Specks drifting in the air around the camera, like marine snow. 0 = none.")]
        public int floatingSpecks = 0;
        public Color speckColor = new Color(0.7f, 0.9f, 1f, 0.6f);

        public static VisualStyle Synthwave() => new VisualStyle
        {
            name = "Synthwave",
            zones = new[]
            {
                new Zone { name = "Crimson", color = new Color(1.00f, 0.10f, 0.25f) },
                new Zone { name = "Amber",   color = new Color(1.00f, 0.50f, 0.05f) },
                new Zone { name = "Gold",    color = new Color(1.00f, 0.85f, 0.10f) },
                new Zone { name = "Lime",    color = new Color(0.60f, 1.00f, 0.10f) },
                new Zone { name = "Jade",    color = new Color(0.05f, 1.00f, 0.45f) },
                new Zone { name = "Teal",    color = new Color(0.00f, 0.95f, 0.90f) },
                new Zone { name = "Azure",   color = new Color(0.10f, 0.60f, 1.00f) },
                new Zone { name = "Cobalt",  color = new Color(0.30f, 0.30f, 1.00f) },
                new Zone { name = "Violet",  color = new Color(0.65f, 0.20f, 1.00f) },
                new Zone { name = "Magenta", color = new Color(1.00f, 0.15f, 0.85f) },
                new Zone { name = "Coral",   color = new Color(1.00f, 0.45f, 0.40f) },
                new Zone { name = "Ice",     color = new Color(0.75f, 0.90f, 1.00f) },
            },
        };

        /// <summary>
        /// Deep sea: dark blue-green surfaces glowing softly in bioluminescent colors, with caustic light
        /// rippling over them; the sky is water, brighter overhead with light shafts fanning down and specks
        /// drifting by; a blue haze swallows the distance.
        /// </summary>
        public static VisualStyle DeepSea() => new VisualStyle
        {
            name = "Deep Sea",
            zones = new[]
            {
                new Zone { name = "Lagoon",    color = new Color(0.00f, 0.85f, 0.80f) },
                new Zone { name = "Jellyfish", color = new Color(1.00f, 0.35f, 0.80f) },
                new Zone { name = "Plankton",  color = new Color(0.35f, 1.00f, 0.45f) },
                new Zone { name = "Anemone",   color = new Color(0.85f, 0.30f, 1.00f) },
                new Zone { name = "Lantern",   color = new Color(0.85f, 1.00f, 0.25f) },
                new Zone { name = "Abyss",     color = new Color(0.15f, 0.45f, 1.00f) },
                new Zone { name = "Coral",     color = new Color(1.00f, 0.45f, 0.35f) },
                new Zone { name = "Seafoam",   color = new Color(0.55f, 1.00f, 0.85f) },
                new Zone { name = "Kelp",      color = new Color(0.55f, 0.80f, 0.15f) },
                new Zone { name = "Pearl",     color = new Color(0.85f, 0.90f, 1.00f) },
                new Zone { name = "Urchin",    color = new Color(0.55f, 0.35f, 1.00f) },
                new Zone { name = "Reef",      color = new Color(1.00f, 0.65f, 0.20f) },
            },
            darkColor = new Color(0.01f, 0.04f, 0.07f),
            groundZoneMix = 0.35f,
            wallZoneMix = 0.5f,
            glowIntensity = 1.3f,
            patternStrength = 0.35f,
            caustics = 1f,
            causticScale = 7f,
            skyTop = new Color(0.05f, 0.30f, 0.40f),
            skyMid = new Color(0.01f, 0.10f, 0.18f),
            skyHorizon = new Color(0.01f, 0.05f, 0.10f),
            skyBelow = new Color(0.00f, 0.01f, 0.03f),
            sunSize = 0f,
            sunGlow = 0f,
            sunTop = new Color(0.6f, 0.95f, 1f),
            sunBottom = new Color(0.1f, 0.5f, 0.7f),
            starDensity = 0.006f,
            starDrift = 0.02f,
            lightRays = 0.6f,
            innerBackground = new Color(0.0f, 0.015f, 0.03f),
            sunLight = new Color(0.55f, 0.85f, 0.95f),
            sunLightIntensity = 0.8f,
            ambientLight = new Color(0.08f, 0.20f, 0.26f),
            fogDensity = 0.009f,
            fogColor = new Color(0.01f, 0.06f, 0.10f),
            floatingSpecks = 400,
            speckColor = new Color(0.7f, 0.95f, 1f, 0.55f),
        };
    }
}
