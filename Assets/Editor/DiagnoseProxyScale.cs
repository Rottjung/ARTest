using UnityEditor;
using UnityEngine;

namespace ARReveal.EditorTools
{
    public static class DiagnoseProxyScale
    {
        [MenuItem("ARReveal/Fix/Diagnose UCIProxy Scale")]
        public static void Run()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FBX/Proxy.fbx");
            if (prefab == null)
            {
                Debug.LogError("[DiagnoseProxyScale] Could not load UCIProxy.fbx");
                return;
            }

            var renderers = prefab.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0)
            {
                Debug.LogError("[DiagnoseProxyScale] No MeshRenderers found on the prefab.");
                return;
            }

            Bounds combined = renderers[0].bounds;
            foreach (var r in renderers) combined.Encapsulate(r.bounds);

            Debug.Log("[DiagnoseProxyScale] Prefab root local scale: " + prefab.transform.localScale +
                " | Combined renderer bounds size (world, at prefab default transform): " + combined.size +
                " | Expected from Blender: (35.07, 26.5, ~18-20)");

            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                Debug.Log("[DiagnoseProxyScale] " + mf.name + " mesh.bounds.size (local, unscaled mesh data): " +
                    (mesh != null ? mesh.bounds.size.ToString() : "null") +
                    " | transform.localScale: " + mf.transform.localScale +
                    " | transform.lossyScale: " + mf.transform.lossyScale);
            }
        }
    }
}
