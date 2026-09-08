# Tokendial — tracker

Plan: `~/.claude/plans/giggly-wobbling-willow.md`.

## 0 — Mac access
- [x] Remote Login on the Mac, SSH key authorised, host/user recorded in tools/mac.local.ps1 (ignored)
- [x] tools/mac.ps1 with sync / test / build / run / shot (screencapture and the probe run inside the GUI launchd domain; Screen Recording granted to sshd-keygen-wrapper)
- [x] xcodebuild 26.6, xcodegen, swift 6.3 verified over SSH

## 1 — Foundations
- [x] Repo, license, README, VERSION, ignores
- [x] docs/providers schema + seven specs
- [x] docs/fixtures (25) and docs/alerts spec + 17 vectors
- [x] docs/design/tokens.md
- [x] CI workflow (spec validation, windows, macos)

## 2 — Cores: model, Claude Code, alert engine
- [x] C# alert engine passes the 17 vectors
- [x] Swift alert engine passes the 17 vectors
- [x] C# model, store, archive, backoff, copy rules
- [x] Swift model, store, archive, backoff, copy rules
- [x] Claude Code adapter in C#, verified live on Windows (Team plan, three windows)
- [x] Claude Code adapter in Swift, verified live on the Mac from the login keychain (Team plan, 34 %)

## 3 — Windows panel
- [x] Compact capsule (18 pt dials) and expanded cells (56 pt dials, marks, percent, bars, session line)
- [x] Non-activating, click-through window at the top edge; hover by cursor polling; spring motion
- [x] Hover card per provider (windows, reset copy, both ends, sessions)
- [x] Toasts through the toolkit, verified in the notification store; Focus Assist badge
- [x] Tray icon coloured by the worst band; second-instance signals (show, --settings, --test-alert)

## 4 — macOS panel
- [x] Status-level non-activating panel below the menu bar (under the notch when present), compact and expanded, dials with spring animation
- [x] Hover card, banners in the accent colour, UNUserNotificationCenter sink, menu bar item, SwiftUI settings and welcome
- [x] tokendial:// URL scheme (show, show?provider=, settings, test-alert) for deep links and remote verification
- [x] Hover card, banner, settings and menu bar item verified by screenshot on the Mac (2026-09-08)
- [x] Stable dev signing identity (self-signed Tokendial Dev, trusted for code signing); remote builds run inside the GUI launchd domain so codesign reaches the keychain
- [ ] Geometry tests for notch / no-notch / multiple screens

## 5 — Remaining providers and sessions
- [x] C#: Codex, Cursor, Antigravity, GLM, Grok, OpenCode adapters; every fixture parses (45 fixture cases)
- [x] C#: Claude, Cursor, Codex, Antigravity, Grok session monitors
- [x] Swift: parsers and adapters for the seven providers pass every fixture; Claude sessions monitor
- [ ] Swift: Cursor, Codex, Antigravity, Grok session monitors
- [ ] Live check on Windows of Codex sessions (usage verified live: two windows)

## 6 — Settings, login, first run
- [x] Settings window: providers with account detail, panel mode, alert kinds and numbers, launch at login, test alert
- [x] First run: detected tools, opt-in, absent tools listed
- [x] Launch at login via HKCU Run

## 6b — Languages
- [x] Shared catalogue docs/i18n (en, es, ar) with CLDR plurals and culture formats; C# loader, tests keep languages aligned
- [x] Windows: every string through the catalogue, language selector with live switch, RTL for Arabic
- [x] fr, de, en-GB catalogues
- [x] Swift loader, Copy and sign-in copy through the catalogue, tests (run by CI on macos)
- [ ] macOS app wiring: language picker, settings and welcome strings, RTL

## 7 — Distribution
- [x] `dotnet publish` single-file self-contained exe (77 MB) runs from `windows/dist`
- [ ] Code signing, installer (Velopack), GitHub Release, tokendial.app page
- [ ] macOS zip/dmg

## Review
- macOS v0.1.0 panel, card, banners, settings and Claude Code reading verified live on the Mac over SSH on 2026-09-08; Swift core passes the same 17 vectors and 25 fixtures as C#.
- Windows v0.1.0 is functionally complete and verified live on 2026-09-07: welcome, compact and expanded capsule, hover card, settings, toast delivery, published exe.
- Known limits: Windows 11 Do not disturb is not visible through SHQueryUserNotificationState, so the muted badge only tracks legacy quiet hours; no acrylic blur behind the capsule (plain translucent surface).
