# Lessons

- **Text next to an edge needs slack, not a computed width.** A StackPanel given `CardWidth - 2*Padding` ignored the 1 px border and clipped the last glyph of every right-aligned line ("Resets Tue 12:16 AM" lost its "M"). Let the body stretch inside the border and give right-aligned text a small margin; never hand-compute an inner width that the container already knows. Raised by the user on 2026-09-07 ("separa los límites del borde para que no se coma caracteres").
- **Fixture expectations are derived, not typed.** Five fixtures carried `resetsAt` values produced by a buggy epoch conversion; the C# parser was right and the spec was wrong. Generate expected values from the raw response fields with one script, and keep that script.
- **Do not move the user's cursor while they are working.** Synthetic hover tests fought a live desktop and produced noise. Prove hover with logs; take one screenshot when a cursor move is unavoidable.
- **RTL captures and percent signs.** `PrintWindow` on a WPF window in right-to-left mode returns mirrored glyphs; capture the screen region instead. In Arabic a percentage renders as "%80" by design of the bidi algorithm, and WPF ignores Unicode isolates (U+2066–2069), so do not fight it: keep numeric dial badges LTR and let prose follow the language.

## Never press the user's confirmation buttons for them (2026-09-08)
While verifying the install assistant I drove the settings window with UI Automation and invoked the sheet's
"Run" button myself, which installed OpenCode through winget on the user's machine. The user never accepted it.
Rule: anything that installs software, signs in, or changes the machine goes through a click the user makes.
I may open the sheet, capture it, and read the result; the Run/Confirm button is theirs. Same for any dialog
whose whole purpose is consent. If a live test needs that click, ask and wait.

## Look at the foreground window before sending any input (2026-09-08)
A mouse-wheel scroll meant for the settings window landed in a full-screen game the user was playing: the
Debug app sat behind it. Synthetic input goes to whatever has focus, not to the window I think I am driving.
Rule: before SetCursorPos, mouse_event or keyboard input, read the foreground window; if it is not Tokendial,
stop and verify another way (UI Automation tree, logs, PrintWindow). Prefer those three always; input is the
last resort and only when the machine is visibly idle.

## Parity between platforms includes the visual layer (2026-09-09)
Asked to plan bringing macOS up to parity with Windows, I planned only the four functional features
(Gemini, forecast, Codex profiles, updater) and left out the entire design layer: the light/dark themes
and their switch, the redesigned settings and welcome windows, the four dock positions. The user caught
it immediately — the running Mac app is stock SwiftUI with a system-blue button, nothing like the Windows
redesign. The visual layer was ~1,520 lines on Windows against 1,796 for the whole macOS app target, so it
was the biggest part of the gap, not a detail.
Rule: when planning parity, diff the UI layer too, not just behaviour. Read the other platform's design
files and the shared spec (`docs/design/tokens.md`) and list what the lagging platform does not have.
A second reason to do design first: rows that live in a window being rebuilt (a new provider row, a new
settings toggle) would otherwise be built twice.

## Shared design tokens are not literally portable across platforms (2026-09-09)
The macOS dock reused the Windows surface opacity from `docs/design/tokens.md` (0.50 dark, 0.80 light) and
the user could barely see it: "la pantalla retina castiga la transparencia y casi no se ve nada contra el
fondo". The cause is that the macOS dock paints one flat translucent colour into a CAShapeLayer with no
backdrop blur behind it, while the Windows panel sits on an acrylic backdrop that does the separating work.
Same alpha, very different result, and a Retina screen shows every pixel of the wallpaper through it.
Rule: a token that describes a *material* (translucency, blur, elevation, shadow) has to be re-checked
against a screenshot on each platform, not copied. Copy the metrics and the hues; verify the materials.
Related: the hover card looked translucent for a different reason worth remembering — its fade-in used
`panel.animator().alphaValue = 1` and a concurrent animation group from `layout` interrupted it, freezing
the window near 0.5. An implicit alpha animation that matters needs a completion handler that sets the
final value.
