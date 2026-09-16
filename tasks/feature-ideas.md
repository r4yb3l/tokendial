# Feature ideas, and the code facts that make them cheap or expensive

Nothing here is decided. What makes this worth keeping is the second column: each idea is priced
against what the code actually does today, established by reading it on 2026-09-16. Re-deriving
that is the expensive part.

## What the app already has

- **`Store/ReadingArchive.cs` keeps only the last good reading per provider**, plus each provider's
  backoff deadline (`readings.json`, `backoff.json`). It is a state file, not a time series.
- **`Model/UsageHistory.cs` is in memory only** — 180 samples per window, three hours of age at
  most, and the ring is cleared when the epoch changes (the reset moves, or the fraction drops by
  more than 10%, which is what a rollover looks like from outside).
- **`Model/Forecast.cs` already computes pace**: least squares over the last 60 minutes, ignoring
  anything slower than half a percentage point an hour, and it surfaces in the hover card.
- **`IAlertSink` is a clean seam** with three live implementations — `BannerSink` and `ToastSink` on
  Windows, `BannerSink` and `NotifySink` on Linux.
- **Alert thresholds are already user-set**: `Settings.Thresholds`, default `[50, 80, 95]`, validated
  on load.
- **`Sessions/ActivityHub.cs` knows which tool is running right now**, and the panel model consumes it.

**So the app deliberately remembers nothing across launches but the last number.** Every "report" or
"analytics" idea below is gated on reversing that one decision, and none of the others are.

## Cheap, and they do not need history

- **A webhook alert sink.** A fourth `IAlertSink` that POSTs to a URL the user supplies — ntfy,
  Gotify, Discord, Telegram, anything. This is the honest version of "alerts on my phone": the
  machine stays the only thing that ever holds a token, and what leaves is "Claude Code 90%, resets
  at 18:00". No backend, no account, nothing for us to custody. The alert logic already exists and
  already passes its vectors; what is new is one class per platform, a URL field, and a plain
  statement in the UI that this is the first thing Tokendial sends anywhere that is not the
  provider itself. Watch that a dead webhook cannot stall the alert path.
- **`tokendial status --json`.** There is no command-line surface at all today. The readings are
  already on disk; printing them and exiting turns Tokendial into the data source for other
  people's status bars — tmux, starship, waybar, Raycast. Cheapest item here, and the only one that
  reaches people who would never install a dock.
- **Which model to use now.** When one window is nearly spent and another is not, say so. Both
  numbers are already on screen; only the sentence is missing.
- **Pace against reset, not just percentage.** `Forecast` computes the slope and spends it on a
  single "runs out at" line. The same number supports "three times your sustainable pace" without
  persisting anything.
- **Long-session alert.** `ActivityHub` plus the current reading is enough for "four hours in, 60%
  of the weekly gone" — which nothing else installed on the machine can tell you.

## The fork in the road: keeping history

Persisting the samples that are thrown away today unlocks the rest, and the one worth having is
**subscriptions you are not using**: three weeks without touching Copilot while paying for it.
It is the only idea here that hands the user money back rather than preventing an interruption,
and Tokendial is the only thing installed that watches all of them at once. Day-of-week and
hour-of-day patterns, week-over-week, CSV export and a sparkline under each dial all fall out of
the same change.

The cost is not the code. It is that the app stops holding a state file and starts holding a record
of somebody's working activity — retention, size, what uninstall removes, and the fact that on
Linux it sits in `~/.config` unencrypted. Today the worst case of a leaked `readings.json` is that
someone learns you were at 40%. With six months of history the worst case is a person's whole
working schedule. Doable well, with a retention ceiling, explicit deletion and off by default —
but it is a product decision, not an afternoon.

## Rejected, with the reason

- **Cloud account, cross-machine sync, web dashboard.** Anything that moves tokens or history off
  the machine spends the only differentiating argument the product has.
- **Counting tokens or cost per prompt.** That means reading transcripts and competing with the
  usage-analysis tools. Tokendial reads the provider, not the log — and that line is what keeps the
  provider specs small.

## Cost of a new provider, for reference

Adding one touches the spec plus roughly ten code sites across both languages: the Core provider,
`ProviderCatalog`, `ToolLocator`, `AlertCopy`, `Marks` and `PanelModel` on each of the three
platforms, `Profiles.swift` and `Providers.swift` on macOS, the fixtures and the i18n catalogues.
See `tasks/candidate-providers.md` for the two that are researched and parked.
