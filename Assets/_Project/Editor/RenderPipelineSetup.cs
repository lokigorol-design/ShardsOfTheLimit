using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// One-time switch of the prototype to URP: pipeline + renderer assets with SSAO, the battle post-processing
/// profile, and conversion of the authored Standard materials to URP Lit. Run explicitly:
/// Unity -batchmode -projectPath ... -executeMethod RenderPipelineSetup.Configure -quit
/// Safe to run again: existing assets are reused.
/// </summary>
public static class RenderPipelineSetup
{
    private const string SettingsFolder = "Assets/_Project/Settings";
    private const string PipelinePath = SettingsFolder + "/URP-Battle.asset";
    private const string RendererPath = SettingsFolder + "/URP-Battle-Renderer.asset";
    private const string ProfileFolder = "Assets/_Project/Resources/Prototype/Rendering";
    private const string ProfilePath = ProfileFolder + "/BattlePostProcess.asset";

    private static readonly string[] AuthoredMaterials =
    {
        "Assets/_Project/Art/Models/BlazeWorm/BlazeWorm.mat",
        "Assets/_Project/Art/Models/Blightgullet/Blightgullet.mat"
    };

    public static void Configure()
    {
        Directory.CreateDirectory(SettingsFolder);
        Directory.CreateDirectory(ProfileFolder);

        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rendererData == null)
        {
            rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, RendererPath);
            AddSsao(rendererData);
        }

        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
        if (pipeline == null)
        {
            pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(pipeline, PipelinePath);
        }

        var serialized = new SerializedObject(pipeline);
        SetValue(serialized, "m_SupportsHDR", true);
        SetValue(serialized, "m_MSAA", 4);
        SetValue(serialized, "m_MainLightShadowsSupported", true);
        SetValue(serialized, "m_MainLightShadowmapResolution", 4096);
        SetValue(serialized, "m_ShadowDistance", 45f);
        SetValue(serialized, "m_ShadowCascadeCount", 2);
        SetValue(serialized, "m_SoftShadowsSupported", true);
        SetValue(serialized, "m_SupportsCameraDepthTexture", true);
        SetValue(serialized, "m_SupportsCameraOpaqueTexture", false);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pipeline);

        GraphicsSettings.defaultRenderPipeline = pipeline;
        for (int level = 0; level < QualitySettings.names.Length; level++)
        {
            QualitySettings.SetQualityLevel(level, false);
            QualitySettings.renderPipeline = pipeline;
        }

        CreatePostProcessProfile();
        foreach (string path in AuthoredMaterials) ConvertToLit(path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[RenderPipelineSetup] URP configured.");
    }

    private static void AddSsao(UniversalRendererData rendererData)
    {
        Type ssaoType = Type.GetType(
            "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion, Unity.RenderPipelines.Universal.Runtime");
        if (ssaoType == null)
        {
            Debug.LogWarning("[RenderPipelineSetup] SSAO feature type not found; skipping.");
            return;
        }

        var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(ssaoType);
        feature.name = "SSAO";
        AssetDatabase.AddObjectToAsset(feature, rendererData);
        var serialized = new SerializedObject(rendererData);
        SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
        features.arraySize++;
        features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
        SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");
        if (map != null)
        {
            map.arraySize++;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        var ssaoSettings = new SerializedObject(feature);
        SetValue(ssaoSettings, "m_Settings.Intensity", 1.6f);
        SetValue(ssaoSettings, "m_Settings.Radius", 0.45f);
        ssaoSettings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rendererData);
    }

    private const string ComicMaterialPath = "Assets/_Project/Art/Shaders/ComicPost.mat";

    /// <summary>
    /// Adds the comic-book full-screen pass (Shards/ComicPost) to the battle renderer. Run explicitly:
    /// Unity -batchmode -projectPath ... -executeMethod RenderPipelineSetup.AddComicPass -quit
    /// Safe to run again: an existing "ComicPost" feature is reused.
    /// </summary>
    public static void AddComicPass()
    {
        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (rendererData == null)
        {
            Debug.LogError("[RenderPipelineSetup] Renderer asset not found; run Configure first.");
            return;
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(ComicMaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Shards/ComicPost")) { name = "ComicPost" };
            AssetDatabase.CreateAsset(material, ComicMaterialPath);
        }

        var feature = rendererData.rendererFeatures.Find(existing => existing != null && existing.name == "ComicPost")
            as FullScreenPassRendererFeature;
        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name = "ComicPost";
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            var serialized = new SerializedObject(rendererData);
            SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");
            if (map != null)
            {
                map.arraySize++;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
        feature.fetchColorBuffer = true;
        feature.requirements = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;
        feature.passMaterial = material;
        feature.passIndex = 0;
        feature.SetActive(true);
        EditorUtility.SetDirty(feature);
        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssets();
        Debug.Log("[RenderPipelineSetup] Comic pass configured.");
    }

    private static void CreatePostProcessProfile()
    {
        if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath) != null) return;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, ProfilePath);

        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(0.7f);
        bloom.threshold.Override(1.05f);
        bloom.scatter.Override(0.65f);
        var tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.Override(TonemappingMode.ACES);
        var color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.35f);
        color.contrast.Override(14f);
        color.saturation.Override(10f);
        var whiteBalance = profile.Add<WhiteBalance>(true);
        whiteBalance.temperature.Override(8f);
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.28f);
        vignette.smoothness.Override(0.45f);

        foreach (VolumeComponent component in profile.components)
        {
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
        }

        EditorUtility.SetDirty(profile);
    }

    private static void ConvertToLit(string path)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null || material.shader.name == "Universal Render Pipeline/Lit") return;
        Texture main = material.GetTexture("_MainTex");
        Texture normal = material.HasProperty("_BumpMap") ? material.GetTexture("_BumpMap") : null;
        Texture metallic = material.HasProperty("_MetallicGlossMap") ? material.GetTexture("_MetallicGlossMap") : null;
        Texture emission = material.HasProperty("_EmissionMap") ? material.GetTexture("_EmissionMap") : null;
        Color color = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
        Color emissionColor = material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black;

        material.shader = Shader.Find("Universal Render Pipeline/Lit");
        material.SetTexture("_BaseMap", main);
        material.SetTexture("_MainTex", main);
        material.SetColor("_BaseColor", color);
        if (normal != null)
        {
            material.SetTexture("_BumpMap", normal);
            material.EnableKeyword("_NORMALMAP");
        }

        if (metallic != null)
        {
            material.SetTexture("_MetallicGlossMap", metallic);
            material.SetFloat("_Smoothness", 1f);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
        }

        if (emission != null)
        {
            material.SetTexture("_EmissionMap", emission);
            material.SetColor("_EmissionColor", emissionColor == Color.black ? Color.white : emissionColor);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        EditorUtility.SetDirty(material);
    }

    private static void SetValue(SerializedObject serialized, string name, object value)
    {
        SerializedProperty property = serialized.FindProperty(name);
        if (property == null)
        {
            Debug.LogWarning($"[RenderPipelineSetup] Property {name} not found on {serialized.targetObject.name}.");
            return;
        }

        switch (value)
        {
            case bool flag: property.boolValue = flag; break;
            case int number when property.propertyType == SerializedPropertyType.Enum: property.enumValueFlag = number; break;
            case int number: property.intValue = number; break;
            case float real: property.floatValue = real; break;
        }
    }
}
