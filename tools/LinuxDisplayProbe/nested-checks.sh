#!/usr/bin/env bash
# Display checks on a nested X server (Xephyr), so nothing on the desktop you are using is reconfigured and
# the pointer only ever moves inside the nested window. Each check prints PASS or FAIL and the script exits
# non-zero if any failed.
#
#   tools/LinuxDisplayProbe/nested-checks.sh hotplug [edge] [primary-scale] [external-scale]
#   tools/LinuxDisplayProbe/nested-checks.sh card [scale] [edges...]
#   tools/LinuxDisplayProbe/nested-checks.sh notice
#
# hotplug  A primary monitor and an external one at another Avalonia scale. The dock is sent to the external
#          one, which is removed and re-added while the probe runs: the dock must land on the primary and
#          come back on its own.
# card     Hovers a cell on each edge and checks the card opens on the interior side, beside the hovered
#          cell, without covering the capsule and inside the monitor.
# notice   Starts the real app as if under XWayland: it must refuse, show its explanation window and create
#          nothing in its configuration directory.
#
# Needs Xephyr, xrandr, xwininfo and python3. Artifacts go to $OUT (default /tmp/tokendial-nested-checks).
#
# What this is not: physical hotplug or physical mixed-DPI panels. RandR monitors are declared with
# --setmonitor and scales with AVALONIA_SCREEN_SCALE_FACTORS. `xrandr --delmonitor` emits no RandR event on
# its own, so each topology change is followed by re-asserting the primary output, which does - the event a
# real unplug sends. The server's reply to every geometry query is real.
set -u
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
PROBE_DIR="$ROOT/tools/LinuxDisplayProbe"
PROBE="$PROBE_DIR/bin/Release/net10.0/LinuxDisplayProbe"
APP="$ROOT/linux/Tokendial.Avalonia/bin/Release/net10.0/Tokendial"
OUT=${OUT:-/tmp/tokendial-nested-checks}
D=${NESTED_DISPLAY:-:73}
mkdir -p "$OUT"
failures=0

for tool in Xephyr xrandr xwininfo python3; do
  command -v "$tool" >/dev/null || { echo "missing $tool"; exit 2; }
done

pass() { echo "PASS $*"; }
fail() { echo "FAIL $*"; failures=$((failures + 1)); }

start_server() {
  Xephyr "$D" -screen "$1" -resizeable -ac -br -noreset >"$OUT/xephyr.log" 2>&1 &
  XPID=$!
  for _ in $(seq 50); do xwininfo -display "$D" -root >/dev/null 2>&1 && return; sleep 0.1; done
  echo "Xephyr did not start on $D"; exit 2
}
stop_server() { kill "${XPID:-}" 2>/dev/null; wait "${XPID:-}" 2>/dev/null; }
trap stop_server EXIT

rr() { xrandr -d "$D" "$@" >/dev/null 2>&1; }
notify() { rr --noprimary; rr --output default --primary; }

# x y width height map-state
rect() { timeout 3 xwininfo -display "$D" -id "$1" | awk '/Absolute upper-left X/{x=$4}/Absolute upper-left Y/{y=$4}/Width:/{w=$2}/Height:/{h=$2}/Map State/{m=$3}END{print x, y, w, h, m}'; }

warp() {
  python3 - "$D" "$1" "$2" <<'PY'
import ctypes, sys
x = ctypes.cdll.LoadLibrary("libX11.so.6")
x.XOpenDisplay.restype = ctypes.c_void_p
x.XDefaultRootWindow.restype = ctypes.c_ulong
d = ctypes.c_void_p(x.XOpenDisplay(sys.argv[1].encode()))
x.XWarpPointer(d, 0, ctypes.c_ulong(x.XDefaultRootWindow(d)), 0, 0, 0, 0, int(sys.argv[2]), int(sys.argv[3]))
x.XFlush(d)
x.XCloseDisplay(d)
PY
}

wait_window() {
  for _ in $(seq 100); do
    id=$(grep -o '^window=0x[0-9a-f]*' "$1" | head -1 | cut -d= -f2)
    [ -n "$id" ] && { echo "$id"; return; }
    sleep 0.1
  done
}

build() {
  dotnet build "$PROBE_DIR" --configuration Release -v q -nologo >"$OUT/build.log" 2>&1 \
    || { cat "$OUT/build.log"; exit 2; }
}

hotplug() {
  local edge=${1:-Right} primary=${2:-1} external=${3:-2}
  local tag="hotplug-$edge-$primary-$external"
  start_server 3840x1080
  rr --setmonitor '*PRIMARY' 1920/500x1080/280+0+0 default
  rr --setmonitor EXT 1920/500x1080/280+1920+0 none
  DISPLAY=$D XDG_SESSION_TYPE=x11 WAYLAND_DISPLAY='' AVALONIA_SCREEN_SCALE_FACTORS="PRIMARY=$primary;EXT=$external" \
    "$PROBE" "$edge" EXT expanded 60 >"$OUT/$tag.log" 2>&1 &
  local probe=$! id
  id=$(wait_window "$OUT/$tag.log")
  [ -n "$id" ] || { fail "$tag: no dock window"; kill $probe; stop_server; return; }

  inside() { # label expected-left expected-right
    local x y w h
    read -r x y w h _ <<<"$(rect "$id")"
    if [ "$x" -ge "$2" ] && [ $((x + w)) -le "$3" ]; then pass "$tag: $1 at $x,$y ${w}x$h"
    else fail "$tag: $1 at $x,$y ${w}x$h, expected within x $2..$3"; fi
    # The probe's latest snapshot: the window must render at its monitor's scale, not a neighbour's.
    local scales
    scales=$(grep -E '^\S+ \{' "$OUT/$tag.log" | tail -1 | cut -d' ' -f2- \
      | python3 -c 'import sys,json; d=json.load(sys.stdin); print(d["renderScaling"], d["target"]["Scaling"])')
    read -r render wanted <<<"$scales"
    if [ "$render" = "$wanted" ]; then pass "$tag: $1 renders at $render"
    else fail "$tag: $1 renders at $render, its monitor is $wanted"; fi
  }
  sleep 3; inside "on EXT" 1920 3840
  xwininfo -display "$D" -root >/dev/null && DISPLAY=$D import -window root "$OUT/$tag-1.png" 2>/dev/null
  rr --delmonitor EXT; notify
  sleep 3; inside "EXT unplugged, on primary" 0 1920
  DISPLAY=$D import -window root "$OUT/$tag-2.png" 2>/dev/null
  rr --setmonitor EXT 1920/500x1080/280+1920+0 none; notify
  sleep 3; inside "EXT back, returned" 1920 3840
  DISPLAY=$D import -window root "$OUT/$tag-3.png" 2>/dev/null
  kill $probe 2>/dev/null; wait $probe 2>/dev/null
  stop_server
}

card() {
  local scale=${1:-1}; shift
  local edges=("$@"); [ ${#edges[@]} -gt 0 ] || edges=(Top Bottom Left Right)
  # Theme.cs: 24 DIP hot zone, 14 DIP slant, 20 DIP padding, 96 DIP cells with a 14 DIP gap. The pointer goes
  # to the centre of the second cell in the first row or column, well clear of the gaps between cells. A side
  # dock that wraps deals its first column nearest the screen's interior on both sides, as Windows does, so
  # on the right that column starts after the hot zone rather than at the screen edge.
  # 1600x900 keeps the nested window smaller than any monitor, so no window manager resizes it.
  local hot=$((24 * scale))
  for edge in "${edges[@]}"; do
    local tag="card-$edge-$scale"
    start_server 1600x900
    warp 800 450
    DISPLAY=$D XDG_SESSION_TYPE=x11 WAYLAND_DISPLAY='' AVALONIA_GLOBAL_SCALE_FACTOR=$scale \
      "$PROBE" "$edge" auto expanded 30 >"$OUT/$tag.log" 2>&1 &
    local probe=$! id
    id=$(wait_window "$OUT/$tag.log")
    [ -n "$id" ] || { fail "$tag: no dock window"; kill $probe; stop_server; continue; }
    sleep 2
    local dx dy dw dh px py
    read -r dx dy dw dh _ <<<"$(rect "$id")"
    case $edge in
      Top)    px=$((dx + dw / 2 - 96 * scale)); py=$((dy + 70 * scale)) ;;
      Bottom) px=$((dx + dw / 2 - 96 * scale)); py=$((dy + dh - 70 * scale)) ;;
      Left)   px=$((dx + 68 * scale)); py=$((dy + 192 * scale)) ;;
      Right)  px=$((dx + hot + 68 * scale)); py=$((dy + 192 * scale)) ;;
    esac
    warp $px $py
    sleep 1.5
    local found=""
    for w in $(xwininfo -display "$D" -root -children | awk '$1 ~ /^0x/{print $1}'); do
      [ "$w" = "$id" ] && continue
      local cx cy cw ch state
      read -r cx cy cw ch state <<<"$(rect "$w")"
      [ "$state" = IsViewable ] && [ "$cw" -gt 10 ] || continue
      found=1
      # The capsule is the window minus the hot zone on its interior side.
      local ok=1 why=""
      case $edge in
        Top)    [ "$cy" -ge $((dy + dh - hot)) ] || { ok=0; why="overlaps capsule"; }
                [ "$px" -ge "$cx" ] && [ "$px" -le $((cx + cw)) ] || { ok=0; why="not beside hovered cell"; } ;;
        Bottom) [ $((cy + ch)) -le $((dy + hot)) ] || { ok=0; why="overlaps capsule"; }
                [ "$px" -ge "$cx" ] && [ "$px" -le $((cx + cw)) ] || { ok=0; why="not beside hovered cell"; } ;;
        Left)   [ "$cx" -ge $((dx + dw - hot)) ] || { ok=0; why="overlaps capsule"; }
                [ "$py" -ge "$cy" ] && [ "$py" -le $((cy + ch)) ] || { ok=0; why="not beside hovered cell"; } ;;
        Right)  [ $((cx + cw)) -le $((dx + hot)) ] || { ok=0; why="overlaps capsule"; }
                [ "$py" -ge "$cy" ] && [ "$py" -le $((cy + ch)) ] || { ok=0; why="not beside hovered cell"; } ;;
      esac
      [ "$cx" -ge 0 ] && [ "$cy" -ge 0 ] && [ $((cx + cw)) -le 1600 ] && [ $((cy + ch)) -le 900 ] \
        || { ok=0; why="off the monitor"; }
      if [ $ok = 1 ]; then pass "$tag: card at $cx,$cy ${cw}x$ch, dock at $dx,$dy ${dw}x$dh"
      else fail "$tag: card at $cx,$cy ${cw}x$ch, dock at $dx,$dy ${dw}x$dh: $why"; fi
    done
    [ -n "$found" ] || fail "$tag: no card appeared for pointer at $px,$py"
    DISPLAY=$D import -window root "$OUT/$tag.png" 2>/dev/null
    kill $probe 2>/dev/null; wait $probe 2>/dev/null
    stop_server
  done
}

notice() {
  local tag=notice config
  dotnet build "$ROOT/linux/Tokendial.Avalonia" --configuration Release -v q -nologo >"$OUT/build-app.log" 2>&1 \
    || { cat "$OUT/build-app.log"; exit 2; }
  config=$(mktemp -d)
  # Fontconfig and Skia keep caches under the home directory; those are the toolkit's, not Tokendial's
  # settings, logs, install or autostart entries, so they get a directory of their own.
  local home
  home=$(mktemp -d)
  start_server 1280x800
  DISPLAY=$D XDG_SESSION_TYPE=wayland WAYLAND_DISPLAY=wayland-0 XDG_CONFIG_HOME="$config" \
    XDG_DATA_HOME="$config" HOME="$home" \
    "$APP" >"$OUT/$tag.out" 2>"$OUT/$tag.err" &
  local app=$! mapped=""
  for _ in $(seq 60); do
    mapped=$(xwininfo -display "$D" -root -children | awk '/"Tokendial"/{print $1}' | while read -r w; do
      read -r _ _ w2 _ state <<<"$(rect "$w")"; [ "$state" = IsViewable ] && [ "$w2" -gt 100 ] && echo "$w"; done | head -1)
    [ -n "$mapped" ] && break
    sleep 0.25
  done
  DISPLAY=$D import -window root "$OUT/$tag.png" 2>/dev/null
  if [ -n "$mapped" ]; then pass "$tag: explanation window $mapped mapped"; else fail "$tag: no explanation window"; fi
  kill $app 2>/dev/null; wait $app 2>/dev/null
  grep -q "Unhandled exception" "$OUT/$tag.err" && fail "$tag: crashed: $(head -1 "$OUT/$tag.err")"
  if [ -z "$(ls -A "$config")" ]; then pass "$tag: configuration directory untouched"
  else fail "$tag: wrote $(ls -A "$config")"; fi
  rm -rf "$config"
  stop_server
}

case ${1:-} in
  hotplug) shift; build; hotplug "$@" ;;
  card) shift; build; card "$@" ;;
  notice) notice ;;
  *) sed -n '2,20p' "$0"; exit 2 ;;
esac

echo "artifacts: $OUT"
[ $failures = 0 ]
