using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// ThirdPartyLicensesDialog: what the program carries of other people's software. The notices
// first, every text of engine/licenses after them, and then Godot's own - its license and the
// notices of every library built into it, which the engine reports about itself.
public partial class LicensesPanel : AppPanel
{
    public LicensesPanel(string? shelf, string? show = null)
        : base("Third-Party Licenses", new Vector2(760, 620))
    {
        var entries = new List<(string Name, Func<string> Text)>();
        if (shelf is not null)
            foreach (var file in LicenseShelf.Files(shelf))
                entries.Add((file, () => File.ReadAllText(Path.Combine(shelf, file))));
        entries.Add(("Godot Engine", Godot.Engine.GetLicenseText));
        entries.Add(("Godot Engine components", GodotComponents));

        var text = new TextEdit
        {
            Editable = false,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            WrapMode = TextEdit.LineWrappingMode.None,
        };
        // License texts are laid out for a fixed-width font.
        text.AddThemeFontOverride("font", UiTheme.Mono);
        text.AddThemeFontSizeOverride("font_size", 12);
        text.AddThemeColorOverride("font_readonly_color", UiTheme.Of(Gooner.Core.App.Palette.Text));
        var chooser = new OptionButton { FocusMode = FocusModeEnum.None };
        foreach (var (name, _) in entries) chooser.AddItem(name);
        chooser.ItemSelected += index => text.Text = entries[(int)index].Text();
        Body.AddChild(chooser);
        Body.AddChild(text);
        var first = Math.Max(0, entries.FindIndex(entry => entry.Name == show));
        chooser.Select(first);
        text.Text = entries[first].Text();
        AddButton("Close", Close);
    }

    // Engine.get_copyright_info and get_license_info, as one page: who wrote each piece built into
    // Godot and under what, then the full text of every license named.
    private static string GodotComponents()
    {
        var page = new StringBuilder();
        foreach (var component in Godot.Engine.GetCopyrightInfo())
        {
            page.Append("== ").Append(component["name"].AsString()).AppendLine(" ==");
            foreach (var part in component["parts"].AsGodotArray<Godot.Collections.Dictionary>())
            {
                page.Append("Files: ").AppendLine(string.Join(", ", part["files"].AsStringArray()));
                foreach (var line in part["copyright"].AsStringArray())
                    page.Append("Copyright: ").AppendLine(line);
                page.Append("License: ").AppendLine(part["license"].AsString());
            }
            page.AppendLine();
        }
        foreach (var (name, licence) in Godot.Engine.GetLicenseInfo())
        {
            page.Append("== License: ").Append(name.AsString()).AppendLine(" ==");
            page.AppendLine(licence.AsString()).AppendLine();
        }
        return page.ToString();
    }
}
