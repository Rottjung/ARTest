using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Zappar;
using ARReveal;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Builds a fresh "BuildingTest" scene for the Bangkok stand-in rehearsal - separate
    /// from the SampleScene UCI rehearsal so the two don't collide. Trains the given
    /// source image, wires a Zappar rear camera (with correct URP camera stacking from
    /// the start), an Image Tracking Target pointed at it, and MajimeCup.fbx with the
    /// CupJiggle pop/idle-wobble behaviour attached.
    /// </summary>
    public static class BuildingTestSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/BuildingTest.unity";
        private const string SourceImagePath = "Assets/images/MajimeKiosk.jpg";
        private const string CupFbxPath = "Assets/FBX/MajimeCup.fbx";

        [MenuItem("ARReveal/Building Test/Train Image + Build Scene")]
        public static void Run()
        {
            string fullImagePath = Path.GetFullPath(SourceImagePath);
            if (!File.Exists(fullImagePath))
            {
                Debug.LogError("[BuildingTestSetup] Source image not found at " + SourceImagePath +
                    " - place it there first (same way as UCI.jpg).");
                return;
            }

            string zptPath = ARReveal.EditorTools.Phase1ImageTrain.TrainImage(fullImagePath);
            if (zptPath == null)
            {
                Debug.LogError("[BuildingTestSetup] Aborting - training failed.");
                return;
            }
            string zptFilename = Path.GetFileName(zptPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var light = new GameObject("Directional Light", typeof(Light));
            light.GetComponent<Light>().type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            GameObject zCam = CreateZapparRearCamera();
            GameObject zTarget = CreateImageTrackingTarget(zptFilename);
            WireCupContent(zTarget);

            RenderSettings.skybox = null;
            RenderSettings.ambientIntensity = 0f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(150f / 255f, 150f / 255f, 150f / 255f);
            Lightmapping.bakedGI = false;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ScenePath)));
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Add to build settings so it's actually included when we build.
            var scenes = EditorBuildSettings.scenes;
            bool alreadyIn = System.Array.Exists(scenes, s => s.path == ScenePath);
            if (!alreadyIn)
            {
                var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes)
                {
                    new EditorBuildSettingsScene(ScenePath, true)
                };
                EditorBuildSettings.scenes = list.ToArray();
            }

            Debug.Log("[BuildingTestSetup] Done. Scene: " + ScenePath + " | Target: " + zptFilename +
                " | Content: " + zTarget.name + "/" + CupFbxPath);
        }

        private static GameObject CreateZapparRearCamera()
        {
            GameObject go = new GameObject("Zappar Camera", typeof(Camera), typeof(ZapparCamera));
            GameObject bg = new GameObject("Zappar Camera Background", typeof(Camera), typeof(ZapparCameraBackground));

            var bgCam = bg.GetComponent<Camera>();
            bgCam.clearFlags = CameraClearFlags.SolidColor;
            bgCam.backgroundColor = Color.black;
            bgCam.cullingMask = 0;
            bg.tag = "MainCamera";
            bg.transform.SetParent(go.transform);

            var cam = go.GetComponent<Camera>();
            cam.nearClipPlane = 0.01f;
            cam.clearFlags = CameraClearFlags.Nothing;
            cam.depth = 1;

            var zCam = go.GetComponent<ZapparCamera>();
            zCam.UseFrontFacingCamera = false;
            zCam.MirrorCamera = false;

            // URP camera stacking - required for the passthrough feed and content to
            // composite correctly (see Phase1 postmortem: without this you get a flat
            // colour fill instead of camera passthrough).
            var contentData = cam.GetUniversalAdditionalCameraData();
            contentData.renderType = CameraRenderType.Overlay;

            var bgData = bgCam.GetUniversalAdditionalCameraData();
            bgData.renderType = CameraRenderType.Base;
            bgData.renderShadows = false;
            bgCam.depth = -1;
            bgCam.useOcclusionCulling = false;
            bgData.cameraStack.Clear();
            bgData.cameraStack.Add(cam);

            return go;
        }

        private static GameObject CreateImageTrackingTarget(string zptFilename)
        {
            GameObject go = new GameObject("Zappar Image Tracking Target", typeof(ZapparImageTrackingTarget));
            var target = go.GetComponent<ZapparImageTrackingTarget>();
            target.Target = zptFilename;
            target.Orientation = ZapparImageTrackingTarget.PlaneOrientation.Vertical;
            return go;
        }

        private static void WireCupContent(GameObject target)
        {
            var cupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CupFbxPath);
            if (cupPrefab == null)
            {
                Debug.LogError("[BuildingTestSetup] Could not load " + CupFbxPath);
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(cupPrefab);
            instance.name = "MajimeCup";
            instance.transform.SetParent(target.transform, false);
            // No forced position/scale - place and scale by hand in the Editor.
            instance.AddComponent<CupJiggle>();
        }
    }
}
