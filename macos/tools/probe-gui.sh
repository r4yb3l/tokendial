#!/bin/zsh
# Runs tokendial-probe inside the user's GUI launchd domain, where the login keychain is reachable,
# and prints its output. SSH sessions cannot read the keychain themselves.
set -e
export PATH=/opt/homebrew/bin:/usr/local/bin:$PATH
cd "$(dirname "$0")/../TokendialCore"
swift build -c release --product tokendial-probe 2>&1 | grep -E 'error' || true
BIN="$(pwd)/.build/release/tokendial-probe"
UID_=$(id -u)
launchctl bootout "gui/$UID_/tokendial.probe" 2>/dev/null || true
rm -f /tmp/probe.out /tmp/probe.err
cat > /tmp/probe.plist <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>Label</key><string>tokendial.probe</string>
  <key>ProgramArguments</key><array><string>$BIN</string></array>
  <key>RunAtLoad</key><true/>
  <key>StandardOutPath</key><string>/tmp/probe.out</string>
  <key>StandardErrorPath</key><string>/tmp/probe.err</string>
</dict></plist>
PLIST
launchctl bootstrap "gui/$UID_" /tmp/probe.plist
for i in $(seq 1 45); do
  sleep 2
  grep -q 'Claude sessions' /tmp/probe.out 2>/dev/null && break
done
cat /tmp/probe.out 2>/dev/null
tail -3 /tmp/probe.err 2>/dev/null
if grep -q 'Claude sessions' /tmp/probe.out 2>/dev/null; then
  launchctl bootout "gui/$UID_/tokendial.probe" 2>/dev/null || true
else
  echo "probe still running (pid $(launchctl print gui/$UID_/tokendial.probe 2>/dev/null | awk '/pid =/ {print $3}')); look for a Keychain dialog on the Mac"
fi
