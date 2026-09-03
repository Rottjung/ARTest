using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// One-off fix for UCI-RE.unity: content under the panel tracker (5 Tentacle_01
    /// burst points, each with its own WallHoleDemo/DebrisRing children) currently
    /// shows and animates immediately on Play, regardless of whether the real building
    /// has actually been found - because each still carries TentacleGrowOnStart (a
    /// scene-load timer, not tracking-gated) and nothing hides content before tracking
    /// locks on. This:
    ///   1. Removes TentacleGrowOnStart from all 5 burst points.
    ///   2. Adds a RevealOnTrackingFound to the panel tracker, listing all 5 as its
    ///      ContentRoots (hidden until tracking is found).
    ///   3. Wires the tracker's OnSeenEvent to call RevealOnTrackingFound.Reveal().
    /// </summary>
    public static class WireTrackingGatedReveal
    {
        private const string TrackerName = "Zappar Image Tracking Target";

        [MenuItem("ARReveal/UCI-RE/Wire Tracking-Gated Reveal (Panel Target)")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/UCI-RE.unity", OpenSceneMode.Single);

            var trackerGo = GameObject.Find(TrackerName);
            if (trackerGo == null)
            {
                Debug.LogError("[WireTrackingGatedReveal] Could not find '" + TrackerName + "'.");
                return;
            }
            var target = trackerGo.GetComponent<ZapparImageTrackingTarget>();
            if (target == null)
            {
                Debug.LogError("[WireTrackingGatedReveal] '" + TrackerName + "' has no ZapparImageTrackingTarget.");
                return;
            }

            var contentRoots = new List<GameObject>();
            foreach (Transform child in trackerGo.transform)
            {
                if (child.GetComponent<TentacleController>() == null) continue;
                contentRoots.Add(child.gameObject);

                var growOnStart = child.GetComponent<TentacleGrowOnStart>();
                if (growOnStart != null)
                {
                    Object.DestroyImmediate(growOnStart);
                    Debug.Log("[WireTrackingGatedReveal] Removed TentacleGrowOnStart from " + child.name);
                }
            }

            if (contentRoots.Count == 0)
            {
                Debug.LogWarning("[WireTrackingGatedReveal] No TentacleController children found under '" + TrackerName + "'.");
            }

            var reveal = trackerGo.GetComponent<RevealOnTrackingFound>();
            if (reveal == null) reveal = trackerGo.AddComponent<RevealOnTrackingFound>();
            reveal.ContentRoots = contentRoots.ToArray();

            // Avoid double-wiring if this script is re-run.
            bool alreadyWired = false;
            for (int i = 0; i < target.OnSeenEvent.GetPersistentEventCount(); i++)
            {
                if (target.OnSeenEvent.GetPersistentMethodName(i) == nameof(RevealOnTrackingFound.Reveal))
                {
                    alreadyWired = true;
                    break;
                }
            }
            if (!alreadyWired)
            {
                UnityEventTools.AddVoidPersistentListener(target.OnSeenEvent, reveal.Reveal);
            }

            EditorUtility.SetDirty(target);
            EditorUtility.SetDirty(reveal);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[WireTrackingGatedReveal] Done. " + contentRoots.Count + " burst point(s) now hidden until " +
                "OnSeenEvent fires, TentacleGrowOnStart removed from all of them.");
        }
    }
}
