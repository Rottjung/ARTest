using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// One-click rehearsal setup: trains Assets/images/UCI.jpg into a flat placeholder
    /// .zpt, points the scene's Image Tracking Target at it (replacing rocks.zpt), and
    /// wires Proxy.fbx in as visible content so something appears when the trained image
    /// is shown to the camera. This is a pipeline rehearsal only — not physically scaled,
    /// not the real facade target. Re-run Phase1ImageTrain against the real trained image
    /// once the physical QR mount + reference photo are finalized.
    /// </summary>
    public static class Phase1Rehearsal
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SourceImagePath = "Assets/images/UCI.jpg";
        private const string ProxyFbxPath = "Assets/FBX/Proxy.fbx";

        [MenuItem("ARReveal/Rehearsal/Train UCI Image + Wire Proxy Mesh")]
        public static void Run()
        {
            RunInternal(useCube: false);
        }

        [MenuItem("ARReveal/Rehearsal/Train UCI Image + Wire Test Cube")]
        public static void RunWithCube()
        {
            RunInternal(useCube: true);
        }

        private static void RunInternal(bool useCube)
        {
            string fullImagePath = Path.GetFullPath(SourceImagePath);
            string zptPath = Phase1ImageTrain.TrainImage(fullImagePath);
            if (zptPath == null)
            {
                Debug.LogError("[Phase1Rehearsal] Aborting - training failed.");
                return;
            }
            string zptFilename = Path.GetFileName(zptPath);

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var target = Object.FindFirstObjectByType<ZapparImageTrackingTarget>();
            if (target == null)
            {
                Debug.LogError("[Phase1Rehearsal] No ZapparImageTrackingTarget found in scene - run Phase1SceneSetup first.");
                return;
            }

            target.Target = zptFilename;
            Debug.Log("[Phase1Rehearsal] Tracking target now set to: " + zptFilename);

            // Clear any previously-wired content so this is safe to re-run either way.
            var existingProxy = target.transform.Find("Proxy");
            if (existingProxy != null) Object.DestroyImmediate(existingProxy.gameObject);
            var existingCube = target.transform.Find("TestCube");
            if (existingCube != null) Object.DestroyImmediate(existingCube.gameObject);

            if (useCube)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "TestCube";
                cube.transform.SetParent(target.transform, false);
                cube.transform.localPosition = new Vector3(0, 0.05f, 0);
                cube.transform.localScale = Vector3.one * 0.1f;
                Debug.Log("[Phase1Rehearsal] Test cube wired in under tracking target " +
                    "(0.1 unit cube, no scale/material unknowns - pure pipeline sanity check).");
            }
            else
            {
                var proxyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProxyFbxPath);
                if (proxyPrefab == null)
                {
                    Debug.LogError("[Phase1Rehearsal] Could not load " + ProxyFbxPath);
                }
                else
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(proxyPrefab);
                    instance.name = "Proxy";
                    instance.transform.SetParent(target.transform, false);
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;
                    Debug.Log("[Phase1Rehearsal] Proxy.fbx wired in under tracking target. " +
                        "Note: FBX materials/scale are whatever the raw import gave us - not " +
                        "validated against the 25.6m reference yet, that's real Phase 2 work.");
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Phase1Rehearsal] Done. Rebuild to try it.");
        }
    }
}
