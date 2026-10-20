using System.Collections.Generic;
using UnityEngine;

namespace SphereDescent
{
    /// <summary>Root of a generated world. Answers layer queries and cleans up its generated assets.</summary>
    public class LevelInstance : MonoBehaviour
    {
        public LevelSettings Settings { get; private set; }
        public MazeLayout Layout { get; private set; }
        public int World { get; private set; }
        public Vector3 SpawnPosition { get; private set; }
        public ZoneMap Zones { get; private set; }

        readonly List<Object> ownedAssets = new List<Object>();
        readonly Dictionary<int, List<Renderer>> shadowCasters = new Dictionary<int, List<Renderer>>();
        readonly Dictionary<int, (GameObject beam, int layer)> beams = new Dictionary<int, (GameObject, int)>();
        int beamSection = -2;
        int shadowLayer = -1;
        Renderer[][] layerRenderers;   // [layer] -> every renderer on that layer
        int visibleFocus = -2;

        public int LayerCount => Settings.ShellCount + 1;
        public float OuterRadius => Settings.shellRadii[0];

        internal void Init(LevelSettings settings, MazeLayout layout, int world, Vector3 spawn, ZoneMap zones)
        {
            Zones = zones;
            Settings = settings;
            Layout = layout;
            World = world;
            SpawnPosition = spawn;
        }

        internal T Own<T>(T asset) where T : Object
        {
            ownedAssets.Add(asset);
            return asset;
        }

        internal void RegisterShadowCaster(int layer, Renderer r)
        {
            if (!shadowCasters.TryGetValue(layer, out var list)) shadowCasters[layer] = list = new List<Renderer>();
            list.Add(r);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        internal void RegisterBeam(int section, int layer, GameObject beam)
        {
            beams[section] = (beam, layer);
            beam.SetActive(false);
        }

        /// <summary>
        /// Shows only the light beam over the given section's exit (its hole or boost pad), and only while its
        /// layer is the one in view: beams rise far past the (hidden) shells above, so from a layer higher up
        /// one would poke up through the floor. Beams are switched on and off as objects, so layer hiding,
        /// which toggles renderers, can't bring the others back.
        /// </summary>
        public void SetBeamSection(int section)
        {
            if (section == beamSection) return;
            beamSection = section;
            RefreshBeams();
        }

        void RefreshBeams()
        {
            foreach (var kv in beams)
                if (kv.Value.beam != null) kv.Value.beam.SetActive(kv.Key == beamSection && kv.Value.layer == visibleFocus);
        }

        /// <summary>
        /// Only the layer the player is on casts shadows. The sun shines straight through the shells, so
        /// otherwise the walls of every layer above would throw their shadows onto the one below.
        /// </summary>
        public void SetShadowLayer(int layer)
        {
            if (layer == shadowLayer) return;
            foreach (var kv in shadowCasters)
            {
                var mode = kv.Key == layer ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                foreach (var r in kv.Value) if (r != null) r.shadowCastingMode = mode;
            }
            shadowLayer = layer;
        }

        internal void RegisterLayers(Transform[] layerRoots)
        {
            layerRenderers = new Renderer[layerRoots.Length][];
            for (int i = 0; i < layerRoots.Length; i++)
                layerRenderers[i] = layerRoots[i].GetComponentsInChildren<Renderer>(true);
        }

        /// <summary>
        /// Draws only what can be seen from the given layer: that layer and the one below it (visible through
        /// holes). Everything above is hidden, ceiling included, so inner layers sit under a black sky.
        /// Colliders stay active, so physics is unaffected and layers reappear as soon as the player climbs back up.
        /// </summary>
        public void SetVisibleLayers(int focus)
        {
            if (layerRenderers == null || focus == visibleFocus) return;
            visibleFocus = focus;
            for (int layer = 0; layer < layerRenderers.Length; layer++)
            {
                bool full = layer == focus || layer == focus + 1;
                foreach (var r in layerRenderers[layer]) if (r != null) r.enabled = full;
            }
            RefreshBeams();
        }

        /// <summary>Layer index for a distance from the center: 0 = surface, ShellCount = core.</summary>
        public int LayerAt(float radius)
        {
            for (int i = 0; i < Settings.ShellCount; i++)
                if (radius > Settings.shellRadii[i] - Settings.shellThickness) return i;
            return Settings.ShellCount;
        }

        public string LayerName(int layer)
        {
            if (layer <= 0) return "Surface";
            if (layer >= Settings.ShellCount) return "Core";
            return $"Depth {layer}";
        }

        void OnDestroy()
        {
            foreach (var asset in ownedAssets)
            {
                if (asset == null) continue;
                if (Application.isPlaying) Destroy(asset);
                else DestroyImmediate(asset);
            }
            ownedAssets.Clear();
        }
    }
}
