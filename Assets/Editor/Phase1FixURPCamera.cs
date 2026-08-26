using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Fixes two things Zappar's own "Update Project/Scene For SRP" menu commands do
    /// together, which our headless scene setup missed:
    ///   1. ZAPPAR_SRP scripting define wasn't set - the package was running its
    ///      Built-in Render Pipeline code path in a URP project.
    ///   2. The URP camera stack wasn't configured - ZapparCamera must be an Overlay
    ///      camera stacked onto ZapparCameraBackground (Base), or URP won't composite
    ///      the passthrough feed with rendered content correctly.
    /// Reconfigures existing camera objects in place - does not touch the tracking
    /// target or its content, safe to run after Phase1Rehearsal.
    /// </summary>
    public static class Phase1FixURPCamera
    {
        [MenuItem("ARReveal/Fix/URP Camera Stack + ZAPPAR_SRP Define")]
        public static void Run()
        {
            const string define = "ZAPPAR_SRP";
            string symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.WebGL);
            if (!symbols.Contains(define))
            {
                symbols = string.IsNullOrEmpty(symbols) ? define : symbols + ";" + define;
                PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.WebGL, symbols);
                Debug.Log("[Phase1FixURPCamera] Added ZAPPAR_SRP define to WebGL - scripts will now recompile.");
            }
            else
            {
                Debug.Log("[Phase1FixURPCamera] ZAPPAR_SRP already defined.");
            }

            var content = Object.FindFirstObjectByType<ZapparCamera>();
            var background = Object.FindFirstObjectByType<ZapparCameraBackground>();
            if (content == null || background == null)
            {
                Debug.LogError("[Phase1FixURPCamera] Could not find ZapparCamera/ZapparCameraBackground in scene.");
                return;
            }

            var contentCam = content.GetComponent<Camera>();
            var bgCam = background.GetComponent<Camera>();

            var contentData = contentCam.GetUniversalAdditionalCameraData();
            contentData.renderType = CameraRenderType.Overlay;

            var bgData = bgCam.GetUniversalAdditionalCameraData();
            bgData.renderType = CameraRenderType.Base;
            bgData.renderShadows = false;
            bgCam.depth = -1;
            bgCam.useOcclusionCulling = false;
            bgData.cameraStack.Clear();
            bgData.cameraStack.Add(contentCam);

            EditorUtility.SetDirty(contentCam.gameObject);
            EditorUtility.SetDirty(bgCam.gameObject);

            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

            Debug.Log("[Phase1FixURPCamera] Done - " + background.name + " is now Base (stacking " +
                content.name + " as Overlay). Note: if the define was just added, let scripts finish " +
                "recompiling before rebuilding.");
        }
    }
}
