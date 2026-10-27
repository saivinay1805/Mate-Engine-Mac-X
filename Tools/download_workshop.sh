#!/usr/bin/env bash
set -euo pipefail

export PATH="$PATH:/opt/homebrew/bin:/usr/local/bin"

# Steam App ID for Mate Engine
APP_ID=3625270

if [ $# -lt 1 ]; then
  echo "Usage: $0 <workshop_id_or_url>"
  echo "Example: $0 3672894479"
  echo "Example: $0 https://steamcommunity.com/sharedfiles/filedetails/?id=3672894479"
  exit 1
fi

INPUT="$1"
TARGET_BASE="${2:-${MATE_PERSISTENT_DATA_PATH:-$HOME/Library/Application Support/Shinymoon/MateEngineX}}"
ID=$(echo "$INPUT" | grep -oE '[0-9]{8,12}' | head -n 1)

if [ -z "$ID" ]; then
  echo "Error: Could not extract a valid Workshop ID from '$INPUT'" >&2
  exit 1
fi

echo "=============================================="
echo "Downloading Steam Workshop item: $ID"
echo "=============================================="

steamcmd +login anonymous +workshop_download_item "$APP_ID" "$ID" +quit

STEAM_DIR="$HOME/Library/Application Support/Steam/steamapps/workshop/content/$APP_ID/$ID"
if [ ! -d "$STEAM_DIR" ]; then
  echo "Error: Downloaded folder not found at: $STEAM_DIR" >&2
  exit 1
fi

echo "Installing to MateEngineX..."
python3 - "$APP_ID" "$ID" "$TARGET_BASE" << 'PYEOF'
import os, sys, shutil, json, fcntl

app_id = sys.argv[1]
item_id = sys.argv[2]
target_base = os.path.expanduser(sys.argv[3])
steam_dir = os.path.expanduser('~/Library/Application Support/Steam/steamapps/workshop/content/' + app_id + '/' + item_id)

os.makedirs(os.path.join(target_base, 'Steam Workshop'), exist_ok=True)
os.makedirs(os.path.join(target_base, 'Thumbnails'), exist_ok=True)
os.makedirs(os.path.join(target_base, 'Mods'), exist_ok=True)

files = os.listdir(steam_dir)
vrm_file = next((f for f in files if f.lower().endswith('.vrm')), None)
me_file = next((f for f in files if f.lower().endswith('.me')), None)
unity3d_file = next((f for f in files if f.lower().endswith('.unity3d')), None)
thumb_file = next((f for f in files if f.lower().endswith('.png') or f.lower().endswith('.jpg')), None)

metadata = {}
meta_path = os.path.join(steam_dir, 'metadata.json')
if os.path.exists(meta_path):
    try:
        with open(meta_path, 'r', encoding='utf-8') as f:
            metadata = json.load(f)
    except:
        pass

def is_dance_or_mod_me(filepath):
    if not filepath or not os.path.exists(filepath):
        return False
    try:
        import zipfile
        if zipfile.is_zipfile(filepath):
            with zipfile.ZipFile(filepath, 'r') as z:
                names = [n.lower() for n in z.namelist()]
                return any('dance_meta.json' in n or n.endswith('.bundle') for n in names)
    except:
        pass
    return False

me_path = os.path.join(steam_dir, me_file) if me_file else None
me_is_dance = is_dance_or_mod_me(me_path)

if vrm_file or (me_file and not me_is_dance):
    model_name = vrm_file if vrm_file else me_file
    src_model = os.path.join(steam_dir, model_name)
    item_dir = os.path.join(target_base, 'Steam Workshop', item_id)
    os.makedirs(item_dir, exist_ok=True)
    dst_model = os.path.join(item_dir, model_name)
    shutil.copy2(src_model, dst_model)
    
    dst_thumb = ''
    if thumb_file:
        src_thumb = os.path.join(steam_dir, thumb_file)
        dst_thumb = os.path.join(target_base, 'Thumbnails', f'{item_id}_{thumb_file}')
        shutil.copy2(src_thumb, dst_thumb)
    elif model_name.endswith('.vrm'):
        try:
            import struct
            with open(src_model, 'rb') as f:
                f.read(12)
                chunk_len, _ = struct.unpack('<II', f.read(8))
                gltf = json.loads(f.read(chunk_len).decode('utf-8'))
                f.read(8) # bin header
                bin_offset = f.tell()
                
                vrm1_meta = gltf.get('extensions', {}).get('VRMC_vrm', {}).get('meta', {})
                thumb_img = vrm1_meta.get('thumbnailImage')
                vrm0_meta = gltf.get('extensions', {}).get('VRM', {}).get('meta', {})
                texture_idx = vrm0_meta.get('texture')
                
                target_img_idx = None
                if thumb_img is not None:
                    target_img_idx = thumb_img
                elif texture_idx is not None and texture_idx >= 0:
                    target_img_idx = gltf.get('textures', [])[texture_idx].get('source')
                elif gltf.get('images'):
                    target_img_idx = 0
                
                if target_img_idx is not None and target_img_idx < len(gltf.get('images', [])):
                    img_info = gltf['images'][target_img_idx]
                    bv = gltf['bufferViews'][img_info['bufferView']]
                    f.seek(bin_offset + bv.get('byteOffset', 0))
                    img_data = f.read(bv['byteLength'])
                    dst_thumb = os.path.join(target_base, 'Thumbnails', f'{item_id}_{os.path.splitext(model_name)[0]}_thumb.png')
                    with open(dst_thumb, 'wb') as thumb_f:
                        thumb_f.write(img_data)
        except Exception as e:
            print(f'Thumbnail extraction error: {e}')
        
    avatars_lock = os.path.join(target_base, 'avatars.json.lock')
    with open(avatars_lock, 'w') as alf:
        fcntl.flock(alf, fcntl.LOCK_EX)
        try:
            avatars_json_path = os.path.join(target_base, 'avatars.json')
            entries = []
            if os.path.exists(avatars_json_path):
                try:
                    with open(avatars_json_path, 'r', encoding='utf-8') as f:
                        entries = json.load(f)
                except:
                    entries = []
                    
            clean_entries = [e for e in entries if str(e.get('steamFileId')) != item_id]
            clean_entries.append({
                'displayName': metadata.get('displayName', os.path.splitext(model_name)[0]),
                'author': metadata.get('author', 'Workshop'),
                'version': str(metadata.get('version', '1.0')),
                'fileType': metadata.get('fileType', 'VRM1.X' if model_name.endswith('.vrm') else '.ME'),
                'filePath': dst_model,
                'thumbnailPath': dst_thumb,
                'polygonCount': int(metadata.get('polygonCount', 0)),
                'isSteamWorkshop': True,
                'steamFileId': int(item_id),
                'isNSFW': bool(metadata.get('isNSFW', False)),
                'isOwner': False
            })
            tmp_json = avatars_json_path + f'.tmp.{os.getpid()}'
            with open(tmp_json, 'w', encoding='utf-8') as f:
                json.dump(clean_entries, f, indent=2, ensure_ascii=False)
            os.replace(tmp_json, avatars_json_path)
        finally:
            fcntl.flock(alf, fcntl.LOCK_UN)
        
    print(f'Successfully installed avatar: {model_name} to MateEngineX library!')

if unity3d_file or (me_file and me_is_dance):
    mod_file = unity3d_file if unity3d_file else me_file
    src_dance = os.path.join(steam_dir, mod_file)
    dst_dance = os.path.join(target_base, 'Mods', mod_file)
    shutil.copy2(src_dance, dst_dance)

    map_lock = os.path.join(target_base, 'mods_workshop_map.json.lock')
    with open(map_lock, 'w') as mlf:
        fcntl.flock(mlf, fcntl.LOCK_EX)
        try:
            map_path = os.path.join(target_base, 'mods_workshop_map.json')
            mod_map = {}
            if os.path.exists(map_path):
                try:
                    with open(map_path, 'r', encoding='utf-8') as mf:
                        mod_map = json.load(mf)
                except:
                    mod_map = {}
            try:
                mod_map[mod_file] = int(item_id)
            except:
                mod_map[mod_file] = item_id
            tmp_map = map_path + f'.tmp.{os.getpid()}'
            with open(tmp_map, 'w', encoding='utf-8') as mf:
                json.dump(mod_map, mf, indent=2)
            os.replace(tmp_map, map_path)
        finally:
            fcntl.flock(mlf, fcntl.LOCK_UN)

    print(f'Successfully installed dance: {mod_file} to Mods folder!')
PYEOF
