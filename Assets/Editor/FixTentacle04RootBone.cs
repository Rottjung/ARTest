using UnityEditor;
using UnityEngine;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Tentacle_04(Rigged).prefab has an unassigned RootBone (confirmed via
    /// DiagnoseTentaclePrefabs) - with an empty bone chain, TentacleController.Update()
    /// bails out every frame, so the scale it zeros in Awake() (Style=Punch) never
    /// progresses, reading as "stuck invisible". The other three rigs all use a
    /// "Bone" -> "Bone.001" -> ... naming convention, so this searches for a child
    /// transform literally named "Bone" and wires it up the same way.
    /// </summary>
    public static class FixTentacle04RootBone
    {
        private const string PrefabPath = "Assets/Prefabs/Tentacle_04(Rigged).prefab";

        [MenuItem("ARReveal/Wall Hole/Fix Tentacle_04 RootBone")]
        public static void Run()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            var controller = root.GetComponent<TentacleController>();
            if (controller == null)
            {
                Debug.LogError("[FixTentacle04RootBone] No TentacleController on the prefab root.");
                PrefabUtility.UnloadPrefabContents(root);
                return;
            }

            if (controller.RootBone != null)
            {
                Debug.Log("[FixTentacle04RootBone] RootBone is already assigned (" + controller.RootBone.name + ") - leaving as-is.");
                PrefabUtility.UnloadPrefabContents(root);
                return;
            }

            Transform found = FindByName(root.transform, "Bone");
            if (found == null)
            {
                Debug.LogError("[FixTentacle04RootBone] Could not find any child transform named 'Bone' - " +
                    "this rig may use different bone naming, needs manual assignment in the Inspector.");
                PrefabUtility.UnloadPrefabContents(root);
                return;
            }

            controller.RootBone = found;
            string foundName = found.name; // capture before UnloadPrefabContents destroys it
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            PrefabUtility.UnloadPrefabContents(root);

            Debug.Log("[FixTentacle04RootBone] Assigned RootBone = '" + foundName + "' and saved.");
        }

        private static Transform FindByName(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var result = FindByName(t.GetChild(i), name);
                if (result != null) return result;
            }
            return null;
        }
    }
}
