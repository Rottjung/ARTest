using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// The Image Tracking Target inspector's live preview feature creates a runtime
    /// Texture2D at the source image's full resolution and, if the scene is saved while
    /// it exists, bakes that texture directly into the scene file (not as an asset
    /// reference - as raw embedded pixel data). That's what bloated BuildingTest.unity
    /// to 63MB. This removes any such "Preview Object" from the active scene and turns
    /// the feature off in Zappar's settings so it can't happen again.
    /// </summary>
    public static class CleanScenePreview
    {
        [MenuItem("ARReveal/Fix/Strip Embedded Preview Textures From Scene")]
        public static void Run()
        {
            CleanActiveScene();
        }

        // Batch-mode-safe entry point - explicitly opens the scene first rather than
        // relying on whatever happens to be "active" in a headless session.
        public static void RunOnBuildingTestScene()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/BuildingTest.unity", OpenSceneMode.Single);
            CleanActiveScene();
        }

        private static void CleanActiveScene()
        {
            int removed = 0;
            foreach (var t in Object.FindObjectsByType<ZapparImageTrackingTarget>(FindObjectsSortMode.None))
            {
                if (t.PreviewImageObject != null)
                {
                    Object.DestroyImmediate(t.PreviewImageObject);
                    t.PreviewImageObject = null;
                    removed++;
                }
                var stalePreview = t.transform.Find("Preview Object");
                if (stalePreview != null)
                {
                    Object.DestroyImmediate(stalePreview.gameObject);
                    removed++;
                }
            }

            var settings = AssetDatabase.LoadAssetAtPath<ZapparUARSettings>(ZapparUARSettings.MySettingsPathInPackage);
            if (settings != null && settings.ImageTargetPreviewEnabled)
            {
                settings.ImageTargetPreviewEnabled = false;
                EditorUtility.SetDirty(settings);
                Debug.Log("[CleanScenePreview] Disabled ImageTargetPreviewEnabled in Zappar UAR Settings.");
            }

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log("[CleanScenePreview] Removed " + removed + " preview object(s). Scene saved.");
        }
    }
}
