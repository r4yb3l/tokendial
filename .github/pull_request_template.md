## What changes

<!-- One or two sentences on what this does, and why it is worth doing. -->

## How it was verified

<!-- What you ran, and what it said. "It builds" is not a verification; the test
     suite, a screenshot of the window, or the log line that proves the behaviour is. -->

- [ ] `dotnet test windows/Tokendial.slnx` passes (Windows changes)
- [ ] `swift test` in `macos/TokendialCore` passes (macOS changes)
- [ ] Both platforms still read the same specs in `docs/` (spec or provider changes)
- [ ] New copy exists in every catalogue under `docs/i18n` (user-visible strings)

## Anything a reviewer should push back on

<!-- Trade-offs you made, or the part you are least sure about. -->
