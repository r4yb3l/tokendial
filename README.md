# Tokendial

Native dials for your AI coding assistants. A small panel on the top edge of the
screen shows how much of each tool's usage limit you have burned, and native
notifications tell you when you cross a threshold, when a window is about to
reset, when an agent is waiting on you, and when you have hit the limit.

The website lives in `site/` (Astro, six languages, deployed to Vercel at tokendial.vercel.app; no domain bought yet).

macOS (Swift, AppKit) and Windows (WPF, .NET 10), built from one shared
specification in `docs/`.

Status: both platforms read the nine providers, install what is missing, and carry
the same settings window; the macOS updater is not written yet, so a new version is
found through GitHub Releases rather than fetched. See `docs/` for the provider and
alert specifications both platforms follow, and `windows/README.md` for the Windows
build.

## Installing

Releases are built by CI from a `v*` tag and attached to the GitHub release: a
Velopack installer and a portable zip for Windows, a zip and a disk image for macOS.
The macOS build is a universal binary, so one download covers Intel and Apple
Silicon.

Neither build is signed with a paid certificate, and both operating systems say so:

- **Windows** shows "Windows protected your PC" the first time an unsigned installer
  runs. More info, then Run anyway.
- **macOS** refuses to open an app that was not notarised and arrives with a
  quarantine flag, claiming it is damaged. Either right-click the app and choose
  Open, then Open again in System Settings under Privacy & Security, or clear the
  flag yourself:

  ```sh
  xattr -dr com.apple.quarantine /Applications/Tokendial.app
  ```

Signing properly means a code-signing certificate on Windows and an Apple Developer
ID plus notarisation on macOS. Until then the checksums published with each release
are what a careful reader can verify.

## Providers

Claude Code, Codex, GitHub Copilot, Cursor, Antigravity, Gemini CLI (Code Assist Standard and Enterprise), GLM (Z.ai Coding Plan), Grok, OpenCode Go. Each is
opt-in. Tokendial reads the sign-in each tool already stores on your machine and asks that
tool's usage endpoint; it never writes credentials and never sends them anywhere else.

A tool you do not have yet shows an **Install** button in Settings and on the first run. It shows
the vendor's own install command before running it in a visible terminal, chains the tool's sign-in,
and connects the dial the moment the sign-in lands. Nothing runs until you press Run; see
`SECURITY.md`. GLM is a key rather than a tool and keeps its instructions instead.

## Several accounts

Tokendial reads what a tool stores, so a second account is a second configuration directory. Claude Code
signed in with `CLAUDE_CONFIG_DIR=~/.claude-work` and Codex with `CODEX_HOME=~/.codex-work` each get their own
dial ("Claude Code (work)", "Codex (work)"), their own alerts and their own sessions. The other tools keep a
single sign-in.

## Alerts

| Alert | When |
|---|---|
| Threshold | crossing 50, 80 or 95 % of a provider's headline window, once per window |
| Reset soon | ten minutes before a window you have leaned on resets; and "available again" once a limit lifts |
| Waiting | an agent has waited on you for 20 s, repeating every five minutes |
| Limit | a window hits 100 % or the provider reports a block, with the time it lifts |

Silent while the panel is expanded under your cursor, at most one per provider per minute,
remembered across restarts. The engine is specified in `docs/alerts/spec.md` and both
platforms pass the same conformance vectors.
