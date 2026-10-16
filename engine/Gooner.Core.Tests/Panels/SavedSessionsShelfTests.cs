using System.Text.Json.Nodes;
using Gooner.Core.Data;
using Gooner.Core.Panels;
using Gooner.Core.Sessions;

namespace Gooner.Core.Tests.Panels;

// Port of tests/test_saved_sessions_dialog.py: the shelf of saved sessions - list them, play one
// again, hand one on, throw one away - without its widgets. What the dialog asked in a message box
// (the missing files, the paths, the deletion) the panel asks; here it is what the shelf proposes
// and what it does with the answer.
public sealed class SavedSessionsShelfTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-shelf-");
    private readonly UserDataStore _store;
    private readonly List<(JsonObject Session, bool IgnorePaths)> _replays = [];
    private bool _replayResult = true;

    public SavedSessionsShelfTests() => _store = new UserDataStore(Path.Combine(_tmp.FullName, "appdata"));

    public void Dispose() => _tmp.Delete(recursive: true);

    private static JsonObject Saved(string outcome = "real", JsonArray? media = null, string savedAt = "2026-09-01 20:00") => new()
    {
        ["format"] = SessionFiles.FormatVersion,
        ["app_version"] = "0.12.0",
        ["saved_at"] = savedAt,
        ["duration_sec"] = 120.0,
        ["segments"] = new JsonArray(new JsonObject { ["kind"] = "beat", ["pattern"] = "Standard Beat", ["freq"] = 2.0, ["duration_sec"] = 120.0 }),
        ["custom_patterns"] = new JsonObject(),
        ["climax"] = new JsonObject { ["at_sec"] = 110.0, ["outcome"] = outcome },
        ["fake_climaxes"] = new JsonArray(),
        ["media"] = media ?? new JsonArray(new JsonObject { ["at_sec"] = 0.0 }),
    };

    private JsonArray WithFiles(int count = 2, int missing = 0)
    {
        var entries = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            var path = Path.Combine(_tmp.FullName, $"m{i}.png");
            File.WriteAllBytes(path, [1]);
            entries.Add(new JsonObject { ["at_sec"] = i * 30.0, ["path"] = path });
        }
        for (var i = 0; i < missing; i++)
            entries.Add(new JsonObject { ["at_sec"] = 90.0 + i, ["path"] = Path.Combine(_tmp.FullName, $"gone{i}.png") });
        return entries;
    }

    private SavedSessionsShelf Shelf(params JsonObject[] sessions)
    {
        foreach (var session in sessions) SessionFiles.StoreSession(_store, session);
        return new SavedSessionsShelf(_store, (session, ignore) =>
        {
            _replays.Add((session, ignore));
            return _replayResult;
        });
    }

    // --- the list ---

    // A shelf of fifty is scrolled from the top, and the one you want is almost always the one you
    // just played.
    [Fact]
    public void the_newest_session_is_at_the_top()
    {
        var shelf = Shelf(Saved(savedAt: "first"), Saved(savedAt: "second"));

        Assert.Equal(2, shelf.Rows.Count);
        Assert.Contains("second", shelf.Rows[0]);
        Assert.Contains("first", shelf.Rows[1]);
    }

    [Fact]
    public void a_session_is_described_rather_than_numbered() =>
        Assert.Contains("Ruined", Shelf(Saved(outcome: "ruined")).Rows[0]);

    [Fact]
    public void an_empty_shelf_says_so_and_offers_nothing_to_press()
    {
        var shelf = Shelf();

        Assert.Empty(shelf.Rows);
        Assert.True(shelf.IsEmpty);
        Assert.False(shelf.CanAct);
    }

    [Fact]
    public void selecting_a_session_enables_what_can_be_done_with_it()
    {
        var shelf = Shelf(Saved());

        shelf.Select(0);

        Assert.True(shelf.CanAct);
    }

    // --- replaying ---

    [Fact]
    public void replaying_uses_the_recorded_files_when_they_are_all_still_there()
    {
        var shelf = Shelf(Saved(media: WithFiles()));
        shelf.Select(0);

        Assert.Equal(new ReplayPlan(IgnorePaths: false, Missing: 0, Total: 2), shelf.PlanReplay());
        Assert.True(shelf.Replay(ignorePaths: false));

        Assert.Single(_replays);
        Assert.False(_replays[0].IgnorePaths);
    }

    // There is nothing to ask about: a stripped session has no files to look for.
    [Fact]
    public void a_session_without_paths_replays_against_your_own_library_without_asking()
    {
        var shelf = Shelf(Saved());
        shelf.Select(0);

        var plan = shelf.PlanReplay()!;

        Assert.True(plan.IgnorePaths);
        Assert.False(plan.MustAsk);
    }

    [Fact]
    public void missing_files_offer_your_own_library_rather_than_failing()
    {
        var shelf = Shelf(Saved(media: WithFiles(count: 2, missing: 1)));
        shelf.Select(0);

        var plan = shelf.PlanReplay()!;

        Assert.True(plan.MustAsk);
        Assert.Equal((1, 3), (plan.Missing, plan.Total));
        Assert.True(plan.IgnorePaths);
        Assert.StartsWith("1 of 3 files this session showed are no longer where they were.", SavedSessionsShelf.MissingFilesQuestion(plan));
    }

    // The one way this fails: own-library mode with no folder loaded.
    [Fact]
    public void a_replay_that_cannot_start_says_why_and_leaves_the_manager_open()
    {
        _replayResult = false;
        var shelf = Shelf(Saved(media: WithFiles()));
        shelf.Select(0);

        Assert.False(shelf.Replay(ignorePaths: false));
    }

    [Fact]
    public void nothing_selected_plans_nothing() => Assert.Null(Shelf(Saved()).PlanReplay());

    // --- exporting ---

    [Fact]
    public void an_export_without_paths_carries_none_of_them()
    {
        var target = Path.Combine(_tmp.FullName, "out.json");
        var shelf = Shelf(Saved(media: WithFiles()));
        shelf.Select(0);

        Assert.True(shelf.Export(target, withPaths: false));

        var written = File.ReadAllText(target);
        Assert.DoesNotContain("m0.png", written);
        Assert.Equal(0.0, (double)JsonNode.Parse(written)!["media"]![0]!["at_sec"]!);
    }

    [Fact]
    public void an_export_with_paths_keeps_them()
    {
        var target = Path.Combine(_tmp.FullName, "out.json");
        var shelf = Shelf(Saved(media: WithFiles()));
        shelf.Select(0);

        shelf.Export(target, withPaths: true);

        Assert.Contains("m0.png", File.ReadAllText(target));
    }

    [Fact]
    public void an_export_that_fails_to_write_says_so()
    {
        var shelf = Shelf(Saved(media: WithFiles()));
        shelf.Select(0);

        Assert.False(shelf.Export(Path.Combine(_tmp.FullName, "no", "such", "dir", "out.json"), withPaths: true));
    }

    // Someone deciding this needs to know it is not only file names - it is their account name and
    // how their collection is laid out.
    [Fact]
    public void the_question_spells_out_what_a_session_with_paths_gives_away()
    {
        var text = SavedSessionsShelf.PathsWarning.ToLowerInvariant();

        Assert.True(text.Contains("user name", StringComparison.Ordinal) || text.Contains("username", StringComparison.Ordinal));
        Assert.Contains("folder", text);
        Assert.True(text.Contains("file name", StringComparison.Ordinal) || text.Contains("filename", StringComparison.Ordinal));
    }

    // --- importing ---

    [Fact]
    public void a_session_file_can_be_brought_in_and_shows_up_on_the_shelf()
    {
        var incoming = Path.Combine(_tmp.FullName, "shared.json");
        SessionFiles.WriteSessionFile(incoming, Saved(savedAt: "from a friend"));
        var shelf = Shelf();

        Assert.Null(shelf.Import(incoming));

        Assert.Single(SessionFiles.LoadSavedSessions(_store));
        Assert.Contains("from a friend", shelf.Rows[0]);
    }

    [Fact]
    public void a_file_that_is_not_a_session_is_refused_with_a_word_rather_than_a_crash()
    {
        var broken = Path.Combine(_tmp.FullName, "broken.json");
        File.WriteAllText(broken, "{not json");

        Assert.NotNull(Shelf().Import(broken));

        Assert.Empty(SessionFiles.LoadSavedSessions(_store));
    }

    // Somebody iterating on a generated file needs the list, not "could not import".
    [Fact]
    public void importing_a_broken_session_names_what_is_wrong_with_it()
    {
        var broken = Saved();
        broken["segments"]![0]!["pattern"] = "Furious Wiggle";
        var incoming = Path.Combine(_tmp.FullName, "generated.json");
        SessionFiles.WriteSessionFile(incoming, broken);

        Assert.Contains("Furious Wiggle", Shelf().Import(incoming));
    }

    // --- deleting ---

    [Fact]
    public void deleting_takes_the_session_off_the_shelf()
    {
        var shelf = Shelf(Saved(savedAt: "keep me"), Saved(savedAt: "drop me"));
        shelf.Select(0); // the newest, "drop me"

        shelf.DeleteSelected();

        Assert.Equal(["keep me"], SessionFiles.LoadSavedSessions(_store).Select(e => (string)e!["saved_at"]!));
        Assert.Single(shelf.Rows);
        Assert.False(shelf.CanAct);
    }

    // The list is built from what the store actually holds, so a deletion in Privacy & Data cannot
    // leave the shelf showing sessions that are gone.
    [Fact]
    public void the_shelf_is_re_read_from_disk_rather_than_trusted_in_memory()
    {
        var shelf = Shelf(Saved());
        _store.Delete(SessionFiles.SavedSessionsKey);

        shelf.Refresh();

        Assert.Empty(shelf.Rows);
    }

    // It replays differently - the recording plays out and then it carries on as a normal session -
    // so the list has to say which kind it is.
    [Fact]
    public void a_session_that_never_climaxed_is_marked_in_the_list()
    {
        var incomplete = Saved();
        incomplete["climax"] = null;

        Assert.Contains("stopped early", Shelf(incomplete).Rows[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void the_manager_explains_what_replaying_an_unfinished_session_does() =>
        Assert.Contains("stopped early", SavedSessionsShelf.Intro, StringComparison.OrdinalIgnoreCase);
}
