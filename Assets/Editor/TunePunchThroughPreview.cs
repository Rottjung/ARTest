using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// One-off batch-mode tune-up for TentacleTest.unity: switches Tentacle_01 to
    /// GrowStyle.Punch (tip punches through instead of slowly unfurling) and speeds up
    /// WallHoleEffect's opening to match, so the crack/hole/punch/debris all resolve
    /// together as one fast, violent burst instead of the tentacle finishing its punch
    /// well before the hole has visibly opened.
    /// </summary>
    public static class TunePunchThroughPreview
    {
        private const string ScenePath = "Assets/Scenes/TentacleTest.unity";

        [MenuItem("ARReveal/Wall Hole/Tune Punch-Through Timing")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // Tentacle_01 is (confusingly) also the name of its own mesh child, so
            // GameObject.Find("Tentacle_01") is ambiguous - it doesn't guarantee the
            // root that actually carries TentacleController. Scan root objects instead.
            TentacleController tentacle = null;
            GameObject holeGo = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (tentacle == null) tentacle = root.GetComponent<TentacleController>();
                if (holeGo == null && root.name == "WallHoleDemo") holeGo = root;
            }

            if (tentacle == null || holeGo == null)
            {
                Debug.LogError("[TunePunchThroughPreview] Couldn't find a root TentacleController and/or " +
                    "WallHoleDemo in " + ScenePath + " - run 'ARReveal/Wall Hole/Setup TentacleTest Preview' first.");
                return;
            }
            tentacle.Style = TentacleController.GrowStyle.Punch;
            // Tentacle_01 is a prefab instance - a plain field assignment doesn't
            // register as an override, so SaveScene silently discards it unless we
            // explicitly record it.
            PrefabUtility.RecordPrefabInstancePropertyModifications(tentacle);

            var hole = holeGo.GetComponent<WallHoleEffect>();
            hole.OpenDuration = 0.42f;
            hole.GlowRiseDuration = 0.12f;
            hole.GlowFadeDuration = 0.45f;
            hole.PreCrackFadeDuration = 0.2f;

            var debrisGo = holeGo.transform.Find("DebrisRing");
            if (debrisGo != null)
            {
                var debris = debrisGo.GetComponent<DebrisRing>();
                // Was baked as hole.OpenOnStartDelay + old OpenDuration(1.0)*0.8 - recompute
                // against the new, faster OpenDuration so debris still lands once the hole
                // is most of the way open.
                debris.OpenOnStartDelay = hole.OpenOnStartDelay + hole.OpenDuration * 0.8f;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[TunePunchThroughPreview] Done. Tentacle_01 is now GrowStyle.Punch, hole OpenDuration=" +
                hole.OpenDuration + "s. Both still fire off the same 0.5s start delay, so the crack, hole and " +
                "punch should now land together instead of the punch finishing before the hole catches up.");
        }
    }
}
