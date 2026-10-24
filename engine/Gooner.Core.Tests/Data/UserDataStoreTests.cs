using System.Text.Json.Nodes;
using Gooner.Core.Data;

namespace Gooner.Core.Tests.Data;

// Port of tests/test_user_data.py. Its QSettings tests - the data that once lived in the registry,
// the dead keys - are in Migration/ProfileMigrationTests, where the registry import is.
public sealed class UserDataStoreTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-data-");
    private readonly UserDataStore _store;

    public UserDataStoreTests() => _store = new UserDataStore(Path.Combine(_tmp.FullName, "appdata"));

    public void Dispose() => _tmp.Delete(recursive: true);

    private static bool Same(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);

    // --- basic round-trip ---

    [Fact]
    public void load_returns_default_when_nothing_saved()
    {
        Assert.Null(_store.Load("session_history"));
        Assert.True(Same(new JsonArray(), _store.Load("session_history") ?? new JsonArray()));
    }

    [Fact]
    public void save_then_load_round_trips()
    {
        _store.Save("session_history", JsonNode.Parse("""[{"total_num_beat": 42}]"""));
        Assert.True(Same(JsonNode.Parse("""[{"total_num_beat": 42}]"""), _store.Load("session_history")));
    }

    [Fact]
    public void save_creates_the_data_directory()
    {
        Assert.False(Directory.Exists(_store.BaseDir));
        _store.Save("custom_patterns", JsonNode.Parse("""{"Mine": [1, -1]}"""));
        Assert.True(Directory.Exists(_store.BaseDir));
    }

    [Fact]
    public void save_writes_readable_json()
    {
        _store.Save("custom_patterns", JsonNode.Parse("""{"Mine": [1, -1]}"""));
        var raw = File.ReadAllText(_store.PathFor("custom_patterns"));
        Assert.True(Same(JsonNode.Parse("""{"Mine": [1, -1]}"""), JsonNode.Parse(raw)));
    }

    [Fact]
    public void save_leaves_no_temp_file_behind()
    {
        _store.Save("session_history", JsonNode.Parse("[1, 2, 3]"));
        Assert.Empty(Directory.GetFiles(_store.BaseDir, "*.tmp"));
    }

    [Fact]
    public void save_overwrites_previous_content()
    {
        _store.Save("session_history", JsonNode.Parse("""[{"a": 1}]"""));
        _store.Save("session_history", JsonNode.Parse("""[{"b": 2}]"""));
        Assert.True(Same(JsonNode.Parse("""[{"b": 2}]"""), _store.Load("session_history")));
    }

    [Fact]
    public void non_ascii_content_survives_a_round_trip()
    {
        _store.Save("custom_patterns", JsonNode.Parse("""{"Süß  Pattern": [1, -1]}"""));
        Assert.True(Same(JsonNode.Parse("""{"Süß  Pattern": [1, -1]}"""), _store.Load("custom_patterns")));
        // Written as UTF-8 text, not as \u escapes - the file stays readable by hand.
        Assert.Contains("Süß", File.ReadAllText(_store.PathFor("custom_patterns")));
    }

    // --- corrupt data files must never crash the app ---

    [Fact]
    public void corrupt_file_returns_the_default_instead_of_raising()
    {
        _store.Save("session_history", JsonNode.Parse("""[{"a": 1}]"""));
        File.WriteAllText(_store.PathFor("session_history"), "{not valid json");

        Assert.Null(_store.Load("session_history"));
    }

    [Fact]
    public void corrupt_file_is_quarantined()
    {
        _store.Save("session_history", JsonNode.Parse("""[{"a": 1}]"""));
        File.WriteAllText(_store.PathFor("session_history"), "{not valid json");

        _store.Load("session_history");

        Assert.True(File.Exists(Path.ChangeExtension(_store.PathFor("session_history"), ".json.corrupt")));
        Assert.False(File.Exists(_store.PathFor("session_history")));
    }

    [Fact]
    public void saving_after_a_corrupt_file_recovers()
    {
        Directory.CreateDirectory(_store.BaseDir);
        File.WriteAllText(_store.PathFor("session_history"), "garbage");
        _store.Load("session_history");

        _store.Save("session_history", JsonNode.Parse("""[{"fresh": true}]"""));

        Assert.True(Same(JsonNode.Parse("""[{"fresh": true}]"""), _store.Load("session_history")));
    }

    // --- default location ---

    [Fact]
    public void defaults_to_the_non_roaming_location()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.Equal(Path.Combine(local, "GoonerCock", "GoonerApp"), UserDataStore.DefaultBaseDir);
        Assert.True(Path.IsPathRooted(UserDataStore.DefaultBaseDir));
    }

    [Fact]
    public void the_legacy_location_is_the_roaming_one()
    {
        // QStandardPaths' AppDataLocation for GoonerCock/GoonerApp - where the Python app kept its
        // data before it moved it out.
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.Equal(Path.Combine(roaming, "GoonerCock", "GoonerApp"), UserDataStore.LegacyBaseDir);
        Assert.NotEqual(UserDataStore.DefaultBaseDir, UserDataStore.LegacyBaseDir);
    }

    // --- write failures must not take the app down with them ---

    [Fact]
    public void save_returns_true_on_success() => Assert.True(_store.Save("history", JsonNode.Parse("[1, 2, 3]")));

    [Fact]
    public void save_returns_false_instead_of_raising_when_the_write_fails()
    {
        // A directory where the temp file has to go: the write fails the way a lock or a full
        // disk would, without patching anything.
        Directory.CreateDirectory(Path.ChangeExtension(_store.PathFor("history"), ".json.tmp"));

        Assert.False(_store.Save("history", JsonNode.Parse("[1, 2, 3]")));
    }

    [Fact]
    public void a_failed_save_leaves_the_previous_file_intact()
    {
        _store.Save("history", JsonNode.Parse("""["original"]"""));
        Directory.CreateDirectory(Path.ChangeExtension(_store.PathFor("history"), ".json.tmp"));

        Assert.False(_store.Save("history", JsonNode.Parse("""["replacement"]""")));
        Assert.True(Same(JsonNode.Parse("""["original"]"""), _store.Load("history")));
    }

    // --- deleting data on the user's request ---

    [Fact]
    public void delete_removes_the_file()
    {
        _store.Save("history", JsonNode.Parse("[1]"));
        Assert.True(_store.Delete("history"));
        Assert.False(File.Exists(_store.PathFor("history")));
    }

    [Fact]
    public void delete_is_a_noop_when_there_is_nothing_to_remove() => Assert.False(_store.Delete("history"));

    [Fact]
    public void delete_also_removes_corrupt_and_temp_siblings()
    {
        _store.Save("history", JsonNode.Parse("[1]"));
        var path = _store.PathFor("history");
        File.WriteAllText(Path.ChangeExtension(path, ".json.corrupt"), "[]");
        File.WriteAllText(Path.ChangeExtension(path, ".json.tmp"), "[]");

        _store.Delete("history");

        Assert.False(File.Exists(Path.ChangeExtension(path, ".json.corrupt")));
        Assert.False(File.Exists(Path.ChangeExtension(path, ".json.tmp")));
    }

    [Fact]
    public void delete_leaves_other_files_alone()
    {
        _store.Save("history", JsonNode.Parse("[1]"));
        _store.Save("patterns", JsonNode.Parse("""{"a": [1]}"""));

        _store.Delete("history");

        Assert.True(File.Exists(_store.PathFor("patterns")));
    }

    // --- the data directory moved from Roaming to Local ---

    private string Roaming(params (string Name, string Content)[] files)
    {
        var roaming = Directory.CreateDirectory(Path.Combine(_tmp.FullName, "Roaming", "GoonerApp")).FullName;
        foreach (var (name, content) in files)
            File.WriteAllText(Path.Combine(roaming, name), content);
        return roaming;
    }

    [Fact]
    public void data_files_move_out_of_roaming()
    {
        var roaming = Roaming(("session_history.json", """[{"total_dur_sec": 12}]"""));

        _store.MigrateLegacyLocation(roaming);

        Assert.True(Same(JsonNode.Parse("""[{"total_dur_sec": 12}]"""), _store.Load("session_history")));
        Assert.False(File.Exists(Path.Combine(roaming, "session_history.json")));
    }

    [Fact]
    public void roaming_migration_is_idempotent()
    {
        var roaming = Roaming(("session_history.json", "[1]"));

        _store.MigrateLegacyLocation(roaming);
        _store.MigrateLegacyLocation(roaming);

        Assert.True(Same(JsonNode.Parse("[1]"), _store.Load("session_history")));
    }

    [Fact]
    public void roaming_migration_never_clobbers_a_newer_local_file()
    {
        var roaming = Roaming(("session_history.json", """["old"]"""));
        _store.Save("session_history", JsonNode.Parse("""["current"]"""));

        _store.MigrateLegacyLocation(roaming);

        Assert.True(Same(JsonNode.Parse("""["current"]"""), _store.Load("session_history")));
    }

    [Fact]
    public void roaming_migration_is_a_noop_for_an_injected_store()
    {
        // A store a test injects has no legacy location - the host names one only for the real
        // data directory - and must never go hunting for one in the developer's real AppData.
        var moved = _store.MigrateLegacyLocation(null);

        Assert.Equal(0, moved.Moved);
        Assert.False(Directory.Exists(_store.BaseDir));
    }

    [Fact]
    public void the_roaming_folder_goes_once_it_is_empty()
    {
        var roaming = Roaming(("session_history.json", "[1]"), ("custom_patterns.json", "{}"));

        var moved = _store.MigrateLegacyLocation(roaming);

        Assert.Equal(2, moved.Moved);
        Assert.False(Directory.Exists(roaming));
    }

    [Fact]
    public void only_data_files_leave_the_roaming_folder()
    {
        // *.json, as Python globs it: a log or a quarantined file stays, and with it the folder.
        var roaming = Roaming(("session_history.json", "[1]"), ("gooner.log", "x"), ("custom_patterns.json.corrupt", "x"));

        _store.MigrateLegacyLocation(roaming);

        Assert.True(File.Exists(Path.Combine(roaming, "gooner.log")));
        Assert.True(File.Exists(Path.Combine(roaming, "custom_patterns.json.corrupt")));
        Assert.False(File.Exists(Path.Combine(_store.BaseDir, "gooner.log")));
    }

    [Fact]
    public void a_file_that_cannot_move_stops_the_move_and_is_named()
    {
        // A file held open by something else cannot move; nothing after it is attempted - Python
        // returns at the first failure.
        var roaming = Roaming(("a.json", "[1]"), ("b.json", "[2]"));
        using (File.Open(Path.Combine(roaming, "a.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var moved = _store.MigrateLegacyLocation(roaming);

            Assert.Equal(0, moved.Moved);
            Assert.Equal("a.json", moved.FailedFile);
            Assert.NotNull(moved.Error);
        }
        Assert.True(File.Exists(Path.Combine(roaming, "b.json")));
    }
}
