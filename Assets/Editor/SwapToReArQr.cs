using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Same pattern as SwapMarkerImage.cs (that one for UCI-RE-Marker.unity) - trains
    /// Assets/Images/RE_AR_QR.jpg (the new QR from the client's own Zappar project)
    /// and updates ONLY the 'Zappar Ground Marker' target's Target field in
    /// UCI-RE-AR.unity. Doesn't touch anything else in the scene.
    /// </summary>
    public static class SwapToReArQr
    {
        private const string SourceImagePath = "Assets/Images/RE_AR_QR.jpg";
        private const float MarkerPhysicalWidthMeters = 0.5f; // same 50x50cm ground marker convention as SwapMarkerImage
        private const string MarkerObjectName = "Zappar Ground Marker";

        [MenuItem("ARReveal/UCI-RE-AR/Swap To Real QR (RE_AR_QR)")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/UCI-RE-AR.unity", OpenSceneMode.Single);

            string fullImagePath = Path.GetFullPath(SourceImagePath);
            string zptPath = Phase1ImageTrain.TrainImage(fullImagePath, maxTrainSize: 1024,
                physicalWidthMeters: MarkerPhysicalWidthMeters);
            if (zptPath == null)
            {
                Debug.LogError("[SwapToReArQr] Training failed - aborting, nothing changed.");
                return;
            }
            string zptFilename = Path.GetFileName(zptPath);

            var markerGo = GameObject.Find(MarkerObjectName);
            if (markerGo == null)
            {
                Debug.LogError("[SwapToReArQr] Could not find '" + MarkerObjectName + "' in the scene.");
                return;
            }
            var target = markerGo.GetComponent<ZapparImageTrackingTarget>();
            string previousTarget = target.Target;
            target.Target = zptFilename;
            EditorUtility.SetDirty(target);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[SwapToReArQr] Done. '" + MarkerObjectName + "'.Target switched from '" +
                previousTarget + "' to '" + zptFilename + "'. Nothing else in the scene was touched.");
        }
    }
}
