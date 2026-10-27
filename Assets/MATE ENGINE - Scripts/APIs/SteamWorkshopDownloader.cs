using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Debug = UnityEngine.Debug;

namespace MateEngine.Workshop
{
    public class SteamWorkshopDownloader : MonoBehaviour
    {
        public static SteamWorkshopDownloader Instance { get; private set; }

        private const string AppId = "3625270";
        private readonly HashSet<string> activeDownloads = new HashSet<string>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);
        }

        public string FindSteamCmdPath()
        {
            string[] commonPaths = new[]
            {
                "/opt/homebrew/bin/steamcmd",
                "/usr/local/bin/steamcmd",
                "/usr/bin/steamcmd",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/Steam/steamcmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/bin/steamcmd")
            };

            foreach (var p in commonPaths)
            {
                if (File.Exists(p)) return p;
            }

            // Fallback: check PATH via `which steamcmd`
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/which",
                    Arguments = "steamcmd",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit();
                    if (!string.IsNullOrEmpty(output) && File.Exists(output))
                        return output;
                }
            }
            catch { }

            return null;
        }

        public bool IsItemInstalled(string workshopId, out string installedPath)
        {
            installedPath = "";
            if (string.IsNullOrEmpty(workshopId)) return false;

            ulong wid = 0;
            ulong.TryParse(workshopId, out wid);

            // 1. Check avatars.json
            try
            {
                string avatarsJsonPath = Path.Combine(Application.persistentDataPath, "avatars.json");
                if (File.Exists(avatarsJsonPath))
                {
                    string json = File.ReadAllText(avatarsJsonPath);
                    var entries = JsonConvert.DeserializeObject<List<AvatarLibraryMenu.AvatarEntry>>(json);
                    if (entries != null)
                    {
                        var match = entries.FirstOrDefault(e => e.steamFileId == wid || (e.filePath != null && e.filePath.Contains(workshopId)));
                        if (match != null && !string.IsNullOrEmpty(match.filePath) && File.Exists(match.filePath))
                        {
                            installedPath = match.filePath;
                            return true;
                        }
                    }
                }
            }
            catch { }

            // 2. Check Mods folder & mods_workshop_map.json
            try
            {
                string mapPath = Path.Combine(Application.persistentDataPath, "mods_workshop_map.json");
                if (File.Exists(mapPath))
                {
                    var map = JsonConvert.DeserializeObject<Dictionary<string, ulong>>(File.ReadAllText(mapPath));
                    if (map != null)
                    {
                        foreach (var kv in map)
                        {
                            if (kv.Value == wid)
                            {
                                string target = Path.Combine(Application.persistentDataPath, "Mods", kv.Key);
                                if (File.Exists(target))
                                {
                                    installedPath = target;
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // 3. Check Steam Workshop downloaded content directory
            try
            {
                string steamContentDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library/Application Support/Steam/steamapps/workshop/content",
                    AppId,
                    workshopId
                );
                if (Directory.Exists(steamContentDir))
                {
                    var files = Directory.GetFiles(steamContentDir);
                    var modelFile = files.FirstOrDefault(f => f.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".me", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(modelFile))
                    {
                        installedPath = modelFile;
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        public bool IsDownloading(string workshopId)
        {
            return activeDownloads.Contains(workshopId);
        }

        public void DownloadAndInstall(
            WorkshopItem item,
            Action<float, string> onProgress,
            Action<bool, string, string> onCompleted)
        {
            if (item == null || string.IsNullOrEmpty(item.id))
            {
                onCompleted?.Invoke(false, "Invalid workshop item specified.", "");
                return;
            }

            if (activeDownloads.Contains(item.id))
            {
                onCompleted?.Invoke(false, "Download already in progress for this item.", "");
                return;
            }

            string steamCmdPath = FindSteamCmdPath();
            if (string.IsNullOrEmpty(steamCmdPath))
            {
                onCompleted?.Invoke(false, "SteamCMD is not found. Please install steamcmd (e.g. 'brew install steamcmd').", "");
                return;
            }

            activeDownloads.Add(item.id);
            item.isDownloading = true;
            item.downloadProgress = 0.05f;

            Task.Run(() =>
            {
                bool success = false;
                string message = "";
                string installedPath = "";

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = steamCmdPath,
                        Arguments = $"+login anonymous +workshop_download_item {AppId} {item.id} +quit",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (var proc = new Process { StartInfo = psi })
                    {
                        proc.OutputDataReceived += (s, e) =>
                        {
                            if (string.IsNullOrEmpty(e.Data)) return;
                            string line = e.Data;
                            
                            // Parse download percentage if steamcmd prints progress
                            if (line.Contains("Downloading item") && line.Contains("%"))
                            {
                                int pctIdx = line.IndexOf('%');
                                int startIdx = line.LastIndexOf('(', pctIdx);
                                if (startIdx >= 0 && pctIdx > startIdx)
                                {
                                    string pctStr = line.Substring(startIdx + 1, pctIdx - startIdx - 1).Trim();
                                    if (float.TryParse(pctStr, out float pct))
                                    {
                                        float normalized = Mathf.Clamp01(pct / 100f);
                                        item.downloadProgress = normalized;
                                        UnityMainThreadDispatcher.Enqueue(() => onProgress?.Invoke(normalized, $"Downloading {pct:F0}%"));
                                    }
                                }
                            }
                        };

                        proc.Start();
                        proc.BeginOutputReadLine();
                        proc.BeginErrorReadLine();
                        proc.WaitForExit();

                        // Look for downloaded directory
                        string steamContentDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            "Library/Application Support/Steam/steamapps/workshop/content",
                            AppId,
                            item.id
                        );

                        if (Directory.Exists(steamContentDir))
                        {
                            installedPath = InstallDownloadedFiles(steamContentDir, item);
                            if (!string.IsNullOrEmpty(installedPath))
                            {
                                success = true;
                                message = $"Successfully installed {item.title}!";
                            }
                            else
                            {
                                success = false;
                                message = "Downloaded folder contained no compatible .vrm, .me, or .unity3d files.";
                            }
                        }
                        else
                        {
                            success = false;
                            message = $"SteamCMD exited (code {proc.ExitCode}) but output content folder was not found.";
                        }
                    }
                }
                catch (Exception ex)
                {
                    success = false;
                    message = "Download error: " + ex.Message;
                }
                finally
                {
                    activeDownloads.Remove(item.id);
                    item.isDownloading = false;
                    item.isInstalled = success;
                    item.installedFilePath = installedPath;

                    UnityMainThreadDispatcher.Enqueue(() =>
                    {
                        onCompleted?.Invoke(success, message, installedPath);
                    });
                }
            });
        }

        private string InstallDownloadedFiles(string contentDir, WorkshopItem item)
        {
            string persistentPath = Application.persistentDataPath;
            string workshopDir = Path.Combine(persistentPath, "Steam Workshop");
            string modsDir = Path.Combine(persistentPath, "Mods");
            string thumbsDir = Path.Combine(persistentPath, "Thumbnails");

            Directory.CreateDirectory(workshopDir);
            Directory.CreateDirectory(modsDir);
            Directory.CreateDirectory(thumbsDir);

            string[] allFiles = Directory.GetFiles(contentDir, "*", SearchOption.AllDirectories);
            string vrmFile = allFiles.FirstOrDefault(f => f.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase));
            string meFile = allFiles.FirstOrDefault(f => f.EndsWith(".me", StringComparison.OrdinalIgnoreCase));
            string unity3dFile = allFiles.FirstOrDefault(f => f.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase));
            string thumbFile = allFiles.FirstOrDefault(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase));

            // Check metadata.json
            string metaPath = Path.Combine(contentDir, "metadata.json");
            Dictionary<string, object> meta = new Dictionary<string, object>();
            if (File.Exists(metaPath))
            {
                try { meta = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(metaPath)) ?? meta; }
                catch { }
            }

            string displayName = item.title;
            string author = item.author;
            string version = "1.0";
            int polygonCount = 0;
            bool isNSFW = false;

            if (meta.TryGetValue("displayName", out var d) && d != null) displayName = d.ToString();
            if (meta.TryGetValue("author", out var a) && a != null) author = a.ToString();
            if (meta.TryGetValue("version", out var v) && v != null) version = v.ToString();
            if (meta.TryGetValue("polygonCount", out var p)) int.TryParse(p?.ToString(), out polygonCount);
            if (meta.TryGetValue("isNSFW", out var n)) bool.TryParse(n?.ToString(), out isNSFW);

            // Handle Avatar (.vrm or avatar .me)
            if (!string.IsNullOrEmpty(vrmFile) || (!string.IsNullOrEmpty(meFile) && !IsDanceME(meFile)))
            {
                string srcModel = !string.IsNullOrEmpty(vrmFile) ? vrmFile : meFile;
                string itemFolder = Path.Combine(workshopDir, item.id);
                Directory.CreateDirectory(itemFolder);
                string destModel = Path.Combine(itemFolder, Path.GetFileName(srcModel));
                File.Copy(srcModel, destModel, true);

                string destThumb = "";
                if (!string.IsNullOrEmpty(thumbFile) && File.Exists(thumbFile))
                {
                    string thumbExt = Path.GetExtension(thumbFile);
                    destThumb = Path.Combine(thumbsDir, $"{item.id}_{Path.GetFileNameWithoutExtension(destModel)}_thumb{thumbExt}");
                    File.Copy(thumbFile, destThumb, true);
                }

                ulong wid = 0;
                ulong.TryParse(item.id, out wid);

                // Update avatars.json
                string avatarsJsonPath = Path.Combine(persistentPath, "avatars.json");
                List<AvatarLibraryMenu.AvatarEntry> entries = new List<AvatarLibraryMenu.AvatarEntry>();
                if (File.Exists(avatarsJsonPath))
                {
                    try { entries = JsonConvert.DeserializeObject<List<AvatarLibraryMenu.AvatarEntry>>(File.ReadAllText(avatarsJsonPath)) ?? entries; }
                    catch { }
                }

                entries.RemoveAll(e => e.steamFileId == wid);
                entries.Add(new AvatarLibraryMenu.AvatarEntry
                {
                    displayName = displayName,
                    author = author,
                    version = version,
                    fileType = destModel.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase) ? "VRM1.X" : ".ME",
                    filePath = destModel,
                    thumbnailPath = destThumb,
                    polygonCount = polygonCount,
                    isSteamWorkshop = true,
                    steamFileId = wid,
                    isNSFW = isNSFW,
                    isOwner = false
                });

                File.WriteAllText(avatarsJsonPath, JsonConvert.SerializeObject(entries, Formatting.Indented));

                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    var library = FindAnyObjectByType<AvatarLibraryMenu>();
                    if (library != null) library.ReloadAvatars();
                });

                return destModel;
            }

            // Handle Dance / Mod (.unity3d or dance .me)
            if (!string.IsNullOrEmpty(unity3dFile) || (!string.IsNullOrEmpty(meFile) && IsDanceME(meFile)))
            {
                string srcMod = !string.IsNullOrEmpty(unity3dFile) ? unity3dFile : meFile;
                string destMod = Path.Combine(modsDir, Path.GetFileName(srcMod));
                File.Copy(srcMod, destMod, true);

                ulong wid = 0;
                ulong.TryParse(item.id, out wid);

                // Save to mods_workshop_map.json
                string mapPath = Path.Combine(persistentPath, "mods_workshop_map.json");
                Dictionary<string, ulong> map = new Dictionary<string, ulong>();
                if (File.Exists(mapPath))
                {
                    try { map = JsonConvert.DeserializeObject<Dictionary<string, ulong>>(File.ReadAllText(mapPath)) ?? map; }
                    catch { }
                }
                map[Path.GetFileName(destMod)] = wid;
                File.WriteAllText(mapPath, JsonConvert.SerializeObject(map, Formatting.Indented));

                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    var modHandler = FindAnyObjectByType<MEModHandler>();
                    if (modHandler != null)
                    {
                        var mi = typeof(MEModHandler).GetMethod("LoadAllModsInFolder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (mi != null) mi.Invoke(modHandler, null);
                    }
                    var dance = FindAnyObjectByType<CustomDancePlayer.AvatarDanceHandler>();
                    if (dance != null) dance.RescanMods();
                });

                return destMod;
            }

            return null;
        }

        private bool IsDanceME(string mePath)
        {
            try
            {
                using var fs = File.OpenRead(mePath);
                using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
                return zip.Entries.Any(e => string.Equals(e.FullName, "dance_meta.json", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Thread-safe helper to dispatch callbacks to Unity's main thread.
    /// </summary>
    public class UnityMainThreadDispatcher : MonoBehaviour
    {
        private static readonly Queue<Action> executionQueue = new Queue<Action>();
        private static UnityMainThreadDispatcher instance;

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            lock (executionQueue)
            {
                executionQueue.Enqueue(action);
            }
            EnsureInstance();
        }

        private static void EnsureInstance()
        {
            if (instance != null) return;
            var go = new GameObject("UnityMainThreadDispatcher");
            instance = go.AddComponent<UnityMainThreadDispatcher>();
            DontDestroyOnLoad(go);
        }

        private void Update()
        {
            lock (executionQueue)
            {
                while (executionQueue.Count > 0)
                {
                    executionQueue.Dequeue()?.Invoke();
                }
            }
        }
    }
}
