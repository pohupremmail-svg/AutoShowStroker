using Gooner.Core.App;
using Gooner.Core.Data;
using Gooner.Core.Settings;

namespace Gooner.Core.Tests.App;

// Whether the window is full screen: it is, the first time, and after that it is whatever the user
// left it as - remembered the moment they switch, not at quit, so a crash cannot lose it.
public sealed class WindowStateTests : IDisposable
{
    private readonly DirectoryInfo _tmp = Directory.CreateTempSubdirectory("gooner-window-");

    public void Dispose() => _tmp.Delete(recursive: true);

    private JsonSettingsStore Store() => new(new UserDataStore(_tmp.FullName));

    [Fact]
    public void the_first_start_is_full_screen() => Assert.True(new WindowState(Store()).Fullscreen);

    [Fact]
    public void leaving_full_screen_is_remembered_for_the_next_start()
    {
        new WindowState(Store()).Remember(fullscreen: false);

        Assert.False(new WindowState(Store()).Fullscreen);
    }

    [Fact]
    public void and_so_is_going_back()
    {
        var store = Store();
        var window = new WindowState(store);
        window.Remember(fullscreen: false);
        window.Remember(fullscreen: true);

        Assert.True(window.Fullscreen);
        Assert.True(new WindowState(Store()).Fullscreen);
    }

    [Fact]
    public void without_a_store_it_is_full_screen_and_remembers_in_memory()
    {
        var window = new WindowState(null);
        window.Remember(fullscreen: false);

        Assert.False(window.Fullscreen);
    }
}
