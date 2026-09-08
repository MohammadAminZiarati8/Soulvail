using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Soulvail.Editor.Build;

/// <summary>
/// The one way an Android player is produced from this project: a menu item and the
/// <see cref="BuildPipeline"/> call behind it, so a build is a repeatable action rather than a
/// dialog someone fills in from memory.
/// </summary>
/// <remarks>
/// <para>
/// This script does not configure the player. Company name, application identifier, orientation,
/// scripting backend and API levels are <em>project state</em> — they belong in
/// <c>ProjectSettings.asset</c> where a reviewer can see them in a diff, not in code that quietly
/// rewrites them every time someone builds. The single exception is target architectures, which
/// are a property of the build rather than of the project: the build pins them and puts them back,
/// so what ships is decided here rather than by whatever was last ticked in the Inspector.
/// </para>
/// <para>
/// M0-19 specified ARM64 + x86-64 for development builds, so they would run natively on BlueStacks.
/// <strong>Unity 6.3 cannot build x86-64 for Android.</strong> <c>AndroidArchitecture.X86_64</c> is
/// Magic Leap's, and it is deprecated: assigning it succeeds and reads back correctly, and the
/// build then fails with <c>UnityException: x86-64 (Magic Leap) support is now limited</c> and
/// unsets it. Both build kinds are therefore ARM64, and the emulator has to translate.
/// </para>
/// <para>
/// The scene list is read from Build Settings rather than hard-coded. A build that disagreed with
/// the Editor about which scenes exist would be a build that cannot reproduce what was playtested.
/// </para>
/// </remarks>
public static class AndroidBuild
{
    /// <summary>Where <see cref="BuildDevelopmentApk"/> writes, relative to the project root.</summary>
    private const string DevelopmentApkPath = "Builds/Android/Soulvail-dev.apk";

    /// <summary>
    /// Builds a development APK to <c>Builds/Android/Soulvail-dev.apk</c>, overwriting whatever
    /// was there.
    /// </summary>
    [MenuItem("Soulvail/Build/Android APK (Development)")]
    public static void BuildDevelopmentApk()
    {
        Build(development: true, DevelopmentApkPath);
    }

    /// <summary>
    /// Builds an Android player from the enabled scenes in Build Settings.
    /// </summary>
    /// <param name="development">
    /// <c>true</c> for a development build — the profiler, script debugging, and the debug overlay
    /// that <c>DebugOverlay</c> only shows itself in. <c>false</c> for a plain build.
    /// </param>
    /// <param name="outputPath">Where to write the APK. Missing directories are created.</param>
    /// <returns>The report Unity produced, once the build has succeeded.</returns>
    /// <exception cref="ArgumentException"><paramref name="outputPath"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// No scene in Build Settings is enabled, or the build did not succeed.
    /// </exception>
    public static BuildReport Build(bool development, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("An output path is required.", nameof(outputPath));
        }

        string[] scenes = EnabledScenePaths();

        if (scenes.Length == 0)
        {
            throw new InvalidOperationException(
                "No scene in Build Settings is enabled, so there is nothing to build.");
        }

        string directory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = development
                ? BuildOptions.Development | BuildOptions.AllowDebugging
                : BuildOptions.None,
        };

        // Pinned for the duration of the build and put back afterwards: what an APK contains is
        // then a property of the build, not of whatever someone last ticked in the Inspector, and
        // no build leaves an architecture behind for the next one to inherit silently. ARM64 for
        // both kinds — see the remarks on why x86-64 is not an option here.
        AndroidArchitecture previousArchitectures = PlayerSettings.Android.targetArchitectures;

        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        BuildReport report;

        try
        {
            report = BuildPipeline.BuildPlayer(options);
        }
        finally
        {
            PlayerSettings.Android.targetArchitectures = previousArchitectures;
        }

        BuildSummary summary = report.summary;

        string description =
            $"{(development ? "Development" : "Release")} APK · {summary.result} · " +
            $"{summary.totalSize / (1024f * 1024f):F1} MB · " +
            $"{summary.totalTime.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)} s · " +
            $"{summary.platform} {PlayerSettings.Android.targetArchitectures} · {outputPath}";

        if (summary.result != BuildResult.Succeeded)
        {
            // Thrown rather than logged: a failed build leaves the previous APK sitting at the
            // output path, and an error in a Console full of build spam is exactly how someone
            // installs last week's build and debugs the wrong one.
            throw new InvalidOperationException(
                $"{description} — {summary.totalErrors} error(s). See the Console for the cause.");
        }

        Debug.Log($"[AndroidBuild] {description}");

        return report;
    }

    /// <summary>
    /// The paths of the scenes ticked in Build Settings, in build order.
    /// </summary>
    private static string[] EnabledScenePaths()
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        var paths = new List<string>(scenes.Length);

        foreach (EditorBuildSettingsScene scene in scenes)
        {
            if (scene.enabled)
            {
                paths.Add(scene.path);
            }
        }

        return paths.ToArray();
    }
}
