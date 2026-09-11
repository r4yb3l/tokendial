#!/bin/sh
# Builds the disk image a Mac user expects: the app on the left, a link to Applications on the right,
# an arrow between them and no Finder chrome, so the window itself is the instruction. Expects an
# exported dist/Tokendial.app and needs nothing that Xcode does not already install.
set -e
export PATH=/opt/homebrew/bin:/usr/local/bin:$PATH
cd "$(dirname "$0")/.."

VERSION=$(tr -d '[:space:]' < ../VERSION)
VOL="Tokendial ${VERSION}"
APP=dist/Tokendial.app
STAGE=build/dmg-stage
RW=build/dmg-rw.dmg
OUT="dist/Tokendial-${VERSION}-macos.dmg"

[ -d "$APP" ] || { echo "no app at ${APP}"; exit 1; }

hdiutil detach "/Volumes/${VOL}" -quiet 2>/dev/null || true
rm -rf "$STAGE" "$RW"
mkdir -p "$STAGE/.background" dist
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"

# One file carrying both resolutions is how the backdrop stays sharp on a Retina display.
tiffutil -cathidpicheck Resources/dmg/background.png Resources/dmg/background@2x.png \
  -out "$STAGE/.background/background.tiff" >/dev/null

hdiutil create -volname "$VOL" -srcfolder "$STAGE" -ov -format UDRW -fs HFS+ "$RW" >/dev/null
hdiutil attach "$RW" -nobrowse -quiet
sleep 2

# Only Finder can write the window's arrangement, and only through Apple Events. A machine that refuses
# automation still produces a working image, just an unarranged one, so this must not fail the build.
osascript <<AS || echo "warning: Finder declined to arrange the window; shipping the default layout"
tell application "Finder"
  tell disk "${VOL}"
    open
    set current view of container window to icon view
    set toolbar visible of container window to false
    set statusbar visible of container window to false
    set the bounds of container window to {240, 180, 840, 608}
    set opts to the icon view options of container window
    set arrangement of opts to not arranged
    set icon size of opts to 128
    set text size of opts to 13
    set background picture of opts to file ".background:background.tiff"
    set position of item "Tokendial.app" of container window to {150, 190}
    set position of item "Applications" of container window to {450, 190}
    update without registering applications
    delay 1
    close
  end tell
end tell
AS

sync
hdiutil detach "/Volumes/${VOL}" -quiet
rm -f "$OUT"
hdiutil convert "$RW" -format UDZO -imagekey zlib-level=9 -o "$OUT" >/dev/null
rm -f "$RW"
ls -la "$OUT" | awk '{printf "%.2f MB  %s\n", $5/1048576, $9}'
