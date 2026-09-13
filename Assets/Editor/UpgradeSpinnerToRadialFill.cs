using UnityEditor;
using UnityEngine;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Upgrades the calibration screen's spinner (Page0_Calibration/Spinner)
    /// in Assets/Prefabs/Share.prefab from the old rotating gapped-ring "C"
    /// shape to a proper radial-fill loading ring - "a round loading bar
    /// that fills in a loop" per direct request. Only touches the sprite
    /// and Image fill settings on that one object - whatever position/size
    /// it was already hand-adjusted to is left completely untouched. Edits
    /// the PREFAB ASSET directly via
    /// PrefabUtility.LoadPrefabContents/SaveAsPrefabAsset - no scene needs
    /// to be open. Safe to re-run.
    /// </summary>
    public static class UpgradeSpinnerToRadialFill
    {
        private const string PrefabPath = "Assets/Prefabs/Share.prefab";

        [MenuItem("ARReveal/UI/Upgrade Spinner To Radial Fill")]
        public static void Run()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null)
            {
                Debug.LogError("[UpgradeSpinnerToRadialFill] Could not load " + PrefabPath);
                return;
            }

            try
            {
                var controller = root.GetComponent<ARShareController>();
                if (controller == null)
                {
                    Debug.LogError("[UpgradeSpinnerToRadialFill] " + PrefabPath + " has no ARShareController on its root.");
                    return;
                }

                controller.EditorUpgradeSpinnerToRadialFill();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
