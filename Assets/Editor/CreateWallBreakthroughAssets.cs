using UnityEditor;
using UnityEngine;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// One-off setup for the WallBreakthrough shader: creates the material asset, and
    /// optionally drops a standalone demo quad (with WallHoleEffect, OpenOnStart on)
    /// into the currently open scene so you can hit Play and see it without wiring it
    /// into a real burst point first.
    /// </summary>
    public static class CreateWallBreakthroughAssets
    {
        private const string ShaderName = "ARReveal/WallBreakthrough";
        private const string MaterialPath = "Assets/Materials/WallBreakthrough.mat";

        [MenuItem("ARReveal/Wall Hole/Create Material")]
        public static void CreateMaterial()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError("[CreateWallBreakthroughAssets] Could not find shader '" + ShaderName +
                    "'. Let Unity finish compiling (check the Console for shader errors) and try again.");
                return;
            }

            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null)
            {
                Debug.Log("[CreateWallBreakthroughAssets] " + MaterialPath + " already exists - leaving it as-is.");
                Selection.activeObject = existing;
                return;
            }

            var mat = new Material(shader);
            System.IO.Directory.CreateDirectory("Assets/Materials");
            AssetDatabase.CreateAsset(mat, MaterialPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[CreateWallBreakthroughAssets] Created " + MaterialPath);
            Selection.activeObject = mat;
        }

        [MenuItem("ARReveal/Wall Hole/Add Demo Quad To Active Scene")]
        public static void AddDemoQuad()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                Debug.LogError("[CreateWallBreakthroughAssets] " + MaterialPath +
                    " doesn't exist yet - run 'ARReveal/Wall Hole/Create Material' first.");
                return;
            }

            var quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadGo.name = "WallHoleDemo";
            Object.DestroyImmediate(quadGo.GetComponent<MeshCollider>());
            quadGo.transform.position = new Vector3(0, 1.5f, 0);
            quadGo.transform.localScale = Vector3.one * 2f;

            var renderer = quadGo.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;

            var effect = quadGo.AddComponent<WallHoleEffect>();
            effect.OpenOnStart = true;

            Selection.activeGameObject = quadGo;
            Debug.Log("[CreateWallBreakthroughAssets] Added WallHoleDemo to the scene. Press Play to see it open " +
                "(remember to save the scene if you want to keep it, or delete it before building).");
        }
    }
}
