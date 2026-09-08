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

Settings windows: window #F4F6FA, cards white at 60–95 %, edges #D9DFE8, lines #C5CEDA, inks
#0F172A / #1E293B / #334155 / #64748B / #94A3B8; the brand green and its tints do not change.

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
