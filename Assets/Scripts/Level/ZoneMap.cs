using UnityEngine;
using Random = System.Random;

namespace SphereDescent
{
    /// <summary>
    /// Splits the sphere into 12 named, colored zones (nearest of 12 icosahedron directions) so players can
    /// tell where they are. The zones line up through every layer, so "Amber" is the same side of the world on
    /// the surface and on the core. Each world gets its own rotation and color assignment. The grid shader
    /// reads the zones from global shader arrays to color and pattern the ground and walls.
    /// </summary>
    public sealed class ZoneMap
    {
        public const int Count = 12;

        public readonly Vector3[] Directions = new Vector3[Count];
        public readonly Color[] Colors = new Color[Count];
        public readonly string[] Names = new string[Count];

        /// <param name="palette">The style's zones: each zone's grid lines and wall strips glow in its color.</param>
        public ZoneMap(int seed, VisualStyle.Zone[] palette)
        {
            if (palette == null || palette.Length < Count) palette = VisualStyle.Synthwave().zones;
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
                Colors[i] = palette[order[i]].color;
                Names[i] = palette[order[i]].name;
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
