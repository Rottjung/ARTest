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
        private const string DebrisMaterialPath = "Assets/Materials/DebrisChunk.mat";

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

        /// <summary>
        /// Grey, rough concrete/plaster look for the procedural DebrisChunk meshes -
        /// a plain URP/Lit material, no custom shader needed for these. Per-chunk
        /// brightness variation is applied at spawn time by DebrisRing via a
        /// MaterialPropertyBlock, so this is just the base tone.
        /// </summary>
        [MenuItem("ARReveal/Wall Hole/Create Debris Material")]
        public static void CreateDebrisMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[CreateWallBreakthroughAssets] Could not find the URP/Lit shader.");
                return;
            }

            var existing = AssetDatabase.LoadAssetAtPath<Material>(DebrisMaterialPath);
            if (existing != null)
            {
                Debug.Log("[CreateWallBreakthroughAssets] " + DebrisMaterialPath + " already exists - leaving it as-is.");
                Selection.activeObject = existing;
                return;
            }

            var mat = new Material(shader);
            mat.SetColor("_BaseColor", new Color(0.52f, 0.5f, 0.47f));
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.25f);

            System.IO.Directory.CreateDirectory("Assets/Materials");
            AssetDatabase.CreateAsset(mat, DebrisMaterialPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[CreateWallBreakthroughAssets] Created " + DebrisMaterialPath);
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

            var debrisMat = AssetDatabase.LoadAssetAtPath<Material>(DebrisMaterialPath);
            if (debrisMat == null)
            {
                Debug.LogError("[CreateWallBreakthroughAssets] " + DebrisMaterialPath +
                    " doesn't exist yet - run 'ARReveal/Wall Hole/Create Debris Material' first.");
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

            // Debris ring as a child so it inherits the quad's position - RingRadius
            // is in local units, and the quad is scaled to 2, so 0.4 lines up with
            // the shader's default _MaxHoleRadius (0.4 in UV space, half-quad-width).
            var debrisGo = new GameObject("DebrisRing");
            debrisGo.transform.SetParent(quadGo.transform, false);
            var ring = debrisGo.AddComponent<DebrisRing>();
            ring.ChunkMaterial = debrisMat;
            ring.OpenOnStart = true;
            ring.OpenOnStartDelay = effect.OpenOnStartDelay + effect.OpenDuration * 0.8f;

            Selection.activeGameObject = quadGo;
            Debug.Log("[CreateWallBreakthroughAssets] Added WallHoleDemo (+ DebrisRing) to the scene. Press Play " +
                "to see it open (remember to save the scene if you want to keep it, or delete it before building).");
        }
    }
}
