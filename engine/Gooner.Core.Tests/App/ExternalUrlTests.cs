using Gooner.Core.App;

namespace Gooner.Core.Tests.App;

// Port of the open_external_url tests in tests/test_utils.py.
public sealed class ExternalUrlTests
{
    private readonly List<string> _opened = [];

    // The one route out of the app to a web page, and the reason it is a route rather than a bare
    // call: a release URL comes straight from the GitHub API response, and anything but http(s)
    // would hand an arbitrary protocol handler to the shell on a single click.
    [Fact]
    public void a_foreign_scheme_url_is_not_opened()
    {
        ExternalUrl.Open("file:///C:/Windows/System32/calc.exe", _opened.Add);

        Assert.Empty(_opened);
    }

    [Fact]
    public void an_https_url_is_opened()
    {
        ExternalUrl.Open("https://github.com/owner/repo/releases", _opened.Add);

        Assert.Equal(["https://github.com/owner/repo/releases"], _opened);
    }

    [Theory]
    [InlineData("http://example.org/")]
    [InlineData("HTTPS://example.org/")]
    public void http_and_any_case_of_the_scheme_are_web(string url)
    {
        ExternalUrl.Open(url, _opened.Add);

        Assert.Single(_opened);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:privacy")]
    public void anything_else_is_refused(string url)
    {
        ExternalUrl.Open(url, _opened.Add);

        Assert.Empty(_opened);
    }
}
