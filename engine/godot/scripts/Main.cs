using Gooner.Core.Data;
using Godot;

namespace Gooner.Engine;

// The window. For now only proves the two halves are joined: the scene loads, Gooner.Core is
// reachable, and res/ is found on disk.
//
//   -- --smoke    quits straight away; exit code 0 when res/ was found, 1 when it was not.
//                 What a headless check runs (see engine/CLAUDE.md).
public partial class Main : Control
{
    public override void _Ready()
    {
        var res = AppPaths.Resources;
        if (OS.GetCmdlineUserArgs().Contains("--smoke"))
        {
            GetTree().Quit(res is null ? 1 : 0);
            return;
        }
        AddChild(new Label
        {
            Text = res is null ? "res/ not found." : "GoonerApp",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorRight = 1,
            AnchorBottom = 1,
        });
    }
}

// Where the engine finds what it reads from disk.
public static class AppPaths
{
    // res/ beside the executable once exported; from the editor or a dev run, the repo's own,
    // found by walking up from the Godot project.
    public static string? Resources { get; } = ResourceRoot.Find([
        System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? "",
        ProjectSettings.GlobalizePath("res://"),
    ]);
}
