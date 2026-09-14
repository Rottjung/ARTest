using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ARReveal;

namespace ARRevealEditor
{
    /// <summary>
    /// Adds a RealWorldSunLight component to the currently OPEN scene,
    /// auto-wiring it to that scene's own Directional Light - see
    /// RealWorldSunLight's own doc comment for what it does and why it's a
    /// custom script rather than a built-in Unity feature (URP has no
    /// equivalent to HDRP's "Physically Based Sky"). Safe to re-run -
    /// reuses the existing "Real World Sun" GameObject if one is already
    /// there instead of duplicating it.
    /// </summary>
    public static class AddRealWorldSunLight
    {
        [MenuItem("ARReveal/Add Real World Sun Light To Open Scene")]
        public static void Run()
        {
            var light = Object.FindFirstObjectByType<Light>();
            if (light == null || light.type != LightType.Directional)
            {
                light = null;
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l.type == LightType.Directional) { light = l; break; }
                }
            }
            if (light == null)
            {
                Debug.LogError("[AddRealWorldSunLight] No Directional Light found in the open scene.");
                return;
            }

            var go = GameObject.Find("Real World Sun");
            bool created = go == null;
            if (created)
            {
                go = new GameObject("Real World Sun");
                Undo.RegisterCreatedObjectUndo(go, "Add Real World Sun Light");
            }

            var sun = go.GetComponent<RealWorldSunLight>();
            if (sun == null) sun = go.AddComponent<RealWorldSunLight>();
            sun.DirectionalLight = light;
            EditorUtility.SetDirty(sun);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log("[AddRealWorldSunLight] " + (created ? "Created" : "Reused") + " 'Real World Sun', wired to '" +
                light.gameObject.name + "' in scene '" + scene.name + "'. Save the scene to keep this. " +
                "Remember to calibrate NorthOffsetDegrees on-site (see its own tooltip) - it defaults to an unverified 0.");
        }
    }
}
