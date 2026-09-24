#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEFAULT_DMG="$ROOT/Builds/MateEngineX.dmg"
DMG="$DEFAULT_DMG"

APPLE_ID="${APPLE_ID:-}"
APPLE_PASSWORD="${APPLE_PASSWORD:-}"
TEAM_ID="${APPLE_TEAM_ID:-4WD9WKBAK4}"

# If first parameter is a file path (like a custom DMG path), use it
if [ $# -ge 1 ] && [ -f "$1" ]; then
  DMG="$1"
  shift
fi

# If remaining parameters are provided, treat them as Apple ID and App-Specific Password
if [ $# -ge 2 ]; then
  APPLE_ID="$1"
  APPLE_PASSWORD="$2"
elif [ $# -eq 1 ] && [ -z "$APPLE_ID" ] && [[ "$1" == *"@"* ]]; then
  APPLE_ID="$1"
fi

if [ ! -f "$DMG" ]; then
  echo "Error: DMG not found at: $DMG" >&2
  exit 1
fi

echo "=== Submitting for Apple Notarization ==="
echo "Target:   $DMG"
echo "Team ID:  $TEAM_ID"

if [ -n "${NOTARY_PROFILE:-}" ]; then
  echo "Profile:  $NOTARY_PROFILE"
  xcrun notarytool submit "$DMG" --keychain-profile "$NOTARY_PROFILE" --wait
elif [ -n "$APPLE_ID" ] && [ -n "$APPLE_PASSWORD" ]; then
  echo "Apple ID: $APPLE_ID"
  xcrun notarytool submit "$DMG" \
    --apple-id "$APPLE_ID" \
    --team-id "$TEAM_ID" \
    --password "$APPLE_PASSWORD" \
    --wait
else
  echo ""
  echo "Usage:"
  echo "  1) Using Apple ID and App-Specific Password:"
  echo "     ./Tools/notarize_dmg.sh <apple-id@email.com> <app-specific-password>"
  echo "     or:"
  echo "     APPLE_ID=\"...\" APPLE_PASSWORD=\"...\" ./Tools/notarize_dmg.sh"
  echo ""
  echo "  2) Using Stored Keychain Profile:"
  echo "     NOTARY_PROFILE=\"<profile-name>\" ./Tools/notarize_dmg.sh"
  echo ""
  echo "To store credentials in keychain once (recommended):"
  echo "  xcrun notarytool store-credentials \"notary-profile\" \\"
  echo "    --apple-id \"your-apple-id@email.com\" \\"
  echo "    --team-id \"4WD9WKBAK4\" \\"
  echo "    --password \"<app-specific-password>\""
  exit 1
fi

echo ""
echo "=== Stapling Notarization Ticket to DMG ==="
xcrun stapler staple "$DMG"

echo ""
echo "=== Verifying Gatekeeper Assessment ==="
spctl --assess --type open --context context:primary-signature --verbose "$DMG" || true

echo ""
echo "SUCCESS! $DMG is fully notarized, stapled, and ready for release!"
