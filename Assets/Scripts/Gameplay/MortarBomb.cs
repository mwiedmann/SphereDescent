using System.Collections.Generic;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// A mortar bomb arcing from the barrel to where it will land, with a red light blinking on it and a red
    /// marker on the ground beneath its landing spot that flashes faster and faster until it hits. The marker
    /// is draped over the ground, so it follows hills instead of cutting into them.
    /// </summary>
    public class MortarBomb : MonoBehaviour
    {
        Mortar mortar;
        Vector3 from, to;
        float age, arcHeight;
        Transform marker, blinker;
        Material markerMat;
        Mesh markerMesh;
        float markerIntensity;

        /// <param name="ground">The ground's normal where it will land, so the marker lies flat on a slope.</param>
        public static MortarBomb Launch(Mortar mortar, Vector3 from, Vector3 to, Vector3 ground)
        {
            var go = new GameObject("Mortar Bomb");
            go.transform.SetParent(mortar.bombParent, true);
            var bomb = go.AddComponent<MortarBomb>();
            bomb.mortar = mortar;
            bomb.from = from;
            bomb.to = to;
            bomb.arcHeight = 5f + Vector3.Distance(from, to) * 0.35f;

            var body = Piece(go.transform, "Body", mortar.bombMesh, mortar.bombMaterial);
            body.localScale = Vector3.one * 0.55f;
            bomb.blinker = Piece(go.transform, "Light", mortar.bombMesh, mortar.bombLightMaterial);
            bomb.blinker.localPosition = new Vector3(0f, 0.22f, 0f);

            // The marker lies on the ground, as wide as the blast.
            bomb.markerMat = new Material(mortar.markerMaterial);
            bomb.markerIntensity = bomb.markerMat.GetFloat("_Intensity");
            bomb.markerMesh = DrapedDisc(mortar.GroundHit, to, ground, mortar.blastRadius);
            bomb.marker = Piece(mortar.bombParent, "Bomb Marker", bomb.markerMesh, bomb.markerMat);
            bomb.marker.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            bomb.Tick(0f);
            return bomb;
        }

        /// <summary>Moves the bomb on by <paramref name="seconds"/> of its flight, for still renders.</summary>
        public void Advance(float seconds) => Tick(seconds);

        /// <summary>Finds the solid ground straight down from a point, within a distance.</summary>
        public delegate bool GroundProbe(Vector3 from, float length, out Vector3 point, out Vector3 normal);

        /// <summary>
        /// A disc of <paramref name="radius"/> around <paramref name="center"/>, each of its points dropped onto
        /// the ground below (found by <paramref name="ground"/>) and lifted just off it, in world space. Rays start a little above the landing spot,
        /// below the wall tops, so where the disc runs into a wall it tucks under it (out of sight) rather than
        /// climbing it. UVs match a flat disc's, so the marker glows the same.
        /// </summary>
        public static Mesh DrapedDisc(GroundProbe ground, Vector3 center, Vector3 normal, float radius)
        {
            const int rings = 8, segments = 36;
            const float lift = 0.06f;
            Vector3 up = center.normalized;
            Vector3 t1 = Vector3.Cross(normal, Mathf.Abs(Vector3.Dot(normal, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 t2 = Vector3.Cross(normal, t1);
            var verts = new List<Vector3> { center + normal * lift };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            for (int i = 1; i <= rings; i++)
                for (int j = 0; j < segments; j++)
                {
                    float a = j / (float)segments * Mathf.PI * 2f, f = i / (float)rings;
                    Vector3 flat = center + (t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a)) * (radius * f);
                    Vector3 p = flat + normal * lift;
                    if (ground(flat + up * 2.2f, 5f, out Vector3 hit, out Vector3 hitNormal)) p = hit + hitNormal * lift;
                    verts.Add(p);
                    uvs.Add(new Vector2(0.5f + 0.5f * f * Mathf.Cos(a), 0.5f + 0.5f * f * Mathf.Sin(a)));
                }
            int V(int ring, int seg) => ring == 0 ? 0 : 1 + (ring - 1) * segments + (seg % segments);
            var tris = new List<int>();
            void Tri(int a, int b, int c)
            {
                // Face away from the ground.
                if (Vector3.Dot(Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]), normal) >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
                else { tris.Add(a); tris.Add(c); tris.Add(b); }
            }
            for (int j = 0; j < segments; j++) Tri(0, V(1, j), V(1, j + 1));
            for (int i = 1; i < rings; i++)
                for (int j = 0; j < segments; j++)
                {
                    Tri(V(i, j), V(i + 1, j), V(i + 1, j + 1));
                    Tri(V(i, j), V(i + 1, j + 1), V(i, j + 1));
                }
            var mesh = new Mesh { name = "Bomb Marker" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Transform Piece(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        void Update() => Tick(Time.deltaTime);

        void Tick(float dt)
        {
            age += dt;
            float flight = Mathf.Max(0.1f, mortar != null ? mortar.flightTime : 1f);
            float s = Mathf.Clamp01(age / flight);
            // Along the sphere from barrel to target, lifted into a high lob.
            Vector3 along = Vector3.Slerp(from, to, s);
            transform.position = along + along.normalized * (arcHeight * 4f * s * (1f - s));
            transform.rotation = Quaternion.FromToRotation(Vector3.up, along.normalized);

            // Blinks and flashes speed up as it comes down.
            float rate = Mathf.Lerp(3f, 14f, s * s);
            bool on = Mathf.Repeat(age * rate, 1f) < 0.5f;
            // The light swells and shrinks rather than going out, so the bomb never vanishes against a dark sky.
            if (blinker != null) blinker.localScale = Vector3.one * (on ? 0.4f : 0.22f);
            if (markerMat != null) markerMat.SetFloat("_Intensity", markerIntensity * (on ? 1f : 0.25f));

            if (s >= 1f)
            {
                if (mortar != null) mortar.Impact(to);
                Destroy(gameObject);
            }
        }

        void OnDestroy()
        {
            if (marker != null) Destroy(marker.gameObject);
            if (markerMat != null) Destroy(markerMat);
            if (markerMesh != null) Destroy(markerMesh);
        }
    }
}
