using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ARReveal.EditorTools
{
    public static class DiagnoseTentacleHierarchy
    {
        [MenuItem("ARReveal/Wall Hole/Diagnose Tentacle_01 Hierarchy")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/TentacleTest.unity", OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                Dump(root.transform, 0);
        }

        [MenuItem("ARReveal/Wall Hole/Diagnose UCI-RE Hierarchy")]
        public static void RunUCIRE()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/UCI-RE.unity", OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                Dump(root.transform, 0);
        }

        private static void Dump(Transform t, int depth)
        {
            var comps = t.GetComponents<Component>();
            var names = new System.Text.StringBuilder();
            foreach (var c in comps) names.Append(c == null ? "MISSING" : c.GetType().Name).Append(", ");

            var extra = "";
            var tentacle = t.GetComponent<TentacleController>();
            if (tentacle != null) extra = " Style=" + tentacle.Style + " localScale=" + t.localScale;
            var growOnStart = t.GetComponent<TentacleGrowOnStart>();
            if (growOnStart != null) extra += " HAS_TentacleGrowOnStart";

            Debug.Log(new string(' ', depth * 2) + t.name + " [" + names + "]" + extra + " active=" + t.gameObject.activeSelf);
            for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), depth + 1);
        }
    }
}
