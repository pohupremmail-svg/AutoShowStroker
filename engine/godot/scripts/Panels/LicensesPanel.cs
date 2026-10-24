using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Gooner.Core.Localization;
using Gooner.Core.Panels;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine.Panels;

// ThirdPartyLicensesDialog: what the program carries of other people's software. The notices
// first, every text of engine/licenses after them, and then Godot's own - its license and the
// notices of every library built into it, which the engine reports about itself. Picked from a list
// beside the text, as the redesign's panels pick a page.
public partial class LicensesPanel : AppPanel
{
    public LicensesPanel(string? shelf, string? show = null)
        : base(L10n.T("Third-Party Licenses"), new Vector2(960, 660), bodyScrolls: false)
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
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var chooser = new ItemList { CustomMinimumSize = new Vector2(260, 0), FocusMode = FocusModeEnum.None };
        foreach (var (name, _) in entries) chooser.AddItem(name);
        chooser.ItemSelected += index => text.Text = entries[(int)index].Text();
        var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 14);
        row.AddChild(chooser);
        row.AddChild(text);
        Body.AddChild(row);
        var first = Math.Max(0, entries.FindIndex(entry => entry.Name == show));
        chooser.Select(first);
        text.Text = entries[first].Text();
        AddButton(L10n.T("Close"), Close, primary: true);
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
