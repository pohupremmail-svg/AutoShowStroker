using System.Text.Json.Nodes;
using Gooner.Core.Data;
using Gooner.Core.Sessions;

namespace Gooner.Core.Tests.Sessions;

// Port of tests/test_session_files.py. Turning a recorded session into something that survives
// on disk, and back.
public sealed class SessionFilesTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-sessions-");
    private readonly UserDataStore _dataStore;

    public SessionFilesTests() => _dataStore = new UserDataStore(Path.Combine(_tmp.FullName, "appdata"));

    public void Dispose() => _tmp.Delete(recursive: true);

    private string TmpPath(params string[] parts) => Path.Combine([_tmp.FullName, .. parts]);

    // A recorded session as SessionRecorder hands it over - wall-clock times.
    private static SessionTimeline Timeline(
        double? climaxAt = 5180.0,
        string? climaxOutcome = "ruined",
        Func<List<RecordedSegment>, List<RecordedSegment>>? segments = null)
    {
        var recorded = new List<RecordedSegment>
        {
            new("beat", "Quick Swing", 2.3, 5000.0, 5060.0,
            [
                new(@"C:\pics\a.png", 5000.0, 5030.0, CarriedOver: false),
                new(@"C:\pics\b.png", 5030.0, 5090.0, CarriedOver: false),
            ]),
            new("pause", null, null, 5060.0, 5075.0,
            [
                new(@"C:\pics\b.png", 5030.0, 5090.0, CarriedOver: true),
            ]),
            new("finale", "Build Up", 4.5, 5075.0, 5200.0, []),
        };
        return new SessionTimeline(
            StartedAt: 5000.0,
            EndedAt: 5200.0,
            ClimaxAt: climaxAt,
            ClimaxOutcome: climaxOutcome,
            FakeClimaxes: [5040.0, 5120.0],
            Segments: segments is null ? recorded : segments(recorded));
    }

    private static JsonObject Save(SessionTimeline timeline, Dictionary<string, int[]>? customPatterns = null) =>
        SessionFiles.ToSavedSession(timeline, customPatterns, appVersion: "0.14.3", savedAt: new DateTime(2026, 10, 15, 12, 0, 0));

    private static double Num(JsonNode? node) => node!.GetValue<double>();

    // --- saving ---

    [Fact]
    public void times_are_stored_as_offsets_not_wall_clock()
    {
        var saved = Save(Timeline());

        Assert.Equal(60.0, Num(saved["segments"]![0]!["duration_sec"]), 6);
        Assert.Equal(180.0, Num(saved["climax"]!["at_sec"]), 6);
        Assert.Equal([40.0, 120.0], saved["fake_climaxes"]!.AsArray().Select(Num));
        Assert.Equal(0.0, Num(saved["media"]![0]!["at_sec"]), 6);
    }

    [Fact]
    public void every_segment_keeps_its_rhythm_speed_and_type()
    {
        var segments = Save(Timeline())["segments"]!.AsArray();
        var (first, pause, finale) = (segments[0]!, segments[1]!, segments[2]!);

        Assert.Equal("beat", (string?)first["kind"]);
        Assert.Equal("Quick Swing", (string?)first["pattern"]);
        Assert.Equal(2.3, Num(first["freq"]));
        Assert.Equal("pause", (string?)pause["kind"]);
        Assert.Null(pause["pattern"]);
        Assert.Null(pause["freq"]);
        Assert.Equal("finale", (string?)finale["kind"]);
    }

    [Fact]
    public void media_are_listed_once_in_order()
    {
        var media = Save(Timeline())["media"]!.AsArray();
        Assert.Equal([@"C:\pics\a.png", @"C:\pics\b.png"], media.Select(m => (string?)m!["path"]));
    }

    [Fact]
    public void the_session_duration_is_recorded() => Assert.Equal(200.0, Num(Save(Timeline())["duration_sec"]), 6);

    [Fact]
    public void a_session_without_a_climax_saves_cleanly()
    {
        var saved = Save(Timeline(climaxAt: null, climaxOutcome: null));
        Assert.True(saved.ContainsKey("climax"));
        Assert.Null(saved["climax"]);
    }

    [Fact]
    public void custom_patterns_used_by_the_session_are_carried_along()
    {
        var library = new Dictionary<string, int[]> { ["My Rhythm"] = [1, 2, -1], ["Unused"] = [1] };

        // Only what the session actually used.
        Assert.Empty(Save(Timeline(), library)["custom_patterns"]!.AsObject());

        var used = Timeline(segments: s => [s[0] with { Pattern = "My Rhythm" }, s[1], s[2]]);
        var carried = Save(used, library)["custom_patterns"]!.AsObject();

        Assert.Equal(["My Rhythm"], carried.Select(kv => kv.Key));
        Assert.Equal([1, 2, -1], carried["My Rhythm"]!.AsArray().Select(n => n!.GetValue<int>()));
    }

    [Fact]
    public void the_format_version_and_app_version_are_stamped()
    {
        var saved = Save(Timeline());
        Assert.Equal(SessionFiles.FormatVersion, saved["format"]!.GetValue<int>());
        Assert.False(string.IsNullOrEmpty((string?)saved["app_version"]));
        Assert.Equal("2026-10-15 12:00", (string?)saved["saved_at"]);
    }

    // --- stripping paths ---

    [Fact]
    public void stripping_removes_every_path_but_keeps_the_timing()
    {
        var saved = SessionFiles.StripPaths(Save(Timeline()));
        var media = saved["media"]!.AsArray();

        Assert.All(media, entry => Assert.False(entry!.AsObject().ContainsKey("path")));
        Assert.Equal([0.0, 30.0], media.Select(m => Num(m!["at_sec"])));
        Assert.DoesNotContain(@"C:\", saved.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\\", saved.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void stripping_leaves_the_original_untouched()
    {
        var saved = Save(Timeline());
        SessionFiles.StripPaths(saved);
        Assert.True(saved["media"]![0]!.AsObject().ContainsKey("path"));
    }

    [Fact]
    public void stripping_an_already_stripped_session_is_harmless()
    {
        var once = SessionFiles.StripPaths(Save(Timeline()));
        Assert.True(JsonNode.DeepEquals(once, SessionFiles.StripPaths(once)));
    }

    // --- loading ---

    [Fact]
    public void a_saved_session_parses_back()
    {
        var path = TmpPath("s.json");
        File.WriteAllText(path, Save(Timeline()).ToJsonString());

        var loaded = SessionFiles.ReadSessionFile(path);

        Assert.Equal(["Quick Swing", null, "Build Up"], loaded["segments"]!.AsArray().Select(s => (string?)s!["pattern"]));
        Assert.Equal("ruined", (string?)loaded["climax"]!["outcome"]);
    }

    [Fact]
    public void a_future_format_is_refused_rather_than_half_read()
    {
        var path = TmpPath("s.json");
        var saved = Save(Timeline());
        saved["format"] = SessionFiles.FormatVersion + 1;
        File.WriteAllText(path, saved.ToJsonString());

        Assert.Throws<UnsupportedSessionFileException>(() => SessionFiles.ReadSessionFile(path));
    }

    [Fact]
    public void a_file_that_is_not_a_session_is_refused()
    {
        var path = TmpPath("s.json");
        File.WriteAllText(path, """{"hello": "world"}""");

        Assert.Throws<UnsupportedSessionFileException>(() => SessionFiles.ReadSessionFile(path));
    }

    [Fact]
    public void a_corrupt_file_is_refused_rather_than_raising_json_errors()
    {
        var path = TmpPath("s.json");
        File.WriteAllText(path, "{not json at all");

        Assert.Throws<UnsupportedSessionFileException>(() => SessionFiles.ReadSessionFile(path));
    }

    [Fact]
    public void a_missing_file_is_refused_the_same_way() =>
        Assert.Throws<UnsupportedSessionFileException>(() => SessionFiles.ReadSessionFile(TmpPath("gone.json")));

    // --- labels for the manager ---

    [Fact]
    public void a_session_gets_a_readable_label()
    {
        var label = SessionFiles.Describe(Save(Timeline()));

        Assert.Contains("3:20", label, StringComparison.Ordinal); // 200 seconds
        Assert.Contains("Ruined", label, StringComparison.Ordinal);
    }

    [Fact]
    public void a_session_without_paths_says_so_in_its_label() =>
        Assert.Contains("no media", SessionFiles.Describe(SessionFiles.StripPaths(Save(Timeline()))).ToLowerInvariant(), StringComparison.Ordinal);

    // --- the shelf of saved sessions ---

    [Fact]
    public void a_saved_session_lands_in_the_data_store()
    {
        SessionFiles.StoreSession(_dataStore, Save(Timeline()));

        var stored = SessionFiles.LoadSavedSessions(_dataStore);

        Assert.Single(stored);
        Assert.Equal("ruined", (string?)stored[0]!["climax"]!["outcome"]);
    }

    [Fact]
    public void saved_sessions_keep_their_order()
    {
        foreach (var outcome in new[] { "real", "ruined", "denied" })
            SessionFiles.StoreSession(_dataStore, Save(Timeline(climaxOutcome: outcome)));

        var stored = SessionFiles.LoadSavedSessions(_dataStore);

        Assert.Equal(["real", "ruined", "denied"], stored.Select(e => (string?)e!["climax"]!["outcome"]));
    }

    // A saved session is orders of magnitude bigger than a history entry - a few hundred media
    // paths each - so the cap is much lower than ScoreTracker's.
    [Fact]
    public void the_shelf_is_capped_so_it_cannot_grow_forever()
    {
        for (var index = 0; index < 5; index++)
        {
            var saved = Save(Timeline());
            saved["saved_at"] = $"entry {index}";
            SessionFiles.StoreSession(_dataStore, saved, maxSaved: 3);
        }

        var stored = SessionFiles.LoadSavedSessions(_dataStore);

        Assert.Equal(["entry 2", "entry 3", "entry 4"], stored.Select(e => (string?)e!["saved_at"]));
    }

    [Fact]
    public void a_saved_session_can_be_deleted_again()
    {
        for (var index = 0; index < 3; index++)
        {
            var saved = Save(Timeline());
            saved["saved_at"] = $"entry {index}";
            SessionFiles.StoreSession(_dataStore, saved);
        }

        SessionFiles.DeleteSavedSession(_dataStore, 1);

        Assert.Equal(["entry 0", "entry 2"], SessionFiles.LoadSavedSessions(_dataStore).Select(e => (string?)e!["saved_at"]));
    }

    [Fact]
    public void deleting_something_that_is_not_there_is_harmless()
    {
        SessionFiles.StoreSession(_dataStore, Save(Timeline()));

        Assert.False(SessionFiles.DeleteSavedSession(_dataStore, 7));
        Assert.Single(SessionFiles.LoadSavedSessions(_dataStore));
    }

    [Fact]
    public void a_data_file_that_is_not_a_list_reads_as_an_empty_shelf()
    {
        _dataStore.Save(SessionFiles.SavedSessionsKey, JsonNode.Parse("""{"not": "a list"}"""));

        Assert.Empty(SessionFiles.LoadSavedSessions(_dataStore));
    }

    [Fact]
    public void an_exported_session_reads_back_from_the_file_it_was_written_to()
    {
        var path = TmpPath("out.gooner");

        Assert.True(SessionFiles.WriteSessionFile(path, Save(Timeline())));
        Assert.Equal("ruined", (string?)SessionFiles.ReadSessionFile(path)["climax"]!["outcome"]);
    }

    [Fact]
    public void an_export_that_cannot_be_written_reports_failure_rather_than_raising() =>
        Assert.False(SessionFiles.WriteSessionFile(TmpPath("no", "such", "dir", "o.json"), Save(Timeline())));

    // --- do the recorded files still exist? ---

    [Fact]
    public void the_recorded_paths_come_back_in_the_order_they_were_shown() =>
        Assert.Equal([@"C:\pics\a.png", @"C:\pics\b.png"], SessionFiles.RecordedPaths(Save(Timeline())));

    [Fact]
    public void a_stripped_session_has_no_recorded_paths() =>
        Assert.Empty(SessionFiles.RecordedPaths(SessionFiles.StripPaths(Save(Timeline()))));

    [Fact]
    public void missing_files_are_reported_so_the_replay_can_offer_another_library()
    {
        var there = TmpPath("there.png");
        File.WriteAllBytes(there, [0x78]);
        var gone = TmpPath("gone.png");
        var saved = new JsonObject
        {
            ["format"] = 1,
            ["segments"] = new JsonArray(),
            ["media"] = new JsonArray(
                new JsonObject { ["at_sec"] = 0.0, ["path"] = there },
                new JsonObject { ["at_sec"] = 5.0, ["path"] = gone }),
        };

        Assert.Equal([gone], SessionFiles.MissingPaths(saved));
    }

    // Replaying it does something different from replaying a complete one - it carries on past
    // the recording - so it has to be tellable apart in the list.
    [Fact]
    public void a_session_stopped_before_the_climax_is_marked_as_such() =>
        Assert.Contains("stopped early",
            SessionFiles.Describe(Save(Timeline(climaxAt: null, climaxOutcome: null))).ToLowerInvariant(), StringComparison.Ordinal);

    [Fact]
    public void a_complete_session_is_not_marked_as_stopped_early() =>
        Assert.DoesNotContain("stopped early", SessionFiles.Describe(Save(Timeline())).ToLowerInvariant(), StringComparison.Ordinal);

    // --- planned lengths, not measured ones ---

    // A measured length already carries the overshoot to the next note. Feeding that back to the
    // planner makes the replay overshoot again, so every segment of a replay came out a little
    // longer than the session it was reproducing.
    [Fact]
    public void a_saved_segment_takes_the_length_it_was_planned_to_be()
    {
        var recorded = Timeline(segments: s => [s[0] with { PlannedSec = 58.0 }, s[1], s[2]]); // planned 58, measured 60

        Assert.Equal(58.0, Num(Save(recorded)["segments"]![0]!["duration_sec"]), 6);
    }

    // It is the one segment whose plan was not what happened: the climax holds it open until the
    // session ends, or the user stopped partway through it.
    [Fact]
    public void the_last_segment_keeps_the_length_it_actually_ran()
    {
        var recorded = Timeline(segments: s => s.Select(seg => seg with { PlannedSec = 1.0 }).ToList());

        Assert.Equal(125.0, Num(Save(recorded)["segments"]![2]!["duration_sec"]), 6); // 5075 -> 5200
    }

    [Fact]
    public void a_segment_without_a_planned_length_falls_back_to_what_it_measured() =>
        Assert.Equal(60.0, Num(Save(Timeline())["segments"]![0]!["duration_sec"]), 6);

    // --- refusing a file the app cannot actually play ---

    // A session as an LLM would write one - no media paths, minimal but complete.
    private static JsonObject Handwritten() => JsonNode.Parse("""
        {
          "format": 1,
          "duration_sec": 120.0,
          "segments": [
            {"kind": "beat", "pattern": "Standard Beat", "freq": 2.0, "duration_sec": 60.0},
            {"kind": "pause", "pattern": null, "freq": null, "duration_sec": 10.0},
            {"kind": "finale", "pattern": "Quick Swing", "freq": 4.5, "duration_sec": 50.0}
          ],
          "custom_patterns": {},
          "climax": {"at_sec": 100.0, "outcome": "ruined"},
          "fake_climaxes": [40.0],
          "media": [{"at_sec": 0.0}, {"at_sec": 30.0}]
        }
        """)!.AsObject();

    private static JsonObject Handwritten(string key, JsonNode? value)
    {
        var saved = Handwritten();
        saved[key] = value;
        return saved;
    }

    [Fact]
    public void a_well_formed_handwritten_session_is_accepted() => Assert.Empty(SessionFiles.ValidateSession(Handwritten()));

    // It replays against the reader's own library - an LLM should never have to invent a path,
    // and refusing this would force it to.
    [Fact]
    public void a_session_with_no_media_at_all_is_fine() =>
        Assert.Empty(SessionFiles.ValidateSession(Handwritten("media", new JsonArray())));

    // The mistake an LLM will actually make.
    [Fact]
    public void an_unknown_rhythm_is_refused_by_name()
    {
        var saved = Handwritten();
        saved["segments"]![0]!["pattern"] = "Furious Wiggle";

        Assert.Contains(SessionFiles.ValidateSession(saved), p => p.Contains("Furious Wiggle", StringComparison.Ordinal));
    }

    [Fact]
    public void a_rhythm_the_file_defines_itself_is_accepted()
    {
        var saved = Handwritten("custom_patterns", JsonNode.Parse("""{"Furious Wiggle": [1, 2, -1]}"""));
        saved["segments"]![0]!["pattern"] = "Furious Wiggle";

        Assert.Empty(SessionFiles.ValidateSession(saved));
    }

    [Fact]
    public void a_pattern_definition_has_to_be_playable()
    {
        var saved = Handwritten("custom_patterns", JsonNode.Parse("""{"Broken": [0, 9, "x"]}"""));
        saved["segments"]![0]!["pattern"] = "Broken";

        Assert.NotEmpty(SessionFiles.ValidateSession(saved));
    }

    [Fact]
    public void an_unknown_segment_kind_is_refused()
    {
        var saved = Handwritten();
        saved["segments"]![0]!["kind"] = "crescendo";

        Assert.Contains(SessionFiles.ValidateSession(saved), p => p.Contains("crescendo", StringComparison.Ordinal));
    }

    [Fact]
    public void a_beat_without_a_speed_is_refused()
    {
        var saved = Handwritten();
        saved["segments"]![0]!["freq"] = null;

        Assert.NotEmpty(SessionFiles.ValidateSession(saved));
    }

    // A speed the settings could never be set to is not a session anyone can follow, and at 5000 a
    // note gap is 0 ms - a file like that froze the app. The ceiling is the settings' own.
    [Theory]
    [InlineData(20.5)]
    [InlineData(5000)]
    public void a_beat_faster_than_the_settings_allow_is_refused(double freq)
    {
        var saved = Handwritten();
        saved["segments"]![0]!["freq"] = freq;

        Assert.Contains(SessionFiles.ValidateSession(saved), p => p.Contains("Segment 1", StringComparison.Ordinal) && p.Contains("20", StringComparison.Ordinal));
    }

    [Fact]
    public void the_fastest_beat_the_settings_allow_plays()
    {
        var saved = Handwritten();
        saved["segments"]![0]!["freq"] = 20;

        Assert.Empty(SessionFiles.ValidateSession(saved));
    }

    // A session from somebody else names their files, and only files on this computer's drives are
    // ever looked for. A share on another machine would be contacted the moment the replay looks for
    // its files - with the Windows account's credentials; a URL would be handed to the shell.
    [Theory]
    [InlineData(@"\\host\share\a.png")]
    [InlineData(@"\\host@80\a.png")]
    [InlineData("https://example.org/a.png")]
    [InlineData(@"C:\pics\a.exe")]
    public void a_medium_that_is_not_on_this_computer_is_refused(string path)
    {
        var saved = Handwritten();
        saved["media"]![1]!["path"] = path;

        Assert.Contains(SessionFiles.ValidateSession(saved), p => p.Contains("Media entry 2", StringComparison.Ordinal) && p.Contains(path, StringComparison.Ordinal));
    }

    [Fact]
    public void a_medium_whose_path_is_no_text_is_refused()
    {
        var saved = Handwritten();
        saved["media"]![0]!["path"] = 5;

        Assert.Contains(SessionFiles.ValidateSession(saved), p => p.Contains("Media entry 1", StringComparison.Ordinal));
    }

    [Fact]
    public void a_medium_on_a_drive_of_this_computer_plays()
    {
        var saved = Handwritten();
        saved["media"]![0]!["path"] = @"C:\pics\a.png";
        saved["media"]![1]!["path"] = @"D:\clips\b.mp4";

        Assert.Empty(SessionFiles.ValidateSession(saved));
    }

    // Defence behind the import: a session already on the shelf - carried over from the Python app,
    // say - is not looked for anywhere but on this computer either.
    [Fact]
    public void a_recorded_path_that_is_no_file_counts_as_gone()
    {
        var saved = Handwritten();
        saved["media"]![0]!["path"] = "https://example.org/a.png";
        saved["media"]![1]!["path"] = @"\\?\GLOBALROOT\a.png";

        Assert.Equal(2, SessionFiles.MissingPaths(saved).Count);
    }

    [Fact]
    public void a_segment_without_a_length_is_refused()
    {
        var saved = Handwritten();
        saved["segments"]![0]!.AsObject().Remove("duration_sec");

        Assert.NotEmpty(SessionFiles.ValidateSession(saved));
    }

    [Fact]
    public void a_zero_length_segment_is_refused()
    {
        var saved = Handwritten();
        saved["segments"]![0]!["duration_sec"] = 0;

        Assert.NotEmpty(SessionFiles.ValidateSession(saved));
    }

    // It would simply never fire, and the session would run on with nothing at the end.
    [Fact]
    public void a_climax_past_the_end_of_the_session_is_refused()
    {
        var saved = Handwritten();
        saved["climax"]!["at_sec"] = 999.0;

        Assert.NotEmpty(SessionFiles.ValidateSession(saved));
    }

    [Fact]
    public void an_invented_climax_outcome_is_refused()
    {
        var saved = Handwritten();
        saved["climax"]!["outcome"] = "explosive";

        Assert.Contains(SessionFiles.ValidateSession(saved), p => p.Contains("explosive", StringComparison.Ordinal));
    }

    [Fact]
    public void a_fake_out_past_the_end_is_refused() =>
        Assert.NotEmpty(SessionFiles.ValidateSession(Handwritten("fake_climaxes", new JsonArray(999.0))));

    [Fact]
    public void media_moments_have_to_run_forwards() =>
        Assert.NotEmpty(SessionFiles.ValidateSession(Handwritten("media", JsonNode.Parse("""[{"at_sec": 30.0}, {"at_sec": 10.0}]"""))));

    [Fact]
    public void a_session_with_no_segments_is_refused() =>
        Assert.NotEmpty(SessionFiles.ValidateSession(Handwritten("segments", new JsonArray())));

    // One flat "cannot be replayed" is useless to somebody iterating on a generated file.
    [Fact]
    public void reading_a_broken_file_reports_what_is_actually_wrong()
    {
        var saved = Handwritten();
        saved["segments"]![0]!["pattern"] = "Furious Wiggle";
        var path = TmpPath("bad.json");
        File.WriteAllText(path, saved.ToJsonString());

        var raised = Assert.Throws<UnsupportedSessionFileException>(() => SessionFiles.ReadSessionFile(path));

        Assert.Contains("Furious Wiggle", raised.Message, StringComparison.Ordinal);
    }

    // The serialiser and the validator must not drift apart.
    [Fact]
    public void a_session_the_app_recorded_itself_always_validates() =>
        Assert.Empty(SessionFiles.ValidateSession(Save(Timeline())));
}
