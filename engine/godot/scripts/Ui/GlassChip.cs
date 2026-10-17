using Gooner.Core.App;
using Godot;

namespace Gooner.Engine.Ui;

// A small caption on glass - the session clock, the record chase. Nothing at all while its text is
// null, as SessionHudWidget hid them.
public partial class GlassChip : PanelContainer
{
    private readonly Label _label;

    public GlassChip()
    {
        Glass.Apply(this, 18, 0, opacity: 0.42f, shadow: false);
        var box = (StyleBoxFlat)GetThemeStylebox("panel");
        box.ContentMarginLeft = box.ContentMarginRight = 14;
        box.ContentMarginTop = box.ContentMarginBottom = 6;
        MouseFilter = MouseFilterEnum.Ignore;
        _label = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _label.AddThemeFontSizeOverride("font_size", 15);
        AddChild(_label);
        Visible = false;
    }

    public void Show(string? text)
    {
        Visible = text is not null;
        if (text is not null && _label.Text != text) _label.Text = text;
        _label.AddThemeColorOverride("font_color", UiTheme.Of(Palette.Text));
    }
}
