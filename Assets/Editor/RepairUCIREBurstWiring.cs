using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Repairs UCI-RE.unity's BurstSequencer.Points when their Tentacle/Hole
    /// references come back null even though Name/Delay are correct (a real
    /// bug hit once: UCIREContentPort's own cross-references into a freshly
    /// PrefabUtility.InstantiatePrefab'd hierarchy dropped on scene save -
    /// Name/Delay are plain value fields and persisted fine, but the
    /// Component references didn't). Matches purely by NAME against whatever
    /// is actually under RevealOnTrackingFound.ContentRoots[0] right now
    /// (tentacles named "T1".."T9", holes "H1".."H9" in this project's real
    /// content) rather than depending on the original port's fragile
    /// child-index bookkeeping - so this works regardless of what caused the
    /// nulls. Safe to re-run - only fills in references that are still null,
    /// never overwrites an already-correct one.
    /// </summary>
    public static class RepairUCIREBurstWiring
    {
        private const string ScenePath = "Assets/Scenes/UCI-RE.unity";

        [MenuItem("ARReveal/UCI-RE/Repair BurstSequencer Wiring (Safety Backup)")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var reveal = Object.FindFirstObjectByType<RevealOnTrackingFound>();
            var burst = Object.FindFirstObjectByType<BurstSequencer>();
            if (reveal == null || burst == null)
            {
                Debug.LogError("[RepairUCIREBurstWiring] Need both RevealOnTrackingFound and BurstSequencer in " + ScenePath);
                return;
            }
            if (reveal.ContentRoots == null || reveal.ContentRoots.Length == 0 || reveal.ContentRoots[0] == null)
            {
                Debug.LogError("[RepairUCIREBurstWiring] RevealOnTrackingFound.ContentRoots is empty - nothing to match against.");
                return;
            }
            if (burst.Points == null || burst.Points.Length == 0)
            {
                Debug.LogError("[RepairUCIREBurstWiring] BurstSequencer.Points is empty - nothing to repair.");
                return;
            }

            GameObject content = reveal.ContentRoots[0];
            var tentaclesByName = new Dictionary<string, TentacleController>();
            foreach (var t in content.GetComponentsInChildren<TentacleController>(true))
                tentaclesByName[t.gameObject.name] = t;
            var holesByName = new Dictionary<string, WallHoleEffect>();
            foreach (var h in content.GetComponentsInChildren<WallHoleEffect>(true))
                holesByName[h.gameObject.name] = h;

            int tentacleFixes = 0, holeFixes = 0, misses = 0;
            foreach (var point in burst.Points)
            {
                if (point.Tentacle == null)
                {
                    if (tentaclesByName.TryGetValue(point.Name, out var t)) { point.Tentacle = t; tentacleFixes++; }
                    else { Debug.LogWarning("[RepairUCIREBurstWiring] No tentacle named '" + point.Name + "' found under " + content.name + "."); misses++; }
                }
                if (point.Hole == null)
                {
                    // "T4" -> "H4" - this project's real naming convention (see
                    // the AR scene's own H1..H9 wall-hole instances).
                    string holeName = "H" + point.Name.TrimStart('T', 't');
                    if (holesByName.TryGetValue(holeName, out var h)) { point.Hole = h; holeFixes++; }
                    else { Debug.LogWarning("[RepairUCIREBurstWiring] No hole named '" + holeName + "' found for point '" + point.Name + "'."); misses++; }
                }
            }

            EditorUtility.SetDirty(burst);
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[RepairUCIREBurstWiring] Fixed " + tentacleFixes + " tentacle + " + holeFixes +
                " hole reference(s), " + misses + " still unresolved (see warnings above, if any).");
        }
    }
}
