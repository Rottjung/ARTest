using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Cleans up a structural bug: an earlier scene-setup pass mistakenly attached
    /// CupJiggle directly to the MajimeCup prefab instance, and Unity auto-added a
    /// blank ZapparImageTrackingTarget alongside it (RequireComponent side effect) -
    /// giving the scene two tracking targets, one real (non-empty Target) and one
    /// phantom (empty Target, attached to the cup itself). This strips the phantom's
    /// components from the cup and wires CupJiggle onto the real target instead,
    /// selecting "real" by non-empty Target field rather than object order.
    /// </summary>
    public static class FixCupContentAndDebug
    {
        [MenuItem("ARReveal/Fix/Relink Cup Content + Add Debug Overlay")]
        public static void Run()
        {
            ZapparImageTrackingTarget realTarget = null;
            foreach (var t in Object.FindObjectsByType<ZapparImageTrackingTarget>(FindObjectsSortMode.None))
            {
                if (!string.IsNullOrEmpty(t.Target))
                {
                    realTarget = t;
                    break;
                }
            }

            if (realTarget == null)
            {
                Debug.LogError("[FixCupContentAndDebug] No ZapparImageTrackingTarget with a non-empty Target found.");
                return;
            }

            var cup = realTarget.transform.Find("MajimeCup");
            if (cup == null)
            {
                Debug.LogError("[FixCupContentAndDebug] Could not find a 'MajimeCup' child under " + realTarget.name);
                return;
            }

            // Strip the stray components directly off the cup, if present - never
            // touches its position/scale/rotation.
            var strayJiggle = cup.GetComponent<CupJiggle>();
            if (strayJiggle != null) Object.DestroyImmediate(strayJiggle);

            var strayTarget = cup.GetComponent<ZapparImageTrackingTarget>();
            if (strayTarget != null && string.IsNullOrEmpty(strayTarget.Target))
                Object.DestroyImmediate(strayTarget);

            var jiggle = realTarget.GetComponent<CupJiggle>();
            if (jiggle == null) jiggle = realTarget.gameObject.AddComponent<CupJiggle>();
            jiggle.Content = cup;
            EditorUtility.SetDirty(jiggle);

            if (realTarget.GetComponent<TrackingDebugOverlay>() == null)
                realTarget.gameObject.AddComponent<TrackingDebugOverlay>();

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[FixCupContentAndDebug] Real target: " + realTarget.name + " (Target=" + realTarget.Target +
                "). CupJiggle.Content -> " + cup.name + ". Stray components removed from cup: " +
                (strayJiggle != null || strayTarget != null));
        }
    }
}
