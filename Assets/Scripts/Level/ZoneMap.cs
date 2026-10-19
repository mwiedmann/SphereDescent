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

        static readonly (string name, Color color)[] Palette =
        {
            ("Crimson", new Color(0.90f, 0.20f, 0.22f)),
            ("Amber",   new Color(1.00f, 0.60f, 0.12f)),
            ("Gold",    new Color(0.96f, 0.86f, 0.25f)),
            ("Lime",    new Color(0.55f, 0.88f, 0.22f)),
            ("Jade",    new Color(0.12f, 0.68f, 0.42f)),
            ("Teal",    new Color(0.12f, 0.72f, 0.74f)),
            ("Azure",   new Color(0.25f, 0.58f, 0.98f)),
            ("Cobalt",  new Color(0.28f, 0.32f, 0.85f)),
            ("Violet",  new Color(0.60f, 0.34f, 0.90f)),
            ("Magenta", new Color(0.90f, 0.28f, 0.72f)),
            ("Coral",   new Color(1.00f, 0.50f, 0.45f)),
            ("Cocoa",   new Color(0.62f, 0.40f, 0.24f)),
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
