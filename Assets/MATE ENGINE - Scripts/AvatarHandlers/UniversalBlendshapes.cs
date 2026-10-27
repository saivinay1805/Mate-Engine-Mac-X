using UnityEngine;
using VRM;
using UniVRM10;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class UniversalBlendshapes : MonoBehaviour
{
    [Header("Universal Preview")]
    [Range(0f, 1f)] public float Blink, Blink_L, Blink_R, LookUp, LookDown, LookLeft, LookRight, Neutral;
    [Range(0f, 1f)] public float A, I, U, E, O, Joy, Angry, Sorrow, Fun;
    public float fadeSpeed = 5f, safeTimeout = 2f, minHoldTime = 0.1f;

    [Header("Bone Eyelid / Facial Support")]
    public float upperEyelidClosePitch = 28f;
    public float lowerEyelidClosePitch = -6f;
    public float jawOpenPitch = 12f;

    private Transform leftUpperEyelid, rightUpperEyelid;
    private Transform leftLowerEyelid, rightLowerEyelid;
    private Transform jawBone;
    private Quaternion lUpperInitRot = Quaternion.identity, rUpperInitRot = Quaternion.identity;
    private Quaternion lLowerInitRot = Quaternion.identity, rLowerInitRot = Quaternion.identity;
    private Quaternion jawInitRot = Quaternion.identity;
    private bool boneFacialSearched = false;
    private bool hasBoneEyelids = false;
    private bool hasJawBone = false;

    [Header("Auto Blink")]
    public bool enableAutoBlink = true;
    private float nextBlinkTime;
    private float blinkStartTime = -1f;
    private const float blinkDuration = 0.16f;

    private VRMBlendShapeProxy proxy0; private Vrm10Instance vrm1; private Vrm10RuntimeExpression expr1;
    private class BlendState { public float value, lastInput, lastUpdateTime, holdUntil; }

    private readonly Dictionary<string, BlendState> states = new();
    private readonly List<KeyValuePair<BlendShapeKey, float>> reusableList = new();

    private static readonly string[] keys = new[]
    {
        "Blink", "Blink_L", "Blink_R",
        "LookUp", "LookDown", "LookLeft", "LookRight",
        "Neutral", "A", "I", "U", "E", "O",
        "Joy", "Angry", "Sorrow", "Fun"
    };

    private static readonly BlendShapePreset[] vrm0Presets = new[]
    {
        BlendShapePreset.Blink, BlendShapePreset.Blink_L, BlendShapePreset.Blink_R,
        BlendShapePreset.LookUp, BlendShapePreset.LookDown, BlendShapePreset.LookLeft, BlendShapePreset.LookRight,
        BlendShapePreset.Neutral,
        BlendShapePreset.A, BlendShapePreset.I, BlendShapePreset.U, BlendShapePreset.E, BlendShapePreset.O,
        BlendShapePreset.Joy, BlendShapePreset.Angry, BlendShapePreset.Sorrow, BlendShapePreset.Fun
    };

    private static readonly Dictionary<string, string> vrm10KeyMap = new()
    {
        { "A", "aa" }, { "I", "ih" }, { "U", "ou" }, { "E", "ee" }, { "O", "oh" },
        { "Joy", "happy" }, { "Angry", "angry" }, { "Sorrow", "sad" }, { "Fun", "relaxed" },
        { "Blink", "blink" }, { "Blink_L", "blinkLeft" }, { "Blink_R", "blinkRight" },
        { "LookUp", "lookUp" }, { "LookDown", "lookDown" }, { "LookLeft", "lookLeft" }, { "LookRight", "lookRight" },
        { "Neutral", "neutral" }
    };

    private readonly Dictionary<string, ExpressionKey> vrm1ExpressionKeyMap = new();
    private readonly float[] valueCache = new float[keys.Length];

    private void Awake()
    {
        proxy0 = GetComponent<VRMBlendShapeProxy>();
        vrm1 = GetComponentInChildren<Vrm10Instance>(true);
        expr1 = vrm1 != null ? vrm1.Runtime?.Expression : null;

        for (int i = 0; i < keys.Length; i++)
            states[keys[i]] = new BlendState();

        if (expr1 != null)
        {
            vrm1ExpressionKeyMap.Clear();
            foreach (var k in expr1.ExpressionKeys)
            {
                if (!vrm1ExpressionKeyMap.ContainsKey(k.Name))
                    vrm1ExpressionKeyMap[k.Name] = k;
            }
        }

        nextBlinkTime = Time.time + Random.Range(2.5f, 4.5f);
    }

    private void Start()
    {
        EnsureFacialBones();
    }

    private void EnsureFacialBones()
    {
        if (boneFacialSearched) return;
        boneFacialSearched = true;
        FindBoneFacialTransforms();
    }

    private void FindBoneFacialTransforms()
    {
        var transforms = GetComponentsInChildren<Transform>(true);
        foreach (var t in transforms)
        {
            string n = t.name.ToLower();
            if (leftUpperEyelid == null && (n == "l_eyelidupper" || n.Contains("eyelidupper_l") || n.Contains("eyelid_upper_l") || n.Contains("lefteyelidupper") || n.Contains("eyelid_top_l") || n.Contains("l_eyelid_top")))
            {
                leftUpperEyelid = t;
                lUpperInitRot = t.localRotation;
            }
            else if (rightUpperEyelid == null && (n == "r_eyelidupper" || n.Contains("eyelidupper_r") || n.Contains("eyelid_upper_r") || n.Contains("righteyelidupper") || n.Contains("eyelid_top_r") || n.Contains("r_eyelid_top")))
            {
                rightUpperEyelid = t;
                rUpperInitRot = t.localRotation;
            }
            else if (leftLowerEyelid == null && (n == "l_eyelidlower" || n.Contains("eyelidlower_l") || n.Contains("eyelid_lower_l") || n.Contains("lefteyelidlower") || n.Contains("eyelid_bottom_l") || n.Contains("l_eyelid_bottom")))
            {
                leftLowerEyelid = t;
                lLowerInitRot = t.localRotation;
            }
            else if (rightLowerEyelid == null && (n == "r_eyelidlower" || n.Contains("eyelidlower_r") || n.Contains("eyelid_lower_r") || n.Contains("righteyelidlower") || n.Contains("eyelid_bottom_r") || n.Contains("r_eyelid_bottom")))
            {
                rightLowerEyelid = t;
                rLowerInitRot = t.localRotation;
            }
            else if (jawBone == null && (n == "lowerjaw" || n == "jaw" || n.Contains("jaw_master") || n.Contains("jaw_bone")))
            {
                jawBone = t;
                jawInitRot = t.localRotation;
            }
        }

        hasBoneEyelids = leftUpperEyelid != null && rightUpperEyelid != null;
        hasJawBone = jawBone != null;
        if (hasBoneEyelids || hasJawBone)
        {
            Debug.Log($"[UniversalBlendshapes] Bone facial rigged: Eyelids={hasBoneEyelids} (L={leftUpperEyelid?.name}, R={rightUpperEyelid?.name}), Jaw={hasJawBone} ({jawBone?.name})");
        }
    }

    private void LateUpdate()
    {
        EnsureFacialBones();
        UpdateAutoBlink();

        if (states.Count == 0) return;
        float now = Time.time;
        float dt = Time.deltaTime;

        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i];
            float input = valueCache[i] = GetInputValue(i);
            UpdateState(key, input, now, dt);
        }

        if (proxy0 != null)
        {
            reusableList.Clear();
            for (int i = 0; i < keys.Length; i++)
            {
                reusableList.Add(new KeyValuePair<BlendShapeKey, float>(
                    BlendShapeKey.CreateFromPreset(vrm0Presets[i]), GetStateValue(keys[i])
                ));
            }
            proxy0.SetValues(reusableList);
            proxy0.Apply();
        }
        else if (expr1 != null)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                if (!vrm10KeyMap.TryGetValue(key, out var mapped)) mapped = key;
                if (vrm1ExpressionKeyMap.TryGetValue(mapped, out var exprKey))
                {
                    expr1.SetWeight(exprKey, GetStateValue(key));
                }
            }
        }

        if (leftUpperEyelid != null || rightUpperEyelid != null)
        {
            float bL = Mathf.Clamp01(Mathf.Max(GetStateValue("Blink"), GetStateValue("Blink_L")));
            float bR = Mathf.Clamp01(Mathf.Max(GetStateValue("Blink"), GetStateValue("Blink_R")));

            if (leftUpperEyelid != null)
                leftUpperEyelid.localRotation = lUpperInitRot * Quaternion.Euler(bL * upperEyelidClosePitch, 0f, 0f);
            if (rightUpperEyelid != null)
                rightUpperEyelid.localRotation = rUpperInitRot * Quaternion.Euler(bR * upperEyelidClosePitch, 0f, 0f);
            if (leftLowerEyelid != null)
                leftLowerEyelid.localRotation = lLowerInitRot * Quaternion.Euler(bL * lowerEyelidClosePitch, 0f, 0f);
            if (rightLowerEyelid != null)
                rightLowerEyelid.localRotation = rLowerInitRot * Quaternion.Euler(bR * lowerEyelidClosePitch, 0f, 0f);
        }

        if (jawBone != null)
        {
            float mouthOpen = Mathf.Clamp01(Mathf.Max(GetStateValue("A"), GetStateValue("O"), GetStateValue("U") * 0.7f, GetStateValue("E") * 0.5f, GetStateValue("I") * 0.3f));
            jawBone.localRotation = jawInitRot * Quaternion.Euler(mouthOpen * jawOpenPitch, 0f, 0f);
        }
    }

    private float GetStateValue(string key)
    {
        return states != null && states.TryGetValue(key, out var s) && s != null ? s.value : 0f;
    }

    private float autoBlinkWeight = 0f;

    private void UpdateAutoBlink()
    {
        if (!enableAutoBlink)
        {
            autoBlinkWeight = 0f;
            return;
        }

        float now = Time.time;
        if (blinkStartTime < 0f)
        {
            autoBlinkWeight = 0f;
            if (now >= nextBlinkTime)
            {
                blinkStartTime = now;
                nextBlinkTime = now + Random.Range(3.2f, 5.5f);
            }
        }
        else
        {
            float elapsed = now - blinkStartTime;
            if (elapsed > blinkDuration)
            {
                blinkStartTime = -1f;
                autoBlinkWeight = 0f;
            }
            else
            {
                float t = elapsed / blinkDuration;
                float autoVal = t < 0.45f ? (t / 0.45f) : (1f - (t - 0.45f) / 0.55f);
                autoBlinkWeight = Mathf.Clamp01(autoVal);
            }
        }
    }

    private float GetInputValue(int i) => i switch
    {
        0 => Mathf.Max(Blink, autoBlinkWeight),
        1 => Blink_L,
        2 => Blink_R,
        3 => LookUp,
        4 => LookDown,
        5 => LookLeft,
        6 => LookRight,
        7 => Neutral,
        8 => A,
        9 => I,
        10 => U,
        11 => E,
        12 => O,
        13 => Joy,
        14 => Angry,
        15 => Sorrow,
        16 => Fun,
        _ => 0f
    };


    private void UpdateState(string key, float input, float now, float dt)
    {
        if (!states.TryGetValue(key, out var state)) return;

        bool changed = !Mathf.Approximately(input, state.lastInput);
        bool activelyDriven = !Mathf.Approximately(input, 0f);

        if (changed || activelyDriven)
        {
            state.lastInput = input;
            state.lastUpdateTime = now;
            state.value = input;
            state.holdUntil = now + minHoldTime;
        }
        else
        {
            if (now < state.holdUntil)
            {
                state.value = input;
            }
            else
            {
                float idleTime = now - state.lastUpdateTime;
                if (idleTime > safeTimeout)
                {
                    state.value = 0f;
                }
                else
                {
                    state.value = Mathf.MoveTowards(state.value, 0f, fadeSpeed * dt);
                }
            }
        }
    }
}
