using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Switches the tracking target's Orientation from Flat (tabletop/business-card
    /// convention) to Vertical (wall/facade convention) - our kiosk, and eventually the
    /// real building, are both vertical surfaces, not flat horizontal ones. Only touches
    /// this one field - does not move or recreate anything else.
    /// </summary>
    public static class SetVerticalOrientation
    {
        [MenuItem("ARReveal/Fix/Set Tracking Target Orientation to Vertical")]
        public static void Run()
        {
            var target = Object.FindFirstObjectByType<ZapparImageTrackingTarget>();
            if (target == null)
            {
                Debug.LogError("[SetVerticalOrientation] No ZapparImageTrackingTarget in the active scene.");
                return;
            }

            var previous = target.Orientation;
            target.Orientation = ZapparImageTrackingTarget.PlaneOrientation.Vertical;
            EditorUtility.SetDirty(target);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[SetVerticalOrientation] Orientation changed: " + previous + " -> Vertical (scene: " + scene.name + ")");
        }
    }
}
