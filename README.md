# Tokendial

Native dials for your AI coding assistants. A small panel on the top edge of the
screen shows how much of each tool's usage limit you have burned, and native
notifications tell you when you cross a threshold, when a window is about to
reset, when an agent is waiting on you, and when you have hit the limit.

macOS (Swift, AppKit) and Windows (WPF, .NET 10), built from one shared
specification in `docs/`.

Status: Windows is feature-complete for v0.1 (see `windows/README.md`); macOS is next.
See `docs/` for the provider and alert specifications both platforms follow.

## Providers

Claude Code, Codex, GitHub Copilot, Cursor, Antigravity, GLM (Z.ai Coding Plan), Grok, OpenCode Go. Each is
opt-in. Tokendial reads the sign-in each tool already stores on your machine and asks that
tool's usage endpoint; it never writes credentials and never sends them anywhere else.

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
