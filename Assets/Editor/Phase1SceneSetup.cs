using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Zappar;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Phase 1 scaffold: wires a Zappar rear-facing camera and an Image Tracking Target
    /// (pointed at the sample "rocks.zpt" shipped with the Zappar package, as a placeholder
    /// until the real facade reference image is trained) into the main scene, and applies
    /// Zappar's recommended WebGL publish settings.
    ///
    /// Run headless via:
    ///   Unity.exe -batchmode -quit -projectPath <path> -buildTarget WebGL
    ///     -executeMethod ARReveal.EditorTools.Phase1SceneSetup.Run -logFile <path>
    ///
    /// Safe to re-run: it clears out any previously-created Zappar Camera / Image Tracking
    /// Target objects in the scene before recreating them.
    /// </summary>
    public static class Phase1SceneSetup
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string PlaceholderZpt = "rocks.zpt"; // ships with com.zappar.uar samples

        public static void Run()
        {
            Debug.Log("[Phase1Setup] Starting...");

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            RemoveExisting<Camera>("Main Camera");
            RemoveExisting<ZapparCamera>(null);
            RemoveExisting<ZapparImageTrackingTarget>(null);

            GameObject zCam = CreateZapparRearCamera();
            GameObject zTarget = CreateImageTrackingTarget(PlaceholderZpt);

            // Match Zappar's own "New AR Scene" ambient/skybox setup so the camera
            // pass-through isn't fighting a baked skybox.
            RenderSettings.skybox = null;
            RenderSettings.ambientIntensity = 0f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(150f / 255f, 150f / 255f, 150f / 255f);
            Lightmapping.bakedGI = false;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Phase1Setup] Scene configured: " + zCam.name + " + " + zTarget.name);

            ApplyWebGLPublishSettings();

            AssetDatabase.SaveAssets();
            Debug.Log("[Phase1Setup] Done.");
        }

        private static void RemoveExisting<T>(string exactNameOrNull) where T : Component
        {
            foreach (var comp in Object.FindObjectsByType<T>(FindObjectsSortMode.None))
            {
                var go = comp.gameObject;
                if (exactNameOrNull == null || go.name == exactNameOrNull)
                {
                    // walk up to the root so camera-background child objects go too
                    Object.DestroyImmediate(go.transform.root.gameObject);
                }
            }
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

            // URP camera stacking (required or the passthrough feed + rendered content
            // won't composite - without this you get a flat colour instead of the camera
            // background). Mirrors Zappar/Editor/Update Zappar Scene For SRP.
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
            string zptPath = Path.Combine(Application.streamingAssetsPath, zptFilename);
            if (!File.Exists(zptPath))
            {
                Debug.LogWarning("[Phase1Setup] Placeholder target '" + zptFilename +
                    "' not found in StreamingAssets — Target will be set but won't resolve until it exists.");
            }

            GameObject go = new GameObject("Zappar Image Tracking Target", typeof(ZapparImageTrackingTarget));
            var target = go.GetComponent<ZapparImageTrackingTarget>();
            target.Target = zptFilename;
            target.Orientation = ZapparImageTrackingTarget.PlaneOrientation.Flat;
            return go;
        }

        private static void ApplyWebGLPublishSettings()
        {
            // Mirrors Zappar/Editor/Update Project Settings To Publish (WebGLAll) so builds
            // use the settings the SDK vendor recommends: IL2CPP + stripping, Brotli
            // compression, the Zappar WebGL template, and WebGL2 (GLES3) graphics.
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.WebGL, ScriptingImplementation.IL2CPP);
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.WebGL, ManagedStrippingLevel.High);
            EditorUserBuildSettings.development = false;
            PlayerSettings.runInBackground = true;

            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.nameFilesAsHashes = true;
            PlayerSettings.WebGL.template = "PROJECT:Zappar";

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 });

            const string define = "ZAPPAR_SRP";
            string symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.WebGL);
            if (!symbols.Contains(define))
            {
                symbols = string.IsNullOrEmpty(symbols) ? define : symbols + ";" + define;
                PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.WebGL, symbols);
            }

            Debug.Log("[Phase1Setup] Applied Zappar WebGL publish settings (IL2CPP, Brotli, Zappar template, WebGL2, ZAPPAR_SRP).");
        }
    }
}
