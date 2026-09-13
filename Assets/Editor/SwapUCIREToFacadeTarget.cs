using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Repoints UCI-RE.unity's ZapparImageTrackingTarget from the ground-corner
    /// crop (LeftCornerUCI_cropped.zpt) to the facade crop (BuildingFacade.zpt,
    /// already retrained at an estimated 23m real height - see that commit),
    /// per direct request. Only touches the Target field and Orientation - does
    /// NOT reposition UCI_RE_BuildingContent.
    ///
    /// THAT reposition is NOT done here on purpose: UCI_RE_BuildingContent's
    /// current placement was set assuming the GROUND-CORNER crop's own local
    /// origin (the "bottom-left-front-ground corner" convention from
    /// CalibrateAndSwapProxy/UCIREContentPort) - a totally different reference
    /// frame than this facade crop (a portrait, roughly-mid-height wall shot).
    /// Switching the tracked image alone will very likely leave the content
    /// floating in the wrong place until it's re-positioned by eye against the
    /// new tracking preview, same "nudge by eye" caveat those other tools
    /// already carry.
    /// </summary>
    public static class SwapUCIREToFacadeTarget
    {
        private const string ScenePath = "Assets/Scenes/UCI-RE.unity";
        private const string ZptFilename = "BuildingFacade.zpt";

        [MenuItem("ARReveal/UCI-RE/Swap Target To BuildingFacade (Safety Backup)")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var target = Object.FindFirstObjectByType<ZapparImageTrackingTarget>();
            if (target == null)
            {
                Debug.LogError("[SwapUCIREToFacadeTarget] No ZapparImageTrackingTarget found in " + ScenePath);
                return;
            }

            string previous = target.Target;
            target.Target = ZptFilename;
            // Vertical, not Flat - this is a wall crop, not a ground-lying marker
            // (same reasoning the deleted SetupBuildingFacadeReanchor.cs used for
            // this exact image).
            target.Orientation = ZapparImageTrackingTarget.PlaneOrientation.Vertical;
            EditorUtility.SetDirty(target);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[SwapUCIREToFacadeTarget] Target changed: " + previous + " -> " + ZptFilename +
                ". UCI_RE_BuildingContent's position was NOT touched - it will very likely need " +
                "repositioning by eye against the new tracking preview, since it was placed assuming " +
                "the old ground-corner crop's own local origin, not this facade crop's.");
        }
    }
}
