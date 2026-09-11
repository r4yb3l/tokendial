# Security

Tokendial reads the sign-in each coding tool already keeps on your machine and asks that tool's
usage endpoint. That is the whole threat surface, and these are the rules the code follows.

## What Tokendial never does

- **Store a credential.** Nothing under `%LOCALAPPDATA%\Tokendial` (or `~/Library/Application
  Support/Tokendial`) holds a token, cookie or key. The files there are settings, the last usage
  reading per provider, rate-limit holds and alert epochs.
- **Write or refresh a credential.** Files, keychains and the Credential Manager are opened
  read-only. When a token expires, Tokendial shows "sign in" and waits for the tool to refresh it.
- **Log a secret.** Logs carry HTTP status codes and Tokendial's own messages, never headers,
  bodies or URLs with credentials.
- **Send anything anywhere but the provider's own host.** Every endpoint host is a constant in the
  code and in `docs/providers/*.json`. There is no telemetry and no crash reporting. The one
  exception is the update check: once a day an installed copy asks GitHub Releases whether a newer
  version exists, carrying nothing about you, and Settings can switch it off. The download waits
  for you to choose "Restart to update" in the tray.
- **Follow a redirect.** Redirects are disabled on every client; a 3xx is treated as a bad
  response. A credential therefore cannot be forwarded to a host the spec did not name.
- **Trust an unexpected certificate.** System validation applies everywhere except the Antigravity
  language server on loopback, which uses a self-signed certificate; that exception is limited to
  loopback addresses by construction.

## What it does read, and where

| Provider | Source on Windows | Sent to |
|---|---|---|
| Claude Code | `~/.claude/.credentials.json` (OAuth access token) | `api.anthropic.com` |
| Codex | `~/.codex/auth.json` (ChatGPT access token, account id) | `chatgpt.com` |
| GitHub Copilot | `github-copilot/apps.json` (GitHub OAuth token), else the GitHub CLI token | `api.github.com` |
| Cursor | `state.vscdb` in Cursor's global storage (session token) | `cursor.com` |
| Antigravity | Credential Manager entry `gemini:antigravity`; the local language server | `cloudcode-pa.googleapis.com`, `127.0.0.1` |
| GLM | Claude settings, ZCode or OpenCode config (API key) | `api.z.ai` or `open.bigmodel.cn` |
| Grok | `~/.grok/auth.json`, only entries issued by `auth.x.ai` | `cli-chat-proxy.grok.com` |
| OpenCode | `~/.local/share/opencode/auth.json`, only `opencode-go` | `opencode.ai` |

Encrypted ZCode tokens (`enc:v1:`) are ignored rather than decrypted. Cursor's database is opened
read-only; if that fails and a temporary copy is needed, the copy is zeroed and deleted when the
read finishes and any leftover from a crash is destroyed at the next start.

## The install assistant

A provider whose tool is missing offers an **Install** button, and one that is installed but signed
out offers **Sign in**. Both open a sheet that shows the exact command first, whose vendor it comes
from and where it is documented; nothing runs until you press Run. Then a visible PowerShell window
runs that command and, for a command-line tool, the tool's own sign-in right after it.

- The commands are the vendors' published installers (`irm https://claude.ai/install.ps1 | iex`,
  `winget install GitHub.Copilot`, and so on). They ship inside the binary, read from
  `docs/providers/*.json`; Tokendial never downloads a recipe or a script.
- The script prints the command before running it, runs it in a child process, stops at the first
  failure, and stays open so you can read what happened.
- Nothing runs elevated. When a winget package needs administrator rights, Windows asks you.
- The sign-in is the tool's own; Tokendial only notices that its credential appeared and connects
  the dial. It never sees or stores the resulting token.

## Limits you should know

- Tokens live in process memory while a request is in flight. Any process running as the same
  user can read that memory, exactly as it can read the source files themselves. Tokendial does
  not change that model.
- **The binaries are not signed, and will not be.** A code-signing certificate and an Apple
  Developer ID cost more each year than this project earns, which is nothing, so Windows shows
  SmartScreen and macOS reports the app as damaged. What stands in place of a signature is a
  checksum: each release publishes `Tokendial-win.sha256` and `Tokendial-<version>-macos.sha256`
  over the files offered for download. Check one before running it:

  ```sh
  sha256sum -c Tokendial-win.sha256
  ```

  That tells you the download matches what the public build produced. It cannot tell you the build
  itself is honest - for that, the workflow that made it and the source it was made from are both
  in this repository.
- The settings window and hover card show the account label a tool reports (a plan name, or for
  Codex the account email). Nothing else about the account is displayed.

## The website

The app sends nothing. The website counts page views with Vercel Web Analytics, which sets no cookies,
builds no cross-site identity and serves both its script and its beacon from tokendial's own origin -
which is why the site's content security policy allows `connect-src 'self'` and no third party at all.
What it answers is whether anyone arrived and which page they read. If that is one promise too many for
you, blocking `/_vercel/insights` changes nothing about the site.

## Reporting

Open an issue at https://github.com/r4yb3l-qa/tokendial or write to the address on the profile.
Please do not include tokens or credential files in a report.
