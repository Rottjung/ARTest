using UnityEditor;
using UnityEngine;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Adds the calibration screen (Page0_Calibration) directly into
    /// Assets/Prefabs/Share.prefab - for when the UI was already hand-tuned
    /// and saved as a prefab BEFORE the calibration screen existed, so a
    /// full ARReveal/UI/Build Share UI In Scene rebuild (which discards and
    /// rebuilds the whole ARShareCanvas from scratch) would wipe out that
    /// hand-tuning. Edits the PREFAB ASSET directly via
    /// PrefabUtility.LoadPrefabContents/SaveAsPrefabAsset - no scene needs
    /// to be open, and nothing else in the prefab is touched. Safe to
    /// re-run - ARShareController.EditorAddCalibrationScreenIfMissing()
    /// no-ops if Page0_Calibration is already there.
    /// </summary>
    public static class AddCalibrationScreenToSharePrefab
    {
        private const string PrefabPath = "Assets/Prefabs/Share.prefab";

        [MenuItem("ARReveal/UI/Add Calibration Screen To Share Prefab")]
        public static void Run()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null)
            {
                Debug.LogError("[AddCalibrationScreenToSharePrefab] Could not load " + PrefabPath);
                return;
            }

            try
            {
                var controller = root.GetComponent<ARShareController>();
                if (controller == null)
                {
                    Debug.LogError("[AddCalibrationScreenToSharePrefab] " + PrefabPath + " has no ARShareController on its root.");
                    return;
                }

                controller.EditorAddCalibrationScreenIfMissing();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
