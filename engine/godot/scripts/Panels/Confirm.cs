using System;
using Godot;

namespace Gooner.Engine.Panels;

// QMessageBox.question as a panel: a question, Yes and No. No is what Esc and the cross answer, as
// the default button of every destructive question in the app was No.
public static class Confirm
{
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
