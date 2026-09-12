using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Tokendial.Core.Diagnostics;
using Tokendial.Core.I18n;
using Tokendial.Core.Model;
using Tokendial.Core.Providers.Google;
using Tokendial.Core.Store;

namespace Tokendial.Core.Providers.Antigravity;

/// <summary>Generic credentials from the Windows Credential Manager. Reads never prompt.</summary>
public static class CredentialManager
{
    public sealed record Entry(string Target, string? User, byte[] Blob, DateTimeOffset LastWritten);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags, Type;
        public IntPtr TargetName, Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes, TargetAlias, UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredEnumerateW(string? filter, uint flags, out uint count, out IntPtr credentials);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr buffer);

    public static Entry? Read(string target)
    {
        if (!OperatingSystem.IsWindows() || !CredReadW(target, 1, 0, out var pointer)) return null;
        try { return Convert(Marshal.PtrToStructure<CREDENTIAL>(pointer)); }
        finally { CredFree(pointer); }
    }

    public static IReadOnlyList<Entry> All()
    {
        if (!OperatingSystem.IsWindows() || !CredEnumerateW(null, 1, out var count, out var array)) return [];
        try
        {
            var result = new List<Entry>();
            for (var i = 0; i < count; i++)
            {
                var c = Marshal.PtrToStructure<CREDENTIAL>(Marshal.ReadIntPtr(array, i * IntPtr.Size));
                if (c.Type == 1) result.Add(Convert(c));
            }
            return result;
        }
        finally { CredFree(array); }
    }

    private static Entry Convert(CREDENTIAL c)
    {
        var blob = new byte[c.BlobSize];
        if (c.BlobSize > 0) Marshal.Copy(c.Blob, blob, 0, blob.Length);
        var written = ((long)c.LastWritten.dwHighDateTime << 32) | (uint)c.LastWritten.dwLowDateTime;
        return new Entry(Marshal.PtrToStringUni(c.TargetName) ?? "", c.UserName == IntPtr.Zero ? null : Marshal.PtrToStringUni(c.UserName), blob,
            written > 0 ? DateTimeOffset.FromFileTime(written) : DateTimeOffset.MinValue);
    }
}

/// <summary>The Google sign-in Antigravity keeps through Go's keyring: target gemini:antigravity, JSON blob, optional base64 wrapper.</summary>
public sealed record AntigravityCredential(string AuthMethod, string AccessToken, DateTimeOffset ExpiresAt)
{
    private const string Prefix = "go-keyring-base64:";

    public bool Expired(DateTimeOffset now) => ExpiresAt <= now;

    public static CredentialManager.Entry? Find() =>
        CredentialManager.Read("gemini:antigravity") ?? CredentialManager.Read("gemini")
        ?? CredentialManager.All().Where(e => e.Target.Contains("gemini", StringComparison.OrdinalIgnoreCase) && (e.User == "antigravity" || e.Target.Contains("antigravity", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e => e.LastWritten).FirstOrDefault();

    public static AntigravityCredential Read()
    {
        CredentialManager.Entry? entry;
        try { entry = Find(); }
        catch (Exception e) { Log.Usage.Error($"credential manager: {e.Message}"); throw UsageError.CredentialExpired(); }
        if (entry is null) throw UsageError.NeedsSignIn();
        return Decode(entry.Blob) ?? throw UsageError.NeedsSignIn();
    }

    public static AntigravityCredential? Decode(byte[] blob)
    {
        if (blob.Length == 0) return null;
        var text = blob.Length % 2 == 0 && blob.Length >= 2 && blob[1] == 0 ? Encoding.Unicode.GetString(blob) : Encoding.UTF8.GetString(blob);
        return Decode(text.Trim('\0', ' ', '\r', '\n'));
    }

    public static AntigravityCredential? Decode(string text)
    {
        if (text.StartsWith(Prefix, StringComparison.Ordinal)) text = text[Prefix.Length..];
        if (FromJson(text) is AntigravityCredential direct) return direct;
        try { return FromJson(Encoding.UTF8.GetString(Convert.FromBase64String(text))); }
        catch (FormatException) { return null; }
    }

    private static AntigravityCredential? FromJson(string json)
    {
        using var document = Json.Parse(json);
        var token = document?.RootElement.Obj("token");
        var access = token?.Str("access_token");
        if (access is null) return null;
        return new AntigravityCredential(document!.RootElement.Str("auth_method") ?? "", access, token!.Value.Date("expiry") ?? DateTimeOffset.MinValue);
    }
}

/// <summary>Antigravity's language server: found through the process table, spoken to over loopback HTTPS with its CSRF token.</summary>
public sealed record Bridge(IReadOnlyList<int> Ports, string Csrf)
{
    public const string Path = "/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary";

    public static Bridge? Discover()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            foreach (var (pid, commandLine) in Processes())
            {
                var csrf = CsrfToken(commandLine);
                var ports = csrf is null ? [] : Tcp.ListeningPorts(pid);
                if (csrf is not null && ports.Count > 0) return new Bridge(ports, csrf);
            }
        }
        catch (Exception e) { Log.Usage.Debug($"antigravity discovery: {e.Message}"); }
        return null;
    }

    public static string? CsrfToken(string commandLine)
    {
        var parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++) if (parts[i] == "--csrf_token") return parts[i + 1].Trim('"');
        return null;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IEnumerable<(int Pid, string CommandLine)> Processes()
    {
        using var searcher = new System.Management.ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name LIKE 'language_server%'");
        using var results = searcher.Get();
        foreach (var item in results)
        {
            using (item)
            {
                if (item["CommandLine"] is string line && line.Contains("--csrf_token", StringComparison.Ordinal)) yield return (System.Convert.ToInt32(item["ProcessId"]), line);
            }
        }
    }

    /// <summary>Two ports open and only one serves the RPC: try each; forceRefresh or the server answers from its cache.</summary>
    public async Task<IReadOnlyList<UsageWindow>> QuotaAsync(HttpClient http, CancellationToken cancellationToken)
    {
        Exception? last = null;
        foreach (var port in Ports)
        {
            try
            {
                var target = new Uri($"https://127.0.0.1:{port}{Path}");
                if (!IsLoopback(target)) throw new InvalidOperationException("bridge target is not loopback");
                using var request = new HttpRequestMessage(HttpMethod.Post, target) { Content = new StringContent("{\"forceRefresh\":true}", Encoding.UTF8, "application/json") };
                request.Headers.TryAddWithoutValidation("x-codeium-csrf-token", Csrf);
                using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw UsageError.BadResponse((int)response.StatusCode);
                var windows = ParseQuota(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                if (windows.Count > 0) return windows;
            }
            catch (Exception e) { last = e; }
        }
        if (last is not null) throw last;
        return [];
    }

    /// <summary>The server reports what remains; the dial shows what is used. The group names the models; the bucket only says "Weekly Limit Remaining".</summary>
    public static IReadOnlyList<UsageWindow> ParseQuota(string body)
    {
        using var document = Json.Parse(body);
        var windows = new List<UsageWindow>();
        foreach (var group in document?.RootElement.Obj("response")?.Arr("groups") ?? [])
        {
            var groupName = group.Str("displayName");
            foreach (var bucket in group.Arr("buckets"))
            {
                if (bucket.Num("remainingFraction") is not double remaining || remaining < 0 || remaining > 1) continue;
                windows.Add(new UsageWindow(bucket.Str("bucketId") ?? groupName ?? "quota", groupName ?? bucket.Str("displayName") ?? "Usage", 1 - remaining, ResetsAt: bucket.Date("resetTime")));
            }
        }
        return windows;
    }

    /// <summary>Only ever talks to loopback: the self-signed certificate is accepted there and nowhere else, and redirects are refused.</summary>
    public static HttpClient LoopbackClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        ServerCertificateCustomValidationCallback = (request, _, _, errors) => errors == SslPolicyErrors.None || IsLoopback(request.RequestUri)
    }) { Timeout = TimeSpan.FromSeconds(10) };

    public static bool IsLoopback(Uri? uri) => uri is not null && (uri.Host is "localhost" || (System.Net.IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && System.Net.IPAddress.IsLoopback(ip)));
}

/// <summary>Listening TCP ports by owning pid, IPv4 and IPv6.</summary>
public static class Tcp
{
    [DllImport("iphlpapi.dll", SetLastError = true)] private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

    public static IReadOnlyList<int> ListeningPorts(int pid)
    {
        if (!OperatingSystem.IsWindows()) return [];
        var ports = new SortedSet<int>();
        foreach (var p in Read(2, pid, 24, 8, 20)) ports.Add(p);
        foreach (var p in Read(23, pid, 56, 20, 52)) ports.Add(p);
        return ports.ToList();
    }

    private static IEnumerable<int> Read(int family, int pid, int rowSize, int portOffset, int pidOffset)
    {
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 3, 0);
        if (size <= 0) yield break;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, family, 3, 0) != 0) yield break;
            var count = Marshal.ReadInt32(buffer);
            for (var i = 0; i < count; i++)
            {
                var row = buffer + 4 + i * rowSize;
                if (Marshal.ReadInt32(row + pidOffset) != pid) continue;
                var raw = (uint)Marshal.ReadInt32(row + portOffset);
                yield return (int)(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}

/// <summary>A plain count of model requests today from Antigravity's transcripts, for accounts whose quota nothing publishes.</summary>
public static class AntigravityTranscripts
{
    public static string DefaultRoot => Http.Under(".gemini", "antigravity", "brain");
    public static string TranscriptOf(string trajectory) => Path.Combine(trajectory, ".system_generated", "logs", "transcript.jsonl");

    public static int RequestsToday(string? root, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        root ??= DefaultRoot;
        zone ??= TimeZoneInfo.Local;
        if (!Directory.Exists(root)) return 0;
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        var count = 0;
        foreach (var trajectory in Directory.EnumerateDirectories(root))
        {
            var transcript = TranscriptOf(trajectory);
            if (!File.Exists(transcript)) continue;
            string[] lines;
            try { lines = File.ReadAllLines(transcript); } catch (IOException) { continue; }
            foreach (var line in lines)
            {
                using var document = Json.Parse(line);
                if (document?.RootElement.Str("source") != "MODEL") continue;
                if (document.RootElement.Date("created_at") is DateTimeOffset at && TimeZoneInfo.ConvertTime(at, zone).Date == today) count++;
            }
        }
        return count;
    }
}

/// <summary>Language server first, Google's quota endpoint second, a request count last. Id stays "antigravity".</summary>
public sealed class AntigravityProvider : IUsageProvider, IDisposable
{
    private readonly HttpClient google;
    private readonly HttpClient local;
    private readonly Func<AntigravityCredential> read;
    private readonly Func<Bridge?> discover;
    private readonly Func<int> requestsToday;
    private readonly Func<DateTimeOffset> now;
    private AntigravityCredential? held;
    private Bridge? bridge;
    private bool everBridged;

    public AntigravityProvider(HttpMessageHandler? googleHandler = null, HttpMessageHandler? localHandler = null, Func<AntigravityCredential>? read = null,
        Func<Bridge?>? discover = null, Func<int>? requestsToday = null, Func<DateTimeOffset>? now = null)
    {
        google = Http.Client(googleHandler);
        local = localHandler is null ? Bridge.LoopbackClient() : Http.Client(localHandler, TimeSpan.FromSeconds(10));
        this.read = read ?? AntigravityCredential.Read;
        this.discover = discover ?? Bridge.Discover;
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.requestsToday = requestsToday ?? (() => AntigravityTranscripts.RequestsToday(null, this.now()));
    }

    public string Id => "antigravity";
    public string DisplayName => "Antigravity";
    public SignInRoute SignIn => new SignInRoute.OpenApp("antigravity", "Antigravity");
    public void ForgetCredential() => held = null;

    public ProviderAccount? Account()
    {
        if (!Platforms.SupportedHere(Id)) return null;
        try
        {
            var c = held ?? read();
            return new ProviderAccount(null, c.AuthMethod == "consumer" ? "Personal" : (c.AuthMethod.Length == 0 ? null : c.AuthMethod), "Antigravity", new Uri("https://antigravity.google"));
        }
        catch (Exception) { return null; }
    }

    public async Task<ProviderReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        // The spec declares where a reading is possible. On Linux the token lives in the session keyring
        // behind org.freedesktop.secrets and nothing here reads one, so the credential would simply come back
        // empty and this would report "sign in" to somebody who is signed in. Sessions still work: they are
        // plain files, and AntigravitySessions reads them whatever this says.
        if (!Platforms.SupportedHere(Id)) throw UsageError.NothingMetered(Strings.T("status.notOnThisSystem"));

        var credential = held ??= read();
        if (credential.Expired(now())) { held = null; throw UsageError.CredentialExpired(); }
        await PassGate(credential, cancellationToken).ConfigureAwait(false);

        var bridged = await LocalQuota(cancellationToken).ConfigureAwait(false);
        if (bridged.Count > 0)
        {
            everBridged = true;
            return new ProviderReading(Id, DisplayName, Fidelity.Official, ReadingStatus.LiveNow, bridged, "gemini-weekly");
        }
        if (everBridged) throw UsageError.CredentialExpired();

        var quota = await GoogleQuota(credential, cancellationToken).ConfigureAwait(false);
        if (quota.Count > 0) return new ProviderReading(Id, DisplayName, Fidelity.Official, ReadingStatus.LiveNow, quota);

        return new ProviderReading(Id, DisplayName, Fidelity.Derived, ReadingStatus.LiveNow,
            [new UsageWindow("requests", "Requests today · no limit published", Count: requestsToday())]);
    }

    private async Task PassGate(AntigravityCredential credential, CancellationToken cancellationToken)
    {
        var (status, _, response) = await CodeAssist.PostAsync(google, CodeAssist.Gate, credential.AccessToken, "{\"metadata\":{\"pluginType\":\"GEMINI\"}}", cancellationToken).ConfigureAwait(false);
        using (response)
        {
            Log.Usage.Debug($"antigravity: gate {status}");
            if (status == 401) { held = null; throw UsageError.NeedsSignIn(); }
            if (status == 403) throw UsageError.NeedsSignIn();
            if (status == 429) throw UsageError.RateLimited(RetryAfterHeader.From(response, now()) ?? TimeSpan.Zero);
            if (status != 200) throw UsageError.BadResponse(status);
        }
    }

    private async Task<IReadOnlyList<UsageWindow>> LocalQuota(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            bridge ??= discover();
            if (bridge is null) return [];
            try
            {
                var windows = await bridge.QuotaAsync(local, cancellationToken).ConfigureAwait(false);
                if (windows.Count > 0) return windows;
            }
            catch (Exception e) { Log.Usage.Debug($"antigravity: bridge {e.Message}"); }
            bridge = null;
        }
        return [];
    }

    private async Task<IReadOnlyList<UsageWindow>> GoogleQuota(AntigravityCredential credential, CancellationToken cancellationToken)
    {
        var (status, body, response) = await CodeAssist.PostAsync(google, CodeAssist.QuotaSummary, credential.AccessToken, "{}", cancellationToken).ConfigureAwait(false);
        response.Dispose();
        Log.Usage.Debug($"antigravity: quota {status}");
        return status == 200 ? ParseGoogleQuota(body) : [];
    }

    /// <summary>The quota-summary shape, parsed by the shared Code Assist reader.</summary>
    public static IReadOnlyList<UsageWindow> ParseGoogleQuota(string body) => CodeAssist.ParseQuotaSummary(body);

    public void Dispose()
    {
        google.Dispose();
        local.Dispose();
    }
}
