using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Adds a TargetPreloader component to the currently OPEN scene - see
    /// that class's own doc comment for the real bug it fixes (a cold
    /// browser cache losing the race between Zappar's own async .zpt fetch
    /// and the user actually scanning). Works on whichever scene is open
    /// (UCI-RE-AR, UCI-RE, or any of their backup copies), auto-wiring it
    /// to that scene's own ZapparImageTrackingTarget - no scene-specific
    /// hardcoding, so it's reusable across all of them. Safe to re-run -
    /// reuses the existing "Target Preloader" GameObject if one is already
    /// there instead of duplicating it.
    /// </summary>
    public static class AddTargetPreloader
    {
        [MenuItem("ARReveal/Add Target Preloader To Open Scene")]
        public static void Run()
        {
            var target = Object.FindFirstObjectByType<ZapparImageTrackingTarget>();
            if (target == null)
            {
                Debug.LogError("[AddTargetPreloader] No ZapparImageTrackingTarget found in the open scene.");
                return;
            }

            var go = GameObject.Find("Target Preloader");
            bool created = go == null;
            if (created)
            {
                go = new GameObject("Target Preloader");
                Undo.RegisterCreatedObjectUndo(go, "Add Target Preloader");
            }

            var preloader = go.GetComponent<TargetPreloader>();
            if (preloader == null) preloader = go.AddComponent<TargetPreloader>();
            preloader.ImageTarget = target;
            EditorUtility.SetDirty(preloader);

            // Also wire it into ARShareController.Preloader if that's present
            // in this scene, so the calibration screen's own "Preparing..."
            // vs "Scan the QR" message actually has something to check.
            var shareController = Object.FindFirstObjectByType<ARShareController>();
            if (shareController != null && shareController.Preloader == null)
            {
                shareController.Preloader = preloader;
                EditorUtility.SetDirty(shareController);
            }

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log("[AddTargetPreloader] " + (created ? "Created" : "Reused") + " 'Target Preloader', wired to '" +
                target.gameObject.name + "' (Target: " + target.Target + ") in scene '" + scene.name + "'. Save the scene to keep this.");
        }
    }
}
