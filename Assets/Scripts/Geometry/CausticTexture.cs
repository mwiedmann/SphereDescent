using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// A tileable caustic light pattern (the bright, wobbling net cast on a sea floor), computed once into a
    /// small texture so the surface shader only has to read it instead of computing it for every pixel.
    /// The shader animates it by reading two copies drifting in different directions.
    /// </summary>
    public static class CausticTexture
    {
        static Texture2D texture;

        public static Texture2D Get()
        {
            if (texture != null) return texture;
            const int size = 256;
            texture = new Texture2D(size, size, TextureFormat.R8, true, true)
            {
                name = "Caustics",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            var pixels = new Color32[size * size];
            const float tau = Mathf.PI * 2f;
            const float t = 23f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // The classic tileable water-caustic: iterated warping of the plane, periodic over one tile.
                    Vector2 q = new Vector2(x / (float)size * tau - 250f, y / (float)size * tau - 250f);
                    Vector2 i = q;
                    float c = 1f;
                    const float inten = 0.005f;
                    for (int n = 0; n < 5; n++)
                    {
                        float tt = t * (1f - 3.5f / (n + 1f));
                        i = q + new Vector2(Mathf.Cos(tt - i.x) + Mathf.Sin(tt + i.y), Mathf.Sin(tt - i.y) + Mathf.Cos(tt + i.x));
                        c += 1f / new Vector2(q.x / (Mathf.Sin(i.x + tt) / inten), q.y / (Mathf.Cos(i.y + tt) / inten)).magnitude;
                    }
                    c = 1.17f - Mathf.Pow(c / 5f, 1.4f);
                    float v = Mathf.Clamp01(Mathf.Pow(Mathf.Abs(c), 8f));
                    pixels[y * size + x] = new Color32((byte)(v * 255f), 0, 0, 255);
                }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }
    }
}
