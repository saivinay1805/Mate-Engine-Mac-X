using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ShadowBloomManager : MonoBehaviour
{
    private MeshRenderer shadowRenderer;
    private bool searched = false;

    void Awake()
    {
        Debug.Log("[ShadowBloomManager] Initialized on " + gameObject.name);
    }

    void Update()
    {
        if (!searched || shadowRenderer == null)
        {
            var s = GameObject.Find("Shadow");
            if (s != null) shadowRenderer = s.GetComponent<MeshRenderer>();
            searched = true;
        }

        if (shadowRenderer != null)
        {
            bool bloomOn = SaveLoadHandler.Instance != null && 
                           SaveLoadHandler.Instance.data != null && 
                           SaveLoadHandler.Instance.data.bloom;
            bool shouldShow = !bloomOn;
            if (shadowRenderer.enabled != shouldShow)
            {
                shadowRenderer.enabled = shouldShow;
                Debug.Log($"[ShadowBloomManager] Bloom is {(bloomOn ? "ON" : "OFF")} -> Shadow enabled: {shouldShow}");
            }
        }
    }
}

public static class LocomotionHelper
{
    public static void EnsureLocomotionToggle(SettingsHandlerToggles sht)
    {
        try
        {
            if (sht == null) return;
            if (sht.enableMinecraftMessagesToggle == null) return;

            Transform mcToggleTr = sht.enableMinecraftMessagesToggle.transform;
            Transform mcCategory = mcToggleTr;
            while (mcCategory != null && mcCategory.parent != null && mcCategory.parent.name != "Main Menu")
            {
                mcCategory = mcCategory.parent;
            }
            if (mcCategory == null || mcCategory.parent == null) return;

            RectTransform srcRt = mcCategory.GetComponent<RectTransform>();

            Transform existing = mcCategory.parent.Find("= EXPERIMENTAL");
            GameObject expCategory;
            if (existing != null)
            {
                expCategory = existing.gameObject;
            }
            else
            {
                expCategory = UnityEngine.Object.Instantiate(mcCategory.gameObject, mcCategory.parent);
                expCategory.name = "= EXPERIMENTAL";
                expCategory.transform.SetSiblingIndex(mcCategory.GetSiblingIndex() + 1);
            }
            expCategory.SetActive(true);

            Transform lockTr = expCategory.transform.Find("LOCK_MC");
            if (lockTr != null) lockTr.gameObject.SetActive(false);

            RectTransform rt = expCategory.GetComponent<RectTransform>();
            if (rt != null && srcRt != null)
            {
                rt.anchorMin = srcRt.anchorMin;
                rt.anchorMax = srcRt.anchorMax;
                rt.pivot = srcRt.pivot;
                rt.sizeDelta = srcRt.sizeDelta;
                rt.anchoredPosition = new Vector2(srcRt.anchoredPosition.x, srcRt.anchoredPosition.y - 292f);
            }

            foreach (var mb in expCategory.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb != null && mb.GetType().Name == "LocalizeStringEvent")
                    mb.enabled = false;
            }

            Transform steamMc = expCategory.transform.Find("STEAM_MC");
            Transform infoTr = steamMc != null ? steamMc.Find("Info") : null;
            Transform titleTr = steamMc != null ? steamMc.Find("TITLE") : null;

            if (titleTr != null)
            {
                var tmp = titleTr.GetComponent<TextMeshProUGUI>();
                if (tmp != null) tmp.text = "EXPERIMENTAL FEATURES";
                RectTransform trRt = titleTr.GetComponent<RectTransform>();
                if (trRt != null) trRt.anchoredPosition = new Vector2(trRt.anchoredPosition.x, -50f);
            }

            Toggle t = expCategory.GetComponentInChildren<Toggle>(true);
            if (t != null)
            {
                t.gameObject.SetActive(true);
                t.onValueChanged.RemoveAllListeners();
                sht.enableLocomotionToggle = t;

                RectTransform tRt = t.GetComponent<RectTransform>();
                if (tRt != null)
                {
                    tRt.anchoredPosition = new Vector2(tRt.anchoredPosition.x, -105f);
                }

                foreach (var tmp in t.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    if (tmp != null) tmp.text = "AVATAR CAN WALK AROUND";
                }

                bool currentLoco = SaveLoadHandler.Instance != null && 
                                   SaveLoadHandler.Instance.data != null && 
                                   SaveLoadHandler.Instance.data.enableLocomotion;
                t.SetIsOnWithoutNotify(currentLoco);

                // Create or find sub-toggles for Normal Walk and Happi Jumpy Walk
                Transform normalTr = t.transform.parent.Find("TOGGLE_NORMAL_WALK");
                Toggle normalToggle;
                if (normalTr != null)
                {
                    normalToggle = normalTr.GetComponent<Toggle>();
                }
                else
                {
                    GameObject normalGo = UnityEngine.Object.Instantiate(t.gameObject, t.transform.parent);
                    normalGo.name = "TOGGLE_NORMAL_WALK";
                    normalToggle = normalGo.GetComponent<Toggle>();
                }

                Transform happiTr = t.transform.parent.Find("TOGGLE_HAPPI_WALK");
                Toggle happiToggle;
                if (happiTr != null)
                {
                    happiToggle = happiTr.GetComponent<Toggle>();
                }
                else
                {
                    GameObject happiGo = UnityEngine.Object.Instantiate(t.gameObject, t.transform.parent);
                    happiGo.name = "TOGGLE_HAPPI_WALK";
                    happiToggle = happiGo.GetComponent<Toggle>();
                }

                if (normalToggle != null)
                {
                    normalToggle.gameObject.SetActive(true);
                    normalToggle.onValueChanged.RemoveAllListeners();
                    foreach (var mb in normalToggle.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (mb != null && mb.GetType().Name == "LocalizeStringEvent")
                            mb.enabled = false;
                    }
                    RectTransform nRt = normalToggle.GetComponent<RectTransform>();
                    if (nRt != null)
                    {
                        nRt.anchoredPosition = new Vector2(tRt.anchoredPosition.x + 22f, -145f);
                    }
                    foreach (var tmp in normalToggle.GetComponentsInChildren<TextMeshProUGUI>(true))
                    {
                        if (tmp != null) { tmp.text = "NORMAL WALK"; tmp.fontSize = 13f; }
                    }
                    normalToggle.interactable = currentLoco;
                }

                if (happiToggle != null)
                {
                    happiToggle.gameObject.SetActive(true);
                    happiToggle.onValueChanged.RemoveAllListeners();
                    foreach (var mb in happiToggle.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (mb != null && mb.GetType().Name == "LocalizeStringEvent")
                            mb.enabled = false;
                    }
                    RectTransform hRt = happiToggle.GetComponent<RectTransform>();
                    if (hRt != null)
                    {
                        hRt.anchoredPosition = new Vector2(tRt.anchoredPosition.x + 22f, -182f);
                    }
                    foreach (var tmp in happiToggle.GetComponentsInChildren<TextMeshProUGUI>(true))
                    {
                        if (tmp != null) { tmp.text = "HAPPI JUMPY WALK"; tmp.fontSize = 13f; }
                    }
                    happiToggle.interactable = currentLoco;
                }

                string curStyle = (SaveLoadHandler.Instance != null && SaveLoadHandler.Instance.data != null && !string.IsNullOrEmpty(SaveLoadHandler.Instance.data.locomotionStyle))
                    ? SaveLoadHandler.Instance.data.locomotionStyle
                    : "happi";

                if (normalToggle != null) normalToggle.SetIsOnWithoutNotify(curStyle == "normal");
                if (happiToggle != null) happiToggle.SetIsOnWithoutNotify(curStyle != "normal");

                Action<string> updateLocoStyle = (string style) =>
                {
                    var slh = SaveLoadHandler.Instance;
                    if (slh != null && slh.data != null)
                    {
                        slh.data.locomotionStyle = style;
                        slh.SaveToDisk();
                    }
                    if (normalToggle != null) normalToggle.SetIsOnWithoutNotify(style == "normal");
                    if (happiToggle != null) happiToggle.SetIsOnWithoutNotify(style == "happi");

                    var locos = Resources.FindObjectsOfTypeAll<AvatarLocomotionController>();
                    for (int i = 0; i < locos.Length; i++)
                    {
                        if (locos[i] != null) locos[i].SetWalkStyle(style);
                    }
                };

                if (normalToggle != null)
                {
                    normalToggle.onValueChanged.AddListener((bool val) =>
                    {
                        if (val) updateLocoStyle("normal");
                        else if (SaveLoadHandler.Instance?.data?.locomotionStyle == "normal")
                            normalToggle.SetIsOnWithoutNotify(true);
                    });
                }

                if (happiToggle != null)
                {
                    happiToggle.onValueChanged.AddListener((bool val) =>
                    {
                        if (val) updateLocoStyle("happi");
                        else if (SaveLoadHandler.Instance?.data?.locomotionStyle == "happi")
                            happiToggle.SetIsOnWithoutNotify(true);
                    });
                }

                t.onValueChanged.AddListener((bool val) =>
                {
                    var slh = SaveLoadHandler.Instance;
                    if (slh != null && slh.data != null)
                    {
                        slh.data.enableLocomotion = val;
                        slh.SaveToDisk();
                    }
                    if (normalToggle != null) normalToggle.interactable = val;
                    if (happiToggle != null) happiToggle.interactable = val;

                    var locos = Resources.FindObjectsOfTypeAll<AvatarLocomotionController>();
                    for (int i = 0; i < locos.Length; i++)
                    {
                        if (locos[i] != null)
                        {
                            locos[i].EnableLocomotion = val;
                            locos[i].DrawBlockingDebug = false;
                            if (!val) locos[i].SendMessage("StopWalking", SendMessageOptions.DontRequireReceiver);
                        }
                    }
                    if (!val)
                    {
                        var animators = Resources.FindObjectsOfTypeAll<Animator>();
                        for (int a = 0; a < animators.Length; a++)
                        {
                            if (animators[a] != null)
                            {
                                try
                                {
                                    animators[a].SetBool("WalkLeft", false);
                                    animators[a].SetBool("WalkRight", false);
                                }
                                catch {}
                            }
                        }
                    }
                    var tray = UnityEngine.Object.FindAnyObjectByType<SystemTray>();
                    if (tray != null)
                    {
                        tray.SendMessage("RebuildMacMenu", SendMessageOptions.DontRequireReceiver);
                    }
                });
            }

            if (infoTr != null)
            {
                var tmp = infoTr.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.text = "CHOOSE BETWEEN CLASSIC NORMAL WALK (SPEED 2.5) OR CUTE HAPPI JUMPY WALK WITH SPINS (SPEED 3.0). THIS IS AN EXPERIMENTAL FEATURE.";
                    tmp.fontSize = 11f;
                    tmp.enableAutoSizing = false;
                    tmp.fontStyle = FontStyles.Normal;
                    tmp.color = new Color(1.0f, 0.68f, 0.72f, 1.0f);
                    tmp.lineSpacing = 0f;
                }
                RectTransform infoRt = infoTr.GetComponent<RectTransform>();
                if (infoRt != null)
                {
                    infoRt.anchoredPosition = new Vector2(infoRt.anchoredPosition.x, -228f);
                    infoRt.sizeDelta = new Vector2(410f, 55f);
                }
            }

            foreach (var btn in expCategory.GetComponentsInChildren<Button>(true))
            {
                btn.gameObject.SetActive(false);
            }

            Debug.Log("[LocomotionHelper] Created EXPERIMENTAL FEATURES section successfully!");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LocomotionHelper] EnsureLocomotionToggle error: " + ex.Message);
        }
    }

    public static void ToggleWalkingMode(MonoBehaviour tray)
    {
        try
        {
            var slh = SaveLoadHandler.Instance;
            if (slh != null && slh.data != null)
            {
                bool next = !slh.data.enableLocomotion;
                slh.data.enableLocomotion = next;
                slh.SaveToDisk();
                Debug.Log("[LocomotionHelper] Toggled enableLocomotion to: " + next);

                var locos = Resources.FindObjectsOfTypeAll<AvatarLocomotionController>();
                for (int i = 0; i < locos.Length; i++)
                {
                    if (locos[i] != null)
                    {
                        locos[i].EnableLocomotion = next;
                        locos[i].DrawBlockingDebug = false;
                        if (!next)
                        {
                            locos[i].SendMessage("StopWalking", SendMessageOptions.DontRequireReceiver);
                        }
                    }
                }

                if (!next)
                {
                    var animators = Resources.FindObjectsOfTypeAll<Animator>();
                    for (int a = 0; a < animators.Length; a++)
                    {
                        if (animators[a] != null)
                        {
                            try
                            {
                                animators[a].SetBool("WalkLeft", false);
                                animators[a].SetBool("WalkRight", false);
                            }
                            catch {}
                        }
                    }
                }

                var sht = UnityEngine.Object.FindAnyObjectByType<SettingsHandlerToggles>();
                if (sht != null && sht.enableLocomotionToggle != null)
                {
                    sht.enableLocomotionToggle.SetIsOnWithoutNotify(next);
                }
            }
            if (tray != null)
            {
                tray.SendMessage("RebuildMacMenu", SendMessageOptions.DontRequireReceiver);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LocomotionHelper] ToggleWalkingMode error: " + ex.Message);
        }
    }

    public static void AttachLocomotion()
    {
        try
        {
            var awh = UnityEngine.Object.FindAnyObjectByType<AvatarWindowHandler>();
            if (awh != null)
            {
                var go = awh.gameObject;
                var alc = go.GetComponent<AvatarLocomotionController>();
                if (alc == null)
                {
                    alc = go.AddComponent<AvatarLocomotionController>();
                    bool enabled = SaveLoadHandler.Instance != null && 
                                   SaveLoadHandler.Instance.data != null && 
                                   SaveLoadHandler.Instance.data.enableLocomotion;
                    alc.EnableLocomotion = enabled;
                    alc.DrawBlockingDebug = false;
                    Debug.Log("[LocomotionHelper] Attached AvatarLocomotionController with EnableLocomotion=" + enabled);
                }
                else
                {
                    alc.DrawBlockingDebug = false;
                }
            }

            if (SettingsScrollWatcher.Instance == null)
            {
                var watcherGo = new GameObject("MateSettingsScrollWatcher");
                watcherGo.AddComponent<SettingsScrollWatcher>();
                UnityEngine.Object.DontDestroyOnLoad(watcherGo);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LocomotionHelper] AttachLocomotion error: " + ex.Message);
        }
    }
}

public class SettingsScrollWatcher : MonoBehaviour
{
    public static SettingsScrollWatcher Instance;
    private ScrollRect sr;
    private GameObject expCategory;
    private bool loggedFound = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void FindMainScrollRect()
    {
        var allSrs = Resources.FindObjectsOfTypeAll<ScrollRect>();
        foreach (var s in allSrs)
        {
            if (s != null && s.content != null && s.gameObject.name == "Main Menu" && s.content.name == "Content")
            {
                sr = s;
                if (!loggedFound)
                {
                    loggedFound = true;
                    Debug.Log("[SettingsScrollWatcher] Found Main Menu ScrollRect: " + s.name);
                }
                break;
            }
        }
    }

    void Update()
    {
        if (UnityEngine.Object.FindAnyObjectByType<AvatarLocomotionController>() == null)
        {
            LocomotionHelper.AttachLocomotion();
        }
        MaintainSettings();
    }

    void LateUpdate()
    {
        MaintainSettings();
    }

    private void MaintainSettings()
    {
        try
        {
            if (sr == null || sr.content == null) FindMainScrollRect();
            if (sr != null && sr.content != null)
            {
                var cRt = sr.content;
                if (cRt.sizeDelta.y < 5850f)
                {
                    cRt.sizeDelta = new Vector2(cRt.sizeDelta.x, 5850f);
                }

                if (expCategory == null)
                {
                    var all = cRt.GetComponentsInChildren<Transform>(true);
                    foreach (var t in all)
                    {
                        if (t.name == "= EXPERIMENTAL")
                        {
                            expCategory = t.gameObject;
                            break;
                        }
                    }
                    if (expCategory == null)
                    {
                        var shts = Resources.FindObjectsOfTypeAll<SettingsHandlerToggles>();
                        if (shts != null && shts.Length > 0 && shts[0] != null)
                        {
                            LocomotionHelper.EnsureLocomotionToggle(shts[0]);
                        }
                    }
                }

                if (expCategory != null && !expCategory.activeSelf)
                {
                    expCategory.SetActive(true);
                }
            }
        }
        catch {}
    }
}

public static class BuiltInDanceHelper
{
    private static readonly (string clipKey, string displayName)[] DanceNames = new (string, string)[]
    {
        ("PET_DANCING", "Dance 01 - Standard"),
        ("PET_DANCING_2", "Dance 02 - Cheer"),
        ("PET_DANCING_3", "Dance 03 - Wave"),
        ("PET_DANCING_4", "Dance 04 - Groove"),
        ("PET_DANCING_5", "Dance 05 - Twist"),
        ("PET_DANCING_6", "Dance 06 - Jump Step"),
        ("PET_DANCING_7", "Dance 07 - Swing"),
        ("PET_DANCING_8", "Dance 08 - Pop"),
        ("PET_DANCING_9", "Dance 09 - Shuffle"),
        ("PET_DANCING_10", "Dance 10 - Slide"),
        ("PET_DANCING_11", "Dance 11 - Rhythm"),
        ("PET_DANCING_12", "Dance 12 - Bounce"),
        ("PET_DANCING_13", "Dance 13 - Disco"),
        ("PET_DANCING_14", "Dance 14 - Hip-Hop"),
        ("PET_DANCING_15", "Dance 15 - Samba"),
        ("PET_DANCING_16", "Dance 16 - Salsa"),
        ("PET_DANCING_17", "Dance 17 - Belly Dance"),
        ("HUS_DANCE_01", "Husbando Dance 01"),
        ("HUS_DANCE_02", "Husbando Dance 02"),
        ("HUS_DANCE_03", "Husbando Dance 03"),
        ("HUS_DANCE_04", "Husbando Dance 04"),
        ("PET_IDLE_16", "Idle - Silly Talk"),
        ("PET_IDLE_17", "Idle - Look Around"),
        ("PET_IDLE_18", "Idle - Look Around 2"),
        ("PET_IDLE_19", "Idle - Stop It"),
        ("PET_IDLE_20", "Idle - Confusing"),
        ("PET_IDLE_21", "Idle - Look Around 3")
    };

    private static FieldInfo f_entries;
    private static FieldInfo f_byId;
    private static FieldInfo f_animator;
    private static FieldInfo f_defaultController;
    private static FieldInfo f_currentIndex;
    private static FieldInfo f_contentObject;
    private static FieldInfo f_prefab;
    private static MethodInfo m_loadAllSources;
    private static MethodInfo m_buildListUI;

    private static Type t_danceEntry;
    private static FieldInfo de_id;
    private static FieldInfo de_path;
    private static FieldInfo de_clip;
    private static FieldInfo de_author;
    private static FieldInfo de_stableId;

    private static bool initialized = false;

    private static void InitReflection(Type handlerType)
    {
        if (initialized) return;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        
        f_entries = handlerType.GetField("entries", flags);
        f_byId = handlerType.GetField("byId", flags);
        f_animator = handlerType.GetField("animator", flags);
        f_defaultController = handlerType.GetField("defaultController", flags);
        f_currentIndex = handlerType.GetField("currentIndex", flags);
        f_contentObject = handlerType.GetField("contentObject", flags);
        f_prefab = handlerType.GetField("prefab", flags);
        m_loadAllSources = handlerType.GetMethod("LoadAllSources", flags);
        m_buildListUI = handlerType.GetMethod("BuildListUI", flags);

        t_danceEntry = handlerType.GetNestedType("DanceEntry", BindingFlags.NonPublic | BindingFlags.Public);
        if (t_danceEntry != null)
        {
            de_id = t_danceEntry.GetField("id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            de_path = t_danceEntry.GetField("path", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            de_clip = t_danceEntry.GetField("clip", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            de_author = t_danceEntry.GetField("author", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            de_stableId = t_danceEntry.GetField("stableId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        initialized = true;
    }

    public static void ToggleWalkingMode(object tray)
    {
        LocomotionHelper.ToggleWalkingMode(tray as MonoBehaviour);
    }

    public static void AddWalkingMenuItem(SystemTray tray, List<(string, Action)> context)
    {
        try
        {
            bool walkingOn = SaveLoadHandler.Instance != null && 
                             SaveLoadHandler.Instance.data != null && 
                             SaveLoadHandler.Instance.data.enableLocomotion;
            string label = (walkingOn ? "✔ " : "✖ ") + "Walking Mode (Roaming)";
            context.Add((label, () => LocomotionHelper.ToggleWalkingMode(tray)));
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BuiltInDanceHelper] AddWalkingMenuItem error: " + ex.Message);
        }
    }

    public static void InitOnStartup()
    {
        try
        {
            LocomotionHelper.AttachLocomotion();
            if (UnityEngine.Object.FindAnyObjectByType<ShadowBloomManager>() == null)
            {
                var sbmGo = new GameObject("MateShadowBloomManager");
                sbmGo.AddComponent<ShadowBloomManager>();
                UnityEngine.Object.DontDestroyOnLoad(sbmGo);
            }
            Debug.Log("[BuiltInDanceHelper] InitOnStartup executed successfully!");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[BuiltInDanceHelper] InitOnStartup error: " + ex.Message);
        }
    }

    public static void AddBuiltInDances(object handler)
    {
        if (handler == null) return;
        try
        {
            InitReflection(handler.GetType());
            if (f_entries == null || f_byId == null || t_danceEntry == null) return;

            var entries = (IList)f_entries.GetValue(handler);
            var byId = (IDictionary)f_byId.GetValue(handler);
            if (entries == null || byId == null) return;

            var animator = (Animator)f_animator?.GetValue(handler);
            var defaultCtrl = (RuntimeAnimatorController)f_defaultController?.GetValue(handler);

            RuntimeAnimatorController ctrl = null;
            if (animator != null && animator.runtimeAnimatorController != null)
                ctrl = animator.runtimeAnimatorController;
            else if (defaultCtrl != null)
                ctrl = defaultCtrl;

            if (ctrl == null)
            {
                var anims = UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsInactive.Include);
                for (int i = 0; i < anims.Length; i++)
                {
                    if (anims[i] != null && anims[i].runtimeAnimatorController != null)
                    {
                        ctrl = anims[i].runtimeAnimatorController;
                        break;
                    }
                }
            }

            if (ctrl == null) return;

            var clips = ctrl.animationClips;
            if (clips == null || clips.Length == 0) return;

            var clipMap = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                if (c != null && !clipMap.ContainsKey(c.name))
                    clipMap[c.name] = c;
            }

            int added = 0;
            for (int i = 0; i < DanceNames.Length; i++)
            {
                var pair = DanceNames[i];
                if (clipMap.TryGetValue(pair.clipKey, out var clip) && clip != null)
                {
                    if (byId.Contains(pair.displayName)) continue;

                    var entry = Activator.CreateInstance(t_danceEntry);
                    de_id?.SetValue(entry, pair.displayName);
                    de_path?.SetValue(entry, "builtin:" + pair.clipKey);
                    de_clip?.SetValue(entry, clip);
                    de_author?.SetValue(entry, "Built-in Animation");
                    de_stableId?.SetValue(entry, "builtin:" + pair.clipKey);

                    entries.Add(entry);
                    byId[pair.displayName] = entry;
                    added++;
                }
            }
            if (added > 0)
                Debug.Log($"[BuiltInDanceHelper] Added {added} built-in animations. Total entries: {entries.Count}");
        }
        catch (Exception ex)
        {
            Debug.LogError("[BuiltInDanceHelper] Error in AddBuiltInDances: " + ex);
        }
    }

    public static void EnsurePopulated(object handler)
    {
        if (handler == null) return;
        try
        {
            var mb = handler as MonoBehaviour;
            if (mb != null && mb.gameObject.GetComponent<ShadowBloomManager>() == null)
            {
                mb.gameObject.AddComponent<ShadowBloomManager>();
            }

            LocomotionHelper.AttachLocomotion();

            InitReflection(handler.GetType());
            if (f_entries == null || f_animator == null) return;

            var entries = (IList)f_entries.GetValue(handler);
            var animator = f_animator.GetValue(handler);

            if (entries != null && entries.Count == 0 && animator != null)
            {
                string goName = mb != null ? mb.gameObject.name : "unknown";

                Debug.Log($"[BuiltInDanceHelper] Populating Dance Player on '{goName}'...");
                m_loadAllSources?.Invoke(handler, null);
                m_buildListUI?.Invoke(handler, null);
                if (entries.Count > 0 && f_currentIndex != null)
                {
                    int idx = (int)f_currentIndex.GetValue(handler);
                    if (idx < 0) f_currentIndex.SetValue(handler, 0);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[BuiltInDanceHelper] Error in EnsurePopulated: " + ex);
        }
    }
}
