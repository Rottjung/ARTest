using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Minimal, scoped swap: retrains Assets/Images/AR-RE-QR_Code.png (the client's
    /// actual QR, replacing the placeholder GroundMarker.png generated earlier) and
    /// updates ONLY the existing 'Zappar Ground Marker' target's Target field in
    /// UCI-RE-Marker.unity. Does not touch the camera rig, the placeholder cube, its
    /// offset, or the RevealOnTrackingFound wiring - everything else in the scene stays
    /// exactly as-is.
    /// </summary>
    public static class SwapMarkerImage
    {
        private const string SourceImagePath = "Assets/Images/AR-RE-QR_Code.png";
        private const float MarkerPhysicalWidthMeters = 0.5f; // exact - same 50x50cm as before
        private const string MarkerObjectName = "Zappar Ground Marker";

        [MenuItem("ARReveal/Marker Test/Swap To Real QR (AR-RE-QR_Code)")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/UCI-RE-Marker.unity", OpenSceneMode.Single);

            string fullImagePath = Path.GetFullPath(SourceImagePath);
            string zptPath = Phase1ImageTrain.TrainImage(fullImagePath, maxTrainSize: 1024,
                physicalWidthMeters: MarkerPhysicalWidthMeters);
            if (zptPath == null)
            {
                Debug.LogError("[SwapMarkerImage] Training failed - aborting, nothing changed.");
                return;
            }
            string zptFilename = Path.GetFileName(zptPath);

            var markerGo = GameObject.Find(MarkerObjectName);
            if (markerGo == null)
            {
                Debug.LogError("[SwapMarkerImage] Could not find '" + MarkerObjectName + "' in the scene.");
                return;
            }
            var target = markerGo.GetComponent<ZapparImageTrackingTarget>();
            string previousTarget = target.Target;
            target.Target = zptFilename;
            EditorUtility.SetDirty(target);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[SwapMarkerImage] Done. '" + MarkerObjectName + "'.Target switched from '" +
                previousTarget + "' to '" + zptFilename + "'. Nothing else in the scene was touched.");
        }
    }
}
