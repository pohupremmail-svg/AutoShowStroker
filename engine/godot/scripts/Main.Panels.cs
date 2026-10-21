using System;
using System.Collections.Generic;
using Gooner.Core.App;
using System.Linq;
using System.Text.Json.Nodes;
using Gooner.Core.Localization;
using Gooner.Core.Media;
using Gooner.Core.Panels;
using Gooner.Core.Sessions;
using Gooner.Core.Updates;
using Gooner.Engine.Panels;
using Gooner.Engine.Ui;
using Godot;

namespace Gooner.Engine;

// The start screen, the dock's menu and the panels they open: everything the Python window showed
// as a menu or a dialog.
public partial class Main
{
    private AppPanel? _question;

    // What the menu bar held, now on the start screen - the big cards for what one comes to do, every
    // other way in along its foot - and, during a session, in the dock's menu.
    private StartScreen BuildStart()
    {
        var app = _app!;
        var cards = new List<StartCard>
        {
            new(L10n.T("Start Session"), "play", () => L10n.T("Pick your folders, lean back"), () => OpenFolder(), Primary: true),
            new(L10n.T("Saved Sessions"), "saved", SavedCount, ShowSavedSessions),
            new(L10n.T("Achievements"), "trophy",
                () => L10n.F("{0} of {1} unlocked", app.Achievements.UnlockedIds.Count, Gooner.Core.Achievements.AchievementCatalogue.All.Count), ShowAchievements),
            new(L10n.T("Statistics"), "stats", () => L10n.T("Every session so far"), ShowLongTermStatistics),
            new(L10n.T("Settings"), "settings", () => L10n.T("Rhythm, climax, callouts, look"), ShowSettings),
            new(L10n.T("Exit"), "power", () => null, () => Quit(0)),
        };
        // Under the name, a line in the voice the app will speak in - a new one when that changes.
        var start = new StartScreen(AppPaths.Resources, app.Character.Path, cards, MenuEntries(), app.Version, () => app.Callouts?.TitleLine());
        if (app.Callouts is { } callouts)
        {
            callouts.VoiceChanged += start.RefreshLine;
            // A start screen built again in another language must not be asked for a line once gone.
            start.TreeExiting += () => callouts.VoiceChanged -= start.RefreshLine;
        }
        return start;
    }

    private string? SavedCount()
    {
        if (_app!.DataStore is not { } store) return null;
        var saved = new SavedSessionsShelf(store, (_, _) => false).Rows.Count;
        return saved == 0 ? L10n.T("None saved yet") : L10n.F("{0} saved", saved);
    }

    // The rest of the old menus, in their order. Quitting is the start screen's Exit card and the
    // last entry of the dock's menu.
    private List<(string Label, Action Pressed)> MenuEntries() =>
    [
        (L10n.T("What's New"), () => ShowWhatsNew(_app!.ChangelogEntries)),
        (L10n.T("Guide"), () => _panels.Show(new GuidePanel())),
        (L10n.T("Privacy & Data"), ShowPrivacy),
        (L10n.T("Third-Party Licenses"), () => _panels.Show(new LicensesPanel(AppPaths.Licenses))),
        (L10n.T("Check for Updates"), CheckForUpdates),
        (L10n.T("Join Discord"), () => ExternalUrl.Open(GoonerApp.DiscordInviteUrl, url => OS.ShellOpen(url))),
    ];

    // The dock's menu: everything the start screen offers, without leaving the session.
    private void ShowMenu()
    {
        var panel = new AppPanel(L10n.T("Menu"), new Vector2(360, 0));
        var entries = new List<(string Label, Action Pressed)>
        {
            (L10n.T("Settings"), ShowSettings),
            (L10n.T("Saved Sessions"), ShowSavedSessions),
            (L10n.T("Long-term Statistics"), ShowLongTermStatistics),
            (L10n.T("Achievements"), ShowAchievements),
        };
        entries.AddRange(MenuEntries());
        entries.Add((L10n.T("Quit"), () => Quit(0)));
        foreach (var (label, pressed) in entries)
        {
            var button = new Button { Text = label, FocusMode = Control.FocusModeEnum.None, Alignment = HorizontalAlignment.Left };
            button.Pressed += () =>
            {
                panel.Close();
                pressed();
            };
            panel.Body.AddChild(button);
        }
        _panels.Show(panel);
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
        ["settings-device"] = () => ShowSettings(Gooner.Core.Devices.IntifacePlugin.TabTitle),
        ["settings-look"] = () => ShowSettings("Look"),
        ["menu"] = ShowMenu,
        // Nothing over it: the start screen alone.
        ["start"] = () => { },
        ["phrasefiles"] = ShowPhraseFiles,
        ["patterns"] = () => ShowPatternEditor(() => { }),
        ["saved"] = ShowSavedSessions,
        ["updates"] = CheckForUpdates,
        // An exception nobody catches, thrown from a deferred call as a real one would be.
        ["crash"] = () => Callable.From(() => throw new InvalidOperationException("a smoke-run crash")).CallDeferred(),
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
        var panel = new AppPanel(L10n.T("How did that end?"), new Vector2(520, 0));
        panel.AddText(L10n.T("Before the recap - what actually happened?"));
        panel.AddText(L10n.T("Without an answer this session counts as one that never finished, which drags every average you are tracking."), 13, wrap: true);
        foreach (var (label, answer) in new (string, string?)[] { (L10n.T("I Came"), "came"), (L10n.T("I Ruined It"), "ruined"), (L10n.T("I Stopped"), "stopped"), (L10n.T("Rather not say"), null) })
            panel.AddButton(label, () => app.AnswerHowItEnded(answer));
        panel.OnReject = () => app.AnswerHowItEnded(null);
        return panel;
    }

    private void ShowLongTermStatistics() =>
        _panels.Show(new LongTermStatisticsPanel(new LongTermStatistics(_app!.Score.GetHistory(), _app.Score.GetAllTimeBests())));

    private void ShowAchievements() => _panels.Show(new AchievementsPanel(
        new AchievementsOverview(_app!.Achievements, _app.Score.GetHistory()),
        System.IO.Path.GetDirectoryName(AppPaths.Resources!)!));

    private void ShowPrivacy() => _panels.Show(new PrivacyPanel(new PrivacyData(_app!, OS.GetUserDataDir()), _panels));

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

    // --- crashes ---

    // Nothing to show it on before the window is built; the report has been logged regardless.
    private void ShowCrash(string details)
    {
        if (_panels is null) return;
        var panel = new CrashPanel(details);
        panel.Closed += () => _crashes?.Dismissed();
        _panels.Show(panel);
    }

    // --- updates ---

    // The question first, defaulting to No; then the one request, and what came of it.
    private void CheckForUpdates() =>
        Confirm.Ask(_panels, L10n.T("Check for Updates?"), UpdateTexts.Consent, RunUpdateCheck);

    private async void RunUpdateCheck()
    {
        var version = _app!.Version;
        var result = await new UpdateChecker(version).CheckNowAsync();
        switch (result)
        {
            case UpdateAvailable available:
                var panel = new AppPanel(L10n.T("Update Available"), new Vector2(460, 0));
                panel.AddText(UpdateTexts.Available(available.Tag, version), 14, wrap: true);
                panel.AddButton(L10n.T("Open Releases Page"), () =>
                {
                    panel.Close();
                    ExternalUrl.Open(available.Url, url => OS.ShellOpen(url));
                }, primary: true);
                panel.AddButton(L10n.T("Close"), panel.Close);
                _panels.Show(panel);
                break;
            case UpToDate:
                Confirm.Tell(_panels, L10n.T("Up to Date"), UpdateTexts.UpToDate(version));
                break;
            case CheckFailed failed:
                Confirm.Tell(_panels, L10n.T("Update Check Failed"), UpdateTexts.Failed(failed.Message));
                break;
        }
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
