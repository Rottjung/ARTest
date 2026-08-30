using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Zappar;

namespace ARReveal.EditorTools
{
    public static class DiagnosePreviewTransform
    {
        [MenuItem("ARReveal/Fix/Diagnose Preview Object Transform")]
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
            var target = Object.FindFirstObjectByType<ZapparImageTrackingTarget>();
            if (target == null)
            {
                Debug.LogError("[DiagnosePreviewTransform] No ZapparImageTrackingTarget in the active scene.");
                return;
            }

            Debug.Log("[DiagnosePreviewTransform] Target: " + target.name + " | Target.Target=" + target.Target +
                " | PreviewImageObject assigned: " + (target.PreviewImageObject != null));

            var preview = target.PreviewImageObject != null
                ? target.PreviewImageObject.transform
                : target.transform.Find("Preview Object");

            if (preview == null)
            {
                Debug.LogError("[DiagnosePreviewTransform] No Preview Object found under " + target.name);
                return;
            }

            Debug.Log("[DiagnosePreviewTransform] Preview Object local position: " + preview.localPosition +
                " | local rotation (euler): " + preview.localEulerAngles +
                " | local scale: " + preview.localScale);

            var mf = preview.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Debug.Log("[DiagnosePreviewTransform] Preview mesh.bounds: center=" + mf.sharedMesh.bounds.center +
                    " size=" + mf.sharedMesh.bounds.size);
            }

            var mr = preview.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Debug.Log("[DiagnosePreviewTransform] Preview world-space renderer bounds: center=" +
                    mr.bounds.center + " size=" + mr.bounds.size);
            }
        }
    }
}
