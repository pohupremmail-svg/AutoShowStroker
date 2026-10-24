using System.Text.Json.Nodes;
using Gooner.Core.Callouts;
using Gooner.Core.Data;
using Gooner.Core.Panels;
using Gooner.Core.Settings;

namespace Gooner.Core.Tests.Panels;

// Port of tests/test_custom_phrase_files_dialog.py: the user's own phrase files, added for a
// language and a tone - or every tone - and removed again. Picking the file is the engine's native
// dialog; a cancelled pick never reaches this, so its test has nothing left to hold here.
public sealed class CustomPhraseFilesTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-phrase-files-");
    private readonly CalloutHandler _handler;
    private readonly CustomPhraseFiles _files;

    public CustomPhraseFilesTests()
    {
        var callouts = Path.Combine(_tmp.FullName, "callouts");
        Directory.CreateDirectory(Path.Combine(callouts, "en"));
        foreach (var tone in new[] { "flirty", "shy" })
        {
            var phrases = new JsonObject();
            foreach (var key in CalloutHandler.TriggerKeys) phrases[key] = new JsonArray($"en {tone} {key} phrase");
            File.WriteAllText(Path.Combine(callouts, "en", $"{tone}.json"), phrases.ToJsonString());
        }
        _handler = new CalloutHandler(callouts, new JsonSettingsStore(new UserDataStore(Path.Combine(_tmp.FullName, "settings"))));
        _files = new CustomPhraseFiles(_handler);
    }

    public void Dispose() => _tmp.Delete(recursive: true);

    private string Custom(string name, string json)
    {
        var path = Path.Combine(_tmp.FullName, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void starts_with_empty_list() => Assert.Empty(_files.Rows);

    [Fact]
    public void add_file_loads_it_and_updates_list()
    {
        var path = Custom("custom.json", """{"session_started": ["custom phrase"]}""");

        Assert.True(_files.Add(path, "en", tone: null));

        Assert.Equal(["en flirty session_started phrase", "custom phrase"], _handler.CalloutData["en"]["flirty"]["session_started"]);
        var row = Assert.Single(_files.Rows);
        Assert.Contains("en", row);
        Assert.Contains(path, row);
    }

    [Fact]
    public void add_file_invalid_shows_error_and_does_not_add()
    {
        var path = Custom("bad.json", "{not valid json");

        Assert.False(_files.Add(path, "en", tone: null));

        Assert.NotEqual("", _files.Error);
        Assert.Empty(_files.Rows);
        Assert.Empty(_handler.CustomPhraseFiles);
    }

    // The next try starts without the last one's complaint.
    [Fact]
    public void a_good_file_after_a_bad_one_clears_the_error()
    {
        _files.Add(Custom("bad.json", "{not valid json"), "en", tone: null);

        _files.Add(Custom("good.json", """{"session_started": ["fine"]}"""), "en", tone: null);

        Assert.Equal("", _files.Error);
    }

    [Fact]
    public void remove_selected_unloads_file()
    {
        _files.Add(Custom("custom.json", """{"session_started": ["custom phrase"]}"""), "en", tone: null);

        _files.Remove(0);

        Assert.Empty(_files.Rows);
        Assert.Empty(_handler.CustomPhraseFiles);
        Assert.Equal(["en flirty session_started phrase"], _handler.CalloutData["en"]["flirty"]["session_started"]);
    }

    [Fact]
    public void removing_with_nothing_selected_does_nothing()
    {
        _files.Add(Custom("custom.json", """{"session_started": ["custom phrase"]}"""), "en", tone: null);

        _files.Remove(-1);

        Assert.Single(_files.Rows);
    }

    // --- tone, the second axis next to language ---

    [Fact]
    public void tone_combo_offers_any_tone_first_then_every_tone()
    {
        Assert.Equal([null, .. _handler.AvailableTones], _files.ToneChoices.Select(c => c.Tone));
        Assert.Equal("Any tone", _files.ToneChoices[0].Label);
        Assert.Equal("Flirty", _files.ToneChoices[1].Label);
    }

    // Phrase files predate tones and used to always apply - "Any tone" is that behaviour, and is
    // what stored entries without a tone fall back to.
    [Fact]
    public void a_file_added_for_any_tone_speaks_in_every_tone()
    {
        _files.Add(Custom("custom.json", """{"session_started": ["anywhere"]}"""), "en", tone: null);

        Assert.Null(_handler.CustomPhraseFiles[0].Tone);
        foreach (var tone in new[] { "flirty", "shy" })
            Assert.Contains("anywhere", _handler.CalloutData["en"][tone]["session_started"]);
    }

    [Fact]
    public void a_file_added_for_one_tone_stays_in_that_tone()
    {
        _files.Add(Custom("custom.json", """{"session_started": ["shy only"]}"""), "en", tone: "shy");

        Assert.Equal("shy", _handler.CustomPhraseFiles[0].Tone);
        Assert.Contains("shy only", _handler.CalloutData["en"]["shy"]["session_started"]);
        Assert.DoesNotContain("shy only", _handler.CalloutData["en"]["flirty"]["session_started"]);
    }

    [Fact]
    public void the_list_row_names_the_tone()
    {
        var path = Custom("custom.json", """{"session_started": ["shy only"]}""");

        _files.Add(path, "en", tone: "shy");

        Assert.Equal($"en / Shy: {path}", _files.Rows[0]);
    }
}
