using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Trains MajimeLogo.jpg (cropped from the same photo as kiosk_cropped.jpg, just
    /// tighter) at 1024px and swaps the active scene's tracking target to it - an
    /// experiment comparing a smaller/simpler high-contrast target against the denser
    /// full kiosk crop. Does not delete kiosk_cropped.zpt, so it's easy to swap back.
    /// </summary>
    public static class SwapToLogoTarget
    {
        private const string SourceImagePath = "Assets/images/MajimeLogo.jpg";

        [MenuItem("ARReveal/Building Test/Train + Swap to MajimeLogo")]
        public static void Run()
        {
            RunInternal();
        }

        public static void RunOnBuildingTestScene()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/BuildingTest.unity", OpenSceneMode.Single);
            RunInternal();
        }

        private static void RunInternal()
        {
            string fullImagePath = Path.GetFullPath(SourceImagePath);
            string zptPath = Phase1ImageTrain.TrainImage(fullImagePath, maxTrainSize: 1024);
            if (zptPath == null)
            {
                Debug.LogError("[SwapToLogoTarget] Training failed - target not changed.");
                return;
            }
            string zptFilename = Path.GetFileName(zptPath);

            var target = Object.FindFirstObjectByType<ZapparImageTrackingTarget>();
            if (target == null)
            {
                Debug.LogError("[SwapToLogoTarget] No ZapparImageTrackingTarget in the active scene.");
                return;
            }

            string previous = target.Target;
            target.Target = zptFilename;
            EditorUtility.SetDirty(target);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[SwapToLogoTarget] Target changed: " + previous + " -> " + zptFilename +
                " (scene: " + scene.name + "). Orientation left as-is (should already be Vertical).");
        }
    }
}
