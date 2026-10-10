using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SwissPoliceSim.Editor
{
    /// <summary>Creates the Phase-1 scene programmatically (no hand-written YAML) and makes sure URP is active.</summary>
    public static class Phase1SceneBuilder
    {
        const string ScenePath = "Assets/SwissPoliceSim/Scenes/Phase1_PilotBern.unity";
        const string SettingsDir = "Assets/SwissPoliceSim/Settings";

        [MenuItem("Swiss Police/Create Phase-1 Scene")]
        public static void CreateScene()
        {
            EnsureUrp();
            PlayerSettings.colorSpace = ColorSpace.Linear;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var go = new GameObject("PilotBootstrap");
            go.AddComponent<PilotBootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);

            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == ScenePath)) list.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("Scene created: " + ScenePath + " – press Play.");
        }

        static void EnsureUrp()
        {
            if (GraphicsSettings.defaultRenderPipeline != null) return;
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rd, SettingsDir + "/URP_Renderer.asset");
                var asset = UniversalRenderPipelineAsset.Create(rd);
                AssetDatabase.CreateAsset(asset, SettingsDir + "/URP_Asset.asset");
                GraphicsSettings.defaultRenderPipeline = asset;
                QualitySettings.renderPipeline = asset;
                AssetDatabase.SaveAssets();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Could not create a URP asset automatically (" + e.Message + "). " +
                                 "Create one via Assets ▸ Create ▸ Rendering ▸ URP Asset (with Universal Renderer) and assign it in Project Settings ▸ Graphics.");
            }
        }
    }
}
