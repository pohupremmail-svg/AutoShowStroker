using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Media;
using Gooner.Core.Panels;
using Gooner.Core.Randomness;
using Gooner.Core.Timing;
using Gooner.Engine.Stage;
using Gooner.Engine.Ui;
using Gooner.Engine.Ui.Controls;
using Godot;

namespace Gooner.Engine.Panels;

// MediaFolderPickerDialog: the folders a session plays from, and a grid of what is in them - as
// many cells as fit, refilled as the panel is resized. Images load in the background, GIFs play,
// a video shows a grabbed still, or a five-second loop of itself with the switch on. Two sections of
// the redesign: the folders, and what is in them.
public partial class FolderPickerPanel : AppPanel
{
    private const int Cell = 140;
    private const int Spacing = 8;
    private const int Margin = 8;
    private const int ScrollbarReserve = 12;
    private const double ResizeDebounceSec = 0.15;

    private readonly MediaFolderPicker _picker;
    private readonly Action<IReadOnlyList<string>> _start;
    private readonly IRandomSource _random;
    private readonly VideoThumbnailer _stills;
    private readonly ItemList _folders;
    private readonly Button _remove;
    private readonly Button _startButton;
    private readonly ToggleSwitch _animate;
    private readonly ScrollContainer _scroll;
    private readonly GridContainer _grid;
    private readonly List<Control> _cells = [];
    private readonly Godot.Timer _resize;
    private int _builtGeneration = -1;

    public FolderPickerPanel(MediaFolderPicker picker, Action<IReadOnlyList<string>> start, IRandomSource random, ClockScheduler scheduler)
        : base(L10n.T("Select Gooning Folders"), new Vector2(900, 650))
    {
        _picker = picker;
        _start = start;
        _random = random;
        _stills = new VideoThumbnailer(() =>
        {
            var grabber = new FrameGrabber();
            AddChild(grabber);
            return grabber;
        }, scheduler);

        Body.AddThemeConstantOverride("separation", 12);
        _folders = new ItemList { CustomMinimumSize = new Vector2(0, 96) };
        _folders.ItemSelected += _ => _remove!.Disabled = false;
        var chooser = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Title = L10n.T("Add Gooning Folder"),
        };
        chooser.DirSelected += folder =>
        {
            if (_picker.Add(folder)) Refresh();
        };
        AddChild(chooser);
        var add = Widgets.Button(L10n.T("Add Folder..."), () => chooser.PopupCentered(new Vector2I(900, 600)));
        _remove = Widgets.Button(L10n.T("Remove Selected"), () =>
        {
            var selected = _folders.GetSelectedItems();
            if (selected.Length == 0) return;
            _picker.Remove(selected[0]);
            Refresh();
        }, "Danger");
        _remove.Disabled = true;
        var (folders, folderRows) = Widgets.Section(L10n.T("Folders"), trailing: Widgets.Row(add, _remove));
        folderRows.AddChild(_folders);
        Body.AddChild(folders);

        // Never remembered: a still, not a live decode, is what every opening starts with.
        _animate = new ToggleSwitch(false);
        _animate.Toggled += _ => BuildCells(force: true);
        var animate = Widgets.Row(new Label { Text = L10n.T("Animate video clips (5s loop)"), ThemeTypeVariation = "Hint" }, _animate);
        var (preview, previewRows) = Widgets.Section(L10n.T("Preview"), trailing: animate);
        preview.SizeFlagsVertical = SizeFlags.ExpandFill;
        previewRows.SizeFlagsVertical = SizeFlags.ExpandFill;
        _scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", Margin);
        _grid = new GridContainer { Columns = 1 };
        _grid.AddThemeConstantOverride("h_separation", Spacing);
        _grid.AddThemeConstantOverride("v_separation", Spacing);
        margin.AddChild(_grid);
        _scroll.AddChild(margin);
        previewRows.AddChild(_scroll);
        Body.AddChild(preview);

        _resize = new Godot.Timer { OneShot = true, WaitTime = ResizeDebounceSec };
        _resize.Timeout += ApplyResize;
        AddChild(_resize);
        _scroll.Resized += () => _resize.Start();

        AddButton(L10n.T("Cancel"), Close);
        _startButton = AddButton(L10n.T("Start"), () =>
        {
            var files = _picker.Start();
            Close();
            _start(files);
        }, primary: true);
        Closed += () =>
        {
            _stills.CancelAll();
            foreach (var cell in _cells) cell.QueueFree();
            _cells.Clear();
        };
        Refresh();
    }

    private (int Columns, int Count) GridSize()
    {
        var size = _scroll.Size;
        var grid = ThumbnailSampling.Grid((int)size.X - ScrollbarReserve, (int)size.Y, Cell, Cell, Spacing, Margin);
        return (grid.Columns, grid.Count);
    }

    // The folder list and the buttons, after a folder came or went; the grid is sampled afresh.
    private void Refresh()
    {
        _folders.Clear();
        foreach (var row in _picker.Rows) _folders.AddItem(row);
        _remove.Disabled = true;
        _startButton.Disabled = !_picker.CanStart;
        if (_scroll.Size.X <= 0) return;
        var (columns, count) = GridSize();
        _picker.Resample(count);
        _grid.Columns = columns;
        BuildCells();
    }

    // Settled after a resize: cells added or dropped at the end, the rest left as they are.
    private void ApplyResize()
    {
        var (columns, count) = GridSize();
        _grid.Columns = columns;
        if (_builtGeneration < 0) _picker.Resample(count);
        else _picker.Adjust(count);
        BuildCells();
    }

    // Rebuilds every cell after a resample or a change of the box; otherwise only the difference.
    private void BuildCells(bool force = false)
    {
        if (force || _builtGeneration != _picker.Generation)
        {
            _stills.CancelAll();
            foreach (var cell in _cells) cell.QueueFree();
            _cells.Clear();
            _builtGeneration = _picker.Generation;
        }
        while (_cells.Count > _picker.Thumbnails.Count)
        {
            _cells[^1].QueueFree();
            _cells.RemoveAt(_cells.Count - 1);
        }
        for (var i = _cells.Count; i < _picker.Thumbnails.Count; i++)
        {
            var cell = MakeCell(_picker.Thumbnails[i]);
            _cells.Add(cell);
            _grid.AddChild(cell);
        }
    }

    private PanelContainer MakeCell(string path)
    {
        var cell = new PanelContainer { CustomMinimumSize = new Vector2(Cell, Cell), ClipContents = true };
        var frame = UiTheme.Box(UiTheme.Of(Palette.SurfaceDarkest, 0.8f), 12, 4);
        frame.SetBorderWidthAll(1);
        frame.BorderColor = new Color(1, 1, 1, 0.08f);
        cell.AddThemeStyleboxOverride("panel", frame);
        var inner = new Vector2I(Cell - 8, Cell - 8);
        switch (MediaKinds.Of(path))
        {
            case MediaKind.Gif:
                cell.AddChild(new GifView(path, inner));
                break;
            case MediaKind.Video when _animate.ButtonPressed:
                cell.AddChild(new LoopView(path, _random));
                break;
            case MediaKind.Video:
                var still = Picture(cell, path);
                _stills.Request(path, (_, image) =>
                {
                    if (!IsInstanceValid(still)) return;
                    if (image is Image frame)
                    {
                        Stills.FitInto(frame, inner);
                        still.Texture = ImageTexture.CreateFromImage(frame);
                    }
                    else
                    {
                        NameFile(cell, path);
                    }
                });
                break;
            default:
                var picture = Picture(cell, path);
                // Off the main thread: a photo of a few thousand pixels a side takes a while, and a
                // grid of them would stall the window while it opens.
                Task.Run(() => Stills.Load(path, inner)).ContinueWith(loaded => Callable.From(() =>
                {
                    if (!IsInstanceValid(picture)) return;
                    if (loaded.Result is { } image) picture.Texture = ImageTexture.CreateFromImage(image);
                    else NameFile(cell, path);
                }).CallDeferred(), TaskScheduler.Default);
                break;
        }
        return cell;
    }

    private static TextureRect Picture(Control cell, string path)
    {
        var picture = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TooltipText = Path.GetFileName(path),
        };
        cell.AddChild(picture);
        return picture;
    }

    // A cell with no picture to show names its file.
    private static void NameFile(Control cell, string path)
    {
        if (!IsInstanceValid(cell)) return;
        cell.AddChild(new Label
        {
            Text = Path.GetFileName(path),
            AutowrapMode = TextServer.AutowrapMode.Arbitrary,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    // A video playing five seconds of itself, round and round, from a moment picked anywhere in the
    // clip once its length is known - silent, as a preview has to be.
    private sealed partial class LoopView : VideoStreamPlayer
    {
        private const double WindowSec = 5.0;
        private readonly IRandomSource _random;
        private double _start = -1;
        private double _played;

        public LoopView(string path, IRandomSource random)
        {
            _random = random;
            Expand = true;
            VolumeDb = -80;
            Loop = true;
            MouseFilter = MouseFilterEnum.Ignore;
            if (!VideoView.Available) return;
            var stream = ClassDB.Instantiate("FFmpegVideoStream").As<VideoStream>();
            stream.File = path;
            Stream = stream;
        }

        public override void _Ready()
        {
            if (Stream is not null) Play();
        }

        public override void _Process(double delta)
        {
            if (Stream is null) return;
            if (_start < 0)
            {
                var length = GetStreamLength();
                if (length <= 0) return;
                _start = length > 1 ? 1 + _random.NextDouble() * (length - 2) : 0;
                StreamPosition = _start;
                return;
            }
            if ((_played += delta) < WindowSec) return;
            _played = 0;
            StreamPosition = _start;
        }
    }
}
