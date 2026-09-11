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

## An audit of string keys does not prove the strings are translated (2026-09-09)
Told the translations were "bastante rotas", I audited every `Strings.t` call against the catalogue, got
zero missing keys, and reported the i18n healthy. Both real faults were invisible to that audit: whole
areas never called `Strings.t` at all (the alerts composed English by hand), and the menu bar item resolved
its titles in its initialiser, which runs as a stored property of the app delegate — before
`applicationDidFinishLaunching` loads the catalogue — so it showed `tray.show`, `tray.refresh` verbatim.
Rule: audit from the user's side, not the code's. Run the app in a non-English language and read every
surface, and check *when* each string is resolved, not only whether its key exists. A key that resolves
before the catalogue loads is as broken as a missing one.

## Report the binary that is running, not the source you just fixed (2026-09-09)
I reverted a temporary "force Spanish" hack, rebuilt, ran the tests and reported the language work as
finished. The Mac was still running the previous build, the one pinned to Arabic, and the user found the
whole app in Arabic: "lo dejaste en arabe cacho e cabron, no entiendo nada". Reverting and compiling are not
the same as relaunching, and `mac.ps1 build` does not replace a running app.
Rule: a temporary change made for verification is only undone once the thing the user can see is back to
normal. Revert, rebuild, **relaunch**, and look at it again before saying it is done.

## A refused permission is not a missing credential (2026-09-09)
Backing off after a denied keychain read, I mapped the refusal to `needsSignIn`. The user then saw "Claude
Code · sin sesión iniciada" for a tool that was signed in, and would have been sent to run a login that
could not have helped. The log said it plainly: `the keychain read was refused` followed by
`UsageError(kind: needsSignIn)`.
Rule: never fold an infrastructure refusal into a state that blames the user or their setup. Give it its
own error, name the real cause in the copy, and say what the access was for — a permission prompt with no
explanation reads like an app helping itself to credentials.

## Rebuilding a screen from the catalogue drops the copy that was only in the code (2026-09-09)
The old macOS welcome window had a hardcoded English sentence telling the user that macOS would ask once
before Tokendial could read a tool's keychain item, and to choose Always Allow. Rebuilding the window from
the shared catalogue silently lost it, because the catalogue never had that key: it was Windows copy plus
one macOS-only sentence living in the source.
Rule: when replacing hardcoded strings with catalogue keys, diff the old literals against the new keys and
account for every sentence that has no key yet. A missing key is visible; a dropped sentence is not.

## A duplicate JSON key parses differently in every language (2026-09-09)
Editing a provider spec by text, I inserted `"requires": ["brew"]` into a block that already carried an
empty `"requires": []`. The file then had the key twice. Python keeps the last occurrence, so the script
that wrote the change verified itself and reported success; ajv validated the file happily; Foundation keeps
the *first*, so the Swift side read an empty list and only the new test caught it, two steps later.
Rule: when editing JSON by string surgery, re-read the file with a parser that rejects or reports duplicate
keys (`json.loads(..., object_pairs_hook=...)`), not with the default one that silently picks a winner. And
prefer inserting a key only after proving it is absent from *that* object — not from the file, which may
hold several objects with the same shape.

## Never rewrite a hand-formatted file through a serialiser (2026-09-10)
To add one key to the five catalogues I parsed each one with `json.loads` and wrote it back with
`json.dumps(indent=2)`. Every file came back reformatted: the blank lines that group the keys by screen were
gone and the one-line plural objects were exploded over four lines each, so a two-line change arrived as a
133-line diff over work that was deliberately laid out by hand.
Rule: a serialiser is for reading, not for writing back. To add or change a key in a file a human formatted,
insert the text next to its neighbour and keep the surrounding style, then parse the result only to check it
still loads and the value landed.

## The Bash tool loses CRLF, so anchors built with \r\n stop matching (2026-09-10)
`SettingsWindow.cs` was CRLF and `Chrome.cs` LF in the same folder, so my patch scripts built their anchors
with `\r\n`. One `sed -i` on the CRLF file rewrote it as LF, and every later anchor missed - the script
aborted mid-run, having already written its other file.
Rule: read `.gitattributes` first. This repo declares `* text=auto eol=lf`, so LF is the intended ending and
a CRLF working copy is the accident; normalise the file once and write anchors in LF. And make each patch
script write nothing until every anchor has been found.

## Opening a window to look at it: signal the running instance, do not send input (2026-09-10)
On Windows, `Tokendial.exe --settings` from cold does not open the settings window; it starts the app, and
the flag only means anything to an instance that is already up, so the second launch is what opens it. On
macOS the same job is `open tokendial://settings`, which the app already handles. And a posted
`WM_MOUSEWHEEL` does not scroll a WPF `ScrollViewer` - to see the foot of a column, drive the window through
UI Automation's `ScrollPattern` instead of faking a mouse.
Rule: to inspect a running window, use the app's own entry points - a second launch with the flag, its URL
scheme, or UI Automation. Synthetic global input is both unreliable and not mine to send.

## A runner builds for the machine it is, not for the machines you ship to (2026-09-10)
The macOS release job archived on a `macos-15` runner, which is Apple Silicon, and the archive took the
active architecture alone: v0.1.0 went out with a thin arm64 binary that an Intel Mac cannot open. The
signal was right in front of me - the dmg the runner produced was 1.07 MB where the same app built on the
Mac an hour earlier was 1.9 MB - and I read it as "the runner strips better" instead of "half the binary is
missing". I only caught it after publishing, by reading the Mach-O magic out of the released asset.
Rule: a release artifact is not verified by the build going green. Assert the properties the download must
have, in the workflow, next to where it is produced: `lipo -archs` must name every architecture, the
signature must be the one intended, the version inside must match the tag. And when a size changes by half,
that is the finding - chase it before shipping, not after.

## Verify CI is green the same way you verify anything else (2026-09-10)
I pushed six commits over a session believing CI was unverifiable from here, because `gh` was unauthenticated
and the repository was private. The moment it went public the API showed CI had been failing on every run
since 8 September - the macOS job asked `xcodebuild` for a test action on a scheme with no testable, which
could never have passed. Nothing I pushed broke it, and nothing I pushed would have told me either.
Rule: an unauthenticated `curl` of `api.github.com/repos/<repo>/actions/runs` answers "is CI green" for any
public repository, and a red pipeline is a finding to report even when it predates the work. Do not treat a
missing tool as a missing answer.

## Never type into a surface you have not proved is the target (2026-09-10)
Asked to open an editor in a pane, I split a new pane, called `focus-pane` on it, and sent the command.
`focus-pane` answered `ok`, `send` answered `ok`, and the keystrokes landed in the Claude Code terminal
instead - my command appeared inside the user's own prompt box and came back to me as if they had typed it.
`send` writes to the active surface, `focus-pane` had not made the new pane active, and neither call said
so. `send-key` does not even accept `--surface`, which was the clue that surface-addressed input is not
supported here.
Rule: opening a pane is mine to do; putting characters in it is not. Create the surface, then hand the user
the command to paste. If input ever has to be driven, read the screen of the intended surface first and
confirm it is what you think it is - an `ok` from the focus call is not that confirmation.

## A fix that is committed is not a fix that shipped (2026-09-11)
I fixed the installer collision, committed it, pushed it, said it was resolved, and moved on to the next
task. The tag was never created, so for nineteen hours the only thing a visitor could download was the
broken v0.1.0 - and the user found that out by looking at the releases page themselves and asking me
whether I had published it. The work was real; the delivery was not, and I had already used the word done.
Rule: for anything a user downloads, the definition of done is the published artifact, not the commit.
Finish the chain - tag, watch the release, fetch what was published and open it - or say plainly that it is
committed but unreleased. Never let "fixed" stand for "fixed and available".

## The window is the installer (2026-09-11)
I produced a disk image, confirmed the app and the `/Applications` symlink were both inside, and presented
it. The user's reply: "that is a folder open in the Finder". They were right. A disk image that people
recognise as an installer has no toolbar or sidebar, large icons, the app on the left, the link on the
right and an arrow between them - none of which lives in the image's contents, all of which lives in a
`.DS_Store` that only the Finder will write, through Apple Events, onto a read-write image before it is
converted.
Rule: for anything whose whole purpose is to be looked at, the acceptance test is the screenshot, not the
listing. `ls` proving the right files are present says nothing about whether the thing reads as what it is.

## Leave no copies on someone else's machine (2026-09-11)
Debug builds, an export directory, a dmg staging folder and a mounted volume left four Tokendials in the
user's Spotlight. Each was a legitimate build step; together they made their own machine look broken, and
they noticed before I did.
Rule: build output on a machine that is not mine is litter until it is removed. Clean the intermediates in
the same breath as the build, and keep the tree that only exists to be compiled out of the search index.

## The dock's window behaviour is X11's to grant, and Cinnamon grants all of it (2026-09-11)
Before writing a line of Linux UI, a throwaway Avalonia window was made to prove on a Mint 22.3 Cinnamon VM
that it could do what the Windows dock does. All seven properties hold, and each was measured rather than
looked at: per-pixel alpha renders over the wallpaper; `_NET_ACTIVE_WINDOW` is identical before and after the
window maps, so it steals no focus; `_NET_CLIENT_LIST_STACKING` puts it above a maximised terminal; a cursor
moved to four points is reported over the window inside the capsule and over the desktop in the slant, the
hot zone and the open desktop; `XQueryPointer` returns `same_screen=true`; and the tray icon registers with
`org.kde.StatusNotifierWatcher` and renders a bitmap drawn at runtime.
Rule: a platform question with a yes/no answer is a spike, not a discussion. Two hundred lines and an
afternoon replaced a week of arguing from documentation - and three of the answers contradicted what the
documentation implied.

## Four beliefs the spike corrected, all of which would have cost days (2026-09-11)
- **Avalonia's `Position` getter lies on X11.** It reported `0,0` for a window `xwininfo` showed at `460,0`.
  Read back from X, or from a screenshot, never from the property that was just set.
- **`_NET_WM_WINDOW_TYPE_DOCK` alone keeps a window above.** `_NET_WM_STATE_ABOVE` was deleted entirely and
  the stacking order did not change.
- **Avalonia's TrayIcon works on Mint Cinnamon**, despite an open issue titled otherwise, and it does
  transmit a runtime-rendered icon rather than only a theme name.
- **`gnome-terminal` returned in 357 ms for a six-second job.** Anything that wires "the terminal exited" to
  "the install finished" would end every install instantly.
Rule: write down which beliefs a spike is meant to test, then record which ones it broke. The ones that
break are the plan's real content.

## Run the remote tooling from the shell it was written for (2026-09-11)
`tools\linux.ps1 sync` was invoked through the Bash tool, which put Git's `tar` ahead of Windows' on PATH.
Git's tar reads `C:\...` as a remote host, so the archive was never written - and the failure surfaced only
later, as a build that succeeded against sources from two edits ago. Half an hour went into debugging code
that was never on the machine.
Rule: a PowerShell tool gets the PowerShell tool. And a script that shells out to a common command name
should name the binary absolutely, which `Sync-Linux` now does.
