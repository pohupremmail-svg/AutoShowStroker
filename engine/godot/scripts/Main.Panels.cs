using System;
using System.Collections.Generic;
using Gooner.Core.App;
using System.Linq;
using System.Text.Json.Nodes;
using Gooner.Core.Media;
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
            ("Change Settings", Ctrl(Key.S), ShowSettings),
            ("Quit", Ctrl(Key.Q), () => Quit(0)));
        AddMenu(bar, "Help",
            ("What's New", Key.None, () => ShowWhatsNew(_app!.ChangelogEntries)),
            ("Guide", Key.F1, () => _panels.Show(new GuidePanel())),
            ("Privacy & Data...", Key.None, ShowPrivacy),
            ("Third-Party Licenses...", Key.None, () => _panels.Show(new LicensesPanel(AppPaths.Licenses))),
            (null, Key.None, null),
            ("Check for Updates...", Key.None, () => NotYet("Check for Updates", "3.12")));
        AddMenu(bar, "Sessions",
            ("Saved Sessions...", Key.None, ShowSavedSessions));
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
        ["privacy"] = ShowPrivacy,
        ["settings"] = ShowSettings,
        ["settings-beat"] = () => ShowSettings("Beat & Rhythm"),
        ["settings-callouts"] = () => ShowSettings("Callouts"),
        ["phrasefiles"] = ShowPhraseFiles,
        ["patterns"] = () => ShowPatternEditor(() => { }),
        ["saved"] = ShowSavedSessions,
        ["picker"] = () => OpenFolder(AppPaths.UserArg("media") is { } media ? [media] : null),
        ["explorer"] = ShowSampleExplorer,
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

    private void ShowPrivacy() => _panels.Show(new PrivacyPanel(new PrivacyData(_app!), _panels));

    private void ShowSettings() => ShowSettings(null);

    private void ShowSettings(string? tab)
    {
        var panel = new SettingsPanel(new SettingsForm(_app!), _panels, ShowPatternEditor, ShowPhraseFiles);
        _panels.Show(panel);
        if (tab is not null) panel.ShowTab(tab);
    }

    // The rhythms the editor saved or deleted show in Settings once it closes.
    private void ShowPatternEditor(Action closed)
    {
        var panel = new PatternEditorPanel(new PatternEditor(_app!.Beat), () => _audio?.Click?.PlayNow());
        panel.Closed += closed;
        _panels.Show(panel);
    }

    private void ShowPhraseFiles()
    {
        if (_app!.Callouts is { } callouts) _panels.Show(new PhraseFilesPanel(new CustomPhraseFiles(callouts)));
    }

    // --- the recap ---

    private void ShowRecap()
    {
        var app = _app!;
        var report = new StatisticsReport(app.Score.DeliverInfos(), app.Score.LastSessionNewRecords,
            app.Recorder.Timeline(), app.NewAchievements);
        _panels.Show(new StatisticsPanel(report, () => ShowExplorer(app.Recorder.Timeline(), app.SaveCurrentSession)));
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
        _panels.Show(new StatisticsPanel(report, ShowSampleExplorer));
    }

    // The explorer is handed the timeline and the way to save it; it keeps neither.
    private void ShowExplorer(SessionTimeline timeline, Func<bool>? save) =>
        _panels.Show(new SessionExplorerPanel(new SessionExplorer(timeline, save), _panels, _app!.Scheduler));

    // A session over the smoke run's --media folder, for --smoke-panel=explorer: every file shown
    // for ten seconds, a pause in the middle, a fake-out and a ruined climax.
    private void ShowSampleExplorer()
    {
        var files = AppPaths.UserArg("media") is { } media ? MediaKinds.FindSupportedFiles(media) : [];
        var shown = files.Select((path, i) => new RecordedMedium(path, i * 10.0, i * 10.0 + 10.0, false)).ToList();
        var end = Math.Max(60.0, shown.Count * 10.0);
        var timeline = new SessionTimeline(0, end, end - 5, "ruined", [end * 0.3],
        [
            new RecordedSegment("beat", "Standard Beat", 2.0, 0, end * 0.45, [.. shown.Where(m => m.Start < end * 0.45)]),
            new RecordedSegment("pause", null, null, end * 0.45, end * 0.55, []),
            new RecordedSegment("finale", "Quick Swing", 4.5, end * 0.55, end, [.. shown.Where(m => m.Start >= end * 0.45)]),
        ]);
        var panel = new SessionExplorerPanel(new SessionExplorer(timeline, () => true), _panels, _app!.Scheduler);
        _panels.Show(panel);
        var scrub = AppPaths.UserArg("scrub") is { } at ? double.Parse(at, System.Globalization.CultureInfo.InvariantCulture) : end * 0.2;
        panel.ScrubForSmoke(scrub);
    }

    private void ShowSavedSessions()
    {
        var app = _app!;
        if (app.DataStore is not { } store) return;
        _panels.Show(new SavedSessionsPanel(new SavedSessionsShelf(store, (session, ignore) => app.ReplaySession(session, ignore)), _panels));
    }
}
