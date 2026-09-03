using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Trains Assets/Images/Logos.JPG - a cleaner, more fronto-parallel reshoot of the
    /// same panel+logos idea as IMG_8469 (UCI Luxe + Bowling World logos over the
    /// checkerboard cladding, L'Osteria text, no ad screens) - and swaps it in as the
    /// active UCI-RE tracking target. The second (corner) tracker has been removed for
    /// now, so this project is back to a single target.
    ///
    /// EstimatedPhysicalWidthMeters is a rough guess (~10m), same caveat as the
    /// previous targets - correct and re-run if a real measurement becomes available.
    /// </summary>
    public static class TrainLogosTarget
    {
        private const string SourceImagePath = "Assets/Images/Logos.JPG";
        private const float EstimatedPhysicalWidthMeters = 10f;

        [MenuItem("ARReveal/UCI-RE/Train + Swap to Logos Target")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/UCI-RE.unity", OpenSceneMode.Single);

            string fullImagePath = Path.GetFullPath(SourceImagePath);
            string zptPath = Phase1ImageTrain.TrainImage(fullImagePath, maxTrainSize: 1024,
                physicalWidthMeters: EstimatedPhysicalWidthMeters);
            if (zptPath == null)
            {
                Debug.LogError("[TrainLogosTarget] Training failed - aborting.");
                return;
            }
            string zptFilename = Path.GetFileName(zptPath);

            ZapparImageTrackingTarget target = null;
            foreach (var t in Object.FindObjectsByType<ZapparImageTrackingTarget>(FindObjectsSortMode.None))
            {
                if (!string.IsNullOrEmpty(t.Target)) { target = t; break; }
            }
            if (target == null)
            {
                Debug.LogError("[TrainLogosTarget] No ZapparImageTrackingTarget with a non-empty Target found.");
                return;
            }

            string previousTarget = target.Target;
            target.Target = zptFilename;
            EditorUtility.SetDirty(target);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[TrainLogosTarget] Done. Target switched from '" + previousTarget + "' to '" +
                zptFilename + "', calibrated at an ESTIMATED " + EstimatedPhysicalWidthMeters + "m wide " +
                "(correct EstimatedPhysicalWidthMeters and re-run if you get a real measurement). Proxy " +
                "position was left untouched - re-nudge it by eye against the new tracking preview.");
        }
    }
}
