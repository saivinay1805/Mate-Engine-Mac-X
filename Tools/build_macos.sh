#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [ -n "${UNITY_BIN:-}" ]; then
  :
elif [ -x "/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity" ]; then
  UNITY_BIN="/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity"
elif [ -x "/Applications/Unity/Hub/Editor/6000.4.8f1/Unity.app/Contents/MacOS/Unity" ]; then
  UNITY_BIN="/Applications/Unity/Hub/Editor/6000.4.8f1/Unity.app/Contents/MacOS/Unity"
elif compgen -G "/Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity" > /dev/null; then
  UNITY_BIN="$(ls -d /Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity | head -n 1)"
elif [ -x "/Applications/Unity/Unity-6000.4.8f1/Unity.app/Contents/MacOS/Unity" ]; then
  UNITY_BIN="/Applications/Unity/Unity-6000.4.8f1/Unity.app/Contents/MacOS/Unity"
else
  UNITY_BIN=""
fi
OUTPUT="${OUTPUT:-Builds/macOS/MateEngineX.app}"
LOG_DIR="$ROOT/Builds"
LOG_FILE="$LOG_DIR/macos-build.log"
SIGN_IDENTITY="${SIGN_IDENTITY:-}"
if [ -z "$SIGN_IDENTITY" ] || [ "$SIGN_IDENTITY" = "-" ]; then
  DETECTED_ID="$(security find-identity -v -p codesigning | grep -o 'Developer ID Application: [^"]*' | head -n 1 || true)"
  if [ -n "$DETECTED_ID" ]; then
    SIGN_IDENTITY="$DETECTED_ID"
    echo "[build_macos] Auto-detected Developer ID: $SIGN_IDENTITY"
  else
    SIGN_IDENTITY="-"
  fi
fi
NOTARIZE="${NOTARIZE:-0}"
PACKAGE_DMG="${PACKAGE_DMG:-0}"

if [ -z "$UNITY_BIN" ] || [ ! -x "$UNITY_BIN" ]; then
  echo "Unity editor not found at: $UNITY_BIN" >&2
  echo "Install Unity 6000.4.8f1, or set UNITY_BIN to the Unity executable." >&2
  exit 1
fi

mkdir -p "$LOG_DIR"

echo "[build_macos] Unity: $UNITY_BIN"
echo "[build_macos] Project: $ROOT"
echo "[build_macos] Output: $OUTPUT"
echo "[build_macos] Log: $LOG_FILE"

"$ROOT/Tools/build_native_macos.sh"

echo "[build_macos] Building Apple Silicon (arm64) slice..."
"$UNITY_BIN" -batchmode -quit \
  -projectPath "$ROOT" \
  -executeMethod MacBuild.BuildFromCommandLine \
  -architecture arm64 \
  -output "$ROOT/Builds/macOS-arm64/MateEngineX.app" \
  -logFile "$ROOT/Builds/arm64-build.log"

echo "[build_macos] Building Intel (x86_64) slice..."
"$UNITY_BIN" -batchmode -quit \
  -projectPath "$ROOT" \
  -executeMethod MacBuild.BuildFromCommandLine \
  -architecture x64 \
  -output "$ROOT/Builds/macOS-x64/MateEngineX.app" \
  -logFile "$ROOT/Builds/x64-build.log"

echo "[build_macos] Assembling Universal app bundle..."
python3 -c "
import os, subprocess, shutil

root = '$ROOT'
arm_app = os.path.join(root, 'Builds/macOS-arm64/MateEngineX.app')
x64_app = os.path.join(root, 'Builds/macOS-x64/MateEngineX.app')
out_app = os.path.join(root, '$OUTPUT')

print('Assembling Universal app at', out_app)
if os.path.exists(out_app):
    shutil.rmtree(out_app)

shutil.copytree(arm_app, out_app, symlinks=True)

binaries_to_merge = [
    'Contents/MacOS/MateEngineX',
    'Contents/Frameworks/UnityPlayer.dylib',
    'Contents/Frameworks/libmonobdwgc-2.0.dylib',
    'Contents/Frameworks/libmono-native.dylib',
    'Contents/Frameworks/libMonoPosixHelper.dylib',
    'Contents/PlugIns/lib_burst_generated.bundle'
]

for b in binaries_to_merge:
    p_arm = os.path.join(arm_app, b)
    p_x64 = os.path.join(x64_app, b)
    p_out = os.path.join(out_app, b)
    if os.path.exists(p_arm) and os.path.exists(p_x64):
        print('Lipo merging:', b)
        subprocess.run(['lipo', '-create', p_x64, p_arm, '-output', p_out], check=True)
    else:
        print('Warning: could not find both slices for', b)

print('Universal assembly complete!')
"

if [ -d "$ROOT/$OUTPUT" ] || [ -d "$OUTPUT" ]; then
  APP_BUNDLE="$OUTPUT"
  [ -d "$APP_BUNDLE" ] || APP_BUNDLE="$ROOT/$OUTPUT"

  # Copy matesfbhelper to Contents/MacOS
  if [ -f "$ROOT/Assets/Plugins/MacOS/matesfbhelper" ]; then
    echo "[build_macos] Installing matesfbhelper to $APP_BUNDLE/Contents/MacOS/matesfbhelper"
    cp -f "$ROOT/Assets/Plugins/MacOS/matesfbhelper" "$APP_BUNDLE/Contents/MacOS/matesfbhelper"
    chmod +x "$APP_BUNDLE/Contents/MacOS/matesfbhelper"
  fi

  # Patch bloom alpha multipliers in sharedassets0.assets to prevent dark oval artifact on macOS
  if [ -f "$ROOT/Tools/patch_bloom_alpha.py" ] && [ -f "$APP_BUNDLE/Contents/Resources/Data/sharedassets0.assets" ]; then
    echo "[build_macos] Applying macOS bloom alpha fix to sharedassets0.assets..."
    python3 "$ROOT/Tools/patch_bloom_alpha.py" "$APP_BUNDLE/Contents/Resources/Data/sharedassets0.assets" || true
  fi

  # Inject MateEngine 3.4X animations and AnimatorController (27 dances/idles, bouncy walk, spins)
  if [ -f "$ROOT/Tools/inject_animations.py" ] && [ -f "$APP_BUNDLE/Contents/Resources/Data/sharedassets0.assets" ]; then
    echo "[build_macos] Injecting MateEngine 3.4X animations to sharedassets0.assets..."
    python3 "$ROOT/Tools/inject_animations.py" "$APP_BUNDLE/Contents/Resources/Data/sharedassets0.assets"
  fi

  # Remove any rogue test dance bundles
  rm -f "$APP_BUNDLE/Contents/Resources/Data/StreamingAssets/CustomDances/new_dances.unity3d"

  CODESIGN_FLAGS=()
  if [ "$SIGN_IDENTITY" != "-" ]; then
    CODESIGN_FLAGS=(--options runtime --timestamp)
  fi

  ENTITLEMENTS=""
  if [ -f "$ROOT/Tools/MateEngineX.entitlements" ]; then
    ENTITLEMENTS="$ROOT/Tools/MateEngineX.entitlements"
  fi

  echo "[build_macos] Signing $APP_BUNDLE with '$SIGN_IDENTITY' (flags: ${CODESIGN_FLAGS[*]:-none})"
  # Sign all dynamic libraries, frameworks, bundle files/directories, and helper executables
  find "$APP_BUNDLE/Contents" \( -name '*.bundle' -o -name '*.dylib' -o -name '*.framework' -o -name 'matesfbhelper' \) -print0 \
    | xargs -0 -n1 codesign --force "${CODESIGN_FLAGS[@]}" --sign "$SIGN_IDENTITY"

  # Sign main executable
  if [ -f "$APP_BUNDLE/Contents/MacOS/MateEngineX" ]; then
    codesign --force "${CODESIGN_FLAGS[@]}" --sign "$SIGN_IDENTITY" "$APP_BUNDLE/Contents/MacOS/MateEngineX"
  fi

  if [ -n "$ENTITLEMENTS" ] && [ "$SIGN_IDENTITY" != "-" ]; then
    codesign --force "${CODESIGN_FLAGS[@]}" --entitlements "$ENTITLEMENTS" --sign "$SIGN_IDENTITY" "$APP_BUNDLE"
  else
    codesign --force "${CODESIGN_FLAGS[@]}" --sign "$SIGN_IDENTITY" "$APP_BUNDLE"
  fi
  codesign --verify --deep --strict "$APP_BUNDLE"

  REQUIRE_UNIVERSAL="${REQUIRE_UNIVERSAL:-1}"
  if [ "$REQUIRE_UNIVERSAL" = "1" ]; then
    echo "[build_macos] Checking universal architectures"
    for binary in \
      "$APP_BUNDLE/Contents/MacOS/MateEngineX" \
      "$APP_BUNDLE/Contents/MacOS/matesfbhelper" \
      "$APP_BUNDLE"/Contents/PlugIns/*.bundle/Contents/MacOS/* \
      "$APP_BUNDLE"/Contents/Frameworks/*.dylib; do
      [ -e "$binary" ] || continue
      if ! lipo -info "$binary" | grep -q "x86_64" || ! lipo -info "$binary" | grep -q "arm64"; then
        echo "[build_macos] Not universal: $binary" >&2
        lipo -info "$binary" >&2 || true
        exit 1
      fi
    done
    echo "[build_macos] Universal architecture check OK"
  fi

  if [ "$PACKAGE_DMG" = "1" ]; then
    DMG="$LOG_DIR/MateEngineX.dmg"
    rm -f "$DMG"
    DMG_STAGING="$(mktemp -d)"
    ditto "$APP_BUNDLE" "$DMG_STAGING/$(basename "$APP_BUNDLE")"
    ln -s /Applications "$DMG_STAGING/Applications"
    hdiutil create \
      -volname "MateEngineX" \
      -srcfolder "$DMG_STAGING" \
      -ov \
      -format UDZO \
      "$DMG"
    rm -rf "$DMG_STAGING"
    echo "[build_macos] DMG: $DMG"
    if [ "$SIGN_IDENTITY" != "-" ]; then
      echo "[build_macos] Signing DMG with '$SIGN_IDENTITY'"
      codesign --force "${CODESIGN_FLAGS[@]}" --sign "$SIGN_IDENTITY" "$DMG"
    fi
  fi

  if [ "$NOTARIZE" = "1" ]; then
    NOTARY_TARGET="$APP_BUNDLE"
    if [ "$PACKAGE_DMG" = "1" ] && [ -f "$LOG_DIR/MateEngineX.dmg" ]; then
      NOTARY_TARGET="$LOG_DIR/MateEngineX.dmg"
    else
      NOTARY_TARGET="$LOG_DIR/MateEngineX-notarize.zip"
      rm -f "$NOTARY_TARGET"
      ditto -c -k --keepParent "$APP_BUNDLE" "$NOTARY_TARGET"
    fi

    if [ -n "${NOTARY_KEYCHAIN_PROFILE:-}" ]; then
      echo "[build_macos] Submitting to Apple Notary Service using keychain profile '$NOTARY_KEYCHAIN_PROFILE'..."
      xcrun notarytool submit "$NOTARY_TARGET" --keychain-profile "$NOTARY_KEYCHAIN_PROFILE" --wait
      if [ "$PACKAGE_DMG" = "1" ] && [ -f "$LOG_DIR/MateEngineX.dmg" ]; then
        xcrun stapler staple "$LOG_DIR/MateEngineX.dmg"
      fi
      xcrun stapler staple "$APP_BUNDLE"
      echo "[build_macos] Notarized and stapled: $APP_BUNDLE"
    else
      APPLE_ID_PASSWORD="${APPLE_ID_PASSWORD:-${APPLE_PASSWORD:-}}"
      APPLE_TEAM_ID="${APPLE_TEAM_ID:-4WD9WKBAK4}"
      if [ -n "${APPLE_ID:-}" ] && [ -n "$APPLE_ID_PASSWORD" ]; then
        echo "[build_macos] Submitting to Apple Notary Service using Apple ID '$APPLE_ID'..."
        xcrun notarytool submit "$NOTARY_TARGET" \
          --apple-id "$APPLE_ID" \
          --team-id "$APPLE_TEAM_ID" \
          --password "$APPLE_ID_PASSWORD" \
          --wait
        if [ "$PACKAGE_DMG" = "1" ] && [ -f "$LOG_DIR/MateEngineX.dmg" ]; then
          xcrun stapler staple "$LOG_DIR/MateEngineX.dmg"
        fi
        xcrun stapler staple "$APP_BUNDLE"
        echo "[build_macos] Notarized and stapled: $APP_BUNDLE"
      else
        echo "[build_macos] NOTARIZE=1 requested, but neither NOTARY_KEYCHAIN_PROFILE nor APPLE_ID & APPLE_ID_PASSWORD are provided." >&2
      fi
    fi
  fi

  echo "[build_macos] Done: $APP_BUNDLE"
else
  echo "[build_macos] Build completed but output was not found; check $LOG_FILE" >&2
  exit 1
fi
