using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Safety-backup path: direct image tracking on the real building corner
    /// (LeftCornerUCI_cropped.jpg / UCI-RE.unity), no QR/SLAM at all - for the
    /// scenario where the viewer stands still, requested as a fallback in case
    /// the QR+SLAM handoff (UCI-RE-AR.unity, the primary path) isn't reliable
    /// enough on the day.
    ///
    /// Rather than hand-rebuild the tentacle/hole/proxy content from scratch
    /// (error-prone, and would drift out of sync with the real, polished
    /// content), this saves the AR scene's OWN "ContentParent" GameObject
    /// (already assigned as HandoffToInstantTracking.ContentRoot there - the
    /// real proxy + all 9 tentacle/hole burst pairs, fully wired) as a
    /// reusable prefab, then instantiates that SAME prefab under
    /// UCI-RE.unity's plain ZapparImageTrackingTarget instead - swapping only
    /// the anchoring mechanism (direct image tracking vs QR+SLAM), not the
    /// content itself.
    ///
    /// Burst orchestration uses BurstSequencer/RevealOnTrackingFound (the
    /// older, simpler pair built for exactly this non-SLAM case) instead of
    /// HandoffToInstantTracking (QR/SLAM-specific, and depends on an
    /// InstantTrackingTarget this scene deliberately doesn't have). The same
    /// 9 Tentacle/Hole pairings from the AR scene's own
    /// HandoffToInstantTracking.Pairs are read directly as real C# object
    /// references (not hand-copied fileIDs) and carried over by CHILD-INDEX
    /// correspondence, since saving a hierarchy as a prefab and instantiating
    /// it preserves child order exactly - the same index into ContentParent's
    /// children list finds the same tentacle/hole in the new instance.
    ///
    /// Safe to re-run: clears its own previous output (old
    /// BurstSequencer/RevealOnTrackingFound wiring, old content instance,
    /// old OnSeenEvent listener) before rebuilding.
    /// </summary>
    public static class UCIREContentPort
    {
        private const string ArScenePath = "Assets/Scenes/UCI-RE-AR.unity";
        private const string DirectScenePath = "Assets/Scenes/UCI-RE.unity";
        private const string PrefabOutputPath = "Assets/Prefabs/UCI_RE_BuildingContent.prefab";
        private const string SourceImagePath = "Assets/images/LeftCornerUCI_cropped.jpg";

        /// <summary>Same estimate CalibrateAndSwapProxy already uses for this exact photo - see that script's own doc comment.</summary>
        private const float EstimatedPhysicalWidthMeters = 30f;

        /// <summary>Seconds between each non-hero burst point's fixed delay - BurstSequencer takes fixed delays (not a random range like HandoffToInstantTracking), so a simple stagger step stands in for that.</summary>
        private const float DelayStep = 0.6f;

        private struct PairIndex
        {
            public int TentacleChildIndex;
            public int HoleChildIndex;
        }

        [MenuItem("ARReveal/UCI-RE/Port Real Content From AR Scene (Safety Backup)")]
        public static void Run()
        {
            var pairIndices = new List<PairIndex>();
            if (!SaveContentPrefabFromArScene(pairIndices))
            {
                Debug.LogError("[UCIREContentPort] Aborting - could not save content prefab from the AR scene.");
                return;
            }

            WireDirectTrackingScene(pairIndices);
        }

        private static bool SaveContentPrefabFromArScene(List<PairIndex> pairIndices)
        {
            EditorSceneManager.OpenScene(ArScenePath, OpenSceneMode.Single);

            var handoff = Object.FindFirstObjectByType<HandoffToInstantTracking>();
            if (handoff == null || handoff.ContentRoot == null)
            {
                Debug.LogError("[UCIREContentPort] No HandoffToInstantTracking.ContentRoot found in " + ArScenePath);
                return false;
            }

            Transform contentParent = handoff.ContentRoot;
            var children = new List<Transform>();
            foreach (Transform child in contentParent) children.Add(child);

            if (handoff.Pairs != null)
            {
                foreach (var pair in handoff.Pairs)
                {
                    if (pair.Tentacle == null || pair.Hole == null) continue;
                    int tIndex = children.IndexOf(pair.Tentacle.transform);
                    int hIndex = children.IndexOf(pair.Hole.transform);
                    if (tIndex < 0 || hIndex < 0)
                    {
                        Debug.LogWarning("[UCIREContentPort] Pair '" + pair.Name + "' - Tentacle/Hole isn't a direct child of ContentRoot, skipping automatic pairing for it (still ported as content, just won't auto-fire together in BurstSequencer - wire it by hand if needed).");
                        continue;
                    }
                    pairIndices.Add(new PairIndex { TentacleChildIndex = tIndex, HoleChildIndex = hIndex });
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(PrefabOutputPath)));
            PrefabUtility.SaveAsPrefabAsset(contentParent.gameObject, PrefabOutputPath);
            Debug.Log("[UCIREContentPort] Saved " + PrefabOutputPath + " from " + ArScenePath + "'s ContentParent (" +
                children.Count + " children, " + pairIndices.Count + " tentacle/hole pairs tracked).");
            return true;
        }

        private static void WireDirectTrackingScene(List<PairIndex> pairIndices)
        {
            EditorSceneManager.OpenScene(DirectScenePath, OpenSceneMode.Single);

            var target = Object.FindFirstObjectByType<ZapparImageTrackingTarget>();
            if (target == null)
            {
                Debug.LogError("[UCIREContentPort] No ZapparImageTrackingTarget found in " + DirectScenePath);
                return;
            }

            // Remove the old placeholder cubes and any previous run's output -
            // matching CalibrateAndSwapProxy's own "safe to re-run" cleanup.
            foreach (var childName in new[] { "BuildingCornerPlaceholder", "BuildingCornerPlaceholder (1)", "Proxy", "UCIProxy", "UCI_RE_BuildingContent" })
            {
                var old = target.transform.Find(childName);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }

            // Calibrate the tracked image to the same real-world scale
            // CalibrateAndSwapProxy already established (~30m wide, estimated -
            // same value, same reasoning, see that script's own doc comment) -
            // 1 Unity unit = 1 metre, matching UCI_RE_BuildingContent's own
            // real-world-scale authoring in the AR scene.
            string fullImagePath = Path.GetFullPath(SourceImagePath);
            string zptPath = Phase1ImageTrain.TrainImage(fullImagePath, maxTrainSize: 1024,
                physicalWidthMeters: EstimatedPhysicalWidthMeters);
            if (zptPath == null)
            {
                Debug.LogError("[UCIREContentPort] Training failed - aborting before wiring content.");
                return;
            }
            target.Target = Path.GetFileName(zptPath);
            EditorUtility.SetDirty(target);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabOutputPath);
            if (prefab == null)
            {
                Debug.LogError("[UCIREContentPort] Could not load " + PrefabOutputPath);
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "UCI_RE_BuildingContent";
            instance.transform.SetParent(target.transform, false);

            // Same "quad's bottom-left-front-ground corner" anchor convention
            // CalibrateAndSwapProxy uses - a starting position only; expect to
            // nudge it by eye against the tracking preview afterward (same
            // caveat that script's own log message gives).
            float halfWidth = EstimatedPhysicalWidthMeters / 2f;
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(fullImagePath));
            float halfHeight = (EstimatedPhysicalWidthMeters * (tex.height / (float)tex.width)) / 2f;
            Object.DestroyImmediate(tex);
            instance.transform.localPosition = new Vector3(-halfWidth, -halfHeight, 0f);
            instance.transform.localRotation = Quaternion.identity;

            WireRevealAndBurst(target, instance, pairIndices);

            // Belt-and-suspenders against a real bug hit once: cross-references
            // into a freshly PrefabUtility.InstantiatePrefab'd hierarchy can drop
            // silently on scene save if the new instance isn't fully "settled"
            // with the serialization system first (Name/Delay, being plain value
            // fields, persisted fine while the Tentacle/Hole Component references
            // came back null - see RepairUCIREBurstWiring.cs, which fixes this
            // after the fact by name if it ever happens again). Forcing an asset
            // database save/refresh here, then re-verifying every point actually
            // has both references before the FINAL scene save, means this run
            // either produces fully-correct wiring or loudly says so - never a
            // silent null.
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var burstCheck = target.gameObject.GetComponent<BurstSequencer>();
            if (burstCheck != null && burstCheck.Points != null)
            {
                int broken = 0;
                foreach (var point in burstCheck.Points)
                    if (point.Tentacle == null || point.Hole == null) broken++;
                if (broken > 0)
                    Debug.LogError("[UCIREContentPort] " + broken + "/" + burstCheck.Points.Length +
                        " BurstSequencer.Points still have a null Tentacle/Hole after wiring - run " +
                        "ARReveal/UCI-RE/Repair BurstSequencer Wiring (Safety Backup) to fix by name.");
            }

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[UCIREContentPort] Done. " + DirectScenePath + " now tracks the real building corner directly " +
                "(no QR/SLAM) with the same tentacle/hole content as the AR scene. Position/rotation will likely still " +
                "need a manual nudge against the tracking preview - the calibration gets the SCALE right, not " +
                "necessarily the exact anchor offset.");
        }

        private static void WireRevealAndBurst(ZapparImageTrackingTarget target, GameObject content, List<PairIndex> pairIndices)
        {
            GameObject targetGo = target.gameObject;

            var children = new List<Transform>();
            foreach (Transform child in content.transform) children.Add(child);

            var burst = targetGo.GetComponent<BurstSequencer>();
            if (burst == null) burst = targetGo.AddComponent<BurstSequencer>();

            var points = new List<BurstPoint>();
            float delay = 0f;
            foreach (var pi in pairIndices)
            {
                if (pi.TentacleChildIndex >= children.Count || pi.HoleChildIndex >= children.Count) continue;
                var tentacle = children[pi.TentacleChildIndex].GetComponent<TentacleController>();
                var hole = children[pi.HoleChildIndex].GetComponent<WallHoleEffect>();
                if (tentacle == null || hole == null) continue;

                float pointDelay;
                if (tentacle.IsHero)
                {
                    pointDelay = 0f;
                }
                else
                {
                    delay += DelayStep;
                    pointDelay = delay;
                }

                points.Add(new BurstPoint
                {
                    Name = tentacle.gameObject.name,
                    Tentacle = tentacle,
                    Hole = hole,
                    Delay = pointDelay
                });
            }
            burst.Points = points.ToArray();
            EditorUtility.SetDirty(burst);

            var reveal = targetGo.GetComponent<RevealOnTrackingFound>();
            if (reveal == null) reveal = targetGo.AddComponent<RevealOnTrackingFound>();
            reveal.ContentRoots = new[] { content };
            reveal.BurstSequencer = burst;
            EditorUtility.SetDirty(reveal);

            // Wire ZapparImageTrackingTarget.OnSeenEvent -> RevealOnTrackingFound.Reveal
            // as a proper persistent (Inspector-visible, scene-serialized) listener -
            // a plain runtime .AddListener() call here would NOT survive saving the
            // scene (same lesson learned wiring ARShareController's own buttons).
            // Clear any previous run's listener first so re-running this command
            // doesn't stack up duplicates.
            while (target.OnSeenEvent.GetPersistentEventCount() > 0)
                UnityEventTools.RemovePersistentListener(target.OnSeenEvent, 0);
            UnityEventTools.AddPersistentListener(target.OnSeenEvent, reveal.Reveal);
            EditorUtility.SetDirty(target);
        }
    }
}
