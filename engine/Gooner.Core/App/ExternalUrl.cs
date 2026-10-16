using Gooner.Core.Logging;

namespace Gooner.Core.App;

// Port of utils.open_external_url: hands a URL to the browser, but only if it is one.
//
// The single route out of the app to a web page. A release URL is whatever the GitHub API
// response said, and if that response is ever attacker-influenced, a file:// or custom-scheme URL
// would otherwise be handed to the default Windows handler on a single click. The engine passes
// its own opener (OS.ShellOpen), so the rule is tested without one.
public static class ExternalUrl
{
    private const string Log = "utils";

    public static bool IsWeb(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static void Open(string url, Action<string> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        if (!IsWeb(url))
        {
            AppLog.Warning(Log, "Refusing to open a non-web URL: {0}", url);
            return;
        }
        open(url);
    }
}
