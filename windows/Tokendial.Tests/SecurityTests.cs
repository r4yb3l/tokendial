using System.Net;
using Tokendial.Core.Providers;
using Tokendial.Core.Providers.Antigravity;
using Tokendial.Core.Providers.Claude;
using Tokendial.Core.Store;

namespace Tokendial.Tests;

/// <summary>Credentials go to the host the spec names and nowhere else.</summary>
public class SecurityTests
{
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
}
