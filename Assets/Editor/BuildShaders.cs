using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace SphereDescent.EditorTools
{
    /// <summary>
    /// Makes sure builds carry everything the game sets up at runtime that the scene alone wouldn't pull in:
    /// every shader it looks up by name (to make materials in code), and fog (styles turn it on at runtime,
    /// while the scene has it off, so by default builds would strip fog out of every shader). In the editor
    /// everything is available, so all of this works in Play mode and only shows in a build. Runs before every
    /// build, and from the Sphere Descent menu.
    /// </summary>
    public class BuildShaders : IPreprocessBuildWithReport
    {
        /// <summary>Shaders found by name at runtime (see LevelMaterials and GameManager).</summary>
        static readonly string[] Names =
        {
            "Universal Render Pipeline/Particles/Unlit", // steam, sparks, trails, specks
            LevelMaterials.GridShaderName,
            LevelMaterials.BeamShaderName,
            LevelMaterials.SpotShaderName,
            LevelMaterials.AcidShaderName,
            "SphereDescent/SynthwaveSky",
        };

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => EnsureIncluded();

        /// <summary>Adds any of those shaders missing from the project's Always Included Shaders list, and keeps fog.</summary>
        [MenuItem("Sphere Descent/Include Runtime Shaders In Builds")]
        public static void EnsureIncluded()
        {
            var settings = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            var list = settings.FindProperty("m_AlwaysIncludedShaders");
            bool changed = false;
            foreach (var name in Names)
            {
                var shader = Shader.Find(name);
                if (shader == null) { Debug.LogError($"[BuildShaders] can't find shader {name}"); continue; }
                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) present = true;
                if (present) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                changed = true;
                Debug.Log($"[BuildShaders] always including {name}");
            }
            // Keep every fog mode rather than only the ones the scene uses (it uses none).
            var fogStripping = settings.FindProperty("m_FogStripping");
            if (fogStripping.intValue != 1)
            {
                fogStripping.intValue = 1; // custom
                settings.FindProperty("m_FogKeepLinear").boolValue = true;
                settings.FindProperty("m_FogKeepExp").boolValue = true;
                settings.FindProperty("m_FogKeepExp2").boolValue = true;
                changed = true;
                Debug.Log("[BuildShaders] keeping all fog modes in builds");
            }
            if (changed)
            {
                settings.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
            }
        }
    }
}
