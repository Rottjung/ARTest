using UnityEngine;

namespace ARReveal
{
    /// <summary>
    /// Generates small, irregular "broken wall chunk" meshes at runtime by jittering
    /// Unity's built-in cube primitive per-corner, then recalculating normals for hard,
    /// faceted shading. No DCC round-trip needed and every chunk is unique per seed.
    ///
    /// Starting from the built-in cube (rather than hand-rolling face/triangle winding)
    /// guarantees correct winding/normals for free - only positions are touched.
    /// </summary>
    public static class DebrisChunkFactory
    {
        /// <summary>
        /// Builds one chunk mesh.
        /// size: roughly-final chunk footprint (local units, before jitter).
        /// irregularity: how much each corner is randomly displaced as a fraction of
        ///   size (0 = perfect box, ~0.5+ = quite jagged).
        /// depthScale: multiplies the local Z thickness before jitter (1 = cube,
        ///   ~0.2-0.5 = thin flake, matching the flat chips concrete/plaster actually
        ///   breaks into rather than chunky blocks).
        /// </summary>
        public static Mesh Build(float size, float irregularity, float depthScale, System.Random rng)
        {
            var source = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var mesh = Object.Instantiate(source);
            mesh.name = "DebrisChunk";

            var verts = mesh.vertices;
            var baseScale = new Vector3(1f, 1f, depthScale);

            // The built-in cube has 24 verts - 4 duplicates per corner (one per
            // adjoining face) so it can have hard-edged normals. Jitter every
            // duplicate of a given corner identically, or the faces would tear apart
            // at the seams.
            var cornerJitter = new System.Collections.Generic.Dictionary<Vector3, Vector3>();
            for (int i = 0; i < verts.Length; i++)
            {
                var corner = new Vector3(Mathf.Sign(verts[i].x), Mathf.Sign(verts[i].y), Mathf.Sign(verts[i].z));
                if (!cornerJitter.TryGetValue(corner, out var jitter))
                {
                    jitter = new Vector3(
                        (float)(rng.NextDouble() - 0.5) * 2f,
                        (float)(rng.NextDouble() - 0.5) * 2f,
                        (float)(rng.NextDouble() - 0.5) * 2f) * irregularity;
                    cornerJitter[corner] = jitter;
                }
                verts[i] = (Vector3.Scale(verts[i], baseScale) + jitter) * size;
            }

            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
