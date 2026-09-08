# Tokendial — tracker

Plan: `~/.claude/plans/giggly-wobbling-willow.md`.

## 0 — Mac access
- [ ] Remote Login on the Mac, SSH key authorised, host/user recorded in tools/mac.local.ps1 (ignored)
- [ ] tools/mac.ps1 with sync / test / run / shot
- [ ] xcodebuild, xcodegen, swift verified over SSH

## 1 — Foundations
- [x] Repo, license, README, VERSION, ignores
- [x] docs/providers schema + seven specs
- [x] docs/fixtures (25) and docs/alerts spec + 17 vectors
- [x] docs/design/tokens.md
- [x] CI workflow (spec validation, windows, macos)

## 2 — Cores: model, Claude Code, alert engine
- [x] C# alert engine passes the 17 vectors
- [ ] Swift alert engine passes the 17 vectors
- [ ] C# model, store, archive, backoff, copy rules
- [ ] Swift model, store, archive, backoff, copy rules
- [ ] Claude Code adapter in both, verified live on Windows

## 3 — Windows panel
## 4 — macOS panel
## 5 — Remaining providers and sessions
## 6 — Settings, login, first run
## 7 — Distribution
