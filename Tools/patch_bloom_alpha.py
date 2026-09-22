#!/usr/bin/env python3
"""
patch_bloom_alpha.py - Zeroes out Bloom alpha multipliers in Uber.shader (PID 381)

macOS WindowServer uses premultiplied alpha for transparent overlay windows:
    DesktopPixel = WindowColor + DesktopColor * (1 - Alpha)

Unity's PostProcessing Uber.shader writes 1.0 into alpha multipliers during Bloom passes,
causing the transparent window alpha around the character to rise and darken the background
desktop wallpaper into a visible oval silhouette.

This script patches all 576 tent and 576 box bloom alpha multipliers from 1.0 to 0.0,
ensuring bloom is purely additive RGB with zero alpha distortion.
"""

import sys
import os
import UnityPy
import lz4.block

def patch_sharedassets(sa0_path):
    if not os.path.exists(sa0_path):
        print(f"[patch_bloom_alpha] Error: {sa0_path} does not exist!")
        return False

    print(f"[patch_bloom_alpha] Loading {sa0_path}...")
    env = UnityPy.load(sa0_path)

    if 381 not in env.file.objects:
        print("[patch_bloom_alpha] Error: Object 381 (Uber.shader) not found!")
        return False

    obj = env.file.objects[381]
    d = obj.read_typetree(check_read=False)

    blob = bytes(d["compressedBlob"])
    new_blob_parts = []
    new_offsets = []
    new_compLens = []
    new_decompLens = []
    current_offset = 0

    total_tent_replaced = 0
    total_box_replaced = 0

    for seg_i in range(len(d["offsets"][0])):
        old_off = d["offsets"][0][seg_i]
        old_clen = d["compressedLengths"][0][seg_i]
        old_dlen = d["decompressedLengths"][0][seg_i]

        seg_data = blob[old_off : old_off + old_clen]
        decomp = lz4.block.decompress(seg_data, uncompressed_size=old_dlen)

        c_tent = decomp.count(b"float4(0.0625, 0.0625, 0.0625, 1.0)")
        c_box = decomp.count(b"float4(0.25, 0.25, 0.25, 1.0)")
        total_tent_replaced += c_tent
        total_box_replaced += c_box

        decomp = decomp.replace(b"float4(0.0625, 0.0625, 0.0625, 1.0)", b"float4(0.0625, 0.0625, 0.0625, 0.0)")
        decomp = decomp.replace(b"float4(0.25, 0.25, 0.25, 1.0)", b"float4(0.25, 0.25, 0.25, 0.0)")

        comp_hc = lz4.block.compress(decomp, mode="high_compression", compression=9, store_size=False)

        new_offsets.append(current_offset)
        new_compLens.append(len(comp_hc))
        new_decompLens.append(len(decomp))
        new_blob_parts.append(comp_hc)
        current_offset += len(comp_hc)

    if total_tent_replaced == 0 and total_box_replaced == 0:
        print("[patch_bloom_alpha] Shader is already patched (0 alpha multipliers to replace).")
        return True

    new_blob = b"".join(new_blob_parts)
    d["compressedBlob"] = list(new_blob)
    d["offsets"] = [new_offsets]
    d["compressedLengths"] = [new_compLens]
    d["decompressedLengths"] = [new_decompLens]

    obj.save_typetree(d)

    print(f"[patch_bloom_alpha] Replaced {total_tent_replaced} tent and {total_box_replaced} box bloom alpha multipliers (1.0 -> 0.0).")

    patched_data = env.file.save()
    with open(sa0_path, "wb") as f:
        f.write(patched_data)

    print(f"[patch_bloom_alpha] Successfully saved patched assets to {sa0_path}")
    return True

if __name__ == "__main__":
    target = sys.argv[1] if len(sys.argv) > 1 else "/Applications/MateEngineX.app/Contents/Resources/Data/sharedassets0.assets"
    success = patch_sharedassets(target)
    sys.exit(0 if success else 1)
