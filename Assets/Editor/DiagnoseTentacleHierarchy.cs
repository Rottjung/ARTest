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

        private static void Dump(Transform t, int depth)
        {
            var comps = t.GetComponents<Component>();
            var names = new System.Text.StringBuilder();
            foreach (var c in comps) names.Append(c == null ? "MISSING" : c.GetType().Name).Append(", ");
            Debug.Log(new string(' ', depth * 2) + t.name + " [" + names + "]");
            for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), depth + 1);
        }
    }
}
