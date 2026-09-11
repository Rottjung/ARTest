using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Adds ZapparMotionFeed to the "Zappar Camera" GameObject in UCI-RE-AR.unity -
    /// see that script's own doc comment for why it exists. Safe to re-run (does
    /// nothing if already present).
    /// </summary>
    public static class SetupZapparMotionFeed
    {
        private const string ScenePath = "Assets/Scenes/UCI-RE-AR.unity";
        private const string CameraObjectName = "Zappar Camera";

        [MenuItem("ARReveal/UCI-RE-AR/Wire Zappar Motion Feed")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var cameraGo = GameObject.Find(CameraObjectName);
            if (cameraGo == null)
            {
                Debug.LogError($"[SetupZapparMotionFeed] Could not find '{CameraObjectName}'.");
                return;
            }

            var feed = cameraGo.GetComponent<ZapparMotionFeed>();
            if (feed == null)
            {
                feed = cameraGo.AddComponent<ZapparMotionFeed>();
                Debug.Log($"[SetupZapparMotionFeed] Added ZapparMotionFeed to '{CameraObjectName}'.");
            }
            else
            {
                Debug.Log($"[SetupZapparMotionFeed] '{CameraObjectName}' already has ZapparMotionFeed - nothing to do.");
            }

            EditorUtility.SetDirty(feed);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[SetupZapparMotionFeed] Done. NOT verified on a real device yet - test on-site.");
        }
    }
}
