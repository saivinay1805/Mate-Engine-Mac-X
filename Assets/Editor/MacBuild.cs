using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class MacBuild
{
    private const string MenuPath = "Mate Engine/Build macOS";
    private const string DefaultOutput = "Builds/macOS/MateEngineX.app";

    [MenuItem(MenuPath)]
    public static void BuildFromMenu()
    {
        Build(BuildOptions.None);
    }

    [MenuItem("Mate Engine/Configure Antigravity IDE")]
    public static void ConfigureAntigravity()
    {
        const string editorPath = "/Applications/Antigravity IDE.app";
        Unity.CodeEditor.CodeEditor.SetExternalScriptEditor(editorPath);
        Unity.CodeEditor.CodeEditor.Editor.SetCodeEditor(editorPath);
        UnityEditor.EditorPrefs.SetString("kScriptsDefaultApp", editorPath);
        Unity.CodeEditor.CodeEditor.Editor.CurrentCodeEditor?.SyncAll();
        Debug.Log($"[MacBuild] Antigravity IDE configured: {editorPath}; kScriptsDefaultApp={UnityEditor.EditorPrefs.GetString("kScriptsDefaultApp")}");
    }

    [MenuItem("Mate Engine/Print IDE Status")]
    public static void PrintIdeStatus()
    {
        string staticInstallation = Unity.CodeEditor.CodeEditor.CurrentEditorInstallation;
        string instancePath = Unity.CodeEditor.CodeEditor.CurrentEditorPath;
        string editorType = Unity.CodeEditor.CodeEditor.Editor?.CurrentCodeEditor?.GetType().FullName;
        var codeEditorType = typeof(Unity.CodeEditor.CodeEditor);
        string methods = string.Join(" | ", codeEditorType
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance)
            .Where(m => m.Name.Contains("Editor") || m.Name.Contains("Code"))
            .Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})"));

        Debug.Log($"[MacBuild] Static editor installation: {staticInstallation}");
        Debug.Log($"[MacBuild] CodeEditor.Editor path: {instancePath}");
        Debug.Log($"[MacBuild] CodeEditor type: {editorType}");
        Debug.Log($"[MacBuild] EditorPrefs kScriptsDefaultApp: {UnityEditor.EditorPrefs.GetString("kScriptsDefaultApp")}");
        Debug.Log($"[MacBuild] CodeEditor methods: {methods}");
    }

    public static void ConfigureAntigravityBatch()
    {
        ConfigureAntigravity();
        EditorApplication.Exit(0);
    }

    public static void BuildFromCommandLine()
    {
        string output = GetCommandLineArg("-output", DefaultOutput);
        bool development = HasCommandLineArg("-development");
        BuildOptions options = development ? BuildOptions.Development : BuildOptions.None;

        BuildReport report = Build(options, output);
        bool ok = report != null && report.summary.result == BuildResult.Succeeded;
        if (!ok)
        {
            Debug.LogError("[MacBuild] macOS build failed. See log for details.");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"[MacBuild] macOS build succeeded: {Path.GetFullPath(output)}");
        EditorApplication.Exit(0);
    }

    private static BuildReport Build(BuildOptions options, string output = null)
    {
        string fullOutput = Path.GetFullPath(output ?? DefaultOutput);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutput));

        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);
        string archArg = GetCommandLineArg("-architecture", "universal").ToLowerInvariant();
        if (archArg == "x64" || archArg == "x86_64")
        {
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.x64;
        }
        else if (archArg == "arm64")
        {
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.ARM64;
        }
        else
        {
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.x64ARM64;
        }
        Debug.Log($"[MacBuild] Target architecture set to: {UnityEditor.OSXStandalone.UserBuildSettings.architecture}");

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Debug.LogError("[MacBuild] No enabled scene in EditorBuildSettings.");
            return null;
        }

        BuildPlayerOptions playerOptions = new()
        {
            scenes = scenes,
            locationPathName = fullOutput,
            target = BuildTarget.StandaloneOSX,
            targetGroup = BuildTargetGroup.Standalone,
            options = options
        };

        return BuildPipeline.BuildPlayer(playerOptions);
    }

    private static void DisableBurstForUniversalBuild()
    {
        try
        {
            EditorPrefs.SetBool("BurstCompilation", false);
            EditorPrefs.SetBool("BurstSafetyChecks", false);

            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "UnityEditor.BurstModule");
            if (asm != null)
            {
                var settingsType = asm.GetType("Unity.Burst.Editor.BurstPlatformAotSettings");
                if (settingsType != null)
                {
                    var getOrCreate = settingsType.GetMethod("GetOrCreateSettings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    var settings = getOrCreate?.Invoke(null, new object[] { BuildTargetGroup.Standalone });
                    if (settings != null)
                    {
                        var prop = settingsType.GetProperty("EnableBurstCompilation");
                        prop?.SetValue(settings, false);
                        Debug.Log("[MacBuild] Disabled BurstPlatformAotSettings.EnableBurstCompilation.");
                    }
                }

                var editorOptionsType = asm.GetType("Unity.Burst.Editor.BurstEditorOptions");
                if (editorOptionsType != null)
                {
                    var prop = editorOptionsType.GetProperty("EnableBurstCompilation", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    prop?.SetValue(null, false);
                    Debug.Log("[MacBuild] Disabled BurstEditorOptions.EnableBurstCompilation.");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[MacBuild] Exception while disabling Burst: " + ex.Message);
        }
    }

    private static string GetCommandLineArg(string name, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return fallback;
    }

    private static bool HasCommandLineArg(string name)
    {
        return Environment.GetCommandLineArgs().Any(arg => arg == name);
    }

    [UnityEditor.Callbacks.PostProcessBuild(100)]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.StandaloneOSX) return;

        string helperSrc = Path.Combine(Application.dataPath, "Plugins/MacOS/matesfbhelper");
        string helperDst = Path.Combine(pathToBuiltProject, "Contents/MacOS/matesfbhelper");
        if (File.Exists(helperSrc))
        {
            try
            {
                File.Copy(helperSrc, helperDst, true);
                var chmod = System.Diagnostics.Process.Start("chmod", $"+x \"{helperDst}\"");
                chmod?.WaitForExit();
                Debug.Log($"[MacBuild] PostProcessBuild: Copied matesfbhelper to {helperDst}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MacBuild] PostProcessBuild failed to copy matesfbhelper: {ex.Message}");
            }
        }
    }
}
