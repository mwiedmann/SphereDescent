using UnityEngine;
using Random = System.Random;

namespace SphereDescent
{
    /// <summary>
    /// Splits the sphere into 12 named, colored zones (nearest of 12 icosahedron directions) so players can
    /// tell where they are. The zones line up through every layer, so "Amber" is the same side of the world on
    /// the surface and on the core. Each world gets its own rotation and color assignment. The grid shader
    /// reads the zones from global shader arrays to color and pattern the ground, walls and ceilings.
    /// </summary>
    public sealed class ZoneMap
    {
        public const int Count = 12;

        // Neon colors: each zone's grid lines, wall strips and landmark glow in its color.
        static readonly (string name, Color color)[] Palette =
        {
            ("Crimson", new Color(1.00f, 0.10f, 0.25f)),
            ("Amber",   new Color(1.00f, 0.50f, 0.05f)),
            ("Gold",    new Color(1.00f, 0.85f, 0.10f)),
            ("Lime",    new Color(0.60f, 1.00f, 0.10f)),
            ("Jade",    new Color(0.05f, 1.00f, 0.45f)),
            ("Teal",    new Color(0.00f, 0.95f, 0.90f)),
            ("Azure",   new Color(0.10f, 0.60f, 1.00f)),
            ("Cobalt",  new Color(0.30f, 0.30f, 1.00f)),
            ("Violet",  new Color(0.65f, 0.20f, 1.00f)),
            ("Magenta", new Color(1.00f, 0.15f, 0.85f)),
            ("Coral",   new Color(1.00f, 0.45f, 0.40f)),
            ("Ice",     new Color(0.75f, 0.90f, 1.00f)),
        };

        public readonly Vector3[] Directions = new Vector3[Count];
        public readonly Color[] Colors = new Color[Count];
        public readonly string[] Names = new string[Count];

        public ZoneMap(int seed)
        {
            var rng = new Random(seed ^ 0x5f3759df);
            var rotation = Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f);
            var baseDirs = IcoSphere.Get(0).Vertices;

            var order = new int[Count];
            for (int i = 0; i < Count; i++) order[i] = i;
            for (int i = Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            for (int i = 0; i < Count; i++)
            {
                Directions[i] = (rotation * baseDirs[i]).normalized;
                Colors[i] = Palette[order[i]].color;
                Names[i] = Palette[order[i]].name;
            }
        }

        public int ZoneAt(Vector3 position)
        {
            Vector3 dir = position.normalized;
            int best = 0;
            float bestDot = -2f;
            for (int i = 0; i < Count; i++)
            {
                float d = Vector3.Dot(dir, Directions[i]);
                if (d > bestDot) { bestDot = d; best = i; }
            }
            return best;
        }

        /// <summary>Publishes the zones to the grid shader. Call whenever the active world changes.</summary>
        public void ApplyToShaders()
        {
            var dirs = new Vector4[Count];
            var cols = new Vector4[Count];
            for (int i = 0; i < Count; i++)
            {
                dirs[i] = Directions[i];
                // Raw vector uniforms skip Unity's automatic color-space conversion, so do it here.
                Color c = QualitySettings.activeColorSpace == ColorSpace.Linear ? Colors[i].linear : Colors[i];
                cols[i] = new Vector4(c.r, c.g, c.b, 1f);
            }
            Shader.SetGlobalVectorArray("_ZoneDirs", dirs);
            Shader.SetGlobalVectorArray("_ZoneColors", cols);
        }
    }
}
