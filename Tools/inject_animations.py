#!/usr/bin/env python3
"""
inject_animations.py - Injects MateEngine 3.4X animations and AnimatorController into sharedassets0.assets

This script injects:
- 4 Dancing animations: PET_DANCING_14 (Hip-Hop), 15 (Samba), 16 (Salsa), 17 (Belly Dance)
- 6 Idle animations: PET_IDLE_16 (Silly Talk), 17 (Look Around), 18, 19, 20, 21
- 2 Happi Jumpy Walking animations: Happi Walk L, Happi Walk R
- 2 Post-walk Cute Spin animations: Spin L, Spin R
- Full AvatarAnimatorControllerV2 1 state machine (138 clips, blend trees, bouncy walk, cute spin transitions)
"""

import os
import sys
import copy
import pickle
import UnityPy

def inject_animations(target_sa0_path):
    if not os.path.exists(target_sa0_path):
        print(f"[inject_animations] Error: Target {target_sa0_path} does not exist!")
        return False

    script_dir = os.path.dirname(os.path.abspath(__file__))
    pkl_path = os.path.join(script_dir, "assets", "x34_anims.pkl")
    steam_sa0_path = "/Volumes/APFS X9/apps/MateEngine [Build 24920588]/MateEngine/MateEngineX_Data/sharedassets0.assets"

    s_clip_name_by_pid = {}
    missing_clip_trees = {}
    s_ctrl_tree = None

    if os.path.exists(pkl_path):
        print(f"[inject_animations] Loading source animations from {pkl_path}...")
        with open(pkl_path, "rb") as f:
            data_pkg = pickle.load(f)
        s_clip_name_by_pid = data_pkg["steam_clip_name_by_pid"]
        missing_clip_trees = data_pkg["missing_clip_trees"]
        s_ctrl_tree = data_pkg["ctrl_tree"]
    elif os.path.exists(steam_sa0_path):
        print(f"[inject_animations] Loading source animations from Steam build {steam_sa0_path}...")
        s_env = UnityPy.load(steam_sa0_path)
        for obj in s_env.objects:
            if obj.type.name == "AnimationClip":
                tree = obj.read_typetree()
                s_clip_name_by_pid[obj.path_id] = tree["m_Name"]
                if tree["m_Name"] in [
                    'Happi Walk L', 'Happi Walk R',
                    'PET_DANCING_14', 'PET_DANCING_15', 'PET_DANCING_16', 'PET_DANCING_17',
                    'PET_IDLE_16', 'PET_IDLE_17', 'PET_IDLE_18', 'PET_IDLE_19', 'PET_IDLE_20', 'PET_IDLE_21',
                    'Spin L', 'Spin R'
                ]:
                    missing_clip_trees[tree["m_Name"]] = tree
        s_ctrl_obj = [o for o in s_env.objects if o.type.name == 'AnimatorController' and o.read_typetree().get('m_Name') == 'AvatarAnimatorControllerV2 1'][0]
        s_ctrl_tree = s_ctrl_obj.read_typetree()
    else:
        print("[inject_animations] Error: Neither x34_anims.pkl nor Steam build found!")
        return False

    print(f"[inject_animations] Loading target {target_sa0_path}...")
    m_env = UnityPy.load(target_sa0_path)

    # Check if already patched
    m_ctrl_objs = [o for o in m_env.objects if o.type.name == 'AnimatorController' and o.read_typetree().get('m_Name') == 'AvatarAnimatorControllerV2 1']
    if not m_ctrl_objs:
        print("[inject_animations] Error: AvatarAnimatorControllerV2 1 not found in target!")
        return False
    m_ctrl_obj = m_ctrl_objs[0]
    existing_ctrl_tree = m_ctrl_obj.read_typetree()
    if len(existing_ctrl_tree.get("m_AnimationClips", [])) >= 140:
        print(f"[inject_animations] Target already has {len(existing_ctrl_tree.get('m_AnimationClips', []))} animation clips in AvatarAnimatorControllerV2 1. Skipping.")
        return True

    # Map existing clips in target
    m_clip_pid_by_name = {}
    sample_m_clip = None
    for obj in m_env.objects:
        if obj.type.name == "AnimationClip":
            sample_m_clip = obj
            tree = obj.read_typetree()
            m_clip_pid_by_name[tree["m_Name"]] = obj.path_id

    if sample_m_clip is None:
        print("[inject_animations] Error: No existing AnimationClip found in target to use as template!")
        return False

    # Inject missing clips
    next_pid = max(m_env.file.objects.keys()) + 1
    injected_count = 0
    for name, s_tree in missing_clip_trees.items():
        if name in m_clip_pid_by_name:
            continue
        tree = copy.deepcopy(s_tree)
        # Adapt genericBindings for Unity 6000.6.0f1
        if "m_ClipBindingConstant" in tree and "genericBindings" in tree["m_ClipBindingConstant"]:
            for gb in tree["m_ClipBindingConstant"]["genericBindings"]:
                if "metaData" not in gb:
                    gb["metaData"] = 0

        new_pid = next_pid
        next_pid += 1

        new_obj = copy.copy(sample_m_clip)
        new_obj.path_id = new_pid
        new_obj.save_typetree(tree)
        m_env.file.objects[new_pid] = new_obj
        m_clip_pid_by_name[name] = new_pid
        injected_count += 1

    print(f"[inject_animations] Injected {injected_count} new AnimationClips into sharedassets0.assets.")

    # Configure AvatarAnimatorControllerV2 1
    ctrl_tree = copy.deepcopy(s_ctrl_tree)

    # Add m_EvaluateTransitionsOnStart required by Unity 6000.6.0f1
    ctrl_tree["m_EvaluateTransitionsOnStart"] = False
    for sm in ctrl_tree.get("m_Controller", {}).get("m_StateMachineArray", []):
        if "data" in sm and isinstance(sm["data"], dict):
            sm["data"]["m_EvaluateTransitionsOnStart"] = False

    # Remap m_AnimationClips to target PathIDs
    remapped = 0
    for c in ctrl_tree.get("m_AnimationClips", []):
        steam_pid = c["m_PathID"]
        clip_name = s_clip_name_by_pid.get(steam_pid)
        if clip_name and clip_name in m_clip_pid_by_name:
            c["m_PathID"] = m_clip_pid_by_name[clip_name]
            remapped += 1
        else:
            print(f"[inject_animations] Warning: Could not remap clip steam_pid={steam_pid} (name={clip_name})")

    # Also append classic PET_WALK_LEFT and PET_WALK_RIGHT so both normal and happi walk clips are accessible in runtime
    for walk_name in ["PET_WALK_LEFT", "PET_WALK_RIGHT"]:
        if walk_name in m_clip_pid_by_name:
            walk_pid = m_clip_pid_by_name[walk_name]
            ctrl_tree["m_AnimationClips"].append({"m_FileID": 0, "m_PathID": walk_pid})
            print(f"[inject_animations] Appended {walk_name} (pid={walk_pid}) to controller clips.")

    print(f"[inject_animations] Remapped {remapped} clips; total {len(ctrl_tree['m_AnimationClips'])} controller clip references.")
    m_ctrl_obj.save_typetree(ctrl_tree)

    # Save target file
    print(f"[inject_animations] Saving updated {target_sa0_path}...")
    saved_data = m_env.file.save()
    with open(target_sa0_path, "wb") as f:
        f.write(saved_data)

    # Verify reload
    re_env = UnityPy.load(target_sa0_path)
    re_ctrl = [o for o in re_env.objects if o.type.name == 'AnimatorController' and o.read_typetree().get('m_Name') == 'AvatarAnimatorControllerV2 1'][0].read_typetree()
    re_clips = re_ctrl.get("m_AnimationClips", [])
    if len(re_clips) >= 138:
        print(f"[inject_animations] Verification SUCCESS: AvatarAnimatorControllerV2 1 has {len(re_clips)} clips.")
        return True
    else:
        print(f"[inject_animations] Verification FAILURE: expected 138 clips, got {len(re_clips)}")
        return False

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(f"Usage: {sys.argv[0]} <path_to_sharedassets0.assets>")
        sys.exit(1)
    success = inject_animations(sys.argv[1])
    sys.exit(0 if success else 1)
