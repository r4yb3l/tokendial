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
| `tasks/lessons.md` | **read this before a second attempt at anything.** Every entry is a mistake that reached a user or burned an afternoon |

## Build and test

```sh
dotnet test windows/Tokendial.Tests/Tokendial.Tests.csproj   # the whole suite, any OS
dotnet run --project linux/Tokendial.Avalonia                # the Linux app
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

- X11 only. The dock has to place itself and no Wayland protocol lets a client do that, so a Wayland session
  is refused with an explanation rather than degraded silently.
- The dock hangs from the top only. Settings offers four positions and the other three do nothing on this
  platform — either implement them or hide them; a control that lies is worse than one that is absent.
- Antigravity's usage cannot be read: its token lives behind Secret Service. Its sessions work.
- Packaged as a self-updating AppImage by Velopack, built on ubuntu-22.04 so it runs on Mint 21 and
  Debian 12. An AppImage has no installer, so the app carries its own — first run asks before it writes
  anything, and Settings has an Uninstall that removes all of it.

## Before you say it is done

- Run the suite. On the platform you changed, and ideally on another.
- If the change is about behaviour a user sees, look at it — a screenshot, the log at
  `~/.config/Tokendial/logs/tokendial.log` (`%APPDATA%\Tokendial\logs` on Windows), something.
- If you claim it is on `main`, check the remote. `git log @{u}..HEAD` is the whole check, and skipping it
  once cost a contributor an evening building a branch that had never been pushed.
- Say what you did not verify. That sentence is worth more than the one listing what you did.
