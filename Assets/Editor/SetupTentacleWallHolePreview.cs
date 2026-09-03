using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// One-off batch-mode setup: opens TentacleTest.unity and adds a wall-hole decal +
    /// debris ring co-located with the existing Tentacle_01 (which already auto-grows
    /// on Play via TentacleGrowOnStart), so pressing Play shows the full combo -
    /// spiderweb flash, hole opening, tentacle emerging, debris settling - together.
    /// </summary>
    public static class SetupTentacleWallHolePreview
    {
        private const string ScenePath = "Assets/Scenes/TentacleTest.unity";
        private const string HoleMaterialPath = "Assets/Materials/WallBreakthrough.mat";
        private const string DebrisMaterialPath = "Assets/Materials/DebrisChunk.mat";

        [MenuItem("ARReveal/Wall Hole/Setup TentacleTest Preview")]
        public static void Run()
        {
            CreateWallBreakthroughAssets.CreateMaterial();
            CreateWallBreakthroughAssets.CreateDebrisMaterial();

            var holeMat = AssetDatabase.LoadAssetAtPath<Material>(HoleMaterialPath);
            var debrisMat = AssetDatabase.LoadAssetAtPath<Material>(DebrisMaterialPath);
            if (holeMat == null || debrisMat == null)
            {
                Debug.LogError("[SetupTentacleWallHolePreview] Materials missing after creation attempt - aborting.");
                return;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var existing = GameObject.Find("WallHoleDemo");
            if (existing != null)
            {
                Debug.Log("[SetupTentacleWallHolePreview] WallHoleDemo already present in the scene - leaving it as-is.");
                return;
            }

            var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadGo.name = "WallHoleDemo";
            Object.DestroyImmediate(quadGo.GetComponent<MeshCollider>());
            // Co-located with Tentacle_01 (at the world origin), a touch behind it in
            // Z and centered near its base height, facing the camera the same way the
            // default Unity Quad does (-Z), matching this scene's camera at (0,1.2,-5).
            quadGo.transform.position = new Vector3(0f, 0.9f, 0.25f);
            quadGo.transform.localScale = Vector3.one * 2.5f;

            var renderer = quadGo.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = holeMat;

            var effect = quadGo.AddComponent<WallHoleEffect>();
            effect.OpenOnStart = true;
            effect.OpenOnStartDelay = 0.5f; // matches Tentacle_01's TentacleGrowOnStart delay

            var debrisGo = new GameObject("DebrisRing");
            debrisGo.transform.SetParent(quadGo.transform, false);
            var ring = debrisGo.AddComponent<DebrisRing>();
            ring.ChunkMaterial = debrisMat;
            ring.OpenOnStart = true;
            ring.OpenOnStartDelay = effect.OpenOnStartDelay + effect.OpenDuration * 0.8f;

            // Batch-mode quirk: a brand-new root GameObject created via script and
            // saved with EditorSceneManager.SaveScene can end up missing from Unity 6's
            // SceneRoots bookkeeping (data's in the file, but GetRootGameObjects/the
            // Hierarchy don't see it). Reparenting through a real existing root and back
            // to null re-registers it correctly.
            var existingRoot = scene.GetRootGameObjects()[0].transform;
            quadGo.transform.SetParent(existingRoot, true);
            quadGo.transform.SetParent(null, true);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[SetupTentacleWallHolePreview] Done. Open TentacleTest.unity and press Play - " +
                "flash, hole, tentacle and debris should all fire together, staggered off Tentacle_01's own timing.");
        }
    }
}
