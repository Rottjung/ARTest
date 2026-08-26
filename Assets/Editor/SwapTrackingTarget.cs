using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Trains a given source image and repoints the active scene's Image Tracking
    /// Target at the result. Only touches the Target field - does not recreate or move
    /// any other object (camera, content, etc.), so manual placement is preserved.
    /// </summary>
    public static class SwapTrackingTarget
    {
        private const string SourceImagePath = "Assets/images/kiosk_cropped.jpg";

        [MenuItem("ARReveal/Building Test/Retrain + Swap Target (kiosk_cropped)")]
        public static void Run()
        {
            RunInternal();
        }

        // Batch-mode-safe entry point - explicitly opens BuildingTest first.
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
                Debug.LogError("[SwapTrackingTarget] Training failed - target not changed.");
                return;
            }
            string zptFilename = Path.GetFileName(zptPath);

            var target = Object.FindFirstObjectByType<ZapparImageTrackingTarget>();
            if (target == null)
            {
                Debug.LogError("[SwapTrackingTarget] No ZapparImageTrackingTarget in the active scene.");
                return;
            }

            string previous = target.Target;
            target.Target = zptFilename;
            EditorUtility.SetDirty(target);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[SwapTrackingTarget] Target changed: " + previous + " -> " + zptFilename +
                " (scene: " + scene.name + ")");
        }
    }
}
