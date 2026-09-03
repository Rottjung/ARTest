using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Restructures UCI-RE-Marker.unity to hand off from the QR image target to a
    /// persistent ZapparInstantTrackingTarget (SLAM-based) the moment the QR is first
    /// seen, so content stays anchored even after the QR leaves frame - see
    /// HandoffToInstantTracking.cs for why this is needed.
    ///
    /// Adds a "Zappar Instant Tracker" object with a "ContentWrapper" child, reparents
    /// the existing BuildingCornerPlaceholder under it (offset preserved), removes the
    /// old direct RevealOnTrackingFound wiring on the QR target, and wires its
    /// OnSeenEvent to HandoffToInstantTracking.HandoffOnce() instead.
    /// </summary>
    public static class SetupInstantTrackingHandoff
    {
        private const string ScenePath = "Assets/Scenes/UCI-RE-Marker.unity";
        private const string MarkerObjectName = "Zappar Ground Marker";
        private const string PlaceholderName = "BuildingCornerPlaceholder";

        [MenuItem("ARReveal/Marker Test/Wire Instant-Tracking Handoff")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var markerGo = GameObject.Find(MarkerObjectName);
            if (markerGo == null)
            {
                Debug.LogError("[SetupInstantTrackingHandoff] Could not find '" + MarkerObjectName + "'.");
                return;
            }
            var imageTarget = markerGo.GetComponent<ZapparImageTrackingTarget>();

            // Clear ALL existing OnSeenEvent listeners unconditionally, whether or not
            // this is a re-run - destroying RevealOnTrackingFound below doesn't remove
            // its now-dangling listener entry from the event's persistent call list on
            // its own, that has to be done explicitly.
            if (imageTarget.OnSeenEvent == null) imageTarget.OnSeenEvent = new UnityEngine.Events.UnityEvent();
            while (imageTarget.OnSeenEvent.GetPersistentEventCount() > 0)
                UnityEventTools.RemovePersistentListener(imageTarget.OnSeenEvent, 0);

            var oldReveal = markerGo.GetComponent<RevealOnTrackingFound>();
            if (oldReveal != null) Object.DestroyImmediate(oldReveal);

            var instantGo = GameObject.Find("Zappar Instant Tracker");
            Transform wrapper;

            if (instantGo != null)
            {
                Debug.Log("[SetupInstantTrackingHandoff] 'Zappar Instant Tracker' already exists - reusing it, only re-wiring the event.");
                wrapper = instantGo.transform.Find("ContentWrapper");
            }
            else
            {
                var placeholder = GameObject.Find(PlaceholderName);
                if (placeholder == null)
                {
                    Debug.LogError("[SetupInstantTrackingHandoff] Could not find '" + PlaceholderName + "'.");
                    return;
                }

                // Instant tracker - PlaceOnTouch off via SerializedObject (private
                // field, no public setter) so a stray tap can't seed it before the QR
                // is found.
                instantGo = new GameObject("Zappar Instant Tracker", typeof(ZapparInstantTrackingTarget));
                var so = new SerializedObject(instantGo.GetComponent<ZapparInstantTrackingTarget>());
                var placeOnTouch = so.FindProperty("m_placeOnTouch");
                if (placeOnTouch != null) placeOnTouch.boolValue = false;
                so.ApplyModifiedProperties();

                var wrapperGo = new GameObject("ContentWrapper");
                wrapperGo.transform.SetParent(instantGo.transform, false);
                wrapper = wrapperGo.transform;

                placeholder.transform.SetParent(wrapper, true); // keep world position while reparenting
            }

            var instantTarget = instantGo.GetComponent<ZapparInstantTrackingTarget>();

            var handoff = markerGo.GetComponent<HandoffToInstantTracking>();
            if (handoff == null) handoff = markerGo.AddComponent<HandoffToInstantTracking>();
            handoff.ImageTarget = imageTarget;
            handoff.InstantTarget = instantTarget;
            handoff.ContentWrapper = wrapper;

            UnityEventTools.AddVoidPersistentListener(imageTarget.OnSeenEvent, handoff.HandoffOnce);

            EditorUtility.SetDirty(imageTarget);
            EditorUtility.SetDirty(handoff);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[SetupInstantTrackingHandoff] Done. QR OnSeenEvent now hands off to a persistent " +
                "instant-tracking anchor instead of driving content directly - content should stay anchored " +
                "after the QR leaves frame. NOT verified on a real device yet - test on-site.");
        }
    }
}
