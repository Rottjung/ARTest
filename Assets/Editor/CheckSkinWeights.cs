using System.Text;
using UnityEditor;
using UnityEngine;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Diagnostic: select a Mesh asset (or a GameObject/SkinnedMeshRenderer using
    /// one) in the Project or Hierarchy, then run this - logs every vertex whose
    /// bone weights sum to (near) zero, i.e. a vertex with NO influence from any
    /// bone at all, which won't move/deform with the skeleton (stays frozen at the
    /// mesh's bind pose while everything around it bends) - exactly what a single
    /// unweighted vertex at a tentacle's tip would look like: one point staying
    /// put while the rest of the tip curls around it.
    /// </summary>
    public static class CheckSkinWeights
    {
        /// <summary>
        /// Select a GameObject with a SkinnedMeshRenderer (not just the Mesh asset -
        /// this needs the live renderer's own .bones array, which only exists on a
        /// scene/prefab instance) and run this. Dumps every bone in
        /// SkinnedMeshRenderer.bones IN INDEX ORDER - the exact array BoneWeight.
        /// boneIndex0-3 index into - with each one's full hierarchy path and
        /// immediate parent, plus whether skin.rootBone actually matches bones[0].
        /// This is the authoritative source for "which real Transform is bone 9",
        /// rather than guessing from bone naming/position in the Hierarchy view.
        /// </summary>
        [MenuItem("Tools/ARReveal/Dump Skinned Mesh Bone List of Selection")]
        public static void DumpBones()
        {
            var go = Selection.activeGameObject;
            var skin = go != null ? go.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (skin == null)
            {
                Debug.LogError("[CheckSkinWeights] Select a GameObject with a SkinnedMeshRenderer (or a parent of one) in the Hierarchy first.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[CheckSkinWeights] '{skin.name}': rootBone = '{(skin.rootBone != null ? GetPath(skin.rootBone) : "NULL")}', {skin.bones.Length} bones in skin.bones:");
            for (int i = 0; i < skin.bones.Length; i++)
            {
                var b = skin.bones[i];
                if (b == null) { sb.AppendLine($"  [{i}] NULL"); continue; }
                string parent = b.parent != null ? b.parent.name : "(none)";
                bool isRootBoneMatch = skin.rootBone != null && b == skin.rootBone;
                sb.AppendLine($"  [{i}] '{b.name}' - path: {GetPath(b)} - parent: '{parent}' - childCount: {b.childCount}{(isRootBoneMatch ? "  <-- skin.rootBone" : "")}");
            }
            Debug.Log(sb.ToString());
        }

        private static string GetPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var p = t.parent;
            while (p != null)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }
            return sb.ToString();
        }

        [MenuItem("Tools/ARReveal/Check Skin Weights of Selection")]
        public static void Check()
        {
            var mesh = GetSelectedMesh();
            if (mesh == null)
            {
                Debug.LogError("[CheckSkinWeights] Select a Mesh asset, or a GameObject/SkinnedMeshRenderer that uses one, in the Project or Hierarchy first.");
                return;
            }

            var weights = mesh.boneWeights;
            var verts = mesh.vertices;
            if (weights == null || weights.Length == 0)
            {
                Debug.LogError($"[CheckSkinWeights] '{mesh.name}' has no bone weights at all (mesh.boneWeights is empty) - not a skinned mesh, or skinning data wasn't imported.");
                return;
            }
            if (weights.Length != verts.Length)
            {
                Debug.LogWarning($"[CheckSkinWeights] '{mesh.name}': weight count ({weights.Length}) != vertex count ({verts.Length}) - unusual, reading what's there anyway.");
            }

            Bounds b = mesh.bounds;
            var flagged = new StringBuilder();
            int zeroCount = 0;
            int n = Mathf.Min(weights.Length, verts.Length);
            var sums = new float[n];
            for (int i = 0; i < n; i++)
            {
                sums[i] = weights[i].weight0 + weights[i].weight1 + weights[i].weight2 + weights[i].weight3;
                if (sums[i] < 0.001f)
                {
                    zeroCount++;
                    flagged.AppendLine($"  vert {i} at local {verts[i]:F4} - weight sum {sums[i]:F4} (would-be bones: {weights[i].boneIndex0},{weights[i].boneIndex1},{weights[i].boneIndex2},{weights[i].boneIndex3})");
                }
            }

            // Which local axis the mesh is longest along - that's the tentacle's
            // length axis, so the "tip" is whichever extreme end of it.
            int axis = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : (b.size.y >= b.size.z ? 1 : 2);
            string axisName = axis == 0 ? "X" : axis == 1 ? "Y" : "Z";
            int maxIdx = 0, minIdx = 0;
            for (int i = 1; i < n; i++)
            {
                if (verts[i][axis] > verts[maxIdx][axis]) maxIdx = i;
                if (verts[i][axis] < verts[minIdx][axis]) minIdx = i;
            }

            // Lowest-weight vertices overall, regardless of the hard cutoff above -
            // catches "very low but not literally zero" weighting too.
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            System.Array.Sort(order, (a, c) => sums[a].CompareTo(sums[c]));

            var sb = new StringBuilder();
            sb.AppendLine($"[CheckSkinWeights] '{mesh.name}': {n} vertices checked, mesh bounds center {b.center:F3} size {b.size:F3} (longest axis: local {axisName}).");
            sb.AppendLine($"Exactly-unweighted (sum < 0.001) vertices: {zeroCount}");
            if (zeroCount > 0) sb.Append(flagged);

            sb.AppendLine("Lowest-weight vertices overall (top 10, any nonzero amount):");
            for (int k = 0; k < Mathf.Min(10, n); k++)
            {
                int i = order[k];
                sb.AppendLine($"  vert {i} at local {verts[i]:F4} - weight sum {sums[i]:F4} (bones {weights[i].boneIndex0}:{weights[i].weight0:F3}, {weights[i].boneIndex1}:{weights[i].weight1:F3}, {weights[i].boneIndex2}:{weights[i].weight2:F3}, {weights[i].boneIndex3}:{weights[i].weight3:F3})");
            }

            sb.AppendLine($"Extreme-{axisName}-max vertex (tip end): vert {maxIdx} at {verts[maxIdx]:F4}, weight sum {sums[maxIdx]:F4}, bones {weights[maxIdx].boneIndex0}:{weights[maxIdx].weight0:F3}, {weights[maxIdx].boneIndex1}:{weights[maxIdx].weight1:F3}, {weights[maxIdx].boneIndex2}:{weights[maxIdx].weight2:F3}, {weights[maxIdx].boneIndex3}:{weights[maxIdx].weight3:F3}");
            sb.AppendLine($"Extreme-{axisName}-min vertex (base end): vert {minIdx} at {verts[minIdx]:F4}, weight sum {sums[minIdx]:F4}, bones {weights[minIdx].boneIndex0}:{weights[minIdx].weight0:F3}, {weights[minIdx].boneIndex1}:{weights[minIdx].weight1:F3}, {weights[minIdx].boneIndex2}:{weights[minIdx].weight2:F3}, {weights[minIdx].boneIndex3}:{weights[minIdx].weight3:F3}");

            // The tip is rarely a single vertex - it's usually a small cluster/cap
            // with several overlapping verts (UV seam, cap fan, etc.) - list every
            // vertex within the last 2% of the length range near the tip end, so an
            // odd-one-out among that cluster (not necessarily THE single most
            // extreme vertex checked above) shows up too.
            float lo = Mathf.Min(verts[minIdx][axis], verts[maxIdx][axis]);
            float hi = Mathf.Max(verts[minIdx][axis], verts[maxIdx][axis]);
            float tipThreshold = hi - (hi - lo) * 0.02f;
            sb.AppendLine($"All vertices within the last 2% of the {axisName} range near the tip (>= {tipThreshold:F4}):");
            int tipClusterCount = 0;
            for (int i = 0; i < n; i++)
            {
                if (verts[i][axis] >= tipThreshold)
                {
                    tipClusterCount++;
                    sb.AppendLine($"  vert {i} at {verts[i]:F4} - weight sum {sums[i]:F4}, bones {weights[i].boneIndex0}:{weights[i].weight0:F3}, {weights[i].boneIndex1}:{weights[i].weight1:F3}, {weights[i].boneIndex2}:{weights[i].weight2:F3}, {weights[i].boneIndex3}:{weights[i].weight3:F3}");
                }
            }
            sb.AppendLine($"  ({tipClusterCount} vertices in that cluster)");

            Debug.Log(sb.ToString());
        }

        private static Mesh GetSelectedMesh()
        {
            var obj = Selection.activeObject;
            if (obj is Mesh m) return m;
            if (obj is GameObject go)
            {
                var skin = go.GetComponentInChildren<SkinnedMeshRenderer>();
                if (skin != null && skin.sharedMesh != null) return skin.sharedMesh;
                var mf = go.GetComponentInChildren<MeshFilter>();
                if (mf != null) return mf.sharedMesh;
            }
            return null;
        }
    }
}
