# Working on Tokendial

Notes for an AI agent, or for a person who would rather read one page than three. If something here
disagrees with the code, the code is right and this file is a bug.

## What this is

A dock at the edge of the screen showing how much of each AI coding assistant's usage is left. Three native
applications — **WPF on Windows, AppKit on macOS, Avalonia on Linux** — over one shared specification.

The single most useful thing to understand: **`docs/` is the shared artifact, not a code project.** The
provider specs, the alert conformance vectors, the six language catalogues and the design tokens live there
and are read by all three apps at runtime and by the test suites. Code is *reimplemented* per platform and
never shared across languages; `Chrome.cs` and `Chrome.swift` are the same vocabulary written twice on
purpose. Do not propose extracting a cross-platform UI library.

| Path | |
|---|---|
| `docs/` | providers, alert vectors, i18n, design tokens — change a fact here and every platform follows |
| `windows/Tokendial.Core/` | the shared C# core. **Linux uses it unchanged** — the one Windows-only API in it, a WMI query in `AntigravityProvider`, is guarded, and that provider declares itself unreadable on Linux |
| `windows/Tokendial.App/` | the WPF app |
| `macos/` | the AppKit app and `TokendialCore`, its Swift package |
| `linux/Tokendial.Avalonia/` | the Avalonia app, referencing `windows/Tokendial.Core` directly |
| `site/` | the Astro site at tokendial.app — its own npm project, its own locales, its own CI job |
| `tools/` | PowerShell helpers that drive a remote build over SSH. `mac.ps1` works; **`linux.ps1` points at a VM that no longer exists**. `LinuxDisplayProbe/` opens the real dock with fixture readings; its `nested-checks.sh` runs hotplug, scale, hover-card and Wayland-notice checks on a nested X server |
| `tasks/lessons.md` | **read this before a second attempt at anything.** Every entry is a mistake that reached a user or burned an afternoon |

## Build and test

```sh
dotnet test windows/Tokendial.Tests/Tokendial.Tests.csproj   # the Core suite, any OS
dotnet test linux/Tokendial.Linux.Tests/Tokendial.Linux.Tests.csproj # Linux UI and geometry, headless
dotnet run --project linux/Tokendial.Avalonia                # the Linux app
tools/LinuxDisplayProbe/nested-checks.sh hotplug Left 1 2    # Linux display checks, nested X server
dotnet run --project windows/Tokendial.App                   # Windows only
swift test --package-path macos/TokendialCore                # macOS core
```

**Never `dotnet test windows/Tokendial.slnx` off Windows** — that solution includes the WPF app, which
targets `net10.0-windows` and cannot build elsewhere. Name the test csproj directly. The tests are the same
everywhere, and a handful assert behaviour that differs by platform; those say which platform they mean and
return early on the others. If you add one, do the same rather than assuming Windows.

Specs are validated in CI against their schema, so a malformed provider block fails the build:

```sh
npx --yes -p ajv-cli@5 -p ajv-formats@3 ajv validate --spec=draft2020 -c ajv-formats \
  -s docs/providers/schema.json -d "docs/providers/!(schema).json"
```

## House rules

- **Commit messages, branches and PR titles are in English**, always, whatever language the conversation is
  in. Say what changed and why it was wrong before; a commit that only names the files it touched is noise.
- **Code documents itself through naming and structure.** No `// TODO`, no `// fix this`, no narrating what
  the next line does. A comment earns its place by explaining something the code cannot: why a branch
  exists, what broke without it. Use `///` XML docs on public C# surfaces when the behaviour is not obvious.
- **No commented-out code.** Delete it; git remembers.
- **A new string goes in `docs/i18n/{en,es,fr,de,ar}.json`.** Not `en-GB.json` — that is a four-key overlay
  of British spellings and the completeness test exempts it. `I18nTests` fails if English gains a key the
  other four lack, or if the placeholders differ.
- **Do not mutate global state in a test.** `Strings.Use` is process-wide and xUnit runs classes in
  parallel; anything touching it belongs in `[Collection("language")]`. A test that passed locally and
  failed on CI has usually made this exact mistake.

## Things that are true and non-obvious

- **Credentials are read, never written.** Tokendial reads the token each tool already stores, sends it only
  to that tool's own documented endpoint, and never refreshes, rewrites or forwards it. A change that writes
  to a provider's credential file is wrong however convenient.
- **Nothing runs without the user pressing Run.** The install assistant shows the vendor's own command, from
  the spec, in full, before a terminal opens. There is a test asserting the generated script contains
  nothing beyond the recipe's own text and the fixed template — no stray `curl`, `sudo`, `rm` or `$(`.
- **Install recipes never use a distribution's package manager.** The names collide: brew's `grok` is a
  regex tool and its `glm` a C++ maths library; apt has the same trap. Only the vendor's own script at a
  host the spec already names, a vendor-scoped npm package, or a download page.
- **A provider that cannot be read somewhere says so**, via `status.platforms` in its spec. Reporting
  "sign in" to somebody who is signed in is the failure this exists to prevent.
- **Paths go through `Roots`, not `Environment.SpecialFolder`.** On Unix .NET maps `ApplicationData` to
  `~/.config`, so a Windows-shaped path does not throw — it resolves to a directory that exists and holds
  nothing, and the provider silently reports needing a sign-in. `PathTableTests` asserts every platform's
  paths from any platform, against the specs rather than against another copy of the code.

## Linux

Maintained by **@M4ss1ck** since 2026-09-12, on real hardware. There is no Linux machine on the author's
side and no virtual one either: the old VM was deleted because it lied in both directions — it passed
things that only worked because the environment was too simple, and it could not be made to attach a second
monitor at all, which is why the display chooser shipped unverified.

Practical consequence, and the thing to be honest about: **a claim about Linux behaviour is either covered
by a test that runs anywhere, or it was confirmed on the maintainer's hardware.** Say which. "It builds" is
not "it works", and a green CI run is not a verified artifact — v0.1.0 shipped an arm64-only macOS binary
for exactly that reason.

Current state:

- X11 only, by decision. Wayland, XWayland included, is refused before providers or the installer start:
  the dock opens by polling the global pointer, which Wayland does not expose. The way forward is a
  layer-shell backend, and Mutter (GNOME) does not implement layer-shell. `linux/README.md` has the
  reasoning and what would change it.
- All four dock edges work. Side docks are upright columns that wrap into more columns, as on Windows.
- **Do not replace `UsePlatformDetect()` with `UseX11()`** in `Program.Builder`. It drops the renderer and
  text shaper and every start aborts, which the headless tests cannot see. CI's Xvfb probe step can.
- Antigravity's usage cannot be read: its token lives behind Secret Service. Its sessions work.
- Packaged as a self-updating AppImage by Velopack, built on ubuntu-22.04 so it runs on Mint 21 and
  Debian 12. An AppImage has no installer, so the app carries its own — first run asks before it writes
  anything, and Settings has an Uninstall that removes all of it.

## The site

`site/` is an Astro project with its own toolchain, and CI grades it separately from the apps:

```sh
cd site && npm ci && npm run check-posts && npm run check-locales && npm run build
```

`check-locales` exists because a missing key there is **silent** — `t()` falls back to English and the page
renders in the wrong language instead of breaking. `check-posts` asserts what the frontmatter schema
cannot: both languages present, sources filled in, no font the content security policy would refuse. The
site's locales are its own, separate from `docs/i18n/`; adding a string to one does not add it to the other.

## Shipping a release

Releases are cut by **pushing a tag `vX.Y.Z` that matches the `VERSION` file at the repo root**. All three
platform jobs re-read `VERSION` and fail the build if the tag disagrees, so bump `VERSION` in a commit
first, then tag that commit. There is no other trigger and no manual upload step.

What each job produces: Windows a `Setup.exe`, a portable zip and an MSI, macOS a universal `.zip`
and `.dmg`, Linux a self-updating AppImage on the `linux` channel built on **ubuntu-22.04** so it
runs on glibc 2.35 and later. The Windows job creates the GitHub release; macOS and Linux attach to
it with `gh release upload`, because `vpk upload` would try to create a release of its own.

Four gates run on Linux before anything is attached, each of which is a bug that already shipped once:

- the tag must match `VERSION`;
- the published bundle must actually carry `libcoreclr.so` and `libe_sqlite3` — without the SQLite native,
  Cursor's credential and Codex's sessions return null **in silence**, which is the entire class of bug the
  port exists to kill;
- the AppImage's `.desktop` entry must name an icon the AppImage actually contains, or the menu draws a
  blank square;
- the AppImage keeps the name Velopack gave it. `releases.linux.json` references it by that name and
  renaming it to the house convention breaks self-update. Only the checksum file gets our naming.

Release notes are written by hand — nothing generates them. And **a green release run is not a verified
release**: download the artifact from the release page, check the sum, run it. v0.1.0 shipped an
arm64-only macOS binary past a green build.

## What is knowingly unfinished

Not bugs to be discovered — decisions already taken and not yet acted on:

- **The display chooser exists on Linux only.** `windows/Tokendial.Core/Model/Displays.cs` and
  `DockPlacement` are shared and tested, but Windows still resolves `MonitorFromPoint(0,0)` in
  `Interop/Native.cs` and macOS still takes `NSScreen.screens.first`. Both need the same wiring Linux
  already has: a named-screen lookup, the settings chips, and a re-place on the display-change event.
- **Physical unplug and physical mixed-DPI panels are still unseen on Linux.** Fallback, return and
  per-monitor scale pass on a nested X server (`nested-checks.sh`), and the four edges were checked on real
  hardware, but a real driver's RandR events and a real HiDPI panel were not. `linux/README.md` lists the
  record and what would settle each gap.

## Before you say it is done

- Run the suite. On the platform you changed, and ideally on another.
- If the change is about behaviour a user sees, look at it — a screenshot, the log at
  `~/.config/Tokendial/logs/tokendial.log` (`%APPDATA%\Tokendial\logs` on Windows), something.
- If you claim it is on `main`, check the remote. `git log @{u}..HEAD` is the whole check, and skipping it
  once cost a contributor an evening building a branch that had never been pushed.
- Say what you did not verify. That sentence is worth more than the one listing what you did.
