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
        var theme = new Theme { DefaultFontSize = 15, DefaultFont = Regular };

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
        theme.SetFont("font", "PrimaryButton", Bold);

        theme.SetColor("font_color", "Label", Of(Palette.Text));
        theme.SetStylebox("panel", "PanelContainer", Box(Of(Palette.Surface), 12, 18));

        // A panel's heading: the title WhatsNewDialog and its siblings drew in their own body.
        theme.SetTypeVariation("PanelTitle", "Label");
        theme.SetFont("font", "PanelTitle", Bold);
        theme.SetFontSize("font_size", "PanelTitle", 22);
        theme.SetColor("font_color", "PanelTitle", Of(Palette.Accent));

        // The menu bar and its menus, in the palette's Highlight and HighlightedText.
        StyleBoxFlat MenuItem(Color background)
        {
            var box = Box(background, 4);
            box.ContentMarginLeft = box.ContentMarginRight = 10;
            box.ContentMarginTop = box.ContentMarginBottom = 4;
            return box;
        }
        theme.SetStylebox("normal", "MenuBar", MenuItem(Colors.Transparent));
        theme.SetStylebox("hover", "MenuBar", MenuItem(Of(Palette.Secondary)));
        theme.SetStylebox("pressed", "MenuBar", MenuItem(Of(Palette.Accent)));
        theme.SetStylebox("disabled", "MenuBar", MenuItem(Colors.Transparent));
        theme.SetColor("font_color", "MenuBar", Of(Palette.Text));
        theme.SetColor("font_hover_color", "MenuBar", Of(Palette.Text));
        theme.SetColor("font_pressed_color", "MenuBar", Of(Palette.Background));
        theme.SetColor("font_hover_pressed_color", "MenuBar", Of(Palette.Background));
        var popup = Box(Of(Palette.Surface), 6, 4);
        popup.SetBorderWidthAll(1);
        popup.BorderColor = Of(Palette.Secondary);
        theme.SetStylebox("panel", "PopupMenu", popup);
        theme.SetStylebox("hover", "PopupMenu", Box(Of(Palette.Accent), 4));
        theme.SetColor("font_color", "PopupMenu", Of(Palette.Text));
        theme.SetColor("font_hover_color", "PopupMenu", Of(Palette.Background));
        theme.SetColor("font_accelerator_color", "PopupMenu", Of(Palette.DisabledText));
        theme.SetColor("font_separator_color", "PopupMenu", Of(Palette.Secondary));
        var separator = new StyleBoxLine { Color = Of(Palette.Secondary), Thickness = 1 };
        theme.SetStylebox("separator", "PopupMenu", separator);

        // Drop-downs, as QComboBox: a surface field with a secondary rim that lights up on hover.
        StyleBoxFlat Field(string border)
        {
            var box = Box(Of(Palette.Surface), 6);
            box.SetBorderWidthAll(1);
            box.BorderColor = Of(border);
            box.ContentMarginLeft = 8;
            box.ContentMarginRight = 8;
            box.ContentMarginTop = box.ContentMarginBottom = 4;
            return box;
        }
        theme.SetStylebox("normal", "OptionButton", Field(Palette.Secondary));
        theme.SetStylebox("hover", "OptionButton", Field(Palette.Accent));
        theme.SetStylebox("pressed", "OptionButton", Field(Palette.Accent));
        theme.SetStylebox("focus", "OptionButton", new StyleBoxEmpty());
        theme.SetStylebox("disabled", "OptionButton", Field(Palette.DisabledBg));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
            theme.SetColor(state, "OptionButton", Of(Palette.Text));
        theme.SetColor("font_disabled_color", "OptionButton", Of(Palette.DisabledText));

        // Tabs, as QTabBar/QTabWidget: surface tabs, the selected one in the accent, the pages in a
        // rounded secondary frame.
        StyleBoxFlat Tab(string background)
        {
            var box = Box(Of(background), 0);
            box.CornerRadiusTopLeft = box.CornerRadiusTopRight = 8;
            box.ContentMarginLeft = box.ContentMarginRight = 16;
            box.ContentMarginTop = box.ContentMarginBottom = 8;
            box.ExpandMarginRight = 0;
            return box;
        }
        theme.SetStylebox("tab_unselected", "TabContainer", Tab(Palette.Surface));
        theme.SetStylebox("tab_hovered", "TabContainer", Tab(Palette.SecondaryHover));
        theme.SetStylebox("tab_selected", "TabContainer", Tab(Palette.Accent));
        theme.SetStylebox("tab_disabled", "TabContainer", Tab(Palette.DisabledBg));
        theme.SetStylebox("tab_focus", "TabContainer", new StyleBoxEmpty());
        var page = Box(Of(Palette.Background), 8);
        page.SetBorderWidthAll(1);
        page.BorderColor = Of(Palette.Secondary);
        page.CornerRadiusTopLeft = 0;
        theme.SetStylebox("panel", "TabContainer", page);
        theme.SetStylebox("tabbar_background", "TabContainer", new StyleBoxEmpty());
        theme.SetColor("font_unselected_color", "TabContainer", Of(Palette.Text));
        theme.SetColor("font_hovered_color", "TabContainer", Of(Palette.Text));
        theme.SetColor("font_selected_color", "TabContainer", Of(Palette.Background));
        theme.SetConstant("side_margin", "TabContainer", 0);
        theme.SetConstant("tab_separation", "TabContainer", 2);

        // Check boxes, as the stylesheet drew QCheckBox: a rounded surface square with a secondary rim,
        // filled with the accent once ticked; muted, tick kept, while disabled.
        theme.SetIcon("unchecked", "CheckBox", CheckIcon(Of(Palette.Surface), Of(Palette.Secondary)));
        theme.SetIcon("checked", "CheckBox", CheckIcon(Of(Palette.Accent), Of(Palette.Accent)));
        theme.SetIcon("unchecked_disabled", "CheckBox", CheckIcon(Of(Palette.DisabledBg), Of(Palette.DisabledText)));
        theme.SetIcon("checked_disabled", "CheckBox", CheckIcon(Of(Palette.DisabledText), Of(Palette.DisabledText)));
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
        {
            var plain = new StyleBoxEmpty();
            plain.SetContentMarginAll(2);
            theme.SetStylebox(state, "CheckBox", plain);
        }
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
            theme.SetColor(state, "CheckBox", Of(Palette.Text));
        theme.SetColor("font_disabled_color", "CheckBox", Of(Palette.DisabledText));
        theme.SetConstant("h_separation", "CheckBox", 8);

        // Text fields, as the QLineEdit/QPlainTextEdit of the stylesheet: a surface field with a
        // secondary rim, the accent once it has the focus.
        foreach (var field in new[] { "LineEdit", "TextEdit" })
        {
            theme.SetStylebox("normal", field, Field(Palette.Secondary));
            theme.SetStylebox("focus", field, Field(Palette.Accent));
            theme.SetStylebox("read_only", field, Field(Palette.Secondary));
            theme.SetColor("font_color", field, Of(Palette.Text));
            theme.SetColor("font_readonly_color", field, Of(Palette.Text));
            theme.SetColor("font_placeholder_color", field, Of(Palette.DisabledText));
            theme.SetColor("caret_color", field, Of(Palette.Accent));
            theme.SetColor("selection_color", field, Of(Palette.Accent, 0.45f));
        }

        // Scroll bars: a secondary handle that lights up in the accent, on no track of its own.
        foreach (var bar in new[] { "VScrollBar", "HScrollBar" })
        {
            // The track's margins are what makes the bar as thick as it is.
            var track = new StyleBoxEmpty();
            track.SetContentMarginAll(5);
            theme.SetStylebox("scroll", bar, track);
            theme.SetStylebox("scroll_focus", bar, track);
            var grabber = Box(Of(Palette.Secondary), 4);
            grabber.SetContentMarginAll(4);
            theme.SetStylebox("grabber", bar, grabber);
            var lit = Box(Of(Palette.Accent), 4);
            lit.SetContentMarginAll(4);
            theme.SetStylebox("grabber_highlight", bar, lit);
            theme.SetStylebox("grabber_pressed", bar, lit);
        }

        // Rich text: the app's texts are written with bold, italics and the odd bit of code.
        theme.SetColor("default_color", "RichTextLabel", Of(Palette.Text));
        theme.SetFont("bold_font", "RichTextLabel", Bold);
        theme.SetFont("italics_font", "RichTextLabel", Italic);
        theme.SetFont("mono_font", "RichTextLabel", Mono);

        var tooltip = Box(Of(Palette.Surface), 4, 6);
        tooltip.SetBorderWidthAll(1);
        tooltip.BorderColor = Of(Palette.Secondary);
        theme.SetStylebox("panel", "TooltipPanel", tooltip);
        theme.SetColor("font_color", "TooltipLabel", Of(Palette.Text));
        return theme;
    }

    // A 16 px rounded square, 1 px rim, drawn by hand: a CheckBox takes its box as an icon.
    private static ImageTexture CheckIcon(Color fill, Color rim)
    {
        const int size = 16;
        const float half = size / 2f, radius = 4f;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                // Signed distance from the pixel's centre to the rounded square's edge.
                var dx = System.Math.Max(System.Math.Abs(x + 0.5f - half) - (half - radius), 0);
                var dy = System.Math.Max(System.Math.Abs(y + 0.5f - half) - (half - radius), 0);
                var distance = System.MathF.Sqrt(dx * dx + dy * dy) - radius;
                var coverage = System.Math.Clamp(0.5f - distance, 0f, 1f);
                var inner = System.Math.Clamp(0.5f - (distance + 1f), 0f, 1f);
                var colour = rim.Lerp(fill, inner);
                colour.A *= coverage;
                image.SetPixel(x, y, colour);
            }
        }
        return ImageTexture.CreateFromImage(image);
    }

    // Segoe UI, the family the Qt stylesheet names; another sans-serif where it is missing.
    public static readonly SystemFont Regular = new() { FontNames = ["Segoe UI", "sans-serif"] };

    public static readonly SystemFont Bold = new() { FontNames = ["Segoe UI", "sans-serif"], FontWeight = 700 };

    public static readonly SystemFont Italic = new() { FontNames = ["Segoe UI", "sans-serif"], FontItalic = true };

    public static readonly SystemFont Mono = new() { FontNames = ["Consolas", "monospace"] };
}
