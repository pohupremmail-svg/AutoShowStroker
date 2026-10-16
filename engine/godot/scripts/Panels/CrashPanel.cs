using Gooner.Core.App;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// crash_handler's box: what went wrong, said plainly, with the technical part behind Show Details -
// out of the way, but selectable and copyable into a bug report. Nothing is sent anywhere.
public partial class CrashPanel : AppPanel
{
    public CrashPanel(string details)
        : base(CrashReporter.Title, new Vector2(620, 0))
    {
        AddText(CrashReporter.Message, 14, wrap: true);
        var text = new TextEdit
        {
            Text = details,
            Editable = false,
            Visible = false,
            CustomMinimumSize = new Vector2(0, 260),
            WrapMode = TextEdit.LineWrappingMode.None,
        };
        text.AddThemeFontOverride("font", UiTheme.Mono);
        text.AddThemeFontSizeOverride("font_size", 12);
        Body.AddChild(text);
        Button toggle = null!;
        toggle = AddButton("Show Details...", () =>
        {
            text.Visible = !text.Visible;
            toggle.Text = text.Visible ? "Hide Details" : "Show Details...";
        });
        AddButton("Copy Details", () => DisplayServer.ClipboardSet(details));
        AddButton("Close", Close, primary: true);
    }
}
