using System.Text.Json.Nodes;
using Gooner.Core.Data;
using Gooner.Core.Localization;
using Gooner.Core.Logging;
using Gooner.Core.Sessions;

namespace Gooner.Core.Panels;

// How a saved session would be replayed: against the user's own library or the recorded files, and
// whether the user has to be asked first because some of those files are gone.
public sealed record ReplayPlan(bool IgnorePaths, int Missing, int Total)
{
    public bool MustAsk => Missing > 0;
}

// Port of src/SavedSessionsDialog.py without its widgets: the shelf of saved sessions - play one
// again, hand one on, throw one away.
//
// Replaying is the point of the whole thing - the same segments, the same climax, the same pacing,
// against the same files or against the user's own. The shelf only decides which of the two, and
// the rule is simple: if the recorded files are still where they were, use them; if they are not,
// offer the user's own library rather than playing a session of empty frames.
//
// It takes the data store and the way to replay, and nothing else - which is what lets it be
// tested without a window.
public sealed class SavedSessionsShelf
{
    private const string Log = "SavedSessionsDialog";

    public static string Intro => L10n.T(
        "A saved session plays again exactly as it went: the same rhythms, the same pauses, the same climax, " +
        "the same pacing. The callouts stay random, so two runs of the same session are still comparable.\n\n" +
        "One marked Stopped early was saved before it ever climaxed - usually because you did not last that long. " +
        "It replays as recorded and then carries on as an ordinary session, so this time you can finish it.");

    public static string EmptyText => L10n.T("Nothing saved yet. Save a session from the Session Explorer at the end of one.");

    // The whole reason exporting asks every time. Deliberately concrete: "includes paths" means
    // nothing to someone who has not thought about what a path contains.
    public static string PathsWarning => L10n.T(
        "A session exported with paths tells whoever opens it your Windows user name, how your collection is " +
        "laid out in folders, and the file name of everything it showed.\n\n" +
        "Without them it still replays every beat, the climax and the pacing - against whatever collection the " +
        "other person has.");

    private readonly UserDataStore _store;
    private readonly Func<JsonObject, bool, bool> _replay;
    private JsonArray _sessions = [];

    // `replay` is GoonerApp.ReplaySession: the session, whether to ignore its paths, whether it started.
    public SavedSessionsShelf(UserDataStore store, Func<JsonObject, bool, bool> replay)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(replay);
        _store = store;
        _replay = replay;
        Refresh();
    }

    // Newest first: the one wanted is almost always the one just played.
    public IReadOnlyList<string> Rows { get; private set; } = [];

    public bool IsEmpty => Rows.Count == 0;

    // The selected row, or null.
    public int? SelectedRow { get; private set; }

    public bool CanAct => SelectedRow is not null;

    // Read every time rather than kept: Privacy & Data can delete the lot while the shelf is open,
    // and a list still offering to replay them would be a lie.
    public void Refresh()
    {
        _sessions = SessionFiles.LoadSavedSessions(_store);
        Rows = [.. Enumerable.Range(0, _sessions.Count).Reverse().Select(i => SessionFiles.Describe((JsonObject)_sessions[i]!))];
        SelectedRow = null;
    }

    public void Select(int row) => SelectedRow = row >= 0 && row < Rows.Count ? row : null;

    // The index into the stored list, which is not the row - the list is reversed.
    private int? SelectedIndex => SelectedRow is { } row ? _sessions.Count - 1 - row : null;

    private JsonObject? Selected => SelectedIndex is { } index ? (JsonObject)_sessions[index]! : null;

    // --- replaying ---

    public ReplayPlan? PlanReplay()
    {
        if (Selected is not { } session) return null;
        var recorded = SessionFiles.RecordedPaths(session);
        // A stripped session: there is nothing to go looking for.
        if (recorded.Count == 0) return new ReplayPlan(IgnorePaths: true, 0, 0);
        var missing = SessionFiles.MissingPaths(session).Count;
        return new ReplayPlan(IgnorePaths: missing > 0, missing, recorded.Count);
    }

    public static string MissingFilesQuestion(ReplayPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return L10n.F("{0} of {1} files this session showed are no longer where they were.\n\n" +
            "Replay it against your own loaded collection instead? The rhythms, the climax and the pacing stay " +
            "exactly the same - only the pictures are yours.", plan.Missing, plan.Total);
    }

    // Whether it started; the one way it does not is own-library mode with nothing loaded.
    public bool Replay(bool ignorePaths) => Selected is { } session && _replay(session, ignorePaths);

    // --- exporting ---

    // Asked every time on purpose, before this is called: the right answer depends on who is getting
    // the file, not on what the user chose last month.
    public bool Export(string target, bool withPaths)
    {
        if (Selected is not { } session) return false;
        var payload = withPaths ? session : SessionFiles.StripPaths(session);
        if (!SessionFiles.WriteSessionFile(target, payload)) return false;
        AppLog.Info(Log, "Session exported (with paths: {0})", withPaths);
        return true;
    }

    // --- importing ---

    // Null when the file is on the shelf; otherwise what is wrong with it, every problem listed -
    // somebody iterating on a generated file needs the list, not "could not import".
    public string? Import(string source)
    {
        JsonObject session;
        try
        {
            session = SessionFiles.ReadSessionFile(source);
        }
        catch (UnsupportedSessionFileException e)
        {
            return e.Message;
        }
        if (!SessionFiles.StoreSession(_store, session))
            return L10n.T("That session could not be saved to your shelf.");
        AppLog.Info(Log, "Session imported");
        Refresh();
        return null;
    }

    // --- deleting ---

    public void DeleteSelected()
    {
        if (SelectedIndex is not { } index) return;
        SessionFiles.DeleteSavedSession(_store, index);
        Refresh();
    }
}
