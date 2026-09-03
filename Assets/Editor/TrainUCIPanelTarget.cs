using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Trains Assets/Images/IMG_8469.JPG - the "Bowling World" checkerboard-panel crop
    /// picked from a batch of 25 new site photos (see conversation): dense unique
    /// features (two logos + "L'Osteria" text) over the repetitive glass-tower grid, no
    /// dynamic ad screens in frame, minimal foreground clutter - and swaps it in as the
    /// active UCI-RE tracking target, replacing whichever image didn't work on-site.
    ///
    /// EstimatedPhysicalWidthMeters is a ROUGH GUESS (~12m), based on how much of the
    /// facade this crop seems to cover next to the wider reference shots - not an actual
    /// measurement. Correct it and re-run if a real number becomes available; getting it
    /// wrong doesn't break detection, it just changes what "1 meter" means in the
    /// tracked space, which affects proxy scale.
    ///
    /// Deliberately does NOT touch the proxy - switching to a different, differently-
    /// framed target likely means the already-eyeballed proxy position needs re-nudging
    /// against the new tracking preview regardless of what this script does.
    /// </summary>
    public static class TrainUCIPanelTarget
    {
        private const string SourceImagePath = "Assets/Images/IMG_8469.JPG";
        private const float EstimatedPhysicalWidthMeters = 12f;

        [MenuItem("ARReveal/UCI-RE/Train + Swap to IMG_8469 Target")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/UCI-RE.unity", OpenSceneMode.Single);

            string fullImagePath = Path.GetFullPath(SourceImagePath);
            string zptPath = Phase1ImageTrain.TrainImage(fullImagePath, maxTrainSize: 1024,
                physicalWidthMeters: EstimatedPhysicalWidthMeters);
            if (zptPath == null)
            {
                Debug.LogError("[TrainUCIPanelTarget] Training failed - aborting.");
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
                Debug.LogError("[TrainUCIPanelTarget] No ZapparImageTrackingTarget with a non-empty Target found.");
                return;
            }

            string previousTarget = target.Target;
            target.Target = zptFilename;
            EditorUtility.SetDirty(target);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[TrainUCIPanelTarget] Done. Target switched from '" + previousTarget + "' to '" +
                zptFilename + "', calibrated at an ESTIMATED " + EstimatedPhysicalWidthMeters + "m wide " +
                "(correct EstimatedPhysicalWidthMeters and re-run if you get a real measurement). Proxy " +
                "position was left untouched - re-nudge it by eye against the new tracking preview.");
        }
    }
}
