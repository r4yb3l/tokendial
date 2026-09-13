# Candidate providers — research parked, not implemented

Nothing here is in `docs/providers/`. Each entry says what was established, from what source, and
exactly what is missing before it could ship. Re-reading a vendor's client is expensive; this file
exists so it is done once.

---

## Augment Code — ready to spec, blocked only on validation

**Status: parked 2026-09-12.** Everything below was read out of the vendor's own published client,
`@augmentcode/auggie` 0.36.0 on npm (`augment.mjs`, a bundled single file). It is not guesswork and it
is not a live capture either — no response has ever been seen.

**Credential.** `~/.augment/session.json`, written by `auggie login`. The path is built with
`path.join(os.homedir(), ".augment", "session.json")` and the directory is created with mode `0700`,
the file with `0600` — so it is the same location on Windows, macOS and Linux, with no per-platform
branch. Contents:

```json
{ "accessToken": "...", "tenantURL": "https://<tenant>.api.augmentcode.com/", "scopes": [...] }
```

Both fields are needed: the tenant URL is the API base, and it differs per account.

**Endpoint.** `POST {tenantURL}get-billing-summary` — the path is resolved as `new URL(name, tenantURL)`,
so the tenant URL's trailing slash matters. Body is an empty JSON object. Headers, from the shared
`callApi`:

```
Authorization: Bearer {accessToken}
Content-Type: application/json
User-Agent: <client>
x-request-id: <uuid>
x-request-session-id: <uuid>
```

**Response**, field names exact:

| Field | |
|---|---|
| `plan_name` | display string |
| `usage_unit` | the unit `amount_*` are counted in; Augment bills in dollars on the Business plan |
| `amount_remaining` | what is left this cycle |
| `amount_included_per_cycle` | the allowance |
| `billing_cycle_end_date_iso` | ISO 8601, the reset |
| `banner` | optional vendor notice, may be null |

A dial follows directly: `used = 1 - amount_remaining / amount_included_per_cycle`, reset at
`billing_cycle_end_date_iso`. Same shape as Cursor's billing-cycle window.

**Errors the client already distinguishes.** Status `5` is "user has no subscription", and the CLI
renders it as *the command is not available for your current plan*. Status `4` is "unimplemented".
Both must map to a sign-in/unavailable state rather than to a failure, or a free or unentitled account
sees an error where it should see nothing.

**Also present:** `POST {tenantURL}get-credit-info`, same envelope, for sub-agent credits. Not needed
for a headline dial.

**Sign-in and install.** `npm i -g @augmentcode/auggie`, then `auggie login`. A vendor-scoped npm
package, which the install rules already allow. `auggie account status` renders the same figures and
has a JSON mode, so a CLI fallback exists if the endpoint is ever unreachable.

**What is missing, and it is the only thing.** No response has been validated. There is no Augment
machine here and the only plan their pricing page documents is Business at **$100/month**, so
validating costs real money rather than a free sign-up. Until somebody with an account runs
`auggie account status --json` or the raw POST once, this stays out of `docs/providers/`.

**To unblock:** one capture of `get-billing-summary` from a real account — field names, units, and what
`usage_unit` actually contains.

---

## Windsurf — not worth starting

**Status: rejected 2026-09-12**, revisit only if the rebrand settles.

- The product is being folded into Cognition's **Devin Desktop**; `docs.windsurf.com` already serves as
  "Devin Docs" and the prose says Devin Desktop throughout. The separate Codeium/Windsurf editor plugin
  is described by the vendor as in maintenance mode.
- The metering model changed in **March 2026** from prompt credits to a daily + weekly token quota.
  That shape would suit a dial well, but it changed once this year already.
- **There is no documented personal usage endpoint.** The only published API is an Enterprise analytics
  API authenticated with a service key, scoped to teams — not "how much do I have left". The in-app
  usage meter calls something undocumented, and finding it requires the app installed.

Revisit when the Devin Desktop rename is finished and if a per-user endpoint appears. Discovering it
otherwise means installing the IDE and watching its traffic.

---

## Where the lead came from

`moorcheh-ai/memanto`'s `memanto/cli/connect/agent_registry.py` maps fourteen assistants and their
config locations. Tokendial covers nine. The rest — `cline`, `continue`, `goose`, `roo`, `pi` — are
bring-your-own-API-key, so there is no vendor-side quota to draw; only Windsurf and Augment sell a plan
with a limit of their own. Note the registry maps *instruction files and skill directories*, not
credentials, so it answers "does this tool exist and where does it live" and nothing about usage.
