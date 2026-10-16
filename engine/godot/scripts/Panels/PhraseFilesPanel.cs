using Gooner.Core.App;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// CustomPhraseFilesDialog: the user's own phrase files - pick a language and a tone, add a file
// through the system's file dialog, remove one from the list.
public partial class PhraseFilesPanel : AppPanel
{
    private readonly CustomPhraseFiles _files;
    private readonly ItemList _list;
    private readonly Label _error;
    private readonly OptionButton _lang;
    private readonly OptionButton _tone;

    public PhraseFilesPanel(CustomPhraseFiles files)
        : base("Manage Custom Phrase Files", new Vector2(560, 360))
    {
        _files = files;
        var row = new HBoxContainer();
        _lang = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var lang in files.Languages) _lang.AddItem(lang);
        _tone = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var choice in files.ToneChoices) _tone.AddItem(choice.Label);
        var picker = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Title = "Select a Custom Callout File",
            Filters = ["*.json ; JSON Files"],
        };
        picker.FileSelected += Add;
        AddChild(picker);
        var add = new Button { Text = "Add File...", FocusMode = FocusModeEnum.None };
        add.Pressed += () => picker.PopupCentered(new Vector2I(900, 600));
        row.AddChild(_lang);
        row.AddChild(_tone);
        row.AddChild(add);
        Body.AddChild(row);

        _error = Widgets.Text("", 13, Palette.Denied, wrap: true);
        Body.AddChild(_error);
        _list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
        _list.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Of(Palette.SurfaceDark), 6, 6));
        _list.AddThemeColorOverride("font_color", UiTheme.Of(Palette.Text));
        Body.AddChild(_list);
        var remove = new Button { Text = "Remove Selected", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        remove.Pressed += () =>
        {
            var selected = _list.GetSelectedItems();
            _files.Remove(selected.Length > 0 ? selected[0] : -1);
            Refresh();
        };
        Body.AddChild(remove);
        AddButton("Close", Close, primary: true);
        Refresh();
    }

    private void Add(string path)
    {
        _files.Add(path, _files.Languages[_lang.Selected], _files.ToneChoices[_tone.Selected].Tone);
        Refresh();
    }

    private void Refresh()
    {
        _list.Clear();
        foreach (var row in _files.Rows) _list.AddItem(row);
        _error.Text = _files.Error;
        _error.Visible = _error.Text.Length > 0;
    }
}
