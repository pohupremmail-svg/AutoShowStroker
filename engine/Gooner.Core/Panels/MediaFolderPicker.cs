using System.Text.Json.Nodes;
using Gooner.Core.Data;
using Gooner.Core.Media;
using Gooner.Core.Randomness;

namespace Gooner.Core.Panels;

// Port of src/MediaFolderPickerDialog.py without its widgets: the folders a session plays from,
// and the preview grid of what is in them.
//
// A folder is walked once, when it is added; adding or removing another never walks the rest
// again, however large the library - which also means a change inside an added folder shows only
// once it is removed and added again, a fair price for a dialog opened fresh each time. A folder
// that has gone since is emptied rather than kept.
//
// The folders are remembered on Start, in the data store - absolute paths into the user's
// collection are data, which Privacy & Data shows and deletes - and never on Cancel.
public sealed class MediaFolderPicker
{
    public const string StoreKey = "last_selected_folders";

    // Videos are far heavier to show in a cell than images or GIFs, so the grid never holds more
    // than this many of them, however large it is.
    public const int MaxVideoThumbnails = 4;

    private readonly UserDataStore? _store;
    private readonly IRandomSource _random;
    private readonly Func<string, List<string>> _scan;
    private readonly List<string> _folders;
    private readonly Dictionary<string, List<string>> _files = [];
    private readonly List<string> _thumbnails = [];
    private int _gridCount;

    // `initialFolders` null reads the stored ones; `scan` walks one folder (MediaKinds.FindSupportedFiles).
    public MediaFolderPicker(UserDataStore? store, IRandomSource random, IReadOnlyList<string>? initialFolders = null,
        Func<string, List<string>>? scan = null)
    {
        ArgumentNullException.ThrowIfNull(random);
        _store = store;
        _random = random;
        _scan = scan ?? MediaKinds.FindSupportedFiles;
        _folders = initialFolders is not null ? [.. initialFolders] : Stored();
        Rescan();
    }

    public IReadOnlyList<string> Folders => _folders;

    public IReadOnlyDictionary<string, List<string>> FilesByFolder => _files;

    // What the folder list says about each folder.
    public IReadOnlyList<string> Rows =>
        [.. _folders.Select(f => Directory.Exists(f) ? $"{f}  ({_files[f].Count} files)" : $"{f}  (folder not found)")];

    public bool CanStart => _files.Values.Any(files => files.Count > 0);

    // The files the grid shows, in its order.
    public IReadOnlyList<string> Thumbnails => _thumbnails;

    // Counts up on every full resample: the grid builds every cell again, rather than only the ones
    // a resize added.
    public int Generation { get; private set; }

    private List<string> Stored() =>
        _store?.Load(StoreKey) is JsonArray stored
            ? [.. stored.OfType<JsonValue>().Select(v => (string?)v).OfType<string>()]
            : [];

    public bool Add(string folder)
    {
        if (string.IsNullOrEmpty(folder) || _folders.Contains(folder)) return false;
        _folders.Add(folder);
        Rescan();
        return true;
    }

    public void Remove(int row)
    {
        if (row < 0 || row >= _folders.Count) return;
        _folders.RemoveAt(row);
        Rescan();
    }

    // Walks only folders not walked yet, drops the ones removed or gone, and resamples the grid.
    public void Rescan()
    {
        foreach (var known in _files.Keys.ToList())
            if (!_folders.Contains(known) || !Directory.Exists(known))
                _files.Remove(known);
        foreach (var folder in _folders)
            if (!_files.ContainsKey(folder))
                _files[folder] = Directory.Exists(folder) ? _scan(folder) : [];
        Resample(_gridCount);
    }

    // A fresh sample for a grid of `count` cells.
    public void Resample(int count)
    {
        _gridCount = count;
        _thumbnails.Clear();
        _thumbnails.AddRange(Sample(_files, count, MaxVideoThumbnails));
        Generation++;
    }

    // A resize: more cells drawn from what is not shown yet, keeping the video cap across the whole
    // grid, or the ones at the end dropped - the cells that stay are left alone.
    public void Adjust(int count)
    {
        _gridCount = count;
        if (count < _thumbnails.Count)
        {
            _thumbnails.RemoveRange(count, _thumbnails.Count - count);
            return;
        }
        var shown = _thumbnails.ToHashSet();
        var remaining = _files.ToDictionary(f => f.Key, f => f.Value.Where(p => !shown.Contains(p)).ToList());
        var videoBudget = Math.Max(0, MaxVideoThumbnails - _thumbnails.Count(IsVideo));
        _thumbnails.AddRange(Sample(remaining, count - _thumbnails.Count, videoBudget));
    }

    private List<string> Sample(IReadOnlyDictionary<string, List<string>> files, int count, int videoBudget) =>
        ThumbnailSampling.WithVideoCap(files, count, videoBudget, IsVideo, _random);

    private static bool IsVideo(string path) => MediaKinds.Of(path) == MediaKind.Video;

    // Start: every file of every folder, and the folders remembered for next time.
    public IReadOnlyList<string> Start()
    {
        _store?.Save(StoreKey, new JsonArray([.. _folders.Select(f => (JsonNode)f)]));
        return [.. _files.Values.SelectMany(files => files)];
    }
}
