using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System;

[Serializable]
public class InstanceEntry
{
    public Button button;
    public TMP_Text text;
}

public class LaunchMateEngineInstances : MonoBehaviour
{
    [Header("Executable")]
    public string executableName = "MateEngineX";

    [Header("Texts")]
    public string notRunningText = "Open Avatar {0}";
    public string runningText = "Avatar {0} Running";

    [Header("Instances")]
    public List<InstanceEntry> instances = new List<InstanceEntry>(5);

    [Header("Status Polling")]
    public float statusPollInterval = 1.5f;

    [Header("Optional")]
    public GameObject hideIfSecondary;
    public List<GameObject> hideIfSecondaryItems = new List<GameObject>();

    private readonly Dictionary<int, Process> activeInstances = new Dictionary<int, Process>();
    private string persistentPath;
    private int currentInstanceIndex = 0;
    private string pidPath = null;
    private GameObject instancesPanelCache = null;

    public static LaunchMateEngineInstances Instance { get; private set; }
    public int CurrentInstanceIndex => currentInstanceIndex;

    void Awake()
    {
        Instance = this;
        persistentPath = Application.persistentDataPath;
        DetectCurrentInstance();

        if (currentInstanceIndex > 0)
        {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            MacSystemBridge.MacSys_SetDockIconVisible(0);
#endif
            ApplySecondaryHide();
        }

        for (int i = 0; i < instances.Count; i++)
        {
            int index = i + 1;
            if (instances[i] != null && instances[i].button != null)
            {
                int captured = index;
                instances[i].button.onClick.AddListener(() => ToggleInstance(captured));
            }
            UpdateButtonText(index, IsInstanceAlive(index));
        }

        if (statusPollInterval > 0f)
            InvokeRepeating(nameof(RefreshStatusPoll), statusPollInterval, statusPollInterval);

        if (currentInstanceIndex > 0)
            WritePidFile();
    }

    void OnApplicationQuit()
    {
        if (currentInstanceIndex == 0)
        {
            for (int i = 1; i <= 5; i++)
            {
                if (IsInstanceAlive(i))
                    CloseInstance(i);
            }
        }
        CleanupPidFile();
    }

    void OnDestroy() => CleanupPidFile();

    void DetectCurrentInstance()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--instance", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(args[i + 1], out currentInstanceIndex);
                break;
            }
        }
        currentInstanceIndex = Mathf.Max(0, currentInstanceIndex);
    }

    void ApplySecondaryHide()
    {
        var targets = GetHideTargets();
        foreach (var go in targets)
            if (go != null) go.SetActive(false);

        // Hide main settings menu and tutorial roots on secondary instances so they appear cleanly
        var allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (var t in allTransforms)
        {
            if (t == null) continue;
            if (t.name == "Main Menu" || t.name == "Show Instances" || t.name == "Tutorial" || t.name == "TutorialRoot")
            {
                if (t.gameObject != null) t.gameObject.SetActive(false);
            }
        }
    }

    List<GameObject> GetHideTargets()
    {
        var list = new List<GameObject>();
        if (hideIfSecondary != null) list.Add(hideIfSecondary);
        if (hideIfSecondaryItems != null)
        {
            for (int i = 0; i < hideIfSecondaryItems.Count; i++)
            {
                var go = hideIfSecondaryItems[i];
                if (go != null && !list.Contains(go)) list.Add(go);
            }
        }
        return list;
    }

    void WritePidFile()
    {
        try
        {
            pidPath = Path.Combine(persistentPath, $"instance_{currentInstanceIndex}.pid");
            int pid = Process.GetCurrentProcess().Id;
            File.WriteAllText(pidPath, pid.ToString());
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("[Launcher] Failed to write PID file: " + e.Message);
        }
    }

    void CleanupPidFile()
    {
        if (!string.IsNullOrEmpty(pidPath))
        {
            try { if (File.Exists(pidPath)) File.Delete(pidPath); } catch { }
        }
    }

    public void ToggleInstance(int index)
    {
        if (IsInstanceAlive(index))
            CloseInstance(index);
        else
            LaunchInstance(index);
    }

    public void LaunchInstance(int index)
    {
        if (IsInstanceAlive(index))
        {
            UnityEngine.Debug.Log($"[Launcher] Instance {index} already running.");
            UpdateButtonText(index, true);
            return;
        }

        string saveFile = $"settings_instance{index}.json";
        string dataDir = $"Instance_{index}";
        string args = $"--instance {index} --savefile \"{saveFile}\" --datadir \"{dataDir}\"";

#if UNITY_STANDALONE_OSX
        string bundle = GetAppBundlePath();
        if (Directory.Exists(bundle))
        {
            try
            {
                var openInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/open",
                    Arguments = $"-n \"{bundle}\" --args {args}",
                    UseShellExecute = false
                };
                Process.Start(openInfo);
                UpdateButtonText(index, true);
                UnityEngine.Debug.Log($"[Launcher] Started Instance {index} via open -n bundle: {bundle}");
                return;
            }
            catch (Exception exOpen)
            {
                UnityEngine.Debug.LogError("[Launcher] open -n bundle fallback failed: " + exOpen.Message);
            }
        }
#endif

        string exePath = GetExecutablePath();
        if (!File.Exists(exePath))
        {
            UnityEngine.Debug.LogError("[Launcher] Executable not found: " + exePath);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath)
            };

            var p = Process.Start(startInfo);
            if (p != null)
            {
                activeInstances[index] = p;
                p.EnableRaisingEvents = true;
                p.Exited += (s, e) =>
                {
                    UnityMainThreadDispatcher.Enqueue(() => UpdateButtonText(index, false));
                    activeInstances.Remove(index);
                };
            }

            UpdateButtonText(index, true);
            UnityEngine.Debug.Log($"[Launcher] Started Instance {index} with args: {args}");
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError("[Launcher] Failed to start instance " + index + ": " + ex.Message);
        }
    }

    public void CloseInstance(int index)
    {
        if (activeInstances.TryGetValue(index, out var p))
        {
            try
            {
                if (p != null && !p.HasExited)
                    p.Kill();
            }
            catch { }
            activeInstances.Remove(index);
        }

        string pidFile = Path.Combine(persistentPath, $"instance_{index}.pid");
        if (File.Exists(pidFile))
        {
            try
            {
                string txt = File.ReadAllText(pidFile).Trim();
                if (int.TryParse(txt, out int pid))
                {
                    try
                    {
                        var proc = Process.GetProcessById(pid);
                        if (!proc.HasExited)
                            proc.Kill();
                    }
                    catch { }
                }
            }
            catch { }
            try { File.Delete(pidFile); } catch { }
        }

        UpdateButtonText(index, false);
    }

    void RefreshStatusPoll()
    {
        for (int i = 1; i <= instances.Count; i++)
            UpdateButtonText(i, IsInstanceAlive(i));
    }

    public bool IsInstanceAlive(int index)
    {
        if (activeInstances.TryGetValue(index, out var p))
        {
            if (p != null && !p.HasExited) return true;
        }

        string pidFile = Path.Combine(persistentPath, $"instance_{index}.pid");
        if (!File.Exists(pidFile)) return false;

        try
        {
            string txt = File.ReadAllText(pidFile).Trim();
            if (int.TryParse(txt, out int pid))
            {
                try
                {
                    var proc = Process.GetProcessById(pid);
                    if (!proc.HasExited) return true;
                }
                catch { }
            }
        }
        catch { }

        try { File.Delete(pidFile); } catch { }
        return false;
    }

    public GameObject GetInstancesPanel()
    {
        if (instancesPanelCache != null) return instancesPanelCache;
        var allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (var t in allTransforms)
        {
            if (t != null && t.name == "Show Instances")
            {
                instancesPanelCache = t.gameObject;
                return instancesPanelCache;
            }
        }
        return null;
    }

    public void ToggleInstancesPanel()
    {
        var panel = GetInstancesPanel();
        if (panel != null)
        {
            panel.SetActive(!panel.activeSelf);
        }
    }

    private string GetExecutablePath()
    {
#if UNITY_STANDALONE_WIN
        string exeName = executableName;
        if (string.IsNullOrEmpty(Path.GetExtension(exeName)))
            exeName += ".exe";
        return Path.GetFullPath(Path.Combine(Application.dataPath, $"../{exeName}"));
#elif UNITY_STANDALONE_OSX
        string exeName = Path.GetFileNameWithoutExtension(executableName);
        if (string.IsNullOrEmpty(exeName)) exeName = "MateEngineX";

        string path1 = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "MacOS", exeName));
        if (File.Exists(path1)) return path1;

        string path2 = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "MacOS", exeName));
        if (File.Exists(path2)) return path2;

        string bundle = GetAppBundlePath();
        string path3 = Path.Combine(bundle, "Contents", "MacOS", exeName);
        if (File.Exists(path3)) return path3;

        return path1;
#else
        return Path.GetFullPath(Path.Combine(Application.dataPath, $"../{executableName}"));
#endif
    }

#if UNITY_STANDALONE_OSX
    private string GetAppBundlePath()
    {
        string bundle1 = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
        if (Directory.Exists(bundle1) && bundle1.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            return bundle1;

        string bundle2 = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        if (Directory.Exists(bundle2) && bundle2.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            return bundle2;

        string bundle3 = "/Applications/MateEngineX.app";
        if (Directory.Exists(bundle3)) return bundle3;

        return bundle1;
    }
#endif

    void UpdateButtonText(int index, bool running)
    {
        int idx = index - 1;
        if (idx < 0 || idx >= instances.Count) return;
        var entry = instances[idx];
        if (entry == null || entry.text == null) return;

        entry.text.text = running
            ? string.Format(runningText, index)
            : string.Format(notRunningText, index);
    }
}

public class LaunchMateEngineInstance : LaunchMateEngineInstances { }

public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static readonly Queue<Action> queue = new Queue<Action>();
    public static void Enqueue(Action a) { lock (queue) queue.Enqueue(a); }
    void Update()
    {
        lock (queue)
        {
            while (queue.Count > 0)
                queue.Dequeue()?.Invoke();
        }
    }
}
