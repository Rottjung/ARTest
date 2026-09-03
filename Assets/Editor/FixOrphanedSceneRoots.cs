using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// One-off fix: GameObjects created via GameObject.CreatePrimitive/new GameObject()
    /// from a batch-mode -executeMethod call can end up saved into the scene file
    /// without being registered in Unity 6's SceneRoots bookkeeping object, even though
    /// their data (and GameObject.Find) works fine - they're just invisible to
    /// GetRootGameObjects() and the Hierarchy. Re-parenting to null and explicitly
    /// calling SceneManager.MoveGameObjectToScene fixes the registration.
    /// </summary>
    public static class FixOrphanedSceneRoots
    {
        [MenuItem("ARReveal/Wall Hole/Fix Orphaned Scene Roots (TentacleTest)")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/TentacleTest.unity", OpenSceneMode.Single);

            var go = GameObject.Find("WallHoleDemo");
            if (go == null)
            {
                Debug.LogError("[FixOrphanedSceneRoots] WallHoleDemo not found at all.");
                return;
            }

            // MoveGameObjectToScene refuses objects Unity doesn't already consider a
            // root (which is exactly the broken bookkeeping we're fixing) - so instead,
            // parent it under a real registered root, then back out to null. Reparenting
            // through the normal Transform API (rather than however batch-mode's
            // CreatePrimitive+Save left it) is what re-registers it in SceneRoots.
            var anyRoot = scene.GetRootGameObjects()[0].transform;
            go.transform.SetParent(anyRoot, true);
            go.transform.SetParent(null, true);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            var recount = scene.GetRootGameObjects();
            Debug.Log("[FixOrphanedSceneRoots] Done. Root count is now " + recount.Length + ".");
        }
    }
}
