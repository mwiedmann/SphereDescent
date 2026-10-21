using UnityEngine;

namespace SphereDescent
{
    /// <summary>
    /// A mortar bomb arcing from the barrel to where it will land, with a red light blinking on it and a red
    /// marker on the ground beneath its landing spot that flashes faster and faster until it hits.
    /// </summary>
    public class MortarBomb : MonoBehaviour
    {
        Mortar mortar;
        Vector3 from, to;
        float age, arcHeight;
        Transform marker, blinker;
        Material markerMat;
        float markerIntensity;

        /// <param name="ground">The ground's normal where it will land, so the marker lies flat on a slope.</param>
        public static void Launch(Mortar mortar, Vector3 from, Vector3 to, Vector3 ground)
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

            // The marker sits just above the ground, lying on it, as wide as the blast.
            bomb.markerMat = new Material(mortar.markerMaterial);
            bomb.markerIntensity = bomb.markerMat.GetFloat("_Intensity");
            bomb.marker = Piece(mortar.bombParent, "Bomb Marker", mortar.markerMesh, bomb.markerMat);
            bomb.marker.SetPositionAndRotation(to + ground * 0.05f, Quaternion.FromToRotation(Vector3.up, ground));
            bomb.marker.localScale = Vector3.one * mortar.blastRadius;
            bomb.Tick(0f);
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
                if (marker != null) Destroy(marker.gameObject);
                if (markerMat != null) Destroy(markerMat);
                Destroy(gameObject);
            }
        }

        void OnDestroy()
        {
            if (marker != null) Destroy(marker.gameObject);
            if (markerMat != null) Destroy(markerMat);
        }
    }
}
