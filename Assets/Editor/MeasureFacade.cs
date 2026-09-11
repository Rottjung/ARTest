using UnityEditor;
using UnityEngine;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// One-off diagnostic: logs the world-space bounds (position relative to the
    /// scene's QR-anchored origin, and real-world size in meters) of the building
    /// model and any of its named sub-materials whose renderer can be isolated -
    /// used to get real numbers for placing/scaling a second (building-facade)
    /// image tracking target, instead of guessing physical dimensions from photos.
    /// Safe/read-only - logs to Console only, never modifies the scene.
    /// </summary>
    public static class MeasureFacade
    {
        [MenuItem("ARReveal/UCI-RE-AR/Measure Building Bounds (logs to Console)")]
        public static void Run()
        {
            var go = GameObject.Find("Low_Poly_UCI_Cinima");
            if (go == null)
            {
                Debug.LogError("[MeasureFacade] Could not find 'Low_Poly_UCI_Cinima' in the open scene.");
                return;
            }

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogError("[MeasureFacade] No renderers found under Low_Poly_UCI_Cinima.");
                return;
            }

            Bounds combined = renderers[0].bounds;
            foreach (var r in renderers) combined.Encapsulate(r.bounds);

            Debug.Log($"[MeasureFacade] WHOLE BUILDING world bounds - " +
                $"center: {combined.center}, size (W x H x D meters): {combined.size}, " +
                $"min: {combined.min}, max: {combined.max}");

            // Renderer count/material breakdown, so we know if submeshes can be
            // isolated by material name for a tighter measurement of just the
            // tracked facade region.
            Debug.Log($"[MeasureFacade] {renderers.Length} renderer(s) under the building. Listing each one's own bounds and material names:");
            foreach (var r in renderers)
            {
                var matNames = new System.Text.StringBuilder();
                foreach (var m in r.sharedMaterials) matNames.Append(m != null ? m.name : "null").Append(", ");
                Debug.Log($"[MeasureFacade]  - {r.gameObject.name}: bounds center {r.bounds.center}, size {r.bounds.size} | materials: {matNames}");

                // The whole combined mesh's bounds are inflated by unrelated
                // geometry (e.g. a big ground/plaza slab sharing the same mesh) -
                // per-submesh bounds (one submesh per material slot) isolate just
                // the triangles using each material, so we can measure the
                // specific facade region (Front_Use_01 etc) instead of the whole
                // building+context blob.
                var mf = r.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;

                Transform t = r.transform;
                Vector3[] localVerts = mesh.vertices;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int[] tris = mesh.GetTriangles(sub);
                    if (tris.Length == 0) continue;

                    Vector3 worldMin = t.TransformPoint(localVerts[tris[0]]);
                    Vector3 worldMax = worldMin;
                    foreach (int vi in tris)
                    {
                        Vector3 wp = t.TransformPoint(localVerts[vi]);
                        worldMin = Vector3.Min(worldMin, wp);
                        worldMax = Vector3.Max(worldMax, wp);
                    }
                    string matName = (r.sharedMaterials.Length > sub && r.sharedMaterials[sub] != null)
                        ? r.sharedMaterials[sub].name : $"submesh{sub}";
                    Vector3 size = worldMax - worldMin;
                    Debug.Log($"[MeasureFacade]    submesh {sub} ('{matName}'): size (W x H x D meters) {size}, min {worldMin}, max {worldMax}");
                }
            }

            // Also log the QR (image tracking target) world position, so building
            // bounds above can be read as an offset FROM the QR directly.
            var qr = GameObject.Find("Zappar Ground Marker");
            if (qr != null)
                Debug.Log($"[MeasureFacade] QR ('Zappar Ground Marker') world position: {qr.transform.position}");
        }
    }
}
