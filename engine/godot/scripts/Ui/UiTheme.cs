using Gooner.Core.App;
using Godot;

namespace Gooner.Engine.Ui;

// The colour scheme in use (Gooner.Core.App.Palette) as a Godot theme: the rounded buttons, fields
// and tabs of src/theme.py, translucent where they sit on glass, and the look's own pieces - the
// dock's buttons, the start screen's cards and links. Built again when the scheme changes.
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
        var page = Box(Of(Palette.Background, 0.4f), 8);
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

        Look(theme);

        var tooltip = Box(Of(Palette.Surface), 4, 6);
        tooltip.SetBorderWidthAll(1);
        tooltip.BorderColor = Of(Palette.Secondary);
        theme.SetStylebox("panel", "TooltipPanel", tooltip);
        theme.SetColor("font_color", "TooltipLabel", Of(Palette.Text));
        return theme;
    }

    // The pieces of the look that the Python app never had.
    private static void Look(Theme theme)
    {
        StyleBoxFlat Soft(Color background, bool glow = false, int radius = 14)
        {
            var box = Box(background, radius);
            box.ContentMarginLeft = box.ContentMarginRight = 10;
            box.ContentMarginTop = box.ContentMarginBottom = 8;
            if (glow)
            {
                box.ShadowColor = Of(Palette.Accent, 0.4f);
                box.ShadowSize = 14;
            }
            return box;
        }
        void Fonts(string type, Color normal, Color hover)
        {
            foreach (var state in new[] { "font_color", "font_pressed_color", "font_focus_color" })
                theme.SetColor(state, type, normal);
            theme.SetColor("font_hover_color", type, hover);
            theme.SetColor("font_hover_pressed_color", type, hover);
            theme.SetColor("font_disabled_color", type, Of(Palette.DisabledText));
        }

        // The dock's buttons: nothing behind them until the pointer is over one.
        theme.SetTypeVariation("DockButton", "Button");
        theme.SetStylebox("normal", "DockButton", Soft(new Color(1, 1, 1, 0)));
        theme.SetStylebox("hover", "DockButton", Soft(new Color(1, 1, 1, 0.09f), glow: true));
        theme.SetStylebox("pressed", "DockButton", Soft(Of(Palette.Accent, 0.3f)));
        theme.SetStylebox("disabled", "DockButton", Soft(new Color(1, 1, 1, 0)));
        theme.SetColor("icon_disabled_color", "DockButton", new Color(1, 1, 1, 0.3f));
        Fonts("DockButton", Of(Palette.Text), Colors.White);

        // "I reached my Edge" - the one lit button in the dock.
        StyleBoxFlat Edge(float alpha)
        {
            var box = Soft(Of(Palette.Accent, alpha), glow: true);
            box.SetBorderWidthAll(1);
            box.BorderColor = Of(Palette.Accent, 0.6f);
            box.ContentMarginLeft = box.ContentMarginRight = 18;
            return box;
        }
        theme.SetTypeVariation("EdgeButton", "Button");
        theme.SetStylebox("normal", "EdgeButton", Edge(0.22f));
        theme.SetStylebox("hover", "EdgeButton", Edge(0.38f));
        theme.SetStylebox("pressed", "EdgeButton", Edge(0.55f));
        var spent = Soft(new Color(1, 1, 1, 0.05f));
        spent.ContentMarginLeft = spent.ContentMarginRight = 18;
        theme.SetStylebox("disabled", "EdgeButton", spent);
        theme.SetFont("font", "EdgeButton", Bold);
        Fonts("EdgeButton", Colors.White, Colors.White);

        // The start screen's cards: dark glass, lit up and rimmed in the accent under the pointer.
        StyleBoxFlat Card(Color background, Color rim, float glow)
        {
            var box = Box(background, 18);
            box.SetBorderWidthAll(1);
            box.BorderColor = rim;
            box.ContentMarginLeft = box.ContentMarginRight = 18;
            box.ContentMarginTop = box.ContentMarginBottom = 12;
            if (glow > 0)
            {
                box.ShadowColor = Of(Palette.Accent, glow);
                box.ShadowSize = 22;
            }
            return box;
        }
        theme.SetTypeVariation("Card", "Button");
        theme.SetStylebox("normal", "Card", Card(Of(Palette.Background, 0.55f), Of(Palette.Text, 0.16f), 0));
        theme.SetStylebox("hover", "Card", Card(Of(Palette.Background, 0.7f), Of(Palette.Accent, 0.65f), 0.35f));
        theme.SetStylebox("pressed", "Card", Card(Of(Palette.Accent, 0.3f), Of(Palette.Accent, 0.8f), 0.4f));
        theme.SetStylebox("focus", "Card", new StyleBoxEmpty());
        Fonts("Card", Of(Palette.Text), Colors.White);
        theme.SetTypeVariation("CardPrimary", "Button");
        theme.SetStylebox("normal", "CardPrimary", Card(Of(Palette.Accent, 0.55f), Of(Palette.Text, 0.45f), 0.3f));
        theme.SetStylebox("hover", "CardPrimary", Card(Of(Palette.Accent, 0.72f), Of(Palette.Text, 0.7f), 0.6f));
        theme.SetStylebox("pressed", "CardPrimary", Card(Of(Palette.Accent, 0.85f), Of(Palette.Text, 0.8f), 0.5f));
        theme.SetStylebox("focus", "CardPrimary", new StyleBoxEmpty());
        Fonts("CardPrimary", Colors.White, Colors.White);

        // The start screen's footer: plain words until the pointer is on one.
        theme.SetTypeVariation("Link", "Button");
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
        {
            var plain = new StyleBoxEmpty();
            plain.ContentMarginLeft = plain.ContentMarginRight = 4;
            theme.SetStylebox(state, "Link", plain);
        }
        Fonts("Link", Of(Palette.Text, 0.55f), Of(Palette.Accent));
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

    private static readonly System.Collections.Generic.Dictionary<string, SystemFont> Displays = [];

    // The face of the big words - the logo, the callout, the climax banner - in the scheme in use:
    // Segoe UI at its heaviest for Neon, an italic Bodoni for Boudoir.
    public static SystemFont Display
    {
        get
        {
            var scheme = Palette.Current;
            if (Displays.TryGetValue(scheme.Name, out var font)) return font;
            font = new SystemFont { FontNames = [.. scheme.DisplayFonts], FontItalic = scheme.DisplayItalic, FontWeight = scheme.DisplayItalic ? 600 : 800 };
            Displays[scheme.Name] = font;
            return font;
        }
    }
}
