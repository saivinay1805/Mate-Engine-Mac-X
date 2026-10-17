using System;
using System.IO;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MateEngine.Workshop
{
    public class SteamWorkshopBrowser : MonoBehaviour
    {
        public static SteamWorkshopBrowser Instance { get; private set; }

        private TMP_FontAsset defaultFont;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (Instance == null)
            {
                var go = new GameObject("SteamWorkshopBrowser");
                go.AddComponent<SteamWorkshopBrowser>();
            }
        }

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

        private void Start()
        {
            var anyTmp = FindAnyObjectByType<TMP_Text>();
            if (anyTmp != null) defaultFont = anyTmp.font;

            // Ensure symlink /tmp/mateworkshop exists for easy discovery
            EnsureSymlink();

            InjectWorkshopButtonsToExistingMenus();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F3))
            {
                OpenBrowser();
            }
        }

        public void ToggleWorkshopBrowser()
        {
            OpenBrowser();
        }

        public void OpenBrowser()
        {
            OpenNativeWorkshop(null);
        }

        public void OpenItem(string workshopIdOrUrl)
        {
            OpenNativeWorkshop(workshopIdOrUrl);
        }

        public static void OpenNativeWorkshop(string arg = null)
        {
            string helperPath = FindHelperPath();
            if (string.IsNullOrEmpty(helperPath) || !File.Exists(helperPath))
            {
                UnityEngine.Debug.LogError("[SteamWorkshopBrowser] Native helper 'mateworkshop' not found!");
                return;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = helperPath,
                    Arguments = !string.IsNullOrEmpty(arg) ? $"\"{arg}\"" : "",
                    UseShellExecute = false
                };
                Process.Start(psi);
                UnityEngine.Debug.Log($"[SteamWorkshopBrowser] Launched native macOS workshop browser: {helperPath}");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[SteamWorkshopBrowser] Failed to launch native workshop browser: {ex.Message}");
            }
        }

        private static string FindHelperPath()
        {
            string macosDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "MacOS"));
            string p1 = Path.Combine(macosDir, "mateworkshop");
            if (File.Exists(p1)) return p1;

            string p2 = Path.Combine(Application.dataPath, "Plugins/MacOS/mateworkshop");
            if (File.Exists(p2)) return p2;

            if (File.Exists("/Applications/MateEngineX.app/Contents/MacOS/mateworkshop"))
                return "/Applications/MateEngineX.app/Contents/MacOS/mateworkshop";

            if (File.Exists("/tmp/mateworkshop"))
                return "/tmp/mateworkshop";

            return null;
        }

        private void EnsureSymlink()
        {
            try
            {
                string helperPath = FindHelperPath();
                if (!string.IsNullOrEmpty(helperPath) && File.Exists(helperPath))
                {
                    if (File.Exists("/tmp/mateworkshop") || Directory.Exists("/tmp/mateworkshop"))
                    {
                        File.Delete("/tmp/mateworkshop");
                    }
                    Process.Start("ln", $"-sf \"{helperPath}\" /tmp/mateworkshop");
                }
            }
            catch { }
        }

        public void InjectWorkshopButtonsToExistingMenus()
        {
            // 1. Inject into VRMMenu (Avatar Library)
            Transform vrmMenuTr = null;
            var libMenu = FindAnyObjectByType<AvatarLibraryMenu>(FindObjectsInactive.Include);
            if (libMenu != null)
            {
                vrmMenuTr = libMenu.transform;
            }
            else
            {
                var allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
                foreach (var t in allTransforms)
                {
                    if (t != null && t.name == "VRMMenu")
                    {
                        vrmMenuTr = t;
                        break;
                    }
                }
            }

            if (vrmMenuTr != null)
            {
                var existingBtn = vrmMenuTr.Find("BrowseWorkshopBtn");
                if (existingBtn == null)
                {
                    var btnGo = CreateUIObject("BrowseWorkshopBtn", vrmMenuTr,
                        new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                        new Vector2(0, 36), new Vector2(280, 34));
                    var btnImg = btnGo.AddComponent<Image>();
                    btnImg.color = new Color(0.20f, 0.55f, 0.95f, 1f);
                    btnImg.raycastTarget = true;
                    var btn = btnGo.AddComponent<Button>();
                    btn.targetGraphic = btnImg;
                    btn.onClick.AddListener(OpenBrowser);

                    var txtGo = CreateUIObject("Text", btnGo.transform,
                        Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                    var txt = txtGo.AddComponent<TextMeshProUGUI>();
                    if (defaultFont != null) txt.font = defaultFont;
                    txt.text = "<b>Browse Steam Workshop</b>";
                    txt.fontSize = 13;
                    txt.color = Color.white;
                    txt.alignment = TextAlignmentOptions.Center;
                    txt.raycastTarget = false;
                }
            }

            // 2. Inject into ModList
            Transform modListTr = null;
            var allT = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (var t in allT)
            {
                if (t != null && t.name == "ModList")
                {
                    modListTr = t;
                    break;
                }
            }

            if (modListTr != null)
            {
                var existingBtn = modListTr.Find("BrowseWorkshopBtn");
                if (existingBtn == null)
                {
                    var btnGo = CreateUIObject("BrowseWorkshopBtn", modListTr,
                        new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                        new Vector2(0, 36), new Vector2(280, 34));
                    var btnImg = btnGo.AddComponent<Image>();
                    btnImg.color = new Color(0.20f, 0.55f, 0.95f, 1f);
                    btnImg.raycastTarget = true;
                    var btn = btnGo.AddComponent<Button>();
                    btn.targetGraphic = btnImg;
                    btn.onClick.AddListener(OpenBrowser);

                    var txtGo = CreateUIObject("Text", btnGo.transform,
                        Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                    var txt = txtGo.AddComponent<TextMeshProUGUI>();
                    if (defaultFont != null) txt.font = defaultFont;
                    txt.text = "<b>Browse Steam Workshop</b>";
                    txt.fontSize = 13;
                    txt.color = Color.white;
                    txt.alignment = TextAlignmentOptions.Center;
                    txt.raycastTarget = false;
                }
            }
        }

        private GameObject CreateUIObject(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;
            return go;
        }
    }
}
