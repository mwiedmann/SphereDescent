using System;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>The four floor patterns a style's zones take turns through.</summary>
    public enum PatternSet { Geometric, Organic, Crystal, Carved, Cosmic }

    /// <summary>
    /// The look of a world: zone colors, how surfaces glow, the sky, the light and the air. Worlds take turns
    /// through the GameManager's list of styles. Gameplay colors (hole rings, pads, beams, hazards, the core's
    /// lava) are the same in every style, so they always mean the same thing.
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
        [Tooltip("Which four floor patterns the zones take turns through: the geometric set (square grid, diagonal " +
                 "grid, stripes, dots), organic (coral cells, sand ripples, polyp clusters, kelp veins), crystal (frost " +
                 "hexagons, cracked ice, snowflakes, wind-blown drifts), carved (temple meander, cobbles, spiral vines, " +
                 "leaf veins) or cosmic (constellations, orbit rings, spiral galaxies, starbursts).")]
        public PatternSet patternSet = PatternSet.Geometric;
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
        [Tooltip("Retro stripes cut across the bottom of the sun.")]
        public bool sunStripes = true;
        [Range(0f, 0.02f)] public float starDensity = 0.004f;
        [Tooltip("How fast the stars drift (0 = still).")]
        public float starDrift = 0f;
        [Tooltip("Shafts of light fanning down from overhead. 0 = none.")]
        [Range(0f, 1f)] public float lightRays = 0f;
        [Tooltip("The light shafts' color. Fully transparent = the sky's top color.")]
        public Color rayColor = Color.clear;
        [Tooltip("Rippling curtains of aurora across the sky, from the first color at their foot to the second at " +
                 "their top. 0 = none.")]
        [Range(0f, 2f)] public float aurora = 0f;
        public Color auroraLow = new Color(0.1f, 0.95f, 0.85f);
        public Color auroraHigh = new Color(0.7f, 0.3f, 1f);
        [Tooltip("Swirling clouds of glowing gas across the sky, blending the two colors. 0 = none.")]
        [Range(0f, 2f)] public float nebula = 0f;
        public Color nebulaA = new Color(1f, 0.2f, 0.7f);
        public Color nebulaB = new Color(0.2f, 0.4f, 1f);
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
        [Tooltip("How fast the specks fall (toward the ground, wherever the player is), like snow. 0 = they just drift.")]
        public float speckFall = 0f;
        [Tooltip("Size of the specks.")]
        public Vector2 speckSize = new Vector2(0.03f, 0.09f);
        [Tooltip("Specks blink on and off as they drift, like fireflies or twinkling stardust.")]
        public bool speckTwinkle = false;

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
            patternSet = PatternSet.Organic,
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

        /// <summary>
        /// Frozen: an arctic polar night. Frosted blue-grey surfaces etched with crystal patterns glowing in icy
        /// blues, violets and pinks; a dark sky full of stars with curtains of aurora rippling across it; snow
        /// falling; a pale frosty haze in the distance. The aurora stays teal and violet, never acid green.
        /// </summary>
        public static VisualStyle Frozen() => new VisualStyle
        {
            name = "Frozen",
            zones = new[]
            {
                new Zone { name = "Glacier",   color = new Color(0.45f, 0.80f, 1.00f) },
                new Zone { name = "Rose",      color = new Color(1.00f, 0.50f, 0.75f) },
                new Zone { name = "Aurora",    color = new Color(0.15f, 0.95f, 0.90f) },
                new Zone { name = "Lilac",     color = new Color(0.75f, 0.55f, 1.00f) },
                new Zone { name = "Moonlight", color = new Color(1.00f, 0.95f, 0.65f) },
                new Zone { name = "Sapphire",  color = new Color(0.25f, 0.40f, 1.00f) },
                new Zone { name = "Frost",     color = new Color(0.85f, 0.95f, 1.00f) },
                new Zone { name = "Snowberry", color = new Color(1.00f, 0.30f, 0.55f) },
                new Zone { name = "Icicle",    color = new Color(0.55f, 1.00f, 1.00f) },
                new Zone { name = "Twilight",  color = new Color(0.55f, 0.30f, 1.00f) },
                new Zone { name = "Crystal",   color = new Color(0.95f, 0.80f, 1.00f) },
                new Zone { name = "Polar",     color = new Color(0.35f, 0.65f, 0.95f) },
            },
            darkColor = new Color(0.26f, 0.33f, 0.45f),
            groundZoneMix = 0.25f,
            wallZoneMix = 0.35f,
            glowIntensity = 1.5f,
            patternStrength = 0.55f,
            patternSet = PatternSet.Crystal,
            caustics = 0f,
            skyTop = new Color(0.005f, 0.01f, 0.05f),
            skyMid = new Color(0.02f, 0.05f, 0.14f),
            skyHorizon = new Color(0.16f, 0.26f, 0.42f),
            skyBelow = new Color(0.04f, 0.07f, 0.13f),
            sunSize = 0f,
            sunGlow = 0f,
            sunTop = new Color(0.8f, 0.9f, 1f),
            sunBottom = new Color(0.4f, 0.6f, 0.9f),
            starDensity = 0.008f,
            starDrift = 0f,
            lightRays = 0f,
            aurora = 1f,
            auroraLow = new Color(0.1f, 0.95f, 0.85f),
            auroraHigh = new Color(0.75f, 0.3f, 1f),
            innerBackground = new Color(0.01f, 0.02f, 0.05f),
            sunLight = new Color(0.75f, 0.85f, 1f),
            sunLightIntensity = 0.9f,
            ambientLight = new Color(0.30f, 0.36f, 0.50f),
            fogDensity = 0.006f,
            fogColor = new Color(0.22f, 0.30f, 0.44f),
            floatingSpecks = 700,
            speckColor = new Color(0.92f, 0.96f, 1f, 0.85f),
            speckFall = 1.3f,
            speckSize = new Vector2(0.05f, 0.12f),
        };

        /// <summary>
        /// Overgrown ruins: a lost temple at golden hour. Weathered stone with a muted moss tint, carved with
        /// meanders, cobbles, vines and leaves glowing in warm golds, rose, teal and violet; a hazy dusk sky with
        /// a low soft sun and warm shafts of light; fireflies rising and blinking; a warm haze in the distance.
        /// The moss stays olive and the jade pale, never acid green.
        /// </summary>
        public static VisualStyle Ruins() => new VisualStyle
        {
            name = "Overgrown Ruins",
            zones = new[]
            {
                new Zone { name = "Sunstone",   color = new Color(1.00f, 0.80f, 0.30f) },
                new Zone { name = "Turquoise",  color = new Color(0.15f, 0.85f, 0.80f) },
                new Zone { name = "Rosewood",   color = new Color(1.00f, 0.55f, 0.55f) },
                new Zone { name = "Amethyst",   color = new Color(0.65f, 0.45f, 1.00f) },
                new Zone { name = "Jade",       color = new Color(0.50f, 0.90f, 0.70f) },
                new Zone { name = "Honey",      color = new Color(1.00f, 0.90f, 0.55f) },
                new Zone { name = "Lotus",      color = new Color(1.00f, 0.50f, 0.80f) },
                new Zone { name = "Lagoon",     color = new Color(0.30f, 0.70f, 1.00f) },
                new Zone { name = "Sage",       color = new Color(0.70f, 0.85f, 0.60f) },
                new Zone { name = "Firefly",    color = new Color(0.95f, 1.00f, 0.60f) },
                new Zone { name = "Orchid",     color = new Color(0.85f, 0.55f, 1.00f) },
                new Zone { name = "Moonflower", color = new Color(0.85f, 0.85f, 1.00f) },
            },
            darkColor = new Color(0.17f, 0.145f, 0.10f),
            groundZoneMix = 0.3f,
            wallZoneMix = 0.4f,
            glowIntensity = 1.35f,
            patternStrength = 0.6f,
            patternSet = PatternSet.Carved,
            caustics = 0f,
            skyTop = new Color(0.10f, 0.14f, 0.28f),
            skyMid = new Color(0.45f, 0.33f, 0.38f),
            skyHorizon = new Color(1.00f, 0.66f, 0.32f),
            skyBelow = new Color(0.14f, 0.10f, 0.07f),
            sunSize = 0.11f,
            sunGlow = 1.3f,
            sunTop = new Color(1f, 0.93f, 0.7f),
            sunBottom = new Color(1f, 0.62f, 0.3f),
            sunStripes = false,
            starDensity = 0.0008f,
            starDrift = 0f,
            lightRays = 0.45f,
            rayColor = new Color(1f, 0.75f, 0.4f, 1f),
            innerBackground = new Color(0.03f, 0.02f, 0.015f),
            sunLight = new Color(1f, 0.85f, 0.62f),
            sunLightIntensity = 1.05f,
            ambientLight = new Color(0.30f, 0.25f, 0.20f),
            fogDensity = 0.007f,
            fogColor = new Color(0.50f, 0.38f, 0.26f),
            floatingSpecks = 220,
            speckColor = new Color(1f, 0.9f, 0.4f, 0.95f),
            speckFall = -0.35f,
            speckSize = new Vector2(0.06f, 0.14f),
            speckTwinkle = true,
        };

        /// <summary>
        /// Cosmic nebula: glassy obsidian surfaces etched with constellations, orbit rings, spiral galaxies and
        /// starbursts in nebula colors; swirling clouds of glowing gas across a deep, star-thick sky; twinkling
        /// stardust drifting about.
        /// </summary>
        public static VisualStyle Nebula() => new VisualStyle
        {
            name = "Cosmic Nebula",
            zones = new[]
            {
                new Zone { name = "Andromeda", color = new Color(1.00f, 0.30f, 0.80f) },
                new Zone { name = "Sirius",    color = new Color(0.55f, 0.80f, 1.00f) },
                new Zone { name = "Nova",      color = new Color(1.00f, 0.85f, 0.45f) },
                new Zone { name = "Lyra",      color = new Color(0.55f, 0.40f, 1.00f) },
                new Zone { name = "Quasar",    color = new Color(0.20f, 0.95f, 1.00f) },
                new Zone { name = "Vega",      color = new Color(1.00f, 0.55f, 0.65f) },
                new Zone { name = "Halo",      color = new Color(0.90f, 0.90f, 1.00f) },
                new Zone { name = "Orion",     color = new Color(0.30f, 0.45f, 1.00f) },
                new Zone { name = "Pulsar",    color = new Color(0.85f, 0.40f, 1.00f) },
                new Zone { name = "Comet",     color = new Color(0.60f, 1.00f, 0.95f) },
                new Zone { name = "Eclipse",   color = new Color(1.00f, 0.70f, 0.90f) },
                new Zone { name = "Nebula",    color = new Color(0.70f, 0.55f, 1.00f) },
            },
            darkColor = new Color(0.03f, 0.025f, 0.05f),
            groundZoneMix = 0.4f,
            wallZoneMix = 0.6f,
            glowIntensity = 1.6f,
            patternStrength = 0.8f,
            patternSet = PatternSet.Cosmic,
            caustics = 0f,
            skyTop = new Color(0.01f, 0.005f, 0.03f),
            skyMid = new Color(0.03f, 0.015f, 0.07f),
            skyHorizon = new Color(0.10f, 0.05f, 0.18f),
            skyBelow = new Color(0.02f, 0.01f, 0.04f),
            sunSize = 0f,
            sunGlow = 0f,
            starDensity = 0.012f,
            starDrift = 0f,
            lightRays = 0f,
            nebula = 1f,
            nebulaA = new Color(1f, 0.25f, 0.7f),
            nebulaB = new Color(0.2f, 0.45f, 1f),
            innerBackground = new Color(0.01f, 0.005f, 0.02f),
            sunLight = new Color(0.85f, 0.8f, 1f),
            sunLightIntensity = 0.9f,
            ambientLight = new Color(0.18f, 0.14f, 0.28f),
            fogDensity = 0f,
            floatingSpecks = 300,
            speckColor = new Color(0.9f, 0.85f, 1f, 0.8f),
            speckFall = 0f,
            speckSize = new Vector2(0.03f, 0.08f),
            speckTwinkle = true,
        };
    }
}
