using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ARReveal;

namespace ARReveal.EditorTools
{
    /// <summary>
    /// Fast-iteration scene for tuning the tentacle's procedural animation - plain
    /// camera, no Zappar/AR tracking, so testing is "open scene, press Play" instead
    /// of a full build+deploy cycle. Not a rehearsal of the AR pipeline, purely for
    /// dialling in the grow/idle/reach feel.
    /// </summary>
    public static class TentacleTestSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/TentacleTest.unity";
        private const string TentaclePrefabPath = "Assets/FBX/Tentacle/Tentacle_01(Rigged).fbx";

        [MenuItem("ARReveal/Tentacle/Build Test Scene")]
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0, 1.2f, -2.5f);
            camGo.transform.rotation = Quaternion.Euler(10, 0, 0);

            var light = new GameObject("Directional Light", typeof(Light));
            light.GetComponent<Light>().type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            var tentaclePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TentaclePrefabPath);
            if (tentaclePrefab == null)
            {
                Debug.LogError("[TentacleTestSceneSetup] Could not load " + TentaclePrefabPath);
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(tentaclePrefab);
            instance.name = "Tentacle_01";
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;

            var rootBone = FindBoneRecursive(instance.transform, "Bone");
            if (rootBone == null)
            {
                Debug.LogError("[TentacleTestSceneSetup] Could not find a bone named 'Bone' under " + instance.name +
                    " - check the actual bone name in the imported hierarchy and wire RootBone by hand.");
            }

            var controller = instance.AddComponent<TentacleController>();
            controller.RootBone = rootBone;

            var bootstrap = instance.AddComponent<TentacleGrowOnStart>();
            bootstrap.DelaySeconds = 0.5f;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ScenePath)));
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log("[TentacleTestSceneSetup] Done. Scene: " + ScenePath +
                (rootBone != null ? " | RootBone: " + rootBone.name : " | RootBone NOT FOUND - wire manually"));
        }

        private static Transform FindBoneRecursive(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var found = FindBoneRecursive(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
