namespace Gooner.Core.Data;

// Folders that should not outlive their use when nothing is in them.
//
// The case it exists for: Godot makes its user:// folder at every start -
// %APPDATA%\Godot\app_userdata\GoonerApp, the parents with it - before any of the app's code runs,
// and whether or not anything is ever written there. The engine writes nothing there (no file log,
// no shader or pipeline cache), but the folder's name alone says this app ran on this machine.
public static class EmptyFolders
{
    // Removes `path` if it is empty, then each of up to `parents` folders above it while that is
    // empty too; how many went. Never throws: a folder that cannot go now is tried again next time.
    public static int RemoveWithParents(string path, int parents)
    {
        if (string.IsNullOrEmpty(path)) return 0;
        var removed = 0;
        for (var dir = new DirectoryInfo(path); dir is not null && removed <= parents; dir = dir.Parent)
        {
            try
            {
                if (!dir.Exists || dir.EnumerateFileSystemInfos().Any()) break;
                dir.Delete();
                removed++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                break;
            }
        }
        return removed;
    }
}
