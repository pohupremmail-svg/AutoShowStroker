using Gooner.Core.App;
using Godot;

namespace Gooner.Engine.Ui;

// The palette of src/theme.py as a Godot theme: the same colours, the same rounded buttons. The
// new look comes in phase 7; this is the old one, so the two apps can be compared side by side.
public static class UiTheme
{
    public static Color Of(string hex) => Color.FromHtml(hex);

    public static Color Of(string hex, float alpha)
    {
        var colour = Color.FromHtml(hex);
        colour.A = alpha;
        return colour;
    }

    public static StyleBoxFlat Box(Color background, int radius, float margin = 0)
    {
        var box = new StyleBoxFlat { BgColor = background };
        box.SetCornerRadiusAll(radius);
        box.SetContentMarginAll(margin);
        return box;
    }

    public static Theme Build()
    {
        var theme = new Theme { DefaultFontSize = 15 };

        StyleBoxFlat Button(string background)
        {
            var box = Box(Of(background), 8);
            box.ContentMarginLeft = box.ContentMarginRight = 14;
            box.ContentMarginTop = box.ContentMarginBottom = 6;
            return box;
        }
        theme.SetStylebox("normal", "Button", Button(Palette.Secondary));
        theme.SetStylebox("hover", "Button", Button(Palette.SecondaryHover));
        theme.SetStylebox("pressed", "Button", Button(Palette.Background));
        theme.SetStylebox("disabled", "Button", Button(Palette.DisabledBg));
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            theme.SetColor(state, "Button", Of(Palette.Text));
        theme.SetColor("font_disabled_color", "Button", Of(Palette.DisabledText));

        // The primary button: pink, with the glow the Qt side drew as a drop shadow.
        StyleBoxFlat Primary(string background)
        {
            var box = Button(background);
            box.SetBorderWidthAll(1);
            box.BorderColor = Of(Palette.Text);
            box.ShadowColor = Of(Palette.Accent, 0.6f);
            box.ShadowSize = 14;
            return box;
        }
        theme.SetTypeVariation("PrimaryButton", "Button");
        theme.SetStylebox("normal", "PrimaryButton", Primary(Palette.Accent));
        theme.SetStylebox("hover", "PrimaryButton", Primary(Palette.AccentHover));
        theme.SetStylebox("pressed", "PrimaryButton", Primary(Palette.Accent));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            theme.SetColor(state, "PrimaryButton", Of(Palette.Background));

        theme.SetColor("font_color", "Label", Of(Palette.Text));
        theme.SetStylebox("panel", "PanelContainer", Box(Of(Palette.Surface), 12, 18));
        return theme;
    }
}
