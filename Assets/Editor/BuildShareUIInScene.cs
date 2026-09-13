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
        private const string ImagesFolder = "Assets/Images/UI/";

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

            AssignIfMissing(ref controller.LogoSprite, "Logo.png");
            AssignIfMissing(ref controller.Page1TextSprite, "Page_01_Text.png");
            AssignIfMissing(ref controller.Page2TextTopSprite, "Page_02_Text_Top.png");
            AssignIfMissing(ref controller.Page2TextBottomSprite, "Page_02_Text_Bottom.png");
            AssignIfMissing(ref controller.Page3TextSprite, "Page_03_Text.png");
            AssignIfMissing(ref controller.RestartButtonSprite, "Use_Button_Page_02_01.png");
            AssignIfMissing(ref controller.FotoButtonSprite, "Use_Button_Page_02_02.png");
            AssignIfMissing(ref controller.TeilenButtonSprite, "Use_Button_Page_03.png");
            AssignIfMissing(ref controller.RecordButtonSprite, "RecButton.png");

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

        private static void AssignIfMissing(ref Sprite field, string fileName)
        {
            if (field != null) return;
            field = AssetDatabase.LoadAssetAtPath<Sprite>(ImagesFolder + fileName);
            if (field == null)
                Debug.LogWarning("[BuildShareUIInScene] Could not auto-find sprite at " + ImagesFolder + fileName + " - drag it into the matching field on ARShareController by hand.");
        }
    }
}
