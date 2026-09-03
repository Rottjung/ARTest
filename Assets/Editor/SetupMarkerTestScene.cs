using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Builds a standalone test scene for ground-marker anchoring - kept separate from
    /// UCI-RE.unity so experimenting with this doesn't risk the working building-photo
    /// tracking setup there. Trains Assets/Images/GroundMarker.png (a placeholder QR -
    /// swap for whatever the client actually prints) at its EXACT physical size (50cm -
    /// unlike the building photos, we get to define this precisely rather than guess),
    /// with Orientation=Flat since it lies on the ground rather than mounted on a wall.
    ///
    /// Content (currently just a placeholder cube) is positioned at a PLACEHOLDER
    /// offset representing "the building corner is 3m and 3m away" - the actual
    /// direction depends on which way the marker faces once physically placed, which
    /// isn't decided yet. Treat CornerOffsetFromMarker as something to correct once
    /// there's a real placement convention, not a real measurement yet.
    /// </summary>
    public static class SetupMarkerTestScene
    {
        private const string NewScenePath = "Assets/Scenes/UCI-RE-Marker.unity";
        private const string SourceCameraScenePath = "Assets/Scenes/UCI-RE.unity";
        private const string MarkerImagePath = "Assets/Images/GroundMarker.png";
        private const float MarkerPhysicalWidthMeters = 0.5f; // exact - client's print size, 50x50cm

        // PLACEHOLDER - 3m and 3m from the building corner, direction not yet agreed
        // with whoever places the marker on site. Correct once that's known.
        private static readonly Vector3 CornerOffsetFromMarker = new Vector3(3f, 0f, 3f);

        [MenuItem("ARReveal/Marker Test/Build Scene")]
        public static void Run()
        {
            // 1. New empty scene.
            var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 2. Pull the camera rig in from UCI-RE.unity (loaded additively) rather
            // than hand-building it, so it matches the working URP camera-stack setup
            // exactly. "Zappar Camera Background" is a CHILD of "Zappar Camera" there,
            // not a separate root - Instantiate deep-copies children automatically, so
            // instantiating both separately would create two background cameras.
            var camScene = EditorSceneManager.OpenScene(SourceCameraScenePath, OpenSceneMode.Additive);
            var camSource = GameObject.Find("Zappar Camera");
            if (camSource == null)
            {
                Debug.LogError("[SetupMarkerTestScene] Could not find 'Zappar Camera' in " + SourceCameraScenePath);
                EditorSceneManager.CloseScene(camScene, true);
                return;
            }

            var cam = Object.Instantiate(camSource);
            cam.name = camSource.name;
            SceneManager.MoveGameObjectToScene(cam, newScene);

            EditorSceneManager.CloseScene(camScene, true); // discard, we only wanted the rig

            // 3. Train the marker at its exact physical size.
            string fullImagePath = Path.GetFullPath(MarkerImagePath);
            string zptPath = Phase1ImageTrain.TrainImage(fullImagePath, maxTrainSize: 1024,
                physicalWidthMeters: MarkerPhysicalWidthMeters);
            if (zptPath == null)
            {
                Debug.LogError("[SetupMarkerTestScene] Marker training failed - aborting.");
                return;
            }
            string zptFilename = Path.GetFileName(zptPath);

            // 4. The marker tracking target - Flat, since it's face-up on the ground
            // (Vertical is for wall-mounted targets like the building photo).
            var markerGo = new GameObject("Zappar Ground Marker", typeof(ZapparImageTrackingTarget));
            var target = markerGo.GetComponent<ZapparImageTrackingTarget>();
            target.Target = zptFilename;
            target.Orientation = ZapparImageTrackingTarget.PlaneOrientation.Flat;
            markerGo.AddComponent<TrackingDebugOverlay>();

            // 5. Placeholder content at the (placeholder) corner offset, hidden until
            // tracking is found - same gating pattern as the panel target in UCI-RE.
            var placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placeholder.name = "BuildingCornerPlaceholder";
            placeholder.transform.SetParent(markerGo.transform, false);
            placeholder.transform.localPosition = CornerOffsetFromMarker + Vector3.up * 0.5f; // sit on the ground, not half-buried
            placeholder.transform.localScale = Vector3.one;

            var reveal = markerGo.AddComponent<RevealOnTrackingFound>();
            reveal.ContentRoots = new[] { placeholder };
            // A freshly-AddComponent'd ZapparImageTrackingTarget doesn't auto-populate
            // its UnityEvent fields (no inline initializer in the SDK class) - unlike
            // one dragged in via the Inspector/prefab flow, which does.
            if (target.OnSeenEvent == null) target.OnSeenEvent = new UnityEngine.Events.UnityEvent();
            UnityEditor.Events.UnityEventTools.AddVoidPersistentListener(target.OnSeenEvent, reveal.Reveal);

            // 6. Save as the new scene.
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(NewScenePath)));
            EditorSceneManager.MarkSceneDirty(newScene);
            EditorSceneManager.SaveScene(newScene, NewScenePath);

            Debug.Log("[SetupMarkerTestScene] Done. Created " + NewScenePath + " with a Flat-oriented marker " +
                "target (" + zptFilename + ", exact 0.5m wide) and a placeholder cube at " + CornerOffsetFromMarker +
                " from the marker - correct that offset once the real marker placement/orientation is known.");
        }
    }
}
