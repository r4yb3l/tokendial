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
