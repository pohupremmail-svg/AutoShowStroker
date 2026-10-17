using System;
using Gooner.Core.App;
using Gooner.Core.Localization;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// SavedSessionsDialog: the shelf of Gooner.Core.Panels.SavedSessionsShelf - replay, export, import,
// delete - with the questions it asked in message boxes asked as panels.
public partial class SavedSessionsPanel : AppPanel
{
    private static string Filter => "*.json ; " + L10n.T("GoonerApp session");

    private readonly SavedSessionsShelf _shelf;
    private readonly PanelHost _host;
    private readonly ItemList _list;
    private readonly Label _empty;
    private readonly Button _replay;
    private readonly Button _export;
    private readonly Button _delete;
    private readonly FileDialog _exportPicker;
    private readonly FileDialog _importPicker;
    private bool _withPaths;

    public SavedSessionsPanel(SavedSessionsShelf shelf, PanelHost host)
        : base(L10n.T("Saved Sessions"), new Vector2(640, 460))
    {
        _shelf = shelf;
        _host = host;
        Body.AddChild(Widgets.Text(SavedSessionsShelf.Intro, 14, Palette.Text, wrap: true));
        _list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
        _list.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Of(Palette.SurfaceDark), 6, 6));
        _list.ItemSelected += index =>
        {
            _shelf.Select((int)index);
            UpdateButtons();
        };
        _list.ItemActivated += _ => Replay();
        Body.AddChild(_list);
        _empty = Widgets.Text(SavedSessionsShelf.EmptyText, 14, Palette.Text, wrap: true);
        Body.AddChild(_empty);

        _replay = AddButton(L10n.T("Replay"), Replay, primary: true);
        _export = AddButton(L10n.T("Export..."), AskAboutPaths);
        _export.TooltipText = L10n.T("Write this session to a file you can pass on");
        var import = AddButton(L10n.T("Import..."), () => _importPicker!.PopupCentered(new Vector2I(900, 600)));
        import.TooltipText = L10n.T("Put a session file somebody sent you on the shelf");
        _delete = AddButton(L10n.T("Delete"), AskToDelete);
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Buttons.AddChild(spacer);
        Buttons.Alignment = BoxContainer.AlignmentMode.Begin;
        AddButton(L10n.T("Close"), Close);

        _exportPicker = Picker(FileDialog.FileModeEnum.SaveFile, L10n.T("Export Session"));
        _exportPicker.CurrentFile = "session.json";
        _exportPicker.FileSelected += Export;
        _importPicker = Picker(FileDialog.FileModeEnum.OpenFile, L10n.T("Import Session"));
        _importPicker.FileSelected += Import;
        Refresh();
    }

    private FileDialog Picker(FileDialog.FileModeEnum mode, string title)
    {
        var picker = new FileDialog
        {
            FileMode = mode,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Title = title,
            Filters = [Filter],
        };
        AddChild(picker);
        return picker;
    }

    private void Refresh()
    {
        _shelf.Refresh();
        _list.Clear();
        foreach (var row in _shelf.Rows) _list.AddItem(row);
        _empty.Visible = _shelf.IsEmpty;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _replay.Disabled = _export.Disabled = _delete.Disabled = !_shelf.CanAct;
    }

    // --- replaying ---

    private void Replay()
    {
        if (_shelf.PlanReplay() is not { } plan) return;
        if (plan.MustAsk)
            Confirm.Ask(_host, L10n.T("Some of the files are gone"), SavedSessionsShelf.MissingFilesQuestion(plan), () => Start(true), noLabel: L10n.T("Cancel"));
        else
            Start(plan.IgnorePaths);
    }

    private void Start(bool ignorePaths)
    {
        if (!_shelf.Replay(ignorePaths))
        {
            Confirm.Tell(_host, L10n.T("Nothing to play it with"),
                L10n.T("This session has to be replayed against your own collection, and none is loaded. Load a folder first, then replay it."));
            return;
        }
        // Out of the way, so the session is what is on screen.
        Close();
    }

    // --- exporting ---

    // Asked before the file picker, so backing out costs nothing, and every time: the right answer
    // depends on who is getting the file. Leaving them out is the default - the safe answer is the
    // one a distracted Enter gets.
    private void AskAboutPaths()
    {
        if (!_shelf.CanAct) return;
        var question = new AppPanel(L10n.T("Include the media paths?"), new Vector2(520, 0));
        question.AddText(L10n.T("Should this export carry the paths of the media it showed?"), 15, wrap: true);
        question.AddText(SavedSessionsShelf.PathsWarning, 13, wrap: true);
        void Choose(bool withPaths)
        {
            question.Close();
            _withPaths = withPaths;
            _exportPicker.PopupCentered(new Vector2I(900, 600));
        }
        question.AddButton(L10n.T("Include them"), () => Choose(true));
        question.AddButton(L10n.T("Cancel"), question.Close);
        question.AddButton(L10n.T("Leave them out"), () => Choose(false), primary: true);
        _host.Show(question);
    }

    private void Export(string target)
    {
        if (!_shelf.Export(target, _withPaths))
        {
            Confirm.Tell(_host, L10n.T("Could not export"), L10n.T("That session could not be written. Try somewhere else."));
            return;
        }
        Confirm.Tell(_host, L10n.T("Session exported"), L10n.T("Exported. Whoever opens it can put it on their own shelf with Import."));
    }

    // --- importing ---

    private void Import(string source)
    {
        if (_shelf.Import(source) is { } problem)
        {
            Confirm.Tell(_host, L10n.T("Could not import"), problem);
            return;
        }
        Refresh();
    }

    // --- deleting ---

    private void AskToDelete()
    {
        if (!_shelf.CanAct) return;
        Confirm.Ask(_host, L10n.T("Delete this session?"), L10n.T("This removes the saved session for good. Exported copies are not touched."), () =>
        {
            _shelf.DeleteSelected();
            Refresh();
        });
    }
}
