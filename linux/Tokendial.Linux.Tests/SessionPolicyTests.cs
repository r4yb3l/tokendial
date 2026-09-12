using Tokendial.Linux;

namespace Tokendial.Linux.Tests;

public class SessionPolicyTests
{
    [Theory]
    [InlineData("wayland", null, ":0")]
    [InlineData("Wayland", "wayland-0", ":1")]
    [InlineData(null, "wayland-0", ":0")]
    [InlineData("x11", "wayland-0", ":0")]
    [InlineData("wayland", null, null)]
    public void WaylandIsRefusedEvenWithXWayland(string? session, string? wayland, string? display) =>
        Assert.Equal("linux.session.wayland", SessionPolicy.Rejection(session, wayland, display));

    [Theory]
    [InlineData("x11", null, ":0")]
    [InlineData(null, null, ":1")]
    [InlineData("x11", "", "localhost:10.0")]
    public void NativeX11CanStart(string? session, string? wayland, string? display) =>
        Assert.Null(SessionPolicy.Rejection(session, wayland, display));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingDisplayIsExplainedBeforeAvaloniaStarts(string? display) =>
        Assert.Equal("linux.session.noDisplay", SessionPolicy.Rejection("x11", null, display));
}

public class SessionStartupTests
{
    [Theory]
    [InlineData("wayland")]
    [InlineData("x11")]
    public async Task UnsupportedStartupExitsCleanlyWithoutCreatingApplicationData(string session)
    {
        var directory = Path.Combine(Path.GetTempPath(), "tokendial-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            start.Environment.Remove("DISPLAY");
            start.Environment.Remove("WAYLAND_DISPLAY");
            start.Environment["XDG_SESSION_TYPE"] = session;
            start.Environment["XDG_CONFIG_HOME"] = directory;
            using var process = System.Diagnostics.Process.Start(start)!;
            var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            var message = await stderr;
            Assert.Equal(1, process.ExitCode);
            Assert.False(string.IsNullOrWhiteSpace(message));
            Assert.DoesNotContain("XOpenDisplay failed", message);
            Assert.DoesNotContain("Unhandled exception", message);
            Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
