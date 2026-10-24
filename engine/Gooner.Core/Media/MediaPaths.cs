using System.Text.RegularExpressions;

namespace Gooner.Core.Media;

// Which paths of a saved session the app will touch. A session is meant to be handed on, and the
// format is documented so an LLM can write one, so its paths are whatever somebody put there - and
// touching one is not harmless: looking for a file on another machine's share (\\host\share, or
// \\host@80 over WebDAV) opens a connection out that carries the Windows account's credentials, and a
// URL or a scheme like search-ms: handed to the shell runs whatever is registered for it.
public static partial class MediaPaths
{
    // A picture or a clip on a drive of this computer: "C:\..." with a medium's extension, and no
    // stream, scheme or device prefix. What a session from elsewhere may name (SessionFiles validates
    // it on import). A mapped network drive counts - the user mapped it.
    public static bool IsOnThisComputer(string path) =>
        !string.IsNullOrEmpty(path) && OnADrive().IsMatch(path) && IsMedium(path);

    // What the shell may be handed: a medium of the user's on a drive, or on a share as "\\server\share"
    // - their own sessions can come from a media folder on a NAS - and never a URL, a scheme or a
    // device path (\\?\, \\.\).
    public static bool IsAFile(string path) =>
        !string.IsNullOrEmpty(path) && (OnADrive().IsMatch(path) || OnAShare().IsMatch(path)) && IsMedium(path);

    private static bool IsMedium(string path) =>
        path.IndexOfAny(['\0', '\r', '\n']) < 0 && MediaKinds.Of(path) != MediaKind.Unknown;

    // A drive letter, a separator, and no colon after it (no alternate data stream, no scheme).
    [GeneratedRegex(@"^[A-Za-z]:[\\/][^:]*$")]
    private static partial Regex OnADrive();

    // \\server\share\..., where the server is a name and not "?" or "." (the device namespaces).
    [GeneratedRegex(@"^\\\\[^\\/?.:][^\\/:]*\\[^\\/:]+\\[^:]*$")]
    private static partial Regex OnAShare();
}
