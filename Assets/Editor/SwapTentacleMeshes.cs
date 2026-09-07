using ARReveal;
using UnityEditor;
using UnityEngine;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// One-shot tool: swaps the rigged mesh+skeleton under each Tentacle_XX wrapper
    /// prefab for a new one, per the mapping below - 01->05, 02 and 04->06, 03->07.
    /// Deliberately does NOT hand-edit any SkinnedMeshRenderer bone bindings - it
    /// only ever instantiates the new FBX's own already-imported prefab (whose
    /// skinning Unity's importer already wired up correctly against its own
    /// skeleton) as a fresh child, reads that child's own SkinnedMeshRenderer.
    /// rootBone (the same reference the old prefab's RootBone already pointed at,
    /// by the same convention) and re-points TentacleController.RootBone at it,
    /// then removes whatever mesh+rig child was there before. Nothing about the
    /// wrapper root itself (TentacleController's tuned field values, other
    /// components, Transform) is touched.
    ///
    /// Safe to re-run: it first strips ALL existing SkinnedMeshRenderer-bearing
    /// children (covers both the original old mesh and any earlier partial/
    /// duplicate attempt) before adding the one fresh instance, so re-running
    /// after a failed or partial pass just redoes the swap cleanly rather than
    /// stacking duplicates.
    /// </summary>
    public static class SwapTentacleMeshes
    {
        private const string NewTentacleDir = "Assets/FBX/Tentacle/New_Tentacles/New_Tentacle/";

        private static readonly (string prefabPath, string newFbxName)[] Mapping =
        {
            ("Assets/Prefabs/Tentacle_01.prefab", "Tentacle_05(Rigged).fbx"),
            ("Assets/Prefabs/Tentacle_02(Rigged).prefab", "Tentacle_06(Rigged).fbx"),
            ("Assets/Prefabs/Tentacle_03(Rigged).prefab", "Tentacle_07(Rigged).fbx"),
            ("Assets/Prefabs/Tentacle_04(Rigged).prefab", "Tentacle_06(Rigged).fbx"),
        };

        [MenuItem("Tools/ARReveal/Swap Tentacle Meshes (01->05, 02+04->06, 03->07)")]
        public static void SwapAll()
        {
            int done = 0;
            foreach (var (prefabPath, newFbxName) in Mapping)
            {
                if (SwapOne(prefabPath, NewTentacleDir + newFbxName))
                    done++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[SwapTentacleMeshes] Done - {done}/{Mapping.Length} prefabs swapped. Check the Console above for any per-prefab warnings (e.g. root bone not found).");
        }

        private static bool SwapOne(string prefabPath, string newFbxPath)
        {
            var newFbx = AssetDatabase.LoadAssetAtPath<GameObject>(newFbxPath);
            if (newFbx == null)
            {
                Debug.LogError($"[SwapTentacleMeshes] Could not load new FBX at '{newFbxPath}' - skipped '{prefabPath}'.");
                return false;
            }

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                Debug.LogError($"[SwapTentacleMeshes] Could not load prefab at '{prefabPath}' - skipped.");
                return false;
            }

            try
            {
                var controller = root.GetComponent<TentacleController>();
                if (controller == null)
                {
                    Debug.LogError($"[SwapTentacleMeshes] '{prefabPath}' has no TentacleController on its root - skipped, nothing changed.");
                    return false;
                }

                // Strip every existing mesh+rig child (old one, and any leftover
                // partial attempt) before adding the fresh one - see class doc.
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    var child = root.transform.GetChild(i);
                    if (child.GetComponentInChildren<SkinnedMeshRenderer>() != null)
                        Object.DestroyImmediate(child.gameObject);
                }

                var newInstance = (GameObject)PrefabUtility.InstantiatePrefab(newFbx, root.transform);
                newInstance.transform.localPosition = Vector3.zero;
                newInstance.transform.localRotation = Quaternion.identity;
                newInstance.transform.localScale = Vector3.one;

                var skin = newInstance.GetComponentInChildren<SkinnedMeshRenderer>();
                Transform newRootBone = skin != null ? skin.rootBone : null;
                if (newRootBone == null)
                {
                    Debug.LogWarning($"[SwapTentacleMeshes] '{prefabPath}': new rig '{newFbxPath}' has no SkinnedMeshRenderer.rootBone set - RootBone left unchanged, set it manually on the wrapper's TentacleController.");
                }
                else
                {
                    controller.RootBone = newRootBone;
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"[SwapTentacleMeshes] '{prefabPath}' now uses '{newFbxPath}'" + (newRootBone != null ? $", RootBone -> '{newRootBone.name}'." : " (RootBone unchanged - see warning above)."));
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
