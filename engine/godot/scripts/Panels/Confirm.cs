using System;
using Godot;

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
        panel.AddButton("OK", panel.Close, primary: true);
        host.Show(panel);
    }

    public static void Ask(PanelHost host, string title, string text, Action yes, string yesLabel = "Yes", string noLabel = "No")
    {
        ArgumentNullException.ThrowIfNull(host);
        var panel = new AppPanel(title, new Vector2(460, 0));
        panel.AddText(text, 14, wrap: true);
        panel.AddButton(yesLabel, () =>
        {
            panel.Close();
            yes();
        });
        panel.AddButton(noLabel, panel.Close, primary: true);
        host.Show(panel);
    }
}
