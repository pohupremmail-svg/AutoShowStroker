using System;
using System.Collections.Generic;
using System.Linq;
using Gooner.Core.App;
using Gooner.Core.Formatting;
using Gooner.Engine.Panels;
using Godot;

namespace Gooner.Engine;

// The menu bar and the panels it opens: everything the Python window showed as a dialog.
public partial class Main
{
    private AppPanel? _question;

    // The menus of GoonerApp.create_menu_bar, in its order and with its shortcuts.
    private MenuBar BuildMenu()
    {
        var bar = new MenuBar();
        AddMenu(bar, "Settings",
            ("Change Settings", Ctrl(Key.S), () => NotYet("Settings", "3.8")),
            ("Quit", Ctrl(Key.Q), () => Quit(0)));
        AddMenu(bar, "Help",
            ("What's New", Key.None, () => ShowWhatsNew(_app!.ChangelogEntries)),
            ("Guide", Key.F1, () => NotYet("Guide", "3.6")),
            ("Privacy & Data...", Key.None, () => NotYet("Privacy & Data", "3.7")),
            ("Third-Party Licenses...", Key.None, () => NotYet("Third-Party Licenses", "3.6")),
            (null, Key.None, null),
            ("Check for Updates...", Key.None, () => NotYet("Check for Updates", "3.12")));
        AddMenu(bar, "Sessions",
            ("Saved Sessions...", Key.None, () => NotYet("Saved Sessions", "3.10")));
        AddMenu(bar, "Statistics",
            ("Long-term Statistics", Key.None, () => NotYet("Long-term Statistics", "3.4")),
            ("Achievements", Key.None, () => NotYet("Achievements", "3.5")));
        AddMenu(bar, "Socials",
            ("Join Discord", Key.None, () => ExternalUrl.Open(GoonerApp.DiscordInviteUrl, url => OS.ShellOpen(url))));
        return bar;
    }

    private static Key Ctrl(Key key) => (Key)((long)key | (long)KeyModifierMask.MaskCtrl);

    // A null label is a separator.
    private static void AddMenu(MenuBar bar, string title, params (string? Label, Key Accel, Action? Run)[] items)
    {
        var menu = new PopupMenu { Name = title };
        for (var id = 0; id < items.Length; id++)
        {
            if (items[id].Label is { } label) menu.AddItem(label, id, items[id].Accel);
            else menu.AddSeparator();
        }
        menu.IdPressed += id => items[id].Run?.Invoke();
        bar.AddChild(menu);
    }

    // The panels a smoke run can open by name: -- --smoke-panel=<name> --screenshot=<png>.
    private Dictionary<string, Action> SmokePanels() => new()
    {
        ["whatsnew"] = () => ShowWhatsNew(_app!.ChangelogEntries),
    };

    // Once, at startup: what has come since the version last seen.
    private void ShowWhatsNewOnStartup()
    {
        var entries = _app!.WhatsNewOnStartup();
        if (entries.Count > 0) ShowWhatsNew(entries);
    }

    private void ShowWhatsNew(IEnumerable<ChangelogEntry> entries) => _panels.Show(new WhatsNewPanel(entries));

    // Until the dialog behind a menu entry is ported, its entry says which step brings it.
    private void NotYet(string title, string step)
    {
        var panel = new AppPanel(title, new Vector2(420, 0));
        panel.AddText($"Comes with phase 3, step {step}.");
        panel.AddButton("Close", panel.Close, primary: true);
        _panels.Show(panel);
    }

    // --- Stop's question ---

    // Up while the app waits for the answer, and gone once it has one - however it got it. Esc and
    // the cross are "Rather not say", the escape button of the QMessageBox this was.
    private void SyncQuestion()
    {
        var pending = _app!.HowItEndedPending;
        if (pending && _question is null)
        {
            _question = BuildQuestion();
            _panels.Show(_question);
        }
        else if (!pending && _question is not null)
        {
            var question = _question;
            _question = null;
            question.Close();
        }
    }

    private AppPanel BuildQuestion()
    {
        var app = _app!;
        var panel = new AppPanel("How did that end?", new Vector2(520, 0));
        panel.AddText("Before the recap - what actually happened?");
        panel.AddText("Without an answer this session counts as one that never finished, which drags every average you are tracking.", 13, wrap: true);
        foreach (var (label, answer) in new (string, string?)[] { ("I Came", "came"), ("I Ruined It", "ruined"), ("I Stopped", "stopped"), ("Rather not say", null) })
            panel.AddButton(label, () => app.AnswerHowItEnded(answer));
        panel.OnReject = () => app.AnswerHowItEnded(null);
        return panel;
    }

    // --- the recap ---

    // A stand-in for the statistics dialog until step 3.3: the headline numbers and the way to save
    // the session.
    private void ShowRecap()
    {
        var app = _app!;
        var info = app.Score.DeliverInfos();
        var lines = new List<string>
        {
            $"{Clock.FormatClock((double?)info["total_dur_sec"] ?? 0)} played, {info["total_num_beat"]} beats, {info["total_num_pauses"]} pauses",
            $"Climax: {info["climax_outcome"]?.ToString() ?? "none"}   Reported: {info["reported_outcome"]?.ToString() ?? "-"}",
        };
        if (app.Score.LastSessionNewRecords.Count > 0)
            lines.Add("New records: " + string.Join(", ", app.Score.LastSessionNewRecords.Keys));
        if (app.NewAchievements is { Count: > 0 } earned)
            lines.Add("Achievements: " + string.Join(", ", earned.Select(a => a.Name)));

        var panel = new AppPanel("Session over", new Vector2(520, 0));
        panel.AddText(string.Join("\n", lines));
        Button save = null!;
        save = panel.AddButton("Save this session", () =>
        {
            save.Text = app.SaveCurrentSession() ? "Saved" : "Could not save it";
            save.Disabled = true;
        });
        panel.AddButton("Close", panel.Close, primary: true);
        _panels.Show(panel);
    }
}
