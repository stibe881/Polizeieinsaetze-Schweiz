using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SwissPoliceSim.Editor
{
    /// <summary>One-click project configuration: Linear colour space, a URP asset with the renderer features the game expects, quality settings, and tags. Runs once automatically on first open.</summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        const string Flag = "SPS.ProjectSetup.v2";
        static ProjectSetup() { EditorApplication.delayCall += () => { if (!EditorPrefs.GetBool(Flag, false)) { Apply(); EditorPrefs.SetBool(Flag, true); } }; }

        [MenuItem("Swiss Police Sim/Projekt einrichten")]
        public static void Apply()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1920; PlayerSettings.defaultScreenHeight = 1080; PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.companyName = "SwissPoliceSim"; PlayerSettings.productName = "Swiss Police Simulator";
            PlayerSettings.runInBackground = true;
            Directory.CreateDirectory("Assets/SwissPoliceSim/Settings");
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/SwissPoliceSim/Settings/SPS_Renderer.asset");
            if (rendererData == null) { rendererData = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(rendererData, "Assets/SwissPoliceSim/Settings/SPS_Renderer.asset"); }
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/SwissPoliceSim/Settings/SPS_URP.asset");
            if (asset == null) { asset = UniversalRenderPipelineAsset.Create(rendererData); AssetDatabase.CreateAsset(asset, "Assets/SwissPoliceSim/Settings/SPS_URP.asset"); }
            asset.supportsHDR = true; asset.msaaSampleCount = 4; asset.shadowDistance = 140f; asset.shadowCascadeCount = 4; asset.supportsCameraDepthTexture = true; asset.supportsCameraOpaqueTexture = false; asset.renderScale = 1f;
            EditorUtility.SetDirty(asset);
            AddSsao(rendererData);
            GraphicsSettings.defaultRenderPipeline = asset;
            for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = asset; }
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, true);
            AssetDatabase.SaveAssets();
            Debug.Log("Swiss Police Sim: Projekt eingerichtet (Linear, URP-Asset, SSAO). Szene öffnen und Play drücken – der Bootstrap baut alles zur Laufzeit.");
        }

        /// <summary>Screen-space ambient occlusion via the public renderer-feature type; added through reflection-free API where available.</summary>
        static void AddSsao(UniversalRendererData data)
        {
            foreach (var f in data.rendererFeatures) if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion") return;
            var type = System.Type.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion, Unity.RenderPipelines.Universal.Runtime"); if (type == null) return;
            var feature = ScriptableObject.CreateInstance(type) as ScriptableRendererFeature; if (feature == null) return; feature.name = "SSAO"; AssetDatabase.AddObjectToAsset(feature, data);
            data.rendererFeatures.Add(feature); EditorUtility.SetDirty(data);
        }
    }
}
