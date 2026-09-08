using System.Globalization;
using System.Text.Json;

namespace Tokendial.Core.Providers;

/// <summary>Lenient reads over JsonElement: a missing or mistyped field is null, never an exception.</summary>
public static class Json
{
    public static JsonDocument? Parse(string text)
    {
        try { return JsonDocument.Parse(text); }
        catch (JsonException) { return null; }
    }

    public static JsonElement? Obj(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    public static IEnumerable<JsonElement> Arr(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

    public static string? Str(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.GetString()) ? v.GetString() : null;

    public static double? Num(this JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        };
    }

    public static bool? Bool(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    public static DateTimeOffset? Date(this JsonElement e, string name) => Iso(e.Str(name));

    public static DateTimeOffset? Iso(string? text) =>
        text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    public static DateTimeOffset? EpochMillis(this JsonElement e, string name) =>
        e.Num(name) is double ms ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms) : null;

    public static DateTimeOffset? EpochSeconds(this JsonElement e, string name) =>
        e.Num(name) is double s ? DateTimeOffset.FromUnixTimeSeconds((long)s) : null;
}

/// <summary>Shared HTTP plumbing: one client per provider, standard status mapping.</summary>
public static class Http
{
    public static HttpClient Client(HttpMessageHandler? handler, TimeSpan? timeout = null)
    {
        var client = handler is null ? new HttpClient(Handler(), disposeHandler: true) : new HttpClient(handler, disposeHandler: false);
        client.Timeout = timeout ?? TimeSpan.FromSeconds(15);
        return client;
    }

    /// <summary>Never follows a redirect: a credential goes to the host the spec names and nowhere else. A 3xx is a bad response.</summary>
    public static HttpClientHandler Handler() => new() { AllowAutoRedirect = false, UseCookies = false, UseProxy = true };

    /// <summary>401/403 mean sign in again; anything outside 2xx that is not handled by the caller, redirects included, is a bad response.</summary>
    public static void ThrowUnlessOk(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (status is 401 or 403) throw UsageError.NeedsSignIn();
        if (status is < 200 or >= 300) throw UsageError.BadResponse(status);
    }

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string Under(params string[] parts) => Path.Combine([Home, .. parts]);
}

/// <summary>A parsed response: the windows and which one the dial means.</summary>
public sealed record Parsed(IReadOnlyList<Model.UsageWindow> Windows, string? Headline, string? Plan = null);
