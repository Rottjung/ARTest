using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Offline reference scene: recreates LeftCornerUCI.JPG's camera view in 3D against
    /// Proxy.fbx at real-world scale, so you can nudge the camera by eye and confirm it
    /// against the actual photo. Not AR - plain camera, no Zappar tracking involved.
    ///
    /// FOV is computed exactly from the photo's EXIF (iPhone 14 Pro Max ultra-wide,
    /// 14mm 35mm-equivalent -> 102.06 deg horizontal / 85.67 deg vertical) - not a guess.
    /// Proxy scale is exact (real height 26.5m / mesh's own height 31.81 = 0.833) - not
    /// a guess. Camera POSITION/ROTATION below is a reasonable starting estimate based on
    /// the architecture (plaza-level, roughly in front-left of the tower, tilted up to
    /// match the photo's strong upward angle) - this is the part you should nudge by eye.
    /// </summary>
    public static class CameraMatchTestSetup
    {
        private const string ScenePath = "Assets/Scenes/CameraMatchTest.unity";
        private const string ProxyFbxPath = "Assets/FBX/Proxy.fbx";

        private const float RealHeightMeters = 26.5f;
        private const float MeshHeightUnits = 31.81f;
        private const float VerticalFovDegrees = 85.67f;

        [MenuItem("ARReveal/Camera Match/Build Test Scene")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var light = new GameObject("Directional Light", typeof(Light));
            light.GetComponent<Light>().type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            var proxyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProxyFbxPath);
            if (proxyPrefab == null)
            {
                Debug.LogError("[CameraMatchTestSetup] Could not load " + ProxyFbxPath);
                return;
            }
            var proxy = (GameObject)PrefabUtility.InstantiatePrefab(proxyPrefab);
            proxy.name = "Proxy";
            float scale = RealHeightMeters / MeshHeightUnits;
            proxy.transform.position = Vector3.zero;
            proxy.transform.localScale = Vector3.one * scale;

            var camGo = new GameObject("Match Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.fieldOfView = VerticalFovDegrees; // exact, from EXIF - Unity's fieldOfView is vertical

            // Starting estimate: the mesh's tower sits at its bounding box's negative-X
            // edge (confirmed by render); after Blender->Unity axis conversion the
            // photographed face's normal points toward -Z, so "in front of" the building
            // is more-negative-Z. Placed roughly at photographer eye height, slightly
            // left of the tower, out in the plaza, tilted up and angled toward the corner.
            camGo.transform.position = new Vector3(-32f, 1.6f, -18f);
            camGo.transform.rotation = Quaternion.Euler(28f, 35f, 0f);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ScenePath)));
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log("[CameraMatchTestSetup] Done. Proxy scaled by " + scale.ToString("F4") +
                " (real height " + RealHeightMeters + "m). Camera FOV=" + VerticalFovDegrees +
                " (exact, from EXIF). Camera position/rotation is a starting estimate - " +
                "nudge by eye against Assets/images/LeftCornerUCI.JPG.");
        }
    }
}
