import Foundation
import TokendialCore

setvbuf(stdout, nil, _IOLBF, 0)
print("tokendial-probe starting; a Keychain prompt may appear on the Mac screen")

/// Reads every provider once and prints what the dial would show. A diagnostic, not part of the app.
let archive = ReadingArchive(directory: FileManager.default.temporaryDirectory.appendingPathComponent("tokendial-probe"))
var providers: [UsageProvider] = ClaudeProfile.discover().map { ClaudeProvider(profile: $0, archive: archive) }
providers += [CodexProvider(archive: archive), CopilotProvider(archive: archive), CursorProvider(), AntigravityProvider(), GlmProvider(archive: archive), GrokProvider(), OpenCodeProvider(archive: archive)]

let semaphore = DispatchSemaphore(value: 0)
Task {
    for provider in providers {
        print("reading \(provider.displayName)…")
        let account = provider.account()?.summary ?? "no credential"
        do {
            let reading = try await provider.read()
            print("\(provider.displayName) [\(account)] → \(reading.headlineText)")
            for window in reading.windows {
                let reset = window.resetsAt.map { " · " + Copy.reset($0, now: Date()) } ?? ""
                print("   \(window.label): \(window.summary(reading.fidelity))\(reset)")
            }
        } catch let error as UsageError {
            print("\(provider.displayName) [\(account)] → \(error.kind) \(error.message)")
        } catch {
            print("\(provider.displayName) [\(account)] → \(error)")
        }
    }
    let sessions = ClaudeSessions.read(ClaudeProfile.default().sessionsDirectory, alive: Liveness.isAlive)
    print("Claude sessions: \(sessions.map { "\($0.name) \($0.state)" })")
    semaphore.signal()
}
semaphore.wait()
