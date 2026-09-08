#!/bin/zsh
# screencapture cannot read the display from an SSH session; run it inside the user's GUI launchd domain instead.
set -e
NAME="${1:-shot}"
OUT="/tmp/$NAME.png"
UID_=$(id -u)
rm -f "$OUT"
launchctl bootout "gui/$UID_/tokendial.shot" 2>/dev/null || true
cat > /tmp/tokendial-shot.plist <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>Label</key><string>tokendial.shot</string>
  <key>ProgramArguments</key><array><string>/usr/sbin/screencapture</string><string>-x</string><string>$OUT</string></array>
  <key>RunAtLoad</key><true/>
</dict></plist>
PLIST
launchctl bootstrap "gui/$UID_" /tmp/tokendial-shot.plist
for i in $(seq 1 20); do
  sleep 0.5
  [ -s "$OUT" ] && break
done
launchctl bootout "gui/$UID_/tokendial.shot" 2>/dev/null || true
[ -s "$OUT" ] && echo "$OUT" || { echo "no screenshot produced"; exit 1; }
