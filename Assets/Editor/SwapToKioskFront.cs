using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Trains KioskFront.jpg (straight-on, front-facing shot) at 1024px and swaps the
    /// active scene's tracking target to it. Does not delete kiosk_cropped.zpt, so it's
    /// easy to compare/revert. Only touches the Target field - camera, cup placement,
    /// jiggle, and debug overlay are untouched.
    /// </summary>
    public static class SwapToKioskFront
    {
        private const string SourceImagePath = "Assets/images/KioskFront.jpg";

        [MenuItem("ARReveal/Building Test/Train + Swap to KioskFront")]
        public static void Run()
        {
            string fullImagePath = Path.GetFullPath(SourceImagePath);
            string zptPath = Phase1ImageTrain.TrainImage(fullImagePath, maxTrainSize: 1024);
            if (zptPath == null)
            {
                Debug.LogError("[SwapToKioskFront] Training failed - target not changed.");
                return;
            }
            string zptFilename = Path.GetFileName(zptPath);

            ZapparImageTrackingTarget realTarget = null;
            foreach (var t in Object.FindObjectsByType<ZapparImageTrackingTarget>(FindObjectsSortMode.None))
            {
                if (!string.IsNullOrEmpty(t.Target)) { realTarget = t; break; }
            }
            if (realTarget == null)
            {
                Debug.LogError("[SwapToKioskFront] No ZapparImageTrackingTarget with a non-empty Target found.");
                return;
            }

            string previous = realTarget.Target;
            realTarget.Target = zptFilename;
            EditorUtility.SetDirty(realTarget);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[SwapToKioskFront] Target changed: " + previous + " -> " + zptFilename + " (scene: " + scene.name + ")");
        }
    }
}
