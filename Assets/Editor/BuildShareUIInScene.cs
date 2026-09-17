using UnityEditor;
using UnityEngine;
using ARReveal;

namespace ARRevealEditor
{
    /// <summary>
    /// One-click "materialize the client UI into real, editable scene objects"
    /// command. ARShareController normally builds its Canvas/Image/Button
    /// hierarchy procedurally at runtime (see its own doc comment) - great
    /// for zero-setup, but impossible to hand-tune positions/sizes on since
    /// nothing exists in the Scene view until you press Play.
    ///
    /// This runs that exact same build logic once, in Edit mode, producing
    /// real persisted GameObjects under a "Share" GameObject's "ARShareCanvas"
    /// child that you can freely drag RectTransforms around on, then save as
    /// a prefab. See ARShareController.Awake()/AttachToExistingUI() for how
    /// it picks the hand-tuned version back up at runtime instead of
    /// rebuilding from scratch - it looks for that same "ARShareCanvas"
    /// child by name, so nothing further needs to be wired up as long as it
    /// stays parented under the same GameObject that holds ARShareController.
    /// </summary>
    public static class BuildShareUIInScene
    {
        [MenuItem("ARReveal/UI/Build Share UI In Scene")]
        public static void Build()
        {
            var controller = Object.FindFirstObjectByType<ARShareController>();
            if (controller == null)
            {
                var go = new GameObject("Share");
                Undo.RegisterCreatedObjectUndo(go, "Create Share GameObject");
                controller = go.AddComponent<ARShareController>();
            }

            Undo.RegisterFullObjectHierarchyUndo(controller.gameObject, "Build Share UI");

            // ARShareController no longer has any per-element Sprite fields
            // to auto-assign here - per direct request, no button renders
            // its own sprite art anymore ("all button do not use sprites
            // anymore, we use transparent button on top of the design").
            // Every page's real look is one background image hand-placed
            // directly on the page's own Image component in the saved
            // prefab; EditorRebuildUI()/BuildPage0-4 just lay out the
            // correctly-named, correctly-sized placeholder GameObjects for
            // that art (and the transparent button hit-targets) to be
            // hand-tuned afterward.
            controller.EditorRebuildUI();

            Selection.activeGameObject = controller.gameObject;
            EditorGUIUtility.PingObject(controller.gameObject);
            EditorUtility.SetDirty(controller);

            Debug.Log("[BuildShareUIInScene] Built the UI under '" + controller.gameObject.name +
                "' -> ARShareCanvas. Adjust the layout in the Scene/Game view, then drag the '" +
                controller.gameObject.name + "' GameObject from the Hierarchy into a Project folder " +
                "(e.g. Assets/Prefabs/) to save it as a prefab - it's already wired up and sitting in " +
                "the scene, so nothing else needs to be done for it to work.");
        }
    }
}
