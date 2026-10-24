using System;
using Godot;
using Gooner.Core.Localization;

namespace Gooner.Engine.Panels;

// QMessageBox as a panel. Ask is question(): Yes and No, and No is what Esc and the cross answer,
// as the default button of every destructive question in the app was No. Tell is information() and
// warning(): something to read and an OK.
public static class Confirm
{
    public static void Tell(PanelHost host, string title, string text)
    {
        ArgumentNullException.ThrowIfNull(host);
        var panel = new AppPanel(title, new Vector2(460, 0));
        panel.AddText(text, 14, wrap: true);
        panel.AddButton(L10n.T("OK"), panel.Close, primary: true);
        host.Show(panel);
    }

    // danger: Yes deletes something, and says so in its colour.
    public static void Ask(PanelHost host, string title, string text, Action yes, string? yesLabel = null, string? noLabel = null, bool danger = false)
    {
        ArgumentNullException.ThrowIfNull(host);
        var panel = new AppPanel(title, new Vector2(460, 0));
        panel.AddText(text, 14, wrap: true);
        panel.AddButton(yesLabel ?? L10n.T("Yes"), () =>
        {
            panel.Close();
            yes();
        }, danger: danger);
        panel.AddButton(noLabel ?? L10n.T("No"), panel.Close, primary: true);
        host.Show(panel);
    }
}
