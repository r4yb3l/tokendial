#!/usr/bin/env bash
# The tray must survive its StatusNotifier watcher dying mid-registration. On 2026-10-07 Cinnamon's
# xapp-sn-watcher segfaulted while Tokendial registered its icon, Avalonia 12.1.2 raised the NoReply from an
# async void, and the runtime aborted Tokendial (SIGABRT). This replays that on a private session bus with a
# watcher that dies on purpose, so the desktop's own tray is never touched.
#
#   tools/LinuxDisplayProbe/tray-watcher-check.sh            # on your X display; nothing is shown on it
#   xvfb-run -a tools/LinuxDisplayProbe/tray-watcher-check.sh
#
# Prints PASS or FAIL. The check only counts if the watcher actually received the registration call.
set -u
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
PROBE="$ROOT/tools/LinuxDisplayProbe/bin/Release/net10.0/LinuxDisplayProbe"
OUT=${OUT:-/tmp/tokendial-tray-watcher-check}
mkdir -p "$OUT"

[ -n "${DISPLAY:-}" ] || { echo "needs an X display (DISPLAY is empty)"; exit 2; }
command -v dbus-run-session >/dev/null || { echo "missing dbus-run-session"; exit 2; }
dotnet build "$ROOT/tools/LinuxDisplayProbe" --configuration Release -v q -nologo >"$OUT/build.log" 2>&1 \
  || { cat "$OUT/build.log"; exit 2; }

# A bus with no service directories: nothing on it can be auto-started, so the desktop's portals, gvfs and
# accessibility daemons are not spawned against this throwaway bus. The policy is session.conf's own.
cat >"$OUT/bus.conf" <<'CONF'
<busconfig>
  <type>session</type>
  <listen>unix:tmpdir=/tmp</listen>
  <auth>EXTERNAL</auth>
  <policy context="default"><allow send_destination="*" eavesdrop="true"/><allow eavesdrop="true"/><allow own="*"/></policy>
</busconfig>
CONF
rm -f "$OUT/watcher.log" "$OUT/tray.log" "$OUT/tray.exit"

XDG_SESSION_TYPE=x11 WAYLAND_DISPLAY='' dbus-run-session --config-file="$OUT/bus.conf" -- bash -c '
  "$0" dying-watcher >"$1/watcher.log" 2>&1 &
  watcher=$!
  trap "kill $watcher 2>/dev/null" EXIT
  for _ in $(seq 100); do grep -q "^watcher: owns" "$1/watcher.log" && break; sleep 0.1; done
  grep -q "^watcher: owns" "$1/watcher.log" || exit 0
  "$0" tray 6 >"$1/tray.log" 2>&1
  echo $? >"$1/tray.exit"
' "$PROBE" "$OUT" 2>"$OUT/bus.log"

code=$(cat "$OUT/tray.exit" 2>/dev/null || echo none)
if ! grep -q "^watcher: owns" "$OUT/watcher.log" 2>/dev/null; then
  echo "FAIL the stand-in watcher never took its name, so nothing was tested"; cat "$OUT/watcher.log" "$OUT/bus.log" 2>/dev/null; exit 1
elif ! grep -q "got RegisterStatusNotifierItem" "$OUT/watcher.log"; then
  echo "FAIL the tray never tried to register, so the check proves nothing"; cat "$OUT/watcher.log" "$OUT/tray.log"; exit 1
elif [ "$code" != 0 ]; then
  echo "FAIL the tray exited $code when its watcher died"; cat "$OUT/tray.log"; exit 1
fi
echo "PASS the tray outlived a watcher that died mid-registration (artifacts: $OUT)"
