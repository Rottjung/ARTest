using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Fixes a gap left by WireTrackingGatedReveal: removing TentacleGrowOnStart
    /// stopped anything from actually calling Grow()/Open() - RevealOnTrackingFound
    /// only handles visibility, not triggering. This builds a BurstSequencer covering
    /// all 5 panel burst points (tentacle + hole + debris each, staggered) and wires it
    /// into RevealOnTrackingFound so OnSeenEvent actually fires the burst, not just
    /// reveals inert content.
    /// </summary>
    public static class WireBurstSequencer
    {
        private const string TrackerName = "Zappar Image Tracking Target";
        private const float PerPointStagger = 0.12f;

        [MenuItem("ARReveal/UCI-RE/Wire Burst Sequencer (Panel Target)")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/UCI-RE.unity", OpenSceneMode.Single);

            var trackerGo = GameObject.Find(TrackerName);
            if (trackerGo == null)
            {
                Debug.LogError("[WireBurstSequencer] Could not find '" + TrackerName + "'.");
                return;
            }

            var points = new List<BurstPoint>();
            int index = 0;
            foreach (Transform child in trackerGo.transform)
            {
                var tentacle = child.GetComponent<TentacleController>();
                if (tentacle == null) continue;

                points.Add(new BurstPoint
                {
                    Name = child.name,
                    Tentacle = tentacle,
                    Hole = child.GetComponentInChildren<WallHoleEffect>(true),
                    Debris = child.GetComponentInChildren<DebrisRing>(true),
                    Delay = index * PerPointStagger
                });
                index++;
            }

            if (points.Count == 0)
            {
                Debug.LogWarning("[WireBurstSequencer] No TentacleController children found under '" + TrackerName + "'.");
                return;
            }

            var sequencer = trackerGo.GetComponent<BurstSequencer>();
            if (sequencer == null) sequencer = trackerGo.AddComponent<BurstSequencer>();
            sequencer.Points = points.ToArray();
            sequencer.PlayOnStart = false; // triggered by RevealOnTrackingFound instead

            var reveal = trackerGo.GetComponent<RevealOnTrackingFound>();
            if (reveal == null)
            {
                Debug.LogError("[WireBurstSequencer] No RevealOnTrackingFound on '" + TrackerName +
                    "' - run WireTrackingGatedReveal first.");
                return;
            }
            reveal.BurstSequencer = sequencer;

            EditorUtility.SetDirty(sequencer);
            EditorUtility.SetDirty(reveal);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[WireBurstSequencer] Done. BurstSequencer wired with " + points.Count +
                " burst point(s), staggered " + PerPointStagger + "s apart, and hooked into RevealOnTrackingFound.");
        }
    }
}
