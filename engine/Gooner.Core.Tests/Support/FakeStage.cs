using Gooner.Core.Media;

namespace Gooner.Core.Tests.Support;

// A stage that only writes down what it was asked to show.
internal sealed class FakeStage : IMediaStage
{
    public List<string> Calls { get; } = [];

    public void ShowImage(string path) => Calls.Add("image " + path);

    public void ShowGif(string path) => Calls.Add("gif " + path);

    public void PlayVideo(string path, double volume) => Calls.Add("video " + path);

    public void ReplayVideo() => Calls.Add("replay");

    public void ShowMessage(string text) => Calls.Add("message " + text);

    public void StopAll() => Calls.Add("stop");

    public void SetMuted(bool muted) => Calls.Add("muted " + muted);
}
