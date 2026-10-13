# Design tokens

Tokendial is a set of dials, not rings. Every number here is in device-independent
points; both platforms use them verbatim.

## Colour

| Token | Value | Use |
|---|---|---|
| `surface` | rgba(16, 17, 20, 0.50) over system blur (vibrancy on macOS, acrylic on Windows) | capsule and card background |
| `surfaceEdge` | rgba(255, 255, 255, 0.08) | 1 px inner hairline on the capsule |
| `track` | rgba(255, 255, 255, 0.16) | dial track and bar track |
| `ample` | #34D399 | < 50 % used |
| `watch` | #FBBF24 | 50–79 % |
| `critical` | #FB7185 | ≥ 80 %, and any limit reached |
| `working` | #F5F5F7 | the activity indicator while an agent works |
| `waiting` | #FBBF24 | the activity indicator while an agent waits on you |
| `textPrimary` | #F5F5F7 | |
| `textSecondary` | #9A9DA6 | |
| `textDisabled` | #5C5F68 | providers without a reading |

Bands: `ample` below 0.50, `watch` from 0.50, `critical` from 0.80 (a step earlier
than the usual 0.70, because the 80 % alert and the colour change should coincide).

The alphas above assume a blurred backdrop. macOS has none: the dock is a trapezoid
path filled with one flat colour, and on a Retina screen the wallpaper reads straight
through it. So macOS raises the surfaces instead of matching Windows — `surface` 0.86
dark and 0.95 light, `cardSurface` 0.97 and 0.99, `surfaceEdge` 0.14 and 0.16. Keep the
hues and every metric identical; the opacity is the one deliberate difference.

### Light look

The table above is the dark look, the default when Windows apps are dark. A Theme setting
(system / dark / light) picks the other one; both platforms carry both. The light values were
derived from the dark mockup and are open to a design pass.

| Token | Light value |
|---|---|
| `surface` | rgba(248, 249, 251, 0.80) |
| `surfaceEdge` | rgba(15, 23, 42, 0.10) |
| `track` | rgba(15, 23, 42, 0.12) |
| `ample` / `watch` / `critical` | #10B981 / #F59E0B / #F43F5E (one step darker for contrast on white) |
| `working` | #1B1F27 |
| `textPrimary` / `textSecondary` / `textDisabled` | #1B1F27 / #5B6270 / #A3A9B4 |

Settings windows: window #E9EDF3, cards white at 60–95 %, edges #D2DAE5, lines #C5CEDA, inks
#0F172A / #1E293B / #334155 / #475569 / #64748B; the brand green and its tints do not change.
The two quietest inks are one step darker than a plain inversion of the dark ramp would give:
at 11 px on a white card, #64748B reached only 4.75:1 and #94A3B8 failed outright, where
#475569 reads at 7.5:1. On macOS the settings window also wears the theme's own appearance, so
what the system draws for us — the selection behind a field's text, the caret, the focus ring —
cannot come back in dark-mode colours on a light window. The window ground started at #F4F6FA and
was deepened to #E9EDF3 (edges with it): three per cent from a white card is not a separation, and
the whole light window read as one flat sheet.

## Settings controls

The settings window is a list on the left and controls on the right, over a footer bar carrying the
version, the licence line and the three links.

Each provider is a row of one list card, not a card of its own: the rows are divided by hairlines,
the connected ones are washed with the brand at 10 % and the card clips its own rounded corners.
The row leads with **the dock's own dial** — the same 240° arc opening downward, 34 px at 3 px
stroke, coloured by band through the usage bands, with the tool's mark inside — so a glance means
the same thing in the dock and in the settings list. There is no separate progress bar: the dial is
the reading. The row ends with a switch for whether Tokendial reads that tool at all, under a small
right-aligned `Read` caption over the column, because a switch with no label beside a `Manage`
button does not say what it governs. One `Refresh` under the list replaces a refresh button per row.

Panel mode and dock position are picture-first tiles: a small screen with what the choice puts at
the edge (a compact row of cells, every cell expanded, a menu-bar dot; or the dock's bar against
one of the four edges), with the name under it, and one hint line under the row that follows the
selection instead of three repeated hints.

Flags for the language picker are drawn from bands and crosses, never emoji: Segoe UI Emoji carries
no regional indicator glyphs, so a flag emoji renders on Windows as its two letters in boxes, and
drawing them keeps the platforms identical with no assets. Arabic is a language, not a country, so
it carries ع on a plain badge rather than any one state's flag.

## Dial

An arc of **240°** opening downward, from 150° to 30° (clockwise, 0° at 3 o'clock).
The progress arc runs from the left end; the track shows the rest. No needle: the
arc's end is the reading.

| Token | Compact | Expanded |
|---|---|---|
| diameter | 24 | 56 |
| stroke | 3 | 6 |
| label | none | percent, 15 pt semibold, centred inside the opening |
| glyph | 11 pt provider mark centred in the dial | 16 pt provider mark above the label |
| activity arc | none | 40 pt diameter, 2 pt stroke, inside the dial |

## Dock (screen edge)

The dock hangs from one of the four screen edges (Position setting: top, bottom, left, right). Top and bottom are a row; left and right are a column of the same tiles, 136 pt across when expanded, with the sessions line under the cells. The trapezoid, hot zone, expansion and hover card all face the screen interior. The details below are written for the top edge.

### Top edge

The panel is not a floating pill: it hangs from the screen edge as a trapezoid. The top
spans the full width flush with the edge and carries no outline; the sides slant inward
by `slant` over the height; the bottom is straight with rounded corners. Content sits
inside the bottom width, so the slant is extra room on each side.

| Token | Value |
|---|---|
| slant | 14 |
| bottom corner radius | 17 compact, 20 expanded |
| edge hairline | sides and bottom only |

## Capsule metrics

| Token | Value |
|---|---|
| compact height | 34 |
| compact horizontal padding | 14 |
| compact dial spacing | 10 |
| expanded height | 132 |
| expanded horizontal padding | 20 |
| expanded cell width | 96 |
| hot zone below the capsule | 24 |
| distance below a hardware notch | 0 (the capsule shares its bottom edge) |

Compact shows one 24 pt dial per connected provider with its mark inside; a provider
without a reading shows a hollow track in `textDisabled`. Expanded shows one cell
per provider: dial, provider name, headline window label, and a thin 3 pt bar per
secondary window. Live sessions appear as a one-line list under the cells, with
the activity arc in the dial.

## Card (hover detail)

Width 280, corner 16, padding 14, body 12 pt, title 14 pt semibold. Rows: window
label · reset copy; a 4 pt bar; "N% used · M% left". Sessions under a hairline.

## Floating face

One face for the whole install, in a window the user drags anywhere (off by default).
It is the dial's 240° arc around a dark disc, with a face typed in glyphs inside. It is
always drawn in the **dark** colours above, like a device's screen, so it reads the
same over any window and in either theme. No gradients, sheens or glows.

| Token | Value |
|---|---|
| window | 92 × 92: the 72 disc plus a 10 transparent inset to grab |
| disc | 72 diameter, `faceGround` #101114 opaque (the dark card's hue, so nothing shows through), 1 pt `surfaceEdge` |
| arc | 240°, stroke 0.085 × diameter, centre-line 0.6 × stroke inside the disc edge; `track` dark, fill in the band colour |
| glyphs | the platform monospace, bold: Cascadia Mono on Windows, SF Mono on macOS, DejaVu Sans Mono (or Noto Sans Mono) on Linux |
| eyes | 0.25 × diameter, centred at 0.43 of the height; `textPrimary`, `textDisabled` when spent |
| mouth | 0.22 × diameter, centred at 0.65; `waiting` while an agent waits, otherwise as the eyes |

**What it reads.** The arc takes the one provider closest to running out — highest
band, then fullest — and both its colour and its fill come from that provider. A
blocked provider counts as full. Providers that need a sign-in or meter nothing are
left out. The mood is the strongest session state (waiting, then working, then idle).

| When | Eyes | Mouth |
|---|---|---|
| spent (fill 1, or blocked) | `x x` | `_` |
| an agent waits on you | `O O` | `o` |
| an agent works | `o o` | by band |
| critical | `> <` | `~` |
| watch | `- -` | `_` |
| ample, or nothing to read | `^ ^` | `◡` |

The first matching row wins, so a waiting agent shows over the band except when
spent. The rule lives in `Face` in each core and is covered by tests.

**Motion.** A blink every 5.2 s (eyes become `- -` for the last 3 % of the cycle;
none when spent). While an agent works the eyes glance ±0.05 × diameter on a 2.6 s
cycle, pausing at each end. While one waits the face bobs 0.04 × diameter on a 0.78 s
sine, there and back. The arc moves with the dial-reading spring. Reduce motion
stops all of it; the glyphs alone still say the same thing.

**Speech bubble.** While any agent waits, a bubble sits beside the face on whichever
side has room, top level with the face, its 8 pt tail pointing at it. It replaces the
waiting alert (toast or banner) while the face is on, and goes away by itself once
nobody waits. Same dark colours: `faceGround` with a 1 pt `surfaceEdge`, 12 corner,
14 × 12 padding, at most 300 wide and three sessions. Each row: "{name} is waiting for
you" 13 pt semibold, where it is 11.5 pt `textSecondary`, and a **Take me there**
button (`track` fill, `textDisabled` on hover, 8 corner). It brings forward the window
the agent runs inside — the first ancestor of its process that has a window,
stopping at the shell (Explorer, Finder, init on Linux) — not a tab within it. Once it has, that
session leaves the bubble, and the bubble closes when nobody else is in it; the
session comes back only if it stops waiting and later waits again. When none is found
the button says so instead of doing nothing.

## Type

System UI face: SF Pro on macOS, Segoe UI Variable on Windows. Sizes 11, 12, 14,
15 with the platform's default weights (regular, semibold). Numbers use tabular
figures so a changing percentage does not jitter.

## Motion

Springs, parameterised as (response s, damping): expand 0.38/0.80, contents
0.32/0.84, dial reading 0.80/0.90, card glide 0.45/0.86; crossfade 0.15 s;
per-cell stagger min(i·0.04, 0.16). Respect the system's reduce-motion setting by
using zero durations.

## Copy

Resets: "Resets in 51 min" under an hour (rounded, never "60 min"), "Resets Thu
12:00 AM" within a week, "Resets Sep 28" beyond it, "Resetting…" once passed.
Elapsed: "just now" under 45 s, "6 min", "1 hr", "1 hr 5 min"; "… ago" for ages.
Usage: "63% used · 37% left"; a derived count reads "~7 requests today".

Pace, on the headline window only, under its summary: "At this pace, empty at 4:40 PM" (amber; the weekday is added when that is another day) or "At this pace it lasts until the reset". Computed by least squares over the last hour of the current epoch, at least three samples spanning ten minutes; nothing is shown while a window rests (under half a point an hour), is full, or would only empty more than a day away.
