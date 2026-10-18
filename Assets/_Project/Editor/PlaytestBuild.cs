using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Windows build for external playtests: Builds/Playtest/ShardsOfTheLimit.exe plus a zip next to it.
/// Run: Unity -batchmode -projectPath ... -executeMethod PlaytestBuild.BuildWindows -quit
/// Reports of every battle land in Builds/Playtest/Playtests/.
/// </summary>
public static class PlaytestBuild
{
    private const string Folder = "Builds/Playtest";
    private const string MainScene = "Assets/_Project/Scenes/PrototypeBattle.unity";

    public static void BuildWindows()
    {
        KeepRuntimeShaders();
        string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
        if (!scenes.Contains(MainScene)) scenes = new[] { MainScene }.Concat(scenes).ToArray();
        else scenes = new[] { MainScene }.Concat(scenes.Where(scene => scene != MainScene)).ToArray();

        if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Folder + "/ShardsOfTheLimit.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        Debug.Log($"[PlaytestBuild] {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB, " +
                  $"{report.summary.totalErrors} errors");
        if (report.summary.result != BuildResult.Succeeded)
        {
            EditorApplication.Exit(1);
            return;
        }

        foreach (string debugFolder in Directory.GetDirectories(Folder, "*DoNotShip*")) Directory.Delete(debugFolder, true);
        string zip = "Builds/ShardsOfTheLimit_Playtest.zip";
        if (File.Exists(zip)) File.Delete(zip);
        ZipFile.CreateFromDirectory(Folder, zip, System.IO.Compression.CompressionLevel.Optimal, false);
        Debug.Log($"[PlaytestBuild] Zip: {Path.GetFullPath(zip)} ({new FileInfo(zip).Length / (1024 * 1024)} MB)");
    }

    /// <summary>
    /// Shaders found only by name at runtime (Shader.Find) are stripped from a player unless an asset references them;
    /// a material in Resources keeps the VFX particle shader in the build.
    /// </summary>
    private static void KeepRuntimeShaders()
    {
        Keep("Shards/VfxParticle", "Assets/_Project/Resources/Prototype/Rendering/KeepVfxParticleShader.mat");
        Keep("Shards/IconGradientMap", "Assets/_Project/Resources/Prototype/Rendering/KeepIconGradientMapShader.mat");
        AssetDatabase.SaveAssets();
    }

    private static void Keep(string shaderName, string path)
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
        Shader shader = Shader.Find(shaderName);
        if (shader == null) return;
        AssetDatabase.CreateAsset(new Material(shader) { name = Path.GetFileNameWithoutExtension(path) }, path);
    }
}