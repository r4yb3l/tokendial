# Linux support

Tokendial's Linux app uses Avalonia over the shared C# Core. It runs in an Xorg/X11 desktop session.

## Dock position and display

Settings → Position selects Top, Bottom, Left or Right. The choice applies immediately and is saved for the
next launch. Top and bottom carry a row of upright dials; left and right carry a column. When the expanded
dock would run off the work area it wraps: rows along the top or bottom, columns down a side, dealt in order
the way the Windows dock deals them. Hover cards open toward the inside of the screen.

Settings → Display selects a monitor by its RandR name. Automatic follows the primary monitor. If the saved
monitor disappears, the dock moves to the primary one and the saved name is kept; when that monitor returns,
the dock returns to it without being asked. While the saved monitor is absent, Settings says so and still
offers Automatic, and the list refreshes while Settings is open.

All placement, input regions and pointer arithmetic are in physical pixels at the window's render scale,
and are recomputed when monitors, work areas or scale change. The log is at
`~/.config/Tokendial/logs/tokendial.log` unless `XDG_CONFIG_HOME` says otherwise; every placement writes a
`dock placement:` line naming the saved and chosen monitor, edge, scale and pixel rectangle.

### Scale

Avalonia's X11 backend takes one scale for every monitor from `Xft.dpi`. Monitors get different scales only
through `AVALONIA_SCREEN_SCALE_FACTORS` (for example `eDP-1=2;HDMI-1=1`), `QT_SCREEN_SCALE_FACTORS` (read
the same way; KDE Plasma's X11 session is expected to set it from its display scaling, not verified here), or
`AVALONIA_USE_PHYSICAL_DPI=1`.

With per-monitor scales, Avalonia 12.1.2 gives a window the scale of the *lowest-scale* monitor that
contains its top-left corner, counting a monitor's right and bottom edges as inside it. A left dock on a
monitor to the right of a lower-scale one starts exactly on that shared edge and would render at the
neighbour's scale. The dock is placed one pixel off such an edge
(`DockGeometry.ClearOfLowerScaleNeighbour`). Nothing else is offset.

When the dock changes monitor, its first placement uses the scale of the monitor it is leaving, and the
window re-places itself once Avalonia reports the new scale, one dispatcher pass later. The log shows both
lines.

## Wayland

Wayland sessions, including X11 apps running through XWayland, are refused before providers, credential
discovery or the installer start. If an X display is reachable, a small window explains why; otherwise the
explanation goes to standard error and the exit status is 1.

This is a judgement call, and here is the reasoning so the next person can overturn it on evidence.

**Why not run degraded under XWayland.** The dock's hot zone sits outside its input region, so clicks there
reach the desktop, and it notices the pointer by polling the global pointer position. Wayland does not have
a global pointer position: `wl_pointer.motion` is a "notification of pointer location change" whose
coordinates are "relative to the focused surface". XWayland is a Wayland client, so the X server it runs
knows where the pointer is only while the pointer is over one of its own windows. From that, the expected
result under XWayland is a dock that does not open from its hot zone and can stay open after the pointer
leaves. That is derived from the protocol, not observed: this has not been run on a Wayland compositor. A
dock that works some of the time is the control that lies, so it is refused rather than shipped in that
state.

**What real Wayland support takes.** The protocol built for docks is
[wlr-layer-shell](https://wayland.app/protocols/wlr-layer-shell-unstable-v1). It lets a surface anchor to an
output's edge and receive pointer enter and leave on its own surface. According to wayland.app's compositor
table (read 2026-09-12), KWin, Muffin (Cinnamon's Wayland session), Sway, Hyprland, COSMIC, labwc, niri and
Wayfire implement it. **Mutter, which is GNOME, and Weston do not.** GNOME is the default desktop on Ubuntu
and Fedora, so a layer-shell backend would still leave a large share of Wayland users out; reaching them
means a GNOME Shell extension. Avalonia 12 ships no Wayland backend at all. So the work is a
layer-shell surface for the dock, input handled on that surface instead of a global poll, and a separate
answer for GNOME. It is a project, not a flag.

**When to revisit.** When Avalonia gains a Wayland backend that can create layer-shell surfaces, or when
someone owns a GNOME Shell extension. Until then, the refusal names the fix a user can apply today, an
Xorg session.

## Checks

From the repository root:

```sh
dotnet test windows/Tokendial.Tests/Tokendial.Tests.csproj --configuration Release
dotnet test linux/Tokendial.Linux.Tests/Tokendial.Linux.Tests.csproj --configuration Release
```

The Linux tests run headless: geometry on all four edges at scales 1, 1.25 and 2, negative origins,
wrapping, zero and nine providers, fallback snapshots, the shared-edge scale rule, the chooser, the card and
the session policy. They set up their own Avalonia platform, so they **cannot** see a start that fails in
`AppBuilder.Setup`. CI therefore also opens the display probe under Xvfb on all four edges.

### The display probe

`tools/LinuxDisplayProbe` opens the real dock with nine fixture providers. It reads no credentials, polls no
provider, loads or saves no settings, installs nothing and does not replace a running Tokendial. It prints
the dock's X window id, a JSON snapshot of monitors and scale whenever they change, and the dock's own
placement log, then exits after the requested time.

```sh
dotnet build tools/LinuxDisplayProbe --configuration Release
tools/LinuxDisplayProbe/bin/Release/net10.0/LinuxDisplayProbe Right auto expanded 20
xwininfo -id <window id>    # the server's rectangle; Avalonia's Position getter is not evidence on X11
```

Arguments: edge, monitor name or `auto`, `compact` or `expanded`, seconds (1–3600).

### Nested checks

`tools/LinuxDisplayProbe/nested-checks.sh` runs pass/fail checks on a nested Xephyr server. Nothing on your
own desktop is reconfigured and the pointer only moves inside the nested window. Needs `Xephyr`, `xrandr`,
`xwininfo` and `python3`.

```sh
tools/LinuxDisplayProbe/nested-checks.sh hotplug Left 1 1.25   # edge, primary scale, external scale
tools/LinuxDisplayProbe/nested-checks.sh card 2                 # scale, optionally a list of edges
tools/LinuxDisplayProbe/nested-checks.sh notice
```

- **hotplug**: a primary monitor and an external one at another scale. The dock goes to the external one,
  which is removed and re-added. At each step the dock must be inside the right monitor and render at that
  monitor's scale.
- **card**: hovers a cell on each edge. The card must open inward, beside the hovered cell, without
  covering the capsule and inside the monitor.
- **notice**: starts the real app as if under XWayland. It must show its explanation and write nothing to
  its configuration or data directories.

These are simulations, and the script's header says of what: monitors are declared with `xrandr
--setmonitor` and scales with `AVALONIA_SCREEN_SCALE_FACTORS`. `xrandr --delmonitor` emits no RandR event by
itself (checked with `xev -root -event randr`), so each change is followed by re-asserting the primary
output, which does. The geometry the server reports back is real.

## Verification record

2026-09-12, @M4ss1ck's machine: Cinnamon 6.6.9 on Xorg, two 1920×1080 monitors at 96 DPI (`DisplayPort-0`
primary at 0,0, `DisplayPort-1` at 1920,0).

| Check | How | Result |
|---|---|---|
| Core suite | `dotnet test`, Release | 265 passed |
| Linux suite | `dotnet test`, Release | 75 passed |
| Four edges, compact and expanded, on `DisplayPort-1` | probe on the real desktop, `xwininfo` | all 8 server rectangles equal the logged placement; window type `_NET_WM_WINDOW_TYPE_DOCK`, state includes `_NET_WM_STATE_ABOVE`; nine providers wrap to 8+1 columns at the sides |
| App starts | probe on the real desktop | the builder change had broken every start; fixed and seen starting |
| Unplug and replug the chosen monitor | nested `hotplug`: Right 1/2, Top 2/1, Left 1/1.25, Bottom 1.5/1 | falls back to primary, returns on its own, renders at each monitor's scale |
| Hover card, all edges | nested `card` at 1 and 2 | opens inward beside the hovered cell, clear of the capsule, on the monitor |
| Wayland refusal | nested `notice` | explanation window maps, configuration directory untouched |

Not verified, and what would settle each:

- **Physical unplug.** A real driver's RandR events have not been seen reaching the dock. Settle it by
  choosing an external monitor, pulling its cable while the probe runs, and reading the `dock placement:`
  lines.
- **Physical mixed-DPI panels**, and KDE's X11 session setting `QT_SCREEN_SCALE_FACTORS` for real. Settle it on
  a HiDPI laptop with a 1x external monitor under Plasma X11, all four edges, cards included.
- **Hover on the real desktop.** Not done, because it moves the pointer of the person using the machine. The
  nested `card` check covers the geometry; a person hovering each edge once covers the rest.
- **Any Wayland compositor.** None is installed here. The XWayland behaviour described above is derived from
  the protocol.
