using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Trains Assets/Images/IMG_8439.JPG (the tighter "corner" crop - glass tower +
    /// UCI/Bowling World logos, the client-requested corner view, similar spirit to the
    /// original LeftCornerUCI.jpg but screen-free) and adds it as a SECOND, simultaneous
    /// ZapparImageTrackingTarget alongside the existing IMG_8469 one - each
    /// ZapparImageTrackingTarget runs its own independent tracker under the same
    /// pipeline (confirmed against Zappar's own SDK source), so both can be tracked at
    /// once without replacing each other.
    ///
    /// EstimatedPhysicalWidthMeters is a rough guess, same caveat as IMG_8469's -
    /// correct and re-run if a real measurement becomes available.
    ///
    /// This only wires up the tracking target itself - no proxy/wall-hole/tentacle
    /// content is attached yet, that's a follow-up once tracking on both is confirmed.
    /// </summary>
    public static class AddCornerImageTarget
    {
        private const string SourceImagePath = "Assets/Images/IMG_8439.JPG";
        private const float EstimatedPhysicalWidthMeters = 11f;
        private const string NewTargetName = "Zappar Image Tracking Target (Corner)";

        [MenuItem("ARReveal/UCI-RE/Train + Add IMG_8439 as Second Target")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/UCI-RE.unity", OpenSceneMode.Single);

            if (GameObject.Find(NewTargetName) != null)
            {
                Debug.Log("[AddCornerImageTarget] '" + NewTargetName + "' already exists - leaving it as-is.");
                return;
            }

            string fullImagePath = Path.GetFullPath(SourceImagePath);
            string zptPath = Phase1ImageTrain.TrainImage(fullImagePath, maxTrainSize: 1024,
                physicalWidthMeters: EstimatedPhysicalWidthMeters);
            if (zptPath == null)
            {
                Debug.LogError("[AddCornerImageTarget] Training failed - aborting.");
                return;
            }
            string zptFilename = Path.GetFileName(zptPath);

            var go = new GameObject(NewTargetName);
            var target = go.AddComponent<ZapparImageTrackingTarget>();
            target.Target = zptFilename;
            target.Orientation = ZapparImageTrackingTarget.PlaneOrientation.Vertical; // matches the existing target
            go.AddComponent<TrackingDebugOverlay>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[AddCornerImageTarget] Done. Added '" + NewTargetName + "' with Target=" + zptFilename +
                ", calibrated at an ESTIMATED " + EstimatedPhysicalWidthMeters + "m wide. Both targets " +
                "(IMG_8469 + IMG_8439) are now active simultaneously - no content attached to the new one yet.");
        }
    }
}
