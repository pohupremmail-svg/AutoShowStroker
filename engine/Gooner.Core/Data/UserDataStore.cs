using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gooner.Core.Logging;

namespace Gooner.Core.Data;

// Port of src/user_data.py - the JSON data files (session history, custom patterns, saved
// sessions, ...), as opposed to settings.
//
// They live in the same folder the Python app uses, %LOCALAPPDATA%\GoonerCock\GoonerApp, so an
// existing install is picked up as it is. Local, not roaming: with folder redirection, a domain
// profile or OneDrive's Known Folder Move, the roaming profile gets copied off the machine, and
// for an app that promises to stay local that is the wrong default.
//
// The base directory is injectable for the same reason it is on the Python side: a test must
// never touch the real user directory.
public sealed class UserDataStore
{
    private const string Log = "user_data";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // Written as UTF-8 text, not as \u escapes - the files stay readable by hand.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string DefaultBaseDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoonerCock", "GoonerApp");

    public string BaseDir { get; }

    public UserDataStore(string? baseDir = null) => BaseDir = baseDir ?? DefaultBaseDir;

    public string PathFor(string name) => Path.Combine(BaseDir, name + ".json");

    // The stored value, or null when there is none - or when the file cannot be read. Unreadable
    // data must never stop the app from starting; the bad file is set aside as .json.corrupt
    // rather than deleted, so it can still be recovered by hand.
    public JsonNode? Load(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Quarantine(path);
            return null;
        }
    }

    // Writes atomically - a crash mid-write can never leave a half-written data file, which,
    // unlike a settings value, would be actual lost user history. Returns whether the write
    // landed and never throws: the main caller is the end of a session, and a full disk or an
    // antivirus lock on the temp file must not cost the user the session and its recap.
    public bool Save(string name, JsonNode? payload)
    {
        var path = PathFor(name);
        var tmp = Path.ChangeExtension(path, ".json.tmp");
        try
        {
            Directory.CreateDirectory(BaseDir);
            File.WriteAllText(tmp, payload is null ? "null" : payload.ToJsonString(WriteOptions));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Error(Log, "Could not save {0}: {1}", name, e.Message);
            return false;
        }
    }

    // Removes a data file at the user's request; returns whether anything was there. Takes the
    // .corrupt and .tmp siblings with it - both hold the same content, and leaving one behind
    // would make "delete my data" untrue.
    public bool Delete(string name)
    {
        var path = PathFor(name);
        var removed = false;
        foreach (var candidate in new[] { path, Path.ChangeExtension(path, ".json.corrupt"), Path.ChangeExtension(path, ".json.tmp") })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                File.Delete(candidate);
                removed = true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                AppLog.Warning(Log, "Could not delete {0}: {1}", Path.GetFileName(candidate), e.Message);
            }
        }
        return removed;
    }

    private static void Quarantine(string path)
    {
        try
        {
            File.Move(path, Path.ChangeExtension(path, ".json.corrupt"), overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing more to do: the caller already falls back to the default.
        }
    }
}
