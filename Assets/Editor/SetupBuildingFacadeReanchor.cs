using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;
using ARReveal;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Adds a second ZapparImageTrackingTarget (trained on BuildingFacadeTarget.jpg -
    /// see that file and HandoffToInstantTracking's "SECONDARY RE-ANCHOR SOURCES" doc
    /// comment) to UCI-RE-AR.unity, and wires it into HandoffToInstantTracking's
    /// AdditionalReanchorSources so it corrects position drift whenever seen,
    /// throughout the experience, instead of just once at the start via the QR.
    ///
    /// WorldPositionRelativeToQR below is an ESTIMATE, not a survey measurement -
    /// built from the one real number we have (QR is 2.50m left of the building
    /// corner, 20.40m in front of it) plus a rough visual read of where the tracked
    /// crop sits relative to that corner (a few meters further right along the front
    /// face, roughly mid-height). Re-anchoring from an estimated position is still a
    /// real improvement over nothing correcting drift between QR sightings, but this
    /// value should be replaced with an actual on-site measurement (or a more careful
    /// derivation from the building model) once available - see this class's own
    /// EstimatedFacadePosition constant to change it, then re-run this menu item
    /// (safe to re-run - updates the existing entry instead of duplicating it).
    /// </summary>
    public static class SetupBuildingFacadeReanchor
    {
        private const string ScenePath = "Assets/Scenes/UCI-RE-AR.unity";
        private const string TargetObjectName = "Zappar Building Facade Target";
        private const string ZptFilename = "BuildingFacade.zpt";
        private const string HandoffObjectName = "Zappar Ground Marker";

        // See class doc above - estimate, not a survey measurement.
        private static readonly Vector3 EstimatedFacadePosition = new Vector3(5.5f, 11.0f, 20.4f);

        [MenuItem("ARReveal/UCI-RE-AR/Wire Building-Facade Re-anchor Target")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var handoffGo = GameObject.Find(HandoffObjectName);
            if (handoffGo == null)
            {
                Debug.LogError($"[SetupBuildingFacadeReanchor] Could not find '{HandoffObjectName}'.");
                return;
            }
            var handoff = handoffGo.GetComponent<HandoffToInstantTracking>();
            if (handoff == null)
            {
                Debug.LogError($"[SetupBuildingFacadeReanchor] '{HandoffObjectName}' has no HandoffToInstantTracking component.");
                return;
            }

            var targetGo = GameObject.Find(TargetObjectName);
            bool created = targetGo == null;
            if (created)
            {
                targetGo = new GameObject(TargetObjectName, typeof(ZapparImageTrackingTarget));
                Debug.Log($"[SetupBuildingFacadeReanchor] Created '{TargetObjectName}'.");
            }
            else
            {
                Debug.Log($"[SetupBuildingFacadeReanchor] '{TargetObjectName}' already exists - reusing it.");
            }

            var imageTarget = targetGo.GetComponent<ZapparImageTrackingTarget>();
            if (imageTarget == null) imageTarget = targetGo.AddComponent<ZapparImageTrackingTarget>();
            imageTarget.Target = ZptFilename;
            // Vertical, not Flat - this is a wall, not a ground-lying marker. Flat
            // applies an extra +90 degree correction meant for markers lying on the
            // ground (see ZapparImageTrackingTarget.UpdateTargetPose) which would be
            // wrong here. Not that rotation is actually used from this target (see
            // HandoffToInstantTracking's doc - re-anchoring from this source is
            // position-only), but Orientation also has no other effect here since
            // this GameObject's own transform isn't consumed for anything besides
            // AnchorPoseCameraRelative()'s POSITION component - set correctly anyway
            // for consistency/future use.
            imageTarget.Orientation = ZapparImageTrackingTarget.PlaneOrientation.Vertical;

            // Find (or add) the AdditionalReanchorSources entry for this target -
            // re-running this menu item updates the existing entry (e.g. after
            // tweaking EstimatedFacadePosition above) instead of duplicating it.
            var sources = handoff.AdditionalReanchorSources;
            SecondaryReanchorSource entry = null;
            if (sources != null)
            {
                foreach (var s in sources)
                {
                    if (s != null && s.Target == imageTarget) { entry = s; break; }
                }
            }
            if (entry == null)
            {
                entry = new SecondaryReanchorSource { Name = "Building Facade (UCI LUXE + Bowling World signage)" };
                var list = new System.Collections.Generic.List<SecondaryReanchorSource>(sources ?? new SecondaryReanchorSource[0]);
                list.Add(entry);
                handoff.AdditionalReanchorSources = list.ToArray();
            }
            entry.Target = imageTarget;
            entry.WorldPositionRelativeToQR = EstimatedFacadePosition;

            EditorUtility.SetDirty(imageTarget);
            EditorUtility.SetDirty(handoff);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[SetupBuildingFacadeReanchor] Done. '{TargetObjectName}' tracks {ZptFilename} " +
                $"(Vertical orientation) and is wired into {HandoffObjectName}'s AdditionalReanchorSources " +
                $"with an ESTIMATED position {EstimatedFacadePosition} relative to the QR - refine this once " +
                "a real on-site measurement is available. NOT verified on a real device yet - test on-site.");
        }
    }
}
