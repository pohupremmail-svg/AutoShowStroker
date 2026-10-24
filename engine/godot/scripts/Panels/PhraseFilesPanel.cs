using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// CustomPhraseFilesDialog: the user's own phrase files - pick a language and a tone, add a file
// through the system's file dialog, remove one from the list. Two sections of the redesign: adding
// one, and the ones there are.
public partial class PhraseFilesPanel : AppPanel
{
    private readonly CustomPhraseFiles _files;
    private readonly ItemList _list;
    private readonly Label _error;
    private readonly OptionButton _lang;
    private readonly OptionButton _tone;
    private readonly Button _remove;

    public PhraseFilesPanel(CustomPhraseFiles files)
        : base(L10n.T("Manage Custom Phrase Files"), new Vector2(640, 500))
    {
        _files = files;
        Body.AddThemeConstantOverride("separation", 12);
        _lang = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var lang in files.Languages) _lang.AddItem(lang);
        _tone = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var choice in files.ToneChoices) _tone.AddItem(choice.Label);
        var picker = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Title = L10n.T("Select a Custom Callout File"),
            Filters = ["*.json ; " + L10n.T("JSON Files")],
        };
        picker.FileSelected += Add;
        AddChild(picker);
        var add = Widgets.Button(L10n.T("Add File..."), () => picker.PopupCentered(new Vector2I(900, 600)));
        var (adding, addRows) = Widgets.Section(L10n.T("Add a file"));
        addRows.AddChild(Widgets.Row(_lang, _tone, add));
        _error = Widgets.Text("", 13, Palette.Denied, wrap: true);
        addRows.AddChild(_error);
        Body.AddChild(adding);

        _remove = Widgets.Button(L10n.T("Remove Selected"), () =>
        {
            var selected = _list!.GetSelectedItems();
            _files.Remove(selected.Length > 0 ? selected[0] : -1);
            Refresh();
        }, "Danger");
        var (yours, rows) = Widgets.Section(L10n.T("Your files"), trailing: _remove);
        yours.SizeFlagsVertical = SizeFlags.ExpandFill;
        rows.SizeFlagsVertical = SizeFlags.ExpandFill;
        _list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 140) };
        _list.ItemSelected += _ => _remove.Disabled = false;
        rows.AddChild(_list);
        Body.AddChild(yours);
        AddButton(L10n.T("Close"), Close, primary: true);
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
        _remove.Disabled = true;
        _error.Text = _files.Error;
        _error.Visible = _error.Text.Length > 0;
    }
}
