# Tokendial for Windows

WPF on .NET 10. Three projects:

| Project | What |
|---|---|
| `Tokendial.Core` | Model, provider adapters, session monitors, polling store, alert engine. No UI. |
| `Tokendial.App` | The capsule window, toasts, tray icon, settings, first run. |
| `Tokendial.Tests` | xunit. Runs every fixture in `docs/fixtures` and every vector in `docs/alerts/vectors`. |

## Build and run

```
dotnet test windows/Tokendial.slnx
dotnet run --project windows/Tokendial.App
```

Publish a single self-contained executable:

```
dotnet publish windows/Tokendial.App -c Release -o windows/dist
```

`windows/dist/Tokendial.exe` runs on Windows 10 1809 or later with no runtime installed.

## Command line

| Switch | Effect on a running instance |
|---|---|
| none | Expand the panel for a few seconds |
| `--settings` | Open the settings window |
| `--test-alert` | Send a sample threshold toast |

## Where it keeps things

Everything lives under `%LOCALAPPDATA%\Tokendial`:

- `settings.json`: panel mode, connected providers, thresholds, alert switches.
- `readings.json`, `backoff.json`: the last good reading per provider and any rate-limit hold.
- `alerts.json`: the alert engine's epochs so a threshold is not repeated after a restart.
- `logs\tokendial.log`: rolling log. Set `TOKENDIAL_DEBUG=1` for request-level lines.

Tokendial only reads the credentials the coding tools store themselves. It never writes or refreshes them.

## Notes

- The capsule window never takes focus and its transparent pixels pass clicks through. Hover is detected by polling the cursor against the capsule's bounds because alpha-zero pixels never receive mouse events.
- Alerts default to Tokendial's own banners: a card in the top-right corner of the work area, filled with the Windows accent colour at 80 %, white text when apps are dark and near-black when they are light, eight seconds on screen, longer under the cursor, click to expand the panel. They are ordinary windows, so Do not disturb never hides them. Settings can switch to Windows notifications (through `Microsoft.Toolkit.Uwp.Notifications`, kept in the notification centre) or to Windows first with banners only when Windows is silenced.
- The system's reduce-motion setting turns every spring into an instant change.
