using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gooner.Core.App;
using Gooner.Core.Logging;

namespace Gooner.Core.Updates;

public abstract record UpdateResult;

public sealed record UpdateAvailable(string Tag, string Url) : UpdateResult;

public sealed record UpToDate : UpdateResult;

public sealed record CheckFailed(string Message) : UpdateResult;

// Port of src/UpdateChecker.py: one of the app's two network code paths, kept on its own so "what
// does this app send" has one obvious place to audit. The other is the optional device output,
// which is off at every launch and only ever goes to an address the user types in. Nothing else
// opens a connection, and this one runs only when the user asks for it and confirms the question
// in UpdateTexts.Consent.
public sealed class UpdateChecker
{
    public const string ReleasesApiUrl = "https://api.github.com/repos/pohupremmail-svg/AutoShowStroker/releases/latest";

    // Read back by UpdateTexts.Consent, which promises the user exactly this header - named once,
    // so the promise and the request cannot disagree. GitHub's API refuses a request without one.
    public const string UserAgent = "GoonerApp-UpdateChecker";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private const string Log = "UpdateChecker";
    private const string Unreadable = "Couldn't understand GitHub's response.";

    private readonly string _currentVersion;
    private readonly HttpMessageHandler? _handler;

    // `handler` is for tests; the real request goes through the platform's own.
    public UpdateChecker(string currentVersion, HttpMessageHandler? handler = null)
    {
        _currentVersion = currentVersion;
        _handler = handler;
    }

    public async Task<UpdateResult> CheckNowAsync(CancellationToken cancellation = default)
    {
        using var client = _handler is null ? new HttpClient() : new HttpClient(_handler, disposeHandler: false);
        client.Timeout = Timeout;
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApiUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, null));
        AppLog.Info(Log, "Update check: requesting {0}", ReleasesApiUrl);
        try
        {
            using var response = await client.SendAsync(request, cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var reason = $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim();
                AppLog.Warning(Log, "Update check failed: {0}", reason);
                return new CheckFailed(reason);
            }
            return ProcessBody(await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            AppLog.Warning(Log, "Update check failed: {0}", e.Message);
            return new CheckFailed(e.Message);
        }
    }

    internal UpdateResult ProcessBody(byte[] body)
    {
        string tag;
        string url;
        IReadOnlyList<int> latest, current;
        try
        {
            var data = JsonNode.Parse(body) as JsonObject;
            if (data?["tag_name"] is not JsonValue tagValue || tagValue.GetValueKind() != JsonValueKind.String)
                throw new FormatException("no tag_name");
            tag = (string)tagValue!;
            url = data["html_url"] is JsonValue page && page.GetValueKind() == JsonValueKind.String ? (string)page! : "";
            latest = Changelog.ParseVersion(tag.TrimStart('v'));
            current = Changelog.ParseVersion(_currentVersion);
        }
        catch (Exception e) when (e is JsonException or FormatException or OverflowException or ArgumentException)
        {
            AppLog.Warning(Log, "Update check: could not parse GitHub's response");
            return new CheckFailed(Unreadable);
        }
        AppLog.Info(Log, "Update check: latest is {0}, running {1}", tag, _currentVersion);
        return Changelog.Compare(latest, current) > 0 ? new UpdateAvailable(tag, url) : new UpToDate();
    }
}

// Port of src/update_dialogs.py's words: what the user is asked before the check runs, and told
// after. Split out so the question asked before the app touches the network can be found, read and
// audited on its own.
public static class UpdateTexts
{
    // Spells out everything that actually goes over the wire. It used to say "nothing else is
    // sent" flat out, which was not quite true: the request carries a User-Agent naming the app,
    // so GitHub's access logs tie an IP to "runs GoonerApp". Small, but a privacy promise is worth
    // nothing unless it is exact.
    public static string Consent { get; } =
        "This will send one request to GitHub.com to check the latest release version.\n\n" +
        "It carries your IP address (unavoidable for any web request) and a User-Agent of " +
        $"\"{UpdateChecker.UserAgent}\", which identifies the app to GitHub. Nothing " +
        "else is sent - no folders, no filenames, no statistics, nothing identifying you or " +
        "your machine - and this never runs on its own.\n\n" +
        "Continue?";

    public static string Available(string latestTag, string currentVersion) =>
        $"A new version is available: {latestTag} (you're on v{currentVersion}).";

    public static string UpToDate(string currentVersion) => $"You're on the latest version (v{currentVersion}).";

    public static string Failed(string message) => $"Couldn't check for updates:\n{message}";
}
