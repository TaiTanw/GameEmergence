using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Build;
public static class LocalWindowsBuild
{
    public static void Run()
    {
        var output = Environment.GetEnvironmentVariable("GAMEEMERGENCE_BUILD_PATH");
        if (string.IsNullOrEmpty(output)) throw new Exception("GAMEEMERGENCE_BUILD_PATH is required");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult content);
        if (!string.IsNullOrEmpty(content.Error)) throw new Exception(content.Error);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            options = Environment.GetEnvironmentVariable("GAMEEMERGENCE_DEVELOPMENT_BUILD")=="1" ? BuildOptions.Development : BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Build failed: " + report.summary.result);
        UnityEngine.Debug.Log("LOCAL_BUILD_SUCCESS " + output);
    }
}
