using System.Text.Json.Nodes;
using Gooner.Core.Data;
using Gooner.Core.Media;
using Gooner.Core.Migration;
using Gooner.Core.Panels;
using Gooner.Core.Randomness;
using Gooner.Core.Tests.Support;

namespace Gooner.Core.Tests.Panels;

// Port of tests/test_media_folder_picker_dialog.py: the folders a session plays from, and the
// preview grid of what is in them. What the Qt tests held about its nested event loops - the
// rebuild guard, the busy cursor, the frame-grab budget - has nothing to hold here: the engine
// grabs video stills in the background (VideoThumbnailer). The registry migration of the stored
// folders runs the startup import (ProfileMigration) where Python's dialog migrated on its first read.
public sealed class MediaFolderPickerTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-picker-");
    private readonly UserDataStore _store;

    public MediaFolderPickerTests() => _store = new UserDataStore(Path.Combine(_tmp.FullName, "appdata"));

    public void Dispose() => _tmp.Delete(recursive: true);

    private string Folder(string name, int count, string ext = "png")
    {
        var folder = Path.Combine(_tmp.FullName, name);
        Directory.CreateDirectory(folder);
        for (var i = 0; i < count; i++) File.WriteAllBytes(Path.Combine(folder, $"file{i}.{ext}"), [1]);
        return folder;
    }

    private MediaFolderPicker Picker(IReadOnlyList<string>? initial = null, Func<string, List<string>>? scan = null) =>
        new(_store, new SystemRandomSource(new Random(7)), initial, scan);

    [Fact]
    public void starts_with_no_folders_and_start_disabled()
    {
        var picker = Picker();

        Assert.Empty(picker.Folders);
        Assert.False(picker.CanStart);
    }

    [Fact]
    public void add_folder_appends_and_rescans()
    {
        var folder = Folder("a", 3);
        var picker = Picker();

        Assert.True(picker.Add(folder));

        Assert.Equal([folder], picker.Folders);
        Assert.Equal(3, picker.FilesByFolder[folder].Count);
        Assert.Equal([$"{folder}  (3 files)"], picker.Rows);
        Assert.True(picker.CanStart);
    }

    [Fact]
    public void add_duplicate_folder_is_noop()
    {
        var folder = Folder("a", 1);
        var picker = Picker();

        picker.Add(folder);
        Assert.False(picker.Add(folder));

        Assert.Equal([folder], picker.Folders);
    }

    [Fact]
    public void remove_selected_folder()
    {
        var a = Folder("a", 2);
        var b = Folder("b", 2);
        var picker = Picker([a, b]);

        picker.Remove(0);

        Assert.Equal([b], picker.Folders);
        Assert.DoesNotContain(a, picker.FilesByFolder.Keys);
    }

    [Fact]
    public void start_disabled_when_all_folders_empty() => Assert.False(Picker([Folder("empty", 0)]).CanStart);

    [Fact]
    public void start_enabled_when_any_folder_has_files() => Assert.True(Picker([Folder("empty", 0), Folder("b", 1)]).CanStart);

    [Fact]
    public void a_folder_that_is_gone_says_so()
    {
        var gone = Path.Combine(_tmp.FullName, "gone");

        var picker = Picker([gone]);

        Assert.Equal([$"{gone}  (folder not found)"], picker.Rows);
        Assert.False(picker.CanStart);
    }

    [Fact]
    public void on_start_sets_selected_files_as_union_of_all_folders() =>
        Assert.Equal(5, Picker([Folder("a", 2), Folder("b", 3)]).Start().Count);

    // Absolute paths into the user's collection: user data, kept where Privacy & Data can show and
    // delete them.
    [Fact]
    public void on_start_persists_folders_to_the_data_store()
    {
        var a = Folder("a", 1);

        Picker([a]).Start();

        Assert.Equal([a], ((JsonArray)_store.Load(MediaFolderPicker.StoreKey)!).Select(f => (string)f!));
    }

    [Fact]
    public void reopening_dialog_prefills_persisted_folders()
    {
        var a = Folder("a", 1);
        _store.Save(MediaFolderPicker.StoreKey, new JsonArray(a));

        Assert.Equal([a], Picker().Folders);
    }

    [Fact]
    public void persisted_folders_migrate_out_of_the_registry()
    {
        var a = Folder("a", 1);
        var registry = new FakeLegacySettings().Set("GoonerApp/last_selected_folders", new JsonArray(a).ToJsonString());

        ProfileMigration.Run(_store, registry, roamingDir: null);

        Assert.Equal([a], Picker().Folders);
        Assert.Equal([a], ((JsonArray)_store.Load(MediaFolderPicker.StoreKey)!).Select(f => (string)f!));
        Assert.False(registry.Contains("GoonerApp/last_selected_folders"));
    }

    [Fact]
    public void stored_folders_that_are_not_a_list_are_ignored()
    {
        _store.Save(MediaFolderPicker.StoreKey, new JsonObject { ["a"] = 1 });

        Assert.Empty(Picker().Folders);
    }

    [Fact]
    public void cancel_does_not_persist_folders()
    {
        _ = Picker([Folder("a", 1)]);

        Assert.Null(_store.Load(MediaFolderPicker.StoreKey));
    }

    // --- the preview grid ---

    [Fact]
    public void thumbnail_grid_count_matches_computed_grid_size()
    {
        var picker = Picker([Folder("a", 200)]);

        picker.Resample(24);

        Assert.Equal(24, picker.Thumbnails.Count);
    }

    [Fact]
    public void a_small_collection_fills_what_it_can()
    {
        var picker = Picker([Folder("a", 10)]);

        picker.Resample(24);

        Assert.Equal(10, picker.Thumbnails.Count);
    }

    [Fact]
    public void adjust_thumbnail_count_grows_without_touching_existing_cells()
    {
        var picker = Picker([Folder("a", 200)]);
        picker.Resample(10);
        var original = picker.Thumbnails.ToList();

        picker.Adjust(15);

        Assert.Equal(15, picker.Thumbnails.Count);
        Assert.Equal(original, picker.Thumbnails.Take(10));
        Assert.Equal(15, picker.Thumbnails.Distinct().Count());
    }

    [Fact]
    public void adjust_thumbnail_count_shrinks_by_dropping_from_the_end()
    {
        var picker = Picker([Folder("a", 200)]);
        picker.Resample(15);
        var kept = picker.Thumbnails.Take(10).ToList();

        picker.Adjust(10);

        Assert.Equal(kept, picker.Thumbnails);
    }

    // Adding or removing a folder resamples everything, so the grid shows what is in the new set.
    [Fact]
    public void rescan_always_fully_replaces_thumbnails_even_if_count_unchanged()
    {
        var picker = Picker([Folder("a", 200)]);
        picker.Resample(24);
        var generation = picker.Generation;

        picker.Rescan();

        Assert.True(picker.Generation > generation);
        Assert.Equal(24, picker.Thumbnails.Count);
    }

    [Fact]
    public void a_resize_does_not_count_as_a_new_sample()
    {
        var picker = Picker([Folder("a", 200)]);
        picker.Resample(10);
        var generation = picker.Generation;

        picker.Adjust(15);

        Assert.Equal(generation, picker.Generation);
    }

    [Fact]
    public void video_cap_enforced_on_initial_refresh()
    {
        var picker = Picker([Folder("videos", 50, "mp4"), Folder("images", 50)]);

        picker.Resample(30);

        Assert.True(picker.Thumbnails.Count(IsVideo) <= MediaFolderPicker.MaxVideoThumbnails);
    }

    [Fact]
    public void video_cap_enforced_after_growing_via_resize()
    {
        var picker = Picker([Folder("videos", 50, "mp4"), Folder("images", 50)]);
        picker.Resample(10);

        picker.Adjust(40);

        Assert.True(picker.Thumbnails.Count(IsVideo) <= MediaFolderPicker.MaxVideoThumbnails);
    }

    private static bool IsVideo(string path) => MediaKinds.Of(path) == MediaKind.Video;

    // --- per-folder scan caching ---

    [Fact]
    public void adding_a_folder_does_not_rewalk_previously_scanned_folders()
    {
        var a = Folder("a", 3);
        var b = Folder("b", 2);
        var walked = new List<string>();
        var picker = Picker([a], folder =>
        {
            walked.Add(folder);
            return MediaKinds.FindSupportedFiles(folder);
        });
        walked.Clear();

        picker.Add(b);

        Assert.Equal([b], walked);
        Assert.Equal(3, picker.FilesByFolder[a].Count);
        Assert.Equal(2, picker.FilesByFolder[b].Count);
    }

    [Fact]
    public void removing_a_folder_does_not_rescan_remaining_folders()
    {
        var a = Folder("a", 3);
        var b = Folder("b", 2);
        var walked = new List<string>();
        var picker = Picker([a, b], folder =>
        {
            walked.Add(folder);
            return MediaKinds.FindSupportedFiles(folder);
        });
        walked.Clear();

        picker.Remove(0);

        Assert.Empty(walked);
        Assert.Equal(2, picker.FilesByFolder[b].Count);
    }

    [Fact]
    public void rescan_invalidates_cache_for_a_folder_that_disappeared()
    {
        var a = Folder("a", 3);
        var picker = Picker([a]);
        Assert.Equal(3, picker.FilesByFolder[a].Count);

        Directory.Delete(a, recursive: true);
        picker.Rescan();

        Assert.Empty(picker.FilesByFolder[a]);
    }
}
