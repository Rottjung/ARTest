using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Builds a fresh "UCI-RE" scene: trains LeftCornerUCI_cropped.jpg at 1024px,
    /// wires a Zappar rear camera (correct URP Base/Overlay stacking from the start),
    /// an Image Tracking Target (Vertical - real facade, not tabletop) pointed at it,
    /// and the on-screen tracking debug overlay. No content wired yet - bare tracking
    /// scaffold only, matching what was asked for.
    /// </summary>
    public static class UCIRESceneSetup
    {
        private const string ScenePath = "Assets/Scenes/UCI-RE.unity";
        private const string SourceImagePath = "Assets/images/LeftCornerUCI_cropped.jpg";

        [MenuItem("ARReveal/UCI-RE/Train Image + Build Scene")]
        public static void Run()
        {
            string fullImagePath = Path.GetFullPath(SourceImagePath);
            string zptPath = Phase1ImageTrain.TrainImage(fullImagePath, maxTrainSize: 1024);
            if (zptPath == null)
            {
                Debug.LogError("[UCIRESceneSetup] Aborting - training failed.");
                return;
            }
            string zptFilename = Path.GetFileName(zptPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var light = new GameObject("Directional Light", typeof(Light));
            light.GetComponent<Light>().type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            GameObject zCam = CreateZapparRearCamera();
            GameObject zTarget = CreateImageTrackingTarget(zptFilename);
            zTarget.AddComponent<TrackingDebugOverlay>();

            RenderSettings.skybox = null;
            RenderSettings.ambientIntensity = 0f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(150f / 255f, 150f / 255f, 150f / 255f);
            Lightmapping.bakedGI = false;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ScenePath)));
            EditorSceneManager.SaveScene(scene, ScenePath);

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

            Debug.Log("[UCIRESceneSetup] Done. Scene: " + ScenePath + " | Target: " + zptFilename);
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
    }
}
