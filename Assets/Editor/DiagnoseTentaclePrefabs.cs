using UnityEditor;
using UnityEngine;

namespace ARReveal.EditorTools
{
    public static class DiagnoseTentaclePrefabs
    {
        [MenuItem("ARReveal/Wall Hole/Diagnose Tentacle Prefabs")]
        public static void Run()
        {
            foreach (var name in new[] { "Tentacle_01", "Tentacle_02(Rigged)", "Tentacle_03(Rigged)", "Tentacle_04(Rigged)" })
            {
                string path = "Assets/Prefabs/" + name + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Debug.Log("[DiagnosePrefabs] " + name + " - COULD NOT LOAD at " + path);
                    continue;
                }

                var controller = prefab.GetComponent<TentacleController>();
                if (controller == null)
                {
                    Debug.Log("[DiagnosePrefabs] " + name + " - no TentacleController on the root.");
                    continue;
                }

                Debug.Log("[DiagnosePrefabs] " + name + " - Style=" + controller.Style +
                    " ExtendAxis=" + controller.ExtendAxis + " localScale=" + prefab.transform.localScale);

                if (controller.RootBone == null)
                {
                    Debug.Log("  RootBone = NULL");
                    continue;
                }

                var sb = new System.Text.StringBuilder("  RootBone chain: ");
                var current = controller.RootBone;
                int count = 0;
                while (current != null && count < 30)
                {
                    sb.Append(current.name + "(children=" + current.childCount + ") -> ");
                    current = current.childCount > 0 ? current.GetChild(0) : null;
                    count++;
                }
                sb.Append("END (total=" + count + ")");
                Debug.Log(sb.ToString());
            }
        }
    }
}
