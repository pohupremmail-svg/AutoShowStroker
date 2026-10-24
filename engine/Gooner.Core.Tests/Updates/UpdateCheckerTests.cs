using System.Net;
using System.Text;
using Gooner.Core.Updates;

namespace Gooner.Core.Tests.Updates;

// Port of tests/test_update_checker.py and the parts of tests/test_update_dialogs.py that are not
// a message box: the one request, what is made of its answer, and what the user is told before
// and after. The network is a stub handler - nothing here leaves the machine.
public sealed class UpdateCheckerTests
{
    private static UpdateResult Process(string current, string body) =>
        new UpdateChecker(current).ProcessBody(Encoding.UTF8.GetBytes(body));

    [Fact]
    public void process_body_emits_update_available_when_remote_is_newer() =>
        Assert.Equal(new UpdateAvailable("v0.2.0", "https://example.com/release"),
            Process("0.1.0", """{"tag_name": "v0.2.0", "html_url": "https://example.com/release"}"""));

    [Fact]
    public void process_body_emits_up_to_date_when_remote_is_same() =>
        Assert.IsType<UpToDate>(Process("0.2.0", """{"tag_name": "v0.2.0"}"""));

    [Fact]
    public void process_body_emits_up_to_date_when_remote_is_older() =>
        Assert.IsType<UpToDate>(Process("0.5.0", """{"tag_name": "v0.2.0"}"""));

    // The tag keeps its "v" for display; it is only dropped to compare.
    [Fact]
    public void process_body_strips_v_prefix_from_tag() =>
        Assert.Equal("v9.9.9", Assert.IsType<UpdateAvailable>(Process("0.1.0", """{"tag_name": "v9.9.9"}""")).Tag);

    [Fact]
    public void a_release_without_a_page_still_counts() =>
        Assert.Equal(new UpdateAvailable("v0.2.0", ""), Process("0.1.0", """{"tag_name": "v0.2.0"}"""));

    [Fact]
    public void process_body_emits_check_failed_on_malformed_json() =>
        Assert.Equal(new CheckFailed("Couldn't understand GitHub's response."), Process("0.1.0", "not json"));

    [Fact]
    public void process_body_emits_check_failed_on_missing_tag_name() =>
        Assert.IsType<CheckFailed>(Process("0.1.0", """{"html_url": "https://example.com"}"""));

    [Fact]
    public void process_body_emits_check_failed_on_non_numeric_version_segment() =>
        Assert.IsType<CheckFailed>(Process("0.1.0", """{"tag_name": "v0.2.0-beta"}"""));

    [Fact]
    public void a_running_version_that_does_not_parse_is_a_failed_check() =>
        Assert.IsType<CheckFailed>(Process("unknown", """{"tag_name": "v0.2.0"}"""));

    [Fact]
    public async Task check_now_sends_request_to_releases_api_and_wires_reply()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"tag_name": "v99.0.0", "html_url": "https://example.com/release"}"""),
        });

        var result = await new UpdateChecker("0.1.0", handler).CheckNowAsync();

        Assert.Equal(new Uri(UpdateChecker.ReleasesApiUrl), handler.Request!.RequestUri);
        Assert.Equal(HttpMethod.Get, handler.Request.Method);
        Assert.Equal(new UpdateAvailable("v99.0.0", "https://example.com/release"), result);
    }

    [Fact]
    public async Task check_now_emits_check_failed_on_reply_error()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("Host not found"));

        Assert.Equal(new CheckFailed("Host not found"), await new UpdateChecker("0.1.0", handler).CheckNowAsync());
    }

    [Fact]
    public async Task an_error_status_is_a_failed_check()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        Assert.IsType<CheckFailed>(await new UpdateChecker("0.1.0", handler).CheckNowAsync());
    }

    // --- what the user is told ---

    [Fact]
    public async Task the_request_carries_exactly_that_user_agent()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });

        await new UpdateChecker("0.1.0", handler).CheckNowAsync();

        Assert.Equal([UpdateChecker.UserAgent], handler.Request!.Headers.UserAgent.Select(p => p.ToString()));
    }

    // The promise and the header it describes sit apart; one that has quietly drifted out of date
    // is worse than none, because it is believed.
    [Fact]
    public void consent_text_names_the_user_agent_the_request_actually_sends() =>
        Assert.Contains(UpdateChecker.UserAgent, UpdateTexts.Consent);

    // It used to say nothing else was sent, flat out - but the request carries the IP and a
    // User-Agent that tells GitHub this machine runs the app.
    [Fact]
    public void consent_text_does_not_promise_that_nothing_identifying_is_sent()
    {
        Assert.Contains("IP address", UpdateTexts.Consent);
        Assert.Contains("User-Agent", UpdateTexts.Consent);
    }

    [Fact]
    public void the_available_text_names_both_versions() =>
        Assert.Equal("A new version is available: v0.3.0 (you're on v0.2.0).", UpdateTexts.Available("v0.3.0", "0.2.0"));

    [Fact]
    public void failed_dialog_repeats_the_reason() =>
        Assert.Contains("Host not found", UpdateTexts.Failed("Host not found"));

    [Fact]
    public void up_to_date_dialog_names_the_running_version() =>
        Assert.Contains("0.14.3", UpdateTexts.UpToDate("0.14.3"));

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(answer(request));
        }
    }
}
