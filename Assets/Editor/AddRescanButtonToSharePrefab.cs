using UnityEditor;
using UnityEngine;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Adds the bottom-center Rescan button directly into
    /// Assets/Prefabs/Share.prefab - same pattern as
    /// AddCalibrationScreenToSharePrefab.cs, for when the UI was already
    /// hand-tuned and saved as a prefab BEFORE the Rescan button existed, so
    /// a full ARReveal/UI/Build Share UI In Scene rebuild (which discards
    /// and rebuilds the whole ARShareCanvas from scratch) would wipe out
    /// that hand-tuning. Edits the PREFAB ASSET directly via
    /// PrefabUtility.LoadPrefabContents/SaveAsPrefabAsset - no scene needs
    /// to be open, and nothing else in the prefab is touched. Safe to
    /// re-run - ARShareController.EditorAddRescanButtonIfMissing() no-ops
    /// if a RescanButton is already there.
    /// </summary>
    public static class AddRescanButtonToSharePrefab
    {
        private const string PrefabPath = "Assets/Prefabs/Share.prefab";

        [MenuItem("ARReveal/UI/Add Rescan Button To Share Prefab")]
        public static void Run()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null)
            {
                Debug.LogError("[AddRescanButtonToSharePrefab] Could not load " + PrefabPath);
                return;
            }

            try
            {
                var controller = root.GetComponent<ARShareController>();
                if (controller == null)
                {
                    Debug.LogError("[AddRescanButtonToSharePrefab] " + PrefabPath + " has no ARShareController on its root.");
                    return;
                }

                controller.EditorAddRescanButtonIfMissing();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
