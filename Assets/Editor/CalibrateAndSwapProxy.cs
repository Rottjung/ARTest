using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Retrains LeftCornerUCI_cropped.jpg with real-world physical scale (~30m wide,
    /// estimated - see conversation), so the tracked space becomes 1 unit = 1 meter,
    /// then swaps the old Tripo-generated Proxy for the new UCIProxy.fbx built from
    /// the real architectural drawing dimensions. Both now share the same real-world
    /// unit convention, so UCIProxy can be positioned using the actual bay offsets.
    /// </summary>
    public static class CalibrateAndSwapProxy
    {
        private const string SourceImagePath = "Assets/images/LeftCornerUCI_cropped.jpg";
        private const float EstimatedPhysicalWidthMeters = 30f;
        private const string ProxyFbxPath = "Assets/FBX/UCIProxy.fbx";

        [MenuItem("ARReveal/UCI-RE/Calibrate Scale + Swap to Real Proxy")]
        public static void Run()
        {
            RunInternal();
        }

        public static void RunOnUCIRSScene()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/UCI-RE.unity", OpenSceneMode.Single);
            RunInternal();
        }

        private static void RunInternal()
        {
            string fullImagePath = Path.GetFullPath(SourceImagePath);
            string zptPath = Phase1ImageTrain.TrainImage(fullImagePath, maxTrainSize: 1024,
                physicalWidthMeters: EstimatedPhysicalWidthMeters);
            if (zptPath == null)
            {
                Debug.LogError("[CalibrateAndSwapProxy] Training failed - aborting.");
                return;
            }
            string zptFilename = Path.GetFileName(zptPath);

            ZapparImageTrackingTarget target = null;
            foreach (var t in Object.FindObjectsByType<ZapparImageTrackingTarget>(FindObjectsSortMode.None))
            {
                if (!string.IsNullOrEmpty(t.Target)) { target = t; break; }
            }
            if (target == null)
            {
                Debug.LogError("[CalibrateAndSwapProxy] No ZapparImageTrackingTarget with a non-empty Target found.");
                return;
            }

            target.Target = zptFilename; // same filename, but now calibrated - reassigning is harmless and explicit
            EditorUtility.SetDirty(target);

            // Remove any previous proxy (old Tripo mesh, or an earlier run of this
            // script) so re-running is safe and doesn't duplicate.
            foreach (var childName in new[] { "Proxy", "UCIProxy" })
            {
                var old = target.transform.Find(childName);
                if (old != null)
                {
                    Debug.Log("[CalibrateAndSwapProxy] Removing existing " + childName + ".");
                    Object.DestroyImmediate(old.gameObject);
                }
            }

            var proxyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProxyFbxPath);
            if (proxyPrefab == null)
            {
                Debug.LogError("[CalibrateAndSwapProxy] Could not load " + ProxyFbxPath);
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(proxyPrefab);
            instance.name = "UCIProxy";
            instance.transform.SetParent(target.transform, false);
            // Proxy origin = STARCAR's bottom-left-front-ground corner. Preview quad is
            // centered at the target's local origin, spanning the calibrated physical
            // width/height - so the proxy origin needs to land at the quad's left/bottom
            // edge: (-halfWidth, -halfHeight, 0). Anchored off the ground edge (unambiguous
            // in the photo) rather than the roofline (perspective-distortion makes that
            // edge less reliable) - see conversation for why.
            float halfWidth = EstimatedPhysicalWidthMeters / 2f;
            var tex = new Texture2D(2, 2);
            tex.LoadImage(System.IO.File.ReadAllBytes(fullImagePath));
            float halfHeight = (EstimatedPhysicalWidthMeters * (tex.height / (float)tex.width)) / 2f;
            Object.DestroyImmediate(tex);
            instance.transform.localPosition = new Vector3(-halfWidth, -halfHeight, 0f);
            instance.transform.localRotation = Quaternion.identity;
            // Deliberately NOT touching localScale here - the imported prefab's root
            // already carries a (100,100,100) default scale that Unity's FBX importer
            // baked in to compensate for Blender's FBX export using centimeters as its
            // native unit. Overwriting it to Vector3.one (as this used to do) undoes
            // that correction and makes the mesh 100x too small.

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[CalibrateAndSwapProxy] Done. Target recalibrated to ~" + EstimatedPhysicalWidthMeters +
                "m wide, UCIProxy wired in at scale 1:1. You'll likely still need to nudge position/rotation " +
                "by eye against the tracking preview - the calibration gets the SCALE right, not necessarily " +
                "the exact anchor offset between the trained image's origin and where UCIProxy's origin sits.");
        }
    }
}
