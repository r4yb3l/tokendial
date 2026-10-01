using System.Net;
using System.Text.Json;
using Tokendial.Core.Providers;
using Tokendial.Core.Providers.Antigravity;
using Tokendial.Core.Providers.Claude;
using Tokendial.Core.Providers.Copilot;
using Tokendial.Core.Providers.Grok;
using Tokendial.Core.Store;

namespace Tokendial.Tests;

/// <summary>Credentials go to the host the spec names and nowhere else.</summary>
public class SecurityTests : IDisposable
{
    private readonly string scratch = Path.Combine(Path.GetTempPath(), "tokendial-tests", Guid.NewGuid().ToString("N"));

    public SecurityTests() => Directory.CreateDirectory(scratch);

    public void Dispose() => Directory.Delete(scratch, recursive: true);

    private string Scratch(string name, string content)
    {
        var path = Path.Combine(scratch, name);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class Redirecting : HttpMessageHandler
    {
        public List<Uri> Seen { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen.Add(request.RequestUri!);
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://evil.example/usage");
            return Task.FromResult(response);
        }
    }

    [Fact]
    public void DefaultHandlerNeverFollowsRedirects()
    {
        using var handler = Http.Handler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
    }

    [Fact]
    public async Task ARedirectIsABadResponseNotASecondRequest()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tokendial-sec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var credentials = Path.Combine(directory, ".credentials.json");
            File.WriteAllText(credentials, """{"claudeAiOauth":{"accessToken":"sk-test","expiresAt":4102444800000,"subscriptionType":"pro"}}""");
            var handler = new Redirecting();
            using var provider = new ClaudeProvider(new ClaudeProfile(null, directory), handler, new ReadingArchive(directory), credentials);
            var error = await Assert.ThrowsAsync<UsageError>(() => provider.ReadAsync());
            Assert.Equal(UsageErrorKind.BadResponse, error.Kind);
            Assert.Equal(302, error.Status);
            Assert.Single(handler.Seen);
            Assert.Equal("api.anthropic.com", handler.Seen[0].Host);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void SelfSignedCertificatesAreOnlyAcceptedOnLoopback()
    {
        Assert.True(Bridge.IsLoopback(new Uri("https://127.0.0.1:4321/x")));
        Assert.True(Bridge.IsLoopback(new Uri("https://localhost:4321/x")));
        Assert.True(Bridge.IsLoopback(new Uri("https://[::1]:4321/x")));
        Assert.False(Bridge.IsLoopback(new Uri("https://cloudcode-pa.googleapis.com/x")));
        Assert.False(Bridge.IsLoopback(new Uri("https://127.0.0.1.evil.example/x")));
    }

    /// <summary>
    /// gh keeps one block per host and, since 2.40, every signed-in account's token under users:. The first
    /// oauth_token in the file used to win, which here is an Enterprise host's; only github.com's own token,
    /// the active account's, may reach api.github.com.
    /// </summary>
    [Fact]
    public void OnlyGitHubDotComsActiveTokenLeavesTheGhHostsFile()
    {
        var hosts = Scratch("hosts.yml", """
            ghe.corp.example:
                users:
                    ada-corp:
                        oauth_token: ghe_enterprise
                git_protocol: https
                user: ada-corp
                oauth_token: ghe_enterprise
            github.com:
                users:
                    ada:
                        oauth_token: gho_personal
                    ada-work:
                        oauth_token: gho_work
                git_protocol: https
                user: ada-work
                oauth_token: gho_work
            """);
        var credential = CopilotCredential.FromGhHosts(hosts);
        Assert.NotNull(credential);
        Assert.Equal("gho_work", credential.Token);
        Assert.Equal("ada-work", credential.User);
    }

    /// <summary>With the github.com token in the keyring, the file holds no token for it at all - and the Enterprise one is still not it.</summary>
    [Fact]
    public void AnEnterpriseTokenIsNeverTakenForGitHubDotCom()
    {
        var hosts = Scratch("hosts.yml", """
            ghe.corp.example:
                oauth_token: ghe_enterprise
                user: ada-corp
            github.com:
                users:
                    ada:
                git_protocol: https
                user: ada
            """);
        Assert.Null(CopilotCredential.FromGhHosts(hosts));
    }

    /// <summary>A bare "github.com" prefix also matched github.company.com, an Enterprise host.</summary>
    [Fact]
    public void ACopilotPluginEntryIsGitHubDotComsOnlyWhenItsKeySaysSoWhole()
    {
        Assert.Null(CopilotCredential.FromPluginFile(Scratch("enterprise.json", """{"github.company.com:Iv1.x":{"oauth_token":"ghe_token","user":"ada-corp"}}""")));
        var both = Scratch("apps.json", """{"github.company.com:Iv1.x":{"oauth_token":"ghe_token"},"github.com:Iv1.y":{"oauth_token":"gho_token","user":"ada"}}""");
        Assert.Equal("gho_token", CopilotCredential.FromPluginFile(both)?.Token);
    }

    /// <summary>The issuer is matched whole: a lookalike host is a customer IdP like any other, and its token never reaches the public endpoint.</summary>
    [Fact]
    public void OnlyAuthXaiItselfIsATrustedGrokIssuer()
    {
        using var lookalike = JsonDocument.Parse("""{"https://auth.x.ai.evil.example::cli":{"key":"foreign","expires_at":"2099-01-01T00:00:00Z"}}""");
        Assert.Null(GrokCredential.Pick(lookalike.RootElement, DateTimeOffset.UnixEpoch));
        using var genuine = JsonDocument.Parse("""{"https://auth.x.ai::cli":{"key":"xai-key","expires_at":"2099-01-01T00:00:00Z"}}""");
        Assert.Equal("xai-key", GrokCredential.Pick(genuine.RootElement, DateTimeOffset.UnixEpoch)?.Key);
    }

    /// <summary>
    /// Windsurf and the Codeium extensions run the same language_server with the same --csrf_token flag. The
    /// first one found used to win, and its quota would have been shown as Antigravity's.
    /// </summary>
    [Fact]
    public void TheBridgeIsAntigravitysOwnLanguageServerAndNeverAnotherProducts()
    {
        (int, string) windsurf = (1, "C:/Programs/Windsurf/resources/app/extensions/windsurf/bin/language_server_windows_x64.exe --csrf_token w");
        (int, string) codeium = (2, "/home/ada/.vscode/extensions/codeium.codeium-1.2.3/dist/language_server_linux_x64 --csrf_token c");
        (int, string) unnamed = (3, "/opt/tools/language_server_linux_x64 --csrf_token u");
        (int, string) antigravity = (4, "C:/Programs/Antigravity/resources/app/extensions/antigravity/bin/language_server_windows_x64.exe --csrf_token a");
        Assert.Equal([4, 3], Bridge.Candidates([windsurf, codeium, unnamed, antigravity]).Select(p => p.Pid));
    }
}
