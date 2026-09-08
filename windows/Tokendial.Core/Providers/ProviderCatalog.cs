using Tokendial.Core.Providers.Antigravity;
using Tokendial.Core.Providers.Claude;
using Tokendial.Core.Providers.Codex;
using Tokendial.Core.Providers.Copilot;
using Tokendial.Core.Providers.Cursor;
using Tokendial.Core.Providers.Glm;
using Tokendial.Core.Providers.Grok;
using Tokendial.Core.Providers.OpenCode;
using Tokendial.Core.Sessions;
using Tokendial.Core.Store;

namespace Tokendial.Core.Providers;

/// <summary>The seven providers and five monitors this build knows, in display order: one Claude provider per discovered profile.</summary>
public static class ProviderCatalog
{
    public static readonly IReadOnlyList<string> Order = ["claude", "codex", "copilot", "cursor", "antigravity", "glm", "grok", "opencode"];

    public static IReadOnlyList<IUsageProvider> Providers(ReadingArchive archive)
    {
        var list = new List<IUsageProvider>();
        foreach (var profile in ClaudeProfile.Discover()) list.Add(new ClaudeProvider(profile, archive: archive));
        list.Add(new CodexProvider(archive: archive));
        list.Add(new CopilotProvider(archive: archive));
        list.Add(new CursorProvider());
        list.Add(new AntigravityProvider());
        list.Add(new GlmProvider(archive: archive));
        list.Add(new GrokProvider());
        list.Add(new OpenCodeProvider(archive: archive));
        return list;
    }

    public static IReadOnlyList<IActivityMonitor> Monitors()
    {
        var list = new List<IActivityMonitor>();
        foreach (var profile in ClaudeProfile.Discover()) list.Add(new ClaudeSessions(profile.Id, profile.SessionsDirectory));
        list.Add(new CursorSessions());
        list.Add(new CodexSessions());
        list.Add(new AntigravitySessions());
        list.Add(new GrokSessions());
        return list;
    }
}
