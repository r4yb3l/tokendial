#!/bin/zsh
# codesign needs the login keychain, which an SSH session cannot unlock. Run xcodegen and xcodebuild inside
# the user's GUI launchd domain instead and stream the log back.
set -e
export PATH=/opt/homebrew/bin:/usr/local/bin:$PATH
cd "$(dirname "$0")/.."
CONFIG="${1:-Debug}"
UID_=$(id -u)
LOG=/tmp/tokendial-build.log
DONE=/tmp/tokendial-build.done
rm -f "$LOG" "$DONE"
launchctl bootout "gui/$UID_/tokendial.build" 2>/dev/null || true
cat > /tmp/tokendial-build.sh <<SCRIPT
#!/bin/zsh
export PATH=/opt/homebrew/bin:/usr/local/bin:\$PATH
cd "$(pwd)"
xcodegen generate >/dev/null 2>&1
xcodebuild -project Tokendial.xcodeproj -scheme Tokendial -destination 'platform=macOS' -configuration $CONFIG -derivedDataPath build CODE_SIGN_STYLE=Manual DEVELOPMENT_TEAM= MARKETING_VERSION=$(tr -d '[:space:]' < ../VERSION) build
echo "exit=\$?" > $DONE
SCRIPT
chmod +x /tmp/tokendial-build.sh
cat > /tmp/tokendial-build.plist <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>Label</key><string>tokendial.build</string>
  <key>ProgramArguments</key><array><string>/tmp/tokendial-build.sh</string></array>
  <key>RunAtLoad</key><true/>
  <key>StandardOutPath</key><string>$LOG</string>
  <key>StandardErrorPath</key><string>$LOG</string>
</dict></plist>
PLIST
launchctl bootstrap "gui/$UID_" /tmp/tokendial-build.plist
for i in $(seq 1 240); do
  sleep 2
  [ -f "$DONE" ] && break
done
launchctl bootout "gui/$UID_/tokendial.build" 2>/dev/null || true
grep -E "error:|error |errSec|BUILD|Signing Identity" "$LOG" | grep -v "appintentsmetadataprocessor" | tail -20
if [ -f "$DONE" ]; then cat "$DONE"; grep -q "exit=0" "$DONE"; else echo "build still running after timeout; a keychain prompt may be waiting on the Mac"; exit 1; fi
