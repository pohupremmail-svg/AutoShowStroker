using System;
using System.Collections.Generic;
using Gooner.Core.App;
using System.Text.Json.Nodes;
using Gooner.Core.Panels;
using Gooner.Core.Sessions;
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
            ("Guide", Key.F1, () => _panels.Show(new GuidePanel())),
            ("Privacy & Data...", Key.None, () => NotYet("Privacy & Data", "3.7")),
            ("Third-Party Licenses...", Key.None, () => _panels.Show(new LicensesPanel(AppPaths.Licenses))),
            (null, Key.None, null),
            ("Check for Updates...", Key.None, () => NotYet("Check for Updates", "3.12")));
        AddMenu(bar, "Sessions",
            ("Saved Sessions...", Key.None, () => NotYet("Saved Sessions", "3.10")));
        AddMenu(bar, "Statistics",
            ("Long-term Statistics", Key.None, ShowLongTermStatistics),
            ("Achievements", Key.None, ShowAchievements));
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
        ["statistics"] = ShowSampleRecap,
        ["longterm"] = ShowLongTermStatistics,
        ["achievements"] = ShowAchievements,
        ["guide"] = () => _panels.Show(new GuidePanel()),
        ["licenses"] = () => _panels.Show(new LicensesPanel(AppPaths.Licenses)),
        ["godot-components"] = () => _panels.Show(new LicensesPanel(AppPaths.Licenses, show: "Godot Engine components")),
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

    private void ShowLongTermStatistics() =>
        _panels.Show(new LongTermStatisticsPanel(new LongTermStatistics(_app!.Score.GetHistory(), _app.Score.GetAllTimeBests())));

    private void ShowAchievements() => _panels.Show(new AchievementsPanel(
        new AchievementsOverview(_app!.Achievements, _app.Score.GetHistory()),
        System.IO.Path.GetDirectoryName(AppPaths.Resources!)!));

    // --- the recap ---

    private void ShowRecap()
    {
        var app = _app!;
        var report = new StatisticsReport(app.Score.DeliverInfos(), app.Score.LastSessionNewRecords,
            app.Recorder.Timeline(), app.NewAchievements);
        _panels.Show(new StatisticsPanel(report, () => NotYet("Session Explorer", "3.10")));
    }

    // A recap with something in every part of it, for --smoke-panel=statistics.
    private void ShowSampleRecap()
    {
        var stats = new JsonObject
        {
            ["total_dur_sec"] = 1325.0, ["pause_dur_sec"] = 140.0, ["average_pause_dur_sec"] = 20.0,
            ["total_num_pauses"] = 7, ["total_num_beat"] = 2150, ["total_num_beat_change"] = 31,
            ["average_beat_speed"] = 1.62, ["average_beat_speed_active"] = 1.81,
            ["most_used_pattern"] = "Standard Beat", ["skips"] = 4, ["repeats"] = 1,
            ["climax_outcome"] = "ruined", ["fakeout_count"] = 2,
        };
        var timeline = new SessionTimeline(0, 1325, 1300, "ruined", [], [new RecordedSegment("beat", "Standard Beat", 2.0, 0, 1325, [])]);
        var report = new StatisticsReport(stats, new Dictionary<string, double> { ["total_dur_sec"] = 1100.0 }, timeline,
            [Gooner.Core.Achievements.AchievementCatalogue.All[0]]);
        _panels.Show(new StatisticsPanel(report, () => NotYet("Session Explorer", "3.10")));
    }
}
