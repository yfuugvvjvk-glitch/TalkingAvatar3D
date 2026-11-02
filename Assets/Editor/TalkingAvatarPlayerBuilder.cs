using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class TalkingAvatarPlayerBuilder
{
    [MenuItem("Tools/Build Talking Avatar Player")]
    public static void BuildPlayerFromEditor()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        BuildPlayer(Path.Combine(projectRoot, "Build", "Latest", "TalkingAvatar3D.exe"));
    }

    public static void BuildPlayerFromCommandLine()
    {
        string outputPath = Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith("-talkingAvatarBuildPath=", StringComparison.Ordinal));
        if (outputPath == null)
            throw new BuildFailedException("Missing -talkingAvatarBuildPath argument.");

        BuildPlayer(outputPath.Substring("-talkingAvatarBuildPath=".Length).Trim('"'));
    }

    private static void BuildPlayer(string outputPath)
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();
        if (scenes.Length == 0)
            throw new BuildFailedException("No enabled scenes are configured in Build Settings.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });

        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException(
                $"Player build failed: {report.summary.result} ({report.summary.totalErrors} errors).");

        Debug.Log($"Talking Avatar player built: {outputPath}");
    }
}
