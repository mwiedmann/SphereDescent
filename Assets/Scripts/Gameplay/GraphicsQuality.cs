using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SphereDescent
{
    public enum GraphicsLevel { Low, Medium, High }

    /// <summary>
    /// The graphics quality setting (Low, Medium or High, remembered between launches), for running well on
    /// slower machines. It works on the game's own copies of the render pipeline settings and of the
    /// post-processing, so the project's settings files are never changed:
    ///
    ///   High:   everything at full quality.
    ///   Medium: drawn at 67% resolution and upscaled (FSR), no anti-aliasing, two shadow cascades, standard
    ///           bloom.
    ///   Low:    drawn at 50% resolution and upscaled, no anti-aliasing, hard shadows over a shorter range in
    ///           one cascade, reduced bloom, and the heaviest shader effects simplified (fewer acid and lava
    ///           bubbles, lighter nebula clouds), with fewer drifting specks.
    /// </summary>
    public static class GraphicsQuality
    {
        const string PrefKey = "graphicsQuality";

        public static GraphicsLevel Level { get; private set; } = GraphicsLevel.High;

        static UniversalRenderPipelineAsset original, copy;
        static RenderPipelineAsset qualityPipeline; // the quality level's own setting before the copy took over (often none)

        /// <summary>The saved level (High the first time).</summary>
        public static GraphicsLevel Saved
        {
            get
            {
                try { return (GraphicsLevel)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, (int)GraphicsLevel.High), 0, 2); }
                catch { return GraphicsLevel.High; }
            }
        }

        /// <summary>
        /// Applies a level now and remembers it. <paramref name="sun"/> gets hard or soft shadows,
        /// <paramref name="post"/> (the scene's post-processing volume) has its bloom adjusted.
        /// </summary>
        public static void Apply(GraphicsLevel level, Light sun, Volume post)
        {
            Level = level;
            PlayerPrefs.SetInt(PrefKey, (int)level);

            var pipeline = OwnPipeline();
            if (pipeline != null)
            {
                var source = original;
                pipeline.renderScale = level == GraphicsLevel.High ? 1f : level == GraphicsLevel.Medium ? 0.67f : 0.5f;
                pipeline.upscalingFilter = level == GraphicsLevel.High ? UpscalingFilterSelection.Auto : UpscalingFilterSelection.FSR;
                pipeline.msaaSampleCount = level == GraphicsLevel.High ? source.msaaSampleCount : 1;
                pipeline.shadowCascadeCount = level == GraphicsLevel.High ? source.shadowCascadeCount : level == GraphicsLevel.Medium ? 2 : 1;
                pipeline.shadowDistance = level == GraphicsLevel.Low ? Mathf.Min(35f, source.shadowDistance) : source.shadowDistance;
                pipeline.mainLightShadowmapResolution = level == GraphicsLevel.Low ? 1024 : source.mainLightShadowmapResolution;
            }
            if (sun != null) sun.shadows = level == GraphicsLevel.Low ? LightShadows.Hard : LightShadows.Soft;
            // volume.profile (not sharedProfile) is the scene's own copy, so the asset stays as it is.
            if (post != null && post.profile.TryGet(out Bloom bloom))
            {
                bloom.highQualityFiltering.Override(level == GraphicsLevel.High);
                bloom.downscale.Override(level == GraphicsLevel.Low ? BloomDownscaleMode.Quarter : BloomDownscaleMode.Half);
                bloom.maxIterations.Override(level == GraphicsLevel.Low ? 4 : 6);
            }
            // For shaders: 0 = low, 1 = medium, 2 = high.
            Shader.SetGlobalFloat("_GraphicsQuality", (float)level);
        }

        /// <summary>
        /// The game's own copy of the render pipeline settings, made (and switched to) the first time, so tuning
        /// it never touches the asset (which in the editor would stay changed after Play mode).
        /// </summary>
        static UniversalRenderPipelineAsset OwnPipeline()
        {
            if (copy != null) return copy;
            qualityPipeline = QualitySettings.renderPipeline;
            original = (qualityPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            if (original == null) return null;
            copy = Object.Instantiate(original);
            copy.name = original.name + " (game)";
            QualitySettings.renderPipeline = copy;
            return copy;
        }

        /// <summary>Goes back to the project's own render pipeline settings (when the game shuts down, in the editor).</summary>
        public static void Restore()
        {
            if (copy == null) return;
            if (QualitySettings.renderPipeline == copy) QualitySettings.renderPipeline = qualityPipeline;
            Object.Destroy(copy);
            copy = null;
        }

        public static string Name(GraphicsLevel level) => level == GraphicsLevel.Medium ? "Medium" : level.ToString();
    }
}
