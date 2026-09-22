using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

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
