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

## 7b — Settings redesign (user's Tailwind mockup, 2026-09-08)
- [x] Chrome design system: palette, title bar with logo/version/status pill, drawn checkbox/radio/card/chip/field/button, thin scrollbar
- [x] Settings window rebuilt: two scrolling columns, provider cards with tinted tiles and usage bar, install rows in the same style
- [x] Theme switch (system / dark / light) with a derived light palette for panel and windows; follows Windows in system mode
- [x] Dock position setting: top, bottom, left, right (rows and columns, same tiles); verified live in the four positions
- [ ] Welcome window on the same components
- [ ] Light palette reviewed by design (current one is derived from the dark mockup)
- [ ] macOS settings to the same tokens

## 10 — From the TokenMeter comparison (approved 2026-09-08)
- [ ] Gemini CLI provider (Workspace/Enterprise only): shared Google CodeAssist transport, spec, fixtures, sign-in guidance, mark
- [ ] Forecast sentence on the hover card from an in-memory usage history (least squares over the current epoch)
- [ ] Codex profiles via CODEX_HOME directories; shared Profiles.Discover; ProviderFamily helper; session ids carry the profile
- [ ] Velopack auto-update with a settings switch; folder publish + vpk pack; release workflow; SECURITY/site copy updated
- Out of scope by decision: quiet hours and changes to the "available again" alert

## 9 — Website (tokendial.app)
- [x] Astro + Tailwind v4 static site in `site/`, six languages with RTL, dock drawn from the app tokens and marks, OG image, sitemap, security headers
- [x] Copy aimed at vibecoders (why / how / providers / alerts / dock / install assistant / security)
- [x] Blog for vibecoders in `site/src/content/blog` (en + es): three save-tokens articles and eight install guides generated from the provider specs; sections, RSS, related posts, landing block
- [ ] Editorial calendar: eight to ten more articles before announcing the community; tools and news sections still empty
- [ ] Cloudflare Pages project connected to the repo, `tokendial.app` domain attached (user)
- [ ] Download links point at GitHub Releases; publish a first release and make the repo public before launch

## 8 — Install assistant (Windows first)
- [x] docs/providers: `install` block in the schema and in seven specs (glm has none)
- [x] docs/i18n: `install.*` strings in en, es, fr, de, ar
- [x] Core: InstallCatalog (embedded specs), ToolLocator, InstallState, InstallScript, InstallWatcher
- [x] App: InstallAssistant (watchers owned by App), TerminalRunner, RunSheet, InstallSteps rows in Settings and Welcome
- [x] Tests: catalog, script, locator, watcher
- [ ] SECURITY.md and README paragraphs
- [x] Live on this PC: OpenCode row Install → sheet → terminal (winget) → detected as installed → terminal closed without sign-in → row shows the not-seen hint with Sign in as the next step. Sign-in → auto-connect still to be exercised by the user pressing the buttons; Grok untested live
- [ ] macOS: same recipes, zsh script, Terminal.app, Strings.load + bundled docs (later, when the Mac is on)

## Review
- macOS v0.1.0 panel, card, banners, settings and Claude Code reading verified live on the Mac over SSH on 2026-09-08; Swift core passes the same 17 vectors and 25 fixtures as C#.
- Windows v0.1.0 is functionally complete and verified live on 2026-09-07: welcome, compact and expanded capsule, hover card, settings, toast delivery, published exe.
- Known limits: Windows 11 Do not disturb is not visible through SHQueryUserNotificationState, so the muted badge only tracks legacy quiet hours; no acrylic blur behind the capsule (plain translucent surface).
