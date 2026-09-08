# Alert engine

The engine is a deterministic reducer, identical in Swift and C#:

```
reduce(state, event, config) -> (state', alerts[])
```

Time is an input. The engine owns no clocks, timers or threads; the host feeds it
events and delivers the alerts it returns through a platform `AlertSink`. Both
cores run the conformance vectors in `vectors/` and must emit exactly the same
sequence for each.

## Events

| Event | Fields | Meaning |
|---|---|---|
| `usage` | `t, provider, window, usedPct, resetsAt?, limited` | A fresh reading of one window. `limited` is true when the provider reports a block. |
| `session` | `t, provider, sessionId, state` | `working`, `waiting` or `done`. |
| `hover` | `t, on` | The panel is expanded under the cursor. |
| `tick` | `t` | The host's clock, at least every 30 s. |
| `restart` | `t` | The app started; state was reloaded from disk. |

## Config

```json
{ "thresholds": [50, 80, 95], "resetLeadSeconds": 600, "resetLeadMinPct": 50,
  "waitingDebounceSeconds": 20, "waitingRepeatSeconds": 300,
  "perProviderCooldownSeconds": 60 }
```

## Epochs

Every `(provider, window)` lives in an epoch keyed by `resetsAt`. A new epoch
begins when `resetsAt` changes or when `usedPct` drops by more than 10 points
from the last sample. A new epoch clears every fired flag: thresholds re-arm,
`limit` re-arms, `resetSoon` and `resetDone` re-arm.

## Alerts

- **threshold** `(provider, window, pct)`. On a `usage` sample, the highest
  threshold in `config.thresholds` that is `<= usedPct` and not yet fired in
  this epoch fires; every lower unfired threshold is marked fired silently
  (jumping 40 → 96 emits one alert, for 95). Suppressed when `limit` fires from
  the same sample.
- **limit** `(provider, window, resetsAt?)`. `usedPct >= 100` or `limited`;
  once per epoch. Marks every threshold fired.
- **resetSoon** `(provider, window, resetsAt)`. On `tick` or `usage`, when
  `resetsAt - t <= resetLeadSeconds`, `resetsAt > t`, the epoch's last
  `usedPct >= resetLeadMinPct`, once per epoch.
- **resetDone** `(provider, window)`. When `t >= resetsAt` for an epoch in
  which `limit` fired, once; then the epoch is closed.
- **waiting** `(provider, sessionId)`. A session entering `waiting` starts a
  debounce of `waitingDebounceSeconds`; if it is still waiting when a later
  event's `t` passes the deadline, fire. Repeat every `waitingRepeatSeconds`
  while it stays waiting. `working` or `done` cancels and resets.

## Cross-cutting rules

- **Hover**: while `hover on`, `threshold` alerts are marked fired but not
  emitted (the user is looking). `limit`, `resetSoon`, `resetDone` and `waiting`
  are *held* and emitted, in order, on the next `hover off`.
- **Cooldown**: at most one emitted alert per provider per
  `perProviderCooldownSeconds`; later ones in the window are dropped, except
  `limit` and `waiting`, which are held until the cooldown ends and emitted on
  the next event.
- **Restart**: state is persisted after every reduction and reloaded before the
  `restart` event, so nothing fires twice across a relaunch. Epochs whose
  `resetsAt` is more than 24 h in the past are pruned on `restart`.
- **Ordering**: alerts emitted by one reduction are ordered `limit`, `threshold`,
  `resetSoon`, `resetDone`, `waiting`, then by provider id.

## Persisted state

```json
{ "schema": 1,
  "epochs": [ { "key": "claude|session|2026-09-07T18:49:59Z", "resetsAt": "…",
                "lastPct": 84, "fired": ["50", "80", "limit"], "resetSoon": false, "resetDone": false } ],
  "waiting": [ { "key": "claude|claude.11048", "since": "…", "lastFired": "…" } ],
  "held": [], "cooldownUntil": { "claude": "…" } }
```

## Conformance vectors

`vectors/*.json`:

```json
{ "name": "…", "config": { … },
  "steps": [ { "t": 0, "event": { "kind": "usage", "provider": "claude", "window": "session", "usedPct": 40, "resetsAt": 18000 } } ],
  "expected": [ { "t": 0, "kind": "threshold", "provider": "claude", "window": "session", "pct": 50 } ] }
```

`t` and `resetsAt` are seconds relative to the vector's start. A test feeds the
steps in order, collects every emitted alert with the `t` of the step that
emitted it, and compares the list to `expected` exactly. Missing `config`
fields take the defaults above. A step may carry `"persistAndRestart": true`,
meaning: serialise the state, build a fresh engine from it, and continue.
